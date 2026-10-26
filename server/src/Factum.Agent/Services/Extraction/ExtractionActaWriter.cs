using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Factum.Agent.Services.Extraction;

/// <summary>Una categoría en el ACTA (método y resultado, §9.3 sección 5).</summary>
public sealed record ActaCategoryLine(string Key, string Label, string Method, string Status,
    long? Count, int? Files, long? Bytes, int? Errors, int? HashMismatches, string? ReasonText);

/// <summary>Una modificación al dispositivo (§9.3 sección 7).</summary>
public sealed record ActaModification(string Kind, DateTime AtUtc, string? Detail);

/// <summary>Un dato de la ficha del dispositivo (§9.3 sección 3).</summary>
public sealed record ActaFichaField(string Key, string? Value, string? Reason);

/// <summary>Identificación del equipo del que salió la evidencia (§9.3 sección 3, obligatoria en el ACTA).</summary>
public sealed record ActaDevice(string Serial, string Manufacturer, string Model, string? AndroidVersion);

/// <summary>
/// Qué pasó REALMENTE con la verificación del hash en el dispositivo (§9.3 sección 6 / D10). El ACTA deriva el texto
/// de acá, no de la mera capacidad del equipo.
/// </summary>
/// <param name="Requested">El operador pidió verificar en el dispositivo (<c>verify_on_device</c>).</param>
/// <param name="CapabilityAvailable">El equipo tiene <c>sha256sum</c>.</param>
/// <param name="Binary">Binario realmente detectado (o null si no se detectó / no corresponde mencionarlo).</param>
/// <param name="FilesComputed">Archivos con hash del dispositivo comparado (coincide si o no).</param>
/// <param name="Mismatches">Archivos con <c>coincide=no</c>.</param>
/// <param name="FilesNotComputed">Archivos con <c>coincide=no_calculado</c>.</param>
public sealed record ActaVerification(bool Requested, bool CapabilityAvailable, string? Binary,
    int FilesComputed, int Mismatches, int FilesNotComputed);

/// <summary>Datos para armar el ACTA de una extracción (extraccion-logica-android §9.3).</summary>
public sealed record ActaData(
    string ExtractionId,
    string Status,
    string StatusLabel,
    string OperatorDni,
    string OperatorName,
    string Hostname,
    string OsUser,
    string Os,
    string AgentVersion,
    string AgentMode,
    string? ClientVersion,
    ActaDevice Device,
    DateTime StartedAtPcUtc,
    DateTime? FinishedAtPcUtc,
    long? PcMinusServerMs,
    long? PcServerMarginMs,
    string DestinationPath,
    string FileSystem,
    long FreeBytesAtStart,
    IReadOnlyList<ActaFichaField> Ficha,
    string? ClockStartText,
    string? ClockCloseText,
    IReadOnlyList<ActaCategoryLine> Categories,
    ActaVerification Verification,
    IReadOnlyList<ActaModification> Modifications,
    string? InterruptionReasonText,
    DateTime? InterruptionAtUtc,
    string ManifestSha256);

/// <summary>
/// Genera ACTA.txt (UTF-8 con BOM, CRLF), ACTA.json (schema <c>factum-extraccion-acta/1</c>), CIERRE.txt y LEEME.txt
/// (extraccion-logica-android §9.3–§9.5). El armado del texto es puro y testeable; <see cref="WriteAsync"/> escribe
/// ACTA.txt/.json y LEEME.txt (el CIERRE.txt lo escribe el orquestador con los hashes finales).
/// </summary>
public static class ExtractionActaWriter
{
    public const string Schema = "factum-extraccion-acta/1";
    public const string NoModifications = "Ninguna";

    /// <summary>§9.3 sección 7: efectos inevitables de adb (no son modificaciones de Factum).</summary>
    public const string AdbEffectsText =
        "Efectos colaterales inevitables de adb (no son modificaciones hechas por Factum): la autorización de " +
        "depuración USB de esta PC quedó registrada en el dispositivo, y el sistema operativo del dispositivo " +
        "puede haber dejado entradas en sus registros (logcat / eventos del sistema) por las consultas realizadas.";

    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task WriteAsync(string finalDir, ActaData data, CancellationToken ct = default)
    {
        // ACTA.txt: BOM + CRLF (para el Bloc de notas de Windows).
        await File.WriteAllTextAsync(Path.Combine(finalDir, "ACTA.txt"), BuildActaText(data), Utf8Bom, ct);
        await File.WriteAllTextAsync(Path.Combine(finalDir, "ACTA.json"), BuildActaJson(data), Utf8NoBom, ct);
        await File.WriteAllTextAsync(Path.Combine(finalDir, "LEEME.txt"), BuildLeeme(data.ExtractionId), Utf8Bom, ct);
    }

