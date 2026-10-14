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
        TimeProvider time, ILogger<SessionRecorder> log)
        : this(opts.Value, journal, closer, keeper, tracks, screen, disk, events, pointRecordings, recovery, time, log,
            isConnected: null)
    {
    }

    internal SessionRecorder(AgentOptions opts, SessionJournal journal, SegmentCloser closer, StayAwakeKeeper keeper,
        ISessionTrackFactory tracks, IAndroidScreenProbe screen, IAgentDiskProbe disk, IAgentEventSink events,
        IEnumerable<IRecordingStateSource> pointRecordings, SessionRecoveryStatus recovery, TimeProvider time,
        ILogger? log, Func<string, Task<bool>>? isConnected)
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
    }

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
        if (a.State is not (SessionStates.Recording or SessionStates.Rotating)) return;

        var available = AvailableBytes();
        var rate = ScreenRate(a.Doc);
        if (SessionDiskBudget.ShouldCut(available, _minFree, rate, _opt.CutReserveMinutes))
        {
            _log.LogWarning("Sesión del caso {CaseId}: poco espacio en el disco ({Available} bytes); se corta la grabación",
                a.CaseId, available);
            await _gate.WaitAsync();
            try
            {
                if (ReferenceEquals(Volatile.Read(ref _active), a) && a.State is SessionStates.Recording or SessionStates.Rotating)
                    await StopLockedAsync(a, SessionReasons.SinEspacio, SessionStates.Stopped, closeBudget: null);
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
                a.Doc.Gaps.Add(NewGap(exit.At, reason));
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
    private async Task FinishInactiveLockedAsync(Active a, string reason, string finalState)
    {
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
                    await FinishInactiveLockedAsync(a, a.Doc.StopReason ?? SessionReasons.Desconexion, SessionStates.Stopped);
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
                var gap = NewGap(stoppedAt, reason);
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
            if (a.State is not (SessionStates.Recording or SessionStates.Rotating)) return null;
            var (index, offset) = MarkRules.Locate(a.Doc.Segments, at, Now);
            var mark = new SessionMarkDto
            {
                Id = Guid.NewGuid().ToString("N"),
                At = at,
                SegmentIndex = index,
                OffsetSeconds = offset,
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
        var rate = ScreenRate(Volatile.Read(ref _active)?.Doc) + (camera ? _opt.CameraBytesPerMinute : 0);
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
        var a = Volatile.Read(ref _active);
        if (a is null) return new PrepareShutdownResponse(false, 0, 0);
        var deadline = Now + budget;
        if (!await _gate.WaitAsync(budget)) return Counts(a, hadSession: true);
        try
        {
            if (!ReferenceEquals(Volatile.Read(ref _active), a)) return Counts(a, hadSession: true);
            if (a.State == SessionStates.WaitingDevice)
            {
                // El hueco de la desconexión sigue abierto tal cual; la recuperación lo deja en interrupted.
                await a.RunCts.CancelAsync();
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

    private SessionGapDto NewGap(DateTime from, string reason) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Track = SessionNames.TrackScreen,
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
}
