# HU: Grabación de cámara externa (y adjuntos) duplicados en la bandeja del paso 3

**Slug:** `grabacion-camara-duplicada`
**Apps afectadas:** solo `client/` (web): `CaptureStep.tsx`, `capture/gallery.ts` y,
según la SDD, `hooks/useFileManager.ts`. **No cambian** el backend, Tatana ni `agent-ui/`.
**Origen:** observación no bloqueante del `reviewer` de `rediseno-dashboard-captura`
(`progress/review_rediseno-dashboard-captura.md`, "Observaciones no bloqueantes").
El implementador lo marcó como un bug que ya existía antes del rediseño.

**Como** perito que captura evidencia en el paso 3 del wizard de `/dashboard`
**quiero** que cada archivo que grabo con la cámara externa o adjunto desde la PC aparezca
**una sola vez** en la bandeja
**para que** la bandeja muestre la evidencia que de verdad se va a subir. Así no borro
"el duplicado" y me quedo, sin saberlo, con un chip que no se sube.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-01, sobre `2193005`)

Hay **dos fuentes de verdad** para el mismo archivo:

1. **`files`** (`hooks/useFileManager.ts`). Es la lista que se sube de verdad.
   `handlePhotoBlob(blob, filename)` guarda el blob en `pendingBlobs` y llama a
   `addFile(filename)`, que deduplica por nombre. `handleUploadAndContinue` recorre `files`
   y sube cada uno una vez (el blob sale de `pendingBlobs` o, si no hay, de
   `agent.downloadFile`). `removeFile` lo saca de `files` y de `pendingBlobs`.
2. **`localFiles`** (estado local de `CaptureStep`, L118). Existe **solo para la vista**:
   blob URL para la previsualización, tamaño y tipo. Nunca se sube.

**El camino de la grabación de la cámara externa:**

- `CameraRecordModal.tsx` L253 arma el nombre `grabacion_camara_<ts>.webm` y llama a `onCapture`.
- `CaptureStep.handleCameraRecording` (L195-199) hace **dos cosas**: agrega un `LocalFile`
  (`kind: "video"`) **y** llama a `onAttachLocalFile`. `page.tsx` L778 lo conecta a
  `handlePhotoBlob`, que lo agrega a `files`.
- La galería (L136-145) recorre `files` **y** `localFiles`:
  - Desde `files`: `fileType("grabacion_camara_….webm")` da `"other"`. `.webm` no entra
    en `isVideo` (`mp4|mkv|mov`) y el nombre no contiene `captura`. Sale un chip
    "Archivo", con key igual al nombre.
  - Desde `localFiles`: sale un chip "Video" con key `local-<nombre>`.
  - Las dos keys son distintas, así que **los dos chips aparecen** en "Adjuntos", y el
    contador del encabezado dice "2 adjuntos".

**Los adjuntos desde la PC tienen el mismo problema** (`processFiles`, L201-211, por
el botón o al arrastrar). Cada archivo va a `localFiles` y a `onAttachLocalFile`, con el
nombre `adjunto_<fecha>_<hora>_<n>.<ext>`. Según la extensión:
- imagen o audio (`.jpg`, `.png`, `.m4a`…): un chip "Imagen"/"Audio" (local) y otro
  "Archivo" (`other`);
- video `.mp4`/`.mov`/`.mkv`: el lado de `files` cae en `isVideo` y aparece como
  **tile "Grabación" en "Capturas"**, con `VideoCard` y variantes que nunca llegan,
  además del chip "Video" en "Adjuntos".

**Orígenes que no se duplican:**
- **Foto webcam** (perito/titular): `handleWebcamCapture` llama a `onPhotoPerito`/
  `onPhotoTitular` → `handlePhotoBlob`, sin `LocalFile`. Además, la galería descarta
  `funcionario`/`denunciante`.
- **Archivos del celular** (explorador): `onDeviceFilesAdded` → solo `addFile`.
- **Capturas y grabaciones del celular**: solo `files`.

### Impacto forense (verificado en el código)

- **No llega duplicado al informe ni al ZIP.** Se sube solo lo que está en `files`, que
  deduplica por nombre. El paso 5 (`GenerateStep`) cuenta sobre `files`. El backend
  recibe un upload por archivo, así que un mismo archivo no se hashea dos veces.
