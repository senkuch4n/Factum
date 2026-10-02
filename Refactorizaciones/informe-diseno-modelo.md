# SDD: Diseño visual del informe pericial según el modelo del estudio

**Slug:** `informe-diseno-modelo`
**HU:** `docs/hu-informe-diseno-modelo.md`. El usuario validó las 11 dudas en la opción **recomendada**. Todavía no hay isotipo (rige el fallback de D4) y no hay reglas de formato del tribunal.
**Implementa:** solo `implementer-backend` (Opus), en `server/src/Factum.Backend`, `server/tests/Factum.Backend.Tests`, `ops/plantilla/`, `README.md` y `.gitignore`. **No hay trabajo de frontend.**
**Secuencia:** se implementa **después** de que `formulario-caso-catalogos` quede aprobada. La rama sale de esa rama (ramas encadenadas, ver `AGENTS.md`). Los puntos de contacto están en la sección 9.

---

## 1. Resumen funcional

El informe DOCX pasa a tener una **portada** propia y un **diseño interior** con la estética del modelo, sin cambiar una palabra del contenido pericial. La portada es una sección aparte del documento. Lleva el título "Informe Pericial Técnico Informático" en grande, la causa, la carátula, el perito con su matrícula y la fecha de inspección. Abajo van el logo completo, el nombre y el contacto del estudio, y a la derecha una franja vertical del color primario, a sangre, con un remate de tres bandas en el color de acento. Las páginas interiores (la segunda sección) tienen arriba una **banda** del color primario, a sangre, con "INFORME PERICIAL TÉCNICO INFORMÁTICO", la causa y, a la derecha, el isotipo o, si no hay isotipo, el nombre del estudio. Al pie llevan "Página N de M". Los títulos de sección quedan en Century Gothic, mayúsculas y color primario, con una línea fina debajo. Los tres bloques de datos cortos pasan a dos columnas, con el título a la izquierda y las filas etiqueta/valor separadas por líneas de color. La tabla de hashes tiene el encabezado sombreado y líneas horizontales. El cuerpo pasa a Arial y los hashes siguen en Courier New. "Realizado con Factum" sigue en el pie de **todas** las páginas. Los colores (`Branding:PrimaryColor` y `Branding:AccentColor`) y el isotipo (`Branding:OrganizationIsotype`) son configuración local. El default versionado es neutro (gris pizarra y gris claro). Los informes ya generados, sus archivos y sus hashes no se tocan.

## 2. Toca

| Lado | ¿Toca? | Qué |
|---|---|---|
| backend (API) `server/src/Factum.Backend` | **sí** | `Services/Branding/*` (colores e isotipo), `Services/Reports/ReportService.cs` (plantilla v5, pasada de colores, isotipo, bloques nuevos, fuente Arial), `Templates/plantilla_informe_v5.docx` (nueva, generada), `appsettings.json` (claves vacías nuevas) |
| backend (Tatana) `server/src/Factum.Agent` | **no** | — |
| client `client/` | **no** | D4 de la HU deja la web como está. El isotipo **no** se expone en `GET /api/config/public` y `organization_logo_url` no cambia. Lo verifiqué: `client/src/hooks/usePublicConfig.ts` y `client/src/lib/api.ts` solo leen `organization_logo_url`. |
| agent-ui `agent-ui/` | **no** | — |
| otros | **sí** | `ops/plantilla/build_plantilla_v5.py` (nuevo) y `ops/plantilla/README.md`; `server/tests/Factum.Backend.Tests/` (tests nuevos); `README.md` (Branding y placeholders); `.gitignore` (D11) |

**No hay Contrato compartido:** ningún campo nuevo cruza a `client/` ni a Tatana.

---

## 3. Modelo de datos

**Sin cambios en MongoDB.** No hay campos, colecciones, índices ni migraciones. Los casos `Completed` no se regeneran (409, como hoy). Sus `zip_hash` y `report_hash` y sus archivos en `Storage:DataDirectory` quedan byte a byte iguales. Un caso en borrador que se genere después del deploy sale con el diseño nuevo.

### 3.1 Configuración (sección `Branding`)

| Clave | Tipo | Default versionado | Validación (al arrancar, en `BrandingService`) |
|---|---|---|---|
| `Branding:PrimaryColor` | `string`, `#RRGGBB` o `RRGGBB` (sin distinguir mayúsculas) | `""` → `BrandingColors.DefaultPrimary = "2F3B4C"` (gris pizarra) | Hex inválido: warning `Branding: PrimaryColor '<valor>' no es un color #RRGGBB; se usa el default` y se usa el default. **Contraste con blanco < 4.5:1:** warning `Branding: PrimaryColor <hex> tiene contraste <x.x>:1 con blanco (mínimo 4.5:1); se usa el default` y se usa el default (D7). |
| `Branding:AccentColor` | igual | `""` → `BrandingColors.DefaultAccent = "9AA5B1"` (gris claro) | Hex inválido: warning y default. Sin chequeo de contraste: el acento es solo decoración y nunca texto (D7). |
| `Branding:OrganizationIsotype` | ruta a PNG/JPEG (absoluta o relativa al ContentRoot) | `""` → sin isotipo | Las mismas reglas que `OrganizationLogo` (≤ 1 MiB, 16–4096 px por lado, PNG o JPEG por contenido). Si no valida: warning `Branding: isotipo ignorado (<motivo>)` y se sigue sin isotipo. |

`appsettings.json` versionado: se agregan `"PrimaryColor": ""`, `"AccentColor": ""` y `"OrganizationIsotype": ""` dentro de `Branding`. Los colores del estudio **nunca** son default de código (escenario "Datos del estudio fuera del repo").

Para referencia de la config local de este estudio (no versionada; ver DP3), muestreé el logo así:

- Fondo marino en la zona central lisa: un marino oscuro (valor exacto solo en `appsettings.Local.json`). Los bordes tienen viñeteado y oscurecen todavía más.
- Dorado de las letras: mediana `#CCB684`, promedio `#D1B67A` (con brillos).
- Propuesta: `PrimaryColor` = el marino muestreado del logo (contraste > 13:1 con blanco) y `AccentColor` = el dorado muestreado del logo, ambos solo en `appsettings.Local.json`. El usuario los aprueba en el render.

---

## 4. Endpoints / WebSocket

**Ninguno nuevo ni modificado.** `GET /api/config/public` y `GET /api/config/branding/logo` siguen igual (el isotipo no se sirve a la web).

---

## 5. Diseño del documento (plantilla v5)

### 5.1 Dónde vive cada cosa (D1, D2)

