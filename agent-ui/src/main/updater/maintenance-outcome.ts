/**
 * Clasificación de la respuesta de `POST /agent/maintenance` (SDD §5.4, D5).
 * PURO (sin Electron ni red): lo testea `maintenance-outcome.test.ts`.
 *
 * Regla de seguridad de la evidencia: solo se instala sin mantenimiento si es
 * SEGURO que no hay agente ejecutando nada:
 *   - el proceso del agente ya no corre, o
 *   - la conexión se rechazó (`ECONNREFUSED`: nadie escucha el puerto).
 * Un timeout, un reset o cualquier otro error con el agente vivo NO prueba que
 * esté libre (puede estar armando un ZIP o en ffmpeg y tardar en contestar):
 * eso es `error` y no se instala, igual que un 5xx.
 */

export interface MaintenanceOperation {
  kind: string
  since: string
  detail?: string
}

/** Falla de transporte de la request HTTP al agente. */
export type TransportFailure = 'refused' | 'timeout' | 'reset' | 'other'

export type MaintenanceRaw =
  | { kind: 'response'; status: number; body: unknown }
  | { kind: 'failure'; failure: TransportFailure }

export type EnterMaintenanceResult =
  | { kind: 'ok'; expiresAt: string }
  | { kind: 'busy'; operations: MaintenanceOperation[] }
  /** El agente no corre o nadie escucha el puerto: no puede haber operaciones en curso. */
  | { kind: 'unreachable' }
  /** Respuesta inesperada o agente vivo que no contestó: no se instala. */
  | { kind: 'error'; status: number; failure?: TransportFailure }

/** `ECONNREFUSED` → refused; `ECONNRESET`/`EPIPE` → reset; timeout propio → timeout; el resto → other. */
export function classifyTransportError(err: unknown): TransportFailure {
  const code = typeof err === 'object' && err !== null ? (err as { code?: unknown }).code : undefined
  if (code === 'ECONNREFUSED') return 'refused'
  if (code === 'ECONNRESET' || code === 'EPIPE') return 'reset'
  if (code === 'ETIMEDOUT' || code === 'ESOCKETTIMEDOUT') return 'timeout'
  return 'other'
}

function operationsOf(body: unknown): MaintenanceOperation[] {
  const ops = typeof body === 'object' && body !== null ? (body as { operations?: unknown }).operations : undefined
  return Array.isArray(ops)
    ? ops.filter((o): o is MaintenanceOperation => !!o && typeof (o as MaintenanceOperation).kind === 'string')
    : []
}

/**
 * @param raw          respuesta HTTP o falla de transporte.
 * @param agentRunning `agentProcess.isRunning()` al momento de la llamada.
 */
export function classifyMaintenance(raw: MaintenanceRaw, agentRunning: boolean): EnterMaintenanceResult {
  if (raw.kind === 'failure') {
    if (!agentRunning || raw.failure === 'refused') return { kind: 'unreachable' }
    return { kind: 'error', status: 0, failure: raw.failure }
  }
  if (raw.status === 200) {
    const exp = typeof raw.body === 'object' && raw.body !== null ? (raw.body as { expires_at?: unknown }).expires_at : undefined
    return { kind: 'ok', expiresAt: typeof exp === 'string' ? exp : '' }
  }
  if (raw.status === 409) {
    const ops = operationsOf(raw.body)
    return { kind: 'busy', operations: ops.length ? ops : [{ kind: 'request', since: '' }] }
  }
  return { kind: 'error', status: raw.status }
}
