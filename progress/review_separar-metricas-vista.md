# Review — separar-metricas-vista

**Veredicto:** APROBADA

## Checkpoints

### C1 — El arnés está sano
- [x] Rama `feat/separar-metricas-vista` sale exactamente del HEAD de `origin/develop` (SHA `3a8ea02`). `./ops/harness/verify.sh` desde el worktree termina con exit code 0.
- [ ] `hu.mjs ver` no devuelve un issue con slug `separar-metricas-vista` en Fase `en_revision` — el issue en el Project no aparece en la lista pública de `gh issue list --label hu`. El orquestador lo pasó a revisión directamente. Esto no es bloqueante en el código; se anota para que el orquestador confirme el estado del issue.

### C2 — La HU tiene su cadena de documentos completa
- [x] `docs/hu-separar-metricas-vista.md` existe con Contexto, Gherkin, Datos, Diseño UX/UI, Fuera de alcance y Dudas validadas.
- [x] `Refactorizaciones/separar-metricas-vista.md` existe con checklist atómico completo. La SDD documenta explícitamente que no hay Contrato compartido (solo `client/`, reusa tipos ya sincronizados).
- [x] No hay contrato que verificar: la HU no cruza a `server/` ni a `agent-ui/`. Los tipos `CaseStats`/`CaseBreakdown` preexisten en `client/src/types/` y no se modificaron.

### C3 — El código respeta la arquitectura del repo
- [x] Solo se tocó `client/` (4 archivos + 1 progress). `server/` y `agent-ui/` intactos — verificado con `git diff origin/develop...origin/feat/separar-metricas-vista -- server/ agent-ui/` (output vacío).
- [x] `client/AGENTS.md` respetado: patrón App Router, server component mínimo + client screen con guarda `useAuth`, `next/dynamic` con `ssr: false` para Recharts.
- [x] `agent-ui/` no tocado.
- [x] No hay modelos de Mongo (la HU es solo `client/`).
- [x] No hay `console.log` de debug ni TODOs sueltos en los archivos nuevos o modificados.

### C4 — La verificación es real
- [x] `npx tsc --noEmit` en `client/` (worktree `agent-a0109f2a635103e93`): sin errores — verificado en esta revisión.
- [x] `npm run build` en `client/` (worktree): compilación OK, ruta `/metricas` generada como estática junto a `/dashboard`, `/admin/cuentas` y las demás. Sin errores de TS ni de prerender.
- [x] La SDD no pidió tests (no hay infraestructura de tests en `client/`).
- [x] No hay generación de PDF/ZIP en esta HU.

### C5 — La sesión se cerró bien
- [x] `progress/impl_frontend_separar-metricas-vista.md` existe y describe todos los archivos tocados, la verificación y las decisiones no obvias.
- [x] El progress deja constancia de los cinco skills: `ui-ux-pro-max` (estado activo por color + peso, `aria-current`), `senior-frontend` (patrón App Router correcto), `3d-web-experience` ("sin hallazgos aplicables"), `web-design-guidelines` (autochequeo ítem por ítem), `mblode-agent-skills-ui-animation` ("sin hallazgos aplicables"), `ui-styling` (tokens `--fx-*` sin hex crudo).
- [x] No hay scripts de prueba ni archivos temporales; no se tocaron datos de desarrollo.

## Verificación puntual de los requisitos del orquestador

**1. `/dashboard` ya NO muestra analítica:**
- Imports `DashboardStats`, `DashboardBreakdown`, `dynamic`, `TrendingUp`, `type CaseStats` quitados de `dashboard/page.tsx`. Verificado con `grep` en el diff: no quedan referencias.
- Bloque de estado `stats`/`statsLoading`/`statsError`/`loadStats` + su `useEffect` eliminados (líneas ~155-186 del original).
- `refreshHistory` simplificado a solo `loadHistory` en línea 161 del archivo final; dos usos conservados (`resetWizard` y `onRefresh={refreshHistory}` de `CaseHistory`).
- JSX de analítica del modo historial (banner error, `DashboardStats`, sección "Casos creados por mes", `DashboardBreakdown`) quitado. `GreetingHeadline`, "Nueva inspección", borradores y `CaseHistory` conservados.

**2. `/metricas` nueva:**
- `client/src/app/metricas/page.tsx`: server component, `metadata.title = "Métricas · Factum"`, renderiza `<MetricsScreen />`.
- `MetricsScreen.tsx`: guarda `useAuth({ withHistory: false })`, navbar con `sections={{ activeSection: "metricas" }}`, `showThemeSwitch`, `brandHref="/dashboard"`, `maxWidthClass="max-w-5xl"`.
- Estados: error (`FxBanner tone="error"` + botón "Reintentar" `min-h-11`), vacío (`stats.total === 0 && !statsLoading && !statsError` → fx-card punteada + ícono + botón "Crear la primera inspección" → `/dashboard`), cargando/con datos (`aria-busy={statsLoading}` en el contenedor de analítica, `DashboardStats stats={stats} loading={statsLoading}`, sección tendencia y `DashboardBreakdown` condicionados a `hasCases`).
- `CasesByMonthChart = dynamic(...)` declarado en `MetricsScreen.tsx` con `ssr: false` y placeholder `h-64`.

**3. Pestañas de sección en el centro del navbar:**
- `AppNavbar` tiene prop `sections?: { activeSection: "inspecciones" | "metricas" }` con comentario.
- `SectionTabs`: `<nav aria-label="Secciones">` con dos `<Link>` — "Inspecciones" → `/dashboard`, "Métricas" → `/metricas`. La activa lleva `aria-current="page"` + `bg-fx-surface-3 font-semibold text-fx-text`; la inactiva `font-medium text-fx-text-3`. Distinción por color Y peso. `fx-focus-ring` en cada ítem. Labels con `truncate`. Grupo centrado con `justify-center` solo cuando `sections` está definido.
- Dashboard modo historial: `sections={{ activeSection: "inspecciones" }}`. Modo wizard: `sections={undefined}` → el centro sigue siendo el breadcrumb del paso (la flecha "volver" y el breadcrumb del wizard quedan intactos, verificado en líneas 707-746 del dashboard final).
- Admin (`/admin/cuentas`): no pasa `sections` → navbar idéntico a antes.

**4. Componentes de analítica no rediseñados:**
- `DashboardStats`, `CasesByMonthChart`, `DashboardBreakdown`, `BreakdownBarsChart`, `chart-theme.ts` no aparecen en el diff. Confirmado con `git diff --stat`.

**5. `npx tsc --noEmit` y `npm run build` en `client/`:**
- Ambos pasan sin errores (verificado en esta revisión sobre el worktree de la rama).
- `server/` y `agent-ui/` intactos.
- Constancia de skills en el progress: presente y completa.

## Observaciones no bloqueantes

- El issue de esta HU no aparece en `gh issue list --label hu --state all`, lo que impide verificar la Fase `en_revision` en el Project con `hu.mjs ver`. El orquestador debería confirmar que el issue existe y está en la columna correcta antes de cerrar.
- El `center` del dashboard en modo historial (el breadcrumb "●Inspecciones") queda en el código pero es inalcanzable cuando `sections` está definido (modo historial). No es un bug — la SDD lo documenta explícitamente como decisión intencionada para no tocar el bloque del wizard.
- `aria-busy` va en el `<div className="space-y-6">` interior, no en el `<main>`. La SDD dice "el contenedor"; el div interno es el contenedor correcto de los datos de analítica. No es un defecto.
