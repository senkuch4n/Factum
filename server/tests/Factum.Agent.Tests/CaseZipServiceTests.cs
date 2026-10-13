using System.Security.Cryptography;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services;
using Factum.Agent.Services.Evidence;

namespace Factum.Agent.Tests;

/// <summary>
/// ZIP del caso en Tatana (zip-local-informe-servidor §6.3, T7). DataDirectory y
/// EvidenceDirectory de prueba propios (Path.GetTempPath()/&lt;guid&gt;), nunca la carpeta default
/// (~/Factum/Evidencia, C:\Factum\Evidencia) ni agent-data del repo.
/// </summary>
public sealed class CaseZipServiceTests : IDisposable
{
    private const string CaseId = "3f2a9c1e-0000-4000-8000-000000000001";
    private const string CaseRef = "1234/2026";
    private const string ZipName = "evidencia_1234_2026_Titular.zip";
    private const string Password = "ABCDEFGH23456789";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-agent-zip-" + Guid.NewGuid().ToString("N"));
    private readonly CaseEvidenceStore _store;
    private readonly CaseZipService _zips;

    public CaseZipServiceTests()
    {
        var opts = new AgentOptions
        {
            DataDirectory = Path.Combine(_root, "agent-data"),
            EvidenceDirectory = Path.Combine(_root, "Evidencia"),
            MinFreeBytes = 0,
        };
        _store = new CaseEvidenceStore(opts);
        _zips = new CaseZipService(opts, _store);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string CaseDir => _store.CaseDir(CaseId);
    private string Final => Path.Combine(_root, "Evidencia", "1234_2026_3f2a9c1e");
    private string PendingDir => Path.Combine(Final, ".factum", "pendiente");

    private List<ZipFileItem> Seed()
    {
        Directory.CreateDirectory(CaseDir);
        var items = new List<ZipFileItem>();
        foreach (var (name, size) in new[] { ("screenshot_1.png", 4096), ("grabacion_1.mkv", 200_000), ("vacio.txt", 0) })
        {
            var data = RandomNumberGenerator.GetBytes(size);
            File.WriteAllBytes(Path.Combine(CaseDir, name), data);
            items.Add(new ZipFileItem(name, size, Convert.ToHexStringLower(SHA256.HashData(data))));
        }
        return items;
    }

    private BuildZipRequest Request(List<ZipFileItem> files, string? password = Password) =>
        new(CaseRef, ZipName, password, files);

    [Fact]
    public async Task Build_WritesVerifiedPending_WithHash()
    {
        var files = Seed();
        var res = await _zips.BuildAsync(CaseId, Request(files), default);

        var pending = Path.Combine(PendingDir, ZipName);
        Assert.True(File.Exists(pending));
        Assert.False(File.Exists(pending + ".part"));
        Assert.False(File.Exists(Path.Combine(Final, ZipName))); // el final recién en el commit
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(pending))), res.ZipHash);
        Assert.Equal(res.ZipHash, File.ReadAllText(pending + ".sha256"));
        Assert.Equal(Path.Combine(Final, ZipName), res.ZipPath);
        Assert.Equal(Final, res.Directory);
        Assert.True(res.Encrypted);
        Assert.Equal("aes256-ae2", res.Encryption);
        Assert.Equal(Environment.MachineName, res.Hostname);
        Assert.Equal(3, Directory.GetFiles(CaseDir).Length); // los sueltos siguen

        var status = _zips.Status(CaseId, CaseRef, ZipName);
        Assert.Equal(("pending", res.ZipHash), (status.State, status.PendingHash));
    }

    [Fact]
    public async Task Build_Plain_WhenNoPassword()
    {
        var res = await _zips.BuildAsync(CaseId, Request(Seed(), password: null), default);
        Assert.False(res.Encrypted);
        Assert.Null(res.Encryption);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("missing")]
    public async Task Build_EvidenceChanged(string reason)
    {
        var files = Seed();
        var target = files[0];
        switch (reason)
        {
            case "hash": files[0] = target with { Sha256 = new string('0', 64) }; break;
            case "size": files[0] = target with { Size = target.Size + 1 }; break;
            case "missing": File.Delete(Path.Combine(CaseDir, target.Filename!)); break;
        }

        var ex = await Assert.ThrowsAsync<AgentHttpException>(() => _zips.BuildAsync(CaseId, Request(files), default));
        Assert.Equal((409, AgentErrorCodes.EvidenceChanged), (ex.Status, ex.Code));
        var changes = Assert.IsType<List<EvidenceChange>>(ex.Extra!["files"]);
        Assert.Equal([new EvidenceChange(target.Filename!, reason)], changes);
        Assert.False(File.Exists(Path.Combine(PendingDir, ZipName)));
    }

    [Fact]
    public async Task Build_IgnoresFilesOutsideManifest()
    {
        var files = Seed();
        File.WriteAllText(Path.Combine(CaseDir, "extra.txt"), "no va");
        await _zips.BuildAsync(CaseId, Request(files), default);
        var commit = await _zips.CommitAsync(CaseId, new CommitZipRequest(CaseRef, ZipName,
            File.ReadAllText(Path.Combine(PendingDir, ZipName + ".sha256")), files.Select(f => f.Filename!).ToList()), default);
        Assert.Equal(3, commit.Deleted);
        Assert.Equal(["extra.txt"], Directory.GetFiles(CaseDir).Select(Path.GetFileName)); // no se borra
    }

    [Fact]
    public async Task Build_InvalidZipName()
    {
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _zips.BuildAsync(CaseId, new BuildZipRequest(CaseRef, "../x.zip", null, Seed()), default));
        Assert.Equal((400, AgentErrorCodes.InvalidFilename), (ex.Status, ex.Code));
    }

    [Fact]
    public async Task Commit_MovesZip_DeletesLoose_AndIsIdempotent()
    {
        var files = Seed();
        var built = await _zips.BuildAsync(CaseId, Request(files), default);
        var names = files.Select(f => f.Filename!).ToList();

        var commit = await _zips.CommitAsync(CaseId, new CommitZipRequest(CaseRef, ZipName, built.ZipHash, names), default);
        var final = Path.Combine(Final, ZipName);
        Assert.Equal(final, commit.ZipPath);
        Assert.Equal(3, commit.Deleted);
        Assert.True(File.Exists(final));
        Assert.Equal(built.ZipHash, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(final))));
        Assert.False(File.Exists(Path.Combine(PendingDir, ZipName)));
        Assert.False(File.Exists(Path.Combine(PendingDir, ZipName + ".sha256")));
        Assert.False(Directory.Exists(CaseDir)); // quedó vacía → se borra
        Assert.True(File.Exists(Path.Combine(Final, ".factum", "estado.json")));

        var again = await _zips.CommitAsync(CaseId, new CommitZipRequest(CaseRef, ZipName, built.ZipHash, names), default);
        Assert.Equal((final, 0), (again.ZipPath, again.Deleted));

        var status = _zips.Status(CaseId, CaseRef, ZipName);
        Assert.Equal(("final", built.ZipHash), (status.State, status.CommittedHash));

        // Ya confirmado desde esta PC: no se arma otro.
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() => _zips.BuildAsync(CaseId, Request(files), default));
        Assert.Equal(AgentErrorCodes.ZipAlreadyCommitted, ex.Code);
    }

    [Fact]
    public async Task Commit_WrongHash_MovesNothing()
    {
        var files = Seed();
        await _zips.BuildAsync(CaseId, Request(files), default);
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() => _zips.CommitAsync(CaseId,
            new CommitZipRequest(CaseRef, ZipName, new string('a', 64), files.Select(f => f.Filename!).ToList()), default));
        Assert.Equal((409, AgentErrorCodes.ZipHashMismatch), (ex.Status, ex.Code));
        Assert.True(File.Exists(Path.Combine(PendingDir, ZipName)));
        Assert.False(File.Exists(Path.Combine(Final, ZipName)));
        Assert.Equal(3, Directory.GetFiles(CaseDir).Length);
    }

    [Fact]
    public async Task Commit_WithoutPending_IsNotFound()
    {
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _zips.CommitAsync(CaseId, new CommitZipRequest(CaseRef, ZipName, new string('a', 64), null), default));
        Assert.Equal((404, AgentErrorCodes.ZipNotFound), (ex.Status, ex.Code));
    }

    [Fact]
    public async Task Discard_RemovesPending_KeepsLoose()
    {
        var files = Seed();
        await _zips.BuildAsync(CaseId, Request(files), default);
        _zips.DiscardPending(CaseId, CaseRef, ZipName);
        Assert.Empty(Directory.GetFiles(PendingDir));
        Assert.Equal(3, Directory.GetFiles(CaseDir).Length);
        Assert.Equal("none", _zips.Status(CaseId, CaseRef, ZipName).State);
        _zips.DiscardPending(CaseId, CaseRef, ZipName); // sin nada: no falla
        Assert.Null(_zips.FinalZipPath(CaseId, CaseRef, ZipName));
    }

    [Fact]
    public async Task LooseFiles_OnlyDeletedByCommit()
    {
        var files = Seed();
        await _zips.BuildAsync(CaseId, Request(files), default);
        Assert.Equal(3, Directory.GetFiles(CaseDir).Length);
        _zips.DiscardPending(CaseId, CaseRef, ZipName);
        Assert.Equal(3, Directory.GetFiles(CaseDir).Length);
    }

    // ── zip-verificacion-stored D4: texto de zip_failed ──────────────────────

    [Fact]
    public void ZipFailedMessage_Verification_IsD4Text()
    {
        var msg = CaseZipService.ZipFailedMessage(
            new EvidenceZipVerificationException("No se pudo verificar el ZIP de evidencia: x.png no se puede leer"));
        Assert.Equal(
            "La evidencia está intacta, pero el ZIP armado no pasó el control de integridad. Si se repite, contactá a soporte.",
            msg);
        Assert.DoesNotContain("Reintentá", msg);
        Assert.DoesNotContain("borr", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ZipFailedMessage_Other_IsGeneric()
    {
        Assert.Equal("No se pudo armar o verificar el ZIP en esta PC; la evidencia quedó intacta",
            CaseZipService.ZipFailedMessage(new IOException("disco")));
    }

    // ── tatana-ws-y-hosts: bajas del QA ──────────────────────────────────────

    private sealed class FixedProbe(long? free) : IAgentDiskProbe
    {
        public List<string> Paths { get; } = [];
        public long? GetAvailableFreeBytes(string path)
        {
            Paths.Add(path);
            return free;
        }
    }

    private string EvidenceRoot => Path.Combine(_root, "Evidencia");

    // T-08
    [Fact]
    public async Task Build_EmptyPassword_Is400_AndCreatesNothing()
    {
        var files = Seed();
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() => _zips.BuildAsync(CaseId, Request(files, ""), default));
        Assert.Equal((400, AgentErrorCodes.InvalidPassword), (ex.Status, ex.Code));
        Assert.Equal("La contraseña del ZIP no puede estar vacía", ex.Message);
        Assert.False(Directory.Exists(EvidenceRoot));
    }

    // T-09
    [Fact]
    public async Task Build_PasswordOver99Bytes_Is400()
    {
        var pw = new string('ñ', 50); // 100 bytes UTF-8
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() => _zips.BuildAsync(CaseId, Request(Seed(), pw), default));
        Assert.Equal((400, AgentErrorCodes.InvalidPassword), (ex.Status, ex.Code));
        Assert.DoesNotContain(pw, ex.Message);
        Assert.False(Directory.Exists(EvidenceRoot));
    }

    [Fact]
    public async Task Build_Password99AsciiBytes_Ok()
    {
        var res = await _zips.BuildAsync(CaseId, Request(Seed(), new string('a', 99)), default);
        Assert.True(res.Encrypted);
    }

    [Fact]
    public async Task Build_SpacePassword_Ok()
    {
        var res = await _zips.BuildAsync(CaseId, Request(Seed(), " "), default);
        Assert.True(res.Encrypted);
    }

    // T-06
    [Fact]
    public async Task Build_EvidenceChanged_LeavesNoFolders()
    {
        var files = Seed();
        File.Delete(Path.Combine(CaseDir, files[0].Filename!));
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() => _zips.BuildAsync(CaseId, Request(files), default));
        Assert.Equal(409, ex.Status);
        Assert.False(Directory.Exists(EvidenceRoot));
    }

    [Fact]
    public async Task Build_InsufficientStorage_Prechecks_OnExistingAncestor_AndLeavesNoFolders()
    {
        var probe = new FixedProbe(10);
        var opts = new AgentOptions
        {
            DataDirectory = Path.Combine(_root, "agent-data"),
            EvidenceDirectory = EvidenceRoot,
            MinFreeBytes = 0,
        };
        var zips = new CaseZipService(opts, _store, probe: probe);
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() => zips.BuildAsync(CaseId, Request(Seed()), default));
        Assert.Equal((507, AgentErrorCodes.InsufficientStorage), (ex.Status, ex.Code));
        Assert.False(Directory.Exists(EvidenceRoot));
        // Se midió sobre una carpeta que existe (el prechequeo no queda muerto en el primer ZIP del caso).
        Assert.All(probe.Paths, p => Assert.True(Directory.Exists(p), p));
    }

    [Fact]
    public async Task Build_FailureWhileZipping_RemovesOnlyFoldersItCreated()
    {
        // Un "archivo" que es una carpeta pasa el paso 4 (manifiesto) solo si existe como archivo; en su lugar se
        // fuerza la falla con un ZIP cuyo .part no se puede crear: la carpeta pendiente es un archivo.
        var files = Seed();
        Directory.CreateDirectory(Final);
        File.WriteAllText(Path.Combine(Final, "otro.txt"), "del perito");
        Directory.CreateDirectory(Path.Combine(Final, ".factum"));
        File.WriteAllText(PendingDir, "no es carpeta");

        var ex = await Assert.ThrowsAsync<AgentHttpException>(() => _zips.BuildAsync(CaseId, Request(files), default));
        Assert.Equal((500, AgentErrorCodes.StorageError), (ex.Status, ex.Code));
        Assert.True(File.Exists(Path.Combine(Final, "otro.txt"))); // lo preexistente queda
        Assert.True(Directory.Exists(Path.Combine(Final, ".factum")));
    }

    [Fact]
    public void ExistingAncestor_SubeHastaUnoQueExiste()
    {
        Directory.CreateDirectory(_root);
        Assert.Equal(Path.GetFullPath(_root), CaseZipService.ExistingAncestor(Path.Combine(_root, "a", "b", "c")));
        Assert.Equal(Path.GetFullPath(_root), CaseZipService.ExistingAncestor(_root));
    }

    // T-07
    [Fact]
    public void ClassifyBuildFailure_MissingFile_Is409()
    {
        Directory.CreateDirectory(_root);
        var present = Path.Combine(_root, "ok.png");
        File.WriteAllText(present, "x");
        var gone = Path.Combine(_root, "borrado.png");
        var ex = CaseZipService.ClassifyBuildFailure(new FileNotFoundException("x", gone), [present, gone], () => 1000, 10, 0);
        Assert.Equal((409, AgentErrorCodes.EvidenceChanged), (ex.Status, ex.Code));
        var changes = Assert.IsType<List<EvidenceChange>>(ex.Extra!["files"]);
        Assert.Equal([new EvidenceChange("borrado.png", "missing")], changes);
    }

    [Fact]
    public void ClassifyBuildFailure_DiskFull_Is507()
    {
        var io = new IOException("No space left on device", 28);
        var ex = CaseZipService.ClassifyBuildFailure(io, [], () => 1234, 5000, 100);
        Assert.Equal((507, AgentErrorCodes.InsufficientStorage), (ex.Status, ex.Code));
        Assert.Equal(5100L, ex.Extra!["required_bytes"]);
        Assert.Equal(1234L, ex.Extra!["available_bytes"]);
        Assert.StartsWith("No hay espacio en el disco de esta PC para guardar el ZIP", ex.Message);
    }

    [Fact]
    public void ClassifyBuildFailure_IoWithLowFreeSpace_Is507()
    {
        var ex = CaseZipService.ClassifyBuildFailure(new IOException("write"), [], () => 10, 5000, 0);
        Assert.Equal(507, ex.Status);
    }

    [Fact]
    public void ClassifyBuildFailure_Other_Is500()
    {
        var ex = CaseZipService.ClassifyBuildFailure(new InvalidOperationException("x"), [], () => 10, 5000, 0);
        Assert.Equal((500, AgentErrorCodes.ZipFailed), (ex.Status, ex.Code));
        var io = CaseZipService.ClassifyBuildFailure(new IOException("x"), [], () => 1_000_000, 5000, 0);
        Assert.Equal((500, AgentErrorCodes.ZipFailed), (io.Status, io.Code));
        // Archivo "faltante" que en realidad sigue ahí → no es evidence_changed.
        var fnf = CaseZipService.ClassifyBuildFailure(new FileNotFoundException("x"), [], () => null, 5000, 0);
        Assert.Equal(500, fnf.Status);
    }

    // T-05
    [Fact]
    public void CleanupOrphanParts_OnlyZipParts()
    {
        var pending = Path.Combine(EvidenceRoot, "X_11111111", ".factum", "pendiente");
        Directory.CreateDirectory(pending);
        foreach (var f in new[] { "evidencia_X.zip.part", "evidencia_X.zip", "evidencia_X.zip.sha256", "otro.part" })
            File.WriteAllText(Path.Combine(pending, f), "x");
        Directory.CreateDirectory(Path.Combine(EvidenceRoot, "vacia_22222222", ".factum", "pendiente"));
        File.WriteAllText(Path.Combine(EvidenceRoot, "X_11111111", "evidencia_X.zip.part"), "fuera de pendiente");

        Assert.Equal(1, _zips.CleanupOrphanParts());

        Assert.Equal(["evidencia_X.zip", "evidencia_X.zip.sha256", "otro.part"],
            Directory.GetFiles(pending).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.True(File.Exists(Path.Combine(EvidenceRoot, "X_11111111", "evidencia_X.zip.part")));
        Assert.True(Directory.Exists(Path.Combine(EvidenceRoot, "vacia_22222222", ".factum", "pendiente")));
    }

    [Fact]
    public void CleanupOrphanParts_WithoutEvidenceDirectory_IsZero()
    {
        Assert.Equal(0, _zips.CleanupOrphanParts());
        Assert.False(Directory.Exists(EvidenceRoot));
    }
}
