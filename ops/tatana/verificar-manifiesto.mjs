#!/usr/bin/env node
// Verifica tatana-update.json (Windows) o tatana-update-linux.json (Linux) contra las claves de
// agent-ui/src/main/updater/trusted-keys.ts, igual que Tatana en la PC del perito (SDD tatana-instalador-autoupdate
// §7; tatana-linux §10.C). Lo usan CI (antes de publicar) y soporte:
//
//   node ops/tatana/verificar-manifiesto.mjs --manifiesto tatana-update.json [--producto tatana-windows|tatana-linux] \
//       [--clave-extra key_id:spki] [--version X.Y.Z] [--archivo Tatana-Setup-X.Y.Z.exe] [--payload payload.json]
//
// --producto (default tatana-windows): un manifiesto de otro producto se rechaza (manifiesto_invalido).
// --archivo (alias --exe): además compara tamaño, sha512 y sha256 del instalador o del AppImage. --payload: los
// bytes firmados tienen que ser exactamente los de ese archivo. Sale 0 si todo verifica; 1 si no (con el motivo).
//
// Tatana en Docker (SDD fiscalia-operacion §5.2): --producto tatana-docker, --archivo = la imagen (.tar.gz) y
// --kit = kit-tatana-docker-vX.Y.Z.tar.gz (compara nombre, tamaño y SHA-256 con payload.kit).
import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { basename } from 'node:path'
import { fail, parseArgs, parseExtraKey, productArg, readTrustedKeys, verifyEnvelopeText } from './_lib.mjs'

const a = parseArgs(process.argv.slice(2), {
  allowed: ['manifiesto', 'producto', 'clave-extra', 'version', 'archivo', 'exe', 'payload', 'kit'],
})
if (!a.manifiesto) fail('falta --manifiesto', 2)
if (a.archivo && a.exe && a.archivo !== a.exe) fail('--archivo y --exe apuntan a archivos distintos', 2)
a.archivo ??= a.exe
const product = productArg(a.producto)
if (a.kit && product !== 'tatana-docker') fail('--kit es solo de --producto tatana-docker', 2)
const keys = readTrustedKeys()
if (a['clave-extra']) keys.push(parseExtraKey(a['clave-extra']))
if (keys.length === 0) fail('no hay claves confiables (trusted-keys.ts vacío y sin --clave-extra)')

const r = verifyEnvelopeText(readFileSync(a.manifiesto, 'utf8'), keys, product)
if (!r.ok) fail(`${r.reason}: ${r.detail}`)
const p = r.payload
if (a.version && p.version !== a.version) fail(`el manifiesto es de ${p.version} y se esperaba ${a.version}`)
if (a.payload && !readFileSync(a.payload).equals(r.payloadBytes)) fail('los bytes firmados no son los de --payload')
if (a.archivo) {
  const exe = readFileSync(a.archivo)
  if (basename(a.archivo) !== p.file) fail(`el archivo se llama ${basename(a.archivo)} y el manifiesto dice ${p.file}`)
  if (exe.length !== p.size) fail(`tamaño ${exe.length} != ${p.size}`)
  if (createHash('sha512').update(exe).digest('base64') !== p.sha512) fail('sha512 del archivo no coincide')
  if (createHash('sha256').update(exe).digest('hex') !== p.sha256) fail('sha256 del archivo no coincide')
}
if (a.kit) {
  const kit = readFileSync(a.kit)
  if (basename(a.kit) !== p.kit.file) fail(`el kit se llama ${basename(a.kit)} y el manifiesto dice ${p.kit.file}`)
  if (kit.length !== p.kit.size) fail(`tamaño del kit ${kit.length} != ${p.kit.size}`)
  if (createHash('sha256').update(kit).digest('hex') !== p.kit.sha256) fail('sha256 del kit no coincide')
}
console.log(`OK firma válida (${r.keyId}): Tatana ${p.version} (${p.product}), ${p.file}, ${p.size} bytes, commit ${p.commit.slice(0, 12)}${p.image ? `, imagen ${p.image}` : ''}`)
