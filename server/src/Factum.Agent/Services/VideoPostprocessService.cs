using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.WebSockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services;

// ── Post-proceso de grabaciones iOS (SDD ios-grabacion-larga §5.3) ────────────
//
// Las variantes ("Suavizado" = minterpolate blend, "RIFE" si está instalado) son SOLO una ayuda
// visual: no son evidencia, nunca van a cases/<id>/ ni al ZIP (D5). Reglas que garantiza esta clase:
//   - ffmpeg/rife NUNCA abren el original: se copia a <DataDirectory>/.postprocess/<stem>/source.mp4
//     desde un handle abierto con FileShare.Read | FileShare.Delete ANTES de responder el stop (DA1).
//     Así mover el original al caso (File.Move) nunca da file_busy, en ningún SO.
//   - Cola global de un trabajo por vez (DA4).
//   - Cada etiqueta planeada termina con EXACTAMENTE un estado terminal (ready/failed/timeout/skipped/
//     cancelled); el finally de RunJobAsync es la red de seguridad (H2).
//   - Plazo proporcional a la duración (D3); al vencer se mata el árbol del proceso y se borra el parcial.
//   - Cancelable: al quitar la grabación (removed), al entrar en mantenimiento (agent_update) y al
//     cerrar Tatana (agent_stopping).

public interface IVideoPostprocessService
{
    /// <summary>
    /// Sincrónico y rápido. Abre el original (FileShare.Read | FileShare.Delete) ANTES de volver y encola
    /// el trabajo. Devuelve las etiquetas planeadas ([] si no habrá variantes).
    /// </summary>
    IReadOnlyList<string> Start(string originalPath, TimeSpan? durationHint);

    /// <summary>
    /// Estado de las variantes de un original. <paramref name="known"/> = true si está en el registro en
    /// memoria; si no, lista las variantes completas que existan en la raíz (§4.4).
    /// </summary>
    IReadOnlyList<VideoVariantStatusDto> GetStatus(string originalFilename, out bool known);

    /// <summary>Cancela el trabajo de ese original (removed) y borra sus variantes. Nunca tira (§4.6).</summary>
    void Discard(string originalFilename);

    /// <summary>Cancela todos los trabajos en curso o en cola con ese motivo (agent_update | agent_stopping).</summary>
    void CancelAll(string reason);

    /// <summary>Inicio del trabajo más viejo en cola o corriendo; null si no hay (§4.5).</summary>
    DateTimeOffset? BackgroundSince { get; }

    /// <summary>Espera a que terminen los trabajos activos, como mucho <paramref name="max"/>.</summary>
    Task WhenIdleAsync(TimeSpan max);
}

/// <summary>
/// Estado de una variante: payload de <c>video_variant_status</c> (WS, null explícitos) e item de
/// <c>GET /video-postprocess/status</c> (HTTP, null omitidos). Contrato §4.3 / §9.
/// </summary>
public sealed record VideoVariantStatusDto(string OriginalFilename, string Label, string State, int? Progress,
    string? Filename, string? Url, string? Reason, string? Message, DateTimeOffset UpdatedAt);

internal static class VideoVariantStates
{
    public const string Pending   = "pending";
    public const string Running   = "running";
    public const string Ready     = "ready";
    public const string Failed    = "failed";
    public const string Timeout   = "timeout";
    public const string Skipped   = "skipped";
    public const string Cancelled = "cancelled";

    public static bool IsTerminal(string state) =>
        state is Ready or Failed or Timeout or Skipped or Cancelled;
}

internal static class VideoVariantReasons
{
    public const string FfmpegError      = "ffmpeg_error";
    public const string Timeout          = "timeout";
    public const string TooLong          = "too_long";
    public const string InsufficientDisk = "insufficient_disk";
    public const string SourceMissing    = "source_missing";
    public const string Removed          = "removed";
    public const string AgentUpdate      = "agent_update";
    public const string AgentStopping    = "agent_stopping";
    public const string InternalError    = "internal_error";
}

