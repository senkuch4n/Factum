using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Auth;

namespace Factum.Backend.Services.Admin;

/// <summary>Quién hace la acción: el superadmin de la sesión (DNI + nombre fresco de la base) y su IP.</summary>
public sealed record AdminActor(string Dni, string Name, string? Ip);

/// <summary>Panel de administración de cuentas (abm-clientes §6.1). Todas las reglas viven acá.</summary>
public interface IUserAdminService
{
    Task<Result<AdminUserListResponse>> ListAsync(CancellationToken ct = default);
    Task<Result<AdminUserResponse>> GetAsync(string id, CancellationToken ct = default);
    Task<Result<AdminUserWithPasswordResponse>> CreateAsync(AdminActor actor, AdminCreateUserRequest req, CancellationToken ct = default);
    Task<Result<AdminUpdateUserResponse>> UpdateAsync(AdminActor actor, string id, AdminUpdateUserRequest req, CancellationToken ct = default);
    Task<Result<AdminUserResponse>> SuspendAsync(AdminActor actor, string id, AdminSuspendRequest? req, CancellationToken ct = default);
    Task<Result<AdminUserResponse>> ReactivateAsync(AdminActor actor, string id, CancellationToken ct = default);
    Task<Result<AdminUserWithPasswordResponse>> ResetPasswordAsync(AdminActor actor, string id, CancellationToken ct = default);
    Task<Result<AdminUserResponse>> UnlockAsync(AdminActor actor, string id, CancellationToken ct = default);
    Task<Result<AdminUserEventsResponse>> ListEventsAsync(string id, int offset, int limit, CancellationToken ct = default);
}

