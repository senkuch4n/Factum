// Tests de state.ts: aviso de operaciones en segundo plano (ios-grabacion-larga, SDD §7).
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { backgroundOperationsNotice, busyOperationText } from './state'

const NOTICE = 'Al actualizar, se cancelará la versión suavizada que se está generando. Las grabaciones no se tocan.'

test('backgroundOperationsNotice: sin operaciones no avisa', () => {
  assert.equal(backgroundOperationsNotice([]), null)
  assert.equal(backgroundOperationsNotice(undefined), null)
})

test('backgroundOperationsNotice: post-proceso de video avisa que se cancela', () => {
  assert.equal(backgroundOperationsNotice(['video_postprocess']), NOTICE)
  assert.equal(backgroundOperationsNotice(['otro', 'video_postprocess']), NOTICE)
})

test('backgroundOperationsNotice: kind desconocido no avisa', () => {
  assert.equal(backgroundOperationsNotice(['otro']), null)
})

test('busyOperationText mantiene el texto de video_postprocess (Tatana viejo)', () => {
  assert.equal(busyOperationText('video_postprocess'), 'procesando un video')
})
