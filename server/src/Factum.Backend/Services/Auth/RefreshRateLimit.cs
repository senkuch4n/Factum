using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Factum.Backend.Services.Auth;

/// <summary>
/// sesion-larga §9.5 / D10: rate limit propio de <c>POST /api/auth/refresh</c>, aparte del login. Ventana
/// fija de <see cref="PermitLimit"/> por <see cref="WindowSeconds"/> por token. La partición es un hash del
/// token (nunca el token crudo: no queda como clave de diccionario ni en un log). Corre antes de la
/// autenticación, así que no puede usar el DNI.
/// </summary>
public static class RefreshRateLimit
{
    public const string PolicyName = "refresh";
    public const string Code = "rate_limited";
    public const int PermitLimit = 6;
    public const int WindowSeconds = 60;
    /// <summary>Misma categoría que los demás logs del refresh (S7).</summary>
    public const string LogCategory = "Factum.Backend.Controllers.AuthController";

    /// <summary><c>tok:&lt;sha256-hex[..32]&gt;</c> con el header Bearer; si no hay, <c>anon:&lt;ip&gt;</c>.</summary>
    public static string PartitionKey(HttpContext ctx)
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = header["Bearer ".Length..].Trim();
            if (token.Length > 0)
                return "tok:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)))[..32];
        }
        return "anon:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }

    public static string Message(int seconds) =>
        $"Demasiadas renovaciones de sesión. Probá de nuevo en {LoginRateLimit.FormatWait(seconds)}.";

    /// <summary>Cuerpo del 429 con claves literales (no pasan por la naming policy).</summary>
    public static Dictionary<string, object?> Body(int seconds) => new()
    {
        ["error"] = Message(seconds),
        ["code"] = Code,
    };

    /// <summary>Para <c>RateLimiterOptions.OnRejected</c> cuando el endpoint tiene la política <c>refresh</c>.</summary>
    public static async Task WriteRejectionAsync(HttpContext ctx, RateLimitLease lease, CancellationToken ct)
    {
        var seconds = LoginRateLimit.ToSeconds(LoginRateLimit.RetryAfterOf(lease, TimeSpan.FromSeconds(WindowSeconds)));
        ctx.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ctx.Response.Headers.CacheControl = "no-store";
        // S7: sin IP, sin token y sin DNI (el DNI no se conoce a esta altura).
        var logger = ctx.RequestServices?.GetService<ILoggerFactory>()?.CreateLogger(LogCategory)
                     ?? NullLogger.Instance;
        logger.LogInformation("Renovación de sesión frenada por rate limit retry_after={Segundos}", seconds);
        ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await ctx.Response.WriteAsJsonAsync(Body(seconds), ct);
    }
}
