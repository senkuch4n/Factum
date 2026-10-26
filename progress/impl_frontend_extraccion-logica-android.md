# Implementación frontend — Extracción lógica de Android (HU #100)

**SDD:** `Refactorizaciones/extraccion-logica-android.md` · **App:** solo `client/` (Next.js 16) ·
**Rama:** `feat/extraccion-logica-android` · **Checklist:** §13.2 · **Contrato:** §8 · **Fecha:** 2026-10-25

Sin commits ni push (los hace el orquestador). No se tocó `server/`, `android/`, `deploy/` ni `agent-ui/`.

## Alcance

Sección nueva `/extracciones` (listado, asistente de 4 pasos y detalle en curso/resultado), bloque
"Extracciones asociadas" en el detalle del caso, acceso desde el navbar y el dashboard, y el contrato
cliente↔Tatana / cliente↔API. Se trabajó contra el contrato de §8 (Tatana en mock, §5.12): no hace falta el
backend real.

## Archivos tocados

### Nuevos

- `client/src/lib/extraction.ts` — lógica pura y etiquetas (§10.2): catálogo de categorías (§4.3), presentación
  con ícono+texto por estado global/categoría/fase, `reasonCodeLabel`/`interruptionReasonLabel` (§4.4),
  `formatEta`, `isTerminal`, `defaultSelection` (D8), `isCategoryAvailable`, `deviceModificationsPreview` +
  `needsConfirmation` (U3=B), `globalProgress`.
- `client/src/hooks/useExtractionLive.ts` — feed WS (`extraction_progress`/`extraction_finished`/
  `extraction_cleanup`) con poll de respaldo cada 5 s, reconexión y reenganche al montar; sigue un `id` o la
  extracción en curso.
- `client/src/hooks/useExtractionRegistration.ts` — registro diferido (D11): reconcilia el índice local con el
  backend (upsert del `record`, copia de sellos `granted`, `markExtractionRegistered`); best-effort, cuenta
  pendientes.
- `client/src/app/extracciones/{page.tsx,nueva/page.tsx,[id]/page.tsx}` — rutas (server mínimos + `metadata`).
  `[id]` resuelve `params` como `Promise` (Next 16).
- `client/src/components/extraction/common.tsx` — `StatusChip`, `ProgressBar` (`role="progressbar"`), íconos y
  etiquetas de unidad de destino.
- `client/src/components/extraction/ExtractionsScreen.tsx` — listado (§10.3): capability, "En curso",
  lista de la API con fallback al índice local, banda de pendientes, estado vacío.
- `client/src/components/extraction/NewExtractionWizard.tsx` — asistente (§10.4): estado, preflight, estimación
  (debounce 400 ms), destinos, chequeo, inicio + registro inicial, foco al cambiar de paso, mapeo de errores de
  inicio.
- `client/src/components/extraction/steps/{DeviceStep,SelectionStep,DeviceFolderPicker,DestinationStep,ConfirmStep}.tsx`
  — los 4 pasos + el árbol de carpetas de `/sdcard` y el navegador de destino con unidades.
- `client/src/components/extraction/ExtractionDetailScreen.tsx` — orquesta progreso/resultado + `LinkCaseDialog`.
- `client/src/components/extraction/{ExtractionProgress,ExtractionResult,CategoryRow,FileErrorsList,LinkCaseDialog}.tsx`
  — vistas de §10.5.
- `client/src/components/extraction/AssociatedExtractions.tsx` — bloque "Extracciones asociadas" + diálogo
  "Asociar extracción" (§10.6).

### Modificados

