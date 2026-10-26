namespace Factum.Agent.Common;

/// <summary>
/// Utilidades compartidas para `adb shell` (extraccion-logica-android §3). Se movió <see cref="RemoteQuote"/> desde
/// <c>AdbService</c> (antes privado) para que lo use también el servicio de extracción. <c>adb shell</c> une los
/// argumentos y los interpreta en el shell del dispositivo (ahí hace falta citar), mientras que <c>adb exec-out</c>
/// y <c>adb pull</c> no pasan por el shell (no hace falta).
/// </summary>
public static class AdbShell
{
    /// <summary>Cita un argumento para el shell del dispositivo con comillas simples (POSIX).</summary>
    public static string RemoteQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
