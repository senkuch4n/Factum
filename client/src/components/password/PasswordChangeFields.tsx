"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { Check, Circle, KeyRound, Lock } from "lucide-react";
import { FxPassword } from "@/components/form/FxPassword";
import { FIELD_ICON, FieldError, INPUT, LABEL } from "@/components/login/LoginForm";
import type { ChangePasswordField } from "@/lib/api";
import {
  PASSWORD_FIELD_ORDER,
  PASSWORD_MESSAGES,
  passwordRules,
  validatePasswordChange,
  type PasswordChangeErrors,
  type PasswordChangeValues,
  type PasswordPolicyContext,
} from "@/lib/password-policy";
import { cn } from "@/lib/utils";

const EMPTY: PasswordChangeValues = { current_password: "", new_password: "", new_password_confirmation: "" };

type InputRefs = Record<ChangePasswordField, React.RefObject<HTMLInputElement | null>>;

/**
 * Estado compartido por la pantalla obligatoria y el diálogo: valores,
 * errores por campo, foco al primer campo con error (después del render en
 * que se rehabilitan los inputs) y `same_as_current` en vivo.
 */
export function usePasswordChangeForm(ctx: PasswordPolicyContext) {
  const [values, setValues] = useState<PasswordChangeValues>(EMPTY);
  const [errors, setErrors] = useState<PasswordChangeErrors>({});
  const [focusRequest, setFocusRequest] = useState<{ field: ChangePasswordField; n: number } | null>(null);
  const currentRef = useRef<HTMLInputElement>(null);
  const newRef = useRef<HTMLInputElement>(null);
  const confirmationRef = useRef<HTMLInputElement>(null);
  const refs: InputRefs = {
    current_password: currentRef,
    new_password: newRef,
    new_password_confirmation: confirmationRef,
  };

  useEffect(() => {
    if (!focusRequest) return;
    const el =
      focusRequest.field === "current_password" ? currentRef.current
      : focusRequest.field === "new_password" ? newRef.current
      : confirmationRef.current;
    el?.focus();
  }, [focusRequest]);

  const focusField = useCallback((field: ChangePasswordField) => {
    setFocusRequest((prev) => ({ field, n: (prev?.n ?? 0) + 1 }));
  }, []);

  function update(field: ChangePasswordField, value: string) {
    const next = { ...values, [field]: value };
    setValues(next);
    setErrors((e) => {
      const cleared: PasswordChangeErrors = { ...e, [field]: undefined };
      // "Distinta de la actual", en vivo: se marca y se limpia mientras escribe.
      const same = !!next.current_password && next.current_password === next.new_password;
      if (same) cleared.new_password = PASSWORD_MESSAGES.password_same_as_current;
      else if (cleared.new_password === PASSWORD_MESSAGES.password_same_as_current) cleared.new_password = undefined;
      return cleared;
    });
  }

  /** Valida todo; si hay errores los muestra, enfoca el primero y devuelve `false`. */
  function validate(): boolean {
    const found = validatePasswordChange(values, ctx);
    const first = PASSWORD_FIELD_ORDER.find((f) => found[f]);
    setErrors(found);
    if (first) {
      focusField(first);
      return false;
    }
    return true;
  }

  /** Error del servidor sobre un campo. */
  function setFieldError(field: ChangePasswordField, message: string) {
    setErrors((e) => ({ ...e, [field]: message }));
    focusField(field);
  }

  const reset = useCallback(() => {
    setValues(EMPTY);
    setErrors({});
    setFocusRequest(null);
  }, []);

  return { values, errors, refs, update, validate, setFieldError, focusField, reset };
}

interface PasswordChangeFieldsProps {
  values: PasswordChangeValues;
  errors: PasswordChangeErrors;
  refs: InputRefs;
  onChange: (field: ChangePasswordField, value: string) => void;
  policy: PasswordPolicyContext;
  /** Prefijo de los `id` (la pantalla y el diálogo no chocan). */
  idPrefix?: string;
  readOnly?: boolean;
  /** Etiqueta del primer campo: en el primer ingreso es la temporal. */
  currentLabel?: string;
}

