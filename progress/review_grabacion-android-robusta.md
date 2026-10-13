# Review — grabacion-android-robusta (#59)

**Veredicto:** APROBADA

Verificado por el reviewer (no por los progress): `dotnet build server/src/Factum.Agent` 0 warnings / 0 errores;
`dotnet test` Factum.Agent.Tests 391/391 verdes; Factum.Backend.Tests 888 pasan, 7 skipped (preexistentes, de
Mongo); `npx tsc --noEmit` en `client/` limpio. No se usó el celular. Sin cambios en `agent-ui/` ni en
`server/src/Factum.Backend`, como declara la SDD.

## Checkpoints
- C1 (arnés): [ ] no verificado por el reviewer: la fase del Project y `./ops/harness/verify.sh` los corre el orquestador. La rama es `feat/grabacion-android-robusta` y los cambios no están commiteados.
- C2 (documentos): [x] existen `docs/hu-…`, la SDD con checklist y §9 Contrato compartido, y el contrato coincide campo por campo (ver abajo).
- C3 (arquitectura): [x] solo se tocan `server/src/Factum.Agent`, sus tests y `client/`. Sin `console.log` ni datos sensibles en logs. No hay Mongo. `client/AGENTS.md` respetado (tsc limpio).
- C4 (verificación real): [x] build, tests y tsc corridos por mí. Los tests son reales (validación, probe, marcador, escalado SIGINT→SIGTERM con proceso real, registro, 0 B en `CaseEvidenceStore`, args de mux).
- C5 (cierre): [x] ambos progress existen. El frontend deja constancia de `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` y `web-design-guidelines`. No quedan scripts ni temporales en el repo (`git status` solo muestra archivos de la HU y de otras HU).

## Contrato compartido (Tatana C# vs client TS)
- `case_id` en el body del start: `StartRecordingRequest.CaseId` (RecordingController.cs) y `agent.startRecording(..., caseId)` (agent.ts). Se valida con `TryParseCaseId`, y un id inválido da 400 `invalid_case_id`.
- Respuesta del start `{filename, state, started_at, case_id}`: `StartRecordingResponse` y el retorno tipado del cliente. Los null se omiten en HTTP y el cliente los trata como ausentes.
- Stop 200: `platform`, `serial`, `case_id`, `validation{status, recorded_seconds, duration_seconds, duration_checked, forced_stop, started_at, stopped_at}`. `RecordingValidationDto` coincide con `RecordingValidation`. Status `ok|incomplete|empty|unreadable` en ambos lados.
- Stop 422: `{error, code recording_empty|recording_unreadable, validation}`. Además hay un `recording_stopped` con `filename/url = null`. `RecordingStoppedData` los admite como `string | null`.
- 409 `not_recording` (`RecordingNotActiveException` → `Conflict`) y `AgentErrorCode` / `RECORDING_ERROR_TEXT` del cliente.
- `already_recording` 409, `recording_start_timeout` 504 y `empty_evidence` 422 existen en `AgentErrorCodes` y en `AgentErrorCode`.
- `/agent/state`: `recordings[]` y `unclaimed_recordings[]` siempre presentes (record posicional con `Recordings` y `UnclaimedRecordings`). `RecordingStateEntry` y `UnclaimedRecording` coinciden. `operations` y `background_operations` no cambian.
- WS `recording_started.data`: `{serial, platform, case_id, started_at, filename}`, igual a `RecordingStartedData`.
- Capability `recording_state_v1` en `HealthController` y `supportsRecordingState()`.

