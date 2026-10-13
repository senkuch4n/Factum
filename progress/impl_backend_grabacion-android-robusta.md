# Implementación backend — `grabacion-android-robusta` (HU #59)

**Estado:** done · **Rama:** `feat/grabacion-android-robusta` (sin commit, base `f71fa63`) · **Fecha:** 2026-10-13
**Alcance:** solo `server/src/Factum.Agent` (Tatana) y `server/tests/Factum.Agent.Tests`. `Factum.Backend`, `client/`,
`agent-ui/` y Mongo no se tocaron.

## Archivos tocados

| Archivo | Qué |
|---|---|
| `Models/AgentModels.cs` | B1: `RecordingOptions` (+ `Normalize`, `Clone`), `AgentOptions.Recording`, `MockAndroidRecordingSource`, `MockAndroidRecordingOutcome`. |
| `appsettings.json` | B2: sección `Agent:Recording` con los defaults (sin los `MockAndroid*`). |
| `Common/AgentErrorCodes.cs` | B3: `empty_evidence`, `already_recording`, `recording_start_timeout`, `recording_empty`, `recording_unreadable`, `not_recording`. |
| `Common/ProcessStop.cs` | B4: `escalateAfter` (SIGINT → SIGTERM, solo macOS/Linux). `StopAsync` nuevo devuelve `ProcessStopResult(Clean, Escalated)`. `StopGracefullyAsync` mantiene la firma vieja más el parámetro opcional, así que los otros llamadores (`IosService`) no cambian. |
| `Common/ScrcpyOutput.cs` (nuevo) | B5: `IsStartMarker` e `IsDebugLine`. |
| `Common/FfmpegProbe.cs` (nuevo) | B8: `ProbeAsync`, `Parse`, `ParseDuration`. Se mudó acá `ParseFfmpegDuration`. |
| `Services/VideoPostprocessService.cs` | `ParseFfmpegDuration` reenvía a `FfmpegProbe.ParseDuration`. Se sacó el regex duplicado. |
| `Services/RecordingValidation.cs` (nuevo) | B9: `RecordingValidationStatus`, `RecordingValidationDto` (`Create` redondea a 1 decimal), `Tolerance` y `Evaluate`. |
| `Services/RecordingRegistry.cs` (nuevo) | B12: `RecordingStateDto`, `UnclaimedRecordingDto`, `IRecordingStateSource` y `RecordingRegistry` (24 h, 20 entradas, dedup por filename, solo lo que existe en disco). |
| `Services/RecordingNotActiveException.cs` (nuevo) | Excepción con el mismo nombre y texto que la HU #60 ("No hay una grabación en curso."). |
| `Services/AdbService.cs` | B6/B10/B11/B13: snapshot inmutable (`RecordingSnapshot`, `Volatile`), flags nuevos, marcador de inicio, 504 por timeout, mic que arranca al ver el marcador, 409 `already_recording`, `LineTail` de 40 líneas sin las DEBUG, stop con escalado, remux, validación previa a la mezcla, mezcla del mic sin cortar al stream más corto y con chequeo del temporal, `empty` (borra lo de ese stop) / `unreadable` (`descartes/` con sufijo `_N`), log de una línea, mock que nunca escribe 0 B, `IRecordingStateSource`. Todo el trabajo usa `CancellationToken.None`. |
| `Services/IosService.cs` | B13: `IosSession.Udid/CaseId/Stopping`, `StartRecordingAsync(…, caseId, ct)`, `IRecordingStateSource`, `_session = null` en el `finally` del stop (el cuerpo pasó a `StopSessionAsync`, sin cambios de lógica), `IosStopResult` con `CaseId` y `Serial`, y `RecordingNotActiveException`. |
| `Controllers/RecordingController.cs` | B14: `case_id` (400 `invalid_case_id`), respuestas tipadas (`StartRecordingResponse`, `StopRecordingResponse`, `RecordingStoppedEvent`), `recording_stopped` también en las fallas 422, 409 `not_recording`, `registry.AddStopped`. |
| `Controllers/AgentStateController.cs` | B16: `recordings` y `unclaimed_recordings` al final del record posicional. Una fuente que falla no tumba el endpoint. |
| `Controllers/HealthController.cs` | B17: capability `recording_state_v1`. |
| `Program.cs` | B17: `IRecordingStateSource` (Adb e iOS) y `RecordingRegistry`. |
| `Services/CaseEvidenceStore.cs` | B15: 422 `empty_evidence` en `ImportAsync` (no mueve ni borra; también si ya estaba vacío en el caso) y en `CheckUpload` (y por lo tanto en la subida). |
| Tests nuevos | `RecordingValidationTests`, `FfmpegProbeTests`, `ScrcpyOutputTests`, `AdbRecordingArgsTests` (mux sin la opción de corte, args de scrcpy, sufijo de descartes), `RecordingOptionsTests`, `ProcessStopTests`, `RecordingRegistryTests`. |
| Tests ampliados | `CaseEvidenceStoreTests`: import de 0 B (no se mueve), 0 B ya en el caso, 1 byte OK, `CheckUpload(size: 0)`. |

