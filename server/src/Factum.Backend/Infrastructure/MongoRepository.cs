using Factum.Backend.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Factum.Backend.Infrastructure;

public sealed class MongoOptions
{
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string DatabaseName { get; set; } = "factum";
}

public interface ICaseRepository
{
    Task<List<Case>> ListByOfficerAsync(string officerDni, CancellationToken ct = default);
    Task<Case?> FindByIdAsync(string id, CancellationToken ct = default);
    Task InsertAsync(Case cas, CancellationToken ct = default);
    Task UpdateStatusAsync(string id, CaseStatus status, CancellationToken ct = default);
    Task UpdateGeneratedAsync(string id, DateTime generatedAt, string zipPassword,
        string zipHash, string zipFilename, string pdfFilename, CancellationToken ct = default);
    Task AddFileSourceAsync(string id, string filename, string sourcePath, CancellationToken ct = default);
}

public sealed class CaseRepository : ICaseRepository
{
    private readonly IMongoCollection<Case> _col;
    private Task? _indexTask;

    public CaseRepository(IOptions<MongoOptions> opts)
    {
        var client = new MongoClient(opts.Value.ConnectionString);
        var db = client.GetDatabase(opts.Value.DatabaseName);
        _col = db.GetCollection<Case>("cases");
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
                new CreateIndexModel<Case>(
                    Builders<Case>.IndexKeys.Ascending(c => c.Officer.Dni)),
                cancellationToken: CancellationToken.None);
        }
        return _indexTask;
    }

    public Task<List<Case>> ListByOfficerAsync(string officerDni, CancellationToken ct = default) =>
        _col.Find(c => c.Officer.Dni == officerDni)
            .SortByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

    public async Task<Case?> FindByIdAsync(string id, CancellationToken ct = default) =>
        await _col.Find(c => c.Id == id).FirstOrDefaultAsync(ct);

    public Task InsertAsync(Case cas, CancellationToken ct = default) =>
        _col.InsertOneAsync(cas, cancellationToken: ct);

    public Task UpdateStatusAsync(string id, CaseStatus status, CancellationToken ct = default) =>
        _col.UpdateOneAsync(
            c => c.Id == id,
            Builders<Case>.Update.Set(c => c.Status, status),
            cancellationToken: ct);

    public Task UpdateGeneratedAsync(string id, DateTime generatedAt, string zipPassword,
        string zipHash, string zipFilename, string pdfFilename, CancellationToken ct = default) =>
        _col.UpdateOneAsync(
            c => c.Id == id,
            Builders<Case>.Update
                .Set(c => c.Status, CaseStatus.Completed)
                .Set(c => c.GeneratedAt, generatedAt)
                .Set(c => c.ZipPassword, zipPassword)
                .Set(c => c.ZipHash, zipHash)
                .Set(c => c.ZipFilename, zipFilename)
                .Set(c => c.PdfFilename, pdfFilename),
            cancellationToken: ct);

    public Task AddFileSourceAsync(string id, string filename, string sourcePath,
        CancellationToken ct = default) =>
        _col.UpdateOneAsync(
            c => c.Id == id,
            Builders<Case>.Update.Push(c => c.FileSources,
                new FileSource { Filename = filename, SourcePath = sourcePath }),
            cancellationToken: ct);
}
