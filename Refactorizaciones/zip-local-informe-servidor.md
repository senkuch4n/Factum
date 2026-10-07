# SDD: La evidencia y su ZIP quedan en la PC del perito; en el servidor solo se guarda el informe

**Slug:** `zip-local-informe-servidor` · **Issue:** #9
**HU:** `docs/hu-zip-local-informe-servidor.md`. Validada por el usuario el 2026-10-06. Se aplican las opciones
recomendadas de D1–D5, D7 y D9–D13; D6 y D8 quedan como dice la sección "Validación del usuario", que manda sobre el
resto del documento.
**Base de código:** `feat/zip-local-informe-servidor` (sale de `develop`, `dab5e4b`).

> **Decisiones del usuario (2026-10-06):** el usuario resolvió las 4 decisiones que dejó abierta la primera versión
> de esta SDD (§12): DP1 = A, DP2 = A, DP3 = A y DP4 = ruta fija `C:\Factum\Evidencia`. Ya están aplicadas en todo el
> documento. No queda nada abierto antes de implementar.

---

## 1. Resumen funcional

En un caso nuevo, Tatana pasa a guardar la evidencia en la PC del perito:

- las capturas, grabaciones y archivos del celular se mueven de la carpeta plana de Tatana a una carpeta del caso
  (`<Agent:DataDirectory>/cases/<id>/`);
- los `Blob` del navegador (cámara externa, adjuntos de la PC) se suben a esa misma carpeta por `localhost`;
- de cada archivo se calcula el SHA-256, y el backend registra en Mongo un **manifiesto**: nombre, tamaño, SHA-256,
  ruta de origen y en qué PC está. Ningún byte de evidencia llega al servidor.

Al generar, el flujo pasa por tres etapas:

1. El backend valida el caso contra el manifiesto, genera la contraseña y abre un "intento de generación".
2. Tatana arma el ZIP AES-256 en `<Agent:EvidenceDirectory>/<causa>_<id8>/` (default `C:\Factum\Evidencia` en
   Windows, §4.2) con un nombre provisorio. Antes lo verifica contra los SHA-256 del manifiesto y después calcula el
   hash del ZIP.
3. El navegador le manda al backend el hash del ZIP, la ubicación y **solo las capturas que el informe embebe**, en
   una única request. El backend arma el DOCX con las capturas en una carpeta temporal que borra al terminar, guarda
   solo el DOCX y los metadatos, y marca el caso `completed`.

Recién entonces Tatana pasa el ZIP a su nombre final y borra los archivos sueltos.

En el resultado se ven dos bloques: **Informe**, que se descarga del servidor, y **ZIP en esta PC**, con "Mostrar en
carpeta" y "Guardar una copia…". Desde otra PC se ve "Guardado en <PC> · <ruta>".

Los casos y borradores viejos que tienen la evidencia en el servidor siguen con el flujo viejo hasta cerrarse (D8).
Backend y Tatana aceptan solo los orígenes configurados (D9). Tatana además rechaza del lado del servidor cualquier
request de un origen no permitido, incluido el WebSocket.

## 2. Toca

- **backend (API): sí** — `Models/Case.cs`, `DTOs/CaseDtos.cs` y `DTOs/EvidenceDtos.cs` (nuevo),
  `Infrastructure/MongoRepository.cs`, `Infrastructure/StorageService.cs` (`.generate-tmp/`),
  `Services/Cases/CaseService.cs`, `Services/Cases/EvidenceManifest.cs` (nuevo),
  `Services/Cases/AgentGeneration.cs` (nuevo), `Services/Reports/ReportService.cs` (separar el DOCX del ZIP),
  `Controllers/CasesController.cs`, `Program.cs` (CORS y limpieza), `appsettings.json`, README. Tests en
  `server/tests/Factum.Backend.Tests`.
- **backend (Tatana): sí** — `Models/AgentModels.cs` (`AgentOptions`), `Program.cs` (guarda de origen, CORS y WS,
  limpieza), `Common/AgentFileNames.cs` y `Common/OriginPolicy.cs` (nuevos), `Services/CaseEvidenceStore.cs`,
  `Services/CaseZipService.cs`, `Services/Evidence/EvidenceZip.cs` y `Services/FolderReveal.cs` (nuevos),
  `Controllers/CaseEvidenceController.cs` y `Controllers/CaseZipController.cs` (nuevos),
  `Controllers/HealthController.cs`, `Controllers/WebSocketController.cs`, `Factum.Agent.csproj` (SharpZipLib).
  Tests en `server/tests/Factum.Agent.Tests`.
- **client: sí** — `lib/agent.ts`, `lib/api.ts`, `types/index.ts`, `lib/agent-messages.ts` (nuevo),
  `hooks/useFileManager.ts`, `hooks/useAgentIdentity.ts` (nuevo), `lib/report-images.ts`, `components/CaptureStep.tsx`,
  `components/capture/{gallery.ts,EvidenceTray.tsx,UploadProgressPanel.tsx}`, `components/VideoCard.tsx`,
  `components/IdentityCard.tsx`, `components/ReportStep.tsx` (aviso), `components/dashboard/GenerateStep.tsx`,
  `components/ResultStep.tsx`, `components/CaseCard.tsx`, `components/CaseGridCard.tsx`, `app/dashboard/page.tsx`.
- **agent-ui: no** — D10 A. Electron lanza Tatana con `--port` y `--data` (`agent-ui/src/main/agent-process.ts`
  L103) y no llama a la API HTTP desde el renderer (solo `http.get` desde main, sin `Origin`). La carpeta de
  evidencia y los orígenes salen de la config de Tatana, con defaults en código.
- **deploy/windows: no** — la instalación local usa `http://localhost:3000`, que está en los defaults de CORS de los
  dos lados. Sumar el dominio de la nube es parte de la HU de despliegue.

## 3. Modelo de datos

### 3.1 MongoDB — colección `cases`

El driver guarda los nombres de propiedad **tal cual** (PascalCase): no hay `CamelCaseElementNameConvention`. Lo
confirmé en `factum_dev` (`Status`, `ZipFilename`, `CaptureRoles`, …). Los campos nuevos siguen esa regla.

| Campo Mongo | Tipo C# | Default (doc. viejo) | JSON | Qué es |
|---|---|---|---|---|
| `EvidenceStorage` | `string?` (`"agent"` \| `"server"`), `[BsonIgnoreIfNull]` | `null` | `evidence_storage` (siempre resuelto, ver §3.2) | Marca de flujo por caso (D8) |
| `Evidence` | `List<EvidenceItem>` | `[]` | `evidence` | Manifiesto del flujo nuevo |
| `EvidenceHost` | `EvidenceHost?` | `null` | `evidence_host` | PC donde está la evidencia (D7) |
| `ZipLocation` | `ZipLocation?` | `null` | `zip_location` | Dónde quedó el ZIP (D6/D7) |
| `PendingGeneration` | `PendingGeneration?`, `[JsonIgnore]` | `null` | — (nunca sale) | Intento de generación abierto (§5.6) |

```csharp
[BsonIgnoreExtraElements]
public sealed class EvidenceItem
{
    public string Filename { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Sha256 { get; set; } = string.Empty;     // 64 hex en minúscula
    public string? SourcePath { get; set; }                 // ruta en el celular, solo explorador
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public sealed class EvidenceHost
{
    public string Hostname { get; set; } = string.Empty;    // Tatana /info.hostname (Environment.MachineName)
    public string OsUser { get; set; } = string.Empty;      // Tatana /info.os_user
    public string AgentVersion { get; set; } = string.Empty;
    public string CaseDirectory { get; set; } = string.Empty; // ruta absoluta de la carpeta de trabajo en esa PC
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public sealed class ZipLocation
{
    public string Hostname { get; set; } = string.Empty;
    public string Directory { get; set; } = string.Empty;   // carpeta final del ZIP en esa PC
    public string Path { get; set; } = string.Empty;        // ruta absoluta del ZIP
}

[BsonIgnoreExtraElements]
public sealed class PendingGeneration
{
    public string Id { get; set; } = string.Empty;          // Guid "N"
    public string? Password { get; set; }                   // null si Report:EncryptZip=false
    public string ZipFilename { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class EvidenceStorages { public const string Agent = "agent"; public const string Server = "server"; }
```

- **Rol de captura:** sigue viviendo solo en `CaptureRoles`. No se duplica en el manifiesto (D-T4). En el flujo nuevo,
  `PUT capture-roles` valida contra el manifiesto (§5.4).
- **Ruta de origen:** se guarda en `EvidenceItem.SourcePath` y **además** se agrega a `FileSources`, igual que
  `AddFileSourceAsync` hoy. Así `EvidenceClassifier`, `ReportValues.Counts` y `ReportService` (que leen
  `cas.FileSources`) siguen andando sin cambios.
- **Índices:** no hay nuevos.
- **Compatibilidad:** todo es anulable o tiene default `[]`, y las clases nuevas llevan `[BsonIgnoreExtraElements]`.
  Un documento viejo deserializa sin migración. **No se reescribe ningún documento existente**: los campos nuevos se
  escriben solo cuando el dueño hace una acción del flujo nuevo sobre su propio caso. `factum_dev` hoy tiene 3 casos,
  los 3 `completed`, sin ninguno de estos campos.
- El repositorio de casos nunca hace `ReplaceOne`: solo `$set`, `$push` y `$pull` puntuales (ya verificado). Por eso
  completar `EvidenceStorage` en memoria (§3.2) no lo persiste.

### 3.2 Flujo efectivo por caso (D8)

`EvidenceManifest.ResolveStorage(Case cas, Func<string, bool> hasServerEvidence) : string`:

1. Si `cas.EvidenceStorage` no es null, devuelve ese valor.
2. Si `cas.Status == Completed`, devuelve `"server"`: es un caso viejo generado con el ZIP en el servidor.
3. Si `hasServerEvidence(cas.Id)`, devuelve `"server"`: es un borrador viejo con evidencia subida.
   `hasServerEvidence` es `StorageService.HasEvidenceFiles(caseId)`: la carpeta `cases/<id>/` existe y tiene al menos
   un archivo que no es `ReportService.IsGeneratedArtifact`.
4. En cualquier otro caso devuelve `"agent"`.

- `CreateAsync` escribe `EvidenceStorage = "agent"` en todo caso nuevo.
- El primer `PUT …/evidence` escribe `"agent"` en un borrador viejo sin evidencia en el servidor.
- `CaseService` llama a `ResolveStorage` y completa `cas.EvidenceStorage` **en memoria** antes de devolver cualquier
  `Case`: list, get, create, update y generate. Así el JSON trae siempre `"agent"` o `"server"`.

### 3.3 Disco del backend (`Storage:DataDirectory`)

- `cases/<id>/`: en el flujo nuevo solo recibe `informe_pericial_<ref>.docx` (D11 A). En el flujo viejo, igual que
  hoy.
- **Nueva:** `.generate-tmp/<generationId>/`, hermana de `.upload-tmp/`. Contiene las capturas recibidas en
  `generate/finish` mientras se arma el DOCX y se borra entera en un `finally` (§5.7). `CleanupOrphanGenerations()`
  la vacía al arrancar, igual que `CleanupOrphanUploads`. Al terminar cada generación no queda nada de evidencia en
  el servidor.
- No se mueve, renombra ni borra **nada** de lo que ya hay en `cases/`.

### 3.4 Disco de Tatana (PC del perito)

Es la lectura literal de la validación (D3 `agent-data/cases/<id>/` + D6 carpeta propia del ZIP), confirmada por el
usuario en DP2 A. La carpeta base del ZIP es la de DP4: `C:\Factum\Evidencia` en Windows y `~/Factum/Evidencia` en
macOS/Linux, nunca Documentos (§4.2).

```
<Agent:DataDirectory>/                       (default ./agent-data; agent-ui pasa userData/agent-data)
├── screenshot_….png, grabacion_….mkv, …     raíz plana de siempre: capturas recién hechas
├── .upload-tmp/<caseId>.<guid32>.part       subidas del navegador en curso (se limpia al arrancar)
└── cases/<caseId>/                          carpeta de trabajo del caso: archivos sueltos del manifiesto
<Agent:EvidenceDirectory>/                   (default C:\Factum\Evidencia en Windows; ~/Factum/Evidencia en macOS/Linux)
└── <ref>_<id8>/                             carpeta final del caso (D6)
    ├── evidencia_<ref>.zip                  ZIP final, después del commit
    └── .factum/
        ├── pendiente/evidencia_<ref>.zip    ZIP provisorio, verificado, todavía sin registrar
        ├── pendiente/evidencia_<ref>.zip.sha256
        └── estado.json                      { "zip_filename", "zip_hash", "committed_at" } después del commit
```

