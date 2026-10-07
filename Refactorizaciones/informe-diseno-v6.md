# SDD: Informe pericial v6, diseño "Filete" con la paleta de Factum

**Slug:** `informe-diseno-v6`
**HU:** `docs/hu-informe-diseno-v6.md`. Validada el 2026-10-01: D1 a D10 en la opción recomendada y **D9 = A "Filete"**, elegida por el usuario sobre el canvas https://claude.ai/artifact/FabuSoobjeFwZKxkn96uU5.
**Maqueta de referencia:** portada A (`Main.dc.html`) e interior A (`A-Interior.dc.html`) del canvas. Las medidas de esta SDD salen de ahí: px a 96 dpi, 1 px = 0.75 pt = 15 twips (dxa). Questrial equivale a Century Gothic, Arimo a Arial y Cousine a Courier New.
**Implementa:** solo `implementer-backend` (Opus), en `server/src/Factum.Backend`, `server/tests/Factum.Backend.Tests`, `ops/plantilla/` y `README.md`. **No hay trabajo de frontend.**
**Rama:** `feat/informe-diseno-v6`, que sale de la v5 (`c8b8fe9`).

---

## 1. Resumen funcional

El informe DOCX cambia de diseño y no cambia de contenido. Se van la banda del encabezado interior, "el cuadro" de acento y la franja y las bandas de la portada: **no queda ninguna forma flotante en el documento**. La portada queda sobria: un filete verde corto, el título "Informe pericial / técnico informático" grande en tinta, el subtítulo "Inspección técnica de dispositivo móvil" en gris y una ficha etiqueta/valor (causa, carátula, perito, fecha) con líneas finas grises. Abajo van la identidad del estudio (logo de unos 4.5 × 2.5 cm, nombre y contacto) y "Realizado con Factum". En las páginas interiores, el encabezado es texto chico gris ("Informe pericial técnico informático · causa", con el nombre del estudio a la derecha) sobre una línea fina. Cada sección numerada muestra su romano grande en verde, arriba del título, que va en mayúsculas, tinta e interletrado, con un filete verde corto debajo. Referencia de la actuación, Identificación y Elementos ofrecidos son fichas a todo el ancho. En la tabla de hashes, el encabezado va sin relleno y con una línea verde debajo, y la fila del ZIP lleva un tinte suave. El pie tiene una línea fina, "Realizado con Factum" a la izquierda y "Página N de M" a la derecha. El título del escrito pierde el subrayado. La paleta por defecto pasa a ser la de Factum (verde `#2F6F12`, tinte `#E8F3DF`). `Branding:PrimaryColor` y `Branding:AccentColor` la pueden pisar, y en esta instalación se borran de la config local. El texto pericial, el orden de las secciones, los placeholders, el flujo de generación y los hashes no cambian. Los informes ya generados no se tocan.

## 2. Toca

| Lado | ¿Toca? | Qué |
|---|---|---|
| backend (API) `server/src/Factum.Backend` | **sí** | `Templates/plantilla_informe_v6.docx` (nueva, generada), se **borra** `Templates/plantilla_informe_v5.docx`, `Services/Reports/ReportService.cs` (plantilla v6, bloques del pie de portada, B-R5 con tinte, B-R8 con slot), `Services/Reports/BrandColors.cs` (comentarios), `Services/Branding/BrandingColors.cs` (defaults de Factum, contraste del acento con la tinta), `Services/Branding/BrandingService.cs` (validación del acento), `Services/Branding/BrandingOptions.cs` (comentarios), `appsettings.Local.json` (**no versionado**: se borran solo las dos claves de color, D1 de la HU) |
| backend (Tatana) `server/src/Factum.Agent` | **no** | — |
| client `client/` | **no** | Verificado: `client/src` y `agent-ui/src` no leen colores ni plantilla (`grep PrimaryColor\|AccentColor\|primary_color\|accent_color` no encuentra nada). `GET /api/config/public` no cambia. |
| agent-ui `agent-ui/` | **no** | — |
| otros | **sí** | `ops/plantilla/build_plantilla_v6.py` (nuevo), `ops/plantilla/README.md`, `server/tests/Factum.Backend.Tests/ReportDesignTests.cs` y `BrandingColorsTests.cs` (se adaptan), `README.md` (Branding y placeholders) |

**No hay Contrato compartido:** ningún campo cruza a `client/` ni a Tatana. No hay endpoints ni mensajes WebSocket nuevos o modificados.

---

## 3. Modelo de datos

**Sin cambios en MongoDB.** No hay campos, colecciones, índices ni migraciones. Los casos `Completed` no se regeneran (409, como hoy). Sus `zip_hash` y `report_hash` y sus archivos en `Storage:DataDirectory` quedan byte a byte iguales. Un caso en borrador que se genere después del deploy sale con la v6. No hace falta inspeccionar Mongo para especificar: el diseño no lee ni escribe datos nuevos.

### 3.1 Configuración (sección `Branding`, claves que ya existen)

| Clave | Default versionado (cambia) | Dónde se usa en la v6 | Validación al arrancar |
|---|---|---|---|
| `Branding:PrimaryColor` | `""` → `BrandingColors.DefaultPrimary = "2F6F12"` (verde de Factum, unos 6.2:1 con blanco) | Filetes (portada, títulos y cierre), números romanos y la línea bajo el encabezado de la tabla de hashes | Igual que hoy: hex inválido o contraste < 4.5:1 con blanco dan warning y default |
| `Branding:AccentColor` | `""` → `BrandingColors.DefaultAccent = "E8F3DF"` (tinte de Factum) | **Solo** el fondo de la fila del contenedor ZIP en la tabla de hashes | Hex inválido: warning y default (igual que hoy). **Nuevo (D4):** si el contraste con la tinta `0E1013` es < 4.5:1, warning `Branding: AccentColor <hex> tiene contraste <x.x>:1 con la tinta (mínimo 4.5:1); se usa el default` y se usa el default |
| `OrganizationName`, `ContactLines`, `OrganizationLogo`, `OrganizationIsotype` | sin cambio | Ver 5.3, 5.4 y 5.6 | sin cambio |

`appsettings.json` versionado **no cambia**: las claves ya están vacías. Los defaults del código pasan a ser la paleta de Factum, que es la marca del producto y no datos de un cliente. Los colores del estudio siguen sin poder ser default de código.

**Config local de esta instalación (D1 B de la HU):** `server/src/Factum.Backend/appsettings.Local.json` (ignorado por git, `.gitignore:38`) tiene hoy en `Branding` las claves `OrganizationName`, `ContactLines`, `OrganizationLogo`, `PrimaryColor` y `AccentColor`. El implementador borra **solo** `PrimaryColor` y `AccentColor`, sin tocar las otras tres ni el resto del archivo, y sin imprimir los valores en la consola, en el progress ni en ningún archivo versionado.

---

## 4. Endpoints / WebSocket

**Ninguno nuevo ni modificado.**

---

## 5. Diseño del documento (plantilla v6)

### 5.0 Paleta y tipografía (valores fijos de la plantilla)

| Rol | Hex | ¿Centinela? |
|---|---|---|
| Verde (filetes, números, línea de la tabla) | `2F6F12` | **Sí:** es el centinela del primario (= `DefaultPrimary`), y B-R2b lo reemplaza si hay `PrimaryColor` |
| Tinte (fila del ZIP) | `E8F3DF` | **No va en la plantilla:** lo aplica B-R5 en código con `brand.AccentColor` (5.5) |
| Tinta (títulos, valores de fichas, nombres de archivo y hashes) | `0E1013` | no, fijo |
| Texto secundario (cuerpo, subtítulos) | `3D444C` | no, fijo |
| Texto terciario (etiquetas, encabezado, pie) | `5C656E` | no, fijo |
| Línea gris | `D9DDE1` | no, fijo |

