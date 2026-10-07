#!/usr/bin/env node
// Verifica tatana-update.json contra las claves de agent-ui/src/main/updater/trusted-keys.ts, igual que
// Tatana en la PC del perito (SDD tatana-instalador-autoupdate §7). Lo usan CI (antes de publicar) y
// soporte:
//
//   node ops/tatana/verificar-manifiesto.mjs --manifiesto tatana-update.json \
//       [--clave-extra key_id:spki] [--version X.Y.Z] [--exe Tatana-Setup-X.Y.Z.exe] [--payload payload.json]
//
// --exe: además compara tamaño, sha512 y sha256 del instalador. --payload: los bytes firmados tienen que
// ser exactamente los de ese archivo. Sale 0 si todo verifica; 1 si no (con el motivo).
import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { basename } from 'node:path'
import { fail, parseArgs, parseExtraKey, readTrustedKeys, verifyEnvelopeText } from './_lib.mjs'

const a = parseArgs(process.argv.slice(2), { allowed: ['manifiesto', 'clave-extra', 'version', 'exe', 'payload'] })
if (!a.manifiesto) fail('falta --manifiesto', 2)
const keys = readTrustedKeys()
if (a['clave-extra']) keys.push(parseExtraKey(a['clave-extra']))
if (keys.length === 0) fail('no hay claves confiables (trusted-keys.ts vacío y sin --clave-extra)')

const r = verifyEnvelopeText(readFileSync(a.manifiesto, 'utf8'), keys)
if (!r.ok) fail(`${r.reason}: ${r.detail}`)
const p = r.payload
if (a.version && p.version !== a.version) fail(`el manifiesto es de ${p.version} y se esperaba ${a.version}`)
if (a.payload && !readFileSync(a.payload).equals(r.payloadBytes)) fail('los bytes firmados no son los de --payload')
if (a.exe) {
  const exe = readFileSync(a.exe)
  if (basename(a.exe) !== p.file) fail(`el exe se llama ${basename(a.exe)} y el manifiesto dice ${p.file}`)
  if (exe.length !== p.size) fail(`tamaño ${exe.length} != ${p.size}`)
  if (createHash('sha512').update(exe).digest('base64') !== p.sha512) fail('sha512 del exe no coincide')
  if (createHash('sha256').update(exe).digest('hex') !== p.sha256) fail('sha256 del exe no coincide')
}
console.log(`OK firma válida (${r.keyId}): Tatana ${p.version}, ${p.file}, ${p.size} bytes, commit ${p.commit.slice(0, 12)}`)
