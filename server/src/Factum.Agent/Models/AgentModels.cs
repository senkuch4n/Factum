namespace Factum.Agent.Models;

public sealed class AgentOptions
{
    public int Port { get; set; } = 8765;
    public bool Mock { get; set; }
    public string DataDirectory { get; set; } = "./agent-data";
    // Dónde escucha el servidor HTTP/WebSocket: "localhost" (127.0.0.1 + ::1, default) o una IP.
    // "0.0.0.0"/"::" expone el agente a la red (sin auth): solo a propósito. CLI: --bind.
    public string BindAddress { get; set; } = "localhost";
    // Micrófono de la PC para el modo "con mic" en Windows (nombre de dshow o su alternative
    // name). null = el primero que lista Windows. Sin UI: se fija en appsettings.Local.json.
    public string? MicDevice { get; set; }
}

public sealed class Device
{
    public string Serial { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int AndroidVersion { get; set; }
    public string Imei { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string Platform { get; set; } = "android";
    public string? IosVersion { get; set; }
}

public sealed class AgentEvent
{
    public string Type { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public object? Data { get; set; }
}

// Entrada del explorador de archivos del dispositivo (Android). Path es la ruta remota completa
// (necesaria en resultados de búsqueda global, que abarcan múltiples carpetas).
public sealed class AndroidFileEntry
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime? ModifiedAt { get; set; }
}
