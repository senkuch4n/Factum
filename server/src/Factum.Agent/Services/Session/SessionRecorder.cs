using System.Collections.Concurrent;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.WebSockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SessionOptions = Factum.Agent.Models.SessionOptions;

namespace Factum.Agent.Services.Session;

/// <summary>Estado de la recuperación al arrancar (§5.10): <c>running</c> | <c>done</c>.</summary>
public sealed class SessionRecoveryStatus
{
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _started;

    public string Status => _done.Task.IsCompleted ? "done" : _started ? "running" : "done";
    public Task Completion => _started ? _done.Task : Task.CompletedTask;

    internal void MarkRunning() => _started = true;
    internal void MarkDone() => _done.TrySetResult();
}

/// <summary>
/// Grabación de la sesión completa (grabacion-sesion-completa §5.3–§5.11). Una sola sesión activa por Tatana. Tatana es la
/// fuente de verdad mientras se graba: cada cambio se escribe en el diario del caso (<see cref="SessionJournal"/>), que
/// sobrevive a recargas y reinicios. Las operaciones de ciclo de vida (start, stop, corte, reconexión, apagado) van de a
/// una (<c>_gate</c>); las marcas y la lectura del estado solo toman el lock corto del diario (<c>_sync</c>). Nada
/// depende del <c>RequestAborted</c> del navegador.
/// </summary>
public sealed class SessionRecorder : IOperationSource, IDisposable
{
    public const string OperationKind = "recording_session";
    internal const int MaxMarkText = 300;
    private static readonly TimeSpan ExitSettle = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan GetStateTimeout = TimeSpan.FromSeconds(3);
    private static readonly Lazy<string> AdbPath = new(() => ToolResolver.Find(AgentTools.Adb)?.Path ?? "adb");

    private readonly SessionOptions _opt;
    private readonly bool _mock;
    private readonly long _minFree;
    private readonly string _dataDir;
    private readonly string _evidenceDir;
    private readonly SessionJournal _journal;
    private readonly SegmentCloser _closer;
    private readonly StayAwakeKeeper _keeper;
    private readonly ISessionTrackFactory _tracks;
    private readonly IAndroidScreenProbe _screen;
    private readonly IAgentDiskProbe _disk;
    private readonly IAgentEventSink _events;
    private readonly IEnumerable<IRecordingStateSource> _pointRecordings;
    private readonly SessionRecoveryStatus _recovery;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly Func<string, Task<bool>> _isConnected;
    private readonly ICameraTrackFactory? _cameras;
    private readonly ICameraPreview? _preview;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _sync = new();
    private Active? _active;
    private Active? _previous;
    private volatile HashSet<string> _lastSerials = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _pendingRestores = new(StringComparer.Ordinal);
    private int _restoring;

    public SessionRecorder(IOptions<AgentOptions> opts, SessionJournal journal, SegmentCloser closer,
        StayAwakeKeeper keeper, ISessionTrackFactory tracks, IAndroidScreenProbe screen, IAgentDiskProbe disk,
        IAgentEventSink events, IEnumerable<IRecordingStateSource> pointRecordings, SessionRecoveryStatus recovery,
        TimeProvider time, ILogger<SessionRecorder> log, ICameraTrackFactory cameras, ICameraPreview preview)
        : this(opts.Value, journal, closer, keeper, tracks, screen, disk, events, pointRecordings, recovery, time, log,
            isConnected: null, cameras, preview)
    {
    }

    internal SessionRecorder(AgentOptions opts, SessionJournal journal, SegmentCloser closer, StayAwakeKeeper keeper,
        ISessionTrackFactory tracks, IAndroidScreenProbe screen, IAgentDiskProbe disk, IAgentEventSink events,
        IEnumerable<IRecordingStateSource> pointRecordings, SessionRecoveryStatus recovery, TimeProvider time,
        ILogger? log, Func<string, Task<bool>>? isConnected, ICameraTrackFactory? cameras = null,
        ICameraPreview? preview = null)
    {
        _log = log ?? NullLogger.Instance;
        _opt = (opts.Session ?? new SessionOptions()).Clone().Normalize(_log);
        _mock = opts.Mock;
        _minFree = Math.Max(0, opts.MinFreeBytes);
        _dataDir = Path.GetFullPath(opts.DataDirectory);
        _evidenceDir = opts.ResolveEvidenceDirectory();
        _journal = journal;
        _closer = closer;
        _keeper = keeper;
        _tracks = tracks;
        _screen = screen;
        _disk = disk;
        _events = events;
        _pointRecordings = pointRecordings;
        _recovery = recovery;
        _time = time;
        _isConnected = isConnected ?? DefaultIsConnectedAsync;
        _cameras = cameras;
        _preview = preview;
    }

    /// <summary>F2 (§5.10): cámara del procedimiento habilitada en este Tatana (y con fábrica de pistas).</summary>
    public bool CameraEnabled => _opt.EffectiveCameraEnabled && _cameras is not null;

    internal SessionOptions Options => _opt;

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    // ── Sesión activa ─────────────────────────────────────────────────────────

    private sealed class Active(SessionJournalDoc doc, string caseId, string serial, int androidVersion)
    {
        public SessionJournalDoc Doc { get; } = doc;
        public string CaseId { get; } = caseId;
        public string Serial { get; } = serial;
        public int AndroidVersion { get; } = androidVersion;
        public string State = SessionStates.Starting;
        public ISessionTrack? Track;
        public JournalSegment? Current;
        public DateTime? RunStartedAt;
        public string? OriginId;
        public bool Resumed;
        public readonly Dictionary<string, SessionWarningDto> Warnings = new(StringComparer.Ordinal);
        public CancellationTokenSource RunCts = new();
        public Task CloseChain = Task.CompletedTask;
        public long? LastWarnMinutes;
        public int ResumeScheduled;
        // ── Cámara del procedimiento (F2, §5.6) ──
        public ICameraTrack? Camera;                 // null = no graba
        public JournalSegment? CameraCurrent;        // tramo de cámara abierto
        public string CameraState = CameraStates.Off;
        public CameraSpec? CameraSpec;               // última cámara pedida en esta sesión (para retry)
        public CameraStart? CameraStarted;           // W×H, fps y ancla del tramo continuo en curso
        public string? CameraStopReason;             // motivo del cierre pedido (para la última línea del CSV)
        public CancellationTokenSource? CameraCts;   // chequeo de disco mientras graba la cámara sola
    }

    public bool IsActiveOn(string serial)
    {
        var a = Volatile.Read(ref _active);
        return a is not null && a.Serial == serial && SessionStates.IsActive(a.State);
    }

    public string? ActiveCaseId => Volatile.Read(ref _active)?.CaseId;

    /// <summary><c>/agent/state.recording_session</c>: solo si hay una sesión en un estado activo.</summary>
    public SessionStateDto? ActiveState
    {
        get
        {
            var a = Volatile.Read(ref _active);
            return a is not null && SessionStates.IsActive(a.State) ? BuildState(a) : null;
        }
    }

    public string RecoveryStatus => _recovery.Status;

    public IEnumerable<AgentOperation> ActiveOperations
    {
        get
        {
            var a = Volatile.Read(ref _active);
            if (a is null || !SessionStates.IsActive(a.State)) return [];
            DateTime since;
            lock (_sync) since = a.RunStartedAt ?? a.Doc.Segments.Select(s => (DateTime?)s.StartedAt).Max() ?? Now;
            return [new AgentOperation(OperationKind, new DateTimeOffset(since, TimeSpan.Zero), a.CaseId)];
        }
    }

    // ── Start (§5.4) ─────────────────────────────────────────────────────────

