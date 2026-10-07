# impl_backend — editor-imagenes-informe

**Estado:** `done` (sin commit, como pidió el orquestador)
**Rama:** `feat/editor-imagenes-informe` (la base `editor-texto-enriquecido` ya estaba commiteada: 453f79f)
**SDD:** `Refactorizaciones/editor-imagenes-informe.md`. Decisiones: las recomendadas, más DP1 B y DP2 B.
**Fecha:** 2026-10-01/02

## Checklist backend (SDD §11.1)

- [x] B1. La base está en la rama. Las diferencias con §9 están en "Puntos de contacto" (más abajo).
- [x] B2. `ReportImageRef.cs`: constantes, `EncodeName`, `TryParse` (decode a mano + UTF-8 estricto), `IsInsertableName`, `Format`, y además `IsPlainName` (público).
- [x] B3. `ImageProbe.TryDetect(Stream, …)` (overload nuevo; el de `byte[]` no se tocó). `ReportImageFiles.cs` con `Inspect` y `TryOpen` (FileShare.Read, rebobinado a 0, primer nivel, sin tope de tamaño).
- [x] B4. `ReportMarkdown.Validate`: rama de imagen 1-6 y `TooManyImages = "imagenes"`.
- [x] B5. `IsBlank` ignora el subárbol de toda imagen. Nuevos `ExtractImages` y `ReportTextRules.HasBlockContent`.
- [x] B6. `ReportMarkdownRenderer`: parámetro opcional `images`, ancla, recorte que respeta anclas, defensa D19 (alt literal) cuando `images` es null o la imagen no es válida.
- [x] B7. `ReportService`: `HasBlockContent` en las tres condiciones de bloque opcionales, `bodyImages` en B-R4, `InsertBodyImages` (B-R7b) y el helper `BuildImageParagraphForRel`. `ReplaceWithImages` sin cambios.
- [x] B8. `CaseValidation`: `TooManyImagesMessage`, `ReportImageKey`, `BrokenImageKeys`.
- [x] B9. `CaseService`: `ListReportImagesAsync`, `GetReportImagePreviewAsync` (también en `ICaseService`) y el bloqueo en `GenerateAsync` antes de `Generating`.
- [x] B10. `DTOs/CaseDtos.cs`: `ReportImageDto`, `ReportImagesResponse`.
- [x] B11. `CasesController`: `ReportImages` y `ReportImagePreview`, con sus headers.
- [x] B12. Tests T1-T20 en verde. Build sin warnings nuevos.
- [x] B13. Prueba manual contra una instancia aislada (ver abajo).
- [x] B14. Este archivo.

## Archivos tocados

Nuevos:
- `server/src/Factum.Backend/Services/Reports/ReportImageRef.cs`
- `server/src/Factum.Backend/Services/Reports/ReportImageFiles.cs`
- `server/tests/Factum.Backend.Tests/ReportImageRefTests.cs` (T1-T3)
- `server/tests/Factum.Backend.Tests/ReportMarkdownImagesTests.cs` (T4-T9)
- `server/tests/Factum.Backend.Tests/ReportImageFilesTests.cs` (T10-T11)
- `server/tests/Factum.Backend.Tests/ReportBodyImagesDocxTests.cs` (T12-T20, más la muestra con `FACTUM_RENDER_DIR`)

Modificados:
- `server/src/Factum.Backend/Services/Branding/ImageProbe.cs`: overload `TryDetect(Stream, …)`.
- `server/src/Factum.Backend/Services/Reports/ReportMarkdown.cs`: `ParseWithImages`/`BlockImage` (internos), `Validate`, `IsBlank`, `ExtractImages`, `TooManyImages`, `ReportTextRules.HasBlockContent`.
- `server/src/Factum.Backend/Services/Reports/ReportMarkdownRenderer.cs`: parámetro `images`, anclas, record `PendingReportImage`.
- `server/src/Factum.Backend/Services/Reports/ReportService.cs`: condiciones de bloque, B-R4, B-R7b `InsertBodyImages`, `BuildImageParagraphForRel`.
- `server/src/Factum.Backend/Services/Cases/CaseValidation.cs`
- `server/src/Factum.Backend/Services/Cases/CaseService.cs`
- `server/src/Factum.Backend/Controllers/CasesController.cs`
- `server/src/Factum.Backend/DTOs/CaseDtos.cs`
- `server/tests/Factum.Backend.Tests/TestImages.cs`: `Jpeg(w, h, params int[] appSegments)` mínimo (SOI + APP0 + APPn + SOF0 + EOI).

No se tocaron: `client/`, `agent-ui/`, `server/src/Factum.Agent/` (su `appsettings.json` ya venía modificado de antes), `appsettings.Local.json`, plantillas, `ops/plantilla/`, `backlog.json`, `progress/current.md`.

