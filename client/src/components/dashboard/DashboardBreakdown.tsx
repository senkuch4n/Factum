"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import dynamic from "next/dynamic";
import { Calendar } from "primereact/calendar";
import { Button } from "primereact/button";
import { BarChart3, CalendarRange, RotateCcw } from "lucide-react";
import { api, type BreakdownDimension, type CaseBreakdown } from "@/lib/api";
import { cn } from "@/lib/utils";
import { FOCUS_RING } from "@/lib/prime/pt/shared";
import { FxBanner } from "@/components/feedback/FxBanner";

// Recharts fuera del bundle inicial del dashboard (patrón de ReportStep.tsx).
const BreakdownBarsChart = dynamic(
  () => import("@/components/dashboard/BreakdownBarsChart").then((m) => m.BreakdownBarsChart),
  {
    ssr: false,
    loading: () => <div aria-hidden className="h-40 rounded-fx-lg bg-fx-surface-2 motion-safe:animate-pulse" />,
  },
);

const DIMENSIONS: { value: BreakdownDimension; label: string }[] = [
  { value: "platform", label: "Tipo de dispositivo" },
  { value: "status", label: "Estado" },
  { value: "caratula", label: "Carátula" },
  { value: "ambito_causa", label: "Ámbito" },
];

/** "YYYY-MM-DD" <-> Date local (mismo criterio que CaseHistory: evita el corrimiento UTC). */
function toYMD(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}
function fromYMD(s: string): Date {
  const [y, m, d] = s.split("-").map(Number);
  return new Date(y, m - 1, d);
}

/** Porcentaje entero con guarda de división por cero. */
function pct(count: number, total: number): number {
  return total > 0 ? Math.round((count / total) * 100) : 0;
}

