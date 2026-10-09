# SDD: Desglose analítico (breakdown) de inspecciones por dimensión

**Slug:** `dashboard-breakdown`
**HU:** `docs/hu-dashboard-breakdown.md` (validada; decisiones aplicadas abajo)
**Apps que toca:** `backend (API Factum.Backend): sí` · `backend (Tatana): no` ·
`client: sí` · `agent-ui: no`

---

## Resumen funcional

En `/dashboard` (modo historial), debajo de `DashboardStats`, se agrega un bloque
**"Desglose"** que muestra la distribución de los casos del perito logueado por una
dimensión seleccionable (tipo de dispositivo / estado / carátula / ámbito de la
causa), con filtro propio de rango de fechas. Los datos salen de un **endpoint de
agregación nuevo en el backend** (`GET /api/cases/breakdown`), filtrado por el DNI
del token igual que todo lo demás. La visualización son **barras horizontales**
(Recharts) con etiqueta + cantidad + porcentaje por categoría; esa misma lista es la
representación textual accesible. Los valores vacíos se agrupan en "Sin especificar"
y, si hay muchas categorías, se muestran las top N y el resto en "Otras".

---

## Decisiones técnicas (ya validadas por el usuario — se aplican tal cual)

- **D1 = A.** Dimensiones: **tipo de dispositivo** (`device.platform`), **estado**
  (`status`), **carátula** (`caratula`) y **ámbito de la causa** (`ambito_causa`)
  tal como viene (texto libre). Vacíos → bucket "Sin especificar". Top N + "Otras".
- **D2 = B.** **Endpoint de agregación en el backend**:
  `GET /api/cases/breakdown?dimension=…&from=…&to=…`. Respuesta
  `{dimension, total, buckets:[{key,label,count}]}`, snake_case_lower, filtrado por
  `Officer.Dni`.
- **D3 = A.** Los filtros del breakdown son **independientes** del historial
  (`CaseHistory` conserva sus filtros actuales sin tocarse).
- **D4 = A.** **Barras horizontales** (Recharts) con etiqueta + número + %. Donut
  opcional como refuerzo solo en dimensiones de pocas categorías (platform, status);
  el dato textual manda. No bloqueante: si se omite el donut, la HU se cumple igual.

### D5 (nueva, cerrada por el arquitecto). Top N y zona horaria del filtro de fechas

- **D5a — Top N = 8.** Para `caratula` y `ambito_causa` (potencialmente muchas
  categorías), el backend ordena por `count` descendente y agrupa todo lo que pase de
  las 8 primeras en un bucket `{ key: "__otras__", label: "Otras", count: … }`. Para
  `platform` (2) y `status` (4) no hay recorte: se devuelven todos los buckets fijos.
  "Sin especificar" **no** se cuenta dentro del Top N: va siempre como bucket propio
  si tiene `count > 0`, ordenado por su `count` como cualquier otro.
- **D5b — Filtro de fechas en UTC, borde inclusivo.** `from`/`to` son `yyyy-MM-dd`.
  El backend filtra `created_at` (UTC) con `from 00:00:00Z <= created_at <
  (to + 1 día) 00:00:00Z`, de modo que `to` es **inclusivo** por día. Es coherente
  con que `CreatedAt` se guarda en UTC (`DateTime.UtcNow`). El cliente manda las
  fechas como las elige el usuario en el `Calendar` (sin convertir a UTC): el desfase
  de zona solo afecta casos creados cerca de medianoche, aceptable para un panel
  analítico. (La HU hermana `dashboard-kpis-tendencias` agrupa por mes en hora local
  del navegador porque calcula en el cliente; acá el cálculo es server-side y el filtro
  es por día en UTC. No hay conflicto: son cálculos distintos.)

---

## COORDINACIÓN con `dashboard-kpis-tendencias` (leer antes de implementar)

Esta HU y `dashboard-kpis-tendencias` **comparten `CasesController` y `CaseService`**.
No son worktrees independientes. Reglas de convivencia:

1. **Dueña del scaffolding de agregación = `dashboard-kpis-tendencias`.** Esa HU crea
   (si se implementa con endpoint, su "Variante B") el método `GET /api/cases/stats`
   en `CasesController`, el método de servicio en `CaseService` y, probablemente, un
   `Breakdown/StatsRepository` o un método nuevo en `ICaseRepository`/`MongoRepository`.
