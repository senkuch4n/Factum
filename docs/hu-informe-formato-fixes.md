# HU: Arreglos de formato y robustez del informe pericial (plantilla v6)

**Slug:** `informe-formato-fixes` · **Issue:** #62
**Apps afectadas (preliminar, lo confirma la SDD):** backend API (`server/src/Factum.Backend/Services/Reports/`,
`Services/Cases/CaseValidation.cs`, `Models/DeviceInfo.cs`), plantilla v6 (`ops/plantilla/build_plantilla_v4.py` /
`build_plantilla_v6.py` → `Templates/plantilla_informe_v{4,6}.docx`) y `client/` (web) según D2 y D3. `agent-ui/` y el
agente Tatana no cambian.

**Como** perito que entrega el informe pericial DOCX que genera Factum
**quiero** que el informe se genere siempre (sin que un carácter raro lo rompa), que la firma no quede separada del
firmante, que no haya texto invertido ni frases mal armadas y que la portada y el encabezado aguanten datos largos
**para que** el escrito que presento ante el tribunal sea confiable y prolijo sin tener que corregirlo a mano en Word.

Origen: QA de formato del informe del 2026-10-13 (`docs/qa/2026-10-13/informe-formato.md`, hallazgos H01-H19 y R1-R3),
priorizado en `docs/qa/2026-10-13/INFORME.md` (ítem 5). Relacionado con API-05 de `docs/qa/2026-10-13/api-backend.md`,
que está asignado a la HU #63 `api-hardening` (ver D1).

---

## Contexto

### Qué existe hoy

- **Generación del informe.** `ReportService` (`Services/Reports/ReportService.cs`) llena la plantilla
  `Templates/plantilla_informe_v6.docx` con los placeholders de `ReportValues.Placeholders` (`ReportValues.cs:67-125`) y
  renderiza los textos del paso Informe con `ReportMarkdownRenderer` (dialecto Markdown de Factum). El DOCX se guarda con
  `doc.Save()`.
- **Sanitizado actual.** `ReportValues.Clean` (`ReportValues.cs:18-29`) normaliza saltos, pasa el tab a espacio y saca
  los controles `< 0x20` salvo `\n`. **Deja pasar todo lo que es `>= 0x20`**, incluidos U+FFFE/U+FFFF (inválidos en XML
  1.0) y los controles bidi U+202A-202E / U+2066-2069. `Clean` se usa en placeholders, textos markdown
  (`ReportMarkdown.cs:84,348`, `ReportMarkdownRenderer.cs:255,284,438`) y en varios puntos de `ReportService`
  (`:540,598,626,862,1148`). El cliente tiene un espejo exacto, `cleanText` en `client/src/lib/report-markdown.ts:25-31`,
  porque `plainToMarkdown` tiene que dar el mismo resultado byte a byte que `ReportMarkdown.FromPlainText` del backend
  (fixtures F1-F17).
- **Validación al guardar.** `CaseFields.From` (`CaseValidation.cs:20-46`) solo hace `Trim`; `ValidateCaseData` controla
  largos (300 / 500 / 20 000). `ValidateReportTexts` valida formato, largo y contenido markdown, no caracteres. Perfil del
  perito y Branding tampoco filtran caracteres. Resultado (H02): un `"A￾ B"` en la carátula entra por la API, se
  guarda en Mongo y después la generación falla siempre con `ArgumentException ... 0xFFFE` en `doc.Save()`: el caso
  queda sin poder generar informe. Un surrogate suelto (`\uD800`) también rompe el generador, pero `System.Text.Json`
  lo rechaza antes con 400.
