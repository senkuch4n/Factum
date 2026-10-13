using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Factum.Backend.Models;
using Factum.Backend.Services.Branding;
using Factum.Backend.Services.Reports;
using Xunit.Abstractions;
using static Factum.Backend.Tests.ReportTestSupport;
using DWP = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace Factum.Backend.Tests;

/// <summary>
/// Repetición de los 11 informes del QA de formato del 2026-10-13 (docs/qa/2026-10-13/informe-formato.md),
/// más los dos casos de portada de §6.4 (informe-formato-fixes §11.1). Datos ficticios armados acá,
/// carpeta temporal propia, sin Mongo ni Storage:DataDirectory.
/// <para>
/// Renders (opcionales): con <c>FACTUM_RENDER_DIR</c> (carpeta FUERA del repo) se copia ahí cada DOCX;
/// con además <c>FACTUM_SOFFICE=1</c> y <c>soffice</c> en el PATH se convierte a PDF y se verifica que
/// la portada ocupe una página y que la firma no se parta (con <c>pdftotext</c>, si está).
/// </para>
/// </summary>
public sealed class ReportQaRegressionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-qa62-" + Guid.NewGuid().ToString("N"));

    public ReportQaRegressionTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private const string TitleDoc = "INFORME PERICIAL TÉCNICO INFORMÁTICO";
    private const string FirmaLinea = "______________________________";

    // ── Datos ────────────────────────────────────────────────────────────────

    private static string Text(int len, string seed = "Texto ficticio de prueba ") => ReportCoverLayoutTests.Text(len, seed);

    private static readonly Dictionary<(int, int), byte[]> PngCache = [];

    private static byte[] Png(int w, int h)
    {
        lock (PngCache)
        {
            if (!PngCache.TryGetValue((w, h), out var data))
                PngCache[(w, h)] = data = TestImages.Png(w, h, (byte)(w % 251), (byte)(h % 241), 0x90);
            return data;
        }
    }

    private static ReportTexts Texts(string? resultados = null, string? formato = ReportTextFormats.Markdown,
        bool optionals = true) => new()
    {
        ObjetoInforme = optionals ? "Objeto ficticio del informe." : "",
        OperacionesRealizadas = "Operaciones ficticias.",
        AseguramientoEvidencia = "Aseguramiento ficticio.",
        Resultados = resultados ?? "Resultados ficticios.",
        ValoracionTecnica = "Valoración ficticia.",
        Conclusiones = "Conclusiones ficticias.",
        NotasTecnicas = optionals ? "Notas ficticias." : "",
        Reserva = optionals ? "Reserva ficticia." : "",
        Formato = formato,
    };

    private static ReportTexts FromDefaults(Case cas, IReportSettings settings, Dictionary<string, byte[]> files,
        string resultados)
    {
        var list = files.Select(f => new DTOs.FileInfoDto(f.Key, f.Value.Length, "", FormatFixesKit.Stamp));
        var d = ReportValues.RenderDefaults(cas, settings, list);
        return new ReportTexts
        {
            ObjetoInforme = "Objeto ficticio del informe.",
            OperacionesRealizadas = d.OperacionesRealizadas!,
            AseguramientoEvidencia = d.AseguramientoEvidencia!,
            Resultados = resultados,
            ValoracionTecnica = "Valoración ficticia.",
            Conclusiones = "Conclusiones ficticias.",
            NotasTecnicas = d.NotasTecnicas!,
            Reserva = d.Reserva!,
            Formato = ReportTextFormats.Markdown,
        };
    }

    private static BrandingSnapshot NoBrand() => new(null, [], null);

    private static BrandingSnapshot NormalBrand() =>
        new("Estudio Ficticio de Prueba", ["Calle Ficticia 123, Ciudad Ficticia", "Tel. 000 0000 · correo@ejemplo.test"],
            FormatFixesKit.Image(180, 100), FormatFixesKit.Image(64, 64), "1F4E79", "DCE9F5");

    private sealed record QaCase(Case Case, Dictionary<string, byte[]> Files, BrandingSnapshot Brand,
        IReportSettings Settings);

    private static Dictionary<string, byte[]> TextFiles() => new(StringComparer.Ordinal)
    {
        ["chat_export.txt"] = "contenido de prueba 1"u8.ToArray(),
        ["registro.log"] = "contenido de prueba 2"u8.ToArray(),
    };

    private static QaCase C1Tipico()
    {
        var settings = FormatFixesKit.ArSettings();
        var cas = MakeCase();
        cas.SalaTribunal = "Sala II";
        cas.Integrantes = ["Dra. Ficticia Uno", "Dr. Ficticio Dos", "Ignacio Íñiguez"];
        cas.IntegrantesTribunal = "Dra. Ficticia Uno, Dr. Ficticio Dos e Ignacio Íñiguez";
        cas.OrganismoTribunal = "Poder Judicial Ficticio";
        cas.Device.Platform = "android";
        cas.Device.OsVersion = "Android 14";
        var files = TextFiles();
        files["screenshot_imei.png"] = Png(1080, 2400);
        files["screenshot_nombre.png"] = Png(1080, 2400);
        files["screenshot_01.png"] = Png(1080, 2400);
        files["screenshot_02.png"] = Png(2400, 1080);
        files["screenshot_03.png"] = Png(720, 1600);
        cas.CaptureRoles =
        [
            new CaptureRole { Filename = "screenshot_imei.png", Role = CaptureRole.ImeiModelo },
            new CaptureRole { Filename = "screenshot_nombre.png", Role = CaptureRole.NombreDispositivo },
        ];
        cas.ReportTexts = FromDefaults(cas, settings, files,
            "Se constató lo siguiente:\n\n![Chat con el titular](captura:screenshot_02.png)\n\nFin.");
        return new QaCase(cas, files, NormalBrand(), settings);
    }

    private static QaCase C2rMinimoReal()
    {
        var cas = MakeCase();
        cas.OrganismoTribunal = "";
        cas.SalaTribunal = "";
        cas.IntegrantesTribunal = "";
        cas.ObjetoCausa = "";
        cas.AmbitoCausa = "";
        cas.LineaDispositivo = "";
        cas.Device.Manufacturer = "";
        cas.Device.Model = "";
        cas.ReportTexts = Texts(optionals: false);
        var files = TextFiles();
        files["screenshot_imei.png"] = Png(720, 1600);
        cas.CaptureRoles = [new CaptureRole { Filename = "screenshot_imei.png", Role = CaptureRole.ImeiModelo }];
        return new QaCase(cas, files, NoBrand(), new FakeSettings());
    }

    private static QaCase C2Minimo()
    {
        var cas = MakeCase();
        cas.NroReferencia = cas.NombreDenunciante = cas.TipoCausa = cas.Caratula = "";
        cas.NombreTribunal = cas.ParteDenunciante = cas.ParteDenunciada = cas.FechaIntervencion = "";
        cas.NombreProponente = cas.TipoDispositivo = "";
        cas.Device.Imei = "";
        cas.Perito = new PeritoSnapshot();
        cas.ReportTexts = Texts(resultados: "", optionals: false);
        cas.ReportTexts.OperacionesRealizadas = cas.ReportTexts.AseguramientoEvidencia = "";
        cas.ReportTexts.ValoracionTecnica = cas.ReportTexts.Conclusiones = "";
        return new QaCase(cas, TextFiles(), NoBrand(), new FakeSettings());
    }

    private static QaCase C2bVacioTotal() =>
        new(new Case { CreatedAt = new DateTime(2026, 9, 20, 13, 30, 0, DateTimeKind.Utc) }, TextFiles(), NoBrand(),
            new FakeSettings());

    // Carátula de 500 con emoji, RTL, U+202E, HTML y comillas; controles U+0001/0007/000B/007F/0085 y tab.
    private const string Extremos =
        "Ficticio \U0001F4F1 c/ “Ejemplo” <b>x</b> <script>alert(1)</script> \u202Eevil\u202C " +
        "\u05E9\u05DC\u05D5\u05DD \u0645\u0631\u062D\u0628\u0627 ctl\u0001\u0007\u000B\u007F\u0085\tfin ";

    private const string MarkdownCompleto =
        "# Título 1\n\n## Título 2\n\n### Título 3\n\n" +
        "Párrafo con **negrita**, *itálica*, ***ambas***, <u>subrayado</u> y `código`.\n\n" +
        "- uno\n  - dos\n    - tres\n      - cuatro\n\n" +
        "1. a\n   1. b\n2. c\n\nPárrafo del medio.\n\n1. reinicia\n\n" +
        "> cita ficticia\n\n| Col A | Col B |\n|---|---|\n| 1 | 2 |\n\n" +
        "```\ncódigo en bloque\n```\n\n---\n\n" +
        "[enlace](https://example.com) y <https://example.org> \U0001F600 \U0001F468\u200D\U0001F469\u200D\U0001F467 " +
        "\U0001F1E6\U0001F1F7\n\n\\<b\\>x\\</b\\> \\<script\\>\n\n" +
        "palabra";

    private static QaCase C3Extremos()
    {
        var cas = MakeCase();
        cas.Caratula = string.Concat(Enumerable.Repeat(Extremos, 8))[..500];
        cas.ParteDenunciante = Text(300);
        cas.Integrantes = Enumerable.Range(1, 11).Select(i => $"Dr. Integrante Ficticio {i}").ToList();
        cas.IntegrantesTribunal = string.Join(", ", cas.Integrantes[..10]) + " y " + cas.Integrantes[10];
        cas.ReportTexts = Texts(resultados: MarkdownCompleto + new string('x', 150));
        var brand = new BrandingSnapshot(Text(150, "Estudio Ficticio de Prueba "), ReportCoverLayoutTests.MaxContact(),
            FormatFixesKit.Image(180, 100), FormatFixesKit.Image(64, 64));
        return new QaCase(cas, TextFiles(), brand, new FakeSettings());
    }

    private static QaCase C3bTextoPlano()
    {
        var cas = MakeCase();
        cas.Caratula = "\u202Eevil\u202C";
        cas.ReportTexts = Texts(resultados: "Línea con *asteriscos* literales\n\nOtra línea\n- no es lista", formato: null);
        return new QaCase(cas, TextFiles(), NormalBrand(), new FakeSettings());
    }

    private static QaCase C3cHtmlCrudo()
    {
        var cas = MakeCase();
        cas.ReportTexts = Texts(resultados: "Antes <b>negrita cruda</b> <script>alert(1)</script> después");
        return new QaCase(cas, TextFiles(), NormalBrand(), new FakeSettings());
    }

    private static QaCase C3xFffeCaratula()
    {
        var cas = MakeCase();
        cas.Caratula = "A\uFFFEB";
        cas.ReportTexts = Texts();
        return new QaCase(cas, TextFiles(), NormalBrand(), new FakeSettings());
    }

    private static QaCase C3xD800()
    {
        var cas = MakeCase();
        cas.Caratula = "A\uD800B";
        cas.ReportTexts = Texts();
        return new QaCase(cas, TextFiles(), NormalBrand(), new FakeSettings());
    }

    private static QaCase C3xFfffMarkdown()
    {
        var cas = MakeCase();
        cas.ReportTexts = Texts(resultados: "Texto con \uFFFF en markdown y &#xFFFF;.");
        return new QaCase(cas, TextFiles(), NormalBrand(), new FakeSettings());
    }

    private static readonly (int W, int H)[] C4Sizes =
    [
        (1080, 2400), (2400, 1080), (1000, 1000), (200, 150), (4000, 3000), (1080, 6000), (3000, 300),
        (720, 1600), (1440, 3200), (150, 900),
    ];

    private static QaCase C4Capturas()
    {
        var cas = MakeCase();
        var files = TextFiles();
        for (var i = 0; i < 34; i++)
        {
            var (w, h) = C4Sizes[i % C4Sizes.Length];
            var jpg = i % 7 == 6; // algunas JPG (mínimas, sin datos de imagen)
            files[$"screenshot_{i + 1:00}_{w}x{h}.{(jpg ? "jpg" : "png")}"] = jpg ? TestImages.Jpeg(w, h) : Png(w, h);
        }
        files["screenshot_imei.png"] = Png(1080, 2400);
        files["screenshot_nombre.png"] = Png(720, 1600);
        cas.CaptureRoles =
        [
            new CaptureRole { Filename = "screenshot_imei.png", Role = CaptureRole.ImeiModelo },
            new CaptureRole { Filename = "screenshot_nombre.png", Role = CaptureRole.NombreDispositivo },
        ];
        // Una PNG de cada tamaño elegido, en el cuerpo.
        var body = new[] { "1080x2400", "200x150", "150x900", "3000x300", "1000x1000" }
            .Select(size => files.Keys.First(k => k.EndsWith($"_{size}.png", StringComparison.Ordinal))).ToArray();
        cas.ReportTexts = Texts(resultados: string.Join("\n\n", body.Select(b => $"Figura:\n\n![{b}](captura:{b})")));
        return new QaCase(cas, files, NormalBrand(), new FakeSettings());
    }

    private static QaCase C5(string fecha)
    {
        var cas = MakeCase();
        cas.FechaIntervencion = fecha;
        cas.CreatedAt = new DateTime(2025, 1, 1, 2, 30, 0, DateTimeKind.Utc);
        cas.ReportTexts = Texts();
        return new QaCase(cas, TextFiles(), NormalBrand(), FormatFixesKit.ArSettings());
    }

    private static QaCase CoverMaxLogo()
    {
        var cas = MakeCase();
        cas.Caratula = Text(500);
        cas.ReportTexts = Texts();
        return new QaCase(cas, TextFiles(), ReportCoverLayoutTests.MaxBrand(), new FakeSettings());
    }

    private static QaCase CoverAllMax()
    {
        var cas = MakeCase();
        cas.Caratula = Text(500);
        cas.TipoCausa = Text(300);
        cas.NroReferencia = Text(300, "Número ficticio ");
        cas.Perito!.Nombre = Text(150, "Perito Ficticio ");
        cas.Perito.Matricula = Text(60, "M ");
        cas.ReportTexts = Texts();
        return new QaCase(cas, TextFiles(), ReportCoverLayoutTests.MaxBrand(), new FakeSettings());
    }

    // ── Generación + asserts comunes ─────────────────────────────────────────

    private string Run(string id, QaCase q)
    {
        var docx = FormatFixesKit.Generate(Path.Combine(_root, id), q.Case, q.Files, q.Brand, q.Settings);
        FormatFixesKit.AssertXmlClean(docx);
        var errors = FormatFixesKit.ValidationErrors(docx);
        foreach (var e in errors) _output.WriteLine($"{id}: {e}");
        Assert.Empty(errors);
        Render(id, docx);
        return docx;
    }

    private static string CoverSpacer(WordprocessingDocument doc) =>
        FormatFixesKit.CoverElements(doc).OfType<Paragraph>().First().ParagraphProperties!.SpacingBetweenLines!.Line!.Value!;

    private static void AssertFirmaKeepNext(WordprocessingDocument doc)
    {
        var paras = doc.MainDocumentPart!.Document.Body!.Elements<Paragraph>().ToList();
        var i = paras.FindIndex(p => TextOf(p) == FirmaLinea);
        Assert.True(i > 0);
        for (var k = i - 3; k <= i + 3; k++) Assert.NotNull(paras[k].ParagraphProperties?.KeepNext);
    }

    [Fact]
    public void C1_Tipico()
    {
        using var doc = WordprocessingDocument.Open(Run("c1_tipico", C1Tipico()), false);
        Assert.Equal("1800", CoverSpacer(doc));
        var body = FormatFixesKit.BodyText(doc);
        Assert.Contains("sistema operativo Android 14", body);
        Assert.DoesNotContain("Android Android", body);
        var figures = FormatFixesKit.BodyParagraphs(doc).Select(TextOf)
            .Where(t => t.StartsWith("Figura ", StringComparison.Ordinal)).ToList();
        Assert.Equal(["Figura 2 – Chat con el titular (screenshot_02.png)", "Figura 1 – screenshot_01.png",
            "Figura 2 – screenshot_02.png", "Figura 3 – screenshot_03.png"], figures);
        AssertFirmaKeepNext(doc);
    }

    [Fact]
    public void C2r_MinimoReal()
    {
        using var doc = WordprocessingDocument.Open(Run("c2r_minimo_real", C2rMinimoReal()), false);
        var row = doc.MainDocumentPart!.Document.Body!.Descendants<TableRow>()
            .Single(r => TextOf(r).StartsWith("Marca y modelo", StringComparison.OrdinalIgnoreCase));
        Assert.EndsWith("No informado", TextOf(row));
        var texts = FormatFixesKit.BodyParagraphs(doc).Select(TextOf).ToList();
        Assert.DoesNotContain("NOTAS TÉCNICAS", texts);
        Assert.DoesNotContain("RESERVA", texts);
        Assert.DoesNotContain(texts, t => t.Contains("Objeto ficticio", StringComparison.Ordinal));
        AssertFirmaKeepNext(doc);
    }

    [Fact]
    public void C2_Minimo_Y_C2b_VacioTotal_NoLanzan_SinDobleEspacio()
    {
        foreach (var (id, q) in new[] { ("c2_minimo", C2Minimo()), ("c2b_vacio_total", C2bVacioTotal()) })
        {
            using var doc = WordprocessingDocument.Open(Run(id, q), false);
            foreach (var t in FormatFixesKit.BodyParagraphs(doc).Select(TextOf))
                Assert.False(t.Contains("  ", StringComparison.Ordinal), $"{id}: \"{t}\"");
            Assert.DoesNotContain("  ", TextOf(FormatFixesKit.InteriorHeader(doc)));
        }
    }

    [Fact]
    public void C3_Extremos()
    {
        var q = C3Extremos();
        var docx = Run("c3_extremos", q);
        var xml = FormatFixesKit.XmlParts(docx)["word/document.xml"];
        Assert.Contains("\u007F", xml); // DEL y C1 se conservan (XML 1.0 válido)
        Assert.Contains("\u0085", xml);
        using var doc = WordprocessingDocument.Open(docx, false);
        Assert.Contains(CoverSpacer(doc), new[] { "600", "240" });
        var org = FormatFixesKit.InteriorHeader(doc).Descendants<Paragraph>().Select(TextOf)
            .Single(t => t.StartsWith("Estudio", StringComparison.Ordinal));
        Assert.EndsWith("…", org);
        Assert.Contains(" evil ", string.Concat(FormatFixesKit.CoverElements(doc).Select(TextOf)));
    }

    [Fact]
    public void C3b_TextoPlano()
    {
        using var doc = WordprocessingDocument.Open(Run("c3b_textoplano", C3bTextoPlano()), false);
        var texts = FormatFixesKit.BodyParagraphs(doc).Select(TextOf).ToList();
        Assert.Contains("Línea con *asteriscos* literales", texts);
        Assert.Contains("- no es lista", texts);
    }

    [Fact]
    public void C3c_HtmlCrudo()
    {
        using var doc = WordprocessingDocument.Open(Run("c3c_html_crudo", C3cHtmlCrudo()), false);
        Assert.Contains("<script>alert(1)</script>", FormatFixesKit.BodyText(doc));
    }

    [Fact]
    public void C3x_LosTresQueAntesLanzaban_SeGeneran()
    {
        using (var d1 = WordprocessingDocument.Open(Run("c3x_fffe_caratula", C3xFffeCaratula()), false))
            Assert.Contains("AB", FormatFixesKit.BodyText(d1));
        using (var d2 = WordprocessingDocument.Open(Run("c3x_d800", C3xD800()), false))
            Assert.Contains("AB", FormatFixesKit.BodyText(d2));
        using (var d3 = WordprocessingDocument.Open(Run("c3x_ffff_markdown", C3xFfffMarkdown()), false))
            Assert.Contains("Texto con  en markdown y .", FormatFixesKit.BodyText(d3));
    }

    [Fact]
    public void C4_Capturas()
    {
        using var doc = WordprocessingDocument.Open(Run("c4_capturas", C4Capturas()), false);
        var inlines = doc.MainDocumentPart!.Document.Body!.Descendants<DWP.Inline>()
            .Where(i => i.GetFirstChild<DWP.DocProperties>()!.Name!.Value!.StartsWith("screenshot", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(34 + 2 + 5, inlines.Count);
        foreach (var inline in inlines)
        {
            var name = inline.GetFirstChild<DWP.DocProperties>()!.Name!.Value!;
            var m = Regex.Match(name, @"_(\d+)x(\d+)\.");
            if (!m.Success) continue; // identificación
            int w = int.Parse(m.Groups[1].Value), h = int.Parse(m.Groups[2].Value);
            var cx = inline.Extent!.Cx!.Value;
            var cy = inline.Extent.Cy!.Value;
            if ((w, h) is (200, 150))
            {
                // Chica: tamaño nativo a 96 dpi (antes se ampliaba a 14 cm).
                Assert.True(w * 9_525L == cx && h * 9_525L == cy, $"{name}: {cx}x{cy}");
            }
            else if ((w, h) is (150, 900))
            {
                // A 96 dpi mediría 23,8 cm de alto: manda la caja (10,5 cm) y nunca se amplía.
                Assert.True(cy == 3_780_000 && cx <= w * 9_525L, $"{name}: {cx}x{cy}");
            }
            else
            {
                // Igual que hoy: manda la caja de 14 × 10,5 cm.
                Assert.True(cx == 5_040_000 || cy == 3_780_000, $"{name}: {cx}x{cy}");
            }
        }
    }

    [Fact]
    public void C5_Fechas()
    {
        using (var d = WordprocessingDocument.Open(Run("c5_fechas_bisiesto_finanio", C5("2024-02-29")), false))
        {
            var body = FormatFixesKit.BodyText(d);
            Assert.Contains("29/02/2024", body);
            Assert.Contains("En fecha 31/12/2024, siendo las 23:30 horas", body);
        }
        using (var d = WordprocessingDocument.Open(Run("c5b_fecha_invalida", C5("2025-02-29")), false))
            Assert.Contains("2025-02-29", FormatFixesKit.BodyText(d));
        using (var d = WordprocessingDocument.Open(Run("c5c_fecha_ddmm", C5("15/09/2026")), false))
            Assert.Contains("15/09/2026", FormatFixesKit.BodyText(d));
    }

    [Fact]
    public void Cover_MaxLogo_Y_AllMax()
    {
        using (var d = WordprocessingDocument.Open(Run("cover_max_logo", CoverMaxLogo()), false))
            Assert.Equal("600", CoverSpacer(d));
        using (var d = WordprocessingDocument.Open(Run("cover_all_max", CoverAllMax()), false))
            Assert.Equal("240", CoverSpacer(d));
    }

    // ── Renders opcionales (§11.1) ───────────────────────────────────────────

    private static readonly string[] OnePageCover = ["c3_extremos", "cover_max_logo", "cover_all_max"];

    private void Render(string id, string docx)
    {
        var dir = Environment.GetEnvironmentVariable("FACTUM_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        var copy = Path.Combine(dir, id + ".docx");
        File.Copy(docx, copy, overwrite: true);
        if (Environment.GetEnvironmentVariable("FACTUM_SOFFICE") != "1") return;
        var soffice = Which("soffice");
        if (soffice is null)
        {
            _output.WriteLine("soffice no está en el PATH: se omite el render");
            return;
        }

        var profile = new Uri(Path.Combine(dir, ".lo-profile")).AbsoluteUri;
        Exec(soffice, $"-env:UserInstallation={profile} --headless --convert-to pdf --outdir \"{dir}\" \"{copy}\"",
            TimeSpan.FromMinutes(3));
        var pdf = Path.Combine(dir, id + ".pdf");
        Assert.True(File.Exists(pdf), $"{id}: soffice no generó el PDF");

        var pdftotext = Which("pdftotext");
        if (pdftotext is null)
        {
            _output.WriteLine("pdftotext no está en el PATH: se omiten los chequeos por página");
            return;
        }
        var pages = PdfPages(pdftotext, pdf);
        _output.WriteLine($"{id}: {pages.Count} páginas");
        if (OnePageCover.Contains(id))
        {
            var titlePage = pages.FindIndex(p => p.Contains(TitleDoc, StringComparison.Ordinal));
            Assert.True(titlePage == 1, $"{id}: el título del escrito está en la página {titlePage + 1} (portada en más de una página)");
        }
        if (id == "c2r_minimo_real")
        {
            var firma = pages.FindIndex(p => p.Contains(FirmaLinea, StringComparison.Ordinal));
            Assert.True(firma >= 0);
            Assert.Contains("Ana Perito Ficticia", pages[firma]);
            Assert.Contains("Perito de parte", pages[firma]);
        }
    }

    private static List<string> PdfPages(string pdftotext, string pdf)
    {
        var txt = Path.ChangeExtension(pdf, ".txt");
        Exec(pdftotext, $"-layout \"{pdf}\" \"{txt}\"", TimeSpan.FromMinutes(1));
        var pages = File.ReadAllText(txt, Encoding.UTF8).Split('\f').ToList();
        if (pages.Count > 0 && pages[^1].Trim().Length == 0) pages.RemoveAt(pages.Count - 1);
        File.Delete(txt);
        return pages;
    }

    private static string? Which(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Select(d => Path.Combine(d, exe)).FirstOrDefault(File.Exists);

    private static void Exec(string file, string args, TimeSpan timeout)
    {
        using var p = Process.Start(new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeout))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException($"{Path.GetFileName(file)} no terminó a tiempo");
        }
        Assert.True(p.ExitCode == 0, $"{Path.GetFileName(file)} salió con {p.ExitCode}: {stderr.Result}");
        _ = stdout.Result;
    }
}
