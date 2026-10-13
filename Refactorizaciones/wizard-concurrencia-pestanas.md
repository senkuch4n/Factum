# SDD: El wizard de inspección no mezcla evidencia entre pestañas ni registra eventos falsos (Bloque 1)

**Slug:** `wizard-concurrencia-pestanas` · **Issue:** #61 · **HU:** `docs/hu-wizard-concurrencia-pestanas.md`
(D1–D11 validadas con la opción recomendada, 2026-10-13).
**Fuente QA:** `docs/qa/2026-10-13/e2e-navegador.md` (A1, A3, A4, A5, A6, M2, M8, B6 vistas previas).
**Alcance (D10 A):** solo **Bloque 1**. M6, M1, M3, M4, B5 y el nombre del ZIP (B6 ZIP) van a `wizard-pulido-ux`.

---

## 1. Resumen funcional

Cada captura, foto o grabación que informa Tatana pasa a llevar el **serial** del equipo, el **caso** y la **pestaña**
(`origin_id`) que la pidieron. Cada pestaña importa solo lo suyo. La vista previa del paso 2 deja de ser evidencia:
usa un endpoint propio de Tatana que no emite eventos y guarda una sola imagen por celular en `agent-data/previews/`,
que se vacía al arrancar Tatana. Los botones que crean o generan cosas (Generar, Grabar/Detener, Pantalla, AirPlay)
quedan protegidos contra el doble clic con un guard sincrónico. Generar el mismo caso en dos pestañas se corta con un
lock entre pestañas. El backend registra evidencia de forma idempotente: un re-registro con el mismo SHA-256 no hace
nada y uno con otro SHA-256 deja un evento nuevo, "Se actualizó evidencia". Además resuelve la carrera entre dos
pestañas con concurrencia optimista. La cadena de custodia anota el inicio y el fin de una captura recién cuando Tatana
los confirma, y deja un evento nuevo `capture_failed` con el motivo cuando Tatana rechaza. Borrar evidencia abre un
`ConfirmDialog` propio. El paso 5 avisa cuando "Operaciones realizadas" se armó con otras cantidades, y el paso 4
ofrece "Actualizar cantidades". El historial se recarga para que el aviso de expediente repetido funcione con casos
creados en la misma sesión o en otra pestaña. Si dos pestañas del wizard usan el mismo celular, se muestra un aviso
informativo.

## 2. Toca

| Lado | ¿Toca? | Qué |
|---|---|---|
| **backend (API)** `server/src/Factum.Backend` | **sí** | Registro de evidencia idempotente con `evidence_updated` y concurrencia optimista (`EvidenceRevision`). Acción de auditoría `capture_failed` → evento de caso `capture_failed`. `operaciones_base` en `report_texts` y en los defaults. Append condicional del snapshot de versión en `prepare` (sin duplicar por carrera). Tests xUnit. |
| **backend (Tatana)** `server/src/Factum.Agent` | **sí** | `serial`/`case_id`/`origin_id` en `screenshot_taken`, `photo_taken`, `recording_started` y `recording_stopped`. Registro de atribución de grabaciones. Endpoint nuevo `POST /devices/{serial}/preview` + `GET /previews/{filename}`, carpeta `previews/` con limpieza. Capabilities nuevas. Tests xUnit. |
| **client** `client/` | **sí** | Filtro de eventos por pestaña. `origin_id` por carga de página. `BroadcastChannel` (presencia y `cases_changed`). Web Locks para Generar. Guards de doble clic. Reporte de auditoría después de la confirmación de Tatana y `capture_failed`. `ConfirmDialog` asíncrono para borrar. `SafeImg` y `storedInCase` temprano (M8). Aviso de conteos en el paso 5 y "Actualizar cantidades" en el paso 4. Recarga del historial. Línea de tiempo con los tipos nuevos. |
| **agent-ui** | **no** | No consume estos eventos ni endpoints (verificado con `grep` en `agent-ui/src`). |

Campo `Toca` del Project: `backend+client`.

---

## 3. Diagnóstico del arquitecto (leído en el código actual de `develop`, 36140d4)

- **A1 / M8.** `handleWsEvent` (`client/src/app/dashboard/page.tsx:250-301`) importa `screenshot_taken` en cualquier
  paso salvo el 2, y `photo_taken`/`recording_stopped` siempre, sin mirar serial ni caso. `CaseFormStep.takeShot`
  (`components/CaseFormStep.tsx:537-555`) usa `agent.takeScreenshot` → `ScreenshotController`, que guarda en la raíz
  y emite el evento. Causa del M8: `SafeImg` (`components/capture/StageScreen.tsx:13-24`) guarda `errored=true` y
  **no lo resetea cuando cambia `src`**. Mientras dura el guardado, la URL es `/files/<nombre>` (`gallery.ts:80`,
  porque `storedInCase` todavía es `false`). Si el archivo se mueve justo en ese momento, el `<img>` falla y queda en
  el ícono aunque después `src` pase a la URL del caso.
- **A3.** Hay **dos** problemas. (a) En `handleGenerateAgent` (`page.tsx:489-…`), el `await ensureAgentIdentity(true)`
  va antes de `setLoad("generate", true)`. (b) Entre dos pestañas, el `prepare` de la pestaña 2 **pisa**
  `PendingGeneration` (`AgentGeneration.cs:173-183`) y el `finish` de la pestaña 1 cae en `generation_stale`. El 409
  `zip_in_progress` de Tatana llega tarde, porque el `prepare` ya pisó el intento. Por eso, para cumplir "la generación
  de la pestaña 1 termina bien", la pestaña 2 **no puede llamar a `prepare`**: hace falta un lock entre pestañas
  (DT9). El snapshot duplicado de versión viene de una **carrera**. `ReportVersioning.GenerateVersionFor` ya descarta
  una versión idéntica a la última, pero los dos `prepare` leen el caso antes de que cualquiera escriba.
- **A5.** `RegisterEvidenceAsync` (`AgentGeneration.cs:41-105`) hace lectura-modificación-escritura sin control de
  concurrencia y emite un `evidence_added` por ítem siempre. Dos pestañas que registran a la vez generan dos eventos y,
  si registran archivos distintos, **una pisa a la otra** (pérdida de un ítem del manifiesto: bug latente que esta HU
  también cierra). `capture_start`/`capture_stop` se reportan antes de llamar a Tatana (`page.tsx:999-1010`,
  `:1037-1041`).
- **A6 (confirmado por lectura de código; la reproducción manual queda en §11.3 paso 0).** `startWizard`
  (`page.tsx:605`) llama a `resetWizard({ keepMode: true })`, que **no** recarga `historyCases`. `handleSaveCase`
  tampoco agrega el caso creado al historial. En el QA, el segundo `QA-ROBUSTEZ-001` se creó en la misma sesión
  después del primero, sin volver a la lista. `findSameExpediente` compara contra un historial viejo y no lo encuentra.
- **A4.** El texto de "Operaciones realizadas" se arma una sola vez con `GET /report-texts/defaults`
  (`ReportStep.tsx:241-245`) y queda guardado como texto libre. No hay ninguna marca de con qué cantidades se armó.

---

## 4. Modelo de datos

### 4.1 MongoDB (`factum` / `factum_dev`). Nombres de campo en Mongo = nombre de la propiedad C# (no hay `ConventionPack`)

Sin migraciones. Ningún documento existente se reescribe. Todos los campos nuevos son tolerantes a su ausencia.

| Colección | Clase | Campo nuevo (Mongo) | Tipo | Documento viejo sin el campo |
|---|---|---|---|---|
| `cases` | `Case` | `EvidenceRevision` | `int`, default 0, **`[JsonIgnore]`** (no sale en el JSON) | deserializa 0; el filtro trata `0` = "0 o inexistente" (§6.2.1) |
| `cases` | `ReportTexts` (sub-doc `ReportTexts`) | `OperacionesBase` | `ReportCounts?`, `[BsonIgnoreIfNull]` | null → no hay aviso de conteos (DT13) |
| `cases` | `ReportCounts` (nueva, `[BsonIgnoreExtraElements]`) | `Capturas`, `Grabaciones`, `ArchivosExtraidos`, `Otros` | `int` cada uno | — |
| `case_events` | `CaseEventDetail` | `PreviousSha256` | `string?`, `[BsonIgnoreIfNull]` + `[JsonIgnore(WhenWritingNull)]` | — |
| `case_events` | `CaseEventDetail` | `FailedAction` | `string?`, ídem | — |
| `case_events` | `CaseEvent.Type` | valores nuevos `evidence_updated`, `capture_failed` (`CaseEventTypes`) | string literal | — |
| `agent_events` | `AgentEvent` | `FailedAction` | `AgentAction?`, `[BsonIgnoreIfNull]` | — |
| `agent_events` | `AgentEvent` | `Reason` | `string?`, `[BsonIgnoreIfNull]` | — |
| `agent_events` | `AgentAction` (enum, **se guarda como int**, verificado en `factum_dev`: `Action: 0..3`) | valor nuevo `CaptureFailed` **al final** (= 5) | enum | — |

- `AgentEvent` **no** tiene hoy `[BsonIgnoreExtraElements]`. Se le agrega, para que un binario anterior (rollback) no
  rompa al leer documentos con los campos nuevos.
- `ReportTextSnapshot` (versionado) **no** incluye `OperacionesBase`. El historial de versiones no cambia.
- No hay índices nuevos.

### 4.2 Disco de Tatana

| Ruta | Qué | Ciclo de vida |
|---|---|---|
| `<Agent:DataDirectory>/previews/` | Vistas previas del paso 2, `preview_<k>_<yyyyMMdd_HHmmss_fff>.png`, con `<k>` = primeros 12 hex del SHA-256 (UTF-8) del serial | Se crea al arrancar. Al arrancar se borra **todo su contenido** (solo esa carpeta). Al guardar una vista previa nueva de un serial, se borran las anteriores de **ese** `<k>`. |

