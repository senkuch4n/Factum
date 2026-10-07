# Implementación frontend — tatana-instalador-autoupdate (#21)

**Estado:** done · **Rama:** `feat/tatana-instalador-autoupdate` (sin commit) · **Fecha:** 2026-10-07
**Alcance:** checklist §15.2 (F1–F28) de `Refactorizaciones/tatana-instalador-autoupdate.md`. Apps: `agent-ui/` y `client/`. No se tocó `server/`.

## Skills invocados (Skill tool)

| Skill | Uso / hallazgos aplicados |
|---|---|
| `ui-ux-pro-max` | Antes del JSX. Consultas `--domain ux`: "disabled button reason" (deshabilitado con `opacity-50 cursor-not-allowed` y **motivo visible**, no solo hover), "status message live region" (`role="status" aria-atomic="true"` en la línea de estado, una sola región), "progress indicator percent" (barra `role="progressbar"` con `aria-valuenow`; foco visible en todo control). "dismissible alert banner" dio 0 resultados; reintento "close button icon label" sin match específico → para los avisos cerrables se usaron defaults generales (botón de ícono con `aria-label`), dicho explícitamente. |
| `senior-frontend` | Patrón de un solo suscriptor IPC (`useTatanaUpdates` en `App`, limpieza con `removeAllListeners`), hooks antes de returns tempranos, lógica pura testeable separada (`manifest.ts`, `agent-version.ts`). |
| `3d-web-experience` | Criterio: **no se agregó 3D ni efectos pseudo-3D**. Las únicas animaciones nuevas son fade de avisos y la barra de progreso (con propósito). |
| `mblode-agent-skills-ui-animation` | Avisos: solo `opacity`, entrada 200 ms con curva enter `cubic-bezier(0.22,1,0.36,1)`, salida más rápida (120 ms); `useReducedMotion` → 0 ms y sin `layout`. Barra de descarga con `transform: scaleX` + `origin-left` (no `width`); `motion-reduce:transition-none`; spinners con `motion-reduce:animate-none`. |
| `ui-styling` | No se invocó: no hay shadcn y el cambio sigue las utilidades Tailwind + variables CSS existentes del agent-ui. |
| `web-design-guidelines` | Autochequeo final sobre `UpdateStatus.tsx`, `Sidebar.tsx`, `Dashboard.tsx` (guías bajadas de vercel-labs). Corregido: hover en los dos botones nuevos (el de "Buscar" pasó el bg a clase para que `hover:` funcione), `translate="no"` en rutas, `\u00a0` antes de `%`, `…` en estados de carga, íconos decorativos con `aria-hidden`, botón de cerrar con `aria-label`, foco visible (`.focus-ring:focus-visible` nuevo en `globals.css`; antes no había ningún estilo de foco). Pendiente fuera de alcance: los botones preexistentes del Dashboard tampoco tienen foco visible/hover; al cerrar un aviso el foco cae al body (no hay un destino obvio). |

## Archivos tocados

### agent-ui — instalador (F1–F5)
- `agent-ui/package.json`: `productName` de primer nivel; script `test` (`tsx --test src/main/updater/*.test.ts`); deps `semver@^7` (antes solo transitiva, la top-level era la 6 de babel), dev `@types/semver`, `tsx`; `build` según §6.1 (`buildResources: build`, `dist-config` en `extraResources`, `publish.url` `https://factum.invalid/…`, `win.target` nsis x64, `artifactName` `Tatana-Setup-${version}.${ext}`, bloque `nsis` con `deleteAppDataOnUninstall: false`). `appId` sigue `com.factum.tatana`. `package-lock.json` actualizado por `npm install`.
- `agent-ui/.gitignore` (nuevo): `resources/dist-config/tatana-dist.json`, `dist-base/`.
- `agent-ui/resources/dist-config/tatana-dist.example.json` (nuevo, versionado).
- `agent-ui/build/installer.nsh` (nuevo): `customInit` (Detectar → 0/10/20/30 con los textos de §6.3, Migrar con `-Silencioso 0|1`, `SetErrorLevel` + `Quit`, después `DetenerInstalado -Carpeta "$INSTDIR"`) y `customUnInit`. Sin `customUnInstall`.
- `agent-ui/build/migrar-portable.ps1` (nuevo, PS 5.1): `Detectar | Migrar | DetenerInstalado`; Migrar en el orden de §6.3 (detener, copiar config local con verificación SHA-256 y `conflicto`, `Move-Item` con 3 intentos → `exit 40`, mover `Startup\Tatana.lnk` si apunta al portátil, escribir `migracion-portable.json` §13.E sin BOM). Códigos extra: 41 (falló la copia de la config; se borra solo la copia que hizo el script), 42 (Migrar sin portátil para la nube). Log en `%APPDATA%\Tatana\logs\instalador.log`. Cabecera con la regla "nunca toca `%LOCALAPPDATA%\Tatana\data` ni `C:\Factum\Evidencia`".
  - Los dos archivos van con **BOM UTF-8**: NSIS y PowerShell 5.1 leen sin BOM como ANSI y romperían los acentos de los `MessageBox`.