/**
 * Los tres campos del cambio de contraseña con el patrón a11y del login
 * (`aria-invalid`, `aria-describedby`, `FieldError`). Bajo "Contraseña nueva"
 * va la lista de reglas, marcada en vivo con ícono + texto (no solo color)
 * dentro de un `aria-live="polite"`.
 */
export function PasswordChangeFields({
  values,
  errors,
  refs,
  onChange,
  policy,
  idPrefix = "",
  readOnly = false,
  currentLabel = "Contraseña actual",
}: PasswordChangeFieldsProps) {
  const id = (f: ChangePasswordField) => `${idPrefix}${f}`;
  const rulesId = `${idPrefix}new_password-rules`;
  const rules = passwordRules(values.new_password, policy);

  const a11y = (f: ChangePasswordField, extraDescribedBy?: string) => {
    const describedBy = [errors[f] ? `${id(f)}-error` : null, extraDescribedBy].filter(Boolean).join(" ");
    return {
      invalid: !!errors[f],
      "aria-invalid": !!errors[f],
      "aria-describedby": describedBy || undefined,
      "aria-required": true,
      readOnly,
    };
  };

  return (
    <div className="space-y-5">
      <div>
        <label htmlFor={id("current_password")} className={LABEL}>{currentLabel}</label>
        <div className="group relative">
          <Lock className={FIELD_ICON} aria-hidden="true" />
          <FxPassword
            inputId={id("current_password")}
            name="current_password"
            autoComplete="current-password"
            inputRef={refs.current_password}
            inputClassName={INPUT}
            value={values.current_password}
            onChange={(e) => onChange("current_password", e.target.value)}
            {...a11y("current_password")}
          />
        </div>
        <FieldError id={`${id("current_password")}-error`} message={errors.current_password} />
      </div>

      <div>
        <label htmlFor={id("new_password")} className={LABEL}>Contraseña nueva</label>
        <div className="group relative">
          <KeyRound className={FIELD_ICON} aria-hidden="true" />
          <FxPassword
            inputId={id("new_password")}
            name="new_password"
            autoComplete="new-password"
            inputRef={refs.new_password}
            inputClassName={INPUT}
            value={values.new_password}
            onChange={(e) => onChange("new_password", e.target.value)}
            {...a11y("new_password", rulesId)}
          />
        </div>
        <FieldError id={`${id("new_password")}-error`} message={errors.new_password} />
        <ul id={rulesId} aria-live="polite" aria-label="Requisitos de la contraseña nueva" className="mt-2 space-y-1">
          {rules.map((r) => (
            <li
              key={r.id}
              className={cn(
                "flex items-center gap-1.5 text-fx-body-sm transition-colors duration-fx-fast ease-fx",
                r.ok ? "text-fx-success" : "text-fx-text-2",
              )}
            >
              {r.ok
                ? <Check className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                : <Circle className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />}
              <span>{r.label}</span>
              <span className="sr-only">{r.ok ? ": cumplido" : ": pendiente"}</span>
            </li>
          ))}
        </ul>
      </div>

      <div>
        <label htmlFor={id("new_password_confirmation")} className={LABEL}>Repetir contraseña nueva</label>
        <div className="group relative">
          <KeyRound className={FIELD_ICON} aria-hidden="true" />
          <FxPassword
            inputId={id("new_password_confirmation")}
            name="new_password_confirmation"
            autoComplete="new-password"
            inputRef={refs.new_password_confirmation}
            inputClassName={INPUT}
            value={values.new_password_confirmation}
            onChange={(e) => onChange("new_password_confirmation", e.target.value)}
            {...a11y("new_password_confirmation")}
          />
        </div>
        <FieldError id={`${id("new_password_confirmation")}-error`} message={errors.new_password_confirmation} />
      </div>
    </div>
  );
}