| Uso | Fuente | Tamaño (`w:sz`) | Otros |
|---|---|---|---|
| Título de portada | Century Gothic | 34 pt (68) | tinta, interlineado exacto 760 |
| Subtítulo de portada | Arial | 13 pt (26) | `3D444C` |
| Números de sección | Century Gothic | 20 pt (40) | verde, sin negrita |
| Títulos de sección | Arial negrita, `<w:caps/>` | 11 pt (22) | tinta, `w:spacing w:val="20"` (1 pt de interletrado) |
| Título del escrito, línea 1 | Century Gothic | 12 pt (24) | tinta, interletrado 20, sin negrita |
| Título del escrito, línea 2 | Arial | 9 pt (18) | `3D444C`, interletrado 20, sin negrita |
| Cuerpo | Arial | 11 pt (22, como la v5) | **`3D444C`** (D8), interlineado 1.5 de la v4 |
| Etiquetas de fichas y encabezado de la tabla | Arial negrita, `<w:caps/>` | 8 pt (16) | `5C656E`, interletrado 20 |
| Valores de fichas | Arial | interior 10 pt (20), portada 11 pt (22) | tinta |
| Encabezado y pie | Arial | 8 pt (16) | `5C656E` |
| Nombre de archivo (tabla) | Arial | 9 pt (18) | tinta |
| Hash | Courier New | 8 pt (16) | tinta |

Líneas: la gris es `single` sz 6 (0.75 pt, `D9DDE1`). La verde de la tabla es `single` sz 8 (1 pt). Los filetes de título y de cierre son sz 18 (2.25 pt, 36 × 3 px en la maqueta) y el de portada es sz 36 (4.5 pt, 112 × 6 px).

### 5.1 De dónde sale la v6 (D1, D2)

- `ops/plantilla/build_plantilla_v6.py` construye `Templates/plantilla_informe_v6.docx` **a partir de la v4 versionada**, igual que hizo la v5. No parte de la v5 porque habría que deshacer la mitad (formas, banda, tablas de dos columnas con `vMerge`). Además, la v4 es la línea de base del test de texto.
- El script **importa** los helpers de `build_plantilla_v4.py` y de `build_plantilla_v5.py` (`children`, `set_children`, `get_ppr`, `with_ppr`, `runs_of`, `restyle_runs`, `rpr`, `color`, `sz`, `run`, `runs`, `root_of`, `tnr_to_arial`, `build_styles`, `build_fonts`, `fingerprint`, `Ids`, `read_parts`, `check_placeholders`…), con `sys.dont_write_bytecode = True`. Define su propio `para()`/`marker()`/`tiny()` con prefijo de semilla `"v6:"`.
- `plantilla_informe_v5.docx` **se borra** (D8 A de la HU: ni el runtime ni los tests la usan). `build_plantilla_v5.py` **queda sin cambios**: lo importa el v6 y reproduce la v5 desde la v4 byte a byte, si alguna vez hace falta.
- `plantilla_informe_v4.docx` y `build_plantilla_v4.py` no cambian.
- Los mismos principios de siempre: XML como texto, anclas por `w14:paraId` con huella no sensible, `FALLA: centinela-en-origen` si la v4 trae `2F6F12` o `E8F3DF` (lo verifiqué: no los trae), placeholders enteros en un `<w:t>`, ZIP determinista y `--check` que imprime solo `OK` o `FALLA:<tipo>`.

### 5.2 Secciones y medidas (A4 = 11906 × 16838 dxa)

| | Sección 1: portada | Sección 2: interior |
|---|---|---|
| `sectPr` | en el `pPr` del **último párrafo de la portada** (un `tiny` después de la ficha), `<w:type w:val="nextPage"/>` | el `sectPr` final del body (el de la v4, modificado) |
| `pgMar` (dxa) | top **1440**, right **1558**, bottom 1417, left 1701, header 284, footer 567 | top **1418**, right 1558, bottom 1417, left 1701, header **680**, footer 567 |
| Ancho de texto | 8647 (la misma grilla que el interior) | 8647 |
| `headerReference default` | `header2.xml` (**vacío**: un párrafo `tiny`) | `header1.xml` (encabezado de texto, 5.4) |
| `footerReference default` | `footer2.xml` (identidad del estudio, 5.3) | `footer1.xml` (línea fina + slot de atribución + Página N de M, 5.4) |
| `titlePg` / `evenAndOddHeaders` | no | no |

Se mantienen las dos secciones de la v5 (portada con su propio pie y sin el encabezado interior), con la misma numeración: `PAGE`/`NUMPAGES` cuentan la portada, así que la primera página interior dice "Página 2 de M" (DP1 de la v5). Con la franja fuera, la portada ya no necesita el margen derecho ancho.

### 5.3 Portada (cuerpo de la sección 1 + `header2` + `footer2`)

**Cuerpo** (párrafos nuevos, insertados **antes** de `0DF4CF61`). Todos van con `jc left`, `ind left 0`, sin numeración:

| # | Contenido | Formato |
|---|---|---|
| P1 | vacío (espaciador) | `w:spacing w:before="0" w:after="0" w:line="1800" w:lineRule="exact"`, rPr sz 2 (120 px de la maqueta) |
| P2 | filete | párrafo vacío, `pBdr/top` single **sz 36** `space 0` color verde, `ind left 0 right 6967` (8647 − 1680 = filete de 1680 dxa ≈ 3 cm), `spacing before 0 after 540 line 40 exact`, rPr sz 2, `keepNext` |
| P3 | `Informe pericial` | Century Gothic sz 68 tinta, `spacing before 0 after 0 line 760 exact`, `keepNext` |
| P4 | `técnico informático` | igual, `after 270` |
| P5 | `Inspección técnica de dispositivo móvil` | Arial sz 26 `3D444C`, `after 1650` (110 px), `keepNext` |
| T1 | **ficha de la causa** (tabla, abajo) | — |
| P6 | vacío `tiny` + **`sectPr` de la sección 1** (A.2) | — |

**Ficha T1** (una tabla de flujo):

- `tblW` 8647 dxa, `tblInd` 0, `tblLayout fixed`, grilla **1920 / 6727** (128 px = etiqueta 110 + separación 18).
- `tblBorders`: `top`, `bottom` e `insideH` single sz 6 `D9DDE1`; `left`, `right` e `insideV` en `nil`.
- `tblCellMar`: top 165, bottom 165, left 0, right 108. Filas con `cantSplit`. Celdas con `vAlign top`.
- Párrafos de celda: `spacing before 0 after 0 line 276 auto`, `jc left`, `ind 0`.
- Etiqueta: rPr Arial negrita `caps` sz 16 `5C656E` con interletrado 20. Valor: Arial sz 22 tinta. Cada placeholder va en su propio run.

| Etiqueta (texto) | Valor |
|---|---|
| `Causa` | `{tipoCausa} N° {numeroCausa}` |
| `Carátula` | `“{caratula}”` (comillas tipográficas, como la v5) |
| `Perito` | `{nombrePerito} · M.P. {matriculaPerito}` |
| `Fecha` | `{fechaInspeccion}` |

Las etiquetas de la portada son texto **nuevo** de la portada, que está fuera de la comparación de contenido. Por eso no llevan dos puntos, como en la maqueta. Una carátula de 300 caracteres ocupa unas 5 líneas en 6727 dxa, y la portada sigue entrando en una página: el cuerpo ocupa unos 10 000 dxa de los ~12 500 disponibles con el pie más alto.

