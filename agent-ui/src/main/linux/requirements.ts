/**
 * Requisitos mínimos de Linux (SDD tatana-linux D-T1, D-T2, §8.2).
 *
 * `glibcTooOld` y `requirementsMessage` son PUROS (los testea
 * `requirements.test.ts`). `checkLinuxRequirements` recibe `dialog` y `app` de
 * Electron por parámetro (solo tipos importados), así este módulo no carga
 * Electron en los tests.
 *
 * Por qué al principio de `ready` y no antes: en Linux, `dialog.showErrorBox`
 * llamado antes de `ready` solo escribe en stderr (documentación de Electron);
 * para que el perito vea el mensaje tiene que ser después de `ready` y antes de
 * crear la ventana.
 */
import type { App, Dialog } from 'electron'

/** glibc mínima publicada (E5: la pide scrcpy 4.1). */
export const MIN_GLIBC = '2.35'

function parts(v: string): number[] | null {
  const m = /^(\d+)\.(\d+)(?:\.(\d+))?/.exec(v.trim())
  return m ? [Number(m[1]), Number(m[2]), Number(m[3] ?? 0)] : null
}

/**
 * `true` si `version` es menor que `min`. Una versión que no se puede leer
 * (vacía, otro formato) NO bloquea: Tatana sigue y, si falta algo, el error lo
 * da el binario correspondiente.
 */
export function glibcTooOld(version: string | null | undefined, min: string = MIN_GLIBC): boolean {
  if (!version) return false
  const v = parts(version)
  const m = parts(min)
  if (!v || !m) return false
  for (let i = 0; i < 3; i++) {
    if (v[i] !== m[i]) return v[i] < m[i]
  }
  return false
}

/** Texto del diálogo (D-T2). */
export function requirementsMessage(version: string): string {
  return `Esta PC tiene una versión de Linux anterior a la que necesita Tatana (glibc ${version}; hace falta ` +
    `${MIN_GLIBC} o posterior, por ejemplo Ubuntu 22.04 o Debian 12).`
}

/** glibc de esta PC según el reporte de diagnóstico de Node (`header.glibcVersionRuntime`). */
export function glibcRuntimeVersion(): string | null {
  try {
    const report = process.report?.getReport() as unknown as { header?: { glibcVersionRuntime?: unknown } } | undefined
    const v = report?.header?.glibcVersionRuntime
    return typeof v === 'string' && v ? v : null
  } catch {
    return null
  }
}

/**
 * Chequeo de D-T2 (solo Linux empaquetado; el caller decide). Si la glibc es
 * menor que la mínima, muestra el error y sale. Devuelve `false` si la app
 * tiene que terminar (el caller no sigue con el arranque).
 */
export function checkLinuxRequirements(electron: { dialog: Dialog; app: App }): boolean {
  const version = glibcRuntimeVersion()
  if (!version || !glibcTooOld(version)) return true
  electron.dialog.showErrorBox('Tatana no puede abrir en esta PC', requirementsMessage(version))
  electron.app.exit(1)
  return false
}
