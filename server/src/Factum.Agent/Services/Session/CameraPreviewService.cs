using System.Diagnostics;
using System.Text;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services.Session;

/// <summary>Resultado de <c>POST /cameras/preview</c> (§7.2.2).</summary>
public sealed record CameraPreviewResult(bool Ok, string? Detail = null, string? Error = null);

/// <summary>Lo que la sesión necesita de la vista previa: cortarla antes de abrir la cámara (dshow no la comparte).</summary>
public interface ICameraPreview
{
    Task StopAsync();
}

/// <summary>
/// Vista previa de la cámara ANTES de grabar (grabacion-sesion-completa-f2 §5.9, D-T14): un ffmpeg con solo la salida JPG
/// (<c>fps=1/2</c>, 320 px, escritura atómica) a <c>&lt;DataDirectory&gt;/camera-preview/preview.jpg</c>. Una a la vez;
/// se corta sola a los <c>CameraPreviewIdleSeconds</c> sin pedidos de la imagen, al abrir la cámara de la sesión, en el
/// apagado y en <c>ApplicationStopping</c>. En mock o con <c>CameraLavfi</c> la fuente es lavfi.
/// </summary>
public sealed class CameraPreviewService : ICameraPreview, IDisposable
{
    public const string DirName = "camera-preview";
    public const string FileName = "preview.jpg";
    private static readonly TimeSpan IdlePoll = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan QuitTimeout = TimeSpan.FromSeconds(3);

    private readonly Models.SessionOptions _opt;
    private readonly CameraDevices _devices;
    private readonly ILogger _log;
    private readonly TimeProvider _time;
    private readonly string _dir;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _proc;
    private string? _cameraKey;
    private CancellationTokenSource? _idleCts;
    private long _lastTouchTicks;

    public CameraPreviewService(IOptions<AgentOptions> opts, CameraDevices devices, ILogger<CameraPreviewService> log,
        TimeProvider time)
        : this((opts.Value.Session ?? new Models.SessionOptions()).Clone().Normalize(null), devices,
            opts.Value.DataDirectory, log, time)
    {
    }

    internal CameraPreviewService(Models.SessionOptions opt, CameraDevices devices, string dataDirectory, ILogger? log,
        TimeProvider time)
    {
        _opt = opt;
        _devices = devices;
        _log = log ?? NullLogger.Instance;
        _time = time;
        _dir = Path.Combine(Path.GetFullPath(dataDirectory), DirName);
    }

    public string ImagePath => Path.Combine(_dir, FileName);

    /// <summary>true mientras el ffmpeg de la vista previa sigue vivo.</summary>
    public bool IsRunning => _proc is { } p && !AdbService.HasExitedSafe(p);

    /// <summary>Renueva el tiempo de inactividad (cada pedido de la imagen).</summary>
    public void Touch() => Interlocked.Exchange(ref _lastTouchTicks, Environment.TickCount64);

