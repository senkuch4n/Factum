using MongoDB.Bson.Serialization.Attributes;

namespace Factum.Backend.Models;

public enum AgentMode { Installed, Portable }

// Se guarda como int en Mongo: los valores nuevos van SIEMPRE al final (CaptureFailed = 5).
public enum AgentAction { Startup, CaptureStart, CaptureStop, Screenshot, Webcam, CaptureFailed }

// [BsonIgnoreExtraElements]: un binario anterior (rollback) no rompe al leer documentos con campos nuevos.
[BsonIgnoreExtraElements]
public sealed class AgentEvent
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Dni { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string OsUser { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
    public AgentMode Mode { get; set; } = AgentMode.Installed;
    public string Ip { get; set; } = string.Empty;
    public string? CaseId { get; set; }
    public AgentAction Action { get; set; } = AgentAction.Startup;

    /// <summary>Solo con <c>CaptureFailed</c>: qué acción falló (wizard-concurrencia-pestanas DT11).</summary>
    [BsonIgnoreIfNull]
    public AgentAction? FailedAction { get; set; }
    /// <summary>Solo con <c>CaptureFailed</c>: motivo que vio el perito (trim, ≤ 300).</summary>
    [BsonIgnoreIfNull]
    public string? Reason { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
