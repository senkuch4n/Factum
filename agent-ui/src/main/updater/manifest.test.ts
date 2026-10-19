// Tests de manifest.ts (SDD §6.5). Corren con `npm test` → `tsx --test`.
// Las claves de prueba se generan acá: no hay claves reales en el repo.
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { generateKeyPairSync, sign, createHash, type KeyObject } from 'node:crypto'
import {
  parseEnvelope, verifyEnvelope, decide, matchesUpdateInfo, sanitizeUpdateUrls,
  expectedProduct, expectedFile, manifestFileName,
  type TrustedKey, type UpdateEnvelope, type UpdatePayload,
} from './manifest'

// Los tests de siempre fijan el producto de Windows: el default sale de
// `process.platform` y el CI corre en Ubuntu (tatana-linux §10.C).
const W = 'tatana-windows' as const
const L = 'tatana-linux' as const

function newKey(keyId: string): { trusted: TrustedKey; privateKey: KeyObject } {
  const { publicKey, privateKey } = generateKeyPairSync('ed25519')
  const spki = publicKey.export({ format: 'der', type: 'spki' }) as Buffer
  return { trusted: { keyId, spkiDerBase64: spki.toString('base64') }, privateKey }
}

const SHA512 = createHash('sha512').update('instalador').digest('base64')
const SHA256 = createHash('sha256').update('instalador').digest('hex')

function payloadOf(over: Partial<Record<keyof UpdatePayload, unknown>> = {}): Record<string, unknown> {
  return {
    schema: 1,
    product: 'tatana-windows',
    version: '1.4.0',
    released_at: '2026-10-07T15:00:00Z',
    commit: 'a'.repeat(40),
    file: 'Tatana-Setup-1.4.0.exe',
    size: 312345678,
    sha512: SHA512,
    sha256: SHA256,
    notes: 'prueba',
    ...over,
  }
}

function envelope(privateKey: KeyObject, keyId: string, payload: Record<string, unknown>): UpdateEnvelope {
  const bytes = Buffer.from(JSON.stringify(payload), 'utf8')
  return {
    schema: 1,
    key_id: keyId,
    payload: bytes.toString('base64'),
    signature: sign(null, bytes, privateKey).toString('base64'),
  }
}

const K1 = newKey('tatana-test-1')
const K2 = newKey('tatana-test-2')

test('firma válida: ok con el payload', () => {
  const env = envelope(K1.privateKey, K1.trusted.keyId, payloadOf())
  const parsed = parseEnvelope(JSON.stringify(env))
  assert.ok(parsed)
  const r = verifyEnvelope(parsed, [K2.trusted, K1.trusted], W)
  assert.equal(r.ok, true)
  if (r.ok) {
    assert.equal(r.payload.version, '1.4.0')
    assert.equal(r.payload.sha512, SHA512)
  }
})

test('payload alterado (un byte): firma_invalida', () => {
  const env = envelope(K1.privateKey, K1.trusted.keyId, payloadOf())
  const bytes = Buffer.from(env.payload, 'base64')
  bytes[bytes.length - 2] ^= 0x01
  const r = verifyEnvelope({ ...env, payload: bytes.toString('base64') }, [K1.trusted], W)
  assert.deepEqual(r, { ok: false, reason: 'firma_invalida' })
})

test('firmado con otra clave pero con el key_id confiable: firma_invalida', () => {
  const env = envelope(K2.privateKey, K1.trusted.keyId, payloadOf())
  assert.deepEqual(verifyEnvelope(env, [K1.trusted], W), { ok: false, reason: 'firma_invalida' })
})

test('key_id desconocido: clave_desconocida', () => {
  const env = envelope(K2.privateKey, K2.trusted.keyId, payloadOf())
  assert.deepEqual(verifyEnvelope(env, [K1.trusted], W), { ok: false, reason: 'clave_desconocida' })
  assert.deepEqual(verifyEnvelope(env, [], W), { ok: false, reason: 'clave_desconocida' })
})

test('JSON roto o sobre con forma incorrecta: parseEnvelope da null', () => {
  assert.equal(parseEnvelope('{no es json'), null)
  assert.equal(parseEnvelope(''), null)
  assert.equal(parseEnvelope('[]'), null)
  assert.equal(parseEnvelope(JSON.stringify({ schema: 2, key_id: 'x', payload: 'AA==', signature: 'AA==' })), null)
  assert.equal(parseEnvelope(JSON.stringify({ schema: 1, key_id: 'x', payload: 1, signature: 'AA==' })), null)
  assert.equal(parseEnvelope('x'.repeat(70 * 1024)), null)
})

