using System.Diagnostics;
using System.Text.Json;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.WebSockets;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services;

public interface IIosService
{
    bool IsAvailable { get; }
    Task<List<Device>> ListDevicesAsync(CancellationToken ct = default);
    Task<string> TakeScreenshotAsync(string udid, string outputPath, string method = "auto", CancellationToken ct = default);
    Task StartRecordingAsync(string udid, string mode, string outputPath, CancellationToken ct = default);
    Task<string> StopRecordingAsync(string udid, CancellationToken ct = default);

    // Sesión de "espejar para capturas": conecta AirPlay una vez y permite marcar N momentos
    // mientras el fiscal navega libremente, extrayendo un PNG por marca recién al finalizar.
    Task<string> StartAirplayShotSessionAsync(CancellationToken ct = default);
    Task<int> MarkAirplayShotAsync(CancellationToken ct = default);
    Task<List<(string Filename, string Url)>> StopAirplayShotSessionAsync(CancellationToken ct = default);
}

// Modos de grabación iOS:
//   video_only  → capturas DVT → ffmpeg MP4 (~2 FPS; limitación de iOS 26)
//   with_mic    → igual que video_only + micrófono del PC mezclado en el MP4 final
//   on_device   → grabación nativa iOS (Control Center); se extrae del DCIM al detener
//
// iOS 26.5 bloquea com.apple.coredevice.feature.startmediastream (requiere iOS 27+).
// En su lugar usamos com.apple.instruments.server.services.screenshot vía DVT,
// que sí funciona con el DDI montado. El Python helper ios_dvt_recorder.py mantiene
// el tunnel abierto y captura frames continuamente → pipe PNG → ffmpeg H.264 MP4.
public sealed class IosService : IIosService
{
    // Resuelve un binario externo primero relativo al exe (tools/{toolDirName}/…, el patrón
    // que ya usa el build portable de Windows para python-embed), y si no está ahí cae a
    // PATH y a las rutas típicas de Homebrew. `winName` puede ser null si el binario no
    // tiene build de Windows (ej. qvh, que el upstream abandonó en esa plataforma).
    private static string[] BuildToolCandidates(string toolDirName, string unixName, string? winName = null)
    {
        var exeName = OperatingSystem.IsWindows() ? (winName ?? unixName) : unixName;
        var portable = Path.Combine(AppContext.BaseDirectory, "tools", toolDirName, exeName);
        List<string> candidates = [portable, unixName];
        if (winName is not null) candidates.Add(winName);
        candidates.Add($"/opt/homebrew/bin/{unixName}");
        candidates.Add($"/usr/local/bin/{unixName}");
        return [.. candidates];
    }

    private static readonly string[] FfmpegPaths = BuildToolCandidates("ffmpeg", "ffmpeg", "ffmpeg.exe");

    private static readonly string[] FfprobePaths =
        ["ffprobe", "/opt/homebrew/bin/ffprobe", "/usr/local/bin/ffprobe"];

    private static readonly string[] RifePaths =
        ["rife-ncnn-vulkan", "/opt/homebrew/bin/rife-ncnn-vulkan", "/usr/local/bin/rife-ncnn-vulkan"];

    // qvh (danielpaulus/quicktime_video_hack, MIT) reimplementa el protocolo de espejado de
    // pantalla por USB que usa QuickTime/com.apple.cmio.iOSScreenCaptureAssistant — no
    // requiere Developer Mode. Sin binario de Windows: el upstream abandonó ese port.
    private static readonly string[] QvhPaths = BuildToolCandidates("qvh", "qvh");

    // python candidates — se elige el primero que tenga pymobiledevice3.
    // Primero el modo portátil ("Tatana Portable"): un Python embebido con
    // pymobiledevice3 preinstalado, copiado junto al exe en tools/python-embed/,
    // sin depender del PATH del sistema. Si no existe, cae a las rutas típicas
    // de Windows (antes no había NINGUNA acá) y luego a las de Unix ya existentes.
    private static readonly string[] PythonCandidates = BuildPythonCandidates();

    private static string[] BuildPythonCandidates()
    {
        var portable = Path.Combine(AppContext.BaseDirectory, "tools", "python-embed",
            OperatingSystem.IsWindows() ? "python.exe" : "python3");
        return
        [
            portable,
            "python.exe", "python", "py",
            "python3", "/usr/bin/python3", "/usr/local/bin/python3", "/opt/homebrew/bin/python3",
        ];
    }

    private readonly bool _mock;
    private readonly ILogger<IosService> _log;
    private readonly AgentWebSocketHub _hub;
    private readonly IFileStorageService _storage;
    private readonly SemaphoreSlim _recordLock = new(1, 1);
    private readonly SemaphoreSlim _shotLock = new(1, 1);
    private IosSession? _session;
    private AirplayShotSession? _shotSession;
    private bool? _available;
    private string? _dvtScriptPath;
    private string? _python; // python3 con pymobiledevice3 instalado

    public IosService(IOptions<AgentOptions> opts, ILogger<IosService> log, AgentWebSocketHub hub,
        IFileStorageService storage)
    {
        _mock    = opts.Value.Mock;
        _log     = log;
        _hub     = hub;
        _storage = storage;
    }

    // ── Session state ─────────────────────────────────────────────────────────

    private sealed class IosSession
    {
        public string Mode       = string.Empty;
        public string OutputPath = string.Empty;
        public string SessionId  = string.Empty;
        public string DataDir    = string.Empty;
        // DVT recorder (Python helper process — manages its own ffmpeg)
        public Process? StreamProc;
        public bool IsStreamMode;
        // burst fallback (último recurso si DVT falla)
        public List<string> Frames = [];
        public readonly object FramesLock = new();
        public CancellationTokenSource? BurstCts;
        public Task? BurstTask;
        // audio (with_mic → PC microphone via ffmpeg)
        public Process? AudioProc;
        public string? AudioPath;
        // on_device
        public List<string> ExistingDCIMFiles = [];
        // airplay
        public string? AirplayReceiverName;
    }

    // Sesión de "espejar para capturas": a diferencia de IosSession (grabación) esta sesión
    // se mantiene viva mientras el fiscal navega libremente por el teléfono y va marcando
    // momentos puntuales — los frames se extraen recién al finalizar, porque un MP4 no es
    // seekable de forma confiable hasta que GStreamer lo termina de escribir (mismo motivo
    // por el que el modo de grabación "airplay" tampoco permite leer el archivo en vivo).
    private sealed class AirplayShotSession
    {
        public string ReceiverName = string.Empty;
        public string Mp4Stem      = string.Empty;
        public Process? UxplayProc;
        public DateTime? ConnectedAt;              // se setea cuando se detecta el primer byte del mp4
        public readonly List<double> Marks = [];   // segundos transcurridos desde ConnectedAt
        public readonly object MarksLock = new();
        public CancellationTokenSource? WatchCts;  // tarea de background que espera la conexión
    }

    // ── Python discovery ──────────────────────────────────────────────────────
    //
    // pymobiledevice3 puede estar instalado en un python distinto al que es
    // default en PATH (ej: Homebrew Python 3.14 no tiene pymobiledevice3,
    // pero sí lo tiene /usr/bin/python3 3.9). Buscamos el primero que lo tenga.

    private string Python
    {
        get
        {
            if (_python is not null) return _python;
            foreach (var candidate in PythonCandidates)
            {
                try
                {
                    var r = ProcessRunner.RunAsync(candidate, "-c \"import pymobiledevice3\"")
                                         .GetAwaiter().GetResult();
                    if (r.Success) { _python = candidate; return _python; }
                }
                catch { }
            }
            _python = "python3"; // fallback
            return _python;
        }
    }

