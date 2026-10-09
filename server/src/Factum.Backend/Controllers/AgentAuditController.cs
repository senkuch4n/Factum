using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Controllers;

// Quién puede leer la auditoría de uso del agente Tatana (usuarios-locales D13):
// los DNIs de Audit:AdminDnis (config, ver appsettings.json "Audit") y, en Auth:Mode=local,
// cualquier cuenta con rol superadmin. El rol viaja en la sesión del request
// (HttpContext.GetSession()), no en Models.User; en dev/external es siempre "cliente",
// así que ahí sigue valiendo solo la lista.
public sealed class AuditOptions
{
    public List<string> AdminDnis { get; set; } = [];
}

[ApiController]
[Route("api/agent-events")]
[Authorize]
[Produces("application/json")]
public sealed class AgentAuditController(
    IAgentEventRepository repo,
    ICaseEventRepository caseEvents,
    IOptions<AuditOptions> auditOpts,
    ILogger<AgentAuditController> log) : ControllerBase
{
    private User Officer => (User)HttpContext.Items["User"]!;

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Report([FromBody] ReportAgentEventRequest request, CancellationToken ct)
    {
        var ip = ResolveClientIp();
        var evt = new AgentEvent
        {
            Dni = Officer.Dni,
            Hostname = request.Hostname,
            OsUser = request.OsUser,
            AgentVersion = request.AgentVersion,
            Mode = request.Mode,
            Action = request.Action,
            CaseId = request.CaseId,
            Ip = ip,
        };

        await repo.InsertAsync(evt, ct);
        // trazabilidad-caso (DT9): si la acción es una captura y trae case_id, se deriva además un
        // case_event equivalente. Best-effort y aislado: un fallo loguea y NO cambia el 204 ni el
        // agent_event ya guardado.
        if (!string.IsNullOrWhiteSpace(request.CaseId) &&
            MapCaptureType(request.Action) is { } type)
        {
            try
            {
                await caseEvents.InsertAsync(new CaseEvent
                {
                    CaseId = request.CaseId!,
                    Type = type,
                    ActorDni = Officer.Dni,
                    ActorName = Officer.Name,
                    Timestamp = DateTime.UtcNow,
                    Hostname = request.Hostname,
                    OsUser = request.OsUser,
                    AgentMode = request.Mode,
                    Ip = ip,
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "No se pudo derivar el evento de captura del caso {CaseId}", request.CaseId);
            }
        }
        return NoContent();
    }

    // DT9: AgentAction → CaseEventType. Startup no genera evento de caso (uso del agente, no un hito).
    private static string? MapCaptureType(AgentAction action) => action switch
    {
        AgentAction.Screenshot => CaseEventTypes.CaptureScreenshot,
        AgentAction.CaptureStart => CaseEventTypes.CaptureVideoStart,
        AgentAction.CaptureStop => CaseEventTypes.CaptureVideoStop,
        AgentAction.Webcam => CaseEventTypes.CapturePhoto,
        _ => null,   // Startup u otros
    };

    // HttpContext.Connection.RemoteIpAddress ya refleja la IP real del fiscal:
    // ForwardedHeadersOptions (Program.cs) la reescribe desde X-Forwarded-For
    // si el backend corre detrás de un reverse proxy; si no hay proxy, ya es
    // la IP de origen directamente.
    private string ResolveClientIp() =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    [HttpGet]
    [ProducesResponseType<List<AgentEvent>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] string? dni, [FromQuery] string? caseId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        if (!auditOpts.Value.AdminDnis.Contains(Officer.Dni) &&
            HttpContext.GetSession().Role != UserRoles.Superadmin)
            return Forbid();

        var events = await repo.ListAsync(dni, caseId, from, to, ct);
        return Ok(new { events });
    }
}
