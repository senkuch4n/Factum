# impl_backend — versionado-informe

Rama: `feat/backend-analitica-forense` (ola backend HU3-6).

## Alcance (solo backend API)

Array embebido `report_text_versions[]` en `Case` (append-only con poda que conserva hitos),
enganchado ATÓMICAMENTE en el mismo update que escribe `report_texts`
(`UpdateReportTextsAsync`), versión `generate` al generar (flujo server y agent) y endpoint de
solo-lectura del historial. `ReportTexts`/`ReportTextsDto` no cambian de forma (solo se agregan
dos campos opcionales al body del PUT).

## Archivos tocados

- `server/src/Factum.Backend/Models/Case.cs`: `List<ReportTextVersion> ReportTextVersions = []`
  con `[JsonIgnore]` (D6) + clases `ReportTextVersion`, `ReportTextSnapshot`
  (`[BsonIgnoreExtraElements]`) y `ReportVersionTriggers`.
- `server/src/Factum.Backend/DTOs/CaseDtos.cs`: `ReportTextsDto` gana `Trigger`/`RestoredFrom`
  opcionales (default null, al final del record); nuevos `ReportTextVersionTextsDto`,
  `ReportTextVersionDto`, `ReportTextVersionsResponse`.
- `server/src/Factum.Backend/Infrastructure/MongoRepository.cs`: `ListByOfficerAsync` excluye
  `ReportTextVersions` de la proyección; `UpdateReportTextsAsync` acepta `versionToPush` /
  `prunedOverride` y combina `$set report_texts` (+ `$push`/`$set` versiones) en un solo
  `UpdateOneAsync(Editable(id))`; nuevo `AppendReportVersionAsync` (append atómico sin tocar
  report_texts, para la versión `generate`).
- `server/src/Factum.Backend/Services/Cases/ReportVersioning.cs` (nuevo): lógica pura —
  `Snapshot`, `IsDuplicate` (D4), `PlanAppend`/`Prune` (D3), `TryNormalizeTrigger`, `ToDto`,
  `GenerateVersionFor`.
- `server/src/Factum.Backend/Services/Cases/CaseService.cs`: `SaveReportTextsAsync` arma el
  candidato, valida trigger, aplica de-dup + poda y hace el append atómico;
  `GetReportTextVersionsAsync` (historial desc por created_at); `AppendGenerateVersionAsync` +
  `GenerateAsync` agrega la versión `generate` antes de `UpdateStatusAsync(Generating)`;
  const `MaxReportVersions = 50`.
- `server/src/Factum.Backend/Services/Cases/AgentGeneration.cs`: `PrepareGenerationAsync` agrega la
  versión `generate` al abrir el intento (flujo agent).
- `server/src/Factum.Backend/Controllers/CasesController.cs`:
  `[HttpGet("{id}/report-text-versions")]` → `Ok(result.Value)`.

## Contrato compartido (verificado contra la SDD)

JSON snake_case_lower. Versión: `id`, `created_at` (ISO UTC), `author_dni`, `author_name`,
`trigger` (`save|restore|generate`), `restored_from` (string|null, ausente si no aplica),
`texts` (ocho secciones + `formato`, mismas claves que report_texts SIN `updated_at`).
Endpoint `GET /api/cases/{id}/report-text-versions` → `{ versions: ReportTextVersion[] }` (más
reciente primero). Body extendido del PUT …/report-texts: `trigger` (`save|restore`, opcional),
`restored_from` (opcional). La respuesta `ReportTexts` del PUT no cambia.

## Decisiones no obvias

- **D1 (atómico):** `$set report_texts` + append al historial viajan en un solo `UpdateOneAsync`
  bajo el filtro `Editable(id)` de siempre → nunca queda a medias; si el caso dejó de ser editable
  entremedio, no escribe y el servicio responde 409.
- **D3 (poda):** si el historial ya llegó al tope (50), en vez de `$push` se hace `$set` del array
  ya podado (`ReportVersioning.Prune`): conserva la PRIMERA versión del caso, TODAS las
  `generate` y, de las restantes, las más recientes hasta completar 50 contando la nueva. Sigue
  siendo un solo UpdateOne. Por debajo del tope se usa `$push` simple.