- **El riesgo real es la desincronización al borrar.** Los dos chips borran cosas distintas:
  - Borrar el chip **local** ("Video"/"Imagen") solo lo saca de `localFiles`. El archivo
    **se sigue subiendo**, aunque el perito crea que lo descartó.
  - Borrar el chip **"Archivo"** llama a `removeFile`: lo saca de `files` y de
    `pendingBlobs`. El chip local queda a la vista, pero ese archivo **ya no se sube**.
    Es evidencia que el perito ve en pantalla y que no va a estar en el ZIP.
- El chip "Archivo" del duplicado, al seleccionarlo, muestra "Abrir archivo" con
  `agentFileURL(nombre)`. Esa URL da error, porque el blob nunca pasó por Tatana.
- **Hallazgo vecino (pérdida silenciosa):** `processFiles` arma el nombre con fecha y hora
  al segundo y con un `n` que arranca en 1 en cada tanda. Si el perito adjunta dos tandas
  en el mismo segundo, salen nombres iguales (`adjunto_…_1.jpg`). `addFile` descarta el
  segundo de `files` y `pendingBlobs.set` **pisa el blob del primero**. Resultado: se
  sube un solo archivo (el segundo), pero la bandeja muestra dos. Ver Duda 3.

---

## Criterios de aceptación

```gherkin
Feature: Un archivo, un ítem en la bandeja del paso 3

  Background:
    Given un caso abierto en el paso 3 "Captura de evidencia" de /dashboard

  Scenario: Grabación con cámara externa aparece una sola vez
    When grabo un video con la cámara externa y elijo "Usar este video"
    Then en "Adjuntos" aparece un solo ítem "grabacion_camara_<ts>.webm"
    And el contador del encabezado suma 1 adjunto
    And el ítem se reproduce en el escenario y muestra su tamaño

  Scenario: Adjunto desde la PC aparece una sola vez, sea imagen, audio o video
    When adjunto una imagen, un audio y un video .mp4 desde la PC
    Then aparecen exactamente 3 ítems en "Adjuntos", uno por archivo
    And ninguno aparece en "Capturas" como "Grabación"

  Scenario: Borrar un ítem lo saca también de lo que se sube
    Given agregué una grabación de cámara externa
    When la borro de la bandeja
    Then no queda ningún ítem con ese nombre en la bandeja
    And al "Enviar evidencia y continuar" ese archivo no se sube
    And en el paso 5 no figura

  Scenario: Lo que se ve es lo que se sube
    Given agregué 1 grabación de cámara externa y 2 adjuntos desde la PC
    When hago "Enviar evidencia y continuar"
    Then se suben exactamente 3 archivos, uno por ítem visible
    And el paso 5 los cuenta una sola vez

  Scenario: Dos tandas de adjuntos en el mismo segundo no se pisan
    When adjunto un archivo y, dentro del mismo segundo, otro archivo distinto
    Then la bandeja muestra 2 ítems con nombres distintos
    And se suben los 2 archivos, cada uno con su contenido

  Scenario: Regresión de los demás orígenes
    When saco la foto del perito y la del titular con la webcam
    And traigo un archivo del celular con el explorador
    And hago una captura y una grabación de pantalla del celular
    Then cada uno aparece una sola vez, en el mismo lugar que hoy
    And los nombres generados siguen el patrón de siempre
```

---

## Datos que se registran

No hay datos nuevos y no cambian colecciones, DTOs ni endpoints. Lo único que tiene que
seguir siendo cierto:

| Dato | Obligatorio | Uso |
|---|---|---|
| Nombre de archivo (`grabacion_camara_<ts>.webm`, `adjunto_<fecha>_<hora>_<n>.<ext>`) | Sí | Nombre con el que se sube y aparece en el informe. Se mantiene el patrón. Ver Duda 3 sobre el `n`. |
| Contenido (blob) | Sí | Se sube una vez y el backend lo hashea una vez. Sin cambios. |

---

## Diseño UX/UI (`client/`, paso 3 de `/dashboard`)

- **Sin cambios visuales.** Se usan el mismo chip de adjunto (`AttachmentChip`), el mismo
  tile y el mismo escenario del rediseño. Lo único que cambia es que cada archivo aparece
  una sola vez.
- El ítem que queda de un archivo local conserva lo que hoy da el chip local: el ícono por
  tipo (Video/Imagen/Audio), el tamaño en MB y la previsualización en el escenario
  (`<video>`/`<img>`/`<audio>` desde el blob). No aparece "Abrir archivo" con una URL del
  agente que no existe.
