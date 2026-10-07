/**
 * Logger de archivo del actualizador: `<userData>/logs/tatana-updater.log`
 * (rota a 1 MB y deja 1 anterior). También es el `autoUpdater.logger` y cada
 * línea va al log de la ventana (sink que conecta index.ts).
 */
import { app } from 'electron'
import { appendFileSync, existsSync, mkdirSync, renameSync, rmSync, statSync } from 'fs'
import { join } from 'path'

const MAX_BYTES = 1024 * 1024

type Level = 'info' | 'warn' | 'error' | 'debug'
let sink: ((line: string) => void) | null = null

export function setUpdaterLogSink(fn: (line: string) => void): void {
  sink = fn
}

function logPath(): string {
  return join(app.getPath('userData'), 'logs', 'tatana-updater.log')
}

function format(arg: unknown): string {
  if (arg instanceof Error) return arg.stack ?? arg.message
  if (typeof arg === 'string') return arg
  try { return JSON.stringify(arg) } catch { return String(arg) }
}

function write(level: Level, args: unknown[]): void {
  const msg = args.map(format).join(' ')
  const line = `${new Date().toISOString()} [${level}] ${msg}`
  try {
    const file = logPath()
    mkdirSync(join(file, '..'), { recursive: true })
    if (existsSync(file) && statSync(file).size > MAX_BYTES) {
      const old = `${file}.1`
      rmSync(old, { force: true })
      renameSync(file, old)
    }
    appendFileSync(file, line + '\n', 'utf-8')
  } catch { /* el log nunca rompe la app */ }
  // A la ventana solo info/warn/error (el debug de electron-updater es ruidoso).
  if (level !== 'debug' && sink) sink(`[Actualizador] ${msg}`)
}

/** Compatible con `autoUpdater.logger` (Logger de electron-updater). */
export const updaterLog = {
  info: (...a: unknown[]) => write('info', a),
  warn: (...a: unknown[]) => write('warn', a),
  error: (...a: unknown[]) => write('error', a),
  debug: (...a: unknown[]) => write('debug', a),
}
