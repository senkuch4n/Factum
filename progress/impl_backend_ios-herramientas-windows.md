# Implementación backend: ios-herramientas-windows (#7)

**Estado:** `done` (sin commit, como pidió el orquestador)
**Rama:** `feat/ios-herramientas-windows`
**SDD:** `Refactorizaciones/ios-herramientas-windows.md`, checklists §11.1, §11.2 y §11.3
**Implementador:** implementer-backend (Claude Opus 5.5), 2026-10-07

No se tocaron `client/`, `agent-ui/`, `Factum.Backend`, `progress/sesiones/`, ni
`server/src/Factum.Agent/appsettings.json` ni `appsettings.Local.json`. Ninguna prueba escribió en
Mongo, en `agent-data/` ni en `~/Factum/Evidencia`. No se ejecutó nada que modifique el iPhone
conectado: no se montó la DDI ni se usó `devmode` o `prepare` contra él (ver "Pruebas en la Mac").

---

## Archivos

### Tatana (`server/src/Factum.Agent`)

| Archivo | Ítem | Qué |
|---|---|---|
| `Common/ToolResolver.cs` | B1 | Se borró `AgentTools.Ffprobe` (D9 a). |
| `Common/ProcessRunner.cs` | B2 | `RunArgumentListAsync(..., environment = null, timeout = null)`: dos parámetros aditivos. Con `timeout`, mata el árbol y devuelve `ExitCode = -2`. Los llamadores de antes no cambian. |
| `Common/AgentErrorCodes.cs` | B4 | Los 12 códigos de §4.1. |
| `Services/Ios/IosErrors.cs` (nuevo) | B3 | `IosException(Code, Message)`, los textos exactos de §4.1 por SO (`MessageFor`), `IsDefinitive` (§6.3), `IsDefinitiveForRecording` (§6.7), `TryParseHelperError`, `FromHelper` y `RecordingEmptyFrom` (motivo en minúscula inicial). |
| `Services/Ios/ios_helper.py` (nuevo) | B5 | Helper único con 5 subcomandos (`devices`, `prepare`, `screenshot`, `record`, `devmode`), clasificación de excepciones recorriendo la cadena de causas, `TATANA_ERROR` y salidas 0/2/3. Usa `UserspaceRsdTunnel(serial=udid)` en iOS 17+ y `--synthetic` con Pillow. Para por stdin (`stop` o EOF) desde un hilo daemon y siempre sale con `os._exit`. No usa `signal` ni separadores de PATH. |
| `Factum.Agent.csproj` | B5 | `EmbeddedResource` con `LogicalName="Factum.Agent.Services.Ios.ios_helper.py"`. |
| `Services/Ios/IosHelper.cs` (nuevo) | B6 | Recurso → `%TEMP%/tatana/ios_helper_<sha8>.py` (escritura atómica, no lo reescribe), `ResolvePython()` (ToolResolver y después los candidatos de siempre, con caché), `PythonEnvironment` y `CreateStartInfo` (ArgumentList, UTF-8, stdin sin BOM). `RunAsync` devuelve el stdout o lanza `IosException`. `ExpectedPymobiledevice3Version = "10.7.4"`. |
| `Services/Ios/AppleServiceProbe.cs` (nuevo) | B7 | TCP a `127.0.0.1:27015` con 500 ms en Windows y `/var/run/usbmuxd` en Unix. Caché de 5 s con reloj inyectable. `ToJson` → `ok`/`missing`/`unknown`. |
| `Services/Ios/DvtRecorder.cs` (nuevo) | B8 | Espera `READY` hasta 30 s, drena stderr en segundo plano (50 líneas, `DONE` y `TATANA_ERROR`) y para con `stop` por stdin. Espera 30 s y después mata el árbol. Devuelve `DvtStopResult(Ok, Frames, Error, ElapsedMs)`. |
| `Services/IosService.cs` | B9–B14, B20 | Refactor (detalle abajo). |
| `Services/ToolInventory.cs` | B15 | Clave `uxplay` sin versión. `ToolStatus.Pymobiledevice3Version` con `[property: JsonPropertyName("pymobiledevice3_version")]`, leída en segundo plano y cacheada por ruta. Agrega `VersionParser.Pymobiledevice3` y el Warning `pymobiledevice3 {Version} en {Path}; la versión probada es 10.7.4`. |
| `Controllers/HealthController.cs` | B16 | Pasa a ser async. Suma el bloque `ios { apple_service, airplay_available, airplay_unavailable_reason }` y la capacidad `ios_developer_mode_v1`. En mock, `apple_service = "ok"`. |
| `Controllers/IosDeveloperModeController.cs` (nuevo) | B17 | `POST /devices/{serial}/ios/developer-mode` → `{ status }` o 500 `{ error, code }`. |
| `Controllers/{Screenshot,Recording,AirplayShot}Controller.cs` | B18 | `catch (IosException ex)` → `StatusCode(500, new { error = ex.Message, code = ex.Code })`, antes del catch genérico. |
| `Services/Ios/IosSelfTest.cs` (nuevo) + `Program.cs` | B19 | `--autoprueba-ios <carpeta>` antes de crear el host, con los 8 pasos de §6.9. `Program.cs` también registra `AppleServiceProbe` como singleton. |

