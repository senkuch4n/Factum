/**
 * "Preparar esta PC" (SDD tatana-linux D-T10, §7, §10.B, §10.F).
 *
 * El script `packaging/linux/preparar-pc.sh` viaja EMBEBIDO en el bundle de
 * main (`?raw`) y se manda por stdin a `bash -s` (simular) o a
 * `pkexec /bin/bash -s` (ejecutar): no queda un archivo temporal que otro
 * proceso pueda cambiar entre la verificación y la ejecución, y root no puede
 * leer el montaje FUSE del usuario. Una sola contraseña (polkit).
 *
 * El protocolo de líneas y el mapeo de códigos son puros (`prepare-pc-protocol.ts`).
 */
import { app } from 'electron'
import { spawn } from 'child_process'
import { createHash } from 'crypto'
import { accessSync, appendFileSync, constants, existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from 'fs'
import { homedir, userInfo } from 'os'
import { delimiter, dirname, join } from 'path'
import preparePcScript from '../../../../packaging/linux/preparar-pc.sh?raw'
import {
  buildPreparacion, fallbackCommandText, finalResultado, mergePaso, parsePreparacion, parsePrepareLine,
  type PasoPreparacion, type PlanPreparacion, type PrepareOutcome, type PreparacionLinux, type ResultadoLinea,
} from './prepare-pc-protocol'

/** SHA-256 (hex) del script embebido: trazabilidad de qué script corrió en esta PC. */
export const scriptSha256: string = createHash('sha256').update(preparePcScript, 'utf8').digest('hex')

const SIMULATE_TIMEOUT_MS = 30_000
/** `apt-get` puede tardar (red lenta, lock de dpkg de 120 s): tope generoso. */
const EXECUTE_TIMEOUT_MS = 20 * 60_000

function configDir(): string {
  return app.getPath('userData') // ${XDG_CONFIG_HOME:-~/.config}/Tatana (D-T5)
}

export function preparacionPath(): string {
  return join(configDir(), 'preparacion-linux.json')
}

export function logPath(): string {
  return join(configDir(), 'logs', 'preparacion-linux.log')
}

function fallbackScriptPath(): string {
  return join(configDir(), 'preparar-pc.sh')
}

function login(): string {
  try { return userInfo().username } catch { return process.env['USER'] ?? '' }
}

/** `pkexec` en el PATH (o en `/usr/bin`), o null. */
function findPkexec(): string | null {
  const dirs = [...(process.env['PATH'] ?? '').split(delimiter), '/usr/bin', '/bin']
  for (const d of dirs) {
    if (!d) continue
    const p = join(d, 'pkexec')
    try { accessSync(p, constants.X_OK); return p } catch { /* sigue */ }
  }
  return null
}

function appendLog(text: string): void {
  try {
    mkdirSync(dirname(logPath()), { recursive: true })
    appendFileSync(logPath(), text.endsWith('\n') ? text : `${text}\n`, 'utf-8')
  } catch (err) {
    console.error('[Tatana] No se pudo escribir el log de preparación:', err)
  }
}

export function readPreparacion(): PreparacionLinux | null {
  try {
    const p = preparacionPath()
    return existsSync(p) ? parsePreparacion(readFileSync(p, 'utf-8')) : null
  } catch {
    return null
  }
}

function writePreparacion(rec: PreparacionLinux): void {
  try {
    mkdirSync(configDir(), { recursive: true })
    const tmp = `${preparacionPath()}.tmp`
    writeFileSync(tmp, JSON.stringify(rec, null, 2), 'utf-8')
    renameSync(tmp, preparacionPath())
  } catch (err) {
    console.error('[Tatana] No se pudo guardar preparacion-linux.json:', err)
  }
}

interface RunResult {
  code: number | null
  output: string
  spawnError?: NodeJS.ErrnoException
}

/** Corre `cmd args` con el script por stdin; `onLine` recibe cada línea de stdout/stderr. */
function runWithScript(cmd: string, args: string[], timeoutMs: number, onLine?: (line: string) => void): Promise<RunResult> {
  return new Promise((resolve) => {
    let output = ''
    let settled = false
    const done = (r: RunResult) => { if (!settled) { settled = true; clearTimeout(timer); resolve(r) } }
    const child = spawn(cmd, args, {
      stdio: ['pipe', 'pipe', 'pipe'],
      env: { ...process.env, LC_ALL: 'C' },
    })
    const timer = setTimeout(() => {
      try { child.kill('SIGTERM') } catch { /* ya salió */ }
      output += `\n[Tatana] Se cortó por tiempo (${Math.round(timeoutMs / 1000)} s).\n`
    }, timeoutMs)
    const buffers = { out: '', err: '' }
    const feed = (key: 'out' | 'err', chunk: Buffer) => {
      const s = chunk.toString('utf8')
      output += s
      buffers[key] += s
      const lines = buffers[key].split('\n')
      buffers[key] = lines.pop() ?? ''
      for (const l of lines) onLine?.(l)
    }
    child.stdout.on('data', (c: Buffer) => feed('out', c))
    child.stderr.on('data', (c: Buffer) => feed('err', c))
    child.on('error', (err: NodeJS.ErrnoException) => done({ code: null, output, spawnError: err }))
    child.on('close', (code) => {
      if (buffers.out) onLine?.(buffers.out)
      if (buffers.err) onLine?.(buffers.err)
      done({ code, output })
    })
    child.stdin.on('error', () => { /* el proceso salió antes de leer todo (p. ej. diálogo cancelado) */ })
    child.stdin.end(preparePcScript, 'utf8')
  })
}

/** `bash -s -- --simular` (sin contraseña): el plan de la distro, o null si no se pudo leer. */
export async function simulate(): Promise<PlanPreparacion | null> {
  let plan: PlanPreparacion | null = null
  const r = await runWithScript('bash', ['-s', '--', '--simular'], SIMULATE_TIMEOUT_MS, (line) => {
    const parsed = parsePrepareLine(line)
    if (parsed.kind === 'plan') plan = parsed.data
  })
  if (!plan) appendLog(`[${new Date().toISOString()}] --simular sin TATANA_PLAN (código ${r.code ?? '?'}):\n${r.output}`)
  return plan
}

/**
 * Fallback sin polkit (D-T10 b): deja el script en `~/.config/Tatana/preparar-pc.sh`
 * (0644) y devuelve el comando `sudo bash … --ejecutar --usuario <login>`.
 */
export function fallbackCommand(): string {
  const path = fallbackScriptPath()
  mkdirSync(dirname(path), { recursive: true })
  writeFileSync(path, preparePcScript, { encoding: 'utf-8', mode: 0o644 })
  return fallbackCommandText(path, process.env['HOME'] || homedir(), login())
}

let running: Promise<PrepareOutcome> | null = null

/**
 * Corre la preparación con una sola contraseña. Antes simula: si la distro no
 * está soportada, no pide contraseña ni ejecuta nada (Gherkin "Distro sin
 * soporte"). `onStep` recibe cada `TATANA_PASO`.
 */
export function execute(onStep: (paso: PasoPreparacion) => void): Promise<PrepareOutcome> {
  if (running) return running
  running = doExecute(onStep).finally(() => { running = null })
  return running
}

async function doExecute(onStep: (paso: PasoPreparacion) => void): Promise<PrepareOutcome> {
  const version = app.getVersion()
  const user = login()
  const startedAt = new Date()
  const plan = await simulate()

  const finish = (o: Omit<PrepareOutcome, 'scriptSha256'>, command: string, output: string): PrepareOutcome => {
    const outcome: PrepareOutcome = { ...o, scriptSha256 }
    appendLog([
      '────────────────────────────────────────────────────────────',
      `Fecha: ${startedAt.toISOString()}`,
      `Tatana: v${version}`,
      `Comando: ${command}`,
      `SHA-256 del script: ${scriptSha256}`,
      `Distro: ${plan ? `${plan.distro_name ?? plan.distro_id} (gestor ${plan.gestor})` : 'desconocida'}`,
      `Resultado: ${outcome.resultado}`,
      'Salida:',
      output.trimEnd() || '(sin salida)',
      '',
    ].join('\n'))
    writePreparacion(buildPreparacion({
      fecha: startedAt,
      tatanaVersion: version,
      plan,
      resultado: outcome.resultado,
      requiereReiniciarSesion: outcome.requiereReiniciarSesion,
      scriptSha256,
      pasos: outcome.pasos,
    }))
    return outcome
  }

  if (plan && !plan.soportada) {
    return finish({ resultado: 'sin_soporte', requiereReiniciarSesion: false, pasos: [], plan },
      'bash -s -- --simular', '(distro sin soporte: no se ejecutó nada)')
  }

  const pkexec = findPkexec()
  const args = ['/bin/bash', '-s', '--', '--ejecutar', '--usuario', user, '--version', version]
  const command = `pkexec ${args.join(' ')}`
  if (!pkexec) {
    const fb = fallbackCommand()
    return finish({ resultado: 'sin_polkit', requiereReiniciarSesion: false, pasos: [], fallbackCommand: fb, ...(plan ? { plan } : {}) },
      command, '(no existe pkexec)')
  }

  let pasos: PasoPreparacion[] = []
  let resultado: ResultadoLinea | null = null
  const r = await runWithScript(pkexec, args, EXECUTE_TIMEOUT_MS, (line) => {
    const parsed = parsePrepareLine(line)
    if (parsed.kind === 'paso') {
      pasos = mergePaso(pasos, parsed.data)
      onStep(parsed.data)
    } else if (parsed.kind === 'resultado') {
      resultado = parsed.data
    }
  })

  const final = finalResultado(r.code, resultado, { pkexecMissing: r.spawnError?.code === 'ENOENT' })
  const res = resultado as ResultadoLinea | null
  const finalPasos = res && res.pasos.length ? res.pasos.reduce(mergePaso, pasos) : pasos
  const outcome: Omit<PrepareOutcome, 'scriptSha256'> = {
    resultado: final,
    requiereReiniciarSesion: res?.requiere_reiniciar_sesion === true,
    pasos: finalPasos,
    ...(plan ? { plan } : {}),
    ...(final === 'sin_polkit' ? { fallbackCommand: fallbackCommand() } : {}),
  }
  const output = r.spawnError ? `${r.output}\n[Tatana] ${r.spawnError.message}` : r.output
  return finish(outcome, command, `${output}\n[código de salida: ${r.code ?? 'sin código'}]`)
}
