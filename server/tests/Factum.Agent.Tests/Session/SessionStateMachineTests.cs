using Factum.Agent.Services;
using Factum.Agent.Services.Session;

namespace Factum.Agent.Tests.Session;

/// <summary>
/// Máquina de estados de la sesión (grabacion-sesion-completa §5.3–§5.9) con una pista falsa: sin celular ni scrcpy.
/// </summary>
public sealed class SessionStateMachineTests : IDisposable
{
    private readonly SessionHarness _h = new();

    public void Dispose() => _h.Dispose();

    private string CaseDir => _h.Store.CaseDir(SessionHarness.CaseId);

    [Fact]
    public async Task Start_Graba_YEsIdempotente()
    {
        var s = await _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start());
        Assert.Equal("recording", s.State);
        Assert.Equal(1, s.ScreenSegmentIndex);
        Assert.False(s.Resumed);
        Assert.Equal("t_1", s.OriginId);
        Assert.True(_h.Recorder.IsActiveOn(SessionHarness.Serial));
        Assert.Single(_h.Recorder.ActiveOperations);
        Assert.Equal("recording_session", _h.Recorder.ActiveOperations.Single().Kind);

        // Dos pestañas o una recarga: 200 con el estado actual, sin otro tramo.
        var again = await _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start());
        Assert.Equal("recording", again.State);
        Assert.Single(_h.Tracks.Started);

        var doc = _h.Doc();
        Assert.Equal("recording", doc.State);
        Assert.Equal("device", doc.Audio);
        Assert.Single(doc.Segments);
        Assert.Equal("recording", doc.Segments[0].State);
        Assert.True(SessionNames.IsSegmentName(doc.Segments[0].Filename, "pantalla"));
    }

    [Fact]
    public async Task Start_OtroCasoUOtroCelular_409SessionActive()
    {
        await _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start());
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _h.Recorder.StartAsync(SessionHarness.OtherCaseId, _h.Start()));
        Assert.Equal(409, ex.Status);
        Assert.Equal("session_active", ex.Code);
        Assert.Equal(SessionHarness.CaseId, ex.Extra!["case_id"]);

        var ex2 = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start("OTRO")));
        Assert.Equal("session_active", ex2.Code);
    }

    [Fact]
    public async Task Start_ConGrabacionPuntual_409AlreadyRecording()
    {
        _h.PointRecordings.Add(new PointRecording(SessionHarness.Serial));
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start()));
        Assert.Equal("already_recording", ex.Code);
        Assert.Null(_h.Recorder.ActiveState);
    }

    [Fact]
    public async Task Start_Validaciones()
    {
        var ios = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start() with { Platform = "ios" }));
        Assert.Equal("session_platform_unsupported", ios.Code);
        var bad = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _h.Recorder.StartAsync("no-es-un-id", _h.Start()));
        Assert.Equal("invalid_case_id", bad.Code);
        var noSerial = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start() with { Serial = " " }));
        Assert.Equal("invalid_request", noSerial.Code);
    }

    [Fact]
    public async Task Start_PantallaBloqueada_409_SalvoGrabarIgual()
    {
        _h.Screen.State = ScreenState.Locked;
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start()));
        Assert.Equal("screen_locked", ex.Code);
        Assert.False(_h.Journal.Exists(SessionHarness.CaseId)); // el diario no cambió

        var s = await _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start(skip: true));
        Assert.Equal("recording", s.State);
    }

    [Fact]
    public async Task Start_SinEspacio_507()
    {
        _h.Disk.Free = 2L * 1024 * 1024 * 1024;
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start()));
        Assert.Equal(507, ex.Status);
        Assert.Equal("insufficient_storage", ex.Code);
        Assert.NotNull(ex.Extra!["required_bytes"]);
    }

    [Fact]
    public async Task Start_FallaElArranque_NoQuedaSesionNiDiario()
    {
        _h.Tracks.FailNextStart = true;
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start()));
        Assert.Equal(504, ex.Status);
        Assert.Null(_h.Recorder.ActiveState);
        Assert.False(_h.Journal.Exists(SessionHarness.CaseId));
    }

    [Fact]
    public async Task Corte_SinHueco_YCorteDemorado()
    {
        await _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start());
        Assert.NotNull(await _h.Recorder.RotateNowAsync());
        await SessionHarness.Eventually(() => _h.Doc().Segments.Count(s => s.State == "saved") == 1);
        var doc = _h.Doc();
        Assert.Equal(2, doc.Segments.Count);
        Assert.Equal("rotacion", doc.Segments[0].StopReason);
        Assert.NotNull(doc.Segments[0].OverlapSeconds);
        Assert.Empty(doc.Gaps);
        Assert.True(File.Exists(Path.Combine(CaseDir, doc.Segments[0].Filename)));
        Assert.Equal(64, doc.Segments[0].Sha256!.Length);
        Assert.NotEmpty(_h.Sink.OfType("session_segment_closed"));

        // El tramo nuevo no arranca: el actual sigue, aviso rotation_delayed, sin hueco.
        _h.Tracks.FailNextRotate = true;
        Assert.Null(await _h.Recorder.RotateNowAsync());
        var state = _h.Recorder.ActiveState!;
        Assert.Equal("recording", state.State);
        Assert.Equal(2, state.ScreenSegmentIndex);
        Assert.Contains(state.Warnings, w => w.Kind == "rotation_delayed");
        Assert.Empty(_h.Doc().Gaps);

        // El reintento sale bien y saca el aviso.
        Assert.NotNull(await _h.Recorder.RotateNowAsync());
        Assert.DoesNotContain(_h.Recorder.ActiveState!.Warnings, w => w.Kind == "rotation_delayed");
    }

    [Fact]
    public async Task Desconexion_EsperaYReanudaConElMismoSerial()
    {
        await _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start());
        _h.Connected.Clear();
        _h.Tracks.Last.RaiseExit();
        await SessionHarness.Eventually(() => _h.Recorder.ActiveState?.State == "waiting_device");
        var waiting = _h.Recorder.ActiveState!;
        Assert.Equal("desconexion", waiting.StopReason);
        Assert.NotNull(waiting.StoppedAt);
        Assert.True(_h.Recorder.IsActiveOn(SessionHarness.Serial)); // waiting_device es activa

        // Otro celular no reanuda nada.
        _h.Recorder.OnDevicesPolled(["OTRO"]);
        await Task.Delay(300);
        Assert.Equal("waiting_device", _h.Recorder.ActiveState!.State);

        // El mismo serial: reanuda en un tramo nuevo y cierra el hueco.
        _h.Connected.Add(SessionHarness.Serial);
        _h.Recorder.OnDevicesPolled([SessionHarness.Serial]);
        await SessionHarness.Eventually(() => _h.Recorder.ActiveState?.State == "recording");
        Assert.Equal(2, _h.Recorder.ActiveState!.ScreenSegmentIndex);
        await SessionHarness.Eventually(() => _h.Doc().Segments[0].State == "saved");
        var doc = _h.Doc();
        var gap = Assert.Single(doc.Gaps);
        Assert.Equal("desconexion", gap.Reason);
        Assert.NotNull(gap.To);
        Assert.Equal(doc.Segments[1].StartedAt, gap.To);
        Assert.Equal("desconexion", doc.Segments[0].StopReason);
    }

    [Fact]
    public async Task FalloGrabacion_ConCelularConectado_ReintentaSolo()
    {
        await _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start());
        _h.Tracks.Last.RaiseExit();
        await SessionHarness.Eventually(() => _h.Tracks.Started.Count == 2 && _h.Recorder.ActiveState?.State == "recording");
        var doc = _h.Doc();
        var gap = Assert.Single(doc.Gaps);
        Assert.Equal("fallo_grabacion", gap.Reason);
        Assert.NotNull(gap.To);
    }

    [Fact]
    public async Task FalloGrabacion_ReintentoFalla_Stopped()
    {
        await _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start());
        _h.Tracks.FailNextStart = true;
        _h.Tracks.Last.RaiseExit();
        await SessionHarness.Eventually(() => _h.Recorder.ActiveState is null && _h.Doc().State == "stopped");
        var doc = _h.Doc();
        Assert.Equal("fallo_grabacion", doc.StopReason);
        Assert.Equal("fallo_grabacion", Assert.Single(doc.Gaps).Reason);
    }

    [Fact]
    public async Task Stop_GuardaElTramo_AbreHueco_EsIdempotente()
    {
        await _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start());
        var s = await _h.Recorder.StopAsync(SessionHarness.CaseId, "cambio_de_paso", "t_2");
        Assert.Equal("stopped", s.State);
        Assert.Equal("cambio_de_paso", s.StopReason);
        Assert.Equal(1, s.SegmentsSaved);
        Assert.Equal("t_2", s.OriginId);
        Assert.False(_h.Recorder.IsActiveOn(SessionHarness.Serial));
        Assert.Empty(_h.Recorder.ActiveOperations);

        var doc = _h.Doc();
        Assert.Equal("saved", doc.Segments[0].State);
        Assert.Equal("cambio_de_paso", Assert.Single(doc.Gaps).Reason);

        // Idempotente: sin sesión activa, el estado del diario.
        var again = await _h.Recorder.StopAsync(SessionHarness.CaseId, "cambio_de_paso", null);
        Assert.Equal("stopped", again.State);
        // Sin diario: idle.
        var idle = await _h.Recorder.StopAsync(SessionHarness.OtherCaseId, "detenido_por_perito", null);
        Assert.Equal("idle", idle.State);

        var bad = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _h.Recorder.StopAsync(SessionHarness.CaseId, "desconexion", null));
        Assert.Equal("invalid_request", bad.Code);
    }

    [Fact]
    public async Task Retomar_CierraElHuecoYSigueLaNumeracion()
    {
        await _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start());
        await _h.Recorder.StopAsync(SessionHarness.CaseId, "cambio_de_paso", null);
        var s = await _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start());
        Assert.True(s.Resumed);
        Assert.Equal(2, s.ScreenSegmentIndex);
        var doc = _h.Doc();
        Assert.Equal(doc.SessionId, _h.Doc().SessionId);
        Assert.NotNull(Assert.Single(doc.Gaps).To);
        Assert.Equal(new[] { 1, 2 }, doc.Segments.Select(x => x.Index));
    }

    [Fact]
    public async Task WaitingDevice_Stop_PasaAStoppedSinOtroHueco()
    {
        await _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start());
        _h.Connected.Clear();
        _h.Tracks.Last.RaiseExit();
        await SessionHarness.Eventually(() => _h.Recorder.ActiveState?.State == "waiting_device");
        var s = await _h.Recorder.StopAsync(SessionHarness.CaseId, "cambio_de_paso", null);
        Assert.Equal("stopped", s.State);
        var doc = _h.Doc();
        var gap = Assert.Single(doc.Gaps);
        Assert.Equal("desconexion", gap.Reason);
        Assert.Null(gap.To);
    }

    [Fact]
    public async Task TramoVacio_Descartado_SegmentLost()
    {
        await _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start());
        _h.Tracks.EmptyFiles = true;
        await _h.Recorder.StopAsync(SessionHarness.CaseId, "detenido_por_perito", null);
        var doc = _h.Doc();
        Assert.Equal("discarded", doc.Segments[0].State);
        Assert.True(Assert.Single(doc.Gaps).SegmentLost);
        Assert.False(Directory.Exists(CaseDir) && Directory.EnumerateFiles(CaseDir).Any());
    }

    [Fact]
    public async Task Shutdown_DejaInterruptedConHuecoTatanaCerrado()
    {
        await _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start());
        var r = await _h.Recorder.ShutdownAsync(TimeSpan.FromSeconds(10));
        Assert.True(r.HadSession);
        Assert.Equal(1, r.SegmentsSaved);
        Assert.Equal(0, r.Pending);
        var doc = _h.Doc();
        Assert.Equal("interrupted", doc.State);
        Assert.Equal("tatana_cerrado", doc.StopReason);
        Assert.Equal("tatana_cerrado", Assert.Single(doc.Gaps).Reason);
        Assert.Null(_h.Recorder.ActiveState);

        var none = await _h.Recorder.ShutdownAsync(TimeSpan.FromSeconds(1));
        Assert.False(none.HadSession);
    }

    [Fact]
    public async Task Marcas_ManualYCaptura()
    {
        var noSession = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _h.Recorder.AddMarkAsync(SessionHarness.CaseId, "x", null));
        Assert.Equal("session_not_active", noSession.Code);

        await _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start());
        var m = await _h.Recorder.AddMarkAsync(SessionHarness.CaseId, "Abre el chat con Juan  ", "t_3");
        Assert.Equal("manual", m.Kind);
        Assert.Equal("Abre el chat con Juan", m.Text);
        Assert.Equal(1, m.SegmentIndex);
        Assert.Equal(32, m.Id.Length);

        var cap = await _h.Recorder.TryAddCaptureMarkAsync(SessionHarness.Serial, SessionHarness.CaseId, DateTime.UtcNow,
            "screenshot_1.png", null);
        Assert.Equal("captura", cap!.Kind);
        Assert.Equal("screenshot_1.png", cap.Filename);
        // Otro caso u otro celular: sin marca (todo queda como hoy).
        Assert.Null(await _h.Recorder.TryAddCaptureMarkAsync("OTRO", SessionHarness.CaseId, DateTime.UtcNow, "s.png", null));
        Assert.Null(await _h.Recorder.TryAddCaptureMarkAsync(SessionHarness.Serial, SessionHarness.OtherCaseId, DateTime.UtcNow, "s.png", null));
        Assert.Null(await _h.Recorder.TryAddCaptureMarkAsync(SessionHarness.Serial, null, DateTime.UtcNow, "s.png", null));

        var bad = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _h.Recorder.AddMarkAsync(SessionHarness.CaseId, new string('x', 301), null));
        Assert.Equal("invalid_request", bad.Code);

        Assert.Equal(2, _h.Doc().Marks.Count);
        Assert.Equal(2, _h.Sink.OfType("session_mark_added").Count);
    }

    [Fact]
    public async Task Tramo_Protegido_NoSeBorraDeLaCarpeta()
    {
        await _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start());
        await _h.Recorder.StopAsync(SessionHarness.CaseId, "cambio_de_paso", null);
        var name = _h.Doc().Segments[0].Filename;
        var ex = Assert.Throws<AgentHttpException>(() => _h.Store.Delete(SessionHarness.CaseId, name));
        Assert.Equal(409, ex.Status);
        Assert.Equal("session_file_protected", ex.Code);
        Assert.True(File.Exists(Path.Combine(CaseDir, name)));
        // El commit del ZIP sí los borra (ya están en el ZIP).
        Assert.True(_h.Store.Delete(SessionHarness.CaseId, name, allowSessionFiles: true));
    }

    [Fact]
    public async Task GetJournal_Y_Capacidad()
    {
        Assert.Null(_h.Recorder.GetJournal(SessionHarness.CaseId));
        await _h.Recorder.StartAsync(SessionHarness.CaseId, _h.Start());
        var j = _h.Recorder.GetJournal(SessionHarness.CaseId)!;
        Assert.Equal(SessionHarness.CaseId, j.CaseId);
        Assert.Single(j.Segments);

        var cap = _h.Recorder.Capacity(camera: false, segmentMinutes: 30);
        Assert.True(cap.CanStart);
        Assert.Equal(30L * 1024 * 1024, cap.BytesPerMinute);
        Assert.NotNull(cap.EstimatedMinutes);
        _h.Disk.Free = null;
        var unknown = _h.Recorder.Capacity(false, null);
        Assert.Null(unknown.AvailableBytes);
        Assert.Null(unknown.EstimatedMinutes);
        Assert.True(unknown.CanStart);
    }

    private sealed class PointRecording(string serial) : IRecordingStateSource
    {
        public IEnumerable<RecordingStateDto> ActiveRecordings =>
            [new RecordingStateDto("android", serial, "recording", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "g.mkv", null, false, null)];
    }
}
