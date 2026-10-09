# Implementación frontend — dashboard-breakdown (HU4)

**Rama:** `feat/frontend-dashboards` (misma rama que HU3, implementadas juntas en el
worktree aislado). **App:** `client/` únicamente. No se tocó `server/` ni `agent-ui/`.
**Backend:** ya en `develop` (`GET /api/cases/breakdown`, `BreakdownResponse`). Solo se consumió.

## Contrato compartido — verificado contra `develop`

- `server/src/Factum.Backend/DTOs/CaseBreakdownDtos.cs` → `BreakdownBucket(Key, Label,
  Count)`, `BreakdownResponse(Dimension, Total, Buckets)`.
- `Controllers/CasesController.cs` → `[HttpGet("breakdown")] Breakdown([FromQuery] string?
  dimension, [FromQuery] string? from, [FromQuery] string? to, …)`.

Casing JSON (snake_case_lower) usado tal cual en TS: `dimension`, `total`,
`buckets[].{key,label,count}`; query `dimension`/`from`/`to`. Dimensiones:
`platform | status | caratula | ambito_causa`. Labels (incluidos "Sin especificar" y
"Otras") los resuelve el backend; el cliente no los duplica. Coincide con la tabla
"Contrato compartido" de la SDD.

## Archivos tocados

- `client/package.json`: `recharts@^3.10.1` (compartido con HU3).
- `client/src/lib/api.ts`:
  - `type BreakdownDimension`, `interface BreakdownBucket`, `interface CaseBreakdown`
    con el casing exacto del contrato.
  - `api.caseBreakdown(dimension, from?, to?, signal?)`: arma la query con
    `URLSearchParams` (solo setea `from`/`to` si vienen), vía `requestSafe` con
    `AbortSignal`.
- `client/src/types/index.ts`: re-export de los tipos de breakdown.
- `client/src/components/dashboard/chart-theme.ts` (compartido con HU3): tokens `--fx-*`
  para Recharts.
- `client/src/components/dashboard/BreakdownBarsChart.tsx` (nuevo, `"use client"`):
  `BarChart layout="vertical"` (barras horizontales), eje Y = `label`, `<Cell>` por bucket.
  `status` usa tokens semánticos (`--fx-success` completados, `--fx-warning` generando/
  borradores, `--fx-danger` error); el resto, acento, con los buckets agregados
  (`__otras__`/`__sin_especificar__`) en tono atenuado. SVG `aria-hidden` (la tabla es la
  fuente textual). `isAnimationActive` atado a `useReducedMotion`.
- `client/src/components/dashboard/DashboardBreakdown.tsx` (nuevo, `"use client"`):
  administra su estado (dimensión + rango de fechas **propios**, D3-A). Selector de
  dimensión = botones con `aria-pressed` (activa marcada por color + peso + sombra, no solo
  color), `role="group"`. Calendar de rango propio (patrón `CaseHistory.tsx`: `selectionMode
  ="range"`, `toYMD`/`fromYMD`, `yyyy-MM-dd`) — no toca el filtro del historial. Fetch en
  `useEffect` keyed en `dimension`/`from`/`to`/`reloadKey` con `AbortController` (cancela el
  anterior; ignora `AbortError`). `aria-live="polite"` anuncia el cambio de desglose. Estados:
  carga (skeleton + `aria-busy`), vacío (`total === 0`: "Sin datos", sin gráfico ni división
  por cero), error (`FxBanner` + reintentar). Gráfico (refuerzo) + **tabla accesible**
  (`label → cantidad → %`, `caption`, `scope`), que es la representación textual que manda.
  Carga del gráfico con `dynamic ssr:false` + placeholder.
- `client/src/app/dashboard/page.tsx`: montaje de `<DashboardBreakdown />` debajo de los
  KPIs/tendencia, dentro del bloque condicionado a `stats.total > 0` (equivalente a
  `historyCases.length > 0`). **No** se tocó `CaseHistory.tsx` ni sus filtros (D3-A).

## Decisiones no obvias

- **Porcentaje en el cliente** (`Math.round(count/total*100)`) con guarda de división por
  cero; conteo/Top N/labels vienen del backend (fuente única). Con `total === 0` no se
  renderiza el gráfico.
- **Selector como botones toggle, no tabs:** la SDD permite `aria-pressed`; es más simple y
  accesible que un tablist falso para 4 opciones que no son paneles navegables.
- **Reintento sin cambiar filtros:** `reloadKey` fuerza el refetch del `useEffect`.

## Skills invocadas (obligatorias)

- `ui-ux-pro-max`: antes del JSX. Aplicado: barra + label + número + % siempre juntos
  (nunca solo color); tabla accesible como texto equivalente; tokens semánticos para
  `status`; sin scroll horizontal a 360 px (barras a ancho completo, tabla fluida).
- `senior-frontend`: `AbortController` para cancelar fetches stale; `next/dynamic ssr:false`
  para Recharts; estado local acotado, sin re-render innecesario.
- `3d-web-experience`: criterio — 2D, sin 3D. Sin hallazgos aplicables.
- `web-design-guidelines`: autochequeo (sin red para traer el command.md; reglas conocidas
  aplicadas). Verificado: `aria-pressed`, `aria-live`, `role="group"` + `aria-label`, label
  del Calendar, foco visible (`FOCUS_RING`), targets ≥44px, tabla semántica con `caption`/
  `scope`, error con `role="alert"` (tono error de `FxBanner`), reduced-motion. Sin
  hallazgos pendientes.

## Verificación

```
cd client && npx tsc --noEmit   → sin errores
cd client && npm run build        → Compiled successfully; /dashboard prerendered OK
```

**Nota de merge (avisada por el orquestador):** esta rama agrega `recharts` a
`package.json`; otra rama en paralelo agrega `diff`. Posible conflicto trivial de
`package.json`/`package-lock.json` — lo resuelve el orquestador.

**Estado:** done.
