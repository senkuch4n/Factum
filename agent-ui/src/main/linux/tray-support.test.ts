// Tests de linux/tray-support.ts (SDD tatana-linux D-T18).
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { parseNameHasOwner } from './tray-support'

test('parseNameHasOwner: respuestas de gdbus', () => {
  assert.equal(parseNameHasOwner('(true,)\n'), true)
  assert.equal(parseNameHasOwner('(false,)\n'), false)
  assert.equal(parseNameHasOwner('( true , )'), true)
  assert.equal(parseNameHasOwner('Error: algo'), null)
  assert.equal(parseNameHasOwner(''), null)
})
