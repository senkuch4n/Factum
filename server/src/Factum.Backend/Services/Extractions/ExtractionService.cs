using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;
using Factum.Backend.Services.Reports;
using Factum.Backend.Services.Timestamping;

namespace Factum.Backend.Services.Extractions;

/// <summary>
/// Registro de extracciones en el backend (extraccion-logica-android §8.3–8.5). Solo del dueño. No guarda contenido:
/// metadatos + hash del manifiesto. Sella el manifiesto (sin caso) cuando el hash se fija por primera vez.
/// </summary>
public interface IExtractionService
{
    Task<Result<ExtractionDto>> UpsertAsync(string id, UpsertExtractionRequest req, User officer, CancellationToken ct);
    Task<Result<List<ExtractionDto>>> ListAsync(User officer, string? caseId, bool unlinkedOnly, CancellationToken ct);
    Task<Result<ExtractionDto>> GetAsync(string id, User officer, CancellationToken ct);
    Task<Result<ExtractionDto>> LinkAsync(string id, string? caseId, User officer, CancellationToken ct);
    Task<Result<ExtractionDto>> UnlinkAsync(string id, User officer, CancellationToken ct);
    Task<Result<ExtractionTimestampFilesResponse>> TimestampFilesAsync(string id, User officer, CancellationToken ct);
}

