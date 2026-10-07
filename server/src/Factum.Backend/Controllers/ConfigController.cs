using Factum.Backend.DTOs;
using Factum.Backend.Services.Branding;
using Factum.Backend.Services.Reports;
using Factum.Backend.Services.Support;
using Factum.Backend.Services.Tatana;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Controllers;

/// <summary>
/// Configuración pública (sin autenticación): la usa el login antes de que exista un token.
/// Expone el nombre y el logo del default de la instalación (la <c>Branding</c> de appsettings: login y
/// fallback de las cuentas sin marca propia, marca-por-cliente D4/D6; la marca de cada cuenta NO sale de acá) y si la integración de soporte está habilitada
/// (<c>support_enabled</c>), si el ZIP de evidencia se cifra (<c>encrypt_zip</c>) y la versión mínima de
/// Tatana (<c>tatana_min_version</c>); <c>ContactLines</c> y la config de soporte no salen de acá.
/// </summary>
[ApiController]
[Route("api/config")]
[AllowAnonymous]
public sealed class ConfigController(IBrandingService branding, SupportSettings support,
    IReportSettings report, IOptions<TatanaOptions> tatana) : ControllerBase
{
    public const string LogoPath = "/api/config/branding/logo";

    [HttpGet("public")]
    [Produces("application/json")]
    [ProducesResponseType<PublicConfigResponse>(StatusCodes.Status200OK)]
    public IActionResult Public()
    {
        var snap = branding.Current;
        var logoUrl = snap.Logo is { } logo ? $"{LogoPath}?v={logo.Version}" : null;
        return Ok(new PublicConfigResponse(snap.OrganizationName, logoUrl, support.Enabled,
            report.EncryptZip, TatanaOptions.Normalize(tatana.Value.MinVersion)));
    }

    // El parámetro "v" de la query es solo cache-busting del lado del cliente; se ignora.
    // Los bytes salen de memoria (cargados al arrancar): no hay acceso a disco por request.
    [HttpGet("branding/logo")]
    [HttpHead("branding/logo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Logo()
    {
        var logo = branding.Current.Logo;
        if (logo is null)
            return NotFound(new { error = "Sin logo configurado" });

        var etag = $"\"{logo.Version}\"";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "public, max-age=3600";
        Response.Headers.ETag = etag;

        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
        if (ifNoneMatch.Length > 0 &&
            ifNoneMatch.Split(',').Select(t => t.Trim()).Any(t => t == etag || t == "W/" + etag || t == "*"))
            return StatusCode(StatusCodes.Status304NotModified);

        return File(logo.Data, logo.ContentType);
    }
}
