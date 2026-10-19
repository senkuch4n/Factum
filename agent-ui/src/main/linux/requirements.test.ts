// Tests de linux/requirements.ts (SDD tatana-linux F3, D-T2).
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { glibcTooOld, requirementsMessage, MIN_GLIBC } from './requirements'

test('glibcTooOld: menor que 2.35 bloquea', () => {
  assert.equal(glibcTooOld('2.31'), true)   // Ubuntu 20.04
  assert.equal(glibcTooOld('2.34'), true)
  assert.equal(glibcTooOld('1.99'), true)
})

test('glibcTooOld: igual o mayor no bloquea', () => {
  assert.equal(glibcTooOld('2.35'), false)  // Ubuntu 22.04
  assert.equal(glibcTooOld('2.36'), false)  // Debian 12
  assert.equal(glibcTooOld('2.39'), false)  // Ubuntu 24.04
  assert.equal(glibcTooOld('2.100'), false) // comparación numérica, no de texto
  assert.equal(glibcTooOld('3.0'), false)
})

test('glibcTooOld: versión ilegible no bloquea', () => {
  assert.equal(glibcTooOld(''), false)
  assert.equal(glibcTooOld(null), false)
  assert.equal(glibcTooOld(undefined), false)
  assert.equal(glibcTooOld('desconocida'), false)
})

test('glibcTooOld: mínimo configurable', () => {
  assert.equal(glibcTooOld('2.27', '2.28'), true)
  assert.equal(glibcTooOld('2.28.1', '2.28'), false)
})

test('requirementsMessage: castellano con la versión detectada y la mínima', () => {
  const m = requirementsMessage('2.31')
  assert.match(m, /glibc 2\.31/)
  assert.ok(m.includes(`hace falta ${MIN_GLIBC} o posterior`))
  assert.match(m, /Ubuntu 22\.04 o Debian 12/)
})
