using System.Security.Claims;
using System.Text.Encodings.Web;
using Factum.Backend.Services.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Infrastructure;

/// <summary>
/// Único handler de autenticación (esquema <c>FactumBearerScheme</c>, usuarios-locales §6.4). Lee el
/// JWT de <c>Authorization: Bearer</c> o de <c>?token=</c> (descargas), lo valida y, en local, chequea
/// la cuenta en <c>users</c> en cada request. Deja <c>Items["User"]</c> y <c>Items["Session"]</c>.
/// El 401 lleva <c>{ error, code }</c> (§5.5).
/// </summary>
public sealed class FactumBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAuthService authService,
    ISessionValidator sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ReadToken();
        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.NoResult();

        var payload = authService.ValidateToken(token);
        if (payload is null)
        {
            Context.Items[AuthContextKeys.FailureCode] = AuthErrors.InvalidToken;
            return AuthenticateResult.Fail("Token inválido o expirado");
        }

        var check = await sessions.ValidateAsync(payload, Context.RequestAborted);
        if (!check.Ok)
        {
            Context.Items[AuthContextKeys.FailureCode] = check.FailureCode ?? AuthErrors.SessionRevoked;
            return AuthenticateResult.Fail("Sesión no válida");
        }

        var user = check.User!;
        var session = check.Session!;
        Context.Items[AuthContextKeys.User] = user;
        Context.Items[AuthContextKeys.Session] = session;

        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("dni", user.Dni));
        identity.AddClaim(new Claim("role", session.Role));
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var code = Context.Items[AuthContextKeys.FailureCode] as string
                   ?? (string.IsNullOrEmpty(ReadToken()) ? AuthErrors.Unauthenticated : AuthErrors.InvalidToken);
        var (status, body) = ChallengeBody(code);

        Response.Headers.WWWAuthenticate = "Bearer";
        await AuthJson.WriteAsync(Response, status, body, Context.RequestAborted);
    }

    /// <summary>Tabla de §5.5. Siempre 401.</summary>
    internal static (int Status, Dictionary<string, string> Body) ChallengeBody(string code)
    {
        var error = code switch
        {
            AuthErrors.Unauthenticated => AuthErrors.MsgUnauthenticated,
            AuthErrors.AccountSuspended => AuthErrors.MsgAccountSuspendedSession,
            _ => AuthErrors.MsgSessionEnded, // invalid_token / session_revoked
        };
        return (StatusCodes.Status401Unauthorized, new Dictionary<string, string> { ["error"] = error, ["code"] = code });
    }

    private string? ReadToken()
    {
        if (Request.Headers.TryGetValue("Authorization", out var header) &&
            header.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return header.ToString()["Bearer ".Length..].Trim();
        if (Request.Query.TryGetValue("token", out var q))
            return q.ToString();
        return null;
    }
}
