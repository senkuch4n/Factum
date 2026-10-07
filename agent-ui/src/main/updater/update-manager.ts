/**
 * Actualización automática de Tatana (SDD tatana-instalador-autoupdate §6.5).
 *
 *   para cada URL horneada (D8): GET tatana-update.json → verificar firma Ed25519
 *   → ¿versión mayor? → electron-updater (generic, misma URL) → updateInfo ==
 *   manifiesto firmado → downloadUpdate → re-verificar sha512 + tamaño → "ready".
 *
 * Instalar SOLO con "Reiniciar y actualizar", "Salir" o el próximo arranque, y
 * NUNCA con operaciones en curso (D5): `POST /agent/maintenance` es atómico y
 * devuelve 409 si hay algo corriendo. `autoInstallOnAppQuit = false`: nada se
 * instala en el apagado de Windows.
 */
import { app, net, Notification, powerMonitor } from 'electron'
import { autoUpdater, type UpdateDownloadedEvent, type UpdateInfo } from 'electron-updater'
import { EventEmitter } from 'events'
import { createReadStream, rmSync, statSync } from 'fs'
import { createHash } from 'crypto'
import { join } from 'path'
import { spawn } from 'child_process'
import { agentProcess } from '../agent-process'
import { enterMaintenance, type AgentOperation } from '../agent-api'
import { readDistConfig } from './dist-config'
import { allTrustedKeys } from './trusted-keys'
import { updaterLog } from './logger'
import {
  decide, matchesUpdateInfo, parseEnvelope, sanitizeUpdateUrls, verifyEnvelope,
  type TrustedKey, type UpdatePayload,
} from './manifest'
import { loadUpdateState, saveUpdateState, type UpdateReason, type UpdateState } from './state'

const FIRST_CHECK_DELAY_MS = 10_000
const CHECK_INTERVAL_MS = 6 * 60 * 60 * 1000
const MANIFEST_TIMEOUT_MS = 10_000
const MANIFEST_MAX_BYTES = 64 * 1024
const MAX_REDIRECTS = 5
const BOOT_BUDGET_MS = 8_000
const QUIT_WATCHDOG_MS = 15_000

/** Fases en las que no se arranca otro chequeo. */
const NO_CHECK_PHASES = new Set(['checking', 'downloading', 'ready', 'installing'])

export type InstallResult = { ok: boolean; reason?: 'busy' | 'not_ready' | 'error' }

type FetchResult = { ok: true; text: string } | { ok: false; kind: 'network' }

type UrlOutcome =
  | { kind: 'ready' }
  | { kind: 'up_to_date' }
  | { kind: 'network' }
  | { kind: 'rejected'; reason: UpdateReason }

function nowIso(): string {
  return new Date().toISOString()
}

function sameList(a: ReadonlyArray<string> | undefined, b: ReadonlyArray<string> | undefined): boolean {
  const x = a ?? []
  const y = b ?? []
  return x.length === y.length && x.every((v, i) => v === y[i])
}

function uniqueKinds(ops: ReadonlyArray<AgentOperation>): string[] {
  return [...new Set(ops.map((o) => o.kind))]
}

/**
 * GET del manifiesto con `net.request` de Electron (proxy del sistema, D-T9):
 * timeout de 10 s, tope de 64 KB y redirecciones solo a `https` (D-T8).
 */
