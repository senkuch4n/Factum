using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Factum.Backend.Controllers;

/// <summary>
/// Panel de administración de cuentas (abm-clientes §5, E1-E9). Solo superadmins en <c>Auth:Mode=local</c>.
/// Sin <c>[AllowDuringPasswordChange]</c>: con la temporal pendiente, el gate responde 403. No-store en
/// todo el controller (notas, contacto y, en E3/E7, la temporal).
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize]
[RequireLocalAuthMode]   // Order = -10: corre ANTES que RequireSuperadmin (D13)
[RequireSuperadmin]      // Order por defecto (0)
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Produces("application/json")]
public sealed class AdminUsersController(IUserAdminService admin) : ControllerBase
{
    /// <summary>E1: todas las cuentas, ordenadas por nombre.</summary>
    [HttpGet]
    [ProducesResponseType<AdminUserListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Respond(await admin.ListAsync(ct), Ok);

    /// <summary>E2.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType<AdminUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken ct) =>
        Respond(await admin.GetAsync(id, ct), Ok);

    /// <summary>E3: alta de un cliente (el rol es siempre cliente). 201 con la temporal, una sola vez.</summary>
    [HttpPost]
    [ProducesResponseType<AdminUserWithPasswordResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] AdminCreateUserRequest request, CancellationToken ct) =>
        Respond(await admin.CreateAsync(Actor(), request, ct),
            v => Created($"/api/admin/users/{Uri.EscapeDataString(v.User.Id)}", v));

    /// <summary>E4: reemplaza los 6 campos editables, con control optimista por <c>expected_updated_at</c>.</summary>
    [HttpPut("{id}")]
    [ProducesResponseType<AdminUpdateUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(string id, [FromBody] AdminUpdateUserRequest request, CancellationToken ct) =>
        Respond(await admin.UpdateAsync(Actor(), id, request, ct), Ok);

    /// <summary>E5: body opcional <c>{ reason? }</c>.</summary>
    [HttpPost("{id}/suspend")]
    [ProducesResponseType<AdminUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Suspend(string id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AdminSuspendRequest? request, CancellationToken ct) =>
        Respond(await admin.SuspendAsync(Actor(), id, request, ct), Ok);

    /// <summary>E6. El body (vacío o <c>{}</c>) se ignora.</summary>
    [HttpPost("{id}/reactivate")]
    [ProducesResponseType<AdminUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reactivate(string id, CancellationToken ct) =>
        Respond(await admin.ReactivateAsync(Actor(), id, ct), Ok);

    /// <summary>E7: temporal nueva (una sola vez); corta las sesiones de la cuenta.</summary>
    [HttpPost("{id}/reset-password")]
    [ProducesResponseType<AdminUserWithPasswordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResetPassword(string id, CancellationToken ct) =>
        Respond(await admin.ResetPasswordAsync(Actor(), id, ct), Ok);

    /// <summary>E8.</summary>
    [HttpPost("{id}/unlock")]
    [ProducesResponseType<AdminUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Unlock(string id, CancellationToken ct) =>
        Respond(await admin.UnlockAsync(Actor(), id, ct), Ok);

    /// <summary>E9: historial, <c>At</c> desc. offset ≥ 0, limit 1-50 (fuera de rango se ajusta).</summary>
    [HttpGet("{id}/events")]
    [ProducesResponseType<AdminUserEventsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Events(string id, [FromQuery] int offset = 0,
        [FromQuery] int limit = UserAdminService.DefaultEventsLimit, CancellationToken ct = default) =>
        Respond(await admin.ListEventsAsync(id, offset, limit, ct), Ok);

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>§5.1: <c>code</c> → HTTP. No usa <c>ResultHttpExtensions.ErrorResult</c> (hallazgo 5).</summary>
    internal static int StatusFor(string? code) => code switch
    {
        AdminErrors.ValidationFailed => StatusCodes.Status400BadRequest,
        AdminErrors.UserNotFound or AdminErrors.NotAvailable => StatusCodes.Status404NotFound,
        AdminErrors.DniTaken or AdminErrors.StaleUpdate or AdminErrors.CannotActOnSelf or
            AdminErrors.LastSuperadmin or AdminErrors.InvalidState or AdminErrors.OperationBusy
            => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };

    private IActionResult Respond<T>(Result<T> result, Func<T, IActionResult> onSuccess)
    {
        if (result.IsSuccess) return onSuccess(result.Value!);
        return StatusCode(StatusFor(AdminErrors.CodeOf(result)), ErrorBody(result));
    }

    /// <summary>{ error, code[, field][, existing_user_id] }: claves literales (no pasan por la naming policy).</summary>
    private static Dictionary<string, object?> ErrorBody<T>(Result<T> result)
    {
        var body = new Dictionary<string, object?> { ["error"] = result.Error };
        if (result.Details is { } details)
            foreach (var (k, v) in details) body[k] = v;
        return body;
    }

    private AdminActor Actor()
    {
        var user = (User)HttpContext.Items[AuthContextKeys.User]!;
        return new AdminActor(user.Dni, user.Name, HttpContext.Connection.RemoteIpAddress?.ToString());
    }
}
