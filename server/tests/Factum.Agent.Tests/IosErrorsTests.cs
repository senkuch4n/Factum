using Factum.Agent.Common;
using Factum.Agent.Services.Ios;

namespace Factum.Agent.Tests;

// T1 de la SDD ios-herramientas-windows: textos exactos de §4.1, definitivos y parseo de TATANA_ERROR.
public sealed class IosErrorsTests
{
    public static TheoryData<string, string, string> Textos => new()
    {
        { IosErrors.AppleServiceMissing,
          "No se encontró el servicio de dispositivos de Apple en esta PC. Instalá la app \"Apple Devices\" desde Microsoft Store (o iTunes) y volvé a conectar el iPhone.",
          "No se pudo hablar con el servicio de dispositivos de macOS (usbmuxd). Desconectá y volvé a conectar el iPhone." },
        { IosErrors.DeviceNotFound,
          "El iPhone no está conectado o no responde. Revisá el cable USB y que el iPhone esté desbloqueado.",
          "El iPhone no está conectado o no responde. Revisá el cable USB y que el iPhone esté desbloqueado." },
        { IosErrors.NotTrusted,
          "El iPhone no confía en esta PC. Desbloquealo, tocá \"Confiar\" e ingresá el código.",
          "El iPhone no confía en esta PC. Desbloquealo, tocá \"Confiar\" e ingresá el código." },
        { IosErrors.Locked,
          "El iPhone está bloqueado. Desbloquealo y reintentá.",
          "El iPhone está bloqueado. Desbloquealo y reintentá." },
        { IosErrors.DeveloperModeDisabled,
          "El Modo Desarrollador del iPhone está apagado. Activalo desde la guía de conexión del iPhone (botón \"Activar Modo Desarrollador\") y reintentá.",
          "El Modo Desarrollador del iPhone está apagado. Activalo desde la guía de conexión del iPhone (botón \"Activar Modo Desarrollador\") y reintentá." },
        { IosErrors.DdiMountFailed,
          "No se pudo preparar el iPhone para las capturas (imagen de desarrollador). Revisá que esta PC tenga internet y que el iPhone esté desbloqueado, y reintentá.",
          "No se pudo preparar el iPhone para las capturas (imagen de desarrollador). Revisá que esta PC tenga internet y que el iPhone esté desbloqueado, y reintentá." },
        { IosErrors.TunnelFailed,
          "No se pudo abrir la conexión de servicios con el iPhone. Desconectá y volvé a conectar el cable con el iPhone desbloqueado.",
          "No se pudo abrir la conexión de servicios con el iPhone. Desconectá y volvé a conectar el cable con el iPhone desbloqueado." },
        { IosErrors.AdminRequired,
          "Windows no dejó a Tatana abrir la conexión con el iPhone. Cerrá Tatana y abrilo con clic derecho → \"Ejecutar como administrador\".",
          "macOS no dejó a Tatana abrir la conexión con el iPhone. Revisá los permisos y reintentá." },
        { IosErrors.ToolsMissing,
          "Tatana no encuentra sus herramientas de iPhone (Python o ffmpeg). Descargá e instalá de nuevo Tatana.",
          "Tatana no encuentra Python con pymobiledevice3 o ffmpeg en esta Mac." },
        { IosErrors.AirplayUnavailable,
          "AirPlay no está disponible en Tatana para Windows. Usá \"Solo pantalla\", \"Pantalla + micrófono de PC\" o \"Grabación nativa del iPhone\".",
          "uxplay no encontrado. Instalá: brew install uxplay" },
    };

    [Theory]
    [MemberData(nameof(Textos))]
    public void Texto_exacto_por_SO(string code, string windows, string mac)
    {
        Assert.Equal(windows, IosErrors.MessageFor(code, isWindows: true));
        Assert.Equal(mac, IosErrors.MessageFor(code, isWindows: false));
    }

    [Fact]
    public void Capture_failed_con_detalle_recortado_a_200_y_en_una_linea()
    {
        Assert.Equal("No se pudo capturar la pantalla del iPhone: DTX timeout",
            IosErrors.MessageFor(IosErrors.CaptureFailed, true, "DTX timeout"));
        var largo = new string('x', 500);
        var msg = IosErrors.MessageFor(IosErrors.CaptureFailed, true, "linea1\r\nlinea2 " + largo);
        Assert.StartsWith("No se pudo capturar la pantalla del iPhone: linea1 linea2 x", msg);
        Assert.Equal("No se pudo capturar la pantalla del iPhone: ".Length + 200, msg.Length);
        Assert.DoesNotContain('\n', msg);
    }

