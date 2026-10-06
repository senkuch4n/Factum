"use client";

import { useEffect, useRef, useState } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { FxBanner } from "@/components/feedback/FxBanner";
import { useFxToast } from "@/components/shell/FxToastProvider";
import { useAuthMode } from "@/hooks/useAuthMode";
import { api } from "@/lib/api";
import { describeChangePasswordError } from "@/lib/password-policy";
import { PasswordChangeFields, usePasswordChangeForm } from "./PasswordChangeFields";

const ID_PREFIX = "dlg-pwd-";

/**
 * "Cambiar contraseña" (menú de usuario, solo en modo `local`). Si sale bien,
 * `api.changePassword` ya guardó el token nuevo: esta sesión sigue y las otras
 * se cortan. Al cerrarse limpia los campos.
 */
export function ChangePasswordDialog({ visible, onHide, dni }: { visible: boolean; onHide: () => void; dni: string }) {
  const { passwordMinLength, passwordMaxLength } = useAuthMode();
  const policy = { dni, minLength: passwordMinLength, maxLength: passwordMaxLength };
  const form = usePasswordChangeForm(policy);
  const { reset, focusField } = form;
  const [saving, setSaving] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const inFlight = useRef(false);
  const toast = useFxToast();

  // Cada apertura arranca limpia, con el foco en el primer campo.
  useEffect(() => {
    if (!visible) return;
    reset();
    setServerError(null);
  }, [visible, reset]);

  function onChange(...args: Parameters<typeof form.update>) {
    setServerError(null);
    form.update(...args);
  }

  async function handleSave() {
    if (inFlight.current) return;
    setServerError(null);
    if (!form.validate()) return;
    inFlight.current = true;
    setSaving(true);
    try {
      await api.changePassword(form.values);
      toast.success("Contraseña actualizada", "Cerramos tus otras sesiones abiertas.");
      onHide();
    } catch (err) {
      const { field, message } = describeChangePasswordError(err);
      if (field) form.setFieldError(field, message);
      else setServerError(message);
    } finally {
      setSaving(false);
      inFlight.current = false;
    }
  }

  return (
    <Dialog
      header="Cambiar contraseña"
      visible={visible}
      onHide={() => { if (!saving) onHide(); }}
      onShow={() => focusField("current_password")}
      // Mismo patrón que ExpertProfileDialog: ancho y pantalla completa en < sm por pt.
      pt={{
        root: { className: "w-[min(28rem,100%)] max-sm:w-full max-sm:h-full max-sm:max-h-full max-sm:rounded-none max-sm:border-0" },
        mask: { className: "max-sm:p-0" },
      }}
      footer={
        <>
          <Button label="Cancelar" severity="secondary" onClick={onHide} disabled={saving} />
          <Button
            type="submit"
            form="change-password-dialog-form"
            label={saving ? "Guardando…" : "Guardar contraseña"}
            loading={saving}
          />
        </>
      }
    >
      <p className="m-0 mb-4 text-fx-body-sm text-fx-text-2">
        Al guardar, se cierran tus otras sesiones abiertas. Esta sigue activa.
      </p>
      {serverError && <FxBanner tone="error" className="mb-3">{serverError}</FxBanner>}
      <form
        id="change-password-dialog-form"
        noValidate
        onSubmit={(e) => { e.preventDefault(); void handleSave(); }}
      >
        <PasswordChangeFields
          values={form.values}
          errors={form.errors}
          refs={form.refs}
          onChange={onChange}
          policy={policy}
          idPrefix={ID_PREFIX}
          readOnly={saving}
        />
      </form>
    </Dialog>
  );
}
