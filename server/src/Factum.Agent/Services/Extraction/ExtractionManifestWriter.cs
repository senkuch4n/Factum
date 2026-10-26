using System.Text;
using Factum.Agent.Common;

namespace Factum.Agent.Services.Extraction;

/// <summary>Una fila del MANIFIESTO.tsv (extraccion-logica-android §9.2). Orden de columnas fijo.</summary>
public sealed record ManifestEntry(
    string RelativePath,
    string DevicePath,
    string Origin,
    string Category,
    string Type,
    long SizeBytes,
    string? MtimeDeviceIso,
    string? Sha256Pc,
    string? Sha256Device,
    string Matches,
    bool NameAdjusted,
    string State,
    string? Reason);

/// <summary>Valores de la columna <c>coincide</c> (§9.2).</summary>
public static class ManifestMatch
{
    public const string Yes = "si", No = "no", NotComputed = "no_calculado", NotApplicable = "no_aplica";
}

/// <summary>Valores de la columna <c>tipo</c> (§9.1/§9.2).</summary>
public static class ManifestTypes
{
    public const string Evidence = "evidencia", Derived = "derivado", Record = "registro", Acta = "acta";
}

/// <summary>Valores de la columna <c>estado</c>.</summary>
public static class ManifestStates
{
    public const string Ok = "ok", Error = "error";
}

/// <summary>
/// Escribe el manifiesto de la extracción (extraccion-logica-android §9.2). Incremental en
/// <c>.factum/manifiesto.parcial.tsv</c> (append + flush por fila) y, al cerrar, ordenado por <c>ruta_relativa</c>
/// (ordinal) en <c>MANIFIESTO.tsv</c> + <c>.sha256</c> + <c>SHA256SUMS.txt</c>. UTF-8 sin BOM, <c>\n</c>. La parte de
/// formato/escape es pura y testeable.
/// </summary>
public sealed class ExtractionManifestWriter : IAsyncDisposable
{
    public const string Header =
        "ruta_relativa\truta_dispositivo\torigen\tcategoria\ttipo\ttamano_bytes\tmtime_dispositivo\t" +
        "sha256_pc\tsha256_dispositivo\tcoincide\tnombre_ajustado\testado\tmotivo";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _partialPath;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ExtractionManifestWriter(string workDir)
    {
        Directory.CreateDirectory(workDir);
        _partialPath = Path.Combine(workDir, "manifiesto.parcial.tsv");
        _writer = new StreamWriter(new FileStream(_partialPath, FileMode.Append, FileAccess.Write, FileShare.Read),
            Utf8NoBom)
        { NewLine = "\n", AutoFlush = true };
    }

    /// <summary>Ruta del parcial (para la recuperación).</summary>
    public string PartialPath => _partialPath;

    public async Task AppendAsync(ManifestEntry entry, CancellationToken ct = default)
    {
        var line = FormatRow(entry);
        await _lock.WaitAsync(ct);
        try { await _writer.WriteLineAsync(line.AsMemory(), ct); await _writer.FlushAsync(ct); }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Cierra el manifiesto: lee el parcial, ordena por <c>ruta_relativa</c> y escribe MANIFIESTO.tsv, su
    /// <c>.sha256</c> (formato sha256sum) y SHA256SUMS.txt (filas ok con hash). Devuelve el hash y la cantidad de
    /// entradas. Idempotente: puede reabrirse con las filas presentes.
    /// </summary>
    public static async Task<(string Sha256, int Entries)> CloseAsync(string workDir, string finalDir,
        CancellationToken ct = default)
    {
        var partial = Path.Combine(workDir, "manifiesto.parcial.tsv");
        var rows = File.Exists(partial)
            ? (await File.ReadAllLinesAsync(partial, Utf8NoBom, ct)).Where(l => l.Length > 0).ToList()
            : [];
        // Orden estable por la primera columna (ruta_relativa), ordinal.
        rows.Sort((a, b) => string.CompareOrdinal(FirstColumn(a), FirstColumn(b)));

        var manifestPath = Path.Combine(finalDir, "MANIFIESTO.tsv");
        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');
        foreach (var r in rows) sb.Append(r).Append('\n');
        await File.WriteAllTextAsync(manifestPath, sb.ToString(), Utf8NoBom, ct);

        var sha = await FileHashing.Sha256Async(manifestPath, ct);
        await File.WriteAllTextAsync(Path.Combine(finalDir, "MANIFIESTO.tsv.sha256"),
            $"{sha}  MANIFIESTO.tsv\n", Utf8NoBom, ct);

        // SHA256SUMS.txt: filas ok con hash de la PC, en formato sha256sum (`<hash>  <ruta>`).
        var sums = new StringBuilder();
        foreach (var r in rows)
        {
            var cols = r.Split('\t');
            if (cols.Length < 13) continue;
            var rel = Unescape(cols[0]);
            var pcHash = cols[7];
            var state = cols[11];
            if (state == ManifestStates.Ok && pcHash.Length == 64)
                sums.Append(pcHash).Append("  ").Append(rel).Append('\n');
        }
        await File.WriteAllTextAsync(Path.Combine(finalDir, "SHA256SUMS.txt"), sums.ToString(), Utf8NoBom, ct);

        return (sha, rows.Count);
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        _lock.Dispose();
    }

    // ── formato puro ──

    public static string FormatRow(ManifestEntry e) => string.Join('\t',
        Escape(e.RelativePath),
        Escape(e.DevicePath),
        Escape(e.Origin),
        Escape(e.Category),
        Escape(e.Type),
        e.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Escape(e.MtimeDeviceIso ?? ""),
        Escape(e.Sha256Pc ?? ""),
        Escape(e.Sha256Device ?? ""),
        Escape(e.Matches),
        e.NameAdjusted ? "si" : "no",
        Escape(e.State),
        Escape(e.Reason ?? ""));

    /// <summary>Escapa <c>\</c>, tab y salto de línea como literales <c>\\</c>, <c>\t</c>, <c>\n</c> (§9.2).</summary>
    public static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace("\t", "\\t")
        .Replace("\r", "\\r")
        .Replace("\n", "\\n");

    public static string Unescape(string value)
    {
        if (!value.Contains('\\')) return value;
        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                var n = value[++i];
                sb.Append(n switch { 't' => '\t', 'n' => '\n', 'r' => '\r', '\\' => '\\', _ => n });
            }
            else sb.Append(value[i]);
        }
        return sb.ToString();
    }

    private static string FirstColumn(string row)
    {
        var tab = row.IndexOf('\t');
        return tab < 0 ? row : row[..tab];
    }
}
