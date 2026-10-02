namespace Factum.Backend.Services.Auth;

/// <summary>Modos de autenticación que expone el backend (<c>GET /api/auth/mode</c>, <c>/health</c>).</summary>
public static class AuthModes
{
    public const string Dev = "dev";
    public const string External = "external";
}

/// <summary>
/// Configuración ya resuelta y validada del proveedor HTTP externo de login
/// (<c>Auth:External:*</c>). La arma <see cref="AuthSettingsResolver"/>.
/// </summary>
public sealed class ExternalAuthOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string LoginPath { get; set; } = "/auth/login";
    public int TimeoutSeconds { get; set; } = 10;
    public ExternalAuthRequestFields Request { get; set; } = new();
    public ExternalAuthResponseFields Response { get; set; } = new();
}

/// <summary>Nombres (planos) de los campos del body JSON que se manda al proveedor.</summary>
public sealed class ExternalAuthRequestFields
{
    public string DniField { get; set; } = "dni";
    public string UserField { get; set; } = "user";
    public string PasswordField { get; set; } = "password";
}

/// <summary>
/// Campos que se leen de la respuesta del proveedor. Admiten rutas con puntos
/// (<c>data.user.fullName</c>). <see cref="SiglaField"/> <c>null</c> o vacío = no se lee sigla.
/// </summary>
public sealed class ExternalAuthResponseFields
{
    public string NameField { get; set; } = "name";
    public string? SiglaField { get; set; } = "sigla";
}
