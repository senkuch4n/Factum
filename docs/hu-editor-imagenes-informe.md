# HU: Imágenes dentro de los campos de redacción del informe

**Slug:** `editor-imagenes-informe`
**Apps afectadas:** `client/` (paso 4 "Informe" del wizard: el editor de texto enriquecido) y
`server/src/Factum.Backend` (endpoint de previsualización, carga de imágenes ilustrativas si D1
lo aprueba, y conversión al DOCX v6 "Filete"). `agent-ui/` y `server/src/Factum.Agent` (Tatana)
**no cambian**.
**Depende de:** `editor-texto-enriquecido` (Markdown acotado + editor WYSIWYG, validada
2026-10-01). Se implementa **después** de ella y se apoya en su editor, en su conversor
Markdown → OpenXML y en la marca `report_texts.formato` (texto plano vs. Markdown).
**Origen:** se desprendió de `editor-texto-enriquecido` (D6 A, validado por el usuario). El pedido
original del perito: "editores de texto como por ejemplo tiene GitLab: negritas, viñetas, código,
**imágenes**, etc."

**Como** perito informático que redacta el informe en Factum
**quiero** poder poner imágenes dentro del texto de las secciones del informe (sobre todo las
capturas que ya tomé del dispositivo, justo donde las describo)
**para que** el informe muestre la evidencia al lado del análisis que la explica, sin tener que
pegar las imágenes a mano en Word después de generarlo, y sin comprometer la integridad de la
evidencia.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-01)

**Cómo se guardan las capturas y los archivos del caso:**

- La captura de pantalla la toma Tatana (`ScreenshotController`, `POST devices/{serial}/screenshot`)
  con nombre `screenshot_<fecha>_<hora>.png` en la carpeta local del agente; la web la muestra
  desde el agente (`agentFileURL`, `localhost:8765/files/...`). Los adjuntos de la PC y las fotos
  de la cámara externa se generan en el navegador como `adjunto_<fecha>_<hora>_<n>.<ext>`
  (`client/src/components/capture/gallery.ts`, `nextAdjuntoName`) y se ven con una blob URL
  local. Las fotos de identidad son `foto_funcionario_*.jpg` / `foto_denunciante_*.jpg`.
- Al "Continuar" del paso 3 (`useFileManager.handleUploadAndContinue`), cada archivo se sube con
  `POST /api/cases/{id}/files?filename=…[&source_path=…]` y `StorageService.SaveFileAsync` lo
  escribe en `Storage:DataDirectory/cases/<caseId>/<filename>`, calculando el SHA-256 mientras
  escribe. **Todo archivo suelto de esa carpeta es evidencia.**
- `StorageService.ListFilesAsync` lista **solo el primer nivel** de la carpeta del caso
  (`Directory.EnumerateFiles` sin recursión): una subcarpeta no entra a la lista de evidencia.
- **No hay endpoint del backend para borrar un archivo ya subido.** "Quitar" en la bandeja borra
  el archivo del agente y de la lista local; si ya estaba subido, sigue en el servidor.
- Roles de captura (`PUT /api/cases/{id}/capture-roles`): una captura puede marcarse
  `imei_modelo` o `nombre_dispositivo`; el rol se puede cambiar mientras el caso es editable.
- Clasificación (`EvidenceClassifier`, espejo de `fileType` del client): es **captura**
  (`Screenshot`) si el nombre contiene `screenshot` o `captura` y la extensión es
  `.png/.jpg/.jpeg`. Los `adjunto_*` de imagen y los archivos copiados del dispositivo
  (`device_pull_*` o con `source_path`) **no** son "captura" y hoy no aparecen como imagen en el
  informe (solo en la tabla de hashes).

**Cómo aparecen hoy las capturas en el DOCX v6 (`ReportService.GenerateDocxAsync`, pasada B-R7):**

- Identificación del dispositivo: las capturas con rol `imei_modelo` (`{capturasImeiModelo}`) y
  `nombre_dispositivo` (`{capturasNombreDispositivo}`), en una caja de 7 × 10.5 cm, con epígrafe
  "Captura de identificación (…) – <archivo>".
- **Anexo – Capturas de pantalla** (`{anexoCapturas}`): las capturas **sin rol**, en orden
  ordinal por nombre de archivo, centradas, ajustadas sin recortar a 14 × 10.5 cm (dos por página
  A4), con `keepNext` y epígrafe "Figura N – <archivo>" (Arial 8 pt, cursiva, gris `#595959`).
  **N se calcula al generar** y depende de qué capturas tienen rol: si una captura gana un rol,
  las figuras siguientes se renumeran.
