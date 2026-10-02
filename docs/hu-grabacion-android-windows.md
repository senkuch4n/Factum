# HU: Grabar la pantalla de un Android desde Tatana en Windows

**Slug:** `grabacion-android-windows`
**Apps afectadas:** agente Tatana (`server/src/Factum.Agent`: `Services/AdbService.cs`, y según las
dudas `Services/IosService.cs`, `Services/WebcamService.cs`, `Controllers/HealthController.cs`),
empaquetado (`deploy/windows/armar-tatana-portable.sh`, quizá `deploy/windows/scripts/diagnostico.ps1`
y la guía de instalación) y, según la duda D7, `client/` (`src/lib/agent.ts`, `src/hooks/useRecording.ts`).
**Sin cambios** en la API (`Factum.Backend`) ni en `agent-ui/`.
**Origen:** reporte del usuario (perito) del 2026-10-02, tras instalar Factum v1.0.0 en la PC
Windows 10 del estudio: "al querer grabar la pantalla del celular Android, no me funciona".
**Restricción:** el desarrollador no tiene un Android para probar en su Mac; la verificación real
se hace en la PC del estudio.
**Depende de:** `instalacion-local-docker` (aprobada), que armó el paquete y el portátil de Tatana.

**Como** perito que usa Factum en la PC Windows del estudio
**quiero** poder grabar la pantalla del celular Android conectado por USB, igual que en la Mac
**para que** la grabación quede como evidencia del caso, sin tener que instalar nada a mano.

---

## Contexto

### Qué existe hoy (arqueología sobre `8290b59`)

**Cómo graba Tatana un Android** — `AdbService.StartRecordingAsync` (L115-167) lanza **scrcpy** como
proceso hijo y graba directo a un MKV local (`RecordingController` L35-37, `storage.NewFilePath("grabacion", "mkv")`):

```
scrcpy --serial <s> --record "<ruta>" --record-format mkv --no-video-playback
       --max-size 1080 --video-bit-rate 4M  <audio>
```

`<audio>` = `--audio-source=output` si Android >= 11 y no se eligió "con mic"; `--no-audio` en otro caso.
stdout/stderr se drenan al log en nivel Debug. Con `--no-video-playback` scrcpy no abre ventana,
pero **sí reproduce el audio del celular por los parlantes de la PC** (no se pasa `--no-audio-playback`).

**Por qué falla en Windows (verificado en el código):**

1. **scrcpy no viaja en el portátil.** `deploy/windows/armar-tatana-portable.sh` (pasos 3-5) mete
   en `tools/` solo `platform-tools/` (adb, versión "la más nueva" del manifest de Google),
   `python-embed/` (+ pymobiledevice3), `ffmpeg/ffmpeg.exe` (BtbN `latest`, sin checksum) y,
   opcional, `uxplay/`. No hay `tools/scrcpy/` (confirmado por el orquestador en el zip de v1.0.0).
2. **Aunque estuviera, Tatana no lo encontraría.** `ScrcpyPaths = ["scrcpy", "/opt/homebrew/bin/scrcpy",
   "/usr/local/bin/scrcpy"]` (L23-24). `FindScrcpy` (L304-308) usa `IsOnPath("scrcpy")` (L310-315),
   que busca un archivo llamado literalmente `scrcpy` **sin `.exe`** en cada carpeta del PATH: en
   Windows nunca da positivo, ni siquiera con scrcpy instalado en el PATH. No mira `tools/`.
   El error resultante es `"scrcpy no encontrado. Instalá con: brew install scrcpy"`.
3. **Lo que ve el perito:** `RecordingController.Start` devuelve 500 con `{ error: "<mensaje>" }`, pero
   `client/src/lib/agent.ts` `startRecording` (L138) descarta el cuerpo y tira
   `"Error iniciando grabación"`. O sea, el perito ve un mensaje genérico, sin causa. Al detener,
   `useRecording.handleToggleRecord` (L32) hace `.catch(() => null)`: si el stop falla, la
   grabación se pierde **sin ningún aviso**.

