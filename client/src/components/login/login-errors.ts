import { ApiError, type AuthMode } from "@/lib/api";

/**
 * Funciones puras del login: validación en cliente y traducción de errores a
 * mensajes en español. Sin React ni JSX. En `dev`/`external` el formulario
 * pide DNI + Usuario + Contraseña; en `local` (o mientras el modo no llegó),
 * solo DNI + Contraseña (usuarios-locales §9.5).
 */

export type LoginField = "dni" | "username" | "password";
export type LoginValues = Record<LoginField, string>;
export type LoginFieldErrors = Partial<Record<LoginField, string>>;

/** Orden de los campos en pantalla: define a cuál va el foco tras validar. */
export function loginFieldOrder(showUsername: boolean): readonly LoginField[] {
  return showUsername ? ["dni", "username", "password"] : ["dni", "password"];
}

const DNI_PATTERN = /^\d{7,8}$/;

export function validateLogin(
  { dni, username, password }: LoginValues,
  { requireUsername }: { requireUsername: boolean },
): LoginFieldErrors {
  const errors: LoginFieldErrors = {};
  if (!dni) errors.dni = "Ingresá tu DNI.";
  else if (!DNI_PATTERN.test(dni)) errors.dni = "El DNI tiene 7 u 8 dígitos.";
  if (requireUsername && !username.trim()) errors.username = "Ingresá tu usuario.";
  if (!password) errors.password = "Ingresá tu contraseña.";
  return errors;
}

/** Deja solo dígitos (pegar "12.345.678" queda "12345678") y corta a 8. */
export function sanitizeDni(value: string): string {
  return value.replace(/\D/g, "").slice(0, 8);
}

export type LoginErrorKind = "credentials" | "locked" | "suspended" | "server" | "network";

export interface LoginErrorDescription {
  kind: LoginErrorKind;
  message: string;
}

const CREDENTIALS_FALLBACK = "DNI, usuario o contraseña incorrectos.";
const LOCAL_CREDENTIALS_FALLBACK = "DNI o contraseña incorrectos.";
const LOCKED_FALLBACK = "Demasiados intentos fallidos. Probá de nuevo en unos minutos.";
const SUSPENDED_FALLBACK = "Tu cuenta está suspendida. Comunicate con Factum para reactivarla.";

export function describeLoginError(err: unknown, mode: AuthMode | null): LoginErrorDescription {
  if (err instanceof ApiError) {
    /* `account_locked` (429) y `account_suspended` (403) antes del bloque
       genérico 4xx: no son credenciales, y no se borra la contraseña. */
    if (err.code === "account_locked") {
      return { kind: "locked", message: err.serverMessage ?? LOCKED_FALLBACK };
    }
    if (err.code === "account_suspended") {
      return { kind: "suspended", message: err.serverMessage ?? SUSPENDED_FALLBACK };
    }
    const credentialsFallback = mode === "local" ? LOCAL_CREDENTIALS_FALLBACK : CREDENTIALS_FALLBACK;
    if (err.status === 401) {
      return { kind: "credentials", message: err.serverMessage ?? credentialsFallback };
    }
    if (err.status >= 400 && err.status < 500) {
      return { kind: "credentials", message: credentialsFallback };
    }
    if (err.status >= 500) {
      return {
        kind: "server",
        message: "El servicio de autenticación no respondió. Intentá de nuevo en unos minutos.",
      };
    }
  }
  return {
    kind: "network",
    message: "No pudimos conectar con el servidor de Factum. Revisá tu conexión e intentá de nuevo.",
  };
}