2. **Orden recomendado: backend primero, y la HU hermana antes o junto con esta.** Si
   `dashboard-kpis-tendencias` se implementa primero, esta HU **reusa** lo que aquella
   dejó (interfaz de servicio, inyección, patrón de agregación) y solo agrega el método
   `BreakdownAsync` + el action `GET /api/cases/breakdown`. Si esta HU va primero,
   crea el scaffolding mínimo y deja la HU hermana reusándolo.
3. **Ambas tocan los mismos archivos** (`CasesController.cs`, `CaseService.cs`,
   `ICaseService`, `api.ts`, `dashboard/page.tsx`). Si se desarrollan en ramas
   paralelas (`feat/dashboard-breakdown` y `feat/dashboard-kpis-tendencias`), **habrá
   conflicto**: el segundo PR que llegue a `develop` **rebasa y resuelve** (regla de
   `AGENTS.md`, "Ramas y flujo"). La forma más segura es implementarlas **en secuencia
   sobre la misma rama** o mergear la hermana a `develop` antes de abrir esta.
4. **Contrato consistente:** el endpoint de esta HU es un action **hermano** de
   `GET /api/cases/stats`, en el mismo controlador, con el mismo `[Authorize]` y el
   mismo filtrado por `Officer.Dni`. Ninguna de las dos modifica el modelo `Case`,
   escribe en Mongo ni crea índices (ambas se apoyan en el índice `Officer.Dni` que ya
   existe, `MongoRepository.EnsureIndexAsync`).

> Nota: a la fecha (2026-10-09) `Refactorizaciones/dashboard-kpis-tendencias.md` **no
> existe**; la HU hermana solo tiene RDD. El implementador que tome primera cualquiera
> de las dos debe avisar al orquestador para que coordine el reparto en una sola rama.

---

## Modelo de datos

**No cambia.** La HU solo lee y agrega campos ya existentes del `Case`:

| Campo C# | Dónde | Casing JSON | Uso en el breakdown |
|---|---|---|---|
| `Case.Device.Platform` | `Models/DeviceInfo.cs` | `device.platform` | dimensión `platform` (`"android"` default, `"ios"`) |
| `Case.Status` | `Models/Case.cs` (enum `CaseStatus`) | `status` | dimensión `status` (`draft`/`generating`/`completed`/`error`) |
| `Case.Caratula` | `Models/Case.cs` | `caratula` | dimensión `caratula`; `""` → "Sin especificar" |
| `Case.AmbitoCausa` | `Models/Case.cs` | `ambito_causa` | dimensión `ambito_causa`; `""` → "Sin especificar" |
| `Case.CreatedAt` | `Models/Case.cs` (UTC) | `created_at` | filtro `from`/`to` |

- **Sin colección, índice ni documento nuevo.** Regla dura de datos de desarrollo de
  `AGENTS.md`: el implementador **no toca documentos de negocio**, no hace `$out`/
  `$merge` ni ninguna etapa de escritura.
- **Compatibilidad con documentos viejos:** `schema_version = 0` puede tener
  `caratula`/`ambito_causa` vacíos → caen en "Sin especificar" (default `""` en el
  modelo). `platform` tiene default `"android"` en `DeviceInfo`, así que nunca falta.
  El enum `CaseStatus` tiene default `Draft`. Todo deserializa sin migración
  (`[BsonIgnoreExtraElements]`).

---

## Endpoint nuevo

### `GET /api/cases/breakdown`

- **Controlador:** `CasesController` (`server/src/Factum.Backend/Controllers/CasesController.cs`),
  action nuevo. Hereda `[ApiController]`, `[Route("api/cases")]`, `[Authorize]`,
  `[Produces("application/json")]`. Perito desde `HttpContext.Items["User"]` (patrón
  `private User Officer => …` ya presente).
- **Query params:**
  - `dimension` (**obligatorio**): uno de `platform` | `status` | `caratula` |
    `ambito_causa`. Valor fuera de ese set → `400` con `{ "error": "…" }`
    (usar el patrón `Result`/`this.ErrorResult(result)` con `ErrorKind.Validation`,
    igual que el resto del controlador — ver `Common/Result.cs` y `ResultHttpExtensions`).
  - `from` (opcional): `yyyy-MM-dd`. Fecha inválida → `400`.
  - `to` (opcional): `yyyy-MM-dd`. Fecha inválida → `400`. Si `to < from` → `400`.
