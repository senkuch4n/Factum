using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Backend.Controllers;

[ApiController]
[Route("api/support")]
[Authorize]
[Produces("application/json")]
public sealed class SupportController(ISupportService supportService) : ControllerBase
{
    private User Officer => (User)HttpContext.Items["User"]!;

    // El oficial ya está autenticado en Factum — no vuelve a loguearse en
    // Faro. Este endpoint arma el token de soporte con su identidad y lo
    // manda al backend de Faro por su cuenta.
    [HttpPost("tokens")]
    [ProducesResponseType<ReportarProblemaResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReportarProblema(
        [FromBody] ReportarProblemaRequest request, CancellationToken ct)
    {
        var result = await supportService.ReportarProblemaAsync(Officer, request, ct);
        return result.Match<IActionResult>(
            onSuccess: Ok,
            onFailure: err => BadRequest(new { error = err }));
    }

    [HttpGet("tokens")]
    [ProducesResponseType<List<MiTokenDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarMisReportes(CancellationToken ct)
    {
        var result = await supportService.ListarMisReportesAsync(Officer, ct);
        return result.Match<IActionResult>(
            onSuccess: tokens => Ok(new { tokens }),
            onFailure: err => BadRequest(new { error = err }));
    }

    // Genera la URL para que el oficial abra Faro ya logueado, sin volver a
    // ingresar credenciales ahí (misma identidad de MPF que en Factum).
    [HttpGet("faro-sso")]
    [ProducesResponseType<FaroSsoLinkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ObtenerLinkFaro(CancellationToken ct)
    {
        var result = await supportService.ObtenerLinkFaroAsync(Officer, ct);
        return result.Match<IActionResult>(
            onSuccess: Ok,
            onFailure: err => BadRequest(new { error = err }));
    }

    [HttpPost("tokens/{idToken:int}/calificacion")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Calificar(
        int idToken, [FromBody] CalificarTokenRequest request, CancellationToken ct)
    {
        var result = await supportService.CalificarReporteAsync(Officer, idToken, request, ct);
        return result.Match<IActionResult>(
            onSuccess: _ => NoContent(),
            onFailure: err => BadRequest(new { error = err }));
    }
}
