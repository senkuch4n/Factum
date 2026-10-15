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

    /// <summary>
    /// Hosts EXTRA que puede traer el header <c>Host</c> (tatana-ws-y-hosts §3.1/§4.3), además de <c>localhost</c>,
    /// <c>127.0.0.1</c>, <c>[::1]</c> y la IP de <see cref="BindAddress"/> (o las de la PC si es 0.0.0.0/::).
    /// Nombre o IP, sin esquema, puerto, path ni <c>*</c>. Inválido en appsettings/env/CLI → no arranca; en la
    /// config local → se ignora el archivo entero. Ver <c>Common.HostPolicy</c>.
    /// </summary>
    public string[] AllowedHosts { get; set; } = [];

    /// <summary>Tope de una subida del navegador a la carpeta del caso. Default 16 GiB.</summary>
    public long MaxUploadBytes { get; set; } = 16L * 1024 * 1024 * 1024;

    /// <summary>Espacio libre que tiene que quedar después de una subida o del ZIP. Default 1 GiB.</summary>
    public long MinFreeBytes { get; set; } = 1L * 1024 * 1024 * 1024;

    // ── ios-grabacion-larga (§5.1) ───────────────────────────────────────────

    /// <summary>Post-proceso de grabaciones iOS (variantes de previsualización). Ver <see cref="VideoPostprocessOptions"/>.</summary>
    public VideoPostprocessOptions VideoPostprocess { get; set; } = new();

    /// <summary>
    /// Solo con <see cref="Mock"/> = true: MP4 que se copia como "grabación" iOS al detener (para probar el
    /// post-proceso sin iPhone, DA9). No va en appsettings.json: variable de entorno, CLI o appsettings.Local.json.
    /// </summary>
    public string? MockIosRecordingSource { get; set; }

    // ── grabacion-android-robusta (§5.1) ─────────────────────────────────────

    /// <summary>Grabación de Android con scrcpy (arranque, cierre y validación). Ver <see cref="RecordingOptions"/>.</summary>
    public RecordingOptions Recording { get; set; } = new();

    // ── grabacion-sesion-completa (§5.14) ────────────────────────────────────

    /// <summary>Grabación de la sesión (tramos, espacio, cortes). Ver <see cref="SessionOptions"/>.</summary>
    public SessionOptions Session { get; set; } = new();

    /// <summary>
    /// Solo con <see cref="Mock"/> = true: video que se copia como "grabación" Android al detener. No va en
    /// appsettings.json: variable de entorno, CLI o appsettings.Local.json.
    /// </summary>
    public string? MockAndroidRecordingSource { get; set; }

    /// <summary>
    /// Solo con <see cref="Mock"/> = true: fuerza el resultado de la validación para probar la UI:
    /// <c>"ok"</c> | <c>"incomplete"</c> | <c>"empty"</c> | <c>"unreadable"</c>. Solo por env/CLI/appsettings.Local.json.
    /// </summary>
    public string? MockAndroidRecordingOutcome { get; set; }

    /// <summary>
    /// Solo con <see cref="Mock"/> = true: estado que devuelve el chequeo de pantalla antes de capturar
    /// (pulido-post-qa-final D9): <c>"on"</c> (default) | <c>"off"</c> | <c>"locked"</c> | <c>"unknown"</c>.
    /// No va en appsettings.json: variable de entorno, CLI o appsettings.Local.json.
    /// </summary>
    public string? MockAndroidScreenState { get; set; }

    /// <summary>
    /// Solo con <see cref="Mock"/> = true: diferencia (segundos, dispositivo − PC) que informa el reloj de la ficha
    /// técnica simulada (metadatos-dispositivo §4.7), para probar el aviso de umbral sin celular. Default 0. No va en
    /// appsettings.json: variable de entorno (<c>Agent__MockDeviceClockSkewSeconds</c>), CLI o appsettings.Local.json.
    /// </summary>
    public int MockDeviceClockSkewSeconds { get; set; }

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

