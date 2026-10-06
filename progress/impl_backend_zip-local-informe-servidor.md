# impl_backend — zip-local-informe-servidor (#9)

**Estado:** done · **Rama:** `feat/zip-local-informe-servidor` (sin commit, como pidió el orquestador)
**SDD:** `Refactorizaciones/zip-local-informe-servidor.md` (§10.1 B1–B14 y §10.2 T1–T11). Decisiones del usuario §12 (DP1–DP4) aplicadas.

## Archivos tocados

### API (`server/src/Factum.Backend`)
| Archivo | Qué |
|---|---|
| `Models/Case.cs` (M) | B1: `EvidenceStorage` (`[BsonIgnoreIfNull]`), `Evidence = []`, `EvidenceHost`, `ZipLocation`, `PendingGeneration` (`[JsonIgnore]`); clases `EvidenceItem`, `EvidenceHost`, `ZipLocation`, `PendingGeneration` con `[BsonIgnoreExtraElements]`; `EvidenceStorages`. |
| `DTOs/EvidenceDtos.cs` (nuevo) | B2: `RegisterEvidenceRequest`, `EvidenceHostDto`, `EvidenceItemDto`, `EvidenceResponse`, `PrepareGenerationRequest`, `PrepareGenerationResponse`, `ManifestFileDto`, `FinishGenerationMetadata`, `ZipLocationDto` (todo anulable, sin `[Required]`). |
| `DTOs/CaseDtos.cs` (M) | `GenerateResponse` suma `ZipLocation? ZipLocation = null` al final. |
| `Services/Cases/EvidenceManifest.cs` (nuevo) | B3: `EvidenceErrorCodes`, `ResolveStorage`, `ValidateItems`, `Upsert`, `MergeFileSources`, `ToFileInfos`, `Hashes`, `ToManifestFiles`, `ReportImageNames`, `Matches`, `SameHost`. Puro. |
| `Services/Cases/AgentGeneration.cs` (nuevo, `partial class CaseService`) | B8/B9: `RegisterEvidenceAsync`, `DeleteEvidenceAsync`, `PrepareGenerationAsync`, `FinishGenerationAsync` (MultipartReader en streaming, SHA-256 al vuelo, tope, `finally` que borra `.generate-tmp/<gid>/`). |
| `Services/Cases/CaseService.cs` (M) | B6/B7: `partial`; `IOptions<ReportOptions>` en el ctor; `EvidenceStorage` resuelto en memoria en list/get/create/update/generate (vía `LoadOwnedAsync`); ramas `agent` en `GetAsync`, `ListFilesAsync`, `GetReportTextDefaultsAsync`, `UpsertCaptureRolesAsync`, `ListReportImagesAsync`; 409 `evidence_on_agent` en `PrecheckUploadAsync` (cubre `POST /files` y `upload-check`) y `GenerateAsync`; `CreateAsync` escribe `EvidenceStorage = "agent"`. |
| `Infrastructure/MongoRepository.cs` (M) | B4: `RegisterEvidenceAsync`, `RemoveEvidenceAsync`, `SetPendingGenerationAsync`, `TryMarkGeneratingAsync`, `CompleteAgentGenerationAsync`, `MarkAgentGenerationFailedAsync`. Solo `$set/$pull/$unset` por `_id` con filtros `Editable` (+ `EvidenceStorage != "server"`). El listado excluye `PendingGeneration`. |
| `Infrastructure/StorageService.cs` (M) | B5: `HasEvidenceFiles` (no crea la carpeta), `NewGenerationTempDir` (Guid "N"), `CleanupOrphanGenerations` (solo dentro de `.generate-tmp/`). |
| `Infrastructure/CorsOrigins.cs` (nuevo) | B12: `Parse(IConfiguration)` / `Parse(values)` → `(Origins, Errors)`. |
| `Services/Reports/ReportService.cs` (M) | B10: `ReportOnlyResult`, `GenerateReportAsync` nuevo en `IReportService`; `GenerateDocxAsync(…, imagesDir, …)` con `IReadOnlyDictionary` de hashes; `GeneratePassword` y `ZipFilenameFor` `internal static`. `GenerateAsync` viejo sin cambio de comportamiento (pasa `caseDir` como `imagesDir`). |
| `Services/Reports/ReportOptions.cs` (M) | `MaxGenerateUploadBytes` (default 268435456). |
| `Controllers/CasesController.cs` (M) | B11: `PUT {id}/evidence`, `DELETE {id}/evidence/{filename}`, `POST {id}/generate/prepare`, `POST {id}/generate/finish` (`[DisableFormValueModelBinding]`, `MaxRequestBodySize` antes de leer, 413 por `Content-Length` sin leer). |
| `Program.cs` (M) | B12: `WithOrigins(Cors:AllowedOrigins)` en lugar de `AllowAnyOrigin`, errores a `configErrors`, `Report:MaxGenerateUploadBytes > 0`, log `CORS: orígenes permitidos …`, `CleanupOrphanGenerations()` sumado a la línea de log de subidas. |
| `appsettings.json` | **Sin cambios** (B13: los defaults van en código). |

