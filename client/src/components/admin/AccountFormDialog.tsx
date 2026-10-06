"use client";

import { useEffect, useRef, useState } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { InputText } from "primereact/inputtext";
import { InputTextarea } from "primereact/inputtextarea";
import { Building2, Fingerprint, Hash, Mail, NotebookPen, Phone, RefreshCw, UserRound } from "lucide-react";
import { FormField, describedBy } from "@/components/FormField";
import { FxBanner } from "@/components/feedback/FxBanner";
import { ConfirmDialog } from "@/components/overlay/ConfirmDialog";
import { useFxToast } from "@/components/shell/FxToastProvider";
import { ApiError, api } from "@/lib/api";
import {
  ACCOUNT_FORM_FIELDS, ACCOUNT_LIMITS, ADMIN_MESSAGES, EMPTY_ACCOUNT_FORM,
  accountToForm, adminErrorMessage, sameAccountForm, trimAccountForm, validateAccountField, validateAccountForm,
  type AccountFormField, type AccountFormValues,
} from "@/lib/admin-accounts";
import type { AdminUser, AdminUserField, AdminUserWithPassword } from "@/types";

const ID_PREFIX = "acct-";
const FORM_ID = "account-form";
const fieldId = (k: AccountFormField) => `${ID_PREFIX}${k}`;

type Errors = Partial<Record<AdminUserField, string>>;

interface Props {
  visible: boolean;
  /** `null` = alta; una cuenta = edición. */
  user: AdminUser | null;
  onHide: () => void;
  /** Alta exitosa: el padre abre el diálogo de la temporal. */
  onCreated: (result: AdminUserWithPassword) => void;
  /** Edición exitosa con cambios. */
  onUpdated: (user: AdminUser) => void;
  /** "Ver cuenta existente" del `dni_taken`. */
  onViewExisting: (id: string) => void;
  /** "Recargar" del `stale_update`: E2 + upsert en la lista. */
  onReload: (id: string) => Promise<AdminUser | null>;
}

/**
 * Alta y edición de una cuenta (abm-clientes §9.4). Valida al salir de cada
 * campo y al enviar (mismas reglas que el backend), con `aria-invalid`,
 * `aria-describedby` y foco al primer campo con error. En edición manda
 * `expected_updated_at` (D11). Con cambios sin guardar, cancelar pide
 * confirmación.
 */
