/**
 * Instalación de la actualización en Linux (SDD tatana-linux D-T7), sin
 * `autoUpdater.quitAndInstall`:
 *   (1) con `isForceRunAfter`, `AppImageUpdater.doInstall` lanza el AppImage
 *       nuevo ANTES de que salga el viejo, y el `requestSingleInstanceLock()`
 *       hace que la instancia nueva se cierre sola;
 *   (2) sin él, hace `execFileSync` del AppImage nuevo con
 *       `APPIMAGE_EXIT_AFTER_INSTALL` (monta un FUSE solo para salir).
 *
 * Flujo: re-verificar `size` + `sha512` contra el manifiesto firmado → copiar a
 * `<canónica>.nuevo` → `chmod 755` → `fsync` → `rename` sobre la canónica (el
 * proceso que corre conserva el inodo viejo vía FUSE). Relanzar y salir lo
 * hace el caller (`update-manager.ts`), con Electron.
 *
 * Sin Electron: lo testea `appimage-install.test.ts` en una carpeta temporal.
 */
import { createReadStream, statSync } from 'fs'
import { createHash } from 'crypto'
import { replaceFile } from '../linux/replace-file'

export function sha512Base64(file: string): Promise<string> {
  return new Promise((resolve, reject) => {
    const hash = createHash('sha512')
    createReadStream(file)
      .on('data', (c) => hash.update(c))
      .on('end', () => resolve(hash.digest('base64')))
      .on('error', reject)
  })
}

/** `true` si el archivo tiene exactamente el tamaño y el sha512 (base64) del manifiesto. */
export async function matchesManifest(file: string, expected: { size: number; sha512: string }): Promise<boolean> {
  try {
    if (statSync(file).size !== expected.size) return false
    return (await sha512Base64(file)) === expected.sha512
  } catch {
    return false
  }
}

export type AppImageInstallResult = { ok: true } | { ok: false; error: string }

/**
 * Instala `downloadedFile` en `canonicalPath` si coincide con el manifiesto.
 * No relanza ni sale: eso es del caller.
 */
export async function installAppImage(opts: {
  downloadedFile: string
  canonicalPath: string
  expected: { size: number; sha512: string }
}): Promise<AppImageInstallResult> {
  if (!(await matchesManifest(opts.downloadedFile, opts.expected))) {
    return { ok: false, error: 'el AppImage descargado no coincide con el manifiesto firmado' }
  }
  try {
    replaceFile(opts.downloadedFile, opts.canonicalPath)
    return { ok: true }
  } catch (err) {
    return { ok: false, error: err instanceof Error ? err.message : String(err) }
  }
}
