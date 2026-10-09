# HU: Dashboard de KPIs y tendencia temporal del perito

**Slug:** `dashboard-kpis-tendencias`
**Apps afectadas:** `server/` (API `Factum.Backend`) + `client/` (Next.js 16).
`agent-ui/` y el agente Tatana quedan fuera.

**Como** perito que usa Factum todos los días
**quiero** ver en el panel un resumen analítico de mi trabajo —tarjetas de KPIs
(total de casos, cuántos hay por estado, tasa de completitud, tiempo promedio de
cierre) y un gráfico de cuántos casos abrí por mes—
**para que** pueda medir de un vistazo mi carga de trabajo, mi ritmo de cierre y
la tendencia de los últimos meses, sin tener que contar casos a mano en el
historial.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-09)

**Frontend.** La única pieza de analítica en `/dashboard` es
`client/src/components/dashboard/DashboardStats.tsx`: cuatro tarjetas (`Total`,
`Este mes`, `Completadas` con anillo de %, `En proceso`) calculadas **en el
cliente** sobre el array `historyCases` que ya trae el dashboard. No hay gráfico
de tendencia ni desglose por estado (solo "completadas" vs. "resto"). El
cálculo del delta mes-a-mes y del % completadas vive dentro del componente
(funciones `isSameMonth`, filtros por `status`). El componente ya está estilado
con tokens `--fx-*`, usa `framer-motion` (anillo de progreso con
`useReducedMotion`) e íconos `lucide-react`.

`app/dashboard/page.tsx` monta `DashboardStats` solo si
`historyCases.length > 0` (línea ~803), con los casos que `useAuth.loadHistory`
ya cargó vía `api.listCases()`.

**Datos que ya tiene el cliente.** `api.listCases()` → `GET /api/cases` devuelve
`{ cases: Case[] }` con, por caso: `status` (`"draft" | "generating" |
"completed" | "error"`), `created_at` (ISO) y `generated_at?` (ISO, presente
solo en casos generados). El listado excluye `report_texts`, `zip_password` y
`pending_generation` (proyección en `MongoRepository.ListByOfficerAsync`), pero
trae `status`, `created_at` y `generated_at`. Es decir: **todos los KPIs y la
tendencia se pueden calcular con los datos que el dashboard ya tiene en
memoria**, sin endpoint nuevo (ver D1).

**Backend.** `server/src/Factum.Backend` (ASP.NET Core .NET 10 + MongoDB.Driver,
serializador `JsonNamingPolicy.SnakeCaseLower`). `CasesController` expone
`GET /api/cases` (lista), `GET /api/cases/{id}`, create/update, subida,
generación, descargas; **no hay ningún endpoint de agregación ni de
estadísticas**. `CaseService.ListAsync(officerDni)` →
`ICaseRepository.ListByOfficerAsync(officerDni)` filtra **siempre** por el DNI
del perito logueado (`Officer.Dni`), con índice ascendente por `Officer.Dni`
(`MongoRepository.EnsureIndexAsync`). El `Case` tiene `CreatedAt`
(`DateTime.UtcNow` al crear), `GeneratedAt` (`DateTime?`, se setea al completar)
y `Status` (enum `Draft/Generating/Completed/Error`).

**No existe un sistema de roles.** `Models/User.cs` tiene solo `Dni`, `Name` y
`Sigla` — no hay campo de rol ni jerarquía, y todo el acceso a casos pasa por
`LoadOwnedAsync` / `ListByOfficerAsync` filtrando por DNI. (El `client/`
declara un tipo `UserRole` para el ABM de usuarios administrables, que es otra
cosa: no cambia quién ve qué casos.) Esto **resuelve de entrada** la pregunta
"¿KPIs propios o globales?": hoy un perito solo puede ver y contar sus propios
casos; no hay forma de pedir casos de otros (ver D2).

### Qué es lo nuevo

1. Un panel de KPIs ampliado: total, desglose por los cuatro estados, tasa de
   completitud y **tiempo promedio de cierre** (promedio de `generated_at −
   created_at` sobre los casos completados — dato nuevo, antes no se mostraba).
2. Un **gráfico de tendencia temporal**: casos creados por mes en los últimos N
   meses (ver D4), con su eje y tooltips accesibles.
3. La decisión de dónde se calcula (cliente sobre los casos ya cargados, o
   endpoint de agregación nuevo) — ver D1.

