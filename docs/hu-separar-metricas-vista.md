# HU: Separar las métricas del dashboard en una vista "Métricas" propia

**Slug:** `separar-metricas-vista`
**Apps afectadas:** `client/` (Next.js 16) **solamente**. No toca `server/`
(ni la API `Factum.Backend` ni el agente Tatana) ni `agent-ui/`.

**Como** perito que usa Factum todos los días
**quiero** que `/dashboard` quede enfocado en operar inspecciones (crear una
nueva y retomar/ver el historial) y tener las métricas (KPIs, tendencia por mes
y desglose) en una vista propia "Métricas" con su acceso en la barra de
navegación
**para que** cuando entro a trabajar vea primero lo operativo sin que la
analítica me tape el camino, y pueda ir a mirar mis números cuando quiera, en
una pantalla pensada para eso.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-09)

Esta HU reorganiza arquitectura de información: **mueve** componentes de
analítica que ya existen y agrega navegación. No crea lógica de negocio nueva.

**La analítica ya está construida y vive mezclada en `/dashboard`.** En la rama
`origin/feat/ola2-frontend` (PR #39, por mergearse a `develop`; inspeccionable
con `git show origin/feat/ola2-frontend:<ruta>`) se montó en
`client/src/app/dashboard/page.tsx`, **solo en modo historial** (`mode ===
"history"`), un bloque de analítica con tres piezas:

- **`client/src/components/dashboard/DashboardStats.tsx`** — cuatro tarjetas KPI
  (Total con chips por estado, Completados con anillo de %, En proceso, Tiempo
  promedio de cierre).
- **`client/src/components/dashboard/CasesByMonthChart.tsx`** — gráfico de
  tendencia "Casos creados por mes" (Recharts, cargado con `next/dynamic`,
  `ssr: false`).
- **`client/src/components/dashboard/DashboardBreakdown.tsx`** +
  **`BreakdownBarsChart.tsx`** + **`chart-theme.ts`** — desglose por dimensión
  (plataforma / estado / carátula / ámbito) con selector, filtro de fechas y
  tabla accesible.

En `page.tsx` esas tres piezas se renderizan juntas (líneas ~838-874), con un
contenedor condicional: esqueleto mientras carga, banner de error con
reintento, y nada cuando el perito no tiene casos (el estado de bienvenida lo
da `CaseHistory`).

**Cómo se alimentan las métricas hoy.** Contra lo que sugiere el nombre
"DashboardStats", **la analítica NO se calcula en el cliente**: el trabajo de
`feat/ola2-frontend` terminó eligiendo endpoints de agregación en el backend
(Variante B de las HU `dashboard-kpis-tendencias` y `dashboard-breakdown`):

- `DashboardStats`/`CasesByMonthChart` consumen `GET /api/cases/stats` vía
  `api.caseStats(signal)` (`client/src/lib/api.ts` ~1230), que devuelve
  `CaseStats` (`{ total, by_status, completion_rate, avg_close_seconds,
  monthly[] }`). En `page.tsx` ese fetch vive en un `useState`/`useEffect`
  propio (`stats`, `statsLoading`, `statsError`, `loadStats`), **separado** del
  fetch del historial (`useAuth().loadHistory` → `api.listCases()`). Se recargan
  juntos solo por conveniencia en `refreshHistory` (que llama a ambos).
- `DashboardBreakdown` tiene su **propio** fetch interno (`api.caseBreakdown(...)`
  → `GET /api/cases/breakdown`), con su `AbortController`, su estado de carga y
  su reintento, independiente del dashboard.

Conclusión clave para el alcance: **el historial y las métricas ya cargan por
fetches distintos.** `DashboardStats` depende solo de `api.caseStats()` (no del
array `historyCases`), y `DashboardBreakdown` es autónomo. Mover estos
componentes a otra ruta **no** requiere arrastrar el historial ni duplicar la
carga de casos: la vista Métricas monta los mismos componentes y dispara los
mismos endpoints. **Los endpoints backend NO cambian** (de ahí "solo `client/`").

