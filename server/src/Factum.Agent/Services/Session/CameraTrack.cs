using System.Diagnostics;
using System.Text;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services.Session;

/// <summary>Cámara pedida (§5.1). <c>Backend</c> = el de la lista (<c>avfoundation</c>, <c>dshow</c>, <c>v4l2</c>, <c>mock</c>).</summary>
public sealed record CameraSpec(string Id, string Name, bool WithAudio, string Backend);

/// <summary>
/// Arranque confirmado de un tramo continuo de cámara (§5.4): el ancla horaria (línea <c>Output #0</c>), el proceso y
/// la resolución/fps reales. <c>Fallback</c> = se reintentó con otro modo.
/// </summary>
public sealed record CameraStart(DateTime AnchorAt, int Pid, DateTime? ProcessStartedAt, int Width, int Height, int Fps,
    bool Fallback);

/// <summary>
/// Una línea nueva del CSV del segment muxer: el tramo <c>Index</c> quedó cerrado. <c>Final</c> = ffmpeg ya terminó y no
/// abrió el tramo siguiente (es el último del tramo continuo).
/// </summary>
public sealed record CameraSegmentClosed(int Index, string PartPath, double StartSeconds, double EndSeconds,
    bool Final = false);

/// <summary>ffmpeg de la cámara terminó sin que se lo pidieran (o la vigilancia lo cortó: <c>Detail = "stalled"</c>).</summary>
public sealed record CameraExit(DateTime At, string? Detail, string? Summary);

/// <summary>La cámara no arrancó (§5.5). <see cref="Detail"/> es el <c>detail</c> del aviso <c>camera_failed</c>.</summary>
public sealed class CameraStartException(string? detail, string message) : Exception(message)
{
    public string? Detail { get; } = detail;
}

/// <summary>
/// Pista de la cámara del procedimiento (§5.1). Aparte de <see cref="ISessionTrack"/>: la cámara no rota por pedido (rota
/// ffmpeg solo con el segment muxer) y avisa cada tramo cerrado.
/// </summary>
public interface ICameraTrack : IDisposable
{
    /// <summary>
    /// Lanza ffmpeg (con el reintento de modo de §5.5), espera la línea "Output #0" y 1,5 s vivo. Lanza
    /// <see cref="CameraStartException"/> si no arranca.
    /// </summary>
    Task<CameraStart> StartAsync(string stagingDir, int startNumber, int segmentSeconds);

    /// <summary>
    /// Cada línea nueva del CSV (sondeo de 1 s). Al salir el proceso se lee el CSV una última vez ANTES de
    /// <see cref="Exited"/>. Se invoca en el hilo de la pista: el que lo maneja no debe bloquearse esperando a la pista.
    /// </summary>
    event Action<CameraSegmentClosed>? SegmentClosed;

    /// <summary>Salió sin que se lo pidieran (o la vigilancia lo cortó por archivo quieto, Detail "stalled").</summary>
    event Action<CameraExit>? Exited;

    /// <summary>"q" por stdin; si no sale, ProcessStop (SIGTERM / Ctrl+C) y kill. Devuelve si fue limpio.</summary>
    Task<TrackStop> StopAsync();
}

public interface ICameraTrackFactory
{
    ICameraTrack Create(CameraSpec spec);
}

/// <summary>Real (ffmpeg con la cámara) o lavfi (mock y <c>Agent:Session:CameraLavfi</c>: nunca abre una cámara).</summary>
public sealed class CameraTrackFactory(IOptions<AgentOptions> opts, CameraDevices devices, ILoggerFactory logs,
    TimeProvider time) : ICameraTrackFactory
{
    public ICameraTrack Create(CameraSpec spec) =>
        new CameraTrack(spec, (opts.Value.Session ?? new Models.SessionOptions()).Clone().Normalize(null),
            devices.Lavfi, devices, opts.Value.MicDevice, logs.CreateLogger<CameraTrack>(), time);
}

/// <summary>
/// Un proceso ffmpeg por tramo continuo de cámara (§5.3–§5.5): desde que arranca hasta que se corta. Lanza, confirma el
/// arranque, sigue el CSV, avisa cada tramo cerrado, vigila que el archivo crezca y cierra con <c>q</c>.
/// </summary>
public sealed class CameraTrack : ICameraTrack
{
    private const int TailLines = 40;
    private const int StartLines = 200;
    internal static readonly TimeSpan EarlyExitWindow = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan CsvPoll = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StallPoll = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan QuitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(5);

