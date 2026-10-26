using Factum.Backend.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Factum.Backend.Infrastructure;

/// <summary>Campos de un sello obtenido (sellado-tiempo §4.1, "solo en granted").</summary>
public sealed record TimestampGrant(
    DateTime GrantedAt,
    string TsaUrl,
    string Authority,
    string SignerSubject,
    DateTime GenTime,
    string SerialNumber,
    string PolicyOid,
    byte[] Tsr);

/// <summary>
/// Colección <c>case_timestamps</c> (sellado-tiempo §4.1, D-T2). Toda escritura de un intento filtra por
/// <c>{ _id, LeaseId }</c>: si otro proceso tomó un lease vencido, el resultado del primero se descarta.
/// </summary>
public interface ICaseTimestampRepository
{
    /// <summary>
    /// Alta idempotente (upsert con <c>$setOnInsert</c> por las cuatro claves del índice único). Devuelve el
    /// documento insertado, o null si ya existía (también ante una carrera con <c>DuplicateKey</c>).
    /// </summary>
    Task<CaseTimestamp?> InsertPendingIfMissingAsync(CaseTimestamp pending, CancellationToken ct = default);

    /// <summary>Busca por las cuatro claves del objeto sellado (sin el <c>.tsr</c>).</summary>
    Task<CaseTimestamp?> FindObjectAsync(string caseId, string target, string filename, string sha256,
        CancellationToken ct = default);

    /// <summary>Busca por id (sin el <c>.tsr</c>).</summary>
    Task<CaseTimestamp?> FindByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Reclama el pendiente vencido más viejo sin lease vigente (§6.4) y le pone el lease. null si no hay.
    /// </summary>
    Task<CaseTimestamp?> ClaimDueAsync(DateTime now, DateTime leaseUntil, string leaseId,
        CancellationToken ct = default);

    /// <summary>Guarda un sello obtenido. false si el lease ya no es de quien llama.</summary>
    Task<bool> CompleteAsync(string id, string leaseId, int attempts, TimestampGrant grant,
        CancellationToken ct = default);

    /// <summary>
    /// Guarda un intento fallido: <paramref name="nextAttemptAt"/> null → <c>failed</c> (terminal). false si el
    /// lease ya no es de quien llama.
    /// </summary>
    Task<bool> FailAsync(string id, string leaseId, int attempts, string kind, string error, DateTime? nextAttemptAt,
        CancellationToken ct = default);

    Task MarkInReportAsync(string id, CancellationToken ct = default);

    /// <summary>§6.3: el sello pendiente del ZIP de una generación fallida pasa a <c>failed</c> (sin evento).</summary>
    Task AbortZipAsync(string caseId, string zipFilename, string sha256, string error, CancellationToken ct = default);

    /// <summary>Todos los sellos del caso. Sin el <c>.tsr</c> salvo que se pida.</summary>
    Task<List<CaseTimestamp>> ListByCaseAsync(string caseId, bool includeTsr = false, CancellationToken ct = default);

    /// <summary>
    /// extraccion-logica-android §6.3: el sello del manifiesto de una extracción (<c>CaseId == ""</c> +
    /// <c>ExtractionId</c>). Sin el <c>.tsr</c> salvo que se pida. null si la extracción no tiene sello.
    /// </summary>
    Task<CaseTimestamp?> FindByExtractionAsync(string extractionId, bool includeTsr = false,
        CancellationToken ct = default);
}

public sealed class CaseTimestampRepository : ICaseTimestampRepository
{
    public const string CollectionName = "case_timestamps";

    private readonly IMongoCollection<CaseTimestamp> _col;
    private readonly ILogger<CaseTimestampRepository> _log;
    private Task? _indexTask;

    private static readonly ProjectionDefinition<CaseTimestamp> WithoutTsr =
        Builders<CaseTimestamp>.Projection.Exclude(t => t.Tsr);

    public CaseTimestampRepository(IOptions<MongoOptions> opts, ILogger<CaseTimestampRepository> log)
    {
        var client = new MongoClient(opts.Value.ConnectionString);
        var db = client.GetDatabase(opts.Value.DatabaseName);
        _col = db.GetCollection<CaseTimestamp>(CollectionName);
        _log = log;
        // Colección e índices en segundo plano al arrancar (permitido por la regla de datos). Ningún request
        // los cancela; si fallan (Mongo caído), se reintentan en la próxima escritura.
        _ = EnsureIndexesAsync();
    }

