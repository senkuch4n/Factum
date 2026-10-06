using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace Factum.Backend.Models;

// [BsonIgnoreExtraElements] en todas las clases persistidas: tolera campos que este binario no
// conoce (rollback del backend, campos futuros). Los defaults no anulables ("" / [] / 0) hacen
// que un documento previo al informe pericial deserialice sin errores (no hay migración).
[BsonIgnoreExtraElements]
public sealed class Case
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>0 = caso previo al informe pericial (falta el campo); 1 = caso pericial.</summary>
    public int SchemaVersion { get; set; }

    // Semántica pericial (D5/D15, sin renombrar): NroReferencia = número de causa,
    // NombreDenunciante = titular del dispositivo, DniDenunciante = DNI del titular (opcional).
    public string NroReferencia { get; set; } = string.Empty;
    public string NombreDenunciante { get; set; } = string.Empty;
    public string DniDenunciante { get; set; } = string.Empty;
    /// <summary>Ya no se pide; se conserva para los casos viejos que lo tengan.</summary>
    public string Observaciones { get; set; } = string.Empty;

    public User Officer { get; set; } = new();
    public DeviceInfo Device { get; set; } = new();

    /// <summary>Copia del perfil del perito, tomada al crear/editar el caso.</summary>
    public PeritoSnapshot? Perito { get; set; }

    // Datos de la causa (planos, como NroReferencia).
    public string NombreTribunal { get; set; } = string.Empty;
    public string OrganismoTribunal { get; set; } = string.Empty;
    public string SalaTribunal { get; set; } = string.Empty;
    public string IntegrantesTribunal { get; set; } = string.Empty;
    /// <summary>
    /// Integrantes como lista ordenada (formulario-caso-catalogos). null = caso guardado antes de
    /// esta HU (o por un cliente que no manda la lista): vale solo IntegrantesTribunal.
    /// Cuando no es null, IntegrantesTribunal es la frase derivada (IntegrantesFormatter.Join).
    /// Anulable y sin default: un documento viejo sin el campo deserializa null (no hay migración).
    /// </summary>
    public List<string>? Integrantes { get; set; }
    public string TipoCausa { get; set; } = string.Empty;
    public string Caratula { get; set; } = string.Empty;
    public string ParteDenunciante { get; set; } = string.Empty;
    public string ParteDenunciada { get; set; } = string.Empty;
    public string ObjetoCausa { get; set; } = string.Empty;
    public string AmbitoCausa { get; set; } = string.Empty;
    /// <summary>"yyyy-MM-dd" o "".</summary>
    public string FechaIntervencion { get; set; } = string.Empty;
    public string NombreProponente { get; set; } = string.Empty;
    public string ProfesionProponente { get; set; } = string.Empty;
    public string MatriculaProponente { get; set; } = string.Empty;
    public string TipoDispositivo { get; set; } = string.Empty;
    public string LineaDispositivo { get; set; } = string.Empty;

    /// <summary>Textos del paso Informe. El listado los proyecta afuera (null).</summary>
    public ReportTexts? ReportTexts { get; set; }

    /// <summary>Marcas de las capturas ("imei_modelo" / "nombre_dispositivo").</summary>
    public List<CaptureRole> CaptureRoles { get; set; } = [];

    public CaseStatus Status { get; set; } = CaseStatus.Draft;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? GeneratedAt { get; set; }

    /// <summary>
    /// Contraseña del ZIP cifrado (en claro en Mongo, D3-B); null si el ZIP no se cifró.
    /// [JsonIgnore]: no sale en ningún JSON de Case; se recupera solo por
    /// GET /api/cases/{id}/zip-password.
    /// </summary>
    [JsonIgnore]
    public string? ZipPassword { get; set; }
    /// <summary>
    /// true si el ZIP de este caso se escribió cifrado. Los casos previos a
    /// zip-cifrado-real no tienen el campo y deserializan false ("sin cifrar").
    /// </summary>
    public bool ZipEncrypted { get; set; }
    /// <summary>Algoritmo del cifrado ("aes256-ae2") o null si no se cifró.</summary>
    public string? ZipEncryption { get; set; }
    public string? ZipHash { get; set; }
    public string? ZipFilename { get; set; }
    public string? PdfFilename { get; set; }
    /// <summary>SHA-256 del DOCX generado (el DOCX ya no va dentro del ZIP).</summary>
    public string? ReportHash { get; set; }

    // Ruta de origen en el dispositivo para archivos traídos con el explorador de archivos
    // (no todos los archivos tienen una — screenshots/grabaciones/fotos de webcam no aplican).
    public List<FileSource> FileSources { get; set; } = [];

    // ── zip-local-informe-servidor (§3.1) ────────────────────────────────────
    // Todo anulable o con default []: un documento previo deserializa sin migración y nada de
    // esto se escribe salvo por una acción del flujo nuevo sobre el propio caso.

    /// <summary>
    /// Marca de flujo del caso (D8): <c>"agent"</c> (evidencia en la PC del perito) o
    /// <c>"server"</c> (flujo viejo). En Mongo puede faltar; el servicio la completa en memoria
    /// con <c>EvidenceManifest.ResolveStorage</c> antes de devolver el caso, así el JSON trae
    /// siempre el valor resuelto.
    /// </summary>
    [BsonIgnoreIfNull]
    public string? EvidenceStorage { get; set; }

    /// <summary>Manifiesto del flujo nuevo: archivos guardados en Tatana (ningún byte en el servidor).</summary>
    public List<EvidenceItem> Evidence { get; set; } = [];

    /// <summary>PC donde está la evidencia (D7). null mientras el manifiesto esté vacío.</summary>
    public EvidenceHost? EvidenceHost { get; set; }

    /// <summary>Dónde quedó el ZIP en la PC del perito (D6/D7). null en el flujo viejo.</summary>
    public ZipLocation? ZipLocation { get; set; }

    /// <summary>
    /// Intento de generación abierto (§5.5/§5.6). Lleva la contraseña del ZIP: nunca sale en un
    /// JSON.
    /// </summary>
    [JsonIgnore]
    public PendingGeneration? PendingGeneration { get; set; }
}