- La imagen se embebe con los **bytes originales** del archivo (`ImageSource.FromFile`, sin
  recodificar). El tipo de imagen se decide **por extensión**, no por contenido. Un archivo de 0
  bytes se saltea (`IsUsableImage`).
- Los textos del perito pasan por `ReplaceParagraphPerLine` (texto plano, un párrafo por línea);
  `editor-texto-enriquecido` lo reemplaza por un conversor Markdown → OpenXML que **no admite
  imágenes** (en esa HU una imagen pegada no se inserta y se avisa).

**Hashes, ZIP y artefactos (`ReportService.GenerateAsync`, HU `zip-cifrado-real`):**

1. Evidencia = archivos del primer nivel de la carpeta del caso menos los artefactos
   (`IsGeneratedArtifact`: `evidencia_*.zip`, `informe_pericial_*.docx/pdf`, `informe_forense_*`).
2. SHA-256 de cada archivo → tabla de hashes del informe.
3. ZIP (`EvidenceZip.WriteAsync`) con esa evidencia, cifrado AES-256 si `Report:EncryptZip`;
   se verifica entrada por entrada contra los hashes.
4. DOCX con la tabla y el hash del ZIP; **`report_hash` = SHA-256 del DOCX ya cerrado** (todo lo
   que se embeba en el DOCX, imágenes incluidas, queda cubierto por ese hash).
5. Con el ZIP verificado, **se borran los archivos sueltos** de la evidencia. Un caso
   `Completed` no se edita ni se regenera (409).

**Qué endpoints sirven archivos a la web:**

| Endpoint | Qué hace | ¿Sirve para previsualizar en el editor? |
|---|---|---|
| `GET /api/cases/{id}/files` | Lista nombre, tamaño, fecha (y `source_path`) del primer nivel de la carpeta | No devuelve contenido |
| `GET /api/cases/{id}/download/{filename}` | `PhysicalFile` de **cualquier** archivo de la carpeta, como descarga (`Content-Disposition: attachment`), `application/octet-stream` salvo ZIP/PDF/DOCX | No: es descarga, no valida el tipo de contenido |
| `GET /api/config/branding/logo` | Logo de la organización | No aplica |
| Agente Tatana `GET /files/{filename}` | Archivos locales del agente | No: en el paso 4 el agente puede no estar abierto y los archivos ya están en el servidor |

La API usa **JWT Bearer en el header** (`client/src/lib/api.ts`), así que un `<img src="…">`
directo al backend no se autentica: la vista previa tiene que pedirse con `fetch` y mostrarse
con una blob URL (detalle para la SDD).

`ReportStep` hoy **no recibe** la lista de archivos del caso (solo `caseId`); para elegir una
captura tiene que pedirla (`GET /files`) o recibirla del dashboard.

**Piezas reutilizables:** `ImageProbe.TryDetect` (`Services/Branding/ImageProbe.cs`) ya detecta
PNG/JPEG **por magic bytes** y lee ancho/alto sin caer a un default (se usa para el logo de la
organización). `BuildImageParagraph` / `BuildCaptionParagraph` / `BuildDrawing` de
`ReportService` ya arman la imagen inline + epígrafe con el estilo de la v6.

**Observaciones al pasar (fuera de alcance, para el architect):** `DownloadAsync` no valida que
`filename` sea un nombre plano (a diferencia de `UpsertCaptureRolesAsync`), y `UploadFileAsync`
no chequea que el caso sea editable ni sanea el nombre. No los toca esta HU, pero el endpoint
nuevo **no** tiene que copiar ese patrón.

### Qué es lo nuevo

1. En el editor de cada sección del paso 4, un botón **"Imagen"** que permite **insertar una
   captura ya tomada del caso** (y, si D1 lo aprueba, **subir una imagen ilustrativa** que no es
   evidencia).
2. La imagen queda guardada en el Markdown de la sección como una **referencia** (no los bytes),
   con un esquema propio (D3), y el editor la muestra con una vista previa obtenida de un
   **endpoint autenticado de solo lectura** (D6).
3. El generador del DOCX v6 inserta la imagen en el cuerpo de la sección, centrada y con
   epígrafe numerado (D4, D5), embebiendo los **mismos bytes** del archivo de evidencia.
