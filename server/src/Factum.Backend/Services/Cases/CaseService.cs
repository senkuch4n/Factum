using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Catalogs;
using Factum.Backend.Services.Profile;
using Factum.Backend.Services.Reports;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Services.Cases;

public interface ICaseService
{
    Task<List<Case>> ListAsync(string officerDni, CancellationToken ct = default);
    /// <summary>dashboard-kpis-tendencias: KPIs + tendencia mensual del perito (GET …/stats).</summary>
    Task<CaseStatsResponse> GetStatsAsync(string officerDni, int tzOffsetMinutes, CancellationToken ct = default);
    /// <summary>dashboard-breakdown: distribución por dimensión del perito (GET …/breakdown).</summary>
    Task<Result<BreakdownResponse>> BreakdownAsync(string officerDni, string? dimension, DateOnly? from,
        DateOnly? to, CancellationToken ct = default);
    Task<Result<(Case Case, List<FileInfoDto> Files)>> GetAsync(string id, string officerDni,
        CancellationToken ct = default);
    Task<Result<Case>> CreateAsync(CreateCaseRequest request, User officer, CancellationToken ct = default);
    Task<Result<Case>> UpdateAsync(string id, UpdateCaseRequest request, User officer,
        CancellationToken ct = default);
    Task<Result<ReportTextsDto>> GetReportTextDefaultsAsync(string id, string officerDni,
        CancellationToken ct = default);
    Task<Result<ReportTexts>> SaveReportTextsAsync(string id, ReportTextsDto request, string officerDni,
        CancellationToken ct = default);
    /// <summary>
    /// versionado-informe: historial de versiones de report_texts del caso propio (más reciente
    /// primero). 404 si no existe, 403 si es de otro perito. Funciona en cualquier Status.
    /// </summary>
    Task<Result<ReportTextVersionsResponse>> GetReportTextVersionsAsync(string id, string officerDni,
        CancellationToken ct = default);
    /// <summary>
    /// trazabilidad-caso: eventos de cadena de custodia del caso, ascendente por timestamp. Dueño
    /// (por Officer.Dni) o superadmin (<paramref name="actorRole"/>). 404 si no existe, 403 si es
    /// ajeno y no superadmin.
    /// </summary>
    Task<Result<List<CaseEvent>>> ListEventsAsync(string id, User actor, string actorRole,
        CancellationToken ct = default);
    Task<Result<List<CaptureRole>>> UpsertCaptureRolesAsync(string id, CaptureRolesRequest request,
        string officerDni, CancellationToken ct = default);
    /// <summary>
    /// Subida atómica (subida-archivos-grandes §4.2/§5.5): nunca lanza; los rechazos traen
    /// <c>Details</c> con <c>code</c> (<see cref="UploadErrorCodes"/>) y los tamaños.
    /// </summary>
    Task<Result<FileInfoDto>> UploadFileAsync(string id, string officerDni, string filename,
        string? sourcePath, long? contentLength, Stream content, CancellationToken ct = default);
    /// <summary>Prechequeo sin cuerpo de la subida (GET …/files/upload-check, §4.1).</summary>
    Task<Result<UploadCheckResponse>> CheckUploadAsync(string id, string officerDni, string? filename,
        long? size, CancellationToken ct = default);
    Task<Result<List<FileInfoDto>>> ListFilesAsync(string id, string officerDni,
        CancellationToken ct = default);
    Task<Result<GenerateResponse>> GenerateAsync(string id, string officerDni,
        CancellationToken ct = default);
    /// <summary>Contraseña del ZIP cifrado de un caso propio (GET …/zip-password).</summary>
    Task<Result<ZipPasswordResponse>> GetZipPasswordAsync(string id, string officerDni,
        CancellationToken ct = default);
    Task<Result<(string Path, string ContentType, string FileName)>> DownloadAsync(
        string id, string filename, string officerDni, CancellationToken ct = default);
    /// <summary>Capturas insertables del caso propio, con su disponibilidad (GET …/report-images).</summary>
    Task<Result<List<ReportImageDto>>> ListReportImagesAsync(string id, string officerDni,
        CancellationToken ct = default);
    /// <summary>
    /// Vista previa de una captura disponible (GET …/files/{filename}/preview): el stream queda
    /// ABIERTO en solo lectura y lo cierra el que lo sirve.
    /// </summary>
    Task<Result<(Stream Content, string ContentType)>> GetReportImagePreviewAsync(string id, string filename,
        string officerDni, CancellationToken ct = default);

