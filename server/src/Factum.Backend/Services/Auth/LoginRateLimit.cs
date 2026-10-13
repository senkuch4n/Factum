using System.Globalization;
using System.Threading.RateLimiting;
using Factum.Backend.Common;
using Factum.Backend.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Factum.Backend.Services.Auth;

/// <summary>
/// api-hardening §4.4.3: respuesta 429 común a los dos frenos del login (por IP, en el middleware de
/// rate limiting; por DNI, en <c>AuthController</c>). Cuerpo <c>{ error, code: "account_locked" }</c>
/// (claves literales) para que el cliente actual lo muestre como cuenta bloqueada temporalmente.
/// </summary>
public static class LoginRateLimit
{
    public const string PolicyName = "login";
    public const string Code = "account_locked";
    public const string LogCategory = "Factum.Backend.RateLimit";

    public enum Scope { Ip, Dni }

    /// <summary>"N segundos" si N &lt; 60; si no, "M minuto(s)" con M = ceil(N/60).</summary>
    public static string FormatWait(int seconds)
    {
        if (seconds < 60) return seconds == 1 ? "1 segundo" : $"{seconds} segundos";
        var minutes = (seconds + 59) / 60;
        return minutes == 1 ? "1 minuto" : $"{minutes} minutos";
    }

    public static string Message(Scope scope, int seconds) => scope == Scope.Ip
        ? $"Demasiados intentos desde esta conexión. Probá de nuevo en {FormatWait(seconds)}."
        : $"Demasiados intentos para este DNI. Probá de nuevo en {FormatWait(seconds)}.";

    /// <summary>Cuerpo del 429 con claves literales (no pasan por la naming policy).</summary>
    public static Dictionary<string, object?> Body(Scope scope, int seconds) => new()
    {
        ["error"] = Message(scope, seconds),
        ["code"] = Code,
    };

    /// <summary>Segundos enteros, redondeados hacia arriba, mínimo 1.</summary>
    public static int ToSeconds(TimeSpan wait) => Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));

    /// <summary>Espera de un lease rechazado: su metadata <c>RetryAfter</c> o, si no viene, la ventana completa.</summary>
    public static TimeSpan RetryAfterOf(RateLimitLease lease, TimeSpan window) =>
        lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) && retryAfter > TimeSpan.Zero
            ? retryAfter
            : window;

    /// <summary>Para <c>RateLimiterOptions.OnRejected</c> (freno por IP; no se leyó el body).</summary>
    public static async Task WriteRejectionAsync(HttpContext ctx, RateLimitLease lease, Scope scope,
        CancellationToken ct)
    {
        var settings = ctx.RequestServices.GetService<SecuritySettings>() ?? SecuritySettings.Defaults;
        var window = TimeSpan.FromSeconds(scope == Scope.Ip
            ? settings.LoginRateLimit.PerIpWindowSeconds
            : settings.LoginRateLimit.PerDniWindowSeconds);
        var seconds = ToSeconds(RetryAfterOf(lease, window));
        ApplyHeaders(ctx, seconds);
        Log(ctx, scope, null, seconds);
        ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await ctx.Response.WriteAsJsonAsync(Body(scope, seconds), ct);
    }

    /// <summary>Para el controller (freno por DNI): pone los headers y devuelve el 429.</summary>
    public static IActionResult Rejection(HttpContext ctx, TimeSpan wait, Scope scope, string? dni = null)
    {
        var seconds = ToSeconds(wait);
        ApplyHeaders(ctx, seconds);
        Log(ctx, scope, dni, seconds);
        return new ObjectResult(Body(scope, seconds)) { StatusCode = StatusCodes.Status429TooManyRequests };
    }

    private static void ApplyHeaders(HttpContext ctx, int seconds)
    {
        ctx.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        ctx.Response.Headers.CacheControl = "no-store";
    }

    // Nunca la contraseña. El DNI se recorta y se limpia (es entrada del usuario y va al log).
    private static void Log(HttpContext ctx, Scope scope, string? dni, int seconds)
    {
        var logger = ctx.RequestServices?.GetService<ILoggerFactory>()?.CreateLogger(LogCategory)
                     ?? NullLogger.Instance;
        var safeDni = string.IsNullOrWhiteSpace(dni) ? "-" : LoginThrottle.KeyOf(SafeText.Clean(dni, TextShape.SingleLine));
        if (safeDni.Length == 0) safeDni = "-";
        logger.LogWarning("Login frenado por rate limit ({Scope}) ip={Ip} dni={Dni} retry_after={Segundos}",
            scope == Scope.Ip ? "ip" : "dni", ctx.Connection.RemoteIpAddress?.ToString() ?? "-", safeDni, seconds);
    }
}