4. El Anexo de capturas, la tabla de hashes, el ZIP y la forma de calcular `report_hash` **no
   cambian** para la evidencia (D4, D7).

---

## Criterios de aceptación

```gherkin
Feature: Imágenes en las secciones del informe

  Background:
    Given un perito logueado con un caso en borrador
    And el caso tiene subidas las capturas "screenshot_20261001_101010.png" y "screenshot_20261001_101530.png"
    And está en el paso 4 "Informe" con el editor de texto enriquecido

  # ── Insertar una captura del caso ─────────────────────────────────
  Scenario: Insertar una captura ya tomada
    Given el cursor está en una línea de "Resultados"
    When el perito aprieta "Imagen" y elige "Captura del caso"
    Then ve una grilla con las miniaturas de las capturas del caso, con su nombre de archivo
    And las capturas con rol se identifican ("IMEI y modelo" / "Nombre del dispositivo")
    When elige "screenshot_20261001_101010.png", escribe la descripción "Chat con Juan" y confirma
    Then la imagen aparece en el editor como un bloque propio debajo del párrafo
    And debajo se ve el epígrafe provisorio "Figura · Chat con Juan (screenshot_20261001_101010.png)"
    And el texto se autoguarda con una referencia a la captura, no con la imagen

  Scenario: Las fotos de identidad no se ofrecen
    Given el caso tiene "foto_funcionario_20261001_100000.jpg"
    When el perito abre "Captura del caso"
    Then esa foto no aparece en la grilla

  Scenario: Caso sin capturas
    Given el caso no tiene capturas
    When el perito abre "Captura del caso"
    Then ve "Este caso no tiene capturas. Las capturas se toman en el paso 3."

  Scenario: Editar o quitar una imagen insertada
    Given "Resultados" tiene una captura insertada
    When el perito selecciona la imagen
    Then puede cambiar su descripción o quitarla
    And quitarla del texto no borra ni modifica el archivo de evidencia

  # ── Imagen ilustrativa (si D1 = C) ────────────────────────────────
  Scenario: Subir una imagen ilustrativa
    When el perito aprieta "Imagen" y elige "Imagen ilustrativa (no es evidencia)"
    And sube un PNG de 800 KB con un diagrama
    Then ve el aviso "Esta imagen no es evidencia: no entra en el ZIP ni en la tabla de hashes"
    And la imagen aparece en el editor con un distintivo "Ilustrativa"
    And el archivo no aparece en la lista de archivos del caso ni en la bandeja del paso 3

  Scenario: Tipo o tamaño no permitido
    When el perito intenta subir un SVG, un GIF, un HEIC, un archivo de más de 5 MB o un ".png" que en realidad no es una imagen
    Then el servidor la rechaza
    And el editor muestra "Solo se admiten imágenes PNG o JPEG de hasta 5 MB"
    And no queda nada guardado

  # ── Vista previa ──────────────────────────────────────────────────
  Scenario: La vista previa no depende del agente
    Given Tatana está cerrado
    When el perito abre el paso 4 de un caso con capturas insertadas
    Then las imágenes se ven igual, porque se piden al backend

  Scenario: Solo el dueño del caso ve las imágenes
    Given otro perito conoce el id del caso y el nombre de una captura
    When pide la vista previa
    Then recibe 403 y ningún byte de la imagen

  Scenario: El endpoint no sirve cualquier archivo
    When alguien pide la vista previa de "../otro-caso/archivo.png", de un video, del ZIP o del DOCX
    Then recibe un error y ningún byte

  # ── Informe DOCX ──────────────────────────────────────────────────
  Scenario: La captura sale en el cuerpo del informe
    Given "Resultados" tiene "Se observa la conversación:" seguido de la captura "screenshot_20261001_101530.png" con la descripción "Chat con Juan"
    When el perito genera el informe
    Then en la sección Resultados del DOCX, después de ese párrafo, está la imagen centrada y ajustada sin recortar según D5
    And debajo, el epígrafe con el formato de D4 (p. ej. "Figura 2 – Chat con Juan (screenshot_20261001_101530.png)")
    And la imagen no se separa de su epígrafe al cambiar de página

  Scenario: La captura sigue en el anexo con el mismo número
    Given la captura del cuerpo es la "Figura 2" del Anexo
    When se genera el informe
    Then el Anexo – Capturas de pantalla sigue mostrando todas las capturas sin rol, como hoy
    And la captura del cuerpo y la del anexo tienen el mismo número de figura (según D4)

  Scenario: Los bytes embebidos son la evidencia
    Given una captura insertada en el cuerpo
    When se genera el informe
    Then el SHA-256 de la imagen embebida en el DOCX es igual al de esa captura en la tabla de hashes
    And el archivo de evidencia no se recodifica, recorta ni modifica

  Scenario: La tabla de hashes y el ZIP no cambian
    Given un caso con capturas insertadas en el cuerpo (e imágenes ilustrativas, si D1 = C)
    When se genera el informe
    Then la tabla de hashes y el ZIP tienen exactamente los mismos archivos que tendrían sin las imágenes en el texto
    And las imágenes ilustrativas no aparecen en la tabla ni en el ZIP (según D7)
    And report_hash se calcula como hoy, sobre el DOCX ya cerrado

  Scenario: Imagen ilustrativa en el DOCX
    Given "Valoración técnica" tiene una imagen ilustrativa con la descripción "Esquema de la extracción"
    When se genera el informe
    Then la imagen sale centrada con el epígrafe "Ilustración 1 – Esquema de la extracción" (según D4)
    And no se confunde con una figura de evidencia

  Scenario: El DOCX abre limpio
    Given varias secciones con imágenes, listas y negritas
    When se genera el informe
    Then el DOCX abre sin avisos de reparación en Word, LibreOffice y WPS
    And el PDF convertido con LibreOffice muestra las imágenes en el mismo lugar

  # ── Referencias rotas (D8) ────────────────────────────────────────
  Scenario: La captura referenciada ya no está o está vacía
    Given "Resultados" referencia una captura que no existe en el servidor o pesa 0 bytes
    Then en el editor se ve un recuadro "Imagen no disponible: <archivo>" con el botón "Quitar"
    And el checklist del paso "Generar" lo marca como pendiente con un enlace a la sección
    And el servidor no genera el informe y lo informa en "missing"

  Scenario: La captura cambia de rol
    Given "Resultados" referencia una captura que después se marca como "IMEI y modelo"
    When se genera el informe
    Then la imagen sale en el cuerpo con el epígrafe que corresponda según D4
    And el anexo y su numeración se calculan igual que hoy

  # ── Seguridad ─────────────────────────────────────────────────────
  Scenario: Referencias externas o inventadas
    Given alguien manda por API un texto con "![x](https://sitio/imagen.png)", "![x](data:image/png;base64,…)" o "![x](file:///etc/passwd)"
    When se guarda el texto o se genera el informe
    Then el servidor rechaza el texto (o neutraliza la referencia según la SDD)
    And el DOCX nunca descarga ni embebe una imagen externa

  # ── Compatibilidad (regla dura de datos) ──────────────────────────
  Scenario: Casos sin imágenes
    Given un caso (viejo o nuevo) cuyos textos no tienen imágenes
    When se genera el informe
    Then el DOCX sale igual que con editor-texto-enriquecido, sin cambios

  Scenario: Textos en texto plano
    Given un caso cuyos report_texts están en texto plano (sin la marca de Markdown)
    When se genera el informe
    Then un "![...](...)" escrito ahí sale como texto literal, sin intentar insertar una imagen

  Scenario: Sin tocar la evidencia existente
    When se despliega esta HU
    Then ningún documento de cases ni ningún archivo de Storage:DataDirectory cambia
    And los casos Completed y sus ZIP, DOCX, PDF y hashes quedan byte a byte iguales

  # ── Regresión ─────────────────────────────────────────────────────
  Scenario: El resto no cambia
    When el perito hace una inspección completa y genera el informe
    Then la captura, los roles, la tabla de hashes, el anexo, el ZIP cifrado y la contraseña funcionan igual
    And "npx tsc --noEmit" en client/ y "dotnet build" / "dotnet test" del Backend terminan sin errores
```

