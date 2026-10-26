using System.Security.Cryptography;
using System.Text;

namespace Factum.Agent.Services.Extraction;

/// <summary>
/// Lógica PURA de rutas del dispositivo (extraccion-logica-android §5.4.2 y §5.4.4): normaliza y valida raíces
/// de <c>/sdcard</c>, deduplica descendientes resolviendo la categoría "dueña", y mapea una ruta del dispositivo
/// a la ruta local saneada para el sistema de archivos destino (Windows/exFAT). Sin E/S salvo el chequeo de
/// colisión que se inyecta como predicado (para testear sin tocar el disco).
/// </summary>
public static class DevicePaths
{
    private static readonly string[] PrimaryAliases =
        ["/storage/emulated/0", "/storage/self/primary"];

    // Caracteres inválidos en Windows/exFAT (sin '/', que separa segmentos).
    private static readonly char[] InvalidChars = ['<', '>', ':', '"', '|', '?', '*'];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Normaliza una ruta del dispositivo a su forma canónica bajo <c>/sdcard</c>, o devuelve null si es inválida
    /// (no cuelga de <c>/sdcard</c>, tiene <c>..</c> o tiene un byte de control). Colapsa <c>//</c> y saca la barra
    /// final.
    /// </summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.Any(c => c < 0x20)) return null;

        var p = path.Replace('\\', '/').Trim();
        foreach (var alias in PrimaryAliases)
        {
            if (p == alias) { p = "/sdcard"; break; }
            if (p.StartsWith(alias + "/", StringComparison.Ordinal)) { p = "/sdcard" + p[alias.Length..]; break; }
        }

        // Colapsar barras repetidas.
        while (p.Contains("//")) p = p.Replace("//", "/");
        if (p.Length > 1) p = p.TrimEnd('/');

        if (p != "/sdcard" && !p.StartsWith("/sdcard/", StringComparison.Ordinal)) return null;

        var segments = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s == ".." || s == ".")) return null;

        return p;
    }

    /// <summary>
    /// Deduplica raíces ya normalizadas: descarta toda raíz descendiente de otra elegida y, en un empate, la
    /// categoría más temprana en <paramref name="categoryOrder"/> gana la raíz. Entrada: pares (raíz, categoría).
    /// Salida: raíces únicas con su categoría dueña, ordenadas por ruta.
    /// </summary>
    public static List<(string Path, string Category)> Dedupe(
        IEnumerable<(string Path, string Category)> roots, IReadOnlyList<string> categoryOrder)
    {
        int Rank(string cat)
        {
            for (int i = 0; i < categoryOrder.Count; i++)
                if (categoryOrder[i] == cat) return i;
            return int.MaxValue;
        }

        // Una sola categoría dueña por ruta: la de menor rango.
        var byPath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, cat) in roots)
        {
            if (!byPath.TryGetValue(path, out var cur) || Rank(cat) < Rank(cur))
                byPath[path] = cat;
        }

        var all = byPath.Keys.ToList();
        var result = new List<(string, string)>();
        foreach (var path in all)
        {
            // ¿Es descendiente de otra raíz elegida (distinta)?
            bool descendant = all.Any(other =>
                !ReferenceEquals(other, path) && other != path && IsDescendant(path, other));
            if (!descendant)
                result.Add((path, byPath[path]));
        }
        return result.OrderBy(r => r.Item1, StringComparer.Ordinal).ToList();
    }

    /// <summary><paramref name="child"/> cuelga de <paramref name="ancestor"/> (o es igual no cuenta).</summary>
    public static bool IsDescendant(string child, string ancestor) =>
        child.StartsWith(ancestor + "/", StringComparison.Ordinal);

    /// <summary>
    /// Mapea una ruta del dispositivo (bajo <c>/sdcard</c>) a la ruta local relativa saneada bajo <c>archivos/</c>,
    /// con <c>/</c> como separador. <paramref name="collides"/>, si se pasa, decide si un candidato choca por
    /// mayúsculas/minúsculas en el destino (se le suma <c>~2</c>). Devuelve también si hubo algún ajuste.
    /// </summary>
    public static LocalMapping ToLocal(string normalizedDevicePath, Func<string, bool>? collides = null)
    {
        // /sdcard/DCIM/a.jpg -> archivos/sdcard/DCIM/a.jpg
        var rest = normalizedDevicePath.TrimStart('/'); // sdcard/DCIM/a.jpg
        var segments = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var adjusted = false;
        var outSegments = new List<string>(segments.Length + 1) { "archivos" };

        for (int i = 0; i < segments.Length; i++)
        {
            var original = segments[i];
            var sane = SanitizeSegment(original, out var changed);
            adjusted |= changed;

            // Colisión por mayúsculas/minúsculas en el destino case-insensitive.
            if (collides is not null)
            {
                var candidate = string.Join('/', outSegments.Append(sane));
                if (collides(candidate))
                {
                    sane = AppendSuffix(sane, "~2");
                    adjusted = true;
                }
            }
            outSegments.Add(sane);
        }

        return new LocalMapping(string.Join('/', outSegments), adjusted);
    }

    private static string SanitizeSegment(string segment, out bool changed)
    {
        var original = segment;
        var sb = new StringBuilder(segment.Length);
        foreach (var c in segment)
            sb.Append(c < 0x20 || Array.IndexOf(InvalidChars, c) >= 0 ? '_' : c);
        var s = sb.ToString();

        // Nombre reservado de Windows (comparando sin extensión).
        var stem = s.Contains('.') ? s[..s.IndexOf('.')] : s;
        if (ReservedNames.Contains(stem)) s = "_" + s;

        // Puntos y espacios finales.
        if (s.Length > 0 && (s[^1] == '.' || s[^1] == ' ')) s += "_";

        // Segmento de más de 255 bytes UTF-8: cortar y sumar ~<hash8>.
        if (Encoding.UTF8.GetByteCount(s) > 255)
        {
            var hash8 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(original)))[..8];
            s = TruncateUtf8(s, 255 - 9) + "~" + hash8; // 9 = "~" + 8 hex
        }

        changed = s != original;
        return s;
    }

    private static string AppendSuffix(string segment, string suffix)
    {
        var dot = segment.LastIndexOf('.');
        return dot > 0 ? segment[..dot] + suffix + segment[dot..] : segment + suffix;
    }

    private static string TruncateUtf8(string s, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(s) <= maxBytes) return s;
        var chars = s.ToCharArray();
        var len = chars.Length;
        while (len > 0 && Encoding.UTF8.GetByteCount(chars, 0, len) > maxBytes) len--;
        return new string(chars, 0, len);
    }
}

/// <summary>Ruta local relativa saneada y si hubo algún ajuste (columna <c>nombre_ajustado</c> del manifiesto).</summary>
public sealed record LocalMapping(string RelativePath, bool Adjusted);
