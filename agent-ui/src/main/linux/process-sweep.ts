/**
 * Barrido de procesos huérfanos de Tatana en Linux (SDD tatana-linux D-T17, paso 4).
 * Equivalente al `Get-Process | Where Path -like '<binDir>\*'` de Windows: los
 * procesos del mismo usuario cuyo `/proc/<pid>/exe` cae bajo `binDir/`
 * (adb, scrcpy, ffmpeg, python del agente) reciben SIGTERM y, 2 s después,
 * SIGKILL los que sigan vivos. El montaje `/tmp/.mount_*` es único por
 * instancia, así que no puede tocar procesos ajenos.
 *
 * `selectOrphans` es PURO (lo testea `process-sweep.test.ts`). Sin Electron.
 */
import { readdirSync, readlinkSync, statSync } from 'fs'
import { posix } from 'path'

export interface ProcEntry {
  pid: number
  /** Destino de `/proc/<pid>/exe` (puede terminar en ` (deleted)`). */
  exe: string
  /** Dueño del proceso (uid de `/proc/<pid>`). */
  uid: number
}

const DELETED = ' (deleted)'

/** Ruta del ejecutable sin el sufijo ` (deleted)` que pone el kernel si el archivo ya no existe. */
export function cleanExe(exe: string): string {
  return exe.endsWith(DELETED) ? exe.slice(0, -DELETED.length) : exe
}

/**
 * pids a cortar: del mismo usuario, con el ejecutable bajo `binDir/` y que no
 * sean el propio proceso. `binDir` vacío o relativo no selecciona nada (evita
 * barrer todo por un error).
 */
export function selectOrphans(
  entries: ReadonlyArray<ProcEntry>, binDir: string, selfPid: number, uid: number,
): number[] {
  const base = posix.normalize(binDir).replace(/\/+$/, '')
  if (!base || !posix.isAbsolute(base) || base === '/') return []
  const prefix = `${base}/`
  return entries
    .filter((e) => e.pid !== selfPid && e.pid > 1 && e.uid === uid && cleanExe(e.exe).startsWith(prefix))
    .map((e) => e.pid)
}

/** Procesos visibles en `/proc` con su ejecutable (los ajenos dan EACCES y se saltean). */
export function listProcesses(procRoot = '/proc'): ProcEntry[] {
  const out: ProcEntry[] = []
  let names: string[]
  try { names = readdirSync(procRoot) } catch { return out }
  for (const name of names) {
    if (!/^\d+$/.test(name)) continue
    const pid = Number(name)
    try {
      const uid = statSync(`${procRoot}/${name}`).uid
      const exe = readlinkSync(`${procRoot}/${name}/exe`)
      out.push({ pid, exe, uid })
    } catch { /* proceso que terminó o de otro usuario */ }
  }
  return out
}

function alive(pid: number): boolean {
  try { process.kill(pid, 0); return true } catch { return false }
}

function signal(pid: number, sig: NodeJS.Signals): void {
  try { process.kill(pid, sig) } catch { /* ya salió */ }
}

/**
 * Barre los procesos que quedaron bajo `binDir`. Devuelve los pids a los que se
 * les mandó señal (para el log). No tira.
 */
export async function sweep(binDir: string, graceMs = 2000): Promise<number[]> {
  if (process.platform !== 'linux') return []
  const uid = typeof process.getuid === 'function' ? process.getuid() : -1
  const pids = selectOrphans(listProcesses(), binDir, process.pid, uid)
  if (pids.length === 0) return []
  for (const pid of pids) signal(pid, 'SIGTERM')
  await new Promise((r) => setTimeout(r, graceMs))
  for (const pid of pids) if (alive(pid)) signal(pid, 'SIGKILL')
  return pids
}
