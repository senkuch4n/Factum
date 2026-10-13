namespace Factum.Agent.Common;

/// <summary>Lectura de la salida de scrcpy (grabacion-android-robusta §5.6, DA3).</summary>
internal static class ScrcpyOutput
{
    /// <summary>
    /// true si la línea indica que el celular empezó a capturar. La del servidor (<c>[server] INFO: Device:</c>)
    /// llega en tiempo real por el pipe; las del cliente (<c>Recording started to …</c>) llegan con buffer, al final.
    /// </summary>
    internal static bool IsStartMarker(string? line)
    {
        if (string.IsNullOrEmpty(line)) return false;
        var t = line.Trim();
        return t.StartsWith("[server] INFO: Device:", StringComparison.Ordinal)
            || t.Contains("Recording started to ", StringComparison.Ordinal)
            || t.StartsWith("[server] DEBUG: Display: using", StringComparison.Ordinal);
    }

    /// <summary>Líneas de debug (se loguean, pero no entran al resumen de errores).</summary>
    internal static bool IsDebugLine(string? line)
    {
        if (string.IsNullOrEmpty(line)) return false;
        var t = line.TrimStart();
        return t.StartsWith("DEBUG:", StringComparison.Ordinal)
            || t.StartsWith("[server] DEBUG:", StringComparison.Ordinal)
            || t.StartsWith("VERBOSE:", StringComparison.Ordinal)
            || t.StartsWith("[server] VERBOSE:", StringComparison.Ordinal);
    }
}