public sealed class ExtractionService(
    IExtractionRepository repo,
    ICaseRepository cases,
    ICaseEventRepository events,
    ICaseTimestampRepository timestampRepo,
    ITimestampService timestamps,
    IReportSettings reportSettings,
    TimeProvider time,
    IHttpContextAccessor http,
    ILogger<ExtractionService> log) : IExtractionService
{
    // Códigos del contrato (§8.3/§8.5), en el campo `code` del cuerpo de error.
    public const string CodeInvalid = "invalid_extraction";
    public const string CodeImmutable = "extraction_immutable";
    public const string CodeFinished = "extraction_finished";
    public const string CodeNotFinished = "extraction_not_finished";
    public const string CodeAlreadyLinked = "extraction_already_linked";

    private DateTime Now => time.GetUtcNow().UtcDateTime;
    private string? Ip => http.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public async Task<Result<ExtractionDto>> UpsertAsync(string id, UpsertExtractionRequest req, User officer,
        CancellationToken ct)
    {
        var existing = await repo.FindByIdAsync(id, ct);
        if (existing is not null && existing.Officer.Dni != officer.Dni)
            return Result.NotFound<ExtractionDto>("Extracción no encontrada");

        var result = ExtractionRules.Apply(existing, id, req, officer, Now);
        if (result.Error != ExtractionRuleError.None)
            return RuleError(result.Error, result.Message!);

        var saved = result.Value!;
        var ok = await repo.UpsertAsync(saved, ct);
        if (!ok)
            return Result.NotFound<ExtractionDto>("Extracción no encontrada"); // _id de otro dueño

        // Sella el manifiesto (sin caso) la primera vez que se fija el hash. Best-effort: nunca lanza.
        CaseTimestamp? sello = null;
        if (result.ManifestJustSet && timestamps.IsEnabled)
            sello = await timestamps.SealExtractionManifestAsync(saved, timestamps.GenerationBudget);
        sello ??= await SafeFindSelloAsync(saved);

        return Result.Ok(await ToDtoAsync(saved, sello, ct));
    }

    public async Task<Result<List<ExtractionDto>>> ListAsync(User officer, string? caseId, bool unlinkedOnly,
        CancellationToken ct)
    {
        if (caseId is not null)
        {
            var cas = await cases.FindByIdAsync(caseId, ct);
            if (CaseAccess.Check(cas, officer.Dni) != CaseAccessResult.Ok)
                return Result.NotFound<List<ExtractionDto>>("Caso no encontrado");
        }

        var list = await repo.ListByOfficerAsync(officer.Dni, caseId, unlinkedOnly, ct);
        var refs = new Dictionary<string, string?>(StringComparer.Ordinal);
        var dtos = new List<ExtractionDto>(list.Count);
        foreach (var e in list)
        {
            var caseRef = await ResolveCaseRefAsync(e.CaseId, officer.Dni, refs, ct);
            var sello = await SafeFindSelloAsync(e);
            dtos.Add(ExtractionDto.From(e, caseRef, DtoOf(sello)));
        }
        return Result.Ok(dtos);
    }

    public async Task<Result<ExtractionDto>> GetAsync(string id, User officer, CancellationToken ct)
    {
        var e = await repo.FindByIdAsync(id, ct);
        if (e is null || e.Officer.Dni != officer.Dni)
            return Result.NotFound<ExtractionDto>("Extracción no encontrada");
        return Result.Ok(await ToDtoAsync(e, await SafeFindSelloAsync(e), ct));
    }

    public async Task<Result<ExtractionDto>> LinkAsync(string id, string? caseId, User officer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(caseId))
            return Result.Invalid<ExtractionDto>("Falta el caso.");

        var e = await repo.FindByIdAsync(id, ct);
        if (e is null || e.Officer.Dni != officer.Dni)
            return Result.NotFound<ExtractionDto>("Extracción no encontrada");
        if (!ExtractionStatuses.IsTerminal(e.Status))
            return Conflict(CodeNotFinished, "La extracción no terminó.");

        var cas = await cases.FindByIdAsync(caseId, ct);
        if (CaseAccess.Check(cas, officer.Dni) != CaseAccessResult.Ok)
            return Result.NotFound<ExtractionDto>("Caso no encontrado");

        if (e.CaseId == caseId) // idempotente, sin evento
            return Result.Ok(await ToDtoAsync(e, await SafeFindSelloAsync(e), ct));
        if (e.CaseId is not null)
            return Conflict(CodeAlreadyLinked, "La extracción ya está asociada a otro caso.");

        var updated = await repo.SetCaseAsync(id, officer.Dni, caseId, Now, ct);
        if (updated is null) return Result.NotFound<ExtractionDto>("Extracción no encontrada");

        await RecordCaseEventAsync(cas!, CaseEventTypes.ExtractionLinked, updated);
        return Result.Ok(await ToDtoAsync(updated, await SafeFindSelloAsync(updated), ct));
    }

    public async Task<Result<ExtractionDto>> UnlinkAsync(string id, User officer, CancellationToken ct)
    {
        var e = await repo.FindByIdAsync(id, ct);
        if (e is null || e.Officer.Dni != officer.Dni)
            return Result.NotFound<ExtractionDto>("Extracción no encontrada");

        if (e.CaseId is null) // idempotente, sin evento
            return Result.Ok(await ToDtoAsync(e, await SafeFindSelloAsync(e), ct));

        var previousCaseId = e.CaseId;
        var updated = await repo.SetCaseAsync(id, officer.Dni, null, null, ct);
        if (updated is null) return Result.NotFound<ExtractionDto>("Extracción no encontrada");

        // El evento se registra en el caso del que se desasoció (ya no en updated.CaseId, que quedó null).
        var cas = await cases.FindByIdAsync(previousCaseId, ct);
        if (cas is not null && cas.Officer.Dni == officer.Dni)
            await RecordCaseEventAsync(cas, CaseEventTypes.ExtractionUnlinked, updated);
        return Result.Ok(await ToDtoAsync(updated, await SafeFindSelloAsync(updated), ct));
    }

    public async Task<Result<ExtractionTimestampFilesResponse>> TimestampFilesAsync(string id, User officer,
        CancellationToken ct)
    {
        var e = await repo.FindByIdAsync(id, ct);
        if (e is null || e.Officer.Dni != officer.Dni)
            return Result.NotFound<ExtractionTimestampFilesResponse>("Extracción no encontrada");

        CaseTimestamp? sello = null;
        try { sello = await timestampRepo.FindByExtractionAsync(id, includeTsr: true, ct); }
        catch (Exception ex) { log.LogWarning(ex, "Extracción {Id}: no se pudo leer el sello", id); }

        var files = sello is { Status: TimestampStatuses.Granted, Tsr: { } tsr }
            ? [new TimestampFileDto(TimestampBundle.TsrName(sello), Convert.ToBase64String(tsr))]
            : new List<TimestampFileDto>();
        var readme = TimestampReadme.BuildForExtraction(id, Now, reportSettings.Zone, sello);
        return Result.Ok(new ExtractionTimestampFilesResponse(files, readme));
    }

    // ── helpers ──

    private async Task<ExtractionDto> ToDtoAsync(Extraction e, CaseTimestamp? sello, CancellationToken ct)
    {
        string? caseRef = null;
        if (e.CaseId is not null)
        {
            var cas = await cases.FindByIdAsync(e.CaseId, ct);
            if (cas is not null && cas.Officer.Dni == e.Officer.Dni) caseRef = cas.NroReferencia;
        }
        return ExtractionDto.From(e, caseRef, DtoOf(sello));
    }

    private TimestampDto? DtoOf(CaseTimestamp? sello) =>
        sello is null ? null : TimestampDto.From(sello, reportSettings.Zone);

    private async Task<CaseTimestamp?> SafeFindSelloAsync(Extraction e)
    {
        if (!timestamps.IsEnabled && e.ManifestSha256 is null) return null;
        try { return await timestampRepo.FindByExtractionAsync(e.Id, includeTsr: false, CancellationToken.None); }
        catch (Exception ex) { log.LogWarning(ex, "Extracción {Id}: no se pudo leer el sello", e.Id); return null; }
    }

    private async Task<string?> ResolveCaseRefAsync(string? caseId, string dni, Dictionary<string, string?> cache,
        CancellationToken ct)
    {
        if (caseId is null) return null;
        if (cache.TryGetValue(caseId, out var cached)) return cached;
        var cas = await cases.FindByIdAsync(caseId, ct);
        var caseRef = cas is not null && cas.Officer.Dni == dni ? cas.NroReferencia : null;
        cache[caseId] = caseRef;
        return caseRef;
    }

    private async Task RecordCaseEventAsync(Case cas, string type, Extraction ext)
    {
        var label = $"{ext.Device.Manufacturer} {ext.Device.Model}".Trim();
        var evt = new CaseEvent
        {
            CaseId = cas.Id,
            Type = type,
            ActorDni = cas.Officer.Dni,
            ActorName = cas.Officer.Name,
            Timestamp = Now,
            Ip = Ip,
            Detail = new CaseEventDetail
            {
                ExtractionId = ext.Id,
                DeviceLabel = string.IsNullOrEmpty(label) ? null : label,
                Sha256 = ext.ManifestSha256,
            },
        };
        try { await events.InsertAsync(evt, CancellationToken.None); }
        catch (Exception ex) { log.LogWarning(ex, "No se pudo registrar el evento {Type} del caso {CaseId}", type, cas.Id); }
    }

    private static Result<ExtractionDto> RuleError(ExtractionRuleError error, string message) => error switch
    {
        ExtractionRuleError.Immutable => Conflict(CodeImmutable, message),
        ExtractionRuleError.Finished => Conflict(CodeFinished, message),
        _ => Result.Fail<ExtractionDto>(ErrorKind.Validation, message,
            new Dictionary<string, object?> { ["code"] = CodeInvalid }),
    };

    private static Result<ExtractionDto> Conflict(string code, string message) =>
        Result.Fail<ExtractionDto>(ErrorKind.Conflict, message, new Dictionary<string, object?> { ["code"] = code });
}
