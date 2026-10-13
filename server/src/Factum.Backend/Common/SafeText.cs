using System.Text;

namespace Factum.Backend.Common;

/// <summary>Forma del texto de entrada: una línea (saltos y tabs → espacio) o multilínea (se conservan).</summary>
public enum TextShape { SingleLine, MultiLine }

/// <summary>
/// Caracteres que la API no guarda y que el render no emite (informe-formato-fixes #62 D1 A; adopta
/// api-hardening #63 §4.8 con DEL y C1 conservados). Puro e idempotente. Recorre por code point: un
/// surrogate alto seguido de uno bajo forma un code point; un surrogate sin pareja se quita.
/// <list type="bullet">
/// <item>C0 (U+0000–U+001F) salvo \t \n \r: se quitan. En SingleLine, \r\n, \r, \n y \t pasan a un espacio.</item>
/// <item>DEL y C1 (U+007F–U+009F): se conservan.</item>
/// <item>Bidi U+202A–U+202E y U+2066–U+2069: se quitan (LRM/RLM/ALM se conservan).</item>
/// <item>No-caracteres U+FDD0–U+FDEF y U+xFFFE/U+xFFFF de los 17 planos: se quitan.</item>
/// </list>
/// Espejo en el cliente: <c>cleanText</c> de <c>client/src/lib/report-markdown.ts</c> (render).
/// </summary>
public static class SafeText
{
    /// <summary>null → "". NO hace Trim (lo hace quien llama, después). Idempotente.</summary>
    public static string Clean(string? value, TextShape shape)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (IsAlreadyClean(value, shape, stripC0: true)) return value;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    var cp = char.ConvertToUtf32(ch, value[i + 1]);
                    if (!IsNonCharacter(cp)) sb.Append(ch).Append(value[i + 1]);
                    i++;
                }
                continue; // par no-carácter o alto suelto
            }
            if (char.IsLowSurrogate(ch)) continue; // bajo suelto

            if (shape == TextShape.SingleLine)
            {
                if (ch == '\r')
                {
                    sb.Append(' ');
                    if (i + 1 < value.Length && value[i + 1] == '\n') i++;
                    continue;
                }
                if (ch is '\n' or '\t') { sb.Append(' '); continue; }
            }

            if (IsStrippedControl(ch, shape) || IsBidiControl(ch) || IsNonCharacter(ch)) continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>null → null; si no, <see cref="Clean"/>.</summary>
    public static string? CleanOrNull(string? value, TextShape shape) =>
        value is null ? null : Clean(value, shape);

    /// <summary>Solo bidi + no-caracteres + surrogates sueltos; deja los controles C0 (para quien ya los rechaza).</summary>
    public static string StripInvisible(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (IsAlreadyClean(value, TextShape.MultiLine, stripC0: false)) return value;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    var cp = char.ConvertToUtf32(ch, value[i + 1]);
                    if (!IsNonCharacter(cp)) sb.Append(ch).Append(value[i + 1]);
                    i++;
                }
                continue;
            }
            if (char.IsLowSurrogate(ch)) continue;
            if (IsBidiControl(ch) || IsNonCharacter(ch)) continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>U+202A–U+202E (LRE, RLE, PDF, LRO, RLO) y U+2066–U+2069 (LRI, RLI, FSI, PDI).</summary>
    public static bool IsBidiControl(int codePoint) =>
        codePoint is >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069;

    /// <summary>U+FDD0–U+FDEF y los dos últimos code points de cada plano (U+xFFFE, U+xFFFF).</summary>
    public static bool IsNonCharacter(int codePoint) =>
        codePoint is >= 0xFDD0 and <= 0xFDEF || (codePoint >= 0 && (codePoint & 0xFFFE) == 0xFFFE);

    /// <summary>
    /// #62: SOLO C0 U+0000–U+001F salvo \t \n \r (en las dos formas; en SingleLine esos tres pasan
    /// a espacio antes de llegar acá). DEL y C1 (U+007F–U+009F) NO se quitan (diferencia con #63).
    /// </summary>
    public static bool IsStrippedControl(int codePoint, TextShape shape) =>
        codePoint is >= 0 and < 0x20 and not (0x09 or 0x0A or 0x0D);

    // Camino rápido: la gran mayoría de los strings no tienen nada que limpiar.
    private static bool IsAlreadyClean(string value, TextShape shape, bool stripC0)
    {
        foreach (var ch in value)
        {
            if (ch < 0x20)
            {
                if (shape == TextShape.SingleLine && stripC0) return false;
                if (stripC0 && ch is not ('\t' or '\n' or '\r')) return false;
                continue;
            }
            if (char.IsSurrogate(ch) || IsBidiControl(ch) || IsNonCharacter(ch)) return false;
        }
        return true;
    }
}