/// <summary>
/// Config <c>Agent:VideoPostprocess:*</c> (ios-grabacion-larga §5.1). Un valor inválido no impide arrancar:
/// <see cref="Normalize"/> loguea un warning y vuelve al default de ese campo (DA6).
/// </summary>
public sealed class VideoPostprocessOptions
{
    public const int DefaultBlendMaxSourceSeconds = 0;
    public const int DefaultRifeMaxSourceSeconds = 120;
    public const double DefaultTimeoutFactor = 3.0;
    public const int DefaultTimeoutExtraSeconds = 120;
    public const int DefaultTimeoutMinSeconds = 300;
    public const int DefaultTimeoutMaxSeconds = 7200;
    public const int DefaultProgressIntervalSeconds = 3;

    /// <summary>false = sin variantes (como AirPlay): <c>variants: []</c>.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Umbral de duración del original para "Suavizado". 0 = sin límite.</summary>
    public int BlendMaxSourceSeconds { get; set; } = DefaultBlendMaxSourceSeconds;
    /// <summary>Umbral de duración del original para "RIFE". 0 = sin límite (DA3).</summary>
    public int RifeMaxSourceSeconds { get; set; } = DefaultRifeMaxSourceSeconds;
    /// <summary>Plazo = factor × duración + extra, acotado entre mínimo y máximo.</summary>
    public double TimeoutFactor { get; set; } = DefaultTimeoutFactor;
    public int TimeoutExtraSeconds { get; set; } = DefaultTimeoutExtraSeconds;
    public int TimeoutMinSeconds { get; set; } = DefaultTimeoutMinSeconds;
    /// <summary>También es el plazo si no se conoce la duración (DA7).</summary>
    public int TimeoutMaxSeconds { get; set; } = DefaultTimeoutMaxSeconds;
    /// <summary>Intervalo mínimo entre eventos de progreso.</summary>
    public int ProgressIntervalSeconds { get; set; } = DefaultProgressIntervalSeconds;

    /// <summary>Corrige, campo por campo, los valores inválidos al default (con un warning). Devuelve this.</summary>
    public VideoPostprocessOptions Normalize(ILogger? log)
    {
        void Warn(string field, object value, object fallback) =>
            log?.LogWarning("Agent:VideoPostprocess:{Field} inválido ({Value}); se usa {Default}", field, value, fallback);

        if (BlendMaxSourceSeconds < 0)
        {
            Warn(nameof(BlendMaxSourceSeconds), BlendMaxSourceSeconds, DefaultBlendMaxSourceSeconds);
            BlendMaxSourceSeconds = DefaultBlendMaxSourceSeconds;
        }
        if (RifeMaxSourceSeconds < 0)
        {
            Warn(nameof(RifeMaxSourceSeconds), RifeMaxSourceSeconds, DefaultRifeMaxSourceSeconds);
            RifeMaxSourceSeconds = DefaultRifeMaxSourceSeconds;
        }
        if (!(TimeoutFactor > 0) || double.IsInfinity(TimeoutFactor))
        {
            Warn(nameof(TimeoutFactor), TimeoutFactor, DefaultTimeoutFactor);
            TimeoutFactor = DefaultTimeoutFactor;
        }
        if (TimeoutExtraSeconds < 0)
        {
            Warn(nameof(TimeoutExtraSeconds), TimeoutExtraSeconds, DefaultTimeoutExtraSeconds);
            TimeoutExtraSeconds = DefaultTimeoutExtraSeconds;
        }
        if (TimeoutMinSeconds < 0)
        {
            Warn(nameof(TimeoutMinSeconds), TimeoutMinSeconds, DefaultTimeoutMinSeconds);
            TimeoutMinSeconds = DefaultTimeoutMinSeconds;
        }
        if (TimeoutMaxSeconds <= 0)
        {
            Warn(nameof(TimeoutMaxSeconds), TimeoutMaxSeconds, DefaultTimeoutMaxSeconds);
            TimeoutMaxSeconds = DefaultTimeoutMaxSeconds;
        }
        if (TimeoutMinSeconds > TimeoutMaxSeconds)
        {
            // Los dos vuelven al default: no se sabe cuál de los dos está mal.
            log?.LogWarning(
                "Agent:VideoPostprocess:TimeoutMinSeconds ({Min}) mayor que TimeoutMaxSeconds ({Max}); se usan {DMin} y {DMax}",
                TimeoutMinSeconds, TimeoutMaxSeconds, DefaultTimeoutMinSeconds, DefaultTimeoutMaxSeconds);
            TimeoutMinSeconds = DefaultTimeoutMinSeconds;
            TimeoutMaxSeconds = DefaultTimeoutMaxSeconds;
        }
        if (ProgressIntervalSeconds < 1)
        {
            Warn(nameof(ProgressIntervalSeconds), ProgressIntervalSeconds, DefaultProgressIntervalSeconds);
            ProgressIntervalSeconds = DefaultProgressIntervalSeconds;
        }
        return this;
    }

