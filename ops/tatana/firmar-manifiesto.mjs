#!/usr/bin/env node
// Firma el payload y escribe tatana-update.json (SDD tatana-instalador-autoupdate §7, §13.C).
//
//   TATANA_FIRMA_CLAVE_PRIVADA="$(cat clave.pem)" \
//   node ops/tatana/firmar-manifiesto.mjs --payload payload.json --key-id tatana-2026-10 \
//       --salida tatana-update.json [--clave-extra key_id:spkiDerBase64]
//
// - La clave privada se lee SOLO de la variable de entorno TATANA_FIRMA_CLAVE_PRIVADA y nunca se imprime.
// - Se niega a firmar si la pública derivada no está en trusted-keys.ts con ese key_id (una release
//   firmada con otra clave no la aceptaría ninguna PC). --clave-extra solo para el ensayo de CI.
// - Firma los BYTES EXACTOS de --payload (no se re-serializa) y verifica lo escrito.
import { readFileSync, writeFileSync } from 'node:fs'
import {
  KEY_ID, fail, loadPrivateKeyFromEnv, parseArgs, parseExtraKey, payloadError, readTrustedKeys, signEnvelope,
  spkiOf, verifyEnvelopeText,
} from './_lib.mjs'

const a = parseArgs(process.argv.slice(2), { allowed: ['payload', 'key-id', 'salida', 'clave-extra'] })
for (const k of ['payload', 'key-id', 'salida']) if (!a[k]) fail(`falta --${k}`, 2)
const keyId = a['key-id']
if (!KEY_ID.test(keyId)) fail('--key-id inválido', 2)

const privateKey = loadPrivateKeyFromEnv()
const spki = spkiOf(privateKey)
const trusted = readTrustedKeys()
const extra = a['clave-extra'] ? parseExtraKey(a['clave-extra']) : null
const keys = extra ? [...trusted, extra] : trusted
const match = keys.find((k) => k.keyId === keyId)
if (!match) fail(`el key_id "${keyId}" no está en trusted-keys.ts${extra ? ' ni en --clave-extra' : ''}`)
if (match.spkiDerBase64 !== spki) fail(`la clave privada no corresponde a la pública de "${keyId}" en trusted-keys.ts`)

const payloadBytes = readFileSync(a.payload)
let payload
try {
  payload = JSON.parse(payloadBytes.toString('utf8'))
} catch {
  fail(`${a.payload} no es JSON`)
}
const err = payloadError(payload)
if (err) fail(err)

const envelope = signEnvelope(payloadBytes, keyId, privateKey)
const check = verifyEnvelopeText(envelope, keys)
if (!check.ok) fail(`el sobre recién firmado no verifica (${check.reason}: ${check.detail})`)
writeFileSync(a.salida, envelope)
console.log(`OK ${a.salida}: Tatana ${payload.version} firmado con "${keyId}"`)
