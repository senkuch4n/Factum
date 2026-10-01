"use client";

/**
 * Filtro de rango de fechas — adaptado de scrollxui (docs/components/calendar).
 *
 * El calendar original de scrollxui es un react-day-picker reimplementado (933
 * líneas: single/multiple/range, dropdowns de mes/año, multi-mes, week numbers,
 * ISO weeks, locale, RTL, modifiers, day components…) y cuelga de
 * `@scrollxui/button` con tokens Tailwind v4. Acá se conserva **el modelo de
 * interacción** que sirve para un filtro "Desde / Hasta":
 *   - un solo popover anclado al trigger (no empuja contenido)
 *   - navegación ‹ › de mes, sin dropdowns
 *   - selección de rango con preview al pasar el mouse
 *   - Escape / click afuera para cerrar, "Limpiar" para resetear
 * reconstruido con los tokens de factum y solo las props que usa el historial.
 */

import { useEffect, useMemo, useRef, useState } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { CalendarRange, ChevronLeft, ChevronRight, X } from "lucide-react";
import { cn } from "@/lib/utils";

export interface DateRange {
  from?: Date;
  to?: Date;
}

interface Props {
  value: DateRange;
  onChange: (range: DateRange) => void;
  className?: string;
}

const WEEKDAYS = ["lu", "ma", "mi", "ju", "vi", "sá", "do"]; // semana empieza lunes
const fmtDay = new Intl.DateTimeFormat("es-ES", { day: "numeric", month: "short" });
const fmtMonth = new Intl.DateTimeFormat("es-ES", { month: "long", year: "numeric" });

const sameDay = (a?: Date, b?: Date) =>
  !!a && !!b && a.toDateString() === b.toDateString();
const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate());
const addMonths = (d: Date, n: number) => new Date(d.getFullYear(), d.getMonth() + n, 1);

/** Grilla de 6 semanas (42 celdas) con días de relleno del mes anterior/siguiente. */
function monthGrid(view: Date): { date: Date; outside: boolean }[] {
  const year = view.getFullYear();
  const month = view.getMonth();
  const firstDow = (new Date(year, month, 1).getDay() + 6) % 7; // 0 = lunes
  const start = new Date(year, month, 1 - firstDow);
  return Array.from({ length: 42 }, (_, i) => {
    const date = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i);
    return { date, outside: date.getMonth() !== month };
  });
}

function triggerLabel(range: DateRange): string {
  if (!range.from && !range.to) return "Fecha";
  const y = (d: Date) => d.getFullYear();
  if (range.from && range.to) {
    const sameYear = y(range.from) === y(range.to);
    return `${fmtDay.format(range.from)}${sameYear ? "" : " " + y(range.from)} – ${fmtDay.format(range.to)} ${y(range.to)}`;
  }
  const d = (range.from ?? range.to)!;
  return `${range.from ? "desde" : "hasta"} ${fmtDay.format(d)} ${y(d)}`;
}

