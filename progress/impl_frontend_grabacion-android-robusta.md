# impl_frontend — grabacion-android-robusta (#59)

**Estado:** done (código + tsc). La prueba en el navegador de §11.3 queda **pendiente**; el motivo está en "Verificación".
**Rama:** `feat/grabacion-android-robusta` (sin commit; base `f71fa63`). **App:** solo `client/`; `agent-ui/` no se tocó.
**Fecha:** 2026-10-13.

## Archivos tocados

| Archivo | Checklist | Qué |
|---|---|---|
| `client/src/lib/agent.ts` | F1 | Tipos `RecordingValidation(Status)`, `RecordingPhase`, `RecordingStateEntry`, `UnclaimedRecording`, `AgentRecordingState`, `RecordingStartedData`, `IosRecordMode` (se movió acá, ver decisiones). `RecordingStoppedData`: `filename`/`url` pasan a `string \| null`, más `serial`, `case_id`, `validation`, `error`, `code`. `AgentErrorCode` suma `already_recording`, `recording_start_timeout`, `not_recording`, `recording_empty`, `recording_unreadable`, `empty_evidence`. `AgentErrorBody.validation`. `startRecording(…, caseId)` y `stopRecording` con `agentJSON` (lanzan `AgentError`). Nuevos `agentState()` (3 s, `null` ante error o Tatana viejo), `supportsRecordingState()` (cache por conexión) y `resetCapabilities()`. Doc de capabilities con `recording_state_v1`. |
| `client/src/lib/agent-messages.ts` | F2 | Acción `record`. `RECORDING_ERROR_TEXT` con los textos de §7.2: para los códigos de grabación se usa el `error` de Tatana si viene y, si no, el texto local. `empty_evidence` usa siempre el texto local con `name`. Con `action: "record"` y un código sin caso propio (iPhone `ios_*`), se muestra el `error` de Tatana tal cual, como antes. |
| `client/src/types/index.ts` | F3 | Re-exporta los tipos nuevos. `IosRecordMode` ahora se re-exporta desde `lib/agent`. |
| `client/src/lib/black-image.ts` (nuevo) | F4 | `ratioDark` (pura), `isMostlyBlack`, `BLACK_LUMA = 8` y `BLACK_RATIO = 0.99`. Hace `fetch` con `force-cache` → `createImageBitmap` a 48×96 → `OffscreenCanvas` (o `<canvas>`). Cualquier error devuelve `null`. No toca `<img>` ni `SafeImg`. |
| `client/src/hooks/useEvidenceNotices.ts` (nuevo) | F5 | `notices`, `setRecordingValidation`, `keepBlack`, `forget` y `summary`. Persiste en `sessionStorage` `factum:evidence-notices:<caseId>`. Analiza las capturas negras una vez por nombre, de a 2 en paralelo, con un `AbortController` por caso. |
| `client/src/hooks/useRecording.ts` | F6 | Firma `useRecording(addFile, expectVariants, opts)`. Estado nuevo: `startedAt`, `remotePhase`, `foreignRecording` y `recordingRecovered`. Refs: `foreignStopsRef`, `inFlightRef` y un contador de generación. Funciones nuevas: `syncFromAgentState`, `refresh` (una ejecución a la vez y, si piden otra, se encola una sola), `stopForeign` (nunca llama a `addFile`) y `sameId`. Errores por código. |
| `client/src/app/dashboard/page.tsx` | F7 | `useEvidenceNotices`; `useRecording` con getters por ref. `recording_started` hace `refresh` si el Tatana tiene la capability y, si no, `setRecording(true)`. `recording_stopped` aplica la regla `mine` (más `foreignStopsRef`), guarda la validación y muestra el error de una falla ajena a esta pestaña. `handleAgentConnected` invalida las capabilities y hace `refresh`. Efecto nuevo del paso 3. `devices_changed` (cuando vuelve el celular) hace `refresh`. Importa las `unclaimed` y muestra el banner info, que se cierra solo a los 6 s. Corrige el texto del banner "Retomando". Pasa las props nuevas a `CaptureStep` y `GenerateStep`. `onRemoveFile` llama a `forget`. |
| `client/src/components/CaptureStep.tsx` | F8 | Botón con los textos y estados nuevos ("Iniciando…"/"Deteniendo…" también por `recordingPhase`). Texto de ayuda `role="status"` debajo de los botones. Cronómetro y "Grabando desde HH:MM". Aviso de grabación ajena (`FxBanner info`) con "Detener esa grabación": mientras está, "Grabar" queda deshabilitado con `aria-describedby` al aviso. Aviso de captura negra con "Conservar"/"Borrar". `notice` en los tiles y `validation` en `VideoCard`. |
| `client/src/components/capture/ElapsedTime.tsx` (nuevo) | F8 | `ElapsedTime` (intervalo de 1 s, `aria-live="off"`, `tabular-nums`), `formatElapsed` (`mm:ss` / `h:mm:ss`) y `clockTime` (HH:MM local). |
| `client/src/components/VideoCard.tsx` | F9 | Prop `validation`: con `ok`, la duración junto al nombre; con `incomplete`, un aviso ámbar `role="status"`: "Grabación incompleta: dura X de Y grabados. Se guardó lo que se pudo capturar." |
| `client/src/components/capture/EvidenceTray.tsx` | F10 | `EvidenceTrayTile.notice` (`"incomplete" \| "black"`): ícono `AlertTriangle` ámbar abajo a la derecha, y el `aria-label` suma " — grabación incompleta" o " — captura negra". |
| `client/src/components/dashboard/GenerateStep.tsx` | F11 | Prop `reviewNotices`. Bloque `role="status"` ámbar, justo antes de la botonera: "Revisá antes de generar: N grabación(es) incompleta(s), N captura(s) negra(s)" y los nombres de archivo. No toca `blocked`. |

