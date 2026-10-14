using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Factum.Backend.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(
    IAuthService authService,
    AuthSettings authSettings,
    IUserAccountService accounts,
    ILoginThrottle throttle,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>usuarios-locales §5.1: modo + largos de contraseña (siempre; en dev/external, los defaults).</summary>
    [HttpGet("mode")]
    [AllowAnonymous]
    public IActionResult Mode()
    {
        var local = authSettings.Local ?? new LocalAuthOptions();
        return Ok(new
        {
            mode = authService.Mode,
            password_min_length = local.PasswordMinLength,
            password_max_length = LocalAuthOptions.PasswordMaxLength,
        });
    }

    /// <summary>
    /// §5.2. En local: 401 invalid_credentials / 429 account_locked / 403 account_suspended, con code.
    /// api-hardening §4.4: además, 429 account_locked + Retry-After por IP (middleware, política "login") y
    /// por DNI (acá, antes de validar credenciales: no suma intentos fallidos), en todos los modos.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimit.PolicyName)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (throttle.TryAcquire(request.Dni) is { } wait)
            return LoginRateLimit.Rejection(HttpContext, wait, LoginRateLimit.Scope.Dni, request.Dni);

        var result = await authService.LoginAsync(request, ct);
        if (result.IsSuccess) return Ok(result.Value);

        var code = AuthErrors.CodeOf(result);
        // dev/external: sin code, como siempre (401 { error }).
        if (code is null) return Unauthorized(new { error = result.Error });

        var status = code switch
        {
            AuthErrors.AccountLocked => StatusCodes.Status429TooManyRequests,
            AuthErrors.AccountSuspended => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status401Unauthorized,
        };
        return StatusCode(status, ErrorBody(result));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public IActionResult Logout() => Ok(new { message = "logout exitoso" });

    /// <summary>§5.4: { user: UserDto }. En local, el handler ya los leyó frescos de users (no del JWT).</summary>
    [HttpGet("me")]
    [Authorize]
    [AllowDuringPasswordChange]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Me()
    {
        var user = (User)HttpContext.Items[AuthContextKeys.User]!;
        var session = HttpContext.GetSession();
        return Ok(new MeResponse(new UserDto(user.Dni, user.Name, user.Sigla, session.Role, session.MustChangePassword)));
    }

    /// <summary>§5.3: cambio obligatorio o voluntario. Devuelve token nuevo; los demás dejan de valer.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [AllowDuringPasswordChange]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        if (authSettings.Mode != AuthModes.Local)
            return NotFound(new Dictionary<string, object?>
            {
                ["error"] = AuthErrors.MsgNotAvailable,
                ["code"] = AuthErrors.NotAvailable,
            });

        var user = (User)HttpContext.Items[AuthContextKeys.User]!;
        var result = await accounts.ChangeOwnPasswordAsync(user.Dni, request, ct);
        if (result.IsSuccess) return Ok(result.Value);

        var status = AuthErrors.CodeOf(result) switch
        {
            AuthErrors.AccountLocked => StatusCodes.Status429TooManyRequests,
            AuthErrors.NotAvailable => StatusCodes.Status404NotFound,
            AuthErrors.SessionRevoked or AuthErrors.AccountSuspended => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status400BadRequest,
        };
        return StatusCode(status, ErrorBody(result));
    }

    /// <summary>
    /// sesion-larga §6.1 / §9.6: vuelve a emitir el token con el mismo <c>user</c>, <c>pca</c> y
    /// <c>auth_time</c>, recortado al tope. Solo con el header (D9). Pasa por el handler (y en local por
    /// SessionValidator) y por el gate de cambio de contraseña. Rate limit propio, no el del login.
    /// </summary>
    [HttpPost("refresh")]
    [Authorize]
    [EnableRateLimiting(RefreshRateLimit.PolicyName)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult Refresh()
    {
        if (HttpContext.Items[AuthContextKeys.TokenFromQuery] is true ||
            HttpContext.Items[AuthContextKeys.TokenPayload] is not TokenPayload payload)
            return StatusCode(StatusCodes.Status401Unauthorized, new Dictionary<string, object?>
            {
                ["error"] = AuthErrors.MsgUnauthenticated,
                ["code"] = AuthErrors.Unauthenticated,
            });

        var user = (User)HttpContext.Items[AuthContextKeys.User]!;
        var result = authService.Refresh(payload, user, HttpContext.GetSession());
        if (result.IsSuccess)
        {
            Response.Headers.CacheControl = "no-store";
            logger.LogDebug("Sesión renovada");
            return Ok(result.Value);
        }

        // S7: sin DNI ni token.
        logger.LogInformation("Renovación de sesión rechazada ({Code}, motivo {Motivo})",
            AuthErrors.CodeOf(result) ?? "-", payload.AuthTimeS is null ? "sin_auth_time" : "tope_alcanzado");
        return StatusCode(StatusCodes.Status401Unauthorized, ErrorBody(result));
    }

    /// <summary>{ error, code[, field] }: las claves del diccionario no pasan por la naming policy.</summary>
    private static Dictionary<string, object?> ErrorBody<T>(Result<T> result)
    {
        var body = new Dictionary<string, object?> { ["error"] = result.Error };
        if (result.Details is { } details)
            foreach (var (k, v) in details) body[k] = v;
        return body;
    }
}

public sealed record MeResponse(UserDto User);
