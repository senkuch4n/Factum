using System.Text;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Timestamping;

/// <summary>
/// Texto de <c>sellos/LEEME-verificacion.txt</c> (sellado-tiempo §10), puro. Fin de línea CRLF. Lo usan
/// <c>timestamps/files</c> (Tatana lo escribe tal cual) y <c>timestamps/bundle</c>. Las URLs ya vienen saneadas
/// (sin userinfo) desde el sello.
/// </summary>
public static class TimestampReadme
{
    private const string Nl = "\r\n";

    /// <param name="caseRef"><c>nro_referencia</c>.</param>
    /// <param name="nowUtc">Momento de armado.</param>
    /// <param name="zone">Zona del informe (<c>Report:TimeZone</c>).</param>
    /// <param name="zipFilename">Nombre del ZIP del caso, o null (borrador).</param>
    /// <param name="pdfFilename">Nombre del DOCX del caso, o null (borrador).</param>
    /// <param name="selection">Sellos incluidos (ZIP, informe, evidencia) y lo que quedó sin sello.</param>
    /// <param name="sessionFiles">
    /// grabacion-sesion-completa §6.7: tramos de la grabación de la sesión. Su sello se describe como "Tramo de la grabación
    /// de la sesión" y suma la línea <c>Dentro del ZIP: "sesion/&lt;archivo&gt;"</c>. null o vacío = como siempre.
    /// </param>
    public static string Build(string caseRef, DateTime nowUtc, TimeZoneInfo zone, string? zipFilename,
        string? pdfFilename, TimestampSelection selection, IReadOnlySet<string>? sessionFiles = null)
    {
        bool IsSession(CaseTimestamp t) =>
            t.Target == TimestampTargets.Evidence && sessionFiles is not null && sessionFiles.Contains(t.Filename);
        var sealedItems = selection.Sealed;
        var sb = new StringBuilder();
        void L(string line = "") => sb.Append(line).Append(Nl);

        // Autoridades distintas (por host), en orden de aparición.
        var authorities = new List<(string? Host, string Name, CaseTimestamp First)>();
        foreach (var t in sealedItems)
        {
            var host = KnownAuthorities.HostOf(t.TsaUrl);
            if (authorities.Any(a => string.Equals(a.Host, host, StringComparison.OrdinalIgnoreCase))) continue;
            authorities.Add((host, t.Authority ?? host ?? "Autoridad de sellado de tiempo", t));
        }
        var cas = authorities.Select(a => KnownAuthorities.CaFileFor(a.Host)).Distinct().ToList();
        var genericCa = cas.Count == 1 ? cas[0] : "<certificado raíz de la autoridad del sello>";
        string CaOf(CaseTimestamp t) => KnownAuthorities.CaFileFor(KnownAuthorities.HostOf(t.TsaUrl));
        string? CaOfTarget(string target) =>
            sealedItems.FirstOrDefault(t => t.Target == target) is { } s ? CaOf(s) : null;

        L("VERIFICACIÓN DE LOS SELLOS DE TIEMPO (RFC 3161)");
        L("===============================================");
        L();
        L($"Caso: {caseRef}");
        L($"Archivo generado por Factum el {TimestampFormat.Utc(nowUtc)} UTC ({TimestampFormat.Display(nowUtc, zone)}).");
        L();
        L("Esta carpeta contiene los sellos de tiempo emitidos por una autoridad de sellado de tiempo");
        L("independiente (TSA) sobre los valores hash SHA-256 de la evidencia, del contenedor ZIP y del");
        L("informe. Cada archivo .tsr es la respuesta firmada digitalmente por la autoridad: certifica que");
        L("ese valor hash existía en la fecha y hora indicadas, según el reloj de la autoridad y no el del");
        L("equipo del perito ni el de Factum.");
        L();
        L("La verificación no necesita Factum, ni acceso al servidor, ni la contraseña del ZIP. Alcanza con");
        L("OpenSSL 1.1 o posterior:");
        L("  - macOS y Linux: ya viene instalado (comando \"openssl\").");
        L("  - Windows: viene con Git para Windows (\"C:\\Program Files\\Git\\usr\\bin\\openssl.exe\") o se instala");
        L("    aparte.");
        L();
        L("Todos los comandos se ejecutan en una terminal abierta en la carpeta del caso: la que contiene el");
        L("ZIP y esta carpeta \"sellos\".");
        L();
        L();
        L("1. CERTIFICADO RAÍZ DE LA AUTORIDAD");
        L("-----------------------------------");
        L("Descargá el certificado raíz desde el sitio oficial de la autoridad. No lo tomes de esta carpeta ni");
        L("de Factum: la verificación tiene que apoyarse en una fuente independiente.");
        if (authorities.Count == 0)
        {
            L();
            L("  Esta carpeta todavía no tiene sellos de tiempo.");
        }
        foreach (var (host, name, first) in authorities)
        {
            L();
            var known = KnownAuthorities.ForHost(host);
            if (known is { RootIsDer: false })
            {
                L($"  {known.ReadmeTitle}");
                L($"    1. Descargá {known.RootUrl}");
                L($"    2. Guardalo como \"{known.CaFile}\".");
            }
            else if (known is { RootIsDer: true })
            {
                L($"  {known.ReadmeTitle}");
                L($"    1. Descargá {known.RootUrl}");
                L("    2. Convertilo a PEM:");
                L($"         openssl x509 -inform DER -in {known.RootDerFileName} -out \"{known.CaFile}\"");
            }
            else
            {
                L($"  {name} ({host ?? "-"})");
                L("    El sello incluye la cadena de certificados de la autoridad. Para ver quién la emitió:");
                L($"      openssl ts -reply -in \"{TimestampBundle.Folder}/{TimestampBundle.TsrName(first)}\" -token_out -out sello.tst");
                L("      openssl pkcs7 -inform DER -in sello.tst -print_certs -noout");
                L("    El \"issuer\" del último certificado de la lista es la raíz. Descargala del sitio oficial de esa");
                L($"    autoridad y guardala en formato PEM como \"{KnownAuthorities.CaFileFor(host)}\".");
            }
        }
        L();
        L();
        L("2. VER LOS DATOS DE UN SELLO");
        L("----------------------------");
        var example = sealedItems.Count > 0 ? TimestampBundle.TsrName(sealedItems[0]) : "<archivo>.tsr";
        L($"  openssl ts -reply -in \"{TimestampBundle.Folder}/{example}\" -text");
        L();
        L("Muestra la fecha y hora certificadas (\"Time stamp\", en hora UTC), el número de serie (\"Serial");
        L("number\"), la política (\"Policy OID\") y el valor hash sellado (\"Message data\").");
        L();
        L();
        L("3. VERIFICAR");
        L("------------");
        L("El resultado esperado de cada comando es \"Verification: OK\". Cualquier otro resultado indica que");
        L("el archivo no es el sellado o que el sello no corresponde.");
        L();
        var zipName = zipFilename ?? "<archivo ZIP>";
        var pdfName = pdfFilename ?? "<informe>.docx";
        L("  a) Contenedor ZIP (no hace falta la contraseña):");
        L($"       {VerifyData(zipName, zipName, CaOfTarget(TimestampTargets.Zip) ?? genericCa)}");
        L();
        L("  b) Informe: el DOCX no está en esta carpeta. Usá la copia descargada de Factum o la presentada.");
        L("     Tiene que ser exactamente el mismo archivo: abrirlo y volver a guardarlo cambia su hash.");
        L($"       {VerifyData(pdfName, pdfName, CaOfTarget(TimestampTargets.Report) ?? genericCa)}");
        L();
        L("  c) Cada archivo de evidencia, sin abrir el ZIP: verifica que el sello corresponde al valor hash");
        L("     que figura en la tabla del informe.");
        L($"       {VerifyDigest("<sha256 del archivo>", "<archivo>", genericCa)}");
        L();
        L("     Con el archivo extraído del ZIP (requiere la contraseña, que se entrega por separado):");
        L($"       {VerifyData("<ruta al archivo extraído>", "<archivo>", genericCa)}");
        L();
        L("     Para calcular el hash SHA-256 de un archivo y compararlo con el del informe:");
        L("       macOS y Linux:  shasum -a 256 \"<archivo>\"");
        L("       Windows:        certutil -hashfile \"<archivo>\" SHA256");
        L();
        L();
        L("4. SELLOS DE ESTA CARPETA");
        L("-------------------------");
        foreach (var t in sealedItems)
        {
            L();
            L(t.Filename);
            L($"  Tipo: {(IsSession(t) ? SessionTypeLabel : TypeLabel(t.Target))}");
            if (IsSession(t)) L($"  Dentro del ZIP: \"{SessionZipFolder}/{t.Filename}\"");
            L($"  SHA-256: {t.Sha256}");
            L($"  Autoridad: {t.Authority} ({t.TsaUrl})");
            var gen = t.GenTime ?? DateTime.MinValue;
            L($"  Fecha y hora certificadas: {TimestampFormat.Utc(gen)} UTC ({TimestampFormat.Display(gen, zone)})");
            L($"  N.º de serie: {t.SerialNumber}");
            L($"  Archivo del sello: \"{TimestampBundle.Folder}/{TimestampBundle.TsrName(t)}\"");
            L("  Verificar:");
            L($"    {VerifyCommand(t, CaOf(t))}");
            if (t.Target == TimestampTargets.Zip && t.InReport != true)
            {
                L("  Nota: este sello se obtuvo después de emitir el informe; el informe indica que el contenedor no");
                L("  tenía sello de tiempo al momento de su emisión.");
            }
        }
        if (selection.Unsealed.Count > 0)
        {
            L();
            L("Sin sello de tiempo:");
            foreach (var u in selection.Unsealed)
                L($"  - \"{u.Filename}\": {u.Reason}");
        }
        L();
        L();
        L("5. QUÉ CERTIFICA Y QUÉ NO");
        L("-------------------------");
        L("- Cada sello certifica que el valor hash existía en la fecha y hora indicadas. No certifica quién");
        L("  generó el archivo.");
        L("- En la evidencia, el sello se pidió al incorporar cada archivo al caso: prueba que el archivo");
        L("  existía así en ese momento, no el instante exacto de la captura.");
        L("- Un sello obtenido después de emitido el informe lleva su fecha real, posterior a la emisión.");
        L("- Factum no afirma la validez legal de ninguna autoridad de sellado de tiempo en particular.");
        return sb.ToString();
    }

    /// <summary>El comando exacto de 3a, 3b o 3c (digest) para un sello.</summary>
    public static string VerifyCommand(CaseTimestamp t, string ca) => t.Target == TimestampTargets.Evidence
        ? VerifyDigest(t.Sha256, t.Filename, ca)
        : VerifyData(t.Filename, t.Filename, ca);

    private static string VerifyData(string data, string sealedName, string ca) =>
        $"openssl ts -verify -data \"{data}\" -in \"{TimestampBundle.Folder}/{sealedName}.tsr\" -CAfile \"{ca}\"";

    private static string VerifyDigest(string sha256, string sealedName, string ca) =>
        $"openssl ts -verify -digest {sha256} -in \"{TimestampBundle.Folder}/{sealedName}.tsr\" -CAfile \"{ca}\"";

    internal const string SessionTypeLabel = "Tramo de la grabación de la sesión";
    internal const string SessionZipFolder = "sesion";

    private static string TypeLabel(string target) => target switch
    {
        TimestampTargets.Zip => "Contenedor ZIP",
        TimestampTargets.Report => "Informe",
        _ => "Evidencia",
    };
}
