// Funciones comunes de las herramientas de firma de Tatana (SDD tatana-instalador-autoupdate §7, §13.C).
// Node >= 22, sin dependencias. El formato del sobre y las reglas del payload son las mismas que
// agent-ui/src/main/updater/manifest.ts (lo que verifica Tatana en la PC del perito).
import { createPrivateKey, createPublicKey, sign, verify } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

export const REPO = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..')
export const TRUSTED_KEYS_FILE = join(REPO, 'agent-ui', 'src', 'main', 'updater', 'trusted-keys.ts')
export const KEY_ID = /^[a-z0-9][a-z0-9._-]{0,63}$/
export const RELEASE_VERSION = /^\d+\.\d+\.\d+$/
const BASE64 = /^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/

export function fail(msg, code = 1) {
  console.error(`ERROR: ${msg}`)
  process.exit(code)
}

/** --clave valor → { clave: valor }. Los flags sin valor (booleanos) se declaran en `flags`. */
export function parseArgs(argv, { allowed, flags = [] }) {
  const out = {}
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i]
    if (!a.startsWith('--')) fail(`argumento inesperado: ${a}`, 2)
    const name = a.slice(2)
    if (flags.includes(name)) {
      out[name] = true
      continue
    }
    if (!allowed.includes(name)) fail(`opción desconocida: ${a}`, 2)
    const v = argv[i + 1]
    if (v === undefined || v.startsWith('--')) fail(`falta el valor de ${a}`, 2)
    out[name] = v
    i++
  }
  return out
}

/** Base64 canónico (Buffer.from acepta basura): null si no lo es. */
export function decodeBase64Strict(s) {
  if (typeof s !== 'string' || s.length === 0 || !BASE64.test(s)) return null
  const buf = Buffer.from(s, 'base64')
  return buf.toString('base64') === s ? buf : null
}

/** SPKI DER base64 de una clave pública o privada Ed25519. */
export function spkiOf(key) {
  const pub = key.type === 'private' ? createPublicKey(key) : key
  if (pub.asymmetricKeyType !== 'ed25519') fail('la clave no es Ed25519')
  return pub.export({ type: 'spki', format: 'der' }).toString('base64')
}

export function loadPrivateKeyFromEnv(name = 'TATANA_FIRMA_CLAVE_PRIVADA') {
  const pem = process.env[name]
  if (!pem || !pem.trim()) fail(`falta la variable de entorno ${name} (PEM PKCS#8 de la clave Ed25519)`)
  let key
  try {
    key = createPrivateKey({ key: pem, format: 'pem' })
  } catch {
    fail(`${name} no es una clave privada PEM válida`)
  }
  if (key.asymmetricKeyType !== 'ed25519') fail(`${name} no es una clave Ed25519`)
  return key
}

/**
 * Claves confiables de agent-ui/src/main/updater/trusted-keys.ts: el contenido del array
 * TRUSTED_UPDATE_KEYS, sin comentarios, con entradas { keyId: '…', spkiDerBase64: '…' }.
 */
