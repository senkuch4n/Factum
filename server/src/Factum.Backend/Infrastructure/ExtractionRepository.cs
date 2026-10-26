using Factum.Backend.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Factum.Backend.Infrastructure;

/// <summary>
/// Colección <c>extractions</c> (extraccion-logica-android §6.1). Solo del dueño (<c>Officer.Dni</c>). Nunca borra.
/// Cliente Mongo propio por <see cref="MongoOptions"/> e índices en segundo plano, como <c>CaseEventRepository</c>.
/// </summary>
public interface IExtractionRepository
{
    Task<Extraction?> FindByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Extracciones del dueño, más recientes primero (máx. 200). Con <paramref name="caseId"/>, solo las de ese caso.
    /// Con <paramref name="unlinkedOnly"/>, solo las sin caso y en estado terminal.
    /// </summary>
    Task<List<Extraction>> ListByOfficerAsync(string dni, string? caseId = null, bool unlinkedOnly = false,
        CancellationToken ct = default);

    /// <summary>
    /// Upsert idempotente filtrando por <c>{ _id, "Officer.Dni" }</c>. Devuelve false si el <c>_id</c> ya existe con
    /// OTRO dueño (el servicio responde 404). El <see cref="Extraction.Officer"/> lo fija el servicio.
    /// </summary>
    Task<bool> UpsertAsync(Extraction ext, CancellationToken ct = default);

    /// <summary>
    /// Asocia (o desasocia con <paramref name="caseId"/> null) filtrando por dueño. Devuelve la extracción como quedó,
    /// o null si no es del dueño o no existe.
    /// </summary>
    Task<Extraction?> SetCaseAsync(string id, string dni, string? caseId, DateTime? linkedAt,
        CancellationToken ct = default);
}

public sealed class ExtractionRepository : IExtractionRepository
{
    public const string CollectionName = "extractions";
    private readonly IMongoCollection<Extraction> _col;
    private Task? _indexTask;

    public ExtractionRepository(IOptions<MongoOptions> opts)
    {
        var client = new MongoClient(opts.Value.ConnectionString);
        var db = client.GetDatabase(opts.Value.DatabaseName);
        _col = db.GetCollection<Extraction>(CollectionName);
        _ = EnsureIndexesAsync();
    }

    private Task EnsureIndexesAsync()
    {
        if (_indexTask is null || _indexTask.IsFaulted || _indexTask.IsCanceled)
        {
            var keys = Builders<Extraction>.IndexKeys;
            _indexTask = _col.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Extraction>(
                    keys.Ascending("Officer.Dni").Descending(e => e.StartedAtPc),
                    new CreateIndexOptions { Name = "ix_extractions_officer_started" }),
                new CreateIndexModel<Extraction>(
                    keys.Ascending(e => e.CaseId),
                    new CreateIndexOptions<Extraction>
                    {
                        Name = "ix_extractions_case",
                        PartialFilterExpression = Builders<Extraction>.Filter.Exists(e => e.CaseId),
                    }),
            ], CancellationToken.None);
        }
        return _indexTask;
    }

    public Task<Extraction?> FindByIdAsync(string id, CancellationToken ct = default) =>
        _col.Find(e => e.Id == id).FirstOrDefaultAsync(ct)!;

    public async Task<List<Extraction>> ListByOfficerAsync(string dni, string? caseId = null,
        bool unlinkedOnly = false, CancellationToken ct = default)
    {
        var f = Builders<Extraction>.Filter;
        var filter = f.Eq("Officer.Dni", dni);
        if (caseId is not null)
            filter &= f.Eq(e => e.CaseId, caseId);
        if (unlinkedOnly)
            filter &= f.Exists(e => e.CaseId, false) & f.In(e => e.Status, ExtractionStatuses.Terminal);
        return await _col.Find(filter)
            .SortByDescending(e => e.StartedAtPc)
            .Limit(200)
            .ToListAsync(ct);
    }

    public async Task<bool> UpsertAsync(Extraction ext, CancellationToken ct = default)
    {
        _ = EnsureIndexesAsync();
        var f = Builders<Extraction>.Filter;
        var filter = f.Eq(e => e.Id, ext.Id) & f.Eq("Officer.Dni", ext.Officer.Dni);
        try
        {
            await _col.ReplaceOneAsync(filter, ext, new ReplaceOptions { IsUpsert = true }, ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Mismo _id con otro dueño: el servicio lo trata como 404.
            return false;
        }
    }

    public async Task<Extraction?> SetCaseAsync(string id, string dni, string? caseId, DateTime? linkedAt,
        CancellationToken ct = default)
    {
        var f = Builders<Extraction>.Filter;
        var filter = f.Eq(e => e.Id, id) & f.Eq("Officer.Dni", dni);
        var update = caseId is not null
            ? Builders<Extraction>.Update.Set(e => e.CaseId, caseId).Set(e => e.LinkedAt, linkedAt)
            : Builders<Extraction>.Update.Unset(e => e.CaseId).Unset(e => e.LinkedAt);
        return await _col.FindOneAndUpdateAsync(filter, update,
            new FindOneAndUpdateOptions<Extraction> { ReturnDocument = ReturnDocument.After }, ct)!;
    }
}
