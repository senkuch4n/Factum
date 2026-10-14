using System.Globalization;

namespace Factum.Backend.Services.Timestamping;

/// <summary>
/// Formato único de una fecha certificada (sellado-tiempo §8.4): lo usan el informe, <c>gen_time_display</c> y el
/// LEEME. <c>dd/MM/yyyy HH:mm:ss (UTC±HH:mm)</c> en la zona del informe (<c>Report:TimeZone</c>), con el offset de
/// esa zona en ese instante. Ejemplo: <c>14/10/2026 09:46:05 (UTC-03:00)</c>.
/// </summary>
public static class TimestampFormat
{
    public static string Display(DateTime utc, TimeZoneInfo zone)
    {
        var u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(u, zone);
        var offset = zone.GetUtcOffset(u);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        return local.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture) +
               $" (UTC{sign}{abs.Hours:00}:{abs.Minutes:00})";
    }

    /// <summary><c>yyyy-MM-dd HH:mm:ss</c> en UTC, sin sufijo (el LEEME agrega " UTC").</summary>
    public static string Utc(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
