# SDD: Editor de texto enriquecido en las secciones del informe

**Slug:** `editor-texto-enriquecido`
**HU:** `docs/hu-editor-texto-enriquecido.md`. El usuario validó las 11 dudas en la opción **recomendada**: D1 A (Markdown), D2 A (marca por caso, texto viejo intacto), D2-UX A (WYSIWYG con atajos Markdown), D3 (negrita, cursiva, subrayado `<u>`, viñetas y numeradas hasta 3 niveles, código en línea y en bloque, cita, enlace, un nivel de subtítulo; sin tablas ni tachado), D4 (tabla de la HU), D5 A (solo las 8 secciones), D6 A (imágenes en la HU aparte `editor-imagenes-informe`), D7 (sin HTML salvo `<u>`; http/https/mailto; validación también en el servidor), D8 A (pegado conserva lo soportado; Ctrl/Cmd+Shift+V sin formato), D9 A (el tope de 20 000 cuenta el texto guardado), D10 A (`Report:DefaultTexts:*` se interpretan como Markdown).
**Base:** rama `feat/editor-texto-enriquecido`.
**Implementan:** `implementer-backend` (Opus) en `server/src/Factum.Backend` y `server/tests/Factum.Backend.Tests`; `implementer-frontend` (Opus) **solo en `client/`** (antes de tocar código hay que leer `client/AGENTS.md` y las guías de `client/node_modules/next/dist/docs/`). Pueden ir en paralelo: el frontend se guía por la **sección 4 (Contrato compartido)**, no por el código del backend. Para probar de punta a punta, el backend tiene que estar listo.

---

## 1. Resumen funcional

