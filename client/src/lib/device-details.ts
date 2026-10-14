/**
 * Ficha técnica del dispositivo y diferencia de reloj (metadatos-dispositivo,
 * SDD §4.1, §4.4, §5.2, §5.3 y §10.1). Lógica pura (salvo `probeServerClock`,
 * que hace el sondeo NTP contra la API): claves, etiquetas, textos de reloj,
 * umbral, armado del `PUT` y modelos de vista comunes a la web.
 *
 * Nunca loguea valores de la ficha (el SSID incluido).
 */

import { AGENT_URL, type AgentDeviceClock, type AgentDeviceDetails } from "./agent";
import { api, type ClockMeasurement, type DeviceDetails, type PcServerProbe, type SaveDeviceDetailsRequest } from "./api";

/* ── Claves y motivos (Contrato compartido §8.3) ── */

export type DeviceDetailKey =
  | "serial_number" | "os_version" | "os_api_level" | "build_id" | "build_fingerprint" | "security_patch" | "kernel"
  | "network_operator" | "network_operator_numeric" | "sim_operator" | "sim_operator_numeric" | "sim_state"
  | "verified_boot_state" | "bootloader_locked" | "vbmeta_device_state" | "timezone" | "auto_time" | "auto_time_zone"
  | "wifi_state" | "wifi_ssid";

export type DeviceDetailReason =
  | "empty" | "not_exposed" | "not_applicable" | "not_connected" | "masked" | "timeout" | "error" | "invalid";

export type ClockReason = "timeout" | "error" | "invalid" | "not_exposed";
export type PcServerReason = "not_measured" | "invalid";

/** Las 20 claves, en el orden de presentación (§4.1). */
export const DEVICE_DETAIL_KEYS: readonly DeviceDetailKey[] = [
  "serial_number", "os_version", "os_api_level", "build_id", "build_fingerprint", "security_patch", "kernel",
  "network_operator", "network_operator_numeric", "sim_operator", "sim_operator_numeric", "sim_state",
  "verified_boot_state", "bootloader_locked", "vbmeta_device_state", "timezone", "auto_time", "auto_time_zone",
  "wifi_state", "wifi_ssid",
];

export const FIELD_LABEL: Record<DeviceDetailKey, string> = {
  serial_number: "Número de serie",
  os_version: "Versión del sistema",
  os_api_level: "Nivel de API",
  build_id: "Compilación",
  build_fingerprint: "Huella de compilación",
  security_patch: "Parche de seguridad",
  kernel: "Kernel",
  network_operator: "Operador de red",
  network_operator_numeric: "Código de red (MCC/MNC)",
  sim_operator: "Operador de la SIM",
  sim_operator_numeric: "Código de la SIM",
  sim_state: "Estado de la SIM",
  verified_boot_state: "Arranque verificado",
  bootloader_locked: "Bootloader bloqueado",
  vbmeta_device_state: "Estado de vbmeta",
  timezone: "Zona horaria",
  auto_time: "Hora automática",
  auto_time_zone: "Zona horaria automática",
  wifi_state: "Wifi",
  wifi_ssid: "Nombre de la red (SSID)",
};

/** Textos de "No disponible" (§4.4): los mismos que el informe. */
export const REASON_LABEL: Record<DeviceDetailReason, string> = {
  empty: "el dispositivo no devolvió el dato (puede estar restringido por el sistema operativo)",
  not_exposed: "el sistema operativo no lo informa por una consulta de solo lectura",
  not_applicable: "no aplica a esta plataforma",
  not_connected: "sin conexión wifi",
  masked: "el sistema no informó el nombre de la red",
  timeout: "el dispositivo no respondió a tiempo",
  error: "falló la lectura",
  invalid: "el valor devuelto no tiene un formato válido y se descartó",
};

const REASONS = new Set<string>(Object.keys(REASON_LABEL));

/** Motivo conocido, o `error` si vino algo fuera del enum (misma regla que la API, §4.5). */
function normalizeReason(r: string | null | undefined): DeviceDetailReason {
  return r && REASONS.has(r) ? (r as DeviceDetailReason) : "error";
}

/** Texto del motivo de un reloj sin hora (los códigos son un subconjunto de §4.4). */
export function clockReasonLabel(r: string | null | undefined): string {
  return REASON_LABEL[normalizeReason(r)];
}

/** Valores largos o técnicos: monoespaciados, sin traducción automática y con `break-all` (§10.8). */
export const MONO_KEYS: ReadonlySet<DeviceDetailKey> = new Set<DeviceDetailKey>([
  "serial_number", "os_api_level", "build_id", "build_fingerprint", "security_patch", "kernel",
  "network_operator_numeric", "sim_operator_numeric", "sim_state", "verified_boot_state", "bootloader_locked",
  "vbmeta_device_state", "timezone",
]);

