"use client";

import { createContext, useContext, useEffect, useState, useCallback } from "react";
import { flushSync } from "react-dom";

type Theme = "light" | "dark";

const THEME_KEY = "ev-theme";

/**
 * Arranque en oscuro (D4 B): solo una preferencia guardada "light" lo cambia.
 * Ya no se consulta prefers-color-scheme. En SSR también devuelve "dark",
 * igual que el script anti-FOUC de layout.tsx.
 */
function getInitialTheme(): Theme {
  if (typeof window === "undefined") return "dark";
  try {
    return localStorage.getItem(THEME_KEY) === "light" ? "light" : "dark";
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
    localStorage.setItem(THEME_KEY, theme);
  } catch {
    /* localStorage no disponible: el cambio vale para esta sesión */
  }
}

/** Disables CSS transitions while the theme swaps, so hover/transition-colors don't stagger it. */
const SWITCHING_CLASS = "fx-theme-switching";

function prefersReducedMotion(): boolean {
  return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

/** Tema guardado (lo que escribió cualquier pestaña). */
function readStoredTheme(): Theme {
  return getInitialTheme();
}

/**
 * Applies the theme change with a compositor-only crossfade (View Transitions API).
 * Instant when `animate` is false (remote change from another tab), without
 * support, with prefers-reduced-motion or with the tab hidden (the browser
 * rejects transitions there with InvalidStateError). Every ViewTransition
 * promise is caught so nothing surfaces as an unhandled rejection
 * (wizard-pulido-ux DT14).
 */
function runThemeSwap(commit: () => void, opts: { animate: boolean }) {
  const root = document.documentElement;
  root.classList.add(SWITCHING_CLASS);

  const instant = () => {
    commit();
    // Force a style flush so the new colors land without transitions, then re-enable them.
    void window.getComputedStyle(root).color;
    requestAnimationFrame(() => root.classList.remove(SWITCHING_CLASS));
  };

  if (
    !opts.animate ||
    typeof document.startViewTransition !== "function" ||
    prefersReducedMotion() ||
    document.visibilityState !== "visible"
  ) {
    instant();
    return;
  }

  let committed = false;
  try {
    const transition = document.startViewTransition(() => {
      committed = true;
      commit();
    });
    transition.ready.catch(() => {});
    transition.updateCallbackDone.catch(() => {});
    transition.finished.catch(() => {}).finally(() => root.classList.remove(SWITCHING_CLASS));
  } catch {
    if (!committed) instant();
    else root.classList.remove(SWITCHING_CLASS);
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
    // Read from <html> rather than state so rapid clicks never act on a stale value.
    const next: Theme = document.documentElement.classList.contains("dark") ? "light" : "dark";
    persistTheme(next);
    runThemeSwap(() => {
      applyThemeClass(next);
      // Commit React synchronously so the "new" snapshot already has theme-dependent UI (logos, icons).
      flushSync(() => setTheme(next));
    }, { animate: true });
  }, []);

  // B5 (wizard-pulido-ux DT13): otra pestaña cambió el tema. `storage` solo
  // llega a las OTRAS pestañas (no hay bucle); visibilitychange/pageshow
  // resincronizan las que vuelven ocultas o del bfcache. Sin view transition
  // y sin persistir (el valor ya está en localStorage).
  useEffect(() => {
    const applyRemote = (next: Theme) => {
      if (document.documentElement.classList.contains("dark") === (next === "dark")) return;
      runThemeSwap(() => {
        applyThemeClass(next);
        flushSync(() => setTheme(next));
      }, { animate: false });
    };
    const onStorage = (e: StorageEvent) => {
      if (e.key !== THEME_KEY) return; // `clear()` (key null) se ignora
      try {
        if (e.storageArea !== window.localStorage) return;
      } catch {
        return;
      }
      applyRemote(e.newValue === "light" ? "light" : "dark");
    };
    const onVisibility = () => {
      if (document.visibilityState === "visible") applyRemote(readStoredTheme());
    };
    const onPageShow = (e: PageTransitionEvent) => {
      if (e.persisted) applyRemote(readStoredTheme());
    };
    window.addEventListener("storage", onStorage);
    document.addEventListener("visibilitychange", onVisibility);
    window.addEventListener("pageshow", onPageShow);
    return () => {
      window.removeEventListener("storage", onStorage);
      document.removeEventListener("visibilitychange", onVisibility);
      window.removeEventListener("pageshow", onPageShow);
    };
  }, []);

  return { theme, toggle, isDark: theme === "dark" };
}

/* ── Consumer hook ───────────────────────────────────────────────── */
export function useTheme(): ThemeContextValue {
  return useContext(ThemeContext);
}
