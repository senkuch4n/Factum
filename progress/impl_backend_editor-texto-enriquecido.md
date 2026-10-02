# impl_backend — editor-texto-enriquecido

**Estado:** done (sin commit, como pidió el orquestador)
**Rama:** `feat/editor-texto-enriquecido`
**SDD:** `Refactorizaciones/editor-texto-enriquecido.md` (D1–D20 recomendadas; DP1 A y DP2 A confirmadas)
**Implementador:** implementer-backend (Claude Opus 5.5), 2026-10-01

## Archivos tocados

| Archivo | Cambio |
|---|---|
| `server/src/Factum.Backend/Factum.Backend.csproj` | `Markdig` `1.4.0`, versión fija (B1) |
| `server/src/Factum.Backend/Models/Case.cs` | `ReportTexts.Formato` (`string?`, `[BsonIgnoreIfNull]`, doc-comment), sin default (B2) |
| `server/src/Factum.Backend/DTOs/CaseDtos.cs` | `ReportTextsDto(..., string? Formato = null)` como último parámetro (B3) |
| `server/src/Factum.Backend/Services/Reports/ReportMarkdown.cs` (**nuevo**) | `ReportTextFormats` (+ `TryNormalize`), `ReportMarkdown` (`Pipeline`, `Parse`, `Validate`, `IsAllowedUrl`, `IsBlank`, `EscapeInline`, `EscapeLineStart`, `FromPlainText`, `FromLineTemplate`) y `ReportTextRules.IsBlank` (B4–B8) |
| `server/src/Factum.Backend/Services/Reports/ReportListNumbering.cs` (**nuevo**) | `abstractNum` de viñetas y decimal, y un `w:num` por lista, creados al vuelo y en el orden del esquema (B14) |
| `server/src/Factum.Backend/Services/Reports/ReportMarkdownRenderer.cs` (**nuevo**) | AST de Markdig → OpenXML: bloques, inlines y listas (B15–B17) |
| `server/src/Factum.Backend/Services/Reports/ReportService.cs` | Condiciones de bloque con `ReportTextRules.IsBlank`; B-R4 con dos caminos. `ReplaceParagraphPerLine` **sin cambios**: el diff no toca ni una línea de ese método (B18) |
| `server/src/Factum.Backend/Services/Reports/ReportValues.cs` | `RenderDefaults`: tokens escapados con `EscapeInline`, `FromLineTemplate` y `Formato = "markdown"` (B12) |
| `server/src/Factum.Backend/Services/Reports/ReportSettings.cs` | Valida cada `Report:DefaultTexts:*` configurado. Si es inválido, loguea un warning y usa el default versionado (B13) |
| `server/src/Factum.Backend/Services/Cases/CaseValidation.cs` | `ValidateReportTexts` valida formato, largos y contenido; `ValidateForGenerate` usa `ReportTextRules.IsBlank` (B9, B10) |
| `server/src/Factum.Backend/Services/Cases/CaseService.cs` | `SaveReportTextsAsync` guarda `Formato` normalizado y los textos tal cual llegan (B11) |
| `README.md` | Fila `Report:DefaultTexts:*`: Markdown línea por línea, qué se admite y qué pasa con uno inválido (B19) |
| `server/tests/Factum.Backend.Tests/ReportTestSupport.cs` (**nuevo**) | Fakes y helpers de los tests nuevos. Los tests existentes no se tocaron |
| `server/tests/Factum.Backend.Tests/ReportMarkdownTests.cs` (**nuevo**) | T1–T7 y `TryNormalize` |
| `server/tests/Factum.Backend.Tests/ReportTextsValidationTests.cs` (**nuevo**) | T8–T10, más el fallback de B13 |
| `server/tests/Factum.Backend.Tests/ReportMarkdownDocxTests.cs` (**nuevo**) | T11–T21, más una muestra opcional con `FACTUM_RENDER_DIR` |

**No se tocaron:** `client/`, `agent-ui/`, `server/src/Factum.Agent/` (su `appsettings.json` es un cambio local del usuario y está como lo dejó), `appsettings.Local.json`, las plantillas `Templates/*.docx`, `ops/plantilla/build_plantilla_v6.py`, `backlog.json` ni `progress/current.md`. **No se tocó MongoDB ni `Storage:DataDirectory`**, y no hay migraciones.

