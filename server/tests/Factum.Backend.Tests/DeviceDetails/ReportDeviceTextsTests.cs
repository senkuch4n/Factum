using Factum.Backend.Models;
using Factum.Backend.Services.Reports;
using static Factum.Backend.Tests.FichaTecnica.DeviceDetailsTestData;

namespace Factum.Backend.Tests.FichaTecnica;

// metadatos-dispositivo §5.3, §9.3 y §9.4: textos del informe, puros.
public sealed class ReportDeviceTextsTests
{
    private static readonly TimeZoneInfo Ar = TimeZoneInfo.CreateCustomTimeZone("AR", TimeSpan.FromHours(-3), "AR", "AR");

    private sealed class Settings(int threshold = 60) : IReportSettings
    {
        public TimeZoneInfo Zone => Ar;
        public string ZoneLabel => "AR";
        public string DomicilioConstituido => "";
        public bool EncryptZip => false;
        public int ClockSkewWarningSeconds => threshold;
        public string DefaultOperacionesRealizadas => "";
        public string DefaultAseguramientoEvidencia => "";
        public string DefaultNotasTecnicas => "";
        public string DefaultReserva => "";
    }

    [Theory]
    [InlineData(0, "0 s")]
    [InlineData(13, "13 s")]
    [InlineData(60, "1 min")]
    [InlineData(133, "2 min 13 s")]
    [InlineData(3600, "1 h")]
    [InlineData(3725, "1 h 2 min 5 s")]
    [InlineData(90061, "1 d 1 h 1 min")]
    [InlineData(90, "1 min 30 s")]
    public void FormatDuration_tabla_de_la_SDD(long seconds, string expected) =>
        Assert.Equal(expected, ReportDeviceTexts.FormatDuration(seconds));

    [Fact]
    public void Filas_Android_real()
    {
        var d = Android();
        var p = ReportDeviceTexts.Placeholders(d, Ar);
        Assert.Equal("14/10/2026", p["{fechaFichaTecnica}"]);
        Assert.Equal("12:22", p["{horaFichaTecnica}"]);
        Assert.Equal("Android 11 (nivel de API 30)", p["{fichaSistemaOperativo}"]);
        Assert.Equal("RP1A.200720.012; huella: samsung/a12sub/a12s:11/RP1A.200720.012/A127MUBU3AUJ5:user/release-keys",
            p["{fichaCompilacion}"]);
        Assert.Equal("01/10/2021", p["{fichaParcheSeguridad}"]);
        Assert.Equal("R58RA3R5AGA", p["{fichaNumeroSerie}"]);
        Assert.Equal("R58RA3R5AGA (identificador de conexión ADB; no es necesariamente el número de serie)",
            p["{fichaIdentificadorConexion}"]);
        Assert.Equal("Linux localhost 4.19.111-22565658 #1 SMP PREEMPT Thu Nov 11 01:08:09 KST 2021 aarch64",
            p["{fichaKernel}"]);
        Assert.Equal("Red: No disponible (722310); SIM: No disponible, estado ABSENT", p["{fichaOperadorSim}"]);
        Assert.Equal("verifiedbootstate: orange; flash.locked: 0; vbmeta.device_state: unlocked",
            p["{fichaEstadoArranque}"]);
        Assert.Equal("America/Argentina/Buenos_Aires; hora automática: activada; zona horaria automática: activada",
            p["{fichaZonaHoraria}"]);
        Assert.Equal("Activado, sin conexión", p["{fichaWifi}"]);
        Assert.Equal(12, p.Count);
    }

    [Fact]
    public void Filas_iOS_sintetico()
    {
        var p = ReportDeviceTexts.Placeholders(Ios(), Ar);
        Assert.Equal("iOS 17.4.1", p["{fichaSistemaOperativo}"]);
        Assert.Equal("21E236", p["{fichaCompilacion}"]);
        Assert.Equal(ReportDeviceTexts.ParcheIos, p["{fichaParcheSeguridad}"]);
        Assert.Equal("F2LZK0ABCDEF", p["{fichaNumeroSerie}"]);
        Assert.Equal("00008110-001A2B3C4D5E801E (UDID)", p["{fichaIdentificadorConexion}"]);
        Assert.Equal("No disponible (el sistema operativo no lo informa por una consulta de solo lectura)",
            p["{fichaKernel}"]);
        Assert.Equal("Red: No disponible; SIM: Personal_ar (72234), estado Ready", p["{fichaOperadorSim}"]);
        Assert.Equal("No aplica (iOS no lo informa)", p["{fichaEstadoArranque}"]);
        Assert.Equal("America/Argentina/Buenos_Aires", p["{fichaZonaHoraria}"]);
        Assert.Equal("No disponible (el sistema operativo no lo informa por una consulta de solo lectura)",
            p["{fichaWifi}"]);
    }

