using System.Globalization;
using Factum.Backend.DTOs;
using Factum.Backend.Models;

namespace Factum.Backend.Tests.FichaTecnica;

/// <summary>
/// Datos de metadatos-dispositivo: la ficha REAL del Samsung SM-M127F (Android 11, fixture §13.1 de Tatana) y la
/// sintética de iOS. Sin datos de casos reales.
/// </summary>
internal static class DeviceDetailsTestData
{
    public const string Serial = "R58RA3R5AGA";

    // Horas de la muestra real elegida (§13.1): device_minus_pc_ms = 10038, margin_ms = 50.
    public static readonly DateTimeOffset PcBefore = Iso("2026-10-14T15:22:39.0791760+00:00");
    public static readonly DateTimeOffset PcAfter = Iso("2026-10-14T15:22:39.1771400+00:00");
    public static readonly DateTimeOffset DeviceTime = Iso("2026-10-14T15:22:49.1662982+00:00");

    public static DateTimeOffset Iso(string s) => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture);

    public static Dictionary<string, DeviceDetailFieldDto> AndroidFieldsDto(string? ssid = null) => new()
    {
        ["serial_number"] = new("R58RA3R5AGA"),
        ["os_version"] = new("11"),
        ["os_api_level"] = new("30"),
        ["build_id"] = new("RP1A.200720.012"),
        ["build_fingerprint"] = new("samsung/a12sub/a12s:11/RP1A.200720.012/A127MUBU3AUJ5:user/release-keys"),
        ["security_patch"] = new("2021-10-01"),
        ["kernel"] = new("Linux localhost 4.19.111-22565658 #1 SMP PREEMPT Thu Nov 11 01:08:09 KST 2021 aarch64"),
        ["network_operator"] = new(Reason: "empty"),
        ["network_operator_numeric"] = new("722310"),
        ["sim_operator"] = new(Reason: "empty"),
        ["sim_operator_numeric"] = new(Reason: "empty"),
        ["sim_state"] = new("ABSENT"),
        ["verified_boot_state"] = new("orange"),
        ["bootloader_locked"] = new("0"),
        ["vbmeta_device_state"] = new("unlocked"),
        ["timezone"] = new("America/Argentina/Buenos_Aires"),
        ["auto_time"] = new("1"),
        ["auto_time_zone"] = new("1"),
        ["wifi_state"] = ssid is null ? new("disconnected") : new("connected"),
        ["wifi_ssid"] = ssid is null ? new(Reason: "not_connected") : new(ssid),
    };

    public static DeviceClockDto RealClock() => new(PcBefore, PcAfter, DeviceTime, 1);

    public static Dictionary<string, DeviceDetailField> ToFields(Dictionary<string, DeviceDetailFieldDto> dto) =>
        dto.ToDictionary(kv => kv.Key, kv => new DeviceDetailField { Value = kv.Value.Value, Reason = kv.Value.Reason });

    /// <summary>Ficha Android real ya guardada, con el reloj de inicio real (10038/50).</summary>
    public static Models.DeviceDetails Android(string? ssid = null, bool includeSsid = false) => new()
    {
        ReadAt = new DateTime(2026, 10, 14, 15, 22, 41, DateTimeKind.Utc),
        Platform = "android",
        ConnectionId = Serial,
        AgentVersion = "1.7.0",
        Fields = ToFields(AndroidFieldsDto(ssid)),
        ClockStart = new ClockMeasurement
        {
            Kind = ClockKinds.Start,
            RecordedAt = new DateTime(2026, 10, 14, 15, 22, 41, DateTimeKind.Utc),
            PcBefore = PcBefore.UtcDateTime, PcAfter = PcAfter.UtcDateTime, DeviceTime = DeviceTime.UtcDateTime,
            ResolutionMs = 1, DeviceMinusPcMs = 10038, MarginMs = 50,
            PcMinusServerMs = -120, PcServerMarginMs = 71,
        },
        IncludeSsidInReport = includeSsid,
    };

    /// <summary>Ficha iOS sintética (fixture §13.1): atrasa 2425 ms (± 16 ms).</summary>
    public static Models.DeviceDetails Ios() => new()
    {
        ReadAt = new DateTime(2026, 10, 14, 15, 22, 41, DateTimeKind.Utc),
        Platform = "ios",
        ConnectionId = "00008110-001A2B3C4D5E801E",
        AgentVersion = "1.7.0",
        Fields = new Dictionary<string, DeviceDetailField>
        {
            ["serial_number"] = new() { Value = "F2LZK0ABCDEF" },
            ["os_version"] = new() { Value = "17.4.1" },
            ["os_api_level"] = new() { Reason = "not_applicable" },
            ["build_id"] = new() { Value = "21E236" },
            ["build_fingerprint"] = new() { Reason = "not_applicable" },
            ["security_patch"] = new() { Reason = "not_applicable" },
            ["kernel"] = new() { Reason = "not_exposed" },
            ["network_operator"] = new() { Reason = "not_exposed" },
            ["network_operator_numeric"] = new() { Reason = "not_exposed" },
            ["sim_operator"] = new() { Value = "Personal_ar" },
            ["sim_operator_numeric"] = new() { Value = "72234" },
            ["sim_state"] = new() { Value = "Ready" },
            ["verified_boot_state"] = new() { Reason = "not_applicable" },
            ["bootloader_locked"] = new() { Reason = "not_applicable" },
            ["vbmeta_device_state"] = new() { Reason = "not_applicable" },
            ["timezone"] = new() { Value = "America/Argentina/Buenos_Aires" },
            ["auto_time"] = new() { Reason = "not_exposed" },
            ["auto_time_zone"] = new() { Reason = "not_exposed" },
            ["wifi_state"] = new() { Reason = "not_exposed" },
            ["wifi_ssid"] = new() { Reason = "not_exposed" },
        },
        ClockStart = new ClockMeasurement
        {
            Kind = ClockKinds.Start,
            RecordedAt = new DateTime(2026, 10, 14, 15, 22, 41, DateTimeKind.Utc),
            ResolutionMs = 1, DeviceMinusPcMs = -2425, MarginMs = 16,
            PcServerReason = "not_measured",
        },
    };
}
