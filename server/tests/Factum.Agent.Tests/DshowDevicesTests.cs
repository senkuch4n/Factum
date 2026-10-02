using Factum.Agent.Services;

namespace Factum.Agent.Tests;

// T3: parser del listado de dispositivos dshow de ffmpeg (formato nuevo ≥ 4.4 y viejo).
public sealed class DshowDevicesTests
{
    private const string NuevoFormato = """
        [in#0 @ 000001d2c5e0a2c0] "Integrated Webcam" (video)
        [in#0 @ 000001d2c5e0a2c0]   Alternative name "@device_pnp_\\?\usb#vid_0c45&pid_6a10&mi_00#6&2b4d7c1&0&0000#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\global"
        [in#0 @ 000001d2c5e0a2c0] "Micrófono (Realtek(R) Audio)" (audio)
        [in#0 @ 000001d2c5e0a2c0]   Alternative name "@device_cm_{33D9A762-90C8-11D0-BD43-00A0C911CE86}\wave_{8F1E2A3B-1111-4C2D-9E8F-0123456789AB}"
        [in#0 @ 000001d2c5e0a2c0] "Varios micrófonos (USB Audio Device)" (audio)
        [in#0 @ 000001d2c5e0a2c0]   Alternative name "@device_cm_{33D9A762-90C8-11D0-BD43-00A0C911CE86}\wave_{AAAAAAAA-2222-4C2D-9E8F-0123456789AB}"
        [in#0 @ 000001d2c5e0a2c0] "OBS Virtual Camera" (none)
        [in#0 @ 000001d2c5e0a2c0]   Alternative name "@device_sw_{860BB310-5D01-11D0-BD3B-00A0C911CE86}\{A3FCE0F5-3493-419F-958A-ABA1250EC20B}"
        [in#0 @ 000001d2c5e0a2c0] Error opening input: Immediate exit requested
        Error opening input file dummy.
        """;

    private const string ViejoFormato = """
        [dshow @ 00000000004d8f40] DirectShow video devices (some may be both video and audio devices)
        [dshow @ 00000000004d8f40]  "Integrated Webcam"
        [dshow @ 00000000004d8f40]     Alternative name "@device_pnp_\\?\usb#vid_0c45&pid_6a10#{65e8773d}\global"
        [dshow @ 00000000004d8f40] DirectShow audio devices
        [dshow @ 00000000004d8f40]  "Micrófono (Realtek High Definition Audio)"
        [dshow @ 00000000004d8f40]     Alternative name "@device_cm_{33D9A762-90C8-11D0-BD43-00A0C911CE86}\wave_{11111111-2222}"
        [dshow @ 00000000004d8f40]  "Mezcla estéreo (Realtek High Definition Audio)"
        [dshow @ 00000000004d8f40]     Alternative name "@device_cm_{33D9A762-90C8-11D0-BD43-00A0C911CE86}\wave_{33333333-4444}"
        dummy: Immediate exit requested
        """;

    [Fact]
    public void Formato_nuevo_devuelve_los_mics_en_orden_con_alternative_name()
    {
        var mics = DshowDevices.ParseAudioDevices(NuevoFormato.Replace("\n", "\r\n"));

        Assert.Equal(2, mics.Count);
        Assert.Equal("Micrófono (Realtek(R) Audio)", mics[0].Name);
        Assert.Equal(@"@device_cm_{33D9A762-90C8-11D0-BD43-00A0C911CE86}\wave_{8F1E2A3B-1111-4C2D-9E8F-0123456789AB}", mics[0].AlternativeName);
        Assert.Equal("Varios micrófonos (USB Audio Device)", mics[1].Name);
        Assert.Equal(@"@device_cm_{33D9A762-90C8-11D0-BD43-00A0C911CE86}\wave_{AAAAAAAA-2222-4C2D-9E8F-0123456789AB}", mics[1].AlternativeName);
    }

    [Fact]
    public void Formato_nuevo_con_prefijo_dshow_tambien_anda()
    {
        var mics = DshowDevices.ParseAudioDevices(NuevoFormato.Replace("[in#0 @", "[dshow @"));
        Assert.Equal(2, mics.Count);
        Assert.Equal("Micrófono (Realtek(R) Audio)", mics[0].Name);
    }

    [Fact]
    public void Formato_viejo_devuelve_los_mics_de_la_seccion_de_audio()
    {
        var mics = DshowDevices.ParseAudioDevices(ViejoFormato);

        Assert.Equal(2, mics.Count);
        Assert.Equal("Micrófono (Realtek High Definition Audio)", mics[0].Name);
        Assert.Equal(@"@device_cm_{33D9A762-90C8-11D0-BD43-00A0C911CE86}\wave_{11111111-2222}", mics[0].AlternativeName);
        Assert.Equal("Mezcla estéreo (Realtek High Definition Audio)", mics[1].Name);
    }

    [Fact]
    public void Solo_video_da_lista_vacia()
    {
        const string soloVideo = """
            [in#0 @ 0000020a] "Integrated Webcam" (video)
            [in#0 @ 0000020a]   Alternative name "@device_pnp_\\?\usb#vid_0c45\global"
            [in#0 @ 0000020a] Error opening input: Immediate exit requested
            """;
        Assert.Empty(DshowDevices.ParseAudioDevices(soloVideo));
    }

    [Fact]
    public void Salida_vacia_da_lista_vacia()
    {
        Assert.Empty(DshowDevices.ParseAudioDevices(""));
    }
}
