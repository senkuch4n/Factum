# impl backend: ios-grabacion-larga (#50)

**Estado:** done
**Rama:** `feat/ios-grabacion-larga`. Sin commit, como se pidió.
**Alcance:** solo `server/src/Factum.Agent` y `server/tests/Factum.Agent.Tests`. No toqué `Factum.Backend`, `client/`, `agent-ui/`, Mongo, `progress/sesiones/` ni el Project.

## Archivos tocados

| Archivo | Cambio | Checklist |
|---|---|---|
| `server/src/Factum.Agent/Models/AgentModels.cs` | `VideoPostprocessOptions` con defaults, `Normalize(ILogger?)` campo por campo (DA6) y `Clone()`. `AgentOptions.VideoPostprocess` y `AgentOptions.MockIosRecordingSource`. | B1 |
| `server/src/Factum.Agent/appsettings.json` | Sección `Agent:VideoPostprocess` con los defaults. No incluye `MockIosRecordingSource`. | B2 |
| `server/src/Factum.Agent/Common/ProcessRunner.cs` | Parámetro opcional `Action<string>? onStdoutLine` al final de `RunArgumentListAsync`. Una excepción del callback se traga. Los que ya lo llamaban no cambian. | B3 |
| `server/src/Factum.Agent/WebSockets/AgentWebSocketHub.cs` | Interfaz `IAgentEventSink`, implementada por el hub. `JsonOpts` no cambia. | B4 |
| `server/src/Factum.Agent/Services/VideoPostprocessService.cs` (**nuevo**) | `IVideoPostprocessService`, `VideoVariantStatusDto`, `VideoVariantStates`/`VideoVariantReasons`, constantes `LabelBlend`/`LabelRife`/`WorkDirName`, cola, copia de trabajo, probe, blend con `-progress`, RIFE dentro de la carpeta de trabajo, plazo y umbral, `finally` con estado terminal garantizado, `Discard`, `CancelAll`, `GetStatus` (registro + disco), `BackgroundSince`, `WhenIdleAsync`, retención 6 h / 200 y `CleanupWorkDirectory` estático. Funciones puras `ComputeTimeout`, `ExceedsThreshold`, `ParseFfmpegDuration` y `ParseProgressPercent`. | B5, B6 |
| `server/src/Factum.Agent/Services/IosService.cs` | Saqué `_videoPostprocessCount`/`SinceTicks`, `StartVideoPostprocess`, `RunInterpolationsAsync` (MCI incluido) y `RunRifeInterpolationAsync`. `ActiveOperations` queda solo con `recording_ios`/`airplay_session`. `IosStopResult(OutputPath, Variants)`. `StopRecordingAsync` sigue §5.4 (mock con fuente, `on_device` y airplay → `[]`, DVT/burst → `_post.Start(path, now - Started)`). Inyecta `IVideoPostprocessService`. | B7 |
| `server/src/Factum.Agent/Controllers/RecordingController.cs` | Suma `variants_expected`/`variants` a `recording_stopped` y a la respuesta del stop. Android → `[]`. | B8 |
| `server/src/Factum.Agent/Controllers/VideoPostprocessController.cs` (**nuevo**) | `GET /video-postprocess/status?filename=…` (1–50, `Path.GetFileName(x)==x`, si no → 400 `invalid_filename`). Records `VideoPostprocessStatusItem`/`VideoPostprocessStatusResponse`. | B9 |
| `server/src/Factum.Agent/Controllers/FilesController.cs`, `CaseEvidenceController.cs` | `post.Discard(filename)` en un `finally` después del borrado: corre exista o no el archivo y aunque el borrado tire. La respuesta no cambia. | B10 |
| `server/src/Factum.Agent/Controllers/AgentStateController.cs` | `AgentStateResponse.BackgroundOperations` (record posicional actualizado) desde `BackgroundSince`. `CancelAll("agent_update")` después de `TryEnterMaintenance` ok. | B11 |
| `server/src/Factum.Agent/Program.cs` | Registros DI: `IAgentEventSink` (→ hub), `VideoPostprocessService`, `IVideoPostprocessService`. Limpieza de `<DataDirectory>/.postprocess/` al arrancar, solo esa ruta, con el log "trabajos de post-proceso huérfanos borrados: N". `ApplicationStopping` → `CancelAll("agent_stopping")` + `WhenIdleAsync(5 s)`. | B12 |
| `server/src/Factum.Agent/Controllers/HealthController.cs` | Capability `video_postprocess_v1`. | B13 |
| `server/src/Factum.Agent/Services/OperationTracker.cs` | Solo el comentario de los `kind`. | B14 |
| `server/tests/Factum.Agent.Tests/VideoPostprocessServiceTests.cs` (**nuevo**) | 25 tests (§10.3) + `[FfmpegFact]`. | B15 |

