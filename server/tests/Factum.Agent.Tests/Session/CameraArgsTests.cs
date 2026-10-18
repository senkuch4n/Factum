using Factum.Agent.Controllers;
using Factum.Agent.Models;
using Factum.Agent.Services.Session;

namespace Factum.Agent.Tests.Session;

/// <summary>Comando de ffmpeg de la cámara y parseo del stderr (grabacion-sesion-completa-f2 §5.3–§5.5). Puro.</summary>
public sealed class CameraArgsTests
{
    private const string Staging = "/datos/sessions/c1";

    private static CameraArgsInput Input(string backend, string video = "Cámara de la MacBook Air", bool audio = false,
        string? mic = null, CameraMode? mode = null) =>
        new(backend, video, audio, mic, mode ?? CameraArgs.DefaultMode(backend, 1280, 720, 15), 1280, 720, 15, 26, 1800, 3,
            Staging);

    private static string Joined(List<string> args) => string.Join(" | ", args);

    private static string After(List<string> args, string flag) => args[args.IndexOf(flag) + 1];

    [Fact]
    public void Mac_SinAudio_NombreYModoPedido()
    {
        var a = CameraArgs.Build(Input("avfoundation"));
        Assert.Equal(["-hide_banner", "-nostats", "-loglevel", "info", "-y", "-f", "avfoundation", "-framerate", "30",
            "-video_size", "1280x720", "-i", "Cámara de la MacBook Air"], a.Take(13));
        Assert.DoesNotContain("-c:a", a);
        Assert.Contains("zerolatency", a);
        Assert.Equal("[0:v]fps=15,scale=w='min(iw,1280)':h=-2,split=2[r][p];[p]fps=1/2,scale=320:-2[pv]",
            After(a, "-filter_complex"));
        Assert.Equal("30", After(a, "-g"));
        Assert.Equal("expr:gte(t,n_forced*1800)", After(a, "-force_key_frames"));
        Assert.Equal("1800", After(a, "-segment_time"));
        Assert.Equal("3", After(a, "-segment_start_number"));
        Assert.Equal(Path.Combine(Staging, "camara.csv"), After(a, "-segment_list"));
        Assert.Equal("+live", After(a, "-segment_list_flags"));
        Assert.Equal("flush_packets=1:cluster_time_limit=2000", After(a, "-segment_format_options"));
        Assert.Contains(Path.Combine(Staging, "camara_%04d.part.mkv"), a);
        Assert.Equal(Path.Combine(Staging, "camara-preview.jpg"), a[^1]);
        Assert.Equal(["-map", "[pv]", "-q:v", "6", "-update", "1", "-atomic_writing", "1", "-f", "image2"], a[^11..^1]);
    }

    [Fact]
    public void Mac_ConAudio_UsaDefault_NuncaCero()
    {
        var a = CameraArgs.Build(Input("avfoundation", audio: true));
        Assert.Equal("Cámara de la MacBook Air:default", After(a, "-i"));
        Assert.Equal(["-map", "[r]", "-map", "0:a"], a.Skip(a.IndexOf("-map")).Take(4));
        Assert.Equal("aac", After(a, "-c:a"));
        Assert.Equal("96k", After(a, "-b:a"));
        Assert.DoesNotContain(a, x => x.EndsWith(":0", StringComparison.Ordinal));
    }

    [Fact]
    public void Mac_ModoDelReintento()
    {
        var a = CameraArgs.Build(Input("avfoundation", "2", mode: new CameraMode(1920, 1080, 25)));
        Assert.Equal("25", After(a, "-framerate"));
        Assert.Equal("1920x1080", After(a, "-video_size"));
        Assert.Equal("2", After(a, "-i"));
    }

