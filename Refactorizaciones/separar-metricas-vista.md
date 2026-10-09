# SDD: Separar las métricas del dashboard en una vista "Métricas" propia

**Slug:** `separar-metricas-vista`
**HU:** `docs/hu-separar-metricas-vista.md` (validada)
**Rama:** `feat/separar-metricas-vista` desde `develop`.

## Resumen funcional

Se mueve TODA la analítica (KPIs, tendencia por mes, desglose) desde
`/dashboard` (modo historial) a una ruta nueva de primer nivel `/metricas`
(hermana de `/dashboard`, patrón `/admin/cuentas`). Se agrega a `AppNavbar` un
selector de secciones ("Inspecciones" / "Métricas") en el centro de la píldora,
que convive con el breadcrumb del wizard (las pestañas solo se muestran fuera
del wizard). `/dashboard` queda enfocado en operar inspecciones y ya no pide
`api.caseStats()`. No cambia ningún componente de analítica (se reubican tal
cual) ni el backend.

## Toca

- **backend (API):** no
- **backend (Tatana):** no
- **client:** sí (`client/`)
- **agent-ui:** no

## Decisiones técnicas (ya validadas con el usuario; no re-preguntar)

- **D1 = A** — Ruta nueva `/metricas` (primer nivel). `page.tsx` server mínimo
  + pantalla cliente `MetricsScreen`.
- **D2 = A** — Acceso entre secciones: dos pestañas/pills en el CENTRO del
  navbar ("Inspecciones" | "Métricas"), activa marcada por `aria-current="page"`
  + color + peso.
- **D3 = A** — Toda la analítica se va a `/metricas`. `/dashboard` no muestra
  ningún KPI. Se MUEVE (no se duplica) el bloque `stats`/`statsLoading`/
  `statsError`/`loadStats` + JSX de analítica.
- **D4 = A** — Estado vacío propio en Métricas (sin casos → mensaje + botón a
  `/dashboard`).
- **D5 = A** — Solo guarda de sesión (sin roles), igual que el resto.
- **D6 = A** — En el wizard el centro sigue siendo el breadcrumb del paso; las
  pestañas de sección solo aparecen en modo historial del dashboard y en
  `/metricas`.

### Decisión de implementación del cambio en `AppNavbar` (D2)

`AppNavbar` recibe **una prop nueva opcional** en vez de meter los pills dentro
de `center`:

```ts
sections?: { activeSection: "inspecciones" | "metricas" };
```

Motivo: `center` hoy lo usan dashboard (breadcrumb/wizard) y admin
(breadcrumb "Administrar cuentas"); si se inyectaran los pills por `center` cada
página tendría que reimplementarlos. Con `sections` el navbar renderiza el
selector de forma reutilizable y centralizada, marca la activa solo, y las
páginas que no pasen `sections` (admin, wizard del dashboard) siguen con su
`center` como hoy. **Convivencia (D6):** si se pasan `sections` **y** `center`,
`sections` tiene prioridad y se renderiza en el centro; el dashboard pasa
`sections` solo en modo historial y `center` (breadcrumb) solo en modo wizard —
nunca los dos a la vez. Esto mantiene `AppNavbar` genérico: el default
(`sections` undefined) deja todo igual.

## Modelo de datos

No aplica. La HU no crea ni modifica datos, colecciones, índices, DTOs ni
mensajes de WebSocket.

## Endpoints / contrato backend

No cambia. `/metricas` reusa exactamente los endpoints ya existentes, vía las
funciones de `client/src/lib/api.ts` que ya los envuelven:

- `GET /api/cases/stats?tz_offset_minutes=…` → `api.caseStats(signal?)` →
  `CaseStats` (lo consumen `DashboardStats` y `CasesByMonthChart`).
- `GET /api/cases/breakdown?…` → `api.caseBreakdown(dimension, from?, to?, signal?)`
  → `CaseBreakdown` (fetch interno propio de `DashboardBreakdown`).