**`header2.xml`:** un solo párrafo `tiny`, sin formas ni texto. Raíz con los namespaces del `footer1` de la v4 (`root_of` de la v5).

**`footer2.xml`** (de arriba hacia abajo; después B-R8 agrega la atribución centrada al final, como hoy):

```
{#MEMBRETE}
{#MEMBRETE_CON_LOGO}
  tabla 2 columnas sin bordes, tblW 8647, fixed, grilla 2851 / 5796, cellMar left 0, right 0, top 0, bottom 0
    celda 1 (vAlign center, tcMar right 300):  {LOGO_ORGANIZACION:4.5x2.5}      ← jc left
    celda 2 (vAlign center):                   {ORGANIZACION}                   ← Arial negrita sz 21 tinta
                                               {CONTACTO}                       ← Arial sz 18 5C656E
{/MEMBRETE_CON_LOGO}
{#MEMBRETE_SIN_LOGO}
  {ORGANIZACION}                                                                ← igual formato
  {CONTACTO}
{/MEMBRETE_SIN_LOGO}
{/MEMBRETE}
(párrafo tiny, spacing after 600)   ← 40 px entre la identidad y la atribución
```

- Párrafos de la identidad: `spacing before 0 after 0 line 240 auto` (el del nombre con `after 60`).
- Los bloques se resuelven con el mecanismo de siempre (`ResolveBlocks` borra todos los hermanos entre los marcadores, tablas incluidas). Si `MEMBRETE` es false, los marcadores internos ya quedaron desprendidos y `Attached` los saltea. Con cualquier combinación, la tabla siempre queda seguida de un párrafo (el `tiny` final).
- Sin Branding: desaparece todo y queda solo la atribución (escenario "Portada sin Branding").
- Con logo y sin nombre: la celda 2 queda con párrafos vacíos. No es un hueco notable, porque el logo sigue a la izquierda.
- El logo se ajusta **sin recortar** dentro de 4.5 × 2.5 cm (D2 A de la HU), con `ReplaceImagePlaceholder` sin cambios.

### 5.4 Páginas interiores

**`header1.xml`** (reemplazo completo; sin formas):

- Tabla de flujo `tblW` 8647, `tblInd` 0, `fixed`, grilla **5400 / 3247**.
- `tblBorders`: `bottom` single sz 6 `D9DDE1`, el resto `nil`. `tblCellMar`: left 0, right 0, top 0, bottom 120 (8 px hasta la línea).
- Celda izquierda (`vAlign bottom`): un párrafo `jc left` con `Informe pericial técnico informático · ` + `{tipoCausa}` + ` N° ` + `{numeroCausa}` (cada placeholder en su propio run).
- Celda derecha (`vAlign bottom`): un párrafo `jc right` con `{ORGANIZACION}`. Si no hay nombre, `ReportValues` lo resuelve a `""` y la celda queda vacía. No hace falta un bloque.
- Runs: Arial sz 16 `5C656E`. Párrafos: `spacing before 0 after 0 line 240 auto`.
- Párrafo `tiny` final (un header tiene que terminar en párrafo).
- El texto del cuerpo nunca queda tapado: el encabezado ocupa de 680 a ~1010 dxa y el cuerpo arranca en 1418 (unos 20 pt de aire, contra 22.5 pt de la maqueta). Si el perito agranda el encabezado, Word y LibreOffice bajan el cuerpo solos, porque todo es de flujo.

**`footer1.xml`** (reemplazo completo):

```xml
<w:p>
  <w:pPr>
    <w:pBdr><w:top w:val="single" w:sz="6" w:space="6" w:color="D9DDE1"/></w:pBdr>
    <w:tabs><w:tab w:val="right" w:pos="8647"/></w:tabs>
    <w:spacing w:before="0" w:after="0" w:line="240" w:lineRule="auto"/>
    <w:jc w:val="left"/>
  </w:pPr>
  <w:r>{ARIAL8GRIS}<w:t>{ATRIBUCION_FACTUM}</w:t></w:r>
  <w:r>{ARIAL8GRIS}<w:tab/></w:r>
  <w:r>{ARIAL8GRIS}<w:t xml:space="preserve">Página </w:t></w:r>
  … campo PAGE (Anexo A.3 de la v5) … " de " … campo NUMPAGES …
</w:p>
```

`{ARIAL8GRIS}` = `<w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial" w:cs="Arial"/><w:color w:val="5C656E"/><w:sz w:val="16"/><w:szCs w:val="16"/></w:rPr>`. `{ATRIBUCION_FACTUM}` es el **slot** donde B-R8 pone el sello y el texto (5.6).

**Cuerpo de la sección 2** (sobre los párrafos de la v4, ubicados por `w14:paraId`):

| Qué | paraIds v4 | Cambio |
|---|---|---|
| Título del escrito | `0DF4CF61` | Se sacan `w:u` (del `pPr/rPr` y de los runs) y `w:b`/`w:bCs`. Los runs antes del `<w:br/>` pasan a Century Gothic sz 24 tinta, interletrado 20. Los runs después del `<w:br/>` pasan a Arial sz 18 `3D444C`, interletrado 20. pPr: `spacing before 0 after 330 line 276 auto`. Quedan iguales `jc center`, `ind right -143`, el `<w:br/>` y el texto. |
| Títulos numerados (11) | `0BF0C8CD`, `0386A4A4`, `0F4EDFCE`, `5B3056CF`, `0FF62E21`, `3A62DE0D`, `37BC776D`, `5A06ED76`, `390F7DAB`, `05D9340B`, `10E394C7` | Grupo de título (5.4.1) **con** párrafo de número |
| Títulos sin número (2) | `3B831F98` (Referencia de la actuación), `18A07FBA` (Declaración de imparcialidad) | Grupo de título **sin** párrafo de número |
| Separadores vacíos | `262A3013`, `236C9BC4`, `48125D00`, `3A37F215`, `4A82988C`, `2DD0B2AA`, `4F0246D1` | Pasan a `tiny` (`spacing before 0 after 0 line 240 auto`, rPr sz 2; se saca `numPr` e `ind` si tienen). Así el aire antes de cada título lo da una sola regla (`before 360`) y no un párrafo vacío de 1.5 líneas que unas secciones tienen y otras no. `236C9BC4` y `3A37F215` siguen siendo el párrafo obligatorio después de su ficha. La huella exige que los 7 estén vacíos. |
| Fichas | Referencia, Identificación, Elementos ofrecidos | 5.4.2 |
| Tabla de hashes | la tabla única de la v4 (`tblStyle 17`) | 5.4.3 |
| Anexo | `6A0EA424` | Se mantiene centrado y con el mismo texto. Runs: Arial negrita `caps` sz 22 tinta con interletrado 20 (el mismo aspecto que un título, sin número ni filete). |
| Cierre | después de `4F0EBB3D` (`{caracterPerito}`), antes de `16E02A15` | Filete centrado (párrafo vacío, `pBdr/top` sz 18 verde, `ind left 4054 right 4053`, `spacing before 480 after 0 line 40 exact`, rPr sz 2) + `{#ISOTIPO}` / párrafo `jc center`, `before 240`, con `{ISOTIPO_ORGANIZACION:2x2}` / `{/ISOTIPO}` |
| Resto (textos largos, firma, capturas) | — | Sin cambios de párrafo. La fuente pasa a Arial (TNR → Arial, como la v5) y el color sale del estilo (5.4.4). |

#### 5.4.1 Grupo de título

Para cada título se emite, en este orden y **dentro del mismo lugar** que ocupaba el párrafo original:

