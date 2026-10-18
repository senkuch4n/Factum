/**
 * Cliente del Backend (corre en el servidor, accesible desde la web).
 */

import { agent } from "./agent";
import type { EvidenceErrorCode, UploadCheckResponse, UploadedFileInfo, UploadErrorBody, UploadErrorCode } from "@/types";
import type { AuthResponse } from "@/types";
import {
  AUTH_EVENT, SESSION_EXPIRED_CODES, beginLongOp, clearSession, dispatchAuthEvent, endLongOp, endSession,
  expireSession, getStoredToken, hadSession, hasActiveLongOps, sessionState, setSessionUser, writeSession,
  type AuthEventDetail,
} from "./session";
import type { ClockReason, DeviceDetailKey, DeviceDetailReason, PcServerReason } from "./device-details";

const BACKEND_URL = process.env.NEXT_PUBLIC_BACKEND_URL || "http://localhost:8080";

export type AgentAuditAction = "startup" | "capture_start" | "capture_stop" | "screenshot" | "webcam" | "capture_failed";
/** Acción que falló en un `capture_failed` (wizard-concurrencia-pestanas DT11). */
export type AgentFailedAction = Exclude<AgentAuditAction, "startup" | "capture_failed">;

function getToken(): string | null {
  return getStoredToken();
}

/**
 * Error de la API. `missing` trae las claves de obligatorios faltantes
 * (`{ error, missing }` del backend; ver `lib/pericial.ts`).
 * `serverMessage` es el `error` del body tal cual vino (o `null` si el body
 * no era JSON o no traía un `error` de texto); lo usa el login para no
 * mostrar textos genéricos como "HTTP 500".
 */
export class ApiError extends Error {
  status: number;
  missing?: string[];
  serverMessage: string | null;
  /** `code` del cuerpo de error (zip-local-informe-servidor §5.1), o `null`. */
  code: string | null;
  /** Cuerpo de error tal cual vino (JSON), o `null`. */
  body: ApiErrorBody | null;
  /** Header `Retry-After` en segundos (429 del refresh, sesion-larga §8.3), si vino. */
  retryAfterSeconds?: number;
  constructor(
    message: string, status: number, missing?: string[], serverMessage: string | null = null,
    body: ApiErrorBody | null = null,
  ) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.missing = missing;
    this.serverMessage = serverMessage;
    this.body = body;
    this.code = typeof body?.code === "string" ? body.code : null;
  }
}

/** Cuerpo de error del backend: claves literales en snake_case. */
export interface ApiErrorBody {
  error?: string;
  code?: EvidenceErrorCode | UploadErrorCode | string;
  missing?: string[];
  /** `evidence_on_other_pc`. */
  evidence_hostname?: string;
  /** `invalid_manifest`, `image_hash_mismatch`. */
  filename?: string;
  /** `manifest_mismatch`. */
  mismatched?: string[];
  /** `missing_images`. */
  missing_images?: string[];
  /** `request_too_large`. */
  max_bytes?: number;
  /**
   * Campo al que pertenece el error: `change-password` (usuarios-locales §5.3)
   * o el panel de cuentas (abm-clientes §8.1).
   */
  field?: ChangePasswordField | AdminUserField | BrandingField;
  /** Índice de `contact_lines` en el array enviado (marca-por-cliente §8.1). */
  index?: number;
  /** Contraste del color rechazado, truncado a 1 decimal (marca-por-cliente §5). */
  contrast?: number;
  /** Contraste mínimo exigido (4.5). */
  min_contrast?: number;
  /** `dni_taken` del alta de cuentas (abm-clientes §8.1): id de la cuenta que ya tiene ese DNI. */
  existing_user_id?: string;
  /** 409 `recording_session_open` del `prepare` (grabacion-sesion-completa §6.5). */
  pending_files?: string[];
}

/* ── Corte de sesión (usuarios-locales §9.1) ── */

/** Evento global que escucha `SessionWatcher` (definido en `lib/session.ts`). */
export { AUTH_EVENT };
export type { AuthEventDetail };

/**
 * Rutas cuyo 401 no corta la sesión acá: login/mode son resultados del
 * formulario, y el refresh lo maneja `SessionManager` (§8.3).
 */
const AUTH_FAILURE_EXCLUDED = ["/api/auth/login", "/api/auth/mode", "/api/auth/refresh"];

/**
 * Operaciones largas en curso (subida, generación, registro de evidencia).
 * Si la sesión se corta mientras alguna corre, el aviso del login lo dice; y
 * mientras corren, cuentan como actividad para renovar la sesión (S1 = A).
 */
async function trackLongOp<T>(op: () => Promise<T>): Promise<T> {
  beginLongOp();
  try {
    return await op();
  } finally {
    endLongOp();
  }
}

/** Hay una subida, generación o registro de evidencia en curso. */
export { hasActiveLongOps };

/**
 * Interceptor de sesión (sesion-larga §8.3): lo llaman `toApiError`, el XHR
 * de `uploadFile` y el refresh (con `force`).
 * - 401 de un pedido que salió con un token ya reemplazado: se ignora (D14).
 * - 401 sin token: solo corta si esta pestaña tuvo sesión y no está ya vencida
 *   (el "Salir" de otra pestaña, S2).
 * - 401 `invalid_token`/`session_max_age`: sesión vencida → `expireSession`
 *   (re-login en el lugar dentro del asistente, login fuera).
 * - Otro 401 (`session_revoked`, `account_suspended`, …): corte "terminada", como antes.
 * - 403 `password_change_required`: avisa a `SessionWatcher` (→ `/cambiar-contrasena`).
 * Se evalúa antes del `finally` de la operación que falló, así
 * `hasActiveLongOps()` incluye a esa misma operación.
 */
function notifyAuthFailure(
  status: number, body: ApiErrorBody | null, url: string, sentToken: string | null,
  opts: { force?: boolean } = {},
) {
  if (typeof window === "undefined") return;
  const code = typeof body?.code === "string" ? body.code : null;
  if (status === 401) {
    if (!opts.force) {
      let path = url;
      try { path = new URL(url, BACKEND_URL).pathname; } catch { /* url relativa o vacía */ }
      if (AUTH_FAILURE_EXCLUDED.some(p => path.startsWith(p))) return;
    }
    const current = getToken();
    if (sentToken && current && current !== sentToken) return;
    if (!current && !sentToken) {
      if (sessionState() === "expired" || !hadSession()) return;
      endSession("session_ended");
      return;
    }
    if (code && SESSION_EXPIRED_CODES.includes(code)) {
      expireSession("http");
      return;
    }
    endSession(code === "account_suspended" ? "account_suspended" : "session_ended");
    return;
  }
  if (status === 403 && code === "password_change_required") {
    dispatchAuthEvent({ type: "password_change_required" });
  }
}

/** Texto cuando el backend no responde (nunca "Failed to fetch"). */
export const SERVER_UNREACHABLE = "No se pudo conectar con el servidor de Factum. Revisá la conexión y reintentá.";

/**
 * Error de las rutas de subida (`upload-check` y `POST …/files`).
 * - `kind: "http"`: el backend respondió con un código de error (`status`, `code`, `body`).
 * - `kind: "network"`: no hubo respuesta (corte, backend caído, CORS); `status` = 0.
 * - `kind: "aborted"`: el usuario canceló; `status` = 0.
 * `message` nunca es "Failed to fetch" ni "HTTP 413": la UI arma el texto con
 * `lib/upload-messages.ts`.
 */
export class UploadError extends ApiError {
  kind: "http" | "network" | "aborted";
  code: UploadErrorCode | null;
  body: UploadErrorBody | null;
  constructor(kind: "http" | "network" | "aborted", status = 0, body: UploadErrorBody | null = null) {
    const serverMessage = typeof body?.error === "string" && body.error.trim() ? body.error : null;
    const fallback =
      kind === "aborted" ? "Envío cancelado"
      : kind === "network" ? "Se cortó la conexión con el servidor"
      : "El servidor rechazó el archivo";
    super(serverMessage ?? fallback, kind === "http" ? status : 0, undefined, serverMessage, body);
    this.name = "UploadError";
    this.kind = kind;
    this.code = body?.code ?? null;
    this.body = body;
  }
}

/** Lee un cuerpo de error de subida; `null` si no es JSON con `error`. */
function parseUploadErrorBody(text: string): UploadErrorBody | null {
  try {
    const b = JSON.parse(text) as unknown;
    if (b && typeof b === "object" && typeof (b as { error?: unknown }).error === "string") return b as UploadErrorBody;
  } catch { /* cuerpo vacío o no JSON (ej. 403) */ }
  return null;
}

/**
 * `fetch` con el token vigente. Ante un 401, si mientras el pedido viajaba
 * otra pestaña (o esta) dejó un token **distinto y no nulo**, reintenta una
 * vez con ese (D14). Los cuerpos que se usan (string, `FormData`, `Blob`) se
 * pueden reenviar.
 */
async function authFetch(url: string, init: RequestInit = {}): Promise<{ res: Response; sentToken: string | null }> {
  const attempt = (token: string | null) => {
    const headers = new Headers(init.headers);
    if (token) headers.set("Authorization", `Bearer ${token}`);
    else headers.delete("Authorization");
    return fetch(url, { ...init, headers });
  };
  let sentToken = getToken();
  let res = await attempt(sentToken);
  if (res.status === 401 && sentToken) {
    const current = getToken();
    if (current && current !== sentToken) {
      sentToken = current;
      res = await attempt(current);
    }
  }
  return { res, sentToken };
}