## Verificación

```
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
    0 Warning(s)
    0 Error(s)

dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj
Passed!  - Failed: 0, Passed: 391, Skipped: 0, Total: 391   (antes: 337)
```

Los warnings CS8714/CS8619 de `EvidenceZipTests.cs` al compilar los tests ya estaban antes y no son de esta HU.
`grep -rn shortest server/src/Factum.Agent/Services/AdbService.cs` no devuelve nada. Quedan dos usos en `IosService`
(mux del burst y del modo `with_mic` de iPhone): la SDD §5.9 dice que en iOS no cambia nada más, así que no los toqué.
El reviewer tiene que leer el chequeo de §11.5 como "en Android".

## Diagnóstico (§3.3) — Samsung SM-M127F, Android 11, scrcpy 4.0 (Homebrew), macOS arm64

Tatana de prueba propio: el binario de `bin/Debug` en `:8766` (`:8767` para el mock), con `--data` en el scratchpad
y logs en Debug. "Terminal interactiva" se simuló lanzando con `perl -e '$SIG{INT}="DEFAULT"; …; exec'`, que deja
SIGINT y SIGTERM por defecto. D-2 y D-6 se lanzaron con `bash -c "nohup … &"`, que hereda SIGINT ignorado. Antes de
cada prueba: `pgrep scrcpy` vacío. Valores iniciales del celular: `stay_on_while_plugged_in = 0` y
`screen_off_timeout = 1800000`. **Al terminar: `0` y `1800000`**, pantalla encendida y sin keyguard.

