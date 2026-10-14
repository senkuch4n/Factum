# impl_frontend — metadatos-dispositivo (HU #79)

**Estado:** done (sin commit, a pedido del orquestador) · **Rama:** `feat/metadatos-dispositivo` (base `460fac0`)
**App:** solo `client/`. `agent-ui/` y `server/` no se tocaron.
**Fecha:** 2026-10-14

## Checklist §14.2

- [x] `lib/device-details.ts` (nuevo): `DeviceDetailKey`, `DeviceDetailReason`, `ClockReason`, `PcServerReason`,
      `DEVICE_DETAIL_KEYS`, `FIELD_LABEL`, `REASON_LABEL`, `formatDuration`, `describeDeviceSkew`, `describePcServerSkew`,
      `describeEventSkew` (trazabilidad), `exceedsThreshold`, `unavailableCount`, `isLoopbackAgent`, `probeServerClock`,
      `toSaveRequest`, `clockForRequest`, `detailsViewFromAgent`, `detailsViewFromCase`, `viewExceedsThreshold`,
      `formatDateTime`, y el tipo `DeviceDetailsUi` (estado que la página pasa al paso 2).
- [x] `lib/agent.ts`: `AgentDetailField`, `AgentDeviceClock`, `AgentDeviceDetails`, `AgentDeviceClockResponse`;
      `agent.supportsDeviceDetails()`, `agent.getDeviceDetails(serial, platform, signal?)`,
      `agent.getDeviceClock(serial, platform, signal?)` (timeout 15 s), ambos con `agentJSON` + `encodeURIComponent`.
      `device_not_found` en `AgentErrorCode`. `device_details_v1` documentada en el comentario de `AgentHealth`.
- [x] `lib/api.ts`: `DeviceDetails`, `DeviceDetailField`, `ClockMeasurement`, `PcServerProbe`, `DeviceClockRequest`,
      `SaveDeviceDetailsRequest`, `SaveDeviceClockRequest`; `Case.device_details?: DeviceDetails | null`;
      `CaseEventType` += `device_details_recorded | device_clock_measured`; `CaseEventDetail` += `unavailable_count`,
      `reread`, `clock_kind`, `device_minus_pc_ms`, `clock_margin_ms`, `pc_minus_server_ms`;
      `PublicConfig.clock_skew_warning_seconds`; `api.getServerTime()`, `api.saveDeviceDetails()`,
      `api.saveDeviceClock()`, `api.setDeviceDetailsOptions()` (las 4 con `requestSafe`). `types/index.ts`: re-exports.
- [x] `hooks/usePublicConfig.ts`: `clockSkewWarningSeconds` (entero 1–86400, si no 60).
- [x] `hooks/useDeviceDetails.ts` (nuevo): estado `idle | unsupported | loading | ready | error`, `read()` / `reset()` /
      `latest()`, timeout 45 s, reset al cambiar el serial, `device_not_found` → "El dispositivo no está conectado o no
      autorizó la conexión.", el resto → `agentErrorMessage`.
- [x] `components/device-details/DeviceClockLine.tsx` (fila compacta + `ClockSkewWarning`), `DeviceDetailsPanel.tsx`,
      `DeviceDetailsDialog.tsx`.
- [x] `CaseSummaryCard` + `CaseFormStep`: fila de reloj, aviso de umbral, "Ver datos técnicos · N no disponibles",
      avisos de error ("Volver a leer") y de Tatana viejo; "Serial" → "Id. de conexión"; "N.° de serie" en el plegable.
- [x] `app/dashboard/page.tsx`: hook; lectura al entrar al paso 2 (create, conectado); `registerDeviceDetails` en el alta
      (§10.3); retoma (§10.4); "Volver a leer" y SSID en edit; `deviceDetailsNotice` en el paso 3; etapa `clock` en
      `handleGenerateAgent` (§10.5).
- [x] `GenerateStep`: etapa `clock` condicional ("Midiendo el reloj del dispositivo…", ícono `Clock`).
- [x] `CaseDetailContent`: sección "Datos técnicos del dispositivo" (panel de solo lectura) o "Sin ficha técnica (caso
      anterior a esta función)". No había fila "Serial" en ese componente.
