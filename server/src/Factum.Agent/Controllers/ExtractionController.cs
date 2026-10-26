using Factum.Agent.Common;
using Factum.Agent.Services;
using Factum.Agent.Models;
using Factum.Agent.Services.Extraction;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Agent.Controllers;

/// <summary>
/// Extracción lógica de Android (extraccion-logica-android §8.2). Errores con <c>{ error, code }</c>
/// (AgentHttpException.ToBody). Local, sin auth (como el resto de Tatana).
/// </summary>
[ApiController]
[Produces("application/json")]
public sealed class ExtractionController(ExtractionService service, ILogger<ExtractionController> log) : ControllerBase
{
    [HttpPost("/extractions")]
    public IActionResult Start([FromBody] ExtractionStartRequest req) =>
        Guard(() => StatusCode(202, service.Start(req ?? new ExtractionStartRequest())));

    [HttpGet("/extractions/current")]
    public IActionResult Current()
    {
        var snap = service.GetCurrent();
        return snap is null ? Ok(new { }) : Ok(new { extraction = snap });
    }

    [HttpGet("/extractions")]
    public IActionResult List() => Ok(new { extractions = service.List() });

    [HttpGet("/extractions/{id}")]
    public IActionResult Get(string id)
    {
        var snap = service.Get(id);
        return snap is null
            ? StatusCode(404, new { error = "No existe la extracción.", code = AgentErrorCodes.ExtractionNotFound })
            : Ok(snap);
    }

    [HttpGet("/extractions/{id}/errors")]
    public IActionResult Errors(string id, [FromQuery] int offset = 0, [FromQuery] int limit = 100) =>
        Guard(() =>
        {
            var (total, items) = service.GetErrors(id, offset, limit);
            return Ok(new { total, items = items.Select(i => new { device_path = i.DevicePath, category = i.Category, reason = i.Reason }) });
        });

    [HttpPost("/extractions/{id}/cancel")]
    public IActionResult Cancel(string id) => Guard(() => StatusCode(202, service.Cancel(id)));

    [HttpPost("/extractions/{id}/registered")]
    public IActionResult Registered(string id, [FromBody] ExtractionRegisteredRequest req) =>
        Guard(() =>
        {
            var sha = (req?.ManifestSha256 ?? "").Trim().ToLowerInvariant();
            service.MarkRegistered(id, sha);
            return Ok(new { registered = true });
        });

    [HttpPut("/extractions/{id}/seals")]
    public IActionResult Seals(string id, [FromBody] ExtractionSealsRequest req) =>
        Guard(() =>
        {
            var (dir, written, unchanged) = service.WriteSeals(id, req ?? new ExtractionSealsRequest());
            return Ok(new { directory = dir, written, unchanged });
        });

    [HttpPut("/extractions/{id}/report")]
    public IActionResult Report(string id, [FromBody] ExtractionReportRequest req) =>
        Guard(() =>
        {
            var (dir, filename, sha256, bytes) = service.WriteReport(id, req ?? new ExtractionReportRequest());
            return Ok(new { directory = dir, filename, sha256, bytes });
        });

    [HttpPost("/extractions/{id}/reveal")]
    public IActionResult Reveal(string id) => Guard(() => { service.Reveal(id); return NoContent(); });

    [HttpGet("/devices/{serial}/extraction/preflight")]
    public IActionResult Preflight(string serial) => Guard(() => Ok(service.Preflight(serial)));

    [HttpPost("/devices/{serial}/extraction/estimate")]
    public IActionResult Estimate(string serial, [FromBody] ExtractionEstimateRequest req) =>
        Guard(() => Ok(service.Estimate(serial, req ?? new ExtractionEstimateRequest())));

    private IActionResult Guard(Func<IActionResult> action)
    {
        try { return action(); }
        catch (AgentHttpException ex) { return StatusCode(ex.Status, ex.ToBody()); }
        catch (Exception ex)
        {
            log.LogError(ex, "Error en una ruta de extracción");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
