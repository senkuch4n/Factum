namespace Factum.Backend.DTOs;

/// <summary>
/// Respuesta de <c>GET /api/config/public</c> (anónimo). JSON en snake_case_lower:
/// <c>organization_name</c>, <c>organization_logo_url</c>, <c>support_enabled</c>, <c>encrypt_zip</c>. Los dos primeros son <c>null</c> si no hay
/// <c>Branding</c> configurado. <c>organization_logo_url</c> es una ruta relativa a la raíz del
/// backend (empieza con <c>/</c>) e incluye <c>?v=&lt;version&gt;</c> para invalidar caché.
/// <c>support_enabled</c> (siempre presente) es <c>true</c> solo si <c>Integrations:Support:Enabled=true</c>
/// y la config de soporte validó al arrancar.
/// <c>encrypt_zip</c> (siempre presente) refleja <c>Report:EncryptZip</c>: si el ZIP de evidencia se
/// genera cifrado con AES-256. El wizard lo usa para saber qué prometer antes de generar.
/// </summary>
public sealed record PublicConfigResponse(string? OrganizationName, string? OrganizationLogoUrl,
    bool SupportEnabled, bool EncryptZip);
