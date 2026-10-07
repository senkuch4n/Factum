using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Auth;

/// <summary>Datos de una cuenta nueva con contraseña temporal (bootstrap y, en #12, el ABM).</summary>
/// <remarks>Los cuatro opcionales del final son del ABM (abm-clientes §6.1); <see cref="IUserAccountService.CreateAsync"/>
/// los guarda con trim y <c>null</c> → <c>""</c>, sin validarlos (los valida <c>UserAdminService</c>).</remarks>
public sealed record NewUserAccount(string Dni, string Name, string Sigla, string Role, string TemporaryPassword,
    string ContactPhone = "", string ContactEmail = "", string Organization = "", string Notes = "")
{
    public override string ToString() => $"NewUserAccount {{ Dni = {Dni}, Name = {Name}, Role = {Role} }}";
}

public interface IUserAccountService
{
    /// <summary>Cambio propio (obligatorio o voluntario). Devuelve token nuevo + UserDto.</summary>
    Task<Result<LoginResponse>> ChangeOwnPasswordAsync(string dni, ChangePasswordRequest req, CancellationToken ct = default);

    /// <summary>
    /// Alta con contraseña temporal (<c>MustChangePassword = true</c>). Valida DNI (7-8 dígitos),
    /// nombre (trim, ≤ 120), rol (superadmin|cliente) y el largo de la temporal. Conflict si el DNI
    /// existe. La usan el bootstrap (<c>createdBy = "bootstrap"</c>) y #12 (DNI del superadmin).
    /// </summary>
    Task<Result<UserAccount>> CreateAsync(NewUserAccount input, string createdBy, CancellationToken ct = default);
}

