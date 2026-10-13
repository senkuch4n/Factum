using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Branding;
using Factum.Backend.Services.Reports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Factum.Backend.Tests.ReportTestSupport;
using DWP = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace Factum.Backend.Tests;

/// <summary>
/// Kit de los tests de informe-formato-fixes (#62): genera con <see cref="ReportService"/> y la v6
/// real en una carpeta temporal propia (sin Mongo ni Storage:DataDirectory), con datos ficticios.
/// </summary>
internal static class FormatFixesKit
{
    public static readonly DateTime Stamp = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    public static BrandingLogo Image(int w, int h, byte r = 0x40, byte g = 0x80, byte b = 0xC0) =>
        new(TestImages.Png(w, h, r, g, b), "image/png", ".png", w, h, "test");

    /// <summary>Settings reales (textos por defecto de verdad), zona AR y ZIP sin cifrar.</summary>
    public static IReportSettings ArSettings() =>
        new ReportSettings(Options.Create(new ReportOptions
        {
            TimeZone = "America/Argentina/Buenos_Aires",
            EncryptZip = false,
        }), NullLogger<ReportSettings>.Instance);

    public static string Generate(string dir, Case cas, IReadOnlyDictionary<string, byte[]>? files = null,
        BrandingSnapshot? brand = null, IReportSettings? settings = null, ILogger<ReportService>? logger = null)
    {
        Directory.CreateDirectory(dir);
        var list = new List<FileInfoDto>();
        files ??= new Dictionary<string, byte[]> { ["chat_export.txt"] = "contenido de prueba 1"u8.ToArray() };
        foreach (var (name, data) in files)
        {
            var path = Path.Combine(dir, name);
            File.WriteAllBytes(path, data);
            File.SetLastWriteTimeUtc(path, Stamp);
            list.Add(new FileInfoDto(name, data.Length, "", Stamp));
        }
        var service = new ReportService(new FakeBranding(brand ?? FactumBrand()), settings ?? new FakeSettings(),
            logger ?? NullLogger<ReportService>.Instance, V6);
        return service.GenerateAsync(cas, list, dir).GetAwaiter().GetResult().PdfPath;
    }

    /// <summary>Partes XML del DOCX, decodificadas en UTF-8 estricto (un surrogate suelto no se puede codificar).</summary>
    public static Dictionary<string, string> XmlParts(string docx)
    {
        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        using var zip = ZipFile.OpenRead(docx);
        foreach (var e in zip.Entries)
        {
            if (!e.FullName.EndsWith(".xml", StringComparison.Ordinal) &&
                !e.FullName.EndsWith(".rels", StringComparison.Ordinal)) continue;
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            parts[e.FullName] = strict.GetString(ms.ToArray()).TrimStart((char)0xFEFF); // BOM de las partes que guarda el SDK
        }
        return parts;
    }

    /// <summary>Todas las partes parsean y ninguna tiene caracteres de §5.1 (ni en-US).</summary>
    public static void AssertXmlClean(string docx)
    {
        foreach (var (name, xml) in XmlParts(docx))
        {
            XDocument.Parse(xml);
            for (var i = 0; i < xml.Length; i++)
            {
                var ch = xml[i];
                Assert.False(char.IsSurrogate(ch) && !(char.IsHighSurrogate(ch) && i + 1 < xml.Length &&
                                                       char.IsLowSurrogate(xml[i + 1]))
                             && !(char.IsLowSurrogate(ch) && i > 0 && char.IsHighSurrogate(xml[i - 1])),
                    $"{name}: surrogate suelto en {i}");
                Assert.False(ch is '\uFFFE' or '\uFFFF', $"{name}: no-carácter en {i}");
                Assert.False(Factum.Backend.Common.SafeText.IsBidiControl(ch), $"{name}: bidi en {i}");
                Assert.False(ch < 0x20 && ch is not ('\t' or '\n' or '\r'), $"{name}: C0 en {i}");
            }
            Assert.DoesNotContain("w:val=\"en-US\"", xml);
        }
    }

    /// <summary>Errores de OpenXmlValidator (Microsoft 365) en document.xml y styles.xml.</summary>
    public static List<string> ValidationErrors(string docx)
    {
        using var doc = WordprocessingDocument.Open(docx, false);
        return new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(doc)
            .Where(e => e.Part?.Uri.ToString() is "/word/document.xml" or "/word/styles.xml")
            .Select(e => $"{e.Part?.Uri} {e.Id} {e.Path?.XPath} {e.Description}")
            .ToList();
    }

