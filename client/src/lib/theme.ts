"use client";

import { createContext, useContext, useEffect, useState, useCallback } from "react";

type Theme = "light" | "dark";

/**
 * Arranque en oscuro (D4 B): solo una preferencia guardada "light" lo cambia.
 * Ya no se consulta prefers-color-scheme. En SSR también devuelve "dark",
 * igual que el script anti-FOUC de layout.tsx.
 */
function getInitialTheme(): Theme {
  if (typeof window === "undefined") return "dark";
  try {
    return localStorage.getItem("ev-theme") === "light" ? "light" : "dark";
  } catch {
    return "dark";
  }
}

/** Sincroniza la clase de <html>. No persiste: eso lo hace solo toggle(). */
function applyThemeClass(theme: Theme) {
  document.documentElement.classList.toggle("dark", theme === "dark");
}

function persistTheme(theme: Theme) {
  try {
    localStorage.setItem("ev-theme", theme);
  } catch {
    /* localStorage no disponible: el cambio vale para esta sesión */
  }
}

/* ── Shared context ──────────────────────────────────────────────── */
interface ThemeContextValue {
  theme: Theme;
  toggle: () => void;
  isDark: boolean;
}

export const ThemeContext = createContext<ThemeContextValue>({
  theme: "dark",
  toggle: () => {},
  isDark: true,
});

/* ── Provider (mount once in the layout) ────────────────────────── */
export function useThemeProviderValue(): ThemeContextValue {
  // "dark" por defecto: coincide con el script anti-FOUC en el primer render,
  // así los consumidores que eligen logo según isDark no parpadean.
  const [theme, setTheme] = useState<Theme>("dark");

  useEffect(() => {
    const t = getInitialTheme();
    setTheme(t);
    applyThemeClass(t);
  }, []);

  const toggle = useCallback(() => {
    setTheme(prev => {
      const next = prev === "dark" ? "light" : "dark";
      applyThemeClass(next);
      persistTheme(next);
      return next;
    });
  }, []);

  return { theme, toggle, isDark: theme === "dark" };
}

/* ── Consumer hook ───────────────────────────────────────────────── */
export function useTheme(): ThemeContextValue {
  return useContext(ThemeContext);
}
