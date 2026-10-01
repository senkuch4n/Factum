using System.Text.RegularExpressions;

namespace Factum.Backend.Services.Reports;

/// <summary>
/// Textos por defecto del paso Informe (DP1, aprobados el 2026-10-01). Neutros, sin datos
/// reales. Cada estudio los puede reemplazar con <c>Report:DefaultTexts:*</c> (config local).
/// Los <c>{token}</c> los completa <see cref="ReportTextRenderer"/> con los datos del caso.
/// </summary>
public static class ReportDefaultTexts
{
    public const string OperacionesRealizadas =
        "En fecha {fechaInspeccion}, siendo las {horaInspeccion} horas, se conectó el dispositivo ({tipoDispositivo} {marcaModeloDispositivo}, sistema operativo {sistemaOperativo}, IMEI {imeiDispositivo}) a la estación de trabajo del perito mediante cable USB, utilizando la herramienta Factum.\n" +
        "Sobre el dispositivo se realizaron las siguientes operaciones:\n" +
        "- Capturas de pantalla: {cantidadCapturas}.\n" +
        "- Grabaciones de pantalla: {cantidadGrabaciones}.\n" +
        "- Archivos copiados desde el almacenamiento del dispositivo: {cantidadArchivosExtraidos}.\n" +
        "- Otros archivos incorporados (fotografías y adjuntos): {cantidadOtros}.\n" +
        "Las operaciones se limitaron a la visualización, captura y copia de la información accesible desde la interfaz del dispositivo, sin modificar intencionalmente su contenido. A cada archivo obtenido se le calculó su valor hash mediante el algoritmo SHA-256.";

    // Texto histórico, sin cambios: se usa con Report:EncryptZip=false.
    public const string AseguramientoEvidenciaSinCifrar =
        "Cada archivo obtenido durante la inspección fue identificado con un nombre único y se le calculó su valor hash mediante el algoritmo SHA-256. Los archivos se agruparon en un contenedor ZIP, cuyo valor hash SHA-256 se calculó una vez cerrado el contenedor. Los valores obtenidos se detallan en la tabla siguiente y permiten verificar, en cualquier momento posterior, que la evidencia no fue alterada.";

    // Report:EncryptZip=true (default). Ver Refactorizaciones/zip-cifrado-real.md §5.6.
    public const string AseguramientoEvidenciaCifrado =
        "Cada archivo obtenido durante la inspección fue identificado con un nombre único y se le calculó su valor hash mediante el algoritmo SHA-256. Los archivos se agruparon en un contenedor ZIP cifrado con el algoritmo AES-256 (formato WinZip AE-2) y protegido con una contraseña generada aleatoriamente para este caso, que no figura en el presente informe y se entrega por un canal separado. Antes de eliminar las copias de trabajo, se verificó que el contenedor cifrado se abre con esa contraseña y que cada archivo contenido conserva su valor hash original. El valor hash SHA-256 del contenedor se calculó sobre el archivo cifrado, una vez cerrado, por lo que puede verificarse sin necesidad de la contraseña. El contenedor se abre con herramientas de compresión de uso habitual como 7-Zip, WinRAR, Keka o The Unarchiver; algunos exploradores de archivos integrados en el sistema operativo no admiten este tipo de cifrado. Los valores obtenidos se detallan en la tabla siguiente y permiten verificar, en cualquier momento posterior, que la evidencia no fue alterada.";

    public const string NotasTecnicas =
        "Los valores hash se expresan en notación hexadecimal y fueron calculados con el algoritmo SHA-256. Cualquier modificación de un archivo, por mínima que sea, produce un valor hash distinto.\n" +
        "Las fechas y horas consignadas corresponden a la zona horaria {zonaHoraria}.\n" +
        "Las capturas de pantalla reflejan el contenido visible en el dispositivo al momento de la inspección. La inspección se limitó a la información accesible desde la interfaz del dispositivo y no incluyó la recuperación de información eliminada.";

    public const string Reserva =
        "{elSuscripto} se reserva el derecho de ampliar o complementar el presente informe en caso de que se aporten nuevos elementos o se requieran precisiones técnicas adicionales.";
}

/// <summary>
/// Renderer de los textos por defecto (§7.6): una sola pasada con <c>\{([A-Za-z]+)\}</c>; un
/// token desconocido queda tal cual (para que el perito lo vea y lo corrija). Los valores no
/// se vuelven a escanear.
/// </summary>
public static class ReportTextRenderer
{
    private static readonly Regex TokenRegex = new(@"\{([A-Za-z]+)\}", RegexOptions.CultureInvariant);

    /// <param name="omitZeroCountLines">Regla de las listas: se omite cada línea que empieza con
    /// "- " y tiene un token {cantidad…} que vale 0 (solo en OperacionesRealizadas).</param>
    public static string Render(string template, IReadOnlyDictionary<string, string> tokens,
        bool omitZeroCountLines = false)
    {
        var lines = template.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            if (omitZeroCountLines && line.StartsWith("- ", StringComparison.Ordinal) &&
                TokenRegex.Matches(line).Any(m =>
                    m.Groups[1].Value.StartsWith("cantidad", StringComparison.Ordinal) &&
                    tokens.TryGetValue(m.Groups[1].Value, out var v) && v == "0"))
                continue;
            kept.Add(TokenRegex.Replace(line, m =>
                tokens.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value));
        }
        return string.Join("\n", kept);
    }
}
