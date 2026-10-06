using System.Security.Cryptography;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services;

namespace Factum.Agent.Tests;

/// <summary>
/// Carpeta de trabajo del caso (zip-local-informe-servidor §6.2, T4). Todo en un DataDirectory
/// temporal propio, nunca en server/src/Factum.Agent/agent-data.
/// </summary>
public sealed class CaseEvidenceStoreTests : IDisposable
{
    private const string CaseId = "3f2a9c1e-0000-4000-8000-000000000001";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-agent-store-" + Guid.NewGuid().ToString("N"));

    private sealed class FixedProbe(long? free) : IAgentDiskProbe
    {
        public long? GetAvailableFreeBytes(string path) => free;
    }

    private CaseEvidenceStore NewStore(long max = 1024 * 1024, long minFree = 0, long? free = null) =>
        new(new AgentOptions { DataDirectory = _root, MaxUploadBytes = max, MinFreeBytes = minFree }, new FixedProbe(free));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string CaseDir => Path.Combine(_root, "cases", CaseId);

    private static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    // ── Import ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_MovesFromRoot_AndIsIdempotent()
    {
        var store = NewStore();
        var data = RandomNumberGenerator.GetBytes(5000);
        File.WriteAllBytes(Path.Combine(_root, "screenshot_1.png"), data);

        var info = await store.ImportAsync(CaseId, "screenshot_1.png", default);
        Assert.Equal(Sha(data), info.Sha256);
        Assert.Equal(5000, info.Size);
        Assert.False(File.Exists(Path.Combine(_root, "screenshot_1.png")));
        Assert.True(File.Exists(Path.Combine(CaseDir, "screenshot_1.png")));

        var again = await store.ImportAsync(CaseId, "screenshot_1.png", default);
        Assert.Equal(info.Sha256, again.Sha256);
    }

    [Fact]
    public async Task Import_UppercaseId_UsesCanonicalFolder()
    {
        var store = NewStore();
        File.WriteAllBytes(Path.Combine(_root, "a.png"), [1, 2, 3]);
        await store.ImportAsync(CaseId.ToUpperInvariant(), "a.png", default);
        Assert.True(File.Exists(Path.Combine(CaseDir, "a.png")));
    }

    [Fact]
    public async Task Import_ConflictAndNotFound()
    {
        var store = NewStore();
        Directory.CreateDirectory(CaseDir);
        File.WriteAllBytes(Path.Combine(_root, "a.png"), [1]);
        File.WriteAllBytes(Path.Combine(CaseDir, "a.png"), [2]);
        var conflict = await Assert.ThrowsAsync<AgentHttpException>(() => store.ImportAsync(CaseId, "a.png", default));
        Assert.Equal((409, AgentErrorCodes.FileExists), (conflict.Status, conflict.Code));
        Assert.True(File.Exists(Path.Combine(_root, "a.png"))); // no se movió nada

        var missing = await Assert.ThrowsAsync<AgentHttpException>(() => store.ImportAsync(CaseId, "b.png", default));
        Assert.Equal((404, AgentErrorCodes.FileNotFound), (missing.Status, missing.Code));
    }

