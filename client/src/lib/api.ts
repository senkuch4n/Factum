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

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const token = getToken();
  const res = await fetch(`${BACKEND_URL}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...options.headers,
    },
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({ error: res.statusText }));
    throw new Error(err.error || `HTTP ${res.status}`);
  }
  return res.json();
}

export interface User {
  dni: string;
  name: string;
  sigla: string;
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
  zip_password?: string;
  zip_filename?: string;
  pdf_filename?: string;
}

/** Config pública del backend (`GET /api/config/public`, sin auth). */
export interface PublicConfig {
  organization_name: string | null;
  /** Ruta relativa a la raíz del backend (empieza con `/`), con `?v=` para la caché. */
  organization_logo_url: string | null;
}

export interface DeviceInput {
  serial: string;
  manufacturer: string;
  model: string;
  android_version: number;
  imei: string;
  platform?: "android" | "ios";
  os_version?: string;
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

  async createCase(data: {
    nro_referencia: string;
    nombre_denunciante: string;
    dni_denunciante: string;
    observaciones: string;
    device: DeviceInput;
  }): Promise<Case> {
    return request<Case>("/api/cases", { method: "POST", body: JSON.stringify(data) });
  },

  async getCase(id: string): Promise<{ case: Case; files: unknown[] }> {
    return request(`/api/cases/${id}`);
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

  async listFiles(caseId: string): Promise<{ files: { name: string; size: number; hash: string }[] }> {
    return request(`/api/cases/${caseId}/files`);
  },

  async listCases(): Promise<{ cases: Case[] }> {
    return request("/api/cases");
  },

  async generateCase(caseId: string): Promise<{
    case: Case;
    zip_hash: string;
    password: string;
    files: { zip: string; pdf: string };
  }> {
    return request(`/api/cases/${caseId}/generate`, { method: "POST" });
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

  async getMode(): Promise<"dev" | "mpf"> {
    const data = await request<{ mode: "dev" | "mpf" }>("/api/auth/mode");
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
