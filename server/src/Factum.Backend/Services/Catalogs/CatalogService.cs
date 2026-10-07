using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using MongoDB.Driver;

namespace Factum.Backend.Services.Catalogs;

public interface ICatalogService
{
    /// <summary>Las cuatro claves siempre; siembra la primera vez (SDD §5.3).</summary>
    Task<CatalogsResponse> GetAsync(string ownerDni, CancellationToken ct = default);
    Task<Result<CatalogEntryDto>> UpdateAsync(string ownerDni, string catalog, string id,
        UpdateCatalogEntryRequest req, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(string ownerDni, string catalog, string id, CancellationToken ct = default);
    /// <summary>Best-effort: nunca lanza (try/catch + LogWarning).</summary>
    Task RecordUsageAsync(string ownerDni, IEnumerable<(string Catalog, string Value)> values,
        CancellationToken ct = default);
}

public sealed class CatalogService : ICatalogService
{
    public const string UnknownCatalogMessage = "Catálogo desconocido";
    public const string EntryNotFoundMessage = "No se encontró el valor";
    public const string EmptyValueMessage = "Ingresá un valor";
    public const string ValueTooLongMessage = "El valor supera los 300 caracteres";
    public const int MaxPerCatalog = 500;

    private readonly ICatalogRepository _repo;
    private readonly ICaseRepository _cases;
    private readonly ILogger<CatalogService> _log;

    public CatalogService(ICatalogRepository repo, ICaseRepository cases, ILogger<CatalogService> log)
    {
        _repo = repo;
        _cases = cases;
        _log = log;
    }

    public static string DuplicateMessage(string existing) => $"Ya tenés «{existing}» en tus sugerencias";

    // ── GET con siembra ───────────────────────────────────────────────────────

    public async Task<CatalogsResponse> GetAsync(string ownerDni, CancellationToken ct = default)
    {
        await SeedIfNeededAsync(ownerDni, ct);
        var entries = await _repo.ListByOwnerAsync(ownerDni, ct);
        return CatalogLogic.BuildResponse(entries);
    }

    // La siembra SOLO LEE cases. Si falla, se loguea y el GET sigue con lo que haya.
    private async Task SeedIfNeededAsync(string ownerDni, CancellationToken ct)
    {
        try
        {
            if (await _repo.IsSeededAsync(ownerDni, ct)) return;

            var cases = await _cases.ListCatalogSourcesAsync(ownerDni, ct);
            var usages = CatalogLogic.SeedUsages(cases);
            await _repo.UpsertUsageAsync(ownerDni, usages, ct);
            // Después de los upserts: si algo falla antes, el próximo GET reintenta.
            await _repo.MarkSeededAsync(ownerDni, ct);
            _log.LogInformation("Catálogos sembrados para {Dni}: {Count} valores de {Cases} casos",
                ownerDni, usages.Count, cases.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo sembrar el catálogo de {Dni}", ownerDni);
        }
    }

    // ── Editar / borrar ───────────────────────────────────────────────────────

    public async Task<Result<CatalogEntryDto>> UpdateAsync(string ownerDni, string catalog, string id,
        UpdateCatalogEntryRequest req, CancellationToken ct = default)
    {
        if (!CatalogDefinitions.Exists(catalog)) return Result.NotFound<CatalogEntryDto>(UnknownCatalogMessage);

        var value = CatalogText.CleanValue(req.Value);
        if (value.Length == 0) return Result.Invalid<CatalogEntryDto>(EmptyValueMessage);
        if (value.Length > CatalogText.MaxValue) return Result.Invalid<CatalogEntryDto>(ValueTooLongMessage);
        var key = CatalogText.NormalizeKey(value);

        try
        {
            var updated = await _repo.UpdateValueAsync(ownerDni, catalog, id, value, key, ct);
            return updated is null
                ? Result.NotFound<CatalogEntryDto>(EntryNotFoundMessage)
                : Result.Ok(CatalogLogic.ToDto(updated));
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var existing = await _repo.FindByKeyAsync(ownerDni, catalog, key, ct);
            return Result.Conflict<CatalogEntryDto>(DuplicateMessage(existing?.Value ?? value));
        }
    }

    public async Task<Result<bool>> DeleteAsync(string ownerDni, string catalog, string id,
        CancellationToken ct = default)
    {
        if (!CatalogDefinitions.Exists(catalog)) return Result.NotFound<bool>(UnknownCatalogMessage);
        return await _repo.DeleteAsync(ownerDni, catalog, id, ct)
            ? Result.Ok(true)
            : Result.NotFound<bool>(EntryNotFoundMessage);
    }

    // ── Alta automática al guardar un caso (D2 A) ─────────────────────────────

    public async Task RecordUsageAsync(string ownerDni, IEnumerable<(string Catalog, string Value)> values,
        CancellationToken ct = default)
    {
        try
        {
            var usages = CatalogLogic.UsagesFor(values, DateTime.UtcNow);
            if (usages.Count == 0) return;
            await _repo.UpsertUsageAsync(ownerDni, usages, ct);
        }
        catch (Exception ex)
        {
            // Un fallo del catálogo nunca cambia la respuesta del POST/PUT del caso.
            _log.LogWarning(ex, "No se pudo actualizar el catálogo de {Dni}", ownerDni);
        }
    }
}

/// <summary>Lógica pura de catálogos (sin Mongo), testeable.</summary>
public static class CatalogLogic
{
    public static CatalogEntryDto ToDto(CatalogEntry e) => new(e.Id, e.Value, e.UseCount, e.LastUsedAt);