/** Respuesta del backend + el token con el que salió (para `toApiError`). */
type Sent = { res: Response; sentToken: string | null };

/** `fetch` al backend con el token y el `Content-Type` JSON. */
function send(path: string, options: RequestInit): Promise<Sent> {
  return authFetch(`${BACKEND_URL}${path}`, {
    ...options,
    headers: { "Content-Type": "application/json", ...options.headers },
  });
}

/** `ApiError` a partir de una respuesta no-OK (`{ error, missing }` del backend). */
async function toApiError(res: Response, sentToken: string | null): Promise<ApiError> {
  const body = await res.json().catch(() => null);
  const serverMessage = typeof body?.error === "string" && body.error.trim() ? (body.error as string) : null;
  const missing = Array.isArray(body?.missing) ? (body.missing as string[]) : undefined;
  // `message` igual que antes: el `error` del body, o el statusText si el body
  // no era JSON, o "HTTP <status>" como último recurso.
  const fallback = body === null ? res.statusText : body?.error;
  const errBody = body && typeof body === "object" ? (body as ApiErrorBody) : null;
  notifyAuthFailure(res.status, errBody, res.url, sentToken);
  const err = new ApiError(serverMessage ?? (fallback || `HTTP ${res.status}`), res.status, missing, serverMessage, errBody);
  const retryAfter = Number(res.headers.get("Retry-After"));
  if (res.headers.has("Retry-After") && Number.isFinite(retryAfter) && retryAfter >= 0) err.retryAfterSeconds = retryAfter;
  return err;
}

/**
 * Como `request`, pero una falla de red (backend caído, CORS) llega como
 * `ApiError` con `status` 0 y `SERVER_UNREACHABLE`. Lo usan las rutas nuevas.
 */
async function requestSafe<T>(path: string, options: RequestInit = {}): Promise<T> {
  let sent: Sent;
  try {
    sent = await send(path, options);
  } catch (e) {
    // Un abort (el llamador canceló con `signal`) se propaga tal cual para que
    // el componente lo distinga de un corte real y lo ignore; no es un error de red.
    if (e instanceof DOMException && e.name === "AbortError") throw e;
    throw new ApiError(SERVER_UNREACHABLE, 0, undefined, null);
  }
  if (!sent.res.ok) throw await toApiError(sent.res, sent.sentToken);
  return sent.res.json();
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const { res, sentToken } = await send(path, options);
  if (!res.ok) throw await toApiError(res, sentToken);
  return res.json();
}

/** Como `request`, para respuestas `204` sin body (no lee el body si salió bien). */
async function requestNoContent(path: string, options: RequestInit = {}): Promise<void> {
  const { res, sentToken } = await send(path, options);
  if (!res.ok) throw await toApiError(res, sentToken);
}

/**
 * Envío `multipart/form-data` con el token, sin `Content-Type` manual (lo pone
 * el navegador con el boundary). Red caída → `ApiError(SERVER_UNREACHABLE, 0)`.
 */
async function sendMultipart<T>(path: string, method: "POST" | "PUT", form: FormData): Promise<T> {
  let sent: Sent;
  try {
    sent = await authFetch(`${BACKEND_URL}${path}`, { method, body: form, cache: "no-store" });
  } catch {
    throw new ApiError(SERVER_UNREACHABLE, 0, undefined, null);
  }
  if (!sent.res.ok) throw await toApiError(sent.res, sent.sentToken);
  return sent.res.json() as Promise<T>;
}

/* ── Catálogos de sugerencias del perito (formulario-caso-catalogos) ── */

/** Ids de catálogo: son literalmente las claves de `catalogs` en `GET /api/catalogs`. */
export type CatalogId = "destinatarios" | "partes" | "profesiones" | "tipos_dispositivo";

export interface CatalogEntry {
  id: string;
  value: string;
  use_count: number;
  /** ISO UTC. */
  last_used_at: string;
}

export interface CatalogsResponse {
  catalogs: Record<CatalogId, CatalogEntry[]>;
}

/* ── Autenticación (usuarios-locales, SDD §8.1) ── */

export type UserRole = "superadmin" | "cliente";

export interface User {
  dni: string;
  name: string;
  sigla: string;
  role: UserRole;
  must_change_password: boolean;
}

/** Respuesta de `GET /api/auth/mode` (anónimo). */
export interface AuthModeInfo {
  mode: AuthMode;
  password_min_length: number;
  password_max_length: number;
}

/** Body de `POST /api/auth/change-password`. */
export interface ChangePasswordRequest {
  current_password: string;
  new_password: string;
  new_password_confirmation: string;
}

/** Valores posibles de `field` en los errores de `change-password`. */
export type ChangePasswordField = keyof ChangePasswordRequest;

/* ── Administración de cuentas (abm-clientes, SDD §8.1) ── */

export type AdminUserStatus = "activo" | "suspendido";
export type AdminAction =
  | "create" | "update" | "suspend" | "reactivate" | "reset_password" | "unlock" | "update_branding";
export type AdminUserField =
  | "dni" | "name" | "sigla" | "contact_phone" | "contact_email" | "organization" | "notes"
  | "reason" | "expected_updated_at";
export type AdminErrorCode =
  | "validation_failed" | "user_not_found" | "dni_taken" | "stale_update" | "cannot_act_on_self"
  | "last_superadmin" | "invalid_state" | "operation_busy" | "not_available" | "superadmin_required";

/** Cuenta para el panel. Nunca trae hashes. Las fechas son ISO UTC. */
export interface AdminUser {
  id: string;
  dni: string;
  name: string;
  sigla: string;
  role: UserRole;
  status: AdminUserStatus;
  must_change_password: boolean;
  /** Solo si el bloqueo está vigente (lo resuelve el servidor). */
  locked_until: string | null;
  last_login_at: string | null;
  created_at: string;
  /** DNI o `"bootstrap"`. */
  created_by: string | null;
  created_by_name: string | null;
  /** Token del control optimista (D11): se reenvía tal cual en `expected_updated_at`. */
  updated_at: string;
  suspended_at: string | null;
  suspended_by: string | null;
  suspended_by_name: string | null;
  suspension_reason: string | null;
  contact_phone: string;
  contact_email: string;
  organization: string;
  notes: string;
  case_count: number;
}

export interface AdminCreateUserRequest {
  dni: string;
  name: string;
  sigla: string;
  contact_phone: string;
  contact_email: string;
  organization: string;
  notes: string;
}

export interface AdminUpdateUserRequest extends Omit<AdminCreateUserRequest, "dni"> {
  expected_updated_at: string;
}

export interface AdminUserChange { field: string; from: string | null; to: string | null; }

export interface AdminUserEvent {
  id: string;
  at: string;
  actor_dni: string;
  actor_name: string;
  action: AdminAction;
  changes: AdminUserChange[];
  reason: string | null;
  ip: string | null;
}

/** Respuesta del alta y del reset: la temporal solo viaja acá (D7). */
export interface AdminUserWithPassword { user: AdminUser; temporary_password: string; }

/* ── Marca del informe por cuenta (marca-por-cliente, SDD §8.1) ── */

export type BrandingImageKind = "logo" | "isotype";
export type BrandingImageAction = "keep" | "replace" | "remove";

export interface BrandingImage {
  /** Ruta relativa al backend, con ?v=<version>. Se pide con Authorization (blob). */
  url: string;
  content_type: "image/png" | "image/jpeg";
  width: number;
  height: number;
  size: number;
  /** 12 hex del SHA-256. */
  version: string;
}

export interface AccountBranding {
  /** false = la cuenta nunca guardó marca: sus informes usan la de la instalación (D4). */
  exists: boolean;
  /** "" = sin nombre. */
  organization_name: string;
  contact_lines: string[];
  /** "#RRGGBB" mayúsculas, null = verde de Factum. */
  primary_color: string | null;
  /** null = tinte de Factum. */
  accent_color: string | null;
  logo: BrandingImage | null;
  isotype: BrandingImage | null;
  /** ISO UTC (ms); token optimista. */
  updated_at: string | null;
  /** DNI. */
  updated_by: string | null;
  /** Solo con exists=false, en modo local y si users.organization tiene valor (D10). */
  suggested_organization_name: string | null;
}

/** Parte `metadata` (JSON) del PUT multipart. Archivos en las partes `logo` / `isotype`. */
export interface BrandingSaveMetadata {
  organization_name: string;
  contact_lines: string[];
  /** "#RRGGBB" o "" (= Factum). */
  primary_color: string;
  accent_color: string;
  logo_action: BrandingImageAction;
  isotype_action: BrandingImageAction;
  /** updated_at tal como vino; null si exists=false. */
  expected_updated_at: string | null;
}

export interface AccountBrandingSaveResponse { branding: AccountBranding; changed: boolean; }

export type BrandingField =
  | "metadata" | "organization_name" | "contact_lines" | "primary_color" | "accent_color" | "logo" | "isotype";
export type BrandingErrorCode =
  | "validation_failed" | "stale_update" | "request_too_large" | "image_not_found"
  | "user_not_found" | "not_available" | "superadmin_required";

