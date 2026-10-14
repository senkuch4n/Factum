using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace Factum.Backend.Models;

// metadatos-dispositivo §6.2: ficha técnica leída por Tatana + mediciones de reloj. Todo anulable o con default y
// [BsonIgnoreExtraElements]: un documento viejo sin DeviceDetails deserializa null y no hay backfill.

[BsonIgnoreExtraElements]
public sealed class DeviceDetails
{
    /// <summary>Reloj del SERVIDOR al recibir la ficha (UTC). JSON: <c>read_at</c>.</summary>
    public DateTime ReadAt { get; set; }
    /// <summary><c>"android"</c> | <c>"ios"</c>.</summary>
    public string Platform { get; set; } = "android";
    /// <summary>Serial ADB o UDID (== <c>Case.Device.Serial</c>).</summary>
    public string ConnectionId { get; set; } = "";
    /// <summary>Versión de Tatana que leyó.</summary>
    public string AgentVersion { get; set; } = "";
    /// <summary>Las 20 claves de <see cref="DeviceDetailKeys"/> (literales snake_case: el diccionario no pasa por la
    /// política de nombres del JSON).</summary>
    public Dictionary<string, DeviceDetailField> Fields { get; set; } = new();
    [BsonIgnoreIfNull] public ClockMeasurement? ClockStart { get; set; }
    [BsonIgnoreIfNull] public ClockMeasurement? ClockClose { get; set; }
    /// <summary>D7: el SSID va al informe solo si es true. Default false.</summary>
    public bool IncludeSsidInReport { get; set; }
}

/// <summary>Un dato de la ficha: exactamente uno de <see cref="Value"/> o <see cref="Reason"/>.</summary>
[BsonIgnoreExtraElements]
public sealed class DeviceDetailField
{
    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; set; }
    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
}

/// <summary>Medición de reloj (§5.1 dispositivo ↔ PC y §5.2 PC ↔ servidor), recalculada por la API.</summary>
[BsonIgnoreExtraElements]
public sealed class ClockMeasurement
{
    /// <summary><c>"start"</c> | <c>"close"</c>.</summary>
    public string Kind { get; set; } = ClockKinds.Start;
    /// <summary>Reloj del servidor al recibirla (UTC).</summary>
    public DateTime RecordedAt { get; set; }

    // Dispositivo ↔ PC. Sin hora: Reason y el resto en null.
    /// <summary><c>timeout</c> | <c>error</c> | <c>invalid</c> | <c>not_exposed</c>; null si hubo hora.</summary>
    [BsonIgnoreIfNull] public string? Reason { get; set; }
    /// <summary>UTC, reloj de la PC (antes de pedir la hora).</summary>
    [BsonIgnoreIfNull] public DateTime? PcBefore { get; set; }
    [BsonIgnoreIfNull] public DateTime? PcAfter { get; set; }
    /// <summary>Reloj del dispositivo (UTC).</summary>
    [BsonIgnoreIfNull] public DateTime? DeviceTime { get; set; }
    /// <summary>1 | 1000.</summary>
    [BsonIgnoreIfNull] public int? ResolutionMs { get; set; }
    [BsonIgnoreIfNull] public long? DeviceMinusPcMs { get; set; }
    [BsonIgnoreIfNull] public long? MarginMs { get; set; }

    // PC ↔ servidor.
    [BsonIgnoreIfNull] public long? PcMinusServerMs { get; set; }
    [BsonIgnoreIfNull] public long? PcServerMarginMs { get; set; }
    /// <summary><c>not_measured</c> | <c>invalid</c>; null si se midió.</summary>
    [BsonIgnoreIfNull] public string? PcServerReason { get; set; }
}

public static class ClockKinds
{
    public const string Start = "start";
    public const string Close = "close";
}

/// <summary>Las 20 claves de la ficha (§4.1), en orden de presentación. Literales snake_case.</summary>
public static class DeviceDetailKeys
{
    public const string SerialNumber = "serial_number", OsVersion = "os_version", OsApiLevel = "os_api_level",
        BuildId = "build_id", BuildFingerprint = "build_fingerprint", SecurityPatch = "security_patch",
        Kernel = "kernel", NetworkOperator = "network_operator", NetworkOperatorNumeric = "network_operator_numeric",
        SimOperator = "sim_operator", SimOperatorNumeric = "sim_operator_numeric", SimState = "sim_state",
        VerifiedBootState = "verified_boot_state", BootloaderLocked = "bootloader_locked",
        VbmetaDeviceState = "vbmeta_device_state", Timezone = "timezone", AutoTime = "auto_time",
        AutoTimeZone = "auto_time_zone", WifiState = "wifi_state", WifiSsid = "wifi_ssid";

    public static readonly string[] All =
    [
        SerialNumber, OsVersion, OsApiLevel, BuildId, BuildFingerprint, SecurityPatch, Kernel,
        NetworkOperator, NetworkOperatorNumeric, SimOperator, SimOperatorNumeric, SimState,
        VerifiedBootState, BootloaderLocked, VbmetaDeviceState, Timezone, AutoTime, AutoTimeZone,
        WifiState, WifiSsid,
    ];
}

/// <summary>Motivos de "No disponible" (§4.4). Enum cerrado.</summary>
public static class DeviceDetailReasons
{
    public const string Empty = "empty", NotExposed = "not_exposed", NotApplicable = "not_applicable",
        NotConnected = "not_connected", Masked = "masked", Timeout = "timeout", Error = "error",
        Invalid = "invalid";

    public static readonly string[] All =
        [Empty, NotExposed, NotApplicable, NotConnected, Masked, Timeout, Error, Invalid];

    /// <summary>Motivos del reloj dispositivo ↔ PC.</summary>
    public static readonly string[] Clock = [Timeout, Error, Invalid, NotExposed];

    public const string NotMeasured = "not_measured";
}
