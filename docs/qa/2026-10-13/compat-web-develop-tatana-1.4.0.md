# Compatibilidad: web de `origin/develop` contra Tatana `tatana-v1.4.0`

Análisis de solo lectura (2026-10-13). Fuentes: `git show origin/develop:client/src/...` contra
`git show tatana-v1.4.0:server/src/Factum.Agent/...`. Las rutas `archivo:línea` del cliente son de
`origin/develop`.

## TL;DR

**No encontré ningún flujo ROTO.** La web nueva prevé un Tatana viejo en todos los puntos nuevos:
404 en `/preview`, 404 en `/video-postprocess/status`, ausencia de `recording_state_v1`, eventos sin
`serial`/`case_id`/`origin_id`, y respuestas de stop sin `validation`/`variants_expected`. Lo que sí
pasa:

1. **Degradado (el más notorio): grabación iOS en Windows.** La web nueva ya no espera la variante
   MCI antes de mover el original a la carpeta del caso. Tatana 1.4.0 sigue corriendo ffmpeg
   (blend → MCI → RIFE) sobre ese **mismo** archivo. El `File.Move` falla con 409 `file_busy`, la web
   reintenta 3 veces cada 5 s y después muestra un error hasta que el perito reintente con ffmpeg ya
   terminado. No se pierde evidencia; antes la espera era silenciosa y ahora aparece un error
   (detalle en §4.5).
2. **Degradado:** el filtro por pestaña descarta `screenshot_taken` (no trae `serial`) y `photo_taken`
   de 1.4.0. Para la pestaña que pidió la captura no pasa nada, porque la agrega por la respuesta HTTP.
   Lo que se pierde es la sincronización entre pestañas, y vuelve el riesgo de mezclar grabaciones
   entre pestañas o casos.
3. **Degradado:** sin `recording_state_v1` no hay recuperación de una grabación tras recargar, ni
   `unclaimed_recordings`, ni validación (ok/incomplete/empty) ni el rechazo de archivos de 0 B. El
   start/stop de Android sigue siendo el de 1.4.0, sin las correcciones de cortes y 0 B de #59/#66.
4. **ZIP:** lo arma Tatana, así que con 1.4.0 sale el ZIP viejo (todo con Deflate). Es más lento
   (en el juego del QA, 190 s contra 54 s) pero correcto. El bug de verificación async de #58/#65 solo
   afecta a entradas *Stored*, que 1.4.0 no genera.

## 1. Qué expone 1.4.0 en `/health`

`HealthController.cs` (tag `tatana-v1.4.0`):

```
capabilities = ["case_evidence_v1", "ios_developer_mode_v1", "real_version_v1", "agent_state_v1"]
```

Le faltan, comparado con develop: `video_postprocess_v1`, `recording_state_v1`, `event_attribution_v1`
y `device_preview_v1`.

Qué hace la web con esto:

- **Identidad / bloqueo:** `useAgentIdentity.ts:49` solo exige `case_evidence_v1`, que 1.4.0 tiene,
  así que queda `online`. Se bloquea solo si el backend tiene `Tatana:MinVersion` (`FACTUM_TATANA_VERSION_MINIMA`)
  por encima de 1.4.0. Ese valor es config de despliegue y no cambió entre main y develop.
- **`recording_state_v1`:** es la única capability nueva que el cliente consulta
  (`agent.ts:707` `supportsRecordingState`). Se cachea por conexión y se resetea en cada
  `onConnected`.
- **`device_preview_v1` / `video_postprocess_v1` / `event_attribution_v1`:** el cliente no las mira.
  Las detecta por comportamiento: 404 sin cuerpo, `null` ante cualquier error, o campos ausentes. Las
  tres degradan bien (ver abajo).

## 2. Infraestructura que permite que los 404 se manejen

- La guarda de origen de 1.4.0 (`Program.cs:178-218`) escribe los headers CORS **antes** de `next()`
  para cualquier ruta con un Origin permitido, incluso las que no existen. Un `POST /devices/{s}/preview`
  o un `GET /video-postprocess/status` llegan al cliente como un 404 legible, no como un error de
  red/CORS. Sin esto, `takePreview` habría lanzado `AGENT_UNREACHABLE` y el paso 2 se rompía.
- Una ruta inexistente responde 404 con cuerpo vacío. `agent.ts:575-584` interpreta "404 sin `{error}`"
  como `PreviewUnsupportedError` y lo cachea en `previewUnsupported` hasta la próxima reconexión.
- Los campos de más (`case_id`, `origin_id`) en el body de `record/start` se ignoran: System.Text.Json
  saltea por defecto los miembros sin mapear en `StartRecordingRequest`. Los query params de más en
  screenshot, record/stop y airplay/stop también se ignoran.