**Detener la grabación en Windows** — `StopRecordingAsync` (L244-302) intenta `kill -2 <pid>`
(SIGINT) lanzando el ejecutable `kill`, que no existe en Windows: la excepción se traga, espera
5 s y hace `Process.Kill()` (TerminateProcess). Consecuencias, a confirmar en la PC del estudio:
- cada stop tarda ~5 s de más;
- scrcpy muere sin cerrar el MKV: el comentario del código dice que el MKV "es válido incluso tras
  SIGKILL", lo cual es en general cierto para reproducir, pero queda **sin duración ni índice
  (cues)** y se pierden los últimos paquetes en buffer; algunos reproductores no permiten
  adelantar/retroceder. Para evidencia conviene un cierre limpio.

Windows no tiene SIGINT para procesos sin consola: el equivalente es `CTRL_C_EVENT`
(`GenerateConsoleCtrlEvent`), que exige compartir consola con scrcpy. El agente se arranca con
`start "Tatana Agent" /min "Factum.Agent.exe"` (`packaging/portable/launch-tatana.bat` L33) y scrcpy
con `CreateNoWindow = true`. Ver duda D5.

**Modo "con micrófono" (Android)** — `StartMicAudio` (L177-205) graba el mic de la PC con ffmpeg y
al detener lo remuxea sobre el MKV. En Windows tiene tres problemas:
- `FfmpegPaths` de `AdbService` (L29-30) no mira `tools/ffmpeg/` y tiene el mismo bug de `.exe`
  (`FindBinary` L32-33); cae a `"ffmpeg"` pelado, que `Process.Start` solo encuentra si está en el
  PATH del sistema (el portátil no lo agrega).
- Usa `-f wasapi -i default`: las builds habituales de ffmpeg para Windows (incluida la BtbN que
  viaja) capturan audio con `dshow`, no con `wasapi`. A verificar con `ffmpeg -devices` en la PC.
- `StopMicAudio` (L207-228) en Windows hace `Kill()` directo: el `.m4a` (contenedor MP4) queda
  sin `moov` y el remux falla → el audio del mic se pierde con solo un warning en el log.
El mismo código existe en `IosService` (`with_mic`, L1176).

