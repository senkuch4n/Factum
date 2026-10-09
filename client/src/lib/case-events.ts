/**
 * Textos e íconos de la línea de tiempo del caso (trazabilidad-caso, HU5).
 *
 * Centraliza el mapa tipo→(ícono, texto legible para un no técnico), la
 * traducción de `changed_fields` a etiquetas y la derivación de hitos para los
 * casos previos a esta HU (DT12: el backend no los fabrica, los arma el cliente
 * a partir de `created_at`/`generated_at`/`officer`).
 */

import {
  AlertCircle, Camera, FileCheck, FolderPlus, Image, Pencil, Trash2, Upload, Video,
  type LucideIcon,
} from "lucide-react";
import type { Case, CaseEvent, CaseEventType } from "@/lib/api";

/** Ícono lucide por tipo de evento (criterio de la SDD §"Cada ítem"). */
const EVENT_ICON: Record<CaseEventType, LucideIcon> = {
  case_created: FolderPlus,
  case_updated: Pencil,
  capture_screenshot: Image,
  capture_video_start: Video,
  capture_video_stop: Video,
  capture_photo: Camera,
  evidence_added: Upload,
  evidence_removed: Trash2,
  report_generated: FileCheck,
  report_failed: AlertCircle,
};

/** Ícono del evento; `FolderPlus` para un tipo desconocido (backend más nuevo). */
export function eventIcon(type: string): LucideIcon {
  return EVENT_ICON[type as CaseEventType] ?? FolderPlus;
}

/**
 * Tono del ítem: la generación fallida resalta en rojo; el resto es neutro. No
 * se transmite nada solo por color (siempre hay ícono + texto).
 */
export function eventTone(type: string): "danger" | "neutral" {
  return type === "report_failed" ? "danger" : "neutral";
}

/**
 * Nombre legible de un campo de la causa a partir de su clave snake_case
 * (`changed_fields` de `case_updated`). Las claves son las que arma el backend
 * (`CaseEventFields.ChangedFields`). Una clave desconocida cae al propio nombre.
 */
const FIELD_LABELS: Record<string, string> = {
  nro_referencia: "número de referencia",
  nombre_denunciante: "titular",
  dni_denunciante: "DNI del titular",
  nombre_tribunal: "tribunal",
  organismo_tribunal: "organismo",
  sala_tribunal: "sala",
  integrantes_tribunal: "integrantes del tribunal",
  integrantes: "integrantes del tribunal",
  tipo_causa: "tipo de causa",
  caratula: "carátula",
  parte_denunciante: "parte denunciante",
  parte_denunciada: "parte denunciada",
  objeto_causa: "objeto de la causa",
  ambito_causa: "ámbito de la causa",
  fecha_intervencion: "fecha de intervención",
  nombre_proponente: "proponente",
  profesion_proponente: "profesión del proponente",
  matricula_proponente: "matrícula del proponente",
  tipo_dispositivo: "tipo de dispositivo",
  linea_dispositivo: "línea del dispositivo",
  imei: "IMEI",
  observaciones: "observaciones",
};

export function fieldLabel(key: string): string {
  return FIELD_LABELS[key] ?? key;
}

/** Lista de campos cambiados en una frase: "carátula, tribunal y 2 más". */
function fieldsPhrase(fields: string[] | undefined): string {
  if (!fields || fields.length === 0) return "";
  const labels = fields.map(fieldLabel);
  if (labels.length === 1) return labels[0];
  if (labels.length === 2) return `${labels[0]} y ${labels[1]}`;
  const head = labels.slice(0, 2).join(", ");
  return `${head} y ${labels.length - 2} más`;
}

/**
 * Texto principal de un evento, en lenguaje claro para un no técnico. El nombre
 * del archivo de evidencia se expone aparte (monoespaciado), no acá.
 */
export function eventTitle(ev: CaseEvent): string {
  switch (ev.type) {
    case "case_created":
      return "Se creó el caso";
    case "case_updated": {
      const phrase = fieldsPhrase(ev.detail?.changed_fields);
      return phrase ? `Se editaron los datos de la causa (${phrase})` : "Se editaron los datos de la causa";
    }
    case "capture_screenshot":
      return "Captura de pantalla";
    case "capture_video_start":
      return "Inicio de grabación de pantalla";
    case "capture_video_stop":
      return "Fin de grabación de pantalla";
    case "capture_photo":
      return "Foto con la cámara";
    case "evidence_added":
      return "Se agregó evidencia";
    case "evidence_removed":
      return "Se quitó evidencia";
    case "report_generated":
      return "Se generó el informe";
    case "report_failed":
      return "Falló la generación del informe";
    default:
      return "Actividad del caso";
  }
}

/** Nombre del archivo a mostrar monoespaciado, si el evento lo trae. */
export function eventFilename(ev: CaseEvent): string | null {
  if (ev.type === "evidence_added" || ev.type === "evidence_removed") return ev.filename ?? null;
  return null;
}

/**
 * Hito derivado para un caso previo a esta HU (DT12): una forma mínima de
 * `CaseEvent` con lo que se puede inferir del propio `Case`. No tiene actor ni
 * equipo reales (no se inventan): `actor_name` sale del dueño del caso.
 */
export interface DerivedMilestone {
  id: string;
  type: Extract<CaseEventType, "case_created" | "report_generated">;
  timestamp: string;
  actor_name: string;
}

/**
 * Hitos derivables de un caso sin eventos: creación (siempre) y generación del
 * informe (si `generated_at`). Orden cronológico ascendente.
 */
export function derivedMilestones(cas: Pick<Case, "created_at" | "generated_at" | "officer">): DerivedMilestone[] {
  const actor = cas.officer?.name ?? "";
  const out: DerivedMilestone[] = [
    { id: "derived-created", type: "case_created", timestamp: cas.created_at, actor_name: actor },
  ];
  if (cas.generated_at) {
    out.push({ id: "derived-generated", type: "report_generated", timestamp: cas.generated_at, actor_name: actor });
  }
  return out;
}

/** Texto de un hito derivado (no hay `detail`, así que es el genérico del tipo). */
export function derivedTitle(type: DerivedMilestone["type"]): string {
  return type === "case_created" ? "Se creó el caso" : "Se generó el informe";
}
