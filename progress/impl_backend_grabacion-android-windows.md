# impl_backend — grabacion-android-windows

**Estado:** `done`, sin commit (lo pidió el orquestador).
**SDD:** `Refactorizaciones/grabacion-android-windows.md`. **HU:** `docs/hu-grabacion-android-windows.md`.
**Rama:** `feat/grabacion-android-windows` (HEAD `8290b59`, sin commits nuevos).
**Fecha:** 2026-10-02. **Decisiones:** las recomendadas, más DP6 = A (con mic, si el mic falla no se graba) y
DP14 = "no por ahora" (solo scrcpy lleva su aviso de licencia).

No toqué `client/`, `agent-ui/`, `Factum.Backend`, `backlog.json`, `progress/current.md` ni
`server/src/Factum.Agent/appsettings.json` (sigue con el `"Mock": true` local del usuario, sin editar ni stagear).
Tampoco toqué MongoDB, `dev-data/`, `Storage:DataDirectory` ni el `agent-data/` de desarrollo: los agentes de prueba
(V4/V5/E2) corrieron con `--data <scratch>/agent-data`, y esa carpeta quedó vacía y después se borró. No usé `prune`.

## Checklist

| # | Estado | Nota |
|---|---|---|
| B1 | [x] | `InternalsVisibleTo Include="Factum.Agent.Tests"` en `Factum.Agent.csproj`. |
| B2 | [x] | `Common/ToolResolver.cs`: `ToolSource`, `ToolSpec`, `ToolResolution`, `ToolEnvironment` (+ `Current`), `AgentTools` y `ToolResolver.Find` con el algoritmo exacto de §6.1. |
| B3 | [x] | `AdbService`: saqué `ScrcpyPaths`, `FfmpegPaths`, `FindBinary`, `IsOnPath` y `FindScrcpy`. `AdbPath` = `Lazy(ToolResolver.Find(AgentTools.Adb)?.Path ?? "adb")`. |
| B4 | [x] | `IosService`: saqué `BuildToolCandidates`, `Ffmpeg/Ffprobe/Rife/Qvh/UxplayPaths`, `FindBinary(string[])` e `IsOnPath`. Ahora hay un `FindBinary(ToolSpec)` y todas las llamadas usan `AgentTools.*`. No toqué `PythonCandidates`/`BuildPythonCandidates` ni `DvtRecorderScript`. |
| B5 | [x] | `Common/WindowsConsoleSignal.cs`: P/Invoke a kernel32 con `[SupportedOSPlatform("windows")]`, `RunHelper(pid)` (0 ok / 1 no-Windows o pid inválido / 2 AttachConsole / 3 GenerateConsoleCtrlEvent), la función pura `BuildHelperCommand` y `EnableCtrlCForChildren()` (DP12). |
| B6 | [x] | `Program.cs`: la rama `--ctrl-c` está antes de `WebApplication.CreateBuilder`. En Windows se llama a `SetConsoleCtrlHandler(NULL, FALSE)`. `ToolInventory` queda registrado como singleton y `WarmUp()` se llama antes del polling. |
| B7 | [x] | `Common/ProcessStop.cs`: `StopGracefullyAsync`. En Windows lanza el auxiliar (timeout de 3 s; si falla, Warning y 1 s de espera). En Unix usa `kill -2`, como antes. Si no termina a tiempo: `Kill(entireProcessTree)` + Warning. Logs con el texto de §6.5. |
| B8 | [x] | `AgentOptions.MicDevice` (`string?`). |
| B9 | [x] | `Services/MicCapture.cs` (`StartAsync`/`StopAsync`/`AudioPath`) y `DshowDevices.ParseAudioDevices` (formato nuevo y viejo), con E4/E5/E6 textuales. |
| B10 | [x] | `AdbService.StartRecordingAsync`: E3, E1 (también si al portátil le falta `scrcpy-server`), `ArgumentList`, `ADB`, `SCRCPY_SERVER_PATH` (solo en el portátil), buffer de 20 líneas, Warning para ERROR/WARN/`adb server version`/`doesn't match`, espera de 1.5 s y E2. El mic usa `MicCapture` y, si falla, hace rollback (para scrcpy y borra solo el MKV y el `.mic.mka` de ese intento). |
| B11 | [x] | `AdbService.StopRecordingAsync`: E7, `ProcessStop` con 10 s, estado limpiado en `finally`, mic detenido siempre, E8, mux del mic (con `ArgumentList`) y remux si el cierre fue forzado y no hubo mux. |
| B12 | [x] | iOS `with_mic`: `MicCapture` con solo Warning si falla. El mic se detiene antes del stop de DVT y del de burst; antes, en burst nunca se detenía. El temporal ahora es `ios_audio_<id>.mka`. |
| B13 | [x] | `Services/ToolInventory.cs` (`ToolStatus`, `Snapshot`, `WarmUp`, caché por ruta con `TryAdd` y versión con timeout de 5 s + `Kill`) y `VersionParser`. `/health` suma `tools`. |
| B14 | [x] | `grep -n brew Services/AdbService.cs`: queda solo la sugerencia de macOS (`ScrcpyMissingMessage`, detrás de `OperatingSystem.IsMacOS()`), más un comentario. Los mensajes de iOS no cambiaron. |
| B15 | [x] | Build sin warnings (V1). |
| T1 | [x] | `server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj`: mismo patrón que `Factum.Backend.Tests` + `ValidateExecutableReferencesMatchSelfContained=false`. No se agregó a `Factum.sln`, igual que el de Backend. |
| T2 | [x] | `ToolResolverTests.cs`: T2a–T2j más 3 casos extra (solo `.bat`/`.cmd` → null, `.`/relativas → null, portátil en Unix usa el nombre Unix). Cada test usa su propio `factum-tools-<guid>` en temp y lo borra en `Dispose`. |
| T3 | [x] | `DshowDevicesTests.cs`: formato nuevo (`[in#0 @ …]`, 1 cámara + 2 mics, uno con acento, más un `(none)`, CRLF), la misma salida con prefijo `[dshow @ …]`, formato viejo, solo video y vacío. |
| T4 | [x] | `VersionParserTests.cs`: adb (con y sin línea `Version`), scrcpy 4.1, los dos ffmpeg de la SDD, python por stdout y por stderr, salida vacía y `SourceText` (portable/path/homebrew). |
| T5 | [x] | `WindowsConsoleSignalTests.cs`: `…\Factum.Agent.exe`, `dotnet` (Unix), `dotnet.exe` y `DOTNET.EXE` + dll, y `RunHelper` fuera de Windows → 1. |
| T6 | [x] | 33/33 en verde (V2). |
| P1 | [x] | `armar-tatana-portable.sh`: constantes `SCRCPY_VERSION="4.1"` / `SCRCPY_WIN64_SHA256` con el comentario de cómo actualizarlas, paso nuevo `[4/7]` (descarga, `shasum -a 256 -c`, copia sin `adb.exe`/`AdbWin*.dll`, `THIRD-PARTY-NOTICES.txt` en CRLF) y renumeración a `/7`. |
| P2 | [x] | Chequeo del zip final con `unzip -Z1`: los 8 requeridos y la ausencia de `tools/scrcpy/adb.exe` y `AdbWin*.dll`. Si todo está bien imprime `OK  contenido del zip verificado`. |
| P3 | [x] | `install-portable.bat`: corta con `exit /b 1` si `xcopy` falla. La línea nueva es ASCII (ver D-7). |
| P4 | [x] | `_comun.ps1` → `Install-TatanaPortable` cierra todo proceso cuyo ejecutable esté bajo `%LOCALAPPDATA%\Programs\Tatana\` (código de §8.3). |
| P5 | [x] | `_comun.ps1` → `Get-LineasHerramientasTatana`. `diagnostico.ps1` la imprime bajo `'  Herramientas de Tatana:'` con `Write-Ok`/`Write-Falla`, usando `Get-PropiedadSegura` y sin `?.`. Se respetaron el BOM y el CRLF. |
| P6 | [x] | `comun.Tests.ps1`: `Describe 'Get-LineasHerramientasTatana'` con los 4 casos (`$null`, sin `tools`, todas OK con y sin `version` en el orden fijo, y `scrcpy` con `found=false`). |
| P7 | [x] | PSScriptAnalyzer 0 hallazgos; Pester 39/39 (V-PS). |
| P8 | [x] | `docs/instalacion-windows.md`: §8 "Comprobar Tatana" (OK/FALTA, cómo arreglarlo, `MicDevice`) y §14 (scrcpy con versión fija y SHA-256, sin adb propio, licencias, chequeo del zip). |
| P9 | [x] | Este archivo. |

## Archivos

**Nuevos**

| Archivo | Qué |
|---|---|
| `server/src/Factum.Agent/Common/ToolResolver.cs` | Helper común de resolución de herramientas (§6.1). |
| `server/src/Factum.Agent/Common/WindowsConsoleSignal.cs` | Auxiliar `--ctrl-c <pid>`, P/Invoke, `BuildHelperCommand` y DP12. |
| `server/src/Factum.Agent/Common/ProcessStop.cs` | `StopGracefullyAsync` (Ctrl+C/SIGINT → espera → Kill). |
| `server/src/Factum.Agent/Services/MicCapture.cs` | `MicCapture`, `DshowDevices` y `LineTail` (buffer circular thread-safe, compartido con scrcpy). |
| `server/src/Factum.Agent/Services/ToolInventory.cs` | `ToolStatus`, `ToolInventory` y `VersionParser`. |
| `server/tests/Factum.Agent.Tests/{Factum.Agent.Tests.csproj, ToolResolverTests.cs, DshowDevicesTests.cs, VersionParserTests.cs, WindowsConsoleSignalTests.cs}` | Tests T1–T5. |

**Modificados**

| Archivo | Qué |
|---|---|
| `server/src/Factum.Agent/Factum.Agent.csproj` | `InternalsVisibleTo`. |
| `server/src/Factum.Agent/Program.cs` | Rama `--ctrl-c`, DP12, singleton `ToolInventory` y `WarmUp()`. |
| `server/src/Factum.Agent/Models/AgentModels.cs` | `AgentOptions.MicDevice`. |
| `server/src/Factum.Agent/Controllers/HealthController.cs` | Inyecta `ToolInventory`; `tools = inventory.Snapshot()`. |
| `server/src/Factum.Agent/Common/ProcessRunner.cs` | Nuevo `RunArgumentListAsync` (ver D-3). Los métodos existentes no cambian. |
| `server/src/Factum.Agent/Services/AdbService.cs` | Resolución de herramientas, start/stop de grabación (§6.2/§6.3) y mic con `MicCapture`. |
| `server/src/Factum.Agent/Services/IosService.cs` | Resolución de herramientas y `with_mic` con `MicCapture` (B4/B12). |
| `deploy/windows/armar-tatana-portable.sh` | Paso de scrcpy y chequeo del zip (P1/P2). |
| `packaging/portable/install-portable.bat` | Corte si `xcopy` falla (P3). |
| `deploy/windows/scripts/_comun.ps1` | `Install-TatanaPortable` (P4) y `Get-LineasHerramientasTatana` (P5). |
| `deploy/windows/scripts/diagnostico.ps1` | Sección "Herramientas de Tatana" (P5). |
| `deploy/windows/tests/comun.Tests.ps1` | P6. |
| `docs/instalacion-windows.md` | P8. |

`WebcamService.cs` no se tocó (DP13).

## Contrato compartido: coincide con la SDD (§5)

- **Errores de grabación:** siguen siendo HTTP 500 con `{ "error": "<texto>" }` (`RecordingController`, sin cambios).
  Los textos son los de §4.3, copiados literalmente: E1 (más ` En la Mac: brew install scrcpy.` solo en macOS), E2
  `scrcpy no pudo iniciar la grabación: …` (hasta 3 líneas unidas con ` · `, o `código de salida <n>`), E3, E4, E5,
  E6 `No se pudo abrir el micrófono de la PC: …`, E7 `No hay una grabación en curso.` (también en mock, ver D-4) y E8
  `scrcpy no generó el archivo de la grabación.` + líneas si hay.
- **`/health.tools`:** es un `Dictionary<string, ToolStatus>` con las claves `adb`, `scrcpy`, `ffmpeg` y `python`
  ya en minúscula. Cada valor trae `found` (siempre), y `source` (`"portable"` | `"path"` | `"homebrew"`, como string
  literal, no como enum), `path` y `version`, que se omiten si son null (`WhenWritingNull`). Comprobado contra la salida
  real en V4.
- `diagnostico.ps1` lee `tools.<k>.found/version/path` con `Get-PropiedadSegura` y aguanta que no venga `tools`
  (Tatana 1.0.0).

## Verificación en la Mac

### V1: `dotnet build server/src/Factum.Agent/Factum.Agent.csproj --no-incremental`
```
Compilación correcta.
    0 Advertencia(s)
    0 Errores