    public async Task<SessionStateDto> StartAsync(string caseIdRaw, StartSessionRequest req)
    {
        var caseId = CaseEvidenceStore.RequireCaseId(caseIdRaw);
        var serial = req.Serial?.Trim();
        if (string.IsNullOrEmpty(serial) || serial.Length > 128 || serial.Any(char.IsControl) ||
            serial.IndexOfAny(['/', '\\']) >= 0)
            throw Invalid("Falta el serial del celular o no es válido");
        var platform = string.IsNullOrWhiteSpace(req.Platform) ? "android" : req.Platform.Trim().ToLowerInvariant();
        if (platform == "ios")
            throw new AgentHttpException(400, AgentErrorCodes.SessionPlatformUnsupported,
                "Esta versión de Tatana no graba la sesión en iPhone.");
        if (platform != "android") throw Invalid("Plataforma inválida");
        var minutes = req.SegmentMinutes ?? 30;
        if (minutes <= 0) throw Invalid("segment_minutes inválido");
        var clamped = Math.Clamp(minutes, _opt.MinSegmentMinutes, _opt.MaxSegmentMinutes);
        if (clamped != minutes)
            _log.LogInformation("Sesión: segment_minutes {Pedido} fuera de rango, se usa {Usado}", minutes, clamped);
        var originId = EventAttribution.Normalize(req.OriginId);
        var androidVersion = Math.Max(0, req.AndroidVersion ?? 0);
        var cameraSpec = ParseCamera(req.Camera);
        if (cameraSpec is not null && !CameraEnabled)
        {
            _log.LogInformation("Sesión: se pidió la cámara del procedimiento pero no está habilitada en este Tatana; se ignora");
            cameraSpec = null;
        }

        // Un diario a medio recuperar no se toca: se espera a la recuperación del arranque.
        await _recovery.Completion;
        await _gate.WaitAsync();
        try
        {
            if (Volatile.Read(ref _active) is { } current)
            {
                if (current.CaseId == caseId && current.Serial == serial)
                {
                    if (originId is not null) current.OriginId = originId;
                    if (current.State == SessionStates.WaitingDevice && await _isConnected(serial))
                        await BeginRunLockedAsync(current, blockingScreenCheck: !req.SkipScreenCheck);
                    if (cameraSpec is not null && current.CameraState == CameraStates.Off &&
                        current.State is SessionStates.Recording or SessionStates.Rotating or SessionStates.WaitingDevice)
                        await StartCameraLockedAsync(current, cameraSpec, explicitRequest: false);
                    return BuildState(current);
                }
                throw new AgentHttpException(409, AgentErrorCodes.SessionActive,
                    "Hay una grabación de la sesión en curso en este Tatana (otro caso o celular).",
                    new Dictionary<string, object?> { ["case_id"] = current.CaseId });
            }

            if (_pointRecordings.Any(s => SafeRecordings(s).Any(r => r.Platform == "android" && r.Serial == serial)))
                throw new AgentHttpException(409, AgentErrorCodes.AlreadyRecording, "Ya hay una grabación de Android en curso.");

            // Espacio (§5.8).
            var available = AvailableBytes();
            var rate = ScreenRate(null);
            if (!SessionDiskBudget.CanStart(available, _minFree, clamped, rate))
            {
                var required = SessionDiskBudget.RequiredToStart(_minFree, clamped, rate);
                throw new AgentHttpException(507, AgentErrorCodes.InsufficientStorage,
                    $"No hay espacio en el disco de esta PC para grabar la sesión (quedan {CaseEvidenceStore.FormatBytes(available ?? 0)})",
                    new Dictionary<string, object?> { ["available_bytes"] = available, ["required_bytes"] = required });
            }

            // Un cierre en segundo plano de la sesión anterior del mismo caso termina antes de releer el diario.
            if (_previous is { } prev && prev.CaseId == caseId)
                await AwaitQuietly(prev.CloseChain);

            var doc = LoadOrCreate(caseId);
            lock (_sync)
            {
                doc.Serial = serial;
                doc.Platform = "android";
                doc.AgentVersion = AgentVersion.Current;
                doc.SegmentMinutes = clamped;
                doc.Audio = AndroidScreenTrack.AudioLabel(androidVersion);
                doc.AndroidVersion = androidVersion;
            }
            var a = new Active(doc, caseId, serial, androidVersion)
            {
                OriginId = originId,
                Resumed = doc.Segments.Count > 0,
            };
            Volatile.Write(ref _active, a);
            await EmitStateAsync(a);
            try
            {
                await BeginRunLockedAsync(a, blockingScreenCheck: !req.SkipScreenCheck);
            }
            catch
            {
                // El diario no cambió (§5.4): la sesión queda como estaba.
                Volatile.Write(ref _active, null);
                await EmitAsync("session_state", StateFromDoc(ReloadOrSame(caseId, doc), originId));
                throw;
            }
            // D1 = A: la cámara arranca después del arranque confirmado de la pantalla; si no abre, la pantalla sigue.
            if (cameraSpec is not null) await StartCameraLockedAsync(a, cameraSpec, explicitRequest: false);
            return BuildState(a);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static IEnumerable<RecordingStateDto> SafeRecordings(IRecordingStateSource s)
    {
        try { return s.ActiveRecordings.ToList(); }
        catch { return []; }
    }

    private SessionJournalDoc LoadOrCreate(string caseId)
    {
        var load = _journal.Load(caseId);
        if (load.Unreadable)
        {
            var moved = _journal.QuarantineUnreadable(caseId, _time.GetLocalNow().DateTime);
            _log.LogError("Diario de la sesión del caso {CaseId} ilegible ({Error}); renombrado a {Path} y se empieza uno nuevo",
                caseId, load.Error, moved);
        }
        return load.Doc ?? new SessionJournalDoc
        {
            SessionId = Guid.NewGuid().ToString("N"),
            CaseId = caseId,
            State = SessionStates.Idle,
        };
    }

    private SessionJournalDoc ReloadOrSame(string caseId, SessionJournalDoc fallback)
    {
        try { return _journal.Load(caseId).Doc ?? fallback; }
        catch { return fallback; }
    }

    /// <summary>
    /// Arranca un tramo continuo (start, reanudación por reconexión o tras una falla). Con el gate tomado. Si falla,
    /// lanza y el diario no cambia.
    /// </summary>
    private async Task BeginRunLockedAsync(Active a, bool blockingScreenCheck)
    {
        // Chequeo de pantalla (§5.4 paso 4): bloqueante en el start; en la reanudación solo avisa (§5.7).
        var probe = await _screen.GetStateAsync(a.Serial, CancellationToken.None);
        if (blockingScreenCheck && AndroidScreenProbe.ScreenBlock(probe.State, "grabar") is { } block) throw block;

        var doc = a.Doc;
        StayAwakeState? stay;
        lock (_sync) stay = doc.StayAwake;
        stay = await _keeper.BeginAsync(stay, a.Serial);

        int index;
        lock (_sync) index = NextIndex(doc, SessionNames.TrackScreen);
        var filename = SessionNames.SegmentName(SessionNames.TrackScreen, index, _time.GetLocalNow().DateTime);
        var staging = _journal.StagingPath(a.CaseId, filename);
        Directory.CreateDirectory(Path.GetDirectoryName(staging)!);

        var track = _tracks.CreateScreen(a.Serial, a.AndroidVersion);
        TrackStart started;
        try
        {
            started = await track.StartAsync(staging);
        }
        catch
        {
            track.Dispose();
            AdbService.TryDelete(staging);
            throw;
        }
        track.Exited += exit => _ = Task.Run(() => OnTrackExitedAsync(a, exit));

        lock (_sync)
        {
            doc.StayAwake = stay;
            GapRules.CloseOpenGap(doc.Gaps, SessionNames.TrackScreen, started.StartedAt, TimeZoneInfo.Local);
            var seg = new JournalSegment
            {
                Id = Guid.NewGuid().ToString("N"),
                Track = SessionNames.TrackScreen,
                Index = index,
                Filename = filename,
                State = SegmentStates.Recording,
                StartedAt = started.StartedAt,
                Process = started.Pid is { } pid && started.ProcessStartedAt is { } ps
                    ? new SegmentProcess { Pid = pid, StartedAt = ps, Exe = started.Exe }
                    : null,
            };
            doc.Segments.Add(seg);
            doc.State = SessionStates.Recording;
            doc.StopReason = null;
            doc.StoppedAt = null;
            doc.LastAliveAt = Now;
            a.Track = track;
            a.Current = seg;
            a.State = SessionStates.Recording;
            a.RunStartedAt = started.StartedAt;
            if (doc.Segments.Count > 1) a.Resumed = true;
            a.Warnings.Remove(SessionWarningKinds.RotationDelayed);
            ApplyScreenWarning(a, probe.State);
            SaveLocked(doc);
        }
        a.RunCts = new CancellationTokenSource();
        StartLoops(a, a.RunCts.Token, started.StartedAt);
        _log.LogInformation("Sesión del caso {CaseId}: tramo {Index} grabando ({File})", a.CaseId, index, filename);
        await EmitStateAsync(a);
    }

    internal static int NextIndex(SessionJournalDoc doc, string track) =>
        doc.Segments.Where(s => s.Track == track).Select(s => s.Index).DefaultIfEmpty(0).Max() + 1;

    // ── Loops: corte, espacio y latido, pantalla ─────────────────────────────

    private TimeSpan SegmentDuration(Active a) =>
        _opt.MockSegmentSeconds > 0
            ? TimeSpan.FromSeconds(_opt.MockSegmentSeconds)
            : TimeSpan.FromMinutes(Math.Max(1, a.Doc.SegmentMinutes));

    private void StartLoops(Active a, CancellationToken ct, DateTime runStart)
    {
        _ = Task.Run(() => RotationLoopAsync(a, runStart, ct));
        _ = Task.Run(() => TickLoopAsync(a, TimeSpan.FromSeconds(_opt.DiskCheckSeconds), DiskTickAsync, ct));
        if (_opt.ScreenProbeSeconds > 0)
            _ = Task.Run(() => TickLoopAsync(a, TimeSpan.FromSeconds(_opt.ScreenProbeSeconds), ScreenTickAsync, ct));
    }

    private async Task RotationLoopAsync(Active a, DateTime lastStart, CancellationToken ct)
    {
        var due = lastStart + SegmentDuration(a);
        while (!ct.IsCancellationRequested)
        {
            var delay = due - Now;
            try
            {
                if (delay > TimeSpan.Zero) await Task.Delay(delay, _time, ct);
            }
            catch (OperationCanceledException) { return; }
            if (ct.IsCancellationRequested) return;
            var started = await RotateOnceAsync(a);
            due = started is { } t ? t + SegmentDuration(a) : Now + TimeSpan.FromSeconds(_opt.RotationRetrySeconds);
        }
    }

    private async Task TickLoopAsync(Active a, TimeSpan interval, Func<Active, Task> tick, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(interval, _time, ct); }
            catch (OperationCanceledException) { return; }
            try { await tick(a); }
            catch (Exception ex) { _log.LogWarning(ex, "Sesión: error en el chequeo periódico"); }
        }
    }

