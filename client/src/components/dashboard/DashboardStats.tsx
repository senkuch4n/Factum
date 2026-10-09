"use client";

import type { ReactNode } from "react";
import { motion, useReducedMotion } from "framer-motion";
import {
  Activity,
  CheckCircle2,
  Clock,
  FolderOpen,
  type LucideIcon,
} from "lucide-react";
import { cn } from "@/lib/utils";
import type { CaseStats } from "@/types";

interface Stat {
  label: string;
  value: ReactNode;
  Icon: LucideIcon;
  /** Color del ícono (y del borde de su insignia) en las tarjetas de superficie. */
  tone: string;
  highlight?: boolean;
  /** Elemento a la derecha del número, centrado con él (anillo de Completados). */
  aside?: ReactNode;
  footer: ReactNode;
}

/* Tarjeta destacada: degradé de marca con brillo radial arriba a la derecha.
   Claro: verde de marca + texto on-accent (blanco). Oscuro: el acento claro
   (#7fd34e) no admite texto claro, así que se mezcla con la superficie para
   bajar la luminancia y mantener texto claro con contraste >= 4.5:1. */
const HIGHLIGHT_CARD = cn(
  "border-transparent text-fx-on-accent shadow-fx-2",
  "bg-[image:radial-gradient(120%_120%_at_100%_0%,color-mix(in_srgb,var(--fx-accent)_88%,white)_0%,transparent_55%),linear-gradient(135deg,var(--fx-accent)_0%,var(--fx-accent-active)_100%)]",
  "dark:text-fx-text",
  "dark:bg-[image:radial-gradient(120%_120%_at_100%_0%,color-mix(in_srgb,var(--fx-accent)_50%,var(--fx-surface-1))_0%,transparent_55%),linear-gradient(135deg,color-mix(in_srgb,var(--fx-accent)_45%,var(--fx-surface-1))_0%,color-mix(in_srgb,var(--fx-accent)_25%,var(--fx-surface-1))_100%)]",
);

/** Velo translúcido sobre el color de texto actual (insignias dentro de la tarjeta verde). */
const ON_HIGHLIGHT_CHIP = "bg-[color:color-mix(in_srgb,currentColor_16%,transparent)]";

// Unidades del viewBox: el tamaño real lo dan las clases (se escala por breakpoint).
const RING_SIZE = 48;
const RING_STROKE = 4.5;
const RING_RADIUS = (RING_SIZE - RING_STROKE) / 2;
const RING_CIRCUMFERENCE = 2 * Math.PI * RING_RADIUS;

/** Anillo de progreso decorativo: el valor accesible lo pone quien lo usa. */
function ProgressRing({ pct }: { pct: number }) {
  const reduceMotion = useReducedMotion();
  const offset = RING_CIRCUMFERENCE * (1 - pct / 100);

  return (
    <span className="relative inline-flex shrink-0" aria-hidden="true">
      <svg
        viewBox={`0 0 ${RING_SIZE} ${RING_SIZE}`}
        className="h-10 w-10 -rotate-90 md:h-11 md:w-11 lg:h-12 lg:w-12"
      >
        <circle
          cx={RING_SIZE / 2}
          cy={RING_SIZE / 2}
          r={RING_RADIUS}
          fill="none"
          strokeWidth={RING_STROKE}
          className="stroke-fx-surface-3"
        />
        {pct > 0 && (
          <motion.circle
            cx={RING_SIZE / 2}
            cy={RING_SIZE / 2}
            r={RING_RADIUS}
            fill="none"
            strokeWidth={RING_STROKE}
            strokeLinecap="round"
            strokeDasharray={RING_CIRCUMFERENCE}
            className="stroke-fx-success"
            initial={reduceMotion ? false : { strokeDashoffset: RING_CIRCUMFERENCE }}
            animate={{ strokeDashoffset: offset }}
            transition={reduceMotion ? { duration: 0 } : { duration: 0.8, ease: [0.2, 0.8, 0.2, 1] }}
          />
        )}
      </svg>
      <span className="absolute inset-0 flex items-center justify-center text-[10px] font-semibold tabular-nums text-fx-text lg:text-[11px]">
        {pct}%
      </span>
    </span>
  );
}

const CHIP = "inline-flex shrink-0 items-center gap-1 rounded-fx-pill px-1.5 text-xs font-semibold leading-5 tabular-nums";

/**
 * Chip de desglose por estado dentro de la tarjeta Total. Lleva un punto de
 * color + número + nombre del estado: la información nunca depende solo del color
 * (el estado va en texto oculto para lectores y como `title`; el número es visible).
 */
function StatusChip({ dotClass, count, label }: { dotClass: string; count: number; label: string }) {
  return (
    <span className={cn(CHIP, ON_HIGHLIGHT_CHIP)} title={label}>
      <span className={cn("h-1.5 w-1.5 rounded-full", dotClass)} aria-hidden="true" />
      {count}
      <span className="sr-only">{label}</span>
    </span>
  );
}

/** Días con 1 decimal y coma (es-AR) a partir de segundos; "—" si no hay dato. */
function formatCloseDays(avgSeconds: number | null): string {
  if (avgSeconds == null) return "—";
  const days = avgSeconds / 86_400;
  return days.toLocaleString("es-AR", { minimumFractionDigits: 1, maximumFractionDigits: 1 });
}

