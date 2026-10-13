# HU: Guardar en el caso grabaciones de iPhone largas (10 min o más) sin esperar ni trabarse

**Slug:** `ios-grabacion-larga` · **Issue:** #50
**Apps afectadas:** agente Tatana (`server/src/Factum.Agent`) y `client/` (web, paso 3 de `/dashboard`).
La API `server/src/Factum.Backend` en principio **no cambia**: `registerEvidence` recibe
filename/size/sha256 y no depende de la duración. `agent-ui/` solo se toca si se resuelve
la Duda 7 en la opción A (texto del aviso de actualización).
**Origen:** QA del 2026-10-12, "video largo grabado por Tatana → caso" (hallazgos H1 y H2),
y mapa de la subida de grabaciones del mismo día.

**Como** perito que graba con Tatana la pantalla de un iPhone (modo "Solo pantalla"/"Con
micrófono", que usan DVT o burst, y modo AirPlay) durante 10 minutos o más
**quiero** que la grabación se guarde en la carpeta del caso apenas la detengo, igual que
una grabación corta
**para que** pueda seguir con la adquisición sin quedar horas trabado en el paso de captura,
y para que una falla al generar la versión suavizada no me deje sin poder guardar la evidencia.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-12, sobre `5320ca6`)

**Grabación en Tatana.** `IosService.StopRecordingAsync` (`IosService.cs:780-847`) cierra la
grabación según el modo (`IosRecordMode` = `video_only` | `with_mic` | `on_device` | `airplay`):

- `video_only` / `with_mic`: capturas DVT a 2 FPS (`DvtFps = 2.0`, `IosService.cs:56`) o burst de
  respaldo → MP4 libx264 crf 23. Al terminar llama a `StartVideoPostprocess` (`IosService.cs:841-842`).
- `airplay`: uxplay, ya a 30 FPS. **No** lanza post-proceso (`IosService.cs:841`).
- `on_device`: extrae el video nativo del DCIM del iPhone. Sale antes (`IosService.cs:794-795`) y
  **tampoco** lanza post-proceso.
- `RecordingController` (`RecordingController.cs:70-75`) emite `recording_stopped` con
  `{ filename, url, platform }`. **No** dice el modo ni si habrá variantes.

**Post-proceso (variantes).** `StartVideoPostprocess` (`IosService.cs:151-161`) corre en segundo
plano `RunInterpolationsAsync` (`IosService.cs:1256-1312`), en serie:

1. "Suavizado": ffmpeg `minterpolate=fps=30:mi_mode=blend` → `<stem>_blend.mp4`.
2. "MCI": ffmpeg `minterpolate … mi_mode=mci:mc_mode=aobmc:me=umh:vsbmc=1` → `<stem>_mci.mp4`.
   El comentario del código estima "~30-60s".
3. "RIFE", solo si está instalado `rife-ncnn-vulkan`.

Cada variante que sale bien emite `video_variant_ready { original_filename, label, filename, url }`.
Si falla, **solo se loguea** (`IosService.cs:1283`, `:1306`). La excepción general también se
loguea y nada más (`IosService.cs:158`). Ningún paso tiene timeout ni se puede cancelar. Las
variantes quedan en la raíz de `DataDirectory`, al lado del original, y el cliente solo las usa
para previsualizar (pestañas del `VideoCard`). **No** se guardan en la carpeta del caso, **no** se
registran en el expediente y **no** van al ZIP.

Mientras dura el post-proceso, `IosService.ActiveOperations` reporta `video_postprocess`
(`IosService.cs:135-147`), y eso **bloquea "Reiniciar y actualizar"** de Tatana
(HU `tatana-instalador-autoupdate`; `agent-ui` lo muestra como "procesando un video").

**Cliente.**
- `dashboard/page.tsx:257`: con `recording_stopped` y `platform === "ios"`, hace `addFile` **y**
  `markPendingVariant`, **siempre**, sin importar el modo.
- `useFileManager.saveOne` (`useFileManager.ts:425`): si el archivo está pendiente de variante,
  no lo guarda en el caso (`return false`).
- `addVideoVariant` (`useFileManager.ts:146-152`): **solo** el evento con `label === "MCI"` lo
  saca de pendientes y lo encola para guardar. "Suavizado" y "RIFE" no lo liberan.
- `handleSaveAndContinue` (`useFileManager.ts:595-601`): si queda alguno pendiente de variante,
  corta con el mensaje de `file_busy` y no avanza de paso.
- `VideoCard` muestra "procesando…" mientras el archivo está pendiente.

