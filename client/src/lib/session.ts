/**
 * Sesión del navegador (sesion-larga §8.1, §8.4, §8.5).
 *
 * - En memoria (nunca se persiste): DNI del usuario, estado (`active`/`expired`),
 *   lo que registra el asistente (`registerInPlace`) y el contador de
 *   operaciones largas.
 * - En `localStorage`, **solo** las claves de §7.3: el token, la meta con
 *   números, la marca de actividad y la marca de "sesión vencida". Nada de la
 *   causa (ni id de caso, ni nombres, ni DNI, ni nombres de archivo).
 *
 * No importa `lib/api.ts` (lo importa él a este módulo): los errores se
 * reconocen por forma (`status`/`code`).
 */

import { useSyncExternalStore } from "react";
import type { AuthResponse } from "@/types";
import { writeSessionNotice, type SessionNoticeReason } from "./session-notice";
import type { SessionMeta } from "./session-timing";

export type { SessionMeta } from "./session-timing";

/* ── Claves (§7.3). Solo estas. ── */

export const TOKEN_KEY = "factum_token";
export const SESSION_META_KEY = "factum_session";
export const SESSION_ACTIVITY_KEY = "factum_session_activity";
export const SESSION_EXPIRED_KEY = "factum_session_expired";

/** Las que escucha `SessionManager` en el evento `storage`. */
export const SESSION_STORAGE_KEYS = [TOKEN_KEY, SESSION_META_KEY, SESSION_ACTIVITY_KEY, SESSION_EXPIRED_KEY] as const;

/* ── Eventos de ventana (§7.2) ── */

/** Corte de sesión que navega (lo escucha `SessionWatcher`). */
export const AUTH_EVENT = "factum:auth";
export type AuthEventDetail = { type: "session_ended" } | { type: "password_change_required" };

/** Ciclo de vida de la sesión dentro de la pestaña. */
export const SESSION_EVENT = "factum:session";
export type SessionEventDetail = { type: "expired" } | { type: "restored" } | { type: "renewed" };

/** Códigos 401 que significan "sesión vencida" (re-login en el lugar). */
export const SESSION_EXPIRED_CODES: readonly string[] = ["invalid_token", "session_max_age"];

const ACTIVITY_WRITE_MS = 60_000;

function hasWindow() {
  return typeof window !== "undefined";
}

function storageGet(key: string): string | null {
  if (!hasWindow()) return null;
  try { return localStorage.getItem(key); } catch { return null; }
}

function storageSet(key: string, value: string) {
  try { localStorage.setItem(key, value); } catch { /* storage bloqueado: la sesión sigue en memoria */ }
}

function storageRemove(key: string) {
  try { localStorage.removeItem(key); } catch { /* idem */ }
}

export function dispatchAuthEvent(detail: AuthEventDetail) {
  if (!hasWindow()) return;
  window.dispatchEvent(new CustomEvent<AuthEventDetail>(AUTH_EVENT, { detail }));
}

export function dispatchSessionEvent(detail: SessionEventDetail) {
  if (!hasWindow()) return;
  window.dispatchEvent(new CustomEvent<SessionEventDetail>(SESSION_EVENT, { detail }));
}

/* ── Token y meta ── */

export function getStoredToken(): string | null {
  return storageGet(TOKEN_KEY);
}

const finite = (n: unknown): n is number => typeof n === "number" && Number.isFinite(n);

export function readSessionMeta(): SessionMeta | null {
  const raw = storageGet(SESSION_META_KEY);
  if (!raw) return null;
  try {
    const m = JSON.parse(raw) as Partial<SessionMeta> | null;
    if (!m || m.v !== 1 || !finite(m.exp) || !finite(m.max) || !finite(m.ttl) || !finite(m.max_age)) return null;
    if (m.src !== "login" && m.src !== "refresh") return null;
    return { v: 1, exp: m.exp, max: m.max, ttl: m.ttl, max_age: m.max_age, src: m.src };
  } catch {
    return null;
  }
}

function writeMeta(meta: SessionMeta) {
  storageSet(SESSION_META_KEY, JSON.stringify(meta));
}

/**
 * Guarda la respuesta de login / change-password / refresh: **meta primero,
 * token después** (las otras pestañas leen la meta al ver el token nuevo).
 * Sin los campos nuevos (backend anterior) no hay meta: no se programa nada.
 * Un login vuelve la pestaña a `active` y borra la marca de vencida.
 */
