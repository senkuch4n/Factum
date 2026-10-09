# SDD: Trazabilidad y cadena de custodia visible del caso

**Slug:** `trazabilidad-caso`
**HU:** `docs/hu-trazabilidad-caso.md` (validada; sección "Dudas" cerrada con las
recomendadas).

## Resumen funcional

Cada caso gana una línea de tiempo append-only de hitos de cadena de custodia
(alta, ediciones de la causa, capturas, altas/bajas de evidencia, generación del
informe OK/fallida). El backend persiste esos hitos en una colección nueva
`case_events` (patrón de `agent_events`), los deriva de las operaciones ya
existentes (incluido el `POST /api/agent-events` que ya reporta Tatana, sin
cambiar el contrato del cliente) y los expone en `GET /api/cases/{id}/events`
para el perito dueño y, en `Auth:Mode=local`, para un superadmin. El cliente web
muestra la traza como una sección nueva dentro de `CaseDetailContent`, en orden
cronológico ascendente, legible por un no técnico; para casos previos a esta HU
deriva los hitos de `created_at`/`generated_at` con un aviso. El registro es
best-effort y aislado: un fallo del insert loguea pero nunca cambia la respuesta
de la operación principal.

## Toca

- **backend (API):** sí.
- **backend (Tatana):** no.
- **client:** sí.
- **agent-ui:** no.

---

## Decisiones técnicas

Las decisiones de diseño de la HU (D1–D10) ya están validadas por el usuario. Acá
van **decisiones técnicas de implementación** (DT), numeradas, que el
implementador debe seguir. Ninguna necesita nueva validación.

- **DT1 — Colección y modelo.** Colección nueva `case_events`, un documento por
  evento (D1-A). Modelo `Models/CaseEvent.cs` con `[BsonIgnoreExtraElements]`.
  Repositorio `Infrastructure/CaseEventRepository.cs` espejo de
  `AgentEventRepository` (cliente Mongo propio por `IOptions<MongoOptions>`, índice
  en background con `CancellationToken.None`). **Append-only duro:** la interfaz
  expone solo `InsertAsync` y `ListByCaseAsync`; **no** hay `Update`/`Delete`
  (D10-A, nivel aplicación, sin hash-chain ni TSA).

- **DT2 — Índice.** `{ case_id↑, timestamp↑ }` (D8-A: lectura ascendente por
  caso). Nombre `ix_case_events_case_at`. Creado en background al construir el
  repo (como `AgentEventRepository.EnsureIndexAsync`), con reintento si falló.
  Crear la colección y el índice está permitido por la regla de datos.

- **DT3 — Tipo del evento: enum serializado a snake_case.** `type` es un string en
  snake_case. En C# conviene un enum `CaseEventType` serializado con el
  `JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)` ya global
  (`Program.cs` L88/93) **y** persistido como string en Mongo. Para que Mongo
  guarde el string snake_case (no el ordinal ni el PascalCase), decorar la
  propiedad con `[BsonRepresentation(BsonType.String)]` **no** alcanza (daría
  `CaseCreated`). Por simplicidad y para evitar divergencia Mongo/JSON, **DT3 fija
  que `CaseEvent.Type` sea `string`** con constantes en una clase estática
  `CaseEventTypes` (patrón de `UserAdminActions`/`EvidenceStorages`), con los
  valores snake_case literales de la tabla de abajo. Así el string que se guarda y
  el que viaja en JSON son idénticos y no dependen de ninguna política de enum.

- **DT4 — Registro best-effort y aislado.** Cada insert de evento se hace
  **después** de que la operación principal persistió con éxito (o, para
  `report_failed`, en el `catch`/rama de error), y envuelto en un helper que
  traga la excepción y la loguea (`_log.LogWarning(ex, …)`), igual que
  `_catalogs.RecordUsageAsync(...)` ya es best-effort en `CreateAsync`/`UpdateAsync`.
  Nunca dentro de la misma operación de Mongo que haría fallar la acción del
  usuario. Usar `CancellationToken.None` para el insert del evento (que el request
  cancelado no pierda el hito ya ocurrido), como hace el flujo agent con los
  commits post-éxito.

