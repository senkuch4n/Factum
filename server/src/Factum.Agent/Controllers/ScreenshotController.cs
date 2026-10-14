using Factum.Agent.Common;
using Factum.Agent.Services;
using Factum.Agent.Services.Ios;
using Factum.Agent.Services.Session;
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
    AgentWebSocketHub hub,
    IAndroidScreenProbe screenProbe,
    SessionRecorder sessions,
    TimeProvider time) : ControllerBase
{
    // wizard-concurrencia-pestanas §5.1: case_id/origin_id opcionales (solo atribución del evento; inválido → null).
    // pulido-post-qa-final D9: en Android, antes de crear el archivo se chequea la pantalla; apagada o bloqueada →
    // 409 screen_off/screen_locked sin archivo ni screenshot_taken. skip_screen_check=true ("Capturar igual") lo saltea.
    [HttpPost]
    public async Task<IActionResult> Take(string serial,
        [FromQuery] string? platform, [FromQuery] string? method,
        [FromQuery(Name = "case_id")] string? caseId, [FromQuery(Name = "origin_id")] string? originId,
        [FromQuery(Name = "skip_screen_check")] bool? skipScreenCheck,
        CancellationToken ct)
    {
        var attr = EventAttribution.From(caseId, originId);
        try
        {
            if (platform != "ios" && skipScreenCheck != true)
            {
                var screen = await screenProbe.GetStateAsync(serial, ct);
                if (AndroidScreenProbe.ScreenBlock(screen.State, "capturar") is { } block)
                    return StatusCode(block.Status, block.ToBody());
            }

            var path = storage.NewFilePath("screenshot", "png");
            // grabacion-sesion-completa §5.9: la marca automática usa la hora de ANTES de screencap.
            var at = time.GetUtcNow().UtcDateTime;

            if (platform == "ios")
                await ios.TakeScreenshotAsync(serial, path, method ?? "auto", ct);
            else
                await adb.TakeScreenshotAsync(serial, path, ct);

            var filename = Path.GetFileName(path);
            // Solo si la sesión activa es de este celular y de este caso; si no, todo queda como antes.
            SessionMarkDto? mark = null;
            if (platform != "ios")
            {
                try { mark = await sessions.TryAddCaptureMarkAsync(serial, attr.CaseId, at, filename, attr.OriginId); }
                catch { /* la marca es accesoria: la captura ya está */ }
            }
            await hub.BroadcastAsync(new AgentEvent
            {
                Type = "screenshot_taken",
                Data = new
                {
                    filename, url = $"/files/{filename}", serial, case_id = attr.CaseId, origin_id = attr.OriginId,
                    session_mark = mark,
                },
            }, ct);

            return mark is null
                ? Ok(new { filename, url = $"/files/{filename}" })
                : Ok(new { filename, url = $"/files/{filename}", session_mark = mark });
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
