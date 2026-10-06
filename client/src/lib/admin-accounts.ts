/**
 * Reglas y textos del panel de cuentas (abm-clientes, SDD §9.3). Puro, sin
 * React. Los textos de `ADMIN_MESSAGES` y las reglas de `validateAccountField`
 * son los mismos del backend (`Services/Admin/AdminErrors.cs` y
 * `AccountFieldRules.cs`, SDD §6.4 y §8.3): si cambian allá, cambian acá.
 */

import { ApiError, SERVER_UNREACHABLE } from "@/lib/api";
import type {
  AdminAction, AdminCreateUserRequest, AdminUser, AdminUserField, AdminUserStatus, UserRole,
} from "@/types";

/* ── Textos (§8.3, idénticos al backend) ── */

export const ADMIN_MESSAGES = {
  not_available: "La administración de cuentas no está disponible en este modo.",
  user_not_found: "No existe esa cuenta.",
  dni_taken: "Ya existe una cuenta con ese DNI.",
  stale_update: "Otro superadmin modificó esta cuenta. Recargá para ver los cambios.",
  cannot_act_on_self:
    "No podés hacer esto sobre tu propia cuenta. Para cambiar tu contraseña usá \"Cambiar contraseña\".",
  last_superadmin: "No se puede: el sistema tiene que tener al menos un superadmin activo.",
  invalid_state_suspend: "La cuenta ya está suspendida.",
  invalid_state_reactivate: "La cuenta ya está activa.",
  invalid_state_unlock: "La cuenta no está bloqueada.",
  operation_busy: "Otra operación sobre los superadmins está en curso. Probá de nuevo.",
  dni: "El DNI tiene que tener 7 u 8 dígitos.",
  name_required: "El nombre es obligatorio.",
  name_long: "El nombre puede tener hasta 120 caracteres.",
  sigla: "La sigla puede tener hasta 30 caracteres.",
  contact_phone: "Ingresá un teléfono válido: números, espacios, guiones, paréntesis y un + al inicio.",
  contact_email: "Ingresá un email válido.",
  organization: "La organización puede tener hasta 120 caracteres.",
  notes: "Las notas pueden tener hasta 1000 caracteres.",
  reason: "El motivo puede tener hasta 300 caracteres.",
  expected_updated_at: "Falta la versión de la cuenta. Recargá e intentá de nuevo.",
} as const;

/** Mensaje genérico cuando la API no trae un texto propio. */
const GENERIC_ERROR = "No se pudo completar la acción. Intentá de nuevo.";

/* ── Límites (§6.4) ── */

export const ACCOUNT_LIMITS = {
  name: 120,
  sigla: 30,
  contact_phone: 40,
  contact_email: 254,
  organization: 120,
  notes: 1000,
  reason: 300,
} as const;

const DNI_RE = /^[0-9]{7,8}$/;
const PHONE_RE = /^\+?[0-9][0-9 ()\-.]*$/;
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Campos que se validan en el formulario (todos los del alta). */
export type AccountFormField = keyof AdminCreateUserRequest;

/** Estado del formulario de alta/edición (en edición el `dni` no se manda). */
export type AccountFormValues = AdminCreateUserRequest;

/** Orden de los campos en pantalla: define a cuál va el foco ante varios errores. */
export const ACCOUNT_FORM_FIELDS: readonly AccountFormField[] = [
  "dni", "name", "sigla", "contact_phone", "contact_email", "organization", "notes",
];

export const EMPTY_ACCOUNT_FORM: AccountFormValues = {
  dni: "", name: "", sigla: "", contact_phone: "", contact_email: "", organization: "", notes: "",
};

/** Formulario a partir de una cuenta existente (edición). */
export function accountToForm(u: AdminUser): AccountFormValues {
  return {
    dni: u.dni,
    name: u.name,
    sigla: u.sigla,
    contact_phone: u.contact_phone,
    contact_email: u.contact_email,
    organization: u.organization,
    notes: u.notes,
  };
}

/** Trim de todos los campos (lo mismo que hace el backend antes de validar y guardar). */
export function trimAccountForm(f: AccountFormValues): AccountFormValues {
  return {
    dni: f.dni.trim(),
    name: f.name.trim(),
    sigla: f.sigla.trim(),
    contact_phone: f.contact_phone.trim(),
    contact_email: f.contact_email.trim(),
    organization: f.organization.trim(),
    notes: f.notes.trim(),
  };
}

