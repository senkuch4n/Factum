# impl_backend — dashboard-breakdown

Rama: `feat/backend-analitica-forense` (ola backend HU3-6, misma rama que la HU hermana).

## Alcance (solo backend API)

Endpoint nuevo `GET /api/cases/breakdown?dimension=…&from=…&to=…` en el MISMO
`CasesController`/`CaseService` que `stats`. Agregación en memoria sobre `ListByOfficerAsync`,
filtrado por `Officer.Dni`. Sin Mongo nuevo, sin índices, solo lectura.

## Archivos tocados

- `server/src/Factum.Backend/DTOs/CaseBreakdownDtos.cs` (nuevo): `BreakdownBucket`,
  `BreakdownResponse` (en archivo propio, distinto de `CaseStatsDtos.cs`, como pide la SDD de la
  HU hermana para no competir por el mismo archivo).
- `server/src/Factum.Backend/Services/Cases/CaseService.cs`: interfaz `ICaseService.BreakdownAsync`
  (nombre distinto de `GetStatsAsync`, mismo servicio).
- `server/src/Factum.Backend/Services/Cases/CaseAnalytics.cs`: implementación `BreakdownAsync` +
  helpers `FixedBuckets` (platform/status) y `FreeTextBuckets` (caratula/ambito_causa).
- `server/src/Factum.Backend/Controllers/CasesController.cs`: action `Breakdown` con
  `[HttpGet("breakdown")]` (literal, gana sobre `{id}`) + helper `TryParseDate` (yyyy-MM-dd, UTC).

## Contrato compartido (verificado contra la SDD)

JSON snake_case_lower. Request: `dimension` (obligatorio: `platform|status|caratula|ambito_causa`;
fuera del set → 400), `from`/`to` (opcionales, `yyyy-MM-dd`; fecha inválida → 400; `to<from` → 400).
Respuesta 200: `{ dimension, total, buckets: [ { key, label, count } ] }`.

Labels resueltos por el backend: platform `android`→Android, `ios`→iOS; status `draft`→Borradores,
`generating`→Generando, `completed`→Completados, `error`→Con error; caratula/ambito_causa el propio
texto, `__sin_especificar__`→Sin especificar, `__otras__`→Otras.

## Decisiones no obvias

- **platform/status:** buckets fijos SIEMPRE presentes (incluso count 0), en el orden del
  catálogo (no por count) para que la UI no salte. `total` = suma de todos los buckets (los 0 no
  suman) = total filtrado.
- **caratula/ambito_causa:** vacío/whitespace → bucket `__sin_especificar__`. Top N = 8 sobre las
  categorías reales; el excedente se acumula en `__otras__`. "Sin especificar" NO compite por el
  Top N ni cae en "Otras" (D5a): sale siempre como bucket propio si count>0, reinsertado y ordenado
  por su count junto al resto. "Otras" queda al final.
- **Filtro de fechas (D5b):** UTC, borde superior inclusivo por día:
  `from 00:00:00Z <= created_at < (to + 1 día) 00:00:00Z`.
- Validación de `dimension` y `to<from` dentro del servicio (`Result.Invalid` →
  `ErrorKind.Validation` → 400); el parseo de las fechas lo hace el controller con `TryParseExact`
  y devuelve 400 si vino y no parsea.

## Coordinación con la HU hermana

Ambas conviven en `CasesController`/`CaseService`/`CaseAnalytics.cs` sin pisarse: métodos
`GetStatsAsync`/`BreakdownAsync`, rutas `stats`/`breakdown`, DTOs en archivos separados. Las dos
reusan `_repo.ListByOfficerAsync` como única fuente; ninguna agrega pipelines de Mongo ni índices.

## Regla dura de datos

Endpoint 100% lectura; no escribe ni crea índices.

## Verificación

`dotnet build server/src/Factum.Backend/Factum.Backend.csproj` → 0 errores, 4 advertencias NuGet
preexistentes, sin warnings CS nuevos.