public sealed partial class VideoPostprocessService : IVideoPostprocessService, IDisposable
{
    public const string LabelBlend = "Suavizado";
    public const string LabelRife = "RIFE";
    public const string LabelMciLegacy = "MCI";
    public const string WorkDirName = ".postprocess";

    private const int CopyBufferSize = 1024 * 1024;
    private const int MaxMessageLength = 200;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retention = TimeSpan.FromHours(6);
    private const int MaxRetainedJobs = 200;

    private readonly string _root;
    private readonly string _workRoot;
    private readonly long _minFreeBytes;
    private readonly VideoPostprocessOptions _o;
    private readonly IAgentEventSink _sink;
    private readonly IAgentDiskProbe _probe;
    private readonly ILogger _log;
    private readonly TimeProvider _time;
    private readonly string? _ffmpeg;
    private readonly string? _rife;

    private readonly SemaphoreSlim _queue = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<string, Job> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<AgentEvent> _events =
        Channel.CreateUnbounded<AgentEvent>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _pump;
    private int _ffmpegMissingLogged;

    public VideoPostprocessService(IOptions<AgentOptions> opts, IAgentEventSink sink, IAgentDiskProbe probe,
        ILogger<VideoPostprocessService> log, TimeProvider time)
        : this(opts.Value, sink, probe, log, time,
            ToolResolver.Find(AgentTools.Ffmpeg)?.Path, ToolResolver.Find(AgentTools.Rife)?.Path)
    {
    }

    /// <summary>Para tests: herramientas explícitas (rife = null para no depender de la PC).</summary>
    internal VideoPostprocessService(AgentOptions opts, IAgentEventSink sink, IAgentDiskProbe? probe,
        ILogger? log, TimeProvider? time, string? ffmpegPath, string? rifePath)
    {
        _log = log ?? NullLogger.Instance;
        _root = Path.GetFullPath(opts.DataDirectory);
        _workRoot = Path.Combine(_root, WorkDirName);
        _minFreeBytes = Math.Max(0, opts.MinFreeBytes);
        _o = (opts.VideoPostprocess ?? new VideoPostprocessOptions()).Clone().Normalize(_log);
        _sink = sink;
        _probe = probe ?? new DriveInfoAgentDiskProbe();
        _time = time ?? TimeProvider.System;
        _ffmpeg = ffmpegPath;
        _rife = rifePath;
        _pump = Task.Run(PumpEventsAsync);
    }

    // ── Trabajo ───────────────────────────────────────────────────────────────

    private sealed class Job
    {
        public required string OriginalFilename { get; init; }
        public required string Stem { get; init; }
        public required string WorkDir { get; init; }
        public required IReadOnlyList<string> Labels { get; init; }
        public required DateTimeOffset Started { get; init; }
        public TimeSpan? DurationHint { get; init; }
        public FileStream? Source;
        public Exception? OpenError;
        public readonly CancellationTokenSource Cts = new();
        // Primer motivo de cancelación (gana el primero). Removed es aparte: borra variantes siempre.
        public string? CancelReason;
        public bool Removed;
        public DateTimeOffset? Finished;
        public Task? Task;
        // Último estado por etiqueta (bajo _gate).
        public readonly Dictionary<string, VideoVariantStatusDto> States = new(StringComparer.Ordinal);
    }

    // ── API pública ───────────────────────────────────────────────────────────