1. **Párrafo de número** (solo los 11 numerados; paraId nuevo, semilla `"v6:num:<paraId del título>"`):
   `<w:pPr><w:keepNext/><w:numPr><w:ilvl w:val="0"/><w:numId w:val="2"/></w:numPr><w:spacing w:before="360" w:after="0" w:line="240" w:lineRule="auto"/><w:ind w:left="0" w:firstLine="0"/><w:contextualSpacing w:val="0"/><w:jc w:val="left"/><w:rPr>{CG sz 40 verde}</w:rPr></w:pPr>`, **sin runs**. Word, LibreOffice y WPS muestran la etiqueta de numeración en un párrafo vacío. El romano sale de la lista, así que la numeración se corre sola cuando falta una sección condicional.
2. **Párrafo de título** (el original, mismo paraId, mismo texto). Se saca `pStyle` (List Paragraph 31) y `numPr`. pPr: `keepNext`, `spacing before 120 after 120 line 240 auto` (sin número: `before 360`), `ind left 0 firstLine 0`, `contextualSpacing 0`, `jc left`. rPr de todos los runs: Arial negrita `caps` sz 22 tinta con interletrado 20. Se recorta el espacio inicial del primer `<w:t>` (`" CADENA DE CUSTODIA DIGITAL"`, `" RESULTADOS"`), para que todos los títulos arranquen en el mismo margen. El test de contenido normaliza espacios, así que esto no cambia el texto comparado.
3. **Filete** (paraId nuevo, semilla `"v6:fil:<paraId>"`): párrafo vacío con `keepNext`, `pBdr/top` single **sz 18** `space 0` verde, `ind left 0 right 8107` (filete de 540 dxa ≈ 0.95 cm), `spacing before 0 after 180 line 40 exact` y rPr sz 2.

Los títulos de Notas técnicas y Reserva están **dentro** de su bloque condicional (`{#descripcionNotasTecnicas}` … `{/…}`). Como el número y el filete se insertan pegados al título, quedan dentro del bloque y desaparecen con él. "Objeto del informe" no es condicional en la v4 (solo lo es su texto `{objetoInforme}`), así que el I siempre está.

**Numeración** (`numbering.xml`, `abstractNum` 1, nivel 0, que usa solo `numId` 2; la huella lo verifica): `lvlText` `"%1"` (sin punto, como la maqueta), `lvlJc left`, se agrega `<w:suff w:val="nothing"/>`, `pPr/ind left 0 hanging 0` y `rPr` Century Gothic sz 40 / szCs 40 verde, **sin** negrita.

**Por qué no se pierde el filete con dos títulos seguidos** (limitación 7 de la v5): cada filete es el borde de su propio párrafo y entre dos filetes siempre hay un párrafo de título sin borde, así que Word y LibreOffice no los agrupan.

#### 5.4.2 Fichas (Referencia de la actuación, Identificación, Elementos ofrecidos)

- El título va arriba como cualquier otro (5.4.1). Debajo va una tabla a todo el ancho.
- Tabla: `tblW` 8647, `tblInd` 0, `fixed`, grilla **2490 / 6157** (166 px = etiqueta 150 + separación 16). `tblBorders` `top`, `bottom` e `insideH` single sz 6 `D9DDE1`, el resto `nil`. `tblCellMar` top 105, bottom 105, left 0, right 108. Filas con `cantSplit`.
- Filas: cada párrafo `**Etiqueta:** valor` de la v4 se parte en etiqueta y valor con el `split_row` de la v5. La etiqueta lleva paraId nuevo, el valor conserva el paraId original, el run de solo espacio se descarta y se sacan `numPr` y viñeta. Etiqueta: Arial negrita `caps` sz 16 `5C656E` con interletrado 20. Valor: Arial sz 20 tinta. Los párrafos de celda van con `spacing 0/0 line 260 auto`, `jc left`, `ind 0`. El texto de la etiqueta **conserva los dos puntos** (DP1).
- **Elementos ofrecidos:** la intro `5FECA2F2` ("En fecha…") **no** va en la tabla. Queda como párrafo de cuerpo entre el filete y la ficha, en su lugar y con su formato de la v4. `26DDBA6A` ("El mismo fue aportado…") y las capturas siguen después de la tabla, a todo el ancho. El orden del texto no cambia.
- Párrafo obligatorio después de cada tabla: Referencia → `236C9BC4`, Identificación → `3A37F215` (los dos `tiny`), Elementos → `26DDBA6A`.

#### 5.4.3 Tabla de hashes (plantilla)

- `tblW` **8647 dxa** (hoy `auto`), `tblInd` 0, grilla **3090 / 5557** (los `tcW` de cada celda se actualizan). `tblLayout fixed` y `tblStyle` quedan (los bordes directos ganan).
- `tblBorders`: `bottom` e `insideH` single sz 6 `D9DDE1`; `top`, `left`, `right` e `insideV` en `nil`. `tblCellMar`: top 100, bottom 100, left 85, right 85.
- **Fila de encabezado** (`04E11EDC` "NOMBRE", `4B698602` "HASH SHA-256"): `tcBorders` `bottom` single **sz 8 verde** y `top`/`left`/`right` `nil`. **Sin `shd`.** Párrafos `jc left`. Runs Arial negrita `caps` sz 16 `5C656E` con interletrado 20. `tblHeader` se conserva (el encabezado se repite si la tabla pasa de página).
- **Fila modelo:** `tcBorders` `bottom` single sz 6 `D9DDE1` y el resto `nil`. El nombre va en Arial sz 18 tinta y el hash en Courier New sz 16 tinta. `cantSplit` se conserva.

#### 5.4.4 Estilos y fuentes

- `build_styles` de la v5 (Arial y sz 22 en `docDefaults` y en Normal), y **además** `<w:color w:val="3D444C"/>` en el `rPr` de `docDefaults/rPrDefault` y del estilo Normal (`styleId 1`), en el orden del esquema (`set_children` con `RPR_ORDER`).
- `tnr_to_arial` en `document.xml` y en las partes nuevas. `build_fonts` (Century Gothic y Arial en `fontTable.xml`).
- La v4 no tiene ningún `w:color` explícito en el cuerpo (verificado), así que el gris del cuerpo sale del estilo. Todo lo que tiene que ir en tinta lleva color explícito.

### 5.5 Colores en la generación

- **B-R2b** (`BrandColors.Apply`) no cambia de lógica. Los centinelas siguen siendo `BrandingColors.DefaultPrimary` / `DefaultAccent`, que ahora valen `2F6F12` / `E8F3DF`. En la v6 el primario aparece en `document.xml` (filetes, párrafos de número y línea de la tabla) y en `numbering.xml`, no en headers ni footers. El acento **no** aparece en la plantilla, así que su reemplazo es un no-op (se deja el soporte por si una plantilla futura lo usa). Se actualizan los comentarios (v5 → v6 y valores).
- **Tinte de la fila del ZIP (B-R5):** `FillHashTable` recibe `brand.AccentColor`. La fila del contenedor se arma con `BuildHashRow(..., isContainer: true, tint)`, que:
  - pone en cada `TableCellProperties` `Shading { Val = Clear, Color = "auto", Fill = tint }` (con la propiedad tipada `tcPr.Shading`, así el SDK respeta el orden del esquema);
  - pone en **negrita** los runs del nombre del ZIP;
  - da a la leyenda "Contenedor de la evidencia" un estilo recto (sin cursiva), sz 16 y color `3D444C`, como la maqueta.

  La línea "Origen: …" de los archivos sigue igual (cursiva, 7 pt, `595959`). Como la fila del ZIP se distingue también por la negrita y la leyenda, no depende solo del color (escenario "Impresión en blanco y negro"). B-R5 corre **después** de B-R2b, por eso usa el color ya resuelto y no un centinela.

### 5.6 Cambios en `ReportService`

