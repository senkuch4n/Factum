using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;
using Factum.Backend.Services.Reports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;
using static Factum.Backend.Tests.ReportTestSupport;
using DWP = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using DRAW = DocumentFormat.OpenXml.Drawing;

namespace Factum.Backend.Tests;

/// <summary>
/// Capturas dentro de las secciones del informe (editor-imagenes-informe §6.4.2 y §6.5,
/// T12-T20), con <c>ReportService.GenerateAsync</c> y la plantilla v6. Cada generación usa una
/// carpeta temporal propia (sin Mongo ni Storage:DataDirectory) y datos ficticios.
/// </summary>
public sealed class ReportBodyImagesDocxTests : IDisposable
{
    private const string Shot1 = "screenshot_20261001_101010.png";
    private const string Shot2 = "screenshot_20261001_101530.png";
    private const string I1 = "![Chat con Juan](captura:" + Shot2 + ")";
    private const string I2 = "![](captura:" + Shot2 + ")";
    private const string I6 = "Se observa la conversación:\n\n" + I1 + "\n\nFin.";
    private const string TextoSinImagen = "Se observa la conversación:\n\nFin.";

    // Emu de la caja del anexo: 14 × 10.5 cm.
    private const long MaxW = 5_040_000;
    private const long MaxH = 3_780_000;

    private readonly ITestOutputHelper _output;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-bodyimg-" + Guid.NewGuid().ToString("N"));

    public ReportBodyImagesDocxTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private sealed record Generated(string Dir, string Docx, ReportResult Result, Dictionary<string, string> ShaBefore);

    private static Dictionary<string, byte[]> DefaultFiles() => new(StringComparer.Ordinal)
    {
        ["chat_export.txt"] = "contenido de prueba 1"u8.ToArray(),
        [Shot1] = TestImages.Png(40, 80, 0x20, 0x90, 0x40),
        [Shot2] = TestImages.Png(90, 30, 0xC0, 0x40, 0x40),
    };

    private static Case MarkdownCase(Action<ReportTexts>? fill = null, string? formato = ReportTextFormats.Markdown)
    {
        var cas = MakeCase();
        var t = new ReportTexts
        {
            ObjetoInforme = "Objeto ficticio.",
            OperacionesRealizadas = "Operaciones ficticias.",
            AseguramientoEvidencia = "Aseguramiento ficticio.",
            Resultados = I6,
            ValoracionTecnica = "Valoración ficticia.",
            Conclusiones = "Conclusiones ficticias.",
            NotasTecnicas = "Notas ficticias.",
            Reserva = "Reserva ficticia.",
            Formato = formato,
        };
        fill?.Invoke(t);
        cas.ReportTexts = t;
        return cas;
    }