**`IosService`:**

- Se borraron `PythonCandidates`, `DvtScreenshotScript`, `DvtRecorderScript` y `EnsureDvt*Script`.
- `IsAvailable` = `ResolvePython() is not null`.
- `ListDevicesAsync` corre la sonda y después `helper devices` (20 s). Si falta el servicio, no lanza Python y avisa una sola vez. El caché `_prepared` se invalida solo con un listado exitoso.
- `EnsurePreparedAsync` (`prepare`, 120 s) y `EnableDeveloperModeAsync` (`devmode`, 60 s; `restarting` invalida `_prepared`).
- `TakeScreenshotAsync` sigue la cadena de §6.3: un error definitivo sale sin fallback, qvh y AirPlay se intentan solo si existen, y al final se lanza el error de DVT.
- Grabación: `TryStartDvtRecorderAsync` usa `DvtRecorder`. Un error definitivo cae como 500 en el start; `tunnel_failed` o `capture_failed` caen al burst con `StartError`. El stop por stdin borra el parcial y lanza `ios_recording_empty` si no salió bien. Un burst sin cuadros también lanza `ios_recording_empty`.
- AirPlay: `AirplayAvailable` y `AirplayUnavailableReason`; en Windows es siempre `false` con `not_supported_on_windows`. Hay `airplay_unavailable` en los tres puntos (foto `airplay`, sesión de capturas y grabación). `GracefulStopAsync` usa `ProcessStop` en Windows. `DYLD_LIBRARY_PATH` solo se setea en macOS.
- `on_device` corre `-m pymobiledevice3 afc …` con ArgumentList y entorno UTF-8. Un `afc pull` que falla se clasifica con `ClassifyAfcError`.

### Tests (`server/tests/Factum.Agent.Tests`)

| Archivo | Ítem |
|---|---|
| `IosErrorsTests.cs` (nuevo) | T1: 10 códigos × 2 SO con texto exacto; `capture_failed`/`recording_empty` con detalle; ningún texto de Windows con `brew`/`Terminal`/`/usr/`; definitivos; `TATANA_ERROR` válido, roto, sin code, código desconocido y `é`. |
| `AppleServiceProbeTests.cs` (nuevo) | T2: Ok / Missing / Unknown, rama Unix, caché de 5 s con reloj inyectado, JSON. |
| `VersionParserTests.cs` | T3: `Pymobiledevice3("10.7.4\n")`, con espacios, `.dev+g…`, vacío y traceback. |
| `IosHelperTests.cs` (nuevo) | T4: recurso embebido sin `add_signal_handler`, sin `split(':')` y sin `import establish_userspace_rsd`; nombre con sha8 y sin reescritura; `CreateStartInfo` con entorno, UTF-8, rutas con espacios y acentos como un solo argumento, stdin sin BOM; errores desde stderr; `DONE`; listado con `é`; `ClassifyAfcError`. T5: `ToolStatus` serializa `pymobiledevice3_version` con las opciones de `Program.cs`. También verifica que `Ffprobe` ya no existe. |
| `DvtRecorderTests.cs` (nuevo) | T6: `DvtRecorder` real con `--synthetic`, ruta con espacio y acento, stop en menos de 10 s, cuadros > 0 y MP4 > 0 bytes. Agrega el caso sin ffmpeg → `ios_tools_missing`. Usa `[SyntheticRecorderFact]`, que marca `Skip` con el motivo si no hay python3 con Pillow o ffmpeg. En la Mac **corrió** (no se salteó). |

