"use client";

/**
 * Grabación de la sesión completa del paso 3 (grabacion-sesion-completa, SDD §9.2).
 *
 * Tatana es la fuente de verdad mientras se graba (diario en disco por caso). Este
 * hook:
 * - refleja el estado de Tatana (WS `session_state` y `/agent/state`);
 * - activa la sesión al entrar al paso 3 y la cierra al salir (`ensureStarted` / `stop`);
 * - suma cada tramo cerrado a la bandeja (`onSegmentSaved` → guardado + registro + sello);
 * - replica el diario en el backend (`PUT …/recording-session`, debounce 1 s, de a uno).
 */

import { useCallback, useEffect, useRef, useState } from "react";
import {
  agent, AgentError, screenBlockOf,
  type AgentEvent, type CaseSessionDto, type SessionCapacity, type SessionMarkAddedData, type SessionMarkDto,
  type SessionSegmentClosedData, type SessionStateDto, type SessionStopReason, type ScreenshotTakenData,
} from "@/lib/agent";
import { api, ApiError, type Case, type RecordingSessionMode } from "@/lib/api";
import { agentErrorMessage, RECORDING_SESSION_API_TEXT } from "@/lib/agent-messages";
import {
  defaultEnabled, isSessionActive, isSessionRecording, lastStopAt, noSpaceText, offsetText, toBackendSnapshot,
} from "@/lib/recording-session";
import { isSessionExpiredError, onSessionRestored, sessionState as webSessionState } from "@/lib/session";

/** Qué ofrece el Tatana conectado. `unknown` mientras se consulta. F2 suma cámara e iPhone. */
export type SessionCapability = "unknown" | "none" | "android";

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
  const [busy, setBusy] = useState<{ start: boolean; stop: boolean; mark: boolean; settings: boolean }>({
    start: false, stop: false, mark: false, settings: false,
  });
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
    if (!next || opts.silent) return;
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
        setCapability(ok ? "android" : "none");
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
      const cap = await agent.sessionCapacity(false, inputRef.current.segmentMinutes);
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
    if (!inStep3 || !agentOnline || capability !== "android") return;
    if (isSessionActive(state?.state)) return;
    void refreshCapacity();
  }, [inStep3, agentOnline, capability, state?.state, refreshCapacity]);

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
      const res = await agent.startSession(id, {
        serial: inp.deviceSerial,
        platform: "android",
        android_version: inp.androidVersion,
        segment_minutes: inp.segmentMinutes,
        ...(opts.skipScreenCheck ? { skip_screen_check: true } : {}),
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
      setLastMark({ id: m.id, text: `Marca guardada · tramo ${m.segment_index}, minuto ${offsetText(m.offset_seconds)}` });
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

  /* ══ Activación al entrar al paso 3 (§9.3) ══ */

  /** Una sola vez por entrada al paso (y por caso): un "Detener" no se deshace solo. */
  const entryKey = useRef<string | null>(null);
  const settingsAttempted = useRef<string | null>(null);
  useEffect(() => { if (!inStep3) entryKey.current = null; }, [inStep3]);

  const ready = inStep3 && agentOnline && capability === "android" && !!deviceSerial && platform === "android"
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

  return {
    capability, backendSupported, state, foreign, journal, capacity, pendingSync, busy, problem,
    interruption, resumeNotice, announcement, recovered, lastMark,
    active: isSessionActive(state?.state) && sameId(state?.case_id, caseId),
    recording: isSessionRecording(state?.state) && sameId(state?.case_id, caseId),
    refresh, handleAgentEvent, ensureStarted, stop, stopDetached, mark, setEnabled, flushSync,
    dismissProblem: () => setProblem(null),
    dismissInterruption: () => setInterruption(null),
    dismissResumeNotice: () => setResumeNotice(null),
  };
}

export type RecordingSessionApi = ReturnType<typeof useRecordingSession>;
