"use client";

import { HardDrive, Network, Usb, type LucideIcon } from "lucide-react";
import { cn } from "@/lib/utils";
import type { Presentation, Tone } from "@/lib/extraction";
import type { DestinationKind } from "@/lib/agent";

/** Clases por tono (color de apoyo; el ícono y el texto llevan el significado). */
const TONE_CHIP: Record<Tone, string> = {
  running: "bg-fx-accent-soft text-fx-accent-text",
  success: "bg-fx-success-soft text-fx-success",
  warning: "bg-fx-warning-soft text-fx-warning",
  danger: "bg-fx-danger-soft text-fx-danger",
  neutral: "bg-fx-surface-2 text-fx-text-2",
};

/**
 * Chip de estado: ícono + texto + color de apoyo. El estado nunca se transmite
 * solo por color (ícono distinto y etiqueta en castellano por cada valor).
 */
export function StatusChip({ view, className }: { view: Presentation; className?: string }) {
  const { label, Icon, tone } = view;
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-fx-pill px-2.5 py-1 text-xs font-semibold",
        TONE_CHIP[tone],
        className,
      )}
    >
      <Icon
        className={cn("h-3.5 w-3.5 shrink-0", tone === "running" && "motion-safe:animate-spin")}
        aria-hidden="true"
      />
      {label}
    </span>
  );
}

/**
 * Barra de progreso determinada con `role="progressbar"` y `aria-valuenow`
 * (SDD §10.5). El ancho transiciona salvo con `prefers-reduced-motion`.
 */
export function ProgressBar({
  value, label, valueText,
}: { value: number; label: string; valueText?: string }) {
  const pct = Math.max(0, Math.min(100, Number.isFinite(value) ? value : 0));
  return (
    <div
      role="progressbar"
      aria-valuenow={Math.round(pct)}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-label={label}
      aria-valuetext={valueText}
      className="h-2 w-full overflow-hidden rounded-fx-pill bg-fx-surface-3"
    >
      <div
        className="h-full rounded-fx-pill bg-fx-accent transition-[width] duration-fx-base ease-fx motion-reduce:transition-none"
        style={{ width: `${pct}%` }}
      />
    </div>
  );
}

const DRIVE_ICON: Record<DestinationKind, LucideIcon> = {
  internal: HardDrive,
  removable: Usb,
  network: Network,
};

export function driveIcon(kind: DestinationKind): LucideIcon {
  return DRIVE_ICON[kind] ?? HardDrive;
}

export const DRIVE_KIND_LABEL: Record<DestinationKind, string> = {
  internal: "Disco interno",
  removable: "Disco extraíble",
  network: "Unidad de red",
};
