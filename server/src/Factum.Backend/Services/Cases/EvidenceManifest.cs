using System.Text.RegularExpressions;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Reports;

namespace Factum.Backend.Services.Cases;

/// <summary>
/// Códigos estables (campo <c>code</c> del JSON) del flujo "evidencia en la PC del perito"
/// (zip-local-informe-servidor §5.1 y §8.1).
/// </summary>
public static class EvidenceErrorCodes
{
    public const string EvidenceOnAgent = "evidence_on_agent";
    public const string EvidenceOnServer = "evidence_on_server";
    public const string EvidenceOnOtherPc = "evidence_on_other_pc";
    public const string CaseNotEditable = "case_not_editable";
    public const string InvalidManifest = "invalid_manifest";
    public const string NoEvidence = "no_evidence";
    public const string GenerationStale = "generation_stale";
    public const string ManifestMismatch = "manifest_mismatch";
    public const string MissingImages = "missing_images";
    public const string ImageHashMismatch = "image_hash_mismatch";
    public const string InvalidZipInfo = "invalid_zip_info";
    public const string RequestTooLarge = "request_too_large";
}

/// <summary>Ítem del manifiesto ya validado y normalizado (sha256 en minúscula, nombre sin espacios).</summary>
public sealed record ValidatedEvidenceItem(string Filename, long Size, string Sha256, string? SourcePath);

/// <summary>Resultado de <see cref="EvidenceManifest.ValidateItems"/>: error (+ nombre) o los ítems normalizados.</summary>
public sealed record ManifestValidation(string? Error, string? Filename, List<ValidatedEvidenceItem> Items)
{
    public bool IsValid => Error is null;
}

/// <summary>
/// Lógica pura del manifiesto de evidencia (zip-local-informe-servidor §3.2, §5.2-§5.6). Sin E/S:
/// lo que necesita del disco entra por parámetro.
/// </summary>
public static class EvidenceManifest
{
    private static readonly Regex Sha256Regex = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    public const string EvidenceOnAgentMessage =
        "La evidencia de este caso se guarda en la PC del perito (Tatana); este endpoint es del flujo anterior";
    public const string EvidenceOnServerMessage =
        "Este caso tiene la evidencia en el servidor y sigue con el flujo anterior";
    public const string NoEvidenceMessage = "El caso no tiene archivos. Capturá evidencia primero.";
    public const string GenerationStaleMessage =
        "La evidencia del caso cambió mientras se generaba; volvé a intentar.";
    public const string ManifestMismatchMessage =
        "Los archivos verificados en la PC no coinciden con los registrados en el expediente";
    public const string MissingImagesMessage = "Faltan capturas que el informe necesita";
    public const string InvalidZipInfoMessage = "Los datos del ZIP no son válidos";

    public static string EvidenceOnOtherPcMessage(string hostname) =>
        $"La evidencia de este caso está en la PC {hostname}. Seguilo desde esa PC.";

    public static string ImageHashMismatchMessage(string filename) =>
        $"La captura {filename} no coincide con el hash registrado en el expediente";

    public static string RequestTooLargeMessage(long max) =>
        $"El pedido supera el máximo permitido ({EvidenceUpload.FormatBytes(max)})";

    /// <summary>SHA-256 en hex minúscula (64 caracteres).</summary>
    public static bool IsSha256(string? value) => value is not null && Sha256Regex.IsMatch(value);

    /// <summary>
    /// Flujo efectivo del caso (D8, §3.2): la marca persistida; si falta, <c>"server"</c> para un
    /// caso ya generado o un borrador con evidencia en el servidor, y <c>"agent"</c> en el resto.
    /// <paramref name="hasServerEvidence"/> recibe el id del caso y no debe crear nada.
    /// </summary>
    public static string ResolveStorage(Case cas, Func<string, bool> hasServerEvidence)
    {
        if (cas.EvidenceStorage is { } stored) return stored;
        if (cas.Status == CaseStatus.Completed) return EvidenceStorages.Server;
        if (hasServerEvidence(cas.Id)) return EvidenceStorages.Server;
        return EvidenceStorages.Agent;
    }

