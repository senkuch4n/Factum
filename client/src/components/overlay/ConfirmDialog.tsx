"use client";

/**
 * Diálogo de confirmación sobre el `Dialog` de PrimeReact (pt `dialog`).
 * Misma API que la versión anterior (la usan `dashboard/page.tsx` y
 * `ReportStep`). Propio de un alert-dialog:
 * - `role="alertdialog"`, `aria-modal`, `aria-labelledby` (Prime) y
 *   `aria-describedby` a la descripción;
 * - foco inicial en "cancelar";
 * - Escape cancela; el click afuera NO cierra salvo `dismissableMask`
 *   (evita descartes accidentales);
 * - al cerrar, Prime devuelve el foco al disparador.
 * El orden `onConfirm()` → `onOpenChange(false)` se mantiene.
 *
 * Asíncrono (wizard-concurrencia-pestanas DT14): si `onConfirm` devuelve una
 * promesa, el botón de confirmar muestra spinner, los dos botones quedan
 * deshabilitados (Escape y el click afuera también) y el diálogo se cierra solo
 * si la promesa no resuelve `false`. El error lo muestra el llamador con `error`.
 */

import { useId, useState, type ReactNode } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { AlertTriangle } from "lucide-react";
import { FxBanner } from "@/components/feedback/FxBanner";
import { cn } from "@/lib/utils";

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Sincrónico (cierra enseguida) o asíncrono: `false` deja el diálogo abierto. */
  onConfirm: () => void | Promise<boolean | void>;
  title: string;
  description?: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  /** rojo para acciones destructivas (default), neutro para confirmaciones */
  tone?: "destructive" | "neutral";
  /** Error de la última confirmación, dentro del cuerpo (`FxBanner` de error). */
  error?: string | null;
  /** Cuerpo extra debajo de la descripción. */
  children?: ReactNode;
  /** El click afuera cancela (default `false`). */
  dismissableMask?: boolean;
}

const isThenable = (v: unknown): v is PromiseLike<unknown> =>
  !!v && typeof (v as { then?: unknown }).then === "function";

export function ConfirmDialog({
  open,
  onOpenChange,
  onConfirm,
  title,
  description,
  confirmLabel = "Confirmar",
  cancelLabel = "Cancelar",
  tone = "destructive",
  error = null,
  children,
  dismissableMask = false,
}: Props) {
  const uid = useId();
  const cancelId = `${uid}-cancel`;
  const descId = `${uid}-desc`;
  const destructive = tone === "destructive";
  const [busy, setBusy] = useState(false);

  async function handleConfirm() {
    if (busy) return;
    const out = onConfirm();
    if (!isThenable(out)) { onOpenChange(false); return; }
    setBusy(true);
    let close = true;
    try {
      close = (await out) !== false;
    } catch {
      close = false; // el llamador ya mostró el motivo con `error`
    } finally {
      setBusy(false);
    }
    if (close) onOpenChange(false);
  }

  return (
    <Dialog
      visible={open}
      onHide={() => { if (!busy) onOpenChange(false); }}
      modal
      closable
      showCloseIcon={false}
      closeOnEscape={!busy}
      dismissableMask={dismissableMask && !busy}
      draggable={false}
      resizable={false}
      onShow={() => document.getElementById(cancelId)?.focus()}
      // Ancho por pt (no por className): el pt del componente se fusiona después
      // del global, así tailwind-merge se queda con este ancho.
      pt={{
        root: {
          role: "alertdialog",
          "aria-describedby": description ? descId : undefined,
          "aria-busy": busy || undefined,
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
          <Button
            id={cancelId}
            severity="secondary"
            label={cancelLabel}
            disabled={busy}
            onClick={() => onOpenChange(false)}
          />
          <Button
            severity={destructive ? "danger" : undefined}
            label={confirmLabel}
            loading={busy}
            disabled={busy}
            onClick={() => { void handleConfirm(); }}
          />
        </>
      }
    >
      {description && <p id={descId} className="m-0 text-fx-body-sm text-fx-text-2">{description}</p>}
      {children && <div className={description ? "mt-4" : undefined}>{children}</div>}
      {error && <FxBanner tone="error" className="mt-4">{error}</FxBanner>}
    </Dialog>
  );
}

export default ConfirmDialog;
