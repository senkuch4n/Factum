/**
 * Aviso de sesión cortada (usuarios-locales §9.2). Funciones puras sobre
 * `sessionStorage`: `lib/api.ts` lo escribe cuando un 401 corta la sesión y
 * el login lo lee (y borra) al montar. Va por `sessionStorage` y no por query
 * string para no depender de `useSearchParams` (y su `Suspense`).
 */

export type SessionNoticeReason = "account_suspended" | "session_ended";

export interface SessionNotice {
  reason: SessionNoticeReason;
  /** Había una subida o generación en curso cuando se cortó la sesión. */
  interrupted: boolean;
}

const KEY = "factum_session_notice";

export function writeSessionNotice(notice: SessionNotice): void {
  if (typeof window === "undefined") return;
  try {
    sessionStorage.setItem(KEY, JSON.stringify(notice));
  } catch { /* storage lleno o bloqueado: el corte igual navega a / */ }
}

/** Lee el aviso pendiente y lo borra (se muestra una sola vez). */
export function readAndClearSessionNotice(): SessionNotice | null {
  if (typeof window === "undefined") return null;
  try {
    const raw = sessionStorage.getItem(KEY);
    if (raw === null) return null;
    sessionStorage.removeItem(KEY);
    const parsed = JSON.parse(raw) as Partial<SessionNotice> | null;
    if (!parsed || (parsed.reason !== "account_suspended" && parsed.reason !== "session_ended")) return null;
    return { reason: parsed.reason, interrupted: parsed.interrupted === true };
  } catch {
    return null;
  }
}

export function sessionNoticeText(notice: SessionNotice): string {
  const base =
    notice.reason === "account_suspended"
      ? "Tu cuenta fue suspendida. Comunicate con Factum para reactivarla."
      : "Tu sesión terminó. Volvé a ingresar.";
  return notice.interrupted ? `${base} La operación en curso se interrumpió.` : base;
}