```
El proyecto de tests también da 0 advertencias y 0 errores con `--no-incremental`.

### V2: `dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj`
```
Correctas! - Con error:     0, Superado:    33, Omitido:     0, Total:    33, Duración: 35 ms - Factum.Agent.Tests.dll (net10.0)
```

### V3: `dotnet publish … -c Release -r win-x64 --self-contained true -o <scratch>/win`
Sin warnings (tampoco IL3000 ni CA1416). Genera `Factum.Agent.exe` (106 MB). La carpeta de scratch se borró.

### V4: agente en mock (`dotnet run … -- --mock --port 8799 --data <scratch>`) + `curl /health`
```json
{"status":"ok","version":"2.0.0","mock":true,"ios_available":true,"tools":{
 "adb":{"found":true,"source":"path","path":"/Users/joelmiguelserrudo/Library/Android/sdk/platform-tools/adb","version":"37.0.0-14910828"},
 "scrcpy":{"found":true,"source":"path","path":"/opt/homebrew/bin/scrcpy","version":"4.0"},
 "ffmpeg":{"found":true,"source":"path","path":"/opt/homebrew/bin/ffmpeg","version":"8.1.2"},
 "python":{"found":true,"source":"path","path":"/opt/homebrew/bin/python3","version":"3.14.7"}}}
