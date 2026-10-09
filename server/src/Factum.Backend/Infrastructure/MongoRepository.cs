using Factum.Backend.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
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
    /// <summary>
    /// $set de <c>report_texts</c> y, de forma ATÓMICA en el mismo UpdateOne, el append al historial
    /// de versiones (versionado-informe, D1). <paramref name="versionToPush"/> no null y
    /// <paramref name="prunedOverride"/> null → $push simple. <paramref name="prunedOverride"/> no
    /// null → $set del array completo ya podado (D3, conserva la primera y las "generate").
    /// Ambos no null es un error de uso (gana el override). Solo si el caso sigue editable.
    /// </summary>
    Task<bool> UpdateReportTextsAsync(string id, ReportTexts texts, ReportTextVersion? versionToPush = null,
        List<ReportTextVersion>? prunedOverride = null, CancellationToken ct = default);
    /// <summary>
    /// Append atómico de una versión al historial SIN tocar <c>report_texts</c> (versionado-informe,
    /// versión "generate"). $push simple o $set del array podado, bajo el mismo filtro de
    /// editabilidad. false si el caso no matcheó (dejó de ser editable).
    /// </summary>
    Task<bool> AppendReportVersionAsync(string id, ReportTextVersion version,
        List<ReportTextVersion>? prunedOverride, CancellationToken ct = default);
    /// <summary>Upsert por filename (role null = borrar la marca); devuelve la lista completa o null si no matcheó.</summary>
    Task<List<CaptureRole>?> UpsertCaptureRolesAsync(string id,
        IReadOnlyList<(string Filename, string? Role)> roles, CancellationToken ct = default);
    /// <summary>
    /// SOLO LECTURA (siembra de catálogos): casos del perito, de cualquier estado o versión,
    /// proyectados a los campos que alimentan catálogos + CreatedAt.
    /// </summary>
    Task<List<Case>> ListCatalogSourcesAsync(string officerDni, CancellationToken ct = default);

    /// <summary>
    /// SOLO LECTURA (abm-clientes §4.5, D9). Cantidad de casos por <c>Officer.Dni</c> para los DNIs
    /// dados; los que no tienen casos no vienen. Una sola agregación $match/$project/$group.
    /// </summary>
    Task<Dictionary<string, long>> CountByOfficerDnisAsync(IReadOnlyCollection<string> dnis, CancellationToken ct = default);

    // ── zip-local-informe-servidor (§5.2-§5.6) ───────────────────────────────

    /// <summary>
    /// $set de <c>Evidence</c>, <c>EvidenceHost</c>, <c>EvidenceStorage = "agent"</c> y
    /// <c>FileSources</c>, solo si el caso sigue editable y no es del flujo "server". false si no matcheó.
    /// </summary>
    Task<bool> RegisterEvidenceAsync(string id, List<EvidenceItem> evidence, EvidenceHost host,
        List<FileSource> fileSources, CancellationToken ct = default);

    /// <summary>
    /// $pull del ítem de <c>Evidence</c>, de su <c>CaptureRoles</c> y de su <c>FileSources</c>; si
    /// el manifiesto queda vacío, $unset de <c>EvidenceHost</c>. Mismo filtro que el registro.
    /// </summary>
    Task<bool> RemoveEvidenceAsync(string id, string filename, CancellationToken ct = default);

    /// <summary>Abre (o pisa) el intento de generación, solo si el caso sigue editable.</summary>
    Task<bool> SetPendingGenerationAsync(string id, PendingGeneration pending, CancellationToken ct = default);

    /// <summary>
    /// <c>Status = Generating</c> solo si el caso sigue editable y el intento abierto es
    /// <paramref name="generationId"/>. false si no matcheó (otro intento o ya no editable).
    /// </summary>
    Task<bool> TryMarkGeneratingAsync(string id, string generationId, CancellationToken ct = default);

    /// <summary>Cierra una generación del flujo agent: <c>Completed</c> + metadatos del ZIP y del DOCX, $unset del intento.</summary>
    Task CompleteAgentGenerationAsync(string id, DateTime generatedAt, string? zipPassword,
        string zipHash, string zipFilename, string pdfFilename, string reportHash, ZipLocation zipLocation,
        CancellationToken ct = default);

    /// <summary><c>Status = Error</c> y $unset del intento (hace falta un <c>prepare</c> nuevo).</summary>
    Task MarkAgentGenerationFailedAsync(string id, CancellationToken ct = default);
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
                .Exclude(c => c.ReportTextVersions)
                .Exclude(c => c.ZipPassword)
                .Exclude(c => c.PendingGeneration))
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

    // SOLO LECTURA: $match por el índice de Officer.Dni, $project solo de ese campo (sin _id, para que
    // el índice cubra la consulta) y $group con $sum: 1. Sin $out/$merge ni ninguna etapa que escriba.
    public async Task<Dictionary<string, long>> CountByOfficerDnisAsync(IReadOnlyCollection<string> dnis,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        if (dnis.Count == 0) return result;

        var docs = await _col.Aggregate()
            .Match(Builders<Case>.Filter.In(c => c.Officer.Dni, dnis))
            .Project(new BsonDocument { { "_id", 0 }, { "Officer.Dni", 1 } })
            .Group(new BsonDocument { { "_id", "$Officer.Dni" }, { "n", new BsonDocument("$sum", 1) } })
            .ToListAsync(ct);
        foreach (var d in docs)
        {
            if (d["_id"].IsString) result[d["_id"].AsString] = d["n"].ToInt64();
        }
        return result;
    }

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

    public async Task<bool> UpdateReportTextsAsync(string id, ReportTexts texts,
        ReportTextVersion? versionToPush = null, List<ReportTextVersion>? prunedOverride = null,
        CancellationToken ct = default)
    {
        // $set report_texts + (opcional) el append al historial viajan en un solo UpdateOne: nunca
        // queda a medias (D1). El filtro Editable(id) mantiene el 409 de siempre si el caso dejó de
        // ser editable entremedio.
        var u = Builders<Case>.Update.Set(c => c.ReportTexts, texts);
        if (prunedOverride is not null)
            u = u.Set(c => c.ReportTextVersions, prunedOverride);          // array ya podado (D3)
        else if (versionToPush is not null)
            u = u.Push(c => c.ReportTextVersions, versionToPush);          // push simple
        var res = await _col.UpdateOneAsync(Editable(id), u, cancellationToken: ct);
        return res.MatchedCount > 0;
    }

    public async Task<bool> AppendReportVersionAsync(string id, ReportTextVersion version,
        List<ReportTextVersion>? prunedOverride, CancellationToken ct = default)
    {
        var u = prunedOverride is not null
            ? Builders<Case>.Update.Set(c => c.ReportTextVersions, prunedOverride)
            : Builders<Case>.Update.Push(c => c.ReportTextVersions, version);
        var res = await _col.UpdateOneAsync(Editable(id), u, cancellationToken: ct);
        return res.MatchedCount > 0;
    }

    // ── zip-local-informe-servidor ───────────────────────────────────────────

    // $ne también matchea los documentos sin el campo (borradores viejos sin evidencia en el servidor).
    private static FilterDefinition<Case> EditableAgent(string id) =>
        Editable(id) & Builders<Case>.Filter.Ne(c => c.EvidenceStorage, EvidenceStorages.Server);

    public async Task<bool> RegisterEvidenceAsync(string id, List<EvidenceItem> evidence, EvidenceHost host,
        List<FileSource> fileSources, CancellationToken ct = default)
    {
        var res = await _col.UpdateOneAsync(EditableAgent(id),
            Builders<Case>.Update
                .Set(c => c.Evidence, evidence)
                .Set(c => c.EvidenceHost, host)
                .Set(c => c.EvidenceStorage, EvidenceStorages.Agent)
                .Set(c => c.FileSources, fileSources),
            cancellationToken: ct);
        return res.MatchedCount > 0;
    }

    public async Task<bool> RemoveEvidenceAsync(string id, string filename, CancellationToken ct = default)
    {
        var res = await _col.UpdateOneAsync(EditableAgent(id),
            Builders<Case>.Update
                .PullFilter(c => c.Evidence, e => e.Filename == filename)
                .PullFilter(c => c.CaptureRoles, r => r.Filename == filename)
                .PullFilter(c => c.FileSources, s => s.Filename == filename),
            cancellationToken: ct);
        if (res.MatchedCount == 0) return false;

        // Segundo paso atómico por sí mismo: solo si el manifiesto quedó vacío.
        await _col.UpdateOneAsync(
            EditableAgent(id) & Builders<Case>.Filter.Size(c => c.Evidence, 0),
            Builders<Case>.Update.Unset(c => c.EvidenceHost),
            cancellationToken: ct);
        return true;
    }

    public async Task<bool> SetPendingGenerationAsync(string id, PendingGeneration pending,
        CancellationToken ct = default)
    {
        var res = await _col.UpdateOneAsync(Editable(id),
            Builders<Case>.Update.Set(c => c.PendingGeneration, pending), cancellationToken: ct);
        return res.MatchedCount > 0;
    }

    public async Task<bool> TryMarkGeneratingAsync(string id, string generationId, CancellationToken ct = default)
    {
        var res = await _col.UpdateOneAsync(
            Editable(id) & Builders<Case>.Filter.Eq(c => c.PendingGeneration!.Id, generationId),
            Builders<Case>.Update.Set(c => c.Status, CaseStatus.Generating), cancellationToken: ct);
        return res.MatchedCount > 0;
    }

    public Task CompleteAgentGenerationAsync(string id, DateTime generatedAt, string? zipPassword,
        string zipHash, string zipFilename, string pdfFilename, string reportHash, ZipLocation zipLocation,
        CancellationToken ct = default) =>
        _col.UpdateOneAsync(
            Builders<Case>.Filter.Eq(c => c.Id, id) & Builders<Case>.Filter.Eq(c => c.Status, CaseStatus.Generating),
            Builders<Case>.Update
                .Set(c => c.Status, CaseStatus.Completed)
                .Set(c => c.GeneratedAt, generatedAt)
                .Set(c => c.ZipPassword, zipPassword)
                .Set(c => c.ZipEncrypted, zipPassword is not null)
                .Set(c => c.ZipEncryption, zipPassword is not null ? "aes256-ae2" : null)
                .Set(c => c.ZipHash, zipHash)
                .Set(c => c.ZipFilename, zipFilename)
                .Set(c => c.PdfFilename, pdfFilename)
                .Set(c => c.ReportHash, reportHash)
                .Set(c => c.ZipLocation, zipLocation)
                .Unset(c => c.PendingGeneration),
            cancellationToken: ct);

    public Task MarkAgentGenerationFailedAsync(string id, CancellationToken ct = default) =>
        _col.UpdateOneAsync(
            c => c.Id == id,
            Builders<Case>.Update
                .Set(c => c.Status, CaseStatus.Error)
                .Unset(c => c.PendingGeneration),
            cancellationToken: ct);

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
