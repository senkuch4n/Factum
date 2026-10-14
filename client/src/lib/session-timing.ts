/**
 * Cálculo puro de las ventanas de la sesión (sesion-larga §7.4 y §8.2).
 * Sin DOM ni React: lo usa `SessionManager` y se puede leer (y probar) aislado.
 * Todos los instantes son epoch ms del reloj local; las duraciones de la meta
 * (`ttl`, `max_age`) vienen en segundos, como las manda el backend.
 */

const MIN = 60_000;

/** Meta de la sesión que se guarda en `localStorage["factum_session"]` (§7.3). */
export interface SessionMeta {
  v: 1;
  /** Vencimiento del token vigente (epoch ms, reloj local). */
  exp: number;
  /** Tope absoluto desde el login (epoch ms, reloj local). */
  max: number;
  /** Vida nominal del token (`token_ttl`, segundos). */
  ttl: number;
  /** Tope configurado (`session_max_age`, segundos). */
  max_age: number;
  src: "login" | "refresh";
}

export interface SessionWindows {
  /** Renovar si al token le queda menos que esto. */
  renewWindow: number;
  /** Hubo actividad reciente si pasó menos que esto desde la última. */
  activityWindow: number;
  /** Banner de vencimiento por inactividad. */
  warnWindow: number;
  /** Banner del tope. */
  capWarnWindow: number;
  /** El banner del tope vuelve tras "Entendido" cuando falta menos que esto. */
  capReappear: number;
}

/** Intervalo del reloj de `SessionManager`. */
export const SESSION_TICK_MS = 15_000;

/** Con 2 h / 12 h da 30 min, 30 min, 10 min, 30 min y 5 min (D3 de la HU). */
export function windowsFor(meta: Pick<SessionMeta, "ttl" | "max_age">): SessionWindows {
  const ttl = Math.max(0, meta.ttl) * 1000;
  const maxAge = Math.max(0, meta.max_age) * 1000;
  const renewWindow = Math.min(30 * MIN, ttl / 4);
  const capWarnWindow = Math.min(30 * MIN, maxAge / 4);
  return {
    renewWindow,
    activityWindow: renewWindow,
    warnWindow: Math.min(10 * MIN, ttl / 6),
    capWarnWindow,
    capReappear: Math.min(5 * MIN, capWarnWindow / 3),
  };
}

export interface SessionDecision {
  /** Pedir `POST /api/auth/refresh` ahora. */
  renew: boolean;
  /** Mostrar "Tu sesión vence a las HH:MM por inactividad". */
  warnExpiry: boolean;
  /** Mostrar el aviso del tope (sin contar el "Entendido", que maneja la UI). */
  warnCap: boolean;
  /** El token ya venció según el reloj. */
  expired: boolean;
  /** El token vigente ya llega al tope: no se puede renovar más. */
  atCap: boolean;
}

/**
 * Qué hacer en este instante. `busy` (operación larga en curso) cuenta como
 * actividad (S1 = A).
 */
export function decide(meta: SessionMeta, now: number, lastActivity: number, busy: boolean): SessionDecision {
  const w = windowsFor(meta);
  const remaining = meta.exp - now;
  const expired = remaining <= 0;
  const atCap = meta.exp >= meta.max - 1000;
  const active = busy || now - lastActivity < w.activityWindow;
  return {
    expired,
    atCap,
    renew: !expired && !atCap && remaining < w.renewWindow && active,
    warnExpiry: !expired && !atCap && remaining < w.warnWindow,
    warnCap: !expired && meta.max - now < w.capWarnWindow,
  };
}

const CLOCK = new Intl.DateTimeFormat("es-AR", { hour: "2-digit", minute: "2-digit", hour12: false });

/** `HH:MM` en hora local. */
export function formatClock(epochMs: number): string {
  return CLOCK.format(new Date(epochMs));
}