### Empaquetado, CI y guía

| Archivo | Ítem | Qué |
|---|---|---|
| `packaging/portable/ios-win/requirements.in` (nuevo) | P1 | `pymobiledevice3==10.7.4` + `pywin32`, `av>=14.0.0`, `lzfse>=0.4.2`, `sslpsk-pmd3>=1.0.3`, `colorama>=0.4.4`, `win32-setctime>=1.0.0`, `pyreadline3`. |
| `packaging/portable/ios-win/hexdump.txt` (nuevo) | P1 | `hexdump==3.3 --hash=sha256:d781a43b…f20db` (sdist). |
| `packaging/portable/ios-win/generar-lock.py` (nuevo) | P1 | Arma el wheel de hexdump y corre un dry-run con `--report` para win_amd64/cp311. La clausura se evalúa con marcadores de Windows (`packaging` o `pip._vendor.packaging`) y el script falla si queda algo sin cubrir. `--check` no re-resuelve: hace un dry-run `--no-deps --require-hashes` del lock y compara versión y hash, pide `pymobiledevice3==10.7.4` y la clausura cubierta por el lock más hexdump. |
| `packaging/portable/ios-win/requirements-win.lock` (nuevo) | P2 | **97 paquetes** con hash. Versiones clave: **pymobiledevice3 10.7.4, pywin32 312, av 18.1.0, lzfse 0.4.2** (además sslpsk-pmd3 1.0.3, colorama 0.4.6, win32_setctime 1.2.0, pyreadline3 3.5.6, pillow 12.3.0, pmd-pytcp 0.3.7). |
| `deploy/windows/armar-tatana-portable.sh` | P3 | Paso 5 según §7.3: lock en `Lib/site-packages`, hexdump desde sdist, `TATANA-PYTHON-LOCK.txt` y `THIRD-PARTY-NOTICES.txt` en CRLF. Aviso nuevo de uxplay (D1). La lista `requerido` suma los 7 archivos de python-embed. Chequeo nuevo: `METADATA` dice `Version: 10.7.4`. Nota "funciona también en Linux". |
| `.github/workflows/tatana-windows.yml` (nuevo) | P4 | Según §8.1 y DP3: `workflow_dispatch` + PR con filtro `paths`. Job `armar` en ubuntu-24.04: checkout, setup-dotnet, setup-python 3.12, `generar-lock.py --check`, script y upload. Job `humo` en windows-latest: checkout sparse de `ci/tatana-windows`, download y `prueba-humo.ps1`. Si falla, sube los logs. |
| `ci/tatana-windows/prueba-humo.ps1` (nuevo) | P5 | Los pasos 1–9 de §8.2, con StrictMode y `Stop`; `Stop-Process` del proceso propio en un `finally`. |
| `.github/workflows/verificar.yml` | P6 | Paso `python3 -m py_compile …/ios_helper.py` después de los tests de Tatana. |
| `docs/instalacion-windows.md` §8 | P7 | Punto 1 (Apple Devices o iTunes, Factum avisa), punto 3 nuevo (botón, internet la primera vez, sin admin) y "AirPlay no está disponible en Tatana para Windows." en Herramientas. |

`deploy/cloud/armar-tatana-nube.sh`: **sin cambios**, como indica §7.4. El zip 1.3.0 se arma después del merge a `main`; eso no lo hace el implementador.

Acciones fijadas por SHA de un release verificado, comprobado con `gh api …/git/ref/tags/<tag>` (tipo `commit`):
- `actions/setup-python@5fda3b95a4ea91299a34e894583c3862153e4b97` (v7.0.0)
- `actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a` (v7.0.1)
- `actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c` (v8.0.1)
- `checkout` y `setup-dotnet`: los mismos SHA que `verificar.yml`.

---

## Verificación (salidas reales)

