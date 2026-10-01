"use client";

import { Activity, CheckCircle2, Clock, FolderOpen, type LucideIcon } from "lucide-react";
import { cn } from "@/lib/utils";
import type { Case } from "@/types";

interface Stat {
  label: string;
  value: number;
  Icon: LucideIcon;
  iconClass: string;
  stripe: string;
  pct?: number;
}

/**
 * Estadísticas del historial. Sin `aria-live` propio: el conteo de
 * `CaseHistory` ya anuncia los cambios.
 */
export function DashboardStats({ cases }: { cases: Case[] }) {
  const now = new Date();
  const thisMonth = cases.filter(c => {
    const d = new Date(c.created_at);
    return d.getFullYear() === now.getFullYear() && d.getMonth() === now.getMonth();
  });
  const completed = cases.filter(c => c.status === "completed");
  const pending   = cases.filter(c => c.status !== "completed");
  const completedPct = cases.length > 0 ? Math.round((completed.length / cases.length) * 100) : 0;

  const stats: Stat[] = [
    { label: "Total",       value: cases.length,     Icon: FolderOpen,   iconClass: "text-fx-text-3",  stripe: "bg-fx-border-strong" },
    { label: "Este mes",    value: thisMonth.length, Icon: Clock,        iconClass: "text-fx-text-3",  stripe: "bg-fx-border-strong" },
    { label: "Completadas", value: completed.length, Icon: CheckCircle2, iconClass: "text-fx-success", stripe: "bg-fx-success", pct: cases.length > 0 ? completedPct : undefined },
    { label: "En proceso",  value: pending.length,   Icon: Activity,     iconClass: "text-fx-warning", stripe: "bg-fx-warning" },
  ];

  return (
    <dl className="grid grid-cols-2 sm:grid-cols-4 gap-3">
      {stats.map(({ label, value, Icon, iconClass, stripe, pct }) => (
        <div key={label} className="fx-card relative overflow-hidden p-4">
          <span className={cn("absolute inset-x-0 top-0 h-0.5", stripe)} aria-hidden="true" />
          {/* dt directo dentro del grupo (div hijo de dl): el ícono va adentro, oculto. */}
          <dt className="flex items-center justify-between gap-2 text-fx-label uppercase text-fx-text-2">
            {label}
            <Icon className={cn("h-4 w-4 shrink-0", iconClass)} aria-hidden="true" />
          </dt>
          <dd className="mt-2 flex items-baseline gap-1.5">
            <span className="text-fx-h1 tabular-nums text-fx-text">{value}</span>
            {pct !== undefined && <span className="text-xs text-fx-text-3 tabular-nums">{pct}%</span>}
          </dd>
        </div>
      ))}
    </dl>
  );
}
