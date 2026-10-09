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

    /// <summary>
    /// Cómo se instaló Tatana: <c>"installed"</c> (instalador NSIS; Electron pasa <c>--mode installed</c>)
    /// o <c>"portable"</c>. null = heurística de <c>tools/</c> junto al exe (tatana-instalador-autoupdate
    /// D-T15). Solo por CLI (<c>--mode</c>); otro valor → Tatana no arranca.
    /// </summary>
    public string? InstallMode { get; set; }

    public static readonly string[] InstallModes = ["installed", "portable"];

    // ── zip-local-informe-servidor (§4.2) ────────────────────────────────────

    /// <summary>
    /// Carpeta base del ZIP de cada caso. null/vacío = default por SO (<see cref="DefaultEvidenceDirectory"/>):
    /// <c>C:\Factum\Evidencia</c> en Windows y <c>~/Factum/Evidencia</c> en macOS/Linux (nunca Documentos).
    /// </summary>
    public string? EvidenceDirectory { get; set; }

    /// <summary>Orígenes del front que pueden llamar a Tatana. Vacío = <c>OriginPolicy.Defaults</c>.</summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>Tope de una subida del navegador a la carpeta del caso. Default 16 GiB.</summary>
    public long MaxUploadBytes { get; set; } = 16L * 1024 * 1024 * 1024;

    /// <summary>Espacio libre que tiene que quedar después de una subida o del ZIP. Default 1 GiB.</summary>
    public long MinFreeBytes { get; set; } = 1L * 1024 * 1024 * 1024;

    /// <summary>Ruta absoluta de la carpeta base del ZIP (config o default por SO).</summary>
    public string ResolveEvidenceDirectory() =>
        ResolveEvidenceDirectory(EvidenceDirectory, DataDirectory, OperatingSystem.IsWindows(),
            Environment.GetEnvironmentVariable("SystemDrive"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>Puro (testeable): una ruta configurada gana; si no, el default de DP4.</summary>
    internal static string ResolveEvidenceDirectory(string? configured, string dataDirectory, bool isWindows,
        string? systemDrive, string? userProfile)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured.Trim());
        return DefaultEvidenceDirectory(dataDirectory, isWindows, systemDrive, userProfile);
    }

    /// <summary>
    /// DP4: Windows → <c>&lt;SystemDrive&gt;\Factum\Evidencia</c> (C: si falta); macOS/Linux →
    /// <c>~/Factum/Evidencia</c>, o <c>&lt;DataDirectory&gt;/evidencia</c> si no hay perfil de usuario.
    /// </summary>
    internal static string DefaultEvidenceDirectory(string dataDirectory, bool isWindows, string? systemDrive,
        string? userProfile)
    {
        if (isWindows)
        {
            var drive = string.IsNullOrWhiteSpace(systemDrive) ? "C:" : systemDrive.Trim().TrimEnd('\\', '/');
            // Se arma con '\\' a mano: en un test que corre en macOS, Path.Combine usaría '/'.
            return drive + "\\Factum\\Evidencia";
        }
        if (string.IsNullOrWhiteSpace(userProfile))
            return Path.GetFullPath(Path.Combine(dataDirectory, "evidencia"));
        return Path.Combine(userProfile, "Factum", "Evidencia");
    }
}

public sealed class Device
{
    /// <summary>
    /// Centinela que Tatana pone en <see cref="Imei"/> cuando no pudo leer el IMEI del dispositivo.
    /// El front y el backend ya lo reconocen: el front muestra el campo manual de IMEI. Debe valer
    /// exactamente <c>INGRESAR_MANUALMENTE</c>.
    /// </summary>
    public const string ImeiManualEntry = "INGRESAR_MANUALMENTE";

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
