# SDD: Informe Pericial Técnico Informático (perito de parte)

**HU:** `docs/hu-informe-pericial-de-parte.md`. Validada el 2026-10-01 con **todas las opciones recomendadas (D1–D16)**. Manda la sección "Validación del usuario".
**Slug:** `informe-pericial-de-parte`
**Absorbe:** `B-PLANTILLA` y el anexo de layout que pedía `Refactorizaciones/marca-comercial-sin-mpf-gfd.md` §8.3 (acá, **Anexo B**), y la DP5 de esa SDD (terminología).
**Implementan:** `implementer-backend` (`server/src/Factum.Backend`, `ops/plantilla/`, `.gitignore`, README) e `implementer-frontend` (solo `client/`).
**Estado de la SDD:** completa. Hay **2 decisiones pendientes del usuario** (sección 11):
- **DP1** (textos por defecto) bloquea **solo** el ítem `B9b`. El mecanismo se implementa igual.
- **DP2** (frases fijas de la v4) no bloquea: se implementa con la redacción propuesta.

> **Regla de datos reales (dura, para todos):** ni esta SDD, ni el script de la plantilla, ni la v4, ni los `progress/*.md`, ni los ejemplos contienen el domicilio del estudio, el nombre del colegio profesional ni nombres de personas. La plantilla del usuario (`docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx`) trae dos datos reales escritos a mano, en los párrafos `7C035883` y `74B641B2` (Anexo A). El script los saca **por posición** (paraId), **sin escribirlos nunca**. `docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - 1.docx`, `- 2.docx` e `INFORME PUBLICO N° 62.pdf` **no se abren**.
>
> **Regla dura de AGENTS.md:** ningún paso modifica documentos de Mongo preexistentes ni archivos de `Storage:DataDirectory`. Un informe viejo sigue siendo el mismo archivo, con el mismo hash. Una prueba que escribe limpia **solo por los `_id` que ella misma insertó**.

---

## 1. Resumen funcional

Factum deja de generar el informe "de extracción" (v3) y pasa a generar el **Informe Pericial Técnico Informático** del perito de parte, a partir de `plantilla_informe_v4.docx`. La v4 se deriva de la plantilla del usuario con un script versionado que:
- canoniza los placeholders;
- neutraliza los textos atados a una causa o a un género;
- saca los datos reales;
- agrega el membrete del estudio (Branding), la firma del perito y un anexo de capturas.

Cada usuario tiene un **perfil de perito** persistente (colección `expert_profiles`, clave = DNI de login), que se **copia al caso** al crearlo o editarlo. El wizard pasa de 5 a 6 pasos:
- **Causa** (antes "Expediente"): pide los datos de la actuación, de las partes y del equipo.
- **Captura**: permite marcar capturas como "IMEI y modelo" o "Nombre del dispositivo".
- **Informe** (paso nuevo): los textos largos, con autoguardado en el servidor.
- **Generar**: lista los obligatorios que faltan y bloquea la generación.

El DOCX **sale del ZIP**. Así, el hash del ZIP que figura en el informe es el del archivo que se descarga, y `ResultStep` muestra también el hash del DOCX. La fecha y la hora de la inspección salen de `CreatedAt`, en hora local configurable. Los casos viejos generados no cambian; los borradores viejos se completan al retomarlos, por acción del usuario. La UI deja el vocabulario fiscal sin renombrar campos persistidos.

## 2. Toca

| Lado | ¿Toca? | Qué |
|---|---|---|
| backend (API) `server/src/Factum.Backend` | **sí** | Modelo `Case` (campos nuevos), colección nueva `expert_profiles`, endpoints de perfil, edición del caso, textos, roles de captura y defaults; `ReportService` reescrito para la v4 (Anexo B); flujo de hash D10; config `Report`; plantilla v4; se borra la v3 |
| backend (Tatana) `server/src/Factum.Agent` | **no** | El agente ya manda `name` y `operator`. Los nombres `foto_funcionario_*`/`foto_denunciante_*` y `captureWebcam("funcionario"\|"denunciante")` no cambian (D15) |
| client `client/` | **sí** | Wizard (pasos 2 a 6), perfil (tarjeta y diálogo), roles de captura, paso Informe, checklist de Generar, `ResultStep` con dos hashes, historial y tarjetas, terminología |
| agent-ui `agent-ui/` | **no** | — |
| otros | **sí** | `ops/plantilla/` (script nuevo), `.gitignore` (`docs/INFORME*`), `README.md` (config `Report`), `Dockerfile` (`tzdata`) |

---

## 3. Hallazgos de la verificación técnica (2026-10-01)

1. **El JSON viaja en `snake_case_lower`**, en `ConfigureHttpJsonOptions` y en `AddControllers().AddJsonOptions` (`Program.cs` L56-65), con enums como string `snake_case_lower` y sin `DefaultIgnoreCondition` (los `null` viajan como `null`). En Mongo los campos se guardan con el nombre **PascalCase** de la propiedad C# (no hay `BsonElement` ni convenciones registradas). Lo confirmé en la base `evidentia_dev`: `NroReferencia`, `NombreDenunciante`, … La base `factum_dev` tiene la colección `cases` **vacía**.
2. **`Case` no tiene `[BsonIgnoreExtraElements]`.** Agregar campos es compatible hacia atrás: a un documento viejo le faltan y quedan en su default. Pero si se hace rollback del backend, los documentos nuevos romperían la deserialización. Se agrega el atributo (T2).
3. **`[ApiController]` + `<Nullable>enable</Nullable>` hace obligatorio cualquier `string` no anulable de un DTO.** `CreateCaseRequest.Observaciones` (`string`) es **implícitamente requerido**. Si el cliente deja de mandarlo (D5 lo saca del wizard), el POST responde 400. Todos los opcionales nuevos van como `string?`.
4. **`[Required]` responde con un `ProblemDetails`** (`{ errors: {...} }`) que `request()` del cliente no lee (busca `error`). La validación de obligatorios pasa al servicio y responde `{ error, missing }` (T5).
5. **`GET /api/cases/{id}` responde `{ cas, files }`, pero `client/src/lib/api.ts#getCase` declara `{ case, files }`.** Hoy nadie lo usa. El paso Informe lo va a usar: se corrige el tipo del cliente a `cas` (el contrato del server no cambia).
6. **`ReplaceTextInBody` colapsa todos los runs del párrafo en el primero.** En la plantilla del usuario los rótulos están en negrita y el valor no (`**Carátula:** {caratula}`): con el algoritmo actual el valor saldría en negrita. Además reemplaza en secuencia (`Replace` por cada placeholder), así que un valor del usuario que contenga, por ejemplo, `{caratula}` se volvería a reemplazar. La v4 usa un reemplazo **por run y de una sola pasada** (Anexo B, B-R2).
7. **El hash informado no es el del ZIP descargado**, como dice la HU: `AppendToZipAsync` reescribe el ZIP después de hashearlo. Se resuelve con D10 A (sección 7.1).
8. **`Sanitize` puede tirar `ArgumentOutOfRangeException`:** `.Trim('_')[..Math.Min(s.Length, 60)]` corta con el largo **previo** al `Trim`. Con un titular que termina en punto o en espacio, y un total ≤ 60 caracteres, la generación explota. Se corrige (B14).
9. **El layout calibrado para la v3 ya está muerto en parte:**
   - `NormalizePageFlow` está comentado en `GenerateDocxAsync` (L131);
   - `MoveContentTableUp`, que cita la SDD anterior, **no existe** en el código;
   - `ConvertDocxToPdfAsync`, `LibreOfficePaths` e `IsOnPath` no se llaman desde ningún lado (el informe se entrega en DOCX).

   El Anexo B dice qué se borra y qué queda.
10. **La plantilla del usuario** (WPS Office, A4, márgenes `top=1809 right=1558 bottom=1417 left=1701`, Times New Roman):
    - no tiene header;
    - tiene un `footer1.xml` con el número de página en un grupo flotante anclado abajo a la derecha;
    - tiene **`w:footer="0"`**: la atribución "Realizado con Factum" quedaría pegada al borde del papel. Se corrige en la v4 (Anexo A).

    Los títulos de sección usan la numeración `numId=2` (romanos `I.`, `II.`, …). Si se borra un título, la numeración se recalcula sola. No hay dibujos ni `mc:AlternateContent` en el cuerpo. Todos los párrafos tienen `w14:paraId` único (78), y el script los usa como ancla.
11. **`docProps/core.xml` de la plantilla del usuario trae nombres de personas** (`dc:creator`, `cp:lastModifiedBy`). La v4 los reemplaza por `Factum` (Anexo A, A6). No los transcribo acá.
12. **`docs/INFORME*` está excluido solo en `.git/info/exclude`**, que no se versiona: otro clon podría commitearlos. Se suma a `.gitignore` (B11).
13. **Los archivos de evidencia viven en el agente hasta el final del paso 3.** `handleUploadAndContinue` los sube al pasar al paso siguiente. Por eso el rol de una captura que todavía no se subió se guarda en el estado local y se manda al servidor después de subirla (sección 8.4).
14. **Textos que esta HU vuelve falsos:**
    - `ResultStep` dice "Incluida en el informe Word" sobre la contraseña, pero la v4 no tiene `{CLAVE}`;
    - dice "Word oficial generado".

    Se corrigen acá. Las afirmaciones de cifrado ("ZIP cifrado con AES-256", "encriptado y con firma digital") son de `zip-cifrado-real` y **no se tocan**, salvo la frase "con firma digital" de `ResultStep`, que se reescribe junto con el título (8.7).

---

## 4. Modelo de datos y configuración

### 4.1 Colección nueva `expert_profiles` (perfil del perito, D1 A)

Modelo `server/src/Factum.Backend/Models/ExpertProfile.cs`, con `[BsonIgnoreExtraElements]`:

| Propiedad C# (= campo Mongo) | Tipo | Regla |
|---|---|---|
| `Id` (`[BsonId]`) | `string` | **DNI de login** (`User.Dni`). Un documento por usuario |
| `Nombre` | `string` | obligatorio, trim, 1..150 |
| `Matricula` | `string` | obligatorio, trim, 1..60, texto libre (letras y barras) |
| `Profesion` | `string` | obligatorio, trim, 1..150 |
| `Caracter` | `string` | obligatorio, trim, 1..150 |
| `Tratamiento` | `string` | `"suscripto"` (default) o `"suscripta"` (D4 B: "El suscripto"/"La suscripta") |
| `CreatedAt` / `UpdatedAt` | `DateTime` (UTC) | — |

- **No hace falta índice:** se busca por `_id`.
- **Escritura:** `ReplaceOneAsync(p => p.Id == dni, doc, new ReplaceOptions { IsUpsert = true })`. Solo toca el documento del propio usuario.
- **Valores iniciales sugeridos** (si no existe el documento; no se persisten hasta el primer `PUT`):
  - `Nombre = User.Name`;
  - `Caracter = "perito informático de parte"`;
  - `Tratamiento = "suscripto"`.

### 4.2 Campos nuevos en `cases`

`Models/Case.cs` suma `[BsonIgnoreExtraElements]` (T2) y estos campos. Todos los `string` tienen default `""` (no anulable), así un documento viejo deserializa sin errores.

| Propiedad C# | Tipo | Default | Uso / placeholder |
|---|---|---|---|
| `SchemaVersion` | `int` | `0` | `0` = caso previo a esta HU (falta el campo). `1` = caso pericial. Se pone en `1` en el `POST` y en el primer `PUT` de un borrador viejo |
| `Perito` | `PeritoSnapshot?` | `null` | Copia del perfil: `Nombre`, `Matricula`, `Profesion`, `Caracter`, `Tratamiento`. Se copia en `POST` y en `PUT` (T6) |
| `NombreTribunal` | `string` | `""` | `{nombreTribunal}` · obligatorio |
| `OrganismoTribunal` | `string` | `""` | `{organismoTribunal}` · opcional (bloque condicional) |
| `SalaTribunal` | `string` | `""` | parte de `{tramiteAnte}` · opcional |
| `IntegrantesTribunal` | `string` | `""` | parte de `{fraseIntegracion}` · opcional |
| `TipoCausa` | `string` | `""` | `{tipoCausa}` · obligatorio · **adjetivo del fuero** ("disciplinaria", "civil", "penal"; ver DP2) |
| `Caratula` | `string` | `""` | `{caratula}` · obligatorio |
| `ParteDenunciante` | `string` | `""` | `{parteDenunciante}` · obligatorio |
| `ParteDenunciada` | `string` | `""` | `{parteDenunciada}` · obligatorio |
| `ObjetoCausa` | `string` | `""` | `{objetoCausa}` · opcional → "No informado" |
| `AmbitoCausa` | `string` | `""` | `{ambitoCausa}` · opcional → "No informado" |
| `FechaIntervencion` | `string` | `""` | `"yyyy-MM-dd"` · obligatorio · en el informe sale `dd/MM/yyyy` |
| `NombreProponente` | `string` | `""` | parte de `{datosProponente}` · obligatorio |
| `ProfesionProponente` | `string` | `""` | parte de `{datosProponente}` · opcional |
| `MatriculaProponente` | `string` | `""` | parte de `{datosProponente}` · opcional |
| `TipoDispositivo` | `string` | `""` | `{tipoDispositivo}` · obligatorio (el cliente propone "Teléfono celular") |
| `LineaDispositivo` | `string` | `""` | `{lineaDispositivo}` · opcional → "No informado" |
| `ReportTexts` | `ReportTexts?` | `null` | Textos del paso Informe (ver abajo) |
| `CaptureRoles` | `List<CaptureRole>` | `[]` | `{ Filename, Role }`, con `Role` ∈ `"imei_modelo"`, `"nombre_dispositivo"` |
| `ReportHash` | `string?` | `null` | SHA-256 del DOCX generado (D10) |

