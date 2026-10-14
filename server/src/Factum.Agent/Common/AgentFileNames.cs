using System.Text.RegularExpressions;

namespace Factum.Agent.Common;

/// <summary>
/// Reglas puras de nombres y rutas de la evidencia del caso en Tatana (zip-local-informe-servidor
/// §3.4, §6.2 y §6.3). Las de nombre de archivo copian <c>EvidenceUpload.IsValidUploadName</c> del
/// backend (Factum.Backend/Services/Cases/EvidenceUpload.cs).
/// </summary>
public static class AgentFileNames
{
    public const int MaxNameLength = 255;
    public const int MaxRefLength = 60;

    private static readonly char[] NtfsInvalidChars = ['<', '>', ':', '"', '|', '?', '*'];

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly Regex ZipNameRegex = new(@"^evidencia_[\p{L}\p{N}_]{1,60}\.zip$",
        RegexOptions.CultureInvariant);

    private static readonly Regex ZipPartNameRegex = new(@"^evidencia_[\p{L}\p{N}_]{1,60}\.zip\.part$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Nombre plano de evidencia: no vacío, ≤ 255, sin <c>/</c>, <c>\</c> ni control, sin
    /// <c>&lt; &gt; : " | ? *</c>, sin <c>.</c>/espacio final, sin nombre reservado de Windows y que
    /// no sea un artefacto de Factum (<c>evidencia_*.zip</c>, <c>informe_*</c>).
    /// </summary>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength) return false;
        if (name is "." or "..") return false;
        foreach (var ch in name)
            if (ch is '/' or '\\' || ch < 0x20 || ch == 0x7F) return false;
        if (Path.GetFileName(name) != name) return false;
        if (name.IndexOfAny(NtfsInvalidChars) >= 0) return false;
        if (name.EndsWith('.') || name.EndsWith(' ')) return false;
        var dot = name.IndexOf('.');
        var stem = dot < 0 ? name : name[..dot];
        if (WindowsReservedNames.Contains(stem)) return false;
        if (name.StartsWith("evidencia_", StringComparison.OrdinalIgnoreCase) &&
            name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.StartsWith("informe_", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>
    /// Nombre de un sello de tiempo para <c>&lt;carpeta del caso&gt;/sellos/</c> (sellado-tiempo §7.3): termina en
    /// <c>.tsr</c> (ordinal, minúscula), ≤ 255, sin <c>/</c>, <c>\</c> ni control, sin <c>&lt; &gt; : " | ? *</c>,
    /// nombre plano, sin <c>.</c>/espacio final, sin nombre reservado de Windows y sin <see cref="HasUnsafeControls"/>.
    /// NO aplica la regla de artefactos de <see cref="IsValidName"/>: los <c>.tsr</c> del ZIP y del informe empiezan
    /// con <c>evidencia_</c>/<c>informe_</c>.
    /// </summary>
    public static bool IsSealFilename(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength) return false;
        if (!name.EndsWith(".tsr", StringComparison.Ordinal) || name.Length == ".tsr".Length) return false;
        foreach (var ch in name)
            if (ch is '/' or '\\' || ch < 0x20 || ch == 0x7F) return false;
        if (name.IndexOfAny(NtfsInvalidChars) >= 0) return false;
        if (Path.GetFileName(name) != name) return false;
        if (name.EndsWith('.') || name.EndsWith(' ')) return false;
        var dot = name.IndexOf('.');
        var stem = dot < 0 ? name : name[..dot];
        if (WindowsReservedNames.Contains(stem)) return false;
        return !HasUnsafeControls(name);
    }

    /// <summary><c>^evidencia_[\p{L}\p{N}_]{1,60}\.zip$</c>.</summary>
    public static bool IsZipFilename(string? name) => name is not null && ZipNameRegex.IsMatch(name);

    /// <summary>
    /// <c>^evidencia_[\p{L}\p{N}_]{1,60}\.zip\.part$</c>: ZIP a medio escribir (tatana-ws-y-hosts T-05). Solo
    /// estos se borran al arrancar.
    /// </summary>
    public static bool IsZipPartFilename(string? name) => name is not null && ZipPartNameRegex.IsMatch(name);

    /// <summary>
    /// Controles que permiten disfrazar un nombre (tatana-ws-y-hosts T-13): C1 (U+0080–U+009F), ALM (U+061C),
    /// LRM/RLM (U+200E/U+200F), embeddings/overrides bidi (U+202A–U+202E) e isolates (U+2066–U+2069). ZWJ
    /// (U+200D) y NBSP (U+00A0) NO entran: los usan emoji y nombres legítimos.
    /// </summary>
    public static bool HasUnsafeControls(string? name)
    {
        if (name is null) return false;
        foreach (var ch in name)
        {
            if (ch is >= '\u0080' and <= '\u009F') return true;
            if (ch is '\u061C' or '\u200E' or '\u200F') return true;
            if (ch is >= '\u202A' and <= '\u202E') return true;
            if (ch is >= '\u2066' and <= '\u2069') return true;
        }
        return false;
    }

    /// <summary>
    /// Nombre de un archivo NUEVO (upload-check, upload, import): <see cref="IsValidName"/> y sin
    /// <see cref="HasUnsafeControls"/>. GET, DELETE y el ZIP siguen usando <see cref="IsValidName"/> (D-A4).
    /// </summary>
    public static bool IsValidNewName(string? name) => IsValidName(name) && !HasUnsafeControls(name);

    /// <summary>
    /// Id de caso (Guid "D"). Devuelve la forma canónica en minúscula, que es la que usan las
    /// carpetas: <c>Guid.ToString()</c> del backend.
    /// </summary>
    public static bool TryParseCaseId(string? id, out string canonical)
    {
        canonical = "";
        if (id is null || !Guid.TryParseExact(id, "D", out var guid)) return false;
        canonical = guid.ToString("D");
        return true;
    }

    /// <summary>
    /// Mismo saneado que <c>ReportService.Sanitize</c> del backend: lo que no es letra ni dígito
    /// pasa a <c>_</c>, <c>Trim('_')</c>, corte en 60 y <c>"caso"</c> si queda vacío.
    /// </summary>
    public static string Sanitize(string? s)
    {
        var filtered = new string((s ?? "").Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        filtered = filtered[..Math.Min(filtered.Length, MaxRefLength)];
        return filtered.Length == 0 ? "caso" : filtered;
    }

    /// <summary>
    /// Carpeta final del caso (DP2): <c>&lt;ref saneada&gt;_&lt;primeros 8 del id&gt;</c>, p. ej.
    /// <c>1234_2026_3f2a9c1e</c>. Determinística: nunca se acepta una ruta del cliente.
    /// </summary>
    public static string CaseFolderName(string? caseRef, string caseId) =>
        $"{Sanitize(caseRef)}_{caseId[..Math.Min(8, caseId.Length)]}";

    /// <summary>
    /// DP4: true si la ruta tiene un segmento que empieza con <c>OneDrive</c> (sin distinguir
    /// mayúsculas, cubre "OneDrive - Empresa") o contiene <c>Mobile Documents</c> o <c>iCloud Drive</c>.
    /// </summary>
    public static bool IsSyncedFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.Contains("Mobile Documents", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("iCloud Drive", StringComparison.OrdinalIgnoreCase)) return true;
        return path.Split('/', '\\').Any(seg => seg.StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Defensa en profundidad: <paramref name="dir"/>/<paramref name="name"/> resuelto tiene que
    /// tener como directorio exactamente <paramref name="dir"/>. Devuelve la ruta o null.
    /// </summary>
    public static string? SafeChild(string dir, string name)
    {
        var full = Path.GetFullPath(Path.Combine(dir, name));
        var parent = Path.GetDirectoryName(full);
        return string.Equals(parent, Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal)
            ? full : null;
    }
}
