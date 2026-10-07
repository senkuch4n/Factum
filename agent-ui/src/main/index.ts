import { app, BrowserWindow, Tray, Menu, nativeImage, shell } from 'electron'
import { join } from 'path'
import { is } from '@electron-toolkit/utils'
import { readConfig, applyLoginItem } from './config'
import { agentProcess } from './agent-process'
import { appInfo } from './app-info'
import { updateManager } from './updater/update-manager'
import { setUpdaterLogSink } from './updater/logger'
import { readDistConfig } from './updater/dist-config'
import { busyOperationsText, type UpdateState } from './updater/state'
import './ipc'

// D-T4: la config vive en %APPDATA%\Tatana\ (tatana.json, appsettings.Local.json,
// update-state.json, logs\). Se fija ANTES de `ready` para que no dependa de
// `name`/`productName` del package.json.
app.setPath('userData', join(app.getPath('appData'), 'Tatana'))

// Evitar crash por promesas no manejadas en el proceso principal
process.on('unhandledRejection', (reason) => {
  console.error('[Tatana] unhandledRejection:', reason)
})

let mainWindow: BrowserWindow | null = null
let tray: Tray | null = null
let isQuitting = false

/** Las órdenes por línea de comando (D-T13) llegan en argv. */
const hasArg = (argv: readonly string[], flag: string) => argv.includes(flag)

function showWindow(): void {
  if (!mainWindow || mainWindow.isDestroyed()) createWindow(false)
  mainWindow?.show()
  mainWindow?.focus()
}

function createWindow(startHidden: boolean): void {
  mainWindow = new BrowserWindow({
    width: 940,
    height: 640,
    minWidth: 740,
    minHeight: 520,
    show: false,
    autoHideMenuBar: true,
    titleBarStyle: process.platform === 'darwin' ? 'hiddenInset' : 'default',
    backgroundColor: '#f0f4f9',
    webPreferences: {
      preload: join(__dirname, '../preload/index.js'),
      sandbox: false,
      contextIsolation: true,
    },
  })

  // `--hidden` (inicio de sesión, relanzado tras actualizar): no se muestra.
  mainWindow.on('ready-to-show', () => { if (!startHidden) mainWindow!.show() })

  // Minimize to tray instead of closing
  mainWindow.on('close', (e) => {
    if (!isQuitting) {
      e.preventDefault()
      mainWindow!.hide()
    }
  })

  if (is.dev && process.env['ELECTRON_RENDERER_URL']) {
    mainWindow.loadURL(process.env['ELECTRON_RENDERER_URL'])
    if (!startHidden) mainWindow.webContents.openDevTools({ mode: 'detach' })
  } else {
    mainWindow.loadFile(join(__dirname, '../renderer/index.html'))
  }
}

function sendToWindow(channel: string, payload: unknown): void {
  try {
    if (mainWindow && !mainWindow.isDestroyed()) mainWindow.webContents.send(channel, payload)
  } catch { /* window destroyed during quit */ }
}

/**
 * "Salir" (D-T11): con una actualización lista y sin operaciones en curso,
 * instala sin relanzar. Si hay operaciones (409) o no hay nada listo, sale
 * como siempre, sin instalar.
 */
async function requestQuit(): Promise<void> {
  if (updateManager.getState().phase === 'ready') {
    const r = await updateManager.installNow({ relaunch: false })
    if (r.ok) return
  }
  isQuitting = true
  app.quit()
}

async function requestInstall(): Promise<void> {
  const r = await updateManager.installNow({ relaunch: true })
  if (!r.ok) pushLog(`[Tatana] No se instaló la actualización (${r.reason ?? 'error'}).`)
}

// ── Bandeja ──────────────────────────────────────────────────────────────────

function trayTooltip(s: UpdateState): string {
  const base = `Tatana v${app.getVersion()}`
  return s.phase === 'ready' && s.availableVersion ? `${base} — Actualización lista: v${s.availableVersion}` : base
}

function buildTrayMenu(): void {
  if (!tray) return
  const s = updateManager.getState()
  const busy = s.busyOperations ?? []
  const clientUrl = readDistConfig()?.client_url
  const canCheck = s.phase !== 'disabled' && !['checking', 'downloading', 'ready', 'installing'].includes(s.phase)
  tray.setToolTip(trayTooltip(s))
  tray.setContextMenu(
    Menu.buildFromTemplate([
      {
        label: 'Abrir Factum',
        visible: !!clientUrl,
        click: () => { if (clientUrl) void shell.openExternal(clientUrl) },
      },
      { label: 'Abrir Tatana', click: showWindow },
      { type: 'separator' },
      {
        label: 'Buscar actualizaciones',
        enabled: canCheck,
        visible: s.phase !== 'disabled',
        click: () => { void updateManager.check() },
      },
      {
        label: busy.length
          ? `Reiniciar y actualizar (esperando: ${busyOperationsText(busy)})`
          : 'Reiniciar y actualizar',
        visible: s.phase === 'ready',
        enabled: busy.length === 0,
        click: () => { void requestInstall() },
      },
      {
        label: 'Reiniciar agente .NET',
        enabled: s.phase !== 'installing',
        click: () => { agentProcess.stop(); setTimeout(() => agentProcess.start(), 600) },
      },
      { type: 'separator' },
      { label: 'Salir', click: () => { void requestQuit() } },
    ]),
  )
}

