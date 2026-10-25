#!/usr/bin/env node
// claves-confiables.txt del kit de Tatana en Docker (SDD fiscalia-operacion §6, §10.3): "key_id spkiDerBase64" por
// línea, generado de agent-ui/src/main/updater/trusted-keys.ts con el MISMO lector que usan firmar/verificar
// (readTrustedKeys de _lib.mjs): una sola fuente de las claves públicas.
//
//   node ops/tatana/claves-confiables.mjs [--clave-extra key_id:spki] [--salida claves-confiables.txt]
//   node ops/tatana/claves-confiables.mjs --verificar <claves-confiables.txt>
//
// --clave-extra (solo ensayo de CI y pruebas): suma esa clave. --verificar: sale 1 si el archivo no tiene EXACTAMENTE
// las claves de trusted-keys.ts (lo usa la release de tatana-docker.yml: un kit publicado nunca confía en otra cosa).
import { readFileSync, writeFileSync } from 'node:fs'
import { fail, parseArgs, parseExtraKey, readTrustedKeys } from './_lib.mjs'

const a = parseArgs(process.argv.slice(2), { allowed: ['clave-extra', 'salida', 'verificar'] })
const trusted = readTrustedKeys()

/** Líneas "key_id spki" de un claves-confiables.txt (sin comentarios ni líneas vacías). */
function parse(text) {
  return text
    .replace(/\r/g, '')
    .split('\n')
    .map((l) => l.trim())
    .filter((l) => l && !l.startsWith('#'))
}

if (a.verificar) {
  if (a['clave-extra'] || a.salida) fail('--verificar no va con --clave-extra ni --salida', 2)
  const got = parse(readFileSync(a.verificar, 'utf8')).sort()
  const want = trusted.map((k) => `${k.keyId} ${k.spkiDerBase64}`).sort()
  if (want.length === 0) fail('trusted-keys.ts no tiene claves')
  if (got.length !== want.length || got.some((l, i) => l !== want[i]))
    fail(`${a.verificar} no tiene exactamente las claves de trusted-keys.ts (tiene ${got.length}, se esperaban ${want.length})`)
  console.log(`OK ${a.verificar}: ${want.length} clave(s), las de trusted-keys.ts`)
  process.exit(0)
}

const keys = [...trusted]
if (a['clave-extra']) keys.push(parseExtraKey(a['clave-extra']))
if (keys.length === 0) fail('no hay claves confiables (trusted-keys.ts vacío y sin --clave-extra)')
const text =
  '# Claves públicas Ed25519 en las que confía el actualizador de Tatana en Docker (SDD fiscalia-operacion §10.3).\n' +
  '# Generado de agent-ui/src/main/updater/trusted-keys.ts por ops/tatana/claves-confiables.mjs. No editar.\n' +
  keys.map((k) => `${k.keyId} ${k.spkiDerBase64}`).join('\n') +
  '\n'
if (a.salida) writeFileSync(a.salida, text)
else process.stdout.write(text)
