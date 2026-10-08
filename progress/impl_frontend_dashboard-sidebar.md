# impl_frontend — dashboard-sidebar (tarea ad-hoc, sin HU/SDD)

App: `client/` únicamente. Sin cambios en `server/`, `AppNavbar.tsx` ni `AgentChip.tsx`. Sin commit.

## Archivos

| Archivo | Cambio |
|---|---|
| `client/src/lib/activity.ts` | **Nuevo.** `deriveActivity(cases, limit = 5): ActivityEvent[]`, pura y determinística. |
| `client/src/components/dashboard/RecentActivityCard.tsx` | **Nuevo.** Tarjeta "Actividad reciente" (lista, skeleton de carga, estado vacío). |
| `client/src/components/dashboard/CurrentSessionCard.tsx` | **Nuevo.** Tarjeta "Sesión actual" (estado de Tatana / dispositivo, CTA). |
| `client/src/app/dashboard/page.tsx` | Layout de dos columnas en `mode === "history"`; CTA movido a la columna derecha; `useMemo` de la actividad; `sessionDevice`; `focusCaseHistory`. |
| `client/src/components/dashboard/DashboardStats.tsx` | Grilla: `lg:grid-cols-2 xl:grid-cols-4` (2×2 en la columna angosta de lg). |

## Layout

- Contenedor `max-w-[1400px]` (igual al interior de `AppNavbar`; el padding `p-4 sm:p-6` ya coincidía).
- Grilla `grid-cols-1` → `lg:grid-cols-[minmax(0,1fr)_340px]` → `xl:grid-cols-[minmax(0,1fr)_360px]`, `gap-8`.
- Ubicación en lg (CSS grid explícito, sin duplicar nodos):
  - fila 1: saludo + fecha (col 1) | botón "Nueva inspección" (col 2, `self-end`: alineado con la fecha, como antes estaba `sm:items-end`).
  - fila 2: contenido actual (col 1: error, stats, borradores, status, CaseHistory) | `<aside>` con Actividad reciente + Sesión actual (col 2).
  - El CTA es lo primero de la columna derecha, a ancho completo, con `motion-safe:animate-[fx-word-in_600ms_var(--fx-ease-out)_500ms_both]` intacto; las animaciones del saludo/fecha no se tocaron.
- Sticky: el `<aside>` es `lg:sticky lg:top-6 lg:self-start` **solo con `min-height: 860px`** (variante `lg:[@media(min-height:860px)]:sticky`). Las dos tarjetas suman ~700px; en una notebook de 768px de alto el sticky dejaría el botón "Conectar dispositivo" fuera de la vista sin forma de llegar hasta el final de la lista. Con alto suficiente se queda fijo; si no, scrollea normal.
- **Responsive (< lg), decisión:** orden del DOM = saludo → CTA → contenido (stats, borradores, "Mis inspecciones") → tarjetas al final (en `sm`–`md` las dos tarjetas lado a lado, `sm:grid-cols-2`).
  - Justificación: abajo de lg la acción primaria (CTA) sigue pegada al saludo como hoy; la lista de inspecciones es el contenido principal y no queda empujada ~700px por dos tarjetas secundarias. La actividad reciente es un resumen de esa misma lista, y el estado de Tatana ya está en el `AgentChip` de la navbar desde `sm`. Además el orden de lectura/tab coincide con el visual en todos los breakpoints.
  - CTA: `w-full` en mobile, `sm:w-auto` alineado a la izquierda en sm–md (no un botón de 900px), `lg:w-full` en la columna.
- `DashboardStats`: en lg la columna izquierda mide ~600px a 1024px de viewport, 4 tarjetas quedaban en ~140px (label truncado + anillo). Ahora 2×2 en lg y 4 en fila desde xl (~840px disponibles a 1280).

## `evidence` en el listado

Sí viene. `useAuth.loadHistory` → `api.listCases()` → `GET /api/cases` → `CaseService.ListAsync` → `MongoRepository.ListByOfficerAsync`, cuya proyección excluye solo `ReportTexts`, `ZipPassword` y `PendingGeneration` (`server/src/Factum.Backend/Infrastructure/MongoRepository.cs:120-127`). `Case.Evidence` es `List<EvidenceItem>` (`[]` fuera del flujo agent). Se usa directo; no se agregó ningún pedido por caso. Igual el helper trata `evidence` ausente/vacío como "sin evento".

## Derivación (`deriveActivity`)

- `created_at` → "Inspección iniciada" · `<nro> · <fabricante> <modelo>`.
- `evidence` no vacío → un evento por caso, "Evidencia registrada" · `<nro> · N archivo(s)`, en el `registered_at` más reciente.
- `status === "error"` → "Error al generar" · `<nro> · Revisá la inspección`, en `generated_at ?? última evidencia ?? created_at` (no hay fecha propia del fallo).
- Si no es error y hay `generated_at` → "Informe generado" · `<nro> · Paquete disponible`; y si `status === "completed"` → "Inspección completada" · `<nro> · <dispositivo>` en el mismo instante.
- `draft` / `generating` → solo "iniciada" (+ evidencia si hay).
- Orden: timestamp desc → desempate por tipo (completed/error > report > evidence > started; así "completada" queda inmediatamente arriba de "informe generado" en la lista desc) → id. Fechas inválidas se descartan. `slice(0, limit)`.
- Smoke check manual (con `tsx`, archivo temporal ya borrado) sobre 3 casos de ejemplo: orden y textos esperados; el caso con fecha inválida no emite eventos.
- Hora: `es-AR`, `hour: "numeric", minute: "2-digit", hour12: true` → "12:41 a. m." si es de hoy; si no, "6 oct" (con año si no es el actual). Fecha/hora larga en `title`. `<time dateTime={iso}>`.

