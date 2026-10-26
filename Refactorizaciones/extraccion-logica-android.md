# SDD: Extracción lógica de Android (extraer y preservar, con acta y hashes)

**Slug:** `extraccion-logica-android` · **Issue:** #100 · **HU:** `docs/hu-extraccion-logica-android.md` (D1–D14
validadas con la opción recomendada, incluidas las sub-dudas, el 2026-10-25) · **Insumo:** `docs/spike-extraccion-logica.md`
· **Autor:** architect · **Fecha:** 2026-10-25 · **Rama:** `feat/extraccion-logica-android` (desde `develop`, ver Duda U4)

El código se cita por **símbolo**. Los números de línea son una pista y pueden haber cambiado. La arqueología se hizo
sobre `5d3d9fd` (rama `feat/fiscalia-operacion`, PR #103 abierto). Para `deploy/tatana-docker/` se comparó además con
`origin/develop`.

---

## 1. Resumen funcional

Factum suma una **extracción lógica de Android** separada del wizard de inspección. Desde una sección nueva
`/extracciones`, el perito elige el celular conectado, las categorías (contactos, SMS/MMS, registro de llamadas,
calendario, apps instaladas y carpetas de `/sdcard`) y un destino: una carpeta de la PC o un disco externo que lista
Tatana. Tatana hace el trabajo en segundo plano, aunque se cierre la pestaña:

- los datos estructurados salen con `content query` y `pm list packages`, que son de solo lectura;
- el registro de llamadas sale de una app auxiliar firmada de Factum (`android/connector/`). Tatana verifica su hash,
  la instala y la desinstala siempre;
- los archivos salen con `adb pull -a`.

Cada archivo queda con SHA-256 calculado en la PC y, si el `toybox` del celular trae `sha256sum`, también con el hash
calculado en el celular. Al final quedan en la carpeta:

- un `MANIFIESTO.tsv` y su `.sha256`;
- el `ACTA.txt`/`ACTA.json`;
- un `LEEME.txt`.

La web registra la extracción en el backend como entidad propia. Manda solo metadatos y el hash del manifiesto, que
se sella con RFC 3161 si el sellado está activo; el sello vuelve a `sellos/` de la carpeta. Después la extracción se
puede asociar a un caso (0 o 1), y eso deja un evento en la trazabilidad del caso. Ningún contenido extraído llega al
servidor.

## 2. Toca

| Parte | ¿Toca? | Qué |
|---|---|---|
| **backend (Tatana)** `server/src/Factum.Agent` | **sí** | `Services/Extraction/*` (nuevo), `Controllers/ExtractionController.cs` y `Controllers/ExtractionDestinationsController.cs` (nuevos), `Models/ExtractionModels.cs` (nuevo), `Models/AgentModels.cs` (`AgentOptions.Extraction`, mock), `Services/AdbService.cs` (sacar `KnownAppFolders` a una clase compartida), `Common/ProcessRunner.cs` (runner que manda stdout a un archivo y captura stderr, con timeout), `Common/AgentErrorCodes.cs`, `Controllers/HealthController.cs` (`extraction_android_v1`), `Program.cs` (DI, recuperación al arrancar, aviso de dispositivos, apagado), `Factum.Agent.csproj` (embeber `connector-lock.json`). |
| **backend (API)** `server/src/Factum.Backend` | **sí** | `Models/Extraction.cs` (nuevo), `Infrastructure/ExtractionRepository.cs` (nuevo), `DTOs/ExtractionDtos.cs` (nuevo), `Services/Extractions/*` (nuevo: reglas puras + servicio), `Controllers/ExtractionsController.cs` (nuevo), `Models/CaseEvent.cs` (2 tipos + `ExtractionId` en el detalle), `Models/CaseTimestamp.cs` (`ExtractionId`, target `extraction_manifest`), `Infrastructure/CaseTimestampRepository.cs` (índice y lectura por extracción), `Services/Timestamping/TimestampService.cs` (`SealExtractionManifestAsync`, cola sin caso), `Services/Timestamping/TimestampReadme.cs` (LEEME de la extracción), `Program.cs` (DI). |
| **Android** `android/connector/` (nuevo) | **sí** (lo hace `implementer-backend`) | Proyecto Gradle/Kotlin mínimo, `connector-lock.json`. |
| **CI / empaquetado** | **sí** (`implementer-backend`) | `.github/workflows/android-connector.yml` (nuevo); `ops/connector/bajar-connector.sh` (nuevo); `deploy/linux/armar-tatana-linux.sh` y `deploy/windows/armar-tatana-portable.sh` (meten el APK en `tools/connector/`); `.github/workflows/tatana-release.yml`, `tatana-linux.yml` y `tatana-windows.yml` (`GH_TOKEN` para la descarga y `paths`). |
| **deploy Tatana Docker** `deploy/tatana-docker/` | **sí** (`implementer-backend`) | `paquete/comun.sh` → `armar_run` (montaje de `/media` y `/run/media` + `AllowedRoots`), `paquete/LEEME.txt`, `docs/tatana-docker.md`. **Conflicto con PR #103: ver §11.2 y Duda U4.** |
| **client** `client/` | **sí** | `app/extracciones/page.tsx`, `app/extracciones/nueva/page.tsx`, `app/extracciones/[id]/page.tsx` (nuevos), `components/extraction/*` (nuevos), `lib/extraction.ts` y `hooks/useExtraction*.ts` (nuevos), `lib/agent.ts`, `lib/api.ts`, `lib/case-events.ts`, `components/shell/AppNavbar.tsx`, `app/dashboard/page.tsx` (acceso "Nueva extracción"), `components/case-detail/CaseDetailContent.tsx` (bloque "Extracciones asociadas"). |
| **agent-ui** `agent-ui/` | **no** | D9=A. El aviso de actualización bloqueada usa el texto por defecto "operación en curso" para el kind nuevo `extraction_android`, que es aceptable. Si se quiere un texto propio, va como tarea chica aparte. |
| **docs** | **sí** (`implementer-backend`) | `README.md` (sección Extracción), `AGENTE_TATANA.md` (caso de uso), `docs/tatana-docker.md` (montaje de discos USB). |

**Reparto.** `implementer-backend` hace §13.1: Tatana, API, Android, CI, empaquetado, Docker y docs.
`implementer-frontend` hace §13.2: solo `client/`. El frontend arranca con el contrato de §8 y Tatana en modo mock
(§5.12). No necesita esperar al backend real.

---

## 3. Arqueología verificada

| Qué | Dónde | Cómo se usa acá |
|---|---|---|
| `adb` resuelto | `ToolResolver.Find(AgentTools.Adb)` (patrón `Lazy<string> AdbPath` de `AdbService`, `StayAwakeKeeper`, `DeviceDetailsService`) | El servicio de extracción hace lo mismo y corre adb con `ProcessRunner.RunArgumentListAsync` (sin shell local). |
| Citado remoto | `AdbService.RemoteQuote` (privado) | Se mueve a `Common/AdbShell.cs` (público estático) y lo usan los dos. `adb shell` une los argumentos y `adb exec-out` no (ver comentarios de `PullFileAsync`). |
| Lista de dispositivos | `AdbService.ListDevicesAsync`: solo estado `device` (no lista `unauthorized`) | Para distinguir la desconexión de la autorización revocada, el servicio corre `adb -s S get-state`. |
| Polling cada 3 s | `Program.cs`, lazo que llama a `sessions.OnDevicesPolled(serials)` | Se suma `extractions.OnDevicesPolled(serials)`: detecta la desconexión a mitad y la reconexión para desinstalar el connector pendiente. |
| Ficha + reloj (#79) | `IDeviceDetailsService.ReadAsync/MeasureClockAsync` → `DeviceDetailsResponse`, `DeviceClockResponse` | Se lee al iniciar (ficha + reloj) y al cerrar (reloj). Va al ACTA y al registro del backend. |
| Carpetas por app | `AdbService.KnownAppFolders` (privado) | Pasa a `Services/Android/AndroidStorageFolders.cs` (`internal static`), junto con las carpetas por categoría de §5.4. `FindByAppAsync` sigue igual. |
| Pantalla encendida | `Services/Session/StayAwakeKeeper` (`BeginAsync`/`ReassertAsync`/`RestoreAsync`, con estado `StayAwakeState`) | Se reusa tal cual según la Duda U3: lee el original, pone 7 y restaura. El estado se persiste en el diario de la extracción. |
| Operaciones largas | `IOperationSource` + `OperationTracker` | `ExtractionService : IOperationSource` con kind `extraction_android`. El autoupdate nativo y el de Docker (`estado-agente.py` lee `/agent/state`) no cortan mientras dure. |
| WebSocket | `IAgentEventSink.BroadcastAsync(AgentEvent)` con JSON `SnakeCaseLower` | Eventos `extraction_progress`, `extraction_finished` y `extraction_cleanup`. |
| Espacio libre | `IAgentDiskProbe.GetAvailableFreeBytes` (`CaseEvidenceStore.cs`) | Se reusa para el chequeo de destino. |
| SHA-256 en streaming | `IncrementalHash` en `CaseEvidenceStore.Sha256Async` | Helper nuevo `Common/FileHashing.Sha256Async(path, ct, onBytes)`, con la misma técnica y sin depender del store. |
| Abrir carpeta | `IFolderReveal.Reveal(filePath)` | `POST /extractions/{id}/reveal` abre la carpeta con `ACTA.txt` seleccionado. |
| Sellos a Tatana | `CaseZipService.WriteSealsAsync` + `SealFileItem` + `AgentFileNames.IsSealFilename` | Mismo formato de pedido para `PUT /extractions/{id}/seals`. Se reusa la validación de nombres y tamaños (máx. 64 KiB por sello, 256 KiB el LEEME). |
| Recurso embebido | `Factum.Agent.csproj`: `<EmbeddedResource Include="Services/Ios/ios_helper.py" …>` | Precedente para embeber `android/connector/connector-lock.json`. |
| Mock | `AgentOptions.Mock`, `MockDeviceClockSkewSeconds`… | Extracción simulada (§5.12). |
| Eventos de caso | `CaseEvent`/`CaseEventDetail`/`CaseEventTypes`, `CaseEventRepository` (append-only) | 2 tipos nuevos. |
| Sellado | `TimestampService` (todo atado a `Case`); `ProcessClaimedAsync` tolera `cas == null` (no registra evento) | Sello de la extracción sin caso: `CaseId = ""` + `ExtractionId` (§6.3). |
| Mongo | **Sin `ConventionPack`**: los nombres de elemento en Mongo son los de la propiedad C# (PascalCase). JSON de la API: `SnakeCaseLower` (`Program.cs`, controllers y minimal APIs) | Los índices se arman con lambdas. Los literales de §6 van en PascalCase. |
| Acceso | `CaseAccess.Check` (dueño; superadmin solo donde se habilita) | Extracciones: solo el dueño (`Officer.Dni`), mismo criterio que `GET /api/cases` (D6c). |
| Tests .NET | `server/tests/Factum.Agent.Tests` (xUnit, `InternalsVisibleTo`, `Fixtures/` copiados al output) y `server/tests/Factum.Backend.Tests` (`MongoFact` con `FACTUM_TEST_MONGO` y base `factum_test_<guid>`) | §12. CI: `verificar.yml` corre `dotnet test` de los dos. |
| Empaquetado | `deploy/linux/armar-tatana-linux.sh` (AppImage, y lo reusa `armar-tatana-docker.sh` con `COPY agente/ /opt/tatana/`) y `deploy/windows/armar-tatana-portable.sh` (portátil + NSIS); `tatana-release.yml` llama a los dos scripts | El APK entra en `tools/connector/` y llega solo a nativo y a Docker. No hay paquete de macOS: en Mac se usa `Agent:Extraction:ConnectorApkPath` (§7.6). |
| Navbar | `AppNavbar` → `SECTIONS` (`inspecciones`, `metricas`) y `sections.activeSection` | Se suma `extracciones`. |

---

## 4. Modelo de la extracción (común a Tatana, API y web)

### 4.1 Identificador

Tatana genera el `id` como GUID en minúscula con guiones (`Guid.NewGuid().ToString("D")`) al aceptar el inicio. El
backend usa **el mismo** `id` como `_id` (upsert idempotente). La extracción existe en el ACTA aunque el backend no
responda en ese momento (D11).

### 4.2 Estados

| `status` | Significado | UI |
|---|---|---|
| `running` | En curso | "En curso" + spinner |
| `completed` | Terminó, sin errores de archivo ni categorías fallidas | "Completa" + check |
| `completed_with_errors` | Terminó, pero algún archivo quedó con error o alguna categoría quedó `not_extracted` | "Completa con errores" + triángulo |
| `interrupted` | Corte: desconexión, autorización revocada, Tatana cerrado o destino caído | "Interrumpida" + rayo |
| `cancelled` | El perito canceló | "Cancelada" + círculo tachado |

Transiciones válidas: `running` → cualquiera de los terminales. Un terminal no cambia más; un PUT repetido con el
mismo terminal es idempotente.

### 4.3 Categorías (`key`)

| `key` | `kind` | Etiqueta UI | Default |
|---|---|---|---|
| `contacts` | `structured` | Contactos | marcada |
| `sms_mms` | `structured` | Mensajes SMS/MMS | marcada |
| `call_log` | `structured` | Registro de llamadas | marcada si `connector.available` |
| `calendar` | `structured` | Calendario | marcada |
| `apps` | `structured` | Apps instaladas | marcada |
| `media` | `files` | Fotos y videos | sin marcar |
| `downloads` | `files` | Descargas | sin marcar |
| `documents` | `files` | Documentos | sin marcar |
| `audio` | `files` | Audio y grabaciones | sin marcar |
| `whatsapp_media` | `files` | Medios de WhatsApp | sin marcar |
| `all_storage` | `files` | Todo el almacenamiento | sin marcar (D8) |
| `custom` | `files` | Carpetas elegidas | aparece solo si `custom_paths` no está vacío |

Estado por categoría (`categories[].status`): `pending` | `running` | `done` | `done_with_errors` | `not_extracted` |
`cancelled`. `not_extracted` siempre lleva `reason_code` (§4.4).

### 4.4 Códigos de motivo

- **Por categoría** (`reason_code`):
  - `permission_denied`: el proveedor tiró `SecurityException`;
  - `provider_unavailable`: no existe la URI;
  - `connector_unavailable`: el APK no viene en el paquete o falló la verificación del hash;
  - `connector_install_blocked`: `INSTALL_FAILED_USER_RESTRICTED`, "Instalar vía USB" desactivado, el usuario
    rechazó o venció el prompt de Play Protect;
  - `connector_install_failed`: cualquier otro `INSTALL_FAILED_*`, que va en `reason`;
  - `connector_conflict`: ya hay un paquete con el mismo nombre y otra firma;
  - `path_not_found`, `device_disconnected`, `cancelled`, `error`.
- **De interrupción** (`interruption.reason_code`): `device_disconnected` | `device_unauthorized` | `agent_stopped` |
  `destination_error` | `internal_error`.

La cancelación no es una interrupción: tiene su propio `status`.
- **De archivo** (columna `motivo` del manifiesto): texto libre, corto, del stderr de adb o de la excepción de E/S.

---

## 5. Tatana: cómo se hace cada cosa

### 5.1 Componentes (`server/src/Factum.Agent/Services/Extraction/`)

| Archivo | Rol |
|---|---|
| `ExtractionService.cs` | Singleton. Una extracción a la vez (D12). Conduce las fases en un `Task` propio con su `CancellationTokenSource`, y el pedido HTTP vuelve enseguida. Implementa `IOperationSource` (`extraction_android`). Publica progreso. Expone `OnDevicesPolled`. |
| `ExtractionJournal.cs` | Diario JSON por extracción en `<DataDirectory>/extractions/<id>.json`, escrito con temp + rename en cada cambio de fase y cada 5 s como mucho. Guarda estado, carpeta, opciones, progreso, `device_modifications`, `connector_pending_uninstall {serial, package}`, `stay_awake` (`StayAwakeState`), `registered`, `seals_written` y `record`. Es lo que leen `GET /extractions` y la recuperación. |
| `ExtractionRecovery.cs` | Al arrancar: todo diario en `running` pasa a `interrupted` con `agent_stopped`. Cierra el manifiesto parcial y escribe el ACTA parcial (§5.9). Si quedó `connector_pending_uninstall`, lo deja armado para `OnDevicesPolled`. |
| `StructuredExtractor.cs` | `content query` y `pm list` → crudo + normalizado (§5.3). |
| `ContentQueryParser.cs` | Parser puro de la salida de `content query` (§5.3.3). |
| `ConnectorInstaller.cs` | Verificación, instalación, consulta y desinstalación del APK (§5.5, §7). |
| `FileExtractor.cs` | Inventario, `adb pull -a`, conciliación, hash en la PC y en el dispositivo (§5.4). |
| `DevicePaths.cs` | Puro: normaliza y deduplica raíces, valida que estén bajo `/sdcard` o `/storage/emulated/0`, y mapea la ruta del dispositivo a la ruta local con saneado para Windows (§5.4.4). |
| `ExtractionManifestWriter.cs` | Manifiesto incremental (`.factum/manifiesto.parcial.tsv`) → `MANIFIESTO.tsv` + `.sha256` + `SHA256SUMS.txt` (§9). |
| `ExtractionActaWriter.cs` | `ACTA.txt` + `ACTA.json` + `CIERRE.txt` + `LEEME.txt` (§9). |
| `DestinationService.cs` | Unidades, navegación y chequeo de destino (§5.10). |
| `ExtractionFolderNames.cs` | Puro: `Factum_Extraccion_<Marca>-<Modelo>_<yyyyMMdd-HHmmss>` con `AgentFileNames.Sanitize` por parte. Si existe, suma `_2`, `_3`… |
| `MockExtraction.cs` | Simulación (§5.12). |

Y en `Services/Android/AndroidStorageFolders.cs` quedan `KnownAppFolders` (se mueve) y las carpetas por categoría.

### 5.2 Secuencia (fases, `phase` en el snapshot)

1. **`preparing`**:
   1. Valida el pedido (§8.2.4).
   2. Corre `adb get-state`: tiene que dar `device`.
   3. Lee la ficha (`DeviceDetailsService.ReadAsync`).
   4. Crea la carpeta única con `Directory.CreateDirectory` sobre una ruta que **no existía**. Antes chequea que el
      padre no sea un symlink ni un junction (`FileAttributes.ReparsePoint`).
   5. Abre el diario y el manifiesto parcial.
   6. Registra la operación.
   7. Si la Duda U3 se resuelve con A (toda la extracción), aplica `stay_awake` acá. Con B (la recomendada), lo
      hace al entrar en la fase del connector (§5.6). En los dos casos lo anota como modificación con la hora.
2. **`structured`**: una categoría por vez, en el orden `contacts`, `sms_mms`, `calendar`, `apps`, `call_log`. El
   connector va último: si su instalación se cuelga, lo de solo lectura ya está a salvo.
3. **`inventory`** (si hay categorías de archivos): `find` + `stat` en el dispositivo → `registro/inventario_dispositivo.tsv`
   y el total real de bytes.
4. **`copying`**: `adb pull -a` por raíz, conciliación, reintento por archivo.
5. **`hashing`**: SHA-256 en la PC de lo copiado, en paralelo acotado (2 hilos) y en streaming.
6. **`verifying_device`** (si `verify_on_device` y `sha256sum` existe): `sha256sum` en el dispositivo por raíz y
   comparación.
7. **`closing`**:
   1. Reloj de cierre (`MeasureClockAsync`).
   2. Desinstala el connector si sigue instalado.
   3. Restaura `stay_awake`.
   4. Escribe `ACTA.txt`/`ACTA.json` y `LEEME.txt`.
   5. Cierra `MANIFIESTO.tsv` (que incluye ACTA y LEEME), escribe `MANIFIESTO.tsv.sha256`, `SHA256SUMS.txt` y
      `CIERRE.txt`.
   6. Arma `record` (§8.3) y emite `extraction_finished`.

La desinstalación del connector y la restauración de `stay_awake` están en un `finally` que corre siempre (falla,
cancelación o desconexión). Si el celular no está, quedan `connector_pending_uninstall`/`restore_pending` en el diario.

### 5.3 Datos estructurados

#### 5.3.1 Comandos exactos (D5: crudo = evidencia primaria)

Todos se corren como `adb -s <serial> shell -T <comando>`. `-T` evita la pty: no traduce `\n`→`\r\n` y separa stderr
con el protocolo v2. stdout va **directo al archivo** con el runner nuevo `ProcessRunner.RunToFileAsync(exe, args,
outputPath, timeout, ct)`, que devuelve exit code + las últimas 4 KiB de stderr, mata el proceso al cancelar y usa
`ArgumentList`. Cada comando queda en `registro/comandos.log` con la hora de la PC (inicio y fin), los argumentos, el
exit code y el stderr resumido.

| Categoría | URIs / comando | Archivo crudo (`datos/crudo/`) |
|---|---|---|
| `contacts` | `content query --uri content://com.android.contacts/contacts` · `…/raw_contacts` · `…/data` | `contacts_contacts.txt`, `contacts_raw_contacts.txt`, `contacts_data.txt` |
| `sms_mms` | `content query --uri content://sms` · `content://mms` · `content://mms/part` (metadatos de las partes; el binario de los adjuntos queda **fuera**, ver D-T14) | `sms.txt`, `mms.txt`, `mms_part.txt` |
| `calendar` | `content query --uri content://com.android.calendar/calendars` · `…/events` · `…/attendees` · `…/reminders` | `calendar_calendars.txt`, `calendar_events.txt`, `calendar_attendees.txt`, `calendar_reminders.txt` |
| `apps` | `pm list packages -f -i -U --show-versioncode` y `pm list packages -u` (incluye las desinstaladas con datos) | `apps_pm_list_packages.txt`, `apps_pm_list_packages_u.txt` |
| `call_log` | `content query --uri content://app.factum.connector.calllog/calls` (proveedor del connector, §7.2) | `call_log_calls.txt` |

Sin `--projection`, para traer todas las columnas. Si stderr trae `SecurityException`, la URI queda `permission_denied`;
si trae `Unknown URI`/`Could not find provider`, queda `provider_unavailable`. La categoría queda `done` si al menos
una URI salió bien; si solo algunas, `done_with_errors`, con el detalle por URI en el ACTA. El archivo crudo se guarda
**igual** aunque haya fallado (vacío + stderr en `comandos.log`). Timeout por comando: 10 min, para un SMS de 100k
filas.

#### 5.3.2 Normalizado (D5: derivado)

`datos/normalizado/<categoria>.json`, con codificación UTF-8 y la forma siguiente:

```json
{ "schema": "factum-extraccion-normalizado/1", "derived_from": ["datos/crudo/sms.txt"], "source_uri": "content://sms",
  "columns": ["_id","thread_id","address","date","type","body"], "rows": [ {"_id":"1","address":"…","body":"…"} ],
  "row_count": 425, "parse_warnings": 0 }
```

- Hay un archivo por URI (`sms.json`, `mms.json`, `contacts_data.json`…). `apps.json` se arma del `pm list` con
  `package`, `apk_path`, `installer`, `uid`, `version_code` y `uninstalled_with_data`.
- Los valores quedan como **string** tal cual, y `NULL` pasa a `null`. No hay conversión de fechas. La interpretación
  queda para #101.
- Las filas que no parsean limpio llevan `"_raw": "<línea>"` y `"_parse_warning": true`.
- En el manifiesto, los normalizados van con `tipo = derivado`.
- La cantidad del resumen (`count`) es el `row_count` de la URI principal de cada categoría: `contacts/contacts`,
  `sms` + `mms`, `call_log/calls`, `calendar/events` y la cantidad de paquetes de `pm list packages -f`.

#### 5.3.3 Parser de `content query` (`ContentQueryParser`, puro y testeado)

El formato real es `Row: <n> col1=val1, col2=val2, …`. Los valores pueden tener `, ` y saltos de línea, así que el
parser es ambiguo por naturaleza y se resuelve así:

1. Se parte el texto en filas por `^Row: \d+ ` (multilínea): todo hasta el próximo `Row:` es la fila.
2. **Columnas**: se toma la secuencia de claves `(?:^|, )([A-Za-z_][A-Za-z0-9_]*)=` de cada fila y se elige la
   secuencia **más frecuente** (moda). `content` imprime siempre el mismo orden de columnas del cursor.
3. Cada fila se corta anclando **en ese orden**: el valor de `colᵢ` va desde `colᵢ=` hasta la **última** aparición de
   `, colᵢ₊₁=` que deje lugar para las siguientes. Si no se puede ubicar la secuencia completa, la fila queda con
   `_raw` + `_parse_warning`.
4. `No result found.` equivale a 0 filas.

Los tests usan fixtures sintéticos con comas, `=` y saltos de línea dentro de `body`, además de una salida real
anonimizada del M12 (§12.1).

### 5.4 Archivos de `/sdcard`

#### 5.4.1 Raíces por categoría (`AndroidStorageFolders.ForCategory`)

| `key` | Raíces |
|---|---|
| `media` | `/sdcard/DCIM`, `/sdcard/Pictures`, `/sdcard/Movies` |
| `downloads` | `/sdcard/Download` |
| `documents` | `/sdcard/Documents` |
| `audio` | `/sdcard/Music`, `/sdcard/Recordings`, `/sdcard/Call`, `/sdcard/Sounds`, `/sdcard/Podcasts`, `/sdcard/Audiobooks`, `/sdcard/Notifications`, `/sdcard/Ringtones`, `/sdcard/Alarms` |
| `whatsapp_media` | `KnownAppFolders["whatsapp"]` (las 3 rutas actuales) |
| `all_storage` | `/sdcard` |
| `custom` | `custom_paths` del pedido |

Las raíces que no existen se omiten con `path_not_found` en el detalle de la categoría. Eso no es un error de la
categoría: queda en `done` con 0 archivos si no existe ninguna.

#### 5.4.2 Deduplicación (`DevicePaths.Dedupe`, puro)

1. Se normaliza: `/storage/emulated/0` y `/storage/self/primary` pasan a `/sdcard`, se sacan las barras finales y los
   `//`.
2. Se rechaza lo que no esté bajo `/sdcard`, lo que tenga `..` o un byte de control. Eso da 400 `invalid_request`.
3. Se descarta toda raíz descendiente de otra elegida.
4. Cada raíz conserva la categoría "dueña". La primera en el orden de §4.3 gana: si `all_storage` está marcada, todo
   es `all_storage`.

La UI muestra el total **después** de deduplicar (§5.4.6).

#### 5.4.3 Inventario (antes de copiar)

Por raíz: `adb shell -T "find <raíz>/ -type f -exec stat -c '%s|%Y|%n' {} +"`, con la raíz pasada por `RemoteQuote`
y la barra final obligatoria por el symlink de `/sdcard` (comentario de `ListDirectoryAsync`). La salida va a
`registro/inventario_dispositivo.tsv` y da tamaño, mtime (epoch, que pasa a ISO UTC) y ruta. Las líneas que no
parsean (nombres con `\n`) se registran como `estado=error`, `motivo=nombre_no_soportado`. `find` no sigue symlinks
por defecto y se mantiene así. El inventario fija `bytes_total` y `files_total`.

#### 5.4.4 Copia

- Por raíz: `adb -s S pull -a <raíz>/ <carpeta>/archivos/<ruta-local-de-la-raíz>`. `-a` conserva mtime y modo.
- **Progreso**: cada `ProgressIntervalMs` (1 s), se suma el tamaño de lo que hay en la carpeta destino de esa raíz
  contra su total del inventario. No depende de la salida de adb, que sin tty no informa porcentaje.
- **Conciliación**: al terminar la raíz, cada entrada del inventario se busca en local con el mismo tamaño. Si falta,
  o no coincide, o el nombre no es válido en el sistema de archivos destino, se reintenta **por archivo** con
  `adb pull -a <archivo> <ruta-local-saneada>`. Si vuelve a fallar, queda `estado=error` con el stderr resumido. Esto
  cubre los dos comportamientos de adb ante un archivo ilegible (aborta o sigue) y los nombres inválidos en Windows o
  exFAT.
- **Mapeo de rutas** (`DevicePaths.ToLocal`, puro): `/sdcard/DCIM/a.jpg` pasa a `archivos/sdcard/DCIM/a.jpg`. Se
  sanea solo lo necesario:
  - los caracteres `<>:"|?*` y de control pasan a `_`;
  - los nombres reservados de Windows (`CON`, `NUL`, `COM1`…) llevan `_` adelante;
  - los puntos y espacios finales llevan `_` agregado;
  - una colisión por mayúsculas/minúsculas (destino case-insensitive, que se detecta escribiendo una sonda) suma
    `~2`;
  - un segmento de más de 255 bytes UTF-8 se corta y suma `~<hash8>`.

  Si hubo ajuste, el manifiesto marca `nombre_ajustado=si` y conserva siempre `ruta_dispositivo` original.
- **Rutas largas en Windows**: las rutas locales se arman absolutas y, si superan 240 caracteres, se usa el prefijo
  `\\?\` para las operaciones de .NET. El `adb pull` por archivo recibe la ruta corta del directorio padre más el
  nombre, así que conviene crear el destino cerca de la raíz del disco. La UI lo sugiere.
- **FAT32**: si `DriveInfo.DriveFormat` es `FAT`/`FAT32`/`vfat`, el chequeo de destino devuelve `max_file_bytes =
  4294967295` y la UI avisa si el inventario o la estimación tienen archivos más grandes. Durante la copia, ese
  archivo queda `error` con `motivo=destino_fat32_limite_4gib`.
- **Destino lleno o removido**: una `IOException` con `DiskErrors.IsDiskFull`, o el directorio destino desaparecido,
  da `interruption.reason_code = destination_error`.
- **Desconexión**: si adb sale con `no devices`/`device '…' not found`/`device offline`, o `OnDevicesPolled` deja de
  ver el serial, se cancela la fase y `get-state` decide entre `device_unauthorized` y `device_disconnected`.

#### 5.4.5 Hash en el dispositivo (D10)

- Disponibilidad (preflight): `adb shell -T "command -v sha256sum || toybox sha256sum --help"`. Se guarda
  `device_sha256_available` y la ruta del binario.
- Por raíz: `adb shell -T "find <raíz>/ -type f -exec sha256sum {} +"`. La salida cruda se guarda en
  `registro/hashes_dispositivo.txt` y se parsea con el formato `^([0-9a-f]{64})  (.+)$`.
- Comparación contra el SHA-256 de la PC, que queda en la columna `coincide`:
  - `si`;
  - `no`: el archivo cambió en el dispositivo entre la copia y el hash, o la copia es infiel. Se cuenta en
    `hash_mismatches` y **no** es error;
  - `no_calculado`: no estaba en la salida.
- `verify_on_device` es opción del pedido, **activada por defecto** (D10), con la casilla "Verificar también el hash
  en el celular (más lento)". Si `sha256sum` no existe, la casilla aparece deshabilitada con el texto "Este celular no
  permite calcular el hash".

#### 5.4.6 Estimación previa

`POST /devices/{serial}/extraction/estimate` corre `du -s -k` sobre las raíces deduplicadas (`adb shell -T du -s -k
'<r1>' '<r2>' …`) y devuelve los bytes por categoría y el total. Es rápido y alcanza para la estimación. El total real
sale del inventario. Los datos estructurados suman una constante de 50 MiB a la estimación.

### 5.5 Registro de llamadas (connector)

1. **Verificación en Tatana**:
   1. Se ubica el APK (§7.6).
   2. Se calcula su SHA-256 y se compara con `connector-lock.json` (embebido en el binario de Tatana, §7.5). Si no
      coincide o no existe, la categoría queda `not_extracted / connector_unavailable` y **no se instala nada**.
2. **Conflicto**: si `pm path app.factum.connector` devuelve algo **antes** de instalar, el paquete ya estaba. Se
   compara la firma con `dumpsys package app.factum.connector | grep -i signatures` contra `cert_sha256` del lock;
   si no se puede comprobar, queda `connector_conflict` y no se toca nada. Si es una instalación de Factum huérfana,
   de una extracción anterior cortada, se trata como pendiente de desinstalar y se desinstala antes, registrándolo en
   el acta.
3. **Instalación**: `adb -s S install -r -g <apk>` (`-g` concede `READ_CALL_LOG`) con timeout de 120 s, que deja
   lugar al prompt de Play Protect.
   - Antes de la llamada se anota `connector_installed` (hora PC, versión, sha256) y `connector_pending_uninstall` en
     el diario. Una instalación a medias también se intenta desinstalar.
   - Los fallos se clasifican así:
     - `INSTALL_FAILED_USER_RESTRICTED`, `INSTALL_FAILED_VERIFICATION_FAILURE` o el timeout dan
       `connector_install_blocked`;
     - `INSTALL_FAILED_UPDATE_INCOMPATIBLE` da `connector_conflict`;
     - los demás dan `connector_install_failed`.
4. **Exportación**: el `content query` de §5.3.1 sobre el proveedor del connector, y `content query --uri
   content://app.factum.connector.calllog/info` para la versión que corre (la anota el acta).
5. **Desinstalación**: `adb -s S uninstall app.factum.connector`, en `finally`. Si sale bien, se anota
   `connector_uninstalled` con la hora y se limpia `connector_pending_uninstall`. Si el celular no está, queda
   pendiente.
6. **Reconexión** (`OnDevicesPolled`): cuando aparece un serial con `connector_pending_uninstall` en algún diario, se
   desinstala.
   - Si el diario es de una extracción **ya cerrada**, se escribe una **adenda** en la carpeta:
     `ADENDA_ACTA_<yyyyMMdd-HHmmss>.txt` con su `.sha256`, fuera del manifiesto (que ya está cerrado). Además se
     agrega la modificación con `after_close: true` al `record` y se pone `registered = false` para que la web
     re-registre (§8.3, el backend acepta el append).
   - Se emite `extraction_cleanup`.
   - Si la extracción **todavía no cerró** (recuperación al arrancar), la desinstalación entra en el ACTA parcial
     normal.

### 5.6 Pantalla encendida

Depende de la Duda U3. Con la recomendada (B: solo durante la fase del connector), justo antes de instalar se llama a
`StayAwakeKeeper.BeginAsync(null, serial)` y después a `ReassertAsync`. Si el keeper escribió (`state.Owned`), se anota
`stay_awake_set` con el valor original. Al terminar la fase se llama a `RestoreAsync(state, connected)` y se anota
`stay_awake_restored`. El estado se persiste en el diario para restaurarlo al reconectar, con el mismo mecanismo que
el connector. El keeper ya respeta `Agent:Recording:StayAwake=false`.

### 5.7 Concurrencia y conflictos

- Una sola extracción por Tatana. Un `POST /extractions` con otra en curso da 409 `extraction_in_progress`, con el
  `id` de la vigente.
- Si hay una grabación o sesión activa en **el mismo serial** (`IRecordingStateSource.ActiveRecordings` +
  `SessionRecorder`), da 409 `device_busy`. Al revés, mientras corre una extracción, `record/start` y la sesión sobre
  ese serial **no** se bloquean en esta HU: el wizard no se toca. Se documenta como riesgo en §11.3.
- El autoupdate queda bloqueado por `IOperationSource` mientras `status == running`.

### 5.8 Progreso (WebSocket y snapshot)

- `extraction_progress` se emite como mucho **1 vez por segundo** y además en cada cambio de fase o de estado de una
  categoría. Lleva el snapshot completo (§8.2.6, unos 2–4 KB).
- `extraction_finished` se emite una vez con el snapshot terminal (incluye `record`).
- `bytes_per_second` es una media móvil de 10 s sobre `bytes_done` de las fases `copying`/`hashing`. `eta_seconds` es
  `(bytes_total − bytes_done) / bytes_per_second`, y va null si no hay datos o la fase no es de archivos.
- Los errores por archivo **no** viajan en el snapshot. Van en `GET /extractions/{id}/errors?offset&limit`, paginado
  y leído del manifiesto parcial o final.

### 5.9 Cortes, cancelación y apagado

- **Cancelación** (`POST /extractions/{id}/cancel`):
  1. Se cancela el token y se matan los procesos de adb en vuelo (`ProcessStop`).
  2. Las categorías pendientes pasan a `cancelled`.
  3. Se corre el `finally` (connector y stay_awake).
  4. Se cierran el manifiesto y el ACTA con `status = cancelled`.

  Lo copiado **no** se borra.
- **Desconexión o autorización revocada**: lo mismo, pero con `status = interrupted` y el `reason_code`
  correspondiente. Se intenta la desinstalación: si el celular no está, queda pendiente.
- **Apagado de Tatana** (`ApplicationStopping`): presupuesto de 15 s.
  1. Cancela.
  2. Intenta desinstalar el connector, con un timeout de 5 s.
  3. Cierra manifiesto y ACTA como `interrupted / agent_stopped`.

  Si no llega, lo cierra `ExtractionRecovery` en el próximo arranque.
- **Recuperación**:
  1. Con el manifiesto parcial, se hashean los archivos presentes en local que no tengan hash todavía (están en el
     inventario, pero sin fila en el parcial).
  2. Después se cierra todo igual que en un corte.

  Así lo ya copiado queda hasheado (Gherkin "Desconexión o corte").
- **No se reanuda** (D10).

### 5.10 Destinos (D9)

- **`GET /extraction/destinations`**:
  - Nativo, sin `AllowedRoots`: `DriveInfo.GetDrives()` con `IsReady`, filtrando lo que no sirve.
    - En **Linux** se descartan los tipos `proc`, `sysfs`, `tmpfs`, `devtmpfs`, `overlay`, `squashfs`, `cgroup*`,
      `autofs`, `devpts`, `securityfs`, `pstore`, `bpf`, `tracefs`, `fusectl`, `configfs`, `debugfs`, `mqueue`,
      `hugetlbfs`, `ramfs`, `nsfs` y los puntos de montaje bajo `/snap`, `/boot`, `/sys`, `/proc`, `/dev` y `/run`
      (salvo `/run/media`).
    - En **macOS** se ofrecen `/` como disco interno y `/Volumes/*`.
    - En **Windows** se ofrecen las letras `Fixed`, `Removable` y `Network`.
  - Con `AllowedRoots` (Docker): solo esas raíces y, debajo de `/media/<x>/<y>` y `/run/media/<x>/<y>`, cada montaje
    como unidad. Se detecta con `DriveInfo` cuyo `RootDirectory` cuelga de una raíz permitida.
  - Cada unidad informa `kind` (`internal` | `removable` | `network`), `label`, `root_path`, `free_bytes`,
    `total_bytes`, `file_system` y `max_file_bytes`.
  - `default_path` es `<EvidenceDirectory>/Extracciones`. Se crea al elegirlo, y es la única carpeta que Tatana crea
    fuera de la de la extracción.
- **`GET /extraction/destinations/browse?path=`**:
  - Lista **solo subcarpetas**, sin archivos. No sigue symlinks ni junctions (`ReparsePoint` se omite) y oculta las
    carpetas que empiezan con `.` y las de sistema (`$RECYCLE.BIN`, `System Volume Information`).
  - Con `AllowedRoots`, una ruta fuera de ellas (después de `Path.GetFullPath` y de resolver los symlinks del camino)
    da 403 `path_not_allowed`.
- **`POST /extraction/destinations/check`**:
  1. `Path.GetFullPath`, que tiene que existir y ser un directorio no-symlink.
  2. Prueba de escritura: crear y borrar `.factum-write-test-<guid>`.
  3. Espacio libre contra `required_bytes + margen`, con margen = `max(Agent:MinFreeBytes (1 GiB), 5 %)`.
  4. Devuelve la carpeta final que se crearía.

  El mismo chequeo se repite al iniciar: TOCTOU, si falla da 422 o 507.

### 5.11 Preflight (`GET /devices/{serial}/extraction/preflight`)

Devuelve:

- plataforma y SDK (`getprop ro.build.version.sdk`);
- batería: `dumpsys battery`, con `level` y `AC powered`/`USB powered`;
- disponibilidad de `sha256sum`;
- `connector` (`available`, `version_name`, `sha256` y `reason` si no está disponible);
- la lista de categorías de archivos con sus raíces existentes (`ls -d`);
- `screen_state` vía `IAndroidScreenProbe`, solo informativo: la UI recomienda la pantalla desbloqueada.

iPhone (`platform = ios`) da 422 `platform_not_supported`. La UI igual lo deshabilita antes (§10).

### 5.12 Modo mock

Con `Agent:Mock=true`:

- preflight con batería 64 %, `sha256sum` disponible y connector disponible (con hash ficticio);
- estimate con valores fijos;
- la extracción genera en el destino real elegido unos pocos archivos de texto chicos por categoría, pasa por todas
  las fases con demoras de 1–3 s y produce manifiesto, ACTA y CIERRE reales.

`Agent:MockExtractionOutcome` (solo por env/CLI/Local) admite `completed` (default), `completed_with_errors`,
`interrupted`, `connector_blocked` y `cancelled_slow` (la copia dura 60 s, para probar cancelar). Los destinos en mock
son reales: el perito puede probar el navegador.

---

## 6. Modelo de datos (API, Mongo)

Mongo **sin** `ConventionPack`, así que los nombres en la base son PascalCase. Todas las clases llevan
`[BsonIgnoreExtraElements]`, los opcionales `[BsonIgnoreIfNull]` y en el JSON `[JsonIgnore(WhenWritingNull)]` donde
aplique. No hay documentos previos: las colecciones son nuevas, salvo `case_timestamps` y `case_events`, a las que solo
se les agregan campos opcionales.

### 6.1 Colección `extractions` (`Models/Extraction.cs`)

```csharp
[BsonIgnoreExtraElements]
public sealed class Extraction
{
    [BsonId] public string Id { get; set; } = "";          // GUID "D" en minúscula, lo genera Tatana
    public int SchemaVersion { get; set; } = 1;
    public User Officer { get; set; } = new();              // dueño (del JWT), como Case.Officer
    public string Status { get; set; } = ExtractionStatuses.Running;
    public string Hostname { get; set; } = "";
    public string OsUser { get; set; } = "";
    public string AgentVersion { get; set; } = "";
    [BsonIgnoreIfNull] public AgentMode? AgentMode { get; set; }
    public DateTime StartedAtPc { get; set; }               // UTC, reloj de la PC (Tatana)
    [BsonIgnoreIfNull] public DateTime? FinishedAtPc { get; set; }
    public DateTime CreatedAt { get; set; }                 // UTC servidor, primer PUT
    public DateTime UpdatedAt { get; set; }                 // UTC servidor, último PUT
    [BsonIgnoreIfNull] public DateTime? FinalRegisteredAt { get; set; } // UTC servidor, primer PUT terminal
    [BsonIgnoreIfNull] public long? PcMinusServerMs { get; set; }       // sondeo NTP de la web (DeviceDetailsRules.ComputePcServer)
    [BsonIgnoreIfNull] public long? PcServerMarginMs { get; set; }
    public ExtractionDevice Device { get; set; } = new();
    [BsonIgnoreIfNull] public DeviceDetails? DeviceDetails { get; set; } // el MISMO modelo de #79 (Fields + ClockStart/ClockClose)
    public List<ExtractionCategory> Categories { get; set; } = [];
    public int CustomPathsCount { get; set; }               // nunca los nombres de carpetas
    public bool VerifyOnDevice { get; set; }
    public bool DeviceSha256Available { get; set; }
    public ExtractionTotals Totals { get; set; } = new();
    public string DestinationPath { get; set; } = "";       // texto informativo (D6)
    public string FolderName { get; set; } = "";
    [BsonIgnoreIfNull] public string? ManifestSha256 { get; set; }      // 64 hex minúscula; inmutable una vez puesto
    [BsonIgnoreIfNull] public int? ManifestEntries { get; set; }
    public List<DeviceModification> DeviceModifications { get; set; } = []; // append-only
    [BsonIgnoreIfNull] public ExtractionInterruption? Interruption { get; set; }
    [BsonIgnoreIfNull] public string? CaseId { get; set; }  // 0 o 1 caso (D6a)
    [BsonIgnoreIfNull] public DateTime? LinkedAt { get; set; }
}

public sealed class ExtractionDevice { string Serial, Manufacturer, Model, Platform = "android", AndroidVersion; int? SdkInt; }
public sealed class ExtractionCategory { string Key, Kind, Status; long? Count; int? Files; long? Bytes; int? Errors;
    int? HashMismatches; string? ReasonCode; string Method = ""; }   // Method: texto corto ("content query content://sms, content://mms, content://mms/part")
public sealed class ExtractionTotals { int Files; long Bytes; int Errors; int HashMismatches; }
public sealed class DeviceModification { string Kind; DateTime At; string? Detail; bool AfterClose; }
public sealed class ExtractionInterruption { string ReasonCode; DateTime At; }
public static class ExtractionStatuses { Running="running", Completed="completed", CompletedWithErrors="completed_with_errors", Interrupted="interrupted", Cancelled="cancelled" }
public static class DeviceModificationKinds { ConnectorInstalled="connector_installed", ConnectorUninstalled="connector_uninstalled", StayAwakeSet="stay_awake_set", StayAwakeRestored="stay_awake_restored" }
```

**Índices** (`ExtractionRepository.EnsureIndexesAsync`, en segundo plano con `CancellationToken.None`, mismo patrón
que `CaseEventRepository`):

- `ix_extractions_officer_started`: `{ "Officer.Dni": 1, StartedAtPc: -1 }`;
- `ix_extractions_case`: `{ CaseId: 1 }`, parcial `{ CaseId: { $exists: true } }`.

**Repositorio** (`IExtractionRepository`):

- `FindByIdAsync`;
- `ListByOfficerAsync(dni, caseId?, unlinkedOnly, limit 200)`;
- `UpsertAsync(Extraction)`: `ReplaceOne` con filtro `{_id, "Officer.Dni"}` + `IsUpsert`. Ante un duplicado (otro
  dueño), el servicio responde 404;
- `SetCaseAsync(id, dni, caseId | null, linkedAt)` con `$set`/`$unset` filtrado por dueño.

Nunca borra.

### 6.2 `case_events`: tipos y detalle nuevos (`Models/CaseEvent.cs`)

- `CaseEventTypes.ExtractionLinked = "extraction_linked"` y `CaseEventTypes.ExtractionUnlinked = "extraction_unlinked"`.
- `CaseEventDetail` suma `[BsonIgnoreIfNull, JsonIgnore(WhenWritingNull)] public string? ExtractionId` y
  `public string? DeviceLabel` (por ejemplo `"Samsung SM-M127F"`, sin serial).
- Reusa `Sha256` (hash del manifiesto, si hay) y `Reason`, que no se usa.

Actor: el usuario autenticado. `CaseService.CaseEventFor` usa el dueño del caso, y acá coinciden porque solo el dueño
asocia. IP del request. `Timestamp` es la hora del servidor.

### 6.3 Sello del manifiesto (`case_timestamps`)

- `CaseTimestamp` suma `[BsonIgnoreIfNull] public string? ExtractionId`, y `TimestampTargets` suma
  `ExtractionManifest = "extraction_manifest"`.
- El documento lleva `CaseId = ""`, `Target = "extraction_manifest"`, `Filename = "MANIFIESTO.tsv"` y `Sha256 =
  manifest_sha256`. El índice único `ux_case_timestamps_object` (CaseId, Target, Filename, Sha256) se mantiene: el hash
  del manifiesto es distinto por extracción, porque el ACTA con el id está dentro.
- Índice nuevo `ix_case_timestamps_extraction`: `{ ExtractionId: 1 }`, parcial `{ ExtractionId: { $exists: true } }`.
- `ICaseTimestampRepository.FindByExtractionAsync(extractionId, includeTsr)`.
- `ITimestampService.SealExtractionManifestAsync(Extraction ext, TimeSpan budget)`: es `SealNowAsync` sin caso, con
  `NewPending("", ExtractionManifest, "MANIFIESTO.tsv", sha)` + `ExtractionId`, y `AttemptAsync(inserted, cas: null,
  …)`, así que no hay evento de caso. Nunca lanza.
- `ProcessClaimedAsync`: si `claimed.CaseId == ""`, no busca el caso, porque es de una extracción. Los logs dicen
  "Extracción {Id}".
- La extracción **no** guarda copia del sello: `ExtractionDto.manifest_timestamp` se arma al leer, con
  `FindByExtractionAsync` → `TimestampDto.From`. Así la cola de reintentos no necesita actualizar `extractions`.

---

## 7. App auxiliar `android/connector/` (D3=A)

### 7.1 Proyecto

```
android/connector/
  settings.gradle.kts · build.gradle.kts · gradle.properties · gradle/libs.versions.toml
  gradle/wrapper/gradle-wrapper.properties  (con distributionSha256Sum fijado) · gradlew · gradlew.bat · gradle-wrapper.jar
  app/build.gradle.kts
  app/src/main/AndroidManifest.xml
  app/src/main/kotlin/app/factum/connector/CallLogExportProvider.kt
  app/proguard-rules.pro (vacío; minify desactivado)
  connector-lock.json
  LEEME.md  (qué hace, cómo se compila, cómo se firma, cómo se actualiza el lock)
```

- `namespace`/`applicationId` es `app.factum.connector`, que queda fijo después del primer release (D-T5).
- `minSdk = 23`, `targetSdk = 35` y `compileSdk = 35`. AGP y Kotlin en las últimas estables al implementar, fijadas
  en `libs.versions.toml`. JDK 17.
- **Sin dependencias** (ni AndroidX ni Kotlin coroutines): solo el SDK de Android y el stdlib de Kotlin. Superficie
  mínima y auditable. `isMinifyEnabled = false`, para que el bytecode se pueda revisar.
- `versionName` y `versionCode` vienen de propiedades de Gradle (`-PconnectorVersionName=1.0.0
  -PconnectorVersionCode=1`) que pasa el CI desde el tag. Los defaults de desarrollo son `0.0.0-dev`/`1`.
- `signingConfigs.release` lee de variables de entorno (`CONNECTOR_KEYSTORE`, `CONNECTOR_KEYSTORE_PASSWORD`,
  `CONNECTOR_KEY_ALIAS`, `CONNECTOR_KEY_PASSWORD`). Sin ellas, `assembleRelease` sale sin firmar, como en los PR.

### 7.2 Manifiesto y proveedor

```xml
<manifest xmlns:android="http://schemas.android.com/apk/res/android" xmlns:tools="http://schemas.android.com/tools">
  <uses-permission android:name="android.permission.READ_CALL_LOG" />
  <!-- Nada más. Sin INTERNET: la app no puede sacar datos del dispositivo por red (D3c). -->
  <application android:label="Factum Connector" android:allowBackup="false" android:hasFragileUserData="false"
               android:icon="@android:drawable/sym_def_app_icon" tools:ignore="MissingApplicationIcon">
    <!-- Sin activity launcher: no aparece en el cajón de apps. -->
    <provider android:name=".CallLogExportProvider"
              android:authorities="app.factum.connector.calllog"
              android:exported="true" android:grantUriPermissions="false" />
  </application>
</manifest>
```

`CallLogExportProvider : ContentProvider`:

- `onCreate()` devuelve `true` y no hace nada más.
- `query(uri, projection, selection, args, sort)`:
  1. **Controla quién llama**: `Binder.getCallingUid()` tiene que ser `Process.SHELL_UID` (2000) o `0`. Cualquier otro
     uid recibe `SecurityException("Factum connector: solo adb")`. Así ninguna app del celular puede usar el
     proveedor, aunque esté exportado.
  2. Para el path `calls`: `context.contentResolver.query(CallLog.Calls.CONTENT_URI, projection, null, null,
     "${CallLog.Calls._ID} ASC")`. **Ignora** `selection`/`args`/`sort` (exportación completa y determinística) y
     devuelve el cursor tal cual.
  3. Para el path `info`: un `MatrixCursor` de una fila con `package`, `version_name`, `version_code`, `sdk_int` y
     `read_call_log_granted` (`checkSelfPermission`).
  4. Cualquier otro path da `null`.
- `insert`/`update`/`delete` lanzan `UnsupportedOperationException` (solo lectura). `getType` devuelve `null`.

Exporta por adb sin archivos temporales: `adb shell content query --uri content://app.factum.connector.calllog/calls`
(§5.3.1) produce el mismo formato que el resto y pasa por el mismo parser. No queda nada escrito en el dispositivo
fuera de la instalación.

**A verificar en el equipo (checklist B0):**

- el proveedor de un paquete recién instalado y nunca abierto (`stopped=true`) responde al `content query` del shell;
- `install -g` concede `READ_CALL_LOG` en Android 11 (M12);
- el uid que ve el proveedor es 2000.

Si el proveedor no arranca en estado `stopped`, el plan B documentado es exportar con `am instrument -w -r
app.factum.connector/.ExportInstrumentation` y volcar las filas como `INSTRUMENTATION_STATUS: row=…`. En ese caso, el
implementador lo reporta como `blocked` antes de seguir, para que el arquitecto ajuste §5.3.

### 7.3 CI: `.github/workflows/android-connector.yml` (nuevo)

- **`pull_request`** con `paths: android/connector/**` y la workflow:
  1. `actions/setup-java` (17, temurin), `android-actions/setup-android` o `sdkmanager` con versiones fijas, y
     `gradle/actions/setup-gradle` con validación del wrapper (`gradle/actions/wrapper-validation`).
  2. `./gradlew :app:lintRelease :app:assembleRelease` (sin firmar).
  3. **Chequeo de permisos**: `apkanalyzer manifest permissions` (o `aapt2 dump permissions`) tiene que listar
     **exactamente** `android.permission.READ_CALL_LOG`. Si aparece `INTERNET` o cualquier otro, falla.
  4. Chequeo de que no hay activity launcher.
- **`push` de tag `connector-vX.Y.Z`** (solo si el commit está en `main`, mismo patrón P3 que `tatana-release.yml`) y
  `workflow_dispatch` en modo ensayo:
  - En el `environment: android-connector` hay 4 secrets: `ANDROID_CONNECTOR_KEYSTORE_BASE64`,
    `ANDROID_CONNECTOR_KEYSTORE_PASSWORD`, `ANDROID_CONNECTOR_KEY_ALIAS` y `ANDROID_CONNECTOR_KEY_PASSWORD`.
  - Hay una **variable** pública del repo `ANDROID_CONNECTOR_CERT_SHA256`.
  - Pasos:
    1. Decodificar el keystore a `$RUNNER_TEMP` y borrarlo en un `if: always()`.
    2. `assembleRelease` con `-PconnectorVersionName/Code`.
    3. `apksigner verify --print-certs`: el SHA-256 del certificado tiene que ser igual a `ANDROID_CONNECTOR_CERT_SHA256`.
       Esto protege contra un cambio de clave accidental.
    4. Chequeo de permisos.
    5. `sha256sum`.
    6. **GitHub Release** `connector-vX.Y.Z` con `factum-connector-X.Y.Z.apk`, `factum-connector-X.Y.Z.apk.sha256` y
       `build-info.txt` (commit, versiones de AGP/Kotlin/JDK/build-tools y SHA-256 del certificado).
    7. El job summary imprime el JSON listo para pegar en `connector-lock.json`.
  - Los valores de secrets e inputs llegan por `env:`, nunca interpolados en `run:`. `permissions: contents: write`
    solo en ese job.

El lock no lo actualiza el workflow: lo commitea una persona en un PR. Así el cambio de hash que verifica Tatana pasa
por revisión.

### 7.4 Clave de firma

La genera y la custodia el dueño del producto (D3a), fuera del repo y con copia offline (ver Duda U1, que incluye los
comandos `keytool`). Si se pierde, hay que cambiar el paquete o desinstalar en cada celular, y cambiar
`cert_sha256`/`sha256` del lock.

### 7.5 `connector-lock.json` y verificación en Tatana

```json
{ "package": "app.factum.connector", "version_name": "1.0.0", "version_code": 1,
  "sha256": "<64 hex del APK>", "cert_sha256": "<64 hex del certificado>",
  "release_tag": "connector-v1.0.0", "asset": "factum-connector-1.0.0.apk" }
```

- Embebido en `Factum.Agent` con `<EmbeddedResource Include="../../../android/connector/connector-lock.json"
  LogicalName="Factum.Agent.connector-lock.json" />`. Es **la única** fuente del hash fijo: Tatana lo lee al arrancar
  (`ConnectorLock.Load()`).
- Mientras no exista el primer release, el lock lleva `"sha256": ""`. Tatana informa entonces `connector.available =
  false`, `reason = "not_released"`, y la UI deshabilita "Registro de llamadas" con el texto "Esta versión de Tatana
  todavía no trae la app auxiliar".
- `Agent:Extraction:AllowUnpinnedConnector=true` se respeta **solo** si `ASPNETCORE_ENVIRONMENT=Development`. Permite
  un APK de desarrollo con otro hash y deja en el ACTA "app auxiliar NO verificada (modo desarrollo)". En cualquier
  otro entorno se ignora con un warning.

### 7.6 Empaquetado (nativo y Docker)

- `ops/connector/bajar-connector.sh <dir-destino>` lee `android/connector/connector-lock.json`.
  - Si `sha256` está vacío, sale con 0 sin hacer nada: el paquete sale sin connector.
  - Si no, baja el asset con `gh release download <release_tag> -p <asset>` (o `curl` con
    `Authorization: Bearer $GH_TOKEN` contra la API de releases, porque el repo es privado), verifica `sha256` (si no
    coincide, falla) y lo deja en `<dir-destino>/tools/connector/factum-connector.apk` + `connector-lock.json`.
- `deploy/linux/armar-tatana-linux.sh` lo llama después de armar `tools/`. Eso cubre el AppImage, el tar del agente y
  Docker (que reusa ese payload con `COPY agente/ /opt/tatana/`). `deploy/windows/armar-tatana-portable.sh` lo llama
  igual, y eso cubre el portátil y el instalador NSIS.
- `tatana-release.yml`, `tatana-linux.yml` y `tatana-windows.yml`: `env: GH_TOKEN: ${{ github.token }}` en los pasos
  que arman, y `android/connector/connector-lock.json` + `ops/connector/**` en sus `paths`.
- Ubicación en runtime (`ConnectorLocator`), en este orden:
  1. `Agent:Extraction:ConnectorApkPath` (configurable; siempre se verifica el hash);
  2. `<AppContext.BaseDirectory>/tools/connector/factum-connector.apk`.

  Si no está, `connector_unavailable / not_packaged`. Docker: `/opt/tatana/tools/connector/…`, que viene en la imagen.
- **macOS**: no hay paquete. En desarrollo se usa `ConnectorApkPath` con el APK bajado a mano por
  `bajar-connector.sh`.

---

## 8. Contrato compartido

**Casing:** todo JSON en `snake_case_lower`. Tatana: `AddJsonOptions` con `SnakeCaseLower`, enums con
`JsonStringEnumConverter(SnakeCaseLower)` y `WhenWritingNull`, así que **un `null` no viaja** (en TS: campo opcional).
API: `SnakeCaseLower`; en los DTOs de esta HU los null **sí** viajan como `null`, salvo los marcados `WhenWritingNull`.
Fechas: ISO-8601 UTC con `Z`.

Archivos de cada lado:

| Contrato | Lado servidor | Lado web |
|---|---|---|
| Web ↔ Tatana | `server/src/Factum.Agent/Models/ExtractionModels.cs` (records), `Controllers/Extraction*.cs` | `client/src/lib/agent.ts` (tipos `AgentExtraction*` + métodos en `agent`) |
| Web ↔ API | `server/src/Factum.Backend/DTOs/ExtractionDtos.cs`, `Models/Extraction.cs`, `Models/CaseEvent.cs` | `client/src/lib/api.ts` (tipos `Extraction*`, `CaseEventType`, `CaseEventDetail`) |
| Etiquetas y lógica pura compartida en la web | — | `client/src/lib/extraction.ts` |

### 8.1 Capability

`/health.capabilities` suma `"extraction_android_v1"`. Sin ella, la sección muestra "Actualizá Tatana para hacer
extracciones" y no deja iniciar.

### 8.2 Web ↔ Tatana (`http://localhost:8765`)

#### 8.2.1 `GET /devices/{serial}/extraction/preflight` → 200

```json
{
  "serial": "R58RA3R5AGA", "platform": "android", "sdk_int": 30, "android_version": "11",
  "manufacturer": "samsung", "model": "SM-M127F",
  "battery_level": 64, "battery_charging": true,
  "device_sha256_available": true,
  "screen_state": "on",
  "connector": { "available": true, "version_name": "1.0.0", "sha256": "…64hex…" },
  "file_categories": [
    { "key": "media", "roots": ["/sdcard/DCIM", "/sdcard/Pictures"], "available": true },
    { "key": "documents", "roots": [], "available": false }
  ]
}
```

- `battery_level` y `battery_charging` se omiten si no se pudieron leer.
- `screen_state` es `on` | `off` | `locked` | `unknown`.
- Si `connector.available` es `false`, viene `connector.reason`: `not_packaged` | `not_released` | `hash_mismatch` |
  `sdk_too_old`.
- Errores: 404 `device_not_found`, 422 `platform_not_supported`.

#### 8.2.2 `POST /devices/{serial}/extraction/estimate`

Body: `{ "file_categories": ["media","whatsapp_media"], "custom_paths": ["/sdcard/Telegram"] }`. Respuesta 200:

```json
{ "roots": [ { "path": "/sdcard/DCIM", "category": "media", "bytes": 19922944 } ],
  "by_category": { "media": 19922944, "whatsapp_media": 0, "custom": 5242880 },
  "files_bytes": 25165824, "structured_bytes": 52428800, "total_bytes": 77594624 }
```

Errores: 400 `invalid_request` (ruta fuera de `/sdcard` o categoría desconocida), 404 `device_not_found`.

El árbol de "Elegir carpetas" reusa `GET /devices/{serial}/explorer?path=` (sin cambios).

#### 8.2.3 Destinos

- `GET /extraction/destinations` → 200:

  ```json
  { "restricted": false, "can_reveal": true, "default_path": "/home/perito/Factum/Evidencia/Extracciones",
    "drives": [ { "kind": "internal", "label": "Disco del sistema", "root_path": "/", "free_bytes": 120000000000,
                  "total_bytes": 250000000000, "file_system": "ext4" },
                { "kind": "removable", "label": "EVIDENCIA_01", "root_path": "/media/perito/EVIDENCIA_01",
                  "free_bytes": 900000000000, "total_bytes": 1000000000000, "file_system": "exfat" } ] }
  ```

  `max_file_bytes` aparece solo con FAT32 (`4294967295`).
- `GET /extraction/destinations/browse?path=<abs>` → 200:
  `{ "path": "<abs>", "parent": "<abs>|null", "entries": [ { "name": "Casos", "path": "<abs>/Casos" } ] }`. Errores:
  403 `path_not_allowed`, 404 `path_not_found`.
- `POST /extraction/destinations/check`, con body `{ "path": "<abs>", "required_bytes": 77594624 }` → 200:

  ```json
  { "path": "<abs>", "ok": false, "writable": true, "free_bytes": 50000000, "required_bytes": 77594624,
    "margin_bytes": 1073741824, "missing_bytes": 1101336448, "file_system": "exfat",
    "final_folder_preview": "<abs>/Factum_Extraccion_samsung-SM-M127F_20261025-143000",
    "reason": "insufficient_space" }
  ```

  `reason` viene solo si `ok` es `false`: `insufficient_space` | `not_writable` | `path_not_found` |
  `path_not_allowed`. `max_file_bytes` aparece solo con FAT32.

#### 8.2.4 `POST /extractions` (inicio) → 202 con el snapshot (§8.2.6)

```json
{
  "serial": "R58RA3R5AGA",
  "structured_categories": ["contacts","sms_mms","call_log","calendar","apps"],
  "file_categories": ["media"],
  "custom_paths": [],
  "destination_path": "/media/perito/EVIDENCIA_01",
  "verify_on_device": true,
  "confirm_device_modifications": true,
  "operator": { "dni": "30111222", "name": "Ana Pérez" },
  "pc_server_probe": { "pc_before": "…Z", "server_time": "…Z", "pc_after": "…Z" },
  "origin_id": "tab-…"
}
```

Validación:

- al menos una categoría en total;
- claves conocidas;
- `custom_paths` ≤ 200, cada uno válido según §5.4.2;
- `destination_path` absoluto, existente y escribible, con las reglas de `AllowedRoots`;
- `confirm_device_modifications` tiene que ser `true` si `call_log` está elegido, o si la Duda U3 termina en A;
- `operator.dni` y `operator.name` no vacíos, ≤ 100 caracteres. Son informativos para el ACTA: el registro con
  autoridad es el del backend.

`pc_server_probe` y `origin_id` son opcionales.

Errores (`{ "error", "code" }`, más campos extra):

- 400 `invalid_request`, 400 `confirmation_required`;
- 404 `device_not_found`, 409 `device_unauthorized`;
- 409 `extraction_in_progress` (+ `extraction_id`), 409 `device_busy`;
- 403 `path_not_allowed`, 422 `destination_not_writable`;
- 507 `insufficient_storage` (+ `missing_bytes`);
- 422 `platform_not_supported`.

#### 8.2.5 Resto de las rutas de extracción

| Ruta | Respuesta |
|---|---|
| `GET /extractions/current` | 200 `{ "extraction": <snapshot> }` o `{ }` (sin en curso: `extraction` no viaja) |
| `GET /extractions` | 200 `{ "extractions": [ { "id", "status", "started_at", "finished_at", "folder_path", "manufacturer", "model", "registered", "seals_written" } ] }`: el índice local, más recientes primero, últimas 100 |
| `GET /extractions/{id}` | 200 snapshot · 404 `extraction_not_found` |
| `GET /extractions/{id}/errors?offset=0&limit=100` | 200 `{ "total": 3, "items": [ { "device_path", "category", "reason" } ] }` |
| `POST /extractions/{id}/cancel` | 202 snapshot · 409 `extraction_not_running` |
| `POST /extractions/{id}/registered` con body `{ "manifest_sha256": "<64hex>" }` | 200 `{ "registered": true }` · 409 `manifest_mismatch` si no coincide con el de Tatana · 409 `extraction_not_finished` |
| `PUT /extractions/{id}/seals` con body `{ "files": [ { "name": "MANIFIESTO.tsv.tsr", "content_base64": "…" } ], "readme": "…CRLF…" }` | 200 `{ "directory": "<carpeta>/sellos", "written": 1, "unchanged": 0 }`. Mismas validaciones que `WriteSealsRequest` (`invalid_request`) |
| `POST /extractions/{id}/reveal` | 204 · 409 `reveal_unavailable` si es `restricted` (Docker) |

#### 8.2.6 Snapshot de extracción (respuestas y eventos WS)

```json
{
  "id": "3f2a9c1e-…", "status": "running", "phase": "copying",
  "serial": "R58RA3R5AGA", "manufacturer": "samsung", "model": "SM-M127F", "android_version": "11",
  "operator": { "dni": "30111222", "name": "Ana Pérez" },
  "destination_path": "/media/perito/EVIDENCIA_01",
  "folder_name": "Factum_Extraccion_samsung-SM-M127F_20261025-143000",
  "folder_path": "/media/perito/EVIDENCIA_01/Factum_Extraccion_samsung-SM-M127F_20261025-143000",
  "started_at": "2026-10-25T17:30:00.123Z", "updated_at": "…Z",
  "verify_on_device": true, "device_sha256_available": true,
  "categories": [
    { "key": "contacts", "kind": "structured", "status": "done", "count": 22 },
    { "key": "call_log", "kind": "structured", "status": "not_extracted", "reason_code": "connector_install_blocked",
      "reason": "INSTALL_FAILED_USER_RESTRICTED" },
    { "key": "media", "kind": "files", "status": "running", "files_total": 812, "files_done": 300,
      "bytes_total": 19922944, "bytes_done": 7340032, "errors": 0, "hash_mismatches": 0 }
  ],
  "totals": { "files_total": 812, "files_done": 300, "bytes_total": 19922944, "bytes_done": 7340032,
              "errors": 0, "hash_mismatches": 0, "bytes_per_second": 2097152, "eta_seconds": 6 },
  "device_modifications": [ { "kind": "connector_installed", "at": "…Z", "detail": "app.factum.connector 1.0.0 sha256 …" } ],
  "registered": false, "seals_written": false
}
```

- `phase` es `preparing` | `structured` | `inventory` | `copying` | `hashing` | `verifying_device` | `closing` |
  `done`.
- En `categories[]`, los campos opcionales se omiten si no aplican: `count` (estructuradas); `files_total`,
  `files_done`, `bytes_total`, `bytes_done`, `errors` y `hash_mismatches` (archivos); `reason_code` y `reason` (si no
  se extrajo).
- `device_modifications[]` lleva `after_close: true` solo si aplica.
- Campos que solo aparecen en el **terminal**:
  - `finished_at`;
  - `interruption: { "reason_code", "message", "at" }` (solo `interrupted`);
  - `manifest_sha256`, `manifest_entries`;
  - `record` (§8.3).

**Eventos WS** (`AgentEvent { type, timestamp, data }`):

- `extraction_progress` → `data` = snapshot;
- `extraction_finished` → `data` = snapshot terminal;
- `extraction_cleanup` → `data` = `{ "extraction_id", "serial", "kind": "connector_uninstalled", "at" }`.

### 8.3 Web → API: registro (`record`)

Tatana arma `record` y la web lo reenvía **sin tocarlo** a `PUT /api/extractions/{id}`, agregando solo
`pc_server_probe` si lo tiene. Así se evita el teléfono descompuesto entre Tatana y la API: `record` usa exactamente
los nombres del DTO de la API.

`PUT /api/extractions/{id}` (`UpsertExtractionRequest`, todo anulable como en `CaseDtos`; las reglas viven en
`ExtractionRules`):

```json
{
  "status": "completed_with_errors",
  "hostname": "PC-PERITO-01", "os_user": "perito", "agent_version": "1.9.0", "agent_mode": "installed",
  "started_at_pc": "…Z", "finished_at_pc": "…Z",
  "device": { "serial": "R58RA3R5AGA", "manufacturer": "samsung", "model": "SM-M127F", "platform": "android",
              "android_version": "11", "sdk_int": 30 },
  "device_details": { "platform": "android", "agent_version": "1.9.0",
                      "fields": { "serial_number": { "value": "R58…" }, "wifi_ssid": { "reason": "masked" } },
                      "clock_start": { "pc_before": "…", "pc_after": "…", "device_time": "…", "resolution_ms": 1000 },
                      "clock_close": { "pc_before": "…", "pc_after": "…", "device_time": "…", "resolution_ms": 1000 } },
  "categories": [ { "key": "contacts", "kind": "structured", "status": "done", "count": 22,
                    "method": "content query content://com.android.contacts/{contacts,raw_contacts,data}" },
                  { "key": "media", "kind": "files", "status": "done_with_errors", "files": 812,
                    "bytes": 19922944, "errors": 1, "hash_mismatches": 0, "method": "adb pull -a; sha256sum (toybox)" } ],
  "custom_paths_count": 0,
  "verify_on_device": true, "device_sha256_available": true,
  "totals": { "files": 812, "bytes": 19922944, "errors": 1, "hash_mismatches": 0 },
  "destination_path": "/media/perito/EVIDENCIA_01",
  "folder_name": "Factum_Extraccion_samsung-SM-M127F_20261025-143000",
  "manifest_sha256": "…64hex…", "manifest_entries": 830,
  "device_modifications": [ { "kind": "connector_installed", "at": "…Z", "detail": "…", "after_close": false } ],
  "interruption": null,
  "pc_server_probe": { "pc_before": "…Z", "server_time": "…Z", "pc_after": "…Z" }
}
```

- `fields` del `device_details` usa las claves de `DeviceDetailKeys` y pasa por `DeviceDetailsRules.NormalizeFields`.
  Los relojes usan el `DeviceClockDto` existente y se recalculan con `DeviceDetailsRules.ComputeClock`.
- **Al iniciar**, la web manda el mismo `record` mínimo que Tatana deja en el snapshot `running` (`record` aparece
  también en `running` con `status: running` y sin `manifest_sha256`/`finished_at_pc`). Es best-effort: si falla, la
  web lo reintenta al final.
- **Reglas** (`ExtractionRules`, pura):
  - `id` es un GUID;
  - `status` está en el enum y la transición es válida (§4.2);
  - `manifest_sha256` es 64 hex y es **inmutable** una vez guardado: otro valor da 409 `extraction_immutable`, el
    mismo es idempotente;
  - `started_at_pc` y `device.serial` son inmutables;
  - las categorías van en la lista blanca de §4.3, sin duplicados;
  - números ≥ 0;
  - textos saneados con `SafeText`, con topes: `hostname`/`os_user` 100, `destination_path` 1024, `folder_name` 255,
    `method` 300 y `detail` 300;
  - `device_modifications` ≤ 20, `kind` en la lista blanca, **append-only**: se hace merge por `(kind, at)` y no se
    borra nada;
  - `status` terminal requiere `finished_at_pc` y `manifest_sha256`. `interrupted` requiere `interruption`.
- **Efectos**:
  - el primer PUT crea con `Officer` del JWT;
  - el primer PUT terminal fija `FinalRegisteredAt`;
  - si `manifest_sha256` es nuevo y el sellado está activo, se llama a `SealExtractionManifestAsync` con
    `GenerationBudget`.
- **Respuesta** 200: `ExtractionDto` (§8.4).
- **Errores**: 400 `invalid_extraction` (+ `error` legible), 404 (`id` de otro dueño, igual que "no existe"), 409
  `extraction_immutable`, 409 `extraction_finished` (pasar de terminal a otro estado).

### 8.4 API → web: `ExtractionDto`

```json
{
  "id": "…", "status": "completed", "officer": { "dni": "30111222", "name": "Ana Pérez" },
  "hostname": "…", "os_user": "…", "agent_version": "…", "agent_mode": "installed",
  "started_at_pc": "…Z", "finished_at_pc": "…Z", "created_at": "…Z", "updated_at": "…Z", "final_registered_at": "…Z",
  "pc_minus_server_ms": -120, "pc_server_margin_ms": 35,
  "device": { "serial", "manufacturer", "model", "platform", "android_version", "sdk_int" },
  "device_details": { /* misma forma que Case.device_details de #79 (read_at, platform, connection_id, agent_version, fields, clock_start, clock_close, include_ssid_in_report) */ },
  "categories": [ /* §8.3 */ ], "custom_paths_count": 0, "verify_on_device": true, "device_sha256_available": true,
  "totals": { "files", "bytes", "errors", "hash_mismatches" },
  "destination_path": "…", "folder_name": "…", "manifest_sha256": "…", "manifest_entries": 830,
  "device_modifications": [ … ], "interruption": null,
  "case_id": "…|null", "case_ref": "1234/2026|null", "linked_at": "…Z|null",
  "manifest_timestamp": { /* TimestampDto existente (sellado-tiempo §7.2) */ } 
}
```

`manifest_timestamp` es `null` si el sellado está desactivado o la extracción no tiene hash todavía. `case_ref` es el
`NroReferencia` del caso asociado, para mostrarlo sin un segundo fetch.

En TS, `device_details` reusa el tipo existente de `Case.device_details` y `manifest_timestamp` reusa `CaseTimestamp`.

### 8.5 Resto de la API

| Ruta | Respuesta |
|---|---|
| `GET /api/extractions?case_id=<id>&unlinked=true` | 200 `{ "extractions": [ExtractionDto] }`. Las del usuario, por `started_at_pc` desc, máximo 200. Con `case_id` (que tiene que ser un caso propio, si no 404) trae solo las de ese caso. Con `unlinked=true`, solo las que no tienen caso y están en un estado terminal |
| `GET /api/extractions/{id}` | 200 `ExtractionDto` · 404 |
| `PUT /api/extractions/{id}/case` con body `{ "case_id": "…" }` | 200 `ExtractionDto` · 404 (extracción o caso ajeno o inexistente) · 409 `extraction_not_finished` · 409 `extraction_already_linked` (ya tiene **otro** caso: hay que desasociar primero; el mismo caso es idempotente). Registra `extraction_linked` en el caso. **No** valida si el caso es editable: se puede asociar a borradores y generados (D6), y no toca informes |
| `DELETE /api/extractions/{id}/case` | 200 `ExtractionDto` · 404. Si tenía caso, registra `extraction_unlinked` en **ese** caso. Si no tenía, es idempotente y no registra evento |
| `GET /api/extractions/{id}/timestamp/files` | 200 `{ "files": [ { "name": "MANIFIESTO.tsv.tsr", "content_base64": "…" } ], "readme": "…" }` (reusa `TimestampFileDto`). `files` viene vacío si el sello no está `granted`. 404 |

**Evento de caso** (`GET /api/cases/{id}/events`, sin cambios de ruta):

```json
{ "type": "extraction_linked", "detail": { "extraction_id": "…", "sha256": "<manifest>", "device_label": "samsung SM-M127F" } }
```

`extraction_unlinked` lleva el mismo `detail`.

### 8.6 Errores de Tatana: códigos nuevos (`AgentErrorCodes` ↔ `AgentErrorCode` en `lib/agent.ts`)

`extraction_in_progress`, `extraction_not_found`, `extraction_not_running`, `extraction_not_finished`,
`manifest_mismatch`, `confirmation_required`, `device_busy`, `device_unauthorized`, `platform_not_supported`,
`path_not_allowed`, `path_not_found`, `destination_not_writable`, `reveal_unavailable`. Se reusan `invalid_request`,
`insufficient_storage` y `device_not_found`, que ya existen.

---

## 9. Carpeta de salida, MANIFIESTO, ACTA y LEEME (D4, D11)

### 9.1 Estructura

```
<destino>/Factum_Extraccion_<marca>-<modelo>_<yyyyMMdd-HHmmss>/
  LEEME.txt                    ← en el manifiesto (tipo=acta)
  ACTA.txt                     ← en el manifiesto (tipo=acta)
  ACTA.json                    ← en el manifiesto (tipo=acta)
  MANIFIESTO.tsv               ← autoridad de integridad
  MANIFIESTO.tsv.sha256        ← "<hash>  MANIFIESTO.tsv" (formato sha256sum)
  SHA256SUMS.txt               ← copia de conveniencia derivada del manifiesto (sha256sum -c)
  CIERRE.txt                   ← fuera del manifiesto: hash del manifiesto + hash del ACTA + hora (Duda U2)
  sellos/                      ← fuera del manifiesto (lo copia la web después): MANIFIESTO.tsv.tsr + LEEME-verificacion.txt
  ADENDA_ACTA_<fecha>.txt(+.sha256)   ← solo si hubo limpieza posterior al cierre (§5.5 paso 6)
  datos/crudo/*.txt            ← tipo=evidencia
  datos/normalizado/*.json     ← tipo=derivado
  archivos/sdcard/...          ← tipo=evidencia (ruta original del dispositivo)
  registro/comandos.log        ← tipo=registro
  registro/inventario_dispositivo.tsv · registro/hashes_dispositivo.txt  ← tipo=registro
  .factum/                     ← trabajo interno (manifiesto parcial); se borra al cerrar bien, se conserva si hubo error
```

### 9.2 `MANIFIESTO.tsv`

UTF-8 sin BOM, `\n`, una cabecera y una fila por archivo, ordenadas por `ruta_relativa` (ordinal). Los campos escapan
`\t`, `\n` y `\\` como `\t`, `\n` y `\\` literales.

| Columna | Contenido |
|---|---|
| `ruta_relativa` | Ruta dentro de la carpeta, con `/` |
| `ruta_dispositivo` | Ruta original en el celular. Vacía para `datos/`, `registro/` y el acta (en esos casos va la URI o el comando en `origen`) |
| `origen` | URI de `content query`, comando `pm list …`, `adb pull` o `factum` |
| `categoria` | `key` de §4.3, o `acta`/`registro` |
| `tipo` | `evidencia` \| `derivado` \| `registro` \| `acta` |
| `tamano_bytes` | Tamaño local |
| `mtime_dispositivo` | ISO-8601 UTC del inventario (solo archivos de `/sdcard`) |
| `sha256_pc` | 64 hex |
| `sha256_dispositivo` | 64 hex o vacío |
| `coincide` | `si` \| `no` \| `no_calculado` \| `no_aplica` |
| `nombre_ajustado` | `si` \| `no` |
| `estado` | `ok` \| `error` |
| `motivo` | Vacío o texto corto |

Los archivos con `estado=error` también van (sin hash local). El manifiesto se va escribiendo en
`.factum/manifiesto.parcial.tsv` (append + flush por fila) y al cerrar se ordena y se escribe `MANIFIESTO.tsv`.

### 9.3 `ACTA.txt` (UTF-8 con BOM, CRLF, para que se lea bien en el Bloc de notas) y `ACTA.json`

Secciones del `.txt`, con los mismos datos en `ACTA.json`, `schema: "factum-extraccion-acta/1"`, claves en
snake_case:

1. **Identificación**: id de la extracción, estado final, operador (DNI + nombre, informado por la web), PC (hostname,
   usuario del SO, SO), versión de Tatana y modo, versión de Factum web (si la web la pasó: se agrega
   `client_version` opcional al pedido de inicio).
2. **Tiempos**:
   - inicio y fin con el reloj de la PC (ISO + hora local con zona);
   - diferencia PC − servidor medida al iniciar, con su margen, y las horas de servidor **derivadas** (se aclara que
     son derivadas). Si no hubo sondeo, dice "No medida".
3. **Dispositivo**: las 20 claves de la ficha #79 (valor o motivo) y el reloj de inicio y de cierre (dispositivo − PC
   con margen).
4. **Destino**: ruta de la carpeta, sistema de archivos y espacio libre al inicio.
5. **Método y resultado por categoría**: comando o mecanismo exacto (de `comandos.log`), cantidad, archivos y bytes,
   errores, discordancias de hash y "No extraída: <motivo legible>".
6. **Verificación**: si se calculó el hash en el dispositivo (binario usado) o por qué no.
7. **Modificaciones al dispositivo**: lista con hora (instalación y desinstalación del connector con versión y
   SHA-256, pantalla encendida con el valor original y el restaurado), o **"Ninguna"**. Además, un texto fijo sobre
   los efectos colaterales inevitables de adb: la autorización de depuración de esta PC y las entradas de log del
   sistema (D7).
8. **Interrupción o cancelación**: hora y motivo, si aplica.
9. **Integridad**: "El SHA-256 de este acta figura en MANIFIESTO.tsv. El hash del manifiesto está en
   MANIFIESTO.tsv.sha256 y en CIERRE.txt". Ver la Duda U2.

### 9.4 `CIERRE.txt` (fuera del manifiesto)

```
Extracción <id> — cierre de integridad
Estado: Completa
Cerrada (PC): 2026-10-25 14:58:03 -03:00
SHA-256 de MANIFIESTO.tsv: <hash>
SHA-256 de ACTA.txt: <hash>   SHA-256 de ACTA.json: <hash>
Entradas del manifiesto: 830
Registro en Factum: lo hace la web; si la web no estaba abierta, queda pendiente hasta que se vuelva a abrir la sección Extracciones.
```

### 9.5 `LEEME.txt`

Explica en castellano llano:

- qué hay en cada carpeta;
- qué es evidencia, qué es derivado y qué es registro;
- cómo verificar sin Factum:
  - Linux/macOS: `sha256sum -c SHA256SUMS.txt` y `sha256sum MANIFIESTO.tsv` contra `MANIFIESTO.tsv.sha256` y
    `CIERRE.txt`;
  - Windows: `Get-FileHash -Algorithm SHA256 <archivo>` y un one-liner de PowerShell que recorre `SHA256SUMS.txt`;
- que `sellos/LEEME-verificacion.txt` explica `openssl ts -verify -data MANIFIESTO.tsv -in sellos/MANIFIESTO.tsv.tsr
  -CAfile <ca>`;
- la recomendación de guardar la carpeta en un medio cifrado (BitLocker/LUKS/FileVault, D4).

Lo genera Tatana (texto fijo con el id). El LEEME de `sellos/` lo genera la API (§10.4 del backend).

---

## 10. Web (`client/`)

> Leer `client/AGENTS.md` y `node_modules/next/dist/docs/` antes de crear las rutas: en Next.js 16 los `params` de
> las páginas dinámicas son una `Promise`. Para `[id]`, lo más simple es una página server mínima que pasa el `id` a
> un componente cliente, o `useParams()` en el componente cliente. Verificarlo en la doc local.

### 10.1 Rutas y navegación

- `app/extracciones/page.tsx` → `ExtractionsScreen` (server component mínimo + `metadata`, como `app/metricas/page.tsx`).
- `app/extracciones/nueva/page.tsx` → `NewExtractionWizard`.
- `app/extracciones/[id]/page.tsx` → `ExtractionDetailScreen`, que muestra el progreso si está en curso y el resultado
  si terminó.
- `AppNavbar`:
  - `SECTIONS` queda con `inspecciones` → `/dashboard`, **`extracciones` → `/extracciones`** (ícono `HardDriveDownload`
    de lucide) y `metricas` → `/metricas`;
  - el tipo de `sections.activeSection` suma `"extracciones"`;
  - mismo patrón de ícono por debajo de `md` y `aria-current`.
- Dashboard: botón secundario "Nueva extracción" junto al CTA de inspección, que lleva a `/extracciones/nueva`.

### 10.2 Lógica pura y hooks

- `lib/extraction.ts`:
  - etiquetas de categorías, estados, fases y motivos (§4), en castellano, con texto **y** ícono por estado (no solo
    color);
  - `formatEta`;
  - `isTerminal`;
  - `defaultSelection(preflight)` (D8: estructuradas marcadas, archivos sin marcar, `call_log` solo si
    `connector.available`);
  - `deviceModificationsPreview(selection, stayAwakePolicy)` → lista para el paso Confirmar, o "Ninguna: solo lectura";
  - `needsConfirmation`.
- `hooks/useExtractionLive.ts`:
  - se suscribe a `extraction_progress`/`extraction_finished` por el WebSocket existente (`agent.connectWS`,
    `useAgentConnection`);
  - poll de respaldo a `GET /extractions/current` o `GET /extractions/{id}` cada 5 s si el WS no está;
  - se reengancha al montar (Gherkin "al volver a abrir la sección").
- `hooks/useExtractionRegistration.ts` (D11):
  1. Al montar cualquier página de `/extracciones` (y al reconectar Tatana), `agent.listExtractions()`.
  2. Por cada entrada terminal con `registered = false`:
     1. `agent.getExtraction(id)` → `record`.
     2. `api.getServerTime()` para el sondeo NTP (mismo patrón que la ficha #79).
     3. `api.upsertExtraction(id, { ...record, pc_server_probe })`.
     4. Si la respuesta trae `manifest_timestamp.status == "granted"`, `api.getExtractionTimestampFiles(id)` →
        `agent.writeExtractionSeals(id, …)`.
     5. `agent.markExtractionRegistered(id, manifest_sha256)`.
  3. Por cada entrada con `registered = true` y `seals_written = false`, cuyo DTO del backend tenga el sello
     `granted`, copia los sellos.
  4. Los fallos se muestran en una banda discreta ("Hay N extracciones pendientes de registro en Factum") y se
     reintentan en el próximo montaje o reconexión.
  5. El registro **inicial** (`running`) se hace en el wizard justo después del `202`.

### 10.3 Listado (`/extracciones`)

- Encabezado "Extracciones" con el botón primario "Nueva extracción". Si falta la capability, el botón se deshabilita
  y aparece el aviso "Actualizá Tatana para hacer extracciones".
- **Tarjeta destacada "En curso"** si `GET /extractions/current` devuelve algo: dispositivo, barra global, fase y
  enlace "Ver progreso".
- Lista (de la API, `api.listExtractions()`):
  - columnas: fecha (inicio, hora local), dispositivo, estado (ícono + texto), tamaño (`totals.bytes`) y caso asociado
    (`case_ref` o "Sin caso");
  - las filas son enlaces a `/extracciones/{id}`;
  - estado vacío con una explicación breve.
- Si la API no responde, se muestra el índice local de Tatana (`GET /extractions`) con la marca "Pendiente de registro".

### 10.4 Asistente (`/extracciones/nueva`), 4 pasos (no reusa el wizard de inspección)

1. **Dispositivo**:
   - reusa `DeviceConnect` y `useDeviceConnection`. Los iPhone aparecen **deshabilitados** con "La extracción de
     iPhone todavía no está disponible";
   - al elegir el Android, `agent.getExtractionPreflight(serial)` + la ficha (`useDeviceDetails`, existente);
   - recordatorio "Mantené la pantalla desbloqueada";
   - batería con aviso por debajo de 20 % ("Conectá el cargador");
   - estados: sin dispositivo, sin autorizar, Tatana caído.
2. **Qué extraer**:
   - casillas de estructuradas y de archivos;
   - cada categoría de archivos muestra su tamaño estimado (`POST …/estimate` con debounce de 400 ms al cambiar la
     selección); las no disponibles (`available=false`) van deshabilitadas con "No existe en este celular";
   - "Elegir carpetas" abre un modal con el árbol de `/sdcard`. Reusa la navegación de `DeviceFileExplorer` (lista
     carpetas con `agent.listDeviceDir`) en modo **selección de carpetas** con casillas, sin traer archivos. Si hace
     falta, se extrae un `DeviceFolderTree` reutilizable;
   - total estimado siempre visible (barra fija abajo);
   - casilla "Verificar también el hash en el celular (más lento)", marcada por defecto y deshabilitada si
     `device_sha256_available = false`;
   - "Registro de llamadas" deshabilitado con el motivo si `connector.available = false`.
3. **Destino**:
   - lista de unidades (`GET /extraction/destinations`) como tarjetas: ícono por `kind`, etiqueta, libre/total con
     barra y sistema de archivos;
   - navegador de carpetas (`browse`) con migas de pan;
   - la ruta final (`final_folder_preview`) se muestra completa;
   - `check` en cada cambio. Si no alcanza, error en línea "Faltan 1,03 GB de espacio libre" y el botón Siguiente se
     deshabilita. Si no es escribible, "No se puede escribir en esta carpeta". FAT32 con archivos de más de 4 GB da un
     aviso;
   - en Docker (`restricted`): texto "Solo se muestran la carpeta de evidencia y los discos USB conectados. Si
     enchufaste un disco recién, tocá Actualizar". Botón "Actualizar";
   - recomendación fija: "Usá un disco cifrado si la evidencia sale de la oficina".
4. **Confirmar**:
   - resumen (dispositivo, categorías, total y destino);
   - bloque "Modificaciones al dispositivo": la lista de `deviceModificationsPreview` o **"Ninguna: solo lectura"**;
   - si hay modificaciones, casilla obligatoria "Entiendo que Factum va a instalar temporalmente su app auxiliar y la
     va a desinstalar al terminar";
   - "Iniciar extracción" → `agent.startExtraction(...)` → registro inicial en la API → `router.replace('/extracciones/{id}')`.

### 10.5 En curso y resultado (`/extracciones/[id]`)

- **En curso**:
  - barra global (`bytes_done/bytes_total` en las fases de archivos; en la fase estructurada, el avance por
    categorías);
  - fase actual;
  - fila por categoría con estado (ícono + texto), archivos y bytes, velocidad y ETA;
  - aviso fijo "Podés cerrar esta pestaña; la extracción sigue en Tatana. No desconectes el celular.";
  - botón "Cancelar" con diálogo de confirmación ("Lo copiado no se borra").
  - Animaciones con `motion-safe` y `prefers-reduced-motion`. La barra usa `role="progressbar"` con `aria-valuenow`. La
    fase se anuncia con `aria-live="polite"` como mucho cada 10 s (no en cada tick).
- **Resultado**:
  - estado final con ícono y texto;
  - resumen por categoría (contactos, mensajes, llamadas, eventos, apps, archivos) y tamaño total;
  - "No extraída" con el motivo legible;
  - hash del manifiesto con botón Copiar;
  - estado del sello (reusa la presentación de `TimestampPanel` o un chip con "Sellado el …"/"Pendiente"/"Sin
    sellado");
  - ubicación con "Abrir carpeta" (`POST /extractions/{id}/reveal`, oculto si `can_reveal = false`) y "Copiar ruta";
  - "Asociar a un caso" (o "Cambiar caso"/"Desasociar");
  - errores por archivo desplegables y paginados (`GET /extractions/{id}/errors`);
  - modificaciones al dispositivo con hora;
  - si viene de la API y Tatana no la tiene (otra PC), se muestra solo lo de la API, sin "Abrir carpeta".
- **Asociar a un caso** (`LinkCaseDialog`): lista de los casos del usuario (`api.listCases()`) con búsqueda por número
  de causa o carátula → `api.linkExtraction(id, caseId)`. Avisa "No modifica informes ya generados".

### 10.6 Detalle del caso

`CaseDetailContent` suma el bloque **"Extracciones asociadas"**:

- `api.listExtractions({ caseId })`;
- tarjetas con fecha, dispositivo, resumen (archivos, tamaño, estado), hash del manifiesto abreviado con Copiar y
  ubicación;
- enlace a `/extracciones/{id}`;
- acción "Asociar extracción", que abre la lista `unlinked=true` → `linkExtraction`.

En `lib/case-events.ts`:

- `eventTitle` da "Extracción asociada" / "Extracción desasociada";
- `eventIcon` usa `HardDriveDownload`, con tono neutro;
- detalle: dispositivo + hash abreviado.

`CaseEventType` y `CaseEventDetail` (`lib/api.ts`) suman los tipos y los campos `extraction_id` y `device_label`.

### 10.7 Estados de error (todos con texto, no solo color)

- Tatana no responde o es viejo;
- sin dispositivo, o autorización revocada (lo informa el snapshot con `interruption.reason_code`);
- sin espacio o destino no escribible (del `check` y del `start`);
- connector no instalable: la categoría muestra el motivo;
- desconexión a mitad: estado "Interrumpida" + motivo + "Iniciá una extracción nueva";
- otra extracción en curso (409): enlace a la vigente;
- celular ocupado grabando (409 `device_busy`).

---

## 11. Docker, compatibilidad, riesgos

### 11.1 Tatana Docker: discos USB (D9 sub-duda = sí)

Cambio en `armar_run` (`deploy/tatana-docker/paquete/comun.sh`), dentro de la rama **no mock**:

```bash
# Discos USB para la extracción (extraccion-logica-android §11.1): udisks2 monta en /media/<usuario>/<etiqueta>
# (Ubuntu/Debian) o /run/media/<usuario>/<etiqueta> (Fedora/RHEL). rslave: los discos que se enchufan DESPUÉS de
# arrancar el contenedor aparecen solos (propagación del host al contenedor, nunca al revés).
for raiz in /media /run/media; do
  [[ -d "$raiz" ]] && RUN+=(-v "$raiz:$raiz:rslave")
done
RUN+=(-e "Agent__Extraction__AllowedRoots__0=$EVIDENCIA" -e "Agent__Extraction__AllowedRoots__1=/media"
      -e "Agent__Extraction__AllowedRoots__2=/run/media")
```

- Mismas rutas dentro y fuera: la ruta que muestra la web sirve en el host.
- Los discos montados por udisks2 quedan con el uid del usuario de escritorio, que es `TATANA_UID`, así que se puede
  escribir.
- `rslave` requiere que el punto de origen esté en un montaje compartido. En distros con systemd `/` es `rshared`
  (Ubuntu 18.04/20.04 de la fiscalía incluidos).
- No hace falta clave nueva en `instalacion.env`.
- El actualizador ya rearma `docker run` con el `armar_run` del **kit nuevo** (D-T6 de #95), así que el montaje entra
  solo al actualizar. También con `instalar-tatana-docker.sh` de nuevo.
- En mock, el montaje no hace falta.
- `paquete/LEEME.txt` y `docs/tatana-docker.md` suman "Extraer a un disco USB".
- Prueba de CI: en `ci/tatana-docker/` (existe desde #103), una aserción sobre `docker inspect` (`Mounts[]` con
  `Propagation: rslave` para `/media` cuando existe) en el script de prueba que ya arma el contenedor. No se puede
  enchufar un USB en CI: la prueba real queda manual (§12.4).

### 11.2 Conflicto con PR #103 (`feat/fiscalia-operacion`, sin mergear)

En `origin/develop`, el `docker run` está **en línea** en `deploy/tatana-docker/paquete/instalar-tatana-docker.sh`
(alrededor de las líneas 160–180). En PR #103 (HEAD `5d3d9fd`, esta rama, sin cambios sin commitear en
`deploy/tatana-docker/`) se movió a la función `armar_run` de `paquete/comun.sh`, que reusan el instalador y el
actualizador, y además se agregaron `ci/tatana-docker/*`.

- Si `feat/extraccion-logica-android` sale de `develop` **antes** de mergear #103 y edita el `instalar-tatana-docker.sh`
  viejo, el rebase da conflicto, y peor: el actualizador de #103 no tendría el montaje.
- **Recomendación** (Duda U4): crear la rama recién después de mergear #103 a `develop`, o hacer todo menos el bloque
  Docker y sumar el cambio de `armar_run` después del merge (checklist B-D1 al final, con su propio commit).
- El resto de la HU no toca archivos de #103. Excepciones: `.github/workflows/verificar.yml` (que #103 también toca),
  solo si se agrega algo; esta SDD no lo pide.

### 11.3 Compatibilidad y riesgos

| Tema | Tratamiento |
|---|---|
| Tatana viejo | Capability `extraction_android_v1` (§8.1). |
| Backend viejo | Mismo despliegue: la web y la API salen juntas. Un 404 de `/api/extractions` muestra "El servidor de Factum no admite extracciones todavía". |
| Documentos existentes | Colección nueva. `case_events` y `case_timestamps` solo ganan campos opcionales con `BsonIgnoreIfNull`, y los documentos viejos deserializan igual. |
| Android 12–15 sin probar | §12.4 lista los equipos. Lo que falle en un proveedor queda `not_extracted` con motivo, sin abortar. |
| Xiaomi/MIUI ("Instalar vía USB") y Play Protect | `connector_install_blocked` con el texto "Activá 'Instalar vía USB' en Opciones de desarrollador o aceptá el aviso de Play Protect y reintentá". El resto sigue. |
| Archivo que cambia durante la copia | `coincide=no`, sin error. El ACTA lo explica. |
| `content query` ambiguo | El crudo es la evidencia. El normalizado marca `_parse_warning` (§5.3.3). |
| Grabación iniciada en el wizard durante una extracción sobre el mismo celular | No se bloquea en esta HU. Las dos usan adb y deberían convivir, con la copia más lenta. Si QA ve problemas, sumar el chequeo inverso en una HU chica. |
| Superficie de la API local | Los endpoints de navegación del disco solo listan carpetas, quedan detrás de `OriginPolicy`/`HostPolicy` y respetan `AllowedRoots` en Docker. |
| Windows: rutas largas y nombres inválidos | §5.4.4. |
| Rendimiento del hash en el dispositivo | Lee todo dos veces en el celular. Activado por defecto (D10) con casilla. Medirlo en el M12 (B0) y anotar la velocidad en el progress. |
| Tatana cerrado con el connector instalado | Diario + `OnDevicesPolled` + adenda (§5.5). |

---

## 12. Tests

### 12.1 `server/tests/Factum.Agent.Tests/Extraction/` (nuevos, xUnit, sin adb real)

- `ContentQueryParserTests`: fixtures en `Fixtures/Extraction/`:
  - `sms_sintetico.txt`, con `body` que tiene `, `, `=`, `\n` y `Row:` en el medio;
  - `contacts_data_m12_anon.txt`, real del M12 con los valores reemplazados;
  - `no_result.txt`;
  - una salida con `SecurityException` en stderr.

  Verifica columnas por moda, `NULL` → null, `_parse_warning` y `row_count`.
- `DevicePathsTests`: normalización de `/storage/emulated/0`, rechazo de `..` y de rutas fuera de `/sdcard`,
  deduplicación de descendientes y prioridad de categoría. `ToLocal`: caracteres inválidos, nombres reservados, punto
  final, colisión de mayúsculas, segmento de más de 255 bytes, `nombre_ajustado`.
- `InventoryParserTests` (`%s|%Y|%n`) y `DeviceHashParserTests` (`sha256sum`).
- `ManifestWriterTests`: escape TSV, orden, cabecera exacta, `MANIFIESTO.tsv.sha256` en formato sha256sum,
  `SHA256SUMS.txt` coherente con el manifiesto, filas de error sin hash. Todo en un directorio temporal propio, nunca
  en `EvidenceDirectory`.
- `ActaWriterTests`: "Ninguna" sin modificaciones, la lista con horas cuando las hay, la sección de interrupción,
  `ACTA.json` con `schema`, el texto fijo de efectos de adb.
- `ExtractionFolderNamesTests`: saneado y sufijos `_2`/`_3`.
- `ConnectorLockTests`: carga del recurso embebido; `sha256` vacío da `not_released`; hash distinto da
  `hash_mismatch`; `AllowUnpinnedConnector` ignorado fuera de Development.
- `ConnectorInstallerTests` (adb inyectado como `Func<args, Task<ProcessResult>>`, como `StayAwakeKeeper`): mapeo de
  `INSTALL_FAILED_*` a `reason_code`; desinstalación en `finally` ante excepción y ante cancelación;
  `connector_pending_uninstall` cuando no hay celular.
- `ExtractionServiceTests` (con `MockExtraction` + `TimeProvider` falso):
  - 409 con una en curso;
  - `ActiveOperations` informa `extraction_android` mientras corre;
  - cancelación → `cancelled` con manifiesto y ACTA;
  - simulación de desconexión → `interrupted / device_disconnected`;
  - `record` con los nombres del DTO (serializar con `SnakeCaseLower` y comparar con un JSON de referencia que
    también usa el test de la API, §12.2).
- `ExtractionRecoveryTests`: diario en `running` al arrancar → `interrupted / agent_stopped`, se hashea lo copiado y
  queda la desinstalación pendiente.
- `DestinationServiceTests`: filtro de tipos de sistema de archivos de Linux (tabla de entrada), `AllowedRoots`
  (rechazo fuera, symlink que sale de la raíz), margen de espacio con `IAgentDiskProbe` falso, prueba de escritura en
  un directorio temporal.
- `HealthController`: `extraction_android_v1` en `Capabilities` (test existente de capabilities, si hay; si no, uno
  nuevo).

### 12.2 `server/tests/Factum.Backend.Tests/Extractions/` (nuevos)

- `ExtractionRulesTests`: GUID, transiciones, inmutables (`manifest_sha256`, `started_at_pc`, `serial`), lista blanca
  de categorías y modificaciones, topes de texto, append-only de modificaciones, terminal sin hash → inválido.
- `ExtractionServiceTests` (repositorios en memoria, patrón `*TestDoubles`):
  - upsert crea con `Officer` del JWT;
  - otro dueño → NotFound;
  - sello pedido una sola vez al fijar el hash;
  - link y unlink registran `extraction_linked`/`extraction_unlinked` con `extraction_id`, `sha256` y `device_label`;
  - link a otro caso con uno ya asignado → `extraction_already_linked`;
  - link a un caso ajeno → NotFound;
  - link con `running` → `extraction_not_finished`;
  - `unlinked=true` filtra.
- `ExtractionContractTests`: deserializa el **mismo** JSON de referencia de `record` (archivo compartido en
  `server/tests/Fixtures/extraction-record.json`, que referencian los dos proyectos con
  `<None Include="../Fixtures/extraction-record.json" …>`) y verifica que todos los campos llegan. Así el contrato
  Tatana → API no se desincroniza.
- `MongoExtractionRepositoryTests` (`[MongoFact]`, base `factum_test_<guid>` que se borra): índices, upsert filtrado
  por dueño, `SetCaseAsync`.
- `TimestampService`: `SealExtractionManifestAsync` crea el pending con `CaseId=""` y `ExtractionId`, no registra
  `case_events`, y `ProcessClaimedAsync` con `CaseId=""` no busca el caso. Va en `Timestamping/`, junto a los
  existentes.
- `TimestampReadme`: el LEEME de la extracción tiene el comando `openssl ts -verify -data MANIFIESTO.tsv`.

### 12.3 `client/`

No hay infraestructura de tests. Las funciones de `lib/extraction.ts` quedan puras para un test futuro. La
verificación es `tsc` + la prueba manual de §14.

### 12.4 Pruebas con equipos reales (QA manual, ver memoria: la corre un agente de QA con el Samsung)

| Equipo | Qué probar |
|---|---|
| Samsung Galaxy M12, Android 11 (el del spike) | Todas las categorías; connector (instalación, exportación, desinstalación); hash en el dispositivo y su velocidad; desconexión a mitad y reconexión con desinstalación pendiente; cancelación. |
| Algún Android 12–13 | Proveedores y connector; `pm list -U --show-versioncode`. |
| Algún Android 14–15 | Ídem + Play Protect al instalar por USB. |
| Xiaomi/Redmi (MIUI/HyperOS) | "Instalar vía USB" desactivado → `connector_install_blocked`, y el resto sigue. |
| Disco USB exFAT y FAT32 en Linux nativo y en Tatana Docker (Ubuntu 20.04) | Lista de unidades, enchufado después de arrancar el contenedor, aviso de 4 GiB. |
| Windows con destino en un pendrive NTFS | Nombres inválidos y rutas largas (carpeta de WhatsApp profunda). |

Los equipos que no estén disponibles quedan listados en el progress como pendientes, con la nota "sin verificar". La
HU no se bloquea por eso.

---

## 13. Checklist atómico

### 13.1 `implementer-backend`

Regla de datos (`AGENTS.md`):

- no tocar casos, eventos ni sellos existentes en la Mongo de desarrollo;
- las pruebas que escriben usan bases `factum_test_<guid>` o limpian solo sus `_id`;
- las extracciones de prueba se escriben en un directorio temporal propio, **nunca** en `Agent:EvidenceDirectory`
  real ni en `Storage`.

**B0. Verificación previa en el Samsung (solo lectura salvo el connector de prueba)**

- [ ] `adb shell -T content query --uri content://sms --projection _id` funciona y stdout sale sin `\r`.
- [ ] `adb shell -T "command -v sha256sum"` y `toybox sha256sum` en el M12.
- [ ] `adb pull -a` de una carpeta con un archivo sin permiso de lectura: anotar si aborta o sigue.
- [ ] Connector **debug** firmado con la clave de debug + `AllowUnpinnedConnector` en Development:
  - `install -g`;
  - `content query --uri content://app.factum.connector.calllog/calls` sin abrir la app;
  - uid 2000 visto por el proveedor;
  - `uninstall`.

  Si el proveedor no responde en estado `stopped`, **parar y devolver `blocked`** (plan B en §7.2).
- [ ] Anotar los resultados en `progress/impl_backend_extraccion-logica-android.md`.

**Tatana: base**

- [ ] `Common/AdbShell.cs`: `RemoteQuote` público (y `AdbService` lo usa).
- [ ] `Services/Android/AndroidStorageFolders.cs`: mover `KnownAppFolders` y sumar `ForCategory` (§5.4.1).
  `FindByAppAsync` sin cambio de comportamiento.
- [ ] `ProcessRunner.RunToFileAsync(exe, IReadOnlyList<string> args, string outputPath, TimeSpan? timeout,
  CancellationToken ct)` → `(int ExitCode, string StderrTail)`, con `ArgumentList` y matando el proceso al cancelar o
  al vencer el timeout.
- [ ] `Common/FileHashing.Sha256Async(path, ct, Action<long>? onBytes)`.
- [ ] `Models/ExtractionModels.cs`: records de §8.2 (pedidos, snapshot, `record`, destinos, preflight, estimate).
- [ ] `AgentOptions.Extraction` (`ExtractionOptions`: `AllowedRoots`, `VerifyOnDeviceDefault=true`, `StayAwakePolicy`
  según U3, `ProgressIntervalMs=1000`, `ConnectorApkPath`, `AllowUnpinnedConnector`) + `MockExtractionOutcome`.
  `Normalize()` con warnings, como `RecordingOptions`.
- [ ] `AgentErrorCodes`: códigos de §8.6.

**Tatana: servicio**

- [ ] `DevicePaths` (puro) + tests.
- [ ] `ContentQueryParser` (puro) + fixtures + tests.
- [ ] `ExtractionFolderNames` (puro) + tests.
- [ ] `ExtractionJournal` (temp + rename) + `ExtractionRecovery` + tests.
- [ ] `ExtractionManifestWriter` + tests.
- [ ] `ExtractionActaWriter` (ACTA.txt con BOM y CRLF, ACTA.json, CIERRE.txt, LEEME.txt) + tests.
- [ ] `StructuredExtractor` (comandos de §5.3.1, crudo y normalizado, `comandos.log`).
- [ ] `ConnectorLock` (recurso embebido) + `ConnectorLocator` + `ConnectorInstaller` + tests.
- [ ] `FileExtractor` (inventario, pull por raíz, progreso por sondeo del destino, conciliación y reintento por
  archivo, hash en la PC, hash en el dispositivo).
- [ ] Integración de pantalla encendida según la Duda U3 (§5.6).
- [ ] `ExtractionService` (fases de §5.2, `IOperationSource`, eventos WS limitados a 1/s, `OnDevicesPolled`,
  cancelación, `finally` de limpieza, adenda, `record`).
- [ ] `DestinationService` + tests.
- [ ] `MockExtraction` (§5.12).
- [ ] Controllers: `ExtractionController` (`/extractions*`, `/devices/{serial}/extraction/*`) y
  `ExtractionDestinationsController` (`/extraction/destinations*`), con errores `AgentHttpException` →
  `{ error, code }`.
- [ ] `HealthController.Capabilities` suma `extraction_android_v1` y el comentario.
- [ ] `Program.cs`:
  - DI (singleton + `IOperationSource`);
  - `ExtractionRecovery` al arrancar;
  - `extractions.OnDevicesPolled(serials)` en el lazo de polling;
  - cierre en `ApplicationStopping` con un presupuesto de 15 s.
- [ ] `Factum.Agent.csproj`: `EmbeddedResource` de `android/connector/connector-lock.json`.

**Android y CI**

- [ ] `android/connector/` (§7.1, §7.2) con el lock en `"sha256": ""`.
- [ ] `./gradlew :app:assembleRelease` local o en CI sin firmar. Permisos = solo `READ_CALL_LOG`.
- [ ] `.github/workflows/android-connector.yml` (§7.3). Sin secrets en los PR.
- [ ] `ops/connector/bajar-connector.sh` (§7.6) + llamada en `armar-tatana-linux.sh` y `armar-tatana-portable.sh`.
  Con el lock vacío no hace nada.
- [ ] `GH_TOKEN` y `paths` en `tatana-release.yml`, `tatana-linux.yml` y `tatana-windows.yml`.
- [ ] `android/connector/LEEME.md` con el procedimiento de release y de actualización del lock.

**API**

- [ ] `Models/Extraction.cs` (§6.1).
- [ ] `Infrastructure/ExtractionRepository.cs` (índices de §6.1).
- [ ] `DTOs/ExtractionDtos.cs` (§8.3–8.5).
- [ ] `Services/Extractions/ExtractionRules.cs` (puro) + tests.
- [ ] `Services/Extractions/ExtractionService.cs` (upsert, list, get, link, unlink, timestamp files) + tests.
- [ ] `Controllers/ExtractionsController.cs` (`[Authorize]`, `Officer` de `HttpContext.Items["User"]`, errores
  `{ error, code }` con `ResultHttpExtensions`).
- [ ] `CaseEvent`: tipos y `ExtractionId`/`DeviceLabel`.
- [ ] `CaseTimestamp.ExtractionId`, `TimestampTargets.ExtractionManifest`, índice y `FindByExtractionAsync`.
- [ ] `TimestampService.SealExtractionManifestAsync` y `ProcessClaimedAsync` sin caso, + tests.
- [ ] `TimestampReadme` para la extracción (`openssl ts -verify -data MANIFIESTO.tsv …`) + test.
- [ ] `Program.cs`: DI.
- [ ] Fixture compartido `server/tests/Fixtures/extraction-record.json` + `ExtractionContractTests` (API) y la
  aserción del lado de Tatana.

**Docker y docs**

- [ ] **B-D1** (después del merge de #103, Duda U4): `armar_run` en `comun.sh` (§11.1), `LEEME.txt` y la aserción en
  `ci/tatana-docker/`.
- [ ] `docs/tatana-docker.md`, `README.md` (sección Extracción: qué hace, qué modifica, formato de salida) y
  `AGENTE_TATANA.md` (caso de uso).

**Cierre**

- [ ] `graphify update .` si existe el grafo.
- [ ] `progress/impl_backend_extraccion-logica-android.md`: archivos, resultados de B0, verificación y lo pendiente
  (equipos sin probar, primer release del connector).

### 13.2 `implementer-frontend` (solo `client/`)

Skills obligatorias: `ui-ux-pro-max` antes del JSX final, `senior-frontend`, `3d-web-experience` (como criterio, sin
3D) y `web-design-guidelines` como autochequeo. Suma `ui-styling` y `mblode-agent-skills-ui-animation` para la barra
de progreso y las transiciones de paso. Se trabaja contra Tatana en **mock** (`Agent:Mock=true`, §5.12) mientras el
backend no esté.

- [ ] Leer `client/AGENTS.md` y la doc local de Next 16 sobre rutas dinámicas y `params`.
- [ ] `lib/agent.ts`:
  - tipos de §8.2 (`AgentExtractionPreflight`, `AgentExtractionEstimate`, `AgentDestinations`, `AgentDestinationCheck`,
    `AgentExtractionStartRequest`, `AgentExtractionSnapshot`, `AgentExtractionRecord`, `AgentExtractionListItem`);
  - códigos de §8.6 en `AgentErrorCode`;
  - métodos: `getExtractionPreflight`, `estimateExtraction`, `getDestinations`, `browseDestination`,
    `checkDestination`, `startExtraction`, `getCurrentExtraction`, `listExtractions`, `getExtraction`,
    `getExtractionErrors`, `cancelExtraction`, `markExtractionRegistered`, `writeExtractionSeals` y
    `revealExtraction`;
  - la capability `extraction_android_v1` en el comentario de capabilities.
- [ ] `lib/api.ts`:
  - tipos `Extraction`, `ExtractionCategory`, `UpsertExtractionRequest` (= `AgentExtractionRecord` +
    `pc_server_probe`);
  - `CaseEventType` y `CaseEventDetail` (`extraction_id`, `device_label`);
  - métodos: `listExtractions({ caseId?, unlinked? })`, `getExtraction`, `upsertExtraction`, `linkExtraction`,
    `unlinkExtraction` y `getExtractionTimestampFiles`.
- [ ] `lib/extraction.ts` (puro, §10.2).
- [ ] `hooks/useExtractionLive.ts` y `hooks/useExtractionRegistration.ts`.
- [ ] `AppNavbar`: sección `extracciones` y su tipo.
- [ ] Dashboard: acceso "Nueva extracción".
- [ ] `app/extracciones/page.tsx` + `components/extraction/ExtractionsScreen.tsx` (§10.3).
- [ ] `app/extracciones/nueva/page.tsx` + `NewExtractionWizard` con `DeviceStep`, `SelectionStep` (+
  `DeviceFolderPicker`), `DestinationStep` (`DriveList` + `FolderBrowser`) y `ConfirmStep` (§10.4).
- [ ] `app/extracciones/[id]/page.tsx` + `ExtractionDetailScreen` (`ExtractionProgress`, `ExtractionResult`,
  `CategoryRow`, `FileErrorsList`, `LinkCaseDialog`) (§10.5).
- [ ] `CaseDetailContent`: bloque "Extracciones asociadas" + "Asociar extracción" (§10.6).
- [ ] `lib/case-events.ts`: título, ícono y detalle de los 2 eventos.
- [ ] Accesibilidad:
  - estados con ícono y texto;
  - `role="progressbar"`;
  - `aria-live` limitado;
  - foco al cambiar de paso;
  - diálogos con `dialog-a11y`;
  - objetivos táctiles de 44 px o más;
  - `prefers-reduced-motion`.
- [ ] `npx tsc --noEmit` limpio.
- [ ] `progress/impl_frontend_extraccion-logica-android.md`, con la constancia de las skills.

---

## 14. Verificación

**`implementer-backend`, antes de `done`:**

```bash
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj
dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj   # FACTUM_TEST_MONGO opcional para los [MongoFact]
(cd android/connector && ./gradlew :app:lintRelease :app:assembleRelease)    # si hay JDK/SDK local; si no, el workflow del PR
bash -n deploy/tatana-docker/paquete/comun.sh ops/connector/bajar-connector.sh
shellcheck deploy/tatana-docker/paquete/comun.sh ops/connector/bajar-connector.sh   # si está instalado
```

Más una corrida de humo con Tatana en mock: `POST /extractions` hacia un directorio temporal tiene que dejar la
carpeta con `MANIFIESTO.tsv`, `.sha256`, `ACTA.*`, `CIERRE.txt` y `LEEME.txt`. Además, `sha256sum -c SHA256SUMS.txt`
tiene que pasar.

**`implementer-frontend`, antes de `done`:** `cd client && npx tsc --noEmit`, y recorrer el flujo con Tatana en mock en
los 5 resultados de `MockExtractionOutcome`.

**Orquestador:** `./ops/harness/verify.sh` antes de `aprobada`.

**Prueba manual que queda para el usuario (o el agente de QA con el Samsung):**

1. `/extracciones` → "Nueva extracción", con el M12 conectado. Si hay un iPhone conectado, tiene que aparecer
   deshabilitado.
2. Elegir todas las estructuradas, "Fotos y videos" y una carpeta puntual por el árbol. El total estimado tiene que
   coincidir a grandes rasgos con `du`.
3. Destino: un pendrive. Verificar el espacio libre. Elegir uno chico para ver el error "Faltan …".
4. Confirmar: ver la lista de modificaciones (connector). Desmarcar "Registro de llamadas" → tiene que decir "Ninguna:
   solo lectura".
5. Iniciar, cerrar la pestaña, volver a `/extracciones`: tiene que reengancharse.
6. Al terminar: resumen, hash, "Abrir carpeta", sello si el sellado está activo, y `sha256sum -c SHA256SUMS.txt` en una
   terminal.
7. Verificar en el celular que `app.factum.connector` no quedó instalada.
8. Desconectar a mitad en una segunda extracción → "Interrumpida" con el ACTA parcial.
9. Asociar a un caso desde la extracción y desde el detalle del caso. La trazabilidad tiene que mostrar el evento, y
   el informe ya generado no tiene que cambiar.
10. Tatana Docker (Ubuntu 20.04): enchufar un disco USB **después** de arrancar el contenedor, "Actualizar" en el paso
    Destino → tiene que aparecer y tiene que poder escribirse.

---

## 15. Decisiones técnicas

- **D-T1.** Tatana genera el `id` y el backend lo usa como `_id` (upsert). Así la extracción existe aunque el backend no
  responda al inicio o al cierre (D11).
- **D-T2.** `record` lo arma Tatana con los nombres exactos del DTO de la API, y la web lo reenvía sin tocarlo. Un
  fixture JSON compartido lo prueba en los dos proyectos .NET (§12.2).
- **D-T3.** El registro de llamadas sale por un `ContentProvider` exportado del connector que solo responde a uid 2000
  o 0, y se lee con el mismo `content query`. No hay archivos temporales en el dispositivo y el formato es el mismo
  que el resto. Plan B: `am instrument` (§7.2), que se valida en B0.
- **D-T4.** El hash fijo del connector vive **solo** en `android/connector/connector-lock.json`, embebido en Tatana.
  El APK no se versiona en el repo: sale de un GitHub Release firmado en CI y los scripts de empaquetado lo bajan y
  verifican. Con el lock vacío, la categoría queda deshabilitada (`not_released`).
- **D-T5.** Paquete `app.factum.connector`. Queda fijo después del primer release.
- **D-T6.** Sello de la extracción en `case_timestamps` con `CaseId = ""` + `ExtractionId`, y la cola existente sin
  cambios estructurales. `manifest_timestamp` se calcula al leer.
- **D-T7.** Copia por raíz con `adb pull -a`, progreso por sondeo del destino, y conciliación con reintento por archivo
  (robusto ante archivos ilegibles y nombres inválidos en Windows o exFAT).
- **D-T8.** Hash en el dispositivo después de copiar, por raíz, con `find … -exec sha256sum {} +`. Activado por
  defecto, con casilla (D10).
- **D-T9.** Manifiesto TSV en castellano + `SHA256SUMS.txt` derivado, para verificar con `sha256sum -c` sin Factum.
- **D-T10.** Normalizado = JSON con valores como string tal cual, sin interpretar fechas ni tipos. La interpretación es
  de #101.
- **D-T11.** En el backend no se guardan los nombres de las carpetas elegidas: solo `custom_paths_count` (HU, "Datos
  que se registran").
- **D-T12.** Extracciones visibles solo para su dueño, como `GET /api/cases` (D6c).
- **D-T13.** Una sola extracción por Tatana. 409 si el mismo serial está grabando. El bloqueo inverso queda fuera.
- **D-T14.** MMS: se extraen `mms` y los metadatos de `mms/part`. El binario de los adjuntos (`content read`) queda
  fuera de esta HU: se anota en el ACTA como limitación del método y se propone para #101 o una HU chica.
- **D-T15.** En Docker, `/media` y `/run/media` se montan con `rslave` + `AllowedRoots`, sin clave nueva en
  `instalacion.env`. Entra por el actualizador con el kit nuevo.
- **D-T16.** El Android lo hace `implementer-backend`: es parte del lado Tatana y no de `client/` ni `agent-ui/`.

---

## 16. Dudas para el usuario

**U1. Clave de firma del connector y primer release.** Sin esto, el registro de llamadas queda deshabilitado
("todavía no trae la app auxiliar"). El resto de la HU funciona igual.

- A) **Recomendada.** El dueño del producto genera la clave una vez:

  ```
  keytool -genkeypair -v -keystore factum-connector.jks -alias factum-connector -keyalg RSA -keysize 4096 -validity 36500
  ```

  - La guarda fuera del repo con una copia offline.
  - Carga los 4 secrets en el environment `android-connector` y la variable `ANDROID_CONNECTOR_CERT_SHA256`.
  - Cuando el código esté en `main`, empuja el tag `connector-v1.0.0`.
  - Después, un PR chico actualiza `connector-lock.json` y se publica un Tatana nuevo.

  El implementador deja todo listo y documentado, pero no puede firmar.
- B) Usar por ahora una clave generada por el implementador y cambiarla después. No se recomienda: cambiar la clave
  cambia el hash publicado y deja actas con dos firmas distintas.

**U2. ACTA y hash del manifiesto: no pueden contenerse mutuamente.** El Gherkin pide que el ACTA "indique el hash del
manifiesto" y D11 que el hash del ACTA "entre al manifiesto".

- A) **Recomendada.** El ACTA va dentro del manifiesto, sin el hash del manifiesto. El hash del manifiesto queda en
  `MANIFIESTO.tsv.sha256`, en `CIERRE.txt` (fuera del manifiesto, junto con el hash del ACTA) y en el backend, y la web
  lo muestra. Se cumple D11 tal cual, y el ACTA remite a CIERRE.
- B) El ACTA queda fuera del manifiesto y lleva el hash del manifiesto. Su propio hash va en `ACTA.sha256` y se
  registra aparte en el backend. Esto cambia D11 y suma un segundo hash a registrar y sellar.

**U3. Pantalla siempre encendida.**

- A) Durante toda la extracción, igual que la grabación de sesión. Siempre habría una modificación, así que nunca se
  vería "Ninguna: solo lectura".
