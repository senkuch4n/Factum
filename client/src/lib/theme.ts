"use client";

import { createContext, useContext, useEffect, useState, useCallback } from "react";
import { flushSync } from "react-dom";

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

/** Disables CSS transitions while the theme swaps, so hover/transition-colors don't stagger it. */
const SWITCHING_CLASS = "fx-theme-switching";

function prefersReducedMotion(): boolean {
  return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

/**
 * Applies the theme change with a compositor-only crossfade (View Transitions API).
 * Without support or with prefers-reduced-motion, the change is instant.
 */
function runThemeSwap(commit: () => void) {
  const root = document.documentElement;
  root.classList.add(SWITCHING_CLASS);

  if (typeof document.startViewTransition !== "function" || prefersReducedMotion()) {
    commit();
    // Force a style flush so the new colors land without transitions, then re-enable them.
    void window.getComputedStyle(root).color;
    requestAnimationFrame(() => root.classList.remove(SWITCHING_CLASS));
    return;
  }

  const transition = document.startViewTransition(commit);
  transition.finished.finally(() => root.classList.remove(SWITCHING_CLASS));
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
    // Read from <html> rather than state so rapid clicks never act on a stale value.
    const next: Theme = document.documentElement.classList.contains("dark") ? "light" : "dark";
    persistTheme(next);
    runThemeSwap(() => {
      applyThemeClass(next);
      // Commit React synchronously so the "new" snapshot already has theme-dependent UI (logos, icons).
      flushSync(() => setTheme(next));
    });
  }, []);

  return { theme, toggle, isDark: theme === "dark" };
}

/* ── Consumer hook ───────────────────────────────────────────────── */
export function useTheme(): ThemeContextValue {
  return useContext(ThemeContext);
}