- **DT5 — De dónde sale el actor (`actor_dni` + `actor_name`).**
  - `CreateAsync` y `UpdateAsync` ya reciben `User officer` → `officer.Dni` y
    `officer.Name`.
  - Los métodos de evidencia y generación (`RegisterEvidenceAsync`,
    `DeleteEvidenceAsync`, `GenerateAsync`, `FinishGenerationAsync`) hoy reciben
    solo `officerDni` (string). Para tener `actor_name` sin cambiar media docena de
    firmas, **DT5 fija** registrar esos eventos desde el caso ya cargado: el actor
    de un caso siempre es su dueño y `Case.Officer` (tipo `User`) trae `Dni` y
    `Name` congelados. Entonces el nombre sale de `cas.Officer.Name` (el `cas` que
    esos métodos ya cargan con `LoadOwnedAsync`). No hay que tocar firmas.
  - El evento derivado del `POST /api/agent-events` (DT9) se registra en el
    controller `AgentAuditController`, que tiene `Officer` (`User`) en
    `HttpContext.Items["User"]` → `Officer.Dni` / `Officer.Name`.

- **DT6 — `changed_fields` de `case_updated`.** Solo la lista de campos que
  cambiaron, sin valores (D6-A). Se calcula comparando el `cas` leído antes del
  update con el `saved` posterior, igual que `CatalogLogic.ValuesForUpdate(saved, cas)`
  compara (`UpdateAsync` ya tiene ambos en mano, L223 y L256). Agregar un helper
  `CaseEventFields.ChangedFields(Case before, Case after)` en
  `Services/Cases/` que compare los campos de datos de la causa (los mismos que
  arma `CaseDataUpdate`, L240–251) y devuelva sus **nombres snake_case** (los que
  viajan en el JSON del caso: `nro_referencia`, `nombre_denunciante`,
  `dni_denunciante`, `nombre_tribunal`, `organismo_tribunal`, `sala_tribunal`,
  `integrantes_tribunal`/`integrantes`, `tipo_causa`, `caratula`,
  `parte_denunciante`, `parte_denunciada`, `objeto_causa`, `ambito_causa`,
  `fecha_intervencion`, `nombre_proponente`, `profesion_proponente`,
  `matricula_proponente`, `tipo_dispositivo`, `linea_dispositivo`, `imei`,
  `observaciones`). Si `changed_fields` queda vacío (PUT que no cambió nada), **no
  se registra** `case_updated` (evita ruido). Para `integrantes`, comparar la
  lista normalizada; si cambió, incluir `integrantes_tribunal` (que es la frase
  derivada que ve el usuario) y/o `integrantes` — el implementador elige uno
  consistente; recomendado `integrantes_tribunal` por ser el campo legible.

- **DT7 — IP.** Se guarda, como `agent_events` (D5-A). El controller resuelve la
  IP (`HttpContext.Connection.RemoteIpAddress`) y la pasa al servicio. Para los
  eventos que nacen en `CaseService` sin acceso al `HttpContext`, **DT7 fija** que
  `CasesController` resuelva la IP (mismo `ResolveClientIp()` que
  `AgentAuditController`, L54) y la pase al servicio como parámetro opcional del
  registro. Para no tocar las firmas públicas de `ICaseService`, el implementador
  puede: (a) agregar un parámetro `string? actorIp = null` **al final** de los
  métodos que registran eventos (compatible, con default), o (b) inyectar
  `IHttpContextAccessor` en `CaseService` y resolver la IP ahí. **Recomendado (b)**
  — requiere `builder.Services.AddHttpContextAccessor();` en `Program.cs` y no
  cambia ninguna firma; la IP del evento derivado de agent-events la resuelve el
  propio `AgentAuditController` (que ya lo hace). Si el `HttpContext` es null
  (improbable fuera de request), la IP queda `null` y el evento se registra igual.

- **DT8 — Eventos de evidencia: uno por archivo o uno por lote.**
  - `RegisterEvidenceAsync` puede registrar varios archivos de una (manifiesto).
    Registrar **un `evidence_added` por archivo nuevo/actualizado** del lote
    (`validation.Items`), cada uno con su `filename`, `sha256` y `size` en `detail`,
    y `hostname`/`os_user`/`agent_mode` del `request.Host`. Esto da la granularidad
    forense que pide la HU ("cuándo entró cada elemento").
  - `UploadFileAsync` (flujo server) registra **un** `evidence_added` con el
    `filename`, `sha256` (el `staged.Hash`) y `size`, **sin** host (acción 100%
    backend/web → `hostname`/`os_user`/`agent_mode` null). Registrar solo tras el
    `Commit()` exitoso.
  - `DeleteEvidenceAsync` registra **un** `evidence_removed` con `filename`.

