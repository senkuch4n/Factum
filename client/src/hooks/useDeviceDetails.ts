"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { agent, AgentError, type AgentDeviceDetails } from "@/lib/agent";
import { agentErrorMessage } from "@/lib/agent-messages";
import type { DeviceDetailsStatus } from "@/lib/device-details";

/** Tope de la lectura completa (el peor caso de Tatana es ~27 s, SDD §4.2). */
const READ_TIMEOUT_MS = 45_000;

export interface DeviceDetailsState {
  status: DeviceDetailsStatus;
  reading: AgentDeviceDetails | null;
  /** `Date.now()` al terminar la lectura (para reusarla ≤ 60 s, D-T5). */
  readAt: number | null;
  error: string | null;
}

const IDLE: DeviceDetailsState = { status: "idle", reading: null, readAt: null, error: null };

const NOT_FOUND_MESSAGE = "El dispositivo no está conectado o no autorizó la conexión.";
const TIMEOUT_MESSAGE = "El dispositivo no respondió a tiempo. Volvé a leer.";

/**
 * Ficha técnica del dispositivo leída de Tatana bajo demanda (metadatos-dispositivo
 * §10.1). Una sola lectura a la vez: `read()` con una en curso devuelve esa misma
 * promesa. Se resetea (y cancela) al cambiar de dispositivo.
 *
 * `read()` nunca lanza: devuelve la lectura o `null` (Tatana viejo o error, que
 * queda en `status`/`error`). Nunca loguea valores.
 */
export function useDeviceDetails(device: { serial: string; platform?: "android" | "ios" } | null) {
  const serial = device?.serial ?? null;
  const platform = device?.platform ?? "android";
  const [state, setState] = useState<DeviceDetailsState>(IDLE);
  const stateRef = useRef(state);
  stateRef.current = state;
  const ctrlRef = useRef<AbortController | null>(null);
  const inflightRef = useRef<{ serial: string; promise: Promise<AgentDeviceDetails | null> } | null>(null);
  const serialRef = useRef(serial);
  serialRef.current = serial;

  const set = useCallback((next: DeviceDetailsState) => {
    stateRef.current = next;
    setState(next);
  }, []);

  const reset = useCallback(() => {
    ctrlRef.current?.abort();
    ctrlRef.current = null;
    inflightRef.current = null;
    set(IDLE);
  }, [set]);

  // Otro dispositivo (o ninguno): lo leído no vale.
  useEffect(() => {
    reset();
    return () => { ctrlRef.current?.abort(); };
  }, [serial, reset]);

  const read = useCallback((): Promise<AgentDeviceDetails | null> => {
    const s = serialRef.current;
    if (!s) return Promise.resolve(null);
    if (inflightRef.current?.serial === s) return inflightRef.current.promise;

    const ctrl = new AbortController();
    ctrlRef.current = ctrl;
    let timedOut = false;
    const timer = window.setTimeout(() => { timedOut = true; ctrl.abort(); }, READ_TIMEOUT_MS);
    const prev = stateRef.current;
    set({ status: "loading", reading: prev.reading, readAt: prev.readAt, error: null });

    const promise = (async (): Promise<AgentDeviceDetails | null> => {
      try {
        if (!(await agent.supportsDeviceDetails())) {
          if (!ctrl.signal.aborted) set({ ...IDLE, status: "unsupported" });
          return null;
        }
        const reading = await agent.getDeviceDetails(s, platform, ctrl.signal);
        if (ctrl.signal.aborted || serialRef.current !== s) return null;
        set({ status: "ready", reading, readAt: Date.now(), error: null });
        return reading;
      } catch (e) {
        // Abort por cambio de dispositivo o desmontaje: nada que mostrar.
        if (ctrl.signal.aborted && !timedOut) return null;
        const message = timedOut
          ? TIMEOUT_MESSAGE
          : e instanceof AgentError && e.code === "device_not_found"
            ? NOT_FOUND_MESSAGE
            : agentErrorMessage(e, { action: "preview" });
        if (serialRef.current === s) set({ status: "error", reading: null, readAt: null, error: message });
        return null;
      } finally {
        window.clearTimeout(timer);
        if (inflightRef.current?.promise === promise) inflightRef.current = null;
        if (ctrlRef.current === ctrl) ctrlRef.current = null;
      }
    })();
    inflightRef.current = { serial: s, promise };
    return promise;
  }, [platform, set]);

  /** Estado vigente sin esperar un render (lo usa el alta del caso). */
  const latest = useCallback(() => stateRef.current, []);

  return { ...state, read, reset, latest };
}
