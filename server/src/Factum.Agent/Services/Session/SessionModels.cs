using System.Text.Json;
using System.Text.Json.Serialization;

namespace Factum.Agent.Services.Session;

// grabacion-sesion-completa §7.2 (Contrato compartido). JSON snake_case_lower: en HTTP los null no viajan; en el
// WebSocket viajan como null. Las horas son UTC del reloj de esta PC (DateTime con Kind Utc → "…Z").

/// <summary>Estados de la sesión (§5.3) y de los tramos.</summary>
public static class SessionStates
{
    public const string Idle = "idle";
    public const string Starting = "starting";
    public const string Recording = "recording";
    public const string Rotating = "rotating";
    public const string WaitingDevice = "waiting_device";
    public const string Stopping = "stopping";
    public const string Stopped = "stopped";
    public const string Interrupted = "interrupted";

    /// <summary>"Una sesión activa por Tatana" (§5.3).</summary>
    public static bool IsActive(string? state) =>
        state is Starting or Recording or Rotating or WaitingDevice or Stopping;

    /// <summary><c>stop_reason</c> solo viaja en estos estados (§7.2.6).</summary>
    public static bool HasStopReason(string? state) => state is Stopped or Interrupted or WaitingDevice;
}

public static class SegmentStates
{
    public const string Recording = "recording";
    public const string Closing = "closing";
    public const string Saved = "saved";
    public const string Discarded = "discarded";

    /// <summary>Tramo que todavía no se cerró (solo existe en Tatana).</summary>
    public static bool IsOpen(string? state) => state is Recording or Closing;
}

/// <summary>Motivos de huecos y de cortes (§6.2, §8.4).</summary>
public static class SessionReasons
{
    public const string CambioDePaso = "cambio_de_paso";
    public const string RetomadoOtroDia = "retomado_otro_dia";
    public const string Desconexion = "desconexion";
    public const string TatanaCerrado = "tatana_cerrado";
    public const string SinEspacio = "sin_espacio";
    public const string CamaraFallo = "camara_fallo";
    public const string DetenidoPorPerito = "detenido_por_perito";
    public const string FalloGrabacion = "fallo_grabacion";
    public const string Rotacion = "rotacion";

    /// <summary>Motivos que acepta <c>POST …/session/stop</c>.</summary>
    public static bool IsStopRequestReason(string? r) => r is CambioDePaso or DetenidoPorPerito;
}

public static class SessionWarningKinds
{
    public const string ScreenOff = "screen_off";
    public const string ScreenLocked = "screen_locked";
    public const string DiskLow = "disk_low";
    public const string CameraFailed = "camera_failed";
    public const string RotationDelayed = "rotation_delayed";
}

public static class MarkKinds
{
    public const string Manual = "manual";
    public const string Captura = "captura";
}

/// <summary>Cámara del procedimiento (F2). En F1 nunca viaja.</summary>
public sealed record SessionCameraDto(string Name, int Width, int Height, int Fps, bool WithAudio);

/// <summary>Proceso de captura de un tramo abierto (interno: no sale por HTTP). Sirve para la recuperación (§5.10).</summary>
public sealed class SegmentProcess
{
    public int Pid { get; set; }
    /// <summary>Hora de inicio del proceso según el SO (UTC): identifica el PID aunque se reutilice.</summary>
    public DateTime StartedAt { get; set; }
    public string Exe { get; set; } = "scrcpy";
}

/// <summary>"Pantalla siempre encendida" (§5.5, DU2=A). Interno: no sale por HTTP.</summary>
public sealed class StayAwakeState
{
    public string Serial { get; set; } = "";
    /// <summary>Valor de <c>stay_on_while_plugged_in</c> antes de la sesión; null si no se pudo leer.</summary>
    public string? Original { get; set; }
    /// <summary>true si Tatana escribió el 7 (y entonces le toca restaurar).</summary>
    public bool Owned { get; set; }
    /// <summary>Hay que restaurar cuando el celular vuelva a aparecer.</summary>
    public bool RestorePending { get; set; }
}

