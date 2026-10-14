/**
 * Sellado de tiempo RFC 3161 (sellado-tiempo, SDD §9.1 y §9.4).
 *
 * Funciones puras para el estado de cada sello y la sincronización de los
 * `.tsr` con la carpeta del caso a través de Tatana. Ninguna lanza.
 */

import { agent } from "@/lib/agent";
import {
  api,
  type Case, type CaseTimestamp, type CaseTimestampsResponse, type EvidenceStorage, type TimestampTarget,
} from "@/lib/api";

/** Objeto sellado en palabras (títulos de la trazabilidad). */
export const TARGET_LABEL: Record<TimestampTarget, string> = {
  evidence: "evidencia",
  zip: "ZIP",
  report: "informe",
};

export type TimestampTone = "success" | "pending" | "neutral" | "warning";

export interface TimestampLine {
  tone: TimestampTone;
  text: string;
  /** Texto secundario (nota del ZIP sellado tarde, o el último error). */
  detail?: string;
}

/** Contexto del caso para decidir el texto de una fila (§9.4). */
export interface TimestampLineContext {
  target: TimestampTarget;
  /** Config **actual** del backend. */
  enabled: boolean;
  /** `Case.timestamp_mode`. */
  caseMode: "enabled" | "disabled" | null;
  storage: EvidenceStorage;
  /** `true` si el caso ya está generado (`completed`). */
  generated: boolean;
}

/** "Sellado por Free TSA el 14/10/2026 09:46:05 (UTC-03:00) · N.º de serie 1A2B". */
export function grantedText(ts: CaseTimestamp): string {
  const by = ts.authority ? `Sellado por ${ts.authority}` : "Sellado";
  const at = ts.gen_time_display ?? (ts.gen_time ? new Date(ts.gen_time).toLocaleString("es-AR") : null);
  const serial = ts.serial_number ? ` · N.º de serie ${ts.serial_number}` : "";
  return `${by}${at ? ` el ${at}` : ""}${serial}`;
}

/**
 * Texto y tono de una fila del bloque "Sello de tiempo", con la prioridad de la
 * tabla de §9.4. `null` = la fila no se muestra (ZIP o informe de un borrador
 * todavía no existen).
 */
export function timestampLine(ts: CaseTimestamp | null, ctx: TimestampLineContext): TimestampLine | null {
  if (ctx.storage === "server") {
    return { tone: "neutral", text: "Sin sello de tiempo (flujo anterior de evidencia en el servidor)" };
  }
  if (ctx.generated && ctx.caseMode === null && !ts) {
    return { tone: "neutral", text: "Sin sello de tiempo (caso anterior a esta función)" };
  }
  if (ctx.caseMode === "disabled" && !ts) {
    return { tone: "neutral", text: "Sin sello de tiempo (sellado desactivado al generar)" };
  }
  if (ts) {
    switch (ts.status) {
      case "granted":
        return ts.target === "zip" && ts.in_report === false
          ? {
            tone: "success",
            text: grantedText(ts),
            detail: "Obtenido después de emitir el informe; el informe indica que no tenía sello.",
          }
          : { tone: "success", text: grantedText(ts) };
      case "pending":
        if (!ctx.enabled) return { tone: "neutral", text: "Sin sello (sellado desactivado; no se reintenta)" };
        return {
          tone: "pending",
          text: ts.attempts > 0 ? `Pendiente: se reintenta solo (intento ${ts.attempts})` : "Pendiente: se reintenta solo",
        };
      case "failed":
        return { tone: "warning", text: "No se pudo obtener el sello", detail: ts.last_error?.trim() || undefined };
    }
  }
  // Sin documento.
  if (ctx.target !== "evidence") {
    // Borrador: el ZIP y el informe todavía no existen. Generado con el sellado activo pero sin documento
    // (no debería pasar): se dice tal cual, sin inventar un estado.
    return ctx.generated ? { tone: "neutral", text: "Sin sello de tiempo" } : null;
  }
  return ctx.enabled
    ? { tone: "neutral", text: "Sin sello (registrado antes de activar el sellado)" }
    : { tone: "neutral", text: "Sin sello (sellado desactivado)" };
}

