/**
 * Reglas del informe pericial del lado del cliente: claves de obligatorios
 * (idénticas a `server/src/Factum.Backend/Services/Cases/CaseValidation.cs`,
 * SDD informe-pericial-de-parte §6.4), validación del paso "Causa" y del
 * perfil, checklist del paso "Generar" y precarga del formulario.
 */

import type { Case, CaseFormData, ProfileFormData, ReportTextsInput } from "@/types";

/* ── Claves de `missing` (§6.4) ─────────────────────────────────────── */

export const CASE_REQUIRED_KEYS = [
  "nombre_tribunal",
  "tipo_causa",
  "nro_referencia",
  "caratula",
  "parte_denunciante",
  "parte_denunciada",
  "fecha_intervencion",
  "nombre_proponente",
  "nombre_denunciante",
  "tipo_dispositivo",
] as const;

export type CaseRequiredKey = (typeof CASE_REQUIRED_KEYS)[number];

export const REPORT_TEXT_REQUIRED_KEYS = [
  "operaciones_realizadas",
  "aseguramiento_evidencia",
  "resultados",
  "valoracion_tecnica",
  "conclusiones",
] as const satisfies readonly (keyof ReportTextsInput)[];

export type MissingKey =
  | "perfil"
  | "perito"
  | CaseRequiredKey
  | "imei"
  | `report_texts.${(typeof REPORT_TEXT_REQUIRED_KEYS)[number]}`
  | "capture_roles.imei_modelo";

/** Campos del perfil que valida la tarjeta del paso 2 (claves de error `perfil.<campo>`). */
export const PROFILE_REQUIRED_KEYS = ["nombre", "matricula", "profesion", "caracter"] as const;
export type ProfileRequiredKey = (typeof PROFILE_REQUIRED_KEYS)[number];

/* ── Largos máximos (§6.4) ──────────────────────────────────────────── */

export const MAX_LEN_LINE = 300;
export const MAX_LEN_LONG = 500;
export const MAX_LEN_TEXT = 20_000;
export const MAX_LEN_PROFILE = 150;
export const MAX_LEN_MATRICULA = 60;

/* ── Mensajes "Ingresá …" (§8.2) ────────────────────────────────────── */

export const PROFILE_MESSAGES: Record<ProfileRequiredKey, string> = {
  nombre: "Ingresá tu nombre completo",
  matricula: "Ingresá tu matrícula",
  profesion: "Ingresá tu profesión",
  caracter: "Ingresá tu carácter",
};

export const CASE_MESSAGES: Record<CaseRequiredKey | "imei", string> = {
  nombre_tribunal: "Ingresá el tribunal",
  tipo_causa: "Ingresá el tipo de causa",
  nro_referencia: "Ingresá el número de causa",
  caratula: "Ingresá la carátula",
  parte_denunciante: "Ingresá la parte denunciante",
  parte_denunciada: "Ingresá la parte denunciada",
  fecha_intervencion: "Ingresá la fecha de intervención",
  nombre_proponente: "Ingresá el nombre de quien propone",
  nombre_denunciante: "Ingresá el titular del dispositivo",
  tipo_dispositivo: "Ingresá el tipo de dispositivo",
  imei: "Ingresá el IMEI",
};

/** Clave de error de un campo del perfil dentro del mapa de errores del paso 2. */
export const profileErrorKey = (k: ProfileRequiredKey) => `perfil.${k}`;

/* ── ids de los campos (para enfocar desde el checklist) ───────────── */

export const caseFieldId = (key: keyof CaseFormData | "imei") =>
  key === "imei" || key === "imeiOverride" ? "case-imei" : `case-${key}`;
export const profileFieldId = (key: keyof ProfileFormData) => `profile-${key}`;
export const reportFieldId = (key: keyof ReportTextsInput) => `report-${key}`;
export const CAPTURE_ROLES_FIELD_ID = "capture-roles-status";

/* ── Formularios vacíos ─────────────────────────────────────────────── */

export const EMPTY_CASE_FORM: CaseFormData = {
  nro_referencia: "",
  nombre_denunciante: "",
  dni_denunciante: "",
  nombre_tribunal: "",
  organismo_tribunal: "",
  sala_tribunal: "",
  integrantes_tribunal: "",
  tipo_causa: "",
  caratula: "",
  parte_denunciante: "",
  parte_denunciada: "",
  objeto_causa: "",
  ambito_causa: "",
  fecha_intervencion: "",
  nombre_proponente: "",
  profesion_proponente: "",
  matricula_proponente: "",
  tipo_dispositivo: "",
  linea_dispositivo: "",
  imeiOverride: "",
};

export const EMPTY_PROFILE_FORM: ProfileFormData = {
  nombre: "",
  matricula: "",
  profesion: "",
  caracter: "",
  tratamiento: "suscripto",
};

