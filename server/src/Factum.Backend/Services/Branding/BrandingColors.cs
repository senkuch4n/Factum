using System.Globalization;

namespace Factum.Backend.Services.Branding;

/// <summary>
/// Colores de marca del informe (<c>Branding:PrimaryColor</c> / <c>Branding:AccentColor</c>).
/// Los defaults son neutros a propósito: los colores reales del cliente son configuración
/// local, nunca default de código. Los mismos valores son los "centinelas" que trae
/// <c>plantilla_informe_v5.docx</c> (ver <c>Reports/BrandColors</c>).
/// </summary>
public static class BrandingColors
{
    /// <summary>Gris pizarra, 11.4:1 con blanco.</summary>
    public const string DefaultPrimary = "2F3B4C";

    /// <summary>Gris claro (solo decoración, nunca texto).</summary>
    public const string DefaultAccent = "9AA5B1";

    /// <summary>Contraste mínimo del primario con blanco (WCAG AA, texto normal).</summary>
    public const double MinPrimaryContrast = 4.5;

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

    /// <summary>Contraste WCAG 2.x entre el color (6 dígitos hex, sin '#') y blanco.</summary>
    public static double ContrastWithWhite(string hex6)
    {
        var l = RelativeLuminance(hex6);
        return (1.0 + 0.05) / (l + 0.05);
    }

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
