/// <reference types="vite/client" />

interface TatanaConfig {
  serverUrl: string
  autostart: boolean
  agentPort: number
}

interface TatanaDevice {
  serial: string
  platform: 'android' | 'ios'
  model?: string
  state?: string
  manufacturer?: string
  android_version?: number
  ios_version?: string
}

interface TatanaLibrary {
  id: string
  name: string
  description: string
  installed: boolean
  version?: string
}

interface TatanaAPI {
  getConfig(): Promise<TatanaConfig>
  saveConfig(patch: Partial<TatanaConfig>): Promise<TatanaConfig>
  getAgentStatus(): Promise<{ running: boolean }>
  restartAgent(): Promise<{ ok: boolean }>
  checkConnection(url: string): Promise<{ ok: boolean; ms: number; error?: string }>
  getDevices(port?: number): Promise<TatanaDevice[]>
  getLibraries(): Promise<TatanaLibrary[]>
  installLibrary(id: string): Promise<void>
  updateLibrary(id: string): Promise<void>
  onAgentLog(cb: (lines: string[]) => void): void
  onAgentStatusChange(cb: (status: string) => void): void
  onLibProgress(id: string, cb: (p: { line: string; done: boolean; ok: boolean }) => void): void
  removeAllListeners(channel: string): void
}

// Este archivo es un script global (sin import/export), así que `Window` se
// amplía directamente: un `declare global` acá TypeScript lo ignora.
interface Window {
  tatana: TatanaAPI
}

/** Versión de agent-ui (package.json), inyectada por `define` de Vite. */
declare const __TATANA_VERSION__: string