---

## Datos que se registran

| Dato | Obligatorio | Uso |
|---|---|---|
| Referencia a una captura dentro de `report_texts.<clave>` (Markdown) | No | P. ej. `![Chat con Juan](captura:screenshot_20261001_101530.png)` (formato exacto: D3). Guarda **el nombre del archivo de evidencia** y la descripción del perito; **no** guarda el número de figura (se calcula al generar, D4). Cuenta dentro de los 20 000 caracteres de la sección. |
| Referencia a una imagen ilustrativa (si D1 = C) | No | P. ej. `![Esquema](ilustrativa:<id>.png)`. El `<id>` lo genera el servidor (nunca el nombre que trae el perito). |
| Archivo de imagen ilustrativa (si D1 = C) | — | PNG/JPEG ≤ 5 MB, validado por contenido, guardado **fuera** de la evidencia del caso (D7). No entra en la tabla de hashes ni en el ZIP; queda cubierto por `report_hash` al ir embebido en el DOCX. |
| Archivos de evidencia (capturas) | — | **Sin cambios**: no se copian, no se mueven, no se recodifican, no se renombran. |

No hay colecciones nuevas. Si la SDD necesita registrar metadatos de las ilustrativas (nombre
original, tamaño, SHA-256, fecha), lo propone ahí.

