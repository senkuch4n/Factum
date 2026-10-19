/**
 * ¿Hay bandeja del sistema en esta sesión de Linux? (SDD tatana-linux D-T18).
 *
 * Electron dibuja la bandeja en Linux con StatusNotifierItem: hace falta que
 * alguien tenga el nombre DBus `org.kde.StatusNotifierWatcher` (KDE, Cinnamon,
 * Ubuntu con AppIndicator…). GNOME sin la extensión no lo tiene: ahí cerrar la
 * ventana la minimiza en vez de esconderla. Sin `gdbus` (o si no responde) se
 * asume que hay bandeja, como hoy. Sin Electron.
 */
import { execFile } from 'child_process'

const TIMEOUT_MS = 2000

/** Interpreta la respuesta de `gdbus … NameHasOwner` (`(true,)` / `(false,)`). PURO. */
export function parseNameHasOwner(stdout: string): boolean | null {
  const s = stdout.trim()
  if (/^\(\s*true\s*,?\s*\)$/.test(s)) return true
  if (/^\(\s*false\s*,?\s*\)$/.test(s)) return false
  return null
}

let cached: Promise<boolean> | null = null

export function hasStatusNotifierWatcher(): Promise<boolean> {
  if (process.platform !== 'linux') return Promise.resolve(true)
  cached ??= new Promise<boolean>((resolve) => {
    try {
      execFile('gdbus', [
        'call', '--session',
        '--dest', 'org.freedesktop.DBus',
        '--object-path', '/org/freedesktop/DBus',
        '--method', 'org.freedesktop.DBus.NameHasOwner',
        'org.kde.StatusNotifierWatcher',
      ], { timeout: TIMEOUT_MS }, (err, stdout) => {
        if (err) { resolve(true); return }
        resolve(parseNameHasOwner(String(stdout)) ?? true)
      })
    } catch {
      resolve(true)
    }
  })
  return cached
}