### Relación con otras HU

- **`rediseno-dashboard` / `rediseno-dashboard-historial`** (serie de rediseño):
  redefine `DashboardStats` y las tarjetas de stat con tokens y look "xbox". Si
  esa parte ya está aprobada cuando esta HU se implemente, esta **parte de ese
  resultado** (mismas tarjetas `fx-card`, misma grilla) y agrega el desglose por
  estado, el KPI de tiempo de cierre y el gráfico. Esta HU **no** rediseña el
  historial, el wizard ni la captura: solo el bloque de analítica.
- **`zip-cifrado-real`**: sin relación (no toca estados ni fechas de caso).

---

## Criterios de aceptación

```gherkin
Feature: Panel de KPIs y tendencia temporal en /dashboard

  Background:
    Given un perito autenticado en "/dashboard"
    And el perito tiene casos propios cargados

  Scenario: Tarjetas de KPIs
    Then ve "Total de casos" con la cantidad de casos propios
    And ve el desglose por estado: "Borradores" (draft), "Generando" (generating),
        "Completados" (completed) y "Con error" (error), cada uno con su cantidad,
        ícono y etiqueta en español
    And ve "Tasa de completitud" como porcentaje de casos completados sobre el total
    And ve "Tiempo promedio de cierre" como el promedio de días/horas entre creación
        y generación de los casos completados
    And cada tarjeta usa tokens semánticos (éxito, advertencia, peligro, neutro),
        nunca el verde de acento para "completado" si eso rompe el AA, y siempre
        ícono + texto además del color

  Scenario: Tasa de completitud sin divisiones por cero
    Given el perito no tiene ningún caso
    Then "Tasa de completitud" muestra "—" o "0%" sin romper, y el panel muestra su
        estado vacío (ver más abajo)

  Scenario: Tiempo promedio de cierre sin casos completados
    Given el perito tiene casos pero ninguno completado
    Then "Tiempo promedio de cierre" muestra "—" (sin datos), no "0" ni "NaN"

  Scenario: Gráfico de tendencia de casos por mes
    Given el perito tiene casos creados en distintos meses
    Then ve un gráfico con un punto/barra por mes del rango (D4), cada uno con la
        cantidad de casos creados en ese mes
    And cada mes está etiquetado en español (p. ej. "ene", "feb"… o "ene 25")
    And los meses sin casos aparecen con valor 0 (el eje no se saltea meses)

  Scenario: Accesibilidad del gráfico
    Then el gráfico tiene un nombre accesible y una alternativa textual
        (tabla o lista equivalente, o aria con los valores) para lector de pantalla
    And los colores del gráfico cumplen contraste AA contra el fondo y no dependen
        solo del color para distinguir series
    And el gráfico es legible en modo claro y oscuro usando tokens --fx-*

  Scenario: Movimiento reducido
    Given prefers-reduced-motion: reduce
    Then las animaciones de entrada del gráfico y de los contadores no corren
        (o son instantáneas); no hay loops

  Scenario: Estado de carga
    Given el historial todavía está cargando
    Then el panel muestra un esqueleto o un indicador con texto ("Cargando
        estadísticas…"), no números a medias ni saltos bruscos

  Scenario: Estado vacío
    Given el perito no tiene ningún caso
    Then en vez de KPIs en cero y un gráfico vacío, ve un mensaje de bienvenida que
        invita a crear la primera inspección (coherente con el estado vacío del
        historial)

  Scenario: Responsive
    Given una pantalla de 360 px de ancho
    Then las tarjetas se apilan sin scroll horizontal y el gráfico se redimensiona
        o se vuelve desplazable sin romper el layout

  Scenario: Build
    Then "npx tsc --noEmit" (y "npm run build" si aplica) en client/ terminan sin
        errores
    And, si hay endpoint nuevo, "dotnet build" de Factum.Backend termina sin errores
```

---

## Datos que se registran

Esta HU **no crea ni modifica datos de negocio**: solo **lee y agrega** campos
que ya existen en cada `Case`. No cambia el modelo, no escribe en la base, no
agrega colecciones ni índices (los KPIs se calculan sobre el índice de
`Officer.Dni` que ya existe).

### KPIs mostrados y de dónde salen

