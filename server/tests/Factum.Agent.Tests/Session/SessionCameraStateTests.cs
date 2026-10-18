using Factum.Agent.Services;
using Factum.Agent.Services.Session;

namespace Factum.Agent.Tests.Session;

/// <summary>
/// La cámara del procedimiento dentro de la sesión (grabacion-sesion-completa-f2 §5.6–§5.7) con pistas falsas: sin
/// celular, sin ffmpeg y sin cámara.
/// </summary>
public sealed class SessionCameraStateTests : IDisposable
{
    private readonly SessionHarness _h = new();
    private const string Case = SessionHarness.CaseId;

    public void Dispose() => _h.Dispose();

    private List<JournalSegment> Camera(SessionJournalDoc doc) =>
        doc.Segments.Where(s => s.Track == "camara").OrderBy(s => s.Index).ToList();

    private static List<SessionGapDto> CameraGaps(SessionJournalDoc doc) => doc.Gaps.Where(g => g.Track == "camara").ToList();

    [Fact]
    public async Task Start_ConCamara_GrabanLasDos_SinHueco()
    {
        var s = await _h.Recorder.StartAsync(Case, _h.StartWithCamera());
        Assert.Equal("recording", s.State);
        Assert.Equal("recording", s.CameraState);
        Assert.Equal(1, s.CameraSegmentIndex);
        Assert.NotNull(s.CameraSegmentStartedAt);
        Assert.Equal(new SessionActiveCameraDto("Cámara de la MacBook Air", "Cámara de la MacBook Air", 1280, 720, 15, false),
            s.Camera);
        Assert.Equal(1, _h.Preview.Stops); // la vista previa se corta antes de abrir la cámara

        var doc = _h.Doc();
        var cam = Assert.Single(Camera(doc));
        Assert.Equal("recording", cam.State);
        Assert.True(SessionNames.IsSegmentName(cam.Filename, "camara"));
        Assert.Equal("camara_0001.part.mkv", cam.PartFile);
        Assert.Equal(new SessionCameraDto("Cámara de la MacBook Air", 1280, 720, 15, false), cam.Camera);
        Assert.Equal("ffmpeg", cam.Process!.Exe);
        Assert.Equal(cam.Camera, doc.Camera);
        Assert.NotNull(doc.CameraRun);
        Assert.Empty(doc.Gaps);
        Assert.Equal(1800, _h.Cameras.SegmentSeconds);
        // El DTO del diario no lleva los internos.
        var json = System.Text.Json.JsonSerializer.Serialize(_h.Recorder.GetJournal(Case),
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
        Assert.Contains("\"name\":\"Cámara de la MacBook Air\"", json);
        Assert.DoesNotContain("camera_run", json);
        Assert.DoesNotContain("part_file", json);
        Assert.DoesNotContain("\"id\":\"Cámara", json);
    }

    [Fact]
    public async Task CameraStart_ConLaSesionEnCurso_Idempotente_YBusy()
    {
        await _h.Recorder.StartAsync(Case, _h.Start());
        var s = await _h.Recorder.StartCameraAsync(Case, SessionHarness.Mac);
        Assert.Equal("recording", s.CameraState);
        Assert.Empty(_h.Doc().Gaps); // D3 = A: sin hueco previo, no se crea ninguno

        var again = await _h.Recorder.StartCameraAsync(Case, SessionHarness.Mac);
        Assert.Equal("recording", again.CameraState);
        Assert.Single(_h.Cameras.Tracks);

        var busy = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _h.Recorder.StartCameraAsync(Case, new StartCameraRequest("otra", "Otra cámara")));
        Assert.Equal((409, "camera_busy"), (busy.Status, busy.Code));
    }

