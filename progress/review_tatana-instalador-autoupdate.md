# Review — tatana-instalador-autoupdate (#21)

**Veredicto:** APROBADA (tras la re-revisión, ver al final; la primera ronda fue RECHAZADA)

Verificado por el reviewer corriendo los comandos (no por los progress):
`dotnet build` Agent y Backend (0 errores; los NU1902/NU1903 ya estaban en develop) · `dotnet test` Agent 254/254 y Backend 811 ok + 7 omitidos de Mongo (preexistentes) ·
`client` `tsc --noEmit` ok · `agent-ui` `tsc` web y node ok · `agent-ui` `npm test` 11/11 · `actionlint` (todo el repo) exit 0 ·
`bash -n` y `shellcheck` (stable, -x) exit 0 sobre `publicar-tatana-forzado.sh`, `preparar-servidor.sh`, `armar-tatana-portable.sh`, `armar-tatana-nube.sh`, `probar-publicar-forzado.sh`, `_comun.sh` ·
`node --check ops/tatana/*.mjs` ok · `node ops/tatana/probar-firma.mjs` "Todas las pruebas pasaron" · `./ops/harness/verify.sh` "Arnés OK" · `hu.mjs ver 21` = `en_revision`, rama `feat/tatana-instalador-autoupdate` (HEAD == `origin/develop`).
No se corrió `prueba-windows.ps1`, el armado NSIS ni el SSH real (necesitan Windows/VPS): lo cubre el ensayo de CI y D11.

## Checkpoints
- C1 arnés: [x] HU en `en_revision`, rama desde develop; [x] `verify.sh` OK.
- C2 documentos: [x] HU con validación del usuario y SDD con checklist y Contrato; [x] nombres C# ↔ TS coinciden con snake_case_lower
  (`/agent/state` → `server/src/Factum.Agent/Controllers/AgentStateController.cs` vs `agent-ui/src/main/agent-api.ts`;
  `tatana_min_version` → `ConfigDtos.cs` vs `client/src/lib/api.ts` y `usePublicConfig.ts`; `agent_updating` en `client/src/lib/agent.ts`;
  sobre/payload `tatana-update.json` entre `ops/tatana/_lib.mjs` y `agent-ui/src/main/updater/manifest.ts`, con prueba de ida y vuelta).
- C3 arquitectura: [x] lados dentro de lo declarado; [x] `client/AGENTS.md` sin JSX nuevo; [x] main/preload/renderer respetado (canales `tatana:*` por preload, sin Node en el renderer);
  [x] Mongo sin cambios de modelo; [x] sin `console.log` de debug ni secretos en logs.
- C4 verificación: [x] builds sin warnings nuevos; [x] tsc limpio; [x] tests existen y prueban algo real (firma/manifiesto, tracker, inspector, min version, script forzado 37 casos por CI);
  [ ] D5 "nunca durante una operación" no está garantizado (ver cambio 1).
- C5 sesión: [x] `impl_backend_*` e `impl_frontend_*` completos; [x] constancia de `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience`, `web-design-guidelines` (y `ui-animation`; `ui-styling` justificado como no aplicable);
  [x] sin scripts temporales ni datos ajenos tocados; (obs.) `agent-ui/tsconfig.web.tsbuildinfo` sin seguimiento: no commitear.

## Cambios requeridos

### Frontend (`agent-ui/`)
1. **D5 incumplido: un agente que no responde se trata como "sin operaciones" y se lo mata.**
   `agent-ui/src/main/agent-api.ts:45-74` devuelve `null` tanto por conexión rechazada como por **timeout de 3 s** o error de socket, y `agent-api.ts:105`
   lo convierte en `{ kind: 'unreachable' }`. `agent-ui/src/main/updater/update-manager.ts:445-451` lo trata como "agente caído: no puede haber operaciones",
   sigue con `agentProcess.stopAndWait()` (`taskkill /T /F`) y `quitAndInstall`. Un agente vivo pero lento (ZIP, subida grande, ffmpeg/interpolación en la misma PC) que no
   contesta en 3 s es justo el caso con una captura o un ZIP en curso, y se lo termina a la fuerza: pérdida de evidencia. El comentario de `agent-api.ts:37` ("sin respuesta: no puede haber operaciones") es falso.
   Pedido: `unreachable` solo si el socket **se rechazó** (`ECONNREFUSED`, nadie escucha el puerto) o si `agentProcess` ya no está corriendo; timeout/reset/otro error con el agente vivo
   debe ser `error` (no instala, como el 5xx), igual que ya se hace con una respuesta inesperada. Agregar un test del helper de clasificación si se extrae.
