"use client";

import { Moon, Sun } from "lucide-react";
import { useTheme } from "@/lib/theme";
import { cn } from "@/lib/utils";

/**
 * Botón de tema para la navbar. `aria-pressed` refleja el modo oscuro y el
 * label dice a qué modo cambia. El ícono rota apenas al cambiar, salvo con
 * prefers-reduced-motion.
 */
export function ThemeSwitch({ className }: { className?: string }) {
  const { isDark, toggle } = useTheme();
  const label = isDark ? "Cambiar a modo claro" : "Cambiar a modo oscuro";

  return (
    <button
      type="button"
      onClick={toggle}
      aria-pressed={isDark}
      aria-label={label}
      title={label}
      className={cn(
        "inline-flex items-center justify-center w-8 h-8 shrink-0 rounded-fx-md",
        "text-fx-text-2 hover:text-fx-text hover:bg-fx-surface-3",
        "transition-colors duration-fx-fast ease-fx",
        "fx-focus-ring",
        className,
      )}
    >
      <span
        key={isDark ? "sun" : "moon"}
        aria-hidden="true"
        className="inline-flex motion-safe:animate-[fx-icon-in_200ms_cubic-bezier(.2,.8,.2,1)]"
      >
        {isDark ? <Sun className="w-4 h-4" /> : <Moon className="w-4 h-4" />}
      </span>
    </button>
  );
}
