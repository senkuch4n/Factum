# impl_frontend — wizard-concurrencia-pestanas (#61, Bloque 1)

**Estado:** done · **App:** solo `client/` (agent-ui no se tocó) · **Rama:** `feat/wizard-concurrencia-pestanas` (sin commit, como se pidió)
**SDD:** `Refactorizaciones/wizard-concurrencia-pestanas.md` §8.3 / §9.3 (F1–F21)

## Verificación

```
cd client && npx tsc --noEmit   →  exit 0, sin errores
```

La SDD no pide tests de cliente (no hay runner en `client/`). No se corrió `next build` (regla dura) ni se tocó el dev server de :3000. No se usó el celular ni se tocó la base.

## Archivos

Nuevos:
- `client/src/lib/tab-origin.ts`: `getTabOriginId()` (F1, DT2). `crypto.randomUUID` y, si no está, 16 bytes de `getRandomValues` en hex.
- `client/src/lib/agent-event-filter.ts`: `shouldAcceptAgentEvent(type, data, ctx)` + `AttributionCtx`, con el JSDoc de la tabla de reglas (F2, DT1).
- `client/src/lib/tab-channel.ts`: `BroadcastChannel("factum-wizard")` (`postTabMessage`, `subscribeTabMessages`, `parseTabMessage` con la validación de §5.3, descarta los mensajes propios), `withGenerationLock` (Web Locks con `ifAvailable`, y un fallback con avisos `generating` y TTL de 120 s) e `isGenerationBusyFallback` (F5, DT9).
- `client/src/hooks/useTabPresence.ts`: presencia (F6, DT16). Emite al cambiar algún dato, cada 10 s y una sola vez a una pestaña desconocida. Manda `bye` en `pagehide` y al desactivarse. Las entradas vencen a los 30 s.

