"use client";

import { useEffect, useRef, useState } from "react";
import { Check, Copy, AlertCircle } from "lucide-react";
import { cn } from "@/lib/utils";

type CopyState = "idle" | "copied" | "failed";

interface Props {
  /** Texto que se copia. No se loguea ni se guarda en ningún lado. */
  text: string;
  /** Nombre accesible con contexto, p. ej. "Copiar contraseña del ZIP". */
  label: string;
  className?: string;
}

/** Cuánto dura el feedback "Copiada" / "No se pudo copiar". */
const FEEDBACK_MS = 2000;

/**
 * Botón "Copiar" con feedback accesible. El estado se anuncia en una región
 * `aria-live="polite"` que está siempre montada (si se montara junto con el
 * mensaje, algunos lectores de pantalla no lo leen). Si el portapapeles no
 * está disponible o falla (contexto no seguro, permiso denegado), avisa
 * "No se pudo copiar": el texto que acompaña al botón tiene que quedar
 * seleccionable (`select-all`) para copiarlo a mano.
 */
export function CopyButton({ text, label, className }: Props) {
  const [state, setState] = useState<CopyState>("idle");
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => () => {
    if (timer.current) clearTimeout(timer.current);
  }, []);

  function show(next: CopyState) {
    setState(next);
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(() => setState("idle"), FEEDBACK_MS);
  }

  async function handleCopy() {
    try {
      if (!navigator.clipboard) throw new Error("clipboard no disponible");
      await navigator.clipboard.writeText(text);
      show("copied");
    } catch {
      // Sin detalles del error ni del texto: nunca a la consola.
      show("failed");
    }
  }

  const Icon = state === "copied" ? Check : state === "failed" ? AlertCircle : Copy;

  return (
    <span className={cn("inline-flex items-center gap-2", className)}>
      <button
        type="button"
        onClick={handleCopy}
        className="btn-secondary btn-sm min-h-[32px]"
        aria-label={label}
      >
        <Icon
          className={cn(
            "h-3.5 w-3.5",
            state === "copied" && "text-emerald-500",
            state === "failed" && "text-amber-500",
          )}
          aria-hidden="true"
        />
        Copiar
      </button>
      <span
        aria-live="polite"
        role="status"
        className="text-[11px] font-medium"
        style={{ color: state === "failed" ? "var(--amber)" : "var(--text-secondary)" }}
      >
        {state === "copied" ? "Copiada" : state === "failed" ? "No se pudo copiar" : ""}
      </span>
    </span>
  );
}
