/**
 * Siembra de un archivo de configuración (SDD tatana-linux D-T5, §11 humo (e)):
 * si `path` no existe, crea la carpeta y lo escribe de forma atómica
 * (`<path>.tmp` + `rename`); si ya existe, no lo toca. Sin Electron, para
 * poder testearlo con `tsx --test`.
 *
 * @returns `true` si lo creó, `false` si ya existía.
 */
import { existsSync, mkdirSync, renameSync, rmSync, writeFileSync } from 'fs'
import { dirname } from 'path'

export function ensureFile(path: string, content: string): boolean {
  if (existsSync(path)) return false
  mkdirSync(dirname(path), { recursive: true })
  const tmp = `${path}.tmp`
  try {
    // Un .tmp huérfano de un arranque cortado se descarta.
    rmSync(tmp, { force: true })
    writeFileSync(tmp, content, 'utf-8')
    // Otra instancia pudo sembrarlo en el medio: no se pisa lo suyo.
    if (existsSync(path)) {
      rmSync(tmp, { force: true })
      return false
    }
    renameSync(tmp, path)
    return true
  } catch (err) {
    rmSync(tmp, { force: true })
    throw err
  }
}
