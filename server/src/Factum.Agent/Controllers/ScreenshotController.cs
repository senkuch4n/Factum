using Factum.Agent.Common;
using Factum.Agent.Services;
using Factum.Agent.Services.Ios;
using Factum.Agent.WebSockets;
using Factum.Agent.Models;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Agent.Controllers;

[ApiController]
[Route("devices/{serial}/screenshot")]
public sealed class ScreenshotController(
    IAdbService adb,
    IIosService ios,
    IFileStorageService storage,
    AgentWebSocketHub hub) : ControllerBase
{
    // wizard-concurrencia-pestanas §5.1: case_id/origin_id opcionales (solo atribución del evento; inválido → null).
    [HttpPost]
    public async Task<IActionResult> Take(string serial,
        [FromQuery] string? platform, [FromQuery] string? method,
        [FromQuery(Name = "case_id")] string? caseId, [FromQuery(Name = "origin_id")] string? originId,
        CancellationToken ct)
    {
        var attr = EventAttribution.From(caseId, originId);
        try
        {
            var path = storage.NewFilePath("screenshot", "png");

            if (platform == "ios")
                await ios.TakeScreenshotAsync(serial, path, method ?? "auto", ct);
            else
                await adb.TakeScreenshotAsync(serial, path, ct);

            var filename = Path.GetFileName(path);
            await hub.BroadcastAsync(new AgentEvent
            {
                Type = "screenshot_taken",
                Data = new
                {
                    filename, url = $"/files/{filename}", serial, case_id = attr.CaseId, origin_id = attr.OriginId,
                },
            }, ct);

            return Ok(new { filename, url = $"/files/{filename}" });
        }
        catch (IosException ex)
        {
            return StatusCode(500, new { error = ex.Message, code = ex.Code });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
