// Tests de linux/paths.ts (SDD tatana-linux F2): XDG, HOME con espacios y valores inválidos.
import { test } from 'node:test'
import assert from 'node:assert/strict'
import {
  autostartPath, canonicalAppImagePath, configHome, dataDir, dataHome, desktopEntryPath,
  iconPath, pulseSocketPath, runtimeDir,
} from './paths'

const FALLBACK = '/home/fallback'

test('sin XDG: usa ~/.config y ~/.local/share', () => {
  const env = { HOME: '/home/perito' }
  assert.equal(configHome(env, FALLBACK), '/home/perito/.config')
  assert.equal(dataHome(env, FALLBACK), '/home/perito/.local/share')
  assert.equal(dataDir(env, FALLBACK), '/home/perito/.local/share/Tatana/data')
  assert.equal(canonicalAppImagePath(env, FALLBACK), '/home/perito/Applications/Tatana.AppImage')
  assert.equal(desktopEntryPath(env, FALLBACK), '/home/perito/.local/share/applications/factum-tatana.desktop')
  assert.equal(autostartPath(env, FALLBACK), '/home/perito/.config/autostart/factum-tatana.desktop')
  assert.equal(iconPath(env, FALLBACK), '/home/perito/.local/share/icons/hicolor/512x512/apps/factum-tatana.png')
})

test('XDG_CONFIG_HOME y XDG_DATA_HOME absolutos se respetan', () => {
  const env = { HOME: '/home/perito', XDG_CONFIG_HOME: '/srv/cfg', XDG_DATA_HOME: '/srv/data' }
  assert.equal(autostartPath(env, FALLBACK), '/srv/cfg/autostart/factum-tatana.desktop')
  assert.equal(dataDir(env, FALLBACK), '/srv/data/Tatana/data')
  assert.equal(desktopEntryPath(env, FALLBACK), '/srv/data/applications/factum-tatana.desktop')
  // La instalación canónica depende de HOME, no de XDG.
  assert.equal(canonicalAppImagePath(env, FALLBACK), '/home/perito/Applications/Tatana.AppImage')
})

test('XDG vacío o relativo se ignora (especificación XDG)', () => {
  const env = { HOME: '/home/perito', XDG_CONFIG_HOME: '', XDG_DATA_HOME: 'relativo/data' }
  assert.equal(configHome(env, FALLBACK), '/home/perito/.config')
  assert.equal(dataHome(env, FALLBACK), '/home/perito/.local/share')
})

test('HOME con espacios', () => {
  const env = { HOME: '/home/Juan Pérez' }
  assert.equal(canonicalAppImagePath(env, FALLBACK), '/home/Juan Pérez/Applications/Tatana.AppImage')
  assert.equal(dataDir(env, FALLBACK), '/home/Juan Pérez/.local/share/Tatana/data')
})

test('sin HOME: usa el home de respaldo', () => {
  assert.equal(canonicalAppImagePath({}, FALLBACK), '/home/fallback/Applications/Tatana.AppImage')
})

test('XDG_RUNTIME_DIR y socket de PulseAudio', () => {
  assert.equal(runtimeDir({}), undefined)
  assert.equal(pulseSocketPath({ XDG_RUNTIME_DIR: '/run/user/1000' }), '/run/user/1000/pulse/native')
  assert.equal(pulseSocketPath({ XDG_RUNTIME_DIR: 'run' }), undefined)
})
