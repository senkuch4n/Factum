"use client";

import { useCallback, useEffect, useState } from "react";
import { AlertCircle, HardDrive, History, Loader2, RefreshCw } from "lucide-react";
import type { Case, CaseEvent } from "@/lib/api";
import { api } from "@/lib/api";
import { cn } from "@/lib/utils";
import { formatDate, formatTime } from "@/lib/format";
import { FX_BUTTON_SECONDARY } from "@/lib/prime/pt/shared";
import {
  derivedMilestones, derivedTitle, eventFilename, eventIcon, eventTitle, eventTone,
  type DerivedMilestone,
} from "@/lib/case-events";

type LoadState =
  | { kind: "loading" }
  | { kind: "error" }
  | { kind: "ready"; events: CaseEvent[] };

/**
 * Línea de tiempo "Cadena de custodia" (trazabilidad-caso, HU5). Sección del
 * detalle del caso: carga `GET /api/cases/{id}/events` al montarse, en orden
 * cronológico ascendente. Para un caso previo a esta HU (sin eventos) deriva
 * los hitos del propio `Case` con un aviso (DT12). Un fallo de carga no bloquea
 * el resto del detalle: muestra un aviso discreto con "Reintentar".
 */
export function CaseTimeline({ cas }: { cas: Case }) {
  const [state, setState] = useState<LoadState>({ kind: "loading" });
  const headingId = `case-${cas.id}-activity`;

  const load = useCallback((signal?: { aborted: boolean }) => {
    setState({ kind: "loading" });
    api.listCaseEvents(cas.id)
      .then(({ events }) => { if (!signal?.aborted) setState({ kind: "ready", events }); })
      .catch(() => { if (!signal?.aborted) setState({ kind: "error" }); });
  }, [cas.id]);

  useEffect(() => {
    const signal = { aborted: false };
    load(signal);
    return () => { signal.aborted = true; };
  }, [load]);

  return (
    <section aria-labelledby={headingId} className="space-y-3.5">
      <h3 id={headingId} className="flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2">
        <History className="w-3.5 h-3.5" aria-hidden="true" /> Cadena de custodia
      </h3>

      {state.kind === "loading" && (
        <div role="status" className="flex items-center gap-2 py-2 text-fx-body-sm text-fx-text-2">
          <Loader2 className="h-4 w-4 shrink-0 motion-safe:animate-spin text-fx-text-3" aria-hidden="true" />
          Cargando la actividad del caso…
        </div>
      )}

      {state.kind === "error" && (
        <div role="alert" className="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-fx-md border border-fx-border bg-fx-surface-2 px-3.5 py-3">
          <p className="m-0 flex items-center gap-1.5 text-fx-body-sm text-fx-text-2">
            <AlertCircle className="h-4 w-4 shrink-0 text-fx-text-3" aria-hidden="true" />
            No se pudo cargar la actividad del caso.
          </p>
          <button
            type="button"
            onClick={() => load()}
            className={cn(FX_BUTTON_SECONDARY, "min-h-8 px-3 py-1.5 text-xs")}
          >
            <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" /> Reintentar
          </button>
        </div>
      )}

      {state.kind === "ready" && <Timeline cas={cas} events={state.events} />}
    </section>
  );
}

/**
 * Decide qué mostrar según haya o no eventos reales: con eventos, la lista
 * real; sin eventos, los hitos derivados (DT12) con el aviso. El aviso también
 * aparece si hay eventos pero ninguno de creación (caso anterior que solo
 * acumuló capturas) — heurística de la SDD.
 */
function Timeline({ cas, events }: { cas: Case; events: CaseEvent[] }) {
  const hasCreated = events.some(e => e.type === "case_created");
  const showDerived = events.length === 0;
  const showLegacyNotice = showDerived || !hasCreated;
  const derived = showDerived ? derivedMilestones(cas) : [];

  return (
    <div className="space-y-3">
      {showLegacyNotice && (
        <p className="m-0 text-xs text-fx-text-3">
          La actividad detallada arranca desde esta versión; de los casos anteriores solo se
          muestran los hitos que se pueden deducir.
        </p>
      )}

      <ol role="list" className="space-y-4 pl-1">
        {showDerived
          ? derived.map((m, i) => (
              <TimelineItem
                key={m.id}
                first={i === 0}
                last={i === derived.length - 1}
                tone="neutral"
                type={m.type}
              >
                <DerivedItemBody m={m} />
              </TimelineItem>
            ))
          : events.map((ev, i) => (
              <TimelineItem
                key={ev.id}
                first={i === 0}
                last={i === events.length - 1}
                tone={eventTone(ev.type)}
                type={ev.type}
              >
                <RealItemBody ev={ev} />
              </TimelineItem>
            ))}
      </ol>
    </div>
  );
}