function fetchManifest(url: string): Promise<FetchResult> {
  return new Promise((resolve) => {
    let settled = false
    let redirects = 0
    const done = (r: FetchResult) => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      resolve(r)
    }
    let req: Electron.ClientRequest
    try {
      req = net.request({ url, method: 'GET', redirect: 'manual', useSessionCookies: false, cache: 'no-cache' })
    } catch {
      resolve({ ok: false, kind: 'network' })
      return
    }
    const timer = setTimeout(() => { req.abort(); done({ ok: false, kind: 'network' }) }, MANIFEST_TIMEOUT_MS)
    req.on('redirect', (_status, _method, redirectUrl) => {
      redirects++
      if (redirects > MAX_REDIRECTS || !redirectUrl.toLowerCase().startsWith('https://')) {
        updaterLog.warn(`Redirección rechazada desde ${url} hacia ${redirectUrl}`)
        req.abort()
        done({ ok: false, kind: 'network' })
        return
      }
      req.followRedirect()
    })
    req.on('response', (res) => {
      if (res.statusCode >= 400) {
        updaterLog.warn(`${url} respondió HTTP ${res.statusCode}`)
        res.on('data', () => {})
        done({ ok: false, kind: 'network' })
        return
      }
      const chunks: Buffer[] = []
      let size = 0
      res.on('data', (c: Buffer) => {
        size += c.length
        if (size > MANIFEST_MAX_BYTES) {
          req.abort()
          done({ ok: true, text: '' }) // demasiado grande → manifiesto inválido
          return
        }
        chunks.push(c)
      })
      res.on('end', () => done({ ok: true, text: Buffer.concat(chunks).toString('utf8') }))
      res.on('error', () => done({ ok: false, kind: 'network' }))
    })
    req.on('error', (err) => {
      updaterLog.info(`Sin respuesta de ${url}: ${err.message}`)
      done({ ok: false, kind: 'network' })
    })
    req.end()
  })
}

function sha512Base64(file: string): Promise<string> {
  return new Promise((resolve, reject) => {
    const hash = createHash('sha512')
    createReadStream(file)
      .on('data', (c) => hash.update(c))
      .on('end', () => resolve(hash.digest('base64')))
      .on('error', reject)
  })
}

function runQuiet(cmd: string, args: string[], timeoutMs: number): Promise<void> {
  return new Promise((resolve) => {
    let child: ReturnType<typeof spawn>
    try {
      child = spawn(cmd, args, { windowsHide: true, stdio: 'ignore' })
    } catch {
      resolve()
      return
    }
    const timer = setTimeout(() => { try { child.kill() } catch { /* ya salió */ } resolve() }, timeoutMs)
    child.on('error', () => { clearTimeout(timer); resolve() })
    child.on('close', () => { clearTimeout(timer); resolve() })
  })
}

export class UpdateManager extends EventEmitter {
  private state: UpdateState
  private keys: TrustedKey[] = []
  private urls: string[] = []
  private checking: Promise<UpdateState> | null = null
  private interval: NodeJS.Timeout | null = null
  private firstTimer: NodeJS.Timeout | null = null
  private getPort: () => number = () => 8765
  private installing = false
  private initialized = false

  constructor() {
    super()
    this.state = { phase: 'idle', currentVersion: '' }
  }

  private statePath(): string {
    return join(app.getPath('userData'), 'update-state.json')
  }

  getState(): UpdateState {
    return { ...this.state }
  }

  /** Estado persistido de la corrida anterior (para decidir `--hidden` tras actualizar). */
  previousPhase(): UpdateState['phase'] | null {
    return loadUpdateState(this.statePath())?.phase ?? null
  }

  private set(patch: Partial<UpdateState>, opts: { clear?: Array<keyof UpdateState> } = {}): void {
    const next: UpdateState = { ...this.state, ...patch }
    for (const k of opts.clear ?? []) delete next[k]
    this.state = next
    saveUpdateState(this.statePath(), next)
    this.emit('state', this.getState())
  }