- `agent-ui/electron.vite.config.ts`: define `__TATANA_EXTRA_UPDATE_KEY__` en el build de main. `agent-ui/src/main/env.d.ts` (nuevo): su `declare`.

### agent-ui — main (F6–F17)
- `src/main/index.ts`: `app.setPath('userData', appData/Tatana)` antes de `ready`; `requestSingleInstanceLock` + `second-instance` (`--quit` = Salir, `--install-update` = Reiniciar y actualizar, sin args = mostrar); `--hidden`; bandeja con tooltip `Tatana vX.Y.Z[ — Actualización lista: vA.B.C]` y menú de §6.6 (Abrir Factum oculto sin `client_url`, Buscar actualizaciones, Reiniciar y actualizar visible solo en `ready` y deshabilitado con "(esperando: …)", Reiniciar agente, Salir → `installNow({relaunch:false})` si está `ready`); `createTray` en `try`; se borró el bloque viejo de `autoUpdater` y `checkForUpdatesAndNotify`; `updateManager.init()` antes de `agentProcess.start()`.
- `src/main/config.ts`: defaults como función (`autostart = app.isPackaged`, `serverUrl = client_url ?? http://localhost:5000`), `agentDataDir?` y `dismissedLocalConfigNotice?`; `applyLoginItem()` solo empaquetado con `args: ['--hidden']` (también desde `ipc.ts` `save-config`).
- `src/main/agent-process.ts`: `agentDataDir()` (D-T3), `localConfigPath()`, args `--port --data --local-config [--mode installed]`, `stopAndWait()` (`taskkill /T /F` en Windows), `binaryDir()`.
- `src/main/agent-api.ts` (nuevo): `getAgentState`, `enterMaintenance`, `exitMaintenance` con `http`, sin `Origin`, timeout 3 s y los tipos de §13.A.
- `src/main/updater/trusted-keys.ts` (nuevo): `TRUSTED_UPDATE_KEYS = []` con instrucciones, `parseExtraKey`, `allTrustedKeys()`. Verificado que `ops/tatana/_lib.mjs` `readTrustedKeys()` del backend lo parsea (`[]`).
- `src/main/updater/manifest.ts` (nuevo, puro) + `manifest.test.ts` (11 tests).
- `src/main/updater/dist-config.ts`, `state.ts` (tipos §13.F, escritura atómica tmp+rename, textos de `busyOperations`), `logger.ts` (rota a 1 MB, es `autoUpdater.logger`, va a la ventana).
- `src/main/updater/update-manager.ts` (nuevo): §6.5 completo (disabled por `no_empaquetado`/`sin_claves`/`sin_canal`; chequeo a los 10 s, cada 6 h y en `resume`; por URL: manifiesto → firma → `decide` → `setFeedURL`+`checkForUpdates` → `matchesUpdateInfo` → `downloadUpdate` con `percent` → re-verificación sha512+tamaño → `ready`; `offline` vs `rejected`; notificación una vez por versión con `notifiedVersion`; `installNow` con maintenance 409 → `busy`; arranque con `ready` previo con presupuesto de 8 s; arranque tras `installing` → "Tatana se actualizó a vX" y `up_to_date`).
- `src/main/app-info.ts` (nuevo): `TatanaAppInfo` (versión de app y agente, `clientUrl`, avisos de migración y de config local), poll de `/agent/state` cada 15 s que también alimenta `busyOperations`, `dismiss()`.
- `src/main/ipc.ts` + `src/preload/index.ts` + `src/renderer/src/env.d.ts`: canales de §6.7 (`tatana:get-app-info`, `get-update-state`, `check-updates`, `install-update`, `dismiss-notice`, `update-state-change`, `app-info-change`).