| Paso | Configuración | Resultado | ¿Esperado? |
|---|---|---|---|
| **D-1** Señal por el camino de Tatana | SIGINT por defecto, 30 s, `case_id` | Start 200 en 2,28 s (curl). Log "scrcpy detenido limpiamente (exit 0)". Stop: **0,26 s** (flush 173 ms). `ok`, recorded 31,7 s y duration 31,4 s (dif. 0,3 s). `stay_on` pasó a 7 durante la grabación y volvió a 0. | Sí |
| **D-2** SIGINT ignorado heredado | `nohup … &`, 30 s | Log "scrcpy: SIGINT sin efecto; se envía SIGTERM" y "detenido limpiamente (exit 0, con SIGTERM)". Stop: **3,35 s**, `forced_stop: false`. `ok`, recorded 31,6 s y duration **34,4 s** (scrcpy siguió grabando los 3 s del escalado: dura más de lo grabado, y eso es `ok`). | Sí |
| **D-3** Pantalla apagada con el botón | 3 min: `KEYCODE_SLEEP` al min 1 y `KEYCODE_WAKEUP` al min 2 | `ok`, recorded 183,8 s y duration 183,5 s. `blackdetect`: tramo negro de **62,1 s a 123,3 s** (sin paquetes de video entre ~70 y 110 s). La duración del archivo es completa. | Sí. Se anota: el tramo apagado queda negro (detectarlo está fuera de alcance). |
| **D-4** Mic de la PC | 2 min, `android_with_mic: true` | **macOS no le dio permiso de micrófono** al ffmpeg lanzado desde esta sesión: quedó vivo sin escribir el `.mic.mka` y al detener se forzó ("ffmpeg (mic) no respondió a 'q'"). Sin `.mka` no hubo mezcla. Video `ok` 124,6 de 124,8 s. Stop 5,18 s (los 5 s de espera del mic). **Prueba offline de la mezcla** con el MKV real de D-4 (124,60 s) y los mismos args de `BuildMicMuxArgs`: con audio de 60 s → **124,55 s**; con audio de 140 s → 140,04 s. Con la opción vieja de corte y audio de 60 s → 114,52 s (recortaba). | Parcial: **queda para el usuario** la prueba con mic real (§11.4 paso 3). |
| **D-5** Flush vs. duración | 1, 5 y 12 min, con movimiento cada 60 s | 1 min: flush 162 ms, stop 0,22 s, `ok` 61,1/61,4. 5 min: flush 166 ms, stop 0,21 s, `ok` 300,4/300,7. 12 min: flush 139 ms, stop **0,20 s**, `ok` 721,1/721,3. | Sí. El flush no crece con la duración: **30 s fijos (Duda 2 A) sin cambios**. |
| **D-6** Cierre forzado | `nohup … &`, `StopTimeoutSeconds=1`, `SigtermAfterSeconds=0`, 1 min | "no respondió a la señal; se forzó el cierre", "MKV remuxeado". **`incomplete`**: recorded 63,8 s y duration **31,9 s** (`forced_stop: true`). Stop 1,15 s. `stay_on_while_plugged_in` volvió a **0** solo (lo restaura el servidor de scrcpy). Sin scrcpy vivo. | Sí. Reproduce la causa raíz de C2a: con kill se pierde el buffer (acá ~32 s), y ahora queda marcado. No hace falta que Tatana restaure el ajuste. |
| **D-7** Marcador por `Process` | Log Debug con la hora relativa | `[server] INFO: Device: [samsung] samsung SM-M127F (Android 11)` llegó en tiempo real a los **706–829 ms** del lanzamiento en las 9 grabaciones reales. `Recording started to …` llegó recién al final (+32,6 s, con buffer). | Sí. `RequireStartMarker = true` por defecto. |

### §11.2 paso 2 — grabación de 12 min (requisito del usuario)

| Dato | Valor |
|---|---|
| Inicio pedido / inicio real (marcador) | 16:22:47,144 / 16:22:47,873 UTC (start 200 en 2,29 s de curl) |
| Movimiento | `expand-notifications` + `collapse` cada 60 s (11 veces), pantalla quieta en el medio |
| `recorded_seconds` / `duration_seconds` | **721,3 / 721,1** (ffmpeg: `Duration: 00:12:01.07`, h264 486x1080 + opus) |
| `status` / `forced_stop` | `ok` / `false` (log: "scrcpy detenido limpiamente (exit 0)") |
| Tiempo del stop (curl) / flush | **0,197 s** / 139 ms |
| Tamaño | 4 782 195 B |
| `stay_on_while_plugged_in` | 7 durante la grabación, 0 después |
| Último cuadro | Último paquete de video a los 716,7 s (con pantalla quieta, scrcpy no manda cuadros nuevos). El reloj de la barra de estado dice **13:34** y el stop fue a las 13:34:49 hora local. Para mirarlo solo se recortó la barra de estado, y el PNG se borró. |

### Otras pruebas de §11.2

