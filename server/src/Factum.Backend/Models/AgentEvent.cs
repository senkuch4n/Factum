using MongoDB.Bson.Serialization.Attributes;

namespace Factum.Backend.Models;

public enum AgentMode { Installed, Portable }

public enum AgentAction { Startup, CaptureStart, CaptureStop, Screenshot, Webcam }

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

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
