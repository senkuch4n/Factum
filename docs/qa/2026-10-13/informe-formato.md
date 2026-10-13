# QA de formato: informe pericial DOCX (plantilla v6), 2026-10-13

## Resumen

- **Parte A (sintético):** generé 11 DOCX con `ReportService` (constructor público, plantilla v6 real, zona America/Argentina/Buenos_Aires, textos por defecto reales). Usé un proyecto de consola temporal fuera del repo y una carpeta temporal por caso, sin Mongo ni Storage. Cada uno se convirtió a PDF con `soffice` y se revisó página por página como imagen. También pasé un validador XML (python `zipfile` + `ElementTree`, control de caracteres ilegales) y `OpenXmlValidator` (Microsoft365).
- **Parte B (real):** apareció `qa/informe/caso1_QA-ROBUSTEZ-001/informe_pericial_QA_ROBUSTEZ_001_QA_ROBUSTEZ_Titular.docx` (8 páginas). Le hice la misma revisión. Mostró un bug real de contrato client/server ("Android Android 11") que el sintético no podía ver.
- **Veredicto:** el informe es sólido en lo estructural:
  - XML bien formado en todos los casos.
  - Imágenes sin deformar.
  - Numeración de listas y de figuras correcta.
  - Encabezado y pie consistentes.
  - Fechas bien convertidas a la zona local.
  - HTML y markdown escapado salen literales.
- **Hallazgos importantes:**
  1. **El bloque de firma se puede partir entre dos páginas.**
  2. **U+FFFE/U+FFFF en cualquier campo (aceptados por la API) hacen fallar la generación con excepción.**
  3. **"sistema operativo Android Android 11" en todo informe Android real.**
  4. **Los caracteres de control bidi (U+202E) se pasan sin filtrar y permiten mostrar texto invertido (spoofing) en un documento pericial.**

Casos generados:

| Caso | Datos | Págs |
|---|---|---|
| c1_tipico | caso completo, 3 integrantes, Sala, marca con logo e isotipo, capturas IMEI y nombre, 3 capturas de anexo, imagen en el cuerpo, textos por defecto | 10 |
| c2r_minimo_real | solo obligatorios para generar (opcionales vacíos, sin marca, sin marca/modelo de dispositivo) | 6 |
| c2_minimo / c2b_vacio_total | defensivo: obligatorios vacíos (inalcanzable por API, `ValidateForGenerate` lo frena) | 5 |
| c3_extremos | carátula de 500 caracteres con emoji, RTL, U+202E, `<b>x</b>`, `<script>`, comillas tipográficas; parte de 300 caracteres; palabra de 150 caracteres sin espacios; 11 integrantes; controles U+0001/0007/000B/007F/0085; tab; marca de 150 caracteres + 6 líneas de contacto de 150; markdown completo (h1-h3, listas anidadas de 4 niveles, numeradas anidadas, reinicio, cita, tabla, código, `---`, links, emoji) | 11 |
| c3b_textoplano / c3c_html_crudo | `Formato=null` (texto plano heredado) y HTML crudo en markdown (defensa del renderer) | 5 / 5 |
| c3x_* | U+FFFE en carátula, U+D800 suelto, U+FFFF en el texto markdown | excepción |
| c4_capturas | 34 capturas de anexo (1080x2400, 2400x1080, 1000x1000, 200x150, 4000x3000, 1080x6000, 3000x300, 720x1600, 1440x3200, 150x900; PNG y JPG) + 2 de identificación + 5 en el cuerpo | 28 |
| c5 / c5b / c5c | intervención 29/02/2024 + creación 2025-01-01 02:30 UTC (= 31/12/2024 23:30 AR); fecha inválida 2025-02-29; fecha en formato dd/MM | 8 c/u |

## Hallazgos

Capturas en `qa/informe-formato/`. Las páginas son las del PDF de LibreOffice; en Word pueden variar ±1.