  /**
   * Configura electron-updater y resuelve el arranque (D-T11):
   *  - estado previo `installing` → la app se acaba de actualizar: aviso "se actualizó" y `up_to_date`;
   *  - estado previo `ready` → re-chequeo con presupuesto de 8 s ANTES de lanzar el agente y, si
   *    llega a `ready`, instala y relanza (sin agente no hay operaciones en curso).
   * Devuelve `true` si ya se está instalando (el caller NO debe arrancar el agente).
   */
  async init(opts: { getAgentPort: () => number }): Promise<boolean> {
    if (this.initialized) return false
    this.initialized = true
    this.getPort = opts.getAgentPort

    const currentVersion = app.getVersion()
    const prev = loadUpdateState(this.statePath())
    this.state = {
      phase: 'idle',
      currentVersion,
      ...(prev?.notifiedVersion ? { notifiedVersion: prev.notifiedVersion } : {}),
    }

    // `channel` se setea ANTES que allowDowngrade: su setter pone allowDowngrade = true.
    autoUpdater.channel = 'latest'
    autoUpdater.allowPrerelease = false
    autoUpdater.allowDowngrade = false
    autoUpdater.autoDownload = false
    autoUpdater.autoInstallOnAppQuit = false
    autoUpdater.autoRunAppAfterInstall = true
    autoUpdater.logger = updaterLog
    autoUpdater.on('error', (err) => updaterLog.error('electron-updater:', err))
    autoUpdater.on('download-progress', (p) => {
      if (this.state.phase === 'downloading') {
        const percent = Math.max(0, Math.min(100, Math.round(p.percent)))
        if (percent !== this.state.percent) this.set({ percent })
      }
    })

    this.keys = allTrustedKeys()
    const dist = readDistConfig()
    this.urls = sanitizeUpdateUrls(dist?.update_urls ?? [], process.env['TATANA_UPDATE_URLS'])

    const disabledReason: UpdateReason | null =
      !app.isPackaged ? 'no_empaquetado'
      : this.keys.length === 0 ? 'sin_claves'
      : this.urls.length === 0 ? 'sin_canal'
      : null
    if (disabledReason) {
      updaterLog.info(`Actualizaciones desactivadas (${disabledReason}).`)
      this.set({ phase: 'disabled', reason: disabledReason })
      return false
    }
    updaterLog.info(`Tatana v${currentVersion}. Canal: ${this.urls.join(', ')}`)

    if (prev?.phase === 'installing') {
      if (prev.availableVersion && prev.availableVersion === currentVersion) {
        this.notify(`Tatana se actualizó a v${currentVersion}.`)
        updaterLog.info(`Actualizado a v${currentVersion}.`)
      } else {
        updaterLog.warn(`La instalación de v${prev.availableVersion ?? '?'} no se completó; sigue v${currentVersion}.`)
      }
      this.set({ phase: 'up_to_date', checkedAt: nowIso() })
      return false
    }

    if (prev?.phase === 'ready') {
      updaterLog.info('Había una actualización lista: se re-verifica antes de arrancar el agente.')
      const result = await Promise.race([
        this.check().then((s) => s.phase),
        new Promise<'timeout'>((r) => setTimeout(() => r('timeout'), BOOT_BUDGET_MS)),
      ])
      if (result === 'ready') {
        const r = await this.installNow({ relaunch: true })
        if (r.ok) return true
      }
    }
    return false
  }

  /** Primer chequeo a los 10 s, después cada 6 h y al volver de suspensión. */
  schedule(): void {
    if (this.state.phase === 'disabled' || this.interval) return
    this.firstTimer = setTimeout(() => { void this.check() }, FIRST_CHECK_DELAY_MS)
    this.interval = setInterval(() => { void this.check() }, CHECK_INTERVAL_MS)
    powerMonitor.on('resume', () => { void this.check() })
  }

  dispose(): void {
    if (this.firstTimer) clearTimeout(this.firstTimer)
    if (this.interval) clearInterval(this.interval)
    this.firstTimer = null
    this.interval = null
  }

  /** "Buscar actualizaciones": no hace nada si está desactivado o ya hay algo en marcha. */
  check(): Promise<UpdateState> {
    if (this.checking) return this.checking
    if (this.state.phase === 'disabled' || NO_CHECK_PHASES.has(this.state.phase)) {
      return Promise.resolve(this.getState())
    }
    this.checking = this.runCheck()
      .catch((err) => {
        updaterLog.error('Chequeo de actualizaciones falló:', err)
        this.set({ phase: 'offline', checkedAt: nowIso() }, { clear: ['percent'] })
        return this.getState()
      })
      .finally(() => { this.checking = null })
    return this.checking
  }

  private async runCheck(): Promise<UpdateState> {
    this.set({ phase: 'checking' }, { clear: ['reason', 'percent', 'busyOperations'] })
    let firstRejection: UpdateReason | null = null
    for (const url of this.urls) {
      const outcome = await this.tryUrl(url)
      if (outcome.kind === 'ready') return this.getState()
      if (outcome.kind === 'up_to_date') {
        this.set({ phase: 'up_to_date', checkedAt: nowIso(), sourceUrl: url },
          { clear: ['availableVersion', 'percent', 'reason', 'verifiedAt'] })
        return this.getState()
      }
      if (outcome.kind === 'rejected') firstRejection ??= outcome.reason
    }
    if (firstRejection) {
      this.set({ phase: 'rejected', reason: firstRejection, checkedAt: nowIso() },
        { clear: ['percent', 'availableVersion'] })
    } else {
      this.set({ phase: 'offline', checkedAt: nowIso() }, { clear: ['percent', 'reason'] })
    }
    return this.getState()
  }