test('sobre con BOM: se acepta', () => {
  const env = envelope(K1.privateKey, K1.trusted.keyId, payloadOf())
  assert.ok(parseEnvelope('﻿' + JSON.stringify(env)))
})

test('schema/product/file/version incorrectos en el payload firmado: manifiesto_invalido', () => {
  const casos: Array<Record<string, unknown>> = [
    payloadOf({ schema: 2 }),
    payloadOf({ product: 'tatana-mac' }),
    payloadOf({ file: 'otro.exe' }),
    payloadOf({ version: '1.4.0-beta.1', file: 'Tatana-Setup-1.4.0-beta.1.exe' }),
    payloadOf({ size: 0 }),
    payloadOf({ sha512: 'no-es-base64' }),
  ]
  for (const p of casos) {
    const r = verifyEnvelope(envelope(K1.privateKey, K1.trusted.keyId, p), [K1.trusted], W)
    assert.deepEqual(r, { ok: false, reason: 'manifiesto_invalido' }, JSON.stringify(p))
  }
})

test('payload firmado que no es JSON: manifiesto_invalido', () => {
  const bytes = Buffer.from('no json', 'utf8')
  const env: UpdateEnvelope = {
    schema: 1, key_id: K1.trusted.keyId,
    payload: bytes.toString('base64'), signature: sign(null, bytes, K1.privateKey).toString('base64'),
  }
  assert.deepEqual(verifyEnvelope(env, [K1.trusted], W), { ok: false, reason: 'manifiesto_invalido' })
})

test('decide: mayor actualiza, igual al día, menor rechazado', () => {
  assert.equal(decide({ version: '1.4.0' }, '1.3.9'), 'update')
  assert.equal(decide({ version: '1.10.0' }, '1.9.0'), 'update')
  assert.equal(decide({ version: '1.4.0' }, '1.4.0'), 'up_to_date')
  assert.equal(decide({ version: '1.3.0' }, '1.4.0'), 'refused_downgrade')
  assert.equal(decide({ version: '1.4.0' }, 'no-semver'), 'refused_downgrade')
})

test('matchesUpdateInfo: coincide solo si versión, archivo y sha512 son iguales', () => {
  const r = verifyEnvelope(envelope(K1.privateKey, K1.trusted.keyId, payloadOf()), [K1.trusted], W)
  assert.ok(r.ok)
  if (!r.ok) return
  const p = r.payload
  const ok = { version: '1.4.0', files: [{ url: 'Tatana-Setup-1.4.0.exe', sha512: SHA512, size: 312345678 }], path: 'Tatana-Setup-1.4.0.exe', sha512: SHA512 }
  assert.equal(matchesUpdateInfo(p, ok), null)
  const otroSha = createHash('sha512').update('otro').digest('base64')
  assert.equal(matchesUpdateInfo(p, { ...ok, files: [{ ...ok.files[0], sha512: otroSha }], sha512: otroSha }), 'no_coincide_con_manifiesto')
  assert.equal(matchesUpdateInfo(p, { ...ok, files: [{ ...ok.files[0], url: 'Tatana-Setup-1.4.1.exe' }] }), 'no_coincide_con_manifiesto')
  assert.equal(matchesUpdateInfo(p, { ...ok, version: '1.4.1' }), 'no_coincide_con_manifiesto')
  assert.equal(matchesUpdateInfo(p, { ...ok, files: [{ ...ok.files[0], size: 1 }] }), 'no_coincide_con_manifiesto')
  assert.equal(matchesUpdateInfo(p, null), 'no_coincide_con_manifiesto')
})

