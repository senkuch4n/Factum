using Factum.Backend.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Factum.Backend.Infrastructure;

/// <summary>
/// Cuentas propias de Factum (colección <c>users</c>, usuarios-locales §6.6). Todas las escrituras
/// filtran por <c>_id</c> y ninguna toca otra colección. Algunos métodos solo los usa #12 (ABM).
/// </summary>
public interface IUserRepository
{
    Task EnsureIndexesAsync(CancellationToken ct = default);
    /// <summary>Documento completo, con hash (login, cambio de contraseña, bootstrap).</summary>
    Task<UserAccount?> FindByDniAsync(string dni, CancellationToken ct = default);
    /// <summary>Sin <c>PasswordHash</c> ni <c>LastEmergencyResetHash</c> (validación por request).</summary>
    Task<UserAccount?> FindSessionByDniAsync(string dni, CancellationToken ct = default);
    Task<UserAccount?> FindByIdAsync(string id, CancellationToken ct = default);
    /// <summary>#12: sin hashes, ordenado por <c>Name</c>.</summary>
    Task<List<UserAccount>> ListAsync(string? role = null, string? status = null, CancellationToken ct = default);
    Task<long> CountActiveSuperadminsAsync(CancellationToken ct = default);
    /// <summary><c>false</c> si ya hay una cuenta con ese DNI (DuplicateKey).</summary>
    Task<bool> TryInsertAsync(UserAccount user, CancellationToken ct = default);
    /// <summary><c>$inc FailedLoginCount</c>; devuelve el valor nuevo (0 si no existe).</summary>
    Task<int> IncrementFailedLoginAsync(string id, DateTime now, CancellationToken ct = default);
    /// <summary><c>LockedUntil = until</c> y <c>FailedLoginCount = 0</c>.</summary>
    Task LockAsync(string id, DateTime until, DateTime now, CancellationToken ct = default);
    /// <summary><c>FailedLoginCount = 0</c>, <c>LockedUntil = null</c>, <c>LastLoginAt = now</c>.</summary>
    Task RegisterSuccessfulLoginAsync(string id, DateTime now, CancellationToken ct = default);
    /// <summary>Rehash: no toca <c>PasswordChangedAt</c> (no corta sesiones).</summary>
    Task UpdatePasswordHashAsync(string id, string hash, DateTime now, CancellationToken ct = default);
    /// <summary>
    /// Hash nuevo + <c>MustChangePassword</c> + <c>PasswordChangedAt</c> + desbloqueo. Cambio propio
    /// (<paramref name="mustChange"/> = false) y reset de #12 (true).
    /// </summary>
    Task<bool> SetPasswordAsync(string id, string hash, bool mustChange, DateTime changedAt, CancellationToken ct = default);
    /// <summary>#12. Suspender pone <c>SuspendedAt</c>/<c>SuspendedBy</c>; reactivar les hace <c>$unset</c>.</summary>
    Task<bool> SetStatusAsync(string id, string status, string? byDni, DateTime now, CancellationToken ct = default);
    Task<bool> UpdateProfileAsync(string id, string name, string sigla, DateTime now, CancellationToken ct = default);
    Task<bool> UnlockAsync(string id, DateTime now, CancellationToken ct = default);
    /// <summary>Reset de emergencia (§7.2): hash + marca, debe cambiar, corta sesiones, desbloquea y reactiva.</summary>
    Task<bool> ApplyEmergencyResetAsync(string id, string hash, string markerHash, DateTime changedAt, CancellationToken ct = default);
}

public sealed class UserRepository : IUserRepository
{
    public const string CollectionName = "users";

    private static readonly ProjectionDefinition<UserAccount> WithoutHashes =
        Builders<UserAccount>.Projection.Exclude(u => u.PasswordHash).Exclude(u => u.LastEmergencyResetHash);

    private readonly IMongoCollection<UserAccount> _col;

    public UserRepository(IOptions<MongoOptions> opts)
    {
        // MongoClient no conecta hasta el primer uso: en dev/external el repositorio se registra
        // pero no se usa, y la colección no se crea (usuarios-locales §4.1, T10).
        var client = new MongoClient(opts.Value.ConnectionString);
        _col = client.GetDatabase(opts.Value.DatabaseName).GetCollection<UserAccount>(CollectionName);
    }

    private static FilterDefinition<UserAccount> ById(string id) => Builders<UserAccount>.Filter.Eq(u => u.Id, id);
    private static UpdateDefinitionBuilder<UserAccount> Set => Builders<UserAccount>.Update;

