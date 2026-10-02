using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Catalogs;
using Factum.Backend.Services.Profile;
using Factum.Backend.Services.Reports;

namespace Factum.Backend.Services.Cases;

public interface ICaseService
{
    Task<List<Case>> ListAsync(string officerDni, CancellationToken ct = default);
    Task<Result<(Case Case, List<FileInfoDto> Files)>> GetAsync(string id, string officerDni,
        CancellationToken ct = default);
    Task<Result<Case>> CreateAsync(CreateCaseRequest request, User officer, CancellationToken ct = default);
    Task<Result<Case>> UpdateAsync(string id, UpdateCaseRequest request, User officer,
        CancellationToken ct = default);
    Task<Result<ReportTextsDto>> GetReportTextDefaultsAsync(string id, string officerDni,
        CancellationToken ct = default);
    Task<Result<ReportTexts>> SaveReportTextsAsync(string id, ReportTextsDto request, string officerDni,
        CancellationToken ct = default);
    Task<Result<List<CaptureRole>>> UpsertCaptureRolesAsync(string id, CaptureRolesRequest request,
        string officerDni, CancellationToken ct = default);
    Task<Result<FileInfoDto>> UploadFileAsync(string id, string officerDni, string filename,
        string? sourcePath, Stream content, CancellationToken ct = default);
    Task<Result<List<FileInfoDto>>> ListFilesAsync(string id, string officerDni,
        CancellationToken ct = default);
    Task<Result<GenerateResponse>> GenerateAsync(string id, string officerDni,
        CancellationToken ct = default);
    /// <summary>Contraseña del ZIP cifrado de un caso propio (GET …/zip-password).</summary>
    Task<Result<ZipPasswordResponse>> GetZipPasswordAsync(string id, string officerDni,
        CancellationToken ct = default);
    Task<Result<(string Path, string ContentType, string FileName)>> DownloadAsync(
        string id, string filename, string officerDni, CancellationToken ct = default);
}

public sealed class CaseService : ICaseService
{
    public const string NotEditableMessage = "El caso ya fue generado y no se puede editar";
    public const string LegacyCaseMessage =
        "Este caso se creó antes del informe pericial. Completá los datos de la causa para generarlo.";

    private readonly ICaseRepository _repo;
    private readonly IStorageService _storage;
    private readonly IReportService _reports;
    private readonly IExpertProfileService _profiles;
    private readonly IReportSettings _reportSettings;
    private readonly ICatalogService _catalogs;
    private readonly ILogger<CaseService> _log;

    public CaseService(ICaseRepository repo, IStorageService storage, IReportService reports,
        IExpertProfileService profiles, IReportSettings reportSettings, ICatalogService catalogs,
        ILogger<CaseService> log)
    {
        _catalogs = catalogs;
        _repo = repo;
        _storage = storage;
        _reports = reports;
        _profiles = profiles;
        _reportSettings = reportSettings;
        _log = log;
    }

    public Task<List<Case>> ListAsync(string officerDni, CancellationToken ct = default) =>
        _repo.ListByOfficerAsync(officerDni, ct);

    // Caso del dueño, o el error que corresponde (404 / 403).
    private async Task<(Case? Case, Result<T>? Error)> LoadOwnedAsync<T>(string id, string officerDni,
        CancellationToken ct)
    {
        var cas = await _repo.FindByIdAsync(id, ct);
        if (cas is null) return (null, Result.NotFound<T>());
        if (cas.Officer.Dni != officerDni) return (null, Result.Forbidden<T>());
        return (cas, null);
    }

    private static bool IsEditable(Case cas) =>
        cas.Status is not (CaseStatus.Generating or CaseStatus.Completed);

    public async Task<Result<(Case Case, List<FileInfoDto> Files)>> GetAsync(
        string id, string officerDni, CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<(Case, List<FileInfoDto>)>(id, officerDni, ct);
        if (cas is null) return error!;

        var files = await _storage.ListFilesAsync(cas.Id);
        return Result.Ok((cas, MergeSources(files, cas)));
    }

