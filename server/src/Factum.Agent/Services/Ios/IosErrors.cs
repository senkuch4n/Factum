using System.Text.Json;
using Factum.Agent.Common;

namespace Factum.Agent.Services.Ios;

/// <summary>
/// Error de iPhone con código estable (campo <c>code</c> del JSON de error, SDD
/// ios-herramientas-windows §4.1). <see cref="Exception.Message"/> es el texto en castellano que la
/// web muestra tal cual.
/// </summary>
public sealed class IosException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Códigos y textos exactos de §4.1 (puro y testeado).</summary>
public static class IosErrors
{
    public const string AppleServiceMissing    = AgentErrorCodes.IosAppleServiceMissing;
    public const string DeviceNotFound         = AgentErrorCodes.IosDeviceNotFound;
    public const string NotTrusted             = AgentErrorCodes.IosNotTrusted;
    public const string Locked                 = AgentErrorCodes.IosLocked;
    public const string DeveloperModeDisabled  = AgentErrorCodes.IosDeveloperModeDisabled;
    public const string DdiMountFailed         = AgentErrorCodes.IosDdiMountFailed;
    public const string TunnelFailed           = AgentErrorCodes.IosTunnelFailed;
    public const string AdminRequired          = AgentErrorCodes.IosAdminRequired;
    public const string ToolsMissing           = AgentErrorCodes.IosToolsMissing;
    public const string CaptureFailed          = AgentErrorCodes.IosCaptureFailed;
    public const string RecordingEmpty         = AgentErrorCodes.IosRecordingEmpty;
    public const string AirplayUnavailable     = AgentErrorCodes.AirplayUnavailable;

    public static readonly IReadOnlyList<string> AllCodes =
    [
        AppleServiceMissing, DeviceNotFound, NotTrusted, Locked, DeveloperModeDisabled, DdiMountFailed,
        TunnelFailed, AdminRequired, ToolsMissing, CaptureFailed, RecordingEmpty, AirplayUnavailable,
    ];

    /// <summary>Línea de error del helper: <c>TATANA_ERROR {"code": …, "detail": …}</c> (§6.2).</summary>
    public const string HelperErrorPrefix = "TATANA_ERROR ";

    // Motivos de ios_recording_empty sin código (§4.1).
    public const string ReasonNoFrames = "no se capturó ningún cuadro de la pantalla.";
    public const string ReasonTimeout  = "la grabación no terminó de escribirse a tiempo.";

    private const int DetailMax = 200;

    /// <summary>Texto exacto de §4.1 para el código, según el SO. <paramref name="detail"/> solo lo
    /// usan <c>ios_capture_failed</c> (detalle del helper) y <c>ios_recording_empty</c> (motivo).</summary>
    public static string MessageFor(string code, bool isWindows, string? detail = null) => code switch
    {
        AppleServiceMissing => isWindows
            ? "No se encontró el servicio de dispositivos de Apple en esta PC. Instalá la app \"Apple Devices\" desde Microsoft Store (o iTunes) y volvé a conectar el iPhone."
            : "No se pudo hablar con el servicio de dispositivos de macOS (usbmuxd). Desconectá y volvé a conectar el iPhone.",
        DeviceNotFound =>
            "El iPhone no está conectado o no responde. Revisá el cable USB y que el iPhone esté desbloqueado.",
        NotTrusted =>
            "El iPhone no confía en esta PC. Desbloquealo, tocá \"Confiar\" e ingresá el código.",
        Locked =>
            "El iPhone está bloqueado. Desbloquealo y reintentá.",
        DeveloperModeDisabled =>
            "El Modo Desarrollador del iPhone está apagado. Activalo desde la guía de conexión del iPhone (botón \"Activar Modo Desarrollador\") y reintentá.",
        DdiMountFailed =>
            "No se pudo preparar el iPhone para las capturas (imagen de desarrollador). Revisá que esta PC tenga internet y que el iPhone esté desbloqueado, y reintentá.",
        TunnelFailed =>
            "No se pudo abrir la conexión de servicios con el iPhone. Desconectá y volvé a conectar el cable con el iPhone desbloqueado.",
        AdminRequired => isWindows
            ? "Windows no dejó a Tatana abrir la conexión con el iPhone. Cerrá Tatana y abrilo con clic derecho → \"Ejecutar como administrador\"."
            : "macOS no dejó a Tatana abrir la conexión con el iPhone. Revisá los permisos y reintentá.",
        ToolsMissing => isWindows
            ? "Tatana no encuentra sus herramientas de iPhone (Python o ffmpeg). Descargá e instalá de nuevo Tatana."
            : "Tatana no encuentra Python con pymobiledevice3 o ffmpeg en esta Mac.",
        RecordingEmpty =>
            "No se pudo guardar la grabación del iPhone: " + (string.IsNullOrWhiteSpace(detail) ? ReasonNoFrames : detail.Trim()),
        AirplayUnavailable => isWindows
            ? "AirPlay no está disponible en Tatana para Windows. Usá \"Solo pantalla\", \"Pantalla + micrófono de PC\" o \"Grabación nativa del iPhone\"."
            : "uxplay no encontrado. Instalá: brew install uxplay",
        // CaptureFailed y cualquier código desconocido.
        _ => string.IsNullOrWhiteSpace(detail)
            ? "No se pudo capturar la pantalla del iPhone."
            : "No se pudo capturar la pantalla del iPhone: " + Trim(detail, DetailMax),
    };

