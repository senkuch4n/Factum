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
    Task StartRecordingAsync(string serial, string outputPath, int androidVersion, bool withMic = false, CancellationToken ct = default);
    Task<string> StopRecordingAsync(string serial, CancellationToken ct = default);
    Task<List<AndroidFileEntry>> ListDirectoryAsync(string serial, string path, CancellationToken ct = default);
    Task PullFileAsync(string serial, string devicePath, string localOutputPath, CancellationToken ct = default);
    Task<List<AndroidFileEntry>> FindByAppAsync(string serial, string app, CancellationToken ct = default);
}

public sealed class AdbService : IAdbService, IOperationSource
{
    // adb: tools/platform-tools junto al exe (portátil), PATH o Homebrew (ToolResolver). Si no
    // aparece, queda el nombre pelado "adb": ProcessRunner devuelve -1 y se mantienen los
    // mensajes de "ADB no instalado".
    private static readonly Lazy<string> AdbPath =
        new(() => ToolResolver.Find(AgentTools.Adb)?.Path ?? "adb");

    // Textos de error de grabación (SDD grabacion-android-windows §4.3). El client los muestra.
    private const string ErrorScrcpyMissing =
        "No se encontró el componente de grabación de Android (scrcpy). Actualizá o reinstalá Tatana con el paquete de Factum.";
    private const string ErrorAlreadyRecording = "Ya hay una grabación de Android en curso.";
    private const string ErrorNotRecording = "No hay una grabación en curso.";

    private static readonly TimeSpan ScrcpyEarlyExitWindow = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan ScrcpyStopTimeout = TimeSpan.FromSeconds(10);

    private readonly bool _mock;
    private readonly string? _micDevice;
    private readonly ILogger<AdbService> _log;
    private Process? _recordingProcess;
    private string?  _recordingPath;
    // tatana-instalador-autoupdate §5.3: inicio de la grabación en curso (recording_android).
    private DateTimeOffset? _recordingSince;
    private MicCapture? _mic;
    // Últimas líneas de scrcpy, para los errores E2/E8. Se reemplaza en cada start.
    private LineTail _scrcpyTail = new(20);
    private readonly SemaphoreSlim _recordLock = new(1, 1);

    public AdbService(IOptions<AgentOptions> opts, ILogger<AdbService> log)
    {
        _mock      = opts.Value.Mock;
        _micDevice = opts.Value.MicDevice;
        _log       = log;
    }

    // ── Operaciones en curso (tatana-instalador-autoupdate §5.3) ──────────────
    // Una grabación activa bloquea "Reiniciar y actualizar". Si scrcpy ya terminó solo (celular
    // desconectado), no cuenta: no hay nada que cortar y si no la actualización quedaría trabada
    // hasta que alguien toque "Detener".
    public IEnumerable<AgentOperation> ActiveOperations
    {
        get
        {
            var since = _recordingSince;
            var proc  = _recordingProcess;
            if (since is null || _recordingPath is null) return [];
            if (!_mock && (proc is null || HasExitedSafe(proc))) return [];
            return [new AgentOperation("recording_android", since.Value)];
        }
    }

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
        if (exit == -1) throw new InvalidOperationException(
            "ADB no instalado — instalalo desde la sección Librerías de Tatana");
        if (exit != 0) throw new InvalidOperationException($"screencap falló (exit {exit})");