| Pieza | Dónde |
|---|---|
| Estructura, estilos, tipografía, formas decorativas y placeholders | `Templates/plantilla_informe_v5.docx`, generada por `ops/plantilla/build_plantilla_v5.py` **a partir de `plantilla_informe_v4.docx`** (versionada y ya sin datos reales). No depende de la plantilla del usuario en `docs/`. |
| Colores configurables | La v5 lleva los **colores centinela** `2F3B4C` (primario) y `9AA5B1` (acento), que son los mismos defaults neutros, así la v5 abierta sola en Word se ve con la paleta neutra. `ReportService` los reemplaza en la generación (5.6). |
| Logo, isotipo, nombre y contacto | Placeholders resueltos por `ReportService` (5.7), como hoy el membrete. |
| "Realizado con Factum" | Sin cambios: lo agrega B-R8 en el pie de cada sección. |

`plantilla_informe_v4.docx` y `build_plantilla_v4.py` **no cambian**. El `--check` de la v4 tiene que seguir dando `OK` (escenario "Regresión"). La v4 queda como entrada de la v5 y como línea de base de los tests (7.3). El runtime usa solo la v5 (D10 de la HU: reemplazo).

### 5.2 Secciones y medidas (A4: 11906 × 16838 dxa = 7 560 310 × 10 692 130 EMU)

El documento tiene **dos secciones** (D3):

| | Sección 1: portada | Sección 2: interior |
|---|---|---|
| `sectPr` | en el `w:pPr` del **último párrafo de la portada**, `<w:type w:val="nextPage"/>` | el `sectPr` final del `body` (el de la v4, modificado) |
| `pgMar` (dxa) | top 1984, **right 3685**, bottom 1417, left 1701, header 284, footer 567 | top **1984**, right 1558, bottom 1417, left 1701, header **284**, footer 567 |
| Ancho de texto | 6520 dxa (11.5 cm): el texto nunca llega a la franja, que empieza en 16.8 cm | 8647 dxa (sin cambio) |
| `headerReference default` | `header2.xml` (franja y remate) | `header1.xml` (banda; **reemplaza al membrete**) |
| `footerReference default` | `footer2.xml` (identidad del estudio) | `footer1.xml` ("Página N de M"; **reemplaza** el recuadro de número de página que traía la plantilla de origen) |
| `titlePg` / `evenAndOddHeaders` | no | no |

**Por qué dos secciones y no una sola con `titlePg`:** con márgenes propios, cualquier texto que el perito edite o agregue en la portada respeta la franja sin depender de sangrías por párrafo (escenario "Sigue siendo un DOCX editable"). B-R8 (`AddFactumAttributionFooter`) ya recorre todos los `sectPr`, incluidos los de párrafo, y agrega la atribución a `footer2` y a `footer1`. No hace falta tocarlo.

**Numeración:** `footer1` usa `PAGE` y `NUMPAGES`, que cuentan la portada. La primera página interior dice "Página 2 de M" (**DP1**). No se usa `SECTIONPAGES` ni campos anidados (`{= NUMPAGES - 1}`) porque LibreOffice y WPS los soportan mal.

### 5.3 Portada (cuerpo de la sección 1 + `header2` + `footer2`)

**Cuerpo** (párrafos nuevos, insertados **antes** de `0DF4CF61`):

| # | Contenido | Formato |
|---|---|---|
| P1 | vacío (espaciador) | `w:spacing w:line="3402" w:lineRule="exact"`, sz 2. Así el título baja unos 6 cm de forma portable (Word suprime el `before` en algunos casos). |
| P2 | `Informe` | Century Gothic 34 pt (sz 68), color primario (centinela), `keepNext`, interlineado exacto 820 |
| P3 | `Pericial Técnico` | igual |
| P4 | `Informático` | igual; `after` 480 |
| P5 | `{tipoCausa} N° {numeroCausa}` | Century Gothic 16 pt, primario, `after` 120 |
| P6 | `“{caratula}”` (comillas tipográficas) | Arial 13 pt, gris `404040`, `after` 600. Una carátula larga se parte en varias líneas. Como el bloque del estudio está en el **pie**, no puede empujar el logo a otra página (la carátula tiene como máximo 300 caracteres, unas 6 líneas en 11.5 cm). |
| P7 | `{nombrePerito} · M.P. {matriculaPerito}` | Arial 11 pt, `404040` |
| P8 | `{fechaInspeccion}` + **`sectPr` de la sección 1** | Arial 11 pt, `404040` |

Todos alineados a la izquierda. Solo usan placeholders que ya existen (D9 de la HU): **no hay placeholders nuevos de texto**, y titular, IMEI y partes no van en la portada.

**`header2.xml`:** un solo párrafo vacío (sz 2, spacing 0) con cuatro formas ancladas (5.5): la franja primaria y tres bandas de acento.

| Forma | x | y | cx | cy | Color |
|---|---|---|---|---|---|
| Franja | 6 048 248 | 0 | 1 512 062 | 9 072 130 | primario |
| Banda 1 | 6 048 248 | 9 252 130 | 1 512 062 | 360 000 | acento |
| Banda 2 | 6 048 248 | 9 792 130 | 1 512 062 | 360 000 | acento |
| Banda 3 | 6 048 248 | 10 332 130 | 1 512 062 | 360 000 | acento |

(Franja = 20 % del ancho, a sangre arriba, a la derecha y abajo. Entre las piezas quedan huecos blancos de 180 000 EMU. Es la decoración geométrica sobria de D6: **no** se copian las curvas del modelo.)

**`footer2.xml`** (de arriba hacia abajo; después B-R8 agrega la atribución al final):

```
{#MEMBRETE}
{LOGO_ORGANIZACION:7x3.8}      ← izquierda, spacing after 120
{ORGANIZACION}                 ← Century Gothic 11 pt negrita, primario
{CONTACTO}                     ← Arial 8 pt, gris 595959 (líneas con <w:br/>, como hoy)
{/MEMBRETE}
(párrafo vacío sz 2, spacing after 120)
```

- Sin Branding: `MEMBRETE` = false y el bloque desaparece entero. Queda solo la atribución (escenario "Portada sin Branding").
- El logo completo se muestra tal cual, como una "tarjeta" de 7 × 3.8 cm (la proporción del JPEG del estudio entra justa) **sin bloque de color alrededor** (**DP2**). Con el viñeteado, el borde del JPEG va de casi negro al marino, así que ningún bloque liso lo disimula. Y un recorte por código está fuera de alcance (D4).

### 5.4 Páginas interiores

**`header1.xml` (banda), reemplazo completo del membrete:**