/* ── Duraciones y textos de reloj (§5.3) ── */

/**
 * `13 → "13 s"`, `133 → "2 min 13 s"`, `3725 → "1 h 2 min 5 s"`, `90061 → "1 d 1 h 1 min"`.
 * Sin las partes en cero; con días, sin segundos. `0 → "0 s"`.
 */
export function formatDuration(seconds: number): string {
  const total = Math.max(0, Math.round(Math.abs(seconds)));
  if (total === 0) return "0 s";
  const d = Math.floor(total / 86_400);
  const h = Math.floor((total % 86_400) / 3_600);
  const m = Math.floor((total % 3_600) / 60);
  const s = total % 60;
  const parts: string[] = [];
  if (d) parts.push(`${d} d`);
  if (h) parts.push(`${h} h`);
  if (m) parts.push(`${m} min`);
  if (s && !d) parts.push(`${s} s`);
  return parts.join(" ");
}

/** `N` (segundos, half away from zero) y `M` (margen, mínimo 1 s), §5.3. */
function skewParts(ms: number, marginMs: number | null | undefined): { n: number; m: number; ahead: boolean } {
  // Math.round sobre el valor absoluto = half away from zero.
  const n = Math.round(Math.abs(ms) / 1000);
  const m = Math.max(1, Math.ceil((marginMs ?? 0) / 1000));
  return { n, m, ahead: ms > 0 };
}

/** Web, presente: "Adelanta 10 s respecto de esta PC (± 1 s)" / "Coincide con esta PC (± 1 s)". */
export function describeDeviceSkew(ms: number, marginMs: number | null | undefined): string {
  const { n, m, ahead } = skewParts(ms, marginMs);
  if (n === 0) return `Coincide con esta PC (± ${formatDuration(m)})`;
  return `${ahead ? "Adelanta" : "Atrasa"} ${formatDuration(n)} respecto de esta PC (± ${formatDuration(m)})`;
}

/** Web, PC↔servidor: "Esta PC adelanta 2 s respecto del servidor (± 1 s)". */
export function describePcServerSkew(ms: number, marginMs: number | null | undefined): string {
  const { n, m, ahead } = skewParts(ms, marginMs);
  if (n === 0) return `Esta PC coincide con el servidor (± ${formatDuration(m)})`;
  return `Esta PC ${ahead ? "adelanta" : "atrasa"} ${formatDuration(n)} respecto del servidor (± ${formatDuration(m)})`;
}

/** Trazabilidad (§10.7): "El dispositivo adelanta 10 s respecto de la PC (± 1 s)". */
export function describeEventSkew(ms: number, marginMs: number | null | undefined): string {
  const { n, m, ahead } = skewParts(ms, marginMs);
  if (n === 0) return `El dispositivo coincide con la PC (± ${formatDuration(m)})`;
  return `El dispositivo ${ahead ? "adelanta" : "atrasa"} ${formatDuration(n)} respecto de la PC (± ${formatDuration(m)})`;
}

/** `|ms| > umbral × 1000`, estricto (D6). `null`/`undefined` nunca supera. */
export function exceedsThreshold(ms: number | null | undefined, thresholdSeconds: number): boolean {
  return typeof ms === "number" && Number.isFinite(ms) && Math.abs(ms) > thresholdSeconds * 1000;
}

/* ── Conteo de "no disponibles" (§4.4) ── */

type AnyField = { value?: string | null; reason?: string | null } | undefined;

/**
 * Campos con `reason` distinto de `not_applicable` y `not_connected`. Una clave
 * que falta (o sin valor ni motivo) cuenta como `error`, igual que en la API.
 */
export function unavailableCount(fields: Partial<Record<DeviceDetailKey, AnyField>> | null | undefined): number {
  let n = 0;
  for (const k of DEVICE_DETAIL_KEYS) {
    const f = fields?.[k];
    const reason = fieldReason(f);
    if (reason && reason !== "not_applicable" && reason !== "not_connected") n++;
  }
  return n;
}

/** `null` si el campo tiene valor; si no, su motivo (normalizado). `reason` gana sobre `value` (§4.5). */
function fieldReason(f: AnyField): DeviceDetailReason | null {
  if (f?.reason) return normalizeReason(f.reason);
  if (typeof f?.value === "string" && f.value !== "") return null;
  return "error";
}

/* ── Sondeo PC↔servidor (§5.2) ── */

/** El navegador y Tatana comparten el reloj solo si Tatana está en loopback. */
export function isLoopbackAgent(): boolean {
  try {
    const host = new URL(AGENT_URL).hostname;
    return host === "localhost" || host === "127.0.0.1" || host === "[::1]";
  } catch {
    return false;
  }
}

