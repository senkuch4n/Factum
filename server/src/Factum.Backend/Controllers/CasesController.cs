using System.Globalization;
using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;
using Factum.Backend.Services.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Controllers;

// Errores: { error } y, en validación de obligatorios, { error, missing } (claves de la SDD
// §6.4). El código HTTP sale del ErrorKind del Result (ResultHttpExtensions), no del texto.
[ApiController]
[Route("api/cases")]
[Authorize]
[Produces("application/json")]
public sealed class CasesController(ICaseService caseService, IOptions<StorageOptions> storage,
    IOptions<ReportOptions> reportOptions) : ControllerBase
{
    private User Officer => (User)HttpContext.Items["User"]!;

    [HttpGet]
    [ProducesResponseType<List<Case>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var cases = await caseService.ListAsync(Officer.Dni, ct);
        return Ok(new { cases });
    }

    // dashboard-kpis-tendencias: KPIs + tendencia mensual del perito. El segmento literal "stats"
    // matchea antes que el parámetro {id} de GET /api/cases/{id}. Default 0 del binding cubre la
    // ausencia del query param.
    [HttpGet("stats")]
    [ProducesResponseType<CaseStatsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Stats([FromQuery(Name = "tz_offset_minutes")] int tzOffsetMinutes,
        CancellationToken ct)
        => Ok(await caseService.GetStatsAsync(Officer.Dni, tzOffsetMinutes, ct));

    // dashboard-breakdown: distribución por dimensión. Literal "breakdown" (gana sobre {id}).
    [HttpGet("breakdown")]
    [ProducesResponseType<BreakdownResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Breakdown([FromQuery] string? dimension, [FromQuery] string? from,
        [FromQuery] string? to, CancellationToken ct)
    {
        if (!TryParseDate(from, out var fromDate)) return this.ErrorResult(
            Result.Invalid<BreakdownResponse>("from tiene que ser una fecha yyyy-MM-dd"));
        if (!TryParseDate(to, out var toDate)) return this.ErrorResult(
            Result.Invalid<BreakdownResponse>("to tiene que ser una fecha yyyy-MM-dd"));
        var result = await caseService.BreakdownAsync(Officer.Dni, dimension, fromDate, toDate, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
    }

    // yyyy-MM-dd en UTC; null (ausente) es válido. Devuelve false solo si vino y no parsea.
    private static bool TryParseDate(string? raw, out DateOnly? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var parsed))
            return false;
        value = parsed;
        return true;
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

    // trazabilidad-caso: línea de tiempo del caso (dueño o superadmin). Ascendente por timestamp.
    [HttpGet("{id}/events")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Events(string id, CancellationToken ct)
    {
        var result = await caseService.ListEventsAsync(id, Officer, HttpContext.GetSession().Role, ct);
        return result.IsSuccess ? Ok(new { events = result.Value }) : this.ErrorResult(result);
    }

    // versionado-informe: historial de versiones de report_texts (solo lectura). Más reciente primero.
    [HttpGet("{id}/report-text-versions")]
    [ProducesResponseType<ReportTextVersionsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReportTextVersions(string id, CancellationToken ct)
    {
        var result = await caseService.GetReportTextVersionsAsync(id, Officer.Dni, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
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

    // subida-archivos-grandes §4.2: cuerpo crudo con Content-Length obligatorio, hasta
    // Storage:MaxUploadBytes (solo acá; el resto sigue con el tope de Kestrel). Errores
    // { error, code, ... } (§7); los rechazos salen sin leer el cuerpo.
    [HttpPost("{id}/files")]
    [DisableFormValueModelBinding]
    [ProducesResponseType<FileInfoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status507InsufficientStorage)]
    public async Task<IActionResult> UploadFile(string id,
        [FromQuery] string? filename, [FromQuery(Name = "source_path")] string? sourcePath,
        CancellationToken ct)
    {
        // Antes de tocar el cuerpo: la red de Kestrel pasa a ser el tope configurado.
        var bodySize = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false }) bodySize.MaxRequestBodySize = storage.Value.MaxUploadBytes;

        var name = filename ?? $"file_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        var result = await caseService.UploadFileAsync(id, Officer.Dni, name, sourcePath,
            Request.ContentLength, Request.Body, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
    }

    // subida-archivos-grandes §4.1: mismos chequeos que la subida, sin cuerpo. `size` se parsea a
    // mano para que un valor no numérico dé nuestro length_required y no el ProblemDetails de MVC.
    [HttpGet("{id}/files/upload-check")]
    [ProducesResponseType<UploadCheckResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status507InsufficientStorage)]
    public async Task<IActionResult> UploadCheck(string id, [FromQuery] string? filename,
        [FromQuery(Name = "size")] string? size, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        long? parsed = long.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
        var result = await caseService.CheckUploadAsync(id, Officer.Dni, filename, parsed, ct);
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

    // ── zip-local-informe-servidor (§5.2, §5.3, §5.5, §5.6) ─────────────────

    // Registra archivos ya guardados en la carpeta del caso de Tatana (manifiesto, sin bytes).
    [HttpPut("{id}/evidence")]
    [ProducesResponseType<EvidenceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterEvidence(string id, [FromBody] RegisterEvidenceRequest request,
        CancellationToken ct)
    {
        var result = await caseService.RegisterEvidenceAsync(id, request, Officer.Dni, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
    }

    [HttpDelete("{id}/evidence/{filename}")]
    [ProducesResponseType<EvidenceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteEvidence(string id, string filename, CancellationToken ct)
    {
        var result = await caseService.DeleteEvidenceAsync(id, filename, Officer.Dni, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
    }

    // Etapa 1 de 3: valida contra el manifiesto y abre el intento (devuelve la contraseña del ZIP
    // que arma Tatana). No cambia el Status.
    [HttpPost("{id}/generate/prepare")]
    [ProducesResponseType<PrepareGenerationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PrepareGeneration(string id, [FromBody] PrepareGenerationRequest request,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await caseService.PrepareGenerationAsync(id, request, Officer.Dni, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result);
    }

    // Etapa 3 de 3: multipart (metadata + capturas del informe) leído en streaming, sin IFormFile
    // (D-T6). El tope del cuerpo se fija antes de leer nada.
    [HttpPost("{id}/generate/finish")]
    [DisableFormValueModelBinding]
    [ProducesResponseType<GenerateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> FinishGeneration(string id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var max = reportOptions.Value.MaxGenerateUploadBytes;
        var bodySize = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false }) bodySize.MaxRequestBodySize = max;
        if (Request.ContentLength is { } length && length > max)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new Dictionary<string, object?>
            {
                ["error"] = EvidenceManifest.RequestTooLargeMessage(max),
                ["code"] = EvidenceErrorCodes.RequestTooLarge,
                ["max_bytes"] = max,
            });

        var result = await caseService.FinishGenerationAsync(id, Officer.Dni, Request.ContentType, Request.Body, ct);
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

    // editor-imagenes-informe §4.4: capturas insertables en las secciones del informe.
    [HttpGet("{id}/report-images")]
    [ProducesResponseType<ReportImagesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReportImages(string id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await caseService.ListReportImagesAsync(id, Officer.Dni, ct);
        return result.IsSuccess ? Ok(new ReportImagesResponse(result.Value!)) : this.ErrorResult(result);
    }

    // editor-imagenes-informe §4.5: vista previa de una captura disponible, por streaming, con el
    // tipo detectado por contenido. Solo lectura, sin auditoría.
    [HttpGet("{id}/files/{filename}/preview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReportImagePreview(string id, string filename, CancellationToken ct)
    {
        var result = await caseService.GetReportImagePreviewAsync(id, filename, Officer.Dni, ct);
        if (!result.IsSuccess) return this.ErrorResult(result);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        Response.Headers.ContentDisposition = "inline";
        // FileStreamResult: streaming y cierra el stream al terminar. Sin fileDownloadName (sería attachment).
        return File(result.Value.Content, result.Value.ContentType);
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