    private readonly CameraSpec _spec;
    private readonly Models.SessionOptions _opt;
    private readonly bool _lavfi;
    private readonly CameraDevices? _devices;
    private readonly string? _micDevice;
    private readonly ILogger _log;
    private readonly TimeProvider _time;
    private readonly Lock _csvLock = new();
    private readonly LineTail _tail = new(TailLines);
    private readonly CancellationTokenSource _loops = new();
    private readonly TaskCompletionSource _exitHandled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Process? _proc;
    private string _backend = CameraArgs.BackendLavfi;
    private string _staging = "";
    private int _linesDone;
    private int _currentIndex;
    private volatile bool _started;
    private volatile bool _stopRequested;
    private volatile bool _stalled;
    private int _exitNotified;

    public event Action<CameraSegmentClosed>? SegmentClosed;
    public event Action<CameraExit>? Exited;

    /// <summary>Vigilancia de archivo quieto; se puede acortar en los tests.</summary>
    internal TimeSpan StallPollInterval { get; init; } = StallPoll;

    public CameraTrack(CameraSpec spec, Models.SessionOptions opt, bool lavfi, CameraDevices? devices, string? micDevice,
        ILogger? log, TimeProvider? time = null)
    {
        _spec = spec;
        _opt = opt;
        _lavfi = lavfi;
        _devices = devices;
        _micDevice = micDevice;
        _log = log ?? NullLogger.Instance;
        _time = time ?? TimeProvider.System;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    /// <summary>PID del ffmpeg en curso (tests).</summary>
    internal int? Pid => _proc is { } p ? SafePid(p) : null;

    public async Task<CameraStart> StartAsync(string stagingDir, int startNumber, int segmentSeconds)
    {
        if (_proc is not null) throw new InvalidOperationException("La cámara ya está grabando");
        var ffmpeg = ToolResolver.Find(AgentTools.Ffmpeg)?.Path
                     ?? throw new CameraStartException(null, CameraDevices.ErrorNoFfmpeg);
        _staging = stagingDir;
        Directory.CreateDirectory(stagingDir);

        string video = "";
        string? mic = null;
        if (_lavfi)
        {
            _backend = CameraArgs.BackendLavfi;
        }
        else
        {
            var dev = _devices is null ? null : await _devices.ResolveAsync(_spec.Id, _spec.Name);
            if (dev is null)
                throw new CameraStartException(CameraFailDetails.NotFound, CameraArgs.FailureText(CameraFailDetails.NotFound));
            _backend = dev.Backend;
            video = _backend == CameraArgs.BackendMac
                ? (dev.Name.Contains(':') && dev.Index is { } idx ? idx.ToString(System.Globalization.CultureInfo.InvariantCulture) : dev.Name)
                : dev.Id;
            if (_spec.WithAudio && _backend == CameraArgs.BackendLinux)
                mic = _micDevice; // tatana-linux D-T14: nombre de una source de PulseAudio, o null = "default"
            if (_spec.WithAudio && _backend == CameraArgs.BackendWindows)
            {
                try { mic = await MicCapture.ResolveWindowsDeviceAsync(ffmpeg, _micDevice, _log, CancellationToken.None); }
                catch (InvalidOperationException ex)
                {
                    _log.LogWarning("Cámara: sin micrófono de la PC ({Error}); se graba sin audio", ex.Message);
                }
            }
        }

        CameraMode? mode = _backend == CameraArgs.BackendLavfi
            ? null
            : CameraArgs.DefaultMode(_backend, _opt.CameraWidth, _opt.CameraHeight, _opt.CameraFps);
        var fallback = false;
        var alsaAudio = false;
        for (var attempt = 1; ; attempt++)
        {
            var args = CameraArgs.Build(new CameraArgsInput(_backend, video, _spec.WithAudio, mic, mode,
                _opt.CameraWidth, _opt.CameraHeight, _opt.CameraFps, _opt.CameraCrf, segmentSeconds, startNumber,
                stagingDir) { LinuxAlsaAudio = alsaAudio });
            var (start, failure, lines) = await LaunchAsync(ffmpeg, args, startNumber, fallback);
            if (start is not null)
            {
                _currentIndex = startNumber;
                _started = true;
                if (AdbService.HasExitedSafe(_proc!)) _ = Task.Run(OnProcessExitedAsync);
                _ = Task.Run(() => CsvLoopAsync(_loops.Token));
                if (_opt.CameraStallSeconds > 0) _ = Task.Run(() => StallLoopAsync(_loops.Token));
                _log.LogInformation("Cámara de la sesión grabando ({Name}, {W}x{H} a {Fps} fps{Fallback})", _spec.Name,
                    start.Width, start.Height, start.Fps, fallback ? ", con otro modo" : "");
                return start;
            }
            DeleteStartLeftovers(startNumber);
            if (ShouldRetryWithAlsa(_backend, _spec.WithAudio, alsaAudio, lines))
            {
                // tatana-linux D-T14: ffmpeg sin libpulse → se relanza una vez con ALSA (no cuenta como intento de modo).
                alsaAudio = true;
                attempt--;
                _log.LogWarning("Cámara: ffmpeg no soporta PulseAudio; se reintenta el audio con ALSA");
                continue;
            }
            if (attempt == 1 && failure!.RetryMode)
            {
                if (_backend == CameraArgs.BackendMac)
                {
                    mode = CameraArgs.PickMode(CameraArgs.ParseSupportedModes(lines), _opt.CameraWidth, _opt.CameraHeight);
                    if (mode is null)
                        throw new CameraStartException(CameraFailDetails.Unsupported,
                            CameraArgs.FailureText(CameraFailDetails.Unsupported));
                }
                else
                {
                    mode = null; // dshow / v4l2: el modo por defecto de la cámara
                }
                fallback = true;
                _log.LogInformation("Cámara: el modo pedido no se acepta; se reintenta con {Mode}",
                    mode is null ? "el modo por defecto" : $"{mode.Width}x{mode.Height}@{mode.Fps}");
                continue;
            }
            var detail = failure!.Detail ?? (fallback ? CameraFailDetails.Unsupported : null);
            if (failure.RetryMode) detail = CameraFailDetails.Unsupported;
            throw new CameraStartException(detail, CameraArgs.FailureText(detail));
        }
    }

    /// <summary>Relanzar con ALSA: v4l2 con audio, todavía en PulseAudio y ffmpeg dijo <c>Unknown input format</c>.</summary>
    internal static bool ShouldRetryWithAlsa(string backend, bool withAudio, bool alreadyAlsa, IReadOnlyList<string> lines) =>
        backend == CameraArgs.BackendLinux && withAudio && !alreadyAlsa && MicCapture.IsUnknownInputFormat(lines);

    private ProcessStartInfo Psi(string ffmpeg, List<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            // Consola propia oculta: el Ctrl+C del helper funciona en Windows, como scrcpy.
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    // Un intento de arranque: el ancla (Output #0) + 1,5 s vivo. Devuelve el arranque o la falla clasificada.
    private async Task<(CameraStart? Start, CameraFailure? Failure, List<string> Lines)> LaunchAsync(string ffmpeg,
        List<string> args, int startNumber, bool fallback)
    {
        var startLines = new List<string>();
        var anchor = new TaskCompletionSource<DateTime>(TaskCreationOptions.RunContinuationsAsynchronously);
        CameraStream? stream = null;
        Process proc;
        try
        {
            proc = Process.Start(Psi(ffmpeg, args)) ?? throw new CameraStartException(null, "No se pudo lanzar ffmpeg");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new CameraStartException(null, "No se pudo lanzar ffmpeg: " + ex.Message);
        }
        void OnLine(string? line)
        {
            if (line is null) return;
            _tail.Add(line);
            lock (startLines) if (startLines.Count < StartLines) startLines.Add(line);
            if (!anchor.Task.IsCompleted && CameraArgs.IsSegmentOutputLine(line)) anchor.TrySetResult(Now);
            else if (anchor.Task.IsCompleted && stream is null && CameraArgs.ParseOutputStream(line) is { } s) stream = s;
            if (line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("warning", StringComparison.OrdinalIgnoreCase))
                _log.LogWarning("ffmpeg (cámara): {Line}", line);
            else
                _log.LogDebug("ffmpeg (cámara): {Line}", line);
        }
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) _log.LogDebug("ffmpeg (cámara): {Line}", e.Data); };
        proc.ErrorDataReceived += (_, e) => OnLine(e.Data);
        proc.EnableRaisingEvents = true;
        proc.Exited += (_, _) =>
        {
            if (_started && ReferenceEquals(Volatile.Read(ref _proc), proc)) _ = Task.Run(OnProcessExitedAsync);
        };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        var exited = proc.WaitForExitAsync(CancellationToken.None);
        var timeout = TimeSpan.FromSeconds(_opt.CameraStartTimeoutSeconds);
        await Task.WhenAny(anchor.Task, exited, Task.Delay(timeout));
        List<string> Lines() { lock (startLines) return [.. startLines]; }
        if (!anchor.Task.IsCompleted && !AdbService.HasExitedSafe(proc))
        {
            _log.LogWarning("Cámara: ffmpeg no empezó a grabar en {Sec} s; se corta", timeout.TotalSeconds);
            await KillAsync(proc);
            proc.Dispose();
            return (null, CameraArgs.Classify(_backend, Lines(), null, timedOut: true), Lines());
        }
        if (anchor.Task.IsCompleted) await Task.WhenAny(exited, Task.Delay(EarlyExitWindow));
        if (AdbService.HasExitedSafe(proc))
        {
            try { proc.WaitForExit(); } catch { /* ya terminó */ }
            var code = SafeExitCode(proc);
            proc.Dispose();
            var lines = Lines();
            var failure = CameraArgs.Classify(_backend, lines, code, timedOut: false);
            _log.LogWarning("Cámara: ffmpeg terminó al arrancar (código {Code}, {Detail}): {Last}", code,
                failure.Detail ?? "sin detalle", _tail.LastNonEmpty() ?? "-");
            return (null, failure, lines);
        }

        Volatile.Write(ref _proc, proc);
        DateTime? procStart = null;
        try { procStart = proc.StartTime.ToUniversalTime(); } catch { /* sin dato: la recuperación no lo cierra */ }
        var w = stream?.Width ?? _opt.CameraWidth;
        var h = stream?.Height ?? _opt.CameraHeight;
        var f = stream?.Fps ?? _opt.CameraFps;
        return (new CameraStart(anchor.Task.Result, proc.Id, procStart, w, h, f, fallback), null, Lines());
    }