    /// <summary>Para los tests: fuerza el corte ya. Devuelve el inicio del tramo nuevo, o null si no se pudo.</summary>
    internal async Task<DateTime?> RotateNowAsync()
    {
        var a = Volatile.Read(ref _active);
        return a is null ? null : await RotateOnceAsync(a);
    }

    /// <summary>Para los tests: corre el chequeo de disco ya.</summary>
    internal Task DiskTickNowAsync() => Volatile.Read(ref _active) is { } a ? DiskTickAsync(a) : Task.CompletedTask;

    // Corte solapado (§5.4, DU2=A). Devuelve el inicio del tramo nuevo, o null si se demoró (rotation_delayed).
    private async Task<DateTime?> RotateOnceAsync(Active a)
    {
        await _gate.WaitAsync();
        try
        {
            if (!ReferenceEquals(Volatile.Read(ref _active), a) || a.State != SessionStates.Recording ||
                a.Track is null || a.Current is null)
                return Now; // nada que cortar: el lazo se reprograma (o ya se canceló)

            a.State = SessionStates.Rotating;
            await EmitStateAsync(a);
            var prev = a.Current;
            int index;
            lock (_sync) index = NextIndex(a.Doc, SessionNames.TrackScreen);
            var filename = SessionNames.SegmentName(SessionNames.TrackScreen, index, _time.GetLocalNow().DateTime);
            var staging = _journal.StagingPath(a.CaseId, filename);
            try
            {
                var (next, stop) = await a.Track.RotateAsync(staging, s =>
                {
                    lock (_sync)
                    {
                        var seg = new JournalSegment
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Track = SessionNames.TrackScreen,
                            Index = index,
                            Filename = filename,
                            State = SegmentStates.Recording,
                            StartedAt = s.StartedAt,
                            Process = s.Pid is { } pid && s.ProcessStartedAt is { } ps
                                ? new SegmentProcess { Pid = pid, StartedAt = ps, Exe = s.Exe }
                                : null,
                        };
                        a.Doc.Segments.Add(seg);
                        a.Current = seg;
                        SaveLocked(a.Doc);
                    }
                    return Task.CompletedTask;
                });
                lock (_sync)
                {
                    prev.StoppedAt = stop.StopRequestedAt;
                    prev.OverlapSeconds = Math.Round(Math.Max(0, (stop.StopRequestedAt - next.StartedAt).TotalSeconds), 1,
                        MidpointRounding.AwayFromZero);
                    prev.StopReason = SessionReasons.Rotacion;
                    prev.State = SegmentStates.Closing;
                    prev.Forced = stop.Clean ? null : true;
                    prev.Process = null;
                    SaveLocked(a.Doc);
                }
                StayAwakeState? stay;
                lock (_sync) stay = a.Doc.StayAwake;
                await _keeper.ReassertAsync(stay, a.Serial);
                lock (_sync)
                {
                    a.Warnings.Remove(SessionWarningKinds.RotationDelayed);
                    a.State = SessionStates.Recording;
                    SaveLocked(a.Doc);
                }
                EnqueueClose(a, prev, new LostGap(prev.StartedAt, next.StartedAt, SessionReasons.FalloGrabacion));
                _log.LogInformation("Sesión del caso {CaseId}: corte al tramo {Index} (solapamiento {Overlap} s)",
                    a.CaseId, index, prev.OverlapSeconds);
                await EmitStateAsync(a);
                return next.StartedAt;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Sesión del caso {CaseId}: no se pudo abrir el tramo {Index}; el actual sigue grabando",
                    a.CaseId, index);
                lock (_sync)
                {
                    a.Warnings[SessionWarningKinds.RotationDelayed] = new SessionWarningDto(SessionWarningKinds.RotationDelayed, Now);
                    if (a.State == SessionStates.Rotating) a.State = SessionStates.Recording;
                }
                await EmitStateAsync(a);
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DiskTickAsync(Active a)
    {
        if (!ReferenceEquals(Volatile.Read(ref _active), a)) return;
        lock (_sync)
        {
            if (!SessionStates.IsActive(a.State)) return;
            a.Doc.LastAliveAt = Now;
            SaveLocked(a.Doc);
        }
        // F2 (§5.6): corre si graba la pantalla o la cámara (D2 = A: la cámara sigue en waiting_device).
        var screenRecording = a.State is SessionStates.Recording or SessionStates.Rotating;
        if (!screenRecording && a.CameraState != CameraStates.Recording) return;

        var available = AvailableBytes();
        var rate = TotalRate(a);
        if (SessionDiskBudget.ShouldCut(available, _minFree, rate, _opt.CutReserveMinutes))
        {
            _log.LogWarning("Sesión del caso {CaseId}: poco espacio en el disco ({Available} bytes); se corta la grabación",
                a.CaseId, available);
            await _gate.WaitAsync();
            try
            {
                if (ReferenceEquals(Volatile.Read(ref _active), a) && a.State is SessionStates.Recording or SessionStates.Rotating)
                    await StopLockedAsync(a, SessionReasons.SinEspacio, SessionStates.Stopped, closeBudget: null);
                else if (ReferenceEquals(Volatile.Read(ref _active), a) && a.State == SessionStates.WaitingDevice &&
                         a.CameraState == CameraStates.Recording)
                    await FinishInactiveLockedAsync(a, SessionReasons.SinEspacio, SessionStates.Stopped,
                        SessionReasons.SinEspacio);
            }
            finally
            {
                _gate.Release();
            }
            return;
        }

        var minutesLeft = SessionDiskBudget.MinutesLeft(available, _minFree, rate);
        var emit = false;
        lock (_sync)
        {
            if (SessionDiskBudget.ShouldWarn(minutesLeft, _opt.WarnMinutesLeft))
            {
                var had = a.Warnings.TryGetValue(SessionWarningKinds.DiskLow, out var old);
                emit = !had || SessionDiskBudget.ShouldReemit(a.LastWarnMinutes, minutesLeft!.Value);
                a.Warnings[SessionWarningKinds.DiskLow] = new SessionWarningDto(SessionWarningKinds.DiskLow,
                    old?.Since ?? Now, available, minutesLeft);
                if (emit) a.LastWarnMinutes = minutesLeft;
            }
            else if (a.Warnings.Remove(SessionWarningKinds.DiskLow))
            {
                a.LastWarnMinutes = null;
                emit = true;
            }
        }
        if (emit) await EmitStateAsync(a);
    }

    // Con la pantalla sin grabar (waiting_device) y la cámara grabando, el chequeo de disco lo hace este lazo.
    private Task CameraDiskTickAsync(Active a) =>
        a.State is SessionStates.Recording or SessionStates.Rotating ? Task.CompletedTask : DiskTickAsync(a);

    private async Task ScreenTickAsync(Active a)
    {
        if (!ReferenceEquals(Volatile.Read(ref _active), a) || a.State != SessionStates.Recording) return;
        var probe = await _screen.GetStateAsync(a.Serial, CancellationToken.None);
        bool changed;
        lock (_sync) changed = ApplyScreenWarning(a, probe.State);
        if (changed) await EmitStateAsync(a);
    }

    // Avisos de pantalla (§5.7): nunca cortan la grabación. Devuelve true si cambió la lista.
    private bool ApplyScreenWarning(Active a, ScreenState state)
    {
        var before = string.Join(",", a.Warnings.Keys.Order(StringComparer.Ordinal));
        switch (state)
        {
            case ScreenState.Off:
                a.Warnings.Remove(SessionWarningKinds.ScreenLocked);
                if (!a.Warnings.ContainsKey(SessionWarningKinds.ScreenOff))
                    a.Warnings[SessionWarningKinds.ScreenOff] = new SessionWarningDto(SessionWarningKinds.ScreenOff, Now);
                break;
            case ScreenState.Locked:
                a.Warnings.Remove(SessionWarningKinds.ScreenOff);
                if (!a.Warnings.ContainsKey(SessionWarningKinds.ScreenLocked))
                    a.Warnings[SessionWarningKinds.ScreenLocked] = new SessionWarningDto(SessionWarningKinds.ScreenLocked, Now);
                break;
            case ScreenState.On:
                a.Warnings.Remove(SessionWarningKinds.ScreenOff);
                a.Warnings.Remove(SessionWarningKinds.ScreenLocked);
                break;
        }
        return before != string.Join(",", a.Warnings.Keys.Order(StringComparer.Ordinal));
    }

    // ── Salida de scrcpy, desconexión y reconexión (§5.7) ────────────────────

    private async Task OnTrackExitedAsync(Active a, TrackExit exit)
    {
        // Se deja asentar el USB: scrcpy suele salir antes de que adb registre la desconexión.
        await Task.Delay(ExitSettle);
        await _gate.WaitAsync();
        try
        {
            if (!ReferenceEquals(Volatile.Read(ref _active), a) ||
                a.State is not (SessionStates.Recording or SessionStates.Rotating) || a.Current is null)
                return;
            if (_journal.StagingPath(a.CaseId, a.Current.Filename) != exit.OutputPath) return;

            await a.RunCts.CancelAsync();
            var connected = await _isConnected(a.Serial);
            var reason = connected ? SessionReasons.FalloGrabacion : SessionReasons.Desconexion;
            var seg = a.Current;
            try { a.Track?.Dispose(); } catch { /* best effort */ }
            a.Track = null;
            lock (_sync)
            {
                seg.StoppedAt = exit.At;
                seg.StopReason = reason;
                seg.State = SegmentStates.Closing;
                seg.Process = null;
                a.Current = null;
                a.Doc.Gaps.Add(NewGap(exit.At, reason, SessionNames.TrackScreen));
                a.Doc.StopReason = reason;
                a.Doc.StoppedAt = exit.At;
                a.State = connected ? SessionStates.Starting : SessionStates.WaitingDevice;
                a.Doc.State = a.State;
                a.Warnings.Remove(SessionWarningKinds.ScreenOff);
                a.Warnings.Remove(SessionWarningKinds.ScreenLocked);
                SaveLocked(a.Doc);
            }
            EnqueueClose(a, seg, new LostGap(seg.StartedAt, exit.At, reason));
            _log.LogWarning("Sesión del caso {CaseId}: scrcpy terminó ({Reason})", a.CaseId, reason);
            await EmitStateAsync(a);
            if (connected)
            {
                Interlocked.Exchange(ref a.ResumeScheduled, 1);
                _ = Task.Run(() => ResumeAfterDelayAsync(a, SessionReasons.FalloGrabacion));
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Sesión: error manejando la salida de scrcpy");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Final de cada ciclo del sondeo de dispositivos (cada 3 s, en <c>Program.cs</c>): reconexión del MISMO serial
    /// (§5.7, D7 A) y restauraciones pendientes de "pantalla siempre encendida".
    /// </summary>
    public void OnDevicesPolled(IEnumerable<string> serials)
    {
        var set = serials.ToHashSet(StringComparer.Ordinal);
        _lastSerials = set;
        if (Volatile.Read(ref _active) is { State: SessionStates.WaitingDevice } a && set.Contains(a.Serial) &&
            Interlocked.CompareExchange(ref a.ResumeScheduled, 1, 0) == 0)
            _ = Task.Run(() => ResumeAfterDelayAsync(a, SessionReasons.Desconexion));
        if (!_pendingRestores.IsEmpty && Interlocked.CompareExchange(ref _restoring, 1, 0) == 0)
            _ = Task.Run(async () =>
            {
                try { await RestorePendingAsync(set); }
                finally { Interlocked.Exchange(ref _restoring, 0); }
            });
    }

    private async Task ResumeAfterDelayAsync(Active a, string reason)
    {
        try
        {
            if (_opt.ResumeDelaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(_opt.ResumeDelaySeconds), _time);
            await _gate.WaitAsync();
            try
            {
                if (!ReferenceEquals(Volatile.Read(ref _active), a) ||
                    a.State is not (SessionStates.WaitingDevice or SessionStates.Starting))
                    return;
                if (reason == SessionReasons.Desconexion && !await _isConnected(a.Serial)) return;
                try
                {
                    // Sin chequeo de pantalla bloqueante: si está apagada o bloqueada, solo el aviso.
                    await BeginRunLockedAsync(a, blockingScreenCheck: false);
                    _log.LogInformation("Sesión del caso {CaseId}: reanudada tras {Reason}", a.CaseId, reason);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Sesión del caso {CaseId}: no se pudo reanudar tras {Reason}", a.CaseId, reason);
                    await FinishInactiveLockedAsync(a, SessionReasons.FalloGrabacion, SessionStates.Stopped);
                }
            }
            finally
            {
                Interlocked.Exchange(ref a.ResumeScheduled, 0);
                _gate.Release();
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Sesión: error en la reanudación automática");
        }
    }

    // Sin captura en curso (waiting_device o reintento fallido): pasa a un estado final sin abrir otro hueco (el de la
    // desconexión o la falla ya está abierto).
    private async Task FinishInactiveLockedAsync(Active a, string reason, string finalState, string? cameraReason = null)
    {
        // D9 = A: no hay cámara sin sesión. Se corta con el motivo pedido (stop) o el de la sesión.
        await StopCameraLockedAsync(a, cameraReason ?? reason);
        await AwaitQuietly(a.CloseChain);
        var connected = await _isConnected(a.Serial);
        StayAwakeState? stay;
        lock (_sync) stay = a.Doc.StayAwake;
        await _keeper.RestoreAsync(stay, connected);
        lock (_sync)
        {
            a.State = finalState;
            a.Doc.State = finalState;
            a.Doc.StopReason = reason;
            a.Doc.StoppedAt ??= Now;
            a.Warnings.Clear();
            SaveLocked(a.Doc);
            if (a.Doc.StayAwake?.RestorePending == true) _pendingRestores[a.CaseId] = a.Serial;
        }
        Volatile.Write(ref _active, null);
        _previous = a;
        await EmitStateAsync(a);
    }

    private async Task RestorePendingAsync(IReadOnlySet<string> present)
    {
        foreach (var (caseId, serial) in _pendingRestores.ToList())
        {
            if (!present.Contains(serial)) continue;
            if (Volatile.Read(ref _active) is { } act && act.CaseId == caseId) continue;
            await _gate.WaitAsync();
            try
            {
                var load = _journal.Load(caseId);
                if (load.Doc is not { StayAwake: { } stay } doc || !stay.RestorePending)
                {
                    _pendingRestores.TryRemove(caseId, out _);
                    continue;
                }
                await _keeper.RestoreAsync(stay, connected: true);
                lock (_sync) SaveLocked(doc);
                if (!stay.RestorePending) _pendingRestores.TryRemove(caseId, out _);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "No se pudo restaurar «pantalla siempre encendida» del caso {CaseId}", caseId);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>Lo llama la recuperación: un diario con <c>restore_pending</c> se restaura cuando aparece el serial.</summary>
    public void RegisterPendingRestore(string caseId, string serial) => _pendingRestores[caseId] = serial;

    // ── Stop (§7.2.3) ────────────────────────────────────────────────────────

    public async Task<SessionStateDto> StopAsync(string caseIdRaw, string? reason, string? originIdRaw)
    {
        var caseId = CaseEvidenceStore.RequireCaseId(caseIdRaw);
        if (!SessionReasons.IsStopRequestReason(reason))
            throw Invalid("Motivo de corte inválido: tiene que ser cambio_de_paso o detenido_por_perito");
        var originId = EventAttribution.Normalize(originIdRaw);
        await _gate.WaitAsync();
        try
        {
            if (Volatile.Read(ref _active) is { } a && a.CaseId == caseId)
            {
                if (originId is not null) a.OriginId = originId;
                if (a.State == SessionStates.WaitingDevice)
                    await FinishInactiveLockedAsync(a, a.Doc.StopReason ?? SessionReasons.Desconexion, SessionStates.Stopped,
                        cameraReason: reason);
                else
                    await StopLockedAsync(a, reason!, SessionStates.Stopped, closeBudget: null);
                return BuildState(a);
            }
            // Idempotente: sin sesión activa para el caso, el estado del diario (o idle si no hay).
            if (_previous is { } prev && prev.CaseId == caseId) await AwaitQuietly(prev.CloseChain);
            var load = _journal.Load(caseId);
            return load.Doc is { } doc ? StateFromDoc(doc, originId) : SessionStateDto.IdleFor(caseId);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Corta la captura en curso, espera el cierre de los tramos (con presupuesto en el apagado), abre el hueco y deja
    // el estado final. Con el gate tomado.
    private async Task StopLockedAsync(Active a, string reason, string finalState, TimeSpan? closeBudget)
    {
        await a.RunCts.CancelAsync();
        // Primero la cámara (q, rápido) y después la pantalla: el presupuesto del apagado alcanza a las dos (§5.6).
        await StopCameraLockedAsync(a, reason);
        DateTime stoppedAt;
        JournalSegment? last = null;
        if (a.Track is { } track && a.Current is { } seg)
        {
            a.State = SessionStates.Stopping;
            lock (_sync) a.Doc.State = SessionStates.Stopping;
            await EmitStateAsync(a);
            var stop = await track.StopAsync();
            try { track.Dispose(); } catch { /* best effort */ }
            a.Track = null;
            stoppedAt = stop.StopRequestedAt;
            lock (_sync)
            {
                seg.StoppedAt = stoppedAt;
                seg.StopReason = reason;
                seg.State = SegmentStates.Closing;
                seg.Forced = stop.Clean ? null : true;
                seg.Process = null;
                a.Current = null;
                SaveLocked(a.Doc);
            }
            last = seg;
            EnqueueClose(a, seg, lost: null);
        }
        else
        {
            stoppedAt = a.Doc.StoppedAt ?? Now;
        }

        var closed = closeBudget is { } budget
            ? await Task.WhenAny(a.CloseChain, Task.Delay(budget)) == a.CloseChain
            : await AwaitQuietly(a.CloseChain);
        if (!closed) _log.LogWarning("Sesión del caso {CaseId}: el cierre del tramo no terminó a tiempo; lo completa la recuperación", a.CaseId);

        var connected = await _isConnected(a.Serial);
        StayAwakeState? stay;
        lock (_sync) stay = a.Doc.StayAwake;
        await _keeper.RestoreAsync(stay, connected);

        lock (_sync)
        {
            if (!a.Doc.Gaps.Any(g => g.Track == SessionNames.TrackScreen && g.To is null))
            {
                var gap = NewGap(stoppedAt, reason, SessionNames.TrackScreen);
                gap.SegmentLost = last?.State == SegmentStates.Discarded;
                a.Doc.Gaps.Add(gap);
            }
            a.State = finalState;
            a.Doc.State = finalState;
            a.Doc.StopReason = reason;
            a.Doc.StoppedAt = stoppedAt;
            a.Warnings.Clear();
            SaveLocked(a.Doc);
            if (a.Doc.StayAwake?.RestorePending == true) _pendingRestores[a.CaseId] = a.Serial;
        }
        Volatile.Write(ref _active, null);
        _previous = a;
        _log.LogInformation("Sesión del caso {CaseId}: {State} ({Reason})", a.CaseId, finalState, reason);
        await EmitStateAsync(a);
    }

    private static async Task<bool> AwaitQuietly(Task t)
    {
        try
        {
            await t;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── Cierre de tramos (cola secuencial, §5.6) ─────────────────────────────

    /// <summary>Hueco a agregar si el tramo termina descartado (el rango que no quedó en video).</summary>
    private sealed record LostGap(DateTime From, DateTime To, string Reason);

    private void EnqueueClose(Active a, JournalSegment seg, LostGap? lost)
    {
        lock (_sync)
            a.CloseChain = a.CloseChain.ContinueWith(_ => CloseOneAsync(a, seg, lost), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
    }

    private async Task CloseOneAsync(Active a, JournalSegment seg, LostGap? lost)
    {
        try
        {
            SegmentCloseInput input;
            lock (_sync) input = new SegmentCloseInput(seg.Filename, seg.StartedAt, seg.StoppedAt, seg.Forced == true);
            var r = await _closer.CloseAsync(a.CaseId, input);
            SessionSegmentDto dto;
            lock (_sync)
            {
                ApplyClose(seg, r, recovered: false);
                if (r.State == SegmentStates.Discarded && lost is not null)
                {
                    var gap = new SessionGapDto
                    {
                        Id = Guid.NewGuid().ToString("N"), Track = seg.Track, From = lost.From, To = lost.To,
                        Reason = lost.Reason, SegmentLost = true,
                    };
                    a.Doc.Gaps.Add(gap);
                }
                SaveLocked(a.Doc);
                dto = seg.ToDto();
            }
            await EmitAsync("session_segment_closed", new { case_id = a.CaseId, origin_id = a.OriginId, segment = dto });
            if (ReferenceEquals(Volatile.Read(ref _active), a)) await EmitStateAsync(a);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Sesión del caso {CaseId}: no se pudo cerrar el tramo {Name}; queda para la recuperación",
                a.CaseId, seg.Filename);
        }
    }

    internal static void ApplyClose(JournalSegment seg, SegmentCloseResult r, bool recovered)
    {
        seg.State = r.State;
        seg.Filename = r.Filename;
        seg.StoppedAt = r.StoppedAt;
        seg.RecordedSeconds = r.RecordedSeconds;
        seg.DurationSeconds = r.DurationSeconds;
        seg.Validation = r.Validation;
        seg.Size = r.Size;
        seg.Sha256 = r.Sha256;
        seg.Recovered = recovered || seg.Recovered;
        seg.Process = null;
        seg.Forced = null;
    }

    // ── Marcas (§5.9) ────────────────────────────────────────────────────────

    public async Task<SessionMarkDto> AddMarkAsync(string caseIdRaw, string? text, string? originIdRaw)
    {
        var caseId = CaseEvidenceStore.RequireCaseId(caseIdRaw);
        var clean = MarkRules.CleanText(text, out var error);
        if (error is not null) throw Invalid(error);
        var originId = EventAttribution.Normalize(originIdRaw);
        var mark = AddMarkCore(caseId, null, Now, clean, null, MarkKinds.Manual, originId)
            ?? throw new AgentHttpException(409, AgentErrorCodes.SessionNotActive,
                "No hay una grabación de la sesión en curso para este caso.");
        await EmitAsync("session_mark_added", new { case_id = caseId, origin_id = originId, mark });
        return mark;
    }

    /// <summary>
    /// Marca automática de una captura (§5.9, D-T15): solo si la sesión activa es de ese celular y de ese caso.
    /// <paramref name="at"/> = antes de llamar a screencap. null si no hubo marca (todo sigue como hoy).
    /// </summary>
    public async Task<SessionMarkDto?> TryAddCaptureMarkAsync(string serial, string? caseId, DateTime at, string filename,
        string? originId)
    {
        if (caseId is null) return null;
        var mark = AddMarkCore(caseId, serial, at, null, filename, MarkKinds.Captura, originId);
        if (mark is not null)
            await EmitAsync("session_mark_added", new { case_id = caseId, origin_id = originId, mark });
        return mark;
    }

    private SessionMarkDto? AddMarkCore(string caseId, string? serial, DateTime at, string? text, string? filename,
        string kind, string? originId)
    {
        var a = Volatile.Read(ref _active);
        if (a is null || a.CaseId != caseId || (serial is not null && a.Serial != serial)) return null;
        lock (_sync)
        {
            // [DU1=A] Con el cable desconectado (waiting_device) y la cámara grabando, la marca se ubica solo en la cámara.
            var screenOn = a.State is SessionStates.Recording or SessionStates.Rotating;
            if (!screenOn && !(a.State == SessionStates.WaitingDevice && a.CameraState == CameraStates.Recording))
                return null;
            int? index = null;
            double? offset = null;
            if (screenOn) (index, offset) = MarkRules.Locate(a.Doc.Segments, at, Now);
            var camera = MarkRules.Locate(a.Doc.Segments, at, Now, SessionNames.TrackCamera);
            var mark = new SessionMarkDto
            {
                Id = Guid.NewGuid().ToString("N"),
                At = at,
                SegmentIndex = index,
                OffsetSeconds = offset,
                CameraSegmentIndex = camera?.Index,
                CameraOffsetSeconds = camera?.OffsetSeconds,
                Text = text,
                Filename = filename,
                Kind = kind,
            };
            a.Doc.Marks.Add(mark);
            if (originId is not null) a.OriginId = originId;
            SaveLocked(a.Doc);
            return SessionJournalDoc.Clone(mark);
        }
    }

    // ── Cámara del procedimiento (grabacion-sesion-completa-f2 §5.4–§5.7) ─────

    private const int MaxCameraName = 200;
    private const int MaxCameraId = 1024;

    private static AgentHttpException CameraUnsupported() =>
        new(400, AgentErrorCodes.SessionCameraUnsupported, "La cámara del procedimiento no está habilitada en este Tatana.");

    private static AgentHttpException NotActive() =>
        new(409, AgentErrorCodes.SessionNotActive, "No hay una grabación de la sesión en curso para este caso.");

    /// <summary>Cámara del body (start de la sesión o <c>camera/start</c>). null si no vino. 400 si no es válida.</summary>
    internal static CameraSpec? ParseCamera(StartCameraRequest? c)
    {
        if (c is null) return null;
        var name = c.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxCameraName || name.Any(char.IsControl) ||
            AgentFileNames.HasUnsafeControls(name))
            throw Invalid("Falta el nombre de la cámara o no es válido");
        var id = string.IsNullOrWhiteSpace(c.Id) ? null : c.Id.Trim();
        if (id is not null && (id.Length > MaxCameraId || id.Any(char.IsControl)))
            throw Invalid("El id de la cámara no es válido");
        return new CameraSpec(id ?? name, name, c.WithAudio, "");
    }

    private Active? ActiveForCamera(string caseId) =>
        Volatile.Read(ref _active) is { } a && a.CaseId == caseId &&
        a.State is SessionStates.Recording or SessionStates.Rotating or SessionStates.WaitingDevice
            ? a
            : null;

    /// <summary><c>POST /cases/{caseId}/session/camera/start</c> (§7.2.5).</summary>
    public async Task<SessionStateDto> StartCameraAsync(string caseIdRaw, StartCameraRequest? req)
    {
        var caseId = CaseEvidenceStore.RequireCaseId(caseIdRaw);
        if (!CameraEnabled) throw CameraUnsupported();
        var spec = ParseCamera(req ?? new StartCameraRequest()) ?? throw Invalid("Falta la cámara");
        var originId = EventAttribution.Normalize(req?.OriginId);
        await _gate.WaitAsync();
        try
        {
            var a = ActiveForCamera(caseId) ?? throw NotActive();
            if (originId is not null) a.OriginId = originId;
            if (a.Camera is not null)
            {
                if (a.CameraSpec?.Id == spec.Id) return BuildState(a);
                throw new AgentHttpException(409, AgentErrorCodes.CameraBusy,
                    "La cámara del procedimiento está grabando. Detenela para elegir otra.");
            }
            await StartCameraLockedAsync(a, spec, explicitRequest: true);
            return BuildState(a);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary><c>POST /cases/{caseId}/session/camera/retry</c>: con la última cámara pedida en esta sesión.</summary>
    public async Task<SessionStateDto> RetryCameraAsync(string caseIdRaw, string? originIdRaw)
    {
        var caseId = CaseEvidenceStore.RequireCaseId(caseIdRaw);
        if (!CameraEnabled) throw CameraUnsupported();
        var originId = EventAttribution.Normalize(originIdRaw);
        await _gate.WaitAsync();
        try
        {
            var a = ActiveForCamera(caseId) ?? throw NotActive();
            if (originId is not null) a.OriginId = originId;
            if (a.Camera is not null) return BuildState(a);
            var spec = a.CameraSpec ?? throw NotActive();
            await StartCameraLockedAsync(a, spec, explicitRequest: true);
            return BuildState(a);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary><c>POST /cases/{caseId}/session/camera/stop</c> (D3 = A): la pantalla sigue.</summary>
    public async Task<SessionStateDto> StopCameraAsync(string caseIdRaw, string? originIdRaw)
    {
        var caseId = CaseEvidenceStore.RequireCaseId(caseIdRaw);
        if (!CameraEnabled) throw CameraUnsupported();
        var originId = EventAttribution.Normalize(originIdRaw);
        await _gate.WaitAsync();
        try
        {
            var a = ActiveForCamera(caseId) ?? throw NotActive();
            if (originId is not null) a.OriginId = originId;
            await StopCameraLockedAsync(a, SessionReasons.DetenidoPorPerito);
            a.CameraSpec = null;
            _log.LogInformation("Sesión del caso {CaseId}: el perito quitó la cámara", a.CaseId);
            await EmitStateAsync(a);
            return BuildState(a);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>true si la cámara de la sesión está grabando (la vista previa no puede abrirla).</summary>
    public bool IsCameraRecording => Volatile.Read(ref _active) is { CameraState: CameraStates.Recording };

    /// <summary>Imagen de la vista previa que escribe la grabación de la cámara en curso (§5.9), o null.</summary>
    public string? SessionCameraPreviewPath =>
        Volatile.Read(ref _active) is { CameraState: CameraStates.Recording } a
            ? Path.Combine(_journal.DirFor(a.CaseId), CameraArgs.PreviewFileName)
            : null;

    /// <summary>
    /// Arranca el tramo continuo de cámara (§5.4). Con el gate tomado. Una cámara que no abre no es error: queda
    /// <c>failed</c> con el aviso. Sin espacio para las dos pistas: 507 si lo pidió el perito; en el start de la sesión,
    /// la pantalla sigue sola con el aviso <c>disk_low</c> (D-T9).
    /// </summary>
    private async Task StartCameraLockedAsync(Active a, CameraSpec spec, bool explicitRequest)
    {
        if (a.Camera is not null || _cameras is null) return;
        var available = AvailableBytes();
        var rate = ScreenRate(a.Doc) + CameraRate(a.Doc);
        var minutes = Math.Max(1, a.Doc.SegmentMinutes);
        if (!SessionDiskBudget.CanStart(available, _minFree, minutes, rate))
        {
            var required = SessionDiskBudget.RequiredToStart(_minFree, minutes, rate);
            if (explicitRequest)
                throw new AgentHttpException(507, AgentErrorCodes.InsufficientStorage,
                    $"No hay espacio en el disco de esta PC para grabar también la cámara (quedan {CaseEvidenceStore.FormatBytes(available ?? 0)})",
                    new Dictionary<string, object?> { ["available_bytes"] = available, ["required_bytes"] = required });
            _log.LogWarning("Sesión del caso {CaseId}: sin espacio para sumar la cámara; la pantalla graba sola", a.CaseId);
            lock (_sync)
                a.Warnings[SessionWarningKinds.DiskLow] = new SessionWarningDto(SessionWarningKinds.DiskLow, Now, available,
                    SessionDiskBudget.MinutesLeft(available, _minFree, rate));
            await EmitStateAsync(a);
            return;
        }

        // Windows (dshow) no comparte la cámara: la vista previa se corta antes.
        if (_preview is not null)
        {
            try { await _preview.StopAsync(); }
            catch (Exception ex) { _log.LogDebug(ex, "No se pudo cortar la vista previa de la cámara"); }
        }

        var dir = _journal.DirFor(a.CaseId);
        Directory.CreateDirectory(dir);
        int start;
        lock (_sync)
        {
            // El CSV de un tramo continuo anterior ya conciliado es un archivo propio.
            if (a.Doc.CameraRun is null) AdbService.TryDelete(Path.Combine(dir, CameraArgs.CsvFileName));
            start = NextIndex(a.Doc, SessionNames.TrackCamera);
        }
        // Un part_file que no está en el diario no se pisa (regla dura de datos): se saltea el número.
        while (start < SessionNames.MaxIndex && File.Exists(Path.Combine(dir, CameraArgs.PartName(start))))
        {
            _log.LogWarning("Cámara: {Part} ya existe y no está en el diario; no se toca", CameraArgs.PartName(start));
            start++;
        }
        var segSeconds = (int)Math.Max(1, SegmentDuration(a).TotalSeconds);

        var track = _cameras.Create(spec);
        track.SegmentClosed += ev => OnCameraSegmentClosed(a, track, ev);
        track.Exited += exit => _ = Task.Run(() => OnCameraExitedAsync(a, track, exit));
        CameraStart started;
        try
        {
            started = await track.StartAsync(dir, start, segSeconds);
        }
        catch (Exception ex)
        {
            try { track.Dispose(); } catch { /* best effort */ }
            var detail = (ex as CameraStartException)?.Detail;
            _log.LogWarning("Sesión del caso {CaseId}: no se pudo empezar a grabar la cámara «{Name}» ({Detail}): {Error}",
                a.CaseId, spec.Name, detail ?? "sin detalle", ex.Message);
            lock (_sync)
            {
                a.CameraState = CameraStates.Failed;
                a.CameraSpec = spec;
                a.CameraStarted = null;
                a.Warnings[SessionWarningKinds.CameraFailed] =
                    new SessionWarningDto(SessionWarningKinds.CameraFailed, Now, Detail: detail);
            }
            await EmitStateAsync(a);
            return;
        }

        lock (_sync)
        {
            var run = new CameraRunState
            {
                Id = spec.Id, Name = spec.Name, WithAudio = spec.WithAudio, Width = started.Width,
                Height = started.Height, Fps = started.Fps, Fallback = started.Fallback, AnchorAt = started.AnchorAt,
                StartNumber = start, Pid = started.Pid, ProcessStartedAt = started.ProcessStartedAt, CsvLinesDone = 0,
            };
            var camera = new SessionCameraDto(spec.Name, started.Width, started.Height, started.Fps, spec.WithAudio);
            var seg = CameraJournal.NewSegment(start, started.AnchorAt, camera, run, TimeZoneInfo.Local);
            a.Doc.CameraRun = run;
            a.Doc.Segments.Add(seg);
            a.Doc.Camera = camera;
            // D3 = A: cierra el hueco de cámara si lo había; si no (primera vez), no se crea ninguno.
            GapRules.CloseOpenGap(a.Doc.Gaps, SessionNames.TrackCamera, started.AnchorAt, TimeZoneInfo.Local);
            a.Camera = track;
            a.CameraCurrent = seg;
            a.CameraState = CameraStates.Recording;
            a.CameraSpec = spec;
            a.CameraStarted = started;
            a.CameraStopReason = null;
            a.Warnings.Remove(SessionWarningKinds.CameraFailed);
            SaveLocked(a.Doc);
        }
        a.CameraCts = new CancellationTokenSource();
        var ct = a.CameraCts.Token;
        _ = Task.Run(() => TickLoopAsync(a, TimeSpan.FromSeconds(_opt.DiskCheckSeconds), CameraDiskTickAsync, ct));
        _log.LogInformation("Sesión del caso {CaseId}: cámara «{Name}» grabando, tramo {Index} ({W}x{H} a {Fps} fps)",
            a.CaseId, spec.Name, start, started.Width, started.Height, started.Fps);
        await EmitStateAsync(a);
    }

    /// <summary>
    /// Línea nueva del CSV (§5.4): cierra el tramo, abre el siguiente si ffmpeg sigue, renombra el <c>part_file</c> y lo
    /// manda a la cola de cierre. Corre en el hilo de la pista y solo toma el lock del diario (no el gate: el cierre pedido
    /// espera la última línea con el gate tomado).
    /// </summary>
    private void OnCameraSegmentClosed(Active a, ICameraTrack track, CameraSegmentClosed ev)
    {
        JournalSegment closed;
        lock (_sync)
        {
            if (!ReferenceEquals(a.Camera, track) && a.Camera is not null) return;
            if (a.Doc.CameraRun is not { } run) return;
            var (seg, next) = CameraJournal.ApplyLine(a.Doc, run,
                new CameraCsvLine(ev.Index, Path.GetFileName(ev.PartPath), ev.StartSeconds, ev.EndSeconds), ev.Final,
                a.CameraStopReason ?? SessionReasons.CamaraFallo, recovered: false, TimeZoneInfo.Local);
            a.CameraCurrent = next;
            CameraJournal.RenamePart(_journal.DirFor(a.CaseId), seg, _log);
            SaveLocked(a.Doc);
            closed = seg;
        }
        EnqueueClose(a, closed, ev.Final
            ? null
            : new LostGap(closed.StartedAt, closed.StoppedAt ?? closed.StartedAt, SessionReasons.CamaraFallo));
        _log.LogInformation("Sesión del caso {CaseId}: tramo de cámara {Index} cerrado{Final}", a.CaseId, ev.Index,
            ev.Final ? " (último)" : "");
        _ = EmitStateAsync(a);
    }

    /// <summary>
    /// Corta la cámara con el motivo pedido (§5.6): la última línea del CSV cierra el último tramo; si hubo que matar a
    /// ffmpeg, el tramo abierto queda <c>forced</c> (remux). Abre el hueco <c>camara</c> si no hay uno abierto. Con el gate
    /// tomado. Sin cámara grabando: si estaba <c>failed</c>, pasa a <c>off</c> y el hueco sigue tal cual.
    /// </summary>
    private async Task StopCameraLockedAsync(Active a, string reason)
    {
        try { a.CameraCts?.Cancel(); } catch { /* best effort */ }
        a.CameraCts = null;
        if (a.Camera is not { } track)
        {
            lock (_sync)
            {
                if (a.CameraState == CameraStates.Failed) a.CameraState = CameraStates.Off;
                a.CameraStarted = null;
                a.Warnings.Remove(SessionWarningKinds.CameraFailed);
            }
            return;
        }
        a.CameraStopReason = reason;
        var stop = await track.StopAsync();
        JournalSegment? forced = null;
        var dir = _journal.DirFor(a.CaseId);
        lock (_sync)
        {
            if (a.CameraCurrent is { } seg)
            {
                seg.StoppedAt = stop.StopRequestedAt;
                seg.StopReason = reason;
                seg.State = SegmentStates.Closing;
                seg.Forced = true;
                seg.Process = null;
                CameraJournal.RenamePart(dir, seg, _log);
                forced = seg;
                a.CameraCurrent = null;
            }
            var stoppedAt = LastCameraStop(a.Doc) ?? stop.StopRequestedAt;
            a.Doc.CameraRun = null;
            AdbService.TryDelete(Path.Combine(dir, CameraArgs.CsvFileName));
            AdbService.TryDelete(Path.Combine(dir, CameraArgs.PreviewFileName));
            if (!a.Doc.Gaps.Any(g => g.Track == SessionNames.TrackCamera && g.To is null))
                a.Doc.Gaps.Add(NewGap(stoppedAt, reason, SessionNames.TrackCamera));
            a.Camera = null;
            a.CameraState = CameraStates.Off;
            a.CameraStarted = null;
            a.CameraStopReason = null;
            a.Warnings.Remove(SessionWarningKinds.CameraFailed);
            SaveLocked(a.Doc);
        }
        if (forced is not null) EnqueueClose(a, forced, lost: null);
        try { track.Dispose(); } catch { /* best effort */ }
        _log.LogInformation("Sesión del caso {CaseId}: cámara detenida ({Reason}{Clean})", a.CaseId, reason,
            stop.Clean ? "" : ", forzada");
    }

    private static DateTime? LastCameraStop(SessionJournalDoc doc) =>
        doc.Segments.Where(s => s.Track == SessionNames.TrackCamera).OrderBy(s => s.Index).LastOrDefault()?.StoppedAt;

    /// <summary>
    /// ffmpeg de la cámara terminó sin que se lo pidieran (§5.4 "fin no pedido"): la pantalla no se toca. Hueco
    /// <c>camara_fallo</c>, aviso <c>camera_failed</c> y <c>camera_state = failed</c>; sin reintento automático (D-T7).
    /// </summary>
    private async Task OnCameraExitedAsync(Active a, ICameraTrack track, CameraExit exit)
    {
        await _gate.WaitAsync();
        try
        {
            if (!ReferenceEquals(Volatile.Read(ref _active), a) || !ReferenceEquals(a.Camera, track)) return;
            try { a.CameraCts?.Cancel(); } catch { /* best effort */ }
            a.CameraCts = null;
            JournalSegment? forced = null;
            var dir = _journal.DirFor(a.CaseId);
            lock (_sync)
            {
                if (a.CameraCurrent is { } seg)
                {
                    seg.StoppedAt = exit.At;
                    seg.StopReason = SessionReasons.CamaraFallo;
                    seg.State = SegmentStates.Closing;
                    seg.Forced = true;
                    seg.Process = null;
                    CameraJournal.RenamePart(dir, seg, _log);
                    forced = seg;
                    a.CameraCurrent = null;
                }
                var from = LastCameraStop(a.Doc) ?? exit.At;
                a.Doc.CameraRun = null;
                AdbService.TryDelete(Path.Combine(dir, CameraArgs.CsvFileName));
                AdbService.TryDelete(Path.Combine(dir, CameraArgs.PreviewFileName));
                if (!a.Doc.Gaps.Any(g => g.Track == SessionNames.TrackCamera && g.To is null))
                    a.Doc.Gaps.Add(NewGap(from, SessionReasons.CamaraFallo, SessionNames.TrackCamera));
                a.Camera = null;
                a.CameraState = CameraStates.Failed;
                a.CameraStarted = null;
                a.Warnings[SessionWarningKinds.CameraFailed] =
                    new SessionWarningDto(SessionWarningKinds.CameraFailed, Now, Detail: exit.Detail);
                SaveLocked(a.Doc);
            }
            if (forced is not null) EnqueueClose(a, forced, lost: null);
            try { track.Dispose(); } catch { /* best effort */ }
            _log.LogWarning("Sesión del caso {CaseId}: la cámara dejó de grabar ({Detail}); la pantalla sigue", a.CaseId,
                exit.Detail ?? "sin detalle");
            await EmitStateAsync(a);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Sesión: error manejando la salida de la cámara");
        }
        finally
        {
            _gate.Release();
        }
    }

    // ── Lectura ──────────────────────────────────────────────────────────────

    /// <summary><c>GET /cases/{caseId}/session</c>: el diario sin lo interno. null si no hay diario.</summary>
    public CaseSessionDto? GetJournal(string caseIdRaw)
    {
        var caseId = CaseEvidenceStore.RequireCaseId(caseIdRaw);
        if (Volatile.Read(ref _active) is { } a && a.CaseId == caseId)
            lock (_sync) return a.Doc.ToDto();
        var load = _journal.Load(caseId);
        if (load.Unreadable)
            throw new AgentHttpException(500, AgentErrorCodes.StorageError,
                "El diario de la grabación de la sesión de este caso no se puede leer.");
        return load.Doc?.ToDto();
    }

    /// <summary><c>GET /session/capacity</c> (§7.2.1).</summary>
    public SessionCapacityDto Capacity(bool camera, int? segmentMinutes)
    {
        var available = AvailableBytes();
        var doc = Volatile.Read(ref _active)?.Doc;
        var rate = ScreenRate(doc) + (camera ? CameraRate(doc) : 0);
        var minutes = Math.Clamp(segmentMinutes ?? 30, _opt.MinSegmentMinutes, _opt.MaxSegmentMinutes);
        var same = SessionDiskBudget.SameVolume(_dataDir, _evidenceDir);
        return new SessionCapacityDto(available, _minFree, rate, same,
            SessionDiskBudget.EstimatedMinutes(available, _minFree, rate, same),
            SessionDiskBudget.RequiredToStart(_minFree, minutes, rate),
            SessionDiskBudget.CanStart(available, _minFree, minutes, rate));
    }

    private long? AvailableBytes()
    {
        if (_mock && _opt.MockFreeBytes is { } fake) return fake;
        return _disk.GetAvailableFreeBytes(_dataDir);
    }

    private long ScreenRate(SessionJournalDoc? doc)
    {
        double? observed = null;
        if (doc is not null)
            lock (_sync)
            {
                var last = doc.Segments.LastOrDefault(s => s.Track == SessionNames.TrackScreen && s.State == SegmentStates.Saved);
                observed = SessionDiskBudget.ObservedPerMinute(last?.Size, last?.DurationSeconds ?? last?.RecordedSeconds);
            }
        return SessionDiskBudget.Rate(_opt.ScreenBytesPerMinute, observed);
    }

    /// <summary>F2 (§5.6): max(CameraBytesPerMinute, tasa observada del último tramo de cámara guardado).</summary>
    private long CameraRate(SessionJournalDoc? doc)
    {
        double? observed = null;
        if (doc is not null)
            lock (_sync)
            {
                var last = doc.Segments.LastOrDefault(s => s.Track == SessionNames.TrackCamera && s.State == SegmentStates.Saved);
                observed = SessionDiskBudget.ObservedPerMinute(last?.Size, last?.DurationSeconds ?? last?.RecordedSeconds);
            }
        return SessionDiskBudget.Rate(_opt.CameraBytesPerMinute, observed);
    }

    /// <summary>R = pantalla + cámara si la cámara graba (§5.6).</summary>
    private long TotalRate(Active a) =>
        ScreenRate(a.Doc) + (a.CameraState == CameraStates.Recording ? CameraRate(a.Doc) : 0);

    private SessionStateDto BuildState(Active a)
    {
        lock (_sync)
        {
            var dto = StateFromDoc(a.Doc, a.OriginId);
            var current = a.Current;
            return dto with
            {
                State = a.State,
                Serial = a.Serial,
                Resumed = a.Resumed,
                RunStartedAt = SessionStates.IsActive(a.State) ? a.RunStartedAt : null,
                ScreenSegmentIndex = current?.Index,
                ScreenSegmentStartedAt = current?.StartedAt,
                StopReason = SessionStates.HasStopReason(a.State) ? a.Doc.StopReason : null,
                StoppedAt = SessionStates.HasStopReason(a.State) ? a.Doc.StoppedAt : null,
                Warnings = a.Warnings.Values.OrderBy(w => w.Since).ToList(),
                CameraState = a.CameraState,
                CameraSegmentIndex = a.CameraCurrent?.Index,
                CameraSegmentStartedAt = a.CameraCurrent?.StartedAt,
                Camera = a.CameraState != CameraStates.Off && a.CameraSpec is { } spec
                    ? new SessionActiveCameraDto(spec.Id, spec.Name, a.CameraStarted?.Width, a.CameraStarted?.Height,
                        a.CameraStarted?.Fps, spec.WithAudio)
                    : null,
            };
        }
    }

    internal static SessionStateDto StateFromDoc(SessionJournalDoc doc, string? originId)
    {
        var screen = doc.Segments.Where(s => s.Track == SessionNames.TrackScreen).ToList();
        var saved = doc.Segments.Where(s => s.State == SegmentStates.Saved).ToList();
        var hasStop = SessionStates.HasStopReason(doc.State);
        return new SessionStateDto(
            doc.CaseId, doc.Serial, doc.Platform, doc.State, screen.Count > 0, doc.SegmentMinutes,
            screen.Count > 0 ? screen.Min(s => s.StartedAt) : null,
            null, null, null, "off", null, null,
            saved.Count,
            Math.Round(saved.Where(s => s.Track == SessionNames.TrackScreen).Sum(s => s.RecordedSeconds ?? 0), 1,
                MidpointRounding.AwayFromZero),
            hasStop ? doc.StopReason : null,
            hasStop ? doc.StoppedAt : null,
            [], originId);
    }

    // ── Apagado ordenado (§5.11) ─────────────────────────────────────────────

    /// <summary>
    /// Corta la captura, guarda el tramo y deja el diario en <c>interrupted</c> (hueco <c>tatana_cerrado</c>). Lo que no
    /// entra en el presupuesto queda <c>closing</c> y lo termina la recuperación. Lo llaman
    /// <c>POST /agent/prepare-shutdown</c> y <c>ApplicationStopping</c>.
    /// </summary>
    public async Task<PrepareShutdownResponse> ShutdownAsync(TimeSpan budget)
    {
        if (_preview is not null)
        {
            try { await _preview.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception ex) { _log.LogDebug(ex, "No se pudo cortar la vista previa de la cámara"); }
        }
        var a = Volatile.Read(ref _active);
        if (a is null) return new PrepareShutdownResponse(false, 0, 0);
        var deadline = Now + budget;
        if (!await _gate.WaitAsync(budget)) return Counts(a, hadSession: true);
        try
        {
            if (!ReferenceEquals(Volatile.Read(ref _active), a)) return Counts(a, hadSession: true);
            if (a.State == SessionStates.WaitingDevice)
            {
                // El hueco de la desconexión sigue abierto tal cual; la recuperación lo deja en interrupted. F2: la cámara
                // sí graba en waiting_device (D2 = A) y se corta con tatana_cerrado dentro del mismo presupuesto.
                await a.RunCts.CancelAsync();
                await StopCameraLockedAsync(a, SessionReasons.TatanaCerrado);
                var left = deadline - Now;
                if (left > TimeSpan.Zero) await Task.WhenAny(a.CloseChain, Task.Delay(left));
                lock (_sync) SaveLocked(a.Doc);
                Volatile.Write(ref _active, null);
                _previous = a;
                return Counts(a, hadSession: true);
            }
            var remaining = deadline - Now;
            await StopLockedAsync(a, SessionReasons.TatanaCerrado, SessionStates.Interrupted,
                closeBudget: remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            return Counts(a, hadSession: true);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Sesión: error en el cierre ordenado");
            return Counts(a, hadSession: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private PrepareShutdownResponse Counts(Active a, bool hadSession)
    {
        lock (_sync)
            return new PrepareShutdownResponse(hadSession,
                a.Doc.Segments.Count(s => s.State == SegmentStates.Saved),
                a.Doc.Segments.Count(s => SegmentStates.IsOpen(s.State)));
    }

    // ── Auxiliares ───────────────────────────────────────────────────────────

    private static SessionGapDto NewGap(DateTime from, string reason, string track) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Track = track,
        From = from,
        Reason = reason,
    };

    private void SaveLocked(SessionJournalDoc doc)
    {
        try { _journal.Save(doc, Now); }
        catch (Exception ex) { _log.LogError(ex, "No se pudo escribir el diario de la sesión del caso {CaseId}", doc.CaseId); }
    }

    private Task EmitStateAsync(Active a) => EmitAsync("session_state", BuildState(a));

    private async Task EmitAsync(string type, object data)
    {
        try { await _events.BroadcastAsync(new AgentEvent { Type = type, Data = data }, CancellationToken.None); }
        catch (Exception ex) { _log.LogDebug(ex, "No se pudo emitir {Type}", type); }
    }

    private static AgentHttpException Invalid(string message) =>
        new(400, AgentErrorCodes.InvalidRequest, message);

    private async Task<bool> DefaultIsConnectedAsync(string serial)
    {
        if (_mock) return true;
        try
        {
            var r = await ProcessRunner.RunArgumentListAsync(AdbPath.Value, ["-s", serial, "get-state"],
                CancellationToken.None, timeout: GetStateTimeout);
            return r.Success && r.Stdout.Trim() == "device";
        }
        catch
        {
            return _lastSerials.Contains(serial);
        }
    }

    public void Dispose()
    {
        var a = Volatile.Read(ref _active);
        try { a?.RunCts.Cancel(); } catch { /* best effort */ }
        try { a?.CameraCts?.Cancel(); } catch { /* best effort */ }
        _gate.Dispose();
    }
}

/// <summary>Reglas puras de los huecos (§5.4 paso 6).</summary>
public static class GapRules
{
    /// <summary>
    /// Cierra el hueco abierto de la pista con <paramref name="to"/>. Un <c>cambio_de_paso</c> cuyo inicio y fin caen en
    /// días distintos (fecha local de la PC) pasa a <c>retomado_otro_dia</c>. Devuelve el hueco cerrado o null.
    /// </summary>
    public static SessionGapDto? CloseOpenGap(List<SessionGapDto> gaps, string track, DateTime to, TimeZoneInfo zone)
    {
        var open = gaps.LastOrDefault(g => g.Track == track && g.To is null);
        if (open is null) return null;
        open.To = to < open.From ? open.From : to;
        if (open.Reason == SessionReasons.CambioDePaso && LocalDate(open.From, zone) != LocalDate(open.To.Value, zone))
            open.Reason = SessionReasons.RetomadoOtroDia;
        return open;
    }

    private static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));
}

/// <summary>Reglas puras de las marcas (§5.9).</summary>
public static class MarkRules
{
    /// <summary>
    /// Texto de una marca: <c>TrimEnd</c>, hasta 300 caracteres, sin caracteres de control. Vacío → null. Si no cumple,
    /// <paramref name="error"/> trae el motivo.
    /// </summary>
    public static string? CleanText(string? text, out string? error)
    {
        error = null;
        if (text is null) return null;
        var t = text.TrimEnd();
        if (t.Length == 0) return null;
        if (t.Length > SessionRecorder.MaxMarkText)
        {
            error = $"El texto de la marca no puede superar los {SessionRecorder.MaxMarkText} caracteres";
            return null;
        }
        if (t.Any(char.IsControl) || AgentFileNames.HasUnsafeControls(t))
        {
            error = "El texto de la marca no puede tener caracteres de control";
            return null;
        }
        return t;
    }

    /// <summary>
    /// Tramo de pantalla cuyo intervalo <c>[started_at, stopped_at ?? ahora)</c> contiene <paramref name="at"/>; si hay
    /// dos (solapamiento), el más nuevo. Sin ninguno, el último que empezó antes (o el primero). Devuelve el índice y el
    /// segundo del video, redondeado a 0,1.
    /// </summary>
    public static (int Index, double OffsetSeconds) Locate(IEnumerable<JournalSegment> segments, DateTime at, DateTime now)
    {
        var screen = segments.Where(s => s.Track == SessionNames.TrackScreen && s.State != SegmentStates.Discarded)
            .OrderBy(s => s.Index).ToList();
        if (screen.Count == 0) return (1, 0);
        var match = screen.LastOrDefault(s => s.StartedAt <= at && at < (s.StoppedAt ?? now.AddMilliseconds(1)))
                    ?? screen.LastOrDefault(s => s.StartedAt <= at)
                    ?? screen[0];
        var offset = Math.Max(0, (at - match.StartedAt).TotalSeconds);
        return (match.Index, Math.Round(offset, 1, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// Ubicación por pista (F2, §5.7). Pantalla: como <see cref="Locate(IEnumerable{JournalSegment}, DateTime, DateTime)"/>
    /// (null sin tramos). Cámara, estricta (D-T5): el tramo no descartado cuyo <c>[started_at, stopped_at ?? ahora)</c>
    /// contiene <paramref name="at"/>, sin respaldo; null si ninguno.
    /// </summary>
    public static (int Index, double OffsetSeconds)? Locate(IEnumerable<JournalSegment> segments, DateTime at,
        DateTime now, string track)
    {
        var list = segments.Where(s => s.Track == track && s.State != SegmentStates.Discarded).OrderBy(s => s.Index).ToList();
        if (track != SessionNames.TrackCamera) return list.Count == 0 ? null : Locate(list, at, now);
        var match = list.LastOrDefault(s => s.StartedAt <= at && at < (s.StoppedAt ?? now.AddMilliseconds(1)));
        if (match is null) return null;
        var offset = Math.Max(0, (at - match.StartedAt).TotalSeconds);
        return (match.Index, Math.Round(offset, 1, MidpointRounding.AwayFromZero));
    }
}
