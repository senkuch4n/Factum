import { app } from 'electron'
import { join } from 'path'
import { readFileSync, writeFileSync, existsSync } from 'fs'
import { readDistConfig } from './updater/dist-config'

export interface TatanaConfig {
  serverUrl: string
  autostart: boolean
  agentPort: number
  /**
   * Override avanzado, sin UI, de la carpeta de datos del agente (D-T3). Vacío
   * = la de siempre (`%LOCALAPPDATA%\Tatana\data` instalado en Windows).
   */
  agentDataDir?: string
  /** Firma del aviso de config local que el perito cerró (se vuelve a mostrar si cambia). */
  dismissedLocalConfigNotice?: string
}

/**
 * Defaults (D-T14): arranque al iniciar sesión solo empaquetado; el servidor
 * por defecto es el primer origen horneado (`client_url`).
 */
function defaults(): TatanaConfig {
  return {
    serverUrl: readDistConfig()?.client_url ?? 'http://localhost:5000',
    autostart: app.isPackaged,
    agentPort: 8765,
  }
}

function configPath(): string {
  return join(app.getPath('userData'), 'tatana.json')
}

export function readConfig(): TatanaConfig {
  try {
    if (existsSync(configPath())) {
      return { ...defaults(), ...JSON.parse(readFileSync(configPath(), 'utf-8')) }
    }
  } catch {}
  return defaults()
}

export function writeConfig(patch: Partial<TatanaConfig>): TatanaConfig {
  const updated = { ...readConfig(), ...patch }
  writeFileSync(configPath(), JSON.stringify(updated, null, 2), 'utf-8')
  return updated
}

/**
 * Arranque al iniciar sesión (D-T14): solo empaquetado (en dev no se registra
 * nada) y con `--hidden`, así la ventana no aparece en cada login.
 */
export function applyLoginItem(config: TatanaConfig): void {
  if (!app.isPackaged) return
  app.setLoginItemSettings({
    openAtLogin: config.autostart,
    path: process.execPath,
    args: ['--hidden'],
  })
}
