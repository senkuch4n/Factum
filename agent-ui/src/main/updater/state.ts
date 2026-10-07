/**
 * Estado del actualizador (SDD §13.F) y su persistencia en
 * `<userData>/update-state.json`. camelCase porque es interno de TS; la prueba
 * de CI lee `phase`, `availableVersion`, `reason` y `busyOperations`.
 */
import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from 'fs'
import { dirname } from 'path'

export type UpdatePhase =
  | 'disabled' | 'idle' | 'checking' | 'downloading' | 'ready' | 'installing'
  | 'up_to_date' | 'offline' | 'rejected' | 'error'

export type UpdateReason =
  | 'firma_invalida' | 'clave_desconocida' | 'manifiesto_invalido' | 'no_coincide_con_manifiesto'
  | 'hash_no_coincide' | 'sin_canal' | 'sin_claves' | 'no_empaquetado' | 'instalacion_fallida'

export interface UpdateState {
  phase: UpdatePhase
  currentVersion: string
  availableVersion?: string
  percent?: number
  sourceUrl?: string
  /** ISO */
  checkedAt?: string
  verifiedAt?: string
  reason?: UpdateReason
  /** `kind`s de `/agent/state.operations` (sin repetir). */
  busyOperations?: string[]
  notifiedVersion?: string
}

const PHASES: ReadonlySet<string> = new Set([
  'disabled', 'idle', 'checking', 'downloading', 'ready', 'installing',
  'up_to_date', 'offline', 'rejected', 'error',
])

/** Lee el estado persistido; `null` si no hay o está roto. */
export function loadUpdateState(path: string): UpdateState | null {
  try {
    if (!existsSync(path)) return null
    const raw = JSON.parse(readFileSync(path, 'utf-8').replace(/^﻿/, '')) as Partial<UpdateState>
    if (!raw || typeof raw !== 'object' || typeof raw.phase !== 'string' || !PHASES.has(raw.phase)) return null
    return { ...raw, currentVersion: String(raw.currentVersion ?? '') } as UpdateState
  } catch {
    return null
  }
}

/** Escritura atómica: tmp + rename (la prueba de CI lo lee mientras corre). */
export function saveUpdateState(path: string, state: UpdateState): void {
  try {
    mkdirSync(dirname(path), { recursive: true })
    const tmp = `${path}.tmp`
    writeFileSync(tmp, JSON.stringify(state, null, 2), 'utf-8')
    renameSync(tmp, path)
  } catch {
    /* sin disco no se rompe la app: el estado vive en memoria */
  }
}

/** Texto de la UI para cada `kind` de operación en curso (SDD §13.F). */
export function busyOperationText(kind: string): string {
  switch (kind) {
    case 'recording_android':
    case 'recording_ios':
      return 'grabación en curso'
    case 'airplay_session':
      return 'sesión AirPlay en curso'
    case 'video_postprocess':
      return 'procesando un video'
    default:
      return 'operación en curso'
  }
}

/** "grabación en curso, procesando un video" (sin repetir textos). */
export function busyOperationsText(kinds: ReadonlyArray<string> | undefined): string {
  return [...new Set((kinds ?? []).map(busyOperationText))].join(', ')
}
