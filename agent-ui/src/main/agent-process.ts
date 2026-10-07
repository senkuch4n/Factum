import { spawn, ChildProcess } from 'child_process'
import { existsSync } from 'fs'
import { join, dirname, delimiter } from 'path'
import { app } from 'electron'
import { EventEmitter } from 'events'
import { readConfig } from './config'

/**
 * Carpeta de datos del agente (D-T3). Empaquetado en Windows es la MISMA que
 * usaba Tatana portátil (`%LOCALAPPDATA%\Tatana\data`), así la evidencia no se
 * migra ni se copia; `%APPDATA%` es roaming y no sirve para evidencia. En
 * macOS/Linux (dev) sigue en `<userData>/agent-data`. `tatana.json.agentDataDir`
 * la pisa si no está vacío.
 */
export function agentDataDir(): string {
  const override = readConfig().agentDataDir?.trim()
  if (override) return override
  if (app.isPackaged && process.platform === 'win32' && process.env['LOCALAPPDATA']) {
    return join(process.env['LOCALAPPDATA'], 'Tatana', 'data')
  }
  return join(app.getPath('userData'), 'agent-data')
}

/** `%APPDATA%\Tatana\appsettings.Local.json` (D9, D-T4): config local del agente instalado. */
export function localConfigPath(): string {
  return join(app.getPath('userData'), 'appsettings.Local.json')
}

export type AgentStatus = 'starting' | 'running' | 'stopped' | 'missing'

export class AgentProcessManager extends EventEmitter {
  private process: ChildProcess | null = null
  private restartTimer: NodeJS.Timeout | null = null
  private port = 8765
  private shouldRun = false

  setPort(port: number): void {
    this.port = port
  }

  start(): void {
    this.shouldRun = true
    this.spawn()
  }

  stop(): void {
    this.shouldRun = false
    if (this.restartTimer) { clearTimeout(this.restartTimer); this.restartTimer = null }
    if (this.process) {
      // Remover listeners antes de matar para evitar eventos post-destrucción
      this.process.stdout?.removeAllListeners()
      this.process.stderr?.removeAllListeners()
      this.process.removeAllListeners()
      this.process.kill()
      this.process = null
    }
    this.emit('status', 'stopped' satisfies AgentStatus)
  }