    [Fact]
    public async Task CameraStart_SinSesion_409_YValidaciones()
    {
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() => _h.Recorder.StartCameraAsync(Case, SessionHarness.Mac));
        Assert.Equal((409, "session_not_active"), (ex.Status, ex.Code));
        await _h.Recorder.StartAsync(Case, _h.Start());
        var bad = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _h.Recorder.StartCameraAsync(Case, new StartCameraRequest(null, "  ")));
        Assert.Equal("invalid_request", bad.Code);
        var ctl = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _h.Recorder.StartCameraAsync(Case, new StartCameraRequest(null, "cam\u0007")));
        Assert.Equal("invalid_request", ctl.Code);
        var retry = await Assert.ThrowsAsync<AgentHttpException>(() => _h.Recorder.RetryCameraAsync(Case, null));
        Assert.Equal("session_not_active", retry.Code); // sin cámara pedida antes
    }

    [Fact]
    public async Task Rotacion_TramosContiguos_YSeGuardan()
    {
        await _h.Recorder.StartAsync(Case, _h.StartWithCamera());
        await Task.Delay(50);
        _h.Cameras.Last.Rotate();
        await SessionHarness.Eventually(() => Camera(_h.Doc()).FirstOrDefault()?.State == "saved");
        var doc = _h.Doc();
        var cams = Camera(doc);
        Assert.Equal(2, cams.Count);
        Assert.Equal("rotacion", cams[0].StopReason);
        Assert.Equal(cams[0].StoppedAt, cams[1].StartedAt); // contiguos
        Assert.Equal("recording", cams[1].State);
        Assert.Null(cams[0].PartFile);
        Assert.Equal(2, _h.Recorder.ActiveState!.CameraSegmentIndex);
        Assert.Equal(1, doc.CameraRun!.CsvLinesDone);
        Assert.NotNull(cams[0].Sha256);
        Assert.Equal("recording", _h.Recorder.ActiveState!.State); // la pantalla no se tocó
        Assert.Single(doc.Segments, s => s.Track == "pantalla");
    }

    [Fact]
    public async Task Falla_HuecoCamaraFallo_Aviso_LaPantallaSigue_YRetryCierraElHueco()
    {
        await _h.Recorder.StartAsync(Case, _h.StartWithCamera());
        _h.Cameras.Last.Fail("stalled");
        await SessionHarness.Eventually(() => _h.Recorder.ActiveState?.CameraState == "failed");
        var s = _h.Recorder.ActiveState!;
        Assert.Equal("recording", s.State);
        var w = Assert.Single(s.Warnings, x => x.Kind == "camera_failed");
        Assert.Equal("stalled", w.Detail);
        Assert.Equal("Cámara de la MacBook Air", s.Camera!.Name); // con failed, la última pedida
        await SessionHarness.Eventually(() => Camera(_h.Doc())[0].State == "saved");
        var doc = _h.Doc();
        Assert.Equal("camara_fallo", Camera(doc)[0].StopReason);
        var gap = Assert.Single(CameraGaps(doc));
        Assert.Equal("camara_fallo", gap.Reason);
        Assert.Null(gap.To);
        Assert.Null(doc.CameraRun);

        var r = await _h.Recorder.RetryCameraAsync(Case, "t_r");
        Assert.Equal("recording", r.CameraState);
        Assert.DoesNotContain(r.Warnings, x => x.Kind == "camera_failed");
        Assert.Equal(2, r.CameraSegmentIndex);
        Assert.NotNull(Assert.Single(CameraGaps(_h.Doc())).To);
    }

    [Fact]
    public async Task NoAbre_EnElStart_200ConFailed_SinHueco()
    {
        _h.Cameras.FailNextStart = "permission";
        var s = await _h.Recorder.StartAsync(Case, _h.StartWithCamera());
        Assert.Equal("recording", s.State);
        Assert.Equal("failed", s.CameraState);
        Assert.Equal("permission", Assert.Single(s.Warnings, w => w.Kind == "camera_failed").Detail);
        Assert.Null(s.Camera!.Width); // nunca arrancó
        Assert.Empty(_h.Doc().Gaps);
        Assert.Empty(Camera(_h.Doc()));
    }

    [Fact]
    public async Task CameraStop_HuecoDetenidoPorPerito_LaPantallaSigue()
    {
        await _h.Recorder.StartAsync(Case, _h.StartWithCamera());
        var s = await _h.Recorder.StopCameraAsync(Case, null);
        Assert.Equal("off", s.CameraState);
        Assert.Null(s.Camera);
        Assert.Equal("recording", s.State);
        await SessionHarness.Eventually(() => Camera(_h.Doc())[0].State == "saved");
        var doc = _h.Doc();
        Assert.Equal("detenido_por_perito", Camera(doc)[0].StopReason);
        Assert.Equal("detenido_por_perito", Assert.Single(CameraGaps(doc)).Reason);
        Assert.Equal(Camera(doc)[0].StoppedAt, CameraGaps(doc)[0].From);
        Assert.Null(doc.CameraRun);
        var retry = await Assert.ThrowsAsync<AgentHttpException>(() => _h.Recorder.RetryCameraAsync(Case, null));
        Assert.Equal("session_not_active", retry.Code); // quitarla borra la cámara pedida

        // Volver a sumarla cierra el hueco (D3 = A) y sigue la numeración.
        var again = await _h.Recorder.StartCameraAsync(Case, SessionHarness.Mac);
        Assert.Equal(2, again.CameraSegmentIndex);
        Assert.NotNull(Assert.Single(CameraGaps(_h.Doc())).To);
    }

    [Fact]
    public async Task CameraStop_ConFailed_PasaAOff_YElHuecoSigueAbierto()
    {
        await _h.Recorder.StartAsync(Case, _h.StartWithCamera());
        _h.Cameras.Last.Fail(null);
        await SessionHarness.Eventually(() => _h.Recorder.ActiveState?.CameraState == "failed");
        var s = await _h.Recorder.StopCameraAsync(Case, null);
        Assert.Equal("off", s.CameraState);
        Assert.DoesNotContain(s.Warnings, w => w.Kind == "camera_failed");
        var gap = Assert.Single(CameraGaps(_h.Doc()));
        Assert.Equal("camara_fallo", gap.Reason);
        Assert.Null(gap.To);
    }

    [Fact]
    public async Task Desconexion_LaCamaraSigue_MarcaSoloEnLaCamara_YStopConElMotivoPedido()
    {
        await _h.Recorder.StartAsync(Case, _h.StartWithCamera());
        _h.Connected.Clear();
        _h.Tracks.Last.RaiseExit();
        await SessionHarness.Eventually(() => _h.Recorder.ActiveState?.State == "waiting_device");
        Assert.Equal("recording", _h.Recorder.ActiveState!.CameraState);
        Assert.False(_h.Cameras.Last.Stopped);

        // [DU1=A] marca con el cable desconectado: sin pantalla, con la ubicación en la cámara.
        var m = await _h.Recorder.AddMarkAsync(Case, "Desconecto el cable para cambiarlo", null);
        Assert.Null(m.SegmentIndex);
        Assert.Null(m.OffsetSeconds);
        Assert.Equal(1, m.CameraSegmentIndex);
        Assert.NotNull(m.CameraOffsetSeconds);

        var s = await _h.Recorder.StopAsync(Case, "cambio_de_paso", null);
        Assert.Equal("stopped", s.State);
        Assert.Equal("off", s.CameraState);
        await SessionHarness.Eventually(() => Camera(_h.Doc())[0].State == "saved");
        var doc = _h.Doc();
        Assert.Equal("cambio_de_paso", Camera(doc)[0].StopReason);
        Assert.Equal("cambio_de_paso", Assert.Single(CameraGaps(doc)).Reason);
        Assert.Equal("desconexion", Assert.Single(doc.Gaps, g => g.Track == "pantalla").Reason);
    }

    [Fact]
    public async Task Marca_SinCamaraEnWaitingDevice_409()
    {
        await _h.Recorder.StartAsync(Case, _h.Start());
        _h.Connected.Clear();
        _h.Tracks.Last.RaiseExit();
        await SessionHarness.Eventually(() => _h.Recorder.ActiveState?.State == "waiting_device");
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() => _h.Recorder.AddMarkAsync(Case, "x", null));
        Assert.Equal("session_not_active", ex.Code);
    }

    [Fact]
    public async Task Marca_ConLasDosPistas()
    {
        await _h.Recorder.StartAsync(Case, _h.StartWithCamera());
        var m = await _h.Recorder.AddMarkAsync(Case, "Abre el chat", null);
        Assert.Equal(1, m.SegmentIndex);
        Assert.Equal(1, m.CameraSegmentIndex);
        Assert.NotNull(m.CameraOffsetSeconds);
        var c = await _h.Recorder.TryAddCaptureMarkAsync(SessionHarness.Serial, Case, DateTime.UtcNow, "screenshot_1.png", null);
        Assert.Equal(1, c!.CameraSegmentIndex);
    }

    [Fact]
    public async Task ShutdownEnWaitingDevice_CortaLaCamaraConTatanaCerrado()
    {
        await _h.Recorder.StartAsync(Case, _h.StartWithCamera());
        _h.Connected.Clear();
        _h.Tracks.Last.RaiseExit();
        await SessionHarness.Eventually(() => _h.Recorder.ActiveState?.State == "waiting_device");
        await _h.Recorder.ShutdownAsync(TimeSpan.FromSeconds(10));
        Assert.True(_h.Cameras.Last.Stopped);
        var doc = _h.Doc();
        Assert.Equal("saved", Camera(doc)[0].State);
        Assert.Equal("tatana_cerrado", Camera(doc)[0].StopReason);
        Assert.Equal("tatana_cerrado", Assert.Single(CameraGaps(doc)).Reason);
        Assert.Null(doc.CameraRun);
    }

    [Fact]
    public async Task Shutdown_GrabandoLasDos_CortaLasDos()
    {
        await _h.Recorder.StartAsync(Case, _h.StartWithCamera());
        await _h.Recorder.ShutdownAsync(TimeSpan.FromSeconds(10));
        var doc = _h.Doc();
        Assert.Equal("interrupted", doc.State);
        Assert.All(doc.Segments, s => Assert.Equal("saved", s.State));
        Assert.Equal(["camara", "pantalla"], doc.Gaps.Select(g => g.Track).Order());
        Assert.All(doc.Gaps, g => Assert.Equal("tatana_cerrado", g.Reason));
    }

    [Fact]
    public async Task StopForzado_SinUltimaLinea_SeRemuxeaYGuarda()
    {
        await _h.Recorder.StartAsync(Case, _h.StartWithCamera());
        _h.Cameras.KillOnStop = true;
        await _h.Recorder.StopAsync(Case, "detenido_por_perito", null);
        var cam = Camera(_h.Doc())[0];
        Assert.Equal("saved", cam.State);
        Assert.Equal("detenido_por_perito", cam.StopReason);
        Assert.Null(cam.Forced);
    }

    [Fact]
    public async Task Disco_TasaSumada_YCorteDeLasDos()
    {
        await _h.Recorder.StartAsync(Case, _h.StartWithCamera());
        var sin = _h.Recorder.Capacity(camera: false, null).BytesPerMinute;
        var con = _h.Recorder.Capacity(camera: true, null).BytesPerMinute;
        Assert.Equal(18L * 1024 * 1024, con - sin);

        // 1 GiB de reserva + R × 2 min: con 1,05 GiB libres corta.
        _h.Disk.Free = 1024L * 1024 * 1024 + 50L * 1024 * 1024;
        await _h.Recorder.DiskTickNowAsync();
        var doc = _h.Doc();
        Assert.Equal("stopped", doc.State);
        Assert.Equal("sin_espacio", doc.StopReason);
        Assert.Equal("sin_espacio", Assert.Single(CameraGaps(doc)).Reason);
        Assert.Equal("sin_espacio", Camera(doc)[0].StopReason);
    }

    [Fact]
    public async Task Disco_EnWaitingDeviceConCamara_TambienCorta()
    {
        await _h.Recorder.StartAsync(Case, _h.StartWithCamera());
        _h.Connected.Clear();
        _h.Tracks.Last.RaiseExit();
        await SessionHarness.Eventually(() => _h.Recorder.ActiveState?.State == "waiting_device");
        _h.Disk.Free = 1024L * 1024 * 1024 + 10L * 1024 * 1024;
        await _h.Recorder.DiskTickNowAsync();
        var doc = _h.Doc();
        Assert.Equal("stopped", doc.State);
        Assert.Equal("sin_espacio", Assert.Single(CameraGaps(doc)).Reason);
    }

    [Fact]
    public async Task CameraStart_SinEspacioParaLasDos_507_YEnElStartSoloAviso()
    {
        // La pantalla sola entra (1 GiB + 2×30×30 MiB ≈ 2,76 GiB); con la cámara (+2×30×18 MiB) no.
        _h.Disk.Free = 1024L * 1024 * 1024 + 2L * 30 * 30 * 1024 * 1024 + 100L * 1024 * 1024;
        var s = await _h.Recorder.StartAsync(Case, _h.StartWithCamera());
        Assert.Equal("recording", s.State);
        Assert.Equal("off", s.CameraState);
        Assert.Contains(s.Warnings, w => w.Kind == "disk_low");
        Assert.Empty(_h.Cameras.Tracks);

        var ex = await Assert.ThrowsAsync<AgentHttpException>(() => _h.Recorder.StartCameraAsync(Case, SessionHarness.Mac));
        Assert.Equal((507, "insufficient_storage"), (ex.Status, ex.Code));
    }

    [Fact]
    public async Task PartFileAjeno_NoSePisa()
    {
        var dir = _h.Journal.DirFor(Case);
        Directory.CreateDirectory(dir);
        var ajeno = Path.Combine(dir, "camara_0001.part.mkv");
        await File.WriteAllBytesAsync(ajeno, [1, 2, 3]);
        await _h.Recorder.StartAsync(Case, _h.StartWithCamera());
        Assert.Equal(2, _h.Cameras.StartNumbers.Single());
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(ajeno));
    }
}

/// <summary>Con <c>CameraEnabled = false</c>: 400 <c>session_camera_unsupported</c> y el start ignora la cámara.</summary>
public sealed class SessionCameraDisabledTests : IDisposable
{
    private readonly SessionHarness _h = new(o => o.CameraEnabled = false);

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task FlagApagado()
    {
        Assert.False(_h.Recorder.CameraEnabled);
        var s = await _h.Recorder.StartAsync(SessionHarness.CaseId, _h.StartWithCamera());
        Assert.Equal("off", s.CameraState);
        Assert.Empty(_h.Cameras.Tracks);
        foreach (var call in new Func<Task>[]
                 {
                     () => _h.Recorder.StartCameraAsync(SessionHarness.CaseId, SessionHarness.Mac),
                     () => _h.Recorder.StopCameraAsync(SessionHarness.CaseId, null),
                     () => _h.Recorder.RetryCameraAsync(SessionHarness.CaseId, null),
                 })
        {
            var ex = await Assert.ThrowsAsync<AgentHttpException>(call);
            Assert.Equal((400, "session_camera_unsupported"), (ex.Status, ex.Code));
        }
    }
}
