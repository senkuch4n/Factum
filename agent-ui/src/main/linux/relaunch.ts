/**
 * Relanzar Tatana desde un AppImage en Linux (SDD tatana-linux D-T6, D-T7).
 *
 * `app.relaunch()` NO sirve dentro de un AppImage: Electron arranca un proceso
 * "relauncher" con `CHILD_PROCESS_EXE` = `/proc/self/exe`, que es el binario
 * DENTRO del montaje FUSE del AppImage viejo (`/tmp/.mount_Tatan…/tatana`). El
 * relauncher espera a que muera el padre (PR_SET_PDEATHSIG) y recién ahí lanza
 * el programa; pero al morir el padre el runtime del AppImage desmonta el FUSE, y
 * el relauncher ya no puede leer su propio código: nunca lanza nada. Medido en el
 * ensayo de release (run 39191591887): la canónica quedó en v0.9.0 a los 2 s y
 * ningún proceso nuevo arrancó en 3 minutos.
 *
 * En su lugar, un `/bin/sh` (vive en el disco, no en el montaje) despegado
 * (`setsid`) espera a que salga el proceso viejo —así se libera el
 * `requestSingleInstanceLock()`— y hace `exec` del AppImage. Si el viejo no sale
 * en `timeoutS`, lanza igual.
 *
 * PURO: sin Electron, testeable con `tsx --test` (`relaunch.test.ts`).
 */
import { spawn as nodeSpawn, type SpawnOptions } from 'child_process'
import { existsSync } from 'fs'

export type Env = Readonly<Record<string, string | undefined>>

/** `/bin/sh -c` portable (dash/bash/busybox). `$1` pid, `$2` segundos, el resto: programa y argumentos. */
export const RELAUNCH_SCRIPT = [
  // Fds heredados del runtime del AppImage viejo (pipe de keepalive): que no sostengan su montaje.
  'exec 3>&- 4>&- 5>&- 6>&- 7>&- 8>&- 9>&-',
  'pid=$1; max=$2; shift 2',
  // Vivo = existe y no es zombie (con /proc); sin /proc, kill -0.
  'alive() {',
  '  if [ -d /proc/self ]; then',
  '    st=$(sed "s/^.*) //" "/proc/$1/stat" 2>/dev/null) || return 1',
  '    case "$st" in ""|Z*|X*) return 1 ;; esac',
  '    return 0',
  '  fi',
  '  kill -0 "$1" 2>/dev/null',
  '}',
  'i=0',
  'while alive "$pid" && [ "$i" -lt "$max" ]; do sleep 1; i=$((i+1)); done',
  'exec "$@"',
].join('\n')

export const RELAUNCH_TIMEOUT_S = 30

/**
 * Entorno para el relanzado. Electron reemplaza `XDG_CURRENT_DESKTOP` (lo guarda
 * en `ORIGINAL_XDG_CURRENT_DESKTOP`): se restaura. Las variables del AppImage
 * (`APPIMAGE`, `APPDIR`, `ARGV0`, `OWD`) las vuelve a fijar el runtime nuevo.
 */
export function relaunchEnv(env: Env): Record<string, string> {
  const out: Record<string, string> = {}
  for (const [k, v] of Object.entries(env)) if (v !== undefined) out[k] = v
  const original = out['ORIGINAL_XDG_CURRENT_DESKTOP']
  if (original !== undefined) {
    out['XDG_CURRENT_DESKTOP'] = original
    delete out['ORIGINAL_XDG_CURRENT_DESKTOP']
  }
  for (const k of ['APPIMAGE', 'APPDIR', 'ARGV0', 'OWD']) delete out[k]
  return out
}

export interface RelaunchCommand {
  command: string
  args: string[]
}

export function relaunchCommand(opts: {
  waitPid: number
  execPath: string
  args?: readonly string[]
  timeoutS?: number
}): RelaunchCommand {
  const timeout = Math.max(0, Math.floor(opts.timeoutS ?? RELAUNCH_TIMEOUT_S))
  return {
    command: '/bin/sh',
    args: ['-c', RELAUNCH_SCRIPT, 'tatana-relaunch', String(opts.waitPid), String(timeout), opts.execPath, ...(opts.args ?? [])],
  }
}

type SpawnFn = (command: string, args: string[], options: SpawnOptions) => { unref(): void; on(ev: 'error', cb: (e: Error) => void): unknown }

/**
 * Lanza el relanzador despegado (no muere con el grupo de la app) y sin stdio.
 * Devuelve `false` si no se pudo lanzar (el caller cae a `app.relaunch`).
 */
export function spawnRelaunch(
  opts: { waitPid: number; execPath: string; args?: readonly string[]; timeoutS?: number; env?: Env },
  spawnFn: SpawnFn = nodeSpawn as unknown as SpawnFn,
  exists: (path: string) => boolean = existsSync,
): boolean {
  const { command, args } = relaunchCommand(opts)
  // Un ENOENT de spawn llega asíncrono, cuando la app ya salió: se chequea antes.
  if (!exists(command)) return false
  try {
    const child = spawnFn(command, args, {
      detached: true,
      stdio: 'ignore',
      env: relaunchEnv(opts.env ?? process.env),
    })
    child.on('error', (err) => console.error('[Tatana] El relanzador falló:', err))
    child.unref()
    return true
  } catch (err) {
    console.error('[Tatana] No se pudo lanzar el relanzador:', err)
    return false
  }
}
