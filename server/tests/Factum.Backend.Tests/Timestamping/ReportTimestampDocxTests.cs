using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Reports;
using Microsoft.Extensions.Logging.Abstractions;
using static Factum.Backend.Tests.ReportTestSupport;

namespace Factum.Backend.Tests.Timestamping;

/// <summary>
/// Sellos de tiempo en el informe (sellado-tiempo §8.3, §8.4, §11.2) sobre la plantilla v6 real, con datos
/// ficticios y carpetas temporales propias. Con <c>FACTUM_RENDER_DIR</c> deja las muestras para mirarlas.
/// </summary>
public sealed class ReportTimestampDocxTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-sellos-docx-" + Guid.NewGuid().ToString("N"));
    private static readonly TimeZoneInfo Ar = TimeZoneInfo.CreateCustomTimeZone("AR", TimeSpan.FromHours(-3), "AR", "AR");
    private static readonly DateTime Gen = new(2026, 10, 14, 12, 46, 5, DateTimeKind.Utc);
    private const string ZipHash = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

    public ReportTimestampDocxTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private sealed class ArSettings : IReportSettings
    {
        public TimeZoneInfo Zone => Ar;
        public string ZoneLabel => "AR (UTC-03:00)";
        public string DomicilioConstituido => "";
        public bool EncryptZip => false;
        public string DefaultOperacionesRealizadas => "";
        public string DefaultAseguramientoEvidencia => "";
        public string DefaultNotasTecnicas => "";
        public string DefaultReserva => "";
    }

    private static readonly List<FileInfoDto> Files =
    [
        new("chat_export.txt", 10, new string('1', 64), Gen, "/sdcard/WhatsApp/chat_export.txt"),
        new("grabacion_1.mkv", 20, new string('2', 64), Gen),
        new("registro.log", 30, new string('3', 64), Gen),
    ];

    private async Task<(string Body, List<string> NameCells, string Path)> GenerateAsync(ReportTimestampInfo? info,
        string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        var cas = MakeCase();
        cas.EvidenceStorage = EvidenceStorages.Agent;
        cas.ReportTexts = new ReportTexts
        {
            OperacionesRealizadas = "Operaciones ficticias.",
            AseguramientoEvidencia = "Aseguramiento ficticio.",
            Resultados = "Resultados ficticios.",
            ValoracionTecnica = "Valoración ficticia.",
            Conclusiones = "Conclusiones ficticias.",
        };
        var service = new ReportService(new FakeBranding(FactumBrand()), new ArSettings(),
            NullLogger<ReportService>.Instance, V6);
        var hashes = Files.ToDictionary(f => f.Name, f => f.Hash);
        var res = await service.GenerateReportAsync(cas, Files, hashes, dir, dir, ReportService.ZipFilenameFor(cas),
            ZipHash, default, info);

        var render = Environment.GetEnvironmentVariable("FACTUM_RENDER_DIR");
        if (!string.IsNullOrWhiteSpace(render))
        {
            Directory.CreateDirectory(render);
            File.Copy(res.PdfPath, Path.Combine(render, $"sello_{name}.docx"), overwrite: true);
        }

        using var doc = WordprocessingDocument.Open(res.PdfPath, false);
        var body = doc.MainDocumentPart!.Document.Body!;
        var paragraphs = string.Join("\n", body.Descendants<Paragraph>().Select(p => TextOf(p)));
        var cells = body.Descendants<TableRow>()
            .Select(r => r.Elements<TableCell>().FirstOrDefault())
            .Where(c => c is not null)
            .Select(c => string.Join("\n", c!.Descendants<Paragraph>().Select(p => TextOf(p))))
            .ToList();
        return (paragraphs, cells, res.PdfPath);
    }

    private static ReportTimestampInfo Info(bool zip) => new(
        zip ? new TimestampView("Free TSA", Gen, "092E9E8B") : null,
        new Dictionary<string, TimestampView>
        {
            ["chat_export.txt"] = new("Free TSA", Gen.AddMinutes(-30), "01"),
            ["registro.log"] = new("DigiCert", Gen.AddMinutes(-20), "02"),
        });

    [Fact]
    public async Task SinTimestamps_ComoAntes_SinSelloNiBloque()
    {
        var (body, cells, _) = await GenerateAsync(null, "desactivado");
        Assert.DoesNotContain("Sello de tiempo", body);
        Assert.DoesNotContain("sello de tiempo", body);
        Assert.DoesNotContain("RFC 3161", body);
        Assert.DoesNotContain("SelloTiempo", body);
        var chat = Assert.Single(cells, c => c.StartsWith("chat_export.txt"));
        Assert.Equal("chat_export.txt\nOrigen: /sdcard/WhatsApp/chat_export.txt", chat);
        var zip = Assert.Single(cells, c => c.StartsWith("evidencia_"));
        Assert.EndsWith("\nContenedor de la evidencia", zip);
    }

    [Fact]
    public async Task ZipSellado_LineaDelZip_Bloque_YNdeM()
    {
        var (body, cells, _) = await GenerateAsync(Info(zip: true), "zip_sellado");

        var zip = Assert.Single(cells, c => c.StartsWith("evidencia_"));
        Assert.EndsWith("\nContenedor de la evidencia\nSello de tiempo: 14/10/2026 09:46:05 (UTC-03:00) · Free TSA · N.º de serie 092E9E8B", zip);
        Assert.Contains("Los valores hash SHA-256 de la evidencia fueron sellados por una autoridad de sellado de tiempo " +
                        "independiente (Free TSA y DigiCert), conforme al estándar RFC 3161.", body);
        Assert.Contains("El sello del contenedor de la evidencia fue emitido el 14/10/2026 09:46:05 (UTC-03:00), con " +
                        "número de serie 092E9E8B. 2 de 3 archivos cuentan además con un sello propio", body);
        Assert.Contains("sin necesidad de la contraseña del contenedor.", body);
        // Archivo sin sello.
        var video = Assert.Single(cells, c => c.StartsWith("grabacion_1.mkv"));
        Assert.Equal("grabacion_1.mkv\nSin sello de tiempo de un tercero al emitir este informe", video);
        var log = Assert.Single(cells, c => c.StartsWith("registro.log"));
        Assert.Equal("registro.log\nSello de tiempo: 14/10/2026 09:26:05 (UTC-03:00) · DigiCert", log);
        // El bloque va después de la tabla y antes de la cadena de custodia.
        Assert.True(body.IndexOf("Los valores hash SHA-256 de la evidencia fueron sellados", StringComparison.Ordinal)
                    < body.IndexOf("CADENA DE CUSTODIA", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZipSinSello_ConstanciaEnLaFila_YParrafoDeConstancia()
    {
        var (body, cells, _) = await GenerateAsync(Info(zip: false), "zip_sin_sello");

        var zip = Assert.Single(cells, c => c.StartsWith("evidencia_"));
        Assert.EndsWith("\nContenedor de la evidencia\nSin sello de tiempo de un tercero al emitir este informe", zip);
        Assert.Contains("El contenedor de la evidencia no cuenta con sello de tiempo de un tercero al momento de emitir " +
                        "este informe: la autoridad de sellado de tiempo no respondió.", body);
        Assert.Contains("2 de 3 archivos cuentan con un sello de tiempo propio conforme al estándar RFC 3161, emitido " +
                        "por una autoridad independiente (Free TSA y DigiCert)", body);
    }

    [Fact]
    public async Task NingunSello_VarianteNCero()
    {
        var (body, _, _) = await GenerateAsync(new ReportTimestampInfo(null, new Dictionary<string, TimestampView>()),
            "ninguno");
        Assert.Contains("Ningún archivo de la evidencia cuenta con sello de tiempo de un tercero al emitir este informe.", body);
        Assert.DoesNotContain("()", body);
    }

    [Fact]
    public async Task ArchivoConOrigenYSello_DosLineasEnOrden()
    {
        var (_, cells, _) = await GenerateAsync(Info(zip: true), "origen");
        var chat = Assert.Single(cells, c => c.StartsWith("chat_export.txt"));
        Assert.Equal("chat_export.txt\nOrigen: /sdcard/WhatsApp/chat_export.txt\n" +
                     "Sello de tiempo: 14/10/2026 09:16:05 (UTC-03:00) · Free TSA", chat);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NoQuedanPlaceholdersDelSello(bool sealing)
    {
        var (body, _, path) = await GenerateAsync(sealing ? Info(zip: true) : null, "placeholders_" + sealing);
        Assert.DoesNotContain("{descripcionSelloTiempo}", body);
        Assert.DoesNotContain("{#selloTiempo}", body);
        Assert.DoesNotContain("{/selloTiempo}", body);
        Assert.DoesNotContain("{", body);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void JoinSpanish() =>
        Assert.Equal(("", "A", "A y B", "A, B y C"),
            (ReportTimestampTexts.JoinSpanish([]), ReportTimestampTexts.JoinSpanish(["A"]),
                ReportTimestampTexts.JoinSpanish(["A", "B"]), ReportTimestampTexts.JoinSpanish(["A", "B", "C"])));
}