**Guardado del original.** `CaseEvidenceStore.ImportAsync` (`CaseEvidenceStore.cs:134-182`) mueve
el archivo de la raíz a `cases/<id>/` (rename, mismo volumen) y calcula SHA-256 en streaming.
En Windows, si ffmpeg tiene el original abierto, `File.Move` falla y devuelve `409 file_busy`
(`CaseEvidenceStore.cs:152-157`). El cliente reintenta 3 veces cada 5 s
(`useFileManager.ts:513-523`). Hoy eso casi no pasa porque el cliente espera al MCI antes de mover.

### Qué mide el QA (macOS arm64, 16 GB; 12 min de video DVT 1178x2556 a 2 FPS, 1 GB)

| Paso | Velocidad | 12 min de video | 60 min de video (extrapolado) | RAM de ffmpeg |
|---|---|---|---|---|
| Guardar el original (rename + SHA-256) | < 1 s por GB | < 1 s | segundos | plana (~80 MB Tatana) |
| Variante "Suavizado" (blend) | ~38 fps de salida (1,2x tiempo real) | ≈ 10 min | ≈ 50 min | no medida (liviana) |
| Variante "MCI" | ~2,2 fps de salida, 1 núcleo | ≈ 2 h 40 min | ≈ 13-14 h | ≈ 2 GB |

Aún **un minuto** de video necesita ≈ 14 min de MCI, no "30-60 s".

### Problemas que resuelve esta HU

1. **H1. Trabado por horas.** Una grabación DVT de 12 min no se puede guardar en el caso durante
   ≈ 2 h 50 min, y el perito no puede pasar del paso 3.
2. **H2. Pendiente para siempre.** Si el MCI falla (sin RAM, disco lleno, Tatana reiniciado, WS
   caído), el cliente nunca recibe el evento "MCI" y la grabación no se puede guardar nunca.
3. **AirPlay y "en el iPhone" trabados siempre.** El cliente los marca pendientes, pero Tatana no
   genera variantes para esos modos: ningún evento los libera. Pasa con cualquier duración, no
   solo con grabaciones largas. Lo mismo ocurre en modo mock (archivo vacío, sin post-proceso).
4. **Recursos.** El MCI ocupa ≈ 2 GB de RAM durante horas en la PC del perito. Mientras tanto,
   Tatana no se puede actualizar.

---

## Criterios de aceptación

```gherkin
Feature: Grabaciones de iPhone largas se guardan en el caso sin esperar

  Background:
    Given un caso con almacenamiento en esta PC (flujo agent) abierto en el paso 3 de /dashboard
    And un iPhone conectado y Tatana en línea

  Scenario: Grabación DVT de 12 minutos se guarda enseguida
    When grabo 12 minutos en modo "Solo pantalla" y detengo la grabación
    Then la grabación aparece en la bandeja
    And en menos de 1 minuto queda guardada en la carpeta del caso con su SHA-256
    And queda registrada en el expediente
    And "Guardar evidencia en esta PC y continuar" avanza de paso sin error file_busy

  Scenario: La versión suavizada se genera después, sin bloquear
    Given guardé en el caso una grabación DVT de 12 minutos
    When la versión suavizada todavía se está generando
    Then el VideoCard muestra el original reproducible y el estado "Generando versión suavizada"
    And puedo seguir con los pasos siguientes del wizard
    And cuando la versión suavizada termina, aparece como pestaña en el VideoCard

  Scenario: Grabación por AirPlay no espera variantes
    When grabo 10 minutos por AirPlay y detengo la grabación
    Then la grabación queda guardada en el caso enseguida
    And el VideoCard no muestra ningún estado de "procesando"

  Scenario: Grabación "en el iPhone" (on_device) no espera variantes
    When grabo con la grabación nativa del iPhone y detengo
    Then la grabación extraída queda guardada en el caso enseguida
    And el VideoCard no muestra ningún estado de "procesando"

  Scenario: Falla la generación de la versión suavizada
    Given guardé en el caso una grabación DVT
    When la generación de la versión suavizada falla
    Then el VideoCard deja de mostrar "procesando" y muestra que no se pudo generar la versión suavizada
    And el original sigue guardado en el caso, con el mismo SHA-256
    And puedo continuar con el caso sin hacer nada más

  Scenario: La generación supera el tiempo máximo
    Given la generación de una variante lleva más que el tiempo máximo permitido
    Then Tatana la corta, borra el archivo parcial y avisa que no se generó
    And libera la CPU y la memoria que usaba ffmpeg

  Scenario: Grabaciones por encima del umbral no generan la variante pesada
    When grabo en modo DVT más tiempo que el umbral definido para la variante pesada
    Then Tatana no lanza esa variante
    And el VideoCard no la espera ni la ofrece

  Scenario: El original no cambia por el post-proceso
    Given guardé en el caso una grabación DVT y anoté su SHA-256
    When termina (bien o mal) la generación de variantes
    Then el SHA-256 del original en la carpeta del caso es el mismo
    And el ZIP del caso contiene el original con ese SHA-256

  Scenario: Guardar en Windows mientras se genera la variante
    Given Tatana corre en Windows
    When detengo una grabación DVT de 10 minutos
    Then el original se guarda en el caso sin error file_busy, aunque la variante se esté generando

  Scenario: Regresión de grabaciones cortas y Android
    When grabo 30 segundos de iPhone en modo DVT y 30 segundos de Android
    Then ambas se guardan en el caso como hoy
    And la grabación de iPhone muestra su versión suavizada cuando está lista
```