    /// <summary>Copia (Normalize no toca las opciones compartidas de IOptions).</summary>
    public VideoPostprocessOptions Clone() => (VideoPostprocessOptions)MemberwiseClone();
}

/// <summary>
/// Config <c>Agent:Recording:*</c> (grabacion-android-robusta §5.1). Un valor inválido no impide arrancar:
/// <see cref="Normalize"/> loguea un warning y vuelve al default de ese campo.
/// </summary>
public sealed class RecordingOptions
{
    public const int DefaultStartTimeoutSeconds = 20;
    public const int DefaultStopTimeoutSeconds = 30;
    public const int DefaultSigtermAfterSeconds = 3;
    public const double DefaultToleranceMinSeconds = 3;
    public const double DefaultTolerancePercent = 2;
    public const string WindowModeNoWindow = "no_window";
    public const string WindowModeNoVideoPlayback = "no_video_playback";

    /// <summary>Tiempo máximo para ver el marcador de inicio de scrcpy.</summary>
    public int StartTimeoutSeconds { get; set; } = DefaultStartTimeoutSeconds;
    /// <summary>false = criterio viejo: vivo a los 1,5 s = grabando (inicio = lanzamiento + 1 s). Salida de emergencia.</summary>
    public bool RequireStartMarker { get; set; } = true;
    /// <summary>Espera total desde la señal hasta el kill (Duda 2 A).</summary>
    public int StopTimeoutSeconds { get; set; } = DefaultStopTimeoutSeconds;
    /// <summary>Solo macOS/Linux: SIGTERM si scrcpy sigue vivo este tiempo después de la SIGINT. 0 = sin escalado.</summary>
    public int SigtermAfterSeconds { get; set; } = DefaultSigtermAfterSeconds;
    /// <summary>Agrega <c>--stay-awake</c>.</summary>
    public bool StayAwake { get; set; } = true;
    /// <summary><c>"no_window"</c> → <c>--no-window</c>; <c>"no_video_playback"</c> → <c>--no-video-playback</c> (lo anterior).</summary>
    public string WindowMode { get; set; } = WindowModeNoWindow;
    /// <summary>Tolerancia = max(min, grabado × porcentaje / 100) (Duda 3 A).</summary>
    public double ToleranceMinSeconds { get; set; } = DefaultToleranceMinSeconds;
    public double TolerancePercent { get; set; } = DefaultTolerancePercent;

