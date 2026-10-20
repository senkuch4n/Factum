// Tests del protocolo de "Preparar esta PC" (SDD tatana-linux F6, §7, §10.B, §10.F).
import { test } from 'node:test'
import assert from 'node:assert/strict'
import {
  buildPreparacion, fallbackCommandText, finalResultado, mergePaso, parsePreparacion,
  parsePrepareLine, resultadoFromExit, shellQuote,
} from './prepare-pc-protocol'

const PLAN = '{"schema":1,"soportada":true,"distro_id":"ubuntu","distro_version":"24.04","distro_name":"Ubuntu 24.04.1 LTS",' +
  '"gestor":"apt","paquetes":["usbmuxd","avahi-daemon"],"uxplay":{"candidata":"1.68","instalar":false},' +
  '"archivos":["/etc/udev/rules.d/70-factum-tatana.rules","/etc/apparmor.d/factum-tatana"],' +
  '"servicios":["avahi-daemon"],"firewall":"inactivo","grupos":[],' +
  '"comandos":["apt-get update","apt-get install -y --no-install-recommends usbmuxd avahi-daemon"]}'

test('parsePrepareLine: TATANA_PLAN', () => {
  const r = parsePrepareLine(`TATANA_PLAN ${PLAN}`)
  assert.equal(r.kind, 'plan')
  if (r.kind !== 'plan') return
  assert.equal(r.data.soportada, true)
  assert.equal(r.data.gestor, 'apt')
  assert.equal(r.data.distro_version, '24.04')
  assert.deepEqual(r.data.paquetes, ['usbmuxd', 'avahi-daemon'])
  assert.deepEqual(r.data.uxplay, { candidata: '1.68', instalar: false })
  assert.equal(r.data.comandos.length, 2)
})

test('parsePrepareLine: TATANA_PASO en curso y con detalle', () => {
  assert.deepEqual(parsePrepareLine('TATANA_PASO {"id":"paquetes","estado":"en_curso"}'),
    { kind: 'paso', data: { id: 'paquetes', estado: 'en_curso' } })
  assert.deepEqual(parsePrepareLine('TATANA_PASO {"id":"reglas_usb","estado":"error","detalle":"udevadm falló"}'),
    { kind: 'paso', data: { id: 'reglas_usb', estado: 'error', detalle: 'udevadm falló' } })
})

test('parsePrepareLine: TATANA_RESULTADO', () => {
  const r = parsePrepareLine('TATANA_RESULTADO {"schema":1,"resultado":"parcial","requiere_reiniciar_sesion":true,' +
    '"pasos":[{"id":"paquetes","estado":"ok","detalle":"usbmuxd avahi-daemon"},{"id":"airplay","estado":"error","detalle":"x"}]}')
  assert.equal(r.kind, 'resultado')
  if (r.kind !== 'resultado') return
  assert.equal(r.data.resultado, 'parcial')
  assert.equal(r.data.requiere_reiniciar_sesion, true)
  assert.equal(r.data.pasos.length, 2)
})

test('parsePrepareLine: CRLF final se ignora', () => {
  assert.equal(parsePrepareLine('TATANA_PASO {"id":"sandbox","estado":"omitido"}\r').kind, 'paso')
})

test('parsePrepareLine: log, JSON roto, id o estado desconocidos', () => {
  assert.deepEqual(parsePrepareLine('Leyendo lista de paquetes...'), { kind: 'log', data: 'Leyendo lista de paquetes...' })
  assert.equal(parsePrepareLine('TATANA_PASO {roto').kind, 'log')
  assert.equal(parsePrepareLine('TATANA_PASO {"id":"otro","estado":"ok"}').kind, 'log')
  assert.equal(parsePrepareLine('TATANA_PASO {"id":"paquetes","estado":"raro"}').kind, 'log')
  assert.equal(parsePrepareLine('TATANA_RESULTADO {"resultado":"cancelado"}').kind, 'log')
  assert.equal(parsePrepareLine('TATANA_PLANX {}').kind, 'log')
  assert.equal(parsePrepareLine('TATANA_PLAN  ').kind, 'log')
})