    [Fact]
    public void Recording_empty_con_motivo_del_error_que_lo_causo()
    {
        var cause = IosErrors.Create(IosErrors.Locked, isWindows: true);
        var e = IosErrors.RecordingEmptyFrom(cause, IosErrors.ReasonNoFrames, isWindows: true);
        Assert.Equal(IosErrors.RecordingEmpty, e.Code);
        Assert.Equal("No se pudo guardar la grabación del iPhone: el iPhone está bloqueado. Desbloquealo y reintentá.", e.Message);

        var sinCodigo = IosErrors.RecordingEmptyFrom(null, IosErrors.ReasonNoFrames, isWindows: true);
        Assert.Equal("No se pudo guardar la grabación del iPhone: no se capturó ningún cuadro de la pantalla.", sinCodigo.Message);
        var timeout = IosErrors.RecordingEmptyFrom(null, IosErrors.ReasonTimeout, isWindows: false);
        Assert.Equal("No se pudo guardar la grabación del iPhone: la grabación no terminó de escribirse a tiempo.", timeout.Message);

        // Nombres propios no se pasan a minúscula.
        var admin = IosErrors.RecordingEmptyFrom(IosErrors.Create(IosErrors.AdminRequired, true), "x", true);
        Assert.StartsWith("No se pudo guardar la grabación del iPhone: Windows no dejó", admin.Message);
    }

    [Fact]
    public void Ningun_texto_de_Windows_menciona_brew_Terminal_ni_rutas_de_Unix()
    {
        foreach (var code in IosErrors.AllCodes)
        {
            var text = IosErrors.MessageFor(code, isWindows: true, detail: null);
            Assert.DoesNotContain("brew", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Terminal", text, StringComparison.Ordinal);
            Assert.DoesNotContain("/usr/", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Codigos_iguales_a_AgentErrorCodes_y_son_12()
    {
        Assert.Equal(12, IosErrors.AllCodes.Count);
        Assert.Equal(12, IosErrors.AllCodes.Distinct().Count());
        Assert.Contains(AgentErrorCodes.IosRecordingEmpty, IosErrors.AllCodes);
        Assert.Contains("airplay_unavailable", IosErrors.AllCodes);
        Assert.All(IosErrors.AllCodes, c => Assert.Matches("^[a-z0-9_]+$", c));
    }

    [Theory]
    [InlineData("ios_apple_service_missing", true, true)]
    [InlineData("ios_device_not_found", true, true)]
    [InlineData("ios_not_trusted", true, true)]
    [InlineData("ios_locked", true, true)]
    [InlineData("ios_admin_required", true, true)]
    [InlineData("ios_tools_missing", true, true)]
    [InlineData("ios_developer_mode_disabled", false, true)]
    [InlineData("ios_ddi_mount_failed", false, true)]
    [InlineData("ios_tunnel_failed", false, false)]
    [InlineData("ios_capture_failed", false, false)]
    public void Definitivos(string code, bool captura, bool grabacion)
    {
        Assert.Equal(captura, IosErrors.IsDefinitive(code));
        Assert.Equal(grabacion, IosErrors.IsDefinitiveForRecording(code));
    }

    [Fact]
    public void Parsea_TATANA_ERROR_valido()
    {
        Assert.True(IosErrors.TryParseHelperError(
            "TATANA_ERROR {\"code\": \"ios_locked\", \"detail\": \"PasscodeRequiredError\"}", out var code, out var detail));
        Assert.Equal("ios_locked", code);
        Assert.Equal("PasscodeRequiredError", detail);
    }

    [Fact]
    public void Parsea_detalle_con_escape_unicode()
    {
        Assert.True(IosErrors.TryParseHelperError(
            "TATANA_ERROR {\"code\": \"ios_capture_failed\", \"detail\": \"iPhone de Jos\\u00e9\"}", out var code, out var detail));
        Assert.Equal("ios_capture_failed", code);
        Assert.Equal("iPhone de José", detail);
        Assert.Equal("No se pudo capturar la pantalla del iPhone: iPhone de José",
            IosErrors.FromHelper(code, detail, isWindows: true).Message);
    }

    [Theory]
    [InlineData("TATANA_ERROR {\"code\": \"ios_lock")]
    [InlineData("TATANA_ERROR no-es-json")]
    [InlineData("TATANA_ERROR {\"detail\": \"sin code\"}")]
    [InlineData("Traceback (most recent call last):")]
    [InlineData("")]
    [InlineData(null)]
    public void JSON_roto_o_sin_prefijo_no_parsea(string? line)
    {
        Assert.False(IosErrors.TryParseHelperError(line, out _, out _));
    }

    [Fact]
    public void Codigo_desconocido_es_capture_failed()
    {
        Assert.True(IosErrors.TryParseHelperError(
            "TATANA_ERROR {\"code\": \"algo_nuevo\", \"detail\": \"x\"}", out var code, out var detail));
        Assert.Equal(IosErrors.CaptureFailed, code);
        Assert.Equal("x", detail);
        Assert.Equal(IosErrors.CaptureFailed, IosErrors.FromHelper("algo_nuevo", "x", true).Code);
    }
}