    /// <summary>Corrige, campo por campo, los valores inválidos al default (con un warning). Devuelve this.</summary>
    public RecordingOptions Normalize(ILogger? log)
    {
        void Warn(string field, object? value, object fallback) =>
            log?.LogWarning("Agent:Recording:{Field} inválido ({Value}); se usa {Default}", field, value, fallback);

        if (StartTimeoutSeconds <= 0)
        {
            Warn(nameof(StartTimeoutSeconds), StartTimeoutSeconds, DefaultStartTimeoutSeconds);
            StartTimeoutSeconds = DefaultStartTimeoutSeconds;
        }
        if (StopTimeoutSeconds <= 0)
        {
            Warn(nameof(StopTimeoutSeconds), StopTimeoutSeconds, DefaultStopTimeoutSeconds);
            StopTimeoutSeconds = DefaultStopTimeoutSeconds;
        }
        if (SigtermAfterSeconds < 0 || SigtermAfterSeconds >= StopTimeoutSeconds)
        {
            // Con el default de StopTimeoutSeconds (30) el default de SigtermAfterSeconds (3) siempre entra.
            var fallback = DefaultSigtermAfterSeconds < StopTimeoutSeconds ? DefaultSigtermAfterSeconds : 0;
            Warn(nameof(SigtermAfterSeconds), SigtermAfterSeconds, fallback);
            SigtermAfterSeconds = fallback;
        }
        if (!(ToleranceMinSeconds > 0) || double.IsInfinity(ToleranceMinSeconds))
        {
            Warn(nameof(ToleranceMinSeconds), ToleranceMinSeconds, DefaultToleranceMinSeconds);
            ToleranceMinSeconds = DefaultToleranceMinSeconds;
        }
        if (double.IsNaN(TolerancePercent) || TolerancePercent < 0 || TolerancePercent > 50)
        {
            Warn(nameof(TolerancePercent), TolerancePercent, DefaultTolerancePercent);
            TolerancePercent = DefaultTolerancePercent;
        }
        var mode = WindowMode?.Trim().ToLowerInvariant();
        if (mode is not (WindowModeNoWindow or WindowModeNoVideoPlayback))
        {
            Warn(nameof(WindowMode), WindowMode, WindowModeNoWindow);
            mode = WindowModeNoWindow;
        }
        WindowMode = mode;
        return this;
    }

    /// <summary>Copia (Normalize no toca las opciones compartidas de IOptions).</summary>
    public RecordingOptions Clone() => (RecordingOptions)MemberwiseClone();
}

/// <summary>
/// Config <c>Agent:Session:*</c> (grabacion-sesion-completa §5.14): parámetros por PC de la grabación de la sesión. El
/// modo y la duración del tramo son de la instalación (backend) y llegan en el start. Un valor inválido no impide
/// arrancar: <see cref="Normalize"/> loguea un warning y vuelve al default de ese campo.
/// </summary>
public sealed class SessionOptions
{
    public const long DefaultScreenBytesPerMinute = 30L * 1024 * 1024;
    public const long DefaultCameraBytesPerMinute = 12L * 1024 * 1024;
    public const int DefaultMinSegmentMinutes = 5;
    public const int DefaultMaxSegmentMinutes = 120;
    public const int DefaultDiskCheckSeconds = 60;
    public const int DefaultWarnMinutesLeft = 60;
    public const int DefaultCutReserveMinutes = 2;
    public const int DefaultResumeDelaySeconds = 3;
    public const int DefaultScreenProbeSeconds = 30;
    public const int DefaultRotationRetrySeconds = 60;
    public const int DefaultShutdownBudgetSeconds = 20;
    public const int DefaultRotationConfirmMilliseconds = 1500;
    public const int DefaultRotationStartAttempts = 3;
    public const int DefaultCameraWidth = 1280;
    public const int DefaultCameraHeight = 720;
    public const int DefaultCameraFps = 15;
    public const int DefaultCameraCrf = 26;

