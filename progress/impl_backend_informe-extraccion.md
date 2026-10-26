# Impl backend — informe-extraccion (HU #109)

Estado: **done**. Implementado el checklist backend de `Refactorizaciones/informe-extraccion.md`
(§5–§8, §10.1, §11) en `server/`. No se tocó `client/` ni `agent-ui/`. Sin commits.

## Archivos tocados

### API (`server/src/Factum.Backend`)
- **Nuevo** `Services/Reports/ExtractionReportValues.cs` — diccionario `{placeholder}→valor` de una línea desde
  `ExtractionDto` + `IReportSettings` + `BrandingSnapshot` (puro). Reusa `ReportDeviceTexts.Placeholders` para las 12
  filas de la ficha #79. Incluye `StatusLabel`, `Equipo`, `Bytes`, `FactumVersion` (informational version del
  ensamblado).
- **Nuevo** `Services/Reports/ExtractionReportTexts.cs` — textos largos (metodología por categoría, sello RFC 3161 en
  3 casos de D5, hash en dispositivo, modificaciones + efectos de adb, interrupción, **alcances fijos D3 §7.2**,
  cadena de custodia §4.3) y filas de la tabla de categorías (puro). Reusa `ReportDeviceTexts.ClockBlock`.
- `Services/Reports/ReportService.cs` —
  - `ExtractionReportResult` (record) + `GenerateExtractionReportAsync` en `IReportService` y `ReportService`.
  - `ExtractionKnownPlaceholders` / `ExtractionBlockKeys`.
  - `GenerateExtractionReportDocxAsync` (espejo de `GenerateDocxAsync`, misma secuencia de pasadas B-R1..B-R9, sin
    tabla de hashes de archivos, sin capturas, sin textos del perito) + `ExtractionHeaderValues` + `FillCategoryTable`
    (clona la fila modelo `{categoriaExtraccion}`/`{metodoExtraccion}`/`{resultadoExtraccion}`).
  - **Refactor mínimo:** `RemoveUnknownPlaceholders`/`IsKnownPlaceholder`/`WarnLeftovers` ahora reciben el set de
    placeholders/bloques que aplica (inspección pasa `KnownPlaceholders`/`BlockKeys`; extracción, los suyos). El
    camino de inspección no cambia (regresión verde, §11.6).
  - El temp del DOCX se borra en `finally`. SHA-256 informativo en el resultado.
- `Services/Extractions/ExtractionService.cs` — inyecta `IReportService reports`; nuevo `GenerateReportAsync(id,
  officer, ct)` (GetAsync + `GenerateExtractionReportAsync`): 404 ajeno/inexistente, 409 `extraction_not_finished` si
  no es terminal.
- `Controllers/ExtractionsController.cs` — `GET {id}/report` (`[Authorize]`, solo el dueño): `File(...)` con
  `Content-Disposition: attachment; filename="informe_extraccion_<folder saneado>.docx"`, header `X-Report-Sha256`;
  errores 404/409 y 500 con `{ error }` sin filtrar detalles. Se inyectó `ILogger`.

### Tatana (`server/src/Factum.Agent`)
- `Models/ExtractionModels.cs` — `ExtractionReportRequest(ContentBase64, Filename)`.
- `Services/Extraction/ExtractionService.cs` — `WriteReport(id, req)` (espejo de `WriteSeals`): exige terminal
  (409 `extraction_not_finished`), decodifica base64 (400 `invalid_request` si inválido/vacío), cap 20 MiB, calcula
  SHA-256 **de los bytes que escribe**, `temp + rename` de `INFORME.docx` + `INFORME.docx.sha256` (`<hash>  INFORME.docx\n`,
  UTF-8 sin BOM) en la **raíz** con `AgentFileNames.SafeChild`, setea `ReportWrittenAt`. **No toca** el MANIFIESTO, su
  `.sha256`, el CIERRE ni `sellos/`.