export function writeSession(resp: AuthResponse, src: "login" | "refresh") {
  if (!hasWindow()) return;
  const now = Date.now();
  if (finite(resp.expires_in) && finite(resp.session_max_in) && finite(resp.token_ttl) && finite(resp.session_max_age)) {
    writeMeta({
      v: 1,
      exp: now + resp.expires_in * 1000,
      max: now + Math.max(resp.session_max_in, resp.expires_in) * 1000,
      ttl: resp.token_ttl,
      max_age: resp.session_max_age,
      src,
    });
  } else {
    storageRemove(SESSION_META_KEY);
  }
  storageSet(TOKEN_KEY, resp.token);
  if (src === "login") {
    storageRemove(SESSION_EXPIRED_KEY);
    state = "active";
    // Iniciar sesión es interacción real: cuenta para la ventana de actividad.
    markActivity(true);
  }
  bumpTokenVersion();
}

/** El refresh respondió `session_max_age`: todas las pestañas pasan a "en el tope" (§8.2). */
export function markSessionAtCap() {
  const meta = readSessionMeta();
  if (!meta || meta.max === meta.exp) return;
  writeMeta({ ...meta, max: meta.exp });
}

/** Borra token y meta (no la marca de actividad: no identifica a nadie). */
export function clearSession() {
  if (!hasWindow()) return;
  storageRemove(SESSION_META_KEY);
  storageRemove(TOKEN_KEY);
  bumpTokenVersion();
}

/* ── Actividad (S1 = A) ── */

let lastActivityMem = 0;
let lastActivityWrite = 0;

/**
 * Registra actividad. En memoria siempre; en `localStorage` (compartida con
 * las otras pestañas) como mucho una vez por minuto, salvo `force`.
 */
export function markActivity(force = false) {
  if (!hasWindow()) return;
  const now = Date.now();
  lastActivityMem = now;
  if (force || now - lastActivityWrite >= ACTIVITY_WRITE_MS) {
    lastActivityWrite = now;
    storageSet(SESSION_ACTIVITY_KEY, String(now));
  }
}

/** La actividad más reciente de cualquier pestaña (epoch ms; 0 si nunca hubo). */
export function lastActivity(): number {
  const shared = Number(storageGet(SESSION_ACTIVITY_KEY));
  return Math.max(lastActivityMem, Number.isFinite(shared) ? shared : 0);
}

/* ── Usuario (solo en memoria) ── */

let userDni: string | null = null;
let hadSessionFlag = false;

export function setSessionUser(dni: string | null) {
  userDni = dni;
  if (dni) hadSessionFlag = true;
}

export function sessionUserDni(): string | null {
  return userDni;
}

/** Esta pestaña llegó a tener una sesión válida (login o `me`). */
export function hadSession(): boolean {
  return hadSessionFlag;
}

/* ── Operaciones largas (subida, generación, registro de evidencia) ── */

let activeLongOps = 0;

export function beginLongOp() { activeLongOps++; }
export function endLongOp() { activeLongOps = Math.max(0, activeLongOps - 1); }
export function hasActiveLongOps(): boolean { return activeLongOps > 0; }

/* ── Asistente activo (registerInPlace, §8.10) ── */

export interface InPlaceRecording {
  /** ISO-8601 de inicio (Tatana). */
  startedAt: string;
  stopping: boolean;
  onStop: () => void;
}

export interface InPlaceState {
  /** Asistente en los pasos 2 a 5 (D6). */
  active: boolean;
  /** Archivos "Pendiente de registrar". Un número, sin nombres. */
  pendingCount: number;
  /** Grabación, guardado o generación en curso: cuenta como actividad. */
  busy: boolean;
  recording: InPlaceRecording | null;
}

const IDLE_IN_PLACE: InPlaceState = { active: false, pendingCount: 0, busy: false, recording: null };
let inPlace: InPlaceState = IDLE_IN_PLACE;
const inPlaceListeners = new Set<() => void>();

export function registerInPlace(next: InPlaceState | null) {
  inPlace = next ?? IDLE_IN_PLACE;
  inPlaceListeners.forEach(l => l());
}