### Tatana (`server/src/Factum.Agent`)
| Archivo | Qué |
|---|---|
| `Models/AgentModels.cs` (M) | T1: `EvidenceDirectory`, `AllowedOrigins`, `MaxUploadBytes` (16 GiB), `MinFreeBytes` (1 GiB); `ResolveEvidenceDirectory()` con el default DP4 (`<SystemDrive>\Factum\Evidencia` / `~/Factum/Evidencia` / `<DataDirectory>/evidencia`), nunca `MyDocuments`. |
| `Common/OriginPolicy.cs` (nuevo) | T2: `Parse`, `TryNormalize`, `IsAllowed` (puro). |
| `Common/AgentFileNames.cs`, `Common/AgentErrorCodes.cs` (nuevos) | T3: `IsValidName`, `IsZipFilename`, `CaseFolderName`, `TryParseCaseId`, `IsSyncedFolder`, `Sanitize`, `SafeChild`; códigos de §8.2. |
| `Services/CaseEvidenceStore.cs` (nuevo) | T4: `ICaseEvidenceStore` (singleton): import (move + hash, `file_exists`/`file_busy`), upload-check, staging atómico `.upload-tmp/<caseId>.<guid>.part` + fsync + rename, list, archivo, borrado, `CleanupOrphanUploads`; `AgentHttpException` → `{ error, code, … }`; sondeo `DriveInfo` fail-open. |
| `Controllers/CaseEvidenceController.cs` (nuevo) | T5: las 6 rutas de §6.2 bajo `cases/{caseId}`. La subida fija `MaxRequestBodySize` antes de leer. |
| `Services/Evidence/EvidenceZip.cs` (nuevo) + `Factum.Agent.csproj` (M) | T6: copia de la gemela del backend con cabecera que la referencia; único agregado: callback opcional de progreso en `WriteAsync`. `SharpZipLib 1.4.2`. |
| `Services/CaseZipService.cs` (nuevo) | T7: `BuildAsync` (semáforo por caso, `zip_already_committed`, espacio, verificación `missing/size/hash`, `.part` → pendiente, hash, `VerifyAsync`, `.sha256`, `zip_progress`), `CommitAsync` (idempotente con `estado.json`), `DiscardPending`, `Status`, `FinalZipPath`. |
| `Services/FolderReveal.cs`, `Controllers/CaseZipController.cs` (nuevos) | T8: `explorer.exe /select,<zip>` / `open -R` / `xdg-open <dir>` con `ArgumentList`; las 6 rutas de §6.3. |
| `Controllers/HealthController.cs` (M) | T9: `capabilities: ["case_evidence_v1"]` en `/health`; `evidence_directory` y `evidence_directory_synced` en `/info`. |
| `Program.cs` (M) | T2/T10: guarda de origen (403 `origin_not_allowed` sin headers CORS, también `OPTIONS` y `/ws`), CORS por origen con `Vary`, `Max-Age`, PNA; se sacó `AddCors/UseCors`; validación al arrancar (`InvalidOperationException`); registro de servicios; `CleanupOrphanUploads()`; log de §4.2 + `Warning` si la carpeta está sincronizada. |
| `appsettings.json` | **No tocado**: sigue solo el `"Mock": true` local del usuario (sin commitear). |