**La barra de navegación.** `client/src/components/shell/AppNavbar.tsx` es una
píldora flotante con tres zonas: tapa-marca (izquierda), `center` (contexto de
navegación que pasa cada página) y `actions` + tema + menú de usuario
(derecha). Hoy **no hay un menú de secciones**: el `center` del dashboard es un
*breadcrumb* ("● Inspecciones" y, en el wizard, "> Captura"), no ítems de
navegación entre vistas. `AppNavbar` acepta `center`, `actions`, `user`,
`onLogout`, `showThemeSwitch`, `brandHref`, `maxWidthClass`. No tiene hoy un
concepto de "ruta activa".

**Rutas autenticadas existentes.** `client/src/app/`:
`dashboard/page.tsx` (hub), `admin/cuentas/page.tsx` (panel superadmin),
`cambiar-contrasena/page.tsx`. El patrón de una ruta secundaria autenticada es
`/admin/cuentas`: página server mínima que renderiza una pantalla cliente
(`AdminAccountsScreen`) que hace su propia guarda con `useAuth()` (redirige a
`/` si no hay sesión) y monta su `AppNavbar`. No hay un `layout.tsx` de grupo
que comparta la navbar entre rutas: cada pantalla monta su propia `AppNavbar`.

**Roles / acceso.** No hay sistema de roles para casos: `useAuth` resuelve la
sesión por token y toda métrica ya es del perito logueado (los endpoints
`/api/cases/*` filtran por DNI del token). No existe "métricas de otros
peritos" (ver D5).

### Por qué hace falta

El usuario validó que `/dashboard` mezcla dos intenciones: **operar** (empezar
una inspección, retomar borradores, buscar un caso en el historial) y
**analizar** (KPIs, tendencia, desglose). Al entrar a trabajar, lo operativo es
lo urgente; la analítica empuja el historial hacia abajo y compite por la
atención. Separar las métricas en su propia vista deja `/dashboard` limpio para
el flujo de trabajo y le da a la analítica un lugar propio, enlazable y
pensado para leerse de corrido.

### Qué es lo nuevo

1. Una **ruta y pantalla "Métricas"** que monta `DashboardStats`,
   `CasesByMonthChart` y `DashboardBreakdown` (los mismos componentes, los
   mismos endpoints), con su propia guarda de sesión, navbar y estados de
   carga/vacío/error.
2. **Navegación entre "Inspecciones" (`/dashboard`) y "Métricas"** desde
   `AppNavbar` (ver D2 para dónde va el ítem).
3. La **limpieza de `/dashboard`**: sacar el bloque de analítica del modo
   historial (y su fetch de `stats`, salvo lo que decida D3).

### Relación con otras HU

- **`dashboard-kpis-tendencias` y `dashboard-breakdown`** (serie "ola 2",
  PR #39): construyeron la analítica que esta HU reubica. Esta HU **no** cambia
  el contenido, el cálculo ni el look de esos componentes; solo su **ubicación**
  y cómo se llega a ellos. Depende de que PR #39 esté en `develop` (si se
  implementa antes del merge, parte de `feat/ola2-frontend`).
- **`rediseno-dashboard`**: definió el look del hub (saludo, grilla, historial).
  Esta HU respeta ese layout en `/dashboard` y reusa sus tokens/patrones en la
  vista Métricas.

---

## Criterios de aceptación