    public static List<Paragraph> BodyParagraphs(WordprocessingDocument doc) =>
        doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().ToList();

    public static string BodyText(WordprocessingDocument doc) =>
        string.Join("\n", BodyParagraphs(doc).Select(TextOf));

    /// <summary>header1 (el encabezado interior: el que tiene "Informe pericial técnico informático").</summary>
    public static Header InteriorHeader(WordprocessingDocument doc) =>
        doc.MainDocumentPart!.HeaderParts.Select(h => h.Header)
            .Single(h => TextOf(h).Contains("Informe pericial técnico informático", StringComparison.Ordinal));

    /// <summary>footer2 (pie de portada, con el membrete).</summary>
    public static Footer CoverFooter(WordprocessingDocument doc) =>
        doc.MainDocumentPart!.FooterParts.Select(f => f.Footer)
            .Single(f => !TextOf(f).Contains("Página", StringComparison.Ordinal));

    /// <summary>Hijos del body hasta el primer párrafo con sectPr (la portada).</summary>
    public static List<OpenXmlElement> CoverElements(WordprocessingDocument doc)
    {
        var cover = new List<OpenXmlElement>();
        foreach (var child in doc.MainDocumentPart!.Document.Body!.ChildElements)
        {
            cover.Add(child);
            if (child is Paragraph p && p.ParagraphProperties?.GetFirstChild<SectionProperties>() is not null) break;
        }
        return cover;
    }

    public static DWP.Extent ExtentOf(Paragraph p) => p.Descendants<DWP.Inline>().Single().Extent!;

    public static int TextElements(string s) => new StringInfo(s).LengthInTextElements;
}

