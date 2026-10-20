/**
 * Protocolo entre agent-ui y `packaging/linux/preparar-pc.sh` (SDD tatana-linux
 * §7, §10.B y §10.F). PURO: sin Electron, sin disco, sin procesos (lo testea
 * `prepare-pc-protocol.test.ts`). La ejecución vive en `prepare-pc.ts`.
 *
 * Líneas: `TATANA_PLAN <json>`, `TATANA_PASO <json>`, `TATANA_RESULTADO <json>`
 * (prefijo + un espacio + JSON de una línea). El resto es log.
 */

export type PasoId = 'paquetes' | 'reglas_usb' | 'airplay' | 'camara_microfono' | 'sandbox'
export type PasoEstado = 'en_curso' | 'ok' | 'error' | 'omitido'

/** Resultados del script (§7.3) + los que agrega agent-ui (§10.B). */
export type ResultadoScript = 'ok' | 'parcial' | 'sin_soporte' | 'sin_internet'
export type ResultadoPreparacion = ResultadoScript | 'cancelado' | 'sin_polkit' | 'error'

/** Orden de los pasos en la UI (§7.2). */
export const PASO_IDS: readonly PasoId[] = ['paquetes', 'reglas_usb', 'airplay', 'camara_microfono', 'sandbox']

export interface PasoPreparacion {
  id: PasoId
  estado: PasoEstado
  detalle?: string
}

/** `TATANA_PLAN` de `--simular` (§7.2). */
export interface PlanPreparacion {
  schema: 1
  soportada: boolean
  distro_id: string
  distro_version?: string
  distro_name?: string
  gestor: string
  paquetes: string[]
  uxplay?: { candidata?: string | null; instalar: boolean }
  archivos: string[]
  servicios: string[]
  firewall?: string
  grupos: string[]
  comandos: string[]
}

/** `TATANA_RESULTADO` (§7.3). */
export interface ResultadoLinea {
  schema: 1
  resultado: ResultadoScript
  requiere_reiniciar_sesion: boolean
  pasos: PasoPreparacion[]
}

export type PrepareLine =
  | { kind: 'plan'; data: PlanPreparacion }
  | { kind: 'paso'; data: PasoPreparacion }
  | { kind: 'resultado'; data: ResultadoLinea }
  | { kind: 'log'; data: string }

/** `~/.config/Tatana/preparacion-linux.json` (§10.F). */
export interface PreparacionLinux {
  schema: 1
  fecha: string
  tatana_version: string
  distro: string
  gestor: string
  resultado: ResultadoPreparacion
  requiere_reiniciar_sesion: boolean
  script_sha256: string
  pasos: PasoPreparacion[]
}

/** Respuesta de `tatana:prepare-pc` (§10.G). */
export interface PrepareOutcome {
  resultado: ResultadoPreparacion
  requiereReiniciarSesion: boolean
  pasos: PasoPreparacion[]
  plan?: PlanPreparacion
  fallbackCommand?: string
  scriptSha256: string
}

const PASO_ID_SET: ReadonlySet<string> = new Set(PASO_IDS)
const ESTADOS: ReadonlySet<string> = new Set(['en_curso', 'ok', 'error', 'omitido'])
const RESULTADOS_SCRIPT: ReadonlySet<string> = new Set(['ok', 'parcial', 'sin_soporte', 'sin_internet'])

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === 'object' && v !== null && !Array.isArray(v)
}

function strings(v: unknown): string[] {
  return Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string') : []
}

function optString(v: unknown): string | undefined {
  return typeof v === 'string' && v ? v : undefined
}

export function toPaso(raw: unknown): PasoPreparacion | null {
  if (!isRecord(raw) || typeof raw.id !== 'string' || !PASO_ID_SET.has(raw.id)) return null
  if (typeof raw.estado !== 'string' || !ESTADOS.has(raw.estado)) return null
  const detalle = optString(raw.detalle)
  return { id: raw.id as PasoId, estado: raw.estado as PasoEstado, ...(detalle ? { detalle } : {}) }
}

