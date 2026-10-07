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

/// <summary>Respuesta de registrar/borrar: <c>{ "evidence": [ … ], "evidence_host": {…} | null }</c>.</summary>
public sealed record EvidenceResponse(List<EvidenceItem> Evidence, EvidenceHost? EvidenceHost);

/// <summary><c>POST /api/cases/{id}/generate/prepare</c>: <c>{ "hostname": "…" }</c>.</summary>
public sealed record PrepareGenerationRequest(string? Hostname = null);

/// <summary><c>{ "filename", "size", "sha256" }</c> (manifiesto sin ruta de origen).</summary>
public sealed record ManifestFileDto(string? Filename = null, long? Size = null, string? Sha256 = null);

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