- **Firma sugerida del action:**
  ```csharp
  [HttpGet("breakdown")]
  [ProducesResponseType<BreakdownResponse>(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> Breakdown(
      [FromQuery] string? dimension,
      [FromQuery] string? from,
      [FromQuery] string? to,
      CancellationToken ct)
  ```
  Parsear `from`/`to` con `DateTime.TryParseExact(…, "yyyy-MM-dd", CultureInfo.InvariantCulture,
  DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out …)` (el
  controlador ya importa `System.Globalization`). Devolver
  `result.IsSuccess ? Ok(result.Value) : this.ErrorResult(result)`.
- **Servicio:** método nuevo en `ICaseService` / `CaseService`
  (`server/src/Factum.Backend/Services/Cases/CaseService.cs`):
  ```csharp
  Task<Result<BreakdownResponse>> BreakdownAsync(
      string officerDni, string dimension, DateOnly? from, DateOnly? to,
      CancellationToken ct = default);
  ```
  - Trae los casos del perito. **Opción simple y suficiente (recomendada):** reusar
    `_repo.ListByOfficerAsync(officerDni, ct)` (ya existe, ya filtra por DNI, ya
    proyecta afuera `report_texts`/`zip_password`/`pending_generation`) y agregar
    **en memoria** (el volumen por perito es su propio trabajo; mismo criterio que el
    `DashboardStats` actual). Filtrar por `created_at` en UTC (D5b), agrupar por la
    dimensión, mapear vacíos a "Sin especificar", ordenar por `count` desc, aplicar
    Top N = 8 para `caratula`/`ambito_causa` (D5a) acumulando el resto en "Otras".
  - **Opción pipeline Mongo (si la HU hermana ya montó un repo de agregación):**
    reusar ese camino (`$match` por `Officer.Dni` + rango, `$group`, `$sort`), **solo
    lectura**, sin `$out`/`$merge`. No es obligatorio para esta HU; la agregación en
    memoria cumple el criterio de aceptación.
  - Validación de `dimension` dentro del servicio (devolver `Result.Failure` con
    `ErrorKind.Validation` si no está en el set) para que el controlador quede fino.
- **DTO de respuesta:** nuevo en `server/src/Factum.Backend/DTOs/` (p. ej.
  `CaseStatsDtos.cs` compartido con la HU hermana, o `BreakdownDtos.cs`):
  ```csharp
  public sealed record BreakdownBucket(string Key, string Label, long Count);
  public sealed record BreakdownResponse(string Dimension, long Total, IReadOnlyList<BreakdownBucket> Buckets);
  ```
  Serializa en snake_case_lower (política global de `Program.cs`): `dimension`,
  `total`, `buckets`, y por bucket `key`, `label`, `count`.

### Labels que resuelve el backend (fuente única de verdad de los textos)

El `label` lo pone el backend para que el cliente no duplique traducciones:

| dimension | key | label |
|---|---|---|
| `platform` | `android` | `Android` |
| `platform` | `ios` | `iOS` |
| `status` | `draft` | `Borradores` |
| `status` | `generating` | `Generando` |
| `status` | `completed` | `Completados` |
| `status` | `error` | `Con error` |
| `caratula` | `<texto del caso>` | `<mismo texto>` |
| `caratula` | `__sin_especificar__` | `Sin especificar` |
| `caratula` | `__otras__` | `Otras` |
| `ambito_causa` | `<texto del caso>` | `<mismo texto>` |
| `ambito_causa` | `__sin_especificar__` | `Sin especificar` |
| `ambito_causa` | `__otras__` | `Otras` |

- Para `platform` y `status`, los **4/2 buckets fijos** se devuelven **siempre**,
  incluso con `count = 0`, en el orden del enum/catálogo de arriba (no se ordenan por
  count: así la UI no "salta"). Para `caratula`/`ambito_causa` solo se devuelven las
  categorías con `count > 0` (más "Sin especificar" y "Otras" si aplican), ordenadas
  por count desc.
- `total` = suma de `count` de todos los buckets = total de casos del perito dentro del
  filtro de fechas. Para `platform`/`status`, como se incluyen buckets en 0, la suma
  sigue dando el total (los 0 no suman). Garantiza el criterio Gherkin "la suma de las
  categorías es igual al total del conjunto filtrado".

---

## Contrato compartido (client ↔ server)