## Puntos de §11.5 y del pedido, verificados en el código
- scrcpy: `BuildScrcpyArgs` usa `--no-window` por defecto, con `--no-video-playback` solo si `WindowMode` lo pide. Suma `--stay-awake`.
- `grep shortest` en AdbService.cs da vacío. `BuildMicMuxArgs` usa `-map 0:v:0 -map 1:a:0 -c:v copy`. Si la mezcla es más corta que el video − 1 s, se descarta y queda el video sin mic (AdbService.cs 633-664).
- Los `-shortest` que quedan están en IosService.cs (1129 y 1170). Es iOS, y la SDD §5.9 dice que no se toca. Observación aceptada.
- Detención: `ProcessStop.StopAsync` manda SIGINT y escala a SIGTERM a los 3 s (solo macOS/Linux). El timeout total es de 30 s desde la primera señal y `Kill` queda como último recurso. Windows queda igual que antes.
- Start/stop usan `CancellationToken.None`, así que no dependen del `RequestAborted`. El controlador también pasa `None`. El start responde 409 sin esperar el lock.
- Validación: `Evaluate` aplica tamaño ≤ 0 → empty, sin probe → ok sin chequear, sin duración o sin video → unreadable, y duración ≥ grabado − max(3 s, 2 %) → ok (el límite está en `RecordingValidation.Tolerance`). Se evalúa antes de mezclar el mic. Todo camino de `StopScrcpyAsync` y del mock pasa por `Finish`.
- `empty` borra solo `outPath` y el `.mic.mka` de ese stop. `unreadable` mueve a `descartes/` con sufijo `_N` y nunca borra. Ninguno de los dos se devuelve como grabación ni entra a `unclaimed`.
- `CaseEvidenceStore.ImportAsync` rechaza 0 B (422 `empty_evidence`) sin mover. También rechaza un 0 B ya presente en el caso y `CheckUpload(size 0)`.
- Recuperación: `syncFromAgentState` trata como propia solo la entrada con el mismo serial y el mismo `case_id`. Si no, queda como `foreignRecording`, y `stopForeign` registra el filename en `foreignStopsRef` y nunca llama a `addFile`. En el WS, un `recording_stopped` con `case_id` ajeno no hace `addFile` (page.tsx, regla `mine`). Las `unclaimed` se importan solo si son de este caso y el filename no está ya en `files`.
- iOS: `IosService` implementa `IRecordingStateSource` con `Udid`, `CaseId` y `Stopping`, y limpia `_session` en un `finally`. `IosStopResult` suma `CaseId` y `Serial`. Aparece en `recordings` con `ios_mode` y sin `validation`.
- Mock: nunca escribe 0 B, y `MockAndroidRecordingOutcome` cubre los cuatro estados. El mock de iOS sin fuente sigue escribiendo 0 B, y ahora falla al importar con `empty_evidence` (documentado por el backend).
- Captura negra en cliente (`black-image.ts`: luma < 8 en ≥ 99 % de los píxeles, devuelve `null` ante cualquier error, no toca `<img>`). Cronómetro aislado en `ElapsedTime` con `aria-live="off"`. Avisos con `role="status"`.
- Celular: según el progress del backend, `stay_on_while_plugged_in` volvió a 0 y `screen_off_timeout` a 1800000 (el valor lo restaura el servidor de scrcpy tras un cierre limpio o forzado). No lo verifiqué contra el dispositivo, porque la consigna era no usarlo.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- **§11.3 (prueba en el navegador) NO se hizo.** Queda para el orquestador o el usuario, con Tatana mock en `:8767` y un `next dev` apuntando a él. Hay que cubrir: los cuatro outcomes del mock; recarga mientras graba; otro caso con "Detener esa grabación"; Tatana viejo; captura negra; teclado. Mi revisión de esa parte es solo de código y de tsc.
- Quedan para el usuario, según el progress backend: mic real de la PC (macOS no dio el permiso de micrófono; la mezcla se probó offline), Windows con scrcpy 4.1 (`RequireStartMarker` y `WindowMode` como planes B) y los pasos 1, 2, 4, 5, 7 y 8 de §11.4 desde la web.
- `useRecording.recordErrorText` y el `catch` del stop envuelven errores no-`AgentError` con el mensaje de `Error`. Con un Tatana viejo el texto puede ser genérico. Es cosmético.
- El cliente no tiene timeout en start/stop (`agentJSON` sin `signal`). Es adecuado, porque el start puede tardar hasta 20 s y el stop hasta 30 s o más.