No se agrega, renombra ni quita ningún campo. No hay **Contrato compartido** que
documentar porque no cruza de lado (es solo `client/` reusando tipos ya
sincronizados: `CaseStats`/`CaseBreakdown` en `client/src/types/`).

## Arquitectura de la ruta nueva

Patrón `/admin/cuentas` (verificado en `client/src/app/admin/cuentas/page.tsx`
+ `client/src/components/admin/AdminAccountsScreen.tsx`):

- **`client/src/app/metricas/page.tsx`** (server component mínimo, sin
  `"use client"`): exporta `metadata` (`title: "Métricas · Factum"`) y renderiza
  `<MetricsScreen />`.
- **`client/src/components/dashboard/MetricsScreen.tsx`** (`"use client"`):
  guarda de sesión con `useAuth({ withHistory: false })` (Métricas no usa el
  array de casos, se nutre de `api.caseStats()` y del fetch propio del
  breakdown), monta su propia `AppNavbar` con `showThemeSwitch` y
  `brandHref="/dashboard"`, y es dueña del estado
  `stats`/`statsLoading`/`statsError` + `loadStats` (movido desde `page.tsx`).

No se crea `layout.tsx` de grupo (cada pantalla monta su navbar, igual que
admin y dashboard).

### `MetricsScreen` — estructura del render

Envoltura idéntica a `AdminAccountsScreen`:
`<div className="flex min-h-dvh flex-col bg-fx-bg text-fx-text">`.

1. **Guarda / skeleton:** mientras `!user`, renderizar
   `<AppNavbar user={null} showThemeSwitch brandHref="/dashboard" sections={{ activeSection: "metricas" }} />`
   + un skeleton local (patrón `ScreenSkeleton` de admin, adaptado a la grilla de
   métricas: barra de título + fila de 4 tarjetas + bloque de gráfico). `useAuth`
   ya redirige a `/` si no hay sesión (D5).
2. **Navbar (sesión resuelta):** `AppNavbar` con `user`, `onLogout={handleLogout}`,
   `showThemeSwitch`, `brandHref="/dashboard"`, `maxWidthClass="max-w-5xl"`
   (mismo ancho que el dashboard en modo historial) y
   `sections={{ activeSection: "metricas" }}`. No se pasa `center`.
3. **`<main className="mx-auto w-full max-w-5xl flex-1 px-4 pb-16 pt-5 sm:px-6">`**
   con transición de entrada `motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]`
   (mismo patrón que el dashboard; `motion-safe:` ya respeta
   `prefers-reduced-motion`).
   - **Encabezado:** `<h1 className="m-0 text-balance text-fx-h2 text-fx-text">Métricas</h1>`
     + un `<p className="m-0 mt-1 text-fx-body-sm text-fx-text-2">` con un
     subtítulo corto ("Tus inspecciones en números" o similar; el texto exacto lo
     fija el implementador con `ui-ux-pro-max`). No reusar `useGreetingClock`
     (es privado de `page.tsx` y no se exporta; no duplicar lógica de reloj — si
     se quiere fecha, usar un string estático simple o el subtítulo).
   - **Contenido:** mismo árbol que hoy vive en `page.tsx` líneas ~838-874, con
     los tres estados (ver sección "Estados").

## Estados de la vista Métricas

Reusar el mismo árbol de decisión que ya está en `page.tsx` (~842-874), pero con
el estado vacío propio (D4) en lugar de `null`:

| Situación | Condición | Qué se ve |
|---|---|---|
| Error | `statsError` | `FxBanner tone="error"` con "No se pudieron cargar las estadísticas." + botón "Reintentar" (`onClick={loadStats}`, `min-h-11`). El `DashboardBreakdown` igual se monta/reintenta por su cuenta (tiene su propio fetch y reintento). |
| Cargando / con datos | `statsLoading \|\| (stats && stats.total > 0)` | `<DashboardStats stats={stats} loading={statsLoading} />`; si `stats && stats.total > 0`: sección "Casos creados por mes" (`<h2 id="trend-title">` con ícono `TrendingUp` + `CasesByMonthChart data={stats.monthly}` dentro de `.fx-card`) y `<DashboardBreakdown />`. El skeleton de carga ya lo dan `DashboardStats loading` y el `loading` del `dynamic`. Envolver con `aria-busy={statsLoading}` el contenedor. |
| Sin casos | `stats && stats.total === 0` (y no cargando/error) | **Estado vacío propio (D4):** en lugar de `null`, un bloque centrado (`.fx-card` o similar) con ícono, título "Todavía no hay nada para medir", texto breve y un botón/Link "Crear la primera inspección" que va a `/dashboard` (usar `<Link href="/dashboard">` o `<Button onClick={() => router.push("/dashboard")}>`; `min-h-11`). Tono de referencia: estado de bienvenida de `CaseHistory`. |

