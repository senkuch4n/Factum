using Factum.Agent.Models;
using Factum.Agent.Services;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Tests;

/// <summary>Vistas previas del paso 2 en <c>previews/</c> (wizard-concurrencia-pestanas §4.2, DT5).</summary>
public sealed class PreviewStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-prev-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private FileStorageService Create() =>
        new(Options.Create(new AgentOptions { DataDirectory = _root }));

    private static string Write(string path)
    {
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    [Fact]
    public void Ctor_crea_previews()
    {
        var s = Create();
        Assert.True(Directory.Exists(s.PreviewDirectory));
        Assert.Equal(Path.Combine(s.DataDirectory, "previews"), s.PreviewDirectory);
    }

    [Fact]
    public void PreviewKey_es_estable_y_de_12_hex()
    {
        var k = FileStorageService.PreviewKey("R58N123ABC");
        Assert.Matches("^[0-9a-f]{12}$", k);
        Assert.Equal(k, FileStorageService.PreviewKey("R58N123ABC"));
        Assert.NotEqual(k, FileStorageService.PreviewKey("OTRO"));
    }

    [Theory]
    [InlineData("preview_0123456789ab_20261013_101112_123.png", true)]
    [InlineData("../x.png", false)]
    [InlineData("preview_x.png", false)]
    [InlineData("screenshot_20261013_101112_123.png", false)]
    [InlineData("preview_0123456789AB_20261013_101112_123.png", false)]
    [InlineData("preview_0123456789ab_20261013_101112_123.png\n", false)]
    [InlineData("../preview_0123456789ab_20261013_101112_123.png", false)]
    [InlineData(null, false)]
    public void IsPreviewName(string? name, bool ok) => Assert.Equal(ok, FileStorageService.IsPreviewName(name));

    [Fact]
    public void NewPreviewPath_va_a_previews_con_nombre_valido()
    {
        var s = Create();
        var p = s.NewPreviewPath("EMU001");
        Assert.Equal(s.PreviewDirectory, Path.GetDirectoryName(p));
        Assert.True(FileStorageService.IsPreviewName(Path.GetFileName(p)));
        Assert.StartsWith($"preview_{FileStorageService.PreviewKey("EMU001")}_", Path.GetFileName(p));
    }

    [Fact]
    public void PrunePreviews_borra_solo_las_del_mismo_serial_salvo_la_nueva()
    {
        var s = Create();
        var kA = FileStorageService.PreviewKey("A");
        var kB = FileStorageService.PreviewKey("B");
        var oldA = Write(Path.Combine(s.PreviewDirectory, $"preview_{kA}_20261013_100000_000.png"));
        var newA = Write(Path.Combine(s.PreviewDirectory, $"preview_{kA}_20261013_100001_000.png"));
        var b = Write(Path.Combine(s.PreviewDirectory, $"preview_{kB}_20261013_100000_000.png"));
        var root = Write(Path.Combine(s.DataDirectory, $"preview_{kA}_20261013_090000_000.png"));

        s.PrunePreviews("A", newA);

        Assert.False(File.Exists(oldA));
        Assert.True(File.Exists(newA));
        Assert.True(File.Exists(b));
        Assert.True(File.Exists(root)); // la raíz no se toca
    }

    [Fact]
    public void ClearPreviews_vacia_solo_previews()
    {
        var s = Create();
        Write(Path.Combine(s.PreviewDirectory, "preview_0123456789ab_20261013_100000_000.png"));
        Write(Path.Combine(s.PreviewDirectory, "otra_cosa.tmp"));
        var root = Write(Path.Combine(s.DataDirectory, "screenshot_20261013_100000_000.png"));

        Assert.Equal(2, s.ClearPreviews());
        Assert.Empty(Directory.EnumerateFileSystemEntries(s.PreviewDirectory));
        Assert.True(File.Exists(root));
        Assert.DoesNotContain(s.ListFiles(), f => f.Name.StartsWith("preview_"));
    }

    [Fact]
    public void ResolvePreview_valida_nombre_y_existencia()
    {
        var s = Create();
        var p = Write(s.NewPreviewPath("A"));
        Assert.Equal(p, s.ResolvePreview(Path.GetFileName(p)));
        Assert.Null(s.ResolvePreview("preview_0123456789ab_20261013_100000_000.png"));
        Assert.Null(s.ResolvePreview("../appsettings.json"));
    }

    [Fact]
    public void ListFiles_no_ve_previews()
    {
        var s = Create();
        Write(s.NewPreviewPath("A"));
        Assert.Empty(s.ListFiles());
    }

    [Fact]
    public void PreviewGate_es_el_mismo_por_serial()
    {
        var s = Create();
        Assert.Same(s.PreviewGate("A"), s.PreviewGate("A"));
        Assert.NotSame(s.PreviewGate("A"), s.PreviewGate("B"));
    }
}
