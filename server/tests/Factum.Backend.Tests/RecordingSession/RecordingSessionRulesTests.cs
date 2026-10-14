using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;
using Factum.Backend.Services.Recording;

namespace Factum.Backend.Tests.SesionGrabacion;

/// <summary>Datos de prueba de la grabación de la sesión (ids de 32 hex, horas de la PC en UTC).</summary>
internal static class SessionData
{
    public static readonly DateTime T0 = new(2026, 10, 14, 17, 2, 10, 481, DateTimeKind.Utc);
    public const string SessionId = "3f1c00000000000000000000000000aa";

    public static string Id(int n) => n.ToString("x32");
    public static string Sha(char c) => new(c, 64);

    public static SessionSegmentDto Segment(int index, string? state = "saved", char sha = 'a', string track = "pantalla",
        double seconds = 1800, string? stopReason = "rotacion", string? id = null) => new(
        Id: id ?? Id(100 + index),
        Track: track,
        Index: index,
        Filename: $"sesion_{track}_{index:000}_20261014_{14 + index:00}0210.mkv",
        State: state,
        StartedAt: T0.AddSeconds((index - 1) * seconds),
        StoppedAt: T0.AddSeconds(index * seconds + 0.7),
        RecordedSeconds: seconds + 0.7,
        DurationSeconds: state == "saved" ? seconds + 0.2 : null,
        Validation: state == "saved" ? "ok" : null,
        Recovered: false,
        OverlapSeconds: 0.7,
        StopReason: stopReason,
        Size: state == "saved" ? 943718400 : null,
        Sha256: state == "saved" ? Sha(sha) : null);

    public static SessionGapDto Gap(int n, DateTime from, DateTime? to = null, string reason = "desconexion",
        bool lost = false) => new(Id(200 + n), "pantalla", from, to, reason, lost);

    public static SessionMarkDto Mark(int n, string? text = "Abre el chat con Juan", string? filename = null,
        string kind = "manual") => new(Id(300 + n), T0.AddSeconds(513.6 + n), 1, 513.6 + n, text, filename, kind);

    public static RecordingSessionSnapshotRequest Snapshot(List<SessionSegmentDto?>? segments = null,
        List<SessionGapDto?>? gaps = null, List<SessionMarkDto?>? marks = null, string state = "recording") => new(
        SessionId, "android", "R58RA3R5AGA", "1.8.0", 30, "device", null, state,
        segments ?? [Segment(1)], gaps ?? [], marks ?? []);

    public static Models.RecordingSession Enabled() => new()
    {
        Enabled = true, ModeAtChange = "optional_off", SettingsUpdatedAt = T0, SegmentMinutes = 30,
    };

    public static ValidatedSnapshot Valid(RecordingSessionSnapshotRequest s)
    {
        var v = RecordingSessionRules.Validate(s, T0.AddHours(5));
        Assert.True(v.IsValid, v.Field);
        return v.Snapshot!;
    }
}

/// <summary>Validación, merge append-only y pendientes (grabacion-sesion-completa §6.2).</summary>
public sealed class RecordingSessionRulesTests
{
    private static readonly DateTime Now = SessionData.T0.AddHours(5);

    private static string? FieldOf(RecordingSessionSnapshotRequest s) => RecordingSessionRules.Validate(s, Now).Field;

    [Fact]
    public void Valido_NormalizaHoras()
    {
        var s = SessionData.Snapshot(marks: [SessionData.Mark(1)],
            gaps: [SessionData.Gap(1, SessionData.T0.AddHours(1))]);
        var v = SessionData.Valid(s);
        Assert.Single(v.Segments);
        Assert.Equal(DateTimeKind.Utc, v.Segments[0].StartedAt.Kind);
        Assert.Null(v.Gaps[0].To);
        Assert.Equal("Abre el chat con Juan", v.Marks[0].Text);
    }

