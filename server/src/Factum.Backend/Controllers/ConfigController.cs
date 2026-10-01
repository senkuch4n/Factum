using Factum.Backend.DTOs;
using Factum.Backend.Services.Branding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Backend.Controllers;

/// <summary>
/// Configuración pública (sin autenticación): la usa el login antes de que exista un token.
/// Solo expone el nombre y el logo de la organización; <c>ContactLines</c> no sale de acá.
/// </summary>
[ApiController]
[Route("api/config")]
[AllowAnonymous]
public sealed class ConfigController(IBrandingService branding) : ControllerBase
{
    public const string LogoPath = "/api/config/branding/logo";

    [HttpGet("public")]
    [Produces("application/json")]
    [ProducesResponseType<PublicConfigResponse>(StatusCodes.Status200OK)]
    public IActionResult Public()
    {
        var snap = branding.Current;
        var logoUrl = snap.Logo is { } logo ? $"{LogoPath}?v={logo.Version}" : null;
        return Ok(new PublicConfigResponse(snap.OrganizationName, logoUrl));
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
