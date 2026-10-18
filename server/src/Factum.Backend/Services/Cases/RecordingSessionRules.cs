using System.Text.RegularExpressions;
using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Cases;

/// <summary>Valores de la grabación de la sesión (grabacion-sesion-completa §5.3, §6.2).</summary>
public static class RecordingSessionValues
{
    public const string TrackScreen = "pantalla";
    public const string TrackCamera = "camara";
    public const string ZipFolder = "sesion";
    public const string OpenSessionMarker = "<sesion abierta>";

    public static readonly HashSet<string> GapReasons = new(StringComparer.Ordinal)
    {
        "cambio_de_paso", "retomado_otro_dia", "desconexion", "tatana_cerrado", "sin_espacio", "camara_fallo",
        "detenido_por_perito", "fallo_grabacion", "rotacion",
    };

    public static readonly HashSet<string> States = new(StringComparer.Ordinal)
    {
        "idle", "starting", "recording", "rotating", "waiting_device", "stopping", "stopped", "interrupted",
    };

    /// <summary>Estados con la sesión abierta en Tatana: bloquean la generación.</summary>
    public static readonly HashSet<string> OpenStates = new(StringComparer.Ordinal)
    {
        "starting", "recording", "rotating", "waiting_device", "stopping",
    };
}

/// <summary>Snapshot validado y normalizado (horas en UTC, textos saneados).</summary>
public sealed record ValidatedSnapshot(
    string SessionId,
    string Platform,
    string? ConnectionId,
    string? AgentVersion,
    int SegmentMinutes,
    string? Audio,
    SessionCamera? Camera,
    string? State,
    List<SessionSegment> Segments,
    List<SessionGap> Gaps,
    List<SessionMark> Marks);

/// <summary>Validación: <c>Error</c> + <c>Field</c> (para el 400 <c>invalid_recording_session</c>) o el snapshot.</summary>
public sealed record SnapshotValidation(string? Error, string? Field, ValidatedSnapshot? Snapshot)
{
    public bool IsValid => Error is null;
}

/// <summary>Lo que cambió en un merge (para los eventos de §6.4).</summary>
public sealed record SessionChanges(
    List<SessionSegment> NewSegments,
    List<SessionSegment> FirstOfTrack,
    List<SessionGap> NewGaps,
    List<SessionGap> ClosedGaps,
    int NewMarks,
    bool HeaderChanged)
{
    public bool Any => NewSegments.Count > 0 || NewGaps.Count > 0 || ClosedGaps.Count > 0 || NewMarks > 0 || HeaderChanged;
}

/// <summary>Merge: el resultado o el campo en conflicto (409 <c>recording_session_conflict</c>).</summary>
public sealed record SessionMerge(RecordingSession? Merged, SessionChanges? Changes, string? ConflictField)
{
    public bool IsConflict => ConflictField is not null;
}

/// <summary>
/// Reglas puras de la grabación de la sesión (grabacion-sesion-completa §6.2): validación del snapshot, merge append-only
/// por <c>id</c>, archivos de la sesión y pendientes para generar. Sin E/S.
/// </summary>
public static partial class RecordingSessionRules
{
    public const int MaxSegments = 2000;
    public const int MaxGaps = 2000;
    public const int MaxMarks = 5000;
    public const int MaxMarkText = 300;
    public static readonly TimeSpan MaxFuture = TimeSpan.FromDays(1);

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdRegex();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();

    [GeneratedRegex(@"^sesion_(pantalla|camara)_\d{3,4}_\d{8}_\d{6}\.mkv$", RegexOptions.CultureInvariant)]
    private static partial Regex SegmentNameRegex();

    /// <summary><c>^sesion_(pantalla|camara)_\d{3,4}_\d{8}_\d{6}\.mkv$</c> (mismo regex que Tatana, §5.2).</summary>
    public static bool IsSegmentName(string? name) => name is not null && SegmentNameRegex().IsMatch(name);

    public static bool IsTrack(string? t) => t is RecordingSessionValues.TrackScreen or RecordingSessionValues.TrackCamera;