`GET /files` (`FileStorageService.ListFiles`) solo enumera la raíz, y `CaseEvidenceStore.ImportAsync` solo mueve
desde la raíz. Una vista previa **nunca** puede entrar a un caso.

---

## 5. Endpoints y mensajes WebSocket

### 5.1 Tatana (`localhost:8765`, JSON `snake_case_lower`; el hub WS serializa con `SnakeCaseLower` y **sin** omitir null)

| Método y ruta | Cambio |
|---|---|
| `POST /devices/{serial}/screenshot?platform=&method=&case_id=&origin_id=` | Query nuevos opcionales `case_id`, `origin_id`. Respuesta sin cambios `{ filename, url }`. El evento `screenshot_taken` suma `serial`, `case_id`, `origin_id`. |
| **`POST /devices/{serial}/preview?platform=&method=`** (nuevo) | Toma la captura con el mismo código que `screenshot` (Android/iOS), la guarda en `previews/` y **no emite ningún evento WS**. `200 { filename, url }` con `url = "/previews/<filename>"`. Errores con la misma forma que `screenshot`: `500 { error, code? }`. |
| **`GET /previews/{filename}`** (nuevo) | Sirve el PNG con `Cache-Control: no-store`. Nombre que no matchea `^preview_[0-9a-f]{12}_\d{8}_\d{6}_\d{3}\.png$` o que no existe → `404 { error: "Archivo no encontrado" }`. |
| `POST /devices/{serial}/record/start` | Body suma `case_id?`, `origin_id?` (opcionales). Si Tatana confirma, guarda la atribución en `RecordingAttributionRegistry` (DT4). `recording_started` suma `case_id`, `origin_id`. |
| `POST /devices/{serial}/record/stop?platform=&case_id=&origin_id=` | Query nuevos opcionales. `recording_stopped` suma `serial`, `case_id`, `origin_id`, **tomados del registro de la grabación** (los del inicio). Solo si no hay registro se usan los del stop. Respuesta HTTP sin cambios. |
| `POST /devices/{serial}/screenshot/airplay/stop?case_id=&origin_id=` | Query nuevos opcionales. Cada `screenshot_taken` de las marcas extraídas suma `serial` (el de la ruta), `case_id`, `origin_id`. Respuesta sin cambios `{ files: [{ filename, url }] }`. |
| `POST /webcam/capture` | Body suma `case_id?`, `origin_id?`. `photo_taken` los suma (sin `serial`: la webcam no es del celular). |
| `GET /health` | `capabilities` suma `"event_attribution_v1"` y `"device_preview_v1"`. |

Validación de `case_id`/`origin_id` en Tatana (`EventAttribution.Normalize`): trim; si no matchea
`^[A-Za-z0-9_-]{1,64}$`, se trata como **null** (nunca 400: el dato es solo atribución).

### 5.2 Backend API (`:8080`, JSON `snake_case_lower`, enums `JsonStringEnumConverter(SnakeCaseLower)`)

| Método y ruta | Cambio |
|---|---|
| `PUT /api/cases/{id}/evidence` | Misma request y respuesta. Semántica nueva (§6.2.1): ítem idéntico (mismo `filename` y `sha256`) → no-op sin evento; `sha256` distinto → `evidence_updated`; nuevo → `evidence_added`. Si **ningún** ítem cambia, no escribe y devuelve el manifiesto actual. Error nuevo: `409 { error, code: "evidence_busy" }` si después de 3 intentos sigue la carrera. |
| `POST /api/agent-events` | `action` acepta `"capture_failed"`. Campos nuevos opcionales `failed_action` (uno de `capture_start`/`capture_stop`/`screenshot`/`webcam`) y `reason` (string). Con `capture_failed`, `failed_action` es obligatorio → si falta, `400` de validación (`{ error }`). `reason` se recorta (trim) y se trunca a 300. Respuesta `204` sin cambios. |
| `GET /api/cases/{id}/events` | Tipos nuevos `evidence_updated` y `capture_failed`. `detail` suma `previous_sha256` (en `evidence_updated`) y `failed_action` (en `capture_failed`, con el **tipo de evento de caso** que se habría registrado: `capture_video_start` / `capture_video_stop` / `capture_screenshot` / `capture_photo`). `capture_failed` usa el `detail.reason` que ya existe. |
| `GET /api/cases/{id}/report-texts/defaults` | Suma `operaciones_base: { capturas, grabaciones, archivos_extraidos, otros }` (conteos actuales con `ReportValues.Counts`). |
| `PUT /api/cases/{id}/report-texts` | Body suma `operaciones_base?` (mismo shape). Presente → se guarda. Ausente o null → **se conserva** el valor guardado. Cada conteo tiene que ser un entero entre 0 y 100000; si no, `400 { error: "Las cantidades de Operaciones realizadas no son válidas" }`. La respuesta (`ReportTexts`) incluye `operaciones_base`. |
| `GET /api/cases/{id}` | `report_texts.operaciones_base` (si existe). |
| `POST /api/cases/{id}/generate/prepare` | Sin cambio de contrato. El snapshot "generate" se agrega con append condicional (§6.2.4). |

### 5.3 Mensajes entre pestañas (solo `client/`, `BroadcastChannel("factum-wizard")`)

```ts
type TabMessage =
  | { v: 1; type: "presence"; origin_id: string; serial: string; case_id: string | null; case_label: string | null; step: number; at: number }
  | { v: 1; type: "bye"; origin_id: string }
  | { v: 1; type: "cases_changed"; origin_id: string }
  | { v: 1; type: "generating"; origin_id: string; case_id: string; active: boolean; at: number };
```

Se ignora todo mensaje con `v !== 1` o con forma inválida. `at` = `Date.now()` del emisor.

---

## 6. Contrato compartido

### 6.1 Tatana ↔ client

| Mensaje / endpoint | Campo JSON exacto | Tipo | Lado Tatana | Lado client |
|---|---|---|---|---|
| WS `screenshot_taken.data` | `filename`, `url` | string, string | `Controllers/ScreenshotController.cs`, `Services/IosService.cs` (marcas AirPlay) | `ScreenshotTakenData` en `client/src/lib/agent.ts`; consumo en `client/src/app/dashboard/page.tsx` |
|  | `serial` | string | ídem | ídem |
|  | `case_id` | string \| null | ídem | ídem |
|  | `origin_id` | string \| null | ídem | ídem |
| WS `photo_taken.data` | `type`, `filename`, `url`, `case_id`, `origin_id` | string, string, string, string\|null, string\|null (sin `serial`) | `Controllers/WebcamController.cs` | `PhotoTakenData` en `lib/agent.ts` |
| WS `recording_started.data` | `serial`, `platform`, `case_id`, `origin_id` | string, `"android"\|"ios"`, string\|null, string\|null | `Controllers/RecordingController.cs` | `RecordingStartedData` en `lib/agent.ts` |
| WS `recording_stopped.data` | (existentes) `filename`, `url`, `platform`, `variants_expected`, `variants` + **nuevos** `serial`, `case_id`, `origin_id` | string, string\|null, string\|null | `Controllers/RecordingController.cs` | `RecordingStoppedData` en `lib/agent.ts` (campos nuevos opcionales: Tatana viejo no los manda) |
| `POST /devices/{serial}/screenshot` query | `case_id`, `origin_id` | string | `ScreenshotController.Take` (`[FromQuery(Name = "case_id")]`, `[FromQuery(Name = "origin_id")]`) | `agent.takeScreenshot(serial, platform, attribution)` |
| `POST /devices/{serial}/preview` | query `platform`, `method`; respuesta `filename`, `url` | string | `Controllers/PreviewController.cs` (nuevo) | `agent.takePreview(serial, platform)`; URL absoluta = `AGENT_URL + url` |
| `GET /previews/{filename}` | — | `image/png` | `PreviewController.Get` | `<img src>` de la ficha (`CaseFormStep.tsx`) |
| `POST /devices/{serial}/record/start` body | `case_id`, `origin_id` (+ existentes `android_version`, `platform`, `ios_mode`, `android_with_mic`) | string\|omitido | `StartRecordingRequest` (`RecordingController.cs`): `string? CaseId = null, string? OriginId = null` al final | `agent.startRecording(..., attribution)` |
| `POST /devices/{serial}/record/stop` query | `case_id`, `origin_id` | string | `RecordingController.Stop` | `agent.stopRecording(serial, platform, attribution)` |
| `POST /devices/{serial}/screenshot/airplay/stop` query | `case_id`, `origin_id` | string | `AirplayShotController.Stop` | `agent.stopAirplayShot(serial, attribution)` |
| `POST /webcam/capture` body | `type`, `case_id`, `origin_id` | string | `CaptureRequest(string Type, string? CaseId = null, string? OriginId = null)` | `agent.captureWebcam(type, attribution)` (hoy sin uso; se actualiza la firma) |
| `GET /health` | `capabilities[]` += `"event_attribution_v1"`, `"device_preview_v1"` | string[] | `HealthController.Capabilities` | informativo. El cliente **no** depende de ellas para funcionar (DT5/DT17). |

Las C# de body se deserializan con `SnakeCaseLower`: `CaseId` ↔ `case_id`, `OriginId` ↔ `origin_id`. En los query
params hay que poner **`[FromQuery(Name = "case_id")]`** explícito, porque la política de nombres no aplica al binding
de query.

### 6.2 Backend ↔ client

