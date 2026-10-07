import { resolve } from 'path'
import { readFileSync } from 'fs'
import { defineConfig, externalizeDepsPlugin } from 'electron-vite'
import react from '@vitejs/plugin-react'

// Versión de agent-ui (la misma que compara electron-updater), para el pie de
// la sidebar. No es la APP_VERSION de la web: son versiones distintas.
const { version: tatanaVersion } = JSON.parse(
  readFileSync(resolve(__dirname, 'package.json'), 'utf-8'),
) as { version: string }

export default defineConfig({
  main: {
    plugins: [externalizeDepsPlugin()],
    define: {
      // Clave extra de verificación del manifiesto, SOLO para el ensayo de CI
      // (D-T23): `key_id:spkiDerBase64` o vacío. En release el workflow exige vacío.
      __TATANA_EXTRA_UPDATE_KEY__: JSON.stringify(process.env.TATANA_EXTRA_UPDATE_KEY ?? ''),
    },
  },
  preload: {
    plugins: [externalizeDepsPlugin()],
  },
  renderer: {
    resolve: {
      alias: { '@': resolve('src/renderer/src') },
    },
    define: {
      __TATANA_VERSION__: JSON.stringify(tatanaVersion),
    },
    plugins: [react()],
  },
})