Endpoint: **`GET /api/cases/breakdown`**. Backend serializa en **snake_case_lower**
(`JsonNamingPolicy.SnakeCaseLower` + `JsonStringEnumConverter(SnakeCaseLower)` en
`Program.cs`). Enums ya salen en minúscula (`CaseStatus.Draft` → `"draft"`).

### Request (query string)

| Param | Tipo | Oblig. | Valores | Notas |
|---|---|---|---|---|
| `dimension` | string | sí | `platform` \| `status` \| `caratula` \| `ambito_causa` | fuera del set → 400 |
| `from` | string | no | `yyyy-MM-dd` | borde inferior inclusivo, UTC 00:00Z |
| `to` | string | no | `yyyy-MM-dd` | borde superior inclusivo por día (`< to+1día`) |

### Response `200` (snake_case_lower)

```jsonc
{
  "dimension": "platform",
  "total": 42,
  "buckets": [
    { "key": "android", "label": "Android", "count": 30 },
    { "key": "ios",     "label": "iOS",     "count": 12 }
  ]
}
```

Nombres **exactos** que viajan en el JSON: `dimension`, `total`, `buckets[]`,
`buckets[].key`, `buckets[].label`, `buckets[].count`.

### Archivos de cada lado

- **Server (DTO):** `server/src/Factum.Backend/DTOs/CaseStatsDtos.cs`
  (o `BreakdownDtos.cs`) — `BreakdownResponse` / `BreakdownBucket`.
- **Client (tipos + método):** `client/src/lib/api.ts`:
  ```ts
  export type BreakdownDimension = "platform" | "status" | "caratula" | "ambito_causa";
  export interface BreakdownBucket { key: string; label: string; count: number; }
  export interface CaseBreakdown { dimension: BreakdownDimension; total: number; buckets: BreakdownBucket[]; }

  // dentro del objeto `api`:
  async caseBreakdown(dimension: BreakdownDimension, from?: string, to?: string): Promise<CaseBreakdown> {
    const params = new URLSearchParams({ dimension });
    if (from) params.set("from", from);
    if (to) params.set("to", to);
    return request<CaseBreakdown>(`/api/cases/breakdown?${params.toString()}`);
  }
  ```
  (usa el helper `request<T>` existente, que ya adjunta el token y maneja errores).

Si la HU hermana define `client/src/lib/api.ts` → `CaseStats` + `api.caseStats()`, los
tipos del breakdown conviven sin solaparse (nombres distintos). Mantener ambos en el
mismo bloque del objeto `api`.

---

## Frontend (`client/` — Next.js 16)

> Leer `client/AGENTS.md` y la doc de Next 16 (`node_modules/next/dist/docs/`) antes de
> tocar código. Skills obligatorias: `ui-ux-pro-max` (antes del JSX final),
> `senior-frontend`, `3d-web-experience` (como criterio, sin agregar 3D),
> `web-design-guidelines` (autochequeo), `ui-styling` y animación donde aplique.

### Dependencia nueva: Recharts

- Agregar `recharts` a `client/package.json` (`npm install recharts`). Hoy **no** está
  instalada (verificado). Usar una versión compatible con React 19 / Next 16. Confirmar
  que `npm run build` pasa (SSR: el bloque de desglose es `"use client"`, los componentes
  de Recharts solo se montan en cliente; usar `ResponsiveContainer`).
- Estilar con tokens `--fx-*` vía CSS variables / `fill`/`stroke` leídos de variables,
  no hex crudo. Respetar modo claro/oscuro y contraste AA (>= 4.5:1 en texto).

### Componente nuevo: `client/src/components/dashboard/DashboardBreakdown.tsx`

- `"use client"`. Props mínimas: ninguna obligatoria (hace su propio fetch) **o**
  recibir un `defaultDimension`. Decisión del implementador; lo natural es que el
  componente administre su estado (dimensión activa + rango de fechas + fetch).
- **Estado interno:**
  - `dimension: BreakdownDimension` (default `"platform"`).
  - `from`/`to` (strings `yyyy-MM-dd`, propios — **independientes del historial**, D3).
  - `data: CaseBreakdown | null`, `loading`, `error`.
- **Fetch:** `api.caseBreakdown(dimension, from, to)` al montar y cada vez que cambie
  `dimension`/`from`/`to`. Cancelar el fetch anterior si cambia rápido (usar un
  `AbortController` o un flag de "stale"); manejar `ApiError` → estado de error con
  reintento.
