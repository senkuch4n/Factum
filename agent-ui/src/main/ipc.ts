import { app, ipcMain, shell } from 'electron'
import { spawn } from 'child_process'
import * as http from 'http'
import * as https from 'https'
import { readConfig, writeConfig, applyLoginItem } from './config'
import { agentProcess } from './agent-process'
import { confirmSessionShutdown } from './session-shutdown'
import { appInfo, type NoticeId } from './app-info'
import { updateManager } from './updater/update-manager'
import { getAgentState, getHealth } from './agent-api'
import { canonicalAppImagePath } from './linux/paths'
import { installOffer, installOnPc, isInstalled, openInstalled } from './linux/desktop-integration'
import { execute as executePreparePc, logPath as prepareLogPath, readPreparacion, simulate as simulatePreparePc } from './linux/prepare-pc'

/** Linux empaquetado (tatana-linux D-T19): sin pantalla "Librerías" ni instalación por shell. */
const LINUX_PACKAGED = process.platform === 'linux' && app.isPackaged

// ── Config ─────────────────────────────────────────────────────────────────────
ipcMain.handle('tatana:get-config', () => readConfig())

ipcMain.handle('tatana:save-config', (_e, patch: Record<string, unknown>) => {
  const config = writeConfig(patch as never)
  applyLoginItem(config)
  return config
})

// ── App y actualizaciones (SDD §6.7) ─────────────────────────────────────────
ipcMain.handle('tatana:get-app-info', () => appInfo.get())
ipcMain.handle('tatana:get-update-state', () => updateManager.getState())
ipcMain.handle('tatana:check-updates', async () => {
  // Devuelve el estado apenas arranca el chequeo; el resto llega por `tatana:update-state-change`.
  void updateManager.check()
  return updateManager.getState()
})
ipcMain.handle('tatana:install-update', () => updateManager.installNow({ relaunch: true }))
ipcMain.handle('tatana:dismiss-notice', (_e, id: unknown) => {
  if (id === 'migration' || id === 'local_config') appInfo.dismiss(id as NoticeId)
})

// ── Agent process ──────────────────────────────────────────────────────────────
ipcMain.handle('tatana:agent-status', () => ({ running: agentProcess.isRunning() }))

ipcMain.handle('tatana:restart-agent', async () => {
  // Grabación de la sesión en curso (grabacion-sesion-completa §10): mismo diálogo que la bandeja.
  if (!(await confirmSessionShutdown())) return { ok: false }
  await agentProcess.restart()
  return { ok: true }
})

// ── Server connection ──────────────────────────────────────────────────────────
ipcMain.handle('tatana:check-connection', (_e, url: string) => {
  return new Promise<{ ok: boolean; ms: number; error?: string }>((resolve) => {
    const deadline = setTimeout(() => resolve({ ok: false, ms: 0, error: 'Timeout' }), 5000)
    const t0 = Date.now()
    try {
      const target = new URL(`${url.replace(/\/$/, '')}/health`)
      const mod = target.protocol === 'https:' ? https : http
      const req = mod.get(target.toString(), (res) => {
        clearTimeout(deadline)
        res.resume()
        resolve({ ok: (res.statusCode ?? 500) < 500, ms: Date.now() - t0 })
      })
      req.on('error', (err) => {
        clearTimeout(deadline)
        resolve({ ok: false, ms: Date.now() - t0, error: err.message })
      })
    } catch (err: unknown) {
      clearTimeout(deadline)
      resolve({ ok: false, ms: 0, error: String(err) })
    }
  })
})

// ── Devices ────────────────────────────────────────────────────────────────────
ipcMain.handle('tatana:get-devices', (_e, port: number = 8765) => {
  return new Promise<unknown[]>((resolve) => {
    const deadline = setTimeout(() => resolve([]), 3000)
    http.get(`http://localhost:${port}/devices`, (res) => {
      let data = ''
      res.on('data', (c) => { data += c })
      res.on('end', () => {
        clearTimeout(deadline)
        try {
          const parsed = JSON.parse(data)
          // Agent may return [] or { devices: [] }
          resolve(Array.isArray(parsed) ? parsed : (parsed?.devices ?? []))
        } catch { resolve([]) }
      })
    }).on('error', () => { clearTimeout(deadline); resolve([]) })
  })
})

// ── Libraries ──────────────────────────────────────────────────────────────────
interface LibDef {
  id: string
  name: string
  description: string
  checkCmd: string
  install: Record<string, string>
  update: Record<string, string>
}

const LIBRARIES: LibDef[] = [
  {
    id: 'adb',
    name: 'ADB',
    description: 'Android Debug Bridge — extracción de dispositivos Android',
    checkCmd: 'adb --version',
    install: {
      darwin: 'brew install android-platform-tools',
      win32:  'winget install Google.PlatformTools',
      linux:  'sudo apt-get install -y adb',
    },
    update: {
      darwin: 'brew upgrade android-platform-tools',
      win32:  'winget upgrade Google.PlatformTools',
      linux:  'sudo apt-get upgrade -y adb',
    },
  },
  {
    id: 'pymobiledevice3',
    name: 'pymobiledevice3',
    description: 'Soporte para extracción de dispositivos iOS / iPhone',
    checkCmd: process.platform === 'win32'
      ? 'python -c "import pymobiledevice3; print(pymobiledevice3.__version__)"'
      : 'python3 -c "import pymobiledevice3; print(pymobiledevice3.__version__)"',
    install: {
      darwin: 'pip3 install pymobiledevice3',
      win32:  'pip install pymobiledevice3',
      linux:  'pip3 install pymobiledevice3',
    },
    update: {
      darwin: 'pip3 install --upgrade pymobiledevice3',
      win32:  'pip install --upgrade pymobiledevice3',
      linux:  'pip3 install --upgrade pymobiledevice3',
    },
  },
]

