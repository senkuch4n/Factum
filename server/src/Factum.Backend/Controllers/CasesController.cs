using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Backend.Controllers;

// Errores: { error } y, en validación de obligatorios, { error, missing } (claves de la SDD
// §6.4). El código HTTP sale del ErrorKind del Result (ResultHttpExtensions), no del texto.
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
        return result.IsSuccess
            ? Ok(new { cas = result.Value.Case, files = result.Value.Files })
            : this.ErrorResult(result);
    }

    [HttpPost]
    [ProducesResponseType<Case>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateCaseRequest request, CancellationToken ct)
    {
        var result = await caseService.CreateAsync(request, Officer, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value)
            : this.ErrorResult(result);
    }

    [HttpPut("{id}")]
    [ProducesResponseType<Case>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateCaseRequest request,
        CancellationToken ct)
    {
        var result = await caseService.UpdateAsync(id, request, Officer, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
    }

    [HttpGet("{id}/report-texts/defaults")]
    [ProducesResponseType<ReportTextsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReportTextDefaults(string id, CancellationToken ct)
    {
        var result = await caseService.GetReportTextDefaultsAsync(id, Officer.Dni, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
    }

    [HttpPut("{id}/report-texts")]
    [ProducesResponseType<ReportTexts>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveReportTexts(string id, [FromBody] ReportTextsDto request,
        CancellationToken ct)
    {
        var result = await caseService.SaveReportTextsAsync(id, request, Officer.Dni, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
    }

    [HttpPut("{id}/capture-roles")]
    [ProducesResponseType<CaptureRolesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveCaptureRoles(string id, [FromBody] CaptureRolesRequest request,
        CancellationToken ct)
    {
        var result = await caseService.UpsertCaptureRolesAsync(id, request, Officer.Dni, ct);
        return result.IsSuccess ? Ok(new CaptureRolesResponse(result.Value!)) : this.ErrorResult(result);
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
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
    }

    [HttpGet("{id}/files")]
    [ProducesResponseType<List<FileInfoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFiles(string id, CancellationToken ct)
    {
        var result = await caseService.ListFilesAsync(id, Officer.Dni, ct);
        return result.IsSuccess ? Ok(new { files = result.Value }) : this.ErrorResult(result);
    }

    [HttpPost("{id}/generate")]
    [ProducesResponseType<GenerateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Generate(string id, CancellationToken ct)
    {
        var result = await caseService.GenerateAsync(id, Officer.Dni, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
    }

    // La contraseña sale solo de acá (y una vez en generate). No se cachea en ningún lado.
    [HttpGet("{id}/zip-password")]
    [ProducesResponseType<ZipPasswordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ZipPassword(string id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await caseService.GetZipPasswordAsync(id, Officer.Dni, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
    }

    [HttpGet("{id}/download/{filename}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(string id, string filename, CancellationToken ct)
    {
        var result = await caseService.DownloadAsync(id, filename, Officer.Dni, ct);
        if (!result.IsSuccess) return this.ErrorResult(result);
        var (path, contentType, fileName) = result.Value;
        return PhysicalFile(path, contentType, fileName);
    }
}
