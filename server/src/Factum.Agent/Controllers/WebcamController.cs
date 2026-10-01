using Factum.Agent.Services;
using Factum.Agent.WebSockets;
using Factum.Agent.Models;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Agent.Controllers;

[ApiController]
[Route("webcam")]
public sealed class WebcamController(
    IWebcamService webcam,
    IFileStorageService storage,
    AgentWebSocketHub hub) : ControllerBase
{
    [HttpPost("capture")]
    public async Task<IActionResult> Capture([FromBody] CaptureRequest request, CancellationToken ct)
    {
        var path = storage.NewFilePath($"foto_{request.Type}", "jpg");
        try
        {
            await webcam.CaptureAsync(path, ct);
            var filename = Path.GetFileName(path);
            await hub.BroadcastAsync(new AgentEvent
            {
                Type = "photo_taken",
                Data = new { type = request.Type, filename, url = $"/files/{filename}" }
            }, ct);
            return Ok(new { filename, url = $"/files/{filename}" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public sealed record CaptureRequest(string Type);