    // ── IsAvailable ───────────────────────────────────────────────────────────

    public bool IsAvailable
    {
        get
        {
            if (_mock) return true;
            if (_available.HasValue) return _available.Value;
            try
            {
                var r = ProcessRunner.RunAsync(Python, "-m pymobiledevice3 --help")
                                     .GetAwaiter().GetResult();
                _available = r.Success;
            }
            catch { _available = false; }
            return _available!.Value;
        }
    }

    // ── Device listing ────────────────────────────────────────────────────────

    public async Task<List<Device>> ListDevicesAsync(CancellationToken ct = default)
    {
        if (_mock) return [MockDevice()];
        if (!IsAvailable) return [];

        var r = await ProcessRunner.RunAsync(Python,
            "-m pymobiledevice3 usbmux list", ct);
        if (!r.Success || string.IsNullOrWhiteSpace(r.Stdout)) return [];

        try
        {
            using var doc = JsonDocument.Parse(r.Stdout);
            var devices   = new List<Device>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("Identifier", out var idProp)) continue;
                var udid = idProp.GetString() ?? "";
                if (string.IsNullOrEmpty(udid)) continue;
                try   { devices.Add(await FetchDeviceInfoAsync(udid, ct)); }
                catch { devices.Add(new Device { Serial = udid, State = "device",
                            Manufacturer = "Apple", Platform = "ios" }); }
            }
            return devices;
        }
        catch { return []; }
    }

    private async Task<Device> FetchDeviceInfoAsync(string udid, CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync(Python,
            $"-m pymobiledevice3 lockdown info --udid {udid}", ct);

        var d = new Device { Serial = udid, State = "device", Manufacturer = "Apple", Platform = "ios" };
        if (!r.Success) return d;

        try
        {
            using var doc = JsonDocument.Parse(r.Stdout);
            var root      = doc.RootElement;
            if (root.TryGetProperty("DeviceName",    out var v)) d.Name       = v.GetString() ?? "";
            if (root.TryGetProperty("ProductType",   out v))     d.Model      = FriendlyModel(v.GetString() ?? "");
            if (root.TryGetProperty("ProductVersion", out v))
            {
                d.IosVersion = v.GetString() ?? "";
                if (int.TryParse(d.IosVersion.Split('.')[0], out var maj))
                    d.AndroidVersion = maj;
            }
            if (root.TryGetProperty("InternationalMobileEquipmentIdentity", out v))
                d.Imei = v.GetString() ?? "";
            if (root.TryGetProperty("PhoneNumber", out v))
                d.Operator = v.GetString() ?? "";
        }
        catch { /* keep defaults */ }
        return d;
    }

    // ── Screenshot ────────────────────────────────────────────────────────────
    //
    // Usa el mismo servicio DVT (com.apple.instruments.server.services.screenshot)
    // que el grabador de video, vía establish_userspace_rsd(). El anterior comando
    // core-device screen-capture usaba un servicio distinto que falla si no hay
    // DDI montado o si el dispositivo está en estado "passcode locked".

    private const string DvtScreenshotScript = @"
import asyncio, sys, warnings
warnings.filterwarnings('ignore')

async def main(output_path):
    from pymobiledevice3.remote.userspace_tunnel import establish_userspace_rsd
    from pymobiledevice3.services.dvt.instruments.screenshot import Screenshot
    from pymobiledevice3.services.dvt.instruments.dvt_provider import DvtProvider

    rsd = await establish_userspace_rsd()
    async with DvtProvider(rsd) as dvt:
        async with Screenshot(dvt) as ss:
            data = await asyncio.wait_for(ss.get_screenshot(), timeout=10.0)
            with open(output_path, 'wb') as f:
                f.write(data)