export function AccountFormDialog({ visible, user, onHide, onCreated, onUpdated, onViewExisting, onReload }: Props) {
  const mode: "create" | "edit" = user ? "edit" : "create";
  const toast = useFxToast();
  /** Cuenta contra la que se edita (se reemplaza con "Recargar"). */
  const [base, setBase] = useState<AdminUser | null>(user);
  const [form, setForm] = useState<AccountFormValues>(EMPTY_ACCOUNT_FORM);
  const [errors, setErrors] = useState<Errors>({});
  const [saving, setSaving] = useState(false);
  const [banner, setBanner] = useState<string | null>(null);
  const [stale, setStale] = useState(false);
  const [reloading, setReloading] = useState(false);
  const [existingId, setExistingId] = useState<string | null>(null);
  const [confirmDiscard, setConfirmDiscard] = useState(false);
  const inFlight = useRef(false);

  const initial = base ? accountToForm(base) : EMPTY_ACCOUNT_FORM;
  const dirty = !sameAccountForm(form, initial);

  // Cada apertura arranca limpia, con los datos vigentes de la cuenta.
  useEffect(() => {
    if (!visible) return;
    setBase(user);
    setForm(user ? accountToForm(user) : EMPTY_ACCOUNT_FORM);
    setErrors({});
    setBanner(null);
    setStale(false);
    setExistingId(null);
  }, [visible, user]);

  function focusField(k: AdminUserField) {
    if (k === "dni" && mode === "edit") return;
    document.getElementById(fieldId(k as AccountFormField))?.focus();
  }

  function focusFirstError(err: Errors) {
    const k = ACCOUNT_FORM_FIELDS.find(f => err[f]);
    if (k) focusField(k);
  }

  function update(k: AccountFormField, value: string) {
    setForm(f => ({ ...f, [k]: value }));
    if (k === "dni") setExistingId(null);
    // Si el campo ya mostraba un error, se revalida en vivo para sacarlo apenas se corrige.
    if (errors[k]) setErrors(e => ({ ...e, [k]: validateAccountField(k, value) ?? undefined }));
  }

  function blur(k: AccountFormField) {
    if (k === "dni" && mode === "edit") return;
    // No marcar como error un obligatorio que todavía no se tocó.
    if (form[k].trim() === "" && !errors[k]) return;
    setErrors(e => ({ ...e, [k]: validateAccountField(k, form[k]) ?? undefined }));
  }

  function requestClose() {
    if (saving) return;
    if (dirty) setConfirmDiscard(true);
    else onHide();
  }

  async function handleReload() {
    if (!base) return;
    setReloading(true);
    const fresh = await onReload(base.id);
    setReloading(false);
    if (!fresh) { setBanner(ADMIN_MESSAGES.user_not_found); return; }
    setBase(fresh);
    setForm(accountToForm(fresh));
    setErrors({});
    setStale(false);
    setBanner(null);
    toast.info("Cuenta recargada", "Ves los datos actuales. Volvé a hacer tus cambios si hace falta.");
  }

  async function handleSubmit() {
    if (inFlight.current) return;
    setBanner(null);
    setStale(false);
    setExistingId(null);
    const err = validateAccountForm(form, mode);
    setErrors(err);
    if (Object.keys(err).length) { focusFirstError(err); return; }
    const values = trimAccountForm(form);

    if (mode === "edit" && base && sameAccountForm(values, accountToForm(base))) {
      toast.info("No había cambios para guardar");
      onHide();
      return;
    }

    inFlight.current = true;
    setSaving(true);
    try {
      if (mode === "create") {
        const result = await api.adminCreateUser(values);
        onCreated(result);
      } else if (base) {
        const { dni: _dni, ...rest } = values;
        void _dni;
        const res = await api.adminUpdateUser(base.id, { ...rest, expected_updated_at: base.updated_at });
        if (res.changed) {
          onUpdated(res.user);
          toast.success("Cambios guardados", res.user.name);
        } else {
          toast.info("No había cambios para guardar");
        }
        onHide();
      }
    } catch (e) {
      handleError(e);
    } finally {
      inFlight.current = false;
      setSaving(false);
    }
  }

  function handleError(e: unknown) {
    const code = e instanceof ApiError ? e.code : null;
    const body = e instanceof ApiError ? e.body : null;
    if (code === "validation_failed" && body?.field && body.field !== "expected_updated_at") {
      const f = body.field as AdminUserField;
      const msg = e instanceof ApiError && e.serverMessage ? e.serverMessage : (validateAccountField(f, "") ?? "");
      setErrors(er => ({ ...er, [f]: msg }));
      focusField(f);
      return;
    }
    if (code === "dni_taken") {
      setErrors(er => ({ ...er, dni: ADMIN_MESSAGES.dni_taken }));
      setExistingId(body?.existing_user_id ?? null);
      focusField("dni");
      return;
    }
    if (code === "stale_update") {
      setStale(true);
      setBanner(ADMIN_MESSAGES.stale_update);
      return;
    }
    setBanner(adminErrorMessage(e));
  }

  function textField(
    k: Exclude<AccountFormField, "notes" | "dni">,
    opts: { label: string; sublabel?: string; icon: React.ElementType; hint?: string; required?: boolean;
      type?: string; inputMode?: React.HTMLAttributes<HTMLInputElement>["inputMode"]; autoComplete?: string;
      maxLength: number; placeholder?: string },
  ) {
    const id = fieldId(k);
    const error = errors[k];
    return (
      <FormField id={id} icon={opts.icon} label={opts.label} sublabel={opts.sublabel} error={error}
        hint={opts.hint} required={opts.required}>
        <InputText
          id={id}
          name={`cuenta-${k}`}
          type={opts.type ?? "text"}
          inputMode={opts.inputMode}
          autoComplete={opts.autoComplete ?? "off"}
          spellCheck={k === "contact_email" ? false : undefined}
          maxLength={opts.maxLength}
          placeholder={opts.placeholder}
          invalid={!!error}
          aria-invalid={!!error || undefined}
          aria-required={opts.required || undefined}
          aria-describedby={describedBy(id, { hint: opts.hint, error })}
          value={form[k]}
          readOnly={saving}
          onChange={e => update(k, e.target.value)}
          onBlur={() => blur(k)}
        />
      </FormField>
    );
  }

  const dniId = fieldId("dni");
  const dniHint = mode === "edit" ? "El DNI no se puede cambiar." : "Solo números, 7 u 8 dígitos.";
  const notesId = fieldId("notes");
  const notesHint = "El cliente no las ve.";
  const notesLen = form.notes.trim().length;

  return (
    <>
      <Dialog
        header={mode === "create" ? "Nueva cuenta" : `Editar cuenta de ${base?.name ?? ""}`}
        visible={visible}
        onHide={requestClose}
        onShow={() => focusField(mode === "create" ? "dni" : "name")}
        closeOnEscape={!confirmDiscard}
        draggable={false}
        resizable={false}
        pt={{
          root: { className: "w-[min(40rem,100%)] max-sm:w-full max-sm:h-full max-sm:max-h-full max-sm:rounded-none max-sm:border-0" },
          mask: { className: "max-sm:p-0" },
        }}
        footer={
          <>
            <Button label="Cancelar" severity="secondary" onClick={requestClose} disabled={saving} />
            <Button
              type="submit"
              form={FORM_ID}
              label={mode === "create"
                ? (saving ? "Creando…" : "Crear cuenta")
                : (saving ? "Guardando…" : "Guardar cambios")}
              loading={saving}
              disabled={reloading}
            />
          </>
        }
      >
        {mode === "create" && (
          <p className="m-0 mb-4 text-fx-body-sm text-fx-text-2">
            La cuenta se crea con rol cliente. Al confirmar vas a ver una contraseña temporal para entregarle.
          </p>
        )}
        {banner && (
          <FxBanner tone="error" className="mb-4">
            <span className="flex flex-wrap items-center gap-x-3 gap-y-2">
              <span>{banner}</span>
              {stale && (
                <Button
                  type="button"
                  size="small"
                  severity="secondary"
                  label={reloading ? "Recargando…" : "Recargar"}
                  icon={<RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />}
                  loading={reloading}
                  onClick={() => { void handleReload(); }}
                />
              )}
            </span>
          </FxBanner>
        )}
        <form
          id={FORM_ID}
          noValidate
          onSubmit={(e) => { e.preventDefault(); void handleSubmit(); }}
          className="grid grid-cols-1 gap-3.5 sm:grid-cols-2"
        >
          <FormField id={dniId} icon={Fingerprint} label="DNI" required={mode === "create"} error={errors.dni} hint={dniHint}>
            <InputText
              id={dniId}
              name="cuenta-dni"
              inputMode="numeric"
              autoComplete="off"
              maxLength={8}
              placeholder={mode === "create" ? "30111222" : undefined}
              disabled={mode === "edit"}
              invalid={!!errors.dni}
              aria-invalid={!!errors.dni || undefined}
              aria-required={mode === "create" || undefined}
              aria-describedby={describedBy(dniId, { hint: dniHint, error: errors.dni })}
              value={form.dni}
              readOnly={saving}
              onChange={e => update("dni", e.target.value.replace(/\D/g, "").slice(0, 8))}
              onBlur={() => blur("dni")}
              className="tabular-nums"
            />
            {existingId && (
              <Button
                type="button"
                link
                size="small"
                label="Ver cuenta existente"
                className="mt-1 px-0"
                onClick={() => onViewExisting(existingId)}
              />
            )}
          </FormField>
          {textField("name", {
            label: "Nombre y apellido", icon: UserRound, required: true, autoComplete: "off",
            maxLength: ACCOUNT_LIMITS.name, placeholder: "Ana Pérez",
          })}
          {textField("sigla", {
            label: "Sigla / identificador interno", sublabel: "(opcional)", icon: Hash, maxLength: ACCOUNT_LIMITS.sigla,
          })}
          {textField("contact_phone", {
            label: "Teléfono", sublabel: "(opcional)", icon: Phone, type: "tel", inputMode: "tel",
            maxLength: ACCOUNT_LIMITS.contact_phone, placeholder: "+54 11 5555-1234",
          })}
          {textField("contact_email", {
            label: "Email de contacto", sublabel: "(opcional)", icon: Mail, type: "email", inputMode: "email",
            maxLength: ACCOUNT_LIMITS.contact_email, hint: "Factum no le envía mails.", placeholder: "ana@estudio.com",
          })}
          {textField("organization", {
            label: "Organización", sublabel: "(opcional)", icon: Building2, maxLength: ACCOUNT_LIMITS.organization,
            placeholder: "Estudio, fuerza o razón social…",
          })}
          <div className="sm:col-span-2">
            <FormField id={notesId} icon={NotebookPen} label="Notas internas" sublabel="(opcional)" error={errors.notes} hint={notesHint}>
              <InputTextarea
                id={notesId}
                name="cuenta-notes"
                rows={3}
                autoResize
                invalid={!!errors.notes}
                aria-invalid={!!errors.notes || undefined}
                aria-describedby={[describedBy(notesId, { hint: notesHint, error: errors.notes }), `${notesId}-count`].filter(Boolean).join(" ")}
                value={form.notes}
                readOnly={saving}
                onChange={e => update("notes", e.target.value)}
                onBlur={() => blur("notes")}
              />
              <p
                id={`${notesId}-count`}
                className={`m-0 mt-1 text-right text-xs tabular-nums ${notesLen > ACCOUNT_LIMITS.notes ? "text-fx-danger" : "text-fx-text-3"}`}
              >
                {notesLen.toLocaleString("es-AR")} / {ACCOUNT_LIMITS.notes.toLocaleString("es-AR")}
              </p>
            </FormField>
          </div>
        </form>
      </Dialog>
      <ConfirmDialog
        open={confirmDiscard}
        onOpenChange={setConfirmDiscard}
        onConfirm={onHide}
        tone="neutral"
        title="¿Descartar los cambios?"
        description="Lo que cargaste en este formulario no se va a guardar."
        confirmLabel="Descartar"
        cancelLabel="Seguir editando"
      />
    </>
  );
}
