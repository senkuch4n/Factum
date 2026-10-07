using Factum.Backend.Infrastructure;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Branding;

/// <summary>
/// Diff de la marca para la auditoría <c>update_branding</c> (marca-por-cliente §4.2 / §6.4). Pura y con
/// LISTA BLANCA de campos. Los valores van ya formateados: líneas unidas con <c>\n</c>, colores
/// <c>#RRGGBB</c> o <c>""</c> (default de Factum) e imágenes como su <c>Version</c> o <c>""</c>.
/// Nunca bytes de imagen; nunca <c>null</c> en <c>from</c>/<c>to</c>.
/// </summary>
public static class BrandingChanges
{
    public static readonly IReadOnlyList<string> AllowedFields =
    [
        BrandingErrors.FieldOrganizationName, BrandingErrors.FieldContactLines, BrandingErrors.FieldPrimaryColor,
        BrandingErrors.FieldAccentColor, BrandingErrors.FieldLogo, BrandingErrors.FieldIsotype,
    ];

    /// <summary>Campos que cambian, en el orden de <see cref="AllowedFields"/>. Con <paramref name="before"/> null, todo "from" es "".</summary>
    public static List<UserAdminChange> Diff(AccountBranding? before, NormalizedBranding after)
    {
        var pairs = new (string Field, string From, string To)[]
        {
            (BrandingErrors.FieldOrganizationName, before?.OrganizationName ?? "", after.OrganizationName),
            (BrandingErrors.FieldContactLines, JoinLines(before?.ContactLines), JoinLines(after.ContactLines)),
            (BrandingErrors.FieldPrimaryColor, FormatColor(before?.PrimaryColor), FormatColor(after.PrimaryColor)),
            (BrandingErrors.FieldAccentColor, FormatColor(before?.AccentColor), FormatColor(after.AccentColor)),
            (BrandingErrors.FieldLogo, before?.Logo?.Version ?? "", VersionAfter(before?.Logo, after.Logo)),
            (BrandingErrors.FieldIsotype, before?.Isotype?.Version ?? "", VersionAfter(before?.Isotype, after.Isotype)),
        };
        return pairs
            .Where(p => !string.Equals(p.From, p.To, StringComparison.Ordinal))
            .Select(p => new UserAdminChange { Field = p.Field, From = p.From, To = p.To })
            .ToList();
    }

    /// <summary>"RRGGBB" → "#RRGGBB"; "" (o null) → "".</summary>
    public static string FormatColor(string? hex) => string.IsNullOrEmpty(hex) ? "" : "#" + hex;

    public static string JoinLines(IReadOnlyList<string>? lines) => lines is null ? "" : string.Join("\n", lines);

    /// <summary>Versión que queda después del guardado: keep → la actual, replace → la nueva, remove → "".</summary>
    public static string VersionAfter(AccountBrandingImage? current, ImageChange change) => change.Action switch
    {
        BrandingImageAction.Replace => change.Image?.Version ?? "",
        BrandingImageAction.Remove => "",
        _ => current?.Version ?? "",
    };
}
