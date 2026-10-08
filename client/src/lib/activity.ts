import type { Case } from "@/types";

/** Tipo de evento de la actividad reciente (define ícono y tono en la UI). */
export type ActivityKind = "started" | "evidence" | "report" | "completed" | "error";

export interface ActivityEvent {
  /** Estable entre renders: `<caseId>:<kind>`. */
  id: string;
  kind: ActivityKind;
  caseId: string;
  title: string;
  /** `<nro_referencia> · <detalle>`. */
  subtitle: string;
  /** ISO 8601, tal como viene del backend. */
  at: string;
}

/*
 * Desempate entre eventos del mismo caso con el mismo instante: en la lista
 * (orden descendente) el cierre queda arriba del informe, y el informe arriba
 * de la evidencia y del inicio.
 */
const KIND_RANK: Record<ActivityKind, number> = {
  started: 0,
  evidence: 1,
  report: 2,
  completed: 3,
  error: 3,
};

function deviceLabel(cas: Case): string {
  const d = cas.device;
  return d ? `${d.manufacturer} ${d.model}`.trim() : "";
}

function subtitleOf(cas: Case, detail: string): string {
  return [cas.nro_referencia, detail].filter(Boolean).join(" · ");
}

function timeOf(iso: string | undefined | null): number {
  if (!iso) return Number.NaN;
  return new Date(iso).getTime();
}

/** El `registered_at` más reciente de la evidencia del caso (o `undefined`). */
function latestEvidenceAt(cas: Case): string | undefined {
  let best: string | undefined;
  let bestTime = Number.NEGATIVE_INFINITY;
  for (const item of cas.evidence ?? []) {
    const t = timeOf(item.registered_at);
    if (t > bestTime) { bestTime = t; best = item.registered_at; }
  }
  return best;
}

/** Eventos de un caso, sin ordenar. */
function eventsOf(cas: Case): ActivityEvent[] {
  const device = deviceLabel(cas);
  const make = (kind: ActivityKind, title: string, detail: string, at: string): ActivityEvent => ({
    id: `${cas.id}:${kind}`, kind, caseId: cas.id, title, subtitle: subtitleOf(cas, detail), at,
  });

  const out: ActivityEvent[] = [make("started", "Inspección iniciada", device, cas.created_at)];

  // El listado trae el manifiesto del flujo agent (`[]` en los demás): un solo evento por caso.
  const evidenceCount = cas.evidence?.length ?? 0;
  const evidenceAt = latestEvidenceAt(cas);
  if (evidenceCount > 0 && evidenceAt) {
    const files = evidenceCount === 1 ? "1 archivo" : `${evidenceCount} archivos`;
    out.push(make("evidence", "Evidencia registrada", files, evidenceAt));
  }

  if (cas.status === "error") {
    // Sin fecha propia del fallo: el último hito conocido del caso.
    out.push(make("error", "Error al generar", "Revisá la inspección", cas.generated_at ?? evidenceAt ?? cas.created_at));
  } else if (cas.generated_at) {
    out.push(make("report", "Informe generado", "Paquete disponible", cas.generated_at));
    if (cas.status === "completed") {
      out.push(make("completed", "Inspección completada", device, cas.generated_at));
    }
  }

  return out;
}

/**
 * Actividad reciente derivada del historial (sin pedidos extra al backend):
 * los `limit` eventos más nuevos, en orden descendente. Pura y determinística:
 * los empates se resuelven por tipo de evento y después por id.
 */
export function deriveActivity(cases: readonly Case[], limit = 5): ActivityEvent[] {
  return cases
    .flatMap(eventsOf)
    .map(event => ({ event, time: timeOf(event.at) }))
    .filter(({ time }) => Number.isFinite(time))
    .sort((a, b) =>
      b.time - a.time
      || KIND_RANK[b.event.kind] - KIND_RANK[a.event.kind]
      || (a.event.id < b.event.id ? -1 : a.event.id > b.event.id ? 1 : 0))
    .slice(0, Math.max(0, limit))
    .map(({ event }) => event);
}