/** `true` si los dos formularios son iguales después del trim. */
export function sameAccountForm(a: AccountFormValues, b: AccountFormValues): boolean {
  const ta = trimAccountForm(a);
  const tb = trimAccountForm(b);
  return ACCOUNT_FORM_FIELDS.every(k => ta[k] === tb[k]);
}

function countDigits(s: string): number {
  let n = 0;
  for (const ch of s) if (ch >= "0" && ch <= "9") n++;
  return n;
}

/**
 * Error de un campo, o `null`. Trim antes de medir (`string.Length` en C# =
 * `.length` en JS). Mismas reglas que `AccountFieldRules`.
 */
export function validateAccountField(field: AdminUserField, raw: string | null | undefined): string | null {
  const v = (raw ?? "").trim();
  switch (field) {
    case "dni":
      return DNI_RE.test(v) ? null : ADMIN_MESSAGES.dni;
    case "name":
      if (v.length === 0) return ADMIN_MESSAGES.name_required;
      return v.length > ACCOUNT_LIMITS.name ? ADMIN_MESSAGES.name_long : null;
    case "sigla":
      return v.length > ACCOUNT_LIMITS.sigla ? ADMIN_MESSAGES.sigla : null;
    case "contact_phone": {
      if (v === "") return null;
      const digits = countDigits(v);
      const ok = PHONE_RE.test(v) && digits >= 6 && digits <= 20 && v.length <= ACCOUNT_LIMITS.contact_phone;
      return ok ? null : ADMIN_MESSAGES.contact_phone;
    }
    case "contact_email":
      if (v === "") return null;
      return EMAIL_RE.test(v) && v.length <= ACCOUNT_LIMITS.contact_email ? null : ADMIN_MESSAGES.contact_email;
    case "organization":
      return v.length > ACCOUNT_LIMITS.organization ? ADMIN_MESSAGES.organization : null;
    case "notes":
      return v.length > ACCOUNT_LIMITS.notes ? ADMIN_MESSAGES.notes : null;
    case "reason":
      return v.length > ACCOUNT_LIMITS.reason ? ADMIN_MESSAGES.reason : null;
    case "expected_updated_at":
      return v === "" ? ADMIN_MESSAGES.expected_updated_at : null;
  }
}

/** Errores del formulario completo. En edición el DNI no se valida (no se edita ni se manda). */
export function validateAccountForm(
  form: AccountFormValues, mode: "create" | "edit",
): Partial<Record<AdminUserField, string>> {
  const errors: Partial<Record<AdminUserField, string>> = {};
  for (const field of ACCOUNT_FORM_FIELDS) {
    if (field === "dni" && mode === "edit") continue;
    const err = validateAccountField(field, form[field]);
    if (err) errors[field] = err;
  }
  return errors;
}

/* ── Búsqueda y filtros (D8: en el cliente) ── */

// Constructor y no literal: el `target` del tsconfig (ES2017) no admite `\p{…}` en literales.
const COMBINING_MARKS = new RegExp("\\p{M}", "gu");

/**
 * Normaliza para buscar: NFD, sin marcas diacríticas, minúscula y trim. Mismo
 * criterio que `normalizeCatalogKey` de `lib/catalogs.ts`, sin colapsar los
 * espacios internos (acá no se deduplica, solo se filtra). "  Pérez " → "perez".
 */
export function normalizeSearch(s: string | null | undefined): string {
  return (s ?? "").normalize("NFD").replace(COMBINING_MARKS, "").normalize("NFC").toLowerCase().trim();
}

export type StatusFilter = "all" | AdminUserStatus;
export type RoleFilter = "all" | UserRole;

export interface AccountFilters {
  q: string;
  status: StatusFilter;
  role: RoleFilter;
}

export const EMPTY_FILTERS: AccountFilters = { q: "", status: "all", role: "all" };

export function hasActiveFilters(f: AccountFilters): boolean {
  return f.q.trim() !== "" || f.status !== "all" || f.role !== "all";
}

/** `q` se busca en el nombre (sin tildes ni mayúsculas) o en el DNI. */
export function matchesFilters(user: AdminUser, f: AccountFilters): boolean {
  if (f.status !== "all" && user.status !== f.status) return false;
  if (f.role !== "all" && user.role !== f.role) return false;
  const q = normalizeSearch(f.q);
  if (!q) return true;
  return normalizeSearch(user.name).includes(q) || user.dni.includes(q);
}

