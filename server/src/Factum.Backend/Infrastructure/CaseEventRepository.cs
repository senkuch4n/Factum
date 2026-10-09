using Factum.Backend.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Factum.Backend.Infrastructure;

/// <summary>
/// Repositorio append-only de <see cref="CaseEvent"/> (trazabilidad-caso, DT1). Solo expone
/// <see cref="InsertAsync"/> y <see cref="ListByCaseAsync"/>: no hay Update/Delete (D10-A, nivel
/// aplicación). Espejo de <c>AgentEventRepository</c>: cliente Mongo propio por
/// <see cref="MongoOptions"/> e índice en background con <see cref="CancellationToken.None"/>.
/// </summary>
public interface ICaseEventRepository
{
    Task InsertAsync(CaseEvent evt, CancellationToken ct = default);
    Task<List<CaseEvent>> ListByCaseAsync(string caseId, CancellationToken ct = default);
}

public sealed class CaseEventRepository : ICaseEventRepository
{
    private readonly IMongoCollection<CaseEvent> _col;
    private Task? _indexTask;

    public CaseEventRepository(IOptions<MongoOptions> opts)
    {
        var client = new MongoClient(opts.Value.ConnectionString);
        var db = client.GetDatabase(opts.Value.DatabaseName);
        _col = db.GetCollection<CaseEvent>("case_events");
        // Crea la colección y el índice en background al arrancar (permitido por la regla de datos).
        // CancellationToken.None: ningún request HTTP lo cancela.
        _ = EnsureIndexAsync();
    }

    private Task EnsureIndexAsync()
    {
        // Reintenta si el intento anterior falló o fue cancelado.
        if (_indexTask is null || _indexTask.IsFaulted || _indexTask.IsCanceled)
        {
            _indexTask = _col.Indexes.CreateOneAsync(
                new CreateIndexModel<CaseEvent>(
                    Builders<CaseEvent>.IndexKeys
                        .Ascending(e => e.CaseId)
                        .Ascending(e => e.Timestamp),
                    new CreateIndexOptions { Name = "ix_case_events_case_at" }),
                cancellationToken: CancellationToken.None);
        }
        return _indexTask;
    }

    public Task InsertAsync(CaseEvent evt, CancellationToken ct = default) =>
        _col.InsertOneAsync(evt, cancellationToken: ct);

    public Task<List<CaseEvent>> ListByCaseAsync(string caseId, CancellationToken ct = default) =>
        _col.Find(e => e.CaseId == caseId)
            .SortBy(e => e.Timestamp)   // ascendente (D8-A)
            .ToListAsync(ct);
}
