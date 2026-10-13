using Factum.Agent.Services;
using Factum.Agent.Services.Ios;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Agent.Controllers;

/// <summary>
/// Vista previa del paso 2 (wizard-concurrencia-pestanas §5.1, DT5). Toma la captura igual que
/// <see cref="ScreenshotController"/> pero la guarda en <c>previews/</c> (una por celular) y <b>no emite ningún
/// evento WS</b>: una vista previa nunca es evidencia ni aparece en otra pestaña.
/// </summary>
[ApiController]
public sealed class PreviewController(
    IAdbService adb,
    IIosService ios,
    IFileStorageService storage) : ControllerBase
{
    [HttpPost("devices/{serial}/preview")]
    public async Task<IActionResult> Take(string serial,
        [FromQuery] string? platform, [FromQuery] string? method, CancellationToken ct)
    {
        var gate = storage.PreviewGate(serial);
        await gate.WaitAsync(ct);
        try
        {
            var path = storage.NewPreviewPath(serial);

            if (platform == "ios")
                await ios.TakeScreenshotAsync(serial, path, method ?? "auto", ct);
            else
                await adb.TakeScreenshotAsync(serial, path, ct);

            storage.PrunePreviews(serial, path);
            var filename = Path.GetFileName(path);
            return Ok(new { filename, url = $"/previews/{filename}" });
        }
        catch (IosException ex)
        {
            return StatusCode(500, new { error = ex.Message, code = ex.Code });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
        finally
        {
            gate.Release();
        }
    }

    [HttpGet("previews/{filename}")]
    public IActionResult Get(string filename)
    {
        var path = storage.ResolvePreview(filename);
        if (path is null) return NotFound(new { error = "Archivo no encontrado" });
        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(path, "image/png");
    }
}