**Cómo resuelve herramientas `IosService`** — `BuildToolCandidates` (L40-49) arma
`[tools/<dir>/<exe>, unixName, winName, /opt/homebrew/bin/…, /usr/local/bin/…]`, así que
`ffmpeg`/`uxplay` del portátil sí se encuentran. Funciona **de casualidad** en Windows: la ruta
portátil lleva `\`, `p.Contains('/')` da falso, va a `IsOnPath(rutaAbsoluta)` y
`Path.Combine(dir, rutaAbsoluta)` devuelve la absoluta. `FfprobePaths` y `RifePaths` (L53-57) no
usan el helper. `AdbPath` (`AdbService` L39-44) es otro patrón más (solo `tools/platform-tools/`).

**Otros binarios que en Windows tampoco se encuentran** (hallazgos, no todos son de esta HU):
| Binario | Dónde | Situación en Windows |
|---|---|---|
| scrcpy | `AdbService` | No viaja ni se busca bien → **causa del reporte** |
| ffmpeg (mic Android) | `AdbService` | Viaja, pero no se busca en `tools/` |
| ffmpeg (iOS `video_only`) | script Python embebido `DvtRecorderScript` (`IosService` L781-790) | Busca en PATH con separador `:` y sin `.exe`; no recibe la ruta del portátil |
| ffmpeg (foto webcam) | `WebcamService` L28 | `"ffmpeg"` pelado + dispositivo fijo `video="Integrated Camera"` |
| ffprobe | `IosService` | No viaja (solo se copia `ffmpeg.exe` del zip de BtbN, que sí trae `ffprobe.exe`) |
| rife-ncnn-vulkan | `IosService` | Opcional (variante interpolada iOS); no viaja |
| qvh | `IosService` | Sin build de Windows (conocido) |
| uxplay | `IosService` | Opcional, sin AirPlay si no se configura (conocido) |

**Conflicto de adb (riesgo):** el zip de scrcpy para Windows trae su propio `adb.exe`. Si scrcpy usa
el suyo y Tatana el de `platform-tools` en otra versión, cada uno mata el servidor adb del otro
("adb server version doesn't match this client; killing...") y el celular aparece/desaparece de la
lista. scrcpy respeta la variable de entorno `ADB` para elegir qué adb usar. scrcpy además necesita
el archivo `scrcpy-server` en la misma carpeta que `scrcpy.exe` (o `SCRCPY_SERVER_PATH`).

**Cómo llega una corrección a la PC del estudio** — `Actualizar Factum.bat` → `actualizar.ps1`
paso 9 (L170-184): compara la versión del zip `Tatana-Portable-v<X>-Windows.zip` con
`%LOCALAPPDATA%\Programs\Tatana\version.txt` y, si difieren, pregunta S/N; `install-portable.bat`
copia encima con `xcopy /E /Y` (las carpetas nuevas de `tools/` llegan; nada se borra). **Con la
misma versión (1.0.0) no ofrece actualizar Tatana:** hace falta un paquete con versión nueva.
`update-portable.ps1` (auto-actualización) queda apagado en la instalación local (`UPDATE_URL=` vacío).
`/health` informa `version = "2.0.0"` fijo en `HealthController` (no la del paquete) — fuera de alcance.

**Diagnóstico** — `diagnostico.ps1` (L136-141) muestra de Tatana solo `version`, `ios_available` y si
está en mock. No dice qué herramientas encontró.

**Licencias** — scrcpy es Apache-2.0. Su build de Windows incluye además DLLs de FFmpeg (LGPL),
SDL (zlib) y libusb (LGPL). Hoy el portátil no incluye ningún archivo de licencia de terceros
(tampoco de ffmpeg BtbN GPL ni de platform-tools).

---

## Criterios de aceptación

```gherkin
Feature: Grabación de pantalla Android en Tatana para Windows

  Background:
    Given Factum instalado en una PC Windows 10/11 con el paquete de esta HU
    And Tatana en modo real (no mock)

  Scenario: El portátil trae scrcpy y su licencia
    When se arma el paquete con armar-paquete.sh
    Then el zip Tatana-Portable-v<X>-Windows.zip contiene tools/scrcpy/scrcpy.exe y tools/scrcpy/scrcpy-server
    And contiene el texto de la licencia de scrcpy y un aviso de terceros con versión y origen
    And la descarga de scrcpy es de una versión fija y el script falla si el SHA-256 no coincide

  Scenario: Grabar y detener sin micrófono
    Given un Android 11 o superior conectado por USB, autorizado y visible en Factum
    When el perito pulsa "Grabar", espera 30 segundos y pulsa "Detener"
    Then se genera un .mkv con video y audio interno del celular
    And la grabación aparece en la bandeja de evidencia del caso
    And el archivo se reproduce entero y permite adelantar/retroceder en el reproductor de Windows o VLC
    And el stop no tarda más de unos pocos segundos

  Scenario: Android anterior a 11
    Given un Android 10 conectado
    When el perito graba y detiene
    Then se genera un .mkv solo con video, sin error

  Scenario: Grabar con el micrófono de la PC
    Given un Android conectado y la opción "con mic de PC" activada
    When el perito graba hablando cerca de la PC y detiene
    Then el .mkv final tiene el video del celular y el audio del micrófono de la PC

  Scenario: El celular no se cae de la lista mientras se graba
    Given un Android visible en Factum
    When el perito graba 2 minutos
    Then el dispositivo no desaparece ni reaparece de la lista durante la grabación
    And el log de Tatana no registra reinicios del servidor adb por versión distinta

  Scenario: Falta scrcpy (instalación incompleta)
    Given un Tatana sin tools/scrcpy y sin scrcpy en el PATH
    When el perito pulsa "Grabar"
    Then Factum muestra un mensaje en castellano que dice que falta el componente de grabación de Android y que hay que reinstalar o actualizar Tatana
    And el mensaje no menciona brew ni comandos de Mac

  Scenario: Falla al detener
    Given una grabación en curso
    When el stop devuelve error
    Then Factum avisa al perito que la grabación no se pudo guardar (no la pierde en silencio)

  Scenario: La Mac sigue igual
    Given Tatana en macOS con scrcpy de Homebrew
    When el desarrollador graba un Android (o corre el modo disponible en la Mac)
    Then el comportamiento es el mismo que antes de esta HU

  Scenario: La corrección llega con "Actualizar Factum"
    Given la PC del estudio con Factum y Tatana 1.0.0
    When se corre "Actualizar Factum" con el paquete nuevo
    Then el actualizador ofrece actualizar Tatana y, al aceptar, queda tools/scrcpy instalado