    // ── zip-local-informe-servidor (§5.2, §5.3, §5.5, §5.6) ─────────────────
    // Ninguno lanza: los rechazos traen Details con code (EvidenceErrorCodes).

    /// <summary>Registra (upsert por nombre) archivos ya guardados en Tatana (PUT …/evidence).</summary>
    Task<Result<EvidenceResponse>> RegisterEvidenceAsync(string id, RegisterEvidenceRequest request,
        string officerDni, CancellationToken ct = default);
    /// <summary>Saca un archivo del manifiesto (DELETE …/evidence/{filename}). No toca Tatana.</summary>
    Task<Result<EvidenceResponse>> DeleteEvidenceAsync(string id, string filename, string officerDni,
        CancellationToken ct = default);
    /// <summary>Valida contra el manifiesto y abre un intento de generación (no cambia el Status).</summary>
    Task<Result<PrepareGenerationResponse>> PrepareGenerationAsync(string id, PrepareGenerationRequest request,
        string officerDni, CancellationToken ct = default);
    /// <summary>
    /// Cierra la generación con el ZIP ya armado en Tatana: lee el multipart en streaming, arma el
    /// DOCX con las capturas en <c>.generate-tmp/&lt;gid&gt;/</c> (que siempre se borra) y marca el
    /// caso <c>completed</c>.
    /// </summary>
    Task<Result<GenerateResponse>> FinishGenerationAsync(string id, string officerDni, string? contentType,
        Stream body, CancellationToken ct = default);
}

public sealed partial class CaseService : ICaseService
{
    public const string NotEditableMessage = "El caso ya fue generado y no se puede editar";
    public const string LegacyCaseMessage =
        "Este caso se creó antes del informe pericial. Completá los datos de la causa para generarlo.";

    /// <summary>versionado-informe (D3/D5-B): tope de versiones por caso.</summary>
    public const int MaxReportVersions = 50;

    private readonly ICaseRepository _repo;
    private readonly IStorageService _storage;
    private readonly IReportService _reports;
    private readonly IExpertProfileService _profiles;
    private readonly IReportSettings _reportSettings;
    private readonly ICatalogService _catalogs;
    private readonly ICaseEventRepository _caseEvents;
    private readonly IHttpContextAccessor _httpContext;
    private readonly StorageOptions _storageOptions;
    private readonly ReportOptions _reportOptions;
    private readonly ILogger<CaseService> _log;

    public CaseService(ICaseRepository repo, IStorageService storage, IReportService reports,
        IExpertProfileService profiles, IReportSettings reportSettings, ICatalogService catalogs,
        ICaseEventRepository caseEvents, IHttpContextAccessor httpContext,
        IOptions<StorageOptions> storageOptions, IOptions<ReportOptions> reportOptions, ILogger<CaseService> log)
    {
        _storageOptions = storageOptions.Value;
        _reportOptions = reportOptions.Value;
        _catalogs = catalogs;
        _repo = repo;
        _storage = storage;
        _reports = reports;
        _profiles = profiles;
        _reportSettings = reportSettings;
        _caseEvents = caseEvents;
        _httpContext = httpContext;
        _log = log;
    }

    // ── trazabilidad-caso: registro best-effort y aislado de hitos (DT4) ────────