- [x] `lib/case-events.ts` + `CaseTimeline`: íconos `Cpu`/`Clock`, títulos (incluido `reread`), tono `warning` sobre el
      umbral (`eventTone(type, detail?, umbral?)`, sin umbral = neutro), línea secundaria `eventDeviceDetail(ev)`.
- [x] El SSID no aparece en ningún texto de trazabilidad y no se loguea (no hay `console.*` en el código nuevo).
- [x] `clock.pc_before`/`pc_after`/`device_time` se reenvían como strings sin parsear (`clockForRequest`, verificado
      abajo).
- [x] Tabla de `formatDuration`/`describeDeviceSkew` contra §5.3 (abajo).

## Archivos tocados

Nuevos:
- `client/src/lib/device-details.ts`
- `client/src/hooks/useDeviceDetails.ts`
- `client/src/components/device-details/DeviceClockLine.tsx`
- `client/src/components/device-details/DeviceDetailsPanel.tsx`
- `client/src/components/device-details/DeviceDetailsDialog.tsx`

Modificados:
- `client/src/lib/agent.ts`, `client/src/lib/api.ts`, `client/src/types/index.ts`
- `client/src/hooks/usePublicConfig.ts`
- `client/src/components/case-form/CaseSummaryCard.tsx`, `client/src/components/CaseFormStep.tsx`,
  `client/src/components/SpecRow.tsx` (etiqueta `w-16` → `w-24` para que entren "Id. de conexión" y "N.° de serie"; solo
  la usa `CaseSummaryCard`)
- `client/src/app/dashboard/page.tsx`
- `client/src/components/dashboard/GenerateStep.tsx`
- `client/src/components/case-detail/CaseDetailContent.tsx`, `client/src/components/case-detail/CaseTimeline.tsx`
- `client/src/lib/case-events.ts`

## Verificación

```
$ cd client && npx tsc --noEmit
(sin salida, exit 0)
```

- `curl http://localhost:3000/dashboard` → `200` con el `next dev` del usuario (compila los módulos nuevos; no se
  reinició el dev server ni se corrió `next build`).
- ESLint: el repo no tiene `eslint.config.*` (ESLint 10 no arranca); no se corrió.
- No hay tests en `client/` (SDD §13.4). Se verificó la lógica pura con un script descartable en el scratchpad (jiti,
  sin tocar el repo):

| Función | Entrada | Salida | §5.3 |
|---|---|---|---|
| `formatDuration` | 13 | `13 s` | ok |
| `formatDuration` | 60 | `1 min` | ok |
| `formatDuration` | 133 | `2 min 13 s` | ok |
| `formatDuration` | 3600 | `1 h` | ok |
| `formatDuration` | 3725 | `1 h 2 min 5 s` | ok |
| `formatDuration` | 90061 | `1 d 1 h 1 min` | ok |
| `formatDuration` | 0 | `0 s` | (no definido en §5.3; no se usa: M ≥ 1) |
| `describeDeviceSkew` | 10038 ms, ± 50 ms | `Adelanta 10 s respecto de esta PC (± 1 s)` | ok (ejemplo §5.1) |
| `describeDeviceSkew` | −10038, 50 | `Atrasa 10 s respecto de esta PC (± 1 s)` | ok |
| `describeDeviceSkew` | 499, 5 | `Coincide con esta PC (± 1 s)` | ok (N = 0) |
| `describeDeviceSkew` | 500, 5 / −500, 5 | `Adelanta 1 s …` / `Atrasa 1 s …` | ok (half away from zero) |
| `describeDeviceSkew` | 90000, 5 | `Adelanta 1 min 30 s respecto de esta PC (± 1 s)` | ok (prueba manual §15.2 paso 6) |
| `describeDeviceSkew` | 133000, 1001 | `Adelanta 2 min 13 s respecto de esta PC (± 2 s)` | ok (M = ceil) |
| `describePcServerSkew` | −120, 71 | `Esta PC coincide con el servidor (± 1 s)` | ok |
| `describePcServerSkew` | 2600, 71 | `Esta PC adelanta 3 s respecto del servidor (± 1 s)` | ok |
| `exceedsThreshold` | 60000 / 60001 / −90000, umbral 60 | `false` / `true` / `true` | ok (estricto, D6) |
| `unavailableCount` | fixture §8.1 | `3` (network_operator, sim_operator, sim_operator_numeric; `wifi_ssid=not_connected` no cuenta) | ok |
| `toSaveRequest(...).clock` | fixture §8.1 | `{"pc_before":"2026-10-14T15:22:39.0791760+00:00", …,"device_time":"2026-10-14T15:22:49.1662982+00:00","resolution_ms":1,"reason":null}` | strings intactos (7 decimales) |

