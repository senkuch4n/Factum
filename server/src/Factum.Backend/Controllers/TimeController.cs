using Factum.Backend.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Backend.Controllers;

/// <summary>
/// metadatos-dispositivo §5.2/§7.1: hora del servidor (UTC) para el sondeo NTP de una vuelta que hace el navegador
/// antes de mandar la ficha o la medición de cierre. La diferencia PC↔servidor la calcula la API al recibir el
/// sondeo (<c>DeviceDetailsRules.ComputePcServer</c>).
/// </summary>
[ApiController]
[Route("api/time")]
[Authorize]
[Produces("application/json")]
public sealed class TimeController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ServerTimeResponse>(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new ServerTimeResponse(DateTime.UtcNow));
}
