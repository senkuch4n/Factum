# SDD: Dashboard de KPIs y tendencia temporal del perito

**Slug:** `dashboard-kpis-tendencias`
**HU:** `docs/hu-dashboard-kpis-tendencias.md` (validada, con D1-B / D3-Recharts /
D4-A / D5-A / D6-A / D7-A ya decididas por el usuario)

## Resumen funcional

En `/dashboard` (modo historial) el perito ve un bloque de analítica de su propio
trabajo: cuatro tarjetas de KPI (Total con desglose por estado como chips,
Completados con tasa de completitud, En proceso = draft+generating, y Tiempo
promedio de cierre en días) y un gráfico de barras de casos creados por mes en los
últimos 12 meses. Los números salen de un endpoint nuevo de agregación en el
backend (`GET /api/cases/stats`), filtrado por el DNI del perito logueado como todo
lo demás. No se crea ni modifica dato de negocio, colección ni índice.

## Toca

- **backend (API):** sí — endpoint nuevo `GET /api/cases/stats` en `CasesController`
  + método en `CaseService` + DTO nuevo. Sin tocar Mongo (agrega en memoria sobre
  `ListByOfficerAsync`), sin índices nuevos.
- **backend (Tatana):** no.
- **client:** sí — tipo `CaseStats` + método `api.caseStats()`, componente de KPIs
  reescrito/ampliado y gráfico Recharts con carga dinámica.
- **agent-ui:** no.

## Coordinación con `dashboard-breakdown` (leer antes de implementar)

Esta HU es la **dueña del endpoint/controlador de agregación**. `dashboard-breakdown`
sumará su propio endpoint de breakdown en el **mismo `CasesController` y el mismo
`CaseService`/`ICaseService`**. Por eso:

- **NO son worktrees independientes.** Ambas editan los mismos archivos de backend
  (`CasesController.cs`, `CaseService.cs`, `ICaseService`, un archivo de DTOs). Si se
  implementan en ramas paralelas van a chocar en esos archivos. **Se implementan
  juntas o en secuencia, backend primero**, y la segunda rebasa sobre la primera.
- Para que convivan sin pisarse, esta SDD deja el backend preparado así:
  - El método de servicio de esta HU se llama `GetStatsAsync` (no un genérico
    `AggregateAsync`), y el de breakdown será `GetBreakdownAsync`: nombres distintos,
    mismo servicio.
  - Las rutas son `GET /api/cases/stats` (esta HU) y `GET /api/cases/breakdown`
    (breakdown): segmentos literales distintos bajo `api/cases`, sin ambigüedad de
    routing con `GET /api/cases/{id}` porque `stats`/`breakdown` matchean el literal
    antes que el parámetro `{id}` (ASP.NET prioriza segmentos literales sobre los de
    plantilla).
  - El DTO de esta HU (`CaseStatsResponse`) vive en un archivo nuevo
    `DTOs/CaseStatsDtos.cs`. El de breakdown debe ir en **otro** archivo
    (`DTOs/CaseBreakdownDtos.cs`) para no competir por el mismo archivo.
  - Ambas reusan `_repo.ListByOfficerAsync(officerDni, ct)` (o `ListAsync`) como única
    fuente; ninguna agrega pipelines de Mongo ni índices.

> Nota: la HU de breakdown tiene su D2 recomendado como "cálculo en el cliente".
> La decisión del usuario para **esta** serie es endpoint compartido en el backend
> (D1-B). El `architect`/orquestador de `dashboard-breakdown` debe alinear su D2 a
> "Escenario B — endpoint `GET /api/cases/breakdown` en el mismo controlador" cuando
> esa HU entre, para no duplicar la lógica de agregación. Esto queda como nota de
> coordinación, no se decide acá.

## Modelo de datos

**No cambia.** Se leen campos ya existentes del `Case`
(`server/src/Factum.Backend/Models/Case.cs`):

