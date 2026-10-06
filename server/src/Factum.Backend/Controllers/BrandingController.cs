using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Services.Branding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Controllers;

/// <summary>
/// Marca del informe de la cuenta logueada (marca-por-cliente §6.5, D7-A): cualquier rol y cualquier modo de
/// auth. El dueño sale SIEMPRE de la sesión: ninguna ruta recibe un DNI ni un id. Sin
/// <c>[AllowDuringPasswordChange]</c>: con la temporal pendiente, el gate responde 403.
/// </summary>
[ApiController]
[Route("api/branding")]
[Authorize]
public sealed class BrandingController(IAccountBrandingService brandings, IOptions<JsonOptions> json) : ControllerBase
{
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType<AccountBrandingResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return BrandingHttp.Respond(await brandings.GetOwnAsync(BrandingHttp.UserOf(HttpContext), ct), Ok);
    }

    /// <summary>multipart/form-data: <c>metadata</c> (JSON <see cref="BrandingSaveMetadata"/>), <c>logo?</c>, <c>isotype?</c>.</summary>
    [HttpPut]
    [DisableFormValueModelBinding]
    [Produces("application/json")]
    [ProducesResponseType<AccountBrandingSaveResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Put(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var input = await BrandingHttp.ReadAsync(HttpContext, json.Value.JsonSerializerOptions, ct);
        if (!input.IsSuccess) return BrandingHttp.Error(input);
        var user = BrandingHttp.UserOf(HttpContext);
        return BrandingHttp.Respond(
            await brandings.SaveOwnAsync(BrandingHttp.ActorOf(HttpContext), user, input.Value!, ct), Ok);
    }

    [HttpGet("logo")]
    [HttpHead("logo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Logo(CancellationToken ct) => Image(BrandingImageKind.Logo, ct);

    [HttpGet("isotype")]
    [HttpHead("isotype")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Isotype(CancellationToken ct) => Image(BrandingImageKind.Isotype, ct);

    private async Task<IActionResult> Image(BrandingImageKind kind, CancellationToken ct)
    {
        var result = await brandings.GetOwnImageAsync(BrandingHttp.UserOf(HttpContext).Dni, kind, ct);
        return result.IsSuccess
            ? BrandingHttp.ImageResult(Request, Response, result.Value!)
            : BrandingHttp.Error(result);
    }
}