    public static string BuildActaText(ActaData d)
    {
        var sb = new StringBuilder();
        void L(string line = "") => sb.Append(line).Append("\r\n");

        L("ACTA DE EXTRACCIÓN LÓGICA");
        L("=========================");
        L();
        L("1. IDENTIFICACIÓN");
        L($"   Extracción: {d.ExtractionId}");
        L($"   Estado final: {d.StatusLabel}");
        L($"   Operador: {d.OperatorName} (DNI {d.OperatorDni})");
        L($"   PC: {d.Hostname} / usuario {d.OsUser} / {d.Os}");
        L($"   Tatana: {d.AgentVersion} ({d.AgentMode})");
        if (!string.IsNullOrEmpty(d.ClientVersion)) L($"   Factum web: {d.ClientVersion}");
        L();
        L("2. TIEMPOS");
        L($"   Inicio (PC): {Iso(d.StartedAtPcUtc)} UTC");
        L($"   Fin (PC): {(d.FinishedAtPcUtc is { } f ? Iso(f) + " UTC" : "en curso")}");
        if (d.PcMinusServerMs is { } pm && d.PcServerMarginMs is { } pg)
            L($"   Diferencia PC − servidor (medida al iniciar): {pm} ms (± {pg} ms). Las horas de servidor son derivadas.");
        else
            L("   Diferencia PC − servidor: No medida.");
        L();
        L("3. DISPOSITIVO");
        L($"   Marca: {Dash(d.Device.Manufacturer)}");
        L($"   Modelo: {Dash(d.Device.Model)}");
        L($"   Número de serie (conexión): {Dash(d.Device.Serial)}");
        L($"   Versión de Android: {Dash(d.Device.AndroidVersion)}");
        foreach (var fld in d.Ficha)
            L($"   {fld.Key}: {(fld.Value is not null ? fld.Value : "No disponible (" + (fld.Reason ?? "-") + ")")}");
        if (d.ClockStartText is not null) L($"   Reloj al inicio: {d.ClockStartText}");
        if (d.ClockCloseText is not null) L($"   Reloj al cierre: {d.ClockCloseText}");
        L();
        L("4. DESTINO");
        L($"   Carpeta: {d.DestinationPath}");
        L($"   Sistema de archivos: {d.FileSystem}");
        L($"   Espacio libre al inicio: {d.FreeBytesAtStart} bytes");
        L();
        L("5. MÉTODO Y RESULTADO POR CATEGORÍA");
        foreach (var c in d.Categories)
        {
            L($"   [{c.Label}] {c.Status}");
            L($"      Método: {c.Method}");
            if (c.Status == "not_extracted")
                L($"      No extraída: {c.ReasonText ?? "-"}");
            else
            {
                if (c.Count is { } cn) L($"      Cantidad: {cn}");
                if (c.Files is { } fi) L($"      Archivos: {fi}  Bytes: {c.Bytes ?? 0}");
                if (c.Errors is { } er && er > 0) L($"      Errores: {er}");
                if (c.HashMismatches is { } hm && hm > 0) L($"      Discordancias de hash: {hm}");
            }
        }
        L();
        L("6. VERIFICACIÓN");
        foreach (var line in VerificationLines(d.Verification)) L("   " + line);
        L();
        L("7. MODIFICACIONES AL DISPOSITIVO");
        if (d.Modifications.Count == 0)
            L($"   {NoModifications}");
        else
            foreach (var m in d.Modifications)
                L($"   {Iso(m.AtUtc)} UTC — {m.Kind}{(m.Detail is not null ? ": " + m.Detail : "")}");
        L($"   {AdbEffectsText}");
        L();
        if (d.InterruptionReasonText is not null)
        {
            L("8. INTERRUPCIÓN O CANCELACIÓN");
            L($"   {(d.InterruptionAtUtc is { } ia ? Iso(ia) + " UTC — " : "")}{d.InterruptionReasonText}");
            L();
        }
        L("9. INTEGRIDAD");
        L("   El SHA-256 de este acta figura en MANIFIESTO.tsv. El hash del manifiesto está en");
        L("   MANIFIESTO.tsv.sha256 y en CIERRE.txt.");
        return sb.ToString();
    }

