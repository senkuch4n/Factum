using System.IO.Compression;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Factum.Backend.Models;
using Factum.Backend.Services.Reports;
using Xunit.Abstractions;
using static Factum.Backend.Tests.ReportTestSupport;

namespace Factum.Backend.Tests;

/// <summary>
/// Markdown → DOCX con la plantilla v6 (editor-texto-enriquecido §6.3, T11-T21). Cada test
/// genera en su propia carpeta temporal y la borra al final: no toca Mongo ni
/// Storage:DataDirectory. Datos ficticios.
/// </summary>
public sealed class ReportMarkdownDocxTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "factum-mddocx-" + Guid.NewGuid().ToString("N"));

    public ReportMarkdownDocxTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string GenerateCase(Case cas) => Generate(Path.Combine(_root, Guid.NewGuid().ToString("N")), cas);

    private static Case CaseWithTexts(string? formato, Action<ReportTexts> fill)
    {
        var cas = MakeCase();
        var t = new ReportTexts
        {
            ObjetoInforme = "Objeto ficticio.",
            OperacionesRealizadas = "Operaciones ficticias.",
            AseguramientoEvidencia = "Aseguramiento ficticio.",
            Resultados = "Resultados ficticios.",
            ValoracionTecnica = "Valoración ficticia.",
            Conclusiones = "Conclusiones ficticias.",
            NotasTecnicas = "Notas ficticias.",
            Reserva = "Reserva ficticia.",
            Formato = formato,
        };
        fill(t);
        cas.ReportTexts = t;
        return cas;
    }

    private static int? NumId(Paragraph p) => p.ParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value;

    /// <summary>Párrafos generados de una sección: después del filete del título, hasta el número de la siguiente.</summary>
    private static List<Paragraph> Section(Body body, string title, int skip = 0)
    {
        var titlePara = body.Elements<Paragraph>().Single(p => TextOf(p).Trim().ToUpperInvariant() == title);
        var result = new List<Paragraph>();
        var e = titlePara.NextSibling<Paragraph>()?.NextSibling(); // salta el filete
        for (; e is not null; e = e.NextSibling())
        {
            if (e is not Paragraph p) break;
            if (NumId(p) == 2) break;
            if (p.ParagraphProperties?.SectionProperties is not null) break;
            result.Add(p);
        }
        return result.Skip(skip).ToList();
    }

    private static string TemplatePath(string name) => Path.Combine(AppContext.BaseDirectory, "Templates", name);

    private static byte[] ZipEntry(string path, string entry)
    {
        using var zip = ZipFile.OpenRead(path);
        using var s = zip.GetEntry(entry)!.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static RunProperties PlaceholderRunProperties(string placeholder)
    {
        using var doc = WordprocessingDocument.Open(TemplatePath(V6), false);
        var para = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>()
            .Single(p => TextOf(p).Trim() == placeholder);
        return (RunProperties)para.Descendants<Run>().First(r => r.Elements<Text>().Any(t => t.Text.Length > 0))
            .RunProperties!.CloneNode(true);
    }

    private static AbstractNum AbstractOf(WordprocessingDocument doc, int numId)
    {
        var numbering = doc.MainDocumentPart!.NumberingDefinitionsPart!.Numbering;
        var abstractId = numbering.Elements<NumberingInstance>().Single(n => n.NumberID!.Value == numId)
            .AbstractNumId!.Val!.Value;
        return numbering.Elements<AbstractNum>().Single(a => a.AbstractNumberId!.Value == abstractId);
    }

    // ── T11. Caso viejo sin marca: sale exactamente como antes ───────────────

    private const string PlainValoracion =
        "Texto con *asteriscos* y archivo_de_prueba_1.txt\n# no es título\n`no es código`\n{caratula} literal\n- uno\n- dos\n\nDespués de la línea vacía.";

    // Golden: un párrafo por línea, texto literal (lo que generaba ReplaceParagraphPerLine antes de esta HU).
    private static readonly string[] GoldenValoracion =
    [
        "Texto con *asteriscos* y archivo_de_prueba_1.txt", "# no es título", "`no es código`",
        "{caratula} literal", "- uno", "- dos", "", "Después de la línea vacía.",
    ];

    [Fact]
    public void T11_CasoViejoSinMarca_IgualQueAntes()
    {
        var path = GenerateCase(CaseWithTexts(null, t => t.ValoracionTecnica = PlainValoracion));
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart!.Document.Body!;
        var paras = Section(body, "VALORACIÓN TÉCNICA");

        Assert.Equal(GoldenValoracion, paras.Select(TextOf).ToArray());
        var rPr = PlaceholderRunProperties("{descripcionValoracionTecnica}");
        foreach (var p in paras)
        {
            Assert.Null(p.ParagraphProperties?.NumberingProperties);
            Assert.Empty(p.Elements<Hyperlink>());
            var runs = p.Elements<Run>().ToList();
            if (TextOf(p).Length == 0) { Assert.Empty(runs); continue; }
            var run = Assert.Single(runs);
            Assert.Equal(rPr.OuterXml, run.RunProperties!.OuterXml);
        }

        // numbering.xml byte a byte el de la plantilla (con los colores de Factum B-R2b no lo toca).
        Assert.Equal(ZipEntry(TemplatePath(V6), "word/numbering.xml"), ZipEntry(path, "word/numbering.xml"));
        Assert.Null(doc.MainDocumentPart!.DocumentSettingsPart!.Settings
            .GetFirstChild<Compatibility>()?.DoNotExpandShiftReturn);
    }

    [Fact]
    public void T11_CasoViejo_CuerpoIgualAlDeUnCasoSinMarcaDelMismoTexto()
    {
        // Mismo texto plano, con y sin la propiedad presente en el objeto: idéntico.
        var a = GenerateCase(CaseWithTexts(null, t => t.ValoracionTecnica = PlainValoracion));
        var b = GenerateCase(CaseWithTexts("texto-desconocido-no-markdown", t => t.ValoracionTecnica = PlainValoracion));
        using var da = WordprocessingDocument.Open(a, false);
        using var db = WordprocessingDocument.Open(b, false);
        Assert.Equal(TextOfMaskingHashes(da.MainDocumentPart!.Document.Body!),
            TextOfMaskingHashes(db.MainDocumentPart!.Document.Body!));
    }

    // ── Caso con todos los formatos ──────────────────────────────────────────

    private const string AllFormats =
        "Párrafo con **negrita**, *cursiva*, <u>subrayado</u> y ***<u>bi</u>***.\n\n" +
        "### Subtítulo de prueba\n\n" +
        "- uno\n  - dos\n    - tres\n      - cuatro\n\n" +
        "&nbsp;\n\n" +
        "3. tercero\n4. cuarto\n\n" +
        "> Una cita con `código`.\n\n" +
        "````\nlínea 1\n<div>x</div>\n```\n````\n\n" +
        "Ver [sitio](https://ejemplo.com/a), [https://x.com](https://x.com) y [correo](mailto:perito@ejemplo.com).\n\n" +
        "**{caratula}** archivo\\_de\\_prueba\\_1.txt\n\n2 \\* 3 = 6\n\nExpte. #123\n\n" +
        "Línea uno  \nLínea dos";

    private WordprocessingDocument OpenAllFormats(out string path)
    {
        path = GenerateCase(CaseWithTexts(ReportTextFormats.Markdown, t =>
        {
            t.ValoracionTecnica = AllFormats;
            t.Conclusiones = "1. Primera\n2. Segunda";
        }));
        return WordprocessingDocument.Open(path, false);
    }

    private static Paragraph ParaStartingWith(List<Paragraph> paras, string text) =>
        paras.Single(p => TextOf(p).StartsWith(text, StringComparison.Ordinal));

    private static Run RunWithText(OpenXmlElement e, string text) =>
        e.Descendants<Run>().Single(r => TextOf(r) == text);

    // ── T12. Negrita, cursiva, subrayado ─────────────────────────────────────

    [Fact]
    public void T12_NegritaCursivaSubrayado()
    {
        using var doc = OpenAllFormats(out _);
        var paras = Section(doc.MainDocumentPart!.Document.Body!, "VALORACIÓN TÉCNICA");
        var p = ParaStartingWith(paras, "Párrafo con negrita");
        Assert.Equal("Párrafo con negrita, cursiva, subrayado y bi.", TextOf(p));

        var bold = RunWithText(p, "negrita").RunProperties!;
        Assert.NotNull(bold.Bold);
        Assert.Null(bold.Italic);
        Assert.Equal("Arial", bold.RunFonts?.Ascii?.Value);
        Assert.NotNull(RunWithText(p, "cursiva").RunProperties!.Italic);
        Assert.Equal(UnderlineValues.Single, RunWithText(p, "subrayado").RunProperties!.Underline!.Val!.Value);
        var bi = RunWithText(p, "bi").RunProperties!;
        Assert.NotNull(bi.Bold);
        Assert.NotNull(bi.Italic);
        Assert.Equal(UnderlineValues.Single, bi.Underline!.Val!.Value);
        // Texto normal: sin marcas.
        var plain = RunWithText(p, "Párrafo con ").RunProperties!;
        Assert.Null(plain.Bold);
        Assert.Null(plain.Underline);
        // Primer párrafo de nivel superior: conserva la sangría de primera línea de la plantilla.
        Assert.Equal("2268", p.ParagraphProperties!.Indentation!.FirstLine!.Value);
    }

    // ── T13. Viñetas de 3 niveles + 4.º aplanado ─────────────────────────────

    [Fact]
    public void T13_Viñetas_TresNivelesYCuartoAplanado()
    {
        using var doc = OpenAllFormats(out _);
        var paras = Section(doc.MainDocumentPart!.Document.Body!, "VALORACIÓN TÉCNICA");
        var items = new[] { "uno", "dos", "tres", "cuatro" }.Select(t => paras.Single(p => TextOf(p) == t)).ToList();

        Assert.Equal(new[] { 0, 1, 2, 2 },
            items.Select(p => p.ParagraphProperties!.NumberingProperties!.NumberingLevelReference!.Val!.Value).ToArray());
        Assert.Equal(new[] { "360", "720", "1080", "1080" },
            items.Select(p => p.ParagraphProperties!.Indentation!.Left!.Value!).ToArray());
        Assert.All(items, p =>
        {
            Assert.Equal("360", p.ParagraphProperties!.Indentation!.Hanging!.Value);
            Assert.Null(p.ParagraphProperties.Indentation.FirstLine);
        });

        var abs = AbstractOf(doc, NumId(items[0])!.Value);
        var levels = abs.Elements<Level>().ToList();
        Assert.Equal(9, levels.Count);
        Assert.All(levels, l => Assert.Equal(NumberFormatValues.Bullet, l.NumberingFormat!.Val!.Value));
        Assert.Equal(new[] { "•", "◦", "▪", "▪" },
            levels.Take(4).Select(l => l.LevelText!.Val!.Value!).ToArray());
        Assert.All(levels, l =>
            Assert.Equal(ReportListNumbering.InkColor, l.NumberingSymbolRunProperties!.GetFirstChild<Color>()!.Val!.Value));
    }

    // ── T14. Numerada con start, numIds distintos por lista ──────────────────

    [Fact]
    public void T14_Numerada_StartOverride_YNumIdsDistintos()
    {
        using var doc = OpenAllFormats(out _);
        var body = doc.MainDocumentPart!.Document.Body!;
        var tercero = Section(body, "VALORACIÓN TÉCNICA").Single(p => TextOf(p) == "tercero");
        var primera = Section(body, "CONCLUSIONES").Single(p => TextOf(p) == "Primera");

        var numId = NumId(tercero)!.Value;
        var abs = AbstractOf(doc, numId);
        Assert.Equal(NumberFormatValues.Decimal, abs.Elements<Level>().First().NumberingFormat!.Val!.Value);
        Assert.Equal("%1.", abs.Elements<Level>().First().LevelText!.Val!.Value);
        var instance = doc.MainDocumentPart!.NumberingDefinitionsPart!.Numbering.Elements<NumberingInstance>()
            .Single(n => n.NumberID!.Value == numId);
        Assert.Equal(3, instance.Elements<LevelOverride>().Single().StartOverrideNumberingValue!.Val!.Value);

        Assert.NotEqual(numId, NumId(primera));
        Assert.Equal(AbstractOf(doc, NumId(primera)!.Value).AbstractNumberId!.Value, abs.AbstractNumberId!.Value);
    }

    // ── T15. Numeración romana intacta ───────────────────────────────────────

    [Fact]
    public void T15_NumeracionRomanaIntacta()
    {
        using var doc = OpenAllFormats(out _);
        var body = doc.MainDocumentPart!.Document.Body!;
        Assert.Equal(11, body.Descendants<Paragraph>().Count(p => NumId(p) == 2));

        using var template = WordprocessingDocument.Open(TemplatePath(V6), false);
        var original = template.MainDocumentPart!.NumberingDefinitionsPart!.Numbering;
        var generated = doc.MainDocumentPart!.NumberingDefinitionsPart!.Numbering;
        foreach (var a in original.Elements<AbstractNum>())
            Assert.Equal(a.OuterXml, generated.Elements<AbstractNum>()
                .Single(x => x.AbstractNumberId!.Value == a.AbstractNumberId!.Value).OuterXml);
        foreach (var n in original.Elements<NumberingInstance>())
            Assert.Equal(n.OuterXml, generated.Elements<NumberingInstance>()
                .Single(x => x.NumberID!.Value == n.NumberID!.Value).OuterXml);

        // Orden del esquema: todos los abstractNum antes del primer w:num.
        var children = generated.ChildElements.ToList();
        Assert.True(children.FindLastIndex(c => c is AbstractNum) < children.FindIndex(c => c is NumberingInstance));
    }

    // ── T16. Código ──────────────────────────────────────────────────────────

    [Fact]
    public void T16_CodigoEnLineaYBloque()
    {
        using var doc = OpenAllFormats(out _);
        var paras = Section(doc.MainDocumentPart!.Document.Body!, "VALORACIÓN TÉCNICA");

        var inline = RunWithText(ParaStartingWith(paras, "Una cita"), "código").RunProperties!;
        Assert.Equal("Courier New", inline.RunFonts!.Ascii!.Value);
        Assert.Equal("20", inline.FontSize!.Val!.Value);

        var block = ParaStartingWith(paras, "línea 1");
        Assert.Equal("línea 1<div>x</div>```", TextOf(block));
        var pPr = block.ParagraphProperties!;
        Assert.Equal("EEF0F2", pPr.Shading!.Fill!.Value);
        Assert.Equal(JustificationValues.Left, pPr.Justification!.Val!.Value);
        Assert.Equal("240", pPr.SpacingBetweenLines!.Line!.Value);
        Assert.Equal("0", pPr.Indentation!.FirstLine!.Value);
        var run = Assert.Single(block.Elements<Run>());
        Assert.Equal("Courier New", run.RunProperties!.RunFonts!.Ascii!.Value);
        Assert.Equal("18", run.RunProperties.FontSize!.Val!.Value);
        Assert.Equal(2, run.Elements<Break>().Count());
    }

    // ── T17. Cita y subtítulo ────────────────────────────────────────────────

    [Fact]
    public void T17_CitaYSubtitulo()
    {
        using var doc = OpenAllFormats(out _);
        var paras = Section(doc.MainDocumentPart!.Document.Body!, "VALORACIÓN TÉCNICA");

        var quote = ParaStartingWith(paras, "Una cita");
        var border = quote.ParagraphProperties!.ParagraphBorders!.LeftBorder!;
        Assert.Equal("D9DDE1", border.Color!.Value);
        Assert.Equal("567", quote.ParagraphProperties.Indentation!.Left!.Value);
        Assert.Equal("3D444C", RunWithText(quote, "Una cita con ").RunProperties!.Color!.Val!.Value);

        var heading = ParaStartingWith(paras, "Subtítulo de prueba");
        Assert.All(heading.Elements<Run>(), r => Assert.NotNull(r.RunProperties!.Bold));
        Assert.Null(heading.ParagraphProperties!.NumberingProperties);
        Assert.NotNull(heading.ParagraphProperties.KeepNext);
        Assert.Equal(JustificationValues.Left, heading.ParagraphProperties.Justification!.Val!.Value);
    }

    // ── T18. Enlaces ─────────────────────────────────────────────────────────

    [Fact]
    public void T18_Enlaces()
    {
        using var doc = OpenAllFormats(out _);
        var main = doc.MainDocumentPart!;
        var p = ParaStartingWith(Section(main.Document.Body!, "VALORACIÓN TÉCNICA"), "Ver ");
        Assert.Equal("Ver sitio (https://ejemplo.com/a), https://x.com y correo (perito@ejemplo.com).", TextOf(p));

        var links = p.Elements<Hyperlink>().ToList();
        Assert.Equal(3, links.Count);
        string Target(Hyperlink h) => main.HyperlinkRelationships.Single(r => r.Id == h.Id!.Value).Uri.OriginalString;
        Assert.Equal("https://ejemplo.com/a", Target(links[0]));
        Assert.Equal("sitio", TextOf(links[0]));
        Assert.Equal("https://x.com", TextOf(links[1]));
        Assert.Equal("mailto:perito@ejemplo.com", Target(links[2]));
        Assert.All(main.HyperlinkRelationships, r => Assert.True(r.IsExternal));
        var linkRun = links[0].Elements<Run>().Single().RunProperties!;
        Assert.Equal(ReportMarkdownRenderer.LinkColor, linkRun.Color!.Val!.Value);
        Assert.Equal(UnderlineValues.Single, linkRun.Underline!.Val!.Value);
        // El paréntesis va fuera del hipervínculo y sin subrayado.
        var paren = RunWithText(p, " (https://ejemplo.com/a)");
        Assert.IsType<Paragraph>(paren.Parent);
        Assert.Null(paren.RunProperties!.Underline);
    }

    // ── T19. Literales ───────────────────────────────────────────────────────

    [Fact]
    public void T19_Literales_SinReemplazoNiFormato()
    {
        using var doc = OpenAllFormats(out _);
        var paras = Section(doc.MainDocumentPart!.Document.Body!, "VALORACIÓN TÉCNICA");

        var p = ParaStartingWith(paras, "{caratula}");
        Assert.Equal("{caratula} archivo_de_prueba_1.txt", TextOf(p));
        Assert.NotNull(RunWithText(p, "{caratula}").RunProperties!.Bold);
        Assert.All(p.Elements<Run>().Skip(1), r => { Assert.Null(r.RunProperties!.Bold); Assert.Null(r.RunProperties.Italic); });

        foreach (var text in new[] { "2 * 3 = 6", "Expte. #123" })
        {
            var q = paras.Single(x => TextOf(x) == text);
            Assert.All(q.Descendants<RunProperties>(), r => { Assert.Null(r.Bold); Assert.Null(r.Italic); });
            Assert.Equal("2268", q.ParagraphProperties!.Indentation!.FirstLine!.Value);
        }

        var hard = ParaStartingWith(paras, "Línea uno");
        Assert.Single(hard.Descendants<Break>());
        Assert.Equal("Línea unoLínea dos", TextOf(hard));
        // Con un salto manual en un párrafo justificado, la línea anterior no se estira.
        Assert.NotNull(doc.MainDocumentPart!.DocumentSettingsPart!.Settings
            .GetFirstChild<Compatibility>()!.DoNotExpandShiftReturn);
    }

    // ── T20. &nbsp; ──────────────────────────────────────────────────────────

    [Fact]
    public void T20_Nbsp_VaciosDelMedioYExtremos()
    {
        var path = GenerateCase(CaseWithTexts(ReportTextFormats.Markdown, t =>
        {
            t.Conclusiones = "&nbsp;\n\nA\n\n&nbsp;\n\nB\n\n&nbsp;";
            t.NotasTecnicas = "&nbsp;";
            t.ValoracionTecnica = "  sangría  \n segunda";
        }));
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart!.Document.Body!;

        var conclusiones = Section(body, "CONCLUSIONES");
        Assert.Equal(new[] { "A", "", "B" }, conclusiones.Select(TextOf).ToArray());
        Assert.Empty(conclusiones[1].Elements<Run>());

        Assert.DoesNotContain(body.Elements<Paragraph>(), p => TextOf(p).Trim().ToUpperInvariant() == "NOTAS TÉCNICAS");

        // D10: la sangría hecha a mano (U+00A0 al principio de cada línea) se conserva.
        var valoracion = Section(body, "VALORACIÓN TÉCNICA").Single();
        Assert.Equal("  sangría segunda", TextOf(valoracion));
    }

    // ── T21. Validez OpenXML ─────────────────────────────────────────────────

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
    public void T21_Validez_SinErroresNuevos()
    {
        var plain = GenerateCase(CaseWithTexts(null, t => t.ValoracionTecnica = PlainValoracion));
        using var before = WordprocessingDocument.Open(plain, false);
        using var after = OpenAllFormats(out _);
        var b = ValidationKeys(before, _output, "plano");
        var a = ValidationKeys(after, null, "markdown");
        var nuevos = a.Except(b).ToList();
        foreach (var n in nuevos) _output.WriteLine("NUEVO markdown: " + n);
        Assert.Empty(nuevos);
    }

    // ── Muestra para mirar a mano ────────────────────────────────────────────

    /// <summary>
    /// Solo corre si <c>FACTUM_RENDER_DIR</c> apunta a una carpeta (fuera del repo): deja ahí un
    /// DOCX con todos los formatos (datos ficticios) y el mismo caso en texto plano.
    /// </summary>
    [Fact]
    public void Muestra_SiHayCarpetaDeRender()
    {
        var outDir = Environment.GetEnvironmentVariable("FACTUM_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(outDir)) return;
        Directory.CreateDirectory(outDir);

        OpenAllFormats(out var md).Dispose();
        File.Copy(md, Path.Combine(outDir, "editor_markdown_todos_los_formatos.docx"), overwrite: true);
        var plain = GenerateCase(CaseWithTexts(null, t => t.ValoracionTecnica = PlainValoracion));
        File.Copy(plain, Path.Combine(outDir, "editor_caso_viejo_texto_plano.docx"), overwrite: true);
        _output.WriteLine(outDir);
    }
}