| Campo C# | Tipo | Uso |
|---|---|---|
| `Status` | `enum CaseStatus { Draft, Generating, Completed, Error }` | conteos por estado, tasa |
| `CreatedAt` | `DateTime` (UTC, siempre presente) | agrupación mensual, fecha de alta |
| `GeneratedAt` | `DateTime?` (UTC, solo en completados; legacy puede no tenerlo) | tiempo de cierre |

Compatibilidad con documentos existentes: `GeneratedAt` es `DateTime?`; los casos
legacy (`completed` sin `generated_at`) se **excluyen** del promedio de cierre (no
cuentan ni como 0). `Status` default `Draft`, `CreatedAt` siempre presente. No hay
`[BsonIgnoreExtraElements]` que tocar. **No se crea índice** (ya existe el ascendente
por `Officer.Dni`, `MongoRepository.EnsureIndexAsync`); el endpoint reusa el mismo
`Find(c => c.Officer.Dni == officerDni)` vía `ListByOfficerAsync`.

## Endpoint nuevo

### `GET /api/cases/stats`

- **Controlador:** `CasesController` (nuevo método `Stats`), mismo
  `[Authorize]` / `[ApiController]` / `[Route("api/cases")]` / snake_case_lower que el
  resto. Filtrado por `Officer.Dni` (vía `caseService.GetStatsAsync(Officer.Dni, …)`),
  nunca por casos ajenos.
- **Sin parámetros de ruta.** Un query param opcional (ver D2 abajo, zona horaria):
  `tz_offset_minutes` (int, opcional, default `0`).
- **Respuesta 200** (forma exacta, snake_case_lower):

```jsonc
{
  "total": 12,
  "by_status": { "draft": 3, "generating": 1, "completed": 7, "error": 1 },
  "completion_rate": 0.5833,          // 0..1; completed / total. 0 si total == 0.
  "avg_close_seconds": 198000.0,      // double | null. null si no hay completados con generated_at.
  "monthly": [                          // 12 items, ascendente, meses sin casos en 0.
    { "month": "2024-11", "count": 0 },
    { "month": "2024-12", "count": 2 },
    // … hasta el mes actual, inclusive
    { "month": "2025-10", "count": 5 }
  ]
}
```

Reglas de cálculo del servidor:
- `total` = cantidad de casos del perito.
- `by_status` = conteo por los 4 estados; **las 4 claves siempre presentes** (0 si no
  hay), para que el cliente no tenga que defenderse de claves ausentes.
- `completion_rate` = `completed / total` como `double`, o `0.0` si `total == 0`
  (sin división por cero). El cliente lo muestra como porcentaje.
- `avg_close_seconds` = promedio de `(GeneratedAt - CreatedAt).TotalSeconds` sobre los
  casos `Completed` **con `GeneratedAt` no nulo**; `null` si no hay ninguno. Nunca `0`
  ni `NaN`. El cliente convierte a días con 1 decimal (D7-A).
- `monthly` = exactamente **12** items (D4-A: 12 meses incluyendo el actual), del más
  viejo al más nuevo, con los meses sin casos en `count: 0` (el eje no salta). `month`
  en formato `"yyyy-MM"`.

### Zona horaria del agrupamiento mensual (D5-A resuelto)

D5-A pide **hora local del navegador**, pero la agregación la hace el servidor (D1-B)
y el servidor no conoce el huso del perito. **Decisión (D2 de esta SDD):** el cliente
manda su offset y el servidor agrupa con él.

- El cliente envía `tz_offset_minutes` = `-new Date().getTimezoneOffset()` (minutos a
  sumar a UTC para obtener la hora local; p. ej. Argentina UTC-3 → `-180`).
- El servidor, por cada caso, calcula el mes **local** así:
  `localCreated = CreatedAt.AddMinutes(tzOffsetMinutes)` y agrupa por
  `localCreated.ToString("yyyy-MM")`. El rango de 12 meses se arma también sobre
  "ahora local" = `DateTime.UtcNow.AddMinutes(tzOffsetMinutes)`.
