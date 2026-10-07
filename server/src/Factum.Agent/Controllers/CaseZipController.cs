using Factum.Agent.Common;
using Factum.Agent.Services;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Agent.Controllers;

/// <summary>ZIP del caso en esta PC (zip-local-informe-servidor §6.3).</summary>
[ApiController]
[Route("cases/{caseId}/zip")]
public sealed class CaseZipController(ICaseZipService zips, IFolderReveal reveal) : ControllerBase
{
    private IActionResult Error(AgentHttpException ex) => StatusCode(ex.Status, ex.ToBody());

    [HttpPost]
    public async Task<IActionResult> Build(string caseId, [FromBody] BuildZipRequest request, CancellationToken ct)
    {
        try { return Ok(await zips.BuildAsync(caseId, request, ct)); }
        catch (AgentHttpException ex) { return Error(ex); }
    }

    [HttpPost("commit")]
    public async Task<IActionResult> Commit(string caseId, [FromBody] CommitZipRequest request, CancellationToken ct)
    {
        try { return Ok(await zips.CommitAsync(caseId, request, ct)); }
        catch (AgentHttpException ex) { return Error(ex); }
    }

    [HttpDelete("pending")]
    public IActionResult DiscardPending(string caseId, [FromQuery(Name = "case_ref")] string? caseRef,
        [FromQuery(Name = "zip_filename")] string? zipFilename)
    {
        try
        {
            zips.DiscardPending(caseId, caseRef, zipFilename);
            return NoContent();
        }
        catch (AgentHttpException ex) { return Error(ex); }
    }

    [HttpGet("status")]
    public IActionResult Status(string caseId, [FromQuery(Name = "case_ref")] string? caseRef,
        [FromQuery(Name = "zip_filename")] string? zipFilename)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(zips.Status(caseId, caseRef, zipFilename)); }
        catch (AgentHttpException ex) { return Error(ex); }
    }

    [HttpPost("reveal")]
    public IActionResult Reveal(string caseId, [FromQuery(Name = "case_ref")] string? caseRef,
        [FromQuery(Name = "zip_filename")] string? zipFilename)
    {
        try
        {
            var path = zips.FinalZipPath(caseId, caseRef, zipFilename)
                ?? throw new AgentHttpException(404, AgentErrorCodes.ZipNotFound, "El ZIP no está en esta PC");
            reveal.Reveal(path);
            return NoContent();
        }
        catch (AgentHttpException ex) { return Error(ex); }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"No se pudo abrir la carpeta: {ex.Message}", code = AgentErrorCodes.StorageError });
        }
    }

    [HttpGet("file")]
    public IActionResult DownloadZip(string caseId, [FromQuery(Name = "case_ref")] string? caseRef,
        [FromQuery(Name = "zip_filename")] string? zipFilename)
    {
        try
        {
            var path = zips.FinalZipPath(caseId, caseRef, zipFilename)
                ?? throw new AgentHttpException(404, AgentErrorCodes.ZipNotFound, "El ZIP no está en esta PC");
            Response.Headers.CacheControl = "no-store";
            return PhysicalFile(path, "application/zip", System.IO.Path.GetFileName(path), enableRangeProcessing: true);
        }
        catch (AgentHttpException ex) { return Error(ex); }
    }
}
