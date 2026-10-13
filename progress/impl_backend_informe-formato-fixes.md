# impl_backend — informe-formato-fixes (#62)

**Estado:** done (sin commit, como pidió el orquestador). Rama `feat/informe-formato-fixes`.
**SDD:** `Refactorizaciones/informe-formato-fixes.md` · **HU:** `docs/hu-informe-formato-fixes.md`.
Reconciliado con lo que trajo develop (#58–#61): `SaveReportTextsAsync` conserva `operaciones_base` (DT13) y el
log de `EvidenceZipVerificationException`; `RenderDefaults` sigue devolviendo `OperacionesBase`.

## Archivos tocados

Nuevos:
- `server/src/Factum.Backend/Common/SafeText.cs`: `TextShape`, `SafeText.Clean/CleanOrNull/StripInvisible/IsBidiControl/IsNonCharacter/IsStrippedControl`. Nombre y firma exactos de §5.1 (los reutiliza #63).
- `server/src/Factum.Backend/Services/Reports/ReportCoverLayout.cs`: `CoverInput`, `ReportCoverLayout.ChooseLevel/EstimateTwips/FitsAtLevel` y las constantes por nivel.
- Tests: `SafeTextTests.cs`, `ReportFormatFixesTests.cs` (incluye el kit `FormatFixesKit`), `ReportCoverLayoutTests.cs`, `ReportQaRegressionTests.cs`.

Modificados:
- Entrada (§5.2): `Services/Cases/CaseValidation.cs` (`C()` y `observaciones`), `Services/Catalogs/CatalogText.cs` (`CleanValue`), `Services/Cases/CaseService.cs` (device en `CreateAsync`, `imei` en `UpdateAsync`, `CleanReportTexts` antes de validar en `SaveReportTextsAsync`), `Services/Profile/ExpertProfileService.cs`, `Services/Admin/AccountFieldRules.cs` (`Normalize` = MultiLine + Trim), `Services/Branding/BrandingRules.cs` (`StripInvisible` + Trim antes del `HasControlChars` de siempre), `Services/Branding/BrandingService.cs` (DA2), `Controllers/AgentAuditController.cs` (`hostname`, `os_user`, `agent_version`; el case_event derivado usa los mismos valores limpios).
- Render: `Services/Reports/ReportValues.cs` (`Clean` = SafeText MultiLine + saltos/tabs; `Ellipsize`; `{marcaModeloDispositivo}` con `OrNoInformado`), `Services/Reports/ReportMarkdownRenderer.cs` (`IsCleanUrl` en `RenderLink`/`RenderAutolink`), `Services/Reports/ReportService.cs` (B-R2c, contacto unido en el nivel 2 solo en el footer del membrete, valores recortados para `HeaderPart` en B-R6, reglas H14 en `RewriteParagraph(joinAdjacent)`, `capAtNative`, B-R9b `SanitizeAllParts`), `Models/DeviceInfo.cs` (`OsLabel` idempotente).
- Plantillas: `ops/plantilla/build_plantilla_v4.py` (H10, H12, H11 y huellas `543E58FA`/`05004875`), `ops/plantilla/build_plantilla_v6.py` (H01 `FIRMA_BLOCK` + filete del cierre con `keepNext`, H16 idioma, H17 orden de `CT_Style`, `check_v62` con (a)–(f)), `ops/plantilla/README.md` (sección "v6: arreglos de #62"), `server/src/Factum.Backend/Templates/plantilla_informe_v{4,6}.docx` regeneradas por script.
- Tests ajustados: `ReportBodyImagesDocxTests.cs` (T12: el 90×30 ahora a tamaño nativo, 857 250 EMU), `ReportMarkdownTests.cs` (F18–F24; T2 compara contra `ReportValues.Clean(input)`; `DisableDiscoveryEnumeration` porque xUnit serializa los datos y cambia los surrogates sueltos de F21 por U+FFFD).

No toqué `client/`, `agent-ui/`, `server/src/Factum.Agent`, `progress/sesiones/` ni los archivos sin trackear de otras HU.

## Contrato (coincide con la SDD §8)

No cambia ningún campo JSON. `os_label` cambia de valor ("Android 14"), no de nombre. `PUT /report-texts` devuelve los 8 textos limpios; `formato`, `trigger`, `restored_from` y `operaciones_base` no se tocan. El 400 `MsgControlChars` de la marca sigue igual.

### Lista exacta para el espejo `cleanText` del cliente (`ReportValues.Clean`)

Orden: primero se quita, después se normalizan saltos y tabs. Recorrido por code point (un alto seguido de un bajo forman uno).

**Se quita:**
- C0 U+0000–U+0008, U+000B, U+000C, U+000E–U+001F.
- Bidi U+202A, U+202B, U+202C, U+202D, U+202E, U+2066, U+2067, U+2068, U+2069.
- No-caracteres U+FDD0–U+FDEF, U+FFFE, U+FFFF y, en los planos 1 a 16, todo code point con `(cp & 0xFFFE) == 0xFFFE` (U+1FFFE, U+1FFFF … U+10FFFE, U+10FFFF): se descarta el par entero.
- Surrogate suelto: alto sin bajo detrás, o bajo sin alto delante.

**Se conserva:** `\n`; `\r` y `\r\n` (pasan a `\n` en el paso 2); `\t` (pasa a un espacio en el paso 2); DEL y C1 U+007F–U+009F (incluye U+0085); LRM/RLM U+200E/U+200F; ALM U+061C; ZWJ U+200D; ZWNJ U+200C; NBSP U+00A0; U+2028/U+2029; BOM U+FEFF; U+FFFD; emoji y cualquier par válido que no sea no-carácter; árabe y hebreo.

**Paso 2:** `\r\n` → `\n`, `\r` → `\n`, `\t` → `" "`.

Vectores (los mismos que F18–F24 de `ReportMarkdownTests`): `"A\uFFFEB\uFFFF"`→`"AB"`; `"x\u202Eevil\u202Cy"`→`"xevily"`; `"a\u2066b\u2069c\u200Ed"`→`"abc\u200Ed"`; `"lone\uD800 hi\uDC00 ok \U0001F600"`→`"lone hi ok \U0001F600"`; `"del\u007Fc1\u0085"` sin cambios; `"\r\uFFFE\nfin"`→ (FromPlainText) `"fin"`; `"\uFDD0x\U0001FFFE"`→`"x"`.

## Verificación

- `python3 ops/plantilla/build_plantilla_v4.py --check …` → `OK`. `build_plantilla_v6.py --check …` → `OK`. La v6 vieja falla los checks nuevos (`FALLA:firma`, `frases`, `idioma`, `orden-estilos`), así que el check discrimina.
- Antes de tocar nada verifiqué que los scripts regeneraban byte a byte las plantillas versionadas (SHA-256 iguales). Después del cambio, dos corridas dan el mismo SHA-256: v4 `30a48c9f61b7ced6e37d82cb3b169ba41595bad1d79463ae21960e0b7fcb4413`, v6 `5339f05ffa8bbf021b3d3de3bdf52c77b3df31436750e77a87d2140c9d259510`.
- `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` → `Build succeeded`, sin warnings de C#. Solo los NU1902/NU1903 de paquetes, que ya estaban. En el proyecto de tests quedan CS8714/CS8619 en `EvidenceZipTests.cs`, que también ya estaban.
- `dotnet test server/tests/Factum.Backend.Tests` → `Passed! Failed: 0, Passed: 1012, Skipped: 7, Total: 1019`.
- `FACTUM_RENDER_DIR=<scratch> FACTUM_SOFFICE=1 dotnet test … --filter "ReportQaRegression|Muestras"` → todos en verde. Portada en una página en `c3_extremos`, `cover_max_logo` y `cover_all_max`: el título del escrito queda en la página 2 según `pdftotext`. La firma de `c2r` queda entera en una página. Páginas: c1 10, c2r 6, c2/c2b 5, c3 6, c3b/c3c 5, c3x 5, c4 27, c5 5, cover_max_logo 5, cover_all_max 6.
- `OpenXmlValidator(Microsoft365)` sobre **todas las partes** de los 20 DOCX de render y sobre la v6 nueva: **0 errores**. La v6 anterior tenía 29, todos en `styles.xml`, igual que en el QA.
- WPS Office: abrí `c1_tipico`, `c3_extremos` y `cover_all_max`. Los tres abrieron sin aviso de reparación, con 10, 6 y 6 páginas (las mismas que LibreOffice). Las portadas se ven igual que en el render. Cerré WPS al terminar. La apertura en Microsoft Word queda para el usuario (§11.4).

### Renders mirados (evidencia: `/private/tmp/claude-501/-Users-joelmiguelserrudo-Projects-Factum/7b8e9155-f8ea-4033-8a4e-349a2f9fa9fa/scratchpad/impl-62/img/`)

- `cover_all_max-1.png` (nivel 2): la portada entra en una página, con la causa de 600 caracteres, la carátula a 9 pt, el perito de 150 y el membrete con el contacto unido con " · " a 8 pt.
- `cover_all_max-2.png`: encabezado interior de 2 líneas, con la referencia y la organización recortadas con "…".
- `c3_extremos-1.png` (nivel 1): portada en una página y membrete completo. `c3_extremos-2.png`: encabezado de 2 líneas, sin texto invertido ("evil" se lee normal) y hebreo y árabe bien.
- `cover_max_logo-1.png` (nivel 1): una página. `c1_tipico-01.png` (nivel 0): idéntica a la de hoy.
- `c2r_minimo_real_firma-6.png`: el bloque "SE DEJA CONSTANCIA / línea / nombre / profesión / carácter / filete" pasa entero a la página 6; ya no queda la línea sola al pie.
- `c1-03.png`: "siendo las 10:30 horas" (13:30 UTC en zona AR) y sin "Se constató:". `c1-07.png`: Notas técnicas y Reserva con la misma sangría de 4 cm que el resto.
- `c4-12.png`: la 200×150 a tamaño nativo (5,3 cm), sin ampliar; la 1000×1000 sigue ajustada a la caja. `c4-05.png`: tabla de hashes sin cambios.
- `c3md-5.png`: el markdown completo sale igual que antes (H08 y H09 quedan fuera de alcance).
- `wps_*.png`: capturas de pantalla de WPS con cada documento abierto.

## Decisiones no obvias / desvíos respecto de la SDD

1. **`150x900` en `c4_capturas`:** la SDD esperaba "nativo", pero a 96 dpi mide 23,8 cm de alto y la caja del anexo es de 10,5 cm. Manda la caja (630 000 × 3 780 000 EMU) y nunca se amplía, que es la regla de D4. El test lo afirma así. La 200×150 sí sale nativa.
2. **H16 vs. "ninguna parte contiene en-US":** la SDD fija `w:eastAsia="en-US"` en `rPrDefault` (§7.2). Los tests y `--check` verifican que no haya ningún `w:val="en-US"`, que es lo que importa para la ortografía.
3. **`c2r`, "sin títulos de Objeto":** el título "OBJETO DEL INFORME" está fuera del bloque `{#objetoInforme}` porque la sección tiene texto fijo, y no cambió. Se verifica que no aparezcan "NOTAS TÉCNICAS" ni "RESERVA" ni el texto opcional de objeto.
4. **Capacidad del nivel 2:** la SDD no fija el umbral del `LogWarning` del último nivel. Usé `Level2Capacity = 8520` (7800 más lo que ahorran los espaciadores, (600−240)×2). Las demás constantes son las iniciales de §6.4. No hizo falta calibrarlas: los renders pasaron con 6000/7800 y los topes 60/70.
5. **H14:** las reglas viven en `RewriteParagraph(..., joinAdjacent: true)`, que solo usa B-R6. B-R1, la tabla de hashes y la atribución usan el default `false`.
6. **B-R9b:** usa `SafeText.Clean(MultiLine)`, que equivale exactamente a StripInvisible más la quita de C0 salvo `\t\n\r`. Recorre el cuerpo, todos los headers y footers (incluido uno creado por B-R8), footnotes y endnotes. Con datos normales no loguea nada (hay un test para eso).
7. **Contacto del nivel 2:** el footer del membrete se identifica antes de B-R2, buscando el párrafo `{#MEMBRETE}`, porque B-R2 borra los marcadores.
8. **`BrandingRules.HasControlChars`** sigue usando `char.IsControl`, así que DEL/C1 en el nombre o el contacto de la marca siguen dando 400. Es el contrato existente y la SDD dice "sin cambios". Para #63: es la única entrada donde C1 no "se conserva".
9. **Fuentes de los tests:** todos los caracteres especiales van como escapes `\uXXXX` en el fuente (sin invisibles literales). Ningún `InlineData` lleva surrogates sueltos (xUnit los serializa).

## Observaciones para el orquestador / usuario

- **U+0085 (NEL) conservado (D1):** en el render de LibreOffice de `c3_extremos`, la carátula con U+0085 muestra un salto o una superposición de glyphs en esa posición. Es XML válido y la validación D1 pide conservarlo, así que no lo toqué. Si molesta en Word, se resuelve en otra HU.
- **El backend que corre en :8080 no se reinició:** levanta los cambios recién cuando se reinicie.
- **Avisos de macOS:** al usar `screencapture` para las capturas de WPS, macOS mostró un aviso "tmux solicita acceder a tu pantalla". También había uno de "Orca" pidiendo el micrófono, que no causé yo. No respondí ninguno de los dos: conviene que el usuario los rechace o los ignore.
- WPS tiene activada la "copia de seguridad local", así que puede haber guardado copias de los 3 DOCX de prueba (datos ficticios) en su carpeta de backups.
- Borré los temporales (DOCX, PDF, proyecto validador y plantillas intermedias). Solo quedan las imágenes de `img/`.
- Prueba manual pendiente para el usuario (§11.4): abrir en Microsoft Word, generar un caso Android real y pegar caracteres invisibles en la carátula.
