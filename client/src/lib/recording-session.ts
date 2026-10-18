/**
 * Grabación de la sesión completa (grabacion-sesion-completa, SDD §9.1).
 * Funciones puras: sin React ni red, exportadas para cuando haya tests.
 */

import type {
  CaseSessionDto, SessionCameraInfo, SessionCapacity, SessionReason, SessionSegmentDto, SessionStateDto,
  SessionStateName, SessionWarning,
} from "./agent";
import type {
  Case, RecordingSession, RecordingSessionCamera, RecordingSessionMode, RecordingSessionSegment,
  RecordingSessionSnapshot,
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

/**
 * Se puede "Marcar momento" (grabacion-sesion-completa-f2 §9.2, [DU1=A]): la pantalla
 * graba, o espera el cable mientras la cámara del procedimiento sigue grabando.
 */
export function canMarkSession(state: Pick<SessionStateDto, "state" | "camera_state"> | null | undefined): boolean {
  if (!state) return false;
  return isSessionRecording(state.state) || (state.state === "waiting_device" && state.camera_state === "recording");
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

/** Cámara armada campo por campo (F2 §7.4): nunca viaja el `id` técnico al backend. */
function cameraInfo(c: SessionCameraInfo | RecordingSessionCamera | null | undefined): RecordingSessionCamera | null {
  if (!c || typeof c.name !== "string") return null;
  return { name: c.name, width: c.width, height: c.height, fps: c.fps, with_audio: c.with_audio === true };
}

const numOrNull = (n: number | null | undefined): number | null => (typeof n === "number" && Number.isFinite(n) ? n : null);

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
    camera: s.track === "camara" ? cameraInfo(s.camera) : null,
  }));
  return {
    session_id: dto.session_id,
    platform: dto.platform,
    connection_id: dto.serial,
    agent_version: dto.agent_version ?? null,
    segment_minutes: dto.segment_minutes,
    audio: dto.audio ?? null,
    camera: cameraInfo(dto.camera),
    state: dto.state,
    segments,
    gaps: dto.gaps.map(g => ({
      id: g.id, track: g.track, from: g.from, to: g.to ?? null, reason: g.reason, segment_lost: g.segment_lost === true,
    })),
    marks: dto.marks.map(m => ({
      id: m.id, at: m.at,
      segment_index: numOrNull(m.segment_index), offset_seconds: numOrNull(m.offset_seconds),
      camera_segment_index: numOrNull(m.camera_segment_index), camera_offset_seconds: numOrNull(m.camera_offset_seconds),
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
export function capacityText(
  cap: SessionCapacity | null | undefined,
  opts: { camera?: boolean } = {},
): string | null {
  if (!cap || typeof cap.available_bytes !== "number" || typeof cap.estimated_minutes !== "number") return null;
  const zip = cap.zip_on_same_volume ? ", contando la copia del ZIP" : "";
  const cam = opts.camera ? " con cámara" : "";
  return `Espacio libre en esta PC: ${formatBytes(cap.available_bytes)} (${minutesText(cap.estimated_minutes)} de grabación${cam}${zip}).`;
}

/** "No hay espacio en el disco de esta PC para grabar la sesión (quedan X)". */
export function noSpaceText(availableBytes: number | null | undefined): string {
  return typeof availableBytes === "number"
    ? `No hay espacio en el disco de esta PC para grabar la sesión (quedan ${formatBytes(availableBytes)}).`
    : "No hay espacio en el disco de esta PC para grabar la sesión.";
}

/** Texto de un aviso de Tatana durante la sesión (§9.4). `null` para los que no van como banner. */
export function warningText(w: SessionWarning, opts: { cameraNeverStarted?: boolean } = {}): string | null {
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
    case "camera_failed":
      return cameraFailText(w.detail, { since: w.since, neverStarted: opts.cameraNeverStarted });
    default:
      return null;
  }
}

/** Agregado del texto de falla de la cámara según el `detail` de Tatana (F2 §9.1). */
export function cameraFailDetailText(detail: string | null | undefined): string | null {
  switch (detail) {
    case "permission":
      return "Tatana no tiene permiso para usar la cámara: habilitalo en Ajustes del Sistema > Privacidad y seguridad > Cámara.";
    case "blocked":
      return "Windows no deja usar la cámara: revisá Configuración > Privacidad y seguridad > Cámara, o cerrá la aplicación que la esté usando.";
    case "not_found":
      return "No se encuentra la cámara: revisá que esté conectada.";
    case "unsupported":
      return "La cámara no aceptó ningún modo de video.";
    default:
      return null; // `stalled`, sin detail o uno desconocido
  }
}

/**
 * Texto de la cámara que falló (F2 §9.1). `since` = hora del aviso; `neverStarted` = la cámara
 * no llegó a grabar en esta sesión (el aviso llegó en el arranque, o es la vista previa).
 */
export function cameraFailText(
  detail: string | null | undefined,
  opts: { since?: string | null; neverStarted?: boolean; preview?: boolean } = {},
): string {
  const base = opts.preview ? "No se pudo abrir la cámara para la vista previa."
    : opts.neverStarted
    ? "No se pudo empezar a grabar la cámara."
    : `La cámara dejó de grabar a las ${hhmm(opts.since) ?? "--:--"}.`;
  const extra = cameraFailDetailText(detail);
  return extra ? `${base} ${extra}` : base;
}

/** "08:32": `mm:ss` de un desplazamiento (los minutos pueden pasar de 59), como el informe (§6.4). */
export function minuteSecond(seconds: number): string {
  const t = Math.max(0, Math.floor(seconds));
  return `${pad2(Math.floor(t / 60))}:${pad2(t % 60)}`;
}

/** Ubicación de una marca en las dos pistas (los campos que comparten Tatana y el backend). */
export interface MarkLocation {
  segment_index?: number | null;
  offset_seconds?: number | null;
  camera_segment_index?: number | null;
  camera_offset_seconds?: number | null;
}

/**
 * Dónde cae la marca (F2 §9.1, mismos textos que el informe §6.4):
 * - sin cámara en el caso: "tramo 1, minuto 08:32";
 * - con cámara: "pantalla: tramo 1, minuto 08:32; cámara: tramo 2, minuto 07:50", y
 *   "no grababa en ese momento" en la pista que no tiene ubicación.
 */
export function markLocationText(mark: MarkLocation, hasCamera: boolean): string {
  const loc = (i: number | null | undefined, off: number | null | undefined) =>
    typeof i === "number" ? `tramo ${i}, minuto ${minuteSecond(typeof off === "number" ? off : 0)}` : null;
  const screen = loc(mark.segment_index, mark.offset_seconds);
  if (!hasCamera) return screen ?? "la pantalla no grababa en ese momento";
  const camera = loc(mark.camera_segment_index, mark.camera_offset_seconds);
  return `pantalla: ${screen ?? "no grababa en ese momento"}; cámara: ${camera ?? "no grababa en ese momento"}`;
}

/** ¿La sesión tuvo cámara? (tramos `camara` o marcas con ubicación en la cámara). */
export function sessionHasCamera(
  rs: Pick<RecordingSession, "segments" | "marks"> | Pick<CaseSessionDto, "segments" | "marks"> | null | undefined,
): boolean {
  if (!rs) return false;
  return rs.segments.some(s => s.track === "camara") || rs.marks.some(m => typeof m.camera_segment_index === "number");
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

/** Tramo mínimo para medir duraciones (copia del caso o diario de Tatana). */
type TimedSegment = Pick<RecordingSessionSegment, "track" | "state" | "started_at"> & {
  stopped_at?: string | null;
  recorded_seconds?: number | null;
  duration_seconds?: number | null;
};

/**
 * Duración de un tramo (B-9, D10 = A): el tiempo grabado, la misma cifra del informe y la
 * trazabilidad. `duration_seconds` (lo que dura el archivo) solo si falta el grabado.
 */
export function segmentSeconds(s: Pick<TimedSegment, "recorded_seconds" | "duration_seconds">): number {
  if (typeof s.recorded_seconds === "number" && Number.isFinite(s.recorded_seconds)) return s.recorded_seconds;
  return typeof s.duration_seconds === "number" && Number.isFinite(s.duration_seconds) ? s.duration_seconds : 0;
}

/**
 * Segundos cubiertos por los tramos `saved` de una pista (B-9): unión de intervalos
 * [inicio, fin], los solapados cuentan una vez. Misma cuenta que
 * `ReportSessionTexts.CoveredSeconds` del backend (orden por `started_at`; un fin anterior
 * al inicio cuenta como el inicio; un tramo sin fin no suma).
 */
export function coveredSeconds(segments: readonly TimedSegment[] | null | undefined, track: "pantalla" | "camara"): number {
  const rows = (segments ?? [])
    .filter(s => s.state === "saved" && s.track === track)
    .map(s => {
      const start = Date.parse(s.started_at);
      const stop = s.stopped_at ? Date.parse(s.stopped_at) : NaN;
      return { start, end: Number.isNaN(stop) || stop < start ? start : stop };
    })
    .filter(r => !Number.isNaN(r.start))
    .sort((a, b) => a.start - b.start);
  let total = 0;
  let cur: { start: number; end: number } | null = null;
  for (const r of rows) {
    if (cur && r.start <= cur.end) {
      if (r.end > cur.end) cur.end = r.end;
      continue;
    }
    if (cur) total += cur.end - cur.start;
    cur = { ...r };
  }
  if (cur) total += cur.end - cur.start;
  return total / 1000;
}

/** Resumen de una pista. */
export interface TrackSummary {
  segments: number;
  coveredSeconds: number;
}

/** Resumen del registro audiovisual (§9.7; por pista desde F2 §9.1, B-9). */
export interface SessionSummary {
  screen: TrackSummary;
  camera: TrackSummary;
  gaps: number;
  totalBytes: number;
}

export function sessionSummary(rs: RecordingSession | null | undefined): SessionSummary {
  const saved = (rs?.segments ?? []).filter(s => s.state === "saved");
  const track = (t: "pantalla" | "camara"): TrackSummary => ({
    segments: saved.filter(s => s.track === t).length,
    coveredSeconds: coveredSeconds(saved, t),
  });
  return {
    screen: track("pantalla"),
    camera: track("camara"),
    gaps: (rs?.gaps ?? []).length,
    totalBytes: saved.reduce((a, s) => a + (s.size ?? 0), 0),
  };
}

/** Configuración de la cámara en palabras, para la web (F2 §9.3). */
export function cameraConfigText(c: RecordingSessionCamera): string {
  return `Cámara «${c.name}» (${c.width}×${c.height}, ${c.fps} fps, ${c.with_audio ? "con micrófono" : "sin audio"})`;
}

/**
 * Configuraciones de cámara distintas usadas en la sesión, en orden de aparición
 * (tramos consecutivos con la misma configuración cuentan una vez, como el informe §6.4).
 */
export function cameraConfigs(rs: Pick<RecordingSession, "segments" | "camera"> | null | undefined): RecordingSessionCamera[] {
  if (!rs) return [];
  const cams = rs.segments
    .filter(s => s.state === "saved" && s.track === "camara")
    .sort((a, b) => a.started_at.localeCompare(b.started_at) || a.index - b.index);
  if (cams.length === 0) return [];
  const key = (c: RecordingSessionCamera) => `${c.name}|${c.width}|${c.height}|${c.fps}|${c.with_audio}`;
  const out: RecordingSessionCamera[] = [];
  const seen = new Set<string>();
  for (const s of cams) {
    const c = s.camera ?? rs.camera ?? null; // tramo de un backend sin la config por tramo: la de cabecera
    if (!c) continue;
    const k = key(c);
    if (seen.has(k)) continue;
    seen.add(k);
    out.push(c);
  }
  return out;
}