- `<caseId>` tiene que pasar `Guid.TryParseExact(id, "D")`, porque los ids de caso son `Guid.ToString()`. Si no pasa,
  la respuesta es 400 `invalid_case_id`. Es la defensa de rutas.
- `<ref>_<id8>` (nombre aprobado por el usuario en DP2, p. ej. `1234_2026_3f2a9c1e`) sale de
  `AgentFileNames.CaseFolderName(caseRef, caseId)`:
  - toma `nro_referencia` saneado con la misma regla que `ReportService.Sanitize`: lo que no es letra ni dígito pasa a
    `_`, se hace `Trim('_')`, se corta en 60 caracteres y, si queda vacío, vale `"caso"`;
  - le agrega `_` y los primeros 8 caracteres del id.
  - Es determinística: el navegador siempre manda `case_ref` y Tatana recalcula la carpeta. Nunca se acepta una ruta
    del cliente.
- Los archivos sueltos se **mueven** (`File.Move`, mismo volumen) de la raíz a `cases/<id>/`. Es un rename atómico y
  no duplica GB.
- El ZIP se escribe directamente en el volumen de `EvidenceDirectory` (en `.factum/pendiente/`). Por eso el commit
  también es un rename en el mismo volumen.

## 4. Configuración

### 4.1 Backend

| Clave | Env | Tipo | Default | Regla |
|---|---|---|---|---|
| `Cors:AllowedOrigins` | `Cors__AllowedOrigins__0`, `__1`, … | `string[]` | `["http://localhost:3000", "http://127.0.0.1:3000"]` si falta o está vacía | Cada valor es un origen absoluto `http`/`https` sin path, query ni fragmento; se le saca la `/` final. `"*"` o un valor inválido suma un error a `configErrors` y el backend no arranca. |
| `Report:MaxGenerateUploadBytes` | `Report__MaxGenerateUploadBytes` | `long` | `268435456` (256 MiB) | `> 0`. Tope del cuerpo de `generate/finish`. |

Política CORS: `WithOrigins(lista).AllowAnyHeader().AllowAnyMethod()`, sin credenciales (el token va en el header
`Authorization`). Al arrancar se loguea en `Information`: `"CORS: orígenes permitidos {Lista}"`.

### 4.2 Tatana (`Agent:*`, enlazado a `AgentOptions`)

| Clave | Env | Tipo | Default |
|---|---|---|---|
| `Agent:EvidenceDirectory` | `Agent__EvidenceDirectory` | `string?` | `null` → default por SO (DP4, ver abajo). |
| `Agent:AllowedOrigins` | `Agent__AllowedOrigins__0`, … | `string[]` | `["http://localhost:3000", "http://127.0.0.1:3000"]` |
| `Agent:MaxUploadBytes` | `Agent__MaxUploadBytes` | `long` | `17179869184` (16 GiB) |
| `Agent:MinFreeBytes` | `Agent__MinFreeBytes` | `long` | `1073741824` (1 GiB) |

- **Default de `Agent:EvidenceDirectory` (DP4, decisión del usuario: ruta fija, nunca Documentos):**
  - **Windows:** `C:\Factum\Evidencia`. En código:
    `Path.Combine((Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + "\\", "Factum", "Evidencia")`.
  - **macOS y Linux (desarrollo):** `~/Factum/Evidencia`, es decir
    `Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), "Factum", "Evidencia")`. Queda fuera de
    `~/Documents` y de iCloud Drive (`~/Library/Mobile Documents`). Si `UserProfile` viene vacío, cae a
    `<DataDirectory>/evidencia`.
  - Nunca se usa `SpecialFolder.MyDocuments`.
  - Una ruta relativa en la config se resuelve con `Path.GetFullPath` contra el directorio de trabajo de Tatana.
  - La carpeta se crea al primer `POST /cases/{id}/zip` (no al arrancar). Si no se puede crear (permisos), 500
    `storage_error` con el mensaje "No se pudo crear la carpeta de evidencia {ruta}".
- Las reglas de validación son las mismas que en el backend. Un error lanza `InvalidOperationException` al arrancar,
  igual que `Agent:BindAddress` inválido.
