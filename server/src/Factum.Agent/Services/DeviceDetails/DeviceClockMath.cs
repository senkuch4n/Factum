using System.Globalization;
using System.Text.RegularExpressions;
using Factum.Agent.Models;

namespace Factum.Agent.Services.DeviceDetails;

/// <summary>
/// Una muestra de reloj (§5.1): <c>b</c> = hora de la PC antes, <c>a</c> = después, <c>d</c> = hora del dispositivo
/// (ticks UTC desde 0001-01-01). <see cref="Failure"/> no nulo = la muestra no tiene hora (<c>timeout</c> |
/// <c>error</c> | <c>invalid</c>).
/// </summary>
public sealed record ClockSample(DateTimeOffset PcBefore, DateTimeOffset PcAfter, long? DeviceTicks, int ResolutionMs,
    string? Failure)
{
    public static ClockSample Failed(DateTimeOffset b, DateTimeOffset a, string failure) => new(b, a, null, 0, failure);
}

/// <summary>
/// Matemática del reloj dispositivo ↔ PC (metadatos-dispositivo §5.1), todo en ticks (<c>long</c>) y
/// <c>decimal</c>: nada pasa por <c>double</c>. Signo: <c>device_minus_pc_ms</c> = dispositivo − PC (positivo =
/// el dispositivo adelanta). Margen = RTT/2 + resolución (cota estricta).
/// </summary>
public static partial class DeviceClockMath
{
    public const int MaxSamples = 3;
    private static readonly long MaxRttTicks = TimeSpan.FromSeconds(5).Ticks;
    private static readonly long MaxDistanceTicks = TimeSpan.FromDays(365.25 * 50).Ticks;
    private static readonly long EpochTicks = DateTime.UnixEpoch.Ticks;

    /// <summary>
    /// stdout de <c>date +%s.%N</c> → ticks UTC. Con fracción de 1 a 9 dígitos, resolución 1 ms (los primeros 7
    /// dígitos completados con ceros van a ticks). Sin fracción, o con <c>%N</c>/<c>N</c> literal (toybox viejo),
    /// solo los segundos y resolución 1000 ms. Otra cosa → false.
    /// </summary>
    public static bool ParseAndroidDate(string? stdout, out long ticks, out int resolutionMs)
    {
        ticks = 0;
        resolutionMs = 0;
        var text = (stdout ?? "").Trim();
        var m = AndroidDate().Match(text);
        string seconds;
        string? fraction = null;
        if (m.Success)
        {
            seconds = m.Groups[1].Value;
            if (m.Groups[2].Success) fraction = m.Groups[2].Value;
        }
        else
        {
            var lit = AndroidDateLiteralN().Match(text);
            if (!lit.Success) return false;
            seconds = lit.Groups[1].Value;
        }

        if (!long.TryParse(seconds, NumberStyles.None, CultureInfo.InvariantCulture, out var s)) return false;
        long frac = 0;
        if (fraction is not null)
        {
            var seven = fraction.Length >= 7 ? fraction[..7] : fraction.PadRight(7, '0');
            frac = long.Parse(seven, NumberStyles.None, CultureInfo.InvariantCulture);
            resolutionMs = 1;
        }
        else
        {
            resolutionMs = 1000;
        }
        ticks = EpochTicks + s * TimeSpan.TicksPerSecond + frac;
        return true;
    }

    /// <summary>Muestra de iOS del helper: <c>pc_*_ns</c> (ns Unix del mismo reloj de la PC) y <c>device</c> (s Unix).</summary>
    public static ClockSample FromIosSample(long pcBeforeNs, decimal device, long pcAfterNs)
    {
        var b = new DateTimeOffset(EpochTicks + pcBeforeNs / 100, TimeSpan.Zero);
        var a = new DateTimeOffset(EpochTicks + pcAfterNs / 100, TimeSpan.Zero);
        var whole = decimal.Truncate(device);
        var resolution = device != whole ? 1 : 1000;
        decimal ticksDec = EpochTicks + decimal.Truncate(device * TimeSpan.TicksPerSecond);
        if (ticksDec < DateTime.MinValue.Ticks || ticksDec > DateTime.MaxValue.Ticks)
            return ClockSample.Failed(b, a, DetailReasons.Invalid);
        return new ClockSample(b, a, (long)ticksDec, resolution, null);
    }

