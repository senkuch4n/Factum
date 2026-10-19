// Tests de linux/ensure-file.ts (SDD tatana-linux D-T5, humo (e)): tatana.json se siembra en el primer arranque.
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'
import { ensureFile } from './ensure-file'

function scratch(): string {
  return mkdtempSync(join(tmpdir(), 'tatana-ensure-'))
}

test('crea el archivo y la carpeta si no existen', () => {
  const dir = scratch()
  try {
    const p = join(dir, '.config', 'Tatana', 'tatana.json')
    assert.equal(ensureFile(p, '{"agentPort":8765}'), true)
    assert.deepEqual(JSON.parse(readFileSync(p, 'utf-8')), { agentPort: 8765 })
    assert.equal(existsSync(`${p}.tmp`), false)
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
})

test('no pisa un archivo existente', () => {
  const dir = scratch()
  try {
    const p = join(dir, 'tatana.json')
    writeFileSync(p, '{"serverUrl":"https://perito.example"}')
    assert.equal(ensureFile(p, '{"serverUrl":"otro"}'), false)
    assert.equal(readFileSync(p, 'utf-8'), '{"serverUrl":"https://perito.example"}')
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
})

test('un .tmp huérfano de un arranque cortado no lo frena', () => {
  const dir = scratch()
  try {
    const p = join(dir, 'tatana.json')
    writeFileSync(`${p}.tmp`, 'basura a medias')
    assert.equal(ensureFile(p, '{}'), true)
    assert.equal(readFileSync(p, 'utf-8'), '{}')
    assert.equal(existsSync(`${p}.tmp`), false)
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
})
