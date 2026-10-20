/**
 * Texto de los `.desktop` de Tatana (menú y autoarranque), SDD tatana-linux §8.4.
 *
 * PURO (lo testea `desktop-entry.test.ts`); la escritura a disco está en
 * `desktop-integration.ts`.
 */
import { DESKTOP_ID } from './paths'

/**
 * `WM_CLASS` de la ventana: Electron usa `app.name` (= `productName` "Tatana").
 * La SDD pide confirmarlo con `xprop WM_CLASS` en la QA con hardware (§11).
 */
export const STARTUP_WM_CLASS = 'Tatana'

/**
 * Argumento entre comillas según la especificación Desktop Entry ("The Exec
 * key"): dentro de comillas dobles se escapan `"`, `` ` ``, `$` y `\` con `\`.
 * Después se aplica el escape general de los valores string (`\` → `\\`), y un
 * `%` literal se escribe `%%` (si no, es un código de campo).
 */
export function quoteExecArg(arg: string): string {
  const quoted = arg.replace(/(["`$\\])/g, '\\$1')
  return `"${quoted.replace(/\\/g, '\\\\').replace(/%/g, '%%')}"`
}

/** Valor de una clave string: sin saltos de línea (rompen el archivo). */
function oneLine(v: string): string {
  return v.replace(/[\r\n]+/g, ' ').trim()
}

export interface DesktopEntryOptions {
  /** Ruta absoluta del AppImage canónico. */
  exec: string
  /** Versión de Tatana (`X-AppImage-Version`). */
  version: string
  /** `true` = entrada de autoarranque (`--hidden` + `X-GNOME-Autostart-enabled`). */
  hidden: boolean
}

export function desktopEntryText({ exec, version, hidden }: DesktopEntryOptions): string {
  const lines = [
    '[Desktop Entry]',
    'Type=Application',
    'Name=Tatana',
    'Comment=Agente de Factum para capturar evidencia de celulares',
    `Exec=${quoteExecArg(exec)} ${hidden ? '--hidden' : '%U'}`,
    `Icon=${DESKTOP_ID}`,
    'Terminal=false',
    'Categories=Utility;',
    `StartupWMClass=${STARTUP_WM_CLASS}`,
    `X-AppImage-Version=${oneLine(version)}`,
  ]
  if (hidden) lines.push('X-GNOME-Autostart-enabled=true')
  return lines.join('\n') + '\n'
}
