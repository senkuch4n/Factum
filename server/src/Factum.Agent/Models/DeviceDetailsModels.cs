namespace Factum.Agent.Models;

// metadatos-dispositivo §8.1: ficha técnica y reloj del dispositivo (GET /devices/{serial}/details y /clock).
// JSON snake_case_lower con WhenWritingNull: un null no viaja.

/// <summary>Un dato de la ficha: exactamente uno de <see cref="Value"/> o <see cref="Reason"/> (§4.1, §4.4).</summary>
public sealed record DetailField(string? Value, string? Reason)
{
    public static DetailField Of(string value) => new(value, null);
    public static DetailField Missing(string reason) => new(null, reason);
}

/// <summary>
/// Medición del reloj dispositivo ↔ PC (§5.1). Con muestra válida: horas, resolución, diferencia y margen. Sin
/// ninguna: <c>Samples = 0</c> y <see cref="Reason"/> (<c>timeout</c> | <c>error</c> | <c>invalid</c>).
/// </summary>
public sealed record DeviceClockReading(
    DateTimeOffset? PcBefore,
    DateTimeOffset? PcAfter,
    DateTimeOffset? DeviceTime,
    int? ResolutionMs,
    long? DeviceMinusPcMs,
    long? MarginMs,
    int Samples,
    string? Reason)
{
    public static DeviceClockReading Failed(string reason) => new(null, null, null, null, null, null, 0, reason);
}

public sealed record DeviceDetailsResponse(
    string Serial,
    string Platform,
    string AgentVersion,
    DateTimeOffset ReadStartedAt,
    DateTimeOffset ReadFinishedAt,
    IReadOnlyDictionary<string, DetailField> Fields,
    DeviceClockReading Clock);

public sealed record DeviceClockResponse(string Serial, string Platform, string AgentVersion, DeviceClockReading Clock);

/// <summary>Las 20 claves de la ficha (§4.1), en orden de presentación. Literales snake_case (el diccionario no
/// pasa por la política de nombres del JSON).</summary>
public static class DetailKeys
{
    public const string SerialNumber = "serial_number";
    public const string OsVersion = "os_version";
    public const string OsApiLevel = "os_api_level";
    public const string BuildId = "build_id";
    public const string BuildFingerprint = "build_fingerprint";
    public const string SecurityPatch = "security_patch";
    public const string Kernel = "kernel";
    public const string NetworkOperator = "network_operator";
    public const string NetworkOperatorNumeric = "network_operator_numeric";
    public const string SimOperator = "sim_operator";
    public const string SimOperatorNumeric = "sim_operator_numeric";
    public const string SimState = "sim_state";
    public const string VerifiedBootState = "verified_boot_state";
    public const string BootloaderLocked = "bootloader_locked";
    public const string VbmetaDeviceState = "vbmeta_device_state";
    public const string Timezone = "timezone";
    public const string AutoTime = "auto_time";
    public const string AutoTimeZone = "auto_time_zone";
    public const string WifiState = "wifi_state";
    public const string WifiSsid = "wifi_ssid";

    public static readonly string[] All =
    [
        SerialNumber, OsVersion, OsApiLevel, BuildId, BuildFingerprint, SecurityPatch, Kernel,
        NetworkOperator, NetworkOperatorNumeric, SimOperator, SimOperatorNumeric, SimState,
        VerifiedBootState, BootloaderLocked, VbmetaDeviceState, Timezone, AutoTime, AutoTimeZone,
        WifiState, WifiSsid,
    ];
}

/// <summary>Motivos de "No disponible" (§4.4). Enum cerrado.</summary>
public static class DetailReasons
{
    public const string Empty = "empty";
    public const string NotExposed = "not_exposed";
    public const string NotApplicable = "not_applicable";
    public const string NotConnected = "not_connected";
    public const string Masked = "masked";
    public const string Timeout = "timeout";
    public const string Error = "error";
    public const string Invalid = "invalid";

    public static readonly string[] All =
        [Empty, NotExposed, NotApplicable, NotConnected, Masked, Timeout, Error, Invalid];

    /// <summary>"Datos no disponibles" (§4.4): motivo distinto de <c>not_applicable</c> y <c>not_connected</c>.</summary>
    public static int UnavailableCount(IReadOnlyDictionary<string, DetailField> fields) =>
        fields.Values.Count(f => f.Reason is not null and not (NotApplicable or NotConnected));
}