    private Task EnsureIndexesAsync()
    {
        if (_indexTask is null || _indexTask.IsFaulted || _indexTask.IsCanceled)
        {
            var keys = Builders<CaseTimestamp>.IndexKeys;
            _indexTask = _col.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<CaseTimestamp>(
                    keys.Ascending(t => t.CaseId).Ascending(t => t.Target).Ascending(t => t.Filename)
                        .Ascending(t => t.Sha256),
                    new CreateIndexOptions { Name = "ux_case_timestamps_object", Unique = true }),
                new CreateIndexModel<CaseTimestamp>(
                    keys.Ascending(t => t.Status).Ascending(t => t.NextAttemptAt),
                    new CreateIndexOptions<CaseTimestamp>
                    {
                        Name = "ix_case_timestamps_due",
                        PartialFilterExpression = Builders<CaseTimestamp>.Filter.Eq(t => t.Status, TimestampStatuses.Pending),
                    }),
                // extraccion-logica-android §6.3: buscar el sello por extracción. Parcial: solo los docs que lo tienen.
                new CreateIndexModel<CaseTimestamp>(
                    keys.Ascending(t => t.ExtractionId),
                    new CreateIndexOptions<CaseTimestamp>
                    {
                        Name = "ix_case_timestamps_extraction",
                        PartialFilterExpression = Builders<CaseTimestamp>.Filter.Exists(t => t.ExtractionId),
                    }),
            ], CancellationToken.None);
            _indexTask.ContinueWith(t => _log.LogWarning("No se pudieron crear los índices de {Col} ({Error}); se reintenta en la próxima escritura",
                CollectionName, t.Exception?.GetBaseException().GetType().Name), TaskContinuationOptions.OnlyOnFaulted);
        }
        return _indexTask;
    }

    private static FilterDefinition<CaseTimestamp> ObjectFilter(string caseId, string target, string filename,
        string sha256)
    {
        var f = Builders<CaseTimestamp>.Filter;
        return f.Eq(t => t.CaseId, caseId) & f.Eq(t => t.Target, target) & f.Eq(t => t.Filename, filename) &
               f.Eq(t => t.Sha256, sha256);
    }

    private static FilterDefinition<CaseTimestamp> Leased(string id, string leaseId) =>
        Builders<CaseTimestamp>.Filter.Eq(t => t.Id, id) & Builders<CaseTimestamp>.Filter.Eq(t => t.LeaseId, leaseId);

    public async Task<CaseTimestamp?> InsertPendingIfMissingAsync(CaseTimestamp pending, CancellationToken ct = default)
    {
        _ = EnsureIndexesAsync();
        var u = Builders<CaseTimestamp>.Update
            .SetOnInsert(t => t.Id, pending.Id)
            .SetOnInsert(t => t.Status, TimestampStatuses.Pending)
            .SetOnInsert(t => t.Attempts, 0)
            .SetOnInsert(t => t.CreatedAt, pending.CreatedAt)
            .SetOnInsert(t => t.NextAttemptAt, pending.NextAttemptAt);
        if (pending.LeaseId is not null)
            u = u.SetOnInsert(t => t.LeaseUntil, pending.LeaseUntil).SetOnInsert(t => t.LeaseId, pending.LeaseId);
        // Sello de extracción (§6.3): conserva el ExtractionId al insertar.
        if (pending.ExtractionId is not null)
            u = u.SetOnInsert(t => t.ExtractionId, pending.ExtractionId);
        try
        {
            var res = await _col.UpdateOneAsync(
                ObjectFilter(pending.CaseId, pending.Target, pending.Filename, pending.Sha256), u,
                new UpdateOptions { IsUpsert = true }, ct);
            return res.UpsertedId is null ? null : pending;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return null; // carrera entre pestañas: el otro lo insertó
        }
    }

    public async Task<CaseTimestamp?> FindObjectAsync(string caseId, string target, string filename, string sha256,
        CancellationToken ct = default) =>
        await _col.Find(ObjectFilter(caseId, target, filename, sha256)).Project<CaseTimestamp>(WithoutTsr)
            .FirstOrDefaultAsync(ct);

    public async Task<CaseTimestamp?> FindByIdAsync(string id, CancellationToken ct = default) =>
        await _col.Find(t => t.Id == id).Project<CaseTimestamp>(WithoutTsr).FirstOrDefaultAsync(ct);

    public async Task<CaseTimestamp?> ClaimDueAsync(DateTime now, DateTime leaseUntil, string leaseId,
        CancellationToken ct = default)
    {
        var f = Builders<CaseTimestamp>.Filter;
        var filter = f.Eq(t => t.Status, TimestampStatuses.Pending) & f.Lte(t => t.NextAttemptAt, now) &
                     (f.Exists(t => t.LeaseUntil, false) | f.Lt(t => t.LeaseUntil, now));
        return await _col.FindOneAndUpdateAsync(filter,
            Builders<CaseTimestamp>.Update.Set(t => t.LeaseUntil, leaseUntil).Set(t => t.LeaseId, leaseId),
            new FindOneAndUpdateOptions<CaseTimestamp>
            {
                Sort = Builders<CaseTimestamp>.Sort.Ascending(t => t.NextAttemptAt),
                ReturnDocument = ReturnDocument.After,
                Projection = WithoutTsr,
            }, ct);
    }

    public async Task<bool> CompleteAsync(string id, string leaseId, int attempts, TimestampGrant grant,
        CancellationToken ct = default)
    {
        var res = await _col.UpdateOneAsync(Leased(id, leaseId),
            Builders<CaseTimestamp>.Update
                .Set(t => t.Status, TimestampStatuses.Granted)
                .Set(t => t.Attempts, attempts)
                .Set(t => t.GrantedAt, grant.GrantedAt)
                .Set(t => t.TsaUrl, grant.TsaUrl)
                .Set(t => t.Authority, grant.Authority)
                .Set(t => t.SignerSubject, grant.SignerSubject)
                .Set(t => t.GenTime, grant.GenTime)
                .Set(t => t.SerialNumber, grant.SerialNumber)
                .Set(t => t.PolicyOid, grant.PolicyOid)
                .Set(t => t.Tsr, grant.Tsr)
                .Unset(t => t.NextAttemptAt)
                .Unset(t => t.LeaseUntil)
                .Unset(t => t.LeaseId)
                .Unset(t => t.LastError)
                .Unset(t => t.LastErrorKind),
            cancellationToken: ct);
        return res.MatchedCount > 0;
    }

    public async Task<bool> FailAsync(string id, string leaseId, int attempts, string kind, string error,
        DateTime? nextAttemptAt, CancellationToken ct = default)
    {
        var u = Builders<CaseTimestamp>.Update
            .Set(t => t.Attempts, attempts)
            .Set(t => t.LastError, error)
            .Set(t => t.LastErrorKind, kind)
            .Unset(t => t.LeaseUntil)
            .Unset(t => t.LeaseId);
        u = nextAttemptAt is { } next
            ? u.Set(t => t.NextAttemptAt, next)
            : u.Set(t => t.Status, TimestampStatuses.Failed).Unset(t => t.NextAttemptAt);
        var res = await _col.UpdateOneAsync(Leased(id, leaseId), u, cancellationToken: ct);
        return res.MatchedCount > 0;
    }

    public Task MarkInReportAsync(string id, CancellationToken ct = default) =>
        _col.UpdateOneAsync(t => t.Id == id, Builders<CaseTimestamp>.Update.Set(t => t.InReport, true),
            cancellationToken: ct);

    public Task AbortZipAsync(string caseId, string zipFilename, string sha256, string error,
        CancellationToken ct = default) =>
        _col.UpdateOneAsync(
            ObjectFilter(caseId, TimestampTargets.Zip, zipFilename, sha256) &
            Builders<CaseTimestamp>.Filter.Eq(t => t.Status, TimestampStatuses.Pending),
            Builders<CaseTimestamp>.Update
                .Set(t => t.Status, TimestampStatuses.Failed)
                .Set(t => t.LastError, error)
                .Unset(t => t.NextAttemptAt)
                .Unset(t => t.LeaseUntil)
                .Unset(t => t.LeaseId),
            cancellationToken: ct);

    public Task<List<CaseTimestamp>> ListByCaseAsync(string caseId, bool includeTsr = false,
        CancellationToken ct = default)
    {
        var find = _col.Find(t => t.CaseId == caseId).SortBy(t => t.CreatedAt);
        return includeTsr ? find.ToListAsync(ct) : find.Project<CaseTimestamp>(WithoutTsr).ToListAsync(ct);
    }

    public Task<CaseTimestamp?> FindByExtractionAsync(string extractionId, bool includeTsr = false,
        CancellationToken ct = default)
    {
        var find = _col.Find(t => t.ExtractionId == extractionId);
        return includeTsr
            ? find.FirstOrDefaultAsync(ct)!
            : find.Project<CaseTimestamp>(WithoutTsr).FirstOrDefaultAsync(ct)!;
    }
}
