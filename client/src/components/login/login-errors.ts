import { ApiError } from "@/lib/api";

/**
 * Funciones puras del login: validación en cliente y traducción de errores a
 * mensajes en español. Sin React ni JSX. El login no depende del modo de
 * autenticación del backend (`dev` / `external`): solo mira el status HTTP.
 */

export type LoginField = "dni" | "username" | "password";
export type LoginValues = Record<LoginField, string>;
export type LoginFieldErrors = Partial<Record<LoginField, string>>;

/** Orden de los campos en pantalla: define a cuál va el foco tras validar. */
export const LOGIN_FIELD_ORDER: readonly LoginField[] = ["dni", "username", "password"];

const DNI_PATTERN = /^\d{7,8}$/;

export function validateLogin({ dni, username, password }: LoginValues): LoginFieldErrors {
  const errors: LoginFieldErrors = {};
  if (!dni) errors.dni = "Ingresá tu DNI.";
  else if (!DNI_PATTERN.test(dni)) errors.dni = "El DNI tiene 7 u 8 dígitos.";
  if (!username.trim()) errors.username = "Ingresá tu usuario.";
  if (!password) errors.password = "Ingresá tu contraseña.";
  return errors;
}

/** Deja solo dígitos (pegar "12.345.678" queda "12345678") y corta a 8. */
export function sanitizeDni(value: string): string {
  return value.replace(/\D/g, "").slice(0, 8);
}

export type LoginErrorKind = "credentials" | "server" | "network";

export interface LoginErrorDescription {
  kind: LoginErrorKind;
  message: string;
}

const CREDENTIALS_FALLBACK = "DNI, usuario o contraseña incorrectos.";

export function describeLoginError(err: unknown): LoginErrorDescription {
  if (err instanceof ApiError) {
    if (err.status === 401) {
      return { kind: "credentials", message: err.serverMessage ?? CREDENTIALS_FALLBACK };
    }
    if (err.status >= 400 && err.status < 500) {
      return { kind: "credentials", message: CREDENTIALS_FALLBACK };
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
