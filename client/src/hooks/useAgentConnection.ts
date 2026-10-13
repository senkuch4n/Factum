"use client";

import { useState, useEffect, useRef } from "react";
import { agent, AGENT_URL } from "@/lib/agent";
import type { Device, AgentEvent } from "@/types";

/**
 * Conexión con Tatana (health + WebSocket, con reintento cada 3 s).
 * `onConnected` se llama cada vez que el WebSocket abre: en la primera conexión
 * y en cada reconexión (ios-grabacion-larga, Duda 9 A).
 */
export function useAgentConnection(onEvent: (e: AgentEvent) => void, onConnected?: () => void) {
  const [agentOnline, setAgent] = useState(false);
  const [devices, setDevices]   = useState<Device[]>([]);
  const [loadingDev, setLoadDev] = useState(false);
  const onEventRef = useRef(onEvent);
  onEventRef.current = onEvent;
  const onConnectedRef = useRef(onConnected);
  onConnectedRef.current = onConnected;

  useEffect(() => {
    let ws: WebSocket | null = null;
    let retryTimer: ReturnType<typeof setTimeout> | null = null;
    let destroyed = false;

    function scheduleRetry() {
      if (destroyed) return;
      ws = null;
      setAgent(false);
      setDevices([]);
      retryTimer = setTimeout(tryConnect, 3000);
    }

    async function tryConnect() {
      if (destroyed) return;
      let online = false;
      try {
        const res = await fetch(`${AGENT_URL}/health`, { signal: AbortSignal.timeout(5000) });
        online = res.ok;
      } catch {
        online = false;
      }
      if (destroyed) return;
      setAgent(online);
      if (!online) { retryTimer = setTimeout(tryConnect, 3000); return; }
      setLoadDev(true);
      agent.listDevices().then(setDevices).finally(() => setLoadDev(false));
      ws = agent.connectWS(e => {
        if (e.type === "devices_changed") {
          const updated = (e.data as { devices: Device[] }).devices || [];
          setDevices(updated);
        }
        onEventRef.current(e);
      });
      ws.onopen = () => { if (!destroyed) onConnectedRef.current?.(); };
      ws.onerror = () => { if (!destroyed) scheduleRetry(); };
      ws.onclose = () => { if (!destroyed) scheduleRetry(); };
    }

    tryConnect();
    return () => {
      destroyed = true;
      if (retryTimer) clearTimeout(retryTimer);
      ws?.close();
    };
  }, []);

  function refreshDevices() {
    setLoadDev(true);
    agent.listDevices().then(setDevices).finally(() => setLoadDev(false));
  }

  return { agentOnline, devices, setDevices, loadingDev, refreshDevices };
}