Modificados:
- `lib/agent.ts` (F3): `AgentAttribution`, `attributionParams`/`attributionBody` (siempre suman `origin_id`; con `null` el pedido sale sin dueño). Atribución en `takeScreenshot`, `stopRecording`, `stopAirplayShot`, `startRecording` (body `case_id` + `origin_id`) y `captureWebcam`. Suma `takePreview`, `previewFileURL` y `PreviewUnsupportedError`. El 404 se recuerda en una variable de módulo y se resetea en `resetCapabilities()`. Tipos `ScreenshotTakenData` y `PhotoTakenData`; `origin_id` en `RecordingStartedData`/`RecordingStoppedData`.
- `lib/api.ts` + `types/index.ts` (F4): `AgentAuditAction` suma `capture_failed`. Tipo `AgentFailedAction`, `reportAgentEvent(action, caseId?, extra?)` (`failed_action`, `reason` con trim y recorte a 300), `CaseEventType` suma `evidence_updated`/`capture_failed`, `CaseEventDetail` suma `previous_sha256`/`failed_action`. `ReportCounts`, y `operaciones_base` en `ReportTextDefaults`, `ReportTextsRequest` y `ReportTexts`. `EvidenceErrorCode` suma `evidence_busy`.
- `components/capture/StageScreen.tsx` (F7): `SafeImg` guarda `erroredSrc` y muestra el ícono solo si `erroredSrc === src`.
- `hooks/useFileManager.ts` (F8): `saveOne` marca `storedInCase: true` (con `sha256`/`size`) apenas termina el import o el upload, antes del registro. `removeFile` devuelve `Promise<boolean>` y avisa por `onError` si está bloqueado. Mensaje de `evidence_busy` en `runQueue`.
- `hooks/useRecording.ts` (F9): `opInFlightRef` único para start, stop y modo iOS. `RecordOutcome`, que devuelven `handleToggleRecord` y `handleSelectIosMode`. El stop manda `{ caseId }`. `stopForeign` detiene **sin** atribución.
- `hooks/useAirplayShotSession.ts` (F10): un guard por acción. `handleStop(serial, onError, attribution)` devuelve los `files`. Se actualizó el comentario del encabezado.
- `app/dashboard/page.tsx` (F11): `acceptsEvent` + filtro en `handleWsEvent` (screenshot, photo, recording_started y recording_stopped). Guard de `handleScreenshot` + `capture_failed`. `runRecordAction`/`reportRecordOutcome` (auditoría con la confirmación de Tatana). Marcas AirPlay desde HTTP. `handleGenerate` con `generateInFlightRef` y `setLoad` en el primer clic, y `withGenerationLock` → `generateNotice`. `zip_in_progress` va a `generateNotice`. Historial: `startWizard`, `handleSaveCase` (+ `cases_changed`), `visibilitychange` cada 15 s como mucho y suscripción al canal. `useTabPresence` + banner info. `onRemoveFile` asíncrono. Props nuevas de `GenerateStep`.
- `components/overlay/ConfirmDialog.tsx` (F12, DT14): `onConfirm` puede ser asíncrono (spinner, botones deshabilitados y Escape o click afuera bloqueados mientras corre; se cierra si no resuelve `false`). Props nuevas `error`, `children` y `dismissableMask`. Los dos usos que ya existían no cambian.
- `components/CaptureStep.tsx` (F13): `removeItem` abre un único `ConfirmDialog` para los cuatro disparadores, con `RemovePreview` (miniatura `SafeImg` o ícono del tipo, nombre en mono con `translate="no"` y tipo + tamaño). El error se ve dentro del modal. Con `removeBlocked` no se abre.
- `components/CaseFormStep.tsx` (F14): `takeShot` usa `takePreview` + `previewFileURL`. Si `PreviewUnsupportedError`, usa `takeScreenshot(serial, platform, null)`.
- `components/ReportStep.tsx` (F15): `pendingBaseRef` va en el PUT (también en el guardado al desmontar) y se limpia cuando el PUT sale bien. Base en la carga inicial sin textos. "Restaurar texto por defecto" de Operaciones pide defaults frescos. Aviso warn + "Actualizar cantidades" y después "Cantidades actualizadas. Deshacer".
- `components/dashboard/GenerateStep.tsx` (F16): pide los defaults al montar (best-effort). Banner warn con "Revisar texto". `ConfirmDialog` neutro "Generar igual" / "Volver". Prop `notice` (+ `onDismissNotice`).
- `lib/case-events.ts` + `components/case-detail/CaseTimeline.tsx` (F17): íconos `RefreshCw`/`AlertTriangle`, `EventTone` con `warning` (tokens `fx-warning`, los mismos de `FxBanner tone="warn"`), títulos por `failed_action`, motivo también para `capture_failed`, `eventHashChange` en mono.
- `lib/agent-messages.ts` (F18): `GENERATION_BUSY_MESSAGE`; `zip_in_progress` + `action: "generate"` devuelve ese texto.
- `lib/pericial.ts`: `sameCounts`, `countsOutdated`, `countsSummary` (omite archivos extraídos y otros cuando son 0).
- `hooks/useAuth.ts` (F19): `loadHistory({ silent })`.
- `components/WebcamCaptureModal.tsx` (DT8): guards de `capture` (`toBlob` es asíncrono) y de "Usar esta foto".

## Reconciliación con #59 (ya en develop)

