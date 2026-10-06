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
/// Estructura del informe generado con la plantilla v6, diseño "Filete"
/// (Refactorizaciones/informe-diseno-v6.md §8.3, B21). Cada test genera DOCX reales con <see cref="ReportService"/> en su propia carpeta
/// temporal (Path.GetTempPath()/&lt;guid&gt;) y la borra al final: no toca Mongo ni
/// Storage:DataDirectory. Todos los datos son ficticios.
/// </summary>
public sealed class ReportDesignTests : IDisposable
{
    private const string V4 = "plantilla_informe_v4.docx";
    private const string V6 = "plantilla_informe_v6.docx";
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

    private sealed class FakeBranding(BrandingSnapshot current) : IBrandingService, IReportBrandingResolver
    {
        public BrandingSnapshot Current { get; } = current;

        // marca-por-cliente §6.6: ReportService pide la marca al resolver; acá siempre la misma.
        public Task<BrandingSnapshot> ResolveAsync(string? ownerDni, CancellationToken ct = default) =>
            Task.FromResult(Current);
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

    private string Generate(BrandingSnapshot brand, string template = V6, Case? cas = null)
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
        var result = service.GenerateAsync(cas ?? MakeCase(), files, dir).GetAwaiter().GetResult();
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

    private static bool IsRule(Paragraph? p) =>
        p?.ParagraphProperties?.ParagraphBorders?.TopBorder is not null && TextOf(p).Length == 0;

    private static readonly string[] SectionTitles =
    [
        "REFERENCIA DE LA ACTUACIÓN:", "DECLARACIÓN DE IMPARCIALIDAD Y RIGOR TÉCNICO",
        "OBJETO DEL INFORME", "IDENTIFICACIÓN", "ELEMENTOS OFRECIDOS", "OPERACIONES REALIZADAS",
        "GENERACIÓN Y ASEGURAMIENTO DE EVIDENCIA DIGITAL", "CADENA DE CUSTODIA DIGITAL",
        "RESULTADOS", "VALORACIÓN TÉCNICA", "CONCLUSIONES", "NOTAS TÉCNICAS", "RESERVA",
    ];

