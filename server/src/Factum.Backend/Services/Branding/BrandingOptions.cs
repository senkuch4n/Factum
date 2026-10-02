namespace Factum.Backend.Services.Branding;

/// <summary>
/// Identidad de la organización que emite los informes (sección <c>Branding</c> de la
/// config). Todo vacío por defecto: los datos reales del cliente viven fuera del repo
/// (<c>appsettings.Local.json</c> ignorado por git o variables de entorno
/// <c>Branding__*</c>). Ver README, "Identidad de la organización (Branding)".
/// </summary>
public sealed class BrandingOptions
{
    /// <summary>Nombre del emisor. Vacío = no configurado.</summary>
    public string OrganizationName { get; set; } = "";

    /// <summary>Ruta (absoluta o relativa al ContentRoot) a un PNG o JPEG.</summary>
    public string OrganizationLogo { get; set; } = "";

    /// <summary>Líneas libres de contacto (domicilio, teléfonos, correo, matrícula…).</summary>
    public List<string> ContactLines { get; set; } = [];

    /// <summary>
    /// Isotipo (versión reducida del logo, idealmente PNG transparente): va al cierre del
    /// informe, debajo de la firma. Mismas reglas que <see cref="OrganizationLogo"/>.
    /// Vacío = sin isotipo.
    /// </summary>
    public string OrganizationIsotype { get; set; } = "";

    /// <summary>
    /// Color primario del informe (filetes, números de sección, línea de la tabla de hashes),
    /// <c>#RRGGBB</c>. Vacío = verde de Factum.
    /// </summary>
    public string PrimaryColor { get; set; } = "";

    /// <summary>
    /// Color de acento del informe, <c>#RRGGBB</c>. Vacío = tinte de Factum; es el fondo de la
    /// fila del contenedor ZIP en la tabla de hashes (tiene que contrastar ≥ 4.5:1 con la tinta).
    /// </summary>
    public string AccentColor { get; set; } = "";
}
