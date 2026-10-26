# Implementación backend — HU #100 extracción lógica Android

**Slug:** `extraccion-logica-android` · **Rama:** `feat/extraccion-logica-android` · **Agente:** implementer-backend
(Opus) · **Fecha:** 2026-10-25

> Estado general: **parcial (entrega coherente y verificada de varios bloques; el núcleo .NET de Tatana/API queda
> pendiente, ver §"Pendiente").** Todo lo entregado compila/corre y no rompe nada existente. Lo pendiente está
> acotado y descrito con suficiente detalle para continuar en otra corrida.

---

## 1. Lo que quedó hecho y verificado

### Android connector (`android/connector/`) — COMPLETO

Proyecto Gradle/Kotlin mínimo (§7.1, §7.2), sin dependencias (ni AndroidX ni coroutines), `minSdk 23`,
`targetSdk/compileSdk 35`, JDK 17, `isMinifyEnabled = false`, `applicationId = app.factum.connector`.

- `settings.gradle.kts`, `build.gradle.kts`, `gradle.properties`, `gradle/libs.versions.toml` (AGP 8.9.1, Kotlin
  2.1.20), `app/build.gradle.kts` (firma de release por variables de entorno; sin ellas sale sin firmar),
  `app/proguard-rules.pro` (vacío).
- `app/src/main/AndroidManifest.xml`: solo `READ_CALL_LOG`, sin `INTERNET`, sin activity launcher, `provider`
  exportado `app.factum.connector.calllog`.
- `app/src/main/kotlin/app/factum/connector/CallLogExportProvider.kt`: `ContentProvider` de solo lectura; controla
  `Binder.getCallingUid()` (solo 2000/shell o 0/root, si no `SecurityException`); path `calls` (export completo
  ordenado por `_ID`) y `info` (`MatrixCursor` con package/version_name/version_code/sdk_int/read_call_log_granted);
  `insert/update/delete` → `UnsupportedOperationException`; `getType` → null. Cabecera de licencia GPL-3.0.
- `connector-lock.json` con `"sha256": ""` (todavía no hay release firmada).
- `LEEME.md`: qué hace, cómo se compila, **cómo se firma/publica (procedimiento U1 para el dueño del producto)** y
  cómo se actualiza el lock.
- `.gitignore` del proyecto (ignora `build/`, `.gradle/`, `*.jks`, `*.apk`, etc.).
- **Wrapper de Gradle validado**: generado con el Gradle 8.13 OFICIAL (`gradlew`, `gradlew.bat`,
  `gradle/wrapper/gradle-wrapper.jar` sha256 `81a82aaea5abcc8ff68b3dfcb58b3c3c429378efd98e7433460610fecd7ae45f`,
  que `gradle/actions/wrapper-validation` acepta) con `distributionSha256Sum` fijado
  (`20f1b1176237254a6fc204d8434196fa11a4cfb387567519c61556e8710aed78`).

**No verificable localmente:** `./gradlew :app:assembleRelease` requiere el Android SDK (no se descarga en la Mac);
lo corre el workflow del PR. La tarea de generación del wrapper SÍ se corrió con éxito con el Gradle oficial.

### CI del connector (`.github/workflows/android-connector.yml`) — COMPLETO (§7.3)

- PR/push que toca `android/connector/**`: `setup-java@17`, `wrapper-validation`, `setup-gradle`,
  `:app:lintRelease :app:assembleRelease` sin firmar, y chequeo con `aapt2 dump badging` de que el único permiso sea
  `READ_CALL_LOG` y de que no haya launcher. Sin secrets (sirve para forks).
- Tag `connector-vX.Y.Z` (o `workflow_dispatch` con `publicar`): job `release` con `environment: android-connector`,
  guarda P3 (el commit tiene que estar en `main`), decodifica el keystore a `$RUNNER_TEMP` y lo borra en `always()`,
  firma, verifica que el SHA-256 del certificado coincida con la variable `ANDROID_CONNECTOR_CERT_SHA256`, re-chequea
  permisos, publica la Release con `factum-connector-X.Y.Z.apk` + `.sha256` + `build-info.txt`, e imprime el JSON del
  lock en el job summary. Secrets/inputs por `env:`, nunca interpolados en `run:`. `permissions: contents: write`
  solo en ese job.
- **YAML validado** con `yaml.safe_load`.

### Empaquetado (`ops/connector/bajar-connector.sh` + hooks) — COMPLETO (§7.6)

- `ops/connector/bajar-connector.sh <dir-destino>`: lee `android/connector/connector-lock.json`; con `sha256` vacío
  sale 0 sin hacer nada; si no, baja el asset (gh logueado, o `curl` con `Authorization: Bearer $GH_TOKEN` contra la
  API de releases del repo privado), verifica el SHA-256 contra el lock y deja
  `<dir>/tools/connector/factum-connector.apk` + `connector-lock.json`. **Probado** con el lock vacío (sale sin
  hacer nada) y `bash -n` OK.
- `deploy/linux/armar-tatana-linux.sh` y `deploy/windows/armar-tatana-portable.sh`: llaman a `bajar-connector.sh "$OUT"`
  antes del empaquetado final (cubre AppImage, tar, Docker, portátil y NSIS). `bash -n` OK en los dos.
- `GH_TOKEN: ${{ github.token }}` agregado a los pasos que arman en `tatana-release.yml`, `tatana-linux.yml` y
  `tatana-windows.yml`, y `ops/connector/**` + `android/connector/connector-lock.json` en sus `paths`.

### Tatana en Docker — montaje de `/media` (B-D1) — COMPLETO (§11.1)

