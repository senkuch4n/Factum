using Factum.Agent.Services;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Agent.Controllers;

// Explorador de archivos del dispositivo (Android) — navegar el almacenamiento, buscar
// contenido de WhatsApp/Instagram/Facebook, y traer archivos puntuales a la evidencia.
[ApiController]
[Route("devices/{serial}/explorer")]
public sealed class DeviceFilesController(IAdbService adb, IFileStorageService storage) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(string serial, [FromQuery] string path, CancellationToken ct)
    {
        try
        {
            var entries = await adb.ListDirectoryAsync(serial, path, ct);
            return Ok(new { path, entries });
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { error = ex.Message }); }
        catch (DirectoryNotFoundException ex)  { return NotFound(new { error = ex.Message }); }
        catch (Exception ex)                   { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(string serial, [FromQuery] string app, CancellationToken ct)
    {
        try
        {
            var entries = await adb.FindByAppAsync(serial, app, ct);
            return Ok(new { app, entries });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpPost("pull")]
    public async Task<IActionResult> Pull(string serial, [FromBody] PullFilesRequest request, CancellationToken ct)
    {
        var results = new List<object>();
        foreach (var devicePath in request.Paths)
        {
            try
            {
                var originalName = devicePath[(devicePath.LastIndexOf('/') + 1)..];
                var ext = Path.GetExtension(originalName).TrimStart('.');
                if (string.IsNullOrEmpty(ext)) ext = "bin";

                var path = storage.NewFilePath("device_pull", ext);
                await adb.PullFileAsync(serial, devicePath, path, ct);

                var filename = Path.GetFileName(path);
                results.Add(new { originalName, filename, url = $"/files/{filename}", ok = true });
            }
            catch (Exception ex)
            {
                results.Add(new { originalName = devicePath, ok = false, error = ex.Message });
            }
        }
        return Ok(new { files = results });
    }
}

public sealed record PullFilesRequest(List<string> Paths);
