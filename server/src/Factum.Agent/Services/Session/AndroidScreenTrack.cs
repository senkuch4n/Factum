using System.Diagnostics;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services.Session;

/// <summary>Inicio real de un tramo: el marcador de scrcpy (<c>StartedAt</c>) y el proceso, para la recuperación.</summary>
public sealed record TrackStart(DateTime StartedAt, int? Pid, DateTime? ProcessStartedAt, string Exe = "scrcpy");

/// <summary>Cierre de un tramo: hora del pedido de cierre y si terminó con la señal (false = kill, hace falta remux).</summary>
public sealed record TrackStop(DateTime StopRequestedAt, bool Clean);

/// <summary>La captura en curso terminó sola (desconexión, error): no la cerró Tatana.</summary>
public sealed record TrackExit(string OutputPath, DateTime At);

/// <summary>
/// Una pista de la sesión (grabacion-sesion-completa §5.1). La de Android rota con corte solapado (§5.4, DU2=A): el
/// tramo nuevo arranca y recién con su marcador se cierra el anterior.
/// </summary>
public interface ISessionTrack : IDisposable
{
    /// <summary>Lanza la captura al archivo y vuelve con el marcador de inicio. 504 <c>recording_start_timeout</c> o error de scrcpy.</summary>
    Task<TrackStart> StartAsync(string outputPath);

    /// <summary>
    /// Corte solapado: lanza el tramo siguiente, espera su marcador, llama a <paramref name="onNextStarted"/> (el diario
    /// lo registra antes de tocar el anterior) y cierra el anterior. Si el siguiente no arranca, lanza y el anterior
    /// sigue grabando (el archivo del intento se borra).
    /// </summary>
    Task<(TrackStart Next, TrackStop Previous)> RotateAsync(string nextPath, Func<TrackStart, Task> onNextStarted);

    /// <summary>Cierra la captura en curso (SIGINT o Ctrl+C, con escalado y kill de último recurso).</summary>
    Task<TrackStop> StopAsync();

    /// <summary>La captura en curso terminó sin que se la pidieran.</summary>
    event Action<TrackExit>? Exited;
}

public interface ISessionTrackFactory
{
    ISessionTrack CreateScreen(string serial, int androidVersion);
}

public sealed class SessionTrackFactory(IOptions<AgentOptions> opts, ILoggerFactory logs, TimeProvider time)
    : ISessionTrackFactory
{
    public ISessionTrack CreateScreen(string serial, int androidVersion) =>
        opts.Value.Mock
            ? new MockScreenTrack(opts.Value.DataDirectory, logs.CreateLogger<MockScreenTrack>(), time)
            : new AndroidScreenTrack(serial, androidVersion,
                (opts.Value.Recording ?? new RecordingOptions()).Clone().Normalize(null),
                logs.CreateLogger<AndroidScreenTrack>(), time,
                (opts.Value.Session ?? new Models.SessionOptions()).Clone().Normalize(null));
}

/// <summary>
/// Pantalla de Android con scrcpy (§5.4). Un tramo = un proceso scrcpy con los argumentos de
/// <see cref="AdbService.BuildScrcpyArgs"/> (mismo audio que los clips: interno en Android 11+, nunca el micrófono de
/// la PC, D-T21). Todo con <see cref="CancellationToken.None"/>: un request cortado no deja la captura a medias.
/// </summary>
public sealed class AndroidScreenTrack : ISessionTrack
{
    private const int TailLines = 40;
    private static readonly TimeSpan StartEarlyExitWindow = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan RotateEarlyExitWindow = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan RotateRetryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan NoMarkerStartOffset = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AbortTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AbortEscalate = TimeSpan.FromSeconds(2);

    private readonly string _serial;
    private readonly string _audioArg;
    private readonly RecordingOptions _rec;
    private readonly ILogger _log;
    private readonly TimeProvider _time;
    private readonly TimeSpan _rotationConfirm;
    private readonly int _rotationAttempts;
    private Capture? _current;

    public event Action<TrackExit>? Exited;

    public AndroidScreenTrack(string serial, int androidVersion, RecordingOptions rec, ILogger log, TimeProvider time,
        Models.SessionOptions? session = null)
    {
        _rotationConfirm = TimeSpan.FromMilliseconds(session?.RotationConfirmMilliseconds
            ?? Models.SessionOptions.DefaultRotationConfirmMilliseconds);
        _rotationAttempts = session?.RotationStartAttempts ?? Models.SessionOptions.DefaultRotationStartAttempts;
        _serial = serial;
        _audioArg = AudioArg(androidVersion);
        _rec = rec;
        _log = log;
        _time = time;
    }

    /// <summary>Audio de la pista de pantalla (D-T21): interno en Android 11+, si no, sin audio.</summary>
    internal static string AudioArg(int androidVersion) => androidVersion >= 11 ? "--audio-source=output" : "--no-audio";

