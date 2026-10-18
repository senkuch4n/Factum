/**
 * Cliente del Agente Local (corre en localhost:8765 en la PC del operador).
 * El agente maneja ADB, scrcpy, webcam y exposición de archivos.
 */

import type { ManifestFile } from "./api";
import type { ClockReason, DeviceDetailKey, DeviceDetailReason } from "./device-details";
import { getTabOriginId } from "./tab-origin";

export const AGENT_URL = process.env.NEXT_PUBLIC_AGENT_URL || "http://localhost:8765";
export function agentFileURL(filename: string) { return `${AGENT_URL}/files/${filename}`; }

/**
 * Atribución de una captura (wizard-concurrencia-pestanas DT1/DT3): el caso que
 * la pidió. El `origin_id` de la pestaña lo agrega siempre el cliente.
 */
export interface AgentAttribution {
  caseId?: string | null;
}

/**
 * Query `case_id`/`origin_id` (los POST sin body; CORS de Tatana no permite headers propios).
 * `null` = pedido sin dueño (vista previa con un Tatana viejo, detener una grabación ajena).
 */
function attributionParams(attribution?: AgentAttribution | null): URLSearchParams {
  const p = new URLSearchParams();
  if (attribution === null) return p;
  if (attribution?.caseId) p.set("case_id", attribution.caseId);
  p.set("origin_id", getTabOriginId());
  return p;
}

/** Body `case_id`/`origin_id` (record/start, webcam/capture). */
function attributionBody(attribution?: AgentAttribution): { case_id?: string; origin_id: string } {
  return {
    ...(attribution?.caseId ? { case_id: attribution.caseId } : {}),
    origin_id: getTabOriginId(),
  };
}

/** Mensaje cuando `fetch` ni siquiera llega a Tatana (agente cerrado o puerto ocupado). */
const AGENT_UNREACHABLE = "No se pudo conectar con Tatana. Revisá que esté abierto en esta PC.";

/**
 * Lee `{ error }` de una respuesta no-2xx de Tatana (contrato de la SDD
 * grabacion-android-windows §5.1); si no viene o no es texto, usa `fallback`.
 */
async function readAgentError(res: Response, fallback: string): Promise<Error> {
  const data = (await res.json().catch(() => null)) as { error?: unknown } | null;
  const msg = data && typeof data.error === "string" && data.error.trim() ? data.error.trim() : fallback;
  return new Error(msg);
}

export interface Device {
  serial: string;
  state: string;
  manufacturer: string;
  model: string;
  android_version: number;
  imei: string;
  name: string;
  operator: string;
  platform: "android" | "ios";
  ios_version?: string;
}

export interface AgentFile {
  name: string;
  size: number;
  url: string;
  mod_time: string;
}

export interface AgentEvent {
  type: string;
  timestamp: string;
  data: Record<string, unknown>;
}

/** `GET /agent/state` → `operations[]` (existe en todo Tatana con `agent_state_v1`). */
export interface AgentOperation {
  kind: string;            // "recording_android" | "recording_ios" | "airplay_session" | "request" | …
  since: string;           // ISO-8601
  detail?: string | null;  // Tatana omite los null
}

/**
 * Kinds de `operations[]` que indican una grabación en curso en la PC.
 * `recording_session`: grabación de la sesión (grabacion-sesion-completa §7.4).
 */
export const RECORDING_OPERATION_KINDS = ["recording_android", "recording_ios", "recording_session"] as const;

export interface VideoVariant {
  label: string;
  filename: string;
  url: string;
}

/* ── ios-grabacion-larga (SDD §6.1): post-proceso de video en segundo plano ── */

export type VideoVariantLabel = "Suavizado" | "RIFE" | "MCI"; // "MCI" solo puede venir en §4.4 known:false (legado)
export type VideoVariantState = "pending" | "running" | "ready" | "failed" | "timeout" | "skipped" | "cancelled";
export type VideoVariantReason =
  | "ffmpeg_error" | "timeout" | "too_long" | "insufficient_disk" | "source_missing"
  | "removed" | "agent_update" | "agent_stopping" | "internal_error";

/** `video_variant_status` (WS, null explícitos) e items de `GET /video-postprocess/status` (HTTP, null omitidos). */
export interface VideoVariantStatus {
  original_filename: string;
  label: VideoVariantLabel | string;   // tolerante a etiquetas futuras
  state: VideoVariantState;
  progress?: number | null;
  filename?: string | null;
  url?: string | null;
  reason?: VideoVariantReason | string | null;
  message?: string | null;             // solo log; no se muestra
  updated_at: string;
}

/* ── grabacion-android-robusta (SDD §7.1): validación y estado observable de la grabación ── */

export type RecordingValidationStatus = "ok" | "incomplete" | "empty" | "unreadable";

/** `validation` de stop, `recording_stopped`, `unclaimed_recordings[]` y del error 422 (solo Android). */
export interface RecordingValidation {
  status: RecordingValidationStatus;
  recorded_seconds: number;
  duration_seconds?: number | null;
  duration_checked: boolean;
  forced_stop: boolean;
  started_at: string;
  stopped_at: string;
}

export type IosRecordMode = "video_only" | "with_mic" | "on_device" | "airplay";
export type RecordingPhase = "starting" | "recording" | "stopping";

/** `GET /agent/state` → `recordings[]` (HTTP: los null se omiten). */
export interface RecordingStateEntry {
  platform: "android" | "ios";
  serial: string;
  state: RecordingPhase;
  requested_at: string;
  started_at?: string | null;
  filename?: string | null;
  case_id?: string | null;
  android_with_mic?: boolean | null;
  ios_mode?: IosRecordMode | null;
}

/** `GET /agent/state` → `unclaimed_recordings[]`: detenidas (ok/incomplete) que nadie importó. */
export interface UnclaimedRecording {
  platform: "android" | "ios";
  serial: string;
  filename: string;
  case_id?: string | null;
  stopped_at: string;
  validation?: RecordingValidation | null;
}

export interface AgentRecordingState {
  recordings: RecordingStateEntry[];
  unclaimed_recordings: UnclaimedRecording[];
}

/** Evento WS `recording_started` (`data`). `case_id`/`origin_id` pueden venir `null`. */
export interface RecordingStartedData {
  serial: string;
  platform: "android" | "ios";
  case_id?: string | null;
  /** Pestaña que la pidió (wizard-concurrencia-pestanas); falta en un Tatana anterior. */
  origin_id?: string | null;
  started_at?: string;
  filename?: string;
  /** Marca de la grabación de la sesión (grabacion-sesion-completa §9.1); falta sin sesión. */
  session_mark?: SessionMarkDto | null;
}

/**
 * Evento WS `screenshot_taken` (`data`). `serial`/`case_id`/`origin_id` faltan
 * en un Tatana anterior a `event_attribution_v1`.
 */
export interface ScreenshotTakenData {
  filename: string;
  url: string;
  serial?: string;
  case_id?: string | null;
  origin_id?: string | null;
  /** Marca automática de la sesión (§7.3): `null` si no hubo; falta en un Tatana anterior. */
  session_mark?: SessionMarkDto | null;
}

/* ── Grabación de la sesión completa (grabacion-sesion-completa, SDD §7.2-§7.4) ──
   HTTP: los `null` no viajan (campos opcionales). WebSocket: viajan como `null`. */

/** Estado de la sesión (§5.3). Activos: `starting`…`stopping` (ver `ACTIVE_SESSION_STATES`). */
export type SessionStateName =
  | "idle" | "starting" | "recording" | "rotating" | "waiting_device" | "stopping" | "stopped" | "interrupted";

/** Pista de un tramo o hueco. `camara` llega en la fase 2. */
export type SessionTrack = "pantalla" | "camara";

/** Motivo de un hueco o de un `stop_reason` (§6.2). `rotacion` solo con corte secuencial. */
export type SessionReason =
  | "cambio_de_paso" | "retomado_otro_dia" | "desconexion" | "tatana_cerrado" | "sin_espacio"
  | "camara_fallo" | "detenido_por_perito" | "fallo_grabacion" | "rotacion";

