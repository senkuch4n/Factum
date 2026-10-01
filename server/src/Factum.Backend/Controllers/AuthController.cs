using Factum.Backend.DTOs;
using Factum.Backend.Services.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Backend.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [HttpGet("mode")]
    public IActionResult Mode() => Ok(new { mode = authService.Mode });

    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request, ct);
        return result.Match<IActionResult>(
            onSuccess: Ok,
            onFailure: err => Unauthorized(new { error = err }));
    }

    [HttpPost("logout")]
    public IActionResult Logout() => Ok(new { message = "logout exitoso" });

    [HttpGet("me")]
    public IActionResult Me()
    {
        var user = HttpContext.Items["User"];
        return user is null ? Unauthorized() : Ok(new { user });
    }
}
