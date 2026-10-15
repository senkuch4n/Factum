# SDD: Video de la recolección completa (pantalla de punta a punta y, opcional, cámara del procedimiento) con hash y sello por tramo

**Slug:** `grabacion-sesion-completa` · **Issue:** #80 · **HU:** `docs/hu-grabacion-sesion-completa.md` (D1–D13
validadas con la opción recomendada el 2026-10-14) · **Autor:** architect · **Fecha:** 2026-10-14 · **Rama:**
`feat/grabacion-sesion-completa` (sobre `develop` con #78 y #79)

El código se cita por **símbolo**. Los números de línea son una pista y pueden haber cambiado.

---

## 0. Decisiones que necesitan al usuario antes de implementar

Las mediciones de §4 cambian dos supuestos de la HU. Hay cuatro decisiones que no puede tomar la SDD. Cada una tiene la
opción recomendada; el resto del documento está escrito asumiendo la recomendada y marca con **[DU-n]** lo que cambia
si se elige otra.

**DU1. Partir la HU en dos fases.** La HU toca tres fuentes de video (scrcpy, ffmpeg con cámara y DVT de iPhone), cada
una con su forma de segmentar y de recuperarse. La recomendación es hacerla en dos ciclos de implementación + revisión
con esta misma SDD:
- **F1 (este ciclo):** toda la infraestructura (diario de sesión, tramos, huecos, marcas, recuperación tras cierre,
  reconexión, espacio, ZIP con `sesion/`, modelo y eventos del backend, sello, informe, web) **con la pantalla de
  Android**. Es lo único que se pudo verificar de punta a punta con hardware real (§4).
- **F2 (ciclo siguiente, sin SDD nueva):** cámara del procedimiento (ffmpeg en macOS/Windows) e iPhone (DVT). El
  contrato de F1 ya trae los campos de pista (`track`), plataforma y cámara, así que F2 no cambia el contrato
  compartido: solo suma dos capabilities.
- *Alternativa:* todo en un ciclo. Es más riesgoso: la cámara y el iPhone no se pudieron probar en vivo (no hay iPhone
  conectado y no se abrió la cámara de la Mac por privacidad).
- **Recomendada: partir.** F1 ya entrega el valor central de la HU (el video continuo del celular con hash y sello) y
  deja F2 sin riesgo de rehacer el contrato.

**DU2. Cómo se corta cada 30 min sin perder cuadros (Android).** scrcpy 4.0 no segmenta solo (§4.2).
- **A) Corte solapado.** El tramo nuevo arranca, y recién cuando el celular confirma que está capturando se cierra el
  anterior. Medido en el SM-M127F: dos scrcpy graban a la vez sin problema (video + audio), el solapamiento queda en
  **~0,7 s** y no se pierde ningún cuadro. Costo: scrcpy restaura "Pantalla siempre encendida" (`--stay-awake`) cuando
  se cierra **cada** instancia, así que con instancias solapadas los tramos pares quedan sin esa opción (medido: §4.2.4).
  Para evitarlo, Tatana vuelve a poner el mismo valor que pone scrcpy (`stay_on_while_plugged_in = 7`) después de cada
  corte y restaura el valor original al terminar la sesión. Es la misma escritura que ya hace scrcpy hoy, pero si
  Tatana se cae y el celular no se vuelve a conectar a esa PC, el ajuste queda en 7 hasta que se lo cambie a mano.
- **B) Corte secuencial.** Se cierra el tramo y se abre el siguiente. Medido: **0,84–0,87 s sin video** en cada corte
  (cierre 0,14 s + arranque hasta el primer cuadro 0,72 s). Ese microhueco queda registrado como hueco con motivo
  `rotacion` y aparece en el informe. No hay escrituras extra en el celular.
- **Recomendada: A.** El objetivo de la HU es que la contraparte no pueda alegar que algo se omitió entre tramos. Un
  segundo sin video cada media hora es justamente eso. La escritura extra es idéntica a la que scrcpy ya hace, y queda
  acotada (se restaura al terminar o en la recuperación).

**DU3. Cierre de Tatana desde su ventana (`agent-ui/`).** La HU dejaba `agent-ui/` sin cambios. Al salir, Electron
mata a Tatana sin aviso (`AgentProcessManager.stop()` → `process.kill()`). En Windows es un `TerminateProcess`, que no
se puede interceptar. scrcpy sobrevive como proceso huérfano y Tatana lo cierra bien al volver a abrirse (§4.6), pero
mientras tanto sigue grabando sin dueño.
- **A) Cambio chico en `agent-ui/`.** Si hay una grabación de sesión en curso, al cerrar o reiniciar Tatana aparece un
  diálogo ("Hay una grabación de la sesión en curso. Si cerrás Tatana, se corta y queda registrado el hueco."). Si se
  confirma, Electron llama a `POST /agent/prepare-shutdown`, que cierra y guarda el tramo, y recién después mata el
  proceso.
- **B) Sin cambios en `agent-ui/`.** Se confía solo en la recuperación al reabrir.
- **Recomendada: A.** Es la salida más común ("cerré Tatana sin querer") y con A el tramo queda guardado limpio en el
  momento.

**DU4. AirPlay en la grabación de sesión (cambia D12).** D12 = A incluía AirPlay en Mac. Al revisar el código
(`IosService.TryStartAirplayRecorderAsync`), uxplay:
- no se puede cortar en tramos sin que el perito vuelva a tocar "Duplicar pantalla" en el iPhone cada 30 min;
- escribe un MP4 que no se recupera si se cierra de golpe;
- y su corrección de audio (`FixAirplaySyncAsync`) **recodifica** el audio, lo que contradice "sin recodificar".
- **A) iPhone solo con DVT (`video_only`)** en la sesión, en F2. AirPlay sigue disponible para clips puntuales, como hoy.
- **B) AirPlay como un único archivo sin tramos**, con los riesgos de arriba.
- **Recomendada: A.**

---

## 1. Resumen funcional

