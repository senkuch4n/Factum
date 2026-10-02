# SDD: Catálogos reutilizables e integrantes como lista en "Datos de la causa" (paso 2)

**Slug:** `formulario-caso-catalogos`
**HU:** `docs/hu-formulario-caso-catalogos.md`. El usuario validó las 11 dudas en la opción **A**.
**Fuera de esta SDD:** el desborde de "Fecha de intervención" ya se corrigió en `baac85e` (D10 A). No se toca.
**Base:** rama `feat/formulario-caso-catalogos` (sobre `baac85e`).
**Implementan:** `implementer-backend` (Opus), en `server/src/Factum.Backend` y `server/tests/Factum.Backend.Tests`, y `implementer-frontend` (Opus), solo en `client/`. Antes de tocar `client/` hay que leer `client/AGENTS.md` (Next.js 16). **El backend va primero**: el frontend consume los endpoints y el campo nuevo. Si se implementan en paralelo, el frontend se guía por la sección 4 (Contrato compartido) y no por el código del backend.

---

## 1. Resumen funcional

Cada perito tiene ahora cuatro catálogos de sugerencias propios: **destinatarios**, **partes**, **profesiones** y **tipos de dispositivo**. Se guardan en una colección nueva de MongoDB, `catalog_entries`. La primera vez que el perito abre el paso 2, el servidor arma los catálogos con los valores distintos que ya usó en sus casos (solo **lee** `cases`) y agrega "Teléfono celular". Cada vez que se crea o se edita un caso, el servidor suma al catálogo los valores nuevos. Esa actualización es best-effort: si falla, el guardado del caso no falla. En el paso 2, los campos destinatario, parte denunciante, parte denunciada, nombre de quien propone, profesión de quien propone y tipo de dispositivo pasan a ser un `AutoComplete` de Prime con texto libre. Las sugerencias se filtran sin distinguir mayúsculas ni tildes, y desde cada ítem (lápiz y papelera) o desde "Administrar" se puede editar o quitar un valor del catálogo. El caso sigue guardando **texto**: editar o borrar en el catálogo no cambia ningún caso. Los integrantes pasan a cargarse como una lista ordenada de filas, que viaja en el campo nuevo `integrantes`. El backend sigue escribiendo `integrantes_tribunal` con la frase armada ("A, B y C"), y esa frase es la que lee el informe DOCX, así que la plantilla v4 no cambia. Un caso viejo sin lista se muestra como una sola fila con su texto y genera el mismo informe que antes. La etiqueta "Tribunal" pasa a ser "Destinatario (tribunal, fiscalía, persona…)", pero el JSON (`nombre_tribunal`) y la plantilla no cambian.

## 2. Toca

| Lado | ¿Toca? |
|---|---|
| backend (API) `server/src/Factum.Backend` (+ `server/tests/Factum.Backend.Tests`) | **sí** |
| backend (Tatana) `server/src/Factum.Agent` | **no** |
| client `client/` | **sí** |
| agent-ui `agent-ui/` | **no** |

Plantilla `Templates/plantilla_informe_v4.docx`: **no cambia**.

---

## 3. Modelo de datos (backend)

> Los nombres de campo en Mongo están en **PascalCase**: no hay `ConventionPack` de camelCase, y lo confirmé en `factum_dev.cases` (`NombreTribunal`, `IntegrantesTribunal`…). Las colecciones nuevas siguen el mismo criterio.
>
> Estado actual de la base de desarrollo (inspección de solo lectura del 2026-10-01): `factum_dev` tiene `cases` (1 documento, perito `45115309`, `IntegrantesTribunal: "integrante 1 e integrante 2"`, `TipoDispositivo: "Tipo de dispositivo"`), `expert_profiles` y `agent_events`. Índices de `cases`: `_id` y `Officer.Dni`. No existen `catalog_entries` ni `catalog_seeds`.

### 3.1 Campo nuevo en `cases`: `Integrantes`

```csharp
// Models/Case.cs, junto a IntegrantesTribunal
/// <summary>
/// Integrantes como lista ordenada (formulario-caso-catalogos). null = caso guardado antes de
/// esta HU (o por un cliente que no manda la lista): vale solo IntegrantesTribunal.
/// Cuando no es null, IntegrantesTribunal es la frase derivada (IntegrantesFormatter.Join).
/// </summary>
public List<string>? Integrantes { get; set; }
```

- Es **anulable y sin default `[]`**. Así un documento viejo sin el campo deserializa `null` y se distingue de "lista vacía guardada a propósito". `[BsonIgnoreExtraElements]` ya está en la clase.
- JSON: `integrantes` (`string[] | null`). Sale en `GET /api/cases`, `GET /api/cases/{id}` y en las respuestas de `POST`/`PUT`.
- **No hay migración**. Ningún documento existente se toca hasta que el perito guarde el paso 2 de ese caso, y ahí el `$set` es por campo, como hoy.

### 3.2 Colección nueva `catalog_entries`

```csharp
// Models/CatalogEntry.cs (nuevo)
[BsonIgnoreExtraElements]
public sealed class CatalogEntry
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>DNI del perito dueño (mismo criterio que expert_profiles._id y cases.Officer.Dni).</summary>
    public string OwnerDni { get; set; } = string.Empty;
    /// <summary>Id del catálogo: "destinatarios" | "partes" | "profesiones" | "tipos_dispositivo".</summary>
    public string Catalog { get; set; } = string.Empty;
    /// <summary>Texto que se sugiere y se copia al caso (CatalogText.CleanValue).</summary>
    public string Value { get; set; } = string.Empty;
    /// <summary>Clave de deduplicación (CatalogText.NormalizeKey). Única por OwnerDni + Catalog.</summary>
    public string NormalizedKey { get; set; } = string.Empty;
    public int UseCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
}
```

**Índices** (se crean en el constructor del repositorio, igual que `CaseRepository.EnsureIndexAsync`, con `CancellationToken.None` y reintento si falló):

| Nombre | Clave | Opciones | Para qué |
|---|---|---|---|
| `owner_catalog_key_unique` | `{ OwnerDni: 1, Catalog: 1, NormalizedKey: 1 }` | `Unique = true` | Deduplicación (D8 A) y protección contra carreras del upsert. Por ser prefijo, también sirve para `find({ OwnerDni, Catalog })`. |
| `owner_lastused` | `{ OwnerDni: 1, LastUsedAt: -1 }` | — | `GET /api/catalogs` (todo lo del perito, ordenado por uso reciente). |

### 3.3 Colección nueva `catalog_seeds` (marca de siembra)

```csharp
// Models/CatalogEntry.cs (mismo archivo)
[BsonIgnoreExtraElements]
public sealed class CatalogSeed
{
    [BsonId] public string Id { get; set; } = string.Empty; // DNI del perito
    public DateTime SeededAt { get; set; } = DateTime.UtcNow;
}
```

Un documento por perito, con `_id` = DNI, así que no necesita índices. Si existe, ese perito ya se sembró y **no se vuelve a sembrar nunca**: lo que el perito borró no reaparece por la siembra.

### 3.4 Definición de catálogos (mecanismo genérico, D9 A)

Hay un único lugar en el servidor que dice qué catálogos existen y de qué campos del caso se alimentan:

