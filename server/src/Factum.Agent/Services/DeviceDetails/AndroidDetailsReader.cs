using System.Text.RegularExpressions;
using Factum.Agent.Common;
using Factum.Agent.Models;

namespace Factum.Agent.Services.DeviceDetails;

/// <summary>Ejecutor de adb inyectable: argumentos después del ejecutable, timeout y cancelación.</summary>
public delegate Task<ProcessResult> AdbRunner(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct);

/// <summary>Ficha (campos 1–20) y reloj leídos de un Android.</summary>
public sealed record AndroidDetailsReading(IReadOnlyDictionary<string, DetailField> Fields, DeviceClockReading Clock,
    DateTimeOffset StartedAt, DateTimeOffset FinishedAt);

/// <summary>
/// Lectura de la ficha técnica de un Android por adb (metadatos-dispositivo §4.2). Todos los comandos son de
/// lectura (<c>getprop</c>, <c>uname</c>, <c>settings get</c>, <c>cmd wifi status</c>, <c>dumpsys wifi</c> filtrado
/// y <c>date</c>): nada escribe en el dispositivo. Cada script va como UN argumento después de <c>shell</c>
/// (patrón de <see cref="AndroidScreenProbe"/>). Nunca se loguea la salida.
/// </summary>
public sealed partial class AndroidDetailsReader(AdbRunner run, TimeProvider time)
{
    internal const string Separator = "__FX_SEP__";
    internal const string DumpsysMarker = "__FX_DUMPSYS__";

    internal static readonly string[] GetStateArgs = ["get-state"];

    /// <summary>Claves 1–16: las 15 propiedades y, después del separador, <c>uname -a</c> (§4.2).</summary>
    internal const string PropsScript =
        "for p in ro.serialno ro.build.version.release ro.build.version.sdk ro.build.id ro.build.fingerprint ro.build.version.security_patch gsm.operator.alpha gsm.operator.numeric gsm.sim.operator.alpha gsm.sim.operator.numeric gsm.sim.state ro.boot.verifiedbootstate ro.boot.flash.locked ro.boot.vbmeta.device_state persist.sys.timezone; do echo \"$p=$(getprop $p)\"; done; echo __FX_SEP__; uname -a";

    /// <summary>Claves 17–18.</summary>
    internal const string SettingsScript =
        "settings get global auto_time; echo __FX_SEP__; settings get global auto_time_zone";

    /// <summary>
    /// Claves 19–20. <c>cmd wifi status</c> (Android 11+) o, si no responde, <c>dumpsys wifi</c> filtrado EN EL
    /// CELULAR con sed + grep: BSSID, MAC, IP y RSSI nunca salen del dispositivo.
    /// </summary>
    internal const string WifiScript =
        "s=$(cmd wifi status 2>/dev/null | grep -E '^Wifi is '); if [ -n \"$s\" ]; then echo \"$s\"; else echo __FX_DUMPSYS__; dumpsys wifi 2>/dev/null | grep -m1 -E '^Wi-Fi is '; dumpsys wifi 2>/dev/null | grep -m1 'mWifiInfo' | sed -E 's/^.*mWifiInfo SSID: (.*), BSSID: .*Supplicant state: ([A-Z_]+).*$/state=\\2 ssid=\\1/' | grep '^state='; fi";

    internal const string DateScript = "date +%s.%N";

    internal static readonly TimeSpan GetStateTimeout = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan DateTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Propiedad de <c>getprop</c> → clave de la ficha (orden de <see cref="PropsScript"/>).</summary>
    internal static readonly (string Prop, string Key)[] PropKeys =
    [
        ("ro.serialno", DetailKeys.SerialNumber),
        ("ro.build.version.release", DetailKeys.OsVersion),
        ("ro.build.version.sdk", DetailKeys.OsApiLevel),
        ("ro.build.id", DetailKeys.BuildId),
        ("ro.build.fingerprint", DetailKeys.BuildFingerprint),
        ("ro.build.version.security_patch", DetailKeys.SecurityPatch),
        ("gsm.operator.alpha", DetailKeys.NetworkOperator),
        ("gsm.operator.numeric", DetailKeys.NetworkOperatorNumeric),
        ("gsm.sim.operator.alpha", DetailKeys.SimOperator),
        ("gsm.sim.operator.numeric", DetailKeys.SimOperatorNumeric),
        ("gsm.sim.state", DetailKeys.SimState),
        ("ro.boot.verifiedbootstate", DetailKeys.VerifiedBootState),
        ("ro.boot.flash.locked", DetailKeys.BootloaderLocked),
        ("ro.boot.vbmeta.device_state", DetailKeys.VbmetaDeviceState),
        ("persist.sys.timezone", DetailKeys.Timezone),
    ];

    internal static readonly string[] PropsFields = [.. PropKeys.Select(p => p.Key), DetailKeys.Kernel];
    internal static readonly string[] SettingsFields = [DetailKeys.AutoTime, DetailKeys.AutoTimeZone];
    internal static readonly string[] WifiFields = [DetailKeys.WifiState, DetailKeys.WifiSsid];

