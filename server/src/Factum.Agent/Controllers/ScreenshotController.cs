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
    [HttpPost]
    public async Task<IActionResult> Take(string serial,
        [FromQuery] string? platform, [FromQuery] string? method, CancellationToken ct)
    {
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
                Data = new { filename, url = $"/files/{filename}" }
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