    public static string BuildActaJson(ActaData d)
    {
        var json = new JsonObject
        {
            ["schema"] = Schema,
            ["extraction_id"] = d.ExtractionId,
            ["status"] = d.Status,
            ["operator"] = new JsonObject { ["dni"] = d.OperatorDni, ["name"] = d.OperatorName },
            ["pc"] = new JsonObject
            {
                ["hostname"] = d.Hostname,
                ["os_user"] = d.OsUser,
                ["os"] = d.Os,
                ["agent_version"] = d.AgentVersion,
                ["agent_mode"] = d.AgentMode,
                ["client_version"] = d.ClientVersion,
            },
            ["started_at_pc"] = Iso(d.StartedAtPcUtc) + "Z",
            ["finished_at_pc"] = d.FinishedAtPcUtc is { } f ? Iso(f) + "Z" : null,
            ["pc_minus_server_ms"] = d.PcMinusServerMs,
            ["pc_server_margin_ms"] = d.PcServerMarginMs,
            ["destination_path"] = d.DestinationPath,
            ["file_system"] = d.FileSystem,
            ["free_bytes_at_start"] = d.FreeBytesAtStart,
            ["device_identity"] = new JsonObject
            {
                ["serial"] = d.Device.Serial,
                ["manufacturer"] = d.Device.Manufacturer,
                ["model"] = d.Device.Model,
                ["android_version"] = d.Device.AndroidVersion,
            },
            ["device"] = new JsonObject(d.Ficha.Select(fld =>
                new KeyValuePair<string, JsonNode?>(fld.Key, new JsonObject
                {
                    ["value"] = fld.Value,
                    ["reason"] = fld.Reason,
                }))),
            ["categories"] = new JsonArray(d.Categories.Select(c => (JsonNode?)new JsonObject
            {
                ["key"] = c.Key,
                ["status"] = c.Status,
                ["method"] = c.Method,
                ["count"] = c.Count,
                ["files"] = c.Files,
                ["bytes"] = c.Bytes,
                ["errors"] = c.Errors,
                ["hash_mismatches"] = c.HashMismatches,
                ["reason"] = c.ReasonText,
            }).ToArray()),
            ["verification"] = new JsonObject
            {
                ["requested"] = d.Verification.Requested,
                ["device_sha256_available"] = d.Verification.CapabilityAvailable,
                ["binary"] = d.Verification.Binary,
                ["files_computed_on_device"] = d.Verification.FilesComputed,
                ["mismatches"] = d.Verification.Mismatches,
                ["files_not_computed"] = d.Verification.FilesNotComputed,
            },
            ["modifications"] = new JsonArray(d.Modifications.Select(m => (JsonNode?)new JsonObject
            {
                ["kind"] = m.Kind,
                ["at"] = Iso(m.AtUtc) + "Z",
                ["detail"] = m.Detail,
            }).ToArray()),
            ["adb_effects"] = AdbEffectsText,
            ["interruption"] = d.InterruptionReasonText is null ? null : new JsonObject
            {
                ["reason"] = d.InterruptionReasonText,
                ["at"] = d.InterruptionAtUtc is { } ia ? Iso(ia) + "Z" : null,
            },
            ["manifest_sha256"] = d.ManifestSha256,
        };
        return json.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static string BuildCierre(string extractionId, string statusLabel, DateTime closedPcLocal,
        string localOffset, string manifestSha256, string actaTxtSha, string actaJsonSha, int manifestEntries)
    {
        var sb = new StringBuilder();
        void L(string line = "") => sb.Append(line).Append("\r\n");
        L($"Extracción {extractionId} — cierre de integridad");
        L($"Estado: {statusLabel}");
        L($"Cerrada (PC): {closedPcLocal:yyyy-MM-dd HH:mm:ss} {localOffset}");
        L($"SHA-256 de MANIFIESTO.tsv: {manifestSha256}");
        L($"SHA-256 de ACTA.txt: {actaTxtSha}   SHA-256 de ACTA.json: {actaJsonSha}");
        L($"Entradas del manifiesto: {manifestEntries}");
        L("Registro en Factum: lo hace la web; si la web no estaba abierta, queda pendiente hasta que se vuelva a " +
          "abrir la sección Extracciones.");
        return sb.ToString();
    }

    public static string BuildLeeme(string extractionId)
    {
        var sb = new StringBuilder();
        void L(string line = "") => sb.Append(line).Append("\r\n");
        L($"EXTRACCIÓN LÓGICA DE ANDROID — {extractionId}");
        L("================================================");
        L();
        L("Contenido de esta carpeta:");
        L("  datos/crudo/        salida tal cual de cada consulta (EVIDENCIA primaria).");
        L("  datos/normalizado/  misma información en JSON (DERIVADO del crudo).");
        L("  archivos/           archivos copiados del dispositivo, con su ruta original (EVIDENCIA).");
        L("  registro/           comandos ejecutados, inventario y hashes del dispositivo (REGISTRO).");
        L("  MANIFIESTO.tsv      lista de todo con su SHA-256 (autoridad de integridad).");
        L("  MANIFIESTO.tsv.sha256, SHA256SUMS.txt, CIERRE.txt, ACTA.txt, ACTA.json, LEEME.txt");
        L("  sellos/             sello de tiempo RFC 3161 del manifiesto (si el sellado está activo).");
        L();
        L("Verificar la integridad SIN Factum:");
        L("  Linux/macOS:");
        L("    sha256sum -c SHA256SUMS.txt");
        L("    sha256sum MANIFIESTO.tsv        (comparar con MANIFIESTO.tsv.sha256 y CIERRE.txt)");
        L("  Windows (PowerShell):");
        L("    Get-FileHash -Algorithm SHA256 MANIFIESTO.tsv");
        L("    Get-Content SHA256SUMS.txt | ForEach-Object { $h,$f = $_ -split '  ',2;");
        L("      if ((Get-FileHash -Algorithm SHA256 $f).Hash -ieq $h) {\"OK  $f\"} else {\"FALLA  $f\"} }");
        L();
        L("Verificar el sello de tiempo (si hay carpeta sellos/): ver sellos/LEEME-verificacion.txt");
        L("  openssl ts -verify -data MANIFIESTO.tsv -in sellos/MANIFIESTO.tsv.tsr -CAfile <ca>");
        L();
        L("Recomendación: guardá esta carpeta en un medio cifrado (BitLocker / LUKS / FileVault) si sale de la");
        L("oficina.");
        return sb.ToString();
    }

    /// <summary>
    /// Texto veraz de §6 según lo que REALMENTE pasó (QA reintento 2, D10): (a) verificado y coincide, (b) verificado
    /// con N discordancias, (c) no verificado con el motivo. El binario es el detectado; si no, no se menciona.
    /// </summary>
    public static IReadOnlyList<string> VerificationLines(ActaVerification v)
    {
        var lines = new List<string>();
        if (v.FilesComputed > 0)
        {
            var bin = string.IsNullOrWhiteSpace(v.Binary) ? "" : $" (binario: {v.Binary})";
            if (v.Mismatches == 0)
            {
                lines.Add($"El SHA-256 se calculó también en el dispositivo{bin} y coincide con el de la PC en los " +
                          $"{v.FilesComputed} archivo(s) verificados.");
            }
            else
            {
                lines.Add($"El SHA-256 se calculó también en el dispositivo{bin} en {v.FilesComputed} archivo(s).");
                lines.Add($"{v.Mismatches} archivo(s) NO coinciden (coincide=no en MANIFIESTO.tsv).");
                lines.Add("El hash del dispositivo se toma ANTES de copiar: un archivo que cambia entre la lectura y " +
                          "la copia puede figurar como discordancia sin ser un error.");
            }
            if (v.FilesNotComputed > 0)
                lines.Add($"{v.FilesNotComputed} archivo(s) quedaron con coincide=no_calculado (no estaban en la " +
                          "lectura del dispositivo).");
        }
        else
        {
            var motivo = !v.Requested ? "verificación en el dispositivo desactivada por el operador"
                : !v.CapabilityAvailable ? "el equipo no tiene sha256sum"
                : "la lectura en el dispositivo no produjo hashes";
            lines.Add($"No se calculó el SHA-256 en el dispositivo ({motivo}).");
            if (v.FilesNotComputed > 0)
                lines.Add($"{v.FilesNotComputed} archivo(s) quedaron con coincide=no_calculado en MANIFIESTO.tsv.");
        }
        return lines;
    }

    private static string Dash(string? s) => string.IsNullOrWhiteSpace(s) ? "-" : s;

    private static string Iso(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
}
