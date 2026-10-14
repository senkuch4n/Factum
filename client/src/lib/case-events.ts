/**
 * Textos e íconos de la línea de tiempo del caso (trazabilidad-caso, HU5).
 *
 * Centraliza el mapa tipo→(ícono, texto legible para un no técnico), la
 * traducción de `changed_fields` a etiquetas y la derivación de hitos para los
 * casos previos a esta HU (DT12: el backend no los fabrica, los arma el cliente
 * a partir de `created_at`/`generated_at`/`officer`).
 */

import {
  AlertCircle, AlertTriangle, Camera, Circle, Clock, ClockAlert, ClockCheck, Cpu, FileCheck, Film, FolderPlus, Image,
  PauseCircle, Pencil, PlayCircle, RefreshCw, Trash2, Upload, Video, type LucideIcon,
} from "lucide-react";
import type { Case, CaseEvent, CaseEventDetail, CaseEventType } from "@/lib/api";
import { TARGET_LABEL } from "@/lib/timestamps";
import { clockReasonLabel, describeEventSkew, exceedsThreshold, formatDuration } from "@/lib/device-details";
import { reasonText } from "@/lib/recording-session";

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
  evidence_updated: RefreshCw,
  capture_failed: AlertTriangle,
  timestamp_granted: ClockCheck,
  timestamp_failed: ClockAlert,
  device_details_recorded: Cpu,
  device_clock_measured: Clock,
  // grabacion-sesion-completa §9.8
  recording_session_settings_changed: Video,
  recording_session_started: Circle,
  recording_session_segment_closed: Film,
  recording_session_stopped: PauseCircle,
  recording_session_resumed: PlayCircle,
};

/** Ícono del evento; `FolderPlus` para un tipo desconocido (backend más nuevo). */
export function eventIcon(type: string): LucideIcon {
  return EVENT_ICON[type as CaseEventType] ?? FolderPlus;
}

export type EventTone = "danger" | "warning" | "neutral";

/**
 * Tono del ítem: la generación fallida resalta en rojo; un intento de captura
 * fallido (no es un hecho, wizard-concurrencia-pestanas D5) en el tono de
 * advertencia; el resto es neutro. No se transmite nada solo por color
 * (siempre hay ícono + texto).
 */
export function eventTone(
  type: string, detail?: CaseEventDetail | null, clockSkewWarningSeconds?: number,
): EventTone {
  if (type === "report_failed") return "danger";
  if (type === "capture_failed" || type === "timestamp_failed") return "warning";
  // metadatos-dispositivo §10.7: diferencia de reloj por encima del umbral (sin umbral, neutro).
  if ((type === "device_details_recorded" || type === "device_clock_measured") && clockSkewWarningSeconds !== undefined
    && exceedsThreshold(detail?.device_minus_pc_ms, clockSkewWarningSeconds)) {
    return "warning";
  }
  return "neutral";
}

/**
 * Línea secundaria de los eventos de la ficha técnica (§10.7): "El dispositivo
 * adelanta 10 s respecto de la PC (± 1 s) · 3 datos no disponibles", o
 * "Reloj no disponible (motivo)". Nunca lleva SSID ni valores de la ficha.
 */
export function eventDeviceDetail(ev: CaseEvent): string | null {
  if (ev.type !== "device_details_recorded" && ev.type !== "device_clock_measured") return null;
  const d = ev.detail;
  const parts: string[] = [];
  if (typeof d?.device_minus_pc_ms === "number") parts.push(describeEventSkew(d.device_minus_pc_ms, d.clock_margin_ms));
  else parts.push(`Reloj no disponible (${clockReasonLabel(d?.reason)})`);
  const n = d?.unavailable_count;
  if (ev.type === "device_details_recorded" && typeof n === "number" && n > 0) {
    parts.push(n === 1 ? "1 dato no disponible" : `${n} datos no disponibles`);
  }
  return parts.join(" · ");
}

/** Título de un `capture_failed` según la acción que falló (`detail.failed_action`). */
function captureFailedTitle(failed: string | undefined): string {
  switch (failed) {
    case "capture_video_start": return "Intento de grabación fallido";
    case "capture_video_stop": return "No se pudo cerrar la grabación";
    case "capture_screenshot": return "Intento de captura de pantalla fallido";
    case "capture_photo": return "Intento de foto fallido";
    default: return "Intento de captura fallido";
  }
}

/** `sha256: <12 primeros del anterior>… → <12 primeros del nuevo>…` de un `evidence_updated`, o `null`. */
export function eventHashChange(ev: CaseEvent): string | null {
  if (ev.type !== "evidence_updated") return null;
  const prev = ev.detail?.previous_sha256;
  const next = ev.detail?.sha256;
  if (!prev || !next) return null;
  return `sha256: ${prev.slice(0, 12)}… → ${next.slice(0, 12)}…`;
}