- `Services/Extraction/ExtractionSnapshot.cs` — campo `ReportWrittenAt` (`DateTimeOffset?`, JSON `report_written_at`,
  `WhenWritingNull`; diarios viejos → null).
- `Controllers/ExtractionController.cs` — `PUT /extractions/{id}/report` con `Guard` (404/409/400/500), responde
  `{ directory, filename, sha256, bytes }`.
- `Controllers/HealthController.cs` — capability `extraction_report_v1`.

### Plantilla / ops
- **Nuevo** `ops/plantilla/build_plantilla_extraccion_v6.py` — script hermano que `import build_plantilla_v6 as v6`,
  lee `plantilla_informe_v6.docx` y **hereda byte a byte** sus partes de diseño (styles, numbering, theme, settings,
  fontTable, header2/footer1/footer2 = salida de `build_header2/build_footer1/build_footer2/build_styles/build_numbering`).
  Solo reescribe `word/document.xml` (las 10 secciones de §6.2, portada "Filete" con `{equipoExtraccion}`/`{fechaExtraccion}`/
  `{estadoExtraccion}`, subtítulo "Informe de extracción lógica de dispositivo móvil", títulos numerados con numId=2,
  bloques `{#actuacionCaso}`/`{#fichaTecnica}`/`{#selloTiempo}`/`{#interrupcion}`/`{#ISOTIPO}`, fila modelo de
  categorías, firma + isotipo) y la celda de la causa de `word/header1.xml` (→ "Informe de extracción lógica ·
  {equipoExtraccion}"). Conserva exactas las partes de corte de sección de la portada (filete y el `<w:sectPr>` →
  header2/footer2). `check()` valida placeholders presentes y sin huérfanos, ficha técnica entre marcadores y fila
  modelo de la tabla de categorías.
- **Nuevo** `server/src/Factum.Backend/Templates/plantilla_informe_extraccion_v6.docx` (generado y versionado).
- `ops/plantilla/README.md` — una fila en la tabla de plantillas.

### Tests
- **Nuevo** `server/tests/Factum.Backend.Tests/Extractions/ExtractionReportDocxTests.cs` (11 tests): sin placeholders
  `{…}` sin reemplazar en ningún part; pie "Realizado con Factum"; sección "Alcances y limitaciones" con el texto D3;
  tabla de categorías con una fila por categoría; sin caso → sin actuación y genera igual; con caso → cita `case_ref`;
  interrumpida → sección con el motivo; sin interrupción → sin sección; `manifest_timestamp == null` → sin bloque de
  sello; `granted` → autoridad + fecha; ficha técnica aparece/desaparece; **validez OpenXML (OpenXmlValidator, 0
  errores)**.
- **Nuevo** `server/tests/Factum.Agent.Tests/Extraction/ExtractionReportWriteTests.cs` (8 tests): escribe INFORME.docx +
  .sha256 con el hash de lo escrito; **no modifica** un MANIFIESTO.tsv/.sha256 preexistente; regenerar sobrescribe y
  recalcula; rechaza base64 inválido/vacío/>20 MiB; 409 si running; 404 si no existe.
- `HealthCapabilityTests.cs` — test de `extraction_report_v1`.
- Doubles: `FakeReportService` (backend) + ajuste del ctor de `ExtractionServiceTests` por la nueva dep `IReportService`.

## Verificación (output)

- `dotnet build Factum.Backend` → **Build succeeded**, 0 errores, 0 warnings CS nuevos (solo NU1902/NU1903 de paquetes,
  preexistentes).
