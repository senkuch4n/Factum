# Review — formulario-caso-catalogos

**Veredicto:** APROBADA

Verificado por el reviewer: `dotnet build` Backend OK (0 errores; 4 warnings NU1902/NU1903 de paquetes ya existentes), `dotnet test` 48/48 verdes, `npx tsc --noEmit` en `client/` limpio, `npm run build` en `client/` OK.

## Checkpoints
- C1 (arnés, backlog/verify.sh): [x] fuera del alcance del diff de código; el orquestador corre verify.sh
- C2 contrato compartido: [x] `integrantes` (`string[] | null`) en `Models/Case.cs`, `DTOs/CaseDtos.cs` y `lib/api.ts` (`Case`, `CaseDataRequest`); `integrantes_tribunal` pasa a opcional en el request; `CatalogEntryDto` (`id`, `value`, `use_count`, `last_used_at`) y `CatalogsResponse.catalogs` coinciden con `CatalogEntry`/`CatalogsResponse` de `api.ts`; ids de catálogo en snake_case literal; rutas `GET /api/catalogs`, `PUT/DELETE /api/catalogs/{catalog}/entries/{id}` iguales; DELETE 204 manejado con `requestNoContent`.
- C3 arquitectura: [x] solo `server/` API y `client/`; Tatana y agent-ui sin tocar; modelos con `Integrantes` anulable sin default (docs viejos deserializan null) y `[BsonIgnoreExtraElements]`; sin debug logs ni datos sensibles (solo DNI en LogWarning, mismo criterio que el resto del repo).
- C4 verificación: [x] build, tests (CatalogText, IntegrantesFormatter, CaseFields, ReportValues, CatalogLogic: prueban lógica real), tsc y next build en verde. Informe DOCX: la salida real queda como prueba manual del usuario (el texto derivado se prueba con ReportValues).
- C5 cierre: [x] progress backend y frontend existen; el frontend deja constancia de ui-ux-pro-max, senior-frontend, 3d-web-experience ("sin hallazgos aplicables") y web-design-guidelines (hallazgos corregidos).

## Puntos de foco verificados
- Autorización: `CatalogsController` con `[Authorize]` y dueño = `HttpContext.Items["User"].Dni`; todas las operaciones del repo (`Owned`, `ListByOwner`, `FindByKey`, upsert, delete) filtran por `OwnerDni` y `Catalog`; ajena/inexistente = 404; `DeleteOne` (nunca `DeleteMany`).
- Índice único `owner_catalog_key_unique` (OwnerDni, Catalog, NormalizedKey) + `owner_lastused`; upsert unordered que tolera E11000; 409 en edición vía `MongoWriteException` DuplicateKey.
- Normalización: `CatalogText` (C#) y `normalizeCatalogKey` (TS) equivalentes (NFD, marcas, lower, `\s+`).
- Siembra: `ListCatalogSourcesAsync` es solo `Find` con proyección; escribe únicamente `catalog_entries`/`catalog_seeds`; marca después de los upserts; falla = warning sin romper el GET. Sin migración ni escritura sobre `cases` fuera del PUT del perito.
- Casos viejos: `Integrantes` null; `integrantesFromCase` muestra una fila con el texto; al guardar sin tocar, el derivado es idéntico.
- Frase: `IntegrantesFormatter.Join` y `joinIntegrantes` idénticas ("e" con i-/hi+no vocal; "Hielo" -> "y").
- Snapshot: el hook y el servicio nunca modifican casos ni el form; editar/borrar solo toca `catalog_entries`.
- ReportService/ReportValues: sigue leyendo `IntegrantesTribunal` (derivado en la misma escritura); solo comentario agregado.
- `RecordUsageAsync` nunca lanza; en edición solo registra campos cuya clave cambió (D12).

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- `CleanValue` colapsa espacios internos de cada integrante; un caso viejo guardado sin tocar puede normalizar espacios dobles del texto (efecto cosmético, esperado por la SDD).
- B19 (humo con Mongo real) y verificación visual F14 (375 px, temas) dependen de la prueba manual del usuario; no se pudieron reverificar acá.
- Warnings NU1902/NU1903 son preexistentes (paquetes), no del diff.