/**
 * Sondeo NTP de una vuelta contra `GET /api/time`. `null` si falla o si Tatana
 * no es loopback (otro reloj: la API registra `not_measured`).
 */
export async function probeServerClock(): Promise<PcServerProbe | null> {
  if (!isLoopbackAgent()) return null;
  try {
    const before = new Date();
    const { server_time } = await api.getServerTime();
    const after = new Date();
    if (typeof server_time !== "string" || !server_time) return null;
    return { pc_before: before.toISOString(), server_time, pc_after: after.toISOString() };
  } catch {
    return null;
  }
}

/**
 * Cuerpo de `PUT …/device-details` (§8.2). `pc_before`/`pc_after`/`device_time`
 * se reenvían **como strings, tal como vinieron** (regla de transporte §8.1):
 * pasarlos por `Date` los truncaría a ms. `includeSsid` `undefined` = conservar
 * lo guardado (retoma, "Volver a leer" en edición).
 */
export function toSaveRequest(
  reading: AgentDeviceDetails, probe: PcServerProbe | null, includeSsid: boolean | undefined,
): SaveDeviceDetailsRequest {
  return {
    platform: reading.platform,
    connection_id: reading.serial,
    agent_version: reading.agent_version,
    fields: reading.fields,
    clock: clockForRequest(reading.clock),
    ...(probe ? { pc_server_probe: probe } : {}),
    ...(includeSsid === undefined ? {} : { include_ssid_in_report: includeSsid }),
  };
}

/** `clock` del `PUT` (ficha o medición de cierre): crudo, sin recalcular nada. */
export function clockForRequest(c: AgentDeviceClock): SaveDeviceDetailsRequest["clock"] {
  return {
    pc_before: c.pc_before ?? null,
    pc_after: c.pc_after ?? null,
    device_time: c.device_time ?? null,
    resolution_ms: c.resolution_ms ?? null,
    reason: c.reason ?? null,
  };
}

/* ── Modelo de vista común (paso 2, diálogo y detalle del caso) ── */

export interface DetailRowView {
  key: DeviceDetailKey;
  label: string;
  /** Texto a mostrar (ya traducido si corresponde), o `null` si no hay dato. */
  value: string | null;
  reason: DeviceDetailReason | null;
  mono: boolean;
}

export interface ClockView {
  kind: "start" | "close";
  /** Instante de la medición (ISO, reloj del servidor) o `null` si es una lectura sin guardar. */
  recordedAt: string | null;
  deviceMinusPcMs: number | null;
  marginMs: number | null;
  /** Motivo si no hubo hora. */
  reason: string | null;
  pcMinusServerMs: number | null;
  pcServerMarginMs: number | null;
  pcServerReason: string | null;
}

export interface DetailsView {
  /** `agent` = lectura de esta sesión sin guardar; `case` = ficha registrada. */
  source: "agent" | "case";
  platform: "android" | "ios";
  connectionId: string;
  agentVersion: string;
  /** Hora de la lectura: ISO del servidor (`case`) o ms locales (`agent`). */
  readAt: string | number | null;
  rows: DetailRowView[];
  clockStart: ClockView | null;
  clockClose: ClockView | null;
  includeSsid: boolean;
  unavailable: number;
  /** El SSID (si se leyó): solo para el diálogo, nunca para trazabilidad ni logs. */
  ssid: string | null;
  wifiConnected: boolean;
}

const WIFI_STATE_LABEL: Record<string, string> = {
  connected: "Conectado",
  disconnected: "Activado, sin conexión",
  disabled: "Desactivado",
};

const ON_OFF: Record<string, string> = { "1": "Activada", "0": "Desactivada" };

/** Valor legible para la web. Arranque y códigos van crudos (D9: sin calificar). */
function displayValue(key: DeviceDetailKey, raw: string, ssid: string | null): string {
  if (key === "auto_time" || key === "auto_time_zone") return ON_OFF[raw] ?? raw;
  if (key === "wifi_state") {
    if (raw === "connected" && ssid) return `Conectado a «${ssid}»`;
    return WIFI_STATE_LABEL[raw] ?? raw;
  }
  return raw;
}