Notas:
- `DashboardStats` acepta `stats: CaseStats | null` y `loading: boolean`
  (verificado: `DashboardStats({ stats, loading })`), muestra su propio
  esqueleto cuando `loading || !stats`.
- `CasesByMonthChart` sigue cargándose con `next/dynamic` (`ssr: false`) tal
  cual en `page.tsx`: **mover esa declaración `const CasesByMonthChart = dynamic(...)`
  a `MetricsScreen.tsx`** (con su `loading` placeholder `h-64` y su
  import de `dynamic` desde `next/dynamic`).
- `DashboardBreakdown` es autónomo (fetch interno con `AbortController`), se
  monta sin props.

## Cambio en `AppNavbar` (D2 + D6)

Archivo: `client/src/components/shell/AppNavbar.tsx`.

1. Agregar a `AppNavbarProps` la prop opcional:
   `sections?: { activeSection: "inspecciones" | "metricas" };`
   con su comentario (patrón de las props existentes).
2. En el render del centro (hoy
   `<div className="flex min-w-0 flex-1 items-center px-1 sm:px-2">{center}</div>`):
   si `sections` está definido, renderizar el **selector de secciones** en lugar
   de `center`; si no, `center` como hoy.
3. El selector: un `<nav aria-label="Secciones">` con dos ítems que son
   `<Link>` de `next/link` (navegación SPA, sin recargar la app):
   - "Inspecciones" → `href="/dashboard"`.
   - "Métricas" → `href="/metricas"`.
   - El ítem cuya sección coincide con `activeSection` lleva `aria-current="page"`
     y estilo activo (color `text-fx-text` + fondo tipo pill
     `bg-fx-surface-3`/`bg-fx-accent-soft` + `font-semibold`); el inactivo
     `text-fx-text-3 font-medium hover:text-fx-text`. Distinción por **color Y
     peso/texto**, no solo color (A11y).
   - Cada ítem con `fx-focus-ring`, `min-h-*` táctil, operable con teclado (son
     `<Link>`, lo son nativamente).
   - Estilo "pill"/pestañas coherente con el lenguaje de la navbar; el pixel
     final lo fija `ui-ux-pro-max`. Centrar el grupo (`justify-center`) como hace
     hoy el `center` del dashboard.
4. **Responsive 360px:** el grupo de pills vive en el contenedor `flex-1 min-w-0`
   del centro, que ya trunca; las acciones de la derecha (`AgentChip`, "Guía",
   "Soporte") no se tapan porque están en su propia zona. Los labels deben
   entrar en 360px: textos cortos ("Inspecciones" / "Métricas"), con `truncate`
   si hace falta; no colapsar a solo-ícono (son dos ítems, entran). Verificar que
   no haya scroll horizontal.
5. **No romper usos existentes:** admin y el wizard del dashboard NO pasan
   `sections`, así que siguen con su `center` intacto. El default (`sections`
   undefined) deja `AppNavbar` idéntico a hoy.

## Checklist atómico — client (`implementer-frontend`)

Skills obligatorias antes del JSX final: `ui-ux-pro-max`, `senior-frontend`,
`3d-web-experience` (criterio, sin 3D), `web-design-guidelines` (autochequeo);
`ui-styling` y `mblode-agent-skills-ui-animation` si aplican. Dejar constancia en
`progress/impl_frontend_separar-metricas-vista.md`. Leer `client/AGENTS.md` y la
doc de Next 16 en `node_modules/next/dist/docs/` antes de tocar código.