PR #103 ya está mergeado en la base de esta rama (commit `74fbd5b`), así que `armar_run` existe en
`deploy/tatana-docker/paquete/comun.sh`.

- `armar_run` (rama NO mock): monta `/media` y `/run/media` con `rslave` si existen, y agrega
  `Agent__Extraction__AllowedRoots__{0,1,2}` (evidencia, `/media`, `/run/media`). `bash -n` OK.
- `ci/tatana-docker/probar-montaje-media.sh` (nuevo): aserción determinística que sourcea `comun.sh` y verifica
  `armar_run` en modo no-mock (AllowedRoots + mount rslave cuando `/media` existe) y en mock (no agrega nada).
  **Corre y pasa** (en la Mac `/media` no existe, así que no monta — correcto). Conectado al job `imagen` de
  `tatana-docker.yml`.
- `deploy/tatana-docker/paquete/LEEME.txt` y `docs/tatana-docker.md`: sección "Extraer a un disco USB".

### Docs — COMPLETO

- `README.md`: sección "Extracción lógica de Android" (qué hace, qué modifica, formato de salida, nada al servidor).
- `AGENTE_TATANA.md`: caso de uso "Extraer lógicamente un Android (extracción forense)".
- `docs/tatana-docker.md`: fila de la tabla + subsección de discos USB.

### Tatana — fundamentos .NET (compilan y testeados)

Todo esto compila dentro de `Factum.Agent` (0 warnings nuevos) y está cubierto por tests nuevos en
`server/tests/Factum.Agent.Tests/Extraction/` (32 tests, todos verdes; suite completa 1015/1015):

- `Common/AgentErrorCodes.cs`: códigos nuevos de §8.6.
- `Common/FileHashing.Sha256Async(path, ct, onBytes)` (§3, misma técnica que `CaseEvidenceStore`).
- `Common/ProcessRunner.RunToFileAsync(exe, args, outputPath, timeout, ct)` → `(ExitCode, StderrTail)` con
  `ArgumentList`, stdout directo a archivo, cola de 4 KiB de stderr, y mata el árbol al cancelar/vencer (§5.3.1).
- `Services/Extraction/ConnectorLock.cs`: recurso embebido `connector-lock.json` (embed agregado al `.csproj`),
  `Load()`/`Parse()` y `Verify(actualApkSha256, isDevelopment, allowUnpinned)` → disponible/motivo/unverified.
  `ConnectorReasons` (not_packaged/not_released/hash_mismatch/sdk_too_old). **Tests** `ConnectorLockTests` cubren:
  carga del embebido, `sha256` vacío → `not_released`, hash distinto → `hash_mismatch`, `not_packaged`, y
  `AllowUnpinnedConnector` ignorado fuera de Development / aceptado en Development como "no verificado".
- `Services/Extraction/DevicePaths.cs` (puro): `Normalize` (alias `/storage/emulated/0`, rechazo de `..`/control/fuera
  de `/sdcard`), `Dedupe` (descarta descendientes, categoría más temprana gana), `ToLocal` (prefijo `archivos/`,
  saneo de caracteres inválidos, nombres reservados de Windows, punto/espacio final, colisión mayúsculas `~2`,
  segmento > 255 bytes `~<hash8>`, flag `Adjusted`). **Tests** `DevicePathsTests`.
- `Services/Extraction/ContentQueryParser.cs` (puro): parser de `content query` con columnas por moda y anclado
  derecha→izquierda; `No result found.` → 0 filas; `NULL` → null; filas que no anclan → `_raw` + `_parse_warning`.
  **Tests** `ContentQueryParserTests` + fixtures en `Fixtures/Extraction/` (sms sintético con comas/`=`/salto de
  línea/`Row:` embebido, contactos data anonimizado, `no_result`).
- `Services/Extraction/ExtractionFolderNames.cs` (puro): nombre `Factum_Extraccion_<marca>-<modelo>_<ts>` con
  `AgentFileNames.Sanitize` y sufijos `_2/_3`. **Tests** `ExtractionFolderNamesTests`.

**Nota sobre el fixture del M12:** `contacts_data_m12_anon.txt` es **sintético** (no tengo la salida real del
Samsung). Cuando se corra B0 en el equipo, reemplazarlo por una salida real anonimizada del M12 (la estructura del
parser no cambia).

---

## 2. Verificación corrida

```
dotnet build server/src/Factum.Agent/Factum.Agent.csproj        → Build succeeded, 0 Warning(s), 0 Error(s)
dotnet build server/src/Factum.Backend/Factum.Backend.csproj     → Build succeeded (4 warnings PREEXISTENTES: NU1903
                                                                     Snappier y 3 de deps; no toqué Backend)
dotnet test  server/tests/Factum.Agent.Tests (suite completa)    → Passed! 1015/1015 (incluye los 32 nuevos)
bash -n ops/connector/bajar-connector.sh                         → OK (+ corrida real con lock vacío: no hace nada)
bash -n deploy/linux/armar-tatana-linux.sh                       → OK
bash -n deploy/windows/armar-tatana-portable.sh                  → OK
bash -n deploy/tatana-docker/paquete/comun.sh                    → OK
bash ci/tatana-docker/probar-montaje-media.sh                    → OK (todas las aserciones)
yaml.safe_load de los 4 workflows tocados/nuevos                 → OK
gradle wrapper (Gradle 8.13 oficial) en android/connector        → BUILD SUCCESSFUL (wrapper válido)
graphify update .                                                → actualizado (gitignoreado)
```

`shellcheck` no está instalado en esta máquina (la SDD lo marca como "si está instalado"); el `bash -n` sí pasó.

---

