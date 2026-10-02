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
    Task UpdateGeneratedAsync(string id, DateTime generatedAt, string? zipPassword,
        bool zipEncrypted, string? zipEncryption,
        string zipHash, string zipFilename, string pdfFilename, string reportHash,
        CancellationToken ct = default);
    Task AddFileSourceAsync(string id, string filename, string sourcePath, CancellationToken ct = default);
    /// <summary>$set de los datos de la causa + Perito + SchemaVersion = 1 (+ Device.Imei si viene).
    /// Solo si el caso sigue editable; devuelve false si no matcheó.</summary>
    Task<bool> UpdateCaseDataAsync(string id, CaseDataUpdate data, CancellationToken ct = default);
    Task<bool> UpdateReportTextsAsync(string id, ReportTexts texts, CancellationToken ct = default);
    /// <summary>Upsert por filename (role null = borrar la marca); devuelve la lista completa o null si no matcheó.</summary>
    Task<List<CaptureRole>?> UpsertCaptureRolesAsync(string id,
        IReadOnlyList<(string Filename, string? Role)> roles, CancellationToken ct = default);
    /// <summary>
    /// SOLO LECTURA (siembra de catálogos): casos del perito, de cualquier estado o versión,
    /// proyectados a los campos que alimentan catálogos + CreatedAt.
    /// </summary>
    Task<List<Case>> ListCatalogSourcesAsync(string officerDni, CancellationToken ct = default);
}

