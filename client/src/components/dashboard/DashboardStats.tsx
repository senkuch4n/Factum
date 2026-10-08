"use client";

import type { ReactNode } from "react";
import { motion, useReducedMotion } from "framer-motion";
import {
  Activity,
  ArrowDown,
  ArrowUp,
  CheckCircle2,
  Clock,
  Equal,
  FolderOpen,
  type LucideIcon,
} from "lucide-react";
import { cn } from "@/lib/utils";
import type { Case } from "@/types";

interface Stat {
  label: string;
  value: number;
  Icon: LucideIcon;
  /** Color del ícono (y del borde de su insignia) en las tarjetas de superficie. */
  tone: string;
  highlight?: boolean;
  /** Elemento a la derecha del número, centrado con él (anillo de Completadas). */
  aside?: ReactNode;
  footer: ReactNode;
}

function isSameMonth(iso: string, year: number, month: number): boolean {
  const d = new Date(iso);
  return d.getFullYear() === year && d.getMonth() === month;
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

/**
 * Estadísticas del historial. Sin `aria-live` propio: el conteo de
 * `CaseHistory` ya anuncia los cambios.
 */
export function DashboardStats({ cases }: { cases: Case[] }) {
  const now = new Date();
  // Date normaliza el mes -1: en enero da diciembre del año anterior.
  const prev = new Date(now.getFullYear(), now.getMonth() - 1, 1);

  const thisMonth = cases.filter(c => isSameMonth(c.created_at, now.getFullYear(), now.getMonth())).length;
  const lastMonth = cases.filter(c => isSameMonth(c.created_at, prev.getFullYear(), prev.getMonth())).length;
  const completed = cases.filter(c => c.status === "completed").length;
  const pending   = cases.length - completed;
  const completedPct = cases.length > 0 ? Math.round((completed / cases.length) * 100) : 0;

  const delta = thisMonth - lastMonth;
  const DeltaIcon = delta > 0 ? ArrowUp : delta < 0 ? ArrowDown : Equal;
  const deltaTone =
    delta > 0 ? "bg-fx-success-soft text-fx-success"
    : delta < 0 ? "bg-fx-warning-soft text-fx-warning"
    : "bg-fx-surface-3 text-fx-text-2";
  // U+2212 (signo menos) en lugar del guion, para que no se lea como raya.
  const deltaLabel = delta > 0 ? `+${delta}` : delta < 0 ? `−${Math.abs(delta)}` : "0";

  const chip = "inline-flex shrink-0 items-center gap-0.5 rounded-fx-pill px-1.5 text-xs font-semibold leading-5 tabular-nums";

  const stats: Stat[] = [
    {
      label: "Total",
      value: cases.length,
      Icon: FolderOpen,
      tone: "",
      highlight: true,
      footer: (
        <>
          <span className={cn(chip, ON_HIGHLIGHT_CHIP)}>+{thisMonth}</span>
          <span className="truncate">este mes</span>
        </>
      ),
    },
    {
      label: "Este mes",
      value: thisMonth,
      Icon: Clock,
      tone: "text-fx-accent-text",
      footer: (
        <>
          <span className={cn(chip, deltaTone)}>
            <DeltaIcon className="h-3 w-3" aria-hidden="true" />
            {deltaLabel}
          </span>
          <span className="truncate">vs. mes anterior</span>
        </>
      ),
    },
    {
      label: "Completadas",
      value: completed,
      Icon: CheckCircle2,
      tone: "text-fx-success",
      aside: <ProgressRing pct={completedPct} />,
      footer: (
        <span className="truncate">
          <span className="sr-only">{completedPct}% </span>
          del total
        </span>
      ),
    },
    {
      label: "En proceso",
      value: pending,
      Icon: Activity,
      tone: "text-fx-warning",
      footer: <span className="truncate">Pendientes de cierre</span>,
    },
  ];

  return (
    <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4 sm:gap-4">
      {stats.map(({ label, value, Icon, tone, highlight, aside, footer }) => (
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