2. **El instalador sigue de largo si `Detectar` falla de forma inesperada.**
   `agent-ui/build/installer.nsh:21-56`: solo se ramifica por `"20"`, `"10"` y `"30"`. Si `nsExec` devuelve `"error"`/`"timeout"` (PowerShell bloqueado por política/AppLocker, que es
   plausible en PCs institucionales) o cualquier código ≠ `0`, no se entra a ninguna rama y se instala encima de `%LOCALAPPDATA%\Programs\Tatana` sin saber si hay un portátil
   (mezcla de archivos, el `Tatana.lnk` del portátil sigue). Pedido: tratar todo resultado distinto de `"0"` como abortar con `MessageBox` (si no es silencioso), `SetErrorLevel` y `Quit`,
   sin tocar nada (mismo criterio que el código 30). `customUnInit`/`DetenerInstalado` pueden seguir siendo "mejor esfuerzo".

### Backend
Ninguno requerido.

## Evaluación de los desvíos declarados por el frontend
- **`--local-config` solo si existe el archivo en dev** (`agent-ui/src/main/agent-process.ts`, `if (app.isPackaged || existsSync(localConfig))`): correcto. Empaquetado siempre se pasa (el instalador no trae uno junto al exe) y en dev evita taparle
  al desarrollador su `appsettings.Local.json` (D-T16: el externo reemplaza al otro). Sin riesgo para producción ni modo Mock.
- **Evidencia legible por debajo de la versión mínima** (`client/src/hooks/useAgentIdentity.ts` `canReadEvidence`/`isSameHostFor`): correcto y consistente con D-T20 ("bloquea capturar y generar el ZIP, deja lo demás") y D6 (solo afecta informes y eventos nuevos).
  El Tatana bajo el mínimo conserva `case_evidence_v1`; `agentLock`/`agentBlocker` (`dashboard/page.tsx`), `useFileManager.ts` y `ReportStep.tsx` siguen bloqueando guardar y generar. Sin mínimo (P2: vacío) no cambia nada.

## Verificado y conforme (sin hallazgos)
1. **Cadena de actualización (D7).** Ed25519 sobre los bytes exactos del payload (`manifest.ts:122-154`); clave pública solo en `trusted-keys.ts`, la privada solo por `TATANA_FIRMA_CLAVE_PRIVADA` (env, nunca impresa; búsqueda de `BEGIN PRIVATE KEY` en el repo: 0; `generar-clave-firma.mjs` se niega a escribir dentro de un repo git).
   Se descarta firma inválida, sobre sin firma o con base64 no canónico, `key_id` desconocido, payload inválido, downgrade e igual (`decide`, `manifest.ts:160`), `latest.yml` que no coincide con el manifiesto (`matchesUpdateInfo`), y se re-verifica sha512 + tamaño del archivo descargado (`update-manager.ts:378-384`) antes de pasar a `ready`.
   Sin claves, `disabled`; no hay ningún fallback que se salte la verificación. Redirecciones solo a https; override `TATANA_UPDATE_URLS` solo https o loopback (no puede servir algo con firma válida).
2. **Workflow** (`tatana-release.yml`): solo `push` de tags `tatana-v*`, `workflow_dispatch` y `pull_request` (nunca `pull_request_target`); `permissions: contents: read` por defecto y `contents: write` solo en `publicar`; actions pineadas por SHA; inputs/vars/secrets por `env:`;
   release exige commit ancestro de `origin/main` y, en dispatch, `ref == refs/heads/main` (`:107-117`); el ensayo firma con una clave efímera dentro del job `instalador` (sin artifact, sin secrets, sirve para forks) y no hornea el canal real; `firmar` y `publicar` usan el environment `tatana-release` y solo corren en release;
   SSH con clave separada `TATANA_PUB_SSH_KEY`, `StrictHostKeyChecking=yes`, `BatchMode`, `IdentitiesOnly`, y la clave se borra en `always()`. Concurrency separada por PR.
3. **VPS.** `publicar-tatana-forzado.sh`: regex estricta del comando, solo `estado`/`publicar X.Y.Z`; tar con exactamente 5 nombres y solo archivos regulares, tope 1 GiB, `SHA256SUMS` exacto, monotonía semver, staging con `trap`, renombres atómicos y escritura solo en `tatana-updates/` y `descargas/tatana/` (rutas fijas, sin entradas del cliente en ellas). `preparar-servidor.sh` bloque 11: `tatana-pub` sin grupo docker, script root:root 755, `authorized_keys` no se pisa.
   Caddy: `/tatana/updates/*` estático read-only con `no-cache` en los manifiestos; `/descargas/` ya existía.
4. **Datos D9/D10.** `migrar-portable.ps1` no toca `%LOCALAPPDATA%\Tatana\data`, `C:\Factum\Evidencia` ni el `appsettings.Local.json` destino (en conflicto copia como `.portable.json`; verifica hash; ante fallo borra solo su propia copia); no hay `customUnInstall`, `deleteAppDataOnUninstall: false` (el workflow lo exige); el agente instalado usa la misma carpeta de datos que el portátil (`agent-process.ts` `agentDataDir`).
   `prueba-windows.ps1` cubre con hashes centinela datos, evidencia y config tras instalar, actualizar y desinstalar, y se niega a correr fuera de CI o con datos existentes; `LocalConfigInspectorTests` prueba que inspeccionar no modifica el archivo.