function toPlan(raw: unknown): PlanPreparacion | null {
  if (!isRecord(raw) || typeof raw.soportada !== 'boolean') return null
  const ux = isRecord(raw.uxplay)
    ? { candidata: typeof raw.uxplay.candidata === 'string' ? raw.uxplay.candidata : null, instalar: raw.uxplay.instalar === true }
    : undefined
  return {
    schema: 1,
    soportada: raw.soportada,
    distro_id: typeof raw.distro_id === 'string' ? raw.distro_id : 'desconocida',
    ...(optString(raw.distro_version) ? { distro_version: raw.distro_version as string } : {}),
    ...(optString(raw.distro_name) ? { distro_name: raw.distro_name as string } : {}),
    gestor: typeof raw.gestor === 'string' ? raw.gestor : 'unknown',
    paquetes: strings(raw.paquetes),
    ...(ux ? { uxplay: ux } : {}),
    archivos: strings(raw.archivos),
    servicios: strings(raw.servicios),
    ...(optString(raw.firewall) ? { firewall: raw.firewall as string } : {}),
    grupos: strings(raw.grupos),
    comandos: strings(raw.comandos),
  }
}

function toResultado(raw: unknown): ResultadoLinea | null {
  if (!isRecord(raw) || typeof raw.resultado !== 'string' || !RESULTADOS_SCRIPT.has(raw.resultado)) return null
  const pasos = Array.isArray(raw.pasos) ? raw.pasos.map(toPaso).filter((p): p is PasoPreparacion => p !== null) : []
  return {
    schema: 1,
    resultado: raw.resultado as ResultadoScript,
    requiere_reiniciar_sesion: raw.requiere_reiniciar_sesion === true,
    pasos,
  }
}

const PREFIXES = [
  ['TATANA_PLAN ', 'plan'],
  ['TATANA_PASO ', 'paso'],
  ['TATANA_RESULTADO ', 'resultado'],
] as const

/** Clasifica una línea de salida del script. JSON roto o inválido → `log` (no se pierde). */
export function parsePrepareLine(line: string): PrepareLine {
  const text = line.replace(/\r$/, '')
  for (const [prefix, kind] of PREFIXES) {
    if (!text.startsWith(prefix)) continue
    let raw: unknown
    try { raw = JSON.parse(text.slice(prefix.length)) } catch { return { kind: 'log', data: text } }
    if (kind === 'plan') { const d = toPlan(raw); return d ? { kind, data: d } : { kind: 'log', data: text } }
    if (kind === 'paso') { const d = toPaso(raw); return d ? { kind, data: d } : { kind: 'log', data: text } }
    const d = toResultado(raw)
    return d ? { kind, data: d } : { kind: 'log', data: text }
  }
  return { kind: 'log', data: text }
}

/**
 * Código de salida → resultado (§8.2, §10.B): 0 ok · 1 parcial · 3 sin soporte ·
 * 4 sin internet · 126 cancelado (pkexec: cerraron el diálogo) · 127 o sin
 * `pkexec` → sin polkit · cualquier otro (2 argumentos, 5 sin root, señal) → error.
 */
export function resultadoFromExit(code: number | null, opts: { pkexecMissing?: boolean } = {}): ResultadoPreparacion {
  if (opts.pkexecMissing) return 'sin_polkit'
  switch (code) {
    case 0: return 'ok'
    case 1: return 'parcial'
    case 3: return 'sin_soporte'
    case 4: return 'sin_internet'
    case 126: return 'cancelado'
    case 127: return 'sin_polkit'
    default: return 'error'
  }
}

/**
 * Resultado final: con 126/127 manda el código de pkexec (el script no corrió);
 * si no, el `TATANA_RESULTADO` del script si llegó; si no, el código.
 */
