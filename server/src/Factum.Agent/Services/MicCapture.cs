using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Factum.Agent.Common;

namespace Factum.Agent.Services;

// Captura del micrófono de la PC con ffmpeg, compartida por Android (modo "con mic") e iOS
// (with_mic). SDD grabacion-android-windows §6.4.
//
// - Windows: dshow, con el dispositivo de Agent:MicDevice o el primero que lista Windows.
// - macOS: avfoundation :0. Linux: alsa default.
// - Salida en .mka (Matroska): si hay que matar a ffmpeg el archivo sigue siendo legible
//   (con .m4a quedaba sin moov y el mux fallaba).
// - Se cierra limpio mandando "q" por stdin, en los tres SO.
internal sealed class MicCapture
{
    internal const string ErrorNoFfmpeg =
        "No se encontró ffmpeg, necesario para grabar con el micrófono de la PC. Actualizá o reinstalá Tatana con el paquete de Factum.";
    internal const string ErrorNoMic = "No se encontró ningún micrófono en esta PC.";
    internal const string ErrorOpenPrefix = "No se pudo abrir el micrófono de la PC: ";

    private static readonly TimeSpan EarlyExitWindow = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ListDevicesTimeout = TimeSpan.FromSeconds(5);
    private const int TailSize = 10;

    private readonly Process _proc;
    private readonly ILogger _log;
    private int _stopped;

    public string AudioPath { get; }

    private MicCapture(Process proc, string audioPath, ILogger log)
    {
        _proc = proc;
        AudioPath = audioPath;
        _log = log;
    }

    // Lanza ffmpeg grabando el mic a `audioPath` (.mka). Lanza InvalidOperationException con
    // los mensajes E4/E5/E6 de la SDD.
    public static async Task<MicCapture> StartAsync(string audioPath, string? configuredDevice,
        ILogger log, CancellationToken ct)
    {
        var ffmpeg = ToolResolver.Find(AgentTools.Ffmpeg)?.Path
            ?? throw new InvalidOperationException(ErrorNoFfmpeg);

        var psi = new ProcessStartInfo
        {
            FileName               = ffmpeg,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            StandardErrorEncoding  = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-nostats");
        psi.ArgumentList.Add("-y");

        if (OperatingSystem.IsWindows())
        {
            var device = await ResolveWindowsDeviceAsync(ffmpeg, configuredDevice, log, ct);
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("dshow");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("audio=" + device);
        }
        else if (OperatingSystem.IsMacOS())
        {
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("avfoundation");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(":0");
        }
        else
        {
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("alsa");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("default");
        }
        psi.ArgumentList.Add("-c:a"); psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add("-b:a"); psi.ArgumentList.Add("128k");
        psi.ArgumentList.Add(audioPath);

        var tail = new LineTail(TailSize);
        Process? proc;
        try { proc = Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(ErrorOpenPrefix + ex.Message);
        }
        if (proc is null)
            throw new InvalidOperationException(ErrorOpenPrefix + "no se pudo iniciar ffmpeg");

        // Drenar los pipes: si se llenan, ffmpeg se bloquea y la captura se congela.
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) log.LogDebug("ffmpeg (mic): {Line}", e.Data); };
        proc.ErrorDataReceived  += (_, e) =>
        {
            if (e.Data == null) return;
            tail.Add(e.Data);
            log.LogDebug("ffmpeg (mic): {Line}", e.Data);
        };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        // Detección temprana (DP4): si ffmpeg muere enseguida, el mic no abrió.
        await Task.WhenAny(proc.WaitForExitAsync(ct), Task.Delay(EarlyExitWindow, ct));
        if (proc.HasExited)
        {
            proc.WaitForExit(); // termina de vaciar stderr
            var last = tail.LastNonEmpty() ?? $"código de salida {proc.ExitCode}";
            proc.Dispose();
            TryDelete(audioPath);
            throw new InvalidOperationException(ErrorOpenPrefix + last);
        }

