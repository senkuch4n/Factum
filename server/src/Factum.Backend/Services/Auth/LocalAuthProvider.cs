using System.Security.Cryptography;
using Factum.Backend.Common;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Auth;

/// <summary>
/// Login contra la colección <c>users</c> (<c>Auth:Mode=local</c>, usuarios-locales §6.3). DNI +
/// contraseña; <c>username</c> se ignora. Un DNI inexistente cuesta lo mismo que una contraseña
/// incorrecta (verifica contra un hash señuelo) y da el mismo error. Nunca loguea DNI ni contraseña.
/// </summary>
public sealed class LocalAuthProvider : IAuthProvider
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly LocalAuthOptions _opts;
    private readonly TimeProvider _time;
    private readonly ILogger<LocalAuthProvider> _logger;
    private readonly string _dummyHash;

    public LocalAuthProvider(IUserRepository users, IPasswordHasher hasher, LocalAuthOptions opts,
        TimeProvider time, ILogger<LocalAuthProvider> logger)
    {
        _users = users;
        _hasher = hasher;
        _opts = opts;
        _time = time;
        _logger = logger;
        _dummyHash = hasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    public string Mode => AuthModes.Local;

    public async Task<Result<AuthenticatedUser>> AuthenticateAsync(string dni, string? username, string password,
        CancellationToken ct = default)
    {
        // 1. Forma inválida: mismo costo y mismo error que una contraseña incorrecta.
        if (!DniFormat.IsValid(dni) || string.IsNullOrEmpty(password) ||
            password.Length > LocalAuthOptions.PasswordMaxLength)
        {
            _hasher.Verify(password ?? string.Empty, _dummyHash);
            return Rejected(AuthErrors.InvalidCredentials);
        }

        // 2. DNI sin cuenta: no se revela.
        var user = await _users.FindByDniAsync(dni, ct);
        if (user is null)
        {
            _hasher.Verify(password, _dummyHash);
            return Rejected(AuthErrors.InvalidCredentials);
        }

        var now = _time.GetUtcNow().UtcDateTime;

        // 3. Bloqueo vigente: ni se verifica la contraseña.
        if (user.LockedUntil is { } until && until > now)
            return Rejected(AuthErrors.AccountLocked);

        // 4. Contraseña incorrecta: suma al contador; al tope, bloquea.
        if (!_hasher.Verify(password, user.PasswordHash))
        {
            var failures = await _users.IncrementFailedLoginAsync(user.Id, now, ct);
            if (failures >= _opts.MaxFailedAttempts)
            {
                await _users.LockAsync(user.Id, now.AddMinutes(_opts.LockoutMinutes), now, ct);
                _logger.LogWarning("Cuenta bloqueada por intentos fallidos");
                return Rejected(AuthErrors.AccountLocked);
            }
            return Rejected(AuthErrors.InvalidCredentials);
        }

        // 5. Correcta pero suspendida: no toca contadores ni LastLoginAt.
        if (user.Status == UserStatuses.Suspendido)
            return Rejected(AuthErrors.AccountSuspended);

        // 6. Correcta y activa.
        await _users.RegisterSuccessfulLoginAsync(user.Id, now, ct);
        if (_hasher.NeedsRehash(user.PasswordHash))
            await _users.UpdatePasswordHashAsync(user.Id, _hasher.Hash(password), now, ct);

        return Result.Ok(new AuthenticatedUser(
            new User { Dni = user.Dni, Name = user.Name, Sigla = user.Sigla },
            user.Role, user.MustChangePassword, user.PasswordChangedAt));
    }

    private Result<AuthenticatedUser> Rejected(string code)
    {
        _logger.LogInformation("Login local rechazado ({Code})", code);
        return code switch
        {
            AuthErrors.AccountLocked => AuthErrors.Fail<AuthenticatedUser>(code, AuthErrors.MsgAccountLocked(_opts.LockoutMinutes)),
            AuthErrors.AccountSuspended => AuthErrors.Fail<AuthenticatedUser>(code, AuthErrors.MsgAccountSuspendedLogin),
            _ => AuthErrors.Fail<AuthenticatedUser>(AuthErrors.InvalidCredentials, AuthErrors.MsgInvalidCredentials),
        };
    }
}