/// <summary>Valores ya normalizados (trim) de un PUT /api/cases/{id}.</summary>
public sealed record CaseDataUpdate(
    string NroReferencia, string NombreDenunciante, string DniDenunciante,
    string NombreTribunal, string OrganismoTribunal, string SalaTribunal, string IntegrantesTribunal,
    string TipoCausa, string Caratula, string ParteDenunciante, string ParteDenunciada,
    string ObjetoCausa, string AmbitoCausa, string FechaIntervencion,
    string NombreProponente, string ProfesionProponente, string MatriculaProponente,
    string TipoDispositivo, string LineaDispositivo,
    PeritoSnapshot Perito, string? Imei, string? Observaciones,
    // null = el request no trajo lista → $unset Integrantes (gana la última escritura, D2).
    IReadOnlyList<string>? Integrantes = null);

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

    // El listado no trae ReportTexts (hasta 8 × 20 000 caracteres por caso): viaja
    // report_texts: null y el paso Informe lo lee de GET /api/cases/{id}. Tampoco trae
    // ZipPassword (defensa en profundidad: además tiene [JsonIgnore]).
    public Task<List<Case>> ListByOfficerAsync(string officerDni, CancellationToken ct = default) =>
        _col.Find(c => c.Officer.Dni == officerDni)
            .SortByDescending(c => c.CreatedAt)
            .Project<Case>(Builders<Case>.Projection
                .Exclude(c => c.ReportTexts)
                .Exclude(c => c.ZipPassword))
            .ToListAsync(ct);

    // Solo lectura, con proyección: la siembra de catálogos no necesita (ni debe traer) el resto.
    public Task<List<Case>> ListCatalogSourcesAsync(string officerDni, CancellationToken ct = default) =>
        _col.Find(c => c.Officer.Dni == officerDni)
            .SortBy(c => c.CreatedAt)
            .Project<Case>(Builders<Case>.Projection
                .Include(c => c.NombreTribunal)
                .Include(c => c.ParteDenunciante)
                .Include(c => c.ParteDenunciada)
                .Include(c => c.NombreProponente)
                .Include(c => c.ProfesionProponente)
                .Include(c => c.TipoDispositivo)
                .Include(c => c.CreatedAt))
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

    public Task UpdateGeneratedAsync(string id, DateTime generatedAt, string? zipPassword,
        bool zipEncrypted, string? zipEncryption,
        string zipHash, string zipFilename, string pdfFilename, string reportHash,
        CancellationToken ct = default) =>
        _col.UpdateOneAsync(
            c => c.Id == id,
            Builders<Case>.Update
                .Set(c => c.Status, CaseStatus.Completed)
                .Set(c => c.GeneratedAt, generatedAt)
                .Set(c => c.ZipPassword, zipPassword)
                .Set(c => c.ZipEncrypted, zipEncrypted)
                .Set(c => c.ZipEncryption, zipEncryption)
                .Set(c => c.ZipHash, zipHash)
                .Set(c => c.ZipFilename, zipFilename)
                .Set(c => c.PdfFilename, pdfFilename)
                .Set(c => c.ReportHash, reportHash),
            cancellationToken: ct);

    public Task AddFileSourceAsync(string id, string filename, string sourcePath,
        CancellationToken ct = default) =>
        _col.UpdateOneAsync(
            c => c.Id == id,
            Builders<Case>.Update.Push(c => c.FileSources,
                new FileSource { Filename = filename, SourcePath = sourcePath }),
            cancellationToken: ct);

    // Filtro de "caso editable" en la propia escritura: si otro request lo pasó a Generating o
    // Completed entre la lectura y el update, no se toca (el servicio responde 409).
    private static FilterDefinition<Case> Editable(string id) =>
        Builders<Case>.Filter.Eq(c => c.Id, id) &
        Builders<Case>.Filter.Nin(c => c.Status, [CaseStatus.Generating, CaseStatus.Completed]);

    // Siempre $set campo por campo (nunca Replace): un PUT no reescribe lo que no maneja.
    public async Task<bool> UpdateCaseDataAsync(string id, CaseDataUpdate d, CancellationToken ct = default)
    {
        var u = Builders<Case>.Update
            .Set(c => c.SchemaVersion, 1)
            .Set(c => c.Perito, d.Perito)
            .Set(c => c.NroReferencia, d.NroReferencia)
            .Set(c => c.NombreDenunciante, d.NombreDenunciante)
            .Set(c => c.DniDenunciante, d.DniDenunciante)
            .Set(c => c.NombreTribunal, d.NombreTribunal)
            .Set(c => c.OrganismoTribunal, d.OrganismoTribunal)
            .Set(c => c.SalaTribunal, d.SalaTribunal)
            .Set(c => c.IntegrantesTribunal, d.IntegrantesTribunal)
            .Set(c => c.TipoCausa, d.TipoCausa)
            .Set(c => c.Caratula, d.Caratula)
            .Set(c => c.ParteDenunciante, d.ParteDenunciante)
            .Set(c => c.ParteDenunciada, d.ParteDenunciada)
            .Set(c => c.ObjetoCausa, d.ObjetoCausa)
            .Set(c => c.AmbitoCausa, d.AmbitoCausa)
            .Set(c => c.FechaIntervencion, d.FechaIntervencion)
            .Set(c => c.NombreProponente, d.NombreProponente)
            .Set(c => c.ProfesionProponente, d.ProfesionProponente)
            .Set(c => c.MatriculaProponente, d.MatriculaProponente)
            .Set(c => c.TipoDispositivo, d.TipoDispositivo)
            .Set(c => c.LineaDispositivo, d.LineaDispositivo);
        if (!string.IsNullOrEmpty(d.Imei))
            u = u.Set(c => c.Device.Imei, d.Imei);
        if (d.Observaciones is not null)
            u = u.Set(c => c.Observaciones, d.Observaciones);
        // IntegrantesTribunal ya viene derivado de la lista cuando la hay (CaseFields.From).
        u = d.Integrantes is not null
            ? u.Set(c => c.Integrantes, d.Integrantes.ToList())
            : u.Unset(c => c.Integrantes);

        var res = await _col.UpdateOneAsync(Editable(id), u, cancellationToken: ct);
        return res.MatchedCount > 0;
    }

    public async Task<bool> UpdateReportTextsAsync(string id, ReportTexts texts, CancellationToken ct = default)
    {
        var res = await _col.UpdateOneAsync(Editable(id),
            Builders<Case>.Update.Set(c => c.ReportTexts, texts), cancellationToken: ct);
        return res.MatchedCount > 0;
    }

    public async Task<List<CaptureRole>?> UpsertCaptureRolesAsync(string id,
        IReadOnlyList<(string Filename, string? Role)> roles, CancellationToken ct = default)
    {
        var cas = await FindByIdAsync(id, ct);
        if (cas is null) return null;

        var merged = cas.CaptureRoles
            .GroupBy(r => r.Filename, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Role, StringComparer.Ordinal);
        foreach (var (filename, role) in roles)
        {
            if (role is null) merged.Remove(filename);
            else merged[filename] = role;
        }
        var list = merged
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new CaptureRole { Filename = kv.Key, Role = kv.Value })
            .ToList();

        var res = await _col.UpdateOneAsync(Editable(id),
            Builders<Case>.Update.Set(c => c.CaptureRoles, list), cancellationToken: ct);
        return res.MatchedCount > 0 ? list : null;
    }
}
