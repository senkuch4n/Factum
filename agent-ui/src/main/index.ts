import { app, BrowserWindow, Tray, Menu, Notification, dialog, nativeImage, shell } from 'electron'
import { join } from 'path'
import { is } from '@electron-toolkit/utils'
import { readConfig, writeConfig, applyLoginItem } from './config'
import { agentProcess } from './agent-process'
import { appInfo } from './app-info'
import { updateManager } from './updater/update-manager'
import { setUpdaterLogSink } from './updater/logger'
import { readDistConfig } from './updater/dist-config'
import { backgroundOperationsNotice, busyOperationsText, type UpdateState } from './updater/state'
import { runInstallOnPc } from './ipc'
import { confirmSessionShutdown } from './session-shutdown'
import { getHealth } from './agent-api'
import { checkLinuxRequirements } from './linux/requirements'
import { hasStatusNotifierWatcher } from './linux/tray-support'
import { installOffer, isInstalled, refreshDesktopEntries } from './linux/desktop-integration'
import { readPreparacion } from './linux/prepare-pc'

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
/** Linux sin StatusNotifierWatcher (D-T18): cerrar minimiza en vez de esconder. */
let trayAvailable = true
const LINUX_PACKAGED = process.platform === 'linux' && app.isPackaged

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

  // Cerrar = esconder en la bandeja. En Linux sin bandeja (D-T18) se minimiza:
  // si se escondiera, solo se podría volver a abrir desde el menú.
  mainWindow.on('close', (e) => {
    if (!isQuitting) {
      e.preventDefault()
      if (process.platform === 'linux' && !trayAvailable) mainWindow!.minimize()
      else mainWindow!.hide()
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
  // Grabación de la sesión en curso (grabacion-sesion-completa §10): diálogo y cierre ordenado del tramo.
  if (!(await confirmSessionShutdown())) return
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
          : backgroundOperationsNotice(s.backgroundOperations)
            ? 'Reiniciar y actualizar (cancela la versión suavizada en curso)'
            : 'Reiniciar y actualizar',
        visible: s.phase === 'ready',
        enabled: busy.length === 0,
        click: () => { void requestInstall() },
      },
      {
        label: 'Reiniciar agente .NET',
        enabled: s.phase !== 'installing',
        click: async () => {
          if (!(await confirmSessionShutdown())) return
          await agentProcess.restart()
        },
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

// ── Linux: instalación y "Preparar esta PC" (tatana-linux D-T6, §8.3) ────────

/** Abre la ventana en la vista "Esta PC". */
function openLinuxView(): void {
  showWindow()
  sendToWindow('tatana:open-view', 'linux')
}

/**
 * Oferta de instalación la primera vez (D-T6): un diálogo, además de la tarjeta
 * de "Esta PC" que queda siempre mientras no esté instalada.
 */
async function offerLinuxInstall(startHidden: boolean): Promise<void> {
  const offer = installOffer()
  if (offer.kind === 'none' || startHidden || readConfig().linuxInstallOffered) return
  writeConfig({ linuxInstallOffered: true })
  const install = offer.kind === 'install'
  const accept = install ? 'Instalar (recomendado)' : offer.kind === 'replace' ? 'Reemplazar la instalada' : 'Abrir la instalada'
  const detail = install
    ? 'Tatana se copia a la carpeta Applications de tu usuario, aparece en el menú de aplicaciones y arranca solo al iniciar sesión. ' +
      'Sin instalar, no recibe actualizaciones. Después podés borrar el archivo que descargaste.'
    : offer.kind === 'replace'
      ? `Hay una versión anterior instalada (v${offer.installed}). Se reemplaza por esta (v${app.getVersion()}).`
      : `Ya hay un Tatana instalado (v${offer.installed}). Conviene usar ese: es el que recibe actualizaciones.`
  const win = mainWindow && !mainWindow.isDestroyed() ? mainWindow : undefined
  const opts = {
    type: 'question' as const,
    title: 'Tatana',
    message: install ? 'Instalá Tatana en esta PC' : 'Tatana ya está instalado en esta PC',
    detail,
    buttons: [accept, 'Ahora no'],
    defaultId: 0,
    cancelId: 1,
    noLink: true,
  }
  const { response } = win ? await dialog.showMessageBox(win, opts) : await dialog.showMessageBox(opts)
  if (response !== 0) return
  const r = await runInstallOnPc()
  if (!r.ok && r.error) {
    await dialog.showMessageBox({ type: 'error', title: 'Tatana', message: 'No se pudo instalar Tatana', detail: r.error })
  }
}

/**
 * La primera vez sin `preparacion-linux.json`, o con `/health.linux.setup_state =
 * "incomplete"`, abre "Esta PC". Arrancado con `--hidden`, una sola notificación.
 */
let linuxSetupChecked = false
async function checkLinuxSetup(startHidden: boolean): Promise<void> {
  if (linuxSetupChecked) return
  linuxSetupChecked = true
  const health = await getHealth(readConfig().agentPort)
  const needsSetup = !readPreparacion() || health?.linux?.setup_state === 'incomplete'
  if (!needsSetup) return
  if (startHidden && !(mainWindow?.isVisible() ?? false)) {
    try {
      if (!Notification.isSupported()) return
      const n = new Notification({ title: 'Tatana', body: 'Tatana necesita preparar esta PC para reconocer iPhone y Android' })
      n.on('click', openLinuxView)
      n.show()
    } catch { /* sin notificaciones del sistema: queda "Esta PC" en la ventana */ }
    return
  }
  openLinuxView()
}

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
    // Linux (D-T2): requisitos mínimos antes de crear la ventana. Va después de
    // `ready` porque en Linux `showErrorBox` antes de `ready` solo escribe en stderr.
    if (LINUX_PACKAGED && !checkLinuxRequirements({ dialog, app })) return

    app.setAppUserModelId('com.factum.tatana')

    const config = readConfig()
    applyLoginItem(config)

    // Relanzado después de instalar una actualización: oculto, igual que `--hidden`.
    const startHidden = hasArg(process.argv, '--hidden') || updateManager.previousPhase() === 'installing'
    if (LINUX_PACKAGED) {
      trayAvailable = await hasStatusNotifierWatcher()
      appInfo.setTrayAvailable(trayAvailable)
      // Autorreparación del menú y del autoarranque desde la canónica (D-T6).
      if (isInstalled()) refreshDesktopEntries(config.autostart)
    }
    createWindow(startHidden)
    if (LINUX_PACKAGED) {
      mainWindow?.webContents.once('did-finish-load', () => { void offerLinuxInstall(startHidden) })
    }
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

    if (LINUX_PACKAGED) {
      // Con el agente arriba (para leer `/health.linux`), una sola vez por arranque.
      const onRunning = (status: string) => {
        if (status !== 'running') return
        agentProcess.off('status', onRunning)
        setTimeout(() => { void checkLinuxSetup(startHidden) }, 3000)
      }
      agentProcess.on('status', onRunning)
    }

    app.on('activate', () => {
      if (BrowserWindow.getAllWindows().length === 0) createWindow(false)
      else showWindow()
    })
  })

  app.on('before-quit', () => { isQuitting = true })
  // Linux (D-T17): salir espera a cortar el árbol del agente (SIGTERM, adb
  // kill-server, grupo y barrido de /proc) para no dejar huérfanos.
  let linuxStopped = false
  app.on('will-quit', (e) => {
    appInfo.stop()
    updateManager.dispose()
    if (process.platform === 'linux' && !linuxStopped) {
      linuxStopped = true
      e.preventDefault()
      void agentProcess.stopAndWait().finally(() => app.exit(0))
      return
    }
    agentProcess.stop()
  })
  app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') app.quit()
  })
}