public static class EvidenceStorages
{
    public const string Agent = "agent";
    public const string Server = "server";
}

[BsonIgnoreExtraElements]
public sealed class EvidenceItem
{
    public string Filename { get; set; } = string.Empty;
    public long Size { get; set; }
    /// <summary>SHA-256, 64 hex en minúscula.</summary>
    public string Sha256 { get; set; } = string.Empty;
    /// <summary>Ruta en el celular (solo explorador de archivos).</summary>
    public string? SourcePath { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public sealed class EvidenceHost
{
    /// <summary>Tatana <c>/info.hostname</c> (Environment.MachineName).</summary>
    public string Hostname { get; set; } = string.Empty;
    public string OsUser { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
    /// <summary>Ruta absoluta de la carpeta de trabajo del caso en esa PC.</summary>
    public string CaseDirectory { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public sealed class ZipLocation
{
    public string Hostname { get; set; } = string.Empty;
    /// <summary>Carpeta final del ZIP en esa PC.</summary>
    public string Directory { get; set; } = string.Empty;
    /// <summary>Ruta absoluta del ZIP.</summary>
    public string Path { get; set; } = string.Empty;
}

[BsonIgnoreExtraElements]
public sealed class PendingGeneration
{
    /// <summary>Guid "N".</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>null si Report:EncryptZip=false.</summary>
    public string? Password { get; set; }
    public string ZipFilename { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public sealed class FileSource
{
    public string Filename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
}

/// <summary>Copia congelada del perfil del perito dentro del caso.</summary>
[BsonIgnoreExtraElements]
public sealed class PeritoSnapshot
{
    public string Nombre { get; set; } = string.Empty;
    public string Matricula { get; set; } = string.Empty;
    public string Profesion { get; set; } = string.Empty;
    public string Caracter { get; set; } = string.Empty;
    /// <summary>"suscripto" o "suscripta".</summary>
    public string Tratamiento { get; set; } = ExpertProfile.TratamientoSuscripto;

    [JsonIgnore, BsonIgnore]
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Nombre) && !string.IsNullOrWhiteSpace(Matricula) &&
        !string.IsNullOrWhiteSpace(Profesion) && !string.IsNullOrWhiteSpace(Caracter);
}

[BsonIgnoreExtraElements]
public sealed class ReportTexts
{
    public string ObjetoInforme { get; set; } = string.Empty;
    public string OperacionesRealizadas { get; set; } = string.Empty;
    public string AseguramientoEvidencia { get; set; } = string.Empty;
    public string Resultados { get; set; } = string.Empty;
    public string ValoracionTecnica { get; set; } = string.Empty;
    public string Conclusiones { get; set; } = string.Empty;
    public string NotasTecnicas { get; set; } = string.Empty;
    public string Reserva { get; set; } = string.Empty;
    /// <summary>
    /// Formato de los ocho textos (editor-texto-enriquecido). null = texto plano (casos guardados
    /// antes de esta HU o por un cliente viejo): se generan con ReplaceParagraphPerLine, como siempre.
    /// "markdown" = dialecto Factum (ReportMarkdown). Sin default: un documento viejo sin el campo
    /// deserializa null. No hay migración.
    /// </summary>
    [BsonIgnoreIfNull]
    public string? Formato { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public sealed class CaptureRole
{
    public const string ImeiModelo = "imei_modelo";
    public const string NombreDispositivo = "nombre_dispositivo";

    public string Filename { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    public static bool IsValid(string? role) => role is ImeiModelo or NombreDispositivo;
}

public enum CaseStatus { Draft, Generating, Completed, Error }
