/**
 * Catálogos de sugerencias del perito (formulario-caso-catalogos).
 *
 * Espejo de `server/src/Factum.Backend/Services/Catalogs/CatalogDefinitions.cs`
 * (qué campos del caso alimentan cada catálogo) y de `CatalogText.cs`
 * (normalización para deduplicar y filtrar). Sumar un catálogo = una línea
 * en `CatalogDefinitions.Sources` y otra en `FIELD_CATALOG`.
 */

import type { CatalogEntry, CatalogId, CaseFormData } from "@/types";

export const CATALOG_IDS = ["destinatarios", "partes", "profesiones", "tipos_dispositivo"] as const satisfies readonly CatalogId[];

/** Campo del caso (clave JSON) → catálogo del que sugiere (SDD §4.5). */
export const FIELD_CATALOG: Partial<Record<keyof CaseFormData, CatalogId>> = {
  nombre_tribunal: "destinatarios",
  parte_denunciante: "partes",
  parte_denunciada: "partes",
  nombre_proponente: "partes",
  profesion_proponente: "profesiones",
  tipo_dispositivo: "tipos_dispositivo",
};

export const CATALOG_LABELS: Record<CatalogId, string> = {
  destinatarios: "Destinatarios",
  partes: "Partes",
  profesiones: "Profesiones",
  tipos_dispositivo: "Tipos de dispositivo",
};

/** Catálogos vacíos (mientras carga o si falló el `GET`). */
export const EMPTY_CATALOGS: Record<CatalogId, CatalogEntry[]> = {
  destinatarios: [],
  partes: [],
  profesiones: [],
  tipos_dispositivo: [],
};

/** Texto que se guarda: trim y cualquier secuencia de espacios en blanco → un espacio (`CatalogText.CleanValue`). */
export function cleanCatalogValue(s: string | null | undefined): string {
  return (s ?? "").replace(/\s+/g, " ").trim();
}

// Constructor y no literal: el `target` del tsconfig (ES2017) no admite `\p{…}` en literales.
const COMBINING_MARKS = new RegExp("\\p{M}", "gu");

/**
 * Clave de deduplicación y filtro (`CatalogText.NormalizeKey`): limpia, saca
 * las marcas diacríticas (NFD + `\p{M}`, que cubre Mn, Mc y Me) y pasa a
 * minúsculas. "  TELÉFONO   celular " → "telefono celular"; "Muñoz" → "munoz".
 */
export function normalizeCatalogKey(s: string | null | undefined): string {
  return cleanCatalogValue(s).normalize("NFD").replace(COMBINING_MARKS, "").normalize("NFC").toLowerCase();
}

/**
 * Entradas cuyo valor contiene la consulta, sin distinguir mayúsculas,
 * tildes ni espacios repetidos. Consulta vacía → todas. Mantiene el orden
 * del servidor (uso reciente primero).
 */
export function filterEntries(entries: CatalogEntry[], query: string): CatalogEntry[] {
  const q = normalizeCatalogKey(query);
  if (!q) return entries;
  return entries.filter(e => normalizeCatalogKey(e.value).includes(q));
}
