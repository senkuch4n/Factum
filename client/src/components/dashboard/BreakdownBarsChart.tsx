"use client";

import { useReducedMotion } from "framer-motion";
import {
  Bar,
  BarChart,
  Cell,
  ResponsiveContainer,
  XAxis,
  YAxis,
} from "recharts";
import type { BreakdownBucket, BreakdownDimension } from "@/types";
import { useChartTokens, type ChartTokens } from "./chart-theme";

/** Color de cada barra según la dimensión. `status` usa tokens semánticos; el resto, acento. */
function barColor(dimension: BreakdownDimension, key: string, tokens: ChartTokens): string {
  if (dimension === "status") {
    switch (key) {
      case "completed": return tokens.success;
      case "generating":
      case "draft": return tokens.warning;
      case "error": return tokens.danger;
      default: return tokens.accent;
    }
  }
  // platform / caratula / ambito_causa: acento, con los buckets agregados en tono atenuado.
  if (key === "__otras__" || key === "__sin_especificar__") return tokens.axis;
  return tokens.accent;
}

/**
 * Barras horizontales del desglose (Recharts, `layout="vertical"`). Es un refuerzo
 * visual: la tabla accesible de `DashboardBreakdown` ya expone label+cantidad+%,
 * así que este SVG va `aria-hidden`. El color sale de los tokens `--fx-*`.
 */
export function BreakdownBarsChart({
  dimension,
  buckets,
}: {
  dimension: BreakdownDimension;
  buckets: BreakdownBucket[];
}) {
  const tokens = useChartTokens();
  const reduceMotion = useReducedMotion();

  // Altura proporcional a la cantidad de barras, con piso para pocas categorías.
  const rowH = 34;
  const height = Math.max(buckets.length * rowH + 16, 120);

  return (
    <div aria-hidden="true" style={{ height }} className="w-full">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart
          layout="vertical"
          data={buckets}
          margin={{ top: 4, right: 12, bottom: 4, left: 4 }}
          barCategoryGap="28%"
        >
          <XAxis type="number" hide allowDecimals={false} />
          <YAxis
            type="category"
            dataKey="label"
            width={128}
            tick={{ fill: tokens.axis, fontSize: 12 }}
            tickLine={false}
            axisLine={false}
          />
          <Bar
            dataKey="count"
            radius={[0, 4, 4, 0]}
            isAnimationActive={!reduceMotion}
            animationDuration={reduceMotion ? 0 : 420}
          >
            {buckets.map((b) => (
              <Cell key={b.key} fill={barColor(dimension, b.key, tokens)} />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