```

---

## Datos que se registran

No aplica: no hay datos nuevos en la base. El archivo de grabación sigue siendo el `.mkv` de hoy
en la carpeta de datos de Tatana (`%LOCALAPPDATA%\Tatana\data`).

---

## Diseño UX/UI

- **App:** `client/` (web), paso de captura del dashboard. Sin pantallas nuevas ni cambios visuales
  en el flujo feliz: el botón "Grabar"/"Detener" y la bandeja funcionan como en la Mac.
- **Error al iniciar:** el aviso que hoy muestra `onError` pasa a mostrar el mensaje que manda
  Tatana (si viene) en vez de "Error iniciando grabación". Mensaje propuesto para scrcpy ausente:
  *"No se encontró el componente de grabación de Android (scrcpy). Actualizá o reinstalá Tatana
  con el paquete de Factum."*
- **Error al detener:** aviso visible *"No se pudo guardar la grabación: <motivo>"* (hoy se
  descarta en silencio).
- **Diagnóstico de Factum** (consola, `diagnostico.ps1`): si se aprueba D8, una línea por
  herramienta de Tatana: `adb: OK (ruta)`, `scrcpy: OK (versión)`, `ffmpeg: OK`, `python: OK`,
  o `FALTA`.

---

## Fuera de alcance

- Grabar sin cable (scrcpy por Wi-Fi/TCP) o con varios Android a la vez.
- Cambiar resolución, bitrate o formato (MKV) de la grabación.
- AirPlay/uxplay, qvh y rife en Windows (ya conocidos).
- La versión fija "2.0.0" que reporta `/health`.
- Fijar versión/checksum de platform-tools y ffmpeg (hoy "latest"), salvo que el usuario lo pida en D1.
- Instaladores para Mac o Linux.
- Los binarios de iOS y webcam de la tabla de hallazgos, salvo lo que se decida en D11.

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- Prueba manual de referencia en la PC del estudio (cmd), antes y después del cambio, para separar
  "scrcpy anda" de "Tatana lo invoca bien":
  ```
  cd %LOCALAPPDATA%\Programs\Tatana\tools\scrcpy
  set ADB=%LOCALAPPDATA%\Programs\Tatana\tools\platform-tools\adb.exe
  scrcpy.exe --version
  scrcpy.exe --record "%USERPROFILE%\Desktop\prueba.mkv" --record-format mkv --no-video-playback --max-size 1080 --video-bit-rate 4M --audio-source=output
  ```
  (Ctrl+C para cortar; abrir `prueba.mkv` y probar adelantar).
- Verificar en la PC: `tools\ffmpeg\ffmpeg.exe -hide_banner -devices` (¿aparece `wasapi`? ¿`dshow`?)
  y `-list_devices true -f dshow -i dummy` para el nombre del micrófono.
- No hay proyecto de tests .NET; si se aprueba D9, la SDD dice dónde crearlo.

---

## Dudas para validar con el usuario

**D1. Versión de scrcpy y verificación de la descarga.**
- a) Fijar una versión concreta (la última estable al momento de implementar, que soporte todos los
  flags actuales; el architect la confirma contra `scrcpy --version` de Homebrew en la Mac) con su
  SHA-256 escrito en el script; el armado falla si no coincide.
- b) Bajar siempre "latest" como hoy con ffmpeg.
- **Recomendada: a)** — es software de adquisición forense: el informe tiene que poder decir con qué
  versión exacta se grabó, y una release nueva de scrcpy puede renombrar flags (ya pasó con
  `--no-display` → `--no-playback` → `--no-video-playback`). Que platform-tools y ffmpeg sigan en
  "latest" queda fuera de alcance salvo que quieras sumarlos.

**D2. Ubicación y contenido de `tools/scrcpy/`.**
- a) `tools/scrcpy/` con el zip de Windows entero menos su `adb.exe` y `AdbWin*.dll`, más
  `LICENSE` (Apache-2.0) y un `THIRD-PARTY-NOTICES.txt` (scrcpy, FFmpeg LGPL, SDL, libusb, con
  versiones y URLs).
- b) Igual, pero dejando el `adb.exe` de scrcpy.
- **Recomendada: a)** — mismo patrón que `tools/ffmpeg/`; sacar su adb elimina la posibilidad de que se
  use por error (ver D3). Las licencias de terceros son obligatorias al redistribuir.

**D3. Qué adb usa scrcpy.**
- a) Tatana lanza scrcpy con la variable de entorno `ADB` apuntando al `adb.exe` de
  `tools/platform-tools` (el mismo que usa para listar dispositivos).
- b) Usar el adb que trae scrcpy para todo Tatana (y dejar de bajar platform-tools).
- **Recomendada: a)** — un único adb para todo: no hay pelea de versiones ni el celular apareciendo y
  desapareciendo, y no se toca el resto del flujo Android (explorador, capturas) que ya funciona.

**D4. Búsqueda de herramientas unificada.**
- a) Un helper común (`tools/<dir>/<exe>` junto al exe → PATH respetando `.exe` en Windows → rutas
  de Homebrew) usado por `AdbService` (scrcpy, ffmpeg, adb), `IosService` y `WebcamService`, que
  también arregle el chequeo `p.Contains('/')` (usar `Path.IsPathRooted`).
- b) Arreglar solo `AdbService` y copiar el patrón de `BuildToolCandidates`.
- **Recomendada: a)** — hoy hay tres implementaciones distintas y dos tienen el mismo bug; un helper
  único es testeable sin Android (D9) y evita que el próximo binario repita el problema.

**D5. Cómo detener scrcpy en Windows.**
- a) Cierre limpio: mandar `CTRL_C_EVENT` a scrcpy (por ejemplo, lanzándolo en su propio grupo de
  consola y usando un pequeño helper que se adjunta a esa consola), con espera y `Kill()` como
  último recurso, y un remux `-c copy` con ffmpeg si hubo que matarlo, para que el MKV tenga
  duración e índice.
- b) Dejar `Kill()` directo (sin la espera inútil de 5 s) y siempre remuxear con ffmpeg.
- **Recomendada: a)** — el cierre limpio no pierde los últimos segundos y deja el archivo tal como lo
  escribió scrcpy (mejor para cadena de custodia); b) queda como red de seguridad. El architect
  define el mecanismo exacto; la validación final es en la PC del estudio.

**D6. Modo "con micrófono de la PC" en Windows.**
- a) Incluirlo en esta HU: ffmpeg resuelto desde `tools/ffmpeg`, captura con `dshow` (micrófono
  por defecto o el primero que liste), stop limpio mandando `q` por stdin (vale en todos los SO),
  y aplicar el mismo arreglo al `with_mic` de iOS porque es el mismo código.
- b) Solo Android; iOS `with_mic` en otra HU.
- c) Dejar el modo con mic para otra HU.
- **Recomendada: a)** — el perito lo va a usar para notas de voz de WhatsApp, que es justamente lo que
  no se puede grabar de otra forma; hoy en Windows falla en silencio. Compartir el arreglo con iOS
  sale casi gratis si se hace D4.

**D7. Mostrar en Factum el motivo real del error (toca `client/`).**
- a) `agent.ts` lee `error` del cuerpo de la respuesta de Tatana al iniciar y al detener, y
  `useRecording` avisa también cuando falla el stop.
- b) Solo arreglar el mensaje en Tatana (el perito seguiría viendo "Error iniciando grabación").
- **Recomendada: a)** — sin esto el perito nunca sabe qué pasó (fue lo que pasó con este reporte), y
  perder una grabación sin aviso es grave en un peritaje. Son pocas líneas en el client.

**D8. Herramientas de Tatana en "Diagnóstico de Factum".**
- a) `/health` agrega un bloque `tools` (por herramienta: encontrada sí/no, ruta, versión cuando sea
  barato obtenerla) y `diagnostico.ps1` lo imprime con OK/FALTA.
- b) No agregar nada.
- **Recomendada: a)** — es la forma de diagnosticar a distancia en la PC del estudio sin Android a
  mano: "scrcpy: FALTA" responde en un segundo lo que este reporte tardó en averiguar. Cambio
  aditivo al contrato de `/health` (snake_case).

**D9. Cómo verificar sin Android.**
- a) Crear un proyecto de tests .NET mínimo para el helper de D4 (layout falso de `tools/` en una
  carpeta temporal, PATH y extensión `.exe` simulados), un chequeo en `armar-tatana-portable.sh` que
  falle si el zip no trae `scrcpy.exe`/`scrcpy-server`, y la prueba manual en la PC del estudio con
  los comandos de "Notas de implementación" más los escenarios Gherkin.
- b) Solo prueba manual en la PC del estudio.
- **Recomendada: a)** — la lógica de rutas es justo lo que se rompió y se puede probar en la Mac sin
  hardware; lo que depende del celular (stop limpio, audio, adb) queda sí o sí para la PC del estudio.

**D10. Versión del paquete que lleva el arreglo.**
- a) Publicar `v1.0.1` (paquete completo) y actualizar con "Actualizar Factum", aceptando la pregunta
  de actualizar Tatana.
- b) Mandar solo un zip nuevo de Tatana para instalar a mano.
- **Recomendada: a)** — `actualizar.ps1` solo ofrece Tatana si la versión cambia, y es el camino
  documentado; también deja el arreglo registrado en la versión.

**D11. Otros binarios que en Windows no se encuentran (iOS `video_only` vía script Python, foto
de webcam por `WebcamService`, `ffprobe`).**
- a) Incluir solo la resolución de rutas (que sale con D4): pasar la ruta de ffmpeg al script
  Python y usar el helper en `WebcamService`; sumar `ffprobe.exe` al portátil (ya viene en el
  zip de BtbN). Sin tocar el dispositivo fijo `"Integrated Camera"`.
- b) Dejarlo todo para otra HU y solo listarlo en el backlog.
- **Recomendada: b)** — el reporte es de Android y no hay forma de probar iOS en esa PC dentro de
  esta HU; mezclarlo agranda la revisión. Si preferís a), es poco código, pero queda sin prueba real.

**D12. Audio del celular por los parlantes de la PC durante la grabación.**
- a) Agregar `--no-audio-playback` (graba el audio interno pero no lo reproduce en la PC).
- b) Dejarlo como está hoy en la Mac (se escucha por los parlantes).
- **Recomendada: b)** — es el comportamiento actual y puede servirle al perito para confirmar que se
  está grabando sonido; si en el estudio molesta, es un flag. No inventamos el cambio sin tu ok.

## Validación (2026-10-02)

El usuario validó las 12 dudas: D1–D10 en **a)**, D11 y D12 en **b)**. D10: el paquete se llama **1.1.0** (incluye también `subida-archivos-grandes`).
