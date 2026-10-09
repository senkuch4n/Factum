using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace Factum.Backend.Models;

/// <summary>
/// Hito de cadena de custodia de un caso (trazabilidad-caso, DT1). Colección nueva
/// <c>case_events</c>, un documento por evento, append-only (el repositorio solo inserta y lista).
/// [BsonIgnoreExtraElements] + defaults tolerantes: ningún campo rompe la deserialización.
/// Los opcionales llevan [BsonIgnoreIfNull] + [JsonIgnore(WhenWritingNull)] para que solo salga lo
/// que aplica a cada tipo. Nunca se guardan datos sensibles (contraseñas, bytes de evidencia,
/// valores viejos/nuevos de los campos).
/// </summary>
[BsonIgnoreExtraElements]
public sealed class CaseEvent
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString();  // Guid del evento
    public string CaseId { get; set; } = "";       // caso al que pertenece (índice)
    public string Type { get; set; } = "";         // CaseEventTypes.*  (snake_case literal)
    public string ActorDni { get; set; } = "";
    public string ActorName { get; set; } = "";     // copiado al registrar
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;  // UTC

    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hostname { get; set; }           // solo eventos originados en Tatana
    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OsUser { get; set; }
    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AgentMode? AgentMode { get; set; }        // installed/portable; null si no vino de Tatana
    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ip { get; set; }
    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Filename { get; set; }            // captura / subida / baja
    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CaseEventDetail? Detail { get; set; }     // objeto acotado por tipo
}

/// <summary>
/// Sub-documento acotado de un <see cref="CaseEvent"/>: cada campo solo aplica a ciertos tipos y se
/// omite cuando es null (tanto en Mongo como en el JSON). Nunca lleva datos sensibles.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class CaseEventDetail
{
    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ChangedFields { get; set; }  // case_updated
    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ZipHash { get; set; }               // report_generated
    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReportHash { get; set; }            // report_generated
    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }                // report_failed (<=300 chars)
    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sha256 { get; set; }                // evidence_added
    [BsonIgnoreIfNull, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long?   Size { get; set; }                  // evidence_added
}

/// <summary>
/// Valores snake_case literales del campo <c>type</c> (DT3): idénticos en Mongo y en el JSON,
/// sin depender de ninguna política de enum. Patrón de <c>UserAdminActions</c>/<c>EvidenceStorages</c>.
/// </summary>
public static class CaseEventTypes
{
    public const string CaseCreated = "case_created";
    public const string CaseUpdated = "case_updated";
    public const string CaptureScreenshot = "capture_screenshot";
    public const string CaptureVideoStart = "capture_video_start";
    public const string CaptureVideoStop = "capture_video_stop";
    public const string CapturePhoto = "capture_photo";
    public const string EvidenceAdded = "evidence_added";
    public const string EvidenceRemoved = "evidence_removed";
    public const string ReportGenerated = "report_generated";
    public const string ReportFailed = "report_failed";
    /// <summary>DT11: preparado pero no se emite en v1.</summary>
    public const string ReportTextsUpdated = "report_texts_updated";
}
