using System.IO.Compression;
using System.Text;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Timestamping;

/// <summary>Objeto sin sello <c>granted</c> que el LEEME lista en "Sin sello de tiempo".</summary>
public sealed record UnsealedEntry(string Filename, string Reason);

/// <summary>Lo que va a <c>sellos/</c>: los sellos obtenidos (ZIP, informe, evidencia) y lo que quedó sin sello.</summary>
public sealed record TimestampSelection(IReadOnlyList<CaseTimestamp> Sealed, IReadOnlyList<UnsealedEntry> Unsealed);

/// <summary>
/// Qué entra en <c>timestamps/files</c> y en <c>timestamps/bundle</c> (sellado-tiempo §6.6), puro: la evidencia del
/// manifiesto ACTUAL con sello <c>granted</c> para su hash actual, el sello del ZIP con <c>Sha256 == ZipHash</c> y el
/// del informe con <c>Sha256 == ReportHash</c>. Orden: ZIP, informe y evidencia (ordinal).
/// </summary>
public static class TimestampBundle
{
    public const string Folder = "sellos";
    public const string ReadmeName = "LEEME-verificacion.txt";
    public const string ReasonPending = "pendiente (la autoridad no respondió; se reintenta)";
    public const string ReasonFailed = "no se pudo obtener";
    public const string ReasonNone = "registrado antes de activar el sellado";

    public static TimestampSelection Select(Case cas, IEnumerable<CaseTimestamp> all)
    {
        var list = all.ToList();
        var sealedList = new List<CaseTimestamp>();
        var unsealed = new List<UnsealedEntry>();

        void Add(string target, string? filename, string? sha256)
        {
            if (string.IsNullOrEmpty(filename) || string.IsNullOrEmpty(sha256)) return;
            var matches = list.Where(t => t.Target == target && t.Sha256 == sha256 &&
                                          (target != TimestampTargets.Evidence || t.Filename == filename)).ToList();
            var granted = matches.FirstOrDefault(t => t.Status == TimestampStatuses.Granted);
            if (granted is not null)
            {
                sealedList.Add(granted);
                return;
            }
            var reason = matches.Any(t => t.Status == TimestampStatuses.Pending) ? ReasonPending
                : matches.Any(t => t.Status == TimestampStatuses.Failed) ? ReasonFailed
                : ReasonNone;
            unsealed.Add(new UnsealedEntry(filename, reason));
        }

        Add(TimestampTargets.Zip, cas.ZipFilename, cas.ZipHash);
        Add(TimestampTargets.Report, cas.PdfFilename, cas.ReportHash);
        foreach (var e in cas.Evidence.OrderBy(e => e.Filename, StringComparer.Ordinal))
            Add(TimestampTargets.Evidence, e.Filename, e.Sha256);
        return new TimestampSelection(sealedList, unsealed);
    }

    /// <summary>Nombre del <c>.tsr</c> de un objeto: <c>&lt;filename&gt;.tsr</c>.</summary>
    public static string TsrName(CaseTimestamp t) => t.Filename + ".tsr";

    /// <summary>
    /// ZIP sin cifrar, en memoria, con <c>sellos/&lt;nombre&gt;.tsr</c> y <c>sellos/LEEME-verificacion.txt</c> (UTF-8
    /// sin BOM). Los sellos sin <c>.tsr</c> (no debería pasar) se saltean.
    /// </summary>
    public static byte[] BuildZip(IEnumerable<CaseTimestamp> sealedItems, string readme)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var t in sealedItems)
            {
                if (t.Tsr is null) continue;
                var entry = zip.CreateEntry($"{Folder}/{TsrName(t)}", CompressionLevel.Optimal);
                using var s = entry.Open();
                s.Write(t.Tsr);
            }
            var readmeEntry = zip.CreateEntry($"{Folder}/{ReadmeName}", CompressionLevel.Optimal);
            using (var s = readmeEntry.Open())
                s.Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(readme));
        }
        return ms.ToArray();
    }
}
