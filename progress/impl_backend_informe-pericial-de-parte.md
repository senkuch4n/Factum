# impl_backend — informe-pericial-de-parte

**Estado:** done (sin commit, como se pidió)
**SDD:** `Refactorizaciones/informe-pericial-de-parte.md` §12.1 (B1–B18) + Anexo A + Anexo B
**Rama:** `feat/informe-pericial-de-parte`
**DP1:** textos aceptados → `B9b` implementado con los textos de la propuesta. **DP2:** frases fijas aceptadas, implementadas en la v4.

Este archivo no contiene datos reales: ni domicilio, ni colegio, ni nombres. Todos los ejemplos son ficticios.

## Archivos tocados

### Backend (`server/src/Factum.Backend`)

| Archivo | Cambio |
|---|---|
| `Models/Case.cs` | Campos de §4.2 (`SchemaVersion`, `Perito`, datos de la causa, `ReportTexts`, `CaptureRoles`, `ReportHash`), clases `PeritoSnapshot`, `ReportTexts` y `CaptureRole`, y `[BsonIgnoreExtraElements]` en todas las clases (T2). `PeritoSnapshot.IsComplete` lleva `[JsonIgnore, BsonIgnore]`. |
| `Models/DeviceInfo.cs` | `Name` y `[BsonIgnoreExtraElements]` |
| `Models/ExpertProfile.cs` | **nuevo** (§4.1) |
| `Common/Result.cs` | `ErrorKind` (Failure, Validation, NotFound, Forbidden, Conflict) y `Missing`. `Fail(string)` sigue igual para los otros usuarios de `Result` (Auth). |
| `Controllers/ResultHttpExtensions.cs` | **nuevo**. Mapea `ErrorKind` a 400 `{error[,missing]}`, 403, 404 y 409. Reemplaza los `err.Contains(...)`. |
| `Controllers/CasesController.cs` | `PUT /{id}`, `GET /{id}/report-texts/defaults`, `PUT /{id}/report-texts` y `PUT /{id}/capture-roles`. El resto se adapta al mapeo por `ErrorKind`. |
| `Controllers/ProfileController.cs` | **nuevo**: `GET` y `PUT /api/profile` |
| `DTOs/CaseDtos.cs` | `CreateCaseRequest` con todo `string?` y sin `[Required]`; `UpdateCaseRequest` (+`imei`); `ReportTextsDto`; `CaptureRoleDto`, `CaptureRolesRequest` y `CaptureRolesResponse`; `DeviceInfoDto.Name`; `GenerateResponse.ReportHash` |
| `DTOs/ProfileDtos.cs` | **nuevo**: `ExpertProfileRequest` y `ExpertProfileResponse` |
| `Infrastructure/ExpertProfileRepository.cs` | **nuevo**: colección `expert_profiles`, `GetAsync` y `UpsertAsync` (upsert por `_id` = DNI) |
| `Infrastructure/MongoRepository.cs` | `UpdateCaseDataAsync`, `UpdateReportTextsAsync` y `UpsertCaptureRolesAsync`, siempre con `$set` y filtro "editable". `UpdateGeneratedAsync` suma `reportHash`. `ListByOfficerAsync` usa `Projection.Exclude(ReportTexts)`. Record `CaseDataUpdate`. |
| `Services/Cases/CaseValidation.cs` | **nuevo**: `ValidateProfile`, `ValidateCaseData`, `ValidateForGenerate`, `ValidateReportTexts`, las claves de §6.4 y los largos 300/500/20 000 |
| `Services/Cases/CaseService.cs` | Create y Update (exigen perfil completo, copian el perfil, `SchemaVersion=1`, `Device.Name`), defaults, report-texts, capture-roles, y la validación de §7.8 en `GenerateAsync` |
| `Services/Profile/ExpertProfileService.cs` | **nuevo** |
| `Services/Reports/ReportService.cs` | Reescrito según el Anexo B (B-R0 a B-R9), flujo D10 y `Sanitize` corregido |
| `Services/Reports/ReportValues.cs` | **nuevo**: valores de los placeholders (§7.2), tokens y conteos (§7.6) y `RenderDefaults` |
| `Services/Reports/ReportDefaultTexts.cs` | **nuevo**: textos de DP1 (B9a + B9b) y `ReportTextRenderer` (una pasada; omite las líneas `"- "` cuyo `{cantidad…}` vale 0) |
| `Services/Reports/EvidenceClassifier.cs` | **nuevo** (§4.5) |
| `Services/Reports/ReportOptions.cs` / `ReportSettings.cs` | **nuevos**: config `Report`, zona resuelta al arrancar con warning y fallback a UTC-03:00, domicilio con máximo de 300 y defaults por estudio |
| `Program.cs` | `Configure<ReportOptions>` y singletons `IReportSettings` (resuelto al arrancar) y `IExpertProfileRepository`; scoped `IExpertProfileService` |
| `appsettings.json` | Sección `Report` vacía (§4.4) |
| `Dockerfile` | `tzdata` en el stage runtime |
| `Templates/plantilla_informe_v4.docx` | **nuevo**, generado por el script (sha256 `9ccdff7647be13eaf18213ba816cb15b62b93d32c4b3b1cbfe88a535aba2d6f3`; la salida es determinista). **Sin trackear todavía:** hay que hacerle `git add` al commitear. |
| `Templates/plantilla_informe_v3.docx`, `Templates/sin-foto-placeholder.png` | **borrados** con `git rm` (la baja quedó en el index, sin commit). `factum-sello.png` se conserva. |