    public async Task<CameraPreviewResult> StartAsync(string? id, string name)
    {
        await _gate.WaitAsync();
        try
        {
            var key = (id ?? "") + "\n" + name;
            if (IsRunning && _cameraKey == key)
            {
                Touch();
                return new CameraPreviewResult(true);
            }
            await StopLockedAsync(deleteImage: true);

            var ffmpeg = ToolResolver.Find(AgentTools.Ffmpeg)?.Path;
            if (ffmpeg is null) return new CameraPreviewResult(false, null, CameraDevices.ErrorNoFfmpeg);

            string backend, video = "";
            if (_devices.Lavfi)
            {
                backend = CameraArgs.BackendLavfi;
            }
            else
            {
                var dev = await _devices.ResolveAsync(id, name);
                if (dev is null)
                    return new CameraPreviewResult(false, CameraFailDetails.NotFound,
                        CameraArgs.FailureText(CameraFailDetails.NotFound));
                backend = dev.Backend;
                video = backend == CameraArgs.BackendMac
                    ? (dev.Name.Contains(':') && dev.Index is { } i ? i.ToString(System.Globalization.CultureInfo.InvariantCulture) : dev.Name)
                    : dev.Id;
            }
            Directory.CreateDirectory(_dir);
            CameraMode? mode = backend == CameraArgs.BackendLavfi
                ? null
                : CameraArgs.DefaultMode(backend, _opt.CameraWidth, _opt.CameraHeight, _opt.CameraFps);
            for (var attempt = 1; ; attempt++)
            {
                var (proc, failure, lines) = await LaunchAsync(ffmpeg, CameraArgs.BuildPreview(backend, video, mode, ImagePath),
                    backend);
                if (proc is not null)
                {
                    _proc = proc;
                    _cameraKey = key;
                    Touch();
                    _idleCts = new CancellationTokenSource();
                    var ct = _idleCts.Token;
                    _ = Task.Run(() => IdleLoopAsync(proc, ct));
                    _log.LogInformation("Vista previa de la cámara {Name} encendida", name);
                    return new CameraPreviewResult(true);
                }
                if (attempt == 1 && failure!.RetryMode)
                {
                    mode = backend == CameraArgs.BackendMac
                        ? CameraArgs.PickMode(CameraArgs.ParseSupportedModes(lines), _opt.CameraWidth, _opt.CameraHeight)
                        : null;
                    if (backend != CameraArgs.BackendMac || mode is not null) continue;
                }
                var detail = failure!.RetryMode ? CameraFailDetails.Unsupported : failure.Detail;
                return new CameraPreviewResult(false, detail, CameraArgs.FailureText(detail));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(Process? Proc, CameraFailure? Failure, List<string> Lines)> LaunchAsync(string ffmpeg,
        List<string> args, string backend)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        var lines = new List<string>();
        var output = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Process proc;
        try
        {
            proc = Process.Start(psi) ?? throw new InvalidOperationException("no se pudo lanzar ffmpeg");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.LogWarning(ex, "No se pudo lanzar ffmpeg para la vista previa");
            return (null, new CameraFailure(false, null), lines);
        }
        proc.OutputDataReceived += (_, _) => { };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (lines) if (lines.Count < 200) lines.Add(e.Data);
            if (CameraArgs.IsPreviewOutputLine(e.Data)) output.TrySetResult();
            _log.LogDebug("ffmpeg (vista previa): {Line}", e.Data);
        };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        var exited = proc.WaitForExitAsync(CancellationToken.None);
        await Task.WhenAny(output.Task, exited, Task.Delay(TimeSpan.FromSeconds(_opt.CameraStartTimeoutSeconds)));
        List<string> Snapshot() { lock (lines) return [.. lines]; }
        if (!output.Task.IsCompleted && !AdbService.HasExitedSafe(proc))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ya terminó */ }
            try { await exited.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* best effort */ }
            proc.Dispose();
            return (null, CameraArgs.Classify(backend, Snapshot(), null, timedOut: true), Snapshot());
        }
        if (output.Task.IsCompleted) await Task.WhenAny(exited, Task.Delay(CameraTrack.EarlyExitWindow));
        if (AdbService.HasExitedSafe(proc))
        {
            try { proc.WaitForExit(); } catch { /* ya terminó */ }
            int? code;
            try { code = proc.ExitCode; } catch { code = null; }
            proc.Dispose();
            return (null, CameraArgs.Classify(backend, Snapshot(), code, timedOut: false), Snapshot());
        }
        return (proc, null, Snapshot());
    }

    private async Task IdleLoopAsync(Process proc, CancellationToken ct)
    {
        var idleMs = _opt.CameraPreviewIdleSeconds * 1000L;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(IdlePoll, ct); }
            catch (OperationCanceledException) { return; }
            if (AdbService.HasExitedSafe(proc)) return;
            if (Environment.TickCount64 - Interlocked.Read(ref _lastTouchTicks) < idleMs) continue;
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                if (!ReferenceEquals(_proc, proc)) return;
                _log.LogInformation("Vista previa de la cámara cortada por inactividad");
                await StopLockedAsync(deleteImage: true);
            }
            finally
            {
                _gate.Release();
            }
            return;
        }
    }

    /// <summary>Corta la previa (si hay) y borra <c>preview.jpg</c> (archivo propio). Idempotente.</summary>
    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try { await StopLockedAsync(deleteImage: true); }
        finally { _gate.Release(); }
    }

    private async Task StopLockedAsync(bool deleteImage)
    {
        try { _idleCts?.Cancel(); } catch { /* best effort */ }
        _idleCts = null;
        var p = _proc;
        _proc = null;
        _cameraKey = null;
        if (p is not null)
        {
            try
            {
                if (!AdbService.HasExitedSafe(p))
                {
                    try
                    {
                        await p.StandardInput.WriteAsync('q');
                        await p.StandardInput.FlushAsync();
                        p.StandardInput.Close();
                    }
                    catch { /* sigue con kill */ }
                    try { await p.WaitForExitAsync(CancellationToken.None).WaitAsync(QuitTimeout); }
                    catch (TimeoutException)
                    {
                        try { p.Kill(entireProcessTree: true); } catch { /* ya terminó */ }
                        try { await p.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)); }
                        catch { /* best effort */ }
                    }
                }
            }
            finally
            {
                p.Dispose();
            }
            _log.LogInformation("Vista previa de la cámara apagada");
        }
        if (deleteImage)
        {
            AdbService.TryDelete(ImagePath);
            AdbService.TryDelete(ImagePath + ".tmp");
        }
    }

    public void Dispose()
    {
        try { StopAsync().GetAwaiter().GetResult(); } catch { /* best effort */ }
        _gate.Dispose();
    }
}
