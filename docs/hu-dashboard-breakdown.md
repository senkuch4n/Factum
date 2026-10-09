# HU: Desglose analítico (breakdown) de inspecciones por dimensión

**Slug:** `dashboard-breakdown`
**Apps afectadas:** `server/` (API `Factum.Backend`) y `client/` (Next.js 16). El
agente Tatana y `agent-ui/` quedan fuera.
**Serie:** complementa a la HU hermana `dashboard-kpis-tendencias` (KPIs y
tendencias temporales). Comparten origen de datos y, probablemente, endpoint de
agregación (ver Contrato compartido y D2).

**Como** perito u operador que administra muchas inspecciones
**quiero** ver un desglose de mis casos por dimensión (tipo de dispositivo
Android/iOS, estado, y por carátula/ámbito de la causa) y poder filtrarlo
**para que** entienda de un vistazo la composición de mi trabajo —cuántos casos
por plataforma, cuántos cerrados vs. en proceso, sobre qué causas— sin tener que
recorrer el historial caso por caso.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-09)

- **`/dashboard` (`client/src/app/dashboard/page.tsx`)** es la única pantalla del
  panel. En modo historial carga **la lista completa de casos del perito** con
  `api.listCases()` (una sola llamada a `GET /api/cases`, hook `useAuth`) y la pasa
  a `DashboardStats` y a `CaseHistory`.
- **`client/src/components/dashboard/DashboardStats.tsx`** ya calcula métricas
  **en el cliente** sobre ese arreglo: Total, Este mes, Completadas (con %) y En
  proceso. No hay ningún endpoint de agregación: todo sale de contar y filtrar el
  arreglo `cases` en memoria. Es el precedente directo del breakdown.
- **Backend:** `GET /api/cases` (`CasesController.List`) devuelve `{ cases }` con
  **todos** los casos del DNI del token (`CaseService.ListAsync` →
  `ListByOfficerAsync`). No existe ningún endpoint `stats`, `breakdown` ni de
  agregación (grep confirmado: cero coincidencias de `breakdown`/`/stats` en
  `Factum.Backend`). El backend serializa en **snake_case_lower**
  (`JsonNamingPolicy.SnakeCaseLower`).
- **Dimensiones disponibles por caso** (modelo `Case` + `DeviceInfo`, ya en el JSON
  que recibe el cliente, tipo `Case` en `client/src/lib/api.ts`):
  - `device.platform`: `"android"` | `"ios"` (default `"android"`). **Dato sólido**
    y siempre presente.
  - `status`: `"draft"` | `"generating"` | `"completed"` | `"error"` (enum
    `CaseStatus`, default `Draft`). **Dato sólido.**
  - `caratula` (string, puede estar vacío en casos previos al informe pericial,
    `schema_version = 0`).
  - `ambito_causa` (string libre; ejemplos reales en tests: `"Civil"`). Es lo más
    cercano a "jurisdicción/fuero" que hay en el modelo. **No hay un campo
    `jurisdiccion` propio** ni un catálogo cerrado: es texto libre del formulario
    del paso 2.
  - `tipo_causa`, `tipo_dispositivo`, `device.manufacturer`, `device.model`,
    `created_at`: también disponibles, candidatos secundarios de dimensión.
- **Filtros existentes:** `CaseHistory` ya filtra el historial **en el cliente**
  (buscador por número/carátula/partes/titular/DNI/equipo/IMEI y rango de fechas por
  `created_at`). Esos filtros son de presentación del listado, no tocan la API.
- **HU hermana `dashboard-kpis-tendencias`:** mencionada en el brief; a la fecha
  **no tiene artefactos en el repo** (`docs/` ni `Refactorizaciones/`). El contrato
  del endpoint de agregación —si se crea uno compartido— se define en coordinación
  con esa HU (ver D2).

### Por qué hace falta

El dashboard de hoy responde "cuántos casos tengo y cuántos cerré", pero no "de qué
están hechos": qué proporción es iOS vs. Android, cuántos quedaron en borrador o en
error, sobre qué carátulas/ámbitos se concentra el trabajo. Esa lectura analítica es
el objetivo del breakdown, complementando los KPIs/tendencias de la HU hermana.

### Qué es lo nuevo

1. Un bloque de **desglose** en `/dashboard` (modo historial) que muestra la
   distribución de los casos del perito por dimensión seleccionable.
2. La capacidad de **filtrar** ese desglose (al menos por rango de fechas y, según
   D3, por las propias dimensiones).
3. (Según D2) posiblemente un endpoint de agregación nuevo o la extensión del de la
   HU hermana. Si no, cálculo en el cliente como ya hace `DashboardStats`.

---

## Criterios de aceptación

