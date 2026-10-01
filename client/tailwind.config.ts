import type { Config } from "tailwindcss";

export default {
  darkMode: "class",
  content: ["./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        brand:      { DEFAULT: "#003366", light: "#0055a5" },
        // shadcn/tailwind.css compatibility aliases → our CSS vars
        border:     "var(--border)",
        background: "var(--bg-base)",
        foreground: "var(--text-primary)",
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
      // Escala de radios ceñida y nítida (impronta Vercel): esquinas
      // definidas en vez de la curva orgánica anterior. rounded-full queda
      // intacto para avatares/dots/indicadores circulares reales.
      borderRadius: {
        none: "0",
        sm: "5px",
        DEFAULT: "6px",
        md: "6px",
        lg: "8px",
        xl: "10px",
        "2xl": "12px",
        "3xl": "14px",
        full: "9999px",
        // Radios del sistema nuevo (no reemplazan la escala de arriba, que
        // siguen usando las páginas no migradas).
        "fx-sm": "var(--fx-radius-sm)",
        "fx-md": "var(--fx-radius-md)",
        "fx-lg": "var(--fx-radius-lg)",
        "fx-xl": "var(--fx-radius-xl)",
      },
    },
  },
} satisfies Config;
