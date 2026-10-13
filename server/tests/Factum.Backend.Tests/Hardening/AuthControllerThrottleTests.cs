using Factum.Backend.Common;
using Factum.Backend.Controllers;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Backend.Tests.Hardening;

/// <summary>api-hardening §4.4.2 / T10: el freno por DNI corta ANTES de validar credenciales.</summary>
public sealed class AuthControllerThrottleTests
{
    private sealed class CountingAuthService : IAuthService
    {
        public int LoginCalls { get; private set; }
        public string Mode => AuthModes.Dev;

        public Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
        {
            LoginCalls++;
            return Task.FromResult(Result.Ok(new LoginResponse("tok",
                new UserDto(request.Dni, "QA", "QA", UserRoles.Cliente, false))));
        }

        public TokenPayload? ValidateToken(string token) => null;
        public string IssueToken(User user, DateTime? passwordChangedAt) => "tok";
    }

    [Fact]
    public async Task SegundoLoginDelMismoDni_429_SinLlamarAlAuthService()
    {
        var auth = new CountingAuthService();
        using var throttle = new LoginThrottle(SecuritySettings.Defaults with
        {
            LoginRateLimit = new LoginRateLimitOptions { PerDniPermitLimit = 1 },
        });
        var controller = new AuthController(auth, new AuthSettings(AuthModes.Dev, null, null, [], []), null!, throttle)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var first = await controller.Login(new LoginRequest("99000010", "x"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(first);

        var second = await controller.Login(new LoginRequest(" 99000010 ", "x"), CancellationToken.None);
        var rejected = Assert.IsType<ObjectResult>(second);
        Assert.Equal(429, rejected.StatusCode);
        var retryAfter = int.Parse(controller.HttpContext.Response.Headers.RetryAfter.ToString());
        Assert.InRange(retryAfter, 1, 60);
        var body = Assert.IsType<Dictionary<string, object?>>(rejected.Value);
        Assert.Equal("account_locked", body["code"]);
        Assert.StartsWith("Demasiados intentos para este DNI.", (string)body["error"]!);

        Assert.Equal(1, auth.LoginCalls);
    }
}