export function readTrustedKeys(file = TRUSTED_KEYS_FILE) {
  let src
  try {
    src = readFileSync(file, 'utf8')
  } catch {
    fail(`no se pudo leer ${file}`)
  }
  const start = src.search(/TRUSTED_UPDATE_KEYS\b[^=]*=\s*\[/)
  if (start < 0) fail(`no se encontró TRUSTED_UPDATE_KEYS en ${file}`)
  const open = src.indexOf('[', src.indexOf('=', start))
  const close = src.indexOf(']', open)
  if (close < 0) fail(`TRUSTED_UPDATE_KEYS mal formado en ${file}`)
  const body = src
    .slice(open + 1, close)
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .split('\n')
    .map((l) => l.replace(/\/\/.*$/, ''))
    .join('\n')
  const keys = []
  const re = /\{\s*keyId:\s*(['"])([^'"]+)\1\s*,\s*spkiDerBase64:\s*(['"])([A-Za-z0-9+/=]+)\3\s*,?\s*\}/g
  for (const m of body.matchAll(re)) keys.push({ keyId: m[2], spkiDerBase64: m[4] })
  return keys
}

/** "key_id:spkiDerBase64" (TATANA_EXTRA_UPDATE_KEY / --clave-extra). */
export function parseExtraKey(raw) {
  const s = (raw ?? '').trim()
  const i = s.indexOf(':')
  if (i <= 0 || i === s.length - 1) fail(`--clave-extra tiene que ser key_id:spkiDerBase64`)
  const keyId = s.slice(0, i)
  const spkiDerBase64 = s.slice(i + 1)
  if (!KEY_ID.test(keyId) || !decodeBase64Strict(spkiDerBase64)) fail('--clave-extra mal formada')
  return { keyId, spkiDerBase64 }
}

/** Reglas del payload (§13.C). Devuelve un texto de error o null. */
export function payloadError(p) {
  if (typeof p !== 'object' || p === null || Array.isArray(p)) return 'el payload no es un objeto JSON'
  if (p.schema !== 1) return 'payload.schema tiene que ser 1'
  if (p.product !== 'tatana-windows') return 'payload.product tiene que ser "tatana-windows"'
  if (typeof p.version !== 'string' || !RELEASE_VERSION.test(p.version)) return 'payload.version tiene que ser X.Y.Z'
  if (p.file !== `Tatana-Setup-${p.version}.exe`) return `payload.file tiene que ser Tatana-Setup-${p.version}.exe`
  if (!Number.isSafeInteger(p.size) || p.size <= 0) return 'payload.size tiene que ser un entero > 0'
  if (decodeBase64Strict(p.sha512)?.length !== 64) return 'payload.sha512 tiene que ser base64 de 64 bytes'
  if (typeof p.sha256 !== 'string' || !/^[0-9a-f]{64}$/.test(p.sha256)) return 'payload.sha256 tiene que ser hex en minúsculas'
  if (typeof p.released_at !== 'string' || Number.isNaN(Date.parse(p.released_at))) return 'payload.released_at tiene que ser ISO-8601'
  if (typeof p.commit !== 'string' || !/^[0-9a-f]{40}$/.test(p.commit)) return 'payload.commit tiene que ser un SHA de 40 hex'
  if (p.notes !== undefined && p.notes !== null && typeof p.notes !== 'string') return 'payload.notes tiene que ser texto'
  return null
}

/** Firma los bytes EXACTOS del payload; devuelve el texto del sobre. */
export function signEnvelope(payloadBytes, keyId, privateKey) {
  const signature = sign(null, payloadBytes, privateKey)
  return (
    JSON.stringify({
      schema: 1,
      key_id: keyId,
      payload: payloadBytes.toString('base64'),
      signature: signature.toString('base64'),
    }) + '\n'
  )
}

/** Misma lógica que verifyEnvelope de manifest.ts: { ok, payload } | { ok: false, reason, detail }. */
export function verifyEnvelopeText(text, keys) {
  let env
  try {
    env = JSON.parse(text.replace(/^﻿/, ''))
  } catch {
    return { ok: false, reason: 'manifiesto_invalido', detail: 'JSON roto' }
  }
  if (typeof env !== 'object' || env === null || env.schema !== 1 || typeof env.key_id !== 'string' ||
      typeof env.payload !== 'string' || typeof env.signature !== 'string')
    return { ok: false, reason: 'manifiesto_invalido', detail: 'el sobre no tiene schema/key_id/payload/signature' }
  const key = keys.find((k) => k.keyId === env.key_id)
  if (!key) return { ok: false, reason: 'clave_desconocida', detail: `key_id "${env.key_id}" no es de confianza` }
  const payloadBytes = decodeBase64Strict(env.payload)
  const signature = decodeBase64Strict(env.signature)
  if (!payloadBytes) return { ok: false, reason: 'manifiesto_invalido', detail: 'payload no es base64' }
  if (!signature || signature.length !== 64) return { ok: false, reason: 'firma_invalida', detail: 'firma mal formada' }
  let valid = false
  try {
    const pub = createPublicKey({ key: Buffer.from(key.spkiDerBase64, 'base64'), format: 'der', type: 'spki' })
    valid = pub.asymmetricKeyType === 'ed25519' && verify(null, payloadBytes, pub, signature)
  } catch {
    return { ok: false, reason: 'clave_desconocida', detail: 'la clave confiable está corrupta' }
  }
  if (!valid) return { ok: false, reason: 'firma_invalida', detail: 'la firma no coincide' }
  let payload
  try {
    payload = JSON.parse(payloadBytes.toString('utf8'))
  } catch {
    return { ok: false, reason: 'manifiesto_invalido', detail: 'el payload no es JSON' }
  }
  const err = payloadError(payload)
  if (err) return { ok: false, reason: 'manifiesto_invalido', detail: err }
  return { ok: true, keyId: env.key_id, payload, payloadBytes }
}
