using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services.Ios;
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

    // ios-herramientas-windows §6.6: AirPlay (uxplay) disponible en esta PC; en Windows nunca (D1 b).
    bool AirplayAvailable { get; }
    // "not_supported_on_windows" | "uxplay_not_found"; null si AirPlay está disponible.
    string? AirplayUnavailableReason { get; }
    // §4.3: "enabled" | "restarting" | "manual_required".
    Task<string> EnableDeveloperModeAsync(string udid, CancellationToken ct = default);
}

// Modos de grabación iOS:
//   video_only  → capturas DVT → ffmpeg MP4 (~2 FPS; limitación de iOS 26)
//   with_mic    → igual que video_only + micrófono del PC mezclado en el MP4 final
//   on_device   → grabación nativa iOS (Control Center); se extrae del DCIM al detener
//   airplay     → receptor uxplay (solo Mac; en Windows no está disponible, D1 b)
//
// iOS 26.5 bloquea com.apple.coredevice.feature.startmediastream (requiere iOS 27+).
// En su lugar usamos com.apple.instruments.server.services.screenshot vía DVT, que funciona con el
// DDI montado. Todo lo de pymobiledevice3 pasa por un solo helper Python multiplataforma
// (Services/Ios/ios_helper.py, SDD ios-herramientas-windows §6.2): listado, preparación del iPhone
// (Modo Desarrollador + DDI), captura, grabación (DVT → pipe PNG → ffmpeg H.264) y Modo Desarrollador.
public sealed class IosService : IIosService
{
    // Las herramientas externas (ffmpeg, qvh, uxplay, rife) se resuelven con el helper común
    // ToolResolver: tools/<dir>/<exe> junto al exe → PATH → Homebrew (solo fuera de Windows). qvh
    // (danielpaulus/quicktime_video_hack, MIT) reimplementa el protocolo de espejado por USB de
    // QuickTime y no tiene binario de Windows (el upstream abandonó ese port).

    private static readonly TimeSpan DevicesTimeout    = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PrepareTimeout    = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan ScreenshotTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DevModeTimeout    = TimeSpan.FromSeconds(60);
    private const double DvtFps = 2.0;

    private readonly bool _mock;
    private readonly bool _isWindows = OperatingSystem.IsWindows();
    private readonly string? _micDevice;
    private readonly ILogger<IosService> _log;
    private readonly AgentWebSocketHub _hub;
    private readonly IFileStorageService _storage;
    private readonly AppleServiceProbe _probe;
    private readonly SemaphoreSlim _recordLock = new(1, 1);
    private readonly SemaphoreSlim _shotLock = new(1, 1);
    // §6.4: iPhones con Modo Desarrollador y DDI comprobados (se invalida al desconectarse).
    private readonly ConcurrentDictionary<string, bool> _prepared = new(StringComparer.Ordinal);
    private IosSession? _session;
    private AirplayShotSession? _shotSession;
    private int _appleServiceMissingLogged; // 1 = ya se avisó (se rearma cuando vuelve el servicio)

    public IosService(IOptions<AgentOptions> opts, ILogger<IosService> log, AgentWebSocketHub hub,
        IFileStorageService storage, AppleServiceProbe probe)
    {
        _mock      = opts.Value.Mock;
        _micDevice = opts.Value.MicDevice;
        _log       = log;
        _hub       = hub;
        _storage   = storage;
        _probe     = probe;
    }

    // ── Session state ─────────────────────────────────────────────────────────

    private sealed class IosSession
    {
        public string Mode       = string.Empty;
        public string OutputPath = string.Empty;
        public string SessionId  = string.Empty;
        public string DataDir    = string.Empty;
        // DVT recorder (helper Python que maneja su propio ffmpeg)
        public DvtRecorder? Dvt;
        // AirPlay (uxplay)
        public Process? StreamProc;
        public bool IsStreamMode;
        // burst fallback (último recurso si DVT falla)
        public List<string> Frames = [];
        public readonly object FramesLock = new();
        public CancellationTokenSource? BurstCts;
        public Task? BurstTask;
        // Error de DVT que hizo caer al burst: motivo de ios_recording_empty si el burst queda vacío.
        public IosException? StartError;
        // audio (with_mic → micrófono de la PC vía ffmpeg, MicCapture)
        public MicCapture? Mic;
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

