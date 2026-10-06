using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Branding;
using Factum.Backend.Services.Reports;
using Microsoft.Extensions.Logging.Abstractions;
using static Factum.Backend.Tests.Branding.BrandingTestData;
using static Factum.Backend.Tests.ReportTestSupport;
using DWP = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace Factum.Backend.Tests.Branding;

/// <summary>
/// marca-por-cliente B23 (pedido explícito): un <see cref="ReportService"/> real con la plantilla v6 y el
/// resolver real sobre el repositorio en memoria. El DOCX sale con la marca del DUEÑO del caso
/// (<c>Officer.Dni</c>), en los dos flujos, y sin la de otra cuenta ni la de la instalación. Carpeta temporal
/// propia; nada de Mongo ni de <c>Storage:DataDirectory</c>.
/// </summary>
public sealed class ReportOwnerBrandingDocxTests : IDisposable
{
    private const string NameA = "Estudio Ficticio A";
    private const string NameB = "Perito Ficticio B";
    private const string NameInstallation = "Instalación Ficticia";
    private const string PrimaryA = "1F3A93";
    private const string AccentA = "E6ECFA";
    private const string PrimaryB = "7A1F1F";
    private const string PrimaryInstallation = "203040";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-ownerbrand-" + Guid.NewGuid().ToString("N"));
    private readonly ReportService _service;