/// <summary>
/// abm-clientes §6. Flujo de cada acción: validar → leer → reglas → escritura condicional → auditar →
/// releer. Si falla antes de la escritura, no se audita nada. Nunca loguea la temporal, hashes,
/// <c>Changes</c> ni <c>Notes</c>. Escribe solo en <c>users</c> (por <c>_id</c>), <c>user_admin_events</c>
/// (insert) y <c>user_admin_locks</c>.
/// </summary>
public sealed class UserAdminService(
    AuthSettings settings,
    IUserRepository users,
    IUserAccountService accounts,
    IUserAdminEventRepository events,
    IAdminLockRepository locks,
    ICaseRepository cases,
    IPasswordHasher hasher,
    ITemporaryPasswordGenerator generator,
    TimeProvider time,
    ILogger<UserAdminService> log) : IUserAdminService
{
    public const string SuperadminLockKey = "superadmin_status";
    public static readonly TimeSpan LockLease = TimeSpan.FromSeconds(10);
    public const int LockAttempts = 20;
    public static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(50);
    public const int DefaultEventsLimit = 20;
    public const int MaxEventsLimit = 50;
    private const int AuditAttempts = 2;

    private readonly LocalAuthOptions _opts = settings.Local ?? new LocalAuthOptions();

    private bool IsLocal => settings.Mode == AuthModes.Local;
    private DateTime Now() => AuthTime.TruncateToMs(time.GetUtcNow().UtcDateTime);

    private static Result<T> NotAvailable<T>() => AdminErrors.Fail<T>(AdminErrors.NotAvailable, AdminErrors.MsgNotAvailable);
    private static Result<T> NotFound<T>() => AdminErrors.Fail<T>(AdminErrors.UserNotFound, AdminErrors.MsgUserNotFound);
    private static Result<T> Conflict<T>(string code, string msg) => AdminErrors.Fail<T>(code, msg);

    // ── E1 / E2 ──────────────────────────────────────────────────────────────

    public async Task<Result<AdminUserListResponse>> ListAsync(CancellationToken ct = default)
    {
        if (!IsLocal) return NotAvailable<AdminUserListResponse>();

        var all = await users.ListAsync(ct: ct);
        var counts = await cases.CountByOfficerDnisAsync(all.Select(u => u.Dni).Distinct(StringComparer.Ordinal).ToList(), ct);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var u in all) names.TryAdd(u.Dni, u.Name);
        var now = Now();
        return Result.Ok(new AdminUserListResponse(
            all.Select(u => ToDto(u, counts.GetValueOrDefault(u.Dni), names, now)).ToList()));
    }

    public async Task<Result<AdminUserResponse>> GetAsync(string id, CancellationToken ct = default)
    {
        if (!IsLocal) return NotAvailable<AdminUserResponse>();
        var acc = await users.FindAdminViewByIdAsync(id, ct);
        if (acc is null) return NotFound<AdminUserResponse>();
        return Result.Ok(new AdminUserResponse(await BuildDtoAsync(acc, ct)));
    }

    // ── E3 alta ──────────────────────────────────────────────────────────────

    public async Task<Result<AdminUserWithPasswordResponse>> CreateAsync(AdminActor actor, AdminCreateUserRequest req,
        CancellationToken ct = default)
    {
        if (!IsLocal) return NotAvailable<AdminUserWithPasswordResponse>();

        // 1. Validar (las mismas reglas que el client).
        if (AccountFieldRules.ValidateCreate(req.Dni, req.Name, req.Sigla, req.ContactPhone, req.ContactEmail,
                req.Organization, req.Notes) is { } invalid)
            return AdminErrors.Fail<AdminUserWithPasswordResponse>(invalid);

        var dni = AccountFieldRules.Normalize(req.Dni);
        // 2-3. Temporal generada por el servidor (D7) y alta siempre como cliente (D3).
        var temp = generator.Generate(_opts.PasswordMinLength);
        var created = await accounts.CreateAsync(new NewUserAccount(
            dni,
            AccountFieldRules.Normalize(req.Name),
            AccountFieldRules.Normalize(req.Sigla),
            UserRoles.Cliente,
            temp,
            AccountFieldRules.Normalize(req.ContactPhone),
            AccountFieldRules.Normalize(req.ContactEmail),
            AccountFieldRules.Normalize(req.Organization),
            AccountFieldRules.Normalize(req.Notes)), actor.Dni, ct);

        if (!created.IsSuccess)
        {
            // 4. DNI repetido → dni_taken con el id de la cuenta existente.
            if (created.Kind == ErrorKind.Conflict)
            {
                var existing = await users.FindByDniAsync(dni, ct);
                return AdminErrors.Fail<AdminUserWithPasswordResponse>(AdminErrors.DniTaken, AdminErrors.MsgDniTaken,
                    AdminErrors.FieldDni,
                    new Dictionary<string, object?> { [AdminErrors.ExistingUserIdKey] = existing?.Id });
            }
            // Ya se validó: no debería pasar. Se devuelve con el campo más probable.
            var field = (created.Error ?? "").Contains("DNI", StringComparison.Ordinal) ? AdminErrors.FieldDni
                : (created.Error ?? "").Contains("nombre", StringComparison.Ordinal) ? AdminErrors.FieldName
                : null;
            return AdminErrors.Fail<AdminUserWithPasswordResponse>(AdminErrors.ValidationFailed,
                created.Error ?? AdminErrors.MsgNameRequired, field);
        }

        var acc = created.Value!;
        // 5. Auditoría (nunca la temporal: AdminChanges usa lista blanca).
        await AuditAsync(actor, acc, UserAdminActions.Create, AdminChanges.ForCreate(acc), null);

        // 6. Releer sin hashes.
        var view = await users.FindAdminViewByIdAsync(acc.Id, ct) ?? WithoutHashes(acc);
        return Result.Ok(new AdminUserWithPasswordResponse(await BuildDtoAsync(view, ct), temp));
    }

    // ── E4 edición (D11) ─────────────────────────────────────────────────────

    public async Task<Result<AdminUpdateUserResponse>> UpdateAsync(AdminActor actor, string id, AdminUpdateUserRequest req,
        CancellationToken ct = default)
    {
        if (!IsLocal) return NotAvailable<AdminUpdateUserResponse>();

        // 1. expected_updated_at obligatorio.
        if (req.ExpectedUpdatedAt is not { } expectedRaw)
            return AdminErrors.Fail<AdminUpdateUserResponse>(AdminErrors.ValidationFailed,
                AdminErrors.MsgExpectedUpdatedAt, AdminErrors.FieldExpectedUpdatedAt);
        // 2. Los 6 campos.
        if (AccountFieldRules.ValidateEditable(req.Name, req.Sigla, req.ContactPhone, req.ContactEmail,
                req.Organization, req.Notes) is { } invalid)
            return AdminErrors.Fail<AdminUpdateUserResponse>(invalid);

        // 3. Leer.
        var acc = await users.FindAdminViewByIdAsync(id, ct);
        if (acc is null) return NotFound<AdminUpdateUserResponse>();

        var fields = new AccountEditableFields(
            AccountFieldRules.Normalize(req.Name),
            AccountFieldRules.Normalize(req.Sigla),
            AccountFieldRules.Normalize(req.ContactPhone),
            AccountFieldRules.Normalize(req.ContactEmail),
            AccountFieldRules.Normalize(req.Organization),
            AccountFieldRules.Normalize(req.Notes));

        // 4. Sin cambios → changed: false, sin escribir ni auditar (y sin mirar la versión).
        var changes = AdminChanges.ForUpdate(acc, fields);
        if (changes.Count == 0)
            return Result.Ok(new AdminUpdateUserResponse(await BuildDtoAsync(acc, ct), Changed: false));

        // 5. Control optimista.
        var expected = AuthTime.TruncateToMs(expectedRaw.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(expectedRaw, DateTimeKind.Utc)
            : expectedRaw);
        if (expected.Ticks != AuthTime.TruncateToMs(acc.UpdatedAt).Ticks)
            return Conflict<AdminUpdateUserResponse>(AdminErrors.StaleUpdate, AdminErrors.MsgStaleUpdate);

        if (!await users.UpdateEditableFieldsIfUnchangedAsync(acc.Id, fields, acc.UpdatedAt, Now(), ct))
            return Conflict<AdminUpdateUserResponse>(AdminErrors.StaleUpdate, AdminErrors.MsgStaleUpdate);

        var after = await users.FindAdminViewByIdAsync(acc.Id, ct);
        await AuditAsync(actor, after ?? acc, UserAdminActions.Update, changes, null);
        if (after is null) return NotFound<AdminUpdateUserResponse>();
        return Result.Ok(new AdminUpdateUserResponse(await BuildDtoAsync(after, ct), Changed: true));
    }

    // ── E5 suspender (D4, D10) ───────────────────────────────────────────────

    public async Task<Result<AdminUserResponse>> SuspendAsync(AdminActor actor, string id, AdminSuspendRequest? req,
        CancellationToken ct = default)
    {
        if (!IsLocal) return NotAvailable<AdminUserResponse>();

        // 1. Motivo opcional.
        if (AccountFieldRules.Reason(req?.Reason) is { } invalid)
            return AdminErrors.Fail<AdminUserResponse>(invalid);
        var reasonText = AccountFieldRules.Normalize(req?.Reason);
        string? reason = reasonText.Length == 0 ? null : reasonText;

        // 2-4. Leer y reglas.
        var acc = await users.FindAdminViewByIdAsync(id, ct);
        if (acc is null) return NotFound<AdminUserResponse>();
        if (IsSelf(acc, actor))
            return Conflict<AdminUserResponse>(AdminErrors.CannotActOnSelf, AdminErrors.MsgCannotActOnSelf);
        if (acc.Status != UserStatuses.Activo)
            return Conflict<AdminUserResponse>(AdminErrors.InvalidState, AdminErrors.MsgAlreadySuspended);

        if (acc.Role == UserRoles.Superadmin)
        {
            // 5. Regla 2 (§6.3): lock con lease + relectura + recuento, sin transacciones.
            var outcome = await SuspendSuperadminLockedAsync(actor, acc.Id, reason, ct);
            if (outcome is not null) return outcome;
        }
        else if (!await users.SuspendIfActiveAsync(acc.Id, actor.Dni, reason, Now(), ct))
        {
            return Conflict<AdminUserResponse>(AdminErrors.InvalidState, AdminErrors.MsgAlreadySuspended);
        }

        var after = await users.FindAdminViewByIdAsync(acc.Id, ct);
        await AuditAsync(actor, after ?? acc, UserAdminActions.Suspend, [], reason);
        if (after is null) return NotFound<AdminUserResponse>();
        return Result.Ok(new AdminUserResponse(await BuildDtoAsync(after, ct)));
    }

    /// <summary>null si suspendió; si no, el error a devolver. Libera el lock en un finally.</summary>
    private async Task<Result<AdminUserResponse>?> SuspendSuperadminLockedAsync(AdminActor actor, string targetId,
        string? reason, CancellationToken ct)
    {
        var owner = Guid.NewGuid().ToString();
        var acquired = false;
        for (var attempt = 0; attempt < LockAttempts; attempt++)
        {
            if (await locks.TryAcquireAsync(SuperadminLockKey, owner, Now(), LockLease, ct))
            {
                acquired = true;
                break;
            }
            if (attempt < LockAttempts - 1) await Task.Delay(LockRetryDelay, ct);
        }
        if (!acquired)
            return Conflict<AdminUserResponse>(AdminErrors.OperationBusy, AdminErrors.MsgOperationBusy);

        try
        {
            var target = await users.FindAdminViewByIdAsync(targetId, ct);
            if (target is null) return NotFound<AdminUserResponse>();
            if (target.Status != UserStatuses.Activo)
                return Conflict<AdminUserResponse>(AdminErrors.InvalidState, AdminErrors.MsgAlreadySuspended);
            if (await users.CountOtherActiveSuperadminsAsync(target.Id, ct) == 0)
                return Conflict<AdminUserResponse>(AdminErrors.LastSuperadmin, AdminErrors.MsgLastSuperadmin);
            if (!await users.SuspendIfActiveAsync(target.Id, actor.Dni, reason, Now(), ct))
                return Conflict<AdminUserResponse>(AdminErrors.InvalidState, AdminErrors.MsgAlreadySuspended);
            return null;
        }
        finally
        {
            try
            {
                await locks.ReleaseAsync(SuperadminLockKey, owner, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // El lease vence solo a los 10 s; no se propaga.
                log.LogWarning(ex, "No se pudo liberar el lock {LockKey}", SuperadminLockKey);
            }
        }
    }

    // ── E6 reactivar ─────────────────────────────────────────────────────────

    public async Task<Result<AdminUserResponse>> ReactivateAsync(AdminActor actor, string id, CancellationToken ct = default)
    {
        if (!IsLocal) return NotAvailable<AdminUserResponse>();

        var acc = await users.FindAdminViewByIdAsync(id, ct);
        if (acc is null) return NotFound<AdminUserResponse>();
        if (acc.Status != UserStatuses.Suspendido)
            return Conflict<AdminUserResponse>(AdminErrors.InvalidState, AdminErrors.MsgAlreadyActive);
        if (!await users.ReactivateIfSuspendedAsync(acc.Id, Now(), ct))
            return Conflict<AdminUserResponse>(AdminErrors.InvalidState, AdminErrors.MsgAlreadyActive);

        var after = await users.FindAdminViewByIdAsync(acc.Id, ct);
        await AuditAsync(actor, after ?? acc, UserAdminActions.Reactivate, [], null);
        if (after is null) return NotFound<AdminUserResponse>();
        return Result.Ok(new AdminUserResponse(await BuildDtoAsync(after, ct)));
    }

    // ── E7 reset de contraseña ───────────────────────────────────────────────

    public async Task<Result<AdminUserWithPasswordResponse>> ResetPasswordAsync(AdminActor actor, string id,
        CancellationToken ct = default)
    {
        if (!IsLocal) return NotAvailable<AdminUserWithPasswordResponse>();

        var acc = await users.FindAdminViewByIdAsync(id, ct);
        if (acc is null) return NotFound<AdminUserWithPasswordResponse>();
        if (IsSelf(acc, actor))
            return Conflict<AdminUserWithPasswordResponse>(AdminErrors.CannotActOnSelf, AdminErrors.MsgCannotActOnSelf);

        // No cambia Status: una cuenta suspendida sigue suspendida. PasswordChangedAt nuevo corta las sesiones.
        var temp = generator.Generate(_opts.PasswordMinLength);
        if (!await users.SetPasswordAsync(acc.Id, hasher.Hash(temp), mustChange: true, changedAt: Now(), ct))
            return NotFound<AdminUserWithPasswordResponse>();

        var after = await users.FindAdminViewByIdAsync(acc.Id, ct);
        await AuditAsync(actor, after ?? acc, UserAdminActions.ResetPassword, [], null);
        if (after is null) return NotFound<AdminUserWithPasswordResponse>();
        return Result.Ok(new AdminUserWithPasswordResponse(await BuildDtoAsync(after, ct), temp));
    }

    // ── E8 desbloquear ───────────────────────────────────────────────────────

    public async Task<Result<AdminUserResponse>> UnlockAsync(AdminActor actor, string id, CancellationToken ct = default)
    {
        if (!IsLocal) return NotAvailable<AdminUserResponse>();

        var acc = await users.FindAdminViewByIdAsync(id, ct);
        if (acc is null) return NotFound<AdminUserResponse>();
        var now = Now();
        // Uno mismo sí puede desbloquearse (D4 solo prohíbe suspender y resetear).
        if (acc.LockedUntil is not { } until || until <= now)
            return Conflict<AdminUserResponse>(AdminErrors.InvalidState, AdminErrors.MsgNotLocked);
        if (!await users.UnlockIfLockedAsync(acc.Id, now, ct))
            return Conflict<AdminUserResponse>(AdminErrors.InvalidState, AdminErrors.MsgNotLocked);

        var after = await users.FindAdminViewByIdAsync(acc.Id, ct);
        await AuditAsync(actor, after ?? acc, UserAdminActions.Unlock, [], null);
        if (after is null) return NotFound<AdminUserResponse>();
        return Result.Ok(new AdminUserResponse(await BuildDtoAsync(after, ct)));
    }

    // ── E9 historial ─────────────────────────────────────────────────────────

    public async Task<Result<AdminUserEventsResponse>> ListEventsAsync(string id, int offset, int limit,
        CancellationToken ct = default)
    {
        if (!IsLocal) return NotAvailable<AdminUserEventsResponse>();

        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, MaxEventsLimit);
        var acc = await users.FindAdminViewByIdAsync(id, ct);
        if (acc is null) return NotFound<AdminUserEventsResponse>();

        var page = await events.ListByTargetAsync(acc.Id, offset, limit, ct);
        var hasMore = page.Count > limit;
        return Result.Ok(new AdminUserEventsResponse(
            page.Take(limit).Select(e => new AdminUserEventDto(
                e.Id, e.At, e.ActorDni, e.ActorName, e.Action,
                e.Changes.Select(c => new AdminUserChangeDto(c.Field, c.From, c.To)).ToList(),
                e.Reason, e.Ip)).ToList(),
            hasMore));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>D4 regla 1 / T4: uno mismo se identifica por DNI (ordinal).</summary>
    private static bool IsSelf(UserAccount acc, AdminActor actor) =>
        string.Equals(acc.Dni, actor.Dni, StringComparison.Ordinal);

    /// <summary>
    /// §6.6: primero la acción, después la auditoría, con un reintento. Si falla dos veces, se loguea
    /// (sin contraseñas ni Changes) y la respuesta sale igual como éxito: la acción ya se aplicó.
    /// Va con <see cref="CancellationToken.None"/> para no perder el registro si el cliente corta.
    /// </summary>
    private async Task AuditAsync(AdminActor actor, UserAccount target, string action, List<UserAdminChange> changes,
        string? reason)
    {
        var evt = new UserAdminEvent
        {
            At = Now(),
            ActorDni = actor.Dni,
            ActorName = actor.Name,
            TargetUserId = target.Id,
            TargetDni = target.Dni,
            TargetName = target.Name,
            Action = action,
            Changes = changes,
            Reason = reason,
            Ip = actor.Ip,
        };
        for (var attempt = 1; attempt <= AuditAttempts; attempt++)
        {
            try
            {
                await events.InsertAsync(evt, CancellationToken.None);
                return;
            }
            catch (Exception) when (attempt < AuditAttempts)
            {
                // Reintento.
            }
            catch (Exception)
            {
                // Sin la excepción: su texto podría arrastrar el documento (Changes / notas).
                log.LogError("Auditoría no registrada: {Action} sobre la cuenta {TargetUserId} por DNI {ActorDni}",
                    action, target.Id, actor.Dni);
            }
        }
    }

    /// <summary>DTO del detalle: conteo de un DNI y nombres de quien creó / suspendió (a lo sumo dos lecturas).</summary>
    private async Task<AdminUserDto> BuildDtoAsync(UserAccount acc, CancellationToken ct)
    {
        var counts = await cases.CountByOfficerDnisAsync([acc.Dni], ct);
        var names = new Dictionary<string, string>(StringComparer.Ordinal) { [acc.Dni] = acc.Name };
        foreach (var dni in new[] { acc.CreatedBy, acc.SuspendedBy })
        {
            if (string.IsNullOrEmpty(dni) || names.ContainsKey(dni) || !DniFormat.IsValid(dni)) continue;
            if (await users.FindSessionByDniAsync(dni, ct) is { } other) names[dni] = other.Name;
        }
        return ToDto(acc, counts.GetValueOrDefault(acc.Dni), names, Now());
    }

    /// <summary>
    /// Armado explícito (nunca hashes). <c>locked_until</c> solo si está vigente. <c>"bootstrap"</c> o un
    /// DNI sin cuenta → nombre null.
    /// </summary>
    internal static AdminUserDto ToDto(UserAccount acc, long caseCount, IReadOnlyDictionary<string, string> names,
        DateTime now) =>
        new(
            acc.Id, acc.Dni, acc.Name, acc.Sigla, acc.Role, acc.Status,
            acc.MustChangePassword,
            acc.LockedUntil is { } until && until > now ? until : null,
            acc.LastLoginAt,
            acc.CreatedAt, acc.CreatedBy, NameOf(acc.CreatedBy, names), acc.UpdatedAt,
            acc.SuspendedAt, acc.SuspendedBy, NameOf(acc.SuspendedBy, names), acc.SuspensionReason,
            acc.ContactPhone ?? "", acc.ContactEmail ?? "", acc.Organization ?? "", acc.Notes ?? "",
            caseCount);

    private static string? NameOf(string? dni, IReadOnlyDictionary<string, string> names) =>
        dni is not null && names.TryGetValue(dni, out var n) ? n : null;

    private static UserAccount WithoutHashes(UserAccount acc)
    {
        acc.PasswordHash = string.Empty;
        acc.LastEmergencyResetHash = null;
        return acc;
    }
}