Las ocho secciones del paso 4 "Informe" pasan de `InputTextarea` a un editor WYSIWYG (Tiptap 3, headless, con el estilo de Factum). Tiene barra de formato, atajos de teclado y atajos Markdown (`**`, `- `, `1. `, `> `, `` ` ``). El texto se guarda como **Markdown acotado** ("dialecto Factum", sección 4.3) en los mismos ocho campos de `report_texts`. Se suma la marca por caso `report_texts.formato = "markdown"`. Un caso sin la marca es texto plano de antes de esta HU y se genera exactamente como hoy. Cuando el perito abre el paso 4 de un caso viejo, el cliente convierte el texto plano a Markdown escapando los caracteres especiales y pasando las líneas `"- "` a viñetas reales. Esa conversión **se guarda solo si el perito edita algo**. El backend valida el Markdown al guardar: rechaza HTML que no sea `<u>`, imágenes y enlaces que no sean http, https o mailto. Al generar el DOCX v6 "Filete", lo convierte a OpenXML con un recorrido propio del AST de Markdig: runs con `b`/`i`/`u`, listas reales de Word con numeración propia (sin tocar la romana de las secciones), código en Courier New, cita con filete gris, subtítulo en negrita e hipervínculos con la URL visible. Los textos por defecto, los del producto y los de `Report:DefaultTexts:*`, salen del servidor ya en Markdown. Las imágenes quedan fuera (HU `editor-imagenes-informe`); el diseño deja el punto de extensión marcado (D19).

## 2. Toca

| Lado | ¿Toca? |
|---|---|
| backend (API) `server/src/Factum.Backend` (+ `server/tests/Factum.Backend.Tests`) | **sí** |
| backend (Tatana) `server/src/Factum.Agent` | **no** |
| client `client/` | **sí** |
| agent-ui `agent-ui/` | **no** |

Plantillas `Templates/plantilla_informe_v4.docx` / `plantilla_informe_v6.docx` y `ops/plantilla/build_plantilla_v6.py`: **no cambian** (D6). `--check` sigue dando `OK` sin tocar nada.

---

## 3. Modelo de datos (backend)

> En Mongo los campos van en **PascalCase**: no hay `ConventionPack`. Lo confirmé en `factum_dev.cases` (`ReportTexts.OperacionesRealizadas`, …).
>
> Inspección de solo lectura de la base de desarrollo (2026-10-01): `factum_dev.cases` tiene 2 documentos, los dos con `ReportTexts` en texto plano, `Status: 2` (`Completed`) y **sin** `ReportTexts.Formato` (`countDocuments({"ReportTexts.Formato":{$exists:true}}) = 0`). Sus textos son los defaults de hoy, con líneas `"- "` y `Buenos_Aires` con guion bajo. No hay casos en borrador: la prueba manual de compatibilidad (sección 11) crea su propio caso.

### 3.1 Campo nuevo `ReportTexts.Formato`

```csharp
// Models/Case.cs, clase ReportTexts (ya tiene [BsonIgnoreExtraElements])
/// <summary>
/// Formato de los ocho textos (editor-texto-enriquecido). null = texto plano (casos guardados
/// antes de esta HU o por un cliente viejo): se generan con ReplaceParagraphPerLine, como siempre.
/// "markdown" = dialecto Factum (ReportMarkdown). Sin default: un documento viejo sin el campo
/// deserializa null. No hay migración.
/// </summary>
[BsonIgnoreIfNull]
public string? Formato { get; set; }
```

- Mongo: `ReportTexts.Formato` (`"markdown"` o ausente). `[BsonIgnoreIfNull]` hace que un guardado en texto plano **no agregue** el elemento, así el documento conserva la forma de hoy.
- JSON: `report_texts.formato` (`"markdown"` o `null`). Sale en `GET /api/cases/{id}`, en la respuesta de `PUT …/report-texts` y en `GET …/report-texts/defaults`.
- Constantes nuevas en `Services/Reports/ReportMarkdown.cs`: `ReportTextFormats.Markdown = "markdown"`, `ReportTextFormats.Texto = "texto"`, más `Normalize(string? v)`: `null`/`""`/`"texto"` → `null`, `"markdown"` → `"markdown"`, cualquier otro valor → inválido (400).

### 3.2 Compatibilidad y regla dura de datos

- **Ninguna migración ni escritura al desplegar.** Un documento solo cambia cuando el perito guarda el paso 4 de **su** caso en borrador, igual que hoy (el `$set` de `ReportTexts` completo, en `UpdateReportTextsAsync`).
- Un caso `Completed` no se edita ni se regenera (409, sin cambios). Sus ZIP, DOCX y PDF no se tocan.
- **Rollback del backend:** un binario anterior ignora `Formato` (`[BsonIgnoreExtraElements]`) y generaría los casos Markdown como texto plano (se verían `**` y `\_`). No se pierden datos. Queda documentado en la sección 8, D20.
- **Cliente viejo contra backend nuevo:** el `PUT` sin `formato` guarda texto plano (`Formato = null`) y el caso se genera como hoy.

---

## 4. Contrato compartido (client ↔ server)

Política JSON: `JsonNamingPolicy.SnakeCaseLower` (`Program.cs`, líneas 70-78).

### 4.1 Campo nuevo `formato`

| JSON | Tipo | Dónde viaja | C# | TS |
|---|---|---|---|---|
| `report_texts.formato` | `"markdown" \| null` | `GET /api/cases/{id}` (dentro de `report_texts`); respuesta de `PUT /api/cases/{id}/report-texts` | `Models/Case.cs` → `ReportTexts.Formato` (`string?`) | `client/src/lib/api.ts` → `ReportTexts.formato?: ReportTextFormat \| null` |
| `formato` (raíz del body) | `"markdown"` (el cliente nuevo **siempre** lo manda); el servidor también acepta ausente, `null`, `""` o `"texto"` (= texto plano) | Body de `PUT /api/cases/{id}/report-texts` | `DTOs/CaseDtos.cs` → `ReportTextsDto.Formato` (`string? = null`, último parámetro) | `client/src/lib/api.ts` → `ReportTextsRequest.formato: "markdown"` |
| `formato` (raíz de la respuesta) | `"markdown"` siempre | Respuesta de `GET /api/cases/{id}/report-texts/defaults` | `ReportTextsDto.Formato` | `client/src/lib/api.ts` → `ReportTextDefaults.formato: "markdown"` |

Los ocho campos de texto no cambian de nombre: `objeto_informe`, `operaciones_realizadas`, `aseguramiento_evidencia`, `resultados`, `valoracion_tecnica`, `conclusiones`, `notas_tecnicas`, `reserva` (más `updated_at` en la respuesta).

Tipos TS (en `client/src/lib/api.ts`, reexportados desde `client/src/types/index.ts`):

```ts
export type ReportTextFormat = "markdown";
/** Solo las ocho secciones (sin cambios: `keyof ReportTextsInput` sigue siendo la clave de sección). */
export interface ReportTextsInput { objeto_informe: string; /* …las 8, igual que hoy */ }
export interface ReportTextsRequest extends ReportTextsInput { formato: ReportTextFormat }
export interface ReportTextDefaults extends ReportTextsInput { formato: ReportTextFormat }
export interface ReportTexts extends ReportTextsInput {
  /** null/ausente = texto plano anterior a esta HU. "texto" no lo devuelve el servidor, pero se tolera. */
  formato?: ReportTextFormat | "texto" | null;
  updated_at?: string;
}
```

`api.getReportTextDefaults` devuelve `ReportTextDefaults` y `api.saveReportTexts(caseId, data: ReportTextsRequest)`.

### 4.2 Errores nuevos de `PUT …/report-texts` (400, mismo formato de error que hoy)

| Caso | Mensaje (`CaseValidation`) |
|---|---|
| `formato` con otro valor | `El campo formato no es válido` |
| Largo > 20 000 (sin cambios; cuenta el string guardado, D9 A) | `El campo {clave} supera los 20000 caracteres` |
| `formato = "markdown"` y un campo trae HTML que no es exactamente `<u>` o `</u>` (inline o bloque) | `El campo {clave} tiene contenido no permitido: HTML` |
| … una imagen `![…](…)` | `El campo {clave} tiene contenido no permitido: imagen` |
| … un enlace o autolink con un esquema que no es `http`, `https` ni `mailto` | `El campo {clave} tiene contenido no permitido: enlace` |

`{clave}` es la clave snake_case (`resultados`, …). Con texto plano (`formato` nulo) **no** se valida el contenido, como hoy: el camino de texto plano nunca interpreta nada.

### 4.3 Dialecto Markdown de Factum (lo que escribe el cliente y lee el servidor)

CommonMark sin GFM, más la etiqueta `<u>`. El cliente lo produce con `@tiptap/markdown` (marked) y overrides propios (sección 7.3). El servidor lo parsea con Markdig 1.4.0, pipeline base sin extensiones, **sin** el parser de código indentado y **sin** encabezados setext (sección 6.2).

| Elemento | Forma canónica que emite el cliente |
|---|---|
| Párrafo | Bloques separados por `\n\n` |
| Párrafo vacío (línea en blanco intencional) | `&nbsp;` como bloque propio (**todos** los vacíos, no solo del segundo en adelante) |
| Salto de línea dentro del párrafo (Shift+Enter) | `  \n` (dos espacios + `\n`) |
| Negrita / cursiva | `**x**` / `*x*` |
| Subrayado | `<u>x</u>` (lo de adentro **es Markdown**: `<u>a **b**</u>`). Nunca `++x++` |
| Código en línea | `` `x` `` (con fence más largo si el texto tiene backticks) |
| Bloque de código | Fence de backticks sin lenguaje; el fence es `max(3, racha de backticks más larga del contenido + 1)` |
| Cita | `> ` por línea |
| Subtítulo | `### x` (un solo nivel). En el DOCX, **cualquier** nivel `#…######` se trata como subtítulo |
| Viñetas | `- x`, anidadas con 2 espacios |
| Numerada | `1. x` (respeta `start`), anidado alineado al prefijo (`3. x\n   - y`) |
| Enlace | `[texto](url)`. Solo `http://`, `https://` y `mailto:`; espacios, `(`, `)`, `<` y `>` de la URL van percent-encoded |
| Texto literal | Se escapan con `\` los caracteres `` \ ` * _ [ ] ~ ``; `&`, `<`, `>` van como `&amp;`, `&lt;`, `&gt;`. Escape de inicio de línea: ver 4.4 |

No existen en el dialecto: tablas, tachado, HTML (salvo `<u>`), imágenes, línea horizontal y encabezados de más de un nivel.

### 4.4 Texto plano → Markdown (`plainToMarkdown` ↔ `ReportMarkdown.FromPlainText`)

Es la misma función en los dos lados, con el mismo resultado byte a byte. El cliente la usa al abrir un caso sin `formato`; el servidor la tiene como referencia y los tests la fijan con las fixtures de abajo.

- TS: `client/src/lib/report-markdown.ts` → `plainToMarkdown(text: string): string`
- C#: `server/src/Factum.Backend/Services/Reports/ReportMarkdown.cs` → `ReportMarkdown.FromPlainText(string? text)`

Reglas, en orden:

1. Limpieza igual a `ReportValues.Clean`: `\r\n` y `\r` → `\n`; `\t` → espacio; se sacan los caracteres de control `< 0x20` salvo `\n`.
2. Se parte por `\n`, se recorta el final de cada línea (espacios en blanco; `TrimEnd()` en C#, `/\s+$/u` en TS) y se descartan las líneas vacías del principio y del final.
3. Cada línea es un bloque:
   - Vacía → `&nbsp;`.
   - Empieza con `"- "` → ítem de viñeta. El contenido es lo que sigue a `"- "`, sin espacios al principio. Ítems consecutivos forman **una** lista.
   - Cualquier otra → párrafo.
4. Escape de cada contenido (párrafo o ítem), en este orden:
   1. `&` → `&amp;`, `<` → `&lt;`, `>` → `&gt;`.
   2. Se antepone `\` a cada `` \ ` * _ [ ] ~ ``.
   3. **Inicio de línea:** cada espacio inicial pasa a U+00A0 (así no se pierde la sangría y no se vuelve código). Si después la línea empieza con `\d{1,9}` seguido de `.` o `)` y luego espacio o fin, se inserta `\` antes del `.`/`)`. Si empieza con `#{1,6}` seguido de espacio o fin, con `-`, `+` o `*` seguido de espacio o fin, con `=+\s*$`, con `-+\s*$`, con `` `{3,} `` o con `~{3,}`, se antepone `\`.
5. Los ítems de una lista se unen con `\n` (`- a\n- b`); los bloques, con `\n\n`.

La misma función de escape (pasos 4.1-4.3) es la que aplica el override de párrafo del editor (sección 7.3) a cada línea de un párrafo. Así, texto convertido y texto escrito en el editor son iguales.

**Fixtures obligatorias** (strings en notación JSON; `\u00A0` = espacio duro). Las comprueban los tests xUnit; el frontend las verifica a mano y pega la salida en su progress:

| # | Entrada (texto plano) | Salida (Markdown) |
|---|---|---|
| F1 | `"archivo_de_prueba_1.txt"` | `"archivo\\_de\\_prueba\\_1.txt"` |
| F2 | `"2 * 3 = 6"` | `"2 \\* 3 = 6"` |
| F3 | `"Expte. #123"` | `"Expte. #123"` |
| F4 | `"# Título"` | `"\\# Título"` |
| F5 | `"1. Primero"` | `"1\\. Primero"` |
| F6 | `"Línea A\nLínea B"` | `"Línea A\n\nLínea B"` |
| F7 | `"A\n\nB"` | `"A\n\n&nbsp;\n\nB"` |
| F8 | `"Intro:\n- uno\n- dos\nCierre"` | `"Intro:\n\n- uno\n- dos\n\nCierre"` |
| F9 | `"{caratula} <b>x</b> & [y]"` | `"{caratula} &lt;b&gt;x&lt;/b&gt; &amp; \\[y\\]"` |
| F10 | `"   sangría"` | `"\u00A0\u00A0\u00A0sangría"` |
| F11 | `"> cita"` | `"&gt; cita"` |
| F12 | `"- 2. item"` | `"- 2\\. item"` |
| F13 | `"\n\nA\n\n"` | `"A"` |
| F14 | `` "`cmd`" `` | `` "\\`cmd\\`" `` |
| F15 | `"+ más\n---"` | `"\\+ más\n\n\\---"` |
| F16 | `"C:\\ruta"` | `"C:\\\\ruta"` |
| F17 | `""` | `""` |

### 4.5 "Vacío" para los obligatorios (`isBlankReportText` ↔ `ReportMarkdown.IsBlank`)

| `formato` | Regla (igual en los dos lados) |
|---|---|
| `null` / ausente (texto plano) | Igual que hoy: `IsNullOrWhiteSpace` / `!v.trim()` |
| `"markdown"` | Vacío si el **texto visible** no tiene **ninguna letra ni dígito** (`char.IsLetterOrDigit` / `/[\p{L}\p{N}]/u`). Cuenta como visible: los literales, el código en línea, las líneas de los bloques de código, el texto de los enlaces y la URL de un autolink. No cuentan: marcas, URLs de destino, entidades (`&nbsp;`…), `<u>`/`</u>` ni los números de las listas (D8) |

- TS: `client/src/lib/report-markdown.ts` → `isBlankReportText(value, formato)`. La usa `getMissingRequirements` (`client/src/lib/pericial.ts`).
- C#: `ReportMarkdown.IsBlank(string?)`, a través de `ReportTextRules.IsBlank(ReportTexts? t, string? value)`. La usan `CaseValidation.ValidateForGenerate` (`missing`) y las condiciones de bloque de `ReportService` (`objetoInforme`, `descripcionNotasTecnicas`, `descripcionReserva`).

La implementación TS es por regex (no carga Tiptap en `pericial.ts`), en este orden: quitar entidades `&(#\d+|#x[0-9a-f]+|[a-z][a-z0-9]*);` (sin distinguir mayúsculas); quitar `</?u>`; reemplazar el destino de los enlaces `](…)` por `]`; quitar los marcadores de lista numerada de inicio de línea `^(\s*(>\s*)*)\d{1,9}[.)](?=\s|$)` (multilínea); buscar `/[\p{L}\p{N}]/u`. El servidor es la autoridad: si hubiera una diferencia en un borde, gana el `missing` del servidor.

### 4.6 Claves de `missing`

Sin cambios: `report_texts.operaciones_realizadas`, `…aseguramiento_evidencia`, `…resultados`, `…valoracion_tecnica`, `…conclusiones`. Solo cambia la regla de "vacío" (4.5).

---

## 5. Endpoints (backend)

Ninguno nuevo. Cambian:

| Método y ruta | Cambio |
|---|---|
| `GET /api/cases/{id}/report-texts/defaults` | Responde `ReportTextsDto` con `formato: "markdown"` y los textos en el dialecto Factum (sección 6.5). |
| `PUT /api/cases/{id}/report-texts` | Lee `formato` (4.1), lo normaliza y valida (4.2). Guarda `ReportTexts.Formato`. Responde el `ReportTexts` guardado (con `formato`). |
| `GET /api/cases/{id}` | Sin cambios de código: `report_texts` serializa el campo nuevo. |
| `POST /api/cases/{id}/generate` | B-R4 elige el camino según `Formato` (sección 6.4). |

---

## 6. Backend: diseño

### 6.1 Archivos

| Archivo | Cambio |
|---|---|
| `Factum.Backend.csproj` | `<PackageReference Include="Markdig" Version="1.4.0" />` (BSD-2-Clause), versión fija |
| `Models/Case.cs` | `ReportTexts.Formato` (3.1) |
| `DTOs/CaseDtos.cs` | `ReportTextsDto`: parámetro nuevo `string? Formato = null`, al final |
| `Services/Reports/ReportMarkdown.cs` (**nuevo**) | `ReportTextFormats`, pipeline de Markdig, `Parse`, `Validate`, `IsBlank`, `FromPlainText`, `EscapeInline`, `FromLineTemplate` |
| `Services/Reports/ReportMarkdownRenderer.cs` (**nuevo**) | Markdown → párrafos OpenXML (6.3) |
| `Services/Reports/ReportListNumbering.cs` (**nuevo**) | Definiciones de lista en `numbering.xml`, creadas al vuelo (6.3.4) |
| `Services/Reports/ReportService.cs` | B-R4 con dos caminos; condiciones de bloque con `ReportTextRules.IsBlank` |
| `Services/Reports/ReportValues.cs` | `RenderDefaults` produce Markdown y `Formato = "markdown"`; tokens escapados |
| `Services/Reports/ReportSettings.cs` | Valida los `Report:DefaultTexts:*` configurados (6.5) |
| `Services/Cases/CaseValidation.cs` | `ValidateReportTexts` (formato + contenido); `ValidateForGenerate` con `ReportTextRules.IsBlank` |
| `Services/Cases/CaseService.cs` | `SaveReportTextsAsync` guarda `Formato = ReportTextFormats.Normalize(request.Formato)` |
| `README.md` (raíz), sección "Informe pericial: configuración" | `DefaultTexts:*` se interpretan como Markdown, línea por línea (6.5) |

### 6.2 `ReportMarkdown`

```csharp
public static class ReportTextFormats { public const string Markdown = "markdown"; public const string Texto = "texto";
    /// null/""/"texto" → null; "markdown" → "markdown"; otro → throw/flag inválido (lo decide CaseValidation).
    public static bool TryNormalize(string? value, out string? normalized); }

public static class ReportMarkdown
{
    // Construido UNA vez (thread-safe para Parse).
    internal static readonly MarkdownPipeline Pipeline = Build();
    // new MarkdownPipelineBuilder(); BlockParsers.TryRemove<IndentedCodeBlockParser>();
    // BlockParsers.Find<ParagraphBlockParser>()!.ParseSetexHeadings = false;  (sin UseAdvancedExtensions,
    // sin tablas, sin tachado; HtmlBlock/HtmlInline quedan ACTIVOS para poder reconocer <u> y rechazar el resto)
    public static MarkdownDocument Parse(string markdown);      // ReportValues.Clean antes de parsear
    public static string? Validate(string markdown);            // null = OK; si no "HTML" | "imagen" | "enlace"
    public static bool IsBlank(string? markdown);               // 4.5
    public static string FromPlainText(string? text);           // 4.4
    public static string EscapeInline(string text);             // 4.4 pasos 1-2 (sin inicio de línea)
    public static string FromLineTemplate(string text);         // 6.5
    internal static bool IsAllowedUrl(string? url);             // ^(https?://|mailto:) sin distinguir mayúsculas, sin espacios iniciales
}

public static class ReportTextRules
{
    /// Vacío según el formato del caso (4.5). t null → texto plano.
    public static bool IsBlank(ReportTexts? t, string? value);
}
```

`Validate` recorre `doc.Descendants()`: `HtmlBlock` → `"HTML"`; `HtmlInline` cuyo `Tag` no es exactamente `"<u>"` ni `"</u>"` → `"HTML"`; `LinkInline { IsImage: true }` → `"imagen"`; `LinkInline` o `AutolinkInline` con URL no permitida → `"enlace"`. Lo que el dialecto no produce pero tampoco es peligroso (encabezados de otro nivel, línea horizontal, `~~`, `|`) **se acepta** y el renderer lo resuelve (6.3).

### 6.3 `ReportMarkdownRenderer` (Markdig AST → OpenXML)

Firma sugerida:

```csharp
internal static class ReportMarkdownRenderer
{
    /// Reemplaza cada párrafo cuyo texto (Trim) es exactamente `placeholder` por los párrafos del
    /// Markdown, con el pPr del párrafo y el rPr de su primer run con texto como base (mismo criterio
    /// que ReplaceParagraphPerLine). Agrega cada párrafo creado a `resolved` (B-R6 no los escanea:
    /// un "{caratula}" del perito queda literal).
    public static void ReplacePlaceholder(MainDocumentPart main, OpenXmlElement root, string placeholder,
        string markdown, HashSet<Paragraph> resolved, ReportListNumbering numbering);
}
```

Reglas generales:
- Usar las propiedades tipadas del SDK (`rPr.Bold = new Bold()`, `pPr.Indentation = …`, `pPr.NumberingProperties = …`, `pPr.Shading`, `pPr.ParagraphBorders`, `pPr.KeepNext`, `pPr.Justification`, `pPr.SpacingBetweenLines`, `rPr.RunFonts`, `rPr.FontSize`/`FontSizeComplexScript`, `rPr.Color`, `rPr.Underline`) sobre **clones** de la base: así el orden del esquema queda bien. Nada de XML como string.
- Todo `Text` con `Space = Preserve`.
- Se descartan los párrafos vacíos del principio y del final de la sección (como hoy).
- El texto literal pasa por `ReportValues.Clean`.
- Punto de extensión para imágenes (D19): el `switch` de inlines tiene un caso explícito `LinkInline { IsImage: true }` que hoy escribe el texto alternativo literal, con un comentario `// editor-imagenes-informe`.

#### 6.3.1 Bloques

| Nodo Markdig | DOCX |
|---|---|
| `ParagraphBlock` | Párrafo con el pPr base. En el nivel superior conserva todo (sangría de primera línea 2268, justificado, interlineado 1.5). Dentro de lista o cita, `Indentation` = `Left` del contenedor y `FirstLine = 0`. Si el texto visible es solo espacios o U+00A0 (`&nbsp;`) → párrafo **vacío** (sin runs), como las líneas vacías de hoy |
| `HeadingBlock` (cualquier nivel) | Subtítulo: pPr base con `FirstLine = 0` (`Left` del contenedor), `Justification = left`, `KeepNext`, `SpacingBetweenLines.Before = "120"`. Runs en **negrita**, sin numeración |
| `ListBlock` / `ListItemBlock` | 6.3.3 |
| `QuoteBlock` | Los hijos se renderizan con `Left` del contenedor + 567, `FirstLine = 0` y `ParagraphBorders.LeftBorder` (single, `Size = 12`, `Space = 8`, `Color = "D9DDE1"`). Runs con `Color = "3D444C"` |
| `FencedCodeBlock` (y cualquier `CodeBlock`) | **Un** párrafo: pPr base con `FirstLine = 0`, `Left` del contenedor, `Justification = left`, `SpacingBetweenLines { Before = "60", After = "60", Line = "240", LineRule = Auto }`, `Shading { Val = Clear, Color = "auto", Fill = "EEF0F2" }`. Una línea del bloque por `Text`, separadas con `Break`. Runs en Courier New (`Ascii`/`HighAnsi`/`EastAsia`/`ComplexScript`) de 9 pt (`FontSize = "18"`). Un bloque vacío no genera nada |
| `ThematicBreakBlock`, `LinkReferenceDefinitionGroup` | Se omiten |
| `HtmlBlock` (solo si llega sin pasar por la validación: defensa) | Párrafo con su texto crudo **literal** |

#### 6.3.2 Inlines

Se lleva un estado de formato (`bold`, `italic`, `underlineDepth`, `code`, `link`) y se recorre **en orden de documento**:

| Nodo | DOCX |
|---|---|
| `LiteralInline` | Run con el formato actual |
| `EmphasisInline` | `DelimiterCount == 2` → negrita; `1` → cursiva (para `*` y `_`). Recursivo |
| `HtmlInline` `<u>` / `</u>` | `underlineDepth++` / `underlineDepth = max(0, depth-1)`. Subrayado = `depth > 0`. Funciona aunque `<u>` y `**` se crucen |
| `HtmlInline` otro (defensa) | Texto literal |
| `HtmlEntityInline` | `Transcoded` como texto |
| `CodeInline` | Run en Courier New 10 pt (`FontSize = "20"`), conserva negrita/cursiva/subrayado si los hay |
| `LineBreakInline` | `IsHard` → `Break`; si no → un espacio |
| `LinkInline` (no imagen, URL permitida) | `Hyperlink { Id = main.AddHyperlinkRelationship(new Uri(url), true).Id, History = true }` con los runs del texto en `Color = "0E1013"` y subrayado simple. Si el texto visible ≠ URL, va después (fuera del hipervínculo) un run `" (" + urlVisible + ")"` con el formato del cuerpo. `urlVisible` = la URL; para `mailto:`, la dirección sin el prefijo. Si el texto coincide con la URL o con la dirección, no se agrega nada. Si `Uri` no parsea → solo el texto |
| `AutolinkInline` | Hipervínculo con texto = URL (sin el paréntesis) |
| `LinkInline` con URL no permitida o imagen (defensa) | Solo el texto (o el alt) |

#### 6.3.3 Listas

- `ilvl = min(profundidad, 2)` (0 = primer nivel; más de 3 niveles se aplanan en el tercero).
- Primer párrafo de cada `ListItemBlock`: `NumberingProperties { NumberingLevelReference = ilvl, NumberingId = numId }` e `Indentation { Left = 360·(ilvl+1), Hanging = 360 }` (pisa la sangría de primera línea 2268 del pPr base). Justificado e interlineado del pPr base.
- Párrafos siguientes del mismo ítem y bloques hijos (cita, código): sin numeración, `Left = 360·(ilvl+1)`, `FirstLine = 0`.
- Listas anidadas → recursión con profundidad + 1.
- `numId`: cada `ListBlock` (viñetas o numerada, de cualquier nivel) pide **un `w:num` nuevo** a `ReportListNumbering`. Las numeradas llevan `lvlOverride` del `ilvl` con `startOverride = OrderedStart` (o 1). Así cada lista arranca donde dice el Markdown y no sigue la cuenta de otra sección.

#### 6.3.4 `ReportListNumbering`

- Instancia por documento, perezosa: si ninguna sección en Markdown tiene listas, `numbering.xml` **no se toca**.
- `main.NumberingDefinitionsPart ?? main.AddNewPart<NumberingDefinitionsPart>()` (con `new Numbering()` si no tiene raíz).
- Crea dos `AbstractNum`:
  - Viñetas: `abstractNumId = max + 1`.
  - Decimal: `abstractNumId = max + 2`.
  - Para los dos: `Nsid` fijo (`"FAC70001"` / `"FAC70002"`), `MultiLevelType = HybridMultilevel` y **9 niveles** (0-8); del 3 al 8 copian el estilo del 2.
  - Cada nivel n: `LevelJustification = left` y `PreviousParagraphProperties { Indentation { Left = 360·(n+1), Hanging = 360 } }`.
  - Viñetas: `NumberFormat = bullet`; `LevelText` `"•"` (U+2022), `"◦"` (U+25E6) y `"▪"` (U+25AA) en los niveles 0, 1 y 2. Run en Arial y `Color = "0E1013"` (tinta).
  - Decimal: `NumberFormat = decimal`, `LevelText = "%{n+1}."`, `StartNumberingValue = 1`, Arial y tinta (D12).
- **Orden del esquema:** los `AbstractNum` nuevos van **después del último `AbstractNum` existente** y antes del primer `NumberingInstance`. Los `NumberingInstance` nuevos van al final, con `numId = max existente + 1, +2…`.
- **No toca** `abstractNum 1` / `numId 2` (la numeración romana de los títulos) ni ningún otro existente. B-R2b (`BrandColors.Apply`) corre antes y no ve las definiciones nuevas, que van en tinta fija.

### 6.4 `ReportService.GenerateDocxAsync`

- Condiciones de bloque: `["objetoInforme"] = !ReportTextRules.IsBlank(texts, texts.ObjetoInforme)`, y lo mismo para `descripcionNotasTecnicas` y `descripcionReserva`.
- B-R4: `var markdown = texts.Formato == ReportTextFormats.Markdown; var numbering = new ReportListNumbering(mainPart);`. Por cada `(placeholder, value)`: si es Markdown → `ReportMarkdownRenderer.ReplacePlaceholder(mainPart, body, placeholder, value, resolved, numbering)`; si no → `ReplaceParagraphPerLine(body, placeholder, value, resolved)` **sin cambios** (ni una línea de ese método).
- El resto de las pasadas no cambia.

### 6.5 Textos por defecto en Markdown (D10 A)

`ReportValues.RenderDefaults`:

1. Tokens: `DefaultTextTokens(...)` con **cada valor pasado por `ReportMarkdown.EscapeInline`**. Por ejemplo, `America/Argentina/Buenos_Aires` → `…Buenos\_Aires`.
2. `ReportTextRenderer.Render(template, tokensEscapados, omitZeroCountLines)` sin cambios: la regla de las líneas `"- "` con `{cantidad…}` = 0 sigue igual.
3. `ReportMarkdown.FromLineTemplate(rendered)`: **cada línea del template es un bloque** (D7). Se parte por `\n` y se recorta el final de cada línea. Las vacías del principio y del final se descartan; una vacía del medio pasa a `&nbsp;`. Las líneas `^[-+*] ` son ítems de viñeta y las `^\d{1,9}[.)] ` ítems numerados; los ítems consecutivos del mismo tipo se unen con `\n`. Cualquier otra línea es un bloque tal cual: el Markdown de adentro (`**`, `#`, `>`) **se respeta**, porque el texto lo controla el estudio. Los bloques se unen con `\n\n`.
4. `ReportTextsDto(..., Formato: ReportTextFormats.Markdown)`.

Resultado con el default de "Operaciones realizadas": el mismo texto de hoy, con las operaciones como lista real (`"…operaciones:\n\n- Capturas de pantalla: 2.\n- …\n\nLas operaciones se limitaron…"`).

`ReportSettings`: para cada `Report:DefaultTexts:*` **configurado**, si `ReportMarkdown.Validate(ReportMarkdown.FromLineTemplate(valor))` devuelve error, se loguea `Report: DefaultTexts:{Clave} tiene contenido no permitido ({motivo}); se usa el texto por defecto` y se usa el default versionado. Así un default inválido no deja al perito con un autoguardado que falla en el primer `PUT`.

README: en la fila de `Report:DefaultTexts:*` se agrega que el texto se interpreta como **Markdown línea por línea**: cada línea es un párrafo; `- ` es viñeta, `1. ` numerada, `**negrita**`, `*cursiva*`, `<u>subrayado</u>`; sin HTML ni imágenes.

### 6.6 `CaseValidation` / `CaseService`

- `ValidateReportTexts(ReportTextsDto d)` (misma firma):
  1. `formato` con `TryNormalize`; si es inválido → error 4.2.
  2. Largos (sin cambios).
  3. Si es Markdown, `ReportMarkdown.Validate` campo por campo en el orden de hoy (`objeto_informe` … `reserva`). El primer error corta.
- `ValidateForGenerate`: las cinco claves usan `ReportTextRules.IsBlank(t, t?.X)`.
- `SaveReportTextsAsync`: `Formato = normalized` en el `new ReportTexts { … }`. Los textos se guardan **tal cual llegan**: no se reescriben ni se normalizan.

---

## 7. Frontend (`client/`): diseño

### 7.1 Elección de librería (D1)

| Opción | Salida Markdown fiel | React 19 | Peso (gz, aprox.) | Accesibilidad | Licencia | Veredicto |
|---|---|---|---|---|---|---|
| **Tiptap 3.31.4** (`@tiptap/react` + `starter-kit` + `@tiptap/markdown`, ProseMirror) | **Sí**, con overrides chicos. Lo probé headless: con los overrides de 7.3 todas las fixtures hacen roundtrip | Peer `^19` | ~110-140 KB (ProseMirror + core + marked), cargado solo en el paso 4 | `contenteditable` con atributos configurables; esquema estricto en el pegado | MIT (todos los paquetes usados) | **Elegida** |
| `Editor` de PrimeReact (Quill 2) | No: guarda HTML; haría falta HTML→MD (turndown) y sanitizar | Sí | ~50 KB + conversor | Media; CSS del tema "snow" choca con unstyled/pt | MIT/BSD | Descartada |
| Lexical | Paquete Markdown propio, transformadores a mano para `<u>` y listas | Sí | ~40-60 KB | Buena | MIT | Más código propio para el mismo resultado |
| Milkdown | Markdown-first (remark) | Sí | Más pesado (ProseMirror + unified/remark) | Buena | MIT | Theming propio; poca adopción |

Paquetes, **versión fija** (los peers exigen la misma versión): `@tiptap/core`, `@tiptap/pm`, `@tiptap/react`, `@tiptap/starter-kit`, `@tiptap/extensions` y `@tiptap/markdown`, todos `3.31.4`. No hay que instalar `marked` aparte (viene con `@tiptap/markdown`).

### 7.2 Archivos

| Archivo | Cambio |
|---|---|
| `client/package.json` (+ lock) | Dependencias de 7.1 |
| `client/src/lib/api.ts` | Tipos de 4.1; `getReportTextDefaults(): Promise<ReportTextDefaults>`; `saveReportTexts(caseId, data: ReportTextsRequest)` |
| `client/src/types/index.ts` | Reexportar `ReportTextFormat`, `ReportTextsRequest`, `ReportTextDefaults` |
| `client/src/lib/report-markdown.ts` (**nuevo**, puro, sin importar Tiptap) | `REPORT_TEXT_FORMAT = "markdown"`, `cleanText`, `escapeInlineText`, `escapeLineStart`, `plainToMarkdown` (4.4), `normalizeMarkdown`, `isBlankMarkdown`, `isBlankReportText` (4.5), `isAllowedUrl`, `encodeUrlForMarkdown` |
| `client/src/lib/pericial.ts` | `getMissingRequirements` usa `isBlankReportText(cas.report_texts?.[k], cas.report_texts?.formato)` |
| `client/src/components/editor/extensions.ts` (**nuevo**) | Extensiones y overrides (7.3) |
| `client/src/components/editor/RichTextEditor.tsx` (**nuevo**, `"use client"`) | Componente reutilizable (7.4) |
| `client/src/components/editor/EditorToolbar.tsx` (**nuevo**) | Barra (7.5) |
| `client/src/components/editor/LinkPopover.tsx`, `FormatHelp.tsx` (**nuevos**) | Enlace y "Formatos disponibles" |
| `client/src/components/editor/pasteTransform.ts` (**nuevo**) | `transformPastedHTML` (7.6) |
| `client/src/app/globals.css` | Bloque `.fx-rte` con estilos del contenido (solo tokens `--fx-*`, sin hex) y el placeholder |
| `client/src/components/FormField.tsx` | Prop opcional `labelId` → `<label id={labelId}>` (para `aria-labelledby`) |
| `client/src/components/ReportStep.tsx` | Usa el editor (7.7) |

### 7.3 Extensiones (`extensions.ts`)

`StarterKit.configure({ heading: { levels: [3] }, strike: false, horizontalRule: false, trailingNode: false, paragraph: false, underline: false, link: false, orderedList: false })`, más:

- **`FxParagraph`** = `Paragraph.extend({ renderMarkdown })`: si no tiene contenido → `"&nbsp;"` **siempre** (D9). Si tiene → `h.renderChildren(content)` partido por `\n`, aplicando `escapeLineStart` (4.4, paso 4.3) a cada línea, y unido con `\n`. Hay que preservar los dos espacios finales del salto duro: el escape solo mira el principio de la línea.
- **`FxHeading`** (de `heading`, o con `extend` del Heading del kit): si el contenido renderizado termina en ` #+`, se escapa el primer `#` de esa racha (si no, Markdig lo toma como cierre ATX).
- **`FxCodeBlock`**: `renderMarkdown` con un fence de backticks de largo `max(3, racha más larga + 1)` y **sin lenguaje** (`languageClassPrefix` irrelevante; no hay selector de lenguaje).
- **`FxUnderline`** = `Underline.extend({ renderMarkdown: (n, h) => "<u>" + h.renderChildren(n) + "</u>", markdownTokenizer: { name: "underline", level: "inline", start: s => s.indexOf("<u>"), tokenize: (src, _t, lexer) => { const m = /^<u>([\s\S]+?)<\/u>/.exec(src); return m ? { type: "underline", raw: m[0], text: m[1], tokens: lexer.inlineTokens(m[1]) } : undefined } } })`. Reemplaza el tokenizer `++` de Tiptap: `++x++` vuelve a ser texto literal. Lo probé: `<u>sub **n** [lk](https://a.b)</u>` hace roundtrip.
- **`FxLink`** = `Link.extend({ parseMarkdown: (token, h) => isAllowedUrl(token.href) ? h.applyMark("link", h.parseInline(token.tokens || []), { href: token.href }) : h.parseInline(token.tokens || []), renderMarkdown: … [texto](encodeUrlForMarkdown(href)) sin título })`. Configuración: `.configure({ openOnClick: false, autolink: true, linkOnPaste: true, defaultProtocol: "https", isAllowedUri: u => isAllowedUrl(u), HTMLAttributes: { rel: "noopener noreferrer nofollow", target: null } })`. Sin este override, un `[t](javascript:…)` que llegue por API queda como enlace al parsear (lo verifiqué).
- **`FxOrderedList`**: `OrderedList` sin el atributo `type` (`addAttributes` devuelve solo `start`). Si no, un `<ol type="i">` pegado se serializaría como `i.`, que no es lista en CommonMark.
- **`ListDepthLimit`**: atajo `Tab` con prioridad alta. Si el ítem actual ya está en profundidad 3, consume la tecla sin anidar. `Shift-Tab` queda como en el kit.
- **`MaxMarkdownLength`** (D9 A): plugin con `filterTransaction`. Deja pasar toda transacción que no cambia el doc o que no alarga el Markdown. Si la estimación `largoActual + 4·(tr.doc.content.size − state.doc.content.size) + 200` supera el máximo, serializa `editor.markdown.serialize(tr.doc.toJSON())` y, si `normalizeMarkdown(...)` pasa de 20 000, **rechaza** la transacción (pegado completo incluido) y avisa con `onLimit()` (mensaje 7.4).
- **`Placeholder`** (de `@tiptap/extensions`) con el texto de la sección.
- **`Markdown.configure({ markedOptions: { gfm: false }, indentation: { style: "space", size: 2 } })`**. Sin GFM, `~~` y las tablas quedan como texto, en vez de perderse: con GFM una tabla en Markdown se parsea a vacío (lo verifiqué).
- **Pegado sin formato:** ProseMirror ya pega como texto plano con Shift presionado (`view.input.shiftKey`): Ctrl/Cmd+Shift+V funciona sin código extra. Hay que verificarlo en Chrome, Firefox y Safari (en Safari el atajo del sistema es Cmd+Option+Shift+V).

### 7.4 `RichTextEditor`

```tsx
interface RichTextEditorProps {
  id: string;                 // → atributo del elemento editable (report-<clave>): getElementById(id).focus() funciona
  labelId: string;            // → aria-labelledby
  value: string;              // Markdown (dialecto Factum)
  onChange: (markdown: string) => void;   // ya normalizado
  onBlur?: () => void;
  required?: boolean;         // → aria-required
  placeholder?: string;
  maxLength: number;          // MAX_LEN_TEXT
}
```

- Se carga con `next/dynamic(() => import("@/components/editor/RichTextEditor"), { ssr: false, loading: <esqueleto del alto de 4 líneas> })` **desde `ReportStep`** (un client component, como pide la guía `lazy-loading.md` de Next 16). Además `useEditor({ immediatelyRender: false, … })`.
- `content: value, contentType: "markdown"` al crear: no dispara `onUpdate`.
- `editorProps.attributes`: `id`, `role: "textbox"`, `aria-multiline: "true"`, `aria-labelledby`, `aria-required`, `class: "fx-rte …"` (mismo look que `inputRootClasses`: borde, radio, superficie y anillo de foco con `focus-visible` → en un `contenteditable` usar `focus:`/`focus-within:` en el contenedor).
- `onUpdate({ editor })`: `md = normalizeMarkdown(editor.getMarkdown())`. **Solo si `md !== lastValueRef.current`**: actualizar `lastValueRef` y llamar `onChange(md)`. Esta guarda es la que evita que una transacción sin cambio real (selección, normalización del esquema) marque como editado un caso viejo.
- `normalizeMarkdown`: `\r\n` → `\n`. Quita los bloques `&nbsp;` del principio y del final (y sus `\n\n`) y los espacios y saltos finales. Un doc vacío da `""`.
- Cambio externo de `value` (Restaurar texto por defecto): `useEffect([value])`. Si `value !== lastValueRef.current` → `editor.commands.setContent(value, { contentType: "markdown", emitUpdate: false })` y `lastValueRef.current = value`.
- Contador abajo a la derecha: `value.length` / `maxLength`, con separador de miles de `es-AR`. Gris; `text-fx-warning` desde el 90 %; `text-fx-danger` al tope, con el mensaje "Llegaste al máximo de 20 000 caracteres" (`aria-live="polite"`).
- Aviso de pegado (7.6): texto chico `text-fx-text-3`, `aria-live="polite"`, "Se quitaron formatos que el informe no admite", que se va solo a los ~6 s.
- `onBlur` → `props.onBlur` (flush del autoguardado, como hoy).

### 7.5 Barra (`EditorToolbar`)

- `role="toolbar"` con `aria-label="Formato de {sección}"`, `aria-controls={id}`, roving tabindex y flechas izquierda/derecha (Home/End). `Button` de Prime `text` + `severity="secondary"`, `size="small"`, íconos de `lucide-react`, `aria-label`, `aria-pressed` en los de estado y `Tooltip` (pt existente) con el atajo según la plataforma ("Ctrl+B" / "⌘B").
- Grupos y atajos (los de Tiptap por defecto, salvo enlace):

| Grupo | Botón | Atajo |
|---|---|---|
| Texto | Negrita / Cursiva / Subrayado | Mod-B / Mod-I / Mod-U |
| Listas | Viñetas / Numerada | Mod-Shift-8 / Mod-Shift-7 (Tab / Shift+Tab anidan) |
| Bloques | Subtítulo / Cita / Código en línea / Bloque de código | Mod-Alt-3 / Mod-Shift-B / Mod-E / Mod-Alt-C |
| Enlace | Enlace (agregar, editar o quitar) | Mod-K (atajo propio) |
| Historial | Deshacer / Rehacer | Mod-Z / Mod-Shift-Z |
| — | "Formatos disponibles" (popover de ayuda con la lista de atajos y sintaxis) | — |

- La barra es `sticky` arriba **dentro del campo** mientras se edita una sección larga (el `top` tiene que dejar ver el encabezado del wizard). En pantallas angostas (`< sm`), "Bloques" y "Enlace" van a un menú "Más" (`Menu` popup de Prime, pt existente).
- Estado activo: `editor.isActive("bold")`, etc., suscripto con `useEditorState` de `@tiptap/react` (evita re-renders de todo el editor).
- `LinkPopover`: campos "Texto" y "URL". Valida `isAllowedUrl`; si falta el esquema y parece un dominio, antepone `https://`. Error en línea: "Usá una dirección http(s):// o mailto:". Botones "Aplicar" / "Quitar enlace". Accesible con el teclado, Esc cierra y el foco vuelve al editor. Puede ser `Dialog` (pt existente) chico o un popover propio. Si se usa `OverlayPanel`, hay que agregar su pt en `client/src/lib/prime/pt/`.

### 7.6 Pegado (`pasteTransform.ts`, `editorProps.transformPastedHTML`)

1. **Listas de Word:** los `<p>` con `mso-list:lN levelM` consecutivos se convierten en `<ul>`/`<ol>` anidados según `level`. Es `<ol>` si el marcador (el `span` con `mso-list:Ignore`) es `\d+[.)]` o una letra; si no, `<ul>`. Se borra el `span` del marcador.
2. **Tablas:** cada `<tr>` pasa a `<p>` con los textos de sus celdas unidos por `" | "` (D15).
3. **Detección para el aviso:** hay que avisar si el HTML trae `<img`, `<table`, `<h1>`, `<h2>`, `<h4>`-`<h6>`, `<s>`, `<del>`, `<strike>`, `<hr`, o si el portapapeles trae archivos (`handlePaste`: `event.clipboardData.files.length > 0` **sin** HTML ni texto → se consume el evento y solo se avisa).
4. El resto lo resuelve el esquema estricto de ProseMirror: fuentes, tamaños, colores y estilos de Word se descartan. Las negritas de Google Docs con `font-weight:normal` ya las ignora `Bold`.
5. ProseMirror parsea el HTML pegado con un documento inerte (`DOMParser`): un `<img onerror>` o un `<script>` pegado no se ejecuta.

### 7.7 `ReportStep`

- `SECTIONS` y el resto de la UI no cambian. Se reemplaza `InputTextarea` por `RichTextEditor` (dinámico), con `id={reportFieldId(key)}`, `labelId={`${id}-label`}` (y `FormField` recibe `labelId`), `required`, `placeholder`, `maxLength={MAX_LEN_TEXT}`, `onChange={md => update({ ...textsRef.current, [key]: md })}` y `onBlur={() => void flush()}`.
- **Carga:** con `cas.report_texts` y `formato === "markdown"` → los valores tal cual. Si `formato` es otro → `t[k] = plainToMarkdown(cas.report_texts[k] ?? "")` **sin** marcar dirty ni guardar (D2 A). Sin `report_texts` → defaults (ya en Markdown) y `save()` inmediato, como hoy.
- **Solo las ocho claves:** al armar el estado desde `defaults` o desde el caso se toman solo las claves de `EMPTY_REPORT_TEXTS` (helper `pickSections`); `formato` no entra al estado.
- **Guardado:** `api.saveReportTexts(caseId, { ...textsRef.current, formato: "markdown" })`, en `save()` y en el guardado al desmontar.
- **Antes de mandar:** si algún campo pasa de `MAX_LEN_TEXT` (por ejemplo, un texto viejo de casi 20 000 caracteres que creció al escaparse), no se llama a la API. El estado pasa a `"error"` y debajo de ese editor aparece "Este texto supera el máximo de 20 000 caracteres al guardarse con formato; acortalo para poder guardar." (D16). El filtro de 7.3 deja borrar aunque se esté por encima del tope.
- **Restaurar:** sin cambios de lógica (compara strings Markdown). Ahora el default llega en Markdown.
- **Foco desde el checklist:** sin cambios (`getElementById(focusFieldId).focus()` + `scrollIntoView`): el `id` está en el elemento `contenteditable`.
- Texto del `StepHeader`: sin cambios.

### 7.8 Estilos del contenido (`globals.css`, `.fx-rte`)

Hay que seguir las reglas del preset (solo tokens `--fx-*`, sin hex, sin `dark:`).

- Alto mínimo de 4 líneas y crecimiento automático.
- `p` con el margen de un párrafo del cuerpo.
- `ul` con `list-disc` → `circle` → `square` por nivel y `ol` con `list-decimal`, sangría de 1.25 rem.
- `blockquote`: borde izquierdo de 3 px `var(--fx-border-strong)` y texto `var(--fx-text-2)`.
- `code` y `pre`: `font-mono` (IBM Plex Mono), fondo `var(--fx-surface-3)` y radio `--fx-radius-sm`.
- `h3`: negrita, tamaño del cuerpo.
- `a`: subrayado y color de texto.
- Placeholder: `p.is-editor-empty:first-child::before { content: attr(data-placeholder); color: var(--fx-text-3); float: left; height: 0; pointer-events: none; }`.

---

## 8. Decisiones técnicas

| # | Decisión | ¿Necesita al usuario? |
|---|---|---|
| D1 | Editor: **Tiptap 3.31.4** + `@tiptap/markdown`, versión fija (7.1). | No (recomendada) |
| D2 | Parser del servidor: **Markdig 1.4.0** (BSD-2) con recorrido propio del AST; sin `UseAdvancedExtensions`, sin código indentado ni setext. HTML activo **solo para reconocer `<u>`** y rechazar el resto. | No |
| D3 | Dialecto canónico (4.3) y escape compartido (4.4) con fixtures F1-F17, que cumplen los dos lados. | No |
| D4 | `report_texts.formato`: `"markdown"` o ausente. Se acepta `"texto"`/`""`/`null` como texto plano y se guarda **sin** el campo (`[BsonIgnoreIfNull]`). | No |
| D5 | El servidor **rechaza** (400) el Markdown con HTML (salvo `<u>`), imágenes o enlaces no permitidos. El generador igual los neutraliza (texto literal) como defensa. | No |
| D6 | Listas, código, cita y subtítulo se generan **por código**, con formato directo y definiciones de numeración creadas al vuelo. **La plantilla v6 y `build_plantilla_v6.py` no cambian**: `--check` sigue en `OK`, los tests v4/v6 siguen valiendo y un caso sin listas deja `numbering.xml` byte a byte igual. | No |
| D7 | Defaults (del producto y de `Report:DefaultTexts:*`): **cada línea es un párrafo**, igual que hoy (no el "soft break" del Markdown estándar). `- `/`* `/`+ ` son viñetas, `1. ` numeradas, el resto Markdown inline. Los tokens se escapan. Un default configurado inválido se loguea y cae al versionado. | No |
| D8 | En Markdown, una sección "vacía" es la que no tiene **ninguna letra ni dígito** visible (4.5). Una sección con solo "…" o "-" cuenta como vacía. | **Informar** (DP1, no bloquea) |
| D9 | Todo párrafo vacío se guarda como `&nbsp;`. Así se conservan las líneas en blanco del perito y salen en el DOCX como párrafos vacíos, igual que hoy. | No |
| D10 | Los espacios al principio de una línea pasan a U+00A0: se conserva la sangría hecha a mano y no se convierte en bloque de código. | No |
| D11 | Subtítulo = `###`. En el DOCX, cualquier nivel de encabezado se trata como subtítulo (Arial 11 negrita, sin número, `keepNext`). | No |
| D12 | Listas en el DOCX: viñetas `•` → `◦` → `▪` en tinta `0E1013`. Numeradas en arábigos `1.` en **todos** los niveles. Sangría de 0.635 cm (360 dxa) por nivel; más de 3 niveles se aplanan en el tercero. | **Informar** (DP2, no bloquea) |
| D13 | Bloque de código = un párrafo con saltos de línea, fondo `EEF0F2`, Courier New 9 pt, interlineado simple y alineado a la izquierda. Código en línea, Courier New 10 pt. | No |
| D14 | Enlace: hipervínculo real en tinta subrayado y, si el texto no es la URL, ` (url)` después en texto normal. Para `mailto:` se muestra la dirección sin el prefijo. | No |
| D15 | Pegado: listas de Word (`mso-list`) → listas reales; tablas → una línea por fila con celdas separadas por ` \| `; imágenes y archivos no se insertan y se avisa. | No |
| D16 | Un texto viejo que al convertirse pasa de 20 000 no se guarda: aviso en esa sección hasta que el perito lo acorte. | No |
| D17 | Editor con `next/dynamic` y `ssr: false` desde `ReportStep`. Tiptap no entra al bundle del resto del dashboard. | No |
| D18 | Saltos "soft" (solo pueden venir por API) → espacio, como en CommonMark. Línea horizontal → se omite. | No |
| D19 | Imágenes: fuera. Punto de extensión marcado en el renderer (`LinkInline { IsImage: true }`, comentario `// editor-imagenes-informe`) y en `Validate` (hoy las rechaza). La HU de imágenes cambia esas dos ramas y agrega el nodo al editor. | No |
| D20 | Rollback: un backend anterior generaría los casos Markdown con las marcas visibles. No hay pérdida de datos. No se agrega mitigación. | No |

## 9. Decisiones para el usuario (ninguna bloquea; la implementación va con la recomendada)

- **DP1 (D8):** en una sección con formato, ¿un texto que es **solo signos** (p. ej. "-" o "…") cuenta como vacío para "Generar"? **Recomendado: sí.** Con esa regla, el cliente y el servidor dan exactamente el mismo resultado y nadie genera un informe con "Conclusiones: …". Hoy, en texto plano, eso cuenta como escrito; los casos viejos sin formato siguen igual.
- **DP2 (D12):** la HU fija la viñeta "•" y los números arábigos, pero no los niveles anidados. **Recomendado:** viñetas `•` / `◦` / `▪` y numeración `1.` en todos los niveles (sin `a)` ni `i.`, para que no se confunda con la numeración romana de las secciones).

---

## 10. Checklist atómico

### 10.1 Backend (`implementer-backend`) — regla dura de datos de `AGENTS.md`: no se toca ningún documento existente ni archivos de `Storage`; sin migraciones

- [ ] B1. `Factum.Backend.csproj`: `Markdig` `1.4.0`. `dotnet restore` OK.
- [ ] B2. `Models/Case.cs`: `ReportTexts.Formato` con `[BsonIgnoreIfNull]` y doc-comment (3.1).
- [ ] B3. `DTOs/CaseDtos.cs`: `ReportTextsDto(..., string? Formato = null)`.
- [ ] B4. `ReportMarkdown.cs`: `ReportTextFormats` (+ `TryNormalize`), `Pipeline` (sin código indentado, `ParseSetexHeadings = false`), `Parse`, `IsAllowedUrl`.
- [ ] B5. `ReportMarkdown.Validate` (6.2).
- [ ] B6. `ReportMarkdown.IsBlank` (4.5) y `ReportTextRules.IsBlank`.
- [ ] B7. `ReportMarkdown.EscapeInline` y `FromPlainText` (4.4).
- [ ] B8. `ReportMarkdown.FromLineTemplate` (6.5).
- [ ] B9. `CaseValidation.ValidateReportTexts`: formato, largos y contenido (4.2, 6.6).
- [ ] B10. `CaseValidation.ValidateForGenerate` con `ReportTextRules.IsBlank`.
- [ ] B11. `CaseService.SaveReportTextsAsync` guarda `Formato` normalizado.
- [ ] B12. `ReportValues.RenderDefaults`: tokens escapados, `FromLineTemplate` y `Formato = "markdown"`.
- [ ] B13. `ReportSettings`: valida los defaults configurados, con warning y fallback.
- [ ] B14. `ReportListNumbering` (6.3.4), con el orden del esquema correcto.
- [ ] B15. `ReportMarkdownRenderer`: bloques (6.3.1).
- [ ] B16. `ReportMarkdownRenderer`: inlines, con subrayado por profundidad e hipervínculos (6.3.2).
- [ ] B17. `ReportMarkdownRenderer`: listas anidadas, con clamp y `startOverride` (6.3.3).
- [ ] B18. `ReportService`: condiciones de bloque y B-R4 con dos caminos. `ReplaceParagraphPerLine` **sin cambios**.
- [ ] B19. README: `Report:DefaultTexts:*` como Markdown línea por línea.
- [ ] B20. Tests 10.3 en verde; `dotnet build` de los dos `.csproj` del backend sin warnings nuevos.
- [ ] B21. Prueba manual con Swagger o curl, contra la base de desarrollo, **solo sobre un caso en borrador creado por la prueba** y anotando su `_id`: `PUT` con Markdown válido → 200; con `<script>` → 400; sin `formato` → 200 y el documento sin `ReportTexts.Formato` (lectura con `mongosh`). Generar ese caso y abrir el DOCX en Word o LibreOffice. Al terminar, borrar **solo** ese caso por su `_id` (o dejarlo y anotarlo en el progress).
- [ ] B22. `progress/impl_backend_editor-texto-enriquecido.md` con archivos, salida de tests y verificación.

### 10.2 Frontend (`implementer-frontend`, app **`client/`** solamente)

Skills obligatorios: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (como criterio, sin 3D) y `web-design-guidelines` (autochequeo al final). Se suman `ui-styling` y `mblode-agent-skills-ui-animation` si hay transiciones (popover, aviso).

- [ ] F1. Leer `client/AGENTS.md` y `node_modules/next/dist/docs/01-app/02-guides/lazy-loading.md`.
- [ ] F2. Instalar los paquetes de 7.1, versión fija `3.31.4`.
- [ ] F3. `lib/api.ts` + `types/index.ts`: tipos y firmas de 4.1.
- [ ] F4. `lib/report-markdown.ts`: `plainToMarkdown` y el escape (4.4), `normalizeMarkdown`, `isBlankMarkdown`/`isBlankReportText` (4.5), `isAllowedUrl`, `encodeUrlForMarkdown`.
- [ ] F5. Verificar F1-F17 con `plainToMarkdown` (script descartable con `npx tsx` o la consola del navegador, **sin** commitearlo) y pegar entrada y salida en el progress.
- [ ] F6. `lib/pericial.ts`: `getMissingRequirements` con `isBlankReportText`.
- [ ] F7. `components/editor/extensions.ts` (7.3), con todos los overrides.
- [ ] F8. Verificar en el navegador que `editor.getMarkdown()` produce el dialecto (4.3): negrita, cursiva, `<u>`, listas anidadas de 3 niveles, numerada con `start`, cita, bloque con backticks adentro, subtítulo terminado en `#`, enlace con espacios, párrafo vacío, línea que empieza con `1. `/`# `/`- ` escrita a mano (deshaciendo la regla de entrada). Además, que `setContent(md)` → `getMarkdown()` devuelve el mismo string. Pegar la salida en el progress.
- [ ] F9. `components/editor/RichTextEditor.tsx` (7.4), con la guarda de `onUpdate`, el cambio externo, el contador y los avisos.
- [ ] F10. `EditorToolbar.tsx` (7.5): roving tabindex, `aria-pressed`, tooltips con atajo y menú "Más" en angosto.
- [ ] F11. `LinkPopover.tsx` y `FormatHelp.tsx`.
- [ ] F12. `pasteTransform.ts` (7.6) y aviso de pegado. Probar pegando desde Word (o un `.docx` abierto en LibreOffice), Google Docs y una página web.
- [ ] F13. `globals.css` `.fx-rte` (7.8), en claro y oscuro.
- [ ] F14. `FormField`: prop `labelId`.
- [ ] F15. `ReportStep.tsx` (7.7): editor dinámico, carga con conversión sin dirty, `pickSections`, `formato: "markdown"` en cada guardado, chequeo de largo antes de mandar.
- [ ] F16. Comprobar que abrir el paso 4 de un caso **viejo** (sin `formato`; ver 11.2) y salir sin tocar nada **no** hace ningún `PUT` (pestaña Network).
- [ ] F17. `npx tsc --noEmit` limpio; `npm run build` sin errores.
- [ ] F18. `progress/impl_frontend_editor-texto-enriquecido.md`: archivos, salidas de F5 y F8, skills invocados con sus hallazgos.

### 10.3 Tests xUnit (`server/tests/Factum.Backend.Tests/`)

Datos ficticios, carpeta temporal propia por test (como `ReportDesignTests`); sin Mongo ni `Storage`. Si hacen falta fakes compartidos (`FakeSettings`, `FakeBranding`, `MakeCase`), extraerlos a un `ReportTestSupport.cs` interno sin cambiar el comportamiento de los tests existentes.

`ReportMarkdownTests.cs`:
- [ ] T1. `FromPlainText` cumple **F1-F17** byte a byte (`[Theory]`).
- [ ] T2. Todo `FromPlainText(Fn)` pasa `Validate`. Al parsearlo, el texto visible de cada párrafo es el de la línea original, salvo las líneas `"- "`, que pasan a ítems sin el guion: **no se pierde ni cambia ninguna palabra**.
- [ ] T3. `Validate` acepta los ejemplos de la sección 4.3 (incluido `<u>a **b**</u>`, listas de 3 niveles, cita, bloque con `<div>` adentro y enlace `mailto:`).
- [ ] T4. `Validate` rechaza: `<script>alert(1)</script>`, `<img src=x onerror=alert(1)>`, `<div>x</div>` (bloque), `<u class="x">a</u>`, `[a](javascript:alert(1))`, `[a](data:text/html,x)`, `<javascript:alert(1)>`, `![a](https://x/a.png)`. Cada uno con su motivo.
- [ ] T5. `IsBlank`: vacíos `""`, `"&nbsp;"`, `"- "`, `"**  **"`, `"<u></u>"`, `"1. "`, `"> "`, `"\\*"`; no vacíos `"a"`, `` "`x`" ``, `"1\\. x"`, `"<https://x.com>"`.
- [ ] T6. `EscapeInline("Buenos_Aires *x* <b> & [y]")` → `"Buenos\\_Aires \\*x\\* &lt;b&gt; &amp; \\[y\\]"`.
- [ ] T7. `FromLineTemplate`: `"Intro:\n- a\n- b\nCierre"` → `"Intro:\n\n- a\n- b\n\nCierre"`; con línea vacía en el medio → `&nbsp;`; con `**x**` se respeta.

`ReportTextsValidationTests.cs`:
- [ ] T8. `ValidateReportTexts`: `formato` `"html"` → error; `"texto"`/`null`/`""` → OK; Markdown con `<script>` → `El campo resultados tiene contenido no permitido: HTML`; texto plano con `<script>` → OK (no se interpreta).
- [ ] T9. `ValidateForGenerate`: Markdown `"- "` en `conclusiones` → `report_texts.conclusiones` en `missing`; texto plano `"-"` → no falta (como hoy).
- [ ] T10. `RenderDefaults` (con `IReportSettings` falso, defaults versionados y zona `America/Argentina/Buenos_Aires`): `Formato == "markdown"`. "Operaciones realizadas" tiene `\n\n- Capturas de pantalla: N.` como lista, pasa `Validate` y lleva `Buenos\_Aires` escapado en "Notas técnicas". Con `{cantidadGrabaciones}` = 0, la línea se omite (sin cambios).

`ReportMarkdownDocxTests.cs` (genera con la **v6**):
- [ ] T11. **Caso viejo sin marca:** `Formato = null` y textos planos con `*`, `_`, `#`, `` ` ``, `{caratula}`, líneas `"- "` y una línea vacía en el medio. Cada línea es exactamente un párrafo con un run, el texto literal y el `rPr` igual al del placeholder (sin `b`/`i`/`u`, sin `numPr`). `numbering.xml` es **byte a byte** el de la plantilla. El texto del cuerpo (`BodyTextFromTitle`) es idéntico al de generar el mismo caso con la lógica de antes, que se fija como golden string en el test.
- [ ] T12. Negrita, cursiva y subrayado → runs con `w:b`, `w:i` y `w:u val=single`, y con el `rPr` del cuerpo (Arial). `***<u>bi</u>***` → las tres marcas.
- [ ] T13. Viñetas de 3 niveles + un 4.º nivel: `numPr` con `ilvl` 0/1/2/2; el `abstractNum` del `numId` es `bullet` con `•`/`◦`/`▪`; `ind left` 360/720/1080 con `hanging` 360.
- [ ] T14. Numerada con `start` 3: el `numId` apunta a un `abstractNum` `decimal` con `startOverride` 3. Dos listas numeradas en secciones distintas tienen `numId` distintos.
- [ ] T15. La numeración romana está intacta: siguen los 11 párrafos con `numId 2`, y `abstractNum 1` es idéntico al de la plantilla.
- [ ] T16. Código en línea: Courier New, `sz 20`. Bloque: un párrafo con `shd fill EEF0F2`, `jc left`, `line 240`, Courier New `sz 18` y `w:br` entre líneas; `<div>` adentro sale literal.
- [ ] T17. Cita: `pBdr/left` `D9DDE1`, `ind left` 567, runs `color 3D444C`. Subtítulo: run `b`, sin `numPr`, `keepNext`, `jc left`.
- [ ] T18. Enlace `[sitio](https://ejemplo.com/a)`: `w:hyperlink` con relación externa a esa URL y, después, el texto ` (https://ejemplo.com/a)`. `[https://x.com](https://x.com)` → sin paréntesis. `mailto:` → la dirección sin el prefijo.
- [ ] T19. `**{caratula}**` → en el DOCX aparece literal `{caratula}` en negrita (B-R6 no lo reemplaza). `archivo\_de\_prueba\_1.txt`, `2 \* 3 = 6` y `Expte. #123` → literales, sin cursiva ni subtítulo.
- [ ] T20. `&nbsp;` en el medio → párrafo vacío. `&nbsp;` al principio y al final → descartados. `NotasTecnicas = "&nbsp;"` en Markdown → el bloque `descripcionNotasTecnicas` se borra.
- [ ] T21. Validez: generar con **todos** los formatos y comparar con `OpenXmlValidator` (`Office2019`, mismas claves que `ValidationKeys`) contra la generación del mismo caso con textos planos: **sin errores nuevos**.

---

## 11. Verificación

### 11.1 Antes de declararse `done`

| Implementador | Comandos |
|---|---|
| backend | `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` · `dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj` (todo en verde, incluidos los tests existentes `ReportDesignTests`, `EvidenceZipTests`…) · `python3 ops/plantilla/build_plantilla_v6.py --check server/src/Factum.Backend/Templates/plantilla_informe_v4.docx server/src/Factum.Backend/Templates/plantilla_informe_v6.docx` → `OK` (no debería cambiar, pero se confirma) |
| frontend | `cd client && npx tsc --noEmit` · `npm run build` · checks F5, F8 y F16 |

### 11.2 Prueba manual para el usuario

1. **Caso nuevo:** crear un caso en borrador e ir al paso 4. "Operaciones realizadas" muestra las operaciones como **viñetas reales**. Escribir en "Resultados" con la barra y con atajos: `**negrita**`, `- `, `1. `, `> `, `` `código` ``, Ctrl/Cmd+B/I/U/K, Tab para anidar, Ctrl/Cmd+Z. Esperar a "Guardado hace un momento", ir a otro paso y volver: el formato sigue igual.
2. **Pegado:** copiar de Word un párrafo con negritas, una lista, otra fuente y color, y pegarlo: quedan negrita y lista, y se va el resto. Pegar con Ctrl/Cmd+Shift+V: sin formato. Pegar una imagen: no entra y aparece el aviso.
3. **Tope:** pegar un texto largo hasta pasar 20 000: el editor no lo acepta y el contador queda en rojo con el mensaje.
4. **Checklist:** dejar "Conclusiones" con una viñeta vacía, ir a "Generar": aparece como faltante; el enlace vuelve al paso 4 con el cursor en el editor.
5. **DOCX/PDF:** generar el informe y abrir el DOCX en Word (y, si se puede, en LibreOffice y WPS). Debe abrir **sin aviso de reparación**. Comprobar negrita, cursiva, subrayado, viñetas y numeradas reales (con la numeración romana de las secciones intacta), código en Courier sobre gris, cita con filete, subtítulo en negrita y enlace con la URL entre paréntesis. El PDF muestra lo mismo.
6. **Caso viejo (compatibilidad):** como hoy no hay borradores viejos en la base, se simula un cliente anterior. En un caso en borrador de prueba, hacer en Swagger `PUT /api/cases/{id}/report-texts` **sin** `formato`, con `Resultados = "archivo_de_prueba_1.txt\n2 * 3 = 6\n- uno\nExpte. #123"`.
   - a) Generar **sin abrir el paso 4**: el DOCX sale como antes, con un párrafo por línea, el guion literal y ningún carácter interpretado.
   - b) En otro caso preparado igual, abrir el paso 4: se ve el mismo texto, con "uno" como viñeta. Salir sin tocar nada: en Network **no** hay `PUT`. Escribir una letra y borrarla: se guarda con formato y, al generar, el texto es el mismo palabra por palabra.
7. **Seguridad:** `PUT` por Swagger con `formato: "markdown"` y `<script>` o `[x](javascript:alert(1))` → 400.
8. **Casos generados:** un caso `Completed` sigue abriendo y descargando su ZIP/DOCX/PDF igual que antes.

## Resolución de decisiones (2026-10-01)

- **DP1 (D8) → A** y **DP2 (D12) → A**, confirmadas por el usuario: un texto con formato que es solo signos cuenta como vacío; listas • / ◦ / ▪ en tinta y numeración 1. en todos los niveles.
- D1–D20: las recomendadas.
