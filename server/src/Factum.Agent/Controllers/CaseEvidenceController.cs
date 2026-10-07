using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Controllers;

public sealed record ImportEvidenceRequest(string? Filename = null);

/// <summary>
/// Carpeta de trabajo del caso (zip-local-informe-servidor §6.2). Errores <c>{ "error", "code", … }</c>
/// (<see cref="AgentErrorCodes"/>). La raíz plana y <c>/files</c> no cambian (flujo viejo).
/// </summary>
[ApiController]
[Route("cases/{caseId}")]
public sealed class CaseEvidenceController(ICaseEvidenceStore store, IOptions<AgentOptions> opts) : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private IActionResult Error(AgentHttpException ex) => StatusCode(ex.Status, ex.ToBody());

    [HttpPost("evidence/import")]
    public async Task<IActionResult> Import(string caseId, [FromBody] ImportEvidenceRequest? request,
        CancellationToken ct)
    {
        try { return Ok(await store.ImportAsync(caseId, request?.Filename, ct)); }
        catch (AgentHttpException ex) { return Error(ex); }
    }

    [HttpGet("evidence/upload-check")]
    public IActionResult UploadCheck(string caseId, [FromQuery] string? filename, [FromQuery] string? size)
    {
        Response.Headers.CacheControl = "no-store";
        long? parsed = long.TryParse(size, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
        try { return Ok(new { max_upload_bytes = store.CheckUpload(caseId, filename, parsed) }); }
        catch (AgentHttpException ex) { return Error(ex); }
    }

    // Cuerpo crudo con Content-Length obligatorio; el tope de Kestrel pasa a ser Agent:MaxUploadBytes
    // antes de leer nada.
    [HttpPost("evidence/upload")]
    public async Task<IActionResult> Upload(string caseId, [FromQuery] string? filename, CancellationToken ct)
    {
        var bodySize = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false }) bodySize.MaxRequestBodySize = opts.Value.MaxUploadBytes;
        try
        {
            return Ok(await store.StageAndCommitUploadAsync(caseId, filename, Request.ContentLength, Request.Body, ct));
        }
        catch (AgentHttpException ex) { return Error(ex); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(400, new { error = "La copia se cortó; no se guardó nada", code = AgentErrorCodes.IncompleteUpload });
        }
    }

    [HttpGet("files")]
    public IActionResult List(string caseId)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(store.List(caseId)); }
        catch (AgentHttpException ex) { return Error(ex); }
    }

    [HttpGet("files/{filename}")]
    public IActionResult Get(string caseId, string filename)
    {
        try
        {
            var path = store.TryGetFile(caseId, filename);
            if (path is null)
                return Error(new AgentHttpException(404, AgentErrorCodes.FileNotFound, $"El archivo {filename} no existe"));
            if (!ContentTypes.TryGetContentType(filename, out var contentType))
                contentType = "application/octet-stream";
            Response.Headers.XContentTypeOptions = "nosniff";
            Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            Response.Headers.CacheControl = "no-store";
            return PhysicalFile(path, contentType, enableRangeProcessing: true);
        }
        catch (AgentHttpException ex) { return Error(ex); }
    }

    [HttpDelete("files/{filename}")]
    public IActionResult Delete(string caseId, string filename)
    {
        try
        {
            return store.Delete(caseId, filename)
                ? Ok(new { deleted = filename })
                : Error(new AgentHttpException(404, AgentErrorCodes.FileNotFound, $"El archivo {filename} no existe"));
        }
        catch (AgentHttpException ex) { return Error(ex); }
    }
}
