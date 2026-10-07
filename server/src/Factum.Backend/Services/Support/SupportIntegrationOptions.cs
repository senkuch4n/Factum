namespace Factum.Backend.Services.Support;

/// <summary>
/// Integración opcional de soporte (Faro), sección <c>Integrations:Support</c>. Apagada por
/// defecto. La arma y valida <see cref="SupportSettingsResolver"/>.
/// </summary>
public sealed class SupportIntegrationOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Secreto compartido con Faro. Nunca va en archivos versionados ni en logs.</summary>
    public string ServiceKey { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 10;

    // URL pública del frontend de Faro (no del backend): a donde se manda al
    // oficial con el código de SSO.
    public string FrontendUrl { get; set; } = string.Empty;
}
