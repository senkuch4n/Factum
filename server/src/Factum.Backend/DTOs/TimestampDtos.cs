using Factum.Backend.Models;
using Factum.Backend.Services.Timestamping;

namespace Factum.Backend.DTOs;

// sellado-tiempo §7.2 (Contrato compartido). JSON en snake_case_lower (Program.cs); los null salen como null.
// Del lado del cliente: client/src/lib/api.ts (CaseTimestamp, EvidenceTimestampRow, CaseTimestampsResponse,
// TimestampFilesResponse, TimestampFileItem).

/// <summary>Un sello (en TS: <c>CaseTimestamp</c>). Nunca lleva el <c>.tsr</c>.</summary>
public sealed record TimestampDto(
    string Id,
    string Target,
    string Filename,
    string Sha256,
    string Status,
    int Attempts,
    DateTime RequestedAt,
    DateTime? NextAttemptAt,
    string? LastError,
    string? Authority,
    string? TsaUrl,
    DateTime? GenTime,
    string? GenTimeDisplay,
    string? SerialNumber,
    string? PolicyOid,
    DateTime? GrantedAt,
    bool? InReport)
{
    /// <summary>
    /// Único mapeo <see cref="CaseTimestamp"/> → DTO. <c>gen_time_display</c> sale formateada en la zona del
    /// informe (<c>Report:TimeZone</c>) con <see cref="TimestampFormat.Display"/>, igual que en el DOCX.
    /// </summary>
    public static TimestampDto From(CaseTimestamp t, TimeZoneInfo zone) => new(
        t.Id, t.Target, t.Filename, t.Sha256, t.Status, t.Attempts,
        Utc(t.CreatedAt),
        t.Status == TimestampStatuses.Pending ? Utc(t.NextAttemptAt) : null,
        t.LastError,
        t.Authority,
        t.TsaUrl,
        Utc(t.GenTime),
        t.GenTime is { } g ? TimestampFormat.Display(g, zone) : null,
        t.SerialNumber,
        t.PolicyOid,
        Utc(t.GrantedAt),
        t.Target == TimestampTargets.Zip ? t.InReport ?? (t.Status == TimestampStatuses.Granted ? false : null) : null);

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? d) => d is { } v ? Utc(v) : null;
}

/// <summary>Fila de evidencia del manifiesto actual con su sello (o null).</summary>
public sealed record EvidenceTimestampRow(string Filename, string Sha256, TimestampDto? Timestamp);

/// <summary><c>GET /api/cases/{id}/timestamps</c>.</summary>
public sealed record CaseTimestampsResponse(
    /// <summary>Config ACTUAL del backend.</summary>
    bool Enabled,
    string? AuthorityHint,
    /// <summary><c>Case.TimestampMode</c>: "enabled" | "disabled" | null.</summary>
    string? CaseMode,
    /// <summary>"agent" | "server".</summary>
    string? EvidenceStorage,
    TimestampDto? Zip,
    TimestampDto? Report,
    List<EvidenceTimestampRow> Evidence,
    int EvidenceSealed,
    int EvidenceTotal);

/// <summary>Un <c>.tsr</c> para copiar a <c>sellos/</c> (Tatana).</summary>
public sealed record TimestampFileDto(string Name, string ContentBase64);

/// <summary><c>GET /api/cases/{id}/timestamps/files</c>: lo que el navegador le pasa a Tatana.</summary>
public sealed record TimestampFilesResponse(
    string CaseRef,
    /// <summary>null si el caso es un borrador.</summary>
    string? ZipFilename,
    List<TimestampFileDto> Files,
    /// <summary>Texto de <c>LEEME-verificacion.txt</c> (CRLF).</summary>
    string Readme);