- `DefaultTemplateFileName = "plantilla_informe_v6.docx"`. Comentario de clase: v6, `build_plantilla_v6.py` y esta SDD.
- `BlockKeys`: se agregan `"MEMBRETE_CON_LOGO"` y `"MEMBRETE_SIN_LOGO"` y **se saca** `"NOMBRE_EN_BANDA"` (en la v6 el nombre va siempre en el encabezado y el isotipo solo al cierre). En `conditions`:
  - `["MEMBRETE_CON_LOGO"] = brand.Logo is not null`
  - `["MEMBRETE_SIN_LOGO"] = brand.Logo is null`
  - se borra `NOMBRE_EN_BANDA`. `MEMBRETE` e `ISOTIPO` quedan iguales. Se actualizan los comentarios ("MEMBRETE vive en el pie de la portada", "ISOTIPO: solo al cierre").
- `KnownPlaceholders` suma `"{ATRIBUCION_FACTUM}"`, para que B-R1 no lo borre. B-R6 no lo toca (no está en `values`).
- **B-R5:** `FillHashTable(body, files, hashes, zipFilename, zipHash, brand.AccentColor, resolved)` y `BuildHashRow(model, name, secondLine, hash, resolved, isContainer, tint)` (5.5).
- **B-R8 (`AddFactumAttributionFooter`):** la búsqueda de secciones y footers (`targets`) no cambia. Por cada `footerPart` de `targets`:
  ```csharp
  private const string AttributionSlot = "{ATRIBUCION_FACTUM}";
  var slot = footerPart.Footer.Descendants<Paragraph>()
      .FirstOrDefault(p => ParagraphText(p).Contains(AttributionSlot));
  if (slot is not null && TryFillAttributionSlot(footerPart, slot, sello, ref drawId)) { }
  else footerPart.Footer.AppendChild(BuildAttributionParagraph(footerPart, sello, ref drawId));
  ```
  - `BuildAttributionRuns(owner, sello, ref drawId)` devuelve los runs de hoy: el sello con `Position -3`, si hay, y el texto `" Realizado con Factum"` en Arial 8 `5C656E`. `BuildAttributionParagraph` los usa (centrado, como hoy).
  - `TryFillAttributionSlot`: busca el `Run` cuyo texto es exactamente el slot, inserta antes los runs de atribución y borra ese run. Devuelve `false` si el slot no está entero en un run. En ese caso borra el texto del slot con `RewriteParagraph` y se cae al párrafo centrado.
  - La garantía no cambia: **cada** footer de cada sección termina con exactamente una atribución. Con slot, va en el lugar del slot; sin slot, se agrega como hoy. `footer2` (portada) no tiene slot y sale centrada, como en la maqueta.
- **No cambian:** `GenerateAsync` (ZIP → hash → verificación → DOCX → hash), `EvidenceZip`, B-R1, B-R2, B-R3 (`ReplaceImagePlaceholder`; el logo de portada usa el tamaño del placeholder), B-R4, B-R6, B-R7, B-R9, `ReportValues`, `IsKnownPlaceholder` (salvo el set), `BodyFont`, `CaptionColor` y el constructor interno de tests.

### 5.7 Branding

```csharp
// Services/Branding/BrandingColors.cs
public const string DefaultPrimary = "2F6F12"; // verde de Factum (--fx-accent claro), ≈ 6.2:1 con blanco
public const string DefaultAccent  = "E8F3DF"; // tinte de Factum (--fx-accent-soft claro)
public const string InkColor       = "0E1013"; // tinta de Factum: texto sobre el tinte
public const double MinPrimaryContrast = 4.5;
public const double MinAccentContrastWithInk = 4.5;
public static double Contrast(string hexA, string hexB);   // WCAG 2.x, simétrico
public static double ContrastWithWhite(string hex6) => Contrast(hex6, "FFFFFF");
```

- `BrandingService`: `NormalizeAccent(raw, logger)` = `NormalizeColor(...)` + chequeo `Contrast(hex, InkColor) >= MinAccentContrastWithInk`. Si no pasa, warning (3.1) y `DefaultAccent`. El log de inicio sigue igual.
- `BrandingSnapshot` no cambia de firma: sus defaults apuntan a las constantes, que ahora son las de Factum. `new BrandingSnapshot(null, [], null)` (lo usa `ReportValuesIntegrantesTests`) sigue compilando.
- `BrandingOptions`: comentarios actualizados ("Vacío = verde de Factum", "Vacío = tinte de Factum; es el fondo de la fila del contenedor ZIP", "Isotipo: va al cierre del informe").
- `ConfigController` no cambia.

---

## 6. Decisiones técnicas

| # | Decisión | ¿Usuario? |
|---|---|---|
| D1 | v6 generada por `build_plantilla_v6.py` **desde la v4**, reusando los helpers de v4 y v5 por import. Es determinista y tiene `--check`. | no |
| D2 | Se borra `plantilla_informe_v5.docx`. `build_plantilla_v5.py` queda sin cambios: lo importa el v6 y reproduce la v5. | no (D8 de la HU) |
| D3 | Centinela del primario = nuevo default `2F6F12`. El tinte no es centinela: lo aplica B-R5 en código. Tinta y grises van fijos en la plantilla. | no |
| D4 | `AccentColor` se valida contra la tinta (≥ 4.5:1), porque ahora es fondo de texto. Si no pasa, se usa el default. Solo afecta una config inválida. | no |
| D5 | Sin formas en ninguna parte del documento. Encabezado, pie, portada, filetes y fichas son párrafos o tablas de flujo. `header2` queda vacío explícito. | no (HU) |
| D6 | El romano va en un **párrafo propio, vacío, que lleva la numeración** (`numId 2`) arriba del título, con `lvlText "%1"` sin punto, como la maqueta. Si en el render un procesador (Word, LibreOffice o WPS) no muestra el número en el párrafo vacío, el implementador **no improvisa**: informa `blocked`. La alternativa es la variante "en línea" de la HU (número a la izquierda del título, con sangría francesa), y para usarla hay que preguntarle al usuario. | no (salvo el fallback) |
| D7 | El filete es el borde superior de un párrafo vacío con sangría derecha. Es portable, editable y no se agrupa con otros bordes. | no |
| D8 | Tipografía según la maqueta, que el usuario aprobó después de la HU: títulos de sección en **Arial** negrita (Arimo en la maqueta) y Century Gothic en la portada, el título del escrito y los números. Cuerpo en Arial 11 pt (v5) **en `3D444C`** como la maqueta (la HU dejaba "cuerpo o tinta" a la SDD). Los tamaños de la maqueta se respetan salvo el cuerpo, que queda en 11 pt (la maqueta usa ~10 pt). | no |
| D9 | Encabezado interior en una tabla de 2 celdas (causa / nombre), con la línea gris como borde inferior de la tabla. Sin nombre configurado, la celda derecha queda vacía y no hace falta un bloque. | no (D3 y D2 de la HU) |
| D10 | El pie interior lleva "Realizado con Factum" y "Página N de M" en la misma línea, por un **slot** `{ATRIBUCION_FACTUM}` que B-R8 llena. Si una plantilla no tiene slot, B-R8 agrega el párrafo centrado de siempre. | no |
| D11 | Identidad de la portada en `footer2`, con dos variantes por bloque (`MEMBRETE_CON_LOGO` / `MEMBRETE_SIN_LOGO`): sin logo no queda una celda vacía de 4.5 cm. | no |
| D12 | Fichas de una tabla de 2 columnas (etiqueta / valor) con `split_row` de la v5. La intro de Elementos queda fuera de la ficha, como párrafo. | no (D6 de la HU) |
| D13 | Los 7 separadores vacíos de la v4 antes de los títulos pasan a `tiny`, para que el ritmo vertical sea uniforme. | no |
| D14 | Título del escrito sin subrayado y sin negrita (CG 12 tinta + Arial 9 gris), con el mismo texto y centrado. | no (D10 de la HU + maqueta) |
| D15 | Se saca `NOMBRE_EN_BANDA` y el isotipo queda solo al cierre. | no (D2 de la HU) |
| D16 | Márgenes: el interior con la grilla de la v4 (left 1701, right 1558), top 1418 y header 680. La portada con la misma grilla, top 1440. | no |
| D17 | En esta instalación se borran `PrimaryColor` y `AccentColor` de `appsettings.Local.json`, sin tocar otras claves. | no (ya confirmado, D1 de la HU) |
| D18 | Tests: se adaptan `ReportDesignTests` (v4 vs v6) y `BrandingColorsTests`. Siguen sin Mongo ni `Storage`. | no |
| D19 | Los dos puntos de las etiquetas y del título "Referencia de la actuación:" se conservan. | **DP1** |
| D20 | La sangría de primera línea de 4 cm de los párrafos del cuerpo (v4) se conserva. | **DP2** |

