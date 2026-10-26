using Factum.Agent.Services.Extraction;

namespace Factum.Agent.Tests.Extraction;

/// <summary>
/// Parsers del camino real (extraccion-logica-android, QA fixes H1–H3/M1/M2). Las muestras son salidas REALES del
/// Samsung M12 (SM-M127F, Android 11) capturadas durante la verificación.
/// </summary>
public class ExtractionDeviceQueriesTests
{
    // M2 — batería
    [Fact]
    public void ParseBattery_DelM12()
    {
        const string dumpsys = "Current Battery Service state:\n  AC powered: true\n  USB powered: false\n  level: 100\n  scale: 100\n";
        var (level, charging) = ExtractionDeviceQueries.ParseBattery(dumpsys);
        Assert.Equal(100, level);
        Assert.True(charging);
    }

    [Fact]
    public void ParseBattery_SinLevel_Null()
    {
        var (level, charging) = ExtractionDeviceQueries.ParseBattery("");
        Assert.Null(level);
        Assert.Null(charging);
    }

    // H3 — du -s -k
    [Fact]
    public void ParseDuKib_KibABytes()
    {
        var sizes = ExtractionDeviceQueries.ParseDuKib("129375\t/sdcard/Download\n8\t/sdcard/Documents\n");
        Assert.Equal(129375L * 1024, sizes["/sdcard/Download"]);
        Assert.Equal(8L * 1024, sizes["/sdcard/Documents"]);
    }

    // H2 — existencia de raíces (incluye una con espacio, WhatsApp Business)
    [Fact]
    public void ParseExistingRoots_FiltraAlSubconjunto()
    {
        var roots = new[]
        {
            "/sdcard/DCIM", "/sdcard/Download", "/sdcard/NoExiste",
            "/sdcard/Android/media/com.whatsapp/WhatsApp/Media",
        };
        var stdout = "/sdcard/DCIM\n/sdcard/Download\n/sdcard/Android/media/com.whatsapp/WhatsApp/Media\n";
        var present = ExtractionDeviceQueries.ParseExistingRoots(stdout, roots);
        Assert.Contains("/sdcard/DCIM", present);
        Assert.Contains("/sdcard/Download", present);
        Assert.Contains("/sdcard/Android/media/com.whatsapp/WhatsApp/Media", present);
        Assert.DoesNotContain("/sdcard/NoExiste", present);
    }

    // M1 — inventario (stat -c '%s|%Y|%n')
    [Fact]
    public void ParseStatLine_DelM12()
    {
        var row = ExtractionDeviceQueries.ParseStatLine("9752450|1707885719|/sdcard/Download/Flyer Frutas y Verduras (14).pdf");
        Assert.NotNull(row);
        Assert.Equal(9752450L, row!.Value.Size);
        Assert.Equal("2024-02-14T04:41:59Z", row.Value.MtimeIso);
        Assert.Equal("/sdcard/Download/Flyer Frutas y Verduras (14).pdf", row.Value.Path);
    }

    [Fact]
    public void ParseStatLine_LineaRara_Null() =>
        Assert.Null(ExtractionDeviceQueries.ParseStatLine("no-tiene-pipes"));

    // H1 — hash del dispositivo + coincide
    [Fact]
    public void ParseDeviceHashes_YCoincide()
    {
        const string sha = "e31f840ffd0d7ac890901a7933970f8c14eb8879967f19ddf080784ebf4d4158";
        var map = ExtractionDeviceQueries.ParseDeviceHashes($"{sha}  /sdcard/DCIM/Camera/a.mp4\n");
        Assert.Equal(sha, map["/sdcard/DCIM/Camera/a.mp4"]);

        // Igual → si, sin mismatch.
        Assert.Equal(ManifestMatch.Yes, ExtractionDeviceQueries.Coincide(map, "/sdcard/DCIM/Camera/a.mp4", sha.ToUpperInvariant(), out var m1));
        Assert.False(m1);
        // Distinto → no, con mismatch (no es error).
        Assert.Equal(ManifestMatch.No, ExtractionDeviceQueries.Coincide(map, "/sdcard/DCIM/Camera/a.mp4", new string('0', 64), out var m2));
        Assert.True(m2);
        // Ausente del mapa → no_calculado.
        Assert.Equal(ManifestMatch.NotComputed, ExtractionDeviceQueries.Coincide(map, "/sdcard/otro", sha, out var m3));
        Assert.False(m3);
        // Sin sondeo (null) → no_calculado.
        Assert.Equal(ManifestMatch.NotComputed, ExtractionDeviceQueries.Coincide(null, "/sdcard/x", sha, out _));
    }
}
