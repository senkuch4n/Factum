import { ApiError, type ChangePasswordField, type ChangePasswordRequest } from "@/lib/api";

/**
 * Política de contraseñas en el cliente (usuarios-locales §9.6). Pura: sin
 * React. Mismos textos y mismo orden que `PasswordPolicy.cs` (§8.3), así la
 * validación en vivo y la del servidor dicen lo mismo. Se mide con `.length`
 * (UTF-16, igual que `string.Length` en C#) y sin trim.
 */

export type PasswordChangeValues = ChangePasswordRequest;
export type PasswordChangeErrors = Partial<Record<ChangePasswordField, string>>;

export interface PasswordPolicyContext {
  dni: string;
  minLength: number;
  maxLength: number;
}

export interface PasswordRule {
  id: "min_length" | "max_length" | "no_dni";
  label: string;
  ok: boolean;
}

/** Orden de los campos en pantalla: define a cuál va el foco tras validar. */
export const PASSWORD_FIELD_ORDER: readonly ChangePasswordField[] = [
  "current_password",
  "new_password",
  "new_password_confirmation",
];

export const PASSWORD_MESSAGES = {
  password_mismatch: "Las contraseñas nuevas no coinciden.",
  password_too_short: (min: number) => `La contraseña nueva tiene que tener al menos ${min} caracteres.`,
  password_too_long: (max: number) => `La contraseña nueva puede tener hasta ${max} caracteres.`,
  password_same_as_current: "La contraseña nueva tiene que ser distinta de la actual.",
  password_contains_dni: "La contraseña nueva no puede contener tu DNI.",
  invalid_current_password: "La contraseña actual no es correcta.",
} as const;

function containsDni(pwd: string, dni: string): boolean {
  return dni.length > 0 && pwd.includes(dni);
}

/** Reglas visibles bajo "Contraseña nueva", marcadas en vivo. */
export function passwordRules(newPwd: string, { dni, minLength, maxLength }: PasswordPolicyContext): PasswordRule[] {
  return [
    { id: "min_length", label: `Al menos ${minLength} caracteres`, ok: newPwd.length >= minLength },
    { id: "max_length", label: `Hasta ${maxLength} caracteres`, ok: newPwd.length <= maxLength },
    { id: "no_dni", label: "No contiene tu DNI", ok: newPwd.length > 0 && !containsDni(newPwd, dni) },
  ];
}

/**
 * Errores por campo. Requeridos primero; después la política de la nueva en
 * el orden del backend (mismatch → too_short → too_long → contains_dni) y por
 * último `same_as_current`. Un solo error para la nueva (el primero que falla).
 */
export function validatePasswordChange(
  values: PasswordChangeValues,
  { dni, minLength, maxLength }: PasswordPolicyContext,
): PasswordChangeErrors {
  const errors: PasswordChangeErrors = {};
  const { current_password: current, new_password: next, new_password_confirmation: confirmation } = values;

  if (!current) errors.current_password = "Ingresá tu contraseña actual.";
  if (!next) errors.new_password = "Ingresá la contraseña nueva.";
  if (!confirmation) errors.new_password_confirmation = "Repetí la contraseña nueva.";

  if (next && confirmation && next !== confirmation) {
    errors.new_password_confirmation = PASSWORD_MESSAGES.password_mismatch;
  }
  if (next) {
    if (next.length < minLength) errors.new_password = PASSWORD_MESSAGES.password_too_short(minLength);
    else if (next.length > maxLength) errors.new_password = PASSWORD_MESSAGES.password_too_long(maxLength);
    else if (containsDni(next, dni)) errors.new_password = PASSWORD_MESSAGES.password_contains_dni;
    else if (current && current === next) errors.new_password = PASSWORD_MESSAGES.password_same_as_current;
  }
  return errors;
}

export interface ChangePasswordErrorDescription {
  /** Si viene, el error va en ese campo; si no, al mensaje general. */
  field?: ChangePasswordField;
  message: string;
}

const NETWORK_MESSAGE = "No pudimos conectar con el servidor de Factum. Revisá tu conexión e intentá de nuevo.";
const FIELD_CODES = new Set([
  "password_mismatch", "password_too_short", "password_too_long",
  "password_same_as_current", "password_contains_dni", "invalid_current_password",
]);
/* Sin el `error` del servidor (no debería pasar): textos sin el número. */
const FIELD_FALLBACKS: Record<string, string> = {
  password_mismatch: PASSWORD_MESSAGES.password_mismatch,
  password_same_as_current: PASSWORD_MESSAGES.password_same_as_current,
  password_contains_dni: PASSWORD_MESSAGES.password_contains_dni,
  invalid_current_password: PASSWORD_MESSAGES.invalid_current_password,
};
const VALID_FIELDS = new Set<string>(PASSWORD_FIELD_ORDER);
/** `ApiErrorBody.field` también admite los campos del panel de cuentas (abm-clientes): acá solo valen los de la contraseña. */
const isPasswordField = (f: string | undefined): f is ChangePasswordField => !!f && VALID_FIELDS.has(f);

/** Traduce un error de `api.changePassword` a campo + mensaje (§8.2). */
export function describeChangePasswordError(err: unknown): ChangePasswordErrorDescription {
  if (err instanceof ApiError) {
    if (err.status === 0) return { message: NETWORK_MESSAGE };
    const code = err.code;
    const field = err.body?.field;
    if (code && FIELD_CODES.has(code)) {
      const target: ChangePasswordField =
        isPasswordField(field)
          ? field
          : code === "invalid_current_password"
            ? "current_password"
            : code === "password_mismatch"
              ? "new_password_confirmation"
              : "new_password";
      const fallback = FIELD_FALLBACKS[code] ?? "La contraseña nueva no cumple la política.";
      return { field: target, message: err.serverMessage ?? fallback };
    }
    if (code === "account_locked") {
      return { message: err.serverMessage ?? "Demasiados intentos fallidos. Probá de nuevo en unos minutos." };
    }
    if (err.status >= 500) {
      return { message: "El servidor no respondió. Intentá de nuevo en unos minutos." };
    }
    return { message: "No se pudo cambiar la contraseña. Intentá de nuevo." };
  }
  return { message: NETWORK_MESSAGE };
}
