# impl_backend — wizard-concurrencia-pestanas (#61, Bloque 1)

**Estado:** done (sin commit, por indicación del orquestador) · rama `feat/wizard-concurrencia-pestanas` sobre `ddf94bb`
(develop con #59 `grabacion-android-robusta` y #60 `tatana-ws-y-hosts` ya mergeadas).
SDD: `Refactorizaciones/wizard-concurrencia-pestanas.md` · HU: `docs/hu-wizard-concurrencia-pestanas.md`.

## Reconciliación con #59 / #60 (la SDD se escribió antes de esos merges)

| Tema | SDD | Código actual | Qué hice |
|---|---|---|---|
| `case_id` de la grabación | `RecordingAttributionRegistry` nuevo como fuente única | #59 ya guarda `case_id` en `AdbService`/`IosService` (snapshot de la grabación), lo devuelve en el stop (`r.CaseId`, `ex.Data[ExDataCaseId]`) y en `/agent/state` | **No dupliqué `case_id`.** Se sigue tomando del servicio. Para `origin_id` extendí el `RecordingRegistry` de #59 (sin singleton nuevo) con `SetOwner`/`TakeOwner`/`PeekOwner` y el record `RecordingAttribution(Serial, CaseId, OriginId, Since)`. Es lo que pide §12 ("si #59 se mergea primero… reutiliza ese y solo suma `origin_id`"). |
| `case_id` inválido en `record/start` | `EventAttribution.Normalize` → null, nunca 400 | #59 valida con `AgentFileNames.TryParseCaseId` (GUID) → `400 invalid_case_id` | Mantuve la validación de #59 (contrato ya mergeado). `origin_id` del body sí pasa por `Normalize` (inválido = null). |
| `recording_stopped` | Sumar `serial`, `case_id`, `origin_id` | #59 ya manda `serial` y `case_id` (`RecordingStoppedEvent`), también en el 422 `recording_empty`/`recording_unreadable` con `filename: null` | Sumé `OriginId` como **último** parámetro de `RecordingStoppedEvent`, con default null. En el WS sale `origin_id` en los dos caminos (éxito y 422). |
| `recording_started` | `{ serial, platform, case_id, origin_id }` | #59 ya manda `{ serial, platform, case_id, started_at, filename }` | Sumé `origin_id`. |
| Stop / 409 `not_recording` | Conservar el `catch` de #60 + `Take` en `finally` | Está el `catch (RecordingNotActiveException)` → 409 | Lo conservé. `TakeOwner` va en un `finally`. |
| Hub WS (#60) | No se toca | Cola por cliente, `SnakeCaseLower`, null **no** se omite | No lo toqué. Los campos nuevos viajan por el `Data` anónimo o el record (verificado por WS, abajo). |
| Filtro de Host (#60) | — | `HostPolicy` filtra por **header `Host`**, no por path | `/previews/*` no necesita nada. Funcionó con `Host: localhost:8766` sin cambios. CORS y guarda de origen sin tocar. |

## Archivos tocados

**Tatana (`server/src/Factum.Agent`)**
- `Common/EventAttribution.cs` (nuevo): `record EventAttribution(CaseId, OriginId)`, `None`, `From`, `Normalize` (trim + `^[A-Za-z0-9_-]{1,64}$`; si es GUID lo devuelve canónico en minúsculas, igual que `TryParseCaseId` de #59).
- `Services/RecordingRegistry.cs`: `SetOwner`/`TakeOwner`/`PeekOwner` + `record RecordingAttribution`.
- `Controllers/RecordingController.cs`: `StartRecordingRequest.OriginId` (último, default null). `SetOwner` después de que el servicio confirmó. `recording_started` con `origin_id`. Stop: query `case_id`/`origin_id` (`[FromQuery(Name=…)]`), `ResolveAttribution` (case_id: servicio → registro → query; origin_id: registro, y si **no hay registro**, query) y `TakeOwner` en `finally`. `RecordingStoppedEvent` + `OriginId`.
- `Controllers/ScreenshotController.cs`: query `case_id`/`origin_id`. `screenshot_taken` = `{ filename, url, serial, case_id, origin_id }`.
- `Controllers/WebcamController.cs`: `CaptureRequest(Type, CaseId = null, OriginId = null)`. `photo_taken` suma `case_id`/`origin_id` (sin `serial`).
- `Controllers/AirplayShotController.cs` + `Services/IosService.cs`: `StopAirplayShotSessionAsync(serial, attribution, ct)`. Cada `screenshot_taken` de las marcas lleva `serial` (el de la ruta), `case_id` y `origin_id`. No hay fakes de `IIosService` en tests.
- `Services/FileStorageService.cs`: `PreviewDirectory`, `PreviewKey` (12 hex del SHA-256), `IsPreviewName` (regex de §5.1 con `\z`), `NewPreviewPath`, `PrunePreviews`, `ClearPreviews`, `PreviewGate(serial)` (SemaphoreSlim por clave) y `ResolvePreview(filename)`. `ListFiles`/`Delete` sin cambios. El ctor acepta un `ILogger` opcional.
- `Controllers/PreviewController.cs` (nuevo): `POST devices/{serial}/preview` (lock por clave → captura Android/iOS igual que screenshot → poda → `{ filename, url: "/previews/…" }`, **sin broadcast**) y `GET previews/{filename}` (`image/png`, `Cache-Control: no-store`, 404 `{ error: "Archivo no encontrado" }`).
- `Program.cs`: `ClearPreviews()` al arrancar, junto a las otras limpiezas, con "vistas previas borradas: N" en el log de inicio. No hizo falta registrar un singleton nuevo.
- `Controllers/HealthController.cs`: `capabilities` += `event_attribution_v1`, `device_preview_v1`.

**API (`server/src/Factum.Backend`)**
- `Models/Case.cs`: `Case.EvidenceRevision` (`int`, `[JsonIgnore]`), `ReportTexts.OperacionesBase` (`ReportCounts?`, `[BsonIgnoreIfNull]`), clase `ReportCounts` (`[BsonIgnoreExtraElements]`).
- `Models/CaseEvent.cs`: `CaseEventDetail.PreviousSha256` y `FailedAction` (`[BsonIgnoreIfNull]` + `[JsonIgnore(WhenWritingNull)]`). `CaseEventTypes.EvidenceUpdated`/`CaptureFailed`.
- `Models/AgentEvent.cs`: `AgentAction.CaptureFailed` **al final** (= 5). `AgentEvent.FailedAction`/`Reason` (`[BsonIgnoreIfNull]`) + `[BsonIgnoreExtraElements]`.
- `DTOs/AgentEventDtos.cs`: `ReportAgentEventRequest(..., AgentAction? FailedAction = null, string? Reason = null)`.
- `DTOs/CaseDtos.cs`: `ReportTextsDto.OperacionesBase` (último, default null) + `record ReportCountsDto(Capturas, Grabaciones, ArchivosExtraidos, Otros)`.
- `Controllers/AgentAuditController.cs`: valida `capture_failed` (si falta `failed_action` o no es una de las cuatro capturas → `400 { error: "Falta la acción que falló (failed_action)" }`, sin insertar). `Reason` con trim, truncado a 300 sin partir un par sustituto, y vacío = null. `MapCaptureType` (ahora `internal static`) + `CaptureFailed → capture_failed`, con `Detail { FailedAction = MapCaptureType(failed), Reason }`. Helpers `IsFailableAction`/`TruncateReason` `internal static`. `api-hardening` (#62/#63) **no** está mergeada: no hay limpiador de texto que aplicar.
- `Services/Cases/EvidenceManifest.cs`: `PlanRegistration` + `RegistrationPlan`, `EvidenceErrorCodes.EvidenceBusy`, `EvidenceBusyMessage`. `Upsert` queda (tiene tests propios).
- `Infrastructure/MongoRepository.cs` (+ fake `Admin/AdminTestDoubles.cs`): `RegisterEvidenceAsync(id, expectedRevision, …)` con filtro de revisión (0 = `Eq 0` **o** `Exists false`) y `$inc`. `RemoveEvidenceAsync` hace `$inc` en sus dos pasos (pull y unset del host). `AppendReportVersionAsync(…, expectedCount, …)` con `Size`/(`Size 0` **o** `Exists false`).
- `Services/Cases/AgentGeneration.cs`: `RegisterEvidenceAsync` con un loop de hasta 3 intentos (cargar → validar → planificar). `!Changed` → 200 con el manifiesto guardado, sin escribir y sin eventos. Escribe → `evidence_added`/`evidence_updated` (best-effort, después de la escritura que matcheó). 3 carreras seguidas → `409 evidence_busy`.
- `Services/Cases/CaseService.cs`: `AppendGenerateVersionAsync` con append condicional, una relectura y replanificación, y después `Warning`. Aplica a los dos llamadores. `SaveReportTextsAsync` valida, guarda o conserva `OperacionesBase`.
- `Services/Cases/CaseValidation.cs`: `IsValidReportCounts` (0..100000), `InvalidReportCountsMessage`.
- `Services/Reports/ReportValues.cs`: `RenderDefaults` llena `OperacionesBase` con `Counts(files, cas)`.

**Tests**
- Agent: `EventAttributionTests.cs` (incluye `AgentCapabilitiesTests`), `RecordingAttributionTests.cs`, `PreviewStorageTests.cs` (carpeta temporal propia que se borra en `Dispose`).
- Backend: `EvidenceRegistrationPlanTests.cs` (B3), `AgentAuditMappingTests.cs` (B8 + enum al final + truncado), `ReportTextsValidationTests.cs` += B10 (`OperacionesBase` = `Counts`, ceros, rango, null y JSON snake).

## Decisiones no obvias

1. **Reintento con jitter** en `RegisterEvidenceAsync`: antes de releer espera `Random(15..60) ms × intento`. Sin la espera, con 4 PUT simultáneos de archivos distintos, 1 de cada 4 terminaba en `409 evidence_busy` (los cuatro releían en el mismo instante). Con la espera: 40/40 OK. Sigue siendo como mucho 3 intentos.
2. **Registro no-op:** devuelve el manifiesto **guardado** y no reescribe el host (antes cada registro actualizaba `agent_version`/`case_directory`). Es lo que pide DT10. Ojo: el `registered_at` de la respuesta del primer registro tiene precisión de ticks y el leído de Mongo, de ms. Ya pasaba antes. El valor guardado no cambia.
3. **`start` sin atribución registra igual un dueño con nulls** (SDD DT4 literal: `Set` siempre después de confirmar). Así un `stop` con query **no** pisa la atribución de una grabación que se inició sin ella. Los valores del query se usan solo si no hay registro (Tatana reiniciado o grabación iniciada por un Tatana viejo).
4. Un **segundo `start` rechazado** (`already_recording`) no toca el registro: la atribución original sobrevive (escenario A5, verificado).
5. `EventAttribution.Normalize` pasa un GUID a su forma canónica en minúsculas, para que el `case_id` de una captura y el de una grabación (#59) del mismo caso sean idénticos.
6. `ClearPreviews` borra todo lo que hay en `previews/` (archivos y subcarpetas; un enlace se borra como enlace, sin seguirlo). Los errores de IO se loguean y no frenan el arranque.

## Contrato: coincide con la SDD, con estas precisiones para el implementer-frontend

- Nombres JSON **idénticos** a §6: `serial`, `case_id`, `origin_id` en `screenshot_taken`/`recording_started`/`recording_stopped`. `photo_taken` lleva `case_id`/`origin_id` sin `serial`. Query `case_id`/`origin_id` en `screenshot`, `record/stop` y `screenshot/airplay/stop`; body `case_id`/`origin_id` en `record/start` y `webcam/capture`. Además `POST /devices/{serial}/preview` → `{ filename, url: "/previews/<filename>" }`, `GET /previews/{filename}`, `capabilities` += `event_attribution_v1`/`device_preview_v1`. Backend: `action: "capture_failed"`, `failed_action`, `reason`, `detail.previous_sha256`, `detail.failed_action` (`capture_video_start|capture_video_stop|capture_screenshot|capture_photo`), `operaciones_base { capturas, grabaciones, archivos_extraidos, otros }`, `code: "evidence_busy"`.
- **Diferencia por #59:** en `POST /devices/{serial}/record/start`, un `case_id` que no es un GUID devuelve **`400 { error, code: "invalid_case_id" }`** (no se convierte en null, como decía la SDD). El cliente manda el id del caso del backend (GUID), así que no debería pasar. `origin_id` inválido sí pasa a null.
- `case_id`/`origin_id` con forma de GUID vuelven **en minúsculas** en los eventos.
- `recording_stopped` (#59) también se emite en un stop fallido 422 con `filename: null`, `url: null` y `error`/`code`. Ahora ese evento también lleva `origin_id`.
- `GET /api/cases/{id}` devuelve `{ cas, files }`: `operaciones_base` está en `cas.report_texts.operaciones_base`. `evidence_revision` **no** sale en el JSON (verificado).

## Verificación

- `dotnet build server/src/Factum.Agent/Factum.Agent.csproj` → Build succeeded, sin warnings.
- `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` → Build succeeded. Solo los NU1902/NU1903 de NuGet (SharpCompress/Snappier) que ya estaban.
- `dotnet test server/tests/Factum.Agent.Tests` → **Passed 531/531**.
- `dotnet test server/tests/Factum.Backend.Tests` → **Passed 920, Skipped 7 (preexistentes), Failed 0**.
- Warnings CS de los tests: solo los dos que ya había en `EvidenceZipTests.cs`, ninguno nuevo.
- `./ops/harness/verify.sh` → "Arnés OK" (client tsc limpio en ese momento, API y Tatana build limpio).

### T15 — Tatana propia en mock (`:8766`, `--data` y `Agent__EvidenceDirectory` en el scratchpad, `--local-config` inexistente; la de `:8765` no se tocó)
Eventos WS reales (cliente WS de Node con `Origin: http://localhost:3000`):
```
screenshot_taken {"filename":"screenshot_…096.png","url":"/files/…","serial":"EMU001","case_id":"c1","origin_id":"o1"}
screenshot_taken {… "serial":"EMU001","case_id":null,"origin_id":null}                        (sin query)
screenshot_taken {… "serial":"EMU001","case_id":null,"origin_id":"ba916e4f-…-0c1d2e3f4a5b"}   (case_id=../x → null; GUID en mayúsculas → minúsculas)
recording_started {"serial":"EMU001","platform":"android","case_id":"ba916e4f-…","origin_id":"o1","started_at":…,"filename":"grabacion_….mkv"}
recording_stopped {"filename":…,"url":…,"platform":"android","serial":"EMU001","case_id":"ba916e4f-…",…,"error":null,"code":null,"origin_id":"o1"}   (stop SIN query)
recording_started {… "case_id":null,"origin_id":null}  →  stop ?case_id=qc&origin_id=qo  →  recording_stopped case_id:null, origin_id:null (hay registro con nulls; decisión 3)
start origin A → 2º start origin B = 409 already_recording → stop ?origin_id=Z → recording_stopped origin_id:"A"
```
- Preview: dos `POST …/preview` seguidos → **ningún** evento WS y **un** solo archivo en `previews/`. `GET` de la URL → `200 image/png` + `Cache-Control: no-store`. La preview anterior (podada) → 404. `GET /previews/../appsettings.json` y `/previews/..%2F…` → 404. `GET /files/<preview>` → 404, y `GET /files` no la lista.
- Reinicio → log "vistas previas borradas: 1", `previews/` vacía y la raíz con la misma cantidad de entradas.
- `/health` incluye las dos capabilities.
- **No verificado en mock:** `photo_taken` (`POST /webcam/capture`). El mock de la webcam falla **desde antes de esta HU**: `WebcamService.MockJpegBytes` tiene un base64 inválido (initial commit), devuelve 500 y no emite el evento. Está fuera de alcance y la web no usa la webcam de Tatana. El cambio es trivial y lo cubre el build.
- **AirPlay** no se puede probar en mock ni sin iPhone. Queda pendiente para el usuario con un iPhone real, si aplica.
- Instancia detenida y `tatana-qa-data` borrada.

### B12 — API con instancia propia (`PORT=8089`, `factum_dev`, `Storage__DataDirectory` en el scratchpad). **No se reinició el backend de `:8080`**
Login dev con DNI de prueba `99000001` (sin datos de negocio previos: 0 casos/eventos/perfil; el `users` y los 2 `user_admin_events` de ese DNI son anteriores, del 2026-10-06, y **no se tocaron**).
1. Registro `screenshot_x.png`/H1 → 1 `evidence_added`. Repetido igual → manifiesto idéntico en la base y 0 eventos nuevos. Con H2 → 1 `evidence_updated`, `detail = { sha256: H2, size: 11, previous_sha256: H1 }`. Repetido con H2 → sin eventos.
2. Paralelo con archivos distintos: 2 a la vez × 10 rondas → 20/20 en el manifiesto y 20 `evidence_added`. 4 a la vez × 10 rondas (con jitter) → 40/40 y 40 eventos. El mismo archivo y hash ×4 en paralelo → **1** `evidence_added`. `evidence_revision` no sale en el JSON.
3. `capture_failed` + `failed_action: "capture_start"` + motivo con espacios → 204. Evento `capture_failed`, `detail = { reason: "Ya hay una grabación de Android en curso.", failed_action: "capture_video_start" }`, con hostname. En Mongo: `Action: 5, FailedAction: 1`. Sin `failed_action` → `400 {"error":"Falta la acción que falló (failed_action)"}`. `failed_action: "startup"` → 400.
4. Defaults → `operaciones_base {capturas:3, grabaciones:1, archivos_extraidos:0, otros:0}`. PUT con la base → la devuelve. PUT sin el campo → se conserva, y `GET` del caso → `cas.report_texts.operaciones_base` presente. Base con -1 → `400 {"error":"Las cantidades de Operaciones realizadas no son válidas"}`.
5. Prepare en paralelo: con textos iguales a la última versión "save" no se agrega ninguna (de-dup de siempre). Para forzar una versión distinta saqué con `$pop` la última versión **de mi propio caso**. Después, 4 `prepare` en paralelo × 3 rondas → cada ronda `["save","generate"]`: **una** sola "generate".

**Limpieza (solo por `_id`):** 2 casos de prueba (`9af7b8cd-3326-42a5-b086-b84b0d130baa`, `9e28f9d9-e64b-4583-95f0-474651bc45c7`), sus 57 + 42 `case_events` (lista de `_id`), 1 `agent_events` (`9c955662-…`), 6 `catalog_entries` que sembró la creación de esos casos (3 + 3, `OwnerDni 99000001`, creados en la prueba) y el `expert_profiles` `99000001` (creado en la prueba, borrado las dos veces). Estado final: `cases` = 5, como al empezar, y 0 restos del DNI de prueba salvo el usuario y los eventos de admin preexistentes. Las instancias propias (8766, 8089) quedaron detenidas y sus carpetas del scratchpad, borradas.
- Aviso: `dotnet build` del Backend reescribió `server/src/Factum.Backend/bin/Debug/net10.0` mientras el proceso de `:8080` (pid 8723) corre desde ahí. El proceso no se reinició y sigue con el código viejo en memoria. **Para probar a mano hay que reiniciar el backend de `:8080`** (y levantar el Tatana nuevo).

## Pendiente para el usuario
- Reiniciar el backend `:8080` y Tatana `:8765` con esta rama antes de la prueba manual de §10.3.
- Probar con el Samsung real (USB) el `POST /devices/{serial}/preview` y la grabación atribuida (§10.3 pasos 1–3, 7). En mock se verificaron los eventos, no la captura real.
- AirPlay (marcas con atribución) con iPhone real, si aplica.
