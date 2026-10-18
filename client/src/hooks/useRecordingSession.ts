"use client";

/**
 * Grabación de la sesión completa del paso 3 (grabacion-sesion-completa, SDD §9.2).
 *
 * Tatana es la fuente de verdad mientras se graba (diario en disco por caso). Este
 * hook:
 * - refleja el estado de Tatana (WS `session_state` y `/agent/state`);
 * - activa la sesión al entrar al paso 3 y la cierra al salir (`ensureStarted` / `stop`);
 * - suma cada tramo cerrado a la bandeja (`onSegmentSaved` → guardado + registro + sello);
 * - replica el diario en el backend (`PUT …/recording-session`, debounce 1 s, de a uno);
 * - suma la cámara del procedimiento (grabacion-sesion-completa-f2 §9.2): lista, vista
 *   previa, empezar / quitar / reintentar y la cámara recordada en esta PC (D5).
 */

import { useCallback, useEffect, useRef, useState } from "react";
import {
  agent, AgentError, screenBlockOf,
  type AgentEvent, type CameraDevice, type CaseSessionDto, type SessionCameraRequest, type SessionCapacity,
  type SessionMarkAddedData, type SessionMarkDto, type SessionSegmentClosedData, type SessionStateDto,
  type SessionStopReason, type ScreenshotTakenData,
} from "@/lib/agent";
import { api, ApiError, type Case, type RecordingSessionMode } from "@/lib/api";
import { agentErrorMessage, RECORDING_SESSION_API_TEXT } from "@/lib/agent-messages";
import {
  canMarkSession, defaultEnabled, isSessionActive, isSessionRecording, lastStopAt, markLocationText, noSpaceText,
  sessionHasCamera, toBackendSnapshot,
} from "@/lib/recording-session";
import { loadSessionCamera, saveSessionCamera, type SessionCameraPref } from "@/lib/session-camera-pref";
import { isSessionExpiredError, onSessionRestored, sessionState as webSessionState } from "@/lib/session";

/**
 * Qué ofrece el Tatana conectado. `unknown` mientras se consulta. `android+camera`:
 * informa `recording_session_camera_v1` (grabacion-sesion-completa-f2 §9.2).
 */
export type SessionCapability = "unknown" | "none" | "android" | "android+camera";

/** La pantalla de Android se puede grabar (con o sin cámara). */
export const capabilityRecordsAndroid = (c: SessionCapability): boolean => c === "android" || c === "android+camera";

/** La vista previa no abrió la cámara (`POST /cameras/preview` con `ok: false`). */
export interface CameraPreviewProblem {
  detail: string | null;
  error: string | null;
}

/** Por qué no arrancó la sesión (se muestra en la tarjeta). */
export type SessionStartProblem =
  | { kind: "screen"; state: "off" | "locked" }
  | { kind: "disk"; text: string }
  | { kind: "foreign"; text: string }
  | { kind: "error"; text: string };

/** Aviso de un corte que ya pasó (Tatana se cerró): sobrevive a la reanudación automática. */
export interface SessionInterruption {
  at: string | null;
}

export interface UseRecordingSessionInput {
  /** Caso abierto en el flujo `agent` (si no, el hook queda inerte). */
  currentCase: Case | null;
  agentStorage: boolean;
  deviceSerial: string | null;
  platform: "android" | "ios";
  androidVersion: number;
  step: number;
  agentOnline: boolean;
  /** El celular del caso está desconectado o hay un bloqueo de la evidencia (otra PC, Tatana viejo). */
  captureBlocked: boolean;
  mode: RecordingSessionMode;
  segmentMinutes: number;
  /** Tramo cerrado y hasheado por Tatana: a la bandeja (`useFileManager.addStoredFile`). */
  onSegmentSaved: (seg: { filename: string; size: number; sha256: string }) => void;
  /** Respuesta del backend (settings o sync): caso actualizado. */
  onCaseUpdated: (cas: Case) => void;
  onError: (msg: string) => void;
}

const SYNC_DEBOUNCE_MS = 1000;
/** Busy de la sesión: la cámara (F2) tiene el suyo. */
export interface SessionBusy { start: boolean; stop: boolean; mark: boolean; settings: boolean; camera: boolean }
const SYNC_RETRY_MS = 15_000;
const RESUME_NOTICE_MS = 8000;

const sameId = (a: string | null | undefined, b: string | null | undefined) =>
  !!a && !!b && a.toLowerCase() === b.toLowerCase();

const devLog = (...args: unknown[]) => {
  if (process.env.NODE_ENV !== "production") console.info("[sesión]", ...args);
};

/** Estado "quieto" armado a partir del diario (sin sesión activa en Tatana). */
function stateFromJournal(j: CaseSessionDto): SessionStateDto {
  return {
    case_id: j.case_id,
    serial: j.serial,
    platform: j.platform,
    state: j.state,
    segment_minutes: j.segment_minutes,
    stop_reason: j.stop_reason ?? null,
    stopped_at: lastStopAt(j),
    segments_saved: j.segments.filter(s => s.state === "saved").length,
    warnings: [],
  };
}

/**
 * Cámara a pedir en el `start` al retomar (F2 §9.2, D1 = A): solo si el caso la usaba y
 * Tatana tiene la cámara. `id` = el recordado en esta PC si es la misma cámara; si no, el nombre.
 */