    // ── Crear / editar (T5, T6) ──────────────────────────────────────────────

    public async Task<Result<Case>> CreateAsync(CreateCaseRequest request, User officer,
        CancellationToken ct = default)
    {
        var fields = CaseFields.From(request);
        var d = request.Device;
        var imei = (d?.Imei ?? string.Empty).Trim();

        var outcome = CaseValidation.ValidateCaseData(fields, imei);
        if (outcome.Error is not null) return Result.Invalid<Case>(outcome.Error);

        var profile = await _profiles.FindAsync(officer.Dni, ct);
        var missing = new List<string>();
        if (profile is null || !profile.IsComplete) missing.Add(CaseValidation.KeyPerfil);
        missing.AddRange(outcome.Missing);
        if (missing.Count > 0) return Result.Invalid<Case>(CaseValidation.MissingMessage, missing);

        var cas = new Case
        {
            Id = Guid.NewGuid().ToString(),
            SchemaVersion = 1,
            Perito = profile!.ToSnapshot(),
            NroReferencia = fields.NroReferencia,
            NombreDenunciante = fields.NombreDenunciante,
            DniDenunciante = fields.DniDenunciante,
            Observaciones = fields.Observaciones ?? string.Empty,
            NombreTribunal = fields.NombreTribunal,
            OrganismoTribunal = fields.OrganismoTribunal,
            SalaTribunal = fields.SalaTribunal,
            IntegrantesTribunal = fields.IntegrantesTribunal,
            Integrantes = fields.Integrantes?.ToList(),
            TipoCausa = fields.TipoCausa,
            Caratula = fields.Caratula,
            ParteDenunciante = fields.ParteDenunciante,
            ParteDenunciada = fields.ParteDenunciada,
            ObjetoCausa = fields.ObjetoCausa,
            AmbitoCausa = fields.AmbitoCausa,
            FechaIntervencion = fields.FechaIntervencion,
            NombreProponente = fields.NombreProponente,
            ProfesionProponente = fields.ProfesionProponente,
            MatriculaProponente = fields.MatriculaProponente,
            TipoDispositivo = fields.TipoDispositivo,
            LineaDispositivo = fields.LineaDispositivo,
            Officer = officer,
            Device = d is not null ? new DeviceInfo
            {
                Serial = d.Serial ?? string.Empty,
                Name = (d.Name ?? string.Empty).Trim(),
                Manufacturer = d.Manufacturer ?? string.Empty,
                Model = d.Model ?? string.Empty,
                AndroidVersion = d.AndroidVersion,
                Imei = imei,
                Platform = d.Platform ?? "android",
                OsVersion = d.OsVersion ?? string.Empty,
            } : new DeviceInfo(),
            Status = CaseStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };

        _storage.CaseDir(cas.Id);
        await _repo.InsertAsync(cas, ct);
        // Best-effort (nunca lanza): un fallo del catálogo no cambia la respuesta del POST.
        await _catalogs.RecordUsageAsync(officer.Dni, CatalogLogic.ValuesForCreate(cas), ct);
        return Result.Ok(cas);
    }

