/**
 * Cliente del Backend (corre en el servidor, accesible desde la web).
 */

import { agent } from "./agent";
import type { EvidenceErrorCode, UploadCheckResponse, UploadedFileInfo, UploadErrorBody, UploadErrorCode } from "@/types";
import { writeSessionNotice } from "./session-notice";

const BACKEND_URL = process.env.NEXT_PUBLIC_BACKEND_URL || "http://localhost:8080";

export type AgentAuditAction = "startup" | "capture_start" | "capture_stop" | "screenshot" | "webcam";

function getToken(): string | null {
  if (typeof window === "undefined") return null;
  return localStorage.getItem("factum_token");
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
}

/* ── Corte de sesión (usuarios-locales §9.1) ── */

/** Evento global que escucha `SessionWatcher`. */
export const AUTH_EVENT = "factum:auth";
export type AuthEventDetail = { type: "session_ended" } | { type: "password_change_required" };

/** Rutas cuyo 401 es un resultado del formulario, no una sesión que se cortó. */
const AUTH_FAILURE_EXCLUDED = ["/api/auth/login", "/api/auth/mode"];

/**
 * Operaciones largas en curso (subida, generación, registro de evidencia).
 * Si la sesión se corta mientras alguna corre, el aviso del login lo dice.
 */
let activeLongOps = 0;

async function trackLongOp<T>(op: () => Promise<T>): Promise<T> {
  activeLongOps++;
  try {
    return await op();
  } finally {
    activeLongOps--;
  }
}

function dispatchAuthEvent(detail: AuthEventDetail) {
  window.dispatchEvent(new CustomEvent<AuthEventDetail>(AUTH_EVENT, { detail }));
}

/**
 * Interceptor de sesión: lo llaman `toApiError` y el XHR de `uploadFile`.
 * - 401 con token y fuera de login/mode: borra el token, deja el aviso para
 *   el login y avisa a `SessionWatcher` (que navega a `/`).
 * - 403 `password_change_required`: avisa a `SessionWatcher` (→ `/cambiar-contrasena`).
 * Se evalúa antes del `finally` de la operación que falló, así
 * `activeLongOps > 0` incluye a esa misma operación.
 */