1. Párrafo ancla (vacío, sz 2) con dos formas (5.5):
   - banda primaria: x 0, y 0, cx 7 560 310, cy 900 000 (2.5 cm, a sangre);
   - bloque de acento: x 6 570 980, y 0, cx 989 330 (= margen derecho), cy 900 000. Va dibujado encima de la banda (`relativeHeight` mayor).
2. Tabla sin bordes, `tblW` 8647 dxa, `tblLayout fixed`, márgenes de celda izquierda y derecha en 0, una fila con `<w:trHeight w:val="1134" w:hRule="exact"/>` (2.0 cm). Con `header` = 284, la fila ocupa de 0.5 a 2.5 cm, dentro de la banda. Las dos celdas tienen `vAlign center`:
   - **Celda izquierda (6247):** `INFORME PERICIAL TÉCNICO INFORMÁTICO` en Century Gothic 11 pt negrita, blanco `FFFFFF`, `w:spacing w:val="20"` (interletrado), y debajo `{tipoCausa} N° {numeroCausa}` en Arial 9 pt blanco.
   - **Celda derecha (2400), alineada a la derecha:**
     ```
     {#ISOTIPO}
     {ISOTIPO_ORGANIZACION:2.4x1.6}
     {/ISOTIPO}
     {#NOMBRE_EN_BANDA}
     {ORGANIZACION}            ← Century Gothic 9 pt, blanco
     {/NOMBRE_EN_BANDA}
     (párrafo vacío sz 2: la celda tiene que terminar en un párrafo)
     ```
3. Párrafo final vacío, sz 2.

El texto del cuerpo **nunca** queda tapado: el margen superior de la sección 2 (3.5 cm) es mayor que la banda (2.5 cm). Si el perito agranda el encabezado, Word y LibreOffice corren el cuerpo hacia abajo solos, porque el texto del header es de flujo, no flotante.

**`footer1.xml`:** un párrafo alineado a la derecha, Arial 8 pt, gris `5C656E`: `Página ` + campo `PAGE` + ` de ` + campo `NUMPAGES` (campos complejos `fldChar`, ver Anexo A.3). B-R8 agrega después la atribución centrada.

**Cuerpo de la sección 2** (sobre los párrafos de la v4, ubicados por `w14:paraId`):

| Qué | paraIds v4 | Cambio |
|---|---|---|
| Título del escrito | `0DF4CF61` | Century Gothic 14 pt negrita, primario. La alineación y el texto quedan igual. |
| Títulos de sección | `18A07FBA`, `0BF0C8CD`, `5B3056CF`, `0FF62E21`, `3A62DE0D`, `37BC776D`, `5A06ED76`, `390F7DAB`, `05D9340B`, `10E394C7` | rPr: Century Gothic 11 pt negrita, primario, `<w:caps/>` (el **texto no cambia**, la mayúscula es formato). pPr: `before` 360, `after` 120, `keepNext`, `pBdr/bottom` single sz 6 primario `space` 1. La numeración romana (`numId` 2) se conserva. |
| Numeración romana | `numbering.xml`, `abstractNum` 1, nivel 0 | `rPr` del nivel: Century Gothic negrita, primario |
| Bloque "Referencia de la actuación" | título `3B831F98` + filas `3A17DBEE`, `2AFB1345`, `15BB4A06`, `69BAC851`, `3DA3ACE8` | Tabla de dos columnas (5.4.1) |
| Bloque "Identificación" | título `0386A4A4` + filas `5F66E0AC`, `08C61010`, `3162E6AB` | Tabla de dos columnas |
| Bloque "Elementos ofrecidos" | título `0F4EDFCE` + intro `5FECA2F2` + filas `14341833`, `15EF193E`, `490B4C01`, `28E6822B`, `2E1C810A` | Tabla de dos columnas. La intro va en la primera fila de la derecha (`gridSpan` 2). `26DDBA6A` ("El mismo fue aportado…") y las capturas **quedan fuera** de la tabla, a todo el ancho, en el mismo orden. |
| Tabla de hashes | tabla única de la v4 | 5.4.2 |
| Cierre | después de la firma (`firma-caracter`), antes de `{#anexoCapturas}` | `{#ISOTIPO}` / `{ISOTIPO_ORGANIZACION:2x2}` (centrado, `before` 480) / `{/ISOTIPO}` |
| Textos largos, firma, anexo, capturas | — | Solo cambia la fuente (5.4.3). Siguen justificados y a todo el ancho. |

#### 5.4.1 Tablas de dos columnas (bloques de datos)

- `tblW` 8647, `tblLayout fixed`, grilla **2400 / 2200 / 4047**, `tblBorders` todos `nil`, `tblInd` 0.
- **Columna 1 (título):** celda con `vMerge restart` en la primera fila y `vMerge` (continue) en las demás. Contiene el párrafo de título **original** (mismo paraId, con el estilo de título de sección pero **sin** `pBdr`). `tcBorders` todos `nil`, `vAlign top`.
- **Columnas 2 y 3:** etiqueta y valor. El párrafo `**Carátula:** {caratula}` de la v4 se parte en dos párrafos: la etiqueta `Carátula:` (Arial 10 pt negrita, `333333`; párrafo con paraId nuevo) y el valor `{caratula}` (Arial 10 pt; conserva el paraId original). Se sacan `numPr` y viñeta. El texto, con los dos puntos, queda idéntico.
- Líneas: `tcBorders` `top` y `bottom` single sz 6 primario en las celdas de las columnas 2 y 3. `left`, `right` e `insideV` en `nil`. Márgenes de celda: top y bottom 60, left 108.
- Filas con `cantSplit`. Los párrafos de celda van con interlineado 240 y `after` 0.
- La primera fila de "Elementos ofrecidos" es la intro `5FECA2F2`, en una celda `gridSpan` 2, con el mismo borde arriba y abajo.
- **Párrafo vacío obligatorio** después de cada tabla (Word fusiona dos tablas contiguas). Se reutilizan los vacíos que ya siguen a cada bloque en la v4. Si no hay uno, se crea con paraId nuevo.

#### 5.4.2 Tabla de hashes

- `tblBorders`: `top`, `bottom` e `insideH` single sz 4 primario; `left`, `right` e `insideV` en `nil`. `tblStyle` se mantiene (los bordes directos ganan).
- Fila de encabezado (`04E11EDC` "NOMBRE", `4B698602` "HASH SHA-256"): `tcPr/shd` con `w:val="clear" w:color="auto" w:fill="<primario>"` y runs en Century Gothic 9 pt negrita blanco. `tblHeader` se conserva (se repite si la tabla pasa de página).
- Fila modelo: nombre en Arial 9 pt (hoy TNR 9) y hash en **Courier New 8 pt** (sin cambio). La grilla 3600/4691, `fixed` y `cantSplit` quedan como están.
- B-R5 (`FillHashTable`/`BuildHashRow`) **no cambia**: clona la fila modelo, y la línea "Origen:" hereda el rPr del nombre (pasa a Arial sola).