/**
 * Un hito: el nodo del ícono (con el riel vertical que conecta los hitos) y el
 * cuerpo. El riel no sobresale del primero ni del último.
 */
function TimelineItem({
  children, first, last, tone, type,
}: {
  children: React.ReactNode;
  first: boolean;
  last: boolean;
  tone: "danger" | "neutral";
  type: string;
}) {
  const Icon = eventIcon(type);
  return (
    <li className="relative flex gap-3">
      <div className="relative flex shrink-0 flex-col items-center">
        {!first && <span aria-hidden="true" className="absolute bottom-full h-2 w-px bg-fx-border" />}
        {!last && <span aria-hidden="true" className="absolute top-7 h-[calc(100%-0.25rem)] w-px bg-fx-border" />}
        <span
          aria-hidden="true"
          className={cn(
            "flex h-7 w-7 items-center justify-center rounded-full border",
            tone === "danger"
              ? "border-fx-danger bg-fx-danger-soft text-fx-danger"
              : "border-fx-border bg-fx-surface-2 text-fx-text-3",
          )}
        >
          <Icon className="h-3.5 w-3.5" aria-hidden="true" />
        </span>
      </div>
      <div className="min-w-0 flex-1 pb-0.5">{children}</div>
    </li>
  );
}

function RealItemBody({ ev }: { ev: CaseEvent }) {
  const tone = eventTone(ev.type);
  const filename = eventFilename(ev);
  const reason = ev.type === "report_failed" ? ev.detail?.reason : undefined;

  return (
    <>
      <p className={cn("m-0 text-fx-body-sm font-medium", tone === "danger" ? "text-fx-danger" : "text-fx-text")}>
        {eventTitle(ev)}
      </p>
      {filename && (
        <p translate="no" className="m-0 mt-0.5 font-mono text-xs text-fx-text-2 break-all">{filename}</p>
      )}
      {reason && <p className="m-0 mt-0.5 text-xs text-fx-text-2 break-words">{reason}</p>}
      <Meta
        actorName={ev.actor_name}
        actorDni={ev.actor_dni}
        timestamp={ev.timestamp}
        hostname={ev.hostname ?? null}
        agentMode={ev.agent_mode ?? null}
      />
    </>
  );
}

function DerivedItemBody({ m }: { m: DerivedMilestone }) {
  return (
    <>
      <p className="m-0 text-fx-body-sm font-medium text-fx-text">{derivedTitle(m.type)}</p>
      <Meta actorName={m.actor_name} timestamp={m.timestamp} />
    </>
  );
}

/** Quién + cuándo (hora local; UTC exacto en `title`) + equipo si aplica. */
function Meta({
  actorName, actorDni, timestamp, hostname, agentMode,
}: {
  actorName: string;
  actorDni?: string;
  timestamp: string;
  hostname?: string | null;
  agentMode?: "installed" | "portable" | null;
}) {
  const utc = new Date(timestamp).toISOString();
  return (
    <p className="m-0 mt-1 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-xs text-fx-text-3">
      {actorName && <span title={actorDni ? `DNI ${actorDni}` : undefined}>{actorName}</span>}
      {actorName && <span aria-hidden="true">·</span>}
      <time dateTime={timestamp} title={utc}>
        {formatDate(timestamp)}, {formatTime(timestamp)}
      </time>
      {hostname && <HostChip hostname={hostname} mode={agentMode ?? null} />}
    </p>
  );
}

/**
 * Chip de equipo, mismo patrón que `EvidenceHostChip`: hostname + modo del
 * agente. Texto siempre visible (no depende del color).
 */
function HostChip({ hostname, mode }: { hostname: string; mode: "installed" | "portable" | null }) {
  const modeLabel = mode === "portable" ? " · portable" : mode === "installed" ? " · instalado" : "";
  return (
    <span className="inline-flex max-w-full items-center gap-1 rounded-fx-sm border border-fx-border bg-fx-surface-2 px-1.5 py-px text-[11px] font-medium text-fx-text-2">
      <HardDrive className="h-3 w-3 shrink-0" aria-hidden="true" />
      <span className="truncate" translate="no">{hostname}{modeLabel}</span>
    </span>
  );
}
