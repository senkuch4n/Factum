# SDD — Versionado de las secciones del informe pericial

**HU:** `docs/hu-versionado-informe.md` · **Slug:** `versionado-informe`

## Resumen funcional

Cada vez que el perito consolida un guardado del paso 4 "Informe" (y siempre al
generar el informe), el backend agrega —de forma **atómica y append-only**, en la
misma operación Mongo que pisa `report_texts`— un **snapshot de las ocho
secciones** a una lista nueva `report_text_versions[]` embebida en `Case`. El
perito puede ver el historial de versiones por sección, ver una versión anterior
en solo lectura, comparar dos versiones con un diff por palabra sobre el texto
visible (con vista lado a lado opcional) y restaurar una versión al editor
(restaurar reusa el PUT de siempre; queda registrada como versión nueva con
`trigger:"restore"` + `restored_from`). El historial se consulta desde el paso 4
del wizard y desde el detalle del caso; en casos `Completed` es solo-lectura. Ningún
caso existente se migra al desplegar.

## Toca

- **backend (API)** (`server/src/Factum.Backend`): **sí** — modelo, DTO de versión,
  endpoint de solo-lectura, append atómico en el guardado y en la generación,
  proyección del listado.
- **backend (Tatana)** (`server/src/Factum.Agent`): **no**.
- **client** (`client/`): **sí** — tipos, cliente de API, panel de historial, diff,
  restaurar; integrado al `ReportStep` y accesible desde el detalle del caso.
- **agent-ui** (`agent-ui/`): **no**.

---

## Modelo de datos

Colección `cases` (sin colección nueva: embebido, decisión **D5-B**). Se **agrega**
un campo a `Case` y una clase embebida nueva. `ReportTexts` **no cambia**.

### `Case.ReportTextVersions` (nuevo)

En `server/src/Factum.Backend/Models/Case.cs`, dentro de `sealed class Case`:

```csharp
/// <summary>
/// Historial append-only de snapshots de report_texts (versionado-informe, D1-B/D5-B).
/// Default [] + [BsonIgnoreExtraElements]: un caso previo a esta HU deserializa lista
/// vacía (no hay migración). El listado la proyecta afuera (como ReportTexts).
/// </summary>
public List<ReportTextVersion> ReportTextVersions { get; set; } = [];
```

### Clase embebida `ReportTextVersion` (nueva, en `Case.cs`)

```csharp
[BsonIgnoreExtraElements]
public sealed class ReportTextVersion
{
    /// <summary>Guid "N" (como PendingGeneration.Id). Lo fija el servidor.</summary>
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Snapshot del autor al consolidar la versión (Officer.Dni / Officer.Name).</summary>
    public string AuthorDni { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    /// <summary>"save" | "restore" | "generate" (ver ReportVersionTriggers).</summary>
    public string Trigger { get; set; } = string.Empty;
    /// <summary>Si es una restauración, el Id de la versión restaurada (D4). Null si no.</summary>
    [BsonIgnoreIfNull]
    public string? RestoredFrom { get; set; }
    /// <summary>Foto de las ocho secciones + formato. Mismo shape que ReportTexts, sin UpdatedAt.</summary>
    public ReportTextSnapshot Texts { get; set; } = new();
}

[BsonIgnoreExtraElements]
public sealed class ReportTextSnapshot
{
    public string ObjetoInforme { get; set; } = string.Empty;
    public string OperacionesRealizadas { get; set; } = string.Empty;
    public string AseguramientoEvidencia { get; set; } = string.Empty;
    public string Resultados { get; set; } = string.Empty;
    public string ValoracionTecnica { get; set; } = string.Empty;
    public string Conclusiones { get; set; } = string.Empty;
    public string NotasTecnicas { get; set; } = string.Empty;
    public string Reserva { get; set; } = string.Empty;
    [BsonIgnoreIfNull]
    public string? Formato { get; set; }
}

public static class ReportVersionTriggers
{
    public const string Save = "save";
    public const string Restore = "restore";
    public const string Generate = "generate";
}
```