    private static Case MakeCaseWithTexts(bool notas, bool reserva)
    {
        var cas = MakeCase();
        cas.ReportTexts = new ReportTexts
        {
            ObjetoInforme = "Objeto ficticio.",
            NotasTecnicas = notas ? "Nota técnica ficticia." : "",
            Reserva = reserva ? "Reserva ficticia." : "",
        };
        return cas;
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

    // ── 2. Sin formas flotantes ──────────────────────────────────────────────

    [Fact]
    public void SinFormasFlotantes_EnNingunaParte()
    {
        using var doc = WordprocessingDocument.Open(
            Generate(Brand(logo: Image(1408, 768), isotype: Image(64, 64))), false);
        var main = doc.MainDocumentPart!;
        Assert.Empty(main.Document.Descendants<DWP.Anchor>());
        foreach (var h in main.HeaderParts) Assert.Empty(h.Header.Descendants<DWP.Anchor>());
        foreach (var f in main.FooterParts) Assert.Empty(f.Footer.Descendants<DWP.Anchor>());
    }

    // ── 3. Pies ──────────────────────────────────────────────────────────────

    private static int Count(string haystack, string needle) =>
        Regex.Matches(haystack, Regex.Escape(needle)).Count;

    [Fact]
    public void Pies_AtribucionUnaVez_EnLaMismaLineaQuePaginaNdeM()
    {
        using var doc = WordprocessingDocument.Open(Generate(Brand()), false);
        var sections = Sections(doc);
        var cover = FooterOf(doc, sections[0]).Footer;
        var inner = FooterOf(doc, sections[1]).Footer;

        Assert.Equal(1, Count(TextOf(cover), "Realizado con Factum"));
        Assert.Equal(1, Count(TextOf(inner), "Realizado con Factum"));
        Assert.DoesNotContain("{ATRIBUCION_FACTUM}", TextOf(inner));

        var line = Assert.Single(inner.Descendants<Paragraph>(), p => TextOf(p).Contains("Realizado con Factum"));
        var codes = string.Join("|", line.Descendants<FieldCode>().Select(f => f.Text));
        Assert.Matches(@"\bPAGE\b", codes);
        Assert.Contains("NUMPAGES", codes);
        Assert.Contains("Página", TextOf(line));
        Assert.DoesNotContain(cover.Descendants<FieldCode>(), f => f.Text.Contains("PAGE"));
    }

    // ── 4. Encabezado interior ───────────────────────────────────────────────

    [Fact]
    public void EncabezadoInterior_TextoCausaYNombre_SinDibujos()
    {
        using var doc = WordprocessingDocument.Open(Generate(Brand()), false);
        var sections = Sections(doc);
        var inner = HeaderOf(doc, sections[1]).Header;
        var cover = HeaderOf(doc, sections[0]).Header;

        Assert.Contains("Informe pericial técnico informático · Expediente N° 4321/2026", TextOf(inner));
        Assert.Contains(OrgName, TextOf(inner));
        Assert.Empty(inner.Descendants<Drawing>());
        Assert.Equal("", TextOf(cover).Trim());
    }

    // ── 5. Títulos ───────────────────────────────────────────────────────────

    private static List<Paragraph> NumberParagraphs(Body body) =>
        body.Descendants<Paragraph>()
            .Where(p => p.ParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value == 2)
            .ToList();

    [Fact]
    public void Titulos_NumeroArriba_FileteAbajo_MismoMargen()
    {
        using var doc = WordprocessingDocument.Open(
            Generate(Brand(), cas: MakeCaseWithTexts(notas: true, reserva: true)), false);
        var body = doc.MainDocumentPart!.Document.Body!;

        var numbers = NumberParagraphs(body);
        Assert.Equal(11, numbers.Count);
        Assert.All(numbers, p => Assert.Equal("", TextOf(p)));

        foreach (var title in SectionTitles)
        {
            var p = Assert.Single(body.Descendants<Paragraph>(), x => TextOf(x).Trim().ToUpperInvariant() == title);
            var pPr = p.ParagraphProperties;
            Assert.Null(pPr?.NumberingProperties);
            var left = pPr?.Indentation?.Left?.Value;
            Assert.True(left is null or "0", $"{title}: sangría {left}");
            Assert.True(IsRule(p.NextSibling<Paragraph>()), $"{title}: sin filete debajo");
        }
    }

    [Fact]
    public void Titulos_SinNotasNiReserva_NueveNumeros()
    {
        using var doc = WordprocessingDocument.Open(
            Generate(Brand(), cas: MakeCaseWithTexts(notas: false, reserva: false)), false);
        Assert.Equal(9, NumberParagraphs(doc.MainDocumentPart!.Document.Body!).Count);
    }

    // ── 6. Colores configurados ──────────────────────────────────────────────

    private static List<TableRow> HashRows(WordprocessingDocument doc) =>
        doc.MainDocumentPart!.Document.Body!.Descendants<Table>()
            .Single(t => TextOf(t).Contains("HASH SHA-256"))
            .Elements<TableRow>().ToList();

    [Fact]
    public void Colores_Configurados_ReemplazanLaPaletaDeFactum()
    {
        using var doc = WordprocessingDocument.Open(Generate(Brand()), false);
        foreach (var root in Roots(doc))
        {
            Assert.DoesNotContain(BrandingColors.DefaultPrimary, root.OuterXml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(BrandingColors.DefaultAccent, root.OuterXml, StringComparison.OrdinalIgnoreCase);
        }

        var main = doc.MainDocumentPart!;
        Assert.Contains(Primary, main.Document.OuterXml);
        Assert.Contains(Primary, main.NumberingDefinitionsPart!.Numbering.OuterXml);
        var zipRow = HashRows(doc)[^1];
        Assert.All(zipRow.Elements<TableCell>(), c =>
            Assert.Equal(Accent, c.TableCellProperties?.Shading?.Fill?.Value));
    }

    [Fact]
    public void BrandColors_Apply_DevuelveReemplazos()
    {
        var dir = Path.Combine(_root, "apply");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "v6.docx");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Templates", V6), path);
        using var doc = WordprocessingDocument.Open(path, true);
        Assert.True(BrandColors.Apply(doc.MainDocumentPart!, Primary, Accent) > 0);
        Assert.Equal(0, BrandColors.Apply(doc.MainDocumentPart!, Primary, Accent)); // ya no hay centinelas
    }

    // ── 7. Sin Branding ──────────────────────────────────────────────────────

    [Fact]
    public void SinBranding_SinMarcadoresNiImagenes_PaletaDeFactum()
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
        var main = doc.MainDocumentPart!;
        Assert.Contains(BrandingColors.DefaultPrimary, main.Document.OuterXml);
        Assert.Contains(BrandingColors.DefaultPrimary, main.NumberingDefinitionsPart!.Numbering.OuterXml);
        var zipRow = HashRows(doc)[^1];
        Assert.All(zipRow.Elements<TableCell>(), c =>
            Assert.Equal(BrandingColors.DefaultAccent, c.TableCellProperties?.Shading?.Fill?.Value));
    }