### Otros

| Archivo | Cambio |
|---|---|
| `ops/plantilla/build_plantilla_v4.py` | **nuevo** (Anexo A): stdlib, edición por `w14:paraId` con huellas no sensibles, modo `--check` |
| `ops/plantilla/README.md` | **nuevo** |
| `.gitignore` | `docs/INFORME*` |
| `README.md` | Tabla de config (`Report:*`), sección "Placeholders de la plantilla del informe" reescrita para la v4 y sección nueva "Informe pericial: configuración", con un ejemplo de placeholders y cómo regenerar la v4 |

No toqué `client/`, `agent-ui/`, `server/src/Factum.Agent`, `backlog.json` ni `progress/current.md`.

## Contrato (§6): verificado contra el JSON real

El JSON real se verificó con las respuestas de B17 (snake_case_lower):

- **`Case`** trae `schema_version`, `perito{nombre,matricula,profesion,caracter,tratamiento}` (sin campos extra), `nombre_tribunal` … `linea_dispositivo`, `report_texts`, `capture_roles[{filename,role}]`, `report_hash` y `device.name`.
- **`ExpertProfileResponse`**: `dni`, `nombre`, `matricula`, `profesion`, `caracter`, `tratamiento`, `exists`, `is_complete`, `updated_at`.
- **`ReportTexts`**: los 8 campos + `updated_at`.
- **`GenerateResponse`**: `case`, `zip_hash`, `password`, `files{zip,pdf}` y `report_hash`.
- **Capture-roles** responde `{ capture_roles: [...] }`.
- **Claves de `missing`**: idénticas a §6.4. Las crucé (solo lectura) con `client/src/lib/pericial.ts` y coinciden.

## Verificación

### Build

```
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
  0 Errores, 4 Advertencia(s)  → las 4 son NU1902/NU1903 (SharpCompress/Snappier), preexistentes (mismo número que antes del cambio)
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
  0 Errores, 0 Advertencia(s)
```

### Plantilla (B11)

- **`--check`:** `OK`.
- **Build del script:** `OK`. Dos corridas dan el mismo sha256, así que la salida es determinista.
- **`git check-ignore -v "docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx"`** → `.gitignore:42:docs/INFORME*`.
- **`git ls-files server/src/Factum.Backend/Templates`** → `factum-sello.png`. La v4 todavía no está trackeada (ver arriba). La v3 y `sin-foto` figuran como borradas en el index.
- **Datos reales:** el `--check` (punto 2) busca en el XML crudo y en el texto de todas las partes de la v4 tres cosas, en memoria y sin imprimirlas:
  - el párrafo `7C035883` del origen;
  - el fragmento de domicilio de `74B641B2`;
  - el `dc:creator`/`cp:lastModifiedBy` del origen.

  Resultado: ninguna aparece. `core.xml` de la v4 tiene autor `Factum`.
- **Render de la v4 vacía con LibreOffice** (5 páginas), revisado a ojo:
  - membrete con `{LOGO…}`, `{ORGANIZACION}` y `{CONTACTO}`;
  - frases de DP2;
  - rótulos en negrita;
  - tabla 3600/4691;
  - firma;
  - anexo en una página nueva;
  - número de página del pie original intacto.

### Arnés descartable (B16, en el scratchpad, ya borrado)

Consola .NET con `ReportService` real, `ReportSettings` real y Branding falso. El directorio temporal tenía:
- 5 PNG (uno `imei_modelo`, uno `nombre_dispositivo` y el resto sin rol en a/c; todos `imei_modelo` en b);
- un `.mp4` falso;
- `device_pull_chat.txt` con `SourcePath`;
- una `foto_funcionario` de 3 bytes.

El texto de Resultados incluía una línea en blanco en el medio y el literal `{caratula}`, para probar que no se re-escanea.

