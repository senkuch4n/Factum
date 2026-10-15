using Factum.Backend.Models;

namespace Factum.Backend.DTOs;

// zip-local-informe-servidor §5.2-§5.6 y §8.1 (Contrato compartido). JSON en snake_case_lower
// (Program.cs). Como en CaseDtos.cs, todos los campos son anulables y SIN [Required]: la
// validación vive en Services/Cases/EvidenceManifest.cs y responde { error, code, … }.

/// <summary>PC que registra la evidencia (Tatana <c>/info</c> + carpeta del caso).</summary>
public sealed record EvidenceHostDto(
    string? Hostname = null,
    string? OsUser = null,
    string? AgentVersion = null,
    string? CaseDirectory = null);

/// <summary>Ítem del manifiesto que manda el navegador después de guardarlo en Tatana.</summary>
public sealed record EvidenceItemDto(
    string? Filename = null,
    long? Size = null,
    string? Sha256 = null,
    string? SourcePath = null);

/// <summary><c>PUT /api/cases/{id}/evidence</c>: <c>{ "host": {…}, "items": [ … ] }</c>.</summary>
public sealed record RegisterEvidenceRequest(
    EvidenceHostDto? Host = null,
    List<EvidenceItemDto>? Items = null);

/// <summary>
/// Respuesta de registrar/borrar: <c>{ "evidence": [ … ], "evidence_host": {…} | null, "timestamping_enabled",
/// "timestamp_authority", "timestamps": [ … ] }</c>. sellado-tiempo §7.2: <c>timestamps</c> trae un sello por ítem
/// del manifiesto actual que tenga sello para su sha256 actual; <c>timestamp_authority</c> = <c>authority_hint</c>.
/// </summary>
public sealed record EvidenceResponse(
    List<EvidenceItem> Evidence,
    EvidenceHost? EvidenceHost,
    bool TimestampingEnabled,
    string? TimestampAuthority,
    List<TimestampDto> Timestamps);

/// <summary><c>POST /api/cases/{id}/generate/prepare</c>: <c>{ "hostname": "…" }</c>.</summary>
public sealed record PrepareGenerationRequest(string? Hostname = null);

/// <summary>
/// <c>{ "filename", "size", "sha256", "zip_folder" }</c> (manifiesto sin ruta de origen). <c>zip_folder</c>
/// (grabacion-sesion-completa §6.5): <c>"sesion"</c> para los tramos de la grabación de la sesión, null para el resto
/// (siempre presente en el JSON). <c>EvidenceManifest.Matches</c> lo ignora.
/// </summary>
public sealed record ManifestFileDto(string? Filename = null, long? Size = null, string? Sha256 = null,
    string? ZipFolder = null);

/// <summary>Respuesta de <c>generate/prepare</c> (§5.5).</summary>
public sealed record PrepareGenerationResponse(
    string GenerationId,
    string ZipFilename,
    string CaseRef,
    /// <summary>null si <c>Report:EncryptZip=false</c>.</summary>
    string? Password,
    bool Encrypted,
    List<ManifestFileDto> Files,
    List<string> ReportImages);

/// <summary><c>{ "hostname", "directory", "path" }</c> del ZIP en la PC del perito.</summary>
public sealed record ZipLocationDto(string? Hostname = null, string? Directory = null, string? Path = null);

/// <summary>Parte <c>metadata</c> (JSON) del multipart de <c>generate/finish</c> (§5.6).</summary>
public sealed record FinishGenerationMetadata(
    string? GenerationId = null,
    string? ZipFilename = null,
    string? ZipHash = null,
    long? ZipSize = null,
    bool? Encrypted = null,
    ZipLocationDto? ZipLocation = null,
    List<ManifestFileDto>? Files = null);
