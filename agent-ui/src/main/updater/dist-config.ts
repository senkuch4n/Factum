/**
 * Config horneada del canal (`<resources>/dist-config/tatana-dist.json`, SDD §13.C).
 * La escribe el job `instalador` de CI; en dev puede no existir (el actualizador
 * queda `disabled` con `sin_canal`). Se lee una sola vez.
 */
import { app } from 'electron'
import { existsSync, readFileSync } from 'fs'
import { join } from 'path'

export interface TatanaDistConfig {
  schema: 1
  /** Primer origen https: "Abrir Factum" y `serverUrl` por defecto. */
  client_url?: string
  /** `https://<origen>/tatana/updates/`, en orden (D8). */
  update_urls: string[]
}

let cached: TatanaDistConfig | null | undefined

function distConfigPath(): string {
  return app.isPackaged
    ? join(process.resourcesPath, 'dist-config', 'tatana-dist.json')
    : join(app.getAppPath(), 'resources', 'dist-config', 'tatana-dist.json')
}

function parse(text: string): TatanaDistConfig | null {
  const raw = JSON.parse(text.replace(/^﻿/, '')) as unknown
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return null
  const r = raw as Record<string, unknown>
  if (r.schema !== 1) return null
  const urls = Array.isArray(r.update_urls) ? r.update_urls.filter((u): u is string => typeof u === 'string') : []
  let clientUrl: string | undefined
  if (typeof r.client_url === 'string' && /^https:\/\/[^\s/]+/i.test(r.client_url)) {
    clientUrl = r.client_url.replace(/\/+$/, '')
  }
  return { schema: 1, client_url: clientUrl, update_urls: urls }
}

/** `null` si no existe o es inválido (se loguea una vez desde el update-manager). */
export function readDistConfig(): TatanaDistConfig | null {
  if (cached !== undefined) return cached
  cached = null
  try {
    const p = distConfigPath()
    if (existsSync(p)) cached = parse(readFileSync(p, 'utf-8'))
  } catch {
    cached = null
  }
  return cached
}