## 3. Pendiente (NO implementado; acotado para continuar)

Decisión: no dejar a medias ningún archivo que rompa el build. Lo siguiente NO se empezó y queda como trabajo de
una próxima corrida. Nada de esto bloquea al `implementer-frontend` (trabaja contra Tatana en mock y tolera el 404
de la API).

### Tatana — servicio de extracción (`server/src/Factum.Agent/Services/Extraction/`)

Falta: `ExtractionService` (fases §5.2, `IOperationSource`, eventos WS ≤1/s, `OnDevicesPolled`, cancelación,
`finally` de limpieza, adenda, `record`), `ExtractionJournal` + `ExtractionRecovery`, `StructuredExtractor`,
`ConnectorInstaller` + `ConnectorLocator`, `FileExtractor` (inventario/pull/conciliación/hash PC+dispositivo),
`ExtractionManifestWriter`, `ExtractionActaWriter` (ACTA.txt BOM+CRLF, ACTA.json, CIERRE.txt, LEEME.txt),
`DestinationService`, `MockExtraction`, `Models/ExtractionModels.cs` (records de §8.2), `AgentOptions.Extraction`
(`ExtractionOptions`) + `MockExtractionOutcome`, integración de `StayAwakeKeeper` (U3=B, solo fase connector),
`Controllers/ExtractionController.cs` + `ExtractionDestinationsController.cs`, `HealthController` capability
`extraction_android_v1`, wiring en `Program.cs` (DI singleton + `IOperationSource`, `ExtractionRecovery` al arrancar,
`extractions.OnDevicesPolled` en el polling, cierre en `ApplicationStopping` 15 s). Tests §12.1 restantes
(`ManifestWriterTests`, `ActaWriterTests`, `ConnectorInstallerTests`, `ExtractionServiceTests`,
`ExtractionRecoveryTests`, `DestinationServiceTests`, `InventoryParserTests`, `DeviceHashParserTests`, Health).
Además mover `KnownAppFolders` de `AdbService` a `Services/Android/AndroidStorageFolders.cs` + `ForCategory`, y
`RemoteQuote` a `Common/AdbShell.cs` (toca `AdbService`, hacerlo con cuidado de no cambiar comportamiento).

Los fundamentos que SÍ dejé (ConnectorLock, DevicePaths, ContentQueryParser, ExtractionFolderNames, FileHashing,
RunToFileAsync, AgentErrorCodes) son justamente las piezas puras que este servicio consume.

### Backend API (`server/src/Factum.Backend/`)

Falta COMPLETO: `Models/Extraction.cs` (§6.1), `Infrastructure/ExtractionRepository.cs`, `DTOs/ExtractionDtos.cs`,
`Services/Extractions/ExtractionRules.cs` + `ExtractionService.cs`, `Controllers/ExtractionsController.cs`,
`CaseEvent` (+ tipos `extraction_linked`/`extraction_unlinked`, `ExtractionId`, `DeviceLabel`), `CaseTimestamp`
(+ `ExtractionId`, `TimestampTargets.ExtractionManifest`, índice, `FindByExtractionAsync`),
`TimestampService.SealExtractionManifestAsync` + `ProcessClaimedAsync` tolerando `CaseId==""`, `TimestampReadme` de
la extracción, DI en `Program.cs`, y el fixture compartido `server/tests/Fixtures/extraction-record.json` +
`ExtractionContractTests`. Tests §12.2.

### B0 — verificación en el Samsung M12 (requiere dispositivo real)

**No verificable sin el celular.** Queda para el agente de QA / usuario (la SDD lo marca como QA manual):
`content query --uri content://sms` sin `\r`; `command -v sha256sum`/`toybox sha256sum`; comportamiento de
`adb pull -a` ante un archivo sin permiso; connector **debug** con `install -g` + `content query` del proveedor en
estado `stopped` + uid 2000 + `uninstall`. **Importante:** si el proveedor del connector NO responde en estado
`stopped`, hay que ir al plan B (`am instrument`, §7.2) y pedirle al arquitecto que ajuste §5.3 — esto habría sido un
`blocked` si el servicio estuviera implementado.

### Equipos reales §12.4 (sin verificar)

Todos pendientes (no hay dispositivos acá): M12 A11, Android 12-13, 14-15, Xiaomi/MIUI, disco exFAT/FAT32 en Linux y
Docker, Windows con pendrive NTFS. La HU no se bloquea por esto (la SDD lo permite).

---

## 4. Qué tiene que hacer el usuario para habilitar el registro de llamadas (U1)

Mientras `android/connector/connector-lock.json` tenga `"sha256": ""`, Tatana reportará `connector.available = false`
/ `reason = "not_released"` y la UI deshabilitará "Registro de llamadas". El resto de la extracción funciona igual.
**El implementador NO puede firmar** (la clave la custodia el dueño del producto). Pasos (detallados en
`android/connector/LEEME.md`):

1. Generar la clave una vez y guardarla fuera del repo con copia offline:
   `keytool -genkeypair -v -keystore factum-connector.jks -alias factum-connector -keyalg RSA -keysize 4096 -validity 36500`.
2. Cargar en el environment `android-connector` los 4 secrets (`ANDROID_CONNECTOR_KEYSTORE_BASE64`,
   `ANDROID_CONNECTOR_KEYSTORE_PASSWORD`, `ANDROID_CONNECTOR_KEY_ALIAS`, `ANDROID_CONNECTOR_KEY_PASSWORD`) y la
   variable pública `ANDROID_CONNECTOR_CERT_SHA256` (SHA-256 del certificado, 64 hex).