### README y tests
- `README.md` (M): tabla de config con `Report:MaxGenerateUploadBytes`, `Cors:AllowedOrigins`, `Agent:EvidenceDirectory`, `Agent:AllowedOrigins`, `Agent:MaxUploadBytes`, `Agent:MinFreeBytes`; nota de `.generate-tmp/`; sección "Evidencia en la PC del perito" con las 3 etapas.
- `server/tests/Factum.Backend.Tests/` (nuevos): `EvidenceManifestTests`, `CorsOriginsTests`, `ReportServiceAgentTests`, `StorageGenerateTmpTests`.
- `server/tests/Factum.Agent.Tests/` (nuevos): `OriginPolicyTests`, `EvidenceDirectoryTests`, `AgentFileNamesTests`, `CaseEvidenceStoreTests`, `EvidenceZipTests` (copia), `CaseZipServiceTests`. Todo en `Path.GetTempPath()/<guid>`.

No toqué `client/`, `agent-ui/`, `progress/sesiones/` ni el Project.

## Contrato compartido (§8) — confirmación

Los nombres JSON coinciden con §8.1 y §8.2 (y los crucé en solo lectura contra `client/src/lib/api.ts`, `client/src/lib/agent.ts` y `client/src/types/index.ts` del frontend en curso: mismos nombres). Verificado contra las respuestas reales del recorrido:
- Case: `evidence_storage` (siempre resuelto), `evidence[{filename,size,sha256,source_path,registered_at}]`, `evidence_host{hostname,os_user,agent_version,case_directory,registered_at}`, `zip_location{hostname,directory,path}`; `pending_generation` nunca viaja.
- `prepare` → `{generation_id, zip_filename, case_ref, password, encrypted, files, report_images}`; `finish` → `{case, zip_hash, password, files:{zip,pdf}, report_hash, zip_location}`.
- Errores con `code` + extras: `evidence_hostname`, `filename`, `mismatched`, `missing_images`, `max_bytes`.
- Tatana: `EvidenceFileInfo{filename,size,sha256,saved_at}`, `{directory, files[{filename,size,modified_at}]}`, `{max_upload_bytes}`, `BuildZipResponse{zip_filename,zip_hash,zip_size,encrypted,encryption?,directory,zip_path,hostname,files}`, `{zip_path,deleted}`, `{state,zip_path,directory,pending_hash?,committed_hash?}`, `zip_progress{case_id,phase,done_bytes,total_bytes}`, `/health.capabilities`, `/info.evidence_directory(_synced)`.
- **Sin desvíos de nombres.**

## Decisiones no obvias (dentro del alcance; ninguna cambia el contrato)

