using Factum.Agent.Common;
using Factum.Agent.Services;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Agent.Controllers;

/// <summary>
/// Estado del post-proceso de grabaciones iOS (ios-grabacion-larga §4.4/§5.7). El cliente lo consulta al
/// (re)conectar el WebSocket para no dejar un indicador girando. GET: no cuenta como operación ni se
/// bloquea en mantenimiento.
/// </summary>
[ApiController]
[Route("video-postprocess")]
public sealed class VideoPostprocessController(IVideoPostprocessService post) : ControllerBase
{
    public const int MaxFilenames = 50;

    [HttpGet("status")]
    public IActionResult Status([FromQuery(Name = "filename")] string[]? filenames)
    {
        if (!AreValid(filenames))
            return BadRequest(new
            {
                error = $"Pedí entre 1 y {MaxFilenames} archivos con ?filename=<nombre> (solo el nombre, sin carpetas)",
                code = AgentErrorCodes.InvalidFilename,
            });

        var items = filenames!.Select(f =>
        {
            var variants = post.GetStatus(f, out var known);
            return new VideoPostprocessStatusItem(f, known, variants);
        }).ToList();
        Response.Headers.CacheControl = "no-store";
        return Ok(new VideoPostprocessStatusResponse(items));
    }

    internal static bool AreValid(string[]? filenames) =>
        filenames is { Length: > 0 and <= MaxFilenames } &&
        filenames.All(f => !string.IsNullOrWhiteSpace(f) && Path.GetFileName(f) == f &&
                           f.IndexOfAny(['/', '\\']) < 0 && f is not ("." or ".."));
}

public sealed record VideoPostprocessStatusItem(string OriginalFilename, bool Known,
    IReadOnlyList<VideoVariantStatusDto> Variants);

public sealed record VideoPostprocessStatusResponse(IReadOnlyList<VideoPostprocessStatusItem> Items);
