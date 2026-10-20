/**
 * Llamadas del proceso main al agente local (SDD §5.4 y §13.A).
 * Con `http` de Node: no manda `Origin` ni `Sec-Fetch-Site`, que es lo que
 * exige `POST/DELETE /agent/maintenance` (403 `maintenance_local_only` si no).
 * Timeout de 3 s en todas.
 */
import * as http from 'http'
import {
  classifyMaintenance, classifyTransportError,
  type EnterMaintenanceResult, type MaintenanceRaw,
} from './updater/maintenance-outcome'

/**
 * `kind` ∈ recording_android | recording_ios | airplay_session | request en `operations`
 * (bloquean la actualización). `video_postprocess` viene en `background_operations`
 * (no bloquea; Tatana lo cancela al entrar en mantenimiento). Un Tatana anterior a
 * `video_postprocess_v1` todavía lo manda en `operations`.
 */
export interface AgentOperation {
  kind: string
  since: string
  detail?: string
}

export interface AgentLocalConfig {
  path: string
  exists: boolean
  loaded: boolean
  overrides_allowed_origins: boolean
  /** Solo si el archivo es inválido. */
  error?: string
}

/** Estado de la grabación de la sesión (grabacion-sesion-completa §7.2.7); solo lo que usa Electron. */
export interface AgentRecordingSession {
  case_id: string
  state: string
}

/** Estados de una grabación de la sesión en curso (§5.3). */
export const ACTIVE_SESSION_STATES: ReadonlySet<string> = new Set([
  'starting', 'recording', 'rotating', 'waiting_device', 'stopping',
])

export interface AgentStateResponse {
  version: string
  mode: 'installed' | 'portable'
  busy: boolean
  maintenance: boolean
  operations: AgentOperation[]
  /** Operaciones que no bloquean y se cancelan al actualizar. `[]` con un Tatana viejo. */
  background_operations: AgentOperation[]
  local_config: AgentLocalConfig
  /** Solo con una grabación de la sesión activa (Tatana lo omite si no hay). */
  recording_session?: AgentRecordingSession
}

export type { EnterMaintenanceResult } from './updater/maintenance-outcome'

const TIMEOUT_MS = 3000

/**
 * Request HTTP al agente. Distingue la respuesta de la falla de transporte
 * (`refused` / `timeout` / `reset` / `other`): solo `refused` prueba que nadie
 * escucha el puerto. Un timeout NO significa "agente caído" (D5).
 */
function request(
  port: number, method: string, path: string, body?: unknown, timeoutMs = TIMEOUT_MS,
): Promise<MaintenanceRaw> {
  return new Promise((resolve) => {
    let settled = false
    const done = (v: MaintenanceRaw) => { if (!settled) { settled = true; resolve(v) } }
    const payload = body === undefined ? undefined : Buffer.from(JSON.stringify(body), 'utf8')
    const req = http.request(
      {
        host: '127.0.0.1',
        port,
        method,
        path,
        headers: payload
          ? { 'Content-Type': 'application/json', 'Content-Length': String(payload.length) }
          : {},
        timeout: timeoutMs,
      },
      (res) => {
        let data = ''
        res.setEncoding('utf8')
        res.on('data', (c: string) => { if (data.length < 256 * 1024) data += c })
        res.on('end', () => done({ kind: 'response', status: res.statusCode ?? 0, body: parseJson(data) }))
        res.on('error', (err) => done({ kind: 'failure', failure: classifyTransportError(err) }))
      },
    )
    req.on('timeout', () => { done({ kind: 'failure', failure: 'timeout' }); req.destroy() })
    req.on('error', (err) => done({ kind: 'failure', failure: classifyTransportError(err) }))
    if (payload) req.write(payload)
    req.end()
  })
}

function parseJson(text: string): unknown {
  try { return JSON.parse(text) } catch { return null }
}

/** `GET /agent/state`. `null` si el agente no responde o es anterior a `agent_state_v1`. */
export async function getAgentState(port: number, timeoutMs = TIMEOUT_MS): Promise<AgentStateResponse | null> {
  const res = await request(port, 'GET', '/agent/state', undefined, timeoutMs)
  if (res.kind !== 'response' || res.status !== 200) return null
  const raw = res.body as Partial<AgentStateResponse> | null
  if (!raw || typeof raw !== 'object' || typeof raw.version !== 'string') return null
  return {
    version: raw.version,
    mode: raw.mode === 'installed' ? 'installed' : 'portable',
    busy: raw.busy === true,
    maintenance: raw.maintenance === true,
    operations: Array.isArray(raw.operations) ? raw.operations.filter((o) => o && typeof o.kind === 'string') : [],
    background_operations: Array.isArray(raw.background_operations)
      ? raw.background_operations.filter((o) => o && typeof o.kind === 'string')
      : [],
    local_config: {
      path: String(raw.local_config?.path ?? ''),
      exists: raw.local_config?.exists === true,
      loaded: raw.local_config?.loaded === true,
      overrides_allowed_origins: raw.local_config?.overrides_allowed_origins === true,
      ...(typeof raw.local_config?.error === 'string' ? { error: raw.local_config.error } : {}),
    },
    ...(raw.recording_session && typeof raw.recording_session === 'object'
      && typeof raw.recording_session.state === 'string'
      ? { recording_session: { case_id: String(raw.recording_session.case_id ?? ''), state: raw.recording_session.state } }
      : {}),
  }
}

/**
 * `POST /agent/prepare-shutdown` (grabacion-sesion-completa §7.2.9, solo local):
 * Tatana cierra y guarda el tramo en curso de la sesión antes de que Electron lo
 * mate. Va sin `Origin` (proceso main). Puede tardar (hash del tramo): 25 s.
 */
