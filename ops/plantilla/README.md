# Plantillas del informe pericial (v4, v5 y v6)

La cadena es **plantilla del usuario (`docs/`, no versionada) → v4 → v6**:

| Plantilla | Script | Qué es |
|---|---|---|
| `Templates/plantilla_informe_v4.docx` | `build_plantilla_v4.py` | Contenido pericial, placeholders y bloques, sin datos reales. Es la entrada de la v6 y la línea de base de los tests. El backend ya no la usa en runtime. |
| `Templates/plantilla_informe_v6.docx` | `build_plantilla_v6.py` | La v4 con el diseño "Filete" y la paleta de Factum. **Es la que usa `ReportService`.** |
| (no versionada) v5 | `build_plantilla_v5.py` | El diseño anterior (portada con franja, banda interior). El `.docx` se borró del repo, pero el script queda sin cambios: lo importa el v6 y reproduce la v5 desde la v4 byte a byte si alguna vez hace falta. |

La v6 se reconstruye **solo desde la v4 versionada**: no necesita la plantilla
privada de `docs/`.

## v6: qué agrega el script

Especificación: `Refactorizaciones/informe-diseno-v6.md` (secciones 5 y 8.1).
Reutiliza por import los helpers de `build_plantilla_v4.py` y
`build_plantilla_v5.py` (con `sys.dont_write_bytecode`, para no dejar
`__pycache__`).

- **Sin formas flotantes** en ninguna parte: encabezado, pie, portada, filetes
  y fichas son párrafos o tablas de flujo.
- **Portada** como sección propia (`sectPr` en el último párrafo de la
  portada): un filete verde de ~3 cm, "Informe pericial / técnico
  informático" en Century Gothic 34 pt, el subtítulo y una ficha
  etiqueta/valor (causa, carátula, perito, fecha) con líneas grises.
  `header2.xml` queda vacío. `footer2.xml` lleva el bloque `MEMBRETE` con dos
  variantes: `MEMBRETE_CON_LOGO` (tabla logo 4.5 × 2.5 | nombre y contacto) y
  `MEMBRETE_SIN_LOGO` (nombre y contacto en texto).
- **Interior:** `header1.xml` es una tabla de texto chico gris ("Informe
  pericial técnico informático · {tipoCausa} N° {numeroCausa}" a la izquierda,
  `{ORGANIZACION}` a la derecha) con una línea fina debajo. `footer1.xml` es
  una línea fina con el slot `{ATRIBUCION_FACTUM}` a la izquierda (lo llena el
  backend con el sello y "Realizado con Factum") y "Página N de M" a la
  derecha (campos `PAGE`/`NUMPAGES`, que cuentan la portada).
- **Títulos:** cada título numerado lleva arriba un párrafo vacío con la
  numeración romana (Century Gothic 20 pt verde, `lvlText "%1"`, sin punto) y
  debajo un filete verde de ~1 cm (borde superior de un párrafo vacío). Los
  títulos van en Arial negrita, mayúscula por formato, tinta e interletrado,
  todos en el mismo margen. Los separadores vacíos de la v4 antes de los
  títulos pasan a párrafos mínimos.
- "Referencia de la actuación", "Identificación" y "Elementos ofrecidos" son
  **fichas** a todo el ancho (etiqueta / valor, grilla 2490/6157, líneas
  grises). La intro de Elementos queda como párrafo de cuerpo.
- **Tabla de hashes** a todo el ancho (3090/5557), encabezado gris sin relleno
  con una línea verde debajo; el tinte de la fila del ZIP lo pone el backend.
- Título del escrito sin subrayado ni negrita; anexo con el aspecto de un
  título; cierre con un filete centrado y `{#ISOTIPO}{ISOTIPO_ORGANIZACION:2x2}{/ISOTIPO}`.
- Cuerpo en Arial 11 pt gris `3D444C` (estilo Normal y `docDefaults`).
  `fontTable.xml` declara Century Gothic y Arial.
- **Centinela:** la v6 lleva el verde `2F6F12` (default del primario) en los
  filetes, los números (`numbering.xml`) y la línea de la tabla. El backend lo
  reemplaza al generar por `Branding:PrimaryColor`. El tinte `E8F3DF` no va en
  la plantilla. Si la v4 de entrada ya trae alguno de los dos valores, el
  script aborta con `FALLA: centinela-en-origen`.