```
$ dotnet build server/src/Factum.Agent/Factum.Agent.csproj --no-incremental
    0 Advertencia(s)
    0 Errores

$ dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj
Correctas! - Con error: 0, Superado: 202, Omitido: 0, Total: 202, Duración: 2 s
  (los 2 warnings CS8714/CS8619 del proyecto de tests son de EvidenceZipTests.cs: no son de esta HU)

$ python3 -m py_compile server/src/Factum.Agent/Services/Ios/ios_helper.py   → OK

$ dotnet run --project server/src/Factum.Agent -- --autoprueba-ios "<scratchpad>/autoprueba"
OK  Python con pymobiledevice3: /opt/homebrew/bin/python3
OK  pymobiledevice3 10.7.4
OK  ffmpeg: /opt/homebrew/bin/ffmpeg
OK  imports: pymobiledevice3.remote.userspace_tunnel, …screenshot, …mobile_image_mounter, …amfi
OK  helper: /var/folders/…/T/tatana/ios_helper_9a6f30ab.py
info: iOS DVT recorder iniciado (~2 FPS)
info: iOS DVT recorder detenido en 84 ms (9 cuadros)
OK  grabación sintética: 9 cuadros, stop en 84 ms, 3226 bytes
OK  el MP4 se lee entero con ffmpeg
OK  captura sin iPhone: exit 2, ios_device_not_found, 540 ms
OK  servicio de Apple: ok
RESULTADO: OK            (exit 0)

$ bash -n deploy/windows/armar-tatana-portable.sh                          → OK
$ docker run koalaman/shellcheck:stable deploy/windows/armar-tatana-portable.sh deploy/cloud/armar-tatana-nube.sh → exit 0, sin hallazgos
$ docker run rhysd/actionlint:latest -no-color                             → exit 0, sin hallazgos
$ pwsh (PowerShell 7.6.6 como dotnet tool en el scratchpad) Parser.ParseFile(prueba-humo.ps1) → parse ok

$ python3 -I packaging/portable/ios-win/generar-lock.py
OK  packaging/portable/ios-win/requirements-win.lock: 97 paquetes
    pymobiledevice3 10.7.4 / pywin32 312 / av 18.1.0 / lzfse 0.4.2
$ python3 -I packaging/portable/ios-win/generar-lock.py --check
OK  …requirements-win.lock: 97 wheels con hash verificado, pymobiledevice3==10.7.4, clausura de Windows cubierta

$ ./ops/harness/verify.sh   → "Arnés OK." (client tsc limpio, agent-ui tsc limpio, Tatana build limpio), exit 0
```

### P8: armado del portátil en la Mac

Árbol limpio: `git archive HEAD | tar -x` en el scratchpad, más **solo** los archivos de esta HU en
`server/src/Factum.Agent/`, `packaging/` y `deploy/windows/`. El `appsettings.json` fue el
**commiteado**, no el modificado localmente. La salida fue a `<scratchpad>/dist`, nunca a
`deploy/*/dist`. No se publicó nada.

```
[1/7] … OK  Agent.Mock = false (fuente y publish)
[2/7] Publicando Factum.Agent para win-x64 (self-contained)...
[3/7] OK  platform-tools_r37.0.1-win.zip
[4/7] OK  scrcpy 4.1 (sin adb propio)
[5/7] Python embebido 3.11.9 + pymobiledevice3 10.7.4 (lock de Windows)...
  OK  pymobiledevice3 10.7.4 (lock de Windows)
[6/7] AVISO: el portátil de Windows sale sin AirPlay (decisión D1 de ios-herramientas-windows); ver packaging/windows-uxplay-build.md
[7/7] OK  contenido del zip verificado
  OK  …/Tatana-Portable-v0.0.0-ci-Windows.zip (209M)
```

- **Tamaño del zip: 209 MB** (208 520 851 bytes, 13 192 entradas). El primer armado, sin
  `--no-compile`, pesaba 225 MB y traía 4 315 `.pyc` de Python 3.14 de la Mac.
