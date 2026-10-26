using Factum.Agent.Common;
using Factum.Agent.Services;
using Factum.Agent.Services.Extraction;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Agent.Controllers;

/// <summary>Navegador de destinos de la extracción (extraccion-logica-android §8.2.3). Local, sin auth.</summary>
[ApiController]
[Produces("application/json")]
public sealed class ExtractionDestinationsController(DestinationService destinations,
    ILogger<ExtractionDestinationsController> log) : ControllerBase
{
    public sealed record CheckBody(string? Path = null, long RequiredBytes = 0);

    [HttpGet("/extraction/destinations")]
    public IActionResult Destinations() => Guard(() => Ok(destinations.GetDestinations()));

    [HttpGet("/extraction/destinations/browse")]
    public IActionResult Browse([FromQuery] string? path) => Guard(() => Ok(destinations.Browse(path ?? "")));

    [HttpPost("/extraction/destinations/check")]
    public IActionResult Check([FromBody] CheckBody body) =>
        Guard(() => Ok(destinations.Check(body?.Path ?? "", body?.RequiredBytes ?? 0)));

    private IActionResult Guard(Func<IActionResult> action)
    {
        try { return action(); }
        catch (AgentHttpException ex) { return StatusCode(ex.Status, ex.ToBody()); }
        catch (Exception ex)
        {
            log.LogError(ex, "Error en una ruta de destinos");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