1. **`PUT …/evidence` con `items` vacío o nulo → 400 `invalid_manifest`.** La SDD no lo decía explícitamente.
2. **Upsert conserva `source_path`** de un ítem previo si el nuevo llega con `null` (p. ej. re-registro tras recarga). `FileSources` se deduplica por nombre: una ruta nueva reemplaza la anterior del mismo archivo.
3. **`DELETE …/evidence/{filename}` con nombre no plano → 400 `{ error }` sin `code`**: la SDD no definía el código y no quise inventar uno nuevo en el contrato.
4. **`finish` relee el caso después de recibir las capturas** y revalida que siga el mismo `PendingGeneration.Id`, que el caso siga editable y que el manifiesto no haya cambiado (→ 409 `generation_stale`). Así también revalida textos/roles editados mientras Tatana armaba el ZIP.
5. **Pasos 8-9 de `finish` sin el `CancellationToken` del request:** si el navegador se corta justo ahí, el caso igual queda `completed` y el ZIP pendiente (con ese hash) lo confirma el auto-commit, en vez de quedar en `error` con un ZIP huérfano.
6. **Ante error en pasos 8-9, además de `Status = Error`, se hace `$unset` de `PendingGeneration`** (`MarkAgentGenerationFailedAsync`): el reintento exige un `prepare` nuevo, que es lo que hace el cliente (y descarta el pendiente). Verificado en el recorrido.
7. **Errores de multipart:** sin boundary o sin parte `metadata` primero, metadata > 1 MiB o JSON inválido → 400 `invalid_zip_info`; parte inesperada/repetida/fuera de `report_images` → 400 `invalid_manifest` (+`filename`); multipart cortado o mal formado → 400 `{ error }` sin `code`; excepción inesperada → 500 `{ error }`. Ninguna sale sin JSON.
8. **Tatana `POST /cases/{id}/zip` con `files` vacío o nombres inválidos/repetidos → 400 `invalid_filename`.** Un `size`/`sha256` mal formado no se rechaza aparte: cae como `evidence_changed` (`size`/`hash`).
9. **`DELETE zip/pending` y `commit` comparten el semáforo del caso:** si hay un ZIP armándose responden 409 `zip_in_progress` (no 204), para no borrar debajo de una escritura en curso. Fuera de ese caso, el descarte es 204 siempre.
10. **Ids de caso en Tatana:** se acepta Guid "D" en cualquier casing y se usa la forma canónica en minúscula para las carpetas (la del backend), así no hay dos carpetas para el mismo caso en un FS sensible a mayúsculas.
11. **`OPTIONS` sin `Origin` sigue respondiendo 204** (como antes); con `Origin` no permitido, 403.
12. **`zip_progress`:** throttle de 500 ms sin envíos solapados al mismo WebSocket (un `SendAsync` a la vez) + uno forzado al cambiar de fase y otro al terminar.

## Verificación

### Builds
```
## dotnet build server/src/Factum.Backend/Factum.Backend.csproj --no-incremental
    0 Errores
    4 Advertencia(s)   ← las mismas del baseline (NU1902 SharpCompress, NU1903 Snappier, x2); ninguna nueva
## dotnet build server/src/Factum.Agent/Factum.Agent.csproj --no-incremental
    0 Advertencia(s)
    0 Errores
```

### Tests
```
## dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
Correctas! - Con error:     0, Superado:   452, Omitido:     0, Total:   452 - Factum.Backend.Tests.dll (net10.0)
## dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj
Correctas! - Con error:     0, Superado:   138, Omitido:     0, Total:   138 - Factum.Agent.Tests.dll (net10.0)
```

### Recorrido de punta a punta (§11.2) con curl

Entorno (regla dura §11.1): copias de `bin/` de los dos proyectos en `<scratch>` (scratchpad de la sesión); backend en `:18080` con `MongoDb__DatabaseName=factum_e2e_zip_local` y `Storage__DataDirectory=<scratch>/data`; Tatana `--mock --port 18765 --data <scratch>/agent-data` con `Agent__EvidenceDirectory=<scratch>/Evidencia`. Solo se importaron archivos generados por la prueba. La plantilla se renombró **solo en la copia scratch de `bin/`**. Al terminar se pararon los procesos, se borró la base propia `factum_e2e_zip_local` (creada por la prueba, 3 documentos propios) y las carpetas scratch.

Arranque:
```
CORS: orígenes permitidos http://localhost:3000, http://127.0.0.1:3000
Subidas: tope 4 GB, margen libre 1 GB, espacio libre en <scratch>/data: 7,2 GB (temporales huérfanos borrados: 0; generaciones huérfanas borradas: 0)
Evidencia: carpeta de trabajo <scratch>/agent-data/cases, ZIP en <scratch>/Evidencia; orígenes permitidos http://localhost:3000, http://127.0.0.1:3000 (temporales huérfanos borrados: 0)
/health → {"status":"ok","mock":true,"capabilities":["case_evidence_v1"]}
/info   → {"hostname":"MacBook-Air-de-Joel","os_user":"…","version":"2.0.0","mode":"installed","evidence_directory":"<scratch>/Evidencia","evidence_directory_synced":false}
```

