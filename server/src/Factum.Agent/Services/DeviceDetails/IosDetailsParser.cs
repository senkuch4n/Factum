using System.Text.Json;
using Factum.Agent.Models;

namespace Factum.Agent.Services.DeviceDetails;

/// <summary>
/// Salida de <c>ios_helper.py details</c> → ficha y reloj (metadatos-dispositivo §4.3). Puro. Tolera la ausencia de
/// cualquier clave de lockdown (da <c>empty</c>); una salida que no es JSON da <c>error</c> en los campos que iOS
/// sí informa y en el reloj.
/// </summary>
public static class IosDetailsParser
{
    private const string AppleBundlePrefix = "com.apple.";
    private const string SimStatusPrefix = "kCTSIMSupportSIMStatus";

    /// <summary>Claves sin lectura en iOS: no existen en la plataforma.</summary>
    internal static readonly string[] NotApplicable =
    [
        DetailKeys.OsApiLevel, DetailKeys.BuildFingerprint, DetailKeys.SecurityPatch,
        DetailKeys.VerifiedBootState, DetailKeys.BootloaderLocked, DetailKeys.VbmetaDeviceState,
    ];

    /// <summary>Claves que iOS no informa por una consulta de solo lectura.</summary>
    internal static readonly string[] NotExposed =
    [
        DetailKeys.Kernel, DetailKeys.NetworkOperator, DetailKeys.NetworkOperatorNumeric, DetailKeys.AutoTime,
        DetailKeys.AutoTimeZone, DetailKeys.WifiState, DetailKeys.WifiSsid,
    ];

    public static (IReadOnlyDictionary<string, DetailField> Fields, DeviceClockReading Clock) Parse(string? stdout)
    {
        var found = new Dictionary<string, DetailField>(StringComparer.Ordinal);
        DeviceClockReading clock;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(stdout) ? "null" : stdout);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException("no es un objeto");

            var values = root.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Object
                ? v : default;
            found[DetailKeys.SerialNumber] = DetailRules.Normalize(DetailKeys.SerialNumber, Str(values, "SerialNumber"));
            found[DetailKeys.OsVersion] = DetailRules.Normalize(DetailKeys.OsVersion, Str(values, "ProductVersion"));
            found[DetailKeys.BuildId] = DetailRules.Normalize(DetailKeys.BuildId, Str(values, "BuildVersion"));
            found[DetailKeys.Timezone] = DetailRules.Normalize(DetailKeys.Timezone, Str(values, "TimeZone"));
            var sim = Str(values, "SIMStatus");
            if (sim is not null && sim.StartsWith(SimStatusPrefix, StringComparison.Ordinal))
                sim = sim[SimStatusPrefix.Length..];
            found[DetailKeys.SimState] = DetailRules.Normalize(DetailKeys.SimState, sim);

            var bundles = new List<string>();
            var numerics = new List<string>();
            if (root.TryGetProperty("carriers", out var carriers) && carriers.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in carriers.EnumerateArray())
                {
                    if (c.ValueKind != JsonValueKind.Object) continue;
                    var bundle = Str(c, "bundle") ?? "";
                    if (bundle.StartsWith(AppleBundlePrefix, StringComparison.Ordinal))
                        bundle = bundle[AppleBundlePrefix.Length..];
                    if (bundle.Length > 0) bundles.Add(bundle);
                    var mcc = Str(c, "mcc") ?? "";
                    var mnc = Str(c, "mnc") ?? "";
                    if (IsDigits(mcc) && IsDigits(mnc)) numerics.Add(mcc + mnc);
                }
            }
            found[DetailKeys.SimOperator] = bundles.Count == 0
                ? DetailField.Missing(DetailReasons.Empty)
                : DetailRules.Normalize(DetailKeys.SimOperator, string.Join(",", bundles));
            found[DetailKeys.SimOperatorNumeric] = numerics.Count == 0
                ? DetailField.Missing(DetailReasons.Empty)
                : DetailRules.Normalize(DetailKeys.SimOperatorNumeric, string.Join(",", numerics));

            clock = ParseClock(root);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            found.Clear();
            clock = DeviceClockReading.Failed(DetailReasons.Error);
        }

        var fields = new Dictionary<string, DetailField>(StringComparer.Ordinal);
        foreach (var k in DetailKeys.All)
        {
            if (NotApplicable.Contains(k)) fields[k] = DetailField.Missing(DetailReasons.NotApplicable);
            else if (NotExposed.Contains(k)) fields[k] = DetailField.Missing(DetailReasons.NotExposed);
            else fields[k] = found.TryGetValue(k, out var f) ? f : DetailField.Missing(DetailReasons.Error);
        }
        return (fields, clock);
    }

    /// <summary>
    /// <c>clock</c> del helper: lista de <c>{pc_before_ns, device, pc_after_ns}</c>. Sin muestras (lista vacía o
    /// <c>clock_error</c>) → <c>error</c>; con muestras pero ninguna válida → <c>invalid</c>.
    /// </summary>
    internal static DeviceClockReading ParseClock(JsonElement root)
    {
        var samples = new List<ClockSample>();
        if (root.TryGetProperty("clock", out var clock) && clock.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in clock.EnumerateArray())
            {
                if (s.ValueKind != JsonValueKind.Object ||
                    !s.TryGetProperty("pc_before_ns", out var b) || b.ValueKind != JsonValueKind.Number ||
                    !s.TryGetProperty("pc_after_ns", out var a) || a.ValueKind != JsonValueKind.Number ||
                    !b.TryGetInt64(out var bNs) || !a.TryGetInt64(out var aNs))
                {
                    samples.Add(ClockSample.Failed(DateTimeOffset.MinValue, DateTimeOffset.MinValue,
                        DetailReasons.Invalid));
                    continue;
                }
                if (!s.TryGetProperty("device", out var d) || d.ValueKind != JsonValueKind.Number ||
                    !d.TryGetDecimal(out var device))
                {
                    samples.Add(ClockSample.Failed(DateTimeOffset.MinValue, DateTimeOffset.MinValue,
                        DetailReasons.Invalid));
                    continue;
                }
                try { samples.Add(DeviceClockMath.FromIosSample(bNs, device, aNs)); }
                catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
                {
                    samples.Add(ClockSample.Failed(DateTimeOffset.MinValue, DateTimeOffset.MinValue,
                        DetailReasons.Invalid));
                }
            }
        }
        if (samples.Count == 0) return DeviceClockReading.Failed(DetailReasons.Error);
        var reading = DeviceClockMath.Compute(samples);
        return reading.Samples == 0 && reading.Reason != DetailReasons.Timeout
            ? DeviceClockReading.Failed(DetailReasons.Invalid)
            : reading;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool IsDigits(string s) => s.Length > 0 && s.All(char.IsAsciiDigit);
}