- **DT9 — Capturas derivadas de `POST /api/agent-events` (D2-A).** En
  `AgentAuditController.Report`, **después** de `await repo.InsertAsync(evt, ct)`
  exitoso, si `request.CaseId` no es nulo/vacío, insertar además un `CaseEvent`
  equivalente en `case_events` vía el repo nuevo, mapeando `AgentAction` →
  `CaseEventType`:
  - `Screenshot` → `capture_screenshot`
  - `CaptureStart` → `capture_video_start`
  - `CaptureStop` → `capture_video_stop`
  - `Webcam` → `capture_photo`
  - `Startup` → **no genera** evento de caso (es uso del agente, no un hito de la
    prueba; y no siempre trae case_id).
  El evento de caso copia `hostname`/`os_user`/`agent_mode` del request, la IP
  resuelta, `actor_dni`/`actor_name` de `Officer`. El cliente **no cambia** su
  reporte (`reportAgentEvent`). Este insert es best-effort (DT4): un fallo loguea y
  **no** cambia el `204 No Content` del endpoint. El agent_event se sigue guardando
  igual (regresión).

- **DT10 — `report_generated` / `report_failed`.** Dos flujos de generación:
  - Flujo server `GenerateAsync`: tras el `UpdateGeneratedAsync` OK →
    `report_generated` con `detail = { zip_hash, report_hash }`. En el `catch`
    (tras `UpdateStatusAsync(Error)`) → `report_failed` con
    `detail = { reason }` (el `ex.Message` resumido, truncado a ~300 chars; nunca
    datos sensibles ni contraseñas).
  - Flujo agent `FinishGenerationAsync`: tras `CompleteAgentGenerationAsync` OK →
    `report_generated` con `detail = { zip_hash, report_hash }`. En el `catch`
    interno de generación (tras `MarkAgentGenerationFailedAsync`) → `report_failed`
    con `detail = { reason }`.
  - **No** registrar `report_failed` por rechazos de validación (400/409: faltan
    datos, caso no editable, manifiesto desincronizado): son estados normales del
    flujo, no fallos de generación. Solo cuando efectivamente se intentó generar y
    explotó.

- **DT11 — `report_texts_updated` queda fuera de v1** (D3-A). `SaveReportTextsAsync`
  **no** registra evento. `CaseEventTypes` puede dejar la constante preparada pero
  no se emite.