- **D4 (de-dup):** antes de agregar `save`/`restore`, se compara el candidato (ocho secciones +
  formato, ordinal exacto) con la última versión; si es idéntico, no se agrega versión (igual se
  persiste report_texts). La primera versión siempre entra. La versión `generate` ignora el de-dup
  salvo que sea idéntica a la última.
- **D6 (exclusión del JSON):** `Case.ReportTextVersions` lleva `[JsonIgnore]` → no sale en `GET
  /api/cases` ni en `GET /api/cases/{id}`; se expone solo por el endpoint dedicado, que la lee del
  Case deserializado de Mongo (JsonIgnore no afecta el mapeo BSON). Además se excluye de la
  proyección de `ListByOfficerAsync`. El tipo `Case` del client no cambia.
- **D9 (autor):** `author_dni`/`author_name` = `cas.Officer` (dueño congelado) tanto en el
  guardado como en la generación. El client no manda autor. No se tocaron firmas de `ICaseService`.
- **trigger:** `TryNormalizeTrigger` acepta null/""/"save" → "save" y "restore"; cualquier otro →
  400; `restored_from` solo con "restore".

## Regla dura de datos

Solo agrega `$push`/`$set` del array `report_text_versions` sobre el propio caso editable. Ningún
documento existente se migra al desplegar: un caso previo deserializa `[]` por el default +
`[BsonIgnoreExtraElements]`. No crea colecciones ni índices.

## Tests (item bloqueante del 1er review — resuelto)

El proyecto `server/tests/Factum.Backend.Tests` ya existía. Se agregó
`server/tests/Factum.Backend.Tests/ReportVersioningTests.cs` (22 tests xUnit) sobre la lógica pura
de `ReportVersioning`, sin tocar Mongo (sin riesgo de datos, rápidos):

- **De-dup (D4):** historial vacío → nunca duplicado (la primera siempre entra); idéntico a la más
  reciente → duplicado; distinto → no; la comparación es contra la de `created_at` más reciente (no
  el último elemento del array); un `formato` distinto no es duplicado.
- **Restaurar (D5):** `trigger:"restore"` + `restored_from` quedan registrados y la versión previa
  sigue en el historial (append-only, push simple por debajo del tope).
- **Retención (D3):** por debajo del tope → push simple; en el tope (`MaxReportVersions=50`) → `$set`
  del array podado que conserva la primera, todas las `generate` y la nueva, descarta las
  intermedias viejas, respeta el tope exacto y deja la nueva al final; con muchas `generate` no se
  borra ninguna.
- **Versión "generate" (D2):** se crea con autor del caso; de-dup propio (null si es idéntica a la
  última); no rompe con `ReportTexts` null.
- **Normalización del trigger:** null/""/"save" → "save"; "restore" ok; valor inválido → error;
  `restored_from` con "save" → error.
- **ToDto:** mapea todos los campos del contrato.
- **Append-only (C3):** test de reflexión que confirma que `ICaseEventRepository` solo expone
  `InsertAsync`/`ListByCaseAsync` (sin Update/Delete/Remove).

Para que el proyecto de tests siguiera compilando tras cambiar la firma de `UpdateReportTextsAsync`
y agregar `AppendReportVersionAsync`, se actualizó el fake `InMemoryCaseCounter`
(`server/tests/Factum.Backend.Tests/Admin/AdminTestDoubles.cs`): los dos métodos nuevos de
`ICaseRepository` tiran `NotImplemented` (ese fake solo usa el conteo de solo lectura).

## Verificación

- `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` → 0 errores, 4 advertencias NuGet
  preexistentes, sin warnings CS nuevos.
- `dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj` → 833 correctas,
  7 omitidas (tests de integración con Mongo que requieren DB viva, preexistentes), 0 fallos. Los
  22 `ReportVersioningTests` pasan.
