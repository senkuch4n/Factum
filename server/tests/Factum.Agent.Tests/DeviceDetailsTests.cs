using System.Globalization;
using System.Text.Json;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services;
using Factum.Agent.Services.DeviceDetails;
using Factum.Agent.Services.Ios;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Tests;

// metadatos-dispositivo §13: fixtures reales del Samsung SM-M127F (Android 11) y sintéticos (wifi, props, date, iOS).

internal static class DetailsFixtures
{
    public static string Path(params string[] parts) =>
        System.IO.Path.Combine([AppContext.BaseDirectory, "Fixtures", "DeviceDetails", .. parts]);

    public static string Read(params string[] parts) => File.ReadAllText(Path(parts));

    public static DateTimeOffset FromUnix(string seconds) =>
        new(DateTime.UnixEpoch.Ticks + (long)(decimal.Parse(seconds, CultureInfo.InvariantCulture) * TimeSpan.TicksPerSecond),
            TimeSpan.Zero);

    /// <summary>Las 3 muestras reales: (pc_before, stdout de date, pc_after).</summary>
    public static List<(DateTimeOffset B, string Out, DateTimeOffset A)> RealDateSamples()
    {
        using var doc = JsonDocument.Parse(Read("samsung-sm-m127f-a11", "date-samples.json"));
        return doc.RootElement.EnumerateArray()
            .Select(e => (FromUnix(e.GetProperty("pc_before").GetString()!), e.GetProperty("out").GetString()!,
                FromUnix(e.GetProperty("pc_after").GetString()!)))
            .ToList();
    }

    public static readonly Dictionary<string, DetailField> RealAndroidExpected = new()
    {
        [DetailKeys.SerialNumber] = DetailField.Of("R58RA3R5AGA"),
        [DetailKeys.OsVersion] = DetailField.Of("11"),
        [DetailKeys.OsApiLevel] = DetailField.Of("30"),
        [DetailKeys.BuildId] = DetailField.Of("RP1A.200720.012"),
        [DetailKeys.BuildFingerprint] =
            DetailField.Of("samsung/a12sub/a12s:11/RP1A.200720.012/A127MUBU3AUJ5:user/release-keys"),
        [DetailKeys.SecurityPatch] = DetailField.Of("2021-10-01"),
        [DetailKeys.Kernel] =
            DetailField.Of("Linux localhost 4.19.111-22565658 #1 SMP PREEMPT Thu Nov 11 01:08:09 KST 2021 aarch64"),
        [DetailKeys.NetworkOperator] = DetailField.Missing("empty"),
        [DetailKeys.NetworkOperatorNumeric] = DetailField.Of("722310"),
        [DetailKeys.SimOperator] = DetailField.Missing("empty"),
        [DetailKeys.SimOperatorNumeric] = DetailField.Missing("empty"),
        [DetailKeys.SimState] = DetailField.Of("ABSENT"),
        [DetailKeys.VerifiedBootState] = DetailField.Of("orange"),
        [DetailKeys.BootloaderLocked] = DetailField.Of("0"),
        [DetailKeys.VbmetaDeviceState] = DetailField.Of("unlocked"),
        [DetailKeys.Timezone] = DetailField.Of("America/Argentina/Buenos_Aires"),
        [DetailKeys.AutoTime] = DetailField.Of("1"),
        [DetailKeys.AutoTimeZone] = DetailField.Of("1"),
        [DetailKeys.WifiState] = DetailField.Of("disconnected"),
        [DetailKeys.WifiSsid] = DetailField.Missing("not_connected"),
    };
}

/// <summary>TimeProvider que devuelve una secuencia fija (y después repite la última).</summary>
internal sealed class SequenceTimeProvider(params DateTimeOffset[] times) : TimeProvider
{
    private int _i;
    public override DateTimeOffset GetUtcNow() => times[Math.Min(_i++, times.Length - 1)];
}

/// <summary>Ejecutor de adb falso: responde por script y registra las llamadas.</summary>
internal sealed class FakeAdb
{
    public string GetState = "device";
    public ProcessResult Props = new(0, DetailsFixtures.Read("samsung-sm-m127f-a11", "props.txt"), "");
    public ProcessResult Settings = new(0, DetailsFixtures.Read("samsung-sm-m127f-a11", "settings.txt"), "");
    public ProcessResult Wifi = new(0, DetailsFixtures.Read("samsung-sm-m127f-a11", "wifi.txt"), "");
    public Queue<ProcessResult> Dates = new();
    public readonly List<(IReadOnlyList<string> Args, TimeSpan Timeout)> Calls = [];