export type CaptureRoleValue = "imei_modelo" | "nombre_dispositivo";
export type Tratamiento = "suscripto" | "suscripta";

/** Copia del perfil del perito guardada en el caso (`perito`). */
export interface PeritoSnapshot {
  nombre: string;
  matricula: string;
  profesion: string;
  caracter: string;
  tratamiento: string;
}

/** Textos del paso "Informe" (`report_texts`). */
export interface ReportTextsInput {
  objeto_informe: string;
  operaciones_realizadas: string;
  aseguramiento_evidencia: string;
  resultados: string;
  valoracion_tecnica: string;
  conclusiones: string;
  notas_tecnicas: string;
  reserva: string;
}

/** Formato de los textos del informe (editor-texto-enriquecido): el dialecto Markdown de Factum. */
export type ReportTextFormat = "markdown";

/** Body de `PUT /api/cases/{id}/report-texts`: este cliente siempre manda `formato: "markdown"`. */
export interface ReportTextsRequest extends ReportTextsInput {
  formato: ReportTextFormat;
  /**
   * versionado-informe (HU6): opcional. `"save"` (default si se omite) o
   * `"restore"`. Solo el guardado disparado por "Restaurar esta versión" lo
   * manda; el autoguardado normal lo omite.
   */
  trigger?: "save" | "restore";
  /** Id de la versión restaurada; solo válido con `trigger: "restore"`. */
  restored_from?: string;
  /**
   * wizard-concurrencia-pestanas (DT13): cantidades con las que se armó
   * "Operaciones realizadas". Solo se manda cuando ese texto sale de los
   * defaults; ausente = el backend conserva el valor guardado.
   */
  operaciones_base?: ReportCounts;
}

/** Conteos de evidencia que usa "Operaciones realizadas" (`ReportValues.Counts` del backend). */
export interface ReportCounts {
  capturas: number;
  grabaciones: number;
  archivos_extraidos: number;
  otros: number;
}

/** Respuesta de `GET /api/cases/{id}/report-texts/defaults` (textos ya en Markdown). */
export interface ReportTextDefaults extends ReportTextsInput {
  formato: ReportTextFormat;
  /** Conteos actuales de la evidencia (falta en un backend anterior). */
  operaciones_base?: ReportCounts;
}

export interface ReportTexts extends ReportTextsInput {
  /** null/ausente = texto plano anterior a esta HU. "texto" no lo devuelve el servidor, pero se tolera. */
  formato?: ReportTextFormat | "texto" | null;
  updated_at?: string;
  /** Cantidades con las que se armó "Operaciones realizadas" (null/ausente = sin dato, sin aviso). */
  operaciones_base?: ReportCounts | null;
}

/* ── Cadena de custodia / actividad del caso (trazabilidad-caso, HU5) ── */

/**
 * Tipo de un hito de la línea de tiempo del caso (`case_events.type`). Valores
 * snake_case literales del backend (`CaseEventTypes`). Un backend más nuevo
 * podría agregar tipos: el cliente los trata con un texto genérico.
 */
export type CaseEventType =
  | "case_created"
  | "case_updated"
  | "capture_screenshot"
  | "capture_video_start"
  | "capture_video_stop"
  | "capture_photo"
  | "evidence_added"
  | "evidence_removed"
  | "report_generated"
  | "report_failed"
  | "evidence_updated"
  | "capture_failed"
  /** sellado-tiempo: sello RFC 3161 obtenido para la evidencia, el ZIP o el informe. */
  | "timestamp_granted"
  /** sellado-tiempo: fallo al pedir un sello (primer intento, respuesta inválida o abandono). */
  | "timestamp_failed"
  /** metadatos-dispositivo: ficha técnica registrada (alta, retoma o "Volver a leer"). Sin SSID ni valores. */
  | "device_details_recorded"
  /** metadatos-dispositivo: medición de reloj al generar (`clock_kind = "close"`). */
  | "device_clock_measured"
  /** grabacion-sesion-completa (§6.4): switch del perito (activó/desactivó, con cámara). */
  | "recording_session_settings_changed"
  /** grabacion-sesion-completa: primer tramo de una pista en el caso. */
  | "recording_session_started"
  /** grabacion-sesion-completa: tramo guardado (`filename` = el del tramo). */
  | "recording_session_segment_closed"
  /** grabacion-sesion-completa: hueco nuevo o tramo descartado. */
  | "recording_session_stopped"
  /** grabacion-sesion-completa: hueco que se cierra. */
  | "recording_session_resumed";

/**
 * Detalle acotado de un `CaseEvent`: cada campo solo aplica a ciertos tipos y el
 * backend lo omite cuando no corresponde. Nunca trae datos sensibles.
 */
export interface CaseEventDetail {
  /** `case_updated`: nombres snake_case de los campos que cambiaron (sin valores). */
  changed_fields?: string[];
  /** `report_generated`: hash del ZIP. */
  zip_hash?: string;
  /** `report_generated`: hash del informe. */
  report_hash?: string;
  /** `report_failed` / `capture_failed`: motivo resumido (<=300 chars). */
  reason?: string;
  /** `evidence_added` / `evidence_updated`: hash del archivo (en `evidence_updated`, el nuevo). */
  sha256?: string;
  /** `evidence_added` / `evidence_updated`: tamaño en bytes. */
  size?: number;
  /** `evidence_updated`: hash que tenía el archivo antes. */
  previous_sha256?: string;
  /** `capture_failed`: tipo de evento de captura que se habría registrado. */
  failed_action?: "capture_video_start" | "capture_video_stop" | "capture_screenshot" | "capture_photo";
  /** `timestamp_granted` / `timestamp_failed`: qué objeto se selló (`sha256` = el hash sellado). */
  target?: TimestampTarget;
  /** `timestamp_granted`: nombre legible de la autoridad. */
  authority?: string;
  /** `timestamp_granted`: fecha y hora certificadas por la autoridad (ISO UTC). */
  gen_time?: string;
  /** `timestamp_granted`: número de serie del sello. */
  serial_number?: string;
  /** `timestamp_granted`: URL de la autoridad que respondió (sin credenciales). */
  tsa_url?: string;
  /** `timestamp_failed`: número de intento. */
  attempt?: number;
  /** `timestamp_failed`: `true` = se abandonó tras agotar los intentos. */
  final?: boolean;
  /** `device_details_recorded`: datos no disponibles (sin `not_applicable` ni `not_connected`). */
  unavailable_count?: number;
  /** `device_details_recorded`: `true` si ya había ficha ("Volver a leer"). */
  reread?: boolean;
  /** `device_details_recorded` / `device_clock_measured`. */
  clock_kind?: "start" | "close";
  /** Reloj del dispositivo − reloj de la PC (ms); ausente si no hubo hora (el motivo va en `reason`). */
  device_minus_pc_ms?: number;
  clock_margin_ms?: number;
  /** PC − servidor (ms); ausente si no se midió. */
  pc_minus_server_ms?: number;
  /* ── grabacion-sesion-completa (§6.4/§7.6.4); `reason`, `sha256` y `size` se reusan ── */
  /** `recording_session_settings_changed`. */
  enabled?: boolean;
  with_camera?: boolean;
  /** Valores anteriores al cambio (grabacion-sesion-completa-f2 §6.3); ausentes si el sub-documento se creó ahí. */
  previous_enabled?: boolean;
  previous_with_camera?: boolean;
  /** Pista del tramo o del hueco. */
  track?: RecordingSessionTrack;
  segment_index?: number;
  /** Hora real de la PC (Tatana); `timestamp` es la del servidor al sincronizar. */
  at?: string;
  duration_seconds?: number;
  /**
   * `recording_session_segment_closed`: tiempo grabado del tramo, la cifra del informe
   * (QA B-4). Opcional: un backend que no lo mande deja el evento sin duración.
   */
  recorded_seconds?: number;
  validation?: "ok" | "incomplete";
  recovered?: boolean;
  /** `recording_session_resumed`: duración del hueco que se cerró. */
  gap_seconds?: number;
  /** `recording_session_stopped`: el tramo en curso no se pudo recuperar. */
  segment_lost?: boolean;
}

/** Un hito de la línea de tiempo del caso (`GET /api/cases/{id}/events`). */
export interface CaseEvent {
  id: string;
  case_id: string;
  type: CaseEventType;
  actor_dni: string;
  /** Copia congelada del nombre del perito al registrar. */
  actor_name: string;
  /** Instante UTC (ISO-8601); se muestra en hora local. */
  timestamp: string;
  /** Solo en eventos originados en Tatana. */
  hostname?: string | null;
  os_user?: string | null;
  /** Modo del agente que originó el evento (null si no vino de Tatana). */
  agent_mode?: "installed" | "portable" | null;
  ip?: string | null;
  /** Captura / subida / baja. */
  filename?: string | null;
  detail?: CaseEventDetail | null;
}

/* ── Historial de versiones de los textos del informe (versionado-informe, HU6) ── */

/** Qué disparó una versión del historial (`report_text_versions[].trigger`). */
export type ReportVersionTrigger = "save" | "restore" | "generate";

/**
 * Foto de las ocho secciones + formato de una versión del historial. Mismas
 * claves que `report_texts`, **sin** `updated_at`.
 */
