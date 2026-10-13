# impl_backend — wizard-pulido-ux (#64)

**Estado:** done · **Rama:** `feat/wizard-pulido-ux` (sin commit, según instrucción) · **Fecha:** 2026-10-13
**SDD:** `Refactorizaciones/wizard-pulido-ux.md` §9 / checklist §10.1 (B1–B11)

## Archivos tocados

| Archivo | Cambio |
|---|---|
| `server/src/Factum.Backend/Services/Reports/ReportService.cs` | B1 `MaxSafeNameLength = 60`, `ZipNameRegex` (comentario con la ruta de la regex de Tatana). B2 `LegacySanitize` (copia textual de la `Sanitize` anterior) y `LegacyZipFilenameFor`. B3 `Sanitize` nueva (DT12: NFC → filtro `IsLetterOrDigit` → `Trim('_')` → si > 60 corta en el último `_` con índice ≤ 60 y `TrimEnd('_')`, sin `_` corta duro en 60 → `"caso"`; los `_` repetidos **no** se colapsan). B4 `ResolveZipFilename` (DT10) y `StemFromZip` (DT11). B5 `GenerateAsync`: DOCX = `informe_pericial_{StemFromZip(zipFilename, cas)}.docx`. B6 `GenerateReportAsync`: `safeName = StemFromZip(zipFilename, cas)`; la limpieza de restos recorre la base del ZIP y, si difiere, también `Sanitize(datos del caso)` (`informe_pericial_*`/`informe_forense_*`, `.docx`/`.pdf`). Nunca toca `evidencia_*.zip`. |
| `server/src/Factum.Backend/Services/Cases/AgentGeneration.cs` | B7 `PrepareGenerationAsync`: `ZipFilename = ReportService.ResolveZipFilename(cas)` + log `"Caso {CaseId}: se conserva el nombre de ZIP ya registrado {ZipFilename}"` solo si difiere de `ZipFilenameFor(cas)`. `FinishGenerationAsync` sin cambios (sigue comparando contra `pending.ZipFilename`). |
| `server/tests/Factum.Backend.Tests/ReportServiceSanitizeTests.cs` (nuevo) | B8: los 10 grupos de §9.4 (25 casos de test). |

No se tocó `server/src/Factum.Agent` (`git diff --stat server/src/Factum.Agent` vacío), ni `client/`, ni `agent-ui/`.

## Reconciliación con #61 / #62

La rama sale de `develop` con #58–#63 mergeadas. #62 cambió el armado del DOCX (otras funciones de `ReportService`) y no
tocó `Sanitize`/`ZipFilenameFor`/los bloques de nombres; #61 no tocó nada de esto en backend. Se editó sobre el código
actual, sin conflicto. Ningún test existente arma a mano un `informe_pericial_{Sanitize(...)}` con datos de más de 60
caracteres, así que no hubo que ajustar ninguno; `ReportServiceAgentTests.ZipFilenameFor_SameFormulaAsOldFlow` sigue
igual y en verde.

## Verificación

El backend de desarrollo está corriendo en :8080 desde `bin/Debug`, así que se compiló con `OutDir` temporal (scratchpad)
para no pisar sus binarios.

```
dotnet build server/src/Factum.Backend/Factum.Backend.csproj -p:OutDir=<scratchpad>/bebuild/
    4 Warning(s)   0 Error(s)
```
Las 4 warnings son las preexistentes NU1902/NU1903 (SharpCompress, Snappier), sin warnings nuevas de compilación.

```
dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj -p:OutDir=<scratchpad>/testbuild/
Passed!  - Failed: 0, Passed: 1091, Skipped: 7, Total: 1098
```
(Skipped = los 7 que ya se saltaban antes.) Las warnings del proyecto de tests son las preexistentes de
`EvidenceZipTests.cs` (CS8714/CS8619); el archivo nuevo no agrega ninguna.

## Ejemplos de nombre viejo → nuevo

| Expediente + titular | Antes | Ahora |
|---|---|---|
| `CCC 12.345/2026 – Señoría` + `María José Fernández de la Santísima Trinidad Gómez` (el de la HU) | `evidencia_CCC_12_345_2026___Señoría_María_José_Fernández_de_la_Santísi.zip` | `evidencia_CCC_12_345_2026___Señoría_María_José_Fernández_de_la.zip` |
| `CCC 12345/2026` + `María José Fernández de la Santísima Trinidades Gómez` | `evidencia_CCC_12345_2026_María_José_Fernández_de_la_Santísima_Trinidad.zip` (corta "Trinidades") | `evidencia_CCC_12345_2026_María_José_Fernández_de_la_Santísima.zip` |
| `Expte 777/2026` + `María Ana Rodríguez` con la "í" en NFD (i + U+0301, p. ej. pegado desde macOS) | `evidencia_Expte_777_2026_Mari_a_Ana_Rodríguez.zip` | `evidencia_Expte_777_2026_María_Ana_Rodríguez.zip` |
| `1234` + `Titular Ficticio` (corto, ASCII: los 5 casos de la base de desarrollo son así) | `evidencia_1234_Titular_Ficticio.zip` | igual |

El DOCX sale siempre como `informe_pericial_<misma base del ZIP>.docx`.

## Decisiones no obvias

- **Test de la regex idéntica a Tatana:** además de comparar `ZipNameRegex.ToString()` con la constante del test, el
  test lee `server/src/Factum.Agent/Common/AgentFileNames.cs` y exige que la misma cadena esté ahí. Encuentra el
  archivo subiendo desde la carpeta del fuente del test (`[CallerFilePath]`), no desde `bin/`, así funciona también
  con `OutDir` fuera del repo. Si alguien cambia la regex de un lado sin cambiar el otro, el test falla.
- **`StemFromZip`** exige que haya al menos un carácter entre `evidencia_` y `.zip`; `"evidencia_.zip"` cae en el
  `Sanitize` del caso (cubierto en el test 9).
- **Limpieza de restos en `GenerateAsync` (flujo server):** ahí `zipFilename = ZipFilenameFor(cas)`, así que la base
  del ZIP y la de los datos son la misma y alcanza con una pasada. La doble pasada (DT11) se aplicó solo en
  `GenerateReportAsync`, que es donde el ZIP puede venir con un nombre legacy. Hay un test que lo verifica.
- **Test 10 (DOCX = ZIP)** usa los datos `CCC 12345/2026` + `…Trinidades Gómez`, donde la regla vieja y la nueva dan
  nombres distintos, y verifica el nombre del DOCX con el ZIP nuevo y con el legacy.

## Contrato

Sin campos nuevos ni renombrados. `zip_filename` (prepare / finish / `GET /api/cases/{id}`) y `files.pdf_filename`
siguen igual; solo cambia el **valor** según la SDD §6.3. Todo nombre emitido cumple
`^evidencia_[\p{L}\p{N}_]{1,60}\.zip$` y está en NFC (cubierto por tests).

## Datos de desarrollo

No se usó Mongo ni `Storage:DataDirectory`. Los tests escriben solo en un directorio temporal propio
(`factum-sanitize-<guid>`) que se borra en `Dispose`. No se reescribe ningún documento: los casos con `ZipFilename`
registrado lo conservan por `ResolveZipFilename`.

## Pendiente para el usuario (prueba manual)

SDD §11, punto 1: en Windows 11 con Tatana, generar un caso con el expediente y el titular de la HU y verificar el
nombre del ZIP y del DOCX y que el ZIP se abra con el Explorador y con 7-Zip. Punto 2: los 5 casos existentes siguen
mostrando su `zip_filename` de siempre.