#### Flujo principal (pasos 1-6)
```

=== 1. Login + perfil + caso nuevo ===
{"exists":true,"is_complete":true}
{"id":"94590ed4-ad30-4b95-ba64-eb53a32f94b0","status":"draft","evidence_storage":"agent","evidence":[],"evidence_host":null,"zip_location":null}
CASE_ID=94590ed4-ad30-4b95-ba64-eb53a32f94b0

=== 2. Screenshot mock -> import -> PUT evidence ===
screenshot=screenshot_20261006_123330_203.png

=== 2b. Origin ajeno -> 403 sin efectos (el archivo sigue en la raíz) ===
{"error":"Origen no permitido","code":"origin_not_allowed"}HTTP 403 ACAO=[]

<scratch>/agent-data/screenshot_20261006_123330_203.png
sigue en la raíz: OK
{"filename":"screenshot_20261006_123330_203.png","size":197,"sha256":"5f24aa1508ee160541abd93b011d98f218992e2b954ac7afb22ae4ca840f8157","saved_at":"2026-10-06T15:33:30.557714Z"}
Access-Control-Allow-Headers: Content-Type
Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS
Access-Control-Allow-Origin: http://localhost:3000
Access-Control-Max-Age: 600
Vary: Origin
{"n":1,"host":"MacBook-Air-de-Joel"}

=== 3. Subida a Tatana de 50 MB ===
{"max_upload_bytes":17179869184}
{"filename":"camara_externa_e2e.webm","size":52428800,"sha256":"66678f89bec9bb517c5552dcb950d8f02e9d3b36ea4507d8738c7fddae7ad087","saved_at":"2026-10-06T15:33:32.050977Z"}
sha local: 66678f89bec9bb517c5552dcb950d8f02e9d3b36ea4507d8738c7fddae7ad087
{"n":2}
upload corta:
       0

=== 3b. Endpoints viejos sobre caso agent -> 409 evidence_on_agent ===
{"error":"La evidencia de este caso se guarda en la PC del perito (Tatana); este endpoint es del flujo anterior","code":"evidence_on_agent"}
{"error":"La evidencia de este caso se guarda en la PC del perito (Tatana); este endpoint es del flujo anterior","code":"evidence_on_agent"}
{"error":"La evidencia de este caso se guarda en la PC del perito (Tatana); este endpoint es del flujo anterior","code":"evidence_on_agent"}

=== 4. capture-roles imei + 5. textos mínimos ===
{"capture_roles":[{"filename":"screenshot_20261006_123330_203.png","role":"imei_modelo"}]}
{"formato":"markdown"}
{"images":[{"filename":"screenshot_20261006_123330_203.png","size":197,"role":"imei_modelo","available":true,"width":null,"height":null}]}
[{"name":"camara_externa_e2e.webm","size":52428800},{"name":"screenshot_20261006_123330_203.png","size":197}]

=== 6. prepare -> zip -> finish -> commit ===
{"generation_id":"f0c953957b26410b836dabe9856393e3","zip_filename":"evidencia_E2E_2026_Titular_Prueba.zip","case_ref":"E2E/2026","encrypted":true,"files":2,"report_images":["screenshot_20261006_123330_203.png"],"has_password":true}
{"status":"draft","pending_visible":false}
{"zip_filename":"evidencia_E2E_2026_Titular_Prueba.zip","zip_hash":"32b27238472e92f563346ac88f742fd9666cd914d11936657920eb7263ab4740","zip_size":52445367,"encrypted":true,"encryption":"aes256-ae2","directory":"<scratch>/Evidencia/E2E_2026_94590ed4","zip_path":"<scratch>/Evidencia/E2E_2026_94590ed4/evidencia_E2E_2026_Titular_Prueba.zip","hostname":"MacBook-Air-de-Joel"}

=== 6a. finish con files alterado -> 409 manifest_mismatch, caso sigue draft ===
{"error":"Los archivos verificados en la PC no coinciden con los registrados en el expediente","code":"manifest_mismatch","mismatched":["camara_externa_e2e.webm"]}
{"status":"draft"}

=== 6b. finish OK ===
{"status":"completed","evidence_storage":"agent","zip_hash":"32b27238472e92f563346ac88f742fd9666cd914d11936657920eb7263ab4740","files":{"zip":"evidencia_E2E_2026_Titular_Prueba.zip","pdf":"informe_pericial_E2E_2026_Titular_Prueba.docx"},"report_hash":"a49daee75d148feecd05b0415fa3d6f241c3f213b14111c9fa3fd2d32f397582","zip_location":{"hostname":"MacBook-Air-de-Joel","directory":"<scratch>/Evidencia/E2E_2026_94590ed4","path":"<scratch>/Evidencia/E2E_2026_94590ed4/evidencia_E2E_2026_Titular_Prueba.zip"},"has_password":true}
{"zip_path":"<scratch>/Evidencia/E2E_2026_94590ed4/evidencia_E2E_2026_Titular_Prueba.zip","deleted":2}
{"state":"final","zip_path":"<scratch>/Evidencia/E2E_2026_94590ed4/evidencia_E2E_2026_Titular_Prueba.zip","directory":"<scratch>/Evidencia/E2E_2026_94590ed4","committed_hash":"32b27238472e92f563346ac88f742fd9666cd914d11936657920eb7263ab4740"}
commit repetido (idempotente):
{"zip_path":"<scratch>/Evidencia/E2E_2026_94590ed4/evidencia_E2E_2026_Titular_Prueba.zip","deleted":0}
```
(“upload corta”: curl con `Content-Length: 2000` y 1000 bytes; el servidor espera el resto y curl corta por timeout; `.upload-tmp` queda con 0 archivos. La respuesta `incomplete_upload` está cubierta por `CaseEvidenceStoreTests`.)

