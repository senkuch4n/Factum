"use client";

import { ProgressBar } from "primereact/progressbar";
import { Check, ChevronRight } from "lucide-react";
import { cn } from "@/lib/utils";

export interface Step { id: number; label: string; sublabel: string; }

interface Props {
  steps: Step[];
  current: number;
  /** Pasos completados con id < minJumpable no se pueden re-visitar (evita, p. ej., duplicar
   *  un caso ya creado si se retrocede a "Dispositivo" o "Causa" después de generar). */
  minJumpable?: number;
  onSelect?: (id: number) => void;
}

function progressOf(current: number, total: number) {
  const doneCount = Math.max(0, Math.min(current - 1, total));
  return { doneCount, pct: (doneCount / total) * 100 };
}

function StepProgress({ doneCount, total, pct }: { doneCount: number; total: number; pct: number }) {
  return (
    <ProgressBar
      value={pct}
      showValue={false}
      aria-label="Progreso de la inspección"
      aria-valuetext={`${doneCount} de ${total} pasos completados`}
    />
  );
}

/**
 * Marcador del paso, distinguible sin color (por forma):
 * hecho = círculo lleno con check; activo = anillo con punto; pendiente = círculo vacío.
 */
function Marker({ state }: { state: "done" | "active" | "pending" }) {
  const base = "flex h-5 w-5 shrink-0 items-center justify-center rounded-full transition-colors duration-fx-fast ease-fx";
  if (state === "done") {
    return (
      <span aria-hidden="true" className={cn(base, "bg-fx-text text-fx-bg")}>
        <Check className="h-3 w-3" strokeWidth={3} />
      </span>
    );
  }
  if (state === "active") {
    return (
      <span aria-hidden="true" className={cn(base, "border-2 border-fx-accent")}>
        <span className="h-2 w-2 rounded-full bg-fx-accent" />
      </span>
    );
  }
  return <span aria-hidden="true" className={cn(base, "border border-fx-border-strong")} />;
}

/** Riel vertical de pasos (md+). Solo los pasos hechos con id >= minJumpable son botones. */
export function StepIndicator({ steps, current, minJumpable = 1, onSelect }: Props) {
  const total = steps.length;
  const { doneCount, pct } = progressOf(current, total);

  return (
    <nav aria-label="Pasos de la inspección" className="flex flex-col gap-4">
      <div className="space-y-2 px-2">
        <p className="m-0 flex items-baseline justify-between text-xs text-fx-text-2">
          <span className="font-semibold text-fx-text">Paso {current} de {total}</span>
          <span className="tabular-nums">{doneCount}/{total} completados</span>
        </p>
        <StepProgress doneCount={doneCount} total={total} pct={pct} />
      </div>

      <ol className="m-0 flex list-none flex-col gap-0.5 p-0">
        {steps.map(step => {
          const done = current > step.id;
          const active = current === step.id;
          const state = done ? "done" : active ? "active" : "pending";
          const jumpable = done && step.id >= minJumpable && !!onSelect;

          const content = (
            <>
              <Marker state={state} />
              <span className="min-w-0 flex-1">
                <span
                  className={cn(
                    "block text-fx-body-sm font-semibold leading-tight",
                    state === "pending" ? "text-fx-text-3" : "text-fx-text",
                  )}
                >
                  {step.label}
                  <span className="sr-only">
                    {done ? " (completado)" : active ? " (paso actual)" : " (pendiente)"}
                  </span>
                </span>
                <span className="mt-0.5 block text-xs leading-tight text-fx-text-3">{step.sublabel}</span>
              </span>
            </>
          );

          const row = cn(
            "flex w-full min-h-11 items-center gap-3 rounded-fx-md px-2 py-2.5 text-left",
            active && "bg-fx-surface-2",
          );

          return (
            <li key={step.id}>
              {jumpable ? (
                <button
                  type="button"
                  onClick={() => onSelect?.(step.id)}
                  className={cn(
                    row,
                    "group cursor-pointer border-0 bg-transparent hover:bg-fx-surface-3 transition-colors duration-fx-fast ease-fx fx-focus-ring",
                  )}
                >
                  {content}
                  <ChevronRight
                    className="h-3.5 w-3.5 shrink-0 text-fx-text-3 opacity-0 transition-opacity duration-fx-fast group-hover:opacity-100 group-focus-visible:opacity-100"
                    aria-hidden="true"
                  />
                </button>
              ) : (
                <div className={row} aria-current={active ? "step" : undefined}>
                  {content}
                </div>
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}

/** Franja compacta (< md): "Paso N de 6 · <etiqueta>" + barra. No es navegable. */
export function StepIndicatorCompact({ steps, current, className }: {
  steps: Step[]; current: number; className?: string;
}) {
  const total = steps.length;
  const { doneCount, pct } = progressOf(current, total);
  return (
    <div className={cn("fx-card space-y-2 px-4 py-3", className)}>
      <p className="m-0 flex min-w-0 items-baseline gap-1.5 text-fx-body-sm">
        <span className="shrink-0 font-semibold text-fx-text">Paso {current} de {total}</span>
        <span aria-hidden="true" className="text-fx-text-3">·</span>
        <span className="truncate text-fx-text-2">{steps[current - 1]?.label}</span>
      </p>
      <StepProgress doneCount={doneCount} total={total} pct={pct} />
    </div>
  );
}