Los tests de C# equivalentes (`ReportDeviceTexts`) todavía no existían en el working tree al terminar; el reviewer
puede cruzar esta tabla con ellos.

**Contrato:** coincide con la SDD §8. Cruce de solo lectura con lo que el backend lleva en el working tree:
`DeviceDetailsDtos.cs` (`SaveDeviceDetailsRequest`, `SaveDeviceClockRequest`, `DeviceDetailsOptionsRequest`,
`ServerTimeResponse`), `PublicConfigResponse.ClockSkewWarningSeconds`, `Case.DeviceDetails` y los 6 campos de
`CaseEventDetail`. Nombres iguales.

**Prueba en el navegador:** pendiente. No tengo herramientas de Chrome en esta sesión, y el backend y Tatana con
`device_details_v1` todavía estaban en curso. Lo que hay que mirar (con Tatana en mock y `MockDeviceClockSkewSeconds=90`):
paso 2 con lectura y aviso ámbar, el diálogo, el alta, el paso 3 sin aviso, la etapa de reloj al generar, el detalle del
caso y la trazabilidad. Además, con alto de ventana < 800 px, que la columna lateral no desborde. No se creó ni se
modificó nada en la Mongo de desarrollo.

## Decisiones no obvias

1. **Una sola lectura a la vez** (`useDeviceDetails.read()`): si hay una en curso, devuelve la misma promesa. El alta
   del caso, apretada mientras corre la lectura automática del paso 2, la espera en vez de cancelarla o repetirla.
   `read()` nunca lanza. Devuelve `null` si Tatana es viejo o falla, y el detalle queda en `status`/`error`.
2. **El timeout de 45 s es manual** (`setTimeout` + `AbortController`) y no `AbortSignal.any`, que no está garantizado en
   el `lib` del `tsconfig`. Un vencimiento muestra "El dispositivo no respondió a tiempo. Volvé a leer.". Un abort por
   cambio de dispositivo no muestra nada.
3. **`registerDeviceDetails(cas, { forceRead?, includeSsid? })`** se reutiliza en el alta (§10.3), la retoma (§10.4),
   "Volver a leer" en edición y el botón del aviso del paso 3. Devuelve `ok | unsupported | read_failed` y lanza solo
   si falla el `PUT`. El alta lo envuelve en un `try` propio: un error de la ficha nunca impide `go(3)`.
4. **Doble registro alta/retoma.** El `id` del caso recién creado se agrega al `Set` de intentos antes del `setCase`.
   Así el efecto de retoma (que ve un caso con `device_details == null`) no repite el registro. El efecto además exige
   `agentOnline`: con Tatana caído no gasta el único intento de la sesión.
5. **Vista del paso 2.** Si el caso tiene `device_details`, se muestra lo guardado. Si no, la lectura de esta sesión. En
   edición, "Volver a leer" muestra "Leyendo…" sobre lo guardado y al terminar se ve la ficha nueva (`setCase`).
6. **SSID (D7).** En el alta es estado local de la página y viaja en el `PUT` (`include_ssid_in_report`). En edición
   con ficha llama a `setDeviceDetailsOptions`. La retoma y "Volver a leer" mandan `undefined` (se conserva lo
   guardado). La casilla aparece solo con `wifi_state=connected` y SSID con valor. En el detalle del caso se dice en
   texto ("Se incluye / No se incluye en el informe").
