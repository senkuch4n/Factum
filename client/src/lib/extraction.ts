/**
 * Lógica pura y etiquetas de la extracción lógica de Android
 * (extraccion-logica-android, SDD §10.2). Sin React: etiquetas en castellano,
 * íconos por estado (texto **y** forma, nunca solo color), selección por
 * defecto, vista previa de modificaciones y cálculos de progreso.
 *
 * Las claves de campo son las del Contrato compartido (§8): no se renombran.
 */

import {
  AlertTriangle, Ban, CheckCircle2, CircleDashed, Clock, Loader2, MinusCircle, XCircle, type LucideIcon,
} from "lucide-react";
import type {
  AgentExtractionPreflight, AgentExtractionSnapshot, ExtractionCategoryKey, ExtractionCategoryKind,
  ExtractionCategoryStatus, ExtractionInterruptionReason, ExtractionPhase, ExtractionReasonCode, ExtractionStatus,
} from "@/lib/agent";
import { formatBytesPair } from "@/lib/format";

/* ── Catálogo de categorías (§4.3, en orden) ─────────────────────────── */

export interface CategoryMeta {
  key: ExtractionCategoryKey;
  kind: ExtractionCategoryKind;
  label: string;
  /** Sustantivo para la cantidad del resumen (solo estructuradas). */
  noun?: string;
}

/** Estructuradas, en el orden de §4.3. */
export const STRUCTURED_CATEGORIES: CategoryMeta[] = [
  { key: "contacts", kind: "structured", label: "Contactos", noun: "contactos" },
  { key: "sms_mms", kind: "structured", label: "Mensajes SMS/MMS", noun: "mensajes" },
  { key: "call_log", kind: "structured", label: "Registro de llamadas", noun: "llamadas" },
  { key: "calendar", kind: "structured", label: "Calendario", noun: "eventos" },
  { key: "apps", kind: "structured", label: "Apps instaladas", noun: "apps" },
];

/** Archivos de `/sdcard`, en el orden de §4.3 (sin `custom`, que es implícito). */
export const FILE_CATEGORIES: CategoryMeta[] = [
  { key: "media", kind: "files", label: "Fotos y videos" },
  { key: "downloads", kind: "files", label: "Descargas" },
  { key: "documents", kind: "files", label: "Documentos" },
  { key: "audio", kind: "files", label: "Audio y grabaciones" },
  { key: "whatsapp_media", kind: "files", label: "Medios de WhatsApp" },
  { key: "all_storage", kind: "files", label: "Todo el almacenamiento" },
];

const ALL_META: Record<ExtractionCategoryKey, CategoryMeta> = {
  ...Object.fromEntries(STRUCTURED_CATEGORIES.map(c => [c.key, c])),
  ...Object.fromEntries(FILE_CATEGORIES.map(c => [c.key, c])),
  custom: { key: "custom", kind: "files", label: "Carpetas elegidas" },
} as Record<ExtractionCategoryKey, CategoryMeta>;

export function categoryLabel(key: ExtractionCategoryKey | string): string {
  return ALL_META[key as ExtractionCategoryKey]?.label ?? key;
}
export function categoryNoun(key: ExtractionCategoryKey | string): string {
  return ALL_META[key as ExtractionCategoryKey]?.noun ?? "elementos";
}
export function categoryKind(key: ExtractionCategoryKey | string): ExtractionCategoryKind {
  return ALL_META[key as ExtractionCategoryKey]?.kind ?? "files";
}

/* ── Presentación con ícono (texto + forma, no solo color) ───────────── */

export type Tone = "running" | "success" | "warning" | "danger" | "neutral";

export interface Presentation { label: string; Icon: LucideIcon; tone: Tone; }

