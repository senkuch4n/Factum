using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;
using Factum.Backend.Services.Reports;
using Factum.Backend.Tests.Auth;
using Microsoft.Extensions.Options;
using static Factum.Backend.Tests.FichaTecnica.DeviceDetailsTestData;

namespace Factum.Backend.Tests.FichaTecnica;

// metadatos-dispositivo §13.3: validación, saneamiento y matemática del reloj de la API.
public sealed class DeviceDetailsRulesTests
{
    // La misma tabla que DetailRulesTests de Tatana (D-T9).
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
    [InlineData("wifi_ssid", "Red‮evil\u0007", "Redevil", null)]
    [InlineData("wifi_ssid", "  \t ", null, "empty")]
    [InlineData("wifi_ssid", "", null, "empty")]
    [InlineData("wifi_ssid", null, null, "empty")]
    [InlineData("wifi_ssid", "a\nb", "a b", null)]
    [InlineData("wifi_ssid", "{caratula}", "{caratula}", null)]
    [InlineData("clave_desconocida", "x", null, "error")]
    public void Tabla_de_validacion(string key, string? raw, string? value, string? reason)
    {
        var f = DeviceDetailsRules.Normalize(key, raw);
        Assert.Equal(value, f.Value);
        Assert.Equal(reason, f.Reason);
    }

    [Fact]
    public void Texto_libre_hasta_64_elementos_de_texto()
    {
        Assert.Equal(new string('a', 64), DeviceDetailsRules.Normalize("wifi_ssid", new string('a', 64)).Value);
        Assert.Equal("invalid", DeviceDetailsRules.Normalize("wifi_ssid", new string('a', 65)).Reason);
        var emojis = string.Concat(Enumerable.Repeat("😀", 64));
        Assert.Equal(emojis, DeviceDetailsRules.Normalize("sim_operator", emojis).Value);
    }

    [Fact]
    public void NormalizeFields_reason_desconocido_clave_desconocida_faltante_y_value_mas_reason()
    {
        var raw = AndroidFieldsDto();
        raw["kernel"] = new DeviceDetailFieldDto(Reason: "porque_si");
        raw["inventada"] = new DeviceDetailFieldDto("x");
        raw.Remove("timezone");
        raw["build_id"] = new DeviceDetailFieldDto("RP1A", "timeout");

        var f = DeviceDetailsRules.NormalizeFields(raw);
        Assert.Equal(Models.DeviceDetailKeys.All, f.Keys.ToArray());
        Assert.Equal("error", f["kernel"].Reason);
        Assert.False(f.ContainsKey("inventada"));
        Assert.Equal("error", f["timezone"].Reason);
        Assert.Null(f["build_id"].Value);
        Assert.Equal("timeout", f["build_id"].Reason);
        Assert.Equal("R58RA3R5AGA", f["serial_number"].Value);

        var none = DeviceDetailsRules.NormalizeFields(null);
        Assert.All(none.Values, v => Assert.Equal("error", v.Reason));
        Assert.Equal(20, none.Count);
    }

    [Fact]
    public void UnavailableCount_no_cuenta_not_applicable_ni_not_connected()
    {
        var f = DeviceDetailsRules.NormalizeFields(AndroidFieldsDto());
        // empty ×3 (network_operator, sim_operator, sim_operator_numeric); wifi_ssid = not_connected no cuenta.
        Assert.Equal(3, DeviceDetailsRules.UnavailableCount(f));
        Assert.Equal(0, DeviceDetailsRules.UnavailableCount(null));
    }

    [Fact]
    public void ComputeClock_con_las_ISO_del_fixture_real_da_10038_y_50()
    {
        var m = new ClockMeasurement();
        DeviceDetailsRules.ComputeClock(RealClock(), m);
        Assert.Null(m.Reason);
        Assert.Equal(10038, m.DeviceMinusPcMs);
        Assert.Equal(50, m.MarginMs);
        Assert.Equal(1, m.ResolutionMs);
        Assert.Equal(DateTimeKind.Utc, m.PcBefore!.Value.Kind);
        Assert.Equal(PcBefore.UtcDateTime, m.PcBefore);
    }

