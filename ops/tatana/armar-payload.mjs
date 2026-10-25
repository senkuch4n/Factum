#!/usr/bin/env node
// Arma el payload de tatana-update.json / tatana-update-linux.json (SDD tatana-instalador-autoupdate §13.C;
// tatana-linux §10.C) a partir del instalador (o del AppImage) y del latest.yml / latest-linux.yml que generó
// electron-builder. Verifica que el sha512 y el tamaño del archivo coincidan con el yml antes de escribir nada.
//
//   node ops/tatana/armar-payload.mjs [--producto tatana-windows|tatana-linux] --archivo dist/Tatana-Setup-1.4.0.exe \
//       --latest dist/latest.yml --version 1.4.0 --commit <40 hex> --salida payload.json [--notas "texto"] [--fecha <ISO>]
//
// --exe se sigue aceptando como alias de --archivo. --producto default: tatana-windows.
//
// Tatana en Docker (SDD fiscalia-operacion §5.2): sin --latest; la imagen (docker save + gzip), el kit y el nombre:tag
// que tiene que quedar cargado:
//
//   node ops/tatana/armar-payload.mjs --producto tatana-docker --archivo imagen-tatana-docker-v1.5.0.tar.gz \
//       --kit kit-tatana-docker-v1.5.0.tar.gz --imagen factum-tatana-docker:1.5.0 --version 1.5.0 --commit <40 hex> \
//       --salida payload-docker.json [--notas "texto"] [--fecha <ISO>]
import { createHash } from 'node:crypto'
import { readFileSync, statSync, writeFileSync } from 'node:fs'
import { basename } from 'node:path'
import { expectedFile, expectedImage, expectedKitFile, fail, parseArgs, payloadError, productArg } from './_lib.mjs'

const a = parseArgs(process.argv.slice(2), {
  allowed: ['producto', 'archivo', 'exe', 'latest', 'version', 'commit', 'salida', 'notas', 'fecha', 'kit', 'imagen'],
})
if (a.archivo && a.exe && a.archivo !== a.exe) fail('--archivo y --exe apuntan a archivos distintos', 2)
a.archivo ??= a.exe
const product = productArg(a.producto)
const docker = product === 'tatana-docker'
const requeridos = docker ? ['archivo', 'kit', 'imagen', 'version', 'commit', 'salida'] : ['archivo', 'latest', 'version', 'commit', 'salida']
for (const k of requeridos) if (!a[k]) fail(`falta --${k}`, 2)
if (docker && a.latest) fail('--latest no corresponde a tatana-docker', 2)
if (!docker && (a.kit || a.imagen)) fail('--kit e --imagen son solo de --producto tatana-docker', 2)

const exe = readFileSync(a.archivo)
const size = statSync(a.archivo).size
const sha512 = createHash('sha512').update(exe).digest('base64')
const sha256 = createHash('sha256').update(exe).digest('hex')
const file = basename(a.archivo)
const checkFile = () => {
  if (file !== expectedFile(product, a.version)) fail(`el archivo de ${product} tiene que llamarse ${expectedFile(product, a.version)} (es ${file})`)
}

let kit = null
if (docker) {
  checkFile()
  if (a.imagen !== expectedImage(a.version)) fail(`--imagen tiene que ser ${expectedImage(a.version)} (es ${a.imagen})`)
  const kitFile = basename(a.kit)
  if (kitFile !== expectedKitFile(a.version)) fail(`el kit tiene que llamarse ${expectedKitFile(a.version)} (es ${kitFile})`)
  const kitBytes = readFileSync(a.kit)
  kit = { file: kitFile, size: kitBytes.length, sha256: createHash('sha256').update(kitBytes).digest('hex') }
} else {
  checkLatest()
}

// latest.yml de electron-builder: version, files[0].{url,sha512,size}, path, sha512.
function checkLatest() {
  const { top, files } = parseLatestYml(readFileSync(a.latest, 'utf8'))
  if (top.version !== a.version) fail(`latest.yml dice version ${top.version} y se esperaba ${a.version}`)
  checkFile()
  if (top.path !== undefined && top.path !== file) fail(`latest.yml apunta a ${top.path} y el archivo es ${file}`)
  if (top.sha512 !== undefined && top.sha512 !== sha512) fail('el sha512 de latest.yml no coincide con el del archivo')
  if (files.length !== 1) fail(`latest.yml tiene que listar exactamente un archivo (tiene ${files.length})`)
  const [f] = files
  if (f.url !== file || f.sha512 !== sha512 || (f.size !== undefined && Number(f.size) !== size))
    fail('files[0] de latest.yml no coincide con el archivo (url, sha512 o size)')
}

// Parser mínimo del YAML plano de electron-builder: claves de primer nivel y la lista "files".
function parseLatestYml(text) {
  const unq = (v) => v.trim().replace(/^(['"])(.*)\1$/, '$2')
  const top = {}
  const files = []
  let inFiles = false
  for (const line of text.replace(/\r/g, '').split('\n')) {
    if (!line.trim()) continue
    let m
    if ((m = line.match(/^([A-Za-z0-9_]+):\s*(.*)$/))) {
      inFiles = m[1] === 'files' && m[2] === ''
      if (!inFiles) top[m[1]] = unq(m[2])
    } else if (inFiles && (m = line.match(/^\s+-\s+([A-Za-z0-9_]+):\s*(.*)$/))) {
      files.push({ [m[1]]: unq(m[2]) })
    } else if (inFiles && files.length && (m = line.match(/^\s+([A-Za-z0-9_]+):\s*(.*)$/))) {
      files[files.length - 1][m[1]] = unq(m[2])
    }
  }
  return { top, files }
}

const payload = {
  schema: 1,
  product,
  version: a.version,
  released_at: a.fecha ?? new Date().toISOString().replace(/\.\d{3}Z$/, 'Z'),
  commit: a.commit,
  file,
  size,
  sha512,
  sha256,
  ...(docker ? { image: a.imagen, kit } : {}),
  ...(a.notas && a.notas.trim() ? { notes: a.notas.trim().slice(0, 2000) } : {}),
}
const err = payloadError(payload, product)
if (err) fail(err)
writeFileSync(a.salida, JSON.stringify(payload, null, 2) + '\n')
console.log(`OK payload ${a.salida}: ${file} ${size} bytes, sha256 ${sha256}`)
