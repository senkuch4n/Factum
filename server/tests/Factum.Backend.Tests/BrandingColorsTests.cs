using Factum.Backend.Services.Branding;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Tests;

/// <summary>
/// Colores de marca e isotipo (Refactorizaciones/informe-diseno-modelo.md §3.1, §5.8, B20, y
/// Refactorizaciones/informe-diseno-v6.md §5.7, B20: paleta de Factum y acento contra la tinta).
/// Solo colores ficticios; el ContentRoot es una carpeta temporal propia que se borra al final.
/// </summary>
public sealed class BrandingColorsTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "factum-branding-" + Guid.NewGuid().ToString("N"));

    public BrandingColorsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData("#1a2b3c", "1A2B3C")]
    [InlineData("1A2B3C", "1A2B3C")]
    [InlineData(" #ABCDEF ", "ABCDEF")]
    [InlineData("#FFF", null)]
    [InlineData("azul", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("#GGGGGG", null)]
    public void Normalize(string? raw, string? expected) =>
        Assert.Equal(expected, BrandingColors.Normalize(raw));

    [Fact]
    public void Defaults_SonLaPaletaDeFactum()
    {
        Assert.Equal("2F6F12", BrandingColors.DefaultPrimary);
        Assert.Equal("E8F3DF", BrandingColors.DefaultAccent);
        Assert.True(BrandingColors.ContrastWithWhite(BrandingColors.DefaultPrimary) >= BrandingColors.MinPrimaryContrast);
        Assert.True(BrandingColors.Contrast(BrandingColors.DefaultAccent, BrandingColors.InkColor)
                    >= BrandingColors.MinAccentContrastWithInk);
    }

    [Fact]
    public void Contrast_Simetrico()
    {
        Assert.Equal(21.0, BrandingColors.Contrast("000000", "FFFFFF"), 2);
        Assert.Equal(BrandingColors.Contrast("123456", "ABCDEF"), BrandingColors.Contrast("ABCDEF", "123456"), 10);
    }

    [Fact]
    public void ContrastWithWhite()
    {
        Assert.Equal(1.0, BrandingColors.ContrastWithWhite("FFFFFF"), 2);
        Assert.Equal(21.0, BrandingColors.ContrastWithWhite("000000"), 2);
        Assert.True(BrandingColors.ContrastWithWhite(BrandingColors.DefaultPrimary) >= BrandingColors.MinPrimaryContrast);
    }

    private sealed class FakeEnv(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Factum.Backend.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private BrandingSnapshot Load(BrandingOptions o) =>
        new BrandingService(Options.Create(o), new FakeEnv(_root), NullLogger<BrandingService>.Instance).Current;

    [Theory]
    [InlineData("#FFFF00", BrandingColors.DefaultPrimary)] // contraste bajo con blanco
    [InlineData("zzz", BrandingColors.DefaultPrimary)]     // no es hex
    [InlineData("", BrandingColors.DefaultPrimary)]        // sin configurar
    [InlineData("#123456", "123456")]
    public void PrimaryColor(string configured, string expected) =>
        Assert.Equal(expected, Load(new BrandingOptions { PrimaryColor = configured }).PrimaryColor);

    [Theory]
    [InlineData("#abcdef", "ABCDEF")]
    [InlineData("#FFFF00", "FFFF00")] // claro: pasa el contraste con la tinta
    [InlineData("#123456", BrandingColors.DefaultAccent)] // oscuro: no contrasta con la tinta
    [InlineData("nada", BrandingColors.DefaultAccent)]
    [InlineData("", BrandingColors.DefaultAccent)]
    public void AccentColor(string configured, string expected) =>
        Assert.Equal(expected, Load(new BrandingOptions { AccentColor = configured }).AccentColor);

    [Fact]
    public void IsotipoInexistente_SinIsotipoNiExcepcion()
    {
        var snap = Load(new BrandingOptions { OrganizationIsotype = "branding/no-existe.png" });
        Assert.Null(snap.Isotype);
    }

    [Fact]
    public void IsotipoValido_SeCarga()
    {
        File.WriteAllBytes(Path.Combine(_root, "iso.png"), TestImages.Png(64, 64));
        var snap = Load(new BrandingOptions { OrganizationIsotype = "iso.png" });
        Assert.NotNull(snap.Isotype);
        Assert.Equal(64, snap.Isotype!.Width);
        Assert.Equal("image/png", snap.Isotype.ContentType);
    }

    [Fact]
    public void SnapshotDeTresArgumentos_PaletaDeFactum()
    {
        var snap = new BrandingSnapshot(null, [], null);
        Assert.Null(snap.Isotype);
        Assert.Equal(BrandingColors.DefaultPrimary, snap.PrimaryColor);
        Assert.Equal(BrandingColors.DefaultAccent, snap.AccentColor);
    }
}
