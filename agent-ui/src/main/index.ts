import { app, BrowserWindow, Tray, Menu, nativeImage } from 'electron'
import { join } from 'path'
import { is } from '@electron-toolkit/utils'
import { autoUpdater } from 'electron-updater'
import { readConfig } from './config'
import { agentProcess } from './agent-process'
import './ipc'

// Evitar crash por promesas no manejadas en el proceso principal
process.on('unhandledRejection', (reason) => {
  console.error('[Tatana] unhandledRejection:', reason)
})

let mainWindow: BrowserWindow | null = null
let tray: Tray | null = null
let isQuitting = false

function createWindow(): void {
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

  mainWindow.on('ready-to-show', () => mainWindow!.show())

  // Minimize to tray instead of closing
  mainWindow.on('close', (e) => {
    if (!isQuitting) {
      e.preventDefault()
      mainWindow!.hide()
    }
  })

  if (is.dev && process.env['ELECTRON_RENDERER_URL']) {
    mainWindow.loadURL(process.env['ELECTRON_RENDERER_URL'])
    mainWindow.webContents.openDevTools({ mode: 'detach' })
  } else {
    mainWindow.loadFile(join(__dirname, '../renderer/index.html'))
  }
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
  tray.setToolTip('Factum — Agente Tatana')
  tray.setContextMenu(
    Menu.buildFromTemplate([
      { label: 'Abrir Tatana', click: () => { mainWindow?.show(); mainWindow?.focus() } },
      { type: 'separator' },
      {
        label: 'Reiniciar agente .NET',
        click: () => { agentProcess.stop(); setTimeout(() => agentProcess.start(), 600) },
      },
      { type: 'separator' },
      { label: 'Salir', click: () => { isQuitting = true; app.quit() } },
    ]),
  )
  tray.on('double-click', () => { mainWindow?.show(); mainWindow?.focus() })
}

// Batch log lines — the .NET agent can emit 50+ lines at once (stack traces, startup),
// sending each individually would flood React with re-renders and blank the UI.
const logBuffer: string[] = []
let logFlushTimer: NodeJS.Timeout | null = null

function flushLogs(): void {
  if (logBuffer.length === 0) return
  const lines = logBuffer.splice(0)
  try {
    if (mainWindow && !mainWindow.isDestroyed()) {
      mainWindow.webContents.send('tatana:agent-log-batch', lines)
    }
  } catch { /* window destroyed during quit */ }
}

function pushLog(line: string): void {
  logBuffer.push(line)
  if (!logFlushTimer) {
    logFlushTimer = setTimeout(() => { logFlushTimer = null; flushLogs() }, 150)
  }
}

agentProcess.on('log', pushLog)

agentProcess.on('status', (status: string) => {
  try {
    if (mainWindow && !mainWindow.isDestroyed()) {
      mainWindow.webContents.send('tatana:agent-status-change', status)
    }
  } catch { /* window destroyed during quit */ }
})

// ── Auto-actualización de Tatana (instalador Electron) ──────────────────────
// El feed ("publish.url" en package.json) apunta al backend de Factum, no
// a GitLab directo — la PC del fiscal puede no tener salida a internet, pero
// sí llega al backend, que espeja los artifacts de la última release.
autoUpdater.autoDownload = true
autoUpdater.autoInstallOnAppQuit = true
autoUpdater.on('checking-for-update', () => pushLog('[Tatana] Buscando actualizaciones...'))
autoUpdater.on('update-available', (info) => pushLog(`[Tatana] Actualización disponible: v${info.version}`))
autoUpdater.on('update-not-available', () => pushLog('[Tatana] Ya está en la última versión.'))
autoUpdater.on('download-progress', (p) => pushLog(`[Tatana] Descargando actualización... ${Math.round(p.percent)}%`))
autoUpdater.on('update-downloaded', (info) =>
  pushLog(`[Tatana] Actualización v${info.version} descargada — se instala al cerrar la app.`))
autoUpdater.on('error', (err) => pushLog(`[Tatana] Error buscando actualizaciones: ${err.message}`))

app.whenReady().then(() => {
  app.setAppUserModelId('com.factum.tatana')

  const config = readConfig()
  app.setLoginItemSettings({ openAtLogin: config.autostart })

  createWindow()
  createTray()

  agentProcess.setPort(config.agentPort)
  agentProcess.start()

  if (app.isPackaged) {
    autoUpdater.checkForUpdatesAndNotify().catch((err) =>
      pushLog(`[Tatana] No se pudo chequear actualizaciones: ${err instanceof Error ? err.message : err}`))
  }

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow()
    else { mainWindow?.show(); mainWindow?.focus() }
  })
})

app.on('before-quit', () => { isQuitting = true })
app.on('will-quit', () => agentProcess.stop())
app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit()
})