    public async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        var keys = Builders<UserAccount>.IndexKeys;
        await _col.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<UserAccount>(keys.Ascending(u => u.Dni),
                new CreateIndexOptions { Name = "ux_users_dni", Unique = true }),
            new CreateIndexModel<UserAccount>(keys.Ascending(u => u.Role).Ascending(u => u.Status),
                new CreateIndexOptions { Name = "ix_users_role_status" }),
        ], ct);
    }

    public async Task<UserAccount?> FindByDniAsync(string dni, CancellationToken ct = default) =>
        await _col.Find(u => u.Dni == dni).FirstOrDefaultAsync(ct);

    public async Task<UserAccount?> FindSessionByDniAsync(string dni, CancellationToken ct = default) =>
        await _col.Find(u => u.Dni == dni).Project<UserAccount>(WithoutHashes).FirstOrDefaultAsync(ct);

    public async Task<UserAccount?> FindByIdAsync(string id, CancellationToken ct = default) =>
        await _col.Find(ById(id)).FirstOrDefaultAsync(ct);

    public Task<List<UserAccount>> ListAsync(string? role = null, string? status = null, CancellationToken ct = default)
    {
        var f = Builders<UserAccount>.Filter;
        var filter = f.Empty;
        if (!string.IsNullOrEmpty(role)) filter &= f.Eq(u => u.Role, role);
        if (!string.IsNullOrEmpty(status)) filter &= f.Eq(u => u.Status, status);
        return _col.Find(filter).Project<UserAccount>(WithoutHashes).SortBy(u => u.Name).ToListAsync(ct);
    }

    public Task<long> CountActiveSuperadminsAsync(CancellationToken ct = default) =>
        _col.CountDocumentsAsync(u => u.Role == UserRoles.Superadmin && u.Status == UserStatuses.Activo,
            cancellationToken: ct);

    public async Task<bool> TryInsertAsync(UserAccount user, CancellationToken ct = default)
    {
        try
        {
            await _col.InsertOneAsync(user, cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<int> IncrementFailedLoginAsync(string id, DateTime now, CancellationToken ct = default)
    {
        var updated = await _col.FindOneAndUpdateAsync(ById(id),
            Set.Inc(u => u.FailedLoginCount, 1).Set(u => u.UpdatedAt, now),
            new FindOneAndUpdateOptions<UserAccount>
            {
                ReturnDocument = ReturnDocument.After,
                Projection = Builders<UserAccount>.Projection.Include(u => u.FailedLoginCount),
            }, ct);
        return updated?.FailedLoginCount ?? 0;
    }

    public Task LockAsync(string id, DateTime until, DateTime now, CancellationToken ct = default) =>
        _col.UpdateOneAsync(ById(id),
            Set.Set(u => u.LockedUntil, until).Set(u => u.FailedLoginCount, 0).Set(u => u.UpdatedAt, now),
            cancellationToken: ct);

    public Task RegisterSuccessfulLoginAsync(string id, DateTime now, CancellationToken ct = default) =>
        _col.UpdateOneAsync(ById(id),
            Set.Set(u => u.FailedLoginCount, 0).Set(u => u.LockedUntil, null).Set(u => u.LastLoginAt, now),
            cancellationToken: ct);

    public Task UpdatePasswordHashAsync(string id, string hash, DateTime now, CancellationToken ct = default) =>
        _col.UpdateOneAsync(ById(id),
            Set.Set(u => u.PasswordHash, hash).Set(u => u.UpdatedAt, now),
            cancellationToken: ct);

    public async Task<bool> SetPasswordAsync(string id, string hash, bool mustChange, DateTime changedAt,
        CancellationToken ct = default)
    {
        var r = await _col.UpdateOneAsync(ById(id),
            Set.Set(u => u.PasswordHash, hash)
                .Set(u => u.MustChangePassword, mustChange)
                .Set(u => u.PasswordChangedAt, changedAt)
                .Set(u => u.FailedLoginCount, 0)
                .Set(u => u.LockedUntil, null)
                .Set(u => u.UpdatedAt, changedAt),
            cancellationToken: ct);
        return r.MatchedCount > 0;
    }

    public async Task<bool> SetStatusAsync(string id, string status, string? byDni, DateTime now,
        CancellationToken ct = default)
    {
        var update = status == UserStatuses.Suspendido
            ? Set.Set(u => u.Status, status).Set(u => u.SuspendedAt, now).Set(u => u.SuspendedBy, byDni)
            : Set.Set(u => u.Status, status).Unset(u => u.SuspendedAt).Unset(u => u.SuspendedBy);
        var r = await _col.UpdateOneAsync(ById(id), update.Set(u => u.UpdatedAt, now), cancellationToken: ct);
        return r.MatchedCount > 0;
    }

    public async Task<bool> UpdateProfileAsync(string id, string name, string sigla, DateTime now,
        CancellationToken ct = default)
    {
        var r = await _col.UpdateOneAsync(ById(id),
            Set.Set(u => u.Name, name).Set(u => u.Sigla, sigla).Set(u => u.UpdatedAt, now),
            cancellationToken: ct);
        return r.MatchedCount > 0;
    }

    public async Task<bool> UnlockAsync(string id, DateTime now, CancellationToken ct = default)
    {
        var r = await _col.UpdateOneAsync(ById(id),
            Set.Set(u => u.FailedLoginCount, 0).Set(u => u.LockedUntil, null).Set(u => u.UpdatedAt, now),
            cancellationToken: ct);
        return r.MatchedCount > 0;
    }

    public async Task<bool> ApplyEmergencyResetAsync(string id, string hash, string markerHash, DateTime changedAt,
        CancellationToken ct = default)
    {
        var r = await _col.UpdateOneAsync(ById(id),
            Set.Set(u => u.PasswordHash, hash)
                .Set(u => u.LastEmergencyResetHash, markerHash)
                .Set(u => u.MustChangePassword, true)
                .Set(u => u.PasswordChangedAt, changedAt)
                .Set(u => u.FailedLoginCount, 0)
                .Set(u => u.LockedUntil, null)
                .Set(u => u.Status, UserStatuses.Activo)
                .Unset(u => u.SuspendedAt)
                .Unset(u => u.SuspendedBy)
                .Set(u => u.UpdatedAt, changedAt),
            cancellationToken: ct);
        return r.MatchedCount > 0;
    }
}