- Se loguea en `Information`: `"Evidencia: carpeta de trabajo {DataDirectory}/cases, ZIP en {EvidenceDirectory}; orígenes permitidos {Lista}"`.
- **Chequeo de sincronización (DP4, se mantiene como aviso):** `AgentFileNames.IsSyncedFolder(path)` da `true` si la
  ruta resuelta contiene un segmento que empieza con `OneDrive` (sin distinguir mayúsculas), o contiene
  `Mobile Documents` o `iCloud Drive`. En ese caso Tatana loguea un `Warning` al arrancar ("La carpeta de evidencia
  {ruta} está dentro de una carpeta sincronizada con la nube; el ZIP se subiría a ese servicio. Configurá
  Agent:EvidenceDirectory fuera de OneDrive/iCloud.") y `/info` informa `evidence_directory_synced: true`, que la web
  muestra (§7.7). No cambia la carpeta.
- **No commitear** el `"Mock": true` que hoy está modificado y sin commitear en
  `server/src/Factum.Agent/appsettings.json`: es config local del usuario. Los defaults nuevos van en código y no en
  ese archivo.

## 5. Backend (API) — endpoints y diseño

Todos los endpoints van con `[Authorize]` y validan el dueño con `LoadOwnedAsync` (404 sin caso, 403 si es ajeno).
JSON en `SnakeCaseLower` (`Program.cs` L74-83). Los errores nuevos usan
`Result.Fail(kind, error, details)` y `ErrorResult`, que ya existen desde `subida-archivos-grandes`: el body es
`{ "error", "code", … }` con claves literales en snake_case.

### 5.1 Códigos de error nuevos (`Services/Cases/EvidenceManifest.cs`, `static class EvidenceErrorCodes`)

| `code` | HTTP | Cuándo | Extra |
|---|---|---|---|
| `evidence_on_agent` | 409 | Endpoint del flujo viejo (`POST /files`, `upload-check`, `POST /generate`) sobre un caso `agent` | — |
| `evidence_on_server` | 409 | Endpoint del flujo nuevo sobre un caso `server` | — |
| `evidence_on_other_pc` | 409 | `hostname` ≠ `EvidenceHost.Hostname` con el manifiesto no vacío | `evidence_hostname` |
| `case_not_editable` | 409 | Caso `generating`/`completed` (ya existe) | — |
| `invalid_manifest` | 400 | Ítem inválido: nombre (`EvidenceUpload.IsValidUploadName` y no `IsGeneratedArtifact`), `sha256` que no es `^[0-9a-f]{64}$`, `size < 0`, nombre repetido o `host.hostname` vacío | `filename` (si aplica) |
| `no_evidence` | 400 | `prepare` con el manifiesto vacío | — |
| `generation_stale` | 409 | `finish` con un `generation_id` que no es el `PendingGeneration.Id` actual | — |
| `manifest_mismatch` | 409 | Los `files` que verificó Tatana no son exactamente el manifiesto (mismo conjunto, `size` y `sha256`) | `mismatched: string[]` |
| `missing_images` | 400 | Faltan capturas que el informe embebe | `missing_images: string[]` |
| `image_hash_mismatch` | 400 | El SHA-256 de una captura recibida no coincide con el manifiesto | `filename` |
| `invalid_zip_info` | 400 | `zip_hash` no es 64 hex, `zip_filename` ≠ el del intento, `encrypted` no coincide con el intento, `zip_location` incompleta | — |
| `request_too_large` | 413 | El cuerpo de `finish` supera `Report:MaxGenerateUploadBytes` | `max_bytes` |

La validación de datos del caso sigue respondiendo como hoy: 400 `{ error, missing }` con
`CaseValidation.MissingMessage`.

### 5.2 `PUT /api/cases/{id}/evidence` (nuevo) — registrar archivos guardados en Tatana

Body `RegisterEvidenceRequest`:

```json
{ "host": { "hostname": "PC-PERITO-01", "os_user": "jperez", "agent_version": "2.1.0",
            "case_directory": "C:\\Users\\jperez\\AppData\\Roaming\\Tatana\\agent-data\\cases\\<id>" },
  "items": [ { "filename": "screenshot_20261006_101500_123.png", "size": 1234567,
               "sha256": "<64 hex>", "source_path": null } ] }
```

Orden de chequeos (el primero que falla gana):

1. 404/403.
2. No editable → 409 `case_not_editable`.
3. `ResolveStorage == "server"` → 409 `evidence_on_server`.
4. Ítems o host inválidos → 400 `invalid_manifest`.
5. `Evidence` no vacío y `!string.Equals(host.hostname, EvidenceHost.Hostname, OrdinalIgnoreCase)` → 409
   `evidence_on_other_pc`.

Después se hace un upsert por `Filename` (un ítem con el mismo nombre se reemplaza) y se agregan a `FileSources` los
`source_path` no vacíos que todavía no estén.

- La escritura es `RegisterEvidenceAsync`, con filtro `Editable(id)` y `EvidenceStorage != "server"`, y hace `$set`
  de `Evidence`, `EvidenceHost`, `EvidenceStorage = "agent"` y `FileSources`. Si el filtro no matchea → 409
  `case_not_editable`.
- `EvidenceHost` se fija en el primer registro, y también si el manifiesto estaba vacío. En los siguientes solo se
  actualizan `AgentVersion` y `CaseDirectory`.
- Respuesta 200 `EvidenceResponse`: `{ "evidence": [EvidenceItem…], "evidence_host": {…} }`.

### 5.3 `DELETE /api/cases/{id}/evidence/{filename}` (nuevo)

- Chequeos 1-3 de §5.2 y nombre plano (`ReportImageRef.IsPlainName`).
- Hace `$pull` del ítem de `Evidence`, de su `CaptureRoles` y de su `FileSources`. Si `Evidence` queda vacío, hace
  `$unset` de `EvidenceHost`.
- Si el nombre no está en el manifiesto → 404 `{ error: "Archivo no encontrado" }`.
- Respuesta 200 `EvidenceResponse`.
- El backend nunca toca Tatana: el navegador borra el archivo de la PC después (§7.4).

### 5.4 Endpoints existentes que cambian según el flujo

Con el caso en `agent`:

| Endpoint | Comportamiento |
|---|---|
| `GET /api/cases`, `GET /api/cases/{id}`, `PUT /api/cases/{id}`, `POST /api/cases` | `Case` con `evidence_storage` resuelto, `evidence`, `evidence_host` y `zip_location`. El `files` de `GET {id}` sale del manifiesto (`EvidenceManifest.ToFileInfos(cas)`: `FileInfoDto(Filename, Size, Sha256, RegisteredAt, SourcePath)`). |
| `GET /api/cases/{id}/files` | Manifiesto (ídem). |
| `GET /api/cases/{id}/report-texts/defaults` | `ReportValues.RenderDefaults(cas, settings, manifiesto)`. |
| `PUT /api/cases/{id}/capture-roles` | En vez de `File.Exists` en disco, valida que el nombre esté en el manifiesto con `Size > 0`. La clasificación `Screenshot` queda igual. |
| `GET /api/cases/{id}/report-images` | Ítems del manifiesto con `ReportImageRef.IsInsertableName`, en orden ordinal: `available = Size > 0`, `width`/`height` = null, `role` de `CaptureRoles`. El cliente cruza `available` con lo que tiene Tatana (§7.5). |
| `GET /api/cases/{id}/files/{f}/preview` | Sin cambios: no hay archivos en el servidor y responde 404 "Imagen no disponible". El cliente no lo llama en el flujo nuevo. |
| `POST /api/cases/{id}/files`, `GET …/files/upload-check` | 409 `evidence_on_agent` antes de leer el cuerpo: se evalúa justo después de `LoadOwnedAsync`, dentro de `PrecheckUploadAsync`. |
| `POST /api/cases/{id}/generate` (flujo viejo) | 409 `evidence_on_agent`. |
| `GET /api/cases/{id}/download/{file}` | Sin cambios. El DOCX está y el ZIP no (404). |
| `GET /api/cases/{id}/zip-password` | Sin cambios. |

Con el caso en `server` (flujo viejo), todo queda **exactamente** como hoy.

### 5.5 `POST /api/cases/{id}/generate/prepare` (nuevo)

Body `PrepareGenerationRequest`: `{ "hostname": "PC-PERITO-01" }`.

Orden:

1. 404/403.
2. `Completed` → 409 `"El caso ya fue generado"` (como hoy).
3. `Generating` → 409 `case_not_editable`.
4. `SchemaVersion == 0` → 400 `LegacyCaseMessage`.
5. `server` → 409 `evidence_on_server`.
6. Manifiesto vacío → 400 `no_evidence` ("El caso no tiene archivos. Capturá evidencia primero.").
7. `hostname` ≠ `EvidenceHost.Hostname` → 409 `evidence_on_other_pc`.
8. `CaseValidation.ValidateForGenerate(cas, hasImeiCapture)`: hay un `CaptureRole` `imei_modelo` cuyo `Filename`
   está en el manifiesto con `Size > 0`. Además, `BrokenImageKeys(cas.ReportTexts, n => manifiesto tiene n && IsInsertableName(n) && Size > 0)`.
   Si falta algo → 400 `{ error, missing }`.
9. Crea `PendingGeneration`:
   - `Id = Guid.NewGuid().ToString("N")`;
   - `Password = settings.EncryptZip ? ReportService.GeneratePassword() : null` (se pasa de `private` a
     `internal static`);
   - `ZipFilename = ReportService.ZipFilenameFor(cas)`, helper nuevo: `$"evidencia_{Sanitize($"{NroReferencia}_{NombreDenunciante}")}.zip"`,
     la misma fórmula que hoy;
   - se escribe con `SetPendingGenerationAsync` (filtro `Editable`) y **pisa** cualquier intento anterior.
10. Respuesta 200 `PrepareGenerationResponse`:

```json
{ "generation_id": "<32 hex>", "zip_filename": "evidencia_1234_titular.zip", "case_ref": "1234",
  "password": "ABCD…" | null, "encrypted": true,
  "files": [ { "filename", "size", "sha256" } ],
  "report_images": [ "screenshot_….png", … ] }
```

- `files` es el manifiesto en orden ordinal.
- `report_images` son los ítems del manifiesto con `EvidenceClassifier.Classify(name, tieneSource) == Screenshot` y
  `Size > 0`: lo que el informe puede embeber (roles, anexo y cuerpo).
- `case_ref` es `cas.NroReferencia`, lo que Tatana usa para nombrar la carpeta.
- `prepare` **no cambia el `Status`**. Si Tatana falla después, el caso queda como estaba (escenario "Tatana apagado").

### 5.6 `POST /api/cases/{id}/generate/finish` (nuevo) — multipart

Va con `[DisableFormValueModelBinding]`, que ya existe. Antes de leer el cuerpo se fija
`IHttpMaxRequestBodySizeFeature.MaxRequestBodySize = Report:MaxGenerateUploadBytes`. Si `Content-Length` lo supera →
413 `request_too_large` sin leer nada.

El cuerpo es `multipart/form-data` y se lee con `MultipartReader` en streaming, sin `ReadFormAsync` ni `IFormFile`
(D-T6):

- **1.ª sección**, `name="metadata"`, `application/json`, ≤ 1 MiB:

```json
{ "generation_id": "<32 hex>", "zip_filename": "evidencia_1234_titular.zip",
  "zip_hash": "<64 hex>", "zip_size": 123456789, "encrypted": true,
  "zip_location": { "hostname": "PC-PERITO-01", "directory": "C:\\Users\\…\\Factum\\Evidencia\\1234_3f2a9c1e",
                    "path": "C:\\Users\\…\\Factum\\Evidencia\\1234_3f2a9c1e\\evidencia_1234_titular.zip" },
  "files": [ { "filename", "size", "sha256" } ] }
```

- **Secciones siguientes**, `name="images"`, `filename="<nombre de evidencia>"`: los bytes tal cual. El `filename`
  tiene que estar en `report_images`; si no, o si viene repetido → 400 `invalid_manifest`.

Pasos:

1. 404/403. `Completed` → 409. `Generating` → 409 `case_not_editable`. `server` → 409 `evidence_on_server`.
2. Metadata:
   - `generation_id` ≠ `PendingGeneration?.Id` → 409 `generation_stale`;
   - `zip_*`, `encrypted` (tiene que valer `PendingGeneration.Password != null`) o `zip_location` inválidos → 400
     `invalid_zip_info`;
   - `zip_location.hostname` ≠ `EvidenceHost.Hostname` → 409 `evidence_on_other_pc`.
3. `files` contra `Evidence`: mismo conjunto de nombres y, para cada uno, mismo `size` y `sha256` → si no, 409
   `manifest_mismatch` con la lista de nombres que difieren (faltan, sobran o cambiaron).
4. Imágenes: `tmp = <DataDirectory>/.generate-tmp/<generationId>/` (si existe, se borra antes). Cada sección se
   escribe a `tmp/<filename>` con SHA-256 incremental y un contador total contra el tope:
   - si se supera el tope → 413;
   - si el hash ≠ manifiesto → 400 `image_hash_mismatch`.
5. Si falta alguna de `report_images` → 400 `missing_images`.
6. Revalidación con bytes reales: `hasImeiCapture` = `ReportImageFiles.Inspect(tmp, f).IsAvailable` para la captura
   `imei_modelo`, y `BrokenImageKeys` con `Inspect(tmp, n).IsAvailable` → 400 `{ error, missing }`.

   **Hasta acá el `Status` no cambió.**
7. `TryMarkGeneratingAsync(id, generationId)`, con filtro `Editable(id)` y `PendingGeneration.Id == generationId`,
   pone `Status = Generating`. Si no matchea → 409 `generation_stale`.
8. `ReportService.GenerateReportAsync(cas, evidencia (manifiesto como FileInfoDto, orden ordinal), hashes (manifiesto), imagesDir: tmp, outputDir: CaseDir(id), zipFilename, zipHash)`
   → `(PdfPath, PdfFilename, ReportHash)`.
9. `CompleteAgentGenerationAsync` pone, en un solo `$set`:
   - `Status = Completed`, `GeneratedAt`;
   - `ZipPassword = PendingGeneration.Password`, `ZipEncrypted = Password != null`,
     `ZipEncryption = Password != null ? "aes256-ae2" : null`;
   - `ZipHash`, `ZipFilename`, `PdfFilename`, `ReportHash`, `ZipLocation`;

   y hace `$unset` de `PendingGeneration`.
10. Respuesta 200 `GenerateResponse`, con la misma forma de hoy más `zip_location`:
    `{ "case", "zip_hash", "password", "files": { "zip", "pdf" }, "report_hash", "zip_location" }`.

**Errores y limpieza:**

- Cualquier excepción en los pasos 8-9 lleva `Status = Error` (como hoy), borra el DOCX de este intento (lo hace
  `GenerateReportAsync`) y responde 400 `Error generando informe: …` (`Result.Fail`, como hoy).
- Un `finally` del action/servicio borra `tmp` **siempre**: con éxito, con error o con cancelación.
  - Es `Directory.Delete(tmp, recursive: true)` dentro de `try`. Si falla, se loguea un `Warning` y lo limpia el
    próximo arranque.
  - Las capturas recibidas nunca llegan a `cases/<id>/`.
- Ninguna excepción sale del action sin JSON: el patrón de `UploadFileAsync` atrapa todo y lo traduce a `Result`.

### 5.7 `ReportService` — separar el DOCX del ZIP

- **`GenerateAsync(cas, files, caseDir, ct)` (flujo viejo): sin cambios de comportamiento.** Por dentro pasa `caseDir`
  como `imagesDir` y como `outputDir`.
- **Nuevo** en `IReportService`:

```csharp
Task<ReportOnlyResult> GenerateReportAsync(Case cas, List<FileInfoDto> evidence,
    IReadOnlyDictionary<string, string> hashes, string imagesDir, string outputDir,
    string zipFilename, string zipHash, CancellationToken ct = default);
public sealed record ReportOnlyResult(string PdfPath, string PdfFilename, string ReportHash);
```

  1. Borra los restos con los nombres de **este** caso en `outputDir`: `informe_pericial_<safe>.docx/.pdf` e
     `informe_forense_<safe>.docx/.pdf`. **No** toca ningún `evidencia_*.zip`.
  2. Ordena `evidence` en orden ordinal.
  3. Llama a `GenerateDocxAsync(cas, evidence, hashes, docxPath, imagesDir, zipFilename, zipHash, branding.Current, ct)`.
  4. Calcula el SHA-256 del DOCX.
  5. Ante una excepción borra el DOCX y relanza.
- `GenerateDocxAsync`: el parámetro `caseDir` pasa a llamarse `imagesDir`, sin cambiar su uso (`IsUsableImage`,
  `ReplaceWithImages`, `InsertBodyImages`).
- `hashes` pasa de `Dictionary` a `IReadOnlyDictionary`.
- `ZipFilenameFor(Case)` es `internal static` y lo usan los dos flujos.

### 5.8 `StorageService`

- Se suman `bool HasEvidenceFiles(string caseId)` y `string NewGenerationTempDir(string generationId)` (crea
  `.generate-tmp/<id>/`), más `int CleanupOrphanGenerations()`, que borra solo **dentro** de `.generate-tmp/`.
- `HasEvidenceFiles` enumera `cases/<id>/` **sin crearla**: no llama a `CaseDir`, que hace `CreateDirectory`.
- `Program.cs` llama a `CleanupOrphanGenerations()` junto a `CleanupOrphanUploads()` y suma el conteo a la línea de
  log existente.

### 5.9 CORS del backend (`Program.cs` L93-95)

Se reemplaza `AllowAnyOrigin()` por la política de §4.1. La lectura y validación de `Cors:AllowedOrigins` va en un
helper puro `Infrastructure/CorsOrigins.cs` (`Parse(IConfiguration) → (IReadOnlyList<string> Origins, List<string> Errors)`),
testeable. Los errores se suman a `configErrors`.

## 6. Tatana (`server/src/Factum.Agent`) — endpoints y diseño

JSON en `SnakeCaseLower` con `WhenWritingNull`: los `null` **no viajan**, así que el cliente los trata como opcionales.
Errores `{ "error", "code", … }`. Todas las rutas nuevas van bajo `/cases/{caseId}` (Guid "D"; si no → 400
`invalid_case_id`).

### 6.1 Guarda de origen, CORS y WebSocket (`Common/OriginPolicy.cs` + `Program.cs`)

Se reemplaza el middleware actual (Program.cs L113-133, que pone `Access-Control-Allow-Origin: *`) y
`AddCors`/`UseCors(AllowAnyOrigin)`:

1. `origin = Request.Headers.Origin`.
2. Si hay `origin` y **no** está en `Agent:AllowedOrigins` (comparación ordinal case-insensitive, sin `/` final; `null`
   cuenta como no permitido) → **403** `{ error: "Origen no permitido", code: "origin_not_allowed" }` sin headers
   CORS. Esto vale también para `OPTIONS` y para `/ws`. **El rechazo es del lado del servidor**: un `POST` "simple"
   desde otra página no llega a ejecutar nada (D-T7).
3. Si hay `origin` permitido, se agregan antes de `next()`:
   - `Access-Control-Allow-Origin: <origin>` y `Vary: Origin`;
   - `Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS`;
   - `Access-Control-Allow-Headers: Content-Type`;
   - `Access-Control-Max-Age: 600`;
   - en un preflight con `Access-Control-Request-Private-Network: true`, también
     `Access-Control-Allow-Private-Network: true` (compatibilidad con Private Network Access y Local Network Access,
     §9.3);
   - `OPTIONS` → 204.
4. Sin `Origin` (curl, el main de Electron, navegación directa, `<img>`), la request pasa.
5. Se conserva el `try/catch` que responde 500 JSON con los headers ya escritos.

`/ws`: el handshake del navegador siempre trae `Origin`, así que lo cubre el paso 2. Los mensajes actuales no
cambian. Se suma **un evento nuevo**, `zip_progress` (§6.4).

`/health` suma `capabilities: ["case_evidence_v1"]`. El cliente lo usa para detectar un Tatana viejo (§7.1).

`/info` suma `evidence_directory` (ruta resuelta) y `evidence_directory_synced` (resultado de
`AgentFileNames.IsSyncedFolder`, DP4).

### 6.2 Guardar evidencia en el caso (`Services/CaseEvidenceStore.cs`, `Controllers/CaseEvidenceController.cs`)

Respuesta común `EvidenceFileInfo`: `{ "filename", "size", "sha256", "saved_at" }`.

| Método y ruta | Qué hace | Errores |
|---|---|---|
| `POST /cases/{id}/evidence/import` body `{ "filename" }` | Si `filename` está en la raíz de `DataDirectory`, hace `File.Move` a `cases/<id>/` sin pisar: si el destino ya existe y la raíz también → 409 `file_exists`. Si ya está en `cases/<id>/` y no en la raíz, es idempotente. Después calcula el SHA-256 en streaming y devuelve `EvidenceFileInfo`. | 400 `invalid_filename`; 404 `file_not_found` (ni en la raíz ni en el caso); 409 `file_busy` (`IOException` por archivo en uso, típico en Windows mientras ffmpeg genera variantes); 500 `storage_error` |
| `GET /cases/{id}/evidence/upload-check?filename=&size=` | Mismas reglas que el backend (§4.1 de `subida-archivos-grandes`) contra el volumen de `DataDirectory`, con `MaxUploadBytes` y `MinFreeBytes`. 200 `{ "max_upload_bytes" }`. | 400 `invalid_filename` / `length_required`; 413 `file_too_large` (+`size`, `max_upload_bytes`); 507 `insufficient_storage` (+`size`, `required_bytes`, `available_bytes`) |
| `POST /cases/{id}/evidence/upload?filename=` cuerpo crudo, `Content-Length` obligatorio | Mismo staging atómico que el backend: temporal `.upload-tmp/<caseId>.<guid>.part`, buffer de 1 MiB, SHA-256 incremental, verificación de bytes, `fsync` y `File.Move(..., overwrite: true)` a `cases/<id>/<filename>`. Antes de leer fija `MaxRequestBodySize = MaxUploadBytes`. | Los de `upload-check`, más 400 `incomplete_upload` (+`size`, `received_bytes`) y 500 `storage_error`. Ante cualquier error se borra el temporal. |
| `GET /cases/{id}/files` | `{ "directory": "<ruta abs>", "files": [ { "filename", "size", "modified_at" } ] }`, solo el primer nivel, sin hashes. Si la carpeta no existe: `files: []`. | 400 `invalid_case_id` |
| `GET /cases/{id}/files/{filename}` | Sirve el archivo: `Content-Type` por extensión (`FileExtensionContentTypeProvider`), `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; sandbox`, `Cache-Control: no-store`, `enableRangeProcessing: true` (videos). | 400; 404 `file_not_found` |
| `DELETE /cases/{id}/files/{filename}` | Borra un archivo suelto del caso. | 400; 404 |

- **Nombres:** `AgentFileNames.IsValidName` copia las reglas de `EvidenceUpload.IsValidUploadName`: nombre plano,
  ≤ 255, sin `< > : " | ? *`, sin `.`/espacio final y sin nombres reservados de Windows. Además, no puede ser
  `evidencia_*.zip` ni `informe_*`.
- **Defensa en profundidad:** la ruta final resuelta tiene que tener como directorio exactamente `cases/<id>`.
- Al arrancar, `CleanupOrphanUploads()` borra solo los archivos de `.upload-tmp/`.
- La raíz plana y los endpoints `/files` de hoy **no cambian**: los sigue usando el flujo viejo.

### 6.3 ZIP del caso (`Services/CaseZipService.cs`, `Controllers/CaseZipController.cs`)

`final = <EvidenceDirectory>/<CaseFolderName(case_ref, id)>/`. En todas las rutas, `zip_filename` tiene que matchear
`^evidencia_[\p{L}\p{N}_]{1,60}\.zip$` → si no, 400 `invalid_filename`.

**`POST /cases/{id}/zip`**. Body:

```json
{ "case_ref": "1234", "zip_filename": "evidencia_1234_titular.zip", "password": "…" | null,
  "files": [ { "filename", "size", "sha256" } ] }
```

1. Un solo ZIP por caso a la vez (`SemaphoreSlim` por `caseId`, `WaitAsync(0)`) → si está tomado, 409
   `zip_in_progress`.
2. Si en `final` ya hay un `estado.json` con este `zip_filename` → 409 `zip_already_committed` (el caso ya se generó
   desde esta PC).
3. Espacio en el volumen de `final`: si `libre < Σ size + MinFreeBytes` → 507 `insufficient_storage`.
4. Verificación de la evidencia, fase `hashing`. Por cada `files[i]`:
   - si no existe en `cases/<id>/` → `missing`;
   - si el tamaño ≠ → `size`;
   - si el SHA-256 ≠ → `hash`.

   Si hay alguno → 409 `evidence_changed` con
   `files: [ { "filename", "reason": "missing" | "size" | "hash" } ]`. Los archivos de la carpeta que no están en
   `files` se ignoran: no entran al ZIP y no se borran.
5. Escribe `final/.factum/pendiente/<zip>.part` con `EvidenceZip.WriteAsync` (fase `zipping`) y lo renombra a
   `pendiente/<zip>`, pisando un pendiente anterior.
6. Calcula `zip_hash` y verifica con `EvidenceZip.VerifyAsync` contra los `sha256` recibidos (fase `verifying`).
7. Escribe `pendiente/<zip>.sha256` con el hash en minúscula.
8. Responde 200:

```json
{ "zip_filename", "zip_hash", "zip_size", "encrypted": true|false, "encryption": "aes256-ae2"|null,
  "directory": "<final>", "zip_path": "<final>/<zip>", "hostname": "<MachineName>",
  "files": [ { "filename", "size", "sha256" } ] }
```

   `zip_path` es la ruta **final**, donde va a quedar después del commit.

Ante cualquier error en los pasos 5-7 se borran `.part`, `pendiente/<zip>` y su `.sha256`, y se responde 500
`zip_failed` (con el mensaje de `EvidenceZipVerificationException` si es de verificación; ese mensaje nunca incluye la
contraseña). La evidencia suelta no se toca.

**`POST /cases/{id}/zip/commit`**. Body `{ "case_ref", "zip_filename", "zip_hash", "delete_files": ["…"] }`:

- **Idempotente:** si ya hay un `estado.json` con el mismo `zip_filename` y `zip_hash` → 200 sin hacer nada (vuelve a
  intentar el borrado de los sueltos que queden).
- Si no hay pendiente → 404 `zip_not_found`.
- Si `pendiente/<zip>.sha256` ≠ `zip_hash` → 409 `zip_hash_mismatch` y no se mueve nada.
- Si no: `File.Move(pendiente/<zip>, final/<zip>, overwrite: false)`. Si el destino existe con otro contenido → 409
  `zip_hash_mismatch`.
- Después escribe `estado.json`, borra el `.sha256` y borra **solo** los `delete_files` que estén en `cases/<id>/`
  (D12). Si la carpeta de trabajo queda vacía, la borra.
- Responde 200 `{ "zip_path", "deleted": n }`.

**`DELETE /cases/{id}/zip/pending?case_ref=&zip_filename=`**: borra el pendiente y su `.sha256`. 204, también si no
había nada.

**`GET /cases/{id}/zip/status?case_ref=&zip_filename=`** → 200:

```json
{ "state": "none" | "pending" | "final", "zip_path", "pending_hash", "committed_hash", "directory" }
```

`pending_hash` viene del `.sha256` y `committed_hash` de `estado.json`. No se recalcula ningún hash.

**`POST /cases/{id}/zip/reveal?case_ref=&zip_filename=`**: abre el administrador de archivos con el ZIP seleccionado
(`Services/FolderReveal.cs`), sin esperar a que termine. Si el ZIP final no existe → 404 `zip_not_found`. 204.

| SO | Comando |
|---|---|
| Windows | `explorer.exe /select,"<zip>"` |
| macOS | `open -R "<zip>"` |
| Linux | `xdg-open "<dir>"` |

Se lanza con `ProcessStartInfo` y `ArgumentList`, nunca con un string con shell.

**`GET /cases/{id}/zip/file?case_ref=&zip_filename=`**: "Guardar una copia…". Sirve el ZIP final con
`Content-Disposition: attachment; filename="<zip>"`, `application/zip` y `enableRangeProcessing`. 404 si no existe.

**`EvidenceZip`:** copia de `Factum.Backend/Services/Reports/EvidenceZip.cs` en
`Factum.Agent/Services/Evidence/EvidenceZip.cs` (namespace `Factum.Agent.Services.Evidence`), con un comentario de
cabecera que apunta a su gemela. Se suma `<PackageReference Include="SharpZipLib" Version="1.4.2" />` al csproj
(D-T1).

### 6.4 Evento WebSocket nuevo

```json
{ "type": "zip_progress", "timestamp": "…",
  "data": { "case_id": "<id>", "phase": "hashing" | "zipping" | "verifying", "done_bytes": 0, "total_bytes": 0 } }
```

Se emite con un throttle de 500 ms por caso y uno forzado al cambiar de fase. Los demás eventos (`screenshot_taken`,
`recording_stopped`, `photo_taken`, `video_variant_ready`, `devices_changed`, …) **no cambian**: siguen apuntando a la
raíz (`/files/<name>`).

## 7. Cliente (`client/`)

### 7.1 `lib/agent.ts`

- **`export class AgentError extends Error`** con `kind: "unreachable" | "http" | "aborted"`, `status` (0 si no es
  http), `code: AgentErrorCode | null` y `body: AgentErrorBody | null`. Todas las funciones nuevas lanzan
  `AgentError`. `fetch`/XHR fallido → `unreachable`.
- **`health()`** → `{ status, version, mock, capabilities?: string[] }`. **`supportsCaseEvidence()`** →
  `capabilities?.includes("case_evidence_v1")`.
- **`getInfo()`**: `AgentInfo` suma `evidence_directory?: string` y `evidence_directory_synced?: boolean`.
- **Evidencia:**
  - `importEvidence(caseId, filename)`;
  - `checkEvidenceUpload(caseId, filename, size, signal?)`;
  - `uploadEvidence(caseId, filename, blob, { signal, onProgress })` con XHR, igual que `api.uploadFile`;
  - `listCaseFiles(caseId)` → `AgentCaseFiles`;
  - `caseFileURL(caseId, filename)` → `${AGENT_URL}/cases/${caseId}/files/${encodeURIComponent(filename)}`;
  - `getCaseFileBlob(caseId, filename, signal?)`;
  - `deleteCaseFile(caseId, filename)`.
- **ZIP:**
  - `buildZip(caseId, body)` con `fetch` sin timeout (puede tardar minutos con GB);
  - `commitZip(caseId, body)`;
  - `discardPendingZip(caseId, caseRef, zipFilename)`;
  - `zipStatus(caseId, caseRef, zipFilename)`;
  - `revealZip(caseId, caseRef, zipFilename)`;
  - `zipFileURL(caseId, caseRef, zipFilename)` (para el `<a href download>`).
- `connectWS`: sin cambios. `AgentEvent` ya es genérico.

### 7.2 `lib/agent-messages.ts` (nuevo, puro)

`agentErrorMessage(err: unknown, ctx: { name?: string; action: "save" | "preview" | "generate" | "reveal" | "copy" }): string`.
Nunca devuelve "Failed to fetch" ni un código HTTP solo.

| Caso | Texto |
|---|---|
| `unreachable`, sin permiso de red local denegado | `Tatana no está corriendo en esta PC: abrilo y reintentá.` |
| `unreachable` y `localNetworkPermission() === "denied"` (§9.3) | `El navegador bloqueó el acceso a Tatana en esta PC. Permitilo en el candado de la barra de direcciones (Configuración del sitio → Acceso a la red local o a apps del dispositivo) y reintentá.` |
| Tatana sin `case_evidence_v1` | `Tu Tatana es de una versión anterior y no puede guardar la evidencia en esta PC. Actualizalo y reintentá.` |
| `origin_not_allowed` | `Tatana no acepta pedidos desde esta página. Pedí que agreguen esta dirección a la configuración de Tatana (Agent:AllowedOrigins).` |
| `file_busy` | `{name} todavía se está procesando en Tatana. Esperá unos segundos y volvé a intentar.` |
| `insufficient_storage` | `No hay espacio en el disco de esta PC para guardar {name} (hace falta {fmt(required)}, quedan {fmt(available)}). Liberá espacio y reintentá.` (sin números: sin el paréntesis) |
| `file_too_large` | `El archivo {name} pesa {fmt(size)} y el máximo que acepta Tatana es {fmt(max)}.` |
| `incomplete_upload` | `Se cortó la copia de {name} a la carpeta del caso; no se guardó nada. Reintentá.` |
| `evidence_changed` | `Algunos archivos de la evidencia cambiaron o faltan en esta PC ({lista}). No se generó nada; revisá la carpeta del caso.` |
| `zip_in_progress` | `Ya se está armando el ZIP de este caso en esta PC.` |
| `zip_failed` | `No se pudo armar o verificar el ZIP en esta PC; la evidencia quedó intacta. Reintentá.` |
| `zip_not_found` | `El ZIP no está en esta PC.` |
| otro `http` | `Tatana respondió con un error: {body.error}` o `Tatana respondió con un error ({status}).` |

`lib/upload-messages.ts` sigue igual para el flujo viejo.

### 7.3 Tipos (`types/index.ts` y `lib/api.ts`)

Ver §8 (Contrato compartido).

`CapturedFile` (en `types/index.ts` y en el duplicado de `components/capture/gallery.ts`; unificar en el de `types`)
suma:

```ts
/** Guardado en la carpeta del caso de Tatana (flujo agent). */
storedInCase?: boolean;
sha256?: string;
size?: number;
/** "missing": está en el manifiesto pero no en este Tatana (otra PC o borrado). */
availability?: "here" | "missing";
```

`uploaded` sigue significando "registrado en el expediente" en los dos flujos.

`UploadPhase` suma `"copying" | "hashing" | "registering"`, que se suman a los de hoy.

### 7.4 `hooks/useFileManager.ts`

Recibe el contexto del caso: `useFileManager()` sigue sin argumentos y las funciones reciben `currentCase`, como hoy.
Estado nuevo:

- `storageMode: "agent" | "server" | null`: se fija al iniciar o retomar un caso, a partir de
  `cas.evidence_storage`;
- `saveQueueRef`: cola secuencial;
- `agentHost: AgentInfo | null`.

**Guardado (flujo `agent`), `saveOne(caseId, file)`:**

1. Si el archivo está en `pendingVariantFiles` (iOS esperando la variante MCI), se saltea y queda pendiente.
2. Si es un `Blob` del navegador (`pendingBlobs`):
   - fase `checking`: `agent.checkEvidenceUpload`;
   - fase `copying`: `agent.uploadEvidence`, con progreso;
   - cuando `loaded === total`, fase `hashing`.

   Si viene de Tatana: fase `hashing`, `agent.importEvidence`.
3. Fase `registering`: `api.registerEvidence(caseId, { host, items: [{ ...info, source_path: file.sourcePath ?? null }] })`.
   `host` sale de `agent.getInfo()`, que se pide una vez por caso, más `case_directory` de `listCaseFiles`.
4. Se marca `uploaded: true, storedInCase: true, sha256, size, availability: "here"`.
   - Si era un `Blob` del navegador: se revoca su blob URL, se saca de `pendingBlobs` y `localBlobs[name].url` pasa a
     `agent.caseFileURL(caseId, name)`. Así un video grande deja de ocupar memoria del navegador.
5. Si quedó una marca de rol local, se persiste con `api.saveCaptureRoles` (la lógica de `setCaptureRole` ya lo hace
   cuando `uploaded`).

**Guardado automático (DP1 A, decisión del usuario).** Cada `addFile`/`handlePhotoBlob` con `storageMode === "agent"` encola
`saveOne`. La cola procesa de a uno. Ante un error, el archivo queda con `uploadStates[name] = "error"` y el mensaje de
`agentErrorMessage` va a `onError`. No reintenta solo, salvo `file_busy`: un reintento a los 5 s, como máximo 3 veces.

El botón **"Guardar evidencia en esta PC y continuar"** (`handleSaveAndContinue`):

- espera que la cola se vacíe;
- reintenta lo que quedó en `error`, mostrando el `UploadProgressPanel`;
- guarda los roles (`PUT capture-roles`, como hoy) y llama a `onSuccess`.


**Flujo viejo:** `handleUploadAndContinue` de hoy se conserva **tal cual**, con el nombre
`handleUploadAndContinueLegacy`, y se usa cuando `storageMode === "server"` (DP3 A, decisión del usuario: los borradores viejos siguen con el flujo viejo completo, incluida la captura y el
envío al servidor). La página elige cuál llamar.

**Quitar un archivo guardado:**

- `removeFile(name, caseId)` con `storedInCase`: `api.deleteEvidence(caseId, name)` y después
  `agent.deleteCaseFile(caseId, name)`. Si falla el primero, no se toca nada y se muestra el error. Si falla el
  segundo, el archivo queda huérfano en la carpeta: queda fuera del manifiesto y del ZIP, y se loguea en consola de
  desarrollo solo en `NODE_ENV !== "production"`.
- Sin `storedInCase`, igual que hoy.
- Bloqueado mientras haya un guardado en curso.

**Reanudar después de una recarga (requisito 1 de D8), `restoreCaseEvidence(cas)`:**

1. `files` = `cas.evidence`, cada uno con `uploaded: true, storedInCase: true, sha256, size, sourcePath` y
   `captureRole` de `cas.capture_roles`.
2. Si Tatana está online y es la misma PC (§7.6):
   - `agent.listCaseFiles(cas.id)`: los del manifiesto que no aparecen quedan como `availability: "missing"`, el
     resto como `"here"`;
   - los archivos de la carpeta del caso que **no** están en el manifiesto (se movieron pero no se llegaron a
     registrar antes de la recarga) se agregan con `uploaded: false, storedInCase: true` y se encolan. `import` es
     idempotente: recalcula el hash y registra.
3. Si no es la misma PC o Tatana no responde, todos quedan `"missing"` y se muestra el aviso de §7.6.

`page.tsx` → `proceedResume(cas)` llama a `clearFiles()`. Después, si `cas.evidence_storage === "agent"`, pide
`api.getCase(cas.id)` (para tener el manifiesto fresco) y llama a `restoreCaseEvidence`. Así un borrador con videos
sobrevive a una recarga: se retoma desde el historial y la evidencia sale de `agent-data/cases/<id>/`.

### 7.5 Editor del informe (`lib/report-images.ts`, `ReportStep`)

- `ReportImagePreviewCache` recibe `source: "server" | "agent"`. Con `agent`, `load` usa
  `agent.getCaseFileBlob(caseId, filename, signal)`:
  - 404 → `unavailable`;
  - `unreachable` → `error` (reintentable).
- La decodificación del `<img>` cubre lo que no sea PNG/JPEG: si `onError`, queda `unavailable`. La validación con
  bytes reales es la de `finish` (§5.6 paso 6).
- `useReportImages(caseId, opts?: { source, sameHost })`. Con `agent`, además del listado del backend, llama a
  `agent.listCaseFiles`:
  - si `!sameHost` o Tatana no responde → todos `available: false` y `agentUnavailable: true`;
  - si no, `available = available && está en la carpeta`.
- `ReportStep` muestra un aviso único arriba cuando `agentUnavailable`: `"Las capturas de este caso están en la PC {hostname}."`
  o `"Tatana no está corriendo en esta PC: abrilo y reintentá."`, con un botón "Reintentar" que llama a `reload`.

### 7.6 Identidad de la PC (`hooks/useAgentIdentity.ts`, nuevo)

`useAgentIdentity()` → `{ status: "loading" | "online" | "offline" | "outdated", info: AgentInfo | null }`:

- pide `agent.health()` y `agent.getInfo()` una vez por montaje y los comparte con un caché a nivel de módulo, con
  revalidación al llamar a `refresh()`;
- `isSameHost(cas)` es `status === "online" && info.hostname.toLowerCase() === cas.evidence_host?.hostname.toLowerCase()`.
  Si no hay `evidence_host` (manifiesto vacío), cualquier PC con Tatana sirve.

### 7.7 UI por paso (flujo `agent`)

**Paso 3, `CaptureStep`:**

- El botón principal dice **"Guardar evidencia en esta PC y continuar"**. `UploadProgressPanel` reusa el layout con
  estos textos de fase:
  - `checking` "Verificando espacio en esta PC…";
  - `copying` "Copiando al caso…";
  - `hashing` "Calculando hash…";
  - `registering` "Registrando en el expediente…".

  El título es `Guardando {index} de {total}`.
- Bandeja: el indicador de `EvidenceTray` suma el estado "en esta PC" (ícono `HardDrive` + texto, nunca solo color)
  cuando `storedInCase && availability === "here"`. Opcionalmente muestra el hash abreviado (`sha256.slice(0, 8)`)
  en el `title`. `availability === "missing"` muestra "No está en esta PC" con `AlertCircle`.
- URLs de vista previa: si `storedInCase`, `agent.caseFileURL(caseId, name)`; si no, `agentFileURL(name)`.
  - Aplica a `gallery.ts` (`srcOf` recibe el `GItem` con `url` ya resuelta: `CaptureStep` arma `url` para los
    remotos), `VideoCard` (solo el "Original"; las variantes siguen en la raíz) e `IdentityCard`.
  - `CaptureStep` recibe la prop `caseId`.
- **Otra PC** (no `isSameHost` con manifiesto no vacío):
  - `FxBanner` warning: "La evidencia de este caso está en la PC {hostname}. Seguilo desde esa PC para capturar o
    generar.";
  - controles de captura y botón de guardar deshabilitados;
  - "Continuar" sigue habilitado, para pasar a editar textos.
- **Tatana caído:** el mensaje de §7.2 y los mismos controles deshabilitados.

**Paso 5, `GenerateStep` + `page.tsx` → `handleGenerate`:**

1. Si es otra PC o Tatana está caído u desactualizado → mensaje (§7.2 / "La evidencia está en la PC X; generalo
   desde ahí") **sin llamar al backend**. El caso no cambia.
2. `api.prepareGeneration(caseId, { hostname })`. Un 400 `missing` se maneja como hoy (`setGenerateMissing`).
3. Tramo 1, **"Armando y verificando el ZIP en esta PC…"**: `agent.buildZip`, con barra opcional por `zip_progress`
   (WS) filtrado por `case_id`.
4. Tramo 2, **"Generando el informe en el servidor…"**:
   - por cada `report_images`, `agent.getCaseFileBlob`;
   - arma el `FormData` con `metadata` (un `Blob` JSON con `type: "application/json"`) y los `images`;
   - llama a `api.finishGeneration(caseId, formData)`.
5. Con éxito: `agent.commitZip(caseId, { case_ref, zip_filename, zip_hash, delete_files: files.map(f => f.filename) })`.
   Si el commit falla, **no** es un error de la generación: `ResultStep` muestra un aviso ("El ZIP quedó verificado
   en esta PC pero no se pudo mover a su carpeta final. Abrí este caso en esta PC con Tatana para completarlo.") y el
   auto-commit de §7.7 lo resuelve después.
6. Si fallan el paso 3 o el 4: `agent.discardPendingZip` (best effort) y mensaje.
   - Si el backend ya puso el caso en `error` (falla en el paso 8-9 de §5.6), se recarga el caso (`api.getCase`).
   - Un 409 `manifest_mismatch` / `generation_stale` → mensaje "La evidencia del caso cambió mientras se generaba;
     volvé a intentar."
7. `GenerateStep`: las miniaturas usan `caseFileURL` cuando `storedInCase`. Los requisitos suman "Tatana disponible en
   esta PC" cuando no lo está.

**Paso 6, `ResultStep`.** Props nuevas: `evidenceStorage`, `zipLocation`, `caseRef`, `zipState`
(`"final" | "pending" | "unknown"`) y `sameHost`.

- Bloque **Informe** (servidor): descarga del DOCX (`api.downloadURL`, como hoy) y hash del DOCX.
- Bloque **Evidencia ZIP — en esta PC**: nombre, hash, contraseña (como hoy), `zip_location.path` en mono con
  `CopyButton`, y dos acciones:
  - **"Mostrar en carpeta"** (`agent.revealZip`);
  - **"Guardar una copia…"** (`<a href={agent.zipFileURL(…)} download>`).

  Con `zipState === "pending"`, al montar intenta el auto-commit (§7.7). Si `info.evidence_directory_synced`, muestra
  un `FxBanner` warning: "La carpeta de evidencia ({evidence_directory}) está dentro de una carpeta sincronizada con
la nube (OneDrive/iCloud): el ZIP se puede subir a ese servicio. Pedí que configuren `Agent:EvidenceDirectory` fuera
de esa carpeta." El mismo aviso aparece en el paso 3 (`CaptureStep`) una vez por sesión, para que se vea antes de
generar.
- Flujo `server` (caso viejo): **igual que hoy**, con el botón "Descargar ZIP" del servidor.

**Historial, `CaseCard` (al expandir) y `CaseGridCard`.** Un caso `completed` en `agent` muestra en lugar del link de
descarga del ZIP:

- `"Guardado en {zip_location.hostname} · {zip_location.path}"`;
- si `isSameHost`, "Mostrar en carpeta" y "Guardar una copia…";
- en `CaseCard`, al expandir y si `isSameHost`: `agent.zipStatus`. Si `state === "pending"` y
  `pending_hash === cas.zip_hash`, llama a `agent.commitZip` (auto-commit) sin `delete_files`: la lista no está a
  mano, así que los sueltos quedan. Si `state === "none"`, "El ZIP ya no está en esta carpeta".
- `CaseGridCard` no consulta el estado. El botón reacciona al 404 con el mensaje `zip_not_found`.
- Un borrador `agent` con `evidence_host` de otra PC muestra un chip "Evidencia en {hostname}".

**`page.tsx`:**

- `attemptExitWizard`: el texto de confirmación pasa a "Lo que no se guardó en esta PC se va a perder." En flujo
  `agent` y sin pendientes se puede salir sin confirmar.

### 7.8 Local Network Access (navegador)

Ver §9.3. `lib/agent.ts` exporta `localNetworkPermission(): Promise<"granted" | "denied" | "prompt" | "unknown">`:

- prueba `navigator.permissions.query({ name })` con `"loopback-network"`, `"local-network-access"` y
  `"local-network"`, cada uno en `try/catch`, y devuelve el primero que responda; si ninguno responde, `"unknown"`;
- solo se consulta si `location.hostname` no es `localhost`/`127.0.0.1`;
- lo usa `agentErrorMessage`.

## 8. Contrato compartido

**Política:** backend `JsonNamingPolicy.SnakeCaseLower` (`server/src/Factum.Backend/Program.cs` L74-83), con los
null **escritos**. Tatana `SnakeCaseLower` con `WhenWritingNull` (`server/src/Factum.Agent/Program.cs` L65-70): los
null **se omiten**. Los cuerpos de error son `Dictionary<string, object?>` con claves literales.

### 8.1 client ↔ backend

| Elemento | JSON exacto | Backend | Client |
|---|---|---|---|
| Case: flujo | `"evidence_storage": "agent" \| "server"` (siempre presente) | `Models/Case.cs` (`EvidenceStorage`), `EvidenceManifest.ResolveStorage` | `lib/api.ts` (`Case.evidence_storage`) |
| Case: manifiesto | `"evidence": [ { "filename": string, "size": number, "sha256": string, "source_path": string \| null, "registered_at": string } ]` | `Models/Case.cs` (`EvidenceItem`) | `lib/api.ts` (`EvidenceItem`, `Case.evidence`) |
| Case: PC | `"evidence_host": { "hostname", "os_user", "agent_version", "case_directory", "registered_at" } \| null` | `Models/Case.cs` (`EvidenceHost`) | `lib/api.ts` (`EvidenceHost`) |
| Case: ZIP | `"zip_location": { "hostname", "directory", "path" } \| null` | `Models/Case.cs` (`ZipLocation`) | `lib/api.ts` (`ZipLocation`) |
| Case: intento | — (no viaja: `[JsonIgnore]`) | `PendingGeneration` | — |
| Registrar | `PUT /api/cases/{id}/evidence` body `{ "host": { "hostname", "os_user", "agent_version", "case_directory" }, "items": [ { "filename", "size", "sha256", "source_path" } ] }` | `DTOs/EvidenceDtos.cs` (`RegisterEvidenceRequest`, `EvidenceHostDto`, `EvidenceItemDto`), `CasesController.RegisterEvidence` | `lib/api.ts` (`registerEvidence`, `RegisterEvidenceRequest`) |
| Respuesta registrar/borrar | `{ "evidence": EvidenceItem[], "evidence_host": EvidenceHost \| null }` | `DTOs/EvidenceDtos.cs` (`EvidenceResponse`) | `lib/api.ts` (`EvidenceResponse`) |
| Borrar | `DELETE /api/cases/{id}/evidence/{filename}` | `CasesController.DeleteEvidence` | `api.deleteEvidence` |
| Preparar | `POST /api/cases/{id}/generate/prepare` body `{ "hostname": string }` → `{ "generation_id", "zip_filename", "case_ref", "password": string \| null, "encrypted": boolean, "files": [ { "filename", "size", "sha256" } ], "report_images": string[] }` | `DTOs/EvidenceDtos.cs` (`PrepareGenerationRequest`, `PrepareGenerationResponse`, `ManifestFileDto`) | `lib/api.ts` (`prepareGeneration`, `PrepareGenerationResponse`, `ManifestFile`) |
| Terminar | `POST /api/cases/{id}/generate/finish`, `multipart/form-data`: parte `metadata` (JSON) `{ "generation_id", "zip_filename", "zip_hash", "zip_size", "encrypted", "zip_location": { "hostname", "directory", "path" }, "files": ManifestFile[] }` + N partes `images` con `filename` | `DTOs/EvidenceDtos.cs` (`FinishGenerationMetadata`), `Services/Cases/AgentGeneration.cs` | `lib/api.ts` (`finishGeneration(caseId, form: FormData)`, `FinishGenerationMetadata`) |
| Respuesta terminar | `{ "case", "zip_hash", "password", "files": { "zip", "pdf" }, "report_hash", "zip_location" }` | `DTOs/CaseDtos.cs` (`GenerateResponse` + `ZipLocation? ZipLocation = null`) | `lib/api.ts` (tipo de retorno de `generateCase` y `finishGeneration` + `zip_location?`) |
| Errores nuevos | `"code"`: `evidence_on_agent`, `evidence_on_server`, `evidence_on_other_pc` (+`"evidence_hostname"`), `invalid_manifest` (+`"filename"`), `no_evidence`, `generation_stale`, `manifest_mismatch` (+`"mismatched": string[]`), `missing_images` (+`"missing_images": string[]`), `image_hash_mismatch` (+`"filename"`), `invalid_zip_info`, `request_too_large` (+`"max_bytes"`) | `Services/Cases/EvidenceManifest.cs` (`EvidenceErrorCodes`) | `lib/api.ts` (`ApiError` suma `code` y `body`, leídos en `toApiError`), `types/index.ts` (`EvidenceErrorCode`) |
| Validación | `400 { "error", "missing": string[] }` (sin cambios) | `CaseValidation` | `ApiError.missing` |
| Config CORS | `Cors:AllowedOrigins` (`Cors__AllowedOrigins__N`) | `Infrastructure/CorsOrigins.cs`, `Program.cs` | — |
| Config tope | `Report:MaxGenerateUploadBytes` (`Report__MaxGenerateUploadBytes`), default `268435456` | `ReportOptions`/`ReportSettings` | — (413 trae `max_bytes`) |

### 8.2 client ↔ Tatana

| Elemento | JSON exacto | Tatana | Client |
|---|---|---|---|
| Health | `GET /health` → `{ …, "capabilities": ["case_evidence_v1"] }` | `Controllers/HealthController.cs` | `lib/agent.ts` (`health`, `supportsCaseEvidence`) |
| Info | `GET /info` → `{ "hostname", "os_user", "version", "mode", "evidence_directory", "evidence_directory_synced" }` | ídem | `AgentInfo` |
| Archivo guardado | `{ "filename", "size", "sha256", "saved_at" }` | `Services/CaseEvidenceStore.cs` (`EvidenceFileInfo`) | `lib/agent.ts` (`EvidenceFileInfo`) |
| Importar | `POST /cases/{id}/evidence/import` body `{ "filename" }` | `Controllers/CaseEvidenceController.cs` | `importEvidence` |
| Prechequeo | `GET /cases/{id}/evidence/upload-check?filename=&size=` → `{ "max_upload_bytes" }` | ídem | `checkEvidenceUpload` |
| Subida | `POST /cases/{id}/evidence/upload?filename=` cuerpo crudo | ídem | `uploadEvidence` (XHR) |
| Listado | `GET /cases/{id}/files` → `{ "directory", "files": [ { "filename", "size", "modified_at" } ] }` | ídem | `listCaseFiles`, `AgentCaseFiles` |
| Archivo | `GET` / `DELETE /cases/{id}/files/{filename}` | ídem | `caseFileURL`, `getCaseFileBlob`, `deleteCaseFile` |
| Armar ZIP | `POST /cases/{id}/zip` body `{ "case_ref", "zip_filename", "password", "files": [ { "filename", "size", "sha256" } ] }` → `{ "zip_filename", "zip_hash", "zip_size", "encrypted", "encryption"?, "directory", "zip_path", "hostname", "files" }` | `Controllers/CaseZipController.cs`, `Services/CaseZipService.cs` | `buildZip`, `BuildZipRequest`, `BuildZipResponse` |
| Commit | `POST /cases/{id}/zip/commit` body `{ "case_ref", "zip_filename", "zip_hash", "delete_files": string[] }` → `{ "zip_path", "deleted" }` | ídem | `commitZip` |
| Descartar | `DELETE /cases/{id}/zip/pending?case_ref=&zip_filename=` → 204 | ídem | `discardPendingZip` |
| Estado | `GET /cases/{id}/zip/status?case_ref=&zip_filename=` → `{ "state": "none" \| "pending" \| "final", "zip_path", "directory", "pending_hash"?, "committed_hash"? }` | ídem | `zipStatus`, `ZipStatus` |
| Mostrar | `POST /cases/{id}/zip/reveal?case_ref=&zip_filename=` → 204 | ídem + `Services/FolderReveal.cs` | `revealZip` |
| Copia | `GET /cases/{id}/zip/file?case_ref=&zip_filename=` (attachment) | ídem | `zipFileURL` |
| WS nuevo | `{ "type": "zip_progress", "data": { "case_id", "phase": "hashing" \| "zipping" \| "verifying", "done_bytes", "total_bytes" } }` | `CaseZipService` → `AgentWebSocketHub` | `page.tsx` (`handleWsEvent`) |
| Errores | `{ "error", "code" }` con `code` ∈ `invalid_case_id`, `invalid_filename`, `file_not_found`, `file_exists`, `file_busy`, `length_required`, `file_too_large` (+`size`, `max_upload_bytes`), `insufficient_storage` (+`size`?, `required_bytes`, `available_bytes`), `incomplete_upload` (+`size`, `received_bytes`), `storage_error`, `evidence_changed` (+`files: [ { "filename", "reason" } ]`), `zip_in_progress`, `zip_already_committed`, `zip_failed`, `zip_not_found`, `zip_hash_mismatch`, `origin_not_allowed` | `Common/AgentErrorCodes.cs` (nuevo) | `lib/agent.ts` (`AgentErrorCode`, `AgentErrorBody`), `lib/agent-messages.ts` |
| Config | `Agent:EvidenceDirectory`, `Agent:AllowedOrigins`, `Agent:MaxUploadBytes`, `Agent:MinFreeBytes` | `Models/AgentModels.cs` (`AgentOptions`) | — |

## 9. Decisiones técnicas

Ninguna de estas necesita al usuario. Las que sí lo necesitan están en §12.

- **D-T1. `EvidenceZip` duplicado en Tatana, sin proyecto compartido.** El `Dockerfile` del backend usa
  `src/Factum.Backend` como contexto (`COPY Factum.Backend.csproj .`). Un proyecto compartido rompe el build de la
  imagen y `deploy/windows/armar-paquete.sh`. Son unas 150 líneas estables, con tests en los dos lados.
- **D-T2. Tres etapas (`prepare` → ZIP en Tatana → `finish`) con el ZIP provisorio hasta el commit.** Es lo que
  garantiza "no queda un ZIP con un hash distinto al registrado":
  - mientras el backend no confirma, el ZIP solo existe como `.factum/pendiente/…` y se descarta ante un error;
  - una vez confirmado, el commit es un rename (el hash no cambia) y es idempotente;
  - si el navegador se cierra entre `finish` y `commit`, el pendiente tiene el hash registrado y se completa después
    (auto-commit).
- **D-T3. `prepare` no cambia el `Status`.** Así un Tatana caído o un ZIP fallido no dejan el caso en `generating`
  colgado (hoy no hay vuelta atrás de `generating`). El paso a `generating` se hace dentro de `finish` (una sola
  request, como hoy) con un filtro atómico. Por eso no hace falta un endpoint de "abortar".
- **D-T4. El rol sigue en `capture_roles`.** El manifiesto no lo duplica: una sola fuente, y el editor y `ResultStep`
  ya lo leen de ahí.
- **D-T5. Las capturas del informe viajan en la request de `finish` (D2 A) y se escriben a `.generate-tmp/` en lugar
  de quedar en memoria.** `ReportService` las abre con `ReportImageFiles.TryOpen` por ruta. Reusar la carpeta evita
  reescribir el armado de imágenes del DOCX. Se borran en un `finally` y al arrancar.
- **D-T6. `MultipartReader` en streaming, no `IFormFile`.** `IFormFile` bufferiza a `ASPNETCORE_TEMP` (`/tmp`), que
  no controlamos. Así el hash se calcula al vuelo, se aplica el tope y se borra todo de una.
- **D-T7. Tatana rechaza en el servidor los orígenes no permitidos (403), además de no mandar CORS.** CORS solo
  impide *leer* la respuesta: un `POST` simple desde otra página igual se ejecutaría. El WebSocket no tiene CORS y
  necesita el chequeo de `Origin` sí o sí.
  - Residual aceptado: una página ajena puede **mostrar** (no leer) una captura con `<img src="http://localhost:8765/…">`
    si adivina el nombre, porque los `<img>` no mandan `Origin`.
  - El emparejamiento por token queda fuera de alcance (D9).
- **D-T8. Los archivos de Tatana se mueven, no se copian.** Es un rename en el mismo volumen: instantáneo y sin
  duplicar GB. Contra: el archivo deja de estar en `/files/<name>`, y el cliente cambia de URL al guardar (§7.7).
- **D-T9. Hostname como identidad de la PC (D7).** `Environment.MachineName` es lo que ya informa `/info`. La
  comparación no distingue mayúsculas. Se aceptan colisiones de nombre entre PCs.
- **D-T10. No se infiere "otra PC" sin Tatana.** Si Tatana no responde, el mensaje es "Tatana no está corriendo" y no
  "está en otra PC".

### 9.1 Convivencia con el flujo viejo (D8)

| Situación | `evidence_storage` | Qué puede hacer |
|---|---|---|
| Caso nuevo | `agent` (persistido) | Flujo nuevo completo |
| Borrador viejo **sin** archivos en el servidor | `agent` (resuelto; se persiste con el primer `PUT evidence`) | Flujo nuevo completo |
| Borrador viejo **con** archivos en el servidor | `server` (resuelto) | Flujo viejo sin cambios: captura → `POST /files` → vista previa del servidor → `POST /generate`. DP3 A: también sigue sumando evidencia al servidor. |
| Caso viejo `completed` | `server` (resuelto) | Descarga ZIP y DOCX del servidor, como hoy |
| Caso nuevo `completed` | `agent` | DOCX del servidor; ZIP "en la PC X" |

- Ningún archivo ni documento se toca en forma automática: `ResolveStorage` solo lee el disco.
- Un caso no puede mezclar flujos: los endpoints de cada flujo responden 409 con `evidence_on_agent` o
  `evidence_on_server` sobre un caso del otro flujo.

### 9.2 Recarga de página con videos (requisito 1 de D8)

- Con el guardado automático (DP1 A), cada captura (incluidos los videos de la cámara externa, que hoy viven solo en memoria del navegador) se
  copia a `agent-data/cases/<id>/` y se registra apenas existe.
- Después de una recarga, el perito retoma el borrador desde el historial y `restoreCaseEvidence` (§7.4) rearma la
  bandeja desde el manifiesto y la carpeta del caso. Lo que se movió pero no se llegó a registrar se registra en ese
  momento.
- Lo único que se puede perder es un `Blob` del navegador que no terminó de copiarse a Tatana cuando se recargó.

### 9.3 Navegador: HTTPS público → `http://localhost` (confirmación pedida por la HU)

- `http://localhost` y `http://127.0.0.1` son "potentially trustworthy": una página HTTPS los puede llamar sin
  bloqueo por contenido mixto, incluido `ws://localhost`.
- Chrome y Edge recientes aplican **Local Network Access**: la primera vez que un sitio público llama a loopback
  piden permiso al usuario ("acceder a otras apps y servicios de este dispositivo"). Si se deniega, `fetch` falla
  igual que con Tatana caído. Por eso el cliente consulta el permiso (§7.8) para dar el mensaje correcto.
- Versiones con el esquema anterior (Private Network Access) mandan el preflight
  `Access-Control-Request-Private-Network: true` y Tatana responde `Access-Control-Allow-Private-Network: true`.
- Los nombres exactos del permiso cambiaron entre versiones de Chrome. El código prueba varios y degrada a
  `"unknown"`. **El usuario lo confirma en la prueba manual con su Chrome/Edge (§11.3, punto 8).**
- En desarrollo (`localhost:3000` → `localhost:8765`) no aparece ningún aviso.

### 9.4 Riesgos conocidos

- **Tope de la nube:** la request de `finish` lleva todas las capturas sin rol (anexo). Con 50 capturas puede pesar
  decenas de MB. El tope propio es configurable (256 MiB), pero el proxy del proveedor puede tener uno menor. Se
  valida en la HU de despliegue.
- **`generating` colgado:** si el backend muere a mitad de `finish`, el caso queda en `generating`, igual que hoy con
  `POST /generate`. Fuera de alcance.
- **Tatana viejo con web nueva:** se detecta por `capabilities` (§7.2).

## 10. Checklist atómico

### 10.1 Backend — API (`server/src/Factum.Backend`)

- [ ] B1. `Models/Case.cs`: `EvidenceItem`, `EvidenceHost`, `ZipLocation`, `PendingGeneration` (`[JsonIgnore]` en la
      propiedad del caso), `EvidenceStorages`, y las propiedades `EvidenceStorage` (`[BsonIgnoreIfNull]`),
      `Evidence = []`, `EvidenceHost`, `ZipLocation`, `PendingGeneration` (§3.1).
- [ ] B2. `DTOs/EvidenceDtos.cs` (nuevo): `RegisterEvidenceRequest`, `EvidenceHostDto`, `EvidenceItemDto`,
      `EvidenceResponse`, `PrepareGenerationRequest`, `PrepareGenerationResponse`, `ManifestFileDto`,
      `FinishGenerationMetadata`, `ZipLocationDto`. Todos los campos de texto `string?` sin `[Required]`, como
      `CaseDtos.cs`. `GenerateResponse` suma `ZipLocation? ZipLocation = null` al final.
- [ ] B3. `Services/Cases/EvidenceManifest.cs` (nuevo, `static`, puro): `ResolveStorage`, `ValidateItems`,
      `ToFileInfos`, `Upsert`, `ReportImageNames`, `Matches(files, manifest) → mismatched`, `EvidenceErrorCodes`.
- [ ] B4. `Infrastructure/MongoRepository.cs`: `RegisterEvidenceAsync`, `RemoveEvidenceAsync`,
      `SetPendingGenerationAsync`, `TryMarkGeneratingAsync`, `CompleteAgentGenerationAsync` (filtros de §5.2-§5.6).
      `CreateAsync` (servicio) pone `EvidenceStorage = "agent"`.
- [ ] B5. `Infrastructure/StorageService.cs`: `HasEvidenceFiles` (sin crear la carpeta), `NewGenerationTempDir`,
      `CleanupOrphanGenerations` (solo dentro de `.generate-tmp/`).
- [ ] B6. `CaseService`: completar `EvidenceStorage` resuelto en todo `Case` devuelto. Ramas `agent` en `GetAsync`,
      `ListFilesAsync`, `GetReportTextDefaultsAsync`, `UpsertCaptureRolesAsync` y `ListReportImagesAsync` (§5.4).
- [ ] B7. `CaseService`: 409 `evidence_on_agent` en `PrecheckUploadAsync` (cubre `POST /files` y `upload-check`) y en
      `GenerateAsync` para casos `agent`.
- [ ] B8. `CaseService.RegisterEvidenceAsync` y `DeleteEvidenceAsync` (§5.2, §5.3).
- [ ] B9. `Services/Cases/AgentGeneration.cs` (nuevo) o métodos en `CaseService`: `PrepareAsync` (§5.5) y
      `FinishAsync` (§5.6) con `MultipartReader`, hash al vuelo, tope y `finally` que borra `.generate-tmp/<gid>/`.
      Ninguna excepción sin `Result`.
- [ ] B10. `ReportService`: `GeneratePassword` y `ZipFilenameFor` `internal static`; `GenerateReportAsync` nuevo;
      `GenerateDocxAsync(…, imagesDir, …)`; `GenerateAsync` viejo sin cambio de comportamiento (§5.7).
- [ ] B11. `CasesController`: `PUT {id}/evidence`, `DELETE {id}/evidence/{filename}`, `POST {id}/generate/prepare`,
      `POST {id}/generate/finish` (`[DisableFormValueModelBinding]`, límite de cuerpo por request antes de leer,
      `[ProducesResponseType]` 400/403/404/409/413).
- [ ] B12. `Infrastructure/CorsOrigins.cs` + `Program.cs`: política `WithOrigins`, validación en `configErrors`, log
      de orígenes, `ReportOptions.MaxGenerateUploadBytes` validado `> 0` y `CleanupOrphanGenerations` al arrancar.
- [ ] B13. `appsettings.json`: **no** agregar `Cors` (los defaults en código cubren desarrollo y la instalación
      local). README: tabla de config con `Cors:AllowedOrigins` y `Report:MaxGenerateUploadBytes`, más un párrafo
      "Evidencia en la PC del perito" con el flujo de 3 etapas.
- [ ] B14. Tests en `server/tests/Factum.Backend.Tests`:
  - `EvidenceManifestTests`: `ResolveStorage` en los 5 casos de §9.1, `ValidateItems` (nombre, sha, size, repetidos,
    artefactos), `Matches`, `ReportImageNames`;
  - `CorsOriginsTests`: default, lista válida, `*`, path, `/` final;
  - `ReportServiceAgentTests`: `GenerateReportAsync` con imágenes en una carpeta temporal distinta de `outputDir`.
    Que la tabla de hashes salga del manifiesto y que en `outputDir` quede **solo** el DOCX. Reusar
    `ReportTestSupport`/`TestImages`.
  - `StorageGenerateTmpTests`: la limpieza borra solo `.generate-tmp/`.

  Todos en `Path.GetTempPath()`/`Guid`, nunca en `dev-data`.

### 10.2 Backend — Tatana (`server/src/Factum.Agent`)

- [ ] T1. `Models/AgentModels.cs`: `AgentOptions` suma `EvidenceDirectory`, `AllowedOrigins`, `MaxUploadBytes` y
      `MinFreeBytes` (§4.2), más un resolvedor de `EvidenceDirectory` efectivo con el default por SO de DP4
      (`C:\Factum\Evidencia` en Windows, `~/Factum/Evidencia` en macOS/Linux; nunca `MyDocuments`).
- [ ] T2. `Common/OriginPolicy.cs` (puro: parse, validación y `IsAllowed`) + `Program.cs`: reemplazar el middleware
      `*` y `AddCors`/`UseCors` por la guarda de §6.1, que incluye `/ws`. Validar la config al arrancar y loguear.
- [ ] T3. `Common/AgentFileNames.cs`: `IsValidName`, `IsZipFilename`, `CaseFolderName`, `TryParseCaseId`,
      `IsSyncedFolder`.
      `Common/AgentErrorCodes.cs`.
- [ ] T4. `Services/CaseEvidenceStore.cs` (singleton): `CaseDir`, `ImportAsync` (move + hash, `file_busy`),
      `CheckUpload`, `StageAndCommitUploadAsync` (patrón de `UploadStaging` del backend), `List`, `TryOpen`,
      `Delete`, `CleanupOrphanUploads`. Sondeo de espacio con `DriveInfo` y fail-open.
- [ ] T5. `Controllers/CaseEvidenceController.cs` (`[Route("cases/{caseId}")]`): las 6 rutas de §6.2. La subida fija
      `MaxRequestBodySize` antes de leer.
- [ ] T6. `Services/Evidence/EvidenceZip.cs` (copia, §6.3) + SharpZipLib 1.4.2 en el csproj.
- [ ] T7. `Services/CaseZipService.cs`: `BuildAsync` (semáforo por caso, espacio, verificación, `.part` → pendiente,
      `.sha256`, `zip_progress`), `CommitAsync` (idempotente con `estado.json`), `DiscardPending`, `Status`.
- [ ] T8. `Services/FolderReveal.cs` (Windows/macOS/Linux con `ArgumentList`) y
      `Controllers/CaseZipController.cs` con las 6 rutas de §6.3.
- [ ] T9. `HealthController`: `capabilities` en `/health`; `evidence_directory` y `evidence_directory_synced` en
      `/info`.
- [ ] T10. `Program.cs`: registrar `ICaseEvidenceStore` y `ICaseZipService`, `CleanupOrphanUploads()` al arrancar y
      línea de log de §4.2 (+ `Warning` si `IsSyncedFolder`).
- [ ] T11. Tests en `server/tests/Factum.Agent.Tests`:
  - `OriginPolicyTests`;
  - `EvidenceDirectoryTests`: default por SO (`C:\Factum\Evidencia` / `~/Factum/Evidencia`), nunca bajo Documents,
    e `IsSyncedFolder` (OneDrive, `OneDrive - Empresa`, `Mobile Documents`, ruta normal);
  - `AgentFileNamesTests` (nombres, `CaseFolderName`, ids inválidos y traversal);
  - `CaseEvidenceStoreTests`: import mueve e idempotente, subida atómica (corta, larga, exacta) sin dejar `.part`,
    nombre fuera de la carpeta rechazado;
  - `EvidenceZipTests`: copia de los del backend;
  - `CaseZipServiceTests`: `evidence_changed` por hash/tamaño/faltante; pendiente y commit con el hash correcto;
    `zip_hash_mismatch`; commit idempotente; descarte; los sueltos se borran solo con commit.

  Todo en directorios temporales propios (`DataDirectory` y `EvidenceDirectory` de prueba), **nunca** en
  `server/src/Factum.Agent/agent-data` ni en la carpeta default de evidencia (`~/Factum/Evidencia`,
  `C:\Factum\Evidencia`).

### 10.3 Frontend — `client/`

**Skills obligatorios:** `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (como
criterio, sin 3D) y `web-design-guidelines` (autochequeo); `ui-styling`/`mblode-agent-skills-ui-animation` si
corresponde. Leer `client/AGENTS.md` (Next.js 16). **`agent-ui/` no se toca.**

- [ ] F1. `lib/api.ts` + `types/index.ts`: tipos de §8.1 (`EvidenceItem`, `EvidenceHost`, `ZipLocation`,
      `ManifestFile`, `PrepareGenerationResponse`, `FinishGenerationMetadata`, `EvidenceResponse`,
      `EvidenceErrorCode`, `Case` con los campos nuevos y `GenerateResult` con `zip_location?`). `ApiError` suma
      `code`/`body`. Funciones `registerEvidence`, `deleteEvidence`, `prepareGeneration` y `finishGeneration`
      (`fetch` con `FormData`, sin `Content-Type` manual).
- [ ] F2. `lib/agent.ts`: `AgentError`, `health`, `supportsCaseEvidence`, `AgentInfo` ampliado, las funciones de
      evidencia y ZIP de §7.1 y `localNetworkPermission` (§7.8). Tipos de §8.2.
- [ ] F3. `lib/agent-messages.ts` (nuevo) con la tabla de §7.2.
- [ ] F4. `hooks/useAgentIdentity.ts` (nuevo) con `isSameHost` (§7.6).
- [ ] F5. `hooks/useFileManager.ts`:
  - `storageMode`;
  - `saveOne` y la cola de guardado automático (DP1 A);
  - `handleSaveAndContinue`;
  - `handleUploadAndContinueLegacy` = el actual sin cambios;
  - `removeFile` de dos lados;
  - `restoreCaseEvidence`;
  - liberar blobs ya guardados (§7.4).
- [ ] F6. `CapturedFile` unificado (`types/index.ts`; `gallery.ts` lo importa) con
      `storedInCase`/`sha256`/`size`/`availability`. `UploadPhase` ampliado.
- [ ] F7. `CaptureStep`, `EvidenceTray`, `UploadProgressPanel`, `VideoCard`, `IdentityCard` y `gallery.ts`: textos y
      fases nuevas, indicador "en esta PC"/"No está en esta PC", URLs de la carpeta del caso, prop `caseId`, banner y
      deshabilitado en "otra PC"/"Tatana caído" (§7.7).
- [ ] F8. `lib/report-images.ts` + `ReportStep`: `source: "agent"`, vistas previas desde Tatana, cruce de
      disponibilidad y aviso único (§7.5).
- [ ] F9. `page.tsx`:
  - elegir el flujo por `currentCase.evidence_storage`;
  - `handleGenerate` de 3 etapas + commit + descarte (§7.7);
  - `zip_progress` en `handleWsEvent`;
  - `proceedResume` → `getCase` + `restoreCaseEvidence`;
  - texto de `attemptExitWizard`.
- [ ] F10. `GenerateStep`: miniaturas desde la carpeta del caso, requisito "Tatana en esta PC" y progreso en dos
      tramos.
- [ ] F11. `ResultStep`: bloques Informe / ZIP en esta PC, "Mostrar en carpeta", "Guardar una copia…" y auto-commit
      del pendiente. El flujo `server` queda igual que hoy.
- [ ] F12. `CaseCard` y `CaseGridCard`: ubicación del ZIP, acciones si es la misma PC, auto-commit al expandir en
      `CaseCard` y chip "Evidencia en {hostname}" en borradores.
- [ ] F13. Ningún texto visible muestra "Failed to fetch". Revisar con un `grep` los `catch` nuevos.

## 11. Verificación (D13 A)

### 11.1 Regla dura de datos de desarrollo (copiar en el prompt de cada implementador)

> La MongoDB de desarrollo (`factum_dev`), la carpeta `Storage:DataDirectory` del backend
> (`server/src/Factum.Backend/dev-data/`, hoy 53 carpetas de casos) y la carpeta de Tatana
> (`server/src/Factum.Agent/agent-data/`, hoy unos 279 archivos) tienen **datos cargados a mano por el usuario**. No
> son descartables.
>
> - **No se borra, mueve, renombra ni modifica ningún documento ni archivo preexistente.** Esto incluye **no
>   importar** al flujo nuevo (`/evidence/import` *mueve* el archivo) ningún archivo que ya estuviera en
>   `agent-data/`: en las pruebas solo se importan archivos que la propia prueba generó.
> - Las pruebas de punta a punta corren Tatana con carpetas propias: `--data <scratch>/agent-data`,
>   `Agent__EvidenceDirectory=<scratch>/Evidencia`. El backend corre con `Storage__DataDirectory=<scratch>/data`.
>   `<scratch>` es un directorio temporal nuevo del implementador.
> - Para Mongo se recomienda una base propia (`MongoDb__DatabaseName=factum_e2e_zip_local`). Si se usa `factum_dev`,
>   se limpia **solo por los `_id`** que la prueba insertó. Nunca `deleteMany` con filtro amplio. Crear la base o la
>   colección está permitido; tocar documentos ajenos, no.
> - Los tests de xUnit usan solo `Path.GetTempPath()` y fakes. No tocan Mongo.
> - El caso "borrador viejo con evidencia en el servidor" se simula insertando **un documento de prueba nuevo** sin
>   `EvidenceStorage` y copiando un PNG de prueba a `<scratch>/data/cases/<ese id>/`. Se limpia por ese `_id` y esa
>   carpeta.

### 11.2 Comandos antes de declararse `done`

**`implementer-backend`:**

```bash
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj
```

Además, con Tatana en `--mock`, backend y Mongo de prueba (§11.1), un recorrido con `curl` que deja la salida en
`progress/impl_backend_zip-local-informe-servidor.md`:

1. Login y caso nuevo.
2. Screenshot mock (`POST /devices/<serial>/screenshot`) → `import` → `PUT evidence`.
3. Una subida a Tatana de un archivo de 50 MB generado en scratch.
4. `PUT capture-roles` imei.
5. Textos mínimos.
6. `prepare` → `POST /cases/{id}/zip` → `finish` (multipart con `curl -F`) → `commit`.

Verificar:

- `find <scratch>/data -type f`: solo `cases/<id>/informe_pericial_*.docx` y nada en `.generate-tmp/`;
- `sha256sum` del ZIP final = `zip_hash` de Mongo = el hash que figura en el DOCX (`unzip -p` del
  `word/document.xml` y `grep`);
- la carpeta de trabajo del caso quedó vacía o borrada;
- `curl -H "Origin: https://evil.example" -X POST …/cases/<id>/evidence/import` → 403 sin efectos (el archivo sigue
  en la raíz);
- `websocat`/`curl` con `Origin` ajeno a `/ws` → 403;
- con `Origin: http://localhost:3000` → headers CORS con ese origen;
- backend con `Origin` ajeno: sin `Access-Control-Allow-Origin`;
- falla a mitad: `finish` con un `files` alterado → 409 `manifest_mismatch` y el caso sigue `draft`. Forzar un error
  de DOCX (por ejemplo, renombrar temporalmente la plantilla **en la carpeta de salida `bin/` del build de prueba**,
  no en `Templates/` del repo) → `error`. `DELETE zip/pending` deja `.factum/pendiente/` vacía y los sueltos intactos;
- borrador viejo simulado (§11.1): `GET` → `evidence_storage: "server"`, `POST /generate` funciona, `PUT evidence` →
  409 `evidence_on_server`;
- un caso `completed` preexistente de `factum_dev`, **solo lectura**: `GET /api/cases/{id}` → `evidence_storage: "server"`,
  y `download` del ZIP sigue respondiendo 200.

**`implementer-frontend`:**

```bash
cd client && npx tsc --noEmit
cd client && npm run build
```

Dejar en `progress/impl_frontend_zip-local-informe-servidor.md` constancia de los skills usados y de un recorrido con
el front en dev contra el backend y Tatana mock de §11.1.

### 11.3 Prueba manual que queda para el usuario

1. **Origen distinto (simula la nube):** levantar el front en otro origen (`npx next start -p 3001`, con
   `Cors__AllowedOrigins__0=http://localhost:3001` y `Agent__AllowedOrigins__0=http://localhost:3001`). Capturar
   pantallas, una grabación del celular, un video de cámara externa y un adjunto. Cada archivo pasa a "en esta PC"
   apenas se captura (DP1 A).
2. **Recarga con videos:** con el video de cámara externa ya guardado, recargar la página (F5), retomar el borrador
   desde el historial: la bandeja vuelve completa, con el video incluido.
3. **Editor:** insertar capturas con vista previa; marcar roles.
4. **Generar:** se ven los dos tramos. En el resultado, "Mostrar en carpeta" abre el Explorador en
   `C:\Factum\Evidencia\<ref>_<id8>\` con el ZIP seleccionado (en macOS de desarrollo, Finder en
   `~/Factum/Evidencia/<ref>_<id8>/`). "Guardar una copia…" baja el ZIP. Se abre el
   ZIP con 7-Zip y la contraseña.
5. **En disco:** en la carpeta de datos del backend solo quedó el DOCX del caso. `certutil -hashfile <zip> SHA256`
   (Windows) coincide con el hash de la pantalla y del informe.
6. **Otra PC** (o cambiar temporalmente el hostname informado con otra instancia de Tatana): el caso muestra
   "Guardado en <PC> · <ruta>", descarga el DOCX y no deja generar ni ver vistas previas.
7. **Tatana apagado:** "Tatana no está corriendo en esta PC: abrilo y reintentá" al guardar, previsualizar y generar.
   El caso no cambia de estado.
8. **Página ajena:** desde un HTML local servido en otro puerto (`python3 -m http.server 8000`) hacer
   `fetch("http://localhost:8765/health")`: lo bloquea el navegador (y el 403 se ve en la pestaña Red). Si tienen la
   web en un dominio HTTPS real, confirmar si Chrome/Edge pide el permiso de red local y que, si se deniega, el
   mensaje sea el de §7.2.
9. **Casos viejos:** un caso generado antes de la HU sigue descargando ZIP y DOCX del servidor.

## 12. Decisiones del usuario (2026-10-06)

Las 4 decisiones que dejó abiertas la primera versión de esta SDD quedan resueltas así. Ya están aplicadas en todo el
documento.

- **DP1 = A. La evidencia se guarda automáticamente apenas se captura.** Cada captura, grabación, foto o adjunto se
  copia a `agent-data/cases/<id>/` y se registra en el expediente apenas existe. El botón "Guardar evidencia en esta
  PC y continuar" solo completa lo que falte (reintentos, archivos que estaban ocupados) y guarda las marcas.
  Aplicado en §7.4, §7.7, §9.2, F5 y §11.3.
- **DP2 = A. Archivos sueltos en `agent-data/cases/<id>/`, ZIP en una carpeta propia `<causa>_<id8>`** bajo
  `Agent:EvidenceDirectory`. Al generar (después del commit) se borran los sueltos y queda solo el ZIP. El nombre
  `<causa>_<id8>` queda aprobado. Aplicado en §3.4 y §6.3.
- **DP3 = A. Los borradores viejos con evidencia en el servidor siguen con el flujo viejo completo**: capturar,
  enviar al servidor, vista previa del servidor y generar en el servidor, hasta que se cierren. Aplicado en §5.4,
  §7.4 (`handleUploadAndContinueLegacy`) y §9.1.
- **DP4. Ruta fija para el ZIP, fuera de Documentos.** La carpeta base default es `C:\Factum\Evidencia` en Windows.
  Para el desarrollo en macOS (y Linux), el default equivalente lo definió esta SDD: `~/Factum/Evidencia`, fuera de
  `~/Documents` y de iCloud Drive. Las dos son configurables con `Agent:EvidenceDirectory`. Se mantiene el chequeo
  como aviso: si la carpeta configurada cae dentro de OneDrive (o iCloud), Tatana lo registra en su log y la web lo
  muestra en el paso 3 y en el resultado, sin cambiar la carpeta. Aplicado en §3.4, §4.2, §6.1, §7.7, T1, T3, T10,
  T11 y §11.3.

**Observación técnica sobre DP4** (no cambia la decisión): en la instalación local con Docker
(`deploy/windows`, `FACTUM_HOME = C:\Factum`), la carpeta de datos del backend es `C:\Factum\evidencia`. En NTFS
eso es **la misma carpeta** que `C:\Factum\Evidencia`, porque no distingue mayúsculas. Las carpetas de caso de
Tatana (`<causa>_<id8>/`) quedan al lado de `cases/`, `.upload-tmp/` y `.generate-tmp/` del backend.

- No se pisan: los nombres no coinciden y cada lado limpia solo sus propias subcarpetas (el backend solo dentro de
  `.upload-tmp/` y `.generate-tmp/`; Tatana solo dentro de `<causa>_<id8>/.factum/`).
- Efecto práctico: el backup de la instalación local que respalde `evidencia/` también se lleva los ZIP, lo que
  probablemente sea deseable.
- No hace falta cambiar código. Si el usuario prefiere separarlas, alcanza con fijar `Agent:EvidenceDirectory` (por
  ejemplo, `C:\Factum\ZIP`) en el `appsettings.json` de Tatana del paquete.