test('sanitizeUpdateUrls: solo https horneadas; override http solo loopback', () => {
  assert.deepEqual(
    sanitizeUpdateUrls(['https://a.example.com/tatana/updates/', 'http://b.example.com/tatana/updates/', 'https://c.example.com/tatana/updates', 'https://a.example.com/tatana/updates/']),
    ['https://a.example.com/tatana/updates/', 'https://c.example.com/tatana/updates/'],
  )
  assert.deepEqual(sanitizeUpdateUrls(['https://a.example.com/u/'], 'http://127.0.0.1:8099/'), ['http://127.0.0.1:8099/'])
  assert.deepEqual(sanitizeUpdateUrls(['https://a.example.com/u/'], 'http://localhost:8099/x'), ['http://localhost:8099/x/'])
  // http a un host que no es loopback: rechazado → override ignorado, quedan las horneadas.
  assert.deepEqual(sanitizeUpdateUrls(['https://a.example.com/u/'], 'http://otro.example.com/'), ['https://a.example.com/u/'])
  assert.deepEqual(sanitizeUpdateUrls([], 'http://otro.example.com/'), [])
  assert.deepEqual(sanitizeUpdateUrls(['https://user:pw@a.example.com/u/', 'https://a.example.com/u/?x=1']), [])
  assert.deepEqual(sanitizeUpdateUrls(['https://a.example.com/u/'], ' , '), ['https://a.example.com/u/'])
})

// ── Linux (tatana-linux D-T8, §10.C) ────────────────────────────────────────

function linuxPayload(over: Partial<Record<keyof UpdatePayload, unknown>> = {}): Record<string, unknown> {
  return payloadOf({ product: 'tatana-linux', file: 'Tatana-1.4.0-x86_64.AppImage', ...over })
}

test('producto esperado por plataforma y nombres del canal', () => {
  assert.equal(expectedProduct('linux'), 'tatana-linux')
  assert.equal(expectedProduct('win32'), 'tatana-windows')
  assert.equal(expectedProduct('darwin'), 'tatana-windows')
  assert.equal(manifestFileName('tatana-linux'), 'tatana-update-linux.json')
  assert.equal(manifestFileName('tatana-windows'), 'tatana-update.json')
  assert.equal(expectedFile('tatana-linux', '1.4.0'), 'Tatana-1.4.0-x86_64.AppImage')
  assert.equal(expectedFile('tatana-windows', '1.4.0'), 'Tatana-Setup-1.4.0.exe')
})

test('Linux: manifiesto tatana-linux válido', () => {
  const r = verifyEnvelope(envelope(K1.privateKey, K1.trusted.keyId, linuxPayload()), [K1.trusted], L)
  assert.ok(r.ok)
  if (!r.ok) return
  assert.equal(r.payload.product, 'tatana-linux')
  assert.equal(r.payload.file, 'Tatana-1.4.0-x86_64.AppImage')
})

test('Linux rechaza un manifiesto de Windows', () => {
  const r = verifyEnvelope(envelope(K1.privateKey, K1.trusted.keyId, payloadOf()), [K1.trusted], L)
  assert.deepEqual(r, { ok: false, reason: 'manifiesto_invalido' })
})

test('Windows rechaza un manifiesto de Linux', () => {
  const r = verifyEnvelope(envelope(K1.privateKey, K1.trusted.keyId, linuxPayload()), [K1.trusted], W)
  assert.deepEqual(r, { ok: false, reason: 'manifiesto_invalido' })
})

test('Linux: file con otro nombre o versión → manifiesto_invalido', () => {
  const casos = [
    linuxPayload({ file: 'Tatana-Setup-1.4.0.exe' }),
    linuxPayload({ file: 'Tatana-1.4.1-x86_64.AppImage' }),
    linuxPayload({ file: 'Tatana.AppImage' }),
    linuxPayload({ file: 'Tatana-1.4.0-arm64.AppImage' }),
    payloadOf({ file: 'Tatana-1.4.0-x86_64.AppImage' }), // producto Windows con archivo de Linux
  ]
  for (const p of casos) {
    const product = p.product === 'tatana-linux' ? L : W
    const r = verifyEnvelope(envelope(K1.privateKey, K1.trusted.keyId, p), [K1.trusted], product)
    assert.deepEqual(r, { ok: false, reason: 'manifiesto_invalido' }, JSON.stringify(p))
  }
})

test('Linux: matchesUpdateInfo con el AppImage de latest-linux.yml', () => {
  const r = verifyEnvelope(envelope(K1.privateKey, K1.trusted.keyId, linuxPayload()), [K1.trusted], L)
  assert.ok(r.ok)
  if (!r.ok) return
  const file = 'Tatana-1.4.0-x86_64.AppImage'
  assert.equal(matchesUpdateInfo(r.payload, { version: '1.4.0', files: [{ url: file, sha512: SHA512, size: 312345678 }], path: file, sha512: SHA512 }), null)
})