    // Un intento que no arrancó deja archivos propios (el CSV, el primer tramo y la previa): se borran.
    private void DeleteStartLeftovers(int startNumber)
    {
        AdbService.TryDelete(Path.Combine(_staging, CameraArgs.PartName(startNumber)));
        AdbService.TryDelete(Path.Combine(_staging, CameraArgs.CsvFileName));
        AdbService.TryDelete(Path.Combine(_staging, CameraArgs.PreviewFileName));
    }

    // ── CSV ──────────────────────────────────────────────────────────────────

    private async Task CsvLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(CsvPoll, ct); }
            catch (OperationCanceledException) { return; }
            try { ReadCsv(final: false); }
            catch (Exception ex) { _log.LogWarning(ex, "Cámara: error leyendo el CSV"); }
        }
    }

    /// <summary>
    /// Avisa las líneas nuevas. Con el proceso vivo, una línea se avisa recién cuando ya existe el tramo siguiente
    /// (ffmpeg abre el siguiente antes de escribir la línea del anterior); así la última línea, la del cierre, la avisa
    /// la lectura final con <c>Final = true</c>.
    /// </summary>
    private void ReadCsv(bool final)
    {
        lock (_csvLock)
        {
            var lines = CameraCsv.Read(Path.Combine(_staging, CameraArgs.CsvFileName));
            for (var k = _linesDone; k < lines.Count; k++)
            {
                var l = lines[k];
                var nextExists = File.Exists(Path.Combine(_staging, CameraArgs.PartName(l.Index + 1)));
                if (!final && (!nextExists || _stopRequested)) break;
                var isFinal = final && !nextExists;
                _linesDone = k + 1;
                _currentIndex = l.Index + 1;
                try
                {
                    SegmentClosed?.Invoke(new CameraSegmentClosed(l.Index, Path.Combine(_staging, l.PartName),
                        l.StartSeconds, l.EndSeconds, isFinal));
                }
                catch (Exception ex) { _log.LogError(ex, "Cámara: error registrando el tramo {Index}", l.Index); }
            }
        }
    }

    // ── Vigilancia (§5.4) ────────────────────────────────────────────────────

    private async Task StallLoopAsync(CancellationToken ct)
    {
        var limit = TimeSpan.FromSeconds(_opt.CameraStallSeconds);
        long lastSize = -1;
        var lastIndex = -1;
        var lastChange = Stopwatch.StartNew();
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(StallPollInterval, ct); }
            catch (OperationCanceledException) { return; }
            if (_stopRequested || _proc is not { } p) return;
            var index = _currentIndex;
            var size = AdbService.FileSize(Path.Combine(_staging, CameraArgs.PartName(index)));
            if (index != lastIndex || size != lastSize)
            {
                lastIndex = index;
                lastSize = size;
                lastChange.Restart();
                continue;
            }
            if (lastChange.Elapsed < limit) continue;
            _log.LogWarning("Cámara: el tramo {Index} no crece hace {Sec:0} s; se corta ffmpeg", index,
                lastChange.Elapsed.TotalSeconds);
            _stalled = true;
            await StopProcessAsync(p);
            return;
        }
    }

    // ── Salida y cierre ──────────────────────────────────────────────────────

    private async Task OnProcessExitedAsync()
    {
        if (Interlocked.Exchange(ref _exitNotified, 1) != 0) return;
        try
        {
            var p = _proc;
            try { p?.WaitForExit(); } catch { /* ya terminó */ }
            await _loops.CancelAsync();
            ReadCsv(final: true);
            if (!_stopRequested)
            {
                var code = p is null ? (int?)null : SafeExitCode(p);
                var detail = _stalled
                    ? CameraFailDetails.Stalled
                    : CameraArgs.Classify(_backend, _tail.Snapshot(), code, timedOut: false).Detail;
                _log.LogWarning("ffmpeg de la cámara terminó solo (código {Code}, {Detail}): {Last}", code,
                    detail ?? "sin detalle", _tail.LastNonEmpty() ?? "-");
                try { Exited?.Invoke(new CameraExit(Now, detail, _tail.LastNonEmpty())); }
                catch (Exception ex) { _log.LogWarning(ex, "Error al avisar la salida de la cámara"); }
            }
        }
        finally
        {
            _exitHandled.TrySetResult();
        }
    }

    // "q" por stdin → ProcessStop (SIGTERM / Ctrl+C) → kill. true = salió limpio (q, o 255 tras SIGTERM).
    private async Task<bool> StopProcessAsync(Process p)
    {
        if (AdbService.HasExitedSafe(p)) return true;
        try
        {
            await p.StandardInput.WriteAsync('q');
            await p.StandardInput.FlushAsync();
            p.StandardInput.Close();
        }
        catch (Exception ex) { _log.LogDebug(ex, "No se pudo mandar 'q' a ffmpeg (cámara)"); }
        using (var quit = new CancellationTokenSource(QuitTimeout))
        {
            try
            {
                await p.WaitForExitAsync(quit.Token);
                return true;
            }
            catch (OperationCanceledException) { /* sigue con la señal */ }
        }
        try
        {
            var r = await ProcessStop.StopAsync(p, SignalTimeout, _log, "ffmpeg (cámara)", CancellationToken.None, null,
                ProcessStop.SessionSignal);
            return r.Clean;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Error deteniendo ffmpeg de la cámara");
            await KillAsync(p);
            return false;
        }
    }

    private static async Task KillAsync(Process p)
    {
        try { p.Kill(entireProcessTree: true); } catch { /* ya terminó */ }
        try { await p.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* best effort */ }
    }

    public async Task<TrackStop> StopAsync()
    {
        var p = _proc;
        var requested = Now;
        if (p is null) return new TrackStop(requested, true);
        _stopRequested = true;
        var clean = await StopProcessAsync(p);
        // La lectura final del CSV la hace el manejador de la salida; si el evento del proceso tarda, se hace acá.
        if (_started)
        {
            if (await Task.WhenAny(_exitHandled.Task, Task.Delay(TimeSpan.FromSeconds(5))) != _exitHandled.Task)
                _log.LogWarning("Cámara: la salida de ffmpeg no se avisó a tiempo; se lee el CSV igual");
            ReadCsv(final: true);
        }
        await _loops.CancelAsync();
        return new TrackStop(requested, clean);
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; }
        catch (InvalidOperationException) { return -1; }
    }

    private static int? SafePid(Process p)
    {
        try { return p.Id; }
        catch (InvalidOperationException) { return null; }
    }

    public void Dispose()
    {
        try { _loops.Cancel(); } catch { /* best effort */ }
        var p = Interlocked.Exchange(ref _proc, null);
        // No se mata ffmpeg: si se llega acá sin StopAsync, la recuperación lo concilia.
        try { p?.Dispose(); } catch { /* best effort */ }
    }
}

