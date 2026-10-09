# Implementación frontend — HU5 `trazabilidad-caso`

**Rama:** `feat/frontend-forense` (worktree aislado, base `origin/develop` @ a260507).
**Alcance:** solo `client/`. No se tocó `server/` ni `agent-ui/`.

## Contrato compartido verificado contra `develop`

Antes de escribir el cliente verifiqué el casing real de los campos leyendo en
`origin/develop`:

- `server/src/Factum.Backend/Models/CaseEvent.cs`: `CaseEvent`
  (`id`, `case_id`, `type`, `actor_dni`, `actor_name`, `timestamp`, `hostname`,
  `os_user`, `agent_mode`, `ip`, `filename`, `detail`), `CaseEventDetail`
  (`changed_fields`, `zip_hash`, `report_hash`, `reason`, `sha256`, `size`),
  `CaseEventTypes` (valores snake_case literales), y `AgentMode { Installed, Portable }`
  (serializado `installed`/`portable`).
- `server/src/Factum.Backend/Controllers/CasesController.cs`: ruta
  `[HttpGet("{id}/events")]` → `Ok(new { events })`.

Los tipos TS coinciden 1:1 con ese contrato (snake_case).

## Archivos tocados

- `client/src/lib/api.ts`
  - Tipos `CaseEventType`, `CaseEventDetail`, `CaseEvent` (con los nombres exactos
    del Contrato compartido).
  - Método `listCaseEvents(caseId): Promise<{ events: CaseEvent[] }>` (usa `request`).
- `client/src/types/index.ts`: re-export de `CaseEvent`, `CaseEventType`, `CaseEventDetail`.
- `client/src/lib/case-events.ts` (nuevo): mapa tipo→ícono lucide, tono
  (danger solo en `report_failed`), textos legibles para no técnicos,
  traducción de `changed_fields` a etiquetas (`fieldLabel`/`fieldsPhrase`),
  nombre de archivo a mostrar, y derivación de hitos para casos viejos (DT12:
  `derivedMilestones` a partir de `created_at`/`generated_at`/`officer`).
- `client/src/components/case-detail/CaseTimeline.tsx` (nuevo): sección
  "Cadena de custodia". Carga `api.listCaseEvents` en `useEffect` al montar
  (con flag `aborted` para evitar setState tras desmontar). Estados:
  cargando (spinner `motion-safe:animate-spin` + texto), error (aviso discreto
  `role="alert"` con "Reintentar", sin bloquear el resto del detalle),
  vacío/caso-viejo (aviso DT12 + hitos derivados), con eventos (lista
  ascendente). Cada ítem: `<ol role="list">`/`<li>`, nodo con ícono + riel
  vertical, texto claro, actor (`actor_name`, `actor_dni` en `title`), hora
  local con `<time dateTime>` y el UTC exacto en `title`, chip de equipo
  (patrón de `EvidenceHostChip`: hostname + modo installed/portable) cuando el
  evento trae `hostname`. Sin botones de editar/borrar (append-only).
- `client/src/components/case-detail/CaseDetailContent.tsx`: monta
  `<CaseTimeline cas={cas} />` como sección nueva, **antes** del bloque
  "Paquete del informe" (D7-A), con `<h3>` consistente (`text-fx-label uppercase`
  + ícono lucide). (También monta `CaseVersionHistoryButton` de HU6.)

## Decisiones no obvias

- **Orden y aviso (SDD §Orden):** si `events` está vacío → hitos derivados +
  aviso; si hay eventos pero ninguno `case_created` (caso anterior que solo
  acumuló capturas) → eventos reales + el mismo aviso; si hay `case_created` →
  solo eventos, sin aviso.
- **Actor de hitos derivados:** sale de `cas.officer.name` (no se inventan
  actores ni equipos; los derivados no tienen `hostname`).
- **Color no es el único canal (a11y):** `report_failed` usa borde/fondo rojo
  **y** ícono `AlertCircle` **y** texto "Falló la generación del informe".
- **Tipo desconocido:** `eventIcon`/`eventTitle` degradan a ícono/texto
  genérico si el backend agrega un tipo nuevo.

## Skills del arnés (constancia)

- `ui-ux-pro-max`: invocada antes del JSX. Búsquedas: `diff added removed not
  color alone` (→ regla "Color Only": íconos+texto además de color),
  `empty state loading skeleton`, `keyboard focus modal`. Aplicado a los
  estados vacío/carga/error y a que ningún estado dependa solo del color.
- `senior-frontend`: invocada. Patrón de fetch en `useEffect` con flag de
  cancelación y `useCallback`, siguiendo lo ya establecido en el repo
  (`ZipPasswordRow`, `ReportStep`). Sin re-render innecesario.
- `3d-web-experience`: invocada como criterio — **sin hallazgos aplicables**:
  no hay 3D ni efectos pseudo-3D; la HU no los pide y no se agregó ninguno.
- `web-design-guidelines`: autochequeo contra las reglas de Vercel (fetch OK).
  Verificado: íconos decorativos `aria-hidden`; botones con texto o
  `aria-label`; loaders terminan en `…`; `<time>` semántico; `translate="no"`
  en hostname; `break-all`/`min-w-0` para contenido largo; fechas vía `Intl`
  (`formatDate`/`formatTime`). Ajuste aplicado: `role="alert"` en el bloque de
  error de la timeline.
- `mblode-agent-skills-ui-animation`: invocada. Decisión: la timeline NO lleva
  entrada escalonada (sería "motion on mount without user trigger", anti-patrón;
  además es una herramienta forense). La entrada la aporta el contenedor del
  `CaseDetailModal` ya existente. Loaders con `motion-safe:animate-spin`
  (respeta `prefers-reduced-motion`).

## Verificación

- `cd client && npx tsc --noEmit` → sin errores.
- `cd client && npm run build` (en el worktree aislado, no en el checkout del
  usuario) → `✓ Compiled successfully`, TypeScript OK, 7/7 páginas generadas.
- No hay tests en `client/`; la SDD no agrega tests.

## Fuera de alcance / no tocado

- `server/` y `agent-ui/`: sin cambios.
- No se movieron tarjetas del Project ni se tocó `progress/sesiones/`.
