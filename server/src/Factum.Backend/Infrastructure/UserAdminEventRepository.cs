using Factum.Backend.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Factum.Backend.Infrastructure;

/// <summary>
/// Auditoría del panel de cuentas (colección <c>user_admin_events</c>, abm-clientes §4.2/§6.6).
/// De SOLO INSERCIÓN: a propósito no hay métodos de update ni de delete.
/// </summary>
public interface IUserAdminEventRepository
{
    /// <summary>Índice <c>ix_user_admin_events_target_at</c>. Solo con <c>Auth:Mode=local</c>.</summary>
    Task EnsureIndexesAsync(CancellationToken ct = default);
    Task InsertAsync(UserAdminEvent evt, CancellationToken ct = default);
    /// <summary>Por <c>TargetUserId</c>, <c>At</c> desc / <c>_id</c> desc, <c>Skip(offset).Limit(limit + 1)</c>.</summary>
    Task<List<UserAdminEvent>> ListByTargetAsync(string targetUserId, int offset, int limit, CancellationToken ct = default);
}

public sealed class UserAdminEventRepository : IUserAdminEventRepository
{
    public const string CollectionName = "user_admin_events";
    public const string TargetAtIndexName = "ix_user_admin_events_target_at";

    private readonly IMongoCollection<UserAdminEvent> _col;

    public UserAdminEventRepository(IOptions<MongoOptions> opts)
    {
        // MongoClient no conecta hasta el primer uso: en dev/external no se crea la colección.
        var client = new MongoClient(opts.Value.ConnectionString);
        _col = client.GetDatabase(opts.Value.DatabaseName).GetCollection<UserAdminEvent>(CollectionName);
    }

    public Task EnsureIndexesAsync(CancellationToken ct = default) =>
        _col.Indexes.CreateOneAsync(new CreateIndexModel<UserAdminEvent>(
            Builders<UserAdminEvent>.IndexKeys.Ascending(e => e.TargetUserId).Descending(e => e.At),
            new CreateIndexOptions { Name = TargetAtIndexName }), cancellationToken: ct);

    public Task InsertAsync(UserAdminEvent evt, CancellationToken ct = default) =>
        _col.InsertOneAsync(evt, cancellationToken: ct);

    public Task<List<UserAdminEvent>> ListByTargetAsync(string targetUserId, int offset, int limit,
        CancellationToken ct = default) =>
        _col.Find(e => e.TargetUserId == targetUserId)
            .SortByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Skip(offset)
            .Limit(limit + 1)
            .ToListAsync(ct);
}
