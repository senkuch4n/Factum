using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Reports;
using Factum.Backend.Services.Timestamping;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Factum.Backend.Services.Cases;

/// <summary>
/// Flujo "evidencia en la PC del perito" (zip-local-informe-servidor §5.2, §5.3, §5.5 y §5.6):
/// manifiesto y generación en dos tramos. Ningún método lanza: todo error sale como Result con
/// <c>code</c> (<see cref="EvidenceErrorCodes"/>), así ninguna respuesta sale sin JSON.
/// </summary>
public sealed partial class CaseService
{
    public const string FileNotFoundMessage = "Archivo no encontrado";
    public const int MaxMetadataBytes = 1024 * 1024;
    private const int ImageBufferSize = 81920;

    private static readonly JsonSerializerOptions MetadataJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
    };

    private static Result<T> EvidenceFail<T>(ErrorKind kind, string error, string code,
        params (string Key, object? Value)[] extra) => UploadFail<T>(kind, error, code, extra);

    /// <summary>
    /// metadatos-dispositivo D-T7: si la medición de cierre se registró antes de abrir este intento, se descarta EN
    /// MEMORIA para el informe (nunca se persiste). Puro.
    /// </summary>
    internal static void DropStaleClockClose(Case cas, DateTime pendingCreatedAt)
    {
        if (cas.DeviceDetails?.ClockClose is { } c && c.RecordedAt < pendingCreatedAt)
            cas.DeviceDetails.ClockClose = null;
    }

    private static Result<T> OtherPc<T>(Case cas) =>
        EvidenceFail<T>(ErrorKind.Conflict, EvidenceManifest.EvidenceOnOtherPcMessage(cas.EvidenceHost?.Hostname ?? ""),
            EvidenceErrorCodes.EvidenceOnOtherPc, ("evidence_hostname", cas.EvidenceHost?.Hostname));

    // ── §5.2 PUT /api/cases/{id}/evidence ────────────────────────────────────

    public const int MaxRegisterAttempts = 3;

    // wizard-concurrencia-pestanas DT10: registro idempotente + concurrencia optimista. Un re-registro
    // idéntico no escribe ni emite eventos; otro sha256 deja evidence_updated. La escritura exige que
    // EvidenceRevision no haya cambiado desde la lectura; si cambió, se relee y se replanifica (hasta 3
    // intentos, después 409 evidence_busy).
    public async Task<Result<EvidenceResponse>> RegisterEvidenceAsync(string id, RegisterEvidenceRequest request,
        string officerDni, CancellationToken ct = default)
    {
        var validation = EvidenceManifest.ValidateItems(request.Host, request.Items);

        for (var attempt = 1; attempt <= MaxRegisterAttempts; attempt++)
        {
            var (cas, error) = await LoadOwnedAsync<EvidenceResponse>(id, officerDni, ct);
            if (cas is null) return error!;
            if (!IsEditable(cas))
                return EvidenceFail<EvidenceResponse>(ErrorKind.Conflict, NotEditableMessage, EvidenceErrorCodes.CaseNotEditable);
            if (!IsAgent(cas))
                return EvidenceFail<EvidenceResponse>(ErrorKind.Conflict, EvidenceManifest.EvidenceOnServerMessage,
                    EvidenceErrorCodes.EvidenceOnServer);

            if (!validation.IsValid)
                return validation.Filename is null
                    ? EvidenceFail<EvidenceResponse>(ErrorKind.Validation, validation.Error!, EvidenceErrorCodes.InvalidManifest)
                    : EvidenceFail<EvidenceResponse>(ErrorKind.Validation, validation.Error!, EvidenceErrorCodes.InvalidManifest,
                        ("filename", validation.Filename));

            var host = request.Host!;
            var hostname = host.Hostname!.Trim();
            var firstRegistration = cas.Evidence.Count == 0 || cas.EvidenceHost is null;
            if (!firstRegistration && !EvidenceManifest.SameHost(hostname, cas.EvidenceHost!.Hostname))
                return OtherPc<EvidenceResponse>(cas);

            var now = DateTime.UtcNow;
            var plan = EvidenceManifest.PlanRegistration(cas.Evidence, validation.Items, now);
            if (!plan.Changed)
            {
                // Todo ya estaba registrado igual: no se escribe nada (ni el host) y no hay eventos.
                _log.LogInformation("Caso {CaseId}: {N} archivo(s) ya registrados sin cambios", id, validation.Items.Count);
                // sellado-tiempo §6.2: también trae timestamps (el estado real del sello en la bandeja).
                return Result.Ok(await EvidenceResponseAsync(id, cas.Evidence, cas.EvidenceHost));
            }

            // El host se fija en el primer registro (o con el manifiesto vacío); después solo se
            // actualizan la versión de Tatana y la carpeta del caso.
            var evidenceHost = firstRegistration
                ? new EvidenceHost
                {
                    Hostname = hostname,
                    OsUser = host.OsUser?.Trim() ?? "",
                    AgentVersion = host.AgentVersion?.Trim() ?? "",
                    CaseDirectory = host.CaseDirectory?.Trim() ?? "",
                    RegisteredAt = now,
                }
                : new EvidenceHost
                {
                    Hostname = cas.EvidenceHost!.Hostname,
                    OsUser = cas.EvidenceHost.OsUser,
                    AgentVersion = string.IsNullOrWhiteSpace(host.AgentVersion) ? cas.EvidenceHost.AgentVersion : host.AgentVersion.Trim(),
                    CaseDirectory = string.IsNullOrWhiteSpace(host.CaseDirectory) ? cas.EvidenceHost.CaseDirectory : host.CaseDirectory.Trim(),
                    RegisteredAt = cas.EvidenceHost.RegisteredAt,
                };
            var sources = EvidenceManifest.MergeFileSources(cas.FileSources, validation.Items);

            if (!await _repo.RegisterEvidenceAsync(id, cas.EvidenceRevision, plan.Merged, evidenceHost, sources, ct))
            {
                // No matcheó: o dejó de ser editable (lo detecta la relectura) o la revisión cambió (carrera).
                _log.LogInformation("Caso {CaseId}: el manifiesto cambió durante el registro (intento {N})", id, attempt);
                // Espera corta con jitter antes de releer: con varias pestañas a la vez, evita que choquen
                // de nuevo en el mismo instante y agoten los intentos.
                if (attempt < MaxRegisterAttempts)
                    await Task.Delay(Random.Shared.Next(15, 60) * attempt, ct);
                continue;
            }

            _log.LogInformation(
                "Caso {CaseId}: registro de {N} archivo(s) desde {Host}: {Added} nuevo(s), {Updated} actualizado(s)",
                id, validation.Items.Count, evidenceHost.Hostname, plan.Added.Count, plan.Updated.Count);
            // trazabilidad-caso (DT8): un evidence_added por archivo nuevo, con filename/sha256/size y el
            // host del manifiesto (agent_mode no viaja en el manifiesto → null). Best-effort.
            foreach (var item in plan.Added)
            {
                var evt = CaseEventFor(cas, CaseEventTypes.EvidenceAdded);
                evt.Filename = item.Filename;
                evt.Hostname = evidenceHost.Hostname;
                evt.OsUser = evidenceHost.OsUser;
                evt.Detail = new CaseEventDetail { Sha256 = item.Sha256, Size = item.Size };
                await RecordEventAsync(evt);
            }
            // DT10: mismo nombre con otro contenido.
            foreach (var (item, previous) in plan.Updated)
            {
                var evt = CaseEventFor(cas, CaseEventTypes.EvidenceUpdated);
                evt.Filename = item.Filename;
                evt.Hostname = evidenceHost.Hostname;
                evt.OsUser = evidenceHost.OsUser;
                evt.Detail = new CaseEventDetail { Sha256 = item.Sha256, Size = item.Size, PreviousSha256 = previous };
                await RecordEventAsync(evt);
            }
            // sellado-tiempo §6.2: sello de lo nuevo o actualizado, después de los eventos de evidencia. Sin el ct
            // del request: si el navegador corta, el lease vence y la cola lo retoma. Nunca lanza.
            var toSeal = plan.Added.Select(i => (i.Filename, i.Sha256))
                .Concat(plan.Updated.Select(u => (u.Item.Filename, u.Item.Sha256))).ToList();
            if (toSeal.Count > 0) await _timestamps.SealEvidenceAsync(cas, toSeal, CancellationToken.None);
            return Result.Ok(await EvidenceResponseAsync(id, plan.Merged, evidenceHost));
        }

        _log.LogWarning("Caso {CaseId}: registro de evidencia abandonado tras {N} intentos en carrera", id,
            MaxRegisterAttempts);
        return EvidenceFail<EvidenceResponse>(ErrorKind.Conflict, EvidenceManifest.EvidenceBusyMessage,
            EvidenceErrorCodes.EvidenceBusy);
    }

    // ── §5.3 DELETE /api/cases/{id}/evidence/{filename} ──────────────────────

    public async Task<Result<EvidenceResponse>> DeleteEvidenceAsync(string id, string filename, string officerDni,
        CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<EvidenceResponse>(id, officerDni, ct);
        if (cas is null) return error!;
        if (!IsEditable(cas))
            return EvidenceFail<EvidenceResponse>(ErrorKind.Conflict, NotEditableMessage, EvidenceErrorCodes.CaseNotEditable);
        if (!IsAgent(cas))
            return EvidenceFail<EvidenceResponse>(ErrorKind.Conflict, EvidenceManifest.EvidenceOnServerMessage,
                EvidenceErrorCodes.EvidenceOnServer);
        if (!ReportImageRef.IsPlainName(filename))
            return Result.Invalid<EvidenceResponse>(InvalidFilenameMessage);
        if (cas.Evidence.All(e => e.Filename != filename))
            return Result.NotFound<EvidenceResponse>(FileNotFoundMessage);

        if (!await _repo.RemoveEvidenceAsync(id, filename, ct))
            return EvidenceFail<EvidenceResponse>(ErrorKind.Conflict, NotEditableMessage, EvidenceErrorCodes.CaseNotEditable);

        // trazabilidad-caso (DT8): baja de evidencia (best-effort).
        var removed = CaseEventFor(cas, CaseEventTypes.EvidenceRemoved);
        removed.Filename = filename;
        await RecordEventAsync(removed);

        var fresh = (await _repo.FindByIdAsync(id, ct))!;
        return Result.Ok(await EvidenceResponseAsync(id, fresh.Evidence, fresh.EvidenceHost));
    }

    // sellado-tiempo §6.3: datos de sellos para el informe. Zip = el sello del ZIP si quedó granted; Evidence = los
    // granted cuyo (Filename, Sha256) coincide con el manifiesto del caso releído. Si Mongo falla, sin sellos de
    // evidencia (el informe lo dice archivo por archivo) en lugar de frenar la generación.
    private async Task<ReportTimestampInfo> ReportTimestampInfoAsync(Case cas, CaseTimestamp? zipTs)
    {
        static TimestampView? View(CaseTimestamp? t) =>
            t is { Status: TimestampStatuses.Granted, GenTime: { } g } ? new TimestampView(t.Authority ?? "", g, t.SerialNumber ?? "") : null;

        var evidence = new Dictionary<string, TimestampView>(StringComparer.Ordinal);
        try
        {
            var all = await _timestamps.ListAsync(cas.Id, CancellationToken.None);
            foreach (var item in cas.Evidence)
            {
                var t = all.FirstOrDefault(x => x.Target == TimestampTargets.Evidence && x.Filename == item.Filename &&
                                                x.Sha256 == item.Sha256 && x.Status == TimestampStatuses.Granted);
                if (View(t) is { } v) evidence[item.Filename] = v;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Caso {CaseId}: no se pudieron leer los sellos de la evidencia para el informe", cas.Id);
        }
        return new ReportTimestampInfo(View(zipTs), evidence);
    }

    // sellado-tiempo §7.2: EvidenceResponse con timestamping_enabled, timestamp_authority y un sello por ítem del
    // manifiesto actual que lo tenga para su sha256 actual. Si Mongo falla al leer los sellos, timestamps = [].
    private async Task<EvidenceResponse> EvidenceResponseAsync(string caseId, List<EvidenceItem> evidence,
        EvidenceHost? host)
    {
        var dtos = new List<TimestampDto>();
        try
        {
            var all = await _timestamps.ListAsync(caseId, CancellationToken.None);
            foreach (var item in evidence)
            {
                var t = all.FirstOrDefault(x => x.Target == TimestampTargets.Evidence &&
                                                x.Filename == item.Filename && x.Sha256 == item.Sha256);
                if (t is not null) dtos.Add(TimestampDto.From(t, _reportSettings.Zone));
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Caso {CaseId}: no se pudieron leer los sellos de tiempo", caseId);
        }
        return new EvidenceResponse(evidence, host, _timestamps.IsEnabled, _timestamps.AuthorityHint, dtos);
    }

    // ── §5.5 POST /api/cases/{id}/generate/prepare ───────────────────────────

    public async Task<Result<PrepareGenerationResponse>> PrepareGenerationAsync(string id,
        PrepareGenerationRequest request, string officerDni, CancellationToken ct = default)
    {
        var (cas, error) = await LoadOwnedAsync<PrepareGenerationResponse>(id, officerDni, ct);
        if (cas is null) return error!;
        if (cas.Status == CaseStatus.Completed)
            return Result.Conflict<PrepareGenerationResponse>("El caso ya fue generado");
        if (cas.Status == CaseStatus.Generating)
            return EvidenceFail<PrepareGenerationResponse>(ErrorKind.Conflict, NotEditableMessage,
                EvidenceErrorCodes.CaseNotEditable);
        if (cas.SchemaVersion == 0)
            return Result.Invalid<PrepareGenerationResponse>(LegacyCaseMessage);
        if (!IsAgent(cas))
            return EvidenceFail<PrepareGenerationResponse>(ErrorKind.Conflict, EvidenceManifest.EvidenceOnServerMessage,
                EvidenceErrorCodes.EvidenceOnServer);
        if (cas.Evidence.Count == 0)
            return EvidenceFail<PrepareGenerationResponse>(ErrorKind.Validation, EvidenceManifest.NoEvidenceMessage,
                EvidenceErrorCodes.NoEvidence);
        if (!EvidenceManifest.SameHost(request.Hostname, cas.EvidenceHost?.Hostname))
            return OtherPc<PrepareGenerationResponse>(cas);

        // Validación con el manifiesto (los bytes reales se revalidan en finish, paso 6).
        var hasImeiCapture = cas.CaptureRoles.Any(r =>
            r.Role == CaptureRole.ImeiModelo && EvidenceManifest.HasNonEmpty(cas, r.Filename));
        var missing = CaseValidation.ValidateForGenerate(cas, hasImeiCapture);
        var broken = CaseValidation.BrokenImageKeys(cas.ReportTexts,
            n => ReportImageRef.IsInsertableName(n) && EvidenceManifest.HasNonEmpty(cas, n));
        if (broken.Count > 0) missing = [.. missing, .. broken];
        if (missing.Count > 0)
            return Result.Invalid<PrepareGenerationResponse>(CaseValidation.MissingMessage, missing);

        // versionado-informe (D2): snapshot "generate" al abrir el intento (flujo agent), append
        // atómico bajo Editable(id). Best-effort para el versionado; no frena el prepare.
        await AppendGenerateVersionAsync(cas, ct);

        var pending = new PendingGeneration
        {
            Id = Guid.NewGuid().ToString("N"),
            Password = _reportSettings.EncryptZip ? ReportService.GeneratePassword() : null,
            ZipFilename = ReportService.ResolveZipFilename(cas),
            CreatedAt = DateTime.UtcNow,
        };
        // wizard-pulido-ux DT10: un nombre ya registrado (caso o intento previo) se conserva.
        if (!string.Equals(pending.ZipFilename, ReportService.ZipFilenameFor(cas), StringComparison.Ordinal))
            _log.LogInformation("Caso {CaseId}: se conserva el nombre de ZIP ya registrado {ZipFilename}",
                id, pending.ZipFilename);
        // Pisa cualquier intento anterior. No cambia el Status (D-T3).
        if (!await _repo.SetPendingGenerationAsync(id, pending, ct))
            return EvidenceFail<PrepareGenerationResponse>(ErrorKind.Conflict, NotEditableMessage,
                EvidenceErrorCodes.CaseNotEditable);

        _log.LogInformation("Caso {CaseId}: intento de generación {GenerationId} abierto ({N} archivos)",
            id, pending.Id, cas.Evidence.Count);
        return Result.Ok(new PrepareGenerationResponse(
            pending.Id, pending.ZipFilename, cas.NroReferencia, pending.Password, pending.Password is not null,
            EvidenceManifest.ToManifestFiles(cas), EvidenceManifest.ReportImageNames(cas)));
    }

    // ── §5.6 POST /api/cases/{id}/generate/finish ────────────────────────────

    public async Task<Result<GenerateResponse>> FinishGenerationAsync(string id, string officerDni,
        string? contentType, Stream body, CancellationToken ct = default)
    {
        var maxBytes = _reportOptions.MaxGenerateUploadBytes;
        string? tmp = null;
        try
        {
            // 1. Caso.
            var (cas, error) = await LoadOwnedAsync<GenerateResponse>(id, officerDni, ct);
            if (cas is null) return error!;
            if (cas.Status == CaseStatus.Completed)
                return Result.Conflict<GenerateResponse>("El caso ya fue generado");
            if (cas.Status == CaseStatus.Generating)
                return EvidenceFail<GenerateResponse>(ErrorKind.Conflict, NotEditableMessage,
                    EvidenceErrorCodes.CaseNotEditable);
            if (!IsAgent(cas))
                return EvidenceFail<GenerateResponse>(ErrorKind.Conflict, EvidenceManifest.EvidenceOnServerMessage,
                    EvidenceErrorCodes.EvidenceOnServer);

            // 2. Metadata (1.ª parte del multipart).
            if (!TryGetBoundary(contentType, out var boundary))
                return InvalidZipInfo("El pedido tiene que ser multipart/form-data con la parte metadata");
            var reader = new MultipartReader(boundary, body);
            var section = await reader.ReadNextSectionAsync(ct);
            if (section is null || PartName(section) != "metadata")
                return InvalidZipInfo("Falta la parte metadata (tiene que ser la primera)");
            var metadataBytes = await ReadLimitedAsync(section.Body, MaxMetadataBytes, ct);
            if (metadataBytes is null)
                return InvalidZipInfo("La parte metadata supera 1 MiB");
            FinishGenerationMetadata? meta;
            try
            {
                meta = JsonSerializer.Deserialize<FinishGenerationMetadata>(metadataBytes, MetadataJson);
            }
            catch (JsonException)
            {
                return InvalidZipInfo("La parte metadata no es un JSON válido");
            }
            if (meta is null) return InvalidZipInfo("La parte metadata está vacía");

            var pending = cas.PendingGeneration;
            if (pending is null || !string.Equals(meta.GenerationId, pending.Id, StringComparison.Ordinal))
                return EvidenceFail<GenerateResponse>(ErrorKind.Conflict, EvidenceManifest.GenerationStaleMessage,
                    EvidenceErrorCodes.GenerationStale);

            var loc = meta.ZipLocation;
            if (!EvidenceManifest.IsSha256(meta.ZipHash) ||
                !string.Equals(meta.ZipFilename, pending.ZipFilename, StringComparison.Ordinal) ||
                meta.ZipSize is not > 0 ||
                meta.Encrypted is not { } encrypted || encrypted != (pending.Password is not null) ||
                loc is null || string.IsNullOrWhiteSpace(loc.Hostname) ||
                string.IsNullOrWhiteSpace(loc.Directory) || string.IsNullOrWhiteSpace(loc.Path))
                return InvalidZipInfo(EvidenceManifest.InvalidZipInfoMessage);
            if (!EvidenceManifest.SameHost(loc.Hostname, cas.EvidenceHost?.Hostname))
                return OtherPc<GenerateResponse>(cas);

            // 3. Lo que verificó Tatana contra el manifiesto.
            if (cas.Evidence.Count == 0)
                return EvidenceFail<GenerateResponse>(ErrorKind.Validation, EvidenceManifest.NoEvidenceMessage,
                    EvidenceErrorCodes.NoEvidence);
            var mismatched = EvidenceManifest.Matches(meta.Files, cas.Evidence);
            if (mismatched.Count > 0)
                return EvidenceFail<GenerateResponse>(ErrorKind.Conflict, EvidenceManifest.ManifestMismatchMessage,
                    EvidenceErrorCodes.ManifestMismatch, ("mismatched", mismatched));

            // 4. Capturas del informe → .generate-tmp/<gid>/, con hash al vuelo y tope.
            var reportImages = EvidenceManifest.ReportImageNames(cas).ToHashSet(StringComparer.Ordinal);
            var expectedHashes = EvidenceManifest.Hashes(cas);
            tmp = _storage.NewGenerationTempDir(pending.Id);
            var received = new HashSet<string>(StringComparer.Ordinal);
            long total = metadataBytes.LongLength;
            while ((section = await reader.ReadNextSectionAsync(ct)) is not null)
            {
                var name = PartName(section);
                var filename = PartFilename(section);
                if (name != "images" || filename is null || !reportImages.Contains(filename) || !received.Add(filename))
                    return EvidenceFail<GenerateResponse>(ErrorKind.Validation,
                        filename is null ? "Parte del pedido inesperada" : $"La captura {filename} no es parte del informe o está repetida",
                        EvidenceErrorCodes.InvalidManifest, ("filename", filename));

                var (hash, written) = await WriteImageAsync(section.Body, Path.Combine(tmp, filename),
                    maxBytes - total, ct);
                if (hash is null) return RequestTooLarge(maxBytes);
                total += written;
                if (!string.Equals(hash, expectedHashes[filename], StringComparison.Ordinal))
                    return EvidenceFail<GenerateResponse>(ErrorKind.Validation,
                        EvidenceManifest.ImageHashMismatchMessage(filename), EvidenceErrorCodes.ImageHashMismatch,
                        ("filename", filename));
            }

            // 5. Todas las capturas que el informe embebe.
            var missingImages = reportImages.Where(n => !received.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (missingImages.Count > 0)
                return EvidenceFail<GenerateResponse>(ErrorKind.Validation, EvidenceManifest.MissingImagesMessage,
                    EvidenceErrorCodes.MissingImages, ("missing_images", missingImages));

            // 6. Revalidación con bytes reales, sobre el caso releído (los textos se pudieron editar
            // mientras se armaba el ZIP). Hasta acá el Status no cambió.
            var fresh = ResolveStorage((await _repo.FindByIdAsync(id, ct))!);
            if (fresh.PendingGeneration?.Id != pending.Id || !IsEditable(fresh) ||
                EvidenceManifest.Matches(meta.Files, fresh.Evidence).Count > 0)
                return EvidenceFail<GenerateResponse>(ErrorKind.Conflict, EvidenceManifest.GenerationStaleMessage,
                    EvidenceErrorCodes.GenerationStale);
            var tmpDir = tmp;
            bool Available(string n) =>
                received.Contains(n) && ReportImageFiles.Inspect(tmpDir, n).IsAvailable;
            var hasImeiCapture = fresh.CaptureRoles.Any(r => r.Role == CaptureRole.ImeiModelo && Available(r.Filename));
            var missing = CaseValidation.ValidateForGenerate(fresh, hasImeiCapture);
            var broken = CaseValidation.BrokenImageKeys(fresh.ReportTexts, Available);
            if (broken.Count > 0) missing = [.. missing, .. broken];
            if (missing.Count > 0)
                return Result.Invalid<GenerateResponse>(CaseValidation.MissingMessage, missing);

            // 7. Paso atómico a Generating (solo si el intento sigue siendo este).
            if (!await _repo.TryMarkGeneratingAsync(id, pending.Id, ct))
                return EvidenceFail<GenerateResponse>(ErrorKind.Conflict, EvidenceManifest.GenerationStaleMessage,
                    EvidenceErrorCodes.GenerationStale);
            _log.LogInformation("Generando informe (flujo agent) para caso {CaseId}", id);

            // 8-9. Sin el token del request: si el navegador se corta acá, el caso igual se
            // completa y el ZIP pendiente (con este hash) se confirma después (auto-commit).
            try
            {
                var zipLocation = new ZipLocation
                {
                    Hostname = loc.Hostname!.Trim(),
                    Directory = loc.Directory!.Trim(),
                    Path = loc.Path!.Trim(),
                };
                // sellado-tiempo §6.3: sello del ZIP (su hash ya se conoce) ANTES del DOCX, para que el informe
                // pueda citarlo. Con presupuesto acotado: si la TSA no responde, queda pending y el informe deja
                // constancia de que el ZIP no tenía sello al emitirse.
                var sealing = _timestamps.IsEnabled;
                var zipTs = sealing
                    ? await _timestamps.SealNowAsync(fresh, TimestampTargets.Zip, pending.ZipFilename, meta.ZipHash!,
                        _timestamps.GenerationBudget)
                    : null;
                var timestampInfo = sealing ? await ReportTimestampInfoAsync(fresh, zipTs) : null;

                // metadatos-dispositivo D-T7: una medición de cierre de un intento anterior no entra al informe
                // (solo en memoria; lo guardado no cambia).
                DropStaleClockClose(fresh, pending.CreatedAt);
                var report = await _reports.GenerateReportAsync(fresh, EvidenceManifest.ToFileInfos(fresh),
                    EvidenceManifest.Hashes(fresh), tmp, _storage.CaseDir(id), pending.ZipFilename, meta.ZipHash!,
                    CancellationToken.None, timestampInfo);
                if (zipTs is { Status: TimestampStatuses.Granted })
                    await _timestamps.MarkZipInReportAsync(zipTs.Id);
                // El hash del DOCX recién se conoce ahora: el informe no puede contener su propio sello.
                if (sealing)
                    await _timestamps.SealNowAsync(fresh, TimestampTargets.Report, report.PdfFilename,
                        report.ReportHash, _timestamps.GenerationBudget);
                await _repo.CompleteAgentGenerationAsync(id, DateTime.UtcNow, pending.Password, meta.ZipHash!,
                    pending.ZipFilename, report.PdfFilename, report.ReportHash, zipLocation,
                    sealing ? "enabled" : "disabled", CancellationToken.None);

                var saved = ResolveStorage((await _repo.FindByIdAsync(id, CancellationToken.None))!);
                // trazabilidad-caso (DT10): hito de informe generado (flujo agent, best-effort).
                var ok = CaseEventFor(saved, CaseEventTypes.ReportGenerated);
                ok.Detail = new CaseEventDetail { ZipHash = meta.ZipHash!, ReportHash = report.ReportHash };
                await RecordEventAsync(ok);
                // pulido-post-qa-final D7: solo id y hashes cortos (nada de contraseña, expediente, nombres ni rutas).
                _log.LogInformation("Informe generado para caso {CaseId} (ZIP {ZipHash}, DOCX {ReportHash})", id,
                    ShortHash(meta.ZipHash), ShortHash(report.ReportHash));
                return Result.Ok(new GenerateResponse(saved, meta.ZipHash!, pending.Password,
                    new FilesDto(pending.ZipFilename, report.PdfFilename), report.ReportHash, zipLocation));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error generando informe (flujo agent) para caso {CaseId}", id);
                try { await _repo.MarkAgentGenerationFailedAsync(id, CancellationToken.None); }
                catch (Exception markEx) { _log.LogError(markEx, "No se pudo marcar el caso {CaseId} en error", id); }
                // sellado-tiempo §6.3: el ZIP de este intento se descarta; su sello pendiente pasa a failed (sin
                // evento: ya está report_failed). Uno ya obtenido queda: es un sello válido de un hash que existió.
                await _timestamps.AbortZipAsync(id, pending.ZipFilename, meta.ZipHash!);
                // trazabilidad-caso (DT10): fallo real de generación (flujo agent, best-effort).
                var failEvt = CaseEventFor(fresh, CaseEventTypes.ReportFailed);
                failEvt.Detail = new CaseEventDetail { Reason = TruncateReason(ex.Message) };
                await RecordEventAsync(failEvt);
                return Result.Fail<GenerateResponse>($"Error generando informe: {ex.Message}");
            }
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return RequestTooLarge(maxBytes);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _log.LogInformation("Generación (flujo agent) del caso {CaseId} cortada por el cliente", id);
            return Result.Invalid<GenerateResponse>("El pedido se cortó antes de terminar; no se generó nada");
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException or IOException)
        {
            // Multipart mal formado o cuerpo cortado: nada cambió en el caso.
            _log.LogInformation(ex, "Generación (flujo agent) del caso {CaseId}: cuerpo inválido", id);
            return Result.Invalid<GenerateResponse>("El pedido multipart no es válido o llegó incompleto; no se generó nada");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error procesando la generación (flujo agent) del caso {CaseId}", id);
            return Result.Fail<GenerateResponse>(ErrorKind.ServerError, "El servidor no pudo procesar la generación; no se generó nada");
        }
        finally
        {
            // Siempre: con éxito, con error o con cancelación. Si falla, lo limpia el próximo arranque.
            if (tmp is not null)
            {
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true); }
                catch (Exception ex) { _log.LogWarning(ex, "No se pudo borrar el temporal de generación {Temp}", tmp); }
            }
        }
    }

    private static Result<GenerateResponse> InvalidZipInfo(string message) =>
        EvidenceFail<GenerateResponse>(ErrorKind.Validation, message, EvidenceErrorCodes.InvalidZipInfo);

    private static Result<GenerateResponse> RequestTooLarge(long maxBytes) =>
        EvidenceFail<GenerateResponse>(ErrorKind.PayloadTooLarge, EvidenceManifest.RequestTooLargeMessage(maxBytes),
            EvidenceErrorCodes.RequestTooLarge, ("max_bytes", maxBytes));

    private static bool TryGetBoundary(string? contentType, out string boundary)
    {
        boundary = "";
        if (!MediaTypeHeaderValue.TryParse(contentType, out var media) ||
            !media.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return false;
        var value = HeaderUtilities.RemoveQuotes(media.Boundary).Value;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200) return false;
        boundary = value;
        return true;
    }

    private static string? PartName(MultipartSection section) =>
        ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var cd) &&
        cd.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase)
            ? HeaderUtilities.RemoveQuotes(cd.Name).Value
            : null;

    private static string? PartFilename(MultipartSection section)
    {
        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var cd)) return null;
        var name = cd.FileNameStar.HasValue ? cd.FileNameStar.Value : HeaderUtilities.RemoveQuotes(cd.FileName).Value;
        return ReportImageRef.IsPlainName(name) ? name : null;
    }

    // null si supera el límite.
    private static async Task<byte[]?> ReadLimitedAsync(Stream stream, int limit, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > limit) return null;
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }

    // (hash, bytes escritos), o (null, n) si se pasó de remaining.
    private static async Task<(string? Hash, long Written)> WriteImageAsync(Stream source, string path,
        long remaining, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ImageBufferSize);
        try
        {
            await using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 1, FileOptions.Asynchronous);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long written = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, ImageBufferSize), ct)) > 0)
            {
                if (written + read > remaining) return (null, written + read);
                await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                sha.AppendData(buffer, 0, read);
                written += read;
            }
            return (Convert.ToHexStringLower(sha.GetHashAndReset()), written);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