    // PUT /api/cases/{id}: escritura por acción del dueño sobre su propio caso editable. Si es
    // un borrador viejo (SchemaVersion 0), queda en 1 y recibe la copia del perfil (§8.9).
    public async Task<Result<Case>> UpdateAsync(string id, UpdateCaseRequest request, User officer,
        CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<Case>(id, officer.Dni, ct);
        if (cas is null) return error!;
        if (!IsEditable(cas)) return Result.Conflict<Case>(NotEditableMessage);

        var fields = CaseFields.From(request);
        var newImei = request.Imei?.Trim();
        var effectiveImei = string.IsNullOrEmpty(newImei) ? cas.Device.Imei : newImei;

        var outcome = CaseValidation.ValidateCaseData(fields, effectiveImei);
        if (outcome.Error is not null) return Result.Invalid<Case>(outcome.Error);

        var profile = await _profiles.FindAsync(officer.Dni, ct);
        var missing = new List<string>();
        if (profile is null || !profile.IsComplete) missing.Add(CaseValidation.KeyPerfil);
        missing.AddRange(outcome.Missing);
        if (missing.Count > 0) return Result.Invalid<Case>(CaseValidation.MissingMessage, missing);

        var update = new CaseDataUpdate(
            fields.NroReferencia, fields.NombreDenunciante, fields.DniDenunciante,
            fields.NombreTribunal, fields.OrganismoTribunal, fields.SalaTribunal, fields.IntegrantesTribunal,
            fields.TipoCausa, fields.Caratula, fields.ParteDenunciante, fields.ParteDenunciada,
            fields.ObjetoCausa, fields.AmbitoCausa, fields.FechaIntervencion,
            fields.NombreProponente, fields.ProfesionProponente, fields.MatriculaProponente,
            fields.TipoDispositivo, fields.LineaDispositivo,
            profile!.ToSnapshot(), string.IsNullOrEmpty(newImei) ? null : newImei,
            // Observaciones solo se pisa si el request la trae (el cliente nuevo no la manda).
            fields.Observaciones,
            // null (cliente viejo) → $unset Integrantes en el repositorio (D2).
            fields.Integrantes);

        if (!await _repo.UpdateCaseDataAsync(id, update, ct))
            return Result.Conflict<Case>(NotEditableMessage);

        var saved = (await _repo.FindByIdAsync(id, ct))!;
        // Solo los campos que cambiaron respecto de `cas` (leído antes del update, D12). Best-effort.
        await _catalogs.RecordUsageAsync(officer.Dni, CatalogLogic.ValuesForUpdate(saved, cas), ct);
        return Result.Ok(saved);
    }

    // ── Paso Informe ──────────────────────────────────────────────────────────

    public async Task<Result<ReportTextsDto>> GetReportTextDefaultsAsync(string id, string officerDni,
        CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<ReportTextsDto>(id, officerDni, ct);
        if (cas is null) return error!;

        var files = MergeSources(await _storage.ListFilesAsync(id), cas);
        return Result.Ok(ReportValues.RenderDefaults(cas, _reportSettings, files));
    }

    public async Task<Result<ReportTexts>> SaveReportTextsAsync(string id, ReportTextsDto request,
        string officerDni, CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<ReportTexts>(id, officerDni, ct);
        if (cas is null) return error!;
        if (!IsEditable(cas)) return Result.Conflict<ReportTexts>(NotEditableMessage);

        var validationError = CaseValidation.ValidateReportTexts(request);
        if (validationError is not null) return Result.Invalid<ReportTexts>(validationError);
        ReportTextFormats.TryNormalize(request.Formato, out var formato);

        var texts = new ReportTexts
        {
            ObjetoInforme = request.ObjetoInforme ?? string.Empty,
            OperacionesRealizadas = request.OperacionesRealizadas ?? string.Empty,
            AseguramientoEvidencia = request.AseguramientoEvidencia ?? string.Empty,
            Resultados = request.Resultados ?? string.Empty,
            ValoracionTecnica = request.ValoracionTecnica ?? string.Empty,
            Conclusiones = request.Conclusiones ?? string.Empty,
            NotasTecnicas = request.NotasTecnicas ?? string.Empty,
            Reserva = request.Reserva ?? string.Empty,
            // null (texto plano) no se escribe en Mongo ([BsonIgnoreIfNull]). Los textos se
            // guardan tal cual llegan.
            Formato = formato,
            UpdatedAt = DateTime.UtcNow,
        };

        if (!await _repo.UpdateReportTextsAsync(id, texts, ct))
            return Result.Conflict<ReportTexts>(NotEditableMessage);
        return Result.Ok(texts);
    }

    // ── Roles de captura (T7) ─────────────────────────────────────────────────

