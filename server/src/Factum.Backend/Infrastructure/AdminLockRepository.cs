using Factum.Backend.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Factum.Backend.Infrastructure;

/// <summary>
/// Lock con lease en <c>user_admin_locks</c> (abm-clientes §6.3): serializa la suspensión de
/// superadmins sin transacciones (el Mongo de despliegue es standalone).
/// </summary>
public interface IAdminLockRepository
{
    /// <summary>
    /// true si se adquirió. Atómico: <c>FindOneAndUpdate({ _id: key, ExpiresAt: { $lte: now } },
    /// { $set: { Owner, ExpiresAt: now + lease } }, upsert)</c>. Con un lease vigente el filtro no
    /// matchea y el upsert choca con el <c>_id</c> existente (DuplicateKey) → false.
    /// </summary>
    Task<bool> TryAcquireAsync(string key, string owner, DateTime now, TimeSpan lease, CancellationToken ct = default);
    /// <summary><c>DeleteOne({ _id: key, Owner: owner })</c>: solo libera el lock propio.</summary>
    Task ReleaseAsync(string key, string owner, CancellationToken ct = default);
}

public sealed class AdminLockRepository : IAdminLockRepository
{
    public const string CollectionName = "user_admin_locks";
    private const int DuplicateKeyCode = 11000;

    private readonly IMongoCollection<AdminLock> _col;

    public AdminLockRepository(IOptions<MongoOptions> opts)
    {
        var client = new MongoClient(opts.Value.ConnectionString);
        _col = client.GetDatabase(opts.Value.DatabaseName).GetCollection<AdminLock>(CollectionName);
    }

    public async Task<bool> TryAcquireAsync(string key, string owner, DateTime now, TimeSpan lease,
        CancellationToken ct = default)
    {
        var f = Builders<AdminLock>.Filter;
        try
        {
            var doc = await _col.FindOneAndUpdateAsync(
                f.Eq(l => l.Id, key) & f.Lte(l => l.ExpiresAt, now),
                Builders<AdminLock>.Update.Set(l => l.Owner, owner).Set(l => l.ExpiresAt, now + lease),
                new FindOneAndUpdateOptions<AdminLock> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
                ct);
            return doc is not null && doc.Owner == owner;
        }
        catch (MongoCommandException ex) when (ex.Code == DuplicateKeyCode)
        {
            return false;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public Task ReleaseAsync(string key, string owner, CancellationToken ct = default) =>
        _col.DeleteOneAsync(l => l.Id == key && l.Owner == owner, ct);
}