    public Task<ProcessResult> Run(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        Calls.Add((args, timeout));
        var last = args[^1];
        ProcessResult r = last switch
        {
            "get-state" => new(0, GetState, ""),
            AndroidDetailsReader.PropsScript => Props,
            AndroidDetailsReader.SettingsScript => Settings,
            AndroidDetailsReader.WifiScript => Wifi,
            AndroidDetailsReader.DateScript => Dates.Count > 0 ? Dates.Dequeue() : new(-2, "", ""),
            _ => throw new InvalidOperationException("script inesperado"),
        };
        return Task.FromResult(r);
    }
}

public sealed class DeviceDetailsAndroidParsingTests
{
    private static void AssertFields(IReadOnlyDictionary<string, DetailField> expected,
        IReadOnlyDictionary<string, DetailField> actual)
    {
        foreach (var (k, v) in expected)
        {
            Assert.True(actual.ContainsKey(k), $"falta {k}");
            Assert.Equal(v, actual[k]);
        }
    }

    [Fact]
    public void Los_scripts_son_el_texto_exacto_de_la_SDD()
    {
        Assert.Equal(
            "for p in ro.serialno ro.build.version.release ro.build.version.sdk ro.build.id ro.build.fingerprint ro.build.version.security_patch gsm.operator.alpha gsm.operator.numeric gsm.sim.operator.alpha gsm.sim.operator.numeric gsm.sim.state ro.boot.verifiedbootstate ro.boot.flash.locked ro.boot.vbmeta.device_state persist.sys.timezone; do echo \"$p=$(getprop $p)\"; done; echo __FX_SEP__; uname -a",
            AndroidDetailsReader.PropsScript);
        Assert.Equal("settings get global auto_time; echo __FX_SEP__; settings get global auto_time_zone",
            AndroidDetailsReader.SettingsScript);
        Assert.Equal(
            "s=$(cmd wifi status 2>/dev/null | grep -E '^Wifi is '); if [ -n \"$s\" ]; then echo \"$s\"; else echo __FX_DUMPSYS__; dumpsys wifi 2>/dev/null | grep -m1 -E '^Wi-Fi is '; dumpsys wifi 2>/dev/null | grep -m1 'mWifiInfo' | sed -E 's/^.*mWifiInfo SSID: (.*), BSSID: .*Supplicant state: ([A-Z_]+).*$/state=\\2 ssid=\\1/' | grep '^state='; fi",
            AndroidDetailsReader.WifiScript);
        Assert.Equal("date +%s.%N", AndroidDetailsReader.DateScript);
        Assert.Equal(new[] { "get-state" }, AndroidDetailsReader.GetStateArgs);
    }

    [Fact]
    public void Ningun_script_escribe_en_el_dispositivo()
    {
        foreach (var script in new[]
                 {
                     AndroidDetailsReader.PropsScript, AndroidDetailsReader.SettingsScript,
                     AndroidDetailsReader.WifiScript, AndroidDetailsReader.DateScript,
                 })
        {
            Assert.DoesNotContain("settings put", script);
            Assert.DoesNotContain("setprop", script);
            Assert.DoesNotContain(" > ", script);
            Assert.DoesNotContain("svc ", script);
            Assert.DoesNotContain("date -s", script);
            Assert.DoesNotContain("cmd wifi set", script);
        }
    }