`ReportTexts` (clase, con `[BsonIgnoreExtraElements]`):
- `ObjetoInforme`, `OperacionesRealizadas`, `AseguramientoEvidencia`, `Resultados`, `ValoracionTecnica`, `Conclusiones`, `NotasTecnicas`, `Reserva`: todos `string`, default `""`;
- `UpdatedAt` (`DateTime`).

`DeviceInfo` suma `Name` (`string`, default `""`): el `device.name` del agente, que hoy se pierde (referencia para la captura "Nombre del dispositivo").

**Campos existentes que cambian de semántica, no de nombre (D5, D15):**
- `NombreDenunciante` = **titular del dispositivo** (`{titularDispositivo}`), obligatorio.
- `DniDenunciante` = DNI del titular, **opcional**.
- `NroReferencia` = número de causa (`{numeroCausa}`), obligatorio.
- `Observaciones` ya no se pide. Se conserva y se muestra en casos viejos que lo tengan.

### 4.3 Compatibilidad con documentos existentes

- **No hay migración** (D12). Un documento sin los campos nuevos deserializa con los defaults de la tabla: `SchemaVersion = 0`, `Perito = null`, `ReportTexts = null`, `CaptureRoles = []`, `ReportHash = null`, `Device.Name = ""`.
- **Los updates usan `$set` campo por campo** (`Builders<Case>.Update.Set`), **nunca `ReplaceOne`** sobre `cases`. Así un `PUT` no reescribe campos que no maneja.
- **Las escrituras sobre un caso existente** solo ocurren por un request del dueño (`Officer.Dni`), sobre un caso en `Draft` o `Error`. Un caso `Completed` o `Generating` responde `409` y no se toca.
- **El listado (`GET /api/cases`) proyecta afuera `ReportTexts`**, para no inflar el historial: viaja `report_texts: null`. El paso Informe lo lee de `GET /api/cases/{id}`.

### 4.4 Config nueva `Report` (local, como `Branding`)

`Services/Reports/ReportOptions.cs`, registrada con `Configure<ReportOptions>(GetSection("Report"))`. En `appsettings.json` versionado:

```json
"Report": {
  "TimeZone": "America/Argentina/Buenos_Aires",
  "DomicilioConstituido": "",
  "DefaultTexts": {
    "OperacionesRealizadas": "",
    "AseguramientoEvidencia": "",
    "NotasTecnicas": "",
    "Reserva": ""
  }
}
```

| Clave | Uso |
|---|---|
| `Report:TimeZone` | Zona IANA (D11). Se resuelve **una vez al arrancar** con `TimeZoneInfo.FindSystemTimeZoneById`. Si falla, se loguea un warning y se usa un offset fijo `-03:00` (Argentina no tiene horario de verano desde 2009). |
| `Report:DomicilioConstituido` | `{fraseDomicilio}` (D3 A). Trim, máximo 300. Vacío → la frase se omite. **El valor real va solo en `appsettings.Local.json` o en `Report__DomicilioConstituido`.** |
| `Report:DefaultTexts:*` | Reemplazo **por estudio** de los textos por defecto (D7 A). Vacío → se usa el default neutro versionado en `Services/Reports/ReportDefaultTexts.cs` (DP1). Admiten los tokens de 7.6. |

El `Dockerfile` suma `tzdata` al `apt-get install` del stage `runtime`, para que la zona IANA exista en la imagen.

### 4.5 Clasificación de archivos (servidor)

Helper `Services/Reports/EvidenceClassifier.cs`. Repite el criterio que ya usan `CaptureStep.fileType`, `GenerateStep` y `EmbedImages`; las reglas se evalúan en este orden:

| Clase | Regla |
|---|---|
| `IdentityPhoto` | el nombre contiene `foto_funcionario` o `foto_denunciante` |
| `Screenshot` | el nombre contiene `screenshot` o `captura`, y la extensión es `.png`, `.jpg` o `.jpeg` |
| `Recording` | extensión `.mp4`, `.mkv`, `.mov`, `.avi` o `.webm` |
| `DevicePull` | tiene `FileSource` en el caso, o el nombre empieza con `device_pull_` |
| `Other` | el resto (adjuntos locales, audio, …) |

`ReportService.IsGeneratedArtifact` se aplica **antes** de clasificar.

---

## 5. Endpoints

Todos llevan `[Authorize]`, salvo que se diga otra cosa, y comprueban `cas.Officer.Dni == user.Dni` (si no, `403`). Los errores tienen la forma `{ "error": string }`; los de validación suman `"missing": string[]` con los nombres JSON de la sección 6.4.

| Método | Ruta | Entrada | Respuesta |
|---|---|---|---|
| `GET` | `/api/profile` | — | `200` `ExpertProfileResponse` (6.1). Si no existe: los valores sugeridos de 4.1, con `exists: false` |
| `PUT` | `/api/profile` | `ExpertProfileRequest` | `200` `ExpertProfileResponse` · `400 { error, missing }` |
| `GET` | `/api/cases` | — | **Sin cambios de forma** (`{ cases: Case[] }`). Cada caso trae los campos nuevos y `report_texts: null` (proyección, 4.3) |
| `GET` | `/api/cases/{id}` | — | **Sin cambios** (`{ cas: Case, files: FileInfoDto[] }`). Trae `report_texts` |
| `POST` | `/api/cases` | `CreateCaseRequest` (6.2) | `201` `Case` · `400 { error, missing }` (incluye `"perfil"` si el perfil no está completo) |
| `PUT` | `/api/cases/{id}` | `UpdateCaseRequest` (6.2) | `200` `Case` · `400 { error, missing }` · `409 { error }` si no es editable |
| `GET` | `/api/cases/{id}/report-texts/defaults` | — | `200` `ReportTextsDto`: defaults renderizados (7.6), `""` en los campos sin default |
| `PUT` | `/api/cases/{id}/report-texts` | `ReportTextsDto` | `200` `ReportTexts` (con `updated_at`) · `400` (largo) · `409` |
| `PUT` | `/api/cases/{id}/capture-roles` | `{ capture_roles: [{ filename, role \| null }] }` | `200 { capture_roles: CaptureRole[] }` · `400` (archivo inexistente o que no es captura, rol inválido) · `409` |
| `POST` | `/api/cases/{id}/files` | **sin cambios** | — |
| `POST` | `/api/cases/{id}/generate` | — | `200` `GenerateResponse` + **`report_hash`** · `400 { error, missing }` si faltan obligatorios (7.8) · `400 { error }` si `schema_version = 0` |
| `GET` | `/api/cases/{id}/download/{filename}` | **sin cambios** | Sirve el ZIP y el DOCX, que ahora son dos archivos independientes |

Detalles:
- **`PUT capture-roles` hace upsert por `filename`.** `role: null` borra la marca. Los archivos que no vienen en la lista **conservan** su marca, así no se pierden las de una sesión anterior si se retoma el caso. Validación:
  - el archivo existe en el directorio del caso;
  - `EvidenceClassifier` dice `Screenshot`;
  - el tamaño es mayor que 0.
- **`409`** se responde si `Status ∈ { Generating, Completed }`, con `"El caso ya fue generado y no se puede editar"`.
- **No hay mensajes de WebSocket nuevos** ni cambios en Tatana.

---

## 6. Contrato compartido (client ↔ server)

Casing JSON real: **`snake_case_lower`** (hallazgo 3.1). En la columna "C#" va el nombre de la propiedad; el JSON es el de la columna 1.

| Lado | Archivo |
|---|---|
| server | `server/src/Factum.Backend/Models/Case.cs` (`Case`, `PeritoSnapshot`, `ReportTexts`, `CaptureRole`, `FileSource`), `Models/DeviceInfo.cs`, `Models/ExpertProfile.cs`, `DTOs/CaseDtos.cs`, `DTOs/ProfileDtos.cs` (nuevo) |
| client | `client/src/lib/api.ts` (interfaces `Case`, `DeviceInput`, `PeritoSnapshot`, `ReportTexts`, `CaptureRole`, `ExpertProfile`; métodos del objeto `api`), re-exportadas en `client/src/types/index.ts` |

### 6.1 Perfil

`ExpertProfileRequest` (`PUT /api/profile`):

| JSON | C# | Tipo | Regla |
|---|---|---|---|
| `nombre` | `Nombre` | `string?` | obligatorio |
| `matricula` | `Matricula` | `string?` | obligatorio |
| `profesion` | `Profesion` | `string?` | obligatorio |
| `caracter` | `Caracter` | `string?` | obligatorio |
| `tratamiento` | `Tratamiento` | `string?` | `"suscripto"` \| `"suscripta"`; `null` → `"suscripto"` |

`ExpertProfileResponse`: `dni` (`string`), los cinco campos anteriores (`string`), `exists` (`bool`), `is_complete` (`bool`, los cuatro obligatorios no vacíos) y `updated_at` (`string` ISO \| `null`).

### 6.2 Caso

`Case` (respuesta). Se suman a lo actual (`id`, `nro_referencia`, `nombre_denunciante`, `dni_denunciante`, `observaciones`, `officer`, `device`, `status`, `created_at`, `generated_at`, `zip_password`, `zip_hash`, `zip_filename`, `pdf_filename`, `file_sources`):

| JSON | C# | Tipo TS |
|---|---|---|
| `schema_version` | `SchemaVersion` | `number` (`0` \| `1`) |
| `perito` | `Perito` | `PeritoSnapshot \| null` → `{ nombre, matricula, profesion, caracter, tratamiento }` (todos `string`) |
| `nombre_tribunal` | `NombreTribunal` | `string` |
| `organismo_tribunal` | `OrganismoTribunal` | `string` |
| `sala_tribunal` | `SalaTribunal` | `string` |
| `integrantes_tribunal` | `IntegrantesTribunal` | `string` |
| `tipo_causa` | `TipoCausa` | `string` |
| `caratula` | `Caratula` | `string` |
| `parte_denunciante` | `ParteDenunciante` | `string` |
| `parte_denunciada` | `ParteDenunciada` | `string` |
| `objeto_causa` | `ObjetoCausa` | `string` |
| `ambito_causa` | `AmbitoCausa` | `string` |
| `fecha_intervencion` | `FechaIntervencion` | `string` (`"yyyy-MM-dd"` o `""`) |
| `nombre_proponente` | `NombreProponente` | `string` |
| `profesion_proponente` | `ProfesionProponente` | `string` |
| `matricula_proponente` | `MatriculaProponente` | `string` |
| `tipo_dispositivo` | `TipoDispositivo` | `string` |
| `linea_dispositivo` | `LineaDispositivo` | `string` |
| `report_texts` | `ReportTexts` | `ReportTexts \| null` |
| `capture_roles` | `CaptureRoles` | `CaptureRole[]` → `{ filename: string; role: "imei_modelo" \| "nombre_dispositivo" }` |
| `report_hash` | `ReportHash` | `string \| null` |
| `device.name` | `DeviceInfo.Name` | `string` |
| `file_sources` | `FileSources` | `{ filename: string; source_path: string }[]` (ya viajaba, el TS no lo declaraba; se suma como opcional) |

`ReportTexts` (`ReportTextsDto` en el `PUT`, la clase `ReportTexts` en la respuesta), todos `string`:
- `objeto_informe`;
- `operaciones_realizadas`;
- `aseguramiento_evidencia`;
- `resultados`;
- `valoracion_tecnica`;
- `conclusiones`;
- `notas_tecnicas`;
- `reserva`.

La respuesta suma `updated_at`. En el DTO de entrada son `string?` (`null` → `""`), con un máximo de **20 000** caracteres cada uno.

**`CreateCaseRequest`** (`POST /api/cases`). Todos los campos son `string?` salvo `device`, y se validan en el servicio (T5):
- `nro_referencia`, `nombre_denunciante`, `dni_denunciante`, `observaciones` (**pasa a `string?`**; el cliente nuevo no lo manda);
- `nombre_tribunal`, `organismo_tribunal`, `sala_tribunal`, `integrantes_tribunal`, `tipo_causa`, `caratula`;
- `parte_denunciante`, `parte_denunciada`, `objeto_causa`, `ambito_causa`, `fecha_intervencion`;
- `nombre_proponente`, `profesion_proponente`, `matricula_proponente`;
- `tipo_dispositivo`, `linea_dispositivo`;
- `device: DeviceInfoDto`, que suma `name` (`string`, default `""`).