## Contrato compartido (SDD §9): coincide

Los nombres se usan tal como viajan, en snake_case_lower:
- Start: `case_id` (body). Respuesta: `filename`, `state`, `started_at`, `case_id`.
- Stop: `platform`, `serial`, `case_id` y `validation{status, recorded_seconds, duration_seconds, duration_checked, forced_stop, started_at, stopped_at}`. Error 422: `error`, `code`, `validation`.
- WS `recording_started.data`: `serial`, `platform`, `case_id`, `started_at`, `filename`.
- WS `recording_stopped.data`: los de stop más `error` y `code`. En la falla, `filename`/`url` vienen `null`.
- `/agent/state.recordings[]`: `platform`, `serial`, `state`, `requested_at`, `started_at`, `filename`, `case_id`, `android_with_mic`, `ios_mode`.
- `unclaimed_recordings[]`: `platform`, `serial`, `filename`, `case_id`, `stopped_at`, `validation`.
- Códigos: `already_recording`, `recording_start_timeout`, `invalid_case_id`, `not_recording`, `recording_empty`, `recording_unreadable`, `empty_evidence`.
- Capability: `recording_state_v1`.

El cliente trata `null` y "ausente" igual (`?.`, `?? null`).

## Verificación

- `cd client && npx tsc --noEmit` → **sin errores** (salida vacía, exit 0).
- `npm run lint`: el script no existe en `client/package.json`, así que no se corrió.
- El dev server del usuario (:3000) compiló `/dashboard` con los cambios: `curl /dashboard` dio 200. No se detuvo ni se tocó `.next` con un build.
- `ratioDark` se probó con un script de Node con `--experimental-strip-types` en el scratchpad:
  - todo en 0 → 1;
  - luma 7 → 1;
  - luma 8 → 0;
  - buffer vacío → 0;
  - 1 de 100 píxeles blancos → 0,99, que se toma como negra (umbral ≥);
  - 2 de 100 claros → no negra.
- **§11.3 (navegador): no se ejecutó.**
  - El backend de Tatana todavía estaba a mitad de implementación en paralelo: en el working tree no había `recording_state_v1`, ni `RecordingController`/`AgentStateController` nuevos, ni `MockAndroidRecordingOutcome`. Sin eso no se pueden probar los estados `incomplete`/`empty`/`unreadable`, `/agent/state` ni las `unclaimed`.
  - Además, en esta sesión no había herramienta de automatización de navegador.
  - **No se creó ningún caso ni se registró evidencia; la base no se tocó.**
  - El orquestador debería correr §11.3 (o pedírselo al reviewer o al usuario) cuando el backend esté `done`, con el Tatana mock en 8767. Para eso hace falta un dev server de client apuntando a 8767 en un worktree, porque el `next dev` del usuario usa 8765.

## Decisiones no obvias

