namespace Factum.Agent.Services.Session;

/// <summary>
/// Cuentas de espacio de la grabación de la sesión (grabacion-sesion-completa §5.8, D13). Puro: el espacio libre
/// (<c>available</c>) llega de afuera y es null si no se pudo medir (fail-open: se graba igual y no se avisa).
/// </summary>
public static class SessionDiskBudget
{
    /// <summary>Tasa de una pista: max(configurada, observada en el último tramo cerrado).</summary>
    public static long Rate(long configuredPerMinute, double? observedPerMinute)
    {
        var configured = Math.Max(1, configuredPerMinute);
        if (observedPerMinute is not { } o || double.IsNaN(o) || double.IsInfinity(o) || o <= configured)
            return configured;
        return (long)Math.Ceiling(o);
    }

    /// <summary>Bytes por minuto observados en un tramo cerrado (<c>size / duración</c>); null si no se puede calcular.</summary>
    public static double? ObservedPerMinute(long? size, double? seconds) =>
        size is > 0 && seconds is > 1 ? size.Value / (seconds.Value / 60.0) : null;

    /// <summary>Para empezar: <c>minFree + 2 × segmentMinutes × R</c>.</summary>
    public static long RequiredToStart(long minFree, int segmentMinutes, long rate) =>
        Math.Max(0, minFree) + 2L * Math.Max(1, segmentMinutes) * Math.Max(1, rate);

    public static bool CanStart(long? available, long minFree, int segmentMinutes, long rate) =>
        available is not { } a || a >= RequiredToStart(minFree, segmentMinutes, rate);

    /// <summary>
    /// Estimación para la web: <c>floor((available − minFree) / (R × k))</c>, con <c>k = 2</c> si el ZIP va al mismo
    /// volumen (vuelve a ocupar lo mismo) y 1 si no. Nunca negativo; null sin medición.
    /// </summary>
    public static long? EstimatedMinutes(long? available, long minFree, long rate, bool zipOnSameVolume)
    {
        if (available is not { } a) return null;
        var k = zipOnSameVolume ? 2 : 1;
        var free = a - Math.Max(0, minFree);
        return free <= 0 ? 0 : free / (Math.Max(1, rate) * k);
    }

    /// <summary>Durante la sesión: <c>floor((available − minFree) / R)</c>. Nunca negativo; null sin medición.</summary>
    public static long? MinutesLeft(long? available, long minFree, long rate)
    {
        if (available is not { } a) return null;
        var free = a - Math.Max(0, minFree);
        return free <= 0 ? 0 : free / Math.Max(1, rate);
    }

    /// <summary>Aviso <c>disk_low</c>: quedan menos de <paramref name="warnMinutesLeft"/> minutos.</summary>
    public static bool ShouldWarn(long? minutesLeft, int warnMinutesLeft) =>
        minutesLeft is { } m && m < warnMinutesLeft;

    /// <summary>Corte limpio <c>sin_espacio</c>: <c>available &lt; minFree + R × cutReserveMinutes</c>.</summary>
    public static bool ShouldCut(long? available, long minFree, long rate, int cutReserveMinutes) =>
        available is { } a && a < Math.Max(0, minFree) + Math.Max(1, rate) * Math.Max(1, cutReserveMinutes);

    /// <summary>
    /// Se reemite el aviso cuando cruza un múltiplo de 10 minutos hacia abajo (§5.8). Sin aviso previo, siempre.
    /// </summary>
    public static bool ShouldReemit(long? previousMinutesLeft, long minutesLeft) =>
        previousMinutesLeft is not { } prev || minutesLeft / 10 < prev / 10;

    /// <summary>
    /// ¿Las dos rutas están en el mismo volumen? Raíz de la ruta y, si se puede, el nombre del volumen de
    /// <see cref="DriveInfo"/>. Ante cualquier error, true (lo conservador: cuenta la copia del ZIP).
    /// </summary>
    public static bool SameVolume(string a, string b)
    {
        try
        {
            var fa = Path.GetFullPath(a);
            var fb = Path.GetFullPath(b);
            if (OperatingSystem.IsWindows())
                return string.Equals(Path.GetPathRoot(fa), Path.GetPathRoot(fb), StringComparison.OrdinalIgnoreCase);
            // macOS/Linux: el punto de montaje más largo que contiene cada ruta (DriveInfo(ruta).Name no resuelve el
            // volumen en Unix, devuelve la ruta tal cual).
            var mounts = DriveInfo.GetDrives().Select(d => d.RootDirectory.FullName).ToList();
            return string.Equals(MountOf(fa, mounts), MountOf(fb, mounts), StringComparison.Ordinal);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Punto de montaje (de <paramref name="mounts"/>) que contiene la ruta: el prefijo más largo. Puro.</summary>
    internal static string MountOf(string fullPath, IEnumerable<string> mounts)
    {
        string best = "/";
        foreach (var m in mounts)
        {
            var root = m.EndsWith('/') ? m : m + "/";
            if ((fullPath + "/").StartsWith(root, StringComparison.Ordinal) && root.Length > best.Length) best = root;
        }
        return best;
    }
}