/** "3 de 4 archivos sellados". */
export function evidenceSummary(res: Pick<CaseTimestampsResponse, "evidence_sealed" | "evidence_total">): string {
  const total = res.evidence_total;
  return `${res.evidence_sealed} de ${total} ${total === 1 ? "archivo sellado" : "archivos sellados"}`;
}

/** `true` si hay al menos un sello obtenido (habilita la descarga y la copia). */
export function hasGranted(res: CaseTimestampsResponse): boolean {
  return res.zip?.status === "granted"
    || res.report?.status === "granted"
    || res.evidence.some(r => r.timestamp?.status === "granted");
}

/** `true` si hay algún sello que la cola todavía va a reintentar (para el polling). */
export function hasPending(res: CaseTimestampsResponse): boolean {
  if (!res.enabled) return false;
  return res.zip?.status === "pending"
    || res.report?.status === "pending"
    || res.evidence.some(r => r.timestamp?.status === "pending");
}

/** `true` si el caso tiene al menos un documento de sello (en cualquier estado). */
function hasAnyDocument(res: CaseTimestampsResponse): boolean {
  return !!res.zip || !!res.report || res.evidence.some(r => !!r.timestamp);
}

/**
 * Cómo se presenta el bloque entero, para que una instalación sin sellado
 * (D-T3) no muestre bloques vacíos ni estados por fila que no aportan:
 * - `hidden`: el sellado está apagado y el caso no tiene ningún sello;
 * - `summary`: una sola línea informativa (flujo viejo, caso anterior a la
 *   función o generado con el sellado apagado) en lugar de tres filas iguales;
 * - `rows`: las filas ZIP / Informe / Evidencia.
 */
export type TimestampPanelMode =
  | { kind: "hidden" }
  | { kind: "summary"; line: TimestampLine }
  | { kind: "rows" };

export function timestampPanelMode(res: CaseTimestampsResponse, generated: boolean): TimestampPanelMode {
  const any = hasAnyDocument(res);
  if (!any && !res.enabled) return { kind: "hidden" };
  if (!any) {
    const ctx: TimestampLineContext = {
      target: "zip", enabled: res.enabled, caseMode: res.case_mode, storage: res.evidence_storage, generated,
    };
    if (res.evidence_storage === "server" || (generated && res.case_mode === null) || res.case_mode === "disabled") {
      const line = timestampLine(null, ctx);
      if (line) return { kind: "summary", line };
    }
    // Borrador sin evidencia registrada todavía: nada que mostrar.
    if (!generated && res.evidence_total === 0) return { kind: "hidden" };
  }
  return { kind: "rows" };
}

export type SyncSealsResult = "written" | "nothing" | "outdated_agent" | "error";

/** Sincronización en curso por caso: dos llamadas simultáneas comparten la misma (Tatana respondería 409). */
const inFlight = new Map<string, Promise<SyncSealsResult>>();

/**
 * Copia los `.tsr` y el LEEME a `sellos/`, al lado del ZIP, a través de Tatana
 * (idempotente). Nunca lanza. Los `.tsr` solo viven en memoria durante la llamada.
 * Si ya hay una en curso para el caso, devuelve esa.
 */
export function syncCaseSeals(cas: Pick<Case, "id">): Promise<SyncSealsResult> {
  const running = inFlight.get(cas.id);
  if (running) return running;
  const p = doSyncCaseSeals(cas.id).finally(() => inFlight.delete(cas.id));
  inFlight.set(cas.id, p);
  return p;
}

async function doSyncCaseSeals(caseId: string): Promise<SyncSealsResult> {
  try {
    if (!(await agent.supportsCaseSeals())) {
      // `supportsCaseSeals` da `false` también sin Tatana: solo es "desactualizado" si responde.
      await agent.health();
      return "outdated_agent";
    }
    const f = await api.getTimestampFiles(caseId);
    if (f.files.length === 0 || !f.zip_filename) return "nothing";
    await agent.writeCaseSeals(caseId, {
      case_ref: f.case_ref, zip_filename: f.zip_filename, files: f.files, readme: f.readme,
    });
    return "written";
  } catch {
    return "error";
  }
}
