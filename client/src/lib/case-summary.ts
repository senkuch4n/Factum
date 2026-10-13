/**
 * Helpers puros de la Ficha viva de la causa y de las ayudas del paso 2
 * (formulario-causa-rediseno §6.3). Sin React: se pueden testear sueltos.
 *
 * Las frases (`tramiteAnte`, `fraseIntegracion`, `datosProponente`) tienen que
 * coincidir con `server/src/Factum.Backend/Services/Reports/ReportValues.cs`
 * `Placeholders`: la ficha muestra lo mismo que va a decir el informe.
 */

import type { Case, CaseFormData, IntegranteRow, ProfileFormData } from "@/types";
import { cleanCatalogValue } from "@/lib/catalogs";
import {
  CASE_FORM_FOCUS_ORDER, PROFILE_REQUIRED_KEYS, caseFieldId, cleanIntegrantes, errorKeyToFieldId,
  isValidIsoDate, joinIntegrantes, todayIso,
} from "@/lib/pericial";

/* ── Frases del informe ─────────────────────────────────────────────── */

/** "Sala II de Tribunal X" | "Tribunal X" | "" — espejo de {tramiteAnte}. */
export function tramiteAnte(tribunal: string, sala: string): string {
  const t = tribunal.trim();
  const s = sala.trim();
  return s.length > 0 ? `${s} de ${t}` : t;
}

/** ", con la integración de A y B" | "" — espejo de {fraseIntegracion}. */
export function fraseIntegracion(rows: IntegranteRow[]): string {
  const integrantes = joinIntegrantes(cleanIntegrantes(rows.map(r => r.value)));
  return integrantes.length > 0 ? ", con la integración de " + integrantes : "";
}

/** "Nombre – Profesión, M.P. 123" | "" — espejo de {datosProponente} (guion en dash U+2013). */
export function datosProponente(nombre: string, profesion: string, matricula: string): string {
  let out = nombre.trim();
  if (profesion.trim()) out += " – " + profesion.trim();
  if (matricula.trim()) out += ", M.P. " + matricula.trim();
  return out;
}

/* ── Fechas ─────────────────────────────────────────────────────────── */

const LONG_DATE = new Intl.DateTimeFormat("es-AR", { weekday: "long", day: "numeric", month: "long", year: "numeric" });

/** "lunes 12 de octubre de 2026" (sin coma), o null si `iso` no es una fecha válida. */
export function fechaEnPalabras(iso: string): string | null {
  const v = iso.trim();
  if (!isValidIsoDate(v)) return null;
  const [y, m, d] = v.split("-").map(Number);
  const parts = LONG_DATE.formatToParts(new Date(y, m - 1, d));
  const get = (t: Intl.DateTimeFormatPartTypes) => parts.find(p => p.type === t)?.value ?? "";
  return `${get("weekday")} ${get("day")} de ${get("month")} de ${get("year")}`;
}

/** `iso` válido y posterior a hoy (comparación de strings yyyy-MM-dd). */
export function isFutureIsoDate(iso: string): boolean {
  const v = iso.trim();
  return isValidIsoDate(v) && v > todayIso();
}

