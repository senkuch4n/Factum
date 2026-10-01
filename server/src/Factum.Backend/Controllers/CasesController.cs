using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Backend.Controllers;

[ApiController]
[Route("api/cases")]
[Authorize]
[Produces("application/json")]
public sealed class CasesController(ICaseService caseService) : ControllerBase
{
    private User Officer => (User)HttpContext.Items["User"]!;

    [HttpGet]
    [ProducesResponseType<List<Case>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var cases = await caseService.ListAsync(Officer.Dni, ct);
        return Ok(new { cases });
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var result = await caseService.GetAsync(id, Officer.Dni, ct);
        return result.Match<IActionResult>(
            onSuccess: t => Ok(new { cas = t.Case, files = t.Files }),
            onFailure: err => err.Contains("no encontrado") ? NotFound(new { error = err })
                : Forbid());
    }

    [HttpPost]
    [ProducesResponseType<Case>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateCaseRequest request, CancellationToken ct)
    {
        var cas = await caseService.CreateAsync(request, Officer, ct);
        return CreatedAtAction(nameof(Get), new { id = cas.Id }, cas);
    }

    [HttpPost("{id}/files")]
    [ProducesResponseType<FileInfoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadFile(string id,
        [FromQuery] string? filename, [FromQuery(Name = "source_path")] string? sourcePath,
        CancellationToken ct)
    {
        var name = filename ?? $"file_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        var result = await caseService.UploadFileAsync(id, Officer.Dni, name, sourcePath, Request.Body, ct);
        return result.Match<IActionResult>(
            onSuccess: Ok,
            onFailure: err => err.Contains("no encontrado") ? NotFound(new { error = err })
                : Forbid());
    }

    [HttpGet("{id}/files")]
    [ProducesResponseType<List<FileInfoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFiles(string id, CancellationToken ct)
    {
        var result = await caseService.ListFilesAsync(id, Officer.Dni, ct);
        return result.Match<IActionResult>(
            onSuccess: files => Ok(new { files }),
            onFailure: err => err.Contains("no encontrado") ? NotFound(new { error = err })
                : Forbid());
    }

    [HttpPost("{id}/generate")]
    [ProducesResponseType<GenerateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Generate(string id, CancellationToken ct)
    {
        var result = await caseService.GenerateAsync(id, Officer.Dni, ct);
        return result.Match<IActionResult>(
            onSuccess: Ok,
            onFailure: err =>
            {
                if (err.Contains("no encontrado")) return NotFound(new { error = err });
                if (err.Contains("denegado")) return Forbid();
                return BadRequest(new { error = err });
            });
    }

    [HttpGet("{id}/download/{filename}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(string id, string filename, CancellationToken ct)
    {
        var result = await caseService.DownloadAsync(id, filename, Officer.Dni, ct);
        return result.Match<IActionResult>(
            onSuccess: t => PhysicalFile(t.Path, t.ContentType, t.FileName),
            onFailure: err => err.Contains("no encontrado") ? NotFound(new { error = err })
                : Forbid());
    }
}