    /// <summary><c>adb -s &lt;serial&gt; get-state</c> devolvió <c>device</c>.</summary>
    public async Task<bool> IsPresentAsync(string serial, CancellationToken ct)
    {
        var r = await run(["-s", serial, .. GetStateArgs], GetStateTimeout, ct);
        return (r.Stdout ?? "").Trim() == "device";
    }

    /// <summary>
    /// Orden de §4.2: presencia, 3 muestras de <c>date</c> (lo más cerca posible del inicio) y después PROPS,
    /// SETTINGS y WIFI, secuenciales. Un script que falla no aborta los demás. Sin dispositivo:
    /// <see cref="DeviceNotFoundException"/> sin correr nada más.
    /// </summary>
    public async Task<AndroidDetailsReading> ReadAsync(string serial, CancellationToken ct)
    {
        var started = time.GetUtcNow();
        if (!await IsPresentAsync(serial, ct)) throw new DeviceNotFoundException();

        var clock = await SampleClockAsync(serial, ct);

        var fields = new Dictionary<string, DetailField>(StringComparer.Ordinal);
        var props = await RunScriptAsync(serial, PropsScript, ct);
        Merge(fields, PropsFields, props, ParseProps);
        var settings = await RunScriptAsync(serial, SettingsScript, ct);
        Merge(fields, SettingsFields, settings, ParseSettings);
        var wifi = await RunScriptAsync(serial, WifiScript, ct);
        Merge(fields, WifiFields, wifi, ParseWifi);

        var ordered = new Dictionary<string, DetailField>(StringComparer.Ordinal);
        foreach (var k in DetailKeys.All)
            ordered[k] = fields.TryGetValue(k, out var f) ? f : DetailField.Missing(DetailReasons.Error);
        return new AndroidDetailsReading(ordered, clock, started, time.GetUtcNow());
    }

    /// <summary>Solo el reloj (medición de cierre): presencia + 3 muestras de <c>date</c>.</summary>
    public async Task<DeviceClockReading> MeasureClockAsync(string serial, CancellationToken ct)
    {
        if (!await IsPresentAsync(serial, ct)) throw new DeviceNotFoundException();
        return await SampleClockAsync(serial, ct);
    }

    private async Task<DeviceClockReading> SampleClockAsync(string serial, CancellationToken ct)
    {
        var samples = new List<ClockSample>(DeviceClockMath.MaxSamples);
        for (var i = 0; i < DeviceClockMath.MaxSamples; i++)
        {
            var b = time.GetUtcNow();
            var r = await run(["-s", serial, "shell", DateScript], DateTimeout, ct);
            var a = time.GetUtcNow();
            samples.Add(ToSample(b, a, r));
        }
        return DeviceClockMath.Compute(samples);
    }

    internal static ClockSample ToSample(DateTimeOffset b, DateTimeOffset a, ProcessResult r)
    {
        if (r.ExitCode == ProcessRunner.TimeoutExitCode) return ClockSample.Failed(b, a, DetailReasons.Timeout);
        if (r.ExitCode != 0) return ClockSample.Failed(b, a, DetailReasons.Error);
        return DeviceClockMath.ParseAndroidDate(r.Stdout, out var ticks, out var res)
            ? new ClockSample(b, a, ticks, res, null)
            : ClockSample.Failed(b, a, DetailReasons.Invalid);
    }

    private Task<ProcessResult> RunScriptAsync(string serial, string script, CancellationToken ct) =>
        run(["-s", serial, "shell", script], ScriptTimeout, ct);

    /// <summary>Errores por script (§4.2): -2 → timeout; -1 o ≠0 sin stdout → error; ≠0 con stdout se parsea.</summary>
    private static void Merge(Dictionary<string, DetailField> fields, string[] keys, ProcessResult r,
        Func<string, IReadOnlyDictionary<string, DetailField>> parse)
    {
        string? failure = null;
        if (r.ExitCode == ProcessRunner.TimeoutExitCode) failure = DetailReasons.Timeout;
        else if (r.ExitCode == -1 || (r.ExitCode != 0 && string.IsNullOrWhiteSpace(r.Stdout)))
            failure = DetailReasons.Error;

        if (failure is not null)
        {
            foreach (var k in keys) fields[k] = DetailField.Missing(failure);
            return;
        }
        var parsed = parse(r.Stdout ?? "");
        foreach (var k in keys)
            fields[k] = parsed.TryGetValue(k, out var f) ? f : DetailField.Missing(DetailReasons.Error);
    }

    // ── Parsers puros ──────────────────────────────────────────────────────────