    [Fact]
    public void Props_real_SM_M127F()
    {
        var f = AndroidDetailsReader.ParseProps(DetailsFixtures.Read("samsung-sm-m127f-a11", "props.txt"));
        Assert.Equal(16, f.Count);
        AssertFields(DetailsFixtures.RealAndroidExpected.Where(kv => f.ContainsKey(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value), f);
    }

    [Fact]
    public void Props_con_CRLF_da_lo_mismo_que_el_real()
    {
        var real = AndroidDetailsReader.ParseProps(DetailsFixtures.Read("samsung-sm-m127f-a11", "props.txt"));
        var crlf = AndroidDetailsReader.ParseProps(DetailsFixtures.Read("synthetic-props", "crlf.txt"));
        Assert.Equal(real.OrderBy(k => k.Key), crlf.OrderBy(k => k.Key));
    }

    [Fact]
    public void Props_doble_SIM_crudos_con_coma()
    {
        var f = AndroidDetailsReader.ParseProps(DetailsFixtures.Read("synthetic-props", "dual-sim.txt"));
        Assert.Equal(DetailField.Of("READY,ABSENT"), f[DetailKeys.SimState]);
        Assert.Equal(DetailField.Of("Personal,"), f[DetailKeys.NetworkOperator]);
    }

    [Fact]
    public void Props_serial_unknown_es_empty_y_parche_imposible_es_invalid()
    {
        var u = AndroidDetailsReader.ParseProps(DetailsFixtures.Read("synthetic-props", "unknown-serial.txt"));
        Assert.Equal(DetailField.Missing("empty"), u[DetailKeys.SerialNumber]);
        var p = AndroidDetailsReader.ParseProps(DetailsFixtures.Read("synthetic-props", "bad-patch.txt"));
        Assert.Equal(DetailField.Missing("invalid"), p[DetailKeys.SecurityPatch]);
    }

    [Fact]
    public void Settings_real_y_null()
    {
        var f = AndroidDetailsReader.ParseSettings(DetailsFixtures.Read("samsung-sm-m127f-a11", "settings.txt"));
        Assert.Equal(DetailField.Of("1"), f[DetailKeys.AutoTime]);
        Assert.Equal(DetailField.Of("1"), f[DetailKeys.AutoTimeZone]);
        var n = AndroidDetailsReader.ParseSettings("null\n__FX_SEP__\n0\n");
        Assert.Equal(DetailField.Missing("empty"), n[DetailKeys.AutoTime]);
        Assert.Equal(DetailField.Of("0"), n[DetailKeys.AutoTimeZone]);
    }

    [Theory]
    [InlineData("samsung-sm-m127f-a11", "wifi.txt", "disconnected", null, "not_connected")]
    [InlineData("samsung-sm-m127f-a11", "wifi-dumpsys.txt", "disconnected", null, "not_connected")]
    [InlineData("synthetic-wifi", "connected-a11.txt", "connected", "Casa Ana", null)]
    [InlineData("synthetic-wifi", "connected-unknown.txt", "connected", null, "masked")]
    [InlineData("synthetic-wifi", "disabled.txt", "disabled", null, "not_connected")]
    [InlineData("synthetic-wifi", "dumpsys-completed.txt", "connected", "Oficina 2", null)]
    [InlineData("synthetic-wifi", "ssid-hostile.txt", "connected", "Redevil", null)]
    public void Wifi_fixtures(string dir, string file, string state, string? ssid, string? ssidReason)
    {
        var f = AndroidDetailsReader.ParseWifi(DetailsFixtures.Read(dir, file));
        Assert.Equal(DetailField.Of(state), f[DetailKeys.WifiState]);
        Assert.Equal(new DetailField(ssid, ssidReason), f[DetailKeys.WifiSsid]);
    }

    [Theory]
    [InlineData("__FX_DUMPSYS__\n")]
    [InlineData("")]
    [InlineData("algo raro\n")]
    public void Wifi_sin_datos_es_not_exposed(string output)
    {
        var f = AndroidDetailsReader.ParseWifi(output);
        Assert.Equal(DetailField.Missing("not_exposed"), f[DetailKeys.WifiState]);
        Assert.Equal(DetailField.Missing("not_exposed"), f[DetailKeys.WifiSsid]);
    }

    [Fact]
    public void Wifi_solo_enabled_es_disconnected()
    {
        var f = AndroidDetailsReader.ParseWifi("Wifi is enabled\n");
        Assert.Equal(DetailField.Of("disconnected"), f[DetailKeys.WifiState]);
        Assert.Equal(DetailField.Missing("not_connected"), f[DetailKeys.WifiSsid]);
    }

    [Fact]
    public void Wifi_dumpsys_deshabilitado()
    {
        var f = AndroidDetailsReader.ParseWifi("__FX_DUMPSYS__\nWi-Fi is disabled\nstate=DISCONNECTED ssid=<unknown ssid>\n");
        Assert.Equal(DetailField.Of("disabled"), f[DetailKeys.WifiState]);
    }

    private static (AndroidDetailsReader Reader, FakeAdb Adb) RealReader()
    {
        var samples = DetailsFixtures.RealDateSamples();
        var adb = new FakeAdb();
        foreach (var s in samples) adb.Dates.Enqueue(new ProcessResult(0, s.Out, ""));
        var start = samples[0].B.AddMilliseconds(-50);
        var times = new List<DateTimeOffset> { start };
        foreach (var s in samples) { times.Add(s.B); times.Add(s.A); }
        times.Add(samples[^1].A.AddSeconds(1));
        return (new AndroidDetailsReader(adb.Run, new SequenceTimeProvider([.. times])), adb);
    }

    [Fact]
    public async Task Lector_con_fixtures_arma_la_respuesta_completa_y_en_orden()
    {
        var (reader, adb) = RealReader();
        var r = await reader.ReadAsync("R58RA3R5AGA", CancellationToken.None);

        Assert.Equal(DetailKeys.All, r.Fields.Keys.ToArray());
        AssertFields(DetailsFixtures.RealAndroidExpected, r.Fields);
        Assert.Equal(10038, r.Clock.DeviceMinusPcMs);
        Assert.Equal(50, r.Clock.MarginMs);
        Assert.Equal(3, r.Clock.Samples);

        // Orden de §4.2: get-state, 3 × date, PROPS, SETTINGS, WIFI. Cada script como UN argumento después de shell.
        var lasts = adb.Calls.Select(c => c.Args[^1]).ToArray();
        Assert.Equal(
            new[]
            {
                "get-state", "date +%s.%N", "date +%s.%N", "date +%s.%N", AndroidDetailsReader.PropsScript,
                AndroidDetailsReader.SettingsScript, AndroidDetailsReader.WifiScript,
            }, lasts);
        foreach (var c in adb.Calls.Skip(1))
        {
            Assert.Equal(4, c.Args.Count);
            Assert.Equal(new[] { "-s", "R58RA3R5AGA", "shell" }, c.Args.Take(3));
        }
        Assert.Equal(TimeSpan.FromSeconds(3), adb.Calls[0].Timeout);
        Assert.Equal(TimeSpan.FromSeconds(3), adb.Calls[1].Timeout);
        Assert.Equal(TimeSpan.FromSeconds(5), adb.Calls[4].Timeout);
    }

    [Fact]
    public async Task Timeout_en_SETTINGS_solo_afecta_a_auto_time()
    {
        var (reader, adb) = RealReader();
        adb.Settings = new ProcessResult(ProcessRunner.TimeoutExitCode, "", "");
        var r = await reader.ReadAsync("R58RA3R5AGA", CancellationToken.None);
        Assert.Equal(DetailField.Missing("timeout"), r.Fields[DetailKeys.AutoTime]);
        Assert.Equal(DetailField.Missing("timeout"), r.Fields[DetailKeys.AutoTimeZone]);
        Assert.Equal(DetailField.Of("R58RA3R5AGA"), r.Fields[DetailKeys.SerialNumber]);
        Assert.Equal(DetailField.Of("disconnected"), r.Fields[DetailKeys.WifiState]);
    }

    [Fact]
    public async Task Sin_adb_en_PROPS_da_error_en_las_16_claves()
    {
        var (reader, adb) = RealReader();
        adb.Props = new ProcessResult(-1, "", "No se encontró 'adb'");
        var r = await reader.ReadAsync("R58RA3R5AGA", CancellationToken.None);
        foreach (var k in AndroidDetailsReader.PropsFields)
            Assert.Equal(DetailField.Missing("error"), r.Fields[k]);
        Assert.Equal(16, AndroidDetailsReader.PropsFields.Length);
        Assert.Equal(DetailField.Of("1"), r.Fields[DetailKeys.AutoTime]);
    }

    [Fact]
    public async Task Exit_distinto_de_cero_con_stdout_se_parsea_igual()
    {
        var (reader, adb) = RealReader();
        adb.Wifi = new ProcessResult(1, "__FX_DUMPSYS__\nWi-Fi is enabled\n", "");
        var r = await reader.ReadAsync("R58RA3R5AGA", CancellationToken.None);
        Assert.Equal(DetailField.Missing("not_exposed"), r.Fields[DetailKeys.WifiState]);
    }

    [Theory]
    [InlineData("unauthorized")]
    [InlineData("")]
    [InlineData("offline")]
    public async Task Get_state_distinto_de_device_no_corre_mas_scripts(string state)
    {
        var (reader, adb) = RealReader();
        adb.GetState = state;
        await Assert.ThrowsAsync<DeviceNotFoundException>(() => reader.ReadAsync("X", CancellationToken.None));
        await Assert.ThrowsAsync<DeviceNotFoundException>(() => reader.MeasureClockAsync("X", CancellationToken.None));
        Assert.All(adb.Calls, c => Assert.Equal("get-state", c.Args[^1]));
    }

    [Fact]
    public async Task Endpoint_404_device_not_found()
    {
        var adb = new FakeAdb { GetState = "" };
        var svc = new DeviceDetailsService(Options.Create(new AgentOptions()), new ThrowingIos(), TimeProvider.System,
            NullLogger<DeviceDetailsService>.Instance, adb.Run);
        var ctrl = new Factum.Agent.Controllers.DeviceDetailsController(svc)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
            },
        };
        var result = await ctrl.Details("X", null, CancellationToken.None);
        var nf = Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundObjectResult>(result);
        var json = JsonSerializer.Serialize(nf.Value);
        Assert.Contains("\"code\":\"device_not_found\"", json);
    }
}