---

## Datos que se registran

No hay colecciones ni DTOs nuevos en `Factum.Backend`. Lo que sí cambia está en el contrato
**Tatana ↔ client** (WebSocket). Los nombres exactos los fija la SDD, en snake_case:

| Dato | Obligatorio | Uso |
|---|---|---|
| Original de la grabación (`grabacion_ios_<ts>.mp4`) + SHA-256 + tamaño | Sí | Única evidencia. Se mueve al caso y se hashea apenas termina la grabación, como cualquier otra captura. Sin cambios en el formato del registro. |
| Si la grabación tendrá variantes (p. ej. un campo en `recording_stopped`) | Sí | El cliente solo muestra "procesando" si Tatana va a generar algo. Así AirPlay, on_device y mock no quedan esperando. |
| Estado de cada variante: `en curso` / `lista` / `falló` / `timeout` / `omitida` | Sí | Feedback en el `VideoCard`. Reemplaza el "solo aviso si salió bien" de hoy. |
| Progreso de la variante (porcentaje o tiempo procesado) | Según Duda 4 | Barra o texto de progreso en el `VideoCard`. |
| Motivo de la falla (texto corto) | No | Se muestra al perito y queda en el log de Tatana. |
| Variantes (`_blend.mp4`, `_mci.mp4`, `_rife.mp4`) | No | Solo previsualización, como hoy. No son evidencia (ver Duda 5). |

---

## Diseño UX/UI (`client/`, paso 3 de `/dashboard`, `VideoCard`)

- **Entrada:** sin cambios. El perito detiene la grabación como hoy.
- **Bandeja:** la grabación sigue el mismo ciclo de guardado automático que una captura
  ("guardando…" → guardada), sin el bloqueo previo por variante.
- **`VideoCard`:**
  - La pestaña "Original" está disponible apenas termina la grabación, y después se sirve
    desde la carpeta del caso (como hoy con lo que ya está guardado).
  - Mientras se genera una variante: un indicador junto a las pestañas, "Generando versión
    suavizada…", con progreso si se resuelve así en la Duda 4. Debe quedar claro que **el
    original ya está guardado**. Ejemplo de copy: "El original ya está guardado en el caso.
    La versión suavizada es solo para ver mejor el video".
  - Variante lista: aparece su pestaña, como hoy.
  - Variante fallida o cortada por tiempo: el indicador pasa a un texto discreto (no un error
    rojo ni un toast bloqueante), por ejemplo "No se pudo generar la versión suavizada. El
    original no se modificó". Sin botón de reintento (ver Fuera de alcance).
  - Variante omitida por duración: sin indicador, o una nota breve "Video largo: no se genera
    versión suavizada" (lo define la Duda 2).
  - AirPlay / on_device: sin pestañas extra ni indicador.
- **"Guardar evidencia en esta PC y continuar":** ya no corta por variantes pendientes. Si al
  avanzar todavía hay variantes en curso, siguen en segundo plano sin aviso bloqueante.
- **Accesibilidad:** el indicador de estado usa `role="status"` (como el "procesando…" actual) y
  el cambio a "falló" se anuncia sin robar el foco.

---

## Fuera de alcance

- Cortar la grabación de la cámara externa a los 10:00 (`CameraRecordModal.tsx:21`,
  `MAX_SECONDS = 600`). Es otro origen y va en otra HU.