## Contrato compartido: confirmación (SDD §4)

| Punto | Implementado | Coincide |
|---|---|---|
| 4.1 Esquema | `![<alt>](captura:<nombre>)`, `captura:` en minúsculas y ordinal, `%XX` en mayúsculas al emitir (minúsculas aceptadas al leer), alt ≤ 200 (después de trim y de colapsar espacios), sin título, tope 20 | Sí. `Format` produce I1-I5 byte a byte (T1) |
| 4.2 Insertable / disponible | `ReportImageRef.IsInsertableName` / `ReportImageFiles.Inspect` | Sí |
| 4.3 Errores PUT | `El campo {clave} tiene contenido no permitido: imagen` y `El campo {clave} supera las 20 imágenes` | Sí (también probado por HTTP en B13) |
| 4.4 `GET /api/cases/{id}/report-images` | `{ "images": [{ "filename", "size", "role", "available", "width", "height" }] }`, orden ordinal, `Cache-Control: no-store`, 403 sin body, 404 `{ "error": "Caso no encontrado" }` | Sí (JSON real en B13) |
| 4.5 `GET /api/cases/{id}/files/{filename}/preview` | 200 por streaming con el tipo detectado y los headers `Content-Disposition: inline`, `X-Content-Type-Options: nosniff`, `Cache-Control: private, no-store`, `Content-Security-Policy: default-src 'none'; sandbox`. 400 `{ "error": "Nombre de archivo inválido" }`. 403 sin body. 404 `{ "error": "Imagen no disponible" }` | Sí |
| 4.6 `missing` | `report_texts.<clave>.imagen`, una por sección, en el orden de las secciones, después de las claves de siempre, solo con `formato = "markdown"` | Sí |
| 4.7 Vacío | Obligatorios: `IsBlank` (una imagen sola = vacío). Opcionales: `HasBlockContent` | Sí |

Del lado del cliente ya existen en `client/src/lib/api.ts` las llamadas a `/report-images` y `/files/${encodeURIComponent(filename)}/preview`, y en `client/src/lib/pericial.ts` las claves `report_texts.${k}.imagen`. Coinciden con lo de arriba.

## Verificación

### `dotnet build server/src/Factum.Backend/Factum.Backend.csproj --no-incremental`
```
    4 Advertencia(s)
    0 Errores
```
Las 4 son NU1902/NU1903 (SharpCompress / Snappier), que ya estaban, contadas dos veces. No hay ningún `warning CS`.

### `dotnet build server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj --no-incremental`
Solo los dos `warning CS8714`/`CS8619` de `EvidenceZipTests.cs`, que ya estaban antes de esta HU. Los tests nuevos no agregan warnings.

### `dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj`
```
Correctas! - Con error:     0, Superado:   335, Omitido:     0, Total:   335, Duración: 3 s
```
Antes de esta HU había 209 tests. Siguen en verde los de la base, `ReportDesignTests` y `EvidenceZipTests`. Después del ajuste de T10 (ver decisión 7) corrí la suite completa 6 veces seguidas, sin fallas.

### `python3 ops/plantilla/build_plantilla_v6.py --check …v4.docx …v6.docx`
```
OK
```
La plantilla no se tocó.

### Muestras (datos ficticios)
Carpeta: `/private/tmp/claude-501/-Users-joelmiguelserrudo-Documents-Projects-Factum/d331c5ef-f445-4af8-98b0-4d017b56de51/scratchpad/muestras-imagenes/`

- `informe_imagenes_en_el_cuerpo.docx` y `.pdf` (con `soffice --headless`). Contenido:
  - En Resultados, tres figuras: dos sin rol (Figura 3, Figura 4) y una con rol IMEI.
  - En Valoración técnica, un archivo con espacios y paréntesis (`Captura de pantalla (1).jpg`, Figura 1).
  - Notas técnicas con solo una imagen y sin alt (DP1 B: la sección sale).
  - El anexo con Figura 1-4. Los números del cuerpo coinciden con los del anexo.
  - `zoom-06.png` y `zoom-07.png` son dos páginas renderizadas del PDF.
- `b13_generado_por_api.docx`: el DOCX que generó la API en B13.

### B13: prueba manual por HTTP
Se hizo contra una **instancia propia y aislada** del backend: build en una carpeta del scratchpad, `PORT=5099`, `MongoDb__DatabaseName=factum_b13_imagenes` (base nueva, solo para esta prueba) y `Storage__DataDirectory` en una carpeta temporal del scratchpad. No se tocó `factum_dev`, la carpeta `dev-data` ni el backend del usuario (PID 96718, sigue corriendo). La instancia se cortó al terminar. Se borró **solo** la base `factum_b13_imagenes`: tenía 1 caso, 1 perfil y 5 catálogos, todos creados por la prueba. También se borró su carpeta de datos. Las bases que quedan son las mismas de antes (`admin, config, evidentia, evidentia_dev, factum_dev, local`).