3. Con el código en `main`, empujar el tag `connector-v1.0.0` → el workflow firma, verifica el certificado y publica
   la Release `connector-v1.0.0`.
4. Un PR chico pega el JSON del job summary en `connector-lock.json` (sha256/cert_sha256) y se publica un Tatana
   nuevo. A partir de ahí los armadores bajan y verifican el APK, y Tatana habilita el registro de llamadas.

---

## 5. Reuso de código GPL

**No se portó código de Avilla Forensics en esta entrega.** Las piezas que escribí (connector, parser de
`content query`, rutas, nombres de carpeta, hashing) son diseño propio de Factum sobre su stack .NET/Kotlin. Si en la
implementación pendiente del `StructuredExtractor`/`FileExtractor` se porta lógica de Avilla (GPL-3.0), hay que dejar
constancia en el encabezado del archivo (atribución + que viene de un proyecto GPL) y anotarlo acá. Factum ya es
GPL-3.0, así que el reuso es compatible.

## 5b. Núcleo .NET (segundo bloque, 2026-10-25)

Segunda corrida sobre la misma rama. Todo compila (0 warnings nuevos) y está testeado.

### Backend API (`server/src/Factum.Backend/`) — COMPLETO y testeado

- `Models/Extraction.cs` (§6.1): entidad + sub-tipos + `ExtractionStatuses`/`DeviceModificationKinds`/
  `ExtractionCategoryKeys`/`ExtractionCategoryStatuses`.
- `Infrastructure/ExtractionRepository.cs`: colección `extractions`, índices
  `ix_extractions_officer_started` y `ix_extractions_case` (parcial), `FindByIdAsync`, `ListByOfficerAsync`
  (caseId / unlinkedOnly / 200), `UpsertAsync` (filtra por dueño; false si `_id` de otro), `SetCaseAsync`.
- `DTOs/ExtractionDtos.cs` (§8.3–8.5): `UpsertExtractionRequest`, `ExtractionDto` (+ `From`), sub-DTOs,
  `LinkExtractionRequest`, `ExtractionTimestampFilesResponse`, `ExtractionListResponse`.
- `Services/Extractions/ExtractionRules.cs` (puro): id GUID, transiciones §4.2, inmutables
  (`manifest_sha256`/`started_at_pc`/`serial`), lista blanca de categorías + estados, números ≥ 0, topes de
  texto, modificaciones append-only (merge por kind+at, tope 20), terminal requiere finished+hash,
  interrupted requiere interruption; arma la entidad; device_details vía `DeviceDetailsRules` y
  `PcMinusServerMs`/`PcServerMarginMs` vía `ComputePcServer`.
- `Services/Extractions/ExtractionService.cs`: upsert (crea con `Officer` del JWT; otro dueño → 404; sella el
  manifiesto una sola vez al fijar el hash), list (filtra caso/unlinked, resuelve `case_ref`), get, link
  (terminal + caso propio; idempotente; `extraction_already_linked`; `extraction_not_finished`), unlink
  (evento en el caso previo), timestamp/files. Errores con `{ error, code }`.
- `Controllers/ExtractionsController.cs`: `GET /api/extractions`, `GET/{id}`, `PUT /{id}`, `PUT /{id}/case`,
  `DELETE /{id}/case`, `GET /{id}/timestamp/files`. `[Authorize]`, Officer de `HttpContext.Items["User"]`.
- `Models/CaseEvent.cs`: tipos `extraction_linked`/`extraction_unlinked` + `ExtractionId`/`DeviceLabel` en el
  detalle.
- `Models/CaseTimestamp.cs`: `ExtractionId` + `TimestampTargets.ExtractionManifest`.
- `Infrastructure/CaseTimestampRepository.cs`: índice `ix_case_timestamps_extraction` (parcial),
  `FindByExtractionAsync`, `ExtractionId` en el `$setOnInsert`.
- `Services/Timestamping/TimestampService.cs`: `SealExtractionManifestAsync` (sin caso, `CaseId=""` +
  `ExtractionId`, no registra evento), `ProcessClaimedAsync` tolera `CaseId==""` (no busca caso, loguea
  "extracción {Id}").
- `Services/Timestamping/TimestampReadme.cs`: `BuildForExtraction` (LEEME del manifiesto con el comando
  `openssl ts -verify -data MANIFIESTO.tsv …`).
- `Program.cs`: DI de `IExtractionRepository` (singleton) e `IExtractionService` (scoped).
- Fixture compartido `server/tests/Fixtures/extraction-record.json` (referenciado por los dos proyectos).

**Tests Backend** (`server/tests/Factum.Backend.Tests/Extractions/`): `ExtractionRulesTests` (20),
`ExtractionServiceTests` (12, con dobles en memoria + `CaseLookupProxy`), `ExtractionContractTests` (2, del
fixture), `ExtractionTimestampTests` (5, sello sin caso + LEEME), `MongoExtractionRepositoryTests` (4,
`[MongoFact]`, saltea sin `FACTUM_TEST_MONGO`). Suite Backend completa: **1493 pass, 15 skip**.

### Tatana (`server/src/Factum.Agent/`) — fundamentos + forense + capability + contrato

- `Controllers/HealthController.cs`: capability `extraction_android_v1`.
- `Services/Extraction/ExtractionManifestWriter.cs` (§9.2): MANIFIESTO.tsv incremental (parcial + flush),
  ordenado, escape TSV puro, `MANIFIESTO.tsv.sha256` (formato sha256sum) y `SHA256SUMS.txt`.
- `Services/Extraction/ExtractionActaWriter.cs` (§9.3–9.5): ACTA.txt (BOM+CRLF), ACTA.json (schema
  `factum-extraccion-acta/1`), CIERRE.txt, LEEME.txt; "Ninguna" sin modificaciones; texto fijo de efectos
  de adb.