    [Fact]
    public void HoraConOffset_SePasaAUtc()
    {
        var local = new DateTime(2026, 10, 14, 14, 2, 10, DateTimeKind.Local);
        var seg = SessionData.Segment(1) with { StartedAt = local, StoppedAt = local.AddMinutes(30) };
        var v = SessionData.Valid(SessionData.Snapshot([seg]));
        Assert.Equal(DateTimeKind.Utc, v.Segments[0].StartedAt.Kind);
        Assert.Equal(local.ToUniversalTime(), v.Segments[0].StartedAt);
    }

    private static readonly Dictionary<string, (RecordingSessionSnapshotRequest Snapshot, string Field)> Cases = Build();

    private static Dictionary<string, (RecordingSessionSnapshotRequest, string)> Build()
    {
        var ok = SessionData.Snapshot();
        var seg = SessionData.Segment(1);
        var list = new (RecordingSessionSnapshotRequest, string)[]
        {
            (ok with { SessionId = "XYZ" }, "session_id"),
            (ok with { SessionId = null }, "session_id"),
            (ok with { Platform = "windows" }, "platform"),
            (ok with { SegmentMinutes = 0 }, "segment_minutes"),
            (ok with { SegmentMinutes = 241 }, "segment_minutes"),
            (ok with { Audio = "mic" }, "audio"),
            (ok with { State = "grabando" }, "state"),
            (SessionData.Snapshot([seg with { Id = "1" }]), "segments[0].id"),
            (SessionData.Snapshot([seg, seg]), "segments[1].id"),
            (SessionData.Snapshot([seg with { Track = "audio" }]), "segments[0].track"),
            (SessionData.Snapshot([seg with { Index = 0 }]), "segments[0].index"),
            (SessionData.Snapshot([seg with { Filename = "grabacion_1.mkv" }]), "segments[0].filename"),
            (SessionData.Snapshot([seg with { Track = "camara" }]), "segments[0].filename"),
            (SessionData.Snapshot([seg, SessionData.Segment(2) with { Filename = seg.Filename }]), "segments[1].filename"),
            (SessionData.Snapshot([seg with { State = "recording" }]), "segments[0].state"),
            (SessionData.Snapshot([seg with { Sha256 = "ABC" }]), "segments[0].sha256"),
            (SessionData.Snapshot([seg with { Sha256 = new string('A', 64) }]), "segments[0].sha256"),
            (SessionData.Snapshot([seg with { Size = 0 }]), "segments[0].size"),
            (SessionData.Snapshot([seg with { Validation = "unreadable" }]), "segments[0].validation"),
            (SessionData.Snapshot([seg with { StoppedAt = seg.StartedAt!.Value.AddSeconds(-1) }]), "segments[0].stopped_at"),
            (SessionData.Snapshot([seg with { StoppedAt = null }]), "segments[0].stopped_at"),
            (SessionData.Snapshot([seg with { StartedAt = Now.AddDays(400), StoppedAt = Now.AddDays(400) }]), "segments[0].started_at"),
            (SessionData.Snapshot([seg with { StopReason = "porque_si" }]), "segments[0].stop_reason"),
            (SessionData.Snapshot(gaps: [SessionData.Gap(1, SessionData.T0, reason: "otro")]), "gaps[0].reason"),
            (SessionData.Snapshot(gaps: [SessionData.Gap(1, SessionData.T0, SessionData.T0.AddSeconds(-1))]), "gaps[0].to"),
            (SessionData.Snapshot(gaps: [SessionData.Gap(1, SessionData.T0) with { Track = "x" }]), "gaps[0].track"),
            (SessionData.Snapshot(marks: [SessionData.Mark(1, text: new string('x', 301))]), "marks[0].text"),
            (SessionData.Snapshot(marks: [SessionData.Mark(1, kind: "auto")]), "marks[0].kind"),
            (SessionData.Snapshot(marks: [SessionData.Mark(1, filename: "../x.png")]), "marks[0].filename"),
            (SessionData.Snapshot(marks: [SessionData.Mark(1) with { SegmentIndex = 0 }]), "marks[0].segment_index"),
            (SessionData.Snapshot(marks: [SessionData.Mark(1) with { OffsetSeconds = -1 }]), "marks[0].offset_seconds"),
        };
        return list.Select((x, i) => (Key: $"{i:00}:{x.Item2}", x)).ToDictionary(t => t.Key, t => t.x);
    }