---

## Diseño UX/UI (`client/`, paso 4 "Informe"; sin cambios en `agent-ui/`)

### Entrada

En la barra del editor (definida por `editor-texto-enriquecido`), un botón **"Imagen"** (ícono
`ImagePlus` de `lucide-react`) en el grupo de bloques, con tooltip. Si D1 = C abre un menú chico:

- **Captura del caso** (ícono `Smartphone`) — "Ya es evidencia: va con su número de figura."
- **Imagen ilustrativa** (ícono `Shapes`) — "Diagrama o referencia. No es evidencia."

Si D1 = A, el botón abre directo el selector de capturas.

### Selector de capturas (diálogo)

```
┌ Insertar captura del caso ───────────────────────────────── ✕ ┐
│ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐                           │
│ │ img  │ │ img  │ │ img  │ │ img  │   ← miniaturas (vista      │
│ │      │ │ IMEI │ │      │ │      │     previa del backend),   │
│ └──────┘ └──────┘ └──────┘ └──────┘     chip si tiene rol      │
│ ..._101010  ..._101530 ...                                     │
│                                                                │
│ Descripción (epígrafe)  [Chat con Juan________________]        │
│ Se verá como: "Figura N – Chat con Juan (screenshot_…png)"     │
│                                        [Cancelar] [Insertar]   │
└────────────────────────────────────────────────────────────────┘
```

- `Dialog` de PrimeReact (unstyled + pt), grilla navegable con teclado (flechas, Enter), cada
  miniatura con `alt` = nombre del archivo y estado seleccionado visible (anillo `--fx-*`).
- Orden: el mismo del anexo (ordinal por nombre), para que el perito reconozca la numeración.
- Descripción opcional (máx. sugerido 200 caracteres); si queda vacía, el epígrafe usa solo el
  nombre del archivo, como el anexo de hoy.
- Miniaturas con carga perezosa y esqueleto mientras llegan.

### Imagen dentro del editor

- Bloque propio (no dentro de una línea de texto), centrado, ancho máximo ~60 % del editor,
  borde fino y radio como las tarjetas de la app.
