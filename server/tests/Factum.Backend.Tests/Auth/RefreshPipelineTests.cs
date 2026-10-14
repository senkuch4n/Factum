using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Factum.Backend.Controllers;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Factum.Backend.Tests.Auth;

/// <summary>
/// sesion-larga §11.1: <c>POST /api/auth/refresh</c> por el pipeline real (borde, <see cref="FactumBearerHandler"/>,
/// SessionValidator en modo local, gate de cambio de contraseña y <see cref="AuthController"/>) sobre
/// <see cref="TestServer"/>, con el repositorio de users en memoria. Sin Mongo.
/// </summary>
public sealed class RefreshPipelineTests
{
    private const string Dni = "30111222";
    private const string Password = "temporal-123";

    private sealed class CapturingProvider : ILoggerProvider
    {
        public ConcurrentQueue<(string Category, LogLevel Level, string Message)> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, this);
        public void Dispose() { }

        private sealed class Logger(string category, CapturingProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                owner.Entries.Enqueue((category, logLevel, formatter(state, exception)));
        }
    }

    private sealed record Harness(WebApplication App, LocalAuthFixture F, CapturingProvider Logs) : IAsyncDisposable
    {
        public HttpClient Client => App.GetTestClient();
        public ValueTask DisposeAsync() => App.DisposeAsync();
    }

    private static async Task<Harness> StartAsync(bool mustChange = false)
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync(Dni, Password, mustChange: mustChange);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var logs = new CapturingProvider();
        builder.Logging.AddProvider(logs);
        builder.Logging.AddFilter<CapturingProvider>(null, LogLevel.Debug);

        // Freno por DNI con 1 permiso por minuto: se agota antes de renovar.
        var security = SecuritySettings.Defaults with
        {
            LoginRateLimit = new LoginRateLimitOptions { PerDniPermitLimit = 1, PerDniWindowSeconds = 60 },
        };
        builder.Services.AddFactumEdge(["http://127.0.0.1:3000"], security);
        builder.Services.AddSingleton(f.Settings);
        builder.Services.AddSingleton<IAuthService>(f.Auth);
        builder.Services.AddSingleton<ISessionValidator>(f.Sessions);
        builder.Services.AddSingleton<IUserAccountService>(f.Accounts);
        builder.Services.AddAuthentication("FactumBearerScheme")
            .AddScheme<AuthenticationSchemeOptions, FactumBearerHandler>("FactumBearerScheme", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(AuthController).Assembly)
            .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);

        var app = builder.Build();
        app.UseFactumEdge();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<PasswordChangeGateMiddleware>();
        app.MapControllers();
        await app.StartAsync();
        return new Harness(app, f, logs);
    }

    private static async Task<string> LoginAsync(Harness h)
    {
        var r = await h.F.Auth.LoginAsync(new LoginRequest(Dni, Password));
        Assert.True(r.IsSuccess, r.Error);
        return r.Value!.Token;
    }

    private static HttpRequestMessage Refresh(string? token, bool viaQuery = false)
    {
        var url = "/api/auth/refresh" + (viaQuery && token is not null ? "?token=" + Uri.EscapeDataString(token) : "");
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (!viaQuery && token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    private static async Task AssertError(HttpResponseMessage r, HttpStatusCode status, string code)
    {
        Assert.Equal(status, r.StatusCode);
        var body = await JsonOf(r);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Exito_200_SeisCamposSnakeCase_NoStore()
    {
        await using var h = await StartAsync();
        var token = await LoginAsync(h);
        h.F.Time.Advance(TimeSpan.FromHours(1));

        var r = await h.Client.SendAsync(Refresh(token));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
        var body = await JsonOf(r);
        Assert.Equal(["token", "user", "expires_in", "session_max_in", "token_ttl", "session_max_age"],
            body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(8 * 3600, body.GetProperty("expires_in").GetInt32());
        Assert.Equal(11 * 3600, body.GetProperty("session_max_in").GetInt32());
        Assert.Equal(8 * 3600, body.GetProperty("token_ttl").GetInt32());
        Assert.Equal(12 * 3600, body.GetProperty("session_max_age").GetInt32());
        Assert.Equal(Dni, body.GetProperty("user").GetProperty("dni").GetString());

        var renewed = h.F.Auth.ValidateToken(body.GetProperty("token").GetString()!)!;
        var original = h.F.Auth.ValidateToken(token)!;
        Assert.Equal(original.AuthTimeS, renewed.AuthTimeS);
        Assert.Equal(original.PasswordChangedAtMs, renewed.PasswordChangedAtMs);

        // S7: Debug sin DNI ni token.
        var line = Assert.Single(h.Logs.Entries, e => e.Message == "Sesión renovada");
        Assert.Equal(LogLevel.Debug, line.Level);
        Assert.Equal(typeof(AuthController).FullName, line.Category);
    }

    [Fact]
    public async Task CuentaSuspendida_401_AccountSuspended()
    {
        await using var h = await StartAsync();
        var token = await LoginAsync(h);
        var acc = h.F.Repo.Get(Dni)!;
        acc.Status = UserStatuses.Suspendido;
        h.F.Repo.Seed(acc);

        await AssertError(await h.Client.SendAsync(Refresh(token)), HttpStatusCode.Unauthorized, "account_suspended");
    }

    [Fact]
    public async Task ContrasenaCambiada_401_SessionRevoked()
    {
        await using var h = await StartAsync();
        var token = await LoginAsync(h);
        var acc = h.F.Repo.Get(Dni)!;
        acc.PasswordChangedAt = acc.PasswordChangedAt.AddMinutes(5);
        h.F.Repo.Seed(acc);

        await AssertError(await h.Client.SendAsync(Refresh(token)), HttpStatusCode.Unauthorized, "session_revoked");
    }

    [Fact]
    public async Task TokenVencido_401_InvalidToken()
    {
        await using var h = await StartAsync();
        var token = await LoginAsync(h);
        h.F.Time.Advance(TimeSpan.FromHours(8).Add(TimeSpan.FromSeconds(1)));

        await AssertError(await h.Client.SendAsync(Refresh(token)), HttpStatusCode.Unauthorized, "invalid_token");
    }

    [Fact]
    public async Task TokenPorQuery_401_Unauthenticated()
    {
        await using var h = await StartAsync();
        var token = await LoginAsync(h);

        await AssertError(await h.Client.SendAsync(Refresh(token, viaQuery: true)),
            HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task SinToken_401_Unauthenticated()
    {
        await using var h = await StartAsync();
        await AssertError(await h.Client.SendAsync(Refresh(null)), HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task TokenSinAuthTime_401_SessionMaxAge_YLogSinDni()
    {
        await using var h = await StartAsync();
        // Formato de antes de sesion-larga: user + pca, sin auth_time.
        var acc = h.F.Repo.Get(Dni)!;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(LocalAuthFixture.Secret));
        var old = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims:
            [
                new Claim("user", JsonSerializer.Serialize(new User { Dni = acc.Dni, Name = acc.Name, Sigla = acc.Sigla })),
                new Claim("pca", AuthTime.ToUnixMs(acc.PasswordChangedAt).ToString()),
            ],
            expires: h.F.Now.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));

        await AssertError(await h.Client.SendAsync(Refresh(old)), HttpStatusCode.Unauthorized, "session_max_age");

        var line = Assert.Single(h.Logs.Entries, e => e.Message.StartsWith("Renovación de sesión rechazada"));
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Equal("Renovación de sesión rechazada (session_max_age, motivo sin_auth_time)", line.Message);
        Assert.DoesNotContain(h.Logs.Entries, e => e.Message.Contains(Dni) || e.Message.Contains(old));
    }

    [Fact]
    public async Task CambioDeContrasenaPendiente_403()
    {
        await using var h = await StartAsync(mustChange: true);
        var token = await LoginAsync(h);
        await AssertError(await h.Client.SendAsync(Refresh(token)), HttpStatusCode.Forbidden, "password_change_required");
    }

    [Fact]
    public async Task ThrottleDelLoginAgotado_NoImpideElRefresh_NiLoConsume()
    {
        await using var h = await StartAsync();
        var token = await LoginAsync(h);
        var throttle = h.App.Services.GetRequiredService<ILoginThrottle>();
        Assert.Null(throttle.TryAcquire(Dni));    // único permiso del minuto
        Assert.NotNull(throttle.TryAcquire(Dni)); // agotado

        Assert.Equal(HttpStatusCode.OK, (await h.Client.SendAsync(Refresh(token))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.SendAsync(Refresh(token))).StatusCode);
    }
}
