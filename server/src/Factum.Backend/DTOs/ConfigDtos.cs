namespace Factum.Backend.DTOs;

/// <summary>
/// Respuesta de <c>GET /api/config/public</c> (anónimo). JSON en snake_case_lower:
/// <c>organization_name</c>, <c>organization_logo_url</c>. Los dos son <c>null</c> si no hay
/// <c>Branding</c> configurado. <c>organization_logo_url</c> es una ruta relativa a la raíz del
/// backend (empieza con <c>/</c>) e incluye <c>?v=&lt;version&gt;</c> para invalidar caché.
/// </summary>
public sealed record PublicConfigResponse(string? OrganizationName, string? OrganizationLogoUrl);