- `client/src/lib/agent.ts` — tipos `AgentExtraction*`/`AgentDestination*`/`AgentDrive`/`AgentConnectorInfo`
  (§8.2), 13 códigos nuevos en `AgentErrorCode` + `extraction_id`/`missing_bytes` en `AgentErrorBody` (§8.6),
  métodos (`getExtractionPreflight`, `estimateExtraction`, `getDestinations`, `browseDestination`,
  `checkDestination`, `startExtraction`, `getCurrentExtraction`, `listExtractions`, `getExtraction`,
  `getExtractionErrors`, `cancelExtraction`, `markExtractionRegistered`, `writeExtractionSeals`,
  `revealExtraction`) y la capability `extraction_android_v1` en el comentario de capabilities.
- `client/src/lib/api.ts` — tipos `Extraction`/`ExtractionCategory`/`ExtractionDevice`/`ExtractionTotals`/
  `ExtractionDeviceModification`/`ExtractionInterruption`/`UpsertExtractionRequest` (§8.3-§8.4),
  `CaseEventType` + `extraction_linked`/`extraction_unlinked`, `CaseEventDetail` + `extraction_id`/`device_label`,
  métodos `listExtractions`/`getExtraction`/`upsertExtraction`/`linkExtraction`/`unlinkExtraction`/
  `getExtractionTimestampFiles`.
- `client/src/lib/case-events.ts` — ícono `HardDriveDownload`, títulos y `eventExtractionDetail` (device + hash).
- `client/src/components/case-detail/CaseTimeline.tsx` — rinde `eventExtractionDetail`.
- `client/src/components/shell/AppNavbar.tsx` — sección `extracciones` (ícono `HardDriveDownload`) + tipo.
- `client/src/components/DeviceConnect.tsx` — props opcionales `disabledPlatform`/`disabledHint` (iPhone
  deshabilitado); sin esas props el wizard de inspección no cambia.
- `client/src/components/case-detail/CaseDetailContent.tsx` — monta `AssociatedExtractions`.
- `client/src/app/dashboard/page.tsx` — acceso secundario "Nueva extracción" junto al CTA de inspección.

## Contrato compartido (§8) — coincide con la SDD

- Casing snake_case_lower respetado en todos los tipos nuevos; los campos usan **los nombres exactos** de la SDD
  (p. ej. `structured_categories`, `file_categories`, `custom_paths`, `verify_on_device`,
  `confirm_device_modifications`, `pc_server_probe`, `files_total`/`files_done`/`bytes_total`/`bytes_done`,
  `bytes_per_second`, `eta_seconds`, `manifest_sha256`, `final_folder_preview`, `missing_bytes`,
  `device_label`, `extraction_id`).
- Eventos WS `extraction_progress`/`extraction_finished`/`extraction_cleanup` consumidos con los nombres de §8.2.6.
- `record` de Tatana (`AgentExtractionSnapshot.record`) se reenvía **sin tocar** a `PUT /api/extractions/{id}`
  agregando solo `pc_server_probe` (D-T2): `UpsertExtractionRequest extends AgentExtractionRecord`.
- No se inventó ningún campo; no falta ninguno del lado backend/Tatana para esta parte (todo lo que la UI necesita
  está en §8).

## Skills de UX/UI (obligatorias)

- **ui-ux-pro-max** (antes del JSX): consultado `--domain ux` para progreso de operaciones largas y wizard
  multi-paso. Hallazgos aplicados: indicador "Paso N de 4" + barra (`StepIndicator`/`StepIndicatorCompact`
  reusados), `role="progressbar"` con `aria-valuenow`/`aria-valuetext`, focus ring visible en todo control,
  chips/botones con rol y estado accesible (no `div` clicables).
- **senior-frontend**: aplicadas sus buenas prácticas (reusar patrones existentes, estado mínimo, debounce de la
  estimación, hooks con cleanup/abort, listas con `key` estable, sin optimizaciones prematuras).
- **mblode-agent-skills-ui-animation**: animaciones solo en `transform`/`opacity` y ancho de la barra; todo bajo
  `motion-safe:`/`motion-reduce:transition-none`; transición de paso con fade suave; sin `transition: all`.
