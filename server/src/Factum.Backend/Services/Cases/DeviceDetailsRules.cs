using System.Globalization;
using System.Text.RegularExpressions;
using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Cases;

/// <summary>
/// metadatos-dispositivo §4.5, §5.1 y §5.2 (puro). La ficha la controla el dispositivo: se sanea de nuevo antes de
/// guardar con la misma regla que <c>DetailRules</c> de Tatana (misma tabla de casos). Nada de esto devuelve 400:
/// un dato malo queda marcado (<c>invalid</c>/<c>error</c>). Los valores descartados no se guardan ni se loguean.
/// </summary>
public static partial class DeviceDetailsRules
{
    public const int FreeTextMax = 64;
    public const int MaxAgentVersionLength = 32;
    private static readonly long MaxClockRttTicks = TimeSpan.FromSeconds(5).Ticks;
    private static readonly long MaxClockDistanceTicks = TimeSpan.FromDays(365.25 * 50).Ticks;
    private static readonly long MaxProbeRttTicks = TimeSpan.FromSeconds(10).Ticks;
    private static readonly TimeSpan ProbeMaxAge = TimeSpan.FromSeconds(300);
    private static readonly TimeSpan ProbeMaxFuture = TimeSpan.FromSeconds(1);

    private static readonly Dictionary<string, Regex> Patterns = new(StringComparer.Ordinal)
    {
        [DeviceDetailKeys.SerialNumber] = new(@"^[A-Za-z0-9._:-]{1,64}$", RegexOptions.CultureInvariant),
        [DeviceDetailKeys.OsVersion] = new(@"^[0-9A-Za-z.]{1,16}$", RegexOptions.CultureInvariant),
        [DeviceDetailKeys.OsApiLevel] = new(@"^[0-9]{1,3}$", RegexOptions.CultureInvariant),
        [DeviceDetailKeys.BuildId] = new(@"^[\x21-\x7E]{1,64}$", RegexOptions.CultureInvariant),
        [DeviceDetailKeys.BuildFingerprint] = new(@"^[\x21-\x7E]{1,200}$", RegexOptions.CultureInvariant),
        [DeviceDetailKeys.SecurityPatch] = new(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant),
        [DeviceDetailKeys.Kernel] = new(@"^[\x20-\x7E]{1,300}$", RegexOptions.CultureInvariant),
        [DeviceDetailKeys.NetworkOperatorNumeric] = new(@"^[0-9,]{1,32}$", RegexOptions.CultureInvariant),
        [DeviceDetailKeys.SimOperatorNumeric] = new(@"^[0-9,]{1,32}$", RegexOptions.CultureInvariant),
        [DeviceDetailKeys.SimState] = new(@"^[A-Za-z_,]{1,64}$", RegexOptions.CultureInvariant),
        [DeviceDetailKeys.VerifiedBootState] = new(@"^[a-z]{1,16}$", RegexOptions.CultureInvariant),
        [DeviceDetailKeys.BootloaderLocked] = new(@"^[01]$", RegexOptions.CultureInvariant),
        [DeviceDetailKeys.VbmetaDeviceState] = new(@"^[a-z]{1,16}$", RegexOptions.CultureInvariant),
        [DeviceDetailKeys.Timezone] = new(@"^[A-Za-z0-9_+\-/]{1,64}$", RegexOptions.CultureInvariant),
        [DeviceDetailKeys.AutoTime] = new(@"^[01]$", RegexOptions.CultureInvariant),
        [DeviceDetailKeys.AutoTimeZone] = new(@"^[01]$", RegexOptions.CultureInvariant),
        [DeviceDetailKeys.WifiState] = new(@"^(connected|disconnected|disabled)$", RegexOptions.CultureInvariant),
    };

    private static readonly HashSet<string> FreeTextKeys = new(
        [DeviceDetailKeys.NetworkOperator, DeviceDetailKeys.SimOperator, DeviceDetailKeys.WifiSsid],
        StringComparer.Ordinal);

    private static readonly HashSet<string> KnownKeys = new(DeviceDetailKeys.All, StringComparer.Ordinal);
    private static readonly HashSet<string> KnownReasons = new(DeviceDetailReasons.All, StringComparer.Ordinal);

