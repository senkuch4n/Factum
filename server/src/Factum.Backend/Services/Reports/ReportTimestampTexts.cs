using Factum.Backend.DTOs;
using Factum.Backend.Services.Timestamping;

namespace Factum.Backend.Services.Reports;

/// <summary>Datos de un sello para el informe (sellado-tiempo §8.3).</summary>
public sealed record TimestampView(string Authority, DateTime GenTimeUtc, string SerialNumber);

/// <summary>
/// null = sellado desactivado al generar. Zip null = sin sello al emitir. Evidence: solo los granted del hash actual.
/// </summary>
public sealed record ReportTimestampInfo(TimestampView? Zip, IReadOnlyDictionary<string, TimestampView> Evidence);

/// <summary>
/// Textos exactos del sellado en el informe (sellado-tiempo §8.4), puros. Siempre texto plano. El sello del
/// INFORME nunca figura (no puede contener su propio hash).
/// </summary>
public static class ReportTimestampTexts
{
    public const string NoSealLine = "Sin sello de tiempo de un tercero al emitir este informe";

    public static string Fmt(TimestampView t, TimeZoneInfo zone) => TimestampFormat.Display(t.GenTimeUtc, zone);

    /// <summary>Línea extra de la fila de un archivo de evidencia.</summary>
    public static string FileLine(TimestampView? t, TimeZoneInfo zone) =>
        t is null ? NoSealLine : $"Sello de tiempo: {Fmt(t, zone)} · {t.Authority}";

    /// <summary>Línea extra de la fila del ZIP (después de "Contenedor de la evidencia").</summary>
    public static string ZipLine(TimestampView? t, TimeZoneInfo zone) =>
        t is null ? NoSealLine : $"Sello de tiempo: {Fmt(t, zone)} · {t.Authority} · N.º de serie {t.SerialNumber}";

    /// <summary>
    /// Bloque <c>{descripcionSelloTiempo}</c>: un párrafo por línea. <paramref name="files"/> = los archivos de la
    /// tabla de hashes, en su orden.
    /// </summary>
    public static string Block(ReportTimestampInfo info, IReadOnlyList<FileInfoDto> files, TimeZoneInfo zone)
    {
        var m = files.Count;
        var sealedFiles = files.Where(f => info.Evidence.ContainsKey(f.Name)).ToList();
        var n = sealedFiles.Count;

        var names = new List<string>();
        void AddName(string a)
        {
            if (!string.IsNullOrWhiteSpace(a) && !names.Contains(a, StringComparer.Ordinal)) names.Add(a);
        }
        if (info.Zip is { } z) AddName(z.Authority);
        foreach (var f in sealedFiles) AddName(info.Evidence[f.Name].Authority);
        var autoridades = JoinSpanish(names);

        if (info.Zip is { } zip && names.Count > 0)
        {
            return string.Join("\n",
                "Los valores hash SHA-256 de la evidencia fueron sellados por una autoridad de sellado de tiempo " +
                $"independiente ({autoridades}), conforme al estándar RFC 3161. Cada sello es una constancia firmada " +
                "digitalmente por esa autoridad que certifica que el valor hash existía en la fecha y hora indicadas, " +
                "tomadas del reloj de la autoridad y no del equipo del perito ni del sistema utilizado.",
                $"El sello del contenedor de la evidencia fue emitido el {Fmt(zip, zone)}, con número de serie " +
                $"{zip.SerialNumber}. {n} de {m} archivos cuentan además con un sello propio, obtenido al incorporarlos " +
                "al caso; la tabla anterior indica la fecha y hora certificadas de cada uno.",
                "Los sellos se entregan como archivos .tsr en la carpeta \"sellos\" que acompaña al contenedor y pueden " +
                "verificarse con herramientas estándar (por ejemplo, openssl ts -verify) siguiendo el procedimiento del " +
                "archivo LEEME-verificacion.txt, sin necesidad de la contraseña del contenedor.");
        }

        var first =
            "El contenedor de la evidencia no cuenta con sello de tiempo de un tercero al momento de emitir este " +
            "informe: la autoridad de sellado de tiempo no respondió. Si el sello se obtiene después, llevará su " +
            "fecha real, posterior a la emisión de este informe.";
        var second = n == 0 || names.Count == 0
            ? "Ningún archivo de la evidencia cuenta con sello de tiempo de un tercero al emitir este informe."
            : $"{n} de {m} archivos cuentan con un sello de tiempo propio conforme al estándar RFC 3161, emitido por " +
              $"una autoridad independiente ({autoridades}) al incorporarlos al caso; la tabla anterior indica la fecha " +
              "y hora certificadas de cada uno. Los sellos se entregan como archivos .tsr en la carpeta \"sellos\" y " +
              "pueden verificarse con openssl ts -verify siguiendo el archivo LEEME-verificacion.txt.";
        return first + "\n" + second;
    }

    /// <summary>"A", "A y B", "A, B y C".</summary>
    public static string JoinSpanish(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " y " + items[^1],
    };
}
