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

    // ── Grabación de la sesión (grabacion-sesion-completa §7.7) ──
    /// <summary>409: hay una sesión activa en este Tatana (otro caso o celular), o en <c>record/start</c> del mismo celular.</summary>
    public const string SessionActive = "session_active";
    /// <summary>409 de <c>…/session/marks</c>: no hay una sesión grabando para ese caso.</summary>
    public const string SessionNotActive = "session_not_active";
    /// <summary>404 de <c>GET /cases/{id}/session</c>: el caso no tiene diario en esta PC.</summary>
    public const string SessionNotFound = "session_not_found";
    /// <summary>409 de <c>DELETE /cases/{id}/files/{name}</c>: los tramos de la sesión no se borran.</summary>
    public const string SessionFileProtected = "session_file_protected";
    /// <summary>400 del start: plataforma sin soporte de sesión en esta versión (iPhone en F1).</summary>
    public const string SessionPlatformUnsupported = "session_platform_unsupported";
    /// <summary>400 del start (F2): en iPhone la sesión usa solo <c>video_only</c>.</summary>
    public const string SessionIosModeUnsupported = "session_ios_mode_unsupported";

    // ── Cámara del procedimiento (grabacion-sesion-completa-f2 §7.6) ──
    /// <summary>400 de <c>/cameras*</c> y <c>…/session/camera/*</c>: la cámara no está habilitada en este Tatana.</summary>
    public const string SessionCameraUnsupported = "session_camera_unsupported";
    /// <summary>409: la cámara de la sesión está grabando (otra cámara pedida, o la vista previa).</summary>
    public const string CameraBusy = "camera_busy";
    /// <summary>404 de <c>GET /cameras/preview.jpg</c>: todavía no hay imagen.</summary>
    public const string CameraPreviewUnavailable = "camera_preview_unavailable";

    // ── Extracción lógica de Android (extraccion-logica-android §8.6). Se reusan invalid_request,
    //    insufficient_storage y device_not_found, que ya existen arriba. ──
    /// <summary>409 de <c>POST /extractions</c>: ya hay una extracción en curso (+ extraction_id).</summary>
    public const string ExtractionInProgress = "extraction_in_progress";
    /// <summary>404: la extracción no existe en este Tatana.</summary>
    public const string ExtractionNotFound = "extraction_not_found";
    /// <summary>409 de <c>/cancel</c>: la extracción no está corriendo.</summary>
    public const string ExtractionNotRunning = "extraction_not_running";
    /// <summary>409 de <c>/registered</c>: la extracción no está en un estado terminal.</summary>
    public const string ExtractionNotFinished = "extraction_not_finished";
    /// <summary>409 de <c>/registered</c>: el manifest_sha256 no coincide con el de Tatana.</summary>
    public const string ManifestMismatch = "manifest_mismatch";
    /// <summary>400 de <c>POST /extractions</c>: falta confirmar las modificaciones al dispositivo.</summary>
    public const string ConfirmationRequired = "confirmation_required";
    /// <summary>409: el mismo serial está grabando o tiene una sesión activa.</summary>
    public const string DeviceBusy = "device_busy";
    /// <summary>409: <c>adb get-state</c> no dio <c>device</c> (autorización revocada).</summary>
    public const string DeviceUnauthorized = "device_unauthorized";
    /// <summary>422: la plataforma no soporta extracción (iPhone en esta HU).</summary>
    public const string PlatformNotSupported = "platform_not_supported";
    /// <summary>403: la ruta de destino queda fuera de <c>Agent:Extraction:AllowedRoots</c> (Docker).</summary>
    public const string PathNotAllowed = "path_not_allowed";
    /// <summary>404: la ruta de destino no existe.</summary>
    public const string PathNotFound = "path_not_found";
    /// <summary>422: no se puede escribir en el destino.</summary>
    public const string DestinationNotWritable = "destination_not_writable";
    /// <summary>409 de <c>/reveal</c>: Tatana en Docker no puede abrir la carpeta.</summary>
    public const string RevealUnavailable = "reveal_unavailable";
}
