/**
 * Manifiesto firmado de actualización de Tatana (`tatana-update.json`).
 * SDD tatana-instalador-autoupdate §7 y §13.C (D7, D-T6, D-T7, D-T8).
 *
 * PURO: solo depende de `node:crypto` y `semver` (lo testea `manifest.test.ts`
 * con `tsx --test`, sin Electron). Nada de red ni de disco acá.
 *
 * Sobre:   { schema: 1, key_id, payload: base64(bytes UTF-8 del payload), signature: base64(64 bytes) }
 * Payload: { schema: 1, product: "tatana-windows", version, released_at, commit, file, size, sha512, sha256, notes? }
 *
 * Se firma con Ed25519 los BYTES EXACTOS del payload que viajan en base64: no
 * se re-serializa nada, así no hay ambigüedad de canonicalización.
 */

import { createPublicKey, verify as cryptoVerify } from 'node:crypto'
import semver from 'semver'

export interface TrustedKey {
  keyId: string
  spkiDerBase64: string
}

export interface UpdateEnvelope {
  schema: 1
  key_id: string
  payload: string
  signature: string
}

export interface UpdatePayload {
  schema: 1
  product: 'tatana-windows'
  version: string
  released_at: string
  commit: string
  file: string
  size: number
  /** base64, idéntico al de `latest.yml`. */
  sha512: string
  /** hex */
  sha256: string
  notes?: string
}

export type VerifyFailure = 'firma_invalida' | 'clave_desconocida' | 'manifiesto_invalido'

export type VerifyResult =
  | { ok: true; payload: UpdatePayload }
  | { ok: false; reason: VerifyFailure }

export type Decision = 'update' | 'up_to_date' | 'refused_downgrade'

/** Lo mínimo que se lee del `UpdateInfo` de electron-updater (no se importa el tipo: queda puro). */
export interface UpdateInfoLike {
  version: string
  files?: ReadonlyArray<{ url: string; sha512: string; size?: number }>
  path?: string
  sha512?: string
}

/** Versión de release aceptada: `X.Y.Z`, sin prerelease ni build (D-T1). */
const RELEASE_VERSION = /^\d+\.\d+\.\d+$/
const BASE64 = /^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/
const MAX_ENVELOPE_CHARS = 64 * 1024

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === 'object' && v !== null && !Array.isArray(v)
}

/** Base64 estricto (Buffer.from es permisivo con basura): devuelve null si no es canónico. */
function decodeBase64Strict(s: string): Buffer | null {
  if (s.length === 0 || !BASE64.test(s)) return null
  const buf = Buffer.from(s, 'base64')
  return buf.toString('base64') === s ? buf : null
}

/** Parsea el sobre. `null` = JSON roto o forma incorrecta (`manifiesto_invalido`). */
export function parseEnvelope(text: string): UpdateEnvelope | null {
  if (typeof text !== 'string' || text.length === 0 || text.length > MAX_ENVELOPE_CHARS) return null
  let raw: unknown
  try {
    raw = JSON.parse(text.replace(/^﻿/, ''))
  } catch {
    return null
  }
  if (!isRecord(raw)) return null
  if (raw.schema !== 1) return null
  if (typeof raw.key_id !== 'string' || raw.key_id.length === 0) return null
  if (typeof raw.payload !== 'string' || typeof raw.signature !== 'string') return null
  return { schema: 1, key_id: raw.key_id, payload: raw.payload, signature: raw.signature }
}

function validatePayload(raw: unknown): UpdatePayload | null {
  if (!isRecord(raw)) return null
  if (raw.schema !== 1 || raw.product !== 'tatana-windows') return null
  const { version, file, size, sha512, sha256, released_at, commit, notes } = raw
  if (typeof version !== 'string' || !RELEASE_VERSION.test(version) || !semver.valid(version)) return null
  if (file !== `Tatana-Setup-${version}.exe`) return null
  if (typeof size !== 'number' || !Number.isSafeInteger(size) || size <= 0) return null
  if (typeof sha512 !== 'string' || decodeBase64Strict(sha512)?.length !== 64) return null
  if (typeof sha256 !== 'string' || !/^[0-9a-f]{64}$/i.test(sha256)) return null
  if (typeof released_at !== 'string' || typeof commit !== 'string') return null
  if (notes !== undefined && notes !== null && typeof notes !== 'string') return null
  return {
    schema: 1,
    product: 'tatana-windows',
    version,
    released_at,
    commit,
    file,
    size,
    sha512,
    sha256: sha256.toLowerCase(),
    ...(typeof notes === 'string' ? { notes } : {}),
  }
}

/**
 * Verifica la firma Ed25519 del sobre contra las claves confiables (por
 * `key_id`) y valida el payload (§13.C). Nunca tira: un error es un `reason`.
 */
