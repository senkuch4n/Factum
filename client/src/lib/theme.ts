"use client";

import { createContext, useContext, useEffect, useState, useCallback } from "react";

type Theme = "light" | "dark";

function getInitialTheme(): Theme {
  if (typeof window === "undefined") return "light";
  const stored = localStorage.getItem("ev-theme") as Theme | null;
  if (stored === "dark" || stored === "light") return stored;
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

function applyTheme(theme: Theme) {
  const html = document.documentElement;
  if (theme === "dark") {
    html.classList.add("dark");
  } else {
    html.classList.remove("dark");
  }
  localStorage.setItem("ev-theme", theme);
}

/* ── Shared context ──────────────────────────────────────────────── */
interface ThemeContextValue {
  theme: Theme;
  toggle: () => void;
  isDark: boolean;
}

export const ThemeContext = createContext<ThemeContextValue>({
  theme: "light",
  toggle: () => {},
  isDark: false,
});

/* ── Provider (mount once in the layout) ────────────────────────── */
export function useThemeProviderValue(): ThemeContextValue {
  const [theme, setTheme] = useState<Theme>("light");

  useEffect(() => {
    const t = getInitialTheme();
    setTheme(t);
    applyTheme(t);
  }, []);

  const toggle = useCallback(() => {
    setTheme(prev => {
      const next = prev === "dark" ? "light" : "dark";
      applyTheme(next);
      return next;
    });
  }, []);

  return { theme, toggle, isDark: theme === "dark" };
}

/* ── Consumer hook ───────────────────────────────────────────────── */
export function useTheme(): ThemeContextValue {
  return useContext(ThemeContext);
}
