"use client";

import { AnimatePresence, motion } from "framer-motion";
import { Check, ChevronRight } from "lucide-react";
import { cn } from "@/lib/utils";

export interface Step { id: number; label: string; sublabel: string; }

interface Props {
  steps: Step[];
  current: number;
  /** Pasos completados con id < minJumpable no se pueden re-visitar (evita, p. ej., duplicar
   *  un caso ya creado si se retrocede a "Dispositivo" o "Expediente" después de generar). */
  minJumpable?: number;
  onSelect?: (id: number) => void;
}

const EASE = [0.4, 0, 0.2, 1] as const;

/** Círculo relleno con check — estado "done". Usa el negro/teal de marca, no el verde
 *  del componente original de scrollxui (el design system de factum es monocromo). */
function FilledCheck() {
  return (
    <span
      className="flex h-[18px] w-[18px] shrink-0 items-center justify-center rounded-full"
      style={{ background: "var(--text-primary)" }}
    >
      <Check className="h-2.5 w-2.5" strokeWidth={3} style={{ color: "var(--bg-base)" }} aria-hidden="true" />
    </span>
  );
}

/** Círculo del paso activo — anillo teal con punto central. */
function ActiveDot() {
  return (
    <span
      className="flex h-[18px] w-[18px] shrink-0 items-center justify-center rounded-full border-2"
      style={{ borderColor: "var(--blue-lg)" }}
    >
      <span className="h-1.5 w-1.5 rounded-full" style={{ background: "var(--blue-lg)" }} />
    </span>
  );
}

/** Círculo vacío — estado "pending". */
function EmptyCircle() {
  return (
    <span
      className="h-[18px] w-[18px] shrink-0 rounded-full border"
      style={{ borderColor: "var(--border-md)" }}
    />
  );
}

export function StepIndicator({ steps, current, minJumpable = 1, onSelect }: Props) {
  const total = steps.length;
  const doneCount = Math.max(0, Math.min(current - 1, total));
  const pct = (doneCount / total) * 100;

  return (
    <nav className="flex flex-col gap-4">
      {/* Cabecera: barra de progreso + contador con dígito que rueda (patrón checklist-cell). */}
      <div className="flex items-center gap-3 px-2">
        <div
          className="h-1.5 flex-1 overflow-hidden rounded-full"
          style={{ background: "var(--bg-elevated)" }}
        >
          <motion.div
            className="h-full rounded-full"
            style={{ background: "var(--text-primary)" }}
            animate={{ width: `${pct}%` }}
            transition={{ duration: 0.6, ease: EASE }}
          />
        </div>
        <div
          className="flex items-center whitespace-nowrap text-[11px]"
          style={{ color: "var(--text-muted)" }}
        >
          <span className="relative flex h-4 items-center overflow-hidden" style={{ minWidth: 7 }}>
            <AnimatePresence mode="popLayout" initial={false}>
              <motion.span
                key={doneCount}
                initial={{ y: "100%", opacity: 0 }}
                animate={{ y: "0%", opacity: 1 }}
                exit={{ y: "-100%", opacity: 0 }}
                transition={{ duration: 0.25, ease: EASE }}
                className="absolute inset-0 flex items-center justify-end tabular-nums"
              >
                {doneCount}
              </motion.span>
            </AnimatePresence>
          </span>
          <span className="mx-0.5">/</span>
          <span>{total} completados</span>
        </div>
      </div>

      {/* Filas de pasos — planas, sin conector vertical, al estilo del componente de referencia. */}
      <div className="flex flex-col gap-0.5">
        {steps.map((step) => {
          const done     = current > step.id;
          const active   = current === step.id;
          const state    = done ? "done" : active ? "active" : "pending";
          const jumpable = done && step.id >= minJumpable && !!onSelect;

          return (
            <button
              key={step.id}
              type="button"
              disabled={!jumpable}
              onClick={() => jumpable && onSelect?.(step.id)}
              aria-current={active ? "step" : undefined}
              className={cn(
                "group relative flex items-center gap-2.5 rounded-lg px-2 py-2.5 text-left transition-colors",
                jumpable ? "cursor-pointer" : "cursor-default",
              )}
              style={{ background: active ? "var(--bg-elevated)" : "transparent" }}
              onMouseEnter={e => { if (jumpable) (e.currentTarget as HTMLButtonElement).style.background = "var(--bg-elevated)"; }}
              onMouseLeave={e => { (e.currentTarget as HTMLButtonElement).style.background = active ? "var(--bg-elevated)" : "transparent"; }}
            >
              <AnimatePresence mode="wait" initial={false}>
                <motion.span
                  key={state}
                  initial={{ scale: 0.4, opacity: 0 }}
                  animate={{ scale: 1, opacity: 1 }}
                  exit={{ scale: 0.4, opacity: 0 }}
                  transition={{ type: "spring", stiffness: 320, damping: 18, mass: 0.8 }}
                  className="flex"
                >
                  {state === "done" ? <FilledCheck /> : state === "active" ? <ActiveDot /> : <EmptyCircle />}
                </motion.span>
              </AnimatePresence>

              <span className="min-w-0 flex-1">
                <span
                  className="block text-[13px] font-medium leading-tight transition-colors"
                  style={{ color: state === "pending" ? "var(--text-muted)" : "var(--text-primary)" }}
                >
                  {step.label}
                </span>
                <span
                  className="mt-0.5 block text-[11px] leading-tight"
                  style={{ color: "var(--text-muted)" }}
                >
                  {step.sublabel}
                </span>
              </span>

              <ChevronRight
                className={cn(
                  "h-3.5 w-3.5 shrink-0 transition-opacity",
                  jumpable ? "opacity-0 group-hover:opacity-100" : "opacity-0",
                )}
                style={{ color: "var(--text-muted)" }}
                aria-hidden="true"
              />
            </button>
          );
        })}
      </div>
    </nav>
  );
}