export function finalResultado(
  code: number | null, resultado: ResultadoLinea | null, opts: { pkexecMissing?: boolean } = {},
): ResultadoPreparacion {
  const byCode = resultadoFromExit(code, opts)
  if (byCode === 'cancelado' || byCode === 'sin_polkit') return byCode
  return resultado?.resultado ?? byCode
}

/** Actualiza la lista de pasos con un `TATANA_PASO`, en el orden de `PASO_IDS`. */
export function mergePaso(pasos: ReadonlyArray<PasoPreparacion>, paso: PasoPreparacion): PasoPreparacion[] {
  const byId = new Map(pasos.map((p) => [p.id, p] as const))
  byId.set(paso.id, paso)
  return PASO_IDS.filter((id) => byId.has(id)).map((id) => byId.get(id)!)
}

/** Registro de `preparacion-linux.json` (§10.F). */
export function buildPreparacion(input: {
  fecha: Date
  tatanaVersion: string
  plan: PlanPreparacion | null
  resultado: ResultadoPreparacion
  requiereReiniciarSesion: boolean
  scriptSha256: string
  pasos: ReadonlyArray<PasoPreparacion>
}): PreparacionLinux {
  const distro = input.plan
    ? [input.plan.distro_id, input.plan.distro_version].filter(Boolean).join(' ')
    : 'desconocida'
  return {
    schema: 1,
    fecha: input.fecha.toISOString().replace(/\.\d{3}Z$/, 'Z'),
    tatana_version: input.tatanaVersion,
    distro,
    gestor: input.plan?.gestor ?? 'unknown',
    resultado: input.resultado,
    requiere_reiniciar_sesion: input.requiereReiniciarSesion,
    script_sha256: input.scriptSha256,
    pasos: [...input.pasos],
  }
}

/** Lee `preparacion-linux.json`; `null` si no es válido. */
export function parsePreparacion(text: string): PreparacionLinux | null {
  let raw: unknown
  try { raw = JSON.parse(text.replace(/^﻿/, '')) } catch { return null }
  if (!isRecord(raw) || raw.schema !== 1 || typeof raw.resultado !== 'string') return null
  return {
    schema: 1,
    fecha: String(raw.fecha ?? ''),
    tatana_version: String(raw.tatana_version ?? ''),
    distro: String(raw.distro ?? ''),
    gestor: String(raw.gestor ?? ''),
    resultado: raw.resultado as ResultadoPreparacion,
    requiere_reiniciar_sesion: raw.requiere_reiniciar_sesion === true,
    script_sha256: String(raw.script_sha256 ?? ''),
    pasos: Array.isArray(raw.pasos) ? raw.pasos.map(toPaso).filter((p): p is PasoPreparacion => p !== null) : [],
  }
}

/** Argumento seguro para mostrar en un comando de shell (comillas simples si hace falta). */
export function shellQuote(arg: string): string {
  return /^[A-Za-z0-9_@%+=:,./~-]+$/.test(arg) ? arg : `'${arg.replace(/'/g, `'\\''`)}'`
}

/**
 * Comando del fallback sin polkit (D-T10, §10.B):
 * `sudo bash ~/.config/Tatana/preparar-pc.sh --ejecutar --usuario <login>`.
 * Si el archivo está bajo `home`, se muestra con `~/` (sin comillas, para que
 * el shell lo expanda) salvo que tenga caracteres especiales.
 */
export function fallbackCommandText(scriptPath: string, home: string, login: string): string {
  const rel = home && scriptPath.startsWith(`${home.replace(/\/+$/, '')}/`)
    ? scriptPath.slice(home.replace(/\/+$/, '').length + 1)
    : null
  const shown = rel !== null && /^[A-Za-z0-9_./-]+$/.test(rel) ? `~/${rel}` : shellQuote(scriptPath)
  return `sudo bash ${shown} --ejecutar --usuario ${shellQuote(login)}`
}