test('resultadoFromExit: mapeo de §10.B', () => {
  assert.equal(resultadoFromExit(0), 'ok')
  assert.equal(resultadoFromExit(1), 'parcial')
  assert.equal(resultadoFromExit(2), 'error')
  assert.equal(resultadoFromExit(3), 'sin_soporte')
  assert.equal(resultadoFromExit(4), 'sin_internet')
  assert.equal(resultadoFromExit(5), 'error')
  assert.equal(resultadoFromExit(126), 'cancelado')
  assert.equal(resultadoFromExit(127), 'sin_polkit')
  assert.equal(resultadoFromExit(null), 'error')
  assert.equal(resultadoFromExit(0, { pkexecMissing: true }), 'sin_polkit')
})

test('finalResultado: pkexec manda; si no, TATANA_RESULTADO; si no, el código', () => {
  const linea = { schema: 1 as const, resultado: 'parcial' as const, requiere_reiniciar_sesion: false, pasos: [] }
  assert.equal(finalResultado(126, linea), 'cancelado')
  assert.equal(finalResultado(127, null), 'sin_polkit')
  assert.equal(finalResultado(1, linea), 'parcial')
  assert.equal(finalResultado(0, null), 'ok')
  assert.equal(finalResultado(4, null), 'sin_internet')
})

test('mergePaso: reemplaza por id y respeta el orden de los pasos', () => {
  let pasos = mergePaso([], { id: 'airplay', estado: 'en_curso' })
  pasos = mergePaso(pasos, { id: 'paquetes', estado: 'ok' })
  pasos = mergePaso(pasos, { id: 'airplay', estado: 'ok', detalle: 'listo' })
  assert.deepEqual(pasos, [{ id: 'paquetes', estado: 'ok' }, { id: 'airplay', estado: 'ok', detalle: 'listo' }])
})

test('buildPreparacion / parsePreparacion: forma de §10.F', () => {
  const planLine = parsePrepareLine(`TATANA_PLAN ${PLAN}`)
  const plan = planLine.kind === 'plan' ? planLine.data : null
  const rec = buildPreparacion({
    fecha: new Date('2026-10-20T14:03:11.123Z'),
    tatanaVersion: '1.5.0',
    plan,
    resultado: 'ok',
    requiereReiniciarSesion: false,
    scriptSha256: 'a'.repeat(64),
    pasos: [{ id: 'paquetes', estado: 'ok', detalle: 'usbmuxd avahi-daemon' }],
  })
  assert.deepEqual(rec, {
    schema: 1, fecha: '2026-10-20T14:03:11Z', tatana_version: '1.5.0', distro: 'ubuntu 24.04', gestor: 'apt',
    resultado: 'ok', requiere_reiniciar_sesion: false, script_sha256: 'a'.repeat(64),
    pasos: [{ id: 'paquetes', estado: 'ok', detalle: 'usbmuxd avahi-daemon' }],
  })
  assert.deepEqual(parsePreparacion(JSON.stringify(rec)), rec)
  assert.equal(parsePreparacion('no json'), null)
  assert.equal(parsePreparacion('{"schema":2,"resultado":"ok"}'), null)
})

test('fallbackCommandText: ~/ bajo el home, comillas si hace falta', () => {
  assert.equal(
    fallbackCommandText('/home/perito/.config/Tatana/preparar-pc.sh', '/home/perito', 'perito'),
    'sudo bash ~/.config/Tatana/preparar-pc.sh --ejecutar --usuario perito')
  assert.equal(
    fallbackCommandText('/home/Juan Pérez/.config/Tatana/preparar-pc.sh', '/home/Juan Pérez', 'juan'),
    'sudo bash ~/.config/Tatana/preparar-pc.sh --ejecutar --usuario juan')
  assert.equal(
    fallbackCommandText('/srv/cfg con espacio/Tatana/preparar-pc.sh', '/home/perito', 'perito'),
    "sudo bash '/srv/cfg con espacio/Tatana/preparar-pc.sh' --ejecutar --usuario perito")
})

test('shellQuote', () => {
  assert.equal(shellQuote('perito'), 'perito')
  assert.equal(shellQuote("o'brien"), "'o'\\''brien'")
})