    [Theory]
    [InlineData("../a.png")]
    [InlineData("sub/a.png")]
    [InlineData("evidencia_x.zip")]
    public async Task Import_InvalidName(string name)
    {
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() => NewStore().ImportAsync(CaseId, name, default));
        Assert.Equal((400, AgentErrorCodes.InvalidFilename), (ex.Status, ex.Code));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../cases")]
    [InlineData("not-a-guid")]
    public async Task InvalidCaseId(string id)
    {
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() => NewStore().ImportAsync(id, "a.png", default));
        Assert.Equal((400, AgentErrorCodes.InvalidCaseId), (ex.Status, ex.Code));
    }

    // ── Upload ──────────────────────────────────────────────────────────────

    private string UploadTmp => Path.Combine(_root, ".upload-tmp");

    [Fact]
    public async Task Upload_Exact_IsCommitted_WithoutPart()
    {
        var store = NewStore();
        var data = RandomNumberGenerator.GetBytes(3 * 1024 * 1024 / 4);
        var info = await store.StageAndCommitUploadAsync(CaseId, "camara_1.webm", data.Length, new MemoryStream(data), default);
        Assert.Equal(Sha(data), info.Sha256);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(CaseDir, "camara_1.webm")));
        Assert.Empty(Directory.EnumerateFiles(UploadTmp));
    }

    [Theory]
    [InlineData(100, 99)]  // corta
    [InlineData(100, 101)] // larga
    public async Task Upload_WrongLength_IsIncomplete_AndLeavesNothing(int declared, int actual)
    {
        var store = NewStore();
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() =>
            store.StageAndCommitUploadAsync(CaseId, "a.bin", declared, new MemoryStream(new byte[actual]), default));
        Assert.Equal((400, AgentErrorCodes.IncompleteUpload), (ex.Status, ex.Code));
        Assert.Empty(Directory.EnumerateFiles(UploadTmp));
        Assert.False(File.Exists(Path.Combine(CaseDir, "a.bin")));
    }

    [Fact]
    public void Upload_Checks()
    {
        var store = NewStore(max: 1000, minFree: 100, free: 500);
        Assert.Equal((400, AgentErrorCodes.LengthRequired), Code(() => store.CheckUpload(CaseId, "a.bin", null)));
        Assert.Equal((413, AgentErrorCodes.FileTooLarge), Code(() => store.CheckUpload(CaseId, "a.bin", 1001)));
        Assert.Equal((507, AgentErrorCodes.InsufficientStorage), Code(() => store.CheckUpload(CaseId, "a.bin", 401)));
        Assert.Equal(1000, store.CheckUpload(CaseId, "a.bin", 400));
        Assert.Equal((400, AgentErrorCodes.InvalidFilename), Code(() => store.CheckUpload(CaseId, "../a.bin", 1)));
        // fail-open sin sondeo
        Assert.Equal(1000, NewStore(max: 1000, free: null).CheckUpload(CaseId, "a.bin", 1000));
    }

    private static (int, string) Code(Action action)
    {
        var ex = Assert.Throws<AgentHttpException>(action);
        return (ex.Status, ex.Code);
    }

    // ── List / Get / Delete / Cleanup ───────────────────────────────────────

    [Fact]
    public async Task List_Get_Delete()
    {
        var store = NewStore();
        Assert.Empty(store.List(CaseId).Files);
        Assert.False(Directory.Exists(CaseDir)); // listar no crea la carpeta

        await store.StageAndCommitUploadAsync(CaseId, "b.txt", 1, new MemoryStream([7]), default);
        await store.StageAndCommitUploadAsync(CaseId, "a.txt", 2, new MemoryStream([7, 8]), default);
        var listing = store.List(CaseId);
        Assert.Equal(CaseDir, listing.Directory);
        Assert.Equal(["a.txt", "b.txt"], listing.Files.Select(f => f.Filename));

        Assert.Equal(Path.Combine(CaseDir, "a.txt"), store.TryGetFile(CaseId, "a.txt"));
        Assert.Null(store.TryGetFile(CaseId, "z.txt"));
        Assert.Equal((400, AgentErrorCodes.InvalidFilename), Code(() => store.TryGetFile(CaseId, "../b.txt")));

        Assert.True(store.Delete(CaseId, "a.txt"));
        Assert.False(store.Delete(CaseId, "a.txt"));
    }

    [Fact]
    public void Cleanup_OnlyUploadTmp()
    {
        var store = NewStore();
        Directory.CreateDirectory(UploadTmp);
        File.WriteAllText(Path.Combine(UploadTmp, "x.part"), "x");
        File.WriteAllText(Path.Combine(_root, "screenshot_raiz.png"), "x");
        Assert.Equal(1, store.CleanupOrphanUploads());
        Assert.True(File.Exists(Path.Combine(_root, "screenshot_raiz.png")));
    }
}