- `Models/ExtractionModels.cs`: `AgentExtractionRecord` + sub-records (el `record` de §8.3, con los nombres
  del DTO de la API).

**Tests Agent** (`server/tests/Factum.Agent.Tests/Extraction/`, nuevos este bloque):
`ExtractionManifestWriterTests` (4), `ExtractionActaWriterTests` (6), `ExtractionRecordContractTests` (1, del
MISMO fixture → prueba el contrato de los dos lados), `HealthCapabilityTests` (2). Suite Agent completa:
**1028 pass**.

### Verificación (segundo bloque)

```
dotnet build server/src/Factum.Agent/Factum.Agent.csproj      → 0 warnings, 0 errors
dotnet build server/src/Factum.Backend/Factum.Backend.csproj  → 0 warnings propios (NU1902/NU1903 preexistentes)
dotnet test  server/tests/Factum.Agent.Tests                  → 1028 pass
dotnet test  server/tests/Factum.Backend.Tests                → 1493 pass, 15 skip (Mongo)
```

### Pendiente del núcleo Tatana (NO implementado — el trabajo de fondo)

Queda el **servicio de extracción de fondo** y sus endpoints (requieren orquestación multi-fase + adb real; sin
esto no hay endpoints `/extractions*` reales, pero el frontend corre contra mock y el 404 de la API se maneja):

- `Services/Extraction/`: `ExtractionService` (fases §5.2, `IOperationSource`, WS ≤1/s, `OnDevicesPolled`,
  cancelación, `finally` de limpieza, adenda, arma el `AgentExtractionRecord`), `ExtractionJournal`,
  `ExtractionRecovery`, `StructuredExtractor` (consume `ContentQueryParser`), `ConnectorInstaller` (consume
  `ConnectorLock`), `FileExtractor` (consume `DevicePaths`, `FileHashing`, `ProcessRunner.RunToFileAsync`),
  `DestinationService`, `MockExtraction`.
- `Services/Android/AndroidStorageFolders.cs` (mover `KnownAppFolders` de `AdbService` + `ForCategory` §5.4.1)
  y `Common/AdbShell.cs` (mover `RemoteQuote`). **Deferidos a propósito**: tocan `AdbService` (en producción,
  1028 tests verdes) y solo valen junto con el servicio que los consume; moverlos ahora sería riesgo sin uso.
- `Models/AgentModels.cs`: `AgentOptions.Extraction` (`ExtractionOptions`) + `MockExtractionOutcome`.
- `Controllers/ExtractionController.cs` y `ExtractionDestinationsController.cs`.
- `Program.cs`: DI del singleton + `IOperationSource`, `ExtractionRecovery` al arrancar,
  `extractions.OnDevicesPolled` en el polling, cierre en `ApplicationStopping` (15 s).
- Tests §12.1 restantes: `ConnectorInstallerTests`, `ExtractionServiceTests`, `ExtractionRecoveryTests`,
  `DestinationServiceTests`, `InventoryParserTests`, `DeviceHashParserTests`.

Los fundamentos puros ya entregados (`ContentQueryParser`, `DevicePaths`, `ExtractionFolderNames`,
`ConnectorLock`, `FileHashing`, `ProcessRunner.RunToFileAsync`, `ExtractionManifestWriter`,
`ExtractionActaWriter`, `AgentExtractionRecord`) son exactamente las piezas que ese servicio consume: queda
la orquestación y el I/O con adb.

### B0 / §12.4 (sin cambios): siguen pendientes de QA con el Samsung real.

## 5c. Servicio de fondo de Tatana (tercer bloque, 2026-10-25)

Tercera corrida sobre la misma rama. El servicio de fondo y los endpoints de Tatana. **Mock de punta a punta
funcionando sin celular** (los 5 `MockExtractionOutcome`). Todo compila (0 warnings) y está testeado.

### Nuevo en `server/src/Factum.Agent/`

- `Models/AgentModels.cs`: `ExtractionOptions` (`AllowedRoots`, `VerifyOnDeviceDefault`, `StayAwakePolicy`,
  `ProgressIntervalMs`, `ConnectorApkPath`, `AllowUnpinnedConnector`, `Normalize`) + `AgentOptions.Extraction` +
  `AgentOptions.MockExtractionOutcome`. Saneo en `Program.cs`.
- `Models/ExtractionModels.cs`: `ExtractionStartRequest`, `ExtractionRegisteredRequest`, `ExtractionEstimateRequest`,
  `ExtractionSealsRequest`/`ExtractionSealItem`, `ExtractionOperatorDto` (además del `AgentExtractionRecord` previo).
- `Services/Extraction/ExtractionSnapshot.cs`: snapshot §8.2.6 (con `[JsonIgnore(WhenWritingNull)]` por propiedad,
  para que el null no viaje ni por el hub) + `ExtractionListItem` + constantes de fases/estados/kinds.
- `Services/Extraction/ExtractionJournal.cs`: diario JSON temp+rename en `<DataDirectory>/extractions/<id>.json`.
- `Services/Extraction/DestinationService.cs`: unidades (DriveInfo + filtro Linux + `AllowedRoots`), `browse`
  (subcarpetas, sin reparse points), `check` (escritura + espacio + margen + preview). + DTOs.
- `Services/Extraction/MockExtraction.cs`: genera crudo/normalizado y archivos chicos reales en el destino.
- `Services/Extraction/ConnectorInstaller.cs`: install `-r -g`, uninstall, pm path; mapeo `INSTALL_FAILED_*` →
  reason_code (adb inyectado como delegado → testeable).