## Contrato compartido: coincide con la SDD §4

- `report_texts.formato` (`ReportTexts.Formato`): vale `"markdown"` o `null`. Sale en `GET /api/cases/{id}` y en la respuesta de `PUT …/report-texts`. En Mongo es `ReportTexts.Formato` y no se escribe si es null.
- `formato` en la raíz del body de `PUT /api/cases/{id}/report-texts` (`ReportTextsDto.Formato`). Se acepta `"markdown"`, ausente, `null`, `""` o `"texto"`; los cuatro últimos significan texto plano. Cualquier otro valor da 400 `El campo formato no es válido`. La comparación es exacta: `"Markdown"` también es inválido.
- `formato: "markdown"` en la raíz de la respuesta de `GET …/report-texts/defaults`.
- Errores 400: `El campo {clave} tiene contenido no permitido: HTML|imagen|enlace`. Se valida campo por campo en el orden `objeto_informe … reserva` y el primer error corta. El largo se valida antes, con el mensaje de siempre.
- Las ocho claves de texto y las claves de `missing` no cambian.

## Verificación

```
$ dotnet build server/src/Factum.Backend/Factum.Backend.csproj
    4 Advertencia(s)   ← NU1902/NU1903 (SharpCompress/Snappier, dependencias de MongoDB.Driver), ya estaban antes; 0 warnings CS
    0 Errores
$ dotnet build server/src/Factum.Agent/Factum.Agent.csproj
    0 Advertencia(s)
    0 Errores
$ dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
Correctas! - Con error: 0, Superado: 209, Omitido: 0, Total: 209
   (incluye los existentes: ReportDesignTests, EvidenceZipTests, etc.; único warning CS8619 en EvidenceZipTests.cs:57, ya estaba antes)
$ python3 ops/plantilla/build_plantilla_v6.py --check server/src/Factum.Backend/Templates/plantilla_informe_v4.docx server/src/Factum.Backend/Templates/plantilla_informe_v6.docx
OK
```

**Un caso viejo sale igual que antes (verificado contra el binario anterior):** extraje `HEAD` con `git archive` a una carpeta del scratchpad, sin tocar el working tree, y generé el mismo caso en texto plano con el código viejo y con el nuevo. El caso tenía `*`, `_`, `#`, `` ` ``, `{caratula}`, `<b>`, `&`, `[y]`, líneas `"- "`, `1. `, `> `, sangría y líneas vacías. Comparé el DOCX parte por parte. `document.xml` es idéntico (solo cambian los SHA-256 del ZIP, que dependen del contenido cifrado y del momento). Los footers difieren solo en el `Id` aleatorio de la relación de la imagen del sello, que cambia en cada generación igual. `numbering.xml` y `settings.xml` son idénticos byte a byte.

**Muestras** (datos ficticios), en `/private/tmp/claude-501/-Users-joelmiguelserrudo-Documents-Projects-Factum/d331c5ef-f445-4af8-98b0-4d017b56de51/scratchpad/muestras-editor/`:
- `editor_markdown_todos_los_formatos.docx` / `.pdf`: negrita, cursiva, subrayado, `***<u>bi</u>***`, subtítulo, viñetas de 4 niveles (el 4.º se aplana), `&nbsp;`, numerada desde 3, cita con código en línea, bloque con `<div>` y ```` ``` ```` adentro, tres enlaces (con URL, con texto igual a la URL y `mailto:`), `**{caratula}**` y literales escapados, salto manual, y "Conclusiones" como numerada aparte.
- `editor_caso_viejo_texto_plano.docx` / `.pdf`: el mismo caso sin marca.
- `pag-4.png`, `pag-5.png`: render de LibreOffice de las páginas con los formatos. La numeración romana de los títulos (VIII, IX, X…) sale intacta.
- El PDF se hizo con `soffice --headless` y un perfil propio en el scratchpad; no quedó ningún proceso. Los procesos de WPS que se ven en `pgrep` son del usuario y no los toqué.

## Decisiones no obvias

