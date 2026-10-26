using Factum.Backend.DTOs;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Reports;

/// <summary>
/// Textos largos (multilínea) y filas de tabla del informe de extracción (informe-extraccion §6.2, §7.1, §7.2),
/// puros. No dependen de Mongo ni de la carpeta: solo del <see cref="ExtractionDto"/>. El informe nunca transcribe
/// contenido extraído (nombres de contactos, mensajes, archivos): solo métodos, cantidades, hashes y metadatos.
/// </summary>
public static class ExtractionReportTexts
{
    /// <summary>Frase fija de cadena de custodia (§4.3): el informe remite al manifiesto sellado y no lo modifica.</summary>
    public const string CustodyChain =
        "Este informe es un documento de presentación generado después del cierre de la extracción. La autoridad de " +
        "integridad es el MANIFIESTO.tsv y su sello de tiempo; este informe remite a ellos y no los modifica. La huella " +
        "SHA-256 de este propio informe figura en INFORME.docx.sha256.";

    // ── §3 Reloj del dispositivo ───────────────────────────────────────────────

    /// <summary><c>{descripcionRelojExtraccion}</c>: relojes de inicio y cierre (si hay). Sin ficha: "".</summary>
    public static string Clock(ExtractionDto ext, IReportSettings settings) =>
        ext.DeviceDetails is { } d ? ReportDeviceTexts.ClockBlock(d, settings) : "";

    // ── §4 Metodología y herramientas ───────────────────────────────────────────

    /// <summary>
    /// <c>{descripcionMetodologia}</c>: un párrafo por categoría extraída, con su mecanismo. El texto se deriva de
    /// <c>category.method</c> (ya viene en el DTO) y del tipo de categoría, sin asumir plataforma salvo donde el DTO
    /// la informa (<c>ext.Device.Platform</c>).
    /// </summary>
    public static string Methodology(ExtractionDto ext)
    {
        var lines = new List<string>
        {
            "Todos los datos estructurados se obtuvieron mediante consultas de solo lectura, sin instalar ni ejecutar " +
            "software en el dispositivo salvo donde se indique. No se modificó el contenido del dispositivo.",
        };
        foreach (var c in ext.Categories)
        {
            if (c.Status == ExtractionCategoryStatuses.NotExtracted) continue;
            var method = (c.Method ?? "").Trim();
            var label = CategoryLabel(c.Key);
            var mech = MechanismNote(c);
            var parts = new List<string>();
            if (method.Length > 0) parts.Add(method);
            if (mech.Length > 0) parts.Add(mech);
            lines.Add(parts.Count > 0 ? $"{label}: {string.Join(" ", parts)}" : $"{label}.");
        }
        if (lines.Count == 1)
            lines.Add("No se completó la extracción de ninguna categoría.");
        return string.Join("\n", lines);
    }

    private static string MechanismNote(ExtractionCategoryDto c) => c.Key switch
    {
        "call_log" => "(registro de llamadas obtenido mediante la aplicación auxiliar firmada de Factum, instalada " +
                      "temporalmente para esta consulta).",
        "contacts" or "sms_mms" or "calendar" or "apps" => "(consulta de solo lectura vía adb).",
        _ => c.Kind == "files"
            ? "(copia de archivos del almacenamiento compartido, con SHA-256 por archivo y registro en el manifiesto)."
            : "",
    };

    // ── §5 Inventario: filas de la tabla de categorías ─────────────────────────

    /// <summary>Una fila por categoría elegida: (categoría, método, resultado).</summary>
    public static List<(string Category, string Method, string Result)> CategoryRows(ExtractionDto ext) =>
        ext.Categories.Select(c => (CategoryLabel(c.Key), MethodCell(c), ResultCell(c))).ToList();

    private static string MethodCell(ExtractionCategoryDto c)
    {
        var method = (c.Method ?? "").Trim();
        return method.Length > 0 ? method : CategoryKindLabel(c.Kind);
    }

    private static string ResultCell(ExtractionCategoryDto c) => c.Status switch
    {
        ExtractionCategoryStatuses.NotExtracted => $"No extraída: {ReasonText(c.ReasonCode)}",
        ExtractionCategoryStatuses.Cancelled => "Cancelada",
        _ => CountCell(c),
    };

    private static string CountCell(ExtractionCategoryDto c)
    {
        var parts = new List<string>();
        if (c.Kind == "structured")
        {
            parts.Add($"{c.Count ?? 0} registros");
        }
        else
        {
            if (c.Files is { } f) parts.Add($"{f} archivos");
            if (c.Bytes is { } b) parts.Add(ExtractionReportValues.Bytes(b));
        }
        if ((c.Errors ?? 0) > 0) parts.Add($"{c.Errors} con error");
        if ((c.HashMismatches ?? 0) > 0) parts.Add($"{c.HashMismatches} discordancias");
        if (parts.Count == 0) parts.Add("sin elementos");
        var text = string.Join(" · ", parts);
        return c.Status == ExtractionCategoryStatuses.DoneWithErrors ? $"{text} (con errores)" : text;
    }

