"use client";

import {
  AlertTriangle,
  ArrowRight,
  CheckCircle2,
  Database,
  FileText,
  History,
  Smartphone,
  type LucideIcon,
} from "lucide-react";
import { cn } from "@/lib/utils";
import type { ActivityEvent, ActivityKind } from "@/lib/activity";

interface Props {
  events: ActivityEvent[];
  loading?: boolean;
  /** Lleva a la lista completa ("Mis inspecciones"). */
  onViewAll: () => void;
}

const KIND_STYLE: Record<ActivityKind, { Icon: LucideIcon; tone: string }> = {
  started:   { Icon: Smartphone,    tone: "bg-fx-accent-soft text-fx-accent-text" },
  evidence:  { Icon: Database,      tone: "bg-fx-surface-3 text-fx-text-2" },
  report:    { Icon: FileText,      tone: "bg-fx-info-soft text-fx-info" },
  completed: { Icon: CheckCircle2,  tone: "bg-fx-success-soft text-fx-success" },
  error:     { Icon: AlertTriangle, tone: "bg-fx-danger-soft text-fx-danger" },
};

const ICON_BUTTON =
  "inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-fx-md border border-fx-border text-fx-text-2 hover:bg-fx-surface-3 hover:text-fx-text transition-colors duration-fx-fast ease-fx fx-focus-ring";

function isSameDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

/** Hora si es de hoy ("12:41 a. m."); si no, la fecha corta ("6 oct", con año si no es el actual). */
function shortWhen(date: Date, now: Date): string {
  if (isSameDay(date, now)) {
    return date.toLocaleTimeString("es-AR", { hour: "numeric", minute: "2-digit", hour12: true });
  }
  return date.toLocaleDateString("es-AR", {
    day: "numeric",
    month: "short",
    ...(date.getFullYear() !== now.getFullYear() ? { year: "numeric" } : {}),
  });
}

function longWhen(date: Date): string {
  return date.toLocaleString("es-AR", { dateStyle: "long", timeStyle: "short" });
}

/** Tarjeta "Actividad reciente" del panel lateral del historial. */
export function RecentActivityCard({ events, loading = false, onViewAll }: Props) {
  const now = new Date();

  return (
    <section aria-labelledby="recent-activity-title" className="fx-card rounded-fx-xl">
      <div className="flex items-center justify-between gap-3 border-b border-fx-border px-5 py-4">
        <h2 id="recent-activity-title" className="text-lg font-semibold leading-tight text-fx-text">
          Actividad reciente
        </h2>
        <button type="button" onClick={onViewAll} aria-label="Ver todas las inspecciones" className={ICON_BUTTON}>
          <ArrowRight className="h-4 w-4" aria-hidden="true" />
        </button>
      </div>

      {loading && events.length === 0 ? (
        <ul aria-hidden="true" className="divide-y divide-fx-border px-5">
          {[0, 1, 2].map(i => (
            <li key={i} className="flex items-center gap-3 py-3">
              <span className="h-10 w-10 shrink-0 rounded-full bg-fx-surface-3 motion-safe:animate-pulse" />
              <span className="flex-1 space-y-1.5">
                <span className="block h-3 w-3/5 rounded bg-fx-surface-3 motion-safe:animate-pulse" />
                <span className="block h-2.5 w-4/5 rounded bg-fx-surface-3 motion-safe:animate-pulse" />
              </span>
            </li>
          ))}
        </ul>
      ) : events.length === 0 ? (
        <div className="flex flex-col items-center gap-2 px-5 py-8 text-center">
          <span className="flex h-10 w-10 items-center justify-center rounded-full bg-fx-surface-3 text-fx-text-3" aria-hidden="true">
            <History className="h-5 w-5" />
          </span>
          <p className="m-0 text-fx-body-sm font-medium text-fx-text">Sin actividad todavía</p>
          <p className="m-0 text-xs text-fx-text-3">Cuando inicies una inspección, vas a verla acá.</p>
        </div>
      ) : (
        <ul className="divide-y divide-fx-border px-5">
          {events.map(event => {
            const { Icon, tone } = KIND_STYLE[event.kind];
            const date = new Date(event.at);
            return (
              <li key={event.id} className="flex items-center gap-3 py-3">
                <span className={cn("flex h-10 w-10 shrink-0 items-center justify-center rounded-full", tone)} aria-hidden="true">
                  <Icon className="h-[18px] w-[18px]" />
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-fx-body-sm font-medium text-fx-text">{event.title}</span>
                  <span className="block truncate text-xs text-fx-text-3" title={event.subtitle}>{event.subtitle}</span>
                </span>
                <time
                  dateTime={event.at}
                  title={longWhen(date)}
                  className="shrink-0 self-start pt-0.5 text-xs tabular-nums text-fx-text-3"
                >
                  {shortWhen(date, now)}
                </time>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