    public static TheoryData<string> CaseKeys() => new(Cases.Keys);

    [Theory]
    [MemberData(nameof(CaseKeys))]
    public void Invalido_DevuelveElCampo(string key)
    {
        var (snapshot, field) = Cases[key];
        var v = RecordingSessionRules.Validate(snapshot, Now);
        Assert.False(v.IsValid);
        Assert.Equal(field, v.Field);
        Assert.Equal("Datos de la grabación de la sesión inválidos.", v.Error);
    }

    [Fact]
    public void Topes_DeLasListas()
    {
        var many = Enumerable.Range(0, RecordingSessionRules.MaxMarks + 1)
            .Select(i => (SessionMarkDto?)SessionData.Mark(i)).ToList();
        Assert.Equal("marks", FieldOf(SessionData.Snapshot(marks: many)));
    }

    [Fact]
    public void Descartado_NoExigeHash()
    {
        var v = SessionData.Valid(SessionData.Snapshot([SessionData.Segment(1, state: "discarded")]));
        Assert.Equal("discarded", v.Segments[0].State);
    }

    [Fact]
    public void Merge_AgregaYNuncaBorra()
    {
        var existing = SessionData.Enabled();
        var first = RecordingSessionRules.Merge(existing, SessionData.Valid(SessionData.Snapshot()));
        Assert.False(first.IsConflict);
        Assert.Single(first.Merged!.Segments);
        Assert.Single(first.Changes!.FirstOfTrack);
        Assert.True(first.Merged.Enabled); // el switch no cambia

        // Un snapshot viejo (sin el tramo 1) no borra nada; uno con el 2 lo agrega.
        var second = RecordingSessionRules.Merge(first.Merged,
            SessionData.Valid(SessionData.Snapshot([SessionData.Segment(2, sha: 'b')])));
        Assert.Equal(2, second.Merged!.Segments.Count);
        Assert.Empty(second.Changes!.FirstOfTrack);
        Assert.Single(second.Changes.NewSegments);

        // Idéntico: sin cambios.
        var same = RecordingSessionRules.Merge(second.Merged,
            SessionData.Valid(SessionData.Snapshot([SessionData.Segment(1), SessionData.Segment(2, sha: 'b')])));
        Assert.False(same.Changes!.Any);
    }

    [Fact]
    public void Merge_MismoIdConOtroHash_Conflict()
    {
        var a = RecordingSessionRules.Merge(SessionData.Enabled(), SessionData.Valid(SessionData.Snapshot())).Merged!;
        var b = RecordingSessionRules.Merge(a, SessionData.Valid(SessionData.Snapshot([SessionData.Segment(1, sha: 'c')])));
        Assert.True(b.IsConflict);
        Assert.Equal("segments.sha256", b.ConflictField);
    }

    [Fact]
    public void Merge_FilenameRepetidoConOtroId_Conflict()
    {
        var a = RecordingSessionRules.Merge(SessionData.Enabled(), SessionData.Valid(SessionData.Snapshot())).Merged!;
        var b = RecordingSessionRules.Merge(a,
            SessionData.Valid(SessionData.Snapshot([SessionData.Segment(1, id: SessionData.Id(999))])));
        Assert.Equal("segments.filename", b.ConflictField);
    }

