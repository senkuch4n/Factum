using System.Security.Cryptography;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services;

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
}
