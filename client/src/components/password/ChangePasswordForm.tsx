"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { Button } from "primereact/button";
import { Message } from "primereact/message";
import { AlertTriangle, Loader2, LogOut, ShieldCheck } from "lucide-react";
import { api } from "@/lib/api";
import { useAuthMode } from "@/hooks/useAuthMode";
import { describeChangePasswordError } from "@/lib/password-policy";
import { PasswordChangeFields, usePasswordChangeForm } from "./PasswordChangeFields";

/**
 * Cambio obligatorio (`/cambiar-contrasena`). Al montar pide `api.me()`: si la
 * cuenta no tiene que cambiar la contraseña, va al dashboard. Un 401 lo
 * resuelve el interceptor de `lib/api.ts` (vuelve a `/` con el aviso).
 */
export function ChangePasswordForm() {
  const router = useRouter();
  const { passwordMinLength, passwordMaxLength } = useAuthMode();
  const [dni, setDni] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [focusSubmit, setFocusSubmit] = useState(0);
  const inFlight = useRef(false);
  const submitRef = useRef<HTMLDivElement>(null);
  const policy = { dni: dni ?? "", minLength: passwordMinLength, maxLength: passwordMaxLength };
  const form = usePasswordChangeForm(policy);

  useEffect(() => {
    let active = true;
    api.me()
      .then((u) => {
        if (!active) return;
        if (!u.must_change_password) router.replace("/dashboard");
        else setDni(u.dni);
      })
      .catch(() => {
        if (active) router.replace("/");
      });
    return () => {
      active = false;
    };
  }, [router]);

  /* Foco al botón después del render en que se rehabilita (un botón disabled
     no recibe foco). */
  useEffect(() => {
    if (focusSubmit && !saving) submitRef.current?.querySelector<HTMLButtonElement>('button[type="submit"]')?.focus();
  }, [focusSubmit, saving]);

  function onChange(...args: Parameters<typeof form.update>) {
    setServerError(null);
    form.update(...args);
  }

  async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (inFlight.current || dni === null) return;
    setServerError(null);
    if (!form.validate()) return;

    inFlight.current = true;
    setSaving(true);
    try {
      await api.changePassword(form.values);
      // Sin resetear `saving`: el botón queda en "Guardando…" mientras navega.
      router.replace("/dashboard");
    } catch (err) {
      const { field, message } = describeChangePasswordError(err);
      setSaving(false);
      inFlight.current = false;
      if (field) {
        form.setFieldError(field, message);
      } else {
        setServerError(message);
        setFocusSubmit((n) => n + 1);
      }
    }
  }

  function handleExit() {
    api.logout();
    router.push("/");
  }

  return (
    <div>
      <p className="text-fx-label uppercase text-fx-accent-text">Primer ingreso</p>
      <h1 className="mt-2 text-fx-h1 text-fx-text">Cambiá tu contraseña</h1>
      <p className="mt-2 text-fx-body-sm text-fx-text-2">
        Es tu primer ingreso. Elegí una contraseña nueva para seguir.
      </p>

      {dni === null ? (
        <p className="mt-6 flex items-center gap-2 text-fx-body-sm text-fx-text-2" role="status">
          <Loader2 className="h-4 w-4 animate-spin motion-reduce:animate-none" aria-hidden="true" /> Cargando tu cuenta…
        </p>
      ) : (
        <form noValidate onSubmit={handleSubmit} className="mt-6 space-y-5">
          <PasswordChangeFields
            values={form.values}
            errors={form.errors}
            refs={form.refs}
            onChange={onChange}
            policy={policy}
            readOnly={saving}
            currentLabel="Contraseña actual (la temporal)"
          />

          {serverError && (
            <Message
              severity="error"
              icon={<AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}
              text={serverError}
              className="w-full motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)]"
            />
          )}

          <div ref={submitRef}>
            <Button
              type="submit"
              size="large"
              className="mt-2 w-full"
              icon={<ShieldCheck className="h-5 w-5" aria-hidden="true" />}
              label={saving ? "Guardando…" : "Guardar y entrar"}
              loading={saving}
            />
          </div>
        </form>
      )}

      <div className="mt-4 flex justify-center">
        <Button
          type="button"
          link
          size="small"
          label="Salir"
          icon={<LogOut className="h-4 w-4" aria-hidden="true" />}
          onClick={handleExit}
          disabled={saving}
        />
      </div>
    </div>
  );
}
