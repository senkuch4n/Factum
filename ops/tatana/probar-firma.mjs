#!/usr/bin/env node
// Ida y vuelta de las herramientas de firma (SDD tatana-instalador-autoupdate B30), en una carpeta
// temporal propia que se borra al final. Sin red y sin secretos: genera claves efímeras.
//
//   node ops/tatana/probar-firma.mjs
//
// generar → armar payload → firmar → verificar ok → alterar un byte → verificar falla, y los rechazos
// (clave que no es de confianza, key_id desconocido, --salida dentro del repo, exe distinto).
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { mkdtempSync, readFileSync, rmSync, writeFileSync, existsSync, statSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { REPO } from './_lib.mjs'

const T = mkdtempSync(join(tmpdir(), 'tatana-firma-'))
const tool = (n) => join(REPO, 'ops', 'tatana', n)
let fallos = 0
const ok = (m) => console.log(`ok   ${m}`)
const mal = (m) => { console.log(`FALLA ${m}`); fallos++ }
function run(script, args, env = {}) {
  const r = spawnSync(process.execPath, [tool(script), ...args], {
    env: { ...process.env, TATANA_FIRMA_CLAVE_PRIVADA: '', ...env },
    encoding: 'utf8',
  })
  return { code: r.status, out: (r.stdout ?? '') + (r.stderr ?? '') }
}
const expect = (desc, r, code, re) => {
  if (r.code === code && (!re || re.test(r.out))) ok(`${desc} (${code})`)
  else mal(`${desc}: esperaba ${code}${re ? ` y ${re}` : ''}, dio ${r.code}: ${r.out.trim()}`)
}

try {
  // 1. Generar dos claves efímeras.
  const pem = join(T, 'clave.pem')
  const extraFile = join(T, 'extra.txt')
  expect('generar clave', run('generar-clave-firma.mjs', ['--key-id', 'prueba-1', '--salida', pem, '--extra-salida', extraFile]), 0, /spkiDerBase64: '/)
  if ((statSync(pem).mode & 0o777) === 0o600 || process.platform === 'win32') ok('PEM con modo 0600')
  else mal(`PEM con modo ${(statSync(pem).mode & 0o777).toString(8)}`)
  expect('no pisa una clave existente', run('generar-clave-firma.mjs', ['--key-id', 'prueba-1', '--salida', pem]), 1, /ya existe/)
  const enRepo = join(REPO, 'ops', 'tatana', 'NO-DEBERIA-EXISTIR.pem')
  expect('se niega dentro del repo', run('generar-clave-firma.mjs', ['--key-id', 'x', '--salida', enRepo]), 1, /repo git/)
  if (existsSync(enRepo)) { mal('escribió la clave dentro del repo'); rmSync(enRepo) }
  const pem2 = join(T, 'otra.pem')
  const extra2File = join(T, 'extra2.txt')
  expect('generar otra clave', run('generar-clave-firma.mjs', ['--key-id', 'prueba-2', '--salida', pem2, '--extra-salida', extra2File]), 0)
  const extra = readFileSync(extraFile, 'utf8').trim()
  const extra2 = readFileSync(extra2File, 'utf8').trim()
  const priv = readFileSync(pem, 'utf8')

  // 2. Instalador y latest.yml falsos, como los deja electron-builder.
  const exe = join(T, 'Tatana-Setup-1.4.0.exe')
  writeFileSync(exe, Buffer.from('MZ' + 'x'.repeat(100000)))
  const bytes = readFileSync(exe)
  const sha512 = createHash('sha512').update(bytes).digest('base64')
  const latest = join(T, 'latest.yml')
  writeFileSync(latest, `version: 1.4.0\nfiles:\n  - url: Tatana-Setup-1.4.0.exe\n    sha512: ${sha512}\n    size: ${bytes.length}\npath: Tatana-Setup-1.4.0.exe\nsha512: ${sha512}\nreleaseDate: '2026-10-07T15:00:00.000Z'\n`)
  const payload = join(T, 'payload.json')
  const commit = 'a'.repeat(40)
  expect('armar payload', run('armar-payload.mjs', ['--exe', exe, '--latest', latest, '--version', '1.4.0', '--commit', commit, '--salida', payload, '--notas', 'prueba']), 0)
  expect('armar payload con otra versión', run('armar-payload.mjs', ['--exe', exe, '--latest', latest, '--version', '1.4.1', '--commit', commit, '--salida', join(T, 'p2.json')]), 1, /version/)

  // 3. Firmar.
  const man = join(T, 'tatana-update.json')
  expect('firmar sin clave', run('firmar-manifiesto.mjs', ['--payload', payload, '--key-id', 'prueba-1', '--salida', man]), 1, /TATANA_FIRMA_CLAVE_PRIVADA/)
  expect('firmar con key_id que no es de confianza', run('firmar-manifiesto.mjs', ['--payload', payload, '--key-id', 'prueba-1', '--salida', man], { TATANA_FIRMA_CLAVE_PRIVADA: priv }), 1, /trusted-keys/)
  expect('firmar con la clave equivocada', run('firmar-manifiesto.mjs', ['--payload', payload, '--key-id', 'prueba-2', '--salida', man, '--clave-extra', extra2], { TATANA_FIRMA_CLAVE_PRIVADA: priv }), 1, /no corresponde/)
  const r = run('firmar-manifiesto.mjs', ['--payload', payload, '--key-id', 'prueba-1', '--salida', man, '--clave-extra', extra], { TATANA_FIRMA_CLAVE_PRIVADA: priv })
  expect('firmar', r, 0)
  if (r.out.includes('PRIVATE KEY')) mal('la salida de firmar muestra la clave')

  // 4. Verificar.
  expect('verificar ok', run('verificar-manifiesto.mjs', ['--manifiesto', man, '--clave-extra', extra, '--version', '1.4.0', '--exe', exe, '--payload', payload]), 0, /firma válida/)
  expect('verificar sin la clave', run('verificar-manifiesto.mjs', ['--manifiesto', man]), 1)
  expect('verificar con otra clave', run('verificar-manifiesto.mjs', ['--manifiesto', man, '--clave-extra', extra2]), 1, /clave_desconocida/)
  expect('verificar otra versión', run('verificar-manifiesto.mjs', ['--manifiesto', man, '--clave-extra', extra, '--version', '1.4.1']), 1)

  // 5. Alterar un byte del payload firmado → firma inválida.
  const env = JSON.parse(readFileSync(man, 'utf8'))
  const p = Buffer.from(env.payload, 'base64')
  p[p.indexOf('1.4.0')] = '9'.charCodeAt(0)
  const alterado = join(T, 'alterado.json')
  writeFileSync(alterado, JSON.stringify({ ...env, payload: p.toString('base64') }))
  expect('payload alterado', run('verificar-manifiesto.mjs', ['--manifiesto', alterado, '--clave-extra', extra]), 1, /firma_invalida/)
  const sig = Buffer.from(env.signature, 'base64')
  sig[0] ^= 1
  writeFileSync(alterado, JSON.stringify({ ...env, signature: sig.toString('base64') }))
  expect('firma alterada', run('verificar-manifiesto.mjs', ['--manifiesto', alterado, '--clave-extra', extra]), 1, /firma_invalida/)
  writeFileSync(alterado, '{ roto')
  expect('JSON roto', run('verificar-manifiesto.mjs', ['--manifiesto', alterado, '--clave-extra', extra]), 1, /manifiesto_invalido/)

  // 6. Exe distinto al firmado.
  writeFileSync(exe, Buffer.concat([bytes, Buffer.from('!')]))
  expect('exe distinto', run('verificar-manifiesto.mjs', ['--manifiesto', man, '--clave-extra', extra, '--exe', exe]), 1, /tamaño|sha/)
} finally {
  rmSync(T, { recursive: true, force: true })
}

console.log(fallos ? `\n${fallos} prueba(s) fallaron` : '\nTodas las pruebas pasaron')
process.exit(fallos ? 1 : 0)