- **Selector de dimensión:** tabs/segmented control ("Tipo de dispositivo" · "Estado" ·
  "Carátula" · "Ámbito"). La opción activa marcada con `aria-pressed` (o
  `role="tablist"`/`aria-selected`). Al cambiar, anunciar con `aria-live="polite"` el
  cambio de desglose (criterio Gherkin).
- **Filtro de fechas:** reutilizar el `Calendar selectionMode="range"` de PrimeReact tal
  como en `CaseHistory.tsx` (líneas ~245-261), con `toYMD` para producir `yyyy-MM-dd`.
  Es **su propio** Calendar; no toca el del historial.
- **Visualización (D4-A):** barras horizontales con Recharts
  (`BarChart layout="vertical"` dentro de `ResponsiveContainer`), eje Y = `label`,
  eje X = `count`. Cada barra muestra `label`, `count` y `%` (`count / total`). Los
  mismos datos van, además, en una **tabla/lista accesible** (ver abajo). Donut opcional
  solo para `platform`/`status` como refuerzo (no bloqueante).
- **Porcentaje:** `total > 0 ? Math.round((count / total) * 100) : 0`. Nunca dividir por
  cero. Con `total === 0`, no renderizar el gráfico: mostrar estado vacío.
- **Colores:** `status` usa tokens semánticos (`--fx-success` completados,
  `--fx-warning` generando/borradores, `--fx-danger` error, neutro el resto);
  `platform`/`caratula`/`ambito_causa` usan neutro/acento con distinción por etiqueta,
  **nunca solo color** (cada barra lleva su label + número).

### Accesibilidad (criterios Gherkin)

- Toda la info del gráfico disponible como texto: una tabla (o lista `<dl>`) con
  `etiqueta → cantidad → %`, visible o `sr-only`, equivalente a las barras. El gráfico
  Recharts lleva `role="img"` + `aria-label` que resuma, o se marca `aria-hidden` si la
  tabla ya expone todo.
- Operable con teclado; nombres accesibles en español. `aria-live` para el cambio de
  dimensión.
- `prefers-reduced-motion: reduce` → sin animación de entrada de barras ni loops
  (Recharts: `isAnimationActive={!reduceMotion}` usando `useReducedMotion` de
  framer-motion, ya disponible). Transición corta (`--fx-dur-base`) cuando sí hay
  movimiento.

### Estados

| Situación | Qué se ve |
|---|---|
| Cargando (`loading`) | Skeleton/placeholder con "Cargando desglose…" y `aria-busy` |
| Vacío (`total === 0`) | Mensaje "Sin datos para mostrar", sin gráfico ni divisiones por cero |
| Error de fetch | `FxBanner` tono error + botón reintentar, sin romper el resto del dashboard |
| Muchas categorías | El backend ya devuelve Top 8 + "Otras"; la UI solo las pinta |
| Dato ausente | Bucket "Sin especificar" ya viene del backend |

### Montaje en la página

- `client/src/app/dashboard/page.tsx`, dentro del bloque de historial, **debajo de**
  `DashboardStats` (línea ~803). Montar solo si `historyCases.length > 0` (mismo criterio
  que `DashboardStats`), para no pedir el breakdown cuando el perito no tiene casos:
  ```tsx
  {historyCases.length > 0 && <DashboardStats cases={historyCases} />}
  {historyCases.length > 0 && <DashboardBreakdown />}
  ```
  El componente hace su propio fetch (no recibe `historyCases`): el breakdown es
  server-side (D2-B), no se calcula sobre el array en memoria.
- Responsive: 360 px sin scroll horizontal; barras a ancho completo; apilado en mobile.
- **No tocar** `CaseHistory.tsx` ni sus filtros (D3-A).

---

## Decisión de dónde calcular el porcentaje y Top N (aclaración)

- **Conteo + Top N + "Sin especificar" + "Otras" + labels:** los resuelve el **backend**
  (fuente única). El cliente **no** reimplementa esa lógica.
- **Porcentaje:** lo calcula el **cliente** a partir de `count`/`total` (evita mandar
  floats redondeados y mantiene el `count` exacto).

---

## Checklist atómico

### Backend (`implementer-backend`)

- [ ] (Coordinación) Confirmar con el orquestador si `dashboard-kpis-tendencias` ya
      creó el scaffolding de agregación en `CasesController`/`CaseService`. Si sí,
      **reusarlo**; si no, crear el mínimo acá.