Igual que los anteriores: edita el XML como texto, ubica cada párrafo por
`w14:paraId` con una huella no sensible (si no coincide, `FALLA: ancla
<paraId>`), los paraIds nuevos son deterministas (semilla `v6:`) y el ZIP
también (la misma v4 da el mismo SHA-256 de la v6).

### Uso

```bash
# Construir (y verificar al final)
python3 ops/plantilla/build_plantilla_v6.py \
  server/src/Factum.Backend/Templates/plantilla_informe_v4.docx \
  server/src/Factum.Backend/Templates/plantilla_informe_v6.docx

# Solo verificar
python3 ops/plantilla/build_plantilla_v6.py --check \
  server/src/Factum.Backend/Templates/plantilla_informe_v4.docx \
  server/src/Factum.Backend/Templates/plantilla_informe_v6.docx
```

Imprime solo `OK` o líneas `FALLA:<tipo>`. `--check` verifica: placeholders y
bloques por parte (cuerpo, `header1`, `footer1`, `footer2`; `header2` vacío),
las dos secciones con su header y footer (la segunda con `top 1418` y
`header 680`), que ninguna parte tenga `wp:anchor`, `wps:wsp` ni `v:shape`, el
centinela en los 15 filetes, la numeración y las 2 líneas de la tabla (y en
ningún header ni footer), que `E8F3DF` no esté, 11 párrafos de número sin
texto y ningún título con `numPr`, el título del escrito sin subrayado, el
encabezado de la tabla sin `w:shd`, estilos con Arial y `3D444C`, que no quede
Times New Roman, XML bien formado, `w14:paraId` únicos, sin `w:author`, autor
`Factum` en `core.xml` y `PAGE`/`NUMPAGES` en `footer1`.

### Render

```bash
soffice --headless --convert-to pdf --outdir /tmp/render \
  server/src/Factum.Backend/Templates/plantilla_informe_v6.docx
```

Para ver informes completos con datos ficticios, el test
`ReportDesignTests.Muestras_SiHayCarpetaDeRender` genera cuatro DOCX (sin
Branding; nombre y contacto sin logo; logo, nombre y contacto con la paleta de
Factum; logo e isotipo con colores ficticios) si se le pasa una carpeta fuera
del repo:

```bash
FACTUM_RENDER_DIR=/tmp/render dotnet test server/tests/Factum.Backend.Tests \
  --filter "FullyQualifiedName~Muestras"
# opcional, para probar un logo o colores locales sin versionarlos:
#   FACTUM_RENDER_LOGO=<ruta> FACTUM_RENDER_PRIMARY=<RRGGBB> FACTUM_RENDER_ACCENT=<RRGGBB>
```

Después de regenerar la v6: `--check`, mirar el render, abrirla en Word y
correr `dotnet test` (los tests comparan el texto pericial contra la v4).

### v6: arreglos de #62 (informe-formato-fixes)

Ver `Refactorizaciones/informe-formato-fixes.md` §7. En `build_plantilla_v4.py`
(frases fijas y sangrías, se heredan en la v6):

- **H10:** "En fecha {fechaInspeccion}, siendo las {horaInspeccion} horas, se
  procedió…" (antes "siendo horas {horaInspeccion}").
- **H12:** se elimina el párrafo fijo "Se constató:" (`543E58FA`, con huella).
- **H11:** Notas técnicas (`7290E48F`), Reserva (`516E02D3`) y "Desde su
  generación…" (`05004875`, conserva sus runs) toman el pPr normalizado
  (primera línea de 4 cm, `firstLine=2268`) como el resto de las descripciones.

En `build_plantilla_v6.py`:

- **H01, firma indivisible:** `FIRMA_BLOCK` (los 7 párrafos de "SE DEJA
  CONSTANCIA…" al carácter del perito) con `keepNext`; `keepLines` en los que
  pueden envolver (`4FC2AE2A`, `3EAB1E3F`, `6AA98C58`, `4F0EBB3D`), y
  `keepNext` en el filete del cierre (el isotipo queda con el bloque).
- **H16, idioma:** `rPrDefault` con `<w:lang w:val="es-AR" w:eastAsia="en-US"
  w:bidi="ar-SA"/>` y todo `w:lang w:val="en-US"` de `document.xml` y
  `styles.xml` pasa a `es-AR`.
- **H17, orden de esquema de `styles.xml`:** los hijos de cada `<w:style>` se
  reordenan según `CT_Style` (solo hermanos, sin cambiar valores). Un hijo
  desconocido aborta con `FALLA: ancla styles`. OpenXmlValidator (Microsoft
  365) pasa de 29 errores a 0.
- `--check` agrega: (a) firma con `keepNext`/`keepLines`; (b) ningún `en-US` y
  `rPrDefault` en es-AR; (c) estilos en orden de esquema; (d) las anclas de
  portada que usa `ReportService` (B-R2c): el espaciador `w:line="1800"`
  exacto, el subtítulo con `w:after="1650"` y `{caratula}` a `sz 22` en la
  ficha; (e) `{CONTACTO}` en `footer2` dentro de `MEMBRETE`; (f) que no quede
  "Se constató:" ni "siendo horas".

La portada en una página (densidad por niveles) y el recorte del encabezado
interior no tocan la plantilla: los aplica `ReportService` al generar
(`ReportCoverLayout`). Los renders de regresión de los 11 informes del QA
están en `ReportQaRegressionTests` (`FACTUM_RENDER_DIR` fuera del repo, y
`FACTUM_SOFFICE=1` para convertir a PDF y verificar portada y firma):

```bash
FACTUM_RENDER_DIR=/tmp/render FACTUM_SOFFICE=1 dotnet test server/tests/Factum.Backend.Tests \
  --filter "FullyQualifiedName~ReportQaRegression|FullyQualifiedName~Muestras"
```

### sellado-tiempo (bloque `selloTiempo`)

Ver `Refactorizaciones/sellado-tiempo.md` §8. En `build_plantilla_v4.py` (se
hereda en la v6, que no cambia):

- **Dónde:** inmediatamente después de `</w:tbl>` de la tabla de hashes, antes
  del título "CADENA DE CUSTODIA DIGITAL": `{#selloTiempo}`, un párrafo vacío
  que separa el texto de la tabla, `{descripcionSelloTiempo}` y
  `{/selloTiempo}`. El párrafo del placeholder usa el pPr normalizado y el rPr
  del de aseguramiento (`2D02EAD7`); los `paraId` son deterministas
  (`a5:sello-espacio`, `a5:sello`, `marker:sello-open`, `marker:sello-close`).
- **Qué lo llena:** `ReportService` (B-R2 con `conditions["selloTiempo"]` y
  B-R4 con `ReportTimestampTexts.Block`, siempre texto plano). Con el sellado
  desactivado (o en el flujo viejo) el bloque se borra entero y el informe
  queda idéntico al de antes. Las líneas "Sello de tiempo: …" de la tabla no
  van en la plantilla: las agrega B-R5 (`FillHashTable`) por código.
- `BODY_PLACEHOLDERS` suma `{descripcionSelloTiempo}` y `BODY_BLOCKS` suma
  `selloTiempo`; el `--check` de la v6 los hereda.

### metadatos-dispositivo (bloque `fichaTecnica`)

Ver `Refactorizaciones/metadatos-dispositivo.md` §9.

- **v4, dónde:** inmediatamente después de la fila "Titular" (`2E1C810A`) y antes
  de `{capturasImeiModelo}` (`07342EEC`), dentro de "Elementos ofrecidos":
  `{#fichaTecnica}`, la aclaración de origen ("Datos técnicos obtenidos
  automáticamente por Factum mediante consultas de solo lectura al dispositivo
  el {fechaFichaTecnica} a las {horaFichaTecnica} horas:", pPr normalizado), las
  10 filas `**Etiqueta:** {fichaX}` (`FICHA_ROWS`, mismo pPr/rPr que "Titular"),
  `{descripcionRelojDispositivo}` (pPr normalizado) y `{/fichaTecnica}`. Los
  `paraId` son deterministas (`a6:ficha-intro`, `a6:ficha-<clave>`,
  `a6:ficha-reloj`, `marker:ficha-open`, `marker:ficha-close`).
- `BODY_PLACEHOLDERS` suma los 13 placeholders (`{fechaFichaTecnica}`,
  `{horaFichaTecnica}`, `{fichaSistemaOperativo}`, `{fichaCompilacion}`,
  `{fichaParcheSeguridad}`, `{fichaNumeroSerie}`, `{fichaIdentificadorConexion}`,
  `{fichaKernel}`, `{fichaOperadorSim}`, `{fichaEstadoArranque}`,
  `{fichaZonaHoraria}`, `{fichaWifi}`, `{descripcionRelojDispositivo}`) y
  `BODY_BLOCKS` suma `fichaTecnica`. La huella de v5 (`BLOCK_ELEMENTOS`
  consecutivo) no cambia: el bloque va después de la última fila.
- **v6:** las 10 filas se convierten en una **segunda ficha** etiqueta/valor con
  el mismo `data_card` (grilla 2490/6157, `CARD_BORDERS`, `split_row`). Se ubican
  por el placeholder que contienen (`FICHA_ROWS`), no por `paraId`; tienen que
  ser consecutivas y estar entre los marcadores (si no, `FALLA: ancla ficha`).
  La aclaración y el párrafo del reloj quedan como cuerpo. `--check` suma
  `FALLA:ficha`: una sola tabla entre los marcadores, con las 10 filas. La
  etiqueta "Identificador de conexión:" puede partirse en dos renglones en la
  columna de 2490 dxa (aceptado).
- **Qué lo llena:** `ReportService` (B-R2 con `conditions["fichaTecnica"] =
  cas.DeviceDetails is not null`, B-R4 con `ReportDeviceTexts.ClockBlock` en
  texto plano y B-R6 con los 12 de una línea). Sin ficha (casos anteriores o
  Tatana viejo) el bloque se borra entero, tabla incluida, y el informe queda
  idéntico al de antes.

### grabacion-sesion-completa (bloque `registroAudiovisual`)

Ver `Refactorizaciones/grabacion-sesion-completa.md` §8.1–§8.2.

- **v4, dónde:** inmediatamente después de `{/selloTiempo}` (y por lo tanto antes
  de "CADENA DE CUSTODIA DIGITAL"): `{#registroAudiovisual}`, un párrafo vacío de
  separación, el subtítulo **Registro audiovisual de la recolección**,
  `{descripcionRegistroAudiovisual}`, la **segunda tabla** y
  `{detalleRegistroAudiovisual}`, y `{/registroAudiovisual}`. Los párrafos usan el
  pPr normalizado y el rPr de `2D02EAD7`. La tabla es un clon de la de hashes ya
  reescrita (grilla 3600/4691, encabezado repetido, `cantSplit`, mismos
  encabezados) con `{nombreArchivoSesion}` / `{hashArchivoSesion}` y `paraId`
  nuevos y deterministas (`a7:ses-<paraId original>`, `a7:ses-titulo`,
  `a7:ses-descripcion`, `a7:ses-detalle`, `a7:ses-espacio`, `marker:ses-open`,
  `marker:ses-close`).
- `BODY_PLACEHOLDERS` suma los 4 placeholders y `BODY_BLOCKS` suma
  `registroAudiovisual`. `--check` suma `FALLA:sesion`: una sola tabla entre los
  marcadores, con dos filas y los dos placeholders, y la tabla de hashes sigue
  siendo la **primera** con `tblStyle 17`.
- **v6:** `hash_table(tbl, hash_ph)` reestila **las dos** tablas con
  `tblStyle 17` (tienen que ser exactamente dos; si no, `FALLA: ancla tabla`): la
  primera con `{hashArchivo}` y la segunda con `{hashArchivoSesion}` en Courier.
  El subtítulo va en Arial negrita 10 pt tinta, sin sangría, con 12 pt antes y
  6 pt después. El chequeo de centinelas cuenta ahora 4 celdas de encabezado con
  la línea verde (2 por tabla) y `--check` suma `FALLA:sesion` (dos tablas con
  su hash en Courier y el subtítulo).
- **Qué lo llena:** `ReportService` (B-R2 con `conditions["registroAudiovisual"]
  = cas.RecordingSession is not null && hay tramos guardados`, B-R4 con
  `ReportSessionTexts.Description` / `Detail` en texto plano y B-R5b
  `FillSessionTable`, que clona la fila con `BuildHashRow(..., namePh:
  "{nombreArchivoSesion}", hashPh: "{hashArchivoSesion}")`, sin fila del ZIP).
  Sin sesión el bloque se borra entero, tabla incluida, y el informe queda
  idéntico al de antes. Los tramos no van en la tabla general ni en los conteos.

Regenerar las dos, siempre con los scripts:

```bash
python3 ops/plantilla/build_plantilla_v4.py "docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx" \
  server/src/Factum.Backend/Templates/plantilla_informe_v4.docx
python3 ops/plantilla/build_plantilla_v6.py server/src/Factum.Backend/Templates/plantilla_informe_v4.docx \
  server/src/Factum.Backend/Templates/plantilla_informe_v6.docx
```

## v5 (reproducible, sin uso)

Especificación: `Refactorizaciones/informe-diseno-modelo.md`. Para
reproducirla (por ejemplo, para comparar), generarla fuera del repo:

```bash
python3 ops/plantilla/build_plantilla_v5.py \
  server/src/Factum.Backend/Templates/plantilla_informe_v4.docx /tmp/plantilla_informe_v5.docx
```

---

# v4

`server/src/Factum.Backend/Templates/plantilla_informe_v4.docx` **no se edita a
mano**: se deriva de la plantilla del usuario con `build_plantilla_v4.py`
(Python 3, solo biblioteca estándar). La especificación completa está en
`Refactorizaciones/informe-pericial-de-parte.md`, Anexo A.

## Qué hace el script

- Canoniza los placeholders (`{nombreTribunal}`, `{caratula}`, …): cada uno
  queda en su propio run y en un solo `<w:t>`.
- Reescribe las frases atadas a una causa o a un género con placeholders y
  frases armadas en código (`{tramiteAnte}`, `{fraseDomicilio}`, `{elSuscripto}`, …).
- Saca los datos reales que trae la plantilla de origen **sin escribirlos ni
  imprimirlos nunca**: reconstruye esos párrafos desde cero, ubicándolos por
  `w14:paraId`.
- Agrega los bloques condicionales (`{#clave}` … `{/clave}`), el membrete
  (`word/header1.xml`), la firma del perito y el anexo de capturas.
- Ajusta la tabla de hashes (anchos, encabezado repetido, filas que no se
  parten) y el margen del pie (`w:footer="567"`, 1 cm).
- Reemplaza `docProps/core.xml` (autor `Factum`, sin nombres de personas).
- Empaqueta en forma determinista: la misma entrada da el mismo archivo.

Antes de editar, el script comprueba una huella **no sensible** de cada párrafo
que toca. Si la plantilla de origen cambió y una huella no coincide, aborta con
`FALLA: ancla <paraId>` y no escribe nada útil: hay que revisar el script.

## Uso

La plantilla de origen vive en `docs/` y **no se versiona** (`.gitignore`:
`docs/INFORME*`), porque es del usuario.

```bash
# Construir (y verificar al final)
python3 ops/plantilla/build_plantilla_v4.py \
  "docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx" \
  server/src/Factum.Backend/Templates/plantilla_informe_v4.docx

# Solo verificar
python3 ops/plantilla/build_plantilla_v4.py --check \
  "docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx" \
  server/src/Factum.Backend/Templates/plantilla_informe_v4.docx
```

Las dos formas imprimen solo `OK` o líneas `FALLA:<tipo>`, nunca el texto que
falló. `--check` verifica:

1. el conjunto exacto de placeholders del cuerpo y del membrete, cada uno
   entero dentro de un `<w:t>`, y que cada bloque abra y cierre una sola vez;
2. que ningún dato real de la plantilla de origen aparezca en el destino;
3. el autor de `core.xml`;
4. la estructura (`headerReference`, `w:footer="567"`, relación y content
   type del header, `w14:paraId` únicos, sin `w:author`);
5. que todas las partes XML estén bien formadas.

## Después de regenerar

1. Correr `--check` (tiene que dar `OK`).
2. Mirar el render: `soffice --headless --convert-to pdf <v4.docx>` en una
   carpeta temporal.
3. Abrir la v4 en Word o WPS y revisar las frases fijas.
4. `dotnet build` del backend: la plantilla se copia a `bin/` como contenido.

Para cambiar la redacción de una frase fija alcanza con editar el texto en
`build_plantilla_v4.py` y regenerar; `ReportService` no cambia mientras los
placeholders sean los mismos.