/// <summary>usuarios-locales §5.3 y §6.6. Nunca loguea ni devuelve contraseñas ni hashes.</summary>
public sealed class UserAccountService(
    AuthSettings settings,
    IUserRepository users,
    IPasswordHasher hasher,
    IAuthService auth,
    TimeProvider time) : IUserAccountService
{
    private const int MaxNameLength = 120;
    private readonly LocalAuthOptions _opts = settings.Local ?? new LocalAuthOptions();

    public async Task<Result<LoginResponse>> ChangeOwnPasswordAsync(string dni, ChangePasswordRequest req,
        CancellationToken ct = default)
    {
        if (settings.Mode != AuthModes.Local)
            return AuthErrors.Fail<LoginResponse>(AuthErrors.NotAvailable, AuthErrors.MsgNotAvailable);

        var acc = await users.FindByDniAsync(dni, ct);
        if (acc is null)
            return AuthErrors.Fail<LoginResponse>(AuthErrors.SessionRevoked, AuthErrors.MsgSessionEnded);
        if (acc.Status == UserStatuses.Suspendido)
            return AuthErrors.Fail<LoginResponse>(AuthErrors.AccountSuspended, AuthErrors.MsgAccountSuspendedSession);

        var now = time.GetUtcNow().UtcDateTime;

        // 1. Bloqueo vigente.
        if (acc.LockedUntil is { } until && until > now)
            return Locked();

        // 2. Política de la nueva: no cuesta un hash ni cuenta como intento.
        var current = req.CurrentPassword ?? string.Empty;
        var next = req.NewPassword ?? string.Empty;
        if (PasswordPolicy.ValidateNew(next, req.NewPasswordConfirmation ?? string.Empty, acc.Dni,
                _opts.PasswordMinLength) is { } rule)
            return Rule(rule);

        // 3. Contraseña actual: si falla, cuenta como intento fallido (D5).
        if (current.Length > LocalAuthOptions.PasswordMaxLength || !hasher.Verify(current, acc.PasswordHash))
        {
            var failures = await users.IncrementFailedLoginAsync(acc.Id, now, ct);
            if (failures >= _opts.MaxFailedAttempts)
            {
                await users.LockAsync(acc.Id, now.AddMinutes(_opts.LockoutMinutes), now, ct);
                return Locked();
            }
            return Rule(PasswordPolicy.InvalidCurrent());
        }

        // 4. Igual a la actual (ya verificada).
        if (string.Equals(next, current, StringComparison.Ordinal))
            return Rule(PasswordPolicy.SameAsCurrent());

        // 5. Guardar: corta todas las otras sesiones (PasswordChangedAt nuevo) y emite un token nuevo.
        var changedAt = AuthTime.TruncateToMs(now);
        if (!await users.SetPasswordAsync(acc.Id, hasher.Hash(next), mustChange: false, changedAt, ct))
            return AuthErrors.Fail<LoginResponse>(AuthErrors.SessionRevoked, AuthErrors.MsgSessionEnded);

        var user = new User { Dni = acc.Dni, Name = acc.Name, Sigla = acc.Sigla };
        var token = auth.IssueToken(user, changedAt);
        return Result.Ok(new LoginResponse(token,
            new UserDto(acc.Dni, acc.Name, acc.Sigla, acc.Role, MustChangePassword: false)));
    }

    public async Task<Result<UserAccount>> CreateAsync(NewUserAccount input, string createdBy,
        CancellationToken ct = default)
    {
        var dni = input.Dni?.Trim() ?? string.Empty;
        if (!DniFormat.IsValid(dni))
            return Result.Invalid<UserAccount>("El DNI tiene que tener 7 u 8 dígitos.");
        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return Result.Invalid<UserAccount>("El nombre es obligatorio.");
        if (name.Length > MaxNameLength)
            return Result.Invalid<UserAccount>($"El nombre puede tener hasta {MaxNameLength} caracteres.");
        if (input.Role is not (UserRoles.Superadmin or UserRoles.Cliente))
            return Result.Invalid<UserAccount>(
                $"El rol tiene que ser \"{UserRoles.Superadmin}\" o \"{UserRoles.Cliente}\".");
        var temp = input.TemporaryPassword ?? string.Empty;
        if (temp.Length < _opts.PasswordMinLength)
            return Result.Invalid<UserAccount>(
                $"La contraseña temporal tiene que tener al menos {_opts.PasswordMinLength} caracteres.");
        if (temp.Length > LocalAuthOptions.PasswordMaxLength)
            return Result.Invalid<UserAccount>(
                $"La contraseña temporal puede tener hasta {LocalAuthOptions.PasswordMaxLength} caracteres.");

        if (await users.FindByDniAsync(dni, ct) is not null)
            return Result.Conflict<UserAccount>("Ya existe un usuario con ese DNI.");

        var now = AuthTime.TruncateToMs(time.GetUtcNow().UtcDateTime);
        var account = new UserAccount
        {
            Dni = dni,
            Name = name,
            Sigla = input.Sigla?.Trim() ?? string.Empty,
            Role = input.Role,
            Status = UserStatuses.Activo,
            PasswordHash = hasher.Hash(temp),
            MustChangePassword = true,
            PasswordChangedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            ContactPhone = input.ContactPhone?.Trim() ?? string.Empty,
            ContactEmail = input.ContactEmail?.Trim() ?? string.Empty,
            Organization = input.Organization?.Trim() ?? string.Empty,
            Notes = input.Notes?.Trim() ?? string.Empty,
        };
        if (!await users.TryInsertAsync(account, ct))
            return Result.Conflict<UserAccount>("Ya existe un usuario con ese DNI.");
        return Result.Ok(account);
    }

    private Result<LoginResponse> Locked() =>
        AuthErrors.Fail<LoginResponse>(AuthErrors.AccountLocked, AuthErrors.MsgAccountLocked(_opts.LockoutMinutes));

    private static Result<LoginResponse> Rule(PasswordRuleError e) =>
        AuthErrors.Fail<LoginResponse>(e.Code, e.Message, e.Field);
}
