# Implementación frontend — separar-metricas-vista

**Rama:** `feat/separar-metricas-vista` (desde `origin/develop` @ 3a8ea02, PR #39 ya mergeado).
**Apps:** solo `client/` (Next.js 16). No se tocó `server/` ni `agent-ui/`.
**SDD:** `Refactorizaciones/separar-metricas-vista.md`. HU: `docs/hu-separar-metricas-vista.md`.

## Qué se hizo (resumen)

Se **movió** (no se duplicó) toda la analítica del dashboard a una vista nueva
`/metricas` (sección paralela, patrón `/admin/cuentas`), y se agregó un selector
de secciones "Inspecciones" | "Métricas" en el centro de `AppNavbar`.

## Archivos tocados

### Nuevos
- `client/src/app/metricas/page.tsx` — server component mínimo, `metadata`
  `title: "Métricas · Factum"`, renderiza `<MetricsScreen />`.
- `client/src/components/dashboard/MetricsScreen.tsx` — pantalla cliente:
  - Guarda de sesión `useAuth({ withHistory: false })` (redirige a `/` sin token).
  - Monta su `AppNavbar` con `sections={{ activeSection: "metricas" }}`,
    `showThemeSwitch`, `brandHref="/dashboard"`, `maxWidthClass="max-w-5xl"`.
  - Dueña del estado `stats`/`statsLoading`/`statsError` + `loadStats` (movido
    desde `dashboard/page.tsx`) y del `useEffect` que dispara `loadStats` con `user`.
  - Trae la declaración `const CasesByMonthChart = dynamic(...)` (`ssr: false`,
    placeholder `h-64`) y el import `dynamic` de `next/dynamic`.
  - Render: `<h1>Métricas</h1>` + subtítulo "Tus inspecciones en números"; árbol
    de estados error / cargando-con-datos / vacío, con `aria-busy={statsLoading}`.
  - `ScreenSkeleton` local (patrón admin) mientras `!user`: título + 4 tarjetas +
    bloque de gráfico.
  - Entrada `motion-safe:animate-[fx-fade-in_...]` en el `<main>`.
  - Estado vacío propio (D4): `fx-card` punteada con ícono `BarChart3`, título
    "Todavía no hay nada para medir", texto breve y botón
    "Crear la primera inspección" → `router.push("/dashboard")` (`min-h-11`).

### Modificados
- `client/src/components/shell/AppNavbar.tsx`
  - Nueva prop opcional `sections?: { activeSection: "inspecciones" | "metricas" }`
    con comentario (prioridad sobre `center`; sin la prop, el navbar queda idéntico).
  - Componente interno `SectionTabs`: `<nav aria-label="Secciones">` con dos
    `<Link>` pill ("Inspecciones"→`/dashboard`, "Métricas"→`/metricas`). La activa
    lleva `aria-current="page"` + `bg-fx-surface-3 font-semibold text-fx-text`; la
    inactiva `text-fx-text-3 font-medium hover:text-fx-text`. Distinción por color
    Y peso. `fx-focus-ring`, `min-h-9`, labels con `truncate`, grupo centrado.
  - En la zona central: `sections ? <SectionTabs/> : center`. Se agrega
    `justify-center` al contenedor del centro **solo cuando hay `sections`**, para
    no alterar el breadcrumb de admin/wizard.
- `client/src/app/dashboard/page.tsx` (limpieza, mover no duplicar)
  - Quitados imports `DashboardStats`, `DashboardBreakdown`, `dynamic`,
    `TrendingUp`, `type CaseStats`.
  - Quitada la declaración `const CasesByMonthChart = dynamic(...)`.
  - Quitado el bloque de estado analítico (`stats`/`statsLoading`/`statsError`/
    `loadStats` + su `useEffect`).
  - `refreshHistory` simplificado a solo `loadHistory` (se mantuvo el nombre;
    hay dos usos: en `resetWizard` y `onRefresh={refreshHistory}` de `CaseHistory`).
  - Quitado el JSX de analítica del modo historial (banner error+reintento,
    `DashboardStats`, sección "Casos creados por mes" y `DashboardBreakdown`).
    `GreetingHeadline`, "Nueva inspección", "Pendientes de retomar", `statusMsg`
    y `CaseHistory` se conservan.
  - `AppNavbar` del dashboard: `sections={mode === "history" ? { activeSection:
    "inspecciones" } : undefined}`. En wizard `sections` es undefined → el centro
    sigue siendo el breadcrumb del paso (flecha "volver" con confirmación intacta).

## Contrato compartido

No aplica: no cruza al backend. `/metricas` reusa `api.caseStats()` y
`api.caseBreakdown()` (vía `DashboardBreakdown`), sin cambios de endpoints ni de
campos. Tipos `CaseStats`/`CaseBreakdown` ya sincronizados en `client/src/types/`.

## Verificación

- `cd client && npx tsc --noEmit` → sin errores. (Hubo que `npm ci` primero: el
  worktree aislado no traía `node_modules`.)
- `cd client && npm run build` → OK. Compiló y generó `/metricas` como ruta
  estática junto a `/dashboard`, `/admin/cuentas`, etc. Sin errores de TS ni de
  prerender.
- No se corrió `next build` en el checkout principal (se usó el `.next` del
  worktree aislado). No se corrió `dotnet build` (sin cambios de backend).

## Skills (constancia)

- `ui-ux-pro-max`: invocado antes del JSX final. Búsqueda UX de navegación/estado
  activo confirmó: indicar sección actual por color + estilo (no solo color),
  URLs que reflejan el estado (dos rutas reales), nombres accesibles. Aplicado:
  pills con `aria-current` + peso + fondo; labels de texto (no icon-only).
- `senior-frontend`: patrón Next 16 App Router — page server + client screen con
  guarda (igual a admin), estado de fetch movido con `useCallback`/`useEffect`,
  `next/dynamic` `ssr:false` para Recharts, prop reutilizable en `AppNavbar` sin
  romper usos existentes (default deja el navbar igual).
- `3d-web-experience` (criterio): no se agregó 3D ni efectos pseudo-3D (la HU no
  lo pide). Sin hallazgos aplicables.
- `web-design-guidelines` (autochequeo): la fuente remota de reglas no era
  accesible por WebFetch en este entorno; se aplicó el checklist de memoria sobre
  los dos archivos nuevos. Hallazgos: ninguno bloqueante. Verificado: focus ring
  visible (no removido), estado activo no solo-color + `aria-current`, `<Link>`
  operables con teclado, nombres accesibles en español, `<h1>` único, `aria-busy`
  en carga, botones táctiles `min-h-11`, icono decorativo `aria-hidden`, entrada
  con `motion-safe:` (respeta `prefers-reduced-motion`), sin scroll horizontal a
  360px (labels cortos + `truncate` en centro `min-w-0 flex-1`).
- `mblode-agent-skills-ui-animation`: no se agregaron transiciones/gestos nuevos;
  se reusó tal cual el patrón `fx-fade-in` del dashboard y `transition-colors` en
  los pills (feedback de hover con propósito, respeta reduced-motion). Sin
  hallazgos aplicables.
- `ui-styling` (Tailwind): clases con tokens `--fx-*` existentes
  (`rounded-fx-pill`, `fx-focus-ring`, `bg-fx-surface-3`, `border-fx-border-strong`,
  `fx-card`), sin hex crudo. Sin hallazgos aplicables más allá de lo anterior.

## Decisiones no obvias

- Se mantuvo el nombre `refreshHistory` (ahora solo `loadHistory`) en vez de
  reemplazar los dos usos, como permite la SDD: menos diff y el nombre sigue
  siendo correcto desde el punto de vista del dashboard.
- El `center` (breadcrumb) del dashboard se conserva en el código; en modo
  historial `sections` lo tapa (prioridad) y en wizard `sections` es undefined →
  se usa el breadcrumb. La rama `mode === "history"` del breadcrumb queda
  inalcanzable pero inofensiva; no se tocó para dejar el bloque del wizard intacto.
- `justify-center` del contenedor central se activa solo con `sections` para no
  alterar la alineación a la izquierda del breadcrumb de admin.