- Debajo, el epígrafe en gris cursiva: "Figura · <descripción> (<archivo>)" — el número exacto
  se resuelve al generar y el editor lo aclara con un tooltip ("El número se asigna al generar el
  informe, igual que en el anexo"). Ilustrativas: distintivo "Ilustrativa" y epígrafe
  "Ilustración · <descripción>".
- Al seleccionarla (clic o flechas): barra flotante con "Editar descripción" y "Quitar"
  (Supr/Backspace también la quita; Ctrl/Cmd+Z la recupera).
- Accesible: el bloque tiene `role="img"` con `aria-label` = descripción + archivo.

### Estados y errores

| Situación | Qué se ve |
|---|---|
| Cargando la vista previa | Esqueleto del tamaño aproximado de la imagen. |
| Captura inexistente, vacía o ilegible | Recuadro punteado "Imagen no disponible: <archivo>" con botón "Quitar"; el checklist de "Generar" la lista (D8). |
| Error de red al cargar la vista previa | "No se pudo cargar la vista previa · Reintentar" (no bloquea la edición). |
| Subida ilustrativa en curso | Barra de progreso en el bloque; "Continuar" espera a que termine o falle. |
| Subida rechazada | Toast de error con el motivo (tipo, tamaño, contenido no válido); no se inserta nada. |
| Imagen pegada o arrastrada | Según D10. |
| Caso `Completed` | No se edita (como hoy). |

---

## Fuera de alcance

- Cambiar el Anexo – Capturas de pantalla, la sección de identificación, la tabla de hashes o el
  contenido del ZIP para la evidencia.
- Recortar, rotar, anotar, resaltar o marcar sobre la imagen (eso alteraría lo que se muestra
  como evidencia).
- Imágenes dentro de una línea de texto, varias imágenes en fila, texto alrededor de la imagen,
  tamaños elegidos por el perito (salvo que D5 diga otra cosa).
- Videos, audios, GIF animados, SVG, WebP, HEIC.
- Imágenes de URLs externas y `data:` URIs.
- Endpoint para **borrar** archivos de evidencia ya subidos.
- Insertar fotos de identidad (`foto_funcionario_*`, `foto_denunciante_*`): no van al informe
  (D9 B de `informe-pericial-de-parte`).
- Miniaturas recodificadas del lado del servidor (salvo que la SDD las necesite por rendimiento).
- Corregir `DownloadAsync` / `UploadFileAsync` (observaciones del Contexto): HU o tarea aparte.
- Cambios en `agent-ui/` y en Tatana.

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- **Depende de la SDD de `editor-texto-enriquecido`** (`Refactorizaciones/editor-texto-enriquecido.md`,
  en diseño): el nodo imagen se agrega al mismo esquema del editor (Tiptap o el que se elija) y al
  mismo conversor Markdown → OpenXML. Solo se interpreta en textos con la marca de Markdown;
  en texto plano, `![...](...)` es literal.
- **Validación en servidor** al guardar `report_texts`: toda imagen tiene que usar el esquema
  propio (D3) con un nombre plano (`Path.GetFileName(x) == x`, sin `..`); el resto se rechaza.
- **Endpoint de vista previa** (D6): nombre plano, archivo del primer nivel de la carpeta del
  caso, clasificado como captura (o ilustrativa del caso), detectado PNG/JPEG con
  `ImageProbe.TryDetect` (no por extensión), `Content-Type` del tipo detectado,
  `Content-Disposition: inline`, `X-Content-Type-Options: nosniff`, `Cache-Control: private,
  no-store`, lectura con `FileShare.Read` sin modificar el archivo. En el client: `fetch` con
  Bearer → blob URL, revocada al desmontar (patrón de `useFileManager`).
- **Ilustrativas** (si D1 = C): fuera del primer nivel de la carpeta del caso, para que
  `ListFilesAsync` nunca las tome como evidencia. Ojo: `DeleteCaseFiles` borra recursivo. La SDD
  decide la ruta (p. ej. `cases/<id>/informe/` vs. una raíz hermana) y qué pasa con las
  ilustrativas huérfanas (subidas y después quitadas del texto) al generar.
- **Generación:** reusar `BuildImageParagraph` + `BuildCaptionParagraph`; la numeración de
  figuras del cuerpo se calcula con la **misma lista** que usa `{anexoCapturas}` (capturas sin
  rol, orden ordinal, `IsUsableImage`). El tipo de `ImagePart` sale del contenido detectado, no
  de la extensión. `docPr id` único con el contador `drawId` existente.
- **Checklist / `missing`:** referencias rotas en `CaseValidation.ValidateForGenerate` y en
  `getMissingRequirements` del client (clave por sección, p. ej. `report_texts.<clave>.imagen`;
  la SDD fija el nombre).
- **Tests:** conversor con imágenes (captura, ilustrativa, rota, esquema externo, texto plano con
  `![…](…)` literal), SHA-256 de la imagen embebida = SHA-256 de la evidencia, DOCX sin errores
  de validación OpenXML, tabla de hashes y lista del ZIP idénticas con y sin imágenes en el texto.
- **Regla dura de datos:** ningún implementador modifica ni borra archivos de
  `Storage:DataDirectory` ni documentos de `cases` preexistentes; las pruebas crean sus propios
  casos y limpian solo por los `_id` / rutas que ellas mismas crearon.

---

## Dudas para validar con el usuario

### D1. Origen de las imágenes
- **A) Solo capturas ya tomadas del caso.** Ya son evidencia hasheada; no se sube nada nuevo.
- **B) Solo imágenes ilustrativas** subidas desde la PC (diagramas, logos de apps, esquemas).
- **C) Ambas**, con la diferencia bien visible (menú "Captura del caso" / "Imagen ilustrativa
  (no es evidencia)", distintivo en el editor y epígrafe distinto en el DOCX).
- **Recomendada: A en esta HU**, y las ilustrativas como HU chica posterior si las pedís. Cubre
  el caso principal (mostrar la evidencia donde se la analiza) sin abrir la discusión forense de
  "contenido del informe que no es evidencia", sin endpoint de subida ni almacenamiento nuevo.
  Si las ilustrativas te hacen falta ya, C es viable y la HU ya trae sus criterios (D7, D9).

### D2. Qué archivos del caso se pueden insertar
- **A) Solo capturas** (`screenshot_*` / `captura_*`, PNG/JPEG), con o sin rol. Son las que
  ya salen como imagen en el informe (identificación y anexo).