    [Fact]
    public void Sin_ficha_los_placeholders_valen_vacio()
    {
        var p = ReportDeviceTexts.Placeholders(null, Ar);
        Assert.Equal(12, p.Count);
        Assert.All(p.Values, v => Assert.Equal("", v));
    }

    [Fact]
    public void Faltantes_y_variantes_de_Android()
    {
        var d = Android();
        d.Fields["os_api_level"] = new DeviceDetailField { Reason = "timeout" };
        d.Fields["build_fingerprint"] = new DeviceDetailField { Reason = "invalid" };
        d.Fields["serial_number"] = new DeviceDetailField { Reason = "empty" };
        d.Fields["auto_time"] = new DeviceDetailField { Value = "0" };
        d.Fields["auto_time_zone"] = new DeviceDetailField { Reason = "empty" };
        d.Fields["vbmeta_device_state"] = new DeviceDetailField { Reason = "empty" };
        Assert.Equal("Android 11", ReportDeviceTexts.SistemaOperativo(d));
        Assert.Equal("RP1A.200720.012", ReportDeviceTexts.Compilacion(d));
        Assert.Equal("No disponible (el dispositivo no devolvió el dato (puede estar restringido por el sistema operativo))",
            ReportDeviceTexts.NumeroSerie(d));
        Assert.Equal("America/Argentina/Buenos_Aires; hora automática: desactivada; zona horaria automática: No disponible",
            ReportDeviceTexts.ZonaHoraria(d));
        Assert.Equal("verifiedbootstate: orange; flash.locked: 0; vbmeta.device_state: No disponible",
            ReportDeviceTexts.EstadoArranque(d));

        d.Fields["os_version"] = new DeviceDetailField { Reason = "error" };
        d.Fields["build_id"] = new DeviceDetailField { Reason = "timeout" };
        Assert.Equal("No disponible (falló la lectura)", ReportDeviceTexts.SistemaOperativo(d));
        Assert.Equal("No disponible (el dispositivo no respondió a tiempo)", ReportDeviceTexts.Compilacion(d));
    }

    [Theory]
    [InlineData("disabled", null, null, false, "Desactivado")]
    [InlineData("disconnected", null, "not_connected", false, "Activado, sin conexión")]
    [InlineData("connected", "Casa Ana", null, true, "Conectado a la red «Casa Ana»")]
    [InlineData("connected", "Casa Ana", null, false, "Conectado (el nombre de la red no se consigna)")]
    [InlineData("connected", null, "masked", true, "Conectado (el sistema no informó el nombre de la red)")]
    [InlineData(null, null, "not_exposed", true,
        "No disponible (el sistema operativo no lo informa por una consulta de solo lectura)")]
    public void Wifi_seis_variantes(string? state, string? ssid, string? ssidReason, bool flag, string expected)
    {
        var d = Android(includeSsid: flag);
        d.Fields["wifi_state"] = state is null
            ? new DeviceDetailField { Reason = "not_exposed" }
            : new DeviceDetailField { Value = state };
        d.Fields["wifi_ssid"] = new DeviceDetailField { Value = ssid, Reason = ssidReason };
        Assert.Equal(expected, ReportDeviceTexts.Wifi(d));
    }

    private static string[] Lines(Models.DeviceDetails d, int threshold = 60) =>
        ReportDeviceTexts.ClockBlock(d, new Settings(threshold)).Split('\n');

    [Fact]
    public void ClockBlock_adelanta_real()
    {
        var lines = Lines(Android());
        Assert.Equal(
        [
            "Reloj del dispositivo al inicio (medición registrada el 14/10/2026 a las 12:22:41): el dispositivo " +
            "adelantaba 10 s respecto de la estación de trabajo (± 1 s).",
            "Zona horaria configurada en el dispositivo: America/Argentina/Buenos_Aires.",
        ], lines);
    }

    [Fact]
    public void ClockBlock_atrasa_coincide_y_sin_hora()
    {
        Assert.StartsWith("Reloj del dispositivo al inicio (medición registrada el 14/10/2026 a las 12:22:41): el " +
                          "dispositivo atrasaba 2 s respecto de la estación de trabajo (± 1 s).", Lines(Ios())[0]);

        var d = Android();
        d.ClockStart!.DeviceMinusPcMs = 499;
        Assert.EndsWith("el dispositivo coincidía con la estación de trabajo (± 1 s).", Lines(d)[0]);

        d.ClockStart = new ClockMeasurement { Kind = "start", Reason = "timeout", RecordedAt = d.ReadAt };
        Assert.Equal("Reloj del dispositivo al inicio: No disponible (el dispositivo no respondió a tiempo).", Lines(d)[0]);

        d.ClockStart = null;
        Assert.Equal("Zona horaria configurada en el dispositivo: America/Argentina/Buenos_Aires.", Lines(d)[0]);
    }