public sealed class DeviceClockMathTests
{
    [Fact]
    public void Muestra_real_da_10038_y_50()
    {
        var samples = DetailsFixtures.RealDateSamples()
            .Select(s => AndroidDetailsReader.ToSample(s.B, s.A, new ProcessResult(0, s.Out, "")))
            .ToList();
        var r = DeviceClockMath.Compute(samples);
        Assert.Null(r.Reason);
        Assert.Equal(10038, r.DeviceMinusPcMs);
        Assert.Equal(50, r.MarginMs);
        Assert.Equal(1, r.ResolutionMs);
        Assert.Equal(3, r.Samples);
        Assert.Equal(DateTimeOffset.Parse("2026-10-14T15:22:39.0791760Z", CultureInfo.InvariantCulture), r.PcBefore);
        Assert.Equal(DateTimeOffset.Parse("2026-10-14T15:22:39.1771400Z", CultureInfo.InvariantCulture), r.PcAfter);
        Assert.Equal(DateTimeOffset.Parse("2026-10-14T15:22:49.1662982Z", CultureInfo.InvariantCulture), r.DeviceTime);
    }

    [Fact]
    public void Conversion_a_ticks_sin_double_con_fraccion_de_9_digitos()
    {
        Assert.True(DeviceClockMath.ParseAndroidDate("1791991369.166298293\n", out var t, out var res));
        Assert.Equal(1, res);
        Assert.Equal(DateTime.UnixEpoch.Ticks + 1791991369L * TimeSpan.TicksPerSecond + 1662982, t);
        Assert.True(DeviceClockMath.ParseAndroidDate("1791991369.5", out var t2, out _));
        Assert.Equal(DateTime.UnixEpoch.Ticks + 1791991369L * TimeSpan.TicksPerSecond + 5000000, t2);
    }