#### Verificaciones en disco, Mongo y DOCX
```
--- find <scratch>/data -type f
./cases/94590ed4-ad30-4b95-ba64-eb53a32f94b0/informe_pericial_E2E_2026_Titular_Prueba.docx
--- .generate-tmp: (vacía)
--- carpeta de trabajo del caso: agent-data/cases/94590ed4-… → No such file or directory (se borró al quedar vacía)
--- <scratch>/Evidencia:
./E2E_2026_94590ed4/.factum/estado.json
./E2E_2026_94590ed4/evidencia_E2E_2026_Titular_Prueba.zip
sha256 ZIP final : 32b27238472e92f563346ac88f742fd9666cd914d11936657920eb7263ab4740
zip_hash Mongo   : 32b27238472e92f563346ac88f742fd9666cd914d11936657920eb7263ab4740
en DOCX          : 32b27238472e92f563346ac88f742fd9666cd914d11936657920eb7263ab4740   (unzip -p word/document.xml | grep)
report_hash Mongo / sha DOCX: a49daee7…7582 / a49daee7…7582
doc Mongo: Status 2 (completed), EvidenceStorage 'agent', Evidence [camara_externa_e2e.webm, screenshot_…png],
           EvidenceHost 'MacBook-Air-de-Joel', ZipLocation {Hostname, Directory, Path}, PendingGeneration undefined,
           ZipEncrypted true, ZipEncryption 'aes256-ae2', ZipPassword presente
bsdtar -x --passphrase <pwd>: camara_externa_e2e.webm 66678f89…d087 (= sha de la subida), screenshot_….png 5f24aa15…8157 (= import)
```

#### Origen, CORS y WebSocket
```
POST /cases/<id>/evidence/import con Origin: https://evil.example → 403 {"error":"Origen no permitido","code":"origin_not_allowed"}, sin ACAO; el archivo sigue en la raíz
/ws Origin ajeno      → HTTP 403
/ws Origin permitido  → HTTP 101
import con Origin http://localhost:3000 → Access-Control-Allow-Origin: http://localhost:3000, Vary: Origin, Allow-Methods GET, POST, PUT, DELETE, OPTIONS, Allow-Headers Content-Type, Max-Age 600
preflight Tatana con Access-Control-Request-Private-Network: true → 204 + Access-Control-Allow-Private-Network: true
preflight Tatana Origin ajeno → 403 sin headers CORS
Tatana sin Origin (curl/Electron) → 200
backend Origin ajeno: GET /health 200 y OPTIONS 204, ambos SIN Access-Control-Allow-Origin
backend Origin http://localhost:3000: Access-Control-Allow-Origin: http://localhost:3000 (GET y preflight con authorization,content-type)
```

