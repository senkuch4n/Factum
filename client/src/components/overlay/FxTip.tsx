"use client";

import { cloneElement, useId, type ReactElement, type ReactNode } from "react";
import { Tooltip } from "primereact/tooltip";

interface FxTipProps {
  label: ReactNode;
  /** Un solo elemento que acepte atributos `data-*` (botón, link…). */
  children: ReactElement<Record<string, unknown>>;
  side?: "top" | "bottom" | "left" | "right";
  disabled?: boolean;
}

/**
 * Tooltip de Prime sobre un único hijo: `label`, `side` y `disabled`.
 *
 * - Aparece con hover y con foco de teclado (`event="both"`) y se descarta con
 *   Escape (WCAG 1.4.13).
 * - Se ata por selector de atributo y no por ref: no hay que fusionar refs con
 *   el hijo. Hijo y Tooltip se montan juntos, así el binding encuentra el target.
 * - No reemplaza el nombre accesible: el hijo tiene que tener su `aria-label`.
 */
export function FxTip({ label, children, side = "top", disabled }: FxTipProps) {
  // useId devuelve ":r1:"; los ":" romperían el selector, por eso se limpian.
  const id = useId().replace(/[^a-zA-Z0-9_-]/g, "");
  if (disabled || label == null || label === "") return children;

  return (
    <>
      {cloneElement(children, { "data-fx-tip": id })}
      <Tooltip
        target={`[data-fx-tip="${id}"]`}
        content={label}
        position={side}
        event="both"
        showDelay={350}
        closeOnEscape
      />
    </>
  );
}