```gherkin
Feature: Vista "Métricas" separada del hub de inspecciones

  Background:
    Given un perito autenticado

  Scenario: /dashboard queda enfocado en inspecciones
    When entro a "/dashboard"
    Then veo el saludo, el acceso a "Nueva inspección", los borradores
         pendientes de retomar y el historial de casos
    And NO veo las tarjetas de KPIs, el gráfico de tendencia ni el desglose
    And el historial no quedó empujado por el bloque de analítica

  Scenario: Acceso a Métricas desde la barra de navegación
    When estoy en "/dashboard"
    Then la barra de navegación ofrece un acceso a "Métricas"
    And el acceso indica que la sección activa es "Inspecciones" (no "Métricas")

  Scenario: Ir a la vista Métricas
    When activo el acceso "Métricas"
    Then navego a la ruta de métricas (D1) sin recargar toda la app
    And la vista muestra las tarjetas de KPIs, el gráfico "Casos creados por mes"
         y el desglose, igual que mostraba el dashboard antes
    And el acceso de navegación marca "Métricas" como sección activa

  Scenario: Volver a Inspecciones desde Métricas
    When estoy en la vista Métricas
    Then la barra de navegación ofrece volver a "Inspecciones" (/dashboard)
    And al activarlo vuelvo al hub operativo

  Scenario: Las métricas siguen siendo las del perito logueado
    Given la vista Métricas monta DashboardStats, CasesByMonthChart y DashboardBreakdown
    Then consume los mismos endpoints que antes (GET /api/cases/stats y
         /api/cases/breakdown), filtrados por el token del perito
    And no se agregó, renombró ni quitó ningún campo de la API

  Scenario: Estado de carga de la vista Métricas
    Given las estadísticas todavía se están cargando
    Then la vista muestra el esqueleto de tarjetas y el placeholder del gráfico
         (los mismos que ya traen DashboardStats y el desglose), con aria-busy
    And no se ven números a medias ni saltos bruscos

  Scenario: Error al cargar las estadísticas en Métricas
    Given GET /api/cases/stats falla
    Then la vista muestra un aviso con ícono + texto y un botón "Reintentar"
    And el resto de la vista (p. ej. el desglose) sigue intentando por su cuenta

  Scenario: Vista Métricas sin casos
    Given el perito no tiene ningún caso
    Then en vez de KPIs en cero y gráficos vacíos, la vista muestra un estado
         vacío que invita a crear la primera inspección con un acceso a /dashboard
         (ver D4)

  Scenario: Acceso directo a la ruta de Métricas sin sesión
    Given no hay sesión activa
    When abro la ruta de métricas directamente por URL
    Then se me redirige al inicio ("/") igual que las demás rutas autenticadas

  Scenario: Responsive de la navegación y la vista
    Given una pantalla de 360 px de ancho
    Then el acceso a Métricas/Inspecciones es visible y operable (sin tapar las
         acciones de la navbar), y la vista Métricas no produce scroll horizontal

  Scenario: Accesibilidad de la navegación
    Then el acceso de navegación entre secciones es operable con teclado,
         con nombres accesibles en español y la sección activa anunciada
         (aria-current="page" o equivalente)

  Scenario: Movimiento reducido
    Given prefers-reduced-motion: reduce
    Then la transición de entrada de la vista Métricas no corre (o es instantánea)
         y las animaciones heredadas de los componentes siguen respetando el ajuste

  Scenario: Build
    Then "npx tsc --noEmit" y "npm run build" en client/ terminan sin errores
    And no hay cambios en server/ que requieran "dotnet build"
```

---

## Datos que se registran

**No aplica.** La HU no crea ni modifica datos de negocio, no agrega campos,
colecciones, índices, DTOs ni mensajes de WebSocket. Mueve componentes de
presentación y reusa endpoints de solo lectura (`GET /api/cases/stats`,
`GET /api/cases/breakdown`) que ya existen y no cambian.

---

## Diseño UX/UI (`client/` — web)

Reorganización de información dentro del sistema de diseño ya vigente: tokens
`--fx-*`, tipografía `text-fx-*`, tarjetas `.fx-card`, íconos `lucide-react`,
píldora de navbar. Sin hex ni paleta Tailwind cruda. No se rediseñan los
componentes de analítica: se reubican tal cual.

### Navegación (`AppNavbar`)

Hace falta introducir un **acceso de navegación entre secciones**
("Inspecciones" ↔ "Métricas") que hoy no existe (el `center` del dashboard es un
breadcrumb, no un menú de secciones). Debe:

- Marcar la **sección activa** (`aria-current="page"` o `aria-pressed`), con
  distinción por color **y** texto/peso, no solo color.
- Ser operable con teclado, con nombres accesibles en español.
- En `/dashboard` modo wizard, el breadcrumb actual ("Inspecciones > Captura" y
  el botón "volver") **se conserva**: el acceso a secciones no debe pelearse con
  ese contexto (ver D2 y D6 para la convivencia).
