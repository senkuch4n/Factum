using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services.Session;

/// <summary>
/// Cámara de la PC (§7.2.1). <c>backend</c> ∈ <c>avfoundation</c> | <c>dshow</c> | <c>v4l2</c> | <c>mock</c>. En Windows
/// <c>id</c> = el <i>alternative name</i> (estable); en macOS, el nombre localizado. <see cref="Index"/> es interno (el
/// índice del listado de avfoundation, para los nombres con <c>:</c>).
/// </summary>
public sealed record CameraDevice(string Id, string Name, string Backend)
{
    [JsonIgnore] public int? Index { get; init; }
}

/// <summary><c>GET /cameras</c>: la lista, o vacía con <c>error</c> (sin ffmpeg).</summary>
public sealed record CameraListResponse(IReadOnlyList<CameraDevice> Cameras, string? Error = null);

/// <summary>
/// Cámaras de la PC (grabacion-sesion-completa-f2 §5.2). Listar no abre la cámara. Caché de 10 s; <c>Resolve</c> siempre
/// con un listado fresco. En mock o con <c>CameraLavfi</c> devuelve solo la cámara simulada: nunca se lista una cámara
/// real en las pruebas.
/// </summary>
public sealed partial class CameraDevices
{
    internal const string ErrorNoFfmpeg =
        "No se encontró ffmpeg, necesario para grabar con la cámara de la PC. Actualizá o reinstalá Tatana con el paquete de Factum.";
    public const string MockId = "mock-cam";
    public const string MockName = "Cámara simulada";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(5);

    private readonly bool _lavfi;
    private readonly ILogger _log;
    private readonly TimeProvider _time;
    private readonly Func<Task<CameraListResponse>>? _lister;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private CameraListResponse? _cached;
    private DateTime _cachedAt;

    public CameraDevices(IOptions<AgentOptions> opts, ILogger<CameraDevices> log, TimeProvider time)
        : this(opts.Value.Mock || (opts.Value.Session?.CameraLavfi ?? false), log, time, lister: null)
    {
    }

    internal CameraDevices(bool lavfi, ILogger? log, TimeProvider time, Func<Task<CameraListResponse>>? lister)
    {
        _lavfi = lavfi;
        _log = log ?? NullLogger.Instance;
        _time = time;
        _lister = lister;
    }

    /// <summary>true = mock o <c>CameraLavfi</c>: la cámara es lavfi.</summary>
    public bool Lavfi => _lavfi;

    public static CameraDevice Mock => new(MockId, MockName, CameraArgs.BackendMock);

    public async Task<CameraListResponse> ListAsync(bool refresh = false)
    {
        await _lock.WaitAsync();
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            if (!refresh && _cached is not null && now - _cachedAt < CacheTtl) return _cached;
            var fresh = await ListFreshAsync();
            _cached = fresh;
            _cachedAt = now;
            return fresh;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Dispositivo actual por <c>id</c>; si no, por <c>name</c> exacto (el primero). Con listado fresco.</summary>
    public async Task<CameraDevice?> ResolveAsync(string? id, string name)
    {
        var list = (await ListAsync(refresh: true)).Cameras;
        return Resolve(list, id, name);
    }

    internal static CameraDevice? Resolve(IReadOnlyList<CameraDevice> list, string? id, string name) =>
        (!string.IsNullOrEmpty(id) ? list.FirstOrDefault(c => c.Id == id) : null)
        ?? list.FirstOrDefault(c => c.Name == name);

    private async Task<CameraListResponse> ListFreshAsync()
    {
        if (_lister is not null) return await _lister();
        if (_lavfi) return new CameraListResponse([Mock]);
        if (OperatingSystem.IsLinux()) return new CameraListResponse(ListLinux());
        var ffmpeg = ToolResolver.Find(AgentTools.Ffmpeg)?.Path;
        if (ffmpeg is null) return new CameraListResponse([], ErrorNoFfmpeg);
        if (OperatingSystem.IsWindows())
        {
            var stderr = await MicCapture.ListDshowDevicesAsync(ffmpeg, _log, CancellationToken.None);
            return new CameraListResponse(DshowDevices.ParseVideoDevices(stderr)
                .Select(d => new CameraDevice(string.IsNullOrWhiteSpace(d.AlternativeName) ? d.Name : d.AlternativeName!,
                    d.Name, CameraArgs.BackendWindows))
                .ToList());
        }
        var mac = await RunListAsync(ffmpeg, ["-hide_banner", "-f", "avfoundation", "-list_devices", "true", "-i", ""]);
        return new CameraListResponse(ParseAvfoundation(mac)
            .Select(d => new CameraDevice(d.Name, d.Name, CameraArgs.BackendMac) { Index = d.Index }).ToList());
    }

    // "[AVFoundation indev @ 0x…] [0] Cámara de la MacBook Air"
    [GeneratedRegex(@"^\[[^\]]*\]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixRegex();

    [GeneratedRegex(@"^\[(\d+)\] (.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DeviceRegex();

    /// <summary>
    /// Cámaras del listado de avfoundation (§5.2): las líneas entre <c>AVFoundation video devices:</c> y
    /// <c>AVFoundation audio devices:</c>, sin las <c>Capture screen</c> (graban el escritorio). Puro.
    /// </summary>
    public static List<(int Index, string Name)> ParseAvfoundation(string stderr)
    {
        var list = new List<(int, string)>();
        var inVideo = false;
        foreach (var raw in stderr.Split('\n'))
        {
            var line = PrefixRegex().Replace(raw.TrimEnd('\r'), "").Trim();
            if (line.Contains("AVFoundation video devices", StringComparison.Ordinal)) { inVideo = true; continue; }
            if (line.Contains("AVFoundation audio devices", StringComparison.Ordinal)) { inVideo = false; continue; }
            if (!inVideo) continue;
            var m = DeviceRegex().Match(line);
            if (!m.Success) continue;
            var name = m.Groups[2].Value.Trim();
            if (name.StartsWith("Capture screen", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add((int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), name));
        }
        return list;
    }

    private static List<CameraDevice> ListLinux()
    {
        try
        {
            return Directory.EnumerateFiles("/dev", "video*").Order(StringComparer.Ordinal).Select(path =>
            {
                var sys = Path.Combine("/sys/class/video4linux", Path.GetFileName(path), "name");
                string name;
                try { name = File.Exists(sys) ? File.ReadAllText(sys).Trim() : path; }
                catch { name = path; }
                return new CameraDevice(path, string.IsNullOrEmpty(name) ? path : name, CameraArgs.BackendLinux);
            }).ToList();
        }
        catch
        {
            return [];
        }
    }

    // ffmpeg -list_devices sale con código ≠ 0: es normal. Devuelve el stderr.
    private async Task<string> RunListAsync(string ffmpeg, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        var sb = new StringBuilder();
        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return "";
            proc.OutputDataReceived += (_, _) => { };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            using var timeout = new CancellationTokenSource(ListTimeout);
            try
            {
                await proc.WaitForExitAsync(timeout.Token);
                proc.WaitForExit();
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ya terminó */ }
                _log.LogWarning("ffmpeg -list_devices (cámaras) no respondió en {Sec} s", ListTimeout.TotalSeconds);
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _log.LogWarning(ex, "No se pudo listar las cámaras con ffmpeg");
        }
        lock (sb) return sb.ToString();
    }
}