```gherkin
Feature: Desglose analítico de inspecciones en /dashboard

  Background:
    Given un usuario autenticado en "/dashboard" (modo historial)
    And el perito tiene inspecciones cargadas con distintas plataformas, estados y carátulas

  Scenario: Desglose por tipo de dispositivo
    Then se ve un desglose "Por tipo de dispositivo" con Android e iOS
    And cada categoría muestra su cantidad y su porcentaje sobre el total filtrado
    And la suma de las categorías es igual al total de inspecciones del conjunto filtrado

  Scenario: Desglose por estado
    Then se ve un desglose "Por estado" con Completadas, En proceso (draft+generating) y con Error
    And el agrupamiento de estados coincide con el criterio de DashboardStats (completadas vs. pendientes)
    And cada categoría usa un color de token semántico, nunca solo color para distinguir (lleva etiqueta y número)

  Scenario: Desglose por carátula o ámbito de la causa
    Given la dimensión elegida es carátula (o ámbito, según D1)
    Then se ven las categorías con más casos primero
    And los casos sin ese dato se agrupan en "Sin especificar"
    And si hay muchas categorías se muestran las primeras N y el resto se agrupa en "Otras" (N según la SDD)

  Scenario: Cambiar la dimensión del desglose
    When el usuario elige otra dimensión en el selector
    Then el desglose se actualiza sin recargar la página y anuncia el cambio (aria-live)
    And el selector indica la dimensión activa (aria-pressed o equivalente)

  Scenario: Filtrar el desglose por fecha
    When el usuario aplica un rango de fechas
    Then el desglose recalcula sobre las inspecciones cuya created_at cae en el rango
    And el total y los porcentajes se ajustan al conjunto filtrado

  Scenario: Relación entre los filtros del desglose y el historial (según D3)
    When el usuario aplica un filtro del desglose
    Then el historial de casos se comporta según lo decidido en D3 (compartido o independiente)

  Scenario: Sin datos
    Given el perito no tiene inspecciones (o ninguna cae en el filtro)
    Then el bloque de desglose muestra un estado vacío con texto, sin gráficos rotos ni divisiones por cero

  Scenario: Accesibilidad y tema
    Then cada categoría es legible en modo claro y oscuro con contraste AA (>= 4.5:1 texto)
    And toda la información del gráfico está disponible como texto (etiqueta + cantidad + porcentaje), no solo visual
    And el bloque es operable con teclado y tiene nombres accesibles en español

  Scenario: Movimiento reducido
    Given prefers-reduced-motion: reduce
    Then las barras/donut no animan su entrada en loop; a lo sumo aparecen sin transición

  Scenario: Build
    Then "npx tsc --noEmit" y "npm run build" en client/ pasan
    And si hay cambios de backend, "dotnet build" de Factum.Backend pasa
```

---

## Datos que se registran

No aplica: la HU **no crea ni modifica datos de negocio**. Solo lee y agrega datos
ya existentes del caso. No hay colección, documento, DTO de escritura ni mensaje de
WebSocket nuevo. La restricción de datos de desarrollo de `AGENTS.md` aplica igual:
el implementador no toca documentos de negocio.

Dimensiones que lee (todas ya presentes en el `Case`):

| Dato | Siempre presente | Uso en el breakdown |
|---|---|---|
| `device.platform` | Sí (`android`/`ios`) | Dimensión "tipo de dispositivo" |
| `status` | Sí (enum) | Dimensión "estado" (agrupada como DashboardStats) |
| `caratula` | No (vacío en `schema_version = 0`) | Dimensión "carátula"; vacío → "Sin especificar" |
| `ambito_causa` | No (texto libre) | Dimensión "ámbito/jurisdicción" (ver D1); vacío → "Sin especificar" |
| `created_at` | Sí | Filtro por rango de fechas |

---

## Diseño UX/UI (`client/` — web)

Ubicación: en `/dashboard`, **modo historial**, debajo de `DashboardStats` (o junto a
los KPIs de la HU hermana si se montan en el mismo bloque; lo coordina la SDD). Usa el
sistema de diseño nuevo: tokens `--fx-*`, tipografía `text-fx-*`, tarjetas `.fx-card`,
coherente con `DashboardStats` y el `rediseno-dashboard`.

### Composición propuesta (sujeta a D4)

- **Encabezado del bloque:** título `text-fx-h2` "Desglose" + un selector de dimensión
  (SelectButton/tabs: "Tipo de dispositivo" · "Estado" · "Carátula/Ámbito") y el filtro
  de rango de fechas (reutiliza el `Calendar` de rango del historial).
- **Visualización recomendada (D4 A):** **barras horizontales con etiqueta + número +
  porcentaje** por categoría. Es la más legible para muchas categorías de texto
  (carátula/ámbito), escala bien, y la cifra exacta siempre está a la vista (no depende
  de leer un ángulo). Para dimensiones de pocas categorías con total acotado (tipo de
  dispositivo, estado) se puede sumar un **donut/anillo** compacto como refuerzo visual
  —el anillo de `DashboardStats` ya marca el estilo— pero el dato textual manda.
