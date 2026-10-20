// Tests de linux/relaunch.ts (SDD tatana-linux D-T6, D-T7): relanzar sin `app.relaunch` en un AppImage.
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { spawn, spawnSync } from 'child_process'
import { chmodSync, existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'
import { RELAUNCH_SCRIPT, relaunchCommand, relaunchEnv, spawnRelaunch } from './relaunch'

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms))

async function waitFor(cond: () => boolean, ms: number): Promise<boolean> {
  const until = Date.now() + ms
  while (Date.now() < until) {
    if (cond()) return true
    await sleep(100)
  }
  return cond()
}

/** Programa falso: escribe sus argumentos y el pid que esperaba el relanzador. */
function fakeApp(dir: string): { exe: string; out: string } {
  const out = join(dir, 'lanzado.txt')
  const exe = join(dir, 'Tatana App.AppImage') // con espacio: el script no tiene que partir la ruta
  writeFileSync(exe, `#!/bin/sh\nprintf '%s\\n' "$@" > "${out}"\n`)
  chmodSync(exe, 0o755)
  return { exe, out }
}

test('relaunchCommand: /bin/sh -c con pid, tope y programa como argumentos posicionales', () => {
  const c = relaunchCommand({ waitPid: 4242, execPath: '/home/u/Applications/Tatana.AppImage', args: ['--hidden'], timeoutS: 12.7 })
  assert.equal(c.command, '/bin/sh')
  assert.deepEqual(c.args, ['-c', RELAUNCH_SCRIPT, 'tatana-relaunch', '4242', '12', '/home/u/Applications/Tatana.AppImage', '--hidden'])
})

test('relaunchCommand: tope por defecto 30 s y nunca negativo', () => {
  assert.equal(relaunchCommand({ waitPid: 1, execPath: '/x' }).args[4], '30')
  assert.equal(relaunchCommand({ waitPid: 1, execPath: '/x', timeoutS: -5 }).args[4], '0')
})

test('relaunchEnv: restaura XDG_CURRENT_DESKTOP y saca las variables del AppImage viejo', () => {
  const env = relaunchEnv({
    HOME: '/home/u', DISPLAY: ':0', XDG_CURRENT_DESKTOP: 'Unity', ORIGINAL_XDG_CURRENT_DESKTOP: 'GNOME',
    APPIMAGE: '/home/u/Applications/Tatana.AppImage', APPDIR: '/tmp/.mount_TatanX', ARGV0: 'Tatana.AppImage', OWD: '/home/u',
    VACIA: undefined,
  })
  assert.deepEqual(env, { HOME: '/home/u', DISPLAY: ':0', XDG_CURRENT_DESKTOP: 'GNOME' })
})

test('relaunchEnv: sin ORIGINAL_XDG_CURRENT_DESKTOP deja XDG_CURRENT_DESKTOP como está', () => {
  assert.deepEqual(relaunchEnv({ XDG_CURRENT_DESKTOP: 'KDE' }), { XDG_CURRENT_DESKTOP: 'KDE' })
})

test('el script es sh válido', () => {
  const r = spawnSync('/bin/sh', ['-n', '-c', RELAUNCH_SCRIPT])
  assert.equal(r.status, 0, r.stderr.toString())
})

test('spawnRelaunch: sin /bin/sh devuelve false sin lanzar nada', () => {
  let called = false
  const ok = spawnRelaunch({ waitPid: 1, execPath: '/x' }, () => { called = true; return { unref() {}, on() {} } }, () => false)
  assert.equal(ok, false)
  assert.equal(called, false)
})

test('spawnRelaunch: lanza despegado, sin stdio y con el entorno limpio', () => {
  let seen: { command: string; args: string[]; options: Record<string, unknown> } | null = null
  const ok = spawnRelaunch(
    { waitPid: 7, execPath: '/c', env: { APPDIR: '/tmp/.mount_TatanX', HOME: '/h' } },
    (command, args, options) => { seen = { command, args, options: options as Record<string, unknown> }; return { unref() {}, on() {} } },
    () => true,
  )
  assert.equal(ok, true)
  assert.ok(seen)
  const s = seen as { command: string; options: Record<string, unknown> }
  assert.equal(s.command, '/bin/sh')
  assert.equal(s.options['detached'], true)
  assert.equal(s.options['stdio'], 'ignore')
  assert.deepEqual(s.options['env'], { HOME: '/h' })
})

test('espera a que salga el proceso viejo y recién ahí lanza el programa (con sus argumentos)', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'tatana-relaunch-'))
  try {
    const { exe, out } = fakeApp(dir)
    const viejo = spawn('/bin/sh', ['-c', 'sleep 2'], { stdio: 'ignore' })
    const { command, args } = relaunchCommand({ waitPid: viejo.pid!, execPath: exe, args: ['--hidden', 'a b'], timeoutS: 20 })
    spawn(command, args, { detached: true, stdio: 'ignore' }).unref()
    await sleep(1000)
    assert.equal(existsSync(out), false, 'lanzó antes de que saliera el proceso viejo')
    assert.ok(await waitFor(() => existsSync(out), 8000), 'no lanzó después de que salió el proceso viejo')
    assert.deepEqual(readFileSync(out, 'utf8').trim().split('\n'), ['--hidden', 'a b'])
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
})

test('con el proceso viejo ya muerto lanza enseguida', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'tatana-relaunch-'))
  try {
    const { exe, out } = fakeApp(dir)
    const muerto = spawnSync('/bin/sh', ['-c', 'echo $$']).stdout.toString().trim()
    const { command, args } = relaunchCommand({ waitPid: Number(muerto), execPath: exe, timeoutS: 20 })
    spawn(command, args, { detached: true, stdio: 'ignore' }).unref()
    assert.ok(await waitFor(() => existsSync(out), 3000), 'no lanzó con el pid ya muerto')
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
})

test('si el proceso viejo no sale, lanza igual al vencer el tope', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'tatana-relaunch-'))
  const colgado = spawn('/bin/sh', ['-c', 'sleep 30'], { stdio: 'ignore' })
  try {
    const { exe, out } = fakeApp(dir)
    const { command, args } = relaunchCommand({ waitPid: colgado.pid!, execPath: exe, timeoutS: 1 })
    spawn(command, args, { detached: true, stdio: 'ignore' }).unref()
    assert.ok(await waitFor(() => existsSync(out), 5000), 'no lanzó al vencer el tope')
  } finally {
    colgado.kill('SIGKILL')
    rmSync(dir, { recursive: true, force: true })
  }
})
