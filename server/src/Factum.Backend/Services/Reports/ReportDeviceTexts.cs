using System.Globalization;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Reports;

/// <summary>
/// Textos de la ficha técnica del dispositivo en el informe (metadatos-dispositivo §5.3, §9.3, §9.4), puros. Los
/// valores ya vienen saneados (<c>DeviceDetailsRules</c>) y además pasan por <c>ReportValues.Clean</c> en B-R6.
/// Los estados de arranque se consignan crudos, sin calificar (D9). El SSID solo sale con el flag (D7).
/// </summary>
public static class ReportDeviceTexts
{
    public const string NoDisponible = "No disponible";
    public const string ParcheIos =
        "No aplica (iOS no informa el parche por separado; lo determina la versión del sistema)";
    public const string ArranqueIos = "No aplica (iOS no lo informa)";

    /// <summary>Motivo de "No disponible" (§4.4), igual que en la web.</summary>
    public static string ReasonText(string? reason) => reason switch
    {
        DeviceDetailReasons.Empty => "el dispositivo no devolvió el dato (puede estar restringido por el sistema operativo)",
        DeviceDetailReasons.NotExposed => "el sistema operativo no lo informa por una consulta de solo lectura",
        DeviceDetailReasons.NotApplicable => "no aplica a esta plataforma",
        DeviceDetailReasons.NotConnected => "sin conexión wifi",
        DeviceDetailReasons.Masked => "el sistema no informó el nombre de la red",
        DeviceDetailReasons.Timeout => "el dispositivo no respondió a tiempo",
        DeviceDetailReasons.Invalid => "el valor devuelto no tiene un formato válido y se descartó",
        _ => "falló la lectura",
    };

    /// <summary>"No disponible (motivo)".</summary>
    public static string Unavailable(string? reason) => $"{NoDisponible} ({ReasonText(reason)})";

    /// <summary>§5.3: partes d/h/min/s sin las que valen cero; con días, sin segundos. 0 → "0 s".</summary>
    public static string FormatDuration(long seconds)
    {
        if (seconds < 0) seconds = -seconds;
        if (seconds == 0) return "0 s";
        var d = seconds / 86400;
        var h = seconds % 86400 / 3600;
        var m = seconds % 3600 / 60;
        var s = seconds % 60;
        var parts = new List<string>(4);
        if (d > 0) parts.Add($"{d} d");
        if (h > 0) parts.Add($"{h} h");
        if (m > 0) parts.Add($"{m} min");
        if (s > 0 && d == 0) parts.Add($"{s} s");
        return parts.Count == 0 ? "0 s" : string.Join(" ", parts);
    }

    /// <summary><c>N = round(|ms| / 1000)</c>, half away from zero.</summary>
    public static long RoundSeconds(long ms) =>
        (long)Math.Round(Math.Abs((decimal)ms) / 1000m, MidpointRounding.AwayFromZero);

    /// <summary><c>M = max(1, ceil(margin / 1000))</c>.</summary>
    public static long MarginSeconds(long? marginMs) =>
        Math.Max(1, (long)Math.Ceiling((decimal)(marginMs ?? 0) / 1000m));

    /// <summary>Supera el umbral estrictamente (D6).</summary>
    public static bool Exceeds(long? ms, int thresholdSeconds) =>
        ms is { } v && Math.Abs(v) > thresholdSeconds * 1000L;

    /// <summary>
    /// Informe, en pasado: "el dispositivo coincidía con la estación de trabajo (± M s)" o "el dispositivo
    /// adelantaba/atrasaba {dur} respecto de la estación de trabajo (± M s)".
    /// </summary>
    public static string DeviceSkewPast(long deviceMinusPcMs, long? marginMs)
    {
        var n = RoundSeconds(deviceMinusPcMs);
        var m = FormatDuration(MarginSeconds(marginMs));
        if (n == 0) return $"el dispositivo coincidía con la estación de trabajo (± {m})";
        var verb = deviceMinusPcMs > 0 ? "adelantaba" : "atrasaba";
        return $"el dispositivo {verb} {FormatDuration(n)} respecto de la estación de trabajo (± {m})";
    }

