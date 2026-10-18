# SDD: Grabación de la sesión completa, fase 2 — cámara del procedimiento (ffmpeg en Tatana) y criterio único de duración (B-9)

**Slug:** `grabacion-sesion-completa-f2` · **Issue:** #85 · **HU:** `docs/hu-grabacion-sesion-completa-f2.md` (validada el
2026-10-15) · **Autor:** architect · **Fecha:** 2026-10-15 · **Rama:** `feat/grabacion-sesion-completa-f2` (sobre
`develop` en `04bb57f`, con #80 mergeada)

**Base:** `Refactorizaciones/grabacion-sesion-completa.md` (la SDD de F1, citada como **F1 §n**). Este documento es un
**delta**: reemplaza F1 §5.16 y §7.2.10, ajusta F1 §5.8, §5.9, §5.11, §5.14, §5.15, §7, §8.4, §9.4, §9.7 y §10, y
reemplaza F1 §15.3/§15.4. Lo que no se menciona sigue como en F1.

**Alcance validado (HU, "Validación del usuario"):** D8 = C → **solo la cámara del procedimiento + B-9**. El iPhone
(DVT `video_only`), D6 y D7 pasan a **#89** `grabacion-sesion-completa-ios`. Acá no se especifica nada de iPhone
(F1 §5.17 queda sin tocar). D1 = A, D2 = A (con campo nuevo en el contrato), D3 = A, D4 = B, D5 = A, D9 = A, D10 = A.
La cámara en Windows queda detrás de `Agent:Session:CameraEnabled`, apagada por defecto fuera de macOS.

El código se cita por **símbolo**; los números de línea son pistas.

---

## 0. Decisiones que necesitan al usuario antes de implementar

Una sola. El resto del documento está escrito con la opción recomendada y marca con **[DU1]** lo que cambia si se elige
la otra.

**DU1. "Marcar momento" con el cable del celular desconectado y la cámara grabando.** Por D2 = A la cámara sigue
grabando mientras la pantalla espera la reconexión (`waiting_device`). Hoy (F1 §5.9) una marca exige que la pantalla
esté grabando, porque toda marca dice "tramo i, minuto mm:ss **de la pantalla**".
- **A) Se puede marcar.** Mientras la pantalla espera el cable y la cámara graba, "Marcar momento" sigue disponible y la
  marca se ubica solo en el video de la cámara. En el informe: "• 14:32:10 — pantalla: no grababa en ese momento;
  cámara: tramo 2, minuto 04:12 — Desconecto el cable para cambiarlo". El contrato cambia una cosa más:
  `segment_index` de la marca pasa a ser opcional (solo si hay ubicación en la cámara).
