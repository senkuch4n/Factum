"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { Dialog } from "primereact/dialog";
import { InputText } from "primereact/inputtext";
import { Button } from "primereact/button";
import { Message } from "primereact/message";
import { AlertTriangle, Hash, LogIn, Lock, Square, User } from "lucide-react";
import { api } from "@/lib/api";
import { useInPlaceState } from "@/lib/session";
import { useAuthMode } from "@/hooks/useAuthMode";
import { FxPassword } from "@/components/form/FxPassword";
import { FxBanner } from "@/components/feedback/FxBanner";
import { ElapsedTime } from "@/components/capture/ElapsedTime";
import { FIELD_ICON, FieldError, INPUT, LABEL } from "@/components/login/LoginForm";
import {
  describeLoginError, validateLogin, type LoginField, type LoginFieldErrors,
} from "@/components/login/login-errors";

export type ReloginMode = "expired" | "early";

interface ReloginDialogProps {
  visible: boolean;
  mode: ReloginMode;
  /** DNI de la sesión (solo lectura). Lo fija `SessionManager` al abrir. */
  dni: string;
  /** Tope configurado en segundos (texto del modo `early`), si se conoce. */
  maxAgeSeconds: number | null;
  /** Otra pestaña inició sesión con otro DNI (D8). */
  otherUser: boolean;
  /** Solo `early`: "Ahora no" / Escape / X (el token sigue vigente). */
  onDismiss: () => void;
  /** Login correcto con el mismo usuario: `SessionManager` cierra y avisa "restored". */
  onSuccess: () => void;
  /** "Salir": descarta el asistente y va al login. */
  onExit: () => void;
}

const TITLE_ID = "relogin-title";
const DESC_ID = "relogin-desc";

const HOURS = new Intl.NumberFormat("es-AR", { maximumFractionDigits: 1 });

/**
 * Re-login en el lugar (sesion-larga §8.8). Modal de Prime encima del
 * asistente: no navega, así el paso, los textos, la bandeja y los Blobs quedan
 * como estaban. Reusa la validación y los mensajes del login. Prime atrapa el
 * foco y lo devuelve al cerrar al elemento que lo tenía.
 */
