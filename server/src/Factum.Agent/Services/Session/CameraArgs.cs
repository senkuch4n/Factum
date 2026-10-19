using System.Globalization;
using System.Text.RegularExpressions;

namespace Factum.Agent.Services.Session;

/// <summary>Modo de entrada de la cámara (lo que se le pide a avfoundation/dshow/v4l2).</summary>
public sealed record CameraMode(int Width, int Height, double Fps);

/// <summary>Modo que informa avfoundation en "Supported modes" (rango de fps).</summary>
public sealed record SupportedMode(int Width, int Height, double MinFps, double MaxFps);

/// <summary>Resolución y fps reales de lo que se graba (línea <c>Stream #0:0: Video: h264</c> de <c>Output #0</c>).</summary>
public sealed record CameraStream(int Width, int Height, int Fps);

/// <summary>
/// Clasificación de una falla al arrancar (§5.5): <c>RetryMode</c> = vale la pena reintentar una vez con otro modo;
/// <c>Detail</c> = el <c>detail</c> del aviso <c>camera_failed</c> (null = sin detalle).
/// </summary>
public sealed record CameraFailure(bool RetryMode, string? Detail);

/// <summary>Lo que hace falta para armar el comando de ffmpeg de la cámara (§5.3).</summary>
public sealed record CameraArgsInput(
    string Backend,
    string Video,
    bool WithAudio,
    string? Mic,
    CameraMode? Mode,
    int Width,
    int Height,
    int Fps,
    int Crf,
    int SegmentSeconds,
    int StartNumber,
    string StagingDir)
{
    /// <summary>
    /// Solo v4l2 (Linux): el audio por ALSA <c>default</c> en vez de PulseAudio (tatana-linux D-T14; fallback cuando el
    /// ffmpeg no trae libpulse).
    /// </summary>
    public bool LinuxAlsaAudio { get; init; }
}

/// <summary>
/// Comando de ffmpeg de la cámara del procedimiento (grabacion-sesion-completa-f2 §5.3–§5.5). Puro: arma la
/// <c>ArgumentList</c> (nunca un string), parsea el stderr de ffmpeg y clasifica las fallas. Backends: <c>avfoundation</c>
/// (macOS), <c>dshow</c> (Windows), <c>v4l2</c> (Linux) y <c>lavfi</c> (mock y tests: nunca abre una cámara).
/// </summary>
public static partial class CameraArgs
{
    public const string BackendMac = "avfoundation";
    public const string BackendWindows = "dshow";
    public const string BackendLinux = "v4l2";
    public const string BackendLavfi = "lavfi";
    public const string BackendMock = "mock";

    public const string CsvFileName = "camara.csv";
    public const string PreviewFileName = "camara-preview.jpg";
    public const string PartPattern = "camara_%04d.part.mkv";

    /// <summary>fps de entrada en macOS en el primer intento (avfoundation pide 29,97 por defecto y muchas cámaras no lo aceptan).</summary>
    public const int MacInputFps = 30;

    public const string LavfiVideo = "testsrc2=size=1280x720:rate=30";
    public const string LavfiAudio = "sine=frequency=440:sample_rate=48000";

    public static string PartName(int index) => string.Create(CultureInfo.InvariantCulture, $"camara_{index:0000}.part.mkv");