asyncio.run(main(sys.argv[1]))
";

    private string? _dvtScreenshotScriptPath;

    private string EnsureDvtScreenshotScript()
    {
        if (_dvtScreenshotScriptPath is not null && File.Exists(_dvtScreenshotScriptPath))
            return _dvtScreenshotScriptPath;
        var path = Path.Combine(Path.GetTempPath(), "ios_dvt_screenshot.py");
        File.WriteAllText(path, DvtScreenshotScript);
        _dvtScreenshotScriptPath = path;
        return path;
    }

    // Orquesta la captura de pantalla probando, en orden, los métodos que no requieren
    // Developer Mode antes de darse por vencido. Con method="auto" (default) el fallback es
    // transparente para el fiscal: DVT (rápido, requiere Developer Mode) → USB/qvh (automático,
    // solo cable) → AirPlay (requiere que el fiscal active Espejo de pantalla a mano — último
    // recurso porque es el único que necesita interacción humana en el teléfono).
    public async Task<string> TakeScreenshotAsync(string udid, string outputPath,
        string method = "auto", CancellationToken ct = default)
    {
        if (_mock)
        {
            await File.WriteAllBytesAsync(outputPath, Array.Empty<byte>(), ct);
            return outputPath;
        }

        if (method is "auto" or "dvt")
        {
            try { return await TakeDvtScreenshotAsync(udid, outputPath, ct); }
            catch (Exception ex) when (method == "auto")
            {
                _log.LogWarning("DVT screenshot falló ({E}); probando USB (qvh)", ex.Message);
            }
        }

        if (method is "auto" or "usb")
        {
            try { return await TakeUsbScreenshotAsync(udid, outputPath, ct); }
            catch (Exception ex) when (method == "auto")
            {
                _log.LogWarning("USB screenshot (qvh) falló ({E}); probando AirPlay", ex.Message);
            }
        }

        if (method is "auto" or "airplay")
            return await TakeAirplayScreenshotAsync(outputPath, ct);

        throw new InvalidOperationException($"Método de captura desconocido: {method}");
    }

    private async Task<string> TakeDvtScreenshotAsync(string udid, string outputPath,
        CancellationToken ct)
    {
        var scriptPath = EnsureDvtScreenshotScript();
        var r = await ProcessRunner.RunAsync(Python,
            $"\"{scriptPath}\" \"{outputPath}\"", ct);
        // No confiar solo en el exit code: pymobiledevice3/DvtProvider hace algo en su
        // limpieza (aparenta ser un atexit/os._exit) que termina el proceso con código 0
        // incluso cuando lanzó una excepción sin capturar (ej. sin Developer Mode:
        // "InvalidServiceError: No such service: com.apple.instruments.dtservicehub").
        // Verificar el archivo de salida es la única señal confiable de éxito real.
        if (!r.Success || !File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            throw new InvalidOperationException(
                $"screenshot iOS (DVT) falló: {r.Stderr}\nAsegurate de que el Modo Desarrollador esté activo y el iPhone desbloqueado.");
        return outputPath;
    }

    // ── Screenshot vía USB (protocolo reverseado, qvh) ────────────────────────
    //
    // qvh (danielpaulus/quicktime_video_hack) reimplementa el protocolo de espejado por USB
    // que usa QuickTime/com.apple.cmio.iOSScreenCaptureAssistant en macOS. No requiere
    // Developer Mode ni que el fiscal toque el teléfono: alcanza con el cable conectado.
    // Graba un par de segundos de H.264 crudo + WAV y se extrae 1 frame con ffmpeg.
    //
    // Confirmado leyendo main.go del repo (no solo el README):
    //  - "qvh record <h264file> <wavfile> [--udid=<udid>]" ya activa el config USB
    //    internamente (record() → startWithConsumer() → screencapture.EnableQTConfig()),
    //    no hace falta un "qvh activate" separado antes.
    //  - qvh solo escucha SIGINT (os.Interrupt) para cortar limpio, no SIGTERM — por eso
    //    se para con signal 2, a diferencia de DVT/uxplay que sí reaccionan a SIGTERM.

    private async Task<string> TakeUsbScreenshotAsync(string udid, string outputPath, CancellationToken ct)
    {
        var qvh = FindBinary(QvhPaths)
            ?? throw new InvalidOperationException(
                "qvh no encontrado. Instalar: https://github.com/danielpaulus/quicktime_video_hack");

        var tmpH264 = Path.Combine(Path.GetTempPath(), $"ios_usb_{Guid.NewGuid():N}.h264");
        var tmpWav  = Path.Combine(Path.GetTempPath(), $"ios_usb_{Guid.NewGuid():N}.wav");
        try
        {
            var proc = Process.Start(new ProcessStartInfo
            {
                FileName        = qvh,
                UseShellExecute = false,
                CreateNoWindow  = true,
                ArgumentList    = { "record", tmpH264, tmpWav, $"--udid={udid}" },
            }) ?? throw new InvalidOperationException("No se pudo iniciar qvh");

            // Suficiente para tener frames decodificables sin alargar demasiado la captura.
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            await GracefulStopAsync(proc, TimeSpan.FromSeconds(10), ct, signal: 2); // SIGINT

            if (!File.Exists(tmpH264) || new FileInfo(tmpH264).Length == 0)
                throw new InvalidOperationException("qvh no generó stream de video (¿cable USB conectado?)");

            var ffmpeg = FindBinary(FfmpegPaths) ?? "ffmpeg";
            var r = await ProcessRunner.RunAsync(ffmpeg,
                $"-y -f h264 -i \"{tmpH264}\" -frames:v 1 \"{outputPath}\"", ct);
            if (!r.Success)
                throw new InvalidOperationException($"ffmpeg no pudo extraer frame de qvh: {r.Stderr}");

            _log.LogInformation("Screenshot iOS vía USB (qvh) capturado");
            return outputPath;
        }
        finally
        {
            try { File.Delete(tmpH264); } catch { }
            try { File.Delete(tmpWav); } catch { }
        }
    }

    // ── Screenshot vía AirPlay (1 frame del mismo receptor uxplay) ────────────
    //
    // Igual que el modo de grabación "airplay" pero corta apenas hay 1 frame utilizable.
    // Requiere que el fiscal active manualmente "Espejo de pantalla" en el iPhone — por eso
    // es el último recurso en la cadena automática, no el primero.

    private async Task<string> TakeAirplayScreenshotAsync(string outputPath, CancellationToken ct)
    {
        var uxplay = FindBinary(UxplayPaths)
            ?? throw new InvalidOperationException(
                "uxplay no encontrado. Instalá: brew install uxplay");

        var receiverName = $"Factum-{Guid.NewGuid().ToString("N")[..6]}";
        var stem = Path.Combine(Path.GetTempPath(), $"ios_shot_{Guid.NewGuid():N}");

        var psi = new ProcessStartInfo
        {
            FileName        = uxplay,
            UseShellExecute = false,
            CreateNoWindow  = true,
        };
        if (OperatingSystem.IsMacOS()) psi.Environment["DYLD_LIBRARY_PATH"] = GlibDyldPath;
        psi.ArgumentList.Add("-n");   psi.ArgumentList.Add(receiverName);
        psi.ArgumentList.Add("-nh");
        psi.ArgumentList.Add("-mp4"); psi.ArgumentList.Add(stem);

        var proc = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar uxplay");
        try
        {
            await _hub.BroadcastAsync(new AgentEvent
            {
                Type = "airplay_receiver_ready",
                Data = new { receiver_name = receiverName }
            });

            var mp4 = await WaitForAirplayFileAsync(stem, TimeSpan.FromSeconds(60), ct)
                ?? throw new InvalidOperationException(
                    "El iPhone no se conectó a AirPlay a tiempo. Verificá WiFi y activá Espejo de pantalla.");

            // Asegurar que haya al menos un frame completo y decodificable antes de cortar.
            await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
            await GracefulStopAsync(proc, TimeSpan.FromSeconds(15), ct);

            var ffmpeg = FindBinary(FfmpegPaths) ?? "ffmpeg";
            // -sseof -1: toma un frame cerca del final, evitando el primer frame (a veces
            // negro/incompleto mientras el handshake RAOP todavía está estabilizándose).
            var r = await ProcessRunner.RunAsync(ffmpeg,
                $"-y -sseof -1 -i \"{mp4}\" -update 1 -frames:v 1 \"{outputPath}\"", ct);
            if (!r.Success || !File.Exists(outputPath))
                throw new InvalidOperationException($"ffmpeg no pudo extraer frame de AirPlay: {r.Stderr}");

            _log.LogInformation("Screenshot iOS vía AirPlay capturado");
            return outputPath;
        }
        finally
        {
            if (!proc.HasExited) try { proc.Kill(); } catch { }
            var dir      = Path.GetTempPath();
            var stemName = Path.GetFileName(stem);
            foreach (var f in Directory.GetFiles(dir, $"{stemName}.*"))
                try { File.Delete(f); } catch { }
        }
    }

    // Detecta que el iPhone se conectó esperando a que aparezca el archivo mp4 que crea
    // GStreamer — alcanza con que EXISTA, no hace falta que ya tenga contenido: el filesink
    // abre/crea el archivo apenas arranca el pipeline, pero bufferea los bytes en memoria y
    // recién los vuelca a disco cuando el mirror termina (por eso exigir tamaño > 0 detectaba
    // la conexión recién al cortar el espejo, no al empezar — leer el stdout/stderr de uxplay
    // tampoco sirve como alternativa: escribe directo a la TTY y no llega nada si se redirige
    // por código, ya lo confirmamos empíricamente).
    private static async Task<string?> WaitForAirplayFileAsync(string stem, TimeSpan timeout, CancellationToken ct)
    {
        var dir      = Path.GetDirectoryName(stem) is { Length: > 0 } d ? d : Path.GetTempPath();
        var stemName = Path.GetFileName(stem);
        using var cts    = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
        try
        {
            while (!linked.Token.IsCancellationRequested)
            {
                var candidates = Directory.GetFiles(dir, $"{stemName}.*.mp4");
                if (candidates.Length > 0)
                    return candidates[0];
                await Task.Delay(300, linked.Token);
            }
        }
        catch (OperationCanceledException) { }
        return null;
    }

    // ── Sesión de "espejar para capturas" (AirPlay, múltiples marcas) ─────────
    //
    // A diferencia de TakeAirplayScreenshotAsync (un solo tiro, corta apenas hay 1 frame),
    // esta sesión mantiene uxplay corriendo mientras el fiscal navega libremente por el
    // teléfono. Cada "marca" solo guarda el timestamp — el frame real recién se extrae al
    // finalizar, cuando el MP4 ya está finalizado y es seekable.

    private const int AirplayShotConnectTimeoutSeconds = 90;

    public async Task<string> StartAirplayShotSessionAsync(CancellationToken ct = default)
    {
        await _shotLock.WaitAsync(ct);
        try
        {
            if (_shotSession is not null)
                throw new InvalidOperationException("Ya hay una sesión de captura AirPlay activa.");
            if (_session is not null)
                throw new InvalidOperationException("Hay una grabación activa — terminala antes de espejar para capturas.");

            var uxplay = FindBinary(UxplayPaths)
                ?? throw new InvalidOperationException("uxplay no encontrado. Instalá: brew install uxplay");

            var receiverName = $"Factum-{Guid.NewGuid().ToString("N")[..6]}";
            var stem = Path.Combine(Path.GetTempPath(), $"ios_shotsession_{Guid.NewGuid():N}");

            var psi = new ProcessStartInfo
            {
                FileName        = uxplay,
                UseShellExecute = false,
                CreateNoWindow  = true,
            };
            if (OperatingSystem.IsMacOS()) psi.Environment["DYLD_LIBRARY_PATH"] = GlibDyldPath;
            psi.ArgumentList.Add("-n");   psi.ArgumentList.Add(receiverName);
            psi.ArgumentList.Add("-nh");
            psi.ArgumentList.Add("-mp4"); psi.ArgumentList.Add(stem);

            var proc = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar uxplay");

            var session = new AirplayShotSession
            {
                ReceiverName = receiverName,
                Mp4Stem      = stem,
                UxplayProc   = proc,
                WatchCts     = new CancellationTokenSource(),
            };
            _shotSession = session;

            await _hub.BroadcastAsync(new AgentEvent
            {
                Type = "airplay_receiver_ready",
                Data = new { receiver_name = receiverName }
            });

            // No bloquear la llamada HTTP esperando la conexión — se resuelve en background.
            _ = WatchAirplayShotConnectionAsync(session);

            return receiverName;
        }
        finally { _shotLock.Release(); }
    }

    private async Task WatchAirplayShotConnectionAsync(AirplayShotSession session)
    {
        var mp4 = await WaitForAirplayFileAsync(session.Mp4Stem,
            TimeSpan.FromSeconds(AirplayShotConnectTimeoutSeconds), session.WatchCts!.Token);

        // Si mientras tanto ya se llamó a Stop (sesión limpiada), no hacer nada más.
        if (!ReferenceEquals(_shotSession, session)) return;

        if (mp4 is null)
        {
            _log.LogWarning("Sesión de captura AirPlay: el iPhone no se conectó a tiempo");
            if (!session.UxplayProc!.HasExited) try { session.UxplayProc.Kill(); } catch { }
            _shotSession = null;
            await _hub.BroadcastAsync(new AgentEvent { Type = "airplay_shot_timeout", Data = new { } });
            return;
        }

        session.ConnectedAt = DateTime.UtcNow;
        _log.LogInformation("Sesión de captura AirPlay conectada");
        await _hub.BroadcastAsync(new AgentEvent { Type = "airplay_shot_connected", Data = new { } });
    }

    public Task<int> MarkAirplayShotAsync(CancellationToken ct = default)
    {
        var session = _shotSession
            ?? throw new InvalidOperationException("No hay una sesión de captura AirPlay activa.");
        if (session.ConnectedAt is null)
            throw new InvalidOperationException("Todavía no se conectó el iPhone por AirPlay.");

        var elapsed = (DateTime.UtcNow - session.ConnectedAt.Value).TotalSeconds;
        int count;
        lock (session.MarksLock)
        {
            session.Marks.Add(elapsed);
            count = session.Marks.Count;
        }
        return Task.FromResult(count);
    }

    public async Task<List<(string Filename, string Url)>> StopAirplayShotSessionAsync(CancellationToken ct = default)
    {
        await _shotLock.WaitAsync(ct);
        try
        {
            var session = _shotSession
                ?? throw new InvalidOperationException("No hay una sesión de captura AirPlay activa.");
            _shotSession = null;
            session.WatchCts?.Cancel();

            if (session.UxplayProc is not null && !session.UxplayProc.HasExited)
                await GracefulStopAsync(session.UxplayProc, TimeSpan.FromSeconds(30), ct);

            var results = new List<(string, string)>();

            List<double> marks;
            lock (session.MarksLock) marks = [.. session.Marks];

            if (session.ConnectedAt is null || marks.Count == 0)
            {
                _log.LogWarning("Sesión de captura AirPlay: sin conexión o sin marcas, nada que extraer");
                CleanupAirplayShotTempFiles(session.Mp4Stem);
                return results;
            }

            var dir      = Path.GetDirectoryName(session.Mp4Stem) ?? Path.GetTempPath();
            var stemName = Path.GetFileName(session.Mp4Stem);
            var candidates = Directory.GetFiles(dir, $"{stemName}.*.mp4");
            if (candidates.Length == 0)
            {
                _log.LogWarning("Sesión de captura AirPlay: no se encontró el MP4 finalizado");
                CleanupAirplayShotTempFiles(session.Mp4Stem);
                return results;
            }
            var mp4 = candidates[0];

            var ffmpeg = FindBinary(FfmpegPaths) ?? "ffmpeg";
            foreach (var elapsed in marks)
            {
                var path = _storage.NewFilePath("screenshot", "png");
                var r = await ProcessRunner.RunAsync(ffmpeg,
                    $"-y -ss {elapsed.ToString(System.Globalization.CultureInfo.InvariantCulture)} -i \"{mp4}\" -frames:v 1 -update 1 \"{path}\"", ct);
                if (!r.Success || !File.Exists(path))
                {
                    _log.LogWarning("Sesión de captura AirPlay: no se pudo extraer marca en {Elapsed}s: {E}", elapsed, r.Stderr);
                    continue;
                }

                var filename = Path.GetFileName(path);
                var url      = $"/files/{filename}";
                results.Add((filename, url));
                await _hub.BroadcastAsync(new AgentEvent
                {
                    Type = "screenshot_taken",
                    Data = new { filename, url }
                });
            }

            _log.LogInformation("Sesión de captura AirPlay finalizada: {N} capturas", results.Count);
            CleanupAirplayShotTempFiles(session.Mp4Stem);
            return results;
        }
        finally { _shotLock.Release(); }
    }

    private static void CleanupAirplayShotTempFiles(string stem)
    {
        var dir      = Path.GetDirectoryName(stem) ?? Path.GetTempPath();
        var stemName = Path.GetFileName(stem);
        foreach (var f in Directory.GetFiles(dir, $"{stemName}.*"))
            try { File.Delete(f); } catch { }
    }

    // ── Recording — start ─────────────────────────────────────────────────────

    public async Task StartRecordingAsync(string udid, string mode, string outputPath,
        CancellationToken ct = default)
    {
        await _recordLock.WaitAsync(ct);
        try
        {
            if (mode == "airplay" && _shotSession is not null)
                throw new InvalidOperationException(
                    "Hay una sesión de captura AirPlay activa — finalizala antes de grabar.");

            var session = new IosSession
            {
                Mode       = mode,
                OutputPath = outputPath,
                SessionId  = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"),
                DataDir    = Path.GetDirectoryName(outputPath)!,
            };
            _session = session;

            if (_mock) { _log.LogInformation("iOS recording (mock): mode={Mode}", mode); return; }

            if (mode == "on_device")
            {
                session.ExistingDCIMFiles = await ListDCIMFilesAsync(udid, ct);
                _log.LogInformation("iOS on_device: snapshot DCIM ({N} archivos)", session.ExistingDCIMFiles.Count);
                return;
            }

            // AirPlay mirror receiver — conecta el iPhone por WiFi, graba 30fps + audio nativo
            if (mode == "airplay")
            {
                if (!await TryStartAirplayRecorderAsync(session, ct))
                    throw new InvalidOperationException("No se pudo iniciar el receptor AirPlay. Instalá uxplay: brew install uxplay");
                return;
            }

            // DVT recorder: captura de pantalla continua vía com.apple.instruments.server.services.screenshot
            if (await TryStartDvtRecorderAsync(session, ct))
            {
                if (mode == "with_mic") StartAudio(session);
                return;
            }

            _log.LogWarning("DVT recorder falló; fallback a burst screenshots");
            StartBurst(session, udid);
            if (mode == "with_mic") StartAudio(session);
        }
        finally { _recordLock.Release(); }
    }

    // ── Recording — stop ──────────────────────────────────────────────────────

    public async Task<string> StopRecordingAsync(string udid, CancellationToken ct = default)
    {
        await _recordLock.WaitAsync(ct);
        try
        {
            var session = _session ?? throw new InvalidOperationException("Sin grabación iOS activa");
            _session    = null;

            if (_mock)
            {
                await File.WriteAllBytesAsync(session.OutputPath, Array.Empty<byte>(), ct);
                return session.OutputPath;
            }

            if (session.Mode == "on_device")
                return await PullNewRecordingAsync(udid, session, ct);

            if (session.IsStreamMode)
            {
                if (session.Mode == "airplay")
                {
                    // uxplay: SIGTERM → finaliza MP4 → renombra al path esperado
                    await StopAirplayAsync(session, ct);
                    // Corregir desfase A/V: en AirPlay el audio llega antes que el video
                    if (File.Exists(session.OutputPath))
                        await FixAirplaySyncAsync(session.OutputPath, ct);
                }
                else
                {
                    // SIGTERM al Python DVT recorder → espera a que cierre ffmpeg → MP4 listo
                    await StopDvtRecorderAsync(session, ct);

                    // with_mic: mux audio PC en el MP4
                    if (session.Mode == "with_mic")
                    {
                        StopAudio(session);
                        if (session.AudioPath is not null && File.Exists(session.OutputPath))
                            await MuxAudioIntoVideoAsync(session.OutputPath, session.AudioPath, ct);
                    }
                }
            }
            else
            {
                // Burst fallback (incluye mux de audio si with_mic)
                await StopBurstAsync(session, ct);
            }

            // Interpolación solo para grabaciones DVT/burst (airplay ya es 30fps nativo)
            if (session.Mode != "airplay" && File.Exists(session.OutputPath))
                _ = Task.Run(() => RunInterpolationsAsync(session.OutputPath));

            return session.OutputPath;
        }
        finally { _recordLock.Release(); }
    }

    // ── DVT recorder mode ────────────────────────────────────────────────────
    //
    // El helper Python ios_dvt_recorder.py mantiene un tunnel DVT abierto,
    // captura screenshots vía com.apple.instruments.server.services.screenshot
    // (~2 FPS, limitado por iOS 26) y los envía a ffmpeg para generar el MP4.
    // Funciona en iOS 26.5 donde startmediastream exige iOS 27.

    private const string DvtRecorderScript = @"
import asyncio, sys, os, subprocess, signal, argparse, time, warnings
warnings.filterwarnings('ignore')

FFMPEG_PATHS = ['ffmpeg', '/opt/homebrew/bin/ffmpeg', '/usr/local/bin/ffmpeg']

def find_ffmpeg():
    for p in FFMPEG_PATHS:
        if os.path.isabs(p):
            if os.path.exists(p): return p
        else:
            for d in os.environ.get('PATH','').split(':'):
                if os.path.exists(os.path.join(d, p)): return os.path.join(d, p)
    return 'ffmpeg'

async def capture_loop(ss, ffmpeg_proc, fps, stop_event):
    frame_interval = 1.0 / fps
    frame_count = 0
    while not stop_event.is_set():
        t0 = time.time()
        try:
            data = await asyncio.wait_for(ss.get_screenshot(), timeout=5.0)
        except asyncio.TimeoutError:
            continue
        except Exception as e:
            if stop_event.is_set(): break
            await asyncio.sleep(0.1)
            continue
        try:
            ffmpeg_proc.stdin.write(data)
            ffmpeg_proc.stdin.flush()
        except (BrokenPipeError, OSError):
            break
        frame_count += 1
        elapsed = time.time() - t0
        remaining = frame_interval - elapsed
        if remaining > 0 and not stop_event.is_set():
            await asyncio.sleep(remaining)
    print(f'frames={frame_count}', file=sys.stderr, flush=True)

async def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('output')
    parser.add_argument('--fps', type=float, default=2.0)
    args = parser.parse_args()

    from pymobiledevice3.remote.userspace_tunnel import establish_userspace_rsd
    from pymobiledevice3.services.dvt.instruments.screenshot import Screenshot
    from pymobiledevice3.services.dvt.instruments.dvt_provider import DvtProvider

    stop_event = asyncio.Event()
    loop = asyncio.get_event_loop()
    loop.add_signal_handler(signal.SIGTERM, stop_event.set)
    loop.add_signal_handler(signal.SIGINT, stop_event.set)

    ffmpeg = find_ffmpeg()
    ffmpeg_proc = subprocess.Popen(
        [ffmpeg, '-y', '-f', 'image2pipe', '-vcodec', 'png', '-r', str(args.fps),
         '-i', 'pipe:0', '-vf', 'scale=trunc(iw/2)*2:trunc(ih/2)*2',
         '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '23', args.output],
        stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    rsd = await establish_userspace_rsd()
    print('READY', file=sys.stderr, flush=True)

    async with DvtProvider(rsd) as dvt:
        async with Screenshot(dvt) as ss:
            await capture_loop(ss, ffmpeg_proc, args.fps, stop_event)

    try: ffmpeg_proc.stdin.close()
    except Exception: pass
    try: ffmpeg_proc.wait(timeout=30)
    except subprocess.TimeoutExpired: ffmpeg_proc.kill()

asyncio.run(main())
";

    private string EnsureDvtScript()
    {
        if (_dvtScriptPath is not null && File.Exists(_dvtScriptPath))
            return _dvtScriptPath;
        var path = Path.Combine(Path.GetTempPath(), "ios_dvt_recorder.py");
        File.WriteAllText(path, DvtRecorderScript);
        _dvtScriptPath = path;
        return path;
    }

    private async Task<bool> TryStartDvtRecorderAsync(IosSession session, CancellationToken ct)
    {
        var scriptPath = EnsureDvtScript();

        var proc = Process.Start(new ProcessStartInfo
        {
            FileName  = Python,
            Arguments = $"\"{scriptPath}\" \"{session.OutputPath}\" --fps 2",
            RedirectStandardError  = true,
            UseShellExecute = false,
            CreateNoWindow  = true,
        });
        if (proc is null) return false;

        // Leer stderr hasta "READY" (tunnel establecido) o error/EOF
        using var readyCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var linked   = CancellationTokenSource.CreateLinkedTokenSource(ct, readyCts.Token);
        bool ready = false;
        try
        {
            while (!linked.Token.IsCancellationRequested)
            {
                var line = await proc.StandardError.ReadLineAsync(linked.Token);
                if (line is null) break;
                _log.LogDebug("DVT recorder: {Line}", line);
                if (line.Contains("READY")) { ready = true; break; }
            }
        }
        catch (OperationCanceledException) { }

        if (!ready || proc.HasExited)
        {
            try { proc.Kill(); } catch { }
            return false;
        }

        session.StreamProc   = proc;
        session.IsStreamMode = true;
        _log.LogInformation("iOS DVT recorder iniciado (~2 FPS via DVT/screenshot service)");
        return true;
    }

    private async Task StopDvtRecorderAsync(IosSession session, CancellationToken ct)
    {
        if (session.StreamProc is null || session.StreamProc.HasExited) return;

        // SIGTERM: el script para la captura, cierra ffmpeg stdin, espera al MP4
        await GracefulStopAsync(session.StreamProc, TimeSpan.FromSeconds(40), ct);

        _log.LogInformation("DVT recorder detenido (exit {Code})", session.StreamProc.HasExited ? session.StreamProc.ExitCode : -1);
    }

    // Detiene un proceso con una señal Unix (15=SIGTERM por default, 2=SIGINT para qvh que
    // solo escucha os.Interrupt) y espera a que cierre solo hasta `timeout`; si no responde,
    // lo mata a la fuerza. Usado por los procesos que necesitan flushear ffmpeg/GStreamer
    // antes de salir (DVT recorder, uxplay, qvh) — un Kill() directo corrompería el archivo
    // de salida a medio escribir.
    private static async Task GracefulStopAsync(Process proc, TimeSpan timeout, CancellationToken ct, int signal = 15)
    {
        try
        {
            using var sig = Process.Start(new ProcessStartInfo
            {
                FileName = "kill", Arguments = $"-{signal} {proc.Id}",
                UseShellExecute = false, CreateNoWindow = true,
            });
            sig?.WaitForExit();
        }
        catch { }

        using var t = new CancellationTokenSource(timeout);
        using var l = CancellationTokenSource.CreateLinkedTokenSource(ct, t.Token);
        try   { await proc.WaitForExitAsync(l.Token); }
        catch { try { proc.Kill(); } catch { } }
    }

    // ── AirPlay mirror receiver ───────────────────────────────────────────────
    //
    // Levanta uxplay como receptor AirPlay en la red local.
    // El iPhone se conecta manualmente desde Control Center → Espejo de pantalla.
    // uxplay graba con -mp4 stem → crea "{stem}.1.H264.AAC.mp4" al conectar iPhone.
    //
    // Nota: uxplay escribe su output a la TTY, no a stderr redirigido.
    //       Usamos espera fija de 3 s para verificar que el proceso no crasheó.
    // Nota: DYLD_LIBRARY_PATH requerido para que uxplay encuentre las libs de Homebrew GLib.

    private static readonly string[] UxplayPaths = BuildToolCandidates("uxplay", "uxplay", "uxplay.exe");

    private static readonly string GlibDyldPath =
        "/opt/homebrew/lib:/opt/homebrew/opt/gstreamer/lib:/opt/homebrew/opt/glib/lib";

    private async Task<bool> TryStartAirplayRecorderAsync(IosSession session, CancellationToken ct)
    {
        var uxplay = FindBinary(UxplayPaths);
        if (uxplay is null)
        {
            _log.LogWarning("uxplay no encontrado. Compilar desde: https://github.com/FDH2/UxPlay");
            return false;
        }

        // Nombre único del receptor visible en el Centro de Control del iPhone
        var receiverName = $"Factum-{session.SessionId[..6]}";
        session.AirplayReceiverName = receiverName;

        // Stem sin extensión → uxplay crea "{stem}.1.H264.AAC.mp4"
        var mp4Stem = Path.ChangeExtension(session.OutputPath, null);

        var psi = new ProcessStartInfo
        {
            FileName        = uxplay,
            UseShellExecute = false,
            CreateNoWindow  = true,
            RedirectStandardError  = false,
            RedirectStandardOutput = false,
        };
        // DYLD_LIBRARY_PATH necesario para GLib/GObject de Homebrew
        psi.Environment["DYLD_LIBRARY_PATH"] = GlibDyldPath;
        psi.ArgumentList.Add("-n");   psi.ArgumentList.Add(receiverName);
        psi.ArgumentList.Add("-nh");  // sin @hostname en el nombre
        psi.ArgumentList.Add("-mp4"); psi.ArgumentList.Add(mp4Stem);

        var proc = Process.Start(psi);
        if (proc is null) return false;

        // Esperar 3 s — suficiente para que mDNS publique el receptor
        await Task.Delay(TimeSpan.FromSeconds(3), ct);

        if (proc.HasExited)
        {
            _log.LogWarning("uxplay terminó inesperadamente (código {Code})", proc.ExitCode);
            return false;
        }

        session.StreamProc   = proc;
        session.IsStreamMode = true;
        _log.LogInformation("AirPlay receiver listo: buscar '{Name}' en Control Center → Espejo de pantalla", receiverName);

        await _hub.BroadcastAsync(new AgentEvent
        {
            Type = "airplay_receiver_ready",
            Data = new { receiver_name = receiverName }
        });

        return true;
    }

    private async Task StopAirplayAsync(IosSession session, CancellationToken ct)
    {
        if (session.StreamProc is null || session.StreamProc.HasExited) return;

        // SIGTERM → uxplay finaliza el pipeline GStreamer y cierra el MP4
        await GracefulStopAsync(session.StreamProc, TimeSpan.FromSeconds(30), ct);

        _log.LogInformation("uxplay detenido (exit {Code})", session.StreamProc.HasExited ? session.StreamProc.ExitCode : -1);

        // Encontrar el archivo generado "{stem}.1.H264.AAC.mp4" y renombrarlo al path esperado
        var stem = Path.ChangeExtension(session.OutputPath, null);
        var dir  = Path.GetDirectoryName(session.OutputPath) ?? ".";
        var stemName = Path.GetFileName(stem);
        var candidates = Directory.GetFiles(dir, $"{stemName}.*.mp4");

        if (candidates.Length > 0)
        {
            var src = candidates[0]; // tomar el primero (suele ser .1.H264.AAC.mp4)
            if (src != session.OutputPath)
            {
                File.Move(src, session.OutputPath, overwrite: true);
                _log.LogInformation("AirPlay recording renombrado: {Src} → {Dst}", src, session.OutputPath);
            }
        }
        else
        {
            _log.LogWarning("AirPlay: no se encontró archivo MP4 (el iPhone nunca conectó o la grabación fue vacía)");
        }
    }

    // Corrige el desfase A/V de las grabaciones uxplay/AirPlay.
    //
    // uxplay escribe los paquetes de audio RAOP con PTS que no siempre coinciden
    // con el reloj de video. El problema se manifiesta en dos formas:
    //   1. Silencio inicial: el stream RAOP llega unos segundos después del video.
    //   2. Desfase mid-recording: al reproducir un segundo audio (WhatsApp, otro
    //      video), uxplay continúa el PTS de audio desde donde paró en lugar de
    //      respetar el tiempo transcurrido → el audio llega "al toque", adelantado.
    //
    // aresample=async=1 recorre TODO el archivo y ajusta los paquetes de audio a
    // sus PTS correctos: inserta silencio donde el audio llega tarde (gap) y recorta
    // audio que llega antes de lo esperado. Corrige ambos casos sin re-encodear video.
    private async Task FixAirplaySyncAsync(string mp4Path, CancellationToken ct)
    {
        var ffmpeg = FindBinary(FfmpegPaths);
        if (ffmpeg is null)
        {
            _log.LogWarning("ffmpeg no encontrado — omitiendo corrección A/V sync de AirPlay");
            return;
        }

        var tmp = mp4Path + ".sync.mp4";
        var r = await ProcessRunner.RunAsync(ffmpeg,
            $"-y -i \"{mp4Path}\" -c:v copy -af \"aresample=async=1\" -c:a aac -b:a 128k \"{tmp}\"", ct);

        if (r.Success && File.Exists(tmp))
        {
            var tmpLen = new FileInfo(tmp).Length;
            if (tmpLen < 4096)
            {
                File.Delete(tmp);
                _log.LogWarning("AirPlay A/V: resultado vacío ({Bytes}B) — preservando archivo original", tmpLen);
                return;
            }
            File.Move(tmp, mp4Path, overwrite: true);
            _log.LogInformation("AirPlay A/V: sincronización corregida con aresample=async=1");
        }
        else
        {
            try { File.Delete(tmp); } catch { }
            _log.LogWarning("AirPlay A/V sync fix falló: {E}", r.Stderr);
        }
    }

    // ── Burst screenshot fallback ─────────────────────────────────────────────

    private void StartBurst(IosSession session, string udid)
    {
        session.BurstCts  = new CancellationTokenSource();
        session.BurstTask = Task.Run(async () =>
        {
            int i     = 0;
            var token = session.BurstCts.Token;
            while (!token.IsCancellationRequested)
            {
                i++;
                var path = Path.Combine(session.DataDir, $"ios_{session.SessionId}_{i:D4}.png");
                var r = await ProcessRunner.RunAsync(Python,
                    $"-m pymobiledevice3 developer core-device screen-capture screenshot --userspace \"{path}\"", token);
                if (r.Success)
                    lock (session.FramesLock) session.Frames.Add(path);
            }
        });
        _log.LogInformation("iOS burst recording iniciada (fallback)");
    }

    private async Task StopBurstAsync(IosSession session, CancellationToken ct)
    {
        session.BurstCts?.Cancel();
        if (session.BurstTask is not null)
            try { await session.BurstTask.WaitAsync(TimeSpan.FromSeconds(5), ct); } catch { }

        List<string> frames;
        lock (session.FramesLock) frames = [..session.Frames];
        if (frames.Count == 0) { _log.LogWarning("iOS burst: sin frames"); return; }

        if (session.Mode == "with_mic" && session.AudioPath is not null)
        {
            var ffmpeg = FindBinary(FfmpegPaths) ?? "ffmpeg";
            var listPath = session.OutputPath + ".txt";
            await WriteConcatListAsync(frames, listPath);
            var r = await ProcessRunner.RunAsync(ffmpeg,
                $"-y -f concat -safe 0 -i \"{listPath}\" " +
                $"-i \"{session.AudioPath}\" " +
                $"-vf scale=trunc(iw/2)*2:trunc(ih/2)*2 " +
                $"-c:v libx264 -c:a aac -pix_fmt yuv420p -shortest \"{session.OutputPath}\"", ct);
            try { File.Delete(listPath); } catch { }
            if (!r.Success) _log.LogWarning("ffmpeg burst+mic falló: {E}", r.Stderr);
        }
        else
        {
            var ffmpeg   = FindBinary(FfmpegPaths) ?? "ffmpeg";
            var listPath = session.OutputPath + ".txt";
            await WriteConcatListAsync(frames, listPath);
            var r = await ProcessRunner.RunAsync(ffmpeg,
                $"-y -f concat -safe 0 -i \"{listPath}\" " +
                $"-vf scale=trunc(iw/2)*2:trunc(ih/2)*2 " +
                $"-c:v libx264 -pix_fmt yuv420p -crf 23 \"{session.OutputPath}\"", ct);
            try { File.Delete(listPath); } catch { }
            if (!r.Success) _log.LogWarning("ffmpeg burst falló: {E}", r.Stderr);
        }

        foreach (var f in frames) try { File.Delete(f); } catch { }
    }

    private static async Task WriteConcatListAsync(List<string> frames, string listPath)
    {
        await using var f = File.CreateText(listPath);
        foreach (var frame in frames)
            await f.WriteLineAsync($"file '{Path.GetFullPath(frame)}'\nduration 1");
        if (frames.Count > 0)
            await f.WriteLineAsync($"file '{Path.GetFullPath(frames[^1])}'");
    }

    // ── ffmpeg helpers ────────────────────────────────────────────────────────

    // Re-mux video (ya codificado) + audio capturado por mic → reemplaza el archivo de video.
    private async Task MuxAudioIntoVideoAsync(string videoPath, string audioPath, CancellationToken ct)
    {
        var ffmpeg = FindBinary(FfmpegPaths) ?? "ffmpeg";
        var tmp    = videoPath + ".mux.mp4";
        var r = await ProcessRunner.RunAsync(ffmpeg,
            $"-y -i \"{videoPath}\" -i \"{audioPath}\" " +
            $"-c:v copy -c:a aac -b:a 128k -shortest \"{tmp}\"", ct);
        if (!r.Success) { _log.LogWarning("mux audio falló: {E}", r.Stderr); try { File.Delete(tmp); } catch { } return; }
        File.Delete(videoPath);
        File.Move(tmp, videoPath);
        try { File.Delete(audioPath); } catch { }
    }

    // ── Audio capture (with_mic — micrófono del PC) ───────────────────────────

    private void StartAudio(IosSession session)
    {
        var audioPath = Path.Combine(session.DataDir, $"ios_audio_{session.SessionId}.m4a");
        var ffmpeg    = FindBinary(FfmpegPaths) ?? "ffmpeg";
        var inputArgs = OperatingSystem.IsWindows()  ? "-f wasapi -i default" :
                        OperatingSystem.IsMacOS()    ? "-f avfoundation -i :0" :
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
        session.AudioProc = proc;
        session.AudioPath = audioPath;
        _log.LogInformation("Captura de audio (mic) iniciada");
    }

    private void StopAudio(IosSession session)
    {
        if (session.AudioProc is null || session.AudioProc.HasExited) return;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                session.AudioProc.Kill();
            }
            else
            {
                using var sigint = Process.Start(new ProcessStartInfo
                {
                    FileName = "kill", Arguments = $"-2 {session.AudioProc.Id}",
                    UseShellExecute = false, CreateNoWindow = true,
                });
                sigint?.WaitForExit();
            }
            session.AudioProc.WaitForExit();
        }
        catch { }
    }

    // ── On-device recording ───────────────────────────────────────────────────
    // Bug fix: afc ls devuelve rutas completas ("DCIM/100APPLE") no solo el nombre de carpeta.
    // Hay que usar esas rutas directamente en la siguiente llamada a afc ls.

    private async Task<List<string>> ListDCIMFilesAsync(string udid, CancellationToken ct)
    {
        // Líneas: "DCIM", "DCIM/100APPLE", "DCIM/101APPLE", "DCIM/.MISC" ...
        var r = await ProcessRunner.RunAsync(Python,
            $"-m pymobiledevice3 afc ls --udid {udid} DCIM", ct);
        if (!r.Success) return [];

        var files = new List<string>();
        foreach (var line in r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var folderPath = line.Trim(); // "DCIM/100APPLE"
            var folderName = Path.GetFileName(folderPath);
            if (folderPath == "DCIM" || folderName.StartsWith('.')) continue;

            // Listar contenido de esa carpeta — afc ls devuelve rutas completas también
            var sub = await ProcessRunner.RunAsync(Python,
                $"-m pymobiledevice3 afc ls --udid {udid} {folderPath}", ct);

            foreach (var subLine in sub.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var filePath = subLine.Trim(); // "DCIM/100APPLE/IMG_0135.MOV"
                if (filePath == folderPath) continue; // saltar la carpeta misma
                var ext = Path.GetExtension(filePath).ToUpperInvariant();
                if (ext is ".MOV" or ".MP4") files.Add(filePath);
            }
        }
        return files;
    }

    private async Task<string> PullNewRecordingAsync(string udid, IosSession session, CancellationToken ct)
    {
        var current  = await ListDCIMFilesAsync(udid, ct);
        var existing = session.ExistingDCIMFiles.ToHashSet();
        var newFiles = current.Where(f => !existing.Contains(f)).ToList();

        if (newFiles.Count == 0)
        {
            _log.LogWarning("iOS on_device: sin archivos nuevos, usando el más reciente");
            newFiles = current.Count > 0 ? [current[^1]] : [];
        }
        if (newFiles.Count == 0)
            throw new InvalidOperationException("No se encontraron grabaciones en el DCIM del dispositivo");

        newFiles.Sort();
        var remote  = newFiles[^1]; // "DCIM/100APPLE/IMG_0001.MOV"
        var outPath = Path.ChangeExtension(session.OutputPath, ".mov");

        // Syntax: afc pull -i REMOTE_FILE LOCAL_FILE  (donde -i = --ignore-errors, requerido)
        var r = await ProcessRunner.RunAsync(Python,
            $"-m pymobiledevice3 afc pull --udid {udid} -i {remote} \"{outPath}\"", ct);
        if (!r.Success) throw new InvalidOperationException($"afc pull falló: {r.Stderr}");

        _log.LogInformation("iOS on_device: extraído {Remote} → {Local}", remote, outPath);
        return outPath;
    }

    // ── Frame interpolation (background) ─────────────────────────────────────
    //
    // Genera variantes interpoladas del video iOS (2 FPS → 30 FPS) en background.
    // Transmite video_variant_ready por WebSocket cuando cada variante está lista.

    private async Task RunInterpolationsAsync(string videoPath)
    {
        var stem   = Path.GetFileNameWithoutExtension(videoPath);
        var dir    = Path.GetDirectoryName(videoPath)!;
        var ffmpeg = FindBinary(FfmpegPaths) ?? "ffmpeg";

        // 1. minterpolate blend (~2s) — frame blending simple
        var blendPath = Path.Combine(dir, stem + "_blend.mp4");
        var r = await ProcessRunner.RunAsync(ffmpeg,
            $"-y -i \"{videoPath}\" " +
            $"-vf \"minterpolate=fps=30:mi_mode=blend\" " +
            $"-c:v libx264 -pix_fmt yuv420p -crf 23 \"{blendPath}\"");
        if (r.Success)
        {
            _log.LogInformation("Variante blend lista: {Path}", blendPath);
            await _hub.BroadcastAsync(new AgentEvent
            {
                Type = "video_variant_ready",
                Data = new
                {
                    original_filename = Path.GetFileName(videoPath),
                    label             = "Suavizado",
                    filename          = Path.GetFileName(blendPath),
                    url               = $"/files/{Path.GetFileName(blendPath)}"
                }
            });
        }
        else _log.LogWarning("Interpolación blend falló: {E}", r.Stderr);

        // 2. minterpolate MCI (~30-60s) — compensación de movimiento
        var mciPath = Path.Combine(dir, stem + "_mci.mp4");
        r = await ProcessRunner.RunAsync(ffmpeg,
            $"-y -i \"{videoPath}\" " +
            $"-vf \"minterpolate=fps=30:mi_mode=mci:mc_mode=aobmc:me=umh:vsbmc=1\" " +
            $"-c:v libx264 -pix_fmt yuv420p -crf 23 \"{mciPath}\"");
        if (r.Success)
        {
            _log.LogInformation("Variante MCI lista: {Path}", mciPath);
            await _hub.BroadcastAsync(new AgentEvent
            {
                Type = "video_variant_ready",
                Data = new
                {
                    original_filename = Path.GetFileName(videoPath),
                    label             = "MCI",
                    filename          = Path.GetFileName(mciPath),
                    url               = $"/files/{Path.GetFileName(mciPath)}"
                }
            });
        }
        else _log.LogWarning("Interpolación MCI falló: {E}", r.Stderr);

        // 3. RIFE (si rife-ncnn-vulkan está instalado)
        var rife = FindBinary(RifePaths);
        if (rife is not null)
            await RunRifeInterpolationAsync(videoPath, stem, dir, ffmpeg, rife);
    }

    private async Task RunRifeInterpolationAsync(string videoPath, string stem, string dir,
        string ffmpeg, string rife)
    {
        var framesIn  = Path.Combine(Path.GetTempPath(), $"rife_in_{stem}");
        var framesOut = Path.Combine(Path.GetTempPath(), $"rife_out_{stem}");
        var rifePath  = Path.Combine(dir, stem + "_rife.mp4");
        try
        {
            Directory.CreateDirectory(framesIn);
            Directory.CreateDirectory(framesOut);

            // Extraer frames del video
            var extract = await ProcessRunner.RunAsync(ffmpeg,
                $"-y -i \"{videoPath}\" -vsync passthrough \"{framesIn}/%08d.png\"");
            if (!extract.Success) { _log.LogWarning("RIFE: extracción de frames falló: {E}", extract.Stderr); return; }

            // Interpolar con RIFE (2^4 = 16×)
            var rifeRun = await ProcessRunner.RunAsync(rife,
                $"-i \"{framesIn}\" -o \"{framesOut}\" -r 4 -j 1:1:1");
            if (!rifeRun.Success) { _log.LogWarning("RIFE: falló: {E}", rifeRun.Stderr); return; }

            // Re-encodear a 30 fps
            var encode = await ProcessRunner.RunAsync(ffmpeg,
                $"-y -framerate 30 -i \"{framesOut}/%08d.png\" " +
                $"-c:v libx264 -pix_fmt yuv420p -crf 23 \"{rifePath}\"");
            if (!encode.Success) { _log.LogWarning("RIFE: re-encode falló: {E}", encode.Stderr); return; }

            _log.LogInformation("Variante RIFE lista: {Path}", rifePath);
            await _hub.BroadcastAsync(new AgentEvent
            {
                Type = "video_variant_ready",
                Data = new
                {
                    original_filename = Path.GetFileName(videoPath),
                    label             = "RIFE",
                    filename          = Path.GetFileName(rifePath),
                    url               = $"/files/{Path.GetFileName(rifePath)}"
                }
            });
        }
        finally
        {
            try { Directory.Delete(framesIn, recursive: true); } catch { }
            try { Directory.Delete(framesOut, recursive: true); } catch { }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? FindBinary(string[] candidates) =>
        candidates.FirstOrDefault(p =>
            p.Contains('/') ? File.Exists(p) : IsOnPath(p));

    private static bool IsOnPath(string exe)
    {
        var sep   = OperatingSystem.IsWindows() ? ';' : ':';
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(sep) ?? [];
        return paths.Any(dir => File.Exists(Path.Combine(dir, exe)));
    }

    private static string FriendlyModel(string pt) => pt switch
    {
        "iPhone12,1" => "iPhone 11",      "iPhone12,3" => "iPhone 11 Pro",
        "iPhone12,5" => "iPhone 11 Pro Max",
        "iPhone13,1" => "iPhone 12 mini", "iPhone13,2" => "iPhone 12",
        "iPhone13,3" => "iPhone 12 Pro",  "iPhone13,4" => "iPhone 12 Pro Max",
        "iPhone14,2" => "iPhone 13 Pro",  "iPhone14,3" => "iPhone 13 Pro Max",
        "iPhone14,4" => "iPhone 13 mini", "iPhone14,5" => "iPhone 13",
        "iPhone15,2" => "iPhone 14 Pro",  "iPhone15,3" => "iPhone 14 Pro Max",
        "iPhone15,4" => "iPhone 14",      "iPhone15,5" => "iPhone 14 Plus",
        "iPhone16,1" => "iPhone 15 Pro",  "iPhone16,2" => "iPhone 15 Pro Max",
        "iPhone16,3" => "iPhone 15",      "iPhone16,4" => "iPhone 15 Plus",
        "iPhone17,1" => "iPhone 16 Pro",  "iPhone17,2" => "iPhone 16 Pro Max",
        "iPhone17,3" => "iPhone 16",      "iPhone17,4" => "iPhone 16 Plus",
        _ => pt
    };

    private static Device MockDevice() => new()
    {
        Serial = "mock-ios-udid-001", State = "device", Manufacturer = "Apple",
        Model = "iPhone 15 (mock)", Platform = "ios", IosVersion = "17.4",
        Name = "iPhone mock", Operator = string.Empty
    };
}