    public async Task<Result<List<CaptureRole>>> UpsertCaptureRolesAsync(string id,
        CaptureRolesRequest request, string officerDni, CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<List<CaptureRole>>(id, officerDni, ct);
        if (cas is null) return error!;
        if (!IsEditable(cas)) return Result.Conflict<List<CaptureRole>>(NotEditableMessage);

        var items = request.CaptureRoles ?? [];
        var caseDir = _storage.CaseDir(id);
        var sources = cas.FileSources.Select(s => s.Filename).ToHashSet(StringComparer.Ordinal);
        var changes = new List<(string Filename, string? Role)>();

        foreach (var item in items)
        {
            var filename = item.Filename?.Trim() ?? string.Empty;
            var role = string.IsNullOrWhiteSpace(item.Role) ? null : item.Role.Trim();

            if (role is not null && !CaptureRole.IsValid(role))
                return Result.Invalid<List<CaptureRole>>(
                    $"Rol de captura inválido: tiene que ser \"{CaptureRole.ImeiModelo}\", \"{CaptureRole.NombreDispositivo}\" o null");

            // Solo un nombre de archivo plano del directorio del caso (sin rutas).
            if (filename.Length == 0 || Path.GetFileName(filename) != filename ||
                filename is "." or "..")
                return Result.Invalid<List<CaptureRole>>("Nombre de archivo inválido");

            var path = Path.Combine(caseDir, filename);
            if (!File.Exists(path))
                return Result.Invalid<List<CaptureRole>>($"El archivo {filename} no existe en el caso");
            if (EvidenceClassifier.Classify(filename, sources.Contains(filename)) != EvidenceClass.Screenshot)
                return Result.Invalid<List<CaptureRole>>($"El archivo {filename} no es una captura de pantalla");
            if (new FileInfo(path).Length == 0)
                return Result.Invalid<List<CaptureRole>>($"El archivo {filename} está vacío");

            changes.Add((filename, role));
        }

        var list = await _repo.UpsertCaptureRolesAsync(id, changes, ct);
        return list is null
            ? Result.Conflict<List<CaptureRole>>(NotEditableMessage)
            : Result.Ok(list);
    }

    // ── Archivos ──────────────────────────────────────────────────────────────

    public async Task<Result<FileInfoDto>> UploadFileAsync(string id, string officerDni,
        string filename, string? sourcePath, Stream content, CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<FileInfoDto>(id, officerDni, ct);
        if (cas is null) return error!;

        var (path, hash) = await _storage.SaveFileAsync(id, filename, content, ct);
        var info = new FileInfo(path);

        if (!string.IsNullOrWhiteSpace(sourcePath))
            await _repo.AddFileSourceAsync(id, info.Name, sourcePath, ct);

        return Result.Ok(new FileInfoDto(info.Name, info.Length, hash, info.LastWriteTimeUtc, sourcePath));
    }

    public async Task<Result<List<FileInfoDto>>> ListFilesAsync(string id, string officerDni,
        CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<List<FileInfoDto>>(id, officerDni, ct);
        if (cas is null) return error!;

        var files = await _storage.ListFilesAsync(id);
        return Result.Ok(MergeSources(files, cas));
    }