- **Sistema operativo (H03).** Al crear el caso, `client/src/app/dashboard/page.tsx:411-413` manda
  `os_version: "Android ${android_version}"` para Android y la versión pelada (`ios_version`, p. ej. `"17.4"`) para iOS.
  El backend lo guarda tal cual (`CaseService.cs:259`) y `DeviceInfo.OsLabel` (`Models/DeviceInfo.cs:18-20`) antepone
  `"Android "` / `"iOS "`. El token `{sistemaOperativo}` de los textos por defecto (`ReportValues.cs:162`) sale entonces
  como "Android Android 11" en **todo caso Android creado desde la web**, y ese texto queda escrito en el
  `report_texts.operaciones_realizadas` guardado del caso cuando el perito acepta el default. Otras lecturas del
  cliente: `CaseDetailContent.tsx:41` muestra `os_version` tal cual (hoy "Android 11", bien) y `GenerateStep.tsx:187`
  arma la etiqueta Android desde `android_version` (no depende de `os_version`).
- **Plantilla.** La cadena es plantilla privada del usuario (`docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx`,
  no versionada, presente en esta máquina) → `build_plantilla_v4.py` → v4 → `build_plantilla_v6.py` → v6. El bloque de
  cierre ("SE DEJA CONSTANCIA" / línea de firma / nombre / profesión y M.P. / carácter / isotipo) se arma en
  `build_plantilla_v4.py:367-372` y `build_plantilla_v5.py:649` sin `keepNext`/`keepLines` (H01). La frase "siendo
  horas {horaInspeccion}" está en `build_plantilla_v4.py:310` (H10). Los tests del backend comparan el texto pericial
  contra la v4, así que cambiar una frase fija obliga a regenerar v4 y v6 y ajustar esos tests.
- **Portada y encabezado.** La portada es una sección propia con la ficha (causa, carátula, perito, fecha) y el
  membrete de la marca en `footer2.xml`; el encabezado interior (`header1.xml`) es una tabla con
  "Informe pericial ... · {tipoCausa} N° {numeroCausa}" a la izquierda y `{ORGANIZACION}` a la derecha
  (`ops/plantilla/README.md`, sección v6). Límites actuales: carátula 500 caracteres (`CaseValidation.MaxLong`), nombre de
  la organización 150, hasta 6 líneas de contacto de 150 (`BrandingService.cs:38-40`).
- **Imágenes.** `BuildImageParagraphForRel` (`ReportService.cs:943-960`) ajusta cada imagen a la caja máxima
  (14 x 10,5 cm en el anexo) **también hacia arriba**: una captura de 200x150 px se estira a 14 cm y se pixela (H07).
- **Editor del informe (`client/`).** Tiptap con un solo nivel de subtítulo (`FxHeading` parsea todo `#`..`######`
  como nivel 3, `client/src/components/editor/extensions.ts:67-68`) y sin tablas: al pegar una tabla HTML se convierte
  en un párrafo por fila con " | " y se muestra el aviso "formats" (`pasteTransform.ts:17,94-107`). Por eso H08 (títulos
  `#`/`##`/`###` iguales) y H09 (tabla GFM como texto) **solo se alcanzan mandando markdown crudo por la API**, no desde
  la UI.

### Lo que el QA confirmó que está bien (no se toca)

XML bien formado con datos normales, imágenes sin deformar, numeración de listas y figuras, encabezado/pie
consistentes, fechas en zona AR, HTML y markdown escapados literales, opcionales vacíos sin títulos sueltos, títulos de
sección con `keepNext`, tabla de hashes con encabezado repetido.

### Qué es nuevo

Un paquete de arreglos sobre lo que ya existe, sin cambiar el contrato de generación ni el flujo del perito:

1. **Robustez de caracteres (H02, H04):** el generador nunca falla por un carácter inválido en XML y no deja pasar
   controles bidi; además la API deja de guardar esos caracteres (alcance según D1).
2. **Sistema operativo sin duplicar (H03)**, también para casos ya guardados (D2).
3. **Firma indivisible (H01).**
4. **Portada en una página y encabezado acotado con datos largos (H05, H06)** según la política de D3.
5. **Imágenes chicas sin ampliar (H07)** según D4.
6. **Menores de redacción y formato** de la plantilla y los valores (H10, H11, H12, H13, H14, H16, H17), según D5-D6.

