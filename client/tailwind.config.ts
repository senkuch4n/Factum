import type { Config } from "tailwindcss";

export default {
  darkMode: "class",
  content: ["./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        // Sistema de diseño Factum (rediseno-base-primereact): todo cuelga de
        // los tokens --fx-* de globals.css. Son var() sin canales, así que no
        // aceptan el modificador de opacidad /xx: para eso están los *-soft.
        fx: {
          bg:                "var(--fx-bg)",
          "surface-1":       "var(--fx-surface-1)",
          "surface-2":       "var(--fx-surface-2)",
          "surface-3":       "var(--fx-surface-3)",
          "nav-bg":          "var(--fx-nav-bg)",
          overlay:           "var(--fx-overlay)",
          border:            "var(--fx-border)",
          "border-strong":   "var(--fx-border-strong)",
          text:              "var(--fx-text)",
          "text-2":          "var(--fx-text-2)",
          "text-3":          "var(--fx-text-3)",
          "text-disabled":   "var(--fx-text-disabled)",
          accent:            "var(--fx-accent)",
          "accent-hover":    "var(--fx-accent-hover)",
          "accent-active":   "var(--fx-accent-active)",
          "on-accent":       "var(--fx-on-accent)",
          "accent-text":     "var(--fx-accent-text)",
          "accent-soft":     "var(--fx-accent-soft)",
          focus:             "var(--fx-focus)",
          success:           "var(--fx-success)",
          "success-soft":    "var(--fx-success-soft)",
          warning:           "var(--fx-warning)",
          "warning-soft":    "var(--fx-warning-soft)",
          danger:            "var(--fx-danger)",
          "danger-fill":     "var(--fx-danger-fill)",
          "danger-fill-hover": "var(--fx-danger-fill-hover)",
          "on-danger":       "var(--fx-on-danger)",
          "danger-soft":     "var(--fx-danger-soft)",
          info:              "var(--fx-info)",
          "info-soft":       "var(--fx-info-soft)",
        },
      },
      // Color de `border` / `divide-*` sin color explícito: lo lee el preflight
      // (`theme('borderColor.DEFAULT')`) y `divideColor` lo hereda.
      borderColor: {
        DEFAULT: "var(--fx-border)",
      },
      fontFamily: {
        sans: ["var(--font-sans)", "system-ui", "sans-serif"],
        mono: ["var(--font-mono)", "ui-monospace", "monospace"],
      },
      fontSize: {
        "fx-display": ["clamp(2.25rem, 4vw + 1rem, 3.5rem)", { lineHeight: "1.05", fontWeight: "800", letterSpacing: "-0.03em" }],
        "fx-h1":      ["2.25rem",  { lineHeight: "1.1",  fontWeight: "800", letterSpacing: "-0.025em" }],
        "fx-h2":      ["1.75rem",  { lineHeight: "1.15", fontWeight: "700", letterSpacing: "-0.02em" }],
        "fx-h3":      ["1.25rem",  { lineHeight: "1.25", fontWeight: "700", letterSpacing: "-0.01em" }],
        "fx-body":    ["1rem",     { lineHeight: "1.5",  letterSpacing: "0" }],
        "fx-body-sm": ["0.875rem", { lineHeight: "1.45", letterSpacing: "0" }],
        "fx-label":   ["0.75rem",  { lineHeight: "1.2",  fontWeight: "600", letterSpacing: "0.06em" }],
      },
      boxShadow: {
        "fx-1": "var(--fx-shadow-1)",
        "fx-2": "var(--fx-shadow-2)",
        "fx-3": "var(--fx-shadow-3)",
      },
      transitionTimingFunction: {
        fx: "var(--fx-ease-out)",
      },
      transitionDuration: {
        "fx-fast": "var(--fx-dur-fast)",
        "fx-base": "var(--fx-dur-base)",
        "fx-slow": "var(--fx-dur-slow)",
      },
      zIndex: {
        "fx-statusline": "30",
        "fx-nav": "40",
      },
      // Radios del sistema de diseño, sobre los tokens --fx-radius-*.
      // `rounded-none` y `rounded-full` (círculos: avatares, puntos) salen del
      // default de Tailwind; `rounded-fx-pill` es para píldoras y badges.
      // Sin `rounded-[…]` literal en componentes: usá estas utilidades
      // (`rounded-fx-*` / `rounded-full` / `rounded-none`). Excepción
      // documentada: el chasis del teléfono en PhoneFrame.tsx (hardware, D1).
      borderRadius: {
        "fx-sm": "var(--fx-radius-sm)",
        "fx-md": "var(--fx-radius-md)",
        "fx-lg": "var(--fx-radius-lg)",
        "fx-xl": "var(--fx-radius-xl)",
        "fx-pill": "var(--fx-radius-pill)",
      },
    },
  },
} satisfies Config;