/// <summary>Tramo en el diario (con el proceso interno).</summary>
public sealed class JournalSegment
{
    public string Id { get; set; } = "";
    public string Track { get; set; } = SessionNames.TrackScreen;
    public int Index { get; set; }
    public string Filename { get; set; } = "";
    public string State { get; set; } = SegmentStates.Recording;
    public DateTime StartedAt { get; set; }
    public DateTime? StoppedAt { get; set; }
    public double? RecordedSeconds { get; set; }
    public double? DurationSeconds { get; set; }
    public string? Validation { get; set; }
    public bool Recovered { get; set; }
    public double? OverlapSeconds { get; set; }
    public string? StopReason { get; set; }
    public long? Size { get; set; }
    public string? Sha256 { get; set; }
    /// <summary>Interno (§5.10). Se borra al cerrar el tramo.</summary>
    public SegmentProcess? Process { get; set; }
    /// <summary>Interno: el cierre fue forzado (kill) y hace falta remux.</summary>
    public bool? Forced { get; set; }

    public SessionSegmentDto ToDto() => new(Id, Track, Index, Filename, State, StartedAt, StoppedAt, RecordedSeconds,
        DurationSeconds, Validation, Recovered, OverlapSeconds, StopReason, Size, Sha256);
}

/// <summary>Tramo como lo ve la web (§7.2.6), sin el proceso.</summary>
public sealed record SessionSegmentDto(string Id, string Track, int Index, string Filename, string State,
    DateTime StartedAt, DateTime? StoppedAt, double? RecordedSeconds, double? DurationSeconds, string? Validation,
    bool Recovered, double? OverlapSeconds, string? StopReason, long? Size, string? Sha256);

/// <summary>Hueco (§7.2.6). <c>to</c> falta mientras sigue abierto.</summary>
public sealed class SessionGapDto
{
    public string Id { get; set; } = "";
    public string Track { get; set; } = SessionNames.TrackScreen;
    public DateTime From { get; set; }
    public DateTime? To { get; set; }
    public string Reason { get; set; } = "";
    public bool SegmentLost { get; set; }
}

/// <summary>Marca de momento (§5.9). Inmutable una vez creada.</summary>
public sealed class SessionMarkDto
{
    public string Id { get; set; } = "";
    public DateTime At { get; set; }
    public int SegmentIndex { get; set; }
    public double OffsetSeconds { get; set; }
    public string? Text { get; set; }
    public string? Filename { get; set; }
    public string Kind { get; set; } = MarkKinds.Manual;
}

/// <summary>
/// <c>&lt;DataDirectory&gt;/sessions/&lt;caseId&gt;/sesion.json</c> (§7.2.6). Lo que sale por HTTP es
/// <see cref="ToDto"/>: sin <c>process</c>, <c>stay_awake</c> ni los demás campos internos.
/// </summary>
public sealed class SessionJournalDoc
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string SessionId { get; set; } = "";
    public string CaseId { get; set; } = "";
    public string Serial { get; set; } = "";
    public string Platform { get; set; } = "android";
    public string? AgentVersion { get; set; }
    public int SegmentMinutes { get; set; }
    /// <summary><c>device</c> (audio interno, Android 11+) | <c>none</c>.</summary>
    public string Audio { get; set; } = "none";
    public SessionCameraDto? Camera { get; set; }
    public string State { get; set; } = SessionStates.Idle;
    public string? StopReason { get; set; }
    public List<JournalSegment> Segments { get; set; } = [];
    public List<SessionGapDto> Gaps { get; set; } = [];
    public List<SessionMarkDto> Marks { get; set; } = [];
    public DateTime UpdatedAt { get; set; }

    // ── Internos (no salen por HTTP) ──
    public StayAwakeState? StayAwake { get; set; }
    /// <summary>Latido (§5.8): última vez que Tatana estaba vivo con la sesión activa.</summary>
    public DateTime? LastAliveAt { get; set; }
    public int? AndroidVersion { get; set; }
    /// <summary>Hora del último corte (para <c>stopped_at</c> del estado).</summary>
    public DateTime? StoppedAt { get; set; }

    public CaseSessionDto ToDto() => new(SchemaVersion, SessionId, CaseId, Serial, Platform, AgentVersion,
        SegmentMinutes, Audio, Camera, State, SessionStates.HasStopReason(State) ? StopReason : null,
        Segments.Select(s => s.ToDto()).ToList(),
        Gaps.Select(Clone).ToList(),
        Marks.Select(Clone).ToList(),
        UpdatedAt);

    // El DTO no comparte instancias con el diario (que se sigue modificando bajo su lock).
    internal static SessionGapDto Clone(SessionGapDto g) => new()
    {
        Id = g.Id, Track = g.Track, From = g.From, To = g.To, Reason = g.Reason, SegmentLost = g.SegmentLost,
    };

    internal static SessionMarkDto Clone(SessionMarkDto m) => new()
    {
        Id = m.Id, At = m.At, SegmentIndex = m.SegmentIndex, OffsetSeconds = m.OffsetSeconds, Text = m.Text,
        Filename = m.Filename, Kind = m.Kind,
    };
}