    [Theory]
    [InlineData("seconds-only.txt", true, 1000)]
    [InlineData("literal-n.txt", true, 1000)]
    [InlineData("garbage.txt", false, 0)]
    [InlineData("zero.txt", false, 0)]
    public void Fixtures_sinteticos_de_date(string file, bool parses, int resolution)
    {
        var text = DetailsFixtures.Read("synthetic-date", file);
        Assert.Equal(parses, DeviceClockMath.ParseAndroidDate(text, out _, out var res));
        if (parses) Assert.Equal(resolution, res);
    }

    [Fact]
    public void Literal_N_sin_porcentaje_tambien_da_segundos()
    {
        Assert.True(DeviceClockMath.ParseAndroidDate("1791991369.N", out var t, out var res));
        Assert.Equal(1000, res);
        Assert.Equal(DateTime.UnixEpoch.Ticks + 1791991369L * TimeSpan.TicksPerSecond, t);
    }

    [Fact]
    public void Cero_esta_fuera_de_los_50_anios()
    {
        var b = DateTimeOffset.Parse("2026-10-14T15:00:00Z", CultureInfo.InvariantCulture);
        // "0" no matchea \d{9,11}; un valor de 9 dígitos de 1970 sí parsea pero queda fuera de las cotas.
        var s = AndroidDetailsReader.ToSample(b, b.AddMilliseconds(10), new ProcessResult(0, "100000000", ""));
        Assert.False(DeviceClockMath.IsValid(s));
        Assert.Equal("invalid", DeviceClockMath.Compute([s]).Reason);
    }

    [Fact]
    public void Empate_de_RTT_elige_la_primera()
    {
        var b = DateTimeOffset.Parse("2026-10-14T15:00:00Z", CultureInfo.InvariantCulture);
        var s1 = new ClockSample(b, b.AddMilliseconds(20), b.AddSeconds(5).UtcTicks, 1, null);
        var s2 = new ClockSample(b.AddSeconds(1), b.AddSeconds(1).AddMilliseconds(20), b.AddSeconds(9).UtcTicks, 1, null);
        var r = DeviceClockMath.Compute([s1, s2]);
        Assert.Equal(4990, r.DeviceMinusPcMs);
        Assert.Equal(2, r.Samples);
    }

    [Fact]
    public void Todas_invalidas_es_invalid_y_todas_timeout_es_timeout()
    {
        var b = DateTimeOffset.UtcNow;
        Assert.Equal("invalid", DeviceClockMath.Compute(
            [ClockSample.Failed(b, b, "invalid"), ClockSample.Failed(b, b, "invalid")]).Reason);
        var t = DeviceClockMath.Compute(
            [ClockSample.Failed(b, b, "timeout"), ClockSample.Failed(b, b, "timeout"), ClockSample.Failed(b, b, "timeout")]);
        Assert.Equal("timeout", t.Reason);
        Assert.Equal(0, t.Samples);
        Assert.Null(t.DeviceMinusPcMs);
        Assert.Equal("error", DeviceClockMath.Compute(
            [ClockSample.Failed(b, b, "error"), ClockSample.Failed(b, b, "timeout")]).Reason);
        Assert.Equal("error", DeviceClockMath.Compute([]).Reason);
    }

    [Fact]
    public void RTT_mayor_a_5_s_o_negativo_es_invalido()
    {
        var b = DateTimeOffset.Parse("2026-10-14T15:00:00Z", CultureInfo.InvariantCulture);
        Assert.False(DeviceClockMath.IsValid(new ClockSample(b, b.AddSeconds(5.001), b.UtcTicks, 1, null)));
        Assert.True(DeviceClockMath.IsValid(new ClockSample(b, b.AddSeconds(5), b.UtcTicks, 1, null)));
        Assert.False(DeviceClockMath.IsValid(new ClockSample(b, b.AddMilliseconds(-1), b.UtcTicks, 1, null)));
    }