export function DashboardBreakdown() {
  const [dimension, setDimension] = useState<BreakdownDimension>("platform");
  // Filtro de fechas propio, independiente del historial (D3-A).
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  const [data, setData] = useState<CaseBreakdown | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  // Fuerza un refetch al reintentar sin cambiar los filtros.
  const [reloadKey, setReloadKey] = useState(0);

  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    const ctrl = new AbortController();
    abortRef.current?.abort();
    abortRef.current = ctrl;

    setLoading(true);
    setError(false);

    api.caseBreakdown(dimension, from || undefined, to || undefined, ctrl.signal)
      .then((res) => {
        if (ctrl.signal.aborted) return;
        setData(res);
      })
      .catch((e: unknown) => {
        // Un abort (cambió el filtro antes de responder) no es un error visible;
        // cualquier otra cosa (corte de red → ApiError status 0, o 4xx/5xx) sí.
        if (ctrl.signal.aborted || (e instanceof DOMException && e.name === "AbortError")) return;
        setError(true);
        setData(null);
      })
      .finally(() => {
        if (!ctrl.signal.aborted) setLoading(false);
      });

    return () => ctrl.abort();
  }, [dimension, from, to, reloadKey]);

  const retry = useCallback(() => setReloadKey((k) => k + 1), []);

  const range: (Date | null)[] | null = from || to
    ? [from ? fromYMD(from) : null, to ? fromYMD(to) : null]
    : null;

  const total = data?.total ?? 0;
  const buckets = data?.buckets ?? [];
  const activeLabel = DIMENSIONS.find((d) => d.value === dimension)?.label ?? "";

  return (
    <section aria-labelledby="breakdown-title" className="space-y-4">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <h2
          id="breakdown-title"
          className="flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2"
        >
          <BarChart3 className="h-3.5 w-3.5" aria-hidden="true" />
          Desglose
        </h2>

        <div className="w-full sm:w-auto">
          <label htmlFor="breakdown-date-range" className="sr-only">
            Filtrar el desglose por fecha de creación
          </label>
          <Calendar
            inputId="breakdown-date-range"
            selectionMode="range"
            value={range}
            onChange={(e) => {
              const [f, t] = e.value ?? [];
              setFrom(f ? toYMD(f) : "");
              setTo(t ? toYMD(t) : "");
            }}
            readOnlyInput
            showIcon
            icon={<CalendarRange className="h-4 w-4" aria-hidden="true" />}
            showButtonBar
            hideOnRangeSelection
            dateFormat="dd/mm/yy"
            placeholder="Desde – hasta"
            className="w-full sm:w-auto"
          />
        </div>
      </div>

      {/* Selector de dimensión: botones con aria-pressed; la activa se marca con color + peso, no solo color. */}
      <div
        role="group"
        aria-label="Dimensión del desglose"
        className="flex flex-wrap gap-1.5"
      >
        {DIMENSIONS.map((d) => {
          const active = d.value === dimension;
          return (
            <button
              key={d.value}
              type="button"
              aria-pressed={active}
              onClick={() => setDimension(d.value)}
              className={cn(
                "min-h-11 rounded-fx-pill border px-3.5 text-fx-body-sm font-medium transition-colors duration-fx-fast ease-fx",
                FOCUS_RING,
                active
                  ? "border-transparent bg-fx-accent text-fx-on-accent shadow-fx-1"
                  : "border-fx-border bg-fx-surface-1 text-fx-text-2 hover:bg-fx-surface-2 hover:text-fx-text",
              )}
            >
              {d.label}
            </button>
          );
        })}
      </div>

      {/* Anuncio del cambio de desglose para lectores de pantalla (criterio Gherkin). */}
      <p aria-live="polite" className="sr-only">
        {loading
          ? `Cargando desglose por ${activeLabel.toLowerCase()}…`
          : error
            ? "No se pudo cargar el desglose."
            : `Desglose por ${activeLabel.toLowerCase()}: ${total} ${total === 1 ? "caso" : "casos"}.`}
      </p>

      <div className="fx-card rounded-fx-xl p-4 sm:p-5">
        {loading ? (
          <div className="space-y-3" aria-busy="true" aria-label="Cargando desglose…">
            {[0, 1, 2].map((i) => (
              <div
                key={i}
                className="h-8 rounded-fx-md bg-fx-surface-2 motion-safe:animate-pulse"
                style={{ width: `${90 - i * 18}%` }}
              />
            ))}
          </div>
        ) : error ? (
          <FxBanner
            tone="error"
            icon={<BarChart3 className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}
          >
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span>No se pudo cargar el desglose.</span>
              <Button
                text
                size="small"
                icon={<RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />}
                label="Reintentar"
                onClick={retry}
                className="min-h-11"
              />
            </div>
          </FxBanner>
        ) : total === 0 ? (
          <p className="py-6 text-center text-fx-body-sm text-fx-text-2">
            Sin datos para mostrar en el rango seleccionado.
          </p>
        ) : (
          <div className="space-y-5">
            {/* Gráfico (refuerzo visual, aria-hidden) */}
            <BreakdownBarsChart dimension={dimension} buckets={buckets} />

            {/* Tabla accesible: la representación textual que manda (label → cantidad → %). */}
            <table className="w-full border-collapse text-fx-body-sm">
              <caption className="sr-only">
                Desglose por {activeLabel.toLowerCase()} — {total} {total === 1 ? "caso" : "casos"} en total
              </caption>
              <thead>
                <tr className="border-b border-fx-border text-left text-xs uppercase tracking-wide text-fx-text-3">
                  <th scope="col" className="py-1.5 pr-2 font-semibold">{activeLabel}</th>
                  <th scope="col" className="py-1.5 px-2 text-right font-semibold">Casos</th>
                  <th scope="col" className="py-1.5 pl-2 text-right font-semibold">%</th>
                </tr>
              </thead>
              <tbody>
                {buckets.map((b) => {
                  const p = pct(b.count, total);
                  return (
                    <tr key={b.key} className="border-b border-fx-border/60 last:border-0">
                      <th scope="row" className="py-2 pr-2 font-normal text-fx-text">
                        {b.label}
                      </th>
                      <td className="py-2 px-2 text-right tabular-nums text-fx-text">{b.count}</td>
                      <td className="py-2 pl-2 text-right tabular-nums text-fx-text-2">{p}%</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </section>
  );
}
