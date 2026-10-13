using Factum.Agent.Common;
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
            var attr = EventAttribution.From(request.CaseId, request.OriginId);
            await hub.BroadcastAsync(new AgentEvent
            {
                Type = "photo_taken",
                Data = new
                {
                    type = request.Type, filename, url = $"/files/{filename}",
                    case_id = attr.CaseId, origin_id = attr.OriginId,
                },
            }, ct);
            return Ok(new { filename, url = $"/files/{filename}" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

/// <summary>Body de <c>POST /webcam/capture</c>. <c>case_id</c>/<c>origin_id</c> = atribución de <c>photo_taken</c>
/// (wizard-concurrencia-pestanas §5.1). La webcam no es del celular: el evento no lleva <c>serial</c>.</summary>
public sealed record CaptureRequest(string Type, string? CaseId = null, string? OriginId = null);