- En mobile (360 px) seguir siendo visible y operable sin tapar `AgentChip`,
  "Nueva inspección", "Guía" ni "Soporte". Si no entra, la SDD define el
  colapso (íconos, "más", etc.), pero el acceso no puede quedar oculto del todo.

El tratamiento visual exacto (dos ítems tipo "pill"/pestañas en el centro, un
ícono de "gráficos" a la derecha, o un menú) lo afina la SDD según D2 y lo que
mande `ui-ux-pro-max`; esta HU fija el requisito funcional (acceso bidireccional,
sección activa, accesible, responsive), no el pixel.

### Vista "Métricas"

- **Ruta:** según D1 (recomendada `/metricas`).
- **Estructura de pantalla:** misma envoltura que el hub (fondo `bg-fx-bg`,
  `AppNavbar` con `user`/`onLogout`/`showThemeSwitch`, contenedor centrado con el
  mismo `max-w` que usa el dashboard en modo historial). Dentro, en orden:
  1. Un encabezado de la vista (título "Métricas" `text-fx-*` + fecha/saludo
     opcional según D4; como mínimo un `<h1>` accesible).
  2. **Fila de KPIs** → `DashboardStats` (sin cambios).
  3. **Tendencia** → sección "Casos creados por mes" + `CasesByMonthChart`
     (cargado con `next/dynamic` como hoy).
  4. **Desglose** → `DashboardBreakdown` (sin cambios).
- **Carga de datos:** la vista Métricas dispara `api.caseStats()` (mismo patrón
  `stats`/`statsLoading`/`statsError`/`loadStats` que hoy vive en `page.tsx`) y
  `DashboardBreakdown` sigue con su fetch propio. Según D3, ese bloque de estado
  de `stats` se **mueve** del dashboard a la vista Métricas (no se duplica).

### Estados

| Situación | Qué se ve |
|---|---|
| Cargando | Esqueleto de tarjetas (`DashboardStats loading`) + placeholder del gráfico + skeleton del desglose, con `aria-busy` |
| Error de `stats` | Banner con ícono + texto + "Reintentar" (el mismo que hoy está en `page.tsx`); el desglose sigue por su cuenta |
| Sin casos (`total === 0`) | Estado vacío de la vista Métricas (D4): mensaje que invita a crear la primera inspección con acceso a `/dashboard`, sin KPIs en cero ni gráficos vacíos |
| Sin sesión | Redirección a `/` (guarda de `useAuth`, igual que las demás rutas) |

### Feedback / movimiento

- Transición de entrada de la vista corta (patrón `fx-fade-in` que ya usa el
  dashboard), desactivada con `prefers-reduced-motion`. Las animaciones internas
  de `DashboardStats` (anillo) y los charts ya respetan el ajuste; no se tocan.

---

## Fuera de alcance

- **Rediseñar o cambiar el contenido** de `DashboardStats`, `CasesByMonthChart`,
  `DashboardBreakdown`, `BreakdownBarsChart` o `chart-theme.ts`: se mueven tal
  cual. Cualquier ajuste de look de esos componentes es otra HU.
- **Cualquier cambio de backend:** los endpoints `/api/cases/stats` y
  `/api/cases/breakdown` no se tocan; no se agregan/renombran/sacan campos.
- Nuevas métricas, dimensiones, filtros temporales avanzados o exportación: no
  pedidos.
- Métricas de **otros** peritos, panel de supervisión o roles sobre casos (ver
  D5): requeriría backend; fuera de esta HU.
- Un **sistema de navegación global** reutilizable más allá de Inspecciones ↔
  Métricas (p. ej. incorporar "Administrar cuentas" al mismo menú): la SDD puede
  dejar la pieza extensible, pero el alcance es solo estas dos secciones.
- `agent-ui/` (Electron) y el agente Tatana.
- Tests automatizados / regresión visual (no hay infraestructura en `client/`).

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- Leer `client/AGENTS.md` y la doc de Next 16 en `node_modules/next/dist/docs/`
  antes de tocar código (el App Router de esta versión difiere del "de memoria").