| ID | Gravedad | Dato de entrada | Pág. | Qué se ve mal | Captura | Archivo:línea probable |
|---|---|---|---|---|---|---|
| H01 | **Alta** | Caso mínimo real (c2r). Ocurre cuando el cierre cae cerca del pie | 5-6 | **La línea de firma queda sola al pie de la pág. 5. Nombre, profesión, M.P. y carácter del perito pasan a la pág. 6.** Ningún párrafo del bloque "SE DEJA CONSTANCIA / línea / nombre / profesión / carácter / isotipo" tiene `keepNext`/`keepLines`. Una firma separada del firmante es un defecto clásico en un escrito judicial. | `H01_firma_partida-5.png`, `-6.png` | Plantilla `Templates/plantilla_informe_v6.docx` (párrafo ~134 en adelante; se genera en `ops/plantilla/build_plantilla_v6.py`). Agregar keepNext a la línea de firma y a las líneas del firmante (o meter el bloque en una tabla de una celda con "no dividir fila"). |
| H02 | **Alta** | Carátula `"A￾B"`, o U+FFFF en un texto del informe | — | **La generación falla** con `ArgumentException: hexadecimal value 0xFFFE, is an invalid character` en `doc.Save()`. `System.Text.Json` acepta `"￾"` (lo verifiqué), `CaseValidation` solo hace trim, así que entra por la API, se guarda en Mongo y deja el caso sin poder generar el informe. Un surrogate suelto (`\uD800`) también rompe el generador, pero STJ lo rechaza antes (400), así que por API no llega. | `H02_caracteres_invalidos_excepcion.txt` | `Services/Reports/ReportValues.cs:18-29` (`Clean` solo filtra `< 0x20`; tendría que filtrar también U+FFFE/U+FFFF y surrogates sueltos, o usar `XmlConvert.IsXmlChar`). Conviene sanear también al guardar en `Services/Cases/CaseValidation.cs:46`. |
| H03 | **Media** | Informe real con dispositivo Android | 4 (real) | **"sistema operativo Android Android 11"** en Operaciones realizadas. El cliente manda `os_version: "Android 11"` y el servidor arma `OsLabel = "Android " + OsVersion`. Sale en todo informe Android que use el texto por defecto. | `R_real_android_dup_y_leadin-4.png` | Contrato: `client/src/app/dashboard/page.tsx:413` (manda `Android ${android_version}`) contra `server/src/Factum.Backend/Models/DeviceInfo.cs:18-20`. Elegir un solo lado: o el cliente manda solo el número, o `OsLabel` no antepone el prefijo si ya está. |
| H04 | **Media** | Carátula con `‮evil‬` | 1-2 | **El texto se ve invertido** ("live") en portada y en referencia. U+202A-202E y U+2066-2069 pasan sin filtro: un tercero puede hacer que lo que se lee no sea lo que dice el archivo, en un documento con valor probatorio. | `H05_bidi_override-1.png` (c3b; el texto también está en c3) | `Services/Reports/ReportValues.cs:18-29` (`Clean`) |
| H05 | Media | Carátula de 500 caracteres + marca larga (nombre de 150, 6 líneas de contacto de 150) | 1-3 | **La portada ocupa 2 páginas.** El membrete del pie de portada se repite en la pág. 2 y el cuerpo arranca en la pág. 3. Con marca normal, la carátula de 500 sí entra (c3b). | `H03_portada_desborda-01..03.png` | Plantilla v6, sección de portada/footer2. Los límites `BrandingService.MaxContactLines=6`/`MaxContactLineLength=150` sumados a `CaseValidation.MaxLong=500` no entran en una portada A4. |
| H06 | Media | Nombre de organización de 150 caracteres + nro. de causa largo | todas | **El encabezado interior crece a 4 líneas** (nombre de la org. a la derecha) y empuja el cuerpo. No se trunca ni se reduce la fuente. | `H03_portada_desborda-03.png` (arriba) | Header de la plantilla v6 + `ReportValues.cs:~120` (`{ORGANIZACION}` sin recorte) |
| H07 | Baja-Media | Captura chica (200x150) | 13, 18, 28 | **Se amplía a 14 cm de ancho y queda pixelada.** El fit escala hacia arriba sin tope de resolución nativa. En pericia conviene no ampliar más allá de ~96-150 dpi. | `H06_captura_chica_ampliada-13.png` | `Services/Reports/ReportService.cs:949-951` (`BuildImageParagraphForRel`, falta `min(1, …)` respecto al tamaño nativo) |
| H08 | Baja-Media | Markdown `#`, `##`, `###` | 4-5 | **Los tres niveles de título se ven iguales** (negrita, mismo tamaño que el cuerpo). Solo se distinguen por el espaciado. | `H07_md_titulos_tabla-04.png` | `Services/Reports/ReportMarkdownRenderer.cs:240-250` (`RenderHeading` ignora `block.Level`) |
| H09 | Baja | Markdown con tabla GFM | 5 | **La tabla sale como texto literal** `\| Col A \| Col B \| \|---\|---\| …`, con sangría de primera línea. Es esperado (dialecto sin GFM), pero un perito que pegue una tabla obtiene basura. Validar al guardar o advertir en el editor. | `H07_md_titulos_tabla-05.png` | `Services/Reports/ReportMarkdown.cs` (pipeline sin tablas) |
| H10 | Baja | Plantilla | 3 | **Frase sin artículo:** "En fecha …, siendo horas 10:30, se procedió…". En Operaciones dice "siendo las 10:30 horas". | `H08_siendo_horas-03.png` | `ops/plantilla/build_plantilla_v4.py:310` (heredado en v6) |
| H11 | Baja | Plantilla | 6, 8 | **Sangrías inconsistentes entre secciones:** los párrafos llevan primera línea de 4 cm (`firstLine=2268`); Notas técnicas lleva sangría izquierda de 0,6 cm en bloque (`left=360`); Reserva y "Desde su generación…" no llevan nada. | `H09_sangrias-08.png`, `H10_…-06.png` | pPr de los párrafos `{descripcionNotasTecnicas}`/`{descripcionReserva}` en la plantilla v6 |
| H12 | Baja | Resultados que empiezan con "Se constató lo siguiente:" | 6 | La plantilla antepone un "Se constató:" fijo, así que sale duplicado ("Se constató: / Se constató lo siguiente:"). Respuestas de una sola línea ("Ninguna.", "a") quedan flotando con 4 cm de sangría. | `H10_se_constato_y_nombre_zip-06.png` | Plantilla v6, párrafo previo a `{descripcionResultados}` |
| H13 | Baja | Dispositivo sin fabricante ni modelo | 3 | "Marca y modelo:" queda **vacío**, mientras Línea/Objeto/Ámbito dicen "No informado". | `H11_marca_modelo_vacio-3.png` | `ReportValues.cs:115` (`{marcaModeloDispositivo}` sin `OrNoInformado`) |
| H14 | Baja | Profesión del perito que termina en punto ("Lic.") | 3 | "Ana, M.P. 1, Lic.." con punto doble. Pasa lo mismo con el `{nombreTribunal}`/`{tipoCausa}` vacío: "causa  N°" con doble espacio. | `H14_vacio_total_comas-2.png` | Plantilla ("…{profesionPerito}."); ver `ReportValues.cs:84-131` |
| H15 | Baja (inalcanzable por API) | Caso con obligatorios vacíos (c2b) | 2-3 | Etiquetas huérfanas y comas sueltas: ", M.P. , , en mi carácter de , …", "seguida contra , que tramita ante , …". `ValidateForGenerate` lo impide, así que solo importa si alguien llama al generador sin validar. | `H14_vacio_total_comas-2.png`, `-3.png` | Plantilla + `ReportValues.Placeholders` |
| H16 | Baja | Cualquiera | — | **Idioma:** `styles.xml` no define `w:lang` por defecto y 24 párrafos de la plantilla marcan `en-US`, así que la ortografía de Word depende del idioma de la instalación. En un Word en inglés todo el informe sale subrayado. | — | Plantilla v6 (`rPrDefault` sin `<w:lang w:val="es-AR"/>`) |
| H17 | Baja | Cualquiera | — | `OpenXmlValidator`: 29 errores, todos en `styles.xml` (`uiPriority` después de `qFormat`, orden de esquema). Ya vienen en la plantilla; `document.xml` generado: 0 errores. Word lo tolera, pero un validador estricto lo marca. | — | Plantilla v6 `styles.xml` (estilo "Intense Reference" y siguientes) |
| H18 | Info | Nombre de archivo largo en la tabla de hashes | 5-6 | `evidencia_CCC_12345_2026_Marí` / `a_José_Fernández.zip` se corta a mitad de palabra (no hay puntos de quiebre). Legible, pero feo. | `H10_se_constato_y_nombre_zip-06.png` | `ReportService.FillHashTable` (se podría insertar U+200B después de `_`) |
| H19 | Info | Capturas muy altas (1080x6000) o muy anchas (3000x300) | 8, 14, 15 | Se respeta la proporción, pero quedan de ~1,9 cm de ancho o ~1 cm de alto, ilegibles. Es esperable por el tope de 14x10,5 cm. | `H13_captura_muy_alta-08.png` | `ReportService.cs:277-281` (cajas fijas) |
| R1 | Info (no es de formato) | Informe real | 8 (real) | La única captura del anexo (`screenshot_20261013_110020_531.png`, 720x1600) es **toda negra** (píxeles 0, alfa 255). Probablemente pantalla apagada o FLAG_SECURE. Se incluye sin ninguna advertencia. Se lo paso al QA E2E. | `R_real_captura_negra-8.png` | Agente/captura, no el generador |
| R2 | Info | Informe real, tipo de causa "Causa" | 2 (real) | "en el marco de la causa Causa N° …": la plantilla dice "la causa {tipoCausa}". Con "Expediente" se lee "la causa Expediente N°", aceptable. | `R_real_emoji_causa_causa-2.png` | Plantilla v6 |
| R3 | Info | Emoji 📱 en carátula (real) | 1-2 | En LibreOffice sale como una caja negra por falta de fuente. Word en Windows usa Segoe UI Emoji y debería verse bien. Es del visor, no del DOCX. | `R_real_emoji_causa_causa-2.png` | — |

