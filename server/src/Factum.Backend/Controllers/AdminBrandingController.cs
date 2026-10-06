using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Services.Branding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Controllers;

/// <summary>
/// Marca del informe de cualquier cuenta, desde el panel (marca-por-cliente §6.5, D1-C). Solo superadmins en
/// <c>Auth:Mode=local</c>, con los filtros en el mismo orden que <see cref="AdminUsersController"/>: un cliente
/// recibe 403 <c>superadmin_required</c> y fuera de local, 404 <c>not_available</c>. <c>{id}</c> = <c>users._id</c>.
/// </summary>
[ApiController]
[Route("api/admin/users/{id}/branding")]
[Authorize]
[RequireLocalAuthMode]   // Order = -10: corre ANTES que RequireSuperadmin
[RequireSuperadmin]      // Order por defecto (0)
public sealed class AdminBrandingController(IAccountBrandingService brandings, IOptions<JsonOptions> json)
    : ControllerBase
{
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType<AccountBrandingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return BrandingHttp.Respond(await brandings.GetForAccountAsync(id, ct), Ok);
    }

    /// <summary>Igual que el PUT propio, sobre la cuenta <paramref name="id"/>.</summary>
    [HttpPut]
    [DisableFormValueModelBinding]
    [Produces("application/json")]
    [ProducesResponseType<AccountBrandingSaveResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Put(string id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var input = await BrandingHttp.ReadAsync(HttpContext, json.Value.JsonSerializerOptions, ct);
        if (!input.IsSuccess) return BrandingHttp.Error(input);
        return BrandingHttp.Respond(
            await brandings.SaveForAccountAsync(BrandingHttp.ActorOf(HttpContext), id, input.Value!, ct), Ok);
    }

    [HttpGet("logo")]
    [HttpHead("logo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Logo(string id, CancellationToken ct) => Image(id, BrandingImageKind.Logo, ct);

    [HttpGet("isotype")]
    [HttpHead("isotype")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Isotype(string id, CancellationToken ct) => Image(id, BrandingImageKind.Isotype, ct);

    private async Task<IActionResult> Image(string id, BrandingImageKind kind, CancellationToken ct)
    {
        var result = await brandings.GetAccountImageAsync(id, kind, ct);
        return result.IsSuccess
            ? BrandingHttp.ImageResult(Request, Response, result.Value!)
            : BrandingHttp.Error(result);
    }
}