function notifyAuthFailure(status: number, body: ApiErrorBody | null, url: string) {
  if (typeof window === "undefined") return;
  const code = typeof body?.code === "string" ? body.code : null;
  if (status === 401) {
    if (!getToken()) return;
    let path = url;
    try { path = new URL(url, BACKEND_URL).pathname; } catch { /* url relativa o vacía */ }
    if (AUTH_FAILURE_EXCLUDED.some(p => path.startsWith(p))) return;
    localStorage.removeItem("factum_token");
    writeSessionNotice({
      reason: code === "account_suspended" ? "account_suspended" : "session_ended",
      interrupted: activeLongOps > 0,
    });
    dispatchAuthEvent({ type: "session_ended" });
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

/** `fetch` al backend con el token y el `Content-Type` JSON. */
function send(path: string, options: RequestInit): Promise<Response> {
  const token = getToken();
  return fetch(`${BACKEND_URL}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...options.headers,
    },
  });
}

/** `ApiError` a partir de una respuesta no-OK (`{ error, missing }` del backend). */
async function toApiError(res: Response): Promise<ApiError> {
  const body = await res.json().catch(() => null);
  const serverMessage = typeof body?.error === "string" && body.error.trim() ? (body.error as string) : null;
  const missing = Array.isArray(body?.missing) ? (body.missing as string[]) : undefined;
  // `message` igual que antes: el `error` del body, o el statusText si el body
  // no era JSON, o "HTTP <status>" como último recurso.
  const fallback = body === null ? res.statusText : body?.error;
  const errBody = body && typeof body === "object" ? (body as ApiErrorBody) : null;
  notifyAuthFailure(res.status, errBody, res.url);
  return new ApiError(serverMessage ?? (fallback || `HTTP ${res.status}`), res.status, missing, serverMessage, errBody);
}

/**
 * Como `request`, pero una falla de red (backend caído, CORS) llega como
 * `ApiError` con `status` 0 y `SERVER_UNREACHABLE`. Lo usan las rutas nuevas.
 */
async function requestSafe<T>(path: string, options: RequestInit = {}): Promise<T> {
  let res: Response;
  try {
    res = await send(path, options);
  } catch {
    throw new ApiError(SERVER_UNREACHABLE, 0, undefined, null);
  }
  if (!res.ok) throw await toApiError(res);
  return res.json();
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const res = await send(path, options);
  if (!res.ok) throw await toApiError(res);
  return res.json();
}

/** Como `request`, para respuestas `204` sin body (no lee el body si salió bien). */
async function requestNoContent(path: string, options: RequestInit = {}): Promise<void> {
  const res = await send(path, options);
  if (!res.ok) throw await toApiError(res);
}

/**
 * Envío `multipart/form-data` con el token, sin `Content-Type` manual (lo pone
 * el navegador con el boundary). Red caída → `ApiError(SERVER_UNREACHABLE, 0)`.
 */
async function sendMultipart<T>(path: string, method: "POST" | "PUT", form: FormData): Promise<T> {
  const token = getToken();
  let res: Response;
  try {
    res = await fetch(`${BACKEND_URL}${path}`, {
      method,
      headers: token ? { Authorization: `Bearer ${token}` } : {},
      body: form,
      cache: "no-store",
    });
  } catch {
    throw new ApiError(SERVER_UNREACHABLE, 0, undefined, null);
  }
  if (!res.ok) throw await toApiError(res);
  return res.json() as Promise<T>;
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
}

/** Respuesta de `GET /api/cases/{id}/report-texts/defaults` (textos ya en Markdown). */
export interface ReportTextDefaults extends ReportTextsInput {
  formato: ReportTextFormat;
}

export interface ReportTexts extends ReportTextsInput {
  /** null/ausente = texto plano anterior a esta HU. "texto" no lo devuelve el servidor, pero se tolera. */
  formato?: ReportTextFormat | "texto" | null;
  updated_at?: string;
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

export const api = {
  async login(dni: string, username: string, password: string): Promise<{ token: string; user: User }> {
    const data = await request<{ token: string; user: User }>("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ dni, username, password }),
    });
    localStorage.setItem("factum_token", data.token);
    return data;
  },

  logout() {
    localStorage.removeItem("factum_token");
  },

  async me(): Promise<User> {
    const data = await request<{ user: User }>("/api/auth/me");
    return data.user;
  },

  /**
   * Cambio de contraseña propio (obligatorio o voluntario). Guarda el token
   * nuevo: el anterior (y las otras sesiones) deja de valer.
   */
  async changePassword(body: ChangePasswordRequest): Promise<{ token: string; user: User }> {
    const data = await requestSafe<{ token: string; user: User }>("/api/auth/change-password", {
      method: "POST",
      body: JSON.stringify(body),
    });
    localStorage.setItem("factum_token", data.token);
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
    const token = getToken();
    let res: Response;
    try {
      res = await fetch(`${BACKEND_URL}${url}`, { headers: token ? { Authorization: `Bearer ${token}` } : {}, signal });
    } catch (e) {
      if (signal?.aborted) throw e;
      throw new ApiError(SERVER_UNREACHABLE, 0, undefined, null);
    }
    if (!res.ok) throw await toApiError(res);
    return res.blob();
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
      res = await send(`/api/cases/${caseId}/files/upload-check?${params.toString()}`, { method: "GET", signal, cache: "no-store" });
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
      const token = getToken();
      if (token) xhr.setRequestHeader("Authorization", `Bearer ${token}`);
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
        notifyAuthFailure(xhr.status, errBody, xhr.responseURL);
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
    const token = getToken();
    const res = await fetch(
      `${BACKEND_URL}/api/cases/${caseId}/files/${encodeURIComponent(filename)}/preview`,
      { headers: token ? { Authorization: `Bearer ${token}` } : {}, signal },
    );
    if (!res.ok) throw await toApiError(res);
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
      const token = getToken();
      let res: Response;
      try {
        res = await fetch(`${BACKEND_URL}/api/cases/${caseId}/generate/finish`, {
          method: "POST",
          headers: token ? { Authorization: `Bearer ${token}` } : {},
          body: form,
        });
      } catch {
        throw new ApiError(SERVER_UNREACHABLE, 0, undefined, null);
      }
      if (!res.ok) throw await toApiError(res);
      return res.json() as Promise<GenerateResult>;
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
  async reportAgentEvent(action: AgentAuditAction, caseId?: string): Promise<void> {
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
          case_id: caseId,
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