5. **D6/D12.** `tatana_min_version` vacío por defecto (P2), validado al arrancar; sin `real_version_v1` se trata como anterior al mínimo; el agente reporta la versión real en `/health` y `/info` (ya no hay "2.0.0" fijo). Sin cambios en documentos de Mongo existentes.
6. **#7.** Se conserva `ios_developer_mode_v1`, `tools`/uxplay en `/health`, `.github/workflows/tatana-windows.yml` sin diff; `IosService` solo suma `IOperationSource` y el contador de post-proceso; `armar-tatana-portable.sh` solo agrega `-p:Version` y saca `*.ps1`/`UPDATE_URL`. Sin referencias vivas a `TatanaUpdates` ni `update-portable` fuera de docs/comentarios históricos.
7. **Modo Mock** del agente intacto (probado por el implementador en el 18765 y cubierto por los tests de `OperationTracker`); `/agent/maintenance` rechaza `Origin` y `Sec-Fetch-Site`.

## Observaciones no bloqueantes
- `agent-ui/build/migrar-portable.ps1:105-107` mata **por nombre** cualquier `Factum.Agent`, además de lo que esté bajo la carpeta del portátil (`Stop-ProcesosBajo`, ya seguro por ruta). Está así en la SDD §6.3, pero conviene filtrar también por `Path` para no cortar un agente de otra carpeta.
- Migración en modo silencioso: no hay aviso ni forma de saber si el portátil viejo (sin `/agent/state`) está grabando. Aceptado por la SDD (aviso solo en modo con UI); dejarlo explícito en la guía.
- `agent-ui/src/main/index.ts:16`: al fijar `userData` en `appData/Tatana`, en macOS/Linux de desarrollo el `agent-data` y `tatana.json` previos del desarrollador quedan en la carpeta anterior (solo dev).
- `ipc.ts` `tatana:save-config` acepta un `patch` sin lista blanca (preexistente); ahora incluye `agentDataDir`. El renderer es local y confiable, pero un allowlist sería más sano.
- `trusted-keys.ts` queda vacío a propósito: el actualizador está `disabled` (`sin_claves`) hasta el paso manual §18.1. Recordarlo antes de la primera release (no hay otra forma de que `preparar` pase: exige `TATANA_FIRMA_KEY_ID` presente en `trusted-keys.ts`).
- `publicar-tatana-forzado.sh:134`: `grep -qx "version: $VERSION"` usa los puntos como comodín de regex (sin impacto real: `latest.yml` ya viene verificado por hash y por la firma).
- El código 3 (paquete > 1 GiB) del script forzado no tiene prueba automática (lo declaró el implementador).
- Quedan pendientes manuales declarados (D11): ensayo de CI real de `prueba-windows.ps1`/NSIS en `windows-latest` (primera corrida), PC con USB, publicación real al VPS.

## Re-revisión (rechazo 1)

**Veredicto final: APROBADA.** Solo se revisaron las correcciones del frontend y que no rompieran nada.

Corrido por el reviewer: `agent-ui` `tsc -p tsconfig.web.json` y `-p tsconfig.node.json` ok · `npm test` 18/18 (11 manifest + 7 nuevos) · `client` `tsc --noEmit` ok.

- Cambio 1 (D5): [x] `agent-ui/src/main/updater/maintenance-outcome.ts:55-60` devuelve `unreachable` solo si `agentProcess` no corre o la conexión fue rechazada; timeout/reset/otro con el agente vivo da `error` y no instala (`update-manager.ts:433` pasa `agentProcess.isRunning()`). Test real de la clasificación en `maintenance-outcome.test.ts` (refused, timeout/reset/other con agente vivo, agente no corriendo, 200/409/5xx/403/404). En el arranque, con el agente aún sin lanzar, sigue instalando la actualización pendiente (correcto).
- Cambio 2: [x] `agent-ui/build/installer.nsh` agrega `${ElseIf} $0 != "0"` (cubre "error"/"timeout" y cualquier código inesperado): MessageBox si no es silencioso, `SetErrorLevel 50` y `Quit` sin tocar nada; solo "0" sigue a instalar. La falla de `Migrar` con "error"/"timeout" se traduce a 51. BOM UTF-8 conservado.
- Observación de `migrar-portable.ps1`: [x] `Migrar` ya no mata `Factum.Agent` por nombre; solo procesos con `Path` bajo la carpeta del portátil.
- Sin regresiones (backend sin cambios). Sigue pendiente lo declarado: compilación NSIS y `.ps1` en el ensayo de CI de Windows.
