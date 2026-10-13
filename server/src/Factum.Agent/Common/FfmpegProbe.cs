using System.Globalization;
using System.Text.RegularExpressions;

namespace Factum.Agent.Common;

/// <summary>Lo que se pudo leer de un archivo con <c>ffmpeg -i</c> (grabacion-android-robusta §5.3).</summary>
internal sealed record FfmpegProbeResult(double? DurationSeconds, bool HasVideo);

/// <summary>
/// Duración y presencia de un stream de video con <c>ffmpeg -hide_banner -i &lt;archivo&gt;</c> (no hay ffprobe
/// empaquetado). El código de salida ≠ 0 es lo esperable: no hay salida, solo interesa el stderr.
/// </summary>
internal static partial class FfmpegProbe
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public static async Task<FfmpegProbeResult> ProbeAsync(string ffmpeg, string path, CancellationToken ct)
    {
        var r = await ProcessRunner.RunArgumentListAsync(ffmpeg, ["-hide_banner", "-i", path], ct,
            timeout: DefaultTimeout);
        return Parse(r.Stderr);
    }

    /// <summary>Puro (testeable).</summary>
    internal static FfmpegProbeResult Parse(string? stderr) =>
        new(ParseDuration(stderr), !string.IsNullOrEmpty(stderr) && VideoStreamRegex().IsMatch(stderr));

    /// <summary><c>Duration: HH:MM:SS.ss</c> del stderr de <c>ffmpeg -i</c>, en segundos; null si no está o es 0.</summary>
    internal static double? ParseDuration(string? stderr)
    {
        if (string.IsNullOrEmpty(stderr)) return null;
        var m = DurationRegex().Match(stderr);
        if (!m.Success) return null;
        var h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var min = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var s = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        var total = h * 3600 + min * 60 + s;
        return total > 0 ? total : null;
    }

    [GeneratedRegex(@"Duration:\s*(\d+):(\d{2}):(\d{2}(?:\.\d+)?)")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"Stream #\d+:\d+.*: Video:")]
    private static partial Regex VideoStreamRegex();
}
