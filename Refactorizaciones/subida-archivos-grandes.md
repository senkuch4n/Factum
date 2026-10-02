# SDD: Subida de evidencia grande al caso (cientos de MB a varios GB)

**Slug:** `subida-archivos-grandes`
**HU:** `docs/hu-subida-archivos-grandes.md`. El usuario la validó el 2026-10-02 con las 12 dudas en la **A**.
**Base de código:** rama `feat/subida-archivos-grandes`, encadenada sobre `feat/instalacion-poca-ram` (`d15970a`).
**Prioridad interna:** el arreglo de seguridad del `filename` (D11a: hoy `../` escribe fuera de la carpeta del caso)
va en el **primer** commit de backend, antes que el resto.

---

## 1. Resumen funcional

`POST /api/cases/{id}/files` acepta archivos de hasta `Storage:MaxUploadBytes` (default 4 GiB, solo en ese
endpoint). La escritura es atómica:

- el cuerpo se escribe a un temporal en `<DataDirectory>/.upload-tmp/`, en el mismo volumen pero fuera de la carpeta
  del caso;
- se verifica que los bytes recibidos sean igual a `Content-Length`, se hace `fsync` y recién entonces se renombra
  al nombre final con `File.Move(..., overwrite: true)`. Así un archivo existente con el mismo nombre se reemplaza
  sin quedar nunca truncado;
- ante cualquier corte, cancelación o error, el temporal se borra. Los que quedan de un proceso muerto se borran al
  arrancar.

Antes de escribir, el backend rechaza con JSON `{ error, code, ... }` y CORS en estos casos:

| Código | Motivo |
|---|---|
| 400 | Nombre con ruta o caracteres inválidos, o falta `Content-Length` |
| 409 | El caso no es editable |
| 413 | El archivo supera el tope |
| 507 | Falta espacio: libre < tamaño + `Storage:MinFreeBytes` (default 1 GiB) |

Si el disco se llena a mitad de la escritura también responde 507, sin dejar nada.

Un endpoint de prechequeo (`GET …/files/upload-check`) aplica las mismas reglas sin cuerpo. El front lo consulta
antes de bajar el archivo de Tatana y antes de subirlo, así que el rechazo llega siempre como JSON legible y no
depende de cómo maneje el navegador una respuesta temprana a mitad de una subida de GB.

En `client/`:

- `uploadFile` pasa a `XMLHttpRequest`, con progreso real, `abort()` y errores tipados;
- la bajada desde Tatana también muestra progreso ("Preparando desde el agente…");
- un panel muestra "Enviando 3 de 7 — nombre (184 / 310 MB)" con barra, más el estado de cada archivo en la bandeja
  y un botón "Cancelar envío";
- los controles de captura quedan deshabilitados mientras dura el envío;
- los errores se muestran en castellano con el nombre del archivo y qué hacer.

Tatana, `agent-ui/` y `deploy/windows/` no cambian.

## 2. Toca

| Lado | ¿Toca? | Qué |
|---|---|---|
| backend (API) `server/src/Factum.Backend` | **sí** | `StorageOptions` (2 claves nuevas), `StorageService` (staging atómico, limpieza de huérfanos, sondeo de espacio), `EvidenceUpload` nuevo (nombre, prechequeo, formateo), `Result`/`ErrorKind`/`ResultHttpExtensions` (413/507/500 + campos extra), `CaseService.UploadFileAsync` + `CheckUploadAsync`, `CasesController` (límite por request + endpoint de prechequeo), `Program.cs` (validación de config y limpieza al arrancar), `DTOs/CaseDtos.cs`, README. Tests xUnit nuevos. |
| backend (Tatana) `server/src/Factum.Agent` | **no** | — |
| client `client/` | **sí** | `lib/api.ts` (`uploadFile` con XHR, `checkUpload`, `UploadError`), `lib/agent.ts` (`downloadFile` con progreso y abort), `lib/format.ts` (`formatBytes`), `lib/upload-messages.ts` nuevo, `types/index.ts`, `hooks/useFileManager.ts`, `components/CaptureStep.tsx`, `components/capture/EvidenceTray.tsx`, `components/capture/UploadProgressPanel.tsx` nuevo, `app/dashboard/page.tsx` (cableado). |
| agent-ui `agent-ui/` | **no** | — |
| `deploy/windows/` | **no** | Los defaults viven en código (D1). |

## 3. Modelo de datos

- **MongoDB: sin cambios.** No hay colecciones, campos ni índices nuevos. `Case.FileSources` se sigue
  escribiendo con `AddFileSourceAsync` igual que hoy, después del commit.
- **Disco (`Storage:DataDirectory`):**
  - `cases/<id>/<nombre>` sigue igual. Ahí solo aparece un archivo **completo**: se renombra desde el temporal.
  - **Nuevo:** `.upload-tmp/` en la raíz de `DataDirectory`, hermano de `cases/`. Contiene
    `<caseId>.<guid32>.part` mientras dura cada subida.
    - `ListFilesAsync` solo enumera `cases/<id>/` y `GenerateAsync` usa esa lista, así que estos archivos nunca
      se listan ni entran al ZIP.
    - Están en el mismo bind mount (`/data` → `C:\Factum\evidencia`), así que el rename es en el mismo
      filesystem.
  - **Compatibilidad:** no se mueve, renombra ni toca ningún archivo existente de `cases/`. La limpieza de huérfanos
    actúa **solo** dentro de `.upload-tmp/`. Las instalaciones viejas no tienen esa carpeta: se crea al arrancar.
- **Config nueva** (sección `Storage`, enlazada a `StorageOptions`, defaults en código):

| Clave | Env | Tipo | Default | Regla |
|---|---|---|---|---|
| `Storage:MaxUploadBytes` | `Storage__MaxUploadBytes` | `long` | `4294967296` (4 GiB) | `> 0`; si no, el backend no arranca (DT11) |
| `Storage:MinFreeBytes` | `Storage__MinFreeBytes` | `long` | `1073741824` (1 GiB) | `>= 0`; si no, el backend no arranca |

## 4. Endpoints

Todos con `[Authorize]`, dueño del caso (`LoadOwnedAsync`: 404 sin caso, 403 si es ajeno, sin cambios). El JSON
sale con `JsonNamingPolicy.SnakeCaseLower` (`Program.cs` L69-78). Los cuerpos de error son
`Dictionary<string, object?>` con las claves escritas **literalmente** en snake_case: la naming policy no
transforma las claves de un diccionario.

### 4.1 `GET /api/cases/{id}/files/upload-check?filename=<nombre>&size=<bytes>` (nuevo)

Hace los mismos chequeos que la subida, sin cuerpo y sin escribir nada. `Cache-Control: no-store`.

Orden de evaluación (el primero que falla gana):

1. `filename` inválido o ausente (§5.2) → 400 `invalid_filename`.
2. Caso inexistente o ajeno → 404 / 403 (como hoy).
3. Caso `generating`/`completed` → 409 `case_not_editable`.
4. `size` ausente, no numérico o `< 0` → 400 `length_required`.
5. `size > MaxUploadBytes` → 413 `file_too_large`.
6. Si se conoce el espacio libre y `libre < size + MinFreeBytes` → 507 `insufficient_storage`.
7. OK → `200 { "max_upload_bytes": 4294967296 }`.

### 4.2 `POST /api/cases/{id}/files?filename=<nombre>[&source_path=<ruta>]` (cambia)

- El cuerpo es crudo, como hoy. El `Content-Type` se ignora.
- `Content-Length` es **obligatorio**. Los navegadores lo mandan siempre con un `Blob`, y `curl -T`/`--data-binary`
  también.
- Si `filename` no viene, se mantiene el default de hoy, `file_yyyyMMdd_HHmmss`, que es un nombre válido.

Pasos:

1. **Antes de tocar el cuerpo:** si `IHttpMaxRequestBodySizeFeature` no es `IsReadOnly`, se fija
   `MaxRequestBodySize = MaxUploadBytes`. Es la red de Kestrel; la validación real es la del paso 2.
2. Mismos chequeos 1-6 de §4.1, con `size = Request.ContentLength`. Si `ContentLength` es `null` → 400
   `length_required`. Ninguno lee el cuerpo ni crea archivos.
3. Staging (§5.3). Errores posibles:
   - bytes ≠ `Content-Length`, corte o lectura fallida → 400 `incomplete_upload` (si el cliente sigue ahí para
     leerlo);
   - disco lleno → 507 `insufficient_storage`;
   - cancelación (`RequestAborted`) → no se espera que nadie lea la respuesta; el temporal se borra igual;
   - cualquier otro error → 500 `storage_error`, con log `Error`.
4. Se vuelve a leer el caso. Si dejó de ser editable (empezó una generación durante la subida), se descarta el
   temporal → 409 `case_not_editable`.