    // ── 8. Logo en la portada ────────────────────────────────────────────────

    [Fact]
    public void ConLogo_SoloEnElPieDeLaPortada_EnTabla()
    {
        using var doc = WordprocessingDocument.Open(Generate(Brand(logo: Image(1408, 768))), false);
        var sections = Sections(doc);
        var coverFooter = FooterOf(doc, sections[0]).Footer;

        var logos = coverFooter.Descendants<DWP.Inline>()
            .Where(i => i.GetFirstChild<DWP.DocProperties>()?.Name?.Value?.StartsWith("logo-organizacion") == true)
            .ToList();
        var logo = Assert.Single(logos);
        Assert.True(logo.Extent!.Cx!.Value <= 1_620_000);
        Assert.True(logo.Extent.Cy!.Value <= 900_000);
        Assert.NotEmpty(coverFooter.Descendants<Table>());

        foreach (var root in Roots(doc).Where(r => r != coverFooter))
            Assert.DoesNotContain(DrawingNames(root), n => n.StartsWith("logo-organizacion"));
        Assert.Contains(OrgName, TextOf(coverFooter));
        Assert.Contains("Calle Ficticia 123", TextOf(coverFooter));
    }

    [Fact]
    public void SinLogo_ConNombre_PieDeLaPortadaSinTabla()
    {
        using var doc = WordprocessingDocument.Open(Generate(Brand()), false);
        var coverFooter = FooterOf(doc, Sections(doc)[0]).Footer;
        Assert.Empty(coverFooter.Descendants<Table>());
        Assert.Contains(OrgName, TextOf(coverFooter));
    }

    // ── 9. Isotipo ───────────────────────────────────────────────────────────

    [Fact]
    public void ConIsotipo_SoloAlCierre()
    {
        using var doc = WordprocessingDocument.Open(Generate(Brand(isotype: Image(64, 64))), false);
        var main = doc.MainDocumentPart!;
        Assert.Contains(DrawingNames(main.Document), n => n.StartsWith("isotipo-organizacion"));
        foreach (var h in main.HeaderParts)
            Assert.DoesNotContain(DrawingNames(h.Header), n => n.StartsWith("isotipo-organizacion"));
        foreach (var f in main.FooterParts)
            Assert.DoesNotContain(DrawingNames(f.Footer), n => n.StartsWith("isotipo-organizacion"));
    }

    // ── 10. Tabla de hashes ──────────────────────────────────────────────────

    [Fact]
    public void TablaDeHashes_EncabezadoSinRelleno_FilaDelZipConTinte()
    {
        using var doc = WordprocessingDocument.Open(Generate(Brand()), false);
        var rows = HashRows(doc);
        Assert.Equal(1 + 2 + 1, rows.Count);

        foreach (var cell in rows[0].Elements<TableCell>())
        {
            Assert.Null(cell.TableCellProperties?.Shading);
            Assert.Equal(Primary, cell.TableCellProperties?.TableCellBorders?.BottomBorder?.Color?.Value);
        }

        foreach (var row in rows.Skip(1))
        {
            var hashCell = row.Elements<TableCell>().ElementAt(1);
            Assert.Matches("^[0-9a-f]{64}$", TextOf(hashCell));
            Assert.All(hashCell.Descendants<Run>(), r =>
                Assert.Equal("Courier New", r.RunProperties?.RunFonts?.Ascii?.Value));
        }

        var shaded = rows.Skip(1)
            .Where(r => r.Elements<TableCell>().Any(c => c.TableCellProperties?.Shading is not null))
            .ToList();
        var zipRow = Assert.Single(shaded);
        Assert.Same(rows[^1], zipRow);
        Assert.Contains("Contenedor de la evidencia", TextOf(zipRow));
    }

