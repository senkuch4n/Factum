using System.Diagnostics;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services;

public interface IAdbService
{
    Task EnsureServerAsync(CancellationToken ct = default);
    Task<List<Device>> ListDevicesAsync(CancellationToken ct = default);
    Task<string> TakeScreenshotAsync(string serial, string outputPath, CancellationToken ct = default);
    /// <summary>
    /// Vuelve recién cuando scrcpy confirmó que el celular empezó a capturar (grabacion-android-robusta §5.4).
    /// Errores tipados como <see cref="AgentHttpException"/>: 409 <c>already_recording</c>, 504
    /// <c>recording_start_timeout</c>. El trabajo con scrcpy no depende de <paramref name="ct"/> (DA4).
    /// </summary>
    Task<AndroidStartResult> StartRecordingAsync(string serial, string outputPath, int androidVersion, bool withMic,
        string? caseId, CancellationToken ct = default);
    /// <summary>
    /// Cierra, valida y devuelve la grabación (ok/incomplete). 422 <c>recording_empty</c> /
    /// <c>recording_unreadable</c> como <see cref="AgentHttpException"/>; sin grabación,
    /// <see cref="RecordingNotActiveException"/>. No depende de <paramref name="ct"/> (DA4).
    /// </summary>
    Task<AndroidStopResult> StopRecordingAsync(string serial, CancellationToken ct = default);
    Task<List<AndroidFileEntry>> ListDirectoryAsync(string serial, string path, CancellationToken ct = default);
    Task PullFileAsync(string serial, string devicePath, string localOutputPath, CancellationToken ct = default);
    Task<List<AndroidFileEntry>> FindByAppAsync(string serial, string app, CancellationToken ct = default);
}

/// <summary>Start de Android: hora real de inicio de la captura (marcador de scrcpy).</summary>
public sealed record AndroidStartResult(DateTimeOffset StartedAt);

/// <summary>Stop de Android con validación ok o incomplete (las otras dos se lanzan como 422).</summary>
public sealed record AndroidStopResult(string OutputPath, string Serial, string? CaseId, RecordingValidationDto Validation);

internal enum RecordingPhase { Starting, Recording, Stopping }

/// <summary>Estado inmutable de la grabación de Android; se reemplaza entero (Volatile.Write).</summary>
internal sealed record RecordingSnapshot(string Serial, string OutputPath, string? CaseId, bool WithMic,
    RecordingPhase Phase, DateTimeOffset RequestedAt, DateTimeOffset? StartedAt, Process? Process);

public sealed class AdbService : IAdbService, IOperationSource, IRecordingStateSource
{
    // tatana-linux §5.4: texto de "adb no encontrado". En Linux, la pantalla Librerías no existe empaquetada.
    internal static string AdbMissingMessage(HostOs os) => os == HostOs.Linux
        ? "No se encontró adb, el componente de Android de Tatana. Descargá Tatana de nuevo desde la página de Factum."
        : "ADB no instalado — instalalo desde la sección Librerías de Tatana";

    // adb: tools/platform-tools junto al exe (portátil), PATH o Homebrew (ToolResolver). Si no
    // aparece, queda el nombre pelado "adb": ProcessRunner devuelve -1 y se mantienen los
    // mensajes de "ADB no instalado".
    private static readonly Lazy<string> AdbPath =
        new(() => ToolResolver.Find(AgentTools.Adb)?.Path ?? "adb");

    // Textos de error de grabación (SDD grabacion-android-windows §4.3 y grabacion-android-robusta §5.4).
    // El client los muestra.
    private const string ErrorScrcpyMissing =
        "No se encontró el componente de grabación de Android (scrcpy). Actualizá o reinstalá Tatana con el paquete de Factum.";
    private const string ErrorAlreadyRecording = "Ya hay una grabación de Android en curso.";
    private const string ErrorStartTimeout =
        "El celular no empezó a grabar a tiempo. Desbloquealo, dejalo con la pantalla encendida y volvé a intentar.";
    private const string ErrorRecordingEmpty = "La grabación no se guardó: el archivo quedó vacío. Volvé a grabar.";
    private const string ErrorRecordingUnreadable =
        "La grabación no se guardó: el archivo no se puede leer como video. Tatana guardó una copia en la carpeta «descartes» para revisión. Volvé a grabar.";

    internal const string DiscardDirName = "descartes";
    private const int TailLines = 40;

    private static readonly TimeSpan ScrcpyEarlyExitWindow = TimeSpan.FromMilliseconds(1500);
    // Sin marcador (RequireStartMarker = false): inicio estimado = lanzamiento + 1 s (criterio anterior).
    private static readonly TimeSpan NoMarkerStartOffset = TimeSpan.FromSeconds(1);
    // Corte de un arranque que no llegó a grabar (§5.4 paso 6).
    private static readonly TimeSpan StartAbortTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StartAbortEscalate = TimeSpan.FromSeconds(2);

    private readonly bool _mock;
    private readonly string? _micDevice;
    private readonly string _dataDir;
    private readonly RecordingOptions _rec;
    private readonly string? _mockRecordingSource;
    private readonly string? _mockRecordingOutcome;
    private readonly ILogger<AdbService> _log;
    // grabacion-android-robusta §5.4: lo leen /agent/state y ActiveOperations sin tomar _recordLock.
    private RecordingSnapshot? _snap;
    private MicCapture? _mic;
    // Últimas líneas de scrcpy (sin las DEBUG), para los errores. Se reemplaza en cada start.
    private LineTail _scrcpyTail = new(TailLines);
    private readonly SemaphoreSlim _recordLock = new(1, 1);

    public AdbService(IOptions<AgentOptions> opts, ILogger<AdbService> log)
    {
        _mock      = opts.Value.Mock;
        _micDevice = opts.Value.MicDevice;
        _dataDir   = Path.GetFullPath(opts.Value.DataDirectory);
        _rec       = (opts.Value.Recording ?? new RecordingOptions()).Clone().Normalize(log);
        _mockRecordingSource  = opts.Value.MockAndroidRecordingSource;
        _mockRecordingOutcome = opts.Value.MockAndroidRecordingOutcome?.Trim().ToLowerInvariant();
        _log       = log;
    }