5. Commit: rename atómico. Después, `AddFileSourceAsync` si vino `source_path` (como hoy).
6. `200` con `FileInfoDto`, **sin cambios** en su forma:
   `{ "name", "size", "hash", "modified_at", "source_path" }`. `hash` es el SHA-256 calculado al vuelo.

**Ninguna excepción sale del action sin respuesta JSON.** Hoy, una excepción no manejada produce un 500 de Kestrel
sin CORS, que el navegador muestra como "Failed to fetch". `CaseService.UploadFileAsync` atrapa todo y lo traduce a
un `Result`.

### 4.3 Resto de endpoints

Sin cambios. El tope de 30 MB de Kestrel sigue rigiendo en todos (D2).

## 5. Diseño técnico — backend

### 5.1 Config y arranque (`Infrastructure/StorageService.cs`, `Program.cs`)

```csharp
public sealed class StorageOptions
{
    public string DataDirectory { get; set; } = "./data";
    public long MaxUploadBytes { get; set; } = 4L * 1024 * 1024 * 1024;   // 4 GiB
    public long MinFreeBytes { get; set; } = 1L * 1024 * 1024 * 1024;     // 1 GiB
}
```

En `Program.cs`, junto a la validación existente (`configErrors`), se leen `Storage:MaxUploadBytes` y
`Storage:MinFreeBytes`. Si hay valores fuera de rango, se suman a `configErrors` con texto en castellano
(`"Storage:MaxUploadBytes tiene que ser mayor que 0"`) y el backend no arranca (DT11).

Después de `app.Services.GetRequiredService<IReportSettings>()`:

1. Se resuelve `IStorageService` y se llama a `CleanupOrphanUploads()`.
2. Se loguea una línea `Information`:
   `"Subidas: tope {Max}, margen libre {MinFree}, espacio libre en {DataDirectory}: {Free} (temporales huérfanos borrados: {N})"`.
   `Free` es el valor del sondeo o `"desconocido"`. La medición de D12 usa esta línea para confirmar que el
   contenedor ve el espacio real de `C:`.

### 5.2 `Services/Cases/EvidenceUpload.cs` (nuevo, `static`, lógica pura y testeable)

**`public static bool IsValidUploadName(string? name)`.** Devuelve `true` solo si se cumple todo esto:

- `ReportImageRef.IsPlainName(name)` (no vacío, ≤ 255, sin `/` ni `\`, sin control, distinto de `.`/`..`, igual a
  su `GetFileName`);
- no contiene ninguno de `< > : " | ? *` (inválidos en NTFS, que es el disco real detrás del bind mount en
  Windows);
- no termina en `.` ni en espacio;
- el nombre sin extensión, en mayúsculas, no es un nombre reservado de Windows: `CON`, `PRN`, `AUX`, `NUL`,
  `COM1`-`COM9`, `LPT1`-`LPT9`.

Los nombres reales de Factum siempre pasan: Tatana renombra lo que trae del celular a `device_pull_<ts>.<ext>` y el
front, los adjuntos a `adjunto_<fecha>_<hora>_<n>.<ext>`.

**`public static class UploadErrorCodes`.** Constantes `InvalidFilename = "invalid_filename"`,
`CaseNotEditable = "case_not_editable"`, `LengthRequired = "length_required"`, `FileTooLarge = "file_too_large"`,
`InsufficientStorage = "insufficient_storage"`, `IncompleteUpload = "incomplete_upload"`,
`StorageError = "storage_error"`.

**`public static UploadRejection? Evaluate(long size, long maxUploadBytes, long? availableBytes, long minFreeBytes)`.**
Devuelve `null` si pasa:

- `size > max` → `TooLarge`;
- `available is not null && available < size + minFree` → `InsufficientStorage(required: size + minFree, available)`.

`UploadRejection` es un record con `Kind` y los números.

**`public static string FormatBytes(long bytes)`.** Mismas reglas que el front (§6.3): base 1024 con etiquetas
`KB`/`MB`/`GB`, un decimal con **coma** y sin `,0` final. Se arma a mano con `CultureInfo.InvariantCulture` y
`Replace('.', ',')`, sin depender de la cultura del contenedor. Ejemplos: `4294967296 → "4 GB"`,
`5583457485 → "5,2 GB"`, `325058560 → "310 MB"`, `512 → "512 B"`.

**Mensajes `error` del backend** (fallback legible si el front no reconoce el `code`):

| `code` | Mensaje |
|---|---|
| `invalid_filename` | `Nombre de archivo inválido: no puede tener rutas ni los caracteres < > : " \| ? *` |
| `case_not_editable` | `CaseService.NotEditableMessage` (el de siempre) |
| `length_required` | `Falta el tamaño del archivo (Content-Length)` |
| `file_too_large` | `El archivo {name} pesa {Fmt(size)} y el máximo permitido es {Fmt(max)}` |
| `insufficient_storage` | `No hay espacio en el disco del servidor para guardar {name} (hace falta {Fmt(required)}, quedan {Fmt(available)})`. Si el disco se llenó a mitad y no se pudo sondear: `No hay espacio en el disco del servidor para guardar {name}` |
| `incomplete_upload` | `La subida de {name} llegó incompleta ({Fmt(received)} de {Fmt(expected)}); no se guardó nada` |
| `storage_error` | `El servidor no pudo guardar {name}; no se guardó nada` |

### 5.3 `StorageService`: staging atómico (`Infrastructure/StorageService.cs` + `Infrastructure/UploadStaging.cs` nuevo)

**Sondeo de espacio:**

- `public interface IDiskSpaceProbe { long? GetAvailableFreeBytes(string path); }`.
- Implementación `DriveInfoDiskSpaceProbe`: `new DriveInfo(path).AvailableFreeSpace` dentro de `try`; ante
  cualquier excepción devuelve `null`. En Linux, `DriveInfo` acepta cualquier ruta y hace `statvfs`.
- Si `GetAvailableFreeBytes` devuelve `null`, el prechequeo de espacio se saltea (fail-open) y queda la red de
  disco lleno a mitad de escritura.
- Se registra como singleton en `Program.cs`.

**Constructor:**

- `StorageService(IOptions<StorageOptions> opts, IDiskSpaceProbe probe)` es el público, el que usa DI.
- `internal StorageService(StorageOptions opts, IDiskSpaceProbe probe, Func<string, Stream>? openTempForWrite)` es
  para tests (`InternalsVisibleTo` ya existe).
- `openTempForWrite` por defecto:
  `new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.Asynchronous)`.
  `bufferSize: 1` desactiva el buffer interno, porque se escribe con un buffer propio.
- El constructor crea `_root`, `cases/` (como hoy) y `_uploadTmp = Path.Combine(_root, ".upload-tmp")`.

**API nueva de `IStorageService`** (se **quita** `SaveFileAsync`, que es el camino inseguro; hoy solo lo usa
`UploadFileAsync`, y el implementador lo confirma con un grep):

```csharp
long? GetAvailableFreeBytes();                       // probe sobre _root
Task<StagedUpload> StageUploadAsync(string caseId, string filename, Stream content,
    long expectedLength, CancellationToken ct);
int CleanupOrphanUploads();                          // borra SOLO archivos dentro de .upload-tmp/
```

**`StageUploadAsync`:**

1. `temp = Path.Combine(_uploadTmp, $"{caseId}.{Guid.NewGuid():N}.part")` y se abre con `openTempForWrite`.
2. Bucle propio con buffer de **1 MiB** (`ArrayPool<byte>.Shared`), con SHA-256 incremental
   (`IncrementalHash.CreateHash(HashAlgorithmName.SHA256)`) y contador `received`:
   - la **lectura** del cuerpo va en su propio `try`. Si falla con algo que no es `OperationCanceledException`
     (corte de red, `BadHttpRequestException` de Kestrel, `IOException`), se lanza
     `UploadIncompleteException(received, expectedLength, inner)`;
   - si `received` supera `expectedLength`, se lanza `UploadIncompleteException` sin seguir leyendo;
   - la **escritura** va en su propio `try`. Una `IOException` es disco lleno si
     `HResult is 28 (ENOSPC en Linux) or unchecked((int)0x80070070) or unchecked((int)0x80070027)`, o si después
     de la falla el sondeo da menos libre que `expectedLength - received`. En ese caso se lanza
     `InsufficientStorageException(required: expectedLength + MinFreeBytes, available: probe, inner)`. Cualquier
     otra `IOException` se relanza.
3. Al terminar el bucle: si `received != expectedLength` → `UploadIncompleteException`.
4. Se llama a `Flush(flushToDisk: true)` cuando el stream es `FileStream` (`fsync`, DT5), se cierra el stream y se
   devuelve un `StagedUpload`.
5. En **cualquier** salida por excepción (incluida la cancelación), un `finally` borra `temp` con
   `try { File.Delete(temp) } catch { log Warning }`, sin `CancellationToken`.

**`StagedUpload : IDisposable`** (`TempPath`, `FinalPath`, `Length`, `Hash`):