    /// <summary>Tasa mínima supuesta de la pantalla (bytes/min) para estimar el espacio.</summary>
    public long ScreenBytesPerMinute { get; set; } = DefaultScreenBytesPerMinute;
    /// <summary>Tasa mínima supuesta de la cámara (F2).</summary>
    public long CameraBytesPerMinute { get; set; } = DefaultCameraBytesPerMinute;
    /// <summary>Rango aceptado de <c>segment_minutes</c> del start (fuera de rango se acota).</summary>
    public int MinSegmentMinutes { get; set; } = DefaultMinSegmentMinutes;
    public int MaxSegmentMinutes { get; set; } = DefaultMaxSegmentMinutes;
    /// <summary>Chequeo de espacio y latido del diario.</summary>
    public int DiskCheckSeconds { get; set; } = DefaultDiskCheckSeconds;
    /// <summary>Aviso <c>disk_low</c> por debajo de estos minutos de grabación.</summary>
    public int WarnMinutesLeft { get; set; } = DefaultWarnMinutesLeft;
    /// <summary>Corte limpio <c>sin_espacio</c> cuando quedan menos de estos minutos (sobre <c>MinFreeBytes</c>).</summary>
    public int CutReserveMinutes { get; set; } = DefaultCutReserveMinutes;
    /// <summary>Espera antes de reanudar tras la reconexión o una salida de scrcpy con el celular conectado.</summary>
    public int ResumeDelaySeconds { get; set; } = DefaultResumeDelaySeconds;
    /// <summary>Chequeo de pantalla apagada/bloqueada durante la sesión. 0 = sin chequeo.</summary>
    public int ScreenProbeSeconds { get; set; } = DefaultScreenProbeSeconds;
    /// <summary>Reintento de un corte que no pudo abrir el tramo nuevo.</summary>
    public int RotationRetrySeconds { get; set; } = DefaultRotationRetrySeconds;
    /// <summary>
    /// Corte solapado: después del marcador, el scrcpy nuevo tiene que seguir vivo este tiempo antes de cerrar el
    /// anterior (post-review #80: en el SM-M127F un scrcpy nuevo llegó a caerse 0,5–0,7 s después del marcador).
    /// 0–10000 ms.
    /// </summary>
    public int RotationConfirmMilliseconds { get; set; } = DefaultRotationConfirmMilliseconds;
    /// <summary>Intentos de arrancar el tramo nuevo en un corte, sin cortar el anterior (1–5).</summary>
    public int RotationStartAttempts { get; set; } = DefaultRotationStartAttempts;
    /// <summary>Presupuesto de <c>prepare-shutdown</c> para cerrar y guardar el tramo en curso.</summary>
    public int ShutdownBudgetSeconds { get; set; } = DefaultShutdownBudgetSeconds;
    /// <summary>Cámara del procedimiento (F2).</summary>
    public int CameraWidth { get; set; } = DefaultCameraWidth;
    public int CameraHeight { get; set; } = DefaultCameraHeight;
    public int CameraFps { get; set; } = DefaultCameraFps;
    public int CameraCrf { get; set; } = DefaultCameraCrf;
    /// <summary>Solo mock o desarrollo: duración del tramo en segundos (0 = usar los minutos del start).</summary>
    public int MockSegmentSeconds { get; set; }
    /// <summary>Solo mock: espacio libre simulado (para ver los avisos sin llenar el disco).</summary>
    public long? MockFreeBytes { get; set; }

