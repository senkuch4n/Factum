using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Reports;
using Microsoft.Extensions.Logging.Abstractions;
using static Factum.Backend.Tests.ReportTestSupport;

namespace Factum.Backend.Tests.SesionGrabacion;

/// <summary>
/// "Registro audiovisual de la recolección" en el informe (grabacion-sesion-completa §8.3) sobre la plantilla v6 real, con
/// datos ficticios y carpetas temporales propias. Con <c>FACTUM_RENDER_DIR</c> deja las muestras.
/// </summary>
public sealed class ReportServiceSessionDocxTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-sesion-docx-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime T0 = new(2026, 10, 14, 17, 2, 10, DateTimeKind.Utc);
    private const string ZipHash = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
    private const string Seg1 = "sesion_pantalla_001_20261014_140210.mkv";
    private const string Seg2 = "sesion_pantalla_002_20261014_143211.mkv";

    public ReportServiceSessionDocxTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static readonly List<FileInfoDto> Files =
    [
        new("chat_export.txt", 10, new string('1', 64), T0),
        new("grabacion_1.mkv", 20, new string('2', 64), T0),
        new(Seg1, 943718400, new string('a', 64), T0),
        new(Seg2, 900000000, new string('b', 64), T0),
    ];

    private static Case MakeSessionCase(bool withSession)
    {
        var cas = MakeCase();
        cas.EvidenceStorage = EvidenceStorages.Agent;
        cas.ReportTexts = new ReportTexts
        {
            OperacionesRealizadas = "Operaciones ficticias.", AseguramientoEvidencia = "Aseguramiento ficticio.",
            Resultados = "Resultados ficticios.", ValoracionTecnica = "Valoración ficticia.", Conclusiones = "Conclusiones ficticias.",
        };
        if (!withSession) return cas;
        cas.RecordingSession = new Models.RecordingSession
        {
            Enabled = true, Platform = "android", Audio = "device", SegmentMinutes = 30, State = "stopped",
            Segments =
            [
                new SessionSegment
                {
                    Id = 1.ToString("x32"), Track = "pantalla", Index = 1, Filename = Seg1, State = "saved",
                    StartedAt = T0, StoppedAt = T0.AddSeconds(1800.7), RecordedSeconds = 1800.7, DurationSeconds = 1800.2,
                    Validation = "ok", StopReason = "rotacion", Size = 943718400, Sha256 = new string('a', 64),
                },
                new SessionSegment
                {
                    Id = 2.ToString("x32"), Track = "pantalla", Index = 2, Filename = Seg2, State = "saved",
                    StartedAt = T0.AddSeconds(1800), StoppedAt = T0.AddSeconds(2400), RecordedSeconds = 600,
                    DurationSeconds = 600, Validation = "ok", StopReason = "cambio_de_paso", Size = 900000000,
                    Sha256 = new string('b', 64),
                },
            ],
            Gaps = [new SessionGap { Id = 3.ToString("x32"), Track = "pantalla", From = T0.AddSeconds(2400), Reason = "cambio_de_paso" }],
            Marks = [new SessionMark { Id = 4.ToString("x32"), At = T0.AddSeconds(513.6), SegmentIndex = 1, OffsetSeconds = 513.6, Text = "Abre el chat con Juan", Kind = "manual" }],
        };
        return cas;
    }

    private async Task<(List<string> Paragraphs, List<List<string>> Tables, string Xml)> RenderAsync(Case cas, string name,
        ReportTimestampInfo? info = null)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        var files = cas.RecordingSession is null ? Files.Where(f => !f.Name.StartsWith("sesion_")).ToList() : Files;
        var service = new ReportService(new FakeBranding(FactumBrand()), new FakeSettings(),
            NullLogger<ReportService>.Instance, V6);
        var res = await service.GenerateReportAsync(cas, files, files.ToDictionary(f => f.Name, f => f.Hash!), dir, dir,
            ReportService.ZipFilenameFor(cas), ZipHash, default, info);
        var render = Environment.GetEnvironmentVariable("FACTUM_RENDER_DIR");
        if (!string.IsNullOrWhiteSpace(render))
        {
            Directory.CreateDirectory(render);
            File.Copy(res.PdfPath, Path.Combine(render, $"sesion_{name}.docx"), overwrite: true);
        }
        using var doc = WordprocessingDocument.Open(res.PdfPath, false);
        var body = doc.MainDocumentPart!.Document.Body!;
        var paras = body.Descendants<Paragraph>().Select(p => TextOf(p)).ToList();
        var tables = body.Descendants<Table>()
            .Select(t => t.Elements<TableRow>()
                .Select(r => string.Join("\n", r.Elements<TableCell>().First().Descendants<Paragraph>().Select(p => TextOf(p))))
                .ToList())
            .ToList();
        return (paras, tables, doc.MainDocumentPart.Document.OuterXml);
    }

    [Fact]
    public async Task SinSesion_SinBloqueNiResiduos()
    {
        var (paras, _, xml) = await RenderAsync(MakeSessionCase(false), "sin");
        Assert.DoesNotContain(paras, p => p.Contains("Registro audiovisual"));
        Assert.DoesNotContain("registroAudiovisual", xml);
        Assert.DoesNotContain("ArchivoSesion", xml);
        Assert.DoesNotContain("RegistroAudiovisual", xml);
    }

    [Fact]
    public async Task ConSesion_BloqueSegundaTablaYConteos()
    {
        var (paras, tables, xml) = await RenderAsync(MakeSessionCase(true), "con");
        Assert.DoesNotContain("registroAudiovisual", xml);
        Assert.DoesNotContain("ArchivoSesion", xml);
        Assert.Contains("Registro audiovisual de la recolección", paras);
        Assert.Contains(paras, p => p.StartsWith("Durante la recolección se grabó en video, de forma continua"));
        Assert.Contains("Huecos en la grabación:", paras);
        Assert.Contains(paras, p => p.Contains("Abre el chat con Juan"));

        // La tabla general no repite los tramos; la de la sesión tiene encabezado + 2 filas, sin fila del ZIP.
        var general = tables.Single(t => t.Any(r => r.StartsWith("chat_export.txt")));
        Assert.DoesNotContain(general, r => r.StartsWith("sesion_"));
        Assert.Contains(general, r => r.StartsWith("evidencia_"));
        var session = tables.Single(t => t.Any(r => r.StartsWith(Seg1)));
        Assert.Equal(3, session.Count);
        Assert.StartsWith(Seg1 + "\nPantalla · tramo 1 · ", session[1]);
        Assert.EndsWith("Archivo completo", session[1]);
        Assert.StartsWith(Seg2, session[2]);
        Assert.DoesNotContain(session, r => r.StartsWith("evidencia_"));
        Assert.Contains(new string('a', 64), xml);

        // Orden: tabla general → bloque de la sesión → cadena de custodia.
        var title = paras.IndexOf("Registro audiovisual de la recolección");
        var custody = paras.FindIndex(p => p.Contains("CADENA DE CUSTODIA"));
        Assert.True(title > 0 && (custody < 0 || title < custody));
    }

    [Fact]
    public async Task ConSello_NdeMNoCuentaLosTramos()
    {
        var info = new ReportTimestampInfo(new TimestampView("Free TSA", T0.AddHours(1), "0A"),
            new Dictionary<string, TimestampView>
            {
                ["chat_export.txt"] = new("Free TSA", T0, "01"),
                [Seg1] = new("Free TSA", T0.AddMinutes(31), "02"),
            });
        var (paras, tables, _) = await RenderAsync(MakeSessionCase(true), "sello", info);
        var all = string.Join("\n", paras);
        Assert.Contains("1 de 2 archivos cuentan además con un sello propio", all);
        Assert.Contains("1 de 2 tramos cuentan con sello de tiempo propio, indicado en la tabla siguiente.", paras);
        var session = tables.Single(t => t.Any(r => r.StartsWith(Seg1)));
        Assert.EndsWith("· Free TSA", session[1]);
        Assert.EndsWith("Sin sello de tiempo de un tercero al emitir este informe", session[2]);
    }

    [Fact]
    public void Conteos_SinTramos()
    {
        var cas = MakeSessionCase(true);
        var (cap, rec, pull, other) = ReportValues.Counts(Files, cas);
        Assert.Equal(1, rec); // grabacion_1.mkv; los tramos no cuentan
        Assert.Equal(0, cap);
        Assert.Equal(2, cap + rec + pull + other); // chat_export.txt y grabacion_1.mkv
    }
}