    [Fact]
    public void Merge_DescartadoPasaAGuardado_UnaVez()
    {
        var a = RecordingSessionRules.Merge(SessionData.Enabled(),
            SessionData.Valid(SessionData.Snapshot([SessionData.Segment(1, state: "discarded")]))).Merged!;
        var b = RecordingSessionRules.Merge(a, SessionData.Valid(SessionData.Snapshot()));
        Assert.Equal("saved", b.Merged!.Segments[0].State);
        Assert.Single(b.Changes!.NewSegments);
    }

    [Fact]
    public void Merge_HuecoSeCierraUnaSolaVez()
    {
        var from = SessionData.T0.AddHours(1);
        var open = RecordingSessionRules.Merge(SessionData.Enabled(),
            SessionData.Valid(SessionData.Snapshot(gaps: [SessionData.Gap(1, from)])));
        Assert.Single(open.Changes!.NewGaps);
        Assert.Empty(open.Changes.ClosedGaps);

        var closed = RecordingSessionRules.Merge(open.Merged!,
            SessionData.Valid(SessionData.Snapshot(gaps: [SessionData.Gap(1, from, from.AddSeconds(77))])));
        Assert.Single(closed.Changes!.ClosedGaps);
        Assert.Equal(from.AddSeconds(77), closed.Merged!.Gaps[0].To);

        // Otro "to": conflicto. Un snapshot viejo (sin "to") se ignora.
        Assert.Equal("gaps.to", RecordingSessionRules.Merge(closed.Merged,
            SessionData.Valid(SessionData.Snapshot(gaps: [SessionData.Gap(1, from, from.AddSeconds(90))]))).ConflictField);
        var stale = RecordingSessionRules.Merge(closed.Merged,
            SessionData.Valid(SessionData.Snapshot(gaps: [SessionData.Gap(1, from)])));
        Assert.False(stale.IsConflict);
        Assert.Equal(from.AddSeconds(77), stale.Merged!.Gaps[0].To);
    }

    [Fact]
    public void Merge_CambioDePasoARetomadoOtroDia_UnaVez()
    {
        var from = SessionData.T0.AddHours(1);
        var a = RecordingSessionRules.Merge(SessionData.Enabled(),
            SessionData.Valid(SessionData.Snapshot(gaps: [SessionData.Gap(1, from, reason: "cambio_de_paso")]))).Merged!;
        var b = RecordingSessionRules.Merge(a, SessionData.Valid(SessionData.Snapshot(
            gaps: [SessionData.Gap(1, from, from.AddDays(1), reason: "retomado_otro_dia")])));
        Assert.Equal("retomado_otro_dia", b.Merged!.Gaps[0].Reason);
        Assert.Equal("gaps.reason", RecordingSessionRules.Merge(b.Merged, SessionData.Valid(SessionData.Snapshot(
            gaps: [SessionData.Gap(1, from, from.AddDays(1), reason: "desconexion")]))).ConflictField);
        Assert.Equal("gaps", RecordingSessionRules.Merge(b.Merged, SessionData.Valid(SessionData.Snapshot(
            gaps: [SessionData.Gap(1, from.AddSeconds(1), from.AddDays(1), reason: "retomado_otro_dia")]))).ConflictField);
    }

    [Fact]
    public void Merge_MarcasInmutables()
    {
        var a = RecordingSessionRules.Merge(SessionData.Enabled(),
            SessionData.Valid(SessionData.Snapshot(marks: [SessionData.Mark(1)]))).Merged!;
        var b = RecordingSessionRules.Merge(a,
            SessionData.Valid(SessionData.Snapshot(marks: [SessionData.Mark(1, text: "otro texto")])));
        Assert.Equal("marks", b.ConflictField);
    }

    [Fact]
    public void SessionFilenames_SoloGuardados()
    {
        var cas = new Case
        {
            RecordingSession = RecordingSessionRules.Merge(SessionData.Enabled(), SessionData.Valid(SessionData.Snapshot(
                [SessionData.Segment(1), SessionData.Segment(2, state: "discarded")]))).Merged,
        };
        var names = RecordingSessionRules.SessionFilenames(cas);
        Assert.Single(names);
        Assert.Contains(SessionData.Segment(1).Filename!, names);
        Assert.Empty(RecordingSessionRules.SessionFilenames(new Case()));
    }