    /// <summary>
    /// Valida el cuerpo de <c>PUT …/evidence</c> (§5.1 <c>invalid_manifest</c>): host con hostname,
    /// al menos un ítem, nombres válidos y no artefactos, sha256 de 64 hex en minúscula, tamaño
    /// presente y ≥ 0, sin nombres repetidos.
    /// </summary>
    public static ManifestValidation ValidateItems(EvidenceHostDto? host, IReadOnlyList<EvidenceItemDto>? items)
    {
        if (host is null || string.IsNullOrWhiteSpace(host.Hostname))
            return new ManifestValidation("Falta el nombre de la PC (host.hostname)", null, []);
        if (items is null || items.Count == 0)
            return new ManifestValidation("No hay archivos para registrar", null, []);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ValidatedEvidenceItem>(items.Count);
        foreach (var item in items)
        {
            if (item is null)
                return new ManifestValidation("Ítem del manifiesto vacío", null, []);
            var name = item.Filename;
            if (!IsValidEvidenceName(name))
                return new ManifestValidation("Nombre de archivo inválido", name, []);
            if (!IsSha256(item.Sha256))
                return new ManifestValidation($"El hash de {name} no es un SHA-256 válido", name, []);
            if (item.Size is not { } size || size < 0)
                return new ManifestValidation($"El tamaño de {name} no es válido", name, []);
            if (!seen.Add(name!))
                return new ManifestValidation($"El archivo {name} está repetido", name, []);
            var source = string.IsNullOrWhiteSpace(item.SourcePath) ? null : item.SourcePath.Trim();
            result.Add(new ValidatedEvidenceItem(name!, size, item.Sha256!, source));
        }
        return new ManifestValidation(null, null, result);
    }

    /// <summary>Nombre aceptable para el manifiesto: válido para subir y no generado por Factum.</summary>
    public static bool IsValidEvidenceName(string? name) =>
        EvidenceUpload.IsValidUploadName(name) && !ReportService.IsGeneratedArtifact(name!);

    /// <summary>
    /// Upsert por <c>Filename</c> (§5.2): un ítem con el mismo nombre se reemplaza. Si el nuevo no
    /// trae ruta de origen se conserva la del anterior. Devuelve la lista en orden ordinal.
    /// </summary>
    public static List<EvidenceItem> Upsert(IEnumerable<EvidenceItem> existing,
        IEnumerable<ValidatedEvidenceItem> incoming, DateTime now)
    {
        var map = new Dictionary<string, EvidenceItem>(StringComparer.Ordinal);
        foreach (var e in existing) map[e.Filename] = e;
        foreach (var i in incoming)
        {
            var source = i.SourcePath ?? (map.TryGetValue(i.Filename, out var old) ? old.SourcePath : null);
            map[i.Filename] = new EvidenceItem
            {
                Filename = i.Filename,
                Size = i.Size,
                Sha256 = i.Sha256,
                SourcePath = source,
                RegisteredAt = now,
            };
        }
        return map.Values.OrderBy(e => e.Filename, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// <c>FileSources</c> con las rutas de origen de <paramref name="items"/> (una por nombre: una
    /// ruta nueva reemplaza a la anterior del mismo archivo). Así <c>EvidenceClassifier</c>,
    /// <c>ReportValues.Counts</c> y <c>ReportService</c> siguen leyendo <c>cas.FileSources</c>.
    /// </summary>
    public static List<FileSource> MergeFileSources(IEnumerable<FileSource> existing,
        IEnumerable<ValidatedEvidenceItem> items)
    {
        var list = existing.Select(s => new FileSource { Filename = s.Filename, SourcePath = s.SourcePath }).ToList();
        foreach (var item in items)
        {
            if (item.SourcePath is null) continue;
            var current = list.FindIndex(s => s.Filename == item.Filename);
            if (current >= 0)
            {
                if (list[current].SourcePath == item.SourcePath) continue;
                list.RemoveAll(s => s.Filename == item.Filename);
            }
            list.Add(new FileSource { Filename = item.Filename, SourcePath = item.SourcePath });
        }
        return list;
    }

    private static HashSet<string> SourceNames(Case cas)
    {
        var set = cas.FileSources.Select(s => s.Filename).ToHashSet(StringComparer.Ordinal);
        foreach (var e in cas.Evidence)
            if (!string.IsNullOrEmpty(e.SourcePath)) set.Add(e.Filename);
        return set;
    }

    /// <summary>¿El archivo tiene ruta de origen (explorador)? Mismo criterio que <c>EvidenceClassifier</c>.</summary>
    public static bool HasSource(Case cas, string filename) => SourceNames(cas).Contains(filename);

    /// <summary>
    /// Manifiesto como <see cref="FileInfoDto"/> (§5.4): <c>(Filename, Size, Sha256, RegisteredAt,
    /// SourcePath)</c>, en orden ordinal. La ruta de origen sale del ítem o, si no la tiene, de
    /// <c>FileSources</c>.
    /// </summary>
    public static List<FileInfoDto> ToFileInfos(Case cas)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in cas.FileSources) sources[s.Filename] = s.SourcePath;
        return cas.Evidence
            .OrderBy(e => e.Filename, StringComparer.Ordinal)
            .Select(e => new FileInfoDto(e.Filename, e.Size, e.Sha256, e.RegisteredAt,
                string.IsNullOrEmpty(e.SourcePath) ? sources.GetValueOrDefault(e.Filename) : e.SourcePath))
            .ToList();
    }