- `unzip -p … pymobiledevice3-10.7.4.dist-info/METADATA` → `Version: 10.7.4`. También están
  `win32/win32security.pyd`, `pywin32.pth`, `lzfse.cp311-win_amd64.pyd`, `hexdump-3.3.dist-info`,
  `LICENSE.txt` del embebido, `TATANA-PYTHON-LOCK.txt` y `THIRD-PARTY-NOTICES.txt`. No hay ffprobe
  en el zip.
- El `._pth` queda con `import site`, con CRLF.

### Pruebas en la Mac contra un Tatana de prueba (no el del usuario)

- **Real** (`Agent__Mock=false`, `--port 18765`, `--data` y `Agent__EvidenceDirectory` en el
  scratchpad). Se detuvo por PID.
  - `/health` devolvió `mock:false`, `tools.python.pymobiledevice3_version:"10.7.4"`,
    `tools.uxplay.found:true`, `ios:{apple_service:"ok", airplay_available:true}` y
    `capabilities:[case_evidence_v1, ios_developer_mode_v1]`.
  - `/devices` (1,26 s) listó el iPhone del usuario, con nombre, modelo `iPhone 14 Pro Max` e iOS
    `26.5.2`. Es lectura de lockdown, lo mismo que hacía el Tatana anterior.
  - `POST /devices/0000-HUMO/screenshot?platform=ios` → `500 {"error":"El iPhone no está conectado o no responde…","code":"ios_device_not_found"}`.
  - `record/start video_only` con el mismo UDID falso → `500 … ios_device_not_found` (definitivo, sin burst).
- **Mock** (`--mock`, `--port 18766`). Se detuvo por PID.
  - `/health` devolvió `ios:{apple_service:"ok", airplay_available:true}`.
  - `developer-mode` → `{"status":"enabled"}`.
  - screenshot y record start/stop respondieron como hoy.

---

## Contrato compartido (§5.1): coincide con la SDD

| JSON | Lado C# | Comprobado |
|---|---|---|
| `tools.python.pymobiledevice3_version` | `ToolStatus.Pymobiledevice3Version` + `JsonPropertyName` explícito | test T5 + `/health` real |
| `tools.uxplay` | clave `"uxplay"` en `ToolInventory.Tools` | `/health` real |
| `ios.apple_service` (`ok`/`missing`/`unknown`) | `AppleServiceProbe.ToJson` | tests + `/health` |
| `ios.airplay_available`, `ios.airplay_unavailable_reason` (`not_supported_on_windows`/`uxplay_not_found`; `null` no viaja) | `HealthController`, `IosService.AirplayAvailable/AirplayUnavailableReason` | `/health` real y mock |
| `capabilities` incluye `ios_developer_mode_v1` | `HealthController` | `/health` |
| error `{ error, code }` con los 12 códigos | `IosException` + controllers | tests + curl |
| `POST /devices/{serial}/ios/developer-mode` → `{ status }` | `IosDeveloperModeController` | mock |

Revisé el `client/src/lib/agent.ts` y el `useAgentIosStatus.ts` del implementador frontend: usan
exactamente estos nombres.

---

## Decisiones no obvias

1. **`ProcessRunner.RunArgumentListAsync` suma `timeout`**, además de `environment`. Sin eso, un
   helper colgado quedaba huérfano al vencer el plazo. Es aditivo, opcional y no cambia a los
   llamadores de antes.
2. **El helper no usa `asyncio.run`**: usa su propio loop y `os._exit`. `asyncio.run` espera las
   tareas y el executor de la pila de pymobiledevice3 al salir, y eso se puede colgar.
3. **Stop rápido**: el bucle de captura compite `get_screenshot()` contra el evento de stop, en vez
   de esperar hasta 5 s la captura en curso. En la Mac, el stop sintético tardó 84 ms.
4. **Grabación sin cuadros con un error**: el helper escribe `TATANA_ERROR` (el último error de
   captura) antes de `DONE`. Así `ios_recording_empty` lleva el motivo real, por ejemplo "el iPhone
   está bloqueado…".
5. **Motivos extra de `ios_recording_empty`** que la SDD no enumera:
   - "ffmpeg no pudo cerrar el archivo de video.": cuando hay cuadros pero ffmpeg salió distinto de 0.
   - "no se pudo armar el video con los cuadros capturados.": cuando falla el ffmpeg del burst.

   En los demás casos se usan los dos motivos de la SDD.
