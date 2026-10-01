using Factum.Backend.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Factum.Backend.Infrastructure;

public interface IExpertProfileRepository
{
    Task<ExpertProfile?> GetAsync(string dni, CancellationToken ct = default);
    /// <summary>Upsert del documento del propio usuario (<c>_id</c> = DNI). No toca otros.</summary>
    Task UpsertAsync(ExpertProfile profile, CancellationToken ct = default);
}

/// <summary>Colección <c>expert_profiles</c>. Se busca por <c>_id</c>: no necesita índices.</summary>
public sealed class ExpertProfileRepository : IExpertProfileRepository
{
    private readonly IMongoCollection<ExpertProfile> _col;

    public ExpertProfileRepository(IOptions<MongoOptions> opts)
    {
        var client = new MongoClient(opts.Value.ConnectionString);
        _col = client.GetDatabase(opts.Value.DatabaseName).GetCollection<ExpertProfile>("expert_profiles");
    }

    public async Task<ExpertProfile?> GetAsync(string dni, CancellationToken ct = default) =>
        await _col.Find(p => p.Id == dni).FirstOrDefaultAsync(ct);

    public Task UpsertAsync(ExpertProfile profile, CancellationToken ct = default) =>
        _col.ReplaceOneAsync(p => p.Id == profile.Id, profile,
            new ReplaceOptions { IsUpsert = true }, ct);
}