    /// <summary>
    /// Inserta un <see cref="CaseEvent"/> fuera de la operación principal: traga y loguea cualquier
    /// excepción (nunca cambia la respuesta de la acción del usuario) y usa CancellationToken.None
    /// (un request cancelado no pierde el hito ya ocurrido). Igual que _catalogs.RecordUsageAsync.
    /// </summary>
    private async Task RecordEventAsync(CaseEvent evt)
    {
        try { await _caseEvents.InsertAsync(evt, CancellationToken.None); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo registrar el evento {Type} del caso {CaseId}", evt.Type, evt.CaseId);
        }
    }

    // IP del request (DT7-b): null fuera de un request HTTP (el evento se registra igual).
    private string? ActorIp() => _httpContext.HttpContext?.Connection.RemoteIpAddress?.ToString();

    // Evento base con el actor congelado del caso (dueño) + IP del request.
    private CaseEvent CaseEventFor(Case cas, string type) => new()
    {
        CaseId = cas.Id,
        Type = type,
        ActorDni = cas.Officer.Dni,
        ActorName = cas.Officer.Name,
        Timestamp = DateTime.UtcNow,
        Ip = ActorIp(),
    };

    public async Task<List<Case>> ListAsync(string officerDni, CancellationToken ct = default)
    {
        var cases = await _repo.ListByOfficerAsync(officerDni, ct);
        foreach (var cas in cases) ResolveStorage(cas);
        return cases;
    }

    // Caso del dueño, o el error que corresponde (404 / 403). Con EvidenceStorage resuelto.
    private async Task<(Case? Case, Result<T>? Error)> LoadOwnedAsync<T>(string id, string officerDni,
        CancellationToken ct)
    {
        var cas = await _repo.FindByIdAsync(id, ct);
        if (cas is null) return (null, Result.NotFound<T>());
        if (cas.Officer.Dni != officerDni) return (null, Result.Forbidden<T>());
        return (ResolveStorage(cas), null);
    }

    // zip-local-informe-servidor §3.2: completa EvidenceStorage EN MEMORIA (el repositorio nunca
    // hace Replace, así que esto no se persiste). Solo lee el disco y no crea la carpeta del caso.
    private Case ResolveStorage(Case cas)
    {
        cas.EvidenceStorage = EvidenceManifest.ResolveStorage(cas, _storage.HasEvidenceFiles);
        return cas;
    }

    private static bool IsAgent(Case cas) => cas.EvidenceStorage == EvidenceStorages.Agent;

    private static bool IsEditable(Case cas) =>
        cas.Status is not (CaseStatus.Generating or CaseStatus.Completed);

    public async Task<Result<(Case Case, List<FileInfoDto> Files)>> GetAsync(
        string id, string officerDni, CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<(Case, List<FileInfoDto>)>(id, officerDni, ct);
        if (cas is null) return error!;

        // Flujo agent: los archivos son el manifiesto (no hay evidencia en el servidor).
        if (IsAgent(cas)) return Result.Ok((cas, EvidenceManifest.ToFileInfos(cas)));

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
            CreatedAt = DateTime.UtcNow,
            // zip-local-informe-servidor (D8): todo caso nuevo guarda la evidencia en la PC del perito.
            EvidenceStorage = EvidenceStorages.Agent,
        };

        _storage.CaseDir(cas.Id);
        await _repo.InsertAsync(cas, ct);
        // Best-effort (nunca lanza): un fallo del catálogo no cambia la respuesta del POST.
        await _catalogs.RecordUsageAsync(officer.Dni, CatalogLogic.ValuesForCreate(cas), ct);
        // trazabilidad-caso: hito de alta (best-effort). El actor es el dueño recién congelado.
        await RecordEventAsync(CaseEventFor(cas, CaseEventTypes.CaseCreated));
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