export function verifyEnvelope(env: UpdateEnvelope, keys: ReadonlyArray<TrustedKey>): VerifyResult {
  const key = keys.find((k) => k.keyId === env.key_id)
  if (!key) return { ok: false, reason: 'clave_desconocida' }

  const payloadBytes = decodeBase64Strict(env.payload)
  const signature = decodeBase64Strict(env.signature)
  if (!payloadBytes || payloadBytes.length === 0) return { ok: false, reason: 'manifiesto_invalido' }
  if (!signature || signature.length !== 64) return { ok: false, reason: 'firma_invalida' }

  let valid = false
  try {
    const publicKey = createPublicKey({
      key: Buffer.from(key.spkiDerBase64, 'base64'),
      format: 'der',
      type: 'spki',
    })
    if (publicKey.asymmetricKeyType !== 'ed25519') return { ok: false, reason: 'clave_desconocida' }
    valid = cryptoVerify(null, payloadBytes, publicKey, signature)
  } catch {
    // Clave horneada corrupta: se trata como desconocida (no se puede confiar en nada firmado con ella).
    return { ok: false, reason: 'clave_desconocida' }
  }
  if (!valid) return { ok: false, reason: 'firma_invalida' }

  let parsed: unknown
  try {
    parsed = JSON.parse(payloadBytes.toString('utf8'))
  } catch {
    return { ok: false, reason: 'manifiesto_invalido' }
  }
  const payload = validatePayload(parsed)
  return payload ? { ok: true, payload } : { ok: false, reason: 'manifiesto_invalido' }
}

/**
 * Solo se acepta una versión MAYOR que la instalada (D-T7: sin downgrade y sin
 * "repetir"). Si la instalada no es semver (no debería pasar), no se actualiza.
 */
export function decide(payload: Pick<UpdatePayload, 'version'>, currentVersion: string): Decision {
  const current = semver.valid(currentVersion)
  if (!current) return 'refused_downgrade'
  if (semver.gt(payload.version, current)) return 'update'
  if (semver.eq(payload.version, current)) return 'up_to_date'
  return 'refused_downgrade'
}

/**
 * `null` si el `UpdateInfo` que obtuvo electron-updater de `latest.yml`
 * coincide EXACTAMENTE con el manifiesto firmado (versión, archivo y sha512;
 * tamaño si viene). Si no, el motivo (D-T6).
 */
export function matchesUpdateInfo(
  payload: UpdatePayload,
  info: UpdateInfoLike | null | undefined,
): 'no_coincide_con_manifiesto' | null {
  if (!info || info.version !== payload.version) return 'no_coincide_con_manifiesto'
  const files = info.files ?? []
  if (files.length !== 1) return 'no_coincide_con_manifiesto'
  const [f] = files
  if (f.url !== payload.file || f.sha512 !== payload.sha512) return 'no_coincide_con_manifiesto'
  if (typeof f.size === 'number' && f.size !== payload.size) return 'no_coincide_con_manifiesto'
  if (info.path !== undefined && info.path !== payload.file) return 'no_coincide_con_manifiesto'
  if (info.sha512 !== undefined && info.sha512 !== payload.sha512) return 'no_coincide_con_manifiesto'
  return null
}

function normalizeUrl(raw: string, allowLoopbackHttp: boolean): string | null {
  let u: URL
  try {
    u = new URL(raw.trim())
  } catch {
    return null
  }
  if (u.username || u.password || u.search || u.hash) return null
  const loopback = u.hostname === '127.0.0.1' || u.hostname === 'localhost'
  if (u.protocol !== 'https:' && !(allowLoopbackHttp && u.protocol === 'http:' && loopback)) return null
  if (!u.pathname.endsWith('/')) u.pathname += '/'
  return u.toString()
}

/**
 * URLs del canal, en orden y sin repetir (D-T8). Las horneadas tienen que ser
 * `https://`. El override de CI (`TATANA_UPDATE_URLS`, separado por comas)
 * además acepta `http://127.0.0.1` / `http://localhost` (cualquier puerto): no
 * es un riesgo porque una URL ajena no puede servir una versión con firma
 * válida. Si el override no deja ninguna URL válida, se ignora.
 */
export function sanitizeUpdateUrls(baked: ReadonlyArray<string>, envOverride?: string | null): string[] {
  const dedupe = (list: Array<string | null>): string[] => [...new Set(list.filter((u): u is string => !!u))]
  if (envOverride && envOverride.trim()) {
    const fromEnv = dedupe(envOverride.split(',').filter((s) => s.trim()).map((s) => normalizeUrl(s, true)))
    if (fromEnv.length > 0) return fromEnv
  }
  return dedupe(baked.map((s) => (typeof s === 'string' ? normalizeUrl(s, false) : null)))
}