| KPI | Cálculo | Fuente |
|---|---|---|
| Total de casos | `count(casos del perito)` | — |
| Borradores | `count(status == "draft")` | `status` |
| Generando | `count(status == "generating")` | `status` |
| Completados | `count(status == "completed")` | `status` |
| Con error | `count(status == "error")` | `status` |
| Tasa de completitud | `completados / total` (0 si total = 0) | `status` |
| Tiempo promedio de cierre | `promedio(generated_at − created_at)` sobre los completados **con `generated_at` presente**; "—" si no hay ninguno | `created_at`, `generated_at` |
| Tendencia por mes | `count(casos) agrupados por mes de created_at`, rango de D4, meses sin casos en 0 | `created_at` |

Notas de cálculo:
- **Tiempo promedio de cierre**: solo cuentan los casos `completed` que además
  tengan `generated_at` (los viejos/legacy pueden no tenerlo). Unidad de D3.
- **Zona horaria**: `created_at`/`generated_at` son UTC. El agrupamiento por mes
  y "este mes" dependen de D5 (UTC vs. hora local del navegador).

---

## Diseño UX/UI (`client/` — web)

Se integra en el bloque superior de `/dashboard` (modo historial), encima o en
lugar del `DashboardStats` actual, dentro del mismo ancho y grilla que ya usa la
página. Todo con tokens `--fx-*`, tipografía `text-fx-*`, íconos `lucide-react`;
sin hex ni paleta Tailwind cruda.

### Entrada / disparo
- Se calcula/pide al cargar el dashboard (junto con `loadHistory`). No hay acción
  del usuario para abrirlo: es parte del panel.

### Pantalla
1. **Fila de KPIs**: tarjetas `fx-card` en grilla responsiva (2 columnas en
   mobile, 4+ en `sm+`). Número en `text-fx-h1 tabular-nums`, etiqueta en
   `text-fx-label`, ícono en una insignia. Orden sugerido: Total (destacada) ·
   Completados (con % de completitud como subdato o anillo) · En proceso
   (draft + generating, o desglosado) · Tiempo promedio de cierre. El desglose
   fino por los 4 estados puede ir como chips/leyenda bajo "Total" o como
   tarjetas propias (lo afina la SDD; ver D6 para cuántas tarjetas).
2. **Gráfico de tendencia**: tarjeta `fx-card` a ancho completo debajo de los
   KPIs, con título ("Casos por mes"), el gráfico (línea o barras, ver D3) con
   eje X de meses en español y eje Y de cantidades enteras, y tooltip al
   hover/foco. Serie única (casos creados). Color de serie por token de acento o
   neutro con suficiente contraste.

### Estados de error y feedback
| Situación | Qué se ve |
|---|---|
| Cargando | Esqueleto de tarjetas + placeholder del gráfico, o indicador con "Cargando estadísticas…" (`aria-busy`) |
| Sin casos | Estado vacío con invitación a crear la primera inspección (sin gráfico vacío ni KPIs en cero sueltos) |
| Error al cargar (si D1 = endpoint) | `Message`/aviso con ícono + texto y opción de reintentar, sin romper el resto del dashboard |
| Sin completados | "Tiempo promedio de cierre" y "Tasa de completitud" muestran "—" / "0%" con un subtexto explicativo |

### Feedback / movimiento
- Entrada de contadores y del gráfico con animación corta (framer-motion, que ya
  está), desactivada con `prefers-reduced-motion`. Sin loops. El anillo de %
  actual (si se conserva) ya respeta `useReducedMotion`.

### Accesibilidad del gráfico
- Nombre accesible (`aria-label`/`<title>`), y una representación textual
  equivalente para lector de pantalla (tabla visualmente oculta con mes →
  cantidad, o `role="img"` con `aria-label` que resuma la tendencia). No
  apoyarse solo en color; tooltips alcanzables con teclado.

---

## Contrato compartido

**Depende de D1.** Dos variantes según se calcule en el cliente o haya endpoint
nuevo. En ambas, el backend serializa en **snake_case_lower**.

### Variante A (recomendada): sin endpoint nuevo
No hay contrato nuevo. El cliente agrega sobre los `Case` que ya trae
`GET /api/cases`, usando los campos existentes (casing JSON real):
`status` (`"draft" | "generating" | "completed" | "error"`), `created_at`
(ISO 8601 UTC), `generated_at` (ISO 8601 UTC u omitido). Ningún campo nuevo en
backend ni frontend.

