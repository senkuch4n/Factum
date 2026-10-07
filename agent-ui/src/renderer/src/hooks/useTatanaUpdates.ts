import { useEffect, useState } from 'react'

/**
 * Estado del actualizador y datos de la app (versión, avisos) que publica el
 * proceso main (SDD tatana-instalador-autoupdate §6.7). Un solo suscriptor
 * (App): los canales se limpian con `removeAllListeners`.
 */
export function useTatanaUpdates(): { appInfo: TatanaAppInfo | null; update: TatanaUpdateState | null } {
  const [appInfo, setAppInfo] = useState<TatanaAppInfo | null>(null)
  const [update, setUpdate] = useState<TatanaUpdateState | null>(null)

  useEffect(() => {
    if (!window.tatana?.getAppInfo) return // preload viejo o no disponible
    let active = true
    window.tatana.getAppInfo().then((i) => { if (active) setAppInfo(i) }).catch(console.error)
    window.tatana.getUpdateState().then((s) => { if (active) setUpdate(s) }).catch(console.error)
    window.tatana.onAppInfoChange(setAppInfo)
    window.tatana.onUpdateStateChange(setUpdate)
    return () => {
      active = false
      window.tatana.removeAllListeners('tatana:app-info-change')
      window.tatana.removeAllListeners('tatana:update-state-change')
    }
  }, [])

  return { appInfo, update }
}

/** Texto de la UI para cada `kind` de operación en curso (SDD §13.F). */
export function busyOperationsText(kinds: ReadonlyArray<string> | undefined): string {
  const text = (k: string) =>
    k === 'recording_android' || k === 'recording_ios' ? 'grabación en curso'
    : k === 'airplay_session' ? 'sesión AirPlay en curso'
    : k === 'video_postprocess' ? 'procesando un video'
    : 'operación en curso'
  return [...new Set((kinds ?? []).map(text))].join(', ')
}