    public static IosException Create(string code, bool isWindows, string? detail = null) =>
        new(code, MessageFor(code, isWindows, detail));

    public static IosException Create(string code, string? detail = null) =>
        Create(code, OperatingSystem.IsWindows(), detail);

    /// <summary>Error que manda el helper. Un código desconocido se trata como <c>ios_capture_failed</c>.</summary>
    public static IosException FromHelper(string code, string? detail, bool isWindows)
    {
        var known = AllCodes.Contains(code) && code != RecordingEmpty ? code : CaptureFailed;
        return Create(known, isWindows, detail);
    }

    /// <summary>
    /// Códigos "definitivos" de la captura (§6.3): cortan la cadena de fallbacks porque qvh y AirPlay
    /// tampoco andarían.
    /// </summary>
    public static bool IsDefinitive(string code) => code is
        AppleServiceMissing or DeviceNotFound or NotTrusted or Locked or AdminRequired or ToolsMissing;

    /// <summary>Definitivos de la grabación (§6.7): los de la captura + Modo Desarrollador y DDI (sin burst).</summary>
    public static bool IsDefinitiveForRecording(string code) =>
        IsDefinitive(code) || code is DeveloperModeDisabled or DdiMountFailed;

    /// <summary>
    /// Parsea <c>TATANA_ERROR {"code": "…", "detail": "…"}</c>. JSON roto o sin <c>code</c> → false.
    /// Un código desconocido → <c>ios_capture_failed</c> (con su detalle).
    /// </summary>
    public static bool TryParseHelperError(string? line, out string code, out string detail)
    {
        code = CaptureFailed;
        detail = string.Empty;
        if (line is null) return false;
        var trimmed = line.Trim();
        if (!trimmed.StartsWith(HelperErrorPrefix.TrimEnd(), StringComparison.Ordinal)) return false;
        var json = trimmed[HelperErrorPrefix.TrimEnd().Length..].Trim();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty("code", out var c) || c.ValueKind != JsonValueKind.String)
                return false;
            var parsed = c.GetString() ?? string.Empty;
            code = AllCodes.Contains(parsed) && parsed != RecordingEmpty ? parsed : CaptureFailed;
            if (doc.RootElement.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String)
                detail = d.GetString() ?? string.Empty;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// <c>ios_recording_empty</c> con el motivo: el texto del error que lo causó (en minúscula
    /// inicial) o, sin código, <paramref name="fallbackReason"/>.
    /// </summary>
    public static IosException RecordingEmptyFrom(IosException? cause, string fallbackReason, bool isWindows)
    {
        if (cause is not null && cause.Code == RecordingEmpty) return cause;
        var reason = cause is null ? fallbackReason : LowerFirst(cause.Message);
        return Create(RecordingEmpty, isWindows, reason);
    }

    // "El iPhone está bloqueado…" → "el iPhone está bloqueado…". No toca nombres propios.
    internal static string LowerFirst(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string[] properNouns = ["Windows", "Tatana", "AirPlay", "Apple", "macOS", "iPhone", "uxplay", "Python"];
        if (properNouns.Any(p => text.StartsWith(p, StringComparison.Ordinal))) return text;
        return char.ToLowerInvariant(text[0]) + text[1..];
    }

    private static string Trim(string text, int max)
    {
        var oneLine = string.Join(' ', text.Split((char[])['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return oneLine.Length <= max ? oneLine : oneLine[..max];
    }
}