- `startRecording` ya mandaba `case_id` (#59). Se mantuvo la firma (`caseId` como sexto parámetro) y solo se agregó `origin_id`. No se cambió a `attribution`.
- `useRecording` ya leía el caso por `opts.getCaseId()`. El stop toma de ahí el `caseId`, así que `handleToggleRecord`/`handleSelectIosMode` **no** reciben un parámetro `attribution` (en la SDD sí). Comportamiento equivalente.
- La regla `mine` de #59 en `recording_stopped` se conserva **sumada** al filtro DT1: `mine = accepted && !foreignStop && (case_id ? sameId : origin_id ? true : isRecordingRef)`.
- `recording_started`/`recording_stopped` rechazados igual llaman a `refreshRecording()` cuando hay `recording_state_v1`. Es la reconciliación por `/agent/state` de #59 (aviso de grabación ajena, recuperación de las de este caso): no importa nada de otro caso. Sin esa capability, un evento rechazado no toca `setRecording`.
- No se duplicó nada de `useEvidenceNotices` ni de `ElapsedTime`. El borrado confirmado sigue llamando a `forgetNotice`, pero solo si salió bien.

## Decisiones no obvias

1. **Orden de reglas de DT1:** "`case_id` distinto → no" va **antes** que "`origin_id` propio → sí". Así, si la pestaña cambió de caso con una captura o grabación suya en vuelo, esa evidencia no cae en el caso nuevo. Coincide con la regla `mine` de #59. En los demás casos da lo mismo que la tabla de la SDD. Está documentado en el JSDoc.
2. **Atribución `null`** (sin `case_id` ni `origin_id`) en dos lugares: el fallback de la vista previa con un Tatana viejo (DT5) y `stopForeign`, para que una grabación ajena detenida desde esta pestaña no vuelva a ella por la regla del origen.
3. **Reintento de `saveOne` después de un registro fallido:** como `storedInCase` ahora se marca temprano, si el archivo ya se movió (`storedInCase` + `sha256` + `size`, sin Blob) no se vuelve a llamar a `importEvidence`. Si no, el reintento caería en `file_not_found`. Se usan el hash y el tamaño que devolvió el import.
4. **Detección del 404 de `/preview`:** un 404 **sin** `{ error }` es la ruta que no existe (Tatana viejo). Un 404 con `error` se trata como un error común de captura.
5. **`ConfirmDialog` asíncrono:** mientras corre la promesa, `closeOnEscape` y `dismissableMask` quedan apagados, para que el borrado no siga en segundo plano con el modal cerrado. Si la promesa lanza, el diálogo queda abierto (el llamador muestra `error`).
6. **Lock de Generar:** guard + `setLoad("generate")` en la primera línea de `handleGenerate` (los dos flujos). `handleGenerateAgent(cas)` corre adentro de `withGenerationLock` y ya no maneja el loading. El `"busy"` no llama al backend.
7. **Presencia:** el label es `currentCase.nro_referencia` o el expediente que se está escribiendo en el paso 2 (si no hay ninguno, "caso nuevo"). El banner lleva el ícono `Smartphone`.
8. **`removeFile` bloqueado** (guardado en curso) ahora devuelve `false` con el mensaje "Esperá a que termine el guardado de la evidencia para borrar." En la UI no se llega a ese punto, porque `removeBlocked` no abre el diálogo.

## Contrato usado (coincide con la SDD §6)

Tatana: query `case_id`/`origin_id` en `screenshot`, `record/stop` y `screenshot/airplay/stop`. Body `case_id`/`origin_id` en `record/start` y `webcam/capture`. `POST /devices/{serial}/preview?platform=` → `{ filename, url }`, con `url = "/previews/…"`. Eventos WS con `serial`/`case_id`/`origin_id`.
Backend: `action: "capture_failed"` + `failed_action` (`capture_start`/`capture_stop`/`screenshot`) + `reason`. `detail.previous_sha256`, `detail.failed_action` (`capture_video_start`/`capture_video_stop`/`capture_screenshot`/`capture_photo`). `operaciones_base { capturas, grabaciones, archivos_extraidos, otros }`. `code: "evidence_busy"`.

**Alineación con el backend:** cuando terminé, `progress/impl_backend_wizard-concurrencia-pestanas.md` todavía **no existía**. Revisé el código del backend que estaba en el working tree: `ScreenshotController`, `AirplayShotController` y `RecordingController.Stop` usan `[FromQuery(Name = "case_id")]`/`"origin_id"`, `PreviewController` tiene `POST devices/{serial}/preview` y `GET previews/{filename}`, `CaptureRequest(Type, CaseId, OriginId)`, `ReportAgentEventRequest.FailedAction`, `ReportTextsDto.OperacionesBase` y `EvidenceErrorCodes.EvidenceBusy = "evidence_busy"`. Todo coincide con los nombres que asumí arriba. Si el backend cambia alguno, se revisa contra esta lista.

## Skills (constancia)

- **ui-ux-pro-max** (antes del JSX final): consultas `confirmation destructive dialog` y `loading button double submit` (dominio ux). Lo que se aplicó: confirmación antes de borrar, botón deshabilitado con spinner mientras corre la acción asíncrona, el error al lado de la acción (dentro del modal) y avisos no bloqueantes con una acción concreta ("Actualizar cantidades", "Revisar texto").
- **senior-frontend**: guards con `useRef` antes del primer `await`, wrappers de módulo tolerantes a SSR y a navegadores sin `BroadcastChannel`/`navigator.locks`, callbacks estables (`acceptsEvent` sin deps, getters por ref), sin `setState` en render.
- **3d-web-experience** (criterio): no se agregó 3D, pseudo-3D ni animación nueva. Los banners usan el `fx-fade-in` que ya tiene `FxBanner` (con `motion-safe`). Sin hallazgos aplicables.
- **ui-styling**: solo tokens que ya existen (`fx-warning`, `fx-warning-soft`, `fx-surface-2`/`-3`, `fx-border`, `rounded-fx-sm`/`-md`; verificados con grep), sin colores crudos. Claro y oscuro salen de los tokens.
- **web-design-guidelines** (autochequeo, se bajaron las guías de Vercel): íconos con `aria-hidden`; `alt=""` en la miniatura del modal (el nombre ya está como texto); `…` tipográfico en el hash; textos largos con `break-all`/`min-w-0`; la confirmación destructiva está (antes era borrado inmediato); el modal tiene foco inicial en Cancelar y Prime devuelve el foco al cerrar; `aria-busy` en el diálogo mientras borra; los avisos usan `FxBanner` (`role=status`/`aria-live`). Sin hallazgos pendientes.
- **mblode-agent-skills-ui-animation**: no se invocó porque no hay transiciones ni gestos nuevos (el diálogo usa la animación de Prime que ya existía y los banners, la de `FxBanner`).

## Pendiente / para la prueba manual (§10.3)

- Para probar de punta a punta hace falta el backend y el Tatana de esta HU. Con un Tatana viejo funcionan el fallback de vista previa y la regla 6.
- No se probó en el navegador: no hay celular y no se arrancó otra instancia. Lo cubre la prueba manual del usuario, §10.3 pasos 1–11.

## Corrección tras review 1

Hallazgo del reviewer (`progress/review_wizard-concurrencia-pestanas.md`): el
`ConfirmDialog` "Generar igual" de `GenerateStep` recibía la promesa de
`handleGenerate` (`onConfirm={() => onGenerate()}`), y como `ConfirmDialog`
(DT14) trata cualquier thenable como acción async, quedaba abierto con spinner
durante todo el armado del ZIP, bloqueando Escape/clic afuera y tapando el
progreso.

**Cambio** (único archivo tocado): `client/src/components/dashboard/GenerateStep.tsx`
- `onConfirm={() => { onGenerate(); }}` — cuerpo con llaves para no devolver la
  promesa; el diálogo cierra enseguida y la generación sigue corriendo con
  `GenerateStages` visible. Comentario inline explicando por qué.
- El guard de doble clic y el Web Lock de A3 no se pierden: viven dentro de
  `handleGenerate` (dashboard/page.tsx), no en el diálogo.

**Revisión del resto de usos de `ConfirmDialog`** (mismo patrón accidental):
- `CaptureStep.tsx:373` `confirmRemove` → async **a propósito** (DT14, borrar
  archivo con error inline). Se deja.
- `ReportStep.tsx` (`confirmRestore`, `closePicker`), `ReportVersionHistory.tsx`
  (`confirmRestore`), `dashboard/page.tsx` (`resetWizard`), `BrandingDialog` /
  `AccountFormDialog` (`onHide` = setters sincrónicos del padre),
  `AdminAccountsScreen` (`void runReactivate/runReset`, ya descartan la promesa),
  `DesignSystemShowcase` → todos sincrónicos. Sin cambios.
- `ResumeDeviceModal` y `SuspendAccountDialog` no son `ConfirmDialog`.

**Autochequeo web-design-guidelines** (GenerateStep + ConfirmDialog, guías de
vercel-labs bajadas en el momento): sin hallazgos nuevos. El progreso de la
generación queda anunciado por la región `role="status"`/`aria-live="polite"`
existente de `GenerateStep`; el estado de carga del botón "Generar" sigue
siendo el del flujo normal. Observación no bloqueante: al cerrar, Prime
devuelve el foco al disparador ("Generar"), que durante la carga queda
deshabilitado — mismo comportamiento que el clic directo sin diálogo, fuera
del alcance de esta corrección.

**Verificación:** `cd client && npx tsc --noEmit` → exit 0, sin errores.