Usuarios dev ficticios: `30111222` (dueño) y `30999888` (otro). Caso de prueba `4a3622dc-7bc6-4c77-a9bd-33fd4db4c0ff` (ya borrado con la base).

```
report-images 200 no-store {"images":[
  {"filename":"screenshot_20261001_101010.png","size":187,"role":"imei_modelo","available":true,"width":60,"height":120},
  {"filename":"screenshot_20261001_101530.png","size":191,"role":null,"available":true,"width":120,"height":60},
  {"filename":"screenshot_texto.png","size":28,"role":null,"available":false,"width":null,"height":null},
  {"filename":"screenshot_vacio.png","size":0,"role":null,"available":false,"width":null,"height":null}]}
preview screenshot_20261001_101010.png 200 image/png inline nosniff private, no-store default-src 'none'; sandbox bytes-ok
preview screenshot_20261001_101530.png 200 image/png inline nosniff private, no-store default-src 'none'; sandbox bytes-ok
preview screenshot_texto.png 404 {"error":"Imagen no disponible"}
preview screenshot_vacio.png 404 {"error":"Imagen no disponible"}
preview a%5Cb_screenshot.png 400 {"error":"Nombre de archivo inválido"}
preview screenshot_%01.png   400 {"error":"Nombre de archivo inválido"}
preview <256 caracteres>     400 {"error":"Nombre de archivo inválido"}
preview ..  /  %2E%2E        404 sin body (Kestrel normaliza el segmento de punto: la ruta no llega a la acción)
preview ..%2F..%2Fappsettings.json 404 {"error":"Imagen no disponible"} (ASP.NET deja %2F codificado: es un nombre plano que no es captura)
preview foto_funcionario_1.jpg 404 {"error":"Imagen no disponible"}
preview con el token de otro usuario 403 (sin body)
report-images con el token de otro usuario 403 (sin body)
report-images de un caso inexistente 404 {"error":"Caso no encontrado"}
PUT I6 200
PUT ![x](https://sitio/imagen.png) 400 {"error":"El campo resultados tiene contenido no permitido: imagen"}
PUT 21 imágenes 400 {"error":"El campo resultados supera las 20 imágenes"}
PUT con referencias al de 0 bytes (resultados) y a uno inexistente (reserva) 200
generate 400 {"error":"Faltan datos obligatorios","missing":["report_texts.resultados.imagen","report_texts.reserva.imagen"]}
PUT I6 (sin las rotas) 200 → generate 200; el SHA-256 del DOCX descargado es igual a report_hash
DOCX: "Se observa la conversación:" → [imagen] → "Figura 1 – Chat con Juan (screenshot_20261001_101530.png)"
preview después de Completed 404 {"error":"Imagen no disponible"}
```

## Decisiones no obvias

1. **Chequeo extra del párrafo-imagen: la línea de origen.** Además de las reglas 1-6 de §6.4.1, el servidor exige otras dos cosas:
   - El párrafo ocupa **una sola línea** de origen y esa línea cumple la regex del escáner del cliente (§4.8, paso 3).
   - Las líneas de antes y de después están vacías (solo espacios) o son el inicio o el fin del texto.

   Así el servidor rechaza lo que el escáner de líneas del cliente no reconocería y el AST de Markdig sí:
   - `![x](<captura:…>)` y `![x]( captura:… )`
   - imágenes por referencia (`![x][r]` + `[r]: captura:…`)
   - una imagen con sangría
   - una imagen pegada a una lista sin línea en blanco (`![x](…)\n- uno`)

   Con eso, `ExtractImages` (servidor) y `extractReportImageRefs` (cliente) dan lo mismo para todo texto que el servidor acepta, que es lo que pide §4.8. Solo rechaza más, nunca acepta algo nuevo. Pasan todas las fixtures I1-I6. "Línea vacía" = solo espacios, igual que el `trim() === ""` del cliente (`client/src/lib/report-markdown.ts`, líneas 267-268) y que Markdig.
