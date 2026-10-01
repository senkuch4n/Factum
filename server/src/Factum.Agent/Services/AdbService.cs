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

public sealed class AdbService : IAdbService
{
    // scrcpy puede estar en el PATH o en la ruta de Homebrew en macOS
    private static readonly string[] ScrcpyPaths =
        ["scrcpy", "/opt/homebrew/bin/scrcpy", "/usr/local/bin/scrcpy"];

    // ffmpeg para la captura de audio del micrófono de la PC (modo "con mic" — análogo al
    // with_mic de iOS). No se comparte con IosService.BuildToolCandidates (cada servicio
    // resuelve sus propios binarios en este código); alcanza con PATH + rutas de Homebrew.
    private static readonly string[] FfmpegPaths =
        ["ffmpeg", "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg"];

    private static string? FindBinary(string[] candidates) =>
        candidates.FirstOrDefault(p => p.Contains('/') ? File.Exists(p) : IsOnPath(p));

    // Modo portátil ("Tatana Portable"): adb viene copiado junto al exe en
    // tools/platform-tools/, sin depender del PATH del sistema (no requiere
    // permisos de admin para "instalarlo"). Si no existe esa carpeta, se cae
    // al comportamiento actual (bare "adb" resuelto por PATH).
    private static readonly Lazy<string> AdbPath = new(() =>
    {
        var portable = Path.Combine(AppContext.BaseDirectory, "tools", "platform-tools",
            OperatingSystem.IsWindows() ? "adb.exe" : "adb");
        return File.Exists(portable) ? portable : "adb";
    });

    private readonly bool _mock;
    private readonly ILogger<AdbService> _log;
    private Process? _recordingProcess;
    private string?  _recordingPath;
    private Process? _micAudioProcess;
    private string?  _micAudioPath;
    private readonly SemaphoreSlim _recordLock = new(1, 1);

