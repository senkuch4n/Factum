"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { InputText } from "primereact/inputtext";
import { Button } from "primereact/button";
import { Message } from "primereact/message";
import { AlertCircle, AlertTriangle, Hash, Lock, ShieldCheck, User } from "lucide-react";
import { api } from "@/lib/api";
import { FxPassword } from "@/components/form/FxPassword";
import { AgentStatusChip } from "./AgentStatusChip";
import {
  describeLoginError,
  LOGIN_FIELD_ORDER,
  sanitizeDni,
  validateLogin,
  type LoginField,
  type LoginFieldErrors,
  type LoginValues,
} from "./login-errors";

type FocusTarget = LoginField | "submit";

const LABEL = "mb-1.5 block text-fx-body-sm font-semibold text-fx-text-2";
const FIELD_ICON =
  "pointer-events-none absolute left-3.5 top-1/2 z-[1] h-4 w-4 -translate-y-1/2 text-fx-text-3 transition-colors duration-fx-fast ease-fx group-focus-within:text-fx-accent-text";
/* `pl-10`: lugar para el ícono. Le gana al px-3 del pt global de inputtext
   porque ese pt reaplica la clase del consumidor al final. */
const INPUT = "h-12 pl-10";

function FieldError({ id, message }: { id: string; message?: string }) {
  if (!message) return null;
  return (
    <p id={id} className="mt-1.5 flex items-center gap-1.5 text-fx-body-sm text-fx-danger">
      <AlertCircle className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
      {message}
    </p>
  );
}

/**
 * Formulario de acceso. Valida en cliente (requeridos + DNI de 7 u 8
 * dígitos), traduce los errores a mensajes propios y deja el foco donde sirve
 * para reintentar. El envío es el de siempre: `api.login` guarda
 * `factum_token` y se navega a /dashboard.
 */
export function LoginForm() {
  const router = useRouter();
  const [values, setValues] = useState<LoginValues>({ dni: "", username: "", password: "" });
  const [fieldErrors, setFieldErrors] = useState<LoginFieldErrors>({});
  const [serverError, setServerError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  // `n` fuerza el efecto aunque el destino se repita (dos errores seguidos).
  const [focusRequest, setFocusRequest] = useState<{ target: FocusTarget; n: number } | null>(null);

  const inFlight = useRef(false);
  const formRef = useRef<HTMLFormElement>(null);
  const dniRef = useRef<HTMLInputElement>(null);
  const usernameRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);

  /* El foco se mueve después del render en que el botón se rehabilita: un
     botón disabled no recibe foco. */
  useEffect(() => {
    if (!focusRequest || loading) return;
    const { target } = focusRequest;
    const el =
      target === "dni"
        ? dniRef.current
        : target === "username"
          ? usernameRef.current
          : target === "password"
            ? passwordRef.current
            : formRef.current?.querySelector<HTMLButtonElement>('button[type="submit"]');
    el?.focus();
  }, [focusRequest, loading]);

  function requestFocus(target: FocusTarget) {
    setFocusRequest((prev) => ({ target, n: (prev?.n ?? 0) + 1 }));
  }

  function update(field: LoginField, value: string) {
    setValues((v) => ({ ...v, [field]: value }));
    setServerError(null);
    setFieldErrors((e) => (e[field] ? { ...e, [field]: undefined } : e));
  }

  async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (inFlight.current) return;

    const errors = validateLogin(values);
    const firstInvalid = LOGIN_FIELD_ORDER.find((f) => errors[f]);
    if (firstInvalid) {
      setFieldErrors(errors);
      setServerError(null);
      requestFocus(firstInvalid);
      return;
    }

    inFlight.current = true;
    setLoading(true);
    setServerError(null);
    try {
      await api.login(values.dni, values.username, values.password);
      // Sin resetear `loading`: el botón queda en "Verificando…" mientras navega.
      router.push("/dashboard");
    } catch (err) {
      const { kind, message } = describeLoginError(err);
      setServerError(message);
      setLoading(false);
      inFlight.current = false;
      if (kind === "credentials") {
        setValues((v) => ({ ...v, password: "" }));
        requestFocus("password");
      } else {
        requestFocus("submit");
      }
    }
  }

  const fieldA11y = (field: LoginField) => ({
    invalid: !!fieldErrors[field],
    "aria-invalid": !!fieldErrors[field],
    "aria-describedby": fieldErrors[field] ? `${field}-error` : undefined,
    "aria-required": true,
    readOnly: loading,
  });

  return (
    <div>
      <p className="text-fx-label uppercase text-fx-accent-text">Acceso</p>
      <h1 className="mt-2 text-fx-h1 text-fx-text">Iniciar sesión</h1>
      <p className="mt-2 text-fx-body-sm text-fx-text-2">Ingresá con tus credenciales.</p>

      <AgentStatusChip className="mt-6" />

      <form ref={formRef} noValidate onSubmit={handleSubmit} className="mt-6 space-y-5">
        <div>
          <label htmlFor="dni" className={LABEL}>DNI</label>
          <div className="group relative">
            <Hash className={FIELD_ICON} aria-hidden="true" />
            <InputText
              ref={dniRef}
              id="dni"
              name="dni"
              inputMode="numeric"
              autoComplete="off"
              placeholder="12345678"
              className={INPUT}
              value={values.dni}
              onChange={(e) => update("dni", sanitizeDni(e.target.value))}
              {...fieldA11y("dni")}
            />
          </div>
          <FieldError id="dni-error" message={fieldErrors.dni} />
        </div>

        <div>
          <label htmlFor="username" className={LABEL}>Usuario</label>
          <div className="group relative">
            <User className={FIELD_ICON} aria-hidden="true" />
            <InputText
              ref={usernameRef}
              id="username"
              name="username"
              autoComplete="username"
              autoCapitalize="none"
              spellCheck={false}
              maxLength={50}
              placeholder="jperez"
              className={INPUT}
              value={values.username}
              onChange={(e) => update("username", e.target.value)}
              {...fieldA11y("username")}
            />
          </div>
          <FieldError id="username-error" message={fieldErrors.username} />
        </div>

        <div>
          <label htmlFor="password" className={LABEL}>Contraseña</label>
          <div className="group relative">
            <Lock className={FIELD_ICON} aria-hidden="true" />
            <FxPassword
              inputId="password"
              name="password"
              autoComplete="current-password"
              placeholder="••••••••"
              inputRef={passwordRef}
              inputClassName={INPUT}
              value={values.password}
              onChange={(e) => update("password", e.target.value)}
              {...fieldA11y("password")}
            />
          </div>
          <FieldError id="password-error" message={fieldErrors.password} />
        </div>

        {serverError && (
          <Message
            severity="error"
            icon={<AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}
            text={serverError}
            className="w-full motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)]"
          />
        )}

        <Button
          type="submit"
          size="large"
          className="mt-2 w-full"
          icon={<ShieldCheck className="h-5 w-5" aria-hidden="true" />}
          label={loading ? "Verificando credenciales…" : "Ingresar a Factum"}
          loading={loading}
        />
      </form>
    </div>
  );
}