- B) Como en F1: con el cable desconectado no se puede marcar (el botón queda deshabilitado con el motivo "La
  pantalla no está grabando"). La captura "Pantalla" tampoco funciona sin cable, así que no hay marca automática.
- **Recomendada: A.** Es el argumento de D2: con el cable desconectado la cámara es lo único que documenta qué se hace
  con el equipo, y la marca es la forma de señalarlo en el informe. El costo es acotado (una validación más en el
  backend, una variante de texto en el informe y en la web).

---

## 1. Resumen funcional

En el paso 3, con la grabación de la sesión activa y un Tatana que informa `recording_session_camera_v1`, la tarjeta
"Grabación de la recolección" ofrece **"Sumar cámara del procedimiento"**. El perito elige una cámara de la PC (sin las
entradas que graban el escritorio), la encuadra con una vista previa chica y toca **"Empezar a grabar la cámara"**
mientras la pantalla ya graba (D1 = A). Tatana graba la cámara con ffmpeg en tramos propios de N minutos contados desde
que arrancó, sin hueco entre tramos (segment muxer). La cámara **no** se corta con los cortes de la pantalla y **sigue
grabando** con el cable del celular desconectado (D2 = A). Cada tramo de cámara se valida, se mueve al caso, se hashea,
se registra, se sella y va al ZIP en `sesion/`, como los de pantalla. Si la cámara falla (desenchufada, sin permiso,
ocupada), la pantalla sigue, queda un hueco de cámara y el aviso con "Reintentar cámara". La cámara se puede sumar o
sacar en cualquier momento (D3 = A). Mientras la cámara del procedimiento está en uso, las fotos del perito y del
titular y "Filmar con cámara externa" quedan deshabilitadas con el motivo (D4 = B). La cámara elegida se recuerda en esa
PC (D5 = A) y, al volver al paso 3, arranca sola si el caso ya la usaba. Cada marca de momento dice también en qué tramo
y segundo del video de cámara cae. El informe describe la cámara con su resolución y fps reales, da el total por pista y
ubica las marcas en los dos videos. Además, el panel "Registro audiovisual" de la web pasa a usar el mismo criterio de
duración que el informe y la trazabilidad (B-9, D10 = A).

## 2. Toca

| Parte | ¿Toca? | Qué |
|---|---|---|
| **backend (Tatana)** `server/src/Factum.Agent` | **sí** | Nuevos: `Services/Session/CameraDevices.cs`, `CameraArgs.cs` (puro), `CameraTrack.cs`, `CameraPreviewService.cs`, `Controllers/CamerasController.cs`. Cambios: `SessionRecorder.cs` (pista de cámara con ciclo propio), `SessionModels.cs`, `SessionRecovery.cs`, `SegmentCloser.cs` (sin cambios de lógica; ver §5.7), `Controllers/CaseSessionController.cs` (rutas de cámara), `HealthController.cs` (capability), `MicCapture.cs` (`DshowDevices.ParseVideoDevices`), `Models/AgentModels.cs` (`SessionOptions`), `Common/AgentErrorCodes.cs`, `Program.cs` (DI), `appsettings.json`. Tests en `server/tests/Factum.Agent.Tests/Session/` y `DshowDevicesTests.cs`. Docs: `AGENTE_TATANA.md`. |
| **backend (API)** `server/src/Factum.Backend` | **sí** | `Models/RecordingSession.cs` (`SessionSegment.Camera`, `SessionMark.CameraSegmentIndex`/`CameraOffsetSeconds`, `SegmentIndex` anulable [DU1=A]), `Models/CaseEvent.cs` (`PreviousEnabled`, `PreviousWithCamera`), `DTOs/RecordingSessionDtos.cs`, `Services/Cases/RecordingSessionRules.cs` (validación y merge), `Services/Cases/RecordingSessionFlow.cs` (detalle del evento de settings), `Services/Reports/ReportSessionTexts.cs` (textos de cámara, totales por pista, marcas). **Sin** cambios de plantilla (`ops/plantilla/` no se toca), de endpoints ni de config. Tests en `server/tests/Factum.Backend.Tests/RecordingSession/`. |
| **client** `client/` | **sí** | `lib/agent.ts`, `lib/api.ts`, `lib/recording-session.ts` (B-9 y cámara), `lib/session-camera-pref.ts` (nuevo, D5), `lib/agent-messages.ts`, `lib/case-events.ts`, `hooks/useRecordingSession.ts`, `components/capture/SessionRecordingCard.tsx`, `components/capture/SessionCameraPicker.tsx` (nuevo), `components/capture/SessionIndicator.tsx`, `components/capture/MarkMomentPopover.tsx`, `components/CaptureStep.tsx` (D4 y aviso de cámara), `components/IdentityCard.tsx` (motivo de deshabilitado), `components/capture/EvidenceTray.tsx` (etiqueta por pista), `components/case-detail/SessionRecordPanel.tsx` (B-9). |
| **agent-ui** `agent-ui/` | **sí, solo `package.json`** | `build.mac.extendInfo` con `NSCameraUsageDescription` y `NSMicrophoneUsageDescription` (F1 §10). Sin cambios de código ni visuales. |
| **plantillas / deploy** | no | Los textos nuevos van por los placeholders que ya existen. No hay variables `FACTUM_*` nuevas. |

**Reparto.**
- `implementer-backend`: §15.1 (Tatana) y §15.2 (API).
- `implementer-frontend`: §15.3 (`client/`) y §15.4 (`agent-ui/`).

**Orden.** Los dos lados pueden avanzar en paralelo con el contrato de §7. La web tolera un Tatana sin
`recording_session_camera_v1` (no ofrece la cámara) y un backend sin los campos nuevos (System.Text.Json los ignora;
ver §12.3). La QA manual (§16.2) necesita los tres.

---

## 3. Arqueología verificada (sobre `04bb57f`)

Confirmado en el código, además de lo que ya lista la HU en "Qué dejó la fase 1".

- **`SessionRecorder.Active`** tiene una sola pista (`ISessionTrack? Track`, `JournalSegment? Current`). Todo el ciclo
  de vida pasa por `_gate` (start, stop, corte, reconexión, apagado) y el diario por `_sync`. Los caminos que terminan
  la sesión son cuatro: `StopLockedAsync`, `FinishInactiveLockedAsync` (desde `waiting_device` o tras un reintento
  fallido), la rama `WaitingDevice` de `ShutdownAsync` (hoy **no corta nada**: solo guarda y suelta) y el corte por
  disco en `DiskTickAsync`. La cámara tiene que cerrarse en los cuatro.
- **`DiskTickAsync`** sale temprano si el estado no es `recording`/`rotating`. Con D2 = A la cámara graba también en
  `waiting_device`, así que el chequeo de disco tiene que correr mientras grabe cualquiera de las dos pistas.
- **`StateFromDoc`** fija `camera_state = "off"` y los campos de cámara en `null`. `BuildState` no los toca.
- **`MarkRules.Locate`** solo mira la pista `pantalla` y cae al "último tramo que empezó antes" si ninguno contiene la
  hora. `AddMarkCore` exige `recording`/`rotating`.
- **`NewGap`** siempre crea huecos de `pantalla`; `GapRules.CloseOpenGap` ya es por pista.
- **`SessionRecovery`**: `ExpectedNames("ffmpeg")` ya existe, pero la señal para todo lo que no es scrcpy es `"-2"`
  (SIGINT). QA #80 B-1 mostró que SIGINT puede quedar ignorada si Tatana la heredó así: para ffmpeg hay que usar
  `ProcessStop.SessionSignal` (`"-15"`). El hueco `tatana_cerrado` solo se abre en `pantalla`.
- **`StartSessionRequest.Camera`** es un `JsonElement?` que hoy se ignora. `SessionCameraDto(Name, Width, Height, Fps,
  WithAudio)` existe y viaja en el diario (`CaseSessionDto.camera`).
- **`SessionOptions`** ya tiene `CameraBytesPerMinute` (12 MiB), `CameraWidth/Height/Fps/Crf` con su `Normalize`.
- **`MicCapture`** usa `:0` en macOS. En esta Mac (listado real, §4.1) el índice 0 de audio es **el micrófono del
  iPhone por Continuidad**, no el de la Mac. La cámara de esta HU no usa `:0` (§5.3). El bug de `MicCapture` afecta a
  las grabaciones puntuales "con micrófono" de hoy y **queda fuera de alcance**: se reporta al orquestador (§18).
- **`DshowDevices.ParseAudioDevices`** asocia el *alternative name* solo a dispositivos de audio. Los fixtures de
  `DshowDevicesTests` ya traen entradas `"Integrated Webcam" (video)` con su alternative name, en los dos formatos.
- **`WebcamService`** sigue como en F1 (D-T24): no se toca.
- **Backend:** `RecordingSessionRules.Validate` valida la cámara de cabecera (`camera.name` ≤ 200, W/H 1–10000, fps
  1–240) y las marcas con `segment_index` obligatorio 1–9999. El merge de marcas compara campo por campo y responde
  conflicto ante cualquier diferencia. `ReportSessionTexts.Description` ya incluye la frase de cámara, **pero dice "con
  los mismos cortes"**, que con D2 = A es falso; y `Detail` usa "el mismo minuto sirve aproximadamente para el de la
  cámara". `CoveredSeconds` (unión de intervalos) ya existe.
- **Web:** `useRecordingSession` solo arranca con `platform === "android"` y siempre manda `with_camera: false`.
  `SessionIndicator` ya muestra un ícono `Camera` con `camera_state === "recording"`. `sessionSummary` suma
  `duration_seconds ?? recorded_seconds` de todas las pistas (B-9). `toBackendSnapshot` pasa `camera` del diario tal
  cual y arma las marcas campo por campo. `lib/case-events.ts` ya usa `recorded_seconds` en "Se guardó el tramo…".
- **D4 (lo que la HU pidió confirmar):** el navegador **no** puede identificar de forma fiable cuál de sus cámaras usa
  Tatana. `MediaDeviceInfo.deviceId` es un id aleatorio por origen, y el `label` no coincide con el nombre de dshow en
  Windows (suele traer un sufijo `(vid:pid)`) ni hay garantía en macOS. Se confirma D4 = B.
- **Mongo de desarrollo:** el contenedor de Factum no estaba corriendo (el `mongo:7` activo es de otro proyecto y no
  se tocó). No hace falta inspección: todos los campos nuevos son anulables y aditivos (§12.1).

---

## 4. Mediciones (lavfi, sin abrir la cámara)

Entorno: la misma MacBook Air de F1 §4, ffmpeg 8.1.2 de Homebrew. Fuente `-re -f lavfi -i testsrc2=size=1280x720:rate=30`
en lugar de la cámara, con el comando de §5.3. Todo en el scratchpad; cada proceso lanzado se cerró (el script guardaba
el PID propio, nunca `pgrep`).

### 4.1 Listado de avfoundation (no abre la cámara)

```
[AVFoundation indev @ 0x…] AVFoundation video devices:
[AVFoundation indev @ 0x…] [0] Cámara de la MacBook Air
[AVFoundation indev @ 0x…] [1] Cámara de iPhone de <nombre>
[AVFoundation indev @ 0x…] [2] Cámara de Vista del Escritorio de iPhone de <nombre>
[AVFoundation indev @ 0x…] [3] Cámara de Vista del Escritorio de la MacBook Air
[AVFoundation indev @ 0x…] [4] Capture screen 0
[AVFoundation indev @ 0x…] [5] Capture screen 1
[AVFoundation indev @ 0x…] AVFoundation audio devices:
[AVFoundation indev @ 0x…] [0] Micrófono de iPhone de <nombre>
[AVFoundation indev @ 0x…] [1] Micrófono de MacBook Air
[in#0 @ 0x…] Error opening input: Input/output error
```

Va como fixture (`Fixtures/Cameras/avfoundation-macbook-air.txt`) **con el nombre de la persona reemplazado por
`<nombre>`**. El audio "por defecto del sistema" se pide con la palabra `default` (presente en `libavdevice`), no con un
índice.

### 4.2 Segment muxer en tiempo real

| Prueba | Resultado |
|---|---|
| Arranque | `Output #0, segment, …` a los **0,05 s** del lanzamiento; con `-tune zerolatency`, el `Opening '…camara_NNNN.part.mkv'` de cada tramo nuevo aparece a ≤ 0,1 s del corte real. Sin `zerolatency` aparecía ~1,4 s tarde (búfer de x264). |
| CSV `+live` | Una línea **en el momento** en que cada tramo queda cerrado (≤ 0,05 s después del `Opening` del siguiente): `camara_0003.part.mkv,0.000000,10.000000`, después `camara_0004.part.mkv,10.000000,…`. `segment_start_number 3` numera desde 3. Tramos contiguos: el inicio de uno es el fin del anterior. |
| Cierre con `q` por stdin | `[q] command received. Exiting.`; escribe la **última línea** del CSV; sale con **0** en ~0,7 s. |
| Cierre con SIGTERM | `Exiting normally, received signal 15.`; escribe la última línea; sale con **255**. Es un cierre limpio. |
| EOF en stdin (Tatana muere) | ffmpeg **sigue vivo y grabando**: 4 s después seguía, y en ese lapso cerró un tramo más y lo anotó en el CSV. Un SIGTERM posterior lo cerró limpio. |
| `kill -9` | Sin `zerolatency`, a 1,5 s de abrir el tramo: 589 B, irrecuperable; a ~5,5 s del inicio del tramo, el remux recuperó 2,0 s. Con `zerolatency`, a ~5,5 s: el remux recuperó **4,0 s** (pérdida ≤ 2 s, el `cluster_time_limit`). |
| Peso (testsrc2, 1280×720 a 15 fps, `veryfast`, crf 26) | **11,4 MB/min** sin `zerolatency`, **17,0 MB/min** con `zerolatency`. testsrc2 tiene mucho movimiento; una cámara real sobre una mesa debería pesar menos (a confirmar en la QA, §16.2 paso 6). |
| Vista previa | `-update 1 -atomic_writing 1 -f image2` actualiza `camara-preview.jpg` cada 2 s (5 KB, 320×180) con escritura atómica (temporal + rename). |
| Líneas de stream | `Output #0 … Stream #0:0: Video: h264, …, 1280x720 …, 15 fps` da la resolución y los fps **reales** de lo que se graba, también después de un reintento con otro modo. |

**Conclusiones** (D-T1, D-T2): `-tune zerolatency` (más peso, menos pérdida y horas exactas), ancla horaria en la línea
`Output #0` y cierre normal con `q` (SIGTERM en la recuperación, salida 255 = limpio). Un dato de diseño clave: **si
Tatana muere, ffmpeg sigue y puede cerrar tramos solo**; la recuperación tiene que conciliar el CSV entero (§5.8).

### 4.3 Lo que no se pudo medir (queda para la QA en la Mac, §16.2)

- La cámara real: modos que acepta, tiempo hasta el primer cuadro, peso real, luz encendida y apagada.
- Permisos de macOS: qué hace ffmpeg 8.1 sin permiso (no tiene pedido de autorización propio: la única pista es
  `Failed to create AV capture input device: %s`, o que el sistema lo mate si la app no tiene
  `NSCameraUsageDescription`).
- Windows (dshow): no hay PC con Windows. Por eso `CameraEnabled` queda apagado fuera de macOS.

---

## 5. Diseño en Tatana

### 5.1 Componentes nuevos (`Services/Session/`)

| Tipo | Responsabilidad |
|---|---|
| `CameraDevices` (singleton) | `ListAsync(refresh)`: cámaras de la PC (§5.2), caché 10 s. `Resolve(id, name)` → dispositivo actual o null. |
| `CameraArgs` (puro, `static`) | Arma la `ArgumentList` de ffmpeg (§5.3) para Mac, Windows y lavfi (mock y tests); parsea los "Supported modes" de avfoundation, la línea `Output #0` (W×H, fps) y clasifica las fallas (§5.5). |
| `CameraTrack` (`ICameraTrack`) | Un proceso ffmpeg por **tramo continuo** de cámara (desde que arranca hasta que se corta). Lanza, confirma el arranque, sigue el CSV, avisa cada tramo cerrado, vigila que el archivo crezca y cierra con `q`. |
| `ICameraTrackFactory` | `Create(CameraSpec spec)`; en mock o con `Agent:Session:CameraLavfi=true` (solo tests y desarrollo) usa lavfi. Los tests de la máquina de estados usan un fake. |
| `CameraPreviewService` (singleton) | Vista previa **antes** de grabar: un ffmpeg solo con la salida JPG, uno a la vez, se corta solo a los `CameraPreviewIdleSeconds` (120) sin pedidos de la imagen. |

`ICameraTrack` es una interfaz aparte de `ISessionTrack`: la cámara no rota por pedido (rota ffmpeg solo) y avisa de
otra forma.

```csharp
public sealed record CameraSpec(string Id, string Name, bool WithAudio, string Backend);
public sealed record CameraStart(DateTime AnchorAt, int Pid, DateTime? ProcessStartedAt, int Width, int Height, int Fps,
    bool Fallback);
public sealed record CameraSegmentClosed(int Index, string PartPath, double StartSeconds, double EndSeconds);
public sealed record CameraExit(DateTime At, string? Detail, string? Summary);

public interface ICameraTrack : IDisposable
{
    /// Lanza ffmpeg (con el reintento de modo de §5.5), espera la línea "Output #0" y 1,5 s vivo. Lanza
    /// CameraStartException(detail, mensaje) si no arranca.
    Task<CameraStart> StartAsync(string stagingDir, int startNumber, int segmentSeconds);
    /// Cada línea nueva del CSV (sondeo de 1 s). Al salir el proceso se lee el CSV una última vez ANTES de Exited.
    event Action<CameraSegmentClosed>? SegmentClosed;
    /// Salió sin que se lo pidieran (o la vigilancia lo cortó por archivo quieto, Detail "stalled").
    event Action<CameraExit>? Exited;
    /// "q" por stdin; a los StopTimeoutSeconds, ProcessStop (SIGTERM / Ctrl+C) y kill. Devuelve si fue limpio.
    Task<TrackStop> StopAsync();
}
```

### 5.2 Lista de cámaras (`GET /cameras`)

- **macOS:** `ffmpeg -hide_banner -f avfoundation -list_devices true -i ""` (timeout 5 s; sale con código ≠ 0, es
  normal). Se toman las líneas entre `AVFoundation video devices:` y `AVFoundation audio devices:` con el patrón
  `\[(\d+)\] (.+)$` después del prefijo `[… @ 0x…] `. Se excluyen las que empiezan con `Capture screen` (graban el
  escritorio). `id` = `name` = el nombre localizado. Las "Vista del Escritorio" **sí** quedan (son cámaras).
- **Windows:** `ffmpeg -hide_banner -list_devices true -f dshow -i dummy` (el mismo listado de `MicCapture`) →
  `DshowDevices.ParseVideoDevices` (§5.2.1). `id` = *alternative name* (`@device_pnp_…`, estable) o, si falta, el
  nombre.
- **Linux:** `/dev/video*` ordenados; `id` = ruta, `name` = `cat /sys/class/video4linux/videoN/name` o la ruta.
  `backend = "v4l2"`. Sin QA (queda apagado por defecto, §5.10).
- **Mock:** `[{ "id": "mock-cam", "name": "Cámara simulada", "backend": "mock" }]`.
- Caché de 10 s; `?refresh=true` la saltea ("Actualizar lista"). Sin ffmpeg → 200 `{ "cameras": [], "error": "No se
  encontró ffmpeg…" }` (mismo texto que `MicCapture.ErrorNoFfmpeg`, adaptado a la cámara).
- `Resolve(id, name)`: busca por `id`; si no está, por `name` exacto; si hay más de uno con ese nombre, el primero.
  Siempre con un listado fresco (sin caché) antes de lanzar ffmpeg.

**5.2.1 `DshowDevices`.** Se generaliza el parser actual a `ParseDevices(stderr, kind)` (`kind` = `"audio"` |
`"video"`), con la misma lógica de tipo por paréntesis o por sección y el *alternative name* asociado al último
dispositivo **del tipo pedido**. `ParseAudioDevices(stderr)` queda como `ParseDevices(stderr, "audio")` (sin cambio de
comportamiento: sus tests actuales siguen verdes). `ParseVideoDevices(stderr)` = `ParseDevices(stderr, "video")`. Un
dispositivo `(audio, video)` aparece en las dos listas.

### 5.3 Proceso de grabación (`CameraArgs.Build`)

Un proceso por tramo continuo de cámara, con `ArgumentList` (nunca un string) y `ToolResolver.Find(AgentTools.Ffmpeg)`.
`RedirectStandardInput/Output/Error = true`, `CreateNoWindow = true` (consola propia oculta: el Ctrl+C del helper
funciona en Windows, como scrcpy). Se drenan stdout y stderr; stderr va a una `LineTail` de 40 líneas y al log en
`Debug` (las líneas `warning`/`error` en `Warning`).

```
ffmpeg -hide_banner -nostats -loglevel info -y
  [mac]   -f avfoundation -framerate {inFps} [-video_size {inW}x{inH}] -i "{video}[:default]"
  [win]   -f dshow -rtbufsize 256M [-video_size {W}x{H} -framerate {F}] -i "video={id}[:audio={mic}]"
  [lavfi] -re -f lavfi -i "testsrc2=size=1280x720:rate=30" [-re -f lavfi -i "sine=frequency=440:sample_rate=48000"]
  -filter_complex "[0:v]fps={F},scale=w='min(iw,{W})':h=-2,split=2[r][p];[p]fps=1/2,scale=320:-2[pv]"
  -map "[r]" [-map {a}] -c:v libx264 -preset veryfast -tune zerolatency -crf {CRF} -pix_fmt yuv420p -g {2F}
  -force_key_frames "expr:gte(t,n_forced*{SEG})" [-c:a aac -b:a 96k]
  -f segment -segment_time {SEG} -segment_format matroska
  -segment_format_options flush_packets=1:cluster_time_limit=2000
  -segment_list {staging}/camara.csv -segment_list_type csv -segment_list_flags +live
  -reset_timestamps 1 -segment_start_number {N} {staging}/camara_%04d.part.mkv
  -map "[pv]" -q:v 6 -update 1 -atomic_writing 1 -f image2 {staging}/camara-preview.jpg
```

- `{staging}` = `<DataDirectory>/sessions/<caseId>/` (F1 §5.2). `{W}/{H}/{F}/{CRF}` = `CameraWidth/Height/Fps/Crf`.
  `{SEG}` = `segment_minutes × 60` (o `MockSegmentSeconds` en mock). `{N}` = próximo índice de la pista `camara`
  (`SessionRecorder.NextIndex(doc, "camara")`), así `camara_%04d` coincide con el `NNN` del nombre final.
- **macOS:** `{video}` = el nombre de la cámara. Si el nombre contiene `:` (rompe la sintaxis `video:audio`), se usa el
  índice del listado fresco. `{inFps}` = 30 y `{inW}x{inH}` = `{W}x{H}` en el primer intento (avfoundation exige un
  modo que la cámara tenga; por defecto pide 29,97 y muchas cámaras lo rechazan). Audio = `:default` (el micrófono por
  defecto del sistema), **nunca** `:0` (§3).
- **Windows:** `{mic}` = `MicCapture.ResolveWindowsDeviceAsync` (respeta `Agent:MicDevice`). `{a}` = `0:a`.
- **lavfi:** con audio, `{a}` = `1:a`. Es el modo de los tests (§14) y del mock.
- `scale=w='min(iw,{W})'` limita el tamaño si el reintento cae en un modo más grande (por ejemplo 1920×1080).
- Mientras corre, `{staging}/camara.csv` y `camara-preview.jpg` son de Tatana. Antes de lanzar, si existe un
  `camara.csv` de un tramo continuo anterior **ya conciliado** (§5.4), se borra: es un archivo propio.

### 5.4 Ciclo de un tramo continuo de cámara

**Arranque** (`CameraTrack.StartAsync`):
1. Lanza ffmpeg. Espera la línea `Output #0, segment` (`CameraStartTimeoutSeconds`, 10 s): su hora UTC es el **ancla**
   (`anchor_at`) de todo el tramo continuo. Toma W×H y fps de la línea `Stream #0:0: Video: h264` que sigue.
2. Espera 1,5 s con el proceso vivo (como `MicCapture.EarlyExitWindow`). Si sale antes, clasifica la falla (§5.5) y,
   si corresponde, reintenta **una** vez con otro modo.
3. Devuelve `CameraStart`.

**Diario** (en `SessionRecorder`, bajo `_sync`), con el arranque confirmado:
- `doc.CameraRun` (interno, §7.2.6) = `{ id, name, with_audio, width, height, fps, fallback, anchor_at, start_number,
  pid, process_started_at, csv_lines_done: 0 }`.
- Tramo `camara` `recording` con `index = N`, `started_at = anchor_at`, `filename =
  SessionNames.SegmentName("camara", N, local(anchor_at))`, `part_file = "camara_NNNN.part.mkv"`, `process = { pid,
  started_at, exe: "ffmpeg" }` y `camera = { name, width, height, fps, with_audio }`.
- `doc.Camera` (cabecera, la última cámara usada) = el mismo `camera`.
- Cierra el hueco abierto de la pista `camara`, si lo hay, con `to = anchor_at` (`GapRules.CloseOpenGap`). Si no había
  hueco (primera vez en el caso, o la cámara se suma con la sesión en curso), no se crea ninguno (D3 = A).

**Cada línea nueva del CSV** (`SegmentClosed(index, part, start, end)`), en orden:
1. Tramo `index`: `stopped_at = anchor_at + end`, `stop_reason = "rotacion"` si el proceso sigue grabando (no se pidió
   el cierre), `state = closing`, `process = null`. `csv_lines_done += 1`.
2. Si el proceso sigue grabando y no se pidió el cierre: tramo nuevo `index + 1` `recording` con `started_at =
   anchor_at + end`, nombre con la hora local de ese inicio, `part_file` y `process` (el mismo PID).
3. Rename `part_file` → `{staging}/<filename>` y a la **cola de cierre** (`EnqueueClose`, F1 §5.6). Desde ahí es
   idéntico a un tramo de pantalla (validación, rename a la carpeta del caso, SHA-256, `session_segment_closed`).
4. `session_state`.

Horas: todas salen del ancla + los tiempos del CSV, que son los del propio video. Así inicio y fin de cada tramo
coinciden con lo que dura el archivo y los tramos quedan contiguos (el inicio de uno es el fin del anterior). La
incertidumbre es la del ancla (≤ ~0,1 s medido con lavfi; se confirma en la QA, §16.2 paso 7).

**Fin pedido** (stop de la sesión, quitar la cámara, disco, apagado): `StopAsync` (`q`), y la última línea del CSV
cierra el último tramo con el `stop_reason` del pedido. Si el proceso tuvo que matarse (sin última línea), el tramo
abierto se cierra con `forced = true` y `stopped_at` = hora del pedido; `SegmentCloser` remuxea.

**Fin no pedido** (`Exited`): se cierra el tramo abierto con `stopped_at = At` y `forced = true` si no hubo última
línea, hueco de pista `camara` con `reason = "camara_fallo"` y `from = stopped_at`, aviso `camera_failed` con `detail`,
`camera_state = "failed"`, `doc.CameraRun = null`. **La pantalla no se toca.** No hay reintento automático: el perito
usa "Reintentar cámara" (D-T7).

**Vigilancia:** cada 5 s se mira el tamaño del `part_file` abierto. Con `flush_packets=1:cluster_time_limit=2000` crece
siempre que entren cuadros (aun con la imagen quieta: H.264 emite un cuadro por cada uno). Si no crece durante
`CameraStallSeconds` (20 s), se corta ffmpeg (`StopAsync`) y se trata como fin no pedido con `detail: "stalled"`. Cubre
una cámara que se desenchufa sin que ffmpeg salga.

### 5.5 Fallas al arrancar y reintento de modo (`CameraArgs.Classify`)

| Señal (stderr / salida) | Qué hace | `detail` del aviso |
|---|---|---|
| mac: `is not supported by the device` + `Supported modes:` | Reintenta **una** vez con el modo elegido de las líneas `  {W}x{H}@[{min} {max}]fps`: el de mayor área ≤ {W}×{H} (si no hay, el más chico); fps de entrada = 30 si cae en el rango, si no el máximo del rango. | — (si el reintento falla: `unsupported`) |
| win: `Could not set video options` o `not supported` | Reintenta **una** vez sin `-video_size`/`-framerate` (el modo por defecto de la cámara). | — (si falla: `unsupported`) |
| mac: `Video device not found` / `No AV capture device found`; win: `Could not find video device`; o `Resolve` devolvió null | Sin reintento. | `not_found` |
| mac: `Failed to create AV capture input device`, o el proceso termina por señal (código de salida negativo, 134 o 137) antes de `Output #0` | Sin reintento. | `permission` |
| win: `Could not run graph`, `I/O error`, `Error opening input` con el dispositivo presente | Sin reintento. Windows no distingue bien "privacidad" de "otra app la usa". | `blocked` |
| No llega `Output #0` en 10 s | Corta el proceso. | `permission` en mac (es lo que pasa con el pedido de permiso pendiente), si no `null` |
| Cualquier otra | — | sin `detail` |

El W×H y los fps que van al diario y al informe son **los reales** de la línea `Output #0` (escenario "La cámara no
acepta la resolución pedida").

### 5.6 La cámara dentro de `SessionRecorder` (D2 = A, D3 = A, D9 = A)

`Active` suma:

```csharp
public ICameraTrack? Camera;                 // null = no graba
public JournalSegment? CameraCurrent;        // tramo de cámara abierto
public string CameraState = "off";           // off | recording | failed
public CameraSpec? CameraSpec;               // última cámara pedida en esta sesión (para retry)
public CameraStart? CameraStarted;           // W×H, fps y ancla del tramo continuo en curso
```

Todas las operaciones de la cámara toman `_gate`, como las de la pantalla. Los eventos de `ICameraTrack` hacen
`Task.Run(...)` y toman `_gate` (como `OnTrackExitedAsync`).

| Situación | Pantalla | Cámara |
|---|---|---|
| `start` con `camera` en el body (D1 = A, retomar) | arranca como F1 | **después** del arranque confirmado de la pantalla, `StartCameraLockedAsync(spec)`. Si falla: 200 igual, `camera_state: "failed"`, aviso con `detail`, hueco abierto si lo había. |
| `POST …/session/camera/start` | no cambia | arranca con el `spec` del body. Requiere sesión activa del caso (`recording`, `rotating` o `waiting_device`); si no → 409 `session_not_active`. Con la cámara ya grabando → 200 idempotente (mismo `id`) o 409 `camera_busy` (otra cámara). |
| `POST …/session/camera/retry` | no cambia | arranca con `Active.CameraSpec`. Sin spec → 409 `session_not_active`. |
| `POST …/session/camera/stop` (D3 = A) | no cambia | `StopAsync`, último tramo `stop_reason = "detenido_por_perito"`, hueco `camara` `detenido_por_perito`, `camera_state = "off"`, `CameraSpec = null`. Si estaba `failed`: pasa a `off` y el hueco `camara_fallo` sigue abierto tal cual. |
| Corte de pantalla (rotación) | F1 | **no se toca** |
| Desconexión del USB / `fallo_grabacion` con reintento | F1 (`waiting_device`) | **sigue grabando** |
| Falla de la cámara | **sigue** | §5.4 "fin no pedido" |
| `stop` (cambio de paso, detenido por perito) | F1 | se corta con el mismo motivo; hueco `camara` con ese motivo |
| `stop` en `waiting_device` (`FinishInactiveLockedAsync`) | F1 | se corta con el motivo pedido (si vino de un `stop`) o el de la sesión (`fallo_grabacion`); hueco `camara` |
| Reintento de la pantalla fallido → `stopped` | F1 | se corta con `fallo_grabacion` (D9 = A: no hay cámara sin sesión) |
| Disco: corte `sin_espacio` | F1 | se corta con `sin_espacio` |
| `ShutdownAsync` (incluida la rama `waiting_device`, que hoy no corta nada) | F1 | se corta con `tatana_cerrado`, dentro del mismo presupuesto; hueco `camara` `tatana_cerrado` |

Reglas comunes:
- **Huecos de cámara:** `NewGap(from, reason, track)` suma el parámetro de pista. Al cortar la cámara se abre un hueco
  `camara` solo si no hay uno abierto en esa pista. El chequeo de `StopLockedAsync` ("si no hay hueco abierto de
  pantalla") pasa a ser por pista.
- **Orden al cortar todo:** primero la cámara (`q`, rápido) y después la pantalla, así el presupuesto del apagado
  alcanza a las dos; los dos cierres van a la misma cola.
- **Abrir la cámara de la sesión corta antes la vista previa** de `CameraPreviewService` (en Windows dshow no comparte
  la cámara).
- **Disco (F1 §5.8):** `R = ScreenRate + (CameraState == "recording" ? CameraRate : 0)`, con `CameraRate =
  max(CameraBytesPerMinute, tasa observada del último tramo de cámara guardado)`. `DiskTickAsync` corre si la pantalla
  **o** la cámara graban. `Capacity(camera: true)` usa la misma `CameraRate`. `StartCameraLockedAsync` chequea
  `CanStart` con la tasa sumada. Si no alcanza: en `camera/start` y `retry` → 507 `insufficient_storage`; en el `start`
  de la sesión, la pantalla arranca igual sin la cámara (si alcanza para ella sola) y queda el aviso `disk_low` (D-T9).
- **`ActiveOperations`** no cambia (la cámara vive dentro de la sesión).
- **`BuildState`**: `camera_state`, `camera_segment_index = CameraCurrent?.Index`, `camera_segment_started_at =
  CameraCurrent?.StartedAt` y `camera` (§7.2.7) mientras `camera_state ≠ "off"`. `StateFromDoc` (sin sesión activa)
  sigue con `"off"`.
- **Avisos:** `camera_failed` se agrega al fallar y se saca al arrancar la cámara o al quitarla.

### 5.7 Marcas (D2 = A, [DU1=A])

- `MarkRules.Locate` gana un parámetro de pista y una variante estricta:
  - pantalla: como hoy;
  - **cámara:** el tramo `camara` no descartado cuyo `[started_at, stopped_at ?? ahora)` contiene `at`; **sin**
    respaldo al "último que empezó antes". Si no hay, la marca no tiene ubicación en la cámara.
- `SessionMarkDto` suma `camera_segment_index` y `camera_offset_seconds` (redondeo a 0,1, como `offset_seconds`).
- **[DU1=A]** `AddMarkCore` acepta la marca también con el estado `waiting_device` **si la cámara graba**. En ese caso
  la marca va **sin** `segment_index`/`offset_seconds` (no hay video de pantalla en ese momento) y con la ubicación en la
  cámara. Con la pantalla grabando, `segment_index` sigue saliendo como hoy (con su respaldo).
  **[DU1=B]:** `AddMarkCore` no cambia de estados permitidos; `segment_index` sigue obligatorio.
- La marca automática de captura (`TryAddCaptureMarkAsync`) suma la ubicación en la cámara de la misma forma.

### 5.8 Recuperación al arrancar (F1 §5.10, suma la cámara)

Por cada diario, **después** de lo de la pantalla y antes de abrir huecos:
1. **Proceso huérfano:** si hay un tramo `camara` abierto con `process.exe = "ffmpeg"`, `FindOwnProcess` (nombre +
   hora de inicio ±2 s) y `ProcessStop.StopAsync(..., unixSignal: ProcessStop.SessionSignal)` (SIGTERM; Ctrl+C en
   Windows). Salida 255 por SIGTERM = limpio (§4.2).
2. **Conciliar el CSV** de `{staging}/camara.csv` con `doc.CameraRun`: por cada línea a partir de `csv_lines_done`,
   se aplica §5.4 "cada línea nueva", **creando** el tramo en el diario si no existe (ffmpeg pudo cerrar tramos solo
   mientras Tatana no estaba, §4.2). Con `recovered: true` y, para el último, `stop_reason = "tatana_cerrado"`.
3. **Tramo abierto sin línea en el CSV** (ffmpeg murió de golpe): `forced`, `stopped_at` desde la duración del probe o
   `last_alive_at`, remux en `SegmentCloser` (F1 §5.6). 0 B → `discarded` + `segment_lost`.
4. **`part_file` huérfanos** de la carpeta que no estén en el diario ni en el CSV: no se tocan y se loguean (regla
   dura de datos).
5. **Hueco** `camara` `tatana_cerrado` si `doc.CameraRun` no era null y no hay hueco abierto de cámara. `doc.CameraRun
   = null`. El `camara.csv` conciliado se borra.
6. La recuperación **no** relanza la cámara (como la pantalla, D7 de F1).

### 5.9 Vista previa (`CameraPreviewService`)

- `POST /cameras/preview { id, name }`: corta la previa anterior (si es de otra cámara) y lanza ffmpeg con la misma
  entrada de §5.3 y **solo** la salida `[pv]` (`fps=1/2,scale=320:-2`, `-update 1 -atomic_writing 1`) a
  `<DataDirectory>/camera-preview/preview.jpg`. Usa el mismo reintento de modo (§5.5). Si la cámara no abre → 200 con
  `{ "ok": false, "detail": "<detail>", "error": "<texto>" }` (la web lo muestra en la tarjeta; no es un aviso de la
  sesión). Con la cámara de la sesión grabando → 409 `camera_busy`.
- `DELETE /cameras/preview` → corta y borra `preview.jpg` (archivo propio). 204, idempotente.
- `GET /cameras/preview.jpg`: con la cámara de la sesión grabando, la de `{staging}/camara-preview.jpg`; si no, la de
  la previa. `image/jpeg`, `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`. 404 `camera_preview_unavailable`
  si no hay. Cada pedido renueva el tiempo de inactividad.
- Se corta sola a los `CameraPreviewIdleSeconds` (120) sin pedidos, al arrancar la cámara de la sesión, en
  `ShutdownAsync` y en `ApplicationStopping`.

### 5.10 Configuración (`Agent:Session:*`, suma a F1 §5.14)

| Clave | Default | Validación |
|---|---|---|
| `CameraEnabled` | `null` = **`true` solo en macOS**; `false` en Windows y Linux | bool |
| `CameraBytesPerMinute` | **`18874368` (18 MiB)**, antes 12 MiB (medido 17 MB/min con `zerolatency`, §4.2) | > 0 |
| `CameraStartTimeoutSeconds` | `10` | 3–60 |
| `CameraStallSeconds` | `20` | 0 = sin vigilancia; si no, 10–300 |
| `CameraPreviewIdleSeconds` | `120` | 10–600 |
| `CameraLavfi` (solo tests y desarrollo) | `false` | bool; con `true`, la cámara es lavfi aunque no sea mock |

Con `CameraEnabled = false`: `/health` no informa `recording_session_camera_v1`; `/cameras*` y `…/session/camera/*`
responden 400 `session_camera_unsupported` ("La cámara del procedimiento no está habilitada en este Tatana."); un
`start` con `camera` la ignora (y lo loguea). Para probarla en Windows: `Agent__Session__CameraEnabled=true` o
`appsettings.Local.json`.

### 5.11 Modo mock

Con `Agent:Mock=true`, `/cameras` devuelve la cámara simulada y `CameraTrack` corre **ffmpeg con lavfi** (§5.3), así
el segment muxer, el CSV, la vista previa y la recuperación funcionan de verdad sin cámara. Sin ffmpeg, la lista viene
vacía con `error`. `MockSegmentSeconds` aplica también a la cámara.

---

## 6. Backend (API)

### 6.1 Modelo de datos (colección `cases`, sub-documento `RecordingSession`, sin backfill)

```csharp
// SessionSegment: solo en tramos "camara"
[BsonIgnoreIfNull] public SessionCamera? Camera { get; set; }

// SessionMark
public int? SegmentIndex { get; set; }                               // [DU1=A] antes int; [DU1=B] sigue int
[BsonIgnoreIfNull] public double? OffsetSeconds { get; set; }        // [DU1=A] antes double; null sin SegmentIndex
[BsonIgnoreIfNull] public int? CameraSegmentIndex { get; set; }
[BsonIgnoreIfNull] public double? CameraOffsetSeconds { get; set; }

// CaseEventDetail (Models/CaseEvent.cs), [BsonIgnoreIfNull, JsonIgnore(WhenWritingNull)]
public bool? PreviousEnabled { get; set; }
public bool? PreviousWithCamera { get; set; }
```

- Los documentos existentes tienen `SegmentIndex`/`OffsetSeconds` enteros/dobles: deserializan igual en `int?`/`double?`.
  `[BsonIgnoreExtraElements]` ya está en todas las clases.
- `RecordingSession.Camera` (cabecera) queda como "última cámara usada"; la descripción del informe sale de los
  `Camera` de cada tramo (D-T4).
- Sin índices nuevos.

### 6.2 Reglas (`RecordingSessionRules`)

**Validación** (400 `invalid_recording_session` con `field`):
- `segments[i].camera`: solo con `track = "camara"` (con `pantalla` → `segments[i].camera`); mismas reglas que la
  cámara de cabecera (`name` 1–200 tras `SafeText.Clean`, W/H 1–10000, fps 1–240).
- `marks[i].camera_segment_index` y `marks[i].camera_offset_seconds`: los dos o ninguno; índice 1–9999, offset finito
  ≥ 0 (`marks[i].camera_segment_index` si falla cualquiera).
- **[DU1=A]** `marks[i].segment_index`: puede faltar **solo** si viene `camera_segment_index`; si falta, también falta
  `offset_seconds` (si viene uno sin el otro → `marks[i].segment_index`). **[DU1=B]** sin cambio.

**Merge:**
- Marcas: la comparación de inmutabilidad suma `SegmentIndex` (anulable), `CameraSegmentIndex` y
  `CameraOffsetSeconds`. Cualquier diferencia → 409 `recording_session_conflict` (como hoy).
- Tramos: `camera` se completa si faltaba (mismo criterio que `size`/`sha256`); distinto de uno ya guardado → 409.
- `Copy(SessionSegment)` y `Copy(SessionMark)` copian los campos nuevos.

### 6.3 Evento de settings (`RecordingSessionFlow`)

`recording_session_settings_changed` suma al `detail` `previous_enabled` y `previous_with_camera` (los valores
anteriores; los dos `null` si el sub-documento se crea en ese pedido). Además, si llega `enabled: false`, se guarda
`with_camera: false` (no hay cámara sin sesión, D9 = A). Endpoints y códigos: sin cambios.

### 6.4 Textos del informe (`ReportSessionTexts`) — reemplaza las partes de cámara de F1 §8.4

Sin cámara en el caso, **todo queda exactamente como en F1** (textos aprobados en la QA de #80). Con cámara:

1. Párrafo 1: sin cambios (ya dice "y con una cámara de la estación de trabajo, el procedimiento…").
2. Pantalla: sin cambios.
3. **Cámara** (reemplaza "…y con los mismos cortes."). Las configuraciones se agrupan por tramos consecutivos de
   cámara con la misma `{nombre, W, H, fps, con audio}`:
   - una sola: "Se registró en video el procedimiento sobre el equipo con la cámara «{nombre}» ({W}×{H}, {fps} cuadros
     por segundo, {sin audio | con el micrófono de la estación de trabajo}), en archivos separados de los de la
     pantalla."
   - varias: "Se registró en video el procedimiento sobre el equipo, en archivos separados de los de la pantalla, con
     la cámara «{n1}» ({…}) en {el tramo de cámara a | los tramos de cámara a a b} y con la cámara «{n2}» ({…}) en
     {…}." (separador ", " y " y " antes del último).
   - siempre a continuación: "La cámara se grabó en tramos propios de hasta {N} minutos, contados desde que empezó a
     grabar, y siguió grabando aunque se cortara la grabación de la pantalla. Para cruzar los dos videos deben usarse las
     horas de inicio y fin de cada tramo y la ubicación de las marcas de momento en cada video."
4. **Totales** (reemplaza la línea con cámara): "Se grabaron {d_p} de pantalla en {n} tramos y {d_c} de cámara en {m}
   tramos, entre el {primer inicio} y el {último fin}." `d_p` = `CoveredSeconds` de la pista pantalla, `d_c` =
   `CoveredSeconds` de la pista cámara (D10 = A). Sin cámara, la línea de F1 sin cambios.
5–6. Sellado y reloj: sin cambios.

**`Detail`, marcas** (reemplaza la variante con cámara):
- encabezado: "Marcas de momento (para ubicar hechos en los videos de la pantalla y de la cámara):"
- cada marca: "• {HH:mm:ss} — pantalla: {tramo i, minuto mm:ss | no grababa en ese momento}; cámara: {tramo j, minuto
  mm:ss | no grababa en ese momento} — {texto | "Captura " + filename | "Marca sin texto"}"
- Sin cámara en el caso: encabezado y líneas de F1 sin cambios.

`RowLines` (tabla) y los huecos: sin cambios (ya dicen la pista). `ReasonText`: sin cambios.

---

## 7. Contrato compartido

Serialización (F1 §3): Tatana HTTP en **snake_case_lower sin `null`** (`WhenWritingNull`); Tatana WebSocket en
snake_case_lower **con `null`**; backend en snake_case_lower **con `null`**. Los nombres de abajo son exactos.

### 7.1 Archivos de cada lado

| Qué | Tatana (C#) | API (C#) | Web (TS) |
|---|---|---|---|
| Cámaras, preview | `Controllers/CamerasController.cs`, `Services/Session/CameraDevices.cs` | — | `client/src/lib/agent.ts` (`CameraDevice`, `CameraListResponse`, `CameraPreviewResult`) |
| Rutas de cámara de la sesión | `Controllers/CaseSessionController.cs` | — | `client/src/lib/agent.ts` |
| Estado, tramos, marcas | `Services/Session/SessionModels.cs` | `DTOs/RecordingSessionDtos.cs`, `Models/RecordingSession.cs` | `client/src/lib/agent.ts` (Tatana), `client/src/lib/api.ts` (API), `client/src/lib/recording-session.ts` (`toBackendSnapshot`) |
| Evento de settings | — | `Models/CaseEvent.cs` (`CaseEventDetail`) | `client/src/lib/api.ts` (detail), `client/src/lib/case-events.ts` |
| Códigos de error | `Common/AgentErrorCodes.cs` | — | `client/src/lib/agent.ts` (`AgentErrorCode`), `client/src/lib/agent-messages.ts` |
| Capability | `Controllers/HealthController.cs` | — | `client/src/lib/agent.ts` (`supportsSessionCamera`) |

### 7.2 Tatana → web, HTTP

**7.2.1 `GET /cameras[?refresh=true]`** → 200

```json
{ "cameras": [ { "id": "Cámara de la MacBook Air", "name": "Cámara de la MacBook Air", "backend": "avfoundation" } ] }
```
- `backend` ∈ `avfoundation` | `dshow` | `v4l2` | `mock`. En Windows `id` = `"@device_pnp_\\\\?\\usb#vid_…"`.
- Sin ffmpeg: `{ "cameras": [], "error": "…" }`.

**7.2.2 `POST /cameras/preview`** — `{ "id": "…", "name": "…" }` → 200 `{ "ok": true }` o `{ "ok": false, "detail":
"permission", "error": "…" }`. 409 `camera_busy` con la cámara de la sesión grabando.

**7.2.3 `DELETE /cameras/preview`** → 204. **`GET /cameras/preview.jpg`** → `image/jpeg` | 404
`camera_preview_unavailable`. La web agrega `?t=<ms>` para no usar caché (Tatana lo ignora).

**7.2.4 `POST /cases/{caseId}/session/start`** (cambio): `camera` deja de ignorarse.

```json
{ "serial": "R58RA3R5AGA", "platform": "android", "android_version": 11, "segment_minutes": 30,
  "skip_screen_check": false, "origin_id": "t_8f2c…",
  "camera": { "id": "Cámara de la MacBook Air", "name": "Cámara de la MacBook Air", "with_audio": false } }
```
- `camera.id` es opcional (si falta, se busca por `name`). `ios_mode` sigue sin uso en esta fase.

**7.2.5 Rutas nuevas de la cámara de la sesión** → 200 `SessionStateDto` (7.2.7)

| Ruta | Body |
|---|---|
| `POST /cases/{caseId}/session/camera/start` | `{ "id": "…", "name": "…", "with_audio": false, "origin_id": "…" }` |
| `POST /cases/{caseId}/session/camera/stop` | `{ "origin_id": "…" }` |
| `POST /cases/{caseId}/session/camera/retry` | `{ "origin_id": "…" }` |

Errores: 400 `invalid_request` (falta `name`, texto con controles o > 200), 400 `session_camera_unsupported`, 409
`session_not_active`, 409 `camera_busy`, 507 `insufficient_storage` (solo `camera/start` y `retry`). Una cámara que no
abre **no** es error HTTP: 200 con `camera_state: "failed"` y el aviso.

**7.2.6 Diario (`CaseSessionDto`, `GET /cases/{caseId}/session`)**, campos nuevos:

```json
{
  "camera": { "name": "Cámara de la MacBook Air", "width": 1280, "height": 720, "fps": 15, "with_audio": false },
  "segments": [
    { "id": "…", "track": "camara", "index": 2, "filename": "sesion_camara_002_20261015_143210.mkv",
      "state": "saved", "started_at": "2026-10-15T17:32:10.050Z", "stopped_at": "2026-10-15T18:02:10.050Z",
      "recorded_seconds": 1800.0, "duration_seconds": 1800.0, "validation": "ok", "recovered": false,
      "stop_reason": "rotacion", "size": 31457280, "sha256": "…",
      "camera": { "name": "Cámara de la MacBook Air", "width": 1280, "height": 720, "fps": 15, "with_audio": false } }
  ],
  "marks": [
    { "id": "…", "at": "2026-10-15T17:40:00.300Z", "segment_index": 1, "offset_seconds": 512.4,
      "camera_segment_index": 2, "camera_offset_seconds": 470.3, "text": "Abre el chat", "kind": "manual" },
    { "id": "…", "at": "2026-10-15T17:45:03.000Z",
      "camera_segment_index": 2, "camera_offset_seconds": 773.0, "text": "Cambio el cable", "kind": "manual" }
  ]
}
```
- `segments[].camera`: solo en tramos `camara`. **Nunca** lleva el `id` técnico (D5: no va al backend).
- `marks[].camera_segment_index` / `camera_offset_seconds`: solo si un tramo de cámara cubre `at`.
- **[DU1=A]** `marks[].segment_index` / `offset_seconds` faltan si la marca se hizo con la pantalla sin grabar.
- Internos que **no** salen por HTTP: `camera_run` (§5.4) y `segments[].part_file`.
- `schema_version` sigue en 1 (cambios aditivos).

**7.2.7 Estado (`SessionStateDto`)**, cambios:

```json
{ "camera_state": "recording", "camera_segment_index": 2, "camera_segment_started_at": "2026-10-15T17:32:10.050Z",
  "camera": { "id": "Cámara de la MacBook Air", "name": "Cámara de la MacBook Air", "width": 1280, "height": 720,
              "fps": 15, "with_audio": false },
  "warnings": [ { "kind": "camera_failed", "since": "…", "detail": "permission" } ] }
```
- `camera_state` ∈ `off` | `recording` | `failed` (ya existía; ahora se usa).
- `camera` (nuevo, `SessionActiveCameraDto`): la cámara de la sesión activa mientras `camera_state ≠ "off"`; con
  `failed`, la última pedida (W×H y fps faltan si nunca arrancó). Este sí lleva `id` (lo usa la web para D5).
- `warnings[].detail` para `camera_failed` ∈ `permission` | `blocked` | `not_found` | `unsupported` | `stalled`, o
  ausente.

**7.2.8 `/health.capabilities`** suma `recording_session_camera_v1` **solo con `CameraEnabled`** efectivo: §7.2.1–7.2.7,
`camera` en el start y la ubicación de las marcas en la cámara.

### 7.3 Tatana → web, WebSocket

Sin tipos nuevos. `session_state`, `session_segment_closed` (también para tramos `camara`) y `session_mark_added` llevan
los campos de 7.2.6/7.2.7, con `null` explícitos (por ejemplo `"camera": null`, `"camera_segment_index": null`).

### 7.4 Web → API

`PUT /api/cases/{id}/recording-session/settings` — mismo body, ahora con `with_camera` real:

```json
{ "enabled": true, "with_camera": true }
```

`PUT /api/cases/{id}/recording-session` (snapshot, F1 §7.5) — `toBackendSnapshot` arma **campo por campo** (no copia el
objeto de Tatana):

```json
{ "camera": { "name": "…", "width": 1280, "height": 720, "fps": 15, "with_audio": false },
  "segments": [ { "…": "…", "camera": { "name": "…", "width": 1280, "height": 720, "fps": 15, "with_audio": false } } ],
  "marks": [ { "id": "…", "at": "…", "segment_index": 1, "offset_seconds": 512.4,
               "camera_segment_index": 2, "camera_offset_seconds": 470.3,
               "text": "…", "filename": null, "kind": "manual" } ] }
```
- `segments[].camera`: objeto o `null` (pantalla). `marks[].segment_index`/`offset_seconds` [DU1=A] y
  `marks[].camera_segment_index`/`camera_offset_seconds`: número o `null`.

### 7.5 API → web

- `Case.recording_session.segments[].camera` (objeto o `null`), `marks[].segment_index` / `offset_seconds` (número o
  `null` [DU1=A]), `marks[].camera_segment_index`, `marks[].camera_offset_seconds` (número o `null`).
- Evento `recording_session_settings_changed`: `detail.previous_enabled`, `detail.previous_with_camera` (bool; ausentes
  si `null`).

### 7.6 Códigos de error nuevos (Tatana)

| HTTP | `code` | Texto (`error`) |
|---|---|---|
| 400 | `session_camera_unsupported` | "La cámara del procedimiento no está habilitada en este Tatana." |
| 409 | `camera_busy` | "La cámara del procedimiento está grabando. Detenela para elegir otra." |
| 404 | `camera_preview_unavailable` | "Todavía no hay vista previa de la cámara." |

### 7.7 Tolerancia entre versiones

| Combinación | Qué pasa |
|---|---|
| Web nueva + Tatana sin `recording_session_camera_v1` (Tatana 1.8 de F1, o Windows con el flag apagado) | No se muestra "Sumar cámara". La pantalla funciona como en F1. Si el caso tenía `with_camera: true`, la web no manda `camera` en el start y no avisa (el hueco de cámara, si lo había, queda abierto). |
| Web vieja + Tatana nuevo | La web nunca pide cámara; Tatana nunca la abre. Las marcas traen `camera_*` ausentes. |
| Web nueva + backend viejo | El snapshot trae campos que el backend ignora (System.Text.Json). Con [DU1=A], una marca sin `segment_index` falla la validación vieja (400 `invalid_recording_session`, `marks[i].segment_index`): solo pasa si se despliega la web sin el backend; se despliegan juntos (§12.4). |
| Casos de F1 | Sin cámara: textos del informe idénticos. El panel web cambia solo por B-9. |

---

## 8. Informe

Solo cambian los textos de §6.4. La plantilla, los placeholders, los bloques y la tabla de tramos (F1 §8.1–§8.3) no
cambian: los tramos de cámara ya entran en `SessionFilenames` y en la segunda tabla con "Cámara · tramo…".

---

## 9. Web (`client/`)

Mismo sistema visual que F1 §9 (`fx-*`, `FxBanner`, claro/oscuro, `prefers-reduced-motion`, teclado y lector de
pantalla, 360/390/1024/1440 px).

### 9.1 Tipos y librería

- **`lib/agent.ts`:**
  - tipos: `CameraDevice { id; name; backend }`, `CameraListResponse { cameras; error? }`, `CameraPreviewResult { ok;
    detail?; error? }`, `SessionActiveCamera { id; name; width?; height?; fps?; with_audio }`,
    `SessionCameraFailDetail = "permission" | "blocked" | "not_found" | "unsupported" | "stalled"`;
  - `SessionStateDto.camera?: SessionActiveCamera | null`; `SessionWarning.detail?: SessionCameraFailDetail | string |
    null`; `SessionSegmentDto.camera?: SessionCameraInfo | null`; `SessionMarkDto.segment_index?: number | null`
    [DU1=A], `offset_seconds?: number | null` [DU1=A], `camera_segment_index?: number | null`, `camera_offset_seconds?:
    number | null`; `SessionStartRequest.camera?: { id?: string; name: string; with_audio: boolean }`;
  - `AgentErrorCode` += `session_camera_unsupported`, `camera_busy`, `camera_preview_unavailable`;
  - métodos: `listCameras(refresh?)`, `startCameraPreview(id, name)`, `stopCameraPreview()`, `cameraPreviewURL()`
    (con `?t=`), `startSessionCamera(caseId, cam)`, `stopSessionCamera(caseId)`, `retrySessionCamera(caseId)`. Todos con
    `origin_id` como los de F1 y timeout de 15 s en los de la sesión.
- **`lib/api.ts`:** `RecordingSessionSegment.camera?: RecordingSessionCamera | null`; `RecordingSessionMark` con
  `segment_index`/`offset_seconds` anulables [DU1=A] y `camera_segment_index`/`camera_offset_seconds`; detail de
  evento `previous_enabled?`, `previous_with_camera?`.
- **`lib/agent-messages.ts`:** los 3 códigos.
- **`lib/session-camera-pref.ts`** (nuevo, D5): `loadSessionCamera(): { id; name } | null` y `saveSessionCamera(cam)` en
  `localStorage` (clave `factum:session-camera`), con `try/catch` (modo privado). Solo id y nombre; el micrófono no se
  recuerda (la casilla arranca apagada).
- **`lib/recording-session.ts`:**
  - `toBackendSnapshot`: `camera` de cabecera y de cada tramo armados campo por campo (`name`, `width`, `height`, `fps`,
    `with_audio`), y las marcas con los 4 campos de ubicación (`?? null`).
  - **B-9:** `coveredSeconds(segments, track)` — misma unión de intervalos que `ReportSessionTexts.CoveredSeconds`
    sobre los tramos `saved` de la pista (orden por `started_at`; un `stopped_at` anterior al inicio cuenta como el
    inicio). `sessionSummary(rs)` pasa a devolver `{ screen: { segments, coveredSeconds }, camera: { segments,
    coveredSeconds }, gaps, totalBytes }`.
  - `segmentSeconds(s)` = `recorded_seconds` (D10 = A), con respaldo `duration_seconds` solo si falta.
  - `markLocationText(mark, hasCamera)`: "tramo 1, minuto 08:32" / "pantalla: tramo 1, minuto 08:32; cámara: tramo 2,
    minuto 07:50" / "pantalla: no grababa en ese momento; cámara: …" — los mismos textos que el informe (§6.4).
  - `warningText` para `camera_failed` por `detail`:
    - `permission`: "La cámara dejó de grabar a las HH:MM. Tatana no tiene permiso para usar la cámara: habilitalo en
      Ajustes del Sistema > Privacidad y seguridad > Cámara." (ya existe);
    - `blocked`: "La cámara dejó de grabar a las HH:MM. Windows no deja usar la cámara: revisá Configuración >
      Privacidad y seguridad > Cámara, o cerrá la aplicación que la esté usando.";
    - `not_found`: "La cámara dejó de grabar a las HH:MM. No se encuentra la cámara: revisá que esté conectada.";
    - `unsupported`: "La cámara dejó de grabar a las HH:MM. La cámara no aceptó ningún modo de video.";
    - `stalled` y sin detail: "La cámara dejó de grabar a las HH:MM.";
    - si la cámara nunca grabó en esta sesión (el aviso llega en el arranque), "No se pudo empezar a grabar la cámara."
      en lugar de "La cámara dejó de grabar a las HH:MM.", con el mismo agregado.
  - `capacityText(cap, { camera })`: con cámara, "(≈ N h de grabación con cámara, contando la copia del ZIP)".

### 9.2 `hooks/useRecordingSession.ts`

- `SessionCapability` = `"unknown" | "none" | "android" | "android+camera"` (`supportsSessionCamera()`).
- Estado nuevo: `cameras: CameraDevice[] | null`, `camerasError`, `selectedCamera: { id; name } | null`, `withMic`,
  `cameraOptIn` (la casilla "Sumar cámara del procedimiento"), `previewOn`, `previewProblem`, `busy.camera`.
- **Activación (D1 = A):** `ensureStarted` agrega `camera` al body si `rs.with_camera === true` y la capability lo
  permite: `id` = el recordado (D5) si su `name` coincide con `rs.camera?.name` o si no hay `rs.camera`; si no, solo
  `name` = `rs.camera.name`; `with_audio` = `rs.camera?.with_audio ?? false`. Si Tatana no puede abrirla, llega el aviso
  con "Reintentar cámara" (escenario de la HU).
- **Acciones nuevas:**
  - `setCameraOptIn(on)`: al prender, `listCameras()`, preselección (recordada → por nombre → la primera) y
    `startCameraPreview`. Al apagar con la cámara grabando: confirmación (§9.3) → `stopSessionCamera` →
    `saveRecordingSessionSettings({ enabled: true, with_camera: false })`. Al apagar sin grabar: `stopCameraPreview`.
  - `selectCamera(cam)`: cambia la preview. Deshabilitado con `camera_state === "recording"`.
  - `startCamera()`: `stopCameraPreview` → `startSessionCamera(caseId, { ...selected, with_audio: withMic })`. Si queda
    `recording`: `saveSessionCamera(selected)` y, si `rs.with_camera !== true`, `saveRecordingSessionSettings({ enabled:
    true, with_camera: true })`.
  - `retryCamera()`: `retrySessionCamera`.
  - `refreshCameras()`: `listCameras(true)`.
  - Al salir del paso 3 o desmontar: `stopCameraPreview()` (sin esperar).
- **Apagar la sesión** (`setEnabled(false)`) con la cámara: el `stop` de la sesión ya corta la cámara;
  `saveRecordingSessionSettings({ enabled: false, with_camera: false })`.
- **Marcar** (`canMark`): `isSessionRecording(state)` **o** [DU1=A] `state === "waiting_device" && camera_state ===
  "recording"`.
- La capacidad se pide con `camera = cameraOptIn || camera_state === "recording"`.

### 9.3 Componentes

- **`SessionRecordingCard.tsx`**: debajo del switch, solo con la sesión habilitada y `android+camera`:
  - casilla "Sumar cámara del procedimiento" (`aria-expanded` sobre el bloque, `aria-controls`);
  - al marcarla, **`SessionCameraPicker.tsx`** (nuevo):
    - desplegable "Cámara" (`<select>` o el `Dropdown` del sistema, con `label`), botón "Actualizar lista";
    - sin cámaras: "No se encontró ninguna cámara en esta PC." y la casilla queda sin efecto (el botón de arranque
      deshabilitado); con `camerasError`, ese texto;
    - vista previa de 320 px (alto proporcional, `aspect-video`, `alt="Vista previa de la cámara del procedimiento"`),
      refresco cada 1,5 s; con `prefers-reduced-motion`, sin refresco automático y con el botón "Actualizar vista";
      `previewProblem` (`ok: false`) en lugar de la imagen, con el texto de `warningText` correspondiente;
    - texto de privacidad: "Encuadrá las manos y el celular, no las caras. La cámara graba sin audio salvo que marques
      «Con micrófono de la PC»."
    - casilla "Con micrófono de la PC", apagada;
    - botón primario "Empezar a grabar la cámara" (con `busy.camera`, "Abriendo la cámara…"); habilitado con una cámara
      elegida y la sesión `recording`/`rotating`/`waiting_device`;
    - con la cámara grabando: el desplegable y el micrófono deshabilitados mostrando la cámara en uso, la vista previa
      sale de la grabación, y en lugar del botón, el texto "La cámara está grabando el procedimiento." Sacarla = destildar
      la casilla, con `ConfirmDialog`: "Va a quedar un hueco en el video de la cámara. ¿Dejar de grabar la cámara?";
    - con `failed`: desplegable habilitado (se puede elegir otra) y "Empezar a grabar la cámara".
  - `capacityText(cap, { camera })`.
- **`SessionIndicator.tsx`**: el ícono pasa a `Video` (`aria-label="Cámara grabando"`) con `recording` y `VideoOff`
  tachado (`aria-label="Cámara detenida"`) con `failed`. El `role="status"` suma los anuncios "Cámara grabando" y "La
  cámara dejó de grabar".
- **`MarkMomentPopover.tsx`** y el aviso de marca guardada: "Marca guardada · {markLocationText}".
- **`CaptureStep.tsx`:**
  - aviso `camera_failed` (`FxBanner tone="warn"`, nunca modal) con `warningText` y la acción "Reintentar cámara"
    (`retryCamera`);
  - **D4 = B:** con `camera_state === "recording"` **o** `previewOn` (D-T10), "Foto del perito", "Foto del titular" y
    "Filmar con cámara externa" deshabilitados con el motivo visible y en `aria-describedby`: "La cámara del
    procedimiento está en uso. Detenela o quitala para usar la cámara desde el navegador." `IdentityCard` suma la prop
    `disabledReason?: string` (texto bajo el botón y `title`).
- **`EvidenceTray.tsx`:** la etiqueta del tramo usa `trackLabel(track)`: "Cámara · tramo {i} · HH:MM–HH:MM",
  intercalados por hora con los de pantalla. Sin botón de quitar.
- **`SessionRecordPanel.tsx` (B-9, D10 = A):**
  - resumen: sin cámara, "{n} tramos de pantalla · {total} · {k} huecos · sellos {s}/{t}"; con cámara, "{n} tramos de
    pantalla ({total_p}) y {m} de cámara ({total_c}) · {k} huecos · sellos {s}/{t}". Totales = `coveredSeconds` por
    pista;
  - cada tramo: duración = `recorded_seconds` ("el video dura X de Y grabados" en los incompletos, ya está);
  - marcas con `markLocationText`;
  - si hubo cámara, una línea por configuración: "Cámara «{nombre}» ({W}×{H}, {fps} fps, {sin audio | con micrófono})".
- **`lib/case-events.ts`**, `recording_session_settings_changed`:
  - `enabled: false` → "Se desactivó la grabación de la sesión" (igual);
  - `enabled: true, with_camera: true, previous_enabled: true, previous_with_camera: false` → "Se sumó la cámara a la
    grabación de la sesión";
  - `enabled: true, with_camera: false, previous_with_camera: true` → "Se quitó la cámara de la grabación de la
    sesión";
  - el resto, como en F1.

---

## 10. `agent-ui/`

Solo `package.json` → `build.mac.extendInfo` (F1 §10, textos exactos):

```json
"extendInfo": {
  "NSCameraUsageDescription": "Tatana usa la cámara para grabar el procedimiento de recolección cuando el perito lo activa.",
  "NSMicrophoneUsageDescription": "Tatana usa el micrófono solo cuando el perito lo activa para una grabación."
}
```

macOS atribuye el permiso a la app que lanzó el proceso (Tatana.app → `Factum.Agent` → ffmpeg), así que la descripción
tiene que estar en el `Info.plist` de la app de Electron. Sin firma ni notarización (fuera de alcance): con la firma
*ad hoc* de electron-builder, macOS puede volver a pedir el permiso después de cada actualización (riesgo en §18).

---

## 11. Configuración y despliegue

- **Tatana `appsettings.json`:** `Agent:Session` suma `CameraStartTimeoutSeconds`, `CameraStallSeconds`,
  `CameraPreviewIdleSeconds` con sus defaults y `CameraBytesPerMinute: 18874368`. `CameraEnabled` **no** se escribe
  (default por sistema operativo); se documenta en `AGENTE_TATANA.md`.
- **Backend y deploy:** sin cambios.
- **`AGENTE_TATANA.md`:** casos de uso de la cámara (lista, previa, empezar, quitar, reintentar, recuperación) y cómo
  habilitarla en Windows.

---

## 12. Compatibilidad, versiones y release

### 12.1 Documentos existentes
Campos nuevos anulables (`[BsonIgnoreIfNull]`), `SegmentIndex`/`OffsetSeconds` a anulables (los valores guardados se
leen igual). Sin backfill, sin índices, sin tocar documentos ajenos.

### 12.2 Datos en la PC
Diarios de F1: sin `camera_run` ni campos de cámara; se leen igual. Archivos nuevos propios de Tatana:
`sessions/<caseId>/camara.csv`, `camara_NNNN.part.mkv`, `camara-preview.jpg` y `camera-preview/preview.jpg`.

### 12.3 Clientes y Tatana viejos
Ver §7.7.

### 12.4 Release
- Tatana: release posterior a la de F1 (`tatana-v1.9.0` o la que siga). Para exigirla: `FACTUM_TATANA_VERSION_MINIMA`.
- Backend y web: se despliegan juntos (por [DU1=A], §7.7).

---

## 13. Decisiones técnicas

| # | Decisión | ¿Usuario? |
|---|---|---|
| D-T1 | `-tune zerolatency` en la cámara: con la misma calidad pesa ~50 % más (medido con testsrc2), pero el corte y el ancla horaria quedan exactos y un `kill -9` pierde ≤ 2 s en lugar de ~3,5 s. `CameraBytesPerMinute` sube a 18 MiB. | no (avisar) |
| D-T2 | Horas de la cámara = ancla (línea `Output #0`) + tiempos del CSV del segment muxer: inicio y fin coinciden con el video y los tramos quedan contiguos. | no |
| D-T3 | Pista de cámara con ciclo propio dentro de la misma sesión (`ICameraTrack` aparte de `ISessionTrack`); sigue con el cable desconectado y se corta en todos los fines de sesión (§5.6). | no (D2 = A, D9 = A) |
| D-T4 | Cada tramo de cámara guarda su configuración (`segments[].camera`): si en el caso se cambió de cámara, de modo o de micrófono, el informe lo dice por tramos. La cabecera `camera` queda como "última usada". | no |
| D-T5 | Ubicación de la marca en la cámara solo si un tramo de cámara la contiene (sin respaldo). | no |
| D-T6 | La recuperación concilia el CSV entero: ffmpeg sigue vivo sin Tatana y puede cerrar tramos solo (§4.2). Señal SIGTERM para ffmpeg (QA #80 B-1). | no |
| D-T7 | Sin reintento automático de la cámara: aviso + "Reintentar cámara" (lo pide la HU). Vigilancia de archivo quieto (`CameraStallSeconds`) para una cámara que se cuelga sin que ffmpeg salga. | no |
| D-T8 | Micrófono de la cámara en macOS = `default` del sistema, no `:0` (en esta Mac el `:0` es el del iPhone). | no |
| D-T9 | En el `start` de la sesión, una cámara que no abre (o sin espacio para las dos pistas) no impide grabar la pantalla: 200 con `camera_state: "failed"` o sin cámara + `disk_low`. En `camera/start` explícito, sin espacio → 507. | no |
| D-T10 | D4 = B se extiende a la **vista previa** antes de grabar: en Windows también ocupa la cámara. | no (avisar) |
| D-T11 | `with_camera` del backend = "la cámara grabó / graba en este caso por decisión del perito": se pone en `true` al arrancarla con el botón y en `false` al quitarla. El evento de settings suma `previous_enabled`/`previous_with_camera` para decir "Se sumó / se quitó la cámara". | no |
| D-T12 | `CameraEnabled` por defecto solo en macOS (Windows por decisión del usuario; Linux tampoco tiene QA). | no (avisar) |
| D-T13 | Textos del informe con cámara (§6.4): se corrige "con los mismos cortes" y "aproximadamente" (falsos con D2 = A) y se agrega el total por pista. Los textos sin cámara, aprobados en #80, no cambian. | no (avisar) |
| D-T14 | La vista previa durante la grabación sale del mismo ffmpeg (segunda salida); antes de grabar, un ffmpeg solo de previa que se corta solo a los 120 s sin pedidos. | no |
| D-T15 | `schema_version` del diario sigue en 1: todos los cambios son aditivos. | no |
| D-T16 | Marcas con la pantalla sin grabar. | **DU1** |

---

## 14. Tests (sin cámara real)

Los que necesitan ffmpeg usan el patrón `SyntheticRecorderFactAttribute` (se saltean sin ffmpeg). Todos los de cámara
usan **lavfi** (`CameraLavfi`), en carpetas temporales propias.

### 14.1 Tatana (`server/tests/Factum.Agent.Tests/`)

| Archivo | Qué cubre |
|---|---|
| `Session/CameraDevicesTests` | Fixture real anonimizado de §4.1: 4 cámaras, sin `Capture screen`, sin los de audio. `DshowDevices.ParseVideoDevices` con los fixtures de `DshowDevicesTests` (nuevo y viejo): nombre + alternative name; dispositivo `(audio, video)` en las dos listas; `ParseAudioDevices` sin cambios (los tests actuales siguen). |
| `Session/CameraArgsTests` | `Build` para mac (nombre, nombre con `:` → índice, `:default` con audio), Windows (`video=…:audio=…`, reintento sin modo), lavfi; `-tune zerolatency`, `segment_start_number`, rutas. Parseo de "Supported modes" y elección del modo; parseo de la línea `Output #0`; `Classify` con cada fila de §5.5. |
| `Session/CameraTrackTests` [ffmpeg] | lavfi con tramos de 3 s durante ~8 s: ancla, `SegmentClosed` por cada línea del CSV con tiempos contiguos, cierre con `q` (última línea, salida 0), SIGTERM (salida 255 = limpio), EOF de stdin → sigue vivo, `kill -9` → `Exited` con el CSV leído antes; vigilancia con `CameraStallSeconds` (proceso pausado con SIGSTOP en macOS/Linux; se saltea en Windows). |
| `Session/SessionCameraStateTests` | Con `FakeTrackFactory` y un `FakeCameraTrack`: cámara en el start; `camera/start` con la sesión en curso sin hueco previo; falla → hueco `camara_fallo` + aviso + la pantalla sigue; `retry` cierra el hueco; `camera/stop` → hueco `detenido_por_perito`; desconexión del USB → la cámara sigue; `stop` desde `waiting_device` corta la cámara con el motivo pedido; `ShutdownAsync` en `waiting_device` corta la cámara; corte por disco corta las dos; tasa sumada en el disco; `camera_busy`; `session_camera_unsupported` con el flag apagado. |
| `Session/GapAndMarkRulesTests` | `Locate` de cámara (estricto); marca con ubicación en las dos pistas; [DU1=A] marca en `waiting_device` con cámara: sin `segment_index`, con la de cámara; sin cámara → 409 `session_not_active`. |
| `Session/SessionRecoveryTests` [ffmpeg] | ffmpeg lavfi lanzado por el test con su PID en `camera_run`, que ya cerró 2 tramos que el diario no conoce → SIGTERM → los 3 tramos `saved`, `recovered`, contiguos, hueco `camara` `tatana_cerrado`. ffmpeg matado con `kill -9` → remux recupera ≥ 1 s. PID de otro proceso → no se toca. `part_file` ajeno → no se toca. |
| `Session/SessionJournalTests` | Ida y vuelta con `camera_run`, `part_file`, `segments[].camera`, marcas con `camera_*`; `ToDto` **sin** `camera_run`, `part_file` ni `id` de cámara. |
| `HealthCapabilitiesTests` | `recording_session_camera_v1` con `CameraEnabled` true/false; default por SO. |
| `RecordingOptionsTests` (o `SessionOptionsTests`) | Defaults y `Normalize` de las claves nuevas. |

### 14.2 API (`server/tests/Factum.Backend.Tests/RecordingSession/`)

| Archivo | Qué cubre |
|---|---|
| `RecordingSessionRulesTests` | `segments[i].camera` (solo cámara, rangos), `marks[i].camera_*` (los dos o ninguno), [DU1=A] `segment_index` ausente solo con cámara; merge: marca con un `camera_*` distinto → conflicto; `camera` del tramo se completa una vez y distinto → conflicto. |
| `ReportSessionTextsTests` | Sin cámara: los textos de F1 **idénticos** (regresión). Con una cámara, con dos configuraciones (tramos 1 a 2 y 3), totales por pista con solapamientos, marcas en las dos pistas, solo pantalla, [DU1=A] solo cámara. |
| `RecordingSessionEventsTests` | `previous_enabled`/`previous_with_camera` en el evento de settings; `enabled: false` guarda `with_camera: false`. |

### 14.3 Web y agent-ui
`npx tsc --noEmit` en los dos y la pasada manual con Tatana en mock (cámara lavfi). Las funciones nuevas de
`lib/recording-session.ts` quedan puras y exportadas.

---

## 15. Checklists atómicos

Cada ítem se tilda en `progress/impl_backend_grabacion-sesion-completa-f2.md` /
`progress/impl_frontend_grabacion-sesion-completa-f2.md`.

### 15.1 `implementer-backend` — Tatana (`server/src/Factum.Agent`)

- [ ] `SessionOptions`: `CameraEnabled` (bool?, `EffectiveCameraEnabled` por SO), `CameraStartTimeoutSeconds`,
  `CameraStallSeconds`, `CameraPreviewIdleSeconds`, `CameraLavfi`; `CameraBytesPerMinute` = 18 MiB; `Normalize` +
  `appsettings.json` (§11).
- [ ] `AgentErrorCodes` += `session_camera_unsupported`, `camera_busy`, `camera_preview_unavailable`.
- [ ] `DshowDevices.ParseDevices(stderr, kind)` + `ParseVideoDevices`; `ParseAudioDevices` delega (tests actuales verdes)
  + tests nuevos.
- [ ] Fixture `Fixtures/Cameras/avfoundation-macbook-air.txt` (§4.1, anonimizado).
- [ ] `CameraArgs.cs` (puro): `Build`, `ParseSupportedModes`, `PickMode`, `ParseOutputStream`, `Classify` + tests.
- [ ] `CameraDevices.cs`: mac / Windows / Linux / mock, caché 10 s, `refresh`, `Resolve` + tests de parseo.
- [ ] `SessionModels.cs`: `SessionActiveCameraDto`, `SessionStateDto.Camera`, `JournalSegment.Camera` + `PartFile`
  (interno), `SessionJournalDoc.CameraRun` (interno), `SessionMarkDto.SegmentIndex`/`OffsetSeconds` anulables [DU1=A] +
  `CameraSegmentIndex`/`CameraOffsetSeconds`, `StartSessionRequest.Camera` tipado (`StartCameraRequest(Id, Name,
  WithAudio, OriginId)`), `CameraClosedRequest(OriginId)`; `ToDto` sin internos.
- [ ] `CameraTrack.cs` + `ICameraTrackFactory` (real, lavfi): §5.3–§5.5, CSV con sondeo de 1 s y lectura final antes de
  `Exited`, vigilancia, `StopAsync` con `q` → `ProcessStop` (`SessionSignal`) → kill; salida 255 tras SIGTERM = limpio
  + tests [ffmpeg].
- [ ] `CameraPreviewService.cs` (§5.9) + DI.
- [ ] `SessionRecorder.cs`: campos de cámara en `Active`; `StartCameraLockedAsync`, `StopCameraLockedAsync(reason)`;
  `StartAsync` con `camera`; `NewGap(…, track)`; hueco por pista en `StopLockedAsync`; cámara en
  `FinishInactiveLockedAsync`, `ShutdownAsync` (las dos ramas) y el corte por disco; `DiskTickAsync` con cualquiera de las
  dos pistas grabando; tasa sumada; `BuildState` con los campos de cámara; aviso `camera_failed`; cola de cierre para
  tramos de cámara (rename `part_file` → nombre final antes de `CloseAsync`).
- [ ] `MarkRules.Locate` por pista (cámara estricta); `AddMarkCore` con ubicación en la cámara y [DU1=A] en
  `waiting_device` con cámara.
- [ ] Tests de la máquina de estados de la cámara (`SessionCameraStateTests`) con un `FakeCameraTrack` en
  `SessionTestSupport.cs`.
- [ ] `SessionRecovery.cs`: §5.8 (huérfano con SIGTERM, conciliación del CSV, tramo sin línea, hueco `camara`) + tests
  [ffmpeg].
- [ ] `CamerasController.cs` (`GET /cameras`, `POST`/`DELETE /cameras/preview`, `GET /cameras/preview.jpg`) y en
  `CaseSessionController.cs` las 3 rutas `camera/*` (§7.2.5). Todas responden 400 `session_camera_unsupported` con el
  flag apagado.
- [ ] `HealthController.Capabilities`: `recording_session_camera_v1` solo con `CameraEnabled` efectivo (deja de ser un
  array fijo para esa entrada) + test.
- [ ] `Program.cs`: DI (`CameraDevices`, `ICameraTrackFactory`, `CameraPreviewService`); cortar la previa en
  `ApplicationStopping`.
- [ ] `AGENTE_TATANA.md`.

### 15.2 `implementer-backend` — API (`server/src/Factum.Backend`)

- [ ] `Models/RecordingSession.cs`: `SessionSegment.Camera`; `SessionMark.SegmentIndex`/`OffsetSeconds` anulables
  [DU1=A]; `CameraSegmentIndex`, `CameraOffsetSeconds`.
- [ ] `DTOs/RecordingSessionDtos.cs`: los mismos campos en `SessionSegmentDto` y `SessionMarkDto` de entrada.
- [ ] `RecordingSessionRules`: validación (§6.2), merge, `Copy`.
- [ ] `Models/CaseEvent.cs`: `PreviousEnabled`, `PreviousWithCamera`; `RecordingSessionFlow`: llenarlos y forzar
  `with_camera: false` con `enabled: false`.
- [ ] `ReportSessionTexts`: frase de cámara agrupada por configuración, totales por pista, marcas en las dos pistas
  (§6.4). Sin cámara, nada cambia.
- [ ] Tests de §14.2.

### 15.3 `implementer-frontend` — `client/`

- [ ] Skills obligatorios: `ui-ux-pro-max` antes del JSX final, `senior-frontend`, `3d-web-experience` (como criterio, sin
  3D) y `web-design-guidelines` como autochequeo; `ui-styling` para la tarjeta. Anotados en el progress.
- [ ] `lib/agent.ts` (§9.1): tipos, códigos y 7 métodos.
- [ ] `lib/api.ts`: tramos con `camera`, marcas con ubicación, detail `previous_*`.
- [ ] `lib/agent-messages.ts`: 3 códigos.
- [ ] `lib/session-camera-pref.ts` (D5).
- [ ] `lib/recording-session.ts`: `toBackendSnapshot` campo por campo, `coveredSeconds`, `sessionSummary` por pista,
  `segmentSeconds`, `markLocationText`, `warningText` por `detail`, `capacityText` con cámara.
- [ ] `hooks/useRecordingSession.ts` (§9.2).
- [ ] `SessionCameraPicker.tsx` + `SessionRecordingCard.tsx` (§9.3).
- [ ] `SessionIndicator.tsx`: `Video`/`VideoOff` y anuncios.
- [ ] `MarkMomentPopover.tsx` / aviso de marca: `markLocationText`; habilitado según `canMark`.
- [ ] `CaptureStep.tsx`: aviso `camera_failed` + "Reintentar cámara"; D4 = B (+ previa, D-T10); `IdentityCard`
  `disabledReason`.
- [ ] `EvidenceTray.tsx`: etiqueta por pista.
- [ ] `SessionRecordPanel.tsx`: B-9 por pista, `recorded_seconds`, marcas, configuración de la cámara.
- [ ] `lib/case-events.ts`: "Se sumó / se quitó la cámara…".

### 15.4 `implementer-frontend` — `agent-ui/`

- [ ] `package.json` → `build.mac.extendInfo` con los dos textos de §10. Ningún otro archivo. Los skills de UX se
  invocan igual y se deja constancia "sin hallazgos aplicables" para esta app.

---

## 16. Verificación

### 16.1 Antes de declararse `done`

**`implementer-backend`**
```bash
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet test  server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj
dotnet test  server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
```
Sin warnings nuevos. **No** abre la cámara real: todo con lavfi. Cada ffmpeg que lancen los tests lo cierra el propio
test (por su PID, nunca `pkill`/`pgrep` por nombre). Prueba en vivo opcional: Tatana desde la rama con `--port 8766
--data <scratch>` y `Agent__Session__CameraLavfi=true`, nunca el de :8765.

**`implementer-frontend`**
```bash
cd client && npx tsc --noEmit
cd agent-ui && npx tsc --noEmit -p tsconfig.web.json && npx tsc --noEmit -p tsconfig.node.json
```
Más una pasada en el navegador con Tatana en mock (`Agent__Mock=true`, `Agent__Session__MockSegmentSeconds=60`): la
cámara simulada graba de verdad con lavfi.

El orquestador corre `./ops/harness/verify.sh` antes de mover la fase.

### 16.2 QA manual en la Mac (con el usuario presente)

**Preparación.**
- Backend y web de la rama. `Recording__SegmentMinutes=5` en el backend (cortes cada 5 min).
- Tatana compilado desde la rama. Para los pasos de permisos (9) hace falta **la app empaquetada** (`agent-ui`,
  `npm run package:mac`, con el agente de la rama en `resources/agent`): el permiso de cámara se pide a nombre de la
  app y solo ahí rige `NSCameraUsageDescription`. El resto se puede hacer con Tatana lanzado desde la terminal (el
  permiso queda a nombre de la terminal).
- Samsung SM-M127F conectado y desbloqueado, con un **cronómetro** abierto en pantalla (sirve para cruzar los videos).
- La cámara se apunta al celular y a las manos, no a caras.
- Antes de empezar: anotar `adb shell settings get global stay_on_while_plugged_in` (F1 §16.2 paso 17) y `pgrep -l
  ffmpeg` (no debería haber ninguno de Tatana).

**Pasos.**
1. **Capability.** `curl -s localhost:<puerto>/health | jq .capabilities` incluye `recording_session_camera_v1`. En el
   paso 3, con la grabación activa, aparece "Sumar cámara del procedimiento".
2. **Lista.** Al marcarla: desplegable con las cámaras de la Mac **sin** "Capture screen". "Actualizar lista" funciona.
   Si se desconecta el iPhone de Continuidad, desaparece de la lista al actualizar.
3. **Vista previa.** Se enciende la luz de la cámara; la imagen chica se actualiza sola. Con "Reducir movimiento" de
   macOS activo, no se actualiza sola y aparece "Actualizar vista". Destildar la casilla: la luz se apaga en ≤ 2 s. Con
   la casilla tildada y sin tocar nada 2 minutos, la previa se corta sola.
4. **Navegador ocupado (D4).** Con la previa o la cámara grabando: "Foto del perito", "Foto del titular" y "Filmar con
   cámara externa" deshabilitados con el motivo.
5. **Empezar a grabar la cámara.** Botón → el indicador suma el ícono de cámara y el lector anuncia "Cámara grabando";
   el desplegable queda bloqueado mostrando la cámara; la previa sigue (ahora sale de la grabación, sin abrir la cámara
   dos veces: una sola luz y `pgrep -l ffmpeg` muestra un solo proceso de cámara).
6. **Tramos.** Esperar > 5 min: en la bandeja aparece "Cámara · tramo 1 · HH:MM–HH:MM" guardado, con hash y sello. En
   el diario (`GET /cases/<id>/session`), `stopped_at` del tramo 1 = `started_at` del tramo 2. `ffprobe` de los dos:
   duraciones ≈ lo grabado. Anotar el peso por minuto (para revisar `CameraBytesPerMinute`).
7. **Marcas en los dos videos.** "Marcar momento" con texto mirando el segundo exacto del cronómetro. Después, en el
   informe, abrir el video de pantalla y el de cámara en el minuto indicado: los dos tienen que mostrar ese segundo del
   cronómetro (± 1 s).
8. **Cable USB.** Desenchufar el celular: aviso de cable desconectado; **la cámara sigue** (ícono y luz). [DU1=A]
   "Marcar momento" sigue disponible y el aviso dice "Marca guardada · pantalla: no grababa en ese momento; cámara:
   tramo…". Reconectar: la pantalla reanuda en un tramo nuevo y la cámara no se cortó (sin hueco de cámara).
9. **Permiso de macOS** (app empaquetada). Primera vez: macOS pide el permiso con el texto de `NSCameraUsageDescription`.
   Negarlo: la pantalla graba igual y aparece "No se pudo empezar a grabar la cámara. Tatana no tiene permiso…".
   Habilitarlo en Ajustes del Sistema > Privacidad y seguridad > Cámara → "Reintentar cámara" → graba. (Para repetir:
   `tccutil reset Camera com.factum.tatana`, solo esa app.)
10. **Cámara caída.** Con el iPhone como cámara de Continuidad: bloquearlo o alejarlo hasta que la Mac lo pierda (o
    desenchufar una webcam USB, si hay). Aparece "La cámara dejó de grabar a las HH:MM…" con "Reintentar cámara"; el
    ícono pasa a `VideoOff`; la pantalla sigue. En la trazabilidad, el hueco de cámara. Recuperar la cámara →
    "Reintentar cámara" → vuelve y el hueco se cierra.
11. **Modo no aceptado.** Reiniciar Tatana con `Agent__Session__CameraWidth=1234 Agent__Session__CameraHeight=777`:
    la cámara arranca igual (reintento) y el diario y el informe dicen la resolución real.
12. **Sacar y volver a sumar (D3).** Destildar → confirmación → hueco de cámara "el perito detuvo la grabación"; la
    trazabilidad dice "Se quitó la cámara…". Volver a tildar y empezar: "Se sumó la cámara…" y el hueco se cierra.
13. **Micrófono.** Con "Con micrófono de la PC": `ffprobe` del tramo muestra pista de audio; el informe dice "con el
    micrófono de la estación de trabajo". Confirmar en Ajustes > Sonido > Entrada que el dispositivo por defecto es el
    de la Mac.
14. **Salir del paso y volver (D1, D5).** "Guardar evidencia…": se cierran las dos pistas y se apaga la luz. Volver al
    paso 3: la pantalla y la cámara arrancan solas; el hueco de cámara "el perito salió del paso…" se cierra. En otro
    caso nuevo, la misma cámara viene preseleccionada.
15. **Tatana se cierra.** (a) Desde su ventana: diálogo, confirmar; al reabrir, los tramos de las dos pistas guardados y
    hueco `tatana_cerrado` en las dos. (b) Matar `Factum.Agent` desde el Monitor de Actividad **más de 5 min** con la
    cámara grabando: la luz sigue encendida (ffmpeg huérfano). Al reabrir Tatana: la luz se apaga, aparecen **todos**
    los tramos de cámara, incluidos los que ffmpeg cerró mientras Tatana no estaba, contiguos y `recovered`.
16. **Espacio.** Con Tatana en mock y `Agent__Session__MockFreeBytes` bajo: la estimación con cámara es menor que sin
    ella; el aviso y el corte por espacio cortan las dos pistas.
17. **Generar.** ZIP con 7-Zip: los tramos de cámara en `sesion/`, sus `.tsr` en `sellos/`. Informe: frase de la cámara
    con la resolución y fps reales, "con sus propios cortes" (no "los mismos"), "Se grabaron X de pantalla en n tramos y
    Y de cámara en m tramos", marcas con las dos ubicaciones, sin "aproximadamente".
18. **B-9.** El panel "Registro audiovisual" (paso 6 y detalle) muestra el mismo total por pista y la misma duración por
    tramo que el informe y la trazabilidad. Con un caso de la fase 1 (solo pantalla): mismas cifras que su informe.
19. **Interruptor de Windows.** Reiniciar Tatana con `Agent__Session__CameraEnabled=false`: sin la capability, sin
    "Sumar cámara"; la pantalla funciona como en F1.
20. **Al final.** `pgrep -l ffmpeg` sin procesos de Tatana, luz apagada y `stay_on_while_plugged_in` como al principio.

---

## 17. Regla dura de datos (AGENTS.md)

- Ningún implementador borra ni modifica documentos de negocio preexistentes; los tests de Mongo usan base efímera y
  limpian por `_id`.
- Tatana borra solo sus archivos propios de esta HU (`camara.csv` ya conciliado, `preview.jpg`, `part_file` ya
  renombrados) y **nunca** un `part_file` que no esté en su diario o en su CSV.
- Las pruebas en vivo de Tatana usan `--port 8766 --data <scratch>`; el Tatana de :8765 no se toca.
- **La cámara de la PC se abre solo en la QA manual con el usuario presente.** Los implementadores y el reviewer usan
  lavfi (`CameraLavfi` o mock). Ningún test lista ni abre una cámara real.
- Los procesos ffmpeg de los tests se cierran por su PID propio.

---

## 18. Riesgos

| Riesgo | Mitigación |
|---|---|
| Permisos de macOS no verificados: ffmpeg 8.1 no pide autorización; el pedido pendiente puede trabar el arranque. | Timeout de 10 s → `detail: "permission"`; "Reintentar cámara"; paso 9 de la QA. |
| Firma *ad hoc* de la app: macOS puede volver a pedir el permiso tras cada actualización de Tatana. | Fuera de alcance (firma y notarización = HU de empaquetado). Se documenta en `AGENTE_TATANA.md`. |
| Peso real de la cámara distinto de 18 MiB/min (luz baja = ruido = más bits). | La tasa observada del último tramo corrige la estimación; paso 6 de la QA. |
| Una cámara que se cuelga sin que ffmpeg salga. | Vigilancia de archivo quieto (`CameraStallSeconds`). |
| `kill -9` o corte de luz: se pierden ≤ 2 s del tramo de cámara en curso. | `zerolatency` + `flush_packets`/`cluster_time_limit`; remux en la recuperación. |
| Windows (dshow, privacidad, cámara ocupada) sin probar. | `CameraEnabled` apagado por defecto fuera de macOS. |
| CPU: dos encoders (scrcpy en el celular, x264 en la PC) durante horas. | `veryfast` a 720p15 es liviano; se observa en la QA (Monitor de Actividad). |
| **Fuera de alcance, observado:** `MicCapture` usa `:0` en macOS y en esta Mac eso es el micrófono del iPhone por Continuidad. Afecta a las grabaciones puntuales "con micrófono" de hoy. | Se reporta al orquestador para un arreglo aparte (`:default`, como D-T8). |

---

## 19. Puntos de extensión para #89 (iPhone)

- `ICameraTrack` y todo §5.6 no dependen de la plataforma de la pantalla: la cámara funciona igual cuando #89 sume
  `IosScreenTrack`. Lo único que mira el serial es `_isConnected` (adb), que #89 tiene que generalizar.
- Las marcas ya tienen ubicación por pista (`MarkRules.Locate(track)`); la de iPhone usa la de pantalla.
- `ReportSessionTexts` arma la frase de pantalla por plataforma aparte de la de cámara; #89 cambia solo la de pantalla.
- La tasa de disco ya es la suma de pistas; #89 agrega su tasa de pantalla (D6) en `ScreenRate`.

## 20. Fuera de alcance

Todo lo de iPhone (#89, con D6 y D7); grabar solo la cámara (D9 = A); combinar pantalla y cámara o recodificar; grabar
el escritorio; limpiar `WebcamService` (D-T24); firma y notarización; Windows con hardware; el arreglo de `MicCapture`
(§18); la exportación JSON (#81).

## Validación del usuario (2026-10-15)

- DU1 = A: "Marcar momento" se permite con el cable desconectado y la cámara grabando; la marca queda ubicada solo en el video de cámara y `segment_index` pasa a opcional en Tatana, la API y la web.
- El bug de `MicCapture` (`:0` en macOS) queda fuera de esta HU: se dio de alta como HU propia (#90).
