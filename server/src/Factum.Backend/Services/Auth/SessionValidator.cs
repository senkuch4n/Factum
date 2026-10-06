using Factum.Backend.Infrastructure;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Auth;

/// <summary>Resultado de validar la sesión de un token ya verificado (firma y vencimiento).</summary>
public sealed record SessionCheck(bool Ok, string? FailureCode, User? User, AuthSession? Session)
{
    public static SessionCheck Fail(string code) => new(false, code, null, null);
}

public interface ISessionValidator
{
    Task<SessionCheck> ValidateAsync(TokenPayload payload, CancellationToken ct);
}

/// <summary>
/// Validación por request (usuarios-locales §6.4). En dev/external no consulta la base. En local
/// busca la cuenta por DNI (índice único, sin hashes, sin caché — D7 = A) y corta la sesión si la
/// cuenta no existe, está suspendida o cambió la contraseña después de emitido el token.
/// </summary>
public sealed class SessionValidator(AuthSettings settings, IUserRepository users) : ISessionValidator
{
    private static readonly AuthSession DefaultSession = new(UserRoles.Cliente, false);

    public async Task<SessionCheck> ValidateAsync(TokenPayload payload, CancellationToken ct)
    {
        if (settings.Mode != AuthModes.Local)
            return new SessionCheck(true, null, payload.User, DefaultSession);

        var acc = await users.FindSessionByDniAsync(payload.User.Dni, ct);
        if (acc is null)
            return SessionCheck.Fail(AuthErrors.SessionRevoked);
        if (acc.Status == UserStatuses.Suspendido)
            return SessionCheck.Fail(AuthErrors.AccountSuspended);
        if (payload.PasswordChangedAtMs is not { } pca || pca != AuthTime.ToUnixMs(acc.PasswordChangedAt))
            return SessionCheck.Fail(AuthErrors.SessionRevoked);

        return new SessionCheck(true, null,
            new User { Dni = acc.Dni, Name = acc.Name, Sigla = acc.Sigla },
            new AuthSession(acc.Role, acc.MustChangePassword));
    }
}
