# SDD: Imágenes (capturas del caso) dentro de las secciones del informe

**Slug:** `editor-imagenes-informe`
**HU:** `docs/hu-editor-imagenes-informe.md`. El usuario validó D1–D12 en la **A**: solo capturas del caso (PNG/JPEG, sin ilustrativas), sintaxis de imagen Markdown con esquema propio, la captura sigue en el anexo y en el cuerpo sale como "Figura N" con epígrafe, tamaño fijo 14 × 10.5 cm centrada, endpoint nuevo de solo lectura para la vista previa (solo el perito dueño, sin auditoría), referencia rota o ilegible = se bloquea la generación, tipo por magic bytes (sin SVG), pegar/arrastrar imagen bloqueado con aviso, las ocho secciones, imagen sola no cuenta como texto.
**Depende de:** `editor-texto-enriquecido` (SDD `Refactorizaciones/editor-texto-enriquecido.md`, en implementación en `feat/editor-texto-enriquecido`). Esta SDD está escrita **contra esa SDD**, no contra el código a medio hacer. Las referencias "base §x" apuntan a secciones de esa SDD. La sección 9 lista cada punto de contacto.
**Rama:** encadenada, sale de `feat/editor-texto-enriquecido` (o de `develop` si esa ya entró), según "Ramas y flujo" de `AGENTS.md`.
**Implementan:** `implementer-backend` (Opus) en `server/src/Factum.Backend` y `server/tests/Factum.Backend.Tests`; `implementer-frontend` (Opus) **solo en `client/`**. Pueden ir en paralelo guiándose por la sección 4 (Contrato compartido). Para probar de punta a punta, el backend tiene que estar desplegado primero (un backend sin esta HU rechaza con 400 cualquier texto con imagen).

---

## 1. Resumen funcional

En la barra del editor de cada una de las ocho secciones del paso 4 aparece un botón **"Imagen"** que abre un selector con las capturas de pantalla ya subidas del caso (miniaturas pedidas al backend, chip si tienen rol) y un campo "Descripción". Al confirmar, el editor inserta un **bloque de imagen** que se guarda en el Markdown como `![<descripción>](captura:<archivo>)`, sin bytes ni número de figura. La vista previa sale de un endpoint nuevo de solo lectura (`GET /api/cases/{id}/files/{filename}/preview`), con Bearer, solo para el dueño, solo capturas PNG/JPEG validadas por contenido. Un segundo endpoint de solo lectura (`GET /api/cases/{id}/report-images`) lista las capturas insertables con su disponibilidad: lo usan el selector, el editor ("Imagen no disponible" + "Quitar") y el checklist de "Generar". El servidor valida el esquema al guardar (rechaza `http:`, `data:`, `file:`, rutas y todo lo que no sea una captura del caso como bloque propio). Al generar, si alguna referencia apunta a una captura inexistente, vacía o ilegible, devuelve 400 con `missing: ["report_texts.<clave>.imagen"]`. Si no, el DOCX v6 inserta en el cuerpo la imagen con los **mismos bytes** de la evidencia, centrada en la caja de 14 × 10.5 cm, con el epígrafe "Figura N – <descripción> (<archivo>)". N es el número de esa captura en el Anexo, calculado con la misma lista. Anexo, tabla de hashes, ZIP y `report_hash` no cambian.

## 2. Toca

| Lado | ¿Toca? |
|---|---|
| backend (API) `server/src/Factum.Backend` (+ `server/tests/Factum.Backend.Tests`) | **sí** |
| backend (Tatana) `server/src/Factum.Agent` | **no** |
| client `client/` | **sí** |
| agent-ui `agent-ui/` | **no** |

Plantillas `Templates/plantilla_informe_v*.docx` y `ops/plantilla/build_plantilla_v6.py`: **no cambian**.

---

## 3. Modelo de datos (backend)

**Sin colecciones, campos, índices ni migraciones nuevos.** La referencia vive dentro del string Markdown de cada sección de `ReportTexts` (Mongo: `ReportTexts.Resultados`, etc., PascalCase; JSON: `report_texts.resultados`), y solo se interpreta cuando `ReportTexts.Formato == "markdown"` (campo de la HU base).

- **Archivos de evidencia:** no se copian, mueven, renombran, recodifican ni se abren para escritura. Toda lectura nueva es `FileMode.Open, FileAccess.Read, FileShare.Read`.
- **Compatibilidad:**
  - Un caso sin `Formato` (texto plano) no interpreta nada: `![x](captura:y)` sale literal.
  - Un caso Markdown sin imágenes genera exactamente igual que con la HU base.
  - Los casos `Completed` no se tocan.
- **Inspección de la base:** en esta sesión no estaba levantado el contenedor de Mongo de Factum. Me apoyo en la inspección de la SDD base (2026-10-01): 2 casos, los dos `Completed`, sin `ReportTexts.Formato`. Ningún documento existente puede tener referencias `captura:` (el dialecto todavía no existía).
- **Rollback:**
  - Un backend con solo la HU base rechaza con 400 los `PUT` con imágenes y genera las ya guardadas como el texto del alt (rama D19 base). No se pierden datos.
  - Un backend anterior a las dos HU las muestra como texto con marcas (base D20).

---

## 4. Contrato compartido (client ↔ server)

Política JSON: `JsonNamingPolicy.SnakeCaseLower` (`Program.cs`, líneas 70-78). Los `null` **se serializan** (no hay `DefaultIgnoreCondition`).

### 4.1 Referencia de imagen en el Markdown (dialecto Factum, extiende base §4.3)

Forma canónica que emite el cliente y la única que acepta el servidor:

```
![<alt>](captura:<nombre-codificado>)
```

- **Bloque propio, de nivel superior:**
  - Va sola en su párrafo, separada de lo anterior y de lo siguiente por `\n\n`.
  - Nunca dentro de un ítem de lista, una cita, un subtítulo ni mezclada con texto.
  - Sin título (`![a](captura:x "t")` no vale).
- **Esquema:** exactamente `captura:`, en minúsculas.
- **`<nombre-codificado>`:** el nombre del archivo (tal cual aparece en `GET …/files`) codificado en UTF-8. Cada byte que **no** esté en `[A-Za-z0-9._-]` va como `%XX` con hex en **mayúsculas**. El servidor acepta hex en minúsculas al leer, pero el cliente siempre emite mayúsculas.
  - TS: `encodeReportImageName(name: string): string` y `decodeReportImageName(encoded: string): string | null` (`null` si el `%` está mal formado o el UTF-8 no es válido).
  - C#: `ReportImageRef.EncodeName(string)` y `ReportImageRef.TryParse(string? url, out string filename)`.
