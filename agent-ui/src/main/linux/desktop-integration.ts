/**
 * Integración con el escritorio de Linux (SDD tatana-linux D-T6, D-T18, §8.4):
 *  - "Instalar en esta PC": copiar el AppImage a `~/Applications/Tatana.AppImage`;
 *  - acceso del menú + ícono, autoarranque XDG y `instalacion-linux.json`;
 *  - autorreparación en cada arranque desde la ruta canónica.
 *
 * Solo tiene sentido empaquetado en Linux (`process.env.APPIMAGE`); el caller
 * lo chequea. El texto de los `.desktop` es puro (`desktop-entry.ts`).
 */
import { app, nativeImage } from 'electron'
import { existsSync, mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'fs'
import { dirname, join } from 'path'
import semver from 'semver'
import { desktopEntryText } from './desktop-entry'
import { replaceFile } from './replace-file'
import { spawnRelaunch } from './relaunch'
import { autostartPath, canonicalAppImagePath, desktopEntryPath, iconPath } from './paths'

export interface InstalacionLinux {
  schema: 1
  fecha: string
  version: string
  ruta: string
}

/** Qué hacer con la instalación según el AppImage que corre (D-T6). */
export type InstallOffer =
  | { kind: 'none' }                       // corre desde la canónica, o no es un AppImage
  | { kind: 'install' }                    // no hay instalada
  | { kind: 'replace'; installed: string } // la que corre es más nueva que la instalada
  | { kind: 'open'; installed: string }    // ya hay una igual o más nueva: abrir esa

function instalacionPath(): string {
  return join(app.getPath('userData'), 'instalacion-linux.json')
}

/** Escritura atómica (tmp + rename). */
function writeAtomic(path: string, data: string | Buffer, mode?: number): void {
  mkdirSync(dirname(path), { recursive: true })
  const tmp = `${path}.tmp`
  writeFileSync(tmp, data, mode !== undefined ? { mode } : undefined)
  renameSync(tmp, path)
}

/** AppImage que corre (`$APPIMAGE`), o null fuera de un AppImage. */
export function runningAppImage(): string | null {
  const p = process.env['APPIMAGE']?.trim()
  return p ? p : null
}

/** `true` si Tatana corre desde `~/Applications/Tatana.AppImage`. */
export function isInstalled(): boolean {
  return runningAppImage() === canonicalAppImagePath()
}

export function readInstalacion(): InstalacionLinux | null {
  try {
    const p = instalacionPath()
    if (!existsSync(p)) return null
    const raw = JSON.parse(readFileSync(p, 'utf-8')) as Partial<InstalacionLinux>
    if (raw?.schema !== 1 || typeof raw.version !== 'string' || typeof raw.ruta !== 'string') return null
    return { schema: 1, fecha: String(raw.fecha ?? ''), version: raw.version, ruta: raw.ruta }
  } catch {
    return null
  }
}

function writeInstalacion(version: string): void {
  const data: InstalacionLinux = { schema: 1, fecha: new Date().toISOString(), version, ruta: canonicalAppImagePath() }
  writeAtomic(instalacionPath(), JSON.stringify(data, null, 2))
}

export function installOffer(): InstallOffer {
  const running = runningAppImage()
  const canonical = canonicalAppImagePath()
  if (!running || running === canonical) return { kind: 'none' }
  if (!existsSync(canonical)) return { kind: 'install' }
  const installed = readInstalacion()?.version ?? '0.0.0'
  const current = app.getVersion()
  if (semver.valid(current) && semver.valid(installed) && semver.gt(current, installed)) {
    return { kind: 'replace', installed }
  }
  return { kind: 'open', installed }
}

/** Ícono del menú: `resources/icon.png` (1024) escalado a 512 (§8.4). */
function writeIcon(): void {
  // Empaquetado viaja por `extraResources` (`<resources>/icon.png`).
  const candidates = app.isPackaged
    ? [join(process.resourcesPath, 'icon.png')]
    : [join(app.getAppPath(), 'resources', 'icon.png')]
  for (const c of candidates) {
    if (!existsSync(c)) continue
    const img = nativeImage.createFromPath(c)
    if (img.isEmpty()) continue
    const size = img.getSize()
    const png = (size.width > 512 ? img.resize({ width: 512, height: 512, quality: 'best' }) : img).toPNG()
    writeAtomic(iconPath(), png)
    return
  }
}

/**
 * Autoarranque XDG (D-T18): escribe o borra `~/.config/autostart/factum-tatana.desktop`.
 * Solo con la instalación canónica; sin instalar, no deja nada que apunte a un
 * archivo que se puede borrar o estar en un pendrive.
 */
export function applyAutostart(enabled: boolean): void {
  const path = autostartPath()
  try {
    if (enabled && isInstalled()) {
      writeAtomic(path, desktopEntryText({ exec: canonicalAppImagePath(), version: app.getVersion(), hidden: true }))
    } else if (!enabled) {
      rmSync(path, { force: true })
    }
  } catch (err) {
    console.error('[Tatana] No se pudo actualizar el autoarranque:', err)
  }
}

/**
 * Reescribe el acceso del menú, el ícono y el autoarranque (autorreparación en
 * cada arranque desde la canónica, D-T6). `autostart` = `config.autostart`.
 */
export function refreshDesktopEntries(autostart: boolean): void {
  if (!isInstalled()) return
  try {
    writeAtomic(desktopEntryPath(), desktopEntryText({ exec: canonicalAppImagePath(), version: app.getVersion(), hidden: false }))
  } catch (err) {
    console.error('[Tatana] No se pudo escribir el acceso del menú:', err)
  }
  try { writeIcon() } catch (err) { console.error('[Tatana] No se pudo escribir el ícono:', err) }
  applyAutostart(autostart)
}

/**
 * "Instalar (recomendado)" / "Reemplazar la instalada" (D-T6). Si sale bien,
 * relanza desde la canónica y sale; el archivo original NO se borra (puede
 * estar en un pendrive). `beforeRelaunch` corta el agente sin huérfanos.
 */
export async function installOnPc(opts: {
  autostart: boolean
  beforeRelaunch: () => Promise<void>
}): Promise<{ ok: boolean; error?: string }> {
  const running = runningAppImage()
  if (!running) return { ok: false, error: 'Tatana no está corriendo desde un AppImage.' }
  const canonical = canonicalAppImagePath()
  if (running === canonical) return { ok: true }
  try {
    replaceFile(running, canonical)
    writeInstalacion(app.getVersion())
  } catch (err) {
    const msg = err instanceof Error ? err.message : String(err)
    console.error('[Tatana] No se pudo instalar en esta PC:', err)
    return { ok: false, error: `No se pudo copiar Tatana a ${canonical}: ${msg}` }
  }
  // Los accesos se escriben con la canónica (todavía no corre desde ahí: se fuerza).
  try {
    writeAtomic(desktopEntryPath(), desktopEntryText({ exec: canonical, version: app.getVersion(), hidden: false }))
    writeIcon()
    if (opts.autostart) {
      writeAtomic(autostartPath(), desktopEntryText({ exec: canonical, version: app.getVersion(), hidden: true }))
    }
  } catch (err) {
    // No es fatal: el relanzado desde la canónica los reescribe (autorreparación).
    console.error('[Tatana] No se pudieron escribir los accesos del escritorio:', err)
  }
  await opts.beforeRelaunch()
  relaunchFrom(canonical)
  return { ok: true }
}

/** "Abrir la instalada": la lanza y sale (la instancia única pasa a ser la instalada). */
export async function openInstalled(beforeRelaunch: () => Promise<void>): Promise<void> {
  await beforeRelaunch()
  relaunchFrom(canonicalAppImagePath())
}

/**
 * Relanzar desde `execPath` y salir. En un AppImage NO se usa `app.relaunch`: su
 * relauncher corre desde el montaje FUSE de esta instancia, que se desmonta al
 * salir, y nunca lanza nada (ver `relaunch.ts`). Un `/bin/sh` despegado espera a
 * que salga este proceso (libera la instancia única) y hace `exec` del AppImage;
 * el runtime relanzado vuelve a fijar `APPIMAGE` al montar. Si no se puede lanzar,
 * cae a `app.relaunch`. `app.exit` (no `quit`): el caller ya cortó el agente, y así
 * el `close` de la ventana (que esconde) no frena la salida.
 */
export function relaunchFrom(execPath: string, extraArgs: string[] = [], log: (msg: string) => void = console.log): void {
  if (spawnRelaunch({ waitPid: process.pid, execPath, args: extraArgs })) {
    log(`Relanzando ${execPath} cuando salga el proceso ${process.pid}.`)
  } else {
    log(`No se pudo lanzar el relanzador; se usa app.relaunch (${execPath}).`)
    app.relaunch({ execPath, args: extraArgs })
  }
  app.exit(0)
}