- **Tabla como fallback accesible:** la lista de barras es, a la vez, la tabla de datos
  (cada fila: etiqueta, cantidad, %), así que el contenido es texto desde el vamos.
- **Colores:** estado usa tokens semánticos (`--fx-success` completadas, `--fx-warning`
  en proceso, `--fx-danger` error); tipo de dispositivo y carátula usan neutros/acento
  con distinción por etiqueta, nunca solo por color.

### Estados

| Situación | Qué se ve |
|---|---|
| Carga | Skeleton o spinner con "Cargando desglose…" (si hay fetch propio) |
| Vacío (sin casos / filtro sin resultados) | Mensaje estático "Sin datos para mostrar", sin gráfico |
| Muchas categorías | Top N + fila "Otras" agrupada (N en la SDD), con tooltip/aria del detalle |
| Dato ausente | Categoría "Sin especificar" para `caratula`/`ambito_causa` vacíos |

### Responsive y movimiento

- 360 px sin scroll horizontal; las barras se apilan a ancho completo en mobile.
- Entrada de barras/anillo con transición corta (`--fx-dur-base`), desactivada con
  `prefers-reduced-motion`. Sin loops decorativos.

---

## Fuera de alcance

- KPIs y tendencias temporales (serie mensual, evolución): son de la HU hermana
  `dashboard-kpis-tendencias`.
- Crear un campo `jurisdiccion`/`fuero` propio, un catálogo cerrado de ámbitos o
  normalizar `ambito_causa`: esta HU usa el dato tal como está (ver D1).
- Exportar el desglose (CSV/PDF), imprimirlo o compartirlo.
- Breakdown de casos de **otros** peritos o agregado global del equipo: cada perito ve
  solo lo suyo (el endpoint de casos ya filtra por DNI del token).
- Paginación del lado del servidor del historial o cambios en los filtros existentes
  de `CaseHistory` (salvo lo que decida D3 sobre compartir el filtro).
- `agent-ui/` (Electron) y el agente Tatana.
- Tests automatizados de regresión visual (no hay infraestructura en `client/`).

---

## Contrato compartido (client ↔ server)

**Depende de D2.** Dos escenarios posibles; el `architect` fija el definitivo en
coordinación con la HU hermana.

### Escenario A (recomendado) — sin endpoint nuevo: cálculo en el cliente

No hay contrato nuevo. El breakdown se calcula en el cliente sobre el arreglo que ya
devuelve `GET /api/cases` (`{ cases: Case[] }`), exactamente como hace hoy
`DashboardStats`. Campos leídos del `Case` (casing JSON real, snake_case):

| Campo JSON | Tipo | De dónde sale |
|---|---|---|
| `device.platform` | `"android" \| "ios"` | `DeviceInfo.Platform` |
| `status` | `"draft" \| "generating" \| "completed" \| "error"` | `CaseStatus` (enum serializado en minúscula) |
| `caratula` | `string` | `Case.Caratula` |
| `ambito_causa` | `string` | `Case.AmbitoCausa` |
| `created_at` | `string` (ISO) | `Case.CreatedAt` |

Sin cambios de backend. El `tsc`/`build` de `client/` es la única verificación.

### Escenario B — endpoint de agregación nuevo/compartido

Si se opta por agregar en el servidor (p. ej. para no mandar todos los casos, o para
compartir con la HU hermana), el nombre tentativo es `GET /api/cases/breakdown`
(o un `GET /api/cases/stats` compartido con `dashboard-kpis-tendencias`). Query y
respuesta en **snake_case_lower**. Propuesta a cerrar por el `architect`:

- Query: `from=YYYY-MM-DD&to=YYYY-MM-DD` (rango sobre `created_at`, opcional),
  `dimension=platform|status|caratula|ambito_causa`.
- Respuesta (ejemplo):
  ```json
  {
    "dimension": "platform",
    "total": 42,
    "buckets": [
      { "key": "android", "label": "Android", "count": 30 },
      { "key": "ios", "label": "iOS", "count": 12 }
    ]
  }
  ```
- Filtrado por DNI del token (igual que `GET /api/cases`). El `label` lo puede resolver
  el cliente si el `key` es estable; definirlo en la SDD para no duplicar textos.

Este escenario agrega una tarea de backend (controller + servicio + DTO) y su casing
exacto se documenta en el **Contrato compartido** de la SDD.

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- Reutilizar el patrón de `DashboardStats` (cálculo sobre el arreglo de casos) y el
  `Calendar` de rango ya usado en `CaseHistory` para el filtro de fechas.
- Coordinar con la SDD/implementación de `dashboard-kpis-tendencias`: si esa HU crea un
  endpoint de agregación, esta se cuelga de él (Escenario B); si no, Escenario A.
