using System.Text.RegularExpressions;

namespace Factum.Backend.Services.Tatana;

/// <summary>
/// Sección <c>Tatana</c> de la config (SDD tatana-instalador-autoupdate D6, D-T19).
/// <c>MinVersion</c>: versión mínima de Tatana con la que la web captura. Vacío = no bloquea;
/// <c>X.Y.Z</c> = la web marca como desactualizado a un Tatana menor (o sin <c>real_version_v1</c>).
/// Override: <c>Tatana__MinVersion</c> (en la nube, <c>FACTUM_TATANA_VERSION_MINIMA</c> de <c>factum.env</c>).
/// Cualquier otro valor hace que el backend no arranque.
/// </summary>
public sealed partial class TatanaOptions
{
    public string? MinVersion { get; set; }

    [GeneratedRegex(@"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    /// <summary>Blancos → null; el resto, recortado.</summary>
    public static string? Normalize(string? minVersion) =>
        string.IsNullOrWhiteSpace(minVersion) ? null : minVersion.Trim();

    /// <summary>null si es válido (vacío o X.Y.Z); si no, el texto para <c>configErrors</c>.</summary>
    public static string? Validate(string? minVersion) =>
        Normalize(minVersion) is { } v && !VersionPattern().IsMatch(v)
            ? "Tatana:MinVersion tiene que ser X.Y.Z o vacío"
            : null;
}