```
- Tiempo de respuesta: 1.5 ms. Las versiones ya estaban en la primera llamada porque `WarmUp` corrió al arrancar.
- `source` da `"path"` y no `"homebrew"` porque `/opt/homebrew/bin` está en el PATH de esta Mac. La SDD acepta las
  dos.

### V5: `curl -XPOST localhost:8799/devices/X/record/stop` sin grabación
```
{"error":"No hay una grabación en curso."}
HTTP 500
```

### E2 real en la Mac (extra, de V9 c)
Agente sin mock (`Agent__Mock=false` por variable de entorno, sin tocar `appsettings.json`), `--data <scratch>` y
`POST /devices/NOEXISTE123/record/start`:
```
{"error":"scrcpy no pudo iniciar la grabación: ERROR: Could not find any ADB device · ERROR: Server connection failed"}
HTTP 500 0.18s
```
Esas dos líneas salieron como `warn` en el log del agente. Después, el stop devolvió E7. No se creó ningún archivo.

### V6: portátil desde un árbol limpio
`git archive HEAD` a scratch, con solo los 23 archivos de la HU copiados encima (`git diff --name-only` +
`git ls-files --others --exclude-standard`, sin `appsettings.json`). Después se corrió
`TMPDIR=<scratch>/tmp deploy/windows/armar-tatana-portable.sh --version 1.1.0-verif --src … --salida …`:
```
[1/7] Verificando Mock en la fuente...       OK  Agent.Mock = false
[2/7] Publicando Factum.Agent para win-x64…  OK  Agent.Mock = false (publish)
[3/7] Descargando adb…                       OK  platform-tools_r37.0.1-win.zip
[4/7] scrcpy 4.1 (Genymobile, Apache-2.0)... OK  scrcpy 4.1 (sin adb propio)
[5/7] Python embebido…                       OK  pymobiledevice3
[6/7] ffmpeg (BtbN win64 gpl) y uxplay...    AVISO: $UXPLAY_WIN_ARTIFACT_URL no está configurada (ya pasaba antes)
[7/7] Scripts del portátil y zip...
  OK  contenido del zip verificado
  OK  …/Tatana-Portable-v1.1.0-verif-Windows.zip (152M)
