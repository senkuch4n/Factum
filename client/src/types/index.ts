export type {
  User, Case, DeviceInput, PublicConfig,
  PeritoSnapshot, ReportTexts, ReportTextsInput, ReportTextFormat, ReportTextsRequest, ReportTextDefaults,
  CaptureRole, CaptureRoleValue, ReportImage,
  ExpertProfile, ExpertProfileRequest, CaseDataRequest, FileSource, Tratamiento,
  AuthMode, CatalogId, CatalogEntry, CatalogsResponse,
} from "@/lib/api";
export type { Device, AgentEvent, AgentFile, VideoVariant } from "@/lib/agent";

export interface CapturedFile {
  name: string;
  uploaded: boolean;
  // Ruta de origen en el dispositivo (solo para archivos traídos con el explorador de
  // archivos) — viaja hasta el informe como referencia de dónde salió cada archivo.
  sourcePath?: string;
  /** Marca local de la captura (se persiste en `capture_roles` del caso). */
  captureRole?: "imei_modelo" | "nombre_dispositivo";
}

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

export type IosRecordMode = "video_only" | "with_mic" | "on_device" | "airplay";

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

export type UploadPhase = "checking" | "preparing" | "uploading" | "finishing";
export type FileUploadState = "uploading" | "error";

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