- `avg_close_seconds` es una **diferencia** entre dos instantes UTC: es independiente
  del huso, no se ajusta.
- Si el query llega sin `tz_offset_minutes` (o inválido), default `0` (UTC): degradación
  aceptable, solo afecta casos creados cerca de medianoche de fin de mes.

Así se cumple D5-A (meses en hora local del perito) sin que el cliente tenga que
reagrupar la respuesta del servidor.

## Contrato compartido (client ↔ server)

Serialización backend: **snake_case_lower** (`JsonNamingPolicy.SnakeCaseLower` +
`JsonStringEnumConverter(SnakeCaseLower)` en `Program.cs`). Los nombres de abajo son
los que viajan en el JSON y los que el tipo TS debe declarar **tal cual**.

| Campo JSON | Tipo JSON | C# (`CaseStatsResponse`, `DTOs/CaseStatsDtos.cs`) | TS (`CaseStats`, `client/src/lib/api.ts`) |
|---|---|---|---|
| `total` | number (int) | `int Total` | `total: number` |
| `by_status` | objeto | `CaseStatusCounts ByStatus` | `by_status: { draft: number; generating: number; completed: number; error: number }` |
| `by_status.draft` | number (int) | `int Draft` | `draft: number` |
| `by_status.generating` | number (int) | `int Generating` | `generating: number` |
| `by_status.completed` | number (int) | `int Completed` | `completed: number` |
| `by_status.error` | number (int) | `int Error` | `error: number` |
| `completion_rate` | number (0..1) | `double CompletionRate` | `completion_rate: number` |
| `avg_close_seconds` | number \| null | `double? AvgCloseSeconds` | `avg_close_seconds: number \| null` |
| `monthly` | array | `List<MonthlyCount> Monthly` | `monthly: { month: string; count: number }[]` |
| `monthly[].month` | string `"yyyy-MM"` | `string Month` | `month: string` |
| `monthly[].count` | number (int) | `int Count` | `count: number` |

Query del request:

| Query JSON | Tipo | C# | TS |
|---|---|---|---|
| `tz_offset_minutes` | number (int, opcional) | `[FromQuery(Name = "tz_offset_minutes")] int tzOffsetMinutes = 0` | se arma en `caseStats()` (ver checklist) |

> Importante sobre el enum: `by_status` es un **objeto plano con 4 claves fijas**, no
> un diccionario indexado por el enum serializado. El DTO tiene propiedades `Draft`,
> `Generating`, `Completed`, `Error` (que serializan a `draft`/`generating`/
> `completed`/`error`). Así el contrato es estable aunque cambie el orden del enum.

## Decisiones técnicas

- **D1 (ya validada, B):** agregación en el backend con `GET /api/cases/stats`. No se
  calcula en el cliente. Reusa `ListByOfficerAsync` y agrega en memoria (volumen por
  perito chico; nada de pipeline de Mongo ni índice nuevo).
- **D2 (zona horaria — resuelta en esta SDD):** el servidor agrupa el `monthly` en
  hora **local del perito** usando `tz_offset_minutes` que manda el cliente; default
  UTC si falta. `avg_close_seconds` no se ajusta (es una diferencia). Cumple D5-A sin
  reagrupar en el cliente. **No necesita validación extra del usuario** (deriva de la
  combinación D1-B + D5-A que ya validó).
- **D3 (ya validada, Recharts):** `recharts@^3.10` (soporta React 19 como peer:
  `^16.8 || ^17 || ^18 || ^19`; compatible con Next 16). Gráfico de **barras**
  verticales (`BarChart`), una serie. Se carga con `next/dynamic` + `ssr: false`
  (patrón ya usado en `client/src/components/ReportStep.tsx` para el editor), para que
  Recharts no entre al bundle inicial del dashboard. El contenedor usa
  `ResponsiveContainer`.
