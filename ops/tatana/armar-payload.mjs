#!/usr/bin/env node
// Arma el payload de tatana-update.json (SDD tatana-instalador-autoupdate §13.C) a partir del
// instalador y del latest.yml que generó electron-builder. Verifica que el sha512 y el tamaño del exe
// coincidan con latest.yml antes de escribir nada.
//
//   node ops/tatana/armar-payload.mjs --exe dist/Tatana-Setup-1.4.0.exe --latest dist/latest.yml \
//       --version 1.4.0 --commit <40 hex> --salida payload.json [--notas "texto"] [--fecha <ISO>]
import { createHash } from 'node:crypto'
import { readFileSync, statSync, writeFileSync } from 'node:fs'
import { basename } from 'node:path'
import { fail, parseArgs, payloadError } from './_lib.mjs'

const a = parseArgs(process.argv.slice(2), { allowed: ['exe', 'latest', 'version', 'commit', 'salida', 'notas', 'fecha'] })
for (const k of ['exe', 'latest', 'version', 'commit', 'salida']) if (!a[k]) fail(`falta --${k}`, 2)

const exe = readFileSync(a.exe)
const size = statSync(a.exe).size
const sha512 = createHash('sha512').update(exe).digest('base64')
const sha256 = createHash('sha256').update(exe).digest('hex')
const file = basename(a.exe)

// latest.yml de electron-builder: version, files[0].{url,sha512,size}, path, sha512.
const { top, files } = parseLatestYml(readFileSync(a.latest, 'utf8'))
if (top.version !== a.version) fail(`latest.yml dice version ${top.version} y se esperaba ${a.version}`)
if (top.path !== undefined && top.path !== file) fail(`latest.yml apunta a ${top.path} y el exe es ${file}`)
if (top.sha512 !== undefined && top.sha512 !== sha512) fail('el sha512 de latest.yml no coincide con el del exe')
if (files.length !== 1) fail(`latest.yml tiene que listar exactamente un archivo (tiene ${files.length})`)
const [f] = files
if (f.url !== file || f.sha512 !== sha512 || (f.size !== undefined && Number(f.size) !== size))
  fail('files[0] de latest.yml no coincide con el exe (url, sha512 o size)')

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
  product: 'tatana-windows',
  version: a.version,
  released_at: a.fecha ?? new Date().toISOString().replace(/\.\d{3}Z$/, 'Z'),
  commit: a.commit,
  file,
  size,
  sha512,
  sha256,
  ...(a.notas && a.notas.trim() ? { notes: a.notas.trim().slice(0, 2000) } : {}),
}
const err = payloadError(payload)
if (err) fail(err)
writeFileSync(a.salida, JSON.stringify(payload, null, 2) + '\n')
console.log(`OK payload ${a.salida}: ${file} ${size} bytes, sha256 ${sha256}`)