```

### V7: contenido del zip
- `tools/scrcpy/` trae: `scrcpy.exe`, `scrcpy-server`, `SDL3.dll`, `avcodec-62.dll`, `avformat-62.dll`,
  `avutil-60.dll`, `swresample-6.dll`, `libusb-1.0.dll`, `LICENSE.txt` (Apache 2.0), `THIRD-PARTY-NOTICES.txt`,
  `scrcpy.png`, `disconnected.png`, `scrcpy-noconsole.vbs` y `open_a_terminal_here.bat`.
- No hay `adb.exe` ni `AdbWin*` bajo `tools/scrcpy/`. Los únicos `adb.exe`/`AdbWin*.dll` del zip están en
  `tools/platform-tools/`.
- `THIRD-PARTY-NOTICES.txt` usa CRLF (`\r\n` comprobado con `od -c`) y dice `scrcpy 4.1` y
  `SHA-256 del zip: 5b12172b…65db`. También lista FFmpeg 8.1.2, SDL 3.4.12, libusb 1.0.30 y dav1d 1.5.3.
  `version.txt` = `1.1.0-verif`.
- **SHA alterado** (copia del script en scratch, con `5b1…` cambiado a `0b1…`): `exit=1` con
  `ERROR: el SHA-256 de scrcpy no coincide (¿descarga corrupta o release reemplazado?).`
- **Chequeo del zip, casos negativos** (extra): corrí el bloque del chequeo sobre dos zips armados a mano.
  - Un zip con `tools/scrcpy/AdbWinApi.dll` → `ERROR: el zip de Tatana trae el adb propio de scrcpy …` y `exit 1`.
  - Un zip sin `tools/ffmpeg/ffmpeg.exe` → `ERROR: al zip de Tatana le falta: tools/ffmpeg/ffmpeg.exe` y `exit 1`.
- Todo lo de V6/V7 (zip, árbol limpio, temporales, módulos de PowerShell) se borró de scratch al terminar.

### V-PS (P7): contenedor `mcr.microsoft.com/powershell:lts-ubuntu-22.04` (pwsh 7.4.7, `--platform linux/amd64`)
Los módulos se bajaron con `Save-Module` a scratch y se montaron en `/mods:ro`. El repo se montó en `/w:ro`, solo
lectura. Se usaron PSScriptAnalyzer 1.25.0 y Pester 5.x (`-MaximumVersion 5.99.99`).
- `Invoke-ScriptAnalyzer -Path /w/scripts -Recurse -Settings /w/scripts/PSScriptAnalyzerSettings.psd1 -Severity Warning,Error`
  → **0 hallazgos**.
- Parser de `_comun.ps1` y `diagnostico.ps1` → 0 errores.
- `Invoke-Pester /w/tests` → **39 passed, 0 failed** (35 que ya había + 4 nuevos).
- Nota: si se le pasa el analyzer también a `/w/tests`, aparecen `PSUseCompatibleCommands` sobre `Should -Be` en
  **todo** el archivo de tests. Es ruido, porque Pester no está en el perfil 5.1. La guía y las HU anteriores
  analizan solo `/w/scripts`.

## Decisiones no obvias

- **D-1. `entryAssemblyPath` sin `Assembly.Location`.** En `ProcessStop` se arma como
  `Path.Combine(AppContext.BaseDirectory, "Factum.Agent.dll")`. `Assembly.Location` da `""` en single-file y además
  dispara el warning **IL3000** en el publish. Solo se usa cuando el host es `dotnet` (desarrollo), donde el dll
  está ahí. `BuildHelperCommand` sigue siendo la función pura de la SDD.
- **D-2. `BuildHelperCommand` corta el nombre del exe por `\` y por `/` a mano.** `Path.GetFileNameWithoutExtension`
  en macOS/Linux no reconoce `\`, y los tests corren ahí con rutas de Windows.
- **D-3. `ProcessRunner.RunArgumentListAsync`.** Es un método nuevo con `ArgumentList` y UTF-8, para el mux y el
  remux. No cambié `RunAsync(IEnumerable)`, que une los argumentos con `QuoteArg`, porque lo usan los
  `adb shell …` y cambiar su citado está fuera de alcance.
- **D-4. E7 también en mock.** En mock, el stop sin grabación ahora devuelve `No hay una grabación en curso.` en
  lugar de `Sin grabación activa`. Hace falta para V5 y para que se vea el banner de V9 (a) del frontend.
- **D-5. Grabación "colgada".** Si scrcpy terminó solo (por ejemplo, se desenchufó el celular), el siguiente start
  no da E3. Antes de seguir, limpia el estado viejo y detiene el mic si seguía vivo (`DiscardStaleRecordingAsync`).
  Antes ese ffmpeg quedaba huérfano.
- **D-6. E2 elige qué líneas mostrar.** Usa las últimas 3 líneas `ERROR`/`WARN`, o las que mencionan
  `adb server version`/`doesn't match`. Si no hay ninguna, usa las últimas 3 líneas que haya escrito scrcpy, y si no
  escribió nada, `código de salida <n>`.