- **D4 (ya validada, A):** 12 meses fijos incluyendo el actual. En 360 px, si 12
  barras quedan apretadas, el eje X muestra etiquetas intercaladas (una sí/una no) o el
  gráfico va dentro de un contenedor con `overflow-x-auto` y ancho mínimo; no se reduce
  el rango. El `implementer-frontend` elige con `ui-ux-pro-max`.
- **D5 (ya validada, A):** meses en hora local — implementado vía D2 de arriba.
- **D6 (ya validada, A):** 4 tarjetas — Total (destacada, con chips del desglose por
  estado), Completados (con tasa de completitud como anillo/subdato), En proceso
  (draft+generating), Tiempo promedio de cierre.
- **D7 (ya validada, A):** tiempo de cierre en **días con 1 decimal** (p. ej. "2,3
  días"); `avg_close_seconds / 86400`, redondeo a 1 decimal, separador decimal local
  (coma en es-AR). "—" cuando `avg_close_seconds` es `null`.
- **D8 (datos para el gráfico en el cliente):** el componente recibe la respuesta del
  endpoint ya lista (meses rellenos y ordenados); no recalcula sobre `historyCases`.
  El dashboard dispara `api.caseStats()` junto con `loadHistory` (segundo fetch,
  aceptado por D1-B). El estado vacío y de carga se decide con la respuesta del stats
  (ver checklist frontend).

## Checklist atómico — Backend

> Restricción de datos de desarrollo (AGENTS.md): este endpoint es **solo lectura**,
> no inserta, actualiza ni borra documentos. No crear índices.

- [ ] `DTOs/CaseStatsDtos.cs` (archivo nuevo): declarar
  `public sealed record CaseStatsResponse(int Total, CaseStatusCounts ByStatus, double CompletionRate, double? AvgCloseSeconds, List<MonthlyCount> Monthly);`
  `public sealed record CaseStatusCounts(int Draft, int Generating, int Completed, int Error);`
  `public sealed record MonthlyCount(string Month, int Count);`
  (namespace `Factum.Backend.DTOs`).
- [ ] `ICaseService` (en `Services/Cases/CaseService.cs`): agregar
  `Task<CaseStatsResponse> GetStatsAsync(string officerDni, int tzOffsetMinutes, CancellationToken ct = default);`
- [ ] `CaseService.GetStatsAsync`: `var cases = await _repo.ListByOfficerAsync(officerDni, ct);`
  (no hace falta `ResolveStorage`, no se usa `EvidenceStorage`). Calcular:
  - `total = cases.Count`.
  - conteos por `CaseStatus` (los 4, 0 si no hay).
  - `completionRate = total == 0 ? 0.0 : (double)completed / total`.
  - `avgCloseSeconds`: filtrar `c.Status == CaseStatus.Completed && c.GeneratedAt is not null`,
    promedio de `(c.GeneratedAt!.Value - c.CreatedAt).TotalSeconds`; `null` si la lista
    filtrada queda vacía.
  - `monthly`: generar los 12 meses desde `nowLocal.AddMonths(-11)` hasta `nowLocal`
    (`nowLocal = DateTime.UtcNow.AddMinutes(tzOffsetMinutes)`), clave `"yyyy-MM"` con
    `CultureInfo.InvariantCulture`, contar cada caso por
    `c.CreatedAt.AddMinutes(tzOffsetMinutes).ToString("yyyy-MM")`, los no mapeados a un
    mes del rango se descartan, los meses sin casos quedan en 0, orden ascendente.
- [ ] `CasesController`: nuevo método, debajo de `List`, antes de `Get` (para dejar el
  literal visible), así:
  ```csharp
  [HttpGet("stats")]
  [ProducesResponseType<CaseStatsResponse>(StatusCodes.Status200OK)]
  public async Task<IActionResult> Stats([FromQuery(Name = "tz_offset_minutes")] int tzOffsetMinutes, CancellationToken ct)
      => Ok(await caseService.GetStatsAsync(Officer.Dni, tzOffsetMinutes, ct));
  ```
  (El default `0` del binding de query cubre el caso sin parámetro.)
- [ ] Verificar que `GET /api/cases/stats` no colisiona con `GET /api/cases/{id}`: el
  segmento literal `stats` gana sobre `{id}`. (Confirmable corriendo el backend y
  pegándole a `/api/cases/stats`; no es estrictamente necesario para el build.)

## Checklist atómico — Frontend (`client/`)

> Skills de UX/UI obligatorias (las aplica el implementer): `ui-ux-pro-max` antes del
> JSX final, `senior-frontend`, `3d-web-experience` como criterio (sin 3D),
> `web-design-guidelines` como autochequeo. Leer `client/AGENTS.md` y la doc de Next 16
> en `node_modules/next/dist/docs/` antes de tocar código.

- [ ] `client/src/lib/api.ts`: declarar el tipo
  ```ts
  export interface CaseStats {
    total: number;
    by_status: { draft: number; generating: number; completed: number; error: number };
    completion_rate: number;     // 0..1
    avg_close_seconds: number | null;
    monthly: { month: string; count: number }[];
  }
  ```
  y el método en el objeto `api`:
  ```ts
  async caseStats(): Promise<CaseStats> {
    const tz = -new Date().getTimezoneOffset();
    return request(`/api/cases/stats?tz_offset_minutes=${tz}`);
  },
  ```
  (usar el helper `request<T>` ya existente). Exportar `CaseStats` por
  `client/src/types/index.ts` si el componente lo importa desde `@/types` (seguir el
  patrón de `Case`, que se re-exporta desde `@/lib/api`).
- [ ] Instalar `recharts`: `npm install recharts@^3.10` en `client/` (queda en
  `dependencies`).
- [ ] Componente de gráfico `client/src/components/dashboard/CasesByMonthChart.tsx`
  (nuevo), `"use client"`: recibe `data: { month: string; count: number }[]`, renderiza
  `ResponsiveContainer > BarChart` de Recharts. Colores por tokens `--fx-*` (pasar los
  valores computados de las CSS vars o clases a los `fill`/`stroke`; Recharts no lee
  Tailwind, así que leer el token con `getComputedStyle`/`color-mix` resuelto o usar un
  color de token expuesto). Eje X: meses en español abreviado ("ene", "feb"…) derivados
  de `"yyyy-MM"` con `Intl.DateTimeFormat("es-AR", { month: "short" })` (+ año cuando
  cambia). Eje Y entero. Tooltip accesible. Alternativa textual: tabla visualmente
  oculta (`sr-only`) con mes→cantidad, o `role="img"` + `aria-label` que resuma la
  tendencia. Animación de entrada corta, desactivada con `prefers-reduced-motion`
  (`isAnimationActive={!reduceMotion}` usando `useReducedMotion`).
- [ ] Cargar el gráfico con `next/dynamic`:
  ```ts
  const CasesByMonthChart = dynamic(() => import("@/components/dashboard/CasesByMonthChart").then(m => m.CasesByMonthChart), {
    ssr: false,
    loading: () => <div aria-hidden className="h-64 rounded-fx-xl bg-fx-surface-2 motion-safe:animate-pulse" />,
  });
  ```
  (patrón de `ReportStep.tsx`).
- [ ] Ampliar/reescribir `client/src/components/dashboard/DashboardStats.tsx` para que
  reciba `stats: CaseStats | null` + `loading: boolean` (en vez de calcular sobre
  `cases`), y rinda las **4 tarjetas** (D6-A): Total (destacada, con chips de los 4
  estados usando tokens semánticos: success/warning/danger/neutro, siempre ícono+texto,
  no solo color), Completados (anillo con `completion_rate*100` redondeado, reusar
  `ProgressRing`), En proceso (`by_status.draft + by_status.generating`), Tiempo
  promedio de cierre (`avg_close_seconds` → días con 1 decimal es-AR, "—" si null).
  Mantener grilla `grid-cols-2 sm:grid-cols-4`, tokens `--fx-*`, `useReducedMotion`.
  - Estado de carga: esqueleto de tarjetas + placeholder del gráfico con `aria-busy`
    ("Cargando estadísticas…").
  - Sin completados: tasa "0%"/"—" y tiempo "—" con subtexto, sin "NaN"/"0".
- [ ] `client/src/app/dashboard/page.tsx`:
  - Pedir las stats: usar el `historyError`/loading existente del hook o un estado local
    `const [stats, setStats] = useState<CaseStats | null>(null)` cargado con
    `api.caseStats()` dentro del mismo efecto/callback que `loadHistory` (o justo
    después). Manejar error con un `FxBanner` de error + reintento sin romper el resto
    del dashboard (el historial sigue andando aunque falle el stats).
  - Reemplazar `{historyCases.length > 0 && <DashboardStats cases={historyCases} />}`
    (línea ~803) por el bloque nuevo: KPIs + gráfico.
  - **Estado vacío** (`stats?.total === 0` y no cargando): en vez de KPIs en 0 y gráfico
    vacío, mostrar el mensaje de bienvenida que invita a crear la primera inspección,
    coherente con el estado vacío del historial.
  - Responsive 360 px: tarjetas apiladas sin scroll horizontal; gráfico redimensiona o
    scrollea según D4.
- [ ] Si `useAuth` no expone un flag de error de historial, NO agregar lógica de red
  nueva al hook salvo lo mínimo; mantener el fetch de stats en la página.

## Verificación

**Backend** (`implementer-backend`, antes de `done`):
```bash
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
```
Debe compilar sin errores ni warnings nuevos.

**Frontend** (`implementer-frontend`, antes de `done`):
```bash
cd client && npx tsc --noEmit && npm run build
```
Ambos sin errores. (`npm run build` confirma que Recharts con `next/dynamic ssr:false`
no rompe el SSR del dashboard.)

**Prueba manual (queda para el usuario):**
1. Loguearse como un perito con casos en varios meses y al menos un completado con
   `generated_at`. En `/dashboard` (modo historial) ver las 4 tarjetas con números
   coherentes con el historial, el desglose por estado en chips, la tasa de
   completitud y el tiempo promedio de cierre en días con 1 decimal.
2. Ver el gráfico de barras con 12 meses, meses sin casos en 0, etiquetas en español,
   tooltip al hover/foco; probar lector de pantalla (alternativa textual) y
   `prefers-reduced-motion` (sin animación de entrada ni loops).
3. Con un perito sin casos: ver el estado vacío de bienvenida (no KPIs en 0).
4. Con casos pero ninguno completado: tiempo de cierre "—" y tasa "0%"/"—" sin "NaN".
5. 360 px de ancho: tarjetas apiladas sin scroll horizontal y gráfico legible.
6. Confirmar en red que `GET /api/cases/stats?tz_offset_minutes=…` responde 200 con el
   JSON del contrato y que un caso ajeno no aparece en los conteos.

## Archivos tocados (referencia)

- Backend: `server/src/Factum.Backend/Controllers/CasesController.cs`,
  `server/src/Factum.Backend/Services/Cases/CaseService.cs` (interfaz + método),
  `server/src/Factum.Backend/DTOs/CaseStatsDtos.cs` (nuevo).
- Frontend: `client/src/lib/api.ts`, `client/src/types/index.ts` (re-export),
  `client/src/components/dashboard/DashboardStats.tsx`,
  `client/src/components/dashboard/CasesByMonthChart.tsx` (nuevo),
  `client/src/app/dashboard/page.tsx`, `client/package.json` (recharts).