## Tarjetas

- Ambas: `<section aria-labelledby>` + `fx-card rounded-fx-xl`, encabezado `h2 text-lg font-semibold` con botón cuadrado outline 32px (`border-fx-border rounded-fx-md fx-focus-ring`, ícono `ArrowRight`, `aria-label` propio), divisor `border-b border-fx-border`.
- **Actividad reciente:** `<ul class="divide-y divide-fx-border">`, insignia redonda 40px por tipo (started `Smartphone` accent-soft; evidence `Database` surface-3; report `FileText` info-soft; completed `CheckCircle2` success-soft; error `AlertTriangle` danger-soft), título + subtítulo truncados (`title` con el texto completo), hora `tabular-nums text-fx-text-3`. Skeleton de 3 filas (`aria-hidden`, `motion-safe:animate-pulse`) mientras `historyLoading` sin datos; estado vacío "Sin actividad todavía". El `→` ("Ver todas las inspecciones") hace `scrollIntoView` (auto con reduced motion) y enfoca un wrapper `tabIndex={-1}` alrededor de `CaseHistory` (no se pudo tocar `CaseHistory`; mismo patrón que `stepRegionRef` del wizard).
- **Sesión actual:** insignia 56px; línea de estado con punto de color ("Grabando" danger + pulse con motion-safe / "Tatana listo" success / "Tatana no disponible" warning); subtítulo `<fabricante> <modelo> · Android x / iOS x` o "Esperando dispositivo" / "Abrí Tatana en esta PC"; divisor; texto de ayuda; `Button outlined severity="secondary"` a ancho completo → `startWizard` ("Conectar dispositivo", o "Nueva inspección con este dispositivo" si hay uno; deshabilitado si está grabando). El bloque de estado es `role="status" aria-live="polite"`. El `→` abre la Guía de uso ("Cómo conectar un dispositivo", `aria-haspopup="dialog"`, `aria-expanded`) para que el botón no sea decorativo.

### Decisión no obvia: dispositivo de la tarjeta

En `mode === "history"`, `selDevice` y `isRecording` siempre están vacíos (`resetWizard` los limpia al volver), así que con los mismos datos que `AgentChip` la tarjeta diría siempre "Esperando dispositivo" aunque haya un celular enchufado. Se pasa `sessionDevice = selDevice ?? (agentOnline ? devices.find(d => d.state === "device") : null)`, usando el `devices` que `useAgentConnection` ya expone (sin llamadas nuevas). `online`/`recording` son los mismos que recibe `AgentChip`.

### Tradeoff conocido

Desde `sm`, `AgentChip` (navbar) y la tarjeta tienen cada uno un `role="status"`: un cambio de Tatana se anuncia dos veces. Se mantuvo porque el pedido exige `role="status"` en la tarjeta y `AgentChip` no se puede tocar. Si molesta, la alternativa es quitar `aria-live` de la tarjeta.

## Skills

- `ui-ux-pro-max`: invocado → `Unknown skill` (no instalado en esta sesión ni en `.claude/skills/`). Se aplicaron sus criterios a mano: jerarquía (CTA primario arriba, tarjetas secundarias), estados vacío/carga/hover/focus, targets ≥ 32px en íconos y ≥ 44px en CTAs, truncado con `title`.
- `senior-frontend`: invocado. Aplicado: derivación pura fuera del componente + `useMemo` por `historyCases`; componentes presentacionales sin estado; sin dependencias nuevas.
- `3d-web-experience`: invocado como criterio. Sin hallazgos aplicables: no hay 3D ni efectos pseudo-3D; las únicas animaciones nuevas son el `animate-pulse` del skeleton y del punto "Grabando", ambas `motion-safe`.
- `web-design-guidelines`: invocado → `Unknown skill` (no instalado). Autochequeo manual: `<section aria-labelledby>`, `<ul>`, `<time dateTime>`, `role="status"`, íconos `aria-hidden`, botones de ícono con `aria-label`, `fx-focus-ring`, foco programático al ir a la lista, reduced motion respetado en scroll y animaciones, contraste con pares de tokens ya usados en el repo (`*-soft` + color base).
- `ui-styling`: invocado → `Unknown skill` (no instalado). `mblode-agent-skills-ui-animation`: no aplica (sin transiciones/gestos nuevos).

## Verificación

- `npx tsc --noEmit` (en `client/`): **exit 0, sin errores**.
- `npx eslint <archivos>`: no corre — el repo no tiene `eslint.config.*` (ESLint 10). No es una regresión de esta tarea.
- Sin tests en `client/` (no hay infraestructura). Sin `next build` (regla del repo).
- No verificado visualmente en navegador.

## Incidente durante la edición

Un primer reemplazo por script en `page.tsx` buscó el fin del bloque con un patrón que también matcheaba el `{mode === "wizard"` de la navbar y duplicó JSX (tsc lo detectó). Se reconstruyó el archivo desde `git show HEAD:client/src/app/dashboard/page.tsx` (solo ese archivo, sin tocar el working tree global) + los cambios nuevos; el diff final contra HEAD solo contiene los cambios descritos.