/// <summary>
/// Aplicar una línea del CSV de la cámara al diario (§5.4 "cada línea nueva", §5.8 paso 2). Puro sobre el documento: lo
/// usan la sesión en curso y la recuperación.
/// </summary>
public static class CameraJournal
{
    /// <summary>
    /// Cierra (o crea, si el diario no lo conocía: ffmpeg pudo cerrarlo solo mientras Tatana no estaba) el tramo
    /// <c>line.Index</c> con <c>stopped_at = ancla + fin</c> y, si no es el último, abre el siguiente con
    /// <c>started_at = ancla + fin</c>. Devuelve el tramo cerrado y el nuevo (o null).
    /// </summary>
    public static (JournalSegment Closed, JournalSegment? Next) ApplyLine(SessionJournalDoc doc, CameraRunState run,
        CameraCsvLine line, bool final, string finalReason, bool recovered, TimeZoneInfo zone)
    {
        var camera = new SessionCameraDto(run.Name, run.Width, run.Height, run.Fps, run.WithAudio);
        var start = run.AnchorAt.AddSeconds(line.StartSeconds);
        var end = run.AnchorAt.AddSeconds(line.EndSeconds);
        var seg = doc.Segments.FirstOrDefault(s => s.Track == SessionNames.TrackCamera && s.Index == line.Index);
        if (seg is null)
        {
            seg = NewSegment(line.Index, start, camera, run, zone);
            seg.Recovered = recovered;
            doc.Segments.Add(seg);
        }
        seg.StoppedAt = end;
        seg.StopReason = final ? finalReason : SessionReasons.Rotacion;
        seg.State = SegmentStates.Closing;
        seg.Process = null;
        if (recovered) seg.Recovered = true;
        run.CsvLinesDone++;

        JournalSegment? next = null;
        if (!final)
        {
            next = doc.Segments.FirstOrDefault(s => s.Track == SessionNames.TrackCamera && s.Index == line.Index + 1);
            if (next is null)
            {
                next = NewSegment(line.Index + 1, end, camera, run, zone);
                next.Recovered = recovered;
                doc.Segments.Add(next);
            }
        }
        return (seg, next);
    }