### A. `AppNavbar` (habilitar navegación entre secciones)
- [ ] Agregar prop `sections?: { activeSection: "inspecciones" | "metricas" }` a
      `AppNavbarProps` con comentario.
- [ ] Importar `Link` ya está (`next/link` ya importado en el archivo).
- [ ] En la zona central: si `sections` definido, renderizar el selector de
      secciones (dos `<Link>` pill "Inspecciones"/"Métricas", activa con
      `aria-current="page"` + color + peso, `fx-focus-ring`, centrado); si no,
      `{center}` como hoy.
- [ ] Verificar que admin y dashboard-wizard (que no pasan `sections`) siguen
      viéndose igual.

### B. Ruta y pantalla `/metricas`
- [ ] Crear `client/src/app/metricas/page.tsx` (server, `metadata` con
      `title: "Métricas · Factum"`, renderiza `<MetricsScreen />`).
- [ ] Crear `client/src/components/dashboard/MetricsScreen.tsx` (`"use client"`):
      guarda `useAuth({ withHistory: false })`, navbar con
      `sections={{ activeSection: "metricas" }}`, `showThemeSwitch`,
      `brandHref="/dashboard"`, `maxWidthClass="max-w-5xl"`.
- [ ] Mover a `MetricsScreen` el estado `stats`/`statsLoading`/`statsError` +
      `loadStats` + el `useEffect` que dispara `loadStats` cuando hay `user`
      (ver bloque C).
- [ ] Mover a `MetricsScreen` la declaración
      `const CasesByMonthChart = dynamic(() => import(...))` (con `ssr: false` y
      su `loading` placeholder) y el import `dynamic` de `next/dynamic`.
- [ ] Render: `<h1>Métricas</h1>` + subtítulo; luego el árbol de estados
      error / cargando-con-datos / vacío (D4) descrito arriba, con `aria-busy`.
- [ ] Skeleton local para `!user` (patrón `ScreenSkeleton` de admin).
- [ ] Transición de entrada `motion-safe:animate-[fx-fade-in_…]` en el `<main>`.
- [ ] Estado vacío (D4): mensaje + botón "Crear la primera inspección" →
      `/dashboard`.

### C. Limpiar `/dashboard` (`client/src/app/dashboard/page.tsx`) — mover, no duplicar
- [ ] **Quitar** los imports `DashboardStats` (línea ~30) y `DashboardBreakdown`
      (línea ~31).
- [ ] **Quitar** la declaración `const CasesByMonthChart = dynamic(...)`
      (líneas ~52-58) y, si ya no se usa, el import `dynamic` de `next/dynamic`
      (línea ~4) — verificar que no haya otro uso de `dynamic` en el archivo
      antes de borrarlo.
- [ ] **Quitar** del componente `Dashboard` el bloque de estado analítico:
      `stats`, `statsLoading`, `statsError`, `loadStats` y el
      `useEffect(() => { if (user) loadStats(); }, …)` (líneas ~170-186).
- [ ] **Simplificar** `refreshHistory` a solo `loadHistory` (líneas ~189-192):
      `const refreshHistory = useCallback(() => { loadHistory(); }, [loadHistory]);`
      — o reemplazar directamente los usos de `refreshHistory` por `loadHistory`
      si queda trivial (hay un uso en ~607 `refreshHistory()` y en ~922
      `onRefresh={refreshHistory}`). Mantener nombre `refreshHistory` es válido.
- [ ] **Quitar** el JSX de analítica del modo historial (líneas ~838-874: el
      bloque `statsError ? … : statsLoading || (stats && stats.total > 0) ? … : null`,
      incluyendo la sección "Casos creados por mes" y `<DashboardBreakdown />`).
      `GreetingHeadline`, "Nueva inspección", "Pendientes de retomar",
      `statusMsg` y `CaseHistory` se CONSERVAN.
- [ ] **Quitar** el import `CaseStats` de `@/lib/api` (línea ~8) si ya no se usa
      en `page.tsx` tras mover el estado — verificar.
