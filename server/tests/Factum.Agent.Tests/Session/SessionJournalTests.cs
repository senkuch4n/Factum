using System.Text.Json;
using Factum.Agent.Services.Session;

namespace Factum.Agent.Tests.Session;

/// <summary>Diario de la sesión (grabacion-sesion-completa §5.10, §7.2.6). Carpeta temporal propia.</summary>
public sealed class SessionJournalTests : IDisposable
{
    private const string CaseId = "3f2a9c1e-0000-4000-8000-0000000000d1";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-agent-journal-" + Guid.NewGuid().ToString("N"));
    private readonly SessionJournal _journal;

    public SessionJournalTests() => _journal = new SessionJournal(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static readonly DateTime T0 = new(2026, 10, 14, 17, 2, 10, 481, DateTimeKind.Utc);

    private static SessionJournalDoc Sample() => new()
    {
        SessionId = "3f1c0000000000000000000000000001",
        CaseId = CaseId,
        Serial = "R58RA3R5AGA",
        Platform = "android",
        AgentVersion = "1.8.0",
        SegmentMinutes = 30,
        Audio = "device",
        State = SessionStates.Recording,
        Segments =
        [
            new JournalSegment
            {
                Id = "a1", Index = 1, Filename = "sesion_pantalla_001_20261014_140210.mkv", State = SegmentStates.Saved,
                StartedAt = T0, StoppedAt = T0.AddSeconds(1800.7), RecordedSeconds = 1800.7, DurationSeconds = 1800.2,
                Validation = "ok", OverlapSeconds = 0.7, StopReason = "rotacion", Size = 943718400,
                Sha256 = new string('a', 64),
            },
            new JournalSegment
            {
                Id = "a2", Index = 2, Filename = "sesion_pantalla_002_20261014_143211.mkv", State = SegmentStates.Recording,
                StartedAt = T0.AddSeconds(1800), Process = new SegmentProcess { Pid = 4242, StartedAt = T0.AddSeconds(1799) },
            },
        ],
        Gaps = [new SessionGapDto { Id = "g1", From = T0.AddSeconds(100), Reason = "desconexion" }],
        Marks = [new SessionMarkDto { Id = "m1", At = T0.AddSeconds(513.6), SegmentIndex = 1, OffsetSeconds = 513.6, Text = "Abre el chat", Kind = "manual" }],
        StayAwake = new StayAwakeState { Serial = "R58RA3R5AGA", Original = "0", Owned = true },
    };

    [Fact]
    public void IdaYVuelta_NombresSnakeCaseExactos()
    {
        var doc = Sample();
        _journal.Save(doc, T0.AddHours(1));
        var json = File.ReadAllText(_journal.PathFor(CaseId));
        foreach (var key in new[]
                 {
                     "\"schema_version\"", "\"session_id\"", "\"case_id\"", "\"serial\"", "\"platform\"", "\"agent_version\"",
                     "\"segment_minutes\"", "\"audio\"", "\"state\"", "\"segments\"", "\"gaps\"", "\"marks\"",
                     "\"updated_at\"", "\"recorded_seconds\"", "\"duration_seconds\"", "\"overlap_seconds\"",
                     "\"stop_reason\"", "\"segment_lost\"", "\"segment_index\"", "\"offset_seconds\"", "\"stay_awake\"",
                     "\"process\"",
                 })
            Assert.Contains(key, json);
        Assert.Contains("\"started_at\": \"2026-10-14T17:02:10.481Z\"", json);
        Assert.DoesNotContain("\"to\"", json); // hueco abierto: sin "to"

        var back = _journal.Load(CaseId).Doc!;
        Assert.Equal(2, back.Segments.Count);
        Assert.Equal(4242, back.Segments[1].Process!.Pid);
        Assert.Equal(DateTimeKind.Utc, back.Segments[0].StartedAt.Kind);
        Assert.Equal("0", back.StayAwake!.Original);
        Assert.Equal(T0.AddHours(1), back.UpdatedAt);
    }

    [Fact]
    public void Dto_SinProcesoNiStayAwake()
    {
        var dto = Sample().ToDto();
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        Assert.DoesNotContain("process", json);
        Assert.DoesNotContain("stay_awake", json);
        Assert.DoesNotContain("last_alive_at", json);
        Assert.DoesNotContain("stop_reason\":\"desconexion", json);
        Assert.Contains("\"filename\":\"sesion_pantalla_001_20261014_140210.mkv\"", json);
    }

    [Fact]
    public void Dto_StopReasonSoloEnEstadosFinalesOEsperando()
    {
        var doc = Sample();
        doc.StopReason = "desconexion";
        Assert.Null(doc.ToDto().StopReason);
        doc.State = SessionStates.WaitingDevice;
        Assert.Equal("desconexion", doc.ToDto().StopReason);
        doc.State = SessionStates.Stopped;
        Assert.Equal("desconexion", doc.ToDto().StopReason);
    }

    [Fact]
    public void Escritura_Atomica_NoDejaTmp()
    {
        _journal.Save(Sample(), T0);
        var dir = _journal.DirFor(CaseId);
        Assert.True(File.Exists(Path.Combine(dir, "sesion.json")));
        Assert.False(File.Exists(Path.Combine(dir, "sesion.json.tmp")));
    }

    [Fact]
    public void TmpHuerfano_SeIgnora()
    {
        _journal.Save(Sample(), T0);
        File.WriteAllText(Path.Combine(_journal.DirFor(CaseId), "sesion.json.tmp"), "{ \"session_id\": \"x");
        var load = _journal.Load(CaseId);
        Assert.False(load.Unreadable);
        Assert.Equal("3f1c0000000000000000000000000001", load.Doc!.SessionId);
        // La siguiente escritura lo pisa.
        _journal.Save(load.Doc, T0.AddMinutes(1));
        Assert.False(File.Exists(Path.Combine(_journal.DirFor(CaseId), "sesion.json.tmp")));
    }

    [Fact]
    public void Ilegible_SeRenombraSinTocarVideos()
    {
        var dir = _journal.DirFor(CaseId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "sesion.json"), "{ esto no es json");
        var video = Path.Combine(dir, "sesion_pantalla_001_20261014_140210.mkv");
        File.WriteAllBytes(video, [1, 2, 3]);

        var load = _journal.Load(CaseId);
        Assert.True(load.Unreadable);
        var moved = _journal.QuarantineUnreadable(CaseId, new DateTime(2026, 10, 14, 15, 0, 0));
        Assert.EndsWith("sesion.json.ilegible-20261014_150000", moved);
        Assert.False(File.Exists(Path.Combine(dir, "sesion.json")));
        Assert.True(File.Exists(video));
        Assert.False(_journal.Load(CaseId).Exists);
    }

