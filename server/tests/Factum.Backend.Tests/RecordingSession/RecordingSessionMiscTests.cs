using System.Security.Cryptography;
using System.Text.Json;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;
using Factum.Backend.Services.Reports;
using Factum.Backend.Services.Timestamping;
using ICSharpCode.SharpZipLib.Zip;

namespace Factum.Backend.Tests.SesionGrabacion;

/// <summary><c>zip_folder</c> del manifiesto (grabacion-sesion-completa §6.5).</summary>
public sealed class ManifestZipFolderTests
{
    private const string Seg = "sesion_pantalla_001_20261014_140210.mkv";

    private static Case CaseWithSession(string segmentState = "saved") => new()
    {
        Evidence =
        [
            new EvidenceItem { Filename = "screenshot_1.png", Size = 10, Sha256 = new string('1', 64) },
            new EvidenceItem { Filename = Seg, Size = 20, Sha256 = new string('a', 64) },
            // Mismo prefijo pero NO es del diario: la clasificación es por pertenencia (D-T5).
            new EvidenceItem { Filename = "sesion_pantalla_009_20261014_140210.mkv", Size = 30, Sha256 = new string('c', 64) },
        ],
        RecordingSession = new Models.RecordingSession
        {
            Enabled = true,
            Segments = [new SessionSegment { Id = "x", Track = "pantalla", Index = 1, Filename = Seg, State = segmentState, Sha256 = new string('a', 64) }],
        },
    };

    [Fact]
    public void ToManifestFiles_PoneSesionSoloALosTramosDelDiario()
    {
        var files = EvidenceManifest.ToManifestFiles(CaseWithSession());
        Assert.Equal("sesion", files.Single(f => f.Filename == Seg).ZipFolder);
        Assert.Null(files.Single(f => f.Filename == "screenshot_1.png").ZipFolder);
        Assert.Null(files.Single(f => f.Filename == "sesion_pantalla_009_20261014_140210.mkv").ZipFolder);
        Assert.All(EvidenceManifest.ToManifestFiles(CaseWithSession("discarded")), f => Assert.Null(f.ZipFolder));
    }

    [Fact]
    public void ZipFolder_SiempreEnElJson_YMatchesLoIgnora()
    {
        var cas = CaseWithSession();
        var files = EvidenceManifest.ToManifestFiles(cas);
        var json = JsonSerializer.Serialize(files, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        Assert.Contains("\"zip_folder\":\"sesion\"", json);
        Assert.Contains("\"zip_folder\":null", json);
        // Lo que verificó Tatana (sin zip_folder, Tatana viejo) coincide igual.
        var verified = files.Select(f => f with { ZipFolder = null }).ToList();
        Assert.Empty(EvidenceManifest.Matches(verified, cas.Evidence));
        Assert.Empty(EvidenceManifest.Matches(files, cas.Evidence));
    }
}

/// <summary>LEEME de los sellos con los tramos dentro de <c>sesion/</c> (§6.7).</summary>
public sealed class TimestampReadmeSessionTests
{
    private const string Seg = "sesion_pantalla_001_20261014_140210.mkv";
    private static readonly DateTime Now = new(2026, 10, 14, 12, 46, 5, DateTimeKind.Utc);

    private static CaseTimestamp Sealed(string filename, char sha) => new()
    {
        CaseId = "c1", Target = TimestampTargets.Evidence, Filename = filename, Sha256 = new string(sha, 64),
        Status = TimestampStatuses.Granted, Authority = "Free TSA", TsaUrl = "https://freetsa.org/tsr",
        GenTime = Now.AddMinutes(-5), SerialNumber = "0A1B", Tsr = [1],
    };

    [Fact]
    public void TramoDentroDelZip()
    {
        var text = TimestampReadme.Build("123/2026", Now, TimeZoneInfo.Utc, "evidencia_x.zip", null,
            new TimestampSelection([Sealed(Seg, 'a'), Sealed("captura_1.png", 'b')], []),
            new HashSet<string>(StringComparer.Ordinal) { Seg });
        Assert.Contains($"{Seg}\r\n  Tipo: Tramo de la grabación de la sesión\r\n  Dentro del ZIP: \"sesion/{Seg}\"\r\n", text);
        Assert.Contains("captura_1.png\r\n  Tipo: Evidencia\r\n  SHA-256", text);
        // El .tsr va plano en sellos/.
        Assert.Contains($"-in \"sellos/{Seg}.tsr\"", text);
    }

    [Fact]
    public void SinSesion_ComoAntes()
    {
        var sel = new TimestampSelection([Sealed(Seg, 'a')], []);
        Assert.Equal(TimestampReadme.Build("1", Now, TimeZoneInfo.Utc, null, null, sel),
            TimestampReadme.Build("1", Now, TimeZoneInfo.Utc, null, null, sel, null));
        Assert.DoesNotContain("Dentro del ZIP", TimestampReadme.Build("1", Now, TimeZoneInfo.Utc, null, null, sel));
    }
}

/// <summary>Espejo de server/tests/Factum.Agent.Tests/Session/SessionZipTests.cs para la copia del backend (§5.13).</summary>
public sealed class EvidenceZipSessionTests : IDisposable
{
    private const string Password = "ABCDEFGH23456789";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-backend-zipses-" + Guid.NewGuid().ToString("N"));

    public EvidenceZipSessionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData(Password)]
    [InlineData(null)]
    public async Task EntradasConCarpeta_IdaYVuelta(string? password)
    {
        var entries = new List<EvidenceZipEntry>();
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, entry) in new[]
                 {
                     ("screenshot_1.png", "screenshot_1.png"),
                     ("sesion_pantalla_001_20261014_140210.mkv", "sesion/sesion_pantalla_001_20261014_140210.mkv"),
                 })
        {
            var p = Path.Combine(_root, name);
            var data = RandomNumberGenerator.GetBytes(50_000);
            File.WriteAllBytes(p, data);
            entries.Add(new EvidenceZipEntry(p, entry));
            expected[entry] = Convert.ToHexStringLower(SHA256.HashData(data));
        }
        var zip = Path.Combine(_root, "evidencia_t.zip");
        await EvidenceZip.WriteAsync(zip, entries, password, CancellationToken.None);
        await EvidenceZip.VerifyAsync(zip, expected, password, CancellationToken.None);
        using var zf = new ZipFile(zip);
        Assert.Contains("sesion/sesion_pantalla_001_20261014_140210.mkv", zf.Cast<ZipEntry>().Select(e => e.Name));
    }

    [Theory]
    [InlineData("a.png", true)]
    [InlineData("sesion/a.mkv", true)]
    [InlineData("../x", false)]
    [InlineData("otra/x", false)]
    [InlineData("sesion/sub/x", false)]
    [InlineData("sesion/", false)]
    public void IsAllowedEntryName(string name, bool expected) => Assert.Equal(expected, EvidenceZip.IsAllowedEntryName(name));

    [Fact]
    public async Task EntradaNoPermitida_NoEscribe()
    {
        var p = Path.Combine(_root, "a.txt");
        File.WriteAllText(p, "x");
        var zip = Path.Combine(_root, "evidencia_x.zip");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            EvidenceZip.WriteAsync(zip, [new EvidenceZipEntry(p, "../a.txt")], null, CancellationToken.None));
        Assert.False(File.Exists(zip));
    }
}
