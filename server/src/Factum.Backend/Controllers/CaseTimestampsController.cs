using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;
using Factum.Backend.Services.Reports;
using Factum.Backend.Services.Timestamping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Backend.Controllers;

/// <summary>
/// Sellos de tiempo de un caso (sellado-tiempo §6.6). Solo el dueño: un caso ajeno o inexistente responde
/// <c>404 { "error": "Caso no encontrado" }</c>, como el resto. Funciona en cualquier estado y flujo, y aunque el
/// sellado esté desactivado hoy (los sellos ya obtenidos siguen visibles y descargables).
/// </summary>
[ApiController]
[Route("api/cases/{id}/timestamps")]
[Authorize]
public sealed class CaseTimestampsController(ICaseRepository cases, IStorageService storage,
    ITimestampService timestamps, IReportSettings reportSettings, TimeProvider time,
    ILogger<CaseTimestampsController> log) : ControllerBase
{
    public const string NoSealsMessage = "El caso no tiene sellos de tiempo";

    private User Officer => (User)HttpContext.Items["User"]!;

    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType<CaseTimestampsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var cas = await LoadOwnedAsync(id, ct);
        if (cas is null) return NotFoundCase();

        var all = await timestamps.ListAsync(cas.Id, ct);
        var zone = reportSettings.Zone;
        TimestampDto? Dto(CaseTimestamp? t) => t is null ? null : TimestampDto.From(t, zone);

        var zip = string.IsNullOrEmpty(cas.ZipHash) ? null
            : all.FirstOrDefault(t => t.Target == TimestampTargets.Zip && t.Sha256 == cas.ZipHash);
        var report = string.IsNullOrEmpty(cas.ReportHash) ? null
            : all.FirstOrDefault(t => t.Target == TimestampTargets.Report && t.Sha256 == cas.ReportHash);
        var rows = cas.Evidence
            .OrderBy(e => e.Filename, StringComparer.Ordinal)
            .Select(e => new EvidenceTimestampRow(e.Filename, e.Sha256, Dto(all.FirstOrDefault(t =>
                t.Target == TimestampTargets.Evidence && t.Filename == e.Filename && t.Sha256 == e.Sha256))))
            .ToList();

        return Ok(new CaseTimestampsResponse(
            timestamps.IsEnabled,
            timestamps.AuthorityHint,
            cas.TimestampMode,
            cas.EvidenceStorage,
            Dto(zip),
            Dto(report),
            rows,
            rows.Count(r => r.Timestamp?.Status == TimestampStatuses.Granted),
            rows.Count));
    }

    [HttpGet("files")]
    [Produces("application/json")]
    [ProducesResponseType<TimestampFilesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Files(string id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var cas = await LoadOwnedAsync(id, ct);
        if (cas is null) return NotFoundCase();

        var (selection, readme) = await SelectAsync(cas, ct);
        var files = selection.Sealed
            .Where(t => t.Tsr is not null)
            .Select(t => new TimestampFileDto(TimestampBundle.TsrName(t), Convert.ToBase64String(t.Tsr!)))
            .ToList();
        return Ok(new TimestampFilesResponse(cas.NroReferencia, ZipFilenameOf(cas), files, readme));
    }

    // Como las descargas de hoy, acepta ?token= (FactumBearerHandler).
    [HttpGet("bundle")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Bundle(string id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var cas = await LoadOwnedAsync(id, ct);
        if (cas is null) return NotFoundCase();

        var (selection, readme) = await SelectAsync(cas, ct);
        var sealedItems = selection.Sealed.Where(t => t.Tsr is not null).ToList();
        if (sealedItems.Count == 0) return NotFound(new { error = NoSealsMessage });

        var stem = ReportService.StemFromZip(cas.ZipFilename ?? "", cas);
        var bytes = TimestampBundle.BuildZip(sealedItems, readme);
        return File(bytes, "application/zip", $"sellos_{stem}.zip");
    }

    private async Task<(TimestampSelection Selection, string Readme)> SelectAsync(Case cas, CancellationToken ct)
    {
        var all = await timestamps.ListAsync(cas.Id, ct, includeTsr: true);
        var selection = TimestampBundle.Select(cas, all);
        var readme = TimestampReadme.Build(cas.NroReferencia, time.GetUtcNow().UtcDateTime, reportSettings.Zone,
            ZipFilenameOf(cas), string.IsNullOrEmpty(cas.PdfFilename) ? null : cas.PdfFilename, selection);
        return (selection, readme);
    }

    private static string? ZipFilenameOf(Case cas) => string.IsNullOrEmpty(cas.ZipFilename) ? null : cas.ZipFilename;

    private IActionResult NotFoundCase() => NotFound(new { error = "Caso no encontrado" });

    // Dueño o null (api-hardening §4.7: ajeno = inexistente, y queda en el log). Con EvidenceStorage resuelto.
    private async Task<Case?> LoadOwnedAsync(string id, CancellationToken ct)
    {
        var cas = await cases.FindByIdAsync(id, ct);
        switch (CaseAccess.Check(cas, Officer.Dni))
        {
            case CaseAccessResult.Ok:
                cas!.EvidenceStorage = EvidenceManifest.ResolveStorage(cas, storage.HasEvidenceFiles);
                return cas;
            case CaseAccessResult.Foreign:
                log.LogWarning("Acceso a caso ajeno: {Dni} pidió {CaseId}", Officer.Dni, id);
                return null;
            default:
                return null;
        }
    }
}
