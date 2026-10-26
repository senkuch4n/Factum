using Factum.Agent.Services.Extraction;

namespace Factum.Agent.Tests.Extraction;

public class ExtractionManifestWriterTests
{
    private static ManifestEntry Ok(string rel, string sha, long size = 10, string cat = "media") => new(
        RelativePath: rel, DevicePath: "/sdcard/" + rel, Origin: "adb pull", Category: cat,
        Type: ManifestTypes.Evidence, SizeBytes: size, MtimeDeviceIso: "2026-10-25T10:00:00Z",
        Sha256Pc: sha, Sha256Device: sha, Matches: ManifestMatch.Yes, NameAdjusted: false,
        State: ManifestStates.Ok, Reason: null);

    [Fact]
    public void Escape_TabSaltoYBackslash()
    {
        Assert.Equal("a\\tb\\nc\\\\d", ExtractionManifestWriter.Escape("a\tb\nc\\d"));
        Assert.Equal("a\tb\nc\\d", ExtractionManifestWriter.Unescape("a\\tb\\nc\\\\d"));
    }

    [Fact]
    public void Header_Exacto()
    {
        Assert.Equal(
            "ruta_relativa\truta_dispositivo\torigen\tcategoria\ttipo\ttamano_bytes\tmtime_dispositivo\t" +
            "sha256_pc\tsha256_dispositivo\tcoincide\tnombre_ajustado\testado\tmotivo",
            ExtractionManifestWriter.Header);
    }

    [Fact]
    public void FilaDeError_SinHash()
    {
        var e = new ManifestEntry("archivos/sdcard/x", "/sdcard/x", "adb pull", "media", ManifestTypes.Evidence,
            0, null, null, null, ManifestMatch.NotComputed, false, ManifestStates.Error, "permiso denegado");
        var row = ExtractionManifestWriter.FormatRow(e);
        var cols = row.Split('\t');
        Assert.Equal("", cols[7]);  // sha256_pc vacío
        Assert.Equal(ManifestStates.Error, cols[11]);
        Assert.Equal("permiso denegado", cols[12]);
    }

    [Fact]
    public async Task Close_OrdenaYEscribeHashYSums()
    {
        var dir = Directory.CreateTempSubdirectory("factum-manifest-test");
        try
        {
            var work = Path.Combine(dir.FullName, ".factum");
            await using (var w = new ExtractionManifestWriter(work))
            {
                // Fuera de orden a propósito.
                await w.AppendAsync(Ok("archivos/sdcard/b.jpg", new string('b', 64)));
                await w.AppendAsync(Ok("archivos/sdcard/a.jpg", new string('a', 64)));
            }

            var (sha, entries) = await ExtractionManifestWriter.CloseAsync(work, dir.FullName);
            Assert.Equal(2, entries);
            Assert.Equal(64, sha.Length);

            var manifest = await File.ReadAllLinesAsync(Path.Combine(dir.FullName, "MANIFIESTO.tsv"));
            Assert.Equal(ExtractionManifestWriter.Header, manifest[0]);
            // Ordenado por ruta_relativa (a antes que b).
            Assert.StartsWith("archivos/sdcard/a.jpg", manifest[1]);
            Assert.StartsWith("archivos/sdcard/b.jpg", manifest[2]);

            var shaFile = await File.ReadAllTextAsync(Path.Combine(dir.FullName, "MANIFIESTO.tsv.sha256"));
            Assert.Equal($"{sha}  MANIFIESTO.tsv\n", shaFile);

            var sums = await File.ReadAllLinesAsync(Path.Combine(dir.FullName, "SHA256SUMS.txt"));
            Assert.Contains(sums, l => l == $"{new string('a', 64)}  archivos/sdcard/a.jpg");
            Assert.Contains(sums, l => l == $"{new string('b', 64)}  archivos/sdcard/b.jpg");
        }
        finally { dir.Delete(recursive: true); }
    }
}
