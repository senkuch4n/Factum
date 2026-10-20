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
         | 'no_instalado' | 'sin_appimage'
  busyOperations?: string[]
  /** `kind`s en segundo plano que se cancelan al actualizar (ios-grabacion-larga). */
  backgroundOperations?: string[]
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

/** Instalación en Linux (espejo de `TatanaLinuxInfo` de src/main/app-info.ts). */
interface TatanaLinuxInfo {
  installed: boolean
  appImagePath?: string
  canonicalPath: string
  trayAvailable: boolean
}

interface TatanaAppInfo {
  appVersion: string
  agentVersion?: string
  clientUrl?: string
  platform: string
  packaged: boolean
  linux?: TatanaLinuxInfo
  notices: {
    migration?: TatanaMigracionPortable
    localConfig?: { path: string; overridesAllowedOrigins: boolean; error?: string }
  }
}

type TatanaNoticeId = 'migration' | 'local_config'

/* ── Linux: "Esta PC" (SDD tatana-linux §10.A, §10.B, §10.F, §10.G) ──
   Espejo de src/main/agent-api.ts y src/main/linux/prepare-pc-protocol.ts. */
type AgentLinuxCheckId = 'usbmuxd' | 'android_usb' | 'avahi' | 'uxplay' | 'camera' | 'microphone' | 'sandbox'
type AgentLinuxCheckStatus = 'ok' | 'missing' | 'outdated' | 'warning' | 'not_applicable'

interface AgentLinuxCheck {
  id: AgentLinuxCheckId
  status: AgentLinuxCheckStatus
  required: boolean
  detail?: string
}

interface AgentLinuxStatus {
  distro_id: string
  distro_version?: string
  distro_name?: string
  distro_like?: string
  glibc_version?: string
  package_manager: 'apt' | 'dnf' | 'pacman' | 'zypper' | 'unknown'
  setup_supported: boolean
  setup_state: 'ok' | 'incomplete'
  checks: AgentLinuxCheck[]
}

type TatanaPasoId = 'paquetes' | 'reglas_usb' | 'airplay' | 'camara_microfono' | 'sandbox'
type TatanaPasoEstado = 'en_curso' | 'ok' | 'error' | 'omitido'
type TatanaResultadoPreparacion =
  | 'ok' | 'parcial' | 'sin_soporte' | 'sin_internet' | 'cancelado' | 'sin_polkit' | 'error'

interface TatanaPasoPreparacion {
  id: TatanaPasoId
  estado: TatanaPasoEstado
  detalle?: string
}

interface TatanaPlanPreparacion {
  schema: 1
  soportada: boolean
  distro_id: string
  distro_version?: string
  distro_name?: string
  gestor: string
  paquetes: string[]
  uxplay?: { candidata?: string | null; instalar: boolean }
  archivos: string[]
  servicios: string[]
  firewall?: string
  grupos: string[]
  comandos: string[]
}

/** `~/.config/Tatana/preparacion-linux.json` */
interface TatanaPreparacionLinux {
  schema: 1
  fecha: string
  tatana_version: string
  distro: string
  gestor: string
  resultado: TatanaResultadoPreparacion
  requiere_reiniciar_sesion: boolean
  script_sha256: string
  pasos: TatanaPasoPreparacion[]
}

interface TatanaPrepareOutcome {
  resultado: TatanaResultadoPreparacion
  requiereReiniciarSesion: boolean
  pasos: TatanaPasoPreparacion[]
  plan?: TatanaPlanPreparacion
  fallbackCommand?: string
  scriptSha256: string
}

type TatanaInstallOffer =
  | { kind: 'none' }
  | { kind: 'install' }
  | { kind: 'replace'; installed: string }
  | { kind: 'open'; installed: string }

interface TatanaLinuxStatus {
  health: AgentLinuxStatus | null
  preparation: TatanaPreparacionLinux | null
  installed: boolean
  canonicalPath: string
  offer: TatanaInstallOffer
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
  getAppInfo(): Promise<TatanaAppInfo>
  getUpdateState(): Promise<TatanaUpdateState>
  checkUpdates(): Promise<TatanaUpdateState>
  installUpdate(): Promise<{ ok: boolean; reason?: 'busy' | 'not_ready' | 'error' }>
  dismissNotice(id: TatanaNoticeId): Promise<void>
  getLinuxStatus(): Promise<TatanaLinuxStatus>
  preparePc(): Promise<TatanaPrepareOutcome | null>
  simulatePreparePc(): Promise<TatanaPlanPreparacion | null>
  installOnPc(): Promise<{ ok: boolean; error?: string }>
  openPrepareLog(): Promise<void>
  onPrepareStep(cb: (paso: TatanaPasoPreparacion) => void): void
  /** El main pide abrir una vista (p. ej. "Esta PC" la primera vez en Linux). */
  onOpenView(cb: (view: string) => void): void
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