### Propuesta de qué hallazgos entran

| Hallazgo | Propuesta | Por qué |
|---|---|---|
| H01 firma partida | **Entra** | Defecto clásico de escrito judicial; arreglo chico en plantilla. |
| H02 U+FFFE/U+FFFF rompen la generación | **Entra** | Deja un caso sin poder generar; es el único hallazgo que bloquea. |
| H03 "Android Android 11" | **Entra** | Sale en todo informe Android real. |
| H04 bidi U+202E | **Entra** | Spoofing en un documento con valor probatorio; mismo lugar que H02. |
| H05 portada en 2 páginas | **Entra** (política en D3) | Solo con datos al máximo, pero los límites los pone el propio sistema. |
| H06 encabezado de 4 líneas | **Entra** (política en D3) | Ídem. |
| H07 capturas chicas pixeladas | **Entra** | Una línea en el cálculo del tamaño; mejora la lectura pericial. |
| H08 títulos `#`/`##`/`###` iguales | **Fuera** (D6) | El editor solo produce un nivel de subtítulo; solo se alcanza por API. |
| H09 tabla markdown como texto | **Fuera** (D6) | El editor ya convierte tablas pegadas a filas con aviso; lo tipeado se ve igual en el editor que en el informe. |
| H10 "siendo horas" | **Entra** | Frase fija mal redactada. |
| H11 sangrías inconsistentes | **Entra** | Unificar pPr de Notas técnicas, Reserva y el párrafo "Desde su generación…". |
| H12 "Se constató:" duplicado / líneas sueltas con sangría de 4 cm | **Entra** (D5) | Se lee mal en casi todo informe. |
| H13 "Marca y modelo" vacío | **Entra** | Una línea (`OrNoInformado`), coherente con Línea/Objeto/Ámbito. |
| H14 "Lic.." y "causa  N°" | **Entra** | Mismo tipo de arreglo que H13 (armado de frases en `ReportValues`). |
| H15 comas sueltas con obligatorios vacíos | Fuera | Inalcanzable: `ValidateForGenerate` lo impide. |
| H16 idioma es-AR | **Entra** | Una propiedad en `styles.xml`; evita el subrayado ortográfico en Word en inglés. |
| H17 orden de esquema en `styles.xml` | **Entra** | Se arregla en el mismo script de plantilla que H16; deja el DOCX validable. |
| H18 nombre de ZIP cortado a mitad de palabra | Fuera | Informativo, legible. |
| H19 capturas extremas diminutas | Fuera | Esperable con el tope de caja; otra discusión de diseño. |
| R1 captura negra sin aviso | Fuera | Ya cubierto por la HU #59 `grabacion-android-robusta`. |
| R2 "la causa Causa N°" | Fuera (ver D5, opción B) | Depende del valor que elige el perito en el catálogo. |
| R3 emoji en LibreOffice | Fuera | Es del visor, no del DOCX. |

---

## Criterios de aceptación