- **DT12 — Casos viejos (D4-A).** El backend **no** fabrica eventos para casos
  previos. `GET …/events` devuelve lo que haya en `case_events` (puede ser `[]`).
  La derivación de hitos (`created_at` → "Caso creado", `generated_at` → "Informe
  generado") y el aviso "La actividad detallada arranca desde esta versión" son
  **100% del cliente**, a partir de los campos que ya trae el `Case`
  (`created_at`, `generated_at`, `officer`). No se escribe nada en la base por
  abrir un caso viejo.

---

## Modelo de datos

### Colección nueva `case_events`

`Models/CaseEvent.cs`:

```
[BsonIgnoreExtraElements]
public sealed class CaseEvent
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString();  // Guid del evento
    public string CaseId { get; set; } = "";       // caso al que pertenece (índice)
    public string Type { get; set; } = "";         // CaseEventTypes.*  (snake_case literal)
    public string ActorDni { get; set; } = "";
    public string ActorName { get; set; } = "";     // copiado al registrar
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;  // UTC

    public string? Hostname { get; set; }           // solo eventos originados en Tatana
    public string? OsUser { get; set; }
    public AgentMode? AgentMode { get; set; }        // installed/portable; null si no vino de Tatana
    public string? Ip { get; set; }
    public string? Filename { get; set; }            // captura / subida / baja
    public CaseEventDetail? Detail { get; set; }     // objeto acotado por tipo (ver abajo)
}
```

`CaseEventDetail` (sub-documento acotado, `[BsonIgnoreExtraElements]`, todos los
campos anulables y `[BsonIgnoreIfNull]` / `[JsonIgnore(WhenWritingNull)]` para que
solo salga lo que aplica a cada tipo):

```
public sealed class CaseEventDetail
{
    public List<string>? ChangedFields { get; set; }  // case_updated
    public string? ZipHash { get; set; }               // report_generated
    public string? ReportHash { get; set; }            // report_generated
    public string? Reason { get; set; }                // report_failed (<=300 chars)
    public string? Sha256 { get; set; }                // evidence_added
    public long?   Size { get; set; }                  // evidence_added
}
```

> **Nunca** datos sensibles del contenido: ni contraseña del ZIP, ni bytes de
> evidencia, ni valores viejos/nuevos de los campos (D6-A).

Constantes de tipo (`Models/CaseEvent.cs`, clase estática `CaseEventTypes`):

| Constante | Valor (snake_case, JSON y Mongo) | Origen |
|---|---|---|
| `CaseCreated` | `case_created` | `CaseService.CreateAsync` |
| `CaseUpdated` | `case_updated` | `CaseService.UpdateAsync` (con `changed_fields`) |
| `CaptureScreenshot` | `capture_screenshot` | `AgentAuditController` (deriva de agent-events) |
| `CaptureVideoStart` | `capture_video_start` | idem |
| `CaptureVideoStop` | `capture_video_stop` | idem |
| `CapturePhoto` | `capture_photo` | idem |
| `EvidenceAdded` | `evidence_added` | `RegisterEvidenceAsync` / `UploadFileAsync` |
| `EvidenceRemoved` | `evidence_removed` | `DeleteEvidenceAsync` |
| `ReportGenerated` | `report_generated` | `GenerateAsync` / `FinishGenerationAsync` |
| `ReportFailed` | `report_failed` | catch de generación (ambos flujos) |

### Índice

- `case_events`: `{ case_id↑, timestamp↑ }`, nombre `ix_case_events_case_at`.
- Append-only: el repo solo hace `InsertOne` y `Find`. Sin `UpdateOne`/`DeleteOne`.

### Compatibilidad con documentos existentes

- Colección nueva: no hay documentos previos que migrar.
- `cases`, `expert_profiles`, `catalog_*`, `agent_events`: **no se tocan**. Esta HU
  solo agrega inserts en `case_events`.
- Casos viejos sin eventos: `GET …/events` devuelve `[]`; el cliente deriva hitos
  (DT12).

---

## Endpoints

### Nuevo: `GET /api/cases/{id}/events`

- Controller: `CasesController` (ruta base `api/cases`, `[Authorize]`). Método
  nuevo `[HttpGet("{id}/events")]`.
- **Autorización:** dueño **o** superadmin (D9-A). El caso es del dueño si
  `Case.Officer.Dni == Officer.Dni`; superadmin si
  `HttpContext.GetSession().Role == UserRoles.Superadmin` (en dev/external siempre
  es `cliente`, así que ahí vale solo el dueño). Reusar `LoadOwnedAsync` para el
  camino del dueño (404 si no existe, 403 si es ajeno) y, **antes** de devolver
  403, chequear el rol: si es superadmin, cargar el caso igual (sin el filtro por
  dueño) y devolver sus eventos. **No** se incluyen los `Audit:AdminDnis` (C
  descartada).
  - Implementación recomendada en el servicio: método nuevo
    `ListEventsAsync(string id, User actor, string actorRole, CancellationToken ct)`
    que:
    1. `cas = await _repo.FindByIdAsync(id)` → null ⇒ `Result.NotFound`.
    2. Si `cas.Officer.Dni != actor.Dni` **y** `actorRole != Superadmin` ⇒
       `Result.Forbidden`.
    3. `events = await _caseEvents.ListByCaseAsync(id, ct)` (asc por timestamp).
    4. `Result.Ok(events)`.
  - El controller pasa `Officer` y `HttpContext.GetSession().Role`.
- **Respuesta 200:** `{ "events": [ CaseEvent... ] }` (envuelto en `{ events }`,
  igual que `AgentAuditController.List` y `CasesController.List`). Orden
  cronológico **ascendente** (D8-A).
- **Errores:** 403 (ajeno sin ser superadmin) / 404 (inexistente), vía
  `this.ErrorResult(result)` como el resto de `CasesController`.
- Serialización: snake_case_lower global (ver Contrato compartido).

### `POST /api/agent-events` — sin cambios de contrato

El contrato de entrada/salida no cambia (sigue `204 No Content`). Internamente
(DT9) inserta además, best-effort, un `case_event` derivado cuando llega
`case_id`. El cliente no cambia.

### DI / arranque (`Program.cs`)

- `builder.Services.AddSingleton<ICaseEventRepository, CaseEventRepository>();`
  (patrón de `AgentEventRepository`, L123).
- `builder.Services.AddHttpContextAccessor();` (DT7, opción b).
- El índice se crea solo en el constructor del repo (background), como
  `AgentEventRepository`. No hace falta un `EnsureIndexesAsync` explícito en el
  pipeline (aunque seguir el patrón de `UserAdminEventRepository` con un
  `EnsureIndexesAsync` llamado tras arrancar también es válido; preferir el patrón
  de `AgentEventRepository` por simplicidad).
- `ICaseService` (Scoped) depende del repo nuevo (Singleton) → inyección directa
  por constructor de `CaseService`. `AgentAuditController` recibe
  `ICaseEventRepository` por constructor.

---

## Contrato compartido (client ↔ server)

Serialización del backend: **snake_case_lower** (`Program.cs`:
`JsonNamingPolicy.SnakeCaseLower` + `JsonStringEnumConverter(SnakeCaseLower)`).
Los `null` de `CaseEventDetail` y de los campos opcionales se omiten del JSON
(`[JsonIgnore(Condition = WhenWritingNull)]`).

### Tipo `CaseEvent` — JSON que viaja ↔ TS

Backend: `server/src/Factum.Backend/Models/CaseEvent.cs`
Frontend: `client/src/types/index.ts` (o nuevo `client/src/types/case-event.ts`,
exportado desde `lib/api.ts` como se hace con `Case`).

| Campo JSON (snake_case) | Tipo C# | Tipo TS | Notas |
|---|---|---|---|
| `id` | `string` | `string` | Guid del evento |
| `case_id` | `string` | `string` | |
| `type` | `string` (`CaseEventTypes.*`) | union literal (ver abajo) | |
| `actor_dni` | `string` | `string` | |
| `actor_name` | `string` | `string` | copia congelada |
| `timestamp` | `DateTime` (UTC) | `string` (ISO) | mostrar en hora local |
| `hostname` | `string?` | `string \| null` | solo eventos de Tatana |
| `os_user` | `string?` | `string \| null` | |
| `agent_mode` | `AgentMode?` (`installed`/`portable`) | `"installed" \| "portable" \| null` | enum snake_case |
| `ip` | `string?` | `string \| null` | |
| `filename` | `string?` | `string \| null` | captura/subida/baja |
| `detail` | `CaseEventDetail?` | `CaseEventDetail \| null` | objeto acotado |

Union TS de `type`:
```ts
export type CaseEventType =
  | "case_created" | "case_updated"
  | "capture_screenshot" | "capture_video_start" | "capture_video_stop" | "capture_photo"
  | "evidence_added" | "evidence_removed"
  | "report_generated" | "report_failed";
```

`CaseEventDetail` TS (todos opcionales):
```ts
export interface CaseEventDetail {
  changed_fields?: string[];   // case_updated
  zip_hash?: string;           // report_generated
  report_hash?: string;        // report_generated
  reason?: string;             // report_failed
  sha256?: string;             // evidence_added
  size?: number;               // evidence_added
}
```

### Respuesta del endpoint

`GET /api/cases/{id}/events` → `{ "events": CaseEvent[] }` (ascendente por
`timestamp`). En `lib/api.ts`, método nuevo:
```ts
async listCaseEvents(caseId: string): Promise<{ events: CaseEvent[] }> {
  return request(`/api/cases/${caseId}/events`);
}
```
(usa `request<T>`, que ya adjunta el token y parsea JSON).

### Mapa de archivos

| Lado | Archivo | Qué |
|---|---|---|
| server | `Models/CaseEvent.cs` | `CaseEvent`, `CaseEventDetail`, `CaseEventTypes` |
| server | `Infrastructure/CaseEventRepository.cs` | `ICaseEventRepository` (Insert + ListByCase) |
| server | `Services/Cases/CaseService.cs` | registro en Create/Update/Upload/Generate + `ListEventsAsync` |
| server | `Services/Cases/AgentGeneration.cs` | registro en Register/Delete evidence + FinishGeneration |
| server | `Services/Cases/CaseEventFields.cs` (nuevo) | helper `ChangedFields(before, after)` |
| server | `Controllers/CasesController.cs` | `GET {id}/events` |
| server | `Controllers/AgentAuditController.cs` | derivar capture_* del POST |
| server | `Program.cs` | DI del repo + `AddHttpContextAccessor` |
| client | `client/src/types/index.ts` (o `case-event.ts`) | `CaseEvent`, `CaseEventType`, `CaseEventDetail` |
| client | `client/src/lib/api.ts` | `listCaseEvents(caseId)` + re-export del tipo |
| client | `client/src/components/case-detail/CaseTimeline.tsx` (nuevo) | la línea de tiempo |
| client | `client/src/components/case-detail/CaseDetailContent.tsx` | montar la sección |
| client | `client/src/lib/case-events.ts` (nuevo, opcional) | textos/íconos por tipo + derivación de hitos viejos |

---

## Diseño UX/UI (client, web)

- **Dónde:** sección nueva dentro de `CaseDetailContent` (D7-A, sin tabs). Ubicarla
  **después** de los datos del caso (`<dl>`) y **antes** del bloque "Paquete del
  informe" (`isDone && <section …package>`), o al final si se prefiere cerrar con
  la actividad; recomendado antes del paquete para que la narrativa (creación →
  … → informe) preceda a los artefactos. Encabezado `<h3>` con el mismo patrón
  que "Paquete del informe" (`text-fx-label uppercase text-fx-text-2`, ícono
  lucide), título "Cadena de custodia" o "Actividad del caso".
- **Componente `CaseTimeline`:** recibe `cas` (o al menos `caseId`,
  `created_at`, `generated_at`, `officer`) y hace `api.listCaseEvents(caseId)` en
  un `useEffect` al montarse. Estados: cargando (skeleton breve), error (mensaje
  discreto "No se pudo cargar la actividad del caso" + reintentar, sin bloquear el
  resto del detalle), vacío/caso viejo (aviso + hitos derivados, DT12).
- **Orden:** ascendente (D8-A). Si hay hitos derivados (caso viejo) **y** eventos
  reales, combinar en orden cronológico; en la práctica un caso viejo no tendrá
  eventos reales de creación/edición, así que: si `events` está vacío → mostrar
  hitos derivados + aviso; si tiene eventos → mostrar eventos (y el aviso solo si
  el caso es anterior, heurística: `created_at` sin un `case_created` en la lista).
- **Cada ítem:**
  - Ícono por tipo (lucide): `case_created`→FolderPlus, `case_updated`→Pencil,
    `capture_screenshot`→Image/Camera, `capture_video_start`/`stop`→Video,
    `capture_photo`→Camera, `evidence_added`→Upload, `evidence_removed`→Trash,
    `report_generated`→FileCheck, `report_failed`→AlertCircle.
  - Texto claro para no técnico (nada de enum crudo). Ej.: "Se creó el caso", "Se
    editaron los datos de la causa (carátula, tribunal)" a partir de
    `changed_fields` traducidos a etiquetas legibles, "Captura de pantalla", "Se
    agregó evidencia: `IMG_0001.jpg`", "Se quitó evidencia: …", "Se generó el
    informe", "Falló la generación del informe". Centralizar el mapa tipo→texto en
    `lib/case-events.ts`.
  - Quién: `actor_name` visible; `actor_dni` en secundario / `title`.
  - Cuándo: `formatDate` + `formatTime` (`lib/format.ts`) en hora local; el UTC
    exacto accesible en `title`/tooltip (p. ej. `new Date(timestamp).toISOString()`).
  - Desde dónde: si el evento trae `hostname`, chip reutilizando el patrón de
    `EvidenceHostChip` (hostname + modo installed/portable).
- **A11y:** lista semántica (`<ol>`/`role="list"`); cada ítem no depende solo de
  color ni solo del ícono (siempre texto); hora con instante exacto en texto. Sin
  botones de editar/borrar (append-only).
- Tokens `--fx-*`, `text-fx-*`, `rounded-fx-*`. Sin 3D.
- **Skills obligatorias del arnés** (el implementer-frontend las invoca y deja
  constancia): `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` (criterio,
  sin 3D), `web-design-guidelines` (autochequeo); `ui-styling` y
  `mblode-agent-skills-ui-animation` si aplica.
- **App:** solo `client/`. `agent-ui/` no cambia. Leer `client/AGENTS.md`
  (Next.js 16) antes de tocar código.

---

## Regla dura de datos (para implementadores)

- Esta HU **solo agrega inserts** en la colección nueva `case_events`.
- Ningún implementador borra ni modifica documentos de `cases`, `expert_profiles`,
  `catalog_*` ni `agent_events`.
- El registro de eventos es best-effort y aislado (DT4): un fallo del insert
  loguea y **no** cambia la respuesta de la operación principal.
- Si se agregan pruebas que escriben en `case_events`, limpian **solo** los `_id`
  que ellas mismas insertaron; nunca un filtro amplio (`deleteMany({ case_id })`).

---

## Checklist atómico

### Backend

- [ ] `Models/CaseEvent.cs`: `CaseEvent`, `CaseEventDetail`
      (`[BsonIgnoreExtraElements]`, campos opcionales `[BsonIgnoreIfNull]` +
      `[JsonIgnore(WhenWritingNull)]`) y `CaseEventTypes` (constantes snake_case).
- [ ] `Infrastructure/CaseEventRepository.cs`: `ICaseEventRepository` con
      `InsertAsync(CaseEvent, ct)` y `ListByCaseAsync(string caseId, ct)`
      (`Find(case_id==id).SortBy(timestamp asc)`). Índice `ix_case_events_case_at`
      `{ case_id↑, timestamp↑ }` en background (patrón `AgentEventRepository`).
      **Sin** Update/Delete.
- [ ] `Program.cs`: registrar `ICaseEventRepository` (Singleton) y
      `AddHttpContextAccessor()`.
- [ ] `Services/Cases/CaseEventFields.cs`: helper
      `ChangedFields(Case before, Case after) → List<string>` (nombres snake_case,
      DT6).
- [ ] `CaseService`: inyectar `ICaseEventRepository` (+ `IHttpContextAccessor` si
      DT7-b) y agregar un helper privado best-effort
      `RecordEventAsync(CaseEvent evt)` que traga/loguea y usa `CancellationToken.None`.
- [ ] `CreateAsync`: tras `_repo.InsertAsync(cas)`, registrar `case_created`
      (`actor` de `officer`, `case_id = cas.Id`, IP de DT7). Best-effort.
- [ ] `UpdateAsync`: tras el update OK, calcular `ChangedFields(cas, saved)`; si no
      está vacío, registrar `case_updated` con `detail.changed_fields`. Best-effort.
- [ ] `UploadFileAsync`: tras `staged.Commit()` OK (y, si aplica, el source), antes
      de `Result.Ok`, registrar `evidence_added` (`filename`, `detail.sha256 =
      staged.Hash`, `detail.size = info.Length`, sin host). Best-effort.
- [ ] `RegisterEvidenceAsync` (`AgentGeneration.cs`): tras
      `_repo.RegisterEvidenceAsync(...)` OK, registrar un `evidence_added` por cada
      item de `validation.Items` (filename, sha256, size) con
      `hostname`/`os_user`/`agent_mode` del `request.Host`. Best-effort.
- [ ] `DeleteEvidenceAsync`: tras `_repo.RemoveEvidenceAsync(...)` OK, registrar
      `evidence_removed` (`filename`). Best-effort.
- [ ] `GenerateAsync`: tras `UpdateGeneratedAsync` OK → `report_generated`
      (`detail.zip_hash`, `detail.report_hash`); en el `catch` tras
      `UpdateStatusAsync(Error)` → `report_failed` (`detail.reason` truncado).
      Best-effort.
- [ ] `FinishGenerationAsync` (`AgentGeneration.cs`): tras
      `CompleteAgentGenerationAsync` OK → `report_generated`; en el `catch` interno
      tras `MarkAgentGenerationFailedAsync` → `report_failed`. Best-effort.
- [ ] `ICaseService` + `CaseService`: `ListEventsAsync(string id, User actor,
      string actorRole, ct)` con autz dueño+superadmin (DT: FindById → 404,
      filtro dueño salvo superadmin → 403, ListByCase).
- [ ] `CasesController`: `[HttpGet("{id}/events")]` → `ListEventsAsync` pasando
      `Officer` y `HttpContext.GetSession().Role`; respuesta `Ok(new { events })`;
      errores vía `this.ErrorResult`.
- [ ] `AgentAuditController.Report`: tras el `InsertAsync` del agent_event, si
      `request.CaseId` no vacío y la acción mapea (DT9), insertar `CaseEvent`
      derivado en el repo nuevo (best-effort, no cambia el 204). `Startup` no
      genera evento de caso.
- [ ] No tocar `agent_events`, `cases`, `expert_profiles`, `catalog_*`.

### Frontend (`client/`)

- [ ] `client/src/types/`: `CaseEvent`, `CaseEventType`, `CaseEventDetail` con los
      nombres exactos del Contrato compartido; re-export desde `lib/api.ts`.
- [ ] `lib/api.ts`: `listCaseEvents(caseId): Promise<{ events: CaseEvent[] }>` con
      `request`.
- [ ] `lib/case-events.ts` (opcional): mapa tipo→(ícono, texto), traducción de
      `changed_fields` a etiquetas legibles, y derivación de hitos para casos
      viejos (DT12).
- [ ] `components/case-detail/CaseTimeline.tsx`: carga `listCaseEvents` al montar;
      estados cargando / error+reintentar / vacío-caso-viejo (aviso + hitos
      derivados) / con eventos; lista ascendente, ítems con ícono+texto+actor+hora
      local (UTC en `title`) + chip de host cuando aplica; a11y (lista semántica,
      no solo color/ícono). Tokens `--fx-*`.
- [ ] `components/case-detail/CaseDetailContent.tsx`: montar `<CaseTimeline cas={cas} />`
      como sección nueva (antes del "Paquete del informe"), con `<h3>` consistente.
- [ ] No tocar `agent-ui/`.

---

## Verificación

### Comandos (los corre cada implementador antes de `done`)

- Backend: `dotnet build server/src/Factum.Backend/Factum.Backend.csproj`
  (baseline actual: compila con 4 warnings NuGet preexistentes, 0 errores).
- Frontend: `npx tsc --noEmit` en `client/`.
- No hay proyectos de tests .NET ni tests en `client/`; esta SDD no agrega tests.
  Si el implementer-backend quiere cubrir `CaseEventFields.ChangedFields`, puede
  hacerlo con un proyecto de tests nuevo, pero no es obligatorio.

### Prueba manual (queda para el usuario)

1. Crear un caso nuevo → abrir su detalle → la línea de tiempo muestra "Se creó el
   caso" con el perito y la hora local.
2. Editar un dato de la causa (p. ej. carátula) y guardar → aparece "Se editaron
   los datos de la causa" listando el campo cambiado; los eventos anteriores no
   cambian.
3. Desde una inspección con Tatana: tomar un screenshot / grabar video / foto de
   webcam → aparecen las capturas en la traza con el equipo (hostname/modo), sin
   duplicar lo que ya reporta el agente.
4. Registrar/subir y luego quitar un archivo de evidencia → `evidence_added` (con
   nombre y hash en el detalle) y `evidence_removed`.
5. Generar el informe OK → `report_generated` con el hash del ZIP y del informe.
   Forzar un fallo de generación (si es reproducible) → `report_failed` con un
   motivo resumido.
6. Abrir un caso **creado antes de esta HU** → la traza muestra solo los hitos
   derivables (creación, y generación si aplica) con el aviso de que la actividad
   detallada arranca desde esta versión; no inventa actores ni equipos.
7. Con `Auth:Mode=local`: un superadmin abre el detalle de un caso de otro perito y
   ve su traza; un perito que pide `GET /api/cases/{id}/events` de un caso ajeno
   recibe 403/404.
8. Regresión: los eventos de `agent_events` se siguen reportando y listando igual;
   ningún documento de `cases`/`expert_profiles`/`catalog_*`/`agent_events` cambió.