    /// <summary>"la estación de trabajo coincidía con / adelantaba/atrasaba {dur} respecto del servidor de Factum (± M s)".</summary>
    public static string PcServerSkewPast(long pcMinusServerMs, long? marginMs)
    {
        var n = RoundSeconds(pcMinusServerMs);
        var m = FormatDuration(MarginSeconds(marginMs));
        if (n == 0) return $"la estación de trabajo coincidía con el servidor de Factum (± {m})";
        var verb = pcMinusServerMs > 0 ? "adelantaba" : "atrasaba";
        return $"la estación de trabajo {verb} {FormatDuration(n)} respecto del servidor de Factum (± {m})";
    }

    // ── Filas de la ficha ─────────────────────────────────────────────────────

    private static bool IsIos(DeviceDetails d) => d.Platform == "ios";

    private static DeviceDetailField? F(DeviceDetails d, string key) =>
        d.Fields is not null && d.Fields.TryGetValue(key, out var f) ? f : null;

    /// <summary>El valor, o null si el campo falta o trae motivo.</summary>
    public static string? Value(DeviceDetails d, string key) =>
        F(d, key) is { Value: { } v } && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    /// <summary>El motivo del campo (o <c>error</c> si falta).</summary>
    public static string ReasonOf(DeviceDetails d, string key) =>
        F(d, key)?.Reason ?? DeviceDetailReasons.Error;

    private static string ValueOrUnavailable(DeviceDetails d, string key) =>
        Value(d, key) ?? Unavailable(ReasonOf(d, key));

    private static string ValueOrNoDisponible(DeviceDetails d, string key) => Value(d, key) ?? NoDisponible;

    public static string SistemaOperativo(DeviceDetails d)
    {
        var version = Value(d, DeviceDetailKeys.OsVersion);
        if (version is null) return Unavailable(ReasonOf(d, DeviceDetailKeys.OsVersion));
        if (IsIos(d)) return $"iOS {version}";
        var api = Value(d, DeviceDetailKeys.OsApiLevel);
        return api is null ? $"Android {version}" : $"Android {version} (nivel de API {api})";
    }

    public static string Compilacion(DeviceDetails d)
    {
        var build = Value(d, DeviceDetailKeys.BuildId);
        if (IsIos(d)) return build ?? Unavailable(ReasonOf(d, DeviceDetailKeys.BuildId));
        var fp = Value(d, DeviceDetailKeys.BuildFingerprint);
        return (build, fp) switch
        {
            ({ } b, { } f) => $"{b}; huella: {f}",
            ({ } b, null) => b,
            (null, { } f) => $"huella: {f}",
            _ => Unavailable(ReasonOf(d, DeviceDetailKeys.BuildId)),
        };
    }

    public static string ParcheSeguridad(DeviceDetails d)
    {
        if (IsIos(d)) return ParcheIos;
        var v = Value(d, DeviceDetailKeys.SecurityPatch);
        if (v is null) return Unavailable(ReasonOf(d, DeviceDetailKeys.SecurityPatch));
        return DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : v;
    }

    public static string NumeroSerie(DeviceDetails d) => ValueOrUnavailable(d, DeviceDetailKeys.SerialNumber);

    public static string IdentificadorConexion(DeviceDetails d)
    {
        var id = (d.ConnectionId ?? "").Trim();
        if (id.Length == 0) return NoDisponible;
        return IsIos(d)
            ? $"{id} (UDID)"
            : $"{id} (identificador de conexión ADB; no es necesariamente el número de serie)";
    }

    public static string Kernel(DeviceDetails d) => ValueOrUnavailable(d, DeviceDetailKeys.Kernel);

    /// <summary>
    /// "Red: {operador} ({código}); SIM: {operador} ({código}), estado {estado}". Lo que falta sale "No disponible" y
    /// los paréntesis vacíos se omiten.
    /// </summary>
    public static string OperadorSim(DeviceDetails d)
    {
        static string Part(string name, string? code) => code is null ? name : $"{name} ({code})";
        var red = Part(ValueOrNoDisponible(d, DeviceDetailKeys.NetworkOperator),
            Value(d, DeviceDetailKeys.NetworkOperatorNumeric));
        var sim = Part(ValueOrNoDisponible(d, DeviceDetailKeys.SimOperator), Value(d, DeviceDetailKeys.SimOperatorNumeric));
        return $"Red: {red}; SIM: {sim}, estado {ValueOrNoDisponible(d, DeviceDetailKeys.SimState)}";
    }

