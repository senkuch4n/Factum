using System.Threading.RateLimiting;
using Factum.Backend.Infrastructure;

namespace Factum.Backend.Services.Auth;

/// <summary>api-hardening §4.4.2: freno del login por DNI, antes de validar credenciales.</summary>
public interface ILoginThrottle
{
    /// <returns>null si se permite; si no, el tiempo de espera.</returns>
    TimeSpan? TryAcquire(string? dni);
}

/// <summary>
/// Ventana deslizante por DNI (singleton). Cuenta todos los intentos, también los exitosos (D2 A).
/// Los limitadores inactivos los descarta el propio <see cref="PartitionedRateLimiter"/>.
/// </summary>
public sealed class LoginThrottle : ILoginThrottle, IDisposable
{
    /// <summary>Largo máximo de la clave (un DNI real tiene 7-8 dígitos; evita claves gigantes).</summary>
    public const int MaxKeyLength = 32;

    private readonly PartitionedRateLimiter<string> _limiter;
    private readonly TimeSpan _window;

    public LoginThrottle(SecuritySettings settings)
    {
        var o = settings.LoginRateLimit;
        _window = TimeSpan.FromSeconds(o.PerDniWindowSeconds);
        var window = _window;
        _limiter = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = o.PerDniPermitLimit,
                Window = window,
                SegmentsPerWindow = 6,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }

    /// <summary>Clave de partición: DNI sin espacios en los extremos, cortado a <see cref="MaxKeyLength"/>.</summary>
    public static string KeyOf(string? dni)
    {
        var key = dni?.Trim() ?? "";
        return key.Length > MaxKeyLength ? key[..MaxKeyLength] : key;
    }

    public TimeSpan? TryAcquire(string? dni)
    {
        var key = KeyOf(dni);
        if (key.Length == 0) return null; // el login va a fallar igual por validación
        using var lease = _limiter.AttemptAcquire(key);
        return lease.IsAcquired ? null : LoginRateLimit.RetryAfterOf(lease, _window);
    }

    public void Dispose() => _limiter.Dispose();
}