        var saved = ResolveStorage((await _repo.FindByIdAsync(id, ct))!);
        // Solo los campos que cambiaron respecto de `cas` (leído antes del update, D12). Best-effort.
        await _catalogs.RecordUsageAsync(officer.Dni, CatalogLogic.ValuesForUpdate(saved, cas), ct);
        // trazabilidad-caso (DT6): case_updated con los campos que cambiaron. Si nada cambió, no se
        // registra (evita ruido de un PUT trivial). Best-effort.
        var changedFields = CaseEventFields.ChangedFields(cas, saved);
        if (changedFields.Count > 0)
        {
            var evt = CaseEventFor(saved, CaseEventTypes.CaseUpdated);
            evt.Detail = new CaseEventDetail { ChangedFields = changedFields };
            await RecordEventAsync(evt);
        }
        return Result.Ok(saved);
    }

    // ── Paso Informe ──────────────────────────────────────────────────────────

    public async Task<Result<ReportTextsDto>> GetReportTextDefaultsAsync(string id, string officerDni,
        CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<ReportTextsDto>(id, officerDni, ct);
        if (cas is null) return error!;

        var files = IsAgent(cas)
            ? EvidenceManifest.ToFileInfos(cas)
            : MergeSources(await _storage.ListFilesAsync(id), cas);
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
        // versionado-informe: trigger ∈ {null,"","save","restore"}; restored_from solo con "restore".
        if (!ReportVersioning.TryNormalizeTrigger(request.Trigger, request.RestoredFrom,
                out var trigger, out var triggerError))
            return Result.Invalid<ReportTexts>(triggerError!);
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

        // versionado-informe (D1/D2/D4): el snapshot candidato se agrega al historial SOLO si pasa el
        // de-dup contra la última versión. El autor es el dueño congelado del caso (D9). El append va
        // ATÓMICO en el mismo UpdateOne que pisa report_texts.
        var snapshot = ReportVersioning.Snapshot(texts);
        ReportTextVersion? versionToPush = null;
        List<ReportTextVersion>? prunedOverride = null;
        if (!ReportVersioning.IsDuplicate(cas.ReportTextVersions, snapshot))
        {
            var version = new ReportTextVersion
            {
                Id = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.UtcNow,
                AuthorDni = cas.Officer.Dni,
                AuthorName = cas.Officer.Name,
                Trigger = trigger,
                RestoredFrom = trigger == ReportVersionTriggers.Restore ? request.RestoredFrom : null,
                Texts = snapshot,
            };
            (versionToPush, prunedOverride) = ReportVersioning.PlanAppend(cas.ReportTextVersions, version);
        }

        if (!await _repo.UpdateReportTextsAsync(id, texts, versionToPush, prunedOverride, ct))
            return Result.Conflict<ReportTexts>(NotEditableMessage);
        return Result.Ok(texts);
    }

    public async Task<Result<ReportTextVersionsResponse>> GetReportTextVersionsAsync(string id,
        string officerDni, CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<ReportTextVersionsResponse>(id, officerDni, ct);
        if (cas is null) return error!;
        // Más reciente primero (orden descendente por created_at).
        var versions = cas.ReportTextVersions
            .OrderByDescending(v => v.CreatedAt)
            .Select(ReportVersioning.ToDto)
            .ToList();
        return Result.Ok(new ReportTextVersionsResponse(versions));
    }

    // ── trazabilidad-caso: GET /api/cases/{id}/events ──────────────────────────

    public async Task<Result<List<CaseEvent>>> ListEventsAsync(string id, User actor, string actorRole,
        CancellationToken ct = default)
    {
        var cas = await _repo.FindByIdAsync(id, ct);
        if (cas is null) return Result.NotFound<List<CaseEvent>>();
        // Dueño o superadmin (D9-A). En dev/external el rol siempre es "cliente": solo vale el dueño.
        if (cas.Officer.Dni != actor.Dni && actorRole != UserRoles.Superadmin)
            return Result.Forbidden<List<CaseEvent>>();
        var events = await _caseEvents.ListByCaseAsync(id, ct);
        return Result.Ok(events);
    }

    // ── Roles de captura (T7) ─────────────────────────────────────────────────

    public async Task<Result<List<CaptureRole>>> UpsertCaptureRolesAsync(string id,
        CaptureRolesRequest request, string officerDni, CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<List<CaptureRole>>(id, officerDni, ct);
        if (cas is null) return error!;
        if (!IsEditable(cas)) return Result.Conflict<List<CaptureRole>>(NotEditableMessage);

        var items = request.CaptureRoles ?? [];
        var agent = IsAgent(cas);
        // Flujo agent: se valida contra el manifiesto, sin tocar el disco del servidor.
        var caseDir = agent ? null : _storage.CaseDir(id);
        var sources = cas.FileSources.Select(s => s.Filename).ToHashSet(StringComparer.Ordinal);
        if (agent)
            foreach (var e in cas.Evidence)
                if (!string.IsNullOrEmpty(e.SourcePath)) sources.Add(e.Filename);
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

            if (agent)
            {
                var registered = cas.Evidence.FirstOrDefault(e => e.Filename == filename);
                if (registered is null)
                    return Result.Invalid<List<CaptureRole>>($"El archivo {filename} no existe en el caso");
                if (EvidenceClassifier.Classify(filename, sources.Contains(filename)) != EvidenceClass.Screenshot)
                    return Result.Invalid<List<CaptureRole>>($"El archivo {filename} no es una captura de pantalla");
                if (registered.Size <= 0)
                    return Result.Invalid<List<CaptureRole>>($"El archivo {filename} está vacío");
                changes.Add((filename, role));
                continue;
            }

            var path = Path.Combine(caseDir!, filename);
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

    // ── Subida de evidencia (subida-archivos-grandes §4.1, §4.2, §5.5) ───────

    public async Task<Result<UploadCheckResponse>> CheckUploadAsync(string id, string officerDni,
        string? filename, long? size, CancellationToken ct = default)
    {
        var (cas, error) = await PrecheckUploadAsync<UploadCheckResponse>(id, officerDni, filename, size, ct);
        if (cas is null) return error!;
        return Result.Ok(new UploadCheckResponse(_storageOptions.MaxUploadBytes));
    }

    // Chequeos 1-6 de §4.1, en orden (el primero que falla gana). Ninguno lee el cuerpo ni
    // crea archivos. Devuelve el caso si pasa.
    private async Task<(Case? Case, Result<T>? Error)> PrecheckUploadAsync<T>(string id, string officerDni,
        string? filename, long? size, CancellationToken ct)
    {
        if (!EvidenceUpload.IsValidUploadName(filename))
            return (null, UploadFail<T>(ErrorKind.Validation, EvidenceUpload.InvalidFilenameMessage,
                UploadErrorCodes.InvalidFilename));

        var (cas, error) = await LoadOwnedAsync<T>(id, officerDni, ct);
        if (cas is null) return (null, error);

        // zip-local-informe-servidor §5.4: un caso agent no recibe evidencia en el servidor.
        if (IsAgent(cas))
            return (null, UploadFail<T>(ErrorKind.Conflict, EvidenceManifest.EvidenceOnAgentMessage,
                EvidenceErrorCodes.EvidenceOnAgent));

        if (!IsEditable(cas))
            return (null, UploadFail<T>(ErrorKind.Conflict, NotEditableMessage, UploadErrorCodes.CaseNotEditable));

        if (size is not { } length || length < 0)
            return (null, UploadFail<T>(ErrorKind.Validation, EvidenceUpload.LengthRequiredMessage,
                UploadErrorCodes.LengthRequired));

        var rejection = EvidenceUpload.Evaluate(length, _storageOptions.MaxUploadBytes,
            _storage.GetAvailableFreeBytes(), _storageOptions.MinFreeBytes);
        if (rejection is { Kind: UploadRejectionKind.TooLarge })
            return (null, UploadFail<T>(ErrorKind.PayloadTooLarge,
                EvidenceUpload.FileTooLargeMessage(filename!, length, rejection.MaxUploadBytes),
                UploadErrorCodes.FileTooLarge,
                ("size", length), ("max_upload_bytes", rejection.MaxUploadBytes)));
        if (rejection is { Kind: UploadRejectionKind.InsufficientStorage })
            return (null, UploadFail<T>(ErrorKind.InsufficientStorage,
                EvidenceUpload.InsufficientStorageMessage(filename!, rejection.RequiredBytes, rejection.AvailableBytes),
                UploadErrorCodes.InsufficientStorage,
                ("size", length), ("required_bytes", rejection.RequiredBytes),
                ("available_bytes", rejection.AvailableBytes)));

        return (cas, null);
    }

    // Error de subida con { code, ...extra } (claves en snake_case literal, §7).
    private static Result<T> UploadFail<T>(ErrorKind kind, string error, string code,
        params (string Key, object? Value)[] extra)
    {
        var details = new Dictionary<string, object?> { ["code"] = code };
        foreach (var (key, value) in extra) details[key] = value;
        return Result.Fail<T>(kind, error, details);
    }

    public async Task<Result<FileInfoDto>> UploadFileAsync(string id, string officerDni,
        string filename, string? sourcePath, long? contentLength, Stream content, CancellationToken ct = default)
    {
        // Ninguna excepción sale de acá: un 500 de Kestrel saldría sin CORS ("Failed to fetch").
        try
        {
            var (cas, error) = await PrecheckUploadAsync<FileInfoDto>(id, officerDni, filename, contentLength, ct);
            if (cas is null) return error!;
            var expected = contentLength!.Value;

            StagedUpload staged;
            try
            {
                staged = await _storage.StageUploadAsync(id, filename, content, expected, ct);
            }
            catch (UploadIncompleteException ex)
            {
                _log.LogInformation("Subida incompleta al caso {CaseId} ({Name}): {Received} de {Expected} bytes",
                    id, filename, ex.ReceivedBytes, ex.ExpectedBytes);
                return IncompleteUpload(filename, ex.ReceivedBytes, ex.ExpectedBytes);
            }
            catch (InsufficientStorageException ex)
            {
                _log.LogWarning(ex, "Disco lleno subiendo al caso {CaseId} ({Name}, {Size} bytes)",
                    id, filename, expected);
                return UploadFail<FileInfoDto>(ErrorKind.InsufficientStorage,
                    EvidenceUpload.InsufficientStorageMessage(filename, ex.AvailableBytes is null ? null : ex.RequiredBytes,
                        ex.AvailableBytes),
                    UploadErrorCodes.InsufficientStorage,
                    ("size", expected), ("required_bytes", ex.RequiredBytes), ("available_bytes", ex.AvailableBytes));
            }

            using (staged)
            {
                // DT9: si empezó una generación durante la subida, se descarta el temporal.
                var fresh = await _repo.FindByIdAsync(id, CancellationToken.None);
                if (fresh is null || !IsEditable(fresh))
                {
                    _log.LogInformation("Subida al caso {CaseId} ({Name}) descartada: el caso dejó de ser editable",
                        id, filename);
                    return UploadFail<FileInfoDto>(ErrorKind.Conflict, NotEditableMessage,
                        UploadErrorCodes.CaseNotEditable);
                }

                var info = staged.Commit();
                if (!string.IsNullOrWhiteSpace(sourcePath))
                {
                    try
                    {
                        await _repo.AddFileSourceAsync(id, info.Name, sourcePath, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        // El archivo ya quedó publicado (completo); falta solo la ruta de origen.
                        // Reenviarlo lo reemplaza con el mismo contenido y registra la ruta.
                        _log.LogError(ex, "Subida al caso {CaseId} ({Name}) guardada sin registrar su ruta de origen",
                            id, filename);
                        return UploadFail<FileInfoDto>(ErrorKind.ServerError,
                            $"El servidor guardó {filename} pero no pudo registrar su ruta de origen; volvé a enviarlo",
                            UploadErrorCodes.StorageError);
                    }
                }

                // trazabilidad-caso (DT8): evidencia subida por el flujo server (sin host). Best-effort.
                var evt = CaseEventFor(cas, CaseEventTypes.EvidenceAdded);
                evt.Filename = info.Name;
                evt.Detail = new CaseEventDetail { Sha256 = staged.Hash, Size = info.Length };
                await RecordEventAsync(evt);

                return Result.Ok(new FileInfoDto(info.Name, info.Length, staged.Hash, info.LastWriteTimeUtc,
                    sourcePath));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // El cliente cortó o canceló: nadie lee la respuesta; el temporal ya se borró.
            _log.LogInformation("Subida al caso {CaseId} ({Name}) cancelada/cortada por el cliente", id, filename);
            return IncompleteUpload(filename, 0, contentLength ?? 0);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error guardando la subida al caso {CaseId} ({Name})", id, filename);
            return UploadFail<FileInfoDto>(ErrorKind.ServerError, EvidenceUpload.StorageErrorMessage(filename),
                UploadErrorCodes.StorageError);
        }
    }

    private static Result<FileInfoDto> IncompleteUpload(string filename, long received, long expected) =>
        UploadFail<FileInfoDto>(ErrorKind.Validation,
            EvidenceUpload.IncompleteUploadMessage(filename, received, expected),
            UploadErrorCodes.IncompleteUpload,
            ("size", expected), ("received_bytes", received));

    public async Task<Result<List<FileInfoDto>>> ListFilesAsync(string id, string officerDni,
        CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<List<FileInfoDto>>(id, officerDni, ct);
        if (cas is null) return error!;
        if (IsAgent(cas)) return Result.Ok(EvidenceManifest.ToFileInfos(cas));

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

        // zip-local-informe-servidor §5.4: el flujo agent genera con prepare/finish.
        if (IsAgent(cas))
            return UploadFail<GenerateResponse>(ErrorKind.Conflict, EvidenceManifest.EvidenceOnAgentMessage,
                EvidenceErrorCodes.EvidenceOnAgent);

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
        // editor-imagenes-informe §4.6: referencias a capturas no disponibles (solo Markdown),
        // después de las claves de siempre y ANTES de pasar a Generating.
        var broken = CaseValidation.BrokenImageKeys(cas.ReportTexts,
            name => ReportImageFiles.Inspect(caseDir, name).IsAvailable);
        if (broken.Count > 0) missing = [.. missing, .. broken];
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

        // versionado-informe (D2): snapshot "generate" del report_texts vigente ANTES de pasar a
        // Generating, append atómico bajo Editable(id). La poda conserva la primera y todas las
        // "generate". Best-effort para el versionado (un fallo no frena la generación).
        await AppendGenerateVersionAsync(cas, ct);

        await _repo.UpdateStatusAsync(id, CaseStatus.Generating, ct);
        _log.LogInformation("Generando informe para caso {CaseId}", id);

        try
        {
            var result = await _reports.GenerateAsync(cas, files, caseDir, ct);
            var now = DateTime.UtcNow;

            await _repo.UpdateGeneratedAsync(id, now, result.Password, result.ZipEncrypted,
                result.ZipEncryption, result.ZipHash, result.ZipFilename, result.PdfFilename,
                result.ReportHash, ct);

            cas = ResolveStorage((await _repo.FindByIdAsync(id, ct))!);
            // trazabilidad-caso (DT10): hito de informe generado (best-effort).
            var ok = CaseEventFor(cas, CaseEventTypes.ReportGenerated);
            ok.Detail = new CaseEventDetail { ZipHash = result.ZipHash, ReportHash = result.ReportHash };
            await RecordEventAsync(ok);
            return Result.Ok(new GenerateResponse(
                cas, result.ZipHash, result.Password,
                new FilesDto(result.ZipFilename, result.PdfFilename), result.ReportHash));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error generando informe para caso {CaseId}", id);
            await _repo.UpdateStatusAsync(id, CaseStatus.Error, ct);
            // trazabilidad-caso (DT10): fallo real de generación (best-effort). Motivo truncado.
            var fail = CaseEventFor(cas, CaseEventTypes.ReportFailed);
            fail.Detail = new CaseEventDetail { Reason = TruncateReason(ex.Message) };
            await RecordEventAsync(fail);
            return Result.Fail<GenerateResponse>($"Error generando informe: {ex.Message}");
        }
    }

    /// <summary>
    /// versionado-informe (D2/D3): agrega una versión "generate" del report_texts vigente, con poda
    /// que conserva la primera y todas las "generate". Append atómico (AppendReportVersionAsync).
    /// Best-effort: si falla, solo loguea (no frena la generación). De-dup: nada si es idéntica a la
    /// última.
    /// </summary>
    private async Task AppendGenerateVersionAsync(Case cas, CancellationToken ct)
    {
        try
        {
            var version = ReportVersioning.GenerateVersionFor(cas);
            if (version is null) return;
            var (push, pruned) = ReportVersioning.PlanAppend(cas.ReportTextVersions, version);
            await _repo.AppendReportVersionAsync(cas.Id, push ?? version, pruned, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo agregar la versión \"generate\" al caso {CaseId}", cas.Id);
        }
    }

    // DT10: el motivo de report_failed nunca lleva datos sensibles y se trunca a ~300 chars.
    private static string TruncateReason(string? reason)
    {
        var r = (reason ?? string.Empty).Trim();
        return r.Length <= 300 ? r : r[..300];
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

    // ── Imágenes del informe (editor-imagenes-informe §4.4, §4.5, §6.6) ─────
    // Solo lectura: no modifican el caso ni los archivos y no auditan (D6 de la HU).

    public const string InvalidFilenameMessage = "Nombre de archivo inválido";
    public const string ImageNotAvailableMessage = "Imagen no disponible";

    public async Task<Result<List<ReportImageDto>>> ListReportImagesAsync(string id, string officerDni,
        CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<List<ReportImageDto>>(id, officerDni, ct);
        if (cas is null) return error!;

        var roles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in cas.CaptureRoles) roles[r.Filename] = r.Role;

        // Flujo agent (§5.4): del manifiesto. Sin dimensiones (los bytes están en Tatana); el
        // cliente cruza "available" con lo que tiene la carpeta del caso.
        if (IsAgent(cas))
            return Result.Ok(cas.Evidence
                .Where(e => ReportImageRef.IsInsertableName(e.Filename))
                .OrderBy(e => e.Filename, StringComparer.Ordinal)
                .Select(e => new ReportImageDto(e.Filename, e.Size, roles.GetValueOrDefault(e.Filename),
                    e.Size > 0, null, null))
                .ToList());

        var caseDir = _storage.CaseDir(id);

        var images = (await _storage.ListFilesAsync(id))
            .Where(f => ReportImageRef.IsInsertableName(f.Name))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .Select(f =>
            {
                var inspection = ReportImageFiles.Inspect(caseDir, f.Name);
                return new ReportImageDto(f.Name, f.Size, roles.GetValueOrDefault(f.Name), inspection.IsAvailable,
                    inspection.IsAvailable ? inspection.Width : null,
                    inspection.IsAvailable ? inspection.Height : null);
            })
            .ToList();
        return Result.Ok(images);
    }

    public async Task<Result<(Stream Content, string ContentType)>> GetReportImagePreviewAsync(string id,
        string filename, string officerDni, CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<(Stream, string)>(id, officerDni, ct);
        if (cas is null) return error!;

        if (!ReportImageRef.IsPlainName(filename))
            return Result.Invalid<(Stream, string)>(InvalidFilenameMessage);

        // Un caso Completed ya no tiene archivos sueltos: cae en "no disponible" como cualquier
        // otro faltante. No se usa DownloadAsync (sirve cualquier archivo con cualquier tipo).
        if (!ReportImageFiles.TryOpen(_storage.CaseDir(id), filename, out var stream, out var contentType,
                out _, out _))
            return Result.NotFound<(Stream, string)>(ImageNotAvailableMessage);

        return Result.Ok(((Stream)stream, contentType));
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
