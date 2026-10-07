using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Catalogs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Backend.Controllers;

// Catálogos de sugerencias del perito (formulario-caso-catalogos, SDD §4.4). El dueño es
// siempre el DNI del token: una entrada ajena o inexistente da 404 (D7). Errores { error }.
[ApiController]
[Route("api/catalogs")]
[Authorize]
[Produces("application/json")]
public sealed class CatalogsController(ICatalogService catalogs) : ControllerBase
{
    private User Officer => (User)HttpContext.Items["User"]!;

    [HttpGet]
    [ProducesResponseType<CatalogsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await catalogs.GetAsync(Officer.Dni, ct));

    [HttpPut("{catalog}/entries/{id}")]
    [ProducesResponseType<CatalogEntryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(string catalog, string id,
        [FromBody] UpdateCatalogEntryRequest request, CancellationToken ct)
    {
        var result = await catalogs.UpdateAsync(Officer.Dni, catalog, id, request, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
    }

    [HttpDelete("{catalog}/entries/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string catalog, string id, CancellationToken ct)
    {
        var result = await catalogs.DeleteAsync(Officer.Dni, catalog, id, ct);
        return result.IsSuccess ? NoContent() : this.ErrorResult(result);
    }
}