    // ── Operaciones y grabaciones en curso ───────────────────────────────────
    // Una grabación activa bloquea "Reiniciar y actualizar" (tatana-instalador-autoupdate §5.3). Si scrcpy ya
    // terminó solo (celular desconectado) y la fase es Recording, no cuenta: no hay nada que cortar.
    // Starting y Stopping cuentan siempre (grabacion-android-robusta §5.4).
    private RecordingSnapshot? VisibleSnapshot()
    {
        var s = Volatile.Read(ref _snap);
        if (s is null) return null;
        if (s.Phase == RecordingPhase.Recording && !_mock && (s.Process is null || HasExitedSafe(s.Process)))
            return null;
        return s;
    }

    public IEnumerable<AgentOperation> ActiveOperations =>
        VisibleSnapshot() is { } s ? [new AgentOperation("recording_android", s.StartedAt ?? s.RequestedAt)] : [];

    public IEnumerable<RecordingStateDto> ActiveRecordings =>
        VisibleSnapshot() is { } s
            ? [new RecordingStateDto("android", s.Serial, PhaseName(s.Phase), s.RequestedAt,
                s.Phase == RecordingPhase.Starting ? null : s.StartedAt,
                Path.GetFileName(s.OutputPath), s.CaseId, s.WithMic, null)]
            : [];

    internal static string PhaseName(RecordingPhase p) => p switch
    {
        RecordingPhase.Starting => "starting",
        RecordingPhase.Stopping => "stopping",
        _                       => "recording",
    };

    public async Task EnsureServerAsync(CancellationToken ct = default)
    {
        if (_mock) return;
        var r = await ProcessRunner.RunAsync(AdbPath.Value, "start-server", ct);
        if (!r.Success) _log.LogWarning("adb start-server: {Stderr}", r.Stderr);
    }

    public async Task<List<Device>> ListDevicesAsync(CancellationToken ct = default)
    {
        if (_mock) return [MockDevice()];

        var r       = await ProcessRunner.RunAsync(AdbPath.Value, "devices -l", ct);
        var devices = new List<Device>();

        foreach (var line in r.Stdout.Split('\n').Skip(1))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("*")) continue;

            var parts = trimmed.Split(null as char[], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[1] != "device") continue;

