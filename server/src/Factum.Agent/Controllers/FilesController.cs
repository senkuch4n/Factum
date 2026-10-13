using Factum.Agent.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace Factum.Agent.Controllers;

[ApiController]
[Route("files")]
public sealed class FilesController(IFileStorageService storage, IVideoPostprocessService post) : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    [HttpGet]
    public IActionResult List() => Ok(new { files = storage.ListFiles() });

    [HttpGet("{filename}")]
    public IActionResult Download(string filename)
    {
        var path = Path.Combine(storage.DataDirectory, Path.GetFileName(filename));
        if (!System.IO.File.Exists(path)) return NotFound(new { error = "Archivo no encontrado" });

        if (!ContentTypeProvider.TryGetContentType(filename, out var contentType))
            contentType = "application/octet-stream";

        return PhysicalFile(path, contentType, filename);
    }

    [HttpDelete("{filename}")]
    public IActionResult Delete(string filename)
    {
        try
        {
            var deleted = storage.Delete(filename);
            return deleted ? Ok(new { deleted = filename })
                : NotFound(new { error = "Archivo no encontrado" });
        }
        finally
        {
            // ios-grabacion-larga §4.6 (D8): haya existido o no, se cancela su post-proceso y se borran sus
            // variantes. Discard nunca tira y no cambia la respuesta.
            post.Discard(filename);
        }
    }
}