- No hay filtro de Host en 1.4.0 (#60 lo agrega del lado de Tatana), así que no hay nada que la web
  pueda romper por ese lado.

## 3. Filtro de eventos por pestaña (#61) contra los eventos de 1.4.0

Reglas en `client/src/lib/agent-event-filter.ts:52-74` y uso en `app/dashboard/page.tsx:321-401`.

| Evento | Datos que trae 1.4.0 | Resultado del filtro | ¿Hace daño? |
|---|---|---|---|
| `devices_changed` | `{devices}` | sin filtro | OK |
| `recording_started` | `{serial, platform}` (`RecordingController.cs` 1.4.0) | regla 1 OK (serial propio) → regla 6 → **aceptado solo en paso 3** | OK. Sin `recording_state_v1` → `setRecording(true)` (`page.tsx:354-357`) |
| `recording_stopped` | `{filename, url, platform}`, **sin serial** | regla 6 → aceptado en paso 3; `mine = isRecordingRef.current` (`page.tsx:362-365`) | OK para la pestaña propia (además lo agrega la respuesta HTTP del stop, `useRecording.ts:195-197`) |
| `screenshot_taken` | `{filename, url}`, **sin serial** (también el de las marcas AirPlay, `IosService.cs:692`) | regla 6: `screenshot_taken` sin `serial` → **rechazado siempre** (`agent-event-filter.ts:72`) | No para la pestaña propia: `handleScreenshot` hace `addFile` con la respuesta HTTP (`page.tsx:580-581`) y las marcas AirPlay llegan en la respuesta de `stop` (`page.tsx:631-632`). Sí afecta a las otras pestañas del mismo caso, que no la ven en vivo (la ven al restaurar). |
| `photo_taken` | `{type, filename, url}` | regla 6 → **rechazado siempre** | No: la web de develop **no llama** a `agent.captureWebcam` (no tiene ningún llamador). Las fotos de webcam son del navegador (`handlePhotoBlob`). |
| `video_variant_ready` | `{original_filename, label: "Suavizado"/"MCI"/"RIFE", filename, url}` | sin filtro | OK: `addVideoVariant` (`page.tsx:388-390`). `VideoCard` muestra la etiqueta desconocida "MCI" al final como "versión MCI". |
| `video_variant_status` | no existe en 1.4.0 | — | Sin barra de avance ni estado "pendiente" de la variante (solo visual). |
| `zip_progress` | `{case_id, phase, ...}` | filtrado por `case_id`, que 1.4.0 manda | OK |
| `airplay_shot_connected/timeout`, `airplay_receiver_ready` | igual que antes | sin filtro | OK |

**Respuesta a "¿la web ignora eventos de un Tatana viejo?":** solo ignora `screenshot_taken` y
`photo_taken`. Los dos sobran en la pestaña que pidió la acción, porque el archivo entra por HTTP.
Los eventos de grabación se aceptan en el paso 3, como en main. No es grave.

**Lo que se pierde (degradado):** la protección entre pestañas de #61 no aplica a la grabación. Una
pestaña B en el paso 3 con el mismo celular y **otro caso** acepta el `recording_started` de A
(regla 6) y queda con `isRecording = true`. Cuando llega el `recording_stopped`, `mine` es `true` por
`isRecordingRef` y B **importa la grabación de A a su propio caso**. Es exactamente el comportamiento
de main (no es una regresión), pero la corrección de #61 solo funciona con un Tatana que tenga
`event_attribution_v1`. El aviso de "mismo celular en otra pestaña" (BroadcastChannel,
`useTabPresence`) no depende de Tatana y sigue funcionando.

## 4. Flujo por flujo

### 4.1 Conectar — OK
`useAgentConnection.ts` hace `GET /health`, abre el WS `/ws` y llama a `GET /devices` sin cambios de
contrato. `onopen → handleAgentConnected` (`page.tsx:410-414`) resetea capabilities, llama a
`refreshVariantStatus` (404 → `null`, silencioso) y a `refreshRecording` (sin `recording_state_v1`,
no hace nada). La identidad queda `online` por `case_evidence_v1`. El IMEI vacío de 1.4.0 (no manda el
centinela `INGRESAR_MANUALMENTE`, que se agregó en main después del tag) lo cubre
`isImeiMissing` (`lib/pericial.ts:183`), que trata igual el vacío y el centinela.

### 4.2 Paso 2, vista previa — OK con fallback (degradado leve)
`CaseFormStep.tsx:548-553`: `takePreview` hace `POST /devices/{s}/preview`, que en 1.4.0 da 404 sin
cuerpo → `PreviewUnsupportedError` → `takeScreenshot(serial, platform, null)` sin atribución. Se ve la
imagen igual que en main. Diferencias:

- La vista previa queda como `screenshot_*.png` suelto en la raíz de datos de Tatana (no en `previews/`
  con limpieza). Es lo mismo que pasa hoy en main.
- 1.4.0 emite `screenshot_taken` sin serial, y otra pestaña en el paso 3 la **rechaza** (regla 6). Es
  una mejora frente a main.
- Solo hay un 404 por conexión: el flag se cachea y se resetea al reconectar (`agent.ts:718-723`).

### 4.3 Capturas (paso 3) — OK
- Captura de pantalla (Android/iOS): `page.tsx:580` manda `?case_id&origin_id`, que 1.4.0 ignora, y la
  respuesta `{filename, url}` se agrega por HTTP. El evento WS se descarta, pero no hace falta.
- AirPlay "espejar para capturas": `start`/`mark` no cambian. `stop` devuelve
  `{files:[{filename,url}]}` en 1.4.0 (`AirplayShotController.cs`) y develop los agrega desde la
  respuesta (`useAirplayShotSession.ts:75`, `page.tsx:631-632`).
- Webcam o foto del navegador: no pasa por Tatana.
- Degradado: las otras pestañas del mismo caso no ven la captura en vivo.

### 4.4 Grabación Android — OK, sin las mejoras de #59/#66 (degradado)
- `record/start` (`useRecording.ts:162-168`): body con `case_id`/`origin_id` extra, que se ignoran.
  1.4.0 responde `{filename, state}` **sin `started_at`** y la web usa `new Date()` como inicio, así que
  el cronómetro anda pero arranca antes que la captura real. 1.4.0 no espera a que scrcpy empiece
  (sin `recording_start_timeout`).
- `record/stop` (`useRecording.ts:195-201`): `{filename, url}`, sin `validation`. No se muestran avisos
  de "incompleta" ni la duración verificada en `VideoCard`.
- Lo que **no** se arregla con 1.4.0 (sigue el comportamiento de main): scrcpy con
  `--no-video-playback`, que ignora SIGINT y termina en kill (cola perdida o archivo de 0 B). Una
  grabación de 0 B **se importa y se registra** como evidencia: el rechazo `empty_evidence` es de
  Tatana nuevo y el backend acepta `size >= 0` (`EvidenceManifest.cs:120`).
- Sin `/agent/state` con `recordings`, al recargar la pestaña con una grabación en curso la web no lo
  sabe: no hay banner de "Grabando desde HH:MM", ni de grabación de otro caso, ni "Detener sin importar".
  Si el perito vuelve a apretar Grabar, 1.4.0 responde un 500 con su propio texto. Es lo mismo que main.
- Códigos nuevos (`already_recording`, `not_recording`, `recording_empty`...): 1.4.0 no los manda. Los
  errores llegan como 500 `{error}` y se muestran tal cual (`agent-messages.ts`, `action: "record"`).

### 4.5 Grabación iOS — degradado (en Windows, con error visible)
- `record/start` iOS en 1.4.0 responde `{state: "recording"}` sin filename, lo que la web tolera. Los
  modos `video_only`/`with_mic`/`on_device`/`airplay` no cambian.
- `record/stop` responde `{filename, url}` sin `variants_expected`, así que la web no marca variantes
  pendientes (`useRecording.ts:200`, `page.tsx:368`).
- **El problema:** develop sacó `markPendingVariant` y la espera de la MCI (diff de
  `useFileManager.ts`). Ahora `addFile` (`useFileManager.ts:141-150`) encola el guardado **en el
  momento**. Mientras tanto, 1.4.0 (`IosService.StopRecordingAsync` → `StartVideoPostprocess` →
  `RunInterpolationsAsync`) corre ffmpeg en segundo plano **sobre el original** (`_blend`, después
  `_mci`, después RIFE si está instalado) para todo lo que no sea AirPlay, incluido `on_device`. El
  import de 1.4.0 hace `File.Move` (`CaseEvidenceStore.cs:150-156` del tag):
  - **Windows:** ffmpeg tiene el archivo abierto sin `FILE_SHARE_DELETE` → `IOException` → 409
    `file_busy`. La web reintenta 3 veces cada 5 s (`useFileManager.ts:618-627`), o sea unos 15 s. La
    MCI de un video corto ya tarda 30-60 s y la de uno largo, muchos minutos. Después queda en estado
    `error` con el texto "…todavía se está procesando en Tatana. Esperá unos segundos y volvé a
    intentar." El perito tiene que reintentar el guardado (o lo reintenta "Generar") cuando termine
    ffmpeg. En main, con 1.4.0, la espera era silenciosa (`pendingVariantFiles`). **No se pierde
    evidencia**: el original sigue en la raíz y se puede reproducir.
  - **macOS/Linux:** el move funciona aunque ffmpeg lo tenga abierto. El blend en curso termina (por
    inodo), pero la MCI y RIFE abren la ruta vieja y fallan (solo queda en el log de Tatana). Resultado:
    solo aparece la "versión suavizada" (si estaba en curso) y la MCI se pierde. El original queda bien
    guardado.
- La recuperación de variantes al reconectar o restaurar (`refreshVariantStatus` →
  `GET /video-postprocess/status`) recibe 404 → `null` y no muestra nada (`agent.ts:740-761`).
- Para el resto vale lo mismo que en Android: sin recuperación tras recargar, sin `validation` (en iOS
  no la había de todos modos) y sin el post-proceso con timeout y cancelación de #50/#53. En 1.4.0, la
  MCI de una grabación larga sigue consumiendo CPU sin tope de tiempo.

### 4.6 Subir archivos (guardar evidencia en la PC) — OK
`checkEvidenceUpload`, `uploadEvidence`, `importEvidence`, `listCaseFiles`, `getCaseFileBlob` y
`deleteCaseFile` no cambiaron en `agent.ts`. Lo nuevo de develop es solo del cliente y del backend:
saltear la reimportación si ya se movió (`useFileManager.ts:534-537`), `evidence_busy` (backend) y el
registro idempotente (backend). La única excepción es el `file_busy` de iOS descrito en §4.5. Una
nota: un archivo de 0 B se acepta (1.4.0 no rechaza `empty_evidence`).

### 4.7 Generar ZIP + informe — OK (ZIP viejo y más lento)
- La web llama a `buildZip`/`commitZip`/`discardPendingZip` (`page.tsx:694-739`) con el mismo
  contrato que en main. Lo único nuevo es mostrar `zip_in_progress` como advertencia de "otra
  pestaña". `zip_progress` sigue trayendo `case_id`.
- El ZIP lo arma 1.4.0 con **Deflate nivel 6 en todas las entradas** (sin el Stored de #51/#54) y lo
  verifica con lectura async. El bug de #58/#65 (`ZipAESStream.ReadAsync` que saltea el HMAC) solo
  afecta a entradas AES **Stored**, que 1.4.0 no escribe, así que el ZIP es correcto. Lo que cambia es
  el tiempo: recomprime video e imágenes (en el QA, 190,5 s contra 54,3 s).
- El backend no lee el ZIP del flujo agent: los cambios de `Factum.Backend/Services/Reports/EvidenceZip.cs`
  son del flujo con almacenamiento en el servidor. El informe (DOCX), `operaciones_base`, la firma y los
  arreglos de formato son del backend y funcionan igual.
- Previo a #61 y sin cambios: con 1.4.0, el hub WS sin cola acotada (T-01 de #60) sigue pudiendo
  trabarse si un cliente no lee. Ya pasa en producción hoy.

### 4.8 Retomar caso — OK (degradado leve)
La restauración desde la carpeta del caso (`listCaseFiles`) no cambió. `refreshVariantStatus` recibe
404 y no hace nada. `refreshRecording` no hace nada sin `recording_state_v1`. Lo que no se recupera:
las grabaciones detenidas que nadie importó (`unclaimed_recordings`) y una grabación en curso. Quedan
en la raíz de Tatana como en main. El chequeo de "misma PC" (`isSameHostFor`) usa `/info.hostname` y
no cambió.

## 5. Endpoints o campos nuevos que la web usa sin chequear capability

| Llamada nueva | Chequeo | Con 1.4.0 |
|---|---|---|
| `POST /devices/{s}/preview` (`agent.ts:566`) | no; por 404 sin cuerpo | fallback a screenshot — OK |
| `GET /previews/{f}` (`previewFileURL`) | solo se usa con una `url` devuelta por `/preview` | nunca se llama — OK |
| `GET /video-postprocess/status` (`agent.ts:740`) | no; cualquier error → `null` | 404 → `null` — OK |
| `GET /agent/state` → `recordings`/`unclaimed_recordings` (`agent.ts:686`) | sí, `recording_state_v1` (además, si faltan los dos campos → `null`) | no se llama — OK |
| `case_id`/`origin_id` en `record/start` (body), screenshot/stop/airplay-stop (query) | no | se ignoran — OK |
| Campos `started_at`, `validation`, `variants_expected`, `variants`, `code` en las respuestas | no; todos opcionales | ausentes, con fallback — OK |

No encontré ninguna llamada nueva sin fallback.

## 6. Recomendaciones (si se quiere promover sin forzar la actualización)

1. iOS en Windows: con una versión sin `video_postprocess_v1`, volver a esperar la variante MCI antes
   de importar (o subir `BUSY_MAX_RETRIES` y el intervalo para iOS). Alternativa: fijar
   `Tatana:MinVersion` en la versión que trae `video_postprocess_v1` y forzar la actualización.
2. Conviene publicar la nueva versión de Tatana junto con la promoción. #59/#60/#61/#58 son
   correcciones de robustez y seguridad que solo existen del lado de Tatana.