#### 5.4.3 Tipografía (D5 de la HU)

- `styles.xml`: `docDefaults` y estilo Normal (`styleId` 1) con `rFonts` Arial en ascii, hAnsi, eastAsia y cs, y **sz 22** (11 pt; hoy 24). Los runs sin `sz` propio heredan 11 pt.
- `document.xml`, `header*.xml` y `footer*.xml`: todo `w:rFonts` con `w:ascii="Times New Roman"` (con o sin `w:hint`) pasa a `<w:rFonts w:ascii="Arial" w:hAnsi="Arial" w:eastAsia="Arial" w:cs="Arial"/>`. Courier New queda tal cual.
- `fontTable.xml`: se agregan `Century Gothic` (`panose1` 020B0502020202020204, `family swiss`, `pitch variable`) y `Arial` si no está. Así LibreOffice y WPS sustituyen por una sans sin romper la grilla.
- `ReportService.BodyFont` = `"Arial"` (pies de foto de capturas).
- Interlineado del cuerpo: el de la v4 (360 = 1.5), sin cambios.

### 5.5 Formas decorativas (DrawingML)

Son rectángulos `wps:wsp` con `prstGeom rect`, relleno sólido `a:srgbClr` con el **centinela** y sin línea, anclados a la página con `behindDoc="1"`, `locked="1"`, `wrapNone` y `allowOverlap="1"`, envueltos en `mc:AlternateContent` / `mc:Choice Requires="wps"` **sin Fallback VML** (D4). No llevan texto adentro. Todo texto va en párrafos o tablas de flujo, que es lo que Word, LibreOffice y WPS respetan igual y lo que el perito puede editar sin mover nada. Los `docPr id` de la plantilla van de 9001 a 9099 (los de `ReportService` arrancan en 100 000). Ver el XML en el Anexo A.1.

### 5.6 Colores en la generación: pasada B-R2b

Va en `ReportService.GenerateDocxAsync`, después de B-R2 (bloques) y antes de B-R3:

```csharp
// B-R2b: colores de marca. La plantilla trae los centinelas (= defaults neutros).
BrandColors.Apply(mainPart, brand.PrimaryColor, brand.AccentColor);
```

```csharp
// Services/Reports/BrandColors.cs (nuevo, internal static, testeable)
internal static class BrandColors
{
    public const string PrimarySentinel = BrandingColors.DefaultPrimary; // "2F3B4C"
    public const string AccentSentinel  = BrandingColors.DefaultAccent;  // "9AA5B1"

    /// Reemplaza el valor de los atributos val/fill/color iguales (ignore case) a un centinela,
    /// en document, headers, footers, numbering y styles. Devuelve la cantidad de reemplazos.
    public static int Apply(MainDocumentPart main, string primary, string accent);
}
```

- Raíces: `main.Document`, cada `HeaderPart.Header`, cada `FooterPart.Footer`, `main.NumberingDefinitionsPart?.Numbering` y `main.StyleDefinitionsPart?.Styles`.
- Por cada elemento (`root.Descendants()` + root) y cada atributo con `LocalName` `val`, `fill` o `color` cuyo valor sea igual, sin distinguir mayúsculas, a un centinela: `element.SetAttribute(new OpenXmlAttribute(a.Prefix, a.LocalName, a.NamespaceUri, nuevo))`. Esto cubre `w:color/@w:val`, `w:shd/@w:fill`, `w:*Bdr/*/@w:color`, `w:tcBorders/*/@w:color` y `a:srgbClr/@val`, incluidos los que están dentro de `mc:AlternateContent`.
- Si `primary == PrimarySentinel` y `accent == AccentSentinel`, es un no-op (instalación sin colores).
- El script v5 garantiza que la v4 **no** contiene ninguno de los dos centinelas antes de agregarlos. Si los tuviera, el build aborta con `FALLA: centinela-en-origen`. Así el reemplazo nunca pisa un color ajeno.

### 5.7 Logo, isotipo y bloques: cambios en `ReportService`

- `TemplateFileName = "plantilla_informe_v5.docx"`. Se agrega un **constructor `internal`** con un parámetro `templateFileName`, que encadena al primario, solo para los tests (7.3). DI sigue usando el público.
- `BlockKeys` suma `"ISOTIPO"` y `"NOMBRE_EN_BANDA"`. En `conditions`:
  - `["ISOTIPO"] = brand.Isotype is not null`
  - `["NOMBRE_EN_BANDA"] = brand.Isotype is null && brand.OrganizationName is not null`
  - `MEMBRETE` sigue con la misma fórmula (ahora vive en `footer2`).
- `ReplaceOrganizationLogo` se generaliza a `ReplaceImagePlaceholder(owner, root, Regex placeholder, BrandingLogo? image, string baseName, double defW, double defH, ref drawId)` y se llama dos veces en B-R3:
  - `LogoPlaceholderRegex` con `brand.Logo` y `"logo-organizacion"` (5 × 1.5 de default, como hoy);
  - `IsotypePlaceholderRegex = \{ISOTIPO_ORGANIZACION(?::(\d+(?:[.,]\d+)?)x(\d+(?:[.,]\d+)?))?\}` con `brand.Isotype` y `"isotipo-organizacion"` (default 2 × 2).
  - Sin imagen: el párrafo queda vacío, como hoy. En la práctica eso no pasa, porque el bloque `ISOTIPO` ya lo borró.
- `IsKnownPlaceholder` acepta también `IsotypePlaceholderRegex` (match completo).
- `BodyFont = "Arial"`.
- **No cambian:** `GenerateAsync` (orden ZIP → hash → verificación → DOCX → hash del DOCX), `EvidenceZip`, B-R4/B-R5/B-R6/B-R7/B-R8/B-R9, `ReportValues` y `KnownPlaceholders` de texto.
- Comentarios de clase y de `GenerateDocxAsync`: actualizar v4 → v5 y sumar B-R2b.

### 5.8 Branding: cambios

