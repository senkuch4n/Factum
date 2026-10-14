using System.Collections.Concurrent;
using System.Diagnostics;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services.Ios;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services.DeviceDetails;

/// <summary>
/// Ficha técnica y reloj del dispositivo bajo demanda (metadatos-dispositivo §4, §7.2). Solo lectura. Sin
/// dispositivo: <see cref="DeviceNotFoundException"/> (Android) o <see cref="IosException"/> (iPhone).
/// </summary>
public interface IDeviceDetailsService
{
    Task<DeviceDetailsResponse> ReadAsync(string serial, string platform, CancellationToken ct);
    Task<DeviceClockResponse> MeasureClockAsync(string serial, string platform, CancellationToken ct);
}

public sealed class DeviceDetailsService : IDeviceDetailsService
{
    public const string Android = "android";
    public const string Ios = "ios";

    // Misma resolución que AdbService/AndroidScreenProbe (portátil, PATH o Homebrew).
    private static readonly Lazy<string> AdbPath = new(() => ToolResolver.Find(AgentTools.Adb)?.Path ?? "adb");

    internal static readonly TimeSpan MockDelay = TimeSpan.FromMilliseconds(300);

    private readonly bool _mock;
    private readonly int _mockSkewSeconds;
    private readonly IIosService _ios;
    private readonly TimeProvider _time;
    private readonly ILogger<DeviceDetailsService> _log;
    private readonly AndroidDetailsReader _android;
    // Un semáforo por serial: dos pestañas que piden la ficha del mismo celular no se pisan los `date` (§7.2).
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public DeviceDetailsService(IOptions<AgentOptions> opts, IIosService ios, TimeProvider time,
        ILogger<DeviceDetailsService> log)
        : this(opts, ios, time, log,
            (args, timeout, ct) => ProcessRunner.RunArgumentListAsync(AdbPath.Value, args, ct, timeout: timeout))
    {
    }

    /// <summary>Para los tests: el ejecutor de adb inyectado.</summary>
    internal DeviceDetailsService(IOptions<AgentOptions> opts, IIosService ios, TimeProvider time,
        ILogger<DeviceDetailsService> log, AdbRunner run)
    {
        _mock = opts.Value.Mock;
        _mockSkewSeconds = opts.Value.MockDeviceClockSkewSeconds;
        _ios = ios;
        _time = time;
        _log = log;
        _android = new AndroidDetailsReader(run, time);
    }