    [Fact]
    public void Inexistente_Missing() => Assert.False(_journal.Load(CaseId).Exists);

    [Fact]
    public void ListCaseIds_SoloCarpetasConDiario()
    {
        _journal.Save(Sample(), T0);
        Directory.CreateDirectory(Path.Combine(_journal.SessionsRoot, "no-es-un-caso"));
        Directory.CreateDirectory(_journal.DirFor("3f2a9c1e-0000-4000-8000-0000000000d2"));
        Assert.Equal([CaseId], _journal.ListCaseIds());
    }

    [Fact]
    public void BorradoAlConfirmar_SoloSiEstaCerradoYVacio()
    {
        var doc = Sample();
        _journal.Save(doc, T0);
        Assert.False(_journal.TryDeleteIfClosed(CaseId)); // tramo abierto

        doc.Segments.RemoveAt(1);
        doc.State = SessionStates.Stopped;
        doc.StayAwake!.RestorePending = true;
        _journal.Save(doc, T0);
        Assert.False(_journal.TryDeleteIfClosed(CaseId)); // restauración pendiente

        doc.StayAwake.RestorePending = false;
        _journal.Save(doc, T0);
        var stray = Path.Combine(_journal.DirFor(CaseId), "otro.mkv");
        File.WriteAllBytes(stray, [1]);
        Assert.False(_journal.TryDeleteIfClosed(CaseId)); // otra cosa en la carpeta
        Assert.True(File.Exists(stray));

        File.Delete(stray);
        Assert.True(_journal.TryDeleteIfClosed(CaseId));
        Assert.False(Directory.Exists(_journal.DirFor(CaseId)));
    }
}
