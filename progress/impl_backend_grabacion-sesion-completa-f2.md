# impl_backend — grabacion-sesion-completa-f2 (HU #85)

**Estado:** done · **Fecha:** 2026-10-15 · **Rama:** `feat/grabacion-sesion-completa-f2` (sin commits)
**SDD:** `Refactorizaciones/grabacion-sesion-completa-f2.md` §15.1 (Tatana) y §15.2 (API). DU1 = A aplicada en todos
lados (las ramas [DU1=B] se ignoraron). El iPhone y el bug de `MicCapture` (`:0`, #90) no se tocaron.

## Checklist §15.1 — Tatana (`server/src/Factum.Agent`)

- [x] `SessionOptions`: `CameraEnabled` (bool?, `EffectiveCameraEnabled` = valor o `OperatingSystem.IsMacOS()`),
  `CameraStartTimeoutSeconds` (10, 3–60), `CameraStallSeconds` (20, 0 o 10–300), `CameraPreviewIdleSeconds` (120,
  10–600), `CameraLavfi` (false); `CameraBytesPerMinute` = 18 MiB; `Normalize` + `appsettings.json` (sin escribir
  `CameraEnabled`, §11).
- [x] `AgentErrorCodes` += `session_camera_unsupported`, `camera_busy`, `camera_preview_unavailable`.
- [x] `DshowDevices.ParseDevices(stderr, kind)` + `ParseVideoDevices`; `ParseAudioDevices` delega (tests viejos verdes)
  + 3 tests nuevos (nuevo/viejo formato, `(audio, video)` en las dos listas).
- [x] Fixture `Fixtures/Cameras/avfoundation-macbook-air.txt` (§4.1, con `<nombre>`).
- [x] `CameraArgs.cs` (puro): `Build`, `BuildPreview`, `ParseSupportedModes`, `PickMode`, `ParseOutputStream`,
  `IsSegmentOutputLine`, `Classify`, `FailureText`; `CameraCsv` (parseo del CSV) + tests.
- [x] `CameraDevices.cs`: mac (avfoundation sin "Capture screen") / Windows (dshow, id = alternative name) / Linux
  (`/dev/video*`) / mock, caché 10 s, `refresh`, `ResolveAsync` (listado fresco; id → nombre) + tests de parseo.
- [x] `SessionModels.cs`: `SessionActiveCameraDto`, `SessionStateDto.Camera`, `JournalSegment.Camera` + `PartFile`
  (interno), `SessionJournalDoc.CameraRun` (`CameraRunState`, interno), `SessionMarkDto.SegmentIndex`/`OffsetSeconds`
  anulables [DU1=A] + `CameraSegmentIndex`/`CameraOffsetSeconds`, `StartSessionRequest.Camera` tipado
  (`StartCameraRequest(Id, Name, WithAudio, OriginId)`), `CameraClosedRequest(OriginId)`, `CameraStates`,
  `CameraFailDetails`. `ToDto` sin `camera_run`, `part_file` ni el `id` de la cámara.
- [x] `CameraTrack.cs` + `ICameraTrackFactory`/`CameraTrackFactory` (real o lavfi): arranque con ancla en
  `Output #0, segment`, 1,5 s vivo, reintento de modo (§5.5), CSV con sondeo de 1 s y lectura final antes de `Exited`,
  vigilancia de archivo quieto, `StopAsync` con `q` → `ProcessStop` (`SessionSignal`) → kill. `CameraJournal`
  (aplicar una línea del CSV al diario, compartido con la recuperación) + tests [ffmpeg].
- [x] `CameraPreviewService.cs` (§5.9) + DI (`ICameraPreview` para que la sesión la corte).
- [x] `SessionRecorder.cs`: campos de cámara en `Active`; `StartCameraLockedAsync`, `StopCameraLockedAsync(reason)`,
  `OnCameraSegmentClosed`, `OnCameraExitedAsync`; `StartAsync` con `camera` (también en el start idempotente si la
  cámara está `off`); `NewGap(…, track)`; hueco por pista en `StopLockedAsync`; cámara en `FinishInactiveLockedAsync`
  (con el motivo pedido), `ShutdownAsync` (las dos ramas, más corte de la previa) y el corte por disco (también desde
  `waiting_device`); `DiskTickAsync` con cualquiera de las dos pistas grabando + lazo propio de la cámara; tasa sumada
  (`CameraRate`, `TotalRate`, `Capacity`); `BuildState` con `camera_state`/`camera_segment_*`/`camera`; aviso
  `camera_failed`; rename `part_file` → nombre final antes de la cola de cierre. Rutas `StartCameraAsync`,
  `StopCameraAsync`, `RetryCameraAsync`.
- [x] `MarkRules.Locate(segments, at, now, track)` (cámara estricta); `AddMarkCore` con la ubicación en la cámara y
  [DU1=A] en `waiting_device` con la cámara grabando (sin `segment_index`/`offset_seconds`). La marca de captura usa
  el mismo camino.
- [x] `SessionCameraStateTests` (19 tests) con `FakeCameraTrack`/`FakeCameraTrackFactory`/`FakePreview` en
  `SessionTestSupport.cs`.
- [x] `SessionRecovery.cs`: §5.8 (huérfano con SIGTERM, conciliación del CSV entero creando los tramos que el diario
  no conocía, tramo abierto sin línea forzado/remux, `part_file` ajenos solo se loguean, hueco `camara`
  `tatana_cerrado`, `camera_run = null`, CSV borrado). La señal de ffmpeg pasa a `SessionSignal` también para los
  tramos de pantalla con `exe = ffmpeg`. Tests [ffmpeg] en `SessionCameraRecoveryTests`.
- [x] `CamerasController.cs` (`GET /cameras`, `POST`/`DELETE /cameras/preview`, `GET /cameras/preview.jpg`) y en
  `CaseSessionController.cs` las 3 rutas `camera/*`. Todo responde 400 `session_camera_unsupported` con el flag apagado.
- [x] `HealthController`: `CapabilitiesFor(cameraEnabled)` suma `recording_session_camera_v1` solo con
  `CameraEnabled` efectivo (el array fijo `Capabilities` queda sin esa entrada) + test.
- [x] `Program.cs`: DI (`CameraDevices`, `ICameraTrackFactory`, `CameraPreviewService`, `ICameraPreview`); corte de la
  previa en `ApplicationStopping`.
- [x] `AGENTE_TATANA.md`: casos de uso "Sumar la cámara…" y "Quitar o reintentar la cámara…" (lista, previa, empezar,
  quitar, reintentar, recuperación, cómo habilitarla en Windows).

## Checklist §15.2 — API (`server/src/Factum.Backend`)

- [x] `Models/RecordingSession.cs`: `SessionSegment.Camera` (`[BsonIgnoreIfNull]`); `SessionMark.SegmentIndex` →
  `int?`, `OffsetSeconds` → `double?` (`[BsonIgnoreIfNull]`) [DU1=A]; `CameraSegmentIndex`, `CameraOffsetSeconds`.
- [x] `DTOs/RecordingSessionDtos.cs`: `SessionSegmentDto.Camera`, `SessionMarkDto.CameraSegmentIndex`/`CameraOffsetSeconds`.
- [x] `RecordingSessionRules`: validación (§6.2: `segments[i].camera` solo con `camara` y mismas reglas que la
  cabecera; `marks[i].camera_*` los dos o ninguno; [DU1=A] `segment_index` ausente solo con `camera_segment_index`),
  merge (marcas comparan los campos nuevos; `camera` del tramo se completa una vez, distinta → 409
  `segments.camera`), `Copy`/`Overwrite` con los campos nuevos.
- [x] `Models/CaseEvent.cs`: `PreviousEnabled`, `PreviousWithCamera`; `RecordingSessionFlow`: los llena (null si el
  sub-documento se crea en ese pedido) y `enabled: false` guarda `with_camera: false`.
- [x] `ReportSessionTexts`: párrafo de cámara agrupado por tramos consecutivos con la misma configuración (una o
  varias, separador ", " y " y "), frase de cortes propios, totales por pista con `CoveredSeconds`, marcas en las dos
  pistas ("no grababa en ese momento"). Sin cámara, textos de F1 idénticos.
- [x] Tests de §14.2 (en `RecordingSessionCameraRulesTests.cs` nuevo, más casos en `ReportSessionTextsTests.cs` y
  `RecordingSessionFlowTests.cs`).

## Archivos tocados

Nuevos:
- `server/src/Factum.Agent/Services/Session/CameraArgs.cs`, `CameraDevices.cs`, `CameraTrack.cs`, `CameraPreviewService.cs`
- `server/src/Factum.Agent/Controllers/CamerasController.cs`
- `server/tests/Factum.Agent.Tests/Fixtures/Cameras/avfoundation-macbook-air.txt`
- `server/tests/Factum.Agent.Tests/Session/CameraArgsTests.cs` (incluye `CameraDevicesTests`, opciones y capability)
- `server/tests/Factum.Agent.Tests/Session/CameraTrackTests.cs` [ffmpeg]
- `server/tests/Factum.Agent.Tests/Session/SessionCameraStateTests.cs` (incluye `SessionCameraDisabledTests`)
- `server/tests/Factum.Agent.Tests/Session/SessionCameraRecoveryTests.cs` [ffmpeg]
- `server/tests/Factum.Backend.Tests/RecordingSession/RecordingSessionCameraRulesTests.cs`

Modificados:
- Tatana: `Common/AgentErrorCodes.cs`, `Controllers/CaseSessionController.cs`, `Controllers/HealthController.cs`,
  `Models/AgentModels.cs`, `Program.cs`, `Services/MicCapture.cs` (solo `DshowDevices` y visibilidad `internal` de
  `ListDshowDevicesAsync`/`ResolveWindowsDeviceAsync`; el `:0` de la captura NO se tocó), `Services/Session/SessionModels.cs`,
  `Services/Session/SessionRecorder.cs`, `Services/Session/SessionRecovery.cs`, `appsettings.json`.
- API: `DTOs/RecordingSessionDtos.cs`, `Models/CaseEvent.cs`, `Models/RecordingSession.cs`,
  `Services/Cases/RecordingSessionFlow.cs`, `Services/Cases/RecordingSessionRules.cs`, `Services/Reports/ReportSessionTexts.cs`.
- Tests: `DshowDevicesTests.cs`, `Session/GapAndMarkRulesTests.cs`, `Session/SessionJournalTests.cs`,
  `Session/SessionTestSupport.cs`, `RecordingSession/RecordingSessionFlowTests.cs`, `RecordingSession/ReportSessionTextsTests.cs`.
- Docs: `AGENTE_TATANA.md`.
- `SegmentCloser.cs` sin cambios (§5.7 de la SDD: los tramos de cámara llegan renombrados y se cierran igual).
- `graphify update .` corrido al final (graphify-out está gitignoreado).

## Verificación

```
dotnet build server/src/Factum.Agent/Factum.Agent.csproj     → Build succeeded, 0 warnings CS (--no-incremental)
dotnet build server/src/Factum.Backend/Factum.Backend.csproj → Build succeeded; solo NU1902/NU1903 (SharpCompress,
                                                               Snappier), preexistentes, sin warnings CS
dotnet test  server/tests/Factum.Agent.Tests/...            → Passed! Failed 0, Passed 882, Skipped 0 (19 s)
dotnet test  server/tests/Factum.Backend.Tests/...          → Passed! Failed 0, Passed 1457, Skipped 11 (los
                                                               Mongo*IntegrationTests: no hay Mongo, no se tocó ninguna base)
```
El único warning de compilación de los tests (`EvidenceZipTests.cs` CS8619) es preexistente.

**Prueba en vivo (opcional de §16.1), con lavfi y sin cámara real:** Tatana compilado de la rama en mock, `--port 8766
--data <scratchpad>`, `Agent__Session__MockSegmentSeconds=10`, `Agent__EvidenceDirectory=<scratchpad>` (el :8765 no se
tocó):
- `/health.capabilities` incluye `recording_session_camera_v1`; `GET /cameras` → la cámara simulada; previa `{ok:true}`,
  `preview.jpg` 200 `image/jpeg`, `DELETE` 204 y después 404 `camera_preview_unavailable`.
- `session/start` con `camera` → `camera_state: recording`, `camera` con W×H/fps reales; con la cámara grabando,
  `POST /cameras/preview` → 409 `camera_busy` y `preview.jpg` sale de la grabación; marca con ubicación en las dos pistas;
  `camera/stop` → hueco `detenido_por_perito`; `camera/start` lo cierra y sigue la numeración. Tramos de cámara de 10,0 s
  exactos y contiguos durante 2 min.
- SIGTERM a Tatana → diario `interrupted`, todos los tramos `saved`, huecos `tatana_cerrado` en las dos pistas, ningún
  ffmpeg vivo.
- `kill -9` a Tatana con la cámara grabando: ffmpeg siguió solo y cerró 3 tramos; al reabrir, la recuperación lo cerró
  con SIGTERM y dejó los 4 tramos `saved`, `recovered`, contiguos, hueco de cámara `tatana_cerrado`, `camera_run` fuera.
- Cada proceso (Tatana y los ffmpeg de prueba) se cerró por su PID; al final no quedó ningún ffmpeg ni nada escuchando
  en :8766. Los datos de esa prueba están solo en el scratchpad de la sesión.

## Contrato (§7) — confirmación

Nombres JSON (snake_case_lower) idénticos a la SDD:
- Tatana HTTP/WS: `cameras[].{id,name,backend}`, `error`; `ok`/`detail`/`error` de la previa; `camera` del start y de
  `camera/start` (`id`, `name`, `with_audio`, `origin_id`); estado: `camera_state`, `camera_segment_index`,
  `camera_segment_started_at`, `camera.{id,name,width,height,fps,with_audio}`, `warnings[].detail`; diario:
  `segments[].camera.{name,width,height,fps,with_audio}` (sin `id`), `marks[].camera_segment_index`,
  `marks[].camera_offset_seconds`, `marks[].segment_index`/`offset_seconds` ausentes (HTTP) o `null` (WS) sin pantalla.
  Internos que no salen: `camera_run`, `part_file`, `process`.
- Códigos: `session_camera_unsupported` (400), `camera_busy` (409), `camera_preview_unavailable` (404), más
  `session_not_active`, `invalid_request`, `insufficient_storage` ya existentes. Textos de §7.6 literales.
- Capability `recording_session_camera_v1` solo con `CameraEnabled` efectivo.
- API: `segments[].camera`, `marks[].segment_index`/`offset_seconds` anulables, `marks[].camera_segment_index`,
  `marks[].camera_offset_seconds`; evento `recording_session_settings_changed` con `detail.previous_enabled` /
  `detail.previous_with_camera` (ausentes si null).

## Decisiones no obvias

1. **`SegmentClosed` no toma el gate** (la SDD decía "Task.Run + `_gate`" para los eventos de la pista): el cierre
   pedido (`StopCameraLockedAsync`) espera la última línea del CSV con el gate tomado, así que la línea se aplica solo
   bajo el lock corto del diario (`_sync`) en el hilo de la pista. `Exited` sí va por `Task.Run` + `_gate`.
2. **`CameraSegmentClosed` suma `Final`** (5º campo, con default): con el proceso vivo, una línea se avisa recién
   cuando ya existe el `part_file` siguiente (ffmpeg lo abre antes de escribir la línea del anterior); la última línea
   (stop, SIGTERM, error) la avisa la lectura final con `Final = true` y no abre un tramo siguiente. La recuperación usa
   la misma regla (siguiente `part_file` presente o no).
3. **lavfi con audio en un solo grafo** (`testsrc2…[out0];sine…[out1]`, audio `0:a`) en lugar de dos entradas `-re`
   (§5.3 de la SDD): medido en esta Mac, con dos `-re` ffmpeg quedaba atrasado (8,2 s de video en 12 s) y las horas
   ancla + CSV dejaban de coincidir. Solo afecta al mock y a los tests.
4. **`q` espera 5 s fijos** (no `Recording:StopTimeoutSeconds`, 30 s): `q` cierra en ~0,7 s y 30 s no entraría en el
   presupuesto del apagado (15/20 s). Después, `ProcessStop` con SIGTERM (5 s) y kill.
5. **`part_file` ajeno**: si `camara_NNNN.part.mkv` ya existe y no está en el diario, se saltea ese número (nunca se pisa,
   regla dura). La recuperación solo loguea los ajenos.
6. **Un intento de arranque fallido** borra lo que ese mismo intento creó (`camara_N.part.mkv`, `camara.csv`,
   `camara-preview.jpg`): son archivos propios recién creados (el número se elige libre antes de lanzar).
7. **`camera_offset_seconds` y `segment_index`**: con la pantalla grabando, `segment_index` sale como antes (con su
   respaldo); la cámara es estricta (sin respaldo, D-T5) y se calcula siempre, aunque la cámara esté `failed` (si un tramo
   guardado contiene la hora, se ubica).
8. **`camera.id` del estado** = el `id` pedido o, si la web mandó solo `name`, el nombre (en macOS son iguales).
9. **Disco con la cámara sola** (`waiting_device`): lazo propio de chequeo (`CameraCts`) porque el de la pantalla se
   cancela con la desconexión; el corte por espacio en ese estado termina la sesión con `sin_espacio` en las dos pistas.
10. **Test de la vigilancia sin SIGSTOP**: detener con SIGSTOP a un hijo del propio proceso traba el manejo de procesos
    de .NET en Unix (`ProcessWaitState.TryReapChild` en bucle, visto con `dotnet-stack`). El test desvincula el
    `part_file` (ffmpeg sigue escribiendo en el inodo y el tamaño visto no cambia). En producción no aplica.
11. **Backend, cámara de cabecera**: los códigos de campo siguen como antes (`camera.name` / `camera`); la validación se
    factorizó en `ValidCamera` y la usan cabecera y tramos (`segments[i].camera` para cualquier falla del tramo).

## Pendiente para el usuario (QA manual §16.2, con la Mac y el Samsung)

- Cámara real: permisos de macOS (app empaquetada), modos que acepta, peso real por minuto (para revisar los 18 MiB),
  incertidumbre del ancla (± 1 s en el cronómetro), cámara de Continuidad que se pierde (`stalled`/salida).
- Windows (dshow) sigue sin probar: `CameraEnabled` apagado por defecto fuera de macOS.

## Bloqueos

Ninguno.
