# SDD: Grabaciones de Android de 10 minutos o más completas, verificadas y recuperables

**Slug:** `grabacion-android-robusta` · **Issue:** #59 · **HU:** `docs/hu-grabacion-android-robusta.md`
(validada el 2026-10-13: Dudas 1 a 9 con la **opción A**, la recomendada).
**Base del análisis:** `develop` `36140d4`. Es una SDD **adelantada**: se escribió desde la rama
`feat/zip-verificacion-stored` sin tocar código. La implementación sale en `feat/grabacion-android-robusta` desde
`develop` actualizado.
**Fuentes:** `docs/qa/2026-10-13/e2e-navegador.md` (C2, A2, B1, pruebas físicas 1 a 3), `informe-formato.md` (R1),
`Refactorizaciones/ios-grabacion-larga.md` (contrato de `recording_stopped`, `/agent/state`, `background_operations`,
`onConnected`) y `Refactorizaciones/tatana-ws-y-hosts.md` (HU #60, toca `RecordingController.Stop`, ver §12).
**Modo:** autónomo. Las decisiones que en otro caso irían al usuario se tomaron con el criterio más conservador y
se marcan como **decisión del arquitecto** (DA, §8.2).

---

## 1. Resumen funcional

Tatana graba la pantalla del Android con scrcpy en un modo que **sí** responde a la señal de cierre (`--no-window`
en lugar de `--no-video-playback`) y escala a SIGTERM si SIGINT no llega. Así scrcpy cierra limpio en menos de un
segundo y escribe todo lo que tenía en buffer. Además mantiene el celular despierto mientras graba
(`--stay-awake`). Recién informa "grabando" cuando scrcpy confirma que el celular empezó a capturar, y toma esa hora
como inicio real. Al detener, espera hasta 30 s el cierre limpio, y el kill + remux queda como último recurso.
Después de cerrar, remuxear y mezclar el micrófono (ya sin `-shortest`), **valida** el archivo en un solo lugar:
tamaño mayor que 0, que ffmpeg lea la duración y haya un stream de video, y que la duración alcance el tiempo
grabado con una tolerancia de max(3 s; 2 %). Una grabación vacía o ilegible no se devuelve como grabación: el stop
responde un error con código y el cliente no la agrega. Una incompleta sí se guarda y se marca con un aviso en la
bandeja y en el paso 5. `ImportAsync` y la subida rechazan cualquier archivo de 0 bytes. Tatana expone en
`/agent/state` las grabaciones en curso (Android e iPhone) con serial, estado, inicio real, archivo y `case_id`, y
las detenidas que todavía no se importaron. El cliente consulta ese estado al entrar al paso 3 y en cada
(re)conexión del WebSocket. Si la grabación es de este caso, recupera "Detener" y el cronómetro. Si es de otro caso,
muestra un aviso con "Detener" que nunca la importa acá. Una captura de pantalla toda negra se marca en el cliente
con un aviso que no bloquea.

## 2. Toca

| Lado | ¿Toca? | Qué |
|---|---|---|
| **backend (API)** `server/src/Factum.Backend` | **no** | Duda 4 A (no C) y Duda 7 A: ni `registerEvidence` ni la plantilla cambian. |
| **backend (Tatana)** `server/src/Factum.Agent` | **sí** | `AdbService` (flags de scrcpy, marcador de inicio, estado observable, stop sin `ct` del request, validación, mux sin `-shortest`, mock válido), `Common/ProcessStop.cs` (escalado SIGINT → SIGTERM), `Common/FfmpegProbe.cs` (nuevo), `Services/RecordingValidation.cs` (nuevo), `Services/RecordingRegistry.cs` (nuevo), `IosService` (`case_id` + estado observable), `RecordingController`, `AgentStateController` (`recordings`, `unclaimed_recordings`), `HealthController` (capability), `CaseEvidenceStore` (rechazo de 0 B), `AgentErrorCodes`, `Models/AgentModels.cs` (`RecordingOptions`), `appsettings.json`, `Program.cs` (registros). Tests xUnit. |
| **client** `client/` | **sí** | `lib/agent.ts`, `lib/agent-messages.ts`, `lib/black-image.ts` (nuevo), `hooks/useRecording.ts`, `hooks/useEvidenceNotices.ts` (nuevo), `app/dashboard/page.tsx`, `components/CaptureStep.tsx`, `components/VideoCard.tsx`, `components/capture/EvidenceTray.tsx`, `components/dashboard/GenerateStep.tsx`, `types/index.ts`. |
| **agent-ui** `agent-ui/` | **no** | Los campos nuevos de `/agent/state` son aditivos; `agent-ui/src/main/agent-api.ts` los ignora. `operations` no cambia de forma. |
| **Mongo** | **no** | Nada. |

---

## 3. Diagnóstico de C2a (Duda 1 A)

### 3.1 Lo que ya midió el arquitecto (2026-10-13, 12:21–12:32)

Entorno: esta Mac (arm64), **scrcpy 4.0** de Homebrew (`/opt/homebrew/bin/scrcpy`, Mach-O directo, sin
wrapper), adb `37.0.0` de platform-tools exportado en `ADB`, **Samsung SM-M127F, Android 11, serial
`R58RA3R5AGA`**, pantalla encendida y sin keyguard. scrcpy se lanzó desde un script Python (`§3.4`) con stdout y
stderr en un pipe, como hace Tatana. Se usaron los mismos flags que `AdbService` (`--record … --record-format mkv
--max-size 1080 --video-bit-rate 4M`), salvo los que indica la tabla. Los archivos quedaron en el scratchpad de la
sesión, no en `agent-data`. Al terminar, `stay_on_while_plugged_in` volvió a `0` y no quedó ningún scrcpy vivo.

| # | Modo de ventana | Audio | Señal (a los N s) | ¿Terminó? | Archivo | Duración (ffmpeg) |
|---|---|---|---|---|---|---|
| t1 | `--no-video-playback` | `--audio-source=output` | SIGINT a los 20 s, otra SIGINT y SIGTERM a mano. scrcpy se lanzó con `&` desde un shell no interactivo, así que heredó SIGINT ignorado | **No** (hubo que hacer `kill -9`) | **0 B** | — |
| t2 | `--no-video-playback` | `--no-audio` | SIGINT a los 20 s, con espera de 30 s | **No** → kill | **0 B** | — |
| t4 | `--no-video-playback` | `--no-audio` | SIGINT a los 20 s (con cambios de pantalla entre los 3 y los 5 s) | **No** → kill | 262 144 B | **N/A** |
| t5 | `--no-video-playback` | `--no-audio` | **SIGTERM** a los 12 s | **No** → kill | 262 144 B | **N/A** |
| t6 | **`--no-window`** | `--no-audio` | SIGINT a los 12,2 s | **Sí**, exit 0 en **0,13 s** | 402 035 B | **11,28 s** |
| t7 | **`--no-window`** | `--audio-source=output`, `--stay-awake`, `--verbosity=debug` | SIGINT a los 15,0 s | **Sí**, exit 0 en **0,17 s** | 35 526 B | **13,98 s** (video h264 + opus) |
| t8 | **`--no-window`** | `--no-audio` | **SIGTERM** a los 8,1 s | **Sí**, exit 0 en 0,13 s | 28 721 B | 6,85 s |
| t9 | **`--no-window`** | `--no-audio` | SIGINT, con SIGINT **ignorado heredado** del padre | **No** → kill | **0 B** | — |

Otras observaciones:
- **Salida de scrcpy.** Las líneas del **cliente** (`INFO: Recording started to matroska file: …`,
  `INFO: Recording complete …`) llegan **recién al terminar**, porque stdout va a un pipe con buffer completo. Las
  del **servidor** (`[server] INFO: Device: [samsung] samsung SM-M127F (Android 11)` a los 0,71–0,73 s,
  `[server] DEBUG: Display: using SurfaceControl API` a los 0,96–0,99 s) llegan **en tiempo real**.
- **El MKV no crece en disco mientras se graba.** Con poco movimiento queda en 0 B o en 262 144 B (bloques de
  256 KiB) hasta el cierre. Si scrcpy se cierra limpio, el buffer se escribe. Si se lo mata, se pierde.
- **`--stay-awake` funciona con `--no-window`.** `stay_on_while_plugged_in` pasó de 0 a 7 durante la grabación y
  volvió a 0 al cerrar limpio.
- **Con pantalla estática, el cierre limpio igual da la duración completa.** En t7 la pantalla casi no cambió y el
  archivo dura 13,98 s de 15,0 s de reloj, que es lo que falta hasta el `Display: using` (~1 s). La duración medida
  desde el marcador coincide con el archivo con una diferencia de ~0,05 s.

### 3.2 Conclusión: causa raíz de C2a (y del 0 B de la grabación huérfana)

1. **H1, confirmada: con `--no-video-playback`, scrcpy 4.0 ignora SIGINT y SIGTERM** (en macOS, que es lo
   medido). Por eso Tatana **siempre** termina en kill (el log del QA dice "no respondió a la señal; se forzó el
   cierre"). El kill pierde el buffer del muxer. Con una pantalla de poco movimiento, el bitrate real es de
   ~20–30 kb/s, así que 256 KiB de buffer son **~70–150 s** de video. Eso explica los ~2 min 30 s que faltan al
   final, y el 0 B de una grabación corta o estática.
2. **H1b, confirmada: si Tatana hereda SIGINT ignorado**, porque se lanzó con `&` o desde un shell no interactivo
   como en el QA, la señal tampoco llega con `--no-window`. SIGTERM sí llega. De ahí el escalado (D2).
3. **C2b ("arranca ~10 s tarde")**: no se reprodujo. La captura real arranca ~1 s después de lanzar scrcpy
   (`Display: using …`). El "10 s" del QA probablemente sale de observar el MKV en 0 B o el servidor con la máquina
   en load 18. El marcador de inicio (D3) lo resuelve igual: la UI dice "Grabando" con la hora real.
4. **Pantalla apagada (H3) y `-shortest` (H4)**: no se midieron. `--stay-awake` evita el apagado por timeout
   mientras está enchufado, y `-shortest` se saca por análisis: con la mezcla del mic, recorta el video al stream
   más corto. El implementador los mide en §3.3 (D-3 y D-4).

### 3.3 Diagnóstico que corre el `implementer-backend` (antes y durante la implementación)

Celular: el mismo Samsung (`R58RA3R5AGA`) por USB a esta Mac. **Antes de cada prueba**: `pgrep -fl scrcpy` vacío
(que el Tatana del usuario no esté grabando) y anotar `adb -s R58RA3R5AGA shell settings get global
stay_on_while_plugged_in` y `settings get system screen_off_timeout`. **Al terminar**, verificar que los dos valores
volvieron a lo anotado. Si no, restaurarlos con `settings put`, que es la única escritura permitida en el celular.
Todo archivo va a `$S/diag/` (scratchpad). Interacción con el celular solo con `cmd statusbar
expand-notifications`/`collapse` y `input keyevent KEYCODE_SLEEP`/`KEYCODE_WAKEUP`. No se abren apps ni se
tocan datos.

| Paso | Qué mide | Cómo | Resultado esperado | Rama si no da lo esperado |
|---|---|---|---|---|
| **D-1** Señal por el camino de Tatana | Que el `Process` de .NET + `ProcessStop` cierren limpio con los flags nuevos | Después de B4–B6 (§10.1): Tatana dev en `--port 8766 --data $S/diag-data`, lanzado **desde una terminal interactiva**. `record/start` → 30 s → `record/stop` con `curl` | Log "scrcpy detenido limpiamente (exit 0)", stop en < 2 s, `validation.status = ok` | Si no cierra: probar `WindowMode = no_video_playback` y la versión 4.1 (`scrcpy --version`), y reportar `blocked` con el log |
| **D-2** SIGINT ignorado heredado | El escalado a SIGTERM (D2) | Igual que D-1, pero Tatana lanzado con `nohup dotnet run … &` desde un `bash -c` no interactivo | Log de escalado "SIGINT sin efecto; se envía SIGTERM" y cierre limpio a los ~3 s, sin kill | Si SIGTERM tampoco funciona: dejar el kill + remux (D2 paso 4) y anotarlo; la validación marca lo que falte |
| **D-3** Pantalla apagada a mitad de la grabación | Si una pantalla que se apaga con el botón (no con el timeout, que `--stay-awake` ya cubre) acorta el archivo | 3 min: `KEYCODE_SLEEP` al minuto 1 y `KEYCODE_WAKEUP` al minuto 2 → stop | `ok` (scrcpy repite el último cuadro o sigue el reloj) **o** `incomplete` correctamente detectado | No se agrega código: lo cubre la validación. Si el archivo sale `ok` pero el tramo apagado es negro, se anota (la detección de cuadros negros dentro del video está fuera de alcance) |
| **D-4** Mic de la PC sin `-shortest` | Que el audio no recorte el video | 2 min con `android_with_mic: true` (requiere permiso de micrófono para la terminal: si macOS lo niega, se anota y queda para el usuario) | La duración del MKV final es ≥ la del video antes de la mezcla − 1 s | Si recorta: revisar el `-map` de B9 |
| **D-5** Tiempo de cierre (flush) vs. duración | Si 30 s fijos alcanzan (Duda 2 A) | Medir el tiempo entre la señal y la salida en grabaciones de 1, 5 y **12 min** (la de 12 min es §11.2) | < 5 s en todas | Si en 12 min supera 10 s: cambiar a timeout proporcional `min(60, 10 + 2 × minutos)` (variante que la Duda 2 A permite) y anotarlo |
| **D-6** Cierre forzado | Cuánto se pierde con kill y que el celular quede bien | 1 min, con Tatana configurado `StopTimeoutSeconds = 1` y lanzado como en D-2 pero con `SigtermAfterSeconds = 0` (sin escalado) para forzar el kill | Remux, validación `incomplete` o `empty`, y `stay_on_while_plugged_in` restaurado por el servidor de scrcpy | Si el valor no se restaura: Tatana lo restaura (guarda el valor previo con `settings get` antes del start y lo repone después de un kill). Se anota como cambio |
| **D-7** Marcador de inicio por `Process` | Que `OutputDataReceived` reciba la línea `[server] INFO: Device:` en tiempo real | Log en Debug de cada línea con su hora relativa durante D-1 | La línea llega < 2 s después del lanzamiento | Si no llega por el pipe de .NET: probar `--verbosity=debug` y el marcador `Display: using`. Si nada llega en tiempo real, `RequireStartMarker = false` por defecto y reportarlo |

Los resultados (tabla con los números) van a `progress/impl_backend_grabacion-android-robusta.md`, sección
"Diagnóstico".

### 3.4 Script de prueba (referencia para D-3/D-6 sin Tatana)

El arquitecto usó este script (fuera del repo, en el scratchpad). El implementador lo puede recrear en `$S/tools/`
(nunca dentro de `$S/diag/`) y correrlo con `python3 -I`:

```python
import subprocess, sys, time, signal, threading, os
# uso: scrcpy_probe.py <out.mkv> <segundos> <espera_max> -- <flags extra de scrcpy>   (SIG=SIGTERM para cambiar la señal)
out, secs, wait_max = sys.argv[1], float(sys.argv[2]), float(sys.argv[3])
extra = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
env = dict(os.environ, ADB='/Users/joelmiguelserrudo/Library/Android/sdk/platform-tools/adb')
args = ['scrcpy','--serial','R58RA3R5AGA','--record',out,'--record-format','mkv','--max-size','1080','--video-bit-rate','4M'] + extra
t0 = time.time()
p = subprocess.Popen(args, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=env, text=True, bufsize=1,
    preexec_fn=lambda: (signal.signal(signal.SIGINT, signal.SIG_DFL), signal.signal(signal.SIGTERM, signal.SIG_DFL)))
threading.Thread(target=lambda: [print(f"[{time.time()-t0:7.2f}] {l.rstrip()}", flush=True) for l in p.stdout], daemon=True).start()
while time.time() < t0 + secs and p.poll() is None: time.sleep(0.2)
ts = time.time(); p.send_signal(getattr(signal, os.environ.get("SIG", "SIGINT")))
try: p.wait(timeout=wait_max); forced = False
except subprocess.TimeoutExpired: p.kill(); p.wait(); forced = True
print(f">>> exit={p.returncode} forced={forced} flush={time.time()-ts:.2f}s wall={ts-t0:.2f}s size={os.path.getsize(out) if os.path.exists(out) else -1}")
r = subprocess.run(['ffmpeg','-hide_banner','-i',out], capture_output=True, text=True)
print('\n'.join(l.strip() for l in r.stderr.splitlines() if 'Duration' in l or 'Stream' in l))
```

---

## 4. Modelo de datos

No hay colecciones ni DTOs nuevos en `Factum.Backend`. No cambia nada en Mongo.

**Disco de Tatana (`DataDirectory`):**

| Ruta | Quién la escribe | Ciclo de vida |
|---|---|---|
| `<DataDirectory>/grabacion_<ts>.mkv` | scrcpy (igual que hoy) | Si la validación da `ok` o `incomplete`, el cliente la mueve a `cases/<id>/`. |
| `<DataDirectory>/grabacion_<ts>.mkv.mic.mka` | `MicCapture` (igual que hoy) | Se borra después de una mezcla exitosa (igual que hoy). Si la grabación termina `empty`, se borra. Si termina `unreadable`, se mueve a `descartes/` junto con el video. |
| `<DataDirectory>/descartes/<nombre>` (**nuevo**) | `AdbService` (validación `unreadable`) | Nunca se borra solo, y nunca es evidencia del caso (Duda 4 A). `FileStorageService.ListFiles` lista solo el primer nivel, así que no aparece en `GET /files`. Si ya existe un archivo con el mismo nombre, se agrega el sufijo `_1`, `_2`… |
| Un MKV de **0 B** | scrcpy | Se **borra** al validar (`empty`). Solo el archivo de este stop, por su ruta exacta. |

**Estado en memoria (Tatana):**
- `AdbService` mantiene un `RecordingSnapshot` inmutable (§5.4), reemplazado de forma atómica (`Volatile.Write`).
  Lo leen `/agent/state` y `ActiveOperations` sin tomar `_recordLock`.
- `IosService` suma `CaseId` a `IosSession` y expone su snapshot de la misma forma.
- `RecordingRegistry` (singleton nuevo) guarda las grabaciones **detenidas** con validación `ok`/`incomplete`
  (`unclaimed_recordings`, §6.5). Se listan mientras el archivo siga en la raíz (`File.Exists`) y tengan menos de
  **24 h**. Guarda como máximo **20** entradas y descarta las más viejas. No se persiste: si Tatana se reinicia, se
  pierde la lista, pero el archivo sigue en la raíz.

---

## 5. Diseño técnico: Tatana (`server/src/Factum.Agent`)

### 5.1 Config (`Models/AgentModels.cs` + `appsettings.json`)

`AgentOptions` suma:

```csharp
public RecordingOptions Recording { get; set; } = new();
/// Solo con Mock = true: video que se copia como "grabación" Android al detener. Solo por env/CLI/appsettings.Local.json.
public string? MockAndroidRecordingSource { get; set; }
/// Solo con Mock = true: fuerza el resultado de la validación para probar la UI: "ok" | "incomplete" | "empty" | "unreadable".
public string? MockAndroidRecordingOutcome { get; set; }
```

`RecordingOptions` (nuevo, en `Models/AgentModels.cs`), config `Agent:Recording:*`:

| Propiedad | Tipo | Default | Uso |
|---|---|---|---|
| `StartTimeoutSeconds` | int | `20` | Tiempo máximo para ver el marcador de inicio (D3). |
| `RequireStartMarker` | bool | `true` | `false` vuelve al criterio de hoy: "vivo a los 1,5 s" = grabando, con el inicio = lanzamiento + 1 s. Es una salida de emergencia si en Windows no llega el marcador (§11.4, paso 6). |
| `StopTimeoutSeconds` | int | `30` | Espera total desde la señal hasta el kill (Duda 2 A). |
| `SigtermAfterSeconds` | int | `3` | Solo macOS/Linux: si scrcpy sigue vivo después de este tiempo desde la SIGINT, se manda SIGTERM. `0` = sin escalado. |
| `StayAwake` | bool | `true` | Agrega `--stay-awake`. |
| `WindowMode` | string | `"no_window"` | `"no_window"` → `--no-window`. `"no_video_playback"` → `--no-video-playback` (lo de hoy, solo para volver atrás). |
| `ToleranceMinSeconds` | double | `3` | Tolerancia = max(min, recorded × percent / 100) (Duda 3 A). |
| `TolerancePercent` | double | `2` | |

`RecordingOptions.Normalize(ILogger)` se llama en el constructor de `AdbService`. Funciona como
`VideoPostprocessOptions.Normalize`: un valor inválido (≤ 0 en los tiempos, salvo `SigtermAfterSeconds` que acepta 0;
porcentaje < 0 o > 50; `WindowMode` desconocido; `SigtermAfterSeconds ≥ StopTimeoutSeconds`) **no impide arrancar**.
Loguea un warning y usa el default de **ese** campo.

`appsettings.json` suma la sección `"Recording"` con los defaults, como documentación. Los dos `MockAndroid*` **no**
van ahí.

### 5.2 `Common/ProcessStop.cs` (escalado)

La firma suma un parámetro opcional al final:
`StopGracefullyAsync(Process p, TimeSpan timeout, ILogger log, string name, CancellationToken ct = default, TimeSpan? escalateAfter = null)`.

- **macOS/Linux:** `kill -2`. Si `escalateAfter` tiene valor > 0 y el proceso sigue vivo después de ese tiempo,
  manda `kill -15` y loguea a nivel warning "{Name}: SIGINT sin efecto; se envía SIGTERM". Después espera **hasta
  completar `timeout` contado desde la primera señal**. Si no terminó, hace `Kill(entireProcessTree: true)` como hoy.
- **Windows:** sin cambios (Ctrl+C por el auxiliar). `escalateAfter` se ignora.
- Valor de retorno sin cambios: `true` = terminó con señal (SIGINT o SIGTERM), `false` = kill.
- Los otros llamadores (si hay) no pasan `escalateAfter` → mismo comportamiento que hoy.

### 5.3 `Common/FfmpegProbe.cs` (nuevo)

```csharp
internal sealed record FfmpegProbeResult(double? DurationSeconds, bool HasVideo);
internal static class FfmpegProbe
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    public static async Task<FfmpegProbeResult> ProbeAsync(string ffmpeg, string path, CancellationToken ct);
    internal static FfmpegProbeResult Parse(string? stderr);   // puro, testeable
}
```

- Corre `ffmpeg -hide_banner -i <path>` con `ProcessRunner.RunArgumentListAsync(..., timeout: DefaultTimeout)`. El
  código de salida ≠ 0 es esperable, porque no hay salida.
- `Parse`: la duración sale de `VideoPostprocessService.ParseFfmpegDuration` (se **mueve** acá y
  `VideoPostprocessService.ParseFfmpegDuration` queda como reenvío `=> FfmpegProbe.ParseDuration(...)`, así los tests
  existentes siguen compilando). `HasVideo` = alguna línea que matchee `Stream #\d+:\d+.*: Video:`.
- `VideoPostprocessService.ProbeDurationAsync` puede seguir como está. No hace falta migrarlo.

### 5.4 `Services/AdbService.cs`

**Estado observable (nuevo):**

```csharp
internal enum RecordingPhase { Starting, Recording, Stopping }
internal sealed record RecordingSnapshot(string Serial, string OutputPath, string? CaseId, bool WithMic,
    RecordingPhase Phase, DateTimeOffset RequestedAt, DateTimeOffset? StartedAt, Process? Process);
private RecordingSnapshot? _snap;   // se lee con Volatile.Read, se escribe con Volatile.Write
```

- `ActiveOperations`: devuelve `recording_android` (con `since = StartedAt ?? RequestedAt`) si `_snap` no es null y
  `Phase` es `Starting` o `Stopping`, o si es `Recording` y el proceso está vivo (en mock, siempre). Si el proceso
  murió solo y la fase es `Recording`, no cuenta, igual que hoy.
- Interfaz nueva `IRecordingStateSource { IEnumerable<RecordingStateDto> ActiveRecordings { get; } }`, en
  `Services/RecordingRegistry.cs`. La implementan `AdbService` e `IosService`, y se registra en `Program.cs` como
  `IOperationSource`. `ActiveRecordings` aplica el mismo filtro que `ActiveOperations`.
- `_recordingProcess`, `_recordingPath` y `_recordingSince` se reemplazan por `_snap`. `_mic` y `_scrcpyTail` quedan.

**`IAdbService`** (firmas nuevas):

```csharp
Task<AndroidStartResult> StartRecordingAsync(string serial, string outputPath, int androidVersion, bool withMic, string? caseId, CancellationToken ct = default);
Task<AndroidStopResult> StopRecordingAsync(string serial, CancellationToken ct = default);
public sealed record AndroidStartResult(DateTimeOffset StartedAt);
public sealed record AndroidStopResult(string OutputPath, string Serial, string? CaseId, RecordingValidationDto Validation);
```

Los errores tipados se lanzan como `AgentHttpException` (la clase de `CaseEvidenceStore.cs`, con status, code y
`extra`). Así el controlador los traduce sin `if`s.

**Start:**
1. `_recordLock` como hoy. `already_recording` (409, texto actual "Ya hay una grabación de Android en curso.") si
   `_snap` está en `Starting`/`Stopping`, o en `Recording` con el proceso vivo.
2. `DiscardStaleRecordingAsync` como hoy (limpia el snapshot si scrcpy terminó solo).
3. Argumentos: `--serial <s> --record <path> --record-format mkv <ventana> --max-size 1080 --video-bit-rate 4M
   <audioArg>`, más `--stay-awake` si `StayAwake`. `<ventana>` = `--no-window` o `--no-video-playback` según
   `WindowMode`. **No** se agrega `--verbosity`: el marcador por defecto es una línea INFO.
4. `_snap = new(serial, path, caseId, withMic, Starting, now, null, proc)` apenas `Process.Start` vuelve.
5. `OnLine` agrega: (a) si la línea matchea el marcador (`ScrcpyOutput.IsStartMarker`, §5.6) y todavía no se vio,
   guarda `markerAt = DateTimeOffset.UtcNow` y completa un `TaskCompletionSource`; (b) **no** mete en `LineTail`
   las líneas que empiezan con `DEBUG:` o `[server] DEBUG:` (se loguean igual), para que no desplacen a las de
   error. `LineTail` pasa de 20 a 40 líneas.
6. Espera: `Task.WhenAny(markerTcs.Task, proc.WaitForExitAsync(), Task.Delay(StartTimeout))`.
   - Proceso terminado → error como hoy ("scrcpy no pudo iniciar la grabación: …", 500 sin code), y borra el archivo
     de este intento.
   - Marcador visto → espera además `ScrcpyEarlyExitWindow` (1,5 s). Si muere en ese lapso, es el mismo error de
     arriba. Si no, `StartedAt = markerAt`.
   - Timeout sin marcador:
     - `RequireStartMarker = true` → `ProcessStop.StopGracefullyAsync(proc, 5 s, …, escalateAfter: 2 s)`, borra
       **solo** `outputPath` (y el `.mic.mka` si existiera) y lanza 504 `recording_start_timeout` con el texto "El
       celular no empezó a grabar a tiempo. Desbloquealo, dejalo con la pantalla encendida y volvé a intentar." más el
       resumen de scrcpy si hay.
     - `RequireStartMarker = false` → `StartedAt = RequestedAt + 1 s` y se loguea un warning "sin marcador de
       inicio".
7. Si `withMic`: `MicCapture.StartAsync` **después** del marcador, así el audio queda alineado con el inicio real.
   Si falla, el flujo de hoy (detener scrcpy, borrar, relanzar el error).
8. `_snap = _snap with { Phase = Recording, StartedAt = … }` y devuelve `AndroidStartResult(StartedAt)`.
9. Todo el start usa `CancellationToken.None` para las esperas internas (DA4): si el navegador recarga mientras dice
   "Iniciando…", la grabación igual arranca y se recupera por `/agent/state`.

**Mock (start):** `Task.Delay(1000)` (así se ve "Iniciando…"), `_snap` en `Recording` con `StartedAt = now`.

**Stop** (todo con `CancellationToken.None` adentro, DA4):
1. `_recordLock`. Sin `_snap` → `RecordingNotActiveException` (409 `not_recording`, §5.8). `stopRequestedAt = now`.
   `_snap = _snap with { Phase = Stopping }`.
2. `forced = !await ProcessStop.StopGracefullyAsync(proc, StopTimeout, _log, "scrcpy", CancellationToken.None,
   escalateAfter: SigtermAfterSeconds > 0 ? … : null)`. Si el proceso ya había terminado solo, devuelve `true`
   como hoy.
3. Se detiene el mic (siempre, como hoy).
4. Si `forced` y el archivo existe con tamaño > 0 → `RemuxAsync` (sin cambios).
5. **Validación del video** (antes de la mezcla, así el audio no infla la duración): `size`, y si `size > 0`,
   `FfmpegProbe.ProbeAsync` → `RecordingValidation.Evaluate(size, probe, recordedSeconds, opts)` (§5.5), con
   `recordedSeconds = (stopRequestedAt − StartedAt).TotalSeconds`.
6. Si `status` es `ok` o `incomplete` y hay mic: `MuxMicAudioIntoVideoAsync` **sin `-shortest`**, con
   `-map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -b:a 128k` (D7). Antes de reemplazar el original, `ProbeAsync` del
   temporal: si no tiene video o su duración es menor que la del video sin audio − 1 s, se descarta el temporal, se
   queda el video sin mic y se loguea un warning "la mezcla del micrófono recortaba el video; se conserva el video
   sin el audio del mic". Los argumentos se arman en una función pura `BuildMicMuxArgs(video, audio, tmp)` para
   testearla.
7. Según el `status`:
   - `empty` → borra `outPath` (si existe) y el `.mic.mka`, y lanza 422 `recording_empty` con el texto "La grabación
     no se guardó: el archivo quedó vacío. Volvé a grabar." más " Detalle de scrcpy: …" si `ScrcpyErrorSummary`
     tiene algo. `extra = { validation }`.
   - `unreadable` → mueve `outPath` (y el `.mic.mka` si queda) a `<DataDirectory>/descartes/` (§4) y lanza 422
     `recording_unreadable` con el texto "La grabación no se guardó: el archivo no se puede leer como video. Tatana
     guardó una copia en la carpeta «descartes» para revisión. Volvé a grabar." `extra = { validation }`.
   - `ok` / `incomplete` → devuelve `AndroidStopResult`.
8. `finally`: `_snap = null` y libera `_recordLock`. El `Process` se dispone.
9. Log de una línea al final: serial, `forced`, `escalated`, `recorded_seconds`, `duration_seconds`, `status`,
   tamaño y tiempo total del stop.

**Mock (stop):** `recordedSeconds` igual que con el real.
- Si `MockAndroidRecordingSource` existe → `File.Copy` al `outPath`.
- Si no, y hay ffmpeg → genera el MKV con
  `ffmpeg -f lavfi -i testsrc2=size=360x640:rate=5 -t <recordedSeconds, mínimo 1> -c:v libx264 -preset ultrafast -pix_fmt yuv420p <outPath>`.
- Si no hay ffmpeg → escribe un MKV mínimo embebido (bytes en base64 en el código, ≤ 4 KB, generado una vez con
  ffmpeg). En ese caso la validación sale `ok` con `duration_checked: false`.
- `MockAndroidRecordingOutcome` (si está) **pisa** el `status` final: `empty` borra el archivo y lanza 422,
  `unreadable` escribe 1 KB de bytes al azar y sigue el camino real, e `incomplete` fuerza el `status` con
  `duration_seconds = recorded / 2`.
- **Nunca** escribe un archivo de 0 B como grabación.

### 5.5 `Services/RecordingValidation.cs` (nuevo, puro)

```csharp
public static class RecordingValidationStatus { public const string Ok = "ok", Incomplete = "incomplete", Empty = "empty", Unreadable = "unreadable"; }

public sealed record RecordingValidationDto(string Status, double RecordedSeconds, double? DurationSeconds,
    bool DurationChecked, bool ForcedStop, DateTimeOffset StartedAt, DateTimeOffset StoppedAt);

internal static class RecordingValidation
{
    internal static double Tolerance(double recordedSeconds, RecordingOptions o);   // max(o.ToleranceMinSeconds, recorded * o.TolerancePercent / 100)
    internal static (string Status, double? Duration, bool Checked) Evaluate(long size, FfmpegProbeResult? probe, double recordedSeconds, RecordingOptions o);
}
```

Reglas de `Evaluate`, en orden:
1. `size <= 0` → `empty`.
2. `probe is null` (no hay ffmpeg) → `ok` con `Checked = false` y `Duration = null` (DA6).
3. `probe.DurationSeconds is null` o `!probe.HasVideo` → `unreadable`.
4. `recorded = max(0, recordedSeconds)`. Si `duration >= recorded − Tolerance(recorded)` → `ok`; si no,
   `incomplete`. Una duración **mayor** que lo grabado es `ok`.

`RecordedSeconds` y `DurationSeconds` se redondean a 1 decimal al armar el DTO.

### 5.6 `Common/ScrcpyOutput.cs` (nuevo, puro)

```csharp
internal static class ScrcpyOutput
{
    /// true si la línea indica que el celular empezó a capturar.
    internal static bool IsStartMarker(string line);
}
```

Matchea (ordinal, después de `Trim()`):
- `[server] INFO: Device:` (scrcpy 2.x–4.x, medido en 4.0, §3.1), o
- `Recording started to ` en cualquier posición (si algún día la salida del cliente llega sin buffer), o
- `[server] DEBUG: Display: using` (por si se activa debug).

### 5.7 `Services/RecordingRegistry.cs` (nuevo)

```csharp
public sealed record RecordingStateDto(string Platform, string Serial, string State, DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt, string? Filename, string? CaseId, bool? AndroidWithMic, string? IosMode);
public sealed record UnclaimedRecordingDto(string Platform, string Serial, string Filename, string? CaseId,
    DateTimeOffset StoppedAt, RecordingValidationDto? Validation);

public interface IRecordingStateSource { IEnumerable<RecordingStateDto> ActiveRecordings { get; } }

public sealed class RecordingRegistry(IOptions<AgentOptions> opts, TimeProvider time)
{
    public void AddStopped(UnclaimedRecordingDto r);            // dedup por Filename
    public IReadOnlyList<UnclaimedRecordingDto> Unclaimed();      // File.Exists(<DataDirectory>/<Filename>) && edad < 24 h; máx. 20, los más nuevos primero
}
```

`State` ∈ `starting` | `recording` | `stopping`. Para iOS siempre es `recording` (su start bloquea hasta grabar)
y pasa a `stopping` mientras corre `StopRecordingAsync`.

### 5.8 `Controllers/RecordingController.cs`

- `StartRecordingRequest` suma `string? CaseId = null` (JSON `case_id`). Si viene, se valida con
  `AgentFileNames.TryParseCaseId` y se guarda en forma canónica. Si es inválido → 400 `invalid_case_id`
  (`AgentErrorCodes.InvalidCaseId`, ya existe).
- **Android start** → `Ok(new { filename, state = "recording", started_at, case_id })`. Evento `recording_started`
  con `data = { serial, platform = "android", case_id, started_at, filename }`.
- **iOS start**: `ios.StartRecordingAsync(serial, mode, path, caseId, ct)` (la firma suma `caseId`) →
  `Ok(new { filename, state = "recording", started_at, case_id })`, con el mismo evento.
- **Stop** (los dos): la llamada al servicio usa `CancellationToken.None` (DA4).
  - 200 Android: `{ filename, url, variants_expected = false, variants = [], platform = "android", serial,
    case_id, validation }`.
  - 200 iOS: igual que hoy más `platform = "ios"`, `serial`, `case_id`. `validation` se omite (null).
  - `recording_stopped` lleva los mismos campos, más `error` y `code` en `null`.
  - Si la validación es `ok` o `incomplete`: `registry.AddStopped(...)`.
- **Errores**, de más específico a más genérico:
  - `AgentHttpException` → `StatusCode(ex.Status, ex.ToBody())`. Si es `recording_empty`/`recording_unreadable`,
    **además** se emite `recording_stopped` con `filename = null, url = null, variants_expected = false,
    variants = [], platform, serial, case_id, validation, error = ex.Message, code = ex.Code`. Así las otras
    pestañas salen de "Grabando" (un cliente viejo hace `if (d.filename)` y no agrega nada).
  - `RecordingNotActiveException` → 409 `{ error, code: "not_recording" }` (alineado con la HU #60, §12).
  - `IosException` y `Exception` → igual que hoy (500).
- `already_recording` (409) y `recording_start_timeout` (504) salen como `AgentHttpException` desde el servicio.

### 5.9 `Services/IosService.cs`

- `IosSession` suma `string? CaseId`, y `StartRecordingAsync` el parámetro `caseId`.
- Implementa `IRecordingStateSource`: si `_session` no es null, `{ platform: "ios", serial: udid, state:
  "recording" | "stopping", requested_at = started_at = session.Started, filename = Path.GetFileName(OutputPath),
  case_id, ios_mode = session.Mode }`. Para eso guarda `Udid` en la sesión y un flag `Stopping` que se activa al
  entrar a `StopRecordingAsync`. **Ojo:** hoy `_session = null` se hace al principio del stop. Se cambia a: marcar
  `Stopping` al principio y poner `_session = null` en un `finally` al final.
- `IosStopResult` suma `string? CaseId` y `string Serial`.
- Nada más cambia en iOS (las validaciones de C2 son solo de Android).

### 5.10 `Controllers/AgentStateController.cs`

`AgentStateResponse` (record posicional) suma al final `IReadOnlyList<RecordingStateDto> Recordings` y
`IReadOnlyList<UnclaimedRecordingDto> UnclaimedRecordings`. El controlador inyecta
`IEnumerable<IRecordingStateSource>` y `RecordingRegistry`. `operations` y `background_operations` no cambian.

### 5.11 `Services/CaseEvidenceStore.cs` (Duda 4 A, defensa extra)

- `ImportAsync`: si `source` existe y `new FileInfo(source).Length == 0` → 422 `empty_evidence` **sin mover**, con
  el texto "{name} está vacío (0 bytes) y no se guarda como evidencia." Si `source` no existe pero `dest` existe con
  0 B (reimportación), el mismo error. No se borra nada.
- `CheckUpload` (y por lo tanto `StageAndCommitUploadAsync`): `size == 0` → el mismo 422 `empty_evidence`.
- `AgentErrorCodes` suma `EmptyEvidence = "empty_evidence"`, `AlreadyRecording = "already_recording"`,
  `RecordingStartTimeout = "recording_start_timeout"`, `RecordingEmpty = "recording_empty"`,
  `RecordingUnreadable = "recording_unreadable"` y, si la HU #60 no se mergeó antes, `NotRecording =
  "not_recording"` (§12).

### 5.12 `HealthController` y `Program.cs`

- `Capabilities` suma `"recording_state_v1"`, que indica todo lo de §6 (`case_id` en el start, `started_at`,
  `validation`, `recordings`/`unclaimed_recordings`, los códigos nuevos y `empty_evidence`).
- `Program.cs`: `AddSingleton<RecordingRegistry>()`,
  `AddSingleton<IRecordingStateSource>(sp => sp.GetRequiredService<AdbService>())` y lo mismo con `IosService`.

---

## 6. Endpoints y mensajes WebSocket de Tatana

**Serialización verificada** (igual que en la SDD `ios-grabacion-larga` §4):
- HTTP (`Program.cs:98-103`): `SnakeCaseLower` y `WhenWritingNull`. Los `null` **se omiten**.
- WebSocket (`AgentWebSocketHub.JsonOpts`): `SnakeCaseLower` sin `WhenWritingNull`. Los `null` **viajan como
  `null`**. El cliente trata `null` y "ausente" igual.
- Los records PascalCase de §5 se serializan con la política. Abajo figura el nombre **tal como viaja**.

### 6.1 `POST /devices/{serial}/record/start`

Request (cambia, campo nuevo opcional):

```json
{ "android_version": 11, "platform": "android", "android_with_mic": false, "case_id": "ba916e4f-0708-4799-91ff-a83ac0f9a2e8" }
```

Respuesta 200 (Android). **Vuelve recién cuando el celular empezó a capturar**, hasta `StartTimeoutSeconds`:

```json
{ "filename": "grabacion_20261013_110421_000.mkv", "state": "recording",
  "started_at": "2026-10-13T14:04:22.731+00:00", "case_id": "ba916e4f-…" }
```

iOS: los mismos campos (`filename` es nuevo en iOS). `case_id` se omite si no vino.

Errores nuevos: 409 `already_recording`, 504 `recording_start_timeout` y 400 `invalid_case_id`. El cuerpo es
`{ error, code }`, como el resto de Tatana. Los demás 500 no cambian (sin `code`).

### 6.2 `POST /devices/{serial}/record/stop[?platform=ios]`

200 Android:

```json
{ "filename": "grabacion_….mkv", "url": "/files/grabacion_….mkv",
  "variants_expected": false, "variants": [],
  "platform": "android", "serial": "R58RA3R5AGA", "case_id": "ba916e4f-…",
  "validation": { "status": "incomplete", "recorded_seconds": 686.0, "duration_seconds": 550.5,
                  "duration_checked": true, "forced_stop": true,
                  "started_at": "2026-10-13T14:04:22.731+00:00", "stopped_at": "2026-10-13T14:15:48.731+00:00" } }
```

200 iOS: lo de hoy (`filename`, `url`, `variants_expected`, `variants`) más `platform`, `serial` y `case_id`, sin
`validation`.

422 (solo Android):

```json
{ "error": "La grabación no se guardó: el archivo quedó vacío. Volvé a grabar.", "code": "recording_empty",
  "validation": { "status": "empty", "recorded_seconds": 41.2, "duration_checked": false, "forced_stop": true,
                  "started_at": "…", "stopped_at": "…" } }
```

`code` ∈ `recording_empty` | `recording_unreadable`. 409 `not_recording` si no hay grabación.

### 6.3 Evento WS `recording_started` (cambia)

```json
{ "type": "recording_started", "timestamp": "…",
  "data": { "serial": "R58RA3R5AGA", "platform": "android", "case_id": "ba916e4f-…",
            "started_at": "2026-10-13T14:04:22.731+00:00", "filename": "grabacion_….mkv" } }
```

`case_id` puede ser `null`.

### 6.4 Evento WS `recording_stopped` (cambia)

Éxito: los campos de §6.2 (200), con `error: null` y `code: null`. iOS: `validation: null`.
Falla (`recording_empty`/`recording_unreadable`):

```json
{ "type": "recording_stopped", "timestamp": "…",
  "data": { "filename": null, "url": null, "platform": "android", "serial": "R58RA3R5AGA", "case_id": "…",
            "variants_expected": false, "variants": [],
            "validation": { "status": "empty", … }, "error": "La grabación no se guardó: …", "code": "recording_empty" } }
```

### 6.5 `GET /agent/state` (cambia, campos nuevos)

```json
{ "version": "…", "mode": "installed", "busy": true, "maintenance": false,
  "operations": [ { "kind": "recording_android", "since": "2026-10-13T14:04:22.731+00:00" } ],
  "background_operations": [],
  "recordings": [
    { "platform": "android", "serial": "R58RA3R5AGA", "state": "recording",
      "requested_at": "2026-10-13T14:04:21.600+00:00", "started_at": "2026-10-13T14:04:22.731+00:00",
      "filename": "grabacion_….mkv", "case_id": "ba916e4f-…", "android_with_mic": false } ],
  "unclaimed_recordings": [
    { "platform": "android", "serial": "R58RA3R5AGA", "filename": "grabacion_….mkv", "case_id": "5bfdf5eb-…",
      "stopped_at": "…", "validation": { "status": "ok", … } } ],
  "local_config": { … } }
```

- `recordings` y `unclaimed_recordings` están **siempre** presentes (pueden ser `[]`).
- `recordings[].state` ∈ `starting` | `recording` | `stopping`. `started_at` se omite en `starting`. `case_id`,
  `android_with_mic` (solo Android) e `ios_mode` (solo iOS) se omiten si son null.
- Una grabación cuyo scrcpy terminó solo (celular desconectado) **no** aparece (escenario "terminó mientras la
  página estaba cerrada").
- `operations` sigue igual: `agent-ui` no se entera de nada nuevo.
- GET: el middleware de `OperationTracker` no lo cuenta ni lo bloquea en mantenimiento.

### 6.6 `POST /cases/{caseId}/evidence/import` y `GET /cases/{caseId}/evidence/upload-check` / upload

Nuevo 422 `{ "error": "<nombre> está vacío (0 bytes) y no se guarda como evidencia.", "code": "empty_evidence" }`.

### 6.7 `GET /health`

`capabilities` suma `"recording_state_v1"`.

---

## 7. Diseño técnico: `client/`

Principios: (1) la única fuente de verdad sobre si hay una grabación en curso es Tatana (`/agent/state`). El estado
local es optimista y se reconcilia. (2) Nunca se importa en silencio una grabación de otro caso. (3) Con un Tatana
sin `recording_state_v1`, todo se comporta como hoy.

### 7.1 `client/src/lib/agent.ts`

```ts
export type RecordingValidationStatus = "ok" | "incomplete" | "empty" | "unreadable";
export interface RecordingValidation {
  status: RecordingValidationStatus;
  recorded_seconds: number;
  duration_seconds?: number | null;
  duration_checked: boolean;
  forced_stop: boolean;
  started_at: string;
  stopped_at: string;
}
export type RecordingPhase = "starting" | "recording" | "stopping";
export interface RecordingStateEntry {
  platform: "android" | "ios";
  serial: string;
  state: RecordingPhase;
  requested_at: string;
  started_at?: string | null;
  filename?: string | null;
  case_id?: string | null;
  android_with_mic?: boolean | null;
  ios_mode?: IosRecordMode | null;
}
export interface UnclaimedRecording {
  platform: "android" | "ios";
  serial: string;
  filename: string;
  case_id?: string | null;
  stopped_at: string;
  validation?: RecordingValidation | null;
}
export interface AgentRecordingState {
  recordings: RecordingStateEntry[];
  unclaimed_recordings: UnclaimedRecording[];
}
export interface RecordingStartedData {
  serial: string; platform: "android" | "ios"; case_id?: string | null; started_at?: string; filename?: string;
}
```

- `RecordingStoppedData`: `filename` y `url` pasan a `string | null`, y suma `serial?`, `case_id?: string | null`,
  `validation?: RecordingValidation | null`, `error?: string | null` y `code?: AgentErrorCode | null`.
- `AgentErrorCode` suma `"already_recording" | "recording_start_timeout" | "not_recording" | "recording_empty" |
  "recording_unreadable" | "empty_evidence"`. `AgentErrorBody` suma `validation?: RecordingValidation`.
- `startRecording(serial, androidVersion, platform, iosMode?, androidWithMic?, caseId?)`: manda `case_id` si viene.
  Pasa a usar `agentJSON` (lanza `AgentError`) y devuelve
  `{ filename?: string; state?: string; started_at?: string; case_id?: string }`.
- `stopRecording(serial, platform)`: pasa a `agentJSON`. Devuelve
  `RecordingStoppedData & { variants_expected?: boolean; variants?: string[] }`, y en no-2xx lanza `AgentError`
  con `code` y `body.validation`.
- Método nuevo `agentState(): Promise<AgentRecordingState | null>`: `GET /agent/state` con timeout de 3 s. Devuelve
  `null` ante cualquier error, y también si faltan `recordings` o `unclaimed_recordings` (Tatana viejo). Normaliza
  arrays ausentes a `[]` solo cuando la respuesta trae al menos uno de los dos.
- Método nuevo `supportsRecordingState(): Promise<boolean>`: `capabilities?.includes("recording_state_v1")`, con
  cache por instancia de conexión (se invalida en `onConnected`).
- Doc de `AgentHealth`: suma `recording_state_v1`.

### 7.2 `client/src/lib/agent-messages.ts`

`AgentAction` suma `"record"`. Casos nuevos en `agentErrorMessage`. Para todos los códigos de grabación se usa el
`error` de Tatana (ya viene en castellano) si está; si no, estos textos:

| `code` | Texto por defecto |
|---|---|
| `already_recording` | "Ya hay una grabación en curso en esta PC. Detenela antes de empezar otra." |
| `recording_start_timeout` | "El celular no empezó a grabar a tiempo. Desbloquealo, dejalo con la pantalla encendida y volvé a intentar." |
| `not_recording` | "La grabación ya se había detenido." |
| `recording_empty` | "La grabación no se guardó: el archivo quedó vacío. Volvé a grabar." |
| `recording_unreadable` | "La grabación no se guardó: el archivo no se puede leer como video. Volvé a grabar." |
| `empty_evidence` | "{name} está vacío (0 bytes) y no se guarda como evidencia." (siempre el texto local, con `name`) |

### 7.3 `client/src/lib/black-image.ts` (nuevo, Duda 6 A)

```ts
/** true si ≥ 99 % de los píxeles tiene luminancia < 8 (0–255). null si no se pudo leer. */
export async function isMostlyBlack(src: string, signal?: AbortSignal): Promise<boolean | null>;
```

- `fetch(src, { cache: "force-cache", signal })` → `blob` → `createImageBitmap(blob, { resizeWidth: 48,
  resizeHeight: 96, resizeQuality: "low" })` → `OffscreenCanvas` (o `<canvas>` si no hay) → `getImageData`.
- Luminancia `0.2126 R + 0.7152 G + 0.0722 B`. Umbrales en constantes exportadas (`BLACK_LUMA = 8`,
  `BLACK_RATIO = 0.99`).
- **No** se toca `<img>`/`SafeImg` (ni `crossOrigin`): el fetch va aparte. Tatana ya manda
  `Access-Control-Allow-Origin` a los orígenes permitidos, y las URLs `blob:` no lo necesitan.
- Cualquier error → `null` (sin aviso). Función pura aparte para el cálculo,
  `ratioDark(data: Uint8ClampedArray): number`, así es fácil de razonar.

### 7.4 `client/src/hooks/useEvidenceNotices.ts` (nuevo)

Estado de avisos por caso, persistido en `sessionStorage` con la clave `factum:evidence-notices:<caseId>` (DA8):

```ts
export interface EvidenceNotices {
  recordings: Record<string, RecordingValidation>;  // por filename, solo status ok|incomplete
  black: Record<string, boolean>;                   // resultado de isMostlyBlack (en memoria, no se persiste)
  keptBlack: string[];                              // "Conservar"
}
export function useEvidenceNotices(caseId: string | null, files: CapturedFile[]): {
  notices: EvidenceNotices;
  setRecordingValidation(filename: string, v: RecordingValidation): void;
  keepBlack(filename: string): void;
  forget(filename: string): void;                   // al borrar de la bandeja
  summary: { incompleteRecordings: string[]; blackScreenshots: string[] };  // para el paso 5 (sin los "Conservar")
};
```

- Corre `isMostlyBlack` una sola vez por nombre para cada archivo de tipo captura (`kind === "screenshot"` de
  `gallery.ts`). Si el archivo ya está en el caso, la URL es `agent.caseFileURL(caseId, name)`; si no, la de la
  raíz. A lo sumo 2 en paralelo, con `AbortController` al desmontar o cambiar de caso.
- Al cambiar `caseId`, recarga desde `sessionStorage` y limpia `black`.

### 7.5 `client/src/hooks/useRecording.ts`

Firma: `useRecording(addFile, expectVariants, opts: { getCaseId: () => string | null; onValidation:
(filename, v) => void })`. Los getters van por ref para no recrear callbacks.

Estado nuevo:
- `startedAt: string | null` (inicio real, de Tatana). Lo setean el start, `syncFromAgentState` y
  `recording_started`.
- `remotePhase: RecordingPhase | null`: lo que dice Tatana para **esta** grabación. `"starting"` muestra
  "Iniciando…" y `"stopping"` muestra "Deteniendo…", aunque esta pestaña no haya hecho el pedido.
- `foreignRecording: RecordingStateEntry | null`: grabación en curso de otro caso, de origen desconocido o de otro
  serial.
- `foreignStopsRef: Set<string>`: filenames detenidos desde el aviso de "otro caso". Su `recording_stopped` nunca se
  importa en esta pestaña.

Cambios:
- **Start (Android e iOS):** pasa `caseId`. En éxito: `setRecording(true)`, `setStartedAt(res.started_at ?? new
  Date().toISOString())`. En `AgentError` → `onError(agentErrorMessage(e, { action: "record" }))`. Si es
  `already_recording`, además llama a `refresh()` (abajo), que trae el aviso de grabación ajena.
- **Stop (propio):** `stopped = await agent.stopRecording(...)`. Si `stopped.filename` → `addFile`, y si hay
  `validation`, `onValidation(filename, validation)` (y lo mismo con `expectVariants` en iOS). En `AgentError` con
  `recording_empty`/`recording_unreadable` → `onError(texto)`, sin `addFile`. Cualquier otro error, como hoy.
  `finally`: `setRecording(false)`, `setStartedAt(null)`, `setRemotePhase(null)`.
- **`stopForeign()`:** agrega `foreignRecording.filename` a `foreignStopsRef`, llama a
  `agent.stopRecording(foreign.serial, foreign.platform)` y **nunca** llama a `addFile`. Al terminar, `onInfo`
  (texto de §7.7) y `refresh()`. Mientras corre, `loading.stopForeign = true`.
- **`syncFromAgentState(state, ctx: { serial: string | null; caseId: string | null })`** (pura sobre el estado
  recibido):
  - Busca la entrada con `serial === ctx.serial`. Si existe **y** `case_id` coincide con `ctx.caseId`
    (comparación sin distinguir mayúsculas) → es propia: `setRecording(state !== "starting")`,
    `setStartedAt(started_at)`, `setRemotePhase(state)`, `setIosRecordMode(ios_mode)` (iOS),
    `setAndroidWithMic(android_with_mic)` (Android) y `foreignRecording = null`.
  - Si no hay propia, pero hay alguna entrada (otro serial, otro caso o `case_id` ausente) →
    `foreignRecording = esa entrada` (la primera), y `setRecording(false)` solo si esta pestaña no tiene un pedido
    en vuelo (`loading.startRecord`/`stopRecord`).
  - Si no hay ninguna → `setRecording(false)` (salvo pedido en vuelo), `foreignRecording = null`,
    `remotePhase = null` y `startedAt = null`. Esto cubre "terminó mientras la página estaba cerrada": el botón
    vuelve a "Grabar".
  - Devuelve las `unclaimed_recordings` con `case_id` igual a `ctx.caseId`, para que `page.tsx` las importe.
- **`refresh()`**: `if (!(await agent.supportsRecordingState())) return;` →
  `const s = await agent.agentState(); if (s) handleUnclaimed(syncFromAgentState(s, ctx))`. Tiene un guard
  `inFlightRef` y una sola ejecución a la vez. Si se pide otra mientras corre, se encola una sola.
- `resetRecording` también limpia `startedAt`, `remotePhase` y `foreignRecording`.
- Return suma `startedAt, remotePhase, foreignRecording, refresh, stopForeign`.

### 7.6 `client/src/app/dashboard/page.tsx`

- `useEvidenceNotices(currentCaseId, files)`.
- `useRecording(addFile, expectVariants, { getCaseId: () => currentCaseIdRef.current, onValidation:
  setRecordingValidation })`.
- **`recording_started`**: si el Tatana tiene la capability → `void refreshRecording()`. Si no → `setRecording(true)`
  como hoy.
- **`recording_stopped`** (`d: RecordingStoppedData`):
  - `setAirplayName(null)`.
  - `mine = d.case_id ? sameId(d.case_id, currentCaseIdRef.current) : (isRecordingRef.current &&
    !foreignStopsRef.has(d.filename ?? ""))`.
  - Si `d.filename && mine` → `addFile(d.filename)`, `if (d.validation) setRecordingValidation(d.filename,
    d.validation)` y `expectVariants` como hoy (iOS).
  - Si `!d.filename && d.code && mine && !stopInFlight` (falla de una grabación de este caso detenida desde otra
    pestaña) → `setGlobal(d.error ?? texto de §7.2)`.
  - Siempre: `if (capability) void refreshRecording(); else { setRecording(false); }`.
  - Es la regla de "nunca importar en silencio una grabación de otro caso". No cambia nada más del reparto entre
    pestañas, que es de la HU `wizard-concurrencia-pestanas`.
- **`handleAgentConnected`**: `void refreshVariantStatus(); void refreshRecording();` (Duda 9 A).
- **Efecto nuevo**: `step === 3 && currentCase && agentOnline` → `void refreshRecording()` al entrar al paso 3 y al
  cambiar de caso o de dispositivo.
- **`devices_changed`** con el celular seleccionado otra vez presente → `void refreshRecording()`. El resto del
  handler no cambia.
- **Recuperación de `unclaimed`**: por cada entrada con `case_id` de este caso y `filename` que no esté en `files` →
  `addFile(filename)` (el guardado automático la importa), `setRecordingValidation` si trae `validation`, y una vez
  por lote `setStatus`/info "Se recuperó {n} grabación(es) de este caso que se había(n) detenido desde otra pestaña
  o desde otro caso." (banner info, se cierra solo a los 6 s o con la ×).
- **Banner "Retomando inspección"**: la frase "Reconectá el dispositivo para capturar…" se muestra solo si el
  serial del caso (o `selDevice`) **no** está en `devices`. Si está conectado: "El celular está conectado: podés
  seguir capturando." (corrige lo de A2 en el QA).
- `CaptureStep` recibe además `recordingStartedAt={startedAt}`, `recordingPhase={remotePhase}`,
  `recordingRecovered={…}` (true si el estado vino de `syncFromAgentState` y no de un start de esta pestaña),
  `foreignRecording`, `onStopForeign={stopForeign}`, `notices`, `onKeepBlack={keepBlack}`.
  `onRemoveFile` además llama a `forget(name)`.
- `GenerateStep` recibe `reviewNotices={summary}`.

### 7.7 `client/src/components/CaptureStep.tsx`

- **Botón de grabación:**
  - Texto: `loading.stopRecord || recordingPhase === "stopping"` → "Deteniendo…";
    `loading.startRecord || recordingPhase === "starting"` → "Iniciando…"; `isRecording` → "Detener"; si no,
    "Grabar".
  - `recordBtnDisabled` suma `recordingPhase === "starting" || recordingPhase === "stopping"`.
  - Debajo del par de botones, solo mientras inicia: texto `text-xs text-fx-text-2` con `role="status"`: "Esperando
    que el celular empiece a grabar…". Mientras detiene: "Cerrando y verificando la grabación. No desconectes el
    celular." Si la espera supera 10 s no aparece ningún error: el texto sigue igual.
- **Indicador REC** (bloque `isRecording`): suma el cronómetro `mm:ss`, o `h:mm:ss` desde 1 h, calculado como
  `Date.now() − Date.parse(recordingStartedAt)` con un `setInterval` de 1 s que se limpia al desmontar, en un
  `<span aria-live="off" className="tabular-nums">`. Si `recordingRecovered`, el texto principal dice
  "Grabando desde HH:MM" (hora local de `recordingStartedAt`) en lugar de "Grabando pantalla". Sin
  `recordingStartedAt` (Tatana viejo), no se muestra el cronómetro.
- **Aviso de grabación ajena** (`foreignRecording`), arriba de la tarjeta "Capturar del celular", con
  `FxBanner tone="info"`:
  - Título: "Hay una grabación en curso iniciada desde otro caso" (si `case_id` no es null) o "Hay una grabación
    en curso de origen desconocido" (si es null), o "Hay una grabación en curso de otro celular" (si el serial no
    es el seleccionado).
  - Texto: "Empezó a las HH:MM. Para grabar en este caso, primero detenela. El archivo queda guardado en Tatana y
    se recupera solo al abrir el caso que la inició."
  - Acción: botón "Detener esa grabación" (`loading.stopForeign` → "Deteniendo…"). Después: info "La grabación se
    detuvo y quedó en Tatana. No se agregó a este caso."
  - Mientras hay `foreignRecording`, "Grabar" queda deshabilitado con `aria-describedby` al aviso.
- **Aviso de captura negra** (seleccionada una captura con `notices.black[name] && !keptBlack.includes(name)`),
  debajo de los `Tag` del escenario: bloque ámbar `role="status"` con ícono `AlertTriangle`, el texto "La captura
  salió negra. ¿Pantalla apagada o app que bloquea capturas?" y dos botones chicos: "Conservar" (`onKeepBlack`) y
  "Borrar" (mismo `removeItem(selected)` de la papelera). Sin robar el foco.
- `EvidenceTrayTile` recibe `notice` (§7.9).

### 7.8 `client/src/components/VideoCard.tsx`

Prop nueva `validation?: RecordingValidation`:
- `ok` con `duration_seconds` → muestra la duración ("11:26") junto al nombre, en `text-fx-text-2 tabular-nums`.
- `incomplete` → aviso ámbar no bloqueante, `role="status"`, con ícono: "Grabación incompleta: dura
  {fmt(duration)} de {fmt(recorded)} grabados. Se guardó lo que se pudo capturar."
- Sin `validation` (iOS, Tatana viejo o recarga sin `sessionStorage`) → como hoy.
- `CaptureStep` le pasa `validation={notices.recordings[selected.name]}`.

### 7.9 `client/src/components/capture/EvidenceTray.tsx`

`EvidenceTrayTile` suma `notice?: "incomplete" | "black"`: un ícono `AlertTriangle` ámbar en la esquina inferior
derecha (sin tapar el check de seleccionado ni el rol), y el `aria-label` suma " — grabación incompleta" o
" — captura negra". No cambia el borrado.

### 7.10 `client/src/components/dashboard/GenerateStep.tsx`

Prop `reviewNotices?: { incompleteRecordings: string[]; blackScreenshots: string[] }`. Si alguna lista tiene
elementos, va un bloque informativo (`role="status"`, tono ámbar suave, **no** bloquea ni cambia `blocked`) justo
antes del botón "Generar informe pericial": "Revisá antes de generar: 1 grabación incompleta, 1 captura negra" (con
singular y plural correctos), y debajo los nombres de archivo en `text-xs`. Las capturas marcadas "Conservar" no
se listan (DA9).

### 7.11 `client/src/types/index.ts`

Re-exporta `RecordingValidation`, `RecordingStateEntry`, `UnclaimedRecording` y `RecordingPhase`.

---

## 8. Decisiones técnicas

### 8.1 Validadas por el usuario (HU, todas A)

| # | Decisión |
|---|---|
| D1 (Duda 1) | Diagnóstico acotado (§3) y solución en capas: (a) la señal llega (`--no-window` + escalado SIGTERM; Windows igual que hoy); (b) espera de 30 s; (c) `--stay-awake`; (d) sin `-shortest`; (e) kill + remux solo como último recurso, siempre seguido de la validación. |
| D2 (Duda 2) | 30 s fijos desde la señal hasta el kill, configurable (`StopTimeoutSeconds`). Si D-5 muestra un flush proporcional, se pasa a `min(60, 10 + 2/min)`. |
| D3 (Duda 3) | Tamaño > 0, duración legible y stream de video, y duración ≥ grabado − max(3 s; 2 %), configurable. El tiempo grabado se mide desde el marcador de inicio hasta el pedido de stop. |
| D4 (Duda 4) | `empty` se borra; `unreadable` va a `descartes/`; ninguno de los dos se devuelve como grabación. `incomplete` se guarda con aviso. `ImportAsync` (y la subida) rechazan 0 B con `empty_evidence`. |
| D5 (Duda 5) | `case_id` en el start. Si coincide, se recupera; si no, aviso con "Detener" que no importa. |
| D6 (Duda 6) | Detección de captura negra en el cliente (canvas), no bloqueante. |
| D7 (Duda 7) | Avisos solo visuales (bandeja + paso 5). Sin backend ni plantilla. |
| D8 (Duda 8) | Cronómetro desde la hora real de inicio de Tatana. |
| D9 (Duda 9) | La recuperación cubre iPhone con el mismo `/agent/state`. Las validaciones de C2 son solo de Android. |

### 8.2 Decisiones del arquitecto (criterio conservador)

| # | Decisión | Por qué |
|---|---|---|
| DA1 | `--no-window` reemplaza `--no-video-playback` en todos los SO, con `WindowMode` para volver atrás sin recompilar. | Es la causa raíz medida (§3.1, t2–t6). `--no-window` solo implica `--no-video-playback`: el audio del celular se sigue escuchando en la PC (D12 b de `grabacion-android-windows`) y el control sigue disponible (lo prueba `--stay-awake`). En Windows (scrcpy 4.1) no se pudo medir: lo cubre la prueba manual §11.4 paso 6. |
| DA2 | Escalado SIGINT → SIGTERM a los 3 s, solo en macOS/Linux. | t9: un Tatana lanzado en segundo plano hereda SIGINT ignorado. SIGTERM es igual de limpio en scrcpy (t8) y no rompe nada si SIGINT ya funcionó. |
| DA3 | Marcador de inicio = línea `[server] INFO: Device:` (tiempo real, medido), más 1,5 s de "sigue vivo". Con `RequireStartMarker` como salida de emergencia. | Las líneas del cliente ("Recording started…") llegan con buffer, solo al final (§3.1). Hay ~0,25 s entre `Device:` y la captura real, muy por debajo de la tolerancia de 3 s. No se activa `--verbosity=debug` para no llenar el log. |
| DA4 | Start y stop usan `CancellationToken.None` para el trabajo con scrcpy. | Si el navegador recarga en "Iniciando…"/"Deteniendo…", hoy el `RequestAborted` cortaría el stop a mitad (sin validar, sin mezclar el mic, sin evento). Así se completa y la pestaña nueva lo recupera por `/agent/state`. |
| DA5 | Códigos en inglés (`ok`, `incomplete`, `empty`, `unreadable`), como el resto del contrato (`failed`, `timeout`…). Equivalen a `ok`/`incompleta`/`vacia`/`ilegible` de la HU. | Coherencia con `video_variant_status` y `AgentErrorCodes`. |
| DA6 | Sin ffmpeg, la grabación con tamaño > 0 se acepta como `ok` con `duration_checked: false`. | No hay forma de medirla, y descartar evidencia por falta de una herramienta es peor. Tatana empaqueta ffmpeg en el portátil y en Mac lo resuelve Homebrew. Queda en el log. |
| DA7 | La validación se hace sobre el video **antes** de mezclar el mic, y la mezcla se descarta si acorta el video. | Sin `-shortest`, la duración del contenedor es la del stream más largo: un audio largo podría tapar un video corto. |
| DA8 | Los avisos `incomplete` se guardan en `sessionStorage` por caso. Los de captura negra se recalculan. | Sobreviven a la recarga (A2), que es justo cuando hacen falta, sin tocar backend ni plantilla (Duda 7 A). Al cerrar la pestaña se pierden: el perito ya los vio en la bandeja y en el paso 5. |
| DA9 | Una captura negra marcada "Conservar" no se lista en el paso 5. Una grabación incompleta siempre se lista. | "Conservar" es una decisión explícita del perito. La incompleta no tiene esa acción. |
| DA10 | `unclaimed_recordings` en memoria (24 h, 20 entradas) para que "se recupera desde ese otro caso" (Duda 5 A) sea cierto: al abrir el paso 3 de ese caso, se importa sola. | Es lo mínimo para cumplir el texto validado sin persistir estado ni tocar el backend. Si Tatana se reinicia, el archivo sigue en la raíz, igual que hoy. |
| DA11 | El código de "no hay grabación" es `not_recording` (409), el mismo de la HU #60. | Las dos HU tocan `RecordingController.Stop`. Mismo nombre, mismo status: el segundo PR solo resuelve un conflicto textual (§12). |
| DA12 | `MockAndroidRecordingOutcome` y el mock que genera un MKV de la duración grabada. | Hacen falta para que el frontend pruebe los cuatro estados sin celular, y para el escenario "Mock sin archivos vacíos". |

---

## 9. Contrato compartido

Tatana ↔ `client/`. Nombres **tal como viajan** (snake_case_lower; en HTTP los `null` se omiten, en WS viajan como
`null`).

| Mensaje | Campo JSON | Tipo | Tatana (C#) | client (TS) |
|---|---|---|---|---|
| `POST /devices/{serial}/record/start` (body) | `case_id` | string, opcional | `StartRecordingRequest.CaseId` en `Controllers/RecordingController.cs` | `agent.startRecording(…, caseId)` en `client/src/lib/agent.ts`; lo llama `client/src/hooks/useRecording.ts` |
| ídem (respuesta 200) | `filename`, `state`, `started_at`, `case_id` | string, `"recording"`, ISO-8601, string? | `RecordingController.Start` | retorno de `agent.startRecording` |
| ídem (error) | `code`: `already_recording` (409), `recording_start_timeout` (504), `invalid_case_id` (400) | string | `Common/AgentErrorCodes.cs`, `Services/AdbService.cs` | `AgentErrorCode` en `client/src/lib/agent.ts`; textos en `client/src/lib/agent-messages.ts` |
| `POST /devices/{serial}/record/stop` (200) | `filename`, `url`, `variants_expected`, `variants` (sin cambios) + `platform`, `serial`, `case_id`, `validation` | …, `"android"\|"ios"`, string, string?, `RecordingValidation`? | `RecordingController.Stop`; `RecordingValidationDto` en `Services/RecordingValidation.cs` | `RecordingStoppedData` y `RecordingValidation` en `client/src/lib/agent.ts`; consumo en `useRecording.ts` |
| `validation` (stop, `recording_stopped`, `unclaimed_recordings[]`, error 422) | `status`, `recorded_seconds`, `duration_seconds`, `duration_checked`, `forced_stop`, `started_at`, `stopped_at` | `"ok"\|"incomplete"\|"empty"\|"unreadable"`, number, number?, boolean, boolean, ISO, ISO | `RecordingValidationDto` | `RecordingValidation` |
| ídem (error 422) | `error`, `code`: `recording_empty` \| `recording_unreadable`, `validation` | string, string, objeto | `AgentHttpException` desde `AdbService.StopRecordingAsync` | `AgentErrorBody.validation`, `agent-messages.ts` |
| ídem (409) | `code`: `not_recording` | string | `RecordingNotActiveException` en `RecordingController` | `agent-messages.ts` |
| WS `recording_started.data` | `serial`, `platform`, `case_id`, `started_at`, `filename` | string, enum, string\|null, ISO, string | `RecordingController.Start` | `RecordingStartedData` en `agent.ts`; `page.tsx` |
| WS `recording_stopped.data` | todos los de stop 200 + `error`, `code` (null en éxito). En falla: `filename: null`, `url: null` | ver arriba | `RecordingController.Stop` | `RecordingStoppedData`; `page.tsx` |
| `GET /agent/state` | `recordings[]`: `platform`, `serial`, `state`, `requested_at`, `started_at`?, `filename`?, `case_id`?, `android_with_mic`?, `ios_mode`? | enum, string, `"starting"\|"recording"\|"stopping"`, ISO, ISO, string, string, boolean, `IosRecordMode` | `RecordingStateDto` en `Services/RecordingRegistry.cs`; `AgentStateResponse` en `Controllers/AgentStateController.cs` | `RecordingStateEntry`, `AgentRecordingState`, `agent.agentState()` |
| ídem | `unclaimed_recordings[]`: `platform`, `serial`, `filename`, `case_id`?, `stopped_at`, `validation`? | | `UnclaimedRecordingDto` | `UnclaimedRecording` |
| `POST /cases/{caseId}/evidence/import`, `upload-check`, `upload` | `code`: `empty_evidence` (422) | string | `Services/CaseEvidenceStore.cs` | `AgentErrorCode`; `agent-messages.ts` (acción `save`); `useFileManager.saveOne` no llega a `registerEvidence` porque `importEvidence` lanza |
| `GET /health` | `capabilities` incluye `"recording_state_v1"` | string[] | `Controllers/HealthController.cs` | `agent.supportsRecordingState()` |

Sin cambios: `operations[]`/`background_operations[]` de `/agent/state` (`agent-ui` no se toca), `video_variant_*` y
el contrato con `Factum.Backend`.

---

## 10. Checklist atómico

### 10.1 `implementer-backend` (`server/src/Factum.Agent` + `server/tests/Factum.Agent.Tests`)

> **Regla dura de datos (AGENTS.md):** esta HU no toca Mongo. No borrar ni modificar archivos preexistentes de
> `DataDirectory`/`Storage`. Las pruebas usan un Tatana **aparte** con `--port 8766 --data $S/…` en el scratchpad,
> nunca el `agent-data` del repo ni el Tatana del usuario (8765). Una prueba limpia **solo lo que creó, por ruta
> exacta**. En el celular, solo las acciones de §3.3, y los ajustes (`stay_on_while_plugged_in`,
> `screen_off_timeout`) quedan como estaban. No mover tarjetas del Project ni tocar `progress/sesiones/`.

- [ ] B1 `Models/AgentModels.cs`: `RecordingOptions` con `Normalize(ILogger)` (§5.1); `AgentOptions.Recording`, `MockAndroidRecordingSource`, `MockAndroidRecordingOutcome`.
- [ ] B2 `appsettings.json`: sección `Agent:Recording` con los defaults.
- [ ] B3 `Common/AgentErrorCodes.cs`: los códigos de §5.11 (si `NotRecording` ya existe por la HU #60, se reusa).
- [ ] B4 `Common/ProcessStop.cs`: `escalateAfter` (§5.2), sin cambiar a los otros llamadores.
- [ ] B5 `Common/ScrcpyOutput.cs` (nuevo, §5.6).
- [ ] B6 `Services/AdbService.cs`, start: flags (`WindowMode`, `--stay-awake`), `RecordingSnapshot`, marcador, timeout `recording_start_timeout` con borrado del intento, mic después del marcador, `already_recording` 409, `LineTail` sin líneas DEBUG (40 líneas), `CancellationToken.None`.
- [ ] B7 **Diagnóstico D-1, D-2 y D-7** (§3.3) con lo de B4–B6. Anotar los números. Si D-1 falla, parar y reportar `blocked`.
- [ ] B8 `Common/FfmpegProbe.cs` (nuevo, §5.3), con el reenvío desde `VideoPostprocessService.ParseFfmpegDuration`.
- [ ] B9 `Services/RecordingValidation.cs` (nuevo, §5.5).
- [ ] B10 `Services/AdbService.cs`, stop: fase `Stopping`, escalado, remux, validación previa a la mezcla, `BuildMicMuxArgs` sin `-shortest` con chequeo del temporal, `empty` (borrado de lo propio) / `unreadable` (`descartes/` con sufijo) / `ok` / `incomplete`, log de una línea, `CancellationToken.None`.
- [ ] B11 `Services/AdbService.cs`, mock: start con 1 s de espera, stop con fuente, MKV generado o embebido, `MockAndroidRecordingOutcome`. Nunca 0 B.
- [ ] B12 `Services/RecordingRegistry.cs` (nuevo, §5.7): DTOs, `IRecordingStateSource`, registro con TTL y tope.
- [ ] B13 `Services/AdbService.cs` e `IosService.cs`: implementan `IRecordingStateSource`. iOS: `CaseId`, `Udid`, flag `Stopping`, `_session = null` en el `finally` del stop, `IosStopResult` con `CaseId` y `Serial`, y `StartRecordingAsync(…, caseId, …)`.
- [ ] B14 `Controllers/RecordingController.cs`: `case_id` en el request (con validación), respuestas y eventos de §6.1–§6.4, `recording_stopped` en las fallas 422, `not_recording` 409, `registry.AddStopped`.
- [ ] B15 `Services/CaseEvidenceStore.cs`: `empty_evidence` en `ImportAsync` (sin mover) y en `CheckUpload`.
- [ ] B16 `Controllers/AgentStateController.cs`: `recordings` y `unclaimed_recordings` (recordar que el record es posicional).
- [ ] B17 `Controllers/HealthController.cs`: `recording_state_v1`. `Program.cs`: registros DI (§5.12).
- [ ] B18 Tests (§10.3).
- [ ] B19 `dotnet build server/src/Factum.Agent/Factum.Agent.csproj` sin warnings nuevos, y `dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj` en verde.
- [ ] B20 Diagnóstico D-3 a D-6 y prueba larga §11.2. Los números van a `progress/impl_backend_grabacion-android-robusta.md`. Si D-5 lo pide, ajustar `StopTimeout` (D2) y anotarlo.

### 10.2 `implementer-frontend` (`client/` solamente; `agent-ui/` **no** se toca)

> Skills obligatorias: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (solo como
> criterio, sin 3D) y `web-design-guidelines` (autochequeo al final). `ui-styling` y
> `mblode-agent-skills-ui-animation` para el cronómetro, el botón y los avisos si aplican. Leer `client/AGENTS.md`
> (Next.js 16). No mover tarjetas del Project ni tocar `progress/sesiones/`. No toca la base. Si en la prueba crea
> un caso, lo deja anotado (no hay endpoint para borrar casos) y borra solo la evidencia que registró, por su id.

- [ ] F1 `src/lib/agent.ts`: tipos de §7.1, `startRecording` con `caseId` y `agentJSON`, `stopRecording` con `agentJSON`, `agentState()`, `supportsRecordingState()`, `AgentErrorCode`/`AgentErrorBody` y el doc de capabilities.
- [ ] F2 `src/lib/agent-messages.ts`: acción `record` y los códigos de §7.2.
- [ ] F3 `src/types/index.ts`: re-exportar los tipos nuevos.
- [ ] F4 `src/lib/black-image.ts` (nuevo, §7.3).
- [ ] F5 `src/hooks/useEvidenceNotices.ts` (nuevo, §7.4).
- [ ] F6 `src/hooks/useRecording.ts`: `caseId`, `startedAt`, `remotePhase`, `foreignRecording`, `foreignStopsRef`, `syncFromAgentState`, `refresh`, `stopForeign` y el manejo de errores con códigos (§7.5).
- [ ] F7 `src/app/dashboard/page.tsx`: handlers de `recording_started`/`recording_stopped` (regla `mine`), `handleAgentConnected`, efecto del paso 3, `devices_changed`, importación de `unclaimed`, texto del banner "Retomando", y las props nuevas a `CaptureStep` y `GenerateStep` (§7.6).
- [ ] F8 `src/components/CaptureStep.tsx`: textos y estados del botón, textos de ayuda, cronómetro, "Grabando desde HH:MM", aviso de grabación ajena, aviso de captura negra, `notice` en los tiles (§7.7).
- [ ] F9 `src/components/VideoCard.tsx`: `validation` (§7.8).
- [ ] F10 `src/components/capture/EvidenceTray.tsx`: `notice` en `EvidenceTrayTile` (§7.9).
- [ ] F11 `src/components/dashboard/GenerateStep.tsx`: bloque "Revisá antes de generar" (§7.10).
- [ ] F12 Accesibilidad: avisos con `role="status"`, errores por `setGlobal` (`role="alert"` del banner), cronómetro `aria-live="off"`, nada roba el foco, spinners con `motion-reduce:animate-none`, objetivos táctiles ≥ 44 px en móvil (`min-h-11 sm:min-h-0` como el resto).
- [ ] F13 `cd client && npx tsc --noEmit` en verde, y `npm run lint` si existe el script.
- [ ] F14 Prueba en el navegador de §11.3.

### 10.3 Tests xUnit (`server/tests/Factum.Agent.Tests`)

Nuevos:
- `RecordingValidationTests.cs`:
  - `Tolerance`: 60 s → 3; 686 s → 13,72; 0 → 3; config propia.
  - `Evaluate`: `size 0` → `empty`; probe null → `ok` sin `Checked`; duración null → `unreadable`; sin video →
    `unreadable`; 550,5 de 686 → `incomplete` (el caso del QA); 683 de 686 → `ok`; 700 de 686 → `ok`; recorded
    negativo → se trata como 0 → `ok`.
- `FfmpegProbeTests.cs`: stderr real de `ffmpeg -i` sobre un MKV de scrcpy (`Duration: 00:00:13.98` + `Stream #0:0:
  Video: h264` + `Stream #0:1: Audio: opus`) → 13,98 y `HasVideo = true`; solo audio → `HasVideo = false`;
  `Duration: N/A` → null; vacío → null.
- `ScrcpyOutputTests.cs`: `[server] INFO: Device: [samsung] samsung SM-M127F (Android 11)` → true; `INFO: Recording
  started to matroska file: /x.mkv` → true; `[server] DEBUG: Display: using SurfaceControl API` → true; `INFO: ADB
  device found:` → false; `[server] INFO: Device` sin dos puntos → false; vacío → false.
- `AdbMicMuxTests.cs`: `BuildMicMuxArgs` no contiene `-shortest` y sí `-map 0:v:0 -map 1:a:0 -c:v copy`.
- `RecordingOptionsTests.cs`: `Normalize` vuelve al default campo por campo (`StopTimeoutSeconds = 0`, `TolerancePercent
  = 80`, `WindowMode = "x"`, `SigtermAfterSeconds = 40` con `StopTimeoutSeconds = 30`).
- `ProcessStopTests.cs` (solo macOS/Linux, con `Skip` en Windows): un proceso `sh -c "trap '' INT; exec sleep 60"`
  → `StopGracefullyAsync(p, 10 s, …, escalateAfter: 1 s)` devuelve `true` en < 5 s (cerró por SIGTERM, sin kill).
  Sin `escalateAfter` y con timeout de 2 s → devuelve `false` (kill).
- `RecordingRegistryTests.cs` (con `FakeTimeProvider` o un `TimeProvider` propio, y un `DataDirectory` temporal
  propio): solo lista lo que existe en disco; descarta lo de más de 24 h; tope de 20; dedup por filename.

Ampliados:
- `CaseEvidenceStoreTests.cs`: import de un archivo de 0 B → `AgentHttpException` 422 `empty_evidence`, y el archivo
  sigue en la raíz (no se movió); `CheckUpload(size: 0)` → 422 `empty_evidence`; import de 1 byte → OK como hoy.
- `VideoPostprocessServiceTests.cs`: sin cambios; tiene que seguir compilando con el reenvío de
  `ParseFfmpegDuration`.

Todos los tests con archivos usan un `Path.GetTempPath()/factum-rec-<guid>` propio y lo borran en `Dispose`, **solo
esa carpeta**. Toda espera tiene tope.

---

## 11. Verificación

### 11.1 Comandos antes de declararse `done`

| Implementador | Comandos |
|---|---|
| backend | `dotnet build server/src/Factum.Agent/Factum.Agent.csproj` · `dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj` · §3.3 · §11.2 |
| frontend | `cd client && npx tsc --noEmit` · §11.3 |
| orquestador | `./ops/harness/verify.sh` |

### 11.2 Pruebas del `implementer-backend` con el Samsung real (en esta Mac)

Tatana aparte: `dotnet run --project server/src/Factum.Agent -- --port 8766 --data "$S/rec-data"`, lanzado desde una
terminal interactiva salvo en D-2. `A=http://localhost:8766`, `D=R58RA3R5AGA`. Antes, `pgrep -fl scrcpy` vacío.

1. **Corta (regresión):** `start` con `{"android_version":11,"platform":"android","case_id":"<uuid nuevo>"}` →
   200 con `started_at` en < 3 s. Durante: `GET $A/agent/state` muestra `recordings[0].state = "recording"` con
   serial, filename y `case_id`. 30 s → `stop` → 200 con `validation.status = "ok"`, `forced_stop: false` y
   `|duration − recorded| < 1,5 s`. El log dice "detenido limpiamente".
2. **Larga (requisito del usuario): 12 minutos.** Igual que 1, con `curl -w '%{time_total}'` en el stop. Durante
   la grabación, cada 60 s, `cmd statusbar expand-notifications` y 2 s después `collapse` (movimiento real), y
   entre medio la pantalla queda quieta. Criterios:
   - `validation.status = "ok"`, `forced_stop: false` y `duration_seconds ≥ recorded_seconds − 3`.
   - Stop (tiempo del `curl`) < 5 s.
   - `stay_on_while_plugged_in` vuelve al valor anotado.
   - Último cuadro: `ffmpeg -sseof -2 -i <mkv> -frames:v 1 $S/ultimo.png` y leer el PNG. El reloj de la barra de
     estado tiene que marcar el minuto del stop (± 1 min).
   - Anotar tamaño, duración, recorded y tiempo de flush en el progress.
3. **D-3** (pantalla apagada con el botón), **D-4** (mic, 2 min; si macOS niega el permiso de micrófono, se anota y
   queda para el usuario), **D-5** (flush de 1 y 5 min; el de 12 ya está en el paso 2) y **D-6** (cierre forzado).
4. **Arranque que no llega:** con el celular **bloqueado y la pantalla apagada** (`KEYCODE_SLEEP`), `start` con
   `Agent__Recording__StartTimeoutSeconds=8`. Esperado: si el marcador igual llega (el servidor arranca con la
   pantalla apagada), anotarlo como "no reproducible así". Si no llega, 504 `recording_start_timeout`, sin archivo
   de ese intento en `$S/rec-data` y sin scrcpy vivo. Después, `KEYCODE_WAKEUP`.
5. **Huérfana + estado:** `start`, y sin hacer stop, `GET /agent/state` → aparece. `start` otra vez → 409
   `already_recording`. Desenchufar **no** (lo hace el usuario en §11.4). `stop` → OK.
   `GET /agent/state` → `recordings: []` y `unclaimed_recordings` con ese archivo y su `case_id`. `POST
   $A/cases/<case_id>/evidence/import` del archivo → 200, y `unclaimed_recordings` queda vacío.
6. **Mock** (`--mock --port 8767 --data "$S/rec-mock"`), por cada `Agent__MockAndroidRecordingOutcome` ∈ (vacío,
   `incomplete`, `empty`, `unreadable`): start → stop → `ok` con archivo > 0 B / `incomplete` / 422
   `recording_empty` sin archivo / 422 `recording_unreadable` con el archivo en `$S/rec-mock/descartes/`.
7. **`empty_evidence`:** `touch "$S/rec-data/vacio_test.png"` → `POST $A/cases/<uuid>/evidence/import
   {"filename":"vacio_test.png"}` → 422 `empty_evidence`, y el archivo sigue en la raíz.
8. **Limpieza:** cortar los Tatana de prueba y hacer `rm -rf "$S/rec-data" "$S/rec-mock" "$S/diag" "$S/diag-data"`
   (**esas rutas exactas**). Verificar los ajustes del celular.

### 11.3 Prueba en el navegador (`implementer-frontend`)

Con `NEXT_PUBLIC_AGENT_URL=http://localhost:8767` (Tatana mock de §11.2 paso 6, que puede levantar el propio
frontend con el mismo comando) y un caso de prueba **creado para esto**, en el flujo agent, paso 3:
- Grabar → "Iniciando…" ~1 s → "Detener" con el cronómetro corriendo. Detener → "Deteniendo…" con el texto de
  ayuda → la grabación aparece en la bandeja con su duración.
- Con `MockAndroidRecordingOutcome=incomplete`: el tile tiene el ícono ámbar, `VideoCard` muestra "Grabación
  incompleta: dura … de … grabados", y en el paso 5 aparece "Revisá antes de generar: 1 grabación incompleta".
  Al recargar, el aviso sigue (`sessionStorage`).
- Con `empty` / `unreadable`: no hay tile y aparece el banner de error con el texto de §7.2.
- **Recarga mientras graba:** grabar, recargar y retomar el caso → "Detener", "Grabando desde HH:MM" y el
  cronómetro sigue desde el inicio real (no desde 0). Detener → se agrega a la bandeja de este caso.
- **Otro caso:** grabar en el caso A, abrir el paso 3 del caso B (otra pestaña o retomar) → aviso "iniciada desde
  otro caso" con "Detener esa grabación" y "Grabar" deshabilitado. Detenerla desde B → no se agrega a B. Abrir A →
  se recupera sola (`unclaimed`) con el banner info.
- **Tatana viejo:** apuntar al Tatana del usuario solo si todavía no tiene `recording_state_v1`, o simularlo
  devolviendo `false` en `supportsRecordingState` con un cambio local que **no se commitea** → grabar y detener
  como hoy, sin errores en consola.
- **Captura negra:** en el mock, la captura es un JPEG de 1x1 (`MockImageBytes`). Comprobar con una captura real o
  con una imagen negra adjuntada como "local-image": el aviso aparece solo en `kind === "screenshot"` (para probar,
  alcanza con forzar el resultado de `isMostlyBlack` en un cambio local que no se commitea). "Conservar" lo oculta
  y lo saca del paso 5; "Borrar" la quita.
- Teclado: Tab llega a "Detener esa grabación", "Conservar" y "Borrar" con el foco visible. Lector de pantalla: el
  cronómetro no se anuncia cada segundo.

Si se registró evidencia en el backend durante la prueba, se borra **solo por su id**. Si no se puede sin tocar
datos ajenos, se deja y se anota en `progress/impl_frontend_grabacion-android-robusta.md`.

### 11.4 Prueba manual que queda para el usuario

1. **Mac + Samsung, desde la web:** grabar **12 min** y detener. La tarjeta muestra una duración igual a la del
   cronómetro (± 3 s), y el último cuadro es de los segundos previos a "Detener". Generar: el hash del ZIP coincide.
2. **Pantalla que se apaga sola:** poner el apagado de pantalla del celular en 30 s, grabar 10 min sin tocarlo y
   detener. Con `--stay-awake` la pantalla no se apaga y la duración es completa. Después, volver el ajuste del
   celular a su valor.
3. **Audio:** 10 min con audio del celular (reproducir algo) y 10 min con "mic de la PC" → en los dos, el video
   dura lo mismo que la grabación y se oye el audio. En el modo con audio del celular, el audio se sigue escuchando
   en la PC mientras se graba.
4. **Recarga y WS:** grabar, recargar la página (y en otra prueba, reiniciar el dev server de Next) → "Detener" y
   el cronómetro se recuperan. Detener → entra al caso.
5. **USB:** desenchufar a mitad de una grabación → la UI sale de "Grabando" (como hoy) y "Grabar" funciona al
   reconectar, sin "Ya hay una grabación en curso".
6. **Windows (Tatana portátil, scrcpy 4.1):** grabar 30 s y 10 min. El log de Tatana dice "detenido limpiamente"
   (no "se forzó el cierre"), y la grabación arranca en pocos segundos. **Si el start falla con "El celular no
   empezó a grabar a tiempo"** con el celular desbloqueado: poner `"Recording": { "RequireStartMarker": false }` en
   `appsettings.Local.json` y avisar (es el plan B de DA3). **Si el stop sigue forzando el cierre:** probar
   `"WindowMode": "no_video_playback"` y avisar.
7. **Captura negra:** con la pantalla del celular apagada, tomar una captura → la tarjeta avisa. "Conservar" la deja
   sin aviso. En el paso 5, con una sin conservar, aparece "1 captura negra".
8. **Regresión:** 30 s de Android en macOS y en Windows, y 30 s de iPhone (DVT) → se guardan con su SHA-256. La de
   iPhone muestra la versión suavizada como hoy.

### 11.5 Qué chequea el reviewer además de `CHECKPOINTS.md`

- `AdbService` ya no usa `--no-video-playback` por defecto, y `-shortest` no aparece en el repo
  (`grep -rn "shortest" server/src`).
- Ningún camino de `StopRecordingAsync` devuelve un archivo sin pasar por `RecordingValidation.Evaluate`, y ningún
  archivo de 0 B se devuelve como grabación ni se importa.
- `empty` borra **solo** el archivo de ese stop y `unreadable` mueve, no borra.
- Start/stop no dependen del `RequestAborted` del request.
- `recording_stopped` con `case_id` de otro caso nunca hace `addFile` en el cliente.
- Contrato de §9 idéntico en los dos lados (nombres snake; el cliente tolera `null` y ausente).
- `agent-ui/` sin cambios. `operations` de `/agent/state` igual que antes.
- El progress de backend tiene la tabla del diagnóstico §3.3 y los números de §11.2 paso 2.

---

## 12. Concurrencia con otras HU

- **`tatana-ws-y-hosts` (#60)** toca `RecordingController.Stop` (T-14: `RecordingNotActiveException` → 409
  `not_recording`), `AdbService.StopRecordingAsync`/`IosService.StopRecordingAsync` (el tipo de excepción) y
  `AgentErrorCodes`. Esta SDD usa **el mismo** nombre de excepción, código y status (DA11). El que se mergee segundo
  rebasa y conserva una sola definición. #60 también convierte `hub.BroadcastAsync` en "encolar y volver": no
  cambia nada acá.
- **`wizard-concurrencia-pestanas`** (propuesta) va a tocar el reparto de `screenshot_taken`/`recording_stopped` entre
  pestañas y los eventos de custodia (A5). Esta HU solo agrega la regla `mine` de §7.6. Si esa HU llega primero, el
  segundo PR combina las dos condiciones.
- **`ios-grabacion-larga`** (#53, mergeada): `recording_stopped` mantiene `variants_expected`/`variants` y la misma
  semántica. Los campos nuevos son aditivos.

## 13. Fuera de alcance (recordatorio técnico)

- Finalizar e importar sola una grabación cuyo scrcpy terminó por desconexión del USB. Hoy queda en la raíz sin
  validar, y esta HU solo garantiza que no trabe "Grabar" (escenario de la HU). Es candidata a una HU propia
  (prueba física 1 del QA).
- Cerrar limpio una grabación en curso cuando Tatana se apaga (lo de T-02 de #60 no la cubre).
- Avisar antes de capturar que la pantalla está apagada (Duda 6 C).
- Avisos en el expediente o el informe (Duda 7 B), y rechazo de 0 B en `Factum.Backend` (Duda 4 C).
- Detectar tramos negros o congelados dentro del video, segmentar, pausar o cambiar formato/bitrate.
- Los eventos de custodia falsos o duplicados (A5), incluido el "Inicio de grabación" de un intento rechazado.