**Sin `[Required]`.**

**`UpdateCaseRequest`** (`PUT /api/cases/{id}`): los mismos campos **sin `device`**, más `imei` (`string?`). Si viene no vacío, actualiza `Device.Imei` (sirve para el IMEI manual).

**`GenerateResponse`**: `case`, `zip_hash`, `password`, `files: { zip, pdf }` (sin cambios) **+ `report_hash`** (`string`). `files.pdf` sigue siendo el nombre del DOCX (el campo no se renombra).

### 6.3 Roles de captura

`PUT /api/cases/{id}/capture-roles`:
- **Request:** `{ "capture_roles": [ { "filename": "screenshot_20261001_101500.png", "role": "imei_modelo" } ] }`. `role` ∈ `"imei_modelo"`, `"nombre_dispositivo"`, `null`.
- **Response:** `{ "capture_roles": [...] }`, con el estado completo después del upsert.

### 6.4 Claves de `missing`

Se usan en el `400` del servidor y en el checklist del cliente: `client/src/lib/pericial.ts` ↔ `server/src/Factum.Backend/Services/Cases/CaseValidation.cs`. Tienen que ser **idénticas**.

| Clave | Cuándo |
|---|---|
| `perfil` | el perfil del usuario no está completo (`POST`/`PUT` caso) |
| `perito` | el caso no tiene copia del perfil completa (`generate`) |
| `nombre_tribunal`, `tipo_causa`, `nro_referencia`, `caratula`, `parte_denunciante`, `parte_denunciada`, `fecha_intervencion`, `nombre_proponente`, `nombre_denunciante`, `tipo_dispositivo` | vacío (o, en `fecha_intervencion`, una fecha inválida) |
| `imei` | `device.imei` vacío o `"INGRESAR_MANUALMENTE"` |
| `report_texts.operaciones_realizadas`, `report_texts.aseguramiento_evidencia`, `report_texts.resultados`, `report_texts.valoracion_tecnica`, `report_texts.conclusiones` | vacío o solo espacios (`generate`) |
| `capture_roles.imei_modelo` | no hay ninguna captura marcada `imei_modelo` que exista y no esté vacía (`generate`, D8) |

**Largos máximos** (el exceso es `400` con `error`, no `missing`):
- 300 en los campos de una línea;
- 500 en `caratula` e `integrantes_tribunal`;
- 20 000 en los textos.

---

## 7. Generación del informe

### 7.1 Flujo D10 (hash verificable)

`ReportService.GenerateAsync`, en este orden:

1. Limpiar restos de intentos previos: `evidencia_*.zip`, `informe_forense_*` e `informe_pericial_*`, con los nombres calculados del caso.
2. `files` = evidencia (sin artefactos), ordenada por **nombre ordinal**. El orden es determinista y vale para la tabla y el anexo.
3. Calcular el SHA-256 de cada archivo.
4. Crear el ZIP **solo con la evidencia** → `zipHash = SHA-256(zip)`. **El ZIP no se vuelve a abrir.**
5. Generar el DOCX con `zipHash` en la tabla.
6. `reportHash = SHA-256(docx)`, después de cerrar el documento.
7. Borrar los archivos sueltos de la evidencia (igual que hoy).
8. Devolver `ReportResult(..., ZipHash, ReportHash)`. `CaseRepository.UpdateGeneratedAsync` suma `ReportHash`.

**Se borra `AppendToZipAsync`**: el DOCX ya no va dentro del ZIP. El hash del ZIP que figura en el informe es el del ZIP que se descarga. El DOCX no puede contener su propio hash: lo muestran `ResultStep` y `CaseCard`.

### 7.2 Valores de los placeholders

Todos los valores se sanitizan:
- `\r\n` → `\n`;
- se eliminan los caracteres de control `< 0x20`, salvo `\n`;
- `\t` → espacio.

| Placeholder | Valor |
|---|---|
| `{nombreTribunal}` | `NombreTribunal` |
| `{organismoTribunal}` | `OrganismoTribunal` (dentro de su bloque, 7.3) |
| `{nombrePerito}`, `{matriculaPerito}`, `{profesionPerito}`, `{caracterPerito}` | `Perito.*` (la copia del caso, nunca el perfil vivo) |
| `{elSuscripto}` | `"La suscripta"` si `Perito.Tratamiento == "suscripta"`; si no, `"El suscripto"` |
| `{fraseDomicilio}` | `", con domicilio constituido en " + DomicilioConstituido`, o `""` si no está configurado |
| `{tipoCausa}` | `TipoCausa` |
| `{numeroCausa}` | `NroReferencia` |
| `{parteDenunciante}`, `{parteDenunciada}`, `{caratula}` | campo homónimo |
| `{tramiteAnte}` | si hay sala: `SalaTribunal + " de " + NombreTribunal`; si no, `NombreTribunal` |
| `{fraseIntegracion}` | `", con la integración de " + IntegrantesTribunal`, o `""` |
| `{objetoCausa}`, `{ambitoCausa}`, `{lineaDispositivo}` | el campo, o `"No informado"` si está vacío (D6) |
| `{fechaIntervencion}` | `FechaIntervencion` en formato `dd/MM/yyyy` |
| `{datosProponente}` | `NombreProponente`, más `" – " + ProfesionProponente` si existe, más `", M.P. " + MatriculaProponente` si existe |
| `{fechaInspeccion}` / `{horaInspeccion}` | `CreatedAt` convertido a `Report:TimeZone` → `dd/MM/yyyy` / `HH:mm` (D11 A) |
| `{tipoDispositivo}`, `{titularDispositivo}` | `TipoDispositivo`, `NombreDenunciante` |
| `{marcaModeloDispositivo}` | `Model` si empieza con `Manufacturer` (sin distinguir mayúsculas); si no, `Manufacturer + " " + Model` |
| `{imeiDispositivo}` | `Device.Imei` |
| `{ORGANIZACION}`, `{CONTACTO}`, `{CONTACTO_EN_LINEA}`, `{LOGO_ORGANIZACION:…}` | Branding, **sin cambios** de la SDD anterior §8.1 |
| `{objetoInforme}`, `{descripcion*}` | `ReportTexts.*`: **multilínea**, 7.4 |
| `{nombreArchivo}` / `{hashArchivo}` | fila modelo de la tabla, 7.5 |
| `{capturasImeiModelo}`, `{capturasNombreDispositivo}`, `{anexoCapturas}` | imágenes, 7.7 |

### 7.3 Bloques condicionales

Sintaxis: un párrafo que contiene **solo** `{#clave}` abre el bloque y otro que contiene **solo** `{/clave}` lo cierra. Los dos están en el mismo contenedor (body o header) y al mismo nivel. Puede haber tablas entre medio.

- **Condición verdadera:** se borran los dos párrafos marcadores y queda el contenido.
- **Condición falsa:** se borra todo, del marcador de apertura al de cierre.

| Clave | Verdadera si |
|---|---|
| `MEMBRETE` | hay `OrganizationName`, logo o al menos una `ContactLine` |
| `organismoTribunal` | `OrganismoTribunal` no vacío |
| `objetoInforme` | `ReportTexts.ObjetoInforme` no vacío |
| `capturasNombreDispositivo` | ≥ 1 captura `nombre_dispositivo` usable |
| `descripcionNotasTecnicas` | `NotasTecnicas` no vacío |
| `descripcionReserva` | `Reserva` no vacío |
| `anexoCapturas` | ≥ 1 captura para el anexo (7.7) |

Si un bloque abre y no cierra (o al revés), se loguea un warning y se borran solo los marcadores.

### 7.4 Texto multilínea

Un párrafo que contiene solo `{objetoInforme}` o `{descripcion…}` se reemplaza por **un párrafo por línea** del valor:
- `Split('\n')`, con `TrimEnd` de cada línea;
- se descartan las líneas vacías al principio y al final;
- las vacías del medio se conservan como párrafos vacíos.

Cada párrafo nuevo clona el `pPr` del párrafo del placeholder y el `rPr` de su run. Los valores **no se vuelven a escanear** en busca de placeholders.

### 7.5 Tabla de hashes

- La fila modelo es la `w:tr` que contiene `{nombreArchivo}`. Por cada archivo (orden 7.1-2) se clona y se completa:
  - **celda 1:** el nombre del archivo. Si tiene `SourcePath`, se agrega un segundo párrafo en la misma celda: `Origen: <ruta>`, en cursiva, 7 pt (`sz=14`), color `595959`;
  - **celda 2:** el hash.
- Después va **una fila más** para el contenedor: celda 1 = `<zipFilename>`, más un segundo párrafo `Contenedor de la evidencia` con el mismo estilo; celda 2 = `zipHash`.
- Al final se borra la fila modelo.
- Formato de la fila (lo trae la v4, Anexo A): el nombre en 9 pt y el hash en Courier New 8 pt. La fila de encabezado se repite en cada página y las filas no se parten.

### 7.6 Textos por defecto y borrador de operaciones (D7)

`GET /api/cases/{id}/report-texts/defaults` arma:

| Campo | Default |
|---|---|
| `operaciones_realizadas` | plantilla `OperacionesRealizadas` renderizada |
| `aseguramiento_evidencia` | `AseguramientoEvidencia` |
| `notas_tecnicas` | `NotasTecnicas` |
| `reserva` | `Reserva` |
| `objeto_informe`, `resultados`, `valoracion_tecnica`, `conclusiones` | `""` |

Cada default sale de `Report:DefaultTexts:*` si no está vacío; si no, de `ReportDefaultTexts.cs` (texto de DP1).

**Tokens del renderer.** Una sola pasada, regex `\{([A-Za-z]+)\}`. Un token desconocido **se deja tal cual**, para que el perito lo vea y lo corrija.

| Token | Valor |
|---|---|
| `{fechaInspeccion}`, `{horaInspeccion}`, `{tipoDispositivo}`, `{marcaModeloDispositivo}`, `{imeiDispositivo}`, `{elSuscripto}` | igual que en 7.2 |
| `{sistemaOperativo}` | `Device.OsLabel` |
| `{zonaHoraria}` | p. ej. `America/Argentina/Buenos_Aires (UTC-03:00)` |
| `{cantidadCapturas}` | archivos `Screenshot` |
| `{cantidadGrabaciones}` | archivos `Recording` |
| `{cantidadArchivosExtraidos}` | archivos `DevicePull` |
| `{cantidadOtros}` | `IdentityPhoto` + `Other` |

Los conteos salen del directorio del caso, sin artefactos.

**Regla de las listas:** en `OperacionesRealizadas`, se omite cada línea que empieza con `"- "` y tiene un token `{cantidad…}` que vale `0`.

El texto se renderiza **una vez**. El cliente lo guarda en `report_texts`, y a partir de ahí es texto del perito.

### 7.7 Imágenes

Se usa `BuildImageParagraph` (fit, sin recortar), y después de cada imagen va un párrafo de pie centrado, en cursiva, 8 pt.

| Placeholder | Qué imágenes | Caja (EMU) | Pie |
|---|---|---|---|
| `{capturasImeiModelo}` | capturas con rol `imei_modelo`, usables (`IsUsableImage`), en orden de nombre | 7 × 10.5 cm (`2_520_000 × 3_780_000`) | `Captura de identificación (IMEI y modelo) – <archivo>` |
| `{capturasNombreDispositivo}` | rol `nombre_dispositivo` | ídem | `Captura de identificación (nombre del dispositivo) – <archivo>` |
| `{anexoCapturas}` | **todas las `Screenshot` sin rol** | 14 × 10.5 cm (`5_040_000 × 3_780_000`, dos por página) | `Figura N – <archivo>` (N desde 1) |

Las fotos de identidad (`IdentityPhoto`) **no van al informe** (D9 B): quedan en el ZIP y en la tabla de hashes. Si un placeholder de imagen no tiene imágenes, se borra su párrafo. Con los bloques de 7.3, eso solo puede pasar con `{capturasImeiModelo}`, y 7.8 lo impide.

### 7.8 Validación previa a generar

`CaseService.GenerateAsync`, antes de cambiar el estado a `Generating`:
- **`SchemaVersion == 0`** → `400 { error: "Este caso se creó antes del informe pericial. Completá los datos de la causa para generarlo." }`.
- **Faltan obligatorios** (6.4) → `400 { error: "Faltan datos obligatorios", missing: [...] }`.
- **Sin archivos** → igual que hoy.

### 7.9 Nombres de archivo

| Archivo | Nombre |
|---|---|
| DOCX | `informe_pericial_<safe>.docx` |
| ZIP | `evidencia_<safe>.zip` (sin cambios) |

`<safe>` = `Sanitize($"{NroReferencia}_{NombreDenunciante}")`, corregido (B14).

`IsGeneratedArtifact` reconoce `evidencia_*.zip`, `informe_forense_*.{docx,pdf}` (viejo) e `informe_pericial_*.{docx,pdf}`.

---