    private static string Inv(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
    private static string Inv(int v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Modo del primer intento: W×H y fps configurados (en macOS, fps de entrada 30).</summary>
    public static CameraMode DefaultMode(string backend, int width, int height, int fps) =>
        new(width, height, backend == BackendMac ? MacInputFps : fps);

    /// <summary>Argumentos de entrada (<c>-f … -i …</c>) y el <c>-map</c> del audio (o null sin audio).</summary>
    internal static (List<string> Args, string? AudioMap) Input(string backend, string video, bool withAudio, string? mic,
        CameraMode? mode, bool linuxAlsaAudio = false)
    {
        var a = new List<string>();
        string? audioMap = null;
        switch (backend)
        {
            case BackendMac:
                a.AddRange(["-f", "avfoundation"]);
                a.AddRange(["-framerate", Inv(mode?.Fps ?? MacInputFps)]);
                if (mode is not null) a.AddRange(["-video_size", $"{Inv(mode.Width)}x{Inv(mode.Height)}"]);
                // D-T8: el micrófono por defecto del sistema, nunca ":0" (en esta Mac es el del iPhone por Continuidad).
                a.AddRange(["-i", withAudio ? video + ":default" : video]);
                if (withAudio) audioMap = "0:a";
                break;
            case BackendWindows:
                a.AddRange(["-f", "dshow", "-rtbufsize", "256M"]);
                if (mode is not null)
                    a.AddRange(["-video_size", $"{Inv(mode.Width)}x{Inv(mode.Height)}", "-framerate", Inv(mode.Fps)]);
                a.AddRange(["-i", "video=" + video + (withAudio && !string.IsNullOrEmpty(mic) ? ":audio=" + mic : "")]);
                if (withAudio && !string.IsNullOrEmpty(mic)) audioMap = "0:a";
                break;
            case BackendLinux:
                a.AddRange(["-f", "v4l2"]);
                if (mode is not null)
                    a.AddRange(["-video_size", $"{Inv(mode.Width)}x{Inv(mode.Height)}", "-framerate", Inv(mode.Fps)]);
                a.AddRange(["-i", video]);
                if (withAudio)
                {
                    // tatana-linux D-T14: PulseAudio (o PipeWire) con Agent:MicDevice o "default"; ALSA como fallback.
                    a.AddRange(MicCapture.LinuxInputArgs(mic, linuxAlsaAudio));
                    audioMap = "1:a";
                }
                break;
            default: // lavfi (mock y tests)
                // Con audio, video y audio salen del MISMO grafo lavfi: con dos entradas "-re" ffmpeg se atrasa respecto
                // del reloj (medido: 8,2 s de video en 12 s) y las horas ancla + CSV dejarían de coincidir.
                a.AddRange(["-re", "-f", "lavfi", "-i", withAudio ? $"{LavfiVideo}[out0];{LavfiAudio}[out1]" : LavfiVideo]);
                if (withAudio) audioMap = "0:a";
                break;
        }
        return (a, audioMap);
    }

    /// <summary>
    /// Comando de grabación (§5.3): una salida segment (MKV por tramo, CSV <c>+live</c>) y una de vista previa (JPG cada
    /// 2 s, escritura atómica). <c>-tune zerolatency</c> (D-T1): el corte y el ancla horaria quedan exactos.
    /// </summary>
    public static List<string> Build(CameraArgsInput i)
    {
        var args = new List<string> { "-hide_banner", "-nostats", "-loglevel", "info", "-y" };
        var (input, audioMap) = Input(i.Backend, i.Video, i.WithAudio, i.Mic, i.Mode, i.LinuxAlsaAudio);
        args.AddRange(input);
        var seg = Math.Max(1, i.SegmentSeconds);
        args.AddRange(["-filter_complex",
            $"[0:v]fps={Inv(i.Fps)},scale=w='min(iw,{Inv(i.Width)})':h=-2,split=2[r][p];[p]fps=1/2,scale=320:-2[pv]"]);
        args.AddRange(["-map", "[r]"]);
        if (audioMap is not null) args.AddRange(["-map", audioMap]);
        args.AddRange(["-c:v", "libx264", "-preset", "veryfast", "-tune", "zerolatency", "-crf", Inv(i.Crf),
            "-pix_fmt", "yuv420p", "-g", Inv(2 * Math.Max(1, i.Fps)),
            "-force_key_frames", $"expr:gte(t,n_forced*{Inv(seg)})"]);
        if (audioMap is not null) args.AddRange(["-c:a", "aac", "-b:a", "96k"]);
        args.AddRange(["-f", "segment", "-segment_time", Inv(seg), "-segment_format", "matroska",
            "-segment_format_options", "flush_packets=1:cluster_time_limit=2000",
            "-segment_list", Path.Combine(i.StagingDir, CsvFileName), "-segment_list_type", "csv",
            "-segment_list_flags", "+live", "-reset_timestamps", "1",
            "-segment_start_number", Inv(i.StartNumber), Path.Combine(i.StagingDir, PartPattern)]);
        args.AddRange(["-map", "[pv]", "-q:v", "6", "-update", "1", "-atomic_writing", "1", "-f", "image2",
            Path.Combine(i.StagingDir, PreviewFileName)]);
        return args;
    }

    /// <summary>Vista previa antes de grabar (§5.9): la misma entrada y solo la salida JPG.</summary>
    public static List<string> BuildPreview(string backend, string video, CameraMode? mode, string jpgPath)
    {
        var args = new List<string> { "-hide_banner", "-nostats", "-loglevel", "info", "-y" };
        var (input, _) = Input(backend, video, withAudio: false, mic: null, mode);
        args.AddRange(input);
        args.AddRange(["-vf", "fps=1/2,scale=320:-2", "-an", "-q:v", "6", "-update", "1", "-atomic_writing", "1",
            "-f", "image2", jpgPath]);
        return args;
    }

    // ── Parseo del stderr ────────────────────────────────────────────────────

    // "  1280x720@[1.000000 30.000000]fps"
    [GeneratedRegex(@"(\d{2,5})x(\d{2,5})@\[(\d+(?:\.\d+)?)\s+(\d+(?:\.\d+)?)\]fps", RegexOptions.CultureInvariant)]
    private static partial Regex SupportedModeRegex();

    [GeneratedRegex(@"\b(\d{2,5})x(\d{2,5})\b", RegexOptions.CultureInvariant)]
    private static partial Regex SizeRegex();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s+fps\b", RegexOptions.CultureInvariant)]
    private static partial Regex FpsRegex();

    /// <summary>Modos de "Supported modes:" de avfoundation (en el orden en que los lista).</summary>
    public static List<SupportedMode> ParseSupportedModes(IEnumerable<string> stderr)
    {
        var list = new List<SupportedMode>();
        foreach (var line in stderr)
        {
            var m = SupportedModeRegex().Match(line);
            if (!m.Success) continue;
            var mode = new SupportedMode(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture));
            if (!list.Contains(mode)) list.Add(mode);
        }
        return list;
    }

    /// <summary>
    /// Reintento de modo (§5.5): el de mayor área ≤ W×H (si no hay, el más chico); fps de entrada = 30 si cae en el
    /// rango, si no el máximo del rango. null sin modos.
    /// </summary>
    public static CameraMode? PickMode(IReadOnlyList<SupportedMode> modes, int width, int height)
    {
        if (modes.Count == 0) return null;
        var fit = modes.Where(m => m.Width <= width && m.Height <= height)
            .OrderByDescending(m => (long)m.Width * m.Height).ThenByDescending(m => m.MaxFps).FirstOrDefault();
        var pick = fit ?? modes.OrderBy(m => (long)m.Width * m.Height).ThenByDescending(m => m.MaxFps).First();
        var fps = pick.MinFps <= MacInputFps && MacInputFps <= pick.MaxFps ? MacInputFps : pick.MaxFps;
        return new CameraMode(pick.Width, pick.Height, fps);
    }

    /// <summary>¿Es la línea <c>Output #0, segment, to '…'</c>? Su hora es el ancla del tramo continuo (§5.4).</summary>
    public static bool IsSegmentOutputLine(string line) =>
        line.Contains("Output #0, segment", StringComparison.Ordinal);

    /// <summary>¿Es la línea <c>Output #0, image2, …</c> (vista previa sola)?</summary>
    public static bool IsPreviewOutputLine(string line) =>
        line.Contains("Output #0, image2", StringComparison.Ordinal);

    /// <summary>
    /// W×H y fps de la línea <c>Stream #0:0: Video: h264 …, 1280x720 …, 15 fps, …</c> (la del <c>Output #0</c>). null si
    /// la línea no es esa.
    /// </summary>
    public static CameraStream? ParseOutputStream(string line)
    {
        if (!line.Contains("Stream #0:0", StringComparison.Ordinal) || !line.Contains("Video: h264", StringComparison.Ordinal))
            return null;
        var size = SizeRegex().Match(line[line.IndexOf("Video:", StringComparison.Ordinal)..]);
        var fps = FpsRegex().Match(line);
        if (!size.Success || !fps.Success) return null;
        return new CameraStream(int.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture),
            (int)Math.Round(double.Parse(fps.Groups[1].Value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// Clasifica una falla al arrancar (§5.5). <paramref name="exitCode"/> = código de salida (null si no salió);
    /// <paramref name="timedOut"/> = no llegó <c>Output #0</c> a tiempo.
    /// </summary>
    public static CameraFailure Classify(string backend, IReadOnlyList<string> stderr, int? exitCode, bool timedOut)
    {
        var text = string.Join("\n", stderr);
        bool Has(string s) => text.Contains(s, StringComparison.OrdinalIgnoreCase);
        var mac = backend == BackendMac;
        var win = backend == BackendWindows;

        if (timedOut) return new CameraFailure(false, mac ? CameraFailDetails.Permission : null);
        if (mac && Has("is not supported by the device") && Has("Supported modes"))
            return new CameraFailure(true, CameraFailDetails.Unsupported);
        if (win && (Has("Could not set video options") || Has("not supported")))
            return new CameraFailure(true, CameraFailDetails.Unsupported);
        if ((mac && (Has("Video device not found") || Has("No AV capture device found"))) ||
            (win && Has("Could not find video device")))
            return new CameraFailure(false, CameraFailDetails.NotFound);
        if (mac && (Has("Failed to create AV capture input device") || exitCode is < 0 or 134 or 137))
            return new CameraFailure(false, CameraFailDetails.Permission);
        if (win && (Has("Could not run graph") || Has("I/O error") || Has("Error opening input")))
            return new CameraFailure(false, CameraFailDetails.Blocked);
        return new CameraFailure(false, null);
    }

    /// <summary>Texto para la web de una falla de la cámara (vista previa, §5.9).</summary>
    public static string FailureText(string? detail) => detail switch
    {
        CameraFailDetails.Permission => "Tatana no tiene permiso para usar la cámara.",
        CameraFailDetails.Blocked => "Windows no deja usar la cámara, o la está usando otra aplicación.",
        CameraFailDetails.NotFound => "No se encuentra la cámara: revisá que esté conectada.",
        CameraFailDetails.Unsupported => "La cámara no aceptó ningún modo de video.",
        _ => "No se pudo abrir la cámara.",
    };
}

/// <summary>Una línea del CSV del segment muxer: <c>camara_0003.part.mkv,0.000000,10.000000</c>.</summary>
public sealed record CameraCsvLine(int Index, string PartName, double StartSeconds, double EndSeconds);

/// <summary>CSV de la cámara (§5.4, §5.8). Puro.</summary>
public static partial class CameraCsv
{
    [GeneratedRegex(@"^camara_(\d{1,6})\.part\.mkv$", RegexOptions.CultureInvariant)]
    private static partial Regex PartRegex();

    /// <summary>Índice del nombre <c>camara_NNNN.part.mkv</c>, o null.</summary>
    public static int? IndexOf(string partName)
    {
        var m = PartRegex().Match(partName);
        return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var i)
            ? i : null;
    }

    /// <summary>
    /// Líneas completas (terminadas en salto de línea) del CSV, en orden. Una línea a medio escribir al final se ignora
    /// (la escribe ffmpeg mientras se lee); una que no se entiende, también.
    /// </summary>
    public static List<CameraCsvLine> Parse(string content)
    {
        var list = new List<CameraCsvLine>();
        var lastNewline = content.LastIndexOf('\n');
        if (lastNewline < 0) return list;
        foreach (var raw in content[..lastNewline].Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(',');
            if (parts.Length < 3) continue;
            var name = parts[0].Trim().Trim('"');
            if (IndexOf(name) is not { } index) continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var start) ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var end))
                continue;
            list.Add(new CameraCsvLine(index, name, start, Math.Max(start, end)));
        }
        return list;
    }

    /// <summary>Lee el CSV compartiendo el archivo con ffmpeg. Vacío si no existe o no se pudo leer.</summary>
    public static List<CameraCsvLine> Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            return Parse(reader.ReadToEnd());
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }
}