export type SessionWarningKind = "screen_off" | "screen_locked" | "disk_low" | "camera_failed" | "rotation_delayed";

export interface SessionWarning {
  kind: SessionWarningKind;
  since: string;
  /** `disk_low`. */
  available_bytes?: number | null;
  minutes_left?: number | null;
  /** `camera_failed`: motivo de la falla de la cámara (grabacion-sesion-completa-f2 §7.2.7). */
  detail?: SessionCameraFailDetail | string | null;
}

/** `warnings[].detail` de `camera_failed` (F2 §7.2.7). Ausente = sin motivo conocido. */
export type SessionCameraFailDetail = "permission" | "blocked" | "not_found" | "unsupported" | "stalled";

/** Cámara de la sesión activa (`SessionStateDto.camera`, F2 §7.2.7). W×H y fps faltan si nunca arrancó. */
export interface SessionActiveCamera {
  id: string;
  name: string;
  width?: number | null;
  height?: number | null;
  fps?: number | null;
  with_audio: boolean;
}

/** Cámara de la PC (`GET /cameras`, F2 §7.2.1). */
export interface CameraDevice {
  id: string;
  name: string;
  backend: "avfoundation" | "dshow" | "v4l2" | "mock" | string;
}

/** `GET /cameras`. Sin ffmpeg: lista vacía con `error`. */
export interface CameraListResponse {
  cameras: CameraDevice[];
  error?: string | null;
}

/** `POST /cameras/preview` (F2 §7.2.2). Una cámara que no abre no es error HTTP. */
export interface CameraPreviewResult {
  ok: boolean;
  detail?: SessionCameraFailDetail | string | null;
  error?: string | null;
}

/** Cámara elegida para la sesión (body de `camera/start` y `camera` del start, F2 §7.2.4–7.2.5). */
export interface SessionCameraRequest {
  /** Opcional en el start de la sesión: si falta, Tatana busca por `name`. */
  id?: string;
  name: string;
  with_audio: boolean;
}

/** `SessionStateDto` (§7.2.7): `start`/`stop`, WS `session_state` y `/agent/state.recording_session`. */
export interface SessionStateDto {
  case_id: string;
  serial?: string | null;
  platform?: "android" | "ios" | null;
  state: SessionStateName;
  resumed?: boolean | null;
  segment_minutes?: number | null;
  session_started_at?: string | null;
  /** Inicio del tramo continuo actual (último start o reanudación): lo usa el cronómetro. */
  run_started_at?: string | null;
  screen_segment_index?: number | null;
  screen_segment_started_at?: string | null;
  camera_state?: "off" | "recording" | "failed" | null;
  camera_segment_index?: number | null;
  camera_segment_started_at?: string | null;
  /** Cámara de la sesión mientras `camera_state ≠ "off"` (F2 §7.2.7). */
  camera?: SessionActiveCamera | null;
  segments_saved?: number | null;
  recorded_seconds?: number | null;
  stop_reason?: SessionReason | null;
  stopped_at?: string | null;
  warnings?: SessionWarning[] | null;
  origin_id?: string | null;
}

/** Tramo del diario (§7.2.6). `recording`/`closing` solo existen en Tatana. */
export interface SessionSegmentDto {
  id: string;
  track: SessionTrack;
  index: number;
  filename: string;
  state: "recording" | "closing" | "saved" | "discarded";
  started_at: string;
  stopped_at?: string | null;
  recorded_seconds?: number | null;
  duration_seconds?: number | null;
  validation?: "ok" | "incomplete" | null;
  recovered?: boolean | null;
  overlap_seconds?: number | null;
  stop_reason?: SessionReason | null;
  size?: number | null;
  sha256?: string | null;
  /** Solo en tramos `camara` (F2 §7.2.6). Nunca trae el `id` técnico. */
  camera?: SessionCameraInfo | null;
}

/** Hueco del diario: `to` falta (o `null`) si sigue abierto. */
export interface SessionGapDto {
  id: string;
  track: SessionTrack;
  from: string;
  to?: string | null;
  reason: SessionReason;
  segment_lost?: boolean | null;
}

/** Marca de momento (§5.9): manual o automática de captura. */
export interface SessionMarkDto {
  id: string;
  at: string;
  /** Ubicación en la pantalla. Falta si la marca se hizo con la pantalla sin grabar (F2 [DU1=A]). */
  segment_index?: number | null;
  offset_seconds?: number | null;
  /** Ubicación en la cámara: solo si un tramo de cámara cubre `at` (F2 §5.7). */
  camera_segment_index?: number | null;
  camera_offset_seconds?: number | null;
  text?: string | null;
  filename?: string | null;
  kind: "manual" | "captura";
}

/** Cámara de la sesión (fase 2). */
export interface SessionCameraInfo {
  name: string;
  width: number;
  height: number;
  fps: number;
  with_audio: boolean;
}

/** Diario de la sesión (`GET /cases/{id}/session`, §7.2.6). */
export interface CaseSessionDto {
  schema_version: number;
  session_id: string;
  case_id: string;
  serial: string;
  platform: "android" | "ios";
  agent_version?: string | null;
  segment_minutes: number;
  audio?: "device" | "none" | null;
  camera?: SessionCameraInfo | null;
  state: SessionStateName;
  stop_reason?: SessionReason | null;
  segments: SessionSegmentDto[];
  gaps: SessionGapDto[];
  marks: SessionMarkDto[];
  updated_at?: string | null;
}

/** `GET /session/capacity` (§7.2.1). Sin medición del disco: faltan `available_bytes`/`estimated_minutes`. */
export interface SessionCapacity {
  available_bytes?: number | null;
  min_free_bytes?: number | null;
  bytes_per_minute?: number | null;
  zip_on_same_volume?: boolean | null;
  estimated_minutes?: number | null;
  required_bytes_to_start?: number | null;
  can_start: boolean;
}

/** Body de `POST /cases/{caseId}/session/start` (§7.2.2) sin `origin_id` (lo agrega el cliente). */
export interface SessionStartRequest {
  serial: string;
  platform: "android" | "ios";
  android_version: number;
  segment_minutes: number;
  skip_screen_check?: boolean;
  /** Cámara del procedimiento al retomar (F2 §7.2.4, D1 = A). */
  camera?: SessionCameraRequest;
}

/** Motivo de un `stop` pedido por la web (§7.2.3). */
export type SessionStopReason = "cambio_de_paso" | "detenido_por_perito";

/** WS `session_segment_closed` (§7.3). */
export interface SessionSegmentClosedData {
  case_id: string;
  origin_id?: string | null;
  segment: SessionSegmentDto;
}

/** WS `session_mark_added` (§7.3). */
export interface SessionMarkAddedData {
  case_id: string;
  origin_id?: string | null;
  mark: SessionMarkDto;
}

/** `/agent/state` → sesión activa (si hay) y estado de la recuperación al arrancar. */
export interface AgentSessionState {
  recording_session: SessionStateDto | null;
  session_recovery: "running" | "done" | null;
}

/** Evento WS `photo_taken` (`data`): webcam de Tatana, sin `serial`. */
export interface PhotoTakenData {
  type?: string;
  filename: string;
  url: string;
  case_id?: string | null;
  origin_id?: string | null;
}

/**
 * Respuesta de `POST /record/stop` (200) y evento WS `recording_stopped`.
 * En la falla `recording_empty`/`recording_unreadable` del WS: `filename`/`url`
 * en `null`, con `error` y `code`.
 */
export interface RecordingStoppedData {
  filename: string | null;
  url: string | null;
  platform?: "android" | "ios";
  serial?: string;
  case_id?: string | null;
  /** Pestaña que inició la grabación (wizard-concurrencia-pestanas); falta en un Tatana anterior. */
  origin_id?: string | null;
  /** Solo Android con Tatana `recording_state_v1`; iOS: `null`/ausente. */
  validation?: RecordingValidation | null;
  error?: string | null;
  code?: AgentErrorCode | null;
  /** Falta en un Tatana anterior a video_postprocess_v1. */
  variants_expected?: boolean;
  variants?: string[];
}