```gherkin
Feature: Informe pericial robusto y prolijo con la plantilla v6

  Background:
    Given un caso con perfil de perito completo, datos obligatorios, textos del informe y captura de IMEI
    And la plantilla v6 regenerada con los cambios de esta HU

  # H02 / H04 — caracteres
  Scenario: Un carácter inválido en XML ya guardado no impide generar
    Given un caso guardado antes de esta HU con U+FFFE en la carátula y U+FFFF en "Resultados"
    When el perito genera el informe
    Then el DOCX se genera sin error
    And esos caracteres no aparecen en ninguna parte del documento
    And el resto del texto queda igual

  Scenario: La API no guarda caracteres inválidos en XML
    When se crea o edita un caso, el perfil del perito o los textos del informe con U+FFFE, U+FFFF o controles C0 (salvo salto de línea)
    Then el valor guardado no los contiene, según la política de D1 (se quitan en silencio o se rechaza con 400 y mensaje claro)

  Scenario: Los controles de dirección no invierten el texto
    Given la carátula "‮evil‬" (con U+202E y U+202C) y un integrante con U+2066..U+2069
    When se genera el informe
    Then en portada, referencia y cuerpo se lee "evil" en el orden escrito
    And los caracteres U+202A-U+202E y U+2066-U+2069 no están en el DOCX

  Scenario: Textos en árabe o hebreo se siguen viendo bien
    Given un nombre de parte escrito en hebreo y otro en árabe, sin controles de dirección
    When se genera el informe
    Then se muestran igual que hoy (dirección implícita del texto)

  Scenario: El editor y el backend siguen limpiando igual
    Given el texto plano heredado de un caso viejo
    When el cliente lo convierte a markdown y el backend hace la misma conversión
    Then los dos resultados son idénticos byte a byte (fixtures F1-F17 más los nuevos de esta HU)

  # H03 — sistema operativo
  Scenario: Caso Android nuevo
    Given un celular Android 11 conectado
    When el perito crea el caso desde la web y carga los textos por defecto
    Then "Operaciones realizadas" dice "sistema operativo Android 11", una sola vez "Android"

  Scenario: Caso Android ya guardado con os_version "Android 11"
    Given un caso creado antes de esta HU cuyo device.os_version es "Android 11"
    When el perito pide los textos por defecto del paso Informe
    Then el token de sistema operativo sale "Android 11"

  Scenario: Caso iOS
    Given un caso con platform "ios" y os_version "17.4"
    Then el sistema operativo sale "iOS 17.4"

  # H01 — firma
  Scenario: La firma no se separa del firmante
    Given un caso cuyo cierre cae en las últimas líneas de una página (como el caso mínimo real del QA)
    When se genera el informe y se abre en Word y en LibreOffice
    Then la línea de firma, el nombre, la profesión con M.P. y el carácter del perito quedan en la misma página
    And "SE DEJA CONSTANCIA" queda en la misma página que la firma

  # H05 / H06 — portada y encabezado
  Scenario: Portada con datos al máximo
    Given una carátula de 500 caracteres, un nombre de organización de 150 y 6 líneas de contacto de 150
    When se genera el informe
    Then la portada cumple la política acordada en D3 (recomendada: entra en una sola página)
    And el membrete de la marca aparece una sola vez, en la portada
    And el cuerpo empieza en la página siguiente a la portada

  Scenario: Encabezado interior con nombre de organización largo
    Given un nombre de organización de 150 caracteres y un número de causa largo
    When se genera el informe
    Then el encabezado interior ocupa como máximo lo acordado en D3 (recomendado: 2 líneas)
    And el nombre completo de la organización sigue en el membrete de la portada

  # H07 — imágenes
  Scenario: Una captura chica no se amplía
    Given una captura de 200x150 px en el anexo y otra en el cuerpo
    When se genera el informe
    Then se muestran a su tamaño según la resolución de D4, sin estirarse a 14 cm
    And se mantienen centradas, con su epígrafe y su proporción

  Scenario: Las capturas normales no cambian
    Given capturas de 1080x2400, 2400x1080 y 1440x3200 px
    When se genera el informe
    Then se ven del mismo tamaño que hoy

  # Menores
  Scenario: Frase de la inspección
    Then el párrafo de la inspección dice "siendo las {hora} horas" (como en Operaciones)

  Scenario: Marca y modelo sin datos
    Given un dispositivo sin fabricante ni modelo
    Then la ficha de identificación dice "Marca y modelo: No informado"

  Scenario: Sin puntuación ni espacios dobles por datos del perfil o de la causa
    Given la profesión del perito "Lic." y un caso sin tipo de causa opcional en alguna frase
    Then no aparece ".." ni dos espacios seguidos por la unión de placeholders

  Scenario: Resultados sin encabezado duplicado
    Given "Resultados" empieza con "Se constató lo siguiente:"
    Then el informe no muestra "Se constató:" seguido de "Se constató lo siguiente:" (según D5)

  Scenario: Sangrías coherentes
    Then Notas técnicas, Reserva y "Desde su generación…" usan la misma sangría que el resto de los párrafos de cuerpo (según D5)

  Scenario: Idioma del documento
    When se abre el informe en un Word configurado en inglés
    Then el texto está marcado como español (es-AR) y no aparece subrayado como error de ortografía en su totalidad

  Scenario: Plantilla válida contra el esquema
    When se valida un informe generado con OpenXmlValidator (Microsoft365)
    Then styles.xml y document.xml tienen 0 errores

  Scenario: Sin regresiones del QA
    When se regeneran los casos c1_tipico, c2r_minimo_real, c3_extremos, c4_capturas y c5 del QA
    Then todo lo listado en "Lo que se ve bien" del QA sigue igual
```

