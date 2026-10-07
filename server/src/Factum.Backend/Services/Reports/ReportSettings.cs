using Microsoft.Extensions.Options;

namespace Factum.Backend.Services.Reports;

/// <summary>Config <c>Report</c> ya resuelta (zona horaria, domicilio, textos por defecto).</summary>
public interface IReportSettings
{
    TimeZoneInfo Zone { get; }
    /// <summary>p. ej. "America/Argentina/Buenos_Aires (UTC-03:00)".</summary>
    string ZoneLabel { get; }
    /// <summary>Domicilio constituido normalizado; "" si no está configurado.</summary>
    string DomicilioConstituido { get; }
    /// <summary><c>Report:EncryptZip</c>: si el ZIP de evidencia se cifra con AES-256.</summary>
    bool EncryptZip { get; }
    string DefaultOperacionesRealizadas { get; }
    string DefaultAseguramientoEvidencia { get; }
    string DefaultNotasTecnicas { get; }
    string DefaultReserva { get; }
}

/// <summary>
/// Lee la sección <c>Report</c> UNA vez (singleton resuelto al arrancar en Program.cs, así los
/// warnings salen en el log de inicio). Nunca tira: una zona inválida cae a UTC-03:00 fijo
/// (Argentina no tiene horario de verano desde 2009).
/// </summary>
public sealed class ReportSettings : IReportSettings
{
    public const int MaxDomicilioLength = 300;
    private static readonly TimeSpan FallbackOffset = TimeSpan.FromHours(-3);

    public TimeZoneInfo Zone { get; }
    public string ZoneLabel { get; }
    public string DomicilioConstituido { get; }
    public bool EncryptZip { get; }
    public string DefaultOperacionesRealizadas { get; }
    public string DefaultAseguramientoEvidencia { get; }
    public string DefaultNotasTecnicas { get; }
    public string DefaultReserva { get; }

    public ReportSettings(IOptions<ReportOptions> options, ILogger<ReportSettings> logger)
    {
        var o = options.Value ?? new ReportOptions();

        var tzId = o.TimeZone?.Trim() ?? "";
        TimeZoneInfo? zone = null;
        if (tzId.Length > 0)
        {
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(tzId); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                logger.LogWarning("Report: zona horaria '{Zone}' no encontrada; se usa UTC-03:00 fijo", tzId);
            }
        }
        else
        {
            logger.LogWarning("Report: TimeZone vacío; se usa UTC-03:00 fijo");
        }

        if (zone is null)
        {
            Zone = TimeZoneInfo.CreateCustomTimeZone("UTC-03:00", FallbackOffset, "UTC-03:00", "UTC-03:00");
            ZoneLabel = "UTC-03:00";
        }
        else
        {
            Zone = zone;
            ZoneLabel = $"{tzId} (UTC{FormatOffset(zone.BaseUtcOffset)})";
        }

        var dom = o.DomicilioConstituido?.Trim() ?? "";
        if (dom.Length > MaxDomicilioLength)
        {
            logger.LogWarning("Report: DomicilioConstituido supera {Max} caracteres; se trunca", MaxDomicilioLength);
            dom = dom[..MaxDomicilioLength].TrimEnd();
        }
        DomicilioConstituido = dom;

        EncryptZip = o.EncryptZip;
        if (!EncryptZip)
            logger.LogWarning("Report: EncryptZip=false; los ZIP de evidencia se generan SIN cifrar");

        // El override de AseguramientoEvidencia gana en los dos modos (DT5): si el estudio lo
        // define, es responsable de que hable (o no) del cifrado.
        var d = o.DefaultTexts ?? new ReportDefaultTextsOptions();
        DefaultOperacionesRealizadas = Pick(d.OperacionesRealizadas, ReportDefaultTexts.OperacionesRealizadas,
            nameof(d.OperacionesRealizadas), logger);
        DefaultAseguramientoEvidencia = Pick(d.AseguramientoEvidencia, EncryptZip
            ? ReportDefaultTexts.AseguramientoEvidenciaCifrado
            : ReportDefaultTexts.AseguramientoEvidenciaSinCifrar, nameof(d.AseguramientoEvidencia), logger);
        DefaultNotasTecnicas = Pick(d.NotasTecnicas, ReportDefaultTexts.NotasTecnicas, nameof(d.NotasTecnicas), logger);
        DefaultReserva = Pick(d.Reserva, ReportDefaultTexts.Reserva, nameof(d.Reserva), logger);
    }

    // Los defaults se interpretan como Markdown línea por línea (editor-texto-enriquecido §6.5):
    // uno configurado con contenido no permitido (HTML, imágenes, enlaces no http/https/mailto)
    // se descarta con un warning, así el perito no arranca con un autoguardado que falla.
    private static string Pick(string? configured, string fallback, string key, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(configured)) return fallback;
        var value = configured.Replace("\r\n", "\n").Trim();
        if (ReportMarkdown.Validate(ReportMarkdown.FromLineTemplate(value)) is { } reason)
        {
            logger.LogWarning(
                "Report: DefaultTexts:{Key} tiene contenido no permitido ({Reason}); se usa el texto por defecto",
                key, reason);
            return fallback;
        }
        return value;
    }

    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        return $"{sign}{abs.Hours:00}:{abs.Minutes:00}";
    }
}
