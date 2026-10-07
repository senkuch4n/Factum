"use client";

import { useEffect, useState } from "react";
import { agent, type AgentHealth, type AirplayUnavailableReason, type AppleServiceState } from "@/lib/agent";

/**
 * Estado de iPhone que informa Tatana en `/health.ios` (ios-herramientas-windows, SDD §9.3).
 * Revalida cada 10 s mientras `enabled` y la pestaña está visible: el servicio de Apple
 * cambia cuando el perito instala "Apple Devices", así que no se cachea a nivel módulo
 * (a diferencia de `useAgentIdentity`).
 *
 * Tatana caído o viejo (sin `ios`): `appleService = null`, AirPlay disponible y sin
 * botón de Modo Desarrollador — el comportamiento de antes.
 */
export interface AgentIosStatusState {
  appleService: AppleServiceState | null;
  airplayAvailable: boolean;
  airplayReason: AirplayUnavailableReason | null;
  supportsDeveloperMode: boolean;
}

const POLL_MS = 10_000;

const UNKNOWN: AgentIosStatusState = {
  appleService: null,
  airplayAvailable: true,
  airplayReason: null,
  supportsDeveloperMode: false,
};

function fromHealth(h: AgentHealth): AgentIosStatusState {
  const supportsDeveloperMode = h.capabilities?.includes("ios_developer_mode_v1") ?? false;
  if (!h.ios) return { ...UNKNOWN, supportsDeveloperMode };
  return {
    appleService: h.ios.apple_service,
    airplayAvailable: h.ios.airplay_available,
    airplayReason: h.ios.airplay_available ? null : h.ios.airplay_unavailable_reason ?? null,
    supportsDeveloperMode,
  };
}

function sameState(a: AgentIosStatusState, b: AgentIosStatusState): boolean {
  return a.appleService === b.appleService && a.airplayAvailable === b.airplayAvailable
    && a.airplayReason === b.airplayReason && a.supportsDeveloperMode === b.supportsDeveloperMode;
}

export function useAgentIosStatus(enabled = true): AgentIosStatusState {
  const [state, setState] = useState<AgentIosStatusState>(UNKNOWN);

  useEffect(() => {
    if (!enabled) return;
    let alive = true;
    let busy = false;

    const load = async () => {
      if (busy || document.visibilityState !== "visible") return;
      busy = true;
      let next: AgentIosStatusState;
      try {
        next = fromHealth(await agent.health());
      } catch {
        next = UNKNOWN;
      } finally {
        busy = false;
      }
      // Sin re-render si no cambió nada (corre cada 10 s en el dashboard).
      if (alive) setState(prev => (sameState(prev, next) ? prev : next));
    };

    void load();
    const id = window.setInterval(() => { void load(); }, POLL_MS);
    // Al volver a la pestaña (p. ej. después de instalar "Apple Devices") revalida enseguida.
    const onVisible = () => { if (document.visibilityState === "visible") void load(); };
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      alive = false;
      window.clearInterval(id);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, [enabled]);

  return enabled ? state : UNKNOWN;
}
