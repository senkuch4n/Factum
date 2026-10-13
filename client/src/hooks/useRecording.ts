"use client";

import { useState, useRef, useEffect, useCallback } from "react";
import {
  agent, AgentError,
  type AgentRecordingState, type RecordingPhase, type RecordingStateEntry,
  type RecordingValidation, type UnclaimedRecording,
} from "@/lib/agent";
import { agentErrorMessage } from "@/lib/agent-messages";
import type { Device, IosRecordMode } from "@/types";

/** Ids de caso (UUID) sin distinguir mayúsculas; `null`/vacío nunca coincide. */
export function sameId(a: string | null | undefined, b: string | null | undefined): boolean {
  return !!a && !!b && a.toLowerCase() === b.toLowerCase();
}

export const FOREIGN_STOPPED_MESSAGE = "La grabación se detuvo y quedó en Tatana. No se agregó a este caso.";

interface RecordingOpts {
  /** Caso abierto (por ref en el llamador): se manda como `case_id` y define qué grabación es "propia". */
  getCaseId: () => string | null;
  /** Serial del celular seleccionado. */
  getSerial: () => string | null;
  /** Validación de Tatana de una grabación agregada (aviso de incompleta). */
  onValidation: (filename: string, v: RecordingValidation) => void;
  /** Grabaciones detenidas de este caso que nadie importó (`unclaimed_recordings`). */
  onUnclaimed?: (list: UnclaimedRecording[]) => void;
}

/** Texto de un error de grabación (los códigos de §7.2 y el `error` de Tatana tal cual). */
function recordErrorText(e: unknown, fallback: string): string {
  if (e instanceof AgentError) return agentErrorMessage(e, { action: "record" });
  return e instanceof Error && e.message ? e.message : fallback;
}

/**
 * Grabación de pantalla del celular (grabacion-android-robusta §7.5). La única
 * fuente de verdad sobre si hay una grabación en curso es Tatana
 * (`GET /agent/state`): el estado local es optimista y `refresh()` lo
 * reconcilia. Con un Tatana sin `recording_state_v1`, se comporta como antes.
 */
