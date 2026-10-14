using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Factum.Agent.Controllers;
using Factum.Agent.Models;
using Factum.Agent.Services;
using Factum.Agent.Services.Evidence;
using Factum.Agent.Services.Session;
using ICSharpCode.SharpZipLib.Zip;

namespace Factum.Agent.Tests.Session;

/// <summary>
/// Entradas <c>sesion/…</c> del ZIP (grabacion-sesion-completa §5.13). El espejo del backend está en
/// server/tests/Factum.Backend.Tests/RecordingSession/EvidenceZipSessionTests.cs.
/// </summary>
public sealed class EvidenceZipSessionTests : IDisposable
{
    private const string Password = "ABCDEFGH23456789";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-agent-zipses-" + Guid.NewGuid().ToString("N"));

    public EvidenceZipSessionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private (List<EvidenceZipEntry> Entries, Dictionary<string, string> Expected) Make()
    {
        var entries = new List<EvidenceZipEntry>();
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string name, string entry, int size)
        {
            var p = Path.Combine(_root, name);
            var data = RandomNumberGenerator.GetBytes(size);
            File.WriteAllBytes(p, data);
            entries.Add(new EvidenceZipEntry(p, entry));
            expected[entry] = Convert.ToHexStringLower(SHA256.HashData(data));
        }
        Add("screenshot_1.png", "screenshot_1.png", 4096);
        Add("sesion_pantalla_001_20261014_140210.mkv", "sesion/sesion_pantalla_001_20261014_140210.mkv", 200_000);
        Add("sesion_pantalla_002_20261014_143211.mkv", "sesion/sesion_pantalla_002_20261014_143211.mkv", 100_000);
        return (entries, expected);
    }

    [Theory]
    [InlineData(Password)]
    [InlineData(null)]
    public async Task EntradasConCarpeta_IdaYVuelta(string? password)
    {
        var (entries, expected) = Make();
        var zip = Path.Combine(_root, "evidencia_t.zip");
        await EvidenceZip.WriteAsync(zip, entries, password, CancellationToken.None);
        await EvidenceZip.VerifyAsync(zip, expected, password, CancellationToken.None);

        using var zf = new ZipFile(zip);
        var names = zf.Cast<ZipEntry>().Select(e => e.Name).ToList();
        Assert.Contains("sesion/sesion_pantalla_001_20261014_140210.mkv", names);
        Assert.Contains("screenshot_1.png", names);
        Assert.Equal(3, names.Count); // sin entrada de carpeta
    }

    [Theory]
    [InlineData("screenshot_1.png", true)]
    [InlineData("sesion/sesion_pantalla_001_20261014_140210.mkv", true)]
    [InlineData("../x", false)]
    [InlineData("otra/x", false)]
    [InlineData("sesion/sub/x", false)]
    [InlineData("sesion/", false)]
    [InlineData("sesion/..", false)]
    [InlineData("a\\b", false)]
    [InlineData("", false)]
    public void IsAllowedEntryName(string name, bool expected) => Assert.Equal(expected, EvidenceZip.IsAllowedEntryName(name));

    [Fact]
    public async Task EntradaNoPermitida_NoEscribeNada()
    {
        var p = Path.Combine(_root, "a.txt");
        File.WriteAllText(p, "x");
        var zip = Path.Combine(_root, "evidencia_x.zip");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            EvidenceZip.WriteAsync(zip, [new EvidenceZipEntry(p, "otra/a.txt")], null, CancellationToken.None));
        Assert.False(File.Exists(zip));
    }

    [Fact]
    public async Task Verificacion_PorNombreDeEntrada()
    {
        var (entries, expected) = Make();
        var zip = Path.Combine(_root, "evidencia_v.zip");
        await EvidenceZip.WriteAsync(zip, entries, Password, CancellationToken.None);
        // Esperado con el nombre plano del tramo: no coincide con la entrada sesion/….
        var wrong = expected.ToDictionary(kv => kv.Key.Replace("sesion/", ""), kv => kv.Value, StringComparer.Ordinal);
        await Assert.ThrowsAsync<EvidenceZipVerificationException>(() =>
            EvidenceZip.VerifyAsync(zip, wrong, Password, CancellationToken.None));
    }
}

/// <summary><c>zip_folder</c> en <c>POST /cases/{id}/zip</c> y borrado del diario al confirmar (§5.10, §5.13).</summary>
public sealed class CaseZipSessionTests : IDisposable
{
    private const string CaseId = "3f2a9c1e-0000-4000-8000-0000000000e1";
    private const string ZipName = "evidencia_77_2026_Titular.zip";
    private const string Segment = "sesion_pantalla_001_20261014_140210.mkv";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-agent-zipfolder-" + Guid.NewGuid().ToString("N"));
    private readonly CaseEvidenceStore _store;
    private readonly SessionJournal _journal;
    private readonly CaseZipService _zips;

