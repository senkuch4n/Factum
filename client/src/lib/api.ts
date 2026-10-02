/**
 * Cliente del Backend (corre en el servidor, accesible desde la web).
 */

import { agent } from "./agent";

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
  constructor(message: string, status: number, missing?: string[], serverMessage: string | null = null) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.missing = missing;
    this.serverMessage = serverMessage;
  }
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
  return new ApiError(serverMessage ?? (fallback || `HTTP ${res.status}`), res.status, missing, serverMessage);
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

export interface User {
  dni: string;
  name: string;
  sigla: string;
}

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
}

/** Modo de autenticación del backend (`GET /api/auth/mode`). */
export type AuthMode = "dev" | "external";

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
   * Sube un archivo desde el agente al backend bajo un caso específico.
   * `sourcePath` (opcional) es la ruta de origen en el dispositivo — solo aplica a archivos
   * traídos con el explorador de archivos, y queda registrada en el informe forense.
   */
  async uploadFile(caseId: string, filename: string, blob: Blob, sourcePath?: string): Promise<{ filename: string; hash: string }> {
    const token = getToken();
    const params = new URLSearchParams({ filename });
    if (sourcePath) params.set("source_path", sourcePath);
    const res = await fetch(`${BACKEND_URL}/api/cases/${caseId}/files?${params.toString()}`, {
      method: "POST",
      headers: {
        "Content-Type": blob.type || "application/octet-stream",
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: blob,
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      throw new Error(err.error || `Upload failed: ${res.status}`);
    }
    return res.json();
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

  async generateCase(caseId: string): Promise<{
    case: Case;
    zip_hash: string;
    report_hash: string;
    /** Contraseña del ZIP (una sola vez); `null` si el ZIP se generó sin cifrar. */
    password: string | null;
    files: { zip: string; pdf: string };
  }> {
    return request(`/api/cases/${caseId}/generate`, { method: "POST" });
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

  async getMode(): Promise<AuthMode> {
    const data = await request<{ mode: AuthMode }>("/api/auth/mode");
    return data.mode;
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