    // ── 11. Título del escrito ───────────────────────────────────────────────

    [Fact]
    public void TituloDelEscrito_SinSubrayado()
    {
        using var doc = WordprocessingDocument.Open(Generate(Brand()), false);
        var title = doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>()
            .First(p => TextOf(p).StartsWith("INFORME PERICIAL TÉCNICO INFORMÁTICO"));
        Assert.All(title.Descendants<Run>(), r => Assert.Null(r.RunProperties?.Underline));
    }

    // ── 12. Contenido idéntico a la v4 ───────────────────────────────────────

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
            sb.Append(ReportTestSupport.TextOfMaskingHashes(c));
        // Sin espacios; el hash del ZIP ya viene enmascarado por <w:t> (cambia entre generaciones).
        return Regex.Replace(sb.ToString(), @"\s+", "");
    }

    [Fact]
    public void ContenidoPericial_IgualALaV4()
    {
        var brand = Brand();
        using var v4 = WordprocessingDocument.Open(Generate(brand, V4), false);
        using var v6 = WordprocessingDocument.Open(Generate(brand, V6), false);
        var t4 = BodyTextFromTitle(v4, skipCover: false);
        var t6 = BodyTextFromTitle(v6, skipCover: true);
        Assert.StartsWith("INFORMEPERICIALTÉCNICOINFORMÁTICO", t6);
        Assert.Equal(t4, t6);
    }

    // ── 13. Validez OpenXML ──────────────────────────────────────────────────

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
        using var v6 = WordprocessingDocument.Open(Generate(brand, V6), false);
        var before = ValidationKeys(v4, _output, "v4");
        var after = ValidationKeys(v6, null, "v6");
        var nuevos = after.Except(before).ToList();
        foreach (var n in nuevos) _output.WriteLine("NUEVO v6: " + n);
        Assert.Empty(nuevos);
    }

    // ── Muestras para mirar a mano (B24) ─────────────────────────────────────

    /// <summary>
    /// Solo corre si <c>FACTUM_RENDER_DIR</c> apunta a una carpeta (fuera del repo): deja ahí
    /// cuatro DOCX de muestra con datos ficticios. Con <c>FACTUM_RENDER_LOGO</c> se puede probar
    /// un logo local sin versionarlo (va en las muestras c y d); <c>FACTUM_RENDER_PRIMARY</c> y
    /// <c>FACTUM_RENDER_ACCENT</c> pisan los colores ficticios de la muestra d.
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
        var factum = (BrandingColors.DefaultPrimary, BrandingColors.DefaultAccent);

        var samples = new (string Name, BrandingSnapshot Brand)[]
        {
            ("a_sin_branding.docx", new BrandingSnapshot(null, [], null)),
            ("b_nombre_y_contacto_sin_logo.docx", Brand(primary: factum.DefaultPrimary, accent: factum.DefaultAccent)),
            ("c_logo_nombre_y_contacto.docx", Brand(logo: logo, primary: factum.DefaultPrimary,
                accent: factum.DefaultAccent)),
            ("d_logo_isotipo_colores_ficticios.docx", Brand(logo: logo, isotype: Image(64, 64, 0xB0, 0x30, 0x30),
                primary: primary, accent: accent)),
        };
        foreach (var (name, brand) in samples)
        {
            var path = Generate(brand, cas: MakeCaseWithTexts(notas: false, reserva: false));
            File.Copy(path, Path.Combine(outDir, name), overwrite: true);
            _output.WriteLine(Path.Combine(outDir, name));
        }
    }
}