> **Por qué `ReportTextSnapshot` aparte y no reusar `ReportTexts`:** `ReportTexts`
> arrastra `UpdatedAt`, que no tiene sentido por versión (la versión ya tiene
> `CreatedAt`). Usar una clase sin `UpdatedAt` evita escribir un campo muerto en
> cada snapshot y deja el contrato del historial independiente del de `report_texts`.

### Retención (D5-B): tope ~50, conservando hitos

Tope **`MaxReportVersions = 50`** por caso. Al pasar del tope se descartan las más
viejas **salvo** la **primera** versión del caso y cualquier versión
`trigger:"generate"` (hitos forenses). Mongo `$slice` recorta por la cola, pero no
sabe "conservar la primera": por eso la poda **no** se delega solo a `$slice`. Ver
D3 en Decisiones técnicas para el algoritmo exacto; la escritura sigue siendo una
sola operación atómica.

### Compatibilidad con documentos existentes

- Un `Case` guardado antes de esta HU **no tiene** `report_text_versions`:
  deserializa `[]` por el default + `[BsonIgnoreExtraElements]`. **Ningún documento
  se toca al desplegar** (no hay migración ni backfill).
- `report_texts.updated_at` y las ocho secciones quedan intactos; el campo nuevo es
  independiente.
- Casos `Completed` y sus artefactos (ZIP/DOCX/PDF/hashes) no se tocan: la generación
  del informe sigue leyendo `report_texts` vigente, el historial no entra al DOCX/ZIP
  ni a la tabla de hashes (ver "Fuera de alcance" de la HU).

### Índices

Ninguno. La lista es embebida y solo se accede por `_id` del caso (ya indexado).

---

## Endpoints

### Nuevo: `GET /api/cases/{id}/report-text-versions` (solo-lectura)

- **Método/ruta:** `GET /api/cases/{id}/report-text-versions`.
- **Auth/aislamiento:** `[Authorize]` + dueño por `Officer.Dni`, idéntico al resto de
  `/api/cases/{id}` (404 si no existe, 403 si es de otro perito — vía
  `LoadOwnedAsync`). Funciona en cualquier `Status`, incluido `Completed` (D6/D7).
- **Entrada:** solo `id` en la ruta.
- **Respuesta 200:** `{ "versions": [ ...ReportTextVersionDto ] }`, **más reciente
  primero** (orden descendente por `created_at`). Un caso sin historial devuelve
  `{ "versions": [] }`.

> No hay endpoint de "una versión" separado: el listado ya trae el `texts` completo de
> cada versión (el snapshot es el mismo que el perito necesita para Ver/Comparar/
> Restaurar, y el tope de 50 acota el peso). La HU lo marca como opcional; se omite.

> **Diff:** se calcula en el client (D3-A). No hay endpoint de diff.

> **Restaurar:** **sin endpoint nuevo.** El client pone el `texts` de la versión en el
> editor y deja que el `PUT /api/cases/{id}/report-texts` de siempre lo guarde; el
> append al historial va enganchado en ese PUT con `trigger:"restore"` +
> `restored_from` (ver Contrato compartido y D5).

### Modificado: `PUT /api/cases/{id}/report-texts`

Mismo contrato de **entrada y salida** (`ReportTextsDto` → `ReportTexts`). Dos campos
**opcionales nuevos en el body** para el versionado (no rompen a clientes viejos):

- `trigger` (string, opcional): `"save"` (default si ausente/otro) o `"restore"`.
- `restored_from` (string, opcional): id de la versión restaurada; solo válido con
  `trigger:"restore"`.

El guardado ahora, además de pisar `report_texts`, agrega una versión al historial en
la **misma operación Mongo** (ver "Append atómico"). La forma de la respuesta
`ReportTexts` **no cambia** (sigue devolviendo las ocho secciones + `formato` +
`updated_at`); el client recarga el historial con el GET tras guardar.