export const EMPTY_REPORT_TEXTS: ReportTextsInput = {
  objeto_informe: "",
  operaciones_realizadas: "",
  aseguramiento_evidencia: "",
  resultados: "",
  valoracion_tecnica: "",
  conclusiones: "",
  notas_tecnicas: "",
  reserva: "",
};

/* ── Helpers ────────────────────────────────────────────────────────── */

const blank = (v: string | null | undefined) => !v || v.trim().length === 0;

/** `yyyy-MM-dd` con una fecha calendario real. */
export function isValidIsoDate(v: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(v)) return false;
  const [y, m, d] = v.split("-").map(Number);
  const dt = new Date(y, m - 1, d);
  return dt.getFullYear() === y && dt.getMonth() === m - 1 && dt.getDate() === d;
}

/** Fecha local de hoy en `yyyy-MM-dd`. */
export function todayIso(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

/** El `operator` del agente parece un número de línea (iOS manda el número propio). */
export function isPhoneLike(operator: string | null | undefined): boolean {
  return !!operator && /^\+?[\d\s()-]{6,}$/.test(operator.trim());
}

export const isImeiMissing = (imei: string | null | undefined) =>
  blank(imei) || imei === "INGRESAR_MANUALMENTE";

/* ── Validación ─────────────────────────────────────────────────────── */

/** Errores del perfil, con claves `perfil.<campo>`. */
export function validateProfile(p: ProfileFormData): Record<string, string> {
  const err: Record<string, string> = {};
  for (const k of PROFILE_REQUIRED_KEYS) {
    if (blank(p[k])) err[profileErrorKey(k)] = PROFILE_MESSAGES[k];
  }
  return err;
}

/** Errores de la causa y el equipo, con las claves de §6.4. */
export function validateCaseForm(
  form: CaseFormData,
  opts: { imeiManual: boolean },
): Record<string, string> {
  const err: Record<string, string> = {};
  for (const k of CASE_REQUIRED_KEYS) {
    if (blank(form[k])) err[k] = CASE_MESSAGES[k];
  }
  if (!err.fecha_intervencion && !isValidIsoDate(form.fecha_intervencion.trim())) {
    err.fecha_intervencion = "Ingresá una fecha de intervención válida";
  }
  if (opts.imeiManual && blank(form.imeiOverride)) err.imei = CASE_MESSAGES.imei;
  return err;
}

/**
 * Orden visual de los campos del paso 2 (perfil → actuación → partes → equipo),
 * para enfocar el primer error.
 */
export const CASE_FORM_FOCUS_ORDER: string[] = [
  ...PROFILE_REQUIRED_KEYS.map(profileErrorKey),
  "nombre_tribunal",
  "tipo_causa",
  "nro_referencia",
  "caratula",
  "fecha_intervencion",
  "parte_denunciante",
  "parte_denunciada",
  "nombre_proponente",
  "nombre_denunciante",
  "tipo_dispositivo",
  "imei",
];

/** id del input que corresponde a una clave de error del paso 2. */
export function errorKeyToFieldId(key: string): string {
  if (key.startsWith("perfil.")) return profileFieldId(key.slice(7) as keyof ProfileFormData);
  return caseFieldId(key as keyof CaseFormData);
}

/* ── Checklist del paso "Generar" (§8.6) ───────────────────────────── */

export interface MissingRequirement {
  key: MissingKey;
  label: string;
  step: 2 | 3 | 4;
  fieldId: string;
}

const REQUIREMENT_META: Record<MissingKey, Omit<MissingRequirement, "key">> = {
  perfil:                  { label: "Tus datos de perito",        step: 2, fieldId: profileFieldId("nombre") },
  perito:                  { label: "Tus datos de perito",        step: 2, fieldId: profileFieldId("nombre") },
  nombre_tribunal:         { label: "Tribunal",                   step: 2, fieldId: caseFieldId("nombre_tribunal") },
  tipo_causa:              { label: "Tipo de causa",              step: 2, fieldId: caseFieldId("tipo_causa") },
  nro_referencia:          { label: "Número de causa / expediente", step: 2, fieldId: caseFieldId("nro_referencia") },
  caratula:                { label: "Carátula",                   step: 2, fieldId: caseFieldId("caratula") },
  parte_denunciante:       { label: "Parte denunciante",          step: 2, fieldId: caseFieldId("parte_denunciante") },
  parte_denunciada:        { label: "Parte denunciada",           step: 2, fieldId: caseFieldId("parte_denunciada") },
  fecha_intervencion:      { label: "Fecha de intervención",      step: 2, fieldId: caseFieldId("fecha_intervencion") },
  nombre_proponente:       { label: "Nombre de quien propone",    step: 2, fieldId: caseFieldId("nombre_proponente") },
  nombre_denunciante:      { label: "Titular del dispositivo",    step: 2, fieldId: caseFieldId("nombre_denunciante") },
  tipo_dispositivo:        { label: "Tipo de dispositivo",        step: 2, fieldId: caseFieldId("tipo_dispositivo") },
  imei:                    { label: "IMEI",                       step: 2, fieldId: caseFieldId("imei") },
  "report_texts.operaciones_realizadas":  { label: "Operaciones realizadas",       step: 4, fieldId: reportFieldId("operaciones_realizadas") },
  "report_texts.aseguramiento_evidencia": { label: "Aseguramiento de la evidencia", step: 4, fieldId: reportFieldId("aseguramiento_evidencia") },
  "report_texts.resultados":              { label: "Resultados",                   step: 4, fieldId: reportFieldId("resultados") },
  "report_texts.valoracion_tecnica":      { label: "Valoración técnica",           step: 4, fieldId: reportFieldId("valoracion_tecnica") },
  "report_texts.conclusiones":            { label: "Conclusiones",                 step: 4, fieldId: reportFieldId("conclusiones") },
  "capture_roles.imei_modelo": { label: "Una captura marcada como «IMEI y modelo»", step: 3, fieldId: CAPTURE_ROLES_FIELD_ID },
};

/** Metadatos (etiqueta, paso, campo) de una clave; `null` si el servidor manda una desconocida. */
export function describeMissing(key: string): MissingRequirement | null {
  const meta = REQUIREMENT_META[key as MissingKey];
  return meta ? { key: key as MissingKey, ...meta } : null;
}

/** Obligatorios que faltan para generar, en el orden de los pasos. */
export function getMissingRequirements(cas: Case): MissingRequirement[] {
  const keys: MissingKey[] = [];
  const p = cas.perito;
  if (!p || PROFILE_REQUIRED_KEYS.some(k => blank(p[k]))) keys.push("perito");
  for (const k of CASE_REQUIRED_KEYS) {
    const v = cas[k];
    if (blank(v) || (k === "fecha_intervencion" && !isValidIsoDate(v.trim()))) keys.push(k);
  }
  if (isImeiMissing(cas.device?.imei)) keys.push("imei");
  if (!(cas.capture_roles ?? []).some(r => r.role === "imei_modelo")) keys.push("capture_roles.imei_modelo");
  for (const k of REPORT_TEXT_REQUIRED_KEYS) {
    if (blank(cas.report_texts?.[k])) keys.push(`report_texts.${k}`);
  }
  return keys.map(k => ({ key: k, ...REQUIREMENT_META[k] }));
}

/* ── Precarga (§8.2) ────────────────────────────────────────────────── */

/**
 * Datos que se repiten entre causas (tribunal y proponente), tomados del caso
 * pericial más reciente. `historyCases` viene ordenado por `created_at` desc.
 */
export function prefillFromLastCase(historyCases: Case[]): Partial<CaseFormData> {
  const last = historyCases.find(c => (c.schema_version ?? 0) >= 1);
  if (!last) return {};
  return {
    nombre_tribunal: last.nombre_tribunal ?? "",
    organismo_tribunal: last.organismo_tribunal ?? "",
    sala_tribunal: last.sala_tribunal ?? "",
    integrantes_tribunal: last.integrantes_tribunal ?? "",
    tipo_causa: last.tipo_causa ?? "",
    nombre_proponente: last.nombre_proponente ?? "",
    profesion_proponente: last.profesion_proponente ?? "",
    matricula_proponente: last.matricula_proponente ?? "",
  };
}

/** Formulario a partir de un caso existente (modo edición). */
export function caseToForm(cas: Case): CaseFormData {
  const f = { ...EMPTY_CASE_FORM };
  for (const k of Object.keys(EMPTY_CASE_FORM) as (keyof CaseFormData)[]) {
    if (k === "imeiOverride") continue;
    const v = (cas as unknown as Record<string, unknown>)[k];
    if (typeof v === "string") f[k] = v;
  }
  return f;
}

/** Cuerpo de `POST`/`PUT /api/cases` (sin `device`), con los valores recortados. */
export function formToCaseRequest(form: CaseFormData) {
  const { imeiOverride: _imei, ...rest } = form;
  const out = {} as Omit<CaseFormData, "imeiOverride">;
  for (const k of Object.keys(rest) as (keyof typeof rest)[]) out[k] = rest[k].trim();
  return out;
}

/* ── Capturas que admiten rol (mismo criterio que `EvidenceClassifier.Screenshot`) ── */

/** Captura de pantalla PNG/JPG: la única clase de archivo que se puede marcar. */
export function isRoleEligible(name: string): boolean {
  const n = name.toLowerCase();
  if (n.includes("foto_funcionario") || n.includes("foto_denunciante")) return false;
  return (n.includes("screenshot") || n.includes("captura")) && /\.(png|jpe?g)$/.test(n);
}

export const CAPTURE_ROLE_LABELS: Record<"imei_modelo" | "nombre_dispositivo", string> = {
  imei_modelo: "IMEI y modelo",
  nombre_dispositivo: "Nombre del dispositivo",
};