/// <summary><c>GET /cases/{caseId}/session</c> (§7.2.6).</summary>
public sealed record CaseSessionDto(int SchemaVersion, string SessionId, string CaseId, string Serial, string Platform,
    string? AgentVersion, int SegmentMinutes, string Audio, SessionCameraDto? Camera, string State, string? StopReason,
    List<SessionSegmentDto> Segments, List<SessionGapDto> Gaps, List<SessionMarkDto> Marks, DateTime UpdatedAt);

/// <summary>Aviso del estado (§7.2.7).</summary>
public sealed record SessionWarningDto(string Kind, DateTime Since, long? AvailableBytes = null, long? MinutesLeft = null,
    string? Detail = null);

/// <summary>
/// Estado de la sesión (§7.2.7): lo devuelven start/stop, el evento <c>session_state</c> y
/// <c>/agent/state.recording_session</c>.
/// </summary>
public sealed record SessionStateDto(
    string CaseId,
    string? Serial,
    string? Platform,
    string State,
    bool Resumed,
    int? SegmentMinutes,
    DateTime? SessionStartedAt,
    DateTime? RunStartedAt,
    int? ScreenSegmentIndex,
    DateTime? ScreenSegmentStartedAt,
    string CameraState,
    int? CameraSegmentIndex,
    DateTime? CameraSegmentStartedAt,
    int SegmentsSaved,
    double RecordedSeconds,
    string? StopReason,
    DateTime? StoppedAt,
    IReadOnlyList<SessionWarningDto> Warnings,
    string? OriginId)
{
    public static SessionStateDto IdleFor(string caseId) =>
        new(caseId, null, null, SessionStates.Idle, false, null, null, null, null, null, "off", null, null, 0, 0,
            null, null, [], null);
}

/// <summary>Body de <c>POST /cases/{caseId}/session/start</c> (§7.2.2).</summary>
public sealed record StartSessionRequest(
    string? Serial = null,
    string? Platform = null,
    int? AndroidVersion = null,
    int? SegmentMinutes = null,
    bool SkipScreenCheck = false,
    string? OriginId = null,
    string? IosMode = null,
    JsonElement? Camera = null);

/// <summary>Body de <c>POST /cases/{caseId}/session/stop</c> (§7.2.3).</summary>
public sealed record StopSessionRequest(string? Reason = null, string? OriginId = null);

/// <summary>Body de <c>POST /cases/{caseId}/session/marks</c> (§7.2.4).</summary>
public sealed record AddMarkRequest(string? Text = null, string? OriginId = null);

/// <summary><c>GET /session/capacity</c> (§7.2.1).</summary>
public sealed record SessionCapacityDto(long? AvailableBytes, long MinFreeBytes, long BytesPerMinute,
    bool ZipOnSameVolume, long? EstimatedMinutes, long RequiredBytesToStart, bool CanStart);

/// <summary>200 de <c>POST /agent/prepare-shutdown</c> (§7.2.9).</summary>
public sealed record PrepareShutdownResponse(bool HadSession, int SegmentsSaved, int Pending);

/// <summary>Opciones JSON del diario en disco (snake_case, sin null, indentado).</summary>
internal static class SessionJson
{
    public static readonly JsonSerializerOptions File = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };
}
