using System.Text;
using Factum.Agent.Common;

namespace Factum.Agent.Services.Extraction;

/// <summary>Resultado de copiar una categoría de archivos. <paramref name="DeviceHashed"/> = archivos con
/// <c>coincide</c> si o no (es decir, con hash del dispositivo realmente comparado).</summary>
public sealed record FileCategoryResult(string Status, int Files, long Bytes, int Errors, int HashMismatches,
    int DeviceHashed, List<ManifestEntry> Entries);

/// <summary>Total del inventario de una categoría (find + stat).</summary>
public sealed record InventoryResult(int Files, long Bytes);

/// <summary>
/// Copia de archivos de <c>/sdcard</c> con <c>adb pull -a</c> por raíz, SHA-256 en la PC y —si el dispositivo tiene
/// <c>sha256sum</c>— también en el dispositivo (D10), con asiento en el manifiesto y en <c>registro/</c>
/// (extraccion-logica-android §5.4). El mapeo de rutas usa <see cref="DevicePaths"/>.
/// </summary>
public sealed class FileExtractor(string adbPath, string serial, string folder, ExtractionCommandLog? cmdLog = null)
{
    /// <summary>
    /// Inventario del dispositivo (§5.4.3): <c>find &lt;raíz&gt;/ -type f -exec stat -c '%s|%Y|%n' {} +</c> por raíz.
    /// Escribe <c>registro/inventario_dispositivo.tsv</c> y devuelve el total real de archivos/bytes.
    /// </summary>
    public async Task<InventoryResult> InventoryAsync(IReadOnlyList<(string Root, string Category)> roots,
        CancellationToken ct)
    {
        var tsvPath = Path.Combine(folder, "registro", "inventario_dispositivo.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(tsvPath)!);
        var sb = new StringBuilder("tamano_bytes\tmtime_iso\truta_dispositivo\testado\tmotivo\n");
        int files = 0; long bytes = 0;

        foreach (var (root, _) in roots)
        {
            ct.ThrowIfCancellationRequested();
            var script = $"find {AdbShell.RemoteQuote(root)}/ -type f -exec stat -c '%s|%Y|%n' {{}} +";
            var (res, start, end) = await RunShellAsync(["-s", serial, "shell", "-T", script], ct, TimeSpan.FromMinutes(10));
            cmdLog?.Log(adbPath, ["shell", "-T", script], res.ExitCode, res.Stderr, start, end);
            foreach (var raw in res.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parsed = ExtractionDeviceQueries.ParseStatLine(raw);
                if (parsed is not { } row) { sb.Append($"0\t\t{Tsv(raw)}\terror\tnombre_no_soportado\n"); continue; }
                files++; bytes += row.Size;
                sb.Append($"{row.Size}\t{row.MtimeIso}\t{Tsv(row.Path)}\tok\t\n");
            }
        }
        await File.WriteAllTextAsync(tsvPath, sb.ToString(), new UTF8Encoding(false), ct);
        return new InventoryResult(files, bytes);
    }

    /// <summary>
    /// SHA-256 en el dispositivo por raíz (§5.4.5): <c>find &lt;raíz&gt;/ -type f -exec sha256sum {} +</c>. Vuelca el
    /// crudo a <c>registro/hashes_dispositivo.txt</c> y devuelve el mapa ruta_dispositivo → hash.
    /// </summary>
    public async Task<Dictionary<string, string>> DeviceHashesAsync(IReadOnlyList<(string Root, string Category)> roots,
        CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var rawPath = Path.Combine(folder, "registro", "hashes_dispositivo.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(rawPath)!);
        var raw = new StringBuilder();
        foreach (var (root, _) in roots)
        {
            ct.ThrowIfCancellationRequested();
            var script = $"find {AdbShell.RemoteQuote(root)}/ -type f -exec sha256sum {{}} +";
            var (res, start, end) = await RunShellAsync(["-s", serial, "shell", "-T", script], ct, TimeSpan.FromMinutes(20));
            cmdLog?.Log(adbPath, ["shell", "-T", script], res.ExitCode, res.Stderr, start, end);
            raw.Append(res.Stdout).Append('\n');
            foreach (var (path, hash) in ExtractionDeviceQueries.ParseDeviceHashes(res.Stdout))
                map[path] = hash;
        }
        await File.WriteAllTextAsync(rawPath, raw.ToString(), new UTF8Encoding(false), ct);
        return map;
    }

    /// <summary>
    /// Copia una raíz a <c>archivos/</c> y asienta cada archivo con su SHA-256 de la PC y, si viene
    /// <paramref name="deviceHashes"/>, la columna <c>coincide</c> (D10). onBytes reporta avance.
    /// </summary>
    public async Task<FileCategoryResult> CopyRootAsync(string normalizedRoot, string category,
        IReadOnlyDictionary<string, string>? deviceHashes, Action<long>? onBytes, CancellationToken ct)
    {
        var entries = new List<ManifestEntry>();
        int files = 0, errors = 0, mismatches = 0, deviceHashed = 0;
        long bytes = 0;

        var localRootRel = DevicePaths.ToLocal(normalizedRoot).RelativePath; // archivos/sdcard/<root>
        var localRootAbs = Path.Combine(folder, localRootRel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Directory.GetParent(localRootAbs)!.FullName);

        ProcessResult pull; DateTime pStart, pEnd;
        try { (pull, pStart, pEnd) = await RunRawAsync(["-s", serial, "pull", "-a", normalizedRoot + "/", localRootAbs], ct, TimeSpan.FromHours(6)); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new FileCategoryResult(CategoryStatuses.DoneWithErrors, 0, 0, 1, 0, 0,
                [ErrorEntry(localRootRel, normalizedRoot, category, ex.Message)]);
        }
        cmdLog?.Log(adbPath, ["pull", "-a", normalizedRoot + "/", localRootAbs], pull.ExitCode, pull.Stderr, pStart, pEnd);

        if (!Directory.Exists(localRootAbs))
            return new FileCategoryResult(CategoryStatuses.Done, 0, 0, 0, 0, 0, entries); // raíz vacía / inexistente

        foreach (var abs in Directory.EnumerateFiles(localRootAbs, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(folder, abs).Replace(Path.DirectorySeparatorChar, '/');
            var devicePath = "/sdcard/" + rel["archivos/sdcard/".Length..];
            try
            {
                var size = new FileInfo(abs).Length;
                var sha = await FileHashing.Sha256Async(abs, ct);
                var matches = ExtractionDeviceQueries.Coincide(deviceHashes, devicePath, sha, out var mismatch);
                var deviceSha = deviceHashes is not null && deviceHashes.TryGetValue(devicePath, out var dh) ? dh : null;
                if (mismatch) mismatches++;
                if (matches is ManifestMatch.Yes or ManifestMatch.No) deviceHashed++; // hash del dispositivo comparado
                entries.Add(new ManifestEntry(rel, devicePath, "adb pull", category, ManifestTypes.Evidence, size,
                    IsoMtime(abs), sha, deviceSha, matches, false, ManifestStates.Ok, null));
                files++; bytes += size; onBytes?.Invoke(bytes);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { entries.Add(ErrorEntry(rel, devicePath, category, ex.Message)); errors++; }
        }

        var status = errors > 0 ? CategoryStatuses.DoneWithErrors : CategoryStatuses.Done;
        return new FileCategoryResult(status, files, bytes, errors, mismatches, deviceHashed, entries);
    }

    // ── helpers ──

    private async Task<(ProcessResult Res, DateTime Start, DateTime End)> RunShellAsync(IReadOnlyList<string> args,
        CancellationToken ct, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        var res = await ProcessRunner.RunArgumentListAsync(adbPath, args, ct, timeout: timeout);
        return (res, start, DateTime.UtcNow);
    }

    private Task<(ProcessResult, DateTime, DateTime)> RunRawAsync(IReadOnlyList<string> args, CancellationToken ct,
        TimeSpan timeout) => RunShellAsync(args, ct, timeout);

    private static ManifestEntry ErrorEntry(string rel, string devicePath, string category, string reason) =>
        new(rel, devicePath, "adb pull", category, ManifestTypes.Evidence, 0, null, null, null,
            ManifestMatch.NotComputed, false, ManifestStates.Error, Short(reason));

    private static string IsoMtime(string abs) =>
        File.GetLastWriteTimeUtc(abs).ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

    private static string Tsv(string s) => s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n");
    private static string Short(string s) => s.Length > 200 ? s[..200] : s;
}
