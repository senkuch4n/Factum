using Factum.Backend.Models;

namespace Factum.Backend.DTOs;

// extraccion-logica-android §8.3–8.5 (Contrato compartido). JSON en snake_case_lower (Program.cs); salvo donde se
// marque, los null salen como null. Del lado del cliente: client/src/lib/api.ts (Extraction*, UpsertExtractionRequest).

// ── Sub-DTOs compartidos entre el record de entrada y el ExtractionDto de salida ──

public sealed record ExtractionDeviceDto(
    string? Serial = null,
    string? Manufacturer = null,
    string? Model = null,
    string? Platform = null,
    string? AndroidVersion = null,
    int? SdkInt = null)
{
    public static ExtractionDeviceDto From(ExtractionDevice d) =>
        new(d.Serial, d.Manufacturer, d.Model, d.Platform, d.AndroidVersion, d.SdkInt);
}

public sealed record ExtractionCategoryDto(
    string? Key = null,
    string? Kind = null,
    string? Status = null,
    long? Count = null,
    int? Files = null,
    long? Bytes = null,
    int? Errors = null,
    int? HashMismatches = null,
    string? ReasonCode = null,
    string? Method = null)
{
    public static ExtractionCategoryDto From(ExtractionCategory c) =>
        new(c.Key, c.Kind, c.Status, c.Count, c.Files, c.Bytes, c.Errors, c.HashMismatches, c.ReasonCode, c.Method);
}

public sealed record ExtractionTotalsDto(
    int? Files = null,
    long? Bytes = null,
    int? Errors = null,
    int? HashMismatches = null)
{
    public static ExtractionTotalsDto From(ExtractionTotals t) =>
        new(t.Files, t.Bytes, t.Errors, t.HashMismatches);
}

public sealed record DeviceModificationDto(
    string? Kind = null,
    DateTimeOffset? At = null,
    string? Detail = null,
    bool? AfterClose = null)
{
    public static DeviceModificationDto From(DeviceModification m) =>
        new(m.Kind, DateTime.SpecifyKind(m.At, DateTimeKind.Utc), m.Detail, m.AfterClose);
}

public sealed record ExtractionInterruptionDto(
    string? ReasonCode = null,
    string? Message = null,
    DateTimeOffset? At = null)
{
    public static ExtractionInterruptionDto From(ExtractionInterruption i) =>
        new(i.ReasonCode, i.Message, DateTime.SpecifyKind(i.At, DateTimeKind.Utc));
}

/// <summary>§8.3 <c>device_details</c>: misma forma que el de #79, pero con dos relojes (inicio y cierre).</summary>
public sealed record ExtractionDeviceDetailsDto(
    string? Platform = null,
    string? AgentVersion = null,
    string? ConnectionId = null,
    Dictionary<string, DeviceDetailFieldDto>? Fields = null,
    DeviceClockDto? ClockStart = null,
    DeviceClockDto? ClockClose = null,
    bool? IncludeSsidInReport = null);

// ── Entrada: PUT /api/extractions/{id} (§8.3). Todo anulable, como CaseDtos. ──

public sealed record UpsertExtractionRequest(
    string? Status = null,
    string? Hostname = null,
    string? OsUser = null,
    string? AgentVersion = null,
    string? AgentMode = null,
    DateTimeOffset? StartedAtPc = null,
    DateTimeOffset? FinishedAtPc = null,
    ExtractionDeviceDto? Device = null,
    ExtractionDeviceDetailsDto? DeviceDetails = null,
    List<ExtractionCategoryDto>? Categories = null,
    int? CustomPathsCount = null,
    bool? VerifyOnDevice = null,
    bool? DeviceSha256Available = null,
    ExtractionTotalsDto? Totals = null,
    string? DestinationPath = null,
    string? FolderName = null,
    string? ManifestSha256 = null,
    int? ManifestEntries = null,
    List<DeviceModificationDto>? DeviceModifications = null,
    ExtractionInterruptionDto? Interruption = null,
    PcServerProbeDto? PcServerProbe = null);

// ── Salida: ExtractionDto (§8.4). device_details y officer salen con su forma de modelo (JSON attrs). ──

public sealed record ExtractionDto(
    string Id,
    string Status,
    User Officer,
    string Hostname,
    string OsUser,
    string AgentVersion,
    string? AgentMode,
    DateTime StartedAtPc,
    DateTime? FinishedAtPc,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? FinalRegisteredAt,
    long? PcMinusServerMs,
    long? PcServerMarginMs,
    ExtractionDeviceDto Device,
    DeviceDetails? DeviceDetails,
    List<ExtractionCategoryDto> Categories,
    int CustomPathsCount,
    bool VerifyOnDevice,
    bool DeviceSha256Available,
    ExtractionTotalsDto Totals,
    string DestinationPath,
    string FolderName,
    string? ManifestSha256,
    int? ManifestEntries,
    List<DeviceModificationDto> DeviceModifications,
    ExtractionInterruptionDto? Interruption,
    string? CaseId,
    string? CaseRef,
    DateTime? LinkedAt,
    TimestampDto? ManifestTimestamp)
{
    public static ExtractionDto From(Extraction e, string? caseRef, TimestampDto? manifestTimestamp)
    {
        static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
        static DateTime? UtcN(DateTime? d) => d is { } v ? Utc(v) : null;
        return new ExtractionDto(
            e.Id, e.Status, e.Officer, e.Hostname, e.OsUser, e.AgentVersion,
            e.AgentMode?.ToString().ToLowerInvariant(),
            Utc(e.StartedAtPc), UtcN(e.FinishedAtPc), Utc(e.CreatedAt), Utc(e.UpdatedAt), UtcN(e.FinalRegisteredAt),
            e.PcMinusServerMs, e.PcServerMarginMs,
            ExtractionDeviceDto.From(e.Device),
            e.DeviceDetails,
            e.Categories.Select(ExtractionCategoryDto.From).ToList(),
            e.CustomPathsCount, e.VerifyOnDevice, e.DeviceSha256Available,
            ExtractionTotalsDto.From(e.Totals),
            e.DestinationPath, e.FolderName, e.ManifestSha256, e.ManifestEntries,
            e.DeviceModifications.Select(DeviceModificationDto.From).ToList(),
            e.Interruption is { } i ? ExtractionInterruptionDto.From(i) : null,
            e.CaseId, caseRef, UtcN(e.LinkedAt),
            manifestTimestamp);
    }
}

public sealed record ExtractionListResponse(List<ExtractionDto> Extractions);

/// <summary><c>PUT /api/extractions/{id}/case</c>.</summary>
public sealed record LinkExtractionRequest(string? CaseId = null);

/// <summary><c>GET /api/extractions/{id}/timestamp/files</c> (§8.5): lo que el navegador le pasa a Tatana.</summary>
public sealed record ExtractionTimestampFilesResponse(List<TimestampFileDto> Files, string Readme);