- **B) A + imágenes PNG/JPEG de los adjuntos** (`adjunto_*`, p. ej. fotos del teléfono con la
  cámara externa) **y de archivos copiados del dispositivo**. Hoy no salen como imagen en
  ningún lado del informe, así que no tienen número de figura.
- **Recomendada: A.** Mantiene una sola regla ("lo que se inserta es una figura del informe") y
  la numeración del anexo. Si las fotos con cámara externa son frecuentes en tu trabajo, decilo:
  lo correcto sería que también entren al anexo (cambio aparte) y después se puedan insertar.
  Las fotos de identidad quedan afuera en cualquier opción.

### D3. Cómo se guarda la imagen en el Markdown
- **A) Sintaxis de imagen de Markdown con esquema propio:**
  `![<descripción>](captura:<nombre de archivo>)` (y `ilustrativa:<id>` si D1 = C). Sin
  número de figura en el texto; cualquier otro esquema (`http:`, `https:`, `data:`, `file:`,
  rutas) se rechaza en el servidor y en el editor.
- **B) Marcador propio fuera de Markdown,** p. ej. `{{figura:screenshot_….png|descripción}}`.
- **C) Guardar el número**, p. ej. `![Figura 3 – …](captura:…)`.
- **Recomendada: A.** Es Markdown estándar (se lee en Mongo y en cualquier visor), el esquema
  propio impide imágenes externas, y no guardar el número evita que quede desactualizado cuando
  una captura gana o pierde un rol. C se rompe con cada cambio de rol; B no es Markdown.

### D4. Relación con el anexo, numeración y epígrafe en el DOCX
- **A) La captura queda en el anexo y en el cuerpo se repite con el mismo número:**
  "Figura N – <descripción> (<archivo>)", donde N es su número en el Anexo. Si la captura
  tiene rol (identificación), el epígrafe del cuerpo es "Captura de identificación (…) –
  <descripción> (<archivo>)", sin número. Ilustrativas (si D1 = C): numeración aparte,
  "Ilustración N – <descripción>", en orden de aparición.
- **B) Numeración única por orden de aparición en todo el documento** (cuerpo primero, anexo
  después): obliga a renumerar el anexo y el anexo deja de ser el orden por nombre de archivo.
- **C) Sacar del anexo las capturas que ya están en el cuerpo:** el anexo deja de ser la lista
  completa de capturas.
- **Recomendada: A.** El anexo sigue siendo, como hoy, la lista completa de capturas, y cada
  captura tiene **un solo número** en todo el informe (sirve como identificador, "ver Figura 3").
  Que en el cuerpo aparezca "Figura 7" antes que "Figura 2" es normal en un informe que cita un
  anexo. El nombre del archivo en el epígrafe permite cruzarla con la tabla de hashes.

### D5. Tamaño y alineación en el DOCX
- **A) Fijo:** centrada, ajustada sin recortar a la misma caja del anexo (14 × 10.5 cm), con el
  epígrafe pegado (`keepNext`). Una captura vertical de celular queda ~5 cm de ancho.
- **B) Fijo más chico** (7 × 10.5 cm, como la identificación), para que el cuerpo no se llene.
- **C) El perito elige** por imagen: chica (7 × 10.5) / grande (14 × 10.5).
- **Recomendada: A.** Igual que el anexo (coherencia visual de la v6), legible impresa y sin
  decisiones extra para el perito. C se puede sumar después si hace falta.

### D6. Endpoint para previsualizar capturas en el editor
Hoy no hay forma de mostrar una captura del servidor en la web (el agente puede estar cerrado).
- **A) Endpoint nuevo de solo lectura** (p. ej. `GET /api/cases/{id}/files/{filename}/preview`),
  autenticado, **solo para el perito dueño del caso** (403 a otro, como el resto), solo
  capturas PNG/JPEG validadas por contenido, `inline`, sin caché compartida, sin modificar el
  archivo. Mientras el caso no esté `Completed` (después los archivos sueltos ya no existen).
- **B) Reusar `download/{filename}`** agregándole un modo inline.
- **Recomendada: A.** Un endpoint acotado a imágenes es más fácil de asegurar que ampliar
  `download`, que hoy sirve cualquier archivo de la carpeta. Pregunta adicional: ¿querés que
  cada acceso quede en la auditoría (`agent-events`)? **Recomendado: no**, es una vista del
  propio perito sobre su propio caso y generaría mucho ruido.