7. **< lg** (SDD D-T11, de ui-ux-pro-max): además del drawer, la fila de reloj y el aviso de umbral van en la franja del
   equipo arriba del formulario. Si no, en celulares y tablets el aviso quedaría escondido en el drawer cerrado. Abrir
   "Ver datos técnicos" desde el drawer lo cierra: un diálogo a la vez.
8. **Columna lateral de alto fijo.** El aviso de umbral va en el contenedor de avisos existente, que ya tiene las reglas
   de alto bajo (`SHORT_COMPACT`, `sr-only` < 720 px). La fila de reloj y el botón están siempre visibles, y el
   escenario del teléfono (`flex-1`) cede el alto.
9. **Etapa `clock` en la generación.** `GenerateProgress` suma `withClock` para listar la etapa solo si se pasó por ella,
   y el título pasa a "paso X de N". En los textos `sr-only` del progreso se sacó el "Paso 1 de 2 / 2 de 2", que ya no
   vale con 2 o 3 etapas.
10. **Textos del panel.** Arranque y códigos van crudos (D9, sin calificar). Hora y zona automática: `1`/`0` →
    "Activada"/"Desactivada" (como el informe). Wifi: "Conectado a «SSID»" / "Activado, sin conexión" / "Desactivado".
    PC↔servidor: solo con la ficha guardada, `not_measured` → "No medida" e `invalid` → "No disponible (la medición no
    fue válida)".
11. **`ClockReason` desconocido** → texto de `error` (misma regla que la API para motivos fuera del enum).
12. **Ancho del diálogo** por `pt.root` (el `className` de props pierde contra el pt global; patrón de
    `DeviceFileExplorer`).

## Skills invocados

- `ui-ux-pro-max` (antes del JSX). Consultas `ux`: "data list label value unavailable" y "loading skeleton status".
  Aplicado: el motivo de "No disponible" va como texto visible, no como tooltip (también por pantallas táctiles); el
  esqueleto de carga tiene alto estable y `aria-busy`, sin spinner que parpadee; los valores largos usan
  `break-all` + mono. Además, la decisión 7.
- `senior-frontend`: lógica pura en `lib/`, un hook con cancelación y una sola promesa en vuelo, componentes de
  presentación sin estado de red, el estado de UI (`DeviceDetailsUi`) armado en la página y `useMemo` para el modelo de
  vista.
- `3d-web-experience` (criterio): no se agregó 3D ni efectos pseudo-3D. La única animación nueva es el
  `motion-safe:animate-pulse` del esqueleto, más el fade de `FxBanner` y el giro del chevron que ya existían.
- `ui-styling`: solo tokens `fx-*` y utilidades Tailwind estáticas (sin clases dinámicas), y el `pt` de Prime para el
  ancho del diálogo.
- `mblode-agent-skills-ui-animation`: no se invocó, porque no hay transiciones ni gestos nuevos (ver arriba).
- `web-design-guidelines` (autochequeo final con las reglas de vercel-labs, sobre los archivos tocados):
  - Hallazgo corregido: la fila de reloj cambia de "Leyendo…" al valor sin anunciarlo. Se le agregó `aria-live="polite"`
    (y `tabular-nums`).
  - Verificado sin hallazgos:
    - íconos decorativos con `aria-hidden`;
    - botones de texto con hover y `fx-focus-ring`;
    - la casilla del SSID con `label htmlFor`;
    - los plegables con `aria-expanded`/`aria-controls`;
    - `translate="no"` en identificadores y valores técnicos;
    - textos de carga terminados en "…";
    - fechas con `toLocaleString` (Intl);
    - `dl` con `dt`/`dd` agrupados en `div` (se corrigió la estructura de las filas de reloj antes del chequeo);
    - el diálogo con foco inicial y `Escape` de Prime y `overscroll-contain` del pt.