### Variante B: endpoint de agregación nuevo
Si se opta por agregación en el servidor, el endpoint recomendado es
`GET /api/cases/stats` (nuevo método en `CasesController`, mismo `[Authorize]`,
filtrado por `Officer.Dni` como todo lo demás). Respuesta propuesta (nombres
**exactos**, snake_case_lower):

```jsonc
{
  "total": 0,
  "by_status": { "draft": 0, "generating": 0, "completed": 0, "error": 0 },
  "completion_rate": 0.0,              // 0..1; el cliente lo muestra como %
  "avg_close_seconds": null,          // null si no hay completados con generated_at
  "monthly": [                          // ordenado ascendente, un item por mes del rango
    { "month": "2026-01", "count": 0 } // month = "yyyy-MM" (UTC o local según D5)
  ]
}
```
En este caso el cliente declara el tipo equivalente en `client/src/lib/api.ts`
(p. ej. `CaseStats`) y un método `api.caseStats()`. Los nombres de arriba son la
propuesta a confirmar en la SDD si se elige B.

---

## Fuera de alcance

- KPIs o tendencias **de otros peritos** o globales de la cuenta: no hay sistema
  de roles ni endpoint para casos ajenos (ver D2).
- Cambiar el modelo `Case`, agregar campos, colecciones, índices nuevos o migrar
  datos.
- Filtros temporales interactivos avanzados (selector de rango arbitrario,
  comparar períodos): el rango se fija en D4.
- Exportar las estadísticas (CSV/PDF), programar reportes o enviarlos por mail.
- Otras dimensiones de analítica (por tipo de dispositivo, por tribunal, por
  tamaño de evidencia): no pedidas.
- Rediseño del historial, wizard o captura (lo cubre la serie `rediseno-*`).
- `agent-ui/` y el agente Tatana.
- Tests automatizados de regresión visual (no hay infraestructura en `client/`).

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- Leer `client/AGENTS.md` y la doc de Next 16 en `node_modules/next/dist/docs/`.
- Los datos para todos los KPIs y la tendencia **ya llegan** en `GET /api/cases`
  (`status`, `created_at`, `generated_at`): la Variante A no necesita backend.
- Si D1 = B, el endpoint reusa `ListByOfficerAsync`/`CaseService` y agrega en
  memoria o con un pipeline de agregación de Mongo; **no** toca documentos ni
  crea índices nuevos (el índice por `Officer.Dni` ya existe).
- Para el gráfico, la skill `ui-ux-pro-max` tiene criterios de tipo de chart;
  evaluar librería vs. SVG propio en D3. `framer-motion` ya está en `client/`.
- El agrupamiento por mes debe generar los meses del rango aunque no tengan
  casos (relleno con 0), para que el eje X no "salte".
- Reusar la grilla y los tokens del `DashboardStats` actual; si la serie
  `rediseno-dashboard` ya reescribió esas tarjetas, partir de esa versión.
- Skills obligatorias de frontend: `ui-ux-pro-max`, `senior-frontend`,
  `3d-web-experience` (como criterio, sin 3D), `web-design-guidelines`.
- La HU no escribe en la base.

---

## Dudas para validar con el usuario

### D1. ¿Dónde se calculan los KPIs y la tendencia: cliente o endpoint nuevo?
- **A) En el cliente**, sobre los `Case` que `GET /api/cases` ya trae (igual que
  hoy `DashboardStats`). Sin backend.
- **B) Endpoint nuevo `GET /api/cases/stats`** que agrega del lado del servidor
  (contrato en "Variante B").
- **C) Extender `GET /api/cases`** para que devuelva también un bloque `stats`.
- **Recomendada: A.** El dashboard **ya carga todos los casos del perito** para
  el historial, y cada caso trae `status`, `created_at` y `generated_at`: las
  agregaciones son triviales en el cliente y no hay dato que el servidor tenga y
  el cliente no. Un endpoint nuevo (B) agrega superficie de API, un segundo
  fetch y un contrato que mantener, sin beneficio hasta que haya paginado del
  lado del servidor (que hoy no existe). C mezcla responsabilidades del listado.
  Si en el futuro el historial pasa a paginado server-side, se migra a B con el
  contrato ya definido aquí. **Decidir A/B cambia si hay trabajo de backend.**