---

## Datos que se registran

No hay campos nuevos. Cambia **qué** se guarda:

| Dato | Obligatorio | Uso / cambio |
|---|---|---|
| Campos de texto del caso, perfil del perito y textos del informe | según hoy | Se guardan sin caracteres inválidos en XML ni controles bidi (alcance y forma según D1). |
| `device.os_version` | no | Casos nuevos según D2 (recomendado: sin cambios en lo que manda el cliente). Los casos existentes no se migran. |

---

## Diseño UX/UI

- **Informe DOCX (backend):** es la "pantalla" de esta HU. Cambios visibles: firma junta, portada en una página,
  encabezado acotado, capturas chicas a tamaño natural, frases corregidas, idioma español. Nada más cambia de diseño.
- **`client/` (web):** sin pantallas nuevas.
  - Si D1 = A (quitar en silencio), `cleanText` (`client/src/lib/report-markdown.ts`) se alinea con el nuevo `Clean`
    del backend para mantener la paridad byte a byte; el perito no ve ningún aviso.
  - Si D1 = B (rechazar), el formulario muestra el error de la API como cualquier otro error de validación
    (mensaje propuesto: "El campo carátula tiene caracteres no permitidos").
  - H03: según D2 (recomendado: sin cambios en el cliente).
- **`agent-ui/` (Electron):** sin cambios.

---

## Fuera de alcance

