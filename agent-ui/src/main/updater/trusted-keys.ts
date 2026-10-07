/**
 * Claves públicas Ed25519 en las que Tatana confía para verificar
 * `tatana-update.json` (SDD tatana-instalador-autoupdate §7, D-T6).
 *
 * Cómo completarlo (paso manual del usuario, SDD §18.1):
 *   node ops/tatana/generar-clave-firma.mjs --key-id tatana-2026-10 --salida ~/Seguro/tatana-firma.pem
 * imprime una línea como
 *   { keyId: 'tatana-2026-10', spkiDerBase64: 'MCowBQYDK2VwAyEA…' },
 * que se pega TAL CUAL dentro del array. Solo va la clave PÚBLICA (SPKI DER
 * en base64): la privada vive en el secreto `TATANA_FIRMA_CLAVE_PRIVADA` del
 * environment `tatana-release` y nunca entra al repo.
 *
 * Formato fijo (una entrada por línea, comillas simples): lo leen con un regex
 * simple `ops/tatana/firmar-manifiesto.mjs` y `verificar-manifiesto.mjs`.
 *
 * Rotación: publicar una versión que confíe en la vieja Y en la nueva (firmada
 * con la vieja), esperar a que las PCs actualicen, firmar con la nueva y recién
 * ahí sacar la vieja.
 *
 * Con el array vacío el actualizador queda `disabled` (motivo `sin_claves`).
 */

import type { TrustedKey } from './manifest'

export const TRUSTED_UPDATE_KEYS: ReadonlyArray<TrustedKey> = [
  { keyId: 'tatana-2026-10', spkiDerBase64: 'MCowBQYDK2VwAyEA6qowB0nU0nTcEMGoja+bR+Pu7/NY2Ea3GieExp15Oyw=' },
]

/** `key_id:spkiDerBase64` → TrustedKey, o null si está vacío o mal formado. */
export function parseExtraKey(raw: string | undefined | null): TrustedKey | null {
  const s = (raw ?? '').trim()
  const i = s.indexOf(':')
  if (i <= 0 || i === s.length - 1) return null
  return { keyId: s.slice(0, i), spkiDerBase64: s.slice(i + 1) }
}

/**
 * Claves horneadas + la clave efímera del ensayo de CI
 * (`__TATANA_EXTRA_UPDATE_KEY__`, D-T23), si el build la trae.
 */
export function allTrustedKeys(): TrustedKey[] {
  const extra = parseExtraKey(
    typeof __TATANA_EXTRA_UPDATE_KEY__ === 'string' ? __TATANA_EXTRA_UPDATE_KEY__ : '',
  )
  return extra ? [...TRUSTED_UPDATE_KEYS, extra] : [...TRUSTED_UPDATE_KEYS]
}