1. **U+00A0 al principio de línea (D10).** Markdig 1.4.0 recorta los U+00A0 iniciales de cada línea de un párrafo como si fueran espacios, así que la sangría hecha a mano se perdía en el DOCX (lo detectó T2 con F10). Para no perderla, `ReportMarkdown.Parse` pasa a la marca `U+E000` (uso privado) solo la racha de U+00A0 que está al principio de línea, después de los marcadores de cita o lista. El renderer la vuelve a U+00A0 (`RestoreNbsp`). La marca no es espacio, así que tampoco abre un bloque. No cambia el dialecto ni lo que se guarda: es interno al parseo. T20 lo cubre. Efecto colateral despreciable: un U+E000 que el perito escriba a propósito saldría como U+00A0.
2. **Salto manual en un párrafo justificado.** Con un `Break`, Word y LibreOffice estiran la línea anterior a todo el ancho (se veía "Línea ··········· uno" en la muestra). Si el renderer emite un salto manual dentro de un párrafo justificado, agrega `w:doNotExpandShiftReturn` a `w:compat` de `settings.xml`. Solo pasa en ese caso: el camino de texto plano nunca lo agrega y T11 comprueba que un caso viejo no lo tiene. Ya está verificado en el PDF.
3. **Espacios en `FromPlainText`/`EscapeLineStart`, byte a byte con el cliente.** Leí (sin tocarlo) `client/src/lib/report-markdown.ts`. Para "espacio o fin", el recorte del final de línea y el `TrimStart` del ítem uso el mismo conjunto que el `\s` de JavaScript (`[\t\n\v\f\r    -     　﻿]`); el `\s` y el `TrimEnd()` de .NET difieren en U+0085 y U+FEFF. Interpreto igual que el cliente el paso 4.3: si la línea tenía espacios iniciales (ahora U+00A0), ya no se escapa nada más.
4. **Sangría base de la sección.** El placeholder de "Notas técnicas" tiene `w:ind left=360` en la plantilla. Tomé como "Left del contenedor" de nivel superior el `Left` del pPr base: las listas quedan en `base + 360·(ilvl+1)` y la cita en `base + 567`. En las demás secciones la base es 0 y queda exactamente lo de la SDD (360/720/1080, 567).
5. **Ítem cuyo primer bloque no es un párrafo** (por ejemplo, un ítem que abre otra lista): la numeración queda pendiente y la toma el primer párrafo que se crea dentro del ítem. Si el primer hijo es otra lista, el ítem de afuera conserva su viñeta en un párrafo vacío propio. Un ítem vacío (`- `) igual muestra su viñeta. Los párrafos numerados no se descartan en los extremos.
6. **Autolink de correo** (`<perito@ejemplo.com>`): se acepta y sale como `mailto:`.
7. **`Validate` rechaza un enlace por referencia** (`[a]` + `[a]: javascript:…`): Markdig lo resuelve a un `LinkInline` con esa URL.
8. **`IsBlank`**: el HTML que no es `<u>` (rechazado al guardar) cuenta como visible, igual que en la regex del cliente. Sus letras cuentan.
9. **La plantilla no cambia.** El pedido del orquestador mencionaba "estilos/numeraciones en `build_plantilla_v6.py`", pero la SDD (§2 y D6) dice que la plantilla y el script **no cambian** y que todo se genera por código. Seguí la SDD; `--check` da `OK`.
10. **Fakes compartidos.** Están en `ReportTestSupport.cs` y los usan solo los tests nuevos. `ReportDesignTests` y `ReportValuesIntegrantesTests` siguen con sus fakes propios, para no cambiar los tests existentes.

## Pendiente para el usuario

- **B21 (prueba manual contra la base de desarrollo): no se hizo**, porque el orquestador indicó no tocar MongoDB. Hay que hacerla a mano según §11.2 de la SDD, sobre un caso en borrador creado para la prueba:
  - `PUT` con Markdown válido → 200.
  - `PUT` con `<script>` o `[x](javascript:alert(1))` → 400.
  - `PUT` sin `formato` → 200, y en `mongosh` el documento no tiene `ReportTexts.Formato`.
  - Generar el caso y abrir el DOCX en Word (sin aviso de reparación) y, si se puede, en WPS.
- Confirmar en Word real que `doNotExpandShiftReturn` evita la línea estirada; en LibreOffice ya está verificado.
