using System.Diagnostics;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.WebSockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services.Session;

/// <summary>
/// Recuperación al arrancar (grabacion-sesion-completa §5.10). Por cada <c>sessions/*/sesion.json</c>:
/// <list type="number">
/// <item>JSON ilegible → se renombra (<c>.ilegible-…</c>) y no se toca ningún MKV.</item>
/// <item>Tramos <c>recording</c>/<c>closing</c> con proceso: si el PID sigue vivo y es el mismo proceso (nombre y hora
/// de inicio ±2 s), se cierra limpio (SIGINT o Ctrl+C): el tramo queda completo (§4.2.6). Si no, no se toca.</item>
/// <item>Cada tramo abierto se valida, se remuxea si hace falta, se mueve y se hashea (<c>recovered: true</c>,
/// <c>stop_reason: tatana_cerrado</c>).</item>
/// <item>Hueco <c>tatana_cerrado</c> (salvo que hubiera uno de desconexión abierto) y estado <c>interrupted</c>: la
/// sesión no se reanuda sola (D7).</item>
/// <item>"Pantalla siempre encendida": se restaura si el celular está; si no, queda pendiente.</item>
/// </list>
/// Corre en segundo plano (<c>Task.Run</c>); mientras tanto <c>/agent/state.session_recovery = "running"</c>.
/// </summary>
public sealed class SessionRecovery
{
    internal static readonly TimeSpan ProcessStartTolerance = TimeSpan.FromSeconds(2);
    private static readonly Lazy<string> AdbPath = new(() => ToolResolver.Find(AgentTools.Adb)?.Path ?? "adb");

    private readonly SessionJournal _journal;
    private readonly SegmentCloser _closer;
    private readonly StayAwakeKeeper _keeper;
    private readonly SessionRecorder _recorder;
    private readonly SessionRecoveryStatus _status;
    private readonly IAgentEventSink _events;
    private readonly RecordingOptions _rec;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly bool _mock;
    private readonly Func<string, Task<bool>> _isConnected;
    private readonly Func<SegmentProcess, string[], Process?> _findProcess;

    public SessionRecovery(SessionJournal journal, SegmentCloser closer, StayAwakeKeeper keeper, SessionRecorder recorder,
        SessionRecoveryStatus status, IAgentEventSink events, IOptions<AgentOptions> opts, TimeProvider time,
        ILogger<SessionRecovery> log)
        : this(journal, closer, keeper, recorder, status, events,
            (opts.Value.Recording ?? new RecordingOptions()).Clone().Normalize(null), time, log, opts.Value.Mock,
            isConnected: null, findProcess: null)
    {
    }

    internal SessionRecovery(SessionJournal journal, SegmentCloser closer, StayAwakeKeeper keeper, SessionRecorder recorder,
        SessionRecoveryStatus status, IAgentEventSink events, RecordingOptions rec, TimeProvider time, ILogger? log,
        bool mock, Func<string, Task<bool>>? isConnected, Func<SegmentProcess, string[], Process?>? findProcess)
    {
        _journal = journal;
        _closer = closer;
        _keeper = keeper;
        _recorder = recorder;
        _status = status;
        _events = events;
        _rec = rec;
        _time = time;
        _log = log ?? NullLogger.Instance;
        _mock = mock;
        _isConnected = isConnected ?? DefaultIsConnectedAsync;
        _findProcess = findProcess ?? FindOwnProcess;
    }

    /// <summary>Lanza la recuperación en segundo plano y marca el estado (<c>running</c> → <c>done</c>).</summary>
    public Task StartInBackground()
    {
        _status.MarkRunning();
        return Task.Run(async () =>
        {
            try { await RunAsync(); }
            catch (Exception ex) { _log.LogError(ex, "Error en la recuperación de las grabaciones de sesión"); }
            finally { _status.MarkDone(); }
        });
    }

    /// <summary>Recorre todos los diarios. Devuelve cuántos tramos recuperó.</summary>
    public async Task<int> RunAsync()
    {
        IReadOnlyList<string> cases;
        try { cases = _journal.ListCaseIds(); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo recorrer la carpeta de sesiones");
            return 0;
        }
        var recovered = 0;
        foreach (var caseId in cases)
        {
            try { recovered += await RecoverCaseAsync(caseId); }
            catch (Exception ex) { _log.LogError(ex, "No se pudo recuperar la sesión del caso {CaseId}", caseId); }
        }
        if (cases.Count > 0)
            _log.LogInformation("Recuperación de sesiones: {Cases} diario(s) revisados, {N} tramo(s) recuperados",
                cases.Count, recovered);
        return recovered;
    }

    internal async Task<int> RecoverCaseAsync(string caseId)
    {
        var load = _journal.Load(caseId);
        if (load.Unreadable)
        {
            var moved = _journal.QuarantineUnreadable(caseId, _time.GetLocalNow().DateTime);
            _log.LogError("Diario de la sesión del caso {CaseId} ilegible ({Error}); renombrado a {Path}. Los videos quedan para revisión manual.",
                caseId, load.Error, moved);
            return 0;
        }
        if (load.Doc is not { } doc) return 0;

        var open = doc.Segments.Where(s => SegmentStates.IsOpen(s.State)).OrderBy(s => s.StartedAt).ToList();
        var wasActive = SessionStates.IsActive(doc.State);
        if (open.Count == 0 && !wasActive && doc.StayAwake is not { RestorePending: true } && doc.StayAwake is not { Owned: true })
            return 0;

        // 2. Procesos huérfanos: cerrar limpio el que sigue grabando (mismo PID y misma hora de inicio).
        var closedClean = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seg in open)
        {
            if (seg.Process is not { } info) continue;
            using var proc = _findProcess(info, ExpectedNames(info.Exe));
            if (proc is null) continue;
            _log.LogInformation("Recuperación: se cierra el proceso huérfano {Exe} ({Pid}) del tramo {Name}",
                info.Exe, info.Pid, seg.Filename);
            try
            {
                var stop = await ProcessStop.StopAsync(proc, TimeSpan.FromSeconds(_rec.StopTimeoutSeconds), _log, info.Exe,
                    CancellationToken.None,
                    _rec.SigtermAfterSeconds > 0 ? TimeSpan.FromSeconds(_rec.SigtermAfterSeconds) : null,
                    info.Exe == "scrcpy" ? ProcessStop.SessionSignal : "-2");
                if (stop.Clean) closedClean.Add(seg.Id);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Recuperación: no se pudo cerrar el proceso {Pid}", info.Pid);
            }
        }

