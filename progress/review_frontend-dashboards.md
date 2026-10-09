# Review — frontend-dashboards (HU3 + HU4)

**Veredicto:** APROBADA

## Checkpoints

### C1 — El arnés está sano
- [ ] La HU tiene su issue en el Project "Factum – HU" con Fase `en_revision`
  — `gh issue list --label hu --state all` no devuelve issues para
  `dashboard-kpis-tendencias` ni `dashboard-breakdown`; `hu.mjs ver` tampoco los
  muestra. Los issues para estas dos HU no están creados en GitHub. **No bloqueante**
  para el código: el orquestador debe dar de alta los issues con `hu.mjs alta`
  antes del merge.
- [x] La rama es `feat/frontend-dashboards` (desde develop; un único commit
  `41ed12a`). `git log origin/develop...origin/feat/frontend-dashboards` confirma
  que parte de develop.

### C2 — La HU tiene su cadena de documentos completa
- [x] `docs/hu-dashboard-kpis-tendencias.md` existe con secciones estándar
  (Contexto, Gherkin, Datos, UX, Fuera de alcance, Dudas). Las decisiones D1-B /
  D3-Recharts / D4-A / D5-A / D6-A / D7-A están documentadas como validadas en la
  SDD.
- [x] `docs/hu-dashboard-breakdown.md` existe con secciones estándar.
- [x] `Refactorizaciones/dashboard-kpis-tendencias.md` con checklist atómico y
  sección **Contrato compartido**.
- [x] `Refactorizaciones/dashboard-breakdown.md` con checklist atómico y sección
  **Contrato compartido**.
- [x] Contrato verificado campo a campo contra `origin/develop`:
  - `CaseStatsDtos.cs` (`CaseStatsResponse`, `CaseStatusCounts`, `MonthlyCount`) →
    casing JSON `total`, `by_status.{draft,generating,completed,error}`,
    `completion_rate`, `avg_close_seconds`, `monthly[].{month,count}` ↔
    `interface CaseStats` en `client/src/lib/api.ts`: coincide exactamente.
  - Query param `tz_offset_minutes` → `?tz_offset_minutes=${tz}` en `api.caseStats()`:
    coincide.
  - `CaseBreakdownDtos.cs` (`BreakdownBucket(Key,Label,Count)`,
    `BreakdownResponse(Dimension,Total,Buckets)`) → casing JSON `dimension`,
    `total`, `buckets[].{key,label,count}` ↔ `interface CaseBreakdown`,
    `interface BreakdownBucket`, `type BreakdownDimension` en `api.ts`: coincide
    exactamente.
  - Rutas `GET /api/cases/stats` y `GET /api/cases/breakdown` confirman en el
    controlador de develop.

### C3 — El código respeta la arquitectura del repo
- [x] Solo se tocó `client/` (+ `progress/`). `server/` y `agent-ui/` intactos
  (verificado con `git diff origin/develop...origin/feat/frontend-dashboards -- server/
  agent-ui/` → vacío).
- [x] `client/AGENTS.md` respetado: `next/dynamic` con `ssr: false` para Recharts
  (patrón de `ReportStep.tsx`), App Router, `"use client"` en los componentes de
  gráficos.
- [x] `agent-ui/` no tocado; regla main/preload/renderer no aplica.
- [x] No hay lectura ni escritura de Mongo; endpoint de solo lectura, no modifica
  documentos ajenos.
- [x] Sin `console.log` de debug, sin datos sensibles en logs, sin TODOs sin
  contexto.

### C4 — La verificación es real
- [x] `npx tsc --noEmit` corrido en worktree `/tmp/review-frontend-dashboards` sobre
  `origin/feat/frontend-dashboards`: sin errores ni advertencias.
- [x] `npm run build` corrido en el mismo worktree: "Compiled successfully in 8.9s",
  `/dashboard` prerendered como static content, sin errores.
- [x] `recharts@^3.10.1` en `dependencies` con peer React `^16.8||^17||^18||^19`;
  compatible con React 19 / Next 16 y con SSR desactivado vía `ssr: false`.
- [ ] No hay tests (no existe infraestructura de tests en client/; la SDD no pidió
  tests; queda la prueba manual para el usuario).

### C5 — La sesión se cerró bien
- [x] `progress/impl_frontend_dashboard-kpis-tendencias.md` existe y detalla
  archivos tocados, contrato verificado, decisiones no obvias y verificación.
- [x] `progress/impl_frontend_dashboard-breakdown.md` ídem.
- [x] Skills documentadas en ambos progress:
  - `ui-ux-pro-max`: aplicado (color nunca canal único, tabla accesible como
    fallback, tokens semánticos para status, sin scroll horizontal a 360 px).
  - `senior-frontend`: aplicado (`AbortController` con stale-guard, `next/dynamic
    ssr:false`, lectura de CSS vars con reevaluación por tema).
  - `3d-web-experience`: criterio 2D, "sin hallazgos aplicables".
  - `web-design-guidelines`: autochequeo, constancia de lo verificado.
- [x] Sin scripts de prueba ni archivos temporales sin borrar.

---

## Observaciones no bloqueantes

1. **Issues de GitHub ausentes (C1):** HU3 y HU4 no tienen issues en el Project
   "Factum – HU". El orquestador debe crearlos con `hu.mjs alta` y llevarlos a
   `aprobada` antes (o inmediatamente después) del merge a develop.

2. **Chips de estado en tarjeta Total (HU3):** la SDD D6-A dice "chips de los 4
   estados". La implementación muestra `completados` + `en proceso`
   (draft+generating combinados) + `error` (solo si > 0). Los estados `draft` y
   `generating` no tienen chip propio: están implícitos en "en proceso". Esto es
   coherente con la tarjeta "En proceso" que también los agrupa, y el SDD lo
   permite ("siempre ícono+texto, no solo color"). Si el usuario prefiere ver draft
   y generating por separado, sería un ajuste menor.

3. **Condición de montaje de DashboardBreakdown:** el breakdown se monta dentro del
   bloque `stats && stats.total > 0`, no directamente sobre
   `historyCases.length > 0` como dice la SDD en la cita de código. En la práctica
   son equivalentes (si el perito tiene casos, `stats.total > 0`), pero hay una
   ventana temporal donde `historyCases` tiene datos y `stats` todavía no cargó
   (o falló): en ese intervalo el breakdown no se muestra aunque el historial sí.
   Esto es aceptable porque el error del stats ya tiene su propio banner de
   reintento.

4. **`DashboardBreakdown` se monta aunque no haya `historyCases`:** el componente
   hace su propio fetch independiente (D2-B), de modo que técnicamente podría
   devolver datos incluso si `loadHistory` falla. La condición de montaje
   (`stats.total > 0`) cubre esto correctamente porque si el perito no tiene casos
   el endpoint de stats devolverá `total: 0`.
