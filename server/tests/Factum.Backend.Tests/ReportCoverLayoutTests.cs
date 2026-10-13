using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Factum.Backend.Models;
using Factum.Backend.Services.Branding;
using Factum.Backend.Services.Reports;
using static Factum.Backend.Tests.ReportTestSupport;

namespace Factum.Backend.Tests;

/// <summary>Portada en una página (informe-formato-fixes §6.4, DA3): niveles y su aplicación en el DOCX.</summary>
public sealed class ReportCoverLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-cover-" + Guid.NewGuid().ToString("N"));

    public ReportCoverLayoutTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    // ── Datos de los anclajes de §6.4 (ficticios) ────────────────────────────

    internal static string Text(int len, string seed = "Texto ficticio de prueba ") =>
        string.Concat(Enumerable.Repeat(seed, len / seed.Length + 1))[..len];

    internal static string[] MaxContact() =>
        Enumerable.Range(1, 6).Select(i => Text(150, $"Contacto {i} ficticio, ")).ToArray();

    private static CoverInput Input(int caratula, string org, IReadOnlyList<string> contact, bool logo,
        string causa = "Expediente N° 4321/2026", string perito = "Ana Perito Ficticia · M.P. 9999") =>
        new(causa, Text(caratula), perito, org, contact, logo);

    private static readonly string[] NormalContact = [Text(40, "Calle Ficticia 123, "), Text(40, "Tel. 000 ")];

    [Fact]
    public void Normal_Nivel0()
    {
        Assert.Equal(0, ReportCoverLayout.ChooseLevel(Input(60, Text(26), NormalContact, logo: true)));
        Assert.Equal(0, ReportCoverLayout.ChooseLevel(Input(60, "", [], logo: false)));
    }

    [Fact]
    public void Caratula500_MarcaNormal_Nivel0()
    {
        var c = Input(500, Text(26), NormalContact, logo: true);
        Assert.InRange(ReportCoverLayout.EstimateTwips(c, 0), 4400, 4800); // ~4600
        Assert.Equal(0, ReportCoverLayout.ChooseLevel(c));
    }

    [Fact]
    public void Caratula500_MarcaAlMaximoConLogo_Nivel1()
    {
        var c = Input(500, Text(150), MaxContact(), logo: true);
        Assert.InRange(ReportCoverLayout.EstimateTwips(c, 0), 7500, 7900); // ~7700
        Assert.Equal(1, ReportCoverLayout.ChooseLevel(c));
    }

    [Fact]
    public void Caratula200_MarcaAlMaximoConLogo_Nivel1()
    {
        var c = Input(200, Text(150), MaxContact(), logo: true);
        Assert.InRange(ReportCoverLayout.EstimateTwips(c, 0), 6100, 6400); // ~6250, en el borde
        Assert.Equal(1, ReportCoverLayout.ChooseLevel(c));
    }

    [Fact]
    public void Caratula500_MarcaAlMaximoSinLogo_Entra()
    {
        // Medido: entra en una página hoy (nivel 0); con la estimación conservadora puede ir a 1.
        Assert.InRange(ReportCoverLayout.ChooseLevel(Input(500, Text(150), MaxContact(), logo: false)), 0, 1);
    }

    [Fact]
    public void TodoAlMaximo_Nivel2()
    {
        var c = Input(500, Text(150), MaxContact(), logo: true,
            causa: Text(300) + " N° " + Text(300), perito: Text(150) + " · M.P. " + Text(60));
        Assert.Equal(2, ReportCoverLayout.ChooseLevel(c));
        Assert.True(ReportCoverLayout.EstimateTwips(c, 2) < ReportCoverLayout.EstimateTwips(c, 1));
    }

    // ── DOCX ─────────────────────────────────────────────────────────────────

    private string Generate(Case cas, BrandingSnapshot brand) =>
        FormatFixesKit.Generate(Path.Combine(_root, Guid.NewGuid().ToString("N")), cas, brand: brand);

    internal static BrandingSnapshot MaxBrand() =>
        new(Text(150, "Estudio Ficticio de Prueba "), MaxContact(), FormatFixesKit.Image(180, 100));

    private static (string Spacer, string SubtitleAfter, string CaratulaSz) CoverAnchors(WordprocessingDocument doc)
    {
        var cover = FormatFixesKit.CoverElements(doc);
        var spacer = cover.OfType<Paragraph>().First().ParagraphProperties!.SpacingBetweenLines!;
        Assert.Equal(LineSpacingRuleValues.Exact, spacer.LineRule!.Value);
        var subtitle = cover.OfType<Paragraph>()
            .Single(p => TextOf(p) == "Inspección técnica de dispositivo móvil");
        var caratula = cover.OfType<Table>().First().Descendants<Paragraph>()
            .Single(p => TextOf(p).StartsWith("“", StringComparison.Ordinal));
        var sizes = caratula.Elements<Run>().Select(r => r.RunProperties!.FontSize!.Val!.Value).Distinct().ToList();
        var csSizes = caratula.Elements<Run>().Select(r => r.RunProperties!.FontSizeComplexScript!.Val!.Value)
            .Distinct().ToList();
        Assert.Single(sizes);
        Assert.Equal(sizes, csSizes);
        Assert.Equal(sizes[0], caratula.ParagraphProperties!.ParagraphMarkRunProperties!
            .GetFirstChild<FontSize>()!.Val!.Value);
        return (spacer.Line!.Value!, subtitle.ParagraphProperties!.SpacingBetweenLines!.After!.Value!, sizes[0]!);
    }

    [Fact]
    public void Docx_Nivel0_IgualQueLaPlantilla()
    {
        using var doc = WordprocessingDocument.Open(Generate(MakeCase(), FactumBrand()), false);
        Assert.Equal(("1800", "1650", "22"), CoverAnchors(doc));
        var contact = FormatFixesKit.CoverFooter(doc).Descendants<Run>()
            .Single(r => TextOf(r).StartsWith("Calle Ficticia", StringComparison.Ordinal));
        Assert.Equal("18", contact.RunProperties!.FontSize!.Val!.Value);
    }

    [Fact]
    public void Docx_Nivel1()
    {
        var cas = MakeCase();
        cas.Caratula = Text(500);
        using var doc = WordprocessingDocument.Open(Generate(cas, MaxBrand()), false);
        Assert.Equal(("600", "600", "20"), CoverAnchors(doc));
        var contact = FormatFixesKit.CoverFooter(doc).Descendants<Run>()
            .Single(r => TextOf(r).StartsWith("Contacto 1", StringComparison.Ordinal));
        Assert.Equal(5, contact.Elements<Break>().Count()); // una línea por renglón
        Assert.Equal("18", contact.RunProperties!.FontSize!.Val!.Value);
    }

    [Fact]
    public void Docx_Nivel2_ContactoUnido()
    {
        var cas = MakeCase();
        cas.Caratula = Text(500);
        cas.TipoCausa = Text(300);
        cas.NroReferencia = Text(300);
        cas.Perito!.Nombre = Text(150);
        cas.Perito.Matricula = Text(60);
        var brand = MaxBrand();
        using var doc = WordprocessingDocument.Open(Generate(cas, brand), false);
        Assert.Equal(("240", "240", "18"), CoverAnchors(doc));
        var contact = FormatFixesKit.CoverFooter(doc).Descendants<Run>()
            .Single(r => TextOf(r).StartsWith("Contacto 1", StringComparison.Ordinal));
        Assert.Empty(contact.Elements<Break>());
        Assert.Equal(string.Join(" · ", brand.ContactLines), TextOf(contact));
        Assert.Equal("16", contact.RunProperties!.FontSize!.Val!.Value);
        Assert.Equal("16", contact.RunProperties.FontSizeComplexScript!.Val!.Value);
        // El encabezado interior no lleva el contacto.
        Assert.DoesNotContain("Contacto 1", TextOf(FormatFixesKit.InteriorHeader(doc)));
    }
}
