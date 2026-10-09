# Implementación frontend — dashboard-kpis-tendencias (HU3)

**Rama:** `feat/frontend-dashboards` (desde `develop` actualizado, worktree aislado).
**App:** `client/` únicamente. No se tocó `server/` ni `agent-ui/`.
**Backend:** ya en `develop` (`GET /api/cases/stats`, `CaseStatsResponse`). Solo se consumió.

## Contrato compartido — verificado contra `develop`

Leídos antes de escribir el cliente:
- `server/src/Factum.Backend/DTOs/CaseStatsDtos.cs` → `CaseStatsResponse(Total, ByStatus,
  CompletionRate, AvgCloseSeconds, Monthly)`, `CaseStatusCounts(Draft, Generating,
  Completed, Error)`, `MonthlyCount(Month, Count)`.
- `Controllers/CasesController.cs` → `[HttpGet("stats")] Stats([FromQuery(Name =
  "tz_offset_minutes")] int tzOffsetMinutes, …)`.

Casing JSON (snake_case_lower) usado tal cual en TS: `total`, `by_status.{draft,
generating,completed,error}`, `completion_rate`, `avg_close_seconds`, `monthly[].{month,
count}`, query `tz_offset_minutes`. Coincide con la tabla "Contrato compartido" de la SDD.

## Archivos tocados

- `client/package.json` + `package-lock.json`: `recharts@^3.10.1` en `dependencies`
  (peer React `^16.8||^17||^18||^19` → compatible con React 19 / Next 16; build y tsc OK).
- `client/src/lib/api.ts`:
  - `interface CaseStats` con el casing exacto del contrato.
  - `api.caseStats(signal?)`: manda `tz_offset_minutes = -new Date().getTimezoneOffset()`
    vía `requestSafe` (un corte de red llega como `ApiError` status 0, no como
    "Failed to fetch"). Acepta `AbortSignal`.
  - `requestSafe` ahora **re-lanza** un `AbortError` en vez de convertirlo en
    `SERVER_UNREACHABLE` (necesario para cancelar sin falso error; inocuo para los
    llamadores existentes que no pasan `signal`).
- `client/src/types/index.ts`: re-export de `CaseStats` (+ tipos de breakdown).
- `client/src/components/dashboard/chart-theme.ts` (nuevo): `useChartTokens()` lee los
  tokens `--fx-*` con `getComputedStyle` y los reevalúa al cambiar el tema (`useTheme().isDark`),
  porque Recharts no lee clases de Tailwind.
- `client/src/components/dashboard/CasesByMonthChart.tsx` (nuevo, `"use client"`):
  `ResponsiveContainer > BarChart` vertical, una serie, color `--fx-accent`. Eje X en
  meses abreviados es-AR (`Intl.DateTimeFormat`, con año de 2 díg. cuando cambia), eje Y
  entero. Tooltip accesible con tokens. Alternativa textual = tabla `sr-only` mes→cantidad
  + `figcaption` con resumen; el SVG va `aria-hidden`. Scroll horizontal (`min-w-[32rem]`)
  para que 12 barras sean legibles a 360 px (D4-A). `isAnimationActive`/duración atados a
  `useReducedMotion`.
- `client/src/components/dashboard/DashboardStats.tsx` (reescrito): recibe
  `stats: CaseStats | null` + `loading` en vez de `cases`. 4 tarjetas (D6-A): Total
  (destacada, chips por estado con punto de color + número + label sr-only, nunca solo
  color), Completados (anillo `ProgressRing` con `completion_rate*100`), En proceso
  (`draft+generating`), Tiempo de cierre (`avg_close_seconds/86400` → días 1 decimal es-AR
  con coma, "—" si `null`, D7-A). Esqueleto de carga con `aria-busy`. Sin completados: tasa
  0% y "—" sin "NaN".
- `client/src/app/dashboard/page.tsx`: `dynamic(() => …CasesByMonthChart, { ssr:false,
  loading: placeholder })` (patrón de `ReportStep.tsx`). Estado local `stats`/`statsLoading`/
  `statsError` + `loadStats()` (fetch propio, D1-B); `refreshHistory()` recarga historial +
  stats juntos (se usa en `onRefresh` de `CaseHistory` y al salir del wizard). Bloque nuevo:
  banner de error con reintento (el historial sigue andando si falla el stats) · esqueleto
  mientras carga · KPIs + gráfico de tendencia cuando `stats.total > 0`. Estado vacío
  (`total === 0`): no se muestran KPIs en 0 — el mensaje de bienvenida lo da `CaseHistory`.

## Decisiones no obvias

- **Cancelación sin falso error (senior-frontend):** en vez de dejar que `requestSafe`
  trague el abort como corte de red, se propaga el `AbortError`; el componente lo ignora.
- **Tokens para Recharts (senior-frontend):** hook `useChartTokens` keyed en `isDark`,
  no hex crudo; mantiene contraste AA en claro/oscuro.
- **Alternativa textual como fuente de verdad del lector (ui-ux-pro-max / charts):** el SVG
  es `aria-hidden` y la tabla `sr-only` expone todo; evita doble lectura.
- **Días con 1 decimal es-AR:** `toLocaleString("es-AR", { min/maxFractionDigits: 1 })`
  (coma decimal); singular "día" cuando el valor es "1,0".

## Skills invocadas (obligatorias)

- `ui-ux-pro-max`: antes del JSX. Consultado `--domain chart` y `--domain ux`. Aplicado:
  color siempre acompañado de número + label; tabla accesible como fallback; anillo (bullet/
  gauge) para KPI-vs-target; tabla con `overflow-x-auto` para no romper a 360 px.
- `senior-frontend`: `next/dynamic ssr:false` para sacar Recharts del bundle inicial;
  `AbortController`/stale-guard; lectura de CSS vars con reevaluación por tema.
- `3d-web-experience`: criterio — dashboard 2D, sin 3D ni pseudo-3D (el propio anti-patrón
  "3D for 3D's sake" lo confirma). Sin hallazgos aplicables.
- `web-design-guidelines`: autochequeo (sin red para traer el command.md; aplicadas las
  reglas conocidas). Verificado: `type="button"`, targets ≥44px (`min-h-11`), foco visible
  (`FOCUS_RING`), `aria-busy` en cargas, reduced-motion, color no es canal único, alturas
  reservadas (sin CLS). Sin hallazgos pendientes.

## Verificación

```
cd client && npx tsc --noEmit   → sin errores
cd client && npm run build        → Compiled successfully; /dashboard prerendered OK
```

(El `npm run build` se corrió en el worktree aislado, no en el checkout del `npm run dev`
del usuario. Se restauró `client/next-env.d.ts`, que el build tocó por el cambio dev→prod
de rutas, para dejar el commit acotado a la feature.)

**Estado:** done.