- **ui-styling**: la app usa PrimeReact + tokens `fx-*` (no shadcn); se siguieron esos tokens y el dark mode por
  variables CSS, sin hex crudos.
- **3d-web-experience** (como criterio): confirmado que el flujo es utilitario/forense y **no** amerita 3D ni
  efectos pseudo-3D; no se agregó ninguno.
- **web-design-guidelines** (autochequeo final): revisado el conjunto nuevo. Correcciones aplicadas: dos botones
  de ícono pasaron de 36 px a 44 px (`DeviceFolderPicker` "Abrir", `SelectionStep` quitar carpeta); se sumó foco
  al contenido al cambiar de paso (`tabIndex=-1` + efecto). Verificado: nombres accesibles en botones de ícono,
  `label htmlFor` en todas las casillas, `aria-current` en los pasos, `aria-live="polite"` de la fase limitado a
  los cambios de fase (no por tick), `translate="no"` en rutas/hashes/seriales, diálogos de Prime (foco atrapado
  + ESC), estados siempre con ícono **y** texto (nunca solo color).

## Decisiones no obvias

- **iPhone deshabilitado** (§10.4.1): en lugar de duplicar `DeviceConnect`, se le sumaron props opcionales
  `disabledPlatform`/`disabledHint` (backward-compatible: sin ellas el wizard de inspección queda igual). El
  iPhone aparece como tarjeta no interactiva con el motivo.
- **Ficha técnica (useDeviceDetails) en el paso 1**: no se lee desde la web. Tatana lee la ficha al iniciar
  (§5.2.3) y la incluye en `record.device_details`, así que el contrato no la necesita del lado web; el paso 1
  muestra batería/pantalla desde el `preflight`. (Ligera simplificación respecto de la mención de §10.4.1; no
  afecta el contrato ni el ACTA.)
- **`useExtractionLive`** gestiona su propia conexión (health + WS + poll) para que el detalle sea autónomo (no
  depende del `useAgentConnection` del dashboard, que no está montado en `/extracciones`). Una sola extracción por
  Tatana (D12), así que el feed "en curso" acepta el único evento activo.
- **Velocidad/ETA**: se muestran a nivel global (desde `totals.bytes_per_second`/`eta_seconds`); por categoría se
  muestran cantidades/bytes (no hay velocidad por categoría en el contrato).
- **CategoryRow** normaliza tanto el snapshot de Tatana (`files_total`…) como el DTO del backend (`files`…) para
  reusar la misma fila en progreso y resultado.
- **`custom`** no es una casilla: se deriva de `custom_paths` (árbol de carpetas) y se envía como `custom_paths`
  aparte de `file_categories`, como en §8.2.2/§8.2.4.

## Verificación

- `cd client && npx tsc --noEmit` → **limpio** (sin errores).
- `npm run build` (`next build`): **no ejecutado en el checkout principal** por la regla dura de no pisar `.next`
  del `npm run dev`. Se intentó un build aislado en el scratchpad con `node_modules` enlazado, pero Turbopack
  rechaza el symlink ("points out of the filesystem root"); copiar `node_modules` entero era desproporcionado. El
  repo **no tiene ESLint configurado** (no hay `eslint` ni config), así que `next build` no agregaría chequeos de
  lint por encima de `tsc`. Queda pendiente un build en worktree si el orquestador lo necesita.

## Pendiente / no verificable

- Recorrido del flujo con Tatana en **mock** en los 5 resultados de `MockExtractionOutcome` (§14): **no
  verificable acá** (requiere Tatana corriendo). Queda para QA.
- Prueba manual de §14 (M12 real, destinos USB/exFAT/FAT32, desconexión a mitad, reveal en Docker, asociación a
  caso): **no verificable** sin celular y backend reales.
- La UI asume los nombres de campo de §8 tal cual; si el `implementer-backend` detectara un desvío al
  implementar Tatana/API, hay que reconciliarlo (no se detectó ninguno del lado web).