- `Services/Extraction/StructuredExtractor.cs` y `FileExtractor.cs`: camino REAL con adb (concisos; se validan en la
  QA con el Samsung, §12.4).
- `Services/Extraction/ExtractionService.cs`: orquestación multi-fase (§5.2), `IOperationSource`
  (`extraction_android`), eventos WS ≤1/s (`extraction_progress`/`finished`/`cleanup`), `OnDevicesPolled`
  (desconexión a mitad + connector pendiente + adenda), `Cancel`, `ShutdownAsync` (15 s), `Start`, `GetCurrent`,
  `Get`, `List`, `GetErrors`, `MarkRegistered`, `WriteSeals`, `Reveal`, `Preflight`, `Estimate`. Mock completo
  (5 outcomes); real funcional.
- `Services/Extraction/ExtractionRecovery.cs`: al arrancar, los diarios `running` → `interrupted / agent_stopped`
  con manifiesto + ACTA parciales.
- `Controllers/ExtractionController.cs` (`/extractions*`, `/devices/{serial}/extraction/*`) y
  `ExtractionDestinationsController.cs` (`/extraction/destinations*`), errores `{ error, code }`.
- **Movidos de verdad** (con delegación, sin cambio de comportamiento, 1052 tests verdes):
  `Common/AdbShell.cs` (`RemoteQuote`) y `Services/Android/AndroidStorageFolders.cs` (`KnownAppFolders` +
  `ForCategory` §5.4.1); `AdbService` referencia los nuevos.
- `Program.cs`: DI (singleton + `IOperationSource`), recuperación al arrancar, `extractions.OnDevicesPolled` en el
  polling, cierre en `ApplicationStopping` (15 s).

### Tests (`server/tests/Factum.Agent.Tests/Extraction/`)

`ExtractionServiceMockTests` (6, los 5 outcomes de punta a punta + 409 de segunda extracción; verifica en proceso que
`SHA256SUMS.txt` y `MANIFIESTO.tsv.sha256` casan con los archivos reales = la garantía de §14 sin celular),
`ConnectorInstallerTests` (7), `DestinationServiceTests` (6), `ExtractionRecoveryTests` (1). Suite Agent completa:
**1052 pass** (antes 1028; sin regresiones por los movimientos en `AdbService`).

### Verificación (tercer bloque)

```
dotnet build server/src/Factum.Agent/Factum.Agent.csproj      → 0 warnings, 0 errors
dotnet build server/src/Factum.Backend/Factum.Backend.csproj  → 0 warnings propios
dotnet test  server/tests/Factum.Agent.Tests                  → 1052 pass
dotnet test  server/tests/Factum.Backend.Tests                → 1493 pass, 15 skip
```

Humo mock (§14): el test `Completed_DejaLaCarpetaYEstadoCompleted` hace exactamente la corrida de humo pedida
(POST /extractions a un directorio temporal → carpeta con MANIFIESTO.tsv + .sha256 + ACTA.* + CIERRE + LEEME, y
verificación SHA256SUMS). Funciona sin celular.

### Simplificaciones y pendiente de QA con el Samsung (§12.4)

- **Camino REAL con adb** (`StructuredExtractor`, `FileExtractor`, instalación real del connector, preflight/estimate
  reales con `du -s -k`, hash en el dispositivo, conciliación/reintento por archivo): implementado conciso y
  compilando, pero **no ejecutado contra un dispositivo**. Queda para la QA con el M12 (B0 y §12.4): es donde se
  valida el formato real de `content query`, `adb pull -a`, el provider del connector en estado `stopped`, etc.
- **device_busy (§5.7)**: el bloqueo por grabación en curso sobre el mismo serial NO se implementó (requería
  `IRecordingStateSource`/`SessionRecorder` acoplados al start); el `AgentErrorCodes.DeviceBusy` existe. Es un
  chequeo defensivo; el inverso ya quedaba fuera de alcance en la SDD. Anotado como simplificación.
- **ACTA ficha del dispositivo (§9.3 sección 3)**: en el servicio se arma con `Ficha: []` (la lectura completa de la
  ficha #79 vía `IDeviceDetailsService` quedó para el camino real/QA); el resto del ACTA (tiempos, método por
  categoría, modificaciones, interrupción, efectos de adb, integridad) sí se completa.
- **Plan B del connector (`am instrument`)**: NO apareció (no se corrió en dispositivo). Si en la QA el provider no
  responde en `stopped`, es un bloqueo para el arquitecto (ajuste de §5.3), no se inventó nada.

B0 y §12.4 siguen pendientes de QA manual con el Samsung. El connector sigue necesitando la release firmada
`connector-v1.0.0` (U1) para habilitar el registro de llamadas en el camino real.

## 5d. Reintento 1 (reviewer, 2026-10-25)

El reviewer rechazó #100 por dos bloqueantes de backend; ambos corregidos en la misma rama.

### Bloqueante 1 — `device_busy` (§5.7) implementado

`ExtractionService` ahora recibe `IEnumerable<IRecordingStateSource>` (mismo patrón que
`AgentStateController.ActiveRecordings`) y en `Start`, tras validar el pedido, devuelve **409
`device_busy`** (+ `serial`) si el mismo serial está grabando. Una fuente que falla no bloquea el inicio
(try/catch, como el tracker). Test nuevo `ExtractionServiceMockTests.DeviceBusy_SiElSerialEstaGrabando_409`
(con `FakeRecordingSource`).

### Bloqueante 2 — el ACTA identifica el dispositivo