export interface ReportTextVersionTexts {
  objeto_informe: string;
  operaciones_realizadas: string;
  aseguramiento_evidencia: string;
  resultados: string;
  valoracion_tecnica: string;
  conclusiones: string;
  notas_tecnicas: string;
  reserva: string;
  /** `"markdown"` o null/ausente (texto plano de una versión vieja). */
  formato?: "markdown" | null;
}

/** Una versión del historial de `report_texts` (`GET …/report-text-versions`). */
export interface ReportTextVersion {
  id: string;
  /** Instante UTC (ISO-8601) en que se consolidó la versión. */
  created_at: string;
  author_dni: string;
  author_name: string;
  trigger: ReportVersionTrigger;
  /** Si es una restauración, el id de la versión restaurada. */
  restored_from?: string | null;
  texts: ReportTextVersionTexts;
}

/** Captura del caso que se puede insertar en un texto del informe (SDD §4.4). */
export interface ReportImage {
  /** Nombre real del archivo. */
  filename: string;
  /** Bytes. */
  size: number;
  role: CaptureRoleValue | null;
  /** Regla "disponible" de §4.2: existe, no está vacía y es PNG/JPEG por contenido. */
  available: boolean;
  /** px; `null` si no está disponible. */
  width: number | null;
  height: number | null;
}

export interface CaptureRole {
  filename: string;
  role: CaptureRoleValue;
}

export interface FileSource {
  filename: string;
  source_path: string;
}

/** Perfil del perito (`GET`/`PUT /api/profile`). */
export interface ExpertProfile {
  dni: string;
  nombre: string;
  matricula: string;
  profesion: string;
  caracter: string;
  tratamiento: string;
  exists: boolean;
  is_complete: boolean;
  updated_at: string | null;
}

export interface ExpertProfileRequest {
  nombre: string;
  matricula: string;
  profesion: string;
  caracter: string;
  tratamiento: Tratamiento;
}

/** Datos de la causa que viajan en `POST`/`PUT /api/cases` (sin `device`). */
export interface CaseDataRequest {
  nro_referencia: string;
  nombre_denunciante: string;
  dni_denunciante: string;
  nombre_tribunal: string;
  organismo_tribunal: string;
  sala_tribunal: string;
  /**
   * Texto libre de integrantes (clientes viejos). El cliente nuevo no lo manda:
   * manda `integrantes` y el servidor deriva este texto.
   */
  integrantes_tribunal?: string;
  /** Integrantes en orden, ya recortados y sin vacíos. Si viene, el servidor ignora `integrantes_tribunal`. */
  integrantes?: string[];
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
}

/* ── Evidencia en la PC del perito (zip-local-informe-servidor, SDD §8.1) ── */

/** Flujo del caso (D8): `agent` = evidencia en Tatana; `server` = flujo viejo. */
export type EvidenceStorage = "agent" | "server";

/** Ítem del manifiesto de evidencia (`Case.evidence`). */
export interface EvidenceItem {
  filename: string;
  size: number;
  sha256: string;
  source_path: string | null;
  registered_at: string;
}

/** PC donde está la evidencia del caso (`Case.evidence_host`). */
export interface EvidenceHost {
  hostname: string;
  os_user: string;
  agent_version: string;
  case_directory: string;
  registered_at: string;
}

/** Dónde quedó el ZIP (`Case.zip_location`). */
export interface ZipLocation {
  hostname: string;
  directory: string;
  path: string;
}

/** Archivo del manifiesto en `prepare`, `zip` y `finish`. */
export interface ManifestFile {
  filename: string;
  size: number;
  sha256: string;
  /**
   * Carpeta dentro del ZIP (grabacion-sesion-completa §7.6.2): `"sesion"` para los
   * tramos de la grabación de la sesión; `null`/ausente = raíz. Se pasa tal cual a Tatana.
   */
  zip_folder?: "sesion" | null;
}

/** Body de `PUT /api/cases/{id}/evidence`. */
export interface RegisterEvidenceRequest {
  host: { hostname: string; os_user: string; agent_version: string; case_directory: string };
  items: { filename: string; size: number; sha256: string; source_path: string | null }[];
}

/** Respuesta de registrar y de borrar evidencia. */
export interface EvidenceResponse {
  evidence: EvidenceItem[];
  evidence_host: EvidenceHost | null;
  /**
   * sellado-tiempo: config actual del sellado en el backend. Falta en un
   * backend anterior a esa HU (la web oculta todo lo del sellado).
   */
  timestamping_enabled?: boolean;
  /** sellado-tiempo: autoridad de la primera URL configurada, o `null`. */
  timestamp_authority?: string | null;
  /** sellado-tiempo: un sello por ítem del manifiesto que lo tenga para su `sha256` actual. */
  timestamps?: CaseTimestamp[];
}

/* ── Sellado de tiempo RFC 3161 (sellado-tiempo, SDD §7.2) ── */

/** Qué objeto se selló. */
export type TimestampTarget = "evidence" | "zip" | "report";
/** Estado del sello: `failed` es terminal; `pending` lo reintenta la cola del backend. */
export type TimestampStatus = "pending" | "granted" | "failed";

/** Un sello de tiempo (`TimestampDto`). Nunca trae el `.tsr`. */
export interface CaseTimestamp {
  id: string;
  target: TimestampTarget;
  filename: string;
  /** Hash sellado (64 hex en minúscula). */
  sha256: string;
  status: TimestampStatus;
  attempts: number;
  /** ISO UTC: cuándo se creó el pedido. */
  requested_at: string;
  /** Solo en `pending`. */
  next_attempt_at: string | null;
  last_error: string | null;
  /** Solo en `granted`. */
  authority: string | null;
  /** Sin credenciales. */
  tsa_url: string | null;
  /** ISO UTC: la fecha y hora certificadas por la autoridad. */
  gen_time: string | null;
  /** `gen_time` formateada por el backend en `Report:TimeZone`, igual que el informe. */
  gen_time_display: string | null;
  serial_number: string | null;
  policy_oid: string | null;
  /** ISO UTC, reloj del servidor. */
  granted_at: string | null;
  /** Solo `zip`: `true` = figura en el DOCX; `false` = obtenido después de emitirlo. */
  in_report: boolean | null;
}

/** Un archivo del manifiesto actual con su sello (o `null`). */
export interface EvidenceTimestampRow {
  filename: string;
  sha256: string;
  timestamp: CaseTimestamp | null;
}

/** `GET /api/cases/{id}/timestamps`. */
export interface CaseTimestampsResponse {
  /** Config **actual** del backend. */
  enabled: boolean;
  authority_hint: string | null;
  /** `Case.timestamp_mode`: si el sellado estaba activo al generar. */
  case_mode: "enabled" | "disabled" | null;
  evidence_storage: EvidenceStorage;
  /** Sello del `zip_hash` actual, o `null`. */
  zip: CaseTimestamp | null;
  /** Sello del `report_hash` actual, o `null`. */
  report: CaseTimestamp | null;
  /** Manifiesto actual en orden ordinal. */
  evidence: EvidenceTimestampRow[];
  evidence_sealed: number;
  evidence_total: number;
}

/** Un `.tsr` en base64. */
export interface TimestampFileItem {
  name: string;
  content_base64: string;
}

/** `GET /api/cases/{id}/timestamps/files`: lo que el navegador le pasa a Tatana. */
export interface TimestampFilesResponse {
  case_ref: string;
  /** `null` si el caso es un borrador. */
  zip_filename: string | null;
  files: TimestampFileItem[];
  /** Texto de `LEEME-verificacion.txt` (CRLF). */
  readme: string;
}

/** Respuesta de `POST /api/cases/{id}/generate/prepare`. */
export interface PrepareGenerationResponse {
  generation_id: string;
  zip_filename: string;
  case_ref: string;
  password: string | null;
  encrypted: boolean;
  files: ManifestFile[];
  report_images: string[];
}

/** Parte `metadata` de `POST /api/cases/{id}/generate/finish`. */
export interface FinishGenerationMetadata {
  generation_id: string;
  zip_filename: string;
  zip_hash: string;
  zip_size: number;
  encrypted: boolean;
  zip_location: ZipLocation;
  files: ManifestFile[];
}

/** Respuesta de `generate` (flujo viejo) y de `generate/finish`. */
export interface GenerateResult {
  case: Case;
  zip_hash: string;
  report_hash: string;
  /** Contraseña del ZIP (una sola vez); `null` si el ZIP se generó sin cifrar. */
  password: string | null;
  files: { zip: string; pdf: string };
  /** Solo en el flujo `agent`. */
  zip_location?: ZipLocation | null;
}