    /// <summary><c>device</c> | <c>none</c> (campo <c>audio</c> del diario).</summary>
    internal static string AudioLabel(int androidVersion) => androidVersion >= 11 ? "device" : "none";

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private sealed class Capture(Process proc, string outputPath, LineTail tail,
        TaskCompletionSource<DateTime> marker, DateTime requestedAt)
    {
        public Process Proc { get; } = proc;
        public string OutputPath { get; } = outputPath;
        public LineTail Tail { get; } = tail;
        public TaskCompletionSource<DateTime> Marker { get; } = marker;
        public DateTime RequestedAt { get; } = requestedAt;
        public volatile bool StopRequested;
        public int ExitNotified;
    }

    private Capture Launch(string outputPath)
    {
        var scrcpy = ToolResolver.Find(AgentTools.Scrcpy)
            ?? throw new InvalidOperationException(AdbService.ScrcpyMissingMessage());
        string? serverPath = null;
        if (scrcpy.Source == ToolSource.Portable)
        {
            serverPath = Path.Combine(Path.GetDirectoryName(scrcpy.Path)!, "scrcpy-server");
            if (!File.Exists(serverPath)) throw new InvalidOperationException(AdbService.ScrcpyMissingMessage());
        }

        // Igual que AdbService.StartScrcpyAsync: consola propia oculta (Ctrl+C en Windows) y un solo servidor adb.
        var psi = new ProcessStartInfo
        {
            FileName = scrcpy.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in AdbService.BuildScrcpyArgs(_serial, outputPath, _audioArg, _rec)) psi.ArgumentList.Add(a);
        var adb = ToolResolver.Find(AgentTools.Adb);
        if (adb is not null) psi.Environment["ADB"] = adb.Path;
        if (serverPath is not null) psi.Environment["SCRCPY_SERVER_PATH"] = serverPath;

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var tail = new LineTail(TailLines);
        var marker = new TaskCompletionSource<DateTime>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestedAt = Now;
        Process proc;
        try
        {
            proc = Process.Start(psi)
                ?? throw new InvalidOperationException("scrcpy no pudo iniciar la grabación: no se pudo lanzar el proceso");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("scrcpy no pudo iniciar la grabación: " + ex.Message);
        }

        var capture = new Capture(proc, outputPath, tail, marker, requestedAt);
        void OnLine(string? line)
        {
            if (line is null) return;
            if (!marker.Task.IsCompleted && ScrcpyOutput.IsStartMarker(line)) marker.TrySetResult(Now);
            if (!ScrcpyOutput.IsDebugLine(line)) tail.Add(line);
            if (AdbService.IsScrcpyWarningLine(line)) _log.LogWarning("scrcpy (sesión): {Line}", line);
            else _log.LogDebug("scrcpy (sesión): {Line}", line);
        }
        proc.OutputDataReceived += (_, e) => OnLine(e.Data);
        proc.ErrorDataReceived += (_, e) => OnLine(e.Data);
        proc.EnableRaisingEvents = true;
        proc.Exited += (_, _) => RaiseExitedIfCurrent(capture);
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        return capture;
    }

    // Avisa una salida que no pidió Tatana, una sola vez por captura y solo si es la captura en curso.
    private void RaiseExitedIfCurrent(Capture capture)
    {
        if (capture.StopRequested || !ReferenceEquals(capture, Volatile.Read(ref _current))) return;
        if (Interlocked.Exchange(ref capture.ExitNotified, 1) != 0) return;
        _log.LogWarning("scrcpy de la sesión terminó solo: {Detail}",
            AdbService.ScrcpyErrorSummary(capture.Tail) ?? "sin detalle");
        try { Exited?.Invoke(new TrackExit(capture.OutputPath, Now)); }
        catch (Exception ex) { _log.LogWarning(ex, "Error al avisar la salida de scrcpy"); }
    }

    // La captura pasa a ser la "en curso". Si terminó justo entre el chequeo de arranque y este punto, el evento Exited
    // ya se descartó (todavía no era la actual): se avisa acá.
    private void MakeCurrent(Capture c)
    {
        Volatile.Write(ref _current, c);
        if (AdbService.HasExitedSafe(c.Proc)) RaiseExitedIfCurrent(c);
    }

