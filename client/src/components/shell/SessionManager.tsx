"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { usePathname } from "next/navigation";
import { api, ApiError } from "@/lib/api";
import {
  AUTH_EVENT, SESSION_ACTIVITY_KEY, SESSION_EVENT, SESSION_EXPIRED_KEY, SESSION_META_KEY, TOKEN_KEY,
  dispatchAuthEvent, dispatchSessionEvent, endSession, expireSession, getStoredToken, hasActiveLongOps,
  inPlaceState, isInPlaceActive, lastActivity, markActivity, markSessionAtCap, markSessionRestored,
  readSessionMeta, sessionState, sessionUserDni, setSessionUser, writeSession,
  type AuthEventDetail, type SessionEventDetail,
} from "@/lib/session";
import { writeSessionNotice } from "@/lib/session-notice";
import { SESSION_TICK_MS, decide, windowsFor } from "@/lib/session-timing";
import { ReloginDialog, type ReloginMode } from "./ReloginDialog";

/* ── Estado visible de la sesión (lo leen los banners de la navbar) ── */

export interface SessionUiValue {
  /** Vencimiento por inactividad a mostrar (epoch ms), o `null`. */
  expiryAt: number | null;
  /** Tope a mostrar (epoch ms), o `null`. */
  capAt: number | null;
  /** "Seguir conectado" en curso. */
  renewing: boolean;
  /** "Seguir conectado" falló por red o servidor. */
  renewFailed: boolean;
  /** Toast visual "Sesión renovada" (3 s). */
  renewedFlash: boolean;
  /** Texto de la región `sr-only` (4 s). */
  announce: string;
  keepAlive: () => void;
  dismissCap: () => void;
  openEarly: () => void;
}

const SessionUiContext = createContext<SessionUiValue | null>(null);

/** `null` fuera de `SessionManager` (p. ej. el showcase sin providers). */
export function useSessionUi(): SessionUiValue | null {
  return useContext(SessionUiContext);
}

type RenewTrigger = "auto" | "button";
type RenewResult = "ok" | "skipped" | "cap" | "auth" | "failed";

interface DialogState {
  mode: ReloginMode;
  dni: string;
  otherUser: boolean;
}

const LOCK_NAME = "factum-session-refresh";
/** Una interacción dispara `evaluate()` como mucho cada tanto (el tick cubre el resto). */
const INTERACTION_EVAL_MS = 5_000;
const FLASH_MS = 3_000;
const ANNOUNCE_MS = 4_000;

const sleep = (ms: number) => new Promise<void>(r => setTimeout(r, ms));

/**
 * Gestor único de la sesión (sesion-larga §8.2, §8.5, §8.6, §8.8). Montado una
 * vez en `AppProviders`, junto a `SessionWatcher`:
 * - reloj de 15 s + interacción + `visibilitychange` + `storage` → `evaluate()`;
 * - renovación silenciosa con lock entre pestañas (`navigator.locks`, o jitter);
 * - avisos (los dibuja `SessionBanners` dentro de la navbar, vía contexto);
 * - re-login en el lugar (`ReloginDialog`).
 */