### D7. Imágenes ilustrativas: ¿entran al ZIP o a la tabla de hashes? (solo si D1 = B o C)
- **A) No entran ni al ZIP ni a la tabla de hashes.** Se guardan fuera de la carpeta de
  evidencia, se embeben en el DOCX y quedan cubiertas por `report_hash` (el hash del DOCX
  cerrado incluye sus bytes). El epígrafe "Ilustración N" las distingue de la evidencia.
- **B) Entran al ZIP y a la tabla** como un grupo aparte ("Material ilustrativo").
- **C) Solo a la tabla** (hash visible, sin ZIP).
- **Recomendada: A.** No son evidencia: mezclarlas en el ZIP o en la tabla confunde qué se
  adquirió del dispositivo y qué agregó el perito. `report_hash` ya garantiza que no se cambien
  después de generar, sin tocar el cálculo de hoy.

### D8. Qué pasa si la captura referenciada falta o cambia
Hoy un archivo subido no se puede borrar por la API, pero puede faltar (quitado de la bandeja
antes de subir, borrado del disco a mano, 0 bytes o corrupto) o cambiar de rol.
- **A) Bloquear la generación:** el editor muestra "Imagen no disponible" con "Quitar", el
  checklist de "Generar" lo lista y el servidor lo devuelve en `missing`. Un cambio de rol no
  bloquea: el epígrafe se adapta (D4).
- **B) Generar igual** con un recuadro "[Imagen no disponible: <archivo>]" en el DOCX y un aviso.
- **C) Omitirla en silencio.**
- **Recomendada: A.** En un informe pericial no debería salir una referencia a evidencia que no
  se puede mostrar ni verificar; el perito decide quitarla o volver a subirla.

### D9. Seguridad y límites
- Tipo real por **magic bytes** (reusar `ImageProbe`): solo PNG y JPEG; SVG, GIF, WebP, HEIC,
  BMP y PDF rechazados aunque cambien la extensión. **¿De acuerdo? Recomendado: sí.**
- Ilustrativas (si aplica): **máx. 5 MB**, máx. 8 000 px por lado, nombre interno generado por el
  servidor. **Recomendado: sí.**
- **Metadatos EXIF** de las ilustrativas (pueden traer ubicación GPS o el modelo de la cámara):
  ¿se quitan? **Recomendado: sí, quitarlos** (la imagen ilustrativa no es evidencia, así que
  alterarla no tiene costo forense, y evita filtrar datos del perito). Las capturas de
  evidencia **nunca** se tocan.
- **Cantidad:** ¿un tope de imágenes? **Recomendado: 20 por sección** (de evidencia e
  ilustrativas juntas), para que un error no genere un DOCX inmanejable. ¿Te parece bien, o
  tenés informes con más?

### D10. Pegar o arrastrar una imagen en el editor
- **A) Se bloquea con un aviso:** "Para insertar una imagen usá el botón Imagen" (como en
  `editor-texto-enriquecido`).
- **B) Se abre el diálogo de "Imagen ilustrativa"** con la imagen precargada (solo si D1 = C).
- **Recomendada: A.** Una captura de pantalla pegada desde el portapapeles puede parecer
  evidencia sin serlo; obligar a pasar por el botón deja explícito el origen.

### D11. En qué secciones se permiten imágenes
- **A) En las ocho** (mismo editor en todas).
- **B) Solo en Operaciones realizadas, Aseguramiento de la evidencia, Resultados y Valoración
  técnica** (donde tiene sentido mostrar evidencia).
- **Recomendada: A.** Un solo editor, sin excepciones que explicar ni probar; si el perito no
  quiere imágenes en Conclusiones, no las pone.

### D12. Una sección obligatoria que solo tiene una imagen
- **A) Sigue contando como vacía** (falta texto) en el checklist y en `missing`.
- **B) Cuenta como completa.**
- **Recomendada: A.** Toda sección obligatoria necesita al menos una frase del perito; una
  imagen sin análisis no cumple el propósito de la sección. Es consistente con la regla de
  `editor-texto-enriquecido` (solo cuenta el texto visible).

## Validación (2026-10-01)

El usuario validó las 12 dudas en la opción **A** (recomendada). D1 A: solo capturas del caso; las ilustrativas quedan para una HU posterior si se piden.
