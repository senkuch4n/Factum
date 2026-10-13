using System.Net;
using System.Text.Json;
using Factum.Backend.Infrastructure;
using Factum.Backend.Services.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Factum.Backend.Tests.Hardening;

/// <summary>
/// api-hardening §8.2 / T11: pipeline real (<c>AddFactumEdge</c>/<c>UseFactumEdge</c>, las mismas extensiones
/// que <c>Program.cs</c>) sobre un <see cref="WebApplication"/> mínimo con <see cref="TestServer"/>. Sin Mongo.
/// </summary>
public sealed class FactumEdgeTests
{
    private const string Origin = "http://127.0.0.1:3000";
    private static readonly IPAddress Untrusted = IPAddress.Parse("203.0.113.5");

    private static async Task<WebApplication> StartAsync(string environment = "Production", int perIp = 2)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var security = SecuritySettings.Defaults with
        {
            LoginRateLimit = new LoginRateLimitOptions { PerIpPermitLimit = perIp, PerIpWindowSeconds = 60 },
        };
        builder.Services.AddFactumEdge([Origin], security);

        var app = builder.Build();
        app.UseFactumEdge();
        app.MapGet("/ok", () => Results.Json(new { ok = true }));
        app.MapGet("/boom", string () => throw new InvalidOperationException("secreto interno"));
        app.MapPost("/big", string () => throw new BadHttpRequestException("x", StatusCodes.Status413PayloadTooLarge));
        app.MapPost("/slow", string () => throw new BadHttpRequestException("x", StatusCodes.Status408RequestTimeout));
        app.MapPost("/bad", string () => throw new BadHttpRequestException("x", StatusCodes.Status400BadRequest));
        app.MapGet("/preview", (HttpContext c) =>
        {
            c.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            return Results.Text("img");
        });
        app.MapPost("/login", (HttpContext c) => Results.Json(new { ip = c.Connection.RemoteIpAddress?.ToString() }))
            .RequireRateLimiting(LoginRateLimit.PolicyName);
        app.MapGet("/swagger/x", () => Results.Text("ui"));
        await app.StartAsync();
        return app;
    }

    private static HttpRequestMessage Get(string path, string? origin = Origin)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (origin is not null) req.Headers.Add("Origin", origin);
        return req;
    }

    private static string Header(HttpResponseMessage r, string name) =>
        r.Headers.TryGetValues(name, out var v) ? string.Join(",", v)
        : r.Content.Headers.TryGetValues(name, out var c) ? string.Join(",", c) : "";

    private static void AssertSecurityHeaders(HttpResponseMessage r, bool csp = true)
    {
        Assert.Equal("nosniff", Header(r, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(r, "X-Frame-Options"));
        Assert.Equal("no-referrer", Header(r, "Referrer-Policy"));
        Assert.Equal(csp ? SecurityHeadersMiddleware.ApiContentSecurityPolicy : "", Header(r, "Content-Security-Policy"));
        Assert.Equal("", Header(r, "Server"));
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    // ── E1-E5, E8: headers ───────────────────────────────────────────────────

    [Fact]
    public async Task E1_Ok_TraeHeadersDeSeguridad_SinServer()
    {
        await using var app = await StartAsync();
        var r = await app.GetTestClient().SendAsync(Get("/ok"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        AssertSecurityHeaders(r);
        Assert.Equal(Origin, Header(r, "Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task E2_Excepcion_500Json_ConCors_SinInternals(string environment)
    {
        await using var app = await StartAsync(environment);
        var r = await app.GetTestClient().SendAsync(Get("/boom"));
        Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode);
        Assert.Equal(Origin, Header(r, "Access-Control-Allow-Origin"));
        Assert.Equal("application/json; charset=utf-8", Header(r, "Content-Type"));
        Assert.Equal("no-store", Header(r, "Cache-Control"));
        AssertSecurityHeaders(r);

        var raw = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secreto interno", raw);
        Assert.DoesNotContain("InvalidOperationException", raw);
        Assert.DoesNotContain("   at ", raw);

        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(["error", "code", "trace_id"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(ApiErrorMiddleware.MsgInternal, body.GetProperty("error").GetString());
        Assert.Equal("internal_error", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("trace_id").GetString()));
    }

    [Theory]
    [InlineData("/big", 413, "payload_too_large", ApiErrorMiddleware.MsgPayloadTooLarge)]
    [InlineData("/slow", 408, "request_timeout", ApiErrorMiddleware.MsgRequestTimeout)]
    [InlineData("/bad", 400, "bad_request", ApiErrorMiddleware.MsgBadRequest)]
    public async Task E3_BadHttpRequest_SuStatus_Json_ConCors(string path, int status, string code, string message)
    {
        await using var app = await StartAsync("Development");
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent("{}") };
        req.Headers.Add("Origin", Origin);
        var r = await app.GetTestClient().SendAsync(req);
        Assert.Equal(status, (int)r.StatusCode);
        Assert.Equal(Origin, Header(r, "Access-Control-Allow-Origin"));
        AssertSecurityHeaders(r);
        var raw = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("   at ", raw);
        Assert.DoesNotContain("BadHttpRequestException", raw);
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal(message, body.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("trace_id").GetString()));
    }

    [Fact]
    public async Task E4_Preview_ConservaSuCsp()
    {
        await using var app = await StartAsync();
        var r = await app.GetTestClient().SendAsync(Get("/preview"));
        Assert.Equal("default-src 'none'; sandbox", Header(r, "Content-Security-Policy"));
        Assert.Equal("nosniff", Header(r, "X-Content-Type-Options"));
    }

    [Fact]
    public async Task E5_Swagger_SinCsp_ConNosniff()
    {
        await using var app = await StartAsync();
        var r = await app.GetTestClient().SendAsync(Get("/swagger/x"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        AssertSecurityHeaders(r, csp: false);
    }

    [Fact]
    public async Task E8_RutaInexistente_404_ConHeaders()
    {
        await using var app = await StartAsync();
        var r = await app.GetTestClient().SendAsync(Get("/api/no-existe"));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        AssertSecurityHeaders(r);
    }

    [Fact]
    public async Task Cors_OrigenNoPermitido_SinAcao_PeroConHeaders()
    {
        await using var app = await StartAsync();
        var r = await app.GetTestClient().SendAsync(Get("/boom", "http://evil.example"));
        Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode);
        Assert.Equal("", Header(r, "Access-Control-Allow-Origin"));
        AssertSecurityHeaders(r);
    }

    // ── E6-E7: rate limit por IP ─────────────────────────────────────────────

    private static Task<HttpContext> LoginFrom(WebApplication app, IPAddress remote, string? xff = null) =>
        app.GetTestServer().SendAsync(c =>
        {
            c.Request.Method = HttpMethods.Post;
            c.Request.Path = "/login";
            c.Connection.RemoteIpAddress = remote;
            if (xff is not null) c.Request.Headers["X-Forwarded-For"] = xff;
        });

    private static async Task<JsonElement> BodyOf(HttpContext c)
    {
        return (await JsonDocument.ParseAsync(c.Response.Body)).RootElement;
    }

    [Fact]
    public async Task E6_TercerLoginMismaIp_429_RetryAfter_AccountLocked()
    {
        await using var app = await StartAsync(perIp: 2);
        var client = app.GetTestClient();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 3; i++)
            last = await client.PostAsync("/login", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        var retryAfter = int.Parse(Header(last, "Retry-After"));
        Assert.InRange(retryAfter, 1, 60);
        Assert.Equal("no-store", Header(last, "Cache-Control"));
        AssertSecurityHeaders(last);
        var body = await JsonOf(last);
        Assert.Equal(["error", "code"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal("account_locked", body.GetProperty("code").GetString());
        Assert.StartsWith("Demasiados intentos desde esta conexión. Probá de nuevo en ",
            body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task E7_IpNoConfiable_IgnoraXff_ElLimiteSeAplica()
    {
        await using var app = await StartAsync(perIp: 2);
        var statuses = new List<int>();
        for (var i = 0; i < 3; i++)
            statuses.Add((await LoginFrom(app, Untrusted, $"198.51.100.{i + 1}")).Response.StatusCode);
        Assert.Equal([200, 200, 429], statuses);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]   // IPv4 mapeada (Kestrel en ListenAnyIP): también confiable
    [InlineData("172.18.0.3")]         // red Docker
    [InlineData("::1")]
    public async Task E7_IpConfiable_XffDistintos_ParticionesSeparadas(string proxy)
    {
        await using var app = await StartAsync(perIp: 2);
        for (var i = 0; i < 4; i++)
        {
            var c = await LoginFrom(app, IPAddress.Parse(proxy), $"198.51.100.{i + 1}");
            Assert.Equal(200, c.Response.StatusCode);
            Assert.Equal($"198.51.100.{i + 1}", (await BodyOf(c)).GetProperty("ip").GetString());
        }
    }

    [Fact]
    public async Task E7_IpConfiable_MismoXff_MismaParticion()
    {
        await using var app = await StartAsync(perIp: 2);
        var statuses = new List<int>();
        for (var i = 0; i < 3; i++)
            statuses.Add((await LoginFrom(app, IPAddress.Loopback, "198.51.100.7")).Response.StatusCode);
        Assert.Equal([200, 200, 429], statuses);
    }

    [Fact]
    public async Task RateLimit_SoloEnElLogin()
    {
        await using var app = await StartAsync(perIp: 1);
        var client = app.GetTestClient();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Get("/ok"))).StatusCode);
    }
}