- La nueva ruta sigue el patrón de `/admin/cuentas`: `page.tsx` server mínimo +
  pantalla cliente (p. ej. `MetricsScreen`) que hace la guarda con `useAuth`
  (`withHistory: false`, porque Métricas no necesita el array de casos — se nutre
  de `api.caseStats()`/`api.caseBreakdown()`) y monta su `AppNavbar`.
- **Mover, no duplicar:** el bloque `stats`/`statsLoading`/`statsError`/`loadStats`
  y el JSX de analítica salen de `dashboard/page.tsx` y se re-montan en la vista
  Métricas. En `page.tsx`, `refreshHistory` vuelve a ser solo `loadHistory` (ya
  no recarga `stats`), y el `import` de `CasesByMonthChart`/`DashboardStats`/
  `DashboardBreakdown` deja de ser necesario ahí (ver D3).
- El acceso de navegación entre secciones es nuevo en `AppNavbar`: definir si se
  agrega como una prop nueva (p. ej. `sections`/`activeSection`) o se pasa dentro
  de `center`/`actions`. Debe convivir con el breadcrumb del wizard del dashboard
  (D6). Mantener `AppNavbar` reutilizable (lo usan dashboard y admin).
- Skills obligatorias de frontend: `ui-ux-pro-max` (antes del JSX final),
  `senior-frontend`, `3d-web-experience` (como criterio, sin agregar 3D),
  `web-design-guidelines` (autochequeo). Sumar `ui-styling` y
  `mblode-agent-skills-ui-animation` si aplica. Dejar constancia en el progress.
- Verificación: `npx tsc --noEmit` y `npm run build` en `client/`. Sin
  `dotnet build` (no hay cambios de backend).

---

## Dudas para validar con el usuario

### D1. Ruta de la vista Métricas: `/metricas` o `/dashboard/metricas`

- **A) `/metricas`** — ruta de primer nivel, hermana de `/dashboard`.
- **B) `/dashboard/metricas`** — anidada bajo el dashboard.
- **Recomendada: A (`/metricas`).** El objetivo validado es que Métricas sea una
  **sección paralela** al hub de inspecciones, no un sub-estado del dashboard;
  `/metricas` lo comunica mejor en la URL, es más corta para compartir/marcar, y
  evita sugerir una jerarquía "el dashboard contiene a métricas" que contradice
  la separación. Además no hay un `layout.tsx` de grupo que las comparta (cada
  pantalla monta su navbar), así que anidar no aporta reuso real. B tendría
  sentido solo si en el futuro hubiera varias sub-vistas colgando del dashboard;
  no es el caso. En español `/metricas` (sin acento en la URL) es consistente con
  las rutas existentes (`/cambiar-contrasena`, `/admin/cuentas`).

### D2. ¿El acceso a Métricas va en el centro de la navbar (como pestañas) o a la derecha (ícono de acción)?

- **A) Centro, como dos "pestañas"/pills** "Inspecciones" y "Métricas" (lenguaje
  pill de la navbar), marcando la activa.
- **B) Derecha, un ícono de acción** (p. ej. gráfico) junto a "Guía"/"Soporte",
  que lleva a Métricas; y en Métricas, un ícono/volver a Inspecciones.
- **C) Dentro del menú de usuario** (menos visible).
- **Recomendada: A (centro, como pestañas).** La separación Inspecciones ↔
  Métricas es un cambio de **sección de primer nivel**, y el centro de la navbar
  es donde el dashboard ya pone el contexto de navegación (el breadcrumb
  "● Inspecciones"): convertirlo en dos ítems navegables es natural y mantiene la
  jerarquía visual (secciones en el centro, acciones a la derecha). B funciona
  pero esconde "Métricas" entre acciones y no comunica que son dos vistas pares;
  C la entierra. **Convivencia:** en `/dashboard` **modo wizard** el centro
  muestra el breadcrumb del paso ("Inspecciones > Captura"), así que las pestañas
  de sección se muestran solo en modo historial del dashboard y en la vista
  Métricas (en el wizard el centro sigue siendo el breadcrumb) — lo fija la SDD
  (ver D6). Si el usuario prefiere no tocar el centro, B es el plan B de menor
  riesgo.

