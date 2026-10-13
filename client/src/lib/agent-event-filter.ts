/**
 * Filtro de eventos de Tatana por pestaña (wizard-concurrencia-pestanas DT1).
 * Puro: sin React ni DOM. Decide si un evento de evidencia que llega por el
 * WebSocket le pertenece a esta pestaña.
 *
 * Reglas, en este orden (la primera que aplica decide):
 *
 * | # | Condición                                                      | ¿Se acepta?          |
 * |---|----------------------------------------------------------------|----------------------|
 * | 0 | La pestaña no tiene caso abierto o no tiene celular elegido     | no                   |
 * | 1 | `data.serial` es string y no es el celular de esta pestaña      | no                   |
 * | 2 | `data.case_id` es string y no es el caso de esta pestaña        | no                   |
 * | 3 | `data.origin_id` es el de esta pestaña                          | sí                   |
 * | 4 | `data.case_id` es el caso de esta pestaña (otra pestaña del     | sí                   |
 * |   | mismo caso, o una grabación detenida por fuera o tras recargar) |                      |
 * | 5 | `data.origin_id` es string (de otra pestaña, sin `case_id`)     | no                   |
 * | 6 | Sin `case_id` ni `origin_id` (Tatana viejo o pedido sin dueño): |                      |
 * |   | – `photo_taken`                                                 | no                   |
 * |   | – `screenshot_taken` sin `serial`                               | no                   |
 * |   | – el resto                                                      | solo en el paso 3    |
 *
 * Diferencia con el texto de la SDD: el chequeo "`case_id` distinto → no" va
 * **antes** que "`origin_id` propio → sí". Así, si esta pestaña cambió de caso
 * mientras una captura o grabación suya seguía en vuelo, el archivo no cae en
 * el caso nuevo (mismo criterio que la regla `mine` de grabacion-android-robusta).
 */

export type AttributedEventType = "screenshot_taken" | "photo_taken" | "recording_started" | "recording_stopped";

export interface AttributionCtx {
  /** `getTabOriginId()` de esta pestaña. */
  originId: string;
  /** Caso abierto en el wizard, o `null`. */
  caseId: string | null;
  /** Serial del celular elegido, o `null`. */
  serial: string | null;
  /** Paso actual del wizard. */
  step: number;
}

/** Campos de atribución que puede traer el `data` de un evento (todos opcionales: Tatana viejo no los manda). */
export interface AttributedEventData {
  serial?: unknown;
  case_id?: unknown;
  origin_id?: unknown;
}

const isStr = (v: unknown): v is string => typeof v === "string" && v.length > 0;
/** Ids de caso (UUID) sin distinguir mayúsculas. */
const sameCase = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();

export function shouldAcceptAgentEvent(
  type: AttributedEventType,
  data: AttributedEventData | null | undefined,
  ctx: AttributionCtx,
): boolean {
  const d = data ?? {};
  // 0
  if (!ctx.caseId || !ctx.serial) return false;
  // 1
  if (isStr(d.serial) && d.serial !== ctx.serial) return false;
  // 2
  if (isStr(d.case_id) && !sameCase(d.case_id, ctx.caseId)) return false;
  // 3
  if (isStr(d.origin_id) && d.origin_id === ctx.originId) return true;
  // 4
  if (isStr(d.case_id)) return true;
  // 5
  if (isStr(d.origin_id)) return false;
  // 6
  if (type === "photo_taken") return false;
  if (type === "screenshot_taken" && !isStr(d.serial)) return false;
  return ctx.step === 3;
}
