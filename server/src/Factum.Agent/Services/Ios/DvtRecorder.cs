using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Factum.Agent.Services.Ios;

/// <summary>Resultado del stop: Ok = exit 0, cuadros &gt; 0, ffmpeg 0 y MP4 &gt; 0 bytes.</summary>
public sealed record DvtStopResult(bool Ok, int Frames, IosException? Error, long ElapsedMs);

/// <summary>
/// Grabación DVT del iPhone con el helper Python (SDD ios-herramientas-windows §6.7): arranca hasta
/// READY (30 s), drena stderr en segundo plano, se detiene con "stop" por stdin (igual en Windows y
/// en la Mac, sin señales) y espera DONE con el MP4 ya cerrado por ffmpeg. Sin DI: lo usa también
/// la autoprueba.
/// </summary>
public sealed class DvtRecorder : IAsyncDisposable
{
    public static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);
    private const int TailLines = 50;

    private readonly Process _proc;
    private readonly ILogger _log;
    private readonly bool _isWindows;
    private readonly Queue<string> _tail = new();
    private readonly object _gate = new();
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _stderrPump = Task.CompletedTask;
    private string? _doneLine;
    private string? _errorLine;
    private bool _stopped;

    public string OutputPath { get; }

    private DvtRecorder(Process proc, string outputPath, ILogger log)
    {
        _proc = proc;
        OutputPath = outputPath;
        _log = log;
        _isWindows = OperatingSystem.IsWindows();
    }

    public static async Task<DvtRecorder> StartAsync(string python, string helperPath, string udid,
        string outputPath, string ffmpeg, double fps, bool synthetic, ILogger log, CancellationToken ct = default)
    {
        List<string> args =
        [
            helperPath, "record", "--udid", udid, "--output", outputPath, "--ffmpeg", ffmpeg,
            "--fps", fps.ToString(CultureInfo.InvariantCulture),
        ];
        if (synthetic) args.Add("--synthetic");

        var psi = IosHelper.CreateStartInfo(python, args, redirectStandardInput: true);
        var proc = new Process { StartInfo = psi };
        try
        {
            proc.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            proc.Dispose();
            log.LogWarning("No arrancó Python para grabar el iPhone: {E}", ex.Message);
            throw IosErrors.Create(IosErrors.ToolsMissing);
        }

        var rec = new DvtRecorder(proc, outputPath, log);
        proc.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 } d) log.LogDebug("DVT recorder (stdout): {Line}", d); };
        proc.BeginOutputReadLine();
        rec._stderrPump = Task.Run(rec.PumpStderrAsync);

        using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readyTimeout.CancelAfter(ReadyTimeout);
        var exited = proc.WaitForExitAsync(readyTimeout.Token);
        var ready = false;
        try
        {
            var first = await Task.WhenAny(rec._ready.Task, exited, Task.Delay(Timeout.Infinite, readyTimeout.Token));
            ready = first == rec._ready.Task && rec._ready.Task.Result;
        }
        catch (OperationCanceledException) { }

        if (!ready)
        {
            rec.KillTree();
            try { await rec._stderrPump.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); } catch { }
            ct.ThrowIfCancellationRequested();
            var error = rec.HelperError()
                ?? IosErrors.Create(IosErrors.TunnelFailed, rec._isWindows);
            log.LogWarning("Error de iPhone {Code}: {Detail}", error.Code, rec.LastStderrLine() ?? "sin READY");
            log.LogDebug("DVT recorder sin READY. stderr:\n{Stderr}", rec.Tail());
            proc.Dispose();
            throw error;
        }

        log.LogInformation("iOS DVT recorder iniciado (~{Fps} FPS)", fps);
        return rec;
    }

    /// <summary>Detiene la grabación y espera a que el MP4 quede cerrado (o 30 s y se mata el árbol).</summary>
    public async Task<DvtStopResult> StopAsync(CancellationToken ct = default)
    {
        _stopped = true;
        var sw = Stopwatch.StartNew();
        try
        {
            _proc.StandardInput.WriteLine("stop");
            _proc.StandardInput.Flush();
            _proc.StandardInput.Close();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // El helper ya terminó (ej. se desconectó el iPhone): se evalúa igual.
        }

        var timedOut = false;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(StopTimeout);
            try { await _proc.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                timedOut = true;
                KillTree();
            }
        }
        try { await _stderrPump.WaitAsync(TimeSpan.FromSeconds(5), ct); } catch (TimeoutException) { }
        sw.Stop();

        var exitCode = SafeExitCode();
        var (frames, ffmpegExit) = ParseDone();
        var fileOk = File.Exists(OutputPath) && new FileInfo(OutputPath).Length > 0;
        var ok = !timedOut && exitCode == 0 && frames > 0 && ffmpegExit == 0 && fileOk;

        _log.LogInformation("iOS DVT recorder detenido en {Ms} ms ({Frames} cuadros)", sw.ElapsedMilliseconds, frames);
        if (ok) return new DvtStopResult(true, frames, null, sw.ElapsedMilliseconds);

        IosException error;
        if (timedOut)
            error = IosErrors.RecordingEmptyFrom(null, IosErrors.ReasonTimeout, _isWindows);
        else if (HelperError() is { } cause)
            error = IosErrors.RecordingEmptyFrom(cause, IosErrors.ReasonNoFrames, _isWindows);
        else if (frames > 0 && ffmpegExit != 0)
            error = IosErrors.RecordingEmptyFrom(null, "ffmpeg no pudo cerrar el archivo de video.", _isWindows);
        else
            error = IosErrors.RecordingEmptyFrom(null, IosErrors.ReasonNoFrames, _isWindows);

        _log.LogWarning("Error de iPhone {Code}: {Detail}", error.Code,
            $"exit {exitCode}, {frames} cuadros, ffmpeg {ffmpegExit}, archivo {(fileOk ? "ok" : "vacío")}");
        _log.LogDebug("DVT recorder stderr:\n{Stderr}", Tail());
        return new DvtStopResult(false, frames, error, sw.ElapsedMilliseconds);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stopped && !HasExited()) KillTree();
        try { await _stderrPump.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        _proc.Dispose();
    }

    // ── stderr ─────────────────────────────────────────────────────────────────────────

    private async Task PumpStderrAsync()
    {
        try
        {
            while (await _proc.StandardError.ReadLineAsync() is { } line)
            {
                lock (_gate)
                {
                    _tail.Enqueue(line);
                    while (_tail.Count > TailLines) _tail.Dequeue();
                    var t = line.Trim();
                    if (t == "READY") _ready.TrySetResult(true);
                    else if (t.StartsWith("DONE ", StringComparison.Ordinal)) _doneLine = t;
                    else if (t.StartsWith(IosErrors.HelperErrorPrefix, StringComparison.Ordinal)) _errorLine = t;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
        finally
        {
            _ready.TrySetResult(false);
        }
    }

    private IosException? HelperError()
    {
        string? line;
        lock (_gate) line = _errorLine;
        return IosErrors.TryParseHelperError(line, out var code, out var detail)
            ? IosErrors.FromHelper(code, detail, _isWindows)
            : null;
    }

    private (int Frames, int FfmpegExit) ParseDone()
    {
        string? line;
        lock (_gate) line = _doneLine;
        return ParseDoneLine(line);
    }

    /// <summary><c>DONE {"frames": N, "ffmpeg_exit": X}</c> → (N, X); sin línea o rota → (0, -1).</summary>
    internal static (int Frames, int FfmpegExit) ParseDoneLine(string? line)
    {
        if (line is null || !line.StartsWith("DONE ", StringComparison.Ordinal)) return (0, -1);
        try
        {
            using var doc = JsonDocument.Parse(line[5..]);
            var frames = doc.RootElement.TryGetProperty("frames", out var f) && f.TryGetInt32(out var fv) ? fv : 0;
            var ffx = doc.RootElement.TryGetProperty("ffmpeg_exit", out var x) && x.TryGetInt32(out var xv) ? xv : -1;
            return (frames, ffx);
        }
        catch (JsonException)
        {
            return (0, -1);
        }
    }

    private string Tail()
    {
        lock (_gate) return string.Join('\n', _tail);
    }

    private string? LastStderrLine()
    {
        lock (_gate) return _tail.Count == 0 ? null : _tail.Last();
    }

    private void KillTree()
    {
        try { _proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        try { _proc.WaitForExit(5000); } catch (InvalidOperationException) { }
    }

    private bool HasExited()
    {
        try { return _proc.HasExited; } catch (InvalidOperationException) { return true; }
    }

    private int SafeExitCode()
    {
        try { return _proc.HasExited ? _proc.ExitCode : -1; } catch (InvalidOperationException) { return -1; }
    }
}