    [Theory]
    [InlineData(-15000, -2)]   // -1,5 ms → -2 (half away from zero)
    [InlineData(15000, 2)]
    [InlineData(-14999, -1)]
    [InlineData(-5000, -1)]    // -0,5 ms → -1
    public void Redondeo_half_away_from_zero(long deltaTicks, long expectedMs)
    {
        const long b = 638_000_000_000_000_000;
        var (diff, _) = DeviceClockMath.Offsets(b, b, b + deltaTicks, 1);
        Assert.Equal(expectedMs, diff);
    }

    [Fact]
    public void Margen_es_ceil_de_RTT_medio_mas_resolucion()
    {
        const long b = 638_000_000_000_000_000;
        Assert.Equal(1000, DeviceClockMath.Offsets(b, b, b, 1000).MarginMs);
        Assert.Equal(6, DeviceClockMath.Offsets(b, b + 90_001, b, 1).MarginMs); // 4,50005 + 1 → 6
    }
}

public sealed class IosDetailsParsingTests
{
    [Fact]
    public void Fixture_sintetico_con_reloj()
    {
        var (f, clock) = IosDetailsParser.Parse(DetailsFixtures.Read("synthetic-ios", "details.json"));
        Assert.Equal(DetailKeys.All, f.Keys.ToArray());
        Assert.Equal(DetailField.Of("F2LZK0ABCDEF"), f[DetailKeys.SerialNumber]);
        Assert.Equal(DetailField.Of("17.4.1"), f[DetailKeys.OsVersion]);
        Assert.Equal(DetailField.Of("21E236"), f[DetailKeys.BuildId]);
        Assert.Equal(DetailField.Of("America/Argentina/Buenos_Aires"), f[DetailKeys.Timezone]);
        Assert.Equal(DetailField.Of("Personal_ar"), f[DetailKeys.SimOperator]);
        Assert.Equal(DetailField.Of("72234"), f[DetailKeys.SimOperatorNumeric]);
        Assert.Equal(DetailField.Of("Ready"), f[DetailKeys.SimState]);
        Assert.Equal(DetailField.Missing("not_exposed"), f[DetailKeys.Kernel]);
        Assert.Equal(DetailField.Missing("not_applicable"), f[DetailKeys.SecurityPatch]);
        Assert.Equal(DetailField.Missing("not_applicable"), f[DetailKeys.OsApiLevel]);
        Assert.Equal(DetailField.Missing("not_applicable"), f[DetailKeys.VerifiedBootState]);
        Assert.Equal(DetailField.Missing("not_exposed"), f[DetailKeys.WifiState]);
        Assert.Equal(DetailField.Missing("not_exposed"), f[DetailKeys.WifiSsid]);

        Assert.Equal(-2425, clock.DeviceMinusPcMs);
        Assert.Equal(16, clock.MarginMs);
        Assert.Equal(1, clock.ResolutionMs);
        Assert.Equal(3, clock.Samples);
    }

    [Fact]
    public void Sin_reloj_es_error()
    {
        var (_, clock) = IosDetailsParser.Parse(DetailsFixtures.Read("synthetic-ios", "details-no-clock.json"));
        Assert.Equal("error", clock.Reason);
        Assert.Equal(0, clock.Samples);
    }

    [Fact]
    public void Sin_carriers_ni_SIMStatus_da_empty()
    {
        var (f, clock) = IosDetailsParser.Parse(DetailsFixtures.Read("synthetic-ios", "details-no-sim.json"));
        Assert.Equal(DetailField.Missing("empty"), f[DetailKeys.SimOperator]);
        Assert.Equal(DetailField.Missing("empty"), f[DetailKeys.SimOperatorNumeric]);
        Assert.Equal(DetailField.Missing("empty"), f[DetailKeys.SimState]);
        Assert.Equal("error", clock.Reason);
    }

    [Fact]
    public void Muestras_invalidas_dan_invalid()
    {
        const string json = """{"values":{},"carriers":[],"clock":[{"pc_before_ns":1791991358900000000,"device":0,"pc_after_ns":1791991358960000000}],"clock_error":null}""";
        var (f, clock) = IosDetailsParser.Parse(json);
        Assert.Equal("invalid", clock.Reason);
        Assert.Equal(DetailField.Missing("empty"), f[DetailKeys.SerialNumber]);
    }

