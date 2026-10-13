"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { agent, RECORDING_OPERATION_KINDS } from "@/lib/agent";

const VISIBLE_THROTTLE_MS = 5000;
const REC_POLL_MS = 15000;

/**
 * ¿Hay una grabación en curso en la PC (cualquier pestaña)? Lee
 * `GET /agent/state` → `operations` (SDD wizard-pulido-ux §8.3, DT3/DT4).
 * Sin sondeo fijo: se relee al conectar, con `refresh()` (eventos WS que
 * dispara el dashboard) y al volver la pestaña a visible. Solo con REC
 * encendido hay un sondeo de 15 s para apagar un REC cuyo evento se perdió.
 */
export function useAgentRecordingActivity(agentOnline: boolean): {
  remoteRecording: boolean;
  refresh: () => void;
} {
  const [remoteRecording, setRemoteRecording] = useState(false);
  const onlineRef = useRef(agentOnline);
  const inFlightRef = useRef(false);
  const queuedRef = useRef(false);
  const lastVisibleRefreshRef = useRef(0);
  const mountedRef = useRef(true);

  useEffect(() => {
    onlineRef.current = agentOnline;
  }, [agentOnline]);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const refresh = useCallback(() => {
    if (inFlightRef.current) {
      queuedRef.current = true;
      return;
    }
    inFlightRef.current = true;
    void (async () => {
      try {
        do {
          queuedRef.current = false;
          const ops = await agent.agentOperations();
          if (!mountedRef.current) return;
          if (ops) {
            const kinds: readonly string[] = RECORDING_OPERATION_KINDS;
            setRemoteRecording(ops.some(o => kinds.includes(o.kind)));
          } else if (!onlineRef.current) {
            setRemoteRecording(false);
          }
          // Un error suelto con Tatana en línea conserva el valor anterior.
        } while (queuedRef.current);
      } finally {
        inFlightRef.current = false;
      }
    })();
  }, []);

  // Conectar → leer; desconectar → apagar.
  useEffect(() => {
    if (agentOnline) refresh();
    else setRemoteRecording(false);
  }, [agentOnline, refresh]);

  // Volver a la pestaña → releer (throttle 5 s).
  useEffect(() => {
    const onVisibility = () => {
      if (document.visibilityState !== "visible" || !onlineRef.current) return;
      const now = Date.now();
      if (now - lastVisibleRefreshRef.current < VISIBLE_THROTTLE_MS) return;
      lastVisibleRefreshRef.current = now;
      refresh();
    };
    document.addEventListener("visibilitychange", onVisibility);
    return () => document.removeEventListener("visibilitychange", onVisibility);
  }, [refresh]);

  // Solo con REC encendido: sondeo de 15 s con la pestaña visible.
  useEffect(() => {
    if (!remoteRecording || !agentOnline) return;
    const id = window.setInterval(() => {
      if (document.visibilityState === "visible") refresh();
    }, REC_POLL_MS);
    return () => window.clearInterval(id);
  }, [remoteRecording, agentOnline, refresh]);

  return { remoteRecording, refresh };
}
