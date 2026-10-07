// Tests de maintenance-outcome.ts (D5: nunca matar un agente vivo que no contestó).
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { classifyMaintenance, classifyTransportError } from './maintenance-outcome'

test('classifyTransportError: códigos de Node', () => {
  assert.equal(classifyTransportError(Object.assign(new Error('x'), { code: 'ECONNREFUSED' })), 'refused')
  assert.equal(classifyTransportError(Object.assign(new Error('x'), { code: 'ECONNRESET' })), 'reset')
  assert.equal(classifyTransportError(Object.assign(new Error('x'), { code: 'EPIPE' })), 'reset')
  assert.equal(classifyTransportError(Object.assign(new Error('x'), { code: 'ETIMEDOUT' })), 'timeout')
  assert.equal(classifyTransportError(new Error('sin código')), 'other')
  assert.equal(classifyTransportError(null), 'other')
})

test('conexión rechazada: unreachable (nadie escucha el puerto)', () => {
  assert.deepEqual(classifyMaintenance({ kind: 'failure', failure: 'refused' }, true), { kind: 'unreachable' })
  assert.deepEqual(classifyMaintenance({ kind: 'failure', failure: 'refused' }, false), { kind: 'unreachable' })
})

test('agente vivo que no contesta (timeout/reset/otro): error, NO unreachable', () => {
  for (const failure of ['timeout', 'reset', 'other'] as const) {
    const r = classifyMaintenance({ kind: 'failure', failure }, true)
    assert.equal(r.kind, 'error', failure)
  }
})

test('proceso del agente que ya no corre: unreachable ante cualquier falla de transporte', () => {
  for (const failure of ['timeout', 'reset', 'other'] as const) {
    assert.deepEqual(classifyMaintenance({ kind: 'failure', failure }, false), { kind: 'unreachable' })
  }
})

test('200: ok con expires_at', () => {
  assert.deepEqual(
    classifyMaintenance({ kind: 'response', status: 200, body: { maintenance: true, expires_at: '2026-10-07T15:00:00Z' } }, true),
    { kind: 'ok', expiresAt: '2026-10-07T15:00:00Z' },
  )
})

test('409: busy con las operaciones (o una genérica si no vienen)', () => {
  const ops = [{ kind: 'recording_android', since: '2026-10-07T15:04:05+00:00' }]
  assert.deepEqual(
    classifyMaintenance({ kind: 'response', status: 409, body: { code: 'agent_busy', operations: ops } }, true),
    { kind: 'busy', operations: ops },
  )
  assert.deepEqual(
    classifyMaintenance({ kind: 'response', status: 409, body: null }, true),
    { kind: 'busy', operations: [{ kind: 'request', since: '' }] },
  )
})

test('5xx / 403 / 404: error (no instala)', () => {
  for (const status of [500, 503, 403, 404]) {
    assert.deepEqual(classifyMaintenance({ kind: 'response', status, body: null }, true), { kind: 'error', status })
  }
})