```csharp
// Services/Catalogs/CatalogDefinitions.cs (nuevo)
public static class CatalogDefinitions
{
    public const string Destinatarios = "destinatarios";
    public const string Partes = "partes";
    public const string Profesiones = "profesiones";
    public const string TiposDispositivo = "tipos_dispositivo";
    public const string DefaultTipoDispositivo = "Teléfono celular";

    /// <summary>Catálogo → campos del caso que lo alimentan. Sumar un catálogo = una línea acá + FIELD_CATALOG en el cliente.</summary>
    public static readonly IReadOnlyDictionary<string, Func<Case, IEnumerable<string>>> Sources =
        new Dictionary<string, Func<Case, IEnumerable<string>>>(StringComparer.Ordinal)
        {
            [Destinatarios]    = c => [c.NombreTribunal],
            [Partes]           = c => [c.ParteDenunciante, c.ParteDenunciada, c.NombreProponente],
            [Profesiones]      = c => [c.ProfesionProponente],
            [TiposDispositivo] = c => [c.TipoDispositivo],
        };

    public static bool Exists(string? id) => id is not null && Sources.ContainsKey(id);
    public static IEnumerable<string> Ids => Sources.Keys;
}
```

### 3.5 Normalización y deduplicación (D8 A)

```csharp
// Services/Catalogs/CatalogText.cs (nuevo, public static, testeable)
public static class CatalogText
{
    public const int MaxValue = 300; // = CaseValidation.MaxLine

    /// <summary>Valor a guardar: trim + cualquier secuencia de espacios en blanco (\s+, incluye \t y \n) → un espacio.</summary>
    public static string CleanValue(string? s);

    /// <summary>
    /// Clave de dedupe: CleanValue → Normalize(FormD) → saca UnicodeCategory.NonSpacingMark,
    /// SpacingCombiningMark y EnclosingMark → Normalize(FormC) → ToLowerInvariant.
    /// </summary>
    public static string NormalizeKey(string? s);
}
```

Vectores, que **tienen que dar igual en C# y en TS** (`client/src/lib/catalogs.ts` → `normalizeCatalogKey`):

| Entrada | `CleanValue` | `NormalizeKey` |
|---|---|---|
| `"  Teléfono   celular "` | `"Teléfono celular"` | `"telefono celular"` |
| `"TELÉFONO CELULAR"` | igual | `"telefono celular"` |
| `"el Sr. Juan Pérez"` / `"el sr. juan perez"` | igual | `"el sr. juan perez"` (las dos) |
| `"Muñoz\tGarcía\n"` | `"Muñoz García"` | `"munoz garcia"` |
| `"   "` / `null` | `""` | `""` (se descarta) |

Si llega un duplicado, se conserva la **primera grafía**: el upsert usa `$setOnInsert` para `Value`.

### 3.6 Compatibilidad con documentos existentes

- `cases` viejos: `Integrantes` deserializa `null`. El informe sigue leyendo `IntegrantesTribunal` (sección 6), así que sale igual.
- La siembra **solo lee** `cases` (método nuevo de solo lectura, con proyección) y escribe solo en `catalog_entries` y `catalog_seeds`.
- Los endpoints de catálogo nunca tocan `cases`, y editar o borrar una entrada no propaga a ningún caso (D11 A).

---

## 4. Contrato compartido (client ↔ server)

JSON en **snake_case_lower** (`Program.cs`: `JsonNamingPolicy.SnakeCaseLower`, tanto en controllers como en minimal APIs). Las **claves de diccionario no se transforman** (no hay `DictionaryKeyPolicy`). Por eso las claves de `catalogs` en la respuesta son literalmente los ids de catálogo, que ya están en snake_case.

### 4.1 Caso: campo nuevo `integrantes`

| JSON | Tipo | Dirección | C# (`server/src/Factum.Backend/…`) | TS (`client/src/…`) |
|---|---|---|---|---|
| `integrantes` | `string[] \| null` | respuesta (`Case`) | `Models/Case.cs` → `List<string>? Integrantes` | `lib/api.ts` → `interface Case { integrantes?: string[] \| null }` |
| `integrantes` | `string[]` (opcional) | `POST /api/cases`, `PUT /api/cases/{id}` | `DTOs/CaseDtos.cs` → `CreateCaseRequest` y `UpdateCaseRequest`: `List<string>? Integrantes = null` | `lib/api.ts` → `interface CaseDataRequest { integrantes?: string[] }` |
| `integrantes_tribunal` | `string` | respuesta: **derivado** cuando hay lista | sin cambio de nombre | `Case.integrantes_tribunal` sigue igual; `CaseDataRequest.integrantes_tribunal` pasa a **opcional** (`?`) y el cliente nuevo **no lo manda** |

Semántica en el servidor (`CaseService`, crear y editar):

- `integrantes` **presente (no null)**: cada ítem pasa por `CatalogText.CleanValue`, se descartan los vacíos y se mantiene el orden. Validaciones: máximo **20** ítems (si hay más, 400 `{ error: "Se pueden cargar hasta 20 integrantes" }`) y cada uno hasta 300 caracteres (si no, 400 `{ error: "El campo integrantes supera los 300 caracteres" }`). Se guardan `Integrantes = lista` (puede ser `[]`) **e** `IntegrantesTribunal = IntegrantesFormatter.Join(lista)`. El `integrantes_tribunal` del request se **ignora**. El texto derivado **no** pasa por el límite de 500 (D6).
- `integrantes` **ausente o null** (cliente viejo): comportamiento de hoy (`integrantes_tribunal`, máximo 500). En crear, `Integrantes` queda `null`. En editar, se hace `$unset` de `Integrantes`: gana la última escritura y no queda una lista vieja que contradiga el texto.

### 4.2 Frase de integrantes (`IntegrantesFormatter.Join` ↔ `joinIntegrantes`)