    public static string EstadoArranque(DeviceDetails d)
    {
        if (IsIos(d)) return ArranqueIos;
        return $"verifiedbootstate: {ValueOrNoDisponible(d, DeviceDetailKeys.VerifiedBootState)}; " +
               $"flash.locked: {ValueOrNoDisponible(d, DeviceDetailKeys.BootloaderLocked)}; " +
               $"vbmeta.device_state: {ValueOrNoDisponible(d, DeviceDetailKeys.VbmetaDeviceState)}";
    }

    public static string ZonaHoraria(DeviceDetails d)
    {
        if (IsIos(d)) return ValueOrUnavailable(d, DeviceDetailKeys.Timezone);
        static string OnOff(string? v) => v switch
        {
            "1" => "activada",
            "0" => "desactivada",
            _ => NoDisponible,
        };
        return $"{ValueOrNoDisponible(d, DeviceDetailKeys.Timezone)}; " +
               $"hora automática: {OnOff(Value(d, DeviceDetailKeys.AutoTime))}; " +
               $"zona horaria automática: {OnOff(Value(d, DeviceDetailKeys.AutoTimeZone))}";
    }

    /// <summary>Wifi (§9.3): el SSID solo con <c>include_ssid_in_report</c> (D7).</summary>
    public static string Wifi(DeviceDetails d)
    {
        var state = Value(d, DeviceDetailKeys.WifiState);
        switch (state)
        {
            case "disabled": return "Desactivado";
            case "disconnected": return "Activado, sin conexión";
            case "connected":
                var ssidField = F(d, DeviceDetailKeys.WifiSsid);
                if (ssidField?.Reason == DeviceDetailReasons.Masked)
                    return "Conectado (el sistema no informó el nombre de la red)";
                var ssid = Value(d, DeviceDetailKeys.WifiSsid);
                if (d.IncludeSsidInReport && ssid is not null) return $"Conectado a la red «{ssid}»";
                return "Conectado (el nombre de la red no se consigna)";
            default:
                return Unavailable(ReasonOf(d, DeviceDetailKeys.WifiState));
        }
    }

    // ── Reloj (§9.3, B-R4 texto plano) ────────────────────────────────────────

