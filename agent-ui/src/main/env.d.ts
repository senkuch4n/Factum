/**
 * Clave pública extra para verificar `tatana-update.json`, inyectada por
 * `define` de electron-vite desde `TATANA_EXTRA_UPDATE_KEY` (solo ensayo de CI,
 * D-T23). Formato `key_id:spkiDerBase64`, o vacío. Fuera del build (tests con
 * tsx) no existe: usar `typeof` antes de leerla.
 */
declare const __TATANA_EXTRA_UPDATE_KEY__: string