export interface Case {
  id: string;
  nro_referencia: string;
  nombre_denunciante: string;
  dni_denunciante: string;
  observaciones: string;
  officer: User;
  device: DeviceInput;
  status: "draft" | "generating" | "completed" | "error";
  created_at: string;
  generated_at?: string;
  zip_hash?: string;
  /**
   * `true` solo si el ZIP de evidencia se generó cifrado (AES-256). Los casos
   * viejos o generados con el cifrado apagado lo traen en `false` (o no lo
   * traen). La contraseña ya no viaja en el `Case`: ver `getZipPassword`.
   */
  zip_encrypted?: boolean;
  /** Algoritmo del cifrado (`"aes256-ae2"`) o `null`. Informativo: la UI decide con `zip_encrypted`. */
  zip_encryption?: string | null;
  zip_filename?: string;
  pdf_filename?: string;
  file_sources?: FileSource[];
  /** 0 = caso previo al informe pericial; 1 = caso pericial. */
  schema_version: number;
  perito: PeritoSnapshot | null;
  nombre_tribunal: string;
  organismo_tribunal: string;
  sala_tribunal: string;
  /** Frase de integrantes. Cuando `integrantes` no es null, es la derivada por el servidor ("A, B y C"). */
  integrantes_tribunal: string;
  /** Integrantes como lista. `null`/ausente = caso guardado antes de formulario-caso-catalogos. */
  integrantes?: string[] | null;
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
  /** `null` en el listado (el backend lo proyecta afuera); viene en `getCase`. */
  report_texts: ReportTexts | null;
  capture_roles: CaptureRole[];
  report_hash: string | null;
  /**
   * Flujo resuelto por el backend (siempre presente desde zip-local-informe-servidor).
   * Si falta (backend anterior), el cliente lo trata como `"server"`: flujo de hoy.
   */
  evidence_storage?: EvidenceStorage;
  /** Manifiesto del flujo `agent` (`[]` en los demás). */
  evidence?: EvidenceItem[];
  evidence_host?: EvidenceHost | null;
  zip_location?: ZipLocation | null;
  /**
   * sellado-tiempo: si el sellado estaba activo cuando se generó (flujo agent).
   * `null`/ausente = caso anterior a esa HU, flujo viejo o borrador.
   */
  timestamp_mode?: "enabled" | "disabled" | null;
  /**
   * metadatos-dispositivo: ficha técnica leída por Tatana. `null`/ausente = caso
   * anterior a la HU, alta con un Tatana sin `device_details_v1` o lectura sin registrar.
   */
  device_details?: DeviceDetails | null;
  /**
   * grabacion-sesion-completa (§7.6.1): grabación de la sesión de recolección.
   * `null`/ausente = caso sin sesión (o anterior a la HU). El backend manda los `null`.
   */
  recording_session?: RecordingSession | null;
}

/* ── Grabación de la sesión completa (grabacion-sesion-completa, SDD §7.5-§7.6) ── */

export type RecordingSessionTrack = "pantalla" | "camara";

/** Motivos de hueco / `stop_reason` (§6.2). */
export type RecordingSessionReason =
  | "cambio_de_paso" | "retomado_otro_dia" | "desconexion" | "tatana_cerrado" | "sin_espacio"
  | "camara_fallo" | "detenido_por_perito" | "fallo_grabacion" | "rotacion";

/** Modo de instalación (`Recording:SessionMode`, §6.6). */
export type RecordingSessionMode = "disabled" | "optional_off" | "optional_on" | "required";

export interface RecordingSessionCamera {
  name: string; width: number; height: number; fps: number; with_audio: boolean;
}

/** Tramo guardado en el caso (solo `saved`/`discarded` llegan al backend). */
export interface RecordingSessionSegment {
  id: string;
  track: RecordingSessionTrack;
  index: number;
  filename: string;
  state: "saved" | "discarded";
  started_at: string;
  stopped_at: string;
  recorded_seconds: number;
  duration_seconds?: number | null;
  validation?: "ok" | "incomplete" | null;
  recovered: boolean;
  overlap_seconds?: number | null;
  stop_reason?: RecordingSessionReason | null;
  size?: number | null;
  sha256?: string | null;
  /** Configuración de la cámara del tramo: objeto en tramos `camara`, `null` en pantalla (F2 §7.4). */
  camera?: RecordingSessionCamera | null;
}

export interface RecordingSessionGap {
  id: string;
  track: RecordingSessionTrack;
  from: string;
  to?: string | null;
  reason: RecordingSessionReason;
  segment_lost: boolean;
}

export interface RecordingSessionMark {
  id: string;
  at: string;
  /** Ubicación en la pantalla; `null` si la pantalla no grababa (F2 [DU1=A]). */
  segment_index?: number | null;
  offset_seconds?: number | null;
  /** Ubicación en la cámara; `null` si ningún tramo de cámara cubre `at` (F2 §7.5). */
  camera_segment_index?: number | null;
  camera_offset_seconds?: number | null;
  text?: string | null;
  filename?: string | null;
  kind: "manual" | "captura";
}

/** `Case.recording_session` (§7.6.1). */
export interface RecordingSession {
  enabled: boolean;
  with_camera: boolean;
  mode_at_change: string;
  settings_updated_at: string;
  session_id?: string | null;
  platform?: "android" | "ios" | null;
  connection_id?: string | null;
  agent_version?: string | null;
  segment_minutes: number;
  audio?: "device" | "none" | null;
  camera?: RecordingSessionCamera | null;
  state?: string | null;
  segments: RecordingSessionSegment[];
  gaps: RecordingSessionGap[];
  marks: RecordingSessionMark[];
  synced_at?: string | null;
}

/** Body de `PUT /api/cases/{id}/recording-session/settings`. */
export interface RecordingSessionSettingsRequest {
  enabled: boolean;
  with_camera: boolean;
}

/**
 * Body de `PUT /api/cases/{id}/recording-session` (§7.5): el diario de Tatana
 * sin `case_id`/`schema_version`/`updated_at`, solo tramos `saved`/`discarded`
 * y con `serial` → `connection_id`. Lo arma `toBackendSnapshot`.
 */
export interface RecordingSessionSnapshot {
  session_id: string;
  platform: "android" | "ios";
  connection_id: string;
  agent_version: string | null;
  segment_minutes: number;
  audio: "device" | "none" | null;
  camera: RecordingSessionCamera | null;
  state: string;
  segments: RecordingSessionSegment[];
  gaps: RecordingSessionGap[];
  marks: RecordingSessionMark[];
}

/* ── Ficha técnica del dispositivo (metadatos-dispositivo, SDD §8.2-§8.3) ── */

export interface DeviceDetailField { value?: string | null; reason?: DeviceDetailReason | null }

/** Una medición de reloj registrada (`clock_start` / `clock_close`). */
export interface ClockMeasurement {
  kind: "start" | "close"; recorded_at: string; reason?: ClockReason | null;
  pc_before?: string | null; pc_after?: string | null; device_time?: string | null; resolution_ms?: number | null;
  device_minus_pc_ms?: number | null; margin_ms?: number | null;
  pc_minus_server_ms?: number | null; pc_server_margin_ms?: number | null; pc_server_reason?: PcServerReason | null;
}

export interface DeviceDetails {
  read_at: string; platform: "android" | "ios"; connection_id: string; agent_version: string;
  fields: Partial<Record<DeviceDetailKey, DeviceDetailField>>;
  clock_start?: ClockMeasurement | null; clock_close?: ClockMeasurement | null;
  include_ssid_in_report: boolean;
}

/** Sondeo NTP PC↔servidor (§5.2): ISO con ms; `server_time` tal como lo devolvió la API. */
export interface PcServerProbe { pc_before: string; server_time: string; pc_after: string }

/** `clock` del `PUT`: los valores crudos de Tatana, sin parsear. La API recalcula todo. */
export interface DeviceClockRequest {
  pc_before?: string | null; pc_after?: string | null; device_time?: string | null;
  resolution_ms?: number | null; reason?: string | null;
}

/** `PUT /api/cases/{id}/device-details`. */
export interface SaveDeviceDetailsRequest {
  platform: "android" | "ios";
  connection_id: string;
  agent_version: string;
  fields: Partial<Record<DeviceDetailKey, { value?: string | null; reason?: string | null }>>;
  clock: DeviceClockRequest;
  /** Opcional: sin él, la API registra `not_measured`. */
  pc_server_probe?: PcServerProbe;
  /** Opcional: sin él, se conserva lo guardado (o `false`). */
  include_ssid_in_report?: boolean;
}

/** `PUT /api/cases/{id}/device-details/clock` (medición de cierre). */
export interface SaveDeviceClockRequest {
  connection_id: string;
  agent_version: string;
  clock: DeviceClockRequest;
  pc_server_probe?: PcServerProbe;
}

/** Config pública del backend (`GET /api/config/public`, sin auth). */
export interface PublicConfig {
  organization_name: string | null;
  /** Ruta relativa a la raíz del backend (empieza con `/`), con `?v=` para la caché. */
  organization_logo_url: string | null;
  /** `true` solo si el backend tiene la integración de soporte habilitada. */
  support_enabled: boolean;
  /** `true` si el backend genera el ZIP de evidencia cifrado con AES-256 (`Report:EncryptZip`). */
  encrypt_zip: boolean;
  /**
   * Versión mínima de Tatana (`Tatana:MinVersion`, `X.Y.Z`) para capturar desde
   * esta web (tatana-instalador-autoupdate, D6). `null` = sin mínimo. Falta en
   * un backend anterior a esa HU (= sin mínimo).
   */
  tatana_min_version?: string | null;
  /** sellado-tiempo: `true` si el backend sella con una autoridad RFC 3161. Falta en un backend anterior. */
  timestamping_enabled?: boolean;
  /** metadatos-dispositivo: umbral (s) del aviso de diferencia de reloj. Falta en un backend anterior (= 60). */
  clock_skew_warning_seconds?: number;
  /** grabacion-sesion-completa (§7.6.3): modo de instalación. Falta en un backend anterior. */
  recording_session_mode?: RecordingSessionMode;
  /** grabacion-sesion-completa: duración del tramo en minutos (5–120). Falta en un backend anterior. */
  recording_session_segment_minutes?: number;
}