## 8. Wizard (`client/`, D14 A: solo campos, paso 4 y terminología)

Sin rediseño visual. Se reusan `FormField`, `.input`, `.card`, `StepIndicator`, `ConfirmDialog` y, para el diálogo de perfil, `Dialog` de PrimeReact con el `pt` existente (`client/src/lib/prime/pt/dialog.ts`). Antes de tocar código hay que leer `client/AGENTS.md`.

### 8.1 Pasos (`STEPS` en `client/src/app/dashboard/page.tsx`)

| id | label | sublabel |
|---|---|---|
| 1 | Dispositivo | Seleccionando dispositivo |
| 2 | **Causa** | Datos de la causa |
| 3 | **Captura** (hoy dice "Factum") | Capturando |
| 4 | **Informe** | Redactando |
| 5 | Generar | Creando informe |
| 6 | Listo | Resumiendo |

Ajustes en `page.tsx`:
- `attemptExitWizard` confirma en los pasos 2 a 5;
- `minJumpable` = `result ? 6 : currentCase ? 2 : 1`. El paso 2 con caso creado entra **en modo edición** (8.2);
- `GenerateStep` pasa al paso 5 y `ResultStep` al 6;
- los checks `stepRef.current === 3`/`!== 2` no cambian.

### 8.2 Paso 2 "Causa" (`CaseFormStep`)

**Estado.** `CaseFormData` (`client/src/types/index.ts`) pasa a usar **las claves JSON** de 6.2: `nro_referencia`, `nombre_denunciante`, `dni_denunciante`, `nombre_tribunal`, …, `linea_dispositivo`, más `imeiOverride`. Los errores usan las mismas claves (las de 6.4), así el mapeo con el `missing` del servidor es directo.

**Columna derecha**, con secciones plegables y en este orden:
- **Tus datos de perito.** `ExpertProfileCard`: los cuatro campos más el select de tratamiento. Desplegada si `!is_complete`; si el perfil está completo, plegada con "Nombre · M.P. matrícula" y un botón "Editar".
- **Actuación:**
  - tribunal (obligatorio);
  - organismo;
  - sala (hint "Ej.: Sala II");
  - integrantes (hint "Ej.: Dres. Nombre Apellido y Nombre Apellido");
  - tipo de causa (obligatorio; hint "Ej.: disciplinaria, civil, penal");
  - número de causa / expediente (obligatorio);
  - carátula (obligatoria);
  - objeto de la causa;
  - ámbito;
  - fecha de intervención (`type="date"`, obligatoria).
- **Partes:** parte denunciante (obligatoria), parte denunciada (obligatoria; hint "Incluí el artículo si corresponde: «el Sr. …», «las Dras. …»"), proponente (nombre obligatorio, profesión, matrícula).
- **Equipo:**
  - titular del dispositivo (obligatorio);
  - DNI del titular (opcional);
  - línea;
  - tipo de dispositivo (obligatorio);
  - el IMEI, igual que hoy (detectado o manual).

**Se quita "Observaciones"** del formulario (D5).

**Indicador `n/m`:** cuenta los obligatorios de todo el paso. Son 10 de la causa y el equipo, más 4 del perfil si la tarjeta está visible por estar incompleta, más el IMEI si es manual.

**Precarga** (solo al crear, no al editar):
- tribunal, organismo, sala, integrantes, tipo de causa y los tres datos del proponente salen del **primer caso de `historyCases` con `schema_version >= 1`** (el listado ya viene ordenado por `created_at` descendente);
- `tipo_dispositivo = "Teléfono celular"`;
- `fecha_intervencion` = hoy (fecha local, `yyyy-MM-dd`);
- `linea_dispositivo` = `selDevice.operator` **solo si** `platform === "ios"` y el valor matchea `/^\+?[\d\s()-]{6,}$/`.

**Validación** (helper `validateCaseForm` en `client/src/lib/pericial.ts`). Mensajes "Ingresá …":
- perfil: tu nombre completo, tu matrícula, tu profesión, tu carácter;
- causa: el tribunal, el tipo de causa, el número de causa, la carátula, la parte denunciante, la parte denunciada, la fecha de intervención, el nombre de quien propone;
- equipo: el titular del dispositivo, el tipo de dispositivo, el IMEI.

El foco va al primer error en el orden visual. Si el error está en el perfil plegado, la tarjeta se despliega.

**Submit:**
1. Si la tarjeta del perfil cambió o `!is_complete` → `PUT /api/profile`.
2. Crear (`POST /api/cases`) o, en modo edición, `PUT /api/cases/{id}`.
3. Si responde `400` con `missing` → se mapea a `errors`. Si es otro error → `globalError`, **sin perder** lo cargado.
4. `setCase(res)` y `go(3)`.

**Modo edición:** el botón principal dice "Guardar y continuar". "Atrás" se reemplaza por "Cancelar" (vuelve al 3), salvo que `schema_version === 0`, donde no hay cancelar.

### 8.3 Perfil fuera del wizard

- **Menú:** `UserMenu` suma el ítem "Mi perfil de perito" (antes de "Cerrar sesión"). Abre `ExpertProfileDialog` (PrimeReact `Dialog`) con los cinco campos, "Guardar" y "Cancelar".
- **Dónde se monta:** el diálogo vive en `AppNavbar` (o en el padre que ya renderiza `UserMenu`).
- **Lógica:** hook `useExpertProfile()` (`client/src/hooks/useExpertProfile.ts`) con `profile`, `loading`, `save(req)` y `reload()`. Lo usan la tarjeta del paso 2 y el diálogo.

### 8.4 Paso 3 "Captura" (`CaptureStep`)

**Roles de captura:**
- **Menú "Marcar como…"** en cada ítem de la galería con `kind === "screenshot"` (no en adjuntos locales ni videos). Es un `Menu` popup de PrimeReact, operable con teclado, con tres opciones: "IMEI y modelo", "Nombre del dispositivo" y "Sin marca". Cada captura tiene **una sola** etiqueta, visible como badge con texto sobre la miniatura.
- **Estado local:** `CapturedFile` suma `captureRole?: "imei_modelo" | "nombre_dispositivo"` y `useFileManager` suma `setCaptureRole(name, role | null)`.
- **Persistencia:**
  - archivo con `uploaded: false` → solo estado local;
  - archivo con `uploaded: true` → `PUT capture-roles` inmediato con esa entrada;
  - en `handleUploadAndContinue`, **después** de subir todo → un `PUT capture-roles` con **todas** las `screenshot` locales y su rol (o `null`).

  La respuesta actualiza `currentCase.capture_roles` vía un callback nuevo, `onCaseUpdated(cas)`.
- **Chips sobre la galería**, con texto y no solo color. Cuentan la unión de las marcas locales y `currentCase.capture_roles`:
  - "IMEI y modelo: N captura(s)" / "IMEI y modelo: falta";
  - "Nombre del dispositivo: N" / "Nombre del dispositivo: sin marcar (opcional)".
- **Ayuda:** "Marcá *#06# o abrí Ajustes › Acerca del teléfono y capturá la pantalla."

**Identificación (D9 B):**
- título "Identificación · opcional";
- texto "Fotos opcionales del perito y del titular. Se guardan en el ZIP con su hash; no se incluyen en el informe.";
- `IdentityCard`: `label="Perito" role="Quien realiza la inspección"` y `label="Titular del dispositivo" role="Titular"`;
- el modal de webcam dice "Foto del perito" / "Foto del titular";
- los props `onPhotoFuncionario`, `denuncianteNombre`, … se pueden renombrar internamente (no son contrato). Los **nombres de archivo y el tipo del agente no cambian**.

### 8.5 Paso 4 "Informe" (componente nuevo `client/src/components/ReportStep.tsx`)

**Carga:**
- Al montar → `api.getCase(id)` (tipo corregido a `{ cas, files }`).
- Si `cas.report_texts` es `null` → `GET …/report-texts/defaults` → se completa el estado → `PUT` inmediato.

**Textareas.** Un `textarea` autoajustable por sección, con `label` asociado por `id`, en este orden. Los marcados con "(default)" tienen "Restaurar texto por defecto":
1. Objeto del informe · opcional
2. Operaciones realizadas (default)
3. Aseguramiento de la evidencia (default)
4. Resultados
5. Valoración técnica
6. Conclusiones
7. Notas técnicas · opcional (default)
8. Reserva · opcional (default)

**Autoguardado:**
- `PUT …/report-texts` con todo el objeto, 1200 ms después de la última tecla, y también en el `blur`.
- Indicador con `aria-live="polite"`: "Guardando…", "Guardado hace un momento", "No se pudo guardar · Reintentar" (botón).
- "Continuar" queda deshabilitado mientras guarda o hay error.
- Al guardar → `onSaved(report_texts)` actualiza `currentCase.report_texts` en `page.tsx`.

**"Restaurar texto por defecto":** vuelve a pedir los defaults (cacheados mientras el paso está montado) y reemplaza ese campo. Si el texto actual no está vacío y es distinto del default, pide confirmación con `ConfirmDialog`.

**Botones:** "Atrás" (paso 3) y "Continuar" (paso 5).

### 8.6 Paso 5 "Generar" (`GenerateStep`)

**Checklist de obligatorios:**
- `getMissingRequirements(currentCase)` en `client/src/lib/pericial.ts` usa las claves de 6.4 y lee:
  - `currentCase.perito`;
  - los campos del caso;
  - `currentCase.report_texts`;
  - `currentCase.capture_roles`.
- Cada faltante es un **enlace real** (`<button>` con el texto del campo) que hace `go(paso)` y enfoca el campo:
  - **mecanismo:** un estado `focusFieldId` en `page.tsx`, que el paso destino consume al montar;
  - **destino:** perfil y causa → paso 2 (perfil: `perito` y `perfil`); textos → 4; captura IMEI → 3.
- "Generar informe" está deshabilitado mientras falte algo.
- Si el servidor igual responde `400` con `missing`, se muestra la misma lista.

**Otros cambios:**
- el resumen muestra la **carátula** (y el titular debajo, en gris) en lugar del denunciante;
- "Observaciones" solo aparece si existe (casos viejos);
- `IdentityConfirm`: "Perito" / "Titular del dispositivo", con la etiqueta "opcional";
- botón "Generar informe pericial" y texto de estado "Generando informe pericial, esperá…";
- "el expediente queda cerrado para edición" → "el caso queda cerrado para edición".

### 8.7 Paso 6 "Listo" (`ResultStep`)

**Props nuevos:** `reportHash`. En `page.tsx`, `result` suma `reportHash: res.report_hash`.

**Textos:**
- título "¡Informe pericial generado!";
- subtítulo "Descargá el ZIP de evidencia y el informe del caso **{nro}**." Se quita "encriptado y con firma digital".

**Hashes:**
- **"Hash SHA-256 del ZIP de evidencia"** (`hash`), con la nota "Es el que figura en el informe."
- **"Hash SHA-256 del informe (DOCX)"** (`reportHash`), con la nota "El informe no puede contener su propio hash: guardalo junto con la entrega."

**Contraseña del ZIP:** el bloque queda (fuera de alcance), pero **se quita** "Incluida en el informe Word".

**Checks:** "Hash SHA-256 por archivo", "Hash del ZIP verificable", "Informe pericial generado".

**Descargas:**
- "Descargar ZIP" con el subtítulo "Evidencia".
- "Informe Word" con el subtítulo "Informe pericial (.docx)".

### 8.8 Historial y tarjetas

**`CaseHistory`:**
- La columna "Expediente" pasa a "N° de causa".
- La columna "Denunciante" pasa a **"Carátula"**: muestra `caratula` y debajo el titular en gris. Si no hay carátula (caso viejo), muestra `nombre_denunciante` como hoy. La clave de orden es `caratula`, con fallback a `nombre_denunciante`.
- La búsqueda incluye `nro_referencia`, `caratula`, `parte_denunciante`, `parte_denunciada`, `nombre_denunciante`, `dni_denunciante`, marca, modelo e IMEI. **Se quitan** `officer.name` y `officer.sigla`.
- Placeholders:
  - fijo: "Buscar por carátula, titular o IMEI…";
  - rotativos: "Buscar por número de causa…", "Buscar por carátula o partes…", "Buscar por titular o DNI…", "Buscar por equipo o IMEI…".

**`CaseCard` y `CaseGridCard`:**
- **Subtítulo:** `caratula || nombre_denunciante`. Si hay DNI, suma " · DNI x"; si está vacío, no se muestra.
- **"Fiscal"** pasa a **"Perito"**, con valor `cas.perito?.nombre ?? cas.officer.name`.
- **"DNI Fiscal"** se quita. "Unidad" (`officer.sigla`) **queda**: es de `auth-e-integraciones-sin-mpf`.
- **Filas nuevas:** "Titular" (siempre) y "Carátula" (si existe). "Observaciones" solo si existe.
- **"Paquete forense"** pasa a "Paquete del informe".
- **Hashes de un caso completado:**
  - si `schema_version >= 1`: "Hash SHA-256 del ZIP" y "Hash SHA-256 del informe" (`report_hash`);
  - si `schema_version === 0`: "Hash SHA-256 de la evidencia (sin el informe)". Es el valor que guardó el informe viejo, que se calculó antes de incorporar el DOCX.
