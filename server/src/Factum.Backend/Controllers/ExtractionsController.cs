using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Extractions;
using Factum.Backend.Services.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Backend.Controllers;

/// <summary>
/// Registro de extracciones lógicas (extraccion-logica-android §8.3–8.5). Solo el dueño: una extracción ajena o
/// inexistente responde <c>404</c>, como el resto. Errores con <c>{ error, code }</c> (ResultHttpExtensions).
/// Nunca se guarda contenido extraído: solo metadatos y el hash del manifiesto.
/// </summary>
[ApiController]
[Route("api/extractions")]
[Authorize]
[Produces("application/json")]
public sealed class ExtractionsController(IExtractionService service, ILogger<ExtractionsController> logger)
    : ControllerBase
{
    private User Officer => (User)HttpContext.Items["User"]!;

    /// <summary>§8.5: las del usuario; con <c>case_id</c> (caso propio) las de ese caso; con <c>unlinked=true</c> las
    /// terminales sin caso.</summary>
    [HttpGet]
    [ProducesResponseType<ExtractionListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery(Name = "case_id")] string? caseId,
        [FromQuery] bool unlinked, CancellationToken ct)
    {
        var result = await service.ListAsync(Officer, caseId, unlinked, ct);
        return result.IsSuccess ? Ok(new ExtractionListResponse(result.Value!)) : this.ErrorResult(result);
    }

    [HttpGet("{id}")]
    [ProducesResponseType<ExtractionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var result = await service.GetAsync(id, Officer, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
    }

    /// <summary>§8.3: upsert idempotente con el <c>record</c> que arma Tatana (más <c>pc_server_probe</c>).</summary>
    [HttpPut("{id}")]
    [ProducesResponseType<ExtractionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Upsert(string id, [FromBody] UpsertExtractionRequest req, CancellationToken ct)
    {
        var result = await service.UpsertAsync(id, req ?? new UpsertExtractionRequest(), Officer, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
    }

    /// <summary>§8.5: asocia la extracción a un caso propio y terminal.</summary>
    [HttpPut("{id}/case")]
    [ProducesResponseType<ExtractionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Link(string id, [FromBody] LinkExtractionRequest req, CancellationToken ct)
    {
        var result = await service.LinkAsync(id, req?.CaseId, Officer, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
    }

    /// <summary>§8.5: desasocia (idempotente si no tenía caso).</summary>
    [HttpDelete("{id}/case")]
    [ProducesResponseType<ExtractionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unlink(string id, CancellationToken ct)
    {
        var result = await service.UnlinkAsync(id, Officer, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
    }

    /// <summary>
    /// informe-extraccion §7.4/§8.2: genera el DOCX del informe pericial de la extracción y lo devuelve como descarga
    /// (<c>Content-Disposition: attachment</c>), con <c>X-Report-Sha256</c> informativo. 404 si no existe o es de otro
    /// dueño; 409 <c>extraction_not_finished</c> si no es terminal; 500 si la generación falla (sin filtrar detalles).
    /// </summary>
    [HttpGet("{id}/report")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Report(string id, CancellationToken ct)
    {
        Result<ExtractionReportResult> result;
        try
        {
            result = await service.GenerateReportAsync(id, Officer, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo generar el informe de la extracción {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "No se pudo generar el informe." });
        }
        if (!result.IsSuccess) return this.ErrorResult(result);

        var report = result.Value!;
        Response.Headers["X-Report-Sha256"] = report.Sha256;
        return File(report.Docx,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", report.Filename);
    }

    /// <summary>§8.5: el <c>.tsr</c> (si el sello está granted) y el LEEME, para que Tatana los copie a <c>sellos/</c>.</summary>
    [HttpGet("{id}/timestamp/files")]
    [ProducesResponseType<ExtractionTimestampFilesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TimestampFiles(string id, CancellationToken ct)
    {
        var result = await service.TimestampFilesAsync(id, Officer, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
    }
}
