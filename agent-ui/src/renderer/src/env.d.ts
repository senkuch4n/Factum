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

declare global {
  interface Window {
    tatana: TatanaAPI
  }
}
