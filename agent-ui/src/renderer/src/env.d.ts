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

/* ── Actualizaciones (SDD tatana-instalador-autoupdate §13.F) ──
   Espejo de src/main/updater/state.ts y src/main/app-info.ts (el renderer no
   importa código de main). */
type TatanaUpdatePhase =
  | 'disabled' | 'idle' | 'checking' | 'downloading' | 'ready' | 'installing'
  | 'up_to_date' | 'offline' | 'rejected' | 'error'

interface TatanaUpdateState {
  phase: TatanaUpdatePhase
  currentVersion: string
  availableVersion?: string
  percent?: number
  sourceUrl?: string
  checkedAt?: string
  verifiedAt?: string
  reason?: 'firma_invalida' | 'clave_desconocida' | 'manifiesto_invalido' | 'no_coincide_con_manifiesto'
         | 'hash_no_coincide' | 'sin_canal' | 'sin_claves' | 'no_empaquetado' | 'instalacion_fallida'
  busyOperations?: string[]
  notifiedVersion?: string
}

/** `%APPDATA%\Tatana\migracion-portable.json` (§13.E). */
interface TatanaMigracionPortable {
  schema: 1
  fecha: string
  portable_version: string | null
  carpeta_anterior: string
  config_local_migrada: boolean
  config_local_destino: string | null
  conflicto: boolean
  mostrado: boolean
}

interface TatanaAppInfo {
  appVersion: string
  agentVersion?: string
  clientUrl?: string
  notices: {
    migration?: TatanaMigracionPortable
    localConfig?: { path: string; overridesAllowedOrigins: boolean; error?: string }
  }
}

type TatanaNoticeId = 'migration' | 'local_config'

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
  getAppInfo(): Promise<TatanaAppInfo>
  getUpdateState(): Promise<TatanaUpdateState>
  checkUpdates(): Promise<TatanaUpdateState>
  installUpdate(): Promise<{ ok: boolean; reason?: 'busy' | 'not_ready' | 'error' }>
  dismissNotice(id: TatanaNoticeId): Promise<void>
  onUpdateStateChange(cb: (s: TatanaUpdateState) => void): void
  onAppInfoChange(cb: (info: TatanaAppInfo) => void): void
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