```csharp
// Services/Branding/BrandingOptions.cs: propiedades nuevas
public string OrganizationIsotype { get; set; } = "";
public string PrimaryColor { get; set; } = "";
public string AccentColor { get; set; } = "";

// Services/Branding/BrandingService.cs: el record suma parámetros CON DEFAULT (ver 9, punto 2)
public sealed record BrandingSnapshot(
    string? OrganizationName, IReadOnlyList<string> ContactLines, BrandingLogo? Logo,
    BrandingLogo? Isotype = null,
    string PrimaryColor = BrandingColors.DefaultPrimary,
    string AccentColor = BrandingColors.DefaultAccent);

// Services/Branding/BrandingColors.cs (nuevo, public static)
public static class BrandingColors
{
    public const string DefaultPrimary = "2F3B4C";   // gris pizarra, 11.4:1 con blanco
    public const string DefaultAccent  = "9AA5B1";   // gris claro
    public const double MinPrimaryContrast = 4.5;
    /// "#1A2B3C" | "1A2B3C" → "1A2B3C"; cualquier otra cosa → null.
    public static string? Normalize(string? raw);
    /// Contraste WCAG 2.x entre el color y blanco (luminancia relativa sRGB).
    public static double ContrastWithWhite(string hex6);
}
```

- `LoadLogo` pasa a `LoadImage(configured, contentRoot, logger, label)`, con `label` "logo" o "isotipo" para el mensaje del warning. El isotipo usa las mismas constantes.
- Log de inicio: `Branding: colores primario {P} y acento {A}` (Information), más los warnings de 3.1.
- `ConfigController` **no cambia**.

---

## 6. Decisiones técnicas

| # | Decisión | ¿Usuario? |
|---|---|---|
| D1 | Plantilla **v5 nueva**, generada por `build_plantilla_v5.py` **desde la v4 versionada**. No se toca la v4 ni su script, así el `--check` de la v4 sigue en `OK` y la v5 se reconstruye sin la plantilla privada de `docs/`. | no |
| D2 | Colores por **centinelas = defaults neutros**, reemplazados en la generación (B-R2b). No se usa un tema de documento (`theme1.xml`): Word y LibreOffice no aplican igual los colores de tema a bordes y sombreados. | no |
| D3 | Portada como **sección propia** con márgenes propios (en lugar de `titlePg`). | no |
| D4 | Formas decorativas: rectángulos DrawingML (`wps`) anclados a la página, detrás del texto, bloqueados, **sin Fallback VML**. Word 2007 no muestra la decoración, pero el texto queda intacto. | no |
| D5 | Banda interior de 2.5 cm, título fijo "INFORME PERICIAL TÉCNICO INFORMÁTICO" + causa (D2 A de la HU). Isotipo, o nombre en texto si no hay (D4 B de la HU). Bloque de acento liso del ancho del margen derecho (D6 A). | no (HU) |
| D6 | Remate de la portada: tres bandas horizontales de acento al pie de la franja (geométrico sobrio, D6 A de la HU). | no (HU) |
| D7 | Un `PrimaryColor` con contraste < 4.5:1 con blanco se **rechaza** (warning y default), porque se usa en títulos sobre blanco y con texto blanco encima (escenario "Impresión en blanco y negro"). El acento no se chequea porque nunca es texto. | no (solo afecta una config inválida) |
| D8 | Cuerpo en Arial **11 pt** (D5 de la HU: "Arial 11-12"), títulos en Century Gothic y hashes en Courier New 8 pt. La paginación cambia respecto de la v4 y eso es esperado. | no (HU) |
| D9 | Bloques de datos en tablas de 3 columnas de grilla (título con `vMerge` / etiqueta / valor). La etiqueta conserva los dos puntos, así el texto concatenado queda idéntico. | no |
| D10 | Identidad del estudio en la portada: va en el **pie** de la sección 1 (queda siempre abajo y una carátula larga no la empuja). Logo de 7 × 3.8 cm. | no |
| D11 | Numeración `PAGE`/`NUMPAGES`, contando la portada. | **DP1** |
| D12 | Logo completo como "tarjeta" oscura, sin bloque de color que lo enmarque. | **DP2** |
| D13 | El isotipo no se expone a la web y `client/` no cambia (D4 de la HU). | no (HU) |
| D14 | Tests de estructura con OpenXML SDK sobre DOCX generados en una carpeta temporal, y comparación de texto contra la v4 como línea de base (7.3). Sin Mongo ni `Storage`. | no |
| D15 | Config local de este estudio (colores + ruta del logo), la deja lista el implementador. | **DP3** |

## 7. Decisiones pendientes del usuario (ninguna bloquea; queda implementada la recomendada)

### DP1. "Página N de M" cuenta la portada

- **A (recomendada):** la portada es la página 1, sin número visible, y la primera página interior dice "Página 2 de 7". Coincide con las hojas físicas del expediente y se ve igual en Word, LibreOffice y WPS.
- B: el interior arranca en "Página 1 de 6". Necesita `SECTIONPAGES` o un campo calculado, que LibreOffice y WPS no muestran bien.

### DP2. Logo completo en la portada

- **A (recomendada):** el JPEG tal cual, como tarjeta marina de 7 × 3.8 cm abajo a la izquierda, sobre blanco. El viñeteado del logo hace imposible un bloque de color que disimule el borde.
- B: si el estudio consigue el logo completo en PNG con fondo transparente, se apunta `OrganizationLogo` a ese archivo. Es solo config y no cambia código.

### DP3. Config local de este estudio

- **A (recomendada):** el implementador copia `docs/Logo_del_estudio` a `server/src/Factum.Backend/branding/logo-estudio.jpg` (carpeta ignorada por git) y **agrega** a `appsettings.Local.json` solo `Branding:OrganizationLogo`, `PrimaryColor` y `AccentColor` con los colores muestreados del logo (los valores exactos quedan solo en ese archivo). No toca las claves que ya están. Así el render de prueba sale con la identidad real.
- B: lo hace el usuario a mano con el ejemplo de 3.1.

---

## 8. Checklist atómico (`implementer-backend`)

**Regla dura de datos (AGENTS.md):** esta HU no escribe en MongoDB. No se generan informes sobre casos del usuario: las pruebas usan carpetas temporales propias. No se toca nada en `Storage:DataDirectory` (`dev-data/`). Solo se lee para comparar hashes (B23).

**Datos del cliente:** ni la v5, ni el script, ni los tests, ni el README pueden contener nombres, domicilio, teléfonos, el logo del estudio ni sus colores como default. Los tests usan imágenes PNG generadas en memoria y colores ficticios (`123456`, `ABCDEF`).

### 8.1 Plantilla