### Generación: `POST /api/cases/{id}/generate` (y flujo agent prepare/finish)

Antes de pasar el caso a `Generating`, se agrega una versión `trigger:"generate"` con
el `report_texts` vigente, sujeta al mismo de-duplicado y retención. Ver D2.

### Proyección del listado

`GET /api/cases` y `GET /api/cases/{id}` (listado) excluyen `report_texts` por peso;
hay que **excluir también `report_text_versions`** en `ListByOfficerAsync`
(`MongoRepository.cs`, el `.Project(...Exclude(c => c.ReportTexts)...)`). El `GET
/api/cases/{id}` individual **sí** puede traer el campo, pero el client **no** lo lee
desde ahí: usa el endpoint dedicado. Para no inflar `GET /api/cases/{id}` con hasta 50
snapshots, **también se excluye `report_text_versions` de la respuesta de ese
endpoint**, igual que ya pasa con campos pesados — se expone solo por
`report-text-versions`. (Se logra con `[JsonIgnore]` sobre la propiedad `Case` +
el endpoint dedicado proyectando la lista; ver D6.)

---

## Contrato compartido (snake_case_lower)

Serialización del backend: `JsonNamingPolicy.SnakeCaseLower` (`Program.cs`).

### Versión del historial

| JSON (snake_case) | Tipo | C# (`ReportTextVersion` en `Models/Case.cs`) | TS (`client/src/lib/api.ts`) |
|---|---|---|---|
| `id` | string | `Id` | `id: string` |
| `created_at` | string ISO-8601 UTC | `CreatedAt` | `created_at: string` |
| `author_dni` | string | `AuthorDni` | `author_dni: string` |
| `author_name` | string | `AuthorName` | `author_name: string` |
| `trigger` | `"save"\|"restore"\|"generate"` | `Trigger` | `trigger: ReportVersionTrigger` |
| `restored_from` | string \| null (ausente si no aplica) | `RestoredFrom` | `restored_from?: string \| null` |
| `texts` | objeto (abajo) | `Texts` (`ReportTextSnapshot`) | `texts: ReportTextVersionTexts` |

`texts` (objeto, ocho secciones + formato — mismas claves que `report_texts` **sin**
`updated_at`):

| JSON | Tipo | C# | TS |
|---|---|---|---|
| `objeto_informe` | string | `ObjetoInforme` | `objeto_informe: string` |
| `operaciones_realizadas` | string | `OperacionesRealizadas` | `operaciones_realizadas: string` |
| `aseguramiento_evidencia` | string | `AseguramientoEvidencia` | `aseguramiento_evidencia: string` |
| `resultados` | string | `Resultados` | `resultados: string` |
| `valoracion_tecnica` | string | `ValoracionTecnica` | `valoracion_tecnica: string` |
| `conclusiones` | string | `Conclusiones` | `conclusiones: string` |
| `notas_tecnicas` | string | `NotasTecnicas` | `notas_tecnicas: string` |
| `reserva` | string | `Reserva` | `reserva: string` |
| `formato` | `"markdown"` \| null (ausente si plano) | `Formato` | `formato?: "markdown" \| null` |

### Respuesta del endpoint de historial

| JSON | Tipo | C# | TS |
|---|---|---|---|
| `versions` | array de versión (desc. por `created_at`) | `List<ReportTextVersionDto>` | `ReportTextVersion[]` |

Archivos exactos del contrato:
- Backend DTO: `server/src/Factum.Backend/DTOs/CaseDtos.cs`
  (`ReportTextVersionDto`, `ReportTextVersionTextsDto`, `ReportTextVersionsResponse`).
- Backend modelo persistido: `server/src/Factum.Backend/Models/Case.cs`.
- Client tipos + API: `client/src/lib/api.ts`, reexportado en
  `client/src/types/index.ts`.

### Body extendido de `PUT …/report-texts`

