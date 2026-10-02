using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Profile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Backend.Controllers;

/// <summary>Perfil del perito del usuario autenticado (colección <c>expert_profiles</c>).</summary>
[ApiController]
[Route("api/profile")]
[Authorize]
[Produces("application/json")]
public sealed class ProfileController(IExpertProfileService profiles) : ControllerBase
{
    private User CurrentUser => (User)HttpContext.Items["User"]!;

    [HttpGet]
    [ProducesResponseType<ExpertProfileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(await profiles.GetAsync(CurrentUser, ct));

    [HttpPut]
    [ProducesResponseType<ExpertProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Put([FromBody] ExpertProfileRequest request, CancellationToken ct)
    {
        var result = await profiles.SaveAsync(CurrentUser, request, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
    }
}