  private async tryUrl(url: string): Promise<UrlOutcome> {
    // 1. Manifiesto.
    const fetched = await fetchManifest(url + 'tatana-update.json')
    if (!fetched.ok) return { kind: 'network' }

    // 2. Firma. Si falla se prueba la URL siguiente: un canal adulterado no tapa a otro sano.
    const env = parseEnvelope(fetched.text)
    if (!env) {
      updaterLog.warn(`${url}: manifiesto inválido.`)
      return { kind: 'rejected', reason: 'manifiesto_invalido' }
    }
    const verified = verifyEnvelope(env, this.keys)
    if (!verified.ok) {
      updaterLog.warn(`${url}: verificación del manifiesto falló (${verified.reason}, key_id=${env.key_id}).`)
      return { kind: 'rejected', reason: verified.reason }
    }
    const payload = verified.payload

    // 3. ¿Versión mayor? (sin downgrade ni repetición, D-T7).
    const decision = decide(payload, this.state.currentVersion)
    if (decision !== 'update') {
      updaterLog.info(`${url}: v${payload.version} publicada, instalada v${this.state.currentVersion} (${decision}).`)
      return { kind: 'up_to_date' }
    }

    // 4. electron-updater contra la misma URL; su updateInfo tiene que ser el manifiesto firmado.
    let info: UpdateInfo | null = null
    try {
      autoUpdater.setFeedURL({ provider: 'generic', url, channel: 'latest' })
      autoUpdater.allowDowngrade = false
      const result = await autoUpdater.checkForUpdates()
      info = result?.updateInfo ?? null
    } catch (err) {
      updaterLog.warn(`${url}: electron-updater no pudo leer latest.yml:`, err)
      return { kind: 'network' }
    }
    const mismatch = matchesUpdateInfo(payload, info)
    if (mismatch) {
      updaterLog.warn(`${url}: latest.yml no coincide con el manifiesto firmado (v${payload.version}).`)
      return { kind: 'rejected', reason: mismatch }
    }

    // 5. Descarga (sha512 lo verifica electron-updater).
    this.set({ phase: 'downloading', availableVersion: payload.version, percent: 0, sourceUrl: url })
    let downloadedFile: string | null = null
    const onDownloaded = (e: UpdateDownloadedEvent) => { downloadedFile = e.downloadedFile }
    autoUpdater.once('update-downloaded', onDownloaded)
    try {
      const paths = await autoUpdater.downloadUpdate()
      downloadedFile ??= paths?.find((p) => p.toLowerCase().endsWith('.exe')) ?? paths?.[0] ?? null
    } catch (err) {
      updaterLog.warn(`${url}: falló la descarga de v${payload.version}:`, err)
      this.set({ phase: 'checking' }, { clear: ['percent'] })
      return { kind: 'network' }
    } finally {
      autoUpdater.removeListener('update-downloaded', onDownloaded)
    }

    // 6. Re-verificar sha512 y tamaño del archivo descargado.
    const ok = downloadedFile ? await this.verifyDownloaded(downloadedFile, payload) : false
    if (!ok) {
      updaterLog.error(`${url}: el instalador descargado no coincide con el manifiesto firmado; se descarta.`)
      if (downloadedFile) rmSync(downloadedFile, { force: true })
      this.set({ phase: 'checking' }, { clear: ['percent'] })
      return { kind: 'rejected', reason: 'hash_no_coincide' }
    }

    // 7. Lista.
    this.set({ phase: 'ready', availableVersion: payload.version, sourceUrl: url, verifiedAt: nowIso(), checkedAt: nowIso() },
      { clear: ['percent', 'reason'] })
    updaterLog.info(`v${payload.version} descargada y verificada: se instala al reiniciar Tatana.`)
    if (this.state.notifiedVersion !== payload.version) {
      this.notify(`Hay una versión nueva de Tatana (v${payload.version}). Se instala al reiniciar Tatana o al iniciar sesión.`)
      this.set({ notifiedVersion: payload.version })
    }
    return { kind: 'ready' }
  }