## Verificación

### Build (B16)

```
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
Build succeeded.    0 Warning(s)    0 Error(s)
```

El baseline antes del cambio también daba 0 warnings, así que no agregué ninguno. El build de tests muestra 2 warnings (CS8714/CS8619) en `EvidenceZipTests.cs:58-59`. Ya estaban: ese archivo no lo toqué.

### Tests (B16)

```
dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj
Passed!  - Failed: 0, Passed: 279, Skipped: 0, Total: 279, Duration: 4 s
```

Corrí los 25 de `VideoPostprocess` 3 veces más seguidas y dieron 25/25 en las tres. ffmpeg 8.1.2 de Homebrew está instalado, así que los `[FfmpegFact]` corrieron, no quedaron en Skip. Al terminar no quedaba ninguna carpeta `factum-pp-*` en el temporal.

Qué cubren:
- **Puros:** `ComputeTimeout` (null → 7200; 60 → 300; 720 → 2280; 3 h → 7200; config custom), `ExceedsThreshold`, `ParseFfmpegDuration` (stderr real, sin línea, `N/A`, null), `ParseProgressPercent` (50, `out_time_ms`, tope 99, negativo → 0, null, `N/A`, otra línea) y `Normalize` (inválidos al default campo por campo; min > max; NaN).
- **Validación** del controlador de estado: nombres con ruta, vacíos, 0 o más de 50.
- **Integración con ffmpeg real:**
  - Guardar no espera: import < 5 s, mismo SHA-256, `ready` con `progress 100`/`filename`/`url`, más `video_variant_ready`. La variante queda en la raíz, el caso solo tiene el original con el mismo hash y no queda `.postprocess/<stem>`.
  - Timeout con min = max = 1 s.
  - `Discard` con el job en `running` → `cancelled/removed`.
  - `CancelAll("agent_update")`, y `BackgroundSince` vuelve a null.
  - Umbral → `skipped/too_long` sin evento `running`.
  - Original corrupto → `failed/ffmpeg_error`, con `message` de 200 caracteres o menos.
  - Exactamente un terminal por etiqueta, y `pending` nunca viaja por WS.
- **Sin ffmpeg:** original inexistente → `failed/source_missing`; sin espacio → `skipped/insufficient_disk`; `Enabled=false` o sin ffmpeg → `[]`.
- **Disco:** `File.Move` con el handle `FileShare.Read|Delete` abierto; estado tras reinicio (`known:false` con Suavizado y MCI legado); `Discard` sin trabajo borra solo las variantes de ese original; `CleanupWorkDirectory` borra solo `.postprocess`.

### Prueba sin iPhone §11.2 (B17)

La corrí en macOS arm64 con un Tatana aparte: `dotnet run --no-build -- --mock --port 8766 --data <scratchpad>/pp-data` y `Agent__MockIosRecordingSource=<scratchpad>/pp-media/fuente_12min.mp4`. El WS lo escuché con un cliente node propio. No usé el Tatana del usuario ni el `agent-data` del repo. El backend (:8080) y el client (:3000) no se tocaron.

