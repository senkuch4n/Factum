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
    IOptions<AuditOptions> auditOpts) : ControllerBase
{
    private User Officer => (User)HttpContext.Items["User"]!;

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Report([FromBody] ReportAgentEventRequest request, CancellationToken ct)
    {
        var evt = new AgentEvent
        {
            Dni = Officer.Dni,
            Hostname = request.Hostname,
            OsUser = request.OsUser,
            AgentVersion = request.AgentVersion,
            Mode = request.Mode,
            Action = request.Action,
            CaseId = request.CaseId,
            Ip = ResolveClientIp(),
        };

        await repo.InsertAsync(evt, ct);
        return NoContent();
    }

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