export function useRecording(
  addFile: (name: string) => void,
  /** Marca las variantes planeadas por Tatana (solo visual; no bloquea el guardado). */
  expectVariants: (filename: string, labels: string[]) => void,
  opts: RecordingOpts,
) {
  const [isRecording, setRecording]               = useState(false);
  const [disconnectedDuringRecord, setDiscoRec]   = useState(false);
  const [deviceOffline, setDeviceOffline]         = useState(false);
  const [iosModePicker, setIosModePicker]         = useState(false);
  const [iosRecordMode, setIosRecordMode]         = useState<IosRecordMode | null>(null);
  const [airplayReceiverName, setAirplayName]     = useState<string | null>(null);
  const [androidWithMic, setAndroidWithMic]       = useState(false);
  const [loadingRec, setLoadingRec]               = useState<Record<string, boolean>>({});
  /** Inicio real de la grabación según Tatana (ISO-8601). */
  const [startedAt, setStartedAt]                 = useState<string | null>(null);
  /** Fase que informa Tatana para la grabación de este caso. */
  const [remotePhase, setRemotePhase]             = useState<RecordingPhase | null>(null);
  /** Grabación en curso de otro caso, de origen desconocido o de otro celular. */
  const [foreignRecording, setForeignRecording]   = useState<RecordingStateEntry | null>(null);
  /** El estado vino de `/agent/state` y no de un start de esta pestaña. */
  const [recovered, setRecovered]                 = useState(false);
  const isRecordingRef                            = useRef(false);

  // Getters por ref: `refresh` y `syncFromAgentState` no se recrean.
  const optsRef = useRef(opts);
  useEffect(() => { optsRef.current = opts; });

  /** Pedidos en vuelo de esta pestaña (para no pisar el estado optimista). */
  const inFlightRef = useRef({ start: false, stop: false });
  /** Filenames detenidos desde el aviso de "otro caso": nunca se importan acá. */
  const foreignStopsRef = useRef<Set<string>>(new Set());
  /** Se incrementa al terminar un start/stop: descarta un `/agent/state` leído antes. */
  const genRef = useRef(0);
  const refreshBusyRef = useRef(false);
  const refreshQueuedRef = useRef(false);

  useEffect(() => { isRecordingRef.current = isRecording; }, [isRecording]);

  function setLoad(key: string, val: boolean) {
    setLoadingRec(l => ({ ...l, [key]: val }));
  }

  /**
   * Aplica el estado de Tatana. Propia = mismo serial y mismo `case_id`. Devuelve
   * las `unclaimed_recordings` de este caso, para que el llamador las importe.
   */
  const syncFromAgentState = useCallback((
    state: AgentRecordingState,
    ctx: { serial: string | null; caseId: string | null },
  ): UnclaimedRecording[] => {
    const busy = inFlightRef.current.start || inFlightRef.current.stop;
    const own = state.recordings.find(r => r.serial === ctx.serial && sameId(r.case_id, ctx.caseId));
    if (own) {
      setRecording(own.state !== "starting");
      setStartedAt(own.started_at ?? null);
      setRemotePhase(own.state);
      if (own.platform === "ios") setIosRecordMode(own.ios_mode ?? null);
      else setAndroidWithMic(!!own.android_with_mic);
      setForeignRecording(null);
      if (!busy) setRecovered(r => r || !isRecordingRef.current);
    } else {
      setForeignRecording(state.recordings[0] ?? null);
      if (!busy) {
        setRecording(false);
        setStartedAt(null);
        setRemotePhase(null);
        setRecovered(false);
      }
    }
    return state.unclaimed_recordings.filter(u => !!u.filename && sameId(u.case_id, ctx.caseId));
  }, []);

  /** Consulta `/agent/state` y reconcilia. Una ejecución a la vez; si llega otra, se encola una sola. */
  const refresh = useCallback(async () => {
    if (refreshBusyRef.current) { refreshQueuedRef.current = true; return; }
    refreshBusyRef.current = true;
    try {
      do {
        refreshQueuedRef.current = false;
        if (!(await agent.supportsRecordingState())) return;
        const gen = genRef.current;
        const s = await agent.agentState();
        if (!s) continue;
        // Terminó un start/stop mientras se leía: la foto es vieja, se vuelve a pedir.
        if (gen !== genRef.current) { refreshQueuedRef.current = true; continue; }
        const o = optsRef.current;
        const mine = syncFromAgentState(s, { serial: o.getSerial(), caseId: o.getCaseId() });
        if (mine.length > 0) o.onUnclaimed?.(mine);
      } while (refreshQueuedRef.current);
    } finally {
      refreshBusyRef.current = false;
    }
  }, [syncFromAgentState]);

  async function startRecording(device: Device, onError: (msg: string) => void, iosMode?: IosRecordMode) {
    const platform = device.platform ?? "android";
    inFlightRef.current.start = true;
    setLoad("startRecord", true);
    try {
      const res = await agent.startRecording(
        device.serial, device.android_version, platform, iosMode,
        platform === "android" ? androidWithMic : undefined,
        optsRef.current.getCaseId(),
      );
      setRecording(true);
      setStartedAt(res?.started_at ?? new Date().toISOString());
      setRemotePhase(null);
      setRecovered(false);
      setForeignRecording(null);
    } catch (e) {
      onError(recordErrorText(e, "Error al iniciar grabación"));
      if (platform === "ios") setIosRecordMode(null);
      // Otra grabación en curso: el aviso de grabación ajena sale de `/agent/state`.
      if (e instanceof AgentError && e.code === "already_recording") void refresh();
    } finally {
      inFlightRef.current.start = false;
      genRef.current++;
      setLoad("startRecord", false);
    }
  }

  async function handleToggleRecord(device: Device, onError: (msg: string) => void) {
    const platform = device.platform ?? "android";
    if (isRecording) {
      inFlightRef.current.stop = true;
      setLoad("stopRecord", true);
      try {
        const stopped = await agent.stopRecording(device.serial, platform);
        if (stopped?.filename) {
          addFile(stopped.filename);
          if (stopped.validation) optsRef.current.onValidation(stopped.filename, stopped.validation);
          // Sin `variants_expected` (Tatana viejo) no se marca nada (DA2).
          if (platform === "ios" && stopped.variants_expected) expectVariants(stopped.filename, stopped.variants ?? []);
        }
      } catch (e) {
        // El agente ya limpió su estado: reintentar el stop no sirve, así que
        // igual se sale del modo grabación (abajo) y se avisa el motivo real.
        if (e instanceof AgentError && (e.code === "recording_empty" || e.code === "recording_unreadable" || e.code === "not_recording")) {
          onError(agentErrorMessage(e, { action: "record" }));
        } else {
          onError(`No se pudo guardar la grabación: ${e instanceof Error ? e.message : "error desconocido"}`);
        }
      } finally {
        inFlightRef.current.stop = false;
        genRef.current++;
        setLoad("stopRecord", false);
        setRecording(false);
        setStartedAt(null);
        setRemotePhase(null);
        setRecovered(false);
        setIosRecordMode(null);
      }
    } else if (platform === "ios") {
      setIosModePicker(true);
    } else {
      await startRecording(device, onError);
    }
  }

  async function handleSelectIosMode(mode: IosRecordMode, device: Device, onError: (msg: string) => void) {
    setIosModePicker(false);
    setIosRecordMode(mode);
    setAirplayName(null);
    await startRecording({ ...device, platform: "ios" }, onError, mode);
  }

  /**
   * Detiene la grabación de otro caso / de origen desconocido. **Nunca** la
   * agrega a este caso: queda en Tatana y la recupera el caso que la inició.
   */
  async function stopForeign(onError: (msg: string) => void, onInfo: (msg: string) => void) {
    const f = foreignRecording;
    if (!f) return;
    if (f.filename) foreignStopsRef.current.add(f.filename);
    setLoad("stopForeign", true);
    try {
      await agent.stopRecording(f.serial, f.platform);
      onInfo(FOREIGN_STOPPED_MESSAGE);
    } catch (e) {
      if (e instanceof AgentError && e.code === "not_recording") onInfo(agentErrorMessage(e, { action: "record" }));
      else onError(recordErrorText(e, "No se pudo detener la grabación."));
    } finally {
      setLoad("stopForeign", false);
      void refresh();
    }
  }

  function resetRecording() {
    genRef.current++;
    setRecording(false);
    setDiscoRec(false);
    setDeviceOffline(false);
    setIosModePicker(false);
    setIosRecordMode(null);
    setAirplayName(null);
    setAndroidWithMic(false);
    setStartedAt(null);
    setRemotePhase(null);
    setForeignRecording(null);
    setRecovered(false);
  }

  return {
    isRecording, disconnectedDuringRecord, deviceOffline,
    iosModePicker, iosRecordMode, airplayReceiverName, androidWithMic,
    startedAt, remotePhase, foreignRecording, recordingRecovered: recovered,
    isRecordingRef, foreignStopsRef, inFlightRef, loading: loadingRec,
    setRecording, setDiscoRec, setDeviceOffline, setAirplayName, setIosModePicker, setAndroidWithMic,
    handleToggleRecord, handleSelectIosMode, resetRecording, refresh, stopForeign,
  };
}