    public AdbService(IOptions<AgentOptions> opts, ILogger<AdbService> log)
    {
        _mock = opts.Value.Mock;
        _log  = log;
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
    // Formato MKV: escribe el índice incrementalmente → válido incluso tras SIGKILL.
    // Audio interno del dispositivo disponible con scrcpy v2+ y Android 11+.

    public async Task StartRecordingAsync(string serial, string outputPath,
        int androidVersion, bool withMic = false, CancellationToken ct = default)
    {
        await _recordLock.WaitAsync(ct);
        try
        {
            if (_mock) { _recordingPath = outputPath; return; }

            var scrcpy = FindScrcpy();
            // Con mic de PC activo, no tiene sentido además intentar el audio de sistema de
            // scrcpy (que de todos modos falla en silencio para contenido protegido como las
            // notas de voz de WhatsApp) — una sola fuente de audio limpia, mismo criterio que
            // el modo with_mic de iOS (video solo + mic de PC, sin mezclar con otra fuente).
            var audioArgs = withMic
                ? "--no-audio"
                : androidVersion >= 11 ? "--audio-source=output" : "--no-audio";

            // --no-video-playback: scrcpy 4.x — no abrir ventana de visualización
            var args = string.Join(' ',
                "--serial", serial,
                "--record", $"\"{outputPath}\"",
                "--record-format", "mkv",
                "--no-video-playback",
                "--max-size", "1080",
                "--video-bit-rate", "4M",
                audioArgs);

            _recordingProcess = Process.Start(new ProcessStartInfo
            {
                FileName               = scrcpy,
                Arguments              = args,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            })!;

            // Sin drenar stdout/stderr, el pipe del SO se llena con el log de scrcpy y el
            // proceso se bloquea escribiendo → la grabación se congela en silencio (el MKV
            // queda truncado) mucho antes de que el usuario aprete Stop.
            _recordingProcess.OutputDataReceived += (_, e) => { if (e.Data != null) _log.LogDebug("scrcpy: {Line}", e.Data); };
            _recordingProcess.ErrorDataReceived  += (_, e) => { if (e.Data != null) _log.LogDebug("scrcpy: {Line}", e.Data); };
            _recordingProcess.BeginOutputReadLine();
            _recordingProcess.BeginErrorReadLine();

            _recordingPath = outputPath;
            _log.LogInformation("scrcpy iniciado: serial={Serial} audio={Audio} output={Path}",
                serial, audioArgs, outputPath);

            if (withMic) StartMicAudio(outputPath);
        }
        finally { _recordLock.Release(); }
    }

    // ── Captura de audio del micrófono de la PC ("con mic") ───────────────────
    //
    // Análogo al modo with_mic de iOS (IosService.StartAudio/StopAudio/MuxAudioIntoVideoAsync):
    // graba el mic por separado vía ffmpeg y, al detener, lo remuxea sobre el MKV de scrcpy.
    // Sirve para capturar audio que la app bloquea digitalmente (ej. notas de voz de WhatsApp,
    // protegidas a nivel de sistema operativo) — al ser una captura acústica real, no hay
    // protección de contenido que la afecte.

    private void StartMicAudio(string videoPath)
    {
        var audioPath = videoPath + ".mic.m4a";
        var ffmpeg    = FindBinary(FfmpegPaths) ?? "ffmpeg";
        var inputArgs = OperatingSystem.IsWindows() ? "-f wasapi -i default" :
                        OperatingSystem.IsMacOS()   ? "-f avfoundation -i :0" :
                                                      "-f alsa -i default";
        var proc = Process.Start(new ProcessStartInfo
        {
            FileName  = ffmpeg,
            Arguments = $"-y {inputArgs} -c:a aac -b:a 128k \"{audioPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute = false,
            CreateNoWindow  = true,
        });
        if (proc is null) { _log.LogWarning("No se pudo iniciar captura de audio (mic)"); return; }

        // Mismo motivo que en el proceso de scrcpy: drenar los pipes para que ffmpeg no se
        // bloquee escribiendo a stderr y se congele la captura de audio.
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) _log.LogDebug("ffmpeg (mic): {Line}", e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) _log.LogDebug("ffmpeg (mic): {Line}", e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        _micAudioProcess = proc;
        _micAudioPath    = audioPath;
        _log.LogInformation("Captura de audio (mic PC) iniciada para grabación Android");
    }

    private void StopMicAudio()
    {
        if (_micAudioProcess is null || _micAudioProcess.HasExited) return;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                _micAudioProcess.Kill();
            }
            else
            {
                using var sigint = Process.Start(new ProcessStartInfo
                {
                    FileName = "kill", Arguments = $"-2 {_micAudioProcess.Id}",
                    UseShellExecute = false, CreateNoWindow = true,
                });
                sigint?.WaitForExit();
            }
            _micAudioProcess.WaitForExit();
        }
        catch { }
    }

    // Re-mux el MKV de scrcpy (ya codificado) + audio del mic → reemplaza el archivo de video.
    private async Task MuxMicAudioIntoVideoAsync(string videoPath, string audioPath, CancellationToken ct)
    {
        var ffmpeg = FindBinary(FfmpegPaths) ?? "ffmpeg";
        var tmp    = videoPath + ".mux.mkv";
        var r = await ProcessRunner.RunAsync(ffmpeg,
            $"-y -i \"{videoPath}\" -i \"{audioPath}\" " +
            $"-c:v copy -c:a aac -b:a 128k -shortest \"{tmp}\"", ct);
        if (!r.Success) { _log.LogWarning("mux audio (mic) falló: {E}", r.Stderr); try { File.Delete(tmp); } catch { } return; }
        File.Delete(videoPath);
        File.Move(tmp, videoPath);
        try { File.Delete(audioPath); } catch { }
    }

    public async Task<string> StopRecordingAsync(string serial, CancellationToken ct = default)
    {
        await _recordLock.WaitAsync(ct);
        try
        {
            if (_mock)
            {
                var mockPath = _recordingPath ?? throw new InvalidOperationException("Sin grabación activa");
                await File.WriteAllBytesAsync(mockPath, Array.Empty<byte>(), ct);
                _recordingPath = null;
                return mockPath;
            }

            var outPath = _recordingPath ?? throw new InvalidOperationException("Sin grabación activa");

            if (_recordingProcess != null && !_recordingProcess.HasExited)
            {
                // SIGINT primero → scrcpy cierra limpiamente el MKV
                try
                {
                    using var sigint = Process.Start(new ProcessStartInfo
                    {
                        FileName       = "kill",
                        Arguments      = $"-2 {_recordingProcess.Id}",
                        UseShellExecute = false,
                        CreateNoWindow  = true,
                    });
                    await sigint!.WaitForExitAsync(ct);
                }
                catch { /* kill no disponible → caemos a SIGKILL */ }

                // Esperar hasta 5 s; si no terminó, SIGKILL (MKV sigue siendo válido)
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                try   { await _recordingProcess.WaitForExitAsync(linked.Token); }
                catch { _recordingProcess.Kill(); await _recordingProcess.WaitForExitAsync(ct); }
            }

            _recordingProcess?.Dispose();
            _recordingProcess = null;
            _recordingPath    = null;

            // scrcpy ya escribió el archivo en outPath — sin adb pull necesario
            if (!File.Exists(outPath))
                throw new InvalidOperationException($"scrcpy no generó el archivo: {outPath}");

            if (_micAudioProcess is not null)
            {
                StopMicAudio();
                _micAudioProcess = null;
                if (_micAudioPath is not null && File.Exists(_micAudioPath))
                    await MuxMicAudioIntoVideoAsync(outPath, _micAudioPath, ct);
                _micAudioPath = null;
            }

            return outPath;
        }
        finally { _recordLock.Release(); }
    }

    private static string FindScrcpy() =>
        ScrcpyPaths.FirstOrDefault(p =>
            p.Contains('/') ? File.Exists(p) : IsOnPath(p))
        ?? throw new InvalidOperationException(
            "scrcpy no encontrado. Instalá con: brew install scrcpy");

    private static bool IsOnPath(string exe)
    {
        var sep   = OperatingSystem.IsWindows() ? ';' : ':';
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(sep) ?? [];
        return paths.Any(dir => File.Exists(Path.Combine(dir, exe)));
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
