using System.Text;
using System.Text.RegularExpressions;

namespace Factum.Backend.Services.Reports;

/// <summary>
/// Referencia a una captura del caso dentro del Markdown de una sección del informe
/// (editor-imagenes-informe, SDD §4.1 y §6.2): <c>![&lt;alt&gt;](captura:&lt;nombre-codificado&gt;)</c>.
/// El nombre va en UTF-8 con cada byte fuera de <c>[A-Za-z0-9._-]</c> como <c>%XX</c> (hex en
/// mayúsculas al emitir; al leer se acepta también en minúsculas). Mismo criterio que
/// <c>encodeReportImageName</c>/<c>decodeReportImageName</c> de <c>client/src/lib/report-markdown.ts</c>.
/// </summary>
public static class ReportImageRef
{
    public const string Scheme = "captura:";
    public const int MaxAltLength = 200;
    public const int MaxPerSection = 20;
    public const int MaxNameLength = 255;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    // Ordinal y sin ignorar mayúsculas: "CAPTURA:" no vale.
    private static readonly Regex UrlRegex = new(
        @"^captura:(?:[A-Za-z0-9._-]|%[0-9A-Fa-f]{2})+$", RegexOptions.CultureInvariant);

    private static bool IsUnreserved(byte b) =>
        b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9'
            or (byte)'.' or (byte)'_' or (byte)'-';

    /// <summary>Bytes UTF-8 fuera de <c>[A-Za-z0-9._-]</c> → <c>%XX</c> (hex en mayúsculas).</summary>
    public static string EncodeName(string filename)
    {
        ArgumentNullException.ThrowIfNull(filename);
        var sb = new StringBuilder(filename.Length + 8);
        foreach (var b in Encoding.UTF8.GetBytes(filename))
        {
            if (IsUnreserved(b)) sb.Append((char)b);
            else sb.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>
    /// <paramref name="url"/> = destino del <c>LinkInline</c> tal como lo deja Markdig. true si
    /// cumple <c>^captura:(?:[A-Za-z0-9._-]|%[0-9A-Fa-f]{2})+$</c>, el decode da UTF-8 estricto y el
    /// nombre es <see cref="IsInsertableName">insertable</see>. Decodifica a mano (no con
    /// <c>Uri.UnescapeDataString</c>, que no valida el UTF-8).
    /// </summary>
    public static bool TryParse(string? url, out string filename)
    {
        filename = string.Empty;
        if (string.IsNullOrEmpty(url) || !UrlRegex.IsMatch(url)) return false;

        var encoded = url.AsSpan(Scheme.Length);
        var bytes = new List<byte>(encoded.Length);
        for (var i = 0; i < encoded.Length; i++)
        {
            if (encoded[i] == '%')
            {
                // El regex ya garantiza dos dígitos hex después de cada '%'.
                bytes.Add((byte)((HexValue(encoded[i + 1]) << 4) | HexValue(encoded[i + 2])));
                i += 2;
            }
            else
            {
                bytes.Add((byte)encoded[i]);
            }
        }

        string decoded;
        try
        {
            decoded = StrictUtf8.GetString(bytes.ToArray());
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        if (!IsInsertableName(decoded)) return false;
        filename = decoded;
        return true;
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'F' => c - 'A' + 10,
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => throw new ArgumentOutOfRangeException(nameof(c)),
    };

    /// <summary>
    /// Nombre plano (§4.2): no vacío, sin <c>/</c>, <c>\</c> ni caracteres de control, distinto
    /// de <c>.</c> y <c>..</c>, igual a su <c>Path.GetFileName</c> y de 255 caracteres como máximo.
    /// </summary>
    public static bool IsPlainName(string? filename)
    {
        if (string.IsNullOrEmpty(filename) || filename.Length > MaxNameLength) return false;
        if (filename is "." or "..") return false;
        foreach (var ch in filename)
            if (ch is '/' or '\\' || ch < 0x20 || ch == 0x7F) return false;
        return Path.GetFileName(filename) == filename;
    }

    /// <summary>
    /// "Captura insertable" (§4.2, solo por el nombre): nombre plano, captura de pantalla según
    /// <see cref="EvidenceClassifier"/> (no foto de identidad) y no artefacto generado.
    /// </summary>
    public static bool IsInsertableName(string? filename) =>
        IsPlainName(filename) &&
        EvidenceClassifier.Classify(filename!, hasFileSource: false) == EvidenceClass.Screenshot &&
        !ReportService.IsGeneratedArtifact(filename!);

    /// <summary>Forma canónica: <c>"![" + EscapeInline(alt) + "](captura:" + EncodeName(f) + ")"</c>.</summary>
    public static string Format(string filename, string alt) =>
        "![" + ReportMarkdown.EscapeInline(alt) + "](" + Scheme + EncodeName(filename) + ")";
}