function resumeCamera(
  rs: Case["recording_session"] | null | undefined,
  capability: SessionCapability,
): SessionCameraRequest | null {
  if (rs?.with_camera !== true || capability !== "android+camera") return null;
  const remembered = loadSessionCamera();
  const withAudio = rs.camera?.with_audio ?? false;
  if (remembered && (!rs.camera || remembered.name === rs.camera.name)) {
    return { id: remembered.id, name: remembered.name, with_audio: withAudio };
  }
  return rs.camera ? { name: rs.camera.name, with_audio: withAudio } : null;
}

export function useRecordingSession(input: UseRecordingSessionInput) {
  const {
    currentCase, agentStorage, deviceSerial, platform, androidVersion, step, agentOnline, captureBlocked,
    mode, segmentMinutes,
  } = input;
  const caseId = agentStorage ? currentCase?.id ?? null : null;
  const rs = currentCase?.recording_session ?? null;

  const [capability, setCapability] = useState<SessionCapability>("unknown");
  /** `false` = el backend no tiene las rutas (404 de `settings`): se oculta todo sin aviso. */
  const [backendSupported, setBackendSupported] = useState<boolean | null>(null);
  const [state, setStateRaw] = useState<SessionStateDto | null>(null);
  /** Sesión activa de OTRO caso en este Tatana (para el aviso de `session_active`). */
  const [foreign, setForeign] = useState<SessionStateDto | null>(null);
  const [journal, setJournal] = useState<CaseSessionDto | null>(null);
  const [capacity, setCapacity] = useState<SessionCapacity | null>(null);
  const [pendingSync, setPendingSync] = useState(false);
  const [busy, setBusy] = useState<SessionBusy>({
    start: false, stop: false, mark: false, settings: false, camera: false,
  });
  // ── Cámara del procedimiento (F2 §9.2) ──
  const [cameras, setCameras] = useState<CameraDevice[] | null>(null);
  const [camerasError, setCamerasError] = useState<string | null>(null);
  const [selectedCamera, setSelectedCamera] = useState<SessionCameraPref | null>(null);
  const [withMic, setWithMic] = useState(false);
  /** Casilla "Sumar cámara del procedimiento" (además, queda tildada con la cámara grabando o caída). */
  const [cameraOptIn, setCameraOptInRaw] = useState(false);
  const [previewOn, setPreviewOn] = useState(false);
  const [previewProblem, setPreviewProblem] = useState<CameraPreviewProblem | null>(null);
  /** La cámara llegó a grabar en esta entrada al caso (texto "dejó de grabar" vs. "no se pudo empezar"). */
  const [cameraEverRecorded, setCameraEverRecorded] = useState(false);
  const [problem, setProblem] = useState<SessionStartProblem | null>(null);
  const [interruption, setInterruption] = useState<SessionInterruption | null>(null);
  const [resumeNotice, setResumeNotice] = useState<{ at: string | null; index: number | null } | null>(null);
  /** Último cambio a anunciar (`role="status"`); `id` fuerza el re-anuncio del mismo texto. */
  const [announcement, setAnnouncement] = useState<{ id: number; text: string } | null>(null);
  /** El estado vino de `/agent/state` (recarga, otra pestaña): "Grabando sesión desde HH:MM". */
  const [recovered, setRecovered] = useState(false);
  /** Última marca guardada desde esta pestaña (texto del aviso "Marca guardada…"). */
  const [lastMark, setLastMark] = useState<{ id: string; text: string } | null>(null);
  /** Caso cuyo estado ya se leyó de Tatana al menos una vez (la activación automática lo espera). */
  const [refreshedCase, setRefreshedCase] = useState<string | null>(null);

  // ── Refs para callbacks estables (WS, timers) ──
  const inputRef = useRef(input);
  useEffect(() => { inputRef.current = input; });
  const caseIdRef = useRef(caseId);
  useEffect(() => { caseIdRef.current = caseId; }, [caseId]);
  const stateRef = useRef(state);
  const capabilityRef = useRef(capability);
  useEffect(() => { capabilityRef.current = capability; }, [capability]);
  const selectedCameraRef = useRef(selectedCamera);
  useEffect(() => { selectedCameraRef.current = selectedCamera; }, [selectedCamera]);
  const previewOnRef = useRef(previewOn);
  useEffect(() => { previewOnRef.current = previewOn; }, [previewOn]);
  const cameraOptInRef = useRef(cameraOptIn);
  useEffect(() => { cameraOptInRef.current = cameraOptIn; }, [cameraOptIn]);
  const journalRef = useRef(journal);
  useEffect(() => { journalRef.current = journal; }, [journal]);
  const withMicRef = useRef(withMic);
  useEffect(() => { withMicRef.current = withMic; }, [withMic]);
  const announceSeq = useRef(0);

  const announce = useCallback((text: string) => {
    announceSeq.current += 1;
    setAnnouncement({ id: announceSeq.current, text });
  }, []);

  /**
   * Aplica un estado de Tatana del caso abierto y anuncia la transición:
   * iniciada / reanudada / detenida (§9.4). El aviso de reanudación dura 8 s.
   */
  const applyState = useCallback((next: SessionStateDto | null, opts: { silent?: boolean } = {}) => {
    const prev = stateRef.current;
    stateRef.current = next;
    setStateRaw(next);
    if (next?.camera_state === "recording") setCameraEverRecorded(true);
    if (!next || opts.silent) return;
    // Cámara (F2 §9.3): "Cámara grabando" / "La cámara dejó de grabar".
    if (next.camera_state === "recording" && prev?.camera_state !== "recording") announce("Cámara grabando");
    else if (next.camera_state === "failed" && prev?.camera_state === "recording") announce("La cámara dejó de grabar");
    const wasActive = isSessionActive(prev?.state);
    const wasRecording = isSessionRecording(prev?.state);
    const nowRecording = isSessionRecording(next.state);
    if (nowRecording && !wasRecording && prev?.state !== "starting") {
      const resumed = prev?.state === "waiting_device" || prev?.state === "stopped" || prev?.state === "interrupted";
      announce(resumed ? "Grabación de la sesión reanudada" : "Grabación de la sesión iniciada");
      if (prev?.state === "waiting_device") {
        setResumeNotice({ at: next.run_started_at ?? next.screen_segment_started_at ?? null, index: next.screen_segment_index ?? null });
      }
    } else if (nowRecording && prev?.state === "starting") {
      announce(next.resumed ? "Grabación de la sesión reanudada" : "Grabación de la sesión iniciada");
    } else if (wasActive && !isSessionActive(next.state)) {
      announce("Grabación de la sesión detenida");
    } else if (wasRecording && next.state === "waiting_device") {
      announce("Grabación de la sesión detenida");
    }
  }, [announce]);

  useEffect(() => {
    if (!resumeNotice) return;
    const t = setTimeout(() => setResumeNotice(null), RESUME_NOTICE_MS);
    return () => clearTimeout(t);
  }, [resumeNotice]);

  useEffect(() => {
    if (!lastMark) return;
    const t = setTimeout(() => setLastMark(null), 6000);
    return () => clearTimeout(t);
  }, [lastMark]);

  // Otro caso (o ninguno): se olvida todo lo del anterior.
  useEffect(() => {
    stateRef.current = null;
    setStateRaw(null);
    setJournal(null);
    setForeign(null);
    setPendingSync(false);
    setProblem(null);
    setInterruption(null);
    setResumeNotice(null);
    setRecovered(false);
    setLastMark(null);
    setCapacity(null);
    setBackendSupported(null);
    setRefreshedCase(null);
    setCameraOptInRaw(false);
    setPreviewProblem(null);
    setCameraEverRecorded(false);
    // La vista previa es de la PC, no del caso: se corta al cambiar de caso.
    if (previewOnRef.current) void agent.stopCameraPreview().catch(() => { /* sin Tatana: se corta sola */ });
    setPreviewOn(false);
  }, [caseId]);

  // Tatana reconectado (puede ser otra versión): se vuelve a consultar la capability.
  useEffect(() => { if (!agentOnline) setCapability("unknown"); }, [agentOnline]);

  /** Tramos `saved` del diario que todavía no están en la bandeja: se suman (dedup en `addStoredFile`). */
  const feedSavedSegments = useCallback((j: CaseSessionDto | null) => {
    j?.segments.forEach(s => {
      if (s.state === "saved" && s.sha256 && typeof s.size === "number") {
        inputRef.current.onSegmentSaved({ filename: s.filename, size: s.size, sha256: s.sha256 });
      }
    });
  }, []);

  /* ══ Sincronización con el backend (§9.2): debounce 1 s, una a la vez, encolada ══ */

  const syncTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const syncRunning = useRef<Promise<void> | null>(null);
  const syncAgain = useRef(false);
  const authPendingSync = useRef(false);

  const runSync = useCallback(async (): Promise<void> => {
    const id = caseIdRef.current;
    if (!id) return;
    let j: CaseSessionDto | null;
    try {
      j = await agent.getCaseSession(id);
    } catch (e) {
      devLog("No se pudo leer el diario de Tatana", e);
      return;
    }
    if (caseIdRef.current !== id) return;
    setJournal(j);
    feedSavedSegments(j);
    const cas = inputRef.current.currentCase;
    if (!j || !cas?.recording_session?.enabled) { setPendingSync(false); return; }
    try {
      if (webSessionState() === "expired") throw new Error("expired-local");
      const updated = await api.syncRecordingSession(id, toBackendSnapshot(j));
      if (caseIdRef.current !== id) return;
      inputRef.current.onCaseUpdated(updated);
      authPendingSync.current = false;
      setPendingSync(false);
    } catch (e) {
      if ((e instanceof Error && e.message === "expired-local") || isSessionExpiredError(e)) {
        // Sesión web vencida (#74): queda pendiente y se reintenta al volver a iniciar sesión.
        authPendingSync.current = true;
        setPendingSync(true);
        return;
      }
      if (e instanceof ApiError && e.status === 404) { setBackendSupported(false); setPendingSync(false); return; }
      if (e instanceof ApiError && e.code === "recording_session_not_enabled") { setPendingSync(false); return; }
      if (e instanceof ApiError && e.code === "recording_session_conflict") {
        setPendingSync(false);
        inputRef.current.onError(e.serverMessage ?? RECORDING_SESSION_API_TEXT.recording_session_conflict);
        return;
      }
      // Backend caído, `recording_session_busy`…: se reintenta solo.
      setPendingSync(true);
      if (syncTimer.current) clearTimeout(syncTimer.current);
      syncTimer.current = setTimeout(() => { void flushSync(); }, SYNC_RETRY_MS);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [feedSavedSegments]);

  /** Corre la sincronización ya (y una más si llegó otro pedido mientras corría). */
  const flushSync = useCallback(async (): Promise<void> => {
    if (syncTimer.current) { clearTimeout(syncTimer.current); syncTimer.current = null; }
    if (syncRunning.current) { syncAgain.current = true; await syncRunning.current; return; }
    const loop = (async () => {
      do {
        syncAgain.current = false;
        await runSync();
      } while (syncAgain.current);
    })();
    syncRunning.current = loop.finally(() => { syncRunning.current = null; });
    await syncRunning.current;
  }, [runSync]);

  const scheduleSync = useCallback(() => {
    if (!caseIdRef.current) return;
    setPendingSync(true);
    if (syncTimer.current) clearTimeout(syncTimer.current);
    syncTimer.current = setTimeout(() => { void flushSync(); }, SYNC_DEBOUNCE_MS);
  }, [flushSync]);

  useEffect(() => () => { if (syncTimer.current) clearTimeout(syncTimer.current); }, []);

  // Sesión web recuperada: lo que quedó pendiente se sincroniza.
  useEffect(() => onSessionRestored(() => {
    if (authPendingSync.current) void flushSync();
  }), [flushSync]);

  /* ══ Lectura del estado (recarga, reconexión, vuelta al frente) ══ */

  const refreshBusy = useRef(false);
  const refreshQueued = useRef(false);

  const refresh = useCallback(async (): Promise<void> => {
    if (refreshBusy.current) { refreshQueued.current = true; return; }
    refreshBusy.current = true;
    try {
      do {
        refreshQueued.current = false;
        const id = caseIdRef.current;
        if (!id) return;
        const ok = await agent.supportsRecordingSession();
        if (caseIdRef.current !== id) continue;
        const withCamera = ok && await agent.supportsSessionCamera();
        if (caseIdRef.current !== id) continue;
        setCapability(!ok ? "none" : withCamera ? "android+camera" : "android");
        if (!ok) return;
        const [agentState, j] = await Promise.all([
          agent.agentRecordingSession(),
          agent.getCaseSession(id).catch(() => undefined),
        ]);
        if (caseIdRef.current !== id) continue;
        const active = agentState?.recording_session ?? null;
        const firstRead = stateRef.current === null;
        if (active && sameId(active.case_id, id)) {
          if (!isSessionActive(stateRef.current?.state)) setRecovered(true);
          applyState(active, { silent: firstRead });
          setForeign(null);
        } else {
          setForeign(active && isSessionActive(active.state) ? active : null);
          if (j) {
            // Sin sesión activa de este caso: el estado quieto del diario (detenida, interrumpida…).
            if (!isSessionActive(stateRef.current?.state) || agentState) applyState(stateFromJournal(j), { silent: firstRead });
            if (j.state === "interrupted" || j.stop_reason === "tatana_cerrado") {
              setInterruption(cur => cur ?? { at: lastStopAt(j) });
            }
          } else if (agentState && isSessionActive(stateRef.current?.state)) {
            applyState(null);
          }
        }
        if (j !== undefined) {
          setJournal(j);
          feedSavedSegments(j);
          if (j && inputRef.current.currentCase?.recording_session?.enabled) scheduleSync();
        }
        setRefreshedCase(id);
      } while (refreshQueued.current);
    } finally {
      refreshBusy.current = false;
    }
  }, [applyState, feedSavedSegments, scheduleSync]);

  /** Espacio libre (antes de empezar y después de detener). Sin medición o con error, nada. */
  const refreshCapacity = useCallback(async () => {
    try {
      // Con la cámara pedida (casilla, grabando, o el caso la usaba): la estimación de las dos pistas.
      const st = stateRef.current;
      const camera = capabilityRef.current === "android+camera" && (cameraOptInRef.current
        || st?.camera_state === "recording" || inputRef.current.currentCase?.recording_session?.with_camera === true);
      const cap = await agent.sessionCapacity(camera, inputRef.current.segmentMinutes);
      setCapacity(cap);
    } catch { /* Tatana viejo o sin respuesta: sin estimación */ }
  }, []);

  // Paso 3 del caso (al entrar, al reconectar Tatana): estado + espacio. En el paso 5, el
  // estado decide si se puede generar (§9.6).
  const inStep3 = step === 3 && !!caseId;
  const watchStep = (step === 3 || step === 5) && !!caseId;
  useEffect(() => {
    if (!watchStep || !agentOnline) return;
    void refresh();
  }, [watchStep, step, caseId, agentOnline, refresh]);
  useEffect(() => {
    if (!inStep3 || !agentOnline || !capabilityRecordsAndroid(capability)) return;
    if (isSessionActive(state?.state)) return;
    void refreshCapacity();
  }, [inStep3, agentOnline, capability, state?.state, cameraOptIn, refreshCapacity]);

  // Pestaña de vuelta al frente: Tatana pudo cambiar mientras tanto.
  useEffect(() => {
    if (!caseId) return;
    const onVisible = () => { if (document.visibilityState === "visible" && agentOnline) void refresh(); };
    document.addEventListener("visibilitychange", onVisible);
    return () => document.removeEventListener("visibilitychange", onVisible);
  }, [caseId, agentOnline, refresh]);

  /* ══ Eventos del WebSocket (filtrados por el caso abierto) ══ */

  const handleAgentEvent = useCallback((event: AgentEvent) => {
    const id = caseIdRef.current;
    if (!id) return;
    switch (event.type) {
      case "session_state": {
        const d = event.data as unknown as SessionStateDto;
        if (!d || typeof d.state !== "string") return;
        if (sameId(d.case_id, id)) {
          const prevState = stateRef.current?.state ?? null;
          applyState(d);
          setForeign(null);
          if (isSessionRecording(d.state)) setProblem(null);
          // Transiciones (cortes y reanudaciones dejan huecos): la copia del caso se actualiza.
          // Un cambio de avisos con la sesión grabando no toca el diario.
          if (!(isSessionRecording(d.state) && isSessionRecording(prevState))) scheduleSync();
        } else {
          setForeign(isSessionActive(d.state) ? d : null);
        }
        return;
      }
      case "session_segment_closed": {
        const d = event.data as unknown as SessionSegmentClosedData;
        if (!d?.segment || !sameId(d.case_id, id)) return;
        const seg = d.segment;
        if (seg.state === "saved" && seg.sha256 && typeof seg.size === "number") {
          inputRef.current.onSegmentSaved({ filename: seg.filename, size: seg.size, sha256: seg.sha256 });
          announce(`Tramo ${seg.index} guardado`);
        }
        scheduleSync();
        return;
      }
      case "session_mark_added": {
        const d = event.data as unknown as SessionMarkAddedData;
        if (d && sameId(d.case_id, id)) scheduleSync();
        return;
      }
      case "screenshot_taken": {
        const d = event.data as unknown as ScreenshotTakenData;
        if (d?.session_mark && sameId(d.case_id, id)) scheduleSync();
        return;
      }
      default:
        return;
    }
  }, [applyState, announce, scheduleSync]);

  /* ══ Acciones ══ */

  const startInFlight = useRef(false);

  /**
   * Empieza (o reanuda) la grabación del caso abierto. Idempotente: con la sesión
   * ya activa para este caso, Tatana devuelve el estado actual. `true` si quedó grabando.
   */
  const ensureStarted = useCallback(async (opts: { skipScreenCheck?: boolean } = {}): Promise<boolean> => {
    const inp = inputRef.current;
    const id = caseIdRef.current;
    if (!id || !inp.deviceSerial || inp.platform !== "android" || startInFlight.current) return false;
    startInFlight.current = true;
    setBusy(b => ({ ...b, start: true }));
    setProblem(null);
    try {
      const camera = resumeCamera(inp.currentCase?.recording_session, capabilityRef.current);
      const res = await agent.startSession(id, {
        serial: inp.deviceSerial,
        platform: "android",
        android_version: inp.androidVersion,
        segment_minutes: inp.segmentMinutes,
        ...(opts.skipScreenCheck ? { skip_screen_check: true } : {}),
        ...(camera ? { camera } : {}),
      });
      if (caseIdRef.current !== id) return false;
      setRecovered(false);
      applyState(res);
      setForeign(null);
      scheduleSync();
      return isSessionActive(res.state);
    } catch (e) {
      if (caseIdRef.current !== id) return false;
      const screen = screenBlockOf(e);
      if (screen) setProblem({ kind: "screen", state: screen });
      else if (e instanceof AgentError && e.code === "insufficient_storage") {
        setProblem({ kind: "disk", text: noSpaceText(e.body?.available_bytes) });
      } else if (e instanceof AgentError && e.code === "session_active") {
        setProblem({ kind: "foreign", text: agentErrorMessage(e, { action: "session" }) });
      } else {
        setProblem({ kind: "error", text: agentErrorMessage(e, { action: "session" }) });
      }
      return false;
    } finally {
      startInFlight.current = false;
      setBusy(b => ({ ...b, start: false }));
    }
  }, [applyState, scheduleSync]);

  const stopInFlight = useRef<Promise<boolean> | null>(null);

  /**
   * Cierra la grabación del caso abierto (§7.2.3) y espera a que los tramos
   * queden en la bandeja y el diario sincronizado. `true` si quedó cerrada (o
   * no había nada que cerrar).
   */
  const stop = useCallback((reason: SessionStopReason): Promise<boolean> => {
    if (stopInFlight.current) return stopInFlight.current;
    const id = caseIdRef.current;
    if (!id || !isSessionActive(stateRef.current?.state)) return Promise.resolve(true);
    setBusy(b => ({ ...b, stop: true }));
    const p = (async () => {
      try {
        const res = await agent.stopSession(id, reason);
        if (caseIdRef.current === id) applyState(res);
        // Los tramos guardados llegan por WS; por las dudas se releen del diario y se sincroniza ya.
        await flushSync();
        if (caseIdRef.current === id) void refreshCapacity();
        return true;
      } catch (e) {
        inputRef.current.onError(agentErrorMessage(e, { action: "session" }));
        return false;
      } finally {
        setBusy(b => ({ ...b, stop: false }));
        stopInFlight.current = null;
      }
    })();
    stopInFlight.current = p;
    return p;
  }, [applyState, flushSync, refreshCapacity]);

  /** Cierre sin esperar (salir del asistente): el caso puede dejar de estar abierto enseguida. */
  const stopDetached = useCallback((reason: SessionStopReason) => {
    const id = caseIdRef.current;
    if (!id || !isSessionActive(stateRef.current?.state)) return;
    void agent.stopSession(id, reason).catch(() => { /* se recupera al volver al caso */ });
  }, []);

  /** "Marcar momento" (D5). Devuelve la marca o `null` (el error sale por `onError`). */
  const mark = useCallback(async (text: string): Promise<SessionMarkDto | null> => {
    const id = caseIdRef.current;
    if (!id) return null;
    setBusy(b => ({ ...b, mark: true }));
    try {
      const m = await agent.addSessionMark(id, text.trim() || null);
      // Con cámara en la sesión, la ubicación en los dos videos (F2 §9.3).
      const hasCamera = typeof m.camera_segment_index === "number" || stateRef.current?.camera_state === "recording"
        || sessionHasCamera(journalRef.current) || sessionHasCamera(inputRef.current.currentCase?.recording_session);
      setLastMark({ id: m.id, text: `Marca guardada · ${markLocationText(m, hasCamera)}` });
      scheduleSync();
      return m;
    } catch (e) {
      inputRef.current.onError(agentErrorMessage(e, { action: "session" }));
      return null;
    } finally {
      setBusy(b => ({ ...b, mark: false }));
    }
  }, [scheduleSync]);

  /** Switch "Grabar toda la recolección". Apagar con la sesión activa la detiene antes. */
  const setEnabled = useCallback(async (enabled: boolean): Promise<void> => {
    const id = caseIdRef.current;
    if (!id) return;
    setBusy(b => ({ ...b, settings: true }));
    try {
      if (!enabled && isSessionActive(stateRef.current?.state)) {
        const ok = await stop("detenido_por_perito");
        if (!ok) return;
      }
      const updated = await api.saveRecordingSessionSettings(id, { enabled, with_camera: false });
      if (caseIdRef.current !== id) return;
      inputRef.current.onCaseUpdated(updated);
      if (enabled) await ensureStarted();
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) { setBackendSupported(false); return; }
      const msg = e instanceof ApiError
        ? (e.serverMessage ?? (e.code ? RECORDING_SESSION_API_TEXT[e.code] : null)
          ?? "No se pudo guardar la opción de la grabación de la sesión. Reintentá.")
        : "No se pudo guardar la opción de la grabación de la sesión. Reintentá.";
      inputRef.current.onError(msg);
    } finally {
      setBusy(b => ({ ...b, settings: false }));
    }
  }, [ensureStarted, stop]);

  /* ══ Cámara del procedimiento (F2 §9.2) ══ */


  const settingsError = (e: unknown) => {
    const msg = e instanceof ApiError
      ? (e.serverMessage ?? (e.code ? RECORDING_SESSION_API_TEXT[e.code] : null)
        ?? "No se pudo guardar la opción de la grabación de la sesión. Reintentá.")
      : "No se pudo guardar la opción de la grabación de la sesión. Reintentá.";
    inputRef.current.onError(msg);
  };

  /** `with_camera` del caso (D-T11): solo si cambia. */
  const saveWithCamera = useCallback(async (id: string, withCamera: boolean) => {
    if ((inputRef.current.currentCase?.recording_session?.with_camera === true) === withCamera) return;
    try {
      const updated = await api.saveRecordingSessionSettings(id, { enabled: true, with_camera: withCamera });
      if (caseIdRef.current === id) inputRef.current.onCaseUpdated(updated);
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) { setBackendSupported(false); return; }
      settingsError(e);
    }
  }, []);

  const startPreview = useCallback(async (cam: SessionCameraPref) => {
    if (stateRef.current?.camera_state === "recording") return; // la previa sale de la grabación
    setPreviewProblem(null);
    try {
      const r = await agent.startCameraPreview(cam.id, cam.name);
      if (selectedCameraRef.current?.id !== cam.id || !cameraOptInRef.current) return;
      if (r.ok) { setPreviewOn(true); return; }
      setPreviewOn(false);
      setPreviewProblem({ detail: r.detail ?? null, error: r.error ?? null });
    } catch (e) {
      setPreviewOn(false);
      if (e instanceof AgentError && e.code === "camera_busy") return; // la cámara de la sesión ya graba
      setPreviewProblem({ detail: null, error: agentErrorMessage(e, { action: "session" }) });
    }
  }, []);

  const stopPreview = useCallback(() => {
    if (!previewOnRef.current) return;
    setPreviewOn(false);
    void agent.stopCameraPreview().catch(() => { /* Tatana la corta sola a los 120 s */ });
  }, []);

  /** Lista de cámaras con la preselección: la recordada (D5) → por nombre → la primera. */
  const loadCameras = useCallback(async (refreshList: boolean): Promise<SessionCameraPref | null> => {
    try {
      const r = await agent.listCameras(refreshList);
      setCameras(r.cameras);
      setCamerasError(r.error ?? null);
      const cur = selectedCameraRef.current;
      const inList = (c: SessionCameraPref | null) => !!c && r.cameras.some(x => x.id === c.id);
      if (inList(cur)) return cur;
      const live = stateRef.current?.camera;
      const remembered = loadSessionCamera();
      const pick = (live && r.cameras.find(x => x.id === live.id || x.name === live.name))
        ?? (remembered && (r.cameras.find(x => x.id === remembered.id) ?? r.cameras.find(x => x.name === remembered.name)))
        ?? r.cameras[0] ?? null;
      const next = pick ? { id: pick.id, name: pick.name } : null;
      selectedCameraRef.current = next;
      setSelectedCamera(next);
      return next;
    } catch (e) {
      setCameras([]);
      setCamerasError(agentErrorMessage(e, { action: "session" }));
      return null;
    }
  }, []);

  /** Casilla "Sumar cámara del procedimiento". Apagarla con la cámara grabando la saca (la confirmación es de la UI). */
  const setCameraOptIn = useCallback(async (on: boolean): Promise<void> => {
    const id = caseIdRef.current;
    if (!id) return;
    if (on) {
      cameraOptInRef.current = true;
      setCameraOptInRaw(true);
      const cam = await loadCameras(false);
      if (cam && cameraOptInRef.current && stateRef.current?.camera_state !== "recording") await startPreview(cam);
      return;
    }
    cameraOptInRef.current = false;
    setCameraOptInRaw(false);
    setPreviewProblem(null);
    stopPreview();
    const camState = stateRef.current?.camera_state;
    if (camState !== "recording" && camState !== "failed") return;
    setBusy(b => ({ ...b, camera: true }));
    try {
      const res = await agent.stopSessionCamera(id);
      if (caseIdRef.current !== id) return;
      applyState(res);
      scheduleSync();
      await saveWithCamera(id, false);
    } catch (e) {
      inputRef.current.onError(agentErrorMessage(e, { action: "session" }));
    } finally {
      setBusy(b => ({ ...b, camera: false }));
    }
  }, [applyState, loadCameras, saveWithCamera, scheduleSync, startPreview, stopPreview]);

  /** Elegir otra cámara: cambia la vista previa. No se puede con la cámara grabando. */
  const selectCamera = useCallback((cam: SessionCameraPref) => {
    if (stateRef.current?.camera_state === "recording") return;
    selectedCameraRef.current = cam;
    setSelectedCamera(cam);
    if (cameraOptInRef.current) void startPreview(cam);
  }, [startPreview]);

  /** "Empezar a grabar la cámara". */
  const startCamera = useCallback(async (): Promise<void> => {
    const id = caseIdRef.current;
    const cam = selectedCameraRef.current;
    if (!id || !cam) return;
    setBusy(b => ({ ...b, camera: true }));
    try {
      // Windows (dshow) no comparte la cámara: primero se corta la previa.
      if (previewOnRef.current) {
        setPreviewOn(false);
        await agent.stopCameraPreview().catch(() => { /* Tatana igual la corta al abrir la de la sesión */ });
      }
      const res = await agent.startSessionCamera(id, { id: cam.id, name: cam.name, with_audio: withMicRef.current });
      if (caseIdRef.current !== id) return;
      applyState(res);
      scheduleSync();
      if (res.camera_state === "recording") {
        saveSessionCamera(cam);
        await saveWithCamera(id, true);
      }
    } catch (e) {
      inputRef.current.onError(agentErrorMessage(e, { action: "session" }));
    } finally {
      setBusy(b => ({ ...b, camera: false }));
    }
  }, [applyState, saveWithCamera, scheduleSync]);

  /** "Reintentar cámara" del aviso `camera_failed`. */
  const retryCamera = useCallback(async (): Promise<void> => {
    const id = caseIdRef.current;
    if (!id) return;
    setBusy(b => ({ ...b, camera: true }));
    try {
      const res = await agent.retrySessionCamera(id);
      if (caseIdRef.current !== id) return;
      applyState(res);
      scheduleSync();
      if (res.camera_state === "recording") await saveWithCamera(id, true);
    } catch (e) {
      inputRef.current.onError(agentErrorMessage(e, { action: "session" }));
    } finally {
      setBusy(b => ({ ...b, camera: false }));
    }
  }, [applyState, saveWithCamera, scheduleSync]);

  /** "Actualizar vista": vuelve a pedir la previa (Tatana la corta sola a los 120 s sin pedidos). */
  const refreshPreview = useCallback(async () => {
    const cam = selectedCameraRef.current;
    if (cam && cameraOptInRef.current && stateRef.current?.camera_state !== "recording") await startPreview(cam);
  }, [startPreview]);

  // Cámara grabando o caída sin la casilla tildada en esta pestaña (recarga, retomar): la lista hace falta igual.
  const cameraChecked = cameraOptIn || state?.camera_state === "recording" || state?.camera_state === "failed";
  useEffect(() => {
    if (!inStep3 || !agentOnline || capability !== "android+camera" || !cameraChecked || cameras !== null) return;
    void loadCameras(false);
  }, [inStep3, agentOnline, capability, cameraChecked, cameras, loadCameras]);

  // Fuera del paso 3 (o sin Tatana): la vista previa se corta sin esperar.
  useEffect(() => {
    if (inStep3 && agentOnline) return;
    stopPreview();
    if (!inStep3) { cameraOptInRef.current = false; setCameraOptInRaw(false); setPreviewProblem(null); }
  }, [inStep3, agentOnline, stopPreview]);
  useEffect(() => () => {
    if (previewOnRef.current) void agent.stopCameraPreview().catch(() => { /* se corta sola */ });
  }, []);

  /* ══ Activación al entrar al paso 3 (§9.3) ══ */

  /** Una sola vez por entrada al paso (y por caso): un "Detener" no se deshace solo. */
  const entryKey = useRef<string | null>(null);
  const settingsAttempted = useRef<string | null>(null);
  useEffect(() => { if (!inStep3) entryKey.current = null; }, [inStep3]);

  const ready = inStep3 && agentOnline && capabilityRecordsAndroid(capability) && !!deviceSerial && platform === "android"
    && !captureBlocked && backendSupported !== false && mode !== "disabled";
  const enabledInCase = rs?.enabled === true;
  const hasSettings = !!rs;
  const currentState = state?.state ?? null;

  useEffect(() => {
    if (!ready || !caseId) return;
    // 1. Primer paso 3 del caso: se guarda el switch con el default de la instalación.
    //    El `Case` que hay en memoria puede ser el del listado (sin `recording_session`,
    //    QA H-3): antes de escribir nada se carga el caso completo y solo si de verdad no
    //    tiene ajustes se guarda el default. Si ya los tiene, se usan (no se pisa `enabled`).
    if (!hasSettings) {
      if (settingsAttempted.current === caseId) return;
      settingsAttempted.current = caseId;
      void (async () => {
        let fresh: Case;
        try {
          fresh = (await api.getCase(caseId)).cas;
        } catch (e) {
          devLog("No se pudo cargar el caso completo; no se guarda el switch por defecto", e);
          if (settingsAttempted.current === caseId) settingsAttempted.current = null;
          return;
        }
        if (caseIdRef.current !== caseId) return;
        if (fresh.recording_session) { inputRef.current.onCaseUpdated(fresh); return; }
        try {
          const updated = await api.saveRecordingSessionSettings(caseId, { enabled: defaultEnabled(mode), with_camera: false });
          if (caseIdRef.current === caseId) inputRef.current.onCaseUpdated(updated);
        } catch (e) {
          if (e instanceof ApiError && e.status === 404) {
            devLog("El backend no tiene las rutas de la grabación de la sesión; se oculta la función.");
            setBackendSupported(false);
          } else {
            devLog("No se pudo guardar el switch por defecto", e);
          }
        }
      })();
      return;
    }
    setBackendSupported(true);
    // 2. Con la sesión activada: arranca sola (una vez por entrada). Con `waiting_device`, Tatana la reanuda sola.
    if (!enabledInCase) return;
    // Sin leer todavía el estado de Tatana: se espera al `refresh` del paso (ve un corte previo).
    if (refreshedCase !== caseId) return;
    if (currentState === "waiting_device" || currentState === "starting" || currentState === "stopping") return;
    if (entryKey.current === caseId) return;
    entryKey.current = caseId;
    if (isSessionActive(currentState)) return;
    void ensureStarted();
  }, [ready, caseId, hasSettings, enabledInCase, currentState, mode, refreshedCase, ensureStarted]);

  const ownState = sameId(state?.case_id, caseId);
  const cameraRecording = ownState && state?.camera_state === "recording";
  // "No se pudo empezar" si la cámara nunca grabó en esta entrada ni dejó tramos en esta sesión.
  const runStart = state?.session_started_at ?? state?.run_started_at ?? null;
  const cameraNeverStarted = !cameraEverRecorded && !(journal?.segments ?? []).some(
    s => s.track === "camara" && (!runStart || s.started_at >= runStart),
  );

  return {
    capability, backendSupported, state, foreign, journal, capacity, pendingSync, busy, problem,
    interruption, resumeNotice, announcement, recovered, lastMark,
    active: isSessionActive(state?.state) && ownState,
    recording: isSessionRecording(state?.state) && ownState,
    /** "Marcar momento" disponible (F2 [DU1=A]: también con el cable desconectado si la cámara graba). */
    canMark: canMarkSession(state) && ownState,
    camera: {
      available: capability === "android+camera",
      checked: cameraChecked,
      cameras, camerasError, selected: selectedCamera, withMic, previewOn, previewProblem,
      recording: cameraRecording,
      /** Cámara en uso según Tatana (se muestra bloqueada mientras graba). */
      liveCamera: ownState && state?.camera ? { id: state.camera.id, name: state.camera.name } : null,
      liveWithAudio: ownState && state?.camera?.with_audio === true,
      busy: busy.camera,
      neverStarted: cameraNeverStarted,
      /** D4 = B (+ D-T10): la cámara de la PC está ocupada por Tatana. */
      inUse: cameraRecording || previewOn,
      setOptIn: setCameraOptIn,
      select: selectCamera,
      setWithMic,
      start: startCamera,
      retry: retryCamera,
      refreshList: async () => { await loadCameras(true); },
      refreshPreview,
    },
    refresh, handleAgentEvent, ensureStarted, stop, stopDetached, mark, setEnabled, flushSync,
    dismissProblem: () => setProblem(null),
    dismissInterruption: () => setInterruption(null),
    dismissResumeNotice: () => setResumeNotice(null),
  };
}

export type RecordingSessionApi = ReturnType<typeof useRecordingSession>;

/** Controles de la cámara del procedimiento que expone el hook (F2 §9.2). */
export type SessionCameraApi = RecordingSessionApi["camera"];