    /// <summary><c>SafeText.Clean(SingleLine)</c> + Trim + colapso de espacios (§4.5.1).</summary>
    public static string Clean(string? value) =>
        MultiSpace().Replace(SafeText.Clean(value, TextShape.SingleLine).Trim(), " ");

    /// <summary>Valor crudo → campo (misma regla que <c>DetailRules.Normalize</c> de Tatana).</summary>
    public static DeviceDetailField Normalize(string key, string? raw)
    {
        var v = Clean(raw);
        if (v.Length == 0) return Missing(DeviceDetailReasons.Empty);

        switch (key)
        {
            case DeviceDetailKeys.SerialNumber when string.Equals(v, "unknown", StringComparison.OrdinalIgnoreCase):
            case DeviceDetailKeys.AutoTime or DeviceDetailKeys.AutoTimeZone when v == "null":
                return Missing(DeviceDetailReasons.Empty);
            case DeviceDetailKeys.WifiSsid when v == "<unknown ssid>":
                return Missing(DeviceDetailReasons.Masked);
        }

        if (FreeTextKeys.Contains(key))
            return new StringInfo(v).LengthInTextElements <= FreeTextMax ? Of(v) : Missing(DeviceDetailReasons.Invalid);

        if (!Patterns.TryGetValue(key, out var re)) return Missing(DeviceDetailReasons.Error);
        if (!re.IsMatch(v)) return Missing(DeviceDetailReasons.Invalid);
        if (key == DeviceDetailKeys.SecurityPatch &&
            !DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return Missing(DeviceDetailReasons.Invalid);
        return Of(v);
    }

    /// <summary>
    /// §4.5.6: siempre devuelve las 20 claves. Clave desconocida → se ignora; clave que falta → <c>error</c>;
    /// <c>reason</c> fuera del enum → <c>error</c>; <c>value</c> + <c>reason</c> → solo el <c>reason</c>.
    /// </summary>
    public static Dictionary<string, DeviceDetailField> NormalizeFields(IReadOnlyDictionary<string, DeviceDetailFieldDto>? raw)
    {
        var result = new Dictionary<string, DeviceDetailField>(StringComparer.Ordinal);
        foreach (var key in DeviceDetailKeys.All)
        {
            if (raw is null || !raw.TryGetValue(key, out var f) || f is null)
            {
                result[key] = Missing(DeviceDetailReasons.Error);
                continue;
            }
            if (f.Reason is not null)
            {
                var reason = f.Reason.Trim();
                result[key] = Missing(KnownReasons.Contains(reason) ? reason : DeviceDetailReasons.Error);
                continue;
            }
            result[key] = Normalize(key, f.Value);
        }
        return result;
    }

    /// <summary>"Datos no disponibles" (§4.4): motivo distinto de <c>not_applicable</c> y <c>not_connected</c>.</summary>
    public static int UnavailableCount(IReadOnlyDictionary<string, DeviceDetailField>? fields) =>
        fields?.Values.Count(f => f.Reason is not null and not (DeviceDetailReasons.NotApplicable or DeviceDetailReasons.NotConnected)) ?? 0;

    /// <summary>
    /// §5.1, recalculado desde las horas crudas (D-T3): la cifra guardada no depende del cliente. Sin horas →
    /// <c>reason</c> del cliente si está en el enum del reloj, si no <c>error</c>. Horas fuera de las cotas o
    /// resolución distinta de 1/1000 → <c>invalid</c>. Llena <paramref name="m"/> (sin tocar Kind/RecordedAt).
    /// </summary>
    public static void ComputeClock(DeviceClockDto? clock, ClockMeasurement m)
    {
        m.Reason = null;
        m.PcBefore = m.PcAfter = m.DeviceTime = null;
        m.ResolutionMs = null;
        m.DeviceMinusPcMs = m.MarginMs = null;

        if (clock is not { PcBefore: { } pb, PcAfter: { } pa, DeviceTime: { } dt })
        {
            var reason = clock?.Reason?.Trim();
            m.Reason = reason is not null && DeviceDetailReasons.Clock.Contains(reason) ? reason : DeviceDetailReasons.Error;
            return;
        }
        if (clock.ResolutionMs is not (1 or 1000))
        {
            m.Reason = DeviceDetailReasons.Invalid;
            return;
        }
        long b = pb.UtcTicks, a = pa.UtcTicks, d = dt.UtcTicks;
        var rtt = a - b;
        if (rtt < 0 || rtt > MaxClockRttTicks || Math.Abs(d - b) >= MaxClockDistanceTicks)
        {
            m.Reason = DeviceDetailReasons.Invalid;
            return;
        }
        var (diff, margin) = Offsets(b, a, d, clock.ResolutionMs.Value);
        m.PcBefore = pb.UtcDateTime;
        m.PcAfter = pa.UtcDateTime;
        m.DeviceTime = dt.UtcDateTime;
        m.ResolutionMs = clock.ResolutionMs;
        m.DeviceMinusPcMs = diff;
        m.MarginMs = margin;
    }

    /// <summary>
    /// Igual que <c>DeviceClockMath.Offsets</c> de Tatana: <c>mid = b + (a − b)/2</c>, diferencia redondeada half away
    /// from zero, margen = <c>ceil(RTT/2 + resolución)</c>. Ticks y decimal, sin double.
    /// </summary>
    public static (long DeviceMinusPcMs, long MarginMs) Offsets(long b, long a, long d, int resolutionMs)
    {
        var mid = b + (a - b) / 2;
        var diff = (long)Math.Round((decimal)(d - mid) / TimeSpan.TicksPerMillisecond, MidpointRounding.AwayFromZero);
        var margin = (long)Math.Ceiling((decimal)(a - b) / 2m / TimeSpan.TicksPerMillisecond + resolutionMs);
        return (diff, margin);
    }

    /// <summary>
    /// §5.2: <c>pc_minus_server_ms = round(mid(pc) − server_time)</c>, margen <c>ceil(RTT/2) + 1</c>. Sin sondeo →
    /// <c>not_measured</c>; RTT negativo o &gt; 10 s, o <c>server_time</c> fuera de [now − 300 s, now + 1 s] → <c>invalid</c>.
    /// </summary>
    public static void ComputePcServer(PcServerProbeDto? probe, DateTime nowUtc, ClockMeasurement m)
    {
        m.PcMinusServerMs = m.PcServerMarginMs = null;
        m.PcServerReason = null;
        if (probe is null || (probe.PcBefore is null && probe.ServerTime is null && probe.PcAfter is null))
        {
            m.PcServerReason = DeviceDetailReasons.NotMeasured;
            return;
        }
        if (probe is not { PcBefore: { } pb, ServerTime: { } st, PcAfter: { } pa })
        {
            m.PcServerReason = DeviceDetailReasons.Invalid;
            return;
        }
        long b = pb.UtcTicks, a = pa.UtcTicks, s = st.UtcTicks;
        var now = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc).Ticks;
        var rtt = a - b;
        if (rtt < 0 || rtt > MaxProbeRttTicks || s < now - ProbeMaxAge.Ticks || s > now + ProbeMaxFuture.Ticks)
        {
            m.PcServerReason = DeviceDetailReasons.Invalid;
            return;
        }
        var mid = b + rtt / 2;
        m.PcMinusServerMs = (long)Math.Round((decimal)(mid - s) / TimeSpan.TicksPerMillisecond,
            MidpointRounding.AwayFromZero);
        m.PcServerMarginMs = (long)Math.Ceiling((decimal)rtt / 2m / TimeSpan.TicksPerMillisecond) + 1;
    }

    /// <summary>Versión de Tatana saneada; más de 32 caracteres → "".</summary>
    public static string CleanAgentVersion(string? raw)
    {
        var v = Clean(raw);
        return v.Length > MaxAgentVersionLength ? "" : v;
    }

    private static DeviceDetailField Of(string v) => new() { Value = v };
    private static DeviceDetailField Missing(string reason) => new() { Reason = reason };

    [GeneratedRegex(" {2,}", RegexOptions.CultureInvariant)]
    private static partial Regex MultiSpace();
}