1. **Fuente:** `testsrc2=size=1178x2556:rate=2,noise=…`, 720 s, libx264 crf 23 → 1 024 626 930 bytes (≈ 1 GB, igual que el QA). Generarla tardó 1 min 36 s.
2. **Stop** (`ios_mode: video_only`): `0,008 s`. Respuesta `{"filename":"grabacion_ios_20261012_232139_660.mp4","url":"/files/…","variants_expected":true,"variants":["Suavizado"]}`. El `recording_stopped` del WS trae los mismos campos más `platform: "ios"`. Hay un solo label porque RIFE no está instalado.
3. **Import inmediato** al caso (`uuidgen`): `0,517 s`, sin `file_busy`. El `sha256` de la respuesta, el `shasum` de la fuente y el `shasum` del archivo en `cases/<id>/` coinciden: `a2719aa11f63f94119c1cf4e23de564ce2dd60537c22eba41d859c9781c1bf3b`. Al terminar la variante, volví a hashear el archivo del caso y daba lo mismo.
4. **Post-proceso:**
   - La copia de trabajo de 1 GB estuvo en ≈ 1,1 s: `running 0` llegó a los 1,1 s del stop.
   - `GET /video-postprocess/status` → `running` con `progress` 3 → 9 → 15 (muestras cada 20 s).
   - El WS mandó `video_variant_status` cada ≈ 3–4 s, uno por punto porcentual.
   - Progreso a los 60 s: ≈ 17 %.
   - `GET /agent/state` durante el proceso: `busy:false`, `operations:[]` y `background_operations:[{"kind":"video_postprocess","since":…}]`.
   - **El blend completo tardó 285,3 s para 720 s de video**: ≈ 0,4x la duración, ≈ 76 fps de salida. Fue más rápido que el ≈ 1,2x del QA, probablemente porque el contenido sintético es más simple.
   - Al final: `ready` con `progress:100`/`filename`/`url`, más `video_variant_ready`. `<stem>_blend.mp4` (366 MB) quedó en la raíz, `.postprocess/` quedó vacía y `background_operations` volvió a `[]`.
5. **Cortes:**
   - **Mantenimiento:** segundo stop + import (0,43 s, mismo hash). Con `running 3 %` → `POST /agent/maintenance` sin `Origin` → **200**. Llegó `cancelled/agent_update` (≈ 30 ms después). `pgrep -fl minterpolate` salió vacío y la carpeta de trabajo quedó borrada. `DELETE /agent/maintenance` → 204.
   - **Quitar:** tercer stop (`with_mic`) + import y, con `running 1 %`, `DELETE /cases/<id>/files/<F3>` → 200 `{"deleted":…}`. Llegó `cancelled/removed`, sin ffmpeg vivo, sin carpeta de trabajo y sin variantes de ese original en la raíz.
   - **Timeout:** reinicié Tatana con `Agent__VideoPostprocess__TimeoutMinSeconds=20` y `TimeoutMaxSeconds=20`. Llegó `timeout/timeout` con el mensaje `"no terminó en 20 s"` a los 20,0 s. No quedó parcial ni ffmpeg vivo.
   - **Reinicio:** ese segundo Tatana responde por el primer original `known:false` + `Suavizado ready` (sale del disco). El log de arranque dice "trabajos de post-proceso huérfanos borrados: 0".
   - **Apagado (extra):** SIGTERM a Tatana con un trabajo `running` → llegó `cancelled/agent_stopping` por WS antes de cerrar, sin ffmpeg vivo y con `.postprocess/` vacía.
   - **Otros modos en mock con fuente:** `airplay` y `on_device` → `variants_expected:false, variants:[]`.
   - **Validación:** `GET /video-postprocess/status` sin `filename` → 400 `invalid_filename`, y con `..%2Fx.mp4` → 400 `invalid_filename`.
6. **Limpieza:** detuve las dos instancias de Tatana y los clientes WS. Corrí `rm -rf "<scratchpad>/pp-media" "<scratchpad>/pp-data"` (esas dos rutas exactas) y borré mis logs y pids del scratchpad. No quedó nada escuchando en 8766.

## Decisiones no obvias

