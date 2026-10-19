/**
 * Rutas de Tatana en Linux (SDD tatana-linux D-T5, D-T6, D-T18, §8.2).
 *
 * PURO: sin Electron. Todo recibe el entorno y el home como parámetros (con
 * `process.env` y `os.homedir()` por defecto) para poder testearlo con
 * `tsx --test`. Respeta `XDG_CONFIG_HOME`, `XDG_DATA_HOME`, `HOME` y
 * `XDG_RUNTIME_DIR`; un `XDG_*` vacío o relativo se ignora, como manda la
 * especificación XDG Base Directory.
 */
import { homedir } from 'os'
import { posix } from 'path'

const { isAbsolute, join } = posix

export type Env = Readonly<Record<string, string | undefined>>

/** Nombre del archivo `.desktop` y del ícono (menú y autoarranque). */
export const DESKTOP_ID = 'factum-tatana'

function home(env: Env, fallbackHome: string): string {
  const h = env['HOME']?.trim()
  return h && isAbsolute(h) ? h : fallbackHome
}

function xdg(env: Env, name: string, fallback: string): string {
  const v = env[name]?.trim()
  return v && isAbsolute(v) ? v : fallback
}

/** `${XDG_CONFIG_HOME:-~/.config}` */
export function configHome(env: Env = process.env, fallbackHome: string = homedir()): string {
  return xdg(env, 'XDG_CONFIG_HOME', join(home(env, fallbackHome), '.config'))
}

/** `${XDG_DATA_HOME:-~/.local/share}` */
export function dataHome(env: Env = process.env, fallbackHome: string = homedir()): string {
  return xdg(env, 'XDG_DATA_HOME', join(home(env, fallbackHome), '.local', 'share'))
}

/** `XDG_RUNTIME_DIR` (o `undefined` si no está o no es absoluta). */
export function runtimeDir(env: Env = process.env): string | undefined {
  const v = env['XDG_RUNTIME_DIR']?.trim()
  return v && isAbsolute(v) ? v : undefined
}

/** Ruta canónica de la instalación (D-T6): `~/Applications/Tatana.AppImage`, sin versión en el nombre. */
export function canonicalAppImagePath(env: Env = process.env, fallbackHome: string = homedir()): string {
  return join(home(env, fallbackHome), 'Applications', 'Tatana.AppImage')
}

/** Carpeta de datos del agente empaquetado en Linux (D-T5): `${XDG_DATA_HOME:-~/.local/share}/Tatana/data`. */
export function dataDir(env: Env = process.env, fallbackHome: string = homedir()): string {
  return join(dataHome(env, fallbackHome), 'Tatana', 'data')
}

/** Acceso del menú: `${XDG_DATA_HOME:-~/.local/share}/applications/factum-tatana.desktop`. */
export function desktopEntryPath(env: Env = process.env, fallbackHome: string = homedir()): string {
  return join(dataHome(env, fallbackHome), 'applications', `${DESKTOP_ID}.desktop`)
}

/** Autoarranque (D-T18): `${XDG_CONFIG_HOME:-~/.config}/autostart/factum-tatana.desktop`. */
export function autostartPath(env: Env = process.env, fallbackHome: string = homedir()): string {
  return join(configHome(env, fallbackHome), 'autostart', `${DESKTOP_ID}.desktop`)
}

/** Ícono del menú: `~/.local/share/icons/hicolor/512x512/apps/factum-tatana.png`. */
export function iconPath(env: Env = process.env, fallbackHome: string = homedir()): string {
  return join(dataHome(env, fallbackHome), 'icons', 'hicolor', '512x512', 'apps', `${DESKTOP_ID}.png`)
}

/** Socket de PulseAudio/PipeWire de la sesión (`$XDG_RUNTIME_DIR/pulse/native`), o `undefined`. */
export function pulseSocketPath(env: Env = process.env): string | undefined {
  const r = runtimeDir(env)
  return r ? join(r, 'pulse', 'native') : undefined
}