- [ ] **Quitar** el import de `TrendingUp` de `lucide-react` (línea ~6) si ya no
      se usa en `page.tsx` tras quitar la sección de tendencia — verificar (se
      reusa en `MetricsScreen`).
- [ ] En la `AppNavbar` del dashboard: pasar
      `sections={{ activeSection: "inspecciones" }}` **solo en modo historial**
      (`mode === "history"`), y seguir pasando el `center` (breadcrumb) **solo en
      modo wizard** (D6). Dado que `sections` tiene prioridad sobre `center` en el
      navbar, en la práctica: `sections={mode === "history" ? { activeSection: "inspecciones" } : undefined}`
      y mantener `center` como está (en wizard `sections` es undefined → se usa
      `center`; en historial `sections` manda y el breadcrumb central del
      historial deja de mostrarse, reemplazado por los pills). Confirmar que el
      breadcrumb del wizard (flecha "volver" + "Inspecciones > paso") sigue
      intacto en modo wizard.

## A11y y responsive (criterios de aceptación)

- Selector de secciones: `<nav aria-label="Secciones">`, ítems `<Link>`
  operables con teclado, `aria-current="page"` en la activa, distinción por
  color **y** peso/texto, `fx-focus-ring` visible.
- Nombres accesibles en español ("Inspecciones", "Métricas").
- `<h1>Métricas</h1>` accesible como título de la vista.
- `aria-busy` en el contenedor de analítica mientras `statsLoading`.
- 360px: pills visibles y operables sin tapar `AgentChip`/"Guía"/"Soporte"; sin
  scroll horizontal en `/metricas`.
- `prefers-reduced-motion`: la entrada de la vista usa `motion-safe:` (no corre
  con reduce); las animaciones internas de `DashboardStats`/charts ya lo
  respetan y no se tocan.

## Verificación

Comandos que corre el implementador antes de declararse `done` (en `client/`):

```bash
cd client && npx tsc --noEmit
cd client && npm run build
```

Ambos deben terminar sin errores. No corre `dotnet build` (no hay cambios de
backend).

**Prueba manual que queda para el usuario:**
1. Entrar a `/dashboard` autenticado → ver saludo, "Nueva inspección",
   pendientes de retomar e historial; NO ver KPIs, gráfico ni desglose; el
   historial no quedó empujado.
2. En el centro de la navbar: dos pestañas "Inspecciones" (activa) | "Métricas".
3. Click en "Métricas" → navega a `/metricas` sin recargar la app; se ven KPIs,
   "Casos creados por mes" y el desglose, igual que antes; la pestaña "Métricas"
   queda activa.
4. Click en "Inspecciones" desde `/metricas` → vuelve a `/dashboard`.
5. Entrar al wizard de nueva inspección → el centro muestra el breadcrumb del
   paso (sin pestañas de sección); la flecha "volver" sigue con su confirmación.
6. Perito sin casos → `/metricas` muestra el estado vacío con botón a
   `/dashboard`.
7. Forzar fallo de `/api/cases/stats` → banner "Reintentar" en `/metricas`; el
   desglose sigue intentando por su cuenta.
8. Abrir `/metricas` sin sesión (borrar token) → redirige a `/`.
9. Ventana a 360px → pestañas visibles/operables, sin scroll horizontal.

## Archivos tocados (resumen)

- `client/src/components/shell/AppNavbar.tsx` — prop `sections` + selector.
- `client/src/app/metricas/page.tsx` — **nuevo** (server).
- `client/src/components/dashboard/MetricsScreen.tsx` — **nuevo** (cliente).
- `client/src/app/dashboard/page.tsx` — limpieza (mover estado y JSX de
  analítica, simplificar `refreshHistory`, pasar `sections` en historial,
  limpiar imports).

Sin cambios en `server/`, `agent-ui/`, ni en los componentes de analítica
(`DashboardStats`, `CasesByMonthChart`, `DashboardBreakdown`,
`BreakdownBarsChart`, `chart-theme.ts`): se reubican tal cual.