#### Fallas a mitad
```
{"generation_id":"f5209ce6dc85439a9a121f676cd37c02","report_images":["screenshot_20261006_123550_993.png"]}
zip pendiente: fc255fdea07190f70ef611e492cb9806afeb2ca2dbf1d765b422914435f807ad
--- finish sin la imagen -> missing_images:
{"error":"Faltan capturas que el informe necesita","code":"missing_images","missing_images":["screenshot_20261006_123550_993.png"]}
--- finish con imagen alterada -> image_hash_mismatch:
{"error":"La captura screenshot_20261006_123550_993.png no coincide con el hash registrado en el expediente","code":"image_hash_mismatch","filename":"screenshot_20261006_123550_993.png"}
--- finish con generation_id viejo -> generation_stale:
{"error":"La evidencia del caso cambió mientras se generaba; volvé a intentar.","code":"generation_stale"}
--- finish con encrypted cambiado -> invalid_zip_info:
{"error":"Los datos del ZIP no son válidos","code":"invalid_zip_info"}
--- finish con otra PC en zip_location -> evidence_on_other_pc:
{"error":"La evidencia de este caso está en la PC MacBook-Air-de-Joel. Seguilo desde esa PC.","code":"evidence_on_other_pc","evidence_hostname":"MacBook-Air-de-Joel"}
--- Content-Length > tope -> 413 sin leer:
{"error":"El pedido supera el máximo permitido (256 MB)","code":"request_too_large","max_bytes":268435456}
{"status":"draft"}
--- plantilla renombrada (solo en la copia scratch de bin/) -> error:
{"error":"Error generando informe: Plantilla DOCX no encontrada"}
{"status":"error","evidence_storage":"agent"}
data del caso: []; .generate-tmp: []
--- DELETE zip/pending:
HTTP 204
pendiente: []  sueltos: screenshot_20261006_123550_993.png
--- reintento tras error: prepare de nuevo OK:
{"generation_id":"c3e3964113a44f4cb963ddd43bd756f1","report_images":["screenshot_20261006_123550_993.png"]}
```
Antes de esto, sobre el mismo caso: `PUT evidence` con `host.hostname` = `OTRA-PC` y `prepare` con `OTRA-PC` → 409 `evidence_on_other_pc` (+`evidence_hostname`); `prepare` sin textos ni rol → 400 `{ error, missing }` con las claves de siempre (`report_texts.*`, `capture_roles.imei_modelo`).

Eventos `zip_progress` capturados por WS (Origin permitido) durante el `POST /zip` de ese caso:
```
HTTP/1.1 101 Switching Protocols
{"type":"zip_progress","timestamp":"2026-10-06T15:36:56.512596Z","data":{"case_id":"2139878f-8184-40d1-976d-e979bd16c568","phase":"hashing","done_bytes":0,"total_bytes":197}}
{"type":"zip_progress","timestamp":"2026-10-06T15:36:56.514015Z","data":{"case_id":"2139878f-8184-40d1-976d-e979bd16c568","phase":"zipping","done_bytes":0,"total_bytes":197}}
{"type":"zip_progress","timestamp":"2026-10-06T15:36:56.520241Z","data":{"case_id":"2139878f-8184-40d1-976d-e979bd16c568","phase":"verifying","done_bytes":0,"total_bytes":384}}
{"type":"zip_progress","timestamp":"2026-10-06T15:36:56.525095Z","data":{"case_id":"2139878f-8184-40d1-976d-e979bd16c568","phase":"verifying","done_bytes":384,"total_bytes":384}}
```