    // ── §6 Integridad y cadena de custodia ─────────────────────────────────────

    /// <summary><c>{hashDispositivoExtraccion}</c>: si el SHA-256 se calculó también en el dispositivo y discordancias.</summary>
    public static string DeviceHashLine(ExtractionDto ext)
    {
        var mismatches = ext.Totals.HashMismatches ?? 0;
        if (!ext.VerifyOnDevice)
            return "No se solicitó el recálculo del SHA-256 en el dispositivo; la integridad se verifica contra el " +
                   "MANIFIESTO.tsv.";
        if (!ext.DeviceSha256Available)
            return "El recálculo del SHA-256 en el dispositivo no fue posible (el dispositivo no expone una utilidad " +
                   "de hash por consola); la integridad se verifica contra el MANIFIESTO.tsv.";
        return mismatches == 0
            ? "El SHA-256 de cada archivo se recalculó también en el dispositivo y coincidió con el preservado, sin " +
              "discordancias."
            : $"El SHA-256 de cada archivo se recalculó también en el dispositivo; se registraron {mismatches} " +
              "discordancias, detalladas en el MANIFIESTO.tsv.";
    }

    /// <summary><c>{descripcionSelloExtraccion}</c>: estado del sellado de tiempo del manifiesto (3 casos de D5).</summary>
    public static string SealDescription(ExtractionDto ext, IReportSettings settings)
    {
        var ts = ext.ManifestTimestamp;
        if (ts is null)
            return "El hash del manifiesto no cuenta con sello de tiempo de un tercero: el sellado estaba desactivado o " +
                   "la extracción no fijó un hash. La integridad del conjunto se verifica contra el MANIFIESTO.tsv y su " +
                   "SHA-256.";
        switch (ts.Status)
        {
            case TimestampStatuses.Granted:
                var when = ts.GenTimeDisplay ?? ExtractionReportValues.NoDisponible;
                var serie = string.IsNullOrWhiteSpace(ts.SerialNumber) ? "" : $", n.º de serie {ts.SerialNumber}";
                return $"El hash SHA-256 del manifiesto fue sellado por una autoridad de sellado de tiempo independiente " +
                       $"({ts.Authority ?? "autoridad no informada"}) el {when}{serie}, conforme al estándar RFC 3161. El " +
                       "sello es una constancia firmada que certifica que ese hash existía en la fecha y hora indicadas, " +
                       "tomadas del reloj de la autoridad. Este informe remite a ese sello como fecha cierta de la " +
                       "integridad del conjunto.";
            case TimestampStatuses.Pending:
                return "El sellado de tiempo del manifiesto quedó pendiente al emitir este informe: la autoridad de " +
                       "sellado se completará más tarde y llevará su fecha real, posterior a esta emisión. La integridad " +
                       "del conjunto se verifica contra el MANIFIESTO.tsv y su SHA-256.";
            default:
                return "El sellado de tiempo del manifiesto no pudo obtenerse al emitir este informe. La integridad del " +
                       "conjunto se verifica contra el MANIFIESTO.tsv y su SHA-256.";
        }
    }

    // ── §7 Modificaciones al dispositivo ────────────────────────────────────────

    /// <summary><c>{descripcionModificaciones}</c>: lista con hora o "Ninguna", más los efectos colaterales de adb.</summary>
    public static string Modifications(ExtractionDto ext, IReportSettings settings)
    {
        var lines = new List<string>();
        if (ext.DeviceModifications.Count == 0)
        {
            lines.Add("Ninguna modificación de contenido: la extracción operó en modo de solo lectura.");
        }
        else
        {
            foreach (var m in ext.DeviceModifications)
            {
                var hora = m.At is { } at ? Timestamp(at, settings) : null;
                var text = ModificationLabel(m.Kind);
                if (!string.IsNullOrWhiteSpace(m.Detail)) text += $" ({m.Detail!.Trim()})";
                if (m.AfterClose == true) text += " [posterior al cierre]";
                lines.Add(hora is null ? text : $"{hora}: {text}");
            }
        }
        lines.Add("Efectos colaterales inevitables de la conexión por adb: autorización de depuración USB de esta " +
                  "estación de trabajo en el dispositivo y entradas en los registros del sistema operativo. No alteran " +
                  "el contenido de los datos extraídos.");
        return string.Join("\n", lines);
    }

    // ── §8 Interrupción / cancelación ───────────────────────────────────────────

    /// <summary><c>{descripcionInterrupcion}</c>: estado final, hora y motivo del corte. "" si terminó normal.</summary>
    public static string Interruption(ExtractionDto ext, IReportSettings settings)
    {
        if (ext.Interruption is { } i)
        {
            var hora = i.At is { } at ? Timestamp(at, settings) : ExtractionReportValues.NoDisponible;
            var motivo = string.IsNullOrWhiteSpace(i.Message) ? ReasonText(i.ReasonCode) : i.Message!.Trim();
            return $"La extracción finalizó de forma anticipada el {hora}. Motivo: {motivo}. El informe refleja lo " +
                   "efectivamente extraído hasta el corte, conforme lo registrado en el MANIFIESTO.tsv.";
        }
        if (ext.Status == ExtractionStatuses.Cancelled)
            return "La extracción fue cancelada por el operador. El informe refleja lo efectivamente extraído hasta la " +
                   "cancelación, conforme lo registrado en el MANIFIESTO.tsv.";
        return "";
    }

