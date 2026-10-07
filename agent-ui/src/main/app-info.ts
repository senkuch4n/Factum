/**
 * Información de la app para la ventana y la bandeja (SDD §6.7, §13.F, F17):
 * versión de la app y del agente, `client_url` y avisos cerrables
 *   - migración del portátil (`%APPDATA%\Tatana\migracion-portable.json`, §13.E);
 *   - config local que fija los orígenes (D9) o que es inválida
 *     (`/agent/state.local_config`).
 * Además refresca cada 15 s las operaciones en curso del agente para el
 * actualizador ("Reiniciar y actualizar" deshabilitado con el motivo).
 */
import { app } from 'electron'
import { EventEmitter } from 'events'
import { existsSync, readFileSync, renameSync, writeFileSync } from 'fs'
import { join } from 'path'
import { getAgentState, type AgentStateResponse } from './agent-api'
import { readConfig, writeConfig } from './config'
import { readDistConfig } from './updater/dist-config'
import { updateManager } from './updater/update-manager'

export interface MigracionPortable {
  schema: 1
  fecha: string
  portable_version: string | null
  carpeta_anterior: string
  config_local_migrada: boolean
  config_local_destino: string | null
  conflicto: boolean
  mostrado: boolean
}

export interface LocalConfigNotice {
  path: string
  overridesAllowedOrigins: boolean
  error?: string
}

export interface TatanaAppInfo {
  appVersion: string
  agentVersion?: string
  clientUrl?: string
  notices: { migration?: MigracionPortable; localConfig?: LocalConfigNotice }
}

export type NoticeId = 'migration' | 'local_config'

const POLL_MS = 15_000

function migrationPath(): string {
  return join(app.getPath('userData'), 'migracion-portable.json')
}

function readMigration(): MigracionPortable | null {
  try {
    const p = migrationPath()
    if (!existsSync(p)) return null
    const raw = JSON.parse(readFileSync(p, 'utf-8').replace(/^﻿/, '')) as Partial<MigracionPortable>
    if (!raw || raw.schema !== 1 || typeof raw.carpeta_anterior !== 'string') return null
    return {
      schema: 1,
      fecha: String(raw.fecha ?? ''),
      portable_version: typeof raw.portable_version === 'string' && raw.portable_version ? raw.portable_version : null,
      carpeta_anterior: raw.carpeta_anterior,
      config_local_migrada: raw.config_local_migrada === true,
      config_local_destino: typeof raw.config_local_destino === 'string' ? raw.config_local_destino : null,
      conflicto: raw.conflicto === true,
      mostrado: raw.mostrado === true,
    }
  } catch {
    return null
  }
}

/** Firma del aviso de config local: si cambia (otro archivo, otro error), se vuelve a mostrar. */
function localConfigSignature(n: LocalConfigNotice): string {
  return JSON.stringify([n.path, n.overridesAllowedOrigins, n.error ?? null])
}

function localConfigNotice(state: AgentStateResponse | null): LocalConfigNotice | undefined {
  const lc = state?.local_config
  if (!lc || !lc.exists) return undefined
  if (!lc.error && !lc.overrides_allowed_origins) return undefined
  return {
    path: lc.path,
    overridesAllowedOrigins: lc.overrides_allowed_origins,
    ...(lc.error ? { error: lc.error } : {}),
  }
}

class AppInfoService extends EventEmitter {
  private agentState: AgentStateResponse | null = null
  private timer: NodeJS.Timeout | null = null
  private last = ''

  get(): TatanaAppInfo {
    const notices: TatanaAppInfo['notices'] = {}
    const migration = readMigration()
    if (migration && !migration.mostrado) notices.migration = migration
    const lc = localConfigNotice(this.agentState)
    if (lc && readConfig().dismissedLocalConfigNotice !== localConfigSignature(lc)) notices.localConfig = lc
    const clientUrl = readDistConfig()?.client_url
    return {
      appVersion: app.getVersion(),
      ...(this.agentState?.version ? { agentVersion: this.agentState.version } : {}),
      ...(clientUrl ? { clientUrl } : {}),
      notices,
    }
  }

  /** Versión del agente según `/agent/state` (undefined si no respondió). */
  agentVersion(): string | undefined {
    return this.agentState?.version
  }

  private publish(): void {
    const info = this.get()
    const key = JSON.stringify(info)
    if (key === this.last) return
    this.last = key
    this.emit('change', info)
  }

  /** Poll de `/agent/state` (cada 15 s; `refreshSoon` lo adelanta al arrancar el agente). */
  start(getPort: () => number): void {
    if (this.timer) return
    const tick = async () => {
      const s = await getAgentState(getPort())
      this.agentState = s
      updateManager.setBusyOperations(s ? [...new Set(s.operations.map((o) => o.kind))] : [])
      this.publish()
    }
    this.refreshSoon = () => { setTimeout(() => { void tick() }, 2500) }
    void tick()
    this.timer = setInterval(() => { void tick() }, POLL_MS)
  }

  refreshSoon: () => void = () => {}

  stop(): void {
    if (this.timer) clearInterval(this.timer)
    this.timer = null
  }

  dismiss(id: NoticeId): void {
    if (id === 'migration') {
      const p = migrationPath()
      try {
        if (existsSync(p)) {
          const raw = JSON.parse(readFileSync(p, 'utf-8').replace(/^﻿/, '')) as Record<string, unknown>
          raw.mostrado = true
          const tmp = `${p}.tmp`
          writeFileSync(tmp, JSON.stringify(raw, null, 2), 'utf-8')
          renameSync(tmp, p)
        }
      } catch { /* si no se puede escribir, el aviso vuelve a aparecer: inofensivo */ }
    } else {
      const lc = localConfigNotice(this.agentState)
      if (lc) writeConfig({ dismissedLocalConfigNotice: localConfigSignature(lc) })
    }
    this.publish()
  }
}

export const appInfo = new AppInfoService()