- No introducir una librería de gráficos pesada sin necesidad: barras y anillo se
  resuelven con CSS/SVG y tokens (el `ProgressRing` de `DashboardStats` es precedente).
- Backend (solo si D2 = B): `CasesController` + `CaseService`, respetando snake_case y
  el filtrado por DNI del token. No tocar documentos de negocio.
- Skills obligatorias del frontend (`ui-ux-pro-max`, `senior-frontend`,
  `3d-web-experience` como criterio sin agregar 3D, `web-design-guidelines`).
- Leer `client/AGENTS.md` y la doc de Next 16 antes de tocar código del cliente.

---

## Dudas para validar con el usuario

### D1. ¿Qué dimensiones entran, y qué hacemos con "jurisdicción/causa"?
El brief pide tipo de dispositivo, estado y "carátula/causa o jurisdicción".
- **A) Tipo de dispositivo + Estado + Carátula**, y una cuarta opcional **Ámbito de la
  causa** (`ambito_causa`) rotulada como "Ámbito/fuero", tratando el texto libre tal
  cual (agrupando vacíos en "Sin especificar" y top N + "Otras").
- **B) Solo tipo de dispositivo + estado + carátula** (dejar afuera ámbito/jurisdicción
  por ser texto libre poco normalizado).
- **C) Agregar un campo/catálogo de jurisdicción nuevo** para tener una dimensión
  limpia.
- **Recomendada: A.** Tipo de dispositivo y estado son datos sólidos y cerrados.
  `ambito_causa` es lo único parecido a "jurisdicción/fuero" que existe en el modelo
  (texto libre, ej. "Civil"); sirve como dimensión útil tratándolo como viene, sin
  inventar normalización. C es una HU aparte de modelado de datos, fuera de este alcance
  (y el brief dice solo lectura sobre el código). Carátula suele ser casi única por caso
  (número de causa + partes), por lo que como dimensión tiende a muchas categorías de
  conteo 1: entra, pero el valor analítico real está en plataforma/estado/ámbito.

### D2. ¿Endpoint propio, endpoint compartido o cálculo en el cliente?
- **A) Cálculo en el cliente** sobre el `GET /api/cases` que ya se trae (como
  `DashboardStats`). Sin backend nuevo.
- **B) Endpoint propio `GET /api/cases/breakdown`.**
- **C) Extender/compartir el endpoint de agregación de `dashboard-kpis-tendencias`**
  (si esa HU lo crea).
- **Recomendada: A.** El dashboard ya descarga todos los casos del perito y ya calcula
  métricas en memoria; el volumen por perito es chico (es su propio trabajo), así que un
  endpoint de agregación no aporta hoy y suma superficie de API, DTOs y contrato que
  mantener. Si más adelante el volumen crece o la HU hermana efectivamente crea un
  `stats` compartido, se migra a C sin cambiar la UX. **B/C se justifican solo si el
  usuario anticipa muchos casos por perito o quiere la agregación del lado servidor
  desde ya.** Esta decisión define si la HU toca backend o es solo `client/`.

### D3. ¿Los filtros del breakdown afectan también el historial de casos?
- **A) Independientes:** el breakdown tiene su propio filtro de fecha/dimensión y no
  altera la lista de `CaseHistory` (que mantiene sus filtros actuales).
- **B) Compartidos:** aplicar un filtro (o clic en una categoría del breakdown) también
  filtra el historial de abajo (p. ej. clic en "iOS" deja el historial solo con casos
  iOS).
- **Recomendada: A.** Mantiene cada bloque con una responsabilidad clara y no cambia el
  comportamiento ya existente de `CaseHistory` (menos riesgo de regresión). B es una
  interacción atractiva ("drill-down": clic en categoría → filtra lista), pero conviene
  pedirla como mejora posterior una vez que el breakdown base esté validado, porque
  acopla dos componentes y multiplica los casos de prueba.

### D4. Visualización: ¿barras, donut o tabla?
- **A) Barras horizontales** (etiqueta + número + %), con donut/anillo opcional como
  refuerzo en las dimensiones de pocas categorías (tipo de dispositivo, estado).
- **B) Donut/torta** como visual principal.
- **C) Solo tabla** (etiqueta + cantidad + %).
- **Recomendada: A.** Las barras escalan a muchas categorías (carátula/ámbito), muestran
  la cifra exacta sin depender de leer un ángulo, y la lista de barras ya es la tabla de
  datos accesible (texto desde el vamos). El donut funciona para 2–4 categorías pero se
  vuelve ilegible con muchas y obliga a una leyenda aparte; lo sumamos solo como adorno
  donde ayuda. C es lo más accesible pero visualmente pobre para un "desglose" que se
  quiere leer de un vistazo; A da lo mejor de ambos.