export function SessionManager({ children }: { children: React.ReactNode }) {
  const [expiryAt, setExpiryAt] = useState<number | null>(null);
  const [capAt, setCapAt] = useState<number | null>(null);
  const [renewing, setRenewing] = useState(false);
  const [renewFailed, setRenewFailed] = useState(false);
  const [renewedFlash, setRenewedFlash] = useState(false);
  const [announce, setAnnounce] = useState("");
  const [maxAge, setMaxAge] = useState<number | null>(null);
  const [dialog, setDialog] = useState<DialogState | null>(null);

  /** No volver a pedir refresh antes de esto (429 con `Retry-After`). */
  const retryAtRef = useRef(0);
  /** `max` de la meta cuyo aviso del tope se cerró con "Entendido". */
  const capDismissedRef = useRef<number | null>(null);
  const timersRef = useRef<Set<ReturnType<typeof setTimeout>>>(new Set());
  const evaluateRef = useRef<() => void>(() => {});
  // Último contenido del diálogo: mientras se cierra (transición) no cambia de texto.
  const [shownDialog, setShownDialog] = useState<DialogState | null>(null);
  if (dialog && dialog !== shownDialog) setShownDialog(dialog);

  // Al cambiar de ruta (p. ej. cambio de contraseña obligatorio tras el re-login) no queda abierto.
  const pathname = usePathname();
  const [lastPath, setLastPath] = useState(pathname);
  if (pathname !== lastPath) {
    setLastPath(pathname);
    if (dialog) setDialog(null);
  }

  const later = useCallback((fn: () => void, ms: number) => {
    const t = setTimeout(() => { timersRef.current.delete(t); fn(); }, ms);
    timersRef.current.add(t);
  }, []);

  const showRenewed = useCallback(() => {
    setRenewedFlash(true);
    setAnnounce("Sesión renovada");
    later(() => setRenewedFlash(false), FLASH_MS);
    later(() => setAnnounce(""), ANNOUNCE_MS);
  }, [later]);

  /** Una sola renovación en vuelo por pestaña; "Seguir conectado" se suma a la que ya corre. */
  const inFlightRef = useRef<Promise<RenewResult> | null>(null);

  const renew = useCallback(async (trigger: RenewTrigger) => {
    const decided = readSessionMeta();
    if (!decided || !getStoredToken()) return;
    const isButton = trigger === "button";
    if (isButton) { setRenewing(true); setRenewFailed(false); }

    const run = async (): Promise<RenewResult> => {
      // Doble chequeo dentro del lock: otra pestaña pudo renovar mientras se esperaba.
      const meta = readSessionMeta();
      const tokenBefore = getStoredToken();
      if (!meta || !tokenBefore || sessionState() === "expired" || meta.exp !== decided.exp) return "skipped";
      if (!isButton && meta.exp - Date.now() >= windowsFor(meta).renewWindow) return "skipped";
      try {
        const resp = await api.refreshSession();
        // Si en el medio venció o cambió el token (login en otra pestaña), no se pisa.
        if (sessionState() === "expired" || getStoredToken() !== tokenBefore) return "skipped";
        writeSession(resp, "refresh");
        dispatchSessionEvent({ type: "renewed" });
        return "ok";
      } catch (e) {
        if (e instanceof ApiError) {
          if (e.status === 401 && e.code === "session_max_age") { markSessionAtCap(); return "cap"; }
          if (e.status === 429) {
            retryAtRef.current = Date.now() + (e.retryAfterSeconds ?? 60) * 1000;
            return "failed";
          }
          // El interceptor ya cortó (revocada, suspendida) o abrió el re-login (vencida).
          if (e.status === 401 || e.status === 403) return "auth";
        }
        return "failed"; // red o 5xx: se reintenta en el próximo tick mientras quede tiempo
      }
    };

    const start = async (): Promise<RenewResult> => {
      const locks = typeof navigator !== "undefined" ? navigator.locks : undefined;
      if (locks?.request) {
        return locks.request(LOCK_NAME, { ifAvailable: true }, async lock => (lock ? run() : "skipped"));
      }
      if (!isButton) await sleep(Math.random() * 3000);
      return run();
    };

    // El pointerdown del propio botón ya pudo disparar la renovación automática: se espera esa.
    const joined = inFlightRef.current;
    const pending = joined ?? start()
      .then(result => {
        // Sin renovar (red/5xx, lock de otra pestaña, …): la renovación automática espera al
        // próximo tick (§8.2). Nunca se reintenta en caliente; el 429 ya dejó una espera mayor.
        if (result !== "ok") retryAtRef.current = Math.max(retryAtRef.current, Date.now() + SESSION_TICK_MS);
        return result;
      })
      .finally(() => { inFlightRef.current = null; });
    if (!joined) inFlightRef.current = pending;
    let result: RenewResult = "skipped";
    try {
      result = await pending;
      if (isButton) {
        const meta = readSessionMeta();
        const renewed = result === "ok" || (!!getStoredToken() && !!meta && meta.exp !== decided.exp);
        if (renewed) showRenewed();
        else if (result === "failed") setRenewFailed(true);
      }
    } finally {
      if (isButton) setRenewing(false);
      // Re-evaluar solo si cambió algo que la UI tiene que reflejar (token nuevo, tope o corte).
      // Tras "failed"/"skipped" no: el tick (o el evento `storage` de la otra pestaña) se encarga.
      if (result === "ok" || result === "cap" || result === "auth") evaluateRef.current();
    }
  }, [showRenewed]);

  const evaluate = useCallback(() => {
    const meta = getStoredToken() && sessionState() === "active" ? readSessionMeta() : null;
    if (!meta) {
      // Sin meta (token viejo o backend anterior): el vencimiento se detecta con el 401, como antes.
      setExpiryAt(null);
      setCapAt(null);
      setRenewFailed(false);
      return;
    }
    const now = Date.now();
    const busy = hasActiveLongOps() || inPlaceState().busy;
    if (busy) markActivity();
    const d = decide(meta, now, lastActivity(), busy);
    if (d.expired) {
      setExpiryAt(null);
      setCapAt(null);
      expireSession("clock");
      return;
    }
    if (d.renew && now >= retryAtRef.current) void renew("auto");
    setExpiryAt(d.warnExpiry ? meta.exp : null);
    if (!d.warnExpiry) setRenewFailed(false);
    const w = windowsFor(meta);
    const dismissed = capDismissedRef.current === meta.max && meta.max - now > w.capReappear;
    setCapAt(d.warnCap && !dismissed ? meta.max : null);
    setMaxAge(meta.max_age);
  }, [renew]);

  useEffect(() => { evaluateRef.current = evaluate; }, [evaluate]);

  /** Otra pestaña guardó un token de login: se adopta solo si es la misma persona (D8). */
  const adoptExternalLogin = useCallback(async (token: string) => {
    const prevDni = sessionUserDni();
    if (!prevDni) { evaluateRef.current(); return; }
    let dni: string;
    try {
      dni = (await api.me()).dni;
    } catch {
      return; // el interceptor ya decidió (401/403); una falla de red no cambia nada
    }
    if (getStoredToken() !== token) return; // cambió de nuevo mientras se preguntaba
    if (dni === prevDni) {
      if (sessionState() === "expired") markSessionRestored();
      evaluateRef.current();
      return;
    }
    // Otra persona: nunca se reutiliza su token en silencio.
    setSessionUser(prevDni);
    if (sessionState() === "expired" && isInPlaceActive()) {
      setDialog(d => ({ mode: "expired", dni: d?.dni ?? prevDni, otherUser: true }));
      return;
    }
    // Corte "terminada" sin borrar el token: es la sesión de la otra pestaña.
    writeSessionNotice({ reason: "session_ended", interrupted: hasActiveLongOps() });
    dispatchAuthEvent({ type: "session_ended" });
  }, []);

  /* ── Reloj, actividad, pestañas y eventos de la sesión ── */
  useEffect(() => {
    evaluateRef.current();
    const tick = setInterval(() => evaluateRef.current(), SESSION_TICK_MS);

    let lastInteractionEval = 0;
    const onActivity = () => {
      markActivity();
      const now = Date.now();
      if (now - lastInteractionEval >= INTERACTION_EVAL_MS) {
        lastInteractionEval = now;
        evaluateRef.current();
      }
    };
    const activityEvents = ["pointerdown", "keydown", "wheel", "touchstart"] as const;
    const opts: AddEventListenerOptions = { capture: true, passive: true };
    activityEvents.forEach(t => window.addEventListener(t, onActivity, opts));

    const onVisibility = () => { if (document.visibilityState === "visible") evaluateRef.current(); };
    document.addEventListener("visibilitychange", onVisibility);

    const onStorage = (e: StorageEvent) => {
      if (e.storageArea !== localStorage) return;
      switch (e.key) {
        case SESSION_EXPIRED_KEY:
          if (e.newValue) expireSession("storage");
          break;
        case SESSION_META_KEY:
          evaluateRef.current();
          break;
        case TOKEN_KEY:
          if (!e.newValue) break; // "Salir" u otro corte: se nota en el próximo pedido (S2)
          if (readSessionMeta()?.src === "refresh") evaluateRef.current();
          else void adoptExternalLogin(e.newValue);
          break;
        case SESSION_ACTIVITY_KEY:
        default:
          break; // la actividad la lee `lastActivity()` en el próximo `evaluate()`
      }
    };
    window.addEventListener("storage", onStorage);

    const onSession = (e: Event) => {
      const detail = (e as CustomEvent<SessionEventDetail>).detail;
      if (detail?.type === "expired") {
        const dni = sessionUserDni();
        if (dni) setDialog(d => ({ mode: "expired", dni, otherUser: d?.otherUser ?? false }));
        setExpiryAt(null);
        setCapAt(null);
      } else if (detail?.type === "restored") {
        setDialog(null);
        capDismissedRef.current = null;
        evaluateRef.current();
      }
    };
    window.addEventListener(SESSION_EVENT, onSession);

    const onAuth = (e: Event) => {
      if ((e as CustomEvent<AuthEventDetail>).detail?.type === "session_ended") {
        setDialog(null);
        setExpiryAt(null);
        setCapAt(null);
      }
    };
    window.addEventListener(AUTH_EVENT, onAuth);

    const timers = timersRef.current;
    return () => {
      clearInterval(tick);
      activityEvents.forEach(t => window.removeEventListener(t, onActivity, opts));
      document.removeEventListener("visibilitychange", onVisibility);
      window.removeEventListener("storage", onStorage);
      window.removeEventListener(SESSION_EVENT, onSession);
      window.removeEventListener(AUTH_EVENT, onAuth);
      timers.forEach(t => clearTimeout(t));
      timers.clear();
    };
  }, [adoptExternalLogin]);

  /* ── Acciones de los banners y del diálogo ── */

  const keepAlive = useCallback(() => {
    markActivity(true);
    void renew("button");
  }, [renew]);

  const dismissCap = useCallback(() => {
    capDismissedRef.current = readSessionMeta()?.max ?? null;
    setCapAt(null);
  }, []);

  const openEarly = useCallback(() => {
    const dni = sessionUserDni();
    if (dni) setDialog({ mode: "early", dni, otherUser: false });
  }, []);

  const closeEarly = useCallback(() => {
    setDialog(d => (d?.mode === "early" ? null : d));
  }, []);

  const onReloginSuccess = useCallback(() => {
    // `api.login` ya guardó la sesión nueva (y su propio tope).
    markSessionRestored();
  }, []);

  const onReloginExit = useCallback(() => {
    const otherUser = dialog?.otherUser ?? false;
    setSessionUser(null);
    if (otherUser) {
      // El token guardado es de otra persona (otra pestaña): no se borra.
      writeSessionNotice({ reason: "session_ended", interrupted: false });
      dispatchAuthEvent({ type: "session_ended" });
    } else {
      endSession("session_ended", false);
    }
  }, [dialog]);

  const value = useMemo<SessionUiValue>(() => ({
    expiryAt, capAt, renewing, renewFailed, renewedFlash, announce, keepAlive, dismissCap, openEarly,
  }), [expiryAt, capAt, renewing, renewFailed, renewedFlash, announce, keepAlive, dismissCap, openEarly]);

  return (
    <SessionUiContext.Provider value={value}>
      {children}
      <ReloginDialog
        visible={!!dialog}
        mode={shownDialog?.mode ?? "expired"}
        dni={shownDialog?.dni ?? ""}
        maxAgeSeconds={maxAge}
        otherUser={shownDialog?.otherUser ?? false}
        onDismiss={closeEarly}
        onSuccess={onReloginSuccess}
        onExit={onReloginExit}
      />
    </SessionUiContext.Provider>
  );
}
