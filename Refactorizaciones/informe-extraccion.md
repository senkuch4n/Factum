# SDD: Informe pericial de una extracción lógica (DOCX en la carpeta preservada)

**Slug:** `informe-extraccion` · **Issue:** #109 · **HU:** `docs/hu-informe-extraccion.md` (D1–D9 validadas
el 2026-10-25; **D8 = B**) · **Autor:** architect · **Fecha:** 2026-10-25 · **Rama:** `feat/informe-extraccion`
(desde `develop`, con #100 mergeada)

El código se cita por **símbolo**. Los números de línea son una pista y pueden haber cambiado. La arqueología se
hizo sobre `feat/informe-extraccion` (= `develop` con #100). Verificá en el archivo real antes de afirmar algo.

---

## 1. Resumen funcional

El perito genera, con un botón en el detalle de una extracción terminal (`/extracciones/[id]`), un **informe
pericial de la extracción** en DOCX, con la misma identidad y el diseño "Filete" del informe de inspección
(membrete de la marca del dueño, colores, pie "Realizado con Factum"). El backend arma el DOCX **solo con el
`ExtractionDto`** (no necesita contenido ni carpeta), lo devuelve como bytes, y el navegador se lo entrega a
**Tatana**, que lo escribe en la **carpeta preservada de la extracción** (`INFORME.docx` + `INFORME.docx.sha256`),
junto al MANIFIESTO y el ACTA (**D8 = B**). El informe también se puede descargar en el navegador. El informe
**remite** al hash del manifiesto sellado sin tocarlo: es un artefacto posterior al cierre, fuera del manifiesto
(como `sellos/` y las adendas). Funciona con o sin caso asociado y en cualquier estado terminal (completa, con
errores, interrumpida, cancelada).

## 2. Toca

| Parte | ¿Toca? | Qué |
|---|---|---|
| **backend (API)** `server/src/Factum.Backend` | **sí** | `Services/Reports/ReportService.cs` + `IReportService` (método nuevo `GenerateExtractionReportAsync`, reusa el motor DOCX), `Services/Reports/ExtractionReportValues.cs` y `ExtractionReportTexts.cs` (nuevos), nueva plantilla `Templates/plantilla_informe_extraccion_v6.docx`, `Controllers/ExtractionsController.cs` (`GET {id}/report`), `Services/Extractions/IExtractionService`+`ExtractionService.cs` (método `GenerateReportAsync`). **Sin cambios de modelo Mongo** (D9=A). |
| **backend (Tatana)** `server/src/Factum.Agent` | **sí (D8=B)** | `Controllers/ExtractionController.cs` (`PUT /extractions/{id}/report`), `Models/ExtractionModels.cs` (`ExtractionReportRequest`), `Services/Extraction/ExtractionService.cs` (`WriteReport`, espejo de `WriteSeals`), `Services/Extraction/ExtractionSnapshot.cs` + `ExtractionJournal.cs` (campo `ReportWrittenAt`), `Controllers/HealthController.cs` (capability `extraction_report_v1`). |
| **client** `client/` | **sí** | `lib/api.ts` (`getExtractionReportBlob`), `lib/agent.ts` (`writeExtractionReport`, `supportsExtractionReport`), `components/extraction/ExtractionResult.tsx` (botón "Generar informe") y `ExtractionDetailScreen.tsx` (orquestación), `lib/extraction.ts` si hace falta algún texto. |
| **agent-ui** `agent-ui/` | **no** | Sin cambios. |
| **plantilla / ops** | **sí** | `ops/plantilla/build_plantilla_extraccion_v6.py` (nuevo, hermano de `build_plantilla_v6.py`), `ops/plantilla/README.md` (una línea). |
| **tests** | **sí** | `server/tests/Factum.Backend.Tests` (generación del informe) y `server/tests/Factum.Agent.Tests` (`WriteReport`). |
| **docs** | opcional | `README.md` / `AGENTE_TATANA.md`: una mención del informe de extracción. No bloqueante. |

**Reparto.** `implementer-backend` hace §5–§8, §10, §11 (API, Tatana, plantilla, tests .NET). `implementer-frontend`
hace §9 (solo `client/`). El frontend puede arrancar con el contrato de §8 y Tatana en mock; no necesita el backend
real para maquetar el botón, pero sí para la prueba end-to-end.

---

## 3. Arqueología verificada

| Qué | Dónde | Cómo se usa acá |
|---|---|---|
| Motor DOCX del informe | `ReportService.GenerateDocxAsync` (privado): `File.Copy` de la plantilla, `RemoveUnknownPlaceholders`→`ResolveBlocks`→`BrandColors.Apply`→imágenes de marca→`ReplaceParagraphPerLine`→tabla por clonado→`RewriteParagraph` por run→`AddFactumAttributionFooter`→`WarnLeftovers`→`SanitizeAllParts` | El método nuevo `GenerateExtractionReportDocxAsync` sigue la misma secuencia, en la **misma clase** (todos los helpers privados en scope), con la plantilla de extracción, su set de placeholders/bloques y los valores de §7. |
| Marca del dueño | `IReportBrandingResolver.ResolveAsync(ownerDni, ct)` → `BrandingSnapshot` (`OrganizationName`, `Logo`, `Isotype`, `ContactLines`, `PrimaryColor`, `AccentColor`) | Se resuelve por `dto.Officer.Dni` (D7), igual que el informe de inspección resuelve por el dueño del caso. Si no hay marca, defaults de Factum. |
| Pie obligatorio | `ReportService.AddFactumAttributionFooter` (inyectado por código, ninguna plantilla lo saca) | Igual, en todas las páginas del informe de extracción. |
| Tabla por clonado | `ReportService.BuildHashRow` / `FillHashTable` (clona una fila modelo con `{nombreArchivo}`/`{hashArchivo}`, borra ids `w14:paraId`) | Patrón reusado por `FillCategoryTable` (fila modelo `{categoriaExtraccion}`/`{metodoExtraccion}`/`{resultadoExtraccion}`). |
| Bloques condicionales | `ReportService.ResolveBlocks` + `BlockKeys` | El informe de extracción define `ExtractionBlockKeys` (p. ej. `actuacionCaso`, `selloTiempo`, `fichaTecnica`, `interrupcion`, `modificaciones`, `MEMBRETE*`, `ISOTIPO`). Hay que **parametrizar** `RemoveUnknownPlaceholders`/`IsKnownPlaceholder` para que acepten el set del informe que corresponda (hoy usan el estático `KnownPlaceholders`). |
| Zona horaria | `IReportSettings.Zone` / `ReportSettings` (`Report:TimeZone`, fallback UTC-03:00) | Los tiempos del DTO son UTC; se muestran en hora local con zona. Mismo `settings.Zone` del informe de inspección. |
| Ficha #79 en texto | `ReportDeviceTexts` (`SistemaOperativo`, `NumeroSerie`, `Kernel`, …, `Placeholders(DeviceDetails, zone)`, relojes `DeviceSkewPast`) | Reusado para la sección **Dispositivo**: el `ExtractionDto.device_details` es el **mismo** `DeviceDetails` de #79, pero con `clock_start` **y** `clock_close`. |
| Sello / timestamp | `ExtractionDto.manifest_timestamp` (`TimestampDto`): `status` (`granted`/`pending`/`failed`), `authority`, `gen_time_display` (ya formateado en `Report:TimeZone`), `granted_at` | Sección **Integridad** (D5): sellado/pendiente/sin sellar. `manifest_timestamp == null` ⇒ sellado desactivado o sin hash ⇒ "sin sellado". |
| DTO completo | `ExtractionDto.From` (`DTOs/ExtractionDtos.cs`) | Confirma que el DTO ya trae **todo** lo del informe (ver §7.3). |
| Descarga de archivo (web) | `api.downloadURL(caseId, file)` (link directo servido por el backend) — **no sirve acá** porque el DOCX no se persiste en el server | Acá el cliente hace un `fetch` autenticado y recibe el **blob** del DOCX; lo usa para descargar y para mandárselo a Tatana. |
| Escribir en la carpeta | `ExtractionService.WriteSeals` (Tatana): busca el diario por `id`, decodifica base64, valida nombre/tamaño, `temp + rename`, marca `SealsWritten` | `WriteReport` lo copia: escribe `INFORME.docx` + `INFORME.docx.sha256` en la **raíz** de la carpeta (no en `sellos/`), calcula el SHA-256 de los bytes recibidos, marca `ReportWrittenAt`. |
| Capability | `HealthController.Capabilities` (`case_seals_v1`, `extraction_android_v1`, …) y `agent.hasCapability` | Nueva `extraction_report_v1`: sin ella, el cliente cae a descarga-solo. |
| Nombres seguros | `AgentFileNames.SafeChild(dir, name)` | Defensa anti-traversal al escribir `INFORME.docx`/`INFORME.docx.sha256`. |

---

## 4. Cadena de custodia (crítico — resuelve el punto 2 del pedido)

El informe se genera **después** de cerrada y sellada la extracción. El `MANIFIESTO.tsv` y su `MANIFIESTO.tsv.sha256`
ya están cerrados, y el hash del manifiesto puede estar sellado con RFC 3161 (`sellos/MANIFIESTO.tsv.tsr`). **Agregar
el DOCX a la carpeta no debe invalidar ni reescribir el manifiesto ni su sello.** Reglas:

1. **El informe es un artefacto posterior al cierre, fuera del conjunto cubierto por el manifiesto.** En la estructura
   de la carpeta (#100 §9.1) ya hay precedentes de artefactos *fuera* del manifiesto: `sellos/` (los copia la web
   después) y `ADENDA_ACTA_<fecha>.txt`. `INFORME.docx` se suma a esa categoría. **El `MANIFIESTO.tsv`, su `.sha256`,
   el `SHA256SUMS.txt` y el `CIERRE.txt` no se tocan, no se reordenan y no se re-sellan.**
2. **El informe tiene su propia huella.** Tatana escribe `INFORME.docx.sha256` junto al DOCX, con el formato
   `sha256sum`: `<64hex>  INFORME.docx`. El hash lo calcula **Tatana sobre los bytes que efectivamente escribió**
   (no el backend), así la huella siempre corresponde al archivo en disco, sea cual sea el transporte.
3. **El informe remite al hash sellado, no lo altera.** La sección "Integridad y cadena de custodia" del DOCX imprime
   el `manifest_sha256`, la cantidad de entradas y el estado del sello (TSA + fecha cierta / pendiente / sin sellar),
   y una frase fija: *"Este informe es un documento de presentación generado después del cierre de la extracción. La
   autoridad de integridad es el `MANIFIESTO.tsv` y su sello de tiempo; este informe remite a ellos y no los
   modifica. La huella SHA-256 de este propio informe figura en `INFORME.docx.sha256`."*
4. **Sin re-registro en el backend, sin hito (D9=A).** La extracción es inmutable; generar el informe **no** escribe
   en Mongo. No hay `report_generated` ni evento de caso.
5. **Idempotencia / regeneración (resuelve el punto 4).** Regenerar **sobrescribe** `INFORME.docx` y
   `INFORME.docx.sha256` (`temp + rename`, como los sellos). Como la extracción terminal es inmutable, el contenido
   del informe es el mismo; el DOCX puede diferir byte a byte entre corridas (ids internos de OpenXML), por eso la
   huella se recalcula en cada escritura y nunca entra al manifiesto. No hay versión ni historial del informe.

---

## 5. Modelo de datos

**Backend (Mongo): sin cambios.** No se agrega `report_sha256` ni `report_generated_at` a `Extraction` (D9=A, y con
D8=B la huella la custodia Tatana en `INFORME.docx.sha256`). La generación es **stateless** en el backend: lee el
`ExtractionDto` que ya existe y devuelve bytes.

**Tatana (diario local, `ExtractionJournal`/`ExtractionSnapshot`): un campo nuevo.**

- `ReportWrittenAt` (`DateTimeOffset?`, JSON `report_written_at`, `WhenWritingNull`): lo setea `WriteReport` tras
  escribir el DOCX. No es autoridad de nada; solo permite que el detalle muestre "Informe guardado en la carpeta".
  Append-only de facto (se pisa en cada regeneración). Tolerar diarios viejos sin el campo (default `null`).

---

## 6. Plantilla `plantilla_informe_extraccion_v6.docx` (D2 = B)

### 6.1 Cómo se construye

Script hermano `ops/plantilla/build_plantilla_extraccion_v6.py` que **importa** `build_plantilla_v6` como módulo
(igual que `build_plantilla_v6.py` importa `build_plantilla_v4 as v4`) y reusa sus helpers de bajo nivel: `para`,
`marker`, `tbl`, `tc`, `cover`, `build_header1/2`, `build_footer1/2` (membrete + atribución), `build_styles`,
`build_numbering`, `hash_table`, `title_group`, `data_card`, y el `check(...)` final. Hereda el diseño "Filete"
(portada, encabezado interior, pie con slot de atribución, colores de marca por centinela) **sin** tocar
`build_plantilla_v6.py` ni `plantilla_informe_v6.docx`. El resultado se commitea en `Templates/`.

Diferencias respecto de la v6 de inspección:

- **Subtítulo de portada:** `"Informe de extracción lógica de dispositivo móvil"` (constante nueva, no
  `COVER_SUBTITLE = "Inspección técnica de dispositivo móvil"`).
- **Carátula de portada:** en vez de la carátula del caso, va `{equipoExtraccion}` (marca + modelo) + `{fechaExtraccion}`
  + `{estadoExtraccion}`.
- **Membrete, encabezado, pie, isotipo del cierre, bloque de ficha técnica:** idénticos a la v6 (mismos
  marcadores `{#MEMBRETE}`, `{#MEMBRETE_CON_LOGO}`, `{#fichaTecnica}`, `{#ISOTIPO}`, `{LOGO_ORGANIZACION:4.5x2.5}`,
  `{CONTACTO}`, `{ORGANIZACION}`, slot de atribución).
- **Cuerpo:** las 10 secciones de §6.2 en vez de las de inspección. **Sin** anexo de capturas, **sin** tabla de hashes
  de evidencia, **sin** textos del perito.

El `check()` del script valida (como la v6) que cada placeholder esperado esté presente, en su propio run, y que no
quede ninguno huérfano; que la ficha técnica tenga sus filas entre `{#fichaTecnica}`/`{/fichaTecnica}`; y que la tabla
de categorías tenga su fila modelo.

### 6.2 Secciones y placeholders (D2)

> Nomenclatura de placeholders: `{camelCase}` de texto (reemplazo por run), `{#clave}…{/clave}` de bloque, fila modelo
> de tabla por clonado. Todos van en el `ExtractionKnownPlaceholders`/`ExtractionBlockKeys` del servicio (§7.1).

| # | Sección | Placeholders de texto | Bloque | Tabla |
|---|---|---|---|---|
| Portada | Marca + título | `{equipoExtraccion}`, `{fechaExtraccion}`, `{estadoExtraccion}`, `{ORGANIZACION}`, `{CONTACTO}` + `{LOGO_ORGANIZACION:4.5x2.5}` | `{#MEMBRETE}`, `{#MEMBRETE_CON_LOGO}`, `{#MEMBRETE_SIN_LOGO}` | — |
| 1 | Identificación | `{peritoExtraccion}`, `{dniPerito}`, `{pcExtraccion}` (hostname), `{usuarioSoExtraccion}`, `{versionTatana}` (`agent_version` + `agent_mode`), `{versionFactum}`, `{actuacionCaso}` | `{#actuacionCaso}…{/actuacionCaso}` (solo si hay caso) | — |
| 2 | Tiempos | `{inicioExtraccion}`, `{finExtraccion}` (hora local con zona) | — | — |
| 3 | Dispositivo | las 12 filas de la ficha #79 (`{fichaSistemaOperativo}`, `{fichaNumeroSerie}`, `{fichaKernel}`, …, reusando `ReportDeviceTexts.Placeholders`) + `{descripcionRelojExtraccion}` (relojes inicio/cierre) | `{#fichaTecnica}…{/fichaTecnica}` (desaparece sin ficha) | — |
| 4 | Metodología y herramientas | `{descripcionMetodologia}` (texto por categoría, derivado de `category.method`, + la app auxiliar firmada y "consultas de solo lectura") | — | — |
| 5 | Inventario | `{totalesExtraccion}` (archivos, bytes, errores, discordancias) | — | fila modelo `{categoriaExtraccion}` / `{metodoExtraccion}` / `{resultadoExtraccion}` (una por categoría) |
| 6 | Integridad y cadena de custodia | `{hashManifiesto}`, `{entradasManifiesto}`, `{descripcionSelloExtraccion}`, `{hashDispositivoExtraccion}`, `{ubicacionCarpeta}`, `{descripcionCadenaCustodia}` (la frase fija de §4.3) | `{#selloTiempo}…{/selloTiempo}` (desaparece si `manifest_timestamp == null`) | — |
| 7 | Modificaciones al dispositivo | `{descripcionModificaciones}` (lista con hora o "Ninguna: solo lectura" + efectos colaterales de adb) | `{#modificaciones}` opcional | — |
| 8 | Interrupción / cancelación | `{descripcionInterrupcion}` | `{#interrupcion}…{/interrupcion}` (solo `interrupted`/`cancelled`) | — |
| 9 | Alcances y limitaciones | `{descripcionAlcances}` (texto fijo D3, §7.2) | — | — |
| 10 | Firma | `{peritoExtraccion}`, `{dniPerito}` (como la firma de la v6) + `{#ISOTIPO}` al cierre | `{#ISOTIPO}…{/ISOTIPO}` | — |

---

## 7. Backend: generación del DOCX

### 7.1 Servicio

`IReportService` suma:

```csharp
// Arma el DOCX del informe de extracción a partir del DTO (no necesita contenido ni carpeta).
// Resuelve la marca por ext.Officer.Dni (D7). Devuelve bytes + nombre sugerido + SHA-256.
Task<ExtractionReportResult> GenerateExtractionReportAsync(ExtractionDto ext, CancellationToken ct = default);

public sealed record ExtractionReportResult(byte[] Docx, string Filename, string Sha256);
```

Implementación en `ReportService.cs` (misma clase → todos los helpers privados en scope):

1. `brand = await brandings.ResolveAsync(ext.Officer.Dni, ct)` (si Mongo falla, falla la generación; nunca deja
   artefacto a medias).
2. Copia `Templates/plantilla_informe_extraccion_v6.docx` a un archivo temporal único
   (`Path.Combine(Path.GetTempPath(), "factum-informe-extraccion", Guid)`).
3. `GenerateExtractionReportDocxAsync` (espejo de `GenerateDocxAsync`): abre el DOCX, arma `conditions`
   (membrete/isotipo según `brand`; `actuacionCaso = ext.CaseRef is not null`; `fichaTecnica = ext.DeviceDetails is
   not null`; `selloTiempo = ext.ManifestTimestamp is not null`; `interrupcion = ext.Interruption is not null ||
   status==cancelled`; `modificaciones = ...`), llama en orden a `RemoveUnknownPlaceholders(root,
   ExtractionKnownPlaceholders)` → `ResolveBlocks` → `BrandColors.Apply` → `ReplaceImagePlaceholder` (logo/isotipo) →
   `ReplaceParagraphPerLine` para los textos largos (metodología, sello, cadena de custodia, modificaciones,
   interrupción, alcances, reloj) → `FillCategoryTable` → `RewriteParagraph` por run para los placeholders de una
   línea → `AddFactumAttributionFooter` → `WarnLeftovers` → `SanitizeAllParts` → `Save`.
4. Lee los bytes del temp, calcula el SHA-256 (informativo; la huella de disco la recalcula Tatana), **borra el
   temp** (en `finally`), devuelve el resultado.
5. Nombre sugerido (para la descarga y el `Content-Disposition`): `informe_extraccion_<Sanitize(folder_name)>.docx`,
   con el mismo `Sanitize` de `ReportService` (letras/dígitos → `_`, tope 60).

**Refactor mínimo necesario:** hoy `RemoveUnknownPlaceholders`/`IsKnownPlaceholder` usan el estático
`KnownPlaceholders` del informe de inspección. Parametrizarlos para recibir el set que aplica
(`KnownPlaceholders`/`BlockKeys` para inspección; `ExtractionKnownPlaceholders`/`ExtractionBlockKeys` para extracción).
No cambia el comportamiento del informe de inspección (regresión en §11).

`ExtractionReportValues` (nuevo, puro) arma el diccionario `{placeholder → valor}` de los textos de una línea desde
el `ExtractionDto` + `IReportSettings` + `BrandingSnapshot`. `ExtractionReportTexts` (nuevo, puro) arma los textos
largos (metodología, integridad, modificaciones, interrupción, alcances, filas de categoría). Reusan
`ReportDeviceTexts` (ficha + relojes) y leen el sello de `ext.ManifestTimestamp`.

**Versión de Factum** (`{versionFactum}`): usar `Assembly.GetExecutingAssembly()` informational version del backend
(o la constante de versión ya usada en el proyecto, si existe); si no hay, dejar vacío. `{versionTatana}` sale de
`ext.AgentVersion` + `ext.AgentMode`. Decisión menor, no bloqueante.

### 7.2 Texto fijo de "Alcances y limitaciones" (D3 — confirmado por el usuario)

Va como constante en `ExtractionReportTexts` (no depende de los datos; es del método). Texto confirmado en la HU
(`docs/hu-informe-extraccion.md`, D3=B):

- **Cubre:** datos estructurados accesibles por adb de solo lectura (contactos, SMS/MMS, calendario, apps
  instaladas), registro de llamadas vía la app auxiliar firmada de Factum, y los archivos del almacenamiento
  compartido (`/sdcard`) seleccionados, preservados con SHA-256 y manifiesto.
- **No cubre:** contenido privado de aplicaciones (`/data/data`) ni el almacenamiento aislado de otras apps
  (Android 11+ no lo expone a adb); contenido cifrado de apps de mensajería (p. ej. mensajes de WhatsApp) sin la
  clave o sin un método avanzado; extracción física, por root, exploits o bootloader; datos borrados o no
  referenciados por el sistema de archivos. El registro de llamadas requiere instalar temporalmente la app auxiliar;
  si el fabricante bloquea la instalación por USB, esa categoría queda "No extraída".
- Cierra aclarando que **es el alcance del método, no un pendiente de desarrollo**.

El texto no debe asumir Android para #102: las limitaciones específicas de adb/Android se enmarcan en "en una
extracción lógica de Android". El informe usa `ext.Device.Platform` donde haga falta distinguir.

### 7.3 De dónde sale cada sección (campos del `ExtractionDto`)

| Sección | Campos del DTO |
|---|---|
| Portada | `device.manufacturer`+`device.model` → `{equipoExtraccion}`; `started_at_pc` → `{fechaExtraccion}`; `status` → `{estadoExtraccion}` |
| 1 Identificación | `officer.name`/`officer.dni`; `hostname`; `os_user`; `agent_version`+`agent_mode`; `case_ref` (bloque `actuacionCaso`) |
| 2 Tiempos | `started_at_pc`, `finished_at_pc` (UTC → `settings.Zone`) |
| 3 Dispositivo | `device_details.fields` (ficha #79) + `device_details.clock_start`/`clock_close` |
| 4 Metodología | `categories[].method` (+ texto fijo del mecanismo por `kind`) |
| 5 Inventario | `categories[]` (`key`, `method`, `status`, `count`/`files`/`bytes`/`reason_code`) + `totals` |
| 6 Integridad | `manifest_sha256`, `manifest_entries`, `manifest_timestamp`, `verify_on_device`, `device_sha256_available`, `totals.hash_mismatches`, `destination_path`+`folder_name` |
| 7 Modificaciones | `device_modifications[]` (`kind`, `at`, `detail`) |
| 8 Interrupción | `interruption` (`reason_code`, `message`, `at`) o `status == cancelled` |
| 9 Alcances | — (texto fijo) |

**El informe no inventa contenido:** solo estos campos (cantidades, métodos, hashes, metadatos). Nunca un nombre de
contacto, número, mensaje ni nombre de archivo (el backend no los tiene).

**Sin caso (punto 3):** si `case_id`/`case_ref` son `null`, el bloque `{#actuacionCaso}` desaparece; la portada y la
identificación usan equipo + perito + marca del dueño, sin causa ni carátula. El informe se genera igual.

### 7.4 Endpoint

`GET /api/extractions/{id}/report` en `ExtractionsController` (`[Authorize]`, solo el dueño):

1. `service.GetAsync(id, Officer)` → 404 si no existe o es de otro dueño (igual que el resto).
2. Si `status` no es terminal → `409 extraction_not_finished` (`{ error, code }`). Estados terminales permitidos:
   `completed`, `completed_with_errors`, `interrupted`, `cancelled`.
3. `report = await reportService.GenerateExtractionReportAsync(dto, ct)`.
4. Devuelve `File(report.Docx,
   "application/vnd.openxmlformats-officedocument.wordprocessingml.document", report.Filename)` → el framework pone
   `Content-Disposition: attachment; filename="informe_extraccion_<...>.docx"`. Header extra
   `X-Report-Sha256: <report.Sha256>` (informativo; no es la autoridad). Errores de generación → `500` con
   `{ error }` (sin filtrar detalles).

En `IExtractionService`/`ExtractionService` (backend) un método `GenerateReportAsync(id, officer, ct)` que compone
`GetAsync` + `reportService.GenerateExtractionReportAsync` y devuelve el `Result<ExtractionReportResult>` (patrón
`Result` existente), para que el controller mantenga su forma. Inyectar `IReportService` en el `ExtractionService`
del backend (ya es `AddScoped`).

---

## 8. Contrato compartido

**Casing:** todo JSON en `snake_case_lower` (`Program.cs` de cada proceso). Fechas ISO-8601 UTC con `Z`.

Archivos de cada lado:

| Contrato | Lado servidor | Lado web |
|---|---|---|
| Web ↔ API (descarga del DOCX) | `server/src/Factum.Backend/Controllers/ExtractionsController.cs` | `client/src/lib/api.ts` (`getExtractionReportBlob`) |
| Web ↔ Tatana (escribir en la carpeta) | `server/src/Factum.Agent/Controllers/ExtractionController.cs`, `Models/ExtractionModels.cs` (`ExtractionReportRequest`) | `client/src/lib/agent.ts` (`writeExtractionReport`, `supportsExtractionReport`) |

### 8.1 Capability (Tatana → web)

`/health.capabilities` suma `"extraction_report_v1"` (`HealthController.Capabilities`). Sin ella, el cliente **no**
intenta escribir en la carpeta: cae a descarga-solo con un aviso (§9).

### 8.2 Web → API: `GET /api/extractions/{id}/report`

- Request: `GET`, con `Authorization: Bearer …` (mismo que el resto de la API).
- Respuesta 200: cuerpo = **bytes del DOCX**; `Content-Type:
  application/vnd.openxmlformats-officedocument.wordprocessingml.document`; `Content-Disposition: attachment;
  filename="informe_extraccion_<folder_name saneado>.docx"`; `X-Report-Sha256: <64hex>`.
- Errores (`{ "error", "code" }`): `404` (no existe / otro dueño), `409 extraction_not_finished`, `500`.

Cliente (`api.getExtractionReportBlob(id)`): hace el `fetch` autenticado, devuelve `{ blob: Blob, filename: string,
sha256: string | null }` (parsea `Content-Disposition` y `X-Report-Sha256`).

### 8.3 Web → Tatana: `PUT /extractions/{id}/report`

Body (`ExtractionReportRequest`):

```json
{ "content_base64": "<DOCX en base64>", "filename": "INFORME.docx" }
```

Respuesta 200:

```json
{ "directory": "<carpeta de la extracción>", "filename": "INFORME.docx", "sha256": "<64hex de lo escrito>", "bytes": 123456 }
```

- El nombre dentro de la carpeta es **fijo** `INFORME.docx` (ver Duda U1): Tatana ignora/valida el `filename` del
  pedido y escribe siempre `INFORME.docx` + `INFORME.docx.sha256` en la **raíz** de la carpeta (no en `sellos/`),
  con `AgentFileNames.SafeChild`.
- `WriteReport` (espejo de `WriteSeals`): busca el diario por `id` (`404 extraction_not_found` si no está); exige
  estado terminal (`409 extraction_not_finished` si `running`); decodifica base64 (`400 invalid_request` si no es
  base64 o es vacío); cap de tamaño (recomendado **≤ 20 MiB**, `400 invalid_request` si lo supera); calcula el
  SHA-256 de los bytes; `temp + rename` de `INFORME.docx`; escribe `INFORME.docx.sha256` con `<hash>  INFORME.docx`
  (`\n`); setea `doc.Snapshot.ReportWrittenAt = DateTimeOffset.UtcNow` y guarda el diario.
- **No toca** `MANIFIESTO.tsv*`, `CIERRE.txt` ni `sellos/`.
- Errores (`{ error, code }`): `404 extraction_not_found`, `409 extraction_not_finished`, `400 invalid_request`,
  `500`.

Cliente (`agent.writeExtractionReport(id, { contentBase64, filename })`): `PUT /extractions/{id}/report`.
`agent.supportsExtractionReport()` = `hasCapability("extraction_report_v1")`.

---

## 9. Web (`client/`)

> Leer `client/AGENTS.md` y `node_modules/next/dist/docs/` antes de tocar las rutas (Next.js 16). Los skills de
> UX/UI obligatorios del arné aplican aunque el cambio sea chico (un botón y una descarga). El cambio es solo en
> `client/`; `agent-ui/` no se toca.

### 9.1 Botón y acción

En `components/extraction/ExtractionResult.tsx` (que ya recibe `snapshot`, `dto`, `canReveal`, `onReveal`, `onOpenLink`,
`onUnlink`), sumar un botón **"Generar informe"** (secundario, ícono de documento, p. ej. `FileText` de lucide), junto
a "Abrir carpeta". Props nuevas: `onGenerateReport: () => void`, `generatingReport: boolean`,
`reportWritten: boolean`. El estado se maneja en `ExtractionDetailScreen.tsx`.

- **Habilitación:** solo en estados terminales. Como `ExtractionResult` ya se monta en el resultado (el detalle
  muestra `ExtractionProgress` si `running`), el botón está disponible cuando se ve el resultado. Si por algún camino
  se ve con `running`, el botón va deshabilitado con "Disponible al terminar la extracción".
- **Acción (`generateReport` en `ExtractionDetailScreen`):**
  1. `setGeneratingReport(true)`; botón "Generando…".
  2. `const { blob, filename } = await api.getExtractionReportBlob(id)`.
  3. Si Tatana tiene la extracción local (`snapshot` presente) y `await agent.supportsExtractionReport()`:
     base64-encodear el blob y `await agent.writeExtractionReport(id, { contentBase64, filename: "INFORME.docx" })`
     → aviso de éxito "Informe guardado en la carpeta de la extracción"; refrescar el snapshot para tomar
     `report_written_at`.
  4. **Siempre** (y única vía si Tatana no la tiene — `onlyApi` — o no soporta la capability): disparar la descarga
     del blob en el navegador (link `URL.createObjectURL(blob)` con `download = filename`, revocar después). En el
     caso sin Tatana, el aviso aclara "No se pudo guardar en la carpeta (la extracción es de otra PC o Tatana es
     viejo); se descargó el informe".
  5. `finally setGeneratingReport(false)`.
- **Error** (backend no responde / 409 / 500 / Tatana falla al escribir): `FxBanner tone="warn"` discreto ("No se
  pudo generar el informe · Reintentar"), sin perder el estado de la página. Si el DOCX ya se descargó pero falló la
  escritura en la carpeta, el aviso lo distingue.
- Si `dto.report_written_at`/`snapshot.report_written_at` existe (ya se generó), mostrar una nota discreta "Informe
  en la carpeta" junto al botón (que sigue permitiendo regenerar).

### 9.2 Tipos y lib

- `lib/api.ts`: `getExtractionReportBlob(id): Promise<{ blob: Blob; filename: string; sha256: string | null }>`.
- `lib/agent.ts`: `writeExtractionReport(id, body: { contentBase64: string; filename: string })`,
  `supportsExtractionReport(): Promise<boolean>`; sumar `extraction_report_v1` al listado documentado de
  capabilities y, si el snapshot lo expone, `report_written_at?: string` en `AgentExtractionSnapshot` (y en
  `Extraction` de `lib/api.ts` solo si el backend lo devolviera — **no** lo hace, D9=A, así que queda solo en el
  snapshot de Tatana).

### 9.3 Sin cambios visuales de branding

El informe sale con la marca del dueño resuelta en el backend. No hay pantalla de configuración nueva.

---

## 10. Checklist atómico

### 10.1 `implementer-backend` (API + Tatana + plantilla + tests)

Plantilla:
- [ ] `ops/plantilla/build_plantilla_extraccion_v6.py`: importar `build_plantilla_v6` como módulo, reusar sus helpers,
      armar las 10 secciones de §6.2 con sus placeholders/bloques, subtítulo "Informe de extracción lógica de
      dispositivo móvil", portada con `{equipoExtraccion}`/`{fechaExtraccion}`/`{estadoExtraccion}`.
- [ ] `check()` del script: valida placeholders presentes y sin huérfanos, ficha técnica, fila modelo de la tabla de
      categorías.
- [ ] Generar y commitear `server/src/Factum.Backend/Templates/plantilla_informe_extraccion_v6.docx`.
- [ ] `ops/plantilla/README.md`: una línea sobre el script hermano.

API:
- [ ] `ExtractionReportValues.cs` (puro): diccionario placeholder→valor desde `ExtractionDto` + `IReportSettings` +
      `BrandingSnapshot`.
- [ ] `ExtractionReportTexts.cs` (puro): textos largos (metodología por categoría, integridad + frase de cadena de
      custodia §4.3, modificaciones + efectos de adb, interrupción, **alcances fijos D3 §7.2**, filas de categoría);
      reusa `ReportDeviceTexts` y lee `ManifestTimestamp`.
- [ ] `ReportService`: parametrizar `RemoveUnknownPlaceholders`/`IsKnownPlaceholder` con el set de placeholders/bloques
      (sin cambiar el camino de inspección).
- [ ] `ReportService`: `ExtractionKnownPlaceholders`/`ExtractionBlockKeys`, `GenerateExtractionReportDocxAsync`
      (espejo de `GenerateDocxAsync`), `FillCategoryTable` (clonado de fila modelo), método público
      `GenerateExtractionReportAsync` + `ExtractionReportResult`; borrado del temp en `finally`.
- [ ] `IExtractionService`/`ExtractionService` (backend): `GenerateReportAsync(id, officer, ct)` (GetAsync +
      ReportService); inyectar `IReportService`.
- [ ] `ExtractionsController`: `GET {id}/report` (§7.4), con `Content-Disposition` y `X-Report-Sha256`; 404/409/500.

Tatana:
- [ ] `Models/ExtractionModels.cs`: `ExtractionReportRequest(string? ContentBase64, string? Filename)`.
- [ ] `ExtractionService.WriteReport(id, req)`: espejo de `WriteSeals`, escribe `INFORME.docx` +
      `INFORME.docx.sha256` en la raíz (nombre fijo, `SafeChild`, cap 20 MiB, terminal, SHA-256 de lo escrito),
      setea `ReportWrittenAt`; **no toca** manifiesto/sellos/cierre.
- [ ] `ExtractionController`: `PUT /extractions/{id}/report` (§8.3), `Guard` con los códigos de error.
- [ ] `ExtractionSnapshot`/`ExtractionJournal`: campo `ReportWrittenAt` (`report_written_at`, `WhenWritingNull`),
      tolerar diarios sin él.
- [ ] `HealthController.Capabilities`: `extraction_report_v1` (+ comentario).

Docs (opcional): mención en `README.md`/`AGENTE_TATANA.md`.

### 10.2 `implementer-frontend` (solo `client/`)

- [ ] `lib/api.ts`: `getExtractionReportBlob(id)` (fetch autenticado, parsea `Content-Disposition` + `X-Report-Sha256`).
- [ ] `lib/agent.ts`: `writeExtractionReport(id, body)`, `supportsExtractionReport()`; `report_written_at?` en
      `AgentExtractionSnapshot`; documentar `extraction_report_v1`.
- [ ] `components/extraction/ExtractionResult.tsx`: botón "Generar informe" (secundario, ícono), props
      `onGenerateReport`/`generatingReport`/`reportWritten`, deshabilitado si `running`, nota "Informe en la carpeta"
      si ya se generó.
- [ ] `components/extraction/ExtractionDetailScreen.tsx`: handler `generateReport` (§9.1): descarga el blob, lo
      escribe en Tatana si hay capability y snapshot local, descarga en el navegador, avisos de éxito/fallo, refresca
      el snapshot.
- [ ] Skills de UX/UI (`ui-ux-pro-max`, `senior-frontend`, `3d-web-experience`, `web-design-guidelines`): invocarlos
      y dejar constancia en el progress (aunque el cambio sea chico).

---

## 11. Verificación

**No se tocan datos de la base de desarrollo** (regla dura de `AGENTS.md`): la generación es solo lectura del
`ExtractionDto`; Tatana escribe solo `INFORME.docx`/`INFORME.docx.sha256` en la carpeta de la extracción que el
usuario eligió, nunca documentos de negocio. Los tests limpian solo lo que ellos insertan y no escriben en
`Agent:EvidenceDirectory` real.

Comandos antes de declarar `done`:

- Backend: `dotnet build server/src/Factum.Backend/Factum.Backend.csproj`.
- Tatana: `dotnet build server/src/Factum.Agent/Factum.Agent.csproj`.
- Plantilla: `python ops/plantilla/build_plantilla_extraccion_v6.py` (corre su `check()` sin fallos).
- Tests .NET (hay proyectos, #100 §12): `dotnet test server/tests/Factum.Backend.Tests` y
  `dotnet test server/tests/Factum.Agent.Tests`.
- Frontend: `npx tsc --noEmit` en `client/`.

**Tests que crea la SDD:**

- `Factum.Backend.Tests`: generar el informe desde un `ExtractionDto` fixture y assertar: (1) **no queda ningún
  `{…}`** sin reemplazar en ningún part (cuerpo + headers + footers); (2) está el pie "Realizado con Factum"; (3)
  existe la sección "Alcances y limitaciones" con el texto D3; (4) la tabla de categorías tiene una fila por
  categoría; (5) **caso sin `case_ref`**: no aparece la sección de actuación y el DOCX se genera; (6) **interrumpida**:
  aparece la sección de interrupción con el motivo; (7) `manifest_timestamp == null` ⇒ sin bloque de sello; con
  `granted` ⇒ TSA + fecha. Reusar el patrón de los tests del informe de inspección (abrir el DOCX con OpenXML y
  buscar texto).
- `Factum.Agent.Tests`: `WriteReport` escribe `INFORME.docx` + `INFORME.docx.sha256` con el hash correcto en una
  carpeta temporal; **no modifica** un `MANIFIESTO.tsv`/`MANIFIESTO.tsv.sha256` preexistente en esa carpeta; rechaza
  base64 inválido, vacío y > 20 MiB; rechaza con `409` si el diario está `running`; regenerar sobrescribe y recalcula
  la huella. Reusar el patrón de los tests de `WriteSeals`.

**Prueba manual (queda para el usuario / QA con el Samsung):**

1. Con una extracción **completa** con caso: abrir `/extracciones/[id]`, tocar "Generar informe"; el DOCX se
   descarga **y** aparece en la carpeta de la extracción como `INFORME.docx` + `INFORME.docx.sha256`. Abrirlo en
   Word/WPS: membrete de la marca del dueño, colores, pie "Realizado con Factum", todas las secciones, cita de la
   actuación (`case_ref`), sin ningún `{…}`.
2. Verificar que el `MANIFIESTO.tsv.sha256` de la carpeta **no cambió** (comparar antes/después) y que el sello
   `sellos/MANIFIESTO.tsv.tsr` sigue verificando.
3. Extracción **sin caso**: el informe se genera igual, sin causa/carátula.
4. Extracción **interrumpida/cancelada**: el informe refleja el estado final, la hora y el motivo, y lo efectivamente
   extraído.
5. Regenerar: sobrescribe `INFORME.docx`/`.sha256` sin errores.
6. Regresión: generar un informe de **inspección** (plantilla v6) y confirmar que sale igual que antes.

---

## 12. Dudas para el usuario

> Dos decisiones de bajo riesgo, con recomendación. No bloquean la arquitectura; confirmarlas antes de implementar.

### U1. Nombre y ubicación del DOCX dentro de la carpeta preservada
- **Recomendada:** nombre **fijo** `INFORME.docx` + `INFORME.docx.sha256` en la **raíz** de la carpeta (junto a
  `ACTA.txt`, `MANIFIESTO.tsv`, `CIERRE.txt`), **fuera** del manifiesto. Es canónico y predecible (como el resto de
  los artefactos de la carpeta), y la huella propia queda clara. El nombre **descriptivo**
  (`informe_extraccion_<folder_name>.docx`) se usa solo para la descarga en el navegador.
- Alternativa: nombre descriptivo también en la carpeta (menos predecible; puede acumular varios si cambia el
  `folder_name`).

### U2. ¿Además de guardar en la carpeta, descargar en el navegador?
- **Recomendada: sí.** D8=B hace que la vía principal sea guardar en la carpeta, pero descargar el mismo DOCX no
  cuesta nada (el blob ya está en el navegador) y es la **única** vía cuando la extracción es de otra PC (`onlyApi`)
  o Tatana es viejo (sin `extraction_report_v1`). En esos casos el aviso lo aclara.
- Alternativa: solo guardar en la carpeta (deja sin salida los casos `onlyApi`/Tatana viejo).