export interface VideoPostprocessStatusResponse {
  items: { original_filename: string; known: boolean; variants: VideoVariantStatus[] }[];
}

/** Tope de `filename` por pedido de `GET /video-postprocess/status` (SDD §4.4). */
const POSTPROCESS_STATUS_BATCH = 50;

export interface AgentInfo {
  hostname: string;
  os_user: string;
  version: string;
  mode: "installed" | "portable";
  /** Carpeta base del ZIP en esta PC (zip-local-informe-servidor §6.1). Tatanas viejos no la mandan. */
  evidence_directory?: string;
  /** `true` si la carpeta del ZIP cae dentro de OneDrive/iCloud (DP4: solo aviso). */
  evidence_directory_synced?: boolean;
}

/** Herramienta externa de Tatana (`/health.tools.<k>`, `ToolInventory`). */
export interface AgentToolStatus {
  found: boolean;
  source?: "portable" | "path" | "homebrew";
  path?: string;
  version?: string;
  /** Solo en `tools.python` (ios-herramientas-windows §4.2); falta si no se pudo leer. */
  pymobiledevice3_version?: string;
}

/** Estado del servicio de dispositivos de Apple en la PC (`/health.ios.apple_service`). */
export type AppleServiceState = "ok" | "missing" | "unknown";

/** Motivo de AirPlay no disponible (`/health.ios.airplay_unavailable_reason`). */
export type AirplayUnavailableReason = "not_supported_on_windows" | "uxplay_not_found";

/** `/health.ios` (ios-herramientas-windows §4.2). Tatanas anteriores a 1.3.0 no lo mandan. */
export interface AgentIosStatus {
  apple_service: AppleServiceState;
  airplay_available: boolean;
  /** Tatana omite los `null`: con AirPlay disponible, no viene. */
  airplay_unavailable_reason?: AirplayUnavailableReason;
}

/** Respuesta de `POST /devices/{serial}/ios/developer-mode` (§4.3). */
export type IosDeveloperModeStatus = "enabled" | "restarting" | "manual_required";

/**
 * `GET /health`. `capabilities` falta en un Tatana anterior a zip-local-informe-servidor.
 * Capabilities conocidas:
 * - `case_evidence_v1`: evidencia del caso en esta PC (zip-local-informe-servidor);
 * - `real_version_v1`: `version` es la versión real de Tatana. Sin ella, `version`
 *   no es confiable (los Tatana anteriores dicen siempre "2.0.0") y se trata
 *   como anterior a cualquier mínimo (tatana-instalador-autoupdate, D-T2);
 * - `agent_state_v1`: `GET /agent/state` y modo mantenimiento (D5);
 * - `video_postprocess_v1`: variantes de video en segundo plano, `video_variant_status`
 *   y `GET /video-postprocess/status` (ios-grabacion-larga);
 * - `recording_state_v1`: `recordings`/`unclaimed_recordings` en `GET /agent/state`,
 *   `case_id` en el start y validación de la grabación (grabacion-android-robusta);
 * - `case_seals_v1`: `PUT /cases/{id}/zip/seals` copia los `.tsr` y el LEEME a
 *   `sellos/`, al lado del ZIP (sellado-tiempo);
 * - `device_details_v1`: `GET /devices/{serial}/details` (ficha técnica de solo
 *   lectura + reloj) y `GET /devices/{serial}/clock` (metadatos-dispositivo);
 * - `recording_session_v1`: grabación de la sesión completa con la pantalla de
 *   Android, `zip_folder` en el ZIP y `/agent/state.recording_session`
 *   (grabacion-sesion-completa); `recording_session_camera_v1` y
 *   `recording_session_ios_v1` llegan en la fase 2.
 */
export interface AgentHealth {
  status: string;
  version: string;
  mock: boolean;
  capabilities?: string[];
  ios_available?: boolean;
  /** Falta en Tatana < 1.1.0. */
  tools?: Record<string, AgentToolStatus>;
  /** Falta en Tatana < 1.3.0 (ios-herramientas-windows). */
  ios?: AgentIosStatus;
}

/* ── Evidencia del caso en Tatana (zip-local-informe-servidor, SDD §6.2 y §8.2) ── */

/** Archivo guardado en la carpeta del caso: respuesta de `import` y `upload`. */
export interface EvidenceFileInfo {
  filename: string;
  size: number;
  sha256: string;
  saved_at: string;
}

/** `GET /cases/{id}/files`: primer nivel de la carpeta del caso, sin hashes. */
export interface AgentCaseFiles {
  directory: string;
  files: { filename: string; size: number; modified_at: string }[];
}

/** `POST /cases/{id}/zip`. */
export interface BuildZipRequest {
  case_ref: string;
  zip_filename: string;
  password: string | null;
  files: ManifestFile[];
}

export interface BuildZipResponse {
  zip_filename: string;
  zip_hash: string;
  zip_size: number;
  encrypted: boolean;
  /** Tatana omite los `null`: sin cifrar, no viene. */
  encryption?: string | null;
  /** Carpeta final del caso (`<EvidenceDirectory>/<causa>_<id8>`). */
  directory: string;
  /** Ruta final del ZIP (donde queda después del commit). */
  zip_path: string;
  hostname: string;
  files: ManifestFile[];
}

/** `POST /cases/{id}/zip/commit`. */
export interface CommitZipRequest {
  case_ref: string;
  zip_filename: string;
  zip_hash: string;
  delete_files: string[];
}

export interface CommitZipResponse {
  zip_path: string;
  deleted: number;
}

/** `PUT /cases/{id}/zip/seals` (sellado-tiempo, SDD §7.3). */
export interface WriteSealsRequest {
  case_ref: string;
  zip_filename: string;
  files: { name: string; content_base64: string }[];
  /** Texto de `LEEME-verificacion.txt`; `null` = no se escribe. */
  readme: string | null;
}

export interface WriteSealsResponse {
  /** Ruta absoluta de `sellos/`. */
  directory: string;
  written: number;
  unchanged: number;
}

/** `GET /cases/{id}/zip/status`. */
export interface ZipStatus {
  state: "none" | "pending" | "final";
  zip_path: string;
  directory: string;
  pending_hash?: string;
  committed_hash?: string;
}

/** Evento WS `zip_progress` (`data`). */
export interface ZipProgress {
  case_id: string;
  phase: "hashing" | "zipping" | "verifying";
  done_bytes: number;
  total_bytes: number;
}

/* ── Ficha técnica del dispositivo (metadatos-dispositivo, SDD §8.1) ── */

/** Un dato de la ficha: exactamente uno de `value` o `reason` (Tatana omite los `null`). */
export interface AgentDetailField { value?: string; reason?: DeviceDetailReason }

/**
 * Medición de reloj dispositivo↔PC. `pc_before`/`pc_after`/`device_time` son ISO
 * con 7 decimales: la web **no los parsea**, los reenvía tal cual a la API.
 */
export interface AgentDeviceClock {
  pc_before?: string; pc_after?: string; device_time?: string;
  resolution_ms?: number; device_minus_pc_ms?: number; margin_ms?: number;
  samples: number; reason?: ClockReason;
}

/** `GET /devices/{serial}/details`. */
export interface AgentDeviceDetails {
  serial: string; platform: "android" | "ios"; agent_version: string;
  read_started_at: string; read_finished_at: string;
  fields: Record<DeviceDetailKey, AgentDetailField>;
  clock: AgentDeviceClock;
}

/** `GET /devices/{serial}/clock`. */
export interface AgentDeviceClockResponse { serial: string; platform: "android" | "ios"; agent_version: string; clock: AgentDeviceClock }