- `dotnet build Factum.Agent` → **Build succeeded**, 0 warnings.
- `python3 ops/plantilla/build_plantilla_extraccion_v6.py` y `--check` → **OK** (genera y valida la plantilla).
- `dotnet test Factum.Backend.Tests --filter ExtractionReportDocxTests` → **11/11 passed** (incluye validación OpenXML).
- `dotnet test Factum.Agent.Tests --filter ExtractionReportWriteTests|HealthCapabilityTests` → **11/11 passed**.
- Regresión informe de inspección: `ReportDesignTests|ReportQaRegressionTests|ReportVersioningTests` → **49/49 passed**.
- Suite completa Backend.Tests → 1502 passed, 15 skipped, **1 failed** =
  `Rfc3161TimestampClientTests.PresupuestoAgotadoAntesDeLaSegunda_Unreachable` (test flaky de timing/red contra host
  inalcanzable; **pasa aislado**, no relacionado con esta HU).
- Suite completa Agent.Tests → **1077/1077 passed**.

## Contrato compartido (coincide con SDD §8, snake_case_lower)

- `GET /api/extractions/{id}/report` → bytes DOCX, `Content-Type` wordprocessingml, `Content-Disposition: attachment;
  filename="informe_extraccion_<folder saneado>.docx"`, `X-Report-Sha256: <64hex>`. Errores `{ error, code }`: 404,
  `409 extraction_not_finished`, 500.
- `PUT /extractions/{id}/report` (Tatana) body `{ "content_base64", "filename" }` → 200 `{ "directory", "filename":
  "INFORME.docx", "sha256", "bytes" }`. Tatana ignora el `filename` del pedido (nombre fijo). Errores `{ error, code }`:
  `404 extraction_not_found`, `409 extraction_not_finished`, `400 invalid_request`, 500.
- `/health.capabilities` incluye `extraction_report_v1`.
- Snapshot de Tatana expone `report_written_at` (WhenWritingNull).
- Sin cambios de modelo Mongo (D9=A).

## Cadena de custodia (SDD §4) — cumplido

El informe es artefacto posterior al cierre: `WriteReport` escribe **solo** `INFORME.docx` + `INFORME.docx.sha256` en
la raíz y no toca el MANIFIESTO, su `.sha256`, el CIERRE ni `sellos/` (test `NoModificaElManifiestoPreexistente`). La
huella del informe la calcula Tatana sobre los bytes escritos; nunca entra al manifiesto. La sección "Integridad y
cadena de custodia" del DOCX imprime el `manifest_sha256`, las entradas, el estado del sello y la frase fija de §4.3.
El backend es stateless (no escribe en Mongo). Regenerar sobrescribe.

## Datos de desarrollo

No se tocaron documentos de negocio de Mongo (la generación es solo lectura del `ExtractionDto`) ni carpetas reales:
cada test usa un `Path.GetTempPath()`/`CreateTempSubdirectory` propio que borra al terminar.

## Pendientes / notas

- **Prueba con dispositivo real (Samsung)**: queda para el usuario/QA (SDD §11 "Prueba manual"): generar desde
  `/extracciones/[id]`, confirmar que el DOCX aparece en la carpeta como `INFORME.docx` + `.sha256`, que el
  `MANIFIESTO.tsv.sha256` no cambió y que el sello sigue verificando. No se puede probar sin un celular/flujo real.
- **Desvío de bajo riesgo respecto de §6.1:** el script hermano **reusa el resultado** de `build_header2/build_footer1/
  build_footer2/build_styles/build_numbering` (heredando esas partes de la v6 ya construida) en vez de invocar esos
  helpers de nuevo, y arma el cuerpo con los patrones de párrafo/tabla de la v6 (mismos estilos y colores centinela) en
  vez de con `title_group`/`data_card` (que transforman párrafos de la v4 por paraId y no sirven para secciones
  nuevas). El diseño "Filete" queda idéntico; el `check()` del script y la validación OpenXML lo confirman.
- Docs (`README.md`/`AGENTE_TATANA.md`): mención opcional y no bloqueante (SDD §10.1); no se agregó para no salir de
  scope. Sí se documentó el script en `ops/plantilla/README.md`.