export function ReloginDialog({
  visible, mode, dni, maxAgeSeconds, otherUser, onDismiss, onSuccess, onExit,
}: ReloginDialogProps) {
  const router = useRouter();
  const { mode: authMode } = useAuthMode();
  const showUsername = authMode === "dev" || authMode === "external";
  const { pendingCount, recording } = useInPlaceState();

  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [fieldErrors, setFieldErrors] = useState<LoginFieldErrors>({});
  const [serverError, setServerError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const inFlight = useRef(false);
  const usernameRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);

  // Cada apertura arranca limpia (la contraseña nunca queda en memoria entre aperturas).
  useEffect(() => {
    if (!visible) return;
    setUsername("");
    setPassword("");
    setFieldErrors({});
    setServerError(null);
    setLoading(false);
    inFlight.current = false;
  }, [visible]);

  // Foco inicial en la contraseña (HU), también en `dev`/`external`: el usuario queda a un Shift+Tab.
  function focusPassword() {
    passwordRef.current?.focus();
  }

  async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (inFlight.current) return;
    const errors = validateLogin({ dni, username, password }, { requireUsername: showUsername });
    const first = (["username", "password"] as LoginField[]).find(f => errors[f]);
    if (first) {
      setFieldErrors(errors);
      setServerError(null);
      (first === "username" ? usernameRef : passwordRef).current?.focus();
      return;
    }
    inFlight.current = true;
    setLoading(true);
    setServerError(null);
    try {
      const { user } = await api.login(dni, showUsername ? username : "", password);
      setPassword("");
      if (user.must_change_password) {
        // Caso raro (le resetearon la contraseña mientras trabajaba): sale del asistente.
        router.replace("/cambiar-contrasena");
        return;
      }
      onSuccess();
    } catch (err) {
      const { kind, message } = describeLoginError(err, authMode);
      setServerError(message);
      if (kind === "credentials") {
        setPassword("");
        passwordRef.current?.focus();
      }
    } finally {
      setLoading(false);
      inFlight.current = false;
    }
  }

  const fieldA11y = (field: LoginField) => ({
    invalid: !!fieldErrors[field],
    "aria-invalid": !!fieldErrors[field],
    "aria-describedby": fieldErrors[field] ? `relogin-${field}-error` : undefined,
    "aria-required": true,
    readOnly: loading,
  });

  const early = mode === "early";
  const hours = maxAgeSeconds && maxAgeSeconds > 0 ? HOURS.format(maxAgeSeconds / 3600) : null;
  const title = early ? "Volver a iniciar sesión" : "Tu sesión venció";
  const description = early
    ? `Iniciá sesión ahora para empezar un nuevo período${hours ? ` de ${hours} h` : ""}.`
    : "Iniciá sesión para seguir. Lo que cargaste en esta inspección no se perdió.";

  return (
    <Dialog
      visible={visible}
      header={<span id={TITLE_ID}>{title}</span>}
      modal
      draggable={false}
      resizable={false}
      // `expired`: sin X ni Escape (el token ya no sirve). `early`: se puede cerrar.
      closable={early}
      closeOnEscape={early}
      onHide={() => { if (early && !loading) onDismiss(); }}
      onShow={focusPassword}
      pt={{
        root: {
          className: "w-[min(28rem,100%)] max-sm:w-full max-sm:max-h-full max-sm:rounded-none max-sm:border-0",
          "aria-labelledby": TITLE_ID,
          "aria-describedby": DESC_ID,
        },
        mask: { className: "max-sm:p-0" },
      }}
    >
      <p id={DESC_ID} className="m-0 mb-4 text-fx-body-sm text-fx-text-2">{description}</p>

      {recording && (
        <div
          role="group"
          aria-label="Grabación en curso"
          className="mb-4 flex flex-col gap-3 rounded-fx-lg border border-fx-border bg-fx-danger-soft p-3 sm:flex-row sm:items-center"
        >
          <div className="min-w-0 flex-1">
            <p className="m-0 flex items-center gap-2 text-fx-body-sm font-semibold text-fx-text">
              <span aria-hidden="true" className="h-2 w-2 shrink-0 rounded-full bg-fx-danger motion-safe:animate-pulse" />
              Grabando · <ElapsedTime since={recording.startedAt} />
            </p>
            <p className="m-0 mt-1 text-fx-body-sm text-fx-text-2">
              La grabación sigue en la PC. Podés detenerla sin iniciar sesión.
            </p>
          </div>
          <Button
            type="button"
            severity="danger"
            outlined
            size="small"
            className="min-h-11 shrink-0 sm:min-h-9"
            icon={<Square className="h-3.5 w-3.5" aria-hidden="true" />}
            label={recording.stopping ? "Deteniendo…" : "Detener grabación"}
            loading={recording.stopping}
            disabled={recording.stopping}
            onClick={recording.onStop}
          />
        </div>
      )}

      {otherUser && (
        <FxBanner tone="warn" className="mb-4">
          Se inició sesión con otro usuario en otra pestaña. Para seguir con esta inspección, iniciá sesión con tu
          usuario o tocá Salir.
        </FxBanner>
      )}

      {pendingCount > 0 && (
        <FxBanner tone="info" className="mb-4">
          {pendingCount === 1
            ? "Hay 1 archivo pendiente de registrar: se va a registrar cuando inicies sesión."
            : `Hay ${pendingCount} archivos pendientes de registrar: se van a registrar cuando inicies sesión.`}
        </FxBanner>
      )}

      <form noValidate onSubmit={handleSubmit} className="space-y-4">
        <div>
          <label htmlFor="relogin-dni" className={LABEL}>DNI</label>
          <div className="group relative">
            <Hash className={FIELD_ICON} aria-hidden="true" />
            <InputText
              id="relogin-dni"
              name="dni"
              // Igual que el login: en `local` el DNI es el identificador del gestor de contraseñas.
              autoComplete={showUsername ? "off" : "username"}
              className={`${INPUT} cursor-default bg-fx-surface-1 text-fx-text-2`}
              value={dni}
              readOnly
              aria-readonly
            />
          </div>
        </div>

        {showUsername && (
          <div>
            <label htmlFor="relogin-username" className={LABEL}>Usuario</label>
            <div className="group relative">
              <User className={FIELD_ICON} aria-hidden="true" />
              <InputText
                ref={usernameRef}
                id="relogin-username"
                name="username"
                autoComplete="username"
                autoCapitalize="none"
                spellCheck={false}
                maxLength={50}
                className={INPUT}
                value={username}
                onChange={e => {
                  setUsername(e.target.value);
                  setServerError(null);
                  setFieldErrors(fe => (fe.username ? { ...fe, username: undefined } : fe));
                }}
                {...fieldA11y("username")}
              />
            </div>
            <FieldError id="relogin-username-error" message={fieldErrors.username} />
          </div>
        )}

        <div>
          <label htmlFor="relogin-password" className={LABEL}>Contraseña</label>
          <div className="group relative">
            <Lock className={FIELD_ICON} aria-hidden="true" />
            <FxPassword
              inputId="relogin-password"
              name="password"
              autoComplete="current-password"
              inputRef={passwordRef}
              inputClassName={INPUT}
              value={password}
              onChange={e => {
                setPassword(e.target.value);
                setServerError(null);
                setFieldErrors(fe => (fe.password ? { ...fe, password: undefined } : fe));
              }}
              {...fieldA11y("password")}
            />
          </div>
          <FieldError id="relogin-password-error" message={fieldErrors.password} />
        </div>

        {serverError && (
          <Message
            severity="error"
            icon={<AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}
            text={serverError}
            className="w-full"
          />
        )}

        <Button
          type="submit"
          className="w-full"
          icon={<LogIn className="h-4 w-4" aria-hidden="true" />}
          label={loading ? "Verificando…" : "Iniciar sesión"}
          loading={loading}
        />

        <div className="flex flex-wrap items-center justify-between gap-2">
          <Button type="button" severity="secondary" text label="Salir" onClick={onExit} disabled={loading} />
          {early && (
            <Button type="button" severity="secondary" outlined label="Ahora no" onClick={onDismiss} disabled={loading} />
          )}
        </div>
      </form>
    </Dialog>
  );
}