2. **`paragraph.Lines` no sirve** (Markdig 1.4 lo libera después de procesar los inlines: `Count = -1`). Por eso la "una sola línea" se mira con el `Span` del párrafo sobre el texto que se parseó (`ParseWithSource`).
3. **`Validate`, `ExtractImages` y el renderer comparten una sola función** (`ReportMarkdown.ParseWithImages`, interna): lo que se valida al guardar es exactamente lo que se ancla al generar. El renderer recibe un diccionario `ParagraphBlock → BlockImage` y deja el ancla en esos párrafos. `IsBlank` ignora toda imagen, válida o no (§4.7).
4. **El orden de los errores** no cambió: `Validate` recorre `Descendants()` y la imagen aparece antes que sus hijos. Por eso `![a <u>b</u>](captura:…)` da `imagen`, no `HTML` (está en T5).
5. **Reutilización del ImagePart (D9).** Se arma un mapa `DocProperties.Name → Blip.Embed` de los dibujos del cuerpo después de B-R7. La parte se reutiliza solo si `ImagePart.ContentType` es igual al tipo detectado. Si no, se crea una parte nueva del tipo detectado con `FeedData(stream)` sobre el mismo `FileStream` validado. T13 lo cubre en los dos casos (PNG reutilizado y `.png` que es JPEG). T15 verifica que con rol `imei_modelo` se reutiliza la parte de la identificación. En los casos sin coincidencia, la nueva parte también entra al mapa: si la misma captura aparece dos veces, no se duplica.
6. **`ReportImageFiles`, defensas extra:**
   - Un enlace simbólico (`FileInfo.LinkTarget != null`) cuenta como `NotInsertable`.
   - El chequeo de primer nivel compara `GetDirectoryName(GetFullPath(...))` con `TrimEndingDirectorySeparator(GetFullPath(caseDir))`.
   - `FileNotFoundException`/`DirectoryNotFoundException` (si el archivo se borra en medio) dan `Missing`.
   - Cualquier otro `IOException`/`UnauthorizedAccessException`, incluido el `EndOfStreamException` de un archivo que se acorta mientras se lee, da `NotAnImage`.
7. **T10 (600 MB) y la memoria.** La medida estable es `GC.GetAllocatedBytesForCurrentThread` (Inspect/TryOpen son sincrónicos), con un tope de < 4 MB. `GC.GetTotalMemory` es de todo el proceso y xUnit corre otras clases en paralelo: con un margen de 4 MB falló una vez. Ahora queda como cota amplia (< 64 MB). El archivo es disperso (`SetLength`) y además se verifica que su SHA-256, su `LastWriteTimeUtc` y su largo no cambien.
8. **T11 "APP1 de 300 KB":** un segmento JPEG tiene como máximo 64 KB (la longitud es de 16 bits), así que el test usa 5 segmentos APP1 de 60 KB (300 KB) más otro de 60 KB antes del SOF.
9. **`GetReportImagePreviewAsync`** devuelve `Result<(Stream Content, string ContentType)>`. El `FileStream` lo cierra `FileStreamResult` al terminar la respuesta. En el controlador se agregaron también `ProducesResponseType(403)`.
10. **`BrokenImageKeys`** consulta cada archivo una sola vez (caché local) aunque se repita en varias secciones.

## Puntos de contacto con la base (§9): diferencias

- `Validate` devuelve `string?` como dice la SDD base. No hubo que adaptar nada.
- El recorte de párrafos vacíos se hace sobre la lista de `Paragraph` ya emitidos. El ancla se reconoce por identidad (`renderer.Anchors`), como prevé §9.
- Las tres condiciones de bloque estaban en `ReportService.GenerateDocxAsync`: se cambiaron ahí. `ValidateForGenerate` sigue con `IsBlank`.
- T4 de la base (`![a](https://x/a.png)` → `imagen`) y T8 de `ReportTextsValidationTests` no cambiaron y siguen en verde.

## Observaciones para el orquestador / usuario

- **Anexo, comportamiento previo (fuera de alcance, no se tocó):** el anexo (`ReplaceWithImages`/`IsUsableImage`) embebe cualquier captura con tamaño > 0, aunque el contenido no sea una imagen. En B13, `screenshot_texto.png` (texto) salió como "Figura 2" en el anexo con una imagen rota. Esta HU no lo empeora (el cuerpo solo acepta capturas disponibles por magic bytes), pero podría valer una HU chica para que el anexo use el mismo `ReportImageFiles.Inspect`.
- **Maquetado:** en la muestra, la imagen del cuerpo sale centrada en el ancho de la caja de texto, con el mismo pPr que el anexo y sin la sangría izquierda de los párrafos de la sección. Es lo que pide §6.5, pero conviene que el usuario lo mire en Word.
- Mis `dotnet build` sobre `server/src/Factum.Backend` escriben en `bin/Debug/net10.0`, que es de donde corre el backend del usuario (PID 96718). El proceso que ya estaba corriendo no se ve afectado. Para ver estos cambios hay que reiniciarlo.
- Pendiente para el usuario: la prueba manual de §12.2 con el cliente (Word sin aviso de reparación, LibreOffice/WPS).