- [ ] Crear DTO `BreakdownResponse` + `BreakdownBucket` en
      `server/src/Factum.Backend/DTOs/` (snake_case por política global).
- [ ] Agregar `BreakdownAsync(string officerDni, string dimension, DateOnly? from,
      DateOnly? to, CancellationToken)` a `ICaseService` y a `CaseService`.
- [ ] Implementar la agregación **en memoria** sobre `_repo.ListByOfficerAsync`
      (solo lectura): filtro `created_at` UTC (D5b), agrupar por dimensión, mapear
      vacíos a "Sin especificar", labels de la tabla, Top 8 + "Otras" para
      `caratula`/`ambito_causa` (D5a), buckets fijos en orden para `platform`/`status`.
- [ ] Validar `dimension` (set cerrado) → `Result.Failure(ErrorKind.Validation)`.
- [ ] Agregar el action `[HttpGet("breakdown")] Breakdown(…)` a `CasesController`,
      parseando `from`/`to` con `TryParseExact` (yyyy-MM-dd, UTC) y devolviendo
      `Ok(result.Value)` / `this.ErrorResult(result)`.
- [ ] **No** modificar `Models/Case.cs`, `MongoRepository` (salvo, opcionalmente, un
      método de agregación de solo lectura si se eligió el pipeline Mongo), ni crear
      índices/colecciones. **No** tocar documentos de negocio.
- [ ] Verificación: `dotnet build server/src/Factum.Backend/Factum.Backend.csproj`.

### Frontend (`implementer-frontend`, app `client/` únicamente)

- [ ] `npm install recharts` en `client/` (anotar versión elegida, compatible React 19).
- [ ] Declarar `BreakdownDimension`, `BreakdownBucket`, `CaseBreakdown` y el método
      `api.caseBreakdown(dimension, from?, to?)` en `client/src/lib/api.ts`.
- [ ] Crear `client/src/components/dashboard/DashboardBreakdown.tsx` ("use client"):
      selector de dimensión (aria-pressed + aria-live), Calendar de rango propio
      (patrón de `CaseHistory.tsx`), fetch con cancelación, estados carga/vacío/error.
- [ ] Barras horizontales Recharts (`ResponsiveContainer` + `BarChart layout="vertical"`)
      con tokens `--fx-*`, + tabla/lista accesible equivalente (label/cantidad/%).
      `isAnimationActive` atado a `useReducedMotion`.
- [ ] Porcentaje con guardas de división por cero; labels directos del backend.
- [ ] Montar `<DashboardBreakdown />` debajo de `DashboardStats` en
      `client/src/app/dashboard/page.tsx` (condicionado a `historyCases.length > 0`).
- [ ] **No** tocar `CaseHistory.tsx` ni sus filtros (D3-A).
- [ ] Invocar las skills de UX/UI obligatorias y dejar constancia en el progress.
- [ ] Verificación: `npx tsc --noEmit` y `npm run build` en `client/`.

---

## Verificación

| Lado | Comando antes de `done` |
|---|---|
| Backend | `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` |
| Frontend | `npx tsc --noEmit` **y** `npm run build` en `client/` |

No se crean proyectos de tests (no hay infraestructura; la agregación en memoria es
simple y se valida con la prueba manual).

### Prueba manual (queda para el usuario)

1. Loguearse en `/dashboard` con un perito que tenga casos de varias plataformas,
   estados y carátulas/ámbitos.
2. Verificar que el bloque **"Desglose"** aparece debajo de los KPIs/stats.
3. Cambiar la dimensión (Tipo de dispositivo / Estado / Carátula / Ámbito): el gráfico
   y la tabla se actualizan sin recargar; se anuncia (aria-live) y la opción activa se
   marca.
4. Comprobar que la **suma de las categorías == total** mostrado, y que los porcentajes
   suman ~100%.
5. Confirmar "Sin especificar" para casos sin carátula/ámbito y "Otras" cuando hay más
   de 8 categorías en carátula/ámbito.
6. Aplicar un rango de fechas: el desglose recalcula sobre `created_at` en el rango; el
   **historial de abajo no cambia** (D3-A independiente).
7. Sin casos en el filtro → estado vacío, sin gráficos rotos ni división por cero.
8. Modo claro/oscuro y 360 px: legible, sin scroll horizontal; `prefers-reduced-motion`
   desactiva la animación de entrada.
