/**
 * Cámara del procedimiento recordada en esta PC (grabacion-sesion-completa-f2 D5, §9.1).
 * Solo id y nombre: el micrófono no se recuerda (la casilla arranca apagada).
 * `localStorage` puede fallar (modo privado, cuota): se ignora.
 */

export interface SessionCameraPref {
  id: string;
  name: string;
}

const KEY = "factum:session-camera";

export function loadSessionCamera(): SessionCameraPref | null {
  try {
    if (typeof window === "undefined") return null;
    const raw = window.localStorage.getItem(KEY);
    if (!raw) return null;
    const v = JSON.parse(raw) as Partial<SessionCameraPref> | null;
    if (!v || typeof v.id !== "string" || typeof v.name !== "string" || !v.name) return null;
    return { id: v.id, name: v.name };
  } catch {
    return null;
  }
}

export function saveSessionCamera(cam: SessionCameraPref): void {
  try {
    if (typeof window === "undefined") return;
    window.localStorage.setItem(KEY, JSON.stringify({ id: cam.id, name: cam.name }));
  } catch {
    /* modo privado o sin cuota: no se recuerda */
  }
}
