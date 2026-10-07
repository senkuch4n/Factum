using Factum.Backend.Infrastructure;

namespace Factum.Backend.Tests;

/// <summary>
/// .generate-tmp/ del backend (zip-local-informe-servidor §3.3, §5.8). Todo en una carpeta
/// temporal propia (Path.GetTempPath()/&lt;guid&gt;), nunca en dev-data.
/// </summary>
public sealed class StorageGenerateTmpTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-gentmp-" + Guid.NewGuid().ToString("N"));

    private sealed class NoProbe : IDiskSpaceProbe
    {
        public long? GetAvailableFreeBytes(string path) => null;
    }

    private StorageService NewStorage() =>
        new(new StorageOptions { DataDirectory = _root }, new NoProbe(), null);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Cleanup_DeletesOnlyInsideGenerateTmp()
    {
        var storage = NewStorage();
        var caseFile = Path.Combine(storage.CaseDir("caso-1"), "screenshot_1.png");
        File.WriteAllText(caseFile, "evidencia");
        var uploadTmp = Path.Combine(_root, ".upload-tmp");
        Directory.CreateDirectory(uploadTmp);
        File.WriteAllText(Path.Combine(uploadTmp, "x.part"), "subida");
        var outside = Path.Combine(_root, "otro.txt");
        File.WriteAllText(outside, "no tocar");

        var gid = Guid.NewGuid().ToString("N");
        var tmp = storage.NewGenerationTempDir(gid);
        File.WriteAllText(Path.Combine(tmp, "screenshot_1.png"), "captura");
        File.WriteAllText(Path.Combine(_root, ".generate-tmp", "suelto.bin"), "x");

        Assert.Equal(2, storage.CleanupOrphanGenerations());
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_root, ".generate-tmp")));
        Assert.True(File.Exists(caseFile));
        Assert.True(File.Exists(Path.Combine(uploadTmp, "x.part")));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void NewGenerationTempDir_RejectsInvalidId_AndResetsExisting()
    {
        var storage = NewStorage();
        Assert.Throws<ArgumentException>(() => storage.NewGenerationTempDir("../x"));
        Assert.Throws<ArgumentException>(() => storage.NewGenerationTempDir(Guid.NewGuid().ToString("D")));

        var gid = Guid.NewGuid().ToString("N");
        var tmp = storage.NewGenerationTempDir(gid);
        File.WriteAllText(Path.Combine(tmp, "viejo.png"), "x");
        Assert.Equal(tmp, storage.NewGenerationTempDir(gid));
        Assert.Empty(Directory.EnumerateFileSystemEntries(tmp));
    }

    [Fact]
    public void HasEvidenceFiles_DoesNotCreateFolder_AndIgnoresArtifacts()
    {
        var storage = NewStorage();
        var id = Guid.NewGuid().ToString();
        Assert.False(storage.HasEvidenceFiles(id));
        Assert.False(Directory.Exists(Path.Combine(_root, "cases", id)));

        var dir = storage.CaseDir(id);
        File.WriteAllText(Path.Combine(dir, "informe_pericial_x.docx"), "x");
        File.WriteAllText(Path.Combine(dir, "evidencia_x.zip"), "x");
        Assert.False(storage.HasEvidenceFiles(id));
        File.WriteAllText(Path.Combine(dir, "screenshot_1.png"), "x");
        Assert.True(storage.HasEvidenceFiles(id));
        Assert.False(storage.HasEvidenceFiles("../" + id));
    }

    [Fact]
    public void Cleanup_WithoutFolder_ReturnsZero() => Assert.Equal(0, NewStorage().CleanupOrphanGenerations());
}