        // 3. Validar, remuxear, mover y hashear cada tramo abierto.
        var count = 0;
        JournalSegment? last = null;
        foreach (var seg in open)
        {
            // Sin cierre limpio conocido (proceso muerto de golpe), se remuxea; el cierre también lo hace si el video
            // no se puede medir.
            var forced = seg.Forced == true || (seg.StoppedAt is null && !closedClean.Contains(seg.Id));
            var input = new SegmentCloseInput(seg.Filename, seg.StartedAt, seg.StoppedAt, forced,
                FallbackStoppedAt: doc.LastAliveAt);
            SegmentCloseResult r;
            try
            {
                r = await _closer.CloseAsync(caseId, input);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Recuperación: no se pudo cerrar el tramo {Name} del caso {CaseId}", seg.Filename, caseId);
                continue;
            }
            seg.StopReason ??= SessionReasons.TatanaCerrado;
            SessionRecorder.ApplyClose(seg, r, recovered: true);
            last = seg;
            count++;
            _journal.Save(doc, Now);
            await EmitAsync("session_segment_closed", new { case_id = caseId, origin_id = (string?)null, segment = seg.ToDto() });
        }

        // 4-5. Hueco y estado.
        if (wasActive)
        {
            var lastScreen = doc.Segments.Where(s => s.Track == SessionNames.TrackScreen)
                .OrderBy(s => s.StartedAt).LastOrDefault();
            var from = lastScreen?.StoppedAt ?? doc.StoppedAt ?? doc.LastAliveAt ?? Now;
            if (!doc.Gaps.Any(g => g.Track == SessionNames.TrackScreen && g.To is null))
            {
                doc.Gaps.Add(new SessionGapDto
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Track = SessionNames.TrackScreen,
                    From = from,
                    Reason = SessionReasons.TatanaCerrado,
                    SegmentLost = last is not null && ReferenceEquals(last, lastScreen) && last.State == SegmentStates.Discarded,
                });
            }
            doc.State = SessionStates.Interrupted;
            doc.StopReason = SessionReasons.TatanaCerrado;
            doc.StoppedAt = from;
        }

        // 6. Pantalla siempre encendida.
        if (doc.StayAwake is { } stay && (stay.Owned || stay.RestorePending))
        {
            var connected = await _isConnected(stay.Serial);
            await _keeper.RestoreAsync(stay, connected);
            if (stay.RestorePending) _recorder.RegisterPendingRestore(caseId, stay.Serial);
        }
        _journal.Save(doc, Now);
        if (wasActive || count > 0)
            _log.LogInformation("Recuperación: sesión del caso {CaseId} interrumpida, {N} tramo(s) recuperados", caseId, count);
        return count;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private async Task EmitAsync(string type, object data)
    {
        try { await _events.BroadcastAsync(new AgentEvent { Type = type, Data = data }, CancellationToken.None); }
        catch { /* informativo */ }
    }

    /// <summary>Nombres de proceso aceptables para el ejecutable guardado (F2 suma ffmpeg y python).</summary>
    internal static string[] ExpectedNames(string? exe) => exe switch
    {
        "ffmpeg" => ["ffmpeg"],
        "python" => ["python"],
        _ => ["scrcpy"],
    };

    /// <summary>
    /// El proceso del diario si sigue vivo y es el mismo: nombre que contiene alguno de <paramref name="names"/> y hora
    /// de inicio a ±2 s. null si murió o el PID es de otro proceso (no se toca).
    /// </summary>
    internal static Process? FindOwnProcess(SegmentProcess info, string[] names)
    {
        Process? p = null;
        try
        {
            p = Process.GetProcessById(info.Pid);
            if (p.HasExited || !IsSameProcess(p.ProcessName, p.StartTime.ToUniversalTime(), info, names))
            {
                p.Dispose();
                return null;
            }
            return p;
        }
        catch
        {
            p?.Dispose();
            return null;
        }
    }

    /// <summary>Puro: mismo nombre (contiene, sin distinguir mayúsculas) y hora de inicio a ±2 s.</summary>
    internal static bool IsSameProcess(string processName, DateTime startUtc, SegmentProcess info, string[] names) =>
        names.Any(n => processName.Contains(n, StringComparison.OrdinalIgnoreCase)) &&
        (startUtc - DateTime.SpecifyKind(info.StartedAt, DateTimeKind.Utc)).Duration() <= ProcessStartTolerance;

    private async Task<bool> DefaultIsConnectedAsync(string serial)
    {
        if (_mock) return true;
        try
        {
            var r = await ProcessRunner.RunArgumentListAsync(AdbPath.Value, ["-s", serial, "get-state"],
                CancellationToken.None, timeout: TimeSpan.FromSeconds(3));
            return r.Success && r.Stdout.Trim() == "device";
        }
        catch
        {
            return false;
        }
    }
}
