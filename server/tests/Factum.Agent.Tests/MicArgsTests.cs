using Factum.Agent.Services;
using Factum.Agent.Services.Session;

namespace Factum.Agent.Tests;

/// <summary>tatana-linux D-T14: el micrófono en Linux va por PulseAudio con fallback a ALSA.</summary>
public sealed class MicArgsTests
{
    [Fact]
    public void Linux_usa_pulse_default_o_el_MicDevice()
    {
        Assert.Equal(["-f", "pulse", "-i", "default"], MicCapture.LinuxInputArgs(null, alsa: false));
        Assert.Equal(["-f", "pulse", "-i", "default"], MicCapture.LinuxInputArgs("  ", alsa: false));
        Assert.Equal(["-f", "pulse", "-i", "alsa_input.usb-headset.mono"],
            MicCapture.LinuxInputArgs(" alsa_input.usb-headset.mono ", alsa: false));
    }

    [Fact]
    public void Fallback_alsa_ignora_el_MicDevice()
    {
        Assert.Equal(["-f", "alsa", "-i", "default"], MicCapture.LinuxInputArgs("alsa_input.x", alsa: true));
    }

    [Fact]
    public void Detecta_Unknown_input_format()
    {
        Assert.True(MicCapture.IsUnknownInputFormat(["algo", "Unknown input format: 'pulse'"]));
        Assert.False(MicCapture.IsUnknownInputFormat(["default: No such file or directory"]));
    }

    [Fact]
    public void Argumentos_completos_del_mic()
    {
        var args = MicCapture.BuildArgs(MicCapture.LinuxInputArgs(null, false), "/tmp/a.mka");
        Assert.Equal(["-hide_banner", "-nostats", "-y", "-f", "pulse", "-i", "default", "-c:a", "aac", "-b:a", "128k",
            "/tmp/a.mka"], args);
    }

    [Fact]
    public void Camara_v4l2_con_audio_por_pulse_y_fallback_alsa()
    {
        var (pulse, map) = CameraArgs.Input(CameraArgs.BackendLinux, "/dev/video0", withAudio: true, mic: "src1", mode: null);
        Assert.Equal(["-f", "v4l2", "-i", "/dev/video0", "-f", "pulse", "-i", "src1"], pulse);
        Assert.Equal("1:a", map);
        var (alsa, _) = CameraArgs.Input(CameraArgs.BackendLinux, "/dev/video0", withAudio: true, mic: "src1", mode: null,
            linuxAlsaAudio: true);
        Assert.Equal(["-f", "v4l2", "-i", "/dev/video0", "-f", "alsa", "-i", "default"], alsa);
        var (sinMic, _) = CameraArgs.Input(CameraArgs.BackendLinux, "/dev/video0", withAudio: true, mic: null, mode: null);
        Assert.Equal("default", sinMic[^1]);
    }

    [Fact]
    public void Camara_relanza_con_alsa_una_sola_vez()
    {
        string[] unknown = ["Unknown input format: 'pulse'"];
        Assert.True(CameraTrack.ShouldRetryWithAlsa(CameraArgs.BackendLinux, true, false, unknown));
        Assert.False(CameraTrack.ShouldRetryWithAlsa(CameraArgs.BackendLinux, true, true, unknown));
        Assert.False(CameraTrack.ShouldRetryWithAlsa(CameraArgs.BackendLinux, false, false, unknown));
        Assert.False(CameraTrack.ShouldRetryWithAlsa(CameraArgs.BackendMac, true, false, unknown));
        Assert.False(CameraTrack.ShouldRetryWithAlsa(CameraArgs.BackendLinux, true, false, ["Device busy"]));
    }
}

/// <summary>tatana-linux §5.4: texto de adb no encontrado por SO.</summary>
public sealed class AdbMissingMessageTests
{
    [Fact]
    public void Linux_no_menciona_Librerias()
    {
        Assert.Equal("No se encontró adb, el componente de Android de Tatana. Descargá Tatana de nuevo desde la página de Factum.",
            AdbService.AdbMissingMessage(Factum.Agent.Common.HostOs.Linux));
        Assert.Equal("ADB no instalado — instalalo desde la sección Librerías de Tatana",
            AdbService.AdbMissingMessage(Factum.Agent.Common.HostOs.Windows));
    }
}
