"use client";

/**
 * Buscador con placeholders rotativos — adaptado de Aceternity UI
 * (placeholders-and-vanish-input).
 *
 * Del original se conserva SOLO la rotación de placeholders (con pausa cuando la
 * pestaña no está visible). Se descartó el efecto "vanish" de partículas en
 * canvas: este buscador filtra en vivo (no hay submit) y el disolverse en
 * partículas es justo la animación decorativa que el design system de factum
 * evita. Respeta `prefers-reduced-motion` → placeholder estático, sin ciclo.
 */

import { useEffect, useRef, useState } from "react";
import { AnimatePresence, motion, useReducedMotion } from "framer-motion";
import { Search, X } from "lucide-react";
import { cn } from "@/lib/utils";

interface Props {
  value: string;
  onChange: (value: string) => void;
  placeholders: string[];
  /** placeholder fijo para reduced-motion (default: el primero de la lista) */
  staticPlaceholder?: string;
  intervalMs?: number;
  className?: string;
  id?: string;
  ariaLabel?: string;
}

export function CyclingPlaceholderInput({
  value,
  onChange,
  placeholders,
  staticPlaceholder,
  intervalMs = 3200,
  className,
  id = "cycling-search",
  ariaLabel = "Buscar",
}: Props) {
  const reduce = useReducedMotion();
  const [idx, setIdx] = useState(0);
  const timer = useRef<ReturnType<typeof setInterval> | null>(null);

  const cycling = !reduce && placeholders.length > 1;

  useEffect(() => {
    if (!cycling) return;

    const stop = () => {
      if (timer.current) { clearInterval(timer.current); timer.current = null; }
    };
    const start = () => {
      stop();
      timer.current = setInterval(
        () => setIdx((i) => (i + 1) % placeholders.length),
        intervalMs,
      );
    };
    const onVisibility = () =>
      document.visibilityState === "visible" ? start() : stop();

    start();
    document.addEventListener("visibilitychange", onVisibility);
    return () => {
      stop();
      document.removeEventListener("visibilitychange", onVisibility);
    };
  }, [cycling, placeholders.length, intervalMs]);

  const fallback = staticPlaceholder ?? placeholders[0] ?? "";

  return (
    <div className={cn("relative", className)}>
      <Search
        className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 pointer-events-none"
        style={{ color: "var(--text-muted)" }}
        aria-hidden="true"
      />

      <label htmlFor={id} className="sr-only">{ariaLabel}</label>
      <input
        id={id}
        type="search"
        autoComplete="off"
        // Cuando ciclamos, el placeholder real va vacío y lo pinta el overlay.
        placeholder={cycling ? "" : fallback}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full pl-9 pr-9 py-2.5 rounded-md text-sm transition outline-none focus-visible:ring-2"
        style={{
          background: "var(--bg-elevated)",
          border: "1px solid var(--border)",
          color: "var(--text-primary)",
          ["--tw-ring-color" as string]: "var(--blue-lg)",
        }}
      />

      {/* Overlay del placeholder rotativo — solo con el input vacío */}
      {cycling && !value && (
        <div className="pointer-events-none absolute inset-y-0 left-9 right-9 flex items-center overflow-hidden">
          <AnimatePresence mode="wait">
            <motion.span
              key={idx}
              initial={{ y: 6, opacity: 0 }}
              animate={{ y: 0, opacity: 1 }}
              exit={{ y: -10, opacity: 0 }}
              transition={{ duration: 0.28, ease: [0.22, 1, 0.36, 1] }}
              className="block truncate text-sm"
              style={{ color: "var(--text-muted)" }}
            >
              {placeholders[idx]}
            </motion.span>
          </AnimatePresence>
        </div>
      )}

      <AnimatePresence>
        {value && (
          <motion.button
            type="button"
            initial={{ opacity: 0, scale: 0.8 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0, scale: 0.8 }}
            className="absolute right-2.5 top-1/2 -translate-y-1/2 w-6 h-6 rounded-full flex items-center justify-center"
            style={{ background: "var(--border-md)" }}
            onClick={() => onChange("")}
            aria-label="Limpiar búsqueda"
          >
            <X className="w-3 h-3" style={{ color: "var(--text-muted)" }} />
          </motion.button>
        )}
      </AnimatePresence>
    </div>
  );
}

export default CyclingPlaceholderInput;
