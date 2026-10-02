using Factum.Backend.Models;

namespace Factum.Backend.DTOs;

// JSON en snake_case_lower (Program.cs). Todos los campos de texto son string? y SIN
// [Required]: con <Nullable>enable</Nullable> + [ApiController], un string no anulable es
// obligatorio implícito y responde un ProblemDetails que el cliente no lee. La validación de
// obligatorios vive en Services/Cases/CaseValidation.cs y responde { error, missing }.

public sealed record CreateCaseRequest(
    string? NroReferencia = null,
    string? NombreDenunciante = null,
    string? DniDenunciante = null,
    string? Observaciones = null,
    string? NombreTribunal = null,
    string? OrganismoTribunal = null,
    string? SalaTribunal = null,
    string? IntegrantesTribunal = null,
    string? TipoCausa = null,
    string? Caratula = null,
    string? ParteDenunciante = null,
    string? ParteDenunciada = null,
    string? ObjetoCausa = null,
    string? AmbitoCausa = null,
    string? FechaIntervencion = null,
    string? NombreProponente = null,
    string? ProfesionProponente = null,
    string? MatriculaProponente = null,
    string? TipoDispositivo = null,
    string? LineaDispositivo = null,
    DeviceInfoDto? Device = null,
    // Lista ordenada de integrantes (formulario-caso-catalogos). Si viene (no null), el servidor
    // deriva integrantes_tribunal de la lista e ignora el que mande el request.
    List<string>? Integrantes = null
);

/// <summary>PUT /api/cases/{id}: los campos de la causa sin <c>device</c>, más <c>imei</c>.</summary>
public sealed record UpdateCaseRequest(
    string? NroReferencia = null,
    string? NombreDenunciante = null,
    string? DniDenunciante = null,
    string? Observaciones = null,
    string? NombreTribunal = null,
    string? OrganismoTribunal = null,
    string? SalaTribunal = null,
    string? IntegrantesTribunal = null,
    string? TipoCausa = null,
    string? Caratula = null,
    string? ParteDenunciante = null,
    string? ParteDenunciada = null,
    string? ObjetoCausa = null,
    string? AmbitoCausa = null,
    string? FechaIntervencion = null,
    string? NombreProponente = null,
    string? ProfesionProponente = null,
    string? MatriculaProponente = null,
    string? TipoDispositivo = null,
    string? LineaDispositivo = null,
    string? Imei = null,
    // Ídem CreateCaseRequest. Ausente o null = cliente viejo: vale integrantes_tribunal y se
    // hace $unset de Integrantes.
    List<string>? Integrantes = null
);

public sealed record DeviceInfoDto(
    string Serial,
    string Manufacturer,
    string Model,
    int AndroidVersion,
    string Imei,
    string Platform = "android",
    string OsVersion = "",
    // JSON "name" (string, default ""). Anulable en C# para que no sea obligatorio implícito:
    // un cliente viejo que no lo manda sigue funcionando.
    string? Name = null
);

/// <summary>Textos del paso Informe (PUT …/report-texts y GET …/report-texts/defaults).</summary>
public sealed record ReportTextsDto(
    string? ObjetoInforme = null,
    string? OperacionesRealizadas = null,
    string? AseguramientoEvidencia = null,
    string? Resultados = null,
    string? ValoracionTecnica = null,
    string? Conclusiones = null,
    string? NotasTecnicas = null,
    string? Reserva = null,
    // editor-texto-enriquecido: "markdown" (cliente nuevo) o null/""/"texto" (texto plano).
    // En la respuesta de defaults es siempre "markdown".
    string? Formato = null
);

public sealed record CaptureRoleDto(string? Filename = null, string? Role = null);

public sealed record CaptureRolesRequest(List<CaptureRoleDto>? CaptureRoles = null);

public sealed record CaptureRolesResponse(List<CaptureRole> CaptureRoles);

public sealed record FileInfoDto(
    string Name,
    long Size,
    string Hash,
    DateTime ModifiedAt,
    string? SourcePath = null
);

/// <summary>Respuesta de <c>GET /api/cases/{id}/zip-password</c>: <c>{ "password": "…" }</c>.</summary>
public sealed record ZipPasswordResponse(string Password);

public sealed record GenerateResponse(
    Case Case,
    string ZipHash,
    /// <summary>Contraseña del ZIP (única vez que viaja junto al caso); null si no se cifró.</summary>
    string? Password,
    FilesDto Files,
    string ReportHash
);

public sealed record FilesDto(string Zip, string Pdf);