    public IReadOnlyList<string> Start(string originalPath, TimeSpan? durationHint)
    {
        if (!_o.Enabled) return [];
        if (_ffmpeg is null)
        {
            if (Interlocked.Exchange(ref _ffmpegMissingLogged, 1) == 0)
                _log.LogInformation("Post-proceso de video deshabilitado: no se encontró ffmpeg");
            return [];
        }

        var full = Path.GetFullPath(originalPath);
        var filename = Path.GetFileName(full);
        var stem = Path.GetFileNameWithoutExtension(full);
        List<string> labels = [LabelBlend];
        if (_rife is not null) labels.Add(LabelRife);

        // El handle se abre ANTES de volver (Duda 6): cuando el cliente recibe la respuesta del stop, el
        // original ya está abierto y se puede mover/borrar (FileShare.Delete) mientras se copia.
        FileStream? source = null;
        Exception? openError = null;
        try
        {
            source = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception ex) { openError = ex; }

        var now = _time.GetUtcNow();
        Job job;
        lock (_gate)
        {
            if (_jobs.TryGetValue(filename, out var existing) && existing.Finished is null)
            {
                source?.Dispose();
                return existing.Labels;
            }
            Prune(now);
            job = new Job
            {
                OriginalFilename = filename,
                Stem = stem,
                WorkDir = Path.Combine(_workRoot, stem),
                Labels = labels,
                Started = now,
                DurationHint = durationHint,
                Source = source,
                OpenError = openError,
            };
            foreach (var label in labels)
                job.States[label] = new VideoVariantStatusDto(filename, label, VideoVariantStates.Pending,
                    null, null, null, null, null, now);
            _jobs[filename] = job;
            job.Task = Task.Run(() => RunJobAsync(job));
        }
        _log.LogInformation("Post-proceso de {Original} en cola: {Labels}", filename, string.Join(", ", labels));
        return labels;
    }