export function DateRangeCalendar({ value, onChange, className }: Props) {
  const [open, setOpen] = useState(false);
  const [view, setView] = useState<Date>(() => startOfDay(value.from ?? value.to ?? new Date()));
  const [hovered, setHovered] = useState<Date | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);

  const active = !!value.from || !!value.to;

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") setOpen(false); };
    const onDown = (e: MouseEvent) => {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("keydown", onKey);
    document.addEventListener("mousedown", onDown);
    return () => {
      document.removeEventListener("keydown", onKey);
      document.removeEventListener("mousedown", onDown);
    };
  }, [open]);

  // Al abrir, encuadra el mes del rango ya elegido.
  useEffect(() => {
    if (open) setView(startOfDay(value.from ?? value.to ?? new Date()));
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const grid = useMemo(() => monthGrid(view), [view]);

  function pick(date: Date) {
    const d = startOfDay(date);
    const { from, to } = value;
    if (!from || (from && to)) {
      onChange({ from: d, to: undefined });        // 1er click / reinicio
    } else if (sameDay(d, from)) {
      onChange({ from: undefined, to: undefined }); // click en el mismo → limpia
    } else if (d < from) {
      onChange({ from: d, to: from });             // eligió antes del inicio
    } else {
      onChange({ from, to: d });                   // cierra el rango
    }
  }

  function inRange(d: Date) {
    const { from, to } = value;
    if (from && to) return d > from && d < to;
    if (from && !to && hovered) {
      const lo = from < hovered ? from : hovered;
      const hi = from < hovered ? hovered : from;
      return d > lo && d < hi;
    }
    return false;
  }

  const today = startOfDay(new Date());

  return (
    <div ref={rootRef} className={cn("relative", className)}>
      <motion.button
        type="button"
        onClick={() => setOpen((o) => !o)}
        whileTap={{ scale: 0.96 }}
        aria-haspopup="dialog"
        aria-expanded={open}
        className="btn-sm flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-medium transition"
        style={{
          background: open || active ? "rgba(45,212,191,0.1)" : "var(--bg-elevated)",
          border: `1px solid ${open || active ? "rgba(45,212,191,0.3)" : "var(--border)"}`,
          color: open || active ? undefined : "var(--text-secondary)",
        }}
      >
        <CalendarRange className={cn("w-3.5 h-3.5", (open || active) && "text-teal-500 dark:text-teal-400")} aria-hidden="true" />
        <span className={cn((open || active) && "text-teal-600 dark:text-teal-300")}>{triggerLabel(value)}</span>
        {active && (
          <span
            role="button"
            tabIndex={0}
            aria-label="Limpiar filtro de fecha"
            onClick={(e) => { e.stopPropagation(); onChange({ from: undefined, to: undefined }); }}
            onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.stopPropagation(); onChange({ from: undefined, to: undefined }); } }}
            className="ml-0.5 -mr-1 flex h-4 w-4 items-center justify-center rounded-full hover:bg-teal-500/20"
          >
            <X className="h-3 w-3" />
          </span>
        )}
      </motion.button>

      <AnimatePresence>
        {open && (
          <motion.div
            role="dialog"
            aria-label="Elegir rango de fechas"
            initial={{ opacity: 0, y: -6, scale: 0.98 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: -6, scale: 0.98 }}
            transition={{ duration: 0.16, ease: [0.22, 1, 0.36, 1] }}
            className="absolute left-0 top-[calc(100%+6px)] z-30 w-[272px] rounded-xl p-3"
            style={{ background: "var(--bg-surface)", border: "1px solid var(--border-md)", boxShadow: "0 16px 48px rgba(0,0,0,0.18)" }}
          >
            {/* Cabecera: mes + navegación */}
            <div className="mb-2 flex items-center justify-between">
              <button
                type="button"
                onClick={() => setView((v) => addMonths(v, -1))}
                aria-label="Mes anterior"
                className="flex h-7 w-7 items-center justify-center rounded-lg transition-colors hover:bg-[var(--bg-elevated)]"
                style={{ color: "var(--text-secondary)" }}
              >
                <ChevronLeft className="h-4 w-4" />
              </button>
              <span className="text-[13px] font-semibold capitalize" style={{ color: "var(--text-primary)" }}>
                {fmtMonth.format(view)}
              </span>
              <button
                type="button"
                onClick={() => setView((v) => addMonths(v, 1))}
                aria-label="Mes siguiente"
                className="flex h-7 w-7 items-center justify-center rounded-lg transition-colors hover:bg-[var(--bg-elevated)]"
                style={{ color: "var(--text-secondary)" }}
              >
                <ChevronRight className="h-4 w-4" />
              </button>
            </div>

            {/* Nombres de día */}
            <div className="grid grid-cols-7 gap-0.5">
              {WEEKDAYS.map((d) => (
                <div key={d} className="flex h-7 items-center justify-center text-[10px] font-medium uppercase" style={{ color: "var(--text-muted)" }}>
                  {d}
                </div>
              ))}
            </div>

            {/* Días */}
            <div className="grid grid-cols-7 gap-0.5" onMouseLeave={() => setHovered(null)}>
              {grid.map(({ date, outside }, i) => {
                const isFrom = sameDay(date, value.from);
                const isTo = sameDay(date, value.to);
                const isEndpoint = isFrom || isTo;
                const mid = inRange(date);
                const isToday = sameDay(date, today);

                return (
                  <button
                    key={i}
                    type="button"
                    onClick={() => pick(date)}
                    onMouseEnter={() => setHovered(date)}
                    aria-label={date.toLocaleDateString("es-ES", { weekday: "long", day: "numeric", month: "long", year: "numeric" })}
                    aria-pressed={isEndpoint}
                    className={cn(
                      "relative flex h-8 items-center justify-center text-[12px] tabular-nums transition-colors",
                      "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--blue-lg)]",
                      isEndpoint ? "rounded-lg font-semibold" : mid ? "rounded-none" : "rounded-lg",
                    )}
                    style={{
                      background: isEndpoint
                        ? "var(--text-primary)"
                        : mid
                          ? "rgba(45,212,191,0.14)"
                          : "transparent",
                      color: isEndpoint
                        ? "var(--bg-base)"
                        : outside
                          ? "var(--text-muted)"
                          : "var(--text-primary)",
                    }}
                    onMouseOver={(e) => { if (!isEndpoint && !mid) e.currentTarget.style.background = "var(--bg-elevated)"; }}
                    onMouseOut={(e) => { if (!isEndpoint && !mid) e.currentTarget.style.background = "transparent"; }}
                  >
                    {date.getDate()}
                    {isToday && !isEndpoint && (
                      <span className="absolute bottom-1 h-1 w-1 rounded-full" style={{ background: "var(--blue-lg)" }} aria-hidden="true" />
                    )}
                  </button>
                );
              })}
            </div>

            {/* Pie: resumen + limpiar */}
            <div className="mt-2 flex items-center justify-between border-t pt-2" style={{ borderColor: "var(--border)" }}>
              <span className="text-[11px]" style={{ color: "var(--text-muted)" }}>
                {value.from && value.to
                  ? `${fmtDay.format(value.from)} – ${fmtDay.format(value.to)}`
                  : value.from
                    ? "Elegí la fecha final"
                    : "Elegí la fecha inicial"}
              </span>
              {active && (
                <button
                  type="button"
                  onClick={() => onChange({ from: undefined, to: undefined })}
                  className="text-[11px] font-medium transition-colors hover:underline"
                  style={{ color: "var(--blue-lg)" }}
                >
                  Limpiar
                </button>
              )}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

export default DateRangeCalendar;
