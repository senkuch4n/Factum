using System.Globalization;
using System.Reflection;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Branding;

namespace Factum.Backend.Services.Reports;

/// <summary>
/// Valores de los placeholders de una línea del informe de extracción (informe-extraccion §6.2, §7.1), puros. Se
/// arman desde el <see cref="ExtractionDto"/> + <see cref="IReportSettings"/> + <see cref="BrandingSnapshot"/>, sin
/// tocar Mongo ni la carpeta. Los textos largos (multilínea) van en <see cref="ExtractionReportTexts"/>. El informe
/// nunca incluye contenido extraído: solo metadatos, cantidades y hashes.
/// </summary>
public static class ExtractionReportValues
{
    public const string NoDisponible = "No disponible";

    /// <summary>Etiqueta en español del estado terminal de la extracción.</summary>
    public static string StatusLabel(string status) => status switch
    {
        ExtractionStatuses.Completed => "Completa",
        ExtractionStatuses.CompletedWithErrors => "Completa con errores",
        ExtractionStatuses.Interrupted => "Interrumpida",
        ExtractionStatuses.Cancelled => "Cancelada",
        ExtractionStatuses.Running => "En curso",
        _ => status,
    };

    /// <summary>Marca + modelo del equipo ("samsung SM-M127F"); sin duplicar la marca si el modelo ya la trae.</summary>
    public static string Equipo(ExtractionDeviceDto d)
    {
        var manufacturer = (d.Manufacturer ?? "").Trim();
        var model = (d.Model ?? "").Trim();
        if (manufacturer.Length == 0) return model.Length == 0 ? NoDisponible : model;
        if (model.Length == 0) return manufacturer;
        if (model.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase)) return model;
        return $"{manufacturer} {model}";
    }

    /// <summary>Versión informativa del backend (atributo informational del ensamblado); "" si no está.</summary>
    public static string FactumVersion()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(info)) return "";
        // El informational version suele traer "+<commit>"; se recorta.
        var plus = info.IndexOf('+');
        return (plus > 0 ? info[..plus] : info).Trim();
    }

    /// <summary>"B" / "KiB" / "MiB" / "GiB" / "TiB" con un decimal a partir de KiB.</summary>
    public static string Bytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        string[] units = ["KiB", "MiB", "GiB", "TiB", "PiB"];
        double value = bytes;
        var i = -1;
        do { value /= 1024; i++; } while (value >= 1024 && i < units.Length - 1);
        return $"{value.ToString("0.#", CultureInfo.InvariantCulture)} {units[i]}";
    }

    private static string FechaHora(DateTime? utc, TimeZoneInfo zone)
    {
        if (utc is not { } v) return NoDisponible;
        var (fecha, hora) = ReportValues.Inspeccion(v, zone);
        return $"{fecha}, {hora}";
    }

    /// <summary>§7.1: diccionario "{placeholder}" → valor de una línea, para el reemplazo de texto (B-R6).</summary>
    public static Dictionary<string, string> Placeholders(ExtractionDto ext, IReportSettings settings,
        BrandingSnapshot brand)
    {
        var tatana = (ext.AgentVersion ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(ext.AgentMode))
            tatana = tatana.Length == 0 ? $"modo {ext.AgentMode}" : $"{tatana} (modo {ext.AgentMode})";
        if (tatana.Length == 0) tatana = NoDisponible;

        var totals = ext.Totals;
        var totalesLine =
            $"Archivos preservados: {totals.Files ?? 0} · Tamaño total: {Bytes(totals.Bytes ?? 0)} · " +
            $"Archivos con error: {totals.Errors ?? 0} · Discordancias de hash: {totals.HashMismatches ?? 0}";

        var carpeta = (ext.DestinationPath ?? "").Trim();
        var folder = (ext.FolderName ?? "").Trim();
        var ubicacion = (carpeta, folder) switch
        {
            ("", "") => NoDisponible,
            ("", _) => folder,
            (_, "") => carpeta,
            _ => carpeta.EndsWith(folder, StringComparison.Ordinal) ? carpeta : $"{carpeta} ({folder})",
        };

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Marca (membrete, encabezado y portada).
            ["{ORGANIZACION}"] = brand.OrganizationName ?? "",
            // Portada.
            ["{equipoExtraccion}"] = Equipo(ext.Device),
            ["{fechaExtraccion}"] = ReportValues.Inspeccion(ext.StartedAtPc, settings.Zone).Fecha,
            ["{estadoExtraccion}"] = StatusLabel(ext.Status),
            // §1 Identificación.
            ["{peritoExtraccion}"] = string.IsNullOrWhiteSpace(ext.Officer.Name) ? NoDisponible : ext.Officer.Name.Trim(),
            ["{dniPerito}"] = string.IsNullOrWhiteSpace(ext.Officer.Dni) ? NoDisponible : ext.Officer.Dni.Trim(),
            ["{pcExtraccion}"] = string.IsNullOrWhiteSpace(ext.Hostname) ? NoDisponible : ext.Hostname.Trim(),
            ["{usuarioSoExtraccion}"] = string.IsNullOrWhiteSpace(ext.OsUser) ? NoDisponible : ext.OsUser.Trim(),
            ["{versionTatana}"] = tatana,
            ["{versionFactum}"] = FactumVersion(),
            ["{actuacionCaso}"] = (ext.CaseRef ?? "").Trim(),
            // §2 Tiempos.
            ["{inicioExtraccion}"] = FechaHora(ext.StartedAtPc, settings.Zone),
            ["{finExtraccion}"] = FechaHora(ext.FinishedAtPc, settings.Zone),
            // §5 Inventario.
            ["{totalesExtraccion}"] = totalesLine,
            // §6 Integridad.
            ["{hashManifiesto}"] = string.IsNullOrWhiteSpace(ext.ManifestSha256) ? NoDisponible : ext.ManifestSha256!,
            ["{entradasManifiesto}"] = ext.ManifestEntries?.ToString(CultureInfo.InvariantCulture) ?? NoDisponible,
            ["{hashDispositivoExtraccion}"] = ExtractionReportTexts.DeviceHashLine(ext),
            ["{ubicacionCarpeta}"] = ubicacion,
        };

        // §3 Dispositivo: las 12 filas de la ficha #79 (mismos nombres que el informe de inspección). Sin ficha: "".
        foreach (var (k, v) in ReportDeviceTexts.Placeholders(ext.DeviceDetails, settings.Zone))
            values[k] = v;

        return values;
    }
}