- El `aria-label` "Expediente …" pasa a "Causa …".

### 8.9 Casos viejos (D12)

**Generados:** no cambia nada en datos ni archivos. La UI los muestra con la terminología nueva (8.8) y se siguen descargando igual.

**Borradores con `schema_version === 0`:**
- `proceedResume` lleva al **paso 2 en modo edición** (no al 3), con un aviso: "Este caso se creó antes del informe pericial. Completá los datos de la causa para continuar."
- El formulario se precarga con `nro_referencia`, `nombre_denunciante` y `dni_denunciante` del caso, más la precarga de 8.2.
- Al guardar (`PUT`), el servidor pone `schema_version = 1` y copia el perfil. Es una escritura **por acción del usuario** sobre su propio caso, no una migración.

**Borradores con `schema_version >= 1`:** se retoman en el paso 3, como hoy.

---

## 9. Terminología (D15: solo etiquetas visibles)

**No se renombran:**
- los campos persistidos ni el JSON (`nombre_denunciante`, `dni_denunciante`, `officer`, `nro_referencia`, `pdf_filename`);
- los nombres de archivo `foto_funcionario_*`/`foto_denunciante_*`;
- el contrato con Tatana (`captureWebcam("funcionario"|"denunciante")`);
- los comentarios de código (opcional).

| Hoy | Nuevo | Archivo |
|---|---|---|
| "Expediente" (paso 2), "Datos del expediente" | "Causa", "Datos de la causa" | `dashboard/page.tsx` (`STEPS`), `CaseFormStep.tsx` |
| "Factum" (paso 3) | "Captura" | `dashboard/page.tsx` |
| "Número de expediente" / "Ingresá el número de expediente" | "Número de causa / expediente" / "Ingresá el número de causa" | `CaseFormStep.tsx`, `lib/pericial.ts` |
| "Nombre del denunciante" / "DNI del denunciante" | "Titular del dispositivo" / "DNI del titular · opcional" | `CaseFormStep.tsx` |
| "Fiscal", "DNI Fiscal" | "Perito", (se quita) | `CaseCard.tsx` |
| "Fiscal · Funcionario", "Denunciante · Titular" | "Perito · Quien realiza la inspección", "Titular del dispositivo · Titular" | `CaptureStep.tsx`, `GenerateStep.tsx` |
| "Foto del fiscal" / "Foto del denunciante" | "Foto del perito" / "Foto del titular" | `CaptureStep.tsx` |
| "…quién realiza la extracción" | texto de 8.4 ("Fotos opcionales del perito y del titular…") | `CaptureStep.tsx` |
| "Denunciante" (columna), "Buscar por fiscal o sigla…" y los demás placeholders | 8.8 | `CaseHistory.tsx` |
| "{nombre} · DNI {dni}" | carátula o titular (8.8) | `CaseCard.tsx`, `CaseGridCard.tsx` |
| "Generar informe forense", "Generando informe forense…" | "Generar informe pericial", "Generando informe pericial…" | `GenerateStep.tsx` |
| "el expediente queda cerrado para edición" (×2) | "el caso queda cerrado para edición" | `GenerateStep.tsx` |
| "¡Expediente completado!", "Expediente encriptado", "Word oficial generado", "Expediente cifrado" | 8.7 | `ResultStep.tsx` |
| "Paquete forense" | "Paquete del informe" | `CaseCard.tsx` |
| "El fiscal activa la grabación…" | "El perito activa la grabación…" | `IOSModePicker.tsx` |
| "…del celular del denunciante" / "En el celular del denunciante" | "…del celular a inspeccionar" / "En el celular a inspeccionar" | `USBGuide.tsx`, `usb-guide/AndroidGuide.tsx` |
| "Guía del Gabinete de Informática Forense" (resto de la marca vieja) | "Guía de uso de Factum" | `GuideModal.tsx` |
| "Datos del expediente", "Nombre del denunciante", "Fiscalía (deshabilitado)", "Unidad Fiscal 3", "Expediente 1234/26", "Se agregó al expediente" | "Datos de la causa", "Titular del dispositivo", "Tribunal (deshabilitado)", "Tribunal de ejemplo", "Causa 1234/26", "Se agregó al caso" | `design-system/DesignSystemShowcase.tsx` |

**Se quedan como están** (descripción del producto, no rol de una persona): "Adquisición forense…" (`layout.tsx`, `SiteFooter.tsx`) y "preservación forense" (`app/page.tsx`).

**Grep de aceptación** (sobre las etiquetas visibles; los comentarios y los identificadores que cita la lista de arriba se admiten):

```bash
grep -rnE "\"[^\"]*(Fiscal|fiscal|Funcionario|Denunciante|denunciante|Expediente|expediente)[^\"]*\"|>[^<{]*(Fiscal|fiscal|Funcionario|Denunciante|denunciante|Expediente|expediente)[^<]*<" client/src
```

---

## 10. Decisiones técnicas

- **T1. Perfil en una colección propia (`expert_profiles`, `_id` = DNI)**, no embebido en otro documento. Upsert solo del propio documento. Implementa D1 A.
- **T2. `[BsonIgnoreExtraElements]`** en `Case`, `PeritoSnapshot`, `ReportTexts`, `CaptureRole`, `DeviceInfo` y `ExpertProfile`, para tolerar un rollback y campos futuros. Los defaults no anulables (`""`, `[]`, `0`) hacen que los documentos viejos deserialicen.
- **T3. `SchemaVersion` explícito** (`0`/`1`) en lugar de inferir "caso viejo" por la ausencia de `Perito`. La UI y la validación preguntan por un solo campo.
- **T4. Los datos de la causa son campos planos de `Case`**, no un subdocumento. Así siguen el estilo de `NroReferencia`/`NombreDenunciante` y el JSON queda plano, como lo consume la UI hoy.
- **T5. La validación de obligatorios vive en `CaseValidation.cs`** y responde `{ error, missing }`, con las mismas claves que el cliente (6.4). Se quitan los `[Required]` de `CreateCaseRequest` (hallazgos 3 y 4).
- **T6. La copia del perfil se hace en `POST` y en `PUT` del caso**, con el perfil vigente en ese momento. Un caso generado no se edita más (`409`), así que su copia queda congelada (Gherkin "Editar el perfil no cambia casos anteriores").
- **T7. Los roles de captura viven en el caso** (`CaptureRoles`, upsert por `filename`), no en el nombre del archivo. No hay que renombrar archivos en el agente ni en el servidor, y las capturas se pueden remarcar.
- **T8. Los textos del paso 4 se guardan enteros** (`PUT` del objeto completo, debounce de 1.2 s). Es más simple que un `PATCH` por campo, y 8 campos de hasta 20 000 caracteres cada uno no pesan.
- **T9. El listado proyecta afuera `ReportTexts`** (4.3). El paso 4 lee `GET /api/cases/{id}`.
- **T10. Reemplazo de placeholders por run y en una sola pasada** (hallazgo 6). Conserva el formato de los rótulos y nunca reinterpreta texto del usuario.
- **T11. Bloques condicionales `{#x}`/`{/x}`** en lugar de heurísticas del tipo "borrar el título anterior". Lo opcional queda declarado en la plantilla.
- **T12. Frases derivadas** (`{fraseDomicilio}`, `{tramiteAnte}`, `{fraseIntegracion}`, `{datosProponente}`, `{elSuscripto}`), calculadas en código, para que un opcional vacío no deje una frase rota (D6).
- **T13. D10 A: el ZIP se cierra antes de generar el DOCX y no se reabre.** Se guarda `ReportHash`. Se borra `AppendToZipAsync`.
- **T14. La v4 nace de un script versionado** (`ops/plantilla/build_plantilla_v4.py`, Python stdlib), que edita por `w14:paraId` y tiene un modo `--check` (Anexo A). La v4 se puede reproducir cuando el usuario actualice su plantilla, y ningún archivo versionado contiene los datos reales.
- **T15. Tests:** no hay proyectos de test en el repo. La verificación del informe es un **arnés descartable** en el scratchpad (B16), como en la HU anterior, más `--check` del script. Proponer un proyecto `Factum.Backend.Tests` queda para una HU de testing.
- **T16. Sin LibreOffice en la generación.** `ConvertDocxToPdfAsync` es código muerto, pero queda (y también `libreoffice-writer` en el `Dockerfile`). Sacarlos no es de esta HU. Los implementadores sí usan `soffice` localmente para **verificar** el render.

---

## 11. Decisiones pendientes del usuario

### DP1. Textos por defecto (D7). Bloquea solo `B9b`

La HU validó la opción A: los redacta el `architect` sin datos reales y el usuario los revisa antes de implementarlos. Propuesta para revisar (van en `ReportDefaultTexts.cs`; cada estudio los puede reemplazar con `Report:DefaultTexts:*`).

**Operaciones realizadas** (plantilla del borrador; los `{…}` se completan con los datos del caso, 7.6):

```text
En fecha {fechaInspeccion}, siendo las {horaInspeccion} horas, se conectó el dispositivo ({tipoDispositivo} {marcaModeloDispositivo}, sistema operativo {sistemaOperativo}, IMEI {imeiDispositivo}) a la estación de trabajo del perito mediante cable USB, utilizando la herramienta Factum.
Sobre el dispositivo se realizaron las siguientes operaciones:
- Capturas de pantalla: {cantidadCapturas}.
- Grabaciones de pantalla: {cantidadGrabaciones}.
- Archivos copiados desde el almacenamiento del dispositivo: {cantidadArchivosExtraidos}.
- Otros archivos incorporados (fotografías y adjuntos): {cantidadOtros}.
Las operaciones se limitaron a la visualización, captura y copia de la información accesible desde la interfaz del dispositivo, sin modificar intencionalmente su contenido. A cada archivo obtenido se le calculó su valor hash mediante el algoritmo SHA-256.
```

**Aseguramiento de la evidencia** (va después de "A fin de resguardar la información obtenida:"):

```text
Cada archivo obtenido durante la inspección fue identificado con un nombre único y se le calculó su valor hash mediante el algoritmo SHA-256. Los archivos se agruparon en un contenedor ZIP, cuyo valor hash SHA-256 se calculó una vez cerrado el contenedor. Los valores obtenidos se detallan en la tabla siguiente y permiten verificar, en cualquier momento posterior, que la evidencia no fue alterada.
```

**Notas técnicas:**

```text
Los valores hash se expresan en notación hexadecimal y fueron calculados con el algoritmo SHA-256. Cualquier modificación de un archivo, por mínima que sea, produce un valor hash distinto.
Las fechas y horas consignadas corresponden a la zona horaria {zonaHoraria}.
Las capturas de pantalla reflejan el contenido visible en el dispositivo al momento de la inspección. La inspección se limitó a la información accesible desde la interfaz del dispositivo y no incluyó la recuperación de información eliminada.
```

**Reserva:**

```text
{elSuscripto} se reserva el derecho de ampliar o complementar el presente informe en caso de que se aporten nuevos elementos o se requieran precisiones técnicas adicionales.
```

- **Recomendado:** aprobar tal cual o con los cambios que indique el usuario.
- **Mientras tanto:** `B9a` (mecanismo, tokens y endpoint) se implementa con estos textos, y `B9b` ("congelar los textos aprobados") espera la respuesta.

### DP2. Frases fijas de la v4 (D4 B). No bloquea

Para que la misma plantilla sirva para cualquier causa sin frases rotas (D4 B, D6), la v4 reescribe cuatro frases (Anexo A, A3):

- **Presentación (P008):** `{nombrePerito}, M.P. {matriculaPerito}, {profesionPerito}, en mi carácter de {caracterPerito}{fraseDomicilio}, me presento y respetuosamente digo:`
- **Encuadre (P010):** `Que vengo a emitir el presente Informe Pericial Técnico Informático en el marco de la causa {tipoCausa} N° {numeroCausa}, seguida contra {parteDenunciada}, que tramita ante {tramiteAnte}{fraseIntegracion}, conforme los siguientes datos:`
  - Por eso "Tipo de causa" se carga como **adjetivo del fuero** ("disciplinaria", "civil", "penal").
  - La parte denunciada lleva su artículo dentro del valor ("las Dras. …").
  - Sin sala, la frase dice "ante {tribunal}"; con sala, "ante Sala II de {tribunal}".
- **Declaración (P020):** "{elSuscripto} manifiesta…"
- **Objeto (P022):** "…aportado voluntariamente por su titular…" y "…en el marco de la actuación en trámite."

Además se agregan la firma y el anexo (A4).

**Recomendado:** se implementa así. Si el usuario quiere otra redacción, alcanza con editar el script y regenerar (sin tocar `ReportService`). El usuario lo revisa con la v4 abierta en Word o WPS antes de aprobar la HU (D13).

---

## 12. Checklist atómico