- `FileInfo Commit()` hace `File.Move(TempPath, FinalPath, overwrite: true)`: en Linux es `rename(2)` en el mismo
  filesystem, atómico y con reemplazo. Antes asegura que exista `CaseDir(caseId)`. Marca `committed` y devuelve
  `new FileInfo(FinalPath)`.
- `Dispose()`: si no hubo commit, borra `TempPath` (best effort).
- `FinalPath` se arma con `Path.Combine(CaseDir(caseId), filename)`. Como defensa en profundidad, se verifica que
  `Path.GetFullPath(FinalPath)` empiece con `Path.GetFullPath(CaseDir(caseId)) + Path.DirectorySeparatorChar`; si
  no, `ArgumentException`, aunque el nombre ya llega validado.

**`CleanupOrphanUploads()`:**

- Si `.upload-tmp/` no existe, la crea y devuelve 0.
- Si no, `Directory.EnumerateFiles(_uploadTmp)` (sin recursión): borra cada archivo y cuenta los borrados.
- No toca subcarpetas ni nada fuera de `.upload-tmp/`.
- Corre solo al arrancar, cuando no puede haber subidas en curso (una instancia por `DataDirectory`).

**`ListFilesAsync`, `CaseDir`, `DeleteCaseFiles`:** sin cambios.

### 5.4 `Common/Result.cs` y `Controllers/ResultHttpExtensions.cs`

- `ErrorKind` suma `PayloadTooLarge`, `InsufficientStorage` y `ServerError`, al final del enum para no cambiar los
  valores existentes.
- `Result<T>` suma `IReadOnlyDictionary<string, object?>? Details` y la sobrecarga
  `Fail(ErrorKind kind, string error, IReadOnlyDictionary<string, object?> details)`. `Cast<TOut>()` propaga
  `Details`.
- `ErrorResult`:
  - si `result.Details is null`, **exactamente** lo de hoy: los endpoints existentes no cambian de forma;
  - si no es `null`, arma `var body = new Dictionary<string, object?> { ["error"] = error }` (más `["missing"]` si
    hay), le agrega los `Details` y responde según el kind: NotFound → 404, Conflict → 409, Validation/Failure →
    400, PayloadTooLarge → 413, InsufficientStorage → 507, ServerError → 500 (con `c.StatusCode(code, body)`).
    Forbidden sigue con `Forbid()`.
  - `PayloadTooLarge`/`InsufficientStorage`/`ServerError` sin `Details` se mapean igual a 413/507/500 con
    `{ error }`.

### 5.5 `CaseService`

**`Task<Result<UploadCheckResponse>> CheckUploadAsync(string id, string officerDni, string? filename, long? size, CancellationToken ct)`** (nuevo, en `ICaseService`):

- Pasos 1-6 de §4.1.
- Usa `IOptions<StorageOptions>`, que se inyecta en el constructor de `CaseService`, y
  `_storage.GetAvailableFreeBytes()`.
- Los rechazos llevan `Details`: `{ "code": ... }` más los números (§7).

**`UploadFileAsync(string id, string officerDni, string filename, string? sourcePath, long? contentLength, Stream content, CancellationToken ct)`** (cambia la firma: se suma `contentLength`):

1. Mismos chequeos que `CheckUploadAsync`, comparten un helper privado.
2. `using var staged = await _storage.StageUploadAsync(id, filename, content, contentLength!.Value, ct);` dentro de
   un `try` con estos `catch`:

| Excepción | Resultado |
|---|---|
| `UploadIncompleteException` | `Invalid` + `Details { code: incomplete_upload, size: expected, received_bytes: received }` y log `Information` (un corte es normal) |
| `InsufficientStorageException` | `InsufficientStorage` + `Details { code, size, required_bytes, available_bytes }` y log `Warning` |
| `OperationCanceledException` cuando `ct.IsCancellationRequested` | Log `Information` "subida cancelada/cortada"; devuelve `Invalid` con `incomplete_upload`, que nadie lee |
| cualquier otra `Exception` | `ServerError` + `Details { code: storage_error }` y log `Error` con el caseId y el nombre (no el contenido) |

3. `var fresh = await _repo.FindByIdAsync(id, CancellationToken.None)`. Si es `null` o `!IsEditable(fresh)` →
   `Conflict` con `case_not_editable` (el `using` borra el temporal).
4. `var info = staged.Commit();` y, si hay `sourcePath`, `AddFileSourceAsync` (como hoy).
5. `Ok(new FileInfoDto(info.Name, info.Length, staged.Hash, info.LastWriteTimeUtc, sourcePath))`.

Ventana residual: si una generación empieza **entre** el paso 3 y el 4 (milisegundos), el archivo queda suelto en un
caso que se está generando. Se acepta y queda documentado (DT9).

### 5.6 `CasesController`

- Constructor `CasesController(ICaseService caseService, IOptions<StorageOptions> storage)`.
- `UploadFile`:
  - primero fija el límite:
    `var f = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>(); if (f is { IsReadOnly: false }) f.MaxRequestBodySize = storage.Value.MaxUploadBytes;`;
  - después llama al servicio con `Request.ContentLength`;
  - suma `[ProducesResponseType]` 400/409/413/507/500.
- Nuevo `[HttpGet("{id}/files/upload-check")] UploadCheck(string id, [FromQuery] string? filename, [FromQuery] long? size, CancellationToken ct)`:
  pone `Response.Headers.CacheControl = "no-store"` y responde `Ok(result.Value)` o `this.ErrorResult(result)`.
  Con un `size` no numérico, `[ApiController]` respondería 400 con un ProblemDetails propio. Para que salga
  nuestro `length_required`, se recibe `[FromQuery(Name = "size")] string? size` y se parsea a mano con
  `long.TryParse(..., NumberStyles.None, CultureInfo.InvariantCulture)`.
- Los rechazos tempranos de la subida salen sin leer el cuerpo. Kestrel intenta drenar el resto unos segundos y
  después cierra la conexión. El navegador puede o no entregar la respuesta, y por eso el front usa el prechequeo
  (DT2). `curl` manda `Expect: 100-continue` en cuerpos grandes y recibe el 413/507 limpio, sin subir nada.

### 5.7 `DTOs/CaseDtos.cs`

`public sealed record UploadCheckResponse(long MaxUploadBytes);`, que viaja como `{ "max_upload_bytes": N }`.

### 5.8 README

En "Variables de entorno / configuración" se agregan dos filas, `Storage:MaxUploadBytes` y `Storage:MinFreeBytes`,
con env, default y para qué sirven. Además, una línea: "las subidas en curso se escriben en
`<DataDirectory>/.upload-tmp/`; no es evidencia y se limpia al arrancar".

## 6. Diseño técnico — `client/`

### 6.1 `client/src/types/index.ts` (tipos del contrato)

```ts
/** Respuesta de POST /api/cases/{id}/files (FileInfoDto). */
export interface UploadedFileInfo { name: string; size: number; hash: string; modified_at: string; source_path?: string | null; }
export interface UploadCheckResponse { max_upload_bytes: number; }
export type UploadErrorCode =
  | "invalid_filename" | "case_not_editable" | "length_required" | "file_too_large"
  | "insufficient_storage" | "incomplete_upload" | "storage_error";
/** Cuerpo de error de las rutas de subida (snake_case tal cual viaja). */
export interface UploadErrorBody {
  error: string; code?: UploadErrorCode; size?: number; max_upload_bytes?: number;
  required_bytes?: number; available_bytes?: number | null; received_bytes?: number;
}
export type UploadPhase = "checking" | "preparing" | "uploading" | "finishing";
export type FileUploadState = "uploading" | "error";
export interface UploadProgress {
  index: number;            // 1-based, archivo actual
  total: number;            // archivos a enviar en esta tanda
  name: string;
  phase: UploadPhase;
  loaded: number;           // bytes de la fase actual
  totalBytes: number | null; // null si no se conoce (lengthComputable=false)
}
```

### 6.2 `client/src/lib/api.ts`

**`export class UploadError extends ApiError`:**

- campos: `kind: "http" | "network" | "aborted"`, `code: UploadErrorCode | null`,
  `body: UploadErrorBody | null`;
- `status` vale `0` cuando `kind` es `"network"` o `"aborted"`;
- `message`: el `error` del body, o un texto genérico. **Nunca** "Failed to fetch" ni "HTTP 413", porque la UI usa
  `upload-messages.ts`.

**`checkUpload(caseId, filename, size, signal?)`:**

- devuelve `Promise<UploadCheckResponse>`;
- es un `GET` con `send` al endpoint de §4.1, con `encodeURIComponent` en los parámetros (`URLSearchParams`);
- si la respuesta no es ok, lanza `UploadError("http", status, code, body)`;
- si `fetch` falla, lanza `UploadError("network")`;
- un `AbortError` se convierte en `UploadError("aborted")`.