function SkeletonCard({ highlight }: { highlight?: boolean }) {
  return (
    <div
      className={cn(
        "fx-card relative flex min-w-0 flex-col gap-3 overflow-hidden rounded-fx-xl p-4",
        highlight && "border-transparent",
      )}
    >
      <div className="flex items-center justify-between gap-2">
        <span className="h-3.5 w-16 rounded-fx-sm bg-fx-surface-3 motion-safe:animate-pulse" />
        <span className="h-8 w-8 rounded-full bg-fx-surface-3 motion-safe:animate-pulse" />
      </div>
      <span className="mt-auto h-8 w-20 rounded-fx-sm bg-fx-surface-3 motion-safe:animate-pulse" />
      <span className="h-3 w-24 rounded-fx-sm bg-fx-surface-3 motion-safe:animate-pulse" />
    </div>
  );
}

/**
 * KPIs del historial a partir de `GET /api/cases/stats` (dashboard-kpis-tendencias).
 * Cuatro tarjetas: Total (destacada, con chips por estado), Completados (anillo
 * con la tasa de completitud), En proceso (draft + generating) y Tiempo promedio
 * de cierre (días, 1 decimal). Sin `aria-live` propio: el conteo de `CaseHistory`
 * ya anuncia los cambios del historial.
 */
export function DashboardStats({ stats, loading }: { stats: CaseStats | null; loading: boolean }) {
  if (loading || !stats) {
    return (
      <dl
        className="grid grid-cols-2 gap-3 sm:grid-cols-4 sm:gap-4"
        aria-busy="true"
        aria-label="Cargando estadísticas…"
      >
        <SkeletonCard highlight />
        <SkeletonCard />
        <SkeletonCard />
        <SkeletonCard />
      </dl>
    );
  }

  const { by_status, completion_rate, avg_close_seconds } = stats;
  const inProgress = by_status.draft + by_status.generating;
  const completedPct = Math.round(completion_rate * 100);
  const closeDays = formatCloseDays(avg_close_seconds);

  const statItems: Stat[] = [
    {
      label: "Total",
      value: stats.total,
      Icon: FolderOpen,
      tone: "",
      highlight: true,
      footer: (
        <div className="flex flex-wrap items-center gap-1">
          <StatusChip dotClass="bg-fx-success" count={by_status.completed} label="completados" />
          <StatusChip dotClass="bg-fx-warning" count={inProgress} label="en proceso" />
          {by_status.error > 0 && (
            <StatusChip dotClass="bg-fx-danger" count={by_status.error} label="con error" />
          )}
        </div>
      ),
    },
    {
      label: "Completados",
      value: by_status.completed,
      Icon: CheckCircle2,
      tone: "text-fx-success",
      aside: <ProgressRing pct={completedPct} />,
      footer: (
        <span className="truncate">
          {stats.total > 0 ? `${completedPct}% del total` : "Sin casos todavía"}
        </span>
      ),
    },
    {
      label: "En proceso",
      value: inProgress,
      Icon: Activity,
      tone: "text-fx-warning",
      footer: <span className="truncate">Borradores y en generación</span>,
    },
    {
      label: "Tiempo de cierre",
      value: (
        <span>
          {closeDays}
          {avg_close_seconds != null && (
            <span className="ml-1 text-base font-semibold text-fx-text-2">
              {closeDays === "1,0" ? "día" : "días"}
            </span>
          )}
        </span>
      ),
      Icon: Clock,
      tone: "text-fx-accent-text",
      footer: (
        <span className="truncate">
          {avg_close_seconds != null ? "Promedio de los completados" : "Aún no hay cierres"}
        </span>
      ),
    },
  ];

  return (
    <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4 sm:gap-4">
      {statItems.map(({ label, value, Icon, tone, highlight, aside, footer }) => (
        <div
          key={label}
          className={cn(
            "fx-card relative flex min-w-0 flex-col overflow-hidden rounded-fx-xl p-4",
            highlight && HIGHLIGHT_CARD,
          )}
        >
          {/* dt directo dentro del grupo (div hijo de dl): el ícono va adentro, oculto. */}
          <dt
            className={cn(
              "flex items-center justify-between gap-2 text-fx-body-sm",
              !highlight && "text-fx-text-2",
            )}
          >
            <span className="min-w-0 truncate">{label}</span>
            <span
              className={cn(
                "flex h-8 w-8 shrink-0 items-center justify-center rounded-full border",
                highlight
                  ? "border-[color:color-mix(in_srgb,currentColor_35%,transparent)]"
                  : cn("border-fx-border", tone),
              )}
              aria-hidden="true"
            >
              <Icon className="h-4 w-4" />
            </span>
          </dt>
          {/* mt-auto + alto mínimo de la fila del número = alto del anillo: las 4
              tarjetas quedan parejas tengan o no anillo. */}
          <dd className="mt-auto flex flex-col gap-1 pt-1.5">
            <span className="flex min-h-10 items-center justify-between gap-2 md:min-h-11 lg:min-h-12">
              <span
                className={cn(
                  "text-3xl font-extrabold leading-none tracking-tight tabular-nums lg:text-4xl",
                  !highlight && "text-fx-text",
                )}
              >
                {value}
              </span>
              {aside}
            </span>
            <span
              className={cn(
                "flex min-w-0 items-center gap-1.5 whitespace-nowrap text-xs",
                !highlight && "text-fx-text-3",
              )}
            >
              {footer}
            </span>
          </dd>
        </div>
      ))}
    </dl>
  );
}
