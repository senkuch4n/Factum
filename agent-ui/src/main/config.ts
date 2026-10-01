import { app } from 'electron'
import { join } from 'path'
import { readFileSync, writeFileSync, existsSync } from 'fs'

export interface TatanaConfig {
  serverUrl: string
  autostart: boolean
  agentPort: number
}

const DEFAULTS: TatanaConfig = {
  serverUrl: 'http://localhost:5000',
  autostart: false,
  agentPort: 8765,
}

function configPath(): string {
  return join(app.getPath('userData'), 'tatana.json')
}

export function readConfig(): TatanaConfig {
  try {
    if (existsSync(configPath())) {
      return { ...DEFAULTS, ...JSON.parse(readFileSync(configPath(), 'utf-8')) }
    }
  } catch {}
  return { ...DEFAULTS }
}

export function writeConfig(patch: Partial<TatanaConfig>): TatanaConfig {
  const updated = { ...readConfig(), ...patch }
  writeFileSync(configPath(), JSON.stringify(updated, null, 2), 'utf-8')
  return updated
}