**`uploadFile(caseId, filename, blob, opts?: { sourcePath?: string; signal?: AbortSignal; onProgress?: (loaded: number, total: number) => void })`:**

- devuelve `Promise<UploadedFileInfo>`. Se arregla el tipo de retorno, que hoy dice `{ filename, hash }` y no
  coincide con el backend;
- usa `XMLHttpRequest`:
  - `open("POST", url)`, header `Authorization` si hay token y `xhr.timeout = 0`;
  - `xhr.upload.onprogress = e => onProgress?.(e.loaded, e.lengthComputable ? e.total : blob.size)`;
  - `signal.addEventListener("abort", () => xhr.abort(), { once: true })`; si `signal.aborted` ya está en `true`,
    rechaza sin abrir;
  - `onload`: un 2xx hace `JSON.parse(xhr.responseText)`; cualquier otro código se parsea con `try` como
    `UploadErrorBody` y lanza `UploadError("http", xhr.status, …)`;
  - `onerror`/`ontimeout` → `UploadError("network")`;
  - `onabort` → `UploadError("aborted")`;
  - `xhr.send(blob)`. El navegador pone `Content-Length` y el `Content-Type` del blob.
- se quita el listener de `abort` al terminar.

### 6.3 `client/src/lib/format.ts`

`export function formatBytes(bytes: number): string` sigue las mismas reglas que `EvidenceUpload.FormatBytes`:
base 1024, etiquetas `B`/`KB`/`MB`/`GB`, un decimal con coma (`toLocaleString("es-AR", { maximumFractionDigits: 1 })`)
y sin `,0`. Un helper `formatBytesPair(loaded, total)` devuelve `"184 / 310 MB"`, con la unidad del total.
`CaptureStep` L568-571 (el `sizeMB` del chip de adjunto) puede seguir como está; no es obligatorio migrarlo.

### 6.4 `client/src/lib/agent.ts`

`downloadFile(filename, opts?: { signal?: AbortSignal; onProgress?: (loaded: number, total: number | null) => void })`:

- devuelve `Promise<Blob>`;
- usa XHR con `responseType = "blob"`, `onprogress` y abort. Una falla de red, un estado distinto de 2xx o el abort
  rechazan con `Error` (o `DOMException("AbortError")` en el abort). El hook los distingue;
- la firma sin `opts` sigue funcionando igual.

### 6.5 `client/src/lib/upload-messages.ts` (nuevo, puro)

`export function uploadErrorMessage(err: unknown, name: string, size: number | null): { tone: "error" | "info"; text: string }`

| Caso | Tono | Texto |
|---|---|---|
| `UploadError` `aborted` | info | `Envío cancelado. Los archivos ya enviados quedaron guardados en el caso; los demás siguen en la lista.` |
| 413 / `file_too_large` | error | `El archivo {name} pesa {fmt(body.size ?? size)} y el máximo permitido es {fmt(body.max_upload_bytes)}. Quitalo de la lista o pedí que se suba el tope.` |
| 507 / `insufficient_storage` con números | error | `No hay espacio en el disco del servidor para guardar {name} (hace falta {fmt(required)}, quedan {fmt(available)}). Liberá espacio y volvé a enviar.` |
| 507 / `insufficient_storage` sin números | error | `No hay espacio en el disco del servidor para guardar {name}. Liberá espacio y volvé a enviar.` |
| 409 | error | `El caso ya fue generado y no admite más evidencia.` |
| 400 / `invalid_filename` | error | `El nombre del archivo {name} no es válido para guardarlo en el caso (tiene una ruta o caracteres no permitidos). Quitalo de la lista.` |
| `network`, 400 / `incomplete_upload` o `length_required` | error | `Se cortó el envío de {name}. Los archivos anteriores quedaron guardados; tocá "Enviar evidencia y continuar" para seguir.` |
| 500 / `storage_error` | error | `El servidor no pudo guardar {name}. Los archivos anteriores quedaron guardados; volvé a intentar.` |
| 401 | error | `Tu sesión expiró. Volvé a iniciar sesión y enviá de nuevo la evidencia.` |
| otro `UploadError` http | error | `No se pudo enviar {name}: {body.error}` si hay `error`; si no, `No se pudo enviar {name} (error {status}).` |
| falla de la bajada desde Tatana (no es `UploadError`) | error | `No se pudo preparar {name} desde el agente Tatana. Verificá que el agente siga abierto y que la PC tenga espacio libre en disco, y volvé a enviar.` |

### 6.6 `client/src/hooks/useFileManager.ts`

Estado nuevo:

- `uploadProgress: UploadProgress | null`;
- `uploadStates: Record<string, FileUploadState>`, transitorio: "pendiente" es `!uploaded` sin entrada, y "subido"
  es `uploaded: true`;
- `uploadNotice: { tone: "info" | "error"; text: string } | null`, para el aviso de cancelación junto al botón;
- `uploadAbortRef = useRef<AbortController | null>(null)`;
- `filesRef`, espejo de `files` con el mismo patrón que `localBlobsRef`.

**`handleUploadAndContinue(currentCase, onSuccess, onError, onStatus, onRolesSaved)`** mantiene la firma; la
página no cambia la llamada:

1. `setLoad("upload", true)`, `setUploadNotice(null)` y `const ac = new AbortController()` en el ref. No se llama
   `onStatus("Enviando archivos…")`, porque el panel lo reemplaza.