- El contador del encabezado ("N adjuntos" / "N capturas") coincide con lo que se sube.
- El borrado sigue siendo inmediato, sin confirmación, como hoy.

---

## Fuera de alcance

- La clasificación del paso 5 (`GenerateStep`): hoy `.webm` cuenta como "adjunto" y
  `.mp4` como "video". No cambia.
- Agregar `.webm` a `isVideo` o generar variantes (MCI) para la grabación de cámara externa.
- La confirmación antes de borrar evidencia.
- El backend, Tatana, `agent-ui/` y el contrato de nombres con Tatana (salvo lo que
  defina la Duda 3, que mantiene el patrón).
- Que los adjuntos locales sobrevivan a una recarga de la página. Hoy se pierden los blobs
  que todavía no se subieron, y eso sigue igual.

---

## Notas de implementación (mínimas; el detalle lo escribe el `architect`)

- La causa está en `CaptureStep.handleCameraRecording` y en `processFiles`, que
  registran el archivo en `localFiles` **y** en `files` (`onAttachLocalFile` →
  `handlePhotoBlob`).
- Sea cual sea la solución, **una sola fuente de verdad**: borrar desde la bandeja tiene
  que pasar por `removeFile` para cualquier archivo que esté en `files`.
- No hay tests en `client/`. Lo que se pruebe a mano tiene que cubrir los escenarios de
  borrado y el conteo del paso 5.

---

## Dudas para validar con el usuario

**1. ¿Cómo se deja una sola fuente de verdad?**
- **A (recomendada):** `files` manda. Los archivos locales se muestran solo a partir de
  `files`. La previsualización y el tamaño salen del blob que ya está en `pendingBlobs`
  (o de un mapa de blob URLs por nombre), y `localFiles` desaparece. Borrar siempre llama
  a `removeFile`. *Por qué:* es lo que se sube, así que lo que se ve no puede divergir de
  lo que se sube. Además, la vista sobrevive si `CaptureStep` se desmonta (por ejemplo, al
  ir al paso 2 y volver), algo que hoy `localFiles` pierde.
- B: dejar `localFiles` y filtrar de la galería los ítems de `files` que ya están en
  `localFiles`, y que borrar el chip local también llame a `onRemoveFile`. Es más chico,
  pero sigue habiendo dos listas que hay que mantener sincronizadas a mano.
- C: no llamar a `onAttachLocalFile` y subir desde `localFiles`. Descartada en la
  práctica, porque rompe el flujo de upload y las marcas.

**2. ¿La HU incluye los adjuntos desde la PC o solo la grabación de cámara externa?**
- **A (recomendada):** incluirlos. Es el mismo bug, con la misma causa y la misma línea
  de código, y tiene el mismo riesgo al borrar. Arreglar solo la cámara deja el problema
  a la vista en el caso más común.
- B: solo la cámara externa, como dice el título original, y los adjuntos en otra HU.

**3. Colisión de nombres de adjuntos en el mismo segundo (pérdida silenciosa de evidencia).**
- **A (recomendada):** incluirlo en esta HU. Se mantiene el patrón
  `adjunto_<fecha>_<hora>_<n>.<ext>`, pero `n` pasa a ser un contador que no se repite
  en la sesión del paso 3 (o se garantiza que el nombre no exista ya en `files`).
  *Por qué:* hoy se puede perder un archivo sin ningún aviso. El arreglo toca la misma
  función y no cambia el formato del nombre que espera el resto del sistema.
- B: dejarlo para una HU aparte. Esta queda más acotada, pero el riesgo sigue abierto.
- C: no hacer nada, porque el caso es raro (hay que soltar dos tandas en el mismo segundo).

**4. ¿Dónde va un video adjuntado desde la PC (`.mp4`/`.mov`)?**
- **A (recomendada):** solo en "Adjuntos", como chip "Video" con previsualización. No
  es una grabación del celular, no tiene variantes y el `VideoCard` de "Capturas" queda
  esperando variantes que nunca llegan.
- B: solo en "Capturas", como "Grabación". Se mantiene lo que hoy muestra el lado de
  `files`, con el `VideoCard` vacío.

## Validación (2026-10-01)

El usuario validó las cuatro dudas en la opción **A** (recomendada).
