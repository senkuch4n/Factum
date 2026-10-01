"use client";

/**
 * Diálogo de confirmación sobre el `Dialog` de PrimeReact (pt `dialog`).
 * Misma API que la versión anterior (la usan `dashboard/page.tsx` y
 * `ReportStep`). Propio de un alert-dialog:
 * - `role="alertdialog"`, `aria-modal`, `aria-labelledby` (Prime) y
 *   `aria-describedby` a la descripción;
 * - foco inicial en "cancelar";
 * - Escape cancela, el click afuera NO cierra (evita descartes accidentales);
 * - al cerrar, Prime devuelve el foco al disparador.
 * El orden `onConfirm()` → `onOpenChange(false)` se mantiene.
 */

import { useId } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { AlertTriangle } from "lucide-react";
import { cn } from "@/lib/utils";

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onConfirm: () => void;
  title: string;
  description?: React.ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  /** rojo para acciones destructivas (default), neutro para confirmaciones */
  tone?: "destructive" | "neutral";
}

export function ConfirmDialog({
  open,
  onOpenChange,
  onConfirm,
  title,
  description,
  confirmLabel = "Confirmar",
  cancelLabel = "Cancelar",
  tone = "destructive",
}: Props) {
  const uid = useId();
  const cancelId = `${uid}-cancel`;
  const descId = `${uid}-desc`;
  const destructive = tone === "destructive";

  return (
    <Dialog
      visible={open}
      onHide={() => onOpenChange(false)}
      modal
      closable
      showCloseIcon={false}
      closeOnEscape
      dismissableMask={false}
      draggable={false}
      resizable={false}
      onShow={() => document.getElementById(cancelId)?.focus()}
      // Ancho por pt (no por className): el pt del componente se fusiona después
      // del global, así tailwind-merge se queda con este ancho.
      pt={{
        root: {
          role: "alertdialog",
          "aria-describedby": description ? descId : undefined,
          className: "w-[min(26rem,100%)]",
        },
      }}
      header={
        <span className="flex items-center gap-3">
          <span
            className={cn(
              "inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-fx-md",
              destructive ? "bg-fx-danger-soft text-fx-danger" : "bg-fx-info-soft text-fx-info",
            )}
          >
            <AlertTriangle className="h-4 w-4" aria-hidden="true" />
          </span>
          {title}
        </span>
      }
      footer={
        <>
          <Button id={cancelId} severity="secondary" label={cancelLabel} onClick={() => onOpenChange(false)} />
          <Button
            severity={destructive ? "danger" : undefined}
            label={confirmLabel}
            onClick={() => { onConfirm(); onOpenChange(false); }}
          />
        </>
      }
    >
      {description && <p id={descId} className="m-0 text-fx-body-sm text-fx-text-2">{description}</p>}
    </Dialog>
  );
}

export default ConfirmDialog;