export type AgentErrorCode =
  | "invalid_case_id" | "invalid_filename" | "file_not_found" | "file_exists" | "file_busy"
  | "length_required" | "file_too_large" | "insufficient_storage" | "incomplete_upload" | "storage_error"
  | "evidence_changed" | "zip_in_progress" | "zip_already_committed" | "zip_failed" | "zip_not_found"
  | "zip_hash_mismatch" | "origin_not_allowed"
  // Tatana en modo mantenimiento mientras se actualiza (tatana-instalador-autoupdate, 503).
  | "agent_updating"
  // iPhone (ios-herramientas-windows §4.1): el `error` que acompaña ya viene en castellano.
  | "ios_apple_service_missing" | "ios_device_not_found" | "ios_not_trusted" | "ios_locked"
  | "ios_developer_mode_disabled" | "ios_ddi_mount_failed" | "ios_tunnel_failed" | "ios_admin_required"
  | "ios_tools_missing" | "ios_capture_failed" | "ios_recording_empty" | "airplay_unavailable"
  // Grabación (grabacion-android-robusta §6): start/stop y evidencia de 0 B.
  | "already_recording" | "recording_start_timeout" | "not_recording" | "recording_empty"
  | "recording_unreadable" | "empty_evidence"
  // Pantalla del celular apagada o bloqueada: 409 de screenshot y record/start (pulido-post-qa-final §6.2).
  | "screen_off" | "screen_locked"
  // Ficha técnica: el dispositivo no está conectado o no autorizó la conexión (metadatos-dispositivo §7.2).
  | "device_not_found"
  // Grabación de la sesión (grabacion-sesion-completa §7.7).
  | "session_active" | "session_not_active" | "session_not_found" | "session_file_protected"
  | "session_platform_unsupported" | "session_ios_mode_unsupported"
  // Cámara del procedimiento (grabacion-sesion-completa-f2 §7.6).
  | "session_camera_unsupported" | "camera_busy" | "camera_preview_unavailable";

/** Cuerpo de error de Tatana (claves literales en snake_case). */
export interface AgentErrorBody {
  error?: string;
  code?: AgentErrorCode;
  size?: number;
  max_upload_bytes?: number;
  required_bytes?: number;
  available_bytes?: number | null;
  received_bytes?: number;
  files?: { filename: string; reason: "missing" | "size" | "hash" }[];
  /** 422 `recording_empty`/`recording_unreadable` del stop. */
  validation?: RecordingValidation;
  /** 409 `screen_off`/`screen_locked` de screenshot y record/start (pulido-post-qa-final §6.2). */
  screen_state?: "off" | "locked";
  /** 409 `session_active` del start de la sesión: caso de la sesión que ya está grabando. */
  case_id?: string;
}

/** Opciones de captura: `skipScreenCheck` = "Capturar igual" / "Grabar igual" (pulido-post-qa-final §6.1). */
export interface ScreenCheckOptions {
  skipScreenCheck?: boolean;
}

/**
 * Si `e` es el 409 de pantalla apagada o bloqueada, devuelve el estado
 * (`screen_state`, o el que se deduce del `code`); si no, `null`.
 */
export function screenBlockOf(e: unknown): "off" | "locked" | null {
  if (!(e instanceof AgentError) || (e.code !== "screen_off" && e.code !== "screen_locked")) return null;
  const s = e.body?.screen_state;
  return s === "off" || s === "locked" ? s : e.code === "screen_off" ? "off" : "locked";
}

/**
 * Error de las rutas nuevas de Tatana.
 * - `unreachable`: `fetch`/XHR no llegó (Tatana cerrado, puerto ocupado, permiso de red local denegado).
 * - `http`: Tatana respondió no-2xx (`status`, `code`, `body`).
 * - `aborted`: se canceló.
 * El `message` no se muestra: la UI arma el texto con `lib/agent-messages.ts`.
 */
export class AgentError extends Error {
  kind: "unreachable" | "http" | "aborted";
  status: number;
  code: AgentErrorCode | null;
  body: AgentErrorBody | null;
  constructor(kind: "unreachable" | "http" | "aborted", status = 0, body: AgentErrorBody | null = null) {
    super(body?.error || (kind === "unreachable" ? "Tatana no responde" : kind === "aborted" ? "Cancelado" : `Tatana respondió ${status}`));
    this.name = "AgentError";
    this.kind = kind;
    this.status = kind === "http" ? status : 0;
    this.code = body?.code ?? null;
    this.body = body;
  }
}

function parseAgentErrorBody(text: string): AgentErrorBody | null {
  try {
    const b = JSON.parse(text) as unknown;
    if (b && typeof b === "object") return b as AgentErrorBody;
  } catch { /* cuerpo vacío o no JSON */ }
  return null;
}

/** `fetch` a Tatana que traduce todo a `AgentError` (nunca deja escapar "Failed to fetch"). */
async function agentFetch(path: string, init: RequestInit = {}): Promise<Response> {
  let res: Response;
  try {
    res = await fetch(`${AGENT_URL}${path}`, { cache: "no-store", ...init });
  } catch (e) {
    if (init.signal?.aborted || (e instanceof DOMException && e.name === "AbortError")) throw new AgentError("aborted");
    throw new AgentError("unreachable");
  }
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new AgentError("http", res.status, parseAgentErrorBody(text));
  }
  return res;
}

async function agentJSON<T>(path: string, init: RequestInit = {}): Promise<T> {
  const res = await agentFetch(path, init);
  try {
    return (await res.json()) as T;
  } catch {
    throw new AgentError("http", res.status, null);
  }
}

function jsonInit(method: string, body: unknown): RequestInit {
  return { method, headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) };
}

const caseBase = (caseId: string) => `/cases/${encodeURIComponent(caseId)}`;
const zipQuery = (caseRef: string, zipFilename: string) =>
  new URLSearchParams({ case_ref: caseRef, zip_filename: zipFilename }).toString();

/* ── Local Network Access (SDD §7.8 y §9.3) ── */

export type LocalNetworkPermission = "granted" | "denied" | "prompt" | "unknown";

let lastLocalNetworkPermission: LocalNetworkPermission = "unknown";

/** Último resultado de `localNetworkPermission()` (lo usa `agentErrorMessage`, que es sincrónico). */
export function cachedLocalNetworkPermission(): LocalNetworkPermission {
  return lastLocalNetworkPermission;
}

/**
 * Estado del permiso de red local/loopback del navegador. Los nombres cambiaron
 * entre versiones de Chrome: se prueban varios y se degrada a `"unknown"`. Solo
 * se consulta si la página no está servida desde localhost.
 */
export async function localNetworkPermission(): Promise<LocalNetworkPermission> {
  if (typeof window === "undefined" || typeof navigator === "undefined" || !navigator.permissions) return "unknown";
  const host = window.location.hostname;
  if (host === "localhost" || host === "127.0.0.1" || host === "[::1]") return "unknown";
  for (const name of ["loopback-network", "local-network-access", "local-network"]) {
    try {
      const st = await navigator.permissions.query({ name } as unknown as PermissionDescriptor);
      const v = st.state;
      lastLocalNetworkPermission = v === "granted" || v === "denied" || v === "prompt" ? v : "unknown";
      return lastLocalNetworkPermission;
    } catch { /* nombre no soportado: probar el siguiente */ }
  }
  lastLocalNetworkPermission = "unknown";
  return "unknown";
}

export interface DeviceFileEntry {
  name: string;
  path: string;
  is_directory: boolean;
  size: number;
  modified_at: string | null;
}

export interface PulledFile {
  original_name: string;
  filename?: string;
  url?: string;
  ok: boolean;
  error?: string;
}

/** Tatana sin `POST /devices/{serial}/preview` (anterior a `device_preview_v1`): no se reintenta el 404. */
let previewUnsupported = false;

/** El Tatana conectado no tiene el endpoint de vista previa (DT5). */
export class PreviewUnsupportedError extends Error {
  constructor() {
    super("Tatana no tiene vista previa");
    this.name = "PreviewUnsupportedError";
  }
}