/** Objeto sellado en palabras ("evidencia", "ZIP", "informe"); "objeto" si el backend no lo manda. */
function targetLabel(ev: CaseEvent): string {
  const t = ev.detail?.target;
  return t ? TARGET_LABEL[t] ?? t : "objeto";
}

/**
 * Línea secundaria de un evento de sellado de tiempo (sellado-tiempo §9.5):
 * autoridad, fecha certificada (hora local) y hash abreviado; o el motivo del
 * fallo. `null` para otros tipos o sin datos.
 */
export function eventTimestampDetail(ev: CaseEvent): string | null {
  const d = ev.detail;
  if (!d) return null;
  if (ev.type === "timestamp_granted") {
    const parts: string[] = [];
    if (d.authority) parts.push(d.authority);
    if (d.gen_time) {
      const t = new Date(d.gen_time);
      if (!Number.isNaN(t.getTime())) parts.push(`fecha certificada ${t.toLocaleString("es-AR")}`);
    }
    if (d.sha256) parts.push(`sha256: ${d.sha256.slice(0, 12)}…`);
    return parts.length > 0 ? parts.join(" · ") : null;
  }
  if (ev.type === "timestamp_failed") return d.reason?.trim() || null;
  return null;
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
    case "evidence_updated":
      return "Se actualizó evidencia (contenido distinto)";
    case "capture_failed":
      return captureFailedTitle(ev.detail?.failed_action);
    case "timestamp_granted":
      return `Sello de tiempo obtenido (${targetLabel(ev)})`;
    case "timestamp_failed":
      return ev.detail?.final
        ? `Se abandonó el sello de tiempo (${targetLabel(ev)}) tras ${ev.detail.attempt ?? "varios"} intentos`
        : `No se pudo obtener el sello de tiempo (${targetLabel(ev)})`;
    case "device_details_recorded":
      return ev.detail?.reread
        ? "Se volvieron a leer los datos técnicos del dispositivo"
        : "Se registraron los datos técnicos del dispositivo";
    case "device_clock_measured":
      return "Se midió el reloj del dispositivo al generar";
    case "recording_session_settings_changed":
    case "recording_session_started":
    case "recording_session_segment_closed":
    case "recording_session_stopped":
    case "recording_session_resumed":
      return recordingSessionTitle(ev);
    default:
      return "Actividad del caso";
  }
}

/** "pantalla" / "cámara" de un evento de la sesión. */
function trackWord(track: string | undefined): string {
  return track === "camara" ? "cámara" : "pantalla";
}

/** Textos de los eventos de la grabación de la sesión (grabacion-sesion-completa §9.8). */
function recordingSessionTitle(ev: CaseEvent): string {
  const d = ev.detail ?? {};
  switch (ev.type) {
    case "recording_session_settings_changed":
      return d.enabled === false
        ? "Se desactivó la grabación de la sesión"
        : `Se activó la grabación de la sesión${d.with_camera ? " con cámara" : ""}`;
    case "recording_session_started":
      return `Empezó la grabación de la sesión (${trackWord(d.track)})`;
    case "recording_session_segment_closed": {
      const n = typeof d.segment_index === "number" ? `${d.segment_index} ` : "";
      // Misma cifra que el informe (QA B-4): el tiempo grabado (`recorded_seconds`), no la
      // duración del video. Si el backend no lo manda en el evento, no se muestra duración.
      const dur = typeof d.recorded_seconds === "number" ? ` · ${formatDuration(d.recorded_seconds)}` : "";
      const incomplete = d.validation === "incomplete" ? " (incompleto)" : "";
      return `Se guardó el tramo ${n}de la ${trackWord(d.track)}${dur}${incomplete}`;
    }
    case "recording_session_stopped":
      return `Se cortó la grabación de la sesión: ${reasonText(d.reason)}${d.segment_lost ? " (el tramo en curso no se pudo recuperar)" : ""}`;
    case "recording_session_resumed":
      return typeof d.gap_seconds === "number"
        ? `Se reanudó la grabación de la sesión tras ${formatDuration(d.gap_seconds)}`
        : "Se reanudó la grabación de la sesión";
    default:
      return "Actividad del caso";
  }
}

/** Nombre del archivo a mostrar monoespaciado, si el evento lo trae. */
export function eventFilename(ev: CaseEvent): string | null {
  if (
    ev.type === "evidence_added" || ev.type === "evidence_removed" || ev.type === "evidence_updated"
    || ev.type === "timestamp_granted" || ev.type === "timestamp_failed"
    || ev.type === "recording_session_segment_closed"
  ) {
    return ev.filename ?? null;
  }
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
