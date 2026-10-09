# impl_backend — trazabilidad-caso

Rama: `feat/backend-analitica-forense` (ola backend HU3-6).

## Alcance (solo backend API)

Colección nueva `case_events` (append-only, patrón `AgentEventRepository`), eventos derivados de
las operaciones existentes (best-effort y aislados), endpoint `GET /api/cases/{id}/events`
(dueño + superadmin) y derivación de capturas desde `POST /api/agent-events` sin cambiar su
contrato.

## Archivos tocados

- `server/src/Factum.Backend/Models/CaseEvent.cs` (nuevo): `CaseEvent`, `CaseEventDetail`
  (`[BsonIgnoreExtraElements]`, opcionales con `[BsonIgnoreIfNull]` + `[JsonIgnore(WhenWritingNull)]`),
  `CaseEventTypes` (constantes snake_case literales).
- `server/src/Factum.Backend/Infrastructure/CaseEventRepository.cs` (nuevo):
  `ICaseEventRepository` con solo `InsertAsync` y `ListByCaseAsync` (append-only, sin Update/Delete).
  Índice `ix_case_events_case_at` `{ case_id↑, timestamp↑ }` en background (CancellationToken.None).
- `server/src/Factum.Backend/Services/Cases/CaseEventFields.cs` (nuevo): helper
  `ChangedFields(before, after)` → nombres snake_case de los campos de la causa que cambiaron (DT6),
  sin valores. Integrantes → `integrantes_tribunal` (frase legible).
- `server/src/Factum.Backend/Services/Cases/CaseService.cs`: inyección de `ICaseEventRepository` +
  `IHttpContextAccessor`; helpers `RecordEventAsync` (best-effort, CancellationToken.None),
  `ActorIp`, `CaseEventFor`; registro en `CreateAsync` (case_created), `UpdateAsync`
  (case_updated con changed_fields, solo si hubo cambios), `UploadFileAsync` (evidence_added sin
  host), `GenerateAsync` (report_generated / report_failed); `ListEventsAsync` (autz dueño+superadmin).
- `server/src/Factum.Backend/Services/Cases/AgentGeneration.cs`: `RegisterEvidenceAsync` (un
  evidence_added por archivo del lote con hostname/os_user del manifiesto), `DeleteEvidenceAsync`
  (evidence_removed), `FinishGenerationAsync` (report_generated / report_failed).
- `server/src/Factum.Backend/Controllers/CasesController.cs`: `[HttpGet("{id}/events")]` → `Ok(new { events })`.
- `server/src/Factum.Backend/Controllers/AgentAuditController.cs`: inyección de
  `ICaseEventRepository` + logger; tras el InsertAsync del agent_event, deriva un case_event de
  captura (DT9) best-effort cuando hay `case_id` y la acción mapea. `Startup` no genera evento.
- `server/src/Factum.Backend/Program.cs`: `AddSingleton<ICaseEventRepository, CaseEventRepository>()`
  + `AddHttpContextAccessor()`.

## Contrato compartido (verificado contra la SDD)

JSON snake_case_lower. `CaseEvent`: `id`, `case_id`, `type` (CaseEventTypes.*), `actor_dni`,
`actor_name`, `timestamp` (UTC), opcionales `hostname`, `os_user`, `agent_mode`
(installed/portable), `ip`, `filename`, `detail`. `detail` opcional: `changed_fields` (string[]),
`zip_hash`, `report_hash`, `reason`, `sha256`, `size`. Los null se omiten del JSON y de Mongo.
Endpoint `GET /api/cases/{id}/events` → `{ events: CaseEvent[] }`, ascendente por `timestamp`.

Mapa de tipos: case_created, case_updated, capture_screenshot, capture_video_start,
capture_video_stop, capture_photo, evidence_added, evidence_removed, report_generated, report_failed.
AgentAction→tipo: Screenshot→capture_screenshot, CaptureStart→capture_video_start,
CaptureStop→capture_video_stop, Webcam→capture_photo.

## Decisiones no obvias

- **DT3:** `CaseEvent.Type` es `string` con constantes snake_case literales (no enum) → el valor
  guardado en Mongo y el del JSON son idénticos, sin depender de ninguna política de enum.
- **DT4/best-effort:** `RecordEventAsync` traga y loguea (`LogWarning`) y usa
  `CancellationToken.None`. Nunca cambia la respuesta de la operación principal. El `case_event`
  derivado en `AgentAuditController` no cambia el 204 ni el agent_event ya guardado.
- **DT5:** el actor sale del caso cargado (`cas.Officer.Dni`/`Name`, congelado) en los métodos de
  evidencia/generación, y de `Officer` en los controllers; sin tocar firmas de `ICaseService`.
- **DT7-b:** IP resuelta por `IHttpContextAccessor` en `CaseService`
  (`HttpContext.Connection.RemoteIpAddress`); null fuera de request (el evento se registra igual).
- **DT8:** `RegisterEvidenceAsync` emite un evento por archivo del lote (filename/sha256/size +
  host del manifiesto; agent_mode no viaja en el manifiesto → null). `UploadFileAsync` emite uno
  sin host. `DeleteEvidenceAsync` emite uno con filename.
- **DT9:** la derivación va DESPUÉS del InsertAsync del agent_event.
- **DT10:** report_generated con zip_hash/report_hash tras completar; report_failed con reason
  truncado a 300 chars en el catch de generación (ambos flujos). No se registra report_failed por
  rechazos de validación (400/409).
- **DT11:** report_texts_updated queda fuera de v1 (constante preparada, no se emite).
- **DT12:** no se fabrican eventos para casos viejos; `GET …/events` devuelve `[]` si no hay.
- **Autz (D9-A):** dueño por `Officer.Dni` o superadmin por `HttpContext.GetSession().Role`.
  En dev/external el rol es siempre `cliente`, así que ahí vale solo el dueño.

## Regla dura de datos

Esta HU SOLO agrega inserts en la colección nueva `case_events` (crear colección/índice está
permitido). No borra ni modifica `cases`, `expert_profiles`, `catalog_*` ni `agent_events`.

## Verificación

`dotnet build server/src/Factum.Backend/Factum.Backend.csproj` → 0 errores, 4 advertencias NuGet
preexistentes, sin warnings CS nuevos.

Prueba con dispositivo real (Tatana/USB): queda pendiente para el usuario — las capturas derivadas
del `POST /api/agent-events` se pueden probar con el modo Mock del agente, pero la verificación
end-to-end con celular real la hace el usuario.
