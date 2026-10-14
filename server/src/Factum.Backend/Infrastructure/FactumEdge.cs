using System.Threading.RateLimiting;
using Factum.Backend.Services.Auth;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace Factum.Backend.Infrastructure;

/// <summary>
/// api-hardening §4.1.1: el "borde" del pipeline (IP real, headers de seguridad, CORS, errores uniformes y
/// rate limit del login), en dos extensiones que usan tanto <c>Program.cs</c> como los tests de
/// integración: así el orden probado es el real.
/// </summary>
public static class FactumEdge
{
    public static IServiceCollection AddFactumEdge(this IServiceCollection services,
        IReadOnlyList<string> corsOrigins, SecuritySettings security)
    {
        services.AddSingleton(security);

        // CORS (zip-local-informe-servidor §5.9): solo los orígenes de Cors:AllowedOrigins. Sin
        // credenciales: el token viaja en el header Authorization.
        services.AddCors(o => o.AddDefaultPolicy(p =>
            p.WithOrigins([.. corsOrigins]).AllowAnyHeader().AllowAnyMethod()));

        // IP real detrás de Caddy (§4.5, D4 A): solo se cree el X-Forwarded-For que llega desde un proxy
        // confiable (loopback, RFC 1918, ULA, o lo que diga Security:ForwardedHeaders:KnownNetworks).
        // Las IPv4 mapeadas en IPv6 las normaliza el propio middleware antes de comparar.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var network in security.KnownNetworks) options.KnownIPNetworks.Add(network);
        });

        // Rate limit del login por IP (§4.4.1). Sin GlobalLimiter: solo los endpoints con la política.
        var rl = security.LoginRateLimit;
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LoginRateLimit.PolicyName, ctx =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = rl.PerIpPermitLimit,
                        Window = TimeSpan.FromSeconds(rl.PerIpWindowSeconds),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
            // sesion-larga §9.5: rate limit propio del refresh (por hash del token, ventana fija 6/60 s).
            o.AddPolicy(RefreshRateLimit.PolicyName, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RefreshRateLimit.PartitionKey(ctx),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = RefreshRateLimit.PermitLimit,
                        Window = TimeSpan.FromSeconds(RefreshRateLimit.WindowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
            // El cuerpo del 429 depende de la política del endpoint: el del login queda igual que antes.
            o.OnRejected = (ctx, ct) =>
                ctx.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName
                    == RefreshRateLimit.PolicyName
                    ? new ValueTask(RefreshRateLimit.WriteRejectionAsync(ctx.HttpContext, ctx.Lease, ct))
                    : new ValueTask(LoginRateLimit.WriteRejectionAsync(
                        ctx.HttpContext, ctx.Lease, LoginRateLimit.Scope.Ip, ct));
        });

        // Freno por DNI (§4.4.2), en AuthController antes de validar credenciales.
        services.AddSingleton<ILoginThrottle, LoginThrottle>();
        return services;
    }

    /// <summary>Orden de §4.1: forwarded headers → headers de seguridad → CORS → errores → rate limit.</summary>
    public static IApplicationBuilder UseFactumEdge(this IApplicationBuilder app)
    {
        app.UseForwardedHeaders();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseCors();
        app.UseMiddleware<ApiErrorMiddleware>();
        app.UseRateLimiter();
        return app;
    }
}