    // Marcador (o salida temprana) → inicio real. Si no llega a tiempo, 504; si scrcpy murió, su error.
    private async Task<TrackStart> WaitStartedAsync(Capture c, TimeSpan earlyExitWindow)
    {
        var exited = c.Proc.WaitForExitAsync(CancellationToken.None);
        DateTime startedAt;
        if (_rec.RequireStartMarker)
        {
            await Task.WhenAny(c.Marker.Task, exited, Task.Delay(TimeSpan.FromSeconds(_rec.StartTimeoutSeconds)));
            if (!c.Marker.Task.IsCompleted && !AdbService.HasExitedSafe(c.Proc))
            {
                var summary = AdbService.ScrcpyErrorSummary(c.Tail);
                throw new AgentHttpException(504, AgentErrorCodes.RecordingStartTimeout,
                    "El celular no empezó a grabar a tiempo. Desbloquealo, dejalo con la pantalla encendida y volvé a intentar." +
                    (summary is null ? "" : " Detalle de scrcpy: " + summary));
            }
            if (c.Marker.Task.IsCompleted) await Task.WhenAny(exited, Task.Delay(earlyExitWindow));
            startedAt = c.Marker.Task.IsCompleted ? c.Marker.Task.Result : c.RequestedAt;
        }
        else
        {
            await Task.WhenAny(exited, Task.Delay(StartEarlyExitWindow));
            startedAt = c.Marker.Task.IsCompleted ? c.Marker.Task.Result : c.RequestedAt + NoMarkerStartOffset;
        }
        if (AdbService.HasExitedSafe(c.Proc))
        {
            try { c.Proc.WaitForExit(); } catch { /* ya terminó */ }
            var detail = AdbService.ScrcpyErrorSummary(c.Tail) ?? "código de salida " + SafeExitCode(c.Proc);
            throw new InvalidOperationException("scrcpy no pudo iniciar la grabación: " + detail);
        }
        DateTime? procStart = null;
        try { procStart = c.Proc.StartTime.ToUniversalTime(); } catch { /* sin dato: la recuperación no lo cierra */ }
        return new TrackStart(startedAt, c.Proc.Id, procStart);
    }

    private async Task AbortAsync(Capture c)
    {
        c.StopRequested = true;
        try
        {
            if (!AdbService.HasExitedSafe(c.Proc))
                await ProcessStop.StopAsync(c.Proc, AbortTimeout, _log, "scrcpy", CancellationToken.None, AbortEscalate,
                    ProcessStop.SessionSignal);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Error deteniendo scrcpy"); }
        c.Proc.Dispose();
        AdbService.TryDelete(c.OutputPath);
    }

    private async Task<TrackStop> StopCaptureAsync(Capture c)
    {
        c.StopRequested = true;
        var requested = Now;
        var result = new ProcessStopResult(true, false);
        try
        {
            result = await ProcessStop.StopAsync(c.Proc, TimeSpan.FromSeconds(_rec.StopTimeoutSeconds), _log, "scrcpy",
                CancellationToken.None, _rec.SigtermAfterSeconds > 0 ? TimeSpan.FromSeconds(_rec.SigtermAfterSeconds) : null,
                ProcessStop.SessionSignal);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Error deteniendo scrcpy de la sesión");
            result = new ProcessStopResult(false, false);
        }
        finally
        {
            c.Proc.Dispose();
        }
        return new TrackStop(requested, result.Clean);
    }

    public async Task<TrackStart> StartAsync(string outputPath)
    {
        if (_current is not null) throw new InvalidOperationException("La pista ya está grabando");
        var c = Launch(outputPath);
        try
        {
            var s = await WaitStartedAsync(c, StartEarlyExitWindow);
            MakeCurrent(c);
            return s;
        }
        catch
        {
            await AbortAsync(c);
            throw;
        }
    }

    public async Task<(TrackStart Next, TrackStop Previous)> RotateAsync(string nextPath, Func<TrackStart, Task> onNextStarted)
    {
        var prev = _current ?? throw new InvalidOperationException("La pista no está grabando");
        // Post-review #80: el anterior se cierra recién cuando el nuevo sigue vivo durante la ventana de confirmación;
        // si se cae, se reintenta (con tope) sin tocar el anterior.
        var (next, started) = await RotationStarter.StartConfirmedAsync(
            () => Launch(nextPath),
            c => WaitStartedAsync(c, RotateEarlyExitWindow),
            c => AdbService.HasExitedSafe(c.Proc),
            AbortAsync,
            _rotationConfirm, _rotationAttempts, RotateRetryDelay, _log);
        MakeCurrent(next);
        try { await onNextStarted(started); }
        catch (Exception ex) { _log.LogError(ex, "No se pudo registrar el tramo nuevo en el diario"); }
        var stop = await StopCaptureAsync(prev);
        return (started, stop);
    }

    public async Task<TrackStop> StopAsync()
    {
        var c = Interlocked.Exchange(ref _current, null);
        if (c is null) return new TrackStop(Now, true);
        return await StopCaptureAsync(c);
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; }
        catch (InvalidOperationException) { return -1; }
    }

