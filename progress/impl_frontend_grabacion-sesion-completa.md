# impl_frontend — grabacion-sesion-completa (#80), fase 1

**Agente:** implementer-frontend · **Fecha:** 2026-10-14 · **Rama:** `feat/grabacion-sesion-completa` · **Estado:** done (sin commit)
**Alcance:** checklist §15.2 de `Refactorizaciones/grabacion-sesion-completa.md` (F1). Diseño de §9 (web) y §10 (agent-ui). DU1–DU4 = A.
La fase 2 (§15.4: cámara e iPhone, HU #85) **no** se tocó: no hay cámara ni `extendInfo` en `agent-ui/package.json`.

## Checklist §15.2

- [x] Skills obligatorios (detalle abajo).
- [x] `lib/agent.ts`: tipos §7.2/§7.3, 6 códigos, `session_mark?`, métodos F1.
- [x] `lib/api.ts`: tipos §7.6, `zip_folder`, 5 eventos + campos de detail, los 2 métodos y la config pública.
- [x] `lib/agent-messages.ts`: los 6 códigos de Tatana + `recording_session_open` (y los de la API, §7.7).
- [x] `lib/recording-session.ts` (puro).
- [x] `hooks/usePublicConfig.ts`: modo y duración del tramo.
- [x] `hooks/useRecordingSession.ts` (§9.2).
- [x] `hooks/useFileManager.ts`: `addStoredFile`.
- [x] `SessionRecordingCard.tsx`, `SessionIndicator.tsx` y `MarkMomentPopover.tsx` (+ `SessionNotices.tsx`).
- [x] `CaptureStep.tsx`: tarjeta, indicador, "Marcar momento", avisos y aviso de pantalla para la sesión.
- [x] `EvidenceTray.tsx`: grupo "Sesión" sin quitar.
- [x] `app/dashboard/page.tsx`: activación al entrar, cierre en todas las salidas y header compacto.
- [x] `GenerateStep.tsx`: checklist, bloqueo y acción (§9.6).
- [x] `SessionRecordPanel.tsx` en `ResultStep` y `CaseDetailContent`.
- [x] `lib/case-events.ts`: 5 tipos.
- [x] `agent-ui` [DU3=A]:
  - `agent-api.ts`: `prepareShutdown` y `AgentStateResponse.recording_session?`;
  - `index.ts`: `confirmSessionShutdown` en `requestQuit` y en "Reiniciar agente .NET".

## Archivos tocados

### `client/` — nuevos

| Archivo | Qué hace |
|---|---|
| `src/lib/recording-session.ts` | Funciones puras para el resto. `ACTIVE_SESSION_STATES`, `isSessionActive`, `isSessionRecording`, `SESSION_SEGMENT_RE`, `REASON_TEXT` (§8.4), `REASON_LABEL`, `reasonText`, `trackLabel`, `defaultEnabled`, `toBackendSnapshot` (§7.5), `sessionFilenames` (por pertenencia al diario, D-T5), `segmentsByName`, `segmentMetaFromName`, `elapsedText`, `offsetText`, `hhmm`, `capacityText`, `noSpaceText`, `warningText`, `generationBlocker`, `lastStopAt`, `sessionSummary`. |
| `src/hooks/useRecordingSession.ts` | Hook del §9.2. Ver "Decisiones". |
| `src/components/capture/SessionRecordingCard.tsx` | Tarjeta "Grabación de la recolección". Tiene switch `role="switch"` (con `required` queda `aria-disabled`), espacio estimado, error por falta de espacio, aviso de pantalla con "Reintentar"/"Grabar igual", error de inicio y `session_active`. Variantes "Tatana viejo" e "iPhone". |
| `src/components/capture/SessionIndicator.tsx` | Indicador sticky (≥ 44 px): punto rojo (`motion-safe:animate-pulse`), "Grabando sesión [desde HH:MM] · HH:MM:SS · tramo N". Cronómetro `aria-hidden` y `role="status"` aparte que anuncia solo los cambios. "Detener grabación de sesión" con `ConfirmDialog` y "Reanudar". Tiene variante `compact` para el header. |
| `src/components/capture/SessionNotices.tsx` | Avisos de §9.4 con `FxBanner`, no modales y sin robar foco: `waiting_device`, reanudación (info, 8 s), `interrupted`, `sin_espacio`, `fallo_grabacion`, `disk_low`, `screen_off`/`screen_locked` y `rotation_delayed`. |
| `src/components/capture/MarkMomentPopover.tsx` | Popover no modal anclado al tile. Input "Qué pasó (opcional)" (`maxLength=300`) y "Guardar marca" (también con Enter). Se cierra con Esc o con un clic afuera, y el foco vuelve al tile. |
| `src/components/case-detail/SessionRecordPanel.tsx` | "Registro audiovisual": resumen "{n} tramos · {duración} · {k} huecos · sellos s/t" y un `<details>` con tramos (SHA-256 truncado + `CopyButton` + sello), huecos, marcas y la línea del reloj (#79). |

### `client/` — modificados

| Archivo | Cambio |
|---|---|
| `src/lib/agent.ts` | Tipos `SessionStateDto`, `CaseSessionDto`, `SessionSegmentDto`, `SessionGapDto`, `SessionMarkDto`, `SessionCapacity`, `SessionStartRequest`, `SessionSegmentClosedData`, `SessionMarkAddedData` y `AgentSessionState`. `AgentErrorCode` suma 6 códigos y `AgentErrorBody` suma `case_id`. `RecordingStartedData`/`ScreenshotTakenData`/`takeScreenshot` suman `session_mark?`. Métodos `supportsRecordingSession/Camera/Ios`, `sessionCapacity`, `startSession`, `stopSession`, `addSessionMark`, `getCaseSession` (404 → `null`) y `agentRecordingSession`. `RECORDING_OPERATION_KINDS` suma `recording_session`: el REC del chip y el "ocupado" de la sesión web la ven. |
| `src/lib/api.ts` | Tipos: `RecordingSession` y sub-tipos, `RecordingSessionSnapshot`, `RecordingSessionMode`, `Case.recording_session?`, `ManifestFile.zip_folder?`, los 5 `CaseEventType` y los 10 campos de `CaseEventDetail`, `PublicConfig.recording_session_mode?/_segment_minutes?` y `ApiErrorBody.pending_files?`. Métodos `saveRecordingSessionSettings` y `syncRecordingSession`. |
| `src/lib/agent-messages.ts` | `screenBlockMessage("session")`, `SESSION_ERROR_TEXT`, `RECORDING_SESSION_OPEN_MESSAGE`, `RECORDING_SESSION_API_TEXT` y la acción `"session"` en `agentErrorMessage`. |
| `src/lib/case-events.ts` | Íconos y textos de los 5 tipos (§9.8). `eventFilename` incluye `recording_session_segment_closed`. |
| `src/hooks/usePublicConfig.ts` | `recordingSessionMode` (default `optional_off`) y `recordingSessionSegmentMinutes` (5–120, default 30). |
| `src/hooks/useFileManager.ts` | `addStoredFile(name, {size, sha256})`. `restoreCaseEvidence` no encola tramos `sesion_*` "extra" para importarlos: Tatana reserva el prefijo y los registra el hook con el SHA-256 del diario. |
| `src/components/CaptureStep.tsx` | Prop `session: CaptureSessionProps`. Indicador arriba del paso, avisos y tarjeta arriba de "Capturar del celular", tile "Marcar momento" + popover + confirmación `role="status"`, y grupo "Sesión" en la bandeja. Los tramos no van en "Capturas"/"Adjuntos", no se pueden quitar (`removeItem` defensivo, `VideoCard removable={false}`) y suman el tag "N tramos". Mientras cierra, el botón del paso dice "Cerrando la grabación de la sesión…". |
| `src/components/capture/EvidenceTray.tsx` | `SessionSegmentTile`: fila "Pantalla · tramo N · HH:MM–HH:MM" con estado de guardado y sello, sin quitar. |
| `src/components/capture/ScreenBlockNotice.tsx` | `ScreenBlock.action` acepta `"session"` ("Grabar igual"). |
| `src/components/VideoCard.tsx` | Prop `removable` (default `true`). |
| `src/components/dashboard/GenerateStep.tsx` | Sección "Grabación de la sesión cerrada y guardada": check o advertencia y "Cerrar grabación de la sesión". `sessionBlocker` deshabilita "Generar" y queda en `aria-describedby`. Grupo "Grabación de la sesión" y carpeta `sesion/` en el paquete. Si los tramos pasan de 2 GiB, el texto del ZIP suma "puede tardar varios minutos". |
| `src/components/ResultStep.tsx` | Props `recordingSession` y `deviceDetails` → `SessionRecordPanel` en el resultado del flujo agent. |
| `src/components/case-detail/CaseDetailContent.tsx` | `SessionRecordPanel` debajo de los datos técnicos. |
| `src/app/dashboard/page.tsx` | Hook `useRecordingSession` con reenvío de los eventos WS y `refresh` al reconectar. Salidas del paso 3: `go()` (`goNow` + cierre previo, con status "Cerrando la grabación de la sesión…"), "Guardar evidencia…" (stop **antes** de `handleSaveAndContinue`) y `resetWizard` (`stopDetached`). Props del paso 3/5/6, indicador compacto fuera del paso 3, 409 `recording_session_open` en la generación, `sessionBusy` y `scroll-padding-top` con el indicador sticky. |

### `agent-ui/`

| Archivo | Cambio |
|---|---|
| `src/main/agent-api.ts` | `request` y `getAgentState` aceptan un timeout. Se agregan `AgentStateResponse.recording_session?`, `ACTIVE_SESSION_STATES` y `prepareShutdown(port, 25000)` → `{ ok, had_session }`, sin `Origin`. |
| `src/main/session-shutdown.ts` (nuevo) | `confirmSessionShutdown()`, en estos pasos: (1) `getAgentState` con timeout de 2 s; (2) con una sesión activa, `dialog.showMessageBox` con el texto de §10 y los botones ["Cancelar", "Cerrar Tatana"]; (3) `prepareShutdown` ignorando errores; (4) sin sesión o sin respuesta, devuelve `true`. Si llegan dos pedidos seguidos, muestra un solo diálogo. |
| `src/main/index.ts` | `requestQuit` (al principio, antes de `installNow`) y "Reiniciar agente .NET" de la bandeja. |
| `src/main/ipc.ts` | `tatana:restart-agent` (el "Reiniciar" de la ventana) usa el mismo diálogo y devuelve `{ ok: false }` si se cancela. Es un tercer punto de reinicio, que la SDD no listaba. |
| `src/main/updater/state.ts` | `busyOperationText('recording_session')` → "grabación de la sesión en curso". El auto-update ya responde `agent_busy`. |

No se tocó `server/`, `progress/sesiones/` ni el Project.

## Verificación

```
client$ npx tsc --noEmit                                  → sin errores
agent-ui$ npx tsc --noEmit -p tsconfig.web.json           → sin errores
agent-ui$ npx tsc --noEmit -p tsconfig.node.json          → sin errores
agent-ui$ npm test                                        → tests 26, pass 26, fail 0
```

`tsc` reescribe los `.tsbuildinfo` de `agent-ui`. Dejé `tsconfig.node.tsbuildinfo` como estaba (`git checkout` de ese único archivo) y borré el `tsconfig.web.tsbuildinfo` que se generó.

**Tests de las funciones puras** (fuera del repo, en el scratchpad, con `tsx --test`): pasaron 4 de 4.
- `toBackendSnapshot`: claves exactas de §7.5, sin `serial`/`case_id`, sin tramos en curso y con los `null` explícitos.
- `generationBlocker`: sesión activa, tramo sin registrar, `pendingSync` y sin bloqueo.
- Textos: `capacityText` ("Espacio libre en esta PC: 120 GB (≈ 33 h de grabación, contando la copia del ZIP)."), `elapsedText`, `offsetText`, `REASON_LABEL`, `warningText`, `lastStopAt`.
- `sessionFilenames` y `segmentMetaFromName`.

No hay infraestructura de tests en `client/` (§14.3), así que no se agregaron al repo.

**Pasada visual** con un harness de componentes en una copia de `client/` en el scratchpad (`next dev -p 3100` y Playwright headless):
- se probó a 390 px en claro y oscuro y a 1024 px;
- se abrió el popover, que enfoca el input; Esc devuelve el foco a "Marcar momento";
- el indicador muestra "Grabando sesión desde 15:04 · 00:42:10 · tramo 2".

A partir de esto, la etiqueta del tramo en la bandeja pasó de `truncate` a `break-words`: a 390 px se cortaba la franja horaria.

No se reinició el `next dev` del usuario en :3000, que siguió respondiendo 200.

**Lo que no se probó:**
- El recorrido de punta a punta con Tatana en mock. El Tatana de :8765 es el del usuario y no tiene `recording_session_v1`, y el backend de :8080 todavía no tiene las rutas: no se tocaron.
- El backend trabaja en paralelo. Los nombres de `Services/Session/SessionModels.cs` que ya están en disco coinciden con los que usa la web.

No se escribió nada en Mongo.

## Contrato compartido

Se usan los nombres exactos de §7:
- **Tatana HTTP** (los `null` no viajan): `GET /session/capacity?camera=false`, `POST /cases/{id}/session/start` (`serial`, `platform`, `android_version`, `segment_minutes`, `skip_screen_check`, `origin_id`), `.../session/stop` (`reason`, `origin_id`), `.../session/marks` (`text`, `origin_id`) y `GET .../session` (404 → `null`).
- **`/agent/state`:** `recording_session` y `session_recovery`.
- **WebSocket** (los `null` viajan): `session_state`, `session_segment_closed { case_id, origin_id, segment }`, `session_mark_added { case_id, origin_id, mark }` y `screenshot_taken.session_mark`.
- **API:** `PUT /api/cases/{id}/recording-session/settings { enabled, with_camera }` y `PUT /api/cases/{id}/recording-session`, que recibe el snapshot de §7.5 con `connection_id` y solo los tramos `saved`/`discarded`.
- **`Case`, config y ZIP:** `Case.recording_session` (§7.6.1), `ManifestFile.zip_folder` (`"sesion" | null`, se pasa tal cual a `buildZip`), `recording_session_mode` y `recording_session_segment_minutes`.
- **Eventos:** los 5 tipos con `enabled`, `with_camera`, `track`, `segment_index`, `at`, `duration_seconds`, `validation`, `recovered`, `gap_seconds` y `segment_lost`.
- **Códigos:** los de §7.7.
- **agent-ui:** `POST /agent/prepare-shutdown` → `{ had_session, segments_saved, pending }`. Electron solo lee `had_session`.

## Decisiones no obvias

1. **Fuente de verdad y sync.**
   - El hook refleja Tatana: WS + `/agent/state` + `GET .../session`.
   - Para sincronizar con el backend, relee el diario y lo manda con `toBackendSnapshot`. Usa debounce de 1 s y corre de a una sincronización, con encolado.
   - Si la sesión web vence, deja `pendingSync` y reintenta con `onSessionRestored`.
   - Con otro error (backend caído, `recording_session_busy`), reintenta a los 15 s.
   - Un 404 oculta la función (`backendSupported=false`) y `recording_session_not_enabled` descarta el pendiente.
2. **Tramos a la bandeja.**
   - Llegan por `session_segment_closed` y además por cada lectura del diario (`feedSavedSegments`), con dedup en `addStoredFile`. Así se registran los tramos cerrados con la página cerrada, los recuperados tras cerrar Tatana y los que quedaron "Pendiente de registrar" por la sesión vencida.
   - El grupo "Sesión" se arma con la unión del diario de Tatana, la copia del caso y los tramos ya llegados por WS (`arrivedSegments`). Así un tramo no aparece un segundo en "Grabaciones" antes de que el diario lo liste.
3. **Activación automática.**
   - Una vez por entrada al paso 3 y por caso: un "Detener" del perito no se deshace solo.
   - Espera la primera lectura del estado de Tatana. Si no, no vería un `interrupted` previo.
   - El primer paso 3 de un caso guarda el switch con `defaultEnabled(mode)` (una vez por caso).
   - No arranca con: iPhone, celular desconectado o distinto del del caso, `agentLock`, modo `disabled` o backend sin rutas.
4. **"Tatana se cerró…".** Al volver al paso 3, D3 hace que la sesión se reanude sola. El aviso de §9.4 queda como `interruption`, descartable, y no se pierde cuando arranca el tramo nuevo. "Reanudar" se ofrece solo si la sesión sigue detenida.
5. **"Reanudar" sin duplicar botones.** El indicador ofrece "Reanudar" solo si ningún aviso lo ofrece ya (`noticeOffersResume`). Los avisos de `sin_espacio`, `fallo_grabacion` e `interrupted` traen el suyo.
6. **Anuncios.**
   - El `role="status"` del indicador anuncia "iniciada", "Tramo N guardado" (con `session_segment_closed`), "reanudada" y "detenida".
   - La primera lectura tras una recarga es silenciosa (`applyState(..., {silent})`) y muestra "Grabando sesión desde HH:MM".
   - El aviso "se reanudó a las HH:MM (tramo N)" (info, 8 s) sale en la reconexión automática (`waiting_device` → `recording`).
7. **Salidas del paso 3.**
   - `go()` pasa a ser un wrapper que espera el `stop("cambio_de_paso")` y navega igual si falla, como dice la SDD; el paso 5 bloquea.
   - "Guardar…" usa `goNow` después del stop, así el último tramo entra en la misma cola.
   - Salir del asistente usa `stopDetached`, sin esperar.
   - Para el estado de carga no se tocó cada botón: muestra "Cerrando la grabación de la sesión…" en el botón principal del paso y en el banner de estado (navegación por riel o "Atrás").
8. **"Toast" de la marca.** No hay sistema de toasts en el repo. "Marca guardada · tramo N, minuto mm:ss" va como línea `role="status"` debajo de los tiles y se borra a los 6 s.
9. **Línea del reloj en el panel.** Usa `describeEventSkew` (#79) en presente ("el dispositivo adelanta 10 s respecto de la PC (± 1 s)"). El texto en pasado de §8.4 es del informe (backend).
10. **Indicador sticky y foco.** El indicador está sticky en `top-2` (`z-30`). Para que no tape el elemento enfocado con teclado, el contenedor con scroll suma `scroll-padding-top: 5rem` mientras la sesión está activa en el paso 3. Viene del autochequeo de web-design-guidelines ("sticky must not cover focus").
11. **agent-ui.** La función vive en `session-shutdown.ts` para usarla desde `index.ts` e `ipc.ts`. El texto se partió en `message` + `detail`, que juntos son el de §10. `before-quit` no se intercepta (Cmd+Q, cierre del SO): queda cubierto por la recuperación de Tatana, como dice la SDD. El camino del auto-update no cambia.

## Skills invocados

- **ui-ux-pro-max** (antes del JSX). Consultas: `live region status announcements`, `popover escape focus return` y `switch toggle disabled reason` (`--domain ux`). Se aplicó:
  - un solo `role="status"` atómico para los cambios, sin anunciar el cronómetro;
  - foco visible y foco que no queda tapado (→ `scroll-padding-top`);
  - estado deshabilitado claro (opacidad + `cursor-not-allowed` + `aria-disabled` con el motivo visible en el `required`);
  - objetivos de 44 px (switch con área táctil extendida, botones `min-h-11`);
  - ícono + texto, nunca solo color.
- **senior-frontend.** Se aplicó:
  - estado del hook con refs para callbacks estables (WS y timers);
  - sync encolado sin carreras, con guardas de caso (`caseIdRef`) en cada `await`;
  - funciones puras aparte;
  - defaults estables (`NO_NAMES`);
  - sin re-renders por segundo fuera del indicador.
- **3d-web-experience** (como criterio). Sin hallazgos aplicables: no se agregó 3D, ni pseudo-3D, ni animaciones sin propósito.
- **ui-styling.** El repo usa PrimeReact + tokens `fx-*` (no shadcn). Se usaron tokens existentes (`bg-fx-danger-soft`, `border-fx-warning`, `shadow-fx-1`…) y componentes del repo (`FxBanner`, `ConfirmDialog`, `CopyButton`, `InputText`/`Button` de Prime). Hay un switch propio porque el repo no tiene ninguno.
- **mblode-agent-skills-ui-animation.** Solo `transform`/`opacity`/color. El popover entra desde el tile (`origin-top`, 150 ms, fade) y el thumb del switch con `translate`. El punto rojo usa `motion-safe:animate-pulse` y todo respeta `prefers-reduced-motion` (`motion-safe`/`motion-reduce`). No se animan propiedades de layout.
- **web-design-guidelines** (autochequeo final sobre los archivos tocados; guía de Vercel actualizada). Hallazgos corregidos:
  - el input sin `name` → `name="session-mark-text"`;
  - el sticky tapaba el foco → `scroll-padding-top`;
  - la etiqueta del tramo se truncaba a 390 px → `break-words`.

  El resto estaba en regla: `…` en los estados de carga, `translate="no"` en nombres y hashes, `tabular-nums` en el cronómetro, íconos decorativos con `aria-hidden` y botones de ícono con nombre accesible.

## Para el reviewer / prueba manual

- §16.2, pasos 1–13 y 15–16: necesitan el Tatana de esta rama y el backend con las rutas.
- Paso 10 [DU3]: con la sesión grabando, "Salir" o "Reiniciar agente .NET" desde la bandeja (o "Reiniciar" desde la ventana) muestra el diálogo.
- No hay commit: lo hace el orquestador.

## Reintento 1 (QA)

Hallazgos de `docs/qa/2026-10-14/grabacion-sesion-completa-manual.md` que tocan al frontend:

| Hallazgo | Arreglo | Archivo |
|---|---|---|
| **H-3 (Alta).** "Retomar" pisaba `enabled` con el default de la instalación. | Antes de escribir el default, el hook carga el caso completo (`api.getCase`). Si ese caso ya trae `recording_session`, lo usa (`onCaseUpdated(fresh)`) y no escribe nada; solo si de verdad no tiene ajustes guarda `defaultEnabled(mode)`. Si `getCase` falla, no escribe y se reintenta en la próxima pasada. Así sirve para cualquier camino que deje en memoria el `Case` del listado: `proceedResume`, el "Retomar" del modal, etc. `proceedResume` no cambió: su propio `getCase` sigue reemplazando el caso cuando llega. | `client/src/hooks/useRecordingSession.ts` (bloque "1. Primer paso 3") |
| **H-2 (Media).** El detalle del caso no mostraba "Registro audiovisual". | `CaseDetailContent` usa un `SessionRecordSection` nuevo. Si el `Case` recibido no trae `recording_session` (el del listado), carga `GET /api/cases/{id}` y muestra el panel con la sesión y la ficha del caso completo. Si no hay sesión o falla el pedido, no muestra nada. `CaseDetailModal` no cambió. | `client/src/components/case-detail/CaseDetailContent.tsx` |
| **B-4.** La trazabilidad usaba `duration_seconds`; el informe, `recorded_seconds`. | `recording_session_segment_closed` muestra `formatDuration(detail.recorded_seconds)`, la misma cifra que el informe. Se agregó `CaseEventDetail.recorded_seconds?` en `lib/api.ts`. | `client/src/lib/case-events.ts`, `client/src/lib/api.ts` |
| **B-6.** `GET /session/capacity` iba sin el tramo de la instalación. | `agent.sessionCapacity(camera, segmentMinutes)` manda `segment_minutes`, y el hook pasa el tramo de la config pública. | `client/src/lib/agent.ts`, `client/src/hooks/useRecordingSession.ts` |

**Dependencia de backend para B-4.** Hoy `CaseEventDetail` del backend (`Models/CaseEvent.cs`) **no** tiene `RecordedSeconds`: el evento trae solo `duration_seconds`. Se necesita que `implementer-backend` agregue `RecordedSeconds` (JSON `recorded_seconds`, `[BsonIgnoreIfNull, JsonIgnore(WhenWritingNull)]`) al detalle de `recording_session_segment_closed`. Mientras no esté, la web muestra "Se guardó el tramo N de la pantalla" sin duración, para no contradecir al informe.

**Verificación.** `client$ npx tsc --noEmit` pasa sin errores. `agent-ui/` no se tocó en este reintento.