> **Regla dura (los dos implementadores):**
> - No se modifican ni borran documentos de Mongo preexistentes ni archivos de `Storage:DataDirectory`.
> - Las pruebas que escriben (perfil, casos) usan un DNI de prueba propio y limpian **solo por los `_id` que insertaron** (`expert_profiles`: el DNI de prueba; `cases`: los ids devueltos).
> - El arnés del informe trabaja en un directorio temporal del scratchpad, nunca en `Storage:DataDirectory`.
> - Ningún dato real (domicilio, colegio, nombres) va a archivos versionados ni a `progress/`.
> - No se toca `backlog.json` ni `progress/current.md`.

### 12.1 `implementer-backend` (`server/src/Factum.Backend`, `ops/plantilla/`, `.gitignore`, `README.md`)

Orden sugerido (D16): modelo → endpoints → plantilla → `ReportService`.

- [ ] **B1. Modelos.**
  - `Models/Case.cs`: los campos de 4.2, más `PeritoSnapshot`, `ReportTexts` y `CaptureRole` (en el mismo archivo), y `[BsonIgnoreExtraElements]` en todas las clases (T2).
  - `Models/DeviceInfo.cs`: `Name` y el atributo.
  - Crear `Models/ExpertProfile.cs` (4.1).
- [ ] **B2. Repositorios.**
  - Crear `Infrastructure/ExpertProfileRepository.cs`: `IExpertProfileRepository` con `GetAsync(dni)` y `UpsertAsync(profile)` sobre la colección `expert_profiles`, registrado singleton en `Program.cs`.
  - Ampliar `CaseRepository`, siempre con `$set` y nunca con `Replace`:
    - `UpdateCaseDataAsync(id, CaseDataUpdate)` (los campos de 6.2 + `Perito` + `SchemaVersion = 1` + `Device.Imei` opcional);
    - `UpdateReportTextsAsync(id, ReportTexts)`;
    - `UpsertCaptureRolesAsync(id, IReadOnlyList<(string Filename, string? Role)>)`: lee, fusiona y hace `$set` de la lista completa;
    - `UpdateGeneratedAsync` suma `reportHash`;
    - `ListByOfficerAsync` con `Projection.Exclude(c => c.ReportTexts)`.
- [ ] **B3. DTOs.**
  - `DTOs/CaseDtos.cs`:
    - `CreateCaseRequest` con todos los campos `string?` y **sin `[Required]`**;
    - `UpdateCaseRequest`, `ReportTextsDto`, `CaptureRolesRequest`/`CaptureRoleDto`;
    - `DeviceInfoDto.Name = ""`;
    - `GenerateResponse` suma `ReportHash`.
  - Crear `DTOs/ProfileDtos.cs`: `ExpertProfileRequest` y `ExpertProfileResponse` (6.1).
  - Verificar el JSON real de cada uno contra la sección 6 (casing).
- [ ] **B4. `Services/Cases/CaseValidation.cs`.**
  - Obligatorios y largos de 6.4.
  - Tres funciones: `ValidateProfile`, `ValidateCaseData` (crear/editar) y `ValidateForGenerate`.
  - Cada una devuelve `IReadOnlyList<string> missing` y/o un error de largo.
- [ ] **B5. Perfil.**
  - Crear `Services/Profile/ExpertProfileService.cs` con `GetAsync(User)` (valores sugeridos si no existe) y `SaveAsync(User, req)` (trim, validación, `Tratamiento` normalizado, `CreatedAt`/`UpdatedAt`).
  - Crear `Controllers/ProfileController.cs` (`[Route("api/profile")]`, `[Authorize]`) con `GET` y `PUT`.
- [ ] **B6. `CaseService` y `CasesController`.**
  - `CreateAsync`:
    - valida;
    - exige un perfil completo (`missing: ["perfil"]`);
    - copia el perfil a `Perito`;
    - `SchemaVersion = 1`;
    - guarda `Device.Name`.
  - `UpdateAsync` (`PUT /{id}`): dueño, `409` si no es editable, valida, copia el perfil y hace `$set`.
  - `SaveReportTextsAsync`, `GetReportTextDefaultsAsync` y `UpsertCaptureRolesAsync`, con las validaciones de 5.
  - `GenerateAsync` con la validación previa de 7.8.
  - Mapear en el controlador `400` (`{ error, missing }`), `403`, `404` y `409`. Reemplazar la detección de errores por `string.Contains` por un tipo de error con código: `Result` + `ErrorKind`, o el equivalente que el implementador prefiera dentro de `Common/Result.cs`, sin romper a los otros usuarios de `Result`.
- [ ] **B7. `EvidenceClassifier`** (4.5), usado por defaults, roles y `ReportService`.
- [ ] **B8. Config `Report`.**
  - `Services/Reports/ReportOptions.cs` y la resolución de la zona horaria al arrancar, con warning y fallback a `-03:00`.
  - `Configure<ReportOptions>` en `Program.cs`, y resolver la opción al arrancar (como `IBrandingService`).
  - La sección vacía en `appsettings.json` (4.4).
  - `tzdata` en el `Dockerfile`.
- [ ] **B9a. Textos por defecto: mecanismo.** `Services/Reports/ReportDefaultTexts.cs` (constantes) + el renderer de tokens de 7.6, con la regla de las líneas `"- "` en 0. Se implementa con los textos de DP1.
- [ ] **B9b. ⛔ Espera DP1.** Reemplazar las constantes por los textos que apruebe el usuario (o confirmar los de la propuesta).
- [ ] **B10. `.gitignore`.** Sumar `docs/INFORME*` y comprobarlo con `git check-ignore -v`.
- [ ] **B11. Plantilla v4** (Anexo A).
  - Crear `ops/plantilla/build_plantilla_v4.py` y `ops/plantilla/README.md`.
  - Correrlo para generar `server/src/Factum.Backend/Templates/plantilla_informe_v4.docx`.
  - `--check` tiene que dar `OK`.
  - Renderizar con `soffice --headless --convert-to pdf` en el scratchpad y mirar el PDF.
  - **Borrar** `Templates/plantilla_informe_v3.docx` y `Templates/sin-foto-placeholder.png`. **Conservar** `factum-sello.png`.
- [ ] **B12. `ReportService`** según el Anexo B: pasadas B-R1 a B-R9, borrado del código de la v3 y la plantilla `plantilla_informe_v4.docx`.
- [ ] **B13. Flujo D10** (7.1): sin `AppendToZipAsync`, con `ReportHash` en `ReportResult`, en el repositorio y en `GenerateResponse`.
- [ ] **B14. Nombres de archivo.**
  - `IsGeneratedArtifact` con el prefijo nuevo y el viejo (7.9).
  - DOCX `informe_pericial_*`.
  - **Corregir `Sanitize`**: recortar sobre el string ya filtrado y recortado, y si queda vacío usar `"caso"`.
- [ ] **B15. `README.md`.** Sección "Informe pericial: configuración", con `Report:*` y un ejemplo con **placeholders** (`"DomicilioConstituido": "Calle Ejemplo 123, Ciudad"`), y una explicación breve de cómo se regenera la v4 (`ops/plantilla/README.md`).
- [ ] **B16. Arnés descartable** (scratchpad, no se versiona): una consola o un `dotnet run` que instancia `ReportService` con un `Case` en memoria y un directorio temporal con 3 PNG (uno `imei_modelo`, uno `nombre_dispositivo`, uno sin rol), un `.mp4` falso y un archivo con `SourcePath`. Tres corridas:
  - **(a)** todo completo, con Branding y domicilio de placeholders;
  - **(b)** opcionales vacíos, sin Branding y sin capturas para el anexo;
  - **(c)** `Tratamiento = "suscripta"` y sala + integrantes.

  En cada corrida, comprobar:
  - que no queda ningún `{` de placeholder en `document.xml`, `header*.xml` ni `footer*.xml`;
  - que la tabla tiene N + 1 filas;
  - que `zipHash == sha256sum(zip)` y que `reportHash == sha256sum(docx)`;
  - que el ZIP no contiene el DOCX;
  - que los bloques condicionales aparecen o desaparecen como corresponde;
  - que los rótulos siguen en negrita y los valores no;
  - el render en PDF con LibreOffice (membrete, atribución con margen de pie, firma y anexo con dos figuras por página).

  Borrar el arnés al terminar.
- [ ] **B17. Prueba de endpoints** (con el backend levantado y Mongo de dev). El ciclo con un DNI de prueba:
  - `GET`/`PUT /api/profile`;
  - `POST /api/cases` (incluido el `400 missing`);
  - `PUT /api/cases/{id}`;
  - subir 1 PNG;
  - `PUT capture-roles`;
  - `GET defaults`;
  - `PUT report-texts`;
  - `generate` (incluido el `400 missing`) y descarga.

  Después se limpian **solo** el perfil del DNI de prueba y los casos creados por id, y se borran sus carpetas `cases/<id>` creadas por la prueba (solo esos ids).
- [ ] **B18.** Escribir `progress/impl_backend_informe-pericial-de-parte.md`: archivos tocados, resultado de `--check` (solo `OK`/`FALLA`), las tres corridas del arnés, los curl, el estado de B9b. **Sin datos reales.**

### 12.2 `implementer-frontend` (solo `client/`)

Skills obligatorias: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (como criterio, sin agregar 3D) y `web-design-guidelines` (autochequeo); sumar `ui-styling` y `mblode-agent-skills-ui-animation` si corresponde. Se deja constancia en el progress. Hay que leer `client/AGENTS.md`.

- [ ] **F1. Tipos y API** (`client/src/lib/api.ts`, `client/src/types/index.ts`).
  - Interfaces de 6.1-6.3, con los campos nuevos de `Case` y `DeviceInput.name?`.
  - Corregir `getCase` a `{ cas: Case; files: … }`.
  - Métodos nuevos: `getProfile`, `saveProfile`, `updateCase`, `getReportTextDefaults`, `saveReportTexts` y `saveCaptureRoles`.
  - `generateCase` suma `report_hash`.
  - `request()` tiene que exponer `missing` cuando venga: por ejemplo, un `ApiError extends Error { status; missing?: string[] }`.
  - `createCase` deja de mandar `observaciones` y manda `device.name`.
- [ ] **F2. `client/src/lib/pericial.ts`.**
  - Las claves de 6.4 y sus etiquetas.
  - `validateCaseForm` y `validateProfile`, con los mensajes de 8.2.
  - `getMissingRequirements(case)`, que devuelve `{ key, label, step, fieldId }[]`.
  - `prefillFromLastCase(historyCases)`.
  - `isPhoneLike(operator)`.
- [ ] **F3. `CaseFormData`** con claves JSON (8.2), y `EMPTY_FORM` actualizado.
- [ ] **F4. `useExpertProfile`** (8.3).
- [ ] **F5. `ExpertProfileCard`**, la tarjeta del paso 2 (plegada o desplegada, botón "Editar", cinco campos).
- [ ] **F6. `CaseFormStep`.**
  - Secciones, campos, hints y orden de foco de 8.2.
  - Sin "Observaciones".
  - Indicador `n/m`.
  - Modo edición (props `mode: "create" | "edit"` y `isLegacy`).
- [ ] **F7. `page.tsx`.**
  - `STEPS` de 8.1, `minJumpable`, confirmación de salida en los pasos 2 a 5.
  - `handleCreateCase` → `handleSaveCase`: `PUT` del perfil si hace falta, después `POST` o `PUT` del caso, y mapeo de `missing`.
  - Precarga al iniciar el wizard.
  - `proceedResume` con la rama de `schema_version === 0` (8.9).
  - `focusFieldId`, `onCaseUpdated` y `result.reportHash`.
  - Renderizar `ReportStep` en el paso 4, `GenerateStep` en el 5 y `ResultStep` en el 6.
- [ ] **F8. `useFileManager`.**
  - `captureRole` en `CapturedFile`.
  - `setCaptureRole`.
  - Después de subir los archivos, `PUT capture-roles` con las screenshots locales y callback con el caso actualizado.
  - El `PUT` inmediato si el archivo ya está subido.
- [ ] **F9. `CaptureStep`.**
  - Menú "Marcar como…" (PrimeReact `Menu`, operable con teclado), badge en la miniatura, chips con texto y ayuda (8.4).
  - Bloque de identificación opcional con la terminología nueva.
- [ ] **F10. `ReportStep`** (nuevo, 8.5): carga, defaults, autoguardado con indicador `aria-live`, "Restaurar" con `ConfirmDialog`, y "Continuar" bloqueado mientras guarda o hay error.
- [ ] **F11. `GenerateStep`** (8.6): checklist con enlaces que enfocan el campo, botón deshabilitado, carátula en el resumen, terminología y manejo del `400 missing`.
- [ ] **F12. `ResultStep`** (8.7): dos hashes, textos corregidos y sin "Incluida en el informe Word".
- [ ] **F13. `UserMenu` y `ExpertProfileDialog`** (8.3).
- [ ] **F14. `CaseHistory`, `CaseCard` y `CaseGridCard`** (8.8), incluidas las etiquetas de hash según `schema_version`.
- [ ] **F15. El resto de la terminología** (sección 9): `IOSModePicker`, `USBGuide`, `AndroidGuide`, `GuideModal` y `DesignSystemShowcase`. Correr el grep de aceptación.
- [ ] **F16. Accesibilidad.**
  - Cada `textarea` e `input` con su `label`.
  - Los chips con texto.
  - El menú operable con teclado.
  - Los enlaces del checklist llevan el foco.
  - Los errores con `aria-describedby`, como hoy.
