namespace Factum.Agent.Models;

/// <summary>Operador informado por la web (va al ACTA; el registro con autoridad es el del backend).</summary>
public sealed record ExtractionOperatorDto(string? Dni = null, string? Name = null);

/// <summary>Cuerpo de <c>POST /extractions</c> (extraccion-logica-android §8.2.4).</summary>
public sealed record ExtractionStartRequest(
    string? Serial = null,
    List<string>? StructuredCategories = null,
    List<string>? FileCategories = null,
    List<string>? CustomPaths = null,
    string? DestinationPath = null,
    bool? VerifyOnDevice = null,
    bool? ConfirmDeviceModifications = null,
    ExtractionOperatorDto? Operator = null,
    AgentPcServerProbe? PcServerProbe = null,
    string? OriginId = null,
    string? ClientVersion = null);

/// <summary>Cuerpo de <c>POST /extractions/{id}/registered</c>.</summary>
public sealed record ExtractionRegisteredRequest(string? ManifestSha256 = null);

/// <summary>Cuerpo de <c>POST /devices/{serial}/extraction/estimate</c>.</summary>
public sealed record ExtractionEstimateRequest(List<string>? FileCategories = null, List<string>? CustomPaths = null);

/// <summary>Un sello a copiar a <c>sellos/</c> (igual que SealFileItem del ZIP del caso).</summary>
public sealed record ExtractionSealItem(string? Name = null, string? ContentBase64 = null);

/// <summary>Cuerpo de <c>PUT /extractions/{id}/seals</c>.</summary>
public sealed record ExtractionSealsRequest(List<ExtractionSealItem>? Files = null, string? Readme = null);

/// <summary>
/// Cuerpo de <c>PUT /extractions/{id}/report</c> (informe-extraccion §8.3): el DOCX del informe en base64. El
/// <c>Filename</c> es informativo; Tatana escribe siempre <c>INFORME.docx</c> + <c>INFORME.docx.sha256</c> en la raíz
/// de la carpeta de la extracción (U1).
/// </summary>
public sealed record ExtractionReportRequest(string? ContentBase64 = null, string? Filename = null);

// extraccion-logica-android §8.2/§8.3 (Contrato compartido, lado Tatana). JSON en snake_case_lower
// (JsonStringEnumConverter(SnakeCaseLower) + WhenWritingNull en Program.cs del agente). Del lado web:
// client/src/lib/agent.ts (AgentExtraction*). El `record` (AgentExtractionRecord) usa EXACTAMENTE los nombres del
// DTO de la API (UpsertExtractionRequest): la web lo reenvía sin tocarlo a PUT /api/extractions/{id} (D-T2), y el
// fixture compartido server/tests/Fixtures/extraction-record.json prueba el contrato en los dos proyectos .NET.

/// <summary>Reloj crudo dispositivo ↔ PC (mismas horas que manda la ficha #79).</summary>
public sealed record AgentDeviceClock(
    DateTimeOffset? PcBefore = null,
    DateTimeOffset? PcAfter = null,
    DateTimeOffset? DeviceTime = null,
    int? ResolutionMs = null,
    string? Reason = null);

/// <summary>Sondeo NTP del navegador contra la API.</summary>
public sealed record AgentPcServerProbe(
    DateTimeOffset? PcBefore = null,
    DateTimeOffset? ServerTime = null,
    DateTimeOffset? PcAfter = null);

public sealed record AgentExtractionDevice(
    string? Serial = null,
    string? Manufacturer = null,
    string? Model = null,
    string? Platform = null,
    string? AndroidVersion = null,
    int? SdkInt = null);

/// <summary>Un dato de la ficha: value XOR reason.</summary>
public sealed record AgentFichaField(string? Value = null, string? Reason = null);

public sealed record AgentExtractionDeviceDetails(
    string? Platform = null,
    string? AgentVersion = null,
    string? ConnectionId = null,
    Dictionary<string, AgentFichaField>? Fields = null,
    AgentDeviceClock? ClockStart = null,
    AgentDeviceClock? ClockClose = null,
    bool? IncludeSsidInReport = null);

public sealed record AgentExtractionCategory(
    string? Key = null,
    string? Kind = null,
    string? Status = null,
    long? Count = null,
    int? Files = null,
    long? Bytes = null,
    int? Errors = null,
    int? HashMismatches = null,
    string? ReasonCode = null,
    string? Method = null);

public sealed record AgentExtractionTotals(
    int? Files = null,
    long? Bytes = null,
    int? Errors = null,
    int? HashMismatches = null);

public sealed record AgentDeviceModification(
    string? Kind = null,
    DateTimeOffset? At = null,
    string? Detail = null,
    bool? AfterClose = null);

public sealed record AgentExtractionInterruption(
    string? ReasonCode = null,
    string? Message = null,
    DateTimeOffset? At = null);

/// <summary>
/// El <c>record</c> que Tatana arma y la web reenvía sin tocar a <c>PUT /api/extractions/{id}</c> (§8.3). Los
/// nombres coinciden con <c>UpsertExtractionRequest</c> de la API.
/// </summary>
public sealed record AgentExtractionRecord(
    string? Status = null,
    string? Hostname = null,
    string? OsUser = null,
    string? AgentVersion = null,
    string? AgentMode = null,
    DateTimeOffset? StartedAtPc = null,
    DateTimeOffset? FinishedAtPc = null,
    AgentExtractionDevice? Device = null,
    AgentExtractionDeviceDetails? DeviceDetails = null,
    List<AgentExtractionCategory>? Categories = null,
    int? CustomPathsCount = null,
    bool? VerifyOnDevice = null,
    bool? DeviceSha256Available = null,
    AgentExtractionTotals? Totals = null,
    string? DestinationPath = null,
    string? FolderName = null,
    string? ManifestSha256 = null,
    int? ManifestEntries = null,
    List<AgentDeviceModification>? DeviceModifications = null,
    AgentExtractionInterruption? Interruption = null,
    AgentPcServerProbe? PcServerProbe = null);
