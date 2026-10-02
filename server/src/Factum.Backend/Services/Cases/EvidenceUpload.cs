using System.Globalization;
using Factum.Backend.Services.Reports;

namespace Factum.Backend.Services.Cases;

/// <summary>Códigos estables de error de las rutas de subida (campo <c>code</c> del JSON, SDD §7).</summary>
public static class UploadErrorCodes
{
    public const string InvalidFilename = "invalid_filename";
    public const string CaseNotEditable = "case_not_editable";
    public const string LengthRequired = "length_required";
    public const string FileTooLarge = "file_too_large";
    public const string InsufficientStorage = "insufficient_storage";
    public const string IncompleteUpload = "incomplete_upload";
    public const string StorageError = "storage_error";
}

public enum UploadRejectionKind { TooLarge, InsufficientStorage }

/// <summary>
/// Rechazo previo a escribir (§5.2). En <see cref="UploadRejectionKind.TooLarge"/> vienen
/// <see cref="Size"/> y <see cref="MaxUploadBytes"/>; en <see cref="UploadRejectionKind.InsufficientStorage"/>,
/// <see cref="Size"/>, <see cref="RequiredBytes"/> y <see cref="AvailableBytes"/>.
/// </summary>
public sealed record UploadRejection(
    UploadRejectionKind Kind,
    long Size,
    long MaxUploadBytes,
    long RequiredBytes = 0,
    long? AvailableBytes = null);

/// <summary>
/// Lógica pura de la subida de evidencia (subida-archivos-grandes §5.2): validación del nombre,
/// prechequeo de tope/espacio, formato de tamaños y mensajes. Sin I/O.
/// </summary>
public static class EvidenceUpload
{
    private static readonly char[] NtfsInvalidChars = ['<', '>', ':', '"', '|', '?', '*'];

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// <c>true</c> solo si el nombre es plano (sin rutas ni control, ≤ 255), sin caracteres
    /// inválidos de NTFS, sin <c>.</c>/espacio al final y sin nombre reservado de Windows.
    /// </summary>
    public static bool IsValidUploadName(string? name)
    {
        if (!ReportImageRef.IsPlainName(name)) return false;
        var n = name!;
        if (n.IndexOfAny(NtfsInvalidChars) >= 0) return false;
        if (n.EndsWith('.') || n.EndsWith(' ')) return false;
        // Windows reserva el nombre aunque tenga una o varias extensiones ("CON.tar.gz"): se mira
        // lo que hay antes del primer punto.
        var dot = n.IndexOf('.');
        var stem = dot < 0 ? n : n[..dot];
        if (WindowsReservedNames.Contains(stem)) return false;
        return true;
    }

    /// <summary>
    /// Tope y espacio (§5.2). <c>null</c> si pasa. Con <paramref name="availableBytes"/> <c>null</c>
    /// (no se pudo sondear) el chequeo de espacio se saltea (fail-open).
    /// </summary>
    public static UploadRejection? Evaluate(long size, long maxUploadBytes, long? availableBytes, long minFreeBytes)
    {
        if (size > maxUploadBytes)
            return new UploadRejection(UploadRejectionKind.TooLarge, size, maxUploadBytes);
        if (availableBytes is { } available)
        {
            var required = size + minFreeBytes;
            if (available < required)
                return new UploadRejection(UploadRejectionKind.InsufficientStorage, size, maxUploadBytes,
                    required, available);
        }
        return null;
    }

    /// <summary>
    /// Base 1024, etiquetas B/KB/MB/GB, un decimal con coma y sin ",0" final. Independiente de la
    /// cultura del proceso (mismas reglas que <c>client/src/lib/format.ts</c>).
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        string[] units = ["KB", "MB", "GB"];
        double value = bytes;
        var unit = -1;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        var text = rounded.ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',');
        return text + " " + units[unit];
    }

    // ── Mensajes (fallback legible del campo "error", §5.2) ──────────────────

    public const string InvalidFilenameMessage =
        "Nombre de archivo inválido: no puede tener rutas ni los caracteres < > : \" | ? *";

    public const string LengthRequiredMessage = "Falta el tamaño del archivo (Content-Length)";

    public static string FileTooLargeMessage(string name, long size, long max) =>
        $"El archivo {name} pesa {FormatBytes(size)} y el máximo permitido es {FormatBytes(max)}";

    public static string InsufficientStorageMessage(string name, long? required, long? available) =>
        required is { } r && available is { } a
            ? $"No hay espacio en el disco del servidor para guardar {name} (hace falta {FormatBytes(r)}, quedan {FormatBytes(a)})"
            : $"No hay espacio en el disco del servidor para guardar {name}";

    public static string IncompleteUploadMessage(string name, long received, long expected) =>
        $"La subida de {name} llegó incompleta ({FormatBytes(received)} de {FormatBytes(expected)}); no se guardó nada";

    public static string StorageErrorMessage(string name) =>
        $"El servidor no pudo guardar {name}; no se guardó nada";
}