- La lentitud del ZIP con media ya comprimida (hallazgo H3 del QA: deflate nivel 6 sobre video).
- El límite de 300 MB de Caddy en la nube para casos con almacenamiento en el servidor.
- Guardar las variantes en el caso, registrarlas en el expediente o incluirlas en el ZIP/informe
  (salvo que la Duda 5 se resuelva en B).
- Un botón de "reintentar" o "generar a pedido" para una variante fallida u omitida.
- Mejorar la calidad o los FPS de la captura DVT (2 FPS).
- Grabaciones de Android (scrcpy): no tienen post-proceso y no cambian.
- Timeouts del stop de grabación (scrcpy/DVT/ffmpeg al detener). La codificación final del stop
  no se midió como problema en el QA.

---

## Notas de implementación (mínimas; el detalle lo escribe el `architect`)

- **Principio rector:** guardar el original no depende **nunca** de las variantes. El cliente
  deja de condicionar `saveOne`/`handleSaveAndContinue` a `pendingVariantRef`; ese estado pasa a
  ser solo visual.
- **Tatana siempre termina cada post-proceso con un evento** (ok, falló, timeout, omitida), desde
  un `finally`, para que el cliente nunca quede esperando.
- **El original no se toca:** ffmpeg solo lo lee. El SHA-256 se calcula al guardarlo y no cambia.
- **Windows:** mover el original mientras ffmpeg lo lee da `file_busy`. El mecanismo para que no
  pase lo elige el architect. Por ejemplo: que la variante lea de otra ubicación, o que el
  post-proceso empiece o continúe sobre el archivo ya guardado en el caso. Requisito: el guardado
  no espera a la variante. Ver Duda 6.
- Las variantes viven en la raíz de `DataDirectory`. Si el post-proceso leyera desde la carpeta
  del caso, las variantes no deben quedar dentro de esa carpeta, porque terminarían en el ZIP
  (ver Duda 5).
- `ProcessRunner.RunAsync` hoy no tiene cancelación ni timeout para ffmpeg: hay que matar el
  proceso al vencer el plazo y borrar el parcial.
- Ya existe un proyecto de tests en `server/tests/Factum.Agent.Tests` (p. ej.
  `OperationTrackerTests`). La lógica de umbral/timeout y la emisión del evento en todos los
  caminos son buenas candidatas a test unitario.
- Contrato compartido Tatana ↔ client: lo fija la SDD (evento nuevo o campos nuevos en
  `video_variant_ready`/`recording_stopped`, en snake_case).

---

## Dudas para validar con el usuario

**1. ¿Cuándo se guarda el original en el caso?**
- **A (recomendada):** apenas termina la grabación, como cualquier captura. Las variantes se
  generan después, en segundo plano, y no bloquean ni el guardado ni el avance del wizard.
  *Por qué:* resuelve H1, H2 y el bloqueo de AirPlay/on_device de una sola vez. El original es
  la evidencia y se hashea en segundos.
- B: esperar solo a la variante "Suavizado" (≈ 1,2x la duración: 12 min de video → 10 min) y
  no al MCI. Reduce la espera, pero el perito sigue trabado en grabaciones largas y una falla
  del blend lo deja igual de bloqueado.
- C: dejarlo como hoy y solo arreglar el evento de falla. Descartada: no cumple el requisito
  de no esperar.

**2. ¿Qué variantes se generan, y con qué umbral de duración?**
Con lo medido, el MCI tarda ≈ 14 min **por cada minuto** de video y usa ≈ 2 GB de RAM.
- **A (recomendada):** sacar el MCI del post-proceso automático y generar solo "Suavizado"
  (blend) para cualquier duración. *Por qué:* el MCI no es viable ni para grabaciones de 1-2
  min, y el blend da una vista fluida en ≈ 1,2x la duración, en segundo plano. RIFE se mantiene
  como hoy (solo si está instalado), pero con el mismo umbral y timeout que las demás.
- B: blend siempre y MCI solo para grabaciones de hasta **N** minutos (p. ej. 2 min ≈ 30 min
  de proceso). Se conserva la mejor calidad en clips cortos, a costa de CPU/RAM y de bloquear
  la actualización de Tatana durante ese tiempo.
- C: umbral para todo: por encima de **N** minutos (p. ej. 30) no se genera ninguna variante.
  Se puede combinar con A para que una grabación de horas no ocupe la PC casi el mismo tiempo.
  *Sugerencia:* A + C con N = 30 min, si el perito graba sesiones muy largas.

