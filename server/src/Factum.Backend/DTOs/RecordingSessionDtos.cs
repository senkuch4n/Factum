namespace Factum.Backend.DTOs;

// grabacion-sesion-completa §7.5 (Contrato compartido). JSON snake_case_lower. Todo anulable y sin [Required]: la
// validación vive en Services/Cases/RecordingSessionRules.cs y responde { error, code, field }. Los campos que la web
// manda de más (case_id, schema_version, updated_at, serial) se ignoran.

/// <summary><c>PUT /api/cases/{id}/recording-session/settings</c>: <c>{ "enabled": true, "with_camera": false }</c>.</summary>
public sealed record RecordingSessionSettingsRequest(bool? Enabled = null, bool? WithCamera = null);

/// <summary>Cámara del procedimiento en el snapshot (F2).</summary>
public sealed record SessionCameraDto(string? Name = null, int? Width = null, int? Height = null, int? Fps = null,
    bool? WithAudio = null);

/// <summary>Tramo cerrado del diario de Tatana.</summary>
public sealed record SessionSegmentDto(
    string? Id = null,
    string? Track = null,
    int? Index = null,
    string? Filename = null,
    string? State = null,
    DateTime? StartedAt = null,
    DateTime? StoppedAt = null,
    double? RecordedSeconds = null,
    double? DurationSeconds = null,
    string? Validation = null,
    bool? Recovered = null,
    double? OverlapSeconds = null,
    string? StopReason = null,
    long? Size = null,
    string? Sha256 = null);

/// <summary>Hueco del diario (<c>to</c> ausente o null = abierto).</summary>
public sealed record SessionGapDto(
    string? Id = null,
    string? Track = null,
    DateTime? From = null,
    DateTime? To = null,
    string? Reason = null,
    bool? SegmentLost = null);

/// <summary>Marca de momento del diario.</summary>
public sealed record SessionMarkDto(
    string? Id = null,
    DateTime? At = null,
    int? SegmentIndex = null,
    double? OffsetSeconds = null,
    string? Text = null,
    string? Filename = null,
    string? Kind = null);

/// <summary>
/// <c>PUT /api/cases/{id}/recording-session</c>: el diario de Tatana sin <c>case_id</c>, <c>schema_version</c> ni
/// <c>updated_at</c>, solo con los tramos <c>saved</c>/<c>discarded</c> y con <c>serial</c> renombrado a
/// <c>connection_id</c>.
/// </summary>
public sealed record RecordingSessionSnapshotRequest(
    string? SessionId = null,
    string? Platform = null,
    string? ConnectionId = null,
    string? AgentVersion = null,
    int? SegmentMinutes = null,
    string? Audio = null,
    SessionCameraDto? Camera = null,
    string? State = null,
    List<SessionSegmentDto?>? Segments = null,
    List<SessionGapDto?>? Gaps = null,
    List<SessionMarkDto?>? Marks = null);