    [Fact]
    public void Windows_VideoYAudio_YReintentoSinModo()
    {
        var a = CameraArgs.Build(Input("dshow", @"@device_pnp_\\?\usb#vid_0c45", audio: true, mic: "@device_cm_mic"));
        Assert.Equal(["-f", "dshow", "-rtbufsize", "256M", "-video_size", "1280x720", "-framerate", "15", "-i",
            @"video=@device_pnp_\\?\usb#vid_0c45:audio=@device_cm_mic"], a.Skip(5).Take(10));
        Assert.Equal("0:a", a[a.IndexOf("[r]") + 2]);

        var retry = CameraArgs.Build(Input("dshow", "Integrated Webcam") with { Mode = null });
        Assert.DoesNotContain("-video_size", retry);
        Assert.Equal("video=Integrated Webcam", After(retry, "-i"));
    }

    [Fact]
    public void Lavfi_ConYSinAudio()
    {
        var a = CameraArgs.Build(Input("lavfi", "", mode: null));
        Assert.Equal(["-re", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30"], a.Skip(5).Take(5));
        var b = CameraArgs.Build(Input("lavfi", "", audio: true, mode: null));
        // Con audio, un solo grafo lavfi (dos entradas -re se atrasan respecto del reloj).
        Assert.Equal("testsrc2=size=1280x720:rate=30[out0];sine=frequency=440:sample_rate=48000[out1]", After(b, "-i"));
        Assert.Single(b, x => x == "-i");
        Assert.Contains("0:a", b);
    }

    [Fact]
    public void Preview_SoloLaSalidaJpg()
    {
        var a = CameraArgs.BuildPreview("avfoundation", "Cámara de la MacBook Air", new CameraMode(1280, 720, 30), "/x/preview.jpg");
        Assert.Contains("-vf", a);
        Assert.Equal("fps=1/2,scale=320:-2", After(a, "-vf"));
        Assert.DoesNotContain("segment", a);
        Assert.Equal("/x/preview.jpg", a[^1]);
    }

    private static readonly string[] ModesStderr =
    [
        "[avfoundation @ 0x1] Selected video size (1234x777) is not supported by the device.",
        "[avfoundation @ 0x1] Supported modes:",
        "[avfoundation @ 0x1]   640x480@[1.000000 30.000000]fps",
        "[avfoundation @ 0x1]   1280x720@[1.000000 30.000000]fps",
        "[avfoundation @ 0x1]   1920x1080@[15.000000 25.000000]fps",
        "[in#0 @ 0x2] Error opening input: Input/output error",
    ];

    [Fact]
    public void ModosSoportados_YEleccion()
    {
        var modes = CameraArgs.ParseSupportedModes(ModesStderr);
        Assert.Equal(3, modes.Count);
        Assert.Equal(new SupportedMode(1920, 1080, 15, 25), modes[2]);
        // Mayor área ≤ W×H, fps 30 si entra.
        Assert.Equal(new CameraMode(1280, 720, 30), CameraArgs.PickMode(modes, 1280, 720));
        Assert.Equal(new CameraMode(640, 480, 30), CameraArgs.PickMode(modes, 1234, 700));
        // Más grande que todo: el de mayor área; fps fuera de rango → el máximo.
        Assert.Equal(new CameraMode(1920, 1080, 25), CameraArgs.PickMode(modes, 3840, 2160));
        // Ninguno entra: el más chico.
        Assert.Equal(new CameraMode(640, 480, 30), CameraArgs.PickMode(modes, 320, 240));
        Assert.Null(CameraArgs.PickMode([], 1280, 720));
    }

    [Fact]
    public void LineaOutput_ResolucionYFpsReales()
    {
        Assert.True(CameraArgs.IsSegmentOutputLine("Output #0, segment, to '/x/camara_%04d.part.mkv':"));
        Assert.False(CameraArgs.IsSegmentOutputLine("Input #0, avfoundation, from 'Cámara':"));
        Assert.Equal(new CameraStream(1280, 720, 15), CameraArgs.ParseOutputStream(
            "  Stream #0:0: Video: h264 (H264 / 0x34363248), yuv420p(tv, progressive), 1280x720 [SAR 1:1 DAR 16:9], q=2-31, 15 fps, 1k tbn"));
        Assert.Equal(new CameraStream(640, 360, 30), CameraArgs.ParseOutputStream(
            "  Stream #0:0(und): Video: h264, yuv420p(progressive), 640x360, q=2-31, 29.97 fps, 1k tbn"));
        Assert.Null(CameraArgs.ParseOutputStream("  Stream #0:0: Video: rawvideo (UYVY / 0x59565955), uyvy422, 1280x720, 30 fps"));
        Assert.Null(CameraArgs.ParseOutputStream("  Stream #0:1: Audio: aac (LC), 48000 Hz, mono, fltp, 96 kb/s"));
    }

    [Theory]
    [InlineData("avfoundation", "Supported modes:|  1280x720@[1.000000 30.000000]fps|Selected framerate (29.970030) is not supported by the device.", null, false, true, "unsupported")]
    [InlineData("dshow", "Could not set video options", null, false, true, "unsupported")]
    [InlineData("avfoundation", "Video device not found", 1, false, false, "not_found")]
    [InlineData("avfoundation", "No AV capture device found", 1, false, false, "not_found")]
    [InlineData("dshow", "Could not find video device with name [x] among source devices of type video.", 1, false, false, "not_found")]
    [InlineData("avfoundation", "Failed to create AV capture input device: Not authorized", 1, false, false, "permission")]
    [InlineData("avfoundation", "", 134, false, false, "permission")]
    [InlineData("avfoundation", "", -9, false, false, "permission")]
    [InlineData("dshow", "Could not run graph (sometimes caused by a device already in use by other application)", 1, false, false, "blocked")]
    [InlineData("dshow", "Error opening input: I/O error", 1, false, false, "blocked")]
    [InlineData("avfoundation", "", null, true, false, "permission")]
    [InlineData("dshow", "", null, true, false, null)]
    [InlineData("avfoundation", "algo raro", 1, false, false, null)]
    public void Clasificacion(string backend, string stderr, int? exit, bool timedOut, bool retry, string? detail)
    {
        var f = CameraArgs.Classify(backend, stderr.Split('|'), exit, timedOut);
        Assert.Equal(retry, f.RetryMode);
        Assert.Equal(detail, f.Detail);
    }

    [Fact]
    public void Csv_LineasCompletas_IgnoraLaAMedias()
    {
        var lines = CameraCsv.Parse("camara_0003.part.mkv,0.000000,10.000000\ncamara_0004.part.mkv,10.000000,20.050000\ncamara_0005.part");
        Assert.Equal([new CameraCsvLine(3, "camara_0003.part.mkv", 0, 10), new CameraCsvLine(4, "camara_0004.part.mkv", 10, 20.05)],
            lines);
        Assert.Empty(CameraCsv.Parse("camara_0003.part.mkv,0.0,1.0"));
        Assert.Empty(CameraCsv.Parse("otro.mkv,0,1\n"));
        Assert.Equal(12, CameraCsv.IndexOf("camara_0012.part.mkv"));
        Assert.Null(CameraCsv.IndexOf("camara_x.part.mkv"));
        Assert.Equal("camara_0007.part.mkv", CameraArgs.PartName(7));
    }

    [Fact]
    public void Opciones_DefaultsYNormalize()
    {
        var o = new SessionOptions();
        Assert.Equal(18L * 1024 * 1024, o.CameraBytesPerMinute);
        Assert.Equal(10, o.CameraStartTimeoutSeconds);
        Assert.Equal(20, o.CameraStallSeconds);
        Assert.Equal(120, o.CameraPreviewIdleSeconds);
        Assert.False(o.CameraLavfi);
        Assert.Null(o.CameraEnabled);
        Assert.Equal(OperatingSystem.IsMacOS(), o.EffectiveCameraEnabled);
        Assert.True(new SessionOptions { CameraEnabled = true }.EffectiveCameraEnabled);
        Assert.False(new SessionOptions { CameraEnabled = false }.EffectiveCameraEnabled);

        var bad = new SessionOptions
        {
            CameraStartTimeoutSeconds = 2, CameraStallSeconds = 5, CameraPreviewIdleSeconds = 601, CameraBytesPerMinute = 0,
        }.Normalize(null);
        Assert.Equal((10, 20, 120, 18L * 1024 * 1024),
            (bad.CameraStartTimeoutSeconds, bad.CameraStallSeconds, bad.CameraPreviewIdleSeconds, bad.CameraBytesPerMinute));
        Assert.Equal(0, new SessionOptions { CameraStallSeconds = 0 }.Normalize(null).CameraStallSeconds);
        Assert.Equal(60, new SessionOptions { CameraStartTimeoutSeconds = 60 }.Normalize(null).CameraStartTimeoutSeconds);
    }

    [Fact]
    public void Capability_SoloConCameraEnabled()
    {
        Assert.Contains("recording_session_camera_v1", HealthController.CapabilitiesFor(true));
        Assert.DoesNotContain("recording_session_camera_v1", HealthController.CapabilitiesFor(false));
        Assert.DoesNotContain("recording_session_camera_v1", HealthController.Capabilities);
        Assert.Contains("recording_session_v1", HealthController.CapabilitiesFor(true));
        var on = new AgentOptions();
        on.Session.CameraEnabled = true;
        Assert.True(HealthController.CameraEnabledFor(on));
        var off = new AgentOptions();
        off.Session.CameraEnabled = false;
        Assert.False(HealthController.CameraEnabledFor(off));
        Assert.Equal(OperatingSystem.IsMacOS(), HealthController.CameraEnabledFor(new AgentOptions()));
    }
}

/// <summary>Lista de cámaras (§5.2): parseo de avfoundation con el fixture real anonimizado, y Resolve.</summary>
public sealed class CameraDevicesTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Cameras", name));

    [Fact]
    public void Avfoundation_CuatroCamaras_SinCaptureScreenNiAudio()
    {
        var cams = CameraDevices.ParseAvfoundation(Fixture("avfoundation-macbook-air.txt"));
        Assert.Equal(
        [
            (0, "Cámara de la MacBook Air"),
            (1, "Cámara de iPhone de <nombre>"),
            (2, "Cámara de Vista del Escritorio de iPhone de <nombre>"),
            (3, "Cámara de Vista del Escritorio de la MacBook Air"),
        ], cams);
        Assert.Empty(CameraDevices.ParseAvfoundation(""));
    }

    [Fact]
    public void Resolve_PorIdLuegoPorNombre()
    {
        CameraDevice[] list =
        [
            new("@pnp_1", "Integrated Webcam", "dshow"),
            new("@pnp_2", "USB Camera", "dshow"),
            new("@pnp_3", "USB Camera", "dshow"),
        ];
        Assert.Equal("@pnp_2", CameraDevices.Resolve(list, "@pnp_2", "x")!.Id);
        Assert.Equal("@pnp_2", CameraDevices.Resolve(list, "@pnp_9", "USB Camera")!.Id); // el primero con ese nombre
        Assert.Equal("@pnp_1", CameraDevices.Resolve(list, null, "Integrated Webcam")!.Id);
        Assert.Null(CameraDevices.Resolve(list, "@pnp_9", "Otra"));
    }

    [Fact]
    public async Task Mock_CamaraSimulada_YCache()
    {
        var calls = 0;
        var d = new CameraDevices(lavfi: false, null, TimeProvider.System, () =>
        {
            calls++;
            return Task.FromResult(new CameraListResponse([CameraDevices.Mock]));
        });
        await d.ListAsync();
        await d.ListAsync();
        Assert.Equal(1, calls); // caché de 10 s
        await d.ListAsync(refresh: true);
        Assert.Equal(2, calls);
        Assert.Equal("mock-cam", (await d.ResolveAsync("mock-cam", "Cámara simulada"))!.Id);

        var lavfi = new CameraDevices(lavfi: true, null, TimeProvider.System, lister: null);
        var mock = Assert.Single((await lavfi.ListAsync()).Cameras);
        Assert.Equal(("mock-cam", "Cámara simulada", "mock"), (mock.Id, mock.Name, mock.Backend));
    }
}
