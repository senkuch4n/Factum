using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Factum.Backend.Services.Branding;

namespace Factum.Backend.Services.Reports;

/// <summary>
/// Pasada B-R2b del informe: la plantilla v5 trae los colores de marca como "centinelas" (los
/// mismos defaults neutros de <see cref="BrandingColors"/>) y acá se reemplazan por los
/// configurados. Ver Refactorizaciones/informe-diseno-modelo.md §5.6. El script de la v5
/// garantiza que la v4 de origen no contenía ninguno de los dos valores, así que el
/// reemplazo nunca pisa un color ajeno.
/// </summary>
internal static class BrandColors
{
    public const string PrimarySentinel = BrandingColors.DefaultPrimary; // "2F3B4C"
    public const string AccentSentinel = BrandingColors.DefaultAccent;   // "9AA5B1"

    private static readonly string[] ColorAttributes = ["val", "fill", "color"];

    /// <summary>
    /// Reemplaza el valor de los atributos <c>val</c>/<c>fill</c>/<c>color</c> iguales (sin
    /// distinguir mayúsculas) a un centinela, en document, headers, footers, numbering y styles
    /// (incluido lo que está dentro de <c>mc:AlternateContent</c>). Devuelve la cantidad de
    /// reemplazos. Con los dos colores en sus defaults es un no-op.
    /// </summary>
    public static int Apply(MainDocumentPart main, string primary, string accent)
    {
        primary = BrandingColors.Normalize(primary) ?? PrimarySentinel;
        accent = BrandingColors.Normalize(accent) ?? AccentSentinel;
        if (primary == PrimarySentinel && accent == AccentSentinel) return 0;

        var roots = new List<OpenXmlPartRootElement>();
        if (main.Document is { } document) roots.Add(document);
        roots.AddRange(main.HeaderParts.Select(h => h.Header).OfType<OpenXmlPartRootElement>());
        roots.AddRange(main.FooterParts.Select(f => f.Footer).OfType<OpenXmlPartRootElement>());
        if (main.NumberingDefinitionsPart?.Numbering is { } numbering) roots.Add(numbering);
        if (main.StyleDefinitionsPart?.Styles is { } styles) roots.Add(styles);

        var count = 0;
        foreach (var root in roots)
        {
            foreach (var element in root.Descendants().Prepend(root))
            {
                if (!element.HasAttributes) continue;
                foreach (var a in element.GetAttributes())
                {
                    if (!ColorAttributes.Contains(a.LocalName) || a.Value is null) continue;
                    string? replacement = null;
                    if (string.Equals(a.Value, PrimarySentinel, StringComparison.OrdinalIgnoreCase))
                        replacement = primary;
                    else if (string.Equals(a.Value, AccentSentinel, StringComparison.OrdinalIgnoreCase))
                        replacement = accent;
                    if (replacement is null) continue;

                    element.SetAttribute(new OpenXmlAttribute(a.Prefix, a.LocalName, a.NamespaceUri, replacement));
                    count++;
                }
            }
        }
        return count;
    }
}