    /// <summary>
    /// Agrupa por catálogo (ignorando los que no están en CatalogDefinitions), ordena por
    /// last_used_at desc → use_count desc → value (ordinal ignore case) y corta en 500. Las
    /// cuatro claves siempre presentes.
    /// </summary>
    public static CatalogsResponse BuildResponse(IEnumerable<CatalogEntry> entries)
    {
        var byCatalog = entries
            .Where(e => CatalogDefinitions.Exists(e.Catalog))
            .ToLookup(e => e.Catalog, StringComparer.Ordinal);

        var dict = new Dictionary<string, List<CatalogEntryDto>>(StringComparer.Ordinal);
        foreach (var id in CatalogDefinitions.Ids)
        {
            dict[id] = byCatalog[id]
                .OrderByDescending(e => e.LastUsedAt)
                .ThenByDescending(e => e.UseCount)
                .ThenBy(e => e.Value, StringComparer.OrdinalIgnoreCase)
                .Take(CatalogService.MaxPerCatalog)
                .Select(ToDto)
                .ToList();
        }
        return new CatalogsResponse(dict);
    }

    /// <summary>
    /// Valores limpios → usos deduplicados por (catálogo, clave), conservando la primera grafía.
    /// Descarta vacíos, catálogos desconocidos y valores de más de 300 caracteres.
    /// </summary>
    public static List<CatalogUsage> UsagesFor(IEnumerable<(string Catalog, string Value)> values, DateTime usedAt)
    {
        var seen = new HashSet<(string, string)>();
        var list = new List<CatalogUsage>();
        foreach (var (catalog, raw) in values)
        {
            if (!CatalogDefinitions.Exists(catalog)) continue;
            var value = CatalogText.CleanValue(raw);
            if (value.Length == 0 || value.Length > CatalogText.MaxValue) continue;
            var key = CatalogText.NormalizeKey(value);
            if (!seen.Add((catalog, key))) continue;
            list.Add(new CatalogUsage(catalog, value, key, 1, usedAt));
        }
        return list;
    }

    /// <summary>Crear: todos los valores no vacíos del caso guardado.</summary>
    public static List<(string Catalog, string Value)> ValuesForCreate(Case cas) =>
        CatalogDefinitions.Sources
            .SelectMany(kv => kv.Value(cas).Select(v => (kv.Key, v)))
            .ToList();

    /// <summary>
    /// Editar: solo los campos cuyo NormalizeKey cambió respecto del caso antes del update
    /// (D12: guardar de nuevo sin tocar un campo no re-agrega una entrada borrada).
    /// </summary>
    public static List<(string Catalog, string Value)> ValuesForUpdate(Case saved, Case previous)
    {
        var list = new List<(string, string)>();
        foreach (var (catalog, source) in CatalogDefinitions.Sources)
        {
            var now = source(saved).ToList();
            var before = source(previous).ToList();
            for (var i = 0; i < now.Count; i++)
            {
                var prev = i < before.Count ? before[i] : null;
                if (CatalogText.NormalizeKey(now[i]) != CatalogText.NormalizeKey(prev))
                    list.Add((catalog, now[i]));
            }
        }
        return list;
    }

    /// <summary>
    /// Siembra: por cada caso (del más viejo al más nuevo) y cada catálogo, agrupa por
    /// (catálogo, clave). Grafía = la del caso más viejo; Count = casos que lo usan; UsedAt =
    /// CreatedAt más reciente. Suma "Teléfono celular" con Count 0 y UsedAt = UnixEpoch.
    /// </summary>
    public static List<CatalogUsage> SeedUsages(IEnumerable<Case> cases)
    {
        var acc = new Dictionary<(string Catalog, string Key), CatalogUsage>();
        var order = new List<(string, string)>();

        foreach (var cas in cases.OrderBy(c => c.CreatedAt))
        {
            foreach (var u in UsagesFor(ValuesForCreate(cas), cas.CreatedAt))
            {
                var k = (u.Catalog, u.Key);
                if (acc.TryGetValue(k, out var prev))
                    acc[k] = prev with
                    {
                        Count = prev.Count + 1,
                        UsedAt = u.UsedAt > prev.UsedAt ? u.UsedAt : prev.UsedAt,
                    };
                else
                {
                    acc[k] = u;
                    order.Add(k);
                }
            }
        }

        var defaultKey = (CatalogDefinitions.TiposDispositivo,
            CatalogText.NormalizeKey(CatalogDefinitions.DefaultTipoDispositivo));
        if (!acc.ContainsKey(defaultKey))
        {
            acc[defaultKey] = new CatalogUsage(CatalogDefinitions.TiposDispositivo,
                CatalogDefinitions.DefaultTipoDispositivo, defaultKey.Item2, 0, DateTime.UnixEpoch);
            order.Add(defaultKey);
        }

        return order.Select(k => acc[k]).ToList();
    }
}
