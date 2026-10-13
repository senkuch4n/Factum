"use client";

import { Button } from "primereact/button";
import { AlertTriangle, CheckCircle2, PanelRightOpen } from "lucide-react";

/**
 * Barra inferior `sticky` de la ficha en `< lg` (§6.15). Va dentro del
 * `<form>`, justo antes de `StepActions`: al llegar al final se apoya encima
 * de los botones y nunca tapa el principal.
 */
export function CaseSummaryBar({ nro, missing, open, onOpen }: {
  nro: string;
  missing: number;
  open: boolean;
  onOpen: () => void;
}) {
  return (
    <div className="sticky bottom-0 z-10 -mx-5 flex items-center gap-2 border-t border-fx-border bg-fx-surface-1 px-5 py-2 shadow-fx-1 sm:-mx-8 sm:px-8">
      <p className="m-0 flex min-w-0 flex-1 items-center gap-1.5 text-fx-body-sm">
        <span className={nro ? "min-w-0 truncate font-medium tabular-nums text-fx-text" : "min-w-0 truncate text-fx-text-3"} translate="no">
          {nro || "Sin expediente"}
        </span>
        <span aria-hidden="true" className="text-fx-text-3">·</span>
        {missing > 0 ? (
          <span className="inline-flex shrink-0 items-center gap-1 text-fx-text-2">
            <AlertTriangle className="h-3.5 w-3.5 text-fx-warning" aria-hidden="true" />
            Faltan {missing}
          </span>
        ) : (
          <span className="inline-flex shrink-0 items-center gap-1 text-fx-success">
            <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
            Completo
          </span>
        )}
      </p>
      <Button
        type="button"
        size="small"
        outlined
        severity="secondary"
        icon={<PanelRightOpen className="h-3.5 w-3.5" aria-hidden="true" />}
        label="Ver ficha"
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={onOpen}
        className="shrink-0 min-h-11"
      />
    </div>
  );
}
