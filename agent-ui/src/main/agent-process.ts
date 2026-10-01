import { spawn, ChildProcess } from 'child_process'
import { existsSync } from 'fs'
import { join, dirname, delimiter } from 'path'
import { app } from 'electron'
import { EventEmitter } from 'events'

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

    // El directorio de datos va a userData (siempre writable, independiente del cwd de Electron)
    const dataDir = join(app.getPath('userData'), 'agent-data')
    // cwd = directorio del binario para que appsettings.json sea encontrado
    const cwd = dirname(bin)

    this.process = spawn(bin, ['--port', String(this.port), '--data', dataDir], {
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