**3. ¿Qué timeout tiene cada variante?**
- **A (recomendada):** un plazo proporcional a la duración del video, con piso y techo (p. ej.
  3 × duración + 2 min, mínimo 5 min, máximo 2 h). Al vencer, Tatana mata ffmpeg, borra el
  parcial y emite el estado `timeout`. *Por qué:* un plazo fijo corta de más los videos largos
  o espera de más los cortos. Los valores exactos van a la config de Tatana.
- B: un timeout fijo (p. ej. 30 min) para cualquier video.
- C: sin timeout, solo el umbral de la Duda 2. Descartada: un ffmpeg colgado vuelve a bloquear
  la actualización de Tatana sin límite.

**4. ¿Cómo se informa al perito?**
- **A (recomendada):** estado en el `VideoCard` (generando / lista / no se pudo / omitida) **con
  porcentaje de avance**, que Tatana saca del progreso de ffmpeg y emite cada pocos segundos.
  Nunca un error bloqueante: el texto deja claro que el original ya está guardado. *Por qué:*
  con videos de 10 min o más, la variante tarda minutos, y sin avance parece colgada.
- B: solo estados, sin porcentaje. Es más simple, pero el "generando…" de 10 min o más no dice
  nada.
- C: también un aviso (toast) cuando termina o falla. Puede sumarse a A, pero si el perito
  ya está en otro paso, el aviso aparece fuera de contexto.

**5. Cadena de custodia: ¿qué estatus tienen las variantes?**
- **A (recomendada):** como hoy, son solo una ayuda visual. No se guardan en el caso, no se
  registran en el expediente, no van al ZIP y no se hashean. La evidencia es el original, con
  su SHA-256 calculado al guardarlo y verificado en el ZIP. *Por qué:* una variante interpolada
  tiene cuadros inventados por el algoritmo. Si se presenta como evidencia, puede confundirse
  con material adquirido.
- B: guardarlas en el caso como "derivadas", con hash propio y una nota "generada a partir de
  `<original>` (SHA-256 …) con `<método>`", e incluirlas en el ZIP. Es más trazable si el perito
  las usa para mostrar, pero agrega volumen al ZIP y cambia el informe. Sería otra HU.

**6. ¿Cómo se garantiza que en Windows el original se pueda guardar mientras se genera la variante?**
- **A (recomendada):** queda como requisito para la SDD: guardar el original nunca falla ni
  espera por el post-proceso, en ningún SO. El architect elige el mecanismo (p. ej. que la
  variante lea desde otra ubicación, o que el post-proceso trabaje sobre el archivo ya guardado
  en el caso, sin escribir variantes dentro de esa carpeta). *Por qué:* hoy el problema queda
  oculto porque el cliente espera al MCI. Con la Duda 1 en A aparece siempre en Windows.
- B: aceptar en Windows los reintentos de `file_busy` (3 × 5 s) y, si no alcanzan, guardar al
  terminar la variante. Es más chico, pero en Windows reaparece la espera larga.

**7. ¿El post-proceso de variantes sigue bloqueando "Reiniciar y actualizar" de Tatana?**
- **A (recomendada):** no. Como el original ya está guardado, la actualización puede seguir:
  Tatana cancela el post-proceso, borra los parciales y el aviso de `agent-ui` lo dice ("se
  cancelará la versión suavizada en curso"). *Por qué:* con videos largos, el post-proceso
  puede durar decenas de minutos, y bloquear una actualización por algo que no es evidencia
  no tiene sentido. Toca `agent-ui/` (texto de `useTatanaUpdates.ts`/`updater/state.ts`).
- B: sigue bloqueando como hoy. Con el umbral y el timeout de las Dudas 2 y 3, el bloqueo
  queda acotado.

**8. ¿Qué pasa si el perito borra la grabación de la bandeja mientras se genera la variante?**
- **A (recomendada):** Tatana cancela el post-proceso de ese archivo y borra los parciales y
  las variantes ya generadas. *Por qué:* libera CPU/RAM y no deja archivos huérfanos en la raíz.
- B: dejar que termine. Las variantes quedan en la raíz como hoy.

**9. ¿Qué pasa si se cae el WebSocket o se recarga la página mientras se genera la variante?**
- **A (recomendada):** al reconectar, el cliente consulta a Tatana el estado del post-proceso
  de las grabaciones de la bandeja (lista / en curso / falló) y actualiza el `VideoCard`.
  *Por qué:* si no, el indicador "generando…" puede quedar girando para siempre. Ya no bloquea
  nada, pero confunde al perito.
- B: no recuperar el estado. Si se perdió el evento, la pestaña de la variante no aparece y el
  indicador se oculta por timeout del lado del cliente.
