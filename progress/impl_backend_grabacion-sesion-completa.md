# impl_backend — grabacion-sesion-completa (#80), fase 1

**Estado:** done (sin commit, por pedido del orquestador) · **Rama:** `feat/grabacion-sesion-completa` · **Fecha:** 2026-10-14
**Alcance:** checklist §15.1 de `Refactorizaciones/grabacion-sesion-completa.md` (Tatana + API + plantillas + deploy + docs),
con DU1–DU4 en la recomendada (DU2 = corte solapado con reafirmación de "pantalla siempre encendida"). La fase 2 (§15.3:
cámara e iPhone, HU #85) **no** se hizo.
No se tocó `client/` ni `agent-ui/`, ni el Project, ni `progress/sesiones/`. Tatana de :8765 y backend de :8080 no se
tocaron (todo se compiló con `--artifacts-path` a una carpeta temporal; siguen corriendo con el código anterior hasta que
se reinicien).

## Checklist §15.1

**Tatana**
- [x] `Services/Session/SessionNames.cs` + tests.
- [x] `SessionOptions` en `AgentModels.cs` (`Agent:Session:*`, `Normalize`, `Clone`) + `appsettings.json` (sin `Mock*`).
- [x] `SessionModels.cs`: DTOs de §7.2.6/§7.2.7 con los nombres exactos; `process`, `stay_awake` y los demás internos no salen por HTTP.
- [x] `SessionJournal.cs` (atómico, lock por caso, ilegible → renombre) + tests.
- [x] `SessionDiskBudget.cs` (puro) + tests.
- [x] `StayAwakeKeeper.cs` [DU2=A] + tests.
- [x] `AndroidScreenTrack.cs`: start con marcador, corte solapado [DU2=A], `Exited`, mock (`MockScreenTrack`).
- [x] `SessionRecorder.cs`: estados, temporizador con `TimeProvider`, cola de cierre, desconexión/reconexión, avisos de pantalla y disco, marcas, latido, `ShutdownAsync`, `IOperationSource`.
- [x] Tests de la máquina de estados, huecos y marcas con fakes.
- [x] `SessionRecovery.cs` (§5.10) + tests con ffmpeg.
- [x] `CaseSessionController.cs` (start/stop/marks/GET) y `SessionCapacityController.cs` con los códigos de §7.7.
- [x] `AgentStateController`: `recording_session`, `session_recovery` y `POST /agent/prepare-shutdown` (solo local).
- [x] `RecordingController.Start`: 409 `session_active`.
- [x] `ScreenshotController`: marca automática y `session_mark` en la respuesta y en el evento.
- [x] `CaseEvidenceStore`: prefijo reservado y `session_file_protected` en `Delete` (consulta `SessionJournal`).
- [x] `EvidenceZip` (Tatana): `EvidenceZipEntry`, overload, `IsAllowedEntryName` + tests.
- [x] `CaseZipService`: `ZipFileItem.ZipFolder`, entradas, devolución y borrado del diario en el commit + tests.
- [x] `Program.cs`: DI, `SessionRecovery` en segundo plano, `OnDevicesPolled` en el bucle de 3 s, `ShutdownAsync` primero en `ApplicationStopping`.
- [x] `HealthController.Capabilities` += `recording_session_v1` (+ test).
- [x] `AgentErrorCodes` += 6 códigos.

**API**
- [x] `Models/RecordingSession.cs` + `Case.RecordingSession` + exclusión en la proyección del listado.
- [x] `RecordingSessionOptions` (`Recording:*`) + `appsettings.json` + DI + warnings al arrancar.
- [x] `PublicConfigResponse` += 2 campos al final + `ConfigController` + test de orden.
- [x] `RecordingSessionRules.cs` (validación, merge, `SessionFilenames`, `PendingForGeneration`) + tests.
- [x] `DTOs/RecordingSessionDtos.cs` (requests de §7.5).
- [x] `RecordingSessionFlow.cs` (`partial class CaseService`): settings y sync, eventos, reintentos optimistas.
- [x] `MongoCaseRepository`: `CreateRecordingSessionAsync`, `SetRecordingSessionSettingsAsync`, `SetRecordingSessionAsync` (bajo `Editable(id)`).
- [x] `CasesController`: 2 rutas.
- [x] `CaseEvent`: 5 tipos y 10 campos de detalle.
- [x] `AgentGeneration.PrepareGenerationAsync`: `recording_session_open`.
- [x] `EvidenceManifest.ToManifestFiles`: `zip_folder`.
- [x] `ManifestFileDto.ZipFolder`.
- [x] `DeleteEvidenceAsync` (vive en `AgentGeneration.cs`): `recording_session_file`.
- [x] `EvidenceZip` (backend): paridad con Tatana + tests espejo.
- [x] `TimestampReadme` + `CaseTimestampsController`: `sesion/` + test.
- [x] `ReportSessionTexts.cs` + tests de textos.
- [x] `ReportService`: placeholders, bloque, `BuildHashRow` parametrizado, `FillSessionTable`, separación general/sesión. `ReportValues.Counts` sin tramos. Test DOCX.

**Plantillas, deploy y docs**
- [x] `build_plantilla_v4.py` y `build_plantilla_v6.py` (§8.1–§8.2) → v4 y v6 regeneradas con los scripts → `--check` OK → `ops/plantilla/README.md`.
- [x] Deploy (§11) y `README.md` / `AGENTE_TATANA.md`.

## Archivos

### Tatana (`server/src/Factum.Agent`)
- **Nuevos** (`Services/Session/`)
  - `SessionNames.cs`: regex `^sesion_(pantalla|camara)_\d{3,4}_\d{8}_\d{6}\.mkv$`, `SegmentName`, `HasReservedPrefix`, constantes de carpetas.
  - `SessionModels.cs`: estados, motivos, `JournalSegment` (con `Process` interno), `SessionGapDto`, `SessionMarkDto`, `SessionJournalDoc` (+ `StayAwake`, `LastAliveAt`, `AndroidVersion`, `StoppedAt` internos) y `ToDto()`, `CaseSessionDto`, `SessionStateDto`, `SessionWarningDto`, requests, `SessionCapacityDto`, `PrepareShutdownResponse`.
  - `SessionJournal.cs`: `Load` (missing / ilegible / ok), `Save` atómico, `ListCaseIds`, `QuarantineUnreadable`, `SegmentFilenames`, `TryDeleteIfClosed`.
  - `SessionDiskBudget.cs`: tasa, requisito de inicio, estimación con factor 2, minutos restantes, aviso, corte, reemisión cada 10 min, `SameVolume`.
  - `StayAwakeKeeper.cs`: `BeginAsync` / `ReassertAsync` / `RestoreAsync` con adb inyectable.
  - `AndroidScreenTrack.cs`: `ISessionTrack`, `ISessionTrackFactory`, `SessionTrackFactory`, `AndroidScreenTrack` (scrcpy con `AdbService.BuildScrcpyArgs`, audio interno en Android 11+, nunca el mic) y `MockScreenTrack`.
  - `SegmentCloser.cs`: cierre de §5.6, común a la sesión y a la recuperación.
  - `SessionRecorder.cs`: la sesión, más `SessionRecoveryStatus`, `GapRules` y `MarkRules` (puros).
  - `SessionRecovery.cs`.
- **Nuevos** (`Controllers/`): `CaseSessionController.cs`, `SessionCapacityController.cs`.
- **Modificados**
  - `Services/AdbService.cs`: helpers `internal static` reutilizables (`GenerateMockVideoAsync`, `MockMkvBytes`, `RemuxFileAsync`, `MoveToDiscards(dataDir, …)`, `FileSize`, `HasExitedSafe`, `ScrcpyErrorSummary`, …). La grabación puntual no cambia de comportamiento.
  - `Services/CaseEvidenceStore.cs`: prefijo `sesion_` reservado en `RequireNewName` (subida, upload-check, import); `Delete` → 409 `session_file_protected` para un tramo del diario, y overload `Delete(…, allowSessionFiles)` que usa solo el commit del ZIP. El ctor recibe `SessionJournal`.
  - `Services/CaseZipService.cs`: `ZipFileItem.ZipFolder`, validación (`null` | `"sesion"` con nombre de tramo), unicidad por entrada, `EvidenceZipEntry`, `zip_folder` en la respuesta y `TryDeleteIfClosed` del diario tras `DeleteLoose`. El ctor recibe `SessionJournal`.
  - `Services/Evidence/EvidenceZip.cs`: `EvidenceZipEntry`, overload por entradas, `IsAllowedEntryName` (en `WriteAsync` y `VerifyAsync`).
  - `Controllers/AgentStateController.cs`, `HealthController.cs`, `RecordingController.cs`, `ScreenshotController.cs`.
  - `Common/AgentErrorCodes.cs`, `Models/AgentModels.cs`, `Program.cs`, `appsettings.json`.

### API (`server/src/Factum.Backend`)
- **Nuevos**: `Models/RecordingSession.cs`, `DTOs/RecordingSessionDtos.cs`, `Services/Cases/RecordingSessionRules.cs` (+ `RecordingSessionValues`), `Services/Cases/RecordingSessionFlow.cs` (+ `RecordingSessionErrorCodes`), `Services/Recording/RecordingSessionOptions.cs` (+ `RecordingSessionModes`), `Services/Reports/ReportSessionTexts.cs`.
- **Modificados**: `Models/Case.cs`, `Models/CaseEvent.cs`, `DTOs/ConfigDtos.cs`, `DTOs/EvidenceDtos.cs`, `Controllers/CasesController.cs`, `Controllers/ConfigController.cs`, `Controllers/CaseTimestampsController.cs`, `Infrastructure/MongoRepository.cs`, `Services/Cases/CaseService.cs` (interfaz + ctor), `Services/Cases/AgentGeneration.cs` (prepare y delete), `Services/Cases/EvidenceManifest.cs`, `Services/Reports/EvidenceZip.cs`, `Services/Reports/ReportService.cs`, `Services/Reports/ReportValues.cs`, `Services/Timestamping/TimestampReadme.cs`, `Program.cs`, `appsettings.json`, `Templates/plantilla_informe_v4.docx`, `Templates/plantilla_informe_v6.docx`.

### Plantillas, deploy y docs
- `ops/plantilla/build_plantilla_v4.py`, `build_plantilla_v6.py`, `README.md`.
- `deploy/cloud/docker-compose.yml`, `deploy/cloud/factum.env.example`, `deploy/windows/docker-compose.yml`, `deploy/windows/.env.example`, `deploy/windows/scripts/restaurar.ps1`, `deploy/windows/tests/comun.Tests.ps1` (los `.ps1` conservan BOM y CRLF; el diff es solo de las líneas tocadas).
- `README.md` (sección "Grabación de la recolección (video de la sesión completa)") y `AGENTE_TATANA.md` (4 casos de uso nuevos).

### Tests
- **Tatana** (`server/tests/Factum.Agent.Tests/Session/`):
  - `SessionTestSupport.cs`: pista falsa, sondas falsas y harness con carpetas temporales propias.
  - `SessionNamesTests.cs`: también `SessionOptions.Normalize` y el prefijo reservado de `RequireNewName`.
  - `SessionDiskBudgetTests`, `SessionJournalTests`, `StayAwakeKeeperTests`.
  - `SessionStateMachineTests`: start, idempotencia, `session_active`, `already_recording`, pantalla, 507, corte, corte demorado, desconexión y reconexión del mismo serial (otro serial no reanuda), `fallo_grabacion` con reintento, stop idempotente, retomar, `waiting_device` → stop, tramo vacío, shutdown, marcas, tramo protegido, GET y capacidad.
  - `GapAndMarkRulesTests`.
  - `SessionRecoveryTests` [ffmpeg]: MKV válido, truncado → remux, 0 B, ilegible → `descartes/`, diario ilegible, `waiting_device`, proceso huérfano cerrado limpio, PID ajeno sin tocar y `restore_pending`. También `MkvTruncationTests` [ffmpeg].
  - `SessionZipTests`: `EvidenceZipSessionTests`, `CaseZipSessionTests` (`zip_folder` y borrado del diario) y `SessionAgentStateTests` (capability y `/agent/state`).
- **API** (`server/tests/Factum.Backend.Tests/RecordingSession/`, namespace `SesionGrabacion` para no chocar con el tipo):
  - `RecordingSessionRulesTests.cs`: validación campo por campo, merge, eventos y opciones.
  - `ReportSessionTextsTests`.
  - `ReportServiceSessionDocxTests`, sobre la v6 real.
  - `RecordingSessionMiscTests`: manifiesto con `zip_folder`, LEEME y espejo de `EvidenceZip`.
  - `RecordingSessionFlowTests`: settings y modos, sync con eventos, conflicto, busy, guardias de prepare y delete, y BSON de un caso viejo.
  - `MongoRecordingSessionRepositoryTests` (`[MongoFact]`).
- **Ajustados:** `TatanaMinVersionTests` (orden de la config pública) y `Admin/AdminTestDoubles.cs` (3 métodos nuevos del repo).

## Verificación

Todo con `--artifacts-path <scratchpad>/art`, para no pisar los binarios de los procesos en :8765/:8080.

| Comando | Resultado |
|---|---|
| `dotnet build server/src/Factum.Agent/Factum.Agent.csproj --no-incremental` | 0 errores, **0 warnings** |
| `dotnet build server/src/Factum.Backend/Factum.Backend.csproj --no-incremental` | 0 errores; solo los 2 `NU1902/NU1903` de paquetes (SharpCompress/Snappier, dependencias de MongoDB.Driver). El `.csproj` no se tocó. |
| `dotnet test server/tests/Factum.Agent.Tests` | **813 passed**, 0 failed, 0 skipped. Incluye los 109 de `Session/`; los de ffmpeg corrieron porque hay ffmpeg. |
| `dotnet test server/tests/Factum.Backend.Tests` | **1437 passed**, 0 failed, 10 skipped. Los skipped son los `[MongoFact]` sin `FACTUM_TEST_MONGO`. |
| `FACTUM_TEST_MONGO=mongodb://localhost:27017 dotnet test … --filter MongoRecordingSession\|MongoDeviceDetails` | 2 passed. Base propia `factum_test_<guid>`, borrada al final; se verificó que no quedó ninguna `factum_test_*`. |
| `python3 ops/plantilla/build_plantilla_v4.py --check "docs/INFORME … FACTUM.docx" …/plantilla_informe_v4.docx` | `OK` |
| `python3 ops/plantilla/build_plantilla_v6.py --check …/plantilla_informe_v4.docx …/plantilla_informe_v6.docx` | `OK` |
| `Invoke-Pester deploy/windows/tests` | **No corrido:** no hay `pwsh` en esta Mac. Se agregaron 2 casos a `comun.Tests.ps1`. |

- **Línea base antes de tocar nada:** la v4 y la v6 regeneradas eran idénticas byte a byte a las versionadas, así que el diff de las `.docx` es solo el bloque nuevo.
- **Render del DOCX:** se renderizó con LibreOffice a PDF y se revisó a ojo. El bloque queda después del de sellos y antes de "CADENA DE CUSTODIA". El subtítulo va a la izquierda y sin sangría. La segunda tabla tiene el mismo estilo que la de hashes.
- Los warnings de los proyectos de test (`CS8714`/`CS8619` en `EvidenceZipTests.cs`, `xUnit1031` en `ReportBodyImagesDocxTests.cs`) ya estaban.

### Prueba con Tatana en mock (puerto 8766, `--data` en el scratchpad)
Configuración: `Agent__Session__MockSegmentSeconds=10`.

Se recorrió todo por HTTP:
- `capacity`, `start`, `marks` (manual), `screenshot` (con `session_mark` en la respuesta) y `record/start` → 409 `session_active`;
- corte cada 10 s, `stop` (`cambio_de_paso`) y `GET /session`;
- `DELETE` de un tramo → 409 `session_file_protected`; `upload-check sesion_x.png` → 400 con el texto del prefijo reservado;
- ZIP con `zip_folder: "sesion"`: `unzip -l` lista `sesion/sesion_pantalla_00N_….mkv`;
- `commit`: borró los 3 sueltos y la carpeta `sessions/<caseId>/`;
- `prepare-shutdown`: `{"had_session":true,"segments_saved":1,"pending":0}`, diario `interrupted` con el hueco `tatana_cerrado`. Con `Origin` responde 403 `maintenance_local_only`;
- `kill -9` de Tatana grabando y relanzado: la recuperación dejó el diario `interrupted`, el hueco `tatana_cerrado` con `segment_lost: true` y el tramo en `discarded` (en mock no hay archivo hasta el cierre).

### Prueba en vivo con el SM-M127F (Tatana de esta rama en el puerto 8767, `--data` en el scratchpad)
- `stay_on_while_plugged_in` antes: `0`. Durante la sesión: `7`, reafirmado después de cada corte. Después del stop: **`0`**, verificado de nuevo al final de todo. No se instaló nada en el teléfono. Del teléfono solo se leyó, salvo la escritura de "pantalla siempre encendida" y `logcat -d`.
- Configuración: `Agent__Session__MockSegmentSeconds` (60 s y 15 s), que en esta implementación también vale fuera de mock (ver decisiones). Además `skip_screen_check: true`, porque la pantalla estaba apagada.

**Corrida 1:** 7 tramos de ~60 s, una captura con marca automática y stop `detenido_por_perito`.
- 6 tramos `saved` y validados `ok`: MKV con h264 + opus y 64 s de duración (los 2–3 s de más sobre lo grabado son el solapamiento y el arranque).
- Solapamiento medido: 0,3–0,7 s.

**Corrida 2:** tramos de 15 s.
- 3 tramos `saved`. Uno `discarded` con el hueco `fallo_grabacion` y `segment_lost: true`, seguido de la reanudación automática en un tramo nuevo, con el hueco cerrado.

**Hallazgo: 2 de 9 cortes fallaron**
- En 2 de los 9 cortes solapados, el scrcpy **nuevo** terminó solo ~0,5–0,7 s después de su marcador.
- `logcat`: el servidor hizo `System.exit(0)`, sin error en el cliente.
- Mientras tanto había **otro scrcpy ajeno** espejando el celular: PID 90402, lanzado a mano desde una terminal a las 15:52. No lo toqué. Con él eran 3 capturas de audio interno a la vez, y es el sospechoso principal; sin él, la SDD midió 2 instancias estables.
- En la corrida 1 la caída cayó justo en la ventana entre el chequeo de arranque y el momento en que el tramo pasa a ser "el actual", así que el evento `Exited` se perdía: el tramo quedó vacío y se cubrió con el hueco `fallo_grabacion` + `segment_lost` del corte.
- Lo corregí (`AndroidScreenTrack.MakeCurrent`, que revisa si el proceso ya terminó). En la corrida 2 la caída se detectó bien: hueco `fallo_grabacion` y reanudación en ~5 s.
- **Pendiente para el usuario:** repetir la prueba de §16.2 paso 4 **sin otros scrcpy abiertos**. Si los cortes siguen fallando en ese equipo, la alternativa es DU2=B (corte secuencial, ~0,85 s de hueco por corte, sin escrituras extra).

Otra observación de ese entorno: Tatana se lanzó en segundo plano desde una shell no interactiva y hereda SIGINT ignorado. Por eso scrcpy cerró siempre con SIGTERM a los 3 s ("SIGINT sin efecto; se envía SIGTERM", exit 0). Es el comportamiento ya previsto por `ProcessStop`; no pasa con Electron.

Los datos de prueba (mock, live, evidencia y ZIP) se borraron del scratchpad. Ninguno estaba en `agent-data` del repo ni en la Mongo de desarrollo.

## Decisiones no obvias
1. **`Agent:Session:MockSegmentSeconds` también vale fuera de mock** ("solo Mock o dev"; no va en `appsettings.json`). Sirvió para la prueba en vivo con tramos de 15–60 s. En producción no se configura.
2. **`GET /session/capacity` acepta `segment_minutes` opcional** (default 30) para calcular `required_bytes_to_start` / `can_start` con el tramo real. La web hoy no lo manda, así que con tramos de 5 min `can_start` es más exigente que el start real. Inofensivo; queda para el frontend si quiere afinarlo.
3. **Merge tolerante a snapshots viejos:** la SDD dice "cualquier otra diferencia → 409". Un snapshot que trae **menos** información no da conflicto: un hueco ya cerrado que llega con `to` null, `retomado_otro_dia` que llega como `cambio_de_paso`, o un tramo que falta. Se ignora, porque con varias pestañas el orden de los syncs no está garantizado. Sigue siendo 409 lo contradictorio: otro `sha256`/`filename` para el mismo id, otro `to`, otro `from`/`track`/`segment_lost`, o una marca distinta.
4. **Tramo descartado:**
   - En un cierre síncrono (stop, apagado, `sin_espacio`, recuperación), el hueco que se abre al final lleva `segment_lost: true`, como dice §5.10.
   - En un cierre asíncrono (corte, salida de scrcpy), el hueco del caller ya pudo haberse sincronizado y `segment_lost` es inmutable en el backend. Por eso se agrega **otro** hueco `{from: inicio del tramo, to: fin (o inicio del siguiente), reason: stop_reason o fallo_grabacion, segment_lost: true}`.
   - Esto corrige además §5.6 ("en un corte solapado no hace falta"): un tramo N descartado deja sin video [inicio N, inicio N+1], que no está cubierto por N+1.
5. **Stop con `waiting_device`:** pasa a `stopped`. No abre otro hueco: el de `desconexion` queda abierto como fin, con `stop_reason: desconexion`. **`prepare-shutdown` con `waiting_device`:** deja el diario tal cual y la recuperación lo pasa a `interrupted`.
6. **`fallo_grabacion` con el celular conectado:** durante el reintento (3 s) el estado es `starting`, con `stop_reason: fallo_grabacion`. Para distinguir desconexión de falla, se espera 1,5 s a que se asiente el USB y se consulta `adb -s <serial> get-state`. El último sondeo de 3 s todavía lista el celular cuando scrcpy ya salió.
7. **Encabezado de las marcas en el informe:** la frase "el mismo minuto sirve aproximadamente para el de la cámara" sale **solo si hay cámara**. Sin cámara queda "Marcas de momento (para ubicar hechos en el video de la pantalla):". El resto de los textos de §8.4 son exactos.
8. **Settings sin cambios:** si `enabled` y `with_camera` ya eran esos, no hay escritura ni evento (§6.3: "solo si cambió"). La creación usa `CreateRecordingSessionAsync` (filtro "no existe") y, si otra pestaña la creó antes, cae al update.
9. **El sync nunca toca el switch:** `$set` campo por campo de la cabecera, los tramos, los huecos, las marcas y `SyncedAt`, con `$unset` de los null. Así un settings concurrente no se pisa.
10. **Plantilla v6:** el subtítulo lleva `w:ind left=0 firstLine=0` y `jc left`. Si no, heredaba la sangría de primera línea del estilo Normal (se vio en el render). En la v4 queda con el pPr normalizado, como pide §8.1. El chequeo de centinelas de la v6 cuenta ahora 4 celdas con la línea verde (2 por tabla).
11. **Inyección opcional:** `RecordingSessionOptions` entra a `CaseService` y a `ConfigController` como último parámetro opcional, para no romper los tests que los construyen a mano. DI lo resuelve porque está registrado.
12. **Commit del ZIP:** borra los tramos con `Delete(…, allowSessionFiles: true)`, porque ya están dentro del ZIP confirmado. `DELETE /cases/{id}/files/{tramo}` desde la web sigue dando 409.
13. **`resumed`** es true si el diario ya tenía tramos de pantalla antes de este tramo continuo (retomar, reconexión o reintento).
14. **`stop_reason`** solo viaja en `stopped` / `interrupted` / `waiting_device`, tanto en el DTO del diario como en el estado. **`stopped_at`** sale de `SessionJournalDoc.StoppedAt` (interno).
15. **Marcas antes del primer tramo o fuera de rango:** quedan en el último tramo que empezó antes, o en el primero.
16. **Recuperación:** se remuxea solo si no hubo un cierre limpio conocido. Si el proceso huérfano se cerró limpio, no, y el cierre igual remuxea si ffmpeg no puede medir el video. `stopped_at` de un tramo recuperado = inicio + duración del probe; sin duración, el último latido.

## Contrato compartido (§7): confirmación
- **Tatana HTTP** (snake_case, sin null):
  - `GET /session/capacity` y los 7 campos de §7.2.1;
  - `POST /cases/{caseId}/session/start|stop|marks` y `GET /cases/{caseId}/session`, con los bodies, respuestas y códigos de §7.2.2–§7.2.5;
  - `SessionStateDto` con los 19 campos de §7.2.7 (`camera_state` siempre `"off"` en F1) y `CaseSessionDto` = §7.2.6;
  - `screenshot` suma `session_mark` solo si hubo marca;
  - `POST /agent/prepare-shutdown` → `{had_session, segments_saved, pending}`;
  - `/agent/state` suma `recording_session` (omitido si no hay sesión activa) y `session_recovery` (siempre); `operations[]` suma `{kind: "recording_session", since, detail: <case_id>}`;
  - `ZipFileItem.zip_folder` en pedido y respuesta.
- **Tatana WS** (los null viajan): `session_state`, `session_segment_closed {case_id, origin_id, segment}`, `session_mark_added {case_id, origin_id, mark}` y `screenshot_taken.session_mark` (null si no hubo).
- **Capability:** `recording_session_v1`.
- **Códigos de Tatana:** `session_active` (con `case_id`), `session_not_active`, `session_not_found`, `session_file_protected`, `session_platform_unsupported`, `session_ios_mode_unsupported` (definido, sin uso en F1).
- **API:**
  - `PUT /api/cases/{id}/recording-session/settings` `{enabled, with_camera}` y `PUT /api/cases/{id}/recording-session` (snapshot de §7.5) → 200 `Case`;
  - `Case.recording_session` = §7.6.1, con los null serializados;
  - `PrepareGenerationResponse.files[].zip_folder` (`"sesion"` | null);
  - config pública `recording_session_mode` y `recording_session_segment_minutes`, al final;
  - los 5 tipos de evento con `enabled`, `with_camera`, `track`, `segment_index`, `at`, `duration_seconds`, `validation`, `recovered`, `gap_seconds`, `segment_lost` (+ `reason`, `sha256`, `size`);
  - códigos `recording_session_disabled|required|not_enabled|conflict|busy|open (+pending_files)|file`, e `invalid_recording_session` (+`field`).
- Se comparó con `client/src/lib/agent.ts` y `client/src/lib/api.ts` del frontend en curso: los nombres coinciden.

## Pendiente para el usuario (prueba manual §16.2)
- Reiniciar el backend y Tatana con el código de esta rama: los procesos de :8080/:8765 siguen con el binario anterior.
- §16.2 paso 4 (cortes) **sin otros scrcpy abiertos** (ver el hallazgo).
- Paso 10 con Electron: diálogo + `prepare-shutdown`, y matar `Factum.Agent` de golpe.
- Paso 14 en Windows: Ctrl+C del helper a un PID huérfano en la recuperación (§4.2.6).
- `Invoke-Pester deploy/windows/tests` en una PC con `pwsh`.

## Ajuste post-review (observación 1 de `progress/review_grabacion-sesion-completa.md`)

**Problema:** en el corte solapado, el scrcpy nuevo se daba por bueno con 300 ms de vida después del marcador. En el
SM-M127F se cayó 0,5–0,7 s después del marcador (2 de 9 cortes) cuando el anterior ya estaba cerrado.

**Cambio:**
- `Services/Session/AndroidScreenTrack.cs`:
  - `RotateAsync` usa el nuevo `RotationStarter.StartConfirmedAsync`: lanzar → marcador → el proceso tiene que seguir **vivo** durante `Agent:Session:RotationConfirmMilliseconds` (default 1500 ms, 0–10000) → recién ahí pasa a ser el actual y se cierra el anterior.
  - Si el nuevo no arranca o se cae dentro de la ventana, se aborta (se borra solo su archivo) y se reintenta hasta `Agent:Session:RotationStartAttempts` veces (default 3, 1–5), con 500 ms entre intentos. Todo eso **sin tocar el anterior**.
  - Si fallan todos, la rotación lanza y queda el camino que ya existía: el tramo actual sigue grabando, aviso `rotation_delayed` y reintento a los `RotationRetrySeconds`.
- **Por qué no se exige que el archivo crezca:** scrcpy escribe por bloques de 256 KiB y con la pantalla quieta el MKV queda en 0 B durante el primer minuto (§4.2.5). Un chequeo de crecimiento rechazaría arranques sanos. La señal confiable es "proceso vivo".
- **"Pantalla siempre encendida":** sin cambios en los caminos. Un intento fallido no reafirma ni restaura nada: el valor lo sigue sosteniendo el scrcpy anterior. La reafirmación sigue después de cerrar el saliente y la restauración, al final.
- **Costo:** el solapamiento medido pasa de ~0,7 s a ~1,8 s por corte (marcador + 1,5 s de confirmación). Va dentro del tramo saliente; el texto del informe dice "alrededor de un segundo" y sigue siendo del orden correcto.
- `Models/AgentModels.cs` (`SessionOptions`: 2 claves con `Normalize`) y `appsettings.json` (las 2 claves).

**Tests:** `server/tests/Factum.Agent.Tests/Session/RotationStarterTests.cs`.
- `RotationStarterTests`, con fakes:
  - el nuevo sigue vivo → se acepta sin reintentos;
  - se cae a los 150 ms con una ventana de 1 s → se aborta y se reintenta;
  - se cae después de la ventana → no cuenta;
  - no arranca → se reintenta;
  - fallan todos → lanza, con el tope (el 4.º no se lanza) y todos abortados;
  - el último error se propaga;
  - defaults y rangos de las opciones.
- `RotationFailureStayAwakeTests`: un corte fallido no escribe el ajuste y la sesión sigue en el tramo 1. Un corte bueno reafirma 7 y el stop restaura 0.
- `dotnet test server/tests/Factum.Agent.Tests` → **821 passed**, 0 failed. `dotnet build` de Tatana → 0 warnings.

**Prueba con el SM-M127F (sin otros scrcpy abiertos):** Tatana de la rama en el puerto 8767, con `--data` en el scratchpad y tramos de 15 s (`MockSegmentSeconds`) durante 100 s.
- **6 de 6 cortes OK:**
  - 7 tramos `saved`, de 33–35 KB, con duración de 20,5–21,1 s sobre 17,6–18,1 s grabados;
  - solapamiento 1,8 s;
  - sin huecos salvo el `cambio_de_paso` final;
  - ningún "terminó solo" ni "el tramo nuevo terminó".
- `stay_on_while_plugged_in`: `0` antes y `0` después.
- **Limpieza:** el script de la prueba tiene un `trap EXIT`. Ese trap manda SIGTERM a Tatana, que corta y guarda la sesión. Después manda SIGTERM a los hijos que queden, `kill -KILL` a Tatana y `pkill` de cualquier scrcpy que grabe en la carpeta de la prueba. Al terminar no quedó ningún scrcpy ni ningún Tatana mío corriendo. Los datos de la prueba se borraron.
- Con las 2 caídas anteriores y esta corrida limpia, los fallos parecen ligados a otras instancias de scrcpy corriendo a la vez. Igual, el ajuste los absorbe: reintento sin hueco.

**Sobre los scrcpy sueltos (PIDs 90402 y 1555):**
- **No eran de mis pruebas.** Todo scrcpy que lanzó mi Tatana sale de `AdbService.BuildScrcpyArgs` con `--no-window` (`WindowMode=no_window`), sin TTY y como hijo de Tatana.
- En cambio, el 90402 corría con TTY `s009` en primer plano (`S+`) y sin `--record`, es decir, con ventana. Lo vi en un `ps` durante la primera prueba y lo anoté como ajeno.
- En las pruebas anteriores, mis Tatana de 8766/8767 se bajaron con `pkill` (SIGTERM) después de un `stop`, sin sesión activa. Ninguna de mis pruebas los dejó.

## Reintento 1 (QA)

Hallazgos de `docs/qa/2026-10-14/grabacion-sesion-completa-manual.md` que tocan al backend.

### H-1 (Crítica): horas con ticks contra las de Mongo, que guarda milisegundos

**Arreglo**, en `Services/Cases/RecordingSessionRules.cs`:
- `Utc(...)` ahora además trunca a milisegundos (`TruncateMs`). Lo usan todas las horas de `Validate`: `started_at`/`stopped_at` de los tramos, `from`/`to` de los huecos y `at` de las marcas.
- El merge compara el valor guardado truncado contra el entrante truncado, en `from`/`to` de los huecos y en `at` de las marcas.
- El `started_at` de los tramos no se compara en el merge (no es parte de la identidad), así que no hacía falta tocarlo ahí.
- Tatana sigue emitiendo ticks. Se normaliza en el backend, que es el que compara.

**Test:** `RecordingSessionFlowTests.Sync_HorasConMicrosegundos_SegundoSyncIdempotente`.
- El repositorio falso hace un round trip por BSON, con la misma precisión de ms que Mongo.
- El snapshot trae un tramo, un hueco cerrado, un hueco abierto y una marca, todos con microsegundos.
- Primer sync: 200. Segundo sync idéntico: 200, sin escrituras ni eventos.
- Las horas guardadas quedan sin fracción de ms.
- Un tercer sync que cierra el hueco abierto también da 200.
- El test de Mongo real (`MongoRecordingSessionRepositoryTests`, base efímera) sigue pasando.

**`DELETE /evidence` de un tramo durante la sesión (200).** La SDD (D-T6, §6.5) dice que los tramos no se quitan del caso. El 200 pasó porque el tramo todavía no estaba en la copia del diario, por H-1 o porque el sync aún no había llegado.
- **Arreglo:** `DeleteEvidenceAsync` también responde 409 `recording_session_file` si el caso tiene `RecordingSession` y el nombre cumple el regex de tramo. Tatana reserva el prefijo `sesion_`, así que ese nombre solo puede venir de la sesión.
- Test: `DeleteEvidence_TramoTodaviaNoSincronizado_409`.

### H-2 (Media): el detalle del caso no mostraba "Registro audiovisual"

**Confirmado:** `GET /api/cases/{id}` devuelve `recording_session` completo. El camino es `CaseService.GetAsync` → `LoadOwnedAsync` → `_repo.FindByIdAsync`, que no tiene proyección. Solo el listado (`ListByOfficerAsync`) lo excluye, a propósito (§6.1).
- No hay cambio de backend. El frontend carga el detalle con `getCase`.
- Test: `RecordingSessionFlowTests.GetCaso_TraeRecordingSession`.

### B-1: scrcpy no terminaba con SIGINT

**Causa:** cuando Tatana se lanza desde una shell que deja SIGINT ignorado (fue el caso de la QA y de mis pruebas, lanzadas en segundo plano), scrcpy hereda el SIG_IGN y SDL no instala su handler. Por eso SIGINT no hacía nada y se escalaba a SIGTERM a los 3 s.

**Arreglo:**
- `ProcessStop.StopAsync` acepta `unixSignal` (default `"-2"`; sin cambios para los clips puntuales).
- La sesión usa `ProcessStop.SessionSignal = "-15"`: SIGTERM directo y sin escalado, en `AndroidScreenTrack.StopCaptureAsync` / `AbortAsync` y en la recuperación de un scrcpy huérfano.
- SDL trata SIGTERM igual que SIGINT: cierra el archivo y sale con código 0.
- En Windows no cambia nada: sigue el Ctrl+C del helper.

**Prueba en el SM-M127F:** Tatana de la rama en el puerto 8767, `--data` en el scratchpad y tramos de 15 s.
- Los 5 cierres dieron "scrcpy detenido limpiamente (exit 0)", sin ningún "SIGINT sin efecto".
- Los 5 tramos quedaron `saved` y válidos.
- La duración ahora coincide con lo grabado (17,6 s sobre 17,9 s). Antes daba unos 3 s de más, que eran el tiempo hasta el SIGTERM.
- No se perdió el final de ningún archivo.

**Observación de la misma corrida:** sin ningún otro scrcpy corriendo, igual hubo 2 arranques fallidos del scrcpy nuevo en un corte. Uno murió a los 105 ms del marcador y el otro salió antes del marcador. El reintento del ajuste post-review los absorbió: 4 de 4 cortes sin hueco. Así que esas caídas son propias de este equipo y no las causaban los scrcpy ajenos; el reintento es necesario.

**Cierre de la prueba:**
- `stay_on_while_plugged_in` quedó en `0` antes y después.
- `trap EXIT` cerró Tatana y cualquier scrcpy de la prueba. Al terminar no quedó ningún scrcpy ni ningún Tatana mío corriendo.
- Todos los scrcpy lanzados usan `--no-window`, así que no se abrió ninguna ventana.
- Los datos de prueba se borraron.

### Textos y eventos

**B-2:** "tramos de hasta 1 minuto" en singular, con N = 1.
- Archivo: `ReportSessionTexts.Description`.
- Test: `Plural_UnMinuto`.

**B-3:** "Se grabaron …" usa `ReportSessionTexts.CoveredSeconds`, la unión de los intervalos de los tramos. Así los solapamientos de los cortes no se cuentan dos veces.
- Test: `Total_NoCuentaDosVecesElSolapamiento` (dos tramos de 60 s solapados 2 s más uno de 0,5 s dan 118,5 s).

**B-4:** el detail de `recording_session_segment_closed` suma `recorded_seconds` (snake_case_lower).
- Campo `CaseEventDetail.RecordedSeconds`, con `[BsonIgnoreIfNull]` y `JsonIgnore(WhenWritingNull)`.
- Lo pide el frontend para que la trazabilidad muestre la misma duración que el informe.
- Test en `RecordingSessionEventsTests.LosCincoTipos`, que verifica el JSON `"recorded_seconds":1800.7`.

**B-5:** `recording_session_resumed` siempre lleva `segment_index`.
- Si el tramo que abre la reanudación ya está en la copia del diario, se toma de ahí.
- Si no llegó todavía (se sincroniza al cerrarse), se calcula como el índice máximo de la pista entre los tramos que empezaron antes de `to`, más 1. La numeración es correlativa por pista.
- Test: `Resumed_SinElTramoNuevoTodavia_SegmentIndexInferido`.

### Verificación del reintento 1

- `dotnet build` de Tatana: 0 warnings. `dotnet build` del backend: 0 errores (solo los `NU190x` de paquetes de siempre).
- `dotnet test Factum.Agent.Tests`: **821 passed**.
- `dotnet test Factum.Backend.Tests`: **1443 passed**, 11 skipped (los `[MongoFact]` sin variable de entorno).
- Con `FACTUM_TEST_MONGO`, `MongoRecordingSessionRepositoryTests`: passed, sobre una base efímera que se borra al final.
- **Contrato:** el único campo nuevo es `recording_session_segment_closed.detail.recorded_seconds`. Nada se renombró ni se sacó.
