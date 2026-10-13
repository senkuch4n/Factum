# SDD: Guardar en el caso grabaciones de iPhone largas sin esperar ni trabarse

**Slug:** `ios-grabacion-larga` · **Issue:** #50 · **HU:** `docs/hu-ios-grabacion-larga.md`
(validada el 2026-10-12: Dudas 1 a 9 resueltas con la **opción A**, la recomendada).
**Base del análisis:** `feat/ios-grabacion-larga` = `develop` `5320ca6`. QA de origen: "video largo grabado por
Tatana → caso" del 2026-10-12, hallazgos H1 y H2. Allí se midió en macOS arm64: blend ≈ 1,2x la duración y MCI
≈ 14 min por minuto de video, con ≈ 2 GB de RAM.
**Modo:** autónomo. Las decisiones técnicas que en otro caso irían al usuario se tomaron con el criterio más
conservador y se marcan como **decisión del arquitecto** (§8.2).

---

## 1. Resumen funcional

Una grabación de iPhone, de cualquier modo y duración, se guarda en la carpeta del caso apenas se detiene, igual
que una captura. El cliente deja de esperar variantes para guardarla (`saveOne`) y para avanzar ("Guardar evidencia
en esta PC y continuar"). Las variantes son solo una ayuda visual y no son evidencia. Tatana las genera en
segundo plano, en una cola de a una. Ahora solo genera "Suavizado" (blend), más "RIFE" si está instalado y el video
dura menos que su umbral; el MCI desaparece. Cada variante tiene un plazo proporcional a la duración. Al vencer,
Tatana mata ffmpeg y borra el parcial. Cada variante **siempre** termina con un evento de estado (`ready`, `failed`,
`timeout`, `skipped` o `cancelled`) e informa su avance en porcentaje mientras corre. Para que en Windows mover el
original al caso nunca dé `file_busy`, el post-proceso trabaja sobre una **copia propia** en
`<DataDirectory>/.postprocess/`. ffmpeg nunca abre el original. El post-proceso deja de bloquear "Reiniciar y
actualizar": al entrar en mantenimiento, Tatana lo cancela, y `agent-ui` avisa que se va a cancelar. Si el perito
quita la grabación de la bandeja, Tatana cancela su post-proceso y borra parciales y variantes. Al reconectar el
WebSocket, el cliente le pregunta a Tatana el estado de las grabaciones de la bandeja.

## 2. Toca

| Lado | ¿Toca? | Qué |
|---|---|---|
| **backend (API)** `server/src/Factum.Backend` | **no** | `registerEvidence` recibe filename/size/sha256 y no depende de la duración. Sin cambios de DTOs ni de Mongo. |
| **backend (Tatana)** `server/src/Factum.Agent` | **sí** | `VideoPostprocessService` nuevo (cola, copia de trabajo, umbral, timeout, progreso, cancelación). `ProcessRunner` con callback de stdout. `IosService.StopRecordingAsync` devuelve las variantes planeadas. `recording_stopped` y la respuesta de `record/stop` suman `variants_expected`/`variants`. Evento WS nuevo `video_variant_status`. `GET /video-postprocess/status`. `/agent/state.background_operations`. Cancelación al entrar en mantenimiento, al apagar y al borrar. Config `Agent:VideoPostprocess`. Fuente de grabación para modo mock. Tests xUnit. |
| **client** `client/` | **sí** | `lib/agent.ts` (tipos y método de estado), `useFileManager.ts` (fuera el bloqueo por variante; estado visual por variante), `useRecording.ts`, `useAgentConnection.ts` (callback al conectar), `dashboard/page.tsx` (eventos), `CaptureStep.tsx` y `VideoCard.tsx` (indicador con progreso). |
| **agent-ui** `agent-ui/` | **sí** (chico) | Lee `background_operations` de `/agent/state`. Si hay `video_postprocess`, "Reiniciar y actualizar" sigue habilitado y avisa "se cancelará la versión suavizada…" (`state.ts`, `update-manager.ts`, `app-info.ts`, `agent-api.ts`, `UpdateStatus.tsx`, `index.ts` de la bandeja, `env.d.ts`). |
| **Mongo** | no | Nada. |

## 3. Modelo de datos

No hay colecciones, documentos ni DTOs nuevos en `Factum.Backend`. La evidencia sigue siendo el original
`grabacion_ios_<ts>.mp4`, con su SHA-256 calculado por `CaseEvidenceStore.ImportAsync`, sin cambios.

**Disco de Tatana (`DataDirectory`):**

| Ruta | Quién la escribe | Ciclo de vida |
|---|---|---|
| `<DataDirectory>/grabacion_ios_<ts>.mp4` | grabación (como hoy) | El cliente la mueve a `cases/<id>/` apenas existe. |
| `<DataDirectory>/.postprocess/<stem>/source.mp4` | `VideoPostprocessService` (copia de trabajo del original) | Se borra al terminar el trabajo, sea cual sea el resultado. |
| `<DataDirectory>/.postprocess/<stem>/<stem>_blend.mp4` (y los PNG de RIFE) | ffmpeg / rife (parciales) | Al salir bien se **mueve** a la raíz; si no, se borra con la carpeta del trabajo. |
| `<DataDirectory>/<stem>_blend.mp4`, `<stem>_rife.mp4` | `VideoPostprocessService` (solo variantes completas) | Previsualización (`/files/…`), como hoy. No van al caso, al expediente ni al ZIP (Duda 5 A). |
| `<DataDirectory>/.postprocess/` completa | — | Se borra entera al arrancar Tatana. Ahí solo escribe Tatana: son trabajos de una ejecución anterior. |

`FileStorageService.ListFiles` usa `Directory.EnumerateFiles(_dir)` (primer nivel), así que `.postprocess/` no
aparece en `GET /files`. La carpeta del caso (`cases/<id>/`) **no** recibe nunca un archivo del post-proceso.

**Estado en memoria (Tatana):** un registro por original (`original_filename`) con el estado de cada variante. Los
trabajos terminados se conservan **6 h** o hasta 200 entradas (descarta los más viejos), para responder
`GET /video-postprocess/status` después de una reconexión. No se persiste: tras reiniciar Tatana, el endpoint
responde por lo que hay en disco (§4.4).

## 4. Endpoints y mensajes WebSocket de Tatana

**Serialización verificada:**
- **HTTP** (`Program.cs:98-103`): `PropertyNamingPolicy = SnakeCaseLower`, enums como string snake_case_lower y
  `DefaultIgnoreCondition = WhenWritingNull`. Los `null` **se omiten**.
- **WebSocket** (`AgentWebSocketHub.JsonOpts`, `AgentWebSocketHub.cs:13-16`): `SnakeCaseLower` **sin**
  `WhenWritingNull`. Los `null` **viajan como `null`**. No se cambia el hub (afectaría a otros eventos). El cliente
  trata `null` y "ausente" igual.
- Los payloads anónimos existentes ya usan nombres snake (`original_filename = …`). En lo nuevo se usan records con
  nombres PascalCase y la política los convierte. Abajo figura el nombre **tal como viaja**.

### 4.1 `POST /devices/{serial}/record/stop[?platform=ios]` (cambia la respuesta)

Respuesta 200:

```json
{ "filename": "grabacion_ios_20261012_101500_123.mp4", "url": "/files/grabacion_ios_20261012_101500_123.mp4",
  "variants_expected": true, "variants": ["Suavizado"] }
```

- `variants_expected` (bool, **siempre** presente): `true` si Tatana va a emitir al menos un
  `video_variant_status` para este archivo.
- `variants` (string[], **siempre** presente, puede ser `[]`): etiquetas planeadas, en orden. Valores posibles:
  `"Suavizado"` y `"RIFE"`. Solo se incluye `"RIFE"` si `rife-ncnn-vulkan` está instalado.
- Android, AirPlay, `on_device`, mock sin fuente, post-proceso deshabilitado o sin ffmpeg dan
  `variants_expected: false, variants: []`.

### 4.2 Evento WS `recording_stopped` (cambia)

```json
{ "type": "recording_stopped", "timestamp": "…",
  "data": { "filename": "…mp4", "url": "/files/…mp4", "platform": "ios",
            "variants_expected": true, "variants": ["Suavizado"] } }
```

Mismos dos campos nuevos y con la misma semántica que en §4.1. `platform` no cambia.

### 4.3 Evento WS `video_variant_status` (nuevo)

Se emite en cada cambio de estado de una variante y, mientras corre, con el avance cada
`ProgressIntervalSeconds` como mínimo, y solo si el porcentaje entero cambió.

```json
{ "type": "video_variant_status", "timestamp": "…",
  "data": {
    "original_filename": "grabacion_ios_20261012_101500_123.mp4",
    "label": "Suavizado",
    "state": "running",
    "progress": 42,
    "filename": null,
    "url": null,
    "reason": null,
    "message": null,
    "updated_at": "2026-10-12T13:20:05.123+00:00"
  } }
```

| Campo | Tipo | Cuándo |
|---|---|---|
| `original_filename` | string | siempre |
| `label` | `"Suavizado"` \| `"RIFE"` | siempre |
| `state` | `"pending"` \| `"running"` \| `"ready"` \| `"failed"` \| `"timeout"` \| `"skipped"` \| `"cancelled"` | siempre. Terminales: `ready`, `failed`, `timeout`, `skipped`, `cancelled`. |
| `progress` | int 0–100 \| null | En `running`, si se conoce la duración (blend). En RIFE es `null` (indeterminado). En `ready` vale `100`. En los demás, `null`. Nunca llega a 100 antes de `ready` (tope 99). |
| `filename` | string \| null | solo en `ready`: nombre de la variante en la raíz (`<stem>_blend.mp4`). |
| `url` | string \| null | solo en `ready`: `/files/<filename>`. |
| `reason` | string \| null | En `failed`, `timeout`, `skipped` y `cancelled` (tabla de abajo). Si no, `null`. |
| `message` | string \| null | Texto corto para el log (p. ej., la última línea de stderr, ≤ 200 caracteres). **El cliente no lo muestra.** |
| `updated_at` | ISO-8601 con offset | siempre |

`reason`:

| `reason` | `state` | Significado |
|---|---|---|
| `ffmpeg_error` | `failed` | ffmpeg/rife salió con código ≠ 0. |
| `timeout` | `timeout` | Venció el plazo (§5.4). Se mató el árbol del proceso y se borró el parcial. |
| `too_long` | `skipped` | La duración supera el umbral de esa variante (`BlendMaxSourceSeconds` / `RifeMaxSourceSeconds`). |
| `insufficient_disk` | `skipped` | No hay espacio para la copia de trabajo: el tamaño del original más `Agent:MinFreeBytes`. |
| `source_missing` | `failed` | No se pudo abrir o copiar el original. |
| `removed` | `cancelled` | El perito quitó la grabación (§4.6). |
| `agent_update` | `cancelled` | Tatana entró en mantenimiento para actualizarse (Duda 7 A). |
| `agent_stopping` | `cancelled` | Tatana se está cerrando. |
| `internal_error` | `failed` | Excepción no prevista. La garantía del `finally` (§5.3) emite este estado. |

**Garantía:** por cada etiqueta de `variants` de §4.1/§4.2 se emite **exactamente un** estado terminal. No se
emite `pending` por WS: `recording_stopped.variants` lo implica. `pending` solo aparece en §4.4.

**Compatibilidad:** al quedar lista una variante, Tatana **sigue emitiendo** además el evento viejo
`video_variant_ready { original_filename, label, filename, url }`, así un cliente desplegado antes de esta HU
muestra la pestaña. El cliente nuevo puede recibir los dos y deduplica por `filename`.

### 4.4 `GET /video-postprocess/status?filename=<a>&filename=<b>…` (nuevo)

- Query: `filename` repetido, entre 1 y 50. Cada uno tiene que cumplir `Path.GetFileName(x) == x` (si no, 400
  `invalid_filename`, `AgentErrorCodes.InvalidFilename`). Si sobran o faltan, 400 `invalid_filename`.
- GET: el middleware de `OperationTracker` no lo cuenta ni lo bloquea en mantenimiento.
- Respuesta 200 (HTTP, omite `null`):

```json
{ "items": [
  { "original_filename": "grabacion_ios_…_123.mp4", "known": true,
    "variants": [ { "original_filename": "…", "label": "Suavizado", "state": "running", "progress": 57,
                    "updated_at": "…" } ] },
  { "original_filename": "grabacion_ios_…_999.mp4", "known": false,
    "variants": [ { "original_filename": "…", "label": "Suavizado", "state": "ready",
                    "filename": "grabacion_ios_…_999_blend.mp4", "url": "/files/grabacion_ios_…_999_blend.mp4",
                    "progress": 100, "updated_at": "…" } ] }
] }
```

- `known: true`: el original está en el registro en memoria, y `variants` trae el último estado de **cada**
  etiqueta planeada (puede incluir `pending`).
- `known: false`: Tatana no tiene registro (se reinició o nunca lo procesó). `variants` lista solo las variantes
  **completas que existen en la raíz**: `<stem>_blend.mp4` → `"Suavizado"`, `<stem>_rife.mp4` → `"RIFE"` y
  `<stem>_mci.mp4` (legado) → `"MCI"`. `updated_at` sale del `LastWriteTimeUtc`. Puede venir `[]`.
- Un item por cada `filename` pedido, en el mismo orden.

### 4.5 `GET /agent/state` (cambia, campo nuevo)

```json
{ "version": "…", "mode": "installed", "busy": false, "maintenance": false,
  "operations": [],
  "background_operations": [ { "kind": "video_postprocess", "since": "2026-10-12T13:10:00+00:00" } ],
  "local_config": { … } }
```

- `operations` **deja de incluir** `video_postprocess`: `IosService.ActiveOperations` ya no lo reporta. Por eso el
  post-proceso no pone `busy: true` ni hace que `POST /agent/maintenance` responda 409.
- `background_operations` (nuevo, **siempre** presente, puede ser `[]`): mismo shape que `operations`
  (`AgentOperationDto`: `kind`, `since`, `detail` omitido). Hoy el único `kind` es `video_postprocess`, presente
  mientras haya un trabajo en cola o corriendo. `since` es el inicio del trabajo más viejo. Son operaciones que
  **no bloquean** y que se cancelan al entrar en mantenimiento.
- `POST /agent/maintenance` → 200: **después** de entrar en mantenimiento, cancela todos los trabajos con
  `reason: "agent_update"` (§5.6). No espera a que terminen de limpiar.

### 4.6 Borrados que cancelan (Duda 8 A)

`DELETE /files/{filename}` (`FilesController`) y `DELETE /cases/{caseId}/files/{filename}`
(`CaseEvidenceController`) llaman a `IVideoPostprocessService.Discard(filename)` **después** del intento de
borrado, haya existido el archivo o no:
- Trabajo en cola o corriendo → se cancela con `reason: "removed"`. Al limpiar, borra la carpeta de trabajo y las
  variantes de ese original que ya estén en la raíz.
- Sin trabajo activo → si el nombre cumple `^grabacion_ios_.+\.mp4$`, borra de la raíz
  `<stem>_blend.mp4`, `<stem>_rife.mp4` y `<stem>_mci.mp4` (legado), si existen. Nada más.
- `Discard` nunca tira: un error al borrar se loguea como warning. No cambia la respuesta HTTP de ninguno de los
  dos endpoints.
- El `zip/commit` con `delete_files` **no** llama a `Discard`: el original está en el ZIP y la vista previa puede
  seguir.

### 4.7 `GET /health` (cambia)

`capabilities` suma `"video_postprocess_v1"`, que indica §4.1–§4.5. El cliente nuevo no depende de la capacidad
para guardar (§6). La usa para decidir si consulta §4.4.

## 5. Diseño técnico: Tatana (`server/src/Factum.Agent`)

### 5.1 Config (`Models/AgentOptions` + `appsettings.json`)

`AgentOptions` suma:

```csharp
public VideoPostprocessOptions VideoPostprocess { get; set; } = new();
/// Solo con Mock = true: MP4 que se copia como "grabación" iOS al detener (para probar sin iPhone).
public string? MockIosRecordingSource { get; set; }
```

`VideoPostprocessOptions` (nuevo, en `Models/AgentModels.cs`):

| Propiedad (config `Agent:VideoPostprocess:*`) | Tipo | Default | Uso |
|---|---|---|---|
| `Enabled` | bool | `true` | `false` deja todo como AirPlay: `variants: []`. |
| `BlendMaxSourceSeconds` | int | `0` | Umbral de duración del original para "Suavizado". `0` = sin límite (Duda 2 A: blend para cualquier duración). |
| `RifeMaxSourceSeconds` | int | `120` | Umbral para "RIFE". `0` = sin límite. **Decisión del arquitecto** (DA3). |
| `TimeoutFactor` | double | `3.0` | Plazo = `factor × duración + extra`, acotado entre el mínimo y el máximo (Duda 3 A). |
| `TimeoutExtraSeconds` | int | `120` | |
| `TimeoutMinSeconds` | int | `300` | |
| `TimeoutMaxSeconds` | int | `7200` | También es el plazo si no se conoce la duración. |
| `ProgressIntervalSeconds` | int | `3` | Intervalo mínimo entre eventos de progreso. |

Se agrega a `appsettings.json` la sección `"VideoPostprocess"` con estos defaults, como documentación. **No** se
agrega `MockIosRecordingSource`: solo por variable de entorno, CLI o `appsettings.Local.json`.

**Validación** (en `VideoPostprocessOptions.Normalize(ILogger)`, que se llama al construir el servicio): un valor
inválido (negativo, `TimeoutFactor <= 0`, `TimeoutMinSeconds > TimeoutMaxSeconds`, `ProgressIntervalSeconds < 1`)
**no impide arrancar**. Loguea un warning y usa el default de **ese** campo (DA6).

### 5.2 `ProcessRunner` (`Common/ProcessRunner.cs`)

`RunArgumentListAsync` suma un parámetro opcional al final: `Action<string>? onStdoutLine = null`. Si no es null,
se invoca por cada línea de stdout, además de acumularla. El resto no cambia. El post-proceso lo llama
**siempre con `timeout`**, porque ese es el único camino que mata el árbol del proceso (`Kill(entireProcessTree:
true)`) tanto al vencer el plazo como al cancelar `ct`. Al cancelar `ct`, `RunArgumentListAsync` mata y
relanza `OperationCanceledException`, y el servicio lo traduce a `cancelled`. Los `RunAsync(string, string)`
existentes no se tocan.

### 5.3 `VideoPostprocessService` (nuevo, `Services/VideoPostprocessService.cs`)

```csharp
public interface IAgentEventSink { Task BroadcastAsync(AgentEvent evt, CancellationToken ct = default); }
// AgentWebSocketHub implementa IAgentEventSink (sin cambiar su serialización).

public interface IVideoPostprocessService
{
    /// Sincrónico y rápido. Abre el original (FileShare.Read | FileShare.Delete) ANTES de volver y encola el trabajo.
    /// Devuelve las etiquetas planeadas ([] si no habrá variantes).
    IReadOnlyList<string> Start(string originalPath, TimeSpan? durationHint);
    IReadOnlyList<VideoVariantStatusDto> GetStatus(string originalFilename, out bool known);
    void Discard(string originalFilename);           // §4.6
    void CancelAll(string reason);                   // "agent_update" | "agent_stopping"
    DateTimeOffset? BackgroundSince { get; }         // §4.5; null si no hay trabajos
    Task WhenIdleAsync(TimeSpan max);                // para el apagado y los tests
}

public sealed record VideoVariantStatusDto(string OriginalFilename, string Label, string State, int? Progress,
    string? Filename, string? Url, string? Reason, string? Message, DateTimeOffset UpdatedAt);
```

Constantes públicas: `LabelBlend = "Suavizado"`, `LabelRife = "RIFE"`, `WorkDirName = ".postprocess"`, y las
constantes de `state`/`reason` de §4.3 (en `internal static class VideoVariantStates` / `VideoVariantReasons`).

Constructor: `IOptions<AgentOptions>`, `IAgentEventSink`, `IAgentDiskProbe`, `ILogger<…>`, `TimeProvider`.
Resuelve ffmpeg y rife con `ToolResolver.Find(AgentTools.Ffmpeg/Rife)`. Se registra como singleton en
`Program.cs`, con la interfaz y la clase.

**`Start(originalPath, durationHint)`:**
1. Si `!Enabled` o no hay ffmpeg, devuelve `[]` y loguea a nivel info, una sola vez por arranque si falta ffmpeg.
2. Etiquetas planeadas: `["Suavizado"]`, más `"RIFE"` si `rife` está resuelto.
3. Abre `new FileStream(originalPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
   1 MiB, FileOptions.Asynchronous | FileOptions.SequentialScan)`. Si falla, registra el trabajo y deja cada
   etiqueta lista para emitir `failed/source_missing` desde la tarea (todo terminal sale de la tarea, no del
   llamador). Este handle es el que garantiza el requisito de la Duda 6: el original **ya está abierto** cuando
   el cliente recibe la respuesta del stop. `FileShare.Delete` permite que `File.Move` (import) y `File.Delete`
   (quitar) funcionen en Windows mientras se copia. ffmpeg **nunca** abre el original.
4. Registra el trabajo (`pending` por etiqueta), con su `CancellationTokenSource`, y lanza `Task.Run(RunJobAsync)`.
   Devuelve las etiquetas.

Si ya hay un trabajo para el mismo `original_filename`, devuelve las etiquetas del existente y cierra el handle
nuevo. No pasa en la práctica (los nombres llevan milisegundos), pero evita trabajos duplicados.

**`RunJobAsync(job)`**, con todo dentro de `try/catch/finally`:
1. Espera la **cola global** (`SemaphoreSlim(1,1)`, con el `ct` del trabajo): un ffmpeg de post-proceso por vez
   en la PC (DA4).
2. **Espacio:** `probe.GetAvailableFreeBytes(DataDirectory)`. Si se conoce y es menor que
   `tamaño + MinFreeBytes`, todas las etiquetas pasan a `skipped/insufficient_disk` y el trabajo termina.
3. **Copia de trabajo:** `Directory.CreateDirectory(.postprocess/<stem>)` y copia del handle a `source.mp4` con
   buffer de 1 MiB y el `ct` del trabajo. Después cierra el handle del original. Si falla: `failed/source_missing`.
4. **Duración:** `ProbeDurationAsync(source.mp4)` corre `ffmpeg -hide_banner -i <copia>` con
   `RunArgumentListAsync` y timeout de 30 s. El exit code ≠ 0 es normal en este caso. Parsea
   `Duration: (\d+):(\d{2}):(\d{2}(?:\.\d+)?)` del stderr. Si no hay duración, usa `durationHint`. Si tampoco
   hay hint, la duración queda `null`.
5. Por cada etiqueta, en orden:
   - Umbral: si `max > 0` y la duración es conocida y mayor que `max`, `skipped/too_long`. Si la duración no se
     conoce, el umbral **no** se aplica (DA7).
   - Emite `running` (progreso `0` si la duración es conocida y es blend; si no, `null`).
   - **Blend:** `ffmpeg -hide_banner -nostats -loglevel error -progress pipe:1 -y -i <copia> -vf
     minterpolate=fps=30:mi_mode=blend -c:v libx264 -pix_fmt yuv420p -crf 23 <work>/<stem>_blend.mp4`,
     armado con `ArgumentList`. El callback de stdout parsea `out_time_us=<n>` (o `out_time_ms=`, que en
     ffmpeg también viene en µs) y calcula `pct = min(99, floor(n / 1e6 / duración × 100))`. Emite si pasaron
     `ProgressIntervalSeconds` desde el último evento y el entero cambió.
   - **RIFE:** los pasos de hoy (extraer PNG, `rife-ncnn-vulkan`, re-encode a 30 fps), pero con las carpetas de
     frames **dentro de** `<work>/rife_in` y `<work>/rife_out` (ya no en `Path.GetTempPath()`). Usa **un solo**
     plazo para los tres pasos: lo que queda del plazo se pasa como `timeout` de cada paso. El progreso queda
     en `null`.
   - Resultado `ExitCode == ProcessRunner.TimeoutExitCode` → borra el parcial y pasa a `timeout/timeout`.
     Otro código ≠ 0 → `failed/ffmpeg_error`, con `message` = última línea de stderr. Éxito → `File.Move` del
     parcial a `<DataDirectory>/<stem>_<sufijo>.mp4` (`overwrite: true`) → `ready` con `progress 100`,
     `filename` y `url`. **Además** emite `video_variant_ready` (compatibilidad, §4.3).
   - Entre etiquetas revisa `ct` (cancelación).
6. `catch (OperationCanceledException)`: toda etiqueta no terminal pasa a `cancelled` con el motivo guardado en
   el trabajo (`removed` / `agent_update` / `agent_stopping`).
   `catch (Exception)`: toda etiqueta no terminal pasa a `failed/internal_error`, y se loguea un warning con
   la excepción.
7. `finally`: cierra el handle si sigue abierto. Si **alguna** etiqueta quedó sin estado terminal, la pasa a
   `failed/internal_error` (red de seguridad, H2). Borra `.postprocess/<stem>/` recursivo, con 3 intentos
   separados por 500 ms porque en Windows el handle de ffmpeg puede tardar en soltarse. Si el motivo fue
   `removed`, borra de la raíz las variantes de este original. Libera la cola y marca el trabajo terminado con
   un timestamp, para la retención de §3.

**Emisión:** cada cambio de estado actualiza el registro **y** llama a `sink.BroadcastAsync` dentro de un
try/catch que solo loguea. Un WS caído nunca rompe el trabajo. Los estados terminales se loguean: info para
`ready`/`skipped`, warning para `failed`/`timeout`, info para `cancelled`. El log incluye la etiqueta, el
original, la duración del video y lo que tardó.

**Plazo** (puro, testeable):
`internal static TimeSpan ComputeTimeout(double? durationSeconds, VideoPostprocessOptions o)` →
`null → Max`; si no, `clamp(factor × d + extra, min, max)`. Por ejemplo, 12 min (720 s) con los defaults da
`3 × 720 + 120 = 2280 s` (38 min). La medición fue ≈ 10 min.

**Umbral** (puro): `internal static bool ExceedsThreshold(double? durationSeconds, int maxSeconds)`.

**Duración** (puro): `internal static double? ParseFfmpegDuration(string stderr)` y
`internal static int? ParseProgressPercent(string line, double? durationSeconds)`.

### 5.4 `IosService` (`Services/IosService.cs`)

- **Saca** `_videoPostprocessCount`, `_videoPostprocessSinceTicks`, `StartVideoPostprocess`,
  `RunInterpolationsAsync` y `RunRifeInterpolationAsync`, con el MCI incluido (Duda 2 A). La lógica pasa a §5.3.
- `ActiveOperations`: solo `recording_ios` y `airplay_session`. El comentario cambia: el post-proceso ya no
  bloquea.
- Inyecta `IVideoPostprocessService`.
- `IIosService.StopRecordingAsync` pasa a devolver `Task<IosStopResult>`, con
  `public sealed record IosStopResult(string OutputPath, IReadOnlyList<string> Variants);` (en `IosService.cs`,
  junto a la interfaz).
- En `StopRecordingAsync`:
  - **Mock:** si `MockIosRecordingSource` tiene valor y el archivo existe, `File.Copy(src, OutputPath,
    overwrite: true)` y, si `session.Mode` es `video_only` o `with_mic`,
    `variants = _post.Start(OutputPath, null)`. Si no, escribe el archivo vacío como hoy, con `variants = []`.
  - `on_device` → `new IosStopResult(path, [])`.
  - `airplay` → `[]` (como hoy, sin post-proceso).
  - DVT/burst (`video_only`/`with_mic`), después de validar que el archivo existe y no está vacío:
    `variants = _post.Start(session.OutputPath, DateTimeOffset.UtcNow - session.Started)`.

### 5.5 `RecordingController`

- iOS: `var r = await ios.StopRecordingAsync(serial, ct)`. Android: `variants = []`.
- Evento `recording_stopped`: `Data = new { filename, url, platform, variants_expected = variants.Count > 0,
  variants }`.
- Respuesta: `Ok(new { filename, url, variants_expected = …, variants })`.

### 5.6 Mantenimiento, apagado y arranque

- `AgentStateController`: inyecta `IVideoPostprocessService`. `State()` arma `background_operations` a partir de
  `BackgroundSince`. `AgentStateResponse` suma
  `IReadOnlyList<AgentOperationDto> BackgroundOperations` (ojo: es un **record posicional**, así que hay que
  actualizar el `new` del controlador). `EnterMaintenance`: si `TryEnterMaintenance` da `true`, llama a
  `post.CancelAll("agent_update")` y responde 200 igual que hoy.
- `Program.cs`:
  - Registros: `IAgentEventSink` → la misma instancia de `AgentWebSocketHub`, `VideoPostprocessService` y
    `IVideoPostprocessService`.
  - Al arrancar, junto a `CleanupOrphanUploads`, borra `<DataDirectory>/.postprocess/` si existe. Solo esa
    carpeta, por su ruta exacta, nunca un patrón. Lo suma al log de arranque ("trabajos de post-proceso
    huérfanos borrados: N").
  - `app.Lifetime.ApplicationStopping.Register(() => { post.CancelAll("agent_stopping");
    post.WhenIdleAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); })`.
- `OperationTracker`: solo se actualiza el comentario de `AgentOperation` (lista de `kind`s). No cambia el código.
- `HealthController.Capabilities`: suma `"video_postprocess_v1"`.

### 5.7 Endpoint de estado (`Controllers/VideoPostprocessController.cs`, nuevo)

`[Route("video-postprocess")]`, `[HttpGet("status")]`,
`([FromQuery(Name = "filename")] string[] filenames)`. Valida según §4.4 y responde
`new VideoPostprocessStatusResponse(items)`, con
`record VideoPostprocessStatusItem(string OriginalFilename, bool Known, IReadOnlyList<VideoVariantStatusDto> Variants)`.
El escaneo de disco para `known: false` (§4.4) va en el servicio (`GetStatus`), no en el controlador.

## 6. Diseño técnico: `client/`

Principio rector: **guardar el original nunca depende de las variantes.** El estado de las variantes es solo
visual.

### 6.1 `client/src/lib/agent.ts` (tipos del contrato)

```ts
export type VideoVariantLabel = "Suavizado" | "RIFE" | "MCI"; // "MCI" solo puede venir en §4.4 known:false (legado)
export type VideoVariantState = "pending" | "running" | "ready" | "failed" | "timeout" | "skipped" | "cancelled";
export type VideoVariantReason =
  | "ffmpeg_error" | "timeout" | "too_long" | "insufficient_disk" | "source_missing"
  | "removed" | "agent_update" | "agent_stopping" | "internal_error";

/** `video_variant_status` (WS, null explícitos) e items de `GET /video-postprocess/status` (HTTP, null omitidos). */
export interface VideoVariantStatus {
  original_filename: string;
  label: VideoVariantLabel | string;   // tolerante a etiquetas futuras
  state: VideoVariantState;
  progress?: number | null;
  filename?: string | null;
  url?: string | null;
  reason?: VideoVariantReason | string | null;
  message?: string | null;             // solo log; no se muestra
  updated_at: string;
}

export interface RecordingStoppedData {
  filename: string;
  url: string;
  platform?: "android" | "ios";
  /** Falta en un Tatana anterior a video_postprocess_v1. */
  variants_expected?: boolean;
  variants?: string[];
}

export interface VideoPostprocessStatusResponse {
  items: { original_filename: string; known: boolean; variants: VideoVariantStatus[] }[];
}
```

- `stopRecording` devuelve `Promise<{ filename: string; url: string; variants_expected?: boolean; variants?: string[] }>`.
- Método nuevo `videoPostprocessStatus(filenames: string[]): Promise<VideoPostprocessStatusResponse | null>`:
  `GET ${AGENT_URL}/video-postprocess/status?filename=…&filename=…` (con `encodeURIComponent`), con lotes de 50
  y timeout de 5 s. Cualquier error, 404 incluido (Tatana viejo), devuelve `null` sin tirar.
- `client/src/types/index.ts` re-exporta `VideoVariantStatus`, `VideoVariantState`, `VideoVariantReason` y
  `RecordingStoppedData`.

### 6.2 `client/src/hooks/useFileManager.ts`

- **Se saca el bloqueo:**
  - `saveOne`: se borra la línea 425 (`if (pendingVariantRef.current.has(name)) return false;`) y su comentario.
  - `handleSaveAndContinue`: se borran `waitingVariant` y su `return` con `file_busy` (líneas 595–601). Queda
    `pending.forEach(f => enqueueSave(f.name))`.
  - `addVideoVariant`: se borra el bloque `if (d.label === "MCI") { … }`. Ya no encola nada.
- **Se reemplazan** `pendingVariantFiles`/`setPending`/`pendingVariantRef`/`markPendingVariant` por:
  - `variantStatus: Record<string, Record<string, VideoVariantStatus>>` (original → etiqueta → estado), en
    `useState`.
  - `expectVariants(filename: string, labels: string[])`: para cada etiqueta **sin estado previo**, crea
    `{ original_filename, label, state: "pending", updated_at: now }`. Nunca pisa un estado que ya llegó: el
    `running` de Tatana puede llegar antes que `recording_stopped`.
  - `applyVariantStatus(s: VideoVariantStatus)`: guarda `s` salvo que ya haya uno con `updated_at` posterior.
    Si `s.state === "ready"` y trae `filename`, también llama a `addVideoVariant` (pestaña), que ya deduplica por
    `filename`. Se aplica aunque el archivo todavía no esté en `files`.
  - `syncVariantStatus(res: VideoPostprocessStatusResponse)`: por cada item, `known: true` → `applyVariantStatus`
    de cada variante. `known: false` → borra de `variantStatus[original]` toda etiqueta **no terminal** (corta
    el spinner) y aplica las `ready` que vengan (pestañas).
  - `refreshVariantStatus()`: toma los `files` cuyo nombre cumple `/^grabacion_ios_.+\.mp4$/i`, llama a
    `agent.videoPostprocessStatus` y, si no es `null`, a `syncVariantStatus`. Si no hay ninguno, no llama a nada.
- `dropLocal(filename)`: también borra `variantStatus[filename]` y `videoVariants[filename]`.
- `clearFiles()`: `setVariantStatus({})`. Se quita `pendingVariantRef.current.clear()`.
- Return: se sacan `pendingVariantFiles` y `markPendingVariant`. Se suman `variantStatus`, `expectVariants`,
  `applyVariantStatus` y `refreshVariantStatus`.
- `restoreCaseEvidence`: al final, si encontró grabaciones iOS, llama a `refreshVariantStatus()` sin `await`
  bloqueante (`void`).

### 6.3 `client/src/hooks/useRecording.ts`

El segundo parámetro pasa de `markPendingVariant` a
`expectVariants: (filename: string, labels: string[]) => void`. En el stop:
`if (stopped?.filename) { addFile(stopped.filename); if (platform === "ios" && stopped.variants_expected) expectVariants(stopped.filename, stopped.variants ?? []); }`.

### 6.4 `client/src/hooks/useAgentConnection.ts`

Suma un segundo parámetro opcional, `onConnected?: () => void`, guardado en un ref como `onEvent`. Se llama en
`ws.onopen`, en la primera conexión y en cada reconexión (Duda 9 A).

### 6.5 `client/src/app/dashboard/page.tsx`

- `recording_stopped`: `const d = event.data as unknown as RecordingStoppedData;` y después
  `addFile(d.filename)`. Si `d.platform === "ios" && d.variants_expected === true`, llama a
  `expectVariants(d.filename, d.variants ?? [])`. Si `variants_expected` falta (Tatana viejo), **no** marca nada
  (DA2).
- Evento nuevo: `if (event.type === "video_variant_status") applyVariantStatus(event.data as unknown as VideoVariantStatus);`
- `video_variant_ready`: sigue llamando a `addVideoVariant` (sin cambios).
- `useAgentConnection(handleWsEvent, () => { void refreshVariantStatus(); })`. El callback se arma con
  `useCallback`/ref estable para no reconectar.
- `useRecording(addFile, expectVariants)`. Se actualizan las deps del `useCallback` de `handleWsEvent`.
- Prop a `CaptureStep`: `variantStatus={variantStatus}` en lugar de `pendingVariantFiles`.

### 6.6 `client/src/components/CaptureStep.tsx` y `client/src/components/VideoCard.tsx`

- `CaptureStep`: la prop `pendingVariantFiles?: Set<string>` se reemplaza por
  `variantStatus?: Record<string, Record<string, VideoVariantStatus>>` (default `{}`). Al `VideoCard` le pasa
  `variantStatus={Object.values(variantStatus[selected.name] ?? {})}` y
  `storedInCase={!!selected.remoteFile?.storedInCase}`.
- `VideoCard`: se saca `isPending` y se suman `variantStatus?: VideoVariantStatus[]` y `storedInCase?: boolean`.
  - Nombre visible por etiqueta: `"Suavizado"` → "versión suavizada", `"RIFE"` → "versión RIFE", y cualquier
    otra → `versión ${label}`.
  - **En curso** (algún `pending`/`running`; se muestra el primero en orden): spinner +
    "Generando versión suavizada…" y, si `progress` es un número, " 42 %". Debajo, en texto chico:
    "El original ya está guardado en el caso. La versión suavizada es solo para ver mejor el video." si
    `storedInCase`, y si no, "El original no se modifica. La versión suavizada es solo para ver mejor el video.".
  - **Lista**: aparece su pestaña, como hoy (sale de `variants`).
  - **`failed` / `timeout` / `cancelled` (≠ `removed`)**: texto discreto, sin rojo ni toast:
    "No se pudo generar la versión suavizada. El original no se modificó." Para `cancelled` con `agent_update`
    o `agent_stopping`: "Se canceló la versión suavizada porque Tatana se reinició. El original no se modificó."
  - **`skipped`**: `too_long` → "Video largo: no se genera la versión suavizada." solo para `"Suavizado"`; para
    `"RIFE"` no se muestra nada (es un extra opcional). `insufficient_disk` → "Sin espacio en disco: no se
    generó la versión suavizada. El original no se modificó."
  - Sin estados ni variantes (AirPlay, `on_device`, Android, Tatana viejo): sin indicador ni pestañas extra.
  - `showTabs = allTabs.length > 1 || hayIndicador`.
  - **Accesibilidad:** el texto de estado va en un `role="status"` (`aria-live` implícito, sin robar el foco),
    con contenido **solo** del estado ("Generando versión suavizada", "No se pudo…"). El porcentaje va aparte,
    en un `role="progressbar"` con `aria-valuenow`/`aria-valuemin=0`/`aria-valuemax=100` y
    `aria-label="Avance de la versión suavizada"`. Así el lector de pantalla no lee el número cada 3 s. Spinner
    con `motion-reduce:animate-none`.

## 7. Diseño técnico: `agent-ui/`

- `src/main/agent-api.ts`: `AgentStateResponse` suma `background_operations: AgentOperation[]`. `getAgentState`
  lo parsea igual que `operations`, y si falta (Tatana viejo) queda `[]`. Se actualiza el comentario de `kind`.
- `src/main/updater/state.ts`: `UpdateState` suma `backgroundOperations?: string[]` (kinds sin repetir). Función
  nueva `backgroundOperationsNotice(kinds): string | null`: si incluye `video_postprocess`, devuelve
  "Al actualizar, se cancelará la versión suavizada que se está generando. Las grabaciones no se tocan."; si
  no, `null`. `busyOperationText('video_postprocess')` **se mantiene** ("procesando un video"), porque un
  Tatana viejo todavía lo manda en `operations`.
- `src/main/updater/update-manager.ts`: `setBackgroundOperations(kinds: string[])`, igual que
  `setBusyOperations` (solo en `phase === 'ready'`, con `sameList`). Se limpia `backgroundOperations` donde hoy se
  limpia `busyOperations` (`checking`, `installing`). **No** participa de `isReadyAndFree()`.
- `src/main/app-info.ts` (`tick`): además de `setBusyOperations`, llama a
  `updateManager.setBackgroundOperations(s ? [...new Set(s.background_operations.map(o => o.kind))] : [])`.
- `src/renderer/src/env.d.ts`: `TatanaUpdateState.backgroundOperations?: string[]`.
- `src/renderer/src/hooks/useTatanaUpdates.ts`: exporta `backgroundOperationsNotice`, la misma función del main
  y duplicada como hoy `busyOperationsText`.
- `src/renderer/src/components/UpdateStatus.tsx`: con `phase === 'ready' && !isBusy`, si
  `backgroundOperationsNotice(state.backgroundOperations)` no es null, muestra ese texto (ícono de info, tono
  secundario/ámbar suave) **en lugar de** "También se instala sola…". El botón sigue habilitado.
- `src/main/index.ts` (bandeja): con `busy.length === 0` y post-proceso en segundo plano, la etiqueta pasa a
  `Reiniciar y actualizar (cancela la versión suavizada en curso)`.
- Test nuevo `src/main/updater/state.test.ts` (`tsx --test`, entra por el glob de `npm test`):
  `backgroundOperationsNotice` con `[]`, `['video_postprocess']`, `['otro']` y `undefined`.

## 8. Decisiones técnicas

### 8.1 Validadas por el usuario (HU, todas A)

- **D1** El original se guarda apenas termina la grabación. Las variantes no bloquean el guardado ni el wizard.
- **D2** Fuera el MCI. "Suavizado" (blend) para cualquier duración; RIFE solo si está instalado, con umbral y el
  mismo esquema de plazo.
- **D3** Plazo proporcional: `3 × duración + 2 min`, mínimo 5 min, máximo 2 h, configurable. Al vencer: kill,
  borra el parcial y estado `timeout`.
- **D4** Estado en el `VideoCard` con porcentaje (salida `-progress` de ffmpeg), nunca bloqueante.
- **D5** Las variantes no son evidencia: ni caso, ni expediente, ni ZIP, ni hash.
- **D6** Guardar el original nunca espera ni falla por el post-proceso, en ningún SO (mecanismo: DA1).
- **D7** El post-proceso no bloquea la actualización. Se cancela en mantenimiento y `agent-ui` lo avisa.
- **D8** Quitar la grabación cancela su post-proceso y borra parciales y variantes.
- **D9** Al reconectar, el cliente consulta el estado en Tatana (§4.4).

### 8.2 Decisiones del arquitecto (modo autónomo, criterio conservador)

- **DA1 (mecanismo de D6): copia de trabajo, en todos los SO.** Tatana abre el original con
  `FileShare.Read | FileShare.Delete` **antes** de responder el stop. Lo copia a `.postprocess/<stem>/source.mp4` y
  ffmpeg/rife solo leen la copia. Alternativas descartadas:
  - Hardlink: en Windows el lock de ffmpeg (abre sin `FILE_SHARE_DELETE`) aplica al archivo, no al nombre, y el
    `File.Move` igual falla.
  - Pipe a stdin de ffmpeg: el MP4 de DVT no tiene `faststart` y ffmpeg necesita poder hacer seek.
  - Leer desde la carpeta del caso: depende del caseId, choca con `zip/commit` (borra la carpeta) y arriesga que
    las variantes terminen en el ZIP.

  Costo: ocupa transitoriamente el tamaño del original (1 GB por 12 min en el peor caso del QA) y suma ~1–3 s de
  copia en segundo plano. Se protege con el chequeo de espacio. Un solo camino de código para todos los SO
  permite probar en la Mac lo mismo que corre en Windows.
- **DA2 (compatibilidad con Tatana viejo).** Si `recording_stopped` no trae `variants_expected`, el cliente no
  marca nada y guarda enseguida (nunca vuelve a bloquear). Con un Tatana viejo en Windows, puede aparecer el
  `file_busy` de siempre mientras su MCI corre; lo cubren los reintentos existentes (3 × 5 s) y el reintento de
  "Guardar y continuar". Si hace falta, el admin sube `Tatana:MinVersion` en el backend. **Orden de despliegue:
  primero la web, después la release de Tatana.** Un cliente viejo con un Tatana nuevo esperaría para siempre un
  `video_variant_ready` con `"MCI"`, que ya no existe. Queda anotado para el PR y para la release.
- **DA3 RIFE con umbral de 120 s por defecto.** RIFE extrae un PNG por cuadro y genera 16x: con 12 min a 2 fps
  serían ~23 000 PNG de 1178x2556, decenas de GB en disco. La HU pide "el mismo umbral que las demás", pero sin
  valor. Para blend se respeta "sin límite" (D2) y RIFE queda acotado y configurable.
- **DA4 Cola global de un trabajo por vez.** Hoy cada stop lanza un `Task.Run` en paralelo. Dos grabaciones
  seguidas pondrían dos ffmpeg a competir por CPU/RAM. La segunda queda `pending` hasta que termine la primera.
- **DA5 Sin persistencia del registro.** Tras reiniciar Tatana, §4.4 responde `known: false` con lo que haya en
  disco, y `.postprocess/` se limpia al arrancar. No se reanudan trabajos interrumpidos (no hay botón de
  reintentar; está fuera de alcance).
- **DA6 Config inválida no impide arrancar.** Es una función de previsualización: un valor inválido loguea un
  warning y usa el default de ese campo. Distinto de `MaxUploadBytes`, que sí aborta el arranque.
- **DA7 Duración desconocida.** No se aplica el umbral, el plazo es el máximo (2 h) y el progreso queda
  indeterminado (`null`).
- **DA8 Se mantiene `video_variant_ready`** además de `video_variant_status`, para clientes viejos. Se puede retirar
  en una HU futura.
- **DA9 Fuente de grabación para el modo mock** (`Agent:MockIosRecordingSource`, solo con `--mock`). Permite
  probar el circuito completo sin iPhone (§11.2) sin agregar endpoints de prueba a la API.
- **DA10 Los nulls del WS no cambian.** El hub serializa `null` explícito. No se le agrega `WhenWritingNull`
  porque cambiaría el JSON de todos los eventos existentes. El cliente trata `null` = ausente.

No hay decisiones abiertas que bloqueen la implementación.

## 9. Contrato compartido

| Mensaje | Campo JSON (tal como viaja) | Tipo | Tatana (C#) | client (TS) | agent-ui (TS) |
|---|---|---|---|---|---|
| `POST /devices/{serial}/record/stop` (respuesta) | `filename`, `url` | string | `Controllers/RecordingController.cs` | `client/src/lib/agent.ts` (`stopRecording`) | — |
| ídem | `variants_expected` | boolean | ídem | ídem (opcional: Tatana viejo) | — |
| ídem | `variants` | string[] | ídem | ídem | — |
| WS `recording_stopped.data` | `filename`, `url`, `platform`, `variants_expected`, `variants` | string, string, `"android"\|"ios"`, boolean, string[] | `Controllers/RecordingController.cs` | `RecordingStoppedData` en `client/src/lib/agent.ts`; consumo en `client/src/app/dashboard/page.tsx` | — |
| WS `video_variant_status.data` | `original_filename`, `label`, `state`, `progress`, `filename`, `url`, `reason`, `message`, `updated_at` | string, string, enum §4.3, int\|null, string\|null, string\|null, enum\|null, string\|null, ISO string | `VideoVariantStatusDto` en `Services/VideoPostprocessService.cs` | `VideoVariantStatus` en `client/src/lib/agent.ts`; consumo en `page.tsx` y `useFileManager.ts` | — |
| WS `video_variant_ready.data` (sin cambios) | `original_filename`, `label`, `filename`, `url` | string | `Services/VideoPostprocessService.cs` (antes `IosService.cs`) | `page.tsx` → `addVideoVariant` | — |
| `GET /video-postprocess/status?filename=…` | `items[].original_filename`, `items[].known`, `items[].variants[]` (= `VideoVariantStatus`, null omitidos) | string, boolean, array | `Controllers/VideoPostprocessController.cs` | `VideoPostprocessStatusResponse` en `client/src/lib/agent.ts` (`videoPostprocessStatus`) | — |
| `GET /agent/state` | `background_operations[]` → `kind`, `since` (`detail` omitido) | `{ kind: string; since: string }[]` | `AgentStateResponse` / `AgentOperationDto` en `Controllers/AgentStateController.cs` | — | `AgentStateResponse` en `agent-ui/src/main/agent-api.ts` |
| ídem | `operations[]` ya **no** trae `kind: "video_postprocess"` | — | `IosService.ActiveOperations` | — | `agent-ui/src/main/updater/state.ts` (mantiene el texto por compatibilidad) |
| `GET /health` | `capabilities` incluye `"video_postprocess_v1"` | string[] | `Controllers/HealthController.cs` | `AgentHealth.capabilities` (comentario en `client/src/lib/agent.ts`) | — |

Valores cerrados:
- `state`: `pending`, `running`, `ready`, `failed`, `timeout`, `skipped` y `cancelled`.
- `reason`: `ffmpeg_error`, `timeout`, `too_long`, `insufficient_disk`, `source_missing`, `removed`,
  `agent_update`, `agent_stopping` e `internal_error`.
- `label`: `Suavizado`, `RIFE` y, solo en el legado de §4.4, `MCI`.

## 10. Checklist atómico

### 10.1 `implementer-backend` (`server/src/Factum.Agent` + `server/tests/Factum.Agent.Tests`)

> Regla dura de datos (AGENTS.md): no borrar ni modificar documentos de negocio ni archivos preexistentes de
> `DataDirectory`/`Storage`. Esta HU no toca Mongo. Las pruebas manuales usan un `--data` y un `--port`
> **propios en el scratchpad** (§11.2), nunca el `agent-data` del repo ni el Tatana que el usuario tenga
> corriendo. Una prueba limpia solo lo que ella creó, por ruta exacta. No mover tarjetas del Project ni tocar
> `progress/sesiones/`.

- [ ] B1 `Models/AgentModels.cs`: `VideoPostprocessOptions` (§5.1) con `Normalize(ILogger)`; `AgentOptions.VideoPostprocess` y `AgentOptions.MockIosRecordingSource`.
- [ ] B2 `appsettings.json`: sección `Agent:VideoPostprocess` con los defaults.
- [ ] B3 `Common/ProcessRunner.cs`: parámetro `onStdoutLine` en `RunArgumentListAsync` (§5.2), sin cambiar a los llamadores existentes.
- [ ] B4 `WebSockets/AgentWebSocketHub.cs`: `IAgentEventSink` (interfaz nueva, mismo archivo o `Services/`) implementada por el hub. Sin tocar `JsonOpts`.
- [ ] B5 `Services/VideoPostprocessService.cs`: interfaz, DTO, constantes, `Start`, `RunJobAsync`, la cola, la copia con `FileShare.Read | FileShare.Delete`, el probe de duración, blend con `-progress`, RIFE dentro de la carpeta de trabajo, plazo y umbral, el `finally` con la garantía de estado terminal, `Discard`, `CancelAll`, `GetStatus` (registro + escaneo de disco), `BackgroundSince`, `WhenIdleAsync` y la retención de 6 h / 200 entradas (§5.3).
- [ ] B6 Funciones puras `internal static`: `ComputeTimeout`, `ExceedsThreshold`, `ParseFfmpegDuration`, `ParseProgressPercent`.
- [ ] B7 `Services/IosService.cs`: saca el post-proceso viejo y el MCI; `ActiveOperations` sin `video_postprocess`; `IosStopResult`; `StopRecordingAsync` según §5.4, mock con fuente incluido; inyecta `IVideoPostprocessService`.
- [ ] B8 `Controllers/RecordingController.cs`: `variants_expected`/`variants` en el evento y en la respuesta (§5.5).
- [ ] B9 `Controllers/VideoPostprocessController.cs` (nuevo): `GET /video-postprocess/status` (§4.4/§5.7).
- [ ] B10 `Controllers/FilesController.cs` y `Controllers/CaseEvidenceController.cs`: `Discard(filename)` después del borrado (§4.6), sin cambiar las respuestas.
- [ ] B11 `Controllers/AgentStateController.cs`: `background_operations` en `AgentStateResponse`; `CancelAll("agent_update")` al entrar en mantenimiento.
- [ ] B12 `Program.cs`: registros DI, limpieza de `.postprocess/` al arrancar (solo esa ruta), `ApplicationStopping`.
- [ ] B13 `Controllers/HealthController.cs`: capability `video_postprocess_v1`.
- [ ] B14 `Services/OperationTracker.cs`: actualizar el comentario de `kind`s (`video_postprocess` ya no viene de `IosService`).
- [ ] B15 Tests xUnit (§10.3).
- [ ] B16 `dotnet build server/src/Factum.Agent/Factum.Agent.csproj` sin warnings nuevos, y `dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj` en verde.
- [ ] B17 Prueba sin iPhone de §11.2 (pasos 1–6), con los números medidos escritos en `progress/impl_backend_ios-grabacion-larga.md`.

### 10.2 `implementer-frontend` (`client/` **y** `agent-ui/`)

> Skills obligatorias: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (solo como
> criterio, sin 3D) y `web-design-guidelines` (autochequeo al final). `ui-styling` y
> `mblode-agent-skills-ui-animation` si aplican al indicador. Leer `client/AGENTS.md` (Next.js 16). No mover
> tarjetas del Project ni tocar `progress/sesiones/`. No toca la base.

**client/**
- [ ] F1 `src/lib/agent.ts`: tipos de §6.1, `stopRecording` con los campos nuevos y método `videoPostprocessStatus` (lotes de 50, 5 s, `null` ante cualquier error).
- [ ] F2 `src/types/index.ts`: re-exportar los tipos nuevos.
- [ ] F3 `src/hooks/useFileManager.ts`: sacar el bloqueo (línea 425, líneas 595–601 y bloque MCI de `addVideoVariant`).
- [ ] F4 `src/hooks/useFileManager.ts`: `variantStatus`, `expectVariants`, `applyVariantStatus`, `syncVariantStatus` y `refreshVariantStatus`; `dropLocal`/`clearFiles`/`restoreCaseEvidence` según §6.2; actualizar el return.
- [ ] F5 `src/hooks/useRecording.ts`: el parámetro `expectVariants` y su uso en el stop (§6.3).
- [ ] F6 `src/hooks/useAgentConnection.ts`: `onConnected` en `ws.onopen` (§6.4).
- [ ] F7 `src/app/dashboard/page.tsx`: `recording_stopped` con `variants_expected`, `video_variant_status`, refresco al conectar y props nuevas (§6.5).
- [ ] F8 `src/components/CaptureStep.tsx`: prop `variantStatus` y pasaje al `VideoCard`.
- [ ] F9 `src/components/VideoCard.tsx`: estados, copy, progreso y accesibilidad de §6.6.
- [ ] F10 `grep -rn "pendingVariant\|markPendingVariant\|isPending" client/src` sin resultados.
- [ ] F11 `cd client && npx tsc --noEmit` en verde.

**agent-ui/**
- [ ] F12 `src/main/agent-api.ts`: `background_operations` (§7).
- [ ] F13 `src/main/updater/state.ts`: `backgroundOperations` y `backgroundOperationsNotice`.
- [ ] F14 `src/main/updater/update-manager.ts`: `setBackgroundOperations` y su limpieza en `checking`/`installing`.
- [ ] F15 `src/main/app-info.ts`: llamada en `tick`.
- [ ] F16 `src/renderer/src/env.d.ts`, `hooks/useTatanaUpdates.ts` y `components/UpdateStatus.tsx`: aviso en lugar de "También se instala sola…" cuando corresponde.
- [ ] F17 `src/main/index.ts`: etiqueta de la bandeja.
- [ ] F18 `src/main/updater/state.test.ts` (nuevo).
- [ ] F19 `cd agent-ui && npx tsc --noEmit -p tsconfig.web.json && npx tsc --noEmit -p tsconfig.node.json && npm test` en verde.

### 10.3 Tests xUnit (`server/tests/Factum.Agent.Tests/VideoPostprocessServiceTests.cs`, nuevo)

Puros, siempre corren:
- `ComputeTimeout`: `null` → máximo; 60 s → 300 (piso); 720 s → 2280; 3 h → 7200 (techo); config custom.
- `ExceedsThreshold`: `max = 0` nunca; duración `null` nunca; 121 > 120 sí; 120 no.
- `ParseFfmpegDuration`: stderr real de `ffmpeg -i` (`Duration: 00:12:00.00, start: …`) → 720; sin línea → `null`; `Duration: N/A` → `null`.
- `ParseProgressPercent`: `out_time_us=360000000` con 720 s → 50; tope 99; duración `null` → `null`; `out_time_us=N/A` → `null`.
- `VideoPostprocessOptions.Normalize`: valores inválidos vuelven al default campo por campo.

De integración, con ffmpeg real (`[FfmpegFact]`: atributo propio, derivado de `FactAttribute`, que pone `Skip`
si `ToolResolver.Find(AgentTools.Ffmpeg)` es `null`). Usan `DataDirectory` en un `Path.GetTempPath()/factum-pp-<guid>`
propio, un `IAgentEventSink` falso que guarda los eventos, y borran **solo** esa carpeta en `Dispose`. El video de
prueba se genera en el propio test con
`ffmpeg -f lavfi -i testsrc2=size=320x640:rate=2 -t 20 -c:v libx264 -pix_fmt yuv420p <tmp>/grabacion_ios_test.mp4`:
- **Guardar no espera:** `Start` y, enseguida, `CaseEvidenceStore.ImportAsync` sobre un caseId Guid nuevo. Se mide el import con `Stopwatch` (< 5 s) y el SHA-256 tiene que coincidir con el calculado antes de `Start`. Se espera el `ready` de "Suavizado". Al final: `<stem>_blend.mp4` está en la raíz, la carpeta del caso solo tiene el original (mismo hash) y `.postprocess/<stem>` no existe.
- **Timeout:** opciones con `TimeoutMinSeconds = TimeoutMaxSeconds = 1` y un video más largo (120 s a 2 fps, 640x1280) → evento `timeout/timeout`, sin `<stem>_blend.mp4` en la raíz y sin carpeta de trabajo.
- **Cancelación por borrado:** `Start`, esperar `running` y llamar a `Discard` → `cancelled/removed`; ni la variante ni la carpeta de trabajo quedan.
- **Mantenimiento:** `Start` y `CancelAll("agent_update")` → `cancelled/agent_update`; `BackgroundSince` vuelve a `null`.
- **Umbral:** `BlendMaxSourceSeconds = 5` con el video de 20 s → `skipped/too_long` sin lanzar ffmpeg de blend.
- **Falla:** original corrupto (bytes al azar con extensión `.mp4`) → `failed` (`ffmpeg_error`) y nunca se queda sin evento terminal.
- **Move con el handle abierto:** abrir la fuente como en `Start` (`FileShare.Read | FileShare.Delete`), hacer `File.Move` a otra carpeta → no tira. En Windows es la prueba que importa; en macOS/Linux pasa trivialmente.
- **Estado tras "reinicio":** un servicio nuevo sobre el mismo `DataDirectory`, con `<stem>_blend.mp4` ya presente → `GetStatus` da `known: false` con un `ready` de "Suavizado".
- Toda espera de evento en los tests tiene un tope (30 s, o 60 s en la de import) para que un bug no cuelgue la suite.

## 11. Verificación

### 11.1 Comandos antes de declararse `done`

| Implementador | Comandos |
|---|---|
| backend | `dotnet build server/src/Factum.Agent/Factum.Agent.csproj` · `dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj` · §11.2 |
| frontend | `cd client && npx tsc --noEmit` · `cd agent-ui && npx tsc --noEmit -p tsconfig.web.json && npx tsc --noEmit -p tsconfig.node.json && npm test` |
| orquestador | `./ops/harness/verify.sh` |

### 11.2 Prueba sin iPhone (implementer-backend, en la Mac; video de 12 min)

Todo en el scratchpad de la sesión (`$S`), con un Tatana **aparte** en el puerto 8766. Así no se toca el
`agent-data` del repo ni el Tatana del usuario (que puede estar en 8765).

1. **Generar el MP4**, con los mismos parámetros que el QA y la salida DVT (1178x2556 a 2 fps, libx264 crf 23,
   12 min):
   ```bash
   S=<scratchpad>; mkdir -p "$S/pp-media" "$S/pp-data"
   ffmpeg -f lavfi -i "testsrc2=size=1178x2556:rate=2,noise=alls=4:allf=t" -t 720 \
     -vf "scale=trunc(iw/2)*2:trunc(ih/2)*2" -c:v libx264 -crf 23 -pix_fmt yuv420p "$S/pp-media/fuente_12min.mp4"
   ```
2. **Levantar Tatana mock** con la fuente:
   ```bash
   Agent__MockIosRecordingSource="$S/pp-media/fuente_12min.mp4" \
     dotnet run --project server/src/Factum.Agent -- --mock --port 8766 --data "$S/pp-data"
   ```
3. **Grabar y detener** (dispositivo mock `mock-ios-udid-001`), midiendo el stop:
   ```bash
   A=http://localhost:8766
   curl -s -X POST -H 'Content-Type: application/json' -d '{"android_version":0,"platform":"ios","ios_mode":"video_only"}' $A/devices/mock-ios-udid-001/record/start
   curl -s -w '\nstop: %{time_total}s\n' -X POST "$A/devices/mock-ios-udid-001/record/stop?platform=ios"
   ```
   Esperado: `variants_expected: true` y `variants: ["Suavizado"]` (más `"RIFE"` si está instalado).
4. **Guardar el original enseguida**, mientras corre el post-proceso. Se mide el tiempo y se compara el hash:
   ```bash
   F=<filename del paso 3>; C=$(uuidgen | tr A-Z a-z)
   curl -s -w '\nimport: %{time_total}s\n' -X POST -H 'Content-Type: application/json' -d "{\"filename\":\"$F\"}" $A/cases/$C/evidence/import
   shasum -a 256 "$S/pp-media/fuente_12min.mp4" "$S/pp-data/cases/$C/$F"
   ```
   Criterio: `import` **< 5 s** (el QA midió 0,81 s por 1 GB), el `sha256` de la respuesta es igual a los dos
   `shasum` y no hay `file_busy`.
5. **Observar el post-proceso:**
   - `curl -s "$A/video-postprocess/status?filename=$F"` varias veces: `running` con `progress` subiendo.
   - `curl -s $A/agent/state`: `busy: false`, `operations: []` y `background_operations` con
     `video_postprocess`.
   - Opcional: `websocat ws://localhost:8766/ws` para ver `video_variant_status` cada ≥ 3 s.

   Se anotan el `progress` a los 60 s y el ritmo (fps de salida). Con el ritmo del QA (≈ 38 fps), el blend
   tarda ≈ 10 min. No hace falta esperarlo entero si el progreso es coherente; si se espera, el criterio es
   `ready` + `video_variant_ready` + `<stem>_blend.mp4` en la raíz + `.postprocess/` vacía.
6. **Cortes:**
   - Repetir 3 y 4 con un segundo stop y, con su trabajo en `running`, correr
     `curl -s -X POST $A/agent/maintenance -d '{"ttl_seconds":30}' -H 'Content-Type: application/json'` (sin
     `Origin`). Esperado: 200, `cancelled/agent_update`, sin `ffmpeg` vivo
     (`pgrep -fl "minterpolate"` vacío) y carpeta de trabajo borrada. Después,
     `curl -s -X DELETE $A/agent/maintenance`.
   - Repetir con `DELETE $A/cases/$C/files/<F2>` → `cancelled/removed` y sin variantes de ese original en la
     raíz.
   - Repetir con `Agent__VideoPostprocess__TimeoutMaxSeconds=20 Agent__VideoPostprocess__TimeoutMinSeconds=20`
     → `timeout` a los ~20 s, sin parcial ni ffmpeg vivo.
7. **Limpieza:** se corta Tatana y se hace `rm -rf "$S/pp-media" "$S/pp-data"`, **esas dos rutas exactas**. No se
   toca nada más.

### 11.3 Prueba en el navegador (implementer-frontend, con el Tatana mock de §11.2 o el del usuario)

Con `NEXT_PUBLIC_AGENT_URL=http://localhost:8766` y un caso de prueba **creado para esto**, en el flujo agent y
paso 3:
- Grabar iOS mock y detener → la grabación pasa a "guardada" sin esperar. El `VideoCard` muestra "Generando versión
  suavizada… N %" con el texto "El original ya está guardado en el caso…". "Guardar evidencia en esta PC y
  continuar" avanza.
- Recargar la página a mitad del proceso y volver al caso → el indicador se recupera desde §4.4. Con Tatana
  reiniciado → no queda girando y, si la variante ya existe, aparece la pestaña.
- Modo AirPlay o `on_device` en mock (sin fuente) → sin indicador.
- Quitar la grabación de la bandeja con el post-proceso en curso → se va sin errores.
- agent-ui (`npm run dev`) contra un agente con post-proceso corriendo → "Reiniciar y actualizar" habilitado con el
  aviso. Si no se puede forzar la fase `ready` en dev, alcanza con el test F18 y una captura del componente con
  estado simulado.

Los datos de prueba que se crearon (caso, evidencia registrada) se borran **solo por sus ids**. Si no se puede
hacer sin tocar datos ajenos, se dejan y se anotan en `progress/impl_frontend_ios-grabacion-larga.md`.

### 11.4 Prueba manual que queda para el usuario

1. **Mac, iPhone real:** grabar 12 min en "Solo pantalla" y detener. La grabación queda guardada en el caso en
   menos de 1 minuto y se puede avanzar de paso. La versión suavizada aparece después (≈ 10 min) como pestaña, y
   el SHA-256 del ZIP coincide con el registrado.
2. **Mac:** grabar 10 min por AirPlay → se guarda enseguida, sin indicador.
3. **iPhone "en el iPhone" (on_device)** → se guarda enseguida, sin indicador.
4. **Windows (Tatana instalado o portátil):** grabar 10 min DVT y detener → se guarda en el caso **sin
   `file_busy`** mientras se genera la variante. Es el caso que no se puede probar en la Mac.
5. **Regresión:** 30 s de iPhone DVT y 30 s de Android → como hoy; la de iPhone muestra la versión suavizada.
6. **agent-ui:** con una versión de Tatana disponible y un post-proceso en curso, "Reiniciar y actualizar" está
   habilitado, avisa que se cancela la versión suavizada y, tras actualizar, la grabación sigue intacta en el
   caso.

### 11.5 Qué chequea el reviewer además de `CHECKPOINTS.md`

- Ningún camino de `RunJobAsync` termina sin un estado terminal por etiqueta (leer el `finally`).
- ffmpeg/rife nunca reciben la ruta del original, solo la copia de `.postprocess/`.
- Nada del post-proceso escribe en `cases/<id>/`.
- La limpieza de arranque borra solo `<DataDirectory>/.postprocess/`.
- `useFileManager` ya no condiciona `saveOne`/`handleSaveAndContinue` a variantes.
- Contrato de §9 igual en los tres lados (nombres snake, `null` tolerado en el cliente).
- `agent-ui` sigue mostrando "procesando un video" si un Tatana viejo lo manda en `operations`.

## 12. Fuera de alcance (recordatorio técnico)

- Botón de reintentar o generar a pedido una variante fallida, omitida o cancelada.
- Persistir el registro de post-proceso entre reinicios o reanudar trabajos.
- Guardar las variantes como evidencia derivada (sería la Duda 5 B, otra HU).
- El ZIP lento con media comprimida (H3), el tope de 10 min de la cámara externa, los timeouts del stop de la
  grabación y el límite de Caddy.
- Cambiar la serialización del hub WS (DA10).