    public CaseZipSessionTests()
    {
        var opts = new AgentOptions
        {
            DataDirectory = Path.Combine(_root, "agent-data"),
            EvidenceDirectory = Path.Combine(_root, "Evidencia"),
            MinFreeBytes = 0,
        };
        _journal = new SessionJournal(opts.DataDirectory);
        _store = new CaseEvidenceStore(opts, sessions: _journal);
        _zips = new CaseZipService(opts, _store, sessions: _journal);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private ZipFileItem Seed(string name, string? folder)
    {
        var dir = _store.CaseDir(CaseId);
        Directory.CreateDirectory(dir);
        var data = RandomNumberGenerator.GetBytes(5000);
        File.WriteAllBytes(Path.Combine(dir, name), data);
        return new ZipFileItem(name, data.Length, Convert.ToHexStringLower(SHA256.HashData(data)), folder);
    }

    private void SaveJournal(string segmentState = "saved") => _journal.Save(new SessionJournalDoc
    {
        SessionId = Guid.NewGuid().ToString("N"),
        CaseId = CaseId,
        Serial = "R58",
        State = SessionStates.Stopped,
        Segments = [new JournalSegment { Id = "s1", Index = 1, Filename = Segment, State = segmentState, StartedAt = DateTime.UtcNow }],
    }, DateTime.UtcNow);

    [Fact]
    public async Task ZipFolder_EntradaEnSesion_YSeDevuelve()
    {
        var files = new List<ZipFileItem> { Seed("screenshot_1.png", null), Seed(Segment, "sesion") };
        var res = await _zips.BuildAsync(CaseId, new BuildZipRequest("77/2026", ZipName, null, files), default);
        Assert.Equal("sesion", res.Files.Single(f => f.Filename == Segment).ZipFolder);
        Assert.Null(res.Files.Single(f => f.Filename == "screenshot_1.png").ZipFolder);
        var pending = Path.Combine(res.Directory, ".factum", "pendiente", ZipName);
        using var zf = new ZipFile(pending);
        Assert.Contains($"sesion/{Segment}", zf.Cast<ZipEntry>().Select(e => e.Name));

        // JSON snake_case con zip_folder, y null omitido (HTTP de Tatana).
        var json = JsonSerializer.Serialize(res.Files, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
        Assert.Contains("\"zip_folder\":\"sesion\"", json);
    }

    [Theory]
    [InlineData("otra")]
    [InlineData("Sesion")]
    public async Task ZipFolder_Invalido_400(string folder)
    {
        var files = new List<ZipFileItem> { Seed(Segment, folder) };
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _zips.BuildAsync(CaseId, new BuildZipRequest("77/2026", ZipName, null, files), default));
        Assert.Equal(400, ex.Status);
        Assert.Equal("invalid_filename", ex.Code);
    }

    [Fact]
    public async Task ZipFolder_Sesion_ConNombreQueNoEsTramo_400()
    {
        var files = new List<ZipFileItem> { Seed("screenshot_1.png", "sesion") };
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _zips.BuildAsync(CaseId, new BuildZipRequest("77/2026", ZipName, null, files), default));
        Assert.Equal("invalid_filename", ex.Code);
    }

    [Fact]
    public async Task Commit_BorraLosTramosYElDiarioCerrado()
    {
        SaveJournal();
        var files = new List<ZipFileItem> { Seed("screenshot_1.png", null), Seed(Segment, "sesion") };
        var res = await _zips.BuildAsync(CaseId, new BuildZipRequest("77/2026", ZipName, null, files), default);
        var commit = await _zips.CommitAsync(CaseId,
            new CommitZipRequest("77/2026", ZipName, res.ZipHash, ["screenshot_1.png", Segment]), default);
        Assert.Equal(2, commit.Deleted);
        Assert.False(Directory.Exists(_journal.DirFor(CaseId)));
    }

    [Fact]
    public async Task Commit_ConTramoAbierto_ConservaElDiario()
    {
        SaveJournal("recording");
        var files = new List<ZipFileItem> { Seed("screenshot_1.png", null) };
        var res = await _zips.BuildAsync(CaseId, new BuildZipRequest("77/2026", ZipName, null, files), default);
        await _zips.CommitAsync(CaseId, new CommitZipRequest("77/2026", ZipName, res.ZipHash, ["screenshot_1.png"]), default);
        Assert.True(_journal.Exists(CaseId));
    }
}

/// <summary>Capability y <c>/agent/state</c> (§7.4).</summary>
public sealed class SessionAgentStateTests
{
    private static readonly JsonSerializerOptions Http = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void Capabilities_RecordingSessionV1() =>
        Assert.Contains("recording_session_v1", HealthController.Capabilities);

    private static AgentStateResponse Response(SessionStateDto? session, string recovery) =>
        new("1.8.0", "installed", false, false, [], [], new LocalConfigDto("x", false, false, false, null), [], [],
            session, recovery);

    [Fact]
    public void AgentState_SinSesion_OmiteRecordingSession()
    {
        var json = JsonSerializer.Serialize(Response(null, "running"), Http);
        Assert.DoesNotContain("recording_session", json);
        Assert.Contains("\"session_recovery\":\"running\"", json);
    }

    [Fact]
    public void AgentState_ConSesion_LaIncluye()
    {
        var state = SessionStateDto.IdleFor("3f2a9c1e-0000-4000-8000-0000000000f1") with
        {
            State = "recording", Serial = "R58", Platform = "android", ScreenSegmentIndex = 3,
        };
        var json = JsonSerializer.Serialize(Response(state, "done"), Http);
        Assert.Contains("\"recording_session\":{", json);
        Assert.Contains("\"screen_segment_index\":3", json);
        Assert.Contains("\"camera_state\":\"off\"", json);
        Assert.Contains("\"warnings\":[]", json);
        Assert.Contains("\"session_recovery\":\"done\"", json);
    }
}
