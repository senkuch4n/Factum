"use client";

import { useEffect, useRef, useState } from "react";
import { Button } from "primereact/button";
import { Check, Copy, AlertCircle } from "lucide-react";
import { cn } from "@/lib/utils";

type CopyState = "idle" | "copied" | "failed";

interface Props {
  /** Texto que se copia. No se loguea ni se guarda en ningún lado. */
  text: string;
  /** Nombre accesible con contexto, p. ej. "Copiar contraseña del ZIP". */
  label: string;
  /** Texto visible del botón (default "Copiar"). */
  buttonLabel?: string;
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
export function CopyButton({ text, label, buttonLabel = "Copiar", className }: Props) {
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
      <Button
        type="button"
        severity="secondary"
        size="small"
        icon={
          <Icon
            className={cn(
              "h-3.5 w-3.5",
              state === "copied" && "text-fx-success",
              state === "failed" && "text-fx-warning",
            )}
            aria-hidden="true"
          />
        }
        label={buttonLabel}
        aria-label={label}
        onClick={handleCopy}
      />
      <span
        aria-live="polite"
        role="status"
        className={cn("text-xs font-medium", state === "failed" ? "text-fx-warning" : "text-fx-text-2")}
      >
        {state === "copied" ? "Copiada" : state === "failed" ? "No se pudo copiar" : ""}
      </span>
    </span>
  );
}
