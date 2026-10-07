using Factum.Agent.Services;
using Factum.Agent.Services.Ios;
using Factum.Agent.WebSockets;
using Factum.Agent.Models;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Agent.Controllers;

[ApiController]
[Route("devices/{serial}/record")]
public sealed class RecordingController(
    IAdbService adb,
    IIosService ios,
    IFileStorageService storage,
    AgentWebSocketHub hub) : ControllerBase
{
    [HttpPost("start")]
    public async Task<IActionResult> Start(string serial,
        [FromBody] StartRecordingRequest request, CancellationToken ct)
    {
        try
        {
            if (request.Platform == "ios")
            {
                var path = storage.NewFilePath("grabacion_ios", "mp4");
                await ios.StartRecordingAsync(serial, request.IosMode ?? "video_only", path, ct);
                await hub.BroadcastAsync(new AgentEvent
                {
                    Type = "recording_started",
                    Data = new { serial, platform = "ios" }
                }, ct);
                return Ok(new { state = "recording" });
            }
            else
            {
                var ext = "mkv"; // scrcpy graba MKV: válido incluso tras SIGKILL (índice incremental)
                var path = storage.NewFilePath("grabacion", ext);
                await adb.StartRecordingAsync(serial, path, request.AndroidVersion, request.AndroidWithMic, ct);
                await hub.BroadcastAsync(new AgentEvent
                {
                    Type = "recording_started",
                    Data = new { serial, platform = "android" }
                }, ct);
                var filename = Path.GetFileName(path);
                return Ok(new { filename, state = "recording" });
            }
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

    [HttpPost("stop")]
    public async Task<IActionResult> Stop(string serial,
        [FromQuery] string? platform, CancellationToken ct)
    {
        try
        {
            string outputPath;
            if (platform == "ios")
                outputPath = await ios.StopRecordingAsync(serial, ct);
            else
                outputPath = await adb.StopRecordingAsync(serial, ct);

            var filename = Path.GetFileName(outputPath);
            await hub.BroadcastAsync(new AgentEvent
            {
                Type = "recording_stopped",
                Data = new { filename, url = $"/files/{filename}", platform = platform ?? "android" }
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

public sealed record StartRecordingRequest(
    int AndroidVersion,
    string Platform = "android",
    string? IosMode = null,
    bool AndroidWithMic = false);