/** Estado global de la extracción (§4.2). */
export function extractionStatusView(status: ExtractionStatus): Presentation {
  switch (status) {
    case "running": return { label: "En curso", Icon: Loader2, tone: "running" };
    case "completed": return { label: "Completa", Icon: CheckCircle2, tone: "success" };
    case "completed_with_errors": return { label: "Completa con errores", Icon: AlertTriangle, tone: "warning" };
    case "interrupted": return { label: "Interrumpida", Icon: Ban, tone: "danger" };
    case "cancelled": return { label: "Cancelada", Icon: XCircle, tone: "neutral" };
    default: return { label: status, Icon: CircleDashed, tone: "neutral" };
  }
}

/** Estado de una categoría (`categories[].status`, §4.3). */
export function categoryStatusView(status: ExtractionCategoryStatus | string): Presentation {
  switch (status) {
    case "pending": return { label: "Pendiente", Icon: CircleDashed, tone: "neutral" };
    case "running": return { label: "En curso", Icon: Loader2, tone: "running" };
    case "done": return { label: "Completa", Icon: CheckCircle2, tone: "success" };
    case "done_with_errors": return { label: "Completa con errores", Icon: AlertTriangle, tone: "warning" };
    case "not_extracted": return { label: "No extraída", Icon: MinusCircle, tone: "neutral" };
    case "cancelled": return { label: "Cancelada", Icon: XCircle, tone: "neutral" };
    default: return { label: String(status), Icon: CircleDashed, tone: "neutral" };
  }
}

/** Etiqueta de la fase en curso (`phase`, §8.2.6). */
export function phaseLabel(phase: ExtractionPhase | string): string {
  switch (phase) {
    case "preparing": return "Preparando";
    case "structured": return "Copiando datos del sistema";
    case "inventory": return "Inventariando archivos";
    case "copying": return "Copiando archivos";
    case "hashing": return "Calculando hashes en la PC";
    case "verifying_device": return "Verificando el hash en el celular";
    case "closing": return "Cerrando el acta";
    case "done": return "Listo";
    default: return String(phase);
  }
}

/** Motivo legible de una categoría no extraída (`reason_code`, §4.4). */
export function reasonCodeLabel(code: ExtractionReasonCode | string | undefined): string {
  switch (code) {
    case "permission_denied": return "El sistema denegó el permiso";
    case "provider_unavailable": return "No disponible en este celular";
    case "connector_unavailable": return "La app auxiliar no está disponible en esta versión";
    case "connector_install_blocked":
      return "No se pudo instalar la app auxiliar: activá “Instalar vía USB” o aceptá el aviso de Play Protect y reintentá";
    case "connector_install_failed": return "Falló la instalación de la app auxiliar";
    case "connector_conflict": return "Ya hay otra app con el mismo nombre y otra firma";
    case "path_not_found": return "La carpeta no existe en el celular";
    case "device_disconnected": return "El celular se desconectó";
    case "cancelled": return "Cancelada";
    case "error": return "Ocurrió un error";
    default: return code ? String(code) : "Motivo desconocido";
  }
}

/** Motivo legible de una interrupción (`interruption.reason_code`, §4.4). */
export function interruptionReasonLabel(code: ExtractionInterruptionReason | string | undefined): string {
  switch (code) {
    case "device_disconnected": return "Se desconectó el celular";
    case "device_unauthorized": return "Se revocó la autorización de depuración en el celular";
    case "agent_stopped": return "Se cerró Tatana";
    case "destination_error": return "Problema con el destino (disco lleno o removido)";
    case "internal_error": return "Error interno";
    default: return code ? String(code) : "Motivo desconocido";
  }
}

/* ── Reloj / terminal ────────────────────────────────────────────────── */

/** `true` si el estado ya no cambia más (§4.2). */
export function isTerminal(status: ExtractionStatus | string): boolean {
  return status !== "running";
}

/** ETA aproximada en castellano. `null`/≤0 → cadena vacía (no se muestra). */
export function formatEta(seconds: number | null | undefined): string {
  if (seconds == null || !Number.isFinite(seconds) || seconds <= 0) return "";
  if (seconds < 60) return "menos de 1 min";
  const mins = Math.round(seconds / 60);
  if (mins < 60) return `~${mins} min`;
  const h = Math.floor(mins / 60);
  const m = mins % 60;
  return m === 0 ? `~${h} h` : `~${h} h ${m} min`;
}