- [ ] B1. `ops/plantilla/build_plantilla_v5.py` (Python 3, solo biblioteca estándar, el mismo estilo que el v4: XML como texto, anclas por `w14:paraId` con huella no sensible, paraIds nuevos deterministas con la clase `Ids`, ZIP determinista). Uso: `build_plantilla_v5.py [--check] <v4.docx> <v5.docx>`.
- [ ] B2. Huellas: verificar las anclas de la v4 de la tabla de 5.4 (texto inicial o exacto, como hace `fingerprint` en el v4) y que la v4 **no** contenga `2F3B4C` ni `9AA5B1` (`FALLA: centinela-en-origen`).
- [ ] B3. Portada (5.3): P1–P8 antes de `0DF4CF61`, con el `sectPr` de la sección 1 en P8.
- [ ] B4. `sectPr` final (5.2): `headerReference` → `header1` (rIdHdr1, ya existe), `pgMar` top 1984 y header 284.
- [ ] B5. `header1.xml` reescrito como banda (5.4). `header2.xml` nuevo (franja y remate). `footer1.xml` reescrito (Página N de M, conservando el elemento raíz y los namespaces del original). `footer2.xml` nuevo (identidad). Rels `rIdHdr2`/`rIdFtr2` y overrides en `[Content_Types].xml`.
- [ ] B6. Títulos, numeración, tablas de dos columnas, tabla de hashes, cierre con isotipo (5.4, 5.4.1, 5.4.2).
- [ ] B7. Tipografía (5.4.3): `styles.xml`, reemplazo de TNR por Arial, `fontTable.xml`.
- [ ] B8. `--check` de la v5, que imprime solo `OK` o `FALLA:<tipo>`:
  - placeholders por parte: cuerpo = conjunto del v4 (`BODY_PLACEHOLDERS`) + bloques `BODY_BLOCKS ∪ {ISOTIPO}` + `{ISOTIPO_ORGANIZACION:2x2}`; `header1` = `{tipoCausa}`, `{numeroCausa}`, `{ISOTIPO_ORGANIZACION:2.4x1.6}`, `{ORGANIZACION}` + bloques `ISOTIPO`, `NOMBRE_EN_BANDA`; `footer2` = `{LOGO_ORGANIZACION:7x3.8}`, `{ORGANIZACION}`, `{CONTACTO}` + bloque `MEMBRETE`; `header2` y `footer1` sin placeholders. Cada placeholder entero en un `<w:t>` y cada bloque abre y cierra una vez por parte;
  - dos `sectPr`, cada uno con su header y footer default, apuntando a las partes de 5.2;
  - los centinelas aparecen en las cuatro formas de `header2`, en las dos de `header1`, en los títulos, en `numbering.xml` y en la tabla de hashes;
  - no queda `Times New Roman` en `document.xml`, `header*.xml`, `footer*.xml` ni `styles.xml`;
  - XML bien formado, `w14:paraId` únicos, sin `w:author`, `core.xml` con autor `Factum`;
  - `PAGE` y `NUMPAGES` en `footer1`.
- [ ] B9. Generar `server/src/Factum.Backend/Templates/plantilla_informe_v5.docx`. Correr el build dos veces y comprobar que el SHA-256 es idéntico (determinismo).
- [ ] B10. `python3 ops/plantilla/build_plantilla_v4.py --check "docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx" server/src/Factum.Backend/Templates/plantilla_informe_v4.docx` tiene que seguir en `OK` (la v4 no cambió).
- [ ] B11. `ops/plantilla/README.md`: sección v5 (qué hace, uso, `--check`, render), con la cadena "plantilla del usuario → v4 → v5".

### 8.2 Código

- [ ] B12. `BrandingColors.cs` (5.8).
- [ ] B13. `BrandingOptions`: `OrganizationIsotype`, `PrimaryColor`, `AccentColor`.
- [ ] B14. `BrandingService`: `BrandingSnapshot` con los parámetros nuevos **con default**, `LoadImage` con label, validación de colores y contraste (3.1), log de inicio.
- [ ] B15. `Services/Reports/BrandColors.cs` (5.6).
- [ ] B16. `ReportService` (5.7): v5, constructor interno, B-R2b, `ISOTIPO` y `NOMBRE_EN_BANDA`, `ReplaceImagePlaceholder` + `IsotypePlaceholderRegex`, `IsKnownPlaceholder`, `BodyFont` y comentarios.
- [ ] B17. `appsettings.json`: las tres claves vacías en `Branding`.
- [ ] B18. `.gitignore`: debajo de `docs/INFORME*`, agregar `docs/informe_modelo*` y `docs/Logo_del_estudio*` (D11 de la HU). Comprobar con `git check-ignore -v` que los dos archivos quedan ignorados por `.gitignore` y no solo por `.git/info/exclude`.
- [ ] B19. `README.md`: tabla de Branding (3 claves nuevas, defaults neutros, validación de contraste, isotipo), ejemplo de `appsettings.Local.json` con colores **ficticios**, tabla de placeholders (`{ISOTIPO_ORGANIZACION[:WxH]}`, bloques `ISOTIPO`/`NOMBRE_EN_BANDA`, `MEMBRETE` ahora en el pie de la portada) y referencias v4 → v5.

### 8.3 Tests (`server/tests/Factum.Backend.Tests/`)

- [ ] B20. `BrandingColorsTests.cs`:
  - `Normalize`: `"#1A2B3C"` → `"1A2B3C"`, `"1A2B3C"` → igual, `" #ABCDEF "` → `"ABCDEF"`, y `"#FFF"`, `"azul"`, `""`, `null`, `"#GGGGGG"` → null;
  - `ContrastWithWhite("FFFFFF")` ≈ 1, `("000000")` ≈ 21, y `DefaultPrimary` ≥ 4.5;
  - `BrandingService` (con `Options.Create`, un `IHostEnvironment` falso con ContentRoot temporal y `NullLogger`): `PrimaryColor` `"#FFFF00"` → `DefaultPrimary`, `"zzz"` → default, `"#123456"` → `"123456"`, y un `OrganizationIsotype` inexistente → `Isotype` null sin excepción.