- H08, H09, H15, H18, H19, R2, R3 (ver tabla de propuesta; D6 para H08/H09).
- R1 (captura negra sin aviso): HU #59 `grabacion-android-robusta`.
- El resto de API-05 y lo demás de `api-hardening` (#63): headers de seguridad, manejo de excepciones, rate-limit,
  403/404. Esta HU solo toma de API-05 lo que rompe o falsea el DOCX (ver D1).
- Regenerar informes ya generados o versiones ya emitidas: los DOCX/ZIP existentes no se tocan.
- Migrar en Mongo `os_version` ni textos de informe ya guardados (el "Android Android 11" que haya quedado escrito en el
  `operaciones_realizadas` de un caso viejo lo corrige el perito a mano, o se recargan los defaults; ver D2).
- Rediseño de la plantilla, nuevos placeholders o secciones; soporte de tablas o de varios niveles de título en el editor.
- Cambiar los límites de largo de la causa o del Branding (salvo que D3 elija esa opción).

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- Un solo saneador de caracteres compartido (generador y, según D1, validación al guardar) para que no diverjan. Criterio
  base: lo que no es `XmlConvert.IsXmlChar` (y los pares surrogate mal formados) se quita; los bidi U+202A-202E y
  U+2066-2069 se quitan. `cleanText` del cliente tiene que seguir siendo el espejo exacto: sumar fixtures nuevos a
  F1-F17.
- H03 en el servidor tiene que ser idempotente: si `OsVersion` ya empieza con "Android"/"iOS" (sin distinguir
  mayúsculas), no se antepone.
- Las frases fijas (H10, H12) viven en `build_plantilla_v4.py` (que necesita la plantilla privada de `docs/`, presente
  en esta máquina); keepNext/keepLines, sangrías, `w:lang es-AR` y el orden de `styles.xml` se pueden hacer en
  `build_plantilla_v6.py`. Después: `--check` de v4 y v6, render con `soffice`, abrir en Word y ajustar los tests que
  comparan el texto pericial con la v4.
- H07: tope de escala por tamaño nativo en `BuildImageParagraphForRel` (`min(1, ...)` respecto del tamaño a la
  resolución de D4). Aplica igual a anexo, identificación y cuerpo.
- Verificación: repetir los casos del QA (generador de consola fuera del repo, conversión a PDF con `soffice`,
  `OpenXmlValidator`) y dejar capturas en el progress del implementador.
- Regla dura de datos (`AGENTS.md`): no modificar casos existentes de la base de desarrollo; las pruebas que escriban
  limpian solo por los `_id` que insertaron y no tocan `Storage:DataDirectory` ajeno.

---

## Dudas para validar con el usuario

> Cada duda tiene una opción **Recomendada** con su porqué.

**D1. Dónde y cómo se sanean los caracteres, y cuáles (H02, H04, parte de API-05).**
Qué caracteres:
- Inválidos en XML 1.0: U+FFFE, U+FFFF, surrogates sueltos y C0 salvo `\n` (el tab sigue pasando a espacio).
- Controles bidi de incrustación/override/aislamiento: U+202A-202E y U+2066-2069.
- Se **conservan** U+200E/U+200F (marcas LRM/RLM, no invierten texto) y U+007F-U+009F (válidos en XML; el QA no vio
  problema). Si preferís, se suman a la lista a quitar.

Dónde:
- A) **Generador siempre (cubre casos ya guardados) + API al guardar quitándolos en silencio** (caso, perfil, textos del
  informe, Branding), con el mismo saneador; `cleanText` del cliente se alinea.
- B) Generador siempre + API que **rechaza con 400** si vienen esos caracteres.
- C) Solo el generador; la API sigue guardando tal cual (API-05 queda para #63).
- **Recomendada: A.** El generador tiene que sanear sí o sí, porque ya puede haber casos guardados con estos
  caracteres. Al guardar, quitarlos en silencio evita que el perito quede trabado por un carácter invisible que vino
  pegado de un PDF o de un sistema judicial (no hay forma razonable de que lo encuentre y lo borre); rechazar (B) es más
  estricto, pero el mensaje sería incomprensible para él. C deja datos sucios en Mongo y en la UI. Esto cubre la parte
  "caracteres de control/RTL" de API-05; propongo que #63 la dé por resuelta acá y no la duplique.

**D2. Arreglo de "Android Android 11" (H03): cliente, servidor o ambos, y datos existentes.**
- A) **Solo el servidor:** `OsLabel` no antepone el prefijo si `os_version` ya lo trae. El cliente sigue mandando
  "Android 11". Casos viejos y nuevos quedan bien sin migrar.
- B) Ambos: el cliente pasa a mandar solo el número (como ya hace iOS) y el servidor tolera los dos formatos. Hay que
  ajustar también `CaseDetailContent.tsx:41`, que hoy muestra `os_version` tal cual.
- C) Solo el cliente: los casos ya guardados siguen saliendo mal.
- **Recomendada: A.** Arregla todos los casos (viejos y nuevos) con un cambio idempotente en un solo lugar y sin tocar el
  contrato ni el cliente. B unifica el formato entre Android e iOS, pero toca dos apps y la ficha del caso sin beneficio
  visible para el perito. Sobre los casos que ya tienen "Android Android 11" escrito en `operaciones_realizadas`
  guardado: no se migra texto del perito (es su escrito); lo corrige a mano. ¿Te sirve así o preferís un script
  puntual para la base de desarrollo?

