/**
 * Mensajes entre pestañas del mismo navegador (wizard-concurrencia-pestanas §5.3)
 * y lock de "Generar" por caso (DT9). Tolerante a que no exista
 * `BroadcastChannel` ni `navigator.locks`: sin ellos todo degrada a no-op.
 */

import { getTabOriginId } from "./tab-origin";

const CHANNEL_NAME = "factum-wizard";

export type TabMessage =
  | { v: 1; type: "presence"; origin_id: string; serial: string; case_id: string | null; case_label: string | null; step: number; at: number }
  | { v: 1; type: "bye"; origin_id: string }
  | { v: 1; type: "cases_changed"; origin_id: string }
  | { v: 1; type: "generating"; origin_id: string; case_id: string; active: boolean; at: number };

/** Lo que arma el emisor: `v` y `origin_id` los agrega `postTabMessage`. */
type DistributiveOmit<T, K extends PropertyKey> = T extends unknown ? Omit<T, K> : never;
export type OutgoingTabMessage = DistributiveOmit<TabMessage, "v" | "origin_id">;

const isStr = (v: unknown): v is string => typeof v === "string" && v.length > 0;
const isStrOrNull = (v: unknown): v is string | null => v === null || typeof v === "string";
const isNum = (v: unknown): v is number => typeof v === "number" && Number.isFinite(v);

/** Valida la forma de un mensaje recibido; `null` si no es uno nuestro (`v !== 1` o campos inválidos). */
export function parseTabMessage(raw: unknown): TabMessage | null {
  if (!raw || typeof raw !== "object") return null;
  const m = raw as Record<string, unknown>;
  if (m.v !== 1 || !isStr(m.origin_id)) return null;
  switch (m.type) {
    case "presence":
      if (!isStr(m.serial) || !isStrOrNull(m.case_id) || !isStrOrNull(m.case_label) || !isNum(m.step) || !isNum(m.at)) return null;
      return {
        v: 1, type: "presence", origin_id: m.origin_id, serial: m.serial,
        case_id: m.case_id, case_label: m.case_label, step: m.step, at: m.at,
      };
    case "bye":
      return { v: 1, type: "bye", origin_id: m.origin_id };
    case "cases_changed":
      return { v: 1, type: "cases_changed", origin_id: m.origin_id };
    case "generating":
      if (!isStr(m.case_id) || typeof m.active !== "boolean" || !isNum(m.at)) return null;
      return { v: 1, type: "generating", origin_id: m.origin_id, case_id: m.case_id, active: m.active, at: m.at };
    default:
      return null;
  }
}

type Listener = (msg: TabMessage) => void;
const listeners = new Set<Listener>();
/** `undefined` = todavía no se intentó abrir; `null` = el navegador no lo soporta. */
let channel: BroadcastChannel | null | undefined;

/** Generaciones activas de otras pestañas (fallback sin Web Locks): caso → origen → `at`. */
const othersGenerating = new Map<string, Map<string, number>>();
const GENERATING_TTL_MS = 120_000;

function trackGenerating(msg: TabMessage) {
  if (msg.type === "bye") {
    othersGenerating.forEach(byOrigin => byOrigin.delete(msg.origin_id));
    return;
  }
  if (msg.type !== "generating") return;
  const byOrigin = othersGenerating.get(msg.case_id) ?? new Map<string, number>();
  if (msg.active) byOrigin.set(msg.origin_id, msg.at);
  else byOrigin.delete(msg.origin_id);
  if (byOrigin.size > 0) othersGenerating.set(msg.case_id, byOrigin);
  else othersGenerating.delete(msg.case_id);
}

function getChannel(): BroadcastChannel | null {
  if (channel !== undefined) return channel;
  if (typeof window === "undefined" || typeof BroadcastChannel === "undefined") {
    channel = null;
    return null;
  }
  try {
    channel = new BroadcastChannel(CHANNEL_NAME);
  } catch {
    channel = null;
    return null;
  }
  channel.onmessage = (e: MessageEvent) => {
    const msg = parseTabMessage(e.data);
    // Los propios no llegan por BroadcastChannel, pero una pestaña duplicada podría compartir id: se descartan igual.
    if (!msg || msg.origin_id === getTabOriginId()) return;
    trackGenerating(msg);
    listeners.forEach(l => { try { l(msg); } catch { /* un listener roto no corta a los demás */ } });
  };
  return channel;
}

/** `true` si el navegador soporta `BroadcastChannel`. */
export function tabChannelAvailable(): boolean {
  return getChannel() !== null;
}

/** Publica un mensaje a las otras pestañas. No-op sin `BroadcastChannel`. */
export function postTabMessage(msg: OutgoingTabMessage): void {
  const ch = getChannel();
  if (!ch) return;
  try {
    ch.postMessage({ ...msg, v: 1, origin_id: getTabOriginId() });
  } catch { /* canal cerrado: no importa */ }
}

/** Escucha los mensajes de las otras pestañas. Devuelve la función para dejar de escuchar. */
export function subscribeTabMessages(fn: Listener): () => void {
  getChannel();
  listeners.add(fn);
  return () => { listeners.delete(fn); };
}

/**
 * Fallback sin Web Locks: `true` si otra pestaña avisó que está generando este
 * caso en los últimos 120 s y todavía no avisó que terminó. No es atómico.
 */
export function isGenerationBusyFallback(caseId: string, now = Date.now()): boolean {
  const byOrigin = othersGenerating.get(caseId);
  if (!byOrigin) return false;
  for (const at of byOrigin.values()) {
    if (now - at < GENERATING_TTL_MS) return true;
  }
  return false;
}

export type GenerationLockResult<T> = { status: "ok"; value: T } | { status: "busy" };

/**
 * Corre `fn` con el lock "Generar" de este caso (DT9). Si otra pestaña o
 * ventana del mismo navegador lo tiene, devuelve `{ status: "busy" }` sin
 * correr `fn`. Con `navigator.locks` (contexto seguro) es atómico y se libera
 * solo si la pestaña se cierra; sin él, usa los avisos `generating` del canal.
 * Los errores de `fn` se propagan.
 */
export async function withGenerationLock<T>(caseId: string, fn: () => Promise<T>): Promise<GenerationLockResult<T>> {
  const locks = typeof navigator !== "undefined" ? navigator.locks : undefined;
  if (locks && typeof locks.request === "function") {
    return locks.request(
      `factum:generate:${caseId}`,
      { ifAvailable: true },
      async (lock): Promise<GenerationLockResult<T>> => {
        if (!lock) return { status: "busy" };
        return { status: "ok", value: await fn() };
      },
    );
  }

  if (isGenerationBusyFallback(caseId)) return { status: "busy" };
  postTabMessage({ type: "generating", case_id: caseId, active: true, at: Date.now() });
  try {
    return { status: "ok", value: await fn() };
  } finally {
    postTabMessage({ type: "generating", case_id: caseId, active: false, at: Date.now() });
  }
}
