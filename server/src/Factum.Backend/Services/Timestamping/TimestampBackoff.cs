namespace Factum.Backend.Services.Timestamping;

/// <summary>
/// Espera entre reintentos de la cola (sellado-tiempo §6.4), pura. <c>attempts</c> = intentos ya hechos (≥ 1).
/// Minutos: 1, 2, 5, 10, 30, 60, 120, 240, 480, 720, 1440 y 1440 de ahí en adelante, con jitter de ±10 %.
/// Con <c>MaxAttempts</c> = 12, el último intento cae a unas 52 h del primero.
/// </summary>
public static class TimestampBackoff
{
    public static readonly IReadOnlyList<int> Minutes = [1, 2, 5, 10, 30, 60, 120, 240, 480, 720, 1440];
    public const double Jitter = 0.10;

    /// <summary>Espera base (sin jitter) después de <paramref name="attempts"/> intentos.</summary>
    public static TimeSpan Base(int attempts)
    {
        var i = Math.Clamp(attempts, 1, int.MaxValue) - 1;
        return TimeSpan.FromMinutes(Minutes[Math.Min(i, Minutes.Count - 1)]);
    }

    /// <summary>Espera con jitter uniforme en [-10 %, +10 %].</summary>
    public static TimeSpan Delay(int attempts, Random? random = null)
    {
        var r = (random ?? Random.Shared).NextDouble(); // [0, 1)
        var factor = 1 + (r * 2 - 1) * Jitter;
        return TimeSpan.FromTicks((long)(Base(attempts).Ticks * factor));
    }

    /// <summary>true si con <paramref name="attempts"/> intentos hechos se agotó el tope: el sello pasa a failed.</summary>
    public static bool IsFinal(int attempts, int maxAttempts) => attempts >= maxAttempts;
}