- **`<alt>`** = la descripción del perito:
  - Texto de una línea, de 0 a **200** caracteres (code points no; `string.Length` en C# = `.length` en TS: UTF-16).
  - Antes de medir se hace `trim` y se colapsan los espacios internos.
  - Se escapa con el **mismo** `escapeInlineText` / `ReportMarkdown.EscapeInline` de la base (§4.4, pasos 1 y 2): `&`→`&amp;`, `<`→`&lt;`, `>`→`&gt;`, y `\` delante de `` \ ` * _ [ ] ~ ``. El alt es **solo texto literal**: sin negritas, código ni HTML.
- **Sin número de figura** en el texto (D4 de la HU).
- **Tope:** **20** imágenes por sección. Cuentan dentro de los 20 000 caracteres (son parte del string).

**Fixtures obligatorias** (strings JSON). Los tests xUnit verifican que el servidor las acepte y que los datos extraídos coincidan. El frontend comprueba el roundtrip `setContent(md)` → `getMarkdown()` byte a byte y pega la salida en su progress.

| # | Archivo | Alt | Markdown |
|---|---|---|---|
| I1 | `screenshot_20261001_101530.png` | `Chat con Juan` | `"![Chat con Juan](captura:screenshot_20261001_101530.png)"` |
| I2 | `screenshot_20261001_101530.png` | `""` | `"![](captura:screenshot_20261001_101530.png)"` |
| I3 | `Captura de pantalla (1).png` | `Chat` | `"![Chat](captura:Captura%20de%20pantalla%20%281%29.png)"` |
| I4 | `screenshot_1.png` | `a_b *c* [d] <e> & f` | `"![a\\_b \\*c\\* \\[d\\] &lt;e&gt; &amp; f](captura:screenshot_1.png)"` |
| I5 | `captura_ñ.jpg` | `x` | `"![x](captura:captura_%C3%B1.jpg)"` |
| I6 | (I1 entre dos párrafos) | | `"Se observa la conversación:\n\n![Chat con Juan](captura:screenshot_20261001_101530.png)\n\nFin."` |

**Rechazadas por el servidor** (`imagen`, ver 4.3): `![x](https://sitio/imagen.png)`, `![x](data:image/png;base64,AAAA)`, `![x](file:///etc/passwd)`, `![x](CAPTURA:screenshot_1.png)`, `![x](captura:..%2Fotro%2Fa.png)`, `![x](captura:%2E%2E)`, `![x](captura:)`, `![x](captura:a/b.png)`, `![x](captura:%FF.png)`, `![x](captura:foto_funcionario_20261001_100000.jpg)`, `![x](captura:video_screenshot.mp4)`, `![x](captura:informe_pericial_x.docx)`, `Texto ![x](captura:screenshot_1.png)`, `- ![x](captura:screenshot_1.png)`, `> ![x](captura:screenshot_1.png)`, `![x](captura:screenshot_1.png "t")`, `![**x**](captura:screenshot_1.png)` y un alt de 201 caracteres.

### 4.2 "Captura insertable" y "captura disponible" (misma regla en los dos lados)

- **Insertable (solo por el nombre):** sirve para validar al guardar y en el cliente.
  - El nombre es plano: no vacío, sin `/`, `\`, ni caracteres de control (`< 0x20`, `0x7F`), distinto de `.` y `..`, con `Path.GetFileName(n) == n` y de 255 caracteres como máximo.
  - `EvidenceClassifier.Classify(n, false) == Screenshot`: contiene `screenshot` o `captura`, la extensión es `.png/.jpg/.jpeg` y **no** es foto de identidad.
  - `!ReportService.IsGeneratedArtifact(n)`.
  - TS: `isRoleEligible(name)` de `lib/pericial.ts` ya es este mismo criterio (más el chequeo de nombre plano, que se agrega).
- **Disponible (solo en el servidor, mira el archivo):** se usa para el listado, la vista previa y la generación.
  - Es insertable.
  - El archivo está en el **primer nivel** de `CaseDir(id)` (`Path.GetDirectoryName(Path.GetFullPath(p)) == Path.GetFullPath(caseDir)`).
  - Tamaño `> 0`. **Sin tope de tamaño** (D7, DP2 B): cualquier captura del caso se puede insertar.
  - `ImageProbe.TryDetect(Stream, …)` (overload nuevo, 6.3) lo reconoce como `image/png` o `image/jpeg` leyendo solo las cabeceras, sin cargar el archivo en memoria.
  - El tipo sale **del contenido**, no de la extensión: un `.png` que es JPEG válido está disponible como `image/jpeg`. GIF, WebP, HEIC, BMP, SVG, PDF y texto no son disponibles, aunque se llamen `.png`.

### 4.3 Errores nuevos o cambiados de `PUT /api/cases/{id}/report-texts` (400, `{ error }`)

Solo con `formato = "markdown"`, igual que la base §4.2:

| Caso | Mensaje |
|---|---|
| Imagen que no cumple 4.1/4.2 (esquema, nombre, posición, título, alt no literal o de más de 200) | `El campo {clave} tiene contenido no permitido: imagen` (**mismo texto que la base**, que hoy rechaza toda imagen) |
| Más de 20 imágenes válidas en una sección | `El campo {clave} supera las 20 imágenes` |

El guardado **no** verifica que el archivo exista: eso se hace al generar (4.6), igual que los roles en el checklist. Una referencia a una captura que no está se guarda, se ve como "Imagen no disponible" y bloquea la generación.

### 4.4 Endpoint nuevo: listado de capturas insertables

`GET /api/cases/{id}/report-images` (Bearer, `[Authorize]`)

| Respuesta | Body |
|---|---|
| 200 | `{ "images": ReportImage[] }` |
| 403 | sin body (`Forbid()`, como el resto) |
| 404 | `{ "error": "Caso no encontrado" }` |

```jsonc
// ReportImage
{
  "filename": "screenshot_20261001_101530.png", // nombre real del archivo
  "size": 482113,                                // bytes
  "role": "imei_modelo",                         // "imei_modelo" | "nombre_dispositivo" | null
  "available": true,                             // regla 4.2 "disponible"
  "width": 1080,                                 // px, null si !available
  "height": 2400                                 // px, null si !available
}
```

- Incluye **todos** los archivos del primer nivel que son *insertables* (4.2), estén disponibles o no, en orden **ordinal por nombre** (`StringComparer.Ordinal`, el mismo orden del anexo). El cliente **no** reordena (nada de `localeCompare`).
- `role` sale de `cas.CaptureRoles`.
- Sin `Cache-Control` especial (JSON chico): `no-store` igual, por consistencia con la vista previa.
- No modifica nada, no audita.

C#:
- `DTOs/CaseDtos.cs` → `public sealed record ReportImageDto(string Filename, long Size, string? Role, bool Available, int? Width, int? Height);`
- `public sealed record ReportImagesResponse(List<ReportImageDto> Images);`

TS (`client/src/lib/api.ts`, reexportado en `client/src/types/index.ts`):
```ts
export interface ReportImage {
  filename: string;
  size: number;
  role: CaptureRoleValue | null;
  available: boolean;
  width: number | null;
  height: number | null;
}
// api.listReportImages(caseId: string): Promise<{ images: ReportImage[] }>
```

### 4.5 Endpoint nuevo: vista previa

`GET /api/cases/{id}/files/{filename}/preview` (Bearer, `[Authorize]`). El cliente arma la ruta con `encodeURIComponent(filename)`.

| Respuesta | Cuándo | Body / headers |
|---|---|---|
| 200 | El archivo es una captura **disponible** (4.2) del caso propio | Bytes **completos y sin modificar** del archivo, enviados **por streaming** (sin cargarlos en memoria, D7). `Content-Type: image/png` o `image/jpeg` (el **detectado**). `Content-Disposition: inline`, `X-Content-Type-Options: nosniff`, `Cache-Control: private, no-store`, `Content-Security-Policy: default-src 'none'; sandbox` |
| 400 | Nombre no plano (`..`, `/`, `\`, control, vacío, > 255) | `{ "error": "Nombre de archivo inválido" }` |
| 403 | Caso de otro perito | sin body |
| 404 | Caso inexistente | `{ "error": "Caso no encontrado" }` |
| 404 | Nombre plano pero no disponible: no existe, vacío, no es captura (video, ZIP, DOCX, foto de identidad, adjunto) o no es PNG/JPEG por contenido. También en un caso `Completed`, que ya no tiene archivos sueltos | `{ "error": "Imagen no disponible" }`. Un solo mensaje para todo: no distingue "no existe" de "no es captura" |

- Ningún byte de imagen en las respuestas que no son 200.
- Sin auditoría (`agent-events`), como pide D6 de la HU.
- TS: `api.getReportImagePreview(caseId: string, filename: string, signal?: AbortSignal): Promise<Blob>`.
  - Usa `fetch` con `Authorization: Bearer` y **sin** `Content-Type`.
  - Si `!res.ok`, lanza `toApiError(res)`.
  - Si `res.ok`, devuelve `res.blob()`.
  - **No** usa `request<T>` (que hace `res.json()`).

### 4.6 `POST /api/cases/{id}/generate`: claves nuevas de `missing`

- Clave: **`report_texts.<clave>.imagen`**, con `<clave>` ∈ `objeto_informe`, `operaciones_realizadas`, `aseguramiento_evidencia`, `resultados`, `valoracion_tecnica`, `conclusiones`, `notas_tecnicas`, `reserva`. Las **ocho** secciones (D11 de la HU).
- **Una por sección** que tenga al menos una referencia `captura:` válida (4.1) a una captura **no disponible** (4.2). Van en el orden de las secciones.
- Se agregan **después** de las claves de hoy, en la misma respuesta 400 `{ error: "Faltan datos obligatorios", missing: [...] }`.
- Solo si `ReportTexts.Formato == "markdown"`.
- C#: `CaseValidation.ReportImageKey(string clave) => $"report_texts.{clave}.imagen"`.
- TS: el tipo `MissingKey` de `client/src/lib/pericial.ts` suma `` `report_texts.${keyof ReportTextsInput}.imagen` ``.
- Checklist: `label` = `Imagen no disponible en «<Sección>»`, `step: 4`, `fieldId: reportFieldId(<clave>)`.

### 4.7 "Vacío" (extiende base §4.5, D12 de la HU)

En Markdown, una imagen **no aporta texto visible**: ni el alt ni el nombre cuentan. Hay dos reglas distintas (DP1 B):

- **Obligatorios (`missing`, checklist):** una sección con solo imágenes está **vacía** (D12 A de la HU). Regla `IsBlank` / `isBlankReportText`.
- **Opcionales (condiciones de bloque `objetoInforme`, `descripcionNotasTecnicas`, `descripcionReserva` del DOCX):** la sección **sale** si tiene texto visible **o** al menos una imagen válida. Regla nueva `ReportTextRules.HasBlockContent(t, value)` = `!IsBlank(t, value) || (t?.Formato == "markdown" && ReportMarkdown.ExtractImages(value).Count > 0)`. Solo existe en C#: el cliente no tiene condiciones de bloque.

- C#: `ReportMarkdown.IsBlank` ignora el subárbol de todo `LinkInline { IsImage: true }`; `HasBlockContent` en `ReportTextRules` (junto a `IsBlank` de la base).
- TS: `isBlankMarkdown` llama primero a `stripReportImages(md)` (4.8) y después aplica las regex de la base.

### 4.8 Extracción de referencias del lado del cliente (`client/src/lib/report-markdown.ts`)

`extractReportImageRefs(md: string): { filename: string; alt: string; line: number }[]`, sin Tiptap (es un escáner de líneas):

1. Normaliza `\r\n` → `\n` y parte por líneas.
2. Lleva el estado de **fence** abierto: una línea que empieza con 0-3 espacios y ```` ``` ```` o `~~~` (3 o más) abre un fence, y lo cierra una línea con el mismo carácter repetido al menos la misma cantidad de veces. Dentro de un fence no se extrae nada.
3. Fuera de un fence, una línea es imagen si:
   - cumple **exactamente** `^!\[((?:\\.|&[a-z0-9#]+;|[^\\\]\n])*)\]\(captura:((?:[A-Za-z0-9._-]|%[0-9A-Fa-f]{2})+)\)[ \t]*$`,
   - la anterior está vacía o es el inicio,
   - y la siguiente está vacía o es el fin.

   `filename = decodeReportImageName(g2)`: si da `null`, no es imagen. `alt = unescapeAlt(g1)`.
4. `unescapeAlt`:
   - Quita la `\` de los escapes de puntuación ASCII (`\\([!-/:-@[-`{-~])` → `$1`).
   - Decodifica `&amp; &lt; &gt; &quot; &#NN; &#xHH;` y `&nbsp;` (`\u00A0`).
   - Otras entidades quedan literales.
5. `stripReportImages(md)` = el mismo escáner, pero en vez de devolver las líneas de imagen las reemplaza por una línea vacía.

Como el servidor solo acepta imágenes como párrafo propio de nivel superior, este escáner da el mismo resultado que el AST de Markdig para todo Markdown que el servidor aceptó. Si llegara a haber una diferencia en un borde, el servidor es la autoridad (su `missing` se suma al checklist).

### 4.9 Resumen de archivos por lado

| Qué | C# | TS |
|---|---|---|
| Referencia, codificación, tope, largo del alt | `Services/Reports/ReportImageRef.cs` (**nuevo**) | `client/src/lib/report-markdown.ts` (`REPORT_IMAGE_SCHEME`, `MAX_REPORT_IMAGES_PER_SECTION = 20`, `MAX_REPORT_IMAGE_ALT = 200`, `encodeReportImageName`, `decodeReportImageName`, `formatReportImageMarkdown`, `extractReportImageRefs`, `stripReportImages`) |
| Disponibilidad | `Services/Reports/ReportImageFiles.cs` (**nuevo**) | `ReportImage.available` (4.4) |
| DTO del listado | `DTOs/CaseDtos.cs` (`ReportImageDto`, `ReportImagesResponse`) | `client/src/lib/api.ts` (`ReportImage`, `listReportImages`) |
| Vista previa | `CasesController.ReportImagePreview` | `api.getReportImagePreview` |
| Claves `missing` | `CaseValidation.ReportImageKey` | `lib/pericial.ts` (`MissingKey`, `REQUIREMENT_META`, `getMissingRequirements`) |
| Validación al guardar | `ReportMarkdown.Validate` (base §6.2) + `CaseValidation.ValidateReportTexts` | — (el editor no puede producir algo inválido; 400 = error de autoguardado de hoy) |

---

## 5. Endpoints (backend)

| Método y ruta | Cambio |
|---|---|
| `GET /api/cases/{id}/report-images` | **Nuevo** (4.4) |
| `GET /api/cases/{id}/files/{filename}/preview` | **Nuevo** (4.5) |
| `PUT /api/cases/{id}/report-texts` | Validación de imágenes (4.3) |
| `POST /api/cases/{id}/generate` | Bloqueo por referencias rotas (4.6) e imágenes en el cuerpo (6.5) |
| `GET /api/cases/{id}/download/{filename}`, `POST …/files` | **Sin cambios** (fuera de alcance en la HU) |

Rutas: `[HttpGet("{id}/report-images")]` y `[HttpGet("{id}/files/{filename}/preview")]`. No chocan con `[HttpGet("{id}/files")]` ni con `[HttpPost("{id}/files")]`.

---

## 6. Backend: diseño

### 6.1 Archivos

| Archivo | Cambio |
|---|---|
| `Services/Reports/ReportImageRef.cs` (**nuevo**) | Constantes, `EncodeName`, `TryParse`, `IsInsertableName`, `Format` (6.2) |
| `Services/Reports/ReportImageFiles.cs` (**nuevo**) | `Inspect`, `TryOpen` (6.3) |
| `Services/Branding/ImageProbe.cs` | **Overload nuevo** `TryDetect(Stream, out …)` con las mismas reglas; el `TryDetect(byte[])` existente no cambia (6.3) |
| `Services/Reports/ReportMarkdown.cs` (base) | `Validate` acepta imágenes válidas (6.4.1); `IsBlank` las ignora; nuevo `ExtractImages` |
| `Services/Reports/ReportMarkdownRenderer.cs` (base) | Párrafo-imagen → ancla + `PendingReportImage` (6.4.2) |
| `Services/Reports/ReportService.cs` | B-R4 pasa la lista de pendientes; nueva pasada B-R7b (6.5) |
| `Services/Cases/CaseValidation.cs` | Mensaje de tope; `ReportImageKey`; `BrokenImageKeys` (6.6) |
| `Services/Cases/CaseService.cs` | `ListReportImagesAsync`, `GetReportImagePreviewAsync`, bloqueo en `GenerateAsync` (6.6) |
| `Controllers/CasesController.cs` | Dos acciones nuevas (6.7) |
| `DTOs/CaseDtos.cs` | `ReportImageDto`, `ReportImagesResponse` |

**No se tocan:** `ReplaceWithImages`, `IsUsableImage`, `GetImageSize`, `ImageSource.FromFile`, `FillHashTable`, `BuildHashesAsync`, `EvidenceZip`, `StorageService`, `EvidenceClassifier`, `ImageProbe.TryDetect(byte[])`, `DownloadAsync`, `UploadFileAsync`, `ReplaceParagraphPerLine`.

### 6.2 `ReportImageRef`

```csharp
namespace Factum.Backend.Services.Reports;

public static class ReportImageRef
{
    public const string Scheme = "captura:";
    public const int MaxAltLength = 200;
    public const int MaxPerSection = 20;
    public const int MaxNameLength = 255;

    /// Bytes UTF-8 fuera de [A-Za-z0-9._-] → %XX (hex en mayúsculas).
    public static string EncodeName(string filename);

    /// url = destino del LinkInline tal como lo deja Markdig. true si:
    /// ^captura:(?:[A-Za-z0-9._-]|%[0-9A-Fa-f]{2})+$ (ordinal, sin ignorar mayúsculas),
    /// el decode da UTF-8 estricto (new UTF8Encoding(false, true)) y el nombre pasa IsInsertableName.
    public static bool TryParse(string? url, out string filename);

    /// 4.2 "insertable": nombre plano + Screenshot + no artefacto.
    public static bool IsInsertableName(string? filename);

    /// Forma canónica (para tests y documentación): "![" + ReportMarkdown.EscapeInline(alt) + "](captura:" + EncodeName(f) + ")".
    public static string Format(string filename, string alt);
}
```

`TryParse` decodifica a mano (pares `%XX` → bytes → UTF-8 estricto), no con `Uri.UnescapeDataString`, que no valida el UTF-8. Hay que verificar en un test (T3) que Markdig deja el `%28` de I3 **sin decodificar** en `LinkInline.Url`. Si lo decodificara, `TryParse` tiene que recibir `LinkInline.UnescapedUrl`/`Url` según corresponda, y el test lo fija.

### 6.3 `ReportImageFiles` (streaming, sin tope de tamaño — DP2 B)

```csharp
public enum ReportImageStatus { Available, NotInsertable, Missing, Empty, NotAnImage }

public sealed record ReportImageInspection(ReportImageStatus Status, string? ContentType, int Width, int Height)
{
    public bool IsAvailable => Status == ReportImageStatus.Available;
}

public static class ReportImageFiles
{
    /// Valida el nombre (IsInsertableName), arma la ruta y comprueba que esté en el primer nivel de caseDir,
    /// mira existencia y tamaño (> 0) y detecta el tipo con ImageProbe.TryDetect(Stream). Lee solo cabeceras.
    public static ReportImageInspection Inspect(string caseDir, string filename);

    /// Igual que Inspect, pero devuelve el FileStream ABIERTO (FileShare.Read) sobre el que se hizo la detección,
    /// rebobinado a la posición 0. El que llama lo usa (sirve o embebe) y lo cierra. Mientras está abierto con
    /// FileShare.Read nadie puede escribir el archivo: lo que se validó es exactamente lo que se usa (sin TOCTOU)
    /// y sin cargarlo en memoria.
    public static bool TryOpen(string caseDir, string filename, out FileStream stream, out string contentType,
        out int width, out int height);
}
```

- **`ImageProbe.TryDetect(Stream s, out string contentType, out int w, out int h)`** (overload nuevo en `Services/Branding/ImageProbe.cs`; el de `byte[]` no se toca). Mismas reglas que el existente:
  - **PNG:** lee 24 bytes (firma + IHDR).
  - **JPEG:** recorre los segmentos leyendo **solo** el marcador y la longitud (4 bytes) y salta con `Seek`, hasta el SOF, del que lee 5 bytes.

  Requiere un stream con `CanSeek` (un `FileStream` lo es). Memoria constante para cualquier tamaño de archivo y cualquier tamaño de APPn. Un tamaño declarado que se pasa del largo del stream da `false`, como en la versión de `byte[]`.
- La lectura es siempre `new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, FileOptions.SequentialScan)`. Nunca `File.Open*` con escritura ni `File.SetLastWriteTime`.
- Un `IOException`/`UnauthorizedAccessException` al abrir o leer → `NotAnImage` (no disponible), no una excepción hacia arriba.
- **Por qué no hace falta tope (DP2 B):** nada carga el archivo entero en memoria.
  - La vista previa se sirve por streaming (`FileStreamResult`). Kestrel no limita el tamaño de las respuestas.
  - El DOCX embebe con `ImagePart.FeedData(stream)`, que copia por bloques.
  - El listado solo lee cabeceras.
  - El `PUT` de textos lleva solo la referencia (el request no crece).
  - El único límite real es el del **DOCX completo**: Word no abre documentos de más de ~512 MB. Ese límite **ya existe hoy** por el anexo, que embebe todas las capturas. Esta HU no lo empeora, porque el cuerpo **reutiliza** el `ImagePart` del anexo o de la identificación (6.5, D9). No es una DP nueva.

### 6.4 Cambios en las piezas de la base

#### 6.4.1 `ReportMarkdown` (base §6.2)

- **`Validate`:** el caso `LinkInline { IsImage: true }` deja de devolver siempre `"imagen"`. Devuelve `"imagen"` si **alguna** de estas condiciones falla:
  1. El padre del inline es un `ParagraphBlock` cuyo padre es el `MarkdownDocument` (nivel superior).
  2. El `Inline` del párrafo tiene como hijos **solo** esa imagen, más `LiteralInline` de espacios en blanco o `LineBreakInline` no duros al principio o al final.
  3. `string.IsNullOrEmpty(link.Title)`.
  4. Los hijos del `LinkInline` (el alt) son solo `LiteralInline` y `HtmlEntityInline`.
  5. El alt aplanado (literales + `Transcoded` de las entidades), con `Trim` y los espacios colapsados, mide como máximo 200.
  6. `ReportImageRef.TryParse(link.Url, out _)`.

  Al recorrer `Descendants()`, **no** se aplica la regla de enlaces (`IsAllowedUrl`) a las imágenes: tienen su propia regla. Si el documento no tiene errores y hay **más de 20** imágenes, devuelve la constante nueva `ReportMarkdown.TooManyImages = "imagenes"`. El primer error en orden de documento corta, como en la base.
- **`IsBlank`:** saltea el subárbol de cada `LinkInline { IsImage: true }` (4.7).
- **Nuevo** `public static IReadOnlyList<(string Filename, string Alt)> ExtractImages(string markdown)`: las imágenes válidas (las que pasan 1-6) en orden de documento, con el alt aplanado. Lo usan `CaseValidation.BrokenImageKeys` y los tests.

#### 6.4.2 `ReportMarkdownRenderer` (base §6.3)

- **Firma:** se agrega un parámetro opcional al final:

  ```csharp
  public static void ReplacePlaceholder(MainDocumentPart main, OpenXmlElement root, string placeholder,
      string markdown, HashSet<Paragraph> resolved, ReportListNumbering numbering,
      ICollection<PendingReportImage>? images = null);

  internal sealed record PendingReportImage(Paragraph Anchor, string Filename, string Alt);
  ```

- **Párrafo-imagen:** un `ParagraphBlock` de nivel superior que cumple 6.4.1 (1-6) se renderiza así: un `Paragraph` **ancla**, vacío y sin `pPr`, insertado en su lugar y agregado a `resolved`, más `images.Add(new(anchor, filename, alt))`.
- **Si `images` es `null`** (defensa) o la imagen no cumple las reglas: se mantiene la rama D19 de la base (el alt como texto literal).
- **Recorte de párrafos vacíos:** la base descarta los párrafos vacíos del principio y del final de la sección. **Un ancla no cuenta como vacía**: no se descarta.
- El comentario `// editor-imagenes-informe` de la rama D19 se reemplaza por la referencia a esta SDD.

### 6.5 `ReportService.GenerateDocxAsync`

1. Antes de B-R4: `var bodyImages = new List<PendingReportImage>();`.
2. **B-R4**, camino Markdown: `ReportMarkdownRenderer.ReplacePlaceholder(mainPart, body, placeholder, value, resolved, numbering, bodyImages)`. El camino de texto plano no cambia.
3. **Condiciones de bloque de los opcionales (DP1 B):** `["objetoInforme"]`, `["descripcionNotasTecnicas"]` y `["descripcionReserva"]` pasan de `!ReportTextRules.IsBlank(texts, x)` (base §6.4) a `ReportTextRules.HasBlockContent(texts, x)` (4.7). Una sección opcional con **solo** imágenes sale con su título, sus figuras y sus epígrafes. Un texto plano (`Formato` null) se evalúa igual que en la base, porque `HasBlockContent` solo mira imágenes en Markdown.
4. **B-R7b (nueva, al final de B-R7, después de `{anexoCapturas}`):** `InsertBodyImages(mainPart, bodyImages, caseDir, annexShots, roles, hashes, resolved, ref drawId)`:
   - `figure` = diccionario `annexShots[i] → i + 1` (**la misma lista** que usa `{anexoCapturas}`, ya filtrada por `IsUsableImage` y sin rol). Así N coincide con el anexo por construcción.
   - Por cada pendiente, en orden:
     1. `ReportImageFiles.TryOpen(caseDir, filename, out stream, …)` (con `using`). Si falla, `throw new InvalidOperationException($"La captura {filename} no está disponible")`. No debería pasar, porque `CaseService` ya bloqueó; si pasa (carrera), la generación termina en `Error` y el `catch` de `GenerateAsync` borra el ZIP y el DOCX de este intento sin tocar la evidencia.
     2. Si `!hashes.ContainsKey(filename)`, también `throw`: la imagen del cuerpo **tiene** que ser evidencia de la tabla.
     3. Epígrafe (D10). Con `suf = alt.Length > 0 ? $"{alt} ({filename})" : filename`:
        - rol `imei_modelo` → `Captura de identificación (IMEI y modelo) – {suf}`
        - rol `nombre_dispositivo` → `Captura de identificación (nombre del dispositivo) – {suf}`
        - sin rol → `Figura {figure[filename]} – {suf}`. Si no está en `figure`, `throw` (defensa).
     4. **Parte de imagen (D9):**
        - Antes del bucle se arma un mapa `filename → relId` de los dibujos que ya están en el cuerpo y que agregó B-R7 (anexo e identificación). Se reconocen por `DWP.DocProperties.Name == filename` (es el nombre que les pone `BuildDrawing`) y se toma su `DRAW.Blip.Embed`.
        - Si existe y el `ImagePart` de ese `relId` tiene `ContentType == contentType` detectado, se **reutiliza** ese `relId`: no se agrega otra copia de los bytes.
        - Si no existe (por ejemplo, una captura con rol `nombre_dispositivo` que no entra al cuerpo de identificación, o un `.png` que es JPEG, donde el anexo la embebió con el tipo por extensión), se crea un `ImagePart` nuevo del tipo **detectado** con `FeedData(stream)` (streaming).
        - Para no duplicar la lógica, se extrae de `BuildImageParagraph` un helper privado `BuildImageParagraphForRel(string relId, string name, int imgW, int imgH, long maxW, long maxH, ref uint drawId, ParagraphProperties? pPr)`. `BuildImageParagraph` lo llama después de `EmbedImagePart`, sin cambiar su comportamiento (los tests de `ReportDesignTests` lo cubren).
        - `anchor.InsertBeforeSelf(BuildImageParagraphForRel(relId, filename, w, h, AnnexShotMaxW, AnnexShotMaxH, ref drawId, pPr))`, con `pPr` **igual al del anexo**: `KeepNext`, `SpacingBetweenLines { Before = "120", After = "60" }` y `Justification = Center`.
     5. `anchor.InsertBeforeSelf(caption = BuildCaptionParagraph(texto))` y `resolved.Add(caption)`, así B-R9 no avisa por un `{…}` escrito en el alt.
     6. `anchor.Remove()`.
   - Sea reutilizada o nueva, la parte tiene los bytes del archivo sin recodificar: el SHA-256 del `ImagePart` es igual al de la tabla de hashes.
   - `ReplaceWithImages` (anexo e identificación) **no cambia**.
5. `GenerateAsync` (pasos 1-9, hashes, ZIP, verificación, `report_hash`, borrado de sueltos): **sin cambios**. La lista `evidence` no depende de los textos.

### 6.6 `CaseValidation` / `CaseService`

- `CaseValidation.ValidateReportTexts`: si `ReportMarkdown.Validate` devuelve `TooManyImages`, el mensaje es `El campo {clave} supera las 20 imágenes`. Cualquier otro motivo sigue el formato de la base (`… contenido no permitido: {motivo}`).
- `CaseValidation.ReportImageKey(string clave)` y:

  ```csharp
  /// Claves report_texts.<clave>.imagen de las secciones con alguna imagen no disponible. Solo Formato == markdown.
  public static IReadOnlyList<string> BrokenImageKeys(ReportTexts? t, Func<string, bool> isAvailable);
  ```

  Recorre las ocho secciones en el orden de la base (`objeto_informe` … `reserva`), con `ReportMarkdown.ExtractImages` en cada una. Es pura: la E/S entra por `isAvailable`.
- `CaseService.ListReportImagesAsync(id, dni)`:
  1. `LoadOwnedAsync`.
  2. `_storage.ListFilesAsync(id)` → filtro `ReportImageRef.IsInsertableName` → orden ordinal.
  3. Por cada uno, `ReportImageFiles.Inspect` y rol desde `cas.CaptureRoles`.
- `CaseService.GetReportImagePreviewAsync(id, filename, dni)` → `Result<(Stream Content, string ContentType)>`:
  1. `LoadOwnedAsync` (404/403).
  2. Si el nombre no es plano → `Result.Invalid("Nombre de archivo inválido")`.
  3. `ReportImageFiles.TryOpen`; si falla → `Result.NotFound("Imagen no disponible")`. El `FileStream` abierto se devuelve al controlador, que lo entrega a `FileStreamResult`, y ASP.NET lo cierra al terminar la respuesta.
  4. **No** usa `DownloadAsync`.
- `CaseService.GenerateAsync`: después de `ValidateForGenerate` y **antes** de pasar a `Generating`:

  ```csharp
  var broken = CaseValidation.BrokenImageKeys(cas.ReportTexts,
      name => ReportImageFiles.Inspect(caseDir, name).IsAvailable);
  // missing = [..missing, ..broken]; si hay algo → 400 MissingMessage como hoy
  ```

  `ValidateForGenerate` no cambia de firma: la parte de imágenes se suma en `CaseService`, porque necesita mirar los archivos.

### 6.7 `CasesController`

```csharp
[HttpGet("{id}/report-images")]
[ProducesResponseType<ReportImagesResponse>(StatusCodes.Status200OK)]
public async Task<IActionResult> ReportImages(string id, CancellationToken ct)
{
    Response.Headers.CacheControl = "no-store";
    var r = await caseService.ListReportImagesAsync(id, Officer.Dni, ct);
    return r.IsSuccess ? Ok(new ReportImagesResponse(r.Value!)) : this.ErrorResult(r);
}

[HttpGet("{id}/files/{filename}/preview")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<IActionResult> ReportImagePreview(string id, string filename, CancellationToken ct)
{
    var r = await caseService.GetReportImagePreviewAsync(id, filename, Officer.Dni, ct);
    if (!r.IsSuccess) return this.ErrorResult(r);
    Response.Headers.CacheControl = "private, no-store";
    Response.Headers.XContentTypeOptions = "nosniff";
    Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
    Response.Headers.ContentDisposition = "inline";
    return File(r.Value.Content, r.Value.ContentType);   // FileStreamResult: streaming; sin fileDownloadName (sería attachment)
}
```

Hay que verificar que el CORS de `Program.cs` deje pasar un `GET` con `Authorization` desde el origen del cliente: ya lo hace con el resto de la API. No hace falta exponer headers.

---

## 7. Frontend (`client/`): diseño

Antes de tocar código: `client/AGENTS.md` y las guías de `client/node_modules/next/dist/docs/` que apliquen.

### 7.1 Archivos

| Archivo | Cambio |
|---|---|
| `client/src/lib/api.ts` (+ `types/index.ts`) | `ReportImage`, `listReportImages`, `getReportImagePreview` (4.4, 4.5) |
| `client/src/lib/report-markdown.ts` (base) | Constantes y funciones de 4.9. `isBlankMarkdown` usa `stripReportImages` (4.7) |
| `client/src/lib/pericial.ts` | `REPORT_SECTION_LABELS: Record<keyof ReportTextsInput, string>` (las 8 etiquetas de `SECTIONS`; `ReportStep` pasa a usarlas). `MissingKey` y `REQUIREMENT_META` con las 8 claves `.imagen`, generadas desde `REPORT_SECTION_LABELS`. `getMissingRequirements(cas, opts?: { reportImages?: ReportImage[] })`. `isRoleEligible` + nombre plano = `isInsertableReportImage(name)` |
| `client/src/lib/report-images.ts` (**nuevo**, sin Tiptap) | `useReportImages(caseId)` (listado: `{ images, status, reload }`) y `ReportImagePreviewCache` (7.4) |
| `client/src/components/editor/ReportImageNode.ts` (**nuevo**) | Nodo Tiptap `reportImage` (7.2) |
| `client/src/components/editor/ReportImageView.tsx` (**nuevo**) | NodeView React (7.3) |
| `client/src/components/editor/ReportImagesContext.tsx` (**nuevo**) | Contexto `{ caseId, images, cache, openPicker, sectionLabel }` que consumen el NodeView y la barra |
| `client/src/components/editor/CapturePickerDialog.tsx` (**nuevo**) | Selector y edición de la descripción (7.5) |
| `client/src/components/editor/extensions.ts` (base) | `FxDocument`, `ReportImage`, `InlineImageAsText`, tope de imágenes (7.2) |
| `client/src/components/editor/RichTextEditor.tsx` (base) | Prop `onRequestImage`; `handleDrop`; aviso D10 (7.6) |
| `client/src/components/editor/EditorToolbar.tsx` (base) | Botón "Imagen" (7.5) |
| `client/src/components/editor/pasteTransform.ts` (base) | Detección de imagen para el aviso D10 (7.6) |
| `client/src/components/ReportStep.tsx` | Provider, listado, un único `CapturePickerDialog` (7.7) |
| `client/src/components/dashboard/GenerateStep.tsx` | `useReportImages(currentCase.id)` → `getMissingRequirements(currentCase, { reportImages })` (7.8) |
| `client/src/app/globals.css` | `.fx-rte figure[data-fx-report-image]` y estados (solo tokens `--fx-*`) |

### 7.2 Esquema del editor (`extensions.ts`)

- **`FxDocument`:** `StarterKit.configure({ …base, document: false })` + `Document.extend({ content: "(block | figure)+" })`. El nodo de imagen está en el grupo **`figure`**, no en `block`. Así ProseMirror **no** lo deja entrar en `listItem` (`paragraph block*`) ni en `blockquote` (`block+`): la regla "solo nivel superior" (4.1) la impone el propio esquema.
- **`ReportImage`:** `Node.create({ name: "reportImage", group: "figure", atom: true, selectable: true, draggable: false, … })`.
  - Atributos `filename: string` y `alt: string` (default `""`).
  - `parseHTML`: `[{ tag: "figure[data-fx-report-image]", getAttrs: el => ({ filename: el.getAttribute("data-fx-report-image"), alt: el.getAttribute("data-alt") ?? "" }) }]`. Permite copiar y pegar **dentro** del editor (D15).
  - `renderHTML`: `["figure", { "data-fx-report-image": filename, "data-alt": alt }]`. **Sin `<img>`**: así nunca se renderiza una URL.
  - `addNodeView: ReactNodeViewRenderer(ReportImageView)`.
  - **Markdown:**
    - `renderMarkdown: n => formatReportImageMarkdown(n.attrs.filename, n.attrs.alt)`.
    - `markdownTokenizer` de **nivel bloque** (`level: "block"`, `start: s => s.search(/^!\[/m)`) con la regex de 4.8, paso 3, anclada al inicio, que consume hasta el `\n` (o el fin). Devuelve `{ type: "reportImage", raw, filename, alt }`, o `undefined` si `decodeReportImageName` da `null`.
    - `parseMarkdown: t => ({ type: "reportImage", attrs: { filename: t.filename, alt: t.alt } })`.
  - **Comando** `insertReportImage({ filename, alt })`:
    - Inserta **después del bloque de nivel superior** que contiene la selección (`$from.after(1)`).
    - Si ese bloque es un párrafo vacío, lo reemplaza.
    - Si la imagen queda última, agrega un párrafo vacío después.
    - Deja una `NodeSelection` sobre la imagen.
  - **Comando** `updateReportImageAlt(pos, alt)`.
  - **Keymap:** `Enter` con `NodeSelection` en un `reportImage` → `openPicker({ mode: "edit", pos })`.
- **`InlineImageAsText`** (defensa): si `marked` llega a producir un token inline `image` (por ejemplo `Texto ![x](captura:y)` en la misma línea, que el servidor no acepta), se convierte en **texto** con `token.raw`. No se pierde nada y nunca se crea un nodo.
- **Tope de imágenes:** en el plugin `filterTransaction` de la base (o uno hermano) se rechaza toda transacción que deje más de `MAX_REPORT_IMAGES_PER_SECTION` nodos `reportImage`, y se avisa "Máximo 20 imágenes por sección".
- **Verificación obligatoria (F6):** I1-I6 hacen roundtrip exacto, y un `![x](https://a/b.png)` o un `Texto ![x](captura:y)` que se carga con `setContent` queda como texto, sin nodo.

### 7.3 `ReportImageView` (NodeView)

- Raíz `NodeViewWrapper` `as="figure"` con `contentEditable={false}` y `data-fx-report-image`.
  - Centrado, ancho máximo 60 % del editor, borde fino `--fx-border`, radio de las tarjetas.
  - Anillo `--fx-*` de foco o selección cuando `props.selected`.
- **Estados** (de `ReportImagesContext`: `images` + `cache.get(filename)`):

  | Estado | Cuándo | Qué se ve |
  |---|---|---|
  | `loading` | Listado o vista previa en curso | Esqueleto con `aspect-ratio` = `width/height` del listado (default 9/16) |
  | `ready` | Vista previa lista | `<img src={blobUrl} alt="" />` dentro de un contenedor `role="img"` con `aria-label` = `${alt || "Sin descripción"} (${filename})` |
  | `unavailable` | El listado dice `available: false`, la captura no está en el listado, o la vista previa da 400/404 | Recuadro punteado `Imagen no disponible: {filename}` con botón **"Quitar"** (`deleteNode()`), que funciona sin seleccionar |
  | `error` | Red o 5xx | `No se pudo cargar la vista previa` + botón **"Reintentar"** (`cache.retry(filename)`). No bloquea la edición |

- **Epígrafe** debajo, en gris y cursiva:
  - Sin rol: `Figura · {alt} ({filename})`. Con alt vacío: `Figura · {filename}`.
  - Con rol: `Identificación (IMEI y modelo) · …` / `Identificación (nombre del dispositivo) · …` (D13).
  - Tooltip (pt existente): "El número se asigna al generar el informe, igual que en el anexo".
- **Barra flotante** (solo con `props.selected`), arriba a la derecha del bloque, como hermano del contenedor `role="img"` y no dentro de él:
  - "Editar descripción" (`Pencil`) → `openPicker({ mode: "edit", pos: getPos() })`.
  - "Quitar" (`Trash2`) → `deleteNode()`.
  - Supr/Backspace sobre la selección borran (comportamiento de ProseMirror) y Ctrl/Cmd+Z restaura.

### 7.4 Vista previa y caché (`lib/report-images.ts`)

- `ReportImagePreviewCache`:
  - `get(filename)` → `{ status, url? }` (con `useSyncExternalStore` o un suscriptor simple).
  - `load(filename)`: deduplica con un `Map<string, Promise>`, pide con `api.getReportImagePreview` y `AbortController`, y crea el blob URL con `URL.createObjectURL`.
  - `retry(filename)` y `dispose()`, que revoca todos los blob URLs y aborta lo pendiente.
- **Una instancia por `ReportStep`**: la crea con `useState(() => new …)` y la libera en el cleanup del `useEffect`. La comparten las 8 instancias del editor y el selector, así una captura se pide **una sola vez** por visita al paso 4. Es el mismo patrón de revocación que `useFileManager`.
- **Carga perezosa:** el NodeView y las miniaturas del selector llaman `load` cuando entran al viewport (`IntersectionObserver`, `rootMargin: "200px"`).
- `useReportImages(caseId)`: `api.listReportImages` al montar. Estados `loading | ready | error`, más `reload()`.

### 7.5 Barra y selector

- **`EditorToolbar`:** botón **"Imagen"** en el grupo "Bloques" (y en el menú "Más" en pantallas angostas).
  - Ícono `ImagePlus`, `aria-label="Insertar captura del caso"` y tooltip con el mismo texto.
  - Llama `props.onRequestImage?.()`. Si no hay `onRequestImage`, no se muestra.
  - Deshabilitado con tooltip "Máximo 20 imágenes por sección" si el documento ya tiene 20.
- **`CapturePickerDialog`** (`Dialog` de PrimeReact unstyled + pt existente; un solo diálogo en `ReportStep`):
  - **Modo `insert`:**
    - Título "Insertar captura del caso".
    - Grilla de `images` en el orden del servidor, con `role="listbox"`, `aria-label="Capturas del caso"`, roving tabindex, flechas en 2D, Home/End y Enter/Espacio para seleccionar.
    - Cada miniatura (`role="option"`, `aria-selected`) muestra la vista previa (`object-contain`) o un esqueleto, el nombre del archivo truncado con `title` completo y un chip `Tag` con `CAPTURE_ROLE_LABELS[role]` si tiene rol.
    - `available: false` → miniatura deshabilitada (`aria-disabled`) con el chip "No disponible" (D14).
  - **Descripción:** `InputText`, etiqueta "Descripción (epígrafe)", `maxLength={200}`, contador. Al confirmar se aplica `trim` y se colapsan los espacios. Los saltos de línea no entran.
  - **Vista previa del epígrafe** (`aria-live="polite"`): `Se verá como: "Figura N – {desc} ({archivo})"` (o la variante con rol / sin descripción de 6.5, con "N" literal).
  - **Botones** "Cancelar" e "Insertar". "Insertar" queda deshabilitado sin una captura seleccionada.
  - **Estados:**
    - Listado cargando: 8 esqueletos.
    - Error: "No se pudieron cargar las capturas" + "Reintentar".
    - Sin capturas: **"Este caso no tiene capturas. Las capturas se toman en el paso 3."**
  - **Modo `edit`:** título "Editar descripción". La captura se muestra fija (sin grilla) y solo se edita la descripción. El botón es "Guardar".
  - **Foco:** al abrir, va a la primera miniatura (o a la descripción en `edit`). Al cerrar, vuelve al editor (`editor.commands.focus()`).

### 7.6 Pegar y arrastrar (D10 de la HU)

Mensaje único, en el aviso de pegado de la base (`aria-live="polite"`, se va solo): **"Para insertar una imagen usá el botón Imagen"**.

- **`handlePaste`** (base §7.6.3): si `clipboardData.files` tiene algún `image/*`, se consume el evento **aunque** haya HTML o texto (una captura copiada al portapapeles suele traer los dos) y se muestra el aviso. Si no, cuando el HTML pegado trae `<img`, se pega el resto como en la base y el aviso es este, en lugar de "Se quitaron formatos…".
- **`handleDrop`** (nuevo en `editorProps`): `if (!moved && (event.dataTransfer?.files?.length || /<img/i.test(event.dataTransfer?.getData("text/html") ?? "")))` → `event.preventDefault()`, aviso y `return true`.
- El arrastre interno de un `reportImage` está deshabilitado (`draggable: false`).

### 7.7 `ReportStep`

- `const reportImages = useReportImages(caseId)` y `const [cache] = useState(() => new ReportImagePreviewCache(caseId))` (con `dispose` al desmontar).
- `<ReportImagesProvider value={{ caseId, images, cache, openPicker }}>` envuelve las 8 secciones. `RichTextEditor` se sigue cargando con `next/dynamic` (base D17). El provider vive en `ReportStep` y no importa Tiptap.
- `RichTextEditor` recibe `onRequestImage={() => openPicker({ mode: "insert", editorKey: key })}`. El editor expone su instancia para insertar mediante un `ref` o un callback `onEditorReady(key, editor)`, que `ReportStep` guarda en un `Map`. El implementador elige el mecanismo y lo deja anotado en el progress.
- Al confirmar el selector: `editor.chain().focus().insertReportImage({ filename, alt }).run()`, o `updateReportImageAlt(pos, alt)` en modo edición. El autoguardado sigue el flujo de la base (`onUpdate` → `onChange`).
- **Sin cambios** en la carga, la conversión de texto plano, el guardado, "Restaurar" ni el chequeo de largo.

### 7.8 Checklist de "Generar"

- `getMissingRequirements(cas, { reportImages })`: si `cas.report_texts?.formato === "markdown"` y vino `reportImages` (estado `ready`), para cada una de las 8 secciones:
  - `refs = extractReportImageRefs(text)`.
  - Si alguna `ref.filename` no está en `reportImages` con `available: true`, se agrega `report_texts.<clave>.imagen`.
- Sin `reportImages` (cargando o con error), no se agrega nada: el servidor es la autoridad y su 400 entra por `serverMissing`, como hoy.
- El enlace del checklist lleva al paso 4 con `focusFieldId = reportFieldId(clave)`, sin cambios en el mecanismo.

### 7.9 Estilos (`globals.css`)

Bajo `.fx-rte`:
- `figure[data-fx-report-image]` con margen vertical de un párrafo.
- La imagen con `max-width: 100%` dentro de la caja del 60 %.
- El epígrafe en `var(--fx-text-3)` y cursiva, en el tamaño chico del sistema.
- El recuadro "no disponible" con `border: 1px dashed var(--fx-border-strong)` y fondo `var(--fx-surface-2)`.
- La barra flotante con superficie y sombra de los overlays.

Solo tokens `--fx-*`, sin hex y sin `dark:`. Se revisa en claro y oscuro.

---

## 8. Decisiones técnicas

| # | Decisión | ¿Necesita al usuario? |
|---|---|---|
| D1 | Esquema `captura:` con nombre percent-encoded canónico (`[A-Za-z0-9._-]` sin codificar, el resto `%XX` en mayúsculas) y fixtures I1-I6 compartidas. Soporta nombres con espacios o paréntesis (archivos traídos del dispositivo cuyo nombre contiene "captura"). | No |
| D2 | La imagen es **siempre** un párrafo propio de nivel superior. El editor lo impone con el esquema (grupo `figure`) y el servidor rechaza cualquier otra posición. | No |
| D3 | Alt: texto literal de una sola línea, de 200 caracteres como máximo, sin título. El servidor lo exige igual. | No |
| D4 | Tope de 20 imágenes por sección (D9 de la HU). Servidor: 400 con mensaje propio. Editor: botón deshabilitado y filtro de transacciones. | No |
| D5 | La vista previa devuelve los bytes completos, con el tipo **detectado**, y un único 404 "Imagen no disponible" para todo lo que no sea una captura disponible (no filtra si el archivo existe). El nombre no plano da 400. Sin auditoría. | No |
| D6 | **Endpoint extra de solo lectura** `GET …/report-images`: es la única fuente de las capturas insertables, su rol y su disponibilidad. Lo usan el selector, el editor y el checklist, con la misma regla que la generación. La HU dejaba abierto pedir `GET /files`, pero así el checklist del cliente no puede detectar un archivo corrupto o de 0 bytes y bloquearía sin saber por qué. | No |
| D7 | **Sin tope de tamaño** (DP2 B, usuario). Detección de tipo por cabeceras con `ImageProbe.TryDetect(Stream)`, vista previa por streaming (`FileStreamResult` sobre el mismo `FileStream` validado) y embebido con `FeedData(stream)`: memoria constante con cualquier tamaño. El único límite real es el de Word con el DOCX completo (~512 MB), que ya existe hoy por el anexo y que esta HU no empeora (D9). | Resuelta: DP2 B |
| D8 | La generación va en dos fases: B-R4 deja un párrafo ancla y B-R7b inserta la imagen con `drawId`, la numeración del anexo y los hashes ya conocidos. El renderer de la base no hace E/S. | No |
| D9 | La imagen del cuerpo **reutiliza** el `ImagePart` que ya embebió B-R7 para esa captura (anexo o identificación), si el tipo coincide. Si no hay o el tipo no coincide, crea una parte nueva del tipo detectado, por streaming. Así el DOCX no duplica bytes en el caso normal y `ReplaceWithImages` queda intacto (solo se extrae un helper de `BuildImageParagraph`, sin cambio de comportamiento). | No |
| D10 | Epígrafes del cuerpo: `Figura N – {desc} ({archivo})`. Sin descripción, `Figura N – {archivo}`. Con rol, `Captura de identificación (IMEI y modelo \| nombre del dispositivo) – …`, sin número. N = posición en la lista del anexo. Formato del anexo (Arial 8 cursiva `#595959`) y `keepNext` en la imagen. | No (fijado por D4 y D5 de la HU) |
| D11 | Bloqueo: `missing` `report_texts.<clave>.imagen`, una por sección, en las ocho secciones y solo con formato Markdown. El servidor revisa antes de pasar a `Generating`. Si una captura desaparece entre el chequeo y el DOCX, la generación falla limpia (caso en `Error`, evidencia intacta). | No |
| D12 | Obligatorias: una imagen sola **no** cuenta como texto (`missing`, D12 A de la HU). Opcionales (Objeto, Notas técnicas, Reserva): el bloque sale si hay texto **o** imágenes (`HasBlockContent`, 4.7), y una sección opcional con solo imágenes sale con sus figuras. | Resuelta: DP1 B |
| D13 | En el editor, el epígrafe provisorio de una captura con rol muestra "Identificación (…) · …" en vez de "Figura · …", como va a salir en el DOCX. | No (UX menor, coherente con D4 de la HU) |
| D14 | El selector muestra deshabilitadas, con el chip "No disponible", las capturas que no se pueden insertar (vacías, ilegibles). | No (UX menor) |
| D15 | Copiar y pegar un bloque de imagen **dentro** del editor está permitido: es la misma referencia, no una imagen nueva. Las imágenes externas (archivos, `<img>`, arrastre) se bloquean con el aviso de D10 de la HU. | No |
| D16 | Una caché de vistas previas por visita al paso 4, compartida entre las 8 secciones y el selector. Los blob URLs se revocan al salir del paso y la carga es perezosa por viewport. | No |
| D17 | Validación al guardar **sin** mirar los archivos (solo sintaxis y nombre). La existencia se verifica al listar y al generar. | No |
| D18 | Texto plano (sin `formato`): sin cambios, `![…](…)` sale literal (camino `ReplaceParagraphPerLine`). | No |
| D19 | Nada cambia en el anexo, la identificación, la tabla de hashes, el ZIP, `report_hash`, `DownloadAsync` ni `UploadFileAsync`. | No |

## 9. Puntos de contacto con `editor-texto-enriquecido` (diseñados contra su SDD)

| Base | Qué cambia esta HU | Riesgo si la base se implementó distinto |
|---|---|---|
| §4.2 tabla de errores | El motivo `imagen` pasa a significar "imagen inválida" (antes era "cualquier imagen"). Se suma el mensaje de tope | Ninguno: el texto es el mismo |
| §4.3 dialecto | Se suma la fila de imagen (4.1) | — |
| §4.5 `IsBlank` / `isBlankReportText` | Se ignoran las imágenes (4.7). En TS, `stripReportImages` antes de las regex | Si la base implementó `isBlankMarkdown` con otra estrategia, igual hay que aplicar `stripReportImages` primero |
| §6.2 `Validate` | Rama `LinkInline { IsImage: true }` (6.4.1), constante `TooManyImages` y `ExtractImages` | Si `Validate` devuelve otro tipo (record o enum), se adapta conservando los mensajes de 4.3 |
| §6.3 renderer | Parámetro `images`, ancla y "el ancla no es vacía" en el recorte (6.4.2). Reemplaza la rama D19 | Si el recorte de vacíos se hace sobre el XML ya emitido, el ancla tiene que reconocerse por identidad (está en `resolved` y en `images`) |
| §6.4 B-R4 | Se pasa `bodyImages`. B-R7b nueva | — |
| §6.4 condiciones de bloque | Las tres de los opcionales pasan de `!ReportTextRules.IsBlank` a `ReportTextRules.HasBlockContent` (DP1 B) | Si la base las calculó en otro lugar, se cambian ahí; `ValidateForGenerate` sigue con `IsBlank` |
| T4 de la base | `![a](https://x/a.png)` sigue dando `imagen`: el test de la base no cambia | — |
| §7.3 extensiones | `StarterKit document: false` + `FxDocument`, `ReportImage`, `InlineImageAsText`. El filtro de tope convive con `MaxMarkdownLength` | Si la base usa otro mecanismo de tokenizer, la regla es la misma: un bloque que cumple 4.8.3 → nodo, y cualquier otra imagen → texto |
| §7.4 props | `onRequestImage?` y el mecanismo para exponer la instancia (7.7) | — |
| §7.5 barra | Botón "Imagen" en "Bloques" | — |
| §7.6 pegado | Aviso D10 para archivos `image/*` y `<img` (reemplaza al genérico en ese caso) y `handleDrop` | — |

Si al arrancar esta HU el código de la base difiere de su SDD en alguno de estos puntos, el implementador lo anota en su progress y adapta **sin cambiar el contrato** de la sección 4.

## 10. Decisiones para el usuario

Las dos DP (DP1 y DP2) están resueltas por el usuario: ver "Resolución de decisiones" al final. No quedan DP abiertas.

---

## 11. Checklist atómico

### 11.1 Backend (`implementer-backend`). Regla dura de datos de `AGENTS.md`: no se toca ningún documento existente ni archivo de `Storage:DataDirectory`; sin migraciones; los archivos de evidencia solo se leen (`FileShare.Read`)

- [ ] B1. Confirmar que la base (`editor-texto-enriquecido`) está mergeada en la rama de trabajo y anotar en el progress cualquier diferencia con la sección 9.
- [ ] B2. `ReportImageRef.cs` (6.2): constantes, `EncodeName`, `TryParse` (decode estricto), `IsInsertableName`, `Format`.
- [ ] B3. `ImageProbe.TryDetect(Stream)` (overload; el de `byte[]` sin cambios) y `ReportImageFiles.cs` (6.3): `Inspect`, `TryOpen` (stream abierto con `FileShare.Read` y rebobinado), chequeo de primer nivel, sin tope de tamaño.
- [ ] B4. `ReportMarkdown.Validate`: rama de imagen 1-6 y `TooManyImages` (6.4.1).
- [ ] B5. `ReportMarkdown.IsBlank` ignora imágenes, se agrega `ReportMarkdown.ExtractImages` y `ReportTextRules.HasBlockContent` (4.7).
- [ ] B6. `ReportMarkdownRenderer`: parámetro `images`, ancla, recorte de vacíos que respeta anclas, defensa D19 (6.4.2).
- [ ] B7. `ReportService`: condiciones de bloque de los opcionales con `HasBlockContent`; `bodyImages` en B-R4; `InsertBodyImages` (B-R7b) con epígrafes D10, reutilización del `ImagePart` (D9) o parte nueva por streaming, helper `BuildImageParagraphForRel`, `pPr` del anexo, `resolved` y `throw` defensivos (6.5). `ReplaceWithImages` **sin cambios**.
- [ ] B8. `CaseValidation`: mensaje de tope, `ReportImageKey`, `BrokenImageKeys` (6.6).
- [ ] B9. `CaseService`: `ListReportImagesAsync`, `GetReportImagePreviewAsync` y el bloqueo en `GenerateAsync` antes de `Generating` (6.6). Las dos firmas nuevas van también en `ICaseService`.
- [ ] B10. `DTOs/CaseDtos.cs`: `ReportImageDto`, `ReportImagesResponse`.
- [ ] B11. `CasesController`: las dos acciones con sus headers (6.7).
- [ ] B12. Tests de 11.3 en verde. `dotnet build` de los dos `.csproj` del backend sin warnings nuevos.
- [ ] B13. Prueba manual con Swagger o curl **sobre un caso en borrador creado por la prueba** (anotar su `_id`). Se suben al caso **de prueba** dos PNG ficticios `screenshot_*.png`, un `.png` que en realidad es texto y uno de 0 bytes.
  - `GET report-images`: los cuatro, dos de ellos `available: false`.
  - `preview` de cada uno: 200 con `Content-Type` correcto en los dos válidos; 404 en los otros dos.
  - `preview` de `..`: 400.
  - `preview` con el token de **otro** usuario de desarrollo: 403.
  - `PUT` con I6: 200. `PUT` con `![x](https://…)`: 400.
  - Generar con una referencia al de 0 bytes: 400 con `report_texts.resultados.imagen`.
  - Quitar esa referencia y generar: 200. Abrir el DOCX en Word o LibreOffice y comprobar la imagen y su epígrafe en Resultados.
  - Al terminar, borrar **solo** ese caso por su `_id` y **solo** su carpeta `cases/<_id>` (o dejarlos y anotarlos en el progress).
- [ ] B14. `progress/impl_backend_editor-imagenes-informe.md` con los archivos, la salida de los tests y la verificación.

### 11.2 Frontend (`implementer-frontend`, app **`client/`** solamente)

Skills obligatorios: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (como criterio, sin 3D) y `web-design-guidelines` (autochequeo al final). Se suman `ui-styling` y `mblode-agent-skills-ui-animation` para el diálogo, la barra flotante y los esqueletos.

- [ ] F1. Leer `client/AGENTS.md`. Confirmar que la base está en la rama y anotar las diferencias con la sección 9.
- [ ] F2. `lib/api.ts` + `types/index.ts`: `ReportImage`, `listReportImages`, `getReportImagePreview` (blob, sin `Content-Type`).
- [ ] F3. `lib/report-markdown.ts`: constantes, `encodeReportImageName`, `decodeReportImageName`, `formatReportImageMarkdown`, `extractReportImageRefs`, `stripReportImages`, y `isBlankMarkdown` con `stripReportImages`.
- [ ] F4. Verificar con un script descartable (`npx tsx`, **sin commitearlo**) y pegar en el progress:
  - `formatReportImageMarkdown` da I1-I5 exactos.
  - `extractReportImageRefs(I6)` → 1 ref con el archivo y el alt de I1.
  - `extractReportImageRefs` de I1 dentro de un fence ```` ``` ```` → 0.
  - `isBlankMarkdown(I1) === true` e `isBlankMarkdown(I6) === false`.
  - `decodeReportImageName("%FF.png") === null`.
- [ ] F5. `extensions.ts`: `FxDocument` (grupo `figure`), `ReportImage` (tokenizer de bloque, render, comandos, keymap), `InlineImageAsText` y tope de 20.
- [ ] F6. En el navegador: roundtrip byte a byte de I1-I6 (`setContent` → `getMarkdown`). Comprobar que no se puede insertar dentro de una lista ni de una cita (se inserta después del bloque de nivel superior) y que `![x](https://a/b.png)` y `Texto ![x](captura:y)` quedan como texto. Pegar la salida en el progress.
- [ ] F7. `lib/report-images.ts`: `useReportImages` y `ReportImagePreviewCache` (dedupe, abort, revocación).
- [ ] F8. `ReportImagesContext.tsx` y `ReportImageView.tsx`: estados de 7.3, epígrafe D13, barra flotante y accesibilidad.
- [ ] F9. `CapturePickerDialog.tsx` (7.5): grilla navegable con teclado, chips de rol y de "No disponible", descripción con contador, vista previa del epígrafe, estados vacío, carga y error, y modo edición.
- [ ] F10. `EditorToolbar`: botón "Imagen" (también en "Más"), deshabilitado al llegar al tope. `RichTextEditor`: prop `onRequestImage`, exposición de la instancia y `handleDrop`. `pasteTransform`/`handlePaste`: aviso D10.
- [ ] F11. `ReportStep.tsx` (7.7): provider, caché con `dispose` y un único diálogo.
- [ ] F12. `lib/pericial.ts`: `REPORT_SECTION_LABELS`, `MissingKey` + `REQUIREMENT_META` con las 8 claves `.imagen`, y `getMissingRequirements(cas, { reportImages })`. `GenerateStep`: `useReportImages`.
- [ ] F13. `globals.css` (7.9), en claro y oscuro.
- [ ] F14. Comprobar en la pestaña Network:
  - Abrir el paso 4 de un caso con 3 imágenes en distintas secciones → **un** `report-images` y **una** `preview` por captura distinta, sin repetir al seleccionar o editar.
  - Al salir del paso no quedan blob URLs vivos (`URL.revokeObjectURL` llamado).
  - Con Tatana cerrado, las imágenes se ven igual.
- [ ] F15. `npx tsc --noEmit` limpio y `npm run build` sin errores.
- [ ] F16. `progress/impl_frontend_editor-imagenes-informe.md`: archivos, salidas de F4 y F6, el mecanismo elegido en 7.7 y los skills invocados con sus hallazgos.

### 11.3 Tests xUnit (`server/tests/Factum.Backend.Tests/`)

Datos ficticios, una carpeta temporal propia por test (como `ReportDesignTests`), sin Mongo ni `Storage`. Si hace falta, se agrega a `TestImages` un `Jpeg(w, h)` mínimo (SOI + APP0 + SOF0 + EOI) que `ImageProbe` reconozca; no hace falta que se pueda decodificar. Se reutilizan o extraen los fakes (`FakeSettings`, `FakeBranding`, `MakeCase`) igual que en la base.

`ReportImageRefTests.cs`:
- [ ] T1. `Format`/`EncodeName` producen I1-I5 byte a byte. `TryParse` sobre el destino de cada uno devuelve el archivo original.
- [ ] T2. `TryParse` rechaza cada destino de la lista "Rechazadas" de 4.1 que es un problema de URL o de nombre. `IsInsertableName` rechaza `foto_funcionario_*.jpg`, `foto_denunciante_*.jpg`, `adjunto_1.png`, `screenshot.mp4`, `informe_pericial_x.docx`, `evidencia_x.zip`, `a\b_captura.png`, `..`, `""` y un nombre de 256 caracteres.
- [ ] T3. Markdig: `LinkInline.Url` de I3 conserva `%20`/`%28`/`%29` (fija el comportamiento que asume 6.2).

`ReportMarkdownImagesTests.cs`:
- [ ] T4. `Validate` acepta I1, I2, I6, una sección con 20 imágenes y la mezcla de I6 con listas, cita y código de la base.
- [ ] T5. `Validate` rechaza con `imagen` cada ejemplo de 4.1 "Rechazadas", y con `TooManyImages` una sección con 21.
- [ ] T6. `IsBlank(I1) == true`, `IsBlank(I6) == false`, `IsBlank("&nbsp;\n\n" + I1) == true`. `ReportTextRules.IsBlank` con `Formato` null y el texto I1 → `false` (texto plano: hay letras).
- [ ] T7. `ExtractImages(I6)` → `[("screenshot_20261001_101530.png", "Chat con Juan")]`. El alt de I4 aplanado es `a_b *c* [d] <e> & f`. Una imagen dentro de un fence → 0.
- [ ] T8. `CaseValidation.ValidateReportTexts` con Markdown y `![x](file:///etc/passwd)` en `resultados` → `El campo resultados tiene contenido no permitido: imagen`. Con 21 imágenes → `El campo resultados supera las 20 imágenes`. Con texto plano (`formato` null) y la misma imagen → OK.
- [ ] T9. `BrokenImageKeys`: con `isAvailable` falso para un archivo referenciado en `resultados` y en `reserva` → `["report_texts.resultados.imagen", "report_texts.reserva.imagen"]`, en ese orden. Dos rotas en la misma sección → una sola clave. `Formato` null → vacío.

`ReportImageFilesTests.cs`:
- [ ] T10. `Inspect`/`TryOpen`:
  - PNG válido → `Available`, `image/png` y su tamaño.
  - JPEG válido llamado `.png` → `Available` `image/jpeg`.
  - Inexistente → `Missing`.
  - 0 bytes → `Empty`.
  - Texto llamado `.png`, GIF (`GIF89a…`), SVG (`<svg …>`) y WebP (`RIFF…WEBP`) llamados `screenshot_x.png` → `NotAnImage`.
  - Un PNG válido de cabecera seguido de relleno hasta **600 MB** (con `SetLength`, archivo disperso) → `Available` (sin tope). La memoria administrada del proceso no crece en proporción (`GC.GetTotalMemory` antes/después, con un margen de unos pocos MB).
  - `foto_funcionario_1.jpg` válido → `NotInsertable`.

  Se comprueba que **ningún** archivo cambió (SHA-256 y `LastWriteTimeUtc` antes y después).
- [ ] T11. `ImageProbe.TryDetect(Stream)`: un JPEG con un APP1 de 300 KB y otro de 60 KB antes del SOF → lo reconoce. Para PNG/JPEG válidos e inválidos da el mismo resultado que `TryDetect(byte[])` (`[Theory]` con los mismos bytes). `TryOpen` devuelve el stream en la posición 0 y un `File.OpenWrite` sobre el archivo mientras está abierto lanza `IOException`.

`ReportBodyImagesDocxTests.cs` (con `ReportService.GenerateAsync` y la v6, en carpeta temporal):
- [ ] T12. Caso Markdown con `screenshot_20261001_101010.png` y `screenshot_20261001_101530.png` (sin rol) y `Resultados = I6` con el archivo `…101530`:
  - En el cuerpo, después del párrafo "Se observa la conversación:", hay un párrafo con `w:drawing` centrado, con `keepNext`, extent ≤ 14 × 10.5 cm y la proporción conservada.
  - Le sigue el epígrafe `Figura 2 – Chat con Juan (screenshot_20261001_101530.png)`.
  - Después viene "Fin.".
  - El anexo sigue con `Figura 1 – …101010.png` y `Figura 2 – …101530.png`.
- [ ] T13. **Bytes:** el SHA-256 del `ImagePart` referido por el `r:embed` del dibujo del cuerpo es igual al SHA-256 de la captura (calculado **antes** de generar, porque los sueltos se borran después) y al de la fila de la tabla de hashes. **Reutilización (D9):** el `r:embed` del cuerpo es el **mismo** `relId` que el de esa captura en el anexo, y la cantidad de `ImagePart` del documento es igual a la del mismo caso sin la imagen en el cuerpo. Una captura `.png` cuyo contenido es JPEG → parte nueva `image/jpeg`, con el mismo SHA-256.
- [ ] T14. **Invariantes:** la misma evidencia generada con `Resultados` = I6 y con `Resultados` = solo el texto (sin imagen) da:
  - Filas de la tabla de hashes (nombre + hash) idénticas.
  - Lista de entradas del ZIP idéntica.
  - Mismo cálculo de `report_hash` (SHA-256 del DOCX en disco = `ReportHash`).
  - Párrafos del anexo idénticos.
- [ ] T15. **Rol:** la captura del cuerpo con rol `imei_modelo` → epígrafe `Captura de identificación (IMEI y modelo) – Chat con Juan (…)`. El anexo se numera como hoy (sin esa captura) y la otra captura pasa a ser `Figura 1`.
- [ ] T16. **Alt vacío** (I2) → `Figura N – screenshot_….png`. Un alt `{caratula}` sale literal (sin reemplazar) y no genera un warning de B-R9.
- [ ] T17. **Validez:** `docPr id` únicos en todo el documento. `OpenXmlValidator` (`Office2019`, las mismas claves que `ValidationKeys`) sin errores nuevos respecto del mismo caso sin imágenes en el cuerpo.
- [ ] T18. **Texto plano:** `Formato = null` y `Resultados = "Ver ![x](captura:screenshot_20261001_101530.png)"` → el párrafo tiene ese texto literal y la cantidad de `w:drawing` del cuerpo es la misma que sin esa línea.
- [ ] T19. **Carrera:** se borra el archivo referenciado justo antes del DOCX (`GenerateAsync` con `files` que lo incluyen pero sin el archivo en disco) → excepción, no quedan `evidencia_*.zip` ni `informe_pericial_*.docx` en la carpeta y los demás archivos sueltos siguen con su SHA-256 original.
- [ ] T20. **Sección opcional con solo imagen** (DP1 B): `Reserva` = I1 → el bloque `descripcionReserva` **está** en el DOCX, con su título, la imagen y el epígrafe `Figura N – Chat con Juan (…)`. Lo mismo con `ObjetoInforme` = I1. `Reserva` = `"&nbsp;"` (sin imagen) → el bloque se borra, como en la base. `Conclusiones` = I1 (obligatoria) → `CaseValidation.ValidateForGenerate` sigue listando `report_texts.conclusiones`.

---

## 12. Verificación

### 12.1 Antes de declararse `done`

| Implementador | Comandos |
|---|---|
| backend | `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` · `dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj` (todo en verde, incluidos los de la base, `ReportDesignTests` y `EvidenceZipTests`) · `python3 ops/plantilla/build_plantilla_v6.py --check server/src/Factum.Backend/Templates/plantilla_informe_v4.docx server/src/Factum.Backend/Templates/plantilla_informe_v6.docx` → `OK` (sin cambios) · B13 |
| frontend | `cd client && npx tsc --noEmit` · `npm run build` · checks F4, F6 y F14 |

### 12.2 Prueba manual para el usuario

1. **Insertar:** en un caso en borrador con al menos dos capturas subidas (una con rol "IMEI y modelo"), ir al paso 4.
   - En "Resultados", escribir un párrafo y apretar **Imagen**: se ven las miniaturas con su nombre y el chip de rol.
   - Elegir una sin rol, escribir "Chat con Juan" e insertar: aparece debajo del párrafo con "Figura · Chat con Juan (screenshot_…png)".
   - Esperar a "Guardado", ir a otro paso y volver: la imagen sigue.
2. **Editar y quitar:**
   - Clic en la imagen → "Editar descripción" → cambiarla.
   - Seleccionarla y apretar Supr: se va. Ctrl/Cmd+Z: vuelve.
   - Comprobar en el paso 3 que la captura sigue en la bandeja.
3. **Pegar o arrastrar:** pegar una captura del portapapeles y arrastrar un PNG desde el escritorio: no entra nada y aparece "Para insertar una imagen usá el botón Imagen".
4. **Sin agente:** cerrar Tatana y recargar el paso 4: las imágenes se siguen viendo.
5. **Sección opcional con solo una imagen:** en "Notas técnicas", borrar el texto y dejar solo una captura. Al generar, la sección sale con su título y la figura. En cambio, dejar "Conclusiones" con solo una imagen: "Generar" la marca como faltante.
6. **Caso sin capturas:** en otro borrador sin capturas, "Imagen" muestra "Este caso no tiene capturas. Las capturas se toman en el paso 3."
7. **Generar:** generar el informe y abrir el DOCX en Word (y, si se puede, en LibreOffice y WPS).
   - Abre **sin aviso de reparación**.
   - En Resultados, la captura sale centrada después del párrafo, con "Figura N – Chat con Juan (archivo)". N es el mismo número que tiene en el Anexo.
   - La tabla de hashes y el anexo están como siempre.
   - El PDF muestra lo mismo.
8. **Referencia rota:** solo es posible simularla en un caso de prueba. Por Swagger, `PUT …/report-texts` con `formato: "markdown"` y `Resultados = "Texto\n\n![x](captura:screenshot_no_existe.png)"`.
   - El paso 4 muestra "Imagen no disponible: screenshot_no_existe.png" con "Quitar".
   - "Generar" lista "Imagen no disponible en «Resultados»", con el enlace a la sección.
   - Al quitarla, se puede generar.
9. **Seguridad:** por Swagger, `PUT` con `![x](https://sitio/imagen.png)` → 400. `GET …/files/..%2F/preview` y `GET …/files/<un video>/preview` → error sin imagen. Con otro usuario, `GET …/files/<captura>/preview` → 403.
10. **Casos generados:** un caso `Completed` sigue abriendo y descargando su ZIP/DOCX/PDF igual que antes.

## Resolución de decisiones (2026-10-01)

- **DP1 (D12) → B, por el usuario** (no era la recomendada). Una sección **opcional** (Objeto del informe, Notas técnicas, Reserva) que tiene solo imágenes **no** cuenta como vacía: sale en el DOCX con sus figuras y epígrafes (`ReportTextRules.HasBlockContent`, 4.7 y 6.5). Las **obligatorias** siguen con D12 A de la HU: una imagen sola cuenta como vacía para el checklist y para `missing`.
- **DP2 (D7) → B, por el usuario** (no era la recomendada). Sin tope de tamaño: cualquier captura del caso se puede insertar en el cuerpo. Se resuelve sin limitar al usuario: detección por cabeceras (`ImageProbe.TryDetect(Stream)`), vista previa por streaming y embebido por streaming, más la reutilización del `ImagePart` del anexo (6.3, 6.5, D7, D9). El único límite técnico es el de Word con el DOCX completo (~512 MB). Ya existe hoy por el anexo y esta HU no lo empeora, así que no hace falta una DP nueva.