    [Fact]
    public void Hora_entera_tiene_resolucion_de_1000()
    {
        const string json = """{"values":{},"clock":[{"pc_before_ns":1791991358900000000,"device":1791991357,"pc_after_ns":1791991358960000000}]}""";
        var (_, clock) = IosDetailsParser.Parse(json);
        Assert.Equal(1000, clock.ResolutionMs);
        Assert.Equal(1030, clock.MarginMs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("[]")]
    public void Salida_no_parseable_da_error(string stdout)
    {
        var (f, clock) = IosDetailsParser.Parse(stdout);
        Assert.Equal(DetailField.Missing("error"), f[DetailKeys.SerialNumber]);
        Assert.Equal(DetailField.Missing("not_applicable"), f[DetailKeys.SecurityPatch]);
        Assert.Equal("error", clock.Reason);
    }
}

public sealed class DetailRulesTests
{
    // La misma tabla que DeviceDetailsRulesTests de la API (metadatos-dispositivo §4.5, D-T9).
    [Theory]
    [InlineData("serial_number", "R58RA3R5AGA", "R58RA3R5AGA", null)]
    [InlineData("serial_number", "unknown", null, "empty")]
    [InlineData("serial_number", "abc def", null, "invalid")]
    [InlineData("os_version", "8.1.0", "8.1.0", null)]
    [InlineData("os_version", "11 beta", null, "invalid")]
    [InlineData("os_api_level", "30", "30", null)]
    [InlineData("os_api_level", "3000", null, "invalid")]
    [InlineData("build_id", "RP1A.200720.012", "RP1A.200720.012", null)]
    [InlineData("build_id", "con espacio", null, "invalid")]
    [InlineData("security_patch", "2021-10-01", "2021-10-01", null)]
    [InlineData("security_patch", "2021-13-45", null, "invalid")]
    [InlineData("security_patch", "2021-10", null, "invalid")]
    [InlineData("kernel", "Linux localhost 4.19.111 #1 SMP aarch64", "Linux localhost 4.19.111 #1 SMP aarch64", null)]
    [InlineData("kernel", "Linux ñ", null, "invalid")]
    [InlineData("network_operator", "Personal,", "Personal,", null)]
    [InlineData("network_operator", "Movistar  AR", "Movistar AR", null)]
    [InlineData("network_operator_numeric", "722310", "722310", null)]
    [InlineData("network_operator_numeric", "722-310", null, "invalid")]
    [InlineData("sim_operator_numeric", "72234,72207", "72234,72207", null)]
    [InlineData("sim_state", "READY,ABSENT", "READY,ABSENT", null)]
    [InlineData("sim_state", "NOT_READY", "NOT_READY", null)]
    [InlineData("sim_state", "READY 1", null, "invalid")]
    [InlineData("verified_boot_state", "orange", "orange", null)]
    [InlineData("verified_boot_state", "ORANGE", null, "invalid")]
    [InlineData("bootloader_locked", "0", "0", null)]
    [InlineData("bootloader_locked", "2", null, "invalid")]
    [InlineData("vbmeta_device_state", "unlocked", "unlocked", null)]
    [InlineData("timezone", "America/Argentina/Buenos_Aires", "America/Argentina/Buenos_Aires", null)]
    [InlineData("timezone", "Etc/GMT+3", "Etc/GMT+3", null)]
    [InlineData("timezone", "America/Buenos Aires", null, "invalid")]
    [InlineData("auto_time", "1", "1", null)]
    [InlineData("auto_time", "null", null, "empty")]
    [InlineData("auto_time_zone", "null", null, "empty")]
    [InlineData("auto_time", "yes", null, "invalid")]
    [InlineData("wifi_state", "connected", "connected", null)]
    [InlineData("wifi_state", "CONNECTED", null, "invalid")]
    [InlineData("wifi_ssid", "Casa Ana", "Casa Ana", null)]
    [InlineData("wifi_ssid", "<unknown ssid>", null, "masked")]
    [InlineData("wifi_ssid", "Red\u202Eevil\u0007", "Redevil", null)]
    [InlineData("wifi_ssid", "  \t ", null, "empty")]
    [InlineData("wifi_ssid", "", null, "empty")]
    [InlineData("wifi_ssid", null, null, "empty")]
    [InlineData("wifi_ssid", "a\nb", "a b", null)]
    [InlineData("wifi_ssid", "{caratula}", "{caratula}", null)]
    [InlineData("clave_desconocida", "x", null, "error")]
    public void Tabla_de_validacion(string key, string? raw, string? value, string? reason) =>
        Assert.Equal(new DetailField(value, reason), DetailRules.Normalize(key, raw));

    [Fact]
    public void Texto_libre_hasta_64_elementos_de_texto()
    {
        Assert.Equal(DetailField.Of(new string('a', 64)), DetailRules.Normalize("wifi_ssid", new string('a', 64)));
        Assert.Equal(DetailField.Missing("invalid"), DetailRules.Normalize("wifi_ssid", new string('a', 65)));
        // 64 emojis (2 chars UTF-16 cada uno) siguen siendo 64 elementos de texto.
        var emojis = string.Concat(Enumerable.Repeat("😀", 64));
        Assert.Equal(DetailField.Of(emojis), DetailRules.Normalize("sim_operator", emojis));
    }