- **D-7. `install-portable.bat` sin `¿`.** El texto de §8.2 tiene `¿`, pero P3 pide ASCII sin tildes en los `echo`,
  así que la línea quedó `^(algun programa de Tatana sigue abierto?^)`. El archivo ya tenía un `—` en el `echo` de la
  línea 10 y en dos comentarios `rem ──`. Son anteriores a esta HU y no los toqué.
- **D-8. ffmpeg del mic con `-hide_banner -nostats`.** Así el buffer de stderr de E6 no se llena con las líneas de
  progreso. El resto de los argumentos son los de §6.4. `MicCapture.StopAsync` es idempotente (`Interlocked`).
- **D-9. Si el mux del mic falla, el `.mic.mka` se conserva al lado del MKV,** igual que antes con el `.m4a`. Es
  audio de evidencia y no conviene perderlo. Si el mux sale bien, se borra.
- **D-10. iOS: el mic se detiene antes de `StopDvtRecorderAsync`, no solo antes de burst.** Es lo que pide §6.4
  ("antes de los dos caminos de stop"). El mux usa `-shortest`, así que el efecto es nulo o de décimas de segundo.
- **D-11. `ToolEnvironment.Current` se arma en cada acceso, no se cachea.** Son lecturas de variables de entorno, y
  así un PATH que cambie mientras Tatana corre se toma sin reiniciar. `ffprobe` y `rife` ahora también se buscan
  primero en `tools/` (antes no tenían ruta portátil). Es inocuo.
