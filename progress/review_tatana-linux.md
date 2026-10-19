# Review — tatana-linux (HU #91)

**Veredicto:** APROBADA

## Verificación propia
- `dotnet build Factum.Agent`: 0 warnings, 0 errores. `dotnet test Factum.Agent.Tests`: 979/979.
- `npx tsc --noEmit` en `client/`; `-p tsconfig.web.json` y `-p tsconfig.node.json` en `agent-ui/`: limpios.
- `npm test` en agent-ui: 68/68. `node ops/tatana/probar-firma.mjs` y `probar-preparar-pc.sh`: OK.
- `./ops/harness/verify.sh`: exit 0. No se corrió preparar-pc.sh en modo real; sin procesos vivos de la HU.

## Checkpoints
- C1: [x] verify.sh exit 0 (estado de Project y rama no re-verificados por el reviewer)
- C2: [x] HU, SDD y Contrato compartido presentes; §10.A (os/linux/checks, snake_case, glibc_version), §10.C (producto/file) y §10.E (variable Linux) coinciden entre C#, client/src/lib/agent.ts, agent-ui/agent-api.ts y ops/tatana/_lib.mjs
- C3: [x] Lados dentro de la SDD; main/preload/renderer respetado; sin Mongo; sin debug (los console.log son salida de CLIs ops)
- C4: [x] builds, tsc y tests reales y limpios
- C5: [x] progress impl_backend e impl_frontend existen; el frontend deja constancia de ui-ux-pro-max, senior-frontend, 3d-web-experience y web-design-guidelines

## Puntos especiales
- Contrato: consistente en los 4 lados.
- Windows/macOS: tests de manifest/updater en verde; `validatePayload` con producto esperado por plataforma (Windows sigue igual); `verifyEnvelope` con default `expectedProduct()`; electron-builder 26.15.3 fijado en package.json y lock (app-builder-lib 26.15.3); sin postinstall ni parchar-electron-builder.mjs ni referencias restantes; release sin el paso del parche.
- preparar-pc.sh: viaja embebido y por stdin (sin archivo temporal), `main()` al final, verifica root y coincidencia con PKEXEC_UID, usuario validado con getent, escritura atómica de solo los dos archivos listados, sin eval ni interpolación en comandos, SHA-256 del script registrado en el log y en preparacion-linux.json, PATH fijo, salida con prefijo TATANA_ neutralizado en el log.
- Versiones fijas con SHA-256 en deploy/linux/armar-tatana-linux.sh y lock con hashes (verificado por generar-lock.py --check según el informe).

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- `agent-ui/tsconfig.*.tsbuildinfo` figuran modificados (artefactos versionados); conviene no incluirlos en el commit.
- Los workflows de GitHub Actions (tatana-linux.yml, tatana-release.yml en ensayo) y el AppImage en una PC real no se pudieron correr localmente; quedan para el PR y la QA manual (§11).
- `StartupWMClass`/`desktopName` pendiente de confirmar con xprop en la QA, como deja la SDD.