    private Generated Gen(Case cas, Dictionary<string, byte[]>? files = null, Action<string>? beforeGenerate = null,
        string? dir = null)
    {
        dir ??= Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var stamp = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var list = new List<FileInfoDto>();
        var sha = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, data) in files ?? DefaultFiles())
        {
            var path = Path.Combine(dir, name);
            File.WriteAllBytes(path, data);
            File.SetLastWriteTimeUtc(path, stamp);
            list.Add(new FileInfoDto(name, data.Length, "", stamp));
            sha[name] = Convert.ToHexStringLower(SHA256.HashData(data));
        }
        beforeGenerate?.Invoke(dir);
        var service = new ReportService(new FakeBranding(FactumBrand()), new FakeSettings(),
            NullLogger<ReportService>.Instance, V6);
        var result = service.GenerateAsync(cas, list, dir).GetAwaiter().GetResult();
        return new Generated(dir, result.PdfPath, result, sha);
    }

    private static List<Paragraph> BodyParagraphs(WordprocessingDocument doc) =>
        doc.MainDocumentPart!.Document.Body!.Elements<Paragraph>().ToList();

    private static int IndexOfText(List<Paragraph> paras, string text) =>
        paras.FindIndex(p => TextOf(p) == text);

    private static DWP.Inline? DrawingOf(Paragraph p) => p.Descendants<DWP.Inline>().SingleOrDefault();

    private static string EmbedOf(Paragraph p) => DrawingOf(p)!.Descendants<DRAW.Blip>().Single().Embed!.Value!;

    private static string PartSha(WordprocessingDocument doc, string relId)
    {
        using var s = doc.MainDocumentPart!.GetPartById(relId).GetStream();
        return Convert.ToHexStringLower(SHA256.HashData(s));
    }

    // Párrafos del anexo: desde el primer epígrafe "Figura 1 – <archivo>" sin alt hasta el final.
    private static List<string> AnnexTexts(WordprocessingDocument doc)
    {
        var paras = BodyParagraphs(doc);
        var title = paras.FindLastIndex(p => TextOf(p).Trim().ToUpperInvariant().Contains("ANEXO"));
        Assert.True(title >= 0, "No se encontró el título del anexo");
        return paras.Skip(title).Select(p => TextOf(p) + "|" + (DrawingOf(p)?.GetFirstChild<DWP.DocProperties>()?.Name?.Value ?? ""))
            .ToList();
    }

    private static readonly Regex HexHash = new("[0-9a-f]{64}", RegexOptions.CultureInvariant);

    // Filas de la tabla de hashes de la evidencia (sin la fila del ZIP): "nombre|hash".
    private static List<string> HashRows(WordprocessingDocument doc) =>
        doc.MainDocumentPart!.Document.Body!.Descendants<TableRow>()
            .Select(TextOf)
            .Where(t => HexHash.IsMatch(t) && !t.Contains("evidencia_", StringComparison.Ordinal))
            .ToList();

    private static string HashInTable(WordprocessingDocument doc, string file) =>
        HexHash.Match(doc.MainDocumentPart!.Document.Body!.Descendants<TableRow>()
            .Select(TextOf).Single(t => t.Contains(file, StringComparison.Ordinal))).Value;

    private static List<string> ZipEntries(string zip)
    {
        using var z = ZipFile.OpenRead(zip);
        return z.Entries.Select(e => e.FullName).ToList();
    }

    private sealed class ListLogger : ILogger<ReportService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add($"{logLevel}: {formatter(state, exception)}");
    }

    // ── T12. Imagen y epígrafe en su lugar ───────────────────────────────────

    [Fact]
    public void T12_ImagenEnElCuerpo_ConEpigrafe_YElAnexoIgual()
    {
        var g = Gen(MarkdownCase());
        using var doc = WordprocessingDocument.Open(g.Docx, false);
        var paras = BodyParagraphs(doc);

        var i = IndexOfText(paras, "Se observa la conversación:");
        Assert.True(i > 0);
        var image = paras[i + 1];
        var inline = DrawingOf(image);
        Assert.NotNull(inline);
        Assert.Equal(JustificationValues.Center, image.ParagraphProperties!.Justification!.Val!.Value);
        Assert.NotNull(image.ParagraphProperties.KeepNext);
        var extent = inline.Extent!;
        Assert.True(extent.Cx!.Value <= MaxW && extent.Cy!.Value <= MaxH);
        Assert.Equal(MaxW, extent.Cx!.Value);                 // 90 × 30: manda el ancho
        Assert.Equal(3.0, (double)extent.Cx!.Value / extent.Cy!.Value, 3);
        Assert.Equal(Shot2, inline.GetFirstChild<DWP.DocProperties>()!.Name!.Value);

        Assert.Equal($"Figura 2 – Chat con Juan ({Shot2})", TextOf(paras[i + 2]));
        Assert.Equal("Fin.", TextOf(paras[i + 3]));

        var annex = paras.Select(TextOf).Where(t => t.StartsWith("Figura ", StringComparison.Ordinal)).ToList();
        Assert.Equal([$"Figura 2 – Chat con Juan ({Shot2})", $"Figura 1 – {Shot1}", $"Figura 2 – {Shot2}"], annex);
    }

    // ── T13. Mismos bytes y reutilización del ImagePart ──────────────────────

    [Fact]
    public void T13_MismosBytes_ReutilizaLaParteDelAnexo()
    {
        var g = Gen(MarkdownCase());
        var plain = Gen(MarkdownCase(t => t.Resultados = TextoSinImagen));
        using var doc = WordprocessingDocument.Open(g.Docx, false);
        using var docPlain = WordprocessingDocument.Open(plain.Docx, false);

        var paras = BodyParagraphs(doc);
        var bodyImage = paras[IndexOfText(paras, "Se observa la conversación:") + 1];
        var bodyRel = EmbedOf(bodyImage);
        Assert.Equal(g.ShaBefore[Shot2], PartSha(doc, bodyRel));
        Assert.Equal(g.ShaBefore[Shot2], HashInTable(doc, Shot2));

        var annexRel = paras.Where(p => DrawingOf(p)?.GetFirstChild<DWP.DocProperties>()?.Name?.Value == Shot2)
            .Select(EmbedOf).ToList();
        Assert.Equal(2, annexRel.Count);
        Assert.All(annexRel, r => Assert.Equal(bodyRel, r));
        Assert.Equal(docPlain.MainDocumentPart!.ImageParts.Count(), doc.MainDocumentPart!.ImageParts.Count());
    }

    [Fact]
    public void T13_PngQueEsJpeg_ParteNuevaDelTipoDetectado()
    {
        var files = DefaultFiles();
        files[Shot2] = TestImages.Jpeg(90, 30);
        var g = Gen(MarkdownCase(), files);
        var plain = Gen(MarkdownCase(t => t.Resultados = TextoSinImagen), files);
        using var doc = WordprocessingDocument.Open(g.Docx, false);
        using var docPlain = WordprocessingDocument.Open(plain.Docx, false);

        var paras = BodyParagraphs(doc);
        var bodyRel = EmbedOf(paras[IndexOfText(paras, "Se observa la conversación:") + 1]);
        var part = (ImagePart)doc.MainDocumentPart!.GetPartById(bodyRel);
        Assert.Equal("image/jpeg", part.ContentType);
        Assert.Equal(g.ShaBefore[Shot2], PartSha(doc, bodyRel));
        Assert.Equal(g.ShaBefore[Shot2], HashInTable(doc, Shot2));
        var annexRel = EmbedOf(paras.Last(p => DrawingOf(p)?.GetFirstChild<DWP.DocProperties>()?.Name?.Value == Shot2));
        Assert.NotEqual(bodyRel, annexRel);
        Assert.Equal(docPlain.MainDocumentPart!.ImageParts.Count() + 1, doc.MainDocumentPart.ImageParts.Count());
    }

    // ── T14. Invariantes de la evidencia ─────────────────────────────────────

    [Fact]
    public void T14_Invariantes_TablaZipReportHashYAnexo()
    {
        var withImage = Gen(MarkdownCase());
        var without = Gen(MarkdownCase(t => t.Resultados = TextoSinImagen));
        using var a = WordprocessingDocument.Open(withImage.Docx, false);
        using var b = WordprocessingDocument.Open(without.Docx, false);

        Assert.Equal(3, HashRows(a).Count);
        Assert.Equal(HashRows(b), HashRows(a));
        Assert.Equal(ZipEntries(without.Result.ZipPath), ZipEntries(withImage.Result.ZipPath));
        Assert.Equal(AnnexTexts(b), AnnexTexts(a));

        foreach (var g in new[] { withImage, without })
        {
            using var fs = File.OpenRead(g.Docx);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(fs)), g.Result.ReportHash);
        }
    }

    // ── T15. Captura con rol ─────────────────────────────────────────────────

    [Fact]
    public void T15_CapturaConRol_EpigrafeDeIdentificacion_YElAnexoSinElla()
    {
        var cas = MarkdownCase();
        cas.CaptureRoles = [new CaptureRole { Filename = Shot2, Role = CaptureRole.ImeiModelo }];
        var g = Gen(cas);
        using var doc = WordprocessingDocument.Open(g.Docx, false);
        var paras = BodyParagraphs(doc);

        var i = IndexOfText(paras, "Se observa la conversación:");
        Assert.Equal($"Captura de identificación (IMEI y modelo) – Chat con Juan ({Shot2})", TextOf(paras[i + 2]));
        var annex = paras.Select(TextOf).Where(t => t.StartsWith("Figura ", StringComparison.Ordinal)).ToList();
        Assert.Equal([$"Figura 1 – {Shot1}"], annex);

        // Reutiliza la parte de la captura de identificación (la del cuerpo de identificación).
        var bodyRel = EmbedOf(paras[i + 1]);
        var identRels = doc.MainDocumentPart!.Document.Body!.Descendants<DWP.Inline>()
            .Where(d => d.GetFirstChild<DWP.DocProperties>()?.Name?.Value == Shot2)
            .Select(d => d.Descendants<DRAW.Blip>().Single().Embed!.Value).Distinct().ToList();
        Assert.Equal([bodyRel], identRels);
        Assert.Equal(g.ShaBefore[Shot2], PartSha(doc, bodyRel));
    }

    [Fact]
    public void T15_CapturaConRolNombreDispositivo()
    {
        var cas = MarkdownCase();
        cas.CaptureRoles = [new CaptureRole { Filename = Shot2, Role = CaptureRole.NombreDispositivo }];
        var g = Gen(cas);
        using var doc = WordprocessingDocument.Open(g.Docx, false);
        var paras = BodyParagraphs(doc);
        var i = IndexOfText(paras, "Se observa la conversación:");
        Assert.Equal($"Captura de identificación (nombre del dispositivo) – Chat con Juan ({Shot2})", TextOf(paras[i + 2]));
        Assert.Equal(g.ShaBefore[Shot2], PartSha(doc, EmbedOf(paras[i + 1])));
    }

    // ── T16. Alt vacío y alt con llaves ──────────────────────────────────────

    [Fact]
    public void T16_AltVacio_YAltConPlaceholderLiteral()
    {
        var g = Gen(MarkdownCase(t => t.Resultados = "Texto:\n\n" + I2));
        using (var doc = WordprocessingDocument.Open(g.Docx, false))
        {
            var paras = BodyParagraphs(doc);
            Assert.Equal($"Figura 2 – {Shot2}", TextOf(paras[IndexOfText(paras, "Texto:") + 2]));
        }

        var logger = new ListLogger();
        var dir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var stamp = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var list = new List<FileInfoDto>();
        foreach (var (name, data) in DefaultFiles())
        {
            File.WriteAllBytes(Path.Combine(dir, name), data);
            list.Add(new FileInfoDto(name, data.Length, "", stamp));
        }
        var cas = MarkdownCase(t => t.Resultados = "Texto:\n\n![{caratula} y {anexoCapturas}](captura:" + Shot2 + ")");
        Assert.Null(CaseValidation.ValidateReportTexts(new ReportTextsDto(Resultados: cas.ReportTexts!.Resultados, Formato: "markdown")));
        var docx = new ReportService(new FakeBranding(FactumBrand()), new FakeSettings(), logger, V6)
            .GenerateAsync(cas, list, dir).GetAwaiter().GetResult().PdfPath;
        using (var doc = WordprocessingDocument.Open(docx, false))
        {
            var paras = BodyParagraphs(doc);
            Assert.Equal($"Figura 2 – {{caratula}} y {{anexoCapturas}} ({Shot2})",
                TextOf(paras[IndexOfText(paras, "Texto:") + 2]));
        }
        foreach (var m in logger.Messages) _output.WriteLine(m);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("quedó sin reemplazar", StringComparison.Ordinal));
    }

    // ── T17. Validez ─────────────────────────────────────────────────────────

    private static HashSet<string> ValidationKeys(WordprocessingDocument doc, ITestOutputHelper? output, string label)
    {
        var validator = new OpenXmlValidator(FileFormatVersions.Office2019);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in validator.Validate(doc))
        {
            var path = Regex.Replace(e.Path?.XPath ?? "", @"\[\d+\]", "");
            var key = $"{e.Part?.Uri} {e.Id} {path}";
            if (keys.Add(key)) output?.WriteLine($"{label}: {key} — {e.Description}");
        }
        return keys;
    }

    [Fact]
    public void T17_DocPrUnicos_YSinErroresNuevos()
    {
        var cas = MarkdownCase(t =>
        {
            t.ObjetoInforme = I1;
            t.Resultados = I6 + "\n\n![Otra](captura:" + Shot1 + ")";
            t.Reserva = "Reserva:\n\n" + I2;
        });
        cas.CaptureRoles = [new CaptureRole { Filename = Shot1, Role = CaptureRole.NombreDispositivo }];
        var g = Gen(cas);
        var plainCas = MarkdownCase(t => t.Resultados = TextoSinImagen);
        plainCas.CaptureRoles = cas.CaptureRoles;
        var plain = Gen(plainCas);
        using var doc = WordprocessingDocument.Open(g.Docx, false);
        using var docPlain = WordprocessingDocument.Open(plain.Docx, false);

        var main = doc.MainDocumentPart!;
        var ids = main.Document.Descendants<DWP.DocProperties>()
            .Concat(main.HeaderParts.SelectMany(h => h.Header.Descendants<DWP.DocProperties>()))
            .Concat(main.FooterParts.SelectMany(f => f.Footer.Descendants<DWP.DocProperties>()))
            .Select(d => d.Id!.Value).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        // Shot1 tiene rol: el anexo solo numera Shot2 (Figura 1).
        var captions = BodyParagraphs(doc).Select(TextOf)
            .Where(t => t.StartsWith("Figura ", StringComparison.Ordinal) ||
                        t.StartsWith("Captura de identificación", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(
        [
            $"Figura 1 – Chat con Juan ({Shot2})",
            $"Captura de identificación (nombre del dispositivo) – {Shot1}",
            $"Figura 1 – Chat con Juan ({Shot2})",
            $"Captura de identificación (nombre del dispositivo) – Otra ({Shot1})",
            $"Figura 1 – {Shot2}",
            $"Figura 1 – {Shot2}",
        ], captions);

        var before = ValidationKeys(docPlain, _output, "sin imágenes");
        var after = ValidationKeys(doc, null, "con imágenes");
        var nuevos = after.Except(before).ToList();
        foreach (var n in nuevos) _output.WriteLine("NUEVO: " + n);
        Assert.Empty(nuevos);
    }

    // ── T18. Texto plano: literal ────────────────────────────────────────────

    [Fact]
    public void T18_TextoPlano_LaReferenciaSaleLiteral()
    {
        var literal = $"Ver ![x](captura:{Shot2})";
        var g = Gen(MarkdownCase(t => t.Resultados = literal, formato: null));
        var baseline = Gen(MarkdownCase(t => t.Resultados = "Ver", formato: null));
        using var doc = WordprocessingDocument.Open(g.Docx, false);
        using var docBase = WordprocessingDocument.Open(baseline.Docx, false);
        Assert.Contains(BodyParagraphs(doc), p => TextOf(p) == literal);
        Assert.Equal(docBase.MainDocumentPart!.Document.Body!.Descendants<Drawing>().Count(),
            doc.MainDocumentPart!.Document.Body!.Descendants<Drawing>().Count());
    }

    // ── T19. Carrera: la captura desaparece antes del DOCX ───────────────────

    [Fact]
    public void T19_CapturaBorradaAntesDelDocx_FallaLimpio()
    {
        var dir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Dictionary<string, string>? shaBefore = null;
        var files = DefaultFiles();
        var ex = Assert.ThrowsAny<InvalidOperationException>(() => Gen(MarkdownCase(), files, d =>
        {
            shaBefore = Directory.EnumerateFiles(d).ToDictionary(p => Path.GetFileName(p),
                p => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))));
            File.Delete(Path.Combine(d, Shot2));
        }, dir));
        _output.WriteLine(ex.Message);
        Assert.Contains(Shot2, ex.Message);

        Assert.Empty(Directory.EnumerateFiles(dir, "evidencia_*.zip"));
        Assert.Empty(Directory.EnumerateFiles(dir, "informe_pericial_*.docx"));
        var remaining = Directory.EnumerateFiles(dir).ToDictionary(p => Path.GetFileName(p),
            p => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))));
        Assert.Equal(shaBefore!.Where(kv => kv.Key != Shot2).OrderBy(kv => kv.Key, StringComparer.Ordinal),
            remaining.OrderBy(kv => kv.Key, StringComparer.Ordinal));
    }

    // ── T20. Sección opcional con solo imágenes (DP1 B) ──────────────────────

    // Entre el título de la sección y el de la siguiente: la imagen y su epígrafe.
    private static void AssertSectionWithImage(WordprocessingDocument doc, string title, string next)
    {
        var paras = BodyParagraphs(doc);
        var t = IndexOfText(paras, title);
        Assert.True(t >= 0, $"Falta el título {title}");
        var end = paras.FindIndex(t + 1, p => TextOf(p).StartsWith(next, StringComparison.Ordinal));
        Assert.True(end > t, $"Falta {next}");
        var caption = paras.FindIndex(t + 1, p => TextOf(p) == $"Figura 2 – Chat con Juan ({Shot2})");
        Assert.InRange(caption, t + 2, end - 1);
        Assert.NotNull(DrawingOf(paras[caption - 1]));
    }

    [Fact]
    public void T20_SeccionOpcionalConSoloImagen_Sale()
    {
        var g = Gen(MarkdownCase(t => { t.Reserva = I1; t.Resultados = TextoSinImagen; }));
        using (var doc = WordprocessingDocument.Open(g.Docx, false))
            AssertSectionWithImage(doc, "RESERVA", "SE DEJA CONSTANCIA");

        g = Gen(MarkdownCase(t => { t.ObjetoInforme = I1; t.Resultados = TextoSinImagen; }));
        using (var doc = WordprocessingDocument.Open(g.Docx, false))
            AssertSectionWithImage(doc, "OBJETO DEL INFORME", "IDENTIFICACIÓN");

        g = Gen(MarkdownCase(t => { t.NotasTecnicas = I1; t.Resultados = TextoSinImagen; }));
        using (var doc = WordprocessingDocument.Open(g.Docx, false))
            AssertSectionWithImage(doc, "NOTAS TÉCNICAS", "RESERVA");

        g = Gen(MarkdownCase(t => t.Reserva = "&nbsp;"));
        using (var doc = WordprocessingDocument.Open(g.Docx, false))
            Assert.Equal(-1, IndexOfText(BodyParagraphs(doc), "RESERVA"));

        var obligatoria = MarkdownCase(t => t.Conclusiones = I1);
        Assert.Contains("report_texts.conclusiones", CaseValidation.ValidateForGenerate(obligatoria, true));
    }

    // ── Muestra para mirar a mano ────────────────────────────────────────────

    /// <summary>
    /// Solo corre si <c>FACTUM_RENDER_DIR</c> apunta a una carpeta (fuera del repo): deja ahí un
    /// DOCX con figuras en el cuerpo (varias secciones, una con rol, una opcional con solo una
    /// imagen) y el anexo. Datos ficticios.
    /// </summary>
    [Fact]
    public void Muestra_SiHayCarpetaDeRender()
    {
        var outDir = Environment.GetEnvironmentVariable("FACTUM_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(outDir)) return;
        Directory.CreateDirectory(outDir);

        const string chat = "screenshot_20261001_101530.png";
        const string perfil = "screenshot_20261001_101845.png";
        const string imei = "screenshot_20261001_100500.png";
        const string ajustes = "Captura de pantalla (1).jpg";
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["chat_export.txt"] = "contenido de prueba 1"u8.ToArray(),
            ["screenshot_20261001_101010.png"] = TestImages.Png(540, 1200, 0x2E, 0x7D, 0x5B),
            [chat] = TestImages.Png(540, 1200, 0x1F, 0x4E, 0x8C),
            [perfil] = TestImages.Png(1200, 540, 0xB0, 0x5A, 0x2A),
            [imei] = TestImages.Png(540, 1200, 0x55, 0x55, 0x55),
            [ajustes] = TestImages.Png(800, 600, 0x7A, 0x3E, 0x9C),
        };
        var cas = MarkdownCase(t =>
        {
            t.Resultados = "Se observa la conversación entre las partes:\n\n" +
                           ReportImageRef.Format(chat, "Chat con Juan Ficticio") +
                           "\n\nEn el perfil del contacto figura el número de línea:\n\n" +
                           ReportImageRef.Format(perfil, "Perfil del contacto") +
                           "\n\nY en la pantalla de identificación:\n\n" + ReportImageRef.Format(imei, "IMEI y modelo") +
                           "\n\n**Fin** de los resultados.";
            t.ValoracionTecnica = "La captura de los ajustes muestra la versión del sistema:\n\n" +
                                  ReportImageRef.Format(ajustes, "Ajustes del sistema");
            t.NotasTecnicas = ReportImageRef.Format(chat, "");
        });
        cas.CaptureRoles = [new CaptureRole { Filename = imei, Role = CaptureRole.ImeiModelo }];
        foreach (var (key, value) in new[] { ("resultados", cas.ReportTexts!.Resultados), ("valoracion", cas.ReportTexts.ValoracionTecnica), ("notas", cas.ReportTexts.NotasTecnicas) })
            Assert.True(ReportMarkdown.Validate(value) is null, key);

        var g = Gen(cas, files);
        File.Copy(g.Docx, Path.Combine(outDir, "informe_imagenes_en_el_cuerpo.docx"), overwrite: true);
        _output.WriteLine(outDir);
    }
}