| Endpoint | Campo JSON exacto | Tipo | Lado servidor | Lado client |
|---|---|---|---|---|
| `POST /api/agent-events` | `action` | `"startup"\|"capture_start"\|"capture_stop"\|"screenshot"\|"webcam"\|"capture_failed"` | `Models/AgentEvent.cs` (`AgentAction.CaptureFailed` al final) + `DTOs/AgentEventDtos.cs` | `AgentAuditAction` en `client/src/lib/api.ts` |
|  | `failed_action` | mismo enum sin `startup`/`capture_failed`, opcional | `ReportAgentEventRequest.FailedAction` (`AgentAction?`, al final, default null) | `api.reportAgentEvent(action, caseId, extra?)` |
|  | `reason` | string opcional (≤300 tras trim) | `ReportAgentEventRequest.Reason` (`string?`) | ídem |
| `GET /api/cases/{id}/events` | `type` | += `"evidence_updated"`, `"capture_failed"` | `Models/CaseEvent.cs` (`CaseEventTypes.EvidenceUpdated`, `CaptureFailed`) | `CaseEventType` en `lib/api.ts`; textos en `lib/case-events.ts` |
|  | `detail.previous_sha256` | string (64 hex) | `CaseEventDetail.PreviousSha256` | `CaseEventDetail` en `lib/api.ts` |
|  | `detail.failed_action` | `"capture_video_start"\|"capture_video_stop"\|"capture_screenshot"\|"capture_photo"` | `CaseEventDetail.FailedAction` | ídem |
|  | `detail.reason`, `detail.sha256`, `detail.size` | (existentes) | — | — |
| `GET …/report-texts/defaults` | `operaciones_base` | `{ capturas: number, grabaciones: number, archivos_extraidos: number, otros: number }` | `ReportTextsDto.OperacionesBase` (`ReportCountsDto?`, **último** parámetro, default null) en `DTOs/CaseDtos.cs` | `ReportTextDefaults.operaciones_base` y tipo `ReportCounts` en `lib/api.ts` |
| `PUT …/report-texts` | `operaciones_base` (request, opcional) | ídem | `ReportTextsDto.OperacionesBase` | `ReportTextsRequest.operaciones_base?` |
| `PUT …/report-texts` (respuesta) y `GET /api/cases/{id}` (`report_texts`) | `operaciones_base` | ídem \| null \| ausente | `Models/Case.cs` → `ReportTexts.OperacionesBase` (`ReportCounts?`) | `ReportTexts.operaciones_base?` |
| `PUT /api/cases/{id}/evidence` (error) | `code: "evidence_busy"` | string | `EvidenceErrorCodes.EvidenceBusy` (`Services/Cases/EvidenceManifest.cs`) | `useFileManager.saveOne` → mensaje de §8.3.10 |

`ReportCountsDto(int Capturas, int Grabaciones, int ArchivosExtraidos, int Otros)` serializa como `capturas`,
`grabaciones`, `archivos_extraidos`, `otros`.

---

## 7. Decisiones técnicas

> Ninguna necesita validación del usuario. Las marcadas **(decisión del arquitecto)** van más allá de lo literal de
> la HU, eligiendo la opción conservadora.

- **DT1. Regla de atribución (D1 A + `case_id`) (decisión del arquitecto).** D1 A habla de `serial` + `origin_id`.
  Esta SDD agrega `case_id` porque `origin_id` solo no alcanza para el escenario "grabación detenida por fuera **tras
  una recarga**": la recarga cambia el `origin_id` (DT2). `case_id` es el mismo campo que valida #59 (D5 A de
  `grabacion-android-robusta`: "el cliente le pasa a Tatana el `case_id` al iniciar la grabación"). Función pura
  `shouldAcceptAgentEvent(type, data, ctx)` en `client/src/lib/agent-event-filter.ts`, con
  `ctx = { originId, caseId, serial, step }`. Se evalúa en este orden:
  0. Sin caso abierto (`ctx.caseId` null) o sin equipo (`ctx.serial` null) → **no**.
  1. `data.serial` es string y distinto de `ctx.serial` → **no**.
  2. `data.origin_id === ctx.originId` → **sí** (lo pidió esta pestaña; el `addFile` deduplica por nombre).
  3. `data.case_id` es string → **sí** solo si `=== ctx.caseId`. Cubre las otras pestañas del **mismo** caso y la
     grabación detenida por fuera o tras recargar.
  4. `data.origin_id` es string (sin `case_id`) → **no**.
  5. Sin `case_id` ni `origin_id` (Tatana viejo, o pedido sin atribución):
     - `photo_taken` → **no** (la web no usa la webcam de Tatana; sin dueño no se adopta).
     - `screenshot_taken` sin `serial` → **no** (Tatana viejo: es justo el caso de A1; la pestaña que capturó ya
       tiene el archivo por HTTP).
     - Resto (`screenshot_taken` con `serial`, `recording_started`/`recording_stopped` con o sin `serial`) → **sí**
       solo si `ctx.step === 3` (D1 A: "la pestaña que está en el paso 3 con ese serial"; con serial, la regla 1 ya
       filtró).
  En `page.tsx`, un evento rechazado **no** cambia nada en la pestaña: ni `setRecording`, ni `setAirplayName`, ni
  `addFile`.
- **DT2. `origin_id` por carga de página (no `sessionStorage`).** `getTabOriginId()` en `client/src/lib/tab-origin.ts`
  genera un id una sola vez por carga (lazy, solo en el navegador). Usa `crypto.randomUUID()` y, si no existe
  (contexto no seguro, como `http://` a una IP de la LAN), 16 bytes de `crypto.getRandomValues` en hex. Se descarta
  `sessionStorage` porque "Duplicar pestaña" lo copia y dejaría dos pestañas con el mismo id.
- **DT3. Transporte sin headers nuevos.** CORS de Tatana solo permite `Content-Type` (`Program.cs`,
  `Access-Control-Allow-Headers`). Por eso la atribución viaja por **query** en los POST sin body y por **body** en
  `record/start` y `webcam/capture`. No se toca la política de CORS.
- **DT4. Atribución de grabaciones en Tatana.** Singleton nuevo `RecordingAttributionRegistry`
  (`Services/RecordingAttributionRegistry.cs`), un `ConcurrentDictionary<string serial, RecordingAttribution>` con
  `record RecordingAttribution(string Serial, string? CaseId, string? OriginId, DateTimeOffset Since)`.
  `RecordingController.Start` hace `Set` **después** de que el servicio confirmó. `Stop` hace `Take` (lee y borra)
  en un `finally`, haya salido bien o mal (el servicio ya limpió su estado, ver `useRecording`). El evento usa los
  valores del registro y, si no hay registro, los del query. Tatana reiniciado = registro vacío = grabación perdida,
  igual que hoy. **#59 tiene que reutilizar este registro** para exponer `case_id` en su estado (§12).
- **DT5. Vista previa (D8 A).** Controller nuevo `PreviewController` (`[Route("devices/{serial}/preview")]` para el
  POST y `[Route("previews")]` para el GET, o dos controllers; lo elige el implementador). Usa los mismos
  `adb.TakeScreenshotAsync` / `ios.TakeScreenshotAsync(serial, path, method ?? "auto")`. Hay un `SemaphoreSlim` por
  `<k>` (diccionario en el store) que cubre captura + poda: dos pestañas con el mismo celular no se borran la imagen
  a mitad de escritura. Con un Tatana sin el endpoint (404 en `POST …/preview`), el cliente cae a
  `agent.takeScreenshot(serial, platform)` **sin** atribución. Ese evento no tiene `serial`, así que la regla 5 lo
  rechaza en las otras pestañas y A1 queda cerrado igual. B6 sigue abierto solo con un Tatana viejo. El fallback se
  recuerda en una variable de módulo para no reintentar el 404 en cada captura.
- **DT6. `screenshot_taken` por WS se conserva, filtrado.** Los pedidos propios ya traen el nombre por HTTP. Las
  marcas de AirPlay pasan a tomarse de la respuesta de `stopAirplayShot` (hoy se ignoran y se depende del WS), así
  que el WS solo agrega lo que DT1 acepta (otra pestaña del mismo caso, pedidos sin atribución).
- **DT7. M8: dos arreglos en el cliente, ninguno en Tatana.** (a) `SafeImg` resetea el error cuando cambia `src`.
  Guarda `erroredSrc` y muestra el ícono solo si `erroredSrc === src`. (b) `saveOne` marca
  `storedInCase: true` **apenas** `importEvidence`/`uploadEvidence` devuelven OK, antes de `registerEvidence`. Así la
  URL pasa a la carpeta del caso en el mismo momento en que el archivo se movió. `uploaded` sigue cambiando recién
  después del registro. Es consistente con `restoreCaseEvidence`, que ya usa `storedInCase: true, uploaded: false`, y
  corrige de paso que `removeFile` borrara en la raíz un archivo ya movido cuando falló el registro.
- **DT8. Guards de doble clic.** Un `useRef<boolean>` puesto **antes del primer `await`**, y el estado de carga
  visible en el mismo tick. Aplica a: `handleGenerate`/`handleGenerateAgent` (page), `handleScreenshot` (page),
  `useRecording` (un solo `opInFlightRef` para start, stop y modo iOS), `useAirplayShotSession` (un ref por acción:
  `start`, `mark`, `stop`) y el botón de captura del `WebcamCaptureModal` (si no lo tiene).