    public IReadOnlyList<VideoVariantStatusDto> GetStatus(string originalFilename, out bool known)
    {
        lock (_gate)
        {
            if (_jobs.TryGetValue(originalFilename, out var job))
            {
                known = true;
                return job.Labels.Select(l => job.States[l]).ToList();
            }
        }

        known = false;
        var stem = Path.GetFileNameWithoutExtension(originalFilename);
        var list = new List<VideoVariantStatusDto>();
        foreach (var (label, suffix) in new[] { (LabelBlend, "_blend"), (LabelRife, "_rife"), (LabelMciLegacy, "_mci") })
        {
            var name = stem + suffix + ".mp4";
            var fi = new FileInfo(Path.Combine(_root, name));
            if (!fi.Exists || fi.Length == 0) continue;
            list.Add(new VideoVariantStatusDto(originalFilename, label, VideoVariantStates.Ready, 100, name,
                "/files/" + name, null, null, new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero)));
        }
        return list;
    }

    public void Discard(string originalFilename)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(originalFilename) ||
                Path.GetFileName(originalFilename) != originalFilename) return;

            Job? active = null;
            lock (_gate)
            {
                if (_jobs.TryGetValue(originalFilename, out var job))
                {
                    if (job.Finished is null)
                    {
                        job.Removed = true;
                        job.CancelReason ??= VideoVariantReasons.Removed;
                        active = job;
                    }
                    else _jobs.Remove(originalFilename);
                }
            }
            if (active is not null)
            {
                // La limpieza (carpeta de trabajo + variantes en la raíz) la hace el finally del trabajo.
                _log.LogInformation("Post-proceso de {Original}: cancelado porque se quitó la grabación", originalFilename);
                TryCancel(active);
                return;
            }
            if (!RecordingNameRegex().IsMatch(originalFilename)) return;
            DeleteRootVariants(Path.GetFileNameWithoutExtension(originalFilename));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudieron descartar las variantes de {Original}", originalFilename);
        }
    }

    public void CancelAll(string reason)
    {
        List<Job> active;
        lock (_gate)
        {
            active = _jobs.Values.Where(j => j.Finished is null).ToList();
            foreach (var j in active) j.CancelReason ??= reason;
        }
        if (active.Count > 0)
            _log.LogInformation("Post-proceso: se cancelan {N} trabajo(s) ({Reason})", active.Count, reason);
        foreach (var j in active) TryCancel(j);
    }

    public DateTimeOffset? BackgroundSince
    {
        get
        {
            lock (_gate)
            {
                DateTimeOffset? since = null;
                foreach (var j in _jobs.Values)
                    if (j.Finished is null && (since is null || j.Started < since)) since = j.Started;
                return since;
            }
        }
    }

    public async Task WhenIdleAsync(TimeSpan max)
    {
        Task[] tasks;
        lock (_gate)
            tasks = _jobs.Values.Where(j => j.Finished is null && j.Task is not null).Select(j => j.Task!).ToArray();
        if (tasks.Length == 0) return;
        try { await Task.WhenAll(tasks).WaitAsync(max); }
        catch (TimeoutException) { _log.LogWarning("Post-proceso: {N} trabajo(s) no terminaron en {S} s", tasks.Length, max.TotalSeconds); }
        catch { /* RunJobAsync no tira; por las dudas */ }
    }

    public void Dispose()
    {
        _events.Writer.TryComplete();
    }

    // ── Arranque ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Borra <c>&lt;DataDirectory&gt;/.postprocess/</c> (trabajos de una ejecución anterior; ahí solo escribe
    /// Tatana). Solo esa ruta exacta. Devuelve cuántas carpetas de trabajo había.
    /// </summary>
    public static int CleanupWorkDirectory(string dataDirectory)
    {
        var work = Path.Combine(Path.GetFullPath(dataDirectory), WorkDirName);
        if (!Directory.Exists(work)) return 0;
        var n = 0;
        try { n = Directory.EnumerateFileSystemEntries(work).Count(); } catch { }
        Directory.Delete(work, recursive: true);
        return n;
    }

    // ── Ejecución ─────────────────────────────────────────────────────────────

    private async Task RunJobAsync(Job job)
    {
        var ct = job.Cts.Token;
        var acquired = false;
        double? duration = null;
        var sw = Stopwatch.StartNew();
        try
        {
            await _queue.WaitAsync(ct);
            acquired = true;

            if (job.OpenError is not null || job.Source is null)
            {
                var msg = Trim(job.OpenError?.Message);
                foreach (var l in job.Labels)
                    await SetTerminalAsync(job, l, VideoVariantStates.Failed, VideoVariantReasons.SourceMissing, msg, null, sw);
                return;
            }

            // Espacio: tamaño del original + MinFreeBytes.
            long size;
            try { size = job.Source.Length; }
            catch (Exception ex)
            {
                foreach (var l in job.Labels)
                    await SetTerminalAsync(job, l, VideoVariantStates.Failed, VideoVariantReasons.SourceMissing, Trim(ex.Message), null, sw);
                return;
            }
            var free = _probe.GetAvailableFreeBytes(_root);
            if (free is long f && f < size + _minFreeBytes)
            {
                foreach (var l in job.Labels)
                    await SetTerminalAsync(job, l, VideoVariantStates.Skipped, VideoVariantReasons.InsufficientDisk,
                        $"libre {f} B, hace falta {size + _minFreeBytes} B", null, sw);
                return;
            }

            // Copia de trabajo (DA1): ffmpeg solo lee esta copia.
            Directory.CreateDirectory(job.WorkDir);
            var work = Path.Combine(job.WorkDir, "source.mp4");
            try
            {
                await using (var dst = new FileStream(work, FileMode.Create, FileAccess.Write, FileShare.None,
                                 CopyBufferSize, FileOptions.Asynchronous))
                {
                    job.Source.Position = 0;
                    await job.Source.CopyToAsync(dst, CopyBufferSize, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                foreach (var l in job.Labels)
                    await SetTerminalAsync(job, l, VideoVariantStates.Failed, VideoVariantReasons.SourceMissing, Trim(ex.Message), null, sw);
                return;
            }
            finally
            {
                CloseSource(job);
            }

            duration = await ProbeDurationAsync(_ffmpeg!, work, ct) ?? job.DurationHint?.TotalSeconds;

            foreach (var label in job.Labels)
            {
                ct.ThrowIfCancellationRequested();
                var max = label == LabelBlend ? _o.BlendMaxSourceSeconds : _o.RifeMaxSourceSeconds;
                if (ExceedsThreshold(duration, max))
                {
                    await SetTerminalAsync(job, label, VideoVariantStates.Skipped, VideoVariantReasons.TooLong,
                        $"duración {duration:0} s > {max} s", duration, sw);
                    continue;
                }

                var started = Stopwatch.StartNew();
                var isBlend = label == LabelBlend;
                await EmitAsync(job, label, VideoVariantStates.Running,
                    isBlend && duration is > 0 ? 0 : null, null, null, null, null);

                var suffix = isBlend ? "_blend" : "_rife";
                var partial = Path.Combine(job.WorkDir, job.Stem + suffix + ".mp4");
                var timeout = ComputeTimeout(duration, _o);
                var result = isBlend
                    ? await RunBlendAsync(job, work, partial, duration, timeout, ct)
                    : await RunRifeAsync(job, work, partial, timeout, ct);

                if (result.ExitCode == ProcessRunner.TimeoutExitCode)
                {
                    TryDeleteFile(partial);
                    await SetTerminalAsync(job, label, VideoVariantStates.Timeout, VideoVariantReasons.Timeout,
                        $"no terminó en {timeout.TotalSeconds:0} s", duration, started);
                    continue;
                }
                if (!result.Success || !File.Exists(partial))
                {
                    TryDeleteFile(partial);
                    var msg = Trim(LastLine(result.Stderr) ?? $"código de salida {result.ExitCode}");
                    await SetTerminalAsync(job, label, VideoVariantStates.Failed, VideoVariantReasons.FfmpegError,
                        msg, duration, started);
                    continue;
                }

                // Una variante removida mientras se movía no tiene que quedar en la raíz: se revisa ct.
                ct.ThrowIfCancellationRequested();
                var finalName = job.Stem + suffix + ".mp4";
                File.Move(partial, Path.Combine(_root, finalName), overwrite: true);
                await SetReadyAsync(job, label, finalName, duration, started);
            }
        }
        catch (OperationCanceledException) when (job.Cts.IsCancellationRequested)
        {
            string reason;
            lock (_gate) reason = job.CancelReason ?? VideoVariantReasons.AgentStopping;
            foreach (var l in NonTerminal(job))
                await SetTerminalAsync(job, l, VideoVariantStates.Cancelled, reason, null, duration, sw);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Post-proceso de {Original} falló", job.OriginalFilename);
            foreach (var l in NonTerminal(job))
                await SetTerminalAsync(job, l, VideoVariantStates.Failed, VideoVariantReasons.InternalError,
                    Trim(ex.Message), duration, sw);
        }
        finally
        {
            CloseSource(job);
            // Red de seguridad (H2): ninguna etiqueta queda sin estado terminal.
            foreach (var l in NonTerminal(job))
            {
                try
                {
                    await SetTerminalAsync(job, l, VideoVariantStates.Failed, VideoVariantReasons.InternalError,
                        "el trabajo terminó sin estado", duration, sw);
                }
                catch { /* SetTerminalAsync no tira; por las dudas */ }
            }
            await DeleteWorkDirAsync(job.WorkDir);
            bool removed;
            lock (_gate)
            {
                job.Finished = _time.GetUtcNow();
                removed = job.Removed;
                Prune(job.Finished.Value);
            }
            if (removed) DeleteRootVariants(job.Stem);
            if (acquired) _queue.Release();
        }
    }

    private async Task<ProcessResult> RunBlendAsync(Job job, string work, string partial, double? duration,
        TimeSpan timeout, CancellationToken ct)
    {
        string[] args =
        [
            "-hide_banner", "-nostats", "-loglevel", "error", "-progress", "pipe:1", "-y",
            "-i", work,
            "-vf", "minterpolate=fps=30:mi_mode=blend",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "23",
            partial,
        ];
        var interval = TimeSpan.FromSeconds(_o.ProgressIntervalSeconds);
        var lastEmit = Stopwatch.StartNew();
        var lastPct = 0;
        var gate = new object();
        void OnLine(string line)
        {
            var pct = ParseProgressPercent(line, duration);
            if (pct is not int p) return;
            lock (gate)
            {
                if (p == lastPct) return;
                // El registro siempre tiene el último valor (GET status); el WS, como mucho cada `interval`.
                var broadcast = lastEmit.Elapsed >= interval;
                lastPct = p;
                if (broadcast) lastEmit.Restart();
                UpdateProgress(job, LabelBlend, p, broadcast);
            }
        }
        return await ProcessRunner.RunArgumentListAsync(_ffmpeg!, args, ct, timeout: timeout, onStdoutLine: OnLine);
    }

    private async Task<ProcessResult> RunRifeAsync(Job job, string work, string partial, TimeSpan timeout,
        CancellationToken ct)
    {
        // Un solo plazo para los tres pasos; frames DENTRO de la carpeta de trabajo (se borran con ella).
        var deadline = Stopwatch.StartNew();
        TimeSpan Remaining() => timeout - deadline.Elapsed;
        var framesIn = Path.Combine(job.WorkDir, "rife_in");
        var framesOut = Path.Combine(job.WorkDir, "rife_out");
        Directory.CreateDirectory(framesIn);
        Directory.CreateDirectory(framesOut);

        var extract = await ProcessRunner.RunArgumentListAsync(_ffmpeg!,
            ["-hide_banner", "-loglevel", "error", "-y", "-i", work, "-fps_mode", "passthrough",
                Path.Combine(framesIn, "%08d.png")], ct, timeout: Positive(Remaining()));
        if (!extract.Success) return extract;

        if (Remaining() <= TimeSpan.Zero) return new ProcessResult(ProcessRunner.TimeoutExitCode, "", "");
        var rife = await ProcessRunner.RunArgumentListAsync(_rife!,
            ["-i", framesIn, "-o", framesOut, "-r", "4", "-j", "1:1:1"], ct, timeout: Positive(Remaining()));
        if (!rife.Success) return rife;

        if (Remaining() <= TimeSpan.Zero) return new ProcessResult(ProcessRunner.TimeoutExitCode, "", "");
        return await ProcessRunner.RunArgumentListAsync(_ffmpeg!,
            ["-hide_banner", "-loglevel", "error", "-y", "-framerate", "30",
                "-i", Path.Combine(framesOut, "%08d.png"),
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "23", partial], ct, timeout: Positive(Remaining()));
    }

    private static TimeSpan Positive(TimeSpan t) => t > TimeSpan.FromMilliseconds(1) ? t : TimeSpan.FromMilliseconds(1);

    private async Task<double?> ProbeDurationAsync(string ffmpeg, string path, CancellationToken ct)
    {
        // `ffmpeg -i` sin salida termina con código ≠ 0: es lo esperado; solo interesa el stderr.
        var r = await ProcessRunner.RunArgumentListAsync(ffmpeg, ["-hide_banner", "-i", path], ct,
            timeout: ProbeTimeout);
        return ParseFfmpegDuration(r.Stderr);
    }

    // ── Estado y emisión ──────────────────────────────────────────────────────

    private List<string> NonTerminal(Job job)
    {
        lock (_gate)
            return job.Labels.Where(l => !VideoVariantStates.IsTerminal(job.States[l].State)).ToList();
    }

    private Task SetTerminalAsync(Job job, string label, string state, string reason, string? message,
        double? duration, Stopwatch elapsed)
    {
        LogTerminal(job, label, state, reason, message, duration, elapsed);
        return EmitAsync(job, label, state, null, null, null, reason, message);
    }

    private Task SetReadyAsync(Job job, string label, string filename, double? duration, Stopwatch elapsed)
    {
        LogTerminal(job, label, VideoVariantStates.Ready, null, null, duration, elapsed);
        var url = "/files/" + filename;
        var emitted = EmitAsync(job, label, VideoVariantStates.Ready, 100, filename, url, null, null);
        // Compatibilidad (§4.3, DA8): un cliente anterior a esta HU solo escucha video_variant_ready.
        Enqueue(new AgentEvent
        {
            Type = "video_variant_ready",
            Data = new
            {
                original_filename = job.OriginalFilename,
                label,
                filename,
                url,
            },
        });
        return emitted;
    }

    private void LogTerminal(Job job, string label, string state, string? reason, string? message,
        double? duration, Stopwatch elapsed)
    {
        var level = state is VideoVariantStates.Failed or VideoVariantStates.Timeout ? LogLevel.Warning : LogLevel.Information;
        _log.Log(level,
            "Variante {Label} de {Original}: {State}{Reason} (video {Duration} s, tardó {Elapsed:0.0} s){Message}",
            label, job.OriginalFilename, state, reason is null ? "" : "/" + reason,
            duration is double d ? d.ToString("0.0", CultureInfo.InvariantCulture) : "?",
            elapsed.Elapsed.TotalSeconds, message is null ? "" : ": " + message);
    }

    /// <summary>
    /// Actualiza el registro y encola el evento. Un estado terminal no se pisa (exactamente un terminal por
    /// etiqueta). No tira: la emisión es asíncrona y un WS caído nunca rompe el trabajo.
    /// </summary>
    private Task EmitAsync(Job job, string label, string state, int? progress, string? filename, string? url,
        string? reason, string? message)
    {
        VideoVariantStatusDto dto;
        lock (_gate)
        {
            if (job.States.TryGetValue(label, out var prev) && VideoVariantStates.IsTerminal(prev.State))
                return Task.CompletedTask;
            dto = new VideoVariantStatusDto(job.OriginalFilename, label, state, progress, filename, url, reason,
                message, _time.GetUtcNow());
            job.States[label] = dto;
        }
        Enqueue(new AgentEvent { Type = "video_variant_status", Data = dto });
        return Task.CompletedTask;
    }

    private void UpdateProgress(Job job, string label, int progress, bool broadcast)
    {
        VideoVariantStatusDto dto;
        lock (_gate)
        {
            if (!job.States.TryGetValue(label, out var prev) || prev.State != VideoVariantStates.Running) return;
            dto = prev with { Progress = progress, UpdatedAt = _time.GetUtcNow() };
            job.States[label] = dto;
        }
        if (broadcast) Enqueue(new AgentEvent { Type = "video_variant_status", Data = dto });
    }

    private void Enqueue(AgentEvent evt) => _events.Writer.TryWrite(evt);

    // Un solo lector: los eventos salen en orden y sin dos SendAsync del post-proceso en paralelo.
    private async Task PumpEventsAsync()
    {
        await foreach (var evt in _events.Reader.ReadAllAsync())
        {
            try { await _sink.BroadcastAsync(evt); }
            catch (Exception ex) { _log.LogDebug(ex, "No se pudo emitir {Type}", evt.Type); }
        }
    }

    // ── Limpieza ──────────────────────────────────────────────────────────────

    private void TryCancel(Job job)
    {
        try { job.Cts.Cancel(); }
        catch (Exception ex) { _log.LogDebug(ex, "Cancelación de {Original}", job.OriginalFilename); }
    }

    private static void CloseSource(Job job)
    {
        var s = Interlocked.Exchange(ref job.Source, null);
        try { s?.Dispose(); } catch { }
    }

    private async Task DeleteWorkDirAsync(string dir)
    {
        // En Windows el handle de ffmpeg puede tardar en soltarse tras el kill: 3 intentos, 500 ms.
        for (var i = 0; i < 3; i++)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                return;
            }
            catch (Exception ex) when (i < 2)
            {
                _log.LogDebug(ex, "Reintento de borrado de {Dir}", dir);
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "No se pudo borrar la carpeta de trabajo {Dir}", dir);
            }
        }
    }

    private void DeleteRootVariants(string stem)
    {
        foreach (var suffix in new[] { "_blend", "_rife", "_mci" })
        {
            var path = Path.Combine(_root, stem + suffix + ".mp4");
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    _log.LogInformation("Variante borrada: {Path}", path);
                }
            }
            catch (Exception ex) { _log.LogWarning(ex, "No se pudo borrar la variante {Path}", path); }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    // Retención del registro: terminados hasta 6 h, como mucho 200 (se descartan los más viejos). Bajo _gate.
    private void Prune(DateTimeOffset now)
    {
        var finished = _jobs.Values.Where(j => j.Finished is not null).ToList();
        foreach (var j in finished.Where(j => now - j.Finished!.Value > Retention))
            _jobs.Remove(j.OriginalFilename);
        var remaining = finished.Where(j => _jobs.ContainsKey(j.OriginalFilename))
            .OrderBy(j => j.Finished).ToList();
        for (var i = 0; i < remaining.Count - MaxRetainedJobs; i++)
            _jobs.Remove(remaining[i].OriginalFilename);
    }

    // ── Funciones puras (testeables) ──────────────────────────────────────────

    /// <summary>Plazo de una variante: null → máximo; si no, clamp(factor × d + extra, min, max) (D3, DA7).</summary>
    internal static TimeSpan ComputeTimeout(double? durationSeconds, VideoPostprocessOptions o)
    {
        if (durationSeconds is not double d || double.IsNaN(d) || d < 0)
            return TimeSpan.FromSeconds(o.TimeoutMaxSeconds);
        var secs = o.TimeoutFactor * d + o.TimeoutExtraSeconds;
        return TimeSpan.FromSeconds(Math.Clamp(secs, o.TimeoutMinSeconds, o.TimeoutMaxSeconds));
    }

    /// <summary>true si hay umbral (max &gt; 0), la duración es conocida y lo supera (DA7).</summary>
    internal static bool ExceedsThreshold(double? durationSeconds, int maxSeconds) =>
        maxSeconds > 0 && durationSeconds is double d && d > maxSeconds;

    /// <summary><c>Duration: HH:MM:SS.ss</c> del stderr de <c>ffmpeg -i</c>, en segundos; null si no está.</summary>
    internal static double? ParseFfmpegDuration(string? stderr) => FfmpegProbe.ParseDuration(stderr);

    /// <summary>
    /// Línea de <c>-progress</c> de ffmpeg (<c>out_time_us=</c> u <c>out_time_ms=</c>, ambos en µs) →
    /// porcentaje entero 0–99 (nunca 100 antes de <c>ready</c>). null si no aplica.
    /// </summary>
    internal static int? ParseProgressPercent(string? line, double? durationSeconds)
    {
        if (line is null || durationSeconds is not double d || d <= 0) return null;
        string value;
        if (line.StartsWith("out_time_us=", StringComparison.Ordinal)) value = line["out_time_us=".Length..];
        else if (line.StartsWith("out_time_ms=", StringComparison.Ordinal)) value = line["out_time_ms=".Length..];
        else return null;
        if (!long.TryParse(value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var us))
            return null;
        var pct = Math.Floor(us / 1e6 / d * 100);
        return (int)Math.Clamp(pct, 0, 99);
    }

    private static string? LastLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
    }

    private static string? Trim(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        message = message.Trim();
        return message.Length <= MaxMessageLength ? message : message[..MaxMessageLength];
    }

    [GeneratedRegex(@"^grabacion_ios_.+\.mp4$", RegexOptions.IgnoreCase)]
    private static partial Regex RecordingNameRegex();
}
