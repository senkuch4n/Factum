using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Controllers;

/// <summary>
/// Estado del agente y modo mantenimiento (SDD tatana-instalador-autoupdate §5.4, contrato §13.A).
/// Lo usa el proceso main de Electron antes de instalar una actualización: <c>POST /agent/maintenance</c>
/// entra solo si no hay operaciones en curso y desde ahí las requests mutantes reciben 503
/// <c>agent_updating</c>. El middleware de <c>Program.cs</c> no cuenta esta ruta como operación.
/// </summary>
[ApiController]
[Route("agent")]
public sealed class AgentStateController(OperationTracker tracker, LocalConfigStatus localConfig,
    IOptions<AgentOptions> opts) : ControllerBase
{
    public const int DefaultTtlSeconds = 120;
    public const int MinTtlSeconds = 10;
    public const int MaxTtlSeconds = 600;

    [HttpGet("state")]
    public ActionResult<AgentStateResponse> State()
    {
        var ops = tracker.Snapshot();
        return Ok(new AgentStateResponse(
            AgentVersion.Current,
            HealthController.ResolveMode(opts.Value),
            ops.Count > 0,
            tracker.InMaintenance,
            ops.Select(AgentOperationDto.From).ToList(),
            new LocalConfigDto(localConfig.Path, localConfig.Exists, localConfig.Loaded,
                localConfig.OverridesAllowedOrigins, localConfig.Error)));
    }

    // El body es opcional (un `curl -X POST` pelado tiene que andar): se lee a mano en vez de
    // [FromBody], que sin Content-Type responde 415.
    [HttpPost("maintenance")]
    public async Task<IActionResult> EnterMaintenance(CancellationToken ct)
    {
        if (!IsLocalCaller(Request)) return LocalOnly();
        var ttl = TimeSpan.FromSeconds(ClampTtl(await ReadTtlAsync(Request, ct)));
        if (!tracker.TryEnterMaintenance(ttl, out var expiresAt, out var busy))
            return Conflict(new MaintenanceBusyResponse("Hay operaciones en curso", AgentErrorCodes.AgentBusy,
                busy.Select(AgentOperationDto.From).ToList()));
        return Ok(new MaintenanceResponse(true, expiresAt));
    }

    [HttpDelete("maintenance")]
    public IActionResult ExitMaintenance()
    {
        if (!IsLocalCaller(Request)) return LocalOnly();
        tracker.ExitMaintenance();
        return NoContent();
    }

    private static readonly System.Text.Json.JsonSerializerOptions BodyJson =
        new() { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower };

    /// <summary><c>{ "ttl_seconds": N }</c>; body vacío o inválido → null (default).</summary>
    private static async Task<int?> ReadTtlAsync(HttpRequest req, CancellationToken ct)
    {
        if (req.ContentLength is 0) return null;
        using var reader = new StreamReader(req.Body);
        var buffer = new char[4096];
        var read = await reader.ReadBlockAsync(buffer.AsMemory(), ct);
        return ParseTtl(new string(buffer, 0, read));
    }

    internal static int? ParseTtl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<MaintenanceRequest>(text, BodyJson)?.TtlSeconds; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>Acota el TTL pedido a [10, 600] s; sin valor, 120 s.</summary>
    internal static int ClampTtl(int? requested) =>
        Math.Clamp(requested ?? DefaultTtlSeconds, MinTtlSeconds, MaxTtlSeconds);

    /// <summary>
    /// D-T10: solo sin <c>Origin</c> ni <c>Sec-Fetch-Site</c>, o sea, no desde un navegador (ni
    /// siquiera desde el front permitido). Electron llama desde el proceso main con <c>http</c>.
    /// </summary>
    internal static bool IsLocalCaller(HttpRequest req) =>
        !req.Headers.ContainsKey("Origin") && !req.Headers.ContainsKey("Sec-Fetch-Site");

    private ObjectResult LocalOnly() => StatusCode(StatusCodes.Status403Forbidden, new
    {
        error = "El modo mantenimiento solo se puede pedir desde Tatana, no desde un navegador.",
        code = AgentErrorCodes.MaintenanceLocalOnly,
    });
}

// ── DTOs (snake_case_lower; null se omite, ver Program.cs) ──────────────────

public sealed record MaintenanceRequest(int? TtlSeconds);

public sealed record MaintenanceResponse(bool Maintenance, DateTimeOffset ExpiresAt);

public sealed record MaintenanceBusyResponse(string Error, string Code, IReadOnlyList<AgentOperationDto> Operations);

public sealed record AgentOperationDto(string Kind, DateTimeOffset Since, string? Detail)
{
    public static AgentOperationDto From(AgentOperation o) => new(o.Kind, o.Since, o.Detail);
}

public sealed record LocalConfigDto(string Path, bool Exists, bool Loaded, bool OverridesAllowedOrigins, string? Error);

public sealed record AgentStateResponse(string Version, string Mode, bool Busy, bool Maintenance,
    IReadOnlyList<AgentOperationDto> Operations, LocalConfigDto LocalConfig);