        log.LogInformation("Captura de audio (mic PC) iniciada: {Path}", audioPath);
        return new MicCapture(proc, audioPath, log);
    }

    // Manda "q" por stdin, espera 5 s y, si no terminó, Kill(entireProcessTree). Nunca lanza.
    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1) return; // idempotente
        try
        {
            if (_proc.HasExited) return;
            try
            {
                await _proc.StandardInput.WriteAsync('q');
                await _proc.StandardInput.FlushAsync();
                _proc.StandardInput.Close();
            }
            catch (Exception ex) { _log.LogDebug(ex, "No se pudo mandar 'q' a ffmpeg (mic)"); }

            using var timeout = new CancellationTokenSource(StopTimeout);
            try
            {
                await _proc.WaitForExitAsync(timeout.Token);
                _log.LogInformation("ffmpeg (mic) detenido limpiamente (exit {Code})", _proc.ExitCode);
            }
            catch (OperationCanceledException)
            {
                try { _proc.Kill(entireProcessTree: true); } catch { }
                try { _proc.WaitForExit(2000); } catch { }
                _log.LogWarning("ffmpeg (mic) no respondió a 'q'; se forzó el cierre");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Error deteniendo la captura del micrófono");
        }
        finally
        {
            try { _proc.Dispose(); } catch { }
        }
    }

    // ── Windows: elección del dispositivo dshow ──────────────────────────────

    private static async Task<string> ResolveWindowsDeviceAsync(string ffmpeg, string? configured,
        ILogger log, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            log.LogInformation("Micrófono de la PC: {Name} (Agent:MicDevice)", configured.Trim());
            return configured.Trim();
        }

        var stderr = await ListDshowDevicesAsync(ffmpeg, log, ct);
        var mics = DshowDevices.ParseAudioDevices(stderr);
        if (mics.Count == 0) throw new InvalidOperationException(ErrorNoMic);

        var first = mics[0];
        log.LogInformation("Micrófono de la PC: {Name}", first.Name);
        return string.IsNullOrWhiteSpace(first.AlternativeName) ? first.Name : first.AlternativeName;
    }

    // `ffmpeg -hide_banner -list_devices true -f dshow -i dummy` sale con código ≠ 0: es normal.
    private static async Task<string> ListDshowDevicesAsync(string ffmpeg, ILogger log, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = ffmpeg,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            StandardErrorEncoding  = Encoding.UTF8,
            ArgumentList = { "-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy" },
        };
        var sb = new StringBuilder();
        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return string.Empty;
            proc.OutputDataReceived += (_, _) => { };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            using var timeout = new CancellationTokenSource(ListDevicesTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try
            {
                await proc.WaitForExitAsync(linked.Token);
                proc.WaitForExit();
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                log.LogWarning("ffmpeg -list_devices no respondió en {Sec} s", ListDevicesTimeout.TotalSeconds);
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            log.LogWarning(ex, "No se pudo listar los micrófonos con ffmpeg");
        }
        lock (sb) return sb.ToString();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

internal static class DshowDevices
{
    // "[dshow @ 0x…] " o "[in#0 @ 0x…] " al principio de cada línea de log de ffmpeg.
    private static readonly Regex Prefix = new(@"^\[[^\]]*\]\s*", RegexOptions.Compiled);
    // "Nombre" (audio)  — formato nuevo;  "Nombre"  — formato viejo (tipo por sección).
    private static readonly Regex DeviceLine = new(
        "^\"(?<name>.*)\"(?:\\s*\\((?<type>[^()]*)\\))?\\s*$", RegexOptions.Compiled);
    private static readonly Regex AltLine = new(
        "^Alternative name\\s+\"(?<alt>.*)\"\\s*$", RegexOptions.Compiled);

    // Parsea el stderr de `ffmpeg -hide_banner -list_devices true -f dshow -i dummy` y devuelve
    // los dispositivos de audio en el orden en que los lista Windows.
    public static IReadOnlyList<(string Name, string? AlternativeName)> ParseAudioDevices(string stderr)
    {
        var result = new List<(string Name, string? AlternativeName)>();
        string? section = null;  // formato viejo: "audio" / "video"
        int lastAudio = -1;      // índice del último dispositivo de audio (para su alternative name)

        foreach (var raw in stderr.Split('\n'))
        {
            var line = Prefix.Replace(raw.TrimEnd('\r'), string.Empty).Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("DirectShow audio devices", StringComparison.OrdinalIgnoreCase))
            { section = "audio"; lastAudio = -1; continue; }
            if (line.StartsWith("DirectShow video devices", StringComparison.OrdinalIgnoreCase))
            { section = "video"; lastAudio = -1; continue; }

            var alt = AltLine.Match(line);
            if (alt.Success)
            {
                if (lastAudio >= 0 && result[lastAudio].AlternativeName is null)
                    result[lastAudio] = (result[lastAudio].Name, alt.Groups["alt"].Value);
                continue;
            }

            var dev = DeviceLine.Match(line);
            if (!dev.Success) continue;

            var type = dev.Groups["type"].Success ? dev.Groups["type"].Value : section;
            var isAudio = type is not null &&
                type.Split(',').Any(t => t.Trim().Equals("audio", StringComparison.OrdinalIgnoreCase));
            if (isAudio)
            {
                result.Add((dev.Groups["name"].Value, null));
                lastAudio = result.Count - 1;
            }
            else
            {
                lastAudio = -1;
            }
        }
        return result;
    }
}

// Buffer circular de las últimas N líneas, thread-safe (los handlers de Process corren en el
// thread pool).
internal sealed class LineTail(int capacity)
{
    private readonly Queue<string> _lines = new();

    public void Add(string line)
    {
        lock (_lines)
        {
            _lines.Enqueue(line);
            while (_lines.Count > capacity) _lines.Dequeue();
        }
    }

    public List<string> Snapshot()
    {
        lock (_lines) return [.. _lines];
    }

    public string? LastNonEmpty()
    {
        lock (_lines) return _lines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
    }
}