    // ── Python, helper y disponibilidad (§6.1) ────────────────────────────────

    public bool IsAvailable => _mock || IosHelper.ResolvePython() is not null;

    public bool AirplayAvailable => _mock || (!_isWindows && FindBinary(AgentTools.Uxplay) is not null);

    public string? AirplayUnavailableReason =>
        _mock ? null
        : _isWindows ? "not_supported_on_windows"
        : FindBinary(AgentTools.Uxplay) is null ? "uxplay_not_found"
        : null;

    private string RequirePython() =>
        IosHelper.ResolvePython() ?? throw IosErrors.Create(IosErrors.ToolsMissing, _isWindows);

    private string RequireFfmpeg() =>
        FindBinary(AgentTools.Ffmpeg) ?? throw IosErrors.Create(IosErrors.ToolsMissing, _isWindows);

    private IosException AirplayUnavailableError() => IosErrors.Create(IosErrors.AirplayUnavailable, _isWindows);

    // §6.5: las operaciones con UDID no lanzan Python si falta el servicio de Apple.
    private async Task EnsureAppleServiceAsync(CancellationToken ct)
    {
        if (await _probe.GetStateAsync(ct) == AppleServiceState.Missing)
            throw IosErrors.Create(IosErrors.AppleServiceMissing, _isWindows);
    }

    private Task<string> RunHelperAsync(string subcommand, IEnumerable<string> args, TimeSpan timeout,
        CancellationToken ct)
    {
        var python = RequirePython();
        var helper = IosHelper.EnsureHelperFile();
        return IosHelper.RunAsync(python, helper, subcommand, args, timeout, _log, ct);
    }

    // ── Device listing (§6.8) ─────────────────────────────────────────────────

    public async Task<List<Device>> ListDevicesAsync(CancellationToken ct = default)
    {
        if (_mock) return [MockDevice()];
        if (!IsAvailable) return [];

        // §6.5: sin el servicio de Apple no se lanza Python cada 3 s; se avisa una sola vez.
        if (await _probe.GetStateAsync(ct) == AppleServiceState.Missing)
        {
            if (Interlocked.Exchange(ref _appleServiceMissingLogged, 1) == 0)
                _log.LogWarning(
                    "Servicio de dispositivos de Apple no encontrado (127.0.0.1:27015): los iPhone no se van a ver hasta instalar \"Apple Devices\"");
            return [];
        }
        Interlocked.Exchange(ref _appleServiceMissingLogged, 0);

        string stdout;
        try
        {
            stdout = await RunHelperAsync("devices", [], DevicesTimeout, ct);
        }
        catch (IosException ex)
        {
            _log.LogDebug("Listado de iPhone falló: {E}", ex.Message);
            return [];
        }

        var devices = ParseDevices(stdout);
        if (devices is null) return [];

        // Un iPhone que se desconectó (o se reinició) pierde el DDI montado (A7): se vuelve a preparar.
        var present = devices.Select(d => d.Serial).ToHashSet(StringComparer.Ordinal);
        foreach (var udid in _prepared.Keys)
            if (!present.Contains(udid)) _prepared.TryRemove(udid, out _);
        return devices;
    }

