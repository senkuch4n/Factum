/**
 * Reemplazo atómico de un archivo ejecutable (SDD tatana-linux D-T6, D-T7):
 * `<dest>.nuevo` → `chmod 755` → `fsync` → `rename` sobre `dest`. Un proceso
 * que corre desde `dest` conserva el inodo viejo (FUSE), así que reemplazar la
 * instalación mientras corre es seguro. Sin Electron.
 */
import { chmodSync, closeSync, copyFileSync, fsyncSync, mkdirSync, openSync, renameSync, rmSync } from 'fs'
import { dirname } from 'path'

export function replaceFile(src: string, dest: string): void {
  mkdirSync(dirname(dest), { recursive: true })
  const tmp = `${dest}.nuevo`
  rmSync(tmp, { force: true })
  try {
    copyFileSync(src, tmp)
    chmodSync(tmp, 0o755)
    const fd = openSync(tmp, 'r')
    try { fsyncSync(fd) } finally { closeSync(fd) }
    renameSync(tmp, dest)
  } catch (err) {
    rmSync(tmp, { force: true })
    throw err
  }
  // fsync de la carpeta: que el rename sobreviva a un corte de luz.
  try {
    const dfd = openSync(dirname(dest), 'r')
    try { fsyncSync(dfd) } finally { closeSync(dfd) }
  } catch { /* no todos los FS lo permiten: no es fatal */ }
}