| JSON | Tipo | C# (`ReportTextsDto`) | TS (`ReportTextsRequest`) |
|---|---|---|---|
| …campos actuales… | — | sin cambios | sin cambios |
| `trigger` | `"save"\|"restore"` opcional | `string? Trigger = null` | `trigger?: "save" \| "restore"` |
| `restored_from` | string opcional | `string? RestoredFrom = null` | `restored_from?: string` |

> El cliente de autoguardado puede **omitir** ambos (equivale a `"save"`). Solo los
> manda en el guardado disparado por "Restaurar esta versión".

---

## Decisiones técnicas

**D1 (append atómico en una sola operación Mongo).**
`MongoRepository.UpdateReportTextsAsync(id, texts, …)` cambia a
`UpdateReportTextsAsync(id, texts, ReportTextVersion? version, int maxVersions, …)`.
Si `version` no es null, el update combina en un solo `UpdateOneAsync` sobre el filtro
`Editable(id)` actual:

```csharp
var u = Builders<Case>.Update.Set(c => c.ReportTexts, texts);
if (version is not null)
    u = u.PushEach(c => c.ReportTextVersions, new[] { version }); // sin $slice: ver D3
var res = await _col.UpdateOneAsync(Editable(id), u, ...);
```

Nunca un segundo `UpdateOne` que pueda quedar a medias. El `$set report_texts` y el
`$push report_text_versions` viajan juntos. Mantiene exactamente el filtro de
editabilidad de hoy (si otro request lo pasó a `Generating`/`Completed` entremedio, no
escribe y el servicio responde 409).

**D2 (cuándo se crea una versión — D2-B: por hito).**
- **Guardado normal (`PUT …/report-texts`):** el `CaseService.SaveReportTextsAsync`
  arma el `ReportTextVersion` candidato y lo pasa al repo **solo si pasa el
  de-duplicado** (D4). Esto ya agrupa la ráfaga de autoguardados de 1.2 s de forma
  natural: un autoguardado que no cambió nada respecto de la última versión **no crea
  versión** (de-dup), y varios guardados idénticos seguidos no ensucian. El resultado
  práctico es "una versión por cambio real consolidado". **No** se agrupa por ventana
  de tiempo en el server (simple y robusto); el ruido lo corta el de-dup.
- **Botón "Marcar versión" (opcional, client):** fuerza un `flush()` del guardado con
  `trigger:"save"`. Si no hubo cambios desde la última versión, el de-dup evita el
  duplicado (nada que marcar). Es un extra de UX, no cambia el contrato.
- **Generación:** `CaseService.GenerateAsync` (flujo server) y
  `PrepareGenerationAsync`/`FinishGenerationAsync` (flujo agent) agregan una versión
  `trigger:"generate"` con el `report_texts` vigente **antes** de pasar a
  `Generating`. Se usa el mismo repo helper; esta versión **ignora el de-dup** salvo
  que sea idéntica byte a byte a la última (para no duplicar si la última ya era igual)
  — pero una versión `generate` **nunca se poda** (D3). Autor = `Officer` del caso.
  Punto exacto backend: en `GenerateAsync`, tras validar (`ValidateForGenerate` +
  `BrokenImageKeys`) y **antes** de `await _repo.UpdateStatusAsync(id,
  Generating)`. En el flujo agent, dentro de `PrepareGenerationAsync` (abre el intento
  sin cambiar el status) usando un repo helper nuevo `AppendReportVersionAsync` que
  hace el push atómico bajo `Editable(id)`.