    private static string ClockLine(string when, ClockMeasurement m, TimeZoneInfo zone)
    {
        if (m.DeviceMinusPcMs is not { } diff)
            return $"Reloj del dispositivo {when}: {Unavailable(m.Reason)}.";
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(m.RecordedAt, DateTimeKind.Utc), zone);
        var fecha = local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var hora = local.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return $"Reloj del dispositivo {when} (medición registrada el {fecha} a las {hora}): " +
               $"{DeviceSkewPast(diff, m.MarginMs)}.";
    }

    /// <summary>
    /// <c>{descripcionRelojDispositivo}</c>: un párrafo por línea. Inicio y cierre (si hay), la diferencia
    /// PC↔servidor solo por encima del umbral, la frase de constancia de D6 una sola vez y la zona horaria.
    /// <c>ClockClose</c> ya viene filtrado (D-T7).
    /// </summary>
    public static string ClockBlock(DeviceDetails d, IReportSettings settings)
    {
        var lines = new List<string>();
        var measurements = new List<ClockMeasurement>();
        if (d.ClockStart is { } start)
        {
            lines.Add(ClockLine("al inicio", start, settings.Zone));
            measurements.Add(start);
        }
        if (d.ClockClose is { } close)
        {
            lines.Add(ClockLine("al cierre", close, settings.Zone));
            measurements.Add(close);
        }

        var threshold = settings.ClockSkewWarningSeconds;
        var server = measurements
            .Where(m => Exceeds(m.PcMinusServerMs, threshold))
            .OrderByDescending(m => Math.Abs(m.PcMinusServerMs!.Value))
            .FirstOrDefault();
        if (server is not null)
            lines.Add($"A su vez, {PcServerSkewPast(server.PcMinusServerMs!.Value, server.PcServerMarginMs)}.");

        var device = measurements
            .Where(m => Exceeds(m.DeviceMinusPcMs, threshold))
            .OrderByDescending(m => Math.Abs(m.DeviceMinusPcMs!.Value))
            .FirstOrDefault();
        if (device is not null)
            lines.Add("Se deja constancia de que el reloj del dispositivo presentaba una diferencia de " +
                      $"{FormatDuration(RoundSeconds(device.DeviceMinusPcMs!.Value))} respecto de la estación de " +
                      "trabajo; las horas visibles en las capturas corresponden al reloj del dispositivo.");

        if (Value(d, DeviceDetailKeys.Timezone) is { } tz)
            lines.Add($"Zona horaria configurada en el dispositivo: {tz}.");
        return string.Join("\n", lines);
    }

    // ── Placeholders y tokens ─────────────────────────────────────────────────

    /// <summary>Los 12 placeholders de una línea de §9.1 (sin <c>{descripcionRelojDispositivo}</c>). Sin ficha: "".</summary>
    public static Dictionary<string, string> Placeholders(DeviceDetails? d, TimeZoneInfo zone)
    {
        if (d is null)
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["{fechaFichaTecnica}"] = "", ["{horaFichaTecnica}"] = "",
                ["{fichaSistemaOperativo}"] = "", ["{fichaCompilacion}"] = "", ["{fichaParcheSeguridad}"] = "",
                ["{fichaNumeroSerie}"] = "", ["{fichaIdentificadorConexion}"] = "", ["{fichaKernel}"] = "",
                ["{fichaOperadorSim}"] = "", ["{fichaEstadoArranque}"] = "", ["{fichaZonaHoraria}"] = "",
                ["{fichaWifi}"] = "",
            };
        var (fecha, hora) = ReportValues.Inspeccion(d.ReadAt, zone);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{fechaFichaTecnica}"] = fecha,
            ["{horaFichaTecnica}"] = hora,
            ["{fichaSistemaOperativo}"] = SistemaOperativo(d),
            ["{fichaCompilacion}"] = Compilacion(d),
            ["{fichaParcheSeguridad}"] = ParcheSeguridad(d),
            ["{fichaNumeroSerie}"] = NumeroSerie(d),
            ["{fichaIdentificadorConexion}"] = IdentificadorConexion(d),
            ["{fichaKernel}"] = Kernel(d),
            ["{fichaOperadorSim}"] = OperadorSim(d),
            ["{fichaEstadoArranque}"] = EstadoArranque(d),
            ["{fichaZonaHoraria}"] = ZonaHoraria(d),
            ["{fichaWifi}"] = Wifi(d),
        };
    }

    /// <summary>
    /// Tokens de los textos por defecto (§9.4): <c>diferenciaReloj</c>, <c>numeroSerieDispositivo</c>,
    /// <c>compilacionDispositivo</c> y <c>zonaHorariaDispositivo</c> (valor o "No disponible").
    /// </summary>
    public static Dictionary<string, string> DefaultTextTokens(DeviceDetails? d) => new(StringComparer.Ordinal)
    {
        ["diferenciaReloj"] = d?.ClockStart is { DeviceMinusPcMs: { } diff } start
            ? DeviceSkewPast(diff, start.MarginMs)
            : NoDisponible,
        ["numeroSerieDispositivo"] = d is null ? NoDisponible : ValueOrNoDisponible(d, DeviceDetailKeys.SerialNumber),
        ["compilacionDispositivo"] = d is null ? NoDisponible : ValueOrNoDisponible(d, DeviceDetailKeys.BuildId),
        ["zonaHorariaDispositivo"] = d is null ? NoDisponible : ValueOrNoDisponible(d, DeviceDetailKeys.Timezone),
    };

    /// <summary>D-T19: "Android 8.1.0" / "iOS 17.4.1" desde la ficha; null si no hay versión (vale <c>OsLabel</c>).</summary>
    public static string? OsLabelFromDetails(DeviceDetails? d)
    {
        if (d is null || Value(d, DeviceDetailKeys.OsVersion) is not { } v) return null;
        return IsIos(d) ? $"iOS {v}" : $"Android {v}";
    }
}