    public async Task<DeviceDetailsResponse> ReadAsync(string serial, string platform, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var gate = _locks.GetOrAdd(serial, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            DeviceDetailsResponse response;
            if (_mock)
            {
                response = await MockReadAsync(serial, platform, ct);
            }
            else if (platform == Ios)
            {
                var started = _time.GetUtcNow();
                var stdout = await _ios.RunDetailsHelperAsync(serial, ct);
                var (fields, clock) = IosDetailsParser.Parse(stdout);
                response = new DeviceDetailsResponse(serial, Ios, AgentVersion.Current, started, _time.GetUtcNow(),
                    fields, clock);
            }
            else
            {
                var r = await _android.ReadAsync(serial, ct);
                response = new DeviceDetailsResponse(serial, Android, AgentVersion.Current, r.StartedAt, r.FinishedAt,
                    r.Fields, r.Clock);
            }
            // §4.6: nunca valores, SSID, salidas crudas ni serial.
            _log.LogInformation("Ficha técnica leída ({Platform}, {Unavailable} no disponibles, {Ms} ms)",
                response.Platform, DetailReasons.UnavailableCount(response.Fields), sw.ElapsedMilliseconds);
            return response;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<DeviceClockResponse> MeasureClockAsync(string serial, string platform, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(serial, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            DeviceClockReading clock;
            if (_mock)
            {
                await Task.Delay(MockDelay, ct);
                clock = MockClock();
            }
            else if (platform == Ios)
            {
                var stdout = await _ios.RunDetailsHelperAsync(serial, ct);
                clock = IosDetailsParser.Parse(stdout).Clock;
            }
            else
            {
                clock = await _android.MeasureClockAsync(serial, ct);
            }
            return new DeviceClockResponse(serial, platform == Ios ? Ios : Android, AgentVersion.Current, clock);
        }
        finally
        {
            gate.Release();
        }
    }

    // ── Mock (§4.7) ────────────────────────────────────────────────────────────

    /// <summary>Salida real de PROPS en el Samsung SM-M127F (fixture §13.1).</summary>
    internal const string MockAndroidProps = """
        ro.serialno=R58RA3R5AGA
        ro.build.version.release=11
        ro.build.version.sdk=30
        ro.build.id=RP1A.200720.012
        ro.build.fingerprint=samsung/a12sub/a12s:11/RP1A.200720.012/A127MUBU3AUJ5:user/release-keys
        ro.build.version.security_patch=2021-10-01
        gsm.operator.alpha=
        gsm.operator.numeric=722310
        gsm.sim.operator.alpha=
        gsm.sim.operator.numeric=
        gsm.sim.state=ABSENT
        ro.boot.verifiedbootstate=orange
        ro.boot.flash.locked=0
        ro.boot.vbmeta.device_state=unlocked
        persist.sys.timezone=America/Argentina/Buenos_Aires
        __FX_SEP__
        Linux localhost 4.19.111-22565658 #1 SMP PREEMPT Thu Nov 11 01:08:09 KST 2021 aarch64
        """;

    /// <summary>Salida sintética del helper de iPhone (fixture §13.1, sin el reloj: el mock lo arma aparte).</summary>
    internal const string MockIosDetails =
        """{"udid":"mock","values":{"SerialNumber":"F2LZK0ABCDEF","ProductVersion":"17.4.1","BuildVersion":"21E236","TimeZone":"America/Argentina/Buenos_Aires","SIMStatus":"kCTSIMSupportSIMStatusReady"},"carriers":[{"bundle":"com.apple.Personal_ar","mcc":"722","mnc":"34"}],"clock":[],"clock_error":null}""";

    private async Task<DeviceDetailsResponse> MockReadAsync(string serial, string platform, CancellationToken ct)
    {
        var started = _time.GetUtcNow();
        await Task.Delay(MockDelay, ct);
        Dictionary<string, DetailField> fields;
        if (platform == Ios)
        {
            fields = new Dictionary<string, DetailField>(IosDetailsParser.Parse(MockIosDetails).Fields,
                StringComparer.Ordinal);
        }
        else
        {
            var parsed = new Dictionary<string, DetailField>(StringComparer.Ordinal);
            foreach (var (k, v) in AndroidDetailsReader.ParseProps(MockAndroidProps)) parsed[k] = v;
            foreach (var (k, v) in AndroidDetailsReader.ParseSettings("1\n__FX_SEP__\n1")) parsed[k] = v;
            parsed[DetailKeys.WifiState] = DetailField.Of("connected");
            parsed[DetailKeys.WifiSsid] = DetailField.Of("Red de prueba");
            fields = new Dictionary<string, DetailField>(StringComparer.Ordinal);
            foreach (var k in DetailKeys.All) fields[k] = parsed[k];
        }
        return new DeviceDetailsResponse(serial, platform == Ios ? Ios : Android, AgentVersion.Current, started,
            _time.GetUtcNow(), fields, MockClock());
    }

    /// <summary>
    /// Reloj del mock: RTT de 8 ms y resolución de 1 ms → <c>margin_ms = 5</c>; el dispositivo difiere
    /// <c>MockDeviceClockSkewSeconds</c> del punto medio. La API recalcula lo mismo desde las tres horas.
    /// </summary>
    internal DeviceClockReading MockClock()
    {
        var b = _time.GetUtcNow().UtcTicks;
        var a = b + 8 * TimeSpan.TicksPerMillisecond;
        var mid = b + (a - b) / 2;
        var d = mid + _mockSkewSeconds * TimeSpan.TicksPerSecond;
        var (diff, margin) = DeviceClockMath.Offsets(b, a, d, 1);
        return new DeviceClockReading(new DateTimeOffset(b, TimeSpan.Zero), new DateTimeOffset(a, TimeSpan.Zero),
            new DateTimeOffset(d, TimeSpan.Zero), 1, diff, margin, DeviceClockMath.MaxSamples, null);
    }
}
