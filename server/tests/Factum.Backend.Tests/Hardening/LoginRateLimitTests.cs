using System.Text.Json;
using Factum.Backend.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Backend.Tests.Hardening;

/// <summary>api-hardening §4.4.3 / T9.</summary>
public sealed class LoginRateLimitTests
{
    [Theory]
    [InlineData(30, "30 segundos")]
    [InlineData(59, "59 segundos")]
    [InlineData(1, "1 segundo")]
    [InlineData(60, "1 minuto")]
    [InlineData(61, "2 minutos")]
    [InlineData(900, "15 minutos")]
    public void Espera(int seconds, string expected) => Assert.Equal(expected, LoginRateLimit.FormatWait(seconds));

    [Fact]
    public void Mensajes_PorAlcance()
    {
        Assert.Equal("Demasiados intentos desde esta conexión. Probá de nuevo en 30 segundos.",
            LoginRateLimit.Message(LoginRateLimit.Scope.Ip, 30));
        Assert.Equal("Demasiados intentos para este DNI. Probá de nuevo en 1 minuto.",
            LoginRateLimit.Message(LoginRateLimit.Scope.Dni, 60));
    }

    [Fact]
    public void Cuerpo_ClavesExactas()
    {
        var json = JsonSerializer.Serialize(LoginRateLimit.Body(LoginRateLimit.Scope.Dni, 45),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal(["error", "code"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal("account_locked", root.GetProperty("code").GetString());
        Assert.Equal("Demasiados intentos para este DNI. Probá de nuevo en 45 segundos.",
            root.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(0.2, 1)]
    [InlineData(1.0, 1)]
    [InlineData(9.1, 10)]
    [InlineData(0.0, 1)]
    public void Segundos_HaciaArriba_Minimo1(double seconds, int expected) =>
        Assert.Equal(expected, LoginRateLimit.ToSeconds(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Rejection_429_ConRetryAfterYNoStore()
    {
        var ctx = new DefaultHttpContext();
        var result = Assert.IsType<ObjectResult>(
            LoginRateLimit.Rejection(ctx, TimeSpan.FromSeconds(12.3), LoginRateLimit.Scope.Dni, "99000010"));
        Assert.Equal(429, result.StatusCode);
        Assert.Equal("13", ctx.Response.Headers.RetryAfter.ToString());
        Assert.Equal("no-store", ctx.Response.Headers.CacheControl.ToString());
    }
}