## 7. Decisiones pendientes del usuario (ninguna bloquea; queda implementada la recomendada)

Las dos son diferencias **visibles** con la maqueta. Salen de la regla "el texto y el formato del escrito no cambian".

### DP1. Dos puntos en las etiquetas de las fichas interiores

La maqueta muestra `CARÁTULA` y `REFERENCIA DE LA ACTUACIÓN`, sin dos puntos. En la v4 el texto es `Carátula:` y `Referencia de la actuación:`.

- **A (recomendada):** se conservan los dos puntos (`CARÁTULA:`). El texto del escrito queda idéntico palabra por palabra y signo por signo, que es lo que exige el escenario "El contenido pericial es el mismo" y lo que compara el test contra la v4.
- B: se sacan los dos puntos. Es un cambio de texto mínimo, pero hay que relajar el test de contenido.

### DP2. Sangría de primera línea del cuerpo

Los párrafos del cuerpo de la v4 llevan una sangría de primera línea de 4 cm (`firstLine 2268`), que es convención de escrito judicial. La maqueta muestra los párrafos sin sangría.

- **A (recomendada):** se conserva. La HU dice "el diseño viste la estructura, no cambia el escrito" y nadie pidió tocarla.
- B: se saca. El cuerpo queda más "consultora", alineado con fichas y títulos.

---

## 8. Checklist atómico (`implementer-backend`)

**Regla dura de datos (AGENTS.md):** esta HU no escribe en MongoDB. No se generan informes sobre casos del usuario: las pruebas usan carpetas temporales propias. No se toca nada en `Storage:DataDirectory` (`dev-data/`); solo se lee para comparar hashes (B22). En `appsettings.Local.json` se borran **solo** las dos claves de color.

**Datos del cliente:** ni la v6, ni el script, ni los tests, ni el README, ni el progress contienen nombres, domicilio, teléfonos, el logo del estudio ni sus colores. Los tests usan imágenes PNG generadas en memoria y colores ficticios (`123456`, `ABCDEF`).

### 8.1 Plantilla

- [ ] B1. `ops/plantilla/build_plantilla_v6.py` (Python 3, solo biblioteca estándar). Uso: `build_plantilla_v6.py [--check] <v4.docx> <v6.docx>`. Importa `build_plantilla_v4` y `build_plantilla_v5` (5.1).
- [ ] B2. Huellas: `v5.fingerprint` + que los 7 separadores de 5.4 estén vacíos + `0386A4A4`/`0F4EDFCE`/`3B831F98` como hoy + `6A0EA424` empieza con "ANEXO" + `numId 2` → `abstractNum 1` y ningún otro `num` usa `abstractNum 1`. Que la v4 no contenga `2F6F12` ni `E8F3DF` (`FALLA: centinela-en-origen`).
- [ ] B3. Portada (5.3): P1–P5, ficha T1, P6 con el `sectPr` de la sección 1, antes de `0DF4CF61`.
- [ ] B4. `sectPr` final (5.2): `top 1418`, `header 680`. `headerReference` → `header1`.
- [ ] B5. `header1.xml` (tabla de texto), `header2.xml` (vacío), `footer1.xml` (línea + slot + Página N de M), `footer2.xml` (identidad con los dos bloques). Rels `rIdHdr2`/`rIdFtr2` y overrides en `[Content_Types].xml`, como la v5.
- [ ] B6. Cuerpo (5.4): título del escrito, 13 grupos de título, numeración, separadores, 3 fichas, tabla de hashes, anexo y cierre.
- [ ] B7. Estilos y fuentes (5.4.4).
- [ ] B8. `--check` de la v6 (solo `OK` o `FALLA:<tipo>`):
  - placeholders por parte: cuerpo = `v4.BODY_PLACEHOLDERS ∪ {"{ISOTIPO_ORGANIZACION:2x2}"}`, bloques `v4.BODY_BLOCKS ∪ {ISOTIPO}`; `header1` = `{tipoCausa}`, `{numeroCausa}`, `{ORGANIZACION}`, sin bloques; `footer1` = `{ATRIBUCION_FACTUM}`, sin bloques; `footer2` = `{LOGO_ORGANIZACION:4.5x2.5}`, `{ORGANIZACION}`, `{CONTACTO}` + bloques `MEMBRETE`, `MEMBRETE_CON_LOGO`, `MEMBRETE_SIN_LOGO`; `header2` sin placeholders. Cada placeholder entero en un `<w:t>` y cada bloque abre y cierra una vez por parte;
  - dos `sectPr`, cada uno con su header y footer default hacia las partes de 5.2, sin `titlePg`; el segundo con `top 1418` y `header 680`;
  - **ninguna** parte contiene `wp:anchor`, `wps:wsp` ni `v:shape`;
  - el centinela `2F6F12` está en `numbering.xml` y en `document.xml` (15 filetes con `pBdr/top` verde = 13 de título + portada + cierre; 2 `tcBorders/bottom` verdes en la tabla de hashes). No está en ningún header ni footer. `E8F3DF` no está en ninguna parte;
  - exactamente 11 párrafos con `numId 2`, todos sin `<w:t>`. Ninguno de los 13 títulos lleva `numPr`;
  - `0DF4CF61` sin `<w:u `; la fila de encabezado de la tabla de hashes sin `w:shd`;
  - `styles.xml` con `3D444C` y Arial; no queda `Times New Roman` en `document.xml`, `styles.xml`, `header*.xml` ni `footer*.xml`; `fontTable.xml` con Century Gothic;
  - XML bien formado, `w14:paraId` únicos, sin `w:author`, `core.xml` con autor `Factum`;
  - `PAGE` y `NUMPAGES` en `footer1`.
- [ ] B9. Generar `server/src/Factum.Backend/Templates/plantilla_informe_v6.docx`. Correr el build dos veces y comprobar el mismo SHA-256 (determinismo).
- [ ] B10. `git rm server/src/Factum.Backend/Templates/plantilla_informe_v5.docx`. Regenerar la v5 **en el scratchpad** con `build_plantilla_v5.py` (sin cambios) y confirmar `--check` en `OK` y el SHA-256 `7c672e325f7d97dc81e47409f8e913940088dca3beb8b272df52d309dc44df1c` del progress de la v5. Eso prueba que el script importado sigue intacto.
- [ ] B11. El `--check` de la v4 sigue en `OK` (si está `docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx`; si no está, dejarlo anotado).
- [ ] B12. `ops/plantilla/README.md`: la cadena pasa a ser "plantilla del usuario → v4 → v6" (la v5 queda reproducible), con una sección v6 (qué hace, uso, `--check`, render). Se actualiza la tabla de plantillas.