    /// <summary><c>filename → sha256</c> del manifiesto (la tabla de hashes del informe).</summary>
    public static Dictionary<string, string> Hashes(Case cas)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in cas.Evidence) map[e.Filename] = e.Sha256;
        return map;
    }

    /// <summary>Manifiesto en la forma <c>{ filename, size, sha256 }</c>, orden ordinal.</summary>
    public static List<ManifestFileDto> ToManifestFiles(Case cas) =>
        cas.Evidence
            .OrderBy(e => e.Filename, StringComparer.Ordinal)
            .Select(e => new ManifestFileDto(e.Filename, e.Size, e.Sha256))
            .ToList();

    /// <summary>
    /// Capturas que el informe puede embeber (§5.5 <c>report_images</c>): ítems del manifiesto que
    /// <c>EvidenceClassifier</c> clasifica como captura de pantalla y con tamaño &gt; 0. Orden ordinal.
    /// </summary>
    public static List<string> ReportImageNames(Case cas)
    {
        var sources = SourceNames(cas);
        return cas.Evidence
            .Where(e => e.Size > 0 &&
                        EvidenceClassifier.Classify(e.Filename, sources.Contains(e.Filename)) == EvidenceClass.Screenshot)
            .Select(e => e.Filename)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>¿<paramref name="filename"/> está en el manifiesto con tamaño &gt; 0?</summary>
    public static bool HasNonEmpty(Case cas, string filename) =>
        cas.Evidence.Any(e => e.Filename == filename && e.Size > 0);

    /// <summary>
    /// Compara lo que verificó Tatana con el manifiesto (§5.6 paso 3): mismo conjunto de nombres y,
    /// para cada uno, mismo tamaño y sha256. Devuelve los nombres que difieren (faltan, sobran,
    /// cambiaron o vienen repetidos/incompletos), en orden ordinal y sin repetir. Vacío = coinciden.
    /// </summary>
    public static List<string> Matches(IReadOnlyList<ManifestFileDto>? files, IReadOnlyList<EvidenceItem> manifest)
    {
        var mismatched = new SortedSet<string>(StringComparer.Ordinal);
        var expected = new Dictionary<string, EvidenceItem>(StringComparer.Ordinal);
        foreach (var e in manifest) expected[e.Filename] = e;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in files ?? [])
        {
            var name = f?.Filename ?? "";
            if (!seen.Add(name))
            {
                mismatched.Add(name);
                continue;
            }
            if (!expected.TryGetValue(name, out var e) || f!.Size != e.Size ||
                !string.Equals(f.Sha256, e.Sha256, StringComparison.Ordinal))
                mismatched.Add(name);
        }
        foreach (var name in expected.Keys)
            if (!seen.Contains(name)) mismatched.Add(name);
        return mismatched.ToList();
    }

    /// <summary>Comparación de nombres de PC (D-T9): sin distinguir mayúsculas.</summary>
    public static bool SameHost(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