**D3 (retención D5-B — poda conservando hitos, sin romper atomicidad).**
`$slice` de Mongo recorta por posición fija y no sabe "conservar la primera ni las
`generate`". Para no perder hitos **ni** dejar de ser atómico, la poda la calcula el
**servicio** antes de escribir:
- El servicio, al construir el update, si la lista ya tiene `>= MaxVersions` (50),
  **no** usa `$push` sobre el array completo: en su lugar hace `.Set(c =>
  c.ReportTextVersions, listaPodada)` donde `listaPodada` = (versiones a conservar) +
  (la nueva). `listaPodada` se arma a partir del `Case` ya leído (`LoadOwnedAsync` ya
  trae el caso con el historial): conserva la **primera** versión, **todas** las
  `trigger:"generate"`, y de las restantes las más recientes hasta completar 50
  (contando la nueva). Esto sigue siendo **un solo `UpdateOneAsync`** sobre
  `Editable(id)` → atómico.
- Si la lista está por debajo del tope, se usa el `$push` simple (menos payload, no
  reescribe el array).
- `MaxReportVersions = 50` vive como const en `CaseValidation` (o en el servicio),
  documentada. Nunca se poda la versión actual (es la recién agregada) ni la primera ni
  las `generate`.

> Nota de concurrencia aceptada: entre el `LoadOwnedAsync` y el `UpdateOne` podría
> colarse otro guardado del mismo dueño (improbable: es una sola persona editando un
> caso). El filtro `Editable(id)` garantiza que no se escribe sobre un caso no
> editable; una carrera entre dos guardados concurrentes del mismo caso podría, en el
> peor caso, recalcular la poda sobre una base levemente vieja — nunca borra la actual
> ni un hito. Es coherente con cómo ya opera `UpsertCaptureRolesAsync` (lee, arma,
> `Set`). Para el versionado forense (un perito, un caso) el riesgo es nulo.

