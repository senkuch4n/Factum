"use client";

import { useMemo } from "react";
import { useReducedMotion } from "framer-motion";
import {
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { useChartTokens } from "./chart-theme";

interface MonthPoint {
  month: string;
  count: number;
}

/** "2025-10" → Date del día 1 de ese mes, en hora local (sin el corrimiento de UTC). */
function monthToDate(month: string): Date {
  const [y, m] = month.split("-").map(Number);
  return new Date(y, (m || 1) - 1, 1);
}

const MONTH_FMT = new Intl.DateTimeFormat("es-AR", { month: "short" });
const MONTH_YEAR_FMT = new Intl.DateTimeFormat("es-AR", { month: "short", year: "2-digit" });
const MONTH_LONG_FMT = new Intl.DateTimeFormat("es-AR", { month: "long", year: "numeric" });

/** Saca el punto final de los abreviados de es-AR ("ene." → "ene"). */
function clean(s: string): string {
  return s.replace(/\.$/, "");
}

/** Etiqueta del eje X: mes abreviado; agrega el año (2 díg.) cuando cambia respecto del anterior. */
function axisLabel(points: MonthPoint[], index: number): string {
  const d = monthToDate(points[index].month);
  const prev = index > 0 ? monthToDate(points[index - 1].month) : null;
  const showYear = !prev || prev.getFullYear() !== d.getFullYear();
  return clean((showYear ? MONTH_YEAR_FMT : MONTH_FMT).format(d));
}

interface TooltipPayload {
  active?: boolean;
  payload?: { payload: MonthPoint }[];
}

function ChartTooltip({ active, payload }: TooltipPayload) {
  const tokens = useChartTokens();
  if (!active || !payload?.length) return null;
  const point = payload[0].payload;
  const long = clean(MONTH_LONG_FMT.format(monthToDate(point.month)));
  return (
    <div
      className="rounded-fx-md border px-2.5 py-1.5 text-fx-body-sm shadow-fx-1"
      style={{ background: tokens.tooltipBg, borderColor: tokens.border, color: tokens.text }}
    >
      <p className="font-semibold capitalize">{long}</p>
      <p className="tabular-nums text-fx-text-2">
        {point.count} {point.count === 1 ? "caso" : "casos"}
      </p>
    </div>
  );
}

/**
 * Tendencia de casos creados por mes (12 meses). Barras verticales (Recharts),
 * una serie. El color sale de los tokens `--fx-*` (no de Tailwind, que Recharts
 * no lee). La alternativa textual es una tabla `sr-only` mes→cantidad; el
 * contenedor del gráfico va `aria-hidden` para que el lector no lea dos veces.
 */
export function CasesByMonthChart({ data }: { data: MonthPoint[] }) {
  const tokens = useChartTokens();
  const reduceMotion = useReducedMotion();

  const total = useMemo(() => data.reduce((s, d) => s + d.count, 0), [data]);
  const ticks = useMemo(() => data.map((_, i) => axisLabel(data, i)), [data]);

  // Resumen para el lector de pantalla: rango de meses + total.
  const firstLabel = data.length ? clean(MONTH_LONG_FMT.format(monthToDate(data[0].month))) : "";
  const lastLabel = data.length ? clean(MONTH_LONG_FMT.format(monthToDate(data[data.length - 1].month))) : "";
  const summary = `Casos creados por mes entre ${firstLabel} y ${lastLabel}: ${total} en total.`;

  return (
    <figure className="m-0">
      {/* Alternativa textual accesible: la tabla expone todos los datos del gráfico. */}
      <figcaption className="sr-only">{summary}</figcaption>
      <table className="sr-only">
        <caption>Casos creados por mes</caption>
        <thead>
          <tr>
            <th scope="col">Mes</th>
            <th scope="col">Casos</th>
          </tr>
        </thead>
        <tbody>
          {data.map((d) => (
            <tr key={d.month}>
              <th scope="row" className="capitalize">{clean(MONTH_LONG_FMT.format(monthToDate(d.month)))}</th>
              <td>{d.count}</td>
            </tr>
          ))}
        </tbody>
      </table>

      {/* El SVG es decorativo frente al lector (la tabla ya dice todo); sí es interactivo con mouse/foco. */}
      <div
        aria-hidden="true"
        className="h-64 w-full overflow-x-auto"
      >
        {/* Ancho mínimo para que, a 360 px, 12 barras no queden ilegibles: scroll horizontal del contenedor. */}
        <div className="h-full min-w-[32rem]">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={data} margin={{ top: 8, right: 8, bottom: 4, left: -16 }} barCategoryGap="22%">
              <CartesianGrid vertical={false} stroke={tokens.grid} strokeDasharray="3 3" />
              <XAxis
                dataKey="month"
                tickFormatter={(_v: string, i: number) => ticks[i] ?? ""}
                tick={{ fill: tokens.axis, fontSize: 12 }}
                tickLine={false}
                axisLine={{ stroke: tokens.grid }}
                interval="preserveStartEnd"
                minTickGap={4}
              />
              <YAxis
                allowDecimals={false}
                width={44}
                tick={{ fill: tokens.axis, fontSize: 12 }}
                tickLine={false}
                axisLine={false}
              />
              <Tooltip cursor={{ fill: tokens.surface }} content={<ChartTooltip />} />
              <Bar
                dataKey="count"
                name="Casos"
                fill={tokens.accent}
                radius={[4, 4, 0, 0]}
                maxBarSize={48}
                isAnimationActive={!reduceMotion}
                animationDuration={reduceMotion ? 0 : 420}
              />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>
    </figure>
  );
}
