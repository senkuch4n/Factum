// Tests de manifest-request.ts. Corren con `npm test` (`tsx --test`).
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { MANIFEST_NO_CACHE_HEADERS, classifyManifestStatus, manifestRequestOptions } from './manifest-request'

test('el pedido del manifiesto no usa la caché HTTP (no-store, sin cookies, redirect manual)', () => {
  const o = manifestRequestOptions('https://canal.example/tatana/updates/tatana-update.json')
  assert.equal(o.cache, 'no-store')
  assert.equal(o.useSessionCookies, false)
  assert.equal(o.redirect, 'manual')
  assert.equal(o.method, 'GET')
  assert.equal(o.url, 'https://canal.example/tatana/updates/tatana-update.json')
})

test('las cabeceras piden no-cache también a los proxies', () => {
  assert.equal(MANIFEST_NO_CACHE_HEADERS['Cache-Control'], 'no-cache')
  assert.equal(MANIFEST_NO_CACHE_HEADERS['Pragma'], 'no-cache')
  assert.ok(Object.isFrozen(MANIFEST_NO_CACHE_HEADERS))
})

test('un 304 nunca se toma como cuerpo válido', () => {
  assert.equal(classifyManifestStatus(304), 'not_modified')
})

test('2xx se lee; 4xx/5xx y otros son error', () => {
  assert.equal(classifyManifestStatus(200), 'body')
  assert.equal(classifyManifestStatus(203), 'body')
  for (const s of [100, 301, 400, 404, 500, 503]) assert.equal(classifyManifestStatus(s), 'error', String(s))
})