Tiene que dar idéntico en `server/src/Factum.Backend/Services/Cases/IntegrantesFormatter.cs` y en `client/src/lib/pericial.ts` (la vista previa usa la versión TS; el informe, la C#). La entrada ya llega limpia (trim, sin vacíos).

| Lista | Resultado |
|---|---|
| `[]` | `""` |
| `["Dr. Juan Pérez"]` | `"Dr. Juan Pérez"` |
| `["Dr. Juan Pérez", "Dra. María Gómez"]` | `"Dr. Juan Pérez y Dra. María Gómez"` |
| `["Dr. Juan Pérez", "Dra. María Gómez", "Dr. Luis Díaz"]` | `"Dr. Juan Pérez, Dra. María Gómez y Dr. Luis Díaz"` |
| `["Juan Pérez", "Ignacio Díaz"]` | `"Juan Pérez e Ignacio Díaz"` (D5, **DP1**) |
| `["Juan Pérez", "Hilda Gómez"]` | `"Juan Pérez e Hilda Gómez"` (D5) |
| `["Juan Pérez", "Hielo Ruiz"]` | `"Juan Pérez y Hielo Ruiz"` (D5: "hie" suena /ie/, va "y") |

Regla del conector final (D5): se usa `" e "` cuando el **último** ítem, en minúsculas y sin tildes, empieza con `"i"`, o con `"hi"` seguido de algo que no sea vocal (o de nada). En cualquier otro caso se usa `" y "`. Los anteriores se separan con `", "`.

### 4.3 Mostrar integrantes de un caso (`integrantesFromCase`, solo cliente)

```ts
// client/src/lib/pericial.ts
export function integrantesFromCase(cas: Pick<Case, "integrantes" | "integrantes_tribunal">): string[] {
  const text = (cas.integrantes_tribunal ?? "").trim();
  const list = Array.isArray(cas.integrantes) ? cas.integrantes.map(s => s.trim()).filter(Boolean) : null;
  // Lista coherente con el texto derivado → filas. Si no (caso viejo, o rollback que reescribió solo el texto) → una fila con el texto.
  if (list && joinIntegrantes(list) === text) return list;
  return text ? [text] : [];
}
```

Ejemplo: un caso viejo con `"Dres. Juan Pérez y María Gómez"` se muestra como una fila con ese texto. Si se guarda sin tocar, viaja `integrantes: ["Dres. Juan Pérez y María Gómez"]`, el derivado queda idéntico y el informe no cambia.

### 4.4 Endpoints de catálogo (nuevos)

Controller nuevo `server/src/Factum.Backend/Controllers/CatalogsController.cs`, con `[Route("api/catalogs")]`, `[Authorize]` y el dueño sacado de `HttpContext.Items["User"]` (como `CasesController`). Errores con `{ error }` vía `ResultHttpExtensions.ErrorResult`.

| Método y ruta | Body | Respuesta OK | Errores |
|---|---|---|---|
| `GET /api/catalogs` | — | `200 { "catalogs": { "destinatarios": CatalogEntry[], "partes": […], "profesiones": […], "tipos_dispositivo": […] } }`. Están las **cuatro claves siempre**, aunque estén vacías. Cada lista va ordenada por `last_used_at` desc, después `use_count` desc y después `value` (ordinal ignore case), con un tope de **500 por catálogo**. Siembra la primera vez (sección 5.3). | `401` sin token |
| `PUT /api/catalogs/{catalog}/entries/{id}` | `{ "value": string }` | `200 CatalogEntry` (la entrada actualizada) | `404 { error: "Catálogo desconocido" }`; `404 { error: "No se encontró el valor" }` (no existe o **es de otro perito**); `400 { error: "Ingresá un valor" }` (vacío después de limpiar); `400 { error: "El valor supera los 300 caracteres" }`; `409 { error: "Ya tenés «<valor existente>» en tus sugerencias" }` (la clave nueva choca con otra entrada del mismo perito y catálogo) |
| `DELETE /api/catalogs/{catalog}/entries/{id}` | — | `204` sin body | `404` como arriba |

`CatalogEntry` (JSON de respuesta):

| JSON | Tipo | C# `DTOs/CatalogDtos.cs` → `CatalogEntryDto` | TS `client/src/lib/api.ts` → `CatalogEntry` |
|---|---|---|---|
| `id` | `string` | `string Id` | `id: string` |
| `value` | `string` | `string Value` | `value: string` |
| `use_count` | `number` | `int UseCount` | `use_count: number` |
| `last_used_at` | `string` (ISO UTC) | `DateTime LastUsedAt` | `last_used_at: string` |

DTOs C# (`DTOs/CatalogDtos.cs`, nuevo):

```csharp
public sealed record CatalogEntryDto(string Id, string Value, int UseCount, DateTime LastUsedAt);
public sealed record CatalogsResponse(Dictionary<string, List<CatalogEntryDto>> Catalogs);
public sealed record UpdateCatalogEntryRequest(string? Value = null); // string? sin [Required], como CaseDtos
```

Tipos TS (`client/src/lib/api.ts`, reexportados desde `client/src/types/index.ts`):

```ts
export type CatalogId = "destinatarios" | "partes" | "profesiones" | "tipos_dispositivo";
export interface CatalogEntry { id: string; value: string; use_count: number; last_used_at: string; }
export interface CatalogsResponse { catalogs: Record<CatalogId, CatalogEntry[]>; }
```

Métodos en `api` (`client/src/lib/api.ts`):

```ts
getCatalogs(): Promise<CatalogsResponse>                                  // GET /api/catalogs
updateCatalogEntry(catalog: CatalogId, id: string, value: string): Promise<CatalogEntry>  // PUT
deleteCatalogEntry(catalog: CatalogId, id: string): Promise<void>         // DELETE, 204
```

`request<T>()` siempre hace `res.json()`. Para el 204 se agrega un helper `requestNoContent(path, options)`, que hace el mismo manejo de errores (`ApiError`) que `request` pero no lee el body. **No** se usa `fetch` suelto sin manejo de errores, como en `reportAgentEvent`.

### 4.5 Campo del caso ↔ catálogo (cliente)

`client/src/lib/catalogs.ts` (nuevo) es el espejo de `CatalogDefinitions.Sources`:

| Campo del caso (JSON) | Catálogo |
|---|---|
| `nombre_tribunal` | `destinatarios` |
| `parte_denunciante`, `parte_denunciada`, `nombre_proponente` | `partes` |
| `profesion_proponente` | `profesiones` |
| `tipo_dispositivo` | `tipos_dispositivo` |

### 4.6 Claves de `missing` y mensajes

**Sin cambios de claves.** Solo cambian textos del cliente (D3):

- `CASE_MESSAGES.nombre_tribunal`: `"Ingresá el destinatario"`.
- `REQUIREMENT_META.nombre_tribunal.label`: `"Destinatario"`.

---

## 5. Backend: diseño

### 5.1 Archivos

| Archivo | Cambio |
|---|---|
| `Models/Case.cs` | `List<string>? Integrantes` (3.1) |
| `Models/CatalogEntry.cs` | nuevo: `CatalogEntry`, `CatalogSeed` |
| `DTOs/CaseDtos.cs` | `List<string>? Integrantes = null` en `CreateCaseRequest` y `UpdateCaseRequest` |
| `DTOs/CatalogDtos.cs` | nuevo (4.4) |
| `Services/Cases/IntegrantesFormatter.cs` | nuevo: `public static string Join(IReadOnlyList<string> items)` y `public static List<string> Clean(IEnumerable<string?>? items)` (`CleanValue` y sin vacíos) |
| `Services/Cases/CaseValidation.cs` | `CaseFields` suma `IReadOnlyList<string>? Integrantes` (null = no vino). Si viene la lista, `CaseFields.From` ya pone `IntegrantesTribunal = Join(lista)`. `ValidateCaseData` valida la lista (máximo 20 ítems, cada uno hasta 300). El chequeo de 500 sobre `integrantes_tribunal` se aplica **solo si `Integrantes is null`**. `ValidateForGenerate` construye `CaseFields` con `Integrantes: null` (no cambia nada más ahí) |
| `Services/Cases/CaseService.cs` | inyecta `ICatalogService`; en crear y editar setea `Integrantes`; después de guardar llama a `RecordUsageAsync` (5.4) |
| `Infrastructure/MongoRepository.cs` | `CaseDataUpdate` suma `IReadOnlyList<string>? Integrantes`. `UpdateCaseDataAsync`: si no es null, `$set Integrantes`; si es null, `$unset Integrantes`. `IntegrantesTribunal` se sigue seteando siempre (ya viene derivado). Método nuevo **de solo lectura** `ListCatalogSourcesAsync(officerDni)` (5.3) |
| `Infrastructure/CatalogRepository.cs` | nuevo: `ICatalogRepository` / `CatalogRepository` (5.2) |
| `Services/Catalogs/CatalogDefinitions.cs`, `CatalogText.cs`, `CatalogService.cs` | nuevos |
| `Controllers/CatalogsController.cs` | nuevo |
| `Program.cs` | `AddSingleton<ICatalogRepository, CatalogRepository>()` y `AddScoped<ICatalogService, CatalogService>()` |
| `Services/Reports/ReportValues.cs` | **sin cambio funcional**: `{fraseIntegracion}` sigue saliendo de `IntegrantesTribunal` (sección 6). Solo se actualiza el comentario |

### 5.2 `ICatalogRepository`

```csharp
public interface ICatalogRepository
{
    Task<List<CatalogEntry>> ListByOwnerAsync(string ownerDni, CancellationToken ct = default);
    Task<CatalogEntry?> FindAsync(string ownerDni, string catalog, string id, CancellationToken ct = default);
    /// <summary>Upsert por (OwnerDni, Catalog, NormalizedKey). BulkWrite unordered; ignora E11000 (carrera).</summary>
    Task UpsertUsageAsync(string ownerDni, IReadOnlyList<CatalogUsage> usages, CancellationToken ct = default);
    /// <summary>Cambia Value + NormalizedKey + UpdatedAt. Devuelve null si no matcheó; lanza DuplicateKey si choca.</summary>
    Task<CatalogEntry?> UpdateValueAsync(string ownerDni, string catalog, string id, string value, string key, CancellationToken ct = default);
    Task<bool> DeleteAsync(string ownerDni, string catalog, string id, CancellationToken ct = default);
    Task<bool> IsSeededAsync(string ownerDni, CancellationToken ct = default);
    Task MarkSeededAsync(string ownerDni, CancellationToken ct = default); // upsert por _id en catalog_seeds
}

public sealed record CatalogUsage(string Catalog, string Value, string Key, int Count, DateTime UsedAt);
```

- **Todas** las consultas y escrituras filtran por `OwnerDni` (y por `Catalog` cuando aplica). Una entrada de otro perito simplemente no matchea y el resultado es 404 (D7).
- `UpsertUsageAsync`: un `UpdateOneModel` por uso, con `IsUpsert = true`, filtro `{OwnerDni, Catalog, NormalizedKey}` y update `$setOnInsert {_id: Guid, OwnerDni, Catalog, NormalizedKey, Value, CreatedAt: now, UpdatedAt: now}` + `$inc {UseCount: Count}` + `$max {LastUsedAt: UsedAt}`. `BulkWriteOptions { IsOrdered = false }`. Atrapa `MongoBulkWriteException` cuyos errores sean todos `DuplicateKey` (dos upserts concurrentes de la misma clave) y lo ignora.
- `DeleteAsync` es un `DeleteOne` por `{_id, OwnerDni, Catalog}`. Nunca `DeleteMany`.

### 5.3 `ICatalogService`

```csharp
public interface ICatalogService
{
    Task<CatalogsResponse> GetAsync(string ownerDni, CancellationToken ct = default);
    Task<Result<CatalogEntryDto>> UpdateAsync(string ownerDni, string catalog, string id, UpdateCatalogEntryRequest req, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(string ownerDni, string catalog, string id, CancellationToken ct = default);
    /// <summary>Best-effort: nunca lanza (try/catch + LogWarning).</summary>
    Task RecordUsageAsync(string ownerDni, IEnumerable<(string Catalog, string Value)> values, CancellationToken ct = default);
}
```

**`GetAsync` con siembra (D4 A):**

1. Si `!IsSeededAsync(dni)`:
   1. `cases.ListCatalogSourcesAsync(dni)` es **solo lectura**: `Find(Officer.Dni == dni)` con proyección a `NombreTribunal, ParteDenunciante, ParteDenunciada, NombreProponente, ProfesionProponente, TipoDispositivo, CreatedAt`. Incluye casos de cualquier `SchemaVersion` y estado.
   2. Por cada caso y cada `CatalogDefinitions.Sources`, toma los valores no vacíos (`CleanValue`) y los agrupa por `(catalog, NormalizeKey)`. Se queda con la grafía del caso **más viejo** (primera grafía), `Count` = ocurrencias y `UsedAt` = `CreatedAt` más reciente.
   3. Suma `("tipos_dispositivo", "Teléfono celular")` con `Count = 0` y `UsedAt = DateTime.UnixEpoch`, así no le gana en orden a lo que el perito usa de verdad. Si ya está en los casos, el upsert no duplica.
   4. `UpsertUsageAsync` y **después** `MarkSeededAsync`. Si falla a mitad de camino, la marca no queda y el próximo GET reintenta. Los upserts son idempotentes en clave, aunque `UseCount` se podría inflar en un reintento (aceptable, solo afecta el orden).
   5. Si cualquier paso de la siembra lanza, se loguea un warning y se sigue con el listado. El GET no falla por la siembra.
2. `ListByOwnerAsync(dni)` agrupa por `Catalog`, ordena (4.4), aplica el tope de 500 y devuelve las cuatro claves siempre. Si hay entradas con un `Catalog` que no está en `CatalogDefinitions`, se ignoran.

**`UpdateAsync`:** valida el catálogo (`404 "Catálogo desconocido"`); limpia con `value = CleanValue(req.Value)` (vacío → 400, más de 300 → 400); `key = NormalizeKey(value)`. Llama a `UpdateValueAsync`: si devuelve null, 404 `"No se encontró el valor"`; si hay `MongoWriteException` con categoría `DuplicateKey`, busca la entrada que choca para el mensaje y devuelve 409. Si la clave no cambia (por ejemplo, solo cambia una tilde o una mayúscula), es la misma entrada y se actualiza sin conflicto.

**`DeleteAsync`:** catálogo inválido o no matcheó → 404; OK → el controller responde `NoContent()`.

### 5.4 Alta automática al guardar un caso (D2 A)

En `CaseService`, **después** de `InsertAsync` o de un `UpdateCaseDataAsync` exitoso, y antes del `return Result.Ok(...)`:

```csharp
await _catalogs.RecordUsageAsync(officer.Dni, CatalogValues(cas, previous), ct);
```

- `CatalogValues` recorre `CatalogDefinitions.Sources` sobre el caso guardado.
  - **Crear:** todos los valores no vacíos.
  - **Editar:** **solo los campos cuyo `NormalizeKey` cambió** respecto del caso leído antes del update (`LoadOwnedAsync` ya lo trae). Así, guardar otra vez un caso sin tocar la profesión no vuelve a meter en el catálogo una entrada que el perito borró (D12).
- Antes del upsert se deduplica por `(catalog, key)`: si la misma persona es denunciante y proponente, se cuenta una vez y la grafía es la primera.
- `RecordUsageAsync` **nunca lanza**: `try { … } catch (Exception ex) { _log.LogWarning(ex, "No se pudo actualizar el catálogo de {Dni}", dni); }`. Un fallo del catálogo **no puede** cambiar la respuesta del `POST`/`PUT` (requisito de la HU).
- No se toca ningún caso desde el servicio de catálogo.

---

## 6. Informe DOCX (`ReportService` / `ReportValues`, plantilla v4)

- **La plantilla no cambia.** `{fraseIntegracion}` sigue siendo `integrantes.Length > 0 ? ", con la integración de " + integrantes : ""`, con `integrantes = cas.IntegrantesTribunal.Trim()` (regla T12 sin cambios).
- **Por qué del texto derivado y no de la lista (D4):** para un caso guardado con esta HU, `IntegrantesTribunal` **es** `Join(Integrantes)` (lo escribe el servidor en la misma operación). Para un caso viejo es el texto de siempre. Así:
  - los casos viejos generan exactamente el mismo informe (escenario "Caso existente con integrantes en el formato viejo");
  - un rollback del backend (que lee y escribe solo `IntegrantesTribunal`) sigue generando bien;
  - hay una única fuente para el informe y no hace falta lógica de fallback en `ReportValues`.
- Resultado esperado para `["Dr. Juan Pérez", "Dra. María Gómez", "Dr. Luis Díaz"]`: `"…que tramita ante <tramiteAnte>, con la integración de Dr. Juan Pérez, Dra. María Gómez y Dr. Luis Díaz, conforme los siguientes datos:"`.
- `nombre_tribunal` → `{nombreTribunal}` y `{tramiteAnte}`, sin cambios (D5 A de la HU: solo cambia la etiqueta).

---

## 7. Frontend (`client/`): diseño

> Skills obligatorios del `implementer-frontend`: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (como criterio, **sin 3D**) y `web-design-guidelines` (autochequeo); `ui-styling` y `mblode-agent-skills-ui-animation` si corresponde. Solo tokens `fx-*` y pt de Prime. App: **solo `client/`**.

### 7.1 Archivos

| Archivo | Cambio |
|---|---|
| `src/lib/api.ts` | tipos `CatalogId`, `CatalogEntry`, `CatalogsResponse`; `Case.integrantes?: string[] \| null`; `CaseDataRequest.integrantes?: string[]` e `integrantes_tribunal?` opcional; métodos `getCatalogs`, `updateCatalogEntry`, `deleteCatalogEntry`; helper `requestNoContent` |
| `src/types/index.ts` | reexporta los tipos nuevos. `CaseFormData`: **sale** `integrantes_tribunal: string` y **entra** `integrantes: IntegranteRow[]`, con `interface IntegranteRow { key: string; value: string }` (el `key` es estable, para React y el foco al reordenar) |
| `src/lib/catalogs.ts` | nuevo: `CATALOG_IDS`, `FIELD_CATALOG` (4.5), `CATALOG_LABELS` (`destinatarios: "Destinatarios"`, `partes: "Partes"`, `profesiones: "Profesiones"`, `tipos_dispositivo: "Tipos de dispositivo"`), `normalizeCatalogKey` (vectores 3.5: `s.normalize("NFD").replace(/\p{M}/gu, "")…`, colapsar `\s+`, trim, `toLowerCase()`), `filterEntries(entries, query)` (contiene, sobre claves normalizadas) |
| `src/lib/pericial.ts` | `joinIntegrantes` (4.2), `integrantesFromCase` (4.3), `newIntegranteRow(value = "")` (key con un contador de módulo, **no** `crypto.randomUUID`, que falla fuera de contexto seguro, como un http por IP de LAN). `EMPTY_CASE_FORM.integrantes = []`. `prefillFromLastCase` devuelve `integrantes: integrantesFromCase(last).map(newIntegranteRow)`. `caseToForm`: el bucle de strings sigue, salteando `integrantes`, y `integrantes` sale de `integrantesFromCase(cas)`. `formToCaseRequest`: los strings igual que hoy, más `integrantes: form.integrantes.map(r => r.value.trim()).filter(Boolean)`, sin mandar `integrantes_tribunal`. `CASE_MESSAGES.nombre_tribunal` y `REQUIREMENT_META.nombre_tribunal.label` según 4.6 |
| `src/hooks/useCatalogs.ts` | nuevo (7.2) |
| `src/lib/prime/pt/autocomplete.ts` | nuevo pt de `AutoComplete` (root, input, dropdownButton, panel, list, item, emptyMessage, footer, transition), con el mismo criterio visual que `dropdown.ts` y `inputtext.ts`. Se registra en `pt/index.ts` |
| `src/components/form/CatalogAutoComplete.tsx` | nuevo (7.3) |
| `src/components/form/CatalogManageDialog.tsx` | nuevo (7.4) |
| `src/components/form/IntegrantesField.tsx` | nuevo (7.5) |
| `src/components/FormField.tsx` | prop opcional `labelAside?: React.ReactNode`, alineada a la derecha en la fila de la etiqueta, **fuera** del `<label>` para no anidar controles interactivos |
| `src/components/CaseFormStep.tsx` | `FieldDef` suma `catalog?: CatalogId`. `FieldDef.key` excluye `"imeiOverride" \| "integrantes"`. `renderField` usa `CatalogAutoComplete` cuando hay `catalog`. La fila de integrantes se reemplaza por `IntegrantesField`. Etiqueta y placeholder de `nombre_tribunal` cambian (7.6). `useCatalogs()` se llama acá |
| `src/app/dashboard/page.tsx` | `newCaseForm` sin cambios de lógica (sigue con `tipo_dispositivo: DEFAULT_TIPO_DISPOSITIVO`); se verifica que compile con el `CaseFormData` nuevo |

### 7.2 `useCatalogs()`

```ts
export function useCatalogs(): {
  catalogs: Record<CatalogId, CatalogEntry[]>; // vacío por catálogo mientras carga o si falló
  loading: boolean;
  error: boolean;                               // true → aviso discreto, los campos siguen como texto libre
  update(catalog: CatalogId, id: string, value: string): Promise<CatalogEntry>; // tira ApiError (p. ej. 409)
  remove(catalog: CatalogId, id: string): Promise<void>;
}
```

- Carga con `api.getCatalogs()` **al montar** (cada vez que se monta el paso 2). Eso cubre "la próxima vez que abra el paso 2 aparece entre las sugerencias", sin acoplarlo a `handleSaveCase`.
- `update` y `remove` actualizan el estado local después del OK (reemplazan o sacan la entrada), así los tres campos de "partes" lo ven al instante.
- **Nunca** modifican `form`: el texto del campo queda como está (D11 A, snapshot).

### 7.3 `CatalogAutoComplete`

Es un wrapper de `AutoComplete` de `primereact/autocomplete` 10.9.9:

- Props: `id`, `name`, `value: string`, `onChange(value: string)`, `entries: CatalogEntry[]`, `catalog: CatalogId`, `catalogLabel`, `invalid`, `ariaDescribedBy`, `required`, `maxLength`, `placeholder`, `onManage(target?: { id: string; mode: "edit" | "delete" })`.
- `dropdown` (botón con `aria-label="Mostrar sugerencias"`), `dropdownMode="blank"`, `completeMethod` → `filterEntries(entries, query)`, `field="value"`, `forceSelection={false}`, `showEmptyMessage`, `emptyMessage="Todavía no guardaste valores. Escribí uno y se va a sugerir en tus próximos casos"`, `appendTo="self"` o `document.body` (lo que no rompa el `overflow` de las secciones; lo verifica el implementador en móvil) y `scrollHeight` acotado para que la lista no se salga de la pantalla.
- **Texto libre:** `onChange` recibe `string` (mientras se tipea) u objeto (al elegir). Se normaliza a `typeof v === "string" ? v : v.value`. El campo queda con lo que se escribió aunque no se elija nada.
- **"Nuevo":** si la consulta no está vacía y su `normalizeCatalogKey` no coincide con ninguna entrada, se antepone a las sugerencias un pseudo-ítem `{ id: "__new__", value: query }`, que el `itemTemplate` muestra como `Nuevo: «…» (se guarda con el caso)`. Elegirlo deja el texto tal cual.
- **Acciones por ítem (D3 A, D9):** el `itemTemplate` de cada entrada real muestra el valor (truncado, con `title`) y, a la derecha, lápiz y papelera. Son `<button type="button" tabIndex={-1} aria-label="Editar «…»" / "Quitar «…» de tus sugerencias">`, con `onMouseDown={e => { e.preventDefault(); e.stopPropagation(); }}` para que no seleccionen la opción ni cierren el panel antes de tiempo. El `onClick` cierra el panel y llama a `onManage({ id, mode })`, que abre el diálogo "Administrar" con esa entrada en edición o en confirmación de borrado. Objetivos táctiles de `min-h-11 min-w-11` en `pointer: coarse` y de 36 px en escritorio.
- **"Administrar" al pie** (`panelFooterTemplate`): un botón de texto `Administrar sugerencias` que abre el diálogo.
- **Acceso por teclado a editar y borrar:** dentro de un listbox los botones no son alcanzables con Tab. Por eso cada campo con catálogo muestra en `FormField.labelAside` un botón de ícono chico (`ListChecks` de lucide, `aria-label="Administrar sugerencias: <catalogLabel>"`), que abre el mismo diálogo. Es el camino accesible. Teclado del combobox: flechas, Enter y Escape (los de Prime).
- ARIA: `inputId={id}`. `aria-required`, `aria-invalid` y `aria-describedby` van al `<input>`; si `AutoComplete` no los reenvía como props, se pasan por `pt.input` (mismo problema y mismo patrón que `calendarPt`). El implementador lo verifica en el DOM.
- Mantiene el ✓ verde de "obligatorio completo" que hoy tiene `InputText`, sin pisar el botón del dropdown.

### 7.4 `CatalogManageDialog`

- Es un `Dialog` de Prime (pt `dialog`), con título `Tus sugerencias: <catalogLabel>` y ancho `w-[min(32rem,100%)]` por pt.
- Arriba tiene un `InputText` de búsqueda (filtra con `filterEntries`). Debajo, la lista completa del catálogo, con scroll y `max-h` acotado al viewport. Cada fila tiene el valor, el lápiz y la papelera, con foco visible y alcanzables con Tab.
- **Editar:** la fila se convierte en un `InputText` (maxLength 300) con "Guardar" y "Cancelar". Enter guarda y Escape cancela la edición (sin cerrar el diálogo). Si sale bien: `toast.success("Sugerencia actualizada")`. Un 409 o un 400 muestra el `error` del servidor debajo del input (`role="alert"`) y deja el input abierto.
- **Borrar:** confirmación **en línea** en la fila: `¿Quitar de tus sugerencias? Los casos que lo usan no cambian`, con [Quitar] (danger) y [Cancelar], y el foco en Cancelar. Si sale bien: `toast.success("Sugerencia quitada")`.
- Si se abre con `target`, arranca con esa fila en edición o en confirmación y el foco en ella.
- Estado vacío: el mismo texto del `emptyMessage`.
- Toast con `useFxToast()` (`components/shell/FxToastProvider.tsx`).

### 7.5 `IntegrantesField`

- Ocupa las dos columnas (`sm:col-span-2`). Usa `FormField` con `icon={Users}`, `label="Integrantes"` y `sublabel="· opcional"`. El `FormField.id` apunta al input de la primera fila, o al botón "Agregar" si no hay filas.
- Cada fila tiene: un `InputText` (`placeholder="Ej.: Dr. Juan Pérez"`, `maxLength={300}`, `aria-label="Integrante N"`); los botones subir y bajar (`ChevronUp`/`ChevronDown`, `aria-label="Subir integrante N"` / `"Bajar integrante N"`, deshabilitados en los extremos); y quitar (`X` o `Trash2`, `aria-label="Quitar integrante N"`). Todos con `min-h-11`. Al reordenar, el foco sigue al botón que se usó en su nueva posición. Al quitar, el foco va a la fila anterior, o a "Agregar" si no quedan.
- Abajo va `+ Agregar integrante`, deshabilitado con 20 filas (D6), con un texto que lo explica. **Enter en la última fila** agrega una fila nueva y la enfoca (`preventDefault`, para que no dispare nada más). Enter en otra fila no hace nada.
- Vista previa (`aria-live="polite"`), solo si hay al menos un integrante no vacío: `…con la integración de {joinIntegrantes(valores limpios)}`, con `text-xs text-fx-text-2`.
- Con cero filas: solo "+ Agregar integrante" y el sublabel "· opcional".
- Las filas vacías se ven pero no viajan (`formToCaseRequest` las filtra).
- **No hay drag & drop** en esta HU (D10): con subir y bajar alcanza, y es accesible por teclado.

### 7.6 Etiquetas y textos

- `nombre_tribunal`: `label: "Destinatario (tribunal, fiscalía, persona…)"`, `placeholder: "Tribunal, fiscalía o persona…"`, `icon: Landmark` (sin cambios).
- Aviso discreto si falla `GET /api/catalogs`: un solo `<p role="status" className="text-xs text-fx-text-3">No se pudieron cargar tus sugerencias</p>` debajo de `StepHeader`, sin bloquear nada.
- El resto de las etiquetas, obligatorios, mensajes, foco al primer error (`CASE_FORM_FOCUS_ORDER`, `errorKeyToFieldId`) y el contador n/m **no cambian**. Los campos con catálogo conservan sus ids `case-<campo>` en el `<input>`, así el foco y el checklist de "Generar" siguen funcionando.

---

## 8. Decisiones técnicas

| # | Decisión | ¿Usuario? |
|---|---|---|
| D1 | El campo nuevo se llama `integrantes` (`string[] \| null`). `null` = caso viejo. No se renombra ni se borra `integrantes_tribunal`. | no (HU D6 A) |
| D2 | El backend deriva `integrantes_tribunal` de la lista. Si la lista viene, ignora el `integrantes_tribunal` del request. Si no viene, hace `$unset Integrantes` en editar (gana la última escritura). | no |
| D3 | Etiqueta "Destinatario (tribunal, fiscalía, persona…)", mensaje "Ingresá el destinatario" y label de checklist "Destinatario". Sin cambios en JSON ni en plantilla. | no (HU D5 A) |
| D4 | El informe lee el texto derivado `IntegrantesTribunal`, no la lista. `ReportValues` no cambia funcionalmente. Compatible con casos viejos y con rollback. | no |
| D5 | El conector final es `" e "` delante de un ítem que empieza con sonido /i/ ("i…", "hi…" + consonante), y `" y "` en el resto. Mejora la corrección del escrito, pero cambia el texto respecto del Gherkin literal en ese caso borde. | **DP1 (sí)** |
| D6 | Máximo 20 integrantes y 300 caracteres por integrante. El texto derivado queda exento del límite de 500 de `integrantes_tribunal` (que sigue valiendo para clientes viejos). | no |
| D7 | Autorización: todo filtra por `OwnerDni` = DNI del token. Una entrada ajena o inexistente da 404, no 403 (no revela que existe). | no |
| D8 | Dedupe con `NormalizeKey` (trim, espacios colapsados, sin marcas diacríticas, minúsculas). La "ñ" se pliega a "n" ("Muñoz" = "Munoz"). Se conserva la primera grafía. | no |
| D9 | Lápiz y papelera en cada ítem, para mouse y touch, abren el diálogo "Administrar" con esa entrada en edición o en confirmación (no se edita dentro del overlay del combobox, para no anidar controles en un `listbox`). El camino por teclado es el botón de ícono junto a la etiqueta y el pie "Administrar sugerencias". | no (dentro de HU D3 A) |
| D10 | Reordenar con botones subir y bajar, sin drag & drop. | no |
| D11 | Siembra única por perito, marcada en `catalog_seeds`, al primer `GET /api/catalogs`. Lee todos sus casos (cualquier estado o versión) y suma "Teléfono celular" con prioridad mínima. Lo borrado no reaparece por la siembra. | no (HU D4 A) |
| D12 | Alta automática: crear registra todos los valores; editar registra **solo los campos que cambiaron**. Si un valor borrado del catálogo se vuelve a usar en un caso, vuelve a entrar, que es lo esperado por HU D2 A. | no |
| D13 | No hay endpoint para **agregar** a mano desde "Administrar": se agrega usando el valor en un caso. Se puede sumar un `POST` después sin cambiar el contrato. | no |
| D14 | Sin migraciones ni escrituras sobre `cases` fuera del `PUT` que dispara el perito. La siembra solo lee `cases`. | no (regla dura) |
| D15 | Tope de 500 entradas por catálogo en el `GET` (ordenadas por uso reciente). | no |

## 9. Decisiones pendientes del usuario

### DP1. Conector "e" delante de /i/ en la frase de integrantes (D5). No bloquea.

- **A (recomendada, implementada por defecto):** `"Juan Pérez e Ignacio Díaz"` y `"… e Hilda Gómez"`. Es lo correcto en castellano y en un escrito judicial se nota. En la práctica casi no aparece, porque cada fila suele empezar con "Dr."/"Dra.".
- B: siempre `" y "`, literal como el Gherkin.

Pasar a B es sacar una rama de `Join` y `joinIntegrantes` y dos filas de la tabla de tests.

---

## 10. Checklist atómico

### 10.1 Backend (`implementer-backend`)

**Regla dura de datos (AGENTS.md):** la MongoDB de desarrollo (`factum_dev`) tiene casos cargados a mano. Nada de migraciones, `updateMany` ni escrituras sobre `cases` existentes. Una prueba manual que escriba limpia **solo por los `_id` que insertó**. Crear las colecciones e índices nuevos está bien.

- [ ] B1. `Models/Case.cs`: `List<string>? Integrantes` (3.1), sin default.
- [ ] B2. `Models/CatalogEntry.cs`: `CatalogEntry` y `CatalogSeed` con `[BsonIgnoreExtraElements]` (3.2, 3.3).
- [ ] B3. `Services/Catalogs/CatalogText.cs`: `CleanValue` y `NormalizeKey` (3.5).
- [ ] B4. `Services/Catalogs/CatalogDefinitions.cs` (3.4).
- [ ] B5. `Services/Cases/IntegrantesFormatter.cs`: `Clean` y `Join` con la regla "e"/"y" (4.2, D5).
- [ ] B6. `DTOs/CaseDtos.cs`: `List<string>? Integrantes = null` en `CreateCaseRequest` y `UpdateCaseRequest`.
- [ ] B7. `CaseValidation.cs`: `CaseFields.Integrantes`. `From(...)` limpia la lista y deriva `IntegrantesTribunal` cuando viene. Validación de 20 ítems y 300 por ítem. El chequeo de 500 de `integrantes_tribunal` solo si la lista es null. `ValidateForGenerate` pasa `null`.
- [ ] B8. `MongoRepository.cs`: `CaseDataUpdate.Integrantes`; en `UpdateCaseDataAsync`, `$set` si no es null y `$unset` si es null.
- [ ] B9. `MongoRepository.cs`: `ListCatalogSourcesAsync(officerDni)` de solo lectura, con proyección (5.3) y declarado en `ICaseRepository`.
- [ ] B10. `DTOs/CatalogDtos.cs` (4.4).
- [ ] B11. `Infrastructure/CatalogRepository.cs`: colecciones `catalog_entries` y `catalog_seeds`, los dos índices de 3.2 y los métodos de 5.2 (bulk upsert unordered que tolera E11000).
- [ ] B12. `Services/Catalogs/CatalogService.cs`: `GetAsync` con siembra, `UpdateAsync`, `DeleteAsync` y `RecordUsageAsync` que nunca lanza (5.3).
- [ ] B13. `Controllers/CatalogsController.cs`: `GET /api/catalogs`, `PUT` y `DELETE /api/catalogs/{catalog}/entries/{id}`, con los códigos de 4.4 (DELETE responde `NoContent()`).
- [ ] B14. `CaseService.cs`: inyecta `ICatalogService`. Crear setea `Integrantes`. Editar pasa `Integrantes` al update. Después de guardar llama a `RecordUsageAsync` con todos los valores (crear) o con los que cambiaron (editar), deduplicados (5.4).
- [ ] B15. `Program.cs`: registra `ICatalogRepository` (singleton) e `ICatalogService` (scoped).
- [ ] B16. `ReportValues.cs`: solo un comentario sobre `{fraseIntegracion}`, que sale del texto derivado (sección 6). Sin cambio funcional.
- [ ] B17. Tests en `server/tests/Factum.Backend.Tests/` (10.3).
- [ ] B18. `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` y `dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj` en verde.
- [ ] B19. Humo manual con el backend en Development: `GET /api/catalogs` con el perito de prueba muestra las cuatro claves, los valores de su caso y "Teléfono celular", y en `factum_dev` aparecen `catalog_entries` y `catalog_seeds` con sus índices. **Verificar con `mongosh` que el documento de `cases` existente no cambió** (comparar `NombreTribunal`, `IntegrantesTribunal` y la ausencia de `Integrantes` antes y después). No se ejecuta `PUT /api/cases` sobre el caso del usuario. Si hace falta probar el `PUT`, se crea un caso propio de prueba y después se borra por su `_id`, junto con las entradas de catálogo que haya generado (también por `_id`).
- [ ] B20. `progress/impl_backend_formulario-caso-catalogos.md` con los archivos tocados, la salida de build y test, y la verificación de B19.

### 10.2 Frontend (`implementer-frontend`, app `client/`)

- [ ] F1. Leer `client/AGENTS.md` y la guía de Next 16 que corresponda. Invocar los skills obligatorios (7, encabezado).
- [ ] F2. `lib/api.ts`: tipos y campos de 4.1 y 4.4, `requestNoContent`, `getCatalogs`, `updateCatalogEntry` y `deleteCatalogEntry`.
- [ ] F3. `types/index.ts`: reexports, `IntegranteRow`, y `CaseFormData` con `integrantes: IntegranteRow[]` en lugar de `integrantes_tribunal`.
- [ ] F4. `lib/catalogs.ts` (7.1), con `normalizeCatalogKey` cumpliendo los vectores de 3.5.
- [ ] F5. `lib/pericial.ts`: `joinIntegrantes` (vectores de 4.2), `integrantesFromCase` (4.3), `newIntegranteRow`, y ajustes a `EMPTY_CASE_FORM`, `prefillFromLastCase`, `caseToForm`, `formToCaseRequest`, `CASE_MESSAGES` y `REQUIREMENT_META`.
- [ ] F6. `hooks/useCatalogs.ts` (7.2).
- [ ] F7. `lib/prime/pt/autocomplete.ts`, registrado en `pt/index.ts`.
- [ ] F8. `FormField.tsx`: prop `labelAside`.
- [ ] F9. `components/form/CatalogAutoComplete.tsx` (7.3): texto libre, "Nuevo", acciones por ítem, pie "Administrar" y ARIA en el `<input>`.
- [ ] F10. `components/form/CatalogManageDialog.tsx` (7.4): buscar, editar en línea, borrar con confirmación en línea, toasts y error 409 en línea.
- [ ] F11. `components/form/IntegrantesField.tsx` (7.5): filas, subir y bajar, quitar, Enter en la última, vista previa, tope de 20 y manejo del foco.
- [ ] F12. `CaseFormStep.tsx`: `catalog` en `FieldDef`, etiqueta del destinatario (7.6), `IntegrantesField`, `useCatalogs`, aviso de error de carga y botón "Administrar" en `labelAside`.
- [ ] F13. `app/dashboard/page.tsx`: compila con el `CaseFormData` nuevo. "Teléfono celular" sigue siendo el default de un caso nuevo.
- [ ] F14. Verificación visual en escritorio y a 375 px: la lista no se sale de la pantalla, no hay scroll horizontal, los objetivos táctiles son de 44 px o más y el foco es visible en todo, en tema claro y oscuro.
- [ ] F15. `npx tsc --noEmit` (en `client/`) en verde. `npm run build` si el tiempo lo permite.
- [ ] F16. `progress/impl_frontend_formulario-caso-catalogos.md` con los archivos tocados, los skills invocados y sus hallazgos, y la verificación.

### 10.3 Tests xUnit (`server/tests/Factum.Backend.Tests/`)

No tocan Mongo ni `Storage` (son puros, como `EvidenceZipTests`).

- `CatalogTextTests.cs`: los vectores de 3.5 (`CleanValue` y `NormalizeKey`), más `"  teléfono celular "` ≡ `"Teléfono celular"` (escenario "No se generan duplicados") y la equivalencia de las tres variantes de "el Sr. Juan Pérez".
- `IntegrantesFormatterTests.cs`: los vectores de 4.2 (0, 1, 2 y 3 ítems; "e" con "Ignacio" e "Hilda"; "y" con "Hielo"); `Clean` descarta vacíos y espacios y conserva el orden.
- `CaseFieldsIntegrantesTests.cs`:
  - (a) `UpdateCaseRequest` con `Integrantes = ["Dr. Juan Pérez", " ", "Dra. María Gómez"]` → `CaseFields.Integrantes` con 2 ítems e `IntegrantesTribunal == "Dr. Juan Pérez y Dra. María Gómez"`, aunque el request traiga otro `IntegrantesTribunal`.
  - (b) `Integrantes = null` e `IntegrantesTribunal = "Dres. Juan Pérez y María Gómez"` → `Integrantes` null y texto tal cual.
  - (c) 21 ítems o un ítem de 301 caracteres → `ValidateCaseData` devuelve error.
  - (d) lista con un texto derivado de más de 500 caracteres → sin error de largo.
- `ReportValuesIntegrantesTests.cs` (con un `IReportSettings` falso y `new BrandingSnapshot(null, [], null)`):
  - (a) **caso viejo**: `Case { IntegrantesTribunal = "Dres. Juan Pérez y María Gómez", Integrantes = null }` → `{fraseIntegracion} == ", con la integración de Dres. Juan Pérez y María Gómez"`;
  - (b) caso nuevo con lista de 3 y su texto derivado → `", con la integración de Dr. Juan Pérez, Dra. María Gómez y Dr. Luis Díaz"`;
  - (c) sin integrantes (`""`, lista `[]`) → `""`.
- (Opcional) `CatalogDefinitionsTests.cs`: los cuatro ids existen y `Sources[Partes]` devuelve los tres campos en orden.

---

## 11. Verificación

| Quién | Comando |
|---|---|
| `implementer-backend` | `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` y `dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj`, más el humo de B19 |
| `implementer-frontend` | `cd client && npx tsc --noEmit` (y `npm run build` si da el tiempo) |
| orquestador | `./ops/harness/verify.sh` |

**Prueba manual para el usuario** (con backend, client y Tatana levantados):

1. Abrir el paso 2 de un caso nuevo. Las sugerencias de destinatario, partes, profesión y tipo de dispositivo traen los valores de los casos anteriores, y tipo de dispositivo trae "Teléfono celular".
2. Escribir "étic" o una parte de un destinatario con tilde: aparece la sugerencia, y al elegirla el campo queda con el texto exacto.
3. Escribir un destinatario nuevo: se ve "Nuevo: «…»". Crear el caso, volver a abrir el paso 2 (o empezar otro caso) y comprobar que aparece entre las sugerencias.
4. Guardar un caso con "  teléfono celular ": en tipos de dispositivo sigue habiendo una sola entrada.
5. Con el lápiz, corregir una parte. Con la papelera, quitar una profesión. Ver los toasts. El caso que usaba esos valores sigue mostrando el texto viejo en el paso 2.
6. Una parte guardada como "denunciada" aparece al escribir en "denunciante" y en "Nombre de quien propone".
7. Cargar tres integrantes, reordenarlos, dejar una fila vacía y guardar. Al volver al paso 2 están los tres en orden, sin la vacía. Generar el informe: "…con la integración de A, B y C…".
8. Abrir el caso cargado a mano antes de esta HU: se ve **una** fila "integrante 1 e integrante 2". Sin guardarlo, el documento en Mongo no cambió.
9. Con otro usuario (otro DNI), las sugerencias no muestran lo del primero.
10. Con el teclado solamente: abrir sugerencias (flecha abajo), elegir (Enter), cerrar (Escape) y abrir "Administrar" con el botón junto a la etiqueta. A 375 px de ancho, la lista y el diálogo entran en la pantalla.

## Resolución de decisiones (2026-10-01)

- **DP1 (D5) → A, confirmada por el usuario:** "e" en lugar de "y" cuando el último integrante empieza con sonido /i/ (sin tratamiento delante).
- D1–D15: las recomendadas, como están en el documento.