| Comprobación | (a) completo + Branding + domicilio | (b) opcionales vacíos, sin Branding, sin anexo | (c) suscripta + sala + integrantes |
|---|---|---|---|
| Sin `{…}` sueltos en document/header/footer (fuera del literal del perito, que queda tal cual) | OK | OK | OK |
| Tabla con N + 1 filas de datos (N = 8) | OK (9) | OK | OK |
| `zipHash == sha256(zip)` / `reportHash == sha256(docx)` | OK / OK | OK / OK | OK / OK |
| El ZIP no contiene el DOCX; el hash del ZIP está en la tabla; filas "Origen:" y "Contenedor de la evidencia" | OK | OK | OK |
| Bloques: membrete, organismo, objeto del informe, nombre del dispositivo, notas, reserva, anexo | todos presentes; 3 figuras | todos ausentes; "No informado" en objeto, ámbito y línea; sin frase de domicilio | presentes; "La suscripta…"; "ante Sala II de Tribunal…, con la integración de…" |
| Rótulos en negrita y valores sin negrita (13 rótulos) | OK | OK | OK |
| `OpenXmlValidator` (Office2019) sobre `document.xml`, `header1.xml` y `footer1.xml` (sin el `mc:Fallback` VML que trae la plantilla de origen) | 0 errores | 0 errores | 0 errores |
| Render en PDF con LibreOffice | 7 páginas | 6 páginas | 8 páginas |

Revisé el render a ojo:
- membrete con logo, nombre y contacto en todas las páginas (en b no hay membrete);
- "Realizado con Factum" con el Sello, separado del borde gracias a `w:footer=567`;
- captura de identificación con su pie;
- firma;
- anexo con dos figuras por página, incluso en la primera página del anexo;
- no aparecieron warnings de "placeholder desconocido" ni de "sin reemplazar".

### Endpoints (B17)

Backend real en el puerto 18080, Mongo de dev `factum_dev` y `Storage__DataDirectory` apuntando a una carpeta del scratchpad, para no tocar `dev-data`. DNI de prueba `99000123`, que no tenía casos previos.

```
GET  /api/profile (sin perfil)            200 exists:false, nombre sugerido = user.Name, caracter "perito informático de parte"
PUT  /api/profile incompleto              400 {error, missing:[matricula, profesion, caracter]}
POST /api/cases sin perfil completo       400 {missing:[perfil]}
PUT  /api/profile                         200 is_complete:true, tratamiento "suscripta"
POST /api/cases incompleto                400 {missing:[caratula, fecha_intervencion, imei]}  (fecha "30/09/2026" inválida, IMEI "INGRESAR_MANUALMENTE")
POST /api/cases                           201 schema_version 1, perito copiado, device.name guardado
PUT  /api/cases/{id}                      200 sala actualizada, device.imei pisado por "imei"
POST /api/cases/{id}/files (1 PNG)        200
POST generate sin textos ni roles         400 {missing:[report_texts.operaciones_realizadas … report_texts.conclusiones, capture_roles.imei_modelo]}
PUT  capture-roles archivo inexistente    400 / rol inválido 400 / válido 200 {capture_roles:[{filename, role:"imei_modelo"}]}
GET  report-texts/defaults                200 (operaciones con fecha/hora local y conteos; reserva "La suscripta …")
PUT  report-texts                         200 (+updated_at) · texto de 20 001 caracteres → 400
GET  /api/cases                           report_texts: null en el listado · GET /{id}: report_texts presente
POST generate                             200 zip_hash + report_hash; las descargas coinciden con sha256; el ZIP solo trae la evidencia
PUT caso / PUT report-texts / generate sobre caso Completed → 409
GET caso de otro DNI 403 · GET id inexistente 404
Caso con forma vieja (insertado por la prueba, sin SchemaVersion): listado con schema_version 0, perito null, capture_roles [], device.name "" (deserializa sin errores) ·
  generate → 400 "Este caso se creó antes del informe pericial…" · PUT → schema_version 1, perfil copiado, observaciones conservada
```

**Limpieza:**
- Se borraron **solo por `_id`** los 2 casos que creó la prueba (el nuevo y el de forma vieja) y el perfil `99000123`. El conteo posterior dio 0 en los tres.
- Se borró la carpeta `cases/<id>` del único caso creado, que estaba en el storage del scratchpad.
- El login de un segundo DNI (`99000124`) no persiste nada: el modo `dev` no guarda usuarios.
- No se tocó ningún otro documento ni `Storage:DataDirectory`.
- La colección `expert_profiles` quedó creada (vacía) en `factum_dev`.

