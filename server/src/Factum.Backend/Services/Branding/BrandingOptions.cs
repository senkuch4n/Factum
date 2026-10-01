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
}
