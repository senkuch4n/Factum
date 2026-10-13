import type { Device } from "@/lib/agent";
import type { DeviceConnection } from "@/hooks/useDeviceConnection";

export type AgentChipTone = "success" | "neutral" | "warning";
export type AgentChipIcon = "wifi" | "wifi-off" | "unplug";
export type AgentChipKind = "offline" | "listing" | "empty" | "single" | "many" | "selected" | "selected_offline";

export interface AgentChipInput {
  online: boolean;
  devices: Device[];
  /** `loadingDev`: Tatana recién (re)conectado está listando los equipos. */
  loading: boolean;
  /** Equipo de la inspección (pasos 2 a 6 del wizard); null fuera de ese contexto. */
  selected: Device | null;
  /** `useDeviceConnection(selected?.serial)`. */
  connection: DeviceConnection;
  /** Grabación de esta pestaña o cualquier grabación en la PC (Tatana). */
  recording: boolean;
}

export interface AgentChipView {
  kind: AgentChipKind;
  tone: AgentChipTone;
  icon: AgentChipIcon;
  /** Texto visible de la píldora. */
  text: string;
  /** Nombre accesible completo (incluye " · grabando"). */
  label: string;
  /** Lista de equipos para el tooltip en "many"; si no, null. */
  tip: string | null;
  recording: boolean;
}

/** Primera letra en mayúscula solo si el fabricante viene todo en minúscula ("samsung" → "Samsung"). */
function cap(s: string | null | undefined): string {
  const t = (s ?? "").trim();
  if (!t || t !== t.toLowerCase()) return t;
  return t.charAt(0).toUpperCase() + t.slice(1);
}

function deviceName(d: Device): string {
  return `${cap(d.manufacturer)} ${d.model ?? ""}`.trim();
}

/** "Samsung SM-M127F · Android 11" / "Apple iPhone 13 · iOS 17.5". */
export function deviceChipLabel(d: Device): string {
  const os = d.platform === "ios" ? `iOS ${d.ios_version ?? d.android_version}` : `Android ${d.android_version}`;
  return `${deviceName(d)} · ${os}`;
}

/**
 * Qué muestra el chip de Tatana del header (SDD wizard-pulido-ux §8.2). Gana
 * la primera fila que aplique: offline → equipo elegido → listando → 0/1/N.
 */
export function deriveAgentChip(i: AgentChipInput): AgentChipView {
  const g = i.recording ? " · grabando" : "";
  const base = { recording: i.recording, tip: null as string | null };

  if (!i.online) {
    const text = "Tatana no disponible";
    return { ...base, kind: "offline", tone: "warning", icon: "wifi-off", text, label: text + g };
  }
  if (i.selected) {
    if (i.connection === "connected") {
      const text = deviceChipLabel(i.selected);
      return { ...base, kind: "selected", tone: "success", icon: "wifi", text, label: `Tatana activo: ${text}${g}` };
    }
    const text = `${deviceName(i.selected)} · desconectado`;
    return { ...base, kind: "selected_offline", tone: "warning", icon: "unplug", text, label: `Tatana activo: ${text}${g}` };
  }
  const n = i.devices.length;
  if (n === 0 && i.loading) {
    const text = "Tatana activo";
    return { ...base, kind: "listing", tone: "neutral", icon: "wifi", text, label: text + g };
  }
  if (n === 0) {
    const text = "Tatana activo · sin equipos conectados";
    return { ...base, kind: "empty", tone: "neutral", icon: "wifi", text, label: text + g };
  }
  if (n === 1) {
    const text = deviceChipLabel(i.devices[0]);
    return { ...base, kind: "single", tone: "success", icon: "wifi", text, label: `Tatana activo: ${text}${g}` };
  }
  const labels = i.devices.map(deviceChipLabel);
  const text = `${n} equipos conectados`;
  return {
    ...base,
    kind: "many",
    tone: "success",
    icon: "wifi",
    text,
    label: `Tatana activo: ${text}: ${labels.join("; ")}${g}`,
    tip: labels.join("\n"),
  };
}