El backend arrancó y corrió sin warnings en el log, con la config local cargada (no se imprimió). Al terminar se detuvo el proceso y se borraron el arnés, la herramienta de Mongo y las salidas.

## Decisiones no obvias

1. **v4, negrita conservada.** En la v4 quedaron en negrita "Informe Pericial Técnico Informático" (P010) y "inspección técnica y documentación del contenido digital" (P022), como en la plantilla de origen. La tabla A3 da solo el texto; los placeholders van sin negrita.
2. **v4, rPr base.** Al rPr base de los párrafos reconstruidos se le sacan `w:lang en-US` y `w:hint` (texto en español), y la negrita del rótulo.
3. **v4, tabla con layout fijo.** La tabla de hashes pasa a `tblLayout fixed`. Con `autofit`, el hash de 64 caracteres sin espacios ensancharía la columna e ignoraría la grilla 3600/4691.
4. **`--check`, paraId del pie.** La unicidad de `w14:paraId` no cuenta los que están dentro de `mc:Fallback`. El `footer1.xml` de origen repite a propósito el paraId del número de página entre `mc:Choice` y `mc:Fallback`, y A2 dice "footer sin cambios".
5. **Generate de un caso `Completed` → 409.** No estaba explícito en §7.8. Se agregó para que un caso generado no se regenere nunca y su ZIP/DOCX/hash quede igual (regla de casos viejos).
6. **`DeviceInfoDto.Name` es `string?` en C#.** El JSON sigue siendo `name` con default `""`. Así no queda como obligatorio implícito y un cliente que no lo manda sigue funcionando (hallazgo 3).
7. **Perfil:**
   - El `PUT /api/profile` responde `missing` con `nombre`, `matricula`, `profesion` y `caracter` (§6.4 no define claves para este endpoint; usé los nombres JSON de §6.1).
   - Un `tratamiento` distinto de suscripto/suscripta → `400 {error}`.
8. **IMEI.** Se valida (`missing: imei`) también en `POST` y `PUT` del caso, con el IMEI efectivo (`device.imei` o el `imei` del PUT, y si no viene, el guardado). §6.4 lo lista sin restringirlo a `generate`.
9. **`PUT` caso y `Observaciones`.** Solo se pisa si el request la trae. El cliente nuevo no la manda, así que en los casos viejos se conserva.
10. **Escrituras con filtro "editable".** Las escrituras sobre `cases` (`UpdateCaseData`, `UpdateReportTexts`, `UpsertCaptureRoles`) filtran `Status ∉ {Generating, Completed}` en el propio update. Si no matchea, responden 409, aunque el estado haya cambiado entre la lectura y la escritura.
11. **Párrafos "resueltos" en `ReportService`.** Los textos del perito, las filas de la tabla y el `{CONTACTO}` se marcan como resueltos y B-R6 no los vuelve a escanear. Un `{caratula}` escrito por el usuario queda literal (verificado en el arnés).
12. **Imágenes con `keepNext`.** El párrafo de la imagen lleva `keepNext`, para que no se separe de su pie.
13. **Texto multilínea vacío.** Si el valor queda vacío después del recorte, el párrafo del placeholder se borra. Solo puede pasar con opcionales, que igual están dentro de un bloque.
14. **`Sanitize`.** Se implementó exactamente como dice B14: filtra, `Trim('_')`, recorta a 60 sobre el string ya filtrado y, si queda vacío, usa `"caso"`. Es `internal static`.
15. **Código muerto que se conserva.** `ConvertDocxToPdfAsync`, `LibreOfficePaths` e `IsOnPath` quedan (T16), con un comentario.
16. **Referencias viejas.** No queda ninguna referencia a `plantilla_informe_v3`, `{FOTO_FUNCIONARIO}`, `AppendToZipAsync` ni `sin-foto` en `server/src`, `README.md` ni `ops/` (grep vacío).

## Bloqueos

Ninguno. El sistema de permisos no rechazó ningún borrado (`git rm` de la v3 y de `sin-foto-placeholder.png` funcionó).

## Pendiente para el usuario / orquestador

- **Al commitear:**
  - `git add server/src/Factum.Backend/Templates/plantilla_informe_v4.docx ops/plantilla/` y los archivos nuevos del backend;
  - las bajas de la v3 y de `sin-foto` ya están en el index.
- **Revisar la v4 en Word o WPS** (D13/DP2): frases fijas, membrete, firma y anexo. LibreOffice la muestra bien, pero no probé Word.
- **Prueba de punta a punta con dispositivo real** (§14): no la hice. No hace falta para el backend, pero queda en la prueba manual.
- **Cargar el domicilio real** en `appsettings.Local.json` (`Report:DomicilioConstituido`) antes de la prueba manual.