- B) **Recomendada.** Solo durante la fase del connector, si se eligió el registro de llamadas: es cuando el celular
  puede mostrar el aviso de Play Protect. adb no necesita la pantalla encendida para copiar ni para consultar, y sin
  registro de llamadas la extracción queda en "Ninguna: solo lectura", como prevé la UX de la HU.
- C) Nunca. Se pierde margen ante el aviso de Play Protect con un bloqueo de 30 s.

**U4. Orden respecto de PR #103 (Tatana Docker).**

- A) **Recomendada.** Crear `feat/extraccion-logica-android` desde `develop` **después** de mergear #103, porque
  #103 movió el `docker run` a `armar_run` de `comun.sh`.
- B) Si hay apuro, crear la rama ya y dejar el ítem B-D1 (montaje de `/media`) para después del merge de #103, en un
  commit aparte. El resto de la HU no choca con #103.

## Dudas resueltas por el usuario

2026-10-25: **U1–U4 con la opción recomendada.** U1: la clave de firma del connector la genera y custodia el dueño
del producto, que dispara `connector-v1.0.0`; hasta entonces, el registro de llamadas queda deshabilitado. U2: el ACTA
va dentro del manifiesto, y el hash del manifiesto va en `MANIFIESTO.tsv.sha256` + `CIERRE.txt` + backend. U3: pantalla
encendida solo durante la fase del connector. U4: la rama `feat/extraccion-logica-android` se crea desde `develop`
después de mergear el PR #103.