- **D-12. `RunHelper` valida el pid.** Si no es un `uint` mayor que 0, devuelve 1. Si `AttachConsole` falla, sale
  con 2 sin generar el evento.

## Pendiente para la PC del estudio (prueba manual, usuario)

No se puede probar en la Mac: el Ctrl+C real de Windows (`AttachConsole` a la consola oculta de scrcpy, R1), `dshow`,
el `xcopy` con el adb de Tatana corriendo, PowerShell 5.1 real y la grabación con un Android conectado. El modo mock
no ejercita scrcpy. Guion de §11.2 de la SDD:

Requisitos: el paquete **Factum 1.1.0**, armado con `deploy/windows/armar-paquete.sh --version 1.1.0` **después del
merge** de esta HU y de `subida-archivos-grandes`; un Android 11+ (y, si hay, uno con Android 10 o anterior) con
depuración USB autorizada; VLC o "Películas y TV". Abreviaturas: `T=%LOCALAPPDATA%\Programs\Tatana`,
`D=%LOCALAPPDATA%\Tatana\data`.

1. **Actualizar.** Copiar el paquete y correr "Actualizar Factum". En el paso 9 tiene que preguntar
   `Hay una versión nueva de Tatana (1.1.0; instalada: 1.0.0)`: responder **S**. Tiene que aparecer
   `Cerrando N proceso(s) de Tatana…` y ningún error de `xcopy`. Después, en cmd, correr `dir "%T%\tools\scrcpy"`:
   tiene que tener `scrcpy.exe`, `scrcpy-server`, `LICENSE.txt` y `THIRD-PARTY-NOTICES.txt`, y **no** `adb.exe`.
   `type "%T%\version.txt"` tiene que dar `1.1.0`.
2. **Diagnóstico.** Correr "Diagnostico de Factum". En la sección Tatana, bajo "Herramientas de Tatana", tienen que
   aparecer `adb: OK`, `scrcpy: OK  4.1`, `ffmpeg: OK` y `python: OK`, cada una con su ruta bajo `%T%\tools`.