export async function prepareShutdown(
  port: number, timeoutMs = 25_000,
): Promise<{ ok: boolean; had_session: boolean }> {
  const res = await request(port, 'POST', '/agent/prepare-shutdown', undefined, timeoutMs)
  if (res.kind !== 'response' || res.status !== 200) return { ok: false, had_session: false }
  const body = res.body as { had_session?: unknown } | null
  return { ok: true, had_session: body?.had_session === true }
}

/**
 * `POST /agent/maintenance` (atómico: 409 `agent_busy` si hay operaciones en curso).
 * `agentRunning` = `agentProcess.isRunning()`: con el agente vivo, un timeout o
 * reset es `error` (no se instala), nunca `unreachable` (ver maintenance-outcome.ts).
 */
export async function enterMaintenance(port: number, agentRunning: boolean, ttlSeconds = 120): Promise<EnterMaintenanceResult> {
  return classifyMaintenance(await request(port, 'POST', '/agent/maintenance', { ttl_seconds: ttlSeconds }), agentRunning)
}

/** `DELETE /agent/maintenance` (idempotente). */
export async function exitMaintenance(port: number): Promise<boolean> {
  const res = await request(port, 'DELETE', '/agent/maintenance')
  return res.kind === 'response' && (res.status === 204 || res.status === 200)
}

// ── /health (tatana-linux §10.A) ─────────────────────────────────────────────
// snake_case_lower como lo serializa el agente; los `null` no viajan.

export type AgentOs = 'windows' | 'macos' | 'linux'

export type AgentLinuxCheckId =
  | 'usbmuxd' | 'android_usb' | 'avahi' | 'uxplay' | 'camera' | 'microphone' | 'sandbox'

export type AgentLinuxCheckStatus = 'ok' | 'missing' | 'outdated' | 'warning' | 'not_applicable'

export interface AgentLinuxCheck {
  id: AgentLinuxCheckId
  status: AgentLinuxCheckStatus
  /** `true` solo para `usbmuxd` y `android_usb`. */
  required: boolean
  /** Castellano corto (cuando no está `ok`). */
  detail?: string
}

export interface AgentLinuxStatus {
  distro_id: string
  distro_version?: string
  distro_name?: string
  distro_like?: string
  glibc_version?: string
  package_manager: 'apt' | 'dnf' | 'pacman' | 'zypper' | 'unknown'
  setup_supported: boolean
  setup_state: 'ok' | 'incomplete'
  /** Siempre los 7, en el orden de `AgentLinuxCheckId`. */
  checks: AgentLinuxCheck[]
}

/** Lo que Electron lee de `/health` (el resto de los campos no se tipa acá). */
export interface AgentHealth {
  version?: string
  os?: AgentOs
  /** Solo en Linux. */
  linux?: AgentLinuxStatus
}

const CHECK_IDS: ReadonlySet<string> = new Set(['usbmuxd', 'android_usb', 'avahi', 'uxplay', 'camera', 'microphone', 'sandbox'])
const CHECK_STATUSES: ReadonlySet<string> = new Set(['ok', 'missing', 'outdated', 'warning', 'not_applicable'])
const PACKAGE_MANAGERS: ReadonlySet<string> = new Set(['apt', 'dnf', 'pacman', 'zypper', 'unknown'])

function optStr(v: unknown): string | undefined {
  return typeof v === 'string' && v ? v : undefined
}

function toLinuxStatus(raw: unknown): AgentLinuxStatus | undefined {
  if (!raw || typeof raw !== 'object') return undefined
  const r = raw as Record<string, unknown>
  if (typeof r.distro_id !== 'string') return undefined
  const checks = Array.isArray(r.checks)
    ? r.checks.flatMap((c): AgentLinuxCheck[] => {
        if (!c || typeof c !== 'object') return []
        const x = c as Record<string, unknown>
        if (typeof x.id !== 'string' || !CHECK_IDS.has(x.id)) return []
        if (typeof x.status !== 'string' || !CHECK_STATUSES.has(x.status)) return []
        const detail = optStr(x.detail)
        return [{
          id: x.id as AgentLinuxCheckId,
          status: x.status as AgentLinuxCheckStatus,
          required: x.required === true,
          ...(detail ? { detail } : {}),
        }]
      })
    : []
  const opt = (k: string) => (optStr(r[k]) ? { [k]: r[k] as string } : {})
  return {
    distro_id: r.distro_id,
    ...opt('distro_version'),
    ...opt('distro_name'),
    ...opt('distro_like'),
    ...opt('glibc_version'),
    package_manager: (typeof r.package_manager === 'string' && PACKAGE_MANAGERS.has(r.package_manager)
      ? r.package_manager : 'unknown') as AgentLinuxStatus['package_manager'],
    setup_supported: r.setup_supported === true,
    setup_state: r.setup_state === 'ok' ? 'ok' : 'incomplete',
    checks,
  }
}

/** `GET /health`. `null` si el agente no responde. */
export async function getHealth(port: number, timeoutMs = TIMEOUT_MS): Promise<AgentHealth | null> {
  const res = await request(port, 'GET', '/health', undefined, timeoutMs)
  if (res.kind !== 'response' || res.status !== 200) return null
  const raw = res.body as Record<string, unknown> | null
  if (!raw || typeof raw !== 'object') return null
  const os = raw.os === 'windows' || raw.os === 'macos' || raw.os === 'linux' ? raw.os : undefined
  const linux = toLinuxStatus(raw.linux)
  return {
    ...(typeof raw.version === 'string' ? { version: raw.version } : {}),
    ...(os ? { os } : {}),
    ...(linux ? { linux } : {}),
  }
}
