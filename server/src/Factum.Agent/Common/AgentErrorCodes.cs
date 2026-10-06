namespace Factum.Agent.Common;

/// <summary>
/// Códigos estables (campo <c>code</c> del JSON de error) de las rutas de evidencia y ZIP del
/// caso (zip-local-informe-servidor §6 y §8.2).
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
}