    // Salida de `ios_helper.py devices`: [{udid, name, product_type, product_version, imei, phone_number}].
    internal static List<Device>? ParseDevices(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout)) return [];
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var devices = new List<Device>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var udid = Str(item, "udid");
                if (string.IsNullOrEmpty(udid)) continue;
                var d = new Device
                {
                    Serial = udid, State = "device", Manufacturer = "Apple", Platform = "ios",
                    Name = Str(item, "name"),
                    Imei = Str(item, "imei"),
                    Operator = Str(item, "phone_number"),
                };
                var productType = Str(item, "product_type");
                if (productType.Length > 0) d.Model = FriendlyModel(productType);
                var version = Str(item, "product_version");
                if (version.Length > 0)
                {
                    d.IosVersion = version;
                    if (int.TryParse(version.Split('.')[0], out var maj)) d.AndroidVersion = maj;
                }
                devices.Add(d);
            }
            return devices;
        }
        catch (JsonException) { return null; }

        static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }

    // ── Preparar el iPhone: Modo Desarrollador + DDI (§6.4) ───────────────────

    private async Task EnsurePreparedAsync(string udid, CancellationToken ct)
    {
        if (_prepared.ContainsKey(udid)) return;
        var stdout = await RunHelperAsync("prepare", ["--udid", udid], PrepareTimeout, ct);
        var ddi = "?";
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            if (doc.RootElement.TryGetProperty("ddi", out var v) && v.ValueKind == JsonValueKind.String)
                ddi = v.GetString() ?? "?";
        }
        catch (JsonException) { }
        _prepared[udid] = true;
        _log.LogInformation("iPhone {Udid}: Modo Desarrollador activo, imagen de desarrollador {Ddi}", udid, ddi);
    }

    public async Task<string> EnableDeveloperModeAsync(string udid, CancellationToken ct = default)
    {
        if (_mock) return "enabled";
        RequirePython();
        await EnsureAppleServiceAsync(ct);
        var stdout = await RunHelperAsync("devmode", ["--udid", udid], DevModeTimeout, ct);
        string? status = null;
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            if (doc.RootElement.TryGetProperty("status", out var v) && v.ValueKind == JsonValueKind.String)
                status = v.GetString();
        }
        catch (JsonException) { }
        if (status is not ("enabled" or "restarting" or "manual_required"))
            throw IosErrors.Create(IosErrors.CaptureFailed, _isWindows, "respuesta inesperada del helper");

        // El iPhone se reinicia: el DDI se desmonta y hay que volver a prepararlo.
        if (status == "restarting") _prepared.TryRemove(udid, out _);
        _log.LogInformation("iPhone {Udid}: Modo Desarrollador → {Status}", udid, status);
        return status;
    }

    // ── Screenshot (§6.3) ─────────────────────────────────────────────────────
    //
    // DVT (com.apple.instruments.server.services.screenshot) con el túnel en modo usuario de
    // pymobiledevice3 en iOS 17+ (sin administrador). Con method="auto": DVT → USB/qvh (solo si
    // existe qvh) → AirPlay (solo si está disponible; requiere que el fiscal active Espejo de
    // pantalla a mano). Un error "definitivo" de DVT corta la cadena; si todo falla o se saltea,
    // se lanza el error de DVT (el más informativo).

    public async Task<string> TakeScreenshotAsync(string udid, string outputPath,
        string method = "auto", CancellationToken ct = default)
    {
        if (_mock)
        {
            await File.WriteAllBytesAsync(outputPath, Array.Empty<byte>(), ct);
            return outputPath;
        }

        switch (method)
        {
            case "dvt":
                return await TakeDvtScreenshotAsync(udid, outputPath, ct);
            case "usb":
                return await TakeUsbScreenshotAsync(udid, outputPath, ct);
            case "airplay":
                if (!AirplayAvailable) throw AirplayUnavailableError();
                return await TakeAirplayScreenshotAsync(outputPath, ct);
            case "auto":
                break;
            default:
                throw new InvalidOperationException($"Método de captura desconocido: {method}");
        }

        IosException dvtError;
        try
        {
            return await TakeDvtScreenshotAsync(udid, outputPath, ct);
        }
        catch (IosException ex)
        {
            if (IosErrors.IsDefinitive(ex.Code)) throw;
            dvtError = ex;
        }

        if (FindBinary(AgentTools.Qvh) is not null)
        {
            _log.LogWarning("DVT screenshot falló ({Code}); probando USB (qvh)", dvtError.Code);
            try { return await TakeUsbScreenshotAsync(udid, outputPath, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning("USB screenshot (qvh) falló ({E})", ex.Message);
            }
        }

        if (AirplayAvailable)
        {
            _log.LogWarning("DVT screenshot falló ({Code}); probando AirPlay", dvtError.Code);
            try { return await TakeAirplayScreenshotAsync(outputPath, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning("AirPlay screenshot falló ({E})", ex.Message);
            }
        }

        throw dvtError;
    }

    private async Task<string> TakeDvtScreenshotAsync(string udid, string outputPath,
        CancellationToken ct)
    {
        RequirePython();
        await EnsureAppleServiceAsync(ct);
        await EnsurePreparedAsync(udid, ct);
        await RunHelperAsync("screenshot", ["--udid", udid, "--output", outputPath], ScreenshotTimeout, ct);
        // El helper ya lo verifica, pero el éxito real es el archivo: exit 0 Y PNG > 0 bytes.
        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            throw IosErrors.Create(IosErrors.CaptureFailed, _isWindows, "el archivo de la captura quedó vacío");
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
        var qvh = FindBinary(AgentTools.Qvh)
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
            await GracefulStopAsync(proc, TimeSpan.FromSeconds(10), ct, signal: 2, name: "qvh"); // SIGINT

            if (!File.Exists(tmpH264) || new FileInfo(tmpH264).Length == 0)
                throw new InvalidOperationException("qvh no generó stream de video (¿cable USB conectado?)");

            var ffmpeg = FindBinary(AgentTools.Ffmpeg) ?? "ffmpeg";
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
        var uxplay = (AirplayAvailable ? FindBinary(AgentTools.Uxplay) : null)
            ?? throw AirplayUnavailableError();

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
            await GracefulStopAsync(proc, TimeSpan.FromSeconds(15), ct, name: "uxplay");

            var ffmpeg = FindBinary(AgentTools.Ffmpeg) ?? "ffmpeg";
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

            var uxplay = (AirplayAvailable ? FindBinary(AgentTools.Uxplay) : null)
                ?? throw AirplayUnavailableError();

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
                await GracefulStopAsync(session.UxplayProc, TimeSpan.FromSeconds(30), ct, name: "uxplay");

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

            var ffmpeg = FindBinary(AgentTools.Ffmpeg) ?? "ffmpeg";
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

            if (_mock)
            {
                _session = session;
                _log.LogInformation("iOS recording (mock): mode={Mode}", mode);
                return;
            }

            // AirPlay mirror receiver — conecta el iPhone por WiFi, graba 30fps + audio nativo.
            // Solo Mac (D1 b): en Windows, o sin uxplay, airplay_unavailable.
            if (mode == "airplay")
            {
                if (!AirplayAvailable) throw AirplayUnavailableError();
                if (!await TryStartAirplayRecorderAsync(session, ct))
                    throw new IosException(IosErrors.AirplayUnavailable,
                        "No se pudo iniciar el receptor AirPlay. Instalá uxplay: brew install uxplay");
                _session = session;
                return;
            }

            RequirePython();
            await EnsureAppleServiceAsync(ct);

            if (mode == "on_device")
            {
                session.ExistingDCIMFiles = await ListDCIMFilesAsync(udid, ct);
                _log.LogInformation("iOS on_device: snapshot DCIM ({N} archivos)", session.ExistingDCIMFiles.Count);
                _session = session;
                return;
            }

            // DVT recorder: captura de pantalla continua vía com.apple.instruments.server.services.screenshot.
            // Un error definitivo (§6.7) se lanza sin caer al burst.
            if (!await TryStartDvtRecorderAsync(session, udid, ct))
            {
                _log.LogWarning("DVT recorder falló; fallback a burst screenshots");
                StartBurst(session, udid);
            }
            if (mode == "with_mic") await StartAudioAsync(session, ct);
            _session = session;
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

            if (session.Mode == "airplay")
            {
                // uxplay: SIGTERM → finaliza MP4 → renombra al path esperado
                await StopAirplayAsync(session, ct);
                // Corregir desfase A/V: en AirPlay el audio llega antes que el video
                if (File.Exists(session.OutputPath))
                    await FixAirplaySyncAsync(session.OutputPath, ct);
            }
            else if (session.Dvt is { } dvt)
            {
                // with_mic: el mic se detiene antes que el recorder (SDD grabacion-android-windows §6.4).
                if (session.Mode == "with_mic") await StopAudioAsync(session);

                // "stop" por stdin → el helper cierra ffmpeg → MP4 completo (§6.7).
                DvtStopResult result;
                await using (dvt) result = await dvt.StopAsync(ct);
                if (!result.Ok)
                {
                    TryDelete(session.OutputPath);
                    if (session.Mic is { } failedMic) TryDelete(failedMic.AudioPath);
                    throw result.Error ?? IosErrors.RecordingEmptyFrom(null, IosErrors.ReasonNoFrames, _isWindows);
                }

                // with_mic: mux audio PC en el MP4
                if (session.Mode == "with_mic" && session.Mic is { } mic &&
                    File.Exists(mic.AudioPath) && File.Exists(session.OutputPath))
                    await MuxAudioIntoVideoAsync(session.OutputPath, mic.AudioPath, ct);
            }
            else
            {
                // Burst fallback (incluye mux de audio si with_mic). Antes el mic nunca se
                // detenía en este camino y el ffmpeg final leía un audio a medio escribir.
                if (session.Mode == "with_mic") await StopAudioAsync(session);
                await StopBurstAsync(session, ct);
            }

            // §4.4 b: nunca se devuelve un archivo que no existe.
            if (!File.Exists(session.OutputPath) || new FileInfo(session.OutputPath).Length == 0)
            {
                TryDelete(session.OutputPath);
                throw IosErrors.RecordingEmptyFrom(session.StartError, IosErrors.ReasonNoFrames, _isWindows);
            }

            // Interpolación solo para grabaciones DVT/burst (airplay ya es 30fps nativo)
            if (session.Mode != "airplay")
                _ = Task.Run(() => RunInterpolationsAsync(session.OutputPath));

            return session.OutputPath;
        }
        finally { _recordLock.Release(); }
    }

    // ── DVT recorder mode (§6.7) ─────────────────────────────────────────────

    // true = grabando con DVT. false = falló con un error no definitivo (queda en
    // session.StartError y se cae al burst). Un error definitivo se lanza.
    private async Task<bool> TryStartDvtRecorderAsync(IosSession session, string udid, CancellationToken ct)
    {
        try
        {
            var python = RequirePython();
            var ffmpeg = RequireFfmpeg();
            await EnsureAppleServiceAsync(ct);
            await EnsurePreparedAsync(udid, ct);
            session.Dvt = await DvtRecorder.StartAsync(python, IosHelper.EnsureHelperFile(), udid,
                session.OutputPath, ffmpeg, DvtFps, synthetic: false, _log, ct);
            return true;
        }
        catch (IosException ex) when (!IosErrors.IsDefinitiveForRecording(ex.Code))
        {
            _log.LogWarning("DVT recorder no arrancó ({Code}): {E}", ex.Code, ex.Message);
            session.StartError = ex;
            return false;
        }
    }

    // Detiene un proceso de la Mac (uxplay: SIGTERM; qvh: SIGINT, solo escucha os.Interrupt) y
    // espera a que cierre solo hasta `timeout`; si no responde, lo mata. Un Kill() directo
    // corrompería el archivo a medio escribir. En Windows no existe `kill`: se usa Ctrl+C con
    // ProcessStop (uxplay/qvh no existen hoy en Windows, D1 b).
    private async Task GracefulStopAsync(Process proc, TimeSpan timeout, CancellationToken ct, int signal = 15,
        string name = "proceso")
    {
        if (_isWindows)
        {
            await ProcessStop.StopGracefullyAsync(proc, timeout, _log, name, ct);
            return;
        }

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

    private static readonly string GlibDyldPath =
        "/opt/homebrew/lib:/opt/homebrew/opt/gstreamer/lib:/opt/homebrew/opt/glib/lib";

    private async Task<bool> TryStartAirplayRecorderAsync(IosSession session, CancellationToken ct)
    {
        var uxplay = FindBinary(AgentTools.Uxplay);
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
        // DYLD_LIBRARY_PATH necesario para GLib/GObject de Homebrew (solo macOS)
        if (OperatingSystem.IsMacOS()) psi.Environment["DYLD_LIBRARY_PATH"] = GlibDyldPath;
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
        await GracefulStopAsync(session.StreamProc, TimeSpan.FromSeconds(30), ct, name: "uxplay");

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
        var ffmpeg = FindBinary(AgentTools.Ffmpeg);
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
        var python = RequirePython();
        session.BurstCts  = new CancellationTokenSource();
        session.BurstTask = Task.Run(async () =>
        {
            int i     = 0;
            var token = session.BurstCts.Token;
            while (!token.IsCancellationRequested)
            {
                i++;
                var path = Path.Combine(session.DataDir, $"ios_{session.SessionId}_{i:D4}.png");
                var r = await ProcessRunner.RunArgumentListAsync(python,
                    ["-m", "pymobiledevice3", "developer", "core-device", "screen-capture", "screenshot",
                     "--userspace", path],
                    token, IosHelper.PythonEnvironment);
                if (r.Success && File.Exists(path))
                    lock (session.FramesLock) session.Frames.Add(path);
                else
                    // Sin esto, un iPhone que no responde lanza procesos sin parar.
                    try { await Task.Delay(500, token); } catch (OperationCanceledException) { }
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
        if (frames.Count == 0)
        {
            _log.LogWarning("iOS burst: sin frames");
            if (session.Mic is { } m) TryDelete(m.AudioPath);
            throw IosErrors.RecordingEmptyFrom(session.StartError, IosErrors.ReasonNoFrames, _isWindows);
        }

        var ffmpeg   = RequireFfmpeg();
        var listPath = session.OutputPath + ".txt";
        await WriteConcatListAsync(frames, listPath);
        ProcessResult r;
        if (session.Mode == "with_mic" && session.Mic is { } mic && File.Exists(mic.AudioPath))
        {
            r = await ProcessRunner.RunArgumentListAsync(ffmpeg,
                ["-y", "-f", "concat", "-safe", "0", "-i", listPath, "-i", mic.AudioPath,
                 "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2",
                 "-c:v", "libx264", "-c:a", "aac", "-pix_fmt", "yuv420p", "-shortest", session.OutputPath], ct);
            if (!r.Success) _log.LogWarning("ffmpeg burst+mic falló: {E}", r.Stderr);
        }
        else
        {
            r = await ProcessRunner.RunArgumentListAsync(ffmpeg,
                ["-y", "-f", "concat", "-safe", "0", "-i", listPath,
                 "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2",
                 "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "23", session.OutputPath], ct);
            if (!r.Success) _log.LogWarning("ffmpeg burst falló: {E}", r.Stderr);
        }
        TryDelete(listPath);
        foreach (var f in frames) TryDelete(f);

        if (!r.Success)
        {
            TryDelete(session.OutputPath);
            if (session.Mic is { } m) TryDelete(m.AudioPath);
            throw IosErrors.RecordingEmptyFrom(session.StartError,
                "no se pudo armar el video con los cuadros capturados.", _isWindows);
        }
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
        var ffmpeg = FindBinary(AgentTools.Ffmpeg) ?? "ffmpeg";
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

    // DP7: comparte MicCapture con Android, pero ante una falla solo se loguea (como antes): el
    // modo with_mic de iOS no se puede probar en esta HU y no debe dejar de grabar el video.
    private async Task StartAudioAsync(IosSession session, CancellationToken ct)
    {
        var audioPath = Path.Combine(session.DataDir, $"ios_audio_{session.SessionId}.mka");
        try
        {
            session.Mic = await MicCapture.StartAsync(audioPath, _micDevice, _log, ct);
            _log.LogInformation("Captura de audio (mic) iniciada");
        }
        catch (Exception ex)
        {
            _log.LogWarning("No se pudo iniciar captura de audio (mic): {Error}", ex.Message);
        }
    }

    private static async Task StopAudioAsync(IosSession session)
    {
        if (session.Mic is not null) await session.Mic.StopAsync();
    }

    // ── On-device recording ───────────────────────────────────────────────────
    // Bug fix: afc ls devuelve rutas completas ("DCIM/100APPLE") no solo el nombre de carpeta.
    // Hay que usar esas rutas directamente en la siguiente llamada a afc ls.

    private Task<ProcessResult> RunPymobiledevice3Async(IReadOnlyList<string> args, CancellationToken ct)
    {
        var python = RequirePython();
        return ProcessRunner.RunArgumentListAsync(python, ["-m", "pymobiledevice3", .. args], ct,
            IosHelper.PythonEnvironment);
    }

    private async Task<List<string>> ListDCIMFilesAsync(string udid, CancellationToken ct)
    {
        // Líneas: "DCIM", "DCIM/100APPLE", "DCIM/101APPLE", "DCIM/.MISC" ...
        var r = await RunPymobiledevice3Async(["afc", "ls", "--udid", udid, "DCIM"], ct);
        if (!r.Success) return [];

        var files = new List<string>();
        foreach (var line in r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var folderPath = line.Trim(); // "DCIM/100APPLE"
            var folderName = Path.GetFileName(folderPath);
            if (folderPath == "DCIM" || folderName.StartsWith('.')) continue;

            // Listar contenido de esa carpeta — afc ls devuelve rutas completas también
            var sub = await RunPymobiledevice3Async(["afc", "ls", "--udid", udid, folderPath], ct);

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
        var r = await RunPymobiledevice3Async(["afc", "pull", "--udid", udid, "-i", remote, outPath], ct);
        if (!r.Success)
        {
            var error = ClassifyAfcError(r.Stderr, _isWindows);
            _log.LogWarning("Error de iPhone {Code}: {Detail}", error.Code, IosHelper.LastLine(r.Stderr) ?? $"exit {r.ExitCode}");
            _log.LogDebug("afc pull falló. stderr:\n{Stderr}", r.Stderr);
            throw error;
        }
        if (!File.Exists(outPath) || new FileInfo(outPath).Length == 0)
            throw IosErrors.Create(IosErrors.CaptureFailed, _isWindows, "el archivo extraído del iPhone quedó vacío");

        _log.LogInformation("iOS on_device: extraído {Remote} → {Local}", remote, outPath);
        return outPath;
    }

    // §6.8: clasificación de un `afc pull` que falló (por el stderr del CLI).
    internal static IosException ClassifyAfcError(string stderr, bool isWindows)
    {
        if (stderr.Contains("ConnectionFailedToUsbmuxd", StringComparison.Ordinal))
            return IosErrors.Create(IosErrors.AppleServiceMissing, isWindows);
        if (stderr.Contains("PasscodeRequired", StringComparison.Ordinal))
            return IosErrors.Create(IosErrors.Locked, isWindows);
        return IosErrors.Create(IosErrors.CaptureFailed, isWindows,
            "no se pudo extraer la grabación del iPhone (" + (IosHelper.LastLine(stderr) ?? "afc pull") + ")");
    }

    // ── Frame interpolation (background) ─────────────────────────────────────
    //
    // Genera variantes interpoladas del video iOS (2 FPS → 30 FPS) en background.
    // Transmite video_variant_ready por WebSocket cuando cada variante está lista.

    private async Task RunInterpolationsAsync(string videoPath)
    {
        var stem   = Path.GetFileNameWithoutExtension(videoPath);
        var dir    = Path.GetDirectoryName(videoPath)!;
        var ffmpeg = FindBinary(AgentTools.Ffmpeg) ?? "ffmpeg";

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
        var rife = FindBinary(AgentTools.Rife);
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

    private static string? FindBinary(ToolSpec s) => ToolResolver.Find(s)?.Path;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
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