    /// <summary>Muestra válida (§5.1): <c>a ≥ b</c>, <c>a − b ≤ 5 s</c> y <c>d</c> a menos de 50 años de <c>b</c>.</summary>
    public static bool IsValid(ClockSample s)
    {
        if (s.Failure is not null || s.DeviceTicks is not { } d) return false;
        var rtt = s.PcAfter.UtcTicks - s.PcBefore.UtcTicks;
        if (rtt < 0 || rtt > MaxRttTicks) return false;
        return Math.Abs(d - s.PcBefore.UtcTicks) < MaxDistanceTicks;
    }

    /// <summary>La muestra válida de menor RTT; en un empate, la primera. null si no hay ninguna válida.</summary>
    public static ClockSample? Pick(IReadOnlyList<ClockSample> samples)
    {
        ClockSample? best = null;
        long bestRtt = long.MaxValue;
        foreach (var s in samples)
        {
            if (!IsValid(s)) continue;
            var rtt = s.PcAfter.UtcTicks - s.PcBefore.UtcTicks;
            if (rtt < bestRtt) { best = s; bestRtt = rtt; }
        }
        return best;
    }

    /// <summary>
    /// Resultado de la medición. Sin ninguna válida: <c>timeout</c> si todas vencieron, <c>invalid</c> si alguna
    /// respondió pero no parseó o falló las cotas, <c>error</c> en cualquier otro caso.
    /// </summary>
    public static DeviceClockReading Compute(IReadOnlyList<ClockSample> samples)
    {
        var best = Pick(samples);
        if (best is null)
        {
            if (samples.Count > 0 && samples.All(s => s.Failure == DetailReasons.Timeout))
                return DeviceClockReading.Failed(DetailReasons.Timeout);
            var anyInvalid = samples.Any(s => s.Failure == DetailReasons.Invalid || (s.Failure is null));
            return DeviceClockReading.Failed(anyInvalid ? DetailReasons.Invalid : DetailReasons.Error);
        }

        var b = best.PcBefore.UtcTicks;
        var a = best.PcAfter.UtcTicks;
        var d = best.DeviceTicks!.Value;
        var (diff, margin) = Offsets(b, a, d, best.ResolutionMs);
        return new DeviceClockReading(
            new DateTimeOffset(b, TimeSpan.Zero),
            new DateTimeOffset(a, TimeSpan.Zero),
            new DateTimeOffset(d, TimeSpan.Zero),
            best.ResolutionMs, diff, margin, samples.Count(IsValid), null);
    }

    /// <summary>
    /// <c>mid = b + (a − b) / 2</c>; <c>diff = round((d − mid) / 10 000)</c> (half away from zero);
    /// <c>margin = ceil((a − b) / 2 / 10 000 + resolución)</c>. En ticks y decimal.
    /// </summary>
    public static (long DeviceMinusPcMs, long MarginMs) Offsets(long b, long a, long d, int resolutionMs)
    {
        var mid = b + (a - b) / 2;
        var diff = (long)Math.Round((decimal)(d - mid) / TimeSpan.TicksPerMillisecond, MidpointRounding.AwayFromZero);
        var margin = (long)Math.Ceiling((decimal)(a - b) / 2m / TimeSpan.TicksPerMillisecond + resolutionMs);
        return (diff, margin);
    }

    [GeneratedRegex(@"^\s*([0-9]{9,11})(?:\.([0-9]{1,9}))?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex AndroidDate();

    [GeneratedRegex(@"^\s*([0-9]{9,11})\.(?:%N|N)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex AndroidDateLiteralN();
}