- [ ] **F17.** Escribir `progress/impl_frontend_informe-pericial-de-parte.md`: archivos tocados, `tsc`, la constancia de las skills y la prueba en `npm run dev`.

### 12.3 Orquestador

- [ ] **O1.** Mostrarle DP1 (los textos) y DP2 (las frases) al usuario. Pasarle la respuesta de DP1 a `implementer-backend` para `B9b`.
- [ ] **O2.** Lanzar `implementer-backend` **antes** de que el frontend dependa de los endpoints. Los dos pueden arrancar a la vez, porque el contrato está fijado en la sección 6.

---

## 13. Verificación

### `implementer-backend`

```bash
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet build server/src/Factum.Agent/Factum.Agent.csproj   # no se toca; tiene que seguir compilando
python3 ops/plantilla/build_plantilla_v4.py --check \
  "docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx" \
  server/src/Factum.Backend/Templates/plantilla_informe_v4.docx      # imprime solo OK / FALLA:<tipo>
git check-ignore -v "docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx"
git ls-files server/src/Factum.Backend/Templates                     # v4 y sello; sin v3 ni sin-foto
```

Además, el arnés de B16 y los curl de B17.

### `implementer-frontend`

```bash
cd client && npx tsc --noEmit
```

Además, el grep de la sección 9 y un recorrido en `npm run dev` con el backend levantado:
- perfil nuevo y perfil completo;
- un caso nuevo de punta a punta;
- la validación del paso 2;
- marcar y desmarcar capturas;
- el autoguardado (cortar el backend y ver "No se pudo guardar · Reintentar");
- el checklist del paso 5;
- `ResultStep`;
- el historial;
- el diálogo de perfil desde `UserMenu`;
- modo claro y oscuro.

### Reviewer

C1 a C5 de `CHECKPOINTS.md`, más:
- los nombres de la sección 6 en el diff de los dos lados;
- `--check` en `OK`;
- que la v4 no está en `docs/` y que el diff no contiene datos reales;
- que no queda ninguna referencia a `plantilla_informe_v3`, `{FOTO_FUNCIONARIO}` ni `AppendToZipAsync`.

---

## 14. Prueba manual para el usuario

1. En `server/src/Factum.Backend/appsettings.Local.json` (no versionado), cargar `Report:DomicilioConstituido` con el domicilio real y dejar `Branding` como está. Reiniciar el backend.
2. **Perfil:** iniciar sesión, "Nueva inspección" → paso **Causa**. La tarjeta "Tus datos de perito" aparece desplegada, con el nombre precargado. Sin matrícula, no deja avanzar.
3. **Caso nuevo:** completar la causa. Probar dejar la carátula vacía y ver "Ingresá la carátula", con el foco en el campo. Crear.
4. **Captura:** tomar una captura de `*#06#` y marcarla "IMEI y modelo"; otra de "Acerca del teléfono", marcada "Nombre del dispositivo"; algunas sin marca. Opcionalmente, las fotos del perito y del titular.
5. **Informe:** "Operaciones realizadas" trae el borrador con los conteos. Escribir resultados, valoración y conclusiones. Cerrar el navegador, retomar el caso desde el historial y comprobar que los textos siguen ahí.
6. **Generar:** con "Conclusiones" vacía, el botón está deshabilitado y el enlace lleva al campo. Completar y generar.
7. **Resultado:** descargar el ZIP y el DOCX.
   - `shasum -a 256` del ZIP coincide con el hash de la pantalla y con la última fila de la tabla del informe; el ZIP **no** contiene el DOCX.
   - El hash del DOCX coincide con el de la pantalla.
8. **Abrir el DOCX en Word o WPS:**
   - membrete con el logo y el nombre del estudio en todas las páginas;
   - el domicilio en la presentación;
   - fecha y hora de la inspección en hora local;
   - sin `{…}` sueltos;
   - rótulos en negrita;
   - las capturas de identificación en "Elementos ofrecidos";
   - la tabla de hashes;
   - la firma;
   - el anexo de capturas;
   - "Realizado con Factum" en el pie, sin pegarse al borde.

   Revisar las frases de DP2.
9. **Opcionales vacíos:** generar otro caso sin organismo, sala, integrantes, notas ni reserva. No quedan frases rotas: la línea del organismo no aparece, "Línea: No informado", y las secciones de notas y reserva no aparecen.
10. **Perfil desde el menú:** cambiar la matrícula en "Mi perfil de perito" (`UserMenu`). El caso ya generado sigue mostrando la matrícula vieja; uno nuevo usa la nueva.
11. **Casos viejos:** los informes generados antes se siguen descargando y su hash no cambió. Un borrador viejo, al retomarlo, pide los datos de la causa.
12. **Regresión:** una inspección Android y una iOS de punta a punta (captura, grabación, explorador de archivos, descarga) funcionan igual que antes.

---

## Anexo A. Cómo nace `plantilla_informe_v4.docx` (`B11`)

### A1. Script

`ops/plantilla/build_plantilla_v4.py` (Python 3, **solo stdlib**: `zipfile`, `re`, `argparse`, `hashlib`).

**Uso:**

```bash
python3 ops/plantilla/build_plantilla_v4.py <origen.docx> <destino.docx>           # construye
python3 ops/plantilla/build_plantilla_v4.py --check <origen.docx> <destino.docx>   # verifica
```

**Reglas del script:**
- **Edita el XML como texto, por `w14:paraId`.** Cada párrafo objetivo se ubica con la regex `<w:p\b[^>]*w14:paraId="ID"[^>]*>.*?</w:p>` (no hay párrafos anidados en el cuerpo). Antes de editar, verifica una **huella no sensible** de cada ancla. Por ejemplo:
  - `384E45D4` contiene `{tribunal/fiscalia}`;
  - `74B641B2` empieza con `{usuarioNombreCompleto}`;
  - `7C035883` es el párrafo siguiente a `384E45D4`.

  Si una huella no coincide (el usuario editó su plantilla), **aborta** con `FALLA: ancla <paraId>`.
- **Nunca escribe ni imprime** el texto original de los párrafos `7C035883` y `74B641B2`. Reconstruye esos párrafos desde cero con el texto de A3.
- **Forma de los párrafos que reescribe:**
  - conserva el `w:pPr` original (salvo la normalización de A3);
  - arma los runs con el `w:rPr` "base" del párrafo: el primer run sin `<w:b/>`; para los rótulos, ese mismo `rPr` con `<w:b/><w:bCs/>`;
  - **cada placeholder va en su propio run y en un solo `<w:t>`**.
- **Empaquetado:** reempaqueta con `[Content_Types].xml` primero, `ZIP_DEFLATED` y fecha fija `1980-01-01 00:00:00` en todas las entradas, para que la salida sea determinista.

### A2. Partes del paquete

| Parte | Cambio |
|---|---|
| `word/document.xml` | A3 y A4. En `w:sectPr`: agregar `<w:headerReference w:type="default" r:id="rIdHdr1"/>` **antes** del `footerReference`, y cambiar `w:footer="0"` por **`w:footer="567"`** (1 cm). El resto de `pgMar` queda igual |
| `word/header1.xml` (nuevo) | Membrete, A5 |
| `word/_rels/document.xml.rels` | Agregar `rIdHdr1` → `header1.xml` (tipo `…/relationships/header`) |
| `[Content_Types].xml` | `Override PartName="/word/header1.xml"` con `application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml` |
| `docProps/core.xml` | A6 |
| `word/footer1.xml` | Sin cambios. La atribución la agrega el código |
| resto | Sin cambios |

### A3. Párrafos del cuerpo

`**x**` = run en negrita. Los marcadores `{#…}`/`{/…}` son párrafos propios: `pPr` con `spacing after=0`, `line=240`, sin numeración, y run en Times New Roman.

| paraId (posición) | v4 |
|---|---|
| `0DF4CF61` (P000) | sin cambios |
| `384E45D4` (P003) | `{nombreTribunal}` |
| `7C035883` (P004) | se reconstruye como `{organismoTribunal}`. **Antes** se inserta un párrafo `{#organismoTribunal}` y **después** uno `{/organismoTribunal}` |
| `27679AFE` (P005) | sin cambios |
| `74B641B2` (P008) | `{nombrePerito}, M.P. {matriculaPerito}, {profesionPerito}, en mi carácter de {caracterPerito}{fraseDomicilio}, me presento y respetuosamente digo:` |
| `03940239` (P010) | `Que vengo a emitir el presente Informe Pericial Técnico Informático en el marco de la causa {tipoCausa} N° {numeroCausa}, seguida contra {parteDenunciada}, que tramita ante {tramiteAnte}{fraseIntegracion}, conforme los siguientes datos:` |
| `3A17DBEE` (P013) | `**Carátula:** {caratula}` |
| `2AFB1345` (P014) | `**Parte denunciante:** {parteDenunciante}` |
| `15BB4A06` (P015) | `**Parte denunciada:** {parteDenunciada}` |
| `69BAC851` (P016) | `**Objeto:** {objetoCausa}` |
| `3DA3ACE8` (P017) | `**Fecha de intervención:** {fechaIntervencion}` |
| `577D1792` (P020) | `{elSuscripto} manifiesta que el presente informe ha sido elaborado conforme a los principios de objetividad, independencia de criterio y rigor técnico-científico, aplicando metodologías reconocidas en el ámbito de la informática forense.` |
| `2F541765` (P022) | `El presente informe tiene por finalidad realizar una inspección técnica y documentación del contenido digital existente en un dispositivo móvil aportado voluntariamente por su titular, a los fines de su análisis y eventual valoración en el marco de la actuación en trámite.` |
| `5596EEF2` (P023) | `{objetoInforme}`, entre `{#objetoInforme}` y `{/objetoInforme}`; `pPr` normalizado (\*) |
| `5F66E0AC` (P026) | `**Perito de parte:** {nombrePerito}, M.P. {matriculaPerito}, {profesionPerito}.` |
| `08C61010` (P027) | `**Parte que lo propone:** {datosProponente}` |
| `3162E6AB` (P028) | `**Ámbito:** {ambitoCausa}` |
| `5FECA2F2` (P031) | `En fecha {fechaInspeccion}, siendo horas {horaInspeccion}, se procedió a la inspección técnica del dispositivo:` |
| `14341833` … `2E1C810A` (P032-P036) | `**Tipo:** {tipoDispositivo}` · `**Marca y modelo:** {marcaModeloDispositivo}` · `**IMEI:** {imeiDispositivo}` · `**Línea:** {lineaDispositivo}` · `**Titular:** {titularDispositivo}` |
| `26DDBA6A` (P037) | sin cambios |
| `07342EEC` (P038) | `{capturasImeiModelo}`, con `jc=center` |
| `1FD00BD3` (P039) | `{capturasNombreDispositivo}`, con `jc=center`, entre `{#capturasNombreDispositivo}` y `{/capturasNombreDispositivo}` |
| `46A3AB79` (P042) | `{descripcionOperacionesRealizadas}`; `pPr` normalizado (\*). Se le saca `numPr numId=0` y `ind left=2192` |
| `2D02EAD7` (P045) | `{descripcionAseguramientoEvidencia}`. Su `pPr` es el modelo (\*) |
| tabla (P047) | Ver abajo |
| `404845D9`, `10CF937D`, `08A80D56` (P060, P062, P064) | `{descripcionResultados}`, `{descripcionValoracionTecnica}`, `{descripcionConclusiones}`; `pPr` normalizado (\*) |
| `05D9340B` + `7290E48F` (P065-P066) | título "NOTAS TÉCNICAS" + `{descripcionNotasTecnicas}`, con `{#descripcionNotasTecnicas}` antes del título y `{/descripcionNotasTecnicas}` después del texto |
| `10E394C7` + `516E02D3` (P067-P068) | lo mismo con `descripcionReserva` |
| `4FC2AE2A` (P070) | sin cambios |
| después de `21DB9123` (P072) | A4 |

(\*) **`pPr` normalizado** = una copia del `w:pPr` de `2D02EAD7`: `spacing after=0 line=360 lineRule=auto`, `ind firstLine=2268`, `jc=both`.

**Tabla `NOMBRE | HASH SHA-256`:**
- **Grilla:** `3600` / `4691` dxa (antes `4153` / `4138`), con los `tcW` iguales.
- **Fila 0:** `w:trPr` con `<w:tblHeader/>`.
- **Fila 1 (modelo):** `w:trPr` con `<w:cantSplit/>`.
  - `{nombreArchivo}`: run de 9 pt (`sz=18`), `jc=left`.
  - `{hashArchivo}`: run Courier New 8 pt (`sz=16`), `jc=left`.