/// <summary>informe-formato-fixes (#62) §6 y §7: arreglos del DOCX con la v6 regenerada.</summary>
public sealed class ReportFormatFixesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-fmtfix-" + Guid.NewGuid().ToString("N"));

    public ReportFormatFixesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string Dir() => Path.Combine(_root, Guid.NewGuid().ToString("N"));

    private static Case Markdown(Case cas, Action<ReportTexts>? fill = null)
    {
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
            Formato = ReportTextFormats.Markdown,
        };
        fill?.Invoke(t);
        cas.ReportTexts = t;
        return cas;
    }

    // ── H02 / H04: caracteres ────────────────────────────────────────────────

    [Fact]
    public void H02_NoCaracteres_Y_SurrogatesSueltos_SeGenera()
    {
        var cas = Markdown(MakeCase(), t => t.Resultados = "Antes\uFFFF y &#xFFFE; después.");
        cas.Caratula = "A\uFFFE B";
        cas.IntegrantesTribunal = "Dr. Uno\uD800";
        var brand = new BrandingSnapshot("Estudio", ["Tel\uFFFF 1"], null);

        var docx = FormatFixesKit.Generate(Dir(), cas, brand: brand);

        FormatFixesKit.AssertXmlClean(docx);
        using var doc = WordprocessingDocument.Open(docx, false);
        var body = FormatFixesKit.BodyText(doc);
        Assert.Contains("A B", body);
        Assert.Contains("Antes y  después.", body);
        Assert.Contains("Dr. Uno", body);
        Assert.Contains("Tel 1", TextOf(FormatFixesKit.CoverFooter(doc)));
    }

    [Fact]
    public void H04_Bidi_SeQuita_EnPortadaReferenciaYCuerpo()
    {
        var cas = Markdown(MakeCase(), t => t.Resultados = "x&#x202E;evil&#x202C;y");
        cas.Caratula = "\u202Eevil\u202C";
        cas.IntegrantesTribunal = "\u2066Dra. Dos\u2067\u2068\u2069";

        var docx = FormatFixesKit.Generate(Dir(), cas);

        FormatFixesKit.AssertXmlClean(docx);
        using var doc = WordprocessingDocument.Open(docx, false);
        var cover = string.Concat(FormatFixesKit.CoverElements(doc).Select(TextOf));
        Assert.Contains("“evil”", cover);
        var body = FormatFixesKit.BodyText(doc);
        Assert.Contains("evil", body[body.IndexOf("INFORME PERICIAL", StringComparison.Ordinal)..]);
        Assert.Contains("xevily", body);
        Assert.Contains("Dra. Dos", body);
    }

    [Fact]
    public void HebreoYArabe_SinControles_SeConservan()
    {
        const string rtl = "\u05E9\u05DC\u05D5\u05DD \u0645\u0631\u062D\u0628\u0627 \u200Fx\u200E";
        var cas = Markdown(MakeCase(), t => t.Resultados = rtl);
        cas.Caratula = rtl;

        var docx = FormatFixesKit.Generate(Dir(), cas);

        using var doc = WordprocessingDocument.Open(docx, false);
        var body = FormatFixesKit.BodyText(doc);
        Assert.Contains(rtl, body);
        Assert.True(body.Split(rtl).Length >= 3); // portada/referencia y resultados
    }

    [Fact]
    public void Enlace_ConUrlQueCambiaAlLimpiar_VaComoTexto()
    {
        var cas = Markdown(MakeCase(), t => t.Resultados = "[x](https://a.com/&#xFFFE;) y [ok](https://b.com/)");

        var docx = FormatFixesKit.Generate(Dir(), cas);

        FormatFixesKit.AssertXmlClean(docx);
        using var doc = WordprocessingDocument.Open(docx, false);
        var links = doc.MainDocumentPart!.Document.Body!.Descendants<Hyperlink>().ToList();
        Assert.Single(links); // solo el de b.com
        Assert.DoesNotContain(doc.MainDocumentPart.HyperlinkRelationships, r => r.Uri.ToString().Contains("a.com"));
    }

    // ── H01: firma ───────────────────────────────────────────────────────────

    [Fact]
    public void H01_BloqueDeFirma_KeepNext_Y_KeepLines()
    {
        var docx = FormatFixesKit.Generate(Dir(), Markdown(MakeCase()));
        using var doc = WordprocessingDocument.Open(docx, false);
        var paras = doc.MainDocumentPart!.Document.Body!.Elements<Paragraph>().ToList();
        var i = paras.FindIndex(p => TextOf(p).StartsWith("SE DEJA CONSTANCIA", StringComparison.Ordinal));
        Assert.True(i > 0);
        var block = paras.Skip(i).Take(7).ToList();
        Assert.Equal("______________________________", TextOf(block[3]));
        Assert.Equal("Ana Perito Ficticia", TextOf(block[4]));
        Assert.Equal("Perito de parte", TextOf(block[6]));
        Assert.All(block, p => Assert.NotNull(p.ParagraphProperties?.KeepNext));
        bool[] keepLines = [true, false, false, false, true, true, true];
        for (var k = 0; k < 7; k++)
            Assert.Equal(keepLines[k], block[k].ParagraphProperties!.KeepLines is not null);
        // El filete del cierre también encadena con lo que sigue.
        Assert.NotNull(paras[i + 7].ParagraphProperties?.KeepNext);
        Assert.NotNull(paras[i + 7].ParagraphProperties?.ParagraphBorders);
    }

    // ── H10 / H12 / H11: frases y sangrías ───────────────────────────────────

    [Fact]
    public void H10_SiendoLasHoras()
    {
        var docx = FormatFixesKit.Generate(Dir(), Markdown(MakeCase()));
        using var doc = WordprocessingDocument.Open(docx, false);
        var body = FormatFixesKit.BodyText(doc);
        Assert.Contains("siendo las 13:30 horas", body);
        Assert.DoesNotContain("siendo horas", body);
    }

    [Fact]
    public void H12_SinSeConstato()
    {
        var docx = FormatFixesKit.Generate(Dir(), Markdown(MakeCase()));
        using var doc = WordprocessingDocument.Open(docx, false);
        Assert.DoesNotContain(FormatFixesKit.BodyParagraphs(doc), p => TextOf(p).Trim() == "Se constató:");
    }

    [Fact]
    public void H11_NotasReservaYDesdeSuGeneracion_ConSangriaDePrimeraLinea()
    {
        var docx = FormatFixesKit.Generate(Dir(), Markdown(MakeCase()));
        using var doc = WordprocessingDocument.Open(docx, false);
        var paras = FormatFixesKit.BodyParagraphs(doc);
        foreach (var start in new[] { "Notas ficticias.", "Reserva ficticia.", "Desde su generación" })
        {
            var p = paras.First(x => TextOf(x).StartsWith(start, StringComparison.Ordinal));
            var ind = p.ParagraphProperties?.Indentation;
            Assert.Equal("2268", ind?.FirstLine?.Value);
            Assert.Null(ind?.Left);
        }
    }

    // ── H13 / H14 ────────────────────────────────────────────────────────────

    [Fact]
    public void H13_MarcaYModeloVacio_NoInformado()
    {
        var cas = Markdown(MakeCase());
        cas.Device.Manufacturer = "";
        cas.Device.Model = "";
        var docx = FormatFixesKit.Generate(Dir(), cas);
        using var doc = WordprocessingDocument.Open(docx, false);
        var row = doc.MainDocumentPart!.Document.Body!.Descendants<TableRow>()
            .Single(r => TextOf(r).StartsWith("Marca y modelo", StringComparison.OrdinalIgnoreCase));
        Assert.EndsWith("No informado", TextOf(row));
    }

    [Fact]
    public void H14_PuntoDoble_Y_DobleEspacio()
    {
        var cas = Markdown(MakeCase());
        cas.Perito!.Profesion = "Lic.";
        cas.TipoCausa = ""; // armado a mano (por API es obligatorio)
        var docx = FormatFixesKit.Generate(Dir(), cas);
        using var doc = WordprocessingDocument.Open(docx, false);
        foreach (var p in FormatFixesKit.BodyParagraphs(doc))
            Assert.DoesNotContain("..", TextOf(p));
        var body = FormatFixesKit.BodyParagraphs(doc).Select(TextOf).ToList();
        Assert.Contains(body, t => t.Contains("Lic.", StringComparison.Ordinal));
        var quevengo = body.Single(t => t.StartsWith("Que vengo", StringComparison.Ordinal));
        Assert.DoesNotContain("  ", quevengo);
        Assert.Contains("la causa N° 4321/2026", quevengo);
        Assert.DoesNotContain("  ", TextOf(FormatFixesKit.InteriorHeader(doc)));
    }

    // ── H16 / H17 ────────────────────────────────────────────────────────────

    [Fact]
    public void H16_Idioma_esAR()
    {
        var docx = FormatFixesKit.Generate(Dir(), Markdown(MakeCase()));
        var parts = FormatFixesKit.XmlParts(docx);
        var styles = XDocument.Parse(parts["word/styles.xml"]);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var lang = styles.Descendants(w + "rPrDefault").Single().Descendants(w + "lang").Single();
        Assert.Equal("es-AR", (string?)lang.Attribute(w + "val"));
        Assert.All(parts.Values, x => Assert.DoesNotContain("w:val=\"en-US\"", x));
    }

    [Fact]
    public void H17_Validador_SinErroresEnDocumentYStyles()
    {
        var brand = new BrandingSnapshot("Estudio Ficticio", ["Calle 1", "Tel. 2"], FormatFixesKit.Image(140, 76),
            FormatFixesKit.Image(64, 64));
        var docx = FormatFixesKit.Generate(Dir(), Markdown(MakeCase()), brand: brand);
        Assert.Empty(FormatFixesKit.ValidationErrors(docx));
    }

    // ── H07: imágenes ────────────────────────────────────────────────────────

    [Fact]
    public void H07_CapturasChicas_TamanoNativo_LasGrandes_Caja_ElLogo_Caja()
    {
        const string small = "screenshot_a_small.png", tall = "screenshot_b_tall.png";
        var files = new Dictionary<string, byte[]>
        {
            ["chat_export.txt"] = "x"u8.ToArray(),
            [small] = TestImages.Png(200, 150),
            [tall] = TestImages.Png(1080, 2400),
        };
        var cas = Markdown(MakeCase(), t => t.Resultados = "Captura:\n\n![](captura:" + small + ")\n\nFin.");
        var brand = new BrandingSnapshot("Estudio", ["Calle 1"], FormatFixesKit.Image(140, 76));

        var docx = FormatFixesKit.Generate(Dir(), cas, files, brand);

        using var doc = WordprocessingDocument.Open(docx, false);
        var inlines = doc.MainDocumentPart!.Document.Body!.Descendants<DWP.Inline>().ToList();
        var smalls = inlines.Where(i => i.GetFirstChild<DWP.DocProperties>()!.Name! == small).ToList();
        Assert.Equal(2, smalls.Count); // cuerpo y anexo
        Assert.All(smalls, i =>
        {
            Assert.Equal(1_905_000L, i.Extent!.Cx!.Value);
            Assert.Equal(1_428_750L, i.Extent.Cy!.Value);
        });
        var big = inlines.Single(i => i.GetFirstChild<DWP.DocProperties>()!.Name! == tall);
        Assert.Equal(3_780_000L, big.Extent!.Cy!.Value);   // manda la caja (alto)
        Assert.Equal((long)(3_780_000 / (2400.0 / 1080)), big.Extent.Cx!.Value);

        var logo = FormatFixesKit.CoverFooter(doc).Descendants<DWP.Inline>()
            .Single(i => i.GetFirstChild<DWP.DocProperties>()!.Name!.Value!.StartsWith("logo", StringComparison.Ordinal));
        Assert.Equal(1_620_000L, logo.Extent!.Cx!.Value); // caja de 4,5 cm, como hoy
    }

    // ── H06: encabezado interior ─────────────────────────────────────────────

    [Fact]
    public void H06_EncabezadoRecortado_MembreteCompleto()
    {
        var org = string.Concat(Enumerable.Repeat("Organización ", 15))[..150];
        var cas = Markdown(MakeCase());
        cas.NroReferencia = new string('7', 300);
        var docx = FormatFixesKit.Generate(Dir(), cas, brand: new BrandingSnapshot(org, ["Calle 1"], null));

        using var doc = WordprocessingDocument.Open(docx, false);
        var header = FormatFixesKit.InteriorHeader(doc);
        var paras = header.Descendants<Paragraph>().Select(TextOf).Where(t => t.Length > 0).ToList();
        var orgText = paras.Single(t => t.StartsWith("Organizaci", StringComparison.Ordinal));
        Assert.True(FormatFixesKit.TextElements(orgText) <= ReportService.HeaderOrganizationMax);
        Assert.EndsWith("…", orgText);
        var refText = paras.Single(t => t.StartsWith("Informe pericial", StringComparison.Ordinal));
        var reference = refText["Informe pericial técnico informático · ".Length..].Replace(" N° ", "");
        Assert.True(FormatFixesKit.TextElements(reference) <= ReportService.HeaderCausaMax, reference);
        Assert.EndsWith("…", refText);

        Assert.Contains(org.Trim(), TextOf(FormatFixesKit.CoverFooter(doc)));
    }

    [Fact]
    public void Ellipsize_NoParteEmojiNiZwj()
    {
        Assert.Equal("abc", ReportValues.Ellipsize("abc", 3));
        Assert.Equal("ab…", ReportValues.Ellipsize("abcd", 3));
        const string family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
        Assert.Equal("a" + family + "…", ReportValues.Ellipsize("a" + family + "bcd", 3));
        Assert.Equal("a…", ReportValues.Ellipsize("a   bcd", 3)); // TrimEnd antes del "…"
    }

    // ── H03: sistema operativo ───────────────────────────────────────────────

    [Theory]
    [InlineData("android", "Android 11", 0, "Android 11")]
    [InlineData("android", "android 11", 0, "android 11")]
    [InlineData("android", "11", 0, "Android 11")]
    [InlineData("android", "", 14, "Android 14")]
    [InlineData("android", "Android", 0, "Android")]
    [InlineData("android", "AndroidX 1", 0, "Android AndroidX 1")]
    [InlineData("ios", "17.4", 0, "iOS 17.4")]
    [InlineData("ios", "iOS 17.4", 0, "iOS 17.4")]
    [InlineData("ios", "iPadOS 17", 0, "iOS iPadOS 17")]
    public void H03_OsLabel_Idempotente(string platform, string osVersion, int androidVersion, string expected)
    {
        var d = new DeviceInfo { Platform = platform, OsVersion = osVersion, AndroidVersion = androidVersion };
        Assert.Equal(expected, d.OsLabel);
    }

    [Fact]
    public void H03_Defaults_SinAndroidAndroid()
    {
        var cas = MakeCase();
        cas.Device.OsVersion = "Android 11";
        var texts = ReportValues.RenderDefaults(cas, FormatFixesKit.ArSettings(), []);
        Assert.Contains("Android 11", texts.OperacionesRealizadas);
        Assert.DoesNotContain("Android Android", texts.OperacionesRealizadas);
    }

    // ── B-R9b ────────────────────────────────────────────────────────────────

    [Fact]
    public void RedDeSeguridad_SinCambiosConDatosNormales_NoLoguea()
    {
        var logger = new ListLogger();
        FormatFixesKit.Generate(Dir(), Markdown(MakeCase()), logger: logger);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("caracteres inválidos", StringComparison.Ordinal));
    }

    internal sealed class ListLogger : ILogger<ReportService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add($"{logLevel}: {formatter(state, exception)}");
    }
}