- **DT9. Generar desde dos pestañas (D3 A) (decisión del arquitecto: lock en el navegador).** `withGenerationLock(caseId, fn)`
  en `client/src/lib/tab-channel.ts`:
  - Si existe `navigator.locks`: `navigator.locks.request("factum:generate:" + caseId, { ifAvailable: true }, cb)`. Si
    `lock === null`, el resultado es `"busy"`. Es atómico entre pestañas del mismo origen y se libera solo si la
    pestaña se cierra.
  - Si no existe (contexto no seguro): fallback con `BroadcastChannel`. Si en los últimos 120 s llegó un
    `generating { case_id, active: true }` de otro `origin_id` sin el `active: false` correspondiente, el resultado
    es `"busy"`. Si no, emite `active: true`, corre y emite `active: false` en el `finally`. No es atómico; queda el
    409 de Tatana como respaldo.
  - `"busy"` → aviso `warn`: **"Este caso ya se está generando en otra pestaña o ventana. Esperá a que termine."**,
    sin llamar al backend. El mismo texto reemplaza al de `zip_in_progress` cuando `action === "generate"`
    (`lib/agent-messages.ts`) y se muestra como advertencia, no como error.
  - Backend: **no** se agrega el 409 de `prepare` (D3 B descartada). El snapshot "generate" deja de duplicarse por
    carrera con el append condicional (§8.2.4).
  - Dos navegadores distintos en la misma PC siguen pudiendo pisarse el intento (lo aceptó D3 A: "no hay un tercer
    camino concurrente"). Queda en riesgos.
- **DT10. Registro de evidencia idempotente (D4 A) + concurrencia optimista (decisión del arquitecto).** Función pura
  `EvidenceManifest.PlanRegistration(existing, incoming, now)` → `RegistrationPlan(List<EvidenceItem> Merged,
  List<ValidatedEvidenceItem> Added, List<(ValidatedEvidenceItem Item, string PreviousSha256)> Updated, bool Changed)`:
  - Nombre nuevo → `Added`, con `RegisteredAt = now`.
  - Mismo nombre y mismo `Sha256` (comparación ordinal; ya vienen en minúscula) → se conserva el ítem existente tal
    cual (`RegisteredAt` y `Size` incluidos). Si el existente no tenía `SourcePath` y el nuevo sí, se completa
    (`Changed = true`, sin evento).
  - Mismo nombre y otro `Sha256` → `Updated`, con el ítem reemplazado (`RegisteredAt = now`, `SourcePath` como hoy).
  - `Changed` = hay `Added`, `Updated` o `SourcePath` completados.
  - `Changed == false` → no se escribe nada, no hay eventos, `200` con el manifiesto y el host actuales.
  - Escritura con `EvidenceRevision`: filtro `EditableAgent(id) & revisión == leída` (0 = `Eq 0` **o** `Exists false`)
    + `$inc EvidenceRevision 1`. Si no matchea, se vuelve a leer el caso: no editable → `case_not_editable` como hoy;
    editable → se replanifica. Como mucho 3 intentos; después, `409 evidence_busy`: "Otra pestaña está registrando
    evidencia de este caso. Reintentá en unos segundos.". `RemoveEvidenceAsync` también hace `$inc` (un registro
    que leyó antes de una baja no resucita el ítem).
  - Eventos (best-effort, como hoy, **después** de la escritura que matcheó): `evidence_added` por `Added` (igual que
    hoy) y `evidence_updated` por `Updated` (`Filename`, `Detail { Sha256 = nuevo, Size = nuevo, PreviousSha256 }`,
    host del manifiesto).
- **DT11. Intento fallido (D5 A).** `AgentAction.CaptureFailed` + `FailedAction` + `Reason`. `AgentAuditController`
  deriva un `case_event` `capture_failed` con `Detail { FailedAction = MapCaptureType(FailedAction), Reason }`
  (truncado a 300, mismo helper que `report_failed`), con los mismos datos de host/IP que los otros eventos de
  captura. `MapCaptureType` pasa a ser `internal static` y testeable. El motivo que manda el cliente es el **mismo
  texto que vio el perito** (mensaje de Tatana ya en castellano o `agentErrorMessage`), nunca un stack.
- **DT12. Auditoría después de la confirmación.** `capture_start` se reporta cuando `startRecording` (o
  `handleSelectIosMode`) devolvió OK. `capture_stop` se reporta cuando `stopRecording` devolvió OK. `screenshot` se
  reporta como hoy, después del OK. Los fallos se reportan como `capture_failed` con `failed_action`
  `capture_start`/`capture_stop`/`screenshot`. La hora del evento es la del servidor al recibir el reporte, o sea, la
  de la confirmación. Las sesiones de marcas AirPlay y la webcam del navegador no cambian (fuera de D5).
- **DT13. Conteos de "Operaciones realizadas" (D7 A).**
  - Los conteos base viven en `report_texts.operaciones_base`, persistidos. Los defaults los devuelven ya calculados
    (`ReportValues.Counts`, la misma clasificación que el DOCX), así que el cliente **nunca** reimplementa la
    clasificación de evidencia.
  - `ReportStep` manda `operaciones_base` en el PUT solo cuando el texto de Operaciones sale de los defaults: carga
    inicial sin textos, "Restaurar texto por defecto" de esa sección y "Actualizar cantidades". El autoguardado normal
    lo omite (el backend conserva el valor).
  - "Desactualizado" = `report_texts.operaciones_base` existe **y** difiere en alguno de los 4 conteos de
    `defaults.operaciones_base`. Sin base (casos anteriores a esta HU) → sin aviso.
  - **(Decisión del arquitecto)** El Gherkin pide que el DOCX no contradiga la tabla "salvo que el perito lo haya
    escrito a mano **y confirmado**". Por eso, además del aviso no bloqueante del paso 5, tocar "Generar" con conteos
    desactualizados abre un `ConfirmDialog` neutro ("Generar igual" / "Volver"). No bloquea, pide la confirmación
    explícita.
- **DT14. `ConfirmDialog` asíncrono (D6 A).** Se extiende sin romper la API: `onConfirm: () => void | Promise<boolean | void>`.
  Si devuelve una promesa, el botón de confirmar muestra spinner (`loading`), los dos botones quedan deshabilitados y
  el diálogo se cierra solo si la promesa resuelve distinto de `false`. Props nuevas opcionales: `error?: string | null`
  (se muestra en `FxBanner tone="error"` dentro del cuerpo), `children?: ReactNode` (cuerpo extra debajo de la
  descripción) y `dismissableMask?: boolean` (default `false`, como hoy; el borrado de evidencia lo activa porque la
  HU pide que "clic fuera" cancele). El foco inicial sigue en "Cancelar" y Prime lo devuelve al disparador al cerrar.