### agent-ui — renderer (F18–F20)
- `components/Sidebar.tsx`: versión de la app (`appInfo.appVersion`, fallback `__TATANA_VERSION__`) y "Agente vX" si difiere; texto en `--text-secondary` (el `--text-muted` anterior no llegaba a contraste).
- `components/UpdateStatus.tsx` (nuevo): `UpdateStatusLine` (textos exactos por fase de §6.8, botón "Reiniciar y actualizar" deshabilitado con el motivo visible y `aria-describedby`, "Buscar actualizaciones" en `idle/up_to_date/offline/rejected/error`, barra de progreso) y `TatanaNotices` (migración, D9, config inválida; cerrables, sin modales).
- `components/Dashboard.tsx`: monta los dos debajo de las tarjetas de estado. `App.tsx`: `useTatanaUpdates` (nuevo, `hooks/useTatanaUpdates.ts`). `styles/globals.css`: `.focus-ring`.

### client (F21–F27)
- `src/lib/api.ts`: `PublicConfig.tatana_min_version?: string | null`.
- `src/hooks/usePublicConfig.ts`: `tatanaMinVersion` (no string o vacío → `null`) y `export loadPublicConfig`.
- `src/lib/agent-version.ts` (nuevo, puro): `meetsMinVersion(health, min)` y `hasRealVersion`. Sin `real_version_v1` → false; semver `X.Y.Z[-pre]` a mano; no parsea → false.
- `src/lib/agent.ts`: `AgentErrorCode` += `"agent_updating"`; doc de `capabilities` con `real_version_v1` y `agent_state_v1`.
- `src/hooks/useAgentIdentity.ts`: `outdatedReason`, `minVersion`, `versionIsReal`; `fetchIdentity` espera `health` + `loadPublicConfig()`; nuevo `canReadEvidence()`.
- `src/lib/agent-messages.ts`: `agentStatusMessage(status, identity?)`, `agentMinVersionMessage()`, `agent_updating` en `agentErrorMessage`.
- Call sites: `app/dashboard/page.tsx` (×2), `components/ReportStep.tsx`, `hooks/useFileManager.ts`.
- Sin cambios de JSX en `client/`: el bloqueo se muestra con los banners existentes (`evidenceLock` de `CaptureStep` y `agentBlocker` de `GenerateStep`, el mismo estilo que `origin_not_allowed`).

## Decisiones no obvias

1. **Sin bloquear la evidencia ya capturada (D6):** `isSameHostFor` ahora acepta `outdated` con motivo `min_version` (vía `canReadEvidence`): ese Tatana tiene `case_evidence_v1` y sigue sirviendo archivos, previsualizaciones e imágenes del informe. Solo se bloquea lo que `agentLock` ya bloqueaba (capturar/guardar evidencia nueva y generar el ZIP). El "Continuar" del paso de captura sigue disponible.
2. `ensureAgentIdentity()` sin `force` revalida también cuando el estado es `outdated` (antes solo `offline`): después de reiniciar o actualizar Tatana, el reintento del perito ya ve la versión nueva.
3. Texto con `min_version`: con versión real, el de la SDD ("Tu Tatana (vX) … Se actualiza solo al reiniciarlo; si no, descargalo de {url}." / "…pedile a tu administrador el instalador nuevo."). Sin `real_version_v1` (portátil viejo, que **no** se auto-actualiza) se ajustó la redacción para no prometer la autoactualización: "Tu Tatana es de una versión anterior y necesita actualizarse… Descargá el instalador nuevo de {url} e instalalo." (o "Pedile a tu administrador…").
4. **`--local-config` en dev:** empaquetado se pasa siempre (§13.A). En dev solo si existe `<userData>/appsettings.Local.json`; si no, se taparía el `appsettings.Local.json` que el desarrollador tenga junto al agente compilado (D-T16 dice que el externo **reemplaza** al de al lado del exe).
5. **Respuesta inesperada de `/agent/maintenance`** (ni 200, ni 409, ni sin conexión): no se instala (`reason: 'error'`). D5 tiene prioridad; el próximo arranque sin agente igual instala.
6. **`quitAndInstall` no tira** si no puede lanzar el instalador (devuelve sin cerrar). Se agregó un watchdog de 15 s: si la app no empezó a cerrarse, se relanza el agente y la fase pasa a `error`/`instalacion_fallida`.
7. **Relanzado oculto:** NSIS relanza Tatana sin `--hidden`; por eso `index.ts` también arranca oculto si la fase persistida era `installing`.
8. **Redirecciones solo a `https` (D-T8):** el manifiesto se pide con `net.request` (`redirect: 'manual'` + `followRedirect()` condicional, hasta 5 saltos) en lugar de `net.fetch`, porque `net.fetch` no permite decidir por salto. Usa el proxy del sistema igual (D-T9).
9. `autoUpdater.channel = 'latest'` se setea **antes** de `allowDowngrade = false` (el setter de `channel` en electron-updater pone `allowDowngrade = true`), y se vuelve a forzar antes de cada `checkForUpdates`.
10. `matchesUpdateInfo` exige `files.length === 1` y compara además `path`/`sha512` de primer nivel y `size` si vienen en `latest.yml`.
11. Si el override `TATANA_UPDATE_URLS` no deja ninguna URL válida, se ignora y se usan las horneadas.
12. El aviso de config local cerrado se guarda en `tatana.json` (`dismissedLocalConfigNotice`) como firma `[path, overrides, error]`: si la config cambia, el aviso vuelve. El de migración pone `mostrado: true` en `migracion-portable.json` (§13.E).
13. El poll de `/agent/state` vive en `app-info.ts` (uno solo para versión del agente, avisos y `busyOperations`) en lugar de un segundo poll en el update-manager; corre siempre cada 15 s (es localhost) y se adelanta 2,5 s cuando el agente pasa a `running`.
14. Línea `installing` ("Instalando vX…"), no pedida explícitamente, para no dejar la ventana sin estado durante esos segundos. `idle`: sin línea.