### 8.2 Código

- [ ] B13. `BrandingColors.cs` (5.7): defaults de Factum, `InkColor`, `MinAccentContrastWithInk`, `Contrast(a, b)`, `ContrastWithWhite` en función de `Contrast`, comentarios.
- [ ] B14. `BrandingService.cs`: `NormalizeAccent` con el chequeo contra la tinta y su warning.
- [ ] B15. `BrandingOptions.cs`: comentarios.
- [ ] B16. `BrandColors.cs`: comentarios (v6 y valores de los centinelas). La lógica no cambia.
- [ ] B17. `ReportService.cs` (5.6): plantilla v6, `BlockKeys` y `conditions`, `KnownPlaceholders`, B-R5 con tinte, B-R8 con slot (`BuildAttributionRuns`, `TryFillAttributionSlot`) y comentarios.
- [ ] B18. `appsettings.Local.json`: borrar `Branding:PrimaryColor` y `Branding:AccentColor`, sin otros cambios. Después: `git status` sin ese archivo, JSON válido (`python3 -c "import json;json.load(open(...))"`) y un listado de las claves que quedan en `Branding`, **sin valores**, que tiene que dar `OrganizationName`, `ContactLines` y `OrganizationLogo`.
- [ ] B19. `README.md` (raíz): tabla de Branding (defaults de Factum, validación del acento contra la tinta, el acento es el tinte de la fila del ZIP, el isotipo va solo al cierre) y tabla de placeholders (`{ATRIBUCION_FACTUM}` como slot de B-R8, `MEMBRETE_CON_LOGO`/`MEMBRETE_SIN_LOGO`, `{LOGO_ORGANIZACION:4.5x2.5}` en la portada, sin `NOMBRE_EN_BANDA`). Referencias v5 → v6 (línea ~333).

### 8.3 Tests (`server/tests/Factum.Backend.Tests/`)

- [ ] B20. `BrandingColorsTests.cs`:
  - `DefaultPrimary == "2F6F12"`, `DefaultAccent == "E8F3DF"`, `ContrastWithWhite(DefaultPrimary) >= 4.5` y `Contrast(DefaultAccent, InkColor) >= 4.5`;
  - `Contrast(a, b) == Contrast(b, a)` y `Contrast("000000", "FFFFFF")` ≈ 21;
  - `AccentColor`: `"#abcdef"` → `"ABCDEF"`, `"#FFFF00"` → `"FFFF00"` (claro: pasa contra la tinta; corregir el comentario), **`"#123456"` → `DefaultAccent`** (oscuro), `"nada"` y `""` → default;
  - se mantienen los de `Normalize`, `PrimaryColor`, isotipo y snapshot de 3 argumentos (ahora con la paleta de Factum).
- [ ] B21. `ReportDesignTests.cs`: `V6 = "plantilla_informe_v6.docx"` reemplaza a `V5` (default de `Generate`). Se mantienen el armado del caso y la limpieza **solo** del directorio temporal propio. Casos:
  1. **Estructura:** 2 `SectionProperties` (la primera dentro de un `ParagraphProperties`, la segunda la última del body), header y footer default hacia partes distintas, sin `TitlePage`.
  2. **Sin formas flotantes:** cero `DWP.Anchor` en el document, en todos los headers y en todos los footers.
  3. **Pies:** cada uno de los dos footers contiene "Realizado con Factum" **una sola vez**. En el de la sección 2, la atribución y los `FieldCode` `PAGE`/`NUMPAGES` están en el **mismo** párrafo y no queda `{ATRIBUCION_FACTUM}`. El de la sección 1 no tiene `PAGE`.
  4. **Encabezado interior:** el header de la sección 2 contiene "Informe pericial técnico informático · `<tipoCausa>` N° `<nro>`" y, con nombre configurado, el `OrganizationName`. No tiene `Drawing`. El header de la sección 1 no tiene texto.
  5. **Títulos:** en el body hay 11 párrafos con `NumberingId == 2`, ninguno con texto propio. Cada uno de los 13 títulos (por texto, con `Trim`) no tiene `NumberingProperties`, tiene sangría izquierda 0 o ausente, y el párrafo siguiente es un filete (`ParagraphBorders.TopBorder` no nulo). Con `NotasTecnicas` y `Reserva` vacíos, hay 9 párrafos numerados.
  6. **Colores configurados** (`123456` / `ABCDEF`): ninguna parte (document, headers, footers, numbering, styles) contiene `2F6F12` ni `E8F3DF`. `123456` aparece en document y en numbering. Las celdas de la fila del ZIP tienen `Shading.Fill == "ABCDEF"`.
  7. **Sin Branding** (`new BrandingSnapshot(null, [], null)`): ningún texto de ninguna parte matchea `\{[#/]?[A-Za-z_]`. No hay imágenes `logo-organizacion*` ni `isotipo-organizacion*`. El texto del footer de la portada es solo "Realizado con Factum". `2F6F12` está en document y numbering, y la fila del ZIP tiene `Shading.Fill == "E8F3DF"`.
  8. **Con logo:** hay un drawing `logo-organizacion*` solo en el footer de la sección 1, con extent ≤ 4.5 × 2.5 cm (1 620 000 × 900 000 EMU), y ese footer tiene una `Table`. **Sin logo y con nombre:** el footer de la portada no tiene `Table` y contiene el nombre.
  9. **Con isotipo:** el drawing `isotipo-organizacion*` está solo en el body (cierre) y en ningún header.
  10. **Tabla de hashes:** filas = 1 encabezado + N archivos + 1 ZIP. Las celdas del encabezado no tienen `Shading` y su `BottomBorder` tiene `Color` = primario configurado. Los runs del hash usan `Courier New`. Solo la fila del ZIP tiene `Shading` y contiene "Contenedor de la evidencia".
  11. **Título del escrito:** ningún run del párrafo que empieza con "INFORME PERICIAL TÉCNICO INFORMÁTICO" en el body tiene `Underline`.
  12. **Contenido idéntico:** como el caso 10 de la v5, con v4 vs **v6** (texto desde `0DF4CF61`, en la v6 después del primer `sectPr`, normalizado sin espacios) **iguales**.
  13. **Validez:** `OpenXmlValidator(Office2019)` sin errores **nuevos** respecto de la v4 (misma clave `Part + Id + XPath` sin índices).
  14. **Muestras** (`FACTUM_RENDER_DIR`): como la v5, con cuatro DOCX: sin Branding; nombre y contacto sin logo; logo, nombre y contacto con la paleta por defecto; y logo + isotipo con colores ficticios.
- [ ] B22. `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` y `dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj` en verde, **todos** los tests incluidos (`ReportValuesIntegrantesTests`, `EvidenceZipTests`, etc.).

### 8.4 Verificación visual y de no-regresión

- [ ] B23. **Hashes existentes (solo lectura):** igual que B23 de la v5. Con `mongosh` sobre `factum_dev`, listar los casos `Completed` con `report_hash`/`zip_hash` y sus archivos en `dev-data/`, y calcular `shasum -a 256` antes y después: tienen que ser idénticos y coincidir con lo guardado. No se escribe nada.
- [ ] B24. **Render:** generar las muestras de B21.14 en el scratchpad y convertirlas con `soffice --headless --convert-to pdf --outdir <scratchpad>`. Mirar la portada, la primera interior, una página con títulos seguidos (Notas y Reserva vacías o cortas), la tabla de hashes (fila del ZIP con tinte), el cierre y el anexo, y comparar contra la maqueta A. En particular:
  - el romano se ve arriba de cada título (D6: si no se ve, `blocked`);
  - los filetes miden ~3 cm (portada) y ~1 cm (títulos);
  - el encabezado y el pie quedan en una línea cada uno;
  - la atribución sale en todas las páginas, una sola vez;
  - no hay huecos sin Branding.

  Los PDF no se versionan.
