/**
 * Cliente del Agente Local (corre en localhost:8765 en la PC del operador).
 * El agente maneja ADB, scrcpy, webcam y exposición de archivos.
 */

export const AGENT_URL = process.env.NEXT_PUBLIC_AGENT_URL || "http://localhost:8765";
export function agentFileURL(filename: string) { return `${AGENT_URL}/files/${filename}`; }

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
    const res = await fetch(`${AGENT_URL}/devices/${serial}/record/start`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        android_version: androidVersion, platform, ios_mode: iosMode,
        android_with_mic: androidWithMic,
      }),
    });
    if (!res.ok) throw new Error("Error iniciando grabación");
    return res.json();
  },

  async stopRecording(serial: string, platform: "android" | "ios" = "android"): Promise<{ filename: string; url: string }> {
    const url = `${AGENT_URL}/devices/${serial}/record/stop${platform === "ios" ? "?platform=ios" : ""}`;
    const res = await fetch(url, { method: "POST" });
    if (!res.ok) throw new Error("Error deteniendo grabación");
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

  /** Descarga un archivo del agente como Blob para subirlo al backend. */
  async downloadFile(filename: string): Promise<Blob> {
    const res = await fetch(`${AGENT_URL}/files/${filename}`);
    if (!res.ok) throw new Error(`Error descargando ${filename}`);
    return res.blob();
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