    // ── Validación ───────────────────────────────────────────────────────────

    private static SnapshotValidation Fail(string field) =>
        new(RecordingSessionErrorCodes.InvalidRecordingSessionMessage, field, null);

    /// <summary>Hora en UTC: un DateTime Local (JSON con offset) se convierte; uno sin Kind se toma como UTC.</summary>
    /// <summary>
    /// QA #80 H-1: además se trunca a milisegundos, la precisión con la que Mongo guarda un DateTime. Si no, una hora
    /// con microsegundos (Tatana emite ticks) nunca es igual a la guardada y el segundo sync de una marca o un hueco da
    /// 409 <c>recording_session_conflict</c>.
    /// </summary>
    internal static DateTime Utc(DateTime d) => TruncateMs(d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
    });

    /// <summary>Trunca a milisegundos (UTC).</summary>
    internal static DateTime TruncateMs(DateTime d) =>
        new(d.Ticks - d.Ticks % TimeSpan.TicksPerMillisecond, d.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : d.Kind);

    private static DateTime? TruncateMs(DateTime? d) => d is { } v ? TruncateMs(v) : null;

    private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    public static SnapshotValidation Validate(RecordingSessionSnapshotRequest? dto, DateTime nowUtc)
    {
        if (dto is null) return Fail("body");
        var limit = nowUtc + MaxFuture;
        bool Future(DateTime d) => d > limit;

        if (dto.SessionId is null || !IdRegex().IsMatch(dto.SessionId)) return Fail("session_id");
        if (dto.Platform is not ("android" or "ios")) return Fail("platform");
        if (dto.SegmentMinutes is not (>= 1 and <= 240)) return Fail("segment_minutes");
        if (dto.Audio is not (null or "device" or "none")) return Fail("audio");
        if (dto.State is not null && !RecordingSessionValues.States.Contains(dto.State)) return Fail("state");

        var connection = SafeText.CleanOrNull(dto.ConnectionId, TextShape.SingleLine)?.Trim();
        if (connection is { Length: > 128 }) return Fail("connection_id");
        var agentVersion = SafeText.CleanOrNull(dto.AgentVersion, TextShape.SingleLine)?.Trim();
        if (agentVersion is { Length: > 32 }) return Fail("agent_version");

        SessionCamera? camera = null;
        if (dto.Camera is { } c)
        {
            camera = ValidCamera(c, out var cameraField);
            if (camera is null) return Fail(cameraField == "name" ? "camera.name" : "camera");
        }

        var segmentsIn = dto.Segments ?? [];
        var gapsIn = dto.Gaps ?? [];
        var marksIn = dto.Marks ?? [];
        if (segmentsIn.Count > MaxSegments) return Fail("segments");
        if (gapsIn.Count > MaxGaps) return Fail("gaps");
        if (marksIn.Count > MaxMarks) return Fail("marks");

        var segments = new List<SessionSegment>(segmentsIn.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < segmentsIn.Count; i++)
        {
            var f = $"segments[{i}]";
            if (segmentsIn[i] is not { } s) return Fail(f);
            if (s.Id is null || !IdRegex().IsMatch(s.Id) || !ids.Add(s.Id)) return Fail(f + ".id");
            if (!IsTrack(s.Track)) return Fail(f + ".track");
            if (s.Index is not (>= 1 and <= 9999)) return Fail(f + ".index");
            if (!IsSegmentName(s.Filename) || !s.Filename!.StartsWith($"sesion_{s.Track}_", StringComparison.Ordinal) ||
                !names.Add(s.Filename))
                return Fail(f + ".filename");
            if (s.State is not ("saved" or "discarded")) return Fail(f + ".state");
            if (s.StartedAt is not { } startedRaw) return Fail(f + ".started_at");
            if (s.StoppedAt is not { } stoppedRaw) return Fail(f + ".stopped_at");
            var started = Utc(startedRaw);
            var stopped = Utc(stoppedRaw);
            if (stopped < started) return Fail(f + ".stopped_at");
            if (Future(started) || Future(stopped)) return Fail(f + ".started_at");
            if (s.RecordedSeconds is { } rec && (!Finite(rec) || rec < 0)) return Fail(f + ".recorded_seconds");
            if (s.DurationSeconds is { } dur && (!Finite(dur) || dur < 0)) return Fail(f + ".duration_seconds");
            if (s.OverlapSeconds is { } ov && (!Finite(ov) || ov < 0)) return Fail(f + ".overlap_seconds");
            if (s.StopReason is not null && !RecordingSessionValues.GapReasons.Contains(s.StopReason))
                return Fail(f + ".stop_reason");
            if (s.State == "saved")
            {
                if (s.Sha256 is null || !Sha256Regex().IsMatch(s.Sha256)) return Fail(f + ".sha256");
                if (s.Size is not > 0) return Fail(f + ".size");
                if (s.Validation is not ("ok" or "incomplete")) return Fail(f + ".validation");
            }
            else
            {
                if (s.Sha256 is not null && !Sha256Regex().IsMatch(s.Sha256)) return Fail(f + ".sha256");
                if (s.Size is < 0) return Fail(f + ".size");
                if (s.Validation is not (null or "ok" or "incomplete")) return Fail(f + ".validation");
            }
            // F2 (§6.2): la configuración de la cámara viaja solo en los tramos de cámara.
            SessionCamera? segCamera = null;
            if (s.Camera is { } sc)
            {
                if (s.Track != RecordingSessionValues.TrackCamera) return Fail(f + ".camera");
                segCamera = ValidCamera(sc, out _);
                if (segCamera is null) return Fail(f + ".camera");
            }
            segments.Add(new SessionSegment
            {
                Id = s.Id, Track = s.Track!, Index = s.Index.Value, Filename = s.Filename, State = s.State!,
                StartedAt = started, StoppedAt = stopped,
                RecordedSeconds = s.RecordedSeconds ?? Math.Round((stopped - started).TotalSeconds, 1),
                DurationSeconds = s.DurationSeconds, Validation = s.Validation, Recovered = s.Recovered ?? false,
                OverlapSeconds = s.OverlapSeconds, StopReason = s.StopReason, Size = s.Size, Sha256 = s.Sha256,
                Camera = segCamera,
            });
        }

        var gaps = new List<SessionGap>(gapsIn.Count);
        ids.Clear();
        for (var i = 0; i < gapsIn.Count; i++)
        {
            var f = $"gaps[{i}]";
            if (gapsIn[i] is not { } g) return Fail(f);
            if (g.Id is null || !IdRegex().IsMatch(g.Id) || !ids.Add(g.Id)) return Fail(f + ".id");
            if (!IsTrack(g.Track)) return Fail(f + ".track");
            if (g.From is not { } fromRaw) return Fail(f + ".from");
            var from = Utc(fromRaw);
            DateTime? to = g.To is { } toRaw ? Utc(toRaw) : null;
            if (to is { } t && t < from) return Fail(f + ".to");
            if (Future(from) || (to is { } t2 && Future(t2))) return Fail(f + ".from");
            if (g.Reason is null || !RecordingSessionValues.GapReasons.Contains(g.Reason)) return Fail(f + ".reason");
            gaps.Add(new SessionGap
            {
                Id = g.Id, Track = g.Track!, From = from, To = to, Reason = g.Reason, SegmentLost = g.SegmentLost ?? false,
            });
        }

        var marks = new List<SessionMark>(marksIn.Count);
        ids.Clear();
        for (var i = 0; i < marksIn.Count; i++)
        {
            var f = $"marks[{i}]";
            if (marksIn[i] is not { } m) return Fail(f);
            if (m.Id is null || !IdRegex().IsMatch(m.Id) || !ids.Add(m.Id)) return Fail(f + ".id");
            if (m.At is not { } atRaw) return Fail(f + ".at");
            var at = Utc(atRaw);
            if (Future(at)) return Fail(f + ".at");
            // F2 (§6.2): ubicación en la cámara, los dos campos o ninguno.
            if (m.CameraSegmentIndex is not null || m.CameraOffsetSeconds is not null)
            {
                if (m.CameraSegmentIndex is not (>= 1 and <= 9999) || m.CameraOffsetSeconds is not { } coff ||
                    !Finite(coff) || coff < 0)
                    return Fail(f + ".camera_segment_index");
            }
            // [DU1=A] Sin pantalla grabando, la marca puede venir sin segment_index/offset_seconds, pero solo si trae
            // la ubicación en la cámara. Uno sin el otro → segment_index.
            double? off = null;
            if (m.SegmentIndex is null && m.OffsetSeconds is null)
            {
                if (m.CameraSegmentIndex is null) return Fail(f + ".segment_index");
            }
            else
            {
                if (m.SegmentIndex is null || m.OffsetSeconds is null) return Fail(f + ".segment_index");
                if (m.SegmentIndex is not (>= 1 and <= 9999)) return Fail(f + ".segment_index");
                if (m.OffsetSeconds is not { } o || !Finite(o) || o < 0) return Fail(f + ".offset_seconds");
                off = o;
            }
            var text = SafeText.CleanOrNull(m.Text, TextShape.SingleLine);
            if (text is { Length: > MaxMarkText }) return Fail(f + ".text");
            if (text is { Length: 0 }) text = null;
            if (m.Filename is not null && !EvidenceUpload.IsValidUploadName(m.Filename)) return Fail(f + ".filename");
            if (m.Kind is not ("manual" or "captura")) return Fail(f + ".kind");
            marks.Add(new SessionMark
            {
                Id = m.Id, At = at, SegmentIndex = m.SegmentIndex, OffsetSeconds = off, Text = text,
                Filename = m.Filename, Kind = m.Kind, CameraSegmentIndex = m.CameraSegmentIndex,
                CameraOffsetSeconds = m.CameraOffsetSeconds,
            });
        }

        return new SnapshotValidation(null, null, new ValidatedSnapshot(dto.SessionId, dto.Platform!, connection,
            agentVersion, dto.SegmentMinutes!.Value, dto.Audio, camera, dto.State, segments, gaps, marks));
    }

    /// <summary>
    /// Cámara válida (§6.2): <c>name</c> 1–200 tras <c>SafeText.Clean</c>, W/H 1–10000, fps 1–240. null si no;
    /// <paramref name="field"/> = <c>name</c> o <c>size</c>.
    /// </summary>
    private static SessionCamera? ValidCamera(SessionCameraDto c, out string? field)
    {
        field = null;
        var name = SafeText.Clean(c.Name, TextShape.SingleLine).Trim();
        if (name.Length is 0 or > 200)
        {
            field = "name";
            return null;
        }
        if (c.Width is not (> 0 and <= 10000) || c.Height is not (> 0 and <= 10000) || c.Fps is not (> 0 and <= 240))
        {
            field = "size";
            return null;
        }
        return new SessionCamera
        {
            Name = name, Width = c.Width.Value, Height = c.Height.Value, Fps = c.Fps.Value, WithAudio = c.WithAudio ?? false,
        };
    }

    // ── Merge append-only ────────────────────────────────────────────────────

    /// <summary>
    /// Merge por <c>id</c> (§6.2). Nunca borra: lo que está en <paramref name="existing"/> y no viene se conserva. Un
    /// snapshot viejo (de otra pestaña) que trae menos información no es conflicto: se ignora lo que no aporta (D-T de la
    /// implementación). Conflicto (409) = la misma identidad con otro contenido.
    /// </summary>
    public static SessionMerge Merge(RecordingSession existing, ValidatedSnapshot incoming)
    {
        var merged = new RecordingSession
        {
            Enabled = existing.Enabled,
            WithCamera = existing.WithCamera,
            ModeAtChange = existing.ModeAtChange,
            SettingsUpdatedAt = existing.SettingsUpdatedAt,
            SessionId = incoming.SessionId,
            Platform = incoming.Platform,
            ConnectionId = incoming.ConnectionId,
            AgentVersion = incoming.AgentVersion,
            SegmentMinutes = incoming.SegmentMinutes,
            Audio = incoming.Audio,
            Camera = incoming.Camera,
            State = incoming.State,
            SyncedAt = existing.SyncedAt,
            Segments = existing.Segments.Select(Copy).ToList(),
            Gaps = existing.Gaps.Select(Copy).ToList(),
            Marks = existing.Marks.Select(Copy).ToList(),
        };
        var headerChanged = existing.SessionId != incoming.SessionId || existing.Platform != incoming.Platform ||
                            existing.ConnectionId != incoming.ConnectionId || existing.AgentVersion != incoming.AgentVersion ||
                            existing.SegmentMinutes != incoming.SegmentMinutes || existing.Audio != incoming.Audio ||
                            existing.State != incoming.State || !SameCamera(existing.Camera, incoming.Camera);

        var newSegments = new List<SessionSegment>();
        var firstOfTrack = new List<SessionSegment>();
        var tracksBefore = existing.Segments.Select(s => s.Track).ToHashSet(StringComparer.Ordinal);
        var byId = merged.Segments.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var byName = merged.Segments.ToDictionary(s => s.Filename, StringComparer.Ordinal);
        foreach (var s in incoming.Segments.OrderBy(s => s.StartedAt))
        {
            if (byId.TryGetValue(s.Id, out var old))
            {
                if (old.Filename != s.Filename) return Conflict("segments.filename");
                if (old.Sha256 is not null && s.Sha256 is not null && old.Sha256 != s.Sha256) return Conflict("segments.sha256");
                if (old.State == "discarded" && s.State == "saved")
                {
                    Overwrite(old, s);
                    newSegments.Add(old);
                    continue;
                }
                // F2: la cámara del tramo se completa si faltaba; distinta de la guardada → conflicto.
                if (old.Camera is not null && s.Camera is not null && !SameCamera(old.Camera, s.Camera))
                    return Conflict("segments.camera");
                old.Size ??= s.Size;
                old.Sha256 ??= s.Sha256;
                old.DurationSeconds ??= s.DurationSeconds;
                old.Camera ??= CopyCamera(s.Camera);
                continue;
            }
            if (byName.ContainsKey(s.Filename)) return Conflict("segments.filename");
            var copy = Copy(s);
            merged.Segments.Add(copy);
            byId[copy.Id] = copy;
            byName[copy.Filename] = copy;
            newSegments.Add(copy);
            if (!tracksBefore.Contains(copy.Track) && firstOfTrack.All(f => f.Track != copy.Track))
                firstOfTrack.Add(copy);
        }

        var newGaps = new List<SessionGap>();
        var closedGaps = new List<SessionGap>();
        var gapsById = merged.Gaps.ToDictionary(g => g.Id, StringComparer.Ordinal);
        foreach (var g in incoming.Gaps.OrderBy(g => g.From))
        {
            if (gapsById.TryGetValue(g.Id, out var old))
            {
                if (old.Track != g.Track || TruncateMs(old.From) != g.From || old.SegmentLost != g.SegmentLost)
                    return Conflict("gaps");
                if (old.To is { } oldTo && g.To is { } newTo && TruncateMs(oldTo) != newTo) return Conflict("gaps.to");
                if (old.Reason != g.Reason)
                {
                    // Única transición permitida (una sola vez): cambio_de_paso → retomado_otro_dia. La inversa es un
                    // snapshot viejo y se ignora.
                    if (old.Reason == "cambio_de_paso" && g.Reason == "retomado_otro_dia") old.Reason = g.Reason;
                    else if (!(old.Reason == "retomado_otro_dia" && g.Reason == "cambio_de_paso")) return Conflict("gaps.reason");
                }
                if (old.To is null && g.To is not null)
                {
                    old.To = g.To;
                    closedGaps.Add(old);
                }
                continue;
            }
            var copy = Copy(g);
            merged.Gaps.Add(copy);
            gapsById[copy.Id] = copy;
            newGaps.Add(copy);
            if (copy.To is not null) closedGaps.Add(copy);
        }

        var newMarks = 0;
        var marksById = merged.Marks.ToDictionary(m => m.Id, StringComparer.Ordinal);
        foreach (var m in incoming.Marks)
        {
            if (marksById.TryGetValue(m.Id, out var old))
            {
                if (TruncateMs(old.At) != m.At || old.SegmentIndex != m.SegmentIndex || old.OffsetSeconds != m.OffsetSeconds ||
                    old.Text != m.Text || old.Filename != m.Filename || old.Kind != m.Kind ||
                    old.CameraSegmentIndex != m.CameraSegmentIndex || old.CameraOffsetSeconds != m.CameraOffsetSeconds)
                    return Conflict("marks");
                continue;
            }
            var copy = Copy(m);
            merged.Marks.Add(copy);
            marksById[copy.Id] = copy;
            newMarks++;
        }

        merged.Segments = merged.Segments.OrderBy(s => s.StartedAt).ThenBy(s => s.Track, StringComparer.Ordinal).ToList();
        merged.Gaps = merged.Gaps.OrderBy(g => g.From).ToList();
        merged.Marks = merged.Marks.OrderBy(m => m.At).ToList();
        return new SessionMerge(merged,
            new SessionChanges(newSegments, firstOfTrack, newGaps, closedGaps, newMarks, headerChanged), null);
    }

    private static SessionMerge Conflict(string field) => new(null, null, field);

    private static bool SameCamera(SessionCamera? a, SessionCamera? b) =>
        a is null ? b is null
            : b is not null && a.Name == b.Name && a.Width == b.Width && a.Height == b.Height && a.Fps == b.Fps &&
              a.WithAudio == b.WithAudio;

    private static void Overwrite(SessionSegment target, SessionSegment s)
    {
        target.State = s.State;
        target.StartedAt = s.StartedAt;
        target.StoppedAt = s.StoppedAt;
        target.RecordedSeconds = s.RecordedSeconds;
        target.DurationSeconds = s.DurationSeconds;
        target.Validation = s.Validation;
        target.Recovered = s.Recovered;
        target.OverlapSeconds = s.OverlapSeconds;
        target.StopReason = s.StopReason;
        target.Size = s.Size;
        target.Sha256 = s.Sha256;
        target.Camera = CopyCamera(s.Camera);
    }

    private static SessionCamera? CopyCamera(SessionCamera? c) => c is null ? null : new SessionCamera
    {
        Name = c.Name, Width = c.Width, Height = c.Height, Fps = c.Fps, WithAudio = c.WithAudio,
    };

    internal static SessionSegment Copy(SessionSegment s) => new()
    {
        Id = s.Id, Track = s.Track, Index = s.Index, Filename = s.Filename, State = s.State, StartedAt = s.StartedAt,
        StoppedAt = s.StoppedAt, RecordedSeconds = s.RecordedSeconds, DurationSeconds = s.DurationSeconds,
        Validation = s.Validation, Recovered = s.Recovered, OverlapSeconds = s.OverlapSeconds, StopReason = s.StopReason,
        Size = s.Size, Sha256 = s.Sha256, Camera = CopyCamera(s.Camera),
    };

    private static SessionGap Copy(SessionGap g) => new()
    {
        Id = g.Id, Track = g.Track, From = g.From, To = g.To, Reason = g.Reason, SegmentLost = g.SegmentLost,
    };

    private static SessionMark Copy(SessionMark m) => new()
    {
        Id = m.Id, At = m.At, SegmentIndex = m.SegmentIndex, OffsetSeconds = m.OffsetSeconds, Text = m.Text,
        Filename = m.Filename, Kind = m.Kind, CameraSegmentIndex = m.CameraSegmentIndex,
        CameraOffsetSeconds = m.CameraOffsetSeconds,
    };

    // ── Eventos (§6.4) ───────────────────────────────────────────────────────

    /// <summary>Tipo + filename + detail de cada evento derivado del merge, en orden temporal.</summary>
    public static List<(string Type, string? Filename, CaseEventDetail Detail)> Events(SessionChanges changes,
        RecordingSession merged)
    {
        var list = new List<(DateTime At, int Order, string Type, string? Filename, CaseEventDetail Detail)>();
        foreach (var s in changes.FirstOfTrack)
            list.Add((s.StartedAt, 0, CaseEventTypes.RecordingSessionStarted, null,
                new CaseEventDetail { Track = s.Track, SegmentIndex = s.Index, At = s.StartedAt }));
        foreach (var g in changes.ClosedGaps)
        {
            // QA #80 B-5: el tramo que abre la reanudación puede no haber llegado todavía (se sincroniza al cerrarse).
            // Como la numeración es correlativa por pista, es el siguiente al último que empezó antes del hueco.
            var next = merged.Segments.Where(s => s.Track == g.Track && s.StartedAt >= g.To!.Value.AddSeconds(-1))
                .OrderBy(s => s.StartedAt).FirstOrDefault();
            var nextIndex = next?.Index ?? merged.Segments.Where(s => s.Track == g.Track && s.StartedAt < g.To!.Value)
                .Select(s => s.Index).DefaultIfEmpty(0).Max() + 1;
            list.Add((g.To!.Value, 1, CaseEventTypes.RecordingSessionResumed, null, new CaseEventDetail
            {
                Track = g.Track, At = g.To, Reason = g.Reason,
                GapSeconds = Math.Round((g.To.Value - g.From).TotalSeconds, 1, MidpointRounding.AwayFromZero),
                SegmentIndex = nextIndex,
            }));
        }
        foreach (var s in changes.NewSegments)
        {
            if (s.State == "saved")
                list.Add((s.StoppedAt, 2, CaseEventTypes.RecordingSessionSegmentClosed, s.Filename, new CaseEventDetail
                {
                    Track = s.Track, SegmentIndex = s.Index, Sha256 = s.Sha256, Size = s.Size,
                    DurationSeconds = s.DurationSeconds, RecordedSeconds = s.RecordedSeconds, Validation = s.Validation,
                    Recovered = s.Recovered,
                }));
            else
                list.Add((s.StoppedAt, 3, CaseEventTypes.RecordingSessionStopped, null, new CaseEventDetail
                {
                    Track = s.Track, At = s.StoppedAt, Reason = s.StopReason ?? "fallo_grabacion", SegmentLost = true,
                }));
        }
        foreach (var g in changes.NewGaps)
            list.Add((g.From, 3, CaseEventTypes.RecordingSessionStopped, null, new CaseEventDetail
            {
                Track = g.Track, At = g.From, Reason = g.Reason, SegmentLost = g.SegmentLost,
            }));
        return list.OrderBy(e => e.At).ThenBy(e => e.Order).Select(e => (e.Type, e.Filename, e.Detail)).ToList();
    }

    // ── Archivos de la sesión y generación ───────────────────────────────────

    /// <summary>
    /// Archivos de la sesión: los <c>Filename</c> de los tramos <c>saved</c>. La clasificación (informe, conteos, ZIP)
    /// es por pertenencia al diario, no por el prefijo (D-T5).
    /// </summary>
    public static IReadOnlySet<string> SessionFilenames(Case cas) =>
        cas.RecordingSession is { } rs
            ? rs.Segments.Where(s => s.State == "saved").Select(s => s.Filename).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Pendientes para generar (§6.5): con la sesión abierta en Tatana, <c>["&lt;sesion abierta&gt;"]</c>; si no, los
    /// tramos <c>saved</c> cuyo (filename, sha256) no está en el manifiesto.
    /// </summary>
    public static List<string> PendingForGeneration(Case cas)
    {
        if (cas.RecordingSession is not { } rs) return [];
        if (rs.State is not null && RecordingSessionValues.OpenStates.Contains(rs.State))
            return [RecordingSessionValues.OpenSessionMarker];
        var registered = cas.Evidence.Select(e => (e.Filename, e.Sha256)).ToHashSet();
        return rs.Segments
            .Where(s => s.State == "saved" && !registered.Contains((s.Filename, s.Sha256 ?? "")))
            .OrderBy(s => s.StartedAt)
            .Select(s => s.Filename)
            .ToList();
    }
}