6. **Guarda final en `StopRecordingAsync`**: aplica también al modo `airplay` de la Mac. Si uxplay
   no dejó el MP4 (el iPhone nunca se conectó), ahora responde 500 `ios_recording_empty` en vez de
   200 con un archivo inexistente (§4.4 b).
7. **`_session` se fija solo cuando el start sale bien**. Antes quedaba fijada aunque el start
   fallara.
8. **iOS < 16 no tiene Modo Desarrollador**: `prepare` y `devmode` no consultan
   `DeveloperModeStatus` (en esa versión daría `False` siempre). `devmode` → `enabled`.
9. **`ImportError` → `ios_tools_missing`** en el helper (falta pymobiledevice3 o Pillow, o hay una
   API distinta).
10. **`ResolvePython()`**: si no encuentra Python, cachea `null` 60 s. Así `/health` y el polling
    no lanzan 7 procesos cada vez. Cuando lo encuentra, lo cachea para siempre.
11. **Burst**: sigue con el CLI de hoy (`developer core-device screen-capture screenshot
    --userspace`; ese comando no acepta `--udid`), ahora con ArgumentList y el entorno UTF-8. Suma
    500 ms de espera tras cada falla para no lanzar procesos sin parar.
12. **Lock**: incluye dependencias puras que pip agrega con los marcadores de la Mac (`pexpect`,
    `ptyprocess`, `daemonize`). Son inofensivas en Windows. Lo importante es que la clausura **con
    marcadores de Windows** queda cubierta, y eso lo verifica el script.
13. **Armado**:
    - `--no-compile` en los dos `pip install --target`, para no llevar `.pyc` de la versión de
      Python de la máquina que arma.
    - El chequeo de `import site` usa `grep -q '^import site'`: el `._pth` del embebido viene con
      CRLF y `grep -x` fallaba.
    - `TATANA-PYTHON-LOCK.txt` también va en CRLF.
14. **Workflow**:
    - El job `armar` corre además `generar-lock.py --check`, que es barato y falla antes de bajar
      300 MB.
    - El job `humo` sube solo `*.log` y la carpeta de la autoprueba si falla, no la app de 200 MB.
    - El checkout del job `humo` es sparse (`ci/tatana-windows`).
15. **Log `Error de iPhone {Code}: {Detail}`**: el Detail es el `detail` del helper, no la línea
    cruda. El stderr completo va a Debug.

---

## Queda para el CI y el piloto (no se puede probar en la Mac)

- **CI (§12.2)**: después del push, `gh workflow run tatana-windows.yml --ref feat/ios-herramientas-windows`.
  Ahí se confirma lo que solo puede probar Windows:
  - que `import win32security` funciona a través de `pywin32.pth` en `Lib\site-packages` (A4);
  - que `ios.apple_service = "missing"` en el runner;
  - que `/devices` responde en menos de 5 s;
  - que los errores salen con `code` y sin "brew";
  - que el grabador sintético para por stdin en Windows.

  Si `prueba-humo.ps1` falla por un detalle de PowerShell, conviene ajustarlo: solo se verificó su
  sintaxis, no se ejecutó.
- **Mac, regresión con el iPhone (§12.3)**: lo hace el usuario.
  - La primera captura va a correr `prepare`, que **monta la DDI** (hoy no está montada, A7). Eso
    lo deliberadamente no lo hice yo.
  - Probar los modos `video_only`, `with_mic`, `on_device` y `airplay`, y el botón "Activar Modo
    Desarrollador".
- **Windows, perito piloto (§12.3, DP2)**:
  - túnel en modo usuario sin admin en hardware real (D3, riesgo pmd-pytcp);
  - montaje del DDI con internet;
  - `manual_required` con un iPhone con código;
  - grabación de 60 s con stop en pocos segundos;
  - mensajes con el iPhone bloqueado o desenchufado.
- **Tatana del usuario**: el Tatana que el usuario tiene corriendo (`bin/Debug/net10.0/osx-arm64/Factum.Agent`,
  PID 31123) sigue con el código anterior hasta que lo reinicie. `dotnet build` actualizó los
  binarios de esa carpeta. No lo toqué.

## Bloqueos

Ninguno.