### D2. ¿Los KPIs son del perito logueado o globales (por rol)?
- **A) Solo del perito logueado.**
- **B) Globales / por rol** (un supervisor ve todos los casos).
- **Recomendada: A** — y hoy es la **única** opción posible. No existe sistema de
  roles para casos: `Models/User.cs` solo tiene `Dni/Name/Sigla` y todo el
  acceso filtra por `Officer.Dni`. B requeriría primero una HU de roles y
  permisos sobre casos (modelo, endpoint, autorización). Lo anotamos como duda
  por si el objetivo real era un panel de supervisión, en cuyo caso esta HU se
  reencuadra o se parte.

### D3. ¿Qué tipo de gráfico y con qué herramienta?
- **A) SVG/CSS propio** (barras o línea simples) con tokens `--fx-*`.
- **B) Recharts** (React, declarativo, buen soporte de accesibilidad y responsive).
- **C) Chart.js** (canvas; requiere wrapper React y alternativa textual a mano
  para accesibilidad).
- **Recomendada: B (Recharts)** para un producto "premium": da ejes, tooltips,
  responsive y transiciones con poco código y se integra bien con React 19; se
  puede estilar con tokens vía CSS variables. Si se prefiere no sumar
  dependencia, **A** es viable para una sola serie (línea/barras de 6–12 puntos)
  y evita el canvas de C (que complica el modo oscuro y el lector de pantalla).
  **El `architect` confirma el encaje con Next 16 y el bundle.** Incluye también
  la decisión línea vs. barras (recomendado: barras para "casos por mes", más
  claras con pocos puntos).

### D4. ¿Rango temporal de la tendencia: fijo o configurable?
- **A) Fijo en 12 meses** (los últimos 12, incluyendo el actual).
- **B) Fijo en 6 meses.**
- **C) Configurable** por el usuario (3/6/12 meses).
- **Recomendada: A (12 meses fijos).** Da una vista de tendencia anual completa,
  es lo que suele esperarse de "casos por mes", y evita el costo de un control
  configurable (D4 C) que no se pidió. Si en 360 px 12 barras quedan apretadas,
  la SDD define si se scrollea horizontal o se reduce la densidad de etiquetas
  (no el rango). Configurable se puede sumar después sin romper nada.

### D5. ¿Mes y "promedio" en UTC o en hora local del navegador?
- **A) Hora local del navegador** (como hoy `DashboardStats`, que usa `new Date`
  del cliente para "este mes").
- **B) UTC** (como se guardan `created_at`/`generated_at`).
- **Recomendada: A.** El `DashboardStats` actual ya agrupa "este mes" en hora
  local, así que A mantiene coherencia y es lo que el perito espera ("mis casos
  de octubre" = octubre de su reloj). El desfase UTC↔local solo afecta casos
  creados cerca de medianoche de fin de mes; es aceptable para un panel de
  tendencia. Si D1 = B (endpoint), el servidor tendría que agrupar en UTC o
  recibir el offset del cliente: otro motivo para preferir A + cálculo en el
  cliente (D1 A).

### D6. ¿Cuántas tarjetas de KPI y cómo se muestra el desglose por estado?
- **A) 4 tarjetas** (Total, Completados+%, En proceso = draft+generating, Tiempo
  promedio de cierre) y el desglose fino de los 4 estados como leyenda/chips.
- **B) 6 tarjetas** (Total, Borradores, Generando, Completados, Con error,
  Tiempo promedio) + tasa de completitud como subdato de Completados.
- **Recomendada: A.** Mantiene la grilla de 4 que ya existe y no la satura en
  mobile; el estado "error" suele ser raro y "generating" transitorio, así que
  verlos como chips bajo el total es suficiente, dejando una tarjeta propia para
  el KPI nuevo de valor (tiempo de cierre). B es más explícito pero 6 tarjetas
  se apilan mucho en 360 px. La SDD puede ajustar las etiquetas exactas.

### D7. "Tiempo promedio de cierre": ¿qué unidad y sobre qué casos?
- Unidad: **A)** días con un decimal ("2,3 días") · **B)** formato humano
  ("1 d 4 h") · **C)** horas.
- Casos incluidos: solo `completed` con `generated_at` presente (los legacy sin
  `generated_at` se excluyen del promedio).
- **Recomendada: unidad A (días con un decimal)** por ser la escala típica de un
  cierre pericial y la más legible en una tarjeta; "—" cuando no hay ningún
  completado con fecha. Se confirma que la métrica es `generated_at −
  created_at` (tiempo desde que se abrió el caso hasta que se generó el
  informe), no otra definición de "cierre".
