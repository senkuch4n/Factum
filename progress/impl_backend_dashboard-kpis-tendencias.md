# impl_backend — dashboard-kpis-tendencias

Rama: `feat/backend-analitica-forense` (ola backend HU3-6, implementadas en secuencia sobre
una sola rama por compartir `CasesController`/`CaseService`).

## Alcance (solo backend API)

Endpoint nuevo `GET /api/cases/stats` (KPIs + tendencia mensual), agregación en memoria sobre
`ListByOfficerAsync`, filtrado por `Officer.Dni`. Sin Mongo nuevo, sin índices, solo lectura.

## Archivos tocados

- `server/src/Factum.Backend/DTOs/CaseStatsDtos.cs` (nuevo): `CaseStatsResponse`,
  `CaseStatusCounts`, `MonthlyCount`.
- `server/src/Factum.Backend/Services/Cases/CaseService.cs`: interfaz `ICaseService.GetStatsAsync`.
- `server/src/Factum.Backend/Services/Cases/CaseAnalytics.cs` (nuevo, partial de `CaseService`):
  implementación de `GetStatsAsync` (comparte archivo con `BreakdownAsync` de la HU hermana,
  como prevé la SDD — ambos reusan `ListByOfficerAsync`).
- `server/src/Factum.Backend/Controllers/CasesController.cs`: action `Stats` con
  `[HttpGet("stats")]`, colocado ANTES de `Get({id})` para que el literal `stats` gane sobre el
  parámetro `{id}`.

## Contrato compartido (verificado contra la SDD)

JSON snake_case_lower (`JsonNamingPolicy.SnakeCaseLower`, `Program.cs`). Respuesta 200:
`total` (int), `by_status` { `draft`, `generating`, `completed`, `error` } (objeto plano de 4
claves fijas, siempre presentes), `completion_rate` (double 0..1; 0 si total==0),
`avg_close_seconds` (double|null; null si no hay completados con generated_at),
`monthly` [ { `month` "yyyy-MM", `count` int } ] (exactamente 12 meses ascendentes, meses sin
casos en 0). Query `tz_offset_minutes` (int, default 0).

## Decisiones no obvias

- `by_status` es un record de 4 propiedades (no un diccionario por enum) → contrato estable aunque
  cambie el orden de `CaseStatus`.
- Agrupación mensual en hora local del perito: `CreatedAt.AddMinutes(tzOffsetMinutes)` y rango de
  12 meses sobre `DateTime.UtcNow.AddMinutes(tzOffsetMinutes)`. `avg_close_seconds` NO se ajusta
  por huso (es una diferencia entre dos instantes UTC).
- `completion_rate` con guarda de división por cero; `avg_close_seconds` null (nunca 0/NaN) si la
  lista filtrada queda vacía (los completados legacy sin `generated_at` se excluyen del promedio).
- Casos fuera del rango de 12 meses se descartan del `monthly` (no se mapean a ningún mes).

## Regla dura de datos

Endpoint 100% lectura: no inserta/actualiza/borra documentos ni crea índices.

## Verificación

`dotnet build server/src/Factum.Backend/Factum.Backend.csproj` → 0 errores, 4 advertencias NuGet
preexistentes (NU1902/NU1903 de SharpCompress/Snappier), sin warnings CS nuevos.

Prueba manual (queda para el usuario): confirmar que `GET /api/cases/stats?tz_offset_minutes=…`
responde 200 con el JSON del contrato y que un caso ajeno no aparece en los conteos.