    public ReportOwnerBrandingDocxTests()
    {
        Directory.CreateDirectory(_root);
        var repo = new InMemoryAccountBrandingRepository();
        repo.Seed(new AccountBranding
        {
            Id = DniA, OrganizationName = NameA, ContactLines = ["Contacto Ficticio A"],
            PrimaryColor = PrimaryA, AccentColor = AccentA, Logo = Image(TestImages.Png(64, 32)),
        });
        repo.Seed(new AccountBranding
        {
            Id = DniB, OrganizationName = NameB, ContactLines = ["Contacto Ficticio B"], PrimaryColor = PrimaryB,
        });
        var installation = new BrandingSnapshot(NameInstallation, ["Contacto Instalación"], null, null,
            PrimaryInstallation, BrandingColors.DefaultAccent);
        var resolver = new ReportBrandingResolver(repo, new FixedInstallationBranding(installation),
            NullLogger<ReportBrandingResolver>.Instance);
        _service = new ReportService(resolver, new FakeSettings(), NullLogger<ReportService>.Instance, V6);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static Case CaseOf(string dni)
    {
        var cas = MakeCase();
        cas.Officer = new User { Dni = dni, Name = "Oficial Ficticio", Sigla = "OF" };
        return cas;
    }

    /// <summary>Flujo viejo (<c>GenerateAsync</c>): evidencia en la carpeta del caso.</summary>
    private async Task<string> GenerateServerFlow(string dni)
    {
        var dir = Path.Combine(_root, "server-" + dni);
        Directory.CreateDirectory(dir);
        var stamp = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        var path = Path.Combine(dir, "chat_export.txt");
        File.WriteAllText(path, "contenido de prueba");
        File.SetLastWriteTimeUtc(path, stamp);
        var result = await _service.GenerateAsync(CaseOf(dni), [new FileInfoDto("chat_export.txt", 19, "", stamp)], dir);
        return result.PdfPath;
    }

    /// <summary>Flujo agent (<c>GenerateReportAsync</c>): solo el DOCX, hashes del manifiesto.</summary>
    private async Task<string> GenerateAgentFlow(string dni)
    {
        var images = Path.Combine(_root, "images-" + dni);
        var output = Path.Combine(_root, "output-" + dni);
        Directory.CreateDirectory(images);
        Directory.CreateDirectory(output);
        var stamp = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        var sha = new string('1', 64);
        var cas = CaseOf(dni);
        cas.EvidenceStorage = EvidenceStorages.Agent;
        var result = await _service.GenerateReportAsync(cas, [new FileInfoDto("chat_export.txt", 19, sha, stamp)],
            new Dictionary<string, string> { ["chat_export.txt"] = sha }, images, output,
            ReportService.ZipFilenameFor(cas), new string('f', 64));
        return result.PdfPath;
    }

    private static IEnumerable<OpenXmlPartRootElement> Roots(WordprocessingDocument doc)
    {
        var main = doc.MainDocumentPart!;
        yield return main.Document;
        foreach (var h in main.HeaderParts) yield return h.Header;
        foreach (var f in main.FooterParts) yield return f.Footer;
        if (main.NumberingDefinitionsPart?.Numbering is { } n) yield return n;
    }

    private static string AllText(WordprocessingDocument doc) =>
        string.Join("\n", Roots(doc).Select(r => string.Concat(r.Descendants<Text>().Select(t => t.Text))));

    private static string AllXml(WordprocessingDocument doc) => string.Join("\n", Roots(doc).Select(r => r.OuterXml));

    private static List<string> DrawingNames(WordprocessingDocument doc, bool headersAndFootersOnly)
    {
        var main = doc.MainDocumentPart!;
        IEnumerable<OpenXmlElement> roots = headersAndFootersOnly
            ? main.HeaderParts.Select(h => (OpenXmlElement)h.Header).Concat(main.FooterParts.Select(f => f.Footer))
            : Roots(doc);
        return roots.SelectMany(r => r.Descendants<DWP.DocProperties>()).Select(d => d.Name?.Value ?? "").ToList();
    }

    private static void AssertAttribution(WordprocessingDocument doc)
    {
        // D13: la atribución de Factum (sello + texto) sigue en el pie, sea cual sea la marca.
        var main = doc.MainDocumentPart!;
        Assert.All(main.FooterParts, f =>
            Assert.Contains("Realizado con Factum", string.Concat(f.Footer.Descendants<Text>().Select(t => t.Text))));
        Assert.Contains(DrawingNames(doc, headersAndFootersOnly: true), n => n.StartsWith("factum-sello"));
    }

    [Fact]
    public async Task FlujoServidor_DuenoA_SaleConLaMarcaA()
    {
        using var doc = WordprocessingDocument.Open(await GenerateServerFlow(DniA), false);
        var text = AllText(doc);
        var xml = AllXml(doc);

        Assert.Contains(NameA, text);
        Assert.Contains("Contacto Ficticio A", text);
        Assert.Contains(PrimaryA, xml);
        Assert.DoesNotContain(NameB, text);
        Assert.DoesNotContain(PrimaryB, xml);
        Assert.DoesNotContain(NameInstallation, text);
        Assert.DoesNotContain(PrimaryInstallation, xml);
        // Membrete con logo (la v6 lo pone en el pie de la portada).
        Assert.Contains(DrawingNames(doc, headersAndFootersOnly: true), n => n.StartsWith("logo-organizacion"));
        AssertAttribution(doc);
    }

    [Fact]
    public async Task FlujoAgent_DuenoB_SaleConLaMarcaB_SinLogo()
    {
        using var doc = WordprocessingDocument.Open(await GenerateAgentFlow(DniB), false);
        var text = AllText(doc);
        var xml = AllXml(doc);

        Assert.Contains(NameB, text);
        Assert.Contains(PrimaryB, xml);
        Assert.DoesNotContain(NameA, text);
        Assert.DoesNotContain(PrimaryA, xml);
        Assert.DoesNotContain(NameInstallation, text);
        Assert.DoesNotContain(PrimaryInstallation, xml);
        Assert.DoesNotContain(DrawingNames(doc, headersAndFootersOnly: false), n => n.StartsWith("logo-organizacion"));
        AssertAttribution(doc);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DuenoSinMarca_SaleConLaInstalacion(bool serverFlow)
    {
        var path = serverFlow ? await GenerateServerFlow(DniC) : await GenerateAgentFlow(DniC);
        using var doc = WordprocessingDocument.Open(path, false);
        var text = AllText(doc);
        var xml = AllXml(doc);

        Assert.Contains(NameInstallation, text);
        Assert.Contains(PrimaryInstallation, xml);
        Assert.DoesNotContain(NameA, text);
        Assert.DoesNotContain(NameB, text);
        Assert.DoesNotContain(PrimaryA, xml);
        Assert.DoesNotContain(PrimaryB, xml);
        AssertAttribution(doc);
    }

    [Fact]
    public async Task FlujoAgent_DuenoA_TambienSaleConA()
    {
        using var doc = WordprocessingDocument.Open(await GenerateAgentFlow(DniA), false);
        Assert.Contains(NameA, AllText(doc));
        Assert.Contains(PrimaryA, AllXml(doc));
        Assert.DoesNotContain(NameInstallation, AllText(doc));
    }

    [Fact]
    public async Task ResolverQueFalla_LaGeneracionFalla_SinArtefactos()
    {
        var repo = new InMemoryAccountBrandingRepository { FindThrows = new TimeoutException("mongo caído") };
        var resolver = new ReportBrandingResolver(repo,
            new FixedInstallationBranding(new BrandingSnapshot(NameInstallation, [], null)),
            NullLogger<ReportBrandingResolver>.Instance);
        var service = new ReportService(resolver, new FakeSettings(), NullLogger<ReportService>.Instance, V6);
        var dir = Path.Combine(_root, "falla");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "chat_export.txt"), "contenido");

        await Assert.ThrowsAsync<TimeoutException>(() => service.GenerateAsync(CaseOf(DniA),
            [new FileInfoDto("chat_export.txt", 9, "", DateTime.UtcNow)], dir));

        // Ni ZIP ni DOCX; la evidencia suelta sigue ahí.
        Assert.Equal(["chat_export.txt"], Directory.GetFiles(dir).Select(Path.GetFileName));
    }
}