**D3. Política de portada y encabezado con datos largos (H05, H06).**
- A) **Portada siempre en una página:** la carátula de la ficha de portada achica la fuente por tramos según el largo
  (p. ej. normal hasta ~200 caracteres, un punto menos hasta ~350, dos menos hasta 500) y el membrete de portada junta
  las líneas de contacto en a lo sumo 2 líneas ("·" entre medio). En el encabezado interior, el nombre de la
  organización se recorta con "…" para que ocupe como máximo 2 líneas (el nombre completo sigue en la portada).
- B) Permitir que la portada ocupe 2 páginas, pero sin repetir el membrete en la segunda y con el cuerpo arrancando en
  la página siguiente; el encabezado crece lo que haga falta.
- C) Bajar los límites (carátula a ~300, contacto a 4 líneas de 100, organización a 80) para que todo entre siempre.
- **Recomendada: A.** Una portada de dos páginas o un encabezado de 4 líneas se ven como error en un escrito; A lo
  resuelve con los límites actuales y sin rechazar datos que ya están guardados. C rompe casos y marcas existentes que
  hoy validan. Los tramos de fuente exactos los fija la SDD con renders de prueba (Word y LibreOffice).

**D4. Tamaño máximo de una imagen chica (H07).**
- A) **No ampliar más allá de 96 dpi** (el tamaño "100 %" de Word): una de 200x150 px queda de ~5,3 x 4 cm.
- B) No ampliar más allá de 150 dpi (más chica: ~3,4 x 2,5 cm, más nítida en impresión).
- C) Sin tope de resolución, pero con un ancho mínimo fijo (p. ej. 6 cm).
- **Recomendada: A.** Es lo que haría Word al insertar la imagen, no pixela a la vista normal y no cambia nada para las
  capturas de celular reales (1080 px o más ya superan los 14 cm a 96 dpi). B achica de más imágenes medianas.

**D5. "Se constató:" fijo, líneas sueltas con sangría de 4 cm y sangrías dispares (H11, H12).**
- A) **Sacar el "Se constató:" fijo de la plantilla** (el perito escribe el encabezado que quiera en Resultados) y unificar
  la sangría de Notas técnicas, Reserva y "Desde su generación…" con la de los párrafos de cuerpo (primera línea de 4 cm,
  como el resto del escrito).
- B) Dejar el "Se constató:" fijo y que el backend no lo repita si el texto de Resultados ya empieza con "Se constató".
- C) No tocar el "Se constató:"; solo unificar sangrías.
- **Recomendada: A.** Es más simple y predecible que una regla de texto (B), y el modelo del usuario no depende de esa
  frase. Pregunta adicional: la sangría de primera línea de 4 cm en respuestas de una línea ("Ninguna.") ¿la querés
  así por estilo forense o preferís una sangría menor (p. ej. 1,25 cm) en todo el cuerpo? Recomiendo dejar 4 cm para no
  cambiar el aspecto general del escrito. (Si te molesta R2, "la causa Causa N°", se puede sumar acá cambiando la frase
  a "en el marco de los autos …"; recomiendo dejarlo fuera.)

**D6. Títulos y tablas markdown (H08, H09).**
- A) **Fuera de esta HU:** el editor solo produce un nivel de subtítulo y convierte las tablas pegadas en filas con
  aviso, así que ningún perito llega a estos casos desde la UI.
- B) Validar al guardar: la API rechaza markdown con títulos `#`/`##` o tablas GFM (solo afecta a clientes por API).
- C) Soportar títulos de 3 niveles con tamaños distintos y tablas reales en el DOCX (y en el editor).
- **Recomendada: A.** Lo que el perito ve en el editor ya coincide con lo que sale en el informe. B agrega rechazos sin un
  usuario real que los dispare; C es una feature nueva del editor que merece su propia HU si se necesita.

## Validación del usuario (2026-10-13)
Todas las dudas con la opción recomendada (D1 A, D2 A **sin script** de migración, D3 A, D4 A, D5 A **manteniendo 4 cm** de sangría también en respuestas de una línea, sin cambiar "la causa Causa N°", D6 A).