3. **(Opcional, aísla scrcpy de Tatana.)** Correr los comandos de "Notas de implementación" de la HU, con
   `set ADB=%T%\tools\platform-tools\adb.exe` y desde `%T%\tools\scrcpy`.
4. **Grabar sin mic (Android 11+).** En Factum, paso de captura: "Grabar". Reproducir un video con sonido en el
   celular (el audio se escucha en la PC, D12 b), esperar **30 s** y apretar "Detener". Lo esperado:
   - "Detener" responde en 3 s o menos y el archivo aparece en la bandeja.
   - En la ventana minimizada "Tatana Agent" aparece `scrcpy detenido limpiamente (exit 0)` y **no** aparece
     `se forzó el cierre`.
   - **Antes de "Subir y continuar"**, correr
     `"%T%\tools\ffmpeg\ffmpeg.exe" -hide_banner -i "%D%\<el grabacion_….mkv más nuevo>"`. Tiene que mostrar
     `Duration: 00:00:3x.xx` (**no** `N/A`), un stream `Video: h264` y uno `Audio: opus`. Si hay MediaInfo,
     "Duración" tiene que tener valor.
   - Abrirlo en VLC: se reproduce entero y se puede adelantar y retroceder con la barra.
5. **Android 10 o anterior** (si hay): grabar y detener. El `.mkv` tiene **solo** `Video:` y no aparece ningún
   error.
6. **Con mic de la PC.** Activar "con mic de PC", grabar 20 s hablando cerca de la PC y detener.
   - En el log tiene que aparecer `Micrófono de la PC: <nombre>`.
   - `ffmpeg -i` del archivo muestra `Audio: aac`, y al reproducirlo se escucha la voz.
   - Si tomó el micrófono equivocado, listar los micrófonos con
     `"%T%\tools\ffmpeg\ffmpeg.exe" -hide_banner -list_devices true -f dshow -i dummy` y fijar el correcto con
     `"Agent": { "MicDevice": "<nombre>" }` en `%T%\appsettings.Local.json`. Después, reiniciar Tatana.
7. **Estabilidad de adb.** Grabar **2 minutos** mirando la lista de dispositivos de Factum: el celular no tiene que
   desaparecer ni reaparecer. En el log de Tatana no tiene que haber ninguna línea con `adb server version`.
8. **Falta scrcpy.** Cerrar la ventana "Tatana Agent", renombrar `%T%\tools\scrcpy` a `scrcpy_x` y correr
   `%T%\launch-tatana.bat`.
   - "Grabar" tiene que mostrar el banner `No se encontró el componente de grabación de Android (scrcpy). Actualizá
     o reinstalá Tatana con el paquete de Factum.`, sin "brew".
   - Diagnóstico tiene que decir `scrcpy: FALTA`.
   - **Volver a renombrar la carpeta a `scrcpy`** y reiniciar Tatana.
9. **Falla al detener.** "Grabar", cerrar la ventana "Tatana Agent", relanzar `launch-tatana.bat` y apretar
   "Detener". Tiene que aparecer el banner `No se pudo guardar la grabación: No hay una grabación en curso.`
   Después, en el Administrador de tareas, **finalizar el `scrcpy.exe` huérfano** que dejó la grabación
   interrumpida (limitación conocida, R3).
10. **Si algo de 4 o 6 falla**, mandar al proveedor la salida de Diagnóstico, las últimas líneas de la ventana
    "Tatana Agent" y la salida de `ffmpeg -i` del archivo.

Riesgos a mirar (§11.3):
- **R1:** si en el paso 4 aparece `se forzó el cierre` + `MKV remuxeado tras cierre forzado`, el auxiliar de Ctrl+C
  no funcionó en esa PC. El archivo igual queda con duración, pero hay que reportarlo.
- **R2:** que el micrófono elegido no sea el correcto. Se arregla con `MicDevice`.
- **R3:** si Tatana muere con una grabación en curso, scrcpy queda huérfano. Ya pasaba antes; no es de esta HU.

Además, el paquete 1.1.0 lo arma el usuario o el orquestador después del merge (§8.5), no este implementador.