    [Fact]
    public void Clean_quita_bidi_C0_no_caracteres_y_surrogates_sueltos()
    {
        Assert.Equal("ab", DetailRules.Clean("a\u2066\u202Ab\u0000"));
        Assert.Equal("a b", DetailRules.Clean(" a \r\n b "));
        Assert.Equal("ab", DetailRules.Clean("a\uFDD0\uFFFEb"));
        Assert.Equal("ab", DetailRules.Clean("a\uD800b"));
        Assert.Equal("a\u007Fb", DetailRules.Clean("a\u007Fb")); // DEL se conserva (como SafeText)
        Assert.Equal("", DetailRules.Clean(null));
    }
}

public sealed class DeviceDetailsServiceTests
{
    private static DeviceDetailsService Mock(int skew) =>
        new(Options.Create(new AgentOptions { Mock = true, MockDeviceClockSkewSeconds = skew }), new ThrowingIos(),
            TimeProvider.System, NullLogger<DeviceDetailsService>.Instance,
            (_, _, _) => throw new InvalidOperationException("el mock no corre adb"));

    [Fact]
    public async Task Mock_con_90_s_da_90000_ms()
    {
        var r = await Mock(90).ReadAsync("SERIAL-X", "android", CancellationToken.None);
        Assert.Equal("SERIAL-X", r.Serial);
        Assert.Equal(90000, r.Clock.DeviceMinusPcMs);
        Assert.Equal(5, r.Clock.MarginMs);
        Assert.Equal(DetailKeys.All, r.Fields.Keys.ToArray());
        Assert.Equal(DetailField.Of("connected"), r.Fields[DetailKeys.WifiState]);
        Assert.Equal(DetailField.Of("Red de prueba"), r.Fields[DetailKeys.WifiSsid]);
        Assert.Equal(DetailField.Of("R58RA3R5AGA"), r.Fields[DetailKeys.SerialNumber]);

        // La API recalcula lo mismo desde las tres horas.
        var (diff, margin) = DeviceClockMath.Offsets(r.Clock.PcBefore!.Value.UtcTicks, r.Clock.PcAfter!.Value.UtcTicks,
            r.Clock.DeviceTime!.Value.UtcTicks, r.Clock.ResolutionMs!.Value);
        Assert.Equal((90000L, 5L), (diff, margin));

        var c = await Mock(-30).MeasureClockAsync("SERIAL-X", "ios", CancellationToken.None);
        Assert.Equal(-30000, c.Clock.DeviceMinusPcMs);
        Assert.Equal("ios", c.Platform);
    }

    [Fact]
    public async Task Mock_ios_usa_el_fixture_sintetico()
    {
        var r = await Mock(0).ReadAsync("UDID", "ios", CancellationToken.None);
        Assert.Equal(DetailField.Of("17.4.1"), r.Fields[DetailKeys.OsVersion]);
        Assert.Equal(DetailField.Missing("not_applicable"), r.Fields[DetailKeys.SecurityPatch]);
        Assert.Equal(0, r.Clock.DeviceMinusPcMs);
    }

    [Fact]
    public void Json_snake_case_sin_nulls()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        var resp = new DeviceClockResponse("S", "android", "1.7.0", DeviceClockReading.Failed("timeout"));
        Assert.Equal("""{"serial":"S","platform":"android","agent_version":"1.7.0","clock":{"samples":0,"reason":"timeout"}}""",
            JsonSerializer.Serialize(resp, opts));
        var field = JsonSerializer.Serialize(new Dictionary<string, DetailField>
        {
            ["wifi_ssid"] = DetailField.Missing("not_connected"),
            ["os_version"] = DetailField.Of("11"),
        }, opts);
        Assert.Equal("""{"wifi_ssid":{"reason":"not_connected"},"os_version":{"value":"11"}}""", field);
    }

    [Fact]
    public void Capability_DeviceDetailsV1() =>
        Assert.Contains("device_details_v1", Factum.Agent.Controllers.HealthController.Capabilities);
}

/// <summary>IIosService que no se tiene que usar en estos tests.</summary>
internal sealed class ThrowingIos : IIosService
{
    public bool IsAvailable => false;
    public bool AirplayAvailable => false;
    public string? AirplayUnavailableReason => null;
    public Task<List<Device>> ListDevicesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<string> TakeScreenshotAsync(string udid, string outputPath, string method = "auto",
        CancellationToken ct = default) => throw new NotSupportedException();
    public Task StartRecordingAsync(string udid, string mode, string outputPath, string? caseId = null,
        CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IosStopResult> StopRecordingAsync(string udid, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<string> StartAirplayShotSessionAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> MarkAirplayShotAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<List<(string Filename, string Url)>> StopAirplayShotSessionAsync(string serial,
        EventAttribution attribution, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<string> EnableDeveloperModeAsync(string udid, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<string> RunDetailsHelperAsync(string udid, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