            var device = new Device { Serial = parts[0], State = "device", Platform = "android" };
            await EnrichDeviceAsync(device, ct);
            devices.Add(device);
        }

        return devices;
    }

    public async Task<string> TakeScreenshotAsync(string serial, string outputPath,
        CancellationToken ct = default)
    {
        if (_mock)
        {
            await File.WriteAllBytesAsync(outputPath, MockImageBytes(), ct);
            return outputPath;
        }

        // screencap -p emite PNG binario a stdout — leer como stream binario
        var exit = await ProcessRunner.RunBinaryToFileAsync(AdbPath.Value,
            ["-s", serial, "exec-out", "screencap", "-p"], outputPath, ct);
        if (exit == -1) throw new InvalidOperationException(AdbMissingMessage(HostOsInfo.Current));
        if (exit != 0) throw new InvalidOperationException($"screencap falló (exit {exit})");

        return outputPath;
    }

    // ── Grabación con scrcpy ──────────────────────────────────────────────────
    //
    // scrcpy graba directo al archivo local (no hay adb pull). Formato MKV.
    // grabacion-android-robusta §3: con --no-video-playback, scrcpy 4.0 ignora SIGINT/SIGTERM y siempre
    // terminaba en kill, que pierde el buffer del muxer (minutos de video con poco movimiento). Con
    // --no-window cierra limpio en < 1 s. Si Tatana heredó SIGINT ignorado, se escala a SIGTERM. El kill +
    // remux queda como último recurso y toda grabación pasa por la validación antes de devolverse.
    // Audio interno del dispositivo disponible con scrcpy v2+ y Android 11+.

    /// <summary>Argumentos de scrcpy (puro, testeable).</summary>
    internal static List<string> BuildScrcpyArgs(string serial, string outputPath, string audioArg, RecordingOptions o)
    {
        var args = new List<string>
        {
            "--serial", serial,
            "--record", outputPath,
            "--record-format", "mkv",
            o.WindowMode == RecordingOptions.WindowModeNoVideoPlayback ? "--no-video-playback" : "--no-window",
            "--max-size", "1080",
            "--video-bit-rate", "4M",
            audioArg,
        };
        if (o.StayAwake) args.Add("--stay-awake");
        return args;
    }

    // already_recording: hay un start o un stop en curso, o una grabación con scrcpy vivo.
    private bool IsBusy(RecordingSnapshot? s) =>
        s is not null && (s.Phase != RecordingPhase.Recording || _mock || (s.Process is not null && !HasExitedSafe(s.Process)));

    private static AgentHttpException AlreadyRecording() =>
        new(409, AgentErrorCodes.AlreadyRecording, ErrorAlreadyRecording);

    public async Task<AndroidStartResult> StartRecordingAsync(string serial, string outputPath,
        int androidVersion, bool withMic, string? caseId, CancellationToken ct = default)
    {
        // Sin esperar el lock: si otro pedido está arrancando o deteniendo, se responde ya.
        if (IsBusy(Volatile.Read(ref _snap))) throw AlreadyRecording();

        await _recordLock.WaitAsync(ct);
        try
        {
            if (IsBusy(Volatile.Read(ref _snap))) throw AlreadyRecording();

            if (_mock)
            {
                var requested = DateTimeOffset.UtcNow;
                Volatile.Write(ref _snap, new RecordingSnapshot(serial, outputPath, caseId, withMic,
                    RecordingPhase.Starting, requested, null, null));
                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None); // así se ve "Iniciando…"
                var started = DateTimeOffset.UtcNow;
                Volatile.Write(ref _snap, new RecordingSnapshot(serial, outputPath, caseId, withMic,
                    RecordingPhase.Recording, requested, started, null));
                _log.LogInformation("Grabación Android (mock) iniciada: {Path} caso={CaseId}", outputPath, caseId);
                return new AndroidStartResult(started);
            }

            await DiscardStaleRecordingAsync();
            return await StartScrcpyAsync(serial, outputPath, androidVersion, withMic, caseId);
        }
        finally { _recordLock.Release(); }
    }

    // DA4: todo con CancellationToken.None. Si el navegador recarga en "Iniciando…", la grabación igual arranca
    // y se recupera por /agent/state.
    private async Task<AndroidStartResult> StartScrcpyAsync(string serial, string outputPath, int androidVersion,
        bool withMic, string? caseId)
    {
        // E1: scrcpy (y, en el portátil, su scrcpy-server al lado).
        var scrcpy = ToolResolver.Find(AgentTools.Scrcpy)
            ?? throw new InvalidOperationException(ScrcpyMissingMessage());
        string? serverPath = null;
        if (scrcpy.Source == ToolSource.Portable)
        {
            serverPath = Path.Combine(Path.GetDirectoryName(scrcpy.Path)!, "scrcpy-server");
            if (!File.Exists(serverPath))
                throw new InvalidOperationException(ScrcpyMissingMessage());
        }

        // Con mic de PC activo, no tiene sentido además intentar el audio de sistema de scrcpy (que de todos
        // modos falla en silencio para contenido protegido como las notas de voz de WhatsApp) — una sola fuente
        // de audio limpia, mismo criterio que el modo with_mic de iOS.
        // Sin --no-audio-playback: el audio del celular se sigue escuchando en la PC (D12 b).
        var audioArg = withMic
            ? "--no-audio"
            : androidVersion >= 11 ? "--audio-source=output" : "--no-audio";

        // ArgumentList (no Arguments): la ruta del perfil puede tener espacios o acentos.
        // UseShellExecute=false + CreateNoWindow=true le da a scrcpy su propia consola oculta, que es lo que
        // permite el Ctrl+C de ProcessStop en Windows. No usar CreateNewProcessGroup: deshabilitaría el Ctrl+C.
        var psi = new ProcessStartInfo
        {
            FileName               = scrcpy.Path,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        var args = BuildScrcpyArgs(serial, outputPath, audioArg, _rec);
        foreach (var a in args) psi.ArgumentList.Add(a);

        // D3: un solo servidor adb. Sin esto scrcpy usa su adb propio, de otra versión, y
        // mata/relanza el servidor de Tatana ("adb server version doesn't match").
        var adb = ToolResolver.Find(AgentTools.Adb);
        if (adb is not null) psi.Environment["ADB"] = adb.Path;
        if (serverPath is not null) psi.Environment["SCRCPY_SERVER_PATH"] = serverPath;

        var tail = new LineTail(TailLines);
        _scrcpyTail = tail;
        var marker = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        var launch = Stopwatch.StartNew();
        var requestedAt = DateTimeOffset.UtcNow;
        var micPath = outputPath + ".mic.mka";

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

        // Sin drenar stdout/stderr, el pipe del SO se llena con el log de scrcpy y el proceso se bloquea
        // escribiendo → la grabación se congela en silencio. Además, la línea del servidor
        // "[server] INFO: Device: …" llega en tiempo real y marca el inicio real de la captura (DA3).
        void OnLine(string? line)
        {
            if (line is null) return;
            if (!marker.Task.IsCompleted && ScrcpyOutput.IsStartMarker(line))
            {
                marker.TrySetResult(DateTimeOffset.UtcNow);
                _log.LogDebug("scrcpy: marcador de inicio a los {Ms} ms", launch.ElapsedMilliseconds);
            }
            if (!ScrcpyOutput.IsDebugLine(line)) tail.Add(line);
            if (IsScrcpyWarningLine(line)) _log.LogWarning("scrcpy: {Line}", line);
            else _log.LogDebug("scrcpy [+{Ms} ms]: {Line}", launch.ElapsedMilliseconds, line);
        }
        proc.OutputDataReceived += (_, e) => OnLine(e.Data);
        proc.ErrorDataReceived  += (_, e) => OnLine(e.Data);
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        Volatile.Write(ref _snap, new RecordingSnapshot(serial, outputPath, caseId, withMic,
            RecordingPhase.Starting, requestedAt, null, proc));

        var ok = false;
        MicCapture? mic = null;
        try
        {
            var exited = proc.WaitForExitAsync(CancellationToken.None);
            DateTimeOffset startedAt;
            Task<MicCapture>? micTask = null;

            if (_rec.RequireStartMarker)
            {
                await Task.WhenAny(marker.Task, exited, Task.Delay(TimeSpan.FromSeconds(_rec.StartTimeoutSeconds)));
                if (!marker.Task.IsCompleted && !HasExitedSafe(proc))
                {
                    // Timeout sin marcador: se corta scrcpy, se borra SOLO lo de este intento y 504.
                    _log.LogWarning("scrcpy no confirmó el inicio en {S} s; se cancela el intento", _rec.StartTimeoutSeconds);
                    await StopQuietlyAsync(proc, StartAbortTimeout, StartAbortEscalate);
                    var summary = ScrcpyErrorSummary(tail);
                    throw new AgentHttpException(504, AgentErrorCodes.RecordingStartTimeout,
                        ErrorStartTimeout + (summary is null ? "" : " Detalle de scrcpy: " + summary));
                }
                if (marker.Task.IsCompleted)
                {
                    // El mic arranca ya (alineado con el inicio real) y en paralelo se mira que scrcpy siga vivo.
                    if (withMic) micTask = MicCapture.StartAsync(micPath, _micDevice, _log, CancellationToken.None);
                    await Task.WhenAny(exited, Task.Delay(ScrcpyEarlyExitWindow));
                }
                startedAt = marker.Task.IsCompleted ? marker.Task.Result : requestedAt;
            }
            else
            {
                // Salida de emergencia: criterio anterior ("vivo a los 1,5 s").
                await Task.WhenAny(exited, Task.Delay(ScrcpyEarlyExitWindow));
                if (marker.Task.IsCompleted) startedAt = marker.Task.Result;
                else
                {
                    startedAt = requestedAt + NoMarkerStartOffset;
                    _log.LogWarning("scrcpy: sin marcador de inicio (RequireStartMarker = false); inicio estimado");
                }
                if (withMic && !HasExitedSafe(proc))
                    micTask = MicCapture.StartAsync(micPath, _micDevice, _log, CancellationToken.None);
            }

            // E2 (DP4): si scrcpy murió, el start falla con su error real.
            if (HasExitedSafe(proc))
            {
                if (micTask is not null) await StopMicQuietlyAsync(micTask);
                proc.WaitForExit(); // termina de vaciar stdout/stderr
                var code = SafeExitCode(proc);
                var detail = ScrcpyErrorSummary(tail) ?? $"código de salida {code}";
                throw new InvalidOperationException("scrcpy no pudo iniciar la grabación: " + detail);
            }

            // DP6 A: con mic, si el mic no arranca (E4/E5/E6), no se graba. El error del mic se relanza.
            if (micTask is not null)
            {
                try { mic = await micTask; }
                catch
                {
                    await StopQuietlyAsync(proc, TimeSpan.FromSeconds(_rec.StopTimeoutSeconds), SigtermAfter());
                    throw;
                }
            }

            _mic = mic;
            Volatile.Write(ref _snap, new RecordingSnapshot(serial, outputPath, caseId, withMic,
                RecordingPhase.Recording, requestedAt, startedAt, proc));
            ok = true;
            _log.LogInformation(
                "scrcpy grabando: serial={Serial} audio={Audio} mic={Mic} caso={CaseId} inicio real a los {Ms} ms adb={Adb} args={Args} output={Path}",
                serial, audioArg, withMic, caseId, (long)(startedAt - requestedAt).TotalMilliseconds,
                adb?.Path ?? "(no encontrado)", string.Join(' ', args.Where(a => a != outputPath)), outputPath);
            return new AndroidStartResult(startedAt);
        }
        finally
        {
            if (!ok)
            {
                if (!HasExitedSafe(proc))
                    await StopQuietlyAsync(proc, StartAbortTimeout, StartAbortEscalate);
                proc.Dispose();
                Volatile.Write(ref _snap, null);
                _mic = null;
                // Solo lo que creó este intento.
                TryDelete(outputPath);
                TryDelete(micPath);
            }
        }
    }

    private TimeSpan? SigtermAfter() =>
        _rec.SigtermAfterSeconds > 0 ? TimeSpan.FromSeconds(_rec.SigtermAfterSeconds) : null;

    private async Task StopQuietlyAsync(Process proc, TimeSpan timeout, TimeSpan? escalate)
    {
        try { await ProcessStop.StopAsync(proc, timeout, _log, "scrcpy", CancellationToken.None, escalate); }
        catch (Exception ex) { _log.LogWarning(ex, "Error deteniendo scrcpy"); }
    }

    private async Task StopMicQuietlyAsync(Task<MicCapture> micTask)
    {
        try { await (await micTask).StopAsync(); }
        catch { /* el mic no llegó a abrir: no hay nada que detener */ }
    }

    public async Task<AndroidStopResult> StopRecordingAsync(string serial, CancellationToken ct = default)
    {
        // DA4: ni el lock ni el trabajo dependen del request. Si hay un start en curso, se espera a que termine.
        await _recordLock.WaitAsync(CancellationToken.None);
        RecordingSnapshot? snap = null;
        try
        {
            snap = Volatile.Read(ref _snap) ?? throw new RecordingNotActiveException();
            Volatile.Write(ref _snap, snap with { Phase = RecordingPhase.Stopping });
            return _mock ? await StopMockAsync(snap) : await StopScrcpyAsync(snap);
        }
        finally
        {
            Volatile.Write(ref _snap, null);
            snap?.Process?.Dispose();
            _recordLock.Release();
        }
    }

    private async Task<AndroidStopResult> StopScrcpyAsync(RecordingSnapshot snap)
    {
        var total = Stopwatch.StartNew();
        var stoppedAt = DateTimeOffset.UtcNow;
        var startedAt = snap.StartedAt ?? snap.RequestedAt;
        var recorded = (stoppedAt - startedAt).TotalSeconds;
        var outPath = snap.OutputPath;
        var mic = _mic;
        var tail = _scrcpyTail;

        var stop = new ProcessStopResult(true, false);
        var flush = Stopwatch.StartNew();
        try
        {
            if (snap.Process is { } proc)
                stop = await ProcessStop.StopAsync(proc, TimeSpan.FromSeconds(_rec.StopTimeoutSeconds), _log, "scrcpy",
                    CancellationToken.None, SigtermAfter());
        }
        finally
        {
            flush.Stop();
            // El mic se detiene siempre, aunque el MKV no exista.
            _mic = null;
            if (mic is not null) await mic.StopAsync();
        }
        var forced = !stop.Clean;

        // Último recurso: tras un kill, el MKV puede quedar sin duración ni índice.
        var size = FileSize(outPath);
        if (forced && size > 0)
        {
            await RemuxAsync(outPath, CancellationToken.None);
            size = FileSize(outPath);
        }

        var ffmpeg = ToolResolver.Find(AgentTools.Ffmpeg)?.Path;
        if (ffmpeg is null)
            _log.LogWarning("Validación de la grabación sin ffmpeg: no se puede medir la duración (DA6)");
        FfmpegProbeResult? probe = size > 0 && ffmpeg is not null
            ? await FfmpegProbe.ProbeAsync(ffmpeg, outPath, CancellationToken.None)
            : null;

        // DA7: la validación es sobre el video, antes de mezclar el mic.
        var (status, duration, durationChecked) = RecordingValidation.Evaluate(size, probe, recorded, _rec);
        var validation = RecordingValidationDto.Create(status, recorded, duration, durationChecked, forced,
            startedAt, stoppedAt);

        if (status is RecordingValidationStatus.Ok or RecordingValidationStatus.Incomplete &&
            mic is not null && File.Exists(mic.AudioPath) && ffmpeg is not null)
            await MuxMicAudioIntoVideoAsync(ffmpeg, outPath, mic.AudioPath, duration);

        _log.LogInformation(
            "Grabación Android detenida: serial={Serial} forced={Forced} escalated={Escalated} recorded_seconds={Recorded:0.0} duration_seconds={Duration} status={Status} size={Size} flush_ms={Flush} stop_ms={Total}",
            snap.Serial, forced, stop.Escalated, recorded, duration?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "-",
            status, size, flush.ElapsedMilliseconds, total.ElapsedMilliseconds);

        return Finish(snap, outPath, mic?.AudioPath, validation, ScrcpyErrorSummary(tail));
    }

    // empty → borra lo de este stop y 422; unreadable → mueve a descartes/ y 422; ok/incomplete → resultado.
    private AndroidStopResult Finish(RecordingSnapshot snap, string outPath, string? micPath,
        RecordingValidationDto validation, string? scrcpyDetail)
    {
        var extra = new Dictionary<string, object?> { ["validation"] = validation };
        switch (validation.Status)
        {
            case RecordingValidationStatus.Empty:
                TryDelete(outPath);
                if (micPath is not null) TryDelete(micPath);
                throw WithRecordingData(new AgentHttpException(422, AgentErrorCodes.RecordingEmpty,
                    ErrorRecordingEmpty + (scrcpyDetail is null ? "" : " Detalle de scrcpy: " + scrcpyDetail), extra), snap);
            case RecordingValidationStatus.Unreadable:
                var moved = MoveToDiscards(outPath);
                if (micPath is not null && File.Exists(micPath)) MoveToDiscards(micPath);
                _log.LogWarning("Grabación ilegible movida a descartes: {Path}", moved ?? outPath);
                throw WithRecordingData(new AgentHttpException(422, AgentErrorCodes.RecordingUnreadable,
                    ErrorRecordingUnreadable, extra), snap);
            default:
                return new AndroidStopResult(outPath, snap.Serial, snap.CaseId, validation);
        }
    }

    /// <summary>Claves de <see cref="Exception.Data"/> con el serial y el caso de una grabación fallida (no van al cuerpo HTTP).</summary>
    internal const string ExDataSerial = "recording_serial", ExDataCaseId = "recording_case_id";

    private static AgentHttpException WithRecordingData(AgentHttpException ex, RecordingSnapshot snap)
    {
        ex.Data[ExDataSerial] = snap.Serial;
        ex.Data[ExDataCaseId] = snap.CaseId;
        return ex;
    }

    // ── Mock (DA12): nunca un archivo de 0 B como grabación ─────────────────────

    private async Task<AndroidStopResult> StopMockAsync(RecordingSnapshot snap)
    {
        var stoppedAt = DateTimeOffset.UtcNow;
        var startedAt = snap.StartedAt ?? snap.RequestedAt;
        var recorded = (stoppedAt - startedAt).TotalSeconds;
        var outPath = snap.OutputPath;
        var ffmpeg = ToolResolver.Find(AgentTools.Ffmpeg)?.Path;

        if (!string.IsNullOrWhiteSpace(_mockRecordingSource) && File.Exists(_mockRecordingSource))
            File.Copy(_mockRecordingSource, outPath, overwrite: true);
        else if (ffmpeg is null || !await GenerateMockVideoAsync(ffmpeg, outPath, recorded))
            await File.WriteAllBytesAsync(outPath, MockMkvBytes(), CancellationToken.None);

        RecordingValidationDto validation;
        switch (_mockRecordingOutcome)
        {
            case RecordingValidationStatus.Empty:
                validation = RecordingValidationDto.Create(RecordingValidationStatus.Empty, recorded, null, false, false,
                    startedAt, stoppedAt);
                break;
            case RecordingValidationStatus.Incomplete:
                validation = RecordingValidationDto.Create(RecordingValidationStatus.Incomplete, recorded, recorded / 2,
                    true, false, startedAt, stoppedAt);
                break;
            default:
                if (_mockRecordingOutcome == RecordingValidationStatus.Unreadable)
                    await File.WriteAllBytesAsync(outPath, System.Security.Cryptography.RandomNumberGenerator.GetBytes(1024),
                        CancellationToken.None);
                var size = FileSize(outPath);
                var probe = size > 0 && ffmpeg is not null
                    ? await FfmpegProbe.ProbeAsync(ffmpeg, outPath, CancellationToken.None)
                    : null;
                var (status, duration, durationChecked) = RecordingValidation.Evaluate(size, probe, recorded, _rec);
                if (_mockRecordingOutcome == RecordingValidationStatus.Unreadable) status = RecordingValidationStatus.Unreadable;
                validation = RecordingValidationDto.Create(status, recorded, duration, durationChecked, false,
                    startedAt, stoppedAt);
                break;
        }
        _log.LogInformation("Grabación Android (mock) detenida: status={Status} recorded_seconds={Recorded:0.0} output={Path}",
            validation.Status, recorded, outPath);
        return Finish(snap, outPath, null, validation, null);
    }

    private Task<bool> GenerateMockVideoAsync(string ffmpeg, string outPath, double recordedSeconds) =>
        GenerateMockVideoAsync(ffmpeg, outPath, recordedSeconds, _log);

    /// <summary>Video de prueba (lavfi testsrc2) de la duración pedida. También lo usa la grabación de la sesión (mock).</summary>
    internal static async Task<bool> GenerateMockVideoAsync(string ffmpeg, string outPath, double recordedSeconds,
        ILogger log)
    {
        var secs = Math.Max(1, recordedSeconds).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var r = await ProcessRunner.RunArgumentListAsync(ffmpeg,
            ["-hide_banner", "-y", "-f", "lavfi", "-i", "testsrc2=size=360x640:rate=5", "-t", secs,
             "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", outPath],
            CancellationToken.None, timeout: TimeSpan.FromMinutes(5));
        if (r.Success && FileSize(outPath) > 0) return true;
        log.LogWarning("mock: ffmpeg no pudo generar el video de prueba; se usa el MKV embebido: {E}", LastLines(r.Stderr, 3));
        return false;
    }

    // MKV de 1 s, 16x16, H.264 (555 bytes), generado una vez con ffmpeg (lavfi color, libx264, bitexact).
    internal static byte[] MockMkvBytes() =>
        Convert.FromBase64String(
            "GkXfo6NChoEBQveBAULygQRC84EIQoKIbWF0cm9za2FCh4EEQoWBAhhTgGcBAAAAAAAB9xFNm3TAv4ScBwFVTbuLU6uEFUmpZlOs" +
            "gaFNu4tTq4QWVK5rU6yBzE27jFOrhBJUw2dTrIIBYE27jFOrhBxTu2tTrIIB2+wBAAAAAAAAUwAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFUmpZqa/hFqKZDUq" +
            "17GDD0JATYCETGF2ZldBhExhdmZEiYhAj0AAAAAAABZUrmtAjr+EGso9Q64BAAAAAAAAf9eBAXPFiAAAAAAAAAABnIEAIrWcg3Vu" +
            "ZIiBAIaPVl9NUEVHNC9JU08vQVZDg4EBI+ODhDuaygDgkLCBELqBEJqBAlWwhFW5gQFV7oEA7AEAAAAAAAACAABjoqQBQsAK/+EA" +
            "FWdCwAraewEQAAADABAAAAMAIPEiagEABGjOD8gSVMNn17+E+lpsqXNzzmPAi2PFiAAAAAAAAAABZ8iZRaOHRU5DT0RFUkSHjExh" +
            "dmMgbGlieDI2NGfIoUWjiERVUkFUSU9ORIeTMDA6MDA6MDEuMDAwMDAwMDAwAB9DtnWav4RK8vMZ54EAo4+BAACAAAAAB2WIhDom" +
            "KA4cU7trl7+E0Vo0b7uPs4EAt4r3gQHxggG88IEJ");

    // ── Auxiliares de la grabación ─────────────────────────────────────────────

    // Si scrcpy terminó solo (celular desconectado, etc.) el estado viejo queda colgado: se limpia antes de un
    // start nuevo, deteniendo el mic si seguía grabando. El archivo queda en la raíz sin validar (§13).
    private async Task DiscardStaleRecordingAsync()
    {
        var s = Volatile.Read(ref _snap);
        if (s is null && _mic is null) return;
        Volatile.Write(ref _snap, null);
        s?.Process?.Dispose();
        var mic = _mic;
        _mic = null;
        if (mic is not null) await mic.StopAsync();
    }

    /// <summary>
    /// Mezcla el audio del mic con el video (D7): sin la opción que corta al stream más corto, el video nunca se
    /// recorta por el audio. Los -map explícitos dejan solo el video de scrcpy y el audio del mic.
    /// </summary>
    internal static List<string> BuildMicMuxArgs(string video, string audio, string tmp) =>
        ["-hide_banner", "-y", "-i", video, "-i", audio,
         "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy", "-c:a", "aac", "-b:a", "128k", tmp];

    // Reemplaza el video por la mezcla solo si la mezcla tiene video y no es más corta que el video − 1 s
    // (DA7). Si no, queda el video sin el audio del mic.
    private async Task MuxMicAudioIntoVideoAsync(string ffmpeg, string videoPath, string audioPath, double? videoDuration)
    {
        var tmp = videoPath + ".mux.mkv";
        try
        {
            var r = await ProcessRunner.RunArgumentListAsync(ffmpeg, BuildMicMuxArgs(videoPath, audioPath, tmp),
                CancellationToken.None);
            if (!r.Success || FileSize(tmp) <= 0)
            {
                _log.LogWarning("mux audio (mic) falló; se conserva el video sin el audio del mic: {E}", LastLines(r.Stderr, 5));
                TryDelete(tmp);
                return;
            }
            var probe = await FfmpegProbe.ProbeAsync(ffmpeg, tmp, CancellationToken.None);
            if (!probe.HasVideo || (videoDuration is { } vd && (probe.DurationSeconds ?? 0) < vd - 1))
            {
                _log.LogWarning(
                    "la mezcla del micrófono recortaba el video; se conserva el video sin el audio del mic (video {V}, mezcla {M})",
                    videoDuration, probe.DurationSeconds);
                TryDelete(tmp);
                return;
            }
            File.Move(tmp, videoPath, overwrite: true);
            TryDelete(audioPath);
            _log.LogInformation("Audio del micrófono incorporado a la grabación");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "mux audio (mic) falló; se conserva el video sin el audio del mic");
            TryDelete(tmp);
        }
    }

    // Tras un cierre forzado, el MKV puede quedar sin duración ni índice: se reescribe el
    // contenedor sin recodificar. Si falla, queda el MKV crudo (la validación decide).
    private Task RemuxAsync(string outPath, CancellationToken ct) => RemuxFileAsync(outPath, _log, ct);

    /// <summary>Remux sin recodificar (<c>ffmpeg -c copy</c>); true si reemplazó el archivo. También lo usa la sesión.</summary>
    internal static async Task<bool> RemuxFileAsync(string outPath, ILogger log, CancellationToken ct)
    {
        var ffmpeg = ToolResolver.Find(AgentTools.Ffmpeg)?.Path;
        if (ffmpeg is null)
        {
            log.LogWarning("No se encontró ffmpeg para remuxear el MKV tras el cierre forzado; queda el archivo crudo");
            return false;
        }
        var tmp = outPath + ".remux.mkv";
        try
        {
            var r = await ProcessRunner.RunArgumentListAsync(ffmpeg,
                ["-hide_banner", "-y", "-i", outPath, "-map", "0", "-c", "copy", tmp], ct);
            if (r.Success && File.Exists(tmp))
            {
                File.Move(tmp, outPath, overwrite: true);
                log.LogInformation("MKV remuxeado tras cierre forzado: {Path}", outPath);
                return true;
            }
            log.LogWarning("El remux del MKV falló; queda el archivo crudo: {E}", LastLines(r.Stderr, 5));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "El remux del MKV falló; queda el archivo crudo");
        }
        TryDelete(tmp);
        return false;
    }

    // <DataDirectory>/descartes/<nombre> (con _1, _2… si ya existe). Nunca borra. Devuelve la ruta nueva.
    private string? MoveToDiscards(string path) => MoveToDiscards(_dataDir, path, _log);

    /// <summary><c>&lt;dataDir&gt;/descartes/&lt;nombre&gt;</c> (con _1, _2… si ya existe). Nunca borra. También lo usa la sesión.</summary>
    internal static string? MoveToDiscards(string dataDir, string path, ILogger log)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var dir = Path.Combine(dataDir, DiscardDirName);
            Directory.CreateDirectory(dir);
            var dest = UniqueDiscardPath(dir, Path.GetFileName(path), File.Exists);
            File.Move(path, dest, overwrite: false);
            return dest;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "No se pudo mover {Path} a descartes; queda en la raíz", path);
            return null;
        }
    }

    /// <summary><c>nombre.ext</c>, <c>nombre_1.ext</c>, <c>nombre_2.ext</c>… el primero que no exista (puro).</summary>
    internal static string UniqueDiscardPath(string dir, string fileName, Func<string, bool> exists)
    {
        var candidate = Path.Combine(dir, fileName);
        if (!exists(candidate)) return candidate;
        // "grabacion_x.mkv.mic.mka" → base "grabacion_x.mkv.mic", ext ".mka".
        var ext = Path.GetExtension(fileName);
        var stem = fileName[..^ext.Length];
        for (var i = 1; ; i++)
        {
            candidate = Path.Combine(dir, $"{stem}_{i}{ext}");
            if (!exists(candidate)) return candidate;
        }
    }

    internal static long FileSize(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    // E1: solo en macOS se sugiere Homebrew.
    internal static string ScrcpyMissingMessage() =>
        OperatingSystem.IsMacOS()
            ? ErrorScrcpyMissing + " En la Mac: brew install scrcpy."
            : ErrorScrcpyMissing;

    internal static bool IsScrcpyWarningLine(string line) =>
        line.StartsWith("ERROR", StringComparison.Ordinal) ||
        line.StartsWith("WARN", StringComparison.Ordinal) ||
        line.Contains("adb server version", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("doesn't match", StringComparison.OrdinalIgnoreCase);

    // Hasta 3 líneas de error de scrcpy unidas con " · ". Prefiere las ERROR/WARN; si no hay,
    // las últimas líneas que haya escrito. null si no escribió nada.
    internal static string? ScrcpyErrorSummary(LineTail tail)
    {
        var lines = tail.Snapshot()
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        if (lines.Count == 0) return null;
        var errors = lines.Where(IsScrcpyWarningLine).ToList();
        var chosen = (errors.Count > 0 ? errors : lines).TakeLast(3);
        return string.Join(" · ", chosen);
    }

    internal static string LastLines(string text, int n) =>
        string.Join(" | ", text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).TakeLast(n));

    internal static bool HasExitedSafe(Process p)
    {
        try { return p.HasExited; }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException
                                       or System.ComponentModel.Win32Exception) { return true; }
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; }
        catch (InvalidOperationException) { return -1; }
    }

    internal static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    // ── Device info ───────────────────────────────────────────────────────────

    private async Task EnrichDeviceAsync(Device device, CancellationToken ct)
    {
        async Task<string> Prop(string prop)
        {
            var r = await ProcessRunner.RunAsync(AdbPath.Value,
                ["-s", device.Serial, "shell", "getprop", prop], ct);
            return r.Stdout.Trim();
        }

        device.Manufacturer   = await Prop("ro.product.manufacturer");
        device.Model          = await Prop("ro.product.model");
        device.Name           = await Prop("ro.product.name");
        device.Operator       = await Prop("gsm.operator.alpha");
        device.AndroidVersion = int.TryParse(
            await Prop("ro.build.version.release"), out var v) ? v : 0;

        var imeiResult = await ProcessRunner.RunAsync(AdbPath.Value,
            ["-s", device.Serial, "shell", "service", "call", "iphonesubinfo", "1"], ct);
        device.Imei = ParseImei(imeiResult.Stdout);
    }

    private static string ParseImei(string output)
    {
        var parts = output.Split("'", StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Trim().All(c => char.IsDigit(c) || c == '.'))
            .Select(p => p.Trim().Replace(".", ""))
            .ToList();
        var joined = string.Concat(parts).Replace("\n", "");
        var imei = joined[..Math.Min(15, joined.Length)];
        // adb no siempre expone el IMEI (sin permisos, versión de Android, etc.). En vez de un
        // string vacío, devolvemos el centinela explícito que el front/backend ya reconocen.
        return string.IsNullOrWhiteSpace(imei) ? Device.ImeiManualEntry : imei;
    }

    // ── Explorador de archivos del dispositivo ────────────────────────────────
    //
    // adb shell une todos los argumentos posteriores a "shell" con un simple espacio antes de
    // reenviarlos al shell remoto — una ruta con espacios (ej. "WhatsApp Business/Media") se
    // parte en dos argumentos salvo que la citemos nosotros mismos para el shell remoto (las
    // comillas que agrega ProcessRunner.QuoteArg son solo para el parseo local de .NET).
    private static string RemoteQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    private static readonly System.Text.RegularExpressions.Regex LsLineRegex = new(
        @"^(?<type>[bcdlpsD-])[rwxsStT@+.-]{9}\S*\s+\S+\s+\S+\s+\S+\s+(?<size>\d+)\s+(?<date>\d{4}-\d{2}-\d{2})\s+(?<time>\d{2}:\d{2})\s+(?<name>.+)$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    public async Task<List<AndroidFileEntry>> ListDirectoryAsync(string serial, string path,
        CancellationToken ct = default)
    {
        if (_mock) return MockDirectoryEntries(path);

        // Barra final obligatoria: algunos dispositivos (confirmado en un Samsung con Android 11)
        // no siguen symlinks de nivel superior sin ella — /sdcard es siempre un symlink a
        // /storage/self/primary, y "ls -la /sdcard" sin barra final lista el symlink en sí
        // (una sola línea) en vez de su contenido.
        var remotePath = path.TrimEnd('/') + "/";
        var r = await ProcessRunner.RunAsync(AdbPath.Value,
            ["-s", serial, "shell", "ls", "-la", RemoteQuote(remotePath)], ct);

        var combined = r.Stdout + "\n" + r.Stderr;
        if (combined.Contains("Permission denied"))
            throw new UnauthorizedAccessException($"Sin acceso a la carpeta: {path}");
        if (combined.Contains("No such file or directory"))
            throw new DirectoryNotFoundException($"La carpeta no existe: {path}");
        if (combined.Contains("Not a directory"))
            throw new InvalidOperationException($"No es una carpeta: {path}");

        return ParseLsOutput(r.Stdout, path);
    }

    private static List<AndroidFileEntry> ParseLsOutput(string stdout, string basePath)
    {
        var entries = new List<AndroidFileEntry>();
        foreach (var rawLine in stdout.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("total ")) continue;

            var match = LsLineRegex.Match(line);
            if (!match.Success) continue;

            var name = match.Groups["name"].Value;
            // symlinks: "nombre -> destino"
            var arrowIdx = name.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIdx >= 0) name = name[..arrowIdx];
            if (name is "." or "..") continue;

            var isDir = match.Groups["type"].Value == "d";
            DateTime? modified = DateTime.TryParse(
                $"{match.Groups["date"].Value} {match.Groups["time"].Value}",
                out var dt) ? dt : null;

            entries.Add(new AndroidFileEntry
            {
                Name        = name,
                Path        = basePath.TrimEnd('/') + "/" + name,
                IsDirectory = isDir,
                Size        = long.TryParse(match.Groups["size"].Value, out var sz) ? sz : 0,
                ModifiedAt  = modified,
            });
        }
        return entries
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task PullFileAsync(string serial, string devicePath, string localOutputPath,
        CancellationToken ct = default)
    {
        if (_mock) { await File.WriteAllBytesAsync(localOutputPath, MockImageBytes(), ct); return; }

        // A diferencia de "adb shell" (que une los argumentos y los reenvía a un shell remoto
        // para su interpretación — ahí sí hace falta RemoteQuote), "adb exec-out" preserva el
        // vector de argumentos tal cual sin pasar por un shell remoto: agregar comillas acá
        // las entrega como caracteres literales del nombre de archivo y rompe la búsqueda
        // (confirmado en un dispositivo real: con comillas, "cat" no encontraba el archivo).
        var exit = await ProcessRunner.RunBinaryToFileAsync(AdbPath.Value,
            ["-s", serial, "exec-out", "cat", devicePath], localOutputPath, ct);
        if (exit == -1) throw new InvalidOperationException(AdbMissingMessage(HostOsInfo.Current));
        if (exit != 0 || !File.Exists(localOutputPath) || new FileInfo(localOutputPath).Length == 0)
            throw new InvalidOperationException($"No se pudo traer el archivo: {devicePath}");
    }

    // Carpetas conocidas por app — WhatsApp tiene convención de nombre de archivo propia y
    // confiable (IMG-/VID-/PTT-/AUD-/STK-/DOC- + "-WA"), así que además de listar sus carpetas
    // dedicadas hacemos una búsqueda global por patrón de nombre (cubre archivos reenviados o
    // movidos fuera de su carpeta original). Instagram/Facebook no tienen convención de nombre
    // propia — la mayoría del contenido queda en storage privado de la app, inaccesible sin
    // root — así que solo listamos las carpetas públicas conocidas si existen.
    private static readonly Dictionary<string, string[]> KnownAppFolders = new()
    {
        ["whatsapp"] = [
            "/sdcard/WhatsApp/Media",
            "/sdcard/Android/media/com.whatsapp/WhatsApp/Media",
            "/sdcard/Android/media/com.whatsapp.w4b/WhatsApp Business/Media",
        ],
        ["instagram"] = [
            "/sdcard/Pictures/Instagram", "/sdcard/DCIM/Instagram", "/sdcard/Movies/Instagram",
        ],
        ["facebook"] = [
            "/sdcard/Pictures/Facebook", "/sdcard/DCIM/Facebook", "/sdcard/Movies/Facebook",
        ],
    };

    private static readonly string[] WhatsappNamePatterns =
        ["IMG-*-WA*", "VID-*-WA*", "PTT-*-WA*", "AUD-*-WA*", "STK-*-WA*", "DOC-*-WA*"];

    public async Task<List<AndroidFileEntry>> FindByAppAsync(string serial, string app,
        CancellationToken ct = default)
    {
        if (_mock) return MockDirectoryEntries("/sdcard").Where(e => !e.IsDirectory).ToList();

        var appKey  = app.ToLowerInvariant();
        var results = new Dictionary<string, AndroidFileEntry>(); // por Path, evita duplicados

        if (KnownAppFolders.TryGetValue(appKey, out var folders))
        {
            foreach (var folder in folders)
            {
                List<AndroidFileEntry> entries;
                try { entries = await ListDirectoryAsync(serial, folder, ct); }
                catch { continue; } // carpeta no existe / sin acceso — se ignora, no es un error real acá
                foreach (var e in entries.Where(e => !e.IsDirectory)) results[e.Path] = e;
            }
        }

        if (appKey == "whatsapp")
        {
            var findExpr = string.Join(" -o ", WhatsappNamePatterns.Select(p => $"-iname {RemoteQuote(p)}"));
            var r = await ProcessRunner.RunAsync(AdbPath.Value,
                $"-s {serial} shell find /sdcard -type f \\( {findExpr} \\) 2>/dev/null", ct);
            foreach (var rawLine in r.Stdout.Split('\n'))
            {
                var devicePath = rawLine.Trim().TrimEnd('\r');
                if (string.IsNullOrEmpty(devicePath) || results.ContainsKey(devicePath)) continue;
                results[devicePath] = new AndroidFileEntry
                {
                    Name = devicePath[(devicePath.LastIndexOf('/') + 1)..],
                    Path = devicePath,
                    IsDirectory = false,
                };
            }
        }

        return results.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<AndroidFileEntry> MockDirectoryEntries(string path) =>
    [
        new() { Name = "DCIM", Path = $"{path.TrimEnd('/')}/DCIM", IsDirectory = true },
        new() { Name = "Download", Path = $"{path.TrimEnd('/')}/Download", IsDirectory = true },
        new() { Name = "foto_mock.jpg", Path = $"{path.TrimEnd('/')}/foto_mock.jpg",
                IsDirectory = false, Size = 45213, ModifiedAt = DateTime.Now },
    ];

    private static Device MockDevice() => new()
    {
        Serial = "EMU001", State = "device", Manufacturer = "Google",
        Model = "Pixel 7 (mock)", AndroidVersion = 14, Imei = "000000000000000",
        Name = "pixel7_mock", Operator = "MockNet", Platform = "android"
    };

    private static byte[] MockImageBytes() =>
        Convert.FromBase64String(
            "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0a" +
            "HBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAARCAABAAEDASIAAhEBAxEB/8QAFAAB" +
            "AAAAAAAAAAAAAAAAAAAJ/8QAFBABAAAAAAAAAAAAAAAAAAAAAP/EABQBAQAAAAAAAAAAAAAAAAAAAADw" +
            "AAAAAP/aAAwDAQACEQMRAD8AJQAB/9k=");
}
