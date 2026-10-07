namespace Factum.Backend.DTOs;

/// <summary>
/// Respuesta de <c>GET /api/config/public</c> (anónimo). JSON en snake_case_lower:
/// <c>organization_name</c>, <c>organization_logo_url</c>, <c>support_enabled</c>, <c>encrypt_zip</c>, <c>tatana_min_version</c>. Los dos primeros son <c>null</c> si no hay
/// <c>Branding</c> configurado. <c>organization_logo_url</c> es una ruta relativa a la raíz del
/// backend (empieza con <c>/</c>) e incluye <c>?v=&lt;version&gt;</c> para invalidar caché.
/// <c>support_enabled</c> (siempre presente) es <c>true</c> solo si <c>Integrations:Support:Enabled=true</c>
/// y la config de soporte validó al arrancar.
/// <c>encrypt_zip</c> (siempre presente) refleja <c>Report:EncryptZip</c>: si el ZIP de evidencia se
/// genera cifrado con AES-256. El wizard lo usa para saber qué prometer antes de generar.
/// <c>tatana_min_version</c> (siempre presente) es <c>Tatana:MinVersion</c> normalizado: <c>"X.Y.Z"</c> o
/// <c>null</c> (sin mínimo). Con un valor, la web no inicia capturas con un Tatana menor o sin
/// <c>real_version_v1</c> (SDD tatana-instalador-autoupdate D6, §13.B).
/// </summary>
public sealed record PublicConfigResponse(string? OrganizationName, string? OrganizationLogoUrl,
    bool SupportEnabled, bool EncryptZip, string? TatanaMinVersion);