    [Fact]
    public void ComputeClock_ignora_lo_que_manda_el_cliente_y_valida()
    {
        var m = new ClockMeasurement();
        DeviceDetailsRules.ComputeClock(null, m);
        Assert.Equal("error", m.Reason);
        DeviceDetailsRules.ComputeClock(new DeviceClockDto(Reason: "timeout"), m);
        Assert.Equal("timeout", m.Reason);
        DeviceDetailsRules.ComputeClock(new DeviceClockDto(Reason: "otra"), m);
        Assert.Equal("error", m.Reason);
        DeviceDetailsRules.ComputeClock(new DeviceClockDto(PcBefore, PcAfter, DeviceTime, 7), m);
        Assert.Equal("invalid", m.Reason);
        Assert.Null(m.DeviceMinusPcMs);
        DeviceDetailsRules.ComputeClock(new DeviceClockDto(PcAfter, PcBefore, DeviceTime, 1), m); // a < b
        Assert.Equal("invalid", m.Reason);
        DeviceDetailsRules.ComputeClock(new DeviceClockDto(PcBefore, PcBefore.AddSeconds(6), DeviceTime, 1), m);
        Assert.Equal("invalid", m.Reason);
        DeviceDetailsRules.ComputeClock(new DeviceClockDto(PcBefore, PcAfter, DateTimeOffset.UnixEpoch, 1), m);
        Assert.Equal("invalid", m.Reason);
        // Resolución de 1 s (date sin %N): el margen sube a ~1 s.
        DeviceDetailsRules.ComputeClock(new DeviceClockDto(PcBefore, PcAfter, DeviceTime, 1000), m);
        Assert.Null(m.Reason);
        Assert.Equal(1049, m.MarginMs);
    }

    private static readonly DateTime Now = new(2026, 10, 14, 15, 22, 41, 500, DateTimeKind.Utc);

    [Fact]
    public void ComputePcServer_valido()
    {
        var s = new DateTimeOffset(Now.AddMilliseconds(-200));
        var m = new ClockMeasurement();
        DeviceDetailsRules.ComputePcServer(new PcServerProbeDto(s.AddMilliseconds(-30), s, s.AddMilliseconds(40)), Now, m);
        Assert.Null(m.PcServerReason);
        Assert.Equal(5, m.PcMinusServerMs);      // mid = s + 5 ms
        Assert.Equal(36, m.PcServerMarginMs);    // ceil(35) + 1
    }

    [Fact]
    public void ComputePcServer_rechazos()
    {
        var m = new ClockMeasurement();
        DeviceDetailsRules.ComputePcServer(null, Now, m);
        Assert.Equal("not_measured", m.PcServerReason);
        DeviceDetailsRules.ComputePcServer(new PcServerProbeDto(), Now, m);
        Assert.Equal("not_measured", m.PcServerReason);

        var future = new DateTimeOffset(Now.AddSeconds(2));
        DeviceDetailsRules.ComputePcServer(new PcServerProbeDto(future, future, future), Now, m);
        Assert.Equal("invalid", m.PcServerReason);
        Assert.Null(m.PcMinusServerMs);

        var old = new DateTimeOffset(Now.AddSeconds(-301));
        DeviceDetailsRules.ComputePcServer(new PcServerProbeDto(old, old, old), Now, m);
        Assert.Equal("invalid", m.PcServerReason);

        var s = new DateTimeOffset(Now.AddSeconds(-1));
        DeviceDetailsRules.ComputePcServer(new PcServerProbeDto(s.AddSeconds(-6), s, s.AddSeconds(5)), Now, m);
        Assert.Equal("invalid", m.PcServerReason); // RTT 11 s

        DeviceDetailsRules.ComputePcServer(new PcServerProbeDto(s, null, s), Now, m);
        Assert.Equal("invalid", m.PcServerReason); // incompleto
    }

    [Fact]
    public void AgentVersion_mas_de_32_queda_vacia()
    {
        Assert.Equal("1.7.0", DeviceDetailsRules.CleanAgentVersion(" 1.7.0 "));
        Assert.Equal("", DeviceDetailsRules.CleanAgentVersion(new string('1', 33)));
        Assert.Equal("", DeviceDetailsRules.CleanAgentVersion(null));
    }

    [Theory]
    [InlineData(60, 60, false)]
    [InlineData(0, 60, true)]
    [InlineData(86401, 60, true)]
    [InlineData(1, 1, false)]
    [InlineData(120, 120, false)]
    public void ReportSettings_umbral_de_reloj(int configured, int expected, bool warns)
    {
        var log = new CapturingLogger<ReportSettings>();
        var s = new ReportSettings(Options.Create(new ReportOptions { ClockSkewWarningSeconds = configured }), log);
        Assert.Equal(expected, s.ClockSkewWarningSeconds);
        Assert.Equal(warns, log.Messages.Any(m => m.Contains("ClockSkewWarningSeconds fuera de rango; se usa 60")));
    }

    [Fact]
    public void ReportOptions_default_60() => Assert.Equal(60, new ReportOptions().ClockSkewWarningSeconds);
}
