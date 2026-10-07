/**
 * Cómo se pide `tatana-update.json` (sin Electron, lo testea `manifest-request.test.ts`).
 *
 * El manifiesto NUNCA sale de una caché HTTP. Con `cache: 'no-cache'`, Chromium revalida con
 * `If-Modified-Since`. Si el servidor contesta 304 (por ejemplo, un manifiesto restaurado con la
 * misma mtime, o dentro del mismo segundo), entrega el cuerpo viejo de la caché, aunque ya haya
 * sido rechazado por firma inválida. Pasó en el ensayo de CI, run 37637630657.
 * `no-store` no lee ni guarda en la caché, así que no manda pedidos condicionales. Las cabeceras
 * `Cache-Control`/`Pragma` piden lo mismo a los proxies del medio (D-T9: proxy del sistema).
 */

export const MANIFEST_NO_CACHE_HEADERS: Readonly<Record<string, string>> = Object.freeze({
  'Cache-Control': 'no-cache',
  Pragma: 'no-cache',
})

export interface ManifestRequestOptions {
  url: string
  method: 'GET'
  redirect: 'manual'
  useSessionCookies: false
  cache: 'no-store'
}

export function manifestRequestOptions(url: string): ManifestRequestOptions {
  return { url, method: 'GET', redirect: 'manual', useSessionCookies: false, cache: 'no-store' }
}

/**
 * Qué hacer con el status de la respuesta.
 * - `body`: 2xx, se lee el cuerpo.
 * - `not_modified`: 304. No hay cuerpo nuevo y no se reutiliza nada guardado, se trata como falla
 *   de red. No debería pasar, porque nunca se manda un pedido condicional, pero un proxy podría
 *   hacerlo.
 * - `error`: cualquier otro.
 */
export type ManifestStatusClass = 'body' | 'not_modified' | 'error'

export function classifyManifestStatus(statusCode: number): ManifestStatusClass {
  if (statusCode === 304) return 'not_modified'
  if (statusCode >= 200 && statusCode < 300) return 'body'
  return 'error'
}