- **Emisión por `Channel` con un solo lector.** Los eventos del post-proceso se encolan y un pump los manda por `IAgentEventSink` de a uno. Así el orden es estable (un `running` viejo nunca sale después del `ready`), el post-proceso nunca hace dos `SendAsync` en paralelo y un WS caído no frena el trabajo, porque el pump atrapa la excepción y loguea en debug.
- **El registro guarda el progreso siempre; el WS lo recibe con throttle.** Cada punto porcentual nuevo actualiza el registro en memoria, así `GET /status` da el último valor. El WS se emite solo si pasaron `ProgressIntervalSeconds` y el entero cambió.
- **Exactamente un terminal:** `EmitAsync` ignora cualquier cambio sobre una etiqueta que ya está terminal. El `finally` emite `failed/internal_error` para las que hayan quedado sin terminal.
- **`Discard` vs. `CancelAll`:** el motivo que gana para el estado es el primero (`CancelReason ??=`). `Removed` es un flag aparte, así quitar la grabación borra sus variantes aunque antes se haya cancelado por mantenimiento. El `finally` marca `Finished` y lee `Removed` bajo el mismo lock. Un `Discard` posterior entra por el camino "sin trabajo activo" y borra de la raíz.
- **Normalize sobre una copia** (`Clone()`) para no mutar las opciones compartidas de `IOptions`. Si `TimeoutMinSeconds > TimeoutMaxSeconds`, los dos vuelven al default, porque no se sabe cuál de los dos está mal. `TimeoutMaxSeconds <= 0` también cuenta como inválido.
- **Resolución de ffmpeg/rife en el constructor**, como pide la SDD. Hay un constructor `internal` con rutas explícitas para los tests (rife = null, así el resultado no depende de la PC).
- **RIFE:** para extraer uso `-fps_mode passthrough` en lugar del `-vsync passthrough` deprecado (existe desde ffmpeg 5.1). Los flags de `rife-ncnn-vulkan` no cambian respecto del código anterior. Un solo plazo para los tres pasos: a cada uno se le pasa lo que queda del plazo, y si se agota antes de un paso se devuelve `TimeoutExitCode`. **RIFE no se pudo probar**: no está instalado en esta Mac.
- **Mock con fuente:** se copia la fuente también en los modos `airplay` y `on_device`, pero sin variantes, como dice §5.4. En macOS `File.Copy` de 1 GB tarda ms (APFS clone).
- **El nombre del original en `Discard` se valida** (`Path.GetFileName(x)==x`, no vacío). El borrado en la raíz solo corre si el nombre cumple `^grabacion_ios_.+\.mp4$`. Solo borra `<stem>_blend|_rife|_mci.mp4`, por ruta exacta.
- **`/video-postprocess/status`** responde con `Cache-Control: no-store`.

## Observación fuera de alcance (no cambiada)

Al apagar Tatana con un cliente WS conectado, el host espera a que ese cliente cierre antes de terminar el proceso: el read loop del hub no corta con `ApplicationStopping`. Ya pasaba antes de esta HU y no afecta al post-proceso: la cancelación `agent_stopping`, el kill de ffmpeg y la limpieza ocurren apenas empieza el apagado. Queda anotado por si la HU de autoupdate necesita que el cierre sea inmediato.

## Contrato (§9): coincide con la SDD

Verificado con el JSON real capturado en la prueba §11.2:
- `POST …/record/stop` → `filename`, `url`, `variants_expected` (bool, siempre), `variants` (string[], siempre).
- WS `recording_stopped.data` → `filename`, `url`, `platform`, `variants_expected`, `variants`.
- WS `video_variant_status.data` → `original_filename`, `label`, `state`, `progress`, `filename`, `url`, `reason`, `message`, `updated_at`, con los `null` explícitos (hub sin cambios, DA10).
- WS `video_variant_ready.data` → `original_filename`, `label`, `filename`, `url` (sin cambios).
- `GET /video-postprocess/status` → `items[].original_filename`, `items[].known`, `items[].variants[]`, con los `null` omitidos (HTTP `WhenWritingNull`).
- `GET /agent/state` → `background_operations[]` con `kind`, `since` (`detail` omitido). `operations` ya no trae `video_postprocess`.
- `GET /health` → `capabilities` incluye `video_postprocess_v1`.
- Valores de `state`/`reason`/`label`: exactamente los de §4.3/§9.

## Pendiente para el usuario (§11.4)

1. iPhone real en Mac, 12 min "Solo pantalla": se guarda enseguida y la versión suavizada aparece después.
2. AirPlay y `on_device` reales sin indicador.
3. **Windows: guardar sin `file_busy` mientras corre la variante.** Es lo único del mecanismo DA1 (handle `FileShare.Read|Delete` + copia) que no se puede probar en la Mac. El test `MoverConElHandleAbierto_NoTira` es el que importa en Windows: conviene correr la suite ahí.
4. RIFE, si se instala `rife-ncnn-vulkan`.
5. Orden de despliegue (DA2): primero la web, después la release de Tatana.
