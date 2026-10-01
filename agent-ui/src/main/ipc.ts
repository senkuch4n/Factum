import { ipcMain, app } from 'electron'
import { spawn } from 'child_process'
import * as http from 'http'
import * as https from 'https'
import { readConfig, writeConfig } from './config'
import { agentProcess } from './agent-process'

// ── Config ─────────────────────────────────────────────────────────────────────
ipcMain.handle('tatana:get-config', () => readConfig())

ipcMain.handle('tatana:save-config', (_e, patch: Record<string, unknown>) => {
  const config = writeConfig(patch as never)
  app.setLoginItemSettings({ openAtLogin: config.autostart })
  return config
})

// ── Agent process ──────────────────────────────────────────────────────────────
ipcMain.handle('tatana:agent-status', () => ({ running: agentProcess.isRunning() }))

ipcMain.handle('tatana:restart-agent', () => {
  agentProcess.stop()
  setTimeout(() => agentProcess.start(), 600)
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
  Promise.all(
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
  const lib = LIBRARIES.find((l) => l.id === id)
  if (!lib) return
  const cmd = lib.install[process.platform] ?? lib.install['linux']
  await stream(cmd, e.sender, `tatana:lib-progress:${id}`)
})

ipcMain.handle('tatana:update-library', async (e, id: string) => {
  const lib = LIBRARIES.find((l) => l.id === id)
  if (!lib) return
  const cmd = lib.update[process.platform] ?? lib.update['linux']
  await stream(cmd, e.sender, `tatana:lib-progress:${id}`)
})
