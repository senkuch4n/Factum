/**
 * Cierre de Tatana con una grabación de la sesión en curso (grabacion-sesion-completa
 * §10, DU3 = A). Electron mata al agente sin aviso (`agentProcess.stop()`); antes,
 * si hay una sesión grabando, se pregunta y se le pide a Tatana que cierre y guarde
 * el tramo (`POST /agent/prepare-shutdown`).
 */
import { BrowserWindow, dialog } from 'electron'
import { ACTIVE_SESSION_STATES, getAgentState, prepareShutdown } from './agent-api'
import { readConfig } from './config'

/** Timeout del `GET /agent/state` previo al diálogo: corto, para no demorar la salida. */
const STATE_TIMEOUT_MS = 2000

let inFlight: Promise<boolean> | null = null

/**
 * `true` = se puede cerrar o reiniciar Tatana. Sin sesión activa o sin respuesta
 * del agente, `true` sin diálogo. Con sesión: diálogo; "Cancelar" → `false`;
 * "Cerrar Tatana" → `prepareShutdown` (los errores se ignoran: la recuperación
 * al reabrir cubre el resto) → `true`.
 */
export function confirmSessionShutdown(): Promise<boolean> {
  // Dos clics seguidos (bandeja + segunda instancia con `--quit`): un solo diálogo.
  if (inFlight) return inFlight
  inFlight = (async () => {
    const port = readConfig().agentPort
    const state = await getAgentState(port, STATE_TIMEOUT_MS)
    const session = state?.recording_session
    if (!session || !ACTIVE_SESSION_STATES.has(session.state)) return true

    const options: Electron.MessageBoxOptions = {
      type: 'warning',
      title: 'Grabación de la sesión en curso',
      message: 'Hay una grabación de la sesión en curso.',
      detail: 'Si cerrás Tatana, la grabación se corta y queda registrado el hueco en el caso.',
      buttons: ['Cancelar', 'Cerrar Tatana'],
      defaultId: 0,
      cancelId: 0,
      noLink: true,
    }
    const parent = BrowserWindow.getFocusedWindow()
    const { response } = parent
      ? await dialog.showMessageBox(parent, options)
      : await dialog.showMessageBox(options)
    if (response !== 1) return false

    try {
      await prepareShutdown(port)
    } catch {
      /* se ignora: Tatana recupera el tramo al volver a abrirse */
    }
    return true
  })().finally(() => { inFlight = null })
  return inFlight
}