    public void Dispose()
    {
        var c = Interlocked.Exchange(ref _current, null);
        if (c is null) return;
        c.StopRequested = true;
        try { c.Proc.Dispose(); } catch { /* best effort */ }
    }
}

/// <summary>
/// Pista simulada (§5.15, <c>Agent:Mock=true</c>): no lanza scrcpy. Al cerrar cada tramo genera el MKV con lavfi
/// (<c>testsrc2</c>, duración = la grabada) o, sin ffmpeg, el MKV embebido.
/// </summary>
public sealed class MockScreenTrack(string dataDirectory, ILogger log, TimeProvider time) : ISessionTrack
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromMilliseconds(300);
    private string? _currentPath;
    private DateTime _currentStart;

    public event Action<TrackExit>? Exited { add { } remove { } }

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public async Task<TrackStart> StartAsync(string outputPath)
    {
        await Task.Delay(StartDelay);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        _currentPath = outputPath;
        _currentStart = Now;
        _ = dataDirectory;
        return new TrackStart(_currentStart, null, null);
    }

    public async Task<(TrackStart Next, TrackStop Previous)> RotateAsync(string nextPath, Func<TrackStart, Task> onNextStarted)
    {
        var prevPath = _currentPath ?? throw new InvalidOperationException("La pista no está grabando");
        var prevStart = _currentStart;
        var next = await StartAsync(nextPath);
        await onNextStarted(next);
        var requested = Now;
        await WriteMockAsync(prevPath, (requested - prevStart).TotalSeconds);
        return (next, new TrackStop(requested, true));
    }

    public async Task<TrackStop> StopAsync()
    {
        var path = Interlocked.Exchange(ref _currentPath, null);
        var requested = Now;
        if (path is not null) await WriteMockAsync(path, (requested - _currentStart).TotalSeconds);
        return new TrackStop(requested, true);
    }

    private async Task WriteMockAsync(string path, double seconds)
    {
        var ffmpeg = ToolResolver.Find(AgentTools.Ffmpeg)?.Path;
        if (ffmpeg is null || !await AdbService.GenerateMockVideoAsync(ffmpeg, path, seconds, log))
            await File.WriteAllBytesAsync(path, AdbService.MockMkvBytes(), CancellationToken.None);
    }

    public void Dispose() { }
}

/// <summary>
/// Arranque confirmado del tramo nuevo en un corte solapado (post-review #80). Lanza, espera el marcador y exige que el
/// proceso siga vivo durante <c>confirm</c>; si se cae (o no arranca), lo aborta y reintenta hasta <c>attempts</c> veces.
/// Nunca toca el tramo anterior. No se exige que el archivo crezca: scrcpy escribe por bloques de 256 KiB y con la
/// pantalla quieta el MKV queda en 0 B durante el primer minuto (§4.2.5), así que "vivo" es la única señal confiable.
/// </summary>
internal static class RotationStarter
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);

    public static async Task<(T Capture, TrackStart Started)> StartConfirmedAsync<T>(
        Func<T> launch, Func<T, Task<TrackStart>> waitStarted, Func<T, bool> hasExited, Func<T, Task> abort,
        TimeSpan confirm, int attempts, TimeSpan retryDelay, ILogger log)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= Math.Max(1, attempts); attempt++)
        {
            if (attempt > 1 && retryDelay > TimeSpan.Zero) await Task.Delay(retryDelay);
            T capture;
            try { capture = launch(); }
            catch (Exception ex)
            {
                last = ex;
                log.LogWarning(ex, "Corte de la sesión: no se pudo lanzar el tramo nuevo (intento {N} de {Max})", attempt, attempts);
                continue;
            }
            TrackStart started;
            try
            {
                started = await waitStarted(capture);
            }
            catch (Exception ex)
            {
                last = ex;
                await abort(capture);
                log.LogWarning("Corte de la sesión: el tramo nuevo no arrancó (intento {N} de {Max}): {Error}", attempt,
                    attempts, ex.Message);
                continue;
            }
            var died = false;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.Elapsed < confirm)
            {
                if (hasExited(capture)) { died = true; break; }
                var left = confirm - watch.Elapsed;
                await Task.Delay(left < Poll ? left : Poll);
            }
            if (!died && hasExited(capture)) died = true;
            if (!died) return (capture, started);
            last = new InvalidOperationException(
                $"scrcpy terminó {watch.Elapsed.TotalSeconds:0.0} s después de empezar a grabar el tramo nuevo");
            await abort(capture);
            log.LogWarning("Corte de la sesión: el tramo nuevo terminó a los {Ms} ms del marcador (intento {N} de {Max}); el anterior sigue grabando",
                (long)watch.Elapsed.TotalMilliseconds, attempt, attempts);
        }
        throw last ?? new InvalidOperationException("No se pudo abrir el tramo nuevo");
    }
}
