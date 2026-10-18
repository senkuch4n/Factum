using Factum.Agent.Common;
using Factum.Agent.Services;
using Factum.Agent.Services.Session;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Agent.Controllers;

/// <summary>
/// Grabación de la sesión de un caso (grabacion-sesion-completa §7.2.2–§7.2.5). Errores <c>{ "error", "code", … }</c>
/// (<see cref="AgentErrorCodes"/>). Nada depende del <c>RequestAborted</c>: si el navegador recarga, la sesión sigue y se
/// recupera por <c>/agent/state</c> y por el diario.
/// </summary>
[ApiController]
[Route("cases/{caseId}/session")]
public sealed class CaseSessionController(SessionRecorder recorder, ILogger<CaseSessionController> log) : ControllerBase
{
    private IActionResult Error(AgentHttpException ex) => StatusCode(ex.Status, ex.ToBody());

    [HttpPost("start")]
    public async Task<IActionResult> Start(string caseId, [FromBody] StartSessionRequest? request)
    {
        try { return Ok(await recorder.StartAsync(caseId, request ?? new StartSessionRequest())); }
        catch (AgentHttpException ex) { return Error(ex); }
        catch (Exception ex)
        {
            // Error de scrcpy (como el record/start de hoy): 500 sin code.
            log.LogWarning(ex, "No se pudo iniciar la grabación de la sesión del caso {CaseId}", caseId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("stop")]
    public async Task<IActionResult> Stop(string caseId, [FromBody] StopSessionRequest? request)
    {
        try { return Ok(await recorder.StopAsync(caseId, request?.Reason, request?.OriginId)); }
        catch (AgentHttpException ex) { return Error(ex); }
        catch (Exception ex)
        {
            log.LogError(ex, "No se pudo detener la grabación de la sesión del caso {CaseId}", caseId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("marks")]
    public async Task<IActionResult> AddMark(string caseId, [FromBody] AddMarkRequest? request)
    {
        try { return Ok(await recorder.AddMarkAsync(caseId, request?.Text, request?.OriginId)); }
        catch (AgentHttpException ex) { return Error(ex); }
    }

    // ── Cámara del procedimiento (grabacion-sesion-completa-f2 §7.2.5) ──

    [HttpPost("camera/start")]
    public async Task<IActionResult> StartCamera(string caseId, [FromBody] StartCameraRequest? request)
    {
        try { return Ok(await recorder.StartCameraAsync(caseId, request)); }
        catch (AgentHttpException ex) { return Error(ex); }
        catch (Exception ex)
        {
            log.LogError(ex, "No se pudo sumar la cámara a la sesión del caso {CaseId}", caseId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("camera/stop")]
    public async Task<IActionResult> StopCamera(string caseId, [FromBody] CameraClosedRequest? request)
    {
        try { return Ok(await recorder.StopCameraAsync(caseId, request?.OriginId)); }
        catch (AgentHttpException ex) { return Error(ex); }
        catch (Exception ex)
        {
            log.LogError(ex, "No se pudo quitar la cámara de la sesión del caso {CaseId}", caseId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("camera/retry")]
    public async Task<IActionResult> RetryCamera(string caseId, [FromBody] CameraClosedRequest? request)
    {
        try { return Ok(await recorder.RetryCameraAsync(caseId, request?.OriginId)); }
        catch (AgentHttpException ex) { return Error(ex); }
        catch (Exception ex)
        {
            log.LogError(ex, "No se pudo reintentar la cámara de la sesión del caso {CaseId}", caseId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet]
    public IActionResult Get(string caseId)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            return recorder.GetJournal(caseId) is { } journal
                ? Ok(journal)
                : Error(new AgentHttpException(404, AgentErrorCodes.SessionNotFound,
                    "Este caso no tiene grabación de la sesión en esta PC."));
        }
        catch (AgentHttpException ex) { return Error(ex); }
    }
}