- **Párrafos de las celdas:** `spacing line=240` (antes 360), para que el hash ocupe dos líneas compactas.

### A4. Firma y anexo (después del último párrafo)

En orden:
1. `______________________________` centrado, `spacing before=720`.
2. `{nombrePerito}` centrado, en negrita.
3. `{profesionPerito} – M.P. {matriculaPerito}` centrado.
4. `{caracterPerito}` centrado.
5. `{#anexoCapturas}`.
6. Un párrafo con `<w:r><w:br w:type="page"/></w:r>`.
7. `ANEXO – CAPTURAS DE PANTALLA`, centrado y en negrita.
8. `Capturas de pantalla obtenidas durante la inspección del dispositivo. Cada una se identifica con el nombre de su archivo, que figura en la tabla de valores hash.` (`jc=both`).
9. `{anexoCapturas}`, con `jc=center`.
10. `{/anexoCapturas}`.
11. Un párrafo vacío final (`spacing after=0`, `sz=2`). El body tiene que terminar en un párrafo aunque se borre el bloque.

### A5. `word/header1.xml` (membrete, D13)

Estructura (namespaces `w`, `r`, `w14` como en `footer1.xml`):

```xml
<w:hdr ...>
  <w:p><w:r><w:t>{#MEMBRETE}</w:t></w:r></w:p>
  <w:tbl>
    <w:tblPr>
      <w:tblW w:w="5000" w:type="pct"/>
      <w:tblBorders><w:bottom w:val="single" w:sz="4" w:space="0" w:color="7F7F7F"/></w:tblBorders>
      <w:tblLayout w:type="fixed"/>
      <w:tblCellMar><w:left w:w="0" w:type="dxa"/><w:right w:w="0" w:type="dxa"/></w:tblCellMar>
    </w:tblPr>
    <w:tblGrid><w:gridCol w:w="3400"/><w:gridCol w:w="5247"/></w:tblGrid>
    <w:tr>
      <w:tc><w:tcPr><w:tcW w:w="3400" w:type="dxa"/><w:vAlign w:val="center"/></w:tcPr>
        <w:p><w:pPr><w:spacing w:after="80"/><w:jc w:val="left"/></w:pPr>
          <w:r><w:t>{LOGO_ORGANIZACION:5x1.5}</w:t></w:r></w:p></w:tc>
      <w:tc><w:tcPr><w:tcW w:w="5247" w:type="dxa"/><w:vAlign w:val="center"/></w:tcPr>
        <w:p><w:pPr><w:spacing w:after="0"/><w:jc w:val="right"/></w:pPr>
          <w:r><w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman"/><w:b/><w:sz w:val="20"/></w:rPr><w:t>{ORGANIZACION}</w:t></w:r></w:p>
        <w:p><w:pPr><w:spacing w:after="80"/><w:jc w:val="right"/></w:pPr>
          <w:r><w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman"/><w:color w:val="595959"/><w:sz w:val="16"/></w:rPr><w:t>{CONTACTO}</w:t></w:r></w:p></w:tc>
    </w:tr>
  </w:tbl>
  <w:p><w:r><w:t>{/MEMBRETE}</w:t></w:r></w:p>
  <w:p><w:pPr><w:spacing w:after="0" w:line="240" w:lineRule="auto"/><w:rPr><w:sz w:val="2"/></w:rPr></w:pPr></w:p>
</w:hdr>
```

- **Ancho:** 8647 dxa = ancho útil A4 con los márgenes de la plantilla. `w14:paraId` nuevos y únicos (8 dígitos hex, sin repetir los existentes).
- **Encabezado en todas las páginas** (recomendación de D13).
- **Alto:** el header arranca a 708 dxa del borde y el margen superior es 1809 dxa. Un logo de 1.5 cm más 2 o 3 líneas de contacto entra. Si hay más líneas, Word empuja el cuerpo hacia abajo solo.

### A6. `docProps/core.xml`

Reemplazar el contenido por:
- `dc:creator` = `Factum`;
- `cp:lastModifiedBy` = `Factum`;
- `dcterms:created` = `dcterms:modified` = `2026-10-01T00:00:00Z`;
- `cp:revision` = `1`.

**Ningún otro campo con nombres.** `docProps/custom.xml` y `customXml/*` se pueden conservar: son identificadores de WPS, sin datos personales.

### A7. `--check` (lo corre el implementador y el reviewer)

Abre los dos `.docx`, el origen y el destino, e imprime **solo** `OK` o una línea `FALLA:<tipo>` por cada problema, **sin mostrar nunca el texto que falló**.

1. **Placeholders esperados:** el destino contiene **exactamente** este conjunto, cada uno entero dentro de un solo `<w:t>`:
   - **cuerpo:**
     - perito y presentación: `{nombreTribunal}`, `{organismoTribunal}`, `{nombrePerito}`, `{matriculaPerito}`, `{profesionPerito}`, `{caracterPerito}`, `{fraseDomicilio}`, `{elSuscripto}`;
     - causa: `{tipoCausa}`, `{numeroCausa}`, `{parteDenunciada}`, `{tramiteAnte}`, `{fraseIntegracion}`, `{caratula}`, `{parteDenunciante}`, `{objetoCausa}`, `{fechaIntervencion}`, `{objetoInforme}`, `{datosProponente}`, `{ambitoCausa}`;
     - equipo: `{fechaInspeccion}`, `{horaInspeccion}`, `{tipoDispositivo}`, `{marcaModeloDispositivo}`, `{imeiDispositivo}`, `{lineaDispositivo}`, `{titularDispositivo}`;
     - capturas: `{capturasImeiModelo}`, `{capturasNombreDispositivo}`, `{anexoCapturas}`;
     - textos: `{descripcionOperacionesRealizadas}`, `{descripcionAseguramientoEvidencia}`, `{descripcionResultados}`, `{descripcionValoracionTecnica}`, `{descripcionConclusiones}`, `{descripcionNotasTecnicas}`, `{descripcionReserva}`;
     - tabla: `{nombreArchivo}`, `{hashArchivo}`;
   - **header:** `{LOGO_ORGANIZACION:5x1.5}`, `{ORGANIZACION}`, `{CONTACTO}`;
   - **marcadores de bloque** de 7.3, cada uno abierto y cerrado una sola vez.

   Cualquier otro `{…}` es `FALLA:placeholder-desconocido`.
2. **Sin datos reales:** el texto de todas las partes XML del destino **no** contiene, sin distinguir mayúsculas ni espacios repetidos:
   - el texto completo del párrafo `7C035883` del **origen** (recortado);
   - el fragmento del párrafo `74B641B2` del origen comprendido entre `domicilio constituido en ` y `, me presento`;
   - los valores de `dc:creator` y `cp:lastModifiedBy` del origen.

   Los tres se leen en memoria y no se imprimen.
3. **`core.xml`:** `dc:creator` y `cp:lastModifiedBy` del destino valen `Factum`.
4. **Estructura:**
   - `sectPr` con `headerReference` y `w:footer="567"`;
   - `header1.xml` declarado en la relación y en el content type;
   - todos los `w14:paraId` del destino son únicos;
   - **no hay** `w:author`.
5. **XML bien formado:** todas las partes `.xml` parsean con `xml.etree.ElementTree.fromstring`.

---

## Anexo B. Ajustes de `ReportService` para la v4 (absorbe §8.3 de la SDD anterior)

### B-1. Qué se borra (era layout calibrado para la v3)

- `NormalizePageFlow` (ya estaba comentado) y las constantes `TableFitsSpacerTwips`, `TableFitsAvailableTwips`, `TableHeaderRowTwips`, `TableRowNoOriginTwips` y `TableRowWithOriginTwips`. **`MoveContentTableUp` no existe** (hallazgo 9).
- `ReplaceArchivosWithList` (`{ARCHIVOS}`): lo reemplaza la tabla de 7.5.
- `EmbedImages` con `{FOTO_FUNCIONARIO}`/`{FOTO_DENUNCIANTE}`, las constantes `PhotoMaxW = 1_238_700` y `PhotoMaxH = 1_332_537`, el uso de `sin-foto-placeholder.png`, y la rama `cover` de `BuildImageParagraph` (ya no la usa nadie: el logo y las capturas usan fit). `BuildDrawing` conserva el parámetro `srcRect` opcional, sin uso, o se simplifica.
- `ReplacePlaceholderWithScreenshots` y su fallback de "agregar al final del body si no está el placeholder".
- `BuildTextReplacements` con los placeholders de la v3 (`{NROREF}`, `{NOMBRE_FUNCIONARIO}`, `{CLAVE}`, `{HASH}`, `{fHora}`, …).
- `AppendToZipAsync` (T13).
- La constante del nombre `plantilla_informe_v3.docx`.

### B-2. Qué queda igual

- `ReplaceOrganizationLogo` (`{LOGO_ORGANIZACION[:WxH]}`, ahora en el header).
- `ReplaceMultilinePlaceholder` para `{CONTACTO}`.
- `AddFactumAttributionFooter` y todo el bloque de la atribución. Con la v4, el footer `Default` existe (`footer1.xml`), así que se **agrega** el párrafo a esa parte, junto al número de página flotante.
- `BuildImageParagraph` (fit), `BuildDrawing`, `ImageSource`, `GetImageSize`, `IsUsableImage`, el contador `drawId` (offset `100_000`), `CreateZipAsync`, `BuildHashesAsync`, `Sha256Async` y `GeneratePassword`.

### B-3. Pasadas de `GenerateDocxAsync` (v4), en este orden

| Paso | Qué hace |
|---|---|
| **B-R0** | Copia `Templates/plantilla_informe_v4.docx`. Arma `parts` = body + headers + footers (como hoy). |
| **B-R1. Validación de la plantilla** | Recolecta los `{…}` de todas las partes, con la regex `\{[#/]?[A-Za-z_][A-Za-z0-9_]*(?::[0-9.,x]+)?\}`. Los que no estén en el conjunto conocido (A7.1) se **borran** y se loguean **una vez** con un warning ("Plantilla: placeholder desconocido {x}"). Esto pasa **antes** de insertar valores, para no tocar nunca texto del usuario. |
| **B-R2. Bloques condicionales** | 7.3, en body y header. |
| **B-R3. Logo** | `ReplaceOrganizationLogo` en cada parte. |
| **B-R4. Multilínea** | `{CONTACTO}` (como hoy) y luego `{objetoInforme}`/`{descripcion*}` (7.4). |
| **B-R5. Tabla de hashes** | 7.5. |
| **B-R6. Texto** | Reemplazo **por run y de una sola pasada**, con los valores de 7.2. Por cada párrafo: se concatena el texto de sus `w:r/w:t` guardando el offset de cada `Text`; `Regex.Matches` con `\{[A-Za-z_][A-Za-z0-9_]*\}`; por cada match que esté en el diccionario, el valor se escribe en el `Text` donde empieza el match (reemplazando ese fragmento), y los fragmentos del placeholder que caigan en `Text` siguientes se recortan de esos nodos. Los runs y sus `rPr` se conservan. Los valores no se re-escanean (se procesa de atrás hacia adelante o con offsets acumulados). Con la v4, el script garantiza un placeholder por `w:t`, así que el camino de "match partido" es solo defensivo. |
| **B-R7. Imágenes** | `{capturasImeiModelo}`, `{capturasNombreDispositivo}`, `{anexoCapturas}` (7.7), con los pies de foto. Cada imagen es un `ImagePart` del `MainDocumentPart`. |
| **B-R8. Atribución** | `AddFactumAttributionFooter` (sin cambios). |
| **B-R9. Guardar** | `doc.Save()`. Si quedó algún placeholder conocido sin reemplazar (no debería), un warning con el nombre. |

### B-4. Tamaños y constantes nuevas

| Constante | Valor | Uso |
|---|---|---|
| `IdentShotMaxW` / `IdentShotMaxH` | `2_520_000` / `3_780_000` EMU (7 × 10.5 cm) | capturas de identificación |
| `AnnexShotMaxW` / `AnnexShotMaxH` | `5_040_000` / `3_780_000` EMU (14 × 10.5 cm) | anexo: dos por página en A4 con los márgenes de la v4 (alto útil ≈ 24 cm) |

**Pie de foto:** párrafo centrado, `spacing before=0 after=200`, run en cursiva, 8 pt (`sz=16`), color `595959`.

**Fila de origen en la tabla:** 7 pt (`sz=14`), cursiva, color `595959` (el mismo estilo que el "Origen:" de la v3).

## Resolución de decisiones pendientes (2026-10-01, modo autónomo)

- **DP1 → se aceptan los textos propuestos** (aseguramiento, notas técnicas, reserva y plantilla del borrador de operaciones). `B9b` se implementa con esos textos. El usuario los revisa en la prueba manual; como son defaults reemplazables por config local, cambiarlos después no requiere código.
- **DP2 → se acepta la redacción propuesta** de las cuatro frases fijas de la v4; el usuario la revisa con la v4 abierta.
- **O1:** los textos se le muestran al usuario en el resumen de cierre de la HU.