- **DT15. Recarga del historial (D11 A).** `useAuth.loadHistory(opts?: { silent?: boolean })`: `silent` no toca
  `historyLoading` (sin parpadeo de la lista). Se recarga en silencio en tres momentos: al entrar a `startWizard`;
  después de crear o editar un caso (más `cases_changed` por el canal); y con `visibilitychange → visible`, como mucho
  una vez cada 15 s. Al recibir `cases_changed` de otra pestaña también se recarga en silencio. El aviso sigue
  siendo no bloqueante (D8 A de #56).
- **DT16. Presencia (D2 A).** Hook `useTabPresence({ enabled, serial, caseId, caseLabel, step })`, activo en los
  pasos 2–3 con equipo elegido. Emite `presence` al cambiar algún dato, cada 10 s y como respuesta a la primera
  `presence` de un `origin_id` desconocido (una sola vez, sin ping-pong). Emite `bye` en `pagehide` y al desactivarse.
  Una entrada vence a los 30 s sin noticias. Devuelve las otras pestañas con el mismo `serial`. Sin `BroadcastChannel`,
  devuelve `[]`.
- **DT17. Compatibilidad.** Todo es aditivo. Un cliente viejo con un Tatana nuevo ignora los campos nuevos y no usa
  `/preview`. Un cliente nuevo con un Tatana viejo usa el fallback de DT5, y la regla 5 de DT1 rechaza
  `screenshot_taken` sin serial. Un backend viejo con un cliente nuevo responde `400` a `capture_failed` (enum
  desconocido); el reporte es best-effort y no rompe nada. El orden de deploy recomendado es: backend → Tatana →
  client.
- **DT18. Datos existentes.** No hay migración. Los eventos falsos o duplicados del QA quedan como están (Gherkin "Los
  eventos ya guardados no se tocan").

---

## 8. Diseño por lado

### 8.1 Tatana (`server/src/Factum.Agent`)

1. **`Common/EventAttribution.cs` (nuevo, puro):**
   ```csharp
   public sealed record EventAttribution(string? CaseId, string? OriginId)
   {
       public static readonly EventAttribution None = new(null, null);
       public static EventAttribution From(string? caseId, string? originId) => new(Normalize(caseId), Normalize(originId));
       /// trim; ^[A-Za-z0-9_-]{1,64}$ o null.
       public static string? Normalize(string? raw);
   }
   ```
2. **`Services/RecordingAttributionRegistry.cs` (nuevo, singleton):** `Set(string serial, EventAttribution a)`,
   `RecordingAttribution? Take(string serial)` y `RecordingAttribution? Peek(string serial)` (para #59).
3. **`ScreenshotController.Take`:** query `case_id`/`origin_id` → `EventAttribution.From`. El evento queda así:
   `Data = new { filename, url, serial, case_id = attr.CaseId, origin_id = attr.OriginId }` (con nombres de propiedad
   anónima `case_id`/`origin_id` o `CaseId`/`OriginId`: el hub los pasa a snake igual; usar lo mismo en todos).
4. **`RecordingController`:** `StartRecordingRequest` suma `string? CaseId = null, string? OriginId = null` al final.
   Al confirmar (después de `StartRecordingAsync`), `registry.Set(serial, attr)` y `recording_started` con
   `{ serial, platform, case_id, origin_id }`. En `Stop`: `var owner = registry.Take(serial)` en un `finally` (también
   si el stop tira). `recording_stopped` suma `serial`, `case_id = owner?.CaseId ?? queryAttr.CaseId` y
   `origin_id = owner?.OriginId ?? queryAttr.OriginId`. La respuesta HTTP no cambia.
5. **`WebcamController`:** `CaptureRequest(string Type, string? CaseId = null, string? OriginId = null)`;
   `photo_taken` suma `case_id`, `origin_id`.
6. **`AirplayShotController.Stop` + `IIosService.StopAirplayShotSessionAsync(string serial, EventAttribution attribution, CancellationToken ct = default)`:**
   el `screenshot_taken` de `IosService.cs:686-690` suma `serial`, `case_id`, `origin_id`. Se actualizan la interfaz y
   todos sus implementadores o fakes (buscar con `grep -rn StopAirplayShotSessionAsync server/`).
7. **`FileStorageService` / `IFileStorageService`:** suma `string PreviewDirectory { get; }` (=
   `Path.Combine(_dir, "previews")`, creado en el ctor), `string NewPreviewPath(string serial)`,
   `void PrunePreviews(string serial, string keepPath)` (borra `preview_<k>_*.png` salvo `keepPath`; los errores de IO
   se loguean y se ignoran), `int ClearPreviews()` (al arrancar; devuelve cuántos borró) y
   `static string PreviewKey(string serial)` (12 hex del SHA-256). Además `static bool IsPreviewName(string name)` con
   la regex de §5.1. `ListFiles` y `Delete` no cambian (siguen en la raíz).
8. **`PreviewController` (nuevo):** POST: lock por `PreviewKey(serial)` → `NewPreviewPath` → captura (misma bifurcación
   Android/iOS que `ScreenshotController`, mismos `catch` y forma de error) → `PrunePreviews` →
   `Ok(new { filename, url = $"/previews/{filename}" })`. **Sin `hub.BroadcastAsync`.** GET:
   `IsPreviewName(filename)` y existe → `PhysicalFile(path, "image/png")` + `Response.Headers.CacheControl = "no-store"`;
   si no → `404 { error = "Archivo no encontrado" }`.
9. **`Program.cs`:** `AddSingleton<RecordingAttributionRegistry>()`. Al arrancar, junto a `CleanupOrphanUploads`,
   `storage.ClearPreviews()`, que se suma al log de inicio. No se tocan el middleware de CORS ni el de operaciones (el
   POST de preview cuenta como operación mutante, que está bien).
10. **`HealthController.Capabilities`:** suma `"event_attribution_v1"` y `"device_preview_v1"`, con su comentario.

### 8.2 Backend API (`server/src/Factum.Backend`)

1. **Modelos:** según §4.1 (`Case.EvidenceRevision` `[JsonIgnore]`, `ReportTexts.OperacionesBase`, clase
   `ReportCounts`, `CaseEventDetail.PreviousSha256`/`FailedAction`, `CaseEventTypes.EvidenceUpdated = "evidence_updated"`
   y `CaptureFailed = "capture_failed"`, `AgentAction.CaptureFailed` al final, `AgentEvent.FailedAction`/`Reason` +
   `[BsonIgnoreExtraElements]`).
2. **`DTOs/AgentEventDtos.cs`:** `ReportAgentEventRequest(... string? CaseId, AgentAction? FailedAction = null, string? Reason = null)`.
3. **`AgentAuditController.Report`:** si `Action == CaptureFailed` y `FailedAction` es null o no es una de
   `CaptureStart`/`CaptureStop`/`Screenshot`/`Webcam` → `400 { error = "Falta la acción que falló (failed_action)" }`
   (sin insertar). `Reason` se recorta y se trunca a 300. Si #63/#62 (api-hardening) ya está mergeada, pasa además
   por el limpiador de texto de §4.8 de esa SDD. Se guarda en `AgentEvent`. Derivación: `MapCaptureType(CaptureFailed)`
   → `CaseEventTypes.CaptureFailed`, con `Detail = new CaseEventDetail { FailedAction = MapCaptureType(FailedAction), Reason }`.
   `MapCaptureType` queda `internal static` (el csproj ya tiene `InternalsVisibleTo Factum.Backend.Tests`).
4. **Snapshot "generate" sin duplicados por carrera:** `ICaseRepository.AppendReportVersionAsync(string id, ReportTextVersion version, List<ReportTextVersion>? prunedOverride, int expectedCount, CancellationToken ct)`.
   Filtro `Editable(id) & (expectedCount == 0 ? Or(Size(ReportTextVersions, 0), Exists(ReportTextVersions, false)) : Size(ReportTextVersions, expectedCount))`.
   En `AppendGenerateVersionAsync`, si no matchea: se relee el caso **una** vez y se replanifica (`GenerateVersionFor`
   ya devuelve null si ahora es duplicada). Si vuelve a no matchear: log `Warning` y se sigue (best-effort, como
   hoy). Se pasa `cas.ReportTextVersions.Count`. Afecta a los dos llamadores (`CaseService.cs:716` y
   `AgentGeneration.cs:171`).
5. **`EvidenceManifest`:** `PlanRegistration` (DT10) + `EvidenceErrorCodes.EvidenceBusy = "evidence_busy"` +
   `EvidenceBusyMessage`. `Upsert` queda (si nadie más lo usa, se puede reemplazar; los tests existentes de `Upsert`
   se adaptan o se mantienen).
6. **`ICaseRepository.RegisterEvidenceAsync(string id, int expectedRevision, List<EvidenceItem> evidence, EvidenceHost host, List<FileSource> fileSources, CancellationToken ct)`**
   con el filtro de DT10 y `.Inc(c => c.EvidenceRevision, 1)`. `RemoveEvidenceAsync` suma `.Inc(c => c.EvidenceRevision, 1)`
   a su update.
7. **`CaseService.RegisterEvidenceAsync`:** loop de hasta 3 intentos (cargar → validar host → planificar →
   `!Changed` → `Ok` sin escribir → escribir → si no matchea, recargar). El cálculo del `EvidenceHost` no cambia. El
   log informa added/updated/sin cambios. Los eventos se emiten solo tras la escritura que matcheó.
8. **Conteos:** `ReportValues.RenderDefaults` llena `OperacionesBase = new ReportCountsDto(cap, rec, pull, other)`
   con el mismo `Counts(files, cas)`. `SaveReportTextsAsync`: valida `request.OperacionesBase` (0..100000 cada uno) y
   guarda `OperacionesBase = request.OperacionesBase is { } b ? new ReportCounts { … } : cas.ReportTexts?.OperacionesBase`.
   El `ReportTexts` de respuesta lo incluye. Revisar cualquier otro lugar que construya `new ReportTexts { … }` desde
   un DTO (`grep -rn "new ReportTexts" server/src/Factum.Backend`) para no perder el campo.

### 8.3 Client (`client/`) — leer `client/AGENTS.md` y `node_modules/next/dist/docs/` antes de tocar código

1. **`lib/tab-origin.ts` (nuevo):** `getTabOriginId(): string` (DT2).
2. **`lib/agent-event-filter.ts` (nuevo, puro, sin React):** `shouldAcceptAgentEvent` (DT1) + tipos `AttributionCtx`.
   Comentario JSDoc con la tabla de reglas.
3. **`lib/agent.ts`:**
   - `type AgentAttribution = { caseId?: string | null }`. El `origin_id` siempre lo agrega el cliente con
     `getTabOriginId()`.
   - `takeScreenshot(serial, platform, attribution?)`, `stopRecording(serial, platform, attribution?)`,
     `startAirplayShot(serial)` (sin cambios), `stopAirplayShot(serial, attribution?)` → query `case_id`/`origin_id`
     con `URLSearchParams`. `startRecording(..., attribution?)` y `captureWebcam(type, attribution?)` → body
     `case_id`/`origin_id`.
   - `takePreview(serial, platform)`: `POST /devices/{serial}/preview`. 404 → lanza un error marcado
     (`PreviewUnsupportedError` o `AgentError` con `status === 404`). Otros errores igual que `takeScreenshot` (motivo
     de Tatana). `previewFileURL(url)` = `AGENT_URL + url`.
   - Tipos: `ScreenshotTakenData`, `PhotoTakenData`, `RecordingStartedData` (nuevos) y `RecordingStoppedData` +
     `serial?`, `case_id?`, `origin_id?` (`string | null`).
4. **`lib/api.ts` + `types/index.ts`:** `AgentAuditAction` += `"capture_failed"`.
   `reportAgentEvent(action, caseId?, extra?: { failed_action?: Exclude<AgentAuditAction, "startup" | "capture_failed">; reason?: string })`.
   `CaseEventType` += `"evidence_updated" | "capture_failed"`. `CaseEventDetail` += `previous_sha256?`,
   `failed_action?`. `interface ReportCounts { capturas; grabaciones; archivos_extraidos; otros }`.
   `ReportTextDefaults.operaciones_base?: ReportCounts`. `ReportTextsRequest.operaciones_base?: ReportCounts`.
   `ReportTexts.operaciones_base?: ReportCounts | null`. Re-exportar lo nuevo en `types/index.ts` si corresponde.
5. **`lib/tab-channel.ts` (nuevo):** wrapper de `BroadcastChannel("factum-wizard")` con `post(msg)`/`subscribe(fn)`,
   tolerante a que no exista. Valida la forma (§5.3) e ignora los mensajes propios (`origin_id === getTabOriginId()`).
   Incluye `withGenerationLock` (DT9) y `isGenerationBusyFallback`.
6. **`hooks/useTabPresence.ts` (nuevo):** DT16.
7. **`app/dashboard/page.tsx`:**
   - Refs `currentCaseIdRef` (sincronizado con `currentCase?.id`). `handleWsEvent` arma
     `ctx = { originId: getTabOriginId(), caseId: currentCaseIdRef.current, serial: selDeviceRef.current?.serial ?? null, step: stepRef.current }`
     y aplica `shouldAcceptAgentEvent` a `screenshot_taken`, `photo_taken`, `recording_started` y
     `recording_stopped` **antes** de cualquier efecto. Se quita la condición `stepRef.current !== 2` (la cubre DT1).
     `devices_changed`, `zip_progress`, `video_variant_*` y `airplay_*` no cambian.
   - `handleScreenshot`: ref guard. `agent.takeScreenshot(serial, platform, { caseId })`. Si sale OK → `addFile` +
     `reportAgentEvent("screenshot")`. Si falla → `reportAgentEvent("capture_failed", caseId, { failed_action: "screenshot", reason: msg })`
     + `setGlobal(msg)`.
   - Grabación: `onToggleRecord`, `onSelectIosMode` y `onRetryRecording` **dejan de reportar antes**. Llaman al hook,
     que devuelve un `RecordOutcome` (punto 8), y reportan según el resultado (DT12).
   - `handleStopAirplayShot`: los `files` que devuelve el hook → `addFile(f.filename)` por cada uno.
   - `handleGenerate` y `handleGenerateAgent`: `generateInFlightRef` + `setLoad("generate", true)` en la **primera**
     línea, antes de `ensureAgentIdentity`. `handleGenerateAgent` corre dentro de `withGenerationLock(cas.id, …)`;
     `"busy"` → `setGenerateNotice(BUSY_TEXT)`. El `catch` de `AgentError` con `code === "zip_in_progress"` también va
     a `generateNotice` (warn), no a `globalError`. `generateNotice` se limpia al reintentar o al salir del paso 5.
   - `handleSaveCase`: tras crear o editar → `loadHistory({ silent: true })` + `post({ type: "cases_changed" })`.
     `startWizard`: `loadHistory({ silent: true })` (sin esperar). Listener de `visibilitychange` (throttle de 15 s) y
     suscripción a `cases_changed` → `loadHistory({ silent: true })`.
   - Presencia: `useTabPresence({ enabled: mode === "wizard" && (step === 2 || step === 3) && !!selDevice, serial, caseId, caseLabel: currentCase?.nro_referencia || caseForm.nro_referencia || null, step })`.
     Si devuelve ≥1, se muestra arriba del contenido del paso `FxBanner tone="info"`: "Este celular también está
     abierto en otra pestaña (caso <label o «caso nuevo»>). Las capturas quedan en el caso de la pestaña donde las
     tomás." Con varias: el label de la más reciente y "y N más".
   - Borrado: `onRemoveFile` deja de borrar directo. CaptureStep abre el diálogo (punto 10) y llama a un
     `onRemoveFile(name): Promise<boolean>` que devuelve el resultado de `removeFile`.
   - A4: le pasa a `GenerateStep` `onReviewOperaciones={() => goToField(4, reportFieldId("operaciones_realizadas"))}`
     y `notice={generateNotice}`. El diálogo de "Generar igual" vive dentro de `GenerateStep`.
8. **`hooks/useRecording.ts`:** `opInFlightRef`. `handleToggleRecord(device, onError, attribution)` y
   `handleSelectIosMode(mode, device, onError, attribution)` devuelven
   `Promise<RecordOutcome>`, con
   `type RecordOutcome = { kind: "started" } | { kind: "stopped"; filename: string | null } | { kind: "start_failed"; reason: string } | { kind: "stop_failed"; reason: string } | { kind: "picker" } | { kind: "busy" }`.
   El comportamiento visible (estados, mensajes, `setRecording`) no cambia. `attribution = { caseId }` viaja a
   `startRecording`/`stopRecording`.
9. **`hooks/useAirplayShotSession.ts`:** refs de guard por acción. `handleStop(serial, onError, attribution)` devuelve
   `Promise<{ filename: string; url: string }[]>` (`[]` si falló). Se actualiza el comentario del encabezado: los PNG
   ya no "llegan por WS".
10. **`components/CaptureStep.tsx`:** estado `pendingRemove: GItem | null` + `ConfirmDialog` (DT14) con
    `tone="destructive"`, `dismissableMask`, `title="¿Borrar esta evidencia?"`, `confirmLabel="Borrar evidencia"`,
    `cancelLabel="Cancelar"`. `description` = "Se borra el archivo de esta PC y sale del expediente. Queda registrado
    en la cadena de custodia. No se puede deshacer.". `children` = miniatura (`SafeImg` con `srcOf(item)`) o ícono de
    video, nombre en `font-mono` con `translate="no"` y tamaño si se conoce. `onConfirm` = `async () => { const ok = await onRemoveFile(item.name); if (!ok) setRemoveError(...); return ok; }`.
    El error se muestra dentro del modal y el archivo queda. Cubre los cuatro disparadores de `removeItem` (bandeja,
    chips, visor y `VideoCard`). Con `removeBlocked` no se abre.
11. **`components/overlay/ConfirmDialog.tsx`:** DT14. Los usos actuales (`page.tsx` salida del wizard, `ReportStep`)
    no cambian.
12. **`components/capture/StageScreen.tsx` (`SafeImg`):** DT7 (a).
13. **`hooks/useFileManager.ts`:**
    - `saveOne`: `storedInCase: true` justo después del `import`/`upload` OK (DT7 b).
    - `removeFile(...)`: devuelve `Promise<boolean>`. `true` si el archivo salió de la bandeja; `false` si no se tocó
      nada (error del manifiesto o bloqueado). Sigue llamando a `onError`. Además suma `onErrorMessage` para el modal,
      o el modal usa el mismo `onError` (lo decide el implementador; el error se ve **dentro** del modal).
    - `ApiError` con `code === "evidence_busy"` en `runQueue`: texto "Otra pestaña está guardando evidencia de este
      caso. Reintentá en unos segundos." (el archivo queda en `save_error`, como cualquier error).
14. **`components/CaseFormStep.tsx`:** `takeShot` usa `agent.takePreview` y `previewFileURL(url)`, con el fallback de
    DT5. El resto (flancos de conexión, `shotInFlight`, estados) no cambia.
15. **`components/ReportStep.tsx`:**
    - `pendingBaseRef: ReportCounts | null`. `save()` lo agrega como `operaciones_base` si no es null y lo limpia
      cuando el PUT salió bien.
    - Carga inicial sin textos: `pendingBaseRef = defaults.operaciones_base`. "Restaurar texto por defecto" de
      `operaciones_realizadas` (directo o confirmado): igual, con los defaults **frescos** (no el `defaultsRef`
      cacheado, que puede tener conteos viejos).
    - Al cargar, si `cas.report_texts.operaciones_base` existe, se piden los defaults frescos y se compara (función
      pura `sameCounts`/`countsSummary` en `lib/pericial.ts`). Si difieren, aparece un aviso `warn` sobre la sección
      Operaciones: "La evidencia cambió después de armar este texto (ahora: N capturas, M grabaciones…)", con el botón
      **"Actualizar cantidades"**. El botón reemplaza la sección por el default fresco y fija `pendingBaseRef`; queda
      "Cantidades actualizadas. **Deshacer**". Deshacer restaura el texto anterior y manda como base la anterior.
      `countsSummary` omite los conteos en 0 salvo capturas y grabaciones.
16. **`components/dashboard/GenerateStep.tsx`:** al montar, `api.getReportTextDefaults(caseId)` (best-effort; si falla,
    no hay aviso). Si está desactualizado (DT13), suma al checklist un ítem **no bloqueante** `FxBanner tone="warn"`:
    "La evidencia cambió después de armar «Operaciones realizadas» (ahora: …)", con el botón "Revisar texto"
    (`onReviewOperaciones`). Al tocar Generar con el aviso activo, aparece `ConfirmDialog tone="neutral"` con el título
    "Las cantidades del informe no coinciden con la evidencia", la descripción "«Operaciones realizadas» se armó con
    N capturas y M grabaciones; ahora hay X y Y. Si las corregiste a mano, podés generar igual." y los botones
    "Generar igual" / "Volver". Prop nueva `notice?: string | null` (banner `warn` de DT9). El botón se deshabilita
    con `loading` (que ahora llega en el primer clic).
17. **`lib/case-events.ts` + `components/case-detail/CaseTimeline.tsx`:** íconos `evidence_updated` → `RefreshCw`,
    `capture_failed` → `AlertTriangle`. `eventTone` devuelve `"danger" | "warning" | "neutral"`, con `capture_failed` →
    `"warning"`, que el timeline pinta con el token de advertencia del sistema (el mismo que `FxBanner tone="warn"`).
    Títulos:
    - `evidence_updated` → "Se actualizó evidencia (contenido distinto)".
    - `capture_failed` según `failed_action`: `capture_video_start` → "Intento de grabación fallido";
      `capture_video_stop` → "No se pudo cerrar la grabación"; `capture_screenshot` → "Intento de captura de pantalla
      fallido"; `capture_photo` → "Intento de foto fallido"; otro → "Intento de captura fallido".
    `eventFilename` incluye `evidence_updated`. En `RealItemBody`, el motivo se muestra también para `capture_failed`.
    `evidence_updated` muestra en mono `sha256: <12 primeros de previous_sha256>… → <12 primeros de sha256>…`.
18. **`lib/agent-messages.ts`:** `zip_in_progress` con `ctx.action === "generate"` → el texto de DT9.
19. **`hooks/useAuth.ts`:** `loadHistory(opts?)` (DT15). `CaseHistory`/`onRefresh` siguen usando la versión con
    loading.

---

## 9. Checklist atómico

### 9.1 `implementer-backend` — Tatana (`server/src/Factum.Agent`)

- [ ] T1 `Common/EventAttribution.cs` con `Normalize` y `From` (§8.1.1).
- [ ] T2 Tests `EventAttributionTests`: null, vacío, espacios, 64/65 chars, caracteres inválidos (`/`, `..`, espacio,
      `<`), GUID válido.
- [ ] T3 `Services/RecordingAttributionRegistry.cs` + `AddSingleton` en `Program.cs`.
- [ ] T4 Tests `RecordingAttributionRegistryTests`: `Set` → `Take` devuelve y vacía; `Take` sin entrada → null;
      `Set` dos veces mismo serial → gana el último.
- [ ] T5 `ScreenshotController`: query `case_id`/`origin_id` (`[FromQuery(Name=…)]`) y evento con `serial`,
      `case_id`, `origin_id`.
- [ ] T6 `RecordingController.Start`: `StartRecordingRequest` + `CaseId`/`OriginId`; `Set` tras confirmar;
      `recording_started` con `case_id`/`origin_id`.
- [ ] T7 `RecordingController.Stop`: query nuevos; `Take` en `finally`; `recording_stopped` con
      `serial`/`case_id`/`origin_id` (registro primero, query después). La respuesta HTTP no cambia.
- [ ] T8 `WebcamController`: `CaptureRequest` + `photo_taken` con `case_id`/`origin_id`.
- [ ] T9 `IIosService.StopAirplayShotSessionAsync(serial, attribution, ct)` + implementación (evento con los 3 campos)
      + `AirplayShotController.Stop` con query + cualquier fake o implementador.
- [ ] T10 `FileStorageService`: `PreviewDirectory`, `PreviewKey`, `IsPreviewName`, `NewPreviewPath`, `PrunePreviews`,
      `ClearPreviews` (§8.1.7).
- [ ] T11 Tests `PreviewStorageTests` (carpeta temporal propia): `PreviewKey` estable de 12 hex; `IsPreviewName`
      acepta el formato y rechaza `../x.png`, `preview_x.png` y `screenshot_….png`; `PrunePreviews` borra solo las del
      mismo serial salvo la nueva; `ClearPreviews` vacía solo `previews/` y no toca la raíz.
- [ ] T12 `PreviewController` POST (sin broadcast, con lock por clave) y GET (`no-store`, 404 con nombre inválido).
- [ ] T13 `Program.cs`: `ClearPreviews()` al arrancar + log.
- [ ] T14 `HealthController.Capabilities` += `event_attribution_v1`, `device_preview_v1`.
- [ ] T15 Verificación con instancia propia mock (§10): `curl` + cliente WS (`websocat` o un script Python en el
      scratchpad) y anotar en el progress el JSON de cada evento.

### 9.2 `implementer-backend` — API (`server/src/Factum.Backend`)

- [ ] B1 Modelos de §4.1 (incluye `[BsonIgnoreExtraElements]` en `AgentEvent`, `[JsonIgnore]` en `EvidenceRevision`,
      `AgentAction.CaptureFailed` **al final**).
- [ ] B2 `EvidenceManifest.PlanRegistration` + `RegistrationPlan` + `EvidenceErrorCodes.EvidenceBusy`.
- [ ] B3 Tests en `EvidenceManifestTests`: nuevo → `Added`; mismo sha → sin cambio y conserva `RegisteredAt`/`Size`;
      sha distinto → `Updated` con `PreviousSha256`; completa `SourcePath` sin evento (`Changed=true`, sin
      `Added`/`Updated`); lote mixto; orden ordinal del `Merged`.
- [ ] B4 `MongoRepository.RegisterEvidenceAsync` con `expectedRevision` (filtro 0/`Exists false`) + `$inc`;
      `RemoveEvidenceAsync` + `$inc`; firma en `ICaseRepository` y fakes de tests si los hay.
- [ ] B5 `CaseService.RegisterEvidenceAsync`: loop de 3 intentos, no-op sin escritura, `evidence_added` /
      `evidence_updated` tras la escritura, `409 evidence_busy`.
- [ ] B6 `AppendReportVersionAsync` con `expectedCount` + reintento único en `AppendGenerateVersionAsync` (los dos
      llamadores).
- [ ] B7 `ReportAgentEventRequest` + `FailedAction`/`Reason`; `AgentAuditController`: validación de `capture_failed`,
      truncado, `AgentEvent` con los campos, derivación `capture_failed` con `Detail.FailedAction`/`Reason`.
      `MapCaptureType` testeable.
- [ ] B8 Tests `AgentAuditMappingTests`: cada `AgentAction` → tipo de caso; `CaptureFailed` → `capture_failed`;
      `Startup` → null.
- [ ] B9 `ReportCountsDto` + `ReportTextsDto.OperacionesBase` (último parámetro); `RenderDefaults` la llena;
      `SaveReportTextsAsync` valida, guarda y conserva.
- [ ] B10 Tests: `RenderDefaults` devuelve `OperacionesBase` con los conteos de `Counts` (reutilizar el armado de
      casos de `ReportTestSupport`); validación de conteos fuera de rango.
- [ ] B11 `grep -rn "new ReportTexts" server/src/Factum.Backend`: ningún camino pierde `OperacionesBase`.
- [ ] B12 Verificación por API contra un caso de prueba **propio** (§10.2, regla de datos §11).

### 9.3 `implementer-frontend` — `client/` (no toca `agent-ui/`)

Skills obligatorias: `ui-ux-pro-max` (antes del JSX final del diálogo de borrado, los avisos y los ítems del
timeline), `senior-frontend`, `3d-web-experience` (como criterio, sin 3D nuevo) y `web-design-guidelines`
(autochequeo al final). Suma `ui-styling` y `mblode-agent-skills-ui-animation` si toca transiciones del diálogo o de
los banners. Dejar constancia en el progress.

- [ ] F1 `lib/tab-origin.ts` (DT2).
- [ ] F2 `lib/agent-event-filter.ts` (DT1), con JSDoc de la tabla.
- [ ] F3 `lib/agent.ts`: atribución en `takeScreenshot`/`startRecording`/`stopRecording`/`stopAirplayShot`/`captureWebcam`;
      `takePreview` + `previewFileURL`; tipos de eventos.
- [ ] F4 `lib/api.ts` + `types/index.ts`: `capture_failed`, `reportAgentEvent(…, extra)`, tipos de eventos de caso,
      `ReportCounts`, `operaciones_base`.
- [ ] F5 `lib/tab-channel.ts` + `withGenerationLock` (DT9).
- [ ] F6 `hooks/useTabPresence.ts` (DT16).
- [ ] F7 `components/capture/StageScreen.tsx`: `SafeImg` resetea el error al cambiar `src`.
- [ ] F8 `hooks/useFileManager.ts`: `storedInCase` temprano, `removeFile` → `Promise<boolean>`, mensaje de `evidence_busy`.
- [ ] F9 `hooks/useRecording.ts`: guard + `RecordOutcome` + atribución.
- [ ] F10 `hooks/useAirplayShotSession.ts`: guards + `handleStop` devuelve los archivos + atribución.
- [ ] F11 `page.tsx`: filtro en `handleWsEvent`, auditoría después de la confirmación + `capture_failed`, guard y lock
      de Generar + `generateNotice`, marcas AirPlay desde HTTP, recarga del historial (3 disparadores + canal),
      presencia + banner, `onRemoveFile` asíncrono.
- [ ] F12 `components/overlay/ConfirmDialog.tsx`: DT14, sin romper los dos usos actuales.
- [ ] F13 `components/CaptureStep.tsx`: diálogo de borrado (los 4 disparadores) con miniatura/ícono, nombre, tamaño y
      error en el modal.
- [ ] F14 `components/CaseFormStep.tsx`: vista previa por `takePreview` con fallback.
- [ ] F15 `components/ReportStep.tsx`: `pendingBaseRef`, defaults frescos, aviso + "Actualizar cantidades" +
      "Deshacer".
- [ ] F16 `components/dashboard/GenerateStep.tsx`: aviso de conteos, confirmación "Generar igual", `notice`.
- [ ] F17 `lib/case-events.ts` + `CaseTimeline.tsx`: tipos nuevos, tono `warning`, motivo y hashes.
- [ ] F18 `lib/agent-messages.ts`: `zip_in_progress` en generate.
- [ ] F19 `hooks/useAuth.ts`: `loadHistory({ silent })`.
- [ ] F20 Textos en español rioplatense, sin códigos crudos. Teclado solo, foco del modal (inicial en Cancelar,
      vuelve a la papelera al cancelar), claro y oscuro, `prefers-reduced-motion`, 390/1024/1440 px en los banners y el
      diálogo.
- [ ] F21 `npx tsc --noEmit` sin errores (`client/` no tiene script `lint`).

---

## 10. Verificación

### 10.1 Comandos antes de declararse `done`

```bash
# implementer-backend
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj
dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
./ops/harness/verify.sh

# implementer-frontend (desde client/)
npx tsc --noEmit
```

Todo en verde y sin warnings nuevos de nullable en los archivos tocados.

### 10.2 Pruebas del implementador (sin tocar datos ajenos, ver §11)

**Tatana (instancia propia mock, nunca la de `:8765`):**
`dotnet build server/src/Factum.Agent/Factum.Agent.csproj -o <scratch>/agentbin` y arrancar con
`--mock --port 8766 --data <scratch>/tatana-qa-data/data`, más `Agent__EvidenceDirectory=<scratch>/tatana-qa-data/evidencia`.
Con un cliente WS conectado a `ws://localhost:8766/ws`:
1. `POST /devices/EMU001/screenshot?case_id=c1&origin_id=o1` → evento con `serial:"EMU001"`, `case_id:"c1"`,
   `origin_id:"o1"`. Sin query → `case_id:null`, `origin_id:null`. Con `case_id=../x` → `null`.
2. `POST /devices/EMU001/preview` → `200 { url:"/previews/preview_<k>_….png" }`, **ningún** evento WS. Un segundo
   preview deja **un** solo archivo en `previews/`. `GET` de la url → 200 `image/png` con `Cache-Control: no-store`.
   `GET /previews/../appsettings.json` y `GET /files/<nombre del preview>` → 404.
3. `POST /devices/EMU001/record/start` con body `{ "android_version":11, "case_id":"c1", "origin_id":"o1" }` →
   `recording_started` con los dos. `POST …/record/stop` **sin** query → `recording_stopped` con
   `case_id:"c1"`, `origin_id:"o1"`, `serial:"EMU001"`.
4. Reiniciar la instancia → `previews/` vacía y el log de inicio informa cuántas borró. La raíz no se toca.
5. `GET /health` → incluye las dos capabilities.
Al terminar: matar la instancia y borrar `<scratch>/tatana-qa-data`.

**API:** contra el backend de desarrollo, con un caso **creado por el implementador para la prueba** (anotar su
`_id`), flujo agent:
1. `PUT /api/cases/{id}/evidence` con `x.png`/H1 → 1 `evidence_added`. Repetir igual → manifiesto idéntico (mismo
   `registered_at`), **0** eventos nuevos. Con H2 → 1 `evidence_updated` con `detail.previous_sha256 = H1`.
2. Dos `PUT` en paralelo (`curl … & curl … & wait`) con archivos distintos (`a.png`, `b.png`) → el manifiesto tiene
   **los dos** y hay exactamente 2 `evidence_added`. Dos `PUT` en paralelo con el mismo archivo y hash → 1 solo
   `evidence_added`.
3. `POST /api/agent-events` con `action:"capture_failed", failed_action:"capture_start", reason:"Ya hay una grabación de Android en curso.", case_id:<id>`
   → `GET /events` muestra `capture_failed` con `detail.failed_action:"capture_video_start"` y el motivo. Sin
   `failed_action` → 400.
4. `GET …/report-texts/defaults` → `operaciones_base`. `PUT …/report-texts` con `operaciones_base` → lo devuelve;
   otro `PUT` sin el campo → se conserva.
5. Dos `POST …/generate/prepare` en paralelo con los mismos textos → `report_text_versions` suma **una** versión
   "generate" (contar antes y después con `mongosh` en solo lectura, filtrando por el `_id` propio).
Limpieza: solo por `_id`. Borrar el caso propio, sus `case_events` (listar con `find({CaseId:<id>})` y borrar con
`deleteMany({_id:{$in:[…]}})`), los `agent_events` que insertó (por `_id`) y su carpeta de evidencia de prueba, si se
creó. Dejarlo anotado en el progress.

### 10.3 Prueba manual para el usuario (la del QA, dos pestañas, Tatana real + celular por USB)

0. **Antes de probar la rama (confirma A6 en `develop`):** crear un caso con expediente `QA-1/2026` → "Nueva
   inspección" desde el header → escribir `QA-1/2026` y salir del campo. En `develop` **no** aparece el aviso. Con la
   rama, sí.
1. **A1 vista previa:** pestaña 1 en el paso 3 del caso A. En la pestaña 2, "Nueva inspección" con el mismo celular →
   paso 2 (vista previa automática) y "Actualizar captura". La bandeja de la pestaña 1 no cambia, la vista previa se
   ve en la pestaña 2 y `agent-data/` (raíz) no tiene PNG nuevos, solo `agent-data/previews/` con **uno**. En las dos
   pestañas, con el mismo celular, aparece el aviso "Este celular también está abierto en otra pestaña…".
2. **A1 captura en otro caso:** pestaña 1 en el paso 3 del caso A y pestaña 2 en el paso 3 del caso B, mismo
   celular. "Pantalla" en la pestaña 2 → solo la bandeja de B cambia. En el detalle de A no hay evidencia nueva.
3. **Grabación detenida tras recarga:** grabar en la pestaña 1 (caso A), recargar y detener por la API de Tatana
   (`curl -X POST localhost:8765/devices/<serial>/record/stop`) → la grabación entra en el caso A y no en una pestaña
   del caso B.
4. **M8:** dos capturas seguidas en el paso 3 → las dos miniaturas se ven, sin recargar.
5. **A3:** doble clic en "Generar informe pericial" → en DevTools > Network, **un** `generate/prepare` y **un**
   `/zip`, y el botón en "Generando…" desde el primer clic. Mismo caso en el paso 5 en dos pestañas: Generar en la 1
   y enseguida en la 2 → la 2 dice "Este caso ya se está generando en otra pestaña o ventana…" y la 1 termina bien.
6. **Doble clic** en Grabar/Detener y Pantalla → un solo pedido a Tatana por acción (Network).
7. **A5:** con una grabación en curso, intentar grabar desde otra pestaña del mismo caso → en el detalle del caso
   aparece "Intento de grabación fallido" con el motivo, y **no** aparece un "Inicio de grabación" extra. El inicio
   real figura una sola vez. La misma grabación importada en dos pestañas del mismo caso deja **un** "Se agregó
   evidencia".
8. **M2:** papelera → modal propio con miniatura y nombre, foco en "Cancelar". Escape, Cancelar y clic fuera no
   borran (el foco vuelve a la papelera). "Borrar evidencia" la saca y la cadena muestra "Se quitó evidencia".
9. **A4:** pasar por el paso 4, volver al 3, borrar una captura e ir al 5 → aviso de cantidades. "Generar" pide
   "Generar igual". "Revisar texto" → paso 4 → "Actualizar cantidades" → el DOCX coincide con la tabla.
10. **A6 otra pestaña:** pestaña 2 en el paso 2 de una inspección nueva. En la pestaña 1, crear un caso con
    `QA-2/2026`. Volver a la pestaña 2, escribir `QA-2/2026` y salir del campo → aviso.
11. Windows 11 con Chrome: repetir 1 y 5 (Web Locks y `BroadcastChannel` en el navegador real).

---

## 11. Regla de datos de desarrollo (`AGENTS.md`), obligatoria

- La MongoDB de desarrollo (`factum_dev`) y `Storage:DataDirectory` pueden tener datos cargados a mano por el usuario
  (incluidos los dos casos del QA `ba916e4f-…` y `5bfdf5eb-…`). **No se modifican ni se borran documentos ni archivos
  preexistentes**, y no se reescriben eventos viejos.
- Una prueba que escribe limpia **solo por los `_id` que ella misma insertó**. Nada de `deleteMany` por `CaseId` ni
  por otro filtro amplio sin la lista explícita de `_id`.
- Los tests xUnit no tocan Mongo ni el disco real: usan funciones puras o carpetas temporales propias
  (`Path.GetTempPath()` + Guid) que borran en `Dispose`.
- Tatana: no tocar la instancia de `:8765`, su `agent-data/`, `~/Factum/Evidencia` ni
  `server/src/Factum.Agent/appsettings.Local.json`. Usar la instancia propia de §10.2. Las 4 vistas previas sueltas
  del QA en la raíz de `agent-data` **no** se borran (las limpia el usuario; `ClearPreviews` solo mira `previews/`).
- No se mueven tarjetas del Project ni se toca `progress/sesiones/`.

---

## 12. Coordinación con otras HU

- **#59 `grabacion-android-robusta` (SDD todavía sin escribir).** Su D5 A pide `case_id` al iniciar la grabación y
  devolverlo en el estado. **Esta SDD define ese campo:** `StartRecordingRequest.CaseId` (`case_id` en el body de
  `record/start`) y `RecordingAttributionRegistry` como fuente única. #59 lo reutiliza: `Peek(serial)` para exponer
  `case_id` en `/agent/state` (o en su endpoint) y no agrega otro campo ni otro almacenamiento. Si #59 se mergea
  primero con otro almacenamiento de `case_id`, el implementador de esta HU reutiliza ese y solo suma `origin_id`. El
  mismo nombre (`case_id`) viaja en `recording_stopped`. Las dos tocan `RecordingController` y `AdbService`/`page.tsx`
  (el filtro de eventos): el segundo PR rebasa y conserva las dos cosas. Los campos que #59 agregue a
  `recording_stopped` (validación, duración) no chocan.
- **`tatana-ws-y-hosts` (SDD escrita).** Reescribe `AgentWebSocketHub.BroadcastAsync` (cola por cliente). No cambia
  el shape de `AgentEvent`, así que los campos de esta HU viajan igual. También toca `RecordingController.Stop` (409
  `not_recording`). Conflicto de merge chico: conservar el `catch` → 409 de esa HU y el `Take` en `finally` de esta.
  Esta HU no toca el hub ni la política de `Host`/CORS.
- **`api-hardening` (#63, SDD escrita) / #62.** Limpian el texto de entrada de `POST /api/agent-events`. El campo
  nuevo `reason` tiene que pasar por ese helper si ya existe. Además, el caso de otro perito pasa a 404: no afecta a
  esta HU.
- **`ios-grabacion-larga` (mergeada).** `recording_stopped` conserva `variants_expected`/`variants`; esta HU solo suma
  campos.
- **`wizard-pulido-ux` (Bloque 2, HU nueva).** Toca el chip del header, el calendario, la ficha, el scroll, el tema y
  el nombre del ZIP. Puede chocar en `page.tsx` y `CaseFormStep.tsx`: el segundo PR rebasa.
- **`informe-formato-fixes`.** Toca la plantilla de Operaciones ("Android Android", "siendo horas"). Si cambia los
  tokens de conteo, `operaciones_base` sigue saliendo de `Counts`, que no depende del texto.

## 13. Riesgos y notas para los implementadores

- **Orden de deploy:** backend → Tatana → client (DT17). Un client nuevo contra un backend viejo pierde solo los
  `capture_failed` (400 silencioso por best-effort).
- **Dos navegadores en la misma PC** pueden seguir pisando el intento de `prepare` (D3 A lo acepta). El 409 de Tatana
  evita dos ZIP a la vez.
- **`navigator.locks` y `crypto.randomUUID`** requieren contexto seguro. `localhost` y HTTPS lo son; `http://<IP>`
  no. Por eso hay fallbacks (DT2, DT9).
- **`EvidenceRevision` y `$size`:** en documentos sin el campo, el primer registro usa la rama `Exists(false)`. Probar
  el caso "caso viejo con evidencia y sin `EvidenceRevision`".
- **Restaurar una versión** del historial de textos no cambia `operaciones_base`. Si se restaura un texto armado con
  otras cantidades, el aviso puede no aparecer. Es un borde aceptado; "Actualizar cantidades" lo corrige.
- **`airplay_receiver_ready`** sigue sin atribución (no es evidencia). Si una pestaña inicia AirPlay, otra del paso 3
  puede mostrar el nombre del receptor. Queda fuera de alcance.
- **Mock de grabación** escribe un archivo de 0 B; si #59 ya rechaza 0 B en `ImportAsync`, la prueba de §10.2 punto 3
  verifica solo los eventos, no el import.

## 14. Fuera de alcance (recordatorio)

Bloque 2 completo (→ `wizard-pulido-ux`). A2/C2 (→ #59). C1. T-01…T-17. Formato del DOCX. M5. Bloqueo de
expedientes repetidos. Corregir eventos o evidencia de casos existentes. Sincronizar el estado completo del wizard
entre pestañas. Papelera recuperable.