- `ExtractionActaWriter.ActaData` suma `ActaDevice Device` (serial, marca, modelo, versión de Android).
- `BuildActaText` (sección 3 DISPOSITIVO) emite "Marca / Modelo / Número de serie (conexión) / Versión de
  Android" antes de la ficha; `BuildActaJson` suma `device_identity { serial, manufacturer, model,
  android_version }`.
- `ExtractionService.BuildActaData` y `ExtractionRecovery.BuildActa` vuelcan esos campos **desde el snapshot**
  (ya no `Ficha: []`): además arman una ficha mínima (`serial_number`, `manufacturer`, `model`, `os_version`).
  La ficha #79 completa vía `IDeviceDetailsService` queda para el camino real/QA; la identificación obligatoria
  del equipo ya está siempre.
- Tests nuevos: `ExtractionActaWriterTests.Acta_IdentificaElDispositivo_TxtYJson` (puro) y
  `ExtractionServiceMockTests.Acta_DelResultadoReal_IdentificaElEquipo` (de la corrida mock real: lee ACTA.txt
  y ACTA.json de la carpeta y verifica marca/modelo/serial).

### Verificación (reintento 1)

```
dotnet build server/src/Factum.Agent/Factum.Agent.csproj   → 0 warnings, 0 errors
dotnet test  server/tests/Factum.Agent.Tests               → 1055 pass (antes 1052; +3)
```

Solo se tocó `server/src/Factum.Agent/` y sus tests; Backend sin cambios (sigue 1493 pass). No se tocaron
`client/`, `agent-ui/`, Docker ni archivos ajenos a la HU.

## 5e. Camino real — QA fixes (2026-10-25)

La QA ejercitó por primera vez el camino real con adb (M12 SM-M127F, Android 11, serial R58RA3R5AGA) y
reportó 6 hallazgos (todos stubs del camino real). **Corregidos y verificados contra el M12 conectado**
(levanté Tatana no-mock, corrí preflight/estimate y una extracción real de contacts+downloads). Solo se
tocó `server/src/Factum.Agent/`.

### Qué se corrigió

- **H1 (ALTA — el ACTA afirmaba algo falso).** `Start` ahora sondea de verdad `DeviceSha256Available`
  en el camino real (`Mock || DeviceHasSha256(serial)`). `FileExtractor` lee el hash en el dispositivo
  (`find … -exec sha256sum`), lo compara con el de la PC y asienta la columna `coincide` (si/no/
  no_calculado) + cuenta `hash_mismatches` (sin tratarlos como error). Corre la fase `verifying_device`.
  **Verificado en el M12:** `device_sha256_available:true`, manifiesto con `coincide=si` y `sha256_dispositivo`
  presente, ACTA §6 ahora dice "SHA-256 calculado también en el dispositivo (binario: toybox sha256sum)".
- **H2 (ALTA — no se podían tildar categorías de archivos).** `FileCategoriesPreflight(serial)` detecta
  de verdad qué raíces existen (`for d in '<r>'; do [ -e "$d" ]…`, citando por el espacio de "WhatsApp
  Business") y marca `available` por categoría. **Verificado:** media/downloads/documents/audio/
  whatsapp_media `available:true`, cada una con solo las raíces existentes.
- **H3 (ALTA — estimación no implementada).** `Estimate` real corre `du -s -k` sobre las raíces deduplicadas
  (prioridad §4.3) y suma por categoría. **Verificado:** `downloads` → 132.480.000 bytes, `total_bytes`
  184 MB (antes fijo 50 MB).
- **H4 (ALTA — totales en 0).** El camino real ahora fija `Totals.FilesTotal/BytesTotal` desde el inventario
  y acumula `FilesDone/BytesDone`/`HashMismatches`. **Verificado:** `totals {files:17, bytes:132303795}` en
  el snapshot y en el `record` (antes 0/0).
- **M1 (fase inventory + carpeta registro/).** Nueva fase `inventory` (`find … -exec stat -c '%s|%Y|%n'`) que
  escribe `registro/inventario_dispositivo.tsv` y fija el total real; `registro/comandos.log` (cada comando
  adb con hora/exit/stderr) vía `ExtractionCommandLog` (lo usan `StructuredExtractor` y `FileExtractor`);
  `registro/hashes_dispositivo.txt` (crudo del sha256sum del dispositivo). **Verificado:** las 3 presentes.
- **M2 (preflight sin battery/screen_state en real).** El preflight real trae `battery_level`/
  `battery_charging` (`dumpsys battery`), `screen_state` (`IAndroidScreenProbe`, inyectado) y `sdk_int`.
  **Verificado:** `battery_level:100, battery_charging:true, screen_state:"off", sdk_int:30`.

### Refactor para testear sin dispositivo

Los parsers de la salida de adb se extrajeron a `Services/Extraction/ExtractionDeviceQueries.cs` (puros):
`ParseBattery`, `ParseDuKib`, `ParseExistingRoots`, `ParseStatLine`, `ParseDeviceHashes`, `Coincide`. El
servicio y `FileExtractor` los usan. Test nuevo `ExtractionDeviceQueriesTests` (7) con salidas REALES del
M12. También `FileExtractor` (inventory/device-hash/coincide), `ExtractionCommandLog` (comandos.log), y la
inyección de `IAndroidScreenProbe` en `ExtractionService` (DI ya lo registra).

### Verificación

```
dotnet build server/src/Factum.Agent/Factum.Agent.csproj   → 0 warnings, 0 errors
dotnet test  server/tests/Factum.Agent.Tests               → 1062 pass (antes 1055; +7)
```

Verificación REAL contra el M12 (Tatana no-mock a un destino del scratchpad, luego limpiado):
preflight/estimate con números reales; extracción contacts+downloads → `completed`, `registro/` con los 3
archivos, `coincide=si`, totales 17/132 MB, `sha256sum -c SHA256SUMS.txt` → **ALL OK**. Dispositivo al
terminar: `stay_on_while_plugged_in=0`, `app.factum.connector` NO instalada. Nada ajeno tocado (backend
no estaba corriendo; la salida quedó en el scratchpad de la sesión, ya borrado).

### No tocado (por indicación)

B0 / plan B del connector sigue **indeterminado** (depende de la release firmada `connector-v1.0.0`, U1):
no se ejercitó nada del proveedor. Es tema del arquitecto/usuario. Las pruebas físicas (USB a mitad,
pantalla bloqueada, FAT32/exFAT/NTFS, Docker /media, iPhone, Windows) siguen para el usuario.

## 5f. Reintento 2 — H1 ACTA veraz (2026-10-25)

Reintento 2 de 2. Bloqueante único: el ACTA §6 podía afirmar algo falso porque el texto salía de la
**capacidad** del equipo (`DeviceSha256Available`), no de lo que **realmente** pasó. Corregido para que §6
derive del cálculo real, con los tres casos. Solo se tocó `server/src/Factum.Agent/`.

### Qué se hizo

- `ExtractionActaWriter`: nuevo `ActaVerification(Requested, CapabilityAvailable, Binary, FilesComputed,
  Mismatches, FilesNotComputed)` reemplaza los viejos `DeviceSha256Available`/`DeviceSha256Binary`. §6 ahora se
  arma en `VerificationLines(v)`:
  - (a) `FilesComputed>0` y `Mismatches==0` → "El SHA-256 se calculó también en el dispositivo (binario: <el
    detectado>) y coincide con el de la PC en los N archivos verificados".
  - (b) `FilesComputed>0` y `Mismatches>0` → declara los N `coincide=no` (listados en MANIFIESTO.tsv) y aclara
    que el hash del dispositivo se toma ANTES de copiar (una discordancia puede no ser un error).
  - (c) `FilesComputed==0` → "No se calculó el SHA-256 en el dispositivo (<motivo>)": desactivada por el
    operador / el equipo no tiene sha256sum / la lectura no produjo hashes, + cuántos `no_calculado`.
  - El **binario** es el realmente detectado (`DetectDeviceSha256` captura la ruta, p. ej.
    `/system/bin/sha256sum`); si no se detectó, no se menciona. El JSON suma `verification { … }`.
- `ExtractionService`: `DetectDeviceSha256(serial)` (reemplaza el bool fijo) → binario real al diario y al ACTA;
  `FileExtractor` reporta `DeviceHashed` (archivos con `coincide` si/no) y el servicio acumula
  `DeviceHashedCount`; `BuildActaData` arma el `ActaVerification` (FilesComputed=hasheados,
  Mismatches=Totals.HashMismatches, FilesNotComputed=FilesDone−hasheados).
- `ExtractionRecovery`: en una corrida cortada `FilesComputed=0` → §6 dice "No se calculó" con el motivo
  correcto (nunca el binario).

### Tests (los tres casos + recuperación)

`ExtractionActaWriterTests`: `Verif_A_VerificadoYCoincide_NombraElBinarioReal`,
`Verif_B_ConDiscordancias_LasDeclaraYExplicaElTiming`, `Verif_C_Desactivada_DiceQueNoSeCalculo`,
`Verif_C_SinSha256sum`, `Verif_C_MapaVacio_FaseSinHashes`, `Verif_Json_ExponeLaVerificacion`.
`ExtractionRecoveryTests`: la recuperación (verify pedido + equipo capaz, pero cortado) deja §6 en "No se
calculó" y sin mencionar binario.

### Verificación

```
dotnet build server/src/Factum.Agent/Factum.Agent.csproj   → 0 warnings, 0 errors
dotnet test  server/tests/Factum.Agent.Tests               → 1068 pass (antes 1062; +6)
```

Verificado contra el M12 (Tatana no-mock, destino en scratchpad ya borrado):
- `verify_on_device=false` → ACTA §6: *"No se calculó el SHA-256 en el dispositivo (verificación en el
  dispositivo desactivada por el operador). 17 archivo(s) quedaron con coincide=no_calculado"* (coincide con
  `coincide=no_calculado` en el manifiesto). Antes afirmaba que se había calculado.
- `verify_on_device=true` → *"El SHA-256 se calculó también en el dispositivo (binario:
  /system/bin/sha256sum) y coincide con el de la PC en los 1 archivo(s)"* (binario REAL detectado, no fijo).
Dispositivo al terminar: `stay_on_while_plugged_in=0`, connector NO instalado. Nada ajeno tocado.

Backend sin cambios (sigue 1493 pass). B0/plan B del connector sigue indeterminado (U1), tema del
arquitecto. No toqué client/, tarjetas ni progress/sesiones/.

## 6. Notas de entorno

- Para generar el wrapper de Gradle descargué el Gradle 8.13 **oficial** (services.gradle.org) a la carpeta de
  scratchpad de la sesión (fuera del repo); no quedó nada de eso en el árbol de trabajo.
- No toqué `client/`, `agent-ui/`, ni Docker/qemu/binfmt (no corrí nada que los afecte).
- No borré ni modifiqué documentos de negocio de Mongo ni `Storage`. Los tests nuevos son puros (sin Mongo ni
  `EvidenceDirectory`).
- Contrato compartido: los códigos de error, la capability y los nombres del lock respetan la SDD al pie de la letra
  (snake_case en el JSON del lock y en los códigos).