## Verificación

```
agent-ui$ npx tsc --noEmit -p tsconfig.web.json      → sin errores
agent-ui$ npx tsc --noEmit -p tsconfig.node.json     → sin errores
agent-ui$ npm test                                    → tests 11, pass 11, fail 0
agent-ui$ npx electron-vite build                     → main/preload/renderer ✓ built
client$   npx tsc --noEmit                            → sin errores
```
- `meetsMinVersion` probado ad hoc con `tsx` (igual/mayor/menor, sin `real_version_v1` con "2.0.0", prerelease < final, versiones que no parsean): todo OK. No hay infraestructura de tests en `client/` (AGENTS.md), así que no se agregó un archivo de test ahí.
- `readTrustedKeys()` de `ops/tatana/_lib.mjs` (backend) lee `trusted-keys.ts` sin errores.

**No verificado localmente (necesita Windows / CI):** compilación NSIS de `installer.nsh` (`electron-builder --win`), sintaxis de `migrar-portable.ps1` (no hay `pwsh` en esta Mac), y el flujo completo de actualización (lo cubre `ops/tatana/prueba-windows.ps1` del backend en el ensayo de CI). **No se corrió `npm run dev`** de agent-ui (levanta Electron y un agente en el 8765; no quise chocar con procesos del usuario): queda para la prueba manual de §16.2 — ventana con la versión, "Actualizaciones desactivadas" en dev, aviso de config local si corresponde, bandeja con los ítems nuevos. Tampoco se probó el `client` contra un backend con `Tatana__MinVersion=9.9.9`.

## Contrato compartido — coincide con la SDD

- `GET /agent/state` (§13.A): `version`, `mode`, `busy`, `maintenance`, `operations[{kind, since, detail?}]`, `local_config{path, exists, loaded, overrides_allowed_origins, error?}` → `agent-ui/src/main/agent-api.ts`. Contrastado con `server/src/Factum.Agent/Controllers/AgentStateController.cs` (records con `SnakeCaseLower`; `error` omitido si es null, contemplado).
- `POST /agent/maintenance` body `{ "ttl_seconds": 120 }` → 200 `{maintenance, expires_at}` / 409 `{error, code: "agent_busy", operations}`; `DELETE` → 204.
- 503 `code: "agent_updating"` → `client/src/lib/agent.ts` (`AgentErrorCode`) y `agent-messages.ts`.
- Capabilities `real_version_v1`, `agent_state_v1` → `client/src/lib/agent-version.ts`, `agent.ts`.
- Args del agente: `--port --data --local-config --mode installed` (ver decisión 4 para dev).
- `GET /api/config/public.tatana_min_version: string | null` → `client/src/lib/api.ts` / `usePublicConfig.ts` (backend: `PublicConfigResponse.TatanaMinVersion`).
- `tatana-update.json` (sobre + payload §13.C), `tatana-dist.json` (`schema`, `client_url`, `update_urls`), `migracion-portable.json` (§13.E), `update-state.json` (`phase`, `availableVersion`, `reason`, `busyOperations`, … §13.F) → nombres exactos.
- `__TATANA_EXTRA_UPDATE_KEY__` / `TATANA_EXTRA_UPDATE_KEY` = `key_id:spkiDerBase64`; `TATANA_UPDATE_URLS` separado por comas.

