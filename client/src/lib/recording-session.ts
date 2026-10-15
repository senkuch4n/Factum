/**
 * Grabación de la sesión completa (grabacion-sesion-completa, SDD §9.1).
 * Funciones puras: sin React ni red, exportadas para cuando haya tests.
 */

import type {
  CaseSessionDto, SessionCapacity, SessionReason, SessionSegmentDto, SessionStateDto, SessionStateName,
  SessionWarning,
} from "./agent";
import type {
  Case, RecordingSession, RecordingSessionMode, RecordingSessionSegment, RecordingSessionSnapshot,
} from "./api";
import { formatBytes } from "./format";

/** Estados de una sesión en curso en Tatana (§5.3): ocupan el lugar de "una sesión por Tatana". */
export const ACTIVE_SESSION_STATES: ReadonlySet<SessionStateName> = new Set<SessionStateName>([
  "starting", "recording", "rotating", "waiting_device", "stopping",
]);

export function isSessionActive(state: SessionStateName | null | undefined): boolean {
  return !!state && ACTIVE_SESSION_STATES.has(state);
}

/** Grabando de verdad (se puede marcar un momento, §5.9). */
export function isSessionRecording(state: SessionStateName | null | undefined): boolean {
  return state === "recording" || state === "rotating";
}

/** Nombre de un tramo (§5.2): `sesion_<pista>_<NNN>_<yyyyMMdd>_<HHmmss>.mkv`. */
export const SESSION_SEGMENT_RE = /^sesion_(pantalla|camara)_(\d{3,4})_(\d{8})_(\d{6})\.mkv$/;

/** Texto del motivo para el informe y la trazabilidad (§8.4), en minúscula. */
export const REASON_TEXT: Record<SessionReason, string> = {
  cambio_de_paso: "el perito salió del paso de captura",
  retomado_otro_dia: "el caso se retomó otro día",
  desconexion: "se desconectó el cable USB del dispositivo",
  tatana_cerrado: "Tatana se cerró o la estación de trabajo se reinició",
  sin_espacio: "quedaba poco espacio en el disco de la estación de trabajo",
  camara_fallo: "la cámara dejó de grabar",
  detenido_por_perito: "el perito detuvo la grabación",
  fallo_grabacion: "la herramienta de captura se interrumpió por un error",
  rotacion: "corte técnico entre tramos",
};

/** Mismos textos con mayúscula inicial, para la web (§9.1). */
export const REASON_LABEL: Record<SessionReason, string> = Object.fromEntries(
  Object.entries(REASON_TEXT).map(([k, v]) => [k, v.charAt(0).toUpperCase() + v.slice(1)]),
) as Record<SessionReason, string>;

/** Motivo legible; un motivo desconocido (backend más nuevo) queda tal cual. */
export function reasonText(reason: string | null | undefined, capitalized = false): string {
  if (!reason) return capitalized ? "Motivo no informado" : "motivo no informado";
  const table = capitalized ? REASON_LABEL : REASON_TEXT;
  return table[reason as SessionReason] ?? reason;
}

/** Pista en palabras. */
export function trackLabel(track: string | null | undefined): string {
  return track === "camara" ? "Cámara" : "Pantalla";
}

/** Switch prendido por defecto según el modo de la instalación (D1). */
export function defaultEnabled(mode: RecordingSessionMode): boolean {
  return mode === "optional_on" || mode === "required";
}

const isSavedOrDiscarded = (s: SessionSegmentDto): boolean => s.state === "saved" || s.state === "discarded";

/**
 * Diario de Tatana → cuerpo de `PUT /api/cases/{id}/recording-session` (§7.5):
 * sin `case_id`/`schema_version`/`updated_at`, sin tramos `recording`/`closing`,
 * y con `serial` → `connection_id`. Los `null` se mandan explícitos.
 */
export function toBackendSnapshot(dto: CaseSessionDto): RecordingSessionSnapshot {
  const segments: RecordingSessionSegment[] = dto.segments.filter(isSavedOrDiscarded).map(s => ({
    id: s.id,
    track: s.track,
    index: s.index,
    filename: s.filename,
    state: s.state as "saved" | "discarded",
    started_at: s.started_at,
    // Un tramo cerrado siempre tiene fin; por las dudas, el inicio (el backend valida started ≤ stopped).
    stopped_at: s.stopped_at ?? s.started_at,
    recorded_seconds: s.recorded_seconds ?? 0,
    duration_seconds: s.duration_seconds ?? null,
    validation: s.validation ?? null,
    recovered: s.recovered === true,
    overlap_seconds: s.overlap_seconds ?? null,
    stop_reason: s.stop_reason ?? null,
    size: s.size ?? null,
    sha256: s.sha256 ?? null,
  }));
  return {
    session_id: dto.session_id,
    platform: dto.platform,
    connection_id: dto.serial,
    agent_version: dto.agent_version ?? null,
    segment_minutes: dto.segment_minutes,
    audio: dto.audio ?? null,
    camera: dto.camera ?? null,
    state: dto.state,
    segments,
    gaps: dto.gaps.map(g => ({
      id: g.id, track: g.track, from: g.from, to: g.to ?? null, reason: g.reason, segment_lost: g.segment_lost === true,
    })),
    marks: dto.marks.map(m => ({
      id: m.id, at: m.at, segment_index: m.segment_index, offset_seconds: m.offset_seconds,
      text: m.text ?? null, filename: m.filename ?? null, kind: m.kind,
    })),
  };
}

