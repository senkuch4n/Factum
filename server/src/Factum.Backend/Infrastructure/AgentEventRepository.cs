using Factum.Backend.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Factum.Backend.Infrastructure;

public interface IAgentEventRepository
{
    Task InsertAsync(AgentEvent evt, CancellationToken ct = default);
    Task<List<AgentEvent>> ListAsync(string? dni, string? caseId, DateTime? from, DateTime? to,
        CancellationToken ct = default);
}

public sealed class AgentEventRepository : IAgentEventRepository
{
    private readonly IMongoCollection<AgentEvent> _col;
    private Task? _indexTask;

    public AgentEventRepository(IOptions<MongoOptions> opts)
    {
        var client = new MongoClient(opts.Value.ConnectionString);
        var db = client.GetDatabase(opts.Value.DatabaseName);
        _col = db.GetCollection<AgentEvent>("agent_events");
        // Kick off index creation in the background at startup.
        // Uses CancellationToken.None so no HTTP request can cancel it.
        _ = EnsureIndexAsync();
    }

    private Task EnsureIndexAsync()
    {
        // Retry if previous attempt failed or was cancelled.
        if (_indexTask is null || _indexTask.IsFaulted || _indexTask.IsCanceled)
        {
            _indexTask = _col.Indexes.CreateOneAsync(
                new CreateIndexModel<AgentEvent>(
                    Builders<AgentEvent>.IndexKeys
                        .Ascending(e => e.Dni)
                        .Descending(e => e.Timestamp)),
                cancellationToken: CancellationToken.None);
        }
        return _indexTask;
    }

    public Task InsertAsync(AgentEvent evt, CancellationToken ct = default) =>
        _col.InsertOneAsync(evt, cancellationToken: ct);

    public Task<List<AgentEvent>> ListAsync(string? dni, string? caseId, DateTime? from, DateTime? to,
        CancellationToken ct = default)
    {
        var filter = Builders<AgentEvent>.Filter.Empty;
        if (!string.IsNullOrEmpty(dni))
            filter &= Builders<AgentEvent>.Filter.Eq(e => e.Dni, dni);
        if (!string.IsNullOrEmpty(caseId))
            filter &= Builders<AgentEvent>.Filter.Eq(e => e.CaseId, caseId);
        if (from is not null)
            filter &= Builders<AgentEvent>.Filter.Gte(e => e.Timestamp, from.Value);
        if (to is not null)
            filter &= Builders<AgentEvent>.Filter.Lte(e => e.Timestamp, to.Value);

        return _col.Find(filter)
            .SortByDescending(e => e.Timestamp)
            .Limit(1000)
            .ToListAsync(ct);
    }
}