    public static JournalSegment NewSegment(int index, DateTime startedAt, SessionCameraDto camera, CameraRunState run,
        TimeZoneInfo zone) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Track = SessionNames.TrackCamera,
        Index = index,
        Filename = SessionNames.SegmentName(SessionNames.TrackCamera, index,
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startedAt, DateTimeKind.Utc), zone)),
        State = SegmentStates.Recording,
        StartedAt = startedAt,
        PartFile = CameraArgs.PartName(index),
        Process = run.ProcessStartedAt is { } ps
            ? new SegmentProcess { Pid = run.Pid, StartedAt = ps, Exe = "ffmpeg" }
            : null,
        Camera = camera,
    };

    /// <summary>
    /// Renombra el <c>part_file</c> del tramo a su nombre final en el staging (si existe). Devuelve false si no estaba
    /// (el cierre lo descarta como vacío).
    /// </summary>
    public static bool RenamePart(string stagingDir, JournalSegment seg, ILogger log)
    {
        if (seg.PartFile is not { } part) return File.Exists(Path.Combine(stagingDir, seg.Filename));
        var from = Path.Combine(stagingDir, part);
        var to = Path.Combine(stagingDir, seg.Filename);
        try
        {
            if (File.Exists(from))
            {
                if (File.Exists(to))
                {
                    log.LogWarning("Cámara: ya existe {Name} en el staging; el tramo queda como {Part}", seg.Filename, part);
                    return false;
                }
                File.Move(from, to, overwrite: false);
                seg.PartFile = null;
                return true;
            }
            seg.PartFile = null;
            return File.Exists(to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Cámara: no se pudo renombrar {Part} a {Name}", part, seg.Filename);
            return false;
        }
    }
}
