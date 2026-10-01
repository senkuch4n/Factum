"use client";

import { useState, useEffect, useRef } from "react";
import { agent } from "@/lib/agent";
import type { Device, AgentEvent } from "@/types";

export function useAgentConnection(onEvent: (e: AgentEvent) => void) {
  const [agentOnline, setAgent] = useState(false);
  const [devices, setDevices]   = useState<Device[]>([]);
  const [loadingDev, setLoadDev] = useState(false);
  const onEventRef = useRef(onEvent);
  onEventRef.current = onEvent;

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
        const res = await fetch(
          `${process.env.NEXT_PUBLIC_AGENT_URL || "http://localhost:8765"}/health`,
          { signal: AbortSignal.timeout(5000) },
        );
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
