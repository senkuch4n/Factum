# Review — wizard-concurrencia-pestanas (#61, Bloque 1)

**Veredicto:** RECHAZADA (frontend, 1 cambio puntual)

Verificado por el reviewer (no por los progress): `dotnet build` de Backend y Agent OK (0 errores; solo los NU1902/NU1903 preexistentes), `dotnet test` Agent 531/531, Backend 920 ok / 7 skipped preexistentes / 0 fallos, `npx tsc --noEmit` en `client/` limpio, `./ops/harness/verify.sh` "Arnés OK". Rama sobre develop (`ddf94bb`, con #59 y #60). No se tocó agent-ui (correcto según SDD).

## Checkpoints
- C1 arnés: [x] HU #61 en `en_revision`, rama `feat/wizard-concurrencia-pestanas` desde develop; verify.sh exit 0.
- C2 docs: [x] HU con Validación del usuario, SDD con checklist y Contrato compartido. [x] Contrato campo por campo coincide en los tres lados (ver abajo).
- C3 arquitectura: [x] Solo Backend, Agent y client (los lados de la SDD). [x] Modelos tolerantes (`EvidenceRevision` 0/`Exists false`, `OperacionesBase` `BsonIgnoreIfNull`, `AgentEvent` + `BsonIgnoreExtraElements`, enum `CaptureFailed` al final = 5). [x] Sin console.log/TODO sueltos. [ ] `client/AGENTS.md`/UX: ver cambio requerido 1 (no es de arquitectura Next, pero rompe un flujo de la SDD).
- C4 verificación: [x] build, [x] tsc, [x] tests reales (EventAttribution, PreviewStorage, RecordingAttribution, EvidenceRegistrationPlan, AgentAuditMapping, ReportTextsValidation/B10). [ ] No hay test de la lógica de reintento de `AgentGeneration.RegisterEvidenceAsync` ni del append condicional (necesitan Mongo); quedan como prueba manual (observación).
- C5 cierre: [x] impl_backend e impl_frontend existen y describen lo tocado. [x] Constancia de las 4 skills obligatorias en impl_frontend (mblode-ui-animation justificada como no aplicable). [x] Sin temporales; el implementador limpió por `_id` propio (según su reporte, sin tocar datos ajenos).

## Contrato compartido (verificado en código)
- Tatana -> client: `screenshot_taken` `{filename,url,serial,case_id,origin_id}` (ScreenshotController, IosService AirPlay con serial de la ruta); `photo_taken` `{type,filename,url,case_id,origin_id}` sin serial; `recording_started` `{serial,platform,case_id,origin_id,started_at,filename}`; `recording_stopped` suma `serial`, `case_id`, `origin_id` (también en el 422 con `filename:null`). Query `case_id`/`origin_id` con `[FromQuery(Name=…)]` en screenshot, record/stop y airplay/stop; body en record/start y webcam/capture. `POST /devices/{serial}/preview` -> `{filename,url:"/previews/…"}` sin broadcast, `GET /previews/{filename}` con regex estricta, `no-store`, 404. Capabilities `event_attribution_v1`, `device_preview_v1`. Tipos de `agent.ts` coinciden.
- Backend -> client: `action:"capture_failed"`, `failed_action`, `reason` (trim, 300), 400 sin `failed_action`; `evidence_updated` con `detail.previous_sha256`; `capture_failed` con `detail.failed_action` (`capture_video_start|stop|capture_screenshot|capture_photo`); `operaciones_base {capturas,grabaciones,archivos_extraidos,otros}` en defaults, PUT (conserva si ausente; rango 0..100000) y `report_texts`; `evidence_busy` (409) mapeado en `useFileManager`. `evidence_revision` no sale en JSON.
- Reconciliación con #59/#60: bien resuelta. Se reutiliza `RecordingRegistry` (SetOwner/TakeOwner/PeekOwner) sin singleton duplicado, `case_id` sigue saliendo del servicio de #59, `400 invalid_case_id` y 409 `not_recording` conservados, `TakeOwner` en `finally`, regla `mine` de #59 sumada al filtro DT1, `stopForeign` sin atribución. No se duplica código ni se rompe #59/#60.

## Verificación por requisito
- A1 atribución por pestaña: [x] `agent-event-filter.ts` + `acceptsEvent` en `handleWsEvent` antes de cualquier efecto (el cambio de orden case_id/origin_id respecto de la SDD está documentado y es más seguro). Vista previa fuera de la raíz (`previews/`, una por serial, limpieza al arrancar).
- A3 doble clic: [x] guards sincrónicos antes del primer await (generar, pantalla, grabación, AirPlay, webcam) y `withGenerationLock` con Web Locks `ifAvailable` + fallback. [ ] ver cambio 1 (confirmación "Generar igual").
- D4 idempotencia: [x] `PlanRegistration` (no-op sin escritura ni eventos, `evidence_updated` con otro sha) + revisión optimista + `$inc` en alta y baja, 3 intentos y 409.
- A5 custodia ante errores: [x] `capture_start`/`capture_stop` se reportan tras la confirmación de Tatana; rechazos -> `capture_failed` con el texto visto por el perito; `busy`/`picker` no reportan.
- ConfirmDialog sin `confirm` nativo: [x] (grep sin `window.confirm`); diálogo único para los 4 disparadores, foco en Cancelar, error dentro del modal, mask cancela.
- A4 conteos: [x] aviso en paso 5 con "Actualizar cantidades"/"Deshacer", aviso en GenerateStep con "Revisar texto"; defaults frescos al restaurar.
- A6: [x] recarga silenciosa en `startWizard`, `handleSaveCase`, `visibilitychange` (15 s) y `cases_changed`; `CaseFormStep` usa `historyCases` vivo.
- B6: [x] `takePreview` con fallback sin atribución y 404 recordado; sin evidencia en la raíz.
- M8: [x] `SafeImg` con `erroredSrc` + `storedInCase` temprano (y reintento de `saveOne` sin re-importar).

## Cambios requeridos
1. **frontend** — `client/src/components/dashboard/GenerateStep.tsx:397`: `onConfirm={() => onGenerate()}` devuelve la promesa de `handleGenerate` (async, aunque el tipo diga `() => void`). `ConfirmDialog` (DT14) trata cualquier thenable como acción asíncrona: deja el modal "Generar igual" abierto con spinner, bloquea Escape/clic afuera y tapa el progreso en dos tramos, el aviso de "otra pestaña" y el "Generando…" durante todo el armado del ZIP (minutos), y recién cierra al terminar. La SDD pide que "Generar igual" proceda y el modal se cierre. Corregir para que no devuelva la promesa (p. ej. `onConfirm={() => { onGenerate(); }}`); idealmente tipar `onGenerate` coherente con eso.

## Observaciones no bloqueantes
- Sin tests automatizados del loop de `RegisterEvidenceAsync` ni de `AppendReportVersionAsync` (requieren Mongo); el implementador los probó por API con un caso propio y limpió por `_id`. Queda para la prueba manual §10.3.
- `photo_taken` no se verificó en mock (el mock de webcam falla desde antes de esta HU) ni AirPlay (requiere iPhone); cubierto por build y revisión de código.
- Jitter con `Task.Delay(Random…)` en el reintento de registro: razonable (documentado), solo hasta 3 intentos.
- Reiniciar backend `:8080` y Tatana `:8765` antes de la prueba manual (el build pisó los binarios).

## Re-revisión (corrección 1)

**Veredicto:** APROBADA

- Cambio requerido 1: [x] `GenerateStep.tsx:401` ahora es `onConfirm={() => { onGenerate(); }}`; ya no devuelve la promesa, `ConfirmDialog` la trata como sincrónica (`isThenable` falso) y llama `onOpenChange(false)`: el modal cierra y la generación sigue con el progreso visible.
- A3: [x] sin pérdida; el guard de doble clic y el Web Lock viven en `handleGenerate` (dashboard/page.tsx), no en el diálogo. El otro camino (`GenerateStep.tsx:189`) ya llamaba `onGenerate()` sin devolverlo.
- Otros usos de `ConfirmDialog`: [x] `CaptureStep` es async a propósito (DT14); el resto (`ReportStep`, `ReportVersionHistory`, `dashboard/page.tsx` resetWizard, `BrandingDialog`, `AccountFormDialog`, `AdminAccountsScreen`, showcase) pasan setters o callbacks sincrónicos. Sin el mismo problema.
- Verificación: `npx tsc --noEmit` en `client/` exit 0. Backend: ningún archivo de `server/` es posterior a la revisión anterior (sin build/tests nuevos necesarios). Único archivo de código tocado: `GenerateStep.tsx`.