En el paso 3 del asistente, Factum graba en video la pantalla del celular durante toda la recolección. La grabación
arranca sola al entrar al paso (después del chequeo de pantalla de #73) y termina al salir. Se guarda en **tramos de
30 min** (configurable por instalación). Cada tramo, apenas se cierra, se valida, se mueve a la carpeta del caso, se le
calcula el SHA-256, se registra en el expediente como evidencia y se sella (#78). Los cortes quedan como **huecos** con
hora y motivo:
- salir del paso;
- retomar otro día;
- desconexión del USB, con reanudación automática al reconectar;
- cierre de Tatana, con recuperación del tramo al reabrir;
- falta de espacio, con corte limpio antes de llenar el disco;
- detención por el perito.

"Grabar" pasa a ser **"Marcar momento"**, y cada captura de pantalla deja una marca automática con el tramo y el
segundo del video. Al generar, los tramos van **dentro del ZIP cifrado**, en `sesion/`. ZIP64 + AES quedó verificado
con 5 y 10 GiB. El informe suma la sección condicional **"Registro audiovisual de la recolección"** con:
- la tabla de tramos (archivo, pista, inicio, fin, duración, resultado, SHA-256 y sello);
- los huecos;
- las marcas;
- la correlación con el reloj del celular (#79).

Tatana es la fuente de verdad mientras se graba: lleva un **diario en disco** por caso que sobrevive a recargas y
reinicios. La web lo replica al backend, que guarda una copia validada y append-only en `Case.RecordingSession`. Con un
Tatana sin la capability `recording_session_v1`, la web no ofrece la función y avisa que hay que actualizar Tatana. F2
suma la cámara del procedimiento (ffmpeg, en archivos aparte con los mismos cortes) y el iPhone en modo DVT.

## 2. Toca

| Parte | ¿Toca? | Qué |
|---|---|---|
| **backend (Tatana)** `server/src/Factum.Agent` | **sí** | Nuevo `Services/Session/*`: `SessionRecorder`, `SessionJournal`, `SessionRecovery`, `AndroidScreenTrack`, `StayAwakeKeeper`, `SessionDiskBudget`, `SessionNames`, `SessionModels`, y en F2 `CameraTrack`, `CameraDevices`, `IosScreenTrack`. Nuevos controllers `CaseSessionController.cs`, `SessionCapacityController.cs` y, en F2, `CamerasController.cs`. Cambios en `AgentStateController` (`recording_session` y `POST /agent/prepare-shutdown`), `HealthController` (capabilities), `RecordingController` (409 `session_active`), `ScreenshotController` (marca automática), `CaseEvidenceController`/`CaseEvidenceStore` (prefijo `sesion_` reservado y tramos protegidos), `CaseZipService` (`zip_folder`, limpieza del diario al confirmar), `Evidence/EvidenceZip.cs` (nombres de entrada con carpeta), `Models/AgentModels.cs` (`SessionOptions`), `Common/AgentErrorCodes.cs` y `Program.cs` (DI, recuperación al arrancar, hook del sondeo de dispositivos, `ApplicationStopping`). En F2 también `Services/Ios/ios_helper.py` (`rotate`/`snap`), `DvtRecorder.cs`, `IosService.cs` y `MicCapture.cs` (`DshowDevices.ParseVideoDevices`). Tests en `server/tests/Factum.Agent.Tests`. |
| **backend (API)** `server/src/Factum.Backend` | **sí** | `Models/RecordingSession.cs` (nuevo), `Models/Case.cs` (`RecordingSession`), `Models/CaseEvent.cs` (5 tipos + campos de detalle), `DTOs/RecordingSessionDtos.cs` (nuevo), `DTOs/EvidenceDtos.cs` (`ManifestFileDto.ZipFolder`), `DTOs/ConfigDtos.cs`, `Controllers/CasesController.cs` (2 rutas), `Controllers/ConfigController.cs`, `Controllers/CaseTimestampsController.cs` (LEEME con `sesion/`), `Services/Cases/RecordingSessionRules.cs` y `RecordingSessionFlow.cs` (nuevos), `Services/Cases/AgentGeneration.cs` (guardia de prepare y `zip_folder`), `Services/Cases/CaseService.cs` (guardia de borrado), `Services/Cases/EvidenceManifest.cs`, `Services/Reports/ReportService.cs`, `ReportValues.cs`, `ReportSessionTexts.cs` (nuevo), `Services/Reports/EvidenceZip.cs` (paridad con Tatana), `Services/Timestamping/TimestampReadme.cs`, `Services/Recording/RecordingSessionOptions.cs` (nuevo), `Infrastructure/MongoRepository.cs` (2 escrituras + proyección del listado), `Templates/plantilla_informe_v{4,6}.docx` (regeneradas con los scripts), `appsettings.json`. Tests en `server/tests/Factum.Backend.Tests/RecordingSession/`. |
| **scripts de plantilla** `ops/plantilla/` | **sí** | `build_plantilla_v4.py` (bloque `{#registroAudiovisual}` con la segunda tabla), `build_plantilla_v6.py` (estilo de la segunda tabla y chequeo), `README.md`. |
| **client** `client/` | **sí** | `lib/agent.ts`, `lib/api.ts`, `lib/recording-session.ts` (nuevo), `hooks/useRecordingSession.ts` (nuevo), `hooks/usePublicConfig.ts`, `hooks/useFileManager.ts` (alta de tramos ya hasheados), `app/dashboard/page.tsx`, `components/CaptureStep.tsx`, `components/capture/EvidenceTray.tsx` (grupo "Sesión"), `components/capture/SessionRecordingCard.tsx`, `SessionIndicator.tsx`, `MarkMomentPopover.tsx` (nuevos), `components/dashboard/GenerateStep.tsx`, `components/ResultStep.tsx`, `components/case-detail/CaseDetailContent.tsx`, `components/case-detail/SessionRecordPanel.tsx` (nuevo), `lib/case-events.ts`, `lib/agent-messages.ts`. |
| **agent-ui** `agent-ui/` | **sí [DU3=A]** | F1: `src/main/index.ts` (diálogo al cerrar o reiniciar) y `src/main/agent-api.ts` (`prepareShutdown`). F2: `package.json` → `build.mac.extendInfo` (`NSCameraUsageDescription`, `NSMicrophoneUsageDescription`). |
| **deploy / docs** | **sí** (lo hace `implementer-backend`) | `deploy/cloud/docker-compose.yml`, `deploy/cloud/factum.env.example`, `deploy/windows/docker-compose.yml`, `deploy/windows/.env.example`, `deploy/windows/scripts/restaurar.ps1`, `deploy/windows/tests/comun.Tests.ps1`, `README.md`, `AGENTE_TATANA.md` (casos de uso nuevos). |

**Reparto.**
- `implementer-backend`: §15.1 (F1) y §15.3 (F2) — Tatana, API, plantillas, deploy y docs.
- `implementer-frontend`: §15.2 (F1) y §15.4 (F2) — `client/` y `agent-ui/`.

**Orden.** Los dos lados pueden avanzar en paralelo. La web tolera un Tatana sin `recording_session_v1` y un backend
sin las rutas nuevas (§7.8). La prueba manual (§16.2) necesita los tres y un Tatana compilado desde esta rama.

---

## 3. Arqueología verificada (sobre `6dfffe5`, `develop` con #78 y #79)

- **Serialización.**
  - **Backend:** `JsonNamingPolicy.SnakeCaseLower` y enums como string. **Los `null` sí viajan** (no hay
    `WhenWritingNull` global).
  - **Tatana, HTTP:** snake_case_lower con `WhenWritingNull`: un `null` **no viaja**.
  - **Tatana, WebSocket:** `AgentWebSocketHub.JsonOpts` usa solo `SnakeCaseLower`: ahí **los `null` viajan como
    `null`**.
  - **Mongo:** sin convenciones, PascalCase. Toda clase persistida lleva `[BsonIgnoreExtraElements]`.
- **Grabación Android de hoy** (`AdbService`).
  - Un solo `_snap` y `_recordLock`; `BuildScrcpyArgs` (puro, testeable).
  - Marcador de inicio real con `ScrcpyOutput.IsStartMarker`.
  - Cierre `ProcessStop.StopAsync`: SIGINT, escalado a SIGTERM o Ctrl+C con el helper `--ctrl-c <pid>` en Windows.
  - `RemuxAsync` tras un kill y `RecordingValidation.Evaluate` (ok/incomplete/empty/unreadable). Los ilegibles van a
    `<DataDirectory>/descartes/`.
  - El scrcpy de esta grabación **no se reutiliza**: la sesión tiene su propio servicio y reutiliza estos helpers
    estáticos (D-T2).
- **Estado recuperable.**
  - `GET /agent/state` arma `recordings[]` con `IRecordingStateSource` y `unclaimed_recordings[]` desde
    `RecordingRegistry` (en memoria).
  - `IOperationSource.ActiveOperations` bloquea el mantenimiento (auto-update) mientras hay algo en curso.
- **Carpeta del caso.**
  - `CaseEvidenceStore`: `<DataDirectory>/cases/<id>/`, con archivos planos.
  - `List()` usa `Directory.EnumerateFiles`, así que **no ve subcarpetas**.
  - `ImportAsync` mueve desde la raíz y hashea. `Delete` borra cualquier nombre válido.
  - `IAgentDiskProbe` mide el espacio con fail-open.
- **ZIP.**
  - `CaseZipService.BuildAsync` verifica tamaño y hash de cada archivo y escribe con
    `EvidenceZip.WriteAsync(zip, paths, password, ct, progress)`. Las **entradas son planas** (`Path.GetFileName`).
  - `UseZip64.Dynamic` con `Size` declarado. `VerifyAsync` lee síncrono (nota de #58 / `zip-verificacion-stored`) y
    compara por nombre de entrada.
  - El espacio se prechequea con `total + MinFreeBytes` sobre la carpeta final.
  - El backend tiene una **copia** de `EvidenceZip` (`Services/Reports/EvidenceZip.cs`) que se mantiene en paridad.
- **Generación agent.**
  - `prepare` devuelve `files: ManifestFileDto[]`. El cliente los pasa **tal cual** a `agent.buildZip` (`page.tsx`,
    `files: prep.files`).
  - `finish` compara `meta.Files` con el manifiesto por nombre, tamaño y hash (`EvidenceManifest.Matches`).
  - `commit` borra los sueltos por nombre.
- **Registro de evidencia.**
  - `PUT /api/cases/{id}/evidence` es idempotente por nombre + SHA-256 y emite `evidence_added`.
  - Después llama a `_timestamps.SealEvidenceAsync` (#78). **Cada tramo se sella por este mismo camino.**
  - El cliente registra con `useFileManager.saveOne`. Un archivo con `storedInCase && sha256 && size` no se reimporta:
    se registra directo.
  - `restoreCaseEvidence` registra los "extras" que están en la carpeta del caso y no en el manifiesto.
- **Informe.** `ReportService.GenerateDocxAsync`, en este orden:
  1. B-R1 borra los placeholders desconocidos (`KnownPlaceholders`).
  2. B-R2 resuelve los bloques `{#k}`/`{/k}` (`BlockKeys`).
  3. B-R4 resuelve los textos multilínea (`ReplaceParagraphPerLine`).
  4. B-R5 arma la tabla de hashes: `FillHashTable`, que busca la fila con `{nombreArchivo}` y la clona con
     `BuildHashRow`. **`BuildHashRow` tiene los placeholders fijos.**
  5. B-R6 reemplaza el texto.

  Además: `ReportValues.Counts` alimenta los defaults de "Operaciones realizadas", y
  `ReportTimestampTexts.Block(info, files, zone)` arma el párrafo del sello.
- **Plantillas.**
  - `build_plantilla_v4.py` insertó `{#selloTiempo}` justo después de la tabla de hashes con `marker(...)`.
  - `build_plantilla_v6.py` ubica la tabla de hashes como **la única** con `tblStyle w:val="17"` y la reestila con
    `hash_table()`. `hash_table()` decide la fuente Courier por el texto `{hashArchivo}`.
- **Ficha y reloj (#79).** `Case.DeviceDetails.ClockStart/ClockClose.DeviceMinusPcMs/MarginMs` y
  `ReportDeviceTexts.DeviceSkewPast(...)` ("el dispositivo adelantaba 10 s respecto de la estación de trabajo (± 1 s)").
- **Config pública.** `PublicConfigResponse` agrega campos al final (#79 dejó `clock_skew_warning_seconds` último; el
  test `TatanaMinVersionTests` fija el orden).
- **Cierre de Tatana desde Electron.**
  - `agent-ui/src/main/index.ts`: `will-quit` → `agentProcess.stop()` → `process.kill()`, sin esperar.
  - "Reiniciar" → `stop()` + `start()`.
  - Solo el camino de actualización usa `stopAndWait` (`taskkill /T /F`). Ese camino nunca corre con una sesión
    activa, porque la sesión es una operación en curso.
- **Webcam hoy.** `WebcamService` usa `"Integrated Camera"` fijo, invoca `ffmpeg` por nombre (sin `ToolResolver`) y
  arma los argumentos como string. `agent.captureWebcam` no tiene usos. **No se toca en F1.** F2 lo deja como está
  (D-T24).
- **Releases de Tatana.** El último tag es `tatana-v1.6.0`, sin #78/#79. Esta HU sale en la próxima release junto con
  ellas, o en la siguiente (§12.4).

---

## 4. Verificaciones técnicas (con evidencia real)

Entorno: MacBook Air (Apple Silicon, macOS 26, APFS en NVMe) con:
- scrcpy 4.0 y ffmpeg 8.1.2 de Homebrew, con `libx264` y `h264_videotoolbox`;
- adb 37.0.0 (el mismo servidor que usa el Tatana de :8765, que **no** se tocó);
- Samsung SM-M127F (`R58RA3R5AGA`, Android 11).

Todo se grabó en el scratchpad de la sesión. Del teléfono solo se leyó. La única escritura fue la de scrcpy con
`--stay-awake`, que es el comportamiento actual de Factum. Al terminar quedó en su valor original (`0`). Los
archivos sintéticos de 5 y 10 GiB se borraron.

### 4.1 ZIP64 con AES-256 (D9)

Arnés: copia exacta de `Factum.Agent/Services/Evidence/EvidenceZip.cs` + SharpZipLib 1.4.2, compilada en Release.

| Prueba | Escritura (`WriteAsync`) | Verificación (`VerifyAsync`) | 7-Zip (`7zz t -p…`) |
|---|---|---|---|
| 1 entrada de 5 GiB + 12 KB (`.mkv`, Stored) | **24,1 s** | **46,6 s** (OK) | `Everything is Ok`; `Characteristics = Zip64 WzAES : Encrypt Descriptor UTF8`, `Method = AES-256 Store` |
| 10 entradas de 1 GiB **bajo `sesion/`** (nombre de entrada con carpeta) | **75,7 s** | **125,6 s** (OK) | `Everything is Ok` (8,6 s); `bsdtar -tf` lista `sesion/sesion_pantalla_001.mkv`… |

Conclusiones:
- `UseZip64.Dynamic` + `Size` declarado **sí** escribe entradas ZIP64 con AES-256 (AE-2) de más de 4 GB.
- `VerifyAsync` (lectura síncrona por bloques) las lee bien. 7-Zip las abre.
- Un nombre de entrada con `/` crea la carpeta `sesion/` dentro del ZIP sin ningún cambio en el formato. **D9 = A se
  confirma.** La caída a C (segundo ZIP) no hace falta.
- **Tiempos para 10 GiB en esta Mac:** ~3,5 min entre el rehash previo, la escritura y la verificación. Con progreso
  (`zip_progress` ya existe). En un disco mecánico puede ser 3–5 veces más: lo dice el texto de la web (§9.6) y queda
  en riesgos.
- La verificación tarda ~1,7× la escritura por la lectura síncrona (decisión de #58). No se cambia.

### 4.2 scrcpy 4.0: tramos sin perder cuadros (D4, D7)

Comando: el de `AdbService.BuildScrcpyArgs` (`--record … --record-format mkv --no-window --max-size 1080
--video-bit-rate 4M --audio-source=output --stay-awake`).

**4.2.1 Arranque y cierre.**
- Marcador `[server] INFO: Device: …` a los **0,70–0,76 s** del lanzamiento.
- Cierre con SIGINT en **0,13–0,18 s** y salida 0.

**4.2.2 Dos instancias a la vez (solapamiento).**
- A arranca. A los 6 s arranca B (marcador a los 7,40 s). A los 8,08 s se le manda SIGINT a A: **solapamiento de
  0,68 s**. B sigue 5 s más.
- Los dos MKV salen válidos, con video H.264 y audio Opus.
- Paquetes de audio: A 336 (≈ 6,7 s, último pts 6,92 s) y B 265 (≈ 5,3 s). **La captura de audio interno convive**
  en Android 11 (este Samsung).
- No hubo avisos de scrcpy.
- El segundo `push` del `scrcpy-server.jar` no afectó a la instancia en curso.

**4.2.3 Reinicio secuencial (alternativa B de DU2).** Tres cortes: cierre 0,134–0,137 s + arranque hasta el marcador
0,706–0,731 s = **0,84–0,87 s** sin video.

**4.2.4 "Pantalla siempre encendida" con instancias solapadas.** `settings get global stay_on_while_plugged_in`:

| Momento | Valor |
|---|---|
| antes | `0` |
| A grabando | `7` |
| A + B | `7` |
| A detenido, B grabando | **`0`** ← A restauró el original con B todavía grabando |
| B + C | `7` |
| B detenido, C grabando | `7` |
| todo detenido | `0` |

El servidor de scrcpy guarda el valor anterior **solo si lo cambió**. B y las instancias pares no lo cambian. Con el
corte solapado, los tramos pares quedan sin `--stay-awake` desde que se cierra el impar. De ahí el manejo de §5.5.

**4.2.5 Escritura a disco durante la grabación (dato clave para la recuperación).**
- Con la pantalla casi quieta, el MKV quedó en **0 bytes durante los primeros 60–70 s**. Después creció de a bloques
  de **262 144 bytes** (256 KiB). En 180 s había 256 KiB en disco y el archivo final pesó 357 KB.
- scrcpy acumula en memoria hasta llenar un bloque de 256 KiB (búfer de escritura de libavformat). No hay opción de
  scrcpy para cambiarlo.
- **kill -9** a los 12 s y a los 40 s: archivo de 0 B, sin cabecera EBML, irrecuperable.
- **kill -9** a los 64 s, con 256 KiB ya escritos: `ffprobe` no da duración, pero `ffmpeg -c copy` (el `RemuxAsync`
  de hoy) recupera **49,2 s** de ~63 s.
- **Conclusión:** si **scrcpy** muere de golpe (corte de luz, cuelgue de la PC, kill del propio scrcpy), se pierde lo
  que no llegó a disco: hasta 256 KiB. Eso es menos de 1 s con movimiento a 4 Mb/s, pero pueden ser minutos de
  pantalla quieta. Con el teléfono quieto desde el inicio del tramo puede perderse todo el tramo. Queda en riesgos (§18)
  y en el texto del informe del tramo recuperado.

**4.2.6 Tatana muere pero scrcpy no (caso más común: Tatana cerrado o colgado).**
- Se mató con `kill -9` al proceso padre de un scrcpy.
- scrcpy **siguió vivo** (PPID 1) y grabando.
- Desde otro proceso se le mandó SIGINT: cerró en ≤ 0,5 s y dejó un MKV válido de **88,2 s** con video y audio.
- Esto es lo que hace la recuperación de §5.10. Cuando se cae solo Tatana, el tramo se recupera **completo** y sin
  remux.
- En Windows el mecanismo es el mismo: `WindowsConsoleSignal --ctrl-c <pid>` funciona con cualquier PID que tenga su
  propia consola. Queda a confirmar en Windows en la prueba manual (§16.2, paso 14).

### 4.3 `screencap` en paralelo con scrcpy grabando (D6)

- Tres `adb exec-out screencap -p` durante una grabación: **2 552–2 563 ms** cada uno. Sin scrcpy: 2 530–2 540 ms.
- Las tres capturas salieron como PNG válidos de 720×1600 y ~1,99 MB.
- scrcpy siguió grabando sin avisos y el MKV salió válido (12,99 s).
- **Se confirma: las capturas durante la sesión en Android funcionan como hoy.**

### 4.4 iPhone: DVT de video y captura (D12)

- No hay iPhone conectado, así que la convivencia de dos canales DVT (el grabador `record` y el subcomando
  `screenshot`) **no se pudo verificar**.
- Por eso la SDD **no depende de esa convivencia** (D-T9). Durante la sesión, la captura de pantalla del iPhone la
  entrega el propio grabador: el helper guarda el último PNG que recibió de DVT, que es una captura a resolución
  completa de `com.apple.instruments.server.services.screenshot`, el mismo origen que `screenshot`. Esa captura es,
  además, un cuadro del video.
- El helper suma dos órdenes por stdin, `snap` y `rotate`. Las dos se prueban sin iPhone con `--synthetic`, como los
  tests actuales de `DvtRecorderTests`.
- Recuperación: si Tatana muere, el helper recibe EOF en stdin. Hoy ya interpreta EOF como "stop": cierra ffmpeg y el
  archivo. Con salida MKV + `flush_packets` (§4.5), un cierre de golpe del helper pierde ≤ ~2 s.

### 4.5 Cámara con ffmpeg (D8)

- **Listado en macOS** (salida real de esta Mac, sin abrir la cámara): `ffmpeg -f avfoundation -list_devices true -i
  ""` lista `[0] Cámara de la MacBook Air`, `[1] Cámara de iPhone de …`, `[2] Cámara de Vista del Escritorio de iPhone
  de …`, `[3] Cámara de Vista del Escritorio de la MacBook Air`, `[4] Capture screen 0` y `[5] Capture screen 1`, y
  después los dispositivos de audio. Las entradas **`Capture screen N` graban el escritorio de la PC**: se excluyen
  (fuera de alcance y riesgo de privacidad). El índice puede cambiar entre listados, así que la cámara se elige **por
  nombre** (avfoundation acepta `-i "<nombre>"`).
- **Listado en Windows:** `ffmpeg -list_devices true -f dshow -i dummy`. El formato ya está cubierto por
  `DshowDevices` (fixtures reales en `DshowDevicesTests` con `"Integrated Webcam" (video)` + `Alternative name
  "@device_pnp_…"`). Se suma `ParseVideoDevices` y se usa el *alternative name* como id estable.
- **Codificación.**
  - Windows: el ffmpeg de Tatana es el build BtbN win64 GPL (`progress/impl_backend_grabacion-android-windows.md`),
    con `libx264` y `dshow`.
  - Mac: `libx264` presente.
  - En los dos: `libx264 -preset veryfast -crf 26`, 1280×720 a 15 fps, MKV.
- **Tramos sin hueco y alineados: segment muxer de ffmpeg.** Se probó con `lavfi testsrc2` en lugar de la cámara.
  - `-f segment -segment_time N -segment_format matroska -segment_list lista.csv -segment_list_type csv
    -segment_list_flags +live -reset_timestamps 1 -force_key_frames "expr:gte(t,n_forced*N)"`.
  - `lista.csv` agrega una línea **en el momento en que cada tramo queda cerrado** (`cam_004.mkv,0.000000,10.133000`,
    después `cam_005.mkv,10.133000,20.133000`).
  - Cada tramo es un MKV válido e independiente.
  - Una segunda salida `-update 1 preview.jpg` (1 cuadro cada 2 s, 320 px) actualiza la vista previa sin abrir la
    cámara dos veces.
- **kill -9 de la cámara.** Sin opciones extra, el tramo abierto quedó en 0 B. Con
  `-segment_format_options flush_packets=1:cluster_time_limit=2000`, el tramo abierto crece en disco de forma continua.
  Tras un kill -9 a ~4 s, el remux recuperó **2,0 s**: la pérdida queda acotada a ~2 s.
- **No verificado** (no se abrió la cámara por privacidad):
  - permisos de macOS: Tatana lanzado por Electron necesita `NSCameraUsageDescription` en el `Info.plist` de la app,
    que hoy no está, al igual que `NSMicrophoneUsageDescription`;
  - qué tamaños y fps aceptan las cámaras reales. Se resuelve con un reintento sin `-video_size`/`-framerate` (§5.16).

  Las dos cosas quedan para la prueba manual de F2.

### 4.6 Recuperación de tramos interrumpidos

| Qué murió | Qué queda en disco | Cómo se recupera |
|---|---|---|
| Solo Tatana (cierre de golpe, crash, Electron que lo mata) | scrcpy sigue vivo y grabando (§4.2.6); el helper DVT se cierra solo por EOF; el ffmpeg de la cámara sigue vivo | Al reabrir, Tatana lee el diario, encuentra el PID, verifica que sea el mismo proceso (nombre y hora de inicio) y lo cierra limpio (SIGINT o Ctrl+C). El tramo queda **completo**. |
| scrcpy o ffmpeg muertos de golpe (corte de luz, cuelgue, kill) | MKV truncado: Android pierde hasta 256 KiB; cámara e iPhone, ≤ ~2 s | Remux (`ffmpeg -c copy`, el `RemuxAsync` de hoy). Si sigue ilegible o pesa 0 B: el archivo va a `descartes/` y el hueco se marca `segment_lost: true`. |
| Todo, con el tramo ya cerrado pero sin mover o hashear | El MKV completo en staging | Se valida, se mueve y se hashea. |

En los tres casos el tramo recuperado queda en la carpeta **del caso que lo inició**: el diario vive en
`<DataDirectory>/sessions/<caseId>/`. Se marca `recovered: true` y se abre un hueco `tatana_cerrado`. La sesión **no**
se reanuda sola (D7): se reanuda cuando el perito vuelve al paso 3.

---

## 5. Diseño en Tatana

### 5.1 Componentes (`Services/Session/`)

| Tipo | Responsabilidad | Fase |
|---|---|---|
| `SessionRecorder` (singleton, `IOperationSource`) | Única sesión activa por Tatana: estados, temporizador de cortes, cola de cierre de tramos, marcas, avisos, `ShutdownAsync`. Expone `State`, `IsActiveOn(serial)`, `ActiveCaseId`. | F1 |
| `SessionJournal` | Lee y escribe `<DataDirectory>/sessions/<caseId>/sesion.json` de forma atómica (`.tmp` + `Flush(true)` + `File.Move(overwrite)`). Un lock por caso. | F1 |
| `SessionRecovery` | Al arrancar: recorre los diarios, cierra procesos huérfanos, valida, remuxea, mueve, hashea y abre el hueco `tatana_cerrado`. | F1 |
| `AndroidScreenTrack` | Un tramo = un proceso scrcpy (`BuildScrcpyArgs` de `AdbService`). Corte solapado [DU2]. | F1 |
| `StayAwakeKeeper` | Valor original / reafirmar / restaurar `stay_on_while_plugged_in` [DU2=A]. | F1 |
| `SessionDiskBudget` (puro) | Requisito para empezar, minutos restantes, umbrales de aviso y de corte. | F1 |
| `SessionNames` (puro) | Nombres de archivo, regex y prefijo reservado. | F1 |
| `SessionModels` | DTOs del diario y del estado (§7.2). | F1 |
| `CameraTrack`, `CameraDevices` | ffmpeg con segment muxer, lista de cámaras y vista previa. | F2 |
| `IosScreenTrack` | `DvtRecorder` con `rotate`/`snap`. | F2 |

Sin herencia rebuscada: `ISessionTrack` con `StartAsync(index, stagingPath)`, `RotateAsync(nextIndex, nextPath)`,
`StopAsync(reason)`, `Exited` (evento) y `Pid`/`ProcessStartedAt`. La pista Android y la de iOS implementan "rotar"
distinto; la cámara rota sola (segment muxer) y avisa por `lista.csv`.

### 5.2 Rutas en disco

```
<DataDirectory>/sessions/<caseId>/sesion.json         diario (§7.2.6)
<DataDirectory>/sessions/<caseId>/<nombre final>.mkv  tramo en curso (staging)
<DataDirectory>/sessions/<caseId>/camara.csv          lista del segment muxer (F2)
<DataDirectory>/sessions/<caseId>/camara-preview.jpg  vista previa (F2)
<DataDirectory>/cases/<caseId>/<nombre final>.mkv     tramo cerrado (rename, mismo volumen)
<DataDirectory>/descartes/<nombre>                    tramo ilegible (como #61)
```

- `<caseId>` es el canónico de `AgentFileNames.TryParseCaseId`.
- `GET /cases/{id}/files` no ve `sessions/`, así que un tramo en curso **nunca** aparece como "extra" en
  `restoreCaseEvidence`.
- **Nombre final:** `sesion_<pista>_<NNN>_<yyyyMMdd>_<HHmmss>.mkv`.
  - `<pista>` ∈ `pantalla` | `camara`.
  - `NNN` es el número de tramo de esa pista, con 3 dígitos mínimo, correlativo en todo el caso (sigue al retomar).
  - Fecha y hora son las locales de la PC al iniciar el tramo.
  - Regex: `^sesion_(pantalla|camara)_\d{3,4}_\d{8}_\d{6}\.mkv$` (`SessionNames.IsSegmentName`).
- **Prefijo reservado:** `CaseEvidenceStore.RequireNewName` (subida, `upload-check`, `import`) rechaza nombres que
  empiecen con `sesion_` con 400 `invalid_filename` y el texto "El prefijo «sesion_» está reservado para la grabación
  de la sesión". Solo la sesión crea esos archivos, por rename interno.

### 5.3 Estados de la sesión

```
                 start ok                       corte por tiempo (cada segment_minutes)
idle ──start──▶ starting ──────▶ recording ◀──────────────▶ rotating
                   │ falla           │  │  └─scrcpy terminó y el celular no está──▶ waiting_device ──reconectó──▶ starting
                   ▼                 │  └─stop(reason) / sin_espacio──▶ stopping ──▶ stopped
                 (409/504/507)       └─Tatana se cierra──▶ (diario queda "recording") ──recuperación──▶ interrupted
```

- `stopped`, `interrupted` y `waiting_device` son por caso y quedan en el diario. Volver al paso 3 hace `start`, que
  reanuda en un tramo nuevo.
- **Una sesión activa por Tatana:** estados `starting`, `recording`, `rotating`, `waiting_device` y `stopping`.
  - `start` de **otro** caso o **otro** celular → 409 `session_active`.
  - `start` del mismo caso y el mismo celular en curso → 200 con el estado actual (idempotente: dos pestañas o una
    recarga).
- Con `waiting_device`, otro `start` del mismo caso con el mismo serial conectado reanuda ya, sin esperar.

### 5.4 Arranque y corte de la pista Android

**Start** (`POST /cases/{caseId}/session/start`, §7.2.2), en este orden. Sin depender del `RequestAborted` (como DA4
de #61):
1. Valida el id, el serial, la `platform` (en F1 solo `android`; `ios` → 400 `session_platform_unsupported`) y
   `segment_minutes`. Si `segment_minutes` está fuera de `[Session:MinSegmentMinutes, Session:MaxSegmentMinutes]`, se
   acota y se loguea.
2. Si `AdbService` tiene una grabación puntual en ese serial → 409 `already_recording`. Otra sesión activa → 409
   `session_active`.
3. Espacio (§5.8): no alcanza → 507 `insufficient_storage` con `available_bytes`/`required_bytes`, y el texto "No hay
   espacio en el disco de esta PC para grabar la sesión (quedan X)".
4. Chequeo de pantalla con `AndroidScreenProbe`, salvo `skip_screen_check` o reanudación por reconexión:
   apagada/bloqueada → 409 `screen_off`/`screen_locked` (mismo cuerpo que #73).
5. `StayAwakeKeeper.Begin(serial)` [DU2=A]: si el diario no tiene `stay_awake.original` para ese serial, lee
   `settings get global stay_on_while_plugged_in` y lo guarda. Si `Agent:Recording:StayAwake=false`, se saltea todo
   §5.5.
6. Crea o carga el diario. Si había un hueco abierto en esa pista, lo cierra con `to` = inicio real del tramo nuevo.
   Si el motivo era `cambio_de_paso` y la fecha local de la PC de `from` y de `to` es distinta, el motivo pasa a
   `retomado_otro_dia`.
7. Lanza scrcpy al staging con los args de `BuildScrcpyArgs` (mismo `audioArg`: `--audio-source=output` en Android
   11+, `--no-audio` en < 11; **nunca** el micrófono de la PC, D-T21). Espera el marcador (`StartTimeoutSeconds`).
8. Con el marcador: tramo `recording` en el diario con `process: { pid, started_at (hora de inicio del proceso),
   exe: "scrcpy" }`. Estado `recording`. Programa el corte en `segment_minutes`. Emite `session_state`.

Si algo falla después del paso 7, se corta scrcpy, se borra **solo** el archivo de este intento y el diario no cambia.

**Corte solapado [DU2=A]** (lo dispara un temporizador con reloj monotónico):
1. Estado `rotating`. Lanza el scrcpy del tramo N+1 a su staging.
2. Espera el marcador (`StartTimeoutSeconds`).
   - **Si no llega o scrcpy sale:** se corta ese intento, se borra su archivo y **N sigue grabando**. Aviso
     `rotation_delayed`; se reintenta a los `RotationRetrySeconds` (60). El tramo N queda más largo de lo configurado,
     sin hueco.
3. Con el marcador en t1: tramo N+1 `recording` (`started_at = t1`). SIGINT a N con `ProcessStop.StopAsync`
   (`StopTimeoutSeconds`, `SigtermAfterSeconds`).
   - `N.stopped_at` = hora del pedido de cierre (t2 ≥ t1).
   - `N.overlap_seconds = round(t2 − t1, 1)`.
   - `N.stop_reason = "rotacion"`.
4. `StayAwakeKeeper.Reassert(serial)` (§5.5).
5. Cierre de N en la **cola de cierre** (§5.6), en segundo plano. Nunca bloquea a N+1.
6. Estado `recording`. Programa el próximo corte desde t1.

**Corte secuencial [DU2=B]:** SIGINT a N → lanza N+1 → marcador. Se agrega un hueco `{ track: "pantalla", from:
N.stopped_at, to: N+1.started_at, reason: "rotacion" }`. No hay §5.5.

### 5.5 Pantalla siempre encendida [DU2=A]

- **Diario:** `stay_awake: { serial, original: "0", owned: false }`.
- **`Reassert(serial)`** después de cerrar cada tramo saliente: `settings get global stay_on_while_plugged_in`. Si es
  distinto de `7`, `settings put global stay_on_while_plugged_in 7` y `owned = true`. Es la misma escritura y el mismo
  valor que pone scrcpy (`AC|USB|WIRELESS`).
- **`Restore(serial)`**, al final de la sesión (stop, `sin_espacio`, `prepare-shutdown`, recuperación), después de que
  terminó el último scrcpy:
  - si `owned` y el valor actual es `7` → `settings put … <original>`;
  - si el valor actual no es `7`, alguien lo cambió y no se toca;
  - en los dos casos, `owned = false`.
- **Celular desconectado al terminar:** `restore_pending = true` en el diario. Se restaura cuando el serial vuelve a
  aparecer en el sondeo (§5.7), o en la recuperación si está conectado.
- Comandos con `ProcessRunner.RunArgumentListAsync(adb, ["-s", serial, "shell", script], timeout 5 s)`, como
  `AndroidScreenProbe`. Si falla: log de warning y sigue, nunca corta la sesión.

### 5.6 Cierre de un tramo (cola de cierre, secuencial por sesión)

1. `size` del staging. Si es 0 → `discarded` (borra el archivo de 0 B, como `empty` de #61) y hueco con
   `segment_lost: true` si no hay otro tramo que cubra ese tiempo. En un corte solapado no hace falta: N+1 ya grababa.
2. Si el cierre fue forzado (kill), `RemuxAsync`. Después `FfmpegProbe.ProbeAsync` + `RecordingValidation.Evaluate`
   (`recorded = stopped_at − started_at`, misma tolerancia).
   - `unreadable` → mueve a `descartes/`, `discarded`.
   - `ok`/`incomplete` → sigue.
3. Rename del staging a `<DataDirectory>/cases/<caseId>/<nombre final>` (`overwrite: false`). Si ya existe un archivo
   con ese nombre, cosa que no debería pasar, se agrega `_1`, `_2`… con `AdbService.UniqueDiscardPath` y se loguea.
4. SHA-256 con `CaseEvidenceStore.Sha256Async`.
5. Diario: tramo `saved` con `size`, `sha256`, `duration_seconds`, `validation` y `recovered`.
6. WS `session_segment_closed` y `session_state`.

Si Tatana se cierra entre los pasos, la recuperación retoma desde el estado del diario (`closing`).

### 5.7 Desconexión del USB y otras salidas de scrcpy

- `AndroidScreenTrack` escucha `Process.Exited`. Si scrcpy sale solo:
  - **El serial no está en el último sondeo** (`Program.cs` llama a `sessionRecorder.OnDevicesPolled(serials)` al
    final de cada ciclo de 3 s; si el último sondeo tiene más de 5 s, se consulta `adb -s <serial> get-state`):
    1. Se cierra el tramo (§5.6). Suele quedar `incomplete`, porque el tiempo hasta detectar la salida cuenta como
       grabado.
    2. Hueco abierto `{ reason: "desconexion", from: stopped_at }`.
    3. Estado `waiting_device`, con `stop_reason: "desconexion"`.
    4. `session_state` con `stopped_at`.
  - **El serial sigue conectado:** cierre del tramo y hueco `fallo_grabacion`. Un reintento automático a los
    `ResumeDelaySeconds` (3 s), con el mismo camino que la reconexión. Si falla, estado `stopped` con `stop_reason:
    "fallo_grabacion"`; la web muestra "Reanudar".
- **Reconexión (D7 A):** en `OnDevicesPolled`, si el estado es `waiting_device` y aparece **el mismo serial**, se
  espera `ResumeDelaySeconds` y se hace start interno:
  - sin chequeo de pantalla bloqueante: corre el probe y, si está apagada o bloqueada, suma el aviso;
  - con `resume_reason: "desconexion"`.

  El hueco se cierra con el inicio del tramo nuevo. Un **celular distinto** no reanuda nada.
- **Pantalla apagada o bloqueada durante la sesión (D7):** cada `ScreenProbeSeconds` (30 s) en `recording`,
  `AndroidScreenProbe.GetStateAsync`. Con `off` o `locked` se agrega el aviso `screen_off`/`screen_locked`, y con `on`
  se saca. La grabación **nunca** se corta por esto. Se emite `session_state` solo cuando cambia la lista de avisos.

### 5.8 Espacio en disco (D13)

`SessionDiskBudget` es puro: recibe `available` (`IAgentDiskProbe` sobre `DataDirectory`), `minFree`
(`Agent:MinFreeBytes`), las tasas y `segmentMinutes`.

- **Tasa** `R`:
  - pantalla: max(`Session:ScreenBytesPerMinute` (30 MiB), tasa observada del último tramo cerrado = `size /
    duration`);
  - cámara: `Session:CameraBytesPerMinute` (12 MiB), con el mismo criterio.
  - R = suma de las pistas activas.
- **Para empezar:** `available ≥ minFree + 2 × segmentMinutes × R`. Si no alcanza → 507.
- **Estimación para la web** (`GET /session/capacity`):
  - `estimated_minutes = floor((available − minFree) / (R × k))`;
  - `k = 2` si `EvidenceDirectory` y `DataDirectory` están en el mismo volumen (`Path.GetPathRoot` + `DriveInfo`
    igual), porque el ZIP vuelve a ocupar lo mismo; si no, `k = 1`.
- **Durante la sesión** (cada `DiskCheckSeconds`, 60 s; el mismo tick escribe `last_alive_at` en el diario):
  - `minutes_left = floor((available − minFree) / R)`.
  - Con `minutes_left < Session:WarnMinutesLeft` (60) → aviso `disk_low` con `available_bytes` y `minutes_left`. Se
    reemite cuando cruza un múltiplo de 10 min.
  - Con `available < minFree + R × Session:CutReserveMinutes` (2 min) → corte limpio de todas las pistas con motivo
    `sin_espacio` y estado `stopped`. No se reanuda solo.
- **Antes de generar:** no cambia. `CaseZipService.BuildAsync` ya exige `total + MinFreeBytes` libres en el volumen
  final. Con los sueltos ya escritos, ese es justamente el pico (sueltos + ZIP).

### 5.9 Marcas de momento (D5, D6)

- **Manual:** `POST /cases/{caseId}/session/marks { text? }`.
  - Requiere estado `recording` o `rotating` para ese caso. Si no → 409 `session_not_active`.
  - `text`: hasta 300 caracteres, `TrimEnd`, sin controles; si no cumple → 400 `invalid_request`.
- **Automática de captura:** `ScreenshotController.Take`. Si `SessionRecorder.IsActiveOn(serial)` y `case_id` coincide
  con el de la sesión, antes de llamar a `screencap` toma `at = now` y, si la captura sale bien, agrega una marca
  `kind: "captura"` con `filename` = la captura.
  - La respuesta 200 suma `session_mark` y el evento `screenshot_taken` suma `session_mark` (`null` si no hubo).
  - Si no hay sesión, todo queda exactamente como hoy.
- **Cálculo:**
  - `segment_index` = el tramo de pantalla cuyo intervalo `[started_at, stopped_at ?? ahora)` contiene `at`. Si hay
    dos (solapamiento), el más nuevo.
  - `offset_seconds = round(at − segment.started_at, 1)`.
  - Las marcas son **solo de pantalla**: la cámara tiene los mismos cortes, así que el mismo segundo sirve
    aproximadamente para la cámara (el informe lo aclara).
- `id` = Guid "N". Se guarda en el diario y se emite `session_mark_added`.

### 5.10 Diario y recuperación al arrancar

- **El diario** (§7.2.6) se reescribe entero en cada cambio (tramo, hueco, marca, estado) y en el latido de 60 s
  (`last_alive_at`). Es chico: unos KB, con ≤ 5 000 marcas.
- **`SessionRecovery.RunAsync()`** corre en `Program.cs` después de `CleanupOrphanUploads` y `CleanupOrphanParts`,
  **en segundo plano** (`Task.Run`) para no demorar el arranque. Mientras corre, `/agent/state` informa
  `session_recovery: "running"`. Por cada `sessions/*/sesion.json`:
  1. **JSON ilegible:** se renombra a `sesion.json.ilegible-<yyyyMMdd_HHmmss>` y se loguea un error. No se toca ningún
     MKV (quedan para revisión manual).
  2. **Tramos `recording`/`closing` con `process`:**
     1. `Process.GetProcessById(pid)` vivo, con nombre que contiene `scrcpy` (o `ffmpeg`/`python` en F2) y
        `StartTime` a ±2 s de `process.started_at` → `ProcessStop.StopAsync` (SIGINT o Ctrl+C con el helper,
        `StopTimeoutSeconds`).
     2. Proceso muerto o distinto → no se toca ningún proceso.
  3. **Cada tramo `recording`/`closing`:** se valida y se recupera como en §5.6.
     - `recovered: true`.
     - `stopped_at = started_at + duration_seconds` del probe; sin duración, `last_alive_at`.
     - `stop_reason: "tatana_cerrado"`.
  4. **Hueco abierto:** `{ reason: "tatana_cerrado", from: <stopped_at del último tramo>, segment_lost: <true si el
     tramo se descartó> }`. Si el diario estaba en `waiting_device`, el hueco de `desconexion` sigue abierto tal cual.
  5. **Estado:** `interrupted`, con `stop_reason: "tatana_cerrado"`. No se reanuda sola (D7).
  6. **Pantalla siempre encendida:** `Restore` si el serial está conectado; si no, `restore_pending`.
  7. **Eventos:** WS `session_segment_closed` por tramo recuperado. Los clientes que se conecten después lo ven con
     `GET /cases/{id}/session`.
- **El diario se borra** solo en `CaseZipService.CommitAsync`, después de `DeleteLoose`, y solo si se cumplen las
  tres condiciones:
  - ningún tramo del diario está en `recording`/`closing`;
  - `restore_pending` es false;
  - la carpeta tiene únicamente `sesion.json` y `sesion.json.tmp`.

  Se borran esos dos archivos y la carpeta. Si queda cualquier otra cosa, no se toca nada y se loguea.

### 5.11 Cierre ordenado de Tatana

- `SessionRecorder.ShutdownAsync(reason: "tatana_cerrado", budget: 20 s)`:
  1. corta todas las pistas limpio (SIGINT; stdin `stop` en DVT; `q` en ffmpeg);
  2. corre la cola de cierre (validar, mover, hashear);
  3. abre el hueco `tatana_cerrado`, pasa a `interrupted` y llama a `Restore`.

  Lo que no entra en el presupuesto queda `closing` en el diario y lo termina la recuperación.
- **Lo llaman:**
  - **`POST /agent/prepare-shutdown`** (§7.2.9), solo local (`AgentStateController.IsLocalCaller`), para Electron
    [DU3=A];
  - **`ApplicationStopping`**, en el mismo callback único de `Program.cs` y **antes** del post-proceso y del cierre de
    los WebSocket. Con presupuesto de 15 s, alcanza en macOS/Linux (SIGTERM). Un `TerminateProcess` de Windows no
    pasa por acá: lo cubre la recuperación.

### 5.12 Convivencia con lo que ya existe

| Situación | Comportamiento |
|---|---|
| `POST /devices/{serial}/record/start` con una sesión activa en ese serial | 409 `session_active` "Hay una grabación de la sesión en curso en este celular. Usá «Marcar momento»." No cambia nada más. |
| Sesión pedida con una grabación puntual en curso en ese serial | 409 `already_recording`. |
| Capturas (`screenshot`) | Igual que hoy, más la marca automática (§5.9). |
| `DELETE /cases/{id}/files/{name}` de un tramo del diario | 409 `session_file_protected` "Los tramos de la grabación de la sesión no se pueden borrar: son parte del registro de la recolección." (D-T6) |
| Mantenimiento / auto-update | `ActiveOperations` agrega `AgentOperation("recording_session", since, caseId)` en `starting`/`recording`/`rotating`/`waiting_device`/`stopping`. → `agent_busy`. |
| `/agent/state` | Suma `recording_session` (§7.4). `recordings[]` y `unclaimed_recordings[]` no cambian: la sesión **no** se mezcla con las grabaciones puntuales. |
| ZIP | `zip_folder` (§5.13). |

### 5.13 ZIP con `sesion/`

- `ZipFileItem` suma `ZipFolder` (`zip_folder`).
  - Valores válidos: `null` o `"sesion"`; otro valor → 400 `invalid_filename` con `filename`.
  - Con `"sesion"`, el nombre tiene que cumplir `SessionNames.IsSegmentName`; si no → 400.
- `BuildAsync`:
  - entrada = `zip_folder is null ? filename : "sesion/" + filename`;
  - la unicidad se controla por nombre de entrada;
  - `expected` se arma por nombre de entrada;
  - `paths` pasa a ser una lista de `EvidenceZipEntry(Path, EntryName)`.
- `EvidenceZip` (en **las dos copias**, Tatana y backend):
  - `public sealed record EvidenceZipEntry(string Path, string EntryName)`;
  - `WriteAsync(string zipPath, IReadOnlyList<EvidenceZipEntry> entries, string? password, CancellationToken ct,
    Action<long>? onProgress = null)`. La firma actual con `IReadOnlyList<string>` queda y delega con `EntryName =
    Path.GetFileName(path)`;
  - `internal static bool IsAllowedEntryName(string name)`: nombre plano o `sesion/<nombre plano>`; nada más. Lo
    revisan `WriteAsync` y `VerifyAsync`;
  - `VerifyAsync` no cambia de firma: las claves ya son nombres de entrada.
- `BuildZipResponse.files` devuelve `zip_folder` igual que en el pedido. `commit` y `delete_files` siguen por nombre:
  los sueltos son planos.

### 5.14 Configuración `Agent:Session:*` (`SessionOptions`, con `Normalize` como `RecordingOptions`)

| Clave | Default | Validación |
|---|---|---|
| `ScreenBytesPerMinute` | `31457280` (30 MiB) | > 0 |
| `CameraBytesPerMinute` | `12582912` (12 MiB) | > 0 |
| `MinSegmentMinutes` / `MaxSegmentMinutes` | `5` / `120` | 1 ≤ min ≤ max |
| `DiskCheckSeconds` | `60` | 10–600 |
| `WarnMinutesLeft` | `60` | ≥ 0 |
| `CutReserveMinutes` | `2` | ≥ 1 |
| `ResumeDelaySeconds` | `3` | 0–60 |
| `ScreenProbeSeconds` | `30` | 0 = sin chequeo; si no, 10–600 |
| `RotationRetrySeconds` | `60` | 10–600 |
| `ShutdownBudgetSeconds` | `20` | 5–120 |
| `CameraWidth` / `CameraHeight` / `CameraFps` / `CameraCrf` (F2) | `1280` / `720` / `15` / `26` | rangos razonables, si no el default |
| `MockSegmentSeconds` (solo `Mock` o dev) | `0` (= usar minutos) | 0 o 10–3600 |
| `MockFreeBytes` (solo `Mock`) | `null` | — |

La config de **instalación** (modo y duración del tramo) es del backend (§6.6) y viaja en el `start`. Los valores de
arriba son por PC: van en `appsettings.json` / `appsettings.Local.json`, sin variables `FACTUM_*` (Tatana no usa ese
mecanismo).

### 5.15 Modo mock

- Con `Agent:Mock=true`, `AndroidScreenTrack` no lanza scrcpy. Al cerrar cada tramo genera el MKV con
  `GenerateMockVideoAsync` (lavfi `testsrc2`, duración = la grabada), o con `MockMkvBytes` si no hay ffmpeg.
- El corte usa `MockSegmentSeconds` si es > 0.
- `MockFreeBytes` simula el espacio libre para ver los avisos.
- En F2, `/cameras` devuelve `[{ id: "mock-cam", name: "Cámara simulada", backend: "mock" }]` y la cámara se simula
  con lavfi.

Así el reviewer y la prueba manual pueden recorrer todo sin celular.

### 5.16 Cámara del procedimiento [F2]

- **`GET /cameras`** (`CameraDevices`, timeout 5 s, caché 10 s):
  - Mac: avfoundation sin `Capture screen N`; `id` = nombre.
  - Windows: `ParseVideoDevices` (paralelo a `ParseAudioDevices`): tipos que contienen `video`, o la sección "video" en
    el formato viejo; `id` = alternative name o, si falta, el nombre.
  - Linux: `/dev/video*`.
- **Proceso de grabación** (uno por tramo continuo, no por tramo de 30 min), con `ArgumentList`:
  ```
  ffmpeg -hide_banner -nostats -loglevel warning -y
    [win]  -f dshow -rtbufsize 256M -video_size {W}x{H} -framerate {F} -i video={id}[:audio={mic}]
    [mac]  -f avfoundation -video_size {W}x{H} -framerate 30 -i "{name}[:{mic index}]"
    -filter_complex "[0:v]fps={F},split=2[r][p];[p]fps=1/2,scale=320:-2[pv]"
    -map "[r]" [-map 0:a] -c:v libx264 -preset veryfast -crf {CRF} -pix_fmt yuv420p -g {2F}
    -force_key_frames "expr:gte(t,n_forced*{SEG})" [-c:a aac -b:a 96k]
    -f segment -segment_time {SEG} -segment_format matroska
    -segment_format_options flush_packets=1:cluster_time_limit=2000
    -segment_list <staging>/camara.csv -segment_list_type csv -segment_list_flags +live
    -reset_timestamps 1 -segment_start_number {N} <staging>/camara_%04d.part.mkv
    -map "[pv]" -q:v 6 -update 1 -f image2 <staging>/camara-preview.jpg
  ```
  - `SEG` = `segment_minutes × 60`. La cámara arranca inmediatamente después del marcador del primer tramo de pantalla,
    así los cortes caen a ~1 s de los de pantalla.
  - Si sale antes de 1,5 s con "Could not set video options" o "not supported", se reintenta **una** vez sin
    `-video_size`/`-framerate`.
  - El micrófono de la PC solo si `with_audio` (D8/D11): Windows con `MicCapture.ResolveWindowsDeviceAsync`; Mac con
    `:0`.
  - Cierre con `q` por stdin (como `MicCapture`).
- **Tramos:** un lector de `camara.csv` (sondeo de 2 s) toma cada línea nueva (`archivo,inicio,fin`) → renombra
  `camara_NNNN.part.mkv` al nombre final `sesion_camara_NNN_<fecha>_<hora>.mkv` (fecha y hora = inicio de la sesión
  continua + `inicio`) → cierre como §5.6.
- **Falla de la cámara:** ffmpeg sale con la sesión en curso →
  1. cierre del tramo abierto (remux si hace falta);
  2. hueco de pista `camara` con `camara_fallo`;
  3. aviso `camera_failed`.

  La pantalla sigue. `POST /cases/{id}/session/camera/retry` relanza ffmpeg y cierra el hueco.
- **Vista previa:**
  - antes de empezar: `POST /cameras/preview { id, name }` lanza el mismo ffmpeg **solo con la salida de preview**
    (se corta solo a los 120 s sin pedidos de la imagen) y `DELETE /cameras/preview` lo corta;
  - durante la sesión, el `preview.jpg` sale de la propia grabación;
  - `GET /cameras/preview.jpg` sirve el que corresponda (`no-store`, `nosniff`); 404 si no hay.
  - Abrir la sesión corta la preview previa antes de abrir la cámara (en Windows dshow no comparte la cámara).
- **macOS:** permiso de cámara de la app Tatana (§10). Si se niega, ffmpeg sale enseguida con "not authorized" →
  start con `camera_state: "failed"` y aviso `camera_failed` con `detail: "permission"`. La pantalla graba igual.

### 5.17 iPhone en la sesión [F2, DU4=A]

- Solo `ios_mode: "video_only"` (DVT). `with_mic`, `airplay` y `on_device` → 400 `session_ios_mode_unsupported`.
- **`ios_helper.py record`:**
  - la salida pasa a MKV (lo decide la extensión) con `-flush_packets 1 -cluster_time_limit 2000` cuando la extensión
    es `.mkv`. Los clips siguen en MP4 sin cambios.
  - `_watch_stdin` acepta, además de `stop`, líneas JSON:
    - `{"cmd":"rotate","path":"…"}`: cierra la entrada del ffmpeg actual y espera su cierre **en un hilo** (no
      bloquea el loop), abre otro ffmpeg sobre `path` y sigue con el próximo cuadro. Responde por stderr `ROTATED
      {"path_closed":"…","frames":N,"ffmpeg_exit":X}`.
    - `{"cmd":"snap","path":"…"}`: escribe el último PNG recibido en `path + ".part"` y renombra. Responde `SNAP
      {"path":"…","ok":true}`, o `ok:false` si todavía no hay cuadro.
  - EOF de stdin = `stop`, como hoy.
- **`DvtRecorder`:** suma `RotateAsync(newPath, timeout 15 s)` y `SnapAsync(path, timeout 5 s)`, que esperan la línea
  `ROTATED`/`SNAP`.
- **Corte:** `rotate`, sin solapamiento ni hueco. El tramo nuevo empieza con el cuadro siguiente: ≤ 0,5 s a 2 fps.
- **Capturas durante la sesión:** `IosService.TakeScreenshotAsync` → si hay una sesión iOS activa en ese UDID, usa
  `SnapAsync` en lugar de abrir otro canal DVT. Marca automática igual que en Android.
- **Sin variantes de previsualización** para los tramos (D12): la sesión no llama a `VideoPostprocessService`.
- `/health` informa `recording_session_ios_v1` solo si `ios.IsAvailable`.

---

## 6. Backend (API)

### 6.1 Modelo de datos (`Models/RecordingSession.cs`, colección `cases`, campo nuevo)

`Case.RecordingSession` (`[BsonIgnoreIfNull]`, JSON `recording_session`; `null` en todos los casos existentes, sin
backfill):

```csharp
[BsonIgnoreExtraElements] public sealed class RecordingSession {
  public bool Enabled { get; set; }                 // switch del perito (o forzado por "required")
  public bool WithCamera { get; set; }
  public string ModeAtChange { get; set; } = "";    // modo de instalación cuando se cambió el switch (auditoría)
  public DateTime SettingsUpdatedAt { get; set; }
  [BsonIgnoreIfNull] public string? SessionId { get; set; }      // del diario de Tatana
  [BsonIgnoreIfNull] public string? Platform { get; set; }       // "android" | "ios"
  [BsonIgnoreIfNull] public string? ConnectionId { get; set; }   // serial / UDID (= Case.Device.Serial)
  [BsonIgnoreIfNull] public string? AgentVersion { get; set; }
  public int SegmentMinutes { get; set; }
  [BsonIgnoreIfNull] public string? Audio { get; set; }          // "device" | "none"
  [BsonIgnoreIfNull] public SessionCamera? Camera { get; set; }
  [BsonIgnoreIfNull] public string? State { get; set; }          // último estado informado (§5.3)
  public List<SessionSegment> Segments { get; set; } = [];
  public List<SessionGap> Gaps { get; set; } = [];
  public List<SessionMark> Marks { get; set; } = [];
  [BsonIgnoreIfNull] public DateTime? SyncedAt { get; set; }     // hora del servidor del último sync
}
[BsonIgnoreExtraElements] public sealed class SessionCamera { Name; Width; Height; Fps; WithAudio }
[BsonIgnoreExtraElements] public sealed class SessionSegment {
  Id; Track; Index; Filename; State ("saved"|"discarded"); StartedAt; StoppedAt;
  double RecordedSeconds; double? DurationSeconds; string? Validation ("ok"|"incomplete");
  bool Recovered; double? OverlapSeconds; string? StopReason; long? Size; string? Sha256 }
[BsonIgnoreExtraElements] public sealed class SessionGap {
  Id; Track; From; DateTime? To; Reason; bool SegmentLost }
[BsonIgnoreExtraElements] public sealed class SessionMark {
  Id; At; int SegmentIndex; double OffsetSeconds; string? Text; string? Filename; Kind ("manual"|"captura") }
```

- Todas las horas son UTC del **reloj de la PC** (Tatana). Las del servidor van aparte (`SyncedAt`, eventos).
- Solo se guardan tramos **cerrados** (`saved`/`discarded`): el tramo en curso no llega al backend.
- La proyección del listado (`MongoCaseRepository`, la que excluye `ReportTexts`) suma
  `.Exclude(c => c.RecordingSession)`.

### 6.2 Reglas (`Services/Cases/RecordingSessionRules.cs`, puras)

- **Validación del snapshot** (`Validate(dto) → (error?, field?)`). Cualquier falla → 400 `invalid_recording_session`
  con `field`:
  - `session_id`: 32 hex.
  - `platform` ∈ android/ios.
  - `segment_minutes` 1–240.
  - Listas con tope: ≤ 2 000 tramos, ≤ 2 000 huecos y ≤ 5 000 marcas.
  - Ids: 32 hex, únicos por lista.
  - `track` ∈ pantalla/camara.
  - `index` ≥ 1.
  - `filename` según el regex de §5.2, con el `track` coincidente.
  - `state` ∈ saved/discarded.
  - `saved` exige `sha256` (64 hex minúscula), `size > 0` y `validation` ∈ ok/incomplete.
  - Horas: `started_at ≤ stopped_at`, y nada más de 1 día en el futuro respecto del servidor (tolera el desfase de la
    PC).
  - `reason` ∈ { `cambio_de_paso`, `retomado_otro_dia`, `desconexion`, `tatana_cerrado`, `sin_espacio`,
    `camara_fallo`, `detenido_por_perito`, `fallo_grabacion`, `rotacion` [DU2=B] }.
  - `stop_reason` ∈ lo anterior + `rotacion`.
  - Marcas: `text` ≤ 300 (`SafeText.Clean(SingleLine)`); `filename` válido o null; `kind` ∈ manual/captura.
- **Merge append-only** (`Merge(existing, incoming, now) → (merged, changes)`):
  - **Tramos por `id`:** uno nuevo se agrega. Uno existente **no cambia** salvo que pase de `discarded` a `saved` (no
    debería) o complete `size`/`sha256`/`duration_seconds` que faltaban. **Mismo `id` con otro `sha256` o `filename`**
    → 409 `recording_session_conflict`. Un `filename` repetido con otro `id` también → 409.
  - **Huecos por `id`:** `to` se completa una sola vez; `reason` puede pasar de `cambio_de_paso` a
    `retomado_otro_dia` una sola vez. Cualquier otra diferencia → 409.
  - **Marcas por `id`:** inmutables; diferencia → 409.
  - Lo que está en `existing` y no en `incoming` **se conserva** (nunca se borra nada).
  - Cabecera: `session_id`, `platform`, `connection_id`, `agent_version`, `segment_minutes`, `audio`, `camera` y
    `state` toman el último valor.
  - `changes`: tramos nuevos, huecos nuevos, huecos cerrados y "primer tramo de la pista" (para los eventos de §6.4).
- **Archivos de la sesión:** `SessionFilenames(cas)` = `Filename` de los tramos `saved`. Clasificar por pertenencia al
  diario, no por el prefijo (D-T5).
- **Pendientes para generar** (`PendingForGeneration(cas) → List<string>`):
  - si `RecordingSession.State` ∈ {starting, recording, rotating, waiting_device, stopping} → `["<sesion abierta>"]`;
  - si no, los `saved` cuyo `(filename, sha256)` no está en `cas.Evidence`.

### 6.3 Endpoints (`CasesController`, `[Authorize]`, dueño + editable como `device-details`)

1. **`PUT /api/cases/{id}/recording-session/settings`** — body `{ "enabled": bool, "with_camera": bool }` → 200 `Case`.
   - **Modo `disabled`:**
     - con `enabled: true` → 409 `recording_session_disabled` "La grabación de la sesión está deshabilitada en esta
       instalación.";
     - con `false` → 200 y se guarda (deja constancia de la decisión).
   - **Modo `required` + `enabled: false`** → 400 `recording_session_required` "La configuración de esta instalación
     exige grabar la sesión."
   - Crea `RecordingSession` si no existe (`SegmentMinutes` = el de la config).
   - Evento `recording_session_settings_changed` **solo si cambió** `enabled` o `with_camera`, o si se creó.
2. **`PUT /api/cases/{id}/recording-session`** — body = snapshot del diario (§7.6.2) → 200 `Case`.
   - Requiere `RecordingSession?.Enabled == true`. Si no → 409 `recording_session_not_enabled`.
   - Valida, hace el merge, escribe con `SetRecordingSessionAsync(id, expectedSyncedAt, merged)` bajo `Editable(id)` y
     concurrencia optimista sobre `SyncedAt`, con hasta 3 reintentos. Mismo patrón que `RegisterEvidenceAsync`; tras
     el tercero → 409 `recording_session_busy`.
   - Emite los eventos de `changes`.
   - **No** valida contra el manifiesto: el orden entre registrar el archivo y sincronizar el diario es libre.

Repositorio (`MongoCaseRepository`): `SetRecordingSessionSettingsAsync(id, settings…)`, que hace `$set` de los cuatro
campos de cabecera y crea el sub-documento si falta, y `SetRecordingSessionAsync(id, expectedSyncedAt, session)`. Las
dos bajo `Editable(id)`.

### 6.4 Eventos (`CaseEventTypes` + `CaseEventDetail`)

| Tipo | Cuándo | `filename` | `detail` |
|---|---|---|---|
| `recording_session_settings_changed` | §6.3.1 | — | `enabled`, `with_camera` |
| `recording_session_started` | primer tramo de una pista en el caso | — | `track`, `segment_index`, `at` |
| `recording_session_segment_closed` | tramo `saved` nuevo | el del tramo | `track`, `segment_index`, `sha256`, `size`, `duration_seconds`, `validation`, `recovered` |
| `recording_session_stopped` | hueco nuevo, o tramo `discarded` | — | `track`, `at` (= `from`), `reason`, `segment_lost` |
| `recording_session_resumed` | hueco que se cierra (`to` nuevo) | — | `track`, `at` (= `to`), `reason`, `gap_seconds`, `segment_index` |

Campos nuevos de `CaseEventDetail`, todos `[BsonIgnoreIfNull, JsonIgnore(WhenWritingNull)]`: `Enabled bool?`,
`WithCamera bool?`, `Track string?`, `SegmentIndex int?`, `At DateTime?`, `DurationSeconds double?`, `Validation
string?`, `Recovered bool?`, `GapSeconds double?` y `SegmentLost bool?`. `Reason`, `Sha256` y `Size` ya existen.
`Timestamp` es la hora del servidor al sincronizar; `at` es la hora real de la PC. Sin datos personales: las marcas no
generan eventos y su texto nunca va a `case_events`.

### 6.5 Generación, borrado y manifiesto

- **`PrepareGenerationAsync`**, después de `no_evidence` y antes de validar los textos:
  `RecordingSessionRules.PendingForGeneration(cas)` no vacío → 409 `recording_session_open` "Primero cerrá la
  grabación de la sesión." con `pending_files: [...]`. Un caso sin `RecordingSession` o con `Enabled=false` y sin tramos
  pasa como hoy.
- **`ManifestFileDto`** suma `string? ZipFolder = null` (JSON `zip_folder`, siempre presente porque el backend
  serializa los null). `EvidenceManifest.ToManifestFiles(cas)` pone `"sesion"` en los nombres de
  `SessionFilenames(cas)`. `Matches` no cambia: ignora `zip_folder`.
- **`DeleteEvidenceAsync`:** un `filename` en `SessionFilenames(cas)` → 409 `recording_session_file` "Los tramos de la
  grabación de la sesión no se pueden quitar del caso."
- **`EvidenceManifest.ReportImageNames`** no cambia: los tramos son video, no capturas.

### 6.6 Configuración (`Services/Recording/RecordingSessionOptions.cs`)

| Clave | Default | Valores | Variable `FACTUM_*` (compose nube y Windows) |
|---|---|---|---|
| `Recording:SessionMode` | `optional_off` | `disabled` \| `optional_off` \| `optional_on` \| `required` | `Recording__SessionMode: ${FACTUM_GRABACION_SESION_MODO:-optional_off}` |
| `Recording:SegmentMinutes` | `30` | 5–120 | `Recording__SegmentMinutes: ${FACTUM_GRABACION_SESION_TRAMO_MINUTOS:-30}` |

- Un valor inválido no impide arrancar: warning y default (como `ReportSettings`).
- **`PublicConfigResponse`** suma **al final**: `RecordingSessionMode` (`recording_session_mode`) y
  `RecordingSessionSegmentMinutes` (`recording_session_segment_minutes`). Se actualiza el test de orden.
- No se usa `FACTUM_SESION_*` para no confundir con la sesión web (`FACTUM_SESION_TOPE_HORAS`).

### 6.7 Integración con el sellado (#78)

- **Cada tramo se sella** por el camino normal: el cliente lo registra con `PUT /evidence` →
  `_timestamps.SealEvidenceAsync`. No hay código nuevo de sellado: sin TSA queda pendiente y la cola reintenta.
- **Bandeja:** `EvidenceResponse.timestamps` ya trae el sello de cada tramo. La web lo muestra en el grupo "Sesión".
- **`sellos/`:** el `.tsr` de cada tramo va plano, `sellos/<filename>.tsr`, como los demás.
  `TimestampReadme.Build(..., IReadOnlySet<string>? sessionFiles = null)` suma por cada sello de tramo la línea
  `  Dentro del ZIP: "sesion/<filename>"` y usa `TypeLabel` "Tramo de la grabación de la sesión". Lo llama
  `CaseTimestampsController` con `RecordingSessionRules.SessionFilenames(cas)`.
- **Informe:** `ReportTimestampInfo.Evidence` incluye los tramos; la tabla de la sesión usa
  `ReportTimestampTexts.FileLine`. `ReportTimestampTexts.Block(info, files, zone)` recibe **solo los archivos de la
  tabla general**, así "n de m archivos" no cuenta los tramos. La sección de la sesión dice cuántos tramos están
  sellados (§8.4).

### 6.8 Integración con la ficha y el reloj (#79)

- La sección del informe usa `cas.DeviceDetails?.ClockStart`, o `ClockClose` si es la única con `DeviceMinusPcMs`, para
  la línea de correlación (§8.4). Sin ficha o sin medición, va la frase sin diferencia.
- La web la muestra igual en el panel de la sesión (§9.7).
- La sesión **no** mide el reloj: reutiliza lo de #79.

---

## 7. Contrato compartido

### 7.1 Archivos de cada lado

| Qué | Tatana (C#) | API (C#) | Web (TS) |
|---|---|---|---|
| Estado, diario, tramos, huecos, marcas | `Services/Session/SessionModels.cs` | `DTOs/RecordingSessionDtos.cs`, `Models/RecordingSession.cs` | `client/src/lib/agent.ts` (Tatana), `client/src/lib/api.ts` (API) |
| Endpoints de sesión | `Controllers/CaseSessionController.cs`, `SessionCapacityController.cs`, `CamerasController.cs` (F2), `AgentStateController.cs` | `Controllers/CasesController.cs` | `lib/agent.ts`, `lib/api.ts` |
| ZIP | `Services/CaseZipService.cs` (`ZipFileItem.ZipFolder`) | `DTOs/EvidenceDtos.cs` (`ManifestFileDto.ZipFolder`) | `lib/api.ts` (`ManifestFile.zip_folder`), `lib/agent.ts` (`BuildZipRequest.files`) |
| Config pública | — | `DTOs/ConfigDtos.cs` | `hooks/usePublicConfig.ts`, `lib/api.ts` |
| Eventos | — | `Models/CaseEvent.cs` | `lib/api.ts` (`CaseEventType`, detail), `lib/case-events.ts` |
| Códigos de error | `Common/AgentErrorCodes.cs` | `Services/Cases/RecordingSessionFlow.cs` (`RecordingSessionErrorCodes`) | `lib/agent.ts` (`AgentErrorCode`), `lib/agent-messages.ts` |

### 7.2 Tatana → web, HTTP (snake_case_lower; **los `null` no viajan**)

**7.2.1 `GET /session/capacity?camera=false`** → 200

```json
{ "available_bytes": 128849018880, "min_free_bytes": 1073741824, "bytes_per_minute": 31457280,
  "zip_on_same_volume": true, "estimated_minutes": 2032, "required_bytes_to_start": 2961178624,
  "can_start": true }
```

Si no se pudo medir el disco (fail-open): `available_bytes` y `estimated_minutes` faltan y `can_start: true`.

**7.2.2 `POST /cases/{caseId}/session/start`**

```json
{ "serial": "R58RA3R5AGA", "platform": "android", "android_version": 11, "segment_minutes": 30,
  "skip_screen_check": false, "origin_id": "t_8f2c…",
  "ios_mode": "video_only",
  "camera": { "id": "@device_pnp_…", "name": "Integrated Webcam", "with_audio": false } }
```

- `ios_mode` y `camera` son opcionales (F2).
- Respuesta 200: `SessionStateDto` (7.2.7) con `"resumed": true|false`.
- Errores `{ error, code, … }`:
  - 400 `invalid_case_id` / `invalid_request` / `session_platform_unsupported` / `session_ios_mode_unsupported`;
  - 409 `already_recording` / `session_active` (con `case_id` de la sesión activa) / `screen_off` / `screen_locked`;
  - 504 `recording_start_timeout`;
  - 507 `insufficient_storage` (`available_bytes`, `required_bytes`);
  - 500 sin `code`, con el error de scrcpy (como hoy).

**7.2.3 `POST /cases/{caseId}/session/stop`**

```json
{ "reason": "cambio_de_paso", "origin_id": "t_8f2c…" }
```

- `reason` ∈ `cambio_de_paso` | `detenido_por_perito`. Cualquier otro → 400 `invalid_request`.
- 200 `SessionStateDto` (`state: "stopped"`) **después** de que los tramos quedaron `saved` (puede tardar segundos:
  hash).
- **Idempotente:** si no hay sesión activa para el caso, 200 con el estado del diario (o `{ "case_id": "…", "state":
  "idle" }` si no hay diario). Con `waiting_device`, cierra el hueco de desconexión como fin y pasa a `stopped`.

**7.2.4 `POST /cases/{caseId}/session/marks`** — `{ "text": "Abre el chat con Juan", "origin_id": "…" }` → 200
`SessionMarkDto` (7.2.6). 409 `session_not_active`.

**7.2.5 `GET /cases/{caseId}/session`** → 200 `CaseSessionDto` (7.2.6). 404 `session_not_found` si no hay diario.
`Cache-Control: no-store`.

**7.2.6 Diario (`CaseSessionDto`).** Es exactamente el `sesion.json` sin `process` ni `stay_awake`:

```json
{
  "schema_version": 1,
  "session_id": "3f1c…(32 hex)",
  "case_id": "6b0e…",
  "serial": "R58RA3R5AGA",
  "platform": "android",
  "agent_version": "1.8.0",
  "segment_minutes": 30,
  "audio": "device",
  "camera": { "name": "Integrated Webcam", "width": 1280, "height": 720, "fps": 15, "with_audio": false },
  "state": "recording",
  "stop_reason": "desconexion",
  "segments": [
    { "id": "…", "track": "pantalla", "index": 1, "filename": "sesion_pantalla_001_20261014_140210.mkv",
      "state": "saved", "started_at": "2026-10-14T17:02:10.481Z", "stopped_at": "2026-10-14T17:32:11.160Z",
      "recorded_seconds": 1800.7, "duration_seconds": 1800.2, "validation": "ok", "recovered": false,
      "overlap_seconds": 0.7, "stop_reason": "rotacion", "size": 943718400, "sha256": "ab12…(64 hex)" }
  ],
  "gaps": [
    { "id": "…", "track": "pantalla", "from": "2026-10-14T17:45:03Z", "to": "2026-10-14T17:46:20Z",
      "reason": "desconexion", "segment_lost": false }
  ],
  "marks": [
    { "id": "…", "at": "2026-10-14T17:10:44.120Z", "segment_index": 1, "offset_seconds": 513.6,
      "text": "Abre el chat con Juan", "kind": "manual" },
    { "id": "…", "at": "2026-10-14T17:12:01.003Z", "segment_index": 1, "offset_seconds": 590.5,
      "filename": "screenshot_20261014_141201_003.png", "kind": "captura" }
  ],
  "updated_at": "2026-10-14T17:46:21Z"
}
```

- `segments[].state` ∈ `recording` | `closing` | `saved` | `discarded`. Los dos primeros solo existen en Tatana y la
  web **no** los manda al backend.
- `camera` solo con cámara (F2). `stop_reason` solo en `stopped` / `interrupted` / `waiting_device`.
- `to` falta si el hueco sigue abierto. `filename`/`text` faltan si son null.
- Sin cámara, `audio` es `device` (Android 11+) o `none`.

**7.2.7 Estado (`SessionStateDto`).** Lo devuelven `start`/`stop`, el evento `session_state` y
`/agent/state.recording_session`:

```json
{ "case_id": "6b0e…", "serial": "R58RA3R5AGA", "platform": "android",
  "state": "recording", "resumed": false, "segment_minutes": 30,
  "session_started_at": "2026-10-14T17:02:10.481Z",
  "run_started_at": "2026-10-14T17:46:20.002Z",
  "screen_segment_index": 3, "screen_segment_started_at": "2026-10-14T18:16:20.700Z",
  "camera_state": "off", "camera_segment_index": 3, "camera_segment_started_at": "…",
  "segments_saved": 2, "recorded_seconds": 4210.5,
  "stop_reason": "desconexion", "stopped_at": "…",
  "warnings": [ { "kind": "disk_low", "since": "…", "available_bytes": 5368709120, "minutes_left": 52 },
                { "kind": "screen_locked", "since": "…" },
                { "kind": "camera_failed", "since": "…", "detail": "permission" },
                { "kind": "rotation_delayed", "since": "…" } ],
  "origin_id": "t_8f2c…" }
```

- `state` ∈ `idle` | `starting` | `recording` | `rotating` | `waiting_device` | `stopping` | `stopped` |
  `interrupted`.
- `camera_state` ∈ `off` | `recording` | `failed`.
- `run_started_at` es el inicio del tramo continuo (desde el último start o reanudación); lo usa el cronómetro.
- `warnings[].kind` ∈ `screen_off` | `screen_locked` | `disk_low` | `camera_failed` | `rotation_delayed`.
- `origin_id` es la pestaña que pidió la última acción, si la hubo.

**7.2.8 `POST /devices/{serial}/screenshot`** (cambio): la respuesta 200 suma `"session_mark": SessionMarkDto` solo si
hubo marca.

**7.2.9 `POST /agent/prepare-shutdown`** (solo local; desde un navegador → 403 `maintenance_local_only`) → 200 `{
"had_session": true, "segments_saved": 1, "pending": 0 }`.

**7.2.10 F2:**
- `GET /cameras` → `{ "cameras": [ { "id": "…", "name": "…", "backend": "avfoundation" | "dshow" | "v4l2" | "mock" }
  ] }`.
- `POST /cameras/preview { "id", "name" }` → 204.
- `DELETE /cameras/preview` → 204.
- `GET /cameras/preview.jpg` → `image/jpeg` | 404.
- `POST /cases/{caseId}/session/camera/retry` → `SessionStateDto`.

### 7.3 Tatana → web, WebSocket (`{ "type", "timestamp", "data" }`; **los `null` viajan como `null`**)

| `type` | `data` |
|---|---|
| `session_state` | `SessionStateDto` (7.2.7). Va en cada transición y cuando cambian los avisos. |
| `session_segment_closed` | `{ "case_id", "origin_id", "segment": SegmentDto }`, con `segment.state` `saved` o `discarded`. |
| `session_mark_added` | `{ "case_id", "origin_id", "mark": SessionMarkDto }` |
| `screenshot_taken` (cambio) | Suma `"session_mark": SessionMarkDto \| null`. |

### 7.4 `/health` y `/agent/state`

- **`capabilities`** (en `HealthController.Capabilities` y su test):
  - `recording_session_v1` [F1]: todo §7.2.1–7.2.9, §7.3, `zip_folder` en el ZIP, `session_active` en `record/start`,
    `session_file_protected` y la pista Android.
  - `recording_session_camera_v1` [F2]: §7.2.10 y `camera` en el start.
  - `recording_session_ios_v1` [F2]: `platform: "ios"` en el start y capturas por `snap`. Solo si `ios.IsAvailable`.
- **`GET /agent/state`** suma:
  - `"recording_session": SessionStateDto`, solo si hay una sesión en un estado activo (se omite si no);
  - `"session_recovery": "running" | "done"`, siempre.
- `operations[]` incluye `{ "kind": "recording_session", "since", "detail": "<case_id>" }`.

### 7.5 Web → API (snake_case_lower)

- `PUT /api/cases/{id}/recording-session/settings` → `{ "enabled": true, "with_camera": false }`.
- `PUT /api/cases/{id}/recording-session` → el diario de Tatana (7.2.6) **sin** `case_id`, `schema_version` ni
  `updated_at`, **solo con los tramos `saved`/`discarded`**, y con `serial` renombrado a `connection_id`:

```json
{ "session_id": "…", "platform": "android", "connection_id": "R58RA3R5AGA", "agent_version": "1.8.0",
  "segment_minutes": 30, "audio": "device", "camera": null, "state": "stopped",
  "segments": [ … ], "gaps": [ … ], "marks": [ … ] }
```

### 7.6 API → web

**7.6.1 `Case.recording_session`** (`null` en casos sin sesión):

```json
{ "enabled": true, "with_camera": false, "mode_at_change": "optional_off",
  "settings_updated_at": "2026-10-14T17:01:58Z",
  "session_id": "…", "platform": "android", "connection_id": "R58RA3R5AGA", "agent_version": "1.8.0",
  "segment_minutes": 30, "audio": "device", "camera": null, "state": "stopped",
  "segments": [ { "id", "track", "index", "filename", "state", "started_at", "stopped_at", "recorded_seconds",
                  "duration_seconds", "validation", "recovered", "overlap_seconds", "stop_reason", "size", "sha256" } ],
  "gaps": [ { "id", "track", "from", "to", "reason", "segment_lost" } ],
  "marks": [ { "id", "at", "segment_index", "offset_seconds", "text", "filename", "kind" } ],
  "synced_at": "2026-10-14T18:30:02Z" }
```

El backend **sí** manda los `null` (por ejemplo `"to": null`, `"text": null`). La web los trata como ausentes.

**7.6.2 Generación.** `PrepareGenerationResponse.files[]` = `{ "filename", "size", "sha256", "zip_folder": "sesion" |
null }`. La web los pasa tal cual a `POST /cases/{id}/zip`. Tatana acepta `zip_folder` y lo devuelve en
`BuildZipResponse.files[]`.

**7.6.3 Config pública:** `"recording_session_mode": "optional_off"`, `"recording_session_segment_minutes": 30`, al
final de `GET /api/config/public`.

**7.6.4 Eventos:** los 5 tipos de §6.4 con los campos de `detail` en snake_case: `enabled`, `with_camera`, `track`,
`segment_index`, `at`, `duration_seconds`, `validation`, `recovered`, `gap_seconds`, `segment_lost`, más `reason`,
`sha256` y `size`, que ya existen.

### 7.7 Códigos de error nuevos

| Lado | HTTP | `code` | Texto (`error`) |
|---|---|---|---|
| Tatana | 409 | `session_active` | "Hay una grabación de la sesión en curso en este Tatana (otro caso o celular)." / en `record/start`: "Hay una grabación de la sesión en curso en este celular. Usá «Marcar momento»." |
| Tatana | 409 | `session_not_active` | "No hay una grabación de la sesión en curso para este caso." |
| Tatana | 404 | `session_not_found` | "Este caso no tiene grabación de la sesión en esta PC." |
| Tatana | 409 | `session_file_protected` | §5.12 |
| Tatana | 400 | `session_platform_unsupported` | "Esta versión de Tatana no graba la sesión en iPhone." |
| Tatana | 400 | `session_ios_mode_unsupported` | "La grabación de la sesión en iPhone usa solo el modo «Solo video»." |
| API | 409 | `recording_session_disabled` | §6.3 |
| API | 400 | `recording_session_required` | §6.3 |
| API | 409 | `recording_session_not_enabled` | "Este caso no tiene activada la grabación de la sesión." |
| API | 400 | `invalid_recording_session` (+`field`) | "Datos de la grabación de la sesión inválidos." |
| API | 409 | `recording_session_conflict` | "La grabación de la sesión registrada no coincide con la de esta PC." |
| API | 409 | `recording_session_busy` | "Otra pestaña está actualizando la grabación de la sesión. Reintentá en unos segundos." |
| API | 409 | `recording_session_open` (+`pending_files`) | "Primero cerrá la grabación de la sesión." |
| API | 409 | `recording_session_file` | §6.5 |

### 7.8 Tolerancia entre versiones

| Combinación | Qué pasa |
|---|---|
| Web nueva + Tatana sin `recording_session_v1` | No se ofrece la sesión; aviso informativo "Para grabar toda la recolección hace falta actualizar Tatana." El resto del paso 3 funciona como hoy. Con modo `required`, el aviso dice además "La configuración de esta instalación exige grabar la sesión", pero no bloquea (HU, escenario "Tatana viejo"). Para exigirla de verdad: `FACTUM_TATANA_VERSION_MINIMA` = la versión con la capability (D-T23). |
| Web nueva + iPhone + Tatana sin `recording_session_ios_v1` | No se ofrece; aviso "La grabación de la sesión en iPhone llega en una próxima versión de Tatana." |
| Web nueva + backend sin las rutas | `settings` responde 404 → la web oculta el bloque (sin aviso al perito; log en consola). |
| Tatana nuevo + web vieja | Tatana nunca graba una sesión si no se la piden. `zip_folder` ausente = plano. |
| Backend nuevo + Tatana viejo generando | `zip_folder` se ignora (System.Text.Json ignora campos desconocidos) y los tramos van planos. Solo pasa si un caso con sesión se genera desde un Tatana degradado. Riesgo aceptado. |
| Casos anteriores | `recording_session: null`. Sin sección, sin cambios en el informe ni en la descarga. |

---

## 8. Informe

### 8.1 Plantilla v4 (`ops/plantilla/build_plantilla_v4.py`)

- `BODY_PLACEHOLDERS` += `{descripcionRegistroAudiovisual}`, `{nombreArchivoSesion}`, `{hashArchivoSesion}` y
  `{detalleRegistroAudiovisual}`. `BODY_BLOCKS` += `"registroAudiovisual"`.
- Inmediatamente **después de `{/selloTiempo}`**, y por lo tanto antes de "CADENA DE CUSTODIA DIGITAL", va el bloque:
  ```
  marker("{#registroAudiovisual}")
  párrafo vacío (separador, como el de selloTiempo)
  párrafo "**Registro audiovisual de la recolección**"     (norm_ppr, rpr de 2D02EAD7, negrita)
  párrafo "{descripcionRegistroAudiovisual}"                 (norm_ppr, rpr de 2D02EAD7)
  tabla: clon de la tabla de hashes YA reescrita (grilla 3600/4691, encabezado repetido, cantSplit),
         con paraIds nuevos (ids.new("a7:ses-…")), {nombreArchivo}→{nombreArchivoSesion},
         {hashArchivo}→{hashArchivoSesion}; encabezados "ARCHIVO" / "HASH SHA-256" iguales
  párrafo "{detalleRegistroAudiovisual}"                     (norm_ppr, rpr de 2D02EAD7)
  marker("{/registroAudiovisual}")
  ```
- `--check` suma `check_sesion(doc)`: exactamente una tabla entre los marcadores, con dos filas y los dos
  placeholders. La tabla de hashes sigue siendo la primera con `tblStyle 17`.

### 8.2 Plantilla v6 (`ops/plantilla/build_plantilla_v6.py`)

- Hereda los conjuntos de la v4.
- `hash_table(tbl_xml, hash_ph="{hashArchivo}")`: el placeholder de la columna Courier pasa a ser un parámetro.
- `build_document` reestila **las dos** tablas con `tblStyle w:val="17"`: tienen que ser exactamente dos, si no
  `Falla("ancla tabla")`. La primera con `{hashArchivo}` y la segunda con `{hashArchivoSesion}`.
- El subtítulo del bloque toma `rpr(ARIAL, BOLD, color(INK), sz(20))` y spacing `before=240 after=120`.
- `check_v62` y el chequeo de placeholders heredan el bloque nuevo.
- Se regeneran `plantilla_informe_v4.docx` y `plantilla_informe_v6.docx` **con los scripts**, nunca a mano. Se
  documenta en `ops/plantilla/README.md`.

### 8.3 Backend (`ReportService`)

- `KnownPlaceholders` += los 4 placeholders. `BlockKeys` += `"registroAudiovisual"`.
- En `GenerateDocxAsync`:
  - `var sessionNames = RecordingSessionRules.SessionFilenames(cas)`.
  - `generalFiles = files` sin `sessionNames`; `sessionFiles = files` con ellos, ordenados por `started_at` y
    después pista (`pantalla` primero).
  - `conditions["registroAudiovisual"] = cas.RecordingSession is not null && sessionFiles.Count > 0` (D10 A: sin
    constancia negativa).
  - B-R4: con la condición, `ReplaceParagraphPerLine` de `{descripcionRegistroAudiovisual}` y
    `{detalleRegistroAudiovisual}` con `ReportSessionTexts.Description(...)` / `Detail(...)`.
  - B-R5: `FillHashTable(body, generalFiles, …)` y `ReportTimestampTexts.Block(timestamps, generalFiles, zone)`.
  - **B-R5b:** `FillSessionTable(body, sessionRows, hashes, resolved, timestamps)`. Busca la fila con
    `{nombreArchivoSesion}` y clona con `BuildHashRow(model, name, lines, hash, resolved, namePh:
    "{nombreArchivoSesion}", hashPh: "{hashArchivoSesion}")`. `BuildHashRow` suma esos dos parámetros con default en
    los placeholders de hoy. **Sin fila del ZIP.**
- **Anexo de capturas:** no cambia (los tramos no son capturas).
- **`ReportValues.Counts(files, cas)`** excluye `SessionFilenames(cas)`. La cantidad de "Grabaciones" de los textos por
  defecto no cuenta los tramos. Lo mismo `RenderDefaults` y `OperacionesBase` (DT13 de #61).
- **Flujo viejo (server):** no hay sesión. `GenerateAsync` no cambia; la condición da false.

### 8.4 Textos exactos (`Services/Reports/ReportSessionTexts.cs`, puro, texto plano)

Horas en la zona del informe (`settings.Zone`), con el formato `dd/MM/yyyy HH:mm:ss`. Si el fin es el mismo día que el
inicio, el fin va solo con `HH:mm:ss`. Las duraciones usan `ReportDeviceTexts.FormatDuration` (redondeo a segundos).

**`Description(session, sessionRows, deviceDetails, timestampsEnabled, sealedCount, zone)`**, un párrafo por línea:
1. "Durante la recolección se grabó en video, de forma continua, la pantalla del dispositivo{, y con una cámara de la
   estación de trabajo, el procedimiento realizado sobre el equipo}. La grabación se guardó en tramos de hasta {N}
   minutos: cada tramo se cerró, se le calculó su valor hash SHA-256 y se incorporó al caso apenas terminó. Los
   archivos se entregan sin edición ni recodificación dentro del contenedor de la evidencia, en la carpeta
   \"sesion\"."
2. **Pantalla:**
   - Android: "La pantalla se capturó por USB con scrcpy, a un máximo de 1080 píxeles de alto{, con el audio interno
     del dispositivo}. Para no perder imagen entre tramos, cada tramo nuevo comenzó antes de cerrar el anterior, por lo
     que tramos consecutivos se superponen alrededor de un segundo." **[DU2=B:** "Entre tramos consecutivos hay un
     corte técnico de alrededor de un segundo, que figura en la lista de huecos."**]**
   - iOS (F2): "La pantalla se capturó por USB a dos cuadros por segundo."
3. Solo con cámara (D11): "Se registró en video el procedimiento sobre el equipo con la cámara «{nombre}» ({W}×{H},
   {fps} cuadros por segundo, {sin audio | con el micrófono de la estación de trabajo}), en archivos separados de los
   de la pantalla y con los mismos cortes."
4. "Se grabaron {d} en {n} tramos{ de pantalla y {m} de cámara}, entre el {primer inicio} y el {último fin}."
5. Solo si hay sellado: "{s} de {t} tramos cuentan con sello de tiempo propio, indicado en la tabla siguiente."
6. **Reloj:**
   - Con `DeviceMinusPcMs`: "Las horas de esta sección corresponden al reloj de la estación de trabajo. Al
     {inicio|cierre} del caso, {ReportDeviceTexts.DeviceSkewPast(ms, margin)}: para comparar la hora que se ve en la
     pantalla del dispositivo dentro del video con las de esta sección, debe tenerse en cuenta esa diferencia."
   - Sin medición: "Las horas de esta sección corresponden al reloj de la estación de trabajo."

**`RowLines(row, timestampView?, timestampsEnabled, zone)`**, líneas extra de la fila (después del nombre):
- "{Pantalla|Cámara} · tramo {i} · {inicio} a {fin} ({FormatDuration(recorded_seconds)})"
- `ok`: "Archivo completo". `incomplete`: "Archivo incompleto: el video dura {FormatDuration(duration)} de
  {FormatDuration(recorded)} grabados".
- Si `recovered`, se agrega " · recuperado tras el cierre de Tatana".
- Con sellado: `ReportTimestampTexts.FileLine(view, zone)`.

**`Detail(session, zone)`**:
- "Huecos en la grabación:" y una línea por hueco ordenado por `from`: "• {from} a {to} ({FormatDuration}) — {Pista}:
  {motivo}{ — el tramo en curso no se pudo recuperar}".
  - Hueco sin `to`: "• Desde {from} — {Pista}: {motivo} (la grabación no se reanudó)".
  - Sin huecos: "Sin huecos: la grabación fue continua entre el primer y el último tramo."
- "La grabación terminó el {fin del último tramo} ({motivo del último stop_reason ≠ rotacion})."
- Si hay marcas: "Marcas de momento (para ubicar hechos en el video de la pantalla; el mismo minuto sirve
  aproximadamente para el de la cámara):" y una por marca ordenada por `at`: "• {HH:mm:ss} — tramo {i}, minuto
  {mm:ss} del video — {texto | "Captura " + filename | "Marca sin texto"}".

**Motivos** (informe y web, `RecordingSessionTexts.ReasonText`):

| `reason` | Texto |
|---|---|
| `cambio_de_paso` | el perito salió del paso de captura |
| `retomado_otro_dia` | el caso se retomó otro día |
| `desconexion` | se desconectó el cable USB del dispositivo |
| `tatana_cerrado` | Tatana se cerró o la estación de trabajo se reinició |
| `sin_espacio` | quedaba poco espacio en el disco de la estación de trabajo |
| `camara_fallo` | la cámara dejó de grabar |
| `detenido_por_perito` | el perito detuvo la grabación |
| `fallo_grabacion` | la herramienta de captura se interrumpió por un error |
| `rotacion` [DU2=B] | corte técnico entre tramos |

---

## 9. Web (`client/`)

Todo con el sistema `fx-*`, `FxBanner`, temas claro y oscuro, `prefers-reduced-motion`, teclado y lector de pantalla,
a 360, 390, 1024 y 1440 px.

### 9.1 Tipos y librería

- **`lib/agent.ts`:**
  - tipos de §7.2/§7.3: `SessionStateDto`, `CaseSessionDto`, `SessionSegmentDto`, `SessionGapDto`, `SessionMarkDto`,
    `SessionCapacity` y `CameraDevice` (F2);
  - `AgentErrorCode` += los 6 códigos de Tatana;
  - `RecordingStartedData`/`ScreenshotTakenData` += `session_mark?`;
  - `BuildZipRequest.files` acepta `zip_folder`;
  - métodos: `supportsRecordingSession()`, `supportsSessionCamera()`, `supportsSessionIos()` (`hasCapability`),
    `sessionCapacity(camera)`, `startSession(caseId, body)`, `stopSession(caseId, reason)`, `addSessionMark(caseId,
    text)`, `getCaseSession(caseId)` (404 → `null`), `agentRecordingSession()` (de `/agent/state`) y, en F2,
    `listCameras()`, `startCameraPreview()`, `stopCameraPreview()`, `cameraPreviewURL()` y `retrySessionCamera()`.
- **`lib/api.ts`:** `RecordingSession` y sub-tipos (§7.6.1), `Case.recording_session`, `ManifestFile.zip_folder?`, los
  5 tipos de evento y los campos de detail, `saveRecordingSessionSettings(caseId, body)` y `syncRecordingSession(caseId,
  snapshot)`.
- **`lib/recording-session.ts`** (nuevo, puro):
  - `REASON_LABEL` (textos de §8.4, con mayúscula inicial para la web);
  - `toBackendSnapshot(dto)` (§7.5: filtra los tramos `recording`/`closing` y renombra `serial` a `connection_id`);
  - `sessionFilenames(caseOrJournal)`;
  - `elapsedText(run_started_at, now)` (`HH:MM:SS`);
  - `capacityText(cap)` ("Espacio libre en esta PC: 120 GB (≈ 33 h de grabación)" con `formatBytes` de `lib/format.ts`;
    con `zip_on_same_volume` suma "contando la copia del ZIP");
  - `generationBlocker(state, journal, case)` → `null` | `{ text, action }`;
  - `defaultEnabled(mode)`.

### 9.2 `hooks/useRecordingSession.ts` (nuevo)

- **Entrada:** `currentCase`, `selDevice`, `step`, `publicConfig` (modo y tramo), `agentOnline`, `onSegmentSaved(seg)`
  (de `useFileManager`), `onCaseUpdated(case)` y `onError`.
- **Estado:**
  - `capability`: `"none"` | `"android"` | `"android+ios"` | `"+camera"`;
  - `state: SessionStateDto | null` (de Tatana);
  - `journal: CaseSessionDto | null`;
  - `capacity`;
  - `pendingSync: boolean`;
  - `busy` (`start`/`stop`/`mark`).
- **Fuentes de verdad:**
  - WS `session_state`, `session_segment_closed`, `session_mark_added` y `screenshot_taken.session_mark`, filtrados por
    `case_id` del caso abierto (`sameId`);
  - `/agent/state.recording_session` en `refresh()`, que se llama en los mismos momentos que `refreshRecording` (al
    entrar al paso 3, al reconectar el WS y al volver la pestaña al frente).
- **Sincronización con el backend** (debounce 1 s, una a la vez, encolada):
  1. `getCaseSession(caseId)`;
  2. `toBackendSnapshot`;
  3. `api.syncRecordingSession` → `onCaseUpdated`.

  Con sesión vencida (401 / `SessionExpiredError`), `pendingSync = true` y se reintenta con `onSessionRestored`, como
  `authPending` de `useFileManager`.
- **Tramo cerrado** (`session_segment_closed` con `saved`): `onSegmentSaved({ filename, size, sha256 })` → en
  `useFileManager`, `addStoredFile(name, { size, sha256 })`, que agrega a la bandeja con `storedInCase: true` y encola
  el guardado (§9.5). Después, sync.
- **Acciones:**
  - `ensureStarted(opts?)`: idempotente por entrada al paso;
  - `stop(reason)`: devuelve una promesa que espera el 200;
  - `mark(text)`;
  - `retryCamera()` (F2);
  - `setEnabled(enabled, withCamera)`: llama a `saveRecordingSessionSettings`. Si se apaga con la sesión activa, antes
    hace `stop("detenido_por_perito")`.

### 9.3 Paso 3 (`app/dashboard/page.tsx`, `CaptureStep.tsx`)

**Activación (D1, D3).** Al entrar al paso 3 con un caso `agent`, Tatana online, un dispositivo elegido y
`supportsRecordingSession`:
1. Sin `case.recording_session` y modo ≠ `disabled` → `saveRecordingSessionSettings({ enabled:
   defaultEnabled(mode), with_camera: false })`. En `required`, `enabled: true`.
2. Con `enabled` → `ensureStarted()` → `startSession` con `segment_minutes` del modo público.
   - Si responde 409 `screen_off`/`screen_locked`, se reutiliza `ScreenBlockNotice` con el texto "Para empezar a
     grabar la recolección, encendé y desbloqueá la pantalla del celular." y las acciones "Reintentar" / "Grabar
     igual" (`skip_screen_check`).
   - 507 → error en el bloque (§9.4).
   - 409 `session_active` de otro caso → `FxBanner` warn "Tatana está grabando la sesión de otro caso. Cerrala desde
     ese caso para grabar esta."

**Salida del paso 3** (`go(n)` con `n ≠ 3`, "Guardar evidencia en esta PC y continuar" y "Continuar sin guardar"):
- Si `state` está activo para este caso, antes de navegar se hace `await stop("cambio_de_paso")`, con el estado de
  carga "Cerrando la grabación de la sesión…" en el botón que se tocó (hasta 60 s).
- En "Guardar…", el stop va **antes** de `handleSaveAndContinue`, así el último tramo entra en la misma cola.
- Si el stop falla, se navega igual y queda el aviso del paso 5 (§9.6).

**Recarga o vuelta (HU, escenario "La página se recarga").** Con `/agent/state.recording_session` activa para este
caso, el indicador muestra "Grabando sesión desde HH:MM" (`session_started_at`) y no se llama a start de nuevo.

### 9.4 Componentes

- **`components/capture/SessionRecordingCard.tsx`:** tarjeta propia arriba de "Capturar del celular".
  - Título "Grabación de la recolección".
  - Switch "Grabar toda la recolección" (`role="switch"`, `aria-describedby`) con el texto "Se graba la pantalla del
    celular durante todo este paso, en tramos de {N} min con hash y sello cada uno."
  - Con `required`: switch encendido y `aria-disabled`, con "La configuración de esta instalación exige grabar la
    recolección."
  - `capacityText`. Con `can_start: false`: error en la tarjeta, "No hay espacio en el disco de esta PC para grabar la
    sesión (quedan X)".
  - Tatana viejo, iPhone sin capability o backend sin rutas: avisos de §7.8 (info, no bloqueantes).
  - F2: casilla "Sumar cámara del procedimiento", desplegable de cámaras, vista previa de 320 px (refresco de la imagen
    cada 1,5 s, pausado con `prefers-reduced-motion` y mostrando un botón "Actualizar vista"), texto de privacidad
    "Encuadrá las manos y el celular, no las caras. La cámara graba sin audio salvo que marques «Con micrófono de la
    PC»." y casilla "Con micrófono de la PC".
- **`components/capture/SessionIndicator.tsx`:** fijo arriba del contenido del paso 3 (sticky dentro del panel) y,
  si se navega dentro del caso con la sesión activa, compacto en el header del asistente.
  - Punto rojo (sin parpadeo con `prefers-reduced-motion`) y "Grabando sesión · {elapsedText} · tramo {i}", más el
    ícono de cámara si `camera_state === "recording"`.
  - El cronómetro se dibuja cada 1 s y es `aria-hidden`.
  - Un `role="status"` aparte anuncia **solo** los cambios: "Grabación de la sesión iniciada", "Tramo {i} guardado",
    "Grabación de la sesión reanudada", "Grabación de la sesión detenida".
  - Botón secundario "Detener grabación de sesión" con confirmación (`ConfirmDialog` existente): "Va a quedar un hueco
    en el registro. ¿Detener la grabación de la sesión?" → `stop("detenido_por_perito")`.
  - Con `stopped`/`interrupted` en el paso 3: botón "Reanudar" → `ensureStarted()`.
- **"Grabar" → "Marcar momento" (D5).** Con la sesión activa para este caso, el tile `recordTileRef` cambia a
  "Marcar momento" (ícono `Bookmark`) y abre `MarkMomentPopover`:
  - popover anclado al tile, no modal;
  - input "Qué pasó (opcional)" con `maxLength=300`;
  - "Guardar marca" (Enter);
  - al guardar, toast "Marca guardada · tramo {i}, minuto {mm:ss}".
  
  "Pantalla" no cambia. Sin sesión, "Grabar" funciona como hoy.
- **Avisos** (`FxBanner tone="warn"` salvo que se indique; nunca un modal que tape "Pantalla"; uno a la vez por tipo):

  | Aviso | Texto | Acción |
  |---|---|---|
  | `waiting_device` | "Cable desconectado: la grabación de la sesión se cortó a las HH:MM. Se reanuda sola al volver a conectar el mismo celular." | — |
  | Reanudación | `FxBanner tone="info"`, 8 s: "La grabación de la sesión se reanudó a las HH:MM (tramo {i})." | — |
  | `screen_off` / `screen_locked` | "La pantalla del celular está apagada / bloqueada. La grabación de la sesión sigue." | — |
  | `disk_low` | "Queda poco espacio en esta PC: {X} (≈ {minutes_left} min de grabación)." | — |
  | `stopped` + `sin_espacio` | `tone="error"`: "Se detuvo la grabación de la sesión por falta de espacio a las HH:MM. Liberá espacio en el disco y tocá «Reanudar»." | "Reanudar" |
  | `interrupted` | "Tatana se cerró a las HH:MM. El tramo en curso se recuperó y se guardó en el caso." | "Reanudar" |
  | `fallo_grabacion` | "La grabación de la sesión se interrumpió por un error a las HH:MM." | "Reanudar" |
  | `rotation_delayed` | "No se pudo abrir el tramo nuevo; el actual sigue grabando y se reintenta en un minuto." | — |
  | `camera_failed` (F2) | "La cámara dejó de grabar a las HH:MM." Con `detail: "permission"` suma: "Tatana no tiene permiso para usar la cámara: habilitalo en Ajustes del Sistema > Privacidad y seguridad > Cámara." | "Reintentar cámara" |

- **Bandeja (`EvidenceTray.tsx`):** grupo propio "Sesión" (los nombres de `sessionFilenames`) arriba de las capturas.
  - Cada tramo muestra "Pantalla · tramo {i} · HH:MM–HH:MM" y el estado de guardado y de sello (`timestampLine`),
    igual que el resto.
  - **Sin** botón de quitar (D-T6). La vista previa usa la del video de #73.
  - Los tramos no entran en "Capturas" ni en "Grabaciones".

### 9.5 Registro de los tramos (`hooks/useFileManager.ts`)

- `addStoredFile(name, { size, sha256 })`: agrega un `CapturedFile` con `storedInCase: true`, `sha256` y `size`, y lo
  encola. `saveOne` ya registra directo sin reimportar (camino `storedInCase && sha256 && size`).
- `restoreCaseEvidence` sigue igual: si un tramo quedó en la carpeta sin registrar (web cerrada), se registra como
  "extra".
- `removeFile` de un nombre de la sesión no se ofrece en la UI. Si llega igual, el 409 se muestra con el texto del
  backend.

### 9.6 Paso 5 (`GenerateStep.tsx`)

- El checklist suma "Grabación de la sesión cerrada y guardada" cuando el caso tiene `recording_session.enabled`.
- `generationBlocker`:
  - sesión activa en Tatana para el caso;
  - o tramos del diario de Tatana sin registrar;
  - o `pendingSync`.
- Con bloqueo, "Generar" queda deshabilitado con el motivo "Primero cerrá la grabación de la sesión" y la acción
  "Cerrar grabación de la sesión" (`stop("cambio_de_paso")` + esperar el registro).
- El 409 `recording_session_open` del backend muestra el mismo texto.
- Para casos grandes, el texto de progreso del ZIP suma "Con la grabación de la sesión, armar el ZIP puede tardar
  varios minutos." si la suma de los tramos supera 2 GiB.

### 9.7 Resultado y detalle del caso

`components/case-detail/SessionRecordPanel.tsx`, usado en `ResultStep` y `CaseDetailContent`, solo si
`recording_session` tiene tramos:
- título "Registro audiovisual";
- resumen: "{n} tramos · {duración total} · {k} huecos · sellos {s}/{t}";
- desplegable (`<details>` accesible) con la lista de tramos (pista, inicio–fin, duración, archivo, SHA-256 truncado
  con copiar, sello);
- huecos con su motivo (`REASON_LABEL`);
- marcas;
- la línea del reloj de §8.4 si hay ficha.

### 9.8 Trazabilidad (`lib/case-events.ts`)

| Tipo | Ícono | Texto |
|---|---|---|
| `recording_session_settings_changed` | `Video` | "Se activó/desactivó la grabación de la sesión" (+ " con cámara") |
| `recording_session_started` | `Circle` rojo | "Empezó la grabación de la sesión (pantalla/cámara)" |
| `recording_session_segment_closed` | `Film` | "Se guardó el tramo {i} de la {pantalla/cámara}" + duración y, si `validation === "incomplete"`, "(incompleto)" |
| `recording_session_stopped` | `PauseCircle` | "Se cortó la grabación de la sesión: {motivo}" |
| `recording_session_resumed` | `PlayCircle` | "Se reanudó la grabación de la sesión tras {gap}" |

### 9.9 Accesibilidad y criterios visuales

- Los textos de §9.4 son exactos.
- Los avisos no roban foco.
- El indicador mide ≥ 44 px de alto y el contraste es AA en los dos temas.
- `MarkMomentPopover` se cierra con Esc, devuelve el foco al tile y usa `aria-expanded`/`aria-controls`.

---

## 10. `agent-ui/` [DU3=A]

- **F1, `src/main/agent-api.ts`:** `prepareShutdown(port, timeoutMs = 25000)` → `POST /agent/prepare-shutdown`. Va
  sin `Origin` (proceso main, como `maintenance`). Devuelve `{ ok, had_session }`.
- **F1, `src/main/index.ts`:** función nueva `confirmSessionShutdown(): Promise<boolean>`.
  1. `getAgentState(port)` (ya existe en `agent-api.ts`, timeout 2 s). `AgentStateResponse` suma
     `recording_session?`.
  2. Si `recording_session.state` es uno de los activos, `dialog.showMessageBox` con el texto "Hay una grabación de la
     sesión en curso. Si cerrás Tatana, la grabación se corta y queda registrado el hueco en el caso." y los botones
     ["Cancelar", "Cerrar Tatana"].
  3. Con "Cancelar" devuelve `false`. Con "Cerrar Tatana": `await prepareShutdown(port)` (ignora errores) y devuelve
     `true`.
  4. Sin sesión o sin respuesta de Tatana, devuelve `true` sin diálogo.

  La usan dos puntos:
  - `requestQuit()` (ítem "Salir"): `if (!(await confirmSessionShutdown())) return` al principio, antes de
    `installNow`/`app.quit()`;
  - "Reiniciar agente .NET" (~L139): `click: async () => { if (!(await confirmSessionShutdown())) return;
    agentProcess.stop(); … }`.

  El camino de la actualización no cambia: con una sesión activa, `/agent/maintenance` ya responde `agent_busy`.
- **F2, `package.json` → `build.mac.extendInfo`:**
  - `NSCameraUsageDescription`: "Tatana usa la cámara para grabar el procedimiento de recolección cuando el perito lo
    activa."
  - `NSMicrophoneUsageDescription`: "Tatana usa el micrófono solo cuando el perito lo activa para una grabación."

---

## 11. Configuración y despliegue

- **Backend:**
  - `appsettings.json` suma `"Recording": { "SessionMode": "optional_off", "SegmentMinutes": 30 }`;
  - compose de nube y de Windows: las dos líneas de §6.6;
  - `deploy/cloud/factum.env.example` y `deploy/windows/.env.example`: las dos variables comentadas, con valores
    posibles;
  - `deploy/windows/scripts/restaurar.ps1`: agregar las dos claves a la lista fija;
  - `deploy/windows/tests/comun.Tests.ps1`: el caso que verifica la lista.
- **Tatana:** `appsettings.json` suma `"Agent": { "Session": { … defaults de §5.14 … } }`, sin `Mock*`.
- **`README.md`:** sección "Grabación de la recolección" (modo, tramo, espacio, entrega en `sesion/`, pérdida posible
  ante un corte de luz y audio del celular durante la sesión).
- **`AGENTE_TATANA.md`:** casos de uso nuevos (sesión, marcas, recuperación, `prepare-shutdown`).

---

## 12. Compatibilidad, versiones y release

### 12.1 Documentos existentes

`Case.RecordingSession` es anulable, con `[BsonIgnoreIfNull]` y sin backfill. Los documentos viejos deserializan
`null`. `CaseEventDetail` suma campos anulables.

### 12.2 Datos en la PC

- `sessions/` es una carpeta nueva.
- Los diarios se borran **solo** al confirmar el ZIP de su caso y si están vacíos de tramos (§5.10).
- Nada toca `cases/` ni `EvidenceDirectory` de otros casos.

### 12.3 Clientes y Tatana viejos

Ver §7.8.

### 12.4 Release de Tatana

- Necesita una release nueva: 1.7.0 si todavía no salió con #78/#79, o 1.8.0.
- Para exigir la función en una instalación: `FACTUM_TATANA_VERSION_MINIMA` = esa versión.
- F2 sale en la siguiente.

---

## 13. Decisiones técnicas

| # | Decisión | ¿Usuario? |
|---|---|---|
| D-T1 | Tatana es dueño del diario de la sesión (disco, por caso); el backend guarda una copia validada y **append-only** por `id`. La web es el puente, porque Tatana no tiene JWT (mismo modelo de confianza que el manifiesto). | no |
| D-T2 | Servicio de sesión propio (no el `_snap` de `AdbService`), reutilizando sus helpers estáticos (`BuildScrcpyArgs`, `ProcessStop`, `RemuxAsync`, `RecordingValidation`). Así la sesión y una grabación puntual de **otro** celular pueden convivir. | no |
| D-T3 | Corte solapado en Android y manejo de "pantalla siempre encendida" (§5.4–§5.5). | **DU2** |
| D-T4 | Staging en `<DataDirectory>/sessions/<caseId>/` y rename al cerrar, en el mismo volumen: el tramo en curso nunca aparece como evidencia. | no |
| D-T5 | Nombres `sesion_<pista>_<NNN>_<fecha>_<hora>.mkv`. La clasificación (informe, conteos, ZIP) es por **pertenencia al diario**, no por prefijo. Tatana reserva el prefijo para subidas e importaciones. | no |
| D-T6 | Los tramos no se pueden borrar ni quitar del caso (Tatana 409, backend 409, UI sin botón): son el registro de custodia. | no (avisar al usuario) |
| D-T7 | La recuperación cierra limpio los procesos huérfanos identificados por PID + hora de inicio antes de recurrir al remux (§4.2.6). | no |
| D-T8 | Cámara con segment muxer de ffmpeg, lista CSV `+live` y `flush_packets=1:cluster_time_limit=2000` (§4.5): sin hueco al cortar y con pérdida ≤ ~2 s ante un kill. | no |
| D-T9 | iPhone: helper con `rotate`/`snap`, salida MKV, y las capturas de la sesión son el cuadro DVT del grabador (no hace falta verificar dos canales DVT). | no |
| D-T10 | ZIP: `zip_folder` por ítem lo decide el backend en `prepare`; `EvidenceZip` acepta nombres de entrada `sesion/<nombre>` en las dos copias. | no |
| D-T11 | Informe: segunda tabla clonada de la de hashes; los tramos no van en la tabla general, ni en los conteos, ni en "n de m" del sello. | no |
| D-T12 | Eventos derivados del diff del diario al sincronizar (hora del servidor en `timestamp` y hora real en `detail.at`). | no |
| D-T13 | Motivo nuevo `fallo_grabacion` (scrcpy sale con el celular conectado) y `rotacion` solo si DU2=B. | no |
| D-T14 | `/agent/state.recording_session` y la operación `recording_session`, que bloquea el auto-update. | no |
| D-T15 | La marca automática de captura la crea Tatana dentro del `screenshot`, atómica para cualquier pestaña. | no |
| D-T16 | Capabilities separadas: `recording_session_v1` (F1), `recording_session_camera_v1` y `recording_session_ios_v1` (F2). | no |
| D-T17 | Modo y duración del tramo son config de instalación (backend → config pública → start). Los parámetros por PC van en `Agent:Session`. | no |
| D-T18 | Sin backfill: `Case.RecordingSession` anulable. | no |
| D-T19 | Guardia de generación también en el backend (`recording_session_open`), no solo en la web. | no |
| D-T20 | La estimación de horas considera la copia del ZIP solo si va al mismo volumen. | no |
| D-T21 | Audio de la pantalla igual que los clips: interno en Android 11+ y nunca el micrófono de la PC en la pista de pantalla. | no |
| D-T22 | `agent-ui`: diálogo + `prepare-shutdown`. | **DU3** |
| D-T23 | Con Tatana viejo y modo `required`: aviso no bloqueante; para exigirla se usa `FACTUM_TATANA_VERSION_MINIMA`. | no |
| D-T24 | `WebcamService` / `POST /webcam/capture` quedan como están (código muerto, fuera de alcance); la cámara de la sesión es código aparte. | no |
| D-T25 | AirPlay fuera de la sesión. | **DU4** |
| D-T26 | Partir en F1/F2. | **DU1** |

---

## 14. Tests (sin hardware en CI)

Ningún test necesita un celular ni una cámara. Los que necesitan `ffmpeg` o `python3` usan el patrón de
`SyntheticRecorderFactAttribute`: se saltean si no están, y en CI de macOS/Windows están.

### 14.1 Tatana (`server/tests/Factum.Agent.Tests/Session/`)

| Archivo | Qué cubre |
|---|---|
| `SessionNamesTests` | Formato y regex de nombres, numeración con 3 o 4 dígitos, prefijo reservado (`RequireNewName` rechaza `sesion_x.png`). |
| `SessionDiskBudgetTests` | Requisito de inicio, `minutes_left`, umbral de aviso, de corte, factor 2 con el mismo volumen y fail-open sin medición. |
| `SessionJournalTests` | Ida y vuelta del JSON (nombres snake_case exactos de §7.2.6), escritura atómica, un `.tmp` huérfano, JSON ilegible → renombre. |
| `SessionStateMachineTests` | Con fakes de `ISessionTrack` y `TimeProvider`: start → corte → corte demorado (sin hueco) → desconexión → `waiting_device` → reconexión del **mismo** serial (otro serial no reanuda) → stop; idempotencia del start y del stop; `session_active` con otro caso; `already_recording`. |
| `GapRulesTests` | Cierre de hueco, `cambio_de_paso` → `retomado_otro_dia` por fecha local distinta, `fallo_grabacion` con reintento. |
| `MarkRulesTests` | Tramo y offset, solapamiento (gana el más nuevo), texto inválido, 409 sin sesión. |
| `StayAwakeKeeperTests` | Con `ProcessRunner` falso: lee el original, reafirma solo si ≠ 7, restaura solo si es 7 y `owned`, `restore_pending` sin celular. |
| `SessionRecoveryTests` | Diarios de fixture con tramos `recording`/`closing` y archivos generados con ffmpeg: MKV válido → `saved` sin remux; MKV truncado (los primeros N bytes de uno válido) → remux → `saved`, `recovered`; 0 B → `discarded` + `segment_lost`; ilegible → `descartes/`. Proceso "huérfano": un `ffmpeg -re -f lavfi …` lanzado por el test con su PID en el diario → se cierra limpio (SIGINT; en Windows el helper Ctrl+C) y el archivo queda válido. PID de otro proceso → no se toca. |
| `MkvTruncationTests` [ffmpeg] | Reproduce §4.5: un ffmpeg escribiendo MKV con `flush_packets=1:cluster_time_limit=2000`, kill a los 4 s → el remux recupera ≥ 1 s. |
| `CameraSegmentListTests` [ffmpeg] (F2) | Segment muxer con lavfi y `segment_time` de 3 s: el lector de `camara.csv` detecta cada tramo recién cuando está cerrado y los nombres finales son correctos. |
| `CameraDevicesTests` (F2) | Parser de avfoundation con la salida **real** de §4.5 (fixture `Fixtures/Cameras/avfoundation-macbook-air.txt`; excluye `Capture screen`) y `DshowDevices.ParseVideoDevices` con los fixtures existentes de `DshowDevicesTests` (nuevo y viejo). |
| `DvtRecorderTests` (F2, `--synthetic`) | `rotate` → 2 MKV válidos y contiguos; `snap` → PNG válido; EOF de stdin → cierre limpio. |
| `EvidenceZipTests` (+ espejo en el backend) | Entradas `sesion/…` con AES y sin contraseña, `IsAllowedEntryName` (rechaza `../x`, `otra/x`, `sesion/sub/x`) y verificación por nombre de entrada. Un test **opcional** `[Trait("Category","Large")]` con una entrada de 4,1 GiB generada al vuelo (se saltea por defecto). |
| `CaseZipServiceTests` | `zip_folder` válido o inválido, unicidad por nombre de entrada, `files` devuelto, borrado del diario al confirmar (solo si está vacío). |
| `CaseEvidenceStoreTests` | `session_file_protected` y prefijo reservado. |
| `HealthCapabilitiesTests` | La lista nueva. |
| `AgentStateTests` | `recording_session` presente u omitido y `session_recovery`. |

### 14.2 API (`server/tests/Factum.Backend.Tests/RecordingSession/`)

| Archivo | Qué cubre |
|---|---|
| `RecordingSessionRulesTests` | Validación campo por campo (cada `field` de §6.2), merge append-only, `conflict` por sha distinto, huecos que se cierran una sola vez, marcas inmutables, `PendingForGeneration`. |
| `RecordingSessionEventsTests` | Los `changes` → los 5 tipos con el `detail` exacto. |
| `RecordingSessionOptionsTests` | Defaults, valores inválidos → default + warning. |
| `PublicConfigOrderTests` | El test de orden existente, con los 2 campos al final. |
| `ReportSessionTextsTests` | Textos exactos de §8.4 (con o sin cámara, con o sin sellado, con o sin reloj, huecos abiertos y cerrados, marcas de los dos tipos, DU2=A). |
| `ReportServiceSessionDocxTests` | Con la plantilla v6 real: caso con sesión → bloque presente, segunda tabla con N filas, sin filas de tramos en la tabla general, `cantidadGrabaciones` sin tramos. Caso sin sesión → DOCX sin el bloque (sin `{#registroAudiovisual}` ni texto residual). |
| `ManifestZipFolderTests` | `ToManifestFiles` con `zip_folder` y `Matches` que ignora `zip_folder`. |
| `TimestampReadmeSessionTests` | Línea "Dentro del ZIP: \"sesion/…\"". |
| Tests de Mongo | Si se agregan, sobre base efímera `factum_test_<guid>` como los existentes, limpiando por `_id`. |

### 14.3 Web y agent-ui

- No hay infraestructura de tests: `npx tsc --noEmit` y la pasada manual con Tatana en mock.
- Las funciones de `lib/recording-session.ts` quedan puras y exportadas para cuando haya tests.

---

## 15. Checklists atómicos

Cada ítem se tilda en `progress/impl_<lado>_grabacion-sesion-completa.md`. En F2 los implementadores agregan una
sección "F2" al mismo archivo.

### 15.1 `implementer-backend` — F1 (Tatana + API + plantillas + deploy + docs)

**Tatana**
- [ ] `Services/Session/SessionNames.cs` + tests.
- [ ] `SessionOptions` en `AgentModels.cs` (`Agent:Session:*`, `Normalize`, `Clone`) + `appsettings.json`.
- [ ] `SessionModels.cs`: DTOs de §7.2.6/§7.2.7 con los nombres exactos y `process`/`stay_awake` internos (no salen por
  HTTP).
- [ ] `SessionJournal.cs` (atómico, lock por caso, ilegible → renombre) + tests.
- [ ] `SessionDiskBudget.cs` (puro) + tests.
- [ ] `StayAwakeKeeper.cs` [DU2=A] + tests.
- [ ] `AndroidScreenTrack.cs`: start con marcador, corte solapado [DU2=A] o secuencial [DU2=B], `Exited`, mock.
- [ ] `SessionRecorder.cs`: estados (§5.3), temporizador con `TimeProvider`, cola de cierre (§5.6), desconexión y
  reconexión (§5.7), avisos de pantalla y disco (§5.7/§5.8), marcas (§5.9), latido, `ShutdownAsync`,
  `IOperationSource`.
- [ ] Tests de la máquina de estados, huecos y marcas con fakes.
- [ ] `SessionRecovery.cs` (§5.10) + tests con ffmpeg.
- [ ] `CaseSessionController.cs` (start/stop/marks/GET) y `SessionCapacityController.cs` con los códigos de §7.7.
- [ ] `AgentStateController`: `recording_session`, `session_recovery` y `POST /agent/prepare-shutdown` (solo local).
- [ ] `RecordingController.Start`: 409 `session_active`.
- [ ] `ScreenshotController`: marca automática y `session_mark` en la respuesta y en el evento.
- [ ] `CaseEvidenceStore`: prefijo reservado y `session_file_protected` en `Delete` (consulta `SessionJournal`).
- [ ] `EvidenceZip` (Tatana): `EvidenceZipEntry`, overload, `IsAllowedEntryName` + tests.
- [ ] `CaseZipService`: `ZipFileItem.ZipFolder`, entradas, devolución y borrado del diario en el commit + tests.
- [ ] `Program.cs`: DI, `SessionRecovery` en segundo plano, `OnDevicesPolled` en el bucle de 3 s, `ShutdownAsync`
  primero en `ApplicationStopping`.
- [ ] `HealthController.Capabilities` += `recording_session_v1` (+ test).
- [ ] `AgentErrorCodes` += 6 códigos.

**API**
- [ ] `Models/RecordingSession.cs` + `Case.RecordingSession` + exclusión en la proyección del listado.
- [ ] `RecordingSessionOptions` (`Recording:*`) + `appsettings.json` + DI + warnings al arrancar.
- [ ] `PublicConfigResponse` += 2 campos al final + `ConfigController` + test de orden.
- [ ] `RecordingSessionRules.cs` (validación, merge, `SessionFilenames`, `PendingForGeneration`) + tests.
- [ ] `DTOs/RecordingSessionDtos.cs` (requests de §7.5).
- [ ] `RecordingSessionFlow.cs` (`partial class CaseService`): settings y sync, eventos, reintentos optimistas.
- [ ] `MongoCaseRepository`: `SetRecordingSessionSettingsAsync`, `SetRecordingSessionAsync` (bajo `Editable(id)`).
- [ ] `CasesController`: 2 rutas.
- [ ] `CaseEvent`: 5 tipos y 10 campos de detalle.
- [ ] `AgentGeneration.PrepareGenerationAsync`: `recording_session_open`.
- [ ] `EvidenceManifest.ToManifestFiles`: `zip_folder`.
- [ ] `ManifestFileDto.ZipFolder`.
- [ ] `CaseService.DeleteEvidenceAsync`: `recording_session_file`.
- [ ] `EvidenceZip` (backend): paridad con Tatana + tests espejo.
- [ ] `TimestampReadme` + `CaseTimestampsController`: `sesion/` + test.
- [ ] `ReportSessionTexts.cs` + tests de textos.
- [ ] `ReportService`: placeholders, bloque, `BuildHashRow` parametrizado, `FillSessionTable`, separación general /
  sesión. `ReportValues.Counts` sin tramos. Test DOCX.

**Plantillas, deploy y docs**
- [ ] `build_plantilla_v4.py` y `build_plantilla_v6.py` (§8.1–§8.2) → regenerar la v4 y la v6 → `--check` OK →
  `README.md` de plantilla.
- [ ] Deploy (§11) y `README.md`/`AGENTE_TATANA.md`.

### 15.2 `implementer-frontend` — F1 (`client/` + `agent-ui/`)

- [ ] Skills obligatorios: `ui-ux-pro-max` antes del JSX final, `senior-frontend`, `3d-web-experience` (como criterio,
  sin 3D) y `web-design-guidelines` como autochequeo. Se dejan anotados en el progress.
- [ ] `lib/agent.ts`: tipos, códigos y métodos de §9.1 (F1).
- [ ] `lib/api.ts`: tipos de §7.6, `zip_folder`, eventos y 2 métodos.
- [ ] `lib/agent-messages.ts`: los 6 códigos de Tatana + `recording_session_open`.
- [ ] `lib/recording-session.ts` (puro).
- [ ] `hooks/usePublicConfig.ts`: modo y tramo.
- [ ] `hooks/useRecordingSession.ts` (§9.2).
- [ ] `hooks/useFileManager.ts`: `addStoredFile`.
- [ ] `SessionRecordingCard.tsx`, `SessionIndicator.tsx` y `MarkMomentPopover.tsx` (§9.4).
- [ ] `CaptureStep.tsx`: tarjeta, indicador, "Marcar momento", avisos, `ScreenBlockNotice` para la sesión.
- [ ] `EvidenceTray.tsx`: grupo "Sesión" sin quitar.
- [ ] `app/dashboard/page.tsx`: activación al entrar, stop al salir (todas las salidas), header compacto.
- [ ] `GenerateStep.tsx`: checklist, bloqueo y acción (§9.6).
- [ ] `SessionRecordPanel.tsx` en `ResultStep` y `CaseDetailContent`.
- [ ] `lib/case-events.ts`: 5 tipos.
- [ ] `agent-ui` [DU3=A]: `agent-api.ts` (`prepareShutdown`, `AgentStateResponse.recording_session?`) e `index.ts`
  (`confirmSessionShutdown` en `requestQuit` y en "Reiniciar agente .NET").

### 15.3 `implementer-backend` — F2

- [ ] `CameraDevices.cs` (avfoundation sin `Capture screen`, dshow `ParseVideoDevices`, v4l2, mock) + fixture real +
  tests.
- [ ] `CameraTrack.cs` (§5.16): args, reintento sin tamaño, lector CSV, falla y reintento, vista previa + tests con
  lavfi.
- [ ] `CamerasController.cs` y `camera/retry`. Start con `camera`. Hueco `camara_fallo`.
- [ ] `ios_helper.py`: MKV + flush, `rotate`, `snap`, EOF. `DvtRecorder.RotateAsync/SnapAsync` + tests `--synthetic`.
- [ ] `IosScreenTrack.cs`. `IosService.TakeScreenshotAsync` con sesión → `snap`. Start `platform: "ios"` solo
  `video_only`.
- [ ] Capabilities `recording_session_camera_v1` y `recording_session_ios_v1`. Recuperación con `ffmpeg`/`python` en
  la verificación de procesos huérfanos.
- [ ] `AGENTE_TATANA.md`.

### 15.4 `implementer-frontend` — F2

- [ ] Skills obligatorios (igual que F1).
- [ ] `lib/agent.ts`: cámaras, preview y retry.
- [ ] `SessionRecordingCard`: cámara (lista, preview, privacidad, micrófono).
- [ ] Indicador con cámara, aviso `camera_failed` + "Reintentar cámara".
- [ ] iPhone: oferta con `recording_session_ios_v1` y aviso sin ella.
- [ ] `agent-ui/package.json` → `extendInfo` (§10).

---

## 16. Verificación

### 16.1 Antes de declararse `done`

**`implementer-backend`**
```bash
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet test  server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj
dotnet test  server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
python3 ops/plantilla/build_plantilla_v4.py --check "docs/INFORME PERICIAL TÉCNICO INFORMÁTICO - FACTUM.docx" server/src/Factum.Backend/Templates/plantilla_informe_v4.docx
python3 ops/plantilla/build_plantilla_v6.py --check server/src/Factum.Backend/Templates/plantilla_informe_v4.docx server/src/Factum.Backend/Templates/plantilla_informe_v6.docx
```

Más:
- `Invoke-Pester deploy/windows/tests` si hay `pwsh`; si no, se deja constancia.
- Sin warnings nuevos en el build.
- **Prueba en vivo opcional** contra el SM-M127F con un Tatana compilado desde la rama **en otro puerto** (`--port
  8766 --data <scratch>`), **nunca** el de :8765:
  - `MockSegmentSeconds` no aplica fuera de mock, así que se usa `segment_minutes: 5` (mínimo) y se graba 6 min;
  - se adjunta el diario resultante y `ffprobe` de los dos tramos;
  - se verifica que `stay_on_while_plugged_in` vuelve a su valor original.

**`implementer-frontend`**
```bash
cd client && npx tsc --noEmit
cd agent-ui && npx tsc --noEmit -p tsconfig.web.json && npx tsc --noEmit -p tsconfig.node.json
```

Más una pasada en el navegador con Tatana en mock (`Agent__Mock=true`, `Agent__Session__MockSegmentSeconds=60`,
`Agent__Session__MockFreeBytes=…`), si el entorno lo permite.

El orquestador corre `./ops/harness/verify.sh` antes de mover la fase.

### 16.2 Prueba manual del usuario (F1)

Preparación: backend, web y **Tatana compilado desde esta rama**. Samsung SM-M127F conectado y desbloqueado. Para no
esperar 30 min por cada corte: `FACTUM_GRABACION_SESION_TRAMO_MINUTOS=5` (o `Recording__SegmentMinutes=5`) en el
backend.

1. **Activación.**
   - Caso nuevo → paso 3: aparece la tarjeta "Grabación de la recolección" apagada (modo `optional_off`) con el espacio
     estimado.
   - Encenderla: arranca, aparece el indicador "Grabando sesión · 00:00:0x · tramo 1" y la trazabilidad suma "Se activó
     la grabación de la sesión" y "Empezó la grabación…".
2. **Pantalla apagada al empezar.**
   - Apagar la pantalla, volver al paso 2 y entrar de nuevo al 3: aviso "Para empezar a grabar la recolección,
     encendé…" con "Reintentar" / "Grabar igual".
3. **Marcas y capturas.**
   - Tocar "Pantalla" dos veces: las capturas entran como siempre y la sesión sigue.
   - "Marcar momento" con texto → toast "Marca guardada · tramo 1, minuto…".
   - "Grabar" no aparece mientras graba la sesión.
4. **Corte.** Esperar más de 5 min:
   - el indicador pasa a "tramo 2" y anuncia "Tramo 1 guardado";
   - en la bandeja, grupo "Sesión", el tramo 1 aparece guardado, con hash y sello (si hay TSA);
   - **[DU2=A]** en el celular, Ajustes > Opciones de desarrollador > "Pantalla activa" sigue activada durante el tramo
     2.
5. **Desconexión.**
   - Desenchufar el USB: aviso "Cable desconectado: la grabación de la sesión se cortó a las HH:MM…".
   - Volver a enchufar: "La grabación de la sesión se reanudó…" con tramo 3. En la trazabilidad, "Se cortó… se
     desconectó el cable USB" y "Se reanudó… tras N s".
6. **Recarga.** Recargar la página en el paso 3: "Grabando sesión desde HH:MM" y no se duplica nada.
7. **Sesión web vencida (#74).** Con un JWT corto, dejar vencer la sesión con la grabación en curso: los tramos
   cerrados quedan "Pendiente de registrar". Al volver a iniciar sesión, se registran.
8. **Salir del paso.**
   - "Guardar evidencia en esta PC y continuar": "Cerrando la grabación de la sesión…" y después paso 4.
   - Volver al paso 3: tramo nuevo y hueco "el perito salió del paso de captura".
9. **Detener a mano.** "Detener grabación de sesión" → confirmación → hueco "el perito detuvo la grabación" →
   "Reanudar".
10. **Tatana cerrado.**
    - Con la sesión grabando, cerrar Tatana desde su ventana: **[DU3=A]** aparece el diálogo; confirmar.
    - Reabrir Tatana y volver al caso: aviso "Tatana se cerró a las HH:MM. El tramo en curso se recuperó…" y el tramo
      aparece guardado.
    - Repetir matando el proceso `Factum.Agent` desde el Monitor de Actividad (cierre de golpe): al reabrir, el tramo
      se recupera completo porque scrcpy seguía vivo.
11. **Espacio.** Con Tatana en mock y `Agent__Session__MockFreeBytes` bajo:
    - aviso "Queda poco espacio…";
    - por debajo del corte, "Se detuvo… por falta de espacio…".
12. **Generar.**
    - En el paso 5 con la sesión abierta: "Generar" deshabilitado con "Primero cerrá la grabación de la sesión".
    - Cerrarla y generar.
    - Abrir el ZIP con 7-Zip y la contraseña: los tramos están en `sesion/`.
    - `sellos/LEEME-verificacion.txt` dice "Dentro del ZIP: \"sesion/…\"".
13. **Informe.**
    - El DOCX tiene "Registro audiovisual de la recolección" con los párrafos, la tabla de tramos (pista, horario,
      resultado, sello y SHA-256), los huecos con sus motivos, las marcas (incluida "Captura screenshot_…") y la línea
      del reloj ("el dispositivo adelantaba ~10 s…").
    - La tabla general de hashes **no** repite los tramos.
    - En el paso 6 y en el detalle aparece el bloque "Registro audiovisual".
14. **Windows** (si hay una PC con Windows): repetir los pasos 1, 4, 5 y 10. En el 10, matar `Factum.Agent.exe` desde
    el Administrador de tareas y confirmar que el tramo se recupera (§4.2.6 en Windows).
15. **Tatana viejo.** Con Tatana 1.6.0: aviso "Para grabar toda la recolección hace falta actualizar Tatana." y el paso
    3 funciona como siempre.
16. **Caso anterior.** Un caso generado antes de esta versión se ve y se descarga igual, sin la sección.
17. **Celular.** `adb shell settings get global stay_on_while_plugged_in` da el mismo valor que antes de la prueba.

**F2** (en su ciclo):
- **Cámara:** lista sin "Capture screen", vista previa, permiso de macOS, tramos alineados (±2 s) con los de la
  pantalla, desenchufar la cámara (aviso + "Reintentar cámara") y la frase de D11 en el informe.
- **iPhone:** `video_only`, captura durante la sesión (el PNG coincide con un cuadro del video), corte sin hueco y
  recuperación al cerrar Tatana.

---

## 17. Regla dura de datos (AGENTS.md)

- La base de desarrollo puede tener casos cargados a mano por el usuario: **ningún implementador borra ni modifica
  documentos de negocio preexistentes**.
- Las pruebas limpian **solo por los `_id` que insertaron**, nunca con filtros amplios. Los tests de Mongo usan una base
  efímera propia.
- `Storage:DataDirectory`, `Agent:DataDirectory` (incluida la nueva `sessions/`), `EvidenceDirectory` y `descartes/`
  pueden tener evidencia real: **no se borra nada que no haya creado la propia prueba**.
- Las pruebas en vivo de Tatana usan **otro puerto y otra carpeta de datos** (`--port 8766 --data <scratch>`). El
  Tatana del usuario en :8765 no se toca.
- **El celular:** solo lectura, salvo la escritura que ya hace scrcpy y que esta HU reafirma y restaura
  (`stay_on_while_plugged_in`, [DU2=A]). Ningún otro ajuste, instalación ni cambio de hora. Al terminar una prueba, el
  valor tiene que quedar como estaba (paso 17 de §16.2).
- La cámara de la PC se abre solo en la prueba manual de F2, con el usuario presente.

---

## 18. Riesgos

| Riesgo | Mitigación |
|---|---|
| **Corte de luz o cuelgue de la PC**: scrcpy pierde lo que no escribió (hasta 256 KiB; con la pantalla quieta, minutos o todo el tramo en curso) (§4.2.5). | Se recupera lo legible con remux. El hueco dice "el tramo en curso no se pudo recuperar" si se descarta. Se documenta en `README.md`. Los tramos ya cerrados no se ven afectados (D4). No hay opción de scrcpy para achicar el búfer. |
| Electron en Windows mata a Tatana sin aviso. | DU3=A (`prepare-shutdown`) + recuperación de huérfanos. Si libuv metiera a scrcpy en su job y lo matara con Electron, queda el caso de arriba. **Verificar en el paso 14.** |
| `stay_on_while_plugged_in` queda en 7 si Tatana se cae y el celular no vuelve a esa PC [DU2=A]. | `restore_pending` en el diario, restauración al reconectar o en la recuperación, y paso 17 de la prueba. Alternativa DU2=B. |
| Tramos `incomplete` con la pantalla quieta: el MKV dura menos que lo grabado si al final no hubo cuadros. | Misma tolerancia que #61. El informe lo dice ("el video dura X de Y grabados") sin calificar. Se recomienda dejar la pantalla encendida. |
| Generar casos de 10 GiB o más tarda minutos (~3,5 min en NVMe; más en HDD). | Progreso existente + texto de §9.6. |
| Dos encoders a la vez durante ~0,7 s en cada corte [DU2=A]. | Medido sin problemas en un equipo de gama baja (SM-M127F). En otros equipos, si el corte falla, el tramo actual sigue y se reintenta (`rotation_delayed`). |
| Audio interno: mientras se graba la sesión, el celular puede no reproducir sonido (Android 11–12 con `--audio-source=output`). | Es lo mismo que con los clips de hoy (D12 b de #61). Queda en `README.md`. |
| Cámara: permisos de macOS y resoluciones no verificados (F2). | Reintento sin `-video_size`, aviso con `detail: "permission"`, `extendInfo` y prueba manual F2. |
| iPhone: la recuperación y `rotate` dependen del helper (F2). | Tests `--synthetic` en CI. Sin iPhone no se libera `recording_session_ios_v1`. |
| Reloj de la PC desfasado: las horas de los tramos son de la PC. | El informe lo dice y usa la medición de #79 (PC ↔ servidor y celular ↔ PC). |
| Un diario muy largo (miles de marcas) agranda el documento del caso. | Topes de §6.2 y exclusión del listado. |

---

## 19. Fuera de alcance (confirmado por la HU)

- Exportación JSON de la sesión (#81: el diario ya tiene la forma que necesita).
- Edición, recorte, unión o extracción de tramos.
- Transcodificar, combinar pantalla y cámara o cambiar el bitrate de scrcpy.
- Grabar el escritorio de la PC (por eso se excluyen las entradas `Capture screen`).
- Transmisión en vivo o subida al servidor.
- Firma digital del perito sobre los videos.
- Cambios en las capturas, clips y filmación con cámara externa fuera de la sesión.
- Grabar los pasos 1, 2 y 4.
- AirPlay en la sesión [DU4=A].
- Limpiar `WebcamService` / `POST /webcam/capture` (D-T24).