## Lo que se ve bien

- **XML válido** en los 12 DOCX: todas las partes parsean y no hay caracteres de control ilegales. U+0001/0007/000B/tab/NUL se sanean bien; U+007F/U+0085 quedan, pero son XML 1.0 válidos.
- **Imágenes:** ninguna deformada (el círculo de prueba se ve redondo en todos los tamaños) ni fuera de página. Con 34 capturas quedan 2 por página, sin separar figura de epígrafe. Las capturas del cuerpo reutilizan la numeración del anexo ("Figura 6 – Vertical alta (captura_06.png)"). Identificación IMEI/nombre en 7x10,5 cm.
- **Listas:** viñetas anidadas hasta 4 niveles con glifos distintos. Numeradas anidadas (1., 1., 2.) correctas, y una lista nueva después de un párrafo reinicia en 1. "5." pegada a una lista anterior continúa la numeración, que es lo que pide CommonMark. Los textos por defecto omiten las líneas con cantidad 0.
- **Markdown:** negrita, itálica, ambas, `<u>`, código inline y en bloque (gris monoespaciado), cita con borde, salto duro y salto suave, links con la URL visible entre paréntesis. `\<b\>` y `\<script\>` escapados salen literales. El HTML crudo (inalcanzable por validación) también sale literal, sin inyectar nada.
- **Texto plano heredado** (`Formato=null`): un párrafo por línea, conserva las líneas vacías y los asteriscos literales.
- **Caracteres:** ñ, acentos, «», comillas tipográficas, raya y guion, emoji (incluye ZWJ y banderas) y árabe/hebreo se ven bien en el cuerpo. Las palabras sin espacios de 150+ caracteres se cortan dentro del margen, sin desbordar. La conjunción "e Ignacio Íñiguez" sale correcta.
- **Tablas:** la de hashes repite el encabezado al cambiar de página, no se sale del margen y la fila del ZIP se tiñe con el acento de la marca.
- **Encabezado y pie:** iguales en todas las páginas interiores. La portada no lleva encabezado. "Página X de Y" correcto. La atribución "Realizado con Factum" está en todas.
- **Fuentes:** Arial en el cuerpo, Century Gothic en títulos y Courier New en hashes/código. No hay otras mezcladas.
- **Fechas:** 29/02/2024 correcto. Una creación del 2025-01-01 02:30 UTC sale como 31/12/2024 23:30 (zona AR). Formato dd/MM/yyyy y HH:mm consistente en portada, referencia, elementos y operaciones. Una fecha inválida sale tal cual, pero la API no la deja pasar.
- **Opcionales vacíos** (caso real mínimo): Objeto, Notas técnicas y Reserva desaparecen sin dejar título suelto. Organismo, Sala e Integrantes vacíos no dejan comas ni "con la integración de". No aparece ningún "null"/"undefined" que no venga del dato del usuario.
- **Títulos de sección** con `keepNext`: no encontré títulos huérfanos al pie.
- **Informe real:** misma estructura y calidad que el sintético. Con marca propia (logo, isotipo, color violeta) se aplica bien en portada, filetes y tinte del ZIP.

## Evidencia y limpieza

- `qa/informe-formato/`: PNG por hallazgo (`H01…`, `R_…`), los PDF de los casos con hallazgos (`c1_tipico`, `c2r_minimo_real`, `c2b_vacio_total`, `c3_extremos`, `c3b_textoplano`, `c4_capturas`, `c5_fechas_bisiesto_finanio`, `real_informe.pdf`) y `H02_caracteres_invalidos_excepcion.txt`.
- Borré el proyecto generador temporal, las imágenes de prueba, los DOCX, el ZIP intermedio y los renders. No toqué el repo (git status sin cambios nuevos), Mongo ni Storage.