- [ ] B21. `ReportDesignTests.cs`: genera DOCX reales con `ReportService` (constructor interno, `IReportSettings` falso con `EncryptZip=false`, `IBrandingService` falso, `NullLogger`), en un directorio temporal con 2 archivos de evidencia chicos, y los abre con `WordprocessingDocument`. Borra **solo** su directorio temporal. Casos:
  1. **Estructura:** el `body` tiene exactamente 2 `SectionProperties`. La primera está dentro de un `ParagraphProperties` y la segunda es la última del body. Las dos tienen `HeaderReference` y `FooterReference` default hacia partes **distintas**, y ninguna tiene `TitlePage`.
  2. **Pies:** los dos footers referenciados contienen "Realizado con Factum". El de la sección 2 tiene `FieldCode` con `PAGE` y con `NUMPAGES`. El de la sección 1 no tiene `PAGE`.
  3. **Banda:** el header de la sección 2 contiene "INFORME PERICIAL TÉCNICO INFORMÁTICO" y "`<tipoCausa>` N° `<nro>`" del caso, más ≥ 2 `DWP.Anchor`. El header de la sección 1 tiene ≥ 4 anchors y ningún texto.
  4. **Colores configurados** (`123456`/`ABCDEF`): ninguna parte (document, headers, footers, numbering, styles) contiene `2F3B4C` ni `9AA5B1` (ignore case). `123456` aparece en document, en ambos headers y en numbering, y `ABCDEF` en los dos headers. `BrandColors.Apply` devuelve > 0.
  5. **Sin Branding** (`new BrandingSnapshot(null, [], null)`): ningún texto de ninguna parte matchea `\{[#/]?[A-Za-z_]`. No hay imagen `logo-organizacion*` ni `isotipo-organizacion*`. El footer de la portada solo tiene la atribución (sin texto de organización). Los colores siguen en los defaults.
  6. **Nombre sin isotipo:** el header interior contiene el `OrganizationName` y no hay drawing `isotipo-organizacion` en el header ni en el body.
  7. **Con isotipo** (PNG de 64×64 generado en memoria): drawing `isotipo-organizacion*` en el header interior y en el body (cierre). El header **no** contiene el nombre de la organización.
  8. **Con logo:** drawing `logo-organizacion*` solo en el footer de la sección 1, con extent ≤ 7 × 3.8 cm (2 520 000 × 1 368 000 EMU).
  9. **Tabla de hashes:** filas = 1 encabezado + N archivos + 1 ZIP. Las celdas del encabezado tienen `Shading.Fill` = primario configurado. Los runs del hash usan `Courier New`.
  10. **Contenido idéntico (escenario "El contenido pericial es el mismo"):** se genera el mismo caso con la v4 (constructor interno con `"plantilla_informe_v4.docx"`) y con la v5. En los dos se toma el texto del body (`Text` en orden de documento) **desde `0DF4CF61` en adelante**: en la v5, todo lo posterior al párrafo que lleva el primer `sectPr`. Se normaliza con `Regex.Replace(s, @"\s+", "")` y se comprueba que son **iguales**.
  11. **Validez:** `new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc)` no devuelve **ningún error nuevo** respecto del DOCX generado con la v4 en el mismo test (comparar por `Id` + `Path.XPath`; ninguno en `header*`, `footer*` ni en los `sectPr`). Si la v4 ya trae errores de origen, se listan en el progress.
- [ ] B22. `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` y `dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj` en verde, **incluidos** los tests de `formulario-caso-catalogos` (en particular `ReportValuesIntegrantesTests`, que construye `BrandingSnapshot` con 3 argumentos).

### 8.4 Verificación visual y de no-regresión

- [ ] B23. **Hashes existentes (solo lectura):** con `mongosh` sobre `factum_dev`, listar los casos `Completed` con `report_hash`/`zip_hash` y sus archivos en `dev-data/`. Calcular `shasum -a 256` antes y después de la implementación: tienen que ser idénticos y coincidir con lo guardado. No se escribe nada.
- [ ] B24. **Render:** generar con un test o un `dotnet run` de consola en una carpeta temporal tres DOCX: (a) sin Branding, (b) con nombre y colores y sin isotipo (el caso real de hoy), y (c) con logo + isotipo de prueba. Convertir cada uno con `soffice --headless --convert-to pdf --outdir <scratchpad>` y **mirar las páginas** (portada, primera interior, tabla de hashes, cierre y anexo). Revisar que la banda no tape texto, que la franja no tape la portada y que la atribución salga en todas las páginas. Los PDF no se versionan.
- [ ] B25. (Si DP3 = A) copiar el logo a `branding/` y agregar las tres claves a `appsettings.Local.json`. Comprobar con `git status` que ninguno de los dos aparece.
- [ ] B26. `progress/impl_backend_informe-diseno-modelo.md`: archivos tocados, salida de los dos `--check`, build y test, hash del determinismo, resultado de B23 y capturas descriptas de B24 (sin datos del cliente).

**No tocar** `backlog.json` ni `progress/current.md`.

---

## 9. Puntos de contacto con `formulario-caso-catalogos`

Esta HU se implementa sobre el código final de esa (rama encadenada). Según su SDD (§5.1 y §6) y el `git status` actual, esa HU **no modifica `ReportService.cs` ni la plantilla**: en `ReportValues.cs` solo agrega un comentario. Lo que hay que reconciliar:

1. **`ReportValues.cs`:** esta HU **no lo toca**. `{fraseIntegracion}` sigue saliendo de `IntegrantesTribunal`, en el párrafo `03940239`, que la v5 solo cambia de fuente. Si al momento de implementar esa HU cambió `ReportValues`, no hay conflicto.
2. **`BrandingSnapshot`:** `ReportValuesIntegrantesTests.cs` (de esa HU) hace `new BrandingSnapshot(null, [], null)`. Por eso los parámetros nuevos van **al final y con default** (5.8). Si se agregaran sin default, ese test deja de compilar.
3. **`ReportService.cs`:** si para entonces esa HU lo hubiera tocado (no está previsto), se reaplican los cambios de 5.7 sobre su versión. Los cambios de esta HU están acotados a: constante de plantilla, constructor interno, `BlockKeys`, `conditions`, B-R2b, B-R3 generalizado, `IsKnownPlaceholder` y `BodyFont`.
4. **Tests:** las dos HU suman archivos distintos en `server/tests/Factum.Backend.Tests/`. Los de esa HU tienen que seguir en verde (B22).
5. **Prueba manual:** el informe de un caso con tres integrantes ("…con la integración de A, B y C…") tiene que salir con el diseño nuevo y la misma frase.

---

## 10. Verificación

| Quién | Qué corre antes de declararse `done` |
|---|---|
| `implementer-backend` | `python3 ops/plantilla/build_plantilla_v5.py --check server/src/Factum.Backend/Templates/plantilla_informe_v4.docx server/src/Factum.Backend/Templates/plantilla_informe_v5.docx` → `OK`; el `--check` del v4 (B10) → `OK`; `dotnet build server/src/Factum.Backend/Factum.Backend.csproj`; `dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj`; y B23 y B24 |
| orquestador | `./ops/harness/verify.sh` |

**Prueba manual para el usuario** (backend reiniciado con la config local de DP3):