  /**
   * Detiene el agente y espera a que salga (D-T12, antes de instalar). En
   * Windows mata el árbol (`taskkill /T /F`: adb, scrcpy, python…). Resuelve en
   * `close` o al timeout.
   */
  stopAndWait(timeoutMs = 10000): Promise<void> {
    this.shouldRun = false
    if (this.restartTimer) { clearTimeout(this.restartTimer); this.restartTimer = null }
    const proc = this.process
    if (!proc || proc.exitCode !== null || proc.pid === undefined) {
      this.process = null
      this.emit('status', 'stopped' satisfies AgentStatus)
      return Promise.resolve()
    }
    return new Promise<void>((resolve) => {
      let finished = false
      const finish = () => {
        if (finished) return
        finished = true
        clearTimeout(timer)
        resolve()
      }
      const timer = setTimeout(finish, timeoutMs)
      proc.once('close', finish)
      if (process.platform === 'win32') {
        const killer = spawn('taskkill', ['/PID', String(proc.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' })
        killer.on('error', () => proc.kill())
      } else {
        proc.kill('SIGTERM')
      }
    })
  }

  /** Carpeta de `Factum.Agent(.exe)` (o null si no está), para D-T12. */
  binaryDir(): string | null {
    const bin = this.findBinary()
    return bin ? dirname(bin) : null
  }

  isRunning(): boolean {
    return this.process !== null && !this.process.killed
  }

  private findBinary(): string | null {
    const exe = process.platform === 'win32' ? 'Factum.Agent.exe' : 'Factum.Agent'
    const is = app.isPackaged

    const candidates = is
      ? [
          // ── PACKAGED: bundled via extraResources ───────────────────────────
          join(process.resourcesPath, 'agent', exe),
        ]
      : [
          // ── DEV: build output de dotnet build/publish ──────────────────────
          // Primero busca el publish self-contained dentro del proyecto agent-ui
          join(app.getAppPath(), 'resources', 'agent', exe),
          // Fallback: salida normal de dotnet build (net10.0)
          join(app.getAppPath(), '..', 'server', 'src', 'Factum.Agent', 'bin', 'Debug',   'net10.0', exe),
          join(app.getAppPath(), '..', 'server', 'src', 'Factum.Agent', 'bin', 'Release', 'net10.0', exe),
          // Fallback legacy (net9.0 por si se downgradera)
          join(app.getAppPath(), '..', 'server', 'src', 'Factum.Agent', 'bin', 'Debug',   'net9.0', exe),
          join(app.getAppPath(), '..', 'server', 'src', 'Factum.Agent', 'bin', 'Release', 'net9.0', exe),
        ]

    return candidates.find(existsSync) ?? null
  }

  private spawn(): void {
    if (!this.shouldRun) return

    const bin = this.findBinary()
    if (!bin) {
      this.emit('log', '[Tatana] No se encontró el binario Factum.Agent — compilalo primero.')
      this.emit('status', 'missing' satisfies AgentStatus)
      return
    }

    this.emit('log', `[Tatana] Iniciando agente en :${this.port}...`)
    this.emit('status', 'starting' satisfies AgentStatus)

    // Agregar rutas comunes de herramientas instaladas por el fiscal via Tatana (brew/winget/pip)
    const extraPaths = process.platform === 'win32'
      ? [
          'C:\\Windows\\System32',
          join(process.env['LOCALAPPDATA'] ?? '', 'Android', 'Sdk', 'platform-tools'),
          join(process.env['PROGRAMFILES'] ?? '', 'Android', 'platform-tools'),
        ]
      : [
          '/opt/homebrew/bin',   // macOS Apple Silicon (brew)
          '/usr/local/bin',      // macOS Intel (brew)
          '/usr/bin',
          '/bin',
        ]

    const env = {
      ...process.env,
      PATH: [...extraPaths, process.env.PATH ?? ''].join(delimiter),
    }

    const dataDir = agentDataDir()
    // cwd = directorio del binario para que appsettings.json sea encontrado
    const cwd = dirname(bin)

    // SDD §13.A: --port --data --local-config [--mode installed].
    // `--local-config` REEMPLAZA al appsettings.Local.json de al lado del exe
    // (D-T16). Empaquetado siempre se pasa (el instalador nunca trae uno al
    // lado). En dev solo si el de userData existe, para no tapar el
    // appsettings.Local.json que el dev tenga junto al agente compilado.
    const args = ['--port', String(this.port), '--data', dataDir]
    const localConfig = localConfigPath()
    if (app.isPackaged || existsSync(localConfig)) args.push('--local-config', localConfig)
    if (app.isPackaged) args.push('--mode', 'installed')

    this.process = spawn(bin, args, {
      stdio: ['ignore', 'pipe', 'pipe'],
      env,
      cwd,
    })

    this.process.stdout?.on('data', (d) => this.emit('log', String(d).trimEnd()))
    this.process.stderr?.on('data', (d) => this.emit('log', String(d).trimEnd()))

    this.process.on('spawn', () => this.emit('status', 'running' satisfies AgentStatus))

    this.process.on('close', (code) => {
      this.process = null
      this.emit('status', 'stopped' satisfies AgentStatus)
      this.emit('log', `[Tatana] Agente detenido (código ${code ?? '?'})`)
      if (this.shouldRun) {
        this.emit('log', '[Tatana] Reiniciando en 3s...')
        this.restartTimer = setTimeout(() => this.spawn(), 3000)
      }
    })

    this.process.on('error', (err) => {
      this.emit('log', `[Tatana] Error al iniciar agente: ${err.message}`)
      this.process = null
      this.emit('status', 'stopped' satisfies AgentStatus)
    })
  }
}

export const agentProcess = new AgentProcessManager()