    internal static string[] Lines(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

    /// <summary>Divide en el primer renglón que es exactamente el separador.</summary>
    internal static (string[] Before, string[]? After) SplitAt(string text, string separator)
    {
        var lines = Lines(text);
        var i = Array.FindIndex(lines, l => l.Trim() == separator);
        return i < 0 ? (lines, null) : (lines[..i], lines[(i + 1)..]);
    }

    /// <summary>Salida de <see cref="PropsScript"/> → claves 1–16 (§4.2).</summary>
    internal static IReadOnlyDictionary<string, DetailField> ParseProps(string stdout)
    {
        var (before, after) = SplitAt(stdout, Separator);
        var raw = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in before)
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var prop = line[..eq].Trim();
            if (!raw.ContainsKey(prop)) raw[prop] = line[(eq + 1)..];
        }
        var fields = new Dictionary<string, DetailField>(StringComparer.Ordinal);
        foreach (var (prop, key) in PropKeys)
            fields[key] = DetailRules.Normalize(key, raw.TryGetValue(prop, out var v) ? v : null);
        fields[DetailKeys.Kernel] =
            DetailRules.Normalize(DetailKeys.Kernel, after is null ? null : string.Join("\n", after).Trim());
        return fields;
    }

    /// <summary>Salida de <see cref="SettingsScript"/> → <c>auto_time</c> y <c>auto_time_zone</c>.</summary>
    internal static IReadOnlyDictionary<string, DetailField> ParseSettings(string stdout)
    {
        var (before, after) = SplitAt(stdout, Separator);
        return new Dictionary<string, DetailField>(StringComparer.Ordinal)
        {
            [DetailKeys.AutoTime] = DetailRules.Normalize(DetailKeys.AutoTime, string.Join("\n", before).Trim()),
            [DetailKeys.AutoTimeZone] = after is null
                ? DetailField.Missing(DetailReasons.Error)
                : DetailRules.Normalize(DetailKeys.AutoTimeZone, string.Join("\n", after).Trim()),
        };
    }

    /// <summary>Salida de <see cref="WifiScript"/> → <c>wifi_state</c> y <c>wifi_ssid</c> (§4.2, en ese orden).</summary>
    internal static IReadOnlyDictionary<string, DetailField> ParseWifi(string stdout)
    {
        var lines = Lines(stdout).Where(l => l.Trim().Length > 0).ToArray();
        if (lines.Length == 0) return Wifi(DetailReasons.NotExposed);

        if (lines[0].Trim() == DumpsysMarker)
        {
            var rest = lines.Skip(1).ToArray();
            if (rest.Any(l => l.Trim() == "Wi-Fi is disabled")) return Wifi("disabled", DetailReasons.NotConnected);
            var stateLine = rest.FirstOrDefault(l => l.StartsWith("state=", StringComparison.Ordinal));
            if (stateLine is null) return Wifi(DetailReasons.NotExposed);
            var m = DumpsysState().Match(stateLine);
            if (!m.Success) return Wifi(DetailReasons.NotExposed);
            return m.Groups[1].Value == "COMPLETED"
                ? Connected(m.Groups[2].Value)
                : Wifi("disconnected", DetailReasons.NotConnected);
        }

        if (lines.Any(l => l.Trim() == "Wifi is disabled")) return Wifi("disabled", DetailReasons.NotConnected);
        const string connectedPrefix = "Wifi is connected to ";
        var connected = lines.FirstOrDefault(l => l.StartsWith(connectedPrefix, StringComparison.Ordinal));
        if (connected is not null) return Connected(connected[connectedPrefix.Length..]);
        if (lines.Any(l => l.Trim() is "Wifi is not connected" or "Wifi is enabled"))
            return Wifi("disconnected", DetailReasons.NotConnected);
        return Wifi(DetailReasons.NotExposed);
    }

    private static Dictionary<string, DetailField> Wifi(string reasonBoth) => new(StringComparer.Ordinal)
    {
        [DetailKeys.WifiState] = DetailField.Missing(reasonBoth),
        [DetailKeys.WifiSsid] = DetailField.Missing(reasonBoth),
    };

    private static Dictionary<string, DetailField> Wifi(string state, string ssidReason) => new(StringComparer.Ordinal)
    {
        [DetailKeys.WifiState] = DetailField.Of(state),
        [DetailKeys.WifiSsid] = DetailField.Missing(ssidReason),
    };

    /// <summary>SSID sin las comillas dobles de los extremos; <c>&lt;unknown ssid&gt;</c> o vacío → <c>masked</c>.</summary>
    private static Dictionary<string, DetailField> Connected(string rawSsid)
    {
        var ssid = rawSsid.Trim(' ');
        if (ssid.Length >= 2 && ssid[0] == '"' && ssid[^1] == '"') ssid = ssid[1..^1];
        var field = DetailRules.Clean(ssid) is "" or "<unknown ssid>"
            ? DetailField.Missing(DetailReasons.Masked)
            : DetailRules.Normalize(DetailKeys.WifiSsid, ssid);
        return new(StringComparer.Ordinal)
        {
            [DetailKeys.WifiState] = DetailField.Of("connected"),
            [DetailKeys.WifiSsid] = field,
        };
    }

    [GeneratedRegex(@"^state=([A-Z_]+) ssid=(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex DumpsysState();
}

/// <summary><c>adb get-state</c> no devolvió <c>device</c>: el endpoint responde 404 <c>device_not_found</c>.</summary>
public sealed class DeviceNotFoundException() : Exception("El dispositivo no está conectado o no autorizó la conexión.");