    [Fact]
    public void PendingForGeneration()
    {
        var rs = RecordingSessionRules.Merge(SessionData.Enabled(), SessionData.Valid(SessionData.Snapshot(
            [SessionData.Segment(1), SessionData.Segment(2, sha: 'b')], state: "stopped"))).Merged!;
        var cas = new Case { RecordingSession = rs };
        Assert.Equal(2, RecordingSessionRules.PendingForGeneration(cas).Count);

        cas.Evidence.Add(new EvidenceItem { Filename = SessionData.Segment(1).Filename!, Sha256 = SessionData.Sha('a'), Size = 1 });
        Assert.Equal([SessionData.Segment(2).Filename!], RecordingSessionRules.PendingForGeneration(cas));
        // Mismo nombre con otro hash: sigue pendiente.
        cas.Evidence.Add(new EvidenceItem { Filename = SessionData.Segment(2).Filename!, Sha256 = SessionData.Sha('x'), Size = 1 });
        Assert.Single(RecordingSessionRules.PendingForGeneration(cas));

        rs.State = "recording";
        Assert.Equal(["<sesion abierta>"], RecordingSessionRules.PendingForGeneration(cas));
        rs.State = "waiting_device";
        Assert.Equal(["<sesion abierta>"], RecordingSessionRules.PendingForGeneration(cas));
        Assert.Empty(RecordingSessionRules.PendingForGeneration(new Case()));
    }
}

/// <summary>Eventos derivados del merge (§6.4) con el detail exacto.</summary>
public sealed class RecordingSessionEventsTests
{
    [Fact]
    public void LosCincoTipos()
    {
        var from = SessionData.T0.AddSeconds(1800.7);
        var to = from.AddSeconds(77);
        var first = RecordingSessionRules.Merge(SessionData.Enabled(), SessionData.Valid(SessionData.Snapshot(
            [SessionData.Segment(1)], gaps: [SessionData.Gap(1, from, reason: "desconexion")])));
        var events = RecordingSessionRules.Events(first.Changes!, first.Merged!);
        Assert.Equal(["recording_session_started", "recording_session_segment_closed", "recording_session_stopped"],
            events.Select(e => e.Type));
        var started = events[0].Detail;
        Assert.Equal("pantalla", started.Track);
        Assert.Equal(1, started.SegmentIndex);
        Assert.Equal(SessionData.T0, started.At);
        var closed = events[1];
        Assert.Equal(SessionData.Segment(1).Filename, closed.Filename);
        Assert.Equal(SessionData.Sha('a'), closed.Detail.Sha256);
        Assert.Equal(943718400, closed.Detail.Size);
        Assert.Equal(1800.2, closed.Detail.DurationSeconds);
        Assert.Equal(1800.7, closed.Detail.RecordedSeconds); // QA #80 B-4
        var json = System.Text.Json.JsonSerializer.Serialize(closed.Detail,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower });
        Assert.Contains("\"recorded_seconds\":1800.7", json);
        Assert.Equal("ok", closed.Detail.Validation);
        Assert.False(closed.Detail.Recovered);
        var stopped = events[2].Detail;
        Assert.Equal(from, stopped.At);
        Assert.Equal("desconexion", stopped.Reason);
        Assert.False(stopped.SegmentLost);

