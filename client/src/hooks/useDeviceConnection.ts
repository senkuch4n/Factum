"use client";

import { useRef } from "react";
import type { Device } from "@/lib/agent";

/** Estado real del equipo elegido, visto desde Tatana (no viaja por JSON). */
export type DeviceConnection = "connected" | "device_offline" | "agent_offline";

/**
 * Deriva si el equipo `serial` está conectado a partir de lo que ya entrega
 * `useAgentConnection` (health + `GET /devices` + WS `devices_changed`).
 * Mismo criterio que el paso 3: conectado = Tatana en línea y el serial está
 * en la lista. Mientras Tatana recién reconectado lista los equipos
 * (`loadingDev`), se conserva el último valor para no parpadear "Desconectado".
 */
export function useDeviceConnection(
  agentOnline: boolean,
  devices: Device[],
  loadingDev: boolean,
  serial: string | null,
): DeviceConnection {
  const last = useRef<DeviceConnection | null>(null);

  let next: DeviceConnection;
  if (!agentOnline) next = "agent_offline";
  else if (!serial) next = "device_offline";
  else if (devices.some(d => d.serial === serial)) next = "connected";
  else if (loadingDev) next = last.current ?? "device_offline";
  else next = "device_offline";

  last.current = next;
  return next;
}