/** Orden del listado: por nombre, como el backend (`localeCompare` en español). */
export function sortAccounts(users: AdminUser[]): AdminUser[] {
  return [...users].sort((a, b) => a.name.localeCompare(b.name, "es", { sensitivity: "base" }));
}

/* ── Estado visible de la cuenta ── */

export type AccountState = "suspendida" | "bloqueada" | "pendiente" | "activa";

/** Prioridad: suspendida > bloqueada > pendiente de primer ingreso > activa. */
export function accountState(user: AdminUser): AccountState {
  if (user.status === "suspendido") return "suspendida";
  if (user.locked_until !== null) return "bloqueada";
  if (user.must_change_password) return "pendiente";
  return "activa";
}

/* ── Etiquetas ── */

export const ACTION_LABELS: Record<AdminAction, string> = {
  create: "Alta",
  update: "Edición",
  suspend: "Suspensión",
  reactivate: "Reactivación",
  reset_password: "Reset de contraseña",
  unlock: "Desbloqueo",
};

/** Nombre visible de cada campo del historial (`changes[].field` es el nombre JSON). */
export const FIELD_LABELS: Record<string, string> = {
  dni: "DNI",
  name: "Nombre",
  sigla: "Sigla",
  role: "Rol",
  contact_phone: "Teléfono",
  contact_email: "Email de contacto",
  organization: "Organización",
  notes: "Notas internas",
};

export const ROLE_LABELS: Record<UserRole, string> = {
  superadmin: "Superadmin",
  cliente: "Cliente",
};

/** Valor de un cambio del historial: `role` con su etiqueta, vacío como "(vacío)". */
export function formatChangeValue(field: string, value: string | null): string {
  if (value === null || value === "") return "(vacío)";
  if (field === "role" && (value === "superadmin" || value === "cliente")) return ROLE_LABELS[value];
  return value;
}

/* ── Fechas (es-AR) ── */

const RELATIVE = new Intl.RelativeTimeFormat("es-AR", { numeric: "auto" });
const DATE_TIME = new Intl.DateTimeFormat("es-AR", {
  day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit",
});
const TIME = new Intl.DateTimeFormat("es-AR", { hour: "2-digit", minute: "2-digit" });

/** "hace 3 días", "hace 5 minutos", "recién". */
export function formatRelative(iso: string, now: Date = new Date()): string {
  const t = new Date(iso).getTime();
  if (Number.isNaN(t)) return "";
  const diffSec = Math.round((t - now.getTime()) / 1000);
  const abs = Math.abs(diffSec);
  if (abs < 45) return "recién";
  const [unit, size]: [Intl.RelativeTimeFormatUnit, number] =
    abs < 3600 ? ["minute", 60]
    : abs < 86400 ? ["hour", 3600]
    : abs < 2592000 ? ["day", 86400]
    : abs < 31536000 ? ["month", 2592000]
    : ["year", 31536000];
  return RELATIVE.format(Math.round(diffSec / size), unit);
}

/** "06/10/2026, 14:32". */
export function formatDateTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "" : DATE_TIME.format(d);
}

/** "14:32" (para "Bloqueada hasta HH:MM"). */
export function formatClock(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "" : TIME.format(d);
}

/* ── Mensaje para el cliente (D7) ── */

export function buildClientMessage(origin: string, dni: string, temp: string): string {
  return `Tu cuenta de Factum está lista.\nIngresá en ${origin} con tu DNI ${dni} y la contraseña temporal ${temp}.\nTe va a pedir que la cambies en el primer ingreso.`;
}

/* ── Errores ── */

/** `code` del error, si es un `ApiError`. */
export function adminErrorCode(err: unknown): string | null {
  return err instanceof ApiError ? err.code : null;
}

/** `true` si el error significa "no hay panel para vos" (→ dashboard). */
export function isAdminAccessError(err: unknown): boolean {
  const code = adminErrorCode(err);
  return code === "superadmin_required" || code === "not_available";
}

/** Texto para mostrar: el de la API, o el de servidor caído, o uno genérico. */
export function adminErrorMessage(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 0) return SERVER_UNREACHABLE;
    if (err.serverMessage) return err.serverMessage;
  }
  return GENERIC_ERROR;
}
