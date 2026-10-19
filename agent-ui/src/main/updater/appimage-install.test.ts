// Tests de appimage-install.ts (SDD tatana-linux D-T7), en una carpeta temporal propia.
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { existsSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { installAppImage, matchesManifest } from './appimage-install'

function fixture(): { dir: string; file: string; expected: { size: number; sha512: string }; cleanup: () => void } {
  const dir = mkdtempSync(join(tmpdir(), 'tatana-appimage-'))
  const content = Buffer.from('AppImage de prueba v1.4.0')
  const file = join(dir, 'descargado.AppImage')
  writeFileSync(file, content)
  return {
    dir,
    file,
    expected: { size: content.length, sha512: createHash('sha512').update(content).digest('base64') },
    cleanup: () => rmSync(dir, { recursive: true, force: true }),
  }
}

test('matchesManifest: tamaño y sha512 exactos', async () => {
  const f = fixture()
  try {
    assert.equal(await matchesManifest(f.file, f.expected), true)
    assert.equal(await matchesManifest(f.file, { ...f.expected, size: f.expected.size + 1 }), false)
    assert.equal(await matchesManifest(f.file, { ...f.expected, sha512: createHash('sha512').update('x').digest('base64') }), false)
    assert.equal(await matchesManifest(join(f.dir, 'no-existe'), f.expected), false)
  } finally { f.cleanup() }
})

test('installAppImage: reemplaza la canónica, ejecutable, sin dejar .nuevo', async () => {
  const f = fixture()
  try {
    const canonical = join(f.dir, 'Applications', 'Tatana.AppImage')
    const r = await installAppImage({ downloadedFile: f.file, canonicalPath: canonical, expected: f.expected })
    assert.deepEqual(r, { ok: true })
    assert.equal(readFileSync(canonical, 'utf8'), 'AppImage de prueba v1.4.0')
    assert.equal(statSync(canonical).mode & 0o777, 0o755)
    assert.equal(existsSync(`${canonical}.nuevo`), false)
  } finally { f.cleanup() }
})

test('installAppImage: con hash distinto no toca la instalada', async () => {
  const f = fixture()
  try {
    const canonical = join(f.dir, 'Tatana.AppImage')
    writeFileSync(canonical, 'versión instalada')
    const r = await installAppImage({
      downloadedFile: f.file, canonicalPath: canonical,
      expected: { ...f.expected, sha512: createHash('sha512').update('otro').digest('base64') },
    })
    assert.equal(r.ok, false)
    assert.equal(readFileSync(canonical, 'utf8'), 'versión instalada')
  } finally { f.cleanup() }
})
