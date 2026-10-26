using Factum.Backend.Models;
using Factum.Backend.Services.Timestamping;
using Factum.Backend.Tests.Timestamping;

namespace Factum.Backend.Tests.Extractions;

/// <summary>
/// extraccion-logica-android §6.3 / §12.2: el sello del manifiesto va sin caso (<c>CaseId == ""</c> +
/// <c>ExtractionId</c>), no registra evento de caso, y <c>ProcessClaimedAsync</c> con <c>CaseId == ""</c> no busca
/// el caso. También el LEEME de la extracción.
/// </summary>
public class ExtractionTimestampTests
{
    private static Extraction Ext(string sha) => new()
    {
        Id = "3f2a9c1e-0000-4000-8000-0000000000bb",
        ManifestSha256 = sha,
        Device = new ExtractionDevice { Manufacturer = "samsung", Model = "SM-M127F" },
    };

    [Fact]
    public async Task SealExtractionManifest_PendingSinCaso_YSinEvento()
    {
        var h = new TimestampHarness(enabled: true);
        var sha = new string('a', 64);
        var doc = await h.Service.SealExtractionManifestAsync(Ext(sha), TimeSpan.FromSeconds(5));

        Assert.NotNull(doc);
        Assert.Equal(TimestampStatuses.Granted, doc!.Status); // el TSA falso concede por defecto

        var stored = Assert.Single(h.Repo.All, t => t.Target == TimestampTargets.ExtractionManifest);
        Assert.Equal("", stored.CaseId);
        Assert.Equal("3f2a9c1e-0000-4000-8000-0000000000bb", stored.ExtractionId);
        Assert.Equal("MANIFIESTO.tsv", stored.Filename);
        Assert.Equal(sha, stored.Sha256);

        // No se registra ningún evento de caso (la extracción no tiene caso).
        Assert.Empty(h.Events.Events);
    }

    [Fact]
    public async Task SealExtractionManifest_SinHash_NoHaceNada()
    {
        var h = new TimestampHarness(enabled: true);
        var doc = await h.Service.SealExtractionManifestAsync(new Extraction { Id = Guid.NewGuid().ToString("D") },
            TimeSpan.FromSeconds(5));
        Assert.Null(doc);
        Assert.Empty(h.Repo.All);
    }

    [Fact]
    public async Task SealExtractionManifest_Deshabilitado_Null()
    {
        var h = new TimestampHarness(enabled: false);
        var doc = await h.Service.SealExtractionManifestAsync(Ext(new string('a', 64)), TimeSpan.FromSeconds(5));
        Assert.Null(doc);
    }

    [Fact]
    public void Readme_TieneElComandoOpensslDelManifiesto()
    {
        var readme = TimestampReadme.BuildForExtraction("ext-123", DateTime.UtcNow, TimeZoneInfo.Utc, sello: null);
        Assert.Contains("openssl ts -verify -data MANIFIESTO.tsv", readme);
        Assert.Contains("\r\n", readme); // CRLF
    }

    [Fact]
    public void Readme_ConSelloMuestraLosDatos()
    {
        var sello = new CaseTimestamp
        {
            Target = TimestampTargets.ExtractionManifest,
            Filename = "MANIFIESTO.tsv",
            Sha256 = new string('a', 64),
            Status = TimestampStatuses.Granted,
            Authority = "Free TSA",
            TsaUrl = "https://freetsa.org/tsr",
            SerialNumber = "ABCD",
            GenTime = new DateTime(2026, 10, 25, 18, 0, 0, DateTimeKind.Utc),
        };
        var readme = TimestampReadme.BuildForExtraction("ext-123", DateTime.UtcNow, TimeZoneInfo.Utc, sello);
        Assert.Contains("Free TSA", readme);
        Assert.Contains("sellos/MANIFIESTO.tsv.tsr", readme);
    }
}
