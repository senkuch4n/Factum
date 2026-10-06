/**
 * Cliente del Agente Local (corre en localhost:8765 en la PC del operador).
 * El agente maneja ADB, scrcpy, webcam y exposición de archivos.
 */

import type { ManifestFile } from "./api";

export const AGENT_URL = process.env.NEXT_PUBLIC_AGENT_URL || "http://localhost:8765";
export function agentFileURL(filename: string) { return `${AGENT_URL}/files/${filename}`; }

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

export interface VideoVariant {
  label: string;
  filename: string;
  url: string;
}

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

/** `GET /health`. `capabilities` falta en un Tatana anterior a zip-local-informe-servidor. */
export interface AgentHealth {
  status: string;
  version: string;
  mock: boolean;
  capabilities?: string[];
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

export type AgentErrorCode =
  | "invalid_case_id" | "invalid_filename" | "file_not_found" | "file_exists" | "file_busy"
  | "length_required" | "file_too_large" | "insufficient_storage" | "incomplete_upload" | "storage_error"
  | "evidence_changed" | "zip_in_progress" | "zip_already_committed" | "zip_failed" | "zip_not_found"
  | "zip_hash_mismatch" | "origin_not_allowed";

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

  async takeScreenshot(serial: string, platform: "android" | "ios" = "android"): Promise<{ filename: string; url: string }> {
    const url = `${AGENT_URL}/devices/${serial}/screenshot${platform === "ios" ? "?platform=ios" : ""}`;
    const res = await fetch(url, { method: "POST" });
    if (!res.ok) throw new Error("Error tomando screenshot");
    return res.json();
  },

  // Sesión de "espejar para capturas" (iOS + AirPlay): conecta una vez, permite marcar
  // varios momentos mientras se navega libremente, y extrae un PNG por marca al finalizar.
  async startAirplayShot(serial: string): Promise<{ receiver_name: string }> {
    const res = await fetch(`${AGENT_URL}/devices/${serial}/screenshot/airplay/start`, { method: "POST" });
    if (!res.ok) throw new Error("Error iniciando sesión de captura AirPlay");
    return res.json();
  },

  async markAirplayShot(serial: string): Promise<{ count: number }> {
    const res = await fetch(`${AGENT_URL}/devices/${serial}/screenshot/airplay/mark`, { method: "POST" });
    if (!res.ok) throw new Error("Error marcando captura");
    return res.json();
  },

  async stopAirplayShot(serial: string): Promise<{ files: { filename: string; url: string }[] }> {
    const res = await fetch(`${AGENT_URL}/devices/${serial}/screenshot/airplay/stop`, { method: "POST" });
    if (!res.ok) throw new Error("Error finalizando sesión de captura AirPlay");
    return res.json();
  },

  async startRecording(
    serial: string,
    androidVersion: number,
    platform: "android" | "ios" = "android",
    iosMode?: "video_only" | "with_mic" | "on_device" | "airplay",
    androidWithMic?: boolean,
  ): Promise<{ filename: string }> {
    let res: Response;
    try {
      res = await fetch(`${AGENT_URL}/devices/${serial}/record/start`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          android_version: androidVersion, platform, ios_mode: iosMode,
          android_with_mic: androidWithMic,
        }),
      });
    } catch {
      throw new Error(AGENT_UNREACHABLE);
    }
    if (!res.ok) throw await readAgentError(res, "Error iniciando grabación");
    return res.json();
  },

  async stopRecording(serial: string, platform: "android" | "ios" = "android"): Promise<{ filename: string; url: string }> {
    const url = `${AGENT_URL}/devices/${serial}/record/stop${platform === "ios" ? "?platform=ios" : ""}`;
    let res: Response;
    try {
      res = await fetch(url, { method: "POST" });
    } catch {
      throw new Error(AGENT_UNREACHABLE);
    }
    if (!res.ok) throw await readAgentError(res, "Error deteniendo grabación");
    return res.json();
  },

  async captureWebcam(type: "funcionario" | "denunciante"): Promise<{ filename: string; url: string }> {
    const res = await fetch(`${AGENT_URL}/webcam/capture`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ type }),
    });
    if (!res.ok) throw new Error("Error capturando foto");
    return res.json();
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