1. **`opts` de `useRecording` suma `getSerial` y `onUnclaimed`.** La SDD solo nombra `getCaseId`/`onValidation`, pero `refresh()` necesita el serial para el `ctx` de `syncFromAgentState`, y las `unclaimed` las importa `page.tsx`. Las dos van por ref.
2. **Contador de generación (`genRef`) en `refresh`.** Si un start o un stop termina mientras se lee `/agent/state`, la lectura es vieja: se descarta y se vuelve a pedir. Así no puede volver a "Detener" justo después de detener.
3. **`foreignStopsRef` también se chequea cuando `recording_stopped` trae `case_id`.** La SDD lo usa solo en la rama sin `case_id`, pero sumarlo es más conservador: lo que se detuvo desde el aviso "otro caso" nunca se agrega acá.
4. **El aviso de grabación ajena prioriza "de otro celular"** (serial distinto) sobre "otro caso"/"origen desconocido": el serial distinto es lo que mejor explica la situación. Si `started_at` falta (fase `starting`), "Empezó a las…" usa `requested_at`.
5. **`IosRecordMode` se mueve a `lib/agent.ts`** (`RecordingStateEntry.ios_mode` lo necesita) y `types/index.ts` lo re-exporta. Así hay una sola definición y no cambia ningún import existente.
6. **`supportsRecordingState` no cachea fallas de red** (devuelve `false` sin cachear). El cache se invalida en cada `onConnected`, porque Tatana pudo reiniciarse con otra versión.
7. **`keptBlack` también se persiste en `sessionStorage`** (la SDD solo exige las validaciones). Así "Conservar" sobrevive a la recarga. Las detecciones de negro se recalculan, como dice DA8.
8. **Los errores del stop:** con `recording_empty`, `recording_unreadable` y `not_recording` va el texto de §7.2 (o el de Tatana). El resto sigue como hoy: "No se pudo guardar la grabación: …".
9. **El texto de ayuda del botón está siempre montado** (`<p role="status" class="empty:hidden">`), para que el lector anuncie el cambio "Iniciando/Deteniendo" sin mover el foco.
10. **`recording_started`/`recording_stopped` sin la capability** se comportan como antes, salvo un detalle: `setRecording` espera el resultado (cacheado) de `/health`. Con un Tatana viejo, el `recording_stopped` sin `case_id` solo importa en la pestaña que estaba grabando: es la regla `mine` de la SDD.

## Skills

- **ui-ux-pro-max** (antes del JSX). Búsquedas `live region timer status` y `color not only indicator warning` (dominio ux). Lo que se aplicó:
  - un solo `role="status"` atómico por aviso;
  - cronómetro sin anunciar (`aria-live="off"`, más un prefijo `sr-only` "Tiempo de grabación:");
  - los avisos nunca dependen solo del color: ícono `AlertTriangle` + texto, y en el tile el texto va en el `aria-label`;
  - objetivos de 44 px en móvil (`min-h-11 sm:min-h-0`).
- **senior-frontend**:
  - getters por ref y callbacks estables (`refresh` y `syncFromAgentState` con `useCallback`);
  - cambio de caso en `useEvidenceNotices` con "ajuste de estado durante el render", para no tener un render con datos del caso anterior;
  - cronómetro aislado en `ElapsedTime`, para que el intervalo de 1 s re-renderice solo ese span y no todo `CaptureStep`;
  - concurrencia limitada a 2 con `AbortController`.
- **3d-web-experience** (criterio): no se agregó 3D ni efectos pseudo-3D. Las únicas animaciones son las que ya existían: el fade `ENTER` y el pulso del punto REC. Los spinners nuevos o tocados usan `motion-safe:animate-spin`.
- **ui-styling**: solo tokens `--fx-*` (`border-fx-warning`, `bg-fx-warning-soft`, `text-fx-warning`, `text-fx-text-2`, `ring-fx-surface-1`) y escala de Tailwind 3. Se evitó `pl-5.5`, que no existe en Tailwind 3; se usó `pl-[1.375rem]`.
- **mblode-agent-skills-ui-animation**: no se invocó porque no hay transiciones ni gestos nuevos; se reusan `ENTER` y `animate-pulse`.
- **web-design-guidelines** (autochequeo, con las reglas de vercel-labs bajadas en el momento) sobre los archivos tocados:
  - íconos decorativos con `aria-hidden`;
  - `…` en los estados de carga;
  - `tabular-nums` en el cronómetro y la duración;
  - `min-w-0` y `truncate` en nombres largos (VideoCard, lista del paso 5);
  - `translate="no"` en los nombres de archivo;
  - foco visible: se usa `Button` de Prime y `FOCUS_RING`.
  
  Hallazgo aceptado sin cambio: "Borrar" en el aviso de captura negra borra sin confirmación. Es igual que la papelera existente (la SDD pide el mismo `removeItem`).

## Pendiente para el orquestador / reviewer

- Correr §11.3 con el Tatana mock del backend (outcomes `incomplete`, `empty` y `unreadable`; recarga mientras graba; otro caso; Tatana viejo; captura negra forzada; teclado).
- Revisar que el backend use exactamente los nombres de §9. Del lado del cliente, cualquier campo ausente degrada a "como hoy".