/** Modo de autenticación del backend (`GET /api/auth/mode`). */
export type AuthMode = "dev" | "external" | "local";

export interface DeviceInput {
  serial: string;
  manufacturer: string;
  model: string;
  android_version: number;
  imei: string;
  platform?: "android" | "ios";
  os_version?: string;
  /** `device.name` del agente (referencia para la captura "Nombre del dispositivo"). */
  name?: string;
}

/* ── Analítica del dashboard (dashboard-kpis-tendencias §8) ── */

/**
 * KPIs + tendencia mensual del perito (`GET /api/cases/stats`). Casing snake_case
 * exacto del backend (`CaseStatsResponse`, `JsonNamingPolicy.SnakeCaseLower`).
 * `by_status` es un objeto plano con las 4 claves fijas (siempre presentes).
 */
export interface CaseStats {
  total: number;
  by_status: { draft: number; generating: number; completed: number; error: number };
  /** 0..1; `completed / total`. 0 si `total === 0`. El cliente lo muestra como %. */
  completion_rate: number;
  /** Promedio de cierre en segundos, o `null` si no hay completados con `generated_at`. */
  avg_close_seconds: number | null;
  /** 12 meses ascendentes (los sin casos en 0). `month` en formato "yyyy-MM". */
  monthly: { month: string; count: number }[];
}

/* ── Desglose analítico (dashboard-breakdown §8) ── */

/** Dimensiones del desglose (`GET /api/cases/breakdown`). */
export type BreakdownDimension = "platform" | "status" | "caratula" | "ambito_causa";

/** Un segmento del desglose. `label` lo resuelve el backend (fuente única de los textos). */
export interface BreakdownBucket {
  key: string;
  label: string;
  count: number;
}

/**
 * Distribución de los casos del perito por una dimensión (`GET /api/cases/breakdown`).
 * `total` = suma de los `count` = total del conjunto filtrado por fecha.
 */
export interface CaseBreakdown {
  dimension: BreakdownDimension;
  total: number;
  buckets: BreakdownBucket[];
}