**D4 (de-duplicado).**
Antes de agregar una versión `save`/`restore`, el servicio compara el `texts`
candidato con el `texts` de la **última** versión del historial (la de `created_at`
más reciente). Si las ocho secciones + `formato` son idénticas (comparación ordinal
exacta), **no agrega versión** (igual persiste `report_texts` como hoy). Si el caso no
tiene historial todavía, la primera versión siempre se agrega (no hay con qué
comparar). Esto corta el ruido del autoguardado (escenario Gherkin "Guardar sin
cambios no crea versión" y "autoguardado trivial").

**D5 (restaurar — D4-A, sin endpoint).**
El client, al confirmar "Restaurar esta versión", pone el `texts` de la versión
elegida en el editor de las ocho secciones y dispara el guardado con
`trigger:"restore"` + `restored_from: <id de la versión restaurada>`. El backend,
en `SaveReportTextsAsync`, si `trigger=="restore"`, arma el `ReportTextVersion` con
ese trigger y `RestoredFrom`. El de-dup aplica igual (restaurar a algo idéntico a lo
actual no crea versión). La versión que había antes de restaurar queda en el historial
(append-only). `restored_from` da la trazabilidad "se restauró la versión del martes".

**D6 (cómo se excluye `report_text_versions` de los `Case` JSON sin romper el
endpoint).**
La propiedad `Case.ReportTextVersions` lleva `[JsonIgnore]` **no** se puede usar porque
el endpoint dedicado proyecta desde el propio `Case`. Enfoque elegido:
- `Case.ReportTextVersions` **sin** `[JsonIgnore]` pero **excluida de la proyección
  del listado** (`ListByOfficerAsync`).
- El `GET /api/cases/{id}` individual la traería; para no inflarlo, el servicio **no**
  la serializa dentro del `Case`: se marca `Case.ReportTextVersions` con `[JsonIgnore]`
  y el endpoint `GET …/report-text-versions` la lee del `Case` cargado por el repo
  (que **sí** la deserializa de Mongo, porque `[JsonIgnore]` solo afecta la
  serialización JSON, no el mapeo BSON). Resultado: la lista viaja **solo** por el
  endpoint dedicado; nunca engorda `GET /api/cases` ni `GET /api/cases/{id}`.
- Como `[JsonIgnore]` la saca del JSON de `Case`, **no** hace falta tocar el tipo
  `Case` del client para esto.

**D7 (diff en el client — D3-A).**
Diff por palabra sobre el **texto visible** (no el Markdown crudo), más vista lado a
lado opcional. Librería recomendada: **`diff`** (jsdiff, `diffWords`), liviana y sin
deps. Alternativa sin dependencia: LCS por palabras propio (pequeño). El diff y el
render de versión histórica se cargan con **import dinámico** (`next/dynamic` / dynamic
`import()`), igual que `RichTextEditor`, para no sumar peso al bundle del dashboard
cuando no se abre el historial. El texto a diffear se obtiene convirtiendo el Markdown
de la sección a texto plano visible (quitar marcas `**`, `- `, imágenes, enlaces →
su texto). Hay helpers de Markdown en `client/src/lib/report-markdown.ts`
(`stripReportImages`, etc.) sobre los que apoyarse; si hace falta un
`markdownToVisibleText` nuevo, va en ese módulo.

**D8 (render de versión histórica "Ver").**
No existe hoy un render Markdown→HTML standalone (el único render es el editor Tiptap).
Para "Ver" en solo lectura se reusa `RichTextEditor` en modo no editable: Tiptap acepta
`editable: false` (o un editor montado con `editable:false`). El implementer-frontend
decide si extiende `RichTextEditor` con una prop `readOnly` o monta un editor Tiptap
mínimo de solo lectura en el panel. **Preferencia:** una prop `readOnly?: boolean` en
`RichTextEditor` que, cuando es true, desactiva la barra de formato y pone el editor
`editable:false` — reusa el mismo pipeline Markdown y garantiza que "Ver" se ve igual
que el editor. Es un cambio de client, no toca el contrato.

**D9 (autor — snapshot server-side).**
El `author_dni`/`author_name` se toman del `Officer` autenticado (guardado/restaurar)
o del `Officer` del caso (generación), en el **backend**, al construir la versión. El
client **no** manda autor. Hoy `User` tiene `Dni` y `Name` (no apellido separado): se
usa `Officer.Name` tal cual para `author_name`.

---

## Checklist atómico

### Backend (`server/src/Factum.Backend`)

- [ ] `Models/Case.cs`: agregar `List<ReportTextVersion> ReportTextVersions { get; set; } = [];`
      a `Case`, con `[JsonIgnore]` (D6) y XML-doc explicando default `[]` + no migración.
- [ ] `Models/Case.cs`: agregar las clases `ReportTextVersion`, `ReportTextSnapshot`
      (ambas `[BsonIgnoreExtraElements]`) y la clase estática `ReportVersionTriggers`.
- [ ] `DTOs/CaseDtos.cs`: agregar `ReportTextVersionTextsDto`, `ReportTextVersionDto`
      y `ReportTextVersionsResponse(List<ReportTextVersionDto> Versions)`.
- [ ] `DTOs/CaseDtos.cs`: agregar a `ReportTextsDto` los campos opcionales
      `string? Trigger = null` y `string? RestoredFrom = null` (al final del record,
      con default null para no romper clientes viejos).
- [ ] `Infrastructure/MongoRepository.cs`: `ListByOfficerAsync` → agregar
      `.Exclude(c => c.ReportTextVersions)` a la proyección del listado.
- [ ] `Infrastructure/MongoRepository.cs`: cambiar la firma de
      `UpdateReportTextsAsync` para aceptar una `List<ReportTextVersion>? versionsOverride`
      **o** un `ReportTextVersion? versionToPush` (elegir: `versionToPush` para el
      `$push` simple; `versionsOverride` para el caso de poda con `.Set`). Implementar
      la combinación `$set report_texts` (+ `$push`/`$set` versiones) en un solo
      `UpdateOneAsync(Editable(id), …)`. Mantener el retorno `bool` (MatchedCount>0).
- [ ] `Infrastructure/MongoRepository.cs`: agregar `AppendReportVersionAsync(string id,
      ReportTextVersion v, List<ReportTextVersion>? podaOverride, CancellationToken)` para
      la versión de generación (push atómico bajo `Editable(id)`, sin tocar `report_texts`).
- [ ] `Services/Cases/CaseService.cs` (`SaveReportTextsAsync`): tras validar, leer el
      historial del `cas` ya cargado; construir el `ReportTextVersion` candidato
      (`Id` = Guid "N", `CreatedAt` = UtcNow, autor = `Officer` del request,
      `Trigger` = normalizado de `request.Trigger` → `"save"`/`"restore"`,
      `RestoredFrom` solo si `restore`, `Texts` = snapshot del `texts` que se va a
      guardar); aplicar **de-dup** (D4) contra la última versión; aplicar **poda** (D3)
      y llamar al repo con push simple o con override. Si el de-dup lo descarta, llamar
      al repo sin versión (solo `report_texts`, como hoy).
- [ ] `Services/Cases/CaseService.cs` (`GenerateAsync`): antes de `UpdateStatusAsync(…
      Generating)`, agregar versión `trigger:"generate"` (autor = `cas.Officer`) con
      `cas.ReportTexts` vigente, vía `AppendReportVersionAsync` con poda que **conserva
      todas las `generate` y la primera**. De-dup: solo si es idéntica a la última.
- [ ] `Services/Cases/CaseService.cs` (`PrepareGenerationAsync`): ídem para el flujo
      agent (agregar la versión `generate` al abrir el intento, bajo `Editable(id)`).
- [ ] `Services/Cases/CaseService.cs`: nuevo método de servicio + interfaz
      `GetReportTextVersionsAsync(string id, string officerDni, ct)` →
      `Result<ReportTextVersionsResponse>` usando `LoadOwnedAsync` (404/403) y
      mapeando `cas.ReportTextVersions` a DTO, **ordenado descendente por
      `CreatedAt`**. Funciona en cualquier `Status`.
- [ ] Helper de mapeo modelo→DTO para la versión (en el servicio o un mapper), y
      const `MaxReportVersions = 50` documentada.
- [ ] `Controllers/CasesController.cs`: `[HttpGet("{id}/report-text-versions")]`
      devolviendo `Ok(result.Value)` o `this.ErrorResult(result)`; `[Authorize]` ya
      está a nivel controlador.
- [ ] `Services/Cases/CaseValidation.cs` (o donde corresponda): validar que
      `trigger` ∈ {null,"","save","restore"} y que `restored_from` solo venga con
      `restore` (si no, 400); normalizar a `ReportVersionTriggers`.
- [ ] Tests backend (ver "Verificación").

### Frontend (`client/`)

- [ ] `client/src/lib/api.ts`: agregar tipos `ReportVersionTrigger`,
      `ReportTextVersionTexts`, `ReportTextVersion` y extender `ReportTextsRequest`
      con `trigger?: "save"|"restore"` y `restored_from?: string`.
- [ ] `client/src/lib/api.ts`: método `getReportTextVersions(caseId): Promise<{ versions: ReportTextVersion[] }>`
      (`GET /api/cases/${caseId}/report-text-versions`).
- [ ] `client/src/types/index.ts`: reexportar los tipos nuevos.
- [ ] `client/src/components/`: componente nuevo `ReportVersionHistory.tsx` (panel
      modal/lateral con `role="dialog"`, `aria-modal`, foco atrapado, `Esc`): lista de
      versiones de la sección (desc., "Actual" marcada), acciones Ver / Comparar /
      Restaurar, estados sin-historial / cargando / error-con-reintentar (patrón
      `FxBanner`), solo-lectura en `Completed` (Restaurar deshabilitado + tooltip).
      Carga con import dinámico (no entra al bundle del dashboard si no se abre).
- [ ] `client/src/components/`: subcomponentes para "Ver" (render Markdown solo
      lectura, D8) y "Comparar" (diff por palabra, D7, + lado a lado opcional),
      con tokens `--fx-success`/`--fx-danger` y legibilidad sin depender solo del
      color.
- [ ] `client/src/components/editor/RichTextEditor.tsx`: prop `readOnly?: boolean`
      (Tiptap `editable:false` + sin barra de formato) para el "Ver" (D8).
- [ ] `client/src/components/ReportStep.tsx`: por sección, botón "Historial" (ícono
      `history` de lucide-react, `text`/`secondary` chico, junto a "Restaurar texto por
      defecto"), con contador de versiones si hay más de una; abre `ReportVersionHistory`
      para esa `key`. Al restaurar: `update({...textsRef.current, [key]: versionTexts[key]})`
      y disparar el guardado con `trigger:"restore"` + `restored_from`. Opcional: botón
      "Marcar versión" que hace `flush()` con `trigger:"save"`.
- [ ] `client/src/components/ReportStep.tsx` (`save`/`flush`): permitir pasar
      `trigger`/`restored_from` al `api.saveReportTexts` del guardado de restauración
      (los guardados normales los omiten → `"save"`).
- [ ] `client/src/components/case-detail/CaseDetailContent.tsx` (o `CaseDetailModal`):
      acceso al historial en solo-lectura desde el detalle del caso (D7), reusando
      `ReportVersionHistory` con Restaurar/Marcar deshabilitados cuando
      `status==="completed"`.
- [ ] Verificar que `report_text_versions` **no** llega en `getCase`/listado (es por el
      endpoint dedicado); el tipo `Case` del client **no** cambia por esto.
- [ ] Invocar skills UX/UI obligatorias y dejar constancia en el progress.

---

## Verificación

### Backend
- `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` sin errores
  (hoy compila limpio; base de partida verificada).
- **Tests** (crear proyecto `server/tests/Factum.Backend.Tests` si no existe, xUnit)
  cubriendo:
  - Append atómico: un `SaveReportTextsAsync` que cambia el texto agrega exactamente
    una versión y pisa `report_texts` (un solo estado resultante).
  - De-dup: dos guardados idénticos seguidos → una sola versión; guardar sin cambios
    respecto de la última versión → no agrega.
  - Restaurar: `trigger:"restore"` + `restored_from` quedan registrados y la versión
    previa sigue en el historial.
  - Retención: con `MaxReportVersions` chico (inyectado/const), superar el tope no
    borra la primera, ni las `generate`, ni la actual; descarta las intermedias viejas.
  - Solo-lectura del historial: no hay forma por API de editar/borrar una versión.
  - Aislamiento por dueño: `GET …/report-text-versions` de otro perito → 404/403.
  - Caso sin el campo: deserializa `[]` y `SaveReportTextsAsync` empieza el historial;
    `GenerateAsync` agrega la versión `generate`.
  > Los tests que escriban en Mongo limpian **solo por los `_id` que ellos insertaron**
  > (regla dura de datos). Preferible testear el servicio con un repo en memoria/fake
  > para no tocar la base de desarrollo.

### Frontend
- `cd client && npx tsc --noEmit` sin errores.
- `npm run build` del client sin errores (verifica el import dinámico del historial).

### Prueba manual (queda para el usuario)
1. Abrir un caso en borrador, editar "Resultados", esperar el autoguardado →
   abrir el historial de esa sección: aparece una versión con fecha/hora y autor.
2. Hacer varios autoguardados sin cambios → no se multiplican las versiones.
3. Ver una versión anterior (solo lectura, con formato) y Comparar con la actual
   (diff agregado/quitado).
4. Restaurar una versión y confirmar → el editor toma ese texto, se guarda, y queda
   una versión nueva `restore` sin perder las anteriores.
5. Generar el informe → aparece una versión `generate`; el DOCX/PDF/ZIP/hashes salen
   igual que hoy.
6. Abrir un caso `Completed` desde el detalle → se ve el historial en solo-lectura,
   sin poder restaurar.
7. Confirmar que un caso viejo (sin el campo) abre el paso 4 igual que hoy y su
   historial arranca vacío.
