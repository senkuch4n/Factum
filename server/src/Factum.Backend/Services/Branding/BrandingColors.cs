using System.Globalization;

namespace Factum.Backend.Services.Branding;

/// <summary>
/// Colores de marca del informe (<c>Branding:PrimaryColor</c> / <c>Branding:AccentColor</c>).
/// Los defaults son la paleta de Factum (la marca del producto, no datos de un cliente): los
/// colores reales de un estudio son configuración local, nunca default de código. El primario
/// es además el "centinela" que trae <c>plantilla_informe_v6.docx</c> (ver
/// <c>Reports/BrandColors</c>); el acento no va en la plantilla: lo aplica B-R5 como fondo de
/// la fila del contenedor ZIP.
/// </summary>
public static class BrandingColors
{
    /// <summary>Verde de Factum (<c>--fx-accent</c> claro), ≈ 6.2:1 con blanco.</summary>
    public const string DefaultPrimary = "2F6F12";

    /// <summary>Tinte de Factum (<c>--fx-accent-soft</c> claro): fondo de la fila del ZIP.</summary>
    public const string DefaultAccent = "E8F3DF";

    /// <summary>Tinta de Factum: el texto que va sobre el tinte.</summary>
    public const string InkColor = "0E1013";

    /// <summary>Contraste mínimo del primario con blanco (WCAG AA, texto normal).</summary>
    public const double MinPrimaryContrast = 4.5;

    /// <summary>Contraste mínimo del acento con la tinta (el acento es fondo de texto).</summary>
    public const double MinAccentContrastWithInk = 4.5;

    /// <summary>"#1a2b3c" | "1A2B3C" (con espacios alrededor o no) → "1A2B3C"; cualquier otra cosa → null.</summary>
    public static string? Normalize(string? raw)
    {
        var s = raw?.Trim() ?? "";
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length != 6) return null;
        foreach (var c in s)
            if (!char.IsAsciiHexDigit(c)) return null;
        return s.ToUpperInvariant();
    }

    /// <summary>Contraste WCAG 2.x entre dos colores (6 dígitos hex, sin '#'). Simétrico.</summary>
    public static double Contrast(string hexA, string hexB)
    {
        var a = RelativeLuminance(hexA);
        var b = RelativeLuminance(hexB);
        var (hi, lo) = a >= b ? (a, b) : (b, a);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>Contraste WCAG 2.x entre el color (6 dígitos hex, sin '#') y blanco.</summary>
    public static double ContrastWithWhite(string hex6) => Contrast(hex6, "FFFFFF");

    private static double RelativeLuminance(string hex6)
    {
        double Channel(int offset)
        {
            var c = int.Parse(hex6.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
    }
}
