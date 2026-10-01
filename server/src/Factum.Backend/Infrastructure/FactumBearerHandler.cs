using System.Text.Encodings.Web;
using Factum.Backend.Services.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Infrastructure;

public sealed class FactumBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAuthService authService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token = null;

        if (Request.Headers.TryGetValue("Authorization", out var header) &&
            header.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = header.ToString()["Bearer ".Length..].Trim();
        }
        else if (Request.Query.TryGetValue("token", out var q))
        {
            token = q.ToString();
        }

        if (string.IsNullOrEmpty(token))
            return Task.FromResult(AuthenticateResult.NoResult());

        var user = authService.ValidateToken(token);
        if (user is null)
            return Task.FromResult(AuthenticateResult.Fail("Token inválido o expirado"));

        Context.Items["User"] = user;

        var identity = new System.Security.Claims.ClaimsIdentity("Bearer");
        identity.AddClaim(new System.Security.Claims.Claim("dni", user.Dni));
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