        var seg2 = SessionData.Segment(2) with { StartedAt = to, StoppedAt = to.AddMinutes(30) };
        var second = RecordingSessionRules.Merge(first.Merged!, SessionData.Valid(SessionData.Snapshot(
            [SessionData.Segment(1), seg2], gaps: [SessionData.Gap(1, from, to, reason: "desconexion")])));
        var ev2 = RecordingSessionRules.Events(second.Changes!, second.Merged!);
        Assert.Equal(["recording_session_resumed", "recording_session_segment_closed"], ev2.Select(e => e.Type));
        var resumed = ev2[0].Detail;
        Assert.Equal(to, resumed.At);
        Assert.Equal(77, resumed.GapSeconds);
        Assert.Equal(2, resumed.SegmentIndex);
        Assert.Equal("desconexion", resumed.Reason);
    }

    [Fact]
    public void Resumed_SinElTramoNuevoTodavia_SegmentIndexInferido()
    {
        // QA #80 B-5: el hueco se cierra antes de que el tramo nuevo llegue al backend.
        var from = SessionData.T0.AddSeconds(1800.7);
        var a = RecordingSessionRules.Merge(SessionData.Enabled(), SessionData.Valid(SessionData.Snapshot(
            [SessionData.Segment(1)], gaps: [SessionData.Gap(1, from)]))).Merged!;
        var b = RecordingSessionRules.Merge(a, SessionData.Valid(SessionData.Snapshot(
            [SessionData.Segment(1)], gaps: [SessionData.Gap(1, from, from.AddSeconds(30))])));
        var resumed = Assert.Single(RecordingSessionRules.Events(b.Changes!, b.Merged!));
        Assert.Equal("recording_session_resumed", resumed.Type);
        Assert.Equal(2, resumed.Detail.SegmentIndex);
    }

    [Fact]
    public void TramoDescartado_Stopped_ConSegmentLost()
    {
        var m = RecordingSessionRules.Merge(SessionData.Enabled(), SessionData.Valid(SessionData.Snapshot(
            [SessionData.Segment(1, state: "discarded", stopReason: "tatana_cerrado")])));
        var events = RecordingSessionRules.Events(m.Changes!, m.Merged!);
        var stopped = Assert.Single(events, e => e.Type == "recording_session_stopped");
        Assert.True(stopped.Detail.SegmentLost);
        Assert.Equal("tatana_cerrado", stopped.Detail.Reason);
        Assert.DoesNotContain(events, e => e.Type == "recording_session_segment_closed");
    }

    [Fact]
    public void LasMarcasNoGeneranEventos()
    {
        var m = RecordingSessionRules.Merge(SessionData.Enabled(),
            SessionData.Valid(SessionData.Snapshot([], marks: [SessionData.Mark(1)])));
        Assert.Empty(RecordingSessionRules.Events(m.Changes!, m.Merged!));
        Assert.Equal(1, m.Changes!.NewMarks);
    }
}

/// <summary><c>Recording:*</c> (§6.6).</summary>
public sealed class RecordingSessionOptionsTests
{
    [Fact]
    public void Defaults()
    {
        var o = RecordingSessionOptions.Resolve(null, null);
        Assert.Equal("optional_off", o.Mode);
        Assert.Equal(30, o.SegmentMinutes);
        Assert.Empty(o.Warnings);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("OPTIONAL_ON")]
    [InlineData(" required ")]
    public void ModosValidos(string raw) =>
        Assert.Equal(raw.Trim().ToLowerInvariant(), RecordingSessionOptions.Resolve(raw, "15").Mode);

    [Theory]
    [InlineData("siempre", null)]
    [InlineData(null, "4")]
    [InlineData(null, "121")]
    [InlineData(null, "treinta")]
    public void Invalidos_DefaultYWarning(string? mode, string? minutes)
    {
        var o = RecordingSessionOptions.Resolve(mode, minutes);
        Assert.Equal("optional_off", o.Mode);
        Assert.Equal(30, o.SegmentMinutes);
        Assert.Single(o.Warnings);
    }

    [Fact]
    public void Rango_5a120()
    {
        Assert.Equal(5, RecordingSessionOptions.Resolve(null, "5").SegmentMinutes);
        Assert.Equal(120, RecordingSessionOptions.Resolve(null, "120").SegmentMinutes);
    }
}