/** Ayer en local, `yyyy-MM-dd`. */
export function yesterdayIso(): string {
  const d = new Date();
  d.setDate(d.getDate() - 1);
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

/* ── Expediente repetido ────────────────────────────────────────────── */

/**
 * Clave de comparación de expedientes (D8 A): NFKC, minúsculas, sin espacios
 * y sin ceros a la izquierda en cada corrida de dígitos.
 */
export function normalizeExpediente(s: string): string {
  return s
    .normalize("NFKC")
    .toLowerCase()
    .replace(/\s+/g, "")
    .replace(/\d+/g, run => (run.length <= 15 ? String(Number(run)) : run.replace(/^0+(?=\d)/, "")));
}

/**
 * Casos del historial con el mismo expediente normalizado, sin `excludeId`,
 * del más reciente al más viejo. Vacío si la clave queda vacía.
 */
export function findSameExpediente(historyCases: Case[], nro: string, excludeId: string | null): Case[] {
  const key = normalizeExpediente(nro);
  if (!key) return [];
  return historyCases
    .filter(c => c.id !== excludeId && normalizeExpediente(c.nro_referencia ?? "") === key)
    .sort((a, b) => (a.created_at < b.created_at ? 1 : a.created_at > b.created_at ? -1 : 0));
}

/* ── Integrantes pegados ────────────────────────────────────────────── */

/**
 * Partes de un pegado de integrantes: separa por salto de línea, ";" y ",";
 * el último fragmento se parte una vez por su último " y " / " e ".
 * Nunca se aplica sin que el perito lo confirme (los nombres pueden llevar comas).
 */
export function splitIntegrantes(text: string): string[] {
  const parts = text.split(/\r?\n|;|,/);
  const last = parts.pop() ?? "";
  const re = /\s+(?:y|e)\s+/gi;
  let lastMatch: RegExpExecArray | null = null;
  for (let m = re.exec(last); m; m = re.exec(last)) lastMatch = m;
  if (lastMatch) {
    parts.push(last.slice(0, lastMatch.index), last.slice(lastMatch.index + lastMatch[0].length));
  } else {
    parts.push(last);
  }
  return parts.map(p => cleanCatalogValue(p)).filter(Boolean);
}

/* ── Índice de secciones de la ficha ────────────────────────────────── */

export type SummarySectionId = "perfil" | "causa" | "destinatario" | "partes" | "equipo";

export interface SummarySection {
  id: SummarySectionId;
  label: string;
  missing: number;
  /** id del primer obligatorio vacío, en el orden de `CASE_FORM_FOCUS_ORDER`. */
  firstMissingFieldId: string | null;
}

const SECTION_KEYS: { id: Exclude<SummarySectionId, "perfil">; label: string; keys: (keyof CaseFormData)[] }[] = [
  { id: "causa",        label: "Causa",        keys: ["nro_referencia", "fecha_intervencion", "caratula", "tipo_causa"] },
  { id: "destinatario", label: "Destinatario", keys: ["nombre_tribunal"] },
  { id: "partes",       label: "Partes",       keys: ["parte_denunciante", "parte_denunciada", "nombre_proponente"] },
  { id: "equipo",       label: "Equipo",       keys: ["nombre_denunciante", "tipo_dispositivo"] },
];

const filled = (v: string) => v.trim().length > 0;

/** Índice de la ficha: obligatorios que faltan por sección (mismo criterio que el n/m). */
export function summarySections(args: {
  form: CaseFormData; profileForm: ProfileFormData; profileIncomplete: boolean; imeiManual: boolean;
}): SummarySection[] {
  const { form, profileForm, profileIncomplete, imeiManual } = args;
  const out: SummarySection[] = [];

  const profileMissing = profileIncomplete ? PROFILE_REQUIRED_KEYS.filter(k => !filled(profileForm[k])) : [];
  out.push({
    id: "perfil",
    label: "Perfil",
    missing: profileMissing.length,
    firstMissingFieldId: profileMissing.length ? errorKeyToFieldId(`perfil.${profileMissing[0]}`) : null,
  });

  for (const s of SECTION_KEYS) {
    // Claves de error de la sección (IMEI cuenta en Equipo solo si se carga a mano).
    const errorKeys: string[] = s.keys.filter(k => !filled(form[k] as string)) as string[];
    if (s.id === "equipo" && imeiManual && !filled(form.imeiOverride)) errorKeys.push("imei");
    const first = CASE_FORM_FOCUS_ORDER.find(k => errorKeys.includes(k)) ?? null;
    out.push({
      id: s.id,
      label: s.label,
      missing: errorKeys.length,
      firstMissingFieldId: first ? (first === "imei" ? caseFieldId("imei") : errorKeyToFieldId(first)) : null,
    });
  }
  return out;
}

/* ── Resumen de una línea de cada sección plegada (acordeón guiado) ─── */

/** `yyyy-MM-dd` → "13/10" (o "" si no es una fecha válida). */
export function fechaCorta(iso: string): string {
  const v = iso.trim();
  if (!isValidIsoDate(v)) return "";
  const [, m, d] = v.split("-");
  return `${d}/${m}`;
}

/** Corta un texto largo a `max` caracteres con "…" (la carátula no tapa el resto del resumen). */
function clip(s: string, max: number): string {
  const v = s.trim();
  return v.length > max ? v.slice(0, max - 1).trimEnd() + "…" : v;
}

/**
 * Resumen de una línea de una sección del paso 2, con lo cargado y en el orden
 * en que se lee ("1234/2026 · Pérez c/ … · 13/10"). Vacío si no hay nada cargado.
 */
export function sectionSummaryLine(id: Exclude<SummarySectionId, "perfil">, form: CaseFormData): string {
  const parts: string[] =
    id === "causa"        ? [form.nro_referencia, clip(form.caratula, 48), fechaCorta(form.fecha_intervencion)] :
    id === "destinatario" ? [form.sala_tribunal, form.nombre_tribunal, form.organismo_tribunal] :
    id === "partes"       ? [form.parte_denunciante, form.parte_denunciada, form.nombre_proponente] :
                            [form.nombre_denunciante, form.tipo_dispositivo, form.linea_dispositivo];
  return parts.map(p => p.trim()).filter(Boolean).join(" · ");
}
