using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Branding;
using Factum.Backend.Services.Reports;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;
using DWP = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace Factum.Backend.Tests;

/// <summary>
/// Estructura del informe generado con la plantilla v5 (Refactorizaciones/informe-diseno-modelo.md
/// §8.3, B21). Cada test genera DOCX reales con <see cref="ReportService"/> en su propia carpeta
/// temporal (Path.GetTempPath()/&lt;guid&gt;) y la borra al final: no toca Mongo ni
/// Storage:DataDirectory. Todos los datos son ficticios.
/// </summary>
public sealed class ReportDesignTests : IDisposable
{
    private const string V4 = "plantilla_informe_v4.docx";
    private const string V5 = "plantilla_informe_v5.docx";
    private const string Primary = "123456";
    private const string Accent = "ABCDEF";
    private const string OrgName = "Estudio Ficticio de Prueba";

    private readonly ITestOutputHelper _output;
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "factum-reportdesign-" + Guid.NewGuid().ToString("N"));

    public ReportDesignTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    // ── Fakes y fixtures ─────────────────────────────────────────────────────

    private sealed class FakeSettings : IReportSettings
    {
        public TimeZoneInfo Zone => TimeZoneInfo.Utc;
        public string ZoneLabel => "UTC";
        public string DomicilioConstituido => "";
        public bool EncryptZip => false;
        public string DefaultOperacionesRealizadas => "";
        public string DefaultAseguramientoEvidencia => "";
        public string DefaultNotasTecnicas => "";
        public string DefaultReserva => "";
    }

    private sealed class FakeBranding(BrandingSnapshot current) : IBrandingService
    {
        public BrandingSnapshot Current { get; } = current;
    }

    private static Case MakeCase() => new()
    {
        NroReferencia = "4321/2026",
        NombreDenunciante = "Titular Ficticio",
        TipoCausa = "Expediente",
        Caratula = "Ficticio, Juan c/ Ejemplo S.A. s/ daños y perjuicios",
        NombreTribunal = "Juzgado Ficticio N° 1",
        ParteDenunciante = "Juan Ficticio",
        ParteDenunciada = "Ejemplo S.A.",
        ObjetoCausa = "Daños",
        AmbitoCausa = "Civil",
        FechaIntervencion = "2026-09-15",
        NombreProponente = "Dra. Proponente Ficticia",
        TipoDispositivo = "Teléfono celular",
        LineaDispositivo = "000-0000000",
        Device = new DeviceInfo { Manufacturer = "Marca", Model = "Modelo X", Imei = "000000000000000" },
        Perito = new PeritoSnapshot
        {
            Nombre = "Ana Perito Ficticia", Matricula = "9999", Profesion = "Lic. en Informática",
            Caracter = "Perito de parte",
        },
        CreatedAt = new DateTime(2026, 9, 20, 13, 30, 0, DateTimeKind.Utc),
    };

    private static BrandingSnapshot Brand(string? name = OrgName, BrandingLogo? logo = null,
        BrandingLogo? isotype = null, string primary = Primary, string accent = Accent) =>
        new(name, name is null ? [] : ["Calle Ficticia 123", "Tel. 000 000"], logo, isotype, primary, accent);

    private static BrandingLogo Image(int w, int h, byte r = 0x40, byte g = 0x80, byte b = 0xC0) =>
        new(TestImages.Png(w, h, r, g, b), "image/png", ".png", w, h, "test");

    private string Generate(BrandingSnapshot brand, string template = V5)
    {
        var dir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var stamp = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var files = new List<FileInfoDto>();
        foreach (var (name, content) in new[]
                 {
                     ("chat_export.txt", "contenido de prueba 1"),
                     ("registro.log", "contenido de prueba 2"),
                 })
        {
            var path = Path.Combine(dir, name);
            File.WriteAllText(path, content);
            File.SetLastWriteTimeUtc(path, stamp);
            files.Add(new FileInfoDto(name, content.Length, "", stamp));
        }

        var service = new ReportService(new FakeBranding(brand), new FakeSettings(),
            NullLogger<ReportService>.Instance, template);
        var result = service.GenerateAsync(MakeCase(), files, dir).GetAwaiter().GetResult();
        return result.PdfPath;
    }

    private static List<SectionProperties> Sections(WordprocessingDocument doc) =>
        doc.MainDocumentPart!.Document.Body!.Descendants<SectionProperties>().ToList();

    private static HeaderPart HeaderOf(WordprocessingDocument doc, SectionProperties s) =>
        (HeaderPart)doc.MainDocumentPart!.GetPartById(s.Elements<HeaderReference>()
            .First(r => (r.Type?.Value ?? HeaderFooterValues.Default) == HeaderFooterValues.Default).Id!.Value!);

    private static FooterPart FooterOf(WordprocessingDocument doc, SectionProperties s) =>
        (FooterPart)doc.MainDocumentPart!.GetPartById(s.Elements<FooterReference>()
            .First(r => (r.Type?.Value ?? HeaderFooterValues.Default) == HeaderFooterValues.Default).Id!.Value!);

    private static string TextOf(OpenXmlElement e) => string.Concat(e.Descendants<Text>().Select(t => t.Text));

    private static IEnumerable<OpenXmlPartRootElement> Roots(WordprocessingDocument doc)
    {
        var main = doc.MainDocumentPart!;
        yield return main.Document;
        foreach (var h in main.HeaderParts) yield return h.Header;
        foreach (var f in main.FooterParts) yield return f.Footer;
        if (main.NumberingDefinitionsPart?.Numbering is { } n) yield return n;
        if (main.StyleDefinitionsPart?.Styles is { } s) yield return s;
    }

    private static List<string> DrawingNames(OpenXmlElement root) =>
        root.Descendants<DWP.DocProperties>().Select(d => d.Name?.Value ?? "").ToList();

    // ── 1. Estructura ────────────────────────────────────────────────────────

    [Fact]
    public void Estructura_DosSecciones_SinTitlePg()
    {
        using var doc = WordprocessingDocument.Open(Generate(Brand()), false);
        var body = doc.MainDocumentPart!.Document.Body!;
        var sections = Sections(doc);

        Assert.Equal(2, sections.Count);
        Assert.IsType<ParagraphProperties>(sections[0].Parent);
        Assert.Same(body.LastChild, sections[1]);
        Assert.NotSame(HeaderOf(doc, sections[0]), HeaderOf(doc, sections[1]));
        Assert.NotSame(FooterOf(doc, sections[0]), FooterOf(doc, sections[1]));
        Assert.All(sections, s => Assert.Null(s.GetFirstChild<TitlePage>()));
    }

    // ── 2. Pies ──────────────────────────────────────────────────────────────

    [Fact]
    public void Pies_AtribucionEnAmbos_PaginaNdeMSoloEnInterior()
    {
        using var doc = WordprocessingDocument.Open(Generate(Brand()), false);
        var sections = Sections(doc);
        var cover = FooterOf(doc, sections[0]).Footer;
        var inner = FooterOf(doc, sections[1]).Footer;

        Assert.Contains("Realizado con Factum", TextOf(cover));
        Assert.Contains("Realizado con Factum", TextOf(inner));

        var innerCodes = string.Join("|", inner.Descendants<FieldCode>().Select(f => f.Text));
        Assert.Matches(@"\bPAGE\b", innerCodes);
        Assert.Contains("NUMPAGES", innerCodes);
        Assert.Contains("Página", TextOf(inner));
        Assert.DoesNotContain(cover.Descendants<FieldCode>(), f => f.Text.Contains("PAGE"));
    }

    // ── 3. Banda ─────────────────────────────────────────────────────────────

    [Fact]
    public void Banda_TituloCausaYFormas()
    {
        using var doc = WordprocessingDocument.Open(Generate(Brand()), false);
        var sections = Sections(doc);
        var band = HeaderOf(doc, sections[1]).Header;
        var stripe = HeaderOf(doc, sections[0]).Header;

        Assert.Contains("INFORME PERICIAL TÉCNICO INFORMÁTICO", TextOf(band));
        Assert.Contains("Expediente N° 4321/2026", TextOf(band));
        Assert.True(band.Descendants<DWP.Anchor>().Count() >= 2);
        Assert.True(stripe.Descendants<DWP.Anchor>().Count() >= 4);
        Assert.Equal("", TextOf(stripe).Trim());
    }

    // ── 4. Colores configurados ──────────────────────────────────────────────

    [Fact]
    public void Colores_Configurados_ReemplazanLosCentinelas()
    {
        using var doc = WordprocessingDocument.Open(Generate(Brand()), false);
        foreach (var root in Roots(doc))
        {
            Assert.DoesNotContain(BrandColors.PrimarySentinel, root.OuterXml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(BrandColors.AccentSentinel, root.OuterXml, StringComparison.OrdinalIgnoreCase);
        }

        var sections = Sections(doc);
        var main = doc.MainDocumentPart!;
        Assert.Contains(Primary, main.Document.OuterXml);
        Assert.Contains(Primary, main.NumberingDefinitionsPart!.Numbering.OuterXml);
        foreach (var s in sections)
        {
            var header = HeaderOf(doc, s).Header.OuterXml;
            Assert.Contains(Primary, header);
            Assert.Contains(Accent, header);
        }
    }

    [Fact]
    public void BrandColors_Apply_DevuelveReemplazos()
    {
        var dir = Path.Combine(_root, "apply");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "v5.docx");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Templates", V5), path);
        using var doc = WordprocessingDocument.Open(path, true);
        Assert.True(BrandColors.Apply(doc.MainDocumentPart!, Primary, Accent) > 0);
        Assert.Equal(0, BrandColors.Apply(doc.MainDocumentPart!, Primary, Accent)); // ya no hay centinelas
    }

    // ── 5. Sin Branding ──────────────────────────────────────────────────────

    [Fact]
    public void SinBranding_SinMarcadoresNiImagenes_ColoresNeutros()
    {
        using var doc = WordprocessingDocument.Open(Generate(new BrandingSnapshot(null, [], null)), false);
        var leftover = new Regex(@"\{[#/]?[A-Za-z_]");
        foreach (var root in Roots(doc))
        {
            foreach (var p in root.Descendants<Paragraph>())
                Assert.DoesNotMatch(leftover, TextOf(p));
            Assert.DoesNotContain(DrawingNames(root), n => n.StartsWith("logo-organizacion") || n.StartsWith("isotipo-organizacion"));
        }

        var sections = Sections(doc);
        Assert.Equal("Realizado con Factum", TextOf(FooterOf(doc, sections[0]).Footer).Trim());
        Assert.Contains(BrandColors.PrimarySentinel, doc.MainDocumentPart!.Document.OuterXml);
        Assert.Contains(BrandColors.AccentSentinel, HeaderOf(doc, sections[1]).Header.OuterXml);
    }

    // ── 6. Nombre sin isotipo ────────────────────────────────────────────────

    [Fact]
    public void NombreSinIsotipo_NombreEnLaBanda()
    {
        using var doc = WordprocessingDocument.Open(Generate(Brand()), false);
        var band = HeaderOf(doc, Sections(doc)[1]).Header;
        Assert.Contains(OrgName, TextOf(band));
        Assert.DoesNotContain(DrawingNames(band), n => n.StartsWith("isotipo-organizacion"));
        Assert.DoesNotContain(DrawingNames(doc.MainDocumentPart!.Document), n => n.StartsWith("isotipo-organizacion"));
    }

    // ── 7. Con isotipo ───────────────────────────────────────────────────────

    [Fact]
    public void ConIsotipo_EnLaBandaYAlCierre_SinNombreEnLaBanda()
    {
        using var doc = WordprocessingDocument.Open(Generate(Brand(isotype: Image(64, 64))), false);
        var band = HeaderOf(doc, Sections(doc)[1]).Header;
        Assert.Contains(DrawingNames(band), n => n.StartsWith("isotipo-organizacion"));
        Assert.Contains(DrawingNames(doc.MainDocumentPart!.Document), n => n.StartsWith("isotipo-organizacion"));
        Assert.DoesNotContain(OrgName, TextOf(band));
    }

    // ── 8. Con logo ──────────────────────────────────────────────────────────

    [Fact]
    public void ConLogo_SoloEnElPieDeLaPortada()
    {
        using var doc = WordprocessingDocument.Open(Generate(Brand(logo: Image(1408, 768))), false);
        var sections = Sections(doc);
        var coverFooter = FooterOf(doc, sections[0]).Footer;

        var logos = coverFooter.Descendants<DWP.Inline>()
            .Where(i => i.GetFirstChild<DWP.DocProperties>()?.Name?.Value?.StartsWith("logo-organizacion") == true)
            .ToList();
        var logo = Assert.Single(logos);
        Assert.True(logo.Extent!.Cx!.Value <= 2_520_000);
        Assert.True(logo.Extent.Cy!.Value <= 1_368_000);

        foreach (var root in Roots(doc).Where(r => r != coverFooter))
            Assert.DoesNotContain(DrawingNames(root), n => n.StartsWith("logo-organizacion"));
        Assert.Contains(OrgName, TextOf(coverFooter));
        Assert.Contains("Calle Ficticia 123", TextOf(coverFooter));
    }

    // ── 9. Tabla de hashes ───────────────────────────────────────────────────

    [Fact]
    public void TablaDeHashes_EncabezadoSombreado_HashMonoespaciado()
    {
        using var doc = WordprocessingDocument.Open(Generate(Brand()), false);
        var table = doc.MainDocumentPart!.Document.Body!.Descendants<Table>()
            .Single(t => TextOf(t).Contains("HASH SHA-256"));
        var rows = table.Elements<TableRow>().ToList();
        Assert.Equal(1 + 2 + 1, rows.Count);

        foreach (var cell in rows[0].Elements<TableCell>())
            Assert.Equal(Primary, cell.TableCellProperties?.Shading?.Fill?.Value);

        foreach (var row in rows.Skip(1))
        {
            var hashCell = row.Elements<TableCell>().ElementAt(1);
            Assert.Matches("^[0-9a-f]{64}$", TextOf(hashCell));
            Assert.All(hashCell.Descendants<Run>(), r =>
                Assert.Equal("Courier New", r.RunProperties?.RunFonts?.Ascii?.Value));
        }
    }

    // ── 10. Contenido idéntico a la v4 ───────────────────────────────────────

    private static string BodyTextFromTitle(WordprocessingDocument doc, bool skipCover)
    {
        var body = doc.MainDocumentPart!.Document.Body!;
        var children = body.ChildElements.ToList();
        var start = 0;
        if (skipCover)
        {
            var coverEnd = children.FindIndex(c => c is Paragraph p &&
                p.ParagraphProperties?.GetFirstChild<SectionProperties>() is not null);
            Assert.True(coverEnd >= 0);
            start = coverEnd + 1;
        }
        var sb = new StringBuilder();
        foreach (var c in children.Skip(start))
            foreach (var t in c.Descendants<Text>())
                sb.Append(t.Text);
        // Sin espacios; el hash del ZIP se enmascara por las dudas (el contenido es el mismo).
        var s = Regex.Replace(sb.ToString(), @"\s+", "");
        return Regex.Replace(s, "[0-9a-f]{64}", "<hash>");
    }

    [Fact]
    public void ContenidoPericial_IgualALaV4()
    {
        var brand = Brand();
        using var v4 = WordprocessingDocument.Open(Generate(brand, V4), false);
        using var v5 = WordprocessingDocument.Open(Generate(brand, V5), false);
        var t4 = BodyTextFromTitle(v4, skipCover: false);
        var t5 = BodyTextFromTitle(v5, skipCover: true);
        Assert.StartsWith("INFORMEPERICIALTÉCNICOINFORMÁTICO", t5);
        Assert.Equal(t4, t5);
    }

    // ── 11. Validez OpenXML ──────────────────────────────────────────────────

    private static HashSet<string> ValidationKeys(WordprocessingDocument doc, ITestOutputHelper? output, string label)
    {
        var validator = new OpenXmlValidator(FileFormatVersions.Office2019);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in validator.Validate(doc))
        {
            // Sin los índices posicionales: la portada corre la posición de todo el cuerpo.
            var path = Regex.Replace(e.Path?.XPath ?? "", @"\[\d+\]", "");
            var key = $"{e.Part?.Uri} {e.Id} {path}";
            if (keys.Add(key)) output?.WriteLine($"{label}: {key} — {e.Description}");
        }
        return keys;
    }

    [Fact]
    public void Validez_SinErroresNuevosRespectoDeLaV4()
    {
        var brand = Brand(logo: Image(140, 76), isotype: Image(64, 64));
        using var v4 = WordprocessingDocument.Open(Generate(brand, V4), false);
        using var v5 = WordprocessingDocument.Open(Generate(brand, V5), false);
        var before = ValidationKeys(v4, _output, "v4");
        var after = ValidationKeys(v5, null, "v5");
        var nuevos = after.Except(before).ToList();
        foreach (var n in nuevos) _output.WriteLine("NUEVO v5: " + n);
        Assert.Empty(nuevos);
    }

    // ── Muestras para mirar a mano (B24) ─────────────────────────────────────

    /// <summary>
    /// Solo corre si <c>FACTUM_RENDER_DIR</c> apunta a una carpeta (fuera del repo): deja ahí
    /// tres DOCX de muestra con datos ficticios. Con <c>FACTUM_RENDER_LOGO</c>,
    /// <c>FACTUM_RENDER_PRIMARY</c> y <c>FACTUM_RENDER_ACCENT</c> se puede probar una identidad
    /// local sin versionarla.
    /// </summary>
    [Fact]
    public void Muestras_SiHayCarpetaDeRender()
    {
        var outDir = Environment.GetEnvironmentVariable("FACTUM_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(outDir)) return;
        Directory.CreateDirectory(outDir);

        BrandingLogo? logo = Image(140, 76);
        var logoPath = Environment.GetEnvironmentVariable("FACTUM_RENDER_LOGO");
        if (!string.IsNullOrWhiteSpace(logoPath))
        {
            var data = File.ReadAllBytes(logoPath);
            Assert.True(ImageProbe.TryDetect(data, out var ct, out var w, out var h));
            logo = new BrandingLogo(data, ct, ct == "image/png" ? ".png" : ".jpg", w, h, "render");
        }
        var primary = BrandingColors.Normalize(Environment.GetEnvironmentVariable("FACTUM_RENDER_PRIMARY")) ?? Primary;
        var accent = BrandingColors.Normalize(Environment.GetEnvironmentVariable("FACTUM_RENDER_ACCENT")) ?? Accent;

        var samples = new (string Name, BrandingSnapshot Brand)[]
        {
            ("a_sin_branding.docx", new BrandingSnapshot(null, [], null)),
            ("b_nombre_y_colores_sin_isotipo.docx", Brand(logo: logo, primary: primary, accent: accent)),
            ("c_logo_e_isotipo.docx", Brand(logo: logo, isotype: Image(64, 64, 0xB0, 0x30, 0x30),
                primary: primary, accent: accent)),
        };
        foreach (var (name, brand) in samples)
        {
            var path = Generate(brand);
            File.Copy(path, Path.Combine(outDir, name), overwrite: true);
            _output.WriteLine(Path.Combine(outDir, name));
        }
    }
}