| Paso | Resultado |
|---|---|
| 1. Corta (regresión) | Es D-1: `ok`, sin forzar y con dif. 0,3 s. `/agent/state` durante la grabación: `recordings[0]` con `state = "recording"`, serial, filename y `case_id`. |
| 4. Arranque que no llega | Con el celular en `Dozing` (`KEYCODE_SLEEP`) y `StartTimeoutSeconds=8`, **el marcador igual llegó (829 ms)**: grabó `ok`. **No es reproducible así**. Para probar el camino 504 usé un `scrcpy` falso solo para esa instancia, primero en el `PATH` (crea el archivo de `--record`, nunca imprime el marcador). Resultado: **504 `recording_start_timeout`** en 4,06 s con "Detalle de scrcpy: INFO: ADB device found:", sin archivo del intento, sin proceso vivo y con `recordings: []`. El script se borró. Serial inexistente → 500 "scrcpy no pudo iniciar la grabación: ERROR: Could not find ADB device…", sin archivo. |
| 5. Huérfana + estado | Start con `case_id` → `busy: true` y aparece en `recordings`. Segundo start → **409 `already_recording`** (en < 1 ms, sin esperar el lock). Stop **cortado por el cliente** (`curl -m 0.05`, DA4) → igual terminó (`ok` 16,3/16,5). `recordings: []` y `unclaimed_recordings` con el archivo y su `case_id`. `POST /cases/<id>/evidence/import` → 200 con SHA-256. Después, `unclaimed_recordings: []`. Además: con el start en curso, `/agent/state` mostró `state: "starting"` sin `started_at`, y un stop pedido en ese momento esperó al start y cerró bien. |
| 6. Mock (`--mock`, `:8767`) | Outcome vacío → `ok` con duración medida (MKV `testsrc2` de 287 KB). `incomplete` → 200 `incomplete` con duration = recorded/2. `empty` → 422 `recording_empty` sin archivo. `unreadable` → 422 `recording_unreadable` con el archivo en `rec-mock/descartes/`. El segundo stop de cada uno → 409 `not_recording`. Start mock ~1,05 s ("Iniciando…"). Por WS (cliente Node): `recording_started` con `serial, platform, case_id, started_at, filename`, y `recording_stopped` con todos los campos de §6.4 (`error: null, code: null` en éxito; `filename: null, url: null, validation.status: "empty", error, code: "recording_empty"` en la falla). `case_id` inválido → 400 `invalid_case_id`. iOS mock con `case_id`: aparece en `recordings` con `ios_mode` y `stop?platform=ios` devuelve `platform`, `serial` y `case_id` sin `validation`. |
| 7. `empty_evidence` | `import` de `vacio_test.png` de 0 B → 422 "vacio_test.png está vacío (0 bytes) y no se guarda como evidencia.", y el archivo sigue en la raíz. `upload-check?size=0` → el mismo 422. |
| 8. Limpieza | Se cortaron todos los Tatana de prueba (8766 y 8767 libres, sin `scrcpy` ni ffmpeg de mic vivos). Cada grabación de prueba se borró por su ruta apenas se midió, y al final hice `rm -rf` de `$S/rec-mock`, `$S/diag`, `$S/diag-data` (y de mis `$S/tools`, `$S/gen` y `$S/fakebin`). No se tocó `agent-data`, `dev-data`, `~/Factum/Evidencia` ni Mongo. El backend en `:8080` siguió corriendo. Ajustes del celular como al principio. |

## Decisiones no obvias

1. **El mic arranca apenas aparece el marcador, en paralelo con la ventana de 1,5 s** (no después). Si arrancara
   después de la ventana, el audio quedaría ~1,5 s corrido respecto del video al mezclar. Si scrcpy muere en esa
   ventana, el mic se detiene y se borra todo lo del intento.
2. **`already_recording` se chequea antes de esperar el lock** y otra vez adentro: un segundo start durante un
   "Iniciando…" o "Deteniendo…" responde 409 al instante, en lugar de quedar colgado hasta 20 o 30 s.
3. **El stop espera el lock con `CancellationToken.None`**: si llega mientras el start está en curso, espera a que
   termine y detiene (probado). Lo mismo el start ante un stop (DA4).
4. **`RequireStartMarker = false`** espera 1,5 s como antes, no 20. Usa el marcador si llegó a verse, y si no
   "lanzamiento + 1 s" con un warning.
5. **Grabación cuyo scrcpy murió solo:** si se pide stop igual, pasa por la validación (antes devolvía el archivo
   tal cual). Si se pide start, el estado viejo se descarta como hoy, y el archivo queda en la raíz sin validar
   (fuera de alcance, §13).
