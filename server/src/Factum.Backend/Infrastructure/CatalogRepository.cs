using Factum.Backend.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Factum.Backend.Infrastructure;

/// <summary>Uso de un valor de catálogo a sumar (siembra o alta automática al guardar un caso).</summary>
public sealed record CatalogUsage(string Catalog, string Value, string Key, int Count, DateTime UsedAt);

public interface ICatalogRepository
{
    Task<List<CatalogEntry>> ListByOwnerAsync(string ownerDni, CancellationToken ct = default);
    Task<CatalogEntry?> FindAsync(string ownerDni, string catalog, string id, CancellationToken ct = default);
    /// <summary>Entrada del perito y catálogo con esa clave normalizada (para el mensaje del 409).</summary>
    Task<CatalogEntry?> FindByKeyAsync(string ownerDni, string catalog, string key, CancellationToken ct = default);
    /// <summary>Upsert por (OwnerDni, Catalog, NormalizedKey). BulkWrite unordered; ignora E11000 (carrera).</summary>
    Task UpsertUsageAsync(string ownerDni, IReadOnlyList<CatalogUsage> usages, CancellationToken ct = default);
    /// <summary>Cambia Value + NormalizedKey + UpdatedAt. Devuelve null si no matcheó; lanza DuplicateKey si choca.</summary>
    Task<CatalogEntry?> UpdateValueAsync(string ownerDni, string catalog, string id, string value, string key,
        CancellationToken ct = default);
    Task<bool> DeleteAsync(string ownerDni, string catalog, string id, CancellationToken ct = default);
    Task<bool> IsSeededAsync(string ownerDni, CancellationToken ct = default);
    /// <summary>Upsert por _id (= DNI) en catalog_seeds.</summary>
    Task MarkSeededAsync(string ownerDni, CancellationToken ct = default);
}

/// <summary>
/// Colecciones <c>catalog_entries</c> y <c>catalog_seeds</c>. Todas las consultas y escrituras
/// filtran por <c>OwnerDni</c> (D7): una entrada de otro perito no matchea. Nunca toca <c>cases</c>.
/// </summary>
public sealed class CatalogRepository : ICatalogRepository
{
    public const string EntriesCollection = "catalog_entries";
    public const string SeedsCollection = "catalog_seeds";

    private readonly IMongoCollection<CatalogEntry> _entries;
    private readonly IMongoCollection<CatalogSeed> _seeds;
    private Task? _indexTask;

    public CatalogRepository(IOptions<MongoOptions> opts)
    {
        var client = new MongoClient(opts.Value.ConnectionString);
        var db = client.GetDatabase(opts.Value.DatabaseName);
        _entries = db.GetCollection<CatalogEntry>(EntriesCollection);
        _seeds = db.GetCollection<CatalogSeed>(SeedsCollection);
        // Índices en segundo plano al arrancar, como CaseRepository (CancellationToken.None).
        _ = EnsureIndexesAsync();
    }

