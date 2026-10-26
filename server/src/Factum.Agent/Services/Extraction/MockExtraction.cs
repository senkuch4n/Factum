using System.Text;
using System.Text.Json.Nodes;

namespace Factum.Agent.Services.Extraction;

/// <summary>
/// Extracción simulada (extraccion-logica-android §5.12): genera en el destino real unos pocos archivos de texto
/// chicos por categoría, para probar el flujo de punta a punta SIN celular. Los destinos siguen siendo reales, así
/// que el perito puede probar el navegador y verificar con <c>sha256sum -c SHA256SUMS.txt</c>. El resultado lo fija
/// <c>Agent:MockExtractionOutcome</c>.
/// </summary>
public static class MockExtraction
{
    public static class Outcomes
    {
        public const string Completed = "completed", CompletedWithErrors = "completed_with_errors",
            Interrupted = "interrupted", ConnectorBlocked = "connector_blocked", CancelledSlow = "cancelled_slow";
    }

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Crudo + normalizado de una categoría estructurada; devuelve (filasCrudo, entradasManifiesto).</summary>
    public static async Task<List<ManifestEntry>> WriteStructuredAsync(string folder, string key, string uri,
        int rows, CancellationToken ct)
    {
        var entries = new List<ManifestEntry>();
        var crudoRel = $"datos/crudo/{key}.txt";
        var normRel = $"datos/normalizado/{key}.json";
        var crudoPath = Path.Combine(folder, "datos", "crudo", $"{key}.txt");
        var normPath = Path.Combine(folder, "datos", "normalizado", $"{key}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(crudoPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(normPath)!);

        var crudo = new StringBuilder();
        var normRows = new JsonArray();
        for (int i = 0; i < rows; i++)
        {
            crudo.Append($"Row: {i} _id={i + 1}, valor=ejemplo {i}\n");
            normRows.Add(new JsonObject { ["_id"] = (i + 1).ToString(), ["valor"] = $"ejemplo {i}" });
        }
        await File.WriteAllTextAsync(crudoPath, crudo.ToString(), Utf8NoBom, ct);
        var norm = new JsonObject
        {
            ["schema"] = "factum-extraccion-normalizado/1",
            ["derived_from"] = new JsonArray(crudoRel),
            ["source_uri"] = uri,
            ["columns"] = new JsonArray("_id", "valor"),
            ["rows"] = normRows,
            ["row_count"] = rows,
            ["parse_warnings"] = 0,
        };
        await File.WriteAllTextAsync(normPath, norm.ToJsonString(), Utf8NoBom, ct);

        entries.Add(await EntryAsync(folder, crudoRel, "", uri, key, ManifestTypes.Evidence, ct));
        entries.Add(await EntryAsync(folder, normRel, "", uri, key, ManifestTypes.Derived, ct));
        return entries;
    }

    /// <summary>Genera <paramref name="count"/> archivos chicos de una categoría de archivos en archivos/.</summary>
    public static async Task<List<ManifestEntry>> WriteFilesAsync(string folder, string key, int count,
        CancellationToken ct)
    {
        var entries = new List<ManifestEntry>();
        for (int i = 0; i < count; i++)
        {
            var rel = $"archivos/sdcard/{key}/archivo_{i}.txt";
            var abs = Path.Combine(folder, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
            await File.WriteAllTextAsync(abs, $"contenido simulado {key} #{i}\n", Utf8NoBom, ct);
            var e = await EntryAsync(folder, rel, "/sdcard/" + rel["archivos/sdcard/".Length..], "adb pull", key,
                ManifestTypes.Evidence, ct);
            entries.Add(e);
        }
        return entries;
    }

    private static async Task<ManifestEntry> EntryAsync(string folder, string rel, string devicePath, string origin,
        string category, string type, CancellationToken ct)
    {
        var abs = Path.Combine(folder, rel.Replace('/', Path.DirectorySeparatorChar));
        var size = new FileInfo(abs).Length;
        var sha = await Common.FileHashing.Sha256Async(abs, ct);
        var isFile = type == ManifestTypes.Evidence && devicePath.StartsWith("/sdcard", StringComparison.Ordinal);
        return new ManifestEntry(
            RelativePath: rel,
            DevicePath: devicePath,
            Origin: origin,
            Category: category,
            Type: type,
            SizeBytes: size,
            MtimeDeviceIso: isFile ? "2026-10-25T10:00:00Z" : null,
            Sha256Pc: sha,
            Sha256Device: isFile ? sha : null,
            Matches: isFile ? ManifestMatch.Yes : ManifestMatch.NotApplicable,
            NameAdjusted: false,
            State: ManifestStates.Ok,
            Reason: null);
    }
}
