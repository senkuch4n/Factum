# Review — ios-grabacion-larga (#50)

**Veredicto:** APROBADA

Verificado por el reviewer corriendo los comandos (no por los progress):
- `dotnet build server/src/Factum.Agent` -> 0 warnings, 0 errors.
- `dotnet test server/tests/Factum.Agent.Tests` -> 279 passed, 0 skipped (los `[FfmpegFact]` corrieron con ffmpeg real).
- `client`: `npx tsc --noEmit` -> exit 0.
- `agent-ui`: tsc web + node -> exit 0; `npm test` -> 26/26.
- `./ops/harness/verify.sh` -> exit 0. Issue #50 en `en_revision`, rama salida de `develop`.

## Checkpoints
- C1: [x] Fase `en_revision`, rama `feat/ios-grabacion-larga` descendiente de `develop`, verify.sh exit 0.
- C2: [x] HU y SDD completas. Contrato §9 verificado campo por campo (ver abajo).
- C3: [x] Cambios dentro de Tatana, `client/` y `agent-ui/` (SDD §2). No se toca `Factum.Backend` ni Mongo. agent-ui respeta main/renderer (la función de aviso se duplica en el renderer, como `busyOperationsText`; no hay APIs de Node nuevas). Sin console.log/TODOs; los logs de Tatana solo llevan nombres de archivo y estados.
- C4: [x] Build/tsc/tests limpios y reales. La prueba de navegador §11.3 NO se hizo (ver "Prueba manual").
- C5: [x] Existen ambos `impl_*`. El frontend deja constancia de `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience`, `web-design-guidelines` y `mblode-agent-skills-ui-animation` (y que `ui-styling` no aplicó) con los hallazgos aplicados.

## Contrato compartido §9 (Tatana <-> client <-> agent-ui)
- `record/stop` y WS `recording_stopped.data`: `filename`, `url`, `platform`, `variants_expected`, `variants` en `RecordingController.cs` <-> `RecordingStoppedData` y tipo de `stopRecording` en `client/src/lib/agent.ts`. OK.
- WS `video_variant_status.data`: `VideoVariantStatusDto` (SnakeCaseLower, `null` explícitos por el hub) <-> `VideoVariantStatus`; el cliente trata `null` = ausente. Valores de `state`/`reason`/`label` idénticos a los de la SDD. OK.
- `video_variant_ready` se sigue emitiendo (`SetReadyAsync`) y el cliente deduplica por `filename`. OK.
- `GET /video-postprocess/status`: `items[].original_filename/known/variants[]` <-> `VideoPostprocessStatusResponse`; lotes de 50, 5 s, `null` ante error. OK.
- `GET /agent/state.background_operations[]` (`kind`, `since`) <-> `agent-ui/src/main/agent-api.ts` (`[]` si falta); `operations` ya no trae `video_postprocess` (`IosService.ActiveOperations`). `/health` suma `video_postprocess_v1`. OK.

## Puntos pedidos, verificados en el código
- **El original se guarda sin esperar al post-proceso.** `saveOne` perdió el `return false` por variante; `handleSaveAndContinue` ya no tiene `waitingVariant`/`file_busy`; `addVideoVariant` ya no encola nada con MCI (`useFileManager.ts`). `grep pendingVariant|markPendingVariant|isPending client/src` sin resultados. Backend: `Start` abre el original con `FileShare.Read|Delete` antes de responder, copia a `.postprocess/<stem>/source.mp4` y ffmpeg solo lee la copia (`RunBlendAsync`/`RunRifeAsync` reciben `work`, nunca el original).
- **AirPlay / on_device / mock sin fuente / ffmpeg ausente / deshabilitado:** `Start` no se llama o devuelve `[]`; `variants_expected=false`; el cliente no marca nada. Sin indicador.
- **Falla de la variante:** `failed`/`timeout`/`skipped`/`cancelled` solo cambian el estado visual; nunca tocan el original ni el guardado. El `finally` de `RunJobAsync` asegura un terminal por etiqueta (`EmitAsync` ignora un segundo terminal) y borra la carpeta de trabajo.
- **Timeout:** `ComputeTimeout` (3x + 120 s, piso 300, techo 7200; sin duración = máximo), pasado como `timeout` a `ProcessRunner` (mata el árbol); `TimeoutExitCode` -> borra el parcial y emite `timeout/timeout`. RIFE usa un solo plazo para los 3 pasos. Cubierto por test real y por la prueba de 20 s del backend.
- **Cancelación al borrar:** `Discard` en `FilesController` y `CaseEvidenceController` dentro de `finally` (no cambia la respuesta, nunca tira); cancela con `removed` y el `finally` del job borra variantes de la raíz. `CancelAll("agent_update")` tras `TryEnterMaintenance`, y `agent_stopping` en `ApplicationStopping`.
- **No bloquea la actualización:** `background_operations` aparte de `operations`; en agent-ui no entra en `isReadyAndFree()`.
- **Limpieza de arranque:** `CleanupWorkDirectory` borra solo `<DataDirectory>/.postprocess` por ruta exacta. Nada del post-proceso escribe en `cases/<id>/`.
- **Datos:** no encontré rastro de datos ajenos tocados; los tests usan un `factum-pp-<guid>` propio que borran en `Dispose` (no quedó nada en /tmp).

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- **Prueba de navegador §11.3 sin hacer (ni el frontend ni yo).** Requiere un Tatana mock en otro puerto con `Agent__MockIosRecordingSource`, un `client` con `NEXT_PUBLIC_AGENT_URL` apuntando ahí, backend :8080 con login y un caso nuevo; eso implica crear un caso en la Mongo de desarrollo del usuario, así que la dejé como **prueba manual del usuario** junto con §11.4: grabar iOS mock/real y ver "Generando versión suavizada… N %" + avance de paso inmediato; recargar a mitad y que el indicador se recupere; Tatana reiniciado (sin spinner colgado); AirPlay/on_device sin indicador; quitar la grabación en curso; agent-ui con post-proceso corriendo (botón habilitado + aviso).
- **Windows sin probar:** la ausencia de `file_busy` con la copia de trabajo solo se puede validar en Windows (`MoverConElHandleAbierto_NoTira` pasa trivialmente en macOS). RIFE tampoco se probó (no instalado). Ambos ya están anotados como pendientes del usuario por el backend.
- **Orden de despliegue (DA2):** primero la web, después la release de Tatana (un cliente viejo con Tatana nuevo esperaría un `video_variant_ready` con "MCI" que ya no existe). Dejarlo en el PR.
- `IosService.StopRecordingAsync` llama a `_post.Start(...)` sin try/catch; hoy no tiene caminos que tiren, pero si lo hiciera, el stop devolvería 500 con el archivo ya grabado. Defensa extra opcional (envolver y devolver `[]`).
- El `message` de los eventos puede incluir la última línea de stderr de ffmpeg, que a veces contiene rutas locales de la carpeta de trabajo; el cliente no lo muestra, solo viaja por WS local.
- Artefactos de `tsc` en `agent-ui/`: `tsconfig.node.tsbuildinfo` (versionado, modificado) y `tsconfig.web.tsbuildinfo` (sin versionar) aparecen en `git status`. No deben entrar en el commit (restaurar el primero y no agregar el segundo).
- El campo `Toca` del Project dice "backend+client"; la HU también toca `agent-ui` (cosmético).
