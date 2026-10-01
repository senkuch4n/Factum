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
      },
    },
  },
} satisfies Config;
