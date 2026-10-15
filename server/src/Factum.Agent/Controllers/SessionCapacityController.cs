using Factum.Agent.Services.Session;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Agent.Controllers;

/// <summary><c>GET /session/capacity?camera=false</c> (§7.2.1): espacio y horas estimadas de grabación en esta PC.</summary>
[ApiController]
[Route("session")]
public sealed class SessionCapacityController(SessionRecorder recorder) : ControllerBase
{
    [HttpGet("capacity")]
    public IActionResult Capacity([FromQuery] bool? camera, [FromQuery(Name = "segment_minutes")] int? segmentMinutes)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(recorder.Capacity(camera == true, segmentMinutes));
    }
}