#### Borrador viejo simulado (§11.1)
Documento de prueba nuevo en la base propia, con `$unset` de `EvidenceStorage` **solo por su `_id`**, y un PNG de prueba copiado a `<scratch>/data/cases/<ese id>/`:
```
CASE_VIEJO=6975aa40-88d1-4ff7-9451-ad6fba8e99ff
1
GET sin archivos en el servidor -> agent (resuelto):
{"evidence_storage":"agent"}
GET con evidencia en el servidor -> server:
{"evidence_storage":"server","files":["screenshot_viejo.png"]}
PUT evidence -> 409 evidence_on_server:
{"error":"Este caso tiene la evidencia en el servidor y sigue con el flujo anterior","code":"evidence_on_server"}
prepare -> 409 evidence_on_server:
{"error":"Este caso tiene la evidencia en el servidor y sigue con el flujo anterior","code":"evidence_on_server"}
flujo viejo: POST /files + roles + textos + POST /generate:
{"name":"adjunto_viejo.txt","size":9}
{"capture_roles":[{"filename":"screenshot_viejo.png","role":"imei_modelo"}]}
{"status":"completed","evidence_storage":"server","files":{"zip":"evidencia_E2E_VIEJO_Titular_Viejo.zip","pdf":"informe_pericial_E2E_VIEJO_Titular_Viejo.docx"},"zip_location":null,"has_password":true}
archivos del caso viejo en el servidor: evidencia_E2E_VIEJO_Titular_Viejo.zip informe_pericial_E2E_VIEJO_Titular_Viejo.docx 
download ZIP viejo: HTTP 200
[{"nro_referencia":"E2E-VIEJO","status":"completed","evidence_storage":"server"},{"nro_referencia":"E2E-FALLA","status":"error","evidence_storage":"agent"},{"nro_referencia":"E2E/2026","status":"completed","evidence_storage":"agent"}]
```

#### Caso `completed` preexistente de `factum_dev` (solo lectura)
Segunda instancia del backend (`:18081`) con `MongoDb__DatabaseName=factum_dev` y `Storage__DataDirectory=<scratch>/data2`, donde **copié** (cp -Rp, lectura) solo la carpeta del caso `92fdedc1-…` de `dev-data/`, para no ejecutar la limpieza de arranque (`.upload-tmp`/`.generate-tmp`) sobre la carpeta real. Login dev (no escribe) + GET + download:
```
{"status":"completed","evidence_storage":"server","evidence":[],"evidence_host":null,"zip_location":null,"files":["evidencia_1234_titular.zip","informe_pericial_1234_titular.docx"]}
download ZIP: HTTP 200 (746 bytes)
[{"id":"087a085d","status":"completed","evidence_storage":"server"},{"id":"92fdedc1","status":"completed","evidence_storage":"server"},{"id":"e661dce0","status":"completed","evidence_storage":"server"}]
factum_dev después: 0 docs con EvidenceStorage; 0 con Evidence (nada escrito)
```

## Datos de desarrollo
- `factum_dev`: solo lectura (GET de casos y descarga). Ningún documento modificado (verificado arriba).
- `server/src/Factum.Backend/dev-data/`: solo se leyó (copia de una carpeta de caso a scratch). No se escribió ni se arrancó el backend sobre esa carpeta.
- `server/src/Factum.Agent/agent-data/`: no se usó (Tatana corrió con `--data <scratch>/agent-data`).
- Carpeta default de evidencia (`~/Factum/Evidencia`): no se creó (tests y recorrido con `EvidenceDirectory` propio).
- `server/src/Factum.Agent/appsettings.json`: no tocado (sigue el `"Mock": true` local del usuario, sin commitear).

## Bloqueos
Ninguno.

## Pendiente para el usuario (no se puede probar acá)
- §11.3 completo con el front (lo hace el frontend + prueba manual), en especial: "Mostrar en carpeta" real (Explorador en Windows / Finder en macOS; acá solo se probó que el endpoint arma el comando correcto por SO en código, no se abrió el Finder), "Guardar una copia…", y el default `C:\Factum\Evidencia` en una PC Windows.
- Dispositivo real (Android/iOS): el recorrido usó el modo `--mock` de Tatana. En Windows, verificar `file_busy` al importar una grabación mientras ffmpeg genera variantes.
- Navegador con Local Network Access / Private Network Access desde un dominio HTTPS (§9.3, punto 8 de §11.3).