export function inPlaceState(): InPlaceState {
  return inPlace;
}

export function isInPlaceActive(): boolean {
  return inPlace.active;
}

function subscribeInPlace(cb: () => void) {
  inPlaceListeners.add(cb);
  return () => { inPlaceListeners.delete(cb); };
}

/** Estado del asistente para el diálogo de re-login (pendientes y grabación). */
export function useInPlaceState(): InPlaceState {
  return useSyncExternalStore(subscribeInPlace, inPlaceState, () => IDLE_IN_PLACE);
}

/* ── Estado de la sesión ── */

export type SessionStateValue = "active" | "expired";
let state: SessionStateValue = "active";

export function sessionState(): SessionStateValue {
  return state;
}

/**
 * Corte "terminada" (el de antes de esta HU): borra la sesión, deja el aviso
 * para el login y `SessionWatcher` navega a `/`.
 */
export function endSession(reason: SessionNoticeReason = "session_ended", interrupted = hasActiveLongOps()) {
  clearSession();
  writeSessionNotice({ reason, interrupted });
  dispatchAuthEvent({ type: "session_ended" });
}

/**
 * La sesión venció (401 `invalid_token`/`session_max_age`, el reloj, u otra
 * pestaña). Idempotente. Con el asistente activo abre el re-login en el lugar
 * (sin navegar); si no, corte "terminada" como hoy (S5 = A).
 */
export function expireSession(origin: "http" | "clock" | "storage") {
  if (state === "expired") return;
  state = "expired";
  if (origin !== "storage") {
    storageSet(SESSION_EXPIRED_KEY, String(Date.now()));
    clearSession();
  }
  if (isInPlaceActive() && sessionUserDni()) {
    dispatchSessionEvent({ type: "expired" });
    return;
  }
  endSession("session_ended");
}

/** Sesión recuperada en esta pestaña (re-login aquí o en otra pestaña con el mismo DNI). */
export function markSessionRestored() {
  state = "active";
  dispatchSessionEvent({ type: "restored" });
}

export function onSessionRestored(cb: () => void): () => void {
  if (!hasWindow()) return () => {};
  const handler = (e: Event) => {
    if ((e as CustomEvent<SessionEventDetail>).detail?.type === "restored") cb();
  };
  window.addEventListener(SESSION_EVENT, handler);
  return () => window.removeEventListener(SESSION_EVENT, handler);
}

/* ── Errores ── */

/** `saveOne` no registra con la sesión vencida: lanza esto (§8.9). */
export class SessionExpiredError extends Error {
  constructor() {
    super("La sesión venció: el registro queda pendiente.");
    this.name = "SessionExpiredError";
  }
}

/**
 * Error de un pedido que falló por la sesión vencida: un `ApiError` 401 con
 * `code` de vencimiento, o un 401 sin token con la sesión ya vencida.
 */
export function isSessionExpiredError(e: unknown): boolean {
  if (e instanceof SessionExpiredError) return true;
  if (!(e instanceof Error)) return false;
  const { status, code } = e as Error & { status?: unknown; code?: unknown };
  if (status !== 401) return false;
  if (typeof code === "string" && SESSION_EXPIRED_CODES.includes(code)) return true;
  return state === "expired";
}

/* ── Versión del token (para las URL con ?token= armadas en el render) ── */

let tokenVersion = 0;
const tokenListeners = new Set<() => void>();
let storageHooked = false;

function bumpTokenVersion() {
  tokenVersion++;
  tokenListeners.forEach(l => l());
}

function onTokenStorage(e: StorageEvent) {
  if (e.key === TOKEN_KEY || e.key === null) bumpTokenVersion();
}

export function subscribeToken(cb: () => void): () => void {
  tokenListeners.add(cb);
  if (!storageHooked && hasWindow()) {
    storageHooked = true;
    window.addEventListener("storage", onTokenStorage);
  }
  return () => {
    tokenListeners.delete(cb);
    if (tokenListeners.size === 0 && storageHooked) {
      storageHooked = false;
      window.removeEventListener("storage", onTokenStorage);
    }
  };
}

/** Re-renderiza el componente cuando cambia el token (de esta pestaña o de otra). */
export function useTokenVersion(): number {
  return useSyncExternalStore(subscribeToken, () => tokenVersion, () => 0);
}
