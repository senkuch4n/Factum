#!/usr/bin/env node
// Genera la clave Ed25519 que firma tatana-update.json (SDD tatana-instalador-autoupdate §7, §18.1).
//
//   node ops/tatana/generar-clave-firma.mjs --key-id tatana-2026-10 --salida ~/Seguro/tatana-firma.pem
//
// - Escribe la PRIVADA (PEM PKCS#8) en --salida con modo 0600. Se niega si --salida cae dentro de un
//   repo git (este u otro) o si el archivo ya existe: la privada NUNCA va a un repo.
// - Imprime la línea PÚBLICA para agent-ui/src/main/updater/trusted-keys.ts y el comando para cargar
//   la privada como secret del environment tatana-release.
// - --extra-salida <archivo>: además escribe "key_id:spkiDerBase64" (lo usa el ensayo de CI para
//   TATANA_EXTRA_UPDATE_KEY con una clave efímera; no es para producción).
import { execFileSync } from 'node:child_process'
import { generateKeyPairSync } from 'node:crypto'
import { existsSync, realpathSync, writeFileSync } from 'node:fs'
import { homedir } from 'node:os'
import { dirname, resolve, sep } from 'node:path'
import { KEY_ID, REPO, fail, parseArgs, spkiOf } from './_lib.mjs'

const args = parseArgs(process.argv.slice(2), { allowed: ['key-id', 'salida', 'extra-salida'] })
const keyId = args['key-id']
if (!keyId || !args.salida) fail('uso: generar-clave-firma.mjs --key-id <id> --salida <archivo.pem> [--extra-salida <archivo>]', 2)
if (!KEY_ID.test(keyId)) fail('--key-id: minúsculas, dígitos, ".", "_" o "-" (hasta 64), p. ej. tatana-2026-10', 2)

const expand = (p) => (p === '~' || p.startsWith('~/') ? homedir() + p.slice(1) : p)
const salida = resolve(expand(args.salida))
const dir = dirname(salida)
if (!existsSync(dir)) fail(`no existe la carpeta ${dir}`)
if (existsSync(salida)) fail(`${salida} ya existe: no se pisa una clave (borrala a mano si de verdad querés otra)`)

// ¿Cae dentro de un repo git? Este repo, o cualquier otro (git rev-parse desde la carpeta destino).
const realDir = realpathSync(dir)
const realRepo = realpathSync(REPO)
let otroRepo = ''
try {
  otroRepo = execFileSync('git', ['rev-parse', '--show-toplevel'], { cwd: realDir, stdio: ['ignore', 'pipe', 'ignore'] })
    .toString()
    .trim()
} catch {
  /* no es un repo git, o no hay git: bien */
}
if (realDir === realRepo || realDir.startsWith(realRepo + sep) || otroRepo)
  fail(`--salida cae dentro de un repo git (${otroRepo || realRepo}). La clave privada va fuera de cualquier repo.`)

const { privateKey } = generateKeyPairSync('ed25519')
const pem = privateKey.export({ type: 'pkcs8', format: 'pem' })
writeFileSync(salida, pem, { mode: 0o600, flag: 'wx' })
const spki = spkiOf(privateKey)
if (args['extra-salida']) writeFileSync(resolve(expand(args['extra-salida'])), `${keyId}:${spki}\n`, { flag: 'wx' })

console.log(`Clave privada: ${salida} (0600). Guardá DOS copias fuera de GitHub (gestor de contraseñas + medio offline).

1) Pegá esta línea dentro de TRUSTED_UPDATE_KEYS en agent-ui/src/main/updater/trusted-keys.ts:

  { keyId: '${keyId}', spkiDerBase64: '${spki}' },

2) Cargá la privada como secret del environment tatana-release (no se imprime):

  gh secret set TATANA_FIRMA_CLAVE_PRIVADA --env tatana-release < ${salida}

3) Variable del repo con el key_id vigente:

  gh variable set TATANA_FIRMA_KEY_ID --body ${keyId}
`)
