// Tests de linux/process-sweep.ts (SDD tatana-linux F5, D-T17).
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { cleanExe, selectOrphans, type ProcEntry } from './process-sweep'

const BIN = '/tmp/.mount_TatanAbC123/resources/agent'
const UID = 1000

const procs: ProcEntry[] = [
  { pid: 100, exe: '/tmp/.mount_TatanAbC123/tatana', uid: UID },                                      // Electron (no está bajo agent/)
  { pid: 101, exe: `${BIN}/Factum.Agent`, uid: UID },
  { pid: 102, exe: `${BIN}/tools/platform-tools/adb`, uid: UID },
  { pid: 103, exe: `${BIN}/tools/scrcpy/scrcpy (deleted)`, uid: UID },
  { pid: 104, exe: `${BIN}/tools/python-embed/bin/python3.11`, uid: UID },
  { pid: 105, exe: `${BIN}/tools/ffmpeg/ffmpeg`, uid: 0 },                                           // de otro usuario
  { pid: 106, exe: '/tmp/.mount_TatanXyZ999/resources/agent/tools/platform-tools/adb', uid: UID },   // otra instancia
  { pid: 107, exe: `${BIN}-viejo/Factum.Agent`, uid: UID },                                          // prefijo parecido
  { pid: 108, exe: '/usr/bin/adb', uid: UID },
]

test('selecciona solo los procesos propios bajo binDir/', () => {
  assert.deepEqual(selectOrphans(procs, BIN, 100, UID), [101, 102, 103, 104])
})

test('barra final en binDir no cambia el resultado', () => {
  assert.deepEqual(selectOrphans(procs, `${BIN}/`, 100, UID), [101, 102, 103, 104])
})

test('nunca selecciona el propio pid', () => {
  assert.deepEqual(selectOrphans(procs, BIN, 102, UID), [101, 103, 104])
})

test('binDir vacío, relativo o raíz no selecciona nada', () => {
  assert.deepEqual(selectOrphans(procs, '', 1, UID), [])
  assert.deepEqual(selectOrphans(procs, 'resources/agent', 1, UID), [])
  assert.deepEqual(selectOrphans(procs, '/', 1, UID), [])
})

test('cleanExe saca el sufijo (deleted)', () => {
  assert.equal(cleanExe('/a/b (deleted)'), '/a/b')
  assert.equal(cleanExe('/a/b'), '/a/b')
})
