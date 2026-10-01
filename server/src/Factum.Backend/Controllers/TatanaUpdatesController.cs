using Factum.Backend.Services.Updates;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Backend.Controllers;

// Sin [Authorize]: esto sirve binarios de actualización de Tatana — la PC del
// fiscal (instalador Electron con electron-updater, o el .bat portátil) lo
// consulta sin JWT de usuario, es un chequeo de versión de la máquina, no una
// acción del fiscal. Nunca sale a GitLab directo desde la PC del fiscal: todo
// pasa por este backend, que sí tiene salida a internet.
[ApiController]
[Route("tatana/updates")]
public sealed class TatanaUpdatesController(ITatanaUpdatesService updates) : ControllerBase
{
    // electron-updater (provider "generic") pide esto primero para saber si hay
    // versión nueva y con qué archivo actualizar.
    [HttpGet("latest.yml")]
    public async Task<IActionResult> LatestYml(CancellationToken ct) =>
        await ProxyOrNotFound(await updates.ProxyInstallerFileAsync("latest.yml", ct));

    // Manifest simple para el modo portátil (update-portable.ps1 lo consume).
    [HttpGet("latest-portable.json")]
    public async Task<IActionResult> LatestPortable(CancellationToken ct)
    {
        var tag = await updates.GetLatestTagAsync(ct);
        if (tag is null) return NotFound(new { error = "Sin releases disponibles" });
        return Ok(new
        {
            version = tag.TrimStart('v'),
            url = $"{updates.PublicBaseUrl.TrimEnd('/')}/tatana/updates/portable.zip",
        });
    }

    [HttpGet("portable.zip")]
    public async Task<IActionResult> PortableZip(CancellationToken ct) =>
        await ProxyOrNotFound(await updates.ProxyPortableZipAsync(ct));

    // Cualquier otro archivo que electron-updater pida (el .exe / .blockmap
    // referenciados dentro de latest.yml).
    [HttpGet("{filename}")]
    public async Task<IActionResult> InstallerFile(string filename, CancellationToken ct) =>
        await ProxyOrNotFound(await updates.ProxyInstallerFileAsync(filename, ct));

    private static async Task<IActionResult> ProxyOrNotFound(HttpResponseMessage? upstream)
    {
        if (upstream is null) return new NotFoundResult();
        using (upstream)
        {
            var bytes = await upstream.Content.ReadAsByteArrayAsync();
            var contentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            return new FileContentResult(bytes, contentType);
        }
    }
}
