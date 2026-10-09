"use client";

import { useEffect, useState } from "react";
import { useTheme } from "@/lib/theme";

/**
 * Recharts no lee clases de Tailwind ni CSS vars: necesita colores resueltos
 * (`fill`/`stroke`). Este hook lee los tokens `--fx-*` del `<html>` con
 * `getComputedStyle` y los reevalúa cuando cambia el tema (claro/oscuro), así
 * las barras y los ejes mantienen el contraste AA en los dos modos.
 */
export interface ChartTokens {
  accent: string;
  accentActive: string;
  success: string;
  warning: string;
  danger: string;
  axis: string;
  grid: string;
  surface: string;
  border: string;
  text: string;
  tooltipBg: string;
}

function read(name: string, fallback: string): string {
  if (typeof window === "undefined") return fallback;
  const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return v || fallback;
}

function readTokens(): ChartTokens {
  return {
    accent: read("--fx-accent", "#2f6f12"),
    accentActive: read("--fx-accent-active", "#1f4d0b"),
    success: read("--fx-success", "#0b6b4f"),
    warning: read("--fx-warning", "#8a5a00"),
    danger: read("--fx-danger", "#b3262a"),
    // Ejes y grilla: texto secundario / borde, suficientes para leer sin saturar.
    axis: read("--fx-text-2", "#3d444c"),
    grid: read("--fx-border", "#d9dde1"),
    surface: read("--fx-surface-2", "#eef0f2"),
    border: read("--fx-border", "#d9dde1"),
    text: read("--fx-text", "#0e1013"),
    tooltipBg: read("--fx-surface-1", "#ffffff"),
  };
}

export function useChartTokens(): ChartTokens {
  const { isDark } = useTheme();
  const [tokens, setTokens] = useState<ChartTokens>(readTokens);

  // Releer cuando cambia el tema: `isDark` ya refleja el swap (flushSync en theme.ts).
  useEffect(() => {
    setTokens(readTokens());
  }, [isDark]);

  return tokens;
}
