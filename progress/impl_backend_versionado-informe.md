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

## Tests

La SDD sugiere un proyecto de tests xUnit para `CaseEventFields`/versionado. No se agregó proyecto
de tests (no hay infraestructura de tests .NET en el repo y la SDD lo marca como opcional —
"preferible testear con un repo en memoria/fake para no tocar la base de desarrollo"). La lógica
pura quedó aislada en `ReportVersioning` para facilitar un test futuro. Pendiente opcional.

## Verificación

`dotnet build server/src/Factum.Backend/Factum.Backend.csproj` → 0 errores, 4 advertencias NuGet
preexistentes, sin warnings CS nuevos.