function buildRows(fields: Partial<Record<DeviceDetailKey, AnyField>>): {
  rows: DetailRowView[]; ssid: string | null; wifiConnected: boolean;
} {
  const ssidField = fields.wifi_ssid;
  const ssid = !ssidField?.reason && typeof ssidField?.value === "string" && ssidField.value ? ssidField.value : null;
  const wifi = fields.wifi_state;
  const wifiConnected = !wifi?.reason && wifi?.value === "connected";
  const rows = DEVICE_DETAIL_KEYS.map((key): DetailRowView => {
    const f = fields[key];
    const reason = fieldReason(f);
    return {
      key,
      label: FIELD_LABEL[key],
      value: reason ? null : displayValue(key, f!.value as string, ssid),
      reason,
      mono: MONO_KEYS.has(key),
    };
  });
  return { rows, ssid, wifiConnected };
}

function num(v: number | null | undefined): number | null {
  return typeof v === "number" && Number.isFinite(v) ? v : null;
}

function clockFromAgent(c: AgentDeviceClock | null | undefined): ClockView | null {
  if (!c) return null;
  const ms = num(c.device_minus_pc_ms);
  return {
    kind: "start",
    recordedAt: null,
    deviceMinusPcMs: ms,
    marginMs: num(c.margin_ms),
    reason: ms === null ? (c.reason ?? "error") : null,
    pcMinusServerMs: null,
    pcServerMarginMs: null,
    pcServerReason: null,
  };
}

function clockFromCase(c: ClockMeasurement | null | undefined, kind: "start" | "close"): ClockView | null {
  if (!c) return null;
  const ms = num(c.device_minus_pc_ms);
  return {
    kind,
    recordedAt: c.recorded_at ?? null,
    deviceMinusPcMs: ms,
    marginMs: num(c.margin_ms),
    reason: ms === null ? (c.reason ?? "error") : null,
    pcMinusServerMs: num(c.pc_minus_server_ms),
    pcServerMarginMs: num(c.pc_server_margin_ms),
    pcServerReason: c.pc_server_reason ?? null,
  };
}

/** Lectura de Tatana todavía no guardada (alta del caso). `readAt` = `Date.now()` del hook. */
export function detailsViewFromAgent(
  reading: AgentDeviceDetails, readAt: number | null, includeSsid: boolean,
): DetailsView {
  const fields = reading.fields ?? ({} as AgentDeviceDetails["fields"]);
  const { rows, ssid, wifiConnected } = buildRows(fields);
  return {
    source: "agent",
    platform: reading.platform,
    connectionId: reading.serial,
    agentVersion: reading.agent_version,
    readAt,
    rows,
    clockStart: clockFromAgent(reading.clock),
    clockClose: null,
    includeSsid,
    unavailable: unavailableCount(fields),
    ssid,
    wifiConnected,
  };
}

/** Ficha registrada en el caso (`Case.device_details`). */
export function detailsViewFromCase(details: DeviceDetails): DetailsView {
  const fields = details.fields ?? {};
  const { rows, ssid, wifiConnected } = buildRows(fields);
  return {
    source: "case",
    platform: details.platform,
    connectionId: details.connection_id,
    agentVersion: details.agent_version,
    readAt: details.read_at ?? null,
    rows,
    clockStart: clockFromCase(details.clock_start, "start"),
    clockClose: clockFromCase(details.clock_close, "close"),
    includeSsid: details.include_ssid_in_report === true,
    unavailable: unavailableCount(fields),
    ssid,
    wifiConnected,
  };
}

/** `true` si alguna medición del dispositivo supera el umbral. */
export function viewExceedsThreshold(view: DetailsView | null, thresholdSeconds: number): boolean {
  if (!view) return false;
  return [view.clockStart, view.clockClose].some(c => exceedsThreshold(c?.deviceMinusPcMs, thresholdSeconds));
}

/** "14/10/2026 15:22" (o con segundos) en hora local. `null` si no parsea. */
export function formatDateTime(at: string | number | null | undefined, withSeconds = false): string | null {
  if (at === null || at === undefined || at === "") return null;
  const d = new Date(at);
  if (Number.isNaN(d.getTime())) return null;
  return d.toLocaleString("es-AR", {
    day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit",
    ...(withSeconds ? { second: "2-digit" } : {}),
    hour12: false,
  });
}

/* ── Estado de UI que la página pasa al paso 2 ── */

export type DeviceDetailsStatus = "idle" | "unsupported" | "loading" | "ready" | "error";

export interface DeviceDetailsUi {
  status: DeviceDetailsStatus;
  /** Lo que se muestra: la ficha guardada (edición) o la lectura de esta sesión (alta). */
  view: DetailsView | null;
  /** Mensaje de la última lectura/guardado fallido. */
  error: string | null;
  thresholdSeconds: number;
  /** "Volver a leer" visible: caso editable (o nuevo), mismo dispositivo y conectado. */
  canReread: boolean;
  onReread: () => void;
  /** Guardando la opción del SSID (solo edición). */
  ssidSaving: boolean;
  onIncludeSsidChange: (include: boolean) => void;
}