No faltó ningún campo del contrato.

## Notas

- Ningún secreto en el repo: `TRUSTED_UPDATE_KEYS` queda vacío (paso manual §18.1); con el array vacío y sin clave extra, el actualizador de un build local queda `disabled` (`sin_claves`).
- No se mataron procesos ni se lanzó Electron. No se tocaron `server/`, `progress/sesiones/` ni el Project.
- `agent-ui/tsconfig.node.tsbuildinfo` / `tsconfig.web.tsbuildinfo` los regenera `tsc`; no son parte del cambio (el orquestador decide si los incluye).

## Correcciones por rechazo 1

Feedback de `progress/review_tatana-instalador-autoupdate.md`, sección Frontend.

1. **Un timeout ya no cuenta como "agente caído" (D5).**
   - Nuevo `agent-ui/src/main/updater/maintenance-outcome.ts` (puro): `classifyTransportError(err)` (`ECONNREFUSED` → `refused`, `ECONNRESET`/`EPIPE` → `reset`, `ETIMEDOUT` → `timeout`, el resto → `other`) y `classifyMaintenance(raw, agentRunning)`:
     - devuelve `unreachable` **solo** si `agentRunning === false` (`agentProcess.isRunning()`) o si la conexión se rechazó;
     - timeout, reset u otra falla con el agente vivo devuelven `{ kind: 'error', status: 0, failure }`;
     - 200 → `ok`, 409 → `busy`, cualquier otro status → `error`.
   - `agent-ui/src/main/agent-api.ts`: `request()` ahora devuelve una respuesta o una falla de transporte tipada, en lugar de `null`; el timeout propio se marca `timeout`. `enterMaintenance(port, agentRunning, ttl)` delega en el helper. Se corrigió el comentario: `unreachable` = "el agente no corre o nadie escucha el puerto".
   - `agent-ui/src/main/updater/update-manager.ts`: `installNow` pasa `agentProcess.isRunning()`. Con `error` no instala, no detiene el agente y loguea si el agente vivo no respondió. En el arranque con una actualización pendiente el agente todavía no corre, así que sigue siendo `unreachable` e instala.
   - Tests: `agent-ui/src/main/updater/maintenance-outcome.test.ts` (7 casos), dentro del glob de `npm test`.
2. **El instalador aborta ante cualquier resultado inesperado de `Detectar`** (`agent-ui/build/installer.nsh`):
   - nueva rama `${ElseIf} $0 != "0"`, que cubre `"error"`/`"timeout"` de `nsExec` y cualquier código distinto de 0/10/20/30. Hace `MessageBox` si no es silencioso, `SetErrorLevel 50` y `Quit` sin tocar nada. Solo `"0"` sigue a instalar;
   - en la falla de `Migrar`, `"error"`/`"timeout"` se traducen a `SetErrorLevel 51`, porque `SetErrorLevel` con un texto daría 0 (éxito);
   - `customUnInit`/`DetenerInstalado` quedan como mejor esfuerzo.
3. **`migrar-portable.ps1` ya no mata `Factum.Agent` por nombre:** al migrar solo detiene procesos con `Path` bajo la carpeta del portátil (`Stop-ProcesosBajo`), así un agente de otra carpeta no se corta.

Ambos archivos de `build/` conservan el BOM UTF-8.

### Verificación (rechazo 1)
```
agent-ui$ npx tsc --noEmit -p tsconfig.node.json   → sin errores
agent-ui$ npx tsc --noEmit -p tsconfig.web.json    → sin errores
agent-ui$ npm test                                  → tests 18, pass 18, fail 0 (11 manifest + 7 maintenance-outcome)
agent-ui$ npx electron-vite build                   → OK
client$   npx tsc --noEmit                          → sin errores
```
Sigue sin verificarse localmente la compilación NSIS y la sintaxis del `.ps1`, porque esta máquina no tiene Windows ni `pwsh`. Queda para el ensayo de CI.
