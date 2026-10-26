using System.Globalization;
using System.Text.RegularExpressions;

namespace Factum.Agent.Services.Extraction;

/// <summary>
/// Parsers PUROS de la salida de los comandos adb del preflight/estimate/inventario de la extracción
/// (extraccion-logica-android §5.4/§5.11). Separados del I/O para poder testearlos sin dispositivo (los comandos en
/// sí se validan en la QA con el Samsung).
/// </summary>
public static class ExtractionDeviceQueries
{
    private static readonly Regex Sha256Line = new(@"^([0-9a-f]{64})\s+(.+)$", RegexOptions.Compiled);

    /// <summary><c>dumpsys battery</c> → (level, charging). charging = AC o USB; null si no hay level.</summary>
    public static (int? Level, bool? Charging) ParseBattery(string dumpsys)
    {
        if (string.IsNullOrWhiteSpace(dumpsys)) return (null, null);
        int? level = null; bool ac = false, usb = false;
        foreach (var raw in dumpsys.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("level:", StringComparison.Ordinal) && int.TryParse(line[6..].Trim(), out var l)) level = l;
            else if (line.StartsWith("AC powered:", StringComparison.Ordinal)) ac = line.EndsWith("true", StringComparison.Ordinal);
            else if (line.StartsWith("USB powered:", StringComparison.Ordinal)) usb = line.EndsWith("true", StringComparison.Ordinal);
        }
        return (level, level is null ? null : ac || usb);
    }

    /// <summary><c>du -s -k</c> → ruta → bytes (KiB × 1024). Las raíces inexistentes no aparecen (van a stderr).</summary>
    public static Dictionary<string, long> ParseDuKib(string stdout)
    {
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in (stdout ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length == 2 && long.TryParse(parts[0].Trim(), out var kb)) sizes[parts[1].Trim()] = kb * 1024;
        }
        return sizes;
    }

    /// <summary>Salida del loop <c>[ -e "$d" ] &amp;&amp; printf</c> → el subconjunto de <paramref name="roots"/> que existe.</summary>
    public static HashSet<string> ParseExistingRoots(string stdout, IReadOnlyCollection<string> roots)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in (stdout ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Trim();
            if (roots.Contains(p)) present.Add(p);
        }
        return present;
    }

    /// <summary><c>stat -c '%s|%Y|%n'</c> → (size, ISO mtime UTC, ruta). null si no parsea (nombre con <c>\n</c>, etc.).</summary>
    public static (long Size, string MtimeIso, string Path)? ParseStatLine(string line)
    {
        var parts = line.Split('|', 3);
        if (parts.Length != 3 || !long.TryParse(parts[0], out var size) || !long.TryParse(parts[1], out var mtime))
            return null;
        var iso = DateTimeOffset.FromUnixTimeSeconds(mtime).UtcDateTime
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        return (size, iso, parts[2]);
    }

    /// <summary><c>sha256sum</c> → ruta_dispositivo → hash. Formato <c>&lt;64hex&gt;  &lt;ruta&gt;</c>.</summary>
    public static Dictionary<string, string> ParseDeviceHashes(string stdout)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in (stdout ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var m = Sha256Line.Match(line);
            if (m.Success) map[m.Groups[2].Value.Trim()] = m.Groups[1].Value;
        }
        return map;
    }

    /// <summary>Columna <c>coincide</c> de un archivo (D10): si/no (y marca mismatch)/no_calculado.</summary>
    public static string Coincide(IReadOnlyDictionary<string, string>? deviceHashes, string devicePath, string pcSha,
        out bool mismatch)
    {
        mismatch = false;
        if (deviceHashes is null || !deviceHashes.TryGetValue(devicePath, out var dh)) return ManifestMatch.NotComputed;
        if (string.Equals(dh, pcSha, StringComparison.OrdinalIgnoreCase)) return ManifestMatch.Yes;
        mismatch = true; // cambió entre la copia y el hash; NO es error
        return ManifestMatch.No;
    }
}