/** Tramo mínimo para la web: lo que comparten el diario de Tatana y la copia del caso. */
export interface SegmentMeta {
  filename: string;
  track: "pantalla" | "camara";
  index: number;
  started_at: string;
  stopped_at?: string | null;
  state: string;
}

function isCase(x: Case | CaseSessionDto | RecordingSession): x is Case {
  return "nro_referencia" in x;
}

/**
 * Archivos de la sesión (tramos `saved`) del caso o del diario de Tatana. La
 * clasificación es por pertenencia al diario, no por prefijo (D-T5).
 */
export function sessionFilenames(
  ...sources: (Case | CaseSessionDto | RecordingSession | null | undefined)[]
): Set<string> {
  const out = new Set<string>();
  for (const src of sources) {
    if (!src) continue;
    const rs = isCase(src) ? src.recording_session : src;
    rs?.segments.forEach(s => { if (s.state === "saved") out.add(s.filename); });
  }
  return out;
}

/** Tramos por nombre (el diario manda sobre la copia del caso). */
export function segmentsByName(
  ...sources: (RecordingSession | CaseSessionDto | null | undefined)[]
): Record<string, SegmentMeta> {
  const out: Record<string, SegmentMeta> = {};
  for (const src of sources) {
    src?.segments.forEach(s => {
      out[s.filename] = {
        filename: s.filename, track: s.track, index: s.index,
        started_at: s.started_at, stopped_at: s.stopped_at ?? null, state: s.state,
      };
    });
  }
  return out;
}

/** Meta de un tramo a partir del nombre (sin diario a mano). `null` si no es un tramo. */
export function segmentMetaFromName(name: string): SegmentMeta | null {
  const m = SESSION_SEGMENT_RE.exec(name);
  if (!m) return null;
  const [, track, idx, date, time] = m;
  // Fecha y hora locales de la PC al iniciar el tramo (§5.2).
  const started = new Date(
    Number(date.slice(0, 4)), Number(date.slice(4, 6)) - 1, Number(date.slice(6, 8)),
    Number(time.slice(0, 2)), Number(time.slice(2, 4)), Number(time.slice(4, 6)),
  );
  return {
    filename: name, track: track as "pantalla" | "camara", index: Number(idx),
    started_at: started.toISOString(), stopped_at: null, state: "saved",
  };
}

const pad2 = (n: number) => String(n).padStart(2, "0");

/** Cronómetro `HH:MM:SS` desde `since` (negativo → 00:00:00). */
export function elapsedText(since: string | null | undefined, now: number): string {
  const start = since ? Date.parse(since) : NaN;
  const t = Number.isNaN(start) ? 0 : Math.max(0, Math.floor((now - start) / 1000));
  return `${pad2(Math.floor(t / 3600))}:${pad2(Math.floor((t % 3600) / 60))}:${pad2(t % 60)}`;
}

/** `mm:ss` (o `h:mm:ss` desde 1 h) de un desplazamiento dentro del video. */
export function offsetText(seconds: number): string {
  const t = Math.max(0, Math.floor(seconds));
  const h = Math.floor(t / 3600);
  const m = Math.floor((t % 3600) / 60);
  const s = t % 60;
  return h > 0 ? `${h}:${pad2(m)}:${pad2(s)}` : `${pad2(m)}:${pad2(s)}`;
}

/** Hora local `HH:MM` de un ISO, o `null`. */
export function hhmm(iso: string | null | undefined): string | null {
  if (!iso) return null;
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return null;
  return d.toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit", hour12: false });
}

/** "≈ 33 h" / "≈ 45 min". */
function minutesText(minutes: number): string {
  if (minutes < 120) return `≈ ${Math.max(0, Math.floor(minutes))} min`;
  return `≈ ${Math.floor(minutes / 60)} h`;
}

/**
 * "Espacio libre en esta PC: 120 GB (≈ 33 h de grabación, contando la copia del ZIP)."
 * `null` si Tatana no pudo medir el disco.
 */
export function capacityText(cap: SessionCapacity | null | undefined): string | null {
  if (!cap || typeof cap.available_bytes !== "number" || typeof cap.estimated_minutes !== "number") return null;
  const zip = cap.zip_on_same_volume ? ", contando la copia del ZIP" : "";
  return `Espacio libre en esta PC: ${formatBytes(cap.available_bytes)} (${minutesText(cap.estimated_minutes)} de grabación${zip}).`;
}