/** URL absoluta de una vista previa (`url` = `/previews/<archivo>`). */
export function previewFileURL(url: string): string {
  return `${AGENT_URL}${url}`;
}

/** Capabilities de `/health` de la conexión actual (ver `supportsRecordingState`). */
let capabilitiesCache: string[] | null = null;

export const agent = {
  baseURL: AGENT_URL,

  async isOnline(): Promise<boolean> {
    try {
      const res = await fetch(`${AGENT_URL}/health`, { signal: AbortSignal.timeout(2000) });
      return res.ok;
    } catch {
      return false;
    }
  },

  async listDevices(): Promise<Device[]> {
    const res = await fetch(`${AGENT_URL}/devices`);
    if (!res.ok) throw new Error("Error listando dispositivos");
    const data = await res.json();
    return data.devices || [];
  },

  /** Identidad de esta PC (hostname, usuario, modo instalado/portátil) — usado para auditoría de uso en el backend. */
  async getInfo(): Promise<AgentInfo> {
    const res = await fetch(`${AGENT_URL}/info`, { signal: AbortSignal.timeout(2000) });
    if (!res.ok) throw new Error("Error obteniendo info del agente");
    return res.json();
  },

  async getDeviceInfo(serial: string): Promise<Device> {
    const res = await fetch(`${AGENT_URL}/devices/${serial}/info`);
    if (!res.ok) throw new Error("Error obteniendo info del dispositivo");
    return res.json();
  },

  /**
   * Captura de evidencia (paso 3). Va con `case_id`/`origin_id` para que el
   * `screenshot_taken` solo lo tome esta pestaña o las del mismo caso.
   */
  async takeScreenshot(
    serial: string,
    platform: "android" | "ios" = "android",
    attribution?: AgentAttribution | null,
    opts?: ScreenCheckOptions,
  ): Promise<{ filename: string; url: string; session_mark?: SessionMarkDto | null }> {
    const q = attributionParams(attribution);
    if (platform === "ios") q.set("platform", "ios");
    // "Capturar igual": un Tatana sin el chequeo ignora el parámetro.
    if (opts?.skipScreenCheck) q.set("skip_screen_check", "true");
    const qs = q.toString();
    const url = `${AGENT_URL}/devices/${serial}/screenshot${qs ? `?${qs}` : ""}`;
    let res: Response;
    try {
      res = await fetch(url, { method: "POST" });
    } catch {
      throw new Error(AGENT_UNREACHABLE);
    }
    // D7: el motivo real que manda Tatana (`{ error, code }`); el fallback solo si no viene.
    // Pantalla apagada o bloqueada: `AgentError` con `code` y `screen_state` (el llamador
    // muestra el aviso con "Capturar igual"). El cuerpo se lee una sola vez.
    if (!res.ok) {
      const body = parseAgentErrorBody(await res.text().catch(() => ""));
      if (body?.code === "screen_off" || body?.code === "screen_locked") throw new AgentError("http", res.status, body);
      const msg = typeof body?.error === "string" && body.error.trim() ? body.error.trim() : "Error tomando screenshot";
      throw new Error(msg);
    }
    return res.json();
  },

  /**
   * Vista previa del paso 2 (wizard-concurrencia-pestanas DT5): `POST
   * /devices/{serial}/preview`. No es evidencia: Tatana la guarda aparte y no
   * emite ningún evento. Un Tatana sin el endpoint (404) lanza
   * `PreviewUnsupportedError`; el llamador cae a `takeScreenshot` sin atribución.
   */
  async takePreview(
    serial: string,
    platform: "android" | "ios" = "android",
  ): Promise<{ filename: string; url: string }> {
    if (previewUnsupported) throw new PreviewUnsupportedError();
    const q = platform === "ios" ? "?platform=ios" : "";
    let res: Response;
    try {
      res = await fetch(`${AGENT_URL}/devices/${encodeURIComponent(serial)}/preview${q}`, { method: "POST", cache: "no-store" });
    } catch {
      throw new Error(AGENT_UNREACHABLE);
    }
    if (res.status === 404) {
      // Un 404 con `{ error }` es del celular (no encontrado); sin cuerpo de Tatana, es la ruta que no existe.
      const body = (await res.clone().json().catch(() => null)) as { error?: unknown } | null;
      if (!body || typeof body.error !== "string") {
        previewUnsupported = true;
        throw new PreviewUnsupportedError();
      }
    }
    if (!res.ok) throw await readAgentError(res, "Error tomando la vista previa");
    return res.json();
  },

  // Sesión de "espejar para capturas" (iOS + AirPlay): conecta una vez, permite marcar
  // varios momentos mientras se navega libremente, y extrae un PNG por marca al finalizar.
  async startAirplayShot(serial: string): Promise<{ receiver_name: string }> {
    let res: Response;
    try {
      res = await fetch(`${AGENT_URL}/devices/${serial}/screenshot/airplay/start`, { method: "POST" });
    } catch {
      throw new Error(AGENT_UNREACHABLE);
    }
    if (!res.ok) throw await readAgentError(res, "Error iniciando sesión de captura AirPlay");
    return res.json();
  },

  async markAirplayShot(serial: string): Promise<{ count: number }> {
    let res: Response;
    try {
      res = await fetch(`${AGENT_URL}/devices/${serial}/screenshot/airplay/mark`, { method: "POST" });
    } catch {
      throw new Error(AGENT_UNREACHABLE);
    }
    if (!res.ok) throw await readAgentError(res, "Error marcando captura");
    return res.json();
  },

  async stopAirplayShot(
    serial: string,
    attribution?: AgentAttribution,
  ): Promise<{ files: { filename: string; url: string }[] }> {
    let res: Response;
    try {
      const q = attributionParams(attribution).toString();
      res = await fetch(`${AGENT_URL}/devices/${serial}/screenshot/airplay/stop?${q}`, { method: "POST" });
    } catch {
      throw new Error(AGENT_UNREACHABLE);
    }
    if (!res.ok) throw await readAgentError(res, "Error finalizando sesión de captura AirPlay");
    return res.json();
  },

  /**
   * Activa el Modo Desarrollador del iPhone desde Tatana (ios-herramientas-windows §4.3).
   * Puede tardar hasta ~1 min. Error: `Error` con el `error` de Tatana.
   */
  async enableIosDeveloperMode(serial: string): Promise<{ status: IosDeveloperModeStatus }> {
    let res: Response;
    try {
      res = await fetch(`${AGENT_URL}/devices/${encodeURIComponent(serial)}/ios/developer-mode`, { method: "POST" });
    } catch {
      throw new Error(AGENT_UNREACHABLE);
    }
    if (!res.ok) throw await readAgentError(res, "No se pudo activar el Modo Desarrollador del iPhone");
    return res.json();
  },

  /**
   * Inicia la grabación. Con `recording_state_v1` vuelve recién cuando el celular
   * empezó a capturar (`started_at` = inicio real). Errores: `AgentError`
   * (`already_recording` 409, `recording_start_timeout` 504, `invalid_case_id` 400…).
   */
  async startRecording(
    serial: string,
    androidVersion: number,
    platform: "android" | "ios" = "android",
    iosMode?: IosRecordMode,
    androidWithMic?: boolean,
    caseId?: string | null,
    opts?: ScreenCheckOptions,
  ): Promise<{ filename?: string; state?: string; started_at?: string; case_id?: string }> {
    return agentJSON(`/devices/${encodeURIComponent(serial)}/record/start`, jsonInit("POST", {
      android_version: androidVersion, platform, ios_mode: iosMode,
      android_with_mic: androidWithMic,
      // `case_id` (grabacion-android-robusta) + `origin_id` de esta pestaña (wizard-concurrencia-pestanas).
      ...attributionBody({ caseId }),
      // "Grabar igual" (pulido-post-qa-final §6.1): solo si viene; un Tatana sin el chequeo lo ignora.
      ...(opts?.skipScreenCheck ? { skip_screen_check: true } : {}),
    }));
  },

  /**
   * Detiene la grabación. `variants_expected`/`variants` faltan en un Tatana
   * anterior a `video_postprocess_v1`; `validation`, en uno anterior a
   * `recording_state_v1` y en iOS. No-2xx: `AgentError` con `code` y `body.validation`.
   */
  async stopRecording(
    serial: string,
    platform: "android" | "ios" = "android",
    attribution?: AgentAttribution | null,
  ): Promise<RecordingStoppedData> {
    // Tatana usa la atribución del inicio; esta solo cuenta si no la tiene registrada.
    const q = attributionParams(attribution);
    if (platform === "ios") q.set("platform", "ios");
    const qs = q.toString();
    return agentJSON<RecordingStoppedData>(`/devices/${encodeURIComponent(serial)}/record/stop${qs ? `?${qs}` : ""}`, { method: "POST" });
  },

  /**
   * Grabaciones en curso y detenidas sin importar (`GET /agent/state`). `null`
   * ante cualquier error o si Tatana no manda ninguno de los dos campos (viejo).
   */
  async agentState(): Promise<AgentRecordingState | null> {
    try {
      const r = await agentJSON<Partial<AgentRecordingState>>("/agent/state", { signal: AbortSignal.timeout(3000) });
      if (!r || typeof r !== "object") return null;
      const hasRec = Array.isArray(r.recordings);
      const hasUnclaimed = Array.isArray(r.unclaimed_recordings);
      if (!hasRec && !hasUnclaimed) return null;
      return {
        recordings: hasRec ? r.recordings! : [],
        unclaimed_recordings: hasUnclaimed ? r.unclaimed_recordings! : [],
      };
    } catch {
      return null;
    }
  },

  /**
   * Operaciones en curso de Tatana (`GET /agent/state` → `operations`). `null`
   * ante cualquier error o si la respuesta no trae `operations` (array). Timeout 3 s.
   */
  async agentOperations(): Promise<AgentOperation[] | null> {
    try {
      const res = await fetch(`${AGENT_URL}/agent/state`, { cache: "no-store", signal: AbortSignal.timeout(3000) });
      if (!res.ok) return null;
      const body = (await res.json()) as { operations?: unknown } | null;
      return Array.isArray(body?.operations) ? (body.operations as AgentOperation[]) : null;
    } catch {
      return null;
    }
  },

  /**
   * `true` si Tatana expone el estado de grabación (`recording_state_v1`). Se
   * cachea por conexión: `resetCapabilities()` lo invalida (en `onConnected`).
   * Un error de red no se cachea.
   */
  async supportsRecordingState(): Promise<boolean> {
    return this.hasCapability("recording_state_v1");
  },

  /** `true` si Tatana sabe copiar los sellos de tiempo a `sellos/` (`case_seals_v1`). Mismo caché. */
  async supportsCaseSeals(): Promise<boolean> {
    return this.hasCapability("case_seals_v1");
  },

  /** `true` si Tatana lee la ficha técnica y el reloj del dispositivo (`device_details_v1`). Mismo caché. */
  async supportsDeviceDetails(): Promise<boolean> {
    return this.hasCapability("device_details_v1");
  },

  /**
   * Ficha técnica de solo lectura + reloj (`GET /devices/{serial}/details`).
   * El timeout (45 s) lo pone el llamador con `signal`. Errores: `AgentError`
   * (`device_not_found` 404, códigos de iPhone…).
   */
  async getDeviceDetails(serial: string, platform: "android" | "ios", signal?: AbortSignal): Promise<AgentDeviceDetails> {
    const q = new URLSearchParams({ platform }).toString();
    return agentJSON<AgentDeviceDetails>(`/devices/${encodeURIComponent(serial)}/details?${q}`, { signal });
  },

  /** Solo el reloj (medición de cierre al generar). Timeout 15 s si no viene `signal`. */
  async getDeviceClock(serial: string, platform: "android" | "ios", signal?: AbortSignal): Promise<AgentDeviceClockResponse> {
    const q = new URLSearchParams({ platform }).toString();
    return agentJSON<AgentDeviceClockResponse>(`/devices/${encodeURIComponent(serial)}/clock?${q}`, {
      signal: signal ?? AbortSignal.timeout(15_000),
    });
  },

  /* ── Grabación de la sesión completa (grabacion-sesion-completa, SDD §7.2/§9.1) ── */

  /** `recording_session_v1`: sesión con pantalla de Android (fase 1). Mismo caché que el resto. */
  async supportsRecordingSession(): Promise<boolean> {
    return this.hasCapability("recording_session_v1");
  },

  /** `recording_session_camera_v1`: cámara del procedimiento (fase 2). */
  async supportsSessionCamera(): Promise<boolean> {
    return this.hasCapability("recording_session_camera_v1");
  },

  /** `recording_session_ios_v1`: sesión en iPhone (fase 2). */
  async supportsSessionIos(): Promise<boolean> {
    return this.hasCapability("recording_session_ios_v1");
  },

  /**
   * Espacio para grabar la sesión en esta PC (`GET /session/capacity`). Timeout 5 s.
   * `segmentMinutes`: tramo de la instalación (QA B-6); sin él, Tatana asume 30 min.
   */
  async sessionCapacity(camera = false, segmentMinutes?: number): Promise<SessionCapacity> {
    const q = new URLSearchParams({
      camera: String(camera),
      ...(typeof segmentMinutes === "number" ? { segment_minutes: String(segmentMinutes) } : {}),
    }).toString();
    return agentJSON<SessionCapacity>(`/session/capacity?${q}`, { signal: AbortSignal.timeout(5000) });
  },

  /**
   * Empieza (o reanuda) la grabación de la sesión del caso. Vuelve cuando el
   * celular empezó a capturar. Idempotente con el mismo caso y celular.
   * Errores: `AgentError` (`session_active`, `already_recording`, `screen_off`,
   * `screen_locked`, `insufficient_storage` 507, `recording_start_timeout` 504…).
   */
  async startSession(caseId: string, body: SessionStartRequest): Promise<SessionStateDto> {
    return agentJSON<SessionStateDto>(`${caseBase(caseId)}/session/start`, jsonInit("POST", {
      ...body, origin_id: getTabOriginId(),
    }));
  },

  /**
   * Cierra la sesión del caso. Responde después de guardar los tramos (puede
   * tardar: hash). Idempotente: sin sesión activa devuelve el estado del diario.
   */
  async stopSession(caseId: string, reason: SessionStopReason): Promise<SessionStateDto> {
    return agentJSON<SessionStateDto>(`${caseBase(caseId)}/session/stop`, {
      ...jsonInit("POST", { reason, origin_id: getTabOriginId() }),
      signal: AbortSignal.timeout(90_000),
    });
  },

  /** "Marcar momento" (§7.2.4). 409 `session_not_active` sin sesión grabando para el caso. */
  async addSessionMark(caseId: string, text: string | null): Promise<SessionMarkDto> {
    return agentJSON<SessionMarkDto>(`${caseBase(caseId)}/session/marks`, jsonInit("POST", {
      ...(text ? { text } : {}), origin_id: getTabOriginId(),
    }));
  },

  /* ── Cámara del procedimiento (grabacion-sesion-completa-f2 §7.2) ── */

  /** Cámaras de esta PC (`GET /cameras`). `refresh` saltea la caché de 10 s de Tatana. */
  async listCameras(refresh = false): Promise<CameraListResponse> {
    const r = await agentJSON<Partial<CameraListResponse>>(`/cameras${refresh ? "?refresh=true" : ""}`, {
      signal: AbortSignal.timeout(15_000),
    });
    return {
      cameras: Array.isArray(r?.cameras) ? r.cameras.filter(c => c && typeof c.name === "string") : [],
      error: typeof r?.error === "string" && r.error.trim() ? r.error.trim() : null,
    };
  },

  /** Enciende la vista previa (antes de grabar). `ok: false` si la cámara no abre; 409 `camera_busy`. */
  async startCameraPreview(id: string, name: string): Promise<CameraPreviewResult> {
    return agentJSON<CameraPreviewResult>("/cameras/preview", {
      ...jsonInit("POST", { id, name }),
      signal: AbortSignal.timeout(15_000),
    });
  },

  /** Corta la vista previa. Idempotente (204). */
  async stopCameraPreview(): Promise<void> {
    await agentFetch("/cameras/preview", { method: "DELETE", signal: AbortSignal.timeout(5000) });
  },

  /** URL de la imagen de la vista previa, con `?t=` para no usar la caché. */
  cameraPreviewURL(): string {
    return `${AGENT_URL}/cameras/preview.jpg?t=${Date.now()}`;
  },

  /** Suma la cámara a la sesión en curso del caso. Una cámara que no abre vuelve 200 con `camera_state: "failed"`. */
  async startSessionCamera(caseId: string, cam: SessionCameraRequest): Promise<SessionStateDto> {
    return agentJSON<SessionStateDto>(`${caseBase(caseId)}/session/camera/start`, {
      ...jsonInit("POST", { ...cam, origin_id: getTabOriginId() }),
      signal: AbortSignal.timeout(15_000),
    });
  },

  /** Saca la cámara de la sesión (queda un hueco de cámara). */
  async stopSessionCamera(caseId: string): Promise<SessionStateDto> {
    return agentJSON<SessionStateDto>(`${caseBase(caseId)}/session/camera/stop`, {
      ...jsonInit("POST", { origin_id: getTabOriginId() }),
      signal: AbortSignal.timeout(15_000),
    });
  },

  /** "Reintentar cámara": vuelve a abrir la última cámara pedida en la sesión. */
  async retrySessionCamera(caseId: string): Promise<SessionStateDto> {
    return agentJSON<SessionStateDto>(`${caseBase(caseId)}/session/camera/retry`, {
      ...jsonInit("POST", { origin_id: getTabOriginId() }),
      signal: AbortSignal.timeout(15_000),
    });
  },

  /** Diario de la sesión del caso en esta PC. `null` si no hay (404). Otros errores: `AgentError`. */
  async getCaseSession(caseId: string): Promise<CaseSessionDto | null> {
    try {
      return await agentJSON<CaseSessionDto>(`${caseBase(caseId)}/session`, { signal: AbortSignal.timeout(5000) });
    } catch (e) {
      if (e instanceof AgentError && e.kind === "http" && e.status === 404) return null;
      throw e;
    }
  },

  /**
   * Sesión activa en Tatana (`/agent/state.recording_session`, se omite si no
   * hay) y `session_recovery`. `null` ante cualquier error.
   */
  async agentRecordingSession(): Promise<AgentSessionState | null> {
    try {
      const r = await agentJSON<{ recording_session?: SessionStateDto | null; session_recovery?: string | null }>(
        "/agent/state", { signal: AbortSignal.timeout(3000) },
      );
      if (!r || typeof r !== "object") return null;
      const rs = r.recording_session;
      return {
        recording_session: rs && typeof rs === "object" && typeof rs.state === "string" ? rs : null,
        session_recovery: r.session_recovery === "running" || r.session_recovery === "done" ? r.session_recovery : null,
      };
    } catch {
      return null;
    }
  },

  /** Capability de `/health`, cacheada por conexión. Un error de red da `false` y no se cachea. */
  async hasCapability(name: string): Promise<boolean> {
    if (capabilitiesCache) return capabilitiesCache.includes(name);
    try {
      const h = await this.health();
      capabilitiesCache = Array.isArray(h.capabilities) ? h.capabilities : [];
      return capabilitiesCache.includes(name);
    } catch {
      return false;
    }
  },

  /** Olvida las capabilities cacheadas (Tatana reconectado: puede ser otra versión). */
  resetCapabilities(): void {
    capabilitiesCache = null;
    // Tatana reconectado: puede ser una versión con vista previa.
    previewUnsupported = false;
  },

  async captureWebcam(type: "funcionario" | "denunciante", attribution?: AgentAttribution): Promise<{ filename: string; url: string }> {
    const res = await fetch(`${AGENT_URL}/webcam/capture`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ type, ...attributionBody(attribution) }),
    });
    if (!res.ok) throw new Error("Error capturando foto");
    return res.json();
  },

  /**
   * Estado del post-proceso de las grabaciones pedidas (SDD §4.4), en lotes de 50.
   * Cualquier error (404 de un Tatana viejo incluido) devuelve `null` sin tirar:
   * el estado de las variantes es solo visual.
   */
  async videoPostprocessStatus(filenames: string[]): Promise<VideoPostprocessStatusResponse | null> {
    if (filenames.length === 0) return { items: [] };
    try {
      const items: VideoPostprocessStatusResponse["items"] = [];
      for (let i = 0; i < filenames.length; i += POSTPROCESS_STATUS_BATCH) {
        const batch = filenames.slice(i, i + POSTPROCESS_STATUS_BATCH);
        const query = batch.map(f => `filename=${encodeURIComponent(f)}`).join("&");
        const res = await fetch(`${AGENT_URL}/video-postprocess/status?${query}`, {
          cache: "no-store",
          signal: AbortSignal.timeout(5000),
        });
        if (!res.ok) return null;
        const data = (await res.json()) as Partial<VideoPostprocessStatusResponse>;
        if (!Array.isArray(data.items)) return null;
        items.push(...data.items);
      }
      return { items };
    } catch {
      return null;
    }
  },

  async listFiles(): Promise<AgentFile[]> {
    const res = await fetch(`${AGENT_URL}/files`);
    if (!res.ok) throw new Error("Error listando archivos");
    const data = await res.json();
    return data.files || [];
  },

  /**
   * Descarga un archivo del agente como Blob para subirlo al backend. Con XHR
   * para tener progreso (`onProgress`, `total` = null si el agente no manda
   * `Content-Length`) y cancelación (`signal`: rechaza con `DOMException("AbortError")`).
   * Falla de red o estado no-2xx: rechaza con `Error`.
   */
  downloadFile(
    filename: string,
    opts: { signal?: AbortSignal; onProgress?: (loaded: number, total: number | null) => void } = {},
  ): Promise<Blob> {
    const { signal, onProgress } = opts;
    return new Promise<Blob>((resolve, reject) => {
      if (signal?.aborted) { reject(new DOMException("Descarga cancelada", "AbortError")); return; }
      const xhr = new XMLHttpRequest();
      const onAbortSignal = () => xhr.abort();
      const done = () => signal?.removeEventListener("abort", onAbortSignal);
      xhr.open("GET", `${AGENT_URL}/files/${filename}`);
      xhr.responseType = "blob";
      if (onProgress) xhr.onprogress = e => onProgress(e.loaded, e.lengthComputable ? e.total : null);
      xhr.onload = () => {
        done();
        if (xhr.status >= 200 && xhr.status < 300) resolve(xhr.response as Blob);
        else reject(new Error(`Error descargando ${filename} (${xhr.status})`));
      };
      xhr.onerror = () => { done(); reject(new Error(`Error descargando ${filename}`)); };
      xhr.onabort = () => { done(); reject(new DOMException("Descarga cancelada", "AbortError")); };
      signal?.addEventListener("abort", onAbortSignal, { once: true });
      xhr.send();
    });
  },

  /** Elimina un archivo capturado del agente (best-effort). */
  async deleteFile(filename: string): Promise<void> {
    await fetch(`${AGENT_URL}/files/${filename}`, { method: "DELETE" });
  },

  /** Explorador de archivos del dispositivo (Android): lista el contenido de una carpeta remota. */
  async listDeviceFiles(serial: string, path: string): Promise<DeviceFileEntry[]> {
    const res = await fetch(`${AGENT_URL}/devices/${serial}/explorer?path=${encodeURIComponent(path)}`);
    if (!res.ok) {
      const data = await res.json().catch(() => ({}));
      throw new Error(data.error || "Error listando la carpeta");
    }
    const data = await res.json();
    return data.entries || [];
  },

  /** Búsqueda global en el dispositivo por app (whatsapp | instagram | facebook). */
  async searchDeviceFilesByApp(serial: string, app: "whatsapp" | "instagram" | "facebook"): Promise<DeviceFileEntry[]> {
    const res = await fetch(`${AGENT_URL}/devices/${serial}/explorer/search?app=${app}`);
    if (!res.ok) throw new Error("Error buscando archivos");
    const data = await res.json();
    return data.entries || [];
  },

  /** Trae N archivos del dispositivo a la evidencia (quedan disponibles como /files/{filename}). */
  async pullDeviceFiles(serial: string, paths: string[]): Promise<PulledFile[]> {
    const res = await fetch(`${AGENT_URL}/devices/${serial}/explorer/pull`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ paths }),
    });
    if (!res.ok) throw new Error("Error trayendo archivos del dispositivo");
    const data = await res.json();
    return data.files || [];
  },

  /* ── zip-local-informe-servidor (SDD §7.1) ── */

  async health(): Promise<AgentHealth> {
    return agentJSON<AgentHealth>("/health", { signal: AbortSignal.timeout(3000) });
  },

  /** `true` si este Tatana sabe guardar evidencia por caso (`case_evidence_v1`). */
  async supportsCaseEvidence(): Promise<boolean> {
    const h = await this.health();
    return h.capabilities?.includes("case_evidence_v1") ?? false;
  },

  /** Mueve un archivo de la raíz de Tatana a la carpeta del caso y calcula su SHA-256 (idempotente). */
  async importEvidence(caseId: string, filename: string): Promise<EvidenceFileInfo> {
    return agentJSON<EvidenceFileInfo>(`${caseBase(caseId)}/evidence/import`, jsonInit("POST", { filename }));
  },

  /** Prechequeo de una subida del navegador: tope y espacio libre en esta PC. */
  async checkEvidenceUpload(caseId: string, filename: string, size: number, signal?: AbortSignal): Promise<{ max_upload_bytes: number }> {
    const q = new URLSearchParams({ filename, size: String(size) }).toString();
    return agentJSON(`${caseBase(caseId)}/evidence/upload-check?${q}`, { signal });
  },

  /**
   * Copia un `Blob` del navegador a la carpeta del caso (cuerpo crudo) con XHR:
   * progreso real, cancelación con `signal` y errores `AgentError`.
   */
  uploadEvidence(
    caseId: string,
    filename: string,
    blob: Blob,
    opts: { signal?: AbortSignal; onProgress?: (loaded: number, total: number) => void } = {},
  ): Promise<EvidenceFileInfo> {
    const { signal, onProgress } = opts;
    return new Promise<EvidenceFileInfo>((resolve, reject) => {
      if (signal?.aborted) { reject(new AgentError("aborted")); return; }
      const xhr = new XMLHttpRequest();
      const onAbortSignal = () => xhr.abort();
      const done = () => signal?.removeEventListener("abort", onAbortSignal);
      xhr.open("POST", `${AGENT_URL}${caseBase(caseId)}/evidence/upload?${new URLSearchParams({ filename }).toString()}`);
      xhr.timeout = 0;
      if (onProgress) xhr.upload.onprogress = e => onProgress(e.loaded, e.lengthComputable ? e.total : blob.size);
      xhr.onload = () => {
        done();
        if (xhr.status >= 200 && xhr.status < 300) {
          try { resolve(JSON.parse(xhr.responseText) as EvidenceFileInfo); }
          catch { reject(new AgentError("http", xhr.status, null)); }
          return;
        }
        reject(new AgentError("http", xhr.status, parseAgentErrorBody(xhr.responseText)));
      };
      xhr.onerror = () => { done(); reject(new AgentError("unreachable")); };
      xhr.ontimeout = () => { done(); reject(new AgentError("unreachable")); };
      xhr.onabort = () => { done(); reject(new AgentError("aborted")); };
      signal?.addEventListener("abort", onAbortSignal, { once: true });
      xhr.send(blob);
    });
  },

  async listCaseFiles(caseId: string, signal?: AbortSignal): Promise<AgentCaseFiles> {
    const r = await agentJSON<Partial<AgentCaseFiles>>(`${caseBase(caseId)}/files`, { signal });
    return { directory: r.directory ?? "", files: Array.isArray(r.files) ? r.files : [] };
  },

  /** URL de un archivo de la carpeta del caso (vista previa en `<img>`/`<video>`). */
  caseFileURL(caseId: string, filename: string): string {
    return `${AGENT_URL}${caseBase(caseId)}/files/${encodeURIComponent(filename)}`;
  },

  async getCaseFileBlob(caseId: string, filename: string, signal?: AbortSignal): Promise<Blob> {
    const res = await agentFetch(`${caseBase(caseId)}/files/${encodeURIComponent(filename)}`, { signal });
    try {
      return await res.blob();
    } catch {
      if (signal?.aborted) throw new AgentError("aborted");
      throw new AgentError("unreachable");
    }
  },

  async deleteCaseFile(caseId: string, filename: string): Promise<void> {
    await agentFetch(`${caseBase(caseId)}/files/${encodeURIComponent(filename)}`, { method: "DELETE" });
  },

  /** Arma y verifica el ZIP provisorio. Sin timeout: con GB de evidencia tarda minutos. */
  async buildZip(caseId: string, body: BuildZipRequest, signal?: AbortSignal): Promise<BuildZipResponse> {
    return agentJSON<BuildZipResponse>(`${caseBase(caseId)}/zip`, { ...jsonInit("POST", body), signal });
  },

  async commitZip(caseId: string, body: CommitZipRequest): Promise<CommitZipResponse> {
    return agentJSON<CommitZipResponse>(`${caseBase(caseId)}/zip/commit`, jsonInit("POST", body));
  },

  /** Copia los `.tsr` y el LEEME a `<carpeta del caso>/sellos/` (idempotente). Requiere `case_seals_v1`. */
  async writeCaseSeals(caseId: string, body: WriteSealsRequest): Promise<WriteSealsResponse> {
    return agentJSON<WriteSealsResponse>(`${caseBase(caseId)}/zip/seals`, jsonInit("PUT", body));
  },

  async discardPendingZip(caseId: string, caseRef: string, zipFilename: string): Promise<void> {
    await agentFetch(`${caseBase(caseId)}/zip/pending?${zipQuery(caseRef, zipFilename)}`, { method: "DELETE" });
  },

  async zipStatus(caseId: string, caseRef: string, zipFilename: string): Promise<ZipStatus> {
    return agentJSON<ZipStatus>(`${caseBase(caseId)}/zip/status?${zipQuery(caseRef, zipFilename)}`);
  },

  /** Abre el Explorador/Finder con el ZIP seleccionado. */
  async revealZip(caseId: string, caseRef: string, zipFilename: string): Promise<void> {
    await agentFetch(`${caseBase(caseId)}/zip/reveal?${zipQuery(caseRef, zipFilename)}`, { method: "POST" });
  },

  /** "Guardar una copia…": Tatana lo sirve como `attachment`. */
  zipFileURL(caseId: string, caseRef: string, zipFilename: string): string {
    return `${AGENT_URL}${caseBase(caseId)}/zip/file?${zipQuery(caseRef, zipFilename)}`;
  },

  /** Conecta al WebSocket del agente para eventos en tiempo real. */
  connectWS(onEvent: (event: AgentEvent) => void): WebSocket {
    const wsURL = AGENT_URL.replace("http", "ws") + "/ws";
    const ws = new WebSocket(wsURL);
    ws.onmessage = (e) => {
      try {
        onEvent(JSON.parse(e.data));
      } catch {}
    };
    ws.onerror = () => console.warn("Agent WS error");
    return ws;
  },
};