  private async verifyDownloaded(file: string, payload: UpdatePayload): Promise<boolean> {
    try {
      if (statSync(file).size !== payload.size) return false
      return (await sha512Base64(file)) === payload.sha512
    } catch {
      return false
    }
  }

  private notify(body: string): void {
    try {
      if (Notification.isSupported()) new Notification({ title: 'Tatana', body }).show()
    } catch { /* sin notificaciones del sistema: queda la línea de estado */ }
  }

  /** Operaciones en curso informadas por `/agent/state` (refresco cada 15 s). */
  setBusyOperations(kinds: string[]): void {
    if (this.state.phase !== 'ready') return
    const next = [...new Set(kinds)]
    if (sameList(next, this.state.busyOperations)) return
    if (next.length) this.set({ busyOperations: next })
    else this.set({}, { clear: ['busyOperations'] })
  }

  isReadyAndFree(): boolean {
    return this.state.phase === 'ready' && (this.state.busyOperations?.length ?? 0) === 0
  }

  /**
   * Instala la actualización lista (D-T11, D-T12). Nunca con operaciones en
   * curso: `POST /agent/maintenance` devuelve 409 y no instala.
   */
  async installNow(opts: { relaunch: boolean }): Promise<InstallResult> {
    if (this.state.phase !== 'ready' || this.installing) return { ok: false, reason: 'not_ready' }
    this.installing = true
    try {
      const m = await enterMaintenance(this.getPort(), agentProcess.isRunning(), 120)
      if (m.kind === 'busy') {
        const kinds = uniqueKinds(m.operations)
        updaterLog.info(`No se instala: hay operaciones en curso (${kinds.join(', ')}).`)
        this.set({ busyOperations: kinds })
        return { ok: false, reason: 'busy' }
      }
      if (m.kind === 'error') {
        // Respuesta inesperada, o agente vivo que no contestó (timeout/reset: puede estar
        // armando un ZIP o en ffmpeg). Ante la duda NO se instala ni se lo mata (D5).
        updaterLog.warn(m.failure
          ? `No se instala: el agente está corriendo pero no respondió a /agent/maintenance (${m.failure}).`
          : `No se instala: /agent/maintenance respondió ${m.status}.`)
        return { ok: false, reason: 'error' }
      }
      // 'ok' (en mantenimiento) o 'unreachable' (el proceso del agente no corre o nadie
      // escucha el puerto: no puede haber operaciones en curso).

      this.set({ phase: 'installing' }, { clear: ['busyOperations'] })
      updaterLog.info(`Instalando v${this.state.availableVersion} (relanzar=${opts.relaunch}).`)

      const binDir = agentProcess.binaryDir()
      await agentProcess.stopAndWait()
      if (process.platform === 'win32' && binDir) {
        await runQuiet(join(binDir, 'tools', 'platform-tools', 'adb.exe'), ['kill-server'], 5000)
        const like = join(binDir, '*').replace(/'/g, "''")
        await runQuiet('powershell.exe', [
          '-NoProfile', '-NonInteractive', '-Command',
          `Get-Process | Where-Object { $_.Path -like '${like}' } | Stop-Process -Force -ErrorAction SilentlyContinue`,
        ], 10_000)
      }

      let quitting = false
      const onBeforeQuit = () => { quitting = true }
      app.once('before-quit', onBeforeQuit)
      try {
        autoUpdater.quitAndInstall(true, opts.relaunch)
      } catch (err) {
        app.removeListener('before-quit', onBeforeQuit)
        this.failInstall(err)
        return { ok: false, reason: 'error' }
      }
      // quitAndInstall no tira si no puede lanzar el instalador: si la app no empezó a
      // cerrarse en un rato, se considera fallido y se vuelve a levantar el agente.
      setTimeout(() => {
        if (!quitting) {
          app.removeListener('before-quit', onBeforeQuit)
          this.failInstall(new Error('el instalador no arrancó'))
        }
      }, QUIT_WATCHDOG_MS)
      return { ok: true }
    } finally {
      this.installing = false
    }
  }

  private failInstall(err: unknown): void {
    updaterLog.error('No se pudo instalar la actualización:', err)
    agentProcess.start()
    this.set({ phase: 'error', reason: 'instalacion_fallida' })
  }
}

export const updateManager = new UpdateManager()