/** Ícono para la ETA (lo usa la vista de progreso). */
export const EtaIcon: LucideIcon = Clock;

/* ── Selección del wizard ─────────────────────────────────────────────── */

export type StayAwakePolicy = "always" | "connector";

/** Estado editable de "Qué extraer". `keys` son las casillas (sin `custom`). */
export interface ExtractionSelection {
  keys: ExtractionCategoryKey[];
  customPaths: string[];
  verifyOnDevice: boolean;
}

/**
 * Selección por defecto (D8): estructuradas marcadas (`call_log` solo si el
 * connector está disponible), archivos sin marcar, verificación en el celular
 * marcada si el celular lo permite.
 */
export function defaultSelection(preflight: AgentExtractionPreflight): ExtractionSelection {
  const keys: ExtractionCategoryKey[] = ["contacts", "sms_mms", "calendar", "apps"];
  if (preflight.connector.available) keys.push("call_log");
  return { keys, customPaths: [], verifyOnDevice: preflight.device_sha256_available };
}

/** `true` si la categoría puede marcarse (archivo existente / connector disponible). */
export function isCategoryAvailable(key: ExtractionCategoryKey, preflight: AgentExtractionPreflight): boolean {
  if (key === "call_log") return preflight.connector.available;
  if (categoryKind(key) === "files" && key !== "custom") {
    const fc = preflight.file_categories.find(c => c.key === key);
    // Una categoría de archivos sin entrada en el preflight (p. ej. una nueva) se asume disponible.
    return fc ? fc.available : true;
  }
  return true;
}

/** `true` si hay al menos una categoría o carpeta elegida. */
export function hasAnySelection(sel: ExtractionSelection): boolean {
  return sel.keys.length > 0 || sel.customPaths.length > 0;
}

/**
 * Modificaciones al dispositivo para el paso Confirmar (§10.4.4). Lista vacía =
 * "Ninguna: solo lectura". Con U3=B (recomendada) solo hay modificaciones si se
 * eligió el registro de llamadas (la app auxiliar y la pantalla encendida
 * durante su instalación).
 */
export function deviceModificationsPreview(
  keys: ExtractionCategoryKey[], stayAwakePolicy: StayAwakePolicy = "connector",
): string[] {
  const mods: string[] = [];
  if (keys.includes("call_log")) {
    mods.push("Se instala temporalmente la app auxiliar de Factum y se la desinstala al terminar.");
    mods.push("La pantalla se mantiene encendida mientras se instala la app auxiliar.");
  } else if (stayAwakePolicy === "always") {
    mods.push("La pantalla se mantiene encendida durante toda la extracción.");
  }
  return mods;
}

/** `true` si habrá modificaciones al dispositivo (requiere la casilla de confirmación). */
export function needsConfirmation(
  keys: ExtractionCategoryKey[], stayAwakePolicy: StayAwakePolicy = "connector",
): boolean {
  return deviceModificationsPreview(keys, stayAwakePolicy).length > 0;
}

/* ── Progreso global (§10.3 / §10.5) ─────────────────────────────────── */

const FILE_PHASES: ReadonlySet<string> = new Set(["inventory", "copying", "hashing", "verifying_device"]);

/**
 * Avance global: por bytes en las fases de archivos; por categorías resueltas en
 * la fase estructurada y las de preparación/cierre. `pct` en 0–100.
 */
export function globalProgress(s: AgentExtractionSnapshot): { pct: number; detail: string } {
  if (FILE_PHASES.has(s.phase) && s.totals.bytes_total > 0) {
    const pct = Math.min(100, (s.totals.bytes_done / s.totals.bytes_total) * 100);
    return { pct, detail: formatBytesPair(s.totals.bytes_done, s.totals.bytes_total) };
  }
  const total = s.categories.length || 1;
  const resolved = s.categories.filter(c => c.status !== "pending" && c.status !== "running").length;
  return { pct: (resolved / total) * 100, detail: `${resolved} de ${total} categorías` };
}