    // ── §9 Alcances y limitaciones (D3, texto fijo del método) ──────────────────

    /// <summary><c>{descripcionAlcances}</c>: texto fijo de D3. No depende de los datos; es del método.</summary>
    public static string Scopes(ExtractionDto ext)
    {
        var android = !string.Equals(ext.Device.Platform, "ios", StringComparison.OrdinalIgnoreCase);
        var noExpone = android
            ? "en una extracción lógica de Android, el almacenamiento aislado de otras aplicaciones no se expone a adb " +
              "(Android 11 y posteriores)"
            : "el almacenamiento aislado de otras aplicaciones no se expone a una extracción lógica";
        return string.Join("\n",
            "Una extracción lógica recupera lo que el sistema operativo expone por una interfaz de solo lectura. A " +
            "continuación se declaran sus alcances y limitaciones como propiedad del método, no como un pendiente de " +
            "desarrollo.",
            "Cubre: datos estructurados accesibles por adb de solo lectura (contactos, SMS/MMS, calendario y " +
            "aplicaciones instaladas), el registro de llamadas a través de la aplicación auxiliar firmada de Factum, y " +
            "los archivos del almacenamiento compartido (por ejemplo /sdcard) seleccionados, preservados con SHA-256 y " +
            "registrados en el manifiesto.",
            $"No cubre: el contenido privado de las aplicaciones (/data/data); {noExpone}; el contenido cifrado de " +
            "aplicaciones de mensajería (por ejemplo los mensajes de WhatsApp) sin la clave o sin un método avanzado; " +
            "la extracción física, por root, exploits o bootloader; y los datos borrados o no referenciados por el " +
            "sistema de archivos. El registro de llamadas requiere instalar temporalmente la aplicación auxiliar; si el " +
            "fabricante bloquea la instalación por USB, esa categoría queda \"No extraída\".",
            "Lo anterior es el alcance propio de una extracción lógica, no una limitación de esta herramienta ni un " +
            "pendiente de desarrollo.");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private static string Timestamp(DateTimeOffset at, IReportSettings settings)
    {
        var (fecha, hora) = ReportValues.Inspeccion(at.UtcDateTime, settings.Zone);
        return $"{fecha}, {hora}";
    }

    public static string CategoryLabel(string? key) => key switch
    {
        "contacts" => "Contactos",
        "sms_mms" => "SMS / MMS",
        "call_log" => "Registro de llamadas",
        "calendar" => "Calendario",
        "apps" => "Aplicaciones instaladas",
        "media" => "Multimedia",
        "downloads" => "Descargas",
        "documents" => "Documentos",
        "audio" => "Audio",
        "whatsapp_media" => "Multimedia de WhatsApp",
        "all_storage" => "Almacenamiento compartido",
        "custom" => "Carpetas puntuales",
        _ => key ?? "",
    };

    private static string CategoryKindLabel(string? kind) => kind == "structured"
        ? "Consulta de solo lectura"
        : "Copia de archivos del almacenamiento compartido";

    private static string ModificationLabel(string? kind) => kind switch
    {
        DeviceModificationKinds.ConnectorInstalled => "Instalación temporal de la aplicación auxiliar firmada de Factum",
        DeviceModificationKinds.ConnectorUninstalled => "Desinstalación de la aplicación auxiliar firmada de Factum",
        DeviceModificationKinds.StayAwakeSet => "Activación de la pantalla encendida mientras duró la extracción",
        DeviceModificationKinds.StayAwakeRestored => "Restauración de la configuración de pantalla del dispositivo",
        _ => kind ?? "Modificación",
    };

    private static string ReasonText(string? reasonCode) => reasonCode switch
    {
        null or "" => "sin motivo informado",
        "connector_install_blocked" or "connector_blocked" =>
            "el fabricante bloqueó la instalación de la aplicación auxiliar por USB",
        "connector_install_failed" => "no se pudo instalar la aplicación auxiliar",
        "connector_unavailable" => "la aplicación auxiliar no estaba disponible",
        "provider_unavailable" => "el proveedor de datos no estaba disponible en el dispositivo",
        "permission_denied" => "el sistema operativo denegó el permiso de lectura",
        "empty" => "no había elementos para extraer",
        "not_present" or "not_selected" => "la categoría no estaba presente o no se seleccionó",
        "timeout" => "el dispositivo no respondió a tiempo",
        "device_disconnected" => "el dispositivo se desconectó",
        "cancelled" => "la extracción se canceló",
        "agent_stopped" => "el agente se detuvo",
        _ => reasonCode.Replace('_', ' '),
    };
}
