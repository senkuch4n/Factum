import type { User as AuthUser } from "@/lib/api";

export type {
  User, Case, DeviceInput, PublicConfig,
  PeritoSnapshot, ReportTexts, ReportTextsInput, ReportTextFormat, ReportTextsRequest, ReportTextDefaults, ReportCounts, AgentFailedAction,
  CaptureRole, CaptureRoleValue, ReportImage,
  ExpertProfile, ExpertProfileRequest, CaseDataRequest, FileSource, Tratamiento,
  AuthMode, AuthModeInfo, UserRole, ChangePasswordRequest, ChangePasswordField, CatalogId, CatalogEntry, CatalogsResponse,
  EvidenceStorage, EvidenceItem, EvidenceHost, ZipLocation, ManifestFile,
  RegisterEvidenceRequest, EvidenceResponse, PrepareGenerationResponse, FinishGenerationMetadata, GenerateResult,
  TimestampTarget, TimestampStatus, CaseTimestamp, EvidenceTimestampRow, CaseTimestampsResponse,
  TimestampFilesResponse, TimestampFileItem,
  AdminUser, AdminUserStatus, AdminAction, AdminUserField, AdminErrorCode, AdminUserEvent, AdminUserChange,
  AdminCreateUserRequest, AdminUpdateUserRequest, AdminUserWithPassword,
  BrandingImageKind, BrandingImageAction, BrandingImage, AccountBranding, BrandingSaveMetadata,
  AccountBrandingSaveResponse, BrandingField, BrandingErrorCode,
  CaseStats, BreakdownDimension, BreakdownBucket, CaseBreakdown,
  CaseEvent, CaseEventType, CaseEventDetail,
  ReportVersionTrigger, ReportTextVersion, ReportTextVersionTexts,
} from "@/lib/api";
/**
 * Respuesta de login, change-password y refresh (sesion-larga §6.3 / §7.1).
 * Duraciones en segundos, relativas a la respuesta. Opcionales: un backend
 * anterior no las manda y el cliente no programa nada.
 */
export interface AuthResponse {
  token: string;
  user: AuthUser;
  expires_in?: number;
  session_max_in?: number;
  token_ttl?: number;
  session_max_age?: number;
}

export type { Device, AgentEvent, AgentFile, VideoVariant, AgentInfo } from "@/lib/agent";
export type {
  VideoVariantStatus, VideoVariantState, VideoVariantReason, RecordingStoppedData,
  RecordingValidation, RecordingValidationStatus, RecordingStateEntry, UnclaimedRecording, RecordingPhase,
  RecordingStartedData, AgentRecordingState, ScreenshotTakenData, PhotoTakenData, AgentAttribution,
} from "@/lib/agent";

/**
 * Archivo de la bandeja del paso 3 (única definición: `capture/gallery.ts` la
 * reexporta). `uploaded` = registrado en el expediente, en los dos flujos.
 */
export interface CapturedFile {
  name: string;
  uploaded: boolean;
  // Ruta de origen en el dispositivo (solo para archivos traídos con el explorador de
  // archivos) — viaja hasta el informe como referencia de dónde salió cada archivo.
  sourcePath?: string;
  /** Marca local de la captura (se persiste en `capture_roles` del caso). */
  captureRole?: "imei_modelo" | "nombre_dispositivo";
  /** Guardado en la carpeta del caso de Tatana (flujo agent). */
  storedInCase?: boolean;
  sha256?: string;
  size?: number;
  /** "missing": está en el manifiesto pero no en este Tatana (otra PC o borrado). */
  availability?: "here" | "missing";
}

/** `code` de los errores nuevos del backend (zip-local-informe-servidor §5.1). */
export type EvidenceErrorCode =
  | "evidence_on_agent" | "evidence_on_server" | "evidence_on_other_pc" | "case_not_editable"
  | "invalid_manifest" | "no_evidence" | "generation_stale" | "manifest_mismatch" | "missing_images"
  | "image_hash_mismatch" | "invalid_zip_info" | "request_too_large" | "evidence_busy";

/**
 * Una fila de integrante del paso 2. `key` es estable (React y foco al
 * reordenar); no viaja al servidor.
 */
export interface IntegranteRow {
  key: string;
  value: string;
}

/**
 * Estado del paso "Causa". Las claves son las del JSON del caso (snake_case),
 * así los errores mapean 1:1 con el `missing` del servidor. Todas son texto
 * salvo `integrantes` (lista de filas).
 */
export interface CaseFormData {
  nro_referencia: string;
  nombre_denunciante: string;
  dni_denunciante: string;
  nombre_tribunal: string;
  organismo_tribunal: string;
  sala_tribunal: string;
  /** Viaja como `integrantes` (valores recortados, sin filas vacías). */
  integrantes: IntegranteRow[];
  tipo_causa: string;
  caratula: string;
  parte_denunciante: string;
  parte_denunciada: string;
  objeto_causa: string;
  ambito_causa: string;
  fecha_intervencion: string;
  nombre_proponente: string;
  profesion_proponente: string;
  matricula_proponente: string;
  tipo_dispositivo: string;
  linea_dispositivo: string;
  imeiOverride: string;
}

/** Formulario del perfil del perito (tarjeta del paso 2 y diálogo del menú). */
export interface ProfileFormData {
  nombre: string;
  matricula: string;
  profesion: string;
  caracter: string;
  tratamiento: "suscripto" | "suscripta";
}

/** Una sola definición en `lib/agent.ts` (la usa también `RecordingStateEntry.ios_mode`). */
export type { IosRecordMode } from "@/lib/agent";

/* ── Subida de evidencia (subida-archivos-grandes, SDD §6.1) ── */

/** Respuesta de POST /api/cases/{id}/files (FileInfoDto). */
export interface UploadedFileInfo {
  name: string;
  size: number;
  hash: string;
  modified_at: string;
  source_path?: string | null;
}

/** Respuesta OK de GET /api/cases/{id}/files/upload-check. */
export interface UploadCheckResponse {
  max_upload_bytes: number;
}

export type UploadErrorCode =
  | "invalid_filename" | "case_not_editable" | "length_required" | "file_too_large"
  | "insufficient_storage" | "incomplete_upload" | "storage_error";

/** Cuerpo de error de las rutas de subida (snake_case tal cual viaja). */
export interface UploadErrorBody {
  error: string;
  code?: UploadErrorCode;
  size?: number;
  max_upload_bytes?: number;
  required_bytes?: number;
  available_bytes?: number | null;
  received_bytes?: number;
}

/** `copying`/`hashing`/`registering`: guardado en la PC (flujo agent). */
export type UploadPhase = "checking" | "preparing" | "uploading" | "finishing" | "copying" | "hashing" | "registering";
/** `auth_pending`: guardado en la PC, registro pendiente por sesión vencida (sesion-larga §8.9). */
export type FileUploadState = "uploading" | "error" | "auth_pending";

export interface UploadProgress {
  /** 1-based, archivo actual. */
  index: number;
  /** Archivos a enviar en esta tanda. */
  total: number;
  name: string;
  phase: UploadPhase;
  /** Bytes de la fase actual. */
  loaded: number;
  /** `null` si no se conoce (lengthComputable=false). */
  totalBytes: number | null;
}