/**
 * Ícono de bandeja (marca "Sello", generado por ops/brand/build-brand.mjs).
 * - macOS: template monocromo (trayTemplate.png + @2x), que el sistema tiñe
 *   según la barra clara u oscura. Sin resize: rompería la variante @2x.
 * - Windows: tray.ico multitamaño. Linux: tray.png (+ @2x).
 * Empaquetada, la carpeta viaja con `extraResources` → `<resources>/tray`.
 */
function createTray(): void {
  let icon = nativeImage.createEmpty()
  try {
    const trayDir = app.isPackaged
      ? join(process.resourcesPath, 'tray')
      : join(__dirname, '../../resources/tray')
    const file =
      process.platform === 'darwin' ? 'trayTemplate.png'
      : process.platform === 'win32' ? 'tray.ico'
      : 'tray.png'
    const raw = nativeImage.createFromPath(join(trayDir, file))
    if (!raw.isEmpty()) {
      if (process.platform === 'darwin') raw.setTemplateImage(true)
      icon = raw
    }
  } catch { /* el ícono queda vacío: la bandeja sigue funcionando sin imagen */ }

  tray = new Tray(icon)
  buildTrayMenu()
  tray.on('double-click', showWindow)
}

// ── Log de la ventana ────────────────────────────────────────────────────────
// Batch log lines — the .NET agent can emit 50+ lines at once (stack traces, startup),
// sending each individually would flood React with re-renders and blank the UI.
const logBuffer: string[] = []
let logFlushTimer: NodeJS.Timeout | null = null

function flushLogs(): void {
  if (logBuffer.length === 0) return
  sendToWindow('tatana:agent-log-batch', logBuffer.splice(0))
}

function pushLog(line: string): void {
  logBuffer.push(line)
  if (!logFlushTimer) {
    logFlushTimer = setTimeout(() => { logFlushTimer = null; flushLogs() }, 150)
  }
}

setUpdaterLogSink(pushLog)
agentProcess.on('log', pushLog)

agentProcess.on('status', (status: string) => {
  sendToWindow('tatana:agent-status-change', status)
  if (status === 'running') appInfo.refreshSoon()
})

updateManager.on('state', (s: UpdateState) => {
  sendToWindow('tatana:update-state-change', s)
  buildTrayMenu()
})

appInfo.on('change', (info) => sendToWindow('tatana:app-info-change', info))

// ── Arranque ─────────────────────────────────────────────────────────────────

// D-T13: una sola instancia. Una segunda con `--quit` = "Salir"; con
// `--install-update` = "Reiniciar y actualizar"; sin argumentos, muestra la ventana.
if (!app.requestSingleInstanceLock()) {
  app.quit()
} else {
  app.on('second-instance', (_e, argv) => {
    if (hasArg(argv, '--quit')) void requestQuit()
    else if (hasArg(argv, '--install-update')) void requestInstall()
    else if (!hasArg(argv, '--hidden')) showWindow()
  })

  app.whenReady().then(async () => {
    app.setAppUserModelId('com.factum.tatana')

    const config = readConfig()
    applyLoginItem(config)

    // Relanzado después de instalar una actualización: oculto, igual que `--hidden`.
    const startHidden = hasArg(process.argv, '--hidden') || updateManager.previousPhase() === 'installing'
    createWindow(startHidden)
    try {
      createTray()
    } catch (err) {
      // En el runner de CI (sin bandeja) la app sigue.
      console.error('[Tatana] No se pudo crear la bandeja:', err)
    }

    agentProcess.setPort(config.agentPort)
    const getPort = () => readConfig().agentPort

    // Con una actualización pendiente se instala ANTES de lanzar el agente (D-T11).
    const installing = await updateManager.init({ getAgentPort: getPort })
    if (installing) return

    agentProcess.start()
    updateManager.schedule()
    appInfo.start(getPort)

    app.on('activate', () => {
      if (BrowserWindow.getAllWindows().length === 0) createWindow(false)
      else showWindow()
    })
  })

  app.on('before-quit', () => { isQuitting = true })
  app.on('will-quit', () => { appInfo.stop(); updateManager.dispose(); agentProcess.stop() })
  app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') app.quit()
  })
}
