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
    public const string OriginNotAllowed = "origin_not_allowed";

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
}