### D3. ¿`/dashboard` conserva un mini-resumen de métricas o se lleva TODA la analítica a Métricas?

- **A) Toda la analítica se va a Métricas**; `/dashboard` no muestra ningún KPI.
- **B) `/dashboard` conserva un mini-resumen** (p. ej. solo "Total" y "Este mes"
  / "Completados") como un vistazo rápido, con el resto en Métricas.
- **Recomendada: A (toda a Métricas).** Es lo que pide el objetivo: dejar el hub
  "enfocado en inspecciones". Un mini-resumen (B) reintroduce justo el ruido que
  se quiere sacar y obliga a decidir qué KPIs son "los importantes", a mantener
  dos vistas de los mismos números y —técnicamente— a seguir pidiendo
  `api.caseStats()` desde el dashboard (no se podría simplificar `refreshHistory`
  ni sacar el fetch de `stats` de `page.tsx`). A deja el corte limpio: el
  dashboard vuelve a necesitar solo `loadHistory`. Nota: hoy "Este mes" no es un
  KPI propio de `DashboardStats` (muestra Total/Completados/En proceso/Tiempo de
  cierre), así que B además implicaría componer un resumen nuevo. Si el usuario
  valora mucho el "pulso al entrar", B con **una sola** tarjeta "Total" sería el
  mínimo aceptable; igual recomiendo A.

### D4. ¿Qué muestra la vista Métricas cuando el perito no tiene casos?

- **A) Estado vacío propio** de Métricas: ilustración/mensaje "Todavía no hay
  nada para medir" + botón "Crear la primera inspección" que va a `/dashboard`.
- **B) Mostrar los componentes igual** (KPIs en 0, gráficos vacíos).
- **C) Redirigir a `/dashboard`** si no hay casos.
- **Recomendada: A (estado vacío propio).** Coherente con cómo el dashboard ya
  evita mostrar "KPIs en cero y gráfico vacío" (hoy, con `total === 0`, no monta
  la analítica). B se ve roto y vacío; C es sorpresivo (el usuario pidió Métricas
  y termina en otra pantalla sin explicación). A comunica el estado y ofrece la
  acción útil. La SDD define el texto exacto y si reusa el estado de bienvenida
  de `CaseHistory` como referencia de tono.

### D5. ¿Algún control de acceso a la vista Métricas (roles)?

- **A) Cualquier perito autenticado ve sus propias métricas** (sin roles); la
  guarda es la misma sesión que el resto de rutas.
- **B) Restringir por rol** (p. ej. solo supervisor/superadmin).
- **Recomendada: A — y hoy es la única opción real.** No existe sistema de roles
  para casos: las métricas ya salen filtradas por el DNI del token, así que cada
  perito ve exclusivamente lo suyo. B requeriría primero una HU de roles/permisos
  sobre casos y un endpoint de métricas de terceros (fuera de alcance; esta HU es
  solo `client/`). La única guarda que aplica es la de sesión (redirigir a `/`
  sin token), igual que `/dashboard` y `/admin/cuentas`. Lo anoto por si el
  objetivo latente era un panel de supervisión, en cuyo caso se reencuadra.

### D6. En el **wizard** de `/dashboard`, ¿cómo convive el acceso a secciones con el breadcrumb del paso?

- **A) Durante el wizard, el centro de la navbar sigue siendo el breadcrumb**
  ("← Inspecciones > Captura") y **no** se muestran las pestañas de sección; las
  pestañas aparecen solo en el hub (modo historial) y en Métricas.
- **B) Mostrar siempre las pestañas de sección** y el breadcrumb debajo/al lado.
- **Recomendada: A.** El wizard es un flujo con pasos donde salir a mitad pide
  confirmación (hay evidencia sin guardar); meter ahí un salto directo a
  "Métricas" invita a perder trabajo y satura un centro de navbar ya estrecho en
  mobile. Mantener el breadcrump del wizard como está (ya resuelve el "volver a
  Inspecciones" con su confirmación) y mostrar las pestañas de sección solo fuera
  del wizard es lo más seguro y simple. Depende de D2-A; si se elige D2-B (ícono
  a la derecha), esta duda casi desaparece porque el breadcrumb central no se
  toca.
