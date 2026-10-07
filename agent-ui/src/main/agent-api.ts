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

/** `kind` ∈ recording_android | recording_ios | airplay_session | video_postprocess | request. */
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

export interface AgentStateResponse {
  version: string
  mode: 'installed' | 'portable'
  busy: boolean
  maintenance: boolean
  operations: AgentOperation[]
  local_config: AgentLocalConfig
}

export type { EnterMaintenanceResult } from './updater/maintenance-outcome'

const TIMEOUT_MS = 3000

/**
 * Request HTTP al agente. Distingue la respuesta de la falla de transporte
 * (`refused` / `timeout` / `reset` / `other`): solo `refused` prueba que nadie
 * escucha el puerto. Un timeout NO significa "agente caído" (D5).
 */
function request(port: number, method: string, path: string, body?: unknown): Promise<MaintenanceRaw> {
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
        timeout: TIMEOUT_MS,
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
export async function getAgentState(port: number): Promise<AgentStateResponse | null> {
  const res = await request(port, 'GET', '/agent/state')
  if (res.kind !== 'response' || res.status !== 200) return null
  const raw = res.body as Partial<AgentStateResponse> | null
  if (!raw || typeof raw !== 'object' || typeof raw.version !== 'string') return null
  return {
    version: raw.version,
    mode: raw.mode === 'installed' ? 'installed' : 'portable',
    busy: raw.busy === true,
    maintenance: raw.maintenance === true,
    operations: Array.isArray(raw.operations) ? raw.operations.filter((o) => o && typeof o.kind === 'string') : [],
    local_config: {
      path: String(raw.local_config?.path ?? ''),
      exists: raw.local_config?.exists === true,
      loaded: raw.local_config?.loaded === true,
      overrides_allowed_origins: raw.local_config?.overrides_allowed_origins === true,
      ...(typeof raw.local_config?.error === 'string' ? { error: raw.local_config.error } : {}),
    },
  }
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
