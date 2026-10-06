"use client";

import { useEffect, useId, useState } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { InputTextarea } from "primereact/inputtextarea";
import { Ban, MessageSquareText } from "lucide-react";
import { FormField, describedBy } from "@/components/FormField";
import { FxBanner } from "@/components/feedback/FxBanner";
import { ACCOUNT_LIMITS, validateAccountField } from "@/lib/admin-accounts";
import type { AdminUser } from "@/types";

interface Props {
  /** `null` = cerrado. */
  user: AdminUser | null;
  onCancel: () => void;
  /**
   * Suspende con el motivo (vacío = sin motivo). Devuelve `null` si salió
   * bien (el padre cierra) o el texto del error para mostrar acá.
   */
  onConfirm: (reason: string) => Promise<string | null>;
}

/**
 * Confirmación de "Suspender" con motivo opcional (D10). Diálogo propio
 * porque `ConfirmDialog` no admite campos. `role="alertdialog"`, foco
 * inicial en "Cancelar", Escape cancela y el click afuera no cierra.
 */
export function SuspendAccountDialog({ user, onCancel, onConfirm }: Props) {
  const uid = useId();
  const cancelId = `${uid}-cancel`;
  const descId = `${uid}-desc`;
  const reasonId = `${uid}-reason`;
  const [reason, setReason] = useState("");
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!user) return;
    setReason("");
    setFieldError(null);
    setError(null);
  }, [user]);

  async function handleConfirm() {
    if (saving) return;
    const err = validateAccountField("reason", reason);
    setFieldError(err);
    if (err) { document.getElementById(reasonId)?.focus(); return; }
    setSaving(true);
    setError(null);
    const result = await onConfirm(reason.trim());
    setSaving(false);
    if (result) setError(result);
  }

  const len = reason.trim().length;
  const hint = "Queda en el historial. El cliente no lo ve.";

  return (
    <Dialog
      visible={user !== null}
      onHide={() => { if (!saving) onCancel(); }}
      modal
      closable
      showCloseIcon={false}
      closeOnEscape
      dismissableMask={false}
      draggable={false}
      resizable={false}
      onShow={() => document.getElementById(cancelId)?.focus()}
      pt={{
        root: {
          role: "alertdialog",
          "aria-describedby": descId,
          className: "w-[min(28rem,100%)]",
        } as React.HTMLAttributes<HTMLDivElement>,
      }}
      header={
        <span className="flex items-center gap-3">
          <span className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-fx-md bg-fx-danger-soft text-fx-danger">
            <Ban className="h-4 w-4" aria-hidden="true" />
          </span>
          <span className="min-w-0">Suspender cuenta</span>
        </span>
      }
      footer={
        <>
          <Button id={cancelId} severity="secondary" label="Cancelar" onClick={onCancel} disabled={saving} />
          <Button
            severity="danger"
            label={saving ? "Suspendiendo…" : "Suspender"}
            loading={saving}
            onClick={() => { void handleConfirm(); }}
          />
        </>
      }
    >
      {user && (
        <div className="flex flex-col gap-4">
          <p id={descId} className="m-0 text-fx-body-sm text-fx-text-2">
            ¿Suspender a <strong className="text-fx-text">{user.name}</strong>? No va a poder entrar y su sesión se va a
            cortar. Sus casos se conservan y podés reactivarla cuando quieras.
          </p>
          {error && <FxBanner tone="error">{error}</FxBanner>}
          <FormField id={reasonId} icon={MessageSquareText} label="Motivo" sublabel="(opcional)" hint={hint} error={fieldError ?? undefined}>
            <InputTextarea
              id={reasonId}
              name="motivo-suspension"
              rows={3}
              autoResize
              value={reason}
              readOnly={saving}
              invalid={!!fieldError}
              aria-invalid={!!fieldError || undefined}
              aria-describedby={[describedBy(reasonId, { hint, error: fieldError ?? undefined }), `${reasonId}-count`].filter(Boolean).join(" ")}
              onChange={(e) => {
                setReason(e.target.value);
                if (fieldError) setFieldError(validateAccountField("reason", e.target.value));
              }}
              onBlur={() => setFieldError(validateAccountField("reason", reason))}
            />
            <p
              id={`${reasonId}-count`}
              className={`m-0 mt-1 text-right text-xs tabular-nums ${len > ACCOUNT_LIMITS.reason ? "text-fx-danger" : "text-fx-text-3"}`}
            >
              {len} / {ACCOUNT_LIMITS.reason}
            </p>
          </FormField>
        </div>
      )}
    </Dialog>
  );
}