- [ ] B25. Arrancar el backend con la config local (después de B18) y confirmar en el log `Branding: colores primario 2F6F12 y acento E8F3DF`, sin warnings nuevos.
- [ ] B26. `progress/impl_backend_informe-diseno-v6.md`: archivos tocados, salida de los `--check` (v6, v5 regenerada, v4), build y test, hash de determinismo, resultado de B23, descripción del render de B24 (sin datos del cliente) y resultado de B25.

**No tocar** `backlog.json` ni `progress/current.md`. No tocar `client/`, `agent-ui/` ni `server/src/Factum.Agent`.

---

## 9. Verificación

| Quién | Qué corre antes de declararse `done` |
|---|---|
| `implementer-backend` | `python3 ops/plantilla/build_plantilla_v6.py --check server/src/Factum.Backend/Templates/plantilla_informe_v4.docx server/src/Factum.Backend/Templates/plantilla_informe_v6.docx` → `OK`; B10 (v5 regenerada en el scratchpad → `OK` y mismo SHA); B11; `dotnet build server/src/Factum.Backend/Factum.Backend.csproj`; `dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj`; B23, B24 y B25 |
| orquestador | `./ops/harness/verify.sh` |

**Prueba manual para el usuario** (backend reiniciado después de B18):

1. Generar el informe de un caso de prueba **nuevo** (no el cargado a mano) y abrirlo en **Microsoft Word (macOS)**. Tiene que abrir sin aviso de reparación. La portada no tiene franjas: muestra el filete verde, "Informe pericial / técnico informático" grande, el subtítulo, la ficha (causa, carátula, perito, fecha) con líneas grises y, abajo, el logo más chico con el nombre y el contacto, más "Realizado con Factum".
2. Página 2 en adelante: arriba hay texto chico gris con la causa y el nombre del estudio a la derecha, sobre una línea fina, **sin banda ni cuadro**. El título del escrito no tiene subrayado. Cada sección tiene su romano grande en verde arriba del título y un filete verde corto debajo. Todos los títulos arrancan en el mismo margen.
3. Referencia de la actuación, Identificación y Elementos ofrecidos se ven como fichas a todo el ancho (DP1: las etiquetas conservan los dos puntos).
4. Tabla de hashes: encabezado gris sin relleno, con una línea verde; la fila del ZIP con fondo verde muy suave y la leyenda "Contenedor de la evidencia". Si la tabla pasa de página, el encabezado se repite.
5. Pie: línea fina, "Realizado con Factum" a la izquierda y "Página N de M" a la derecha (la primera interior dice "Página 2 de M").
6. Cierre: la firma igual que antes y un filete verde corto centrado debajo.
7. Editar un título, un valor de una ficha y la carátula de la portada: nada se mueve ni se desarma.
8. Abrir el mismo archivo en **LibreOffice** y en **WPS**: la misma estructura, con diferencias menores de tipografía. Los romanos se ven.
9. Imprimir o exportar en escala de grises: todo se lee y la fila del ZIP se distingue por la negrita y la leyenda.
10. Descargar un informe generado **antes** de esta HU: es el mismo archivo y su hash coincide con el de la tarjeta del caso.
11. Decidir DP1 y DP2 mirando el render.
12. Opcional (D2 de la HU): si conseguís el logo en PNG con fondo transparente, apuntá `Branding:OrganizationLogo` a ese archivo y reiniciá. Es solo config.

---

## Anexo A. Fragmentos de referencia

### A.1 Grupo de título numerado (resultado esperado)

```xml
<w:p w14:paraId="(v6:num:0BF0C8CD)"><w:pPr><w:keepNext/>
  <w:numPr><w:ilvl w:val="0"/><w:numId w:val="2"/></w:numPr>
  <w:spacing w:before="360" w:after="0" w:line="240" w:lineRule="auto"/>
  <w:ind w:left="0" w:firstLine="0"/><w:contextualSpacing w:val="0"/><w:jc w:val="left"/>
  <w:rPr>{CG}<w:color w:val="2F6F12"/><w:sz w:val="40"/><w:szCs w:val="40"/></w:rPr></w:pPr></w:p>
<w:p w14:paraId="0BF0C8CD"><w:pPr><w:keepNext/>
  <w:spacing w:before="120" w:after="120" w:line="240" w:lineRule="auto"/>
  <w:ind w:left="0" w:firstLine="0"/><w:contextualSpacing w:val="0"/><w:jc w:val="left"/>
  <w:rPr>{TITULO}</w:rPr></w:pPr>
  <w:r><w:rPr>{TITULO}</w:rPr><w:t>OBJETO DEL INFORME</w:t></w:r></w:p>
<w:p w14:paraId="(v6:fil:0BF0C8CD)"><w:pPr><w:keepNext/>
  <w:pBdr><w:top w:val="single" w:sz="18" w:space="0" w:color="2F6F12"/></w:pBdr>
  <w:spacing w:before="0" w:after="180" w:line="40" w:lineRule="exact"/>
  <w:ind w:left="0" w:right="8107"/><w:rPr><w:sz w:val="2"/><w:szCs w:val="2"/></w:rPr></w:pPr></w:p>
```

`{TITULO}` = `{ARIAL}<w:b/><w:bCs/><w:caps/><w:color w:val="0E1013"/><w:spacing w:val="20"/><w:sz w:val="22"/><w:szCs w:val="22"/>`, en el orden de `RPR_ORDER`.

### A.2 Sección de la portada (en el `pPr` del párrafo P6)

```xml
<w:sectPr>
  <w:headerReference w:type="default" r:id="rIdHdr2"/>
  <w:footerReference w:type="default" r:id="rIdFtr2"/>
  <w:type w:val="nextPage"/>
  <w:pgSz w:w="11906" w:h="16838"/>
  <w:pgMar w:top="1440" w:right="1558" w:bottom="1417" w:left="1701"
           w:header="284" w:footer="567" w:gutter="0"/>
  <w:cols w:space="708"/>
  <w:docGrid w:linePitch="360"/>
</w:sectPr>
```

### A.3 Conversión de la maqueta (referencia)

| Maqueta (px) | Documento |
|---|---|
| filete de portada 112 × 6 | 1680 dxa × sz 36 (4.5 pt) |
| título de portada 46, line-height 1.12 | 34 pt (sz 68), línea exacta 760 |
| subtítulo 17 | 13 pt (sz 26) |
| separación título → ficha 110 | `after` 1650 |
| fila de ficha de portada, padding 11 | cellMar top/bottom 165 |
| etiqueta 10–10.5, `.12em` | 8 pt, interletrado 20 |
| número 26 | 20 pt (sz 40) |
| título de sección 12.5 negrita, `.1em` | 11 pt (ajustado a la HU), interletrado 20 |
| filete de título 36 × 3 | 540 dxa × sz 18 |
| fila de ficha interior, padding 7 | cellMar top/bottom 105 |
| encabezado y pie 10.5 | 8 pt |
| logo 150 × 82 | caja 4.5 × 2.5 cm (lo pidió el usuario) |

## Resolución de decisiones (2026-10-01)

- **DP1 → A** y **DP2 → A**, confirmadas por el usuario: se conservan los dos puntos de las etiquetas y la sangría de primera línea de la v4.
- Fallback de D6: si el romano en párrafo propio no se muestra en algún procesador, devolver `blocked` (se consulta al usuario antes de aplicar "número en línea").