    private Task EnsureIndexesAsync()
    {
        // Reintenta si el intento anterior falló o se canceló.
        if (_indexTask is null || _indexTask.IsFaulted || _indexTask.IsCanceled)
        {
            var keys = Builders<CatalogEntry>.IndexKeys;
            _indexTask = _entries.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<CatalogEntry>(
                    keys.Ascending(e => e.OwnerDni).Ascending(e => e.Catalog).Ascending(e => e.NormalizedKey),
                    new CreateIndexOptions { Name = "owner_catalog_key_unique", Unique = true }),
                new CreateIndexModel<CatalogEntry>(
                    keys.Ascending(e => e.OwnerDni).Descending(e => e.LastUsedAt),
                    new CreateIndexOptions { Name = "owner_lastused" }),
            ], cancellationToken: CancellationToken.None);
        }
        return _indexTask;
    }

    private static FilterDefinition<CatalogEntry> Owned(string ownerDni, string catalog, string id) =>
        Builders<CatalogEntry>.Filter.Eq(e => e.Id, id) &
        Builders<CatalogEntry>.Filter.Eq(e => e.OwnerDni, ownerDni) &
        Builders<CatalogEntry>.Filter.Eq(e => e.Catalog, catalog);

    public Task<List<CatalogEntry>> ListByOwnerAsync(string ownerDni, CancellationToken ct = default) =>
        _entries.Find(e => e.OwnerDni == ownerDni)
            .SortByDescending(e => e.LastUsedAt)
            .ToListAsync(ct);

    public async Task<CatalogEntry?> FindAsync(string ownerDni, string catalog, string id,
        CancellationToken ct = default) =>
        await _entries.Find(Owned(ownerDni, catalog, id)).FirstOrDefaultAsync(ct);

    public async Task<CatalogEntry?> FindByKeyAsync(string ownerDni, string catalog, string key,
        CancellationToken ct = default) =>
        await _entries.Find(e => e.OwnerDni == ownerDni && e.Catalog == catalog && e.NormalizedKey == key)
            .FirstOrDefaultAsync(ct);

    public async Task UpsertUsageAsync(string ownerDni, IReadOnlyList<CatalogUsage> usages,
        CancellationToken ct = default)
    {
        if (usages.Count == 0) return;
        await EnsureIndexesAsync(); // el índice único es el que hace seguro el upsert concurrente

        var now = DateTime.UtcNow;
        var f = Builders<CatalogEntry>.Filter;
        var u = Builders<CatalogEntry>.Update;
        var models = usages.Select(x => new UpdateOneModel<CatalogEntry>(
            f.Eq(e => e.OwnerDni, ownerDni) & f.Eq(e => e.Catalog, x.Catalog) & f.Eq(e => e.NormalizedKey, x.Key),
            u.SetOnInsert(e => e.Id, Guid.NewGuid().ToString())
                .SetOnInsert(e => e.Value, x.Value) // primera grafía (D8)
                .SetOnInsert(e => e.CreatedAt, now)
                .SetOnInsert(e => e.UpdatedAt, now)
                .Inc(e => e.UseCount, x.Count)
                .Max(e => e.LastUsedAt, x.UsedAt))
        { IsUpsert = true }).ToList();
        // OwnerDni, Catalog y NormalizedKey se insertan solos desde los Eq del filtro del upsert.

        try
        {
            await _entries.BulkWriteAsync(models, new BulkWriteOptions { IsOrdered = false }, ct);
        }
        catch (MongoBulkWriteException ex) when (
            ex.WriteErrors.Count > 0 &&
            ex.WriteErrors.All(e => e.Category == ServerErrorCategory.DuplicateKey) &&
            ex.WriteConcernError is null)
        {
            // Dos upserts concurrentes de la misma clave: la otra escritura ya la creó.
        }
    }

    public async Task<CatalogEntry?> UpdateValueAsync(string ownerDni, string catalog, string id,
        string value, string key, CancellationToken ct = default)
    {
        await EnsureIndexesAsync();
        // UpdateOne (y no FindOneAndUpdate) para que un choque con el índice único salga como
        // MongoWriteException con Category = DuplicateKey, que es lo que atrapa el servicio.
        var res = await _entries.UpdateOneAsync(
            Owned(ownerDni, catalog, id),
            Builders<CatalogEntry>.Update
                .Set(e => e.Value, value)
                .Set(e => e.NormalizedKey, key)
                .Set(e => e.UpdatedAt, DateTime.UtcNow),
            cancellationToken: ct);
        return res.MatchedCount == 0 ? null : await FindAsync(ownerDni, catalog, id, ct);
    }

    // DeleteOne por {_id, OwnerDni, Catalog}. Nunca DeleteMany.
    public async Task<bool> DeleteAsync(string ownerDni, string catalog, string id, CancellationToken ct = default)
    {
        var res = await _entries.DeleteOneAsync(Owned(ownerDni, catalog, id), ct);
        return res.DeletedCount > 0;
    }

    public async Task<bool> IsSeededAsync(string ownerDni, CancellationToken ct = default) =>
        await _seeds.Find(s => s.Id == ownerDni).AnyAsync(ct);

    public Task MarkSeededAsync(string ownerDni, CancellationToken ct = default) =>
        _seeds.UpdateOneAsync(
            s => s.Id == ownerDni,
            Builders<CatalogSeed>.Update.SetOnInsert(s => s.SeededAt, DateTime.UtcNow),
            new UpdateOptions { IsUpsert = true },
            ct);
}