/** "No hay espacio en el disco de esta PC para grabar la sesión (quedan X)". */
export function noSpaceText(availableBytes: number | null | undefined): string {
  return typeof availableBytes === "number"
    ? `No hay espacio en el disco de esta PC para grabar la sesión (quedan ${formatBytes(availableBytes)}).`
    : "No hay espacio en el disco de esta PC para grabar la sesión.";
}

/** Texto de un aviso de Tatana durante la sesión (§9.4). `null` para los que no van como banner. */
export function warningText(w: SessionWarning): string | null {
  switch (w.kind) {
    case "screen_off":
      return "La pantalla del celular está apagada. La grabación de la sesión sigue.";
    case "screen_locked":
      return "La pantalla del celular está bloqueada. La grabación de la sesión sigue.";
    case "disk_low": {
      const space = typeof w.available_bytes === "number" ? formatBytes(w.available_bytes) : "poco";
      const mins = typeof w.minutes_left === "number" ? ` (≈ ${w.minutes_left} min de grabación)` : "";
      return `Queda poco espacio en esta PC: ${space}${mins}.`;
    }
    case "rotation_delayed":
      return "No se pudo abrir el tramo nuevo; el actual sigue grabando y se reintenta en un minuto.";
    case "camera_failed": {
      const base = `La cámara dejó de grabar a las ${hhmm(w.since) ?? "--:--"}.`;
      return w.detail === "permission"
        ? `${base} Tatana no tiene permiso para usar la cámara: habilitalo en Ajustes del Sistema > Privacidad y seguridad > Cámara.`
        : base;
    }
    default:
      return null;
  }
}

/** Bloqueo de "Generar" por la sesión (§9.6): texto y acción, o `null`. */
export interface GenerationBlocker {
  text: string;
  /** "Cerrar grabación de la sesión": `stop("cambio_de_paso")` + esperar el registro. */
  action: "close";
}

export const GENERATION_BLOCKER_TEXT = "Primero cerrá la grabación de la sesión";

/**
 * Bloqueo de la generación (§9.6):
 * - sesión activa en Tatana para el caso;
 * - o tramos `saved` del diario de Tatana sin registrar en el expediente;
 * - o la copia del diario pendiente de sincronizar con el backend.
 */
export function generationBlocker(
  state: SessionStateDto | null | undefined,
  journal: CaseSessionDto | null | undefined,
  cas: Pick<Case, "id" | "evidence"> | null | undefined,
  opts: { pendingSync?: boolean } = {},
): GenerationBlocker | null {
  if (!cas) return null;
  const sameCase = (id: string | null | undefined) => !!id && id.toLowerCase() === cas.id.toLowerCase();
  if (state && sameCase(state.case_id) && isSessionActive(state.state)) {
    return { text: GENERATION_BLOCKER_TEXT, action: "close" };
  }
  if (journal && sameCase(journal.case_id)) {
    if (isSessionActive(journal.state)) return { text: GENERATION_BLOCKER_TEXT, action: "close" };
    const registered = new Set((cas.evidence ?? []).map(e => e.filename));
    const unregistered = journal.segments.some(s => s.state === "saved" && !registered.has(s.filename));
    if (unregistered) return { text: GENERATION_BLOCKER_TEXT, action: "close" };
    if (journal.segments.some(s => s.state === "recording" || s.state === "closing")) {
      return { text: GENERATION_BLOCKER_TEXT, action: "close" };
    }
  }
  if (opts.pendingSync) return { text: GENERATION_BLOCKER_TEXT, action: "close" };
  return null;
}

/** Hora del último corte de un diario detenido (hueco abierto más reciente, o fin del último tramo). */
export function lastStopAt(journal: CaseSessionDto | RecordingSession | null | undefined): string | null {
  if (!journal) return null;
  const open = journal.gaps.filter(g => !g.to).map(g => g.from).sort();
  if (open.length > 0) return open[open.length - 1];
  const ends = journal.segments.map(s => s.stopped_at ?? "").filter(Boolean).sort();
  return ends.length > 0 ? ends[ends.length - 1] : null;
}

/** Resumen del registro audiovisual (§9.7). */
export interface SessionSummary {
  segments: number;
  totalSeconds: number;
  gaps: number;
  totalBytes: number;
}

export function sessionSummary(rs: RecordingSession | null | undefined): SessionSummary {
  const saved = (rs?.segments ?? []).filter(s => s.state === "saved");
  return {
    segments: saved.length,
    totalSeconds: saved.reduce((a, s) => a + (s.duration_seconds ?? s.recorded_seconds ?? 0), 0),
    gaps: (rs?.gaps ?? []).length,
    totalBytes: saved.reduce((a, s) => a + (s.size ?? 0), 0),
  };
}