    // Los archivos se listan escaneando el directorio del caso (StorageService no sabe nada
    // de metadata) — la ruta de origen en el dispositivo vive en el Case de Mongo y se pega acá.
    private static List<FileInfoDto> MergeSources(List<FileInfoDto> files, Case cas)
    {
        if (cas.FileSources.Count == 0) return files;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in cas.FileSources) map[s.Filename] = s.SourcePath;
        return files.Select(f => map.TryGetValue(f.Name, out var src)
            ? f with { SourcePath = src } : f).ToList();
    }

    // ── Generar (§7.8) ────────────────────────────────────────────────────────

    public async Task<Result<GenerateResponse>> GenerateAsync(string id, string officerDni,
        CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<GenerateResponse>(id, officerDni, ct);
        if (cas is null) return error!;

        // Un caso ya generado no se regenera: su ZIP y su informe quedan como están.
        if (cas.Status == CaseStatus.Completed)
            return Result.Conflict<GenerateResponse>("El caso ya fue generado");

        if (cas.SchemaVersion == 0)
            return Result.Invalid<GenerateResponse>(LegacyCaseMessage);

        var caseDir = _storage.CaseDir(id);
        var hasImeiCapture = cas.CaptureRoles.Any(r =>
            r.Role == CaptureRole.ImeiModelo &&
            Path.GetFileName(r.Filename) == r.Filename &&
            File.Exists(Path.Combine(caseDir, r.Filename)) &&
            new FileInfo(Path.Combine(caseDir, r.Filename)).Length > 0);

        var missing = CaseValidation.ValidateForGenerate(cas, hasImeiCapture);
        if (missing.Count > 0)
            return Result.Invalid<GenerateResponse>(CaseValidation.MissingMessage, missing);

        // ListFilesAsync escanea el directorio completo del caso — si un intento de generación
        // anterior falló a mitad de camino, el ZIP/DOCX que llegó a crear queda en esa carpeta y
        // se listaría como evidencia. Sin este filtro, un reintento re-empaqueta el ZIP/DOCX de
        // la vez anterior (incluido el propio ZIP dentro de sí mismo).
        var files = MergeSources(await _storage.ListFilesAsync(id), cas)
            .Where(f => !ReportService.IsGeneratedArtifact(f.Name))
            .ToList();
        if (files.Count == 0)
            return Result.Invalid<GenerateResponse>("El caso no tiene archivos. Capturá evidencia primero.");

        await _repo.UpdateStatusAsync(id, CaseStatus.Generating, ct);
        _log.LogInformation("Generando informe para caso {CaseId}", id);

        try
        {
            var result = await _reports.GenerateAsync(cas, files, caseDir, ct);
            var now = DateTime.UtcNow;

            await _repo.UpdateGeneratedAsync(id, now, result.Password, result.ZipEncrypted,
                result.ZipEncryption, result.ZipHash, result.ZipFilename, result.PdfFilename,
                result.ReportHash, ct);

            cas = (await _repo.FindByIdAsync(id, ct))!;
            return Result.Ok(new GenerateResponse(
                cas, result.ZipHash, result.Password,
                new FilesDto(result.ZipFilename, result.PdfFilename), result.ReportHash));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error generando informe para caso {CaseId}", id);
            await _repo.UpdateStatusAsync(id, CaseStatus.Error, ct);
            return Result.Fail<GenerateResponse>($"Error generando informe: {ex.Message}");
        }
    }

    public const string NoEncryptedZipMessage = "Este caso no tiene un ZIP cifrado";

    // Único lugar (además de la respuesta de generate) por donde sale la contraseña. Caso ajeno
    // → 403 como el resto de los endpoints del caso (P1-A). Casos viejos (D8) o generados con
    // EncryptZip=false → 404. No se loguea nada.
    public async Task<Result<ZipPasswordResponse>> GetZipPasswordAsync(string id, string officerDni,
        CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<ZipPasswordResponse>(id, officerDni, ct);
        if (cas is null) return error!;

        if (cas.Status != CaseStatus.Completed || !cas.ZipEncrypted || string.IsNullOrEmpty(cas.ZipPassword))
            return Result.NotFound<ZipPasswordResponse>(NoEncryptedZipMessage);

        return Result.Ok(new ZipPasswordResponse(cas.ZipPassword));
    }

    public async Task<Result<(string Path, string ContentType, string FileName)>> DownloadAsync(
        string id, string filename, string officerDni, CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<(string, string, string)>(id, officerDni, ct);
        if (cas is null) return error!;

        var caseDir = _storage.CaseDir(id);
        var path = Path.Combine(caseDir, filename);
        if (!File.Exists(path)) return Result.NotFound<(string, string, string)>("Archivo no encontrado");

        var contentType = Path.GetExtension(filename).ToLower() switch
        {
            ".zip" => "application/zip",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream"
        };

        return Result.Ok((path, contentType, filename));
    }
}