        return outputPath;
    }

    // ── Grabación con scrcpy ──────────────────────────────────────────────────
    //
    // scrcpy graba directo al archivo local (no hay adb pull).
    // Formato MKV: si scrcpy se cierra limpio (SIGINT / Ctrl+C) escribe duración e índice; si
    // hubo que matarlo, se remuxea con ffmpeg -c copy para que quede con duración.
    // Audio interno del dispositivo disponible con scrcpy v2+ y Android 11+.
    // SDD grabacion-android-windows §6.2 / §6.3.

    public async Task StartRecordingAsync(string serial, string outputPath,
        int androidVersion, bool withMic = false, CancellationToken ct = default)
    {
        await _recordLock.WaitAsync(ct);
        try
        {
            if (_mock) { _recordingPath = outputPath; _recordingSince = DateTimeOffset.UtcNow; return; }

            // E3: una sola grabación Android a la vez.
            if (_recordingProcess is not null && !HasExitedSafe(_recordingProcess))
                throw new InvalidOperationException(ErrorAlreadyRecording);
            await DiscardStaleRecordingAsync();

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

            // Con mic de PC activo, no tiene sentido además intentar el audio de sistema de
            // scrcpy (que de todos modos falla en silencio para contenido protegido como las
            // notas de voz de WhatsApp) — una sola fuente de audio limpia, mismo criterio que
            // el modo with_mic de iOS (video solo + mic de PC, sin mezclar con otra fuente).
            // Sin --no-audio-playback: el audio del celular se sigue escuchando en la PC (D12 b).
            var audioArg = withMic
                ? "--no-audio"
                : androidVersion >= 11 ? "--audio-source=output" : "--no-audio";

            // ArgumentList (no Arguments): la ruta del perfil puede tener espacios o acentos.
            // --no-video-playback: scrcpy 4.x — no abrir ventana de visualización.
            // UseShellExecute=false + CreateNoWindow=true le da a scrcpy su propia consola
            // oculta, que es lo que permite el Ctrl+C de ProcessStop en Windows. No usar
            // CreateNewProcessGroup: deshabilitaría el Ctrl+C en scrcpy.
            var psi = new ProcessStartInfo
            {
                FileName               = scrcpy.Path,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            foreach (var a in new[]
            {
                "--serial", serial,
                "--record", outputPath,
                "--record-format", "mkv",
                "--no-video-playback",
                "--max-size", "1080",
                "--video-bit-rate", "4M",
                audioArg,
            }) psi.ArgumentList.Add(a);

            // D3: un solo servidor adb. Sin esto scrcpy usa su adb propio, de otra versión, y
            // mata/relanza el servidor de Tatana ("adb server version doesn't match").
            var adb = ToolResolver.Find(AgentTools.Adb);
            if (adb is not null) psi.Environment["ADB"] = adb.Path;
            if (serverPath is not null) psi.Environment["SCRCPY_SERVER_PATH"] = serverPath;

            var tail = new LineTail(20);
            _scrcpyTail = tail;

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

            // Sin drenar stdout/stderr, el pipe del SO se llena con el log de scrcpy y el
            // proceso se bloquea escribiendo → la grabación se congela en silencio (el MKV
            // queda truncado) mucho antes de que el usuario aprete Stop.
            void OnLine(string? line)
            {
                if (line is null) return;
                tail.Add(line);
                if (IsScrcpyWarningLine(line)) _log.LogWarning("scrcpy: {Line}", line);
                else _log.LogDebug("scrcpy: {Line}", line);
            }
            proc.OutputDataReceived += (_, e) => OnLine(e.Data);
            proc.ErrorDataReceived  += (_, e) => OnLine(e.Data);
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // E2 (DP4): si scrcpy muere enseguida, el start falla con su error real.
            await Task.WhenAny(proc.WaitForExitAsync(ct), Task.Delay(ScrcpyEarlyExitWindow, ct));
            if (HasExitedSafe(proc))
            {
                proc.WaitForExit(); // termina de vaciar stdout/stderr
                var code = proc.ExitCode;
                proc.Dispose();
                var detail = ScrcpyErrorSummary(tail) ?? $"código de salida {code}";
                throw new InvalidOperationException("scrcpy no pudo iniciar la grabación: " + detail);
            }

            _recordingProcess = proc;
            _recordingPath    = outputPath;
            _recordingSince   = DateTimeOffset.UtcNow;

            // DP6 A: con mic, si el mic no arranca (E4/E5/E6), no se graba.
            if (withMic)
            {
                var micPath = outputPath + ".mic.mka";
                try
                {
                    _mic = await MicCapture.StartAsync(micPath, _micDevice, _log, ct);
                }
                catch
                {
                    try
                    {
                        await ProcessStop.StopGracefullyAsync(proc, ScrcpyStopTimeout, _log, "scrcpy",
                            CancellationToken.None);
                    }
                    catch (Exception ex) { _log.LogWarning(ex, "Error deteniendo scrcpy tras fallar el mic"); }
                    proc.Dispose();
                    _recordingProcess = null;
                    _recordingPath    = null;
                    _recordingSince   = null;
                    _mic              = null;
                    // Solo lo que creó este intento.
                    TryDelete(outputPath);
                    TryDelete(micPath);
                    throw;
                }
            }

            _log.LogInformation(
                "scrcpy iniciado: serial={Serial} audio={Audio} mic={Mic} adb={Adb} output={Path}",
                serial, audioArg, withMic, adb?.Path ?? "(no encontrado)", outputPath);
        }
        finally { _recordLock.Release(); }
    }

    public async Task<string> StopRecordingAsync(string serial, CancellationToken ct = default)
    {
        await _recordLock.WaitAsync(ct);
        try
        {
            if (_mock)
            {
                var mockPath = _recordingPath ?? throw new InvalidOperationException(ErrorNotRecording);
                await File.WriteAllBytesAsync(mockPath, Array.Empty<byte>(), ct);
                _recordingPath = null;
                _recordingSince = null;
                return mockPath;
            }

            // E7
            var outPath = _recordingPath ?? throw new InvalidOperationException(ErrorNotRecording);
            var proc    = _recordingProcess;
            var mic     = _mic;
            var tail    = _scrcpyTail;

            var clean = true;
            try
            {
                if (proc is not null)
                    clean = await ProcessStop.StopGracefullyAsync(proc, ScrcpyStopTimeout, _log, "scrcpy", ct);
            }
            finally
            {
                // El estado se limpia siempre, y el mic se detiene aunque el MKV no exista (antes
                // el ffmpeg del mic quedaba vivo en ese caso).
                proc?.Dispose();
                _recordingProcess = null;
                _recordingPath    = null;
                _recordingSince   = null;
                _mic              = null;
                if (mic is not null) await mic.StopAsync();
            }

            // E8: scrcpy no dejó archivo.
            if (!File.Exists(outPath))
            {
                if (mic is not null) TryDelete(mic.AudioPath);
                var detail = ScrcpyErrorSummary(tail);
                throw new InvalidOperationException(
                    "scrcpy no generó el archivo de la grabación." + (detail is null ? "" : " " + detail));
            }

            // El mux del mic reescribe el contenedor, así que reemplaza al remux.
            var muxed = false;
            if (mic is not null && File.Exists(mic.AudioPath))
                muxed = await MuxMicAudioIntoVideoAsync(outPath, mic.AudioPath, ct);

            if (!clean && !muxed)
                await RemuxAsync(outPath, ct);

            return outPath;
        }
        finally { _recordLock.Release(); }
    }

    // Si scrcpy terminó solo (celular desconectado, etc.) el estado viejo queda colgado: se
    // limpia antes de un start nuevo, deteniendo el mic si seguía grabando.
    private async Task DiscardStaleRecordingAsync()
    {
        if (_recordingProcess is null && _mic is null) return;
        _recordingProcess?.Dispose();
        _recordingProcess = null;
        _recordingPath    = null;
        _recordingSince   = null;
        var mic = _mic;
        _mic = null;
        if (mic is not null) await mic.StopAsync();
    }

    // Re-mux el MKV de scrcpy (ya codificado) + audio del mic → reemplaza el archivo de video.
    // Devuelve true si el archivo final quedó reescrito por ffmpeg.
    private async Task<bool> MuxMicAudioIntoVideoAsync(string videoPath, string audioPath, CancellationToken ct)
    {
        var ffmpeg = ToolResolver.Find(AgentTools.Ffmpeg)?.Path;
        if (ffmpeg is null)
        {
            _log.LogWarning("mux audio (mic): no se encontró ffmpeg; queda el video sin el audio del mic");
            return false;
        }
        var tmp = videoPath + ".mux.mkv";
        var r = await ProcessRunner.RunArgumentListAsync(ffmpeg,
            ["-hide_banner", "-y", "-i", videoPath, "-i", audioPath,
             "-c:v", "copy", "-c:a", "aac", "-b:a", "128k", "-shortest", tmp], ct);
        if (!r.Success || !File.Exists(tmp))
        {
            _log.LogWarning("mux audio (mic) falló: {E}", LastLines(r.Stderr, 5));
            TryDelete(tmp);
            return false;
        }
        File.Move(tmp, videoPath, overwrite: true);
        TryDelete(audioPath);
        _log.LogInformation("Audio del micrófono incorporado a la grabación");
        return true;
    }

    // Tras un cierre forzado, el MKV puede quedar sin duración ni índice: se reescribe el
    // contenedor sin recodificar. Si falla, queda el MKV crudo (sigue siendo reproducible).
    private async Task RemuxAsync(string outPath, CancellationToken ct)
    {
        var ffmpeg = ToolResolver.Find(AgentTools.Ffmpeg)?.Path;
        if (ffmpeg is null)
        {
            _log.LogWarning("No se encontró ffmpeg para remuxear el MKV tras el cierre forzado; queda el archivo crudo");
            return;
        }
        var tmp = outPath + ".remux.mkv";
        try
        {
            var r = await ProcessRunner.RunArgumentListAsync(ffmpeg,
                ["-hide_banner", "-y", "-i", outPath, "-map", "0", "-c", "copy", tmp], ct);
            if (r.Success && File.Exists(tmp))
            {
                File.Move(tmp, outPath, overwrite: true);
                _log.LogInformation("MKV remuxeado tras cierre forzado: {Path}", outPath);
                return;
            }
            _log.LogWarning("El remux del MKV falló; queda el archivo crudo: {E}", LastLines(r.Stderr, 5));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "El remux del MKV falló; queda el archivo crudo");
        }
        TryDelete(tmp);
    }

    // E1: solo en macOS se sugiere Homebrew.
    private static string ScrcpyMissingMessage() =>
        OperatingSystem.IsMacOS()
            ? ErrorScrcpyMissing + " En la Mac: brew install scrcpy."
            : ErrorScrcpyMissing;

    private static bool IsScrcpyWarningLine(string line) =>
        line.StartsWith("ERROR", StringComparison.Ordinal) ||
        line.StartsWith("WARN", StringComparison.Ordinal) ||
        line.Contains("adb server version", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("doesn't match", StringComparison.OrdinalIgnoreCase);

    // Hasta 3 líneas de error de scrcpy unidas con " · ". Prefiere las ERROR/WARN; si no hay,
    // las últimas líneas que haya escrito. null si no escribió nada.
    private static string? ScrcpyErrorSummary(LineTail tail)
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

    private static string LastLines(string text, int n) =>
        string.Join(" | ", text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).TakeLast(n));

    private static bool HasExitedSafe(Process p)
    {
        try { return p.HasExited; }
        catch (InvalidOperationException) { return true; }
    }

    private static void TryDelete(string path)
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
        return joined[..Math.Min(15, joined.Length)];
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
        if (exit == -1) throw new InvalidOperationException(
            "ADB no instalado — instalalo desde la sección Librerías de Tatana");
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