function check(cmd: string): Promise<{ ok: boolean; version: string }> {
  return new Promise((resolve) => {
    let out = ''
    const proc = spawn(cmd, { shell: true, stdio: ['ignore', 'pipe', 'pipe'] })
    proc.stdout.on('data', (d) => { out += d })
    proc.on('close', (code) => resolve({ ok: code === 0, version: out.trim().split('\n')[0] ?? '' }))
    proc.on('error', () => resolve({ ok: false, version: '' }))
    setTimeout(() => { proc.kill(); resolve({ ok: false, version: '' }) }, 8000)
  })
}

ipcMain.handle('tatana:get-libraries', async () =>
  LINUX_PACKAGED ? [] : Promise.all(
    LIBRARIES.map(async (lib) => {
      const { ok, version } = await check(lib.checkCmd)
      return { id: lib.id, name: lib.name, description: lib.description, installed: ok, version: ok ? version : undefined }
    }),
  ),
)

function stream(cmd: string, sender: Electron.WebContents, channel: string): Promise<void> {
  return new Promise((resolve) => {
    const proc = spawn(cmd, { shell: true, stdio: ['ignore', 'pipe', 'pipe'] })
    const push = (line: string, done = false, ok = false) =>
      !sender.isDestroyed() && sender.send(channel, { line, done, ok })
    proc.stdout.on('data', (d) => push(String(d).trimEnd()))
    proc.stderr.on('data', (d) => push(String(d).trimEnd()))
    proc.on('close', (code) => { push('', true, code === 0); resolve() })
    proc.on('error', (err) => { push(err.message, true, false); resolve() })
  })
}

ipcMain.handle('tatana:install-library', async (e, id: string) => {
  if (LINUX_PACKAGED) return
  const lib = LIBRARIES.find((l) => l.id === id)
  if (!lib) return
  const cmd = lib.install[process.platform] ?? lib.install['linux']
  await stream(cmd, e.sender, `tatana:lib-progress:${id}`)
})

ipcMain.handle('tatana:update-library', async (e, id: string) => {
  if (LINUX_PACKAGED) return
  const lib = LIBRARIES.find((l) => l.id === id)
  if (!lib) return
  const cmd = lib.update[process.platform] ?? lib.update['linux']
  await stream(cmd, e.sender, `tatana:lib-progress:${id}`)
})

// ── Linux: "Esta PC" (tatana-linux §10.G) ─────────────────────────────────────

const IS_LINUX = process.platform === 'linux'

ipcMain.handle('tatana:get-linux-status', async () => {
  const health = IS_LINUX ? await getHealth(readConfig().agentPort) : null
  return {
    health: health?.linux ?? null,
    preparation: IS_LINUX ? readPreparacion() : null,
    installed: IS_LINUX && isInstalled(),
    canonicalPath: IS_LINUX ? canonicalAppImagePath() : '',
    // Aditivo a §10.G: qué ofrece la tarjeta de instalación (D-T6).
    offer: IS_LINUX && app.isPackaged ? installOffer() : { kind: 'none' },
  }
})

ipcMain.handle('tatana:simulate-prepare-pc', () => (IS_LINUX ? simulatePreparePc() : null))

ipcMain.handle('tatana:prepare-pc', (e) => {
  if (!IS_LINUX) return null
  const sender = e.sender
  return executePreparePc((paso) => {
    if (!sender.isDestroyed()) sender.send('tatana:prepare-pc-step', paso)
  })
})

ipcMain.handle('tatana:open-prepare-log', async () => {
  if (!IS_LINUX) return
  await shell.openPath(prepareLogPath())
})

/**
 * "Instalar (recomendado)" / "Reemplazar la instalada" / "Abrir la instalada"
 * (D-T6). Relanza Tatana: antes corta el agente sin huérfanos y nunca con una
 * grabación en curso.
 */
export async function runInstallOnPc(): Promise<{ ok: boolean; error?: string }> {
  if (!IS_LINUX || !app.isPackaged) return { ok: false, error: 'Solo disponible en Tatana para Linux.' }
  const state = await getAgentState(readConfig().agentPort, 2000)
  if (state && state.operations.length > 0) {
    return { ok: false, error: 'Hay una grabación u operación en curso. Terminala y volvé a intentar.' }
  }
  if (!(await confirmSessionShutdown())) return { ok: false, error: 'Se canceló para no cortar la grabación de la sesión.' }
  const beforeRelaunch = () => agentProcess.stopAndWait()
  const offer = installOffer()
  if (offer.kind === 'open') {
    await openInstalled(beforeRelaunch)
    return { ok: true }
  }
  return installOnPc({ autostart: readConfig().autostart, beforeRelaunch })
}

ipcMain.handle('tatana:install-on-pc', () => runInstallOnPc())