export const api = {
  async login(dni: string, username: string, password: string): Promise<AuthResponse> {
    const data = await request<AuthResponse>("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ dni, username, password }),
    });
    writeSession(data, "login");
    setSessionUser(data.user.dni);
    return data;
  },

  logout() {
    clearSession();
    setSessionUser(null);
  },

  async me(): Promise<User> {
    const data = await request<{ user: User }>("/api/auth/me");
    setSessionUser(data.user.dni);
    return data.user;
  },

  /**
   * Renueva el token (`POST /api/auth/refresh`, sesion-larga §6.1). No guarda
   * nada: lo hace `SessionManager` con `writeSession`. Un 401 que no sea
   * `session_max_age` (o el 403 de cambio de contraseña) pasa por el
   * interceptor como cualquier pedido; `session_max_age` y el 429
   * (`retryAfterSeconds`) se los maneja quien llama.
   */
  async refreshSession(): Promise<AuthResponse> {
    let sent: Sent;
    try {
      sent = await send("/api/auth/refresh", { method: "POST", cache: "no-store" });
    } catch {
      throw new ApiError(SERVER_UNREACHABLE, 0, undefined, null);
    }
    if (!sent.res.ok) {
      const err = await toApiError(sent.res, sent.sentToken);
      if ((err.status === 401 && err.code !== "session_max_age") || err.status === 403) {
        notifyAuthFailure(err.status, err.body, sent.res.url, sent.sentToken, { force: true });
      }
      throw err;
    }
    return sent.res.json() as Promise<AuthResponse>;
  },

  /**
   * Cambio de contraseña propio (obligatorio o voluntario). Guarda el token
   * nuevo: el anterior (y las otras sesiones) deja de valer.
   */
  async changePassword(body: ChangePasswordRequest): Promise<AuthResponse> {
    const data = await requestSafe<AuthResponse>("/api/auth/change-password", {
      method: "POST",
      body: JSON.stringify(body),
    });
    // change-password reinicia la hora del login (D4 de la SDD): es un login más.
    writeSession(data, "login");
    setSessionUser(data.user.dni);
    return data;
  },

  /* ── Administración de cuentas (abm-clientes §8.1; solo superadmin en modo `local`) ── */

  async adminListUsers(): Promise<AdminUser[]> {
    const data = await requestSafe<{ users: AdminUser[] }>("/api/admin/users");
    return data.users;
  },

  async adminGetUser(id: string): Promise<AdminUser> {
    const data = await requestSafe<{ user: AdminUser }>(`/api/admin/users/${encodeURIComponent(id)}`);
    return data.user;
  },

  /** Alta de un cliente (el rol siempre es `cliente`, D3). Devuelve la temporal una sola vez. */
  async adminCreateUser(body: AdminCreateUserRequest): Promise<AdminUserWithPassword> {
    return requestSafe<AdminUserWithPassword>("/api/admin/users", {
      method: "POST",
      body: JSON.stringify(body),
    });
  },

  async adminUpdateUser(id: string, body: AdminUpdateUserRequest): Promise<{ user: AdminUser; changed: boolean }> {
    return requestSafe<{ user: AdminUser; changed: boolean }>(`/api/admin/users/${encodeURIComponent(id)}`, {
      method: "PUT",
      body: JSON.stringify(body),
    });
  },

  async adminSuspendUser(id: string, reason?: string): Promise<AdminUser> {
    const data = await requestSafe<{ user: AdminUser }>(`/api/admin/users/${encodeURIComponent(id)}/suspend`, {
      method: "POST",
      body: JSON.stringify(reason ? { reason } : {}),
    });
    return data.user;
  },

  async adminReactivateUser(id: string): Promise<AdminUser> {
    const data = await requestSafe<{ user: AdminUser }>(`/api/admin/users/${encodeURIComponent(id)}/reactivate`, {
      method: "POST",
      body: "{}",
    });
    return data.user;
  },

  async adminResetPassword(id: string): Promise<AdminUserWithPassword> {
    return requestSafe<AdminUserWithPassword>(`/api/admin/users/${encodeURIComponent(id)}/reset-password`, {
      method: "POST",
      body: "{}",
    });
  },

  async adminUnlockUser(id: string): Promise<AdminUser> {
    const data = await requestSafe<{ user: AdminUser }>(`/api/admin/users/${encodeURIComponent(id)}/unlock`, {
      method: "POST",
      body: "{}",
    });
    return data.user;
  },

  async adminListUserEvents(
    id: string, offset: number, limit = 20,
  ): Promise<{ events: AdminUserEvent[]; has_more: boolean }> {
    const qs = `offset=${encodeURIComponent(String(offset))}&limit=${encodeURIComponent(String(limit))}`;
    return requestSafe<{ events: AdminUserEvent[]; has_more: boolean }>(
      `/api/admin/users/${encodeURIComponent(id)}/events?${qs}`,
    );
  },

  /* ── Marca del informe (marca-por-cliente §8.2) ── */

  /** Marca de la cuenta logueada (el DNI sale de la sesión). */
  async getMyBranding(): Promise<AccountBranding> {
    const data = await requestSafe<{ branding: AccountBranding }>("/api/branding", { cache: "no-store" });
    return data.branding;
  },

  /** `multipart/form-data`: `metadata` (JSON) + `logo?` / `isotype?`. */
  async saveMyBranding(form: FormData): Promise<AccountBrandingSaveResponse> {
    return sendMultipart<AccountBrandingSaveResponse>("/api/branding", "PUT", form);
  },

  async adminGetBranding(id: string): Promise<AccountBranding> {
    const data = await requestSafe<{ branding: AccountBranding }>(
      `/api/admin/users/${encodeURIComponent(id)}/branding`, { cache: "no-store" },
    );
    return data.branding;
  },

  async adminSaveBranding(id: string, form: FormData): Promise<AccountBrandingSaveResponse> {
    return sendMultipart<AccountBrandingSaveResponse>(`/api/admin/users/${encodeURIComponent(id)}/branding`, "PUT", form);
  },

  /**
   * Bytes de una imagen de marca (logo o isotipo) como `Blob`. `url` es la del
   * DTO (propia o del panel), relativa al backend. Con `Authorization`, nunca
   * con `?token=` (DT4).
   */
  async getBrandingImage(url: string, signal?: AbortSignal): Promise<Blob> {
    let sent: Sent;
    try {
      sent = await authFetch(`${BACKEND_URL}${url}`, { signal });
    } catch (e) {
      if (signal?.aborted) throw e;
      throw new ApiError(SERVER_UNREACHABLE, 0, undefined, null);
    }
    if (!sent.res.ok) throw await toApiError(sent.res, sent.sentToken);
    return sent.res.blob();
  },

  async createCase(data: CaseDataRequest & { device: DeviceInput }): Promise<Case> {
    return request<Case>("/api/cases", { method: "POST", body: JSON.stringify(data) });
  },

  /** Edita los datos de la causa (borrador o error). `imei` no vacío actualiza `device.imei`. */
  async updateCase(id: string, data: CaseDataRequest & { imei?: string }): Promise<Case> {
    return request<Case>(`/api/cases/${id}`, { method: "PUT", body: JSON.stringify(data) });
  },

  async getCase(id: string): Promise<{ cas: Case; files: unknown[] }> {
    return request(`/api/cases/${id}`);
  },

  /** Catálogos de sugerencias del perito (las cuatro claves siempre). La primera vez los siembra el servidor. */
  async getCatalogs(): Promise<CatalogsResponse> {
    return request<CatalogsResponse>("/api/catalogs");
  },

  /** Corrige un valor del catálogo. 409 si choca con otro valor equivalente. No cambia ningún caso. */
  async updateCatalogEntry(catalog: CatalogId, id: string, value: string): Promise<CatalogEntry> {
    return request<CatalogEntry>(
      `/api/catalogs/${encodeURIComponent(catalog)}/entries/${encodeURIComponent(id)}`,
      { method: "PUT", body: JSON.stringify({ value }) },
    );
  },

  /** Quita un valor del catálogo (204). No cambia ningún caso. */
  async deleteCatalogEntry(catalog: CatalogId, id: string): Promise<void> {
    return requestNoContent(
      `/api/catalogs/${encodeURIComponent(catalog)}/entries/${encodeURIComponent(id)}`,
      { method: "DELETE" },
    );
  },

  async getProfile(): Promise<ExpertProfile> {
    return request<ExpertProfile>("/api/profile");
  },

  async saveProfile(data: ExpertProfileRequest): Promise<ExpertProfile> {
    return request<ExpertProfile>("/api/profile", { method: "PUT", body: JSON.stringify(data) });
  },

  async getReportTextDefaults(caseId: string): Promise<ReportTextDefaults> {
    return request<ReportTextDefaults>(`/api/cases/${caseId}/report-texts/defaults`);
  },

  async saveReportTexts(caseId: string, data: ReportTextsRequest): Promise<ReportTexts> {
    return request<ReportTexts>(`/api/cases/${caseId}/report-texts`, { method: "PUT", body: JSON.stringify(data) });
  },

  /** Hitos de cadena de custodia del caso (trazabilidad-caso, HU5), ascendente por `timestamp`. */
  async listCaseEvents(caseId: string): Promise<{ events: CaseEvent[] }> {
    return request<{ events: CaseEvent[] }>(`/api/cases/${caseId}/events`);
  },

  /** Historial de versiones de los textos del informe (versionado-informe, HU6), descendente por `created_at`. */
  async getReportTextVersions(caseId: string): Promise<{ versions: ReportTextVersion[] }> {
    return request<{ versions: ReportTextVersion[] }>(`/api/cases/${caseId}/report-text-versions`);
  },

  /** Upsert por `filename`; `role: null` borra la marca. Devuelve el estado completo. */
  async saveCaptureRoles(
    caseId: string,
    roles: { filename: string; role: CaptureRoleValue | null }[],
  ): Promise<{ capture_roles: CaptureRole[] }> {
    return request(`/api/cases/${caseId}/capture-roles`, {
      method: "PUT",
      body: JSON.stringify({ capture_roles: roles }),
    });
  },

  /**
   * Prechequeo de una subida (mismas reglas que el POST, sin cuerpo): tope,
   * espacio libre, nombre y caso editable. Rechaza con `UploadError`.
   */
  async checkUpload(caseId: string, filename: string, size: number, signal?: AbortSignal): Promise<UploadCheckResponse> {
    const params = new URLSearchParams({ filename, size: String(size) });
    let res: Response;
    try {
      ({ res } = await send(`/api/cases/${caseId}/files/upload-check?${params.toString()}`, { method: "GET", signal, cache: "no-store" }));
    } catch (e) {
      if (signal?.aborted || (e instanceof DOMException && e.name === "AbortError")) throw new UploadError("aborted");
      throw new UploadError("network");
    }
    if (!res.ok) {
      const text = await res.text().catch(() => "");
      throw new UploadError("http", res.status, parseUploadErrorBody(text));
    }
    return res.json();
  },

  /**
   * Sube un archivo (cuerpo crudo) al caso con XHR: progreso real de envío,
   * cancelación con `signal` y errores tipados (`UploadError`).
   * `sourcePath` (opcional) es la ruta de origen en el dispositivo — solo aplica a archivos
   * traídos con el explorador de archivos, y queda registrada en el informe forense.
   */
  uploadFile(
    caseId: string,
    filename: string,
    blob: Blob,
    opts: { sourcePath?: string; signal?: AbortSignal; onProgress?: (loaded: number, total: number) => void } = {},
  ): Promise<UploadedFileInfo> {
    const { sourcePath, signal, onProgress } = opts;
    return trackLongOp(() => new Promise<UploadedFileInfo>((resolve, reject) => {
      if (signal?.aborted) { reject(new UploadError("aborted")); return; }
      const params = new URLSearchParams({ filename });
      if (sourcePath) params.set("source_path", sourcePath);
      const xhr = new XMLHttpRequest();
      const onAbortSignal = () => xhr.abort();
      const done = () => signal?.removeEventListener("abort", onAbortSignal);

      xhr.open("POST", `${BACKEND_URL}/api/cases/${caseId}/files?${params.toString()}`);
      const sentToken = getToken();
      if (sentToken) xhr.setRequestHeader("Authorization", `Bearer ${sentToken}`);
      xhr.timeout = 0;
      if (onProgress) {
        xhr.upload.onprogress = e => onProgress(e.loaded, e.lengthComputable ? e.total : blob.size);
      }
      xhr.onload = () => {
        done();
        if (xhr.status >= 200 && xhr.status < 300) {
          try { resolve(JSON.parse(xhr.responseText) as UploadedFileInfo); }
          catch { reject(new UploadError("http", xhr.status, null)); }
          return;
        }
        const errBody = parseUploadErrorBody(xhr.responseText);
        notifyAuthFailure(xhr.status, errBody, xhr.responseURL, sentToken);
        reject(new UploadError("http", xhr.status, errBody));
      };
      xhr.onerror = () => { done(); reject(new UploadError("network")); };
      xhr.ontimeout = () => { done(); reject(new UploadError("network")); };
      xhr.onabort = () => { done(); reject(new UploadError("aborted")); };
      signal?.addEventListener("abort", onAbortSignal, { once: true });
      xhr.send(blob);
    }));
  },

  /**
   * Capturas insertables en el cuerpo del informe, con su rol y disponibilidad
   * (editor-imagenes-informe, SDD §4.4). Orden ordinal por nombre (el del anexo):
   * el cliente no lo reordena.
   */
  async listReportImages(caseId: string): Promise<{ images: ReportImage[] }> {
    return request<{ images: ReportImage[] }>(`/api/cases/${caseId}/report-images`);
  },

  /**
   * Vista previa de una captura del caso (SDD §4.5): los bytes de la evidencia
   * tal cual, como `Blob`. Sin `Content-Type` en el pedido (es un GET) y sin
   * `request<T>` (que lee JSON). 400/404 = imagen no disponible.
   */
  async getReportImagePreview(caseId: string, filename: string, signal?: AbortSignal): Promise<Blob> {
    const { res, sentToken } = await authFetch(
      `${BACKEND_URL}/api/cases/${caseId}/files/${encodeURIComponent(filename)}/preview`,
      { signal },
    );
    if (!res.ok) throw await toApiError(res, sentToken);
    return res.blob();
  },

  async listFiles(caseId: string): Promise<{ files: { name: string; size: number; hash: string }[] }> {
    return request(`/api/cases/${caseId}/files`);
  },

  async listCases(): Promise<{ cases: Case[] }> {
    return request("/api/cases");
  },

  async generateCase(caseId: string): Promise<GenerateResult> {
    return trackLongOp(() => request<GenerateResult>(`/api/cases/${caseId}/generate`, { method: "POST" }));
  },

  /* ── Flujo `agent` (zip-local-informe-servidor, SDD §5.2-§5.6) ── */

  /** Registra (upsert por nombre) archivos ya guardados en la carpeta del caso de Tatana. */
  async registerEvidence(caseId: string, body: RegisterEvidenceRequest): Promise<EvidenceResponse> {
    return trackLongOp(() =>
      requestSafe<EvidenceResponse>(`/api/cases/${caseId}/evidence`, { method: "PUT", body: JSON.stringify(body) }));
  },

  /** Saca un archivo del manifiesto (y su rol). El archivo en la PC lo borra el cliente después. */
  async deleteEvidence(caseId: string, filename: string): Promise<EvidenceResponse> {
    return requestSafe<EvidenceResponse>(`/api/cases/${caseId}/evidence/${encodeURIComponent(filename)}`, { method: "DELETE" });
  },

  /** Etapa 1: valida el caso contra el manifiesto y abre el intento. No cambia el `status`. */
  async prepareGeneration(caseId: string, body: { hostname: string }): Promise<PrepareGenerationResponse> {
    return trackLongOp(() => requestSafe<PrepareGenerationResponse>(`/api/cases/${caseId}/generate/prepare`, {
      method: "POST", body: JSON.stringify(body),
    }));
  },

  /**
   * Etapa 3: `multipart/form-data` con `metadata` (JSON) + las `images`.
   * Sin `Content-Type` manual: lo pone el navegador con el boundary.
   */
  async finishGeneration(caseId: string, form: FormData): Promise<GenerateResult> {
    return trackLongOp(async () => {
      let sent: Sent;
      try {
        sent = await authFetch(`${BACKEND_URL}/api/cases/${caseId}/generate/finish`, { method: "POST", body: form });
      } catch {
        throw new ApiError(SERVER_UNREACHABLE, 0, undefined, null);
      }
      if (!sent.res.ok) throw await toApiError(sent.res, sent.sentToken);
      return sent.res.json() as Promise<GenerateResult>;
    });
  },

  /**
   * Contraseña del ZIP cifrado de un caso propio, bajo pedido (el backend
   * responde con `Cache-Control: no-store`). 404 si el caso no tiene un ZIP
   * cifrado. No guardarla fuera del estado local del componente que la pidió.
   */
  async getZipPassword(caseId: string): Promise<{ password: string }> {
    return request<{ password: string }>(`/api/cases/${caseId}/zip-password`);
  },

  /* ── Analítica del dashboard (dashboard-kpis-tendencias §8) ── */

  /**
   * KPIs + tendencia mensual del perito logueado. Manda el offset de zona del
   * navegador (`-getTimezoneOffset()`, minutos a sumar a UTC) para que el servidor
   * agrupe los meses en hora local del perito (D5-A).
   */
  async caseStats(signal?: AbortSignal): Promise<CaseStats> {
    const tz = -new Date().getTimezoneOffset();
    return requestSafe<CaseStats>(`/api/cases/stats?tz_offset_minutes=${tz}`, { signal });
  },

  /* ── Desglose analítico (dashboard-breakdown §8) ── */

  /**
   * Distribución de los casos del perito por `dimension`, con filtro de fechas
   * propio (`from`/`to` en `yyyy-MM-dd`, `to` inclusivo por día). El backend
   * resuelve labels, "Sin especificar", Top N y "Otras".
   */
  async caseBreakdown(
    dimension: BreakdownDimension, from?: string, to?: string, signal?: AbortSignal,
  ): Promise<CaseBreakdown> {
    const params = new URLSearchParams({ dimension });
    if (from) params.set("from", from);
    if (to) params.set("to", to);
    return requestSafe<CaseBreakdown>(`/api/cases/breakdown?${params.toString()}`, { signal });
  },

  /* ── Sellado de tiempo (sellado-tiempo, SDD §7.4-§7.5) ── */

  /** Estado de los sellos del caso. 404 en un backend anterior a la HU (el llamador oculta el bloque). */
  async getCaseTimestamps(caseId: string, signal?: AbortSignal): Promise<CaseTimestampsResponse> {
    return requestSafe<CaseTimestampsResponse>(`/api/cases/${caseId}/timestamps`, { signal, cache: "no-store" });
  },

  /** Los `.tsr` en base64 + el LEEME, para pasárselos a Tatana. No guardarlos fuera de la llamada. */
  async getTimestampFiles(caseId: string): Promise<TimestampFilesResponse> {
    return requestSafe<TimestampFilesResponse>(`/api/cases/${caseId}/timestamps/files`, { cache: "no-store" });
  },

  /** URL de descarga del ZIP sin cifrar con `sellos/*.tsr` + el LEEME. */
  timestampBundleURL(caseId: string): string {
    const token = getToken();
    const params = token ? `?token=${encodeURIComponent(token)}` : "";
    return `${BACKEND_URL}/api/cases/${caseId}/timestamps/bundle${params}`;
  },

  /* ── Ficha técnica del dispositivo (metadatos-dispositivo, SDD §7.1) ── */

  /** Hora del servidor para el sondeo NTP (§5.2). */
  async getServerTime(): Promise<{ server_time: string }> {
    return requestSafe<{ server_time: string }>("/api/time", { cache: "no-store" });
  },

  /** Registra (o vuelve a registrar) la ficha. Devuelve el caso completo. */
  async saveDeviceDetails(caseId: string, body: SaveDeviceDetailsRequest): Promise<Case> {
    return requestSafe<Case>(`/api/cases/${caseId}/device-details`, { method: "PUT", body: JSON.stringify(body) });
  },

  /** Medición de reloj de cierre (al generar). 409 `no_device_details` si el caso no tiene ficha. */
  async saveDeviceClock(caseId: string, body: SaveDeviceClockRequest): Promise<Case> {
    return requestSafe<Case>(`/api/cases/${caseId}/device-details/clock`, { method: "PUT", body: JSON.stringify(body) });
  },

  /**
   * Switch de la grabación de la sesión del caso (grabacion-sesion-completa §6.3.1).
   * 409 `recording_session_disabled`, 400 `recording_session_required`; 404 = backend sin la ruta.
   */
  async saveRecordingSessionSettings(caseId: string, body: RecordingSessionSettingsRequest): Promise<Case> {
    return requestSafe<Case>(`/api/cases/${caseId}/recording-session/settings`, { method: "PUT", body: JSON.stringify(body) });
  },

  /**
   * Copia del diario de Tatana en el caso (§6.3.2, append-only). 409
   * `recording_session_not_enabled` / `recording_session_conflict` / `recording_session_busy`.
   */
  async syncRecordingSession(caseId: string, snapshot: RecordingSessionSnapshot): Promise<Case> {
    return trackLongOp(() => requestSafe<Case>(`/api/cases/${caseId}/recording-session`, {
      method: "PUT", body: JSON.stringify(snapshot),
    }));
  },

  /** Incluir o no el SSID en el informe (D7). Sin evento de trazabilidad. */
  async setDeviceDetailsOptions(caseId: string, body: { include_ssid_in_report: boolean }): Promise<Case> {
    return requestSafe<Case>(`/api/cases/${caseId}/device-details/options`, { method: "PUT", body: JSON.stringify(body) });
  },

  downloadURL(caseId: string, filename: string): string {
    const token = getToken();
    const params = token ? `?token=${encodeURIComponent(token)}` : "";
    return `${BACKEND_URL}/api/cases/${caseId}/download/${filename}${params}`;
  },

  /** Identidad de la organización emisora. Es anónimo: el login lo usa antes de autenticarse. */
  async getPublicConfig(): Promise<PublicConfig> {
    return request<PublicConfig>("/api/config/public");
  },

  /** URL absoluta del logo de la organización a partir de `organization_logo_url`. */
  brandingLogoURL(path: string): string {
    return `${BACKEND_URL}${path}`;
  },

  /** Modo de autenticación + largos de la política (anónimo). */
  async getAuthModeInfo(): Promise<AuthModeInfo> {
    return request<AuthModeInfo>("/api/auth/mode");
  },

  async getMode(): Promise<AuthMode> {
    const info = await api.getAuthModeInfo();
    return info.mode;
  },

  /** Reporta un problema de Factum como token de soporte (vía Faro). */
  async reportarProblema(data: {
    categoria: "hardware" | "software" | "otro";
    descripcion: string;
    numero_interno: string;
    telefono: string;
  }): Promise<{ token_numero: number }> {
    return request("/api/support/tokens", { method: "POST", body: JSON.stringify(data) });
  },

  /** Lista los tokens de soporte que reportó el oficial logueado. */
  async listarMisReportes(): Promise<{ tokens: MiToken[] }> {
    return request("/api/support/tokens");
  },

  /** Califica un token de soporte propio ya finalizado. */
  async calificarReporte(idToken: number, puntuacion: number, comentario?: string): Promise<void> {
    await request(`/api/support/tokens/${idToken}/calificacion`, {
      method: "POST",
      body: JSON.stringify({ puntuacion, comentario }),
    });
  },

  /** Enlace para abrir Faro ya logueado (SSO) — misma identidad (DNI), sin loguearse de nuevo ahí. */
  async obtenerLinkFaro(): Promise<{ url: string }> {
    return request("/api/support/faro-sso");
  },

  /**
   * Auditoría de uso del agente Tatana: reporta qué fiscal, desde qué PC/modo
   * (instalado/portátil), hizo qué acción. Best-effort — un fallo acá nunca
   * debe interrumpir la captura de evidencia en curso.
   */
  async reportAgentEvent(
    action: AgentAuditAction,
    caseId?: string | null,
    /** Solo con `capture_failed` (DT11): qué acción falló y el mismo texto que vio el perito. */
    extra?: { failed_action?: AgentFailedAction; reason?: string },
  ): Promise<void> {
    try {
      const info = await agent.getInfo();
      const token = getToken();
      // POST directo (no request<T>()): el endpoint responde 204 sin body,
      // y request<T>() asume siempre un JSON de respuesta.
      await fetch(`${BACKEND_URL}/api/agent-events`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
        },
        body: JSON.stringify({
          hostname: info.hostname,
          os_user: info.os_user,
          agent_version: info.version,
          mode: info.mode,
          action,
          case_id: caseId ?? undefined,
          ...(extra?.failed_action ? { failed_action: extra.failed_action } : {}),
          ...(extra?.reason?.trim() ? { reason: extra.reason.trim().slice(0, 300) } : {}),
        }),
      });
    } catch {
      // best-effort
    }
  },
};

export interface MiToken {
  id_token: number;
  servicio: string;
  estado: "EnEspera" | "Reasignado" | "Finalizado";
  descripcion: string;
  descripcion_resolucion: string | null;
  fecha_creacion: string;
  puntuacion: number | null;
  comentario: string | null;
}