6. **Serial y `case_id` en las fallas 422:** viajan en `Exception.Data` (no en `Extra`). Así el cuerpo HTTP queda
   exactamente `{ error, code, validation }` como dice §6.2 y el evento `recording_stopped` igual los lleva.
7. **`unclaimed_recordings` incluye iOS** (`validation` ausente). Es lo que admite el DTO de §5.7 y la Duda 9 A. No
   se registra ningún archivo de **0 B**, porque la importación lo rechazaría. Eso cubre el mock de iOS sin
   `MockIosRecordingSource`, que **sigue escribiendo 0 B como antes** (§5.9: iOS no cambia). Ojo frontend: ahora ese
   archivo falla al importar con `empty_evidence`. Para probar iOS en mock hay que usar `MockIosRecordingSource`.
8. **Respuestas y eventos como records** en lugar de objetos anónimos, para que el casing lo ponga la política
   snake_case y los null se omitan en HTTP (`WhenWritingNull`) y viajen como `null` en WS. Verificado contra los
   payloads reales (arriba).
9. **`RecordingOptions.Normalize`:** si `SigtermAfterSeconds ≥ StopTimeoutSeconds`, vuelve a 3, y a 0 si 3 tampoco
   entra en el `StopTimeoutSeconds` configurado. `WindowMode` se normaliza a minúsculas.
10. **Mock embebido:** MKV H.264 de 1 s y 16x16 (555 B), generado una vez con ffmpeg y en base64. Solo se usa si no
    hay ffmpeg o si falla `testsrc2`.
11. **`descartes/`:** el sufijo se arma sobre la última extensión (`a.mkv.mic.mka` → `a.mkv.mic_1.mka`). Si no se puede
    mover, queda en la raíz y se loguea. Nunca se borra.

## Contrato (§9) — coincide con la SDD

Comprobado con requests y eventos reales, no solo con el código: `case_id` en el body del start; 200 del start
`{filename, state, started_at, case_id?}`; 200 del stop con `platform, serial, case_id, validation`; `validation`
con `status, recorded_seconds, duration_seconds, duration_checked, forced_stop, started_at, stopped_at`; 422 con
`error, code (recording_empty|recording_unreadable), validation`; 409 `not_recording` y `already_recording`; 504
`recording_start_timeout`; 400 `invalid_case_id`; WS `recording_started`/`recording_stopped` como en §6.3 y §6.4;
`/agent/state` con `recordings[]` (`platform, serial, state, requested_at, started_at?, filename, case_id?,
android_with_mic?, ios_mode?`) y `unclaimed_recordings[]` (`platform, serial, filename, case_id?, stopped_at,
validation?`), siempre presentes; `operations` y `background_operations` sin cambios; 422 `empty_evidence`;
capability `recording_state_v1`. Una revisión de solo lectura de `client/src/lib/agent.ts` (hecho por el
implementer-frontend) usa los mismos nombres.

## Pendiente para el usuario (§11.4)

- **Mic de la PC (D-4 / §11.4 paso 3):** en esta sesión macOS no le dio permiso de micrófono a ffmpeg. Hay que
  probarlo desde Tatana instalado (o una terminal con permiso): 10 min con "mic de la PC" → el video tiene que durar
  lo mismo que la grabación y oírse el audio.
- **Windows (§11.4 paso 6):** `--no-window` con scrcpy 4.1 y el marcador por el pipe no se pudieron medir acá. Si
  falla el start: `"Recording": { "RequireStartMarker": false }`. Si sigue forzando el cierre: `"WindowMode":
  "no_video_playback"`.
- Desde la web: 12 min, recarga/WS, USB desenchufado, captura negra y regresión iOS (§11.4 pasos 1, 2, 4, 5, 7 y 8).

## Concurrencia con la HU #60

`RecordingNotActiveException` (en `Services/RecordingNotActiveException.cs`), `AgentErrorCodes.NotRecording` y el
`catch` → 409 en `RecordingController.Stop` usan el mismo nombre, texto, código y status que la SDD de
`tatana-ws-y-hosts`. El segundo PR resuelve un conflicto textual y conserva una sola definición.