1. Generar un informe de un caso de prueba nuevo (no el cargado a mano) y abrirlo en **Microsoft Word (macOS)**: abre sin aviso de reparación. La portada tiene título grande, causa, carátula, perito y fecha. Abajo están el logo, el nombre y el contacto del estudio, y a la derecha la franja marina con tres bandas doradas.
2. Página 2 en adelante: banda marina arriba con "INFORME PERICIAL TÉCNICO INFORMÁTICO", la causa y el nombre del estudio (no hay isotipo todavía). El texto empieza debajo de la banda. Al pie va "Página N de M" (la primera interior dice "Página 2 de M", DP1) y en todas las páginas, también en la portada, "Realizado con Factum".
3. Títulos de sección en mayúsculas, marinos, con línea fina. Referencia de la actuación, Identificación y Elementos ofrecidos en dos columnas, con líneas marinas. Tabla de hashes con encabezado marino, hash en monoespaciada, y el encabezado repetido si la tabla pasa de página.
4. Editar un párrafo del cuerpo y la carátula de la portada: no se mueve la banda ni la franja.
5. Abrir el mismo archivo en **LibreOffice** y en **WPS**: la estructura es la misma, con diferencias menores de tipografía (Century Gothic sustituida, D5 de la HU).
6. Imprimir o exportar en escala de grises: todo se lee.
7. Comparar el texto con un informe anterior del mismo tipo de caso: los datos, las frases, la tabla y la firma son iguales, y solo se agregó la portada.
8. Descargar un informe generado **antes** de esta HU: es el mismo archivo y su hash sigue coincidiendo con el que muestra la tarjeta del caso.
9. Aprobar o ajustar los colores (los de `appsettings.Local.json`) mirando el render. Ajustarlos es solo config, con reinicio del backend.
10. Cuando tengas el isotipo (PNG transparente), configurarlo en `Branding:OrganizationIsotype` y reiniciar: aparece en la banda (en lugar del nombre) y al cierre, debajo de la firma.

---

## Anexo A. Fragmentos de referencia

### A.1 Rectángulo anclado (forma decorativa)

```xml
<w:r><mc:AlternateContent><mc:Choice Requires="wps"><w:drawing>
  <wp:anchor distT="0" distB="0" distL="0" distR="0" simplePos="0" relativeHeight="251658240"
             behindDoc="1" locked="1" layoutInCell="1" allowOverlap="1">
    <wp:simplePos x="0" y="0"/>
    <wp:positionH relativeFrom="page"><wp:posOffset>0</wp:posOffset></wp:positionH>
    <wp:positionV relativeFrom="page"><wp:posOffset>0</wp:posOffset></wp:positionV>
    <wp:extent cx="7560310" cy="900000"/>
    <wp:effectExtent l="0" t="0" r="0" b="0"/>
    <wp:wrapNone/>
    <wp:docPr id="9001" name="Factum banda"/>
    <wp:cNvGraphicFramePr/>
    <a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
      <a:graphicData uri="http://schemas.microsoft.com/office/word/2010/wordprocessingShape">
        <wps:wsp>
          <wps:cNvSpPr/>
          <wps:spPr>
            <a:xfrm><a:off x="0" y="0"/><a:ext cx="7560310" cy="900000"/></a:xfrm>
            <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
            <a:solidFill><a:srgbClr val="2F3B4C"/></a:solidFill>
            <a:ln><a:noFill/></a:ln>
          </wps:spPr>
          <wps:bodyPr/>
        </wps:wsp>
      </a:graphicData>
    </a:graphic>
  </wp:anchor>
</w:drawing></mc:Choice></mc:AlternateContent></w:r>
```

La raíz de la parte tiene que declarar `mc`, `wp`, `wps`, `w14` y `mc:Ignorable="w14 w15 wp14"`. Conviene partir del elemento raíz de `footer1.xml` de la v4, que ya trae todos los namespaces, como hace hoy `build_header`. Cada forma tiene un `docPr id` único y un `relativeHeight` creciente para que el acento quede encima.

### A.2 Sección de la portada (en el `pPr` del último párrafo de la portada)

```xml
<w:sectPr>
  <w:headerReference w:type="default" r:id="rIdHdr2"/>
  <w:footerReference w:type="default" r:id="rIdFtr2"/>
  <w:type w:val="nextPage"/>
  <w:pgSz w:w="11906" w:h="16838"/>
  <w:pgMar w:top="1984" w:right="3685" w:bottom="1417" w:left="1701"
           w:header="284" w:footer="567" w:gutter="0"/>
  <w:cols w:space="708"/>
  <w:docGrid w:linePitch="360"/>
</w:sectPr>
```

### A.3 "Página N de M" (`footer1`)

```xml
<w:p><w:pPr><w:spacing w:before="0" w:after="40"/><w:jc w:val="right"/></w:pPr>
  <w:r><w:rPr>{ARIAL8GRIS}</w:rPr><w:t xml:space="preserve">Página </w:t></w:r>
  <w:r><w:rPr>{ARIAL8GRIS}</w:rPr><w:fldChar w:fldCharType="begin"/></w:r>
  <w:r><w:rPr>{ARIAL8GRIS}</w:rPr><w:instrText xml:space="preserve"> PAGE </w:instrText></w:r>
  <w:r><w:rPr>{ARIAL8GRIS}</w:rPr><w:fldChar w:fldCharType="separate"/></w:r>
  <w:r><w:rPr>{ARIAL8GRIS}</w:rPr><w:t>2</w:t></w:r>
  <w:r><w:rPr>{ARIAL8GRIS}</w:rPr><w:fldChar w:fldCharType="end"/></w:r>
  <w:r><w:rPr>{ARIAL8GRIS}</w:rPr><w:t xml:space="preserve"> de </w:t></w:r>
  <!-- igual con NUMPAGES -->
</w:p>
```

`{ARIAL8GRIS}` = `<w:rFonts w:ascii="Arial" w:hAnsi="Arial" w:cs="Arial"/><w:color w:val="5C656E"/><w:sz w:val="16"/><w:szCs w:val="16"/>`. El gris de la numeración **no** es centinela: no cambia con la marca.

## Resolución de decisiones (2026-10-01)

- **DP1 → A, DP2 → A, DP3 → A**, confirmadas por el usuario (numeración cuenta la portada; logo completo como tarjeta en la portada; el implementador configura branding/ y appsettings.Local.json sin tocar otras claves).
- Resto: las recomendadas.


> Nota del orquestador (2026-10-01): por la revisión 1, se reemplazaron en este documento los colores exactos del estudio por ejemplos genéricos (`#1A2B3C` / `#B0A080`); los reales viven solo en `appsettings.Local.json`.