    [Fact]
    public void ClockBlock_con_cierre()
    {
        var d = Android();
        d.ClockClose = new ClockMeasurement
        {
            Kind = "close", RecordedAt = new DateTime(2026, 10, 14, 16, 0, 5, DateTimeKind.Utc),
            DeviceMinusPcMs = 10100, MarginMs = 40, PcServerReason = "not_measured",
        };
        var lines = Lines(d);
        Assert.Equal("Reloj del dispositivo al cierre (medición registrada el 14/10/2026 a las 13:00:05): el " +
                     "dispositivo adelantaba 10 s respecto de la estación de trabajo (± 1 s).", lines[1]);
        Assert.Equal(3, lines.Length);
    }

    [Theory]
    [InlineData(61000, 60, true)]
    [InlineData(60000, 60, false)]
    [InlineData(-61000, 60, true)]
    [InlineData(90000, 120, false)]
    public void ClockBlock_frase_de_umbral(long ms, int threshold, bool expected)
    {
        var d = Android();
        d.ClockStart!.DeviceMinusPcMs = ms;
        var text = string.Join("\n", Lines(d, threshold));
        Assert.Equal(expected, text.Contains("Se deja constancia de que el reloj del dispositivo presentaba una diferencia de"));
    }

    [Fact]
    public void ClockBlock_frase_de_umbral_usa_la_mayor_y_va_una_sola_vez()
    {
        var d = Android();
        d.ClockStart!.DeviceMinusPcMs = 90000;
        d.ClockClose = new ClockMeasurement { Kind = "close", RecordedAt = d.ReadAt, DeviceMinusPcMs = -133000, MarginMs = 30 };
        var text = string.Join("\n", Lines(d));
        Assert.Contains("Se deja constancia de que el reloj del dispositivo presentaba una diferencia de 2 min 13 s " +
                        "respecto de la estación de trabajo; las horas visibles en las capturas corresponden al reloj " +
                        "del dispositivo.", text);
        Assert.Single(text.Split('\n'), l => l.StartsWith("Se deja constancia"));
        Assert.Contains("el dispositivo adelantaba 1 min 30 s respecto", text);
        Assert.Contains("el dispositivo atrasaba 2 min 13 s respecto", text);
    }

    [Fact]
    public void ClockBlock_linea_PC_servidor_solo_por_encima_del_umbral()
    {
        var d = Android();
        Assert.DoesNotContain("servidor de Factum", string.Join("\n", Lines(d))); // -120 ms
        d.ClockStart!.PcMinusServerMs = 75400;
        d.ClockStart.PcServerMarginMs = 71;
        var text = string.Join("\n", Lines(d));
        Assert.Contains("A su vez, la estación de trabajo adelantaba 1 min 15 s respecto del servidor de Factum (± 1 s).",
            text);
        d.ClockStart.PcMinusServerMs = -60000;
        Assert.DoesNotContain("servidor de Factum", string.Join("\n", Lines(d)));
    }

    [Fact]
    public void Tokens_de_textos_por_defecto()
    {
        var t = ReportDeviceTexts.DefaultTextTokens(Android());
        Assert.Equal("el dispositivo adelantaba 10 s respecto de la estación de trabajo (± 1 s)", t["diferenciaReloj"]);
        Assert.Equal("R58RA3R5AGA", t["numeroSerieDispositivo"]);
        Assert.Equal("RP1A.200720.012", t["compilacionDispositivo"]);
        Assert.Equal("America/Argentina/Buenos_Aires", t["zonaHorariaDispositivo"]);

        var none = ReportDeviceTexts.DefaultTextTokens(null);
        Assert.All(none.Values, v => Assert.Equal("No disponible", v));
    }

    [Fact]
    public void SistemaOperativo_token_usa_la_version_textual()
    {
        var cas = ReportTestSupport.MakeCase();
        cas.Device.OsVersion = "";
        cas.Device.AndroidVersion = 0;
        var settings = new Settings();
        Assert.Equal("Android 0", ReportValues.DefaultTextTokens(cas, settings, [])["sistemaOperativo"]);

        cas.DeviceDetails = Android();
        cas.DeviceDetails.Fields["os_version"] = new DeviceDetailField { Value = "8.1.0" };
        var tokens = ReportValues.DefaultTextTokens(cas, settings, []);
        Assert.Equal("Android 8.1.0", tokens["sistemaOperativo"]);
        Assert.Equal("R58RA3R5AGA", tokens["numeroSerieDispositivo"]);
        Assert.StartsWith("el dispositivo adelantaba", tokens["diferenciaReloj"]);
    }
}
