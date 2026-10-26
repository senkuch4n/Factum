using Factum.Agent.Services.Extraction;

namespace Factum.Agent.Tests.Extraction;

public class DevicePathsTests
{
    // §4.3: orden de categorías para la prioridad del dueño.
    private static readonly string[] Order =
        ["media", "downloads", "documents", "audio", "whatsapp_media", "all_storage", "custom"];

    [Theory]
    [InlineData("/storage/emulated/0", "/sdcard")]
    [InlineData("/storage/emulated/0/DCIM/", "/sdcard/DCIM")]
    [InlineData("/storage/self/primary/Download", "/sdcard/Download")]
    [InlineData("/sdcard//DCIM///a", "/sdcard/DCIM/a")]
    public void Normalize_CanonicalizaPrimario(string input, string expected) =>
        Assert.Equal(expected, DevicePaths.Normalize(input));

    [Theory]
    [InlineData("/data/data/com.app")]
    [InlineData("/sdcard/../etc")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public void Normalize_RechazaFueraDeSdcardYDotDot(string input) =>
        Assert.Null(DevicePaths.Normalize(input));

    [Fact]
    public void Normalize_RechazaBytesDeControl() =>
        Assert.Null(DevicePaths.Normalize("/sdcard/a\u0001b"));

    [Fact]
    public void Dedupe_DescartaDescendientes()
    {
        var roots = new[]
        {
            ("/sdcard", "all_storage"),
            ("/sdcard/DCIM", "media"),
            ("/sdcard/Download", "downloads"),
        };
        var result = DevicePaths.Dedupe(roots, Order);
        // all_storage cubre todo: queda una sola raíz.
        Assert.Single(result);
        Assert.Equal("/sdcard", result[0].Path);
        Assert.Equal("all_storage", result[0].Category);
    }

    [Fact]
    public void Dedupe_CategoriaMasTempranaGanaElEmpate()
    {
        var roots = new[]
        {
            ("/sdcard/WhatsApp", "whatsapp_media"),
            ("/sdcard/WhatsApp", "custom"),
        };
        var result = DevicePaths.Dedupe(roots, Order);
        Assert.Single(result);
        Assert.Equal("whatsapp_media", result[0].Category);
    }

    [Fact]
    public void ToLocal_PrefijaArchivosYConservaSdcard()
    {
        var m = DevicePaths.ToLocal("/sdcard/DCIM/a.jpg");
        Assert.Equal("archivos/sdcard/DCIM/a.jpg", m.RelativePath);
        Assert.False(m.Adjusted);
    }

    [Fact]
    public void ToLocal_SaneaCaracteresInvalidos()
    {
        var m = DevicePaths.ToLocal("/sdcard/DCIM/a:b?c.jpg");
        Assert.Equal("archivos/sdcard/DCIM/a_b_c.jpg", m.RelativePath);
        Assert.True(m.Adjusted);
    }

    [Fact]
    public void ToLocal_NombreReservadoDeWindows()
    {
        var m = DevicePaths.ToLocal("/sdcard/CON.txt");
        Assert.Equal("archivos/sdcard/_CON.txt", m.RelativePath);
        Assert.True(m.Adjusted);
    }

    [Fact]
    public void ToLocal_PuntoFinal()
    {
        var m = DevicePaths.ToLocal("/sdcard/carpeta./x");
        Assert.Equal("archivos/sdcard/carpeta._/x", m.RelativePath);
        Assert.True(m.Adjusted);
    }

    [Fact]
    public void ToLocal_ColisionMayusculas_SumaTilde2()
    {
        // El primer candidato "archivos/sdcard/Foto.JPG" choca; el resto no.
        var m = DevicePaths.ToLocal("/sdcard/Foto.JPG",
            candidate => candidate == "archivos/sdcard/Foto.JPG");
        Assert.Equal("archivos/sdcard/Foto~2.JPG", m.RelativePath);
        Assert.True(m.Adjusted);
    }

    [Fact]
    public void ToLocal_SegmentoMuyLargo_SeCortaConHash()
    {
        var largo = new string('a', 300);
        var m = DevicePaths.ToLocal($"/sdcard/{largo}");
        var seg = m.RelativePath.Split('/').Last();
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(seg) <= 255);
        Assert.Contains("~", seg);
        Assert.True(m.Adjusted);
    }
}