2. `agentSizes`: un solo `agent.listFiles()` al principio, dentro de un `try`. Si falla queda un `Map` vacío.
3. Se recorre **`filesRef.current`** en un `while` hasta que no quede ninguno con `!uploaded` que no se haya
   intentado en esta tanda (DT12). Así entran los archivos que agregó el WebSocket durante el envío. `total` se
   recalcula como `ya intentados + pendientes`. Por cada archivo:
   1. `uploadStates[name] = "uploading"`, fase `checking`.
   2. `size` = `pendingBlobs.get(name)?.size` o `agentSizes.get(name)`.
   3. Si `size` se conoce → `await api.checkUpload(caseId, name, size, ac.signal)`. Si se rechaza, se corta antes de
      bajar nada de Tatana.
   4. Blob: el de `pendingBlobs` (local, sin copia: un `File` de la PC está respaldado en disco) o
      `await agent.downloadFile(name, { signal, onProgress })` en fase `preparing`. Si `size` no se conocía, se
      llama a `checkUpload` con `blob.size` antes de subir.
   5. `await api.uploadFile(caseId, name, blob, { sourcePath, signal, onProgress })` en fase `uploading`. Cuando
      `loaded === total` y la respuesta todavía no llegó, la fase pasa a `finishing` ("Verificando en el
      servidor…").
   6. Se marca `uploaded: true` y se borra la entrada de `uploadStates`. **El `Blob` bajado de Tatana es una
      variable local del bucle:** no se guarda en `pendingBlobs` ni en el estado, así el navegador puede liberarlo
      apenas termina la subida (D10). Si el progreso usa closures, no deben retenerlo.
4. Los roles salen de `filesRef.current` al final, no del snapshot de la llamada. El resto queda como hoy
   (`saveCaptureRoles`, `onRolesSaved`, `onSuccess`).
5. `catch (e)`:
   - el archivo actual queda en `uploadStates[name] = "error"`, salvo que sea un abort: ahí se borra la entrada y
     vuelve a pendiente;
   - `const m = uploadErrorMessage(e, name, size)`. Si `m.tone === "info"`, va a `setUploadNotice(m)`; si es
     `error`, a `onError(m.text)`, el aviso global de siempre.
6. `finally`: `setLoad("upload", false)`, `setUploadProgress(null)`, `uploadAbortRef.current = null` y
   `onStatus("")`.

Más:

- **`cancelUpload()`** hace `uploadAbortRef.current?.abort()`.
- **`removeFile`** no hace nada mientras `loading.upload` (defensa). La UI ya lo deshabilita.
- **`clearFiles`** también limpia `uploadStates` y `uploadNotice`.
- **Exporta:** `uploadProgress`, `uploadStates`, `uploadNotice`, `dismissUploadNotice` y `cancelUpload`.

### 6.7 UI

**`components/capture/UploadProgressPanel.tsx` (nuevo)**

- Props: `progress: UploadProgress`, `onCancel: () => void`.
- Va encima de la `StepActions` de `CaptureStep`, dentro de un `fx-card`. Muestra:
  - Línea 1, el título: `Enviando {index} de {total}`.
  - Línea 2: el nombre del archivo con `truncate` y `title` completo, y a la derecha `formatBytesPair(loaded, totalBytes)`.
  - Barra: `ProgressBar` de Prime, que ya se usa en `StepIndicator`. Va con `role="progressbar"`,
    `aria-valuemin=0`, `aria-valuemax=100`, `aria-valuenow`, `aria-valuetext="184 de 310 MB"` y
    `aria-label="Progreso de {name}"`. Si `totalBytes` es `null`, la barra es indeterminada.
  - Fase en texto chico, según `phase`:
    - `checking`: "Verificando espacio…";
    - `preparing`: "Preparando desde el agente…";
    - `uploading`: "Subiendo al servidor…";
    - `finishing`: "Verificando en el servidor…".
  - Línea de total: `{index - 1} de {total} enviados`.
- Región `aria-live="polite"` (visualmente oculta) que anuncia **solo** el cambio de archivo y de fase, nunca cada
  porcentaje.
- Botón secundario "Cancelar envío" (`Button` de Prime, `outlined`/`severity="secondary"`, `min-h-11`).
- Las animaciones van con `motion-safe`.

**`components/CaptureStep.tsx`**

- Props nuevas: `uploading: boolean`, `uploadProgress`, `uploadStates`, `uploadNotice`, `onCancelUpload` y
  `onDismissUploadNotice`.
- Con `uploading` en `true`, se pone `disabled` en:
  - captura de pantalla, grabar/detener, selector de modo iOS y AirPlay shot;
  - fotos de webcam (perito/titular) y cámara externa;
  - zona de adjuntar: input, botón y drop. `onDrop` ignora los eventos;
  - explorador del celular;
  - botones de eliminar (tile, chip y detalle) y `CaptureRoleMenu`.

  La navegación de la galería (seleccionar y ver) sigue habilitada.
- Botón "Enviar evidencia y continuar": queda `loading` como hoy y, al lado (en `StepActions`), aparece "Cancelar
  envío" mientras `uploading`. El del panel y este son el mismo handler; el implementador elige uno solo si el
  layout duplica. Se recomienda dejarlo **solo en el panel**.
- `uploadNotice`: un `FxBanner tone="info"` con `onClose` encima de la botonera.

**`components/capture/EvidenceTray.tsx`**

`EvidenceTrayTile` y `AttachmentChip` reciben `uploadState?: "pending" | "uploading" | "uploaded" | "error"`
(derivado en `CaptureStep` de `remoteFile.uploaded`/`uploadStates`). Se muestra solo en los tiles cuyo nombre está
en `files`:

- un indicador chico con ícono y color, nunca solo color:
  - subido: `CheckCircle2`;
  - subiendo: `Loader2` con `motion-safe:animate-spin`;
  - error: `AlertCircle` en tono error;
  - pendiente: nada (o un punto neutro);
- el estado se suma al `aria-label` existente (`… — subido`).

**`app/dashboard/page.tsx`**

- Pasa las props nuevas a `CaptureStep` desde `useFileManager`.
- Durante el paso 3 con `uploading`, **no** se muestra el `FxBanner` de `statusMsg` para el envío, porque el hook
  ya no lo setea. Sigue saliendo "Guardando marcas de las capturas…".

## 7. Contrato compartido

**Política:** `JsonNamingPolicy.SnakeCaseLower` (`server/src/Factum.Backend/Program.cs` L69-78). Los cuerpos de
error de subida son `Dictionary<string, object?>` con las claves ya en snake_case literal.

| Elemento | Valor exacto | Backend | Client |
|---|---|---|---|
| Prechequeo | `GET /api/cases/{id}/files/upload-check?filename=<str>&size=<int>` | `Controllers/CasesController.cs` (`UploadCheck`), `Services/Cases/CaseService.cs` (`CheckUploadAsync`) | `client/src/lib/api.ts` (`checkUpload`) |
| OK del prechequeo | `200 { "max_upload_bytes": number }` | `DTOs/CaseDtos.cs` (`UploadCheckResponse`) | `client/src/types/index.ts` (`UploadCheckResponse`) |
| Subida | `POST /api/cases/{id}/files?filename=<str>[&source_path=<str>]`, cuerpo crudo, `Content-Length` obligatorio | `CasesController.UploadFile`, `CaseService.UploadFileAsync` | `api.uploadFile` (XHR) |
| OK de la subida | `200 { "name", "size", "hash", "modified_at", "source_path" }` (sin cambios) | `DTOs/CaseDtos.cs` (`FileInfoDto`) | `types/index.ts` (`UploadedFileInfo`) |
| Error: siempre | `"error": string` | `Controllers/ResultHttpExtensions.cs` | `UploadErrorBody.error` |
| Error: `code` | `"invalid_filename"` (400), `"length_required"` (400), `"incomplete_upload"` (400), `"case_not_editable"` (409), `"file_too_large"` (413), `"insufficient_storage"` (507), `"storage_error"` (500) | `Services/Cases/EvidenceUpload.cs` (`UploadErrorCodes`) | `types/index.ts` (`UploadErrorCode`) + `lib/upload-messages.ts` |
| 413 extra | `"size": number`, `"max_upload_bytes": number` | `CaseService` (Details) | `UploadErrorBody` |
| 507 extra | `"size": number`, `"required_bytes": number`, `"available_bytes": number \| null` | ídem | ídem |
| 400 `incomplete_upload` extra | `"size": number` (esperado), `"received_bytes": number` | ídem | ídem |
| 404 / 403 | Como hoy: 404 `{ "error": "Caso no encontrado" }` y 403 sin cuerpo | sin cambios | `upload-messages.ts` cae en "otro http" |
| CORS | Todas las respuestas de estas rutas salen por el pipeline (`UseCors`) porque ninguna excepción escapa del action | `CaseService` atrapa todo | — |
| Config | `Storage:MaxUploadBytes` (`Storage__MaxUploadBytes`), default `4294967296`; `Storage:MinFreeBytes` (`Storage__MinFreeBytes`), default `1073741824` | `Infrastructure/StorageService.cs` (`StorageOptions`), `Program.cs` | — (el front no las lee; el 413 trae `max_upload_bytes`) |
| Formato de tamaños | Base 1024, `B`/`KB`/`MB`/`GB`, un decimal con coma y sin `,0` | `EvidenceUpload.FormatBytes` | `lib/format.ts` (`formatBytes`) |

Los endpoints existentes que usan `ErrorResult` sin `Details` conservan **exactamente** su forma actual: `{ error }`
o `{ error, missing }`.

## 8. Decisiones técnicas

Ninguna necesita al usuario antes de implementar. Todas están dentro de lo que validó en la HU. DT13 y DT15 cambian
un poco lo visible y se le informan al cerrar.

- **DT1. Temporales en `<DataDirectory>/.upload-tmp/`** (recomendada). Se descartan dos alternativas:
  - una subcarpeta dentro de cada `cases/<id>/`: ensucia la carpeta del caso y obliga a revisar todo lo que la
    recorre;
  - el `/tmp` del contenedor: está en otro filesystem, así que el rename sería copia + borrado (no atómico) y además
    escribiría GB en la capa del contenedor.

  La raíz de `DataDirectory` es el mismo bind mount que `cases/`. La limpieza toca una sola carpeta conocida.
- **DT2. Prechequeo `GET …/files/upload-check`** (recomendada). Con un cuerpo de GB en vuelo, si el servidor
  responde 413/507 sin leerlo, Kestrel drena unos segundos y corta. Chromium puede reportar eso como error de red y
  perder el JSON. El prechequeo garantiza el mensaje claro de D4. También evita bajar 2 GB de Tatana para un archivo
  que se va a rechazar. El POST repite los chequeos, porque es la fuente de verdad. No es visible para el usuario.
- **DT3. Cuerpo de error `{ error, code, …números }`** (recomendada). Cumple el `{ error }` de la HU y suma un
  `code` estable y los tamaños, así el front arma el mensaje con su formato y la acción que corresponde. Además,
  `error` sirve como fallback legible.
- **DT4. Tamaños en base 1024 con etiquetas "MB"/"GB"** (recomendada). Es lo que muestra el Explorador de Windows
  en la PC del perito. El tope de 4 GiB se ve como "4 GB", igual que en la HU.
- **DT5. `fsync` del temporal antes del rename** (recomendada). Sin `fsync`, un apagón justo después del rename
  puede dejar el nombre definitivo con contenido incompleto. Es el patrón estándar de escritura atómica. Cuesta
  esperar el flush del archivo, lo mismo que tarda en escribirse, y se mide en D12.
- **DT6. Sin `Content-Length` → 400 `length_required`, no 411** (recomendada). Mantiene el conjunto de códigos de
  D4. Hace falta igual: sin el tamaño no hay prechequeo de espacio ni verificación de bytes. Los navegadores y
  `curl` siempre lo mandan.
- **DT7. Reglas del nombre** (§5.2) (recomendada). Además de rutas y caracteres de control, rechaza los caracteres y
  nombres reservados de NTFS, porque el disco real en Windows es NTFS detrás del bind mount. No afecta a ningún
  flujo actual: Tatana y el front generan nombres seguros.
- **DT8. Limpieza de huérfanos al arrancar: todo lo que haya en `.upload-tmp/`** (recomendada). Se asume una
  instancia del backend por `DataDirectory`, que es el caso de todos los despliegues. No se limpia periódicamente:
  el `finally` de cada subida ya borra su temporal.
- **DT9. Revalidar "editable" justo antes del commit** (recomendada). Cierra casi toda la carrera con una
  generación que empieza durante una subida larga. La ventana de milisegundos que queda está documentada en §5.5.
- **DT10. Extender `Result`/`ErrorKind`** (`PayloadTooLarge`, `InsufficientStorage`, `ServerError` y `Details`) en
  vez de un tipo de resultado aparte (recomendada). Sin `Details`, el mapeo de hoy queda idéntico.
- **DT11. Config de subida inválida = el backend no arranca** (recomendada). Es el mismo criterio que
  `Auth`/`Integrations` en `Program.cs`. Un valor `0` o negativo dejaría el endpoint inutilizable sin que nadie lo
  note.
- **DT12. El bucle de envío relee `filesRef.current`** (recomendada). Hoy recorre un snapshot: si el WebSocket
  agrega un archivo durante el envío (por ejemplo, al terminar una grabación), ese archivo no se sube y la página
  pasa al paso 4. Además, los roles se leen al final del mismo ref.
- **DT13. Lo que se deshabilita durante el envío** (§6.7) (recomendada, *informar al usuario*). Además de captura y
  quitar, también el menú de marcas de captura y el explorador, para que el snapshot de roles y la lista no cambien
  a mitad. La galería sigue navegable.
- **DT14. Bajada desde Tatana con XHR y progreso, y prechequeo antes de bajar** (recomendada). Es la fase
  "Preparando desde el agente…" de la UX de la HU.
- **DT15. D10 (Blob en el navegador) sin rediseño; mitigaciones dentro de la A** (recomendada, *informar al
  usuario con la medición*):
  - **Estimación.** Chromium (Edge/Chrome) guarda en RAM los blobs hasta un presupuesto proporcional a la RAM física
    (del orden de cientos de MB en una PC de 4 GB) y pagina el resto a archivos en `blob_storage` del perfil, en el
    disco `C:`. Un video de 2 GB bajado de Tatana **no** ocupa 2 GB de RAM, pero ocupa ~2 GB más de disco mientras
    dura su envío. Pico de disco de un archivo de Tatana de tamaño S: Tatana S + `blob_storage` S + temporal en la
    evidencia S, que después pasa a ser el final. O sea **~3 S** transitorios. El prechequeo del backend solo ve la
    parte de la evidencia. Si la PC tiene la evidencia en `C:` (instalación estándar), el margen de 1 GiB es la
    única protección para el `blob_storage`.
  - **Mitigaciones incluidas:**
    1. el `Blob` de Tatana es local al bucle y se suelta al terminar cada archivo, así que nunca hay dos a la vez;
    2. el prechequeo va **antes** de bajar de Tatana;
    3. una falla de la bajada (incluido "sin espacio" del navegador) da un mensaje claro;
    4. los `File` de la PC no se copian: el navegador los lee del disco.
  - **No incluido:** bajar por partes o transmitir sin `Blob` (fuera de alcance por la HU). Si la medición muestra
    que `blob_storage` no se libera o que el navegador falla con 1-2 GB, se abre una HU aparte, como dice D10.
- **DT16. Buffer de copia de 1 MiB con `ArrayPool`** (recomendada). Menos syscalls sobre el bind mount de Docker
  Desktop, y la RAM sigue sin depender del tamaño del archivo (1 MiB por subida concurrente).
- **DT17. Se elimina `IStorageService.SaveFileAsync`** (recomendada). Es el camino no atómico. Si apareciera otro
  uso, se migra a `StageUploadAsync` + `Commit`.

**Observación para el usuario (no se cambia en esta HU):** un archivo de evidencia cuyo nombre empiece con
`evidencia_` y termine en `.zip`, o `informe_pericial_`/`informe_forense_` con `.docx`/`.pdf`, se filtra en silencio
del ZIP (`ReportService.IsGeneratedArtifact`). Hoy no es alcanzable, porque Tatana y el front renombran todo, pero
conviene saberlo si algún día se conservan los nombres originales.

## 9. Fuera de alcance (recordatorio técnico)

- Subidas reanudables, en paralelo, o en streaming sin `Blob`.
- Comparar el hash de Tatana con el del backend (D9). El `hash` de la respuesta no se persiste.
- Cambios en Tatana, `agent-ui/` y `deploy/windows/`.
- Límites en otros endpoints.
- Deshabilitar "Enviar" mientras hay una grabación en curso (comportamiento actual sin cambios). DT12 cubre que el
  archivo, si llega por WebSocket durante el envío, igual se suba.

## 10. Checklist atómico — `implementer-backend` (`server/`)

> Regla dura de datos (`AGENTS.md`): no se borra ni modifica evidencia ni documentos ajenos. Los tests trabajan en
> `Path.GetTempPath()/factum-upload-<guid>` y borran solo eso. La medición de §12 usa su propio proyecto de compose y
> su propia carpeta. Nada contra `server/src/Factum.Backend/dev-data/` ni contra un `DataDirectory` del usuario.

**Commit 1 — seguridad del nombre (primero):**

- [ ] B1. Crear `Services/Cases/EvidenceUpload.cs` con `IsValidUploadName` (§5.2) y `UploadErrorCodes`.
- [ ] B2. En `CaseService.UploadFileAsync`, rechazar un nombre inválido con `Result.Invalid` antes de cualquier
      I/O. En este commit todavía sin `Details`: solo `{ error }`.
- [ ] B3. Tests `UploadFilenameTests.cs` (§11 T1).
- [ ] B4. `dotnet build` + `dotnet test`.

**Resto:**

- [ ] B5. `StorageOptions`: `MaxUploadBytes` y `MinFreeBytes` con los defaults de §3.
- [ ] B6. `Program.cs`: validar los dos valores y sumarlos a `configErrors` (DT11).
- [ ] B7. `IDiskSpaceProbe` + `DriveInfoDiskSpaceProbe`, registrados como singleton.
- [ ] B8. `Infrastructure/UploadStaging.cs`: `StagedUpload`, `UploadIncompleteException` e
      `InsufficientStorageException` (con `RequiredBytes`, `AvailableBytes?`, `ReceivedBytes`/`ExpectedBytes` según
      corresponda).
- [ ] B9. `StorageService`:
  - constructor público e interno (§5.3);
  - `_uploadTmp`;
  - `GetAvailableFreeBytes`, `StageUploadAsync` (bucle de 1 MiB, SHA-256 incremental, lectura y escritura
    separadas, clasificación de disco lleno, verificación de longitud, `fsync`, `finally` que borra);
  - `CleanupOrphanUploads`.
- [ ] B10. Quitar `SaveFileAsync` de la interfaz y de la implementación. Grep de usos = 0.
- [ ] B11. `EvidenceUpload.Evaluate` + `UploadRejection` + `FormatBytes` (§5.2).
- [ ] B12. `Result.cs`: los 3 `ErrorKind` nuevos al final del enum, `Details`, la sobrecarga de `Fail` y
      `Cast` que propaga.
- [ ] B13. `ResultHttpExtensions.ErrorResult`: rama con `Details` (diccionario) y mapeo 413/507/500. Sin
      `Details`, idéntico a hoy.
- [ ] B14. `DTOs/CaseDtos.cs`: `UploadCheckResponse(long MaxUploadBytes)`.
- [ ] B15. `CaseService`: inyectar `IOptions<StorageOptions>`; `CheckUploadAsync` (interfaz + implementación);
      helper compartido de chequeos; `UploadFileAsync` con la firma nueva y el flujo de §5.5 (catch de todo,
      revalidación de editable y commit).
- [ ] B16. `CasesController`: inyectar `IOptions<StorageOptions>`; `IHttpMaxRequestBodySizeFeature` en
      `UploadFile` antes de llamar al servicio; pasar `Request.ContentLength`; acción `UploadCheck` con `size`
      parseado a mano y `no-store`; `ProducesResponseType`.
- [ ] B17. `Program.cs`: después de `IReportSettings`, `CleanupOrphanUploads()` + la línea de log de §5.1.
- [ ] B18. Tests `UploadStorageTests.cs` y `EvidenceUploadTests.cs` (§11 T2-T4).
- [ ] B19. README: las 2 filas de config y la nota de `.upload-tmp/` (§5.8).
- [ ] B20. `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` y
      `dotnet test server/tests/Factum.Backend.Tests` en verde, incluidos los tests existentes.
- [ ] B21. Medición y verificación de §12.1 (Mac). Resultados en `progress/impl_backend_subida-archivos-grandes.md`.

## 11. Tests xUnit (`server/tests/Factum.Backend.Tests/`)

Cada clase trabaja en su propia carpeta temporal, como `EvidenceZipTests`: `IDisposable`, `Directory.Delete` del
root propio. No usa Mongo.

**T1 `UploadFilenameTests.cs`** — `[Theory]` sobre `EvidenceUpload.IsValidUploadName`:

- **Válidos:** `grabacion_20261002_101010.mp4`, `device_pull_20261002_101010.mp4`, `adjunto_20261002_101010_1.mov`,
  `device_pull_canción ñ.txt` y `a.b.c`.
- **Inválidos:**
  - vacíos y puntos: `null`, `""`, `"."`, `".."`;
  - rutas: `"../x.mp4"`, `"..\\x.mp4"`, `"a/b.mp4"`, `"a\\b.mp4"`, `"/etc/passwd"`, `"C:\\x.mp4"`;
  - caracteres de control: `"x\0y"`, `"x\ny"`, `"x\u007Fy"`;
  - caracteres de NTFS: cada uno de `< > : " | ? *` dentro de un nombre;
  - final inválido: `"x.mp4."`, `"x.mp4 "`;
  - reservados de Windows: `"CON"`, `"con.txt"`, `"NUL"`, `"com1.mp4"`, `"LPT9"`;
  - largo: 256 caracteres.

**T2 `EvidenceUploadTests.cs`:**

- **`Evaluate`:**
  - `size == max` → ok;
  - `max + 1` → TooLarge con los números;
  - `available null` → ok;
  - `available == size + minFree` → ok;
  - `available == size + minFree - 1` → Insufficient con `required` y `available`;
  - `size 0` → ok.
- **`FormatBytes`:** los ejemplos de §5.2 y `1536 → "1,5 KB"`.

**T3 `UploadStorageTests.cs`.** `StorageService` con el constructor interno, root temporal, un
`FakeDiskSpaceProbe { long? Free }` y, cuando hace falta, un `openTempForWrite` falso.

1. **Subida completa.** 5 MiB aleatorios, `expectedLength` correcto → `Commit()`. Se verifica:
   - el archivo final existe con los mismos bytes;
   - `staged.Hash` es igual a `SHA256.HashData(input)`;
   - `.upload-tmp/` queda vacío;
   - `ListFilesAsync` devuelve exactamente ese archivo.
2. **El temporal nunca se lista.** El contenido es un stream "con compuerta" que entrega la mitad y espera un
   `TaskCompletionSource`. Mientras espera:
   - `ListFilesAsync(caseId)` está vacío y el nombre final no existe;
   - hay exactamente 1 archivo en `.upload-tmp/`, y está bajo `root/.upload-tmp` (mismo volumen).

   Después se libera la compuerta y se hace commit.
3. **Cuerpo corto** (`expectedLength` = len + 10) → `UploadIncompleteException` (`Received == len`). No existe el
   final y `.upload-tmp/` queda vacío.
4. **Cuerpo largo** (len > `expectedLength`) → `UploadIncompleteException`, sin final y sin temporal.
5. **Corte a mitad.** El stream de origen lanza `IOException("connection reset")` después de 1 MiB →
   `UploadIncompleteException` con inner. Sin final y sin temporal.
6. **Cancelación.** Un `CancellationTokenSource` se cancela después del primer bloque →
   `OperationCanceledException`. Sin final y sin temporal.
7. **Disco lleno a mitad.** `openTempForWrite` devuelve un stream que, después de 2 MiB, lanza
   `new IOException("No space left on device", 28)` → `InsufficientStorageException`. Sin final y sin temporal.
   Variante con `HResult` genérico y `FakeDiskSpaceProbe.Free = 0` después de la falla → también
   `InsufficientStorageException`. Variante con `HResult` genérico y espacio de sobra → la `IOException` original
   (que el servicio convierte en `storage_error`).
8. **Reemplazo atómico:**
   - (a) existe `video.mp4` con el contenido A;
   - (b) se sube B con corte a mitad → `video.mp4` sigue siendo A (hash igual);
   - (c) se sube B con compuerta: a mitad de camino `video.mp4` sigue siendo A;
   - (d) después de `Commit()`, `video.mp4` es B y no quedan temporales.
9. **`Dispose` sin commit** borra el temporal y no crea el final.
10. **Huérfanos:**
    - se crean 3 archivos `.part` en `.upload-tmp/`, más `cases/<id>/evidencia_real.mp4` y un archivo en la raíz;
    - `CleanupOrphanUploads()` devuelve 3;
    - `.upload-tmp/` queda vacío, y `evidencia_real.mp4` y el archivo de la raíz quedan **intactos** (mismo
      hash);
    - sobre un root sin `.upload-tmp/` → devuelve 0 y crea la carpeta.
11. **Nombre que escapa.** `StageUploadAsync` con `"../x"` → `ArgumentException` (defensa en profundidad) y nada
    creado fuera de `cases/<id>/`.

**T4 `DriveInfoDiskSpaceProbe`:** sobre `Path.GetTempPath()` devuelve un valor no nulo y > 0. Es un chequeo de
cordura en macOS/Linux.

No se agregan tests de controlador con `WebApplicationFactory`: el proyecto no tiene esa dependencia y `Program`
necesita Mongo. El camino HTTP completo (CORS, 413/507 reales, `Expect: 100-continue`, corte) se verifica en §12.1.

## 12. Verificación

### 12.0 Comandos antes de `done`

- **Backend:**
  - `dotnet build server/src/Factum.Backend/Factum.Backend.csproj`;
  - `dotnet test server/tests/Factum.Backend.Tests` (todo verde: los nuevos y los existentes);
  - la medición de §12.1.
- **Frontend:**
  - `cd client && npx tsc --noEmit` y `npm run lint`, si el script existe;
  - prueba en el navegador (§12.2) contra el backend de §12.1 o un `dotnet run` local con `DataDirectory` de
    scratch.

### 12.1 Medición en la Mac (implementer-backend; D12)

**Montaje.** Se usa el mismo que `Refactorizaciones/instalacion-poca-ram.md` §10.3 pasos 1-4:

- proyecto `factum-verif`, carpeta de scratch propia y overlay `docker-compose.poca-ram.yml` (backend 768m);
- puerto con `!override ["127.0.0.1:18080:8080"]`, **no** `!reset` (ver `progress/impl_backend_instalacion-poca-ram.md`);
- antes de empezar, foto de `docker ps -a`, `docker volume ls` y `docker images`; al final se compara.

Caso de prueba creado por la prueba (login `dev`, `POST /api/cases`). Archivos sintéticos con `head -c` de
`/dev/urandom` en el scratch, nunca en `dev-data`.

| # | Prueba | Esperado |
|---|---|---|
| V1 | Log de arranque | Línea "Subidas: tope 4 GB, margen libre 1 GB, espacio libre …". `Free` ≈ el `df` del host del disco donde está el scratch (virtiofs). Anotar los dos valores. |
| V2 | Subir 150 MB, 1 GB y 3 GB con `curl -T` | 200. `hash` = `shasum -a 256` del origen = `shasum` de `<scratch>/evidencia/cases/<id>/<nombre>`. `.upload-tmp/` vacío. `docker stats` + `memory.stat` `anon` del backend cada 1 s: el pico de `anon` no crece con el tamaño (criterio: < reposo + 64 MiB en las 3 corridas). Anotar el tiempo de cada subida y el `file`/page cache. Sin OOM. |
| V3 | Tope | `truncate -s 4294967297 big.bin` (sparse, no ocupa disco). `GET upload-check?size=4294967297` → 413 JSON con `code`, `size` y `max_upload_bytes`, y `Access-Control-Allow-Origin` presente (`curl -H "Origin: http://localhost:3000" -i`). `curl -T big.bin` → 413 JSON en < 2 s, sin subir el cuerpo (Expect 100-continue). Nada en `cases/<id>/` ni en `.upload-tmp/`. |
| V4 | Nombre inválido | `filename=../x.bin`, `filename=..%2Fx.bin`, `a:b.bin` y `CON` → 400 `invalid_filename`. **Nada** fuera de `cases/<id>/`: `find <scratch>/evidencia -newer <marca>` muestra solo lo esperado. |
| V5 | Caso no editable | Generar el caso de prueba; subir → 409 `case_not_editable`; `upload-check` → 409. |
| V6 | Espacio insuficiente (prechequeo) | Segundo arranque del backend con `/data` en un `tmpfs` de 200m (override de scratch `volumes: !override [{type: tmpfs, target: /data, tmpfs: {size: 209715200}}]`) y `MinFreeBytes` default → `upload-check` de 50 MB → 507 con `required_bytes` ≈ 1,05 GB y `available_bytes` ≈ 200 MB. El POST también da 507 sin escribir. |
| V7 | Disco lleno a mitad | Mismo `tmpfs` de 200m con `Storage__MinFreeBytes=0` → `curl -T` de 300 MB → 507 `insufficient_storage`. `docker exec … ls -la /data/cases/<id> /data/.upload-tmp` vacíos. `tmpfs` cuenta contra la memoria del contenedor: en V6/V7 se puede quitar `mem_limit` (anotarlo). |
| V8 | Corte del cliente | `curl -T` 1 GB con `--limit-rate 20M`, `kill -9` del `curl` a los ~5 s. Unos segundos después: `cases/<id>/` sin ese archivo y `.upload-tmp/` vacío. Log `Information` de subida cortada. Reintento completo → 200 con el hash correcto. |
| V9 | Reinicio a mitad | Igual que V8, pero `docker restart factum-verif-backend-1` a mitad. Antes de reiniciar, `.upload-tmp/` tiene 1 `.part` (anotar). Después del arranque: `.upload-tmp/` vacío, el log dice "temporales huérfanos borrados: 1" y nada en `cases/<id>/`. |
| V10 | Reemplazo atómico | Subir `rep.bin` (A). Subir `rep.bin` (B) con `--limit-rate`: a mitad, `shasum` del final = A. Cortar → sigue A. Subir B completo → B. |
| V11 | Generación | En un caso nuevo con un video de 1 GB subido por la API + capturas mínimas: generar en el perfil poca RAM → 200. El ZIP contiene el video con su hash. Ningún `.part` dentro del ZIP (`python3 -m zipfile -l`, o listar con la contraseña). |
| V12 | Navegador, respuesta temprana | En Chrome (Mac), contra el backend con `Storage__MaxUploadBytes=104857600`, desde la consola: XHR POST directo (sin prechequeo) de un `Blob` de 200 MB. Anotar si llega el 413 JSON o un error de red. Es informativo: el front usa el prechequeo igual. |
| V13 | Limpieza | `down -v` del proyecto `factum-verif`, `rm` de las imágenes `-verif` y del scratch propio. Las fotos de `docker ps -a`/`volume ls`/`images` quedan idénticas a las de antes. |

### 12.2 Prueba en el navegador (implementer-frontend, con el backend de §12.1 o uno local de scratch)

- **F1.** Adjuntar desde la PC un archivo de 1-2 GB y 3-4 archivos chicos. Se ve el panel: "Enviando i de n", el
  nombre, "x / y MB", la barra que avanza y la fase. Los tiles pasan pendiente → subiendo → subido. Los controles
  de captura, eliminar, marcas y explorador están deshabilitados. Después se pasa al paso 4.
- **F2.** "Cancelar envío" a mitad del archivo grande:
  - aviso informativo (no error);
  - el archivo vuelve a pendiente y los anteriores quedan subidos;
  - en `cases/<id>/` no está el grande y `.upload-tmp/` queda vacío;
  - "Enviar evidencia y continuar" sigue desde ese archivo.
- **F3.** Con `Storage__MaxUploadBytes=104857600`, adjuntar 150 MB → mensaje "El archivo … pesa 150 MB y el máximo
  permitido es 100 MB…". No aparece "Failed to fetch" ni "HTTP 413".
- **F4.** Backend apagado a mitad (`docker stop`) → "Se cortó el envío de …".
- **F5.** Con Tatana en marcha (modo real con un Android, o el modo disponible en la Mac), una grabación de pantalla
  de varios minutos → fase "Preparando desde el agente…" con progreso, después "Subiendo…". `shasum` en Tatana
  (`agent-data/`) = `shasum` en `cases/<id>/`. Si no hay dispositivo, dejarlo anotado como pendiente manual del
  usuario (M1).
- **F6 (D10).** Durante F1 y F5, en Chrome: el Administrador de tareas del navegador (memoria de la pestaña y del
  proceso del navegador) y el tamaño de
  `~/Library/Application Support/Google/Chrome/Default/blob_storage` antes, durante y 30 s después. Esperado: la
  RAM no crece en proporción al archivo y `blob_storage` vuelve cerca del valor inicial después del envío. Anotar
  los números.
- **F7.** Teclado y lector: Tab llega a "Cancelar envío". La barra expone `aria-valuenow`/`aria-valuetext`. La
  región `aria-live` anuncia solo los cambios de archivo y de fase.

### 12.3 Prueba manual del usuario (PC de 4 GB, Windows, perfil poca RAM)

- **M1.** Una grabación de pantalla Android de 10 min (~300 MB) y, si hay, un video de 1-2 GB del explorador del
  celular → "Enviar evidencia y continuar":
  - progreso visible en las dos fases;
  - `Get-FileHash` en la carpeta de Tatana = `Get-FileHash` en `C:\Factum\evidencia\cases\<id>\`.
- **M2.** Mientras dura M1, en el Administrador de tareas de Windows: la memoria de Edge/Chrome y de "Vmmem"/WSL.
  Tamaño de `%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\blob_storage` (o el de Chrome) durante y después.
  Esperado: sin cierre del navegador ni de la pestaña, y `blob_storage` se libera después.
- **M3.** Cancelar a mitad y cerrar la pestaña a mitad de otro envío → en `C:\Factum\evidencia\cases\<id>\` no hay
  ese archivo. `C:\Factum\evidencia\.upload-tmp\` queda vacío; si quedó algo por un corte brusco, se vacía al
  reiniciar Factum.
- **M4.** La línea de log "Subidas: … espacio libre …" (`docker logs` del backend) coincide aproximadamente con el
  espacio libre de `C:` en el Explorador. Confirma que `DriveInfo` ve el disco real a través del bind mount. Si no
  coincide, avisar: el prechequeo de espacio quedaría inexacto y solo actuaría la red de disco lleno.
- **M5.** Generar el informe del caso de M1: el ZIP incluye la grabación y el video con sus hashes. No aparece nada
  de `.upload-tmp`.

### 12.4 Lo que el reviewer chequea además de `CHECKPOINTS.md`

- El fix de `filename` está en su propio commit, el primero, con tests.
- Ningún camino de `UploadFileAsync` deja escapar una excepción. Hay un `catch` general con `storage_error`.
- `ErrorResult` sin `Details` es byte a byte igual al anterior. Un diff mínimo lo muestra.
- `SaveFileAsync` no existe más y `File.Create` no aparece en el camino de subida.
- `CleanupOrphanUploads` solo enumera `.upload-tmp/` (sin recursión).
- El front no muestra "Failed to fetch"/"HTTP 4xx/5xx" crudos. El `Blob` de Tatana no se guarda en estado ni en
  refs.
- `progress/impl_frontend_subida-archivos-grandes.md` deja constancia de los skills obligatorios
  (`ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` como criterio sin 3D y `web-design-guidelines`).

## 13. Checklist atómico — `implementer-frontend` (`client/` solamente)

> Skills obligatorios: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (criterio, sin
> 3D) y `web-design-guidelines` (autochequeo final). Además, `mblode-agent-skills-ui-animation` para la barra y la
> transición del panel. Leer `client/AGENTS.md` antes de tocar código. No tocar `agent-ui/`. Esta parte depende del
> contrato de §7. Si el backend todavía no está, se puede avanzar con los tipos y probar al final.

- [ ] F1. `types/index.ts`: los tipos de §6.1.
- [ ] F2. `lib/format.ts`: `formatBytes` y `formatBytesPair` (§6.3).
- [ ] F3. `lib/api.ts`: `UploadError`, `checkUpload`, y `uploadFile` con XHR, progreso, abort y tipo de retorno
      `UploadedFileInfo` (§6.2). Ajustar los llamadores (solo `useFileManager`).
- [ ] F4. `lib/agent.ts`: `downloadFile` con `opts` (XHR blob, progreso y abort), compatible con la firma vieja
      (§6.4).
- [ ] F5. `lib/upload-messages.ts`: `uploadErrorMessage` con la tabla de §6.5.
- [ ] F6. `hooks/useFileManager.ts`:
  - estado nuevo y `filesRef`;
  - el bucle de §6.6: prechequeo antes de bajar, fases, `Blob` local, relectura del ref y roles del ref;
  - `cancelUpload`, `dismissUploadNotice`, la guarda en `removeFile` y `clearFiles`;
  - los exports.
- [ ] F7. `components/capture/UploadProgressPanel.tsx` (§6.7): barra accesible, fases, `aria-live` educado y
      "Cancelar envío".
- [ ] F8. `components/capture/EvidenceTray.tsx`: `uploadState` en `EvidenceTrayTile` y `AttachmentChip` (ícono +
      `aria-label`).
- [ ] F9. `components/CaptureStep.tsx`: props nuevas; deshabilitar todo lo de §6.7 mientras `uploading`; panel
      encima de la botonera; `FxBanner` de `uploadNotice`; derivar `uploadState` por ítem.
- [ ] F10. `app/dashboard/page.tsx`: cablear las props desde `useFileManager`.
- [ ] F11. `npx tsc --noEmit` (y `npm run lint` si existe) en verde.
- [ ] F12. Prueba en el navegador F1-F7 de §12.2. Resultados en `progress/impl_frontend_subida-archivos-grandes.md`,
      con los números de F6 (D10).
