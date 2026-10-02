using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Factum.Backend.Services.Catalogs;

/// <summary>
/// Limpieza y clave de deduplicación de los valores de catálogo (SDD §3.5, D8). Los vectores
/// tienen que dar igual que <c>normalizeCatalogKey</c> en <c>client/src/lib/catalogs.ts</c>.
/// </summary>
public static partial class CatalogText
{
    public const int MaxValue = 300; // = CaseValidation.MaxLine

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Valor a guardar: trim + cualquier secuencia de espacios en blanco (\s+, incluye \t y \n) → un espacio.</summary>
    public static string CleanValue(string? s) =>
        string.IsNullOrEmpty(s) ? string.Empty : Whitespace().Replace(s, " ").Trim();

    /// <summary>
    /// Clave de dedupe: CleanValue → Normalize(FormD) → saca UnicodeCategory.NonSpacingMark,
    /// SpacingCombiningMark y EnclosingMark → Normalize(FormC) → ToLowerInvariant.
    /// </summary>
    public static string NormalizeKey(string? s)
    {
        var clean = CleanValue(s);
        if (clean.Length == 0) return string.Empty;

        var decomposed = clean.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark)
                continue;
            sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
