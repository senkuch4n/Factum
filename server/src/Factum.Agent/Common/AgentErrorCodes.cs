namespace Factum.Agent.Common;

/// <summary>
/// Códigos estables (campo <c>code</c> del JSON de error) de las rutas de evidencia y ZIP del
/// caso (zip-local-informe-servidor §6 y §8.2) y de iPhone (ios-herramientas-windows §4.1).
/// </summary>
public static class AgentErrorCodes
{
    public const string InvalidCaseId = "invalid_case_id";
    public const string InvalidFilename = "invalid_filename";
    public const string FileNotFound = "file_not_found";
    public const string FileExists = "file_exists";
    public const string FileBusy = "file_busy";
    public const string LengthRequired = "length_required";
    public const string FileTooLarge = "file_too_large";
    public const string InsufficientStorage = "insufficient_storage";
    public const string IncompleteUpload = "incomplete_upload";
    public const string StorageError = "storage_error";
    public const string EvidenceChanged = "evidence_changed";
    public const string ZipInProgress = "zip_in_progress";
    public const string ZipAlreadyCommitted = "zip_already_committed";
    public const string ZipFailed = "zip_failed";
    public const string ZipNotFound = "zip_not_found";
    public const string ZipHashMismatch = "zip_hash_mismatch";
    /// <summary>400 del ZIP: contraseña vacía o de más de 99 bytes UTF-8 (tatana-ws-y-hosts T-08/T-09).</summary>
    public const string InvalidPassword = "invalid_password";
    public const string OriginNotAllowed = "origin_not_allowed";
    /// <summary>400 de <c>PUT …/zip/seals</c> (sellado-tiempo §7.3): sello con nombre, contenido o tamaño inválido.</summary>
    public const string InvalidRequest = "invalid_request";

    // ── iPhone (ios-herramientas-windows §4.1); los textos están en Services/Ios/IosErrors.cs ──
    public const string IosAppleServiceMissing = "ios_apple_service_missing";
    public const string IosDeviceNotFound = "ios_device_not_found";
    public const string IosNotTrusted = "ios_not_trusted";
    public const string IosLocked = "ios_locked";
    public const string IosDeveloperModeDisabled = "ios_developer_mode_disabled";
    public const string IosDdiMountFailed = "ios_ddi_mount_failed";
    public const string IosTunnelFailed = "ios_tunnel_failed";
    public const string IosAdminRequired = "ios_admin_required";
    public const string IosToolsMissing = "ios_tools_missing";
    public const string IosCaptureFailed = "ios_capture_failed";
    public const string IosRecordingEmpty = "ios_recording_empty";
    public const string AirplayUnavailable = "airplay_unavailable";

    // ── Actualización de Tatana (tatana-instalador-autoupdate §5.3) ──
    /// <summary>503: Tatana está en modo mantenimiento (se va a actualizar); la request mutante no entra.</summary>
    public const string AgentUpdating = "agent_updating";
    /// <summary>409 de <c>POST /agent/maintenance</c>: hay operaciones en curso.</summary>
    public const string AgentBusy = "agent_busy";
    /// <summary>403 de <c>/agent/maintenance</c>: solo se acepta sin <c>Origin</c> ni <c>Sec-Fetch-Site</c>.</summary>
    public const string MaintenanceLocalOnly = "maintenance_local_only";

    // ── Grabación de Android y evidencia vacía (grabacion-android-robusta §5.11) ──
    /// <summary>422 de import/upload-check/upload: el archivo tiene 0 bytes y no se guarda como evidencia.</summary>
    public const string EmptyEvidence = "empty_evidence";
    /// <summary>409 del start: ya hay una grabación de Android en curso.</summary>
    public const string AlreadyRecording = "already_recording";
    /// <summary>504 del start: scrcpy no confirmó que el celular empezó a capturar a tiempo.</summary>
    public const string RecordingStartTimeout = "recording_start_timeout";
    /// <summary>422 del stop: la grabación quedó vacía (0 B) y se borró.</summary>
    public const string RecordingEmpty = "recording_empty";
    /// <summary>422 del stop: la grabación no se puede leer como video; se movió a <c>descartes/</c>.</summary>
    public const string RecordingUnreadable = "recording_unreadable";
    /// <summary>409 del stop: no hay una grabación en curso (mismo código que tatana-ws-y-hosts, T-14).</summary>
    public const string NotRecording = "not_recording";

    // ── Pantalla del celular antes de capturar (pulido-post-qa-final D9) ──
    /// <summary>409 de screenshot y record/start (pulido-post-qa-final D9): la pantalla del Android está apagada.</summary>
    public const string ScreenOff = "screen_off";
    /// <summary>409 de screenshot y record/start (pulido-post-qa-final D9): la pantalla del Android está bloqueada.</summary>
    public const string ScreenLocked = "screen_locked";

    // ── Ficha técnica del dispositivo (metadatos-dispositivo §7.2) ──
    /// <summary>404 de <c>GET /devices/{serial}/details</c> y <c>/clock</c>: <c>adb get-state</c> no dio <c>device</c>.</summary>
    public const string DeviceNotFound = "device_not_found";
}