    /// <summary>Corrige, campo por campo, los valores inválidos al default (con un warning). Devuelve this.</summary>
    public SessionOptions Normalize(ILogger? log)
    {
        void Warn(string field, object? value, object fallback) =>
            log?.LogWarning("Agent:Session:{Field} inválido ({Value}); se usa {Default}", field, value, fallback);

        if (ScreenBytesPerMinute <= 0)
        {
            Warn(nameof(ScreenBytesPerMinute), ScreenBytesPerMinute, DefaultScreenBytesPerMinute);
            ScreenBytesPerMinute = DefaultScreenBytesPerMinute;
        }
        if (CameraBytesPerMinute <= 0)
        {
            Warn(nameof(CameraBytesPerMinute), CameraBytesPerMinute, DefaultCameraBytesPerMinute);
            CameraBytesPerMinute = DefaultCameraBytesPerMinute;
        }
        if (MinSegmentMinutes < 1 || MaxSegmentMinutes < MinSegmentMinutes)
        {
            log?.LogWarning(
                "Agent:Session:MinSegmentMinutes ({Min}) / MaxSegmentMinutes ({Max}) inválidos; se usan {DMin} y {DMax}",
                MinSegmentMinutes, MaxSegmentMinutes, DefaultMinSegmentMinutes, DefaultMaxSegmentMinutes);
            MinSegmentMinutes = DefaultMinSegmentMinutes;
            MaxSegmentMinutes = DefaultMaxSegmentMinutes;
        }
        if (DiskCheckSeconds is < 10 or > 600)
        {
            Warn(nameof(DiskCheckSeconds), DiskCheckSeconds, DefaultDiskCheckSeconds);
            DiskCheckSeconds = DefaultDiskCheckSeconds;
        }
        if (WarnMinutesLeft < 0)
        {
            Warn(nameof(WarnMinutesLeft), WarnMinutesLeft, DefaultWarnMinutesLeft);
            WarnMinutesLeft = DefaultWarnMinutesLeft;
        }
        if (CutReserveMinutes < 1)
        {
            Warn(nameof(CutReserveMinutes), CutReserveMinutes, DefaultCutReserveMinutes);
            CutReserveMinutes = DefaultCutReserveMinutes;
        }
        if (ResumeDelaySeconds is < 0 or > 60)
        {
            Warn(nameof(ResumeDelaySeconds), ResumeDelaySeconds, DefaultResumeDelaySeconds);
            ResumeDelaySeconds = DefaultResumeDelaySeconds;
        }
        if (ScreenProbeSeconds != 0 && ScreenProbeSeconds is < 10 or > 600)
        {
            Warn(nameof(ScreenProbeSeconds), ScreenProbeSeconds, DefaultScreenProbeSeconds);
            ScreenProbeSeconds = DefaultScreenProbeSeconds;
        }
        if (RotationRetrySeconds is < 10 or > 600)
        {
            Warn(nameof(RotationRetrySeconds), RotationRetrySeconds, DefaultRotationRetrySeconds);
            RotationRetrySeconds = DefaultRotationRetrySeconds;
        }
        if (RotationConfirmMilliseconds is < 0 or > 10000)
        {
            Warn(nameof(RotationConfirmMilliseconds), RotationConfirmMilliseconds, DefaultRotationConfirmMilliseconds);
            RotationConfirmMilliseconds = DefaultRotationConfirmMilliseconds;
        }
        if (RotationStartAttempts is < 1 or > 5)
        {
            Warn(nameof(RotationStartAttempts), RotationStartAttempts, DefaultRotationStartAttempts);
            RotationStartAttempts = DefaultRotationStartAttempts;
        }
        if (ShutdownBudgetSeconds is < 5 or > 120)
        {
            Warn(nameof(ShutdownBudgetSeconds), ShutdownBudgetSeconds, DefaultShutdownBudgetSeconds);
            ShutdownBudgetSeconds = DefaultShutdownBudgetSeconds;
        }
        if (CameraWidth is < 160 or > 3840)
        {
            Warn(nameof(CameraWidth), CameraWidth, DefaultCameraWidth);
            CameraWidth = DefaultCameraWidth;
        }
        if (CameraHeight is < 120 or > 2160)
        {
            Warn(nameof(CameraHeight), CameraHeight, DefaultCameraHeight);
            CameraHeight = DefaultCameraHeight;
        }
        if (CameraFps is < 1 or > 60)
        {
            Warn(nameof(CameraFps), CameraFps, DefaultCameraFps);
            CameraFps = DefaultCameraFps;
        }
        if (CameraCrf is < 0 or > 51)
        {
            Warn(nameof(CameraCrf), CameraCrf, DefaultCameraCrf);
            CameraCrf = DefaultCameraCrf;
        }
        if (MockSegmentSeconds != 0 && MockSegmentSeconds is < 10 or > 3600)
        {
            Warn(nameof(MockSegmentSeconds), MockSegmentSeconds, 0);
            MockSegmentSeconds = 0;
        }
        if (MockFreeBytes is < 0)
        {
            Warn(nameof(MockFreeBytes), MockFreeBytes, "null");
            MockFreeBytes = null;
        }
        return this;
    }

    /// <summary>Copia (Normalize no toca las opciones compartidas de IOptions).</summary>
    public SessionOptions Clone() => (SessionOptions)MemberwiseClone();
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
