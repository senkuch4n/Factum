namespace Factum.Backend.DTOs;

// metadatos-dispositivo §8.2: ficha técnica y mediciones de reloj que la web reenvía desde Tatana. JSON en
// snake_case_lower. Todo anulable y sin [Required] (convención de CaseDtos.cs): la validación vive en
// DeviceDetailsRules y en DeviceDetailsFlow, y responde { error, code }.

/// <summary>Un dato de la ficha: <c>value</c> o <c>reason</c>.</summary>
public sealed record DeviceDetailFieldDto(string? Value = null, string? Reason = null);

/// <summary>
/// Horas crudas de la medición dispositivo ↔ PC (§5.1), tal como las mandó Tatana (ISO con 7 decimales). La API
/// recalcula la diferencia y el margen; <c>device_minus_pc_ms</c>, <c>margin_ms</c> y <c>samples</c> se ignoran.
/// </summary>
public sealed record DeviceClockDto(
    DateTimeOffset? PcBefore = null,
    DateTimeOffset? PcAfter = null,
    DateTimeOffset? DeviceTime = null,
    int? ResolutionMs = null,
    string? Reason = null);

/// <summary>Sondeo NTP del navegador contra <c>GET /api/time</c> (§5.2).</summary>
public sealed record PcServerProbeDto(
    DateTimeOffset? PcBefore = null,
    DateTimeOffset? ServerTime = null,
    DateTimeOffset? PcAfter = null);

/// <summary><c>PUT /api/cases/{id}/device-details</c>.</summary>
public sealed record SaveDeviceDetailsRequest(
    string? Platform = null,
    string? ConnectionId = null,
    string? AgentVersion = null,
    Dictionary<string, DeviceDetailFieldDto>? Fields = null,
    DeviceClockDto? Clock = null,
    PcServerProbeDto? PcServerProbe = null,
    bool? IncludeSsidInReport = null);

/// <summary><c>PUT /api/cases/{id}/device-details/clock</c> (medición de cierre).</summary>
public sealed record SaveDeviceClockRequest(
    string? ConnectionId = null,
    string? AgentVersion = null,
    DeviceClockDto? Clock = null,
    PcServerProbeDto? PcServerProbe = null);

/// <summary><c>PUT /api/cases/{id}/device-details/options</c>: <c>include_ssid_in_report</c> obligatorio.</summary>
public sealed record DeviceDetailsOptionsRequest(bool? IncludeSsidInReport = null);

/// <summary><c>GET /api/time</c>: hora del servidor (UTC).</summary>
public sealed record ServerTimeResponse(DateTime ServerTime);
