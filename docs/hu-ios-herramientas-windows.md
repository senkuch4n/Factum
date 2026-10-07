# HU: Capturar un iPhone desde Tatana en Windows igual que en la Mac

**Slug:** `ios-herramientas-windows` · **Issue:** #7
**Origen:** desprendida de `grabacion-android-windows` (D11 b, 2026-10-02). Reabierta con contexto nuevo el
2026-10-07: Factum ya corre en la nube y los peritos usan Chrome/Edge en **Windows** con el Tatana portátil
"para la nube" (`deploy/cloud/armar-tatana-nube.sh` → `deploy/windows/armar-tatana-portable.sh`). El usuario
probó el flujo completo con iPhone en **macOS** y funciona; en Windows no.
**Apps afectadas (preliminar, lo confirma la SDD):** agente Tatana (`server/src/Factum.Agent`:
`Services/IosService.cs`, `Common/ToolResolver.cs`, `Services/ToolInventory.cs`, `Controllers/HealthController.cs`),
empaquetado (`deploy/windows/armar-tatana-portable.sh`, `packaging/`), CI (`.github/workflows/`) y, según las
dudas D5/D7/D8, `client/` (`src/lib/agent.ts`, `src/components/IOSModePicker.tsx`,
`src/components/usb-guide/data.ts`). **Sin cambios** en `Factum.Backend` ni en `agent-ui/`.
**Restricción:** el usuario **no tiene PC con Windows** (ver D2).

**Como** perito que usa Factum en la nube desde una PC con Windows y Tatana portátil
**quiero** sacar capturas y grabar la pantalla de un iPhone conectado por USB con los mismos modos que en la Mac
**para que** la evidencia de un iPhone quede en el caso sin depender de tener una Mac.

---

## Contexto

### Qué existe hoy (arqueología sobre `feat/ios-herramientas-windows` = `42fa825`)

**Lo que ya resolvió `grabacion-android-windows` (aprobada, v1.1.0) y sirve acá:**
- `Common/ToolResolver.cs`: búsqueda única `tools/<dir>/<exe>` junto al exe → PATH con PATHEXT → Homebrew (solo
  fuera de Windows). Tiene tests (`server/tests/Factum.Agent.Tests/ToolResolverTests.cs`). `IosService` ya lo usa
  para ffmpeg, qvh, uxplay y rife (`FindBinary`, L1343).
- `Services/MicCapture.cs`: micrófono de la PC con `dshow` en Windows y cierre limpio con `q` por stdin; lo
  comparte el modo iOS `with_mic` (L1152).
- `Common/ProcessStop.cs` + `Common/WindowsConsoleSignal.cs`: cierre limpio de **scrcpy** en Windows con un
  Ctrl+C real (auxiliar `Factum.Agent.exe --ctrl-c <pid>`). **`IosService` no lo usa** (ver abajo).
- `/health.tools` (`ToolInventory`) informa `adb`, `scrcpy`, `ffmpeg`, `python`. No informa `uxplay` ni el estado
  de pymobiledevice3 (solo el booleano `ios_available`).
- `client/src/lib/agent.ts`: `startRecording`/`stopRecording` ya leen el `error` del cuerpo (`readAgentError`).

**Qué trae el portátil de Windows** (`armar-tatana-portable.sh`, pasos 3-6):
`tools/platform-tools/` (adb, "latest"), `tools/scrcpy/` (4.1, SHA-256 fijo), `tools/python-embed/` (Python
3.11.9 embebido + `pymobiledevice3` **sin versión fija**: `pip install --target … pymobiledevice3`),
`tools/ffmpeg/ffmpeg.exe` (BtbN "latest"; del zip se copia **solo** `ffmpeg.exe`) y `tools/uxplay/` **solo si**
existe `$UXPLAY_WIN_ARTIFACT_URL` — al armar la v1.2.0 salió el AVISO de que no estaba, así que **el portátil
actual no trae uxplay**. `launch-tatana.bat` no agrega nada al PATH.

**El build de uxplay:** `packaging/windows-uxplay-build.md` describe un build manual con MSYS2 subido a un
registry de **GitLab** y consumido por el job `build-portable-win` de `.gitlab-ci.yml`, que **no corre** (el repo
está en GitHub). En `.github/workflows/` hay `verificar.yml` (build + tests + tsc, todo en `ubuntu-24.04`) y
`desplegar-produccion.yml` (también Ubuntu). **Nada compila ni prueba en Windows.**

### Inventario: cada herramienta, cómo se resuelve hoy en Windows y qué falla

| # | Herramienta / pieza | Para qué (iOS) | Cómo se resuelve en Windows | Qué falla |
|---|---|---|---|---|
| H1 | Python + pymobiledevice3 | Todo iOS: listar, info, foto, video, `on_device` | `PythonCandidates` (L46-58): primero `tools/python-embed/python.exe`, prueba `-c "import pymobiledevice3"` | **Ruta OK.** Riesgo: versión de pymobiledevice3 distinta de la de la Mac (no está fijada) y la API que usan los scripts (`establish_userspace_rsd`, `DvtProvider`, `Screenshot`) puede cambiar entre versiones (D6). |
| H2 | Servicio **Apple Mobile Device** (usbmuxd de Apple) | pymobiledevice3 lo necesita en Windows para ver el iPhone | No es de Tatana: viene con iTunes o con la app "Apple Devices" de la Microsoft Store | Sin él, `usbmux list` vuelve vacío y el iPhone **no aparece**, sin ningún aviso. La guía de la web (`usb-guide/data.ts` L23) y `docs/instalacion-windows.md` §8 lo piden, pero Tatana no lo detecta (D4). |
| H3 | Túnel iOS 17+ (`establish_userspace_rsd`) | Foto DVT y `video_only`/`with_mic` (DVT). Los iPhone actuales son iOS 17+ (el código habla de iOS 26.5) | Lo abre pymobiledevice3 desde los scripts embebidos | **Sin verificar en Windows.** Puede pedir privilegios de administrador o drivers; `instalacion-windows.md` §8.3 ya dice "algunas funciones necesitan admin" sin que nada lo resuelva (D3). |
| H4 | Modo Desarrollador + imagen de desarrollador (DDI) | DVT (foto y video) | La guía de la web manda a correr `/usr/bin/python3 -m pymobiledevice3 amfi enable-developer-mode` **"en la Terminal de la Mac"** (`usb-guide/data.ts` L64-71). Tatana no monta el DDI en ningún lado (no hay `mounter auto-mount` en el repo) | Un perito con Windows no tiene Mac ni ese Python. Si el DDI no está montado, DVT falla. En la Mac pudo haberlo montado Xcode o un comando manual; **a confirmar** (D5). |
| H5 | Script `DvtRecorderScript` (`video_only`, `with_mic`) | Graba ~2 FPS con capturas DVT → ffmpeg | Se escribe a `%TEMP%\ios_dvt_recorder.py` y se corre con H1 | **Falla seguro en Windows, por dos motivos:** (1) `loop.add_signal_handler(SIGTERM/SIGINT)` (L809-810) lanza `NotImplementedError` en Windows → el script muere antes de `READY`; (2) aunque pasara, `find_ffmpeg()` (L761-770) recorre el PATH con `:` y sin `.exe`, y el portátil no pone `tools\ffmpeg` en el PATH → `ffmpeg` no se encuentra. Tatana cae al **burst** (H6). |
| H6 | Fallback "burst" | Si DVT falla, una captura por proceso con `developer core-device screen-capture screenshot --userspace` | H1 + ffmpeg del portátil | Muy lento (un túnel por captura). Si no junta ningún frame, `StopRecordingAsync` devuelve igual la ruta de un MP4 **que no existe**: la web recibe un nombre de archivo y la descarga falla (pérdida silenciosa). |
| H7 | Cierre de procesos iOS: `GracefulStopAsync` (L901-918) | Detener DVT recorder (40 s), uxplay (30 s), qvh (10 s) | Lanza el ejecutable `kill -15/-2 <pid>`, **que no existe en Windows** | La excepción se traga, espera el timeout **entero** y hace `Kill()` (TerminateProcess). DVT: cada stop tarda ~40 s y el MP4 lo termina un ffmpeg huérfano mientras Tatana ya devolvió el archivo (carrera: puede subirse/interpolarse incompleto). uxplay: muere sin cerrar el MP4 → **grabación AirPlay ilegible**. |
| H8 | ffmpeg | Armar MP4 (burst), mux del mic, corrección A/V de AirPlay, extraer frames, variantes "Suavizado"/"MCI" | `ToolResolver` → `tools/ffmpeg/ffmpeg.exe` | **Ruta OK** en C#. Solo falla dentro del script Python (H5). |
| H9 | ffprobe | — | `AgentTools.Ffprobe` está declarado en `ToolResolver.cs` L34 | **No se usa en ningún lado** del código y no viaja (del zip de BtbN solo se copia `ffmpeg.exe`). Hoy no rompe nada (D9). |
| H10 | uxplay | Modo de grabación `airplay`, sesión "espejar para capturas" y último recurso de la foto `auto` | `ToolResolver` → `tools/uxplay/uxplay.exe` | **No viaja** (sin `UXPLAY_WIN_ARTIFACT_URL`). Los mensajes dicen *"Instalá uxplay: brew install uxplay"* (L384, L485, L672). Aunque viajara: cierre roto (H7), en Windows necesita el servicio **Bonjour** (mDNS) y permiso del **Firewall** para conexiones entrantes, la opción `-mp4` tiene que existir en el build de Windows (a confirmar contra la versión de la Mac), y UxPlay es **GPL-3.0** + GStreamer LGPL (obligaciones de fuente y licencias al redistribuir) (D1). |
| H11 | qvh | Foto "USB" (segundo intento de `auto`) | `AgentTools.Qvh` sin nombre de Windows | Sin build de Windows (conocido, abandonado upstream). En `auto` se saltea y sigue; su mensaje menciona la URL de GitHub. Fuera de alcance. |
| H12 | rife-ncnn-vulkan | Variante "RIFE" opcional del video DVT | `ToolResolver` | No viaja; se omite sin error. Fuera de alcance. |
| H13 | `on_device` (grabación nativa del iPhone) | Lista DCIM antes/después y baja el video con `afc` | Solo H1 + H2 (no usa túnel ni DVT) | **Debería funcionar** si H2 está; nunca se probó en Windows. |
| H14 | Foto de **webcam** por Tatana (`WebcamService`) | — (no es iOS) | `"ffmpeg"` pelado + `video="Integrated Camera"` fijo | La web **no la usa**: `agent.captureWebcam` no tiene llamadores; las fotos de webcam salen del navegador (`WebcamCaptureModal`, `getUserMedia`). Fuera de alcance (D9). |
| H15 | Errores que ve el perito | — | `agent.takeScreenshot`, `startAirplayShot`, `stopAirplayShot` (`agent.ts` L305-330) descartan el cuerpo | Ante cualquier falla de foto iOS el perito ve *"Error tomando screenshot"*, sin causa (D7). |
| H16 | Codificación de la salida de Python | Nombre del iPhone, mensajes de error | `ProcessRunner.RunAsync` no fija `StandardOutputEncoding`; Python en Windows escribe con la página de códigos local | Riesgo (a verificar): nombres con acentos ("iPhone de José") salen mal o rompen el JSON. |

**Qué pasa hoy, modo por modo, en una PC Windows (deducido del código, sin probar):**
- **Foto (`auto`)**: DVT (H3/H4; el script de foto no usa señales, así que *puede* andar) → qvh (no existe) →
  AirPlay (no existe, error con `brew`). El perito ve "Error tomando screenshot".
- **`video_only` / `with_mic`**: DVT muere siempre (H5) → burst lento o vacío (H6) → stop de ~5 s (burst) y,
  si no hubo frames, archivo inexistente.
- **`on_device`**: probablemente funciona (H13).
- **`airplay`** y "espejar para capturas": no hay uxplay (H10).

---

## Criterios de aceptación

```gherkin
Feature: Capturas y grabación de iPhone en Tatana para Windows

  Background:
    Given una PC con Windows 10/11 x64 con el Tatana portátil armado con esta HU
    And el servicio Apple Mobile Device instalado (app "Apple Devices" o iTunes)
    And un iPhone con iOS 17 o posterior, desbloqueado, conectado por USB y con "Confiar" aceptado
    And Factum abierto en Chrome o Edge (en la nube o local)

  Scenario: El iPhone aparece en Factum
    When el perito abre el paso de captura
    Then el iPhone aparece en la lista con su nombre, modelo y versión de iOS, con acentos bien escritos

  Scenario: Falta el servicio de Apple
    Given una PC sin Apple Mobile Device instalado
    When el perito conecta un iPhone
    Then Factum le indica que instale "Apple Devices" desde la Microsoft Store (o iTunes)
    And el mensaje no menciona brew, Terminal ni comandos de Mac

  Scenario: Preparar el iPhone (Modo Desarrollador) sin Mac
    Given un iPhone sin Modo Desarrollador activado
    When el perito sigue la guía de conexión de iPhone en una PC con Windows
    Then puede activar el Modo Desarrollador sin usar una Mac ni escribir comandos con rutas de Mac

  Scenario: Captura de pantalla del iPhone
    Given el iPhone con Modo Desarrollador activo
    When el perito pulsa "Captura"
    Then se genera un PNG de la pantalla del iPhone y aparece en la bandeja de evidencia del caso
    And no hace falta abrir Tatana como administrador, o si hace falta, Factum lo dice con un mensaje claro

  Scenario: Grabar "Solo pantalla"
    When el perito elige "Solo pantalla", graba 60 segundos y pulsa "Detener"
    Then se genera un MP4 con la pantalla del iPhone a ~2 FPS que se reproduce entero
    And el stop termina en pocos segundos (no los 40 s de espera de hoy)
    And Tatana entrega el archivo recién cuando ffmpeg terminó de escribirlo
    And más tarde aparecen las variantes "Suavizado" y "MCI" como en la Mac

  Scenario: Grabar "Pantalla + micrófono de PC"
    When el perito graba con ese modo hablando cerca de la PC
    Then el MP4 final tiene la pantalla del iPhone y el audio del micrófono de la PC

  Scenario: Grabación nativa del iPhone
    When el perito elige "Grabación nativa", graba desde el Centro de Control y pulsa "Detener" en Factum
    Then Tatana baja el video nuevo del iPhone y aparece en la bandeja de evidencia

  Scenario: Falla la grabación
    Given que la captura DVT no se pudo iniciar y tampoco se obtuvo ningún frame
    When el perito pulsa "Detener"
    Then Factum avisa que la grabación no se pudo guardar y por qué
    And no informa un archivo que no existe

  Scenario: AirPlay en Windows (según D1)
    Given un Tatana de Windows sin uxplay
    When el perito abre el selector "Modo de grabación iOS"
    Then "Espejo AirPlay" aparece deshabilitado con el motivo, o no aparece
    And la captura automática no intenta AirPlay ni muestra mensajes de brew

  Scenario: Mensajes de error de captura con causa
    When una captura de pantalla de iPhone falla
    Then Factum muestra el motivo que manda Tatana en castellano, no "Error tomando screenshot"

  Scenario: Herramientas de iOS en /health
    When se consulta /health de Tatana en Windows
    Then tools informa python con la versión de pymobiledevice3, y uxplay con found true o false

  Scenario: Verificación automática en Windows (según D2)
    When se arma el portátil
    Then un job de GitHub Actions en windows-latest descomprime el zip, arranca Tatana
    And comprueba que python, pymobiledevice3 y ffmpeg se resuelven desde tools\
    And comprueba que el grabador DVT arranca y se detiene limpio sin iPhone, dejando un MP4 válido de prueba

  Scenario: La Mac sigue igual
    Given Tatana en macOS con las herramientas de Homebrew
    When el desarrollador repite el flujo completo con iPhone que ya funcionaba
    Then el comportamiento es el mismo que antes de esta HU (incluido AirPlay)
```

---

## Datos que se registran

No aplica: no hay datos nuevos en la base. Los archivos siguen siendo los de hoy (`screenshot_*.png`,
`grabacion_ios_*.mp4` y variantes `_blend`/`_mci`, `.mov` de `on_device`) en `%LOCALAPPDATA%\Tatana\data`.
Cambio aditivo en el contrato de `/health` de Tatana según D8 (snake_case).

---

## Diseño UX/UI

- **App:** `client/` (web). `agent-ui/` sin cambios.
- **Flujo feliz:** sin pantallas nuevas; los botones de captura y grabación de iPhone funcionan como en la Mac.
- **Selector "Modo de grabación iOS"** (`IOSModePicker`): si Tatana informa que no tiene uxplay, la tarjeta
  "Espejo AirPlay" queda deshabilitada con la leyenda *"No disponible en esta PC"* (o se oculta, según D1).
- **Guía de conexión de iPhone** (`usb-guide`): el paso "Ejecutá el comando de activación en la Mac" se
  reemplaza, en Windows, por la variante que se decida en D5 (por ejemplo un botón *"Activar Modo
  Desarrollador"* que hace Tatana). El resto de los pasos no cambia.
- **Errores** (mensajes propuestos):
  - Sin servicio de Apple: *"No se encontró el servicio de dispositivos de Apple en esta PC. Instalá la app
    'Apple Devices' desde Microsoft Store (o iTunes) y volvé a conectar el iPhone."*
  - Foto/grabación DVT: el motivo que manda Tatana (Modo Desarrollador apagado, iPhone bloqueado, falta de
    permisos), en castellano y sin comandos de Mac.
  - Grabación sin frames: *"No se pudo guardar la grabación del iPhone: <motivo>"*.
- **Feedback del stop:** el indicador de "deteniendo" no debería durar más de unos segundos.

---

## Fuera de alcance

- qvh (foto "USB" sin Developer Mode) y rife-ncnn-vulkan en Windows.
- La foto de webcam por Tatana (`WebcamService`, sin uso desde la web).
- Mejorar los ~2 FPS de `video_only` (límite de iOS, igual que en la Mac).
- iPhone por Wi-Fi (salvo AirPlay, según D1), varios iPhone a la vez, iPad.
- `agent-ui/` (Tatana instalado con Electron) y el instalador NSIS de `.gitlab-ci.yml`.
- La versión fija "2.0.0" que reporta `/health`.
- Fijar versión/checksum de platform-tools y ffmpeg (siguen "latest"), salvo que se sume en D6.
- Redistribuir el instalador de Apple Mobile Device Support dentro del paquete (ver D4).

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- El cierre de los procesos iOS tiene que funcionar en Windows sin el ejecutable `kill`: o reusar
  `ProcessStop`/`WindowsConsoleSignal` (Ctrl+C + `KeyboardInterrupt` en Python) o un mecanismo multiplataforma
  (por ejemplo, orden de parada por stdin). Lo decide el architect; en la Mac el resultado tiene que ser el mismo.
- El script DVT tiene que recibir la ruta de ffmpeg desde Tatana (resuelta con `ToolResolver`) en vez de
  buscarla él, y no usar `loop.add_signal_handler` en Windows.
- Para H16, evaluar `PYTHONUTF8=1` / `PYTHONIOENCODING=utf-8` y `StandardOutputEncoding = UTF8` al invocar Python.
- Prueba manual de referencia en Windows (cmd, con el iPhone conectado), para separar "pymobiledevice3 anda"
  de "Tatana lo invoca bien":
  ```
  cd %LOCALAPPDATA%\Programs\Tatana\tools\python-embed
  python.exe -m pymobiledevice3 version
  python.exe -m pymobiledevice3 usbmux list
  python.exe -m pymobiledevice3 lockdown info
  python.exe -m pymobiledevice3 mounter auto-mount
  python.exe -m pymobiledevice3 developer dvt screenshot "%USERPROFILE%\Desktop\prueba.png" --tunnel ""
  ```
  (los subcomandos exactos los ajusta el architect a la versión fijada en D6; correr una vez sin y otra con
  "Ejecutar como administrador" para responder D3).
- Hay proyecto de tests `server/tests/Factum.Agent.Tests` (corre en `verificar.yml`): lo que se pueda probar
  sin iPhone (armado de argumentos, parada, detección del servicio de Apple) va ahí.

---

## Dudas para validar con el usuario

**D1. AirPlay (uxplay) en Windows.**
- a) Incluirlo en esta HU: compilar uxplay en un job de GitHub Actions `windows-latest` con MSYS2 (reemplaza el
  build manual de `windows-uxplay-build.md` y el registry de GitLab), publicarlo como asset de un release de
  GitHub con SHA-256 fijo que `armar-tatana-portable.sh` verifica, más Bonjour, regla de Firewall y los textos de
  licencia GPL-3.0/LGPL con la oferta de código fuente.
- b) Esta HU deja Windows **sin AirPlay pero prolijo** (la tarjeta aparece deshabilitada con el motivo, la foto
  `auto` no intenta AirPlay, sin mensajes de brew) y AirPlay en Windows pasa a una HU propia.
- c) Aceptar Windows sin AirPlay de forma permanente.
- **Recomendada: b)** — AirPlay suma cuatro incógnitas independientes (build, Bonjour, Firewall, licencia GPL) que
  no se pueden probar sin una PC con Windows, un iPhone y la misma red Wi-Fi; mezclarlas frena lo principal, que
  son los modos por USB. El modo `on_device` ("Recomendado" en el selector) ya da audio nativo sin AirPlay. La
  degradación prolija sirve igual si después se hace a).

**D2. Cómo verificar sin una PC con Windows.**
- a) Dos niveles: un job de GitHub Actions `windows-latest` que hace una prueba de humo del zip armado (Tatana
  arranca, `/health` resuelve python/pymobiledevice3/ffmpeg desde `tools\`, el grabador DVT arranca y para limpio
  con un origen de prueba y deja un MP4 válido) y, para lo que necesita el iPhone, un **perito piloto** con una
  checklist escrita (los escenarios Gherkin + los comandos de "Notas de implementación").
- b) Una VM de Windows en la Mac (por ejemplo Parallels, con el iPhone pasado por USB a la VM).
- c) Solo el perito piloto.
- **Recomendada: a)** — los runners de GitHub no tienen USB, pero sí prueban todo lo que hoy está roto sin iPhone
  (rutas, señales, cierre del MP4) y quedan como regresión. Lo que depende del iPhone y del túnel solo se puede
  confirmar en hardware real. Si tenés Parallels o podés conseguirlo, b) acelera mucho el ida y vuelta; decinos si
  es opción. **Necesitamos saber quién sería el perito piloto y con qué iPhone/iOS.**

**D3. iOS 17+ y el túnel de pymobiledevice3 (permisos).**
- a) Incluirlo (es obligatorio: foto y `video_only` dependen de DVT en cualquier iPhone actual). Primero se
  verifica si el túnel en modo usuario anda sin administrador; si no, Tatana lo detecta y Factum muestra un
  mensaje claro para abrir Tatana como administrador.
- b) Igual que a), pero si hace falta admin, el portátil arranca Tatana siempre elevado (UAC al iniciar sesión).
- c) Dejar iOS 17+ fuera y que en Windows solo ande `on_device`.
- **Recomendada: a)** — sin el túnel la HU no cumple su objetivo; pedir admin siempre (b) es invasivo para el
  perito y puede chocar con políticas de las PC de los estudios. La respuesta real sale de la primera prueba en
  hardware (D2).

**D4. Apple Mobile Device Support (iTunes / "Apple Devices").**
- a) Sigue siendo un requisito de la PC (ya documentado), pero Tatana detecta si falta el servicio y la web lo
  dice con un mensaje y el enlace a la Microsoft Store.
- b) Meter el instalador de Apple dentro del portátil.
- c) Solo documentarlo, como hoy.
- **Recomendada: a)** — hoy el iPhone simplemente no aparece y nadie sabe por qué. b) no es viable: la licencia de
  Apple no permite redistribuir ese instalador.

**D5. Modo Desarrollador y DDI sin Mac.**
- a) Tatana hace las dos cosas con su Python embebido: monta automáticamente la imagen de desarrollador antes de
  DVT (si no está montada) y expone una acción "Activar Modo Desarrollador" que la guía de la web usa con un botón
  en lugar del paso "Ejecutá el comando en la Mac".
- b) La guía muestra, en Windows, el comando con la ruta del Python del portátil para pegar en `cmd`.
- c) Dejarlo como está.
- **Recomendada: a)** — un perito con Windows no tiene Mac ni Terminal con ese Python; a) además sirve igual en la
  Mac. Pregunta para vos: **en tu Mac, ¿el DDI lo montó Xcode o algún comando a mano?** Si nunca hiciste nada, puede
  que pymobiledevice3 ya lo monte solo y la parte del DDI sobre.

**D6. Versión de pymobiledevice3 fija.**
- a) Fijar en el armado la misma versión que usás en la Mac (la confirma el architect con `pip show
  pymobiledevice3`), y registrarla en `/health`.
- b) Seguir bajando la última al armar.
- **Recomendada: a)** — los scripts embebidos usan APIs internas que cambian seguido; con "latest" la Mac y Windows
  pueden correr versiones distintas y un armado nuevo puede romper lo que andaba. Para evidencia forense conviene
  saber con qué versión se capturó (mismo criterio que scrcpy en D1 de `grabacion-android-windows`).

**D7. Mostrar el motivo real en las fallas de foto y AirPlay (toca `client/`).**
- a) `takeScreenshot` y las llamadas de "espejar para capturas" leen el `error` de Tatana (como ya hace
  `startRecording`), y Tatana deja de mencionar `brew` en Windows.
- b) Solo arreglar los mensajes en Tatana.
- **Recomendada: a)** — es lo mismo que se hizo para Android en la HU anterior; sin esto el perito en Windows nunca
  sabe si falta el servicio de Apple, el Modo Desarrollador o permisos.

**D8. `/health` con el estado de iOS.**
- a) Sumar a `tools` el bloque de `uxplay` (found sí/no) y la versión de pymobiledevice3 junto a `python`, y que la
  web lo use para habilitar/deshabilitar AirPlay (D1).
- b) No tocar `/health`.
- **Recomendada: a)** — es la forma de diagnosticar a distancia a un perito en la nube (no tiene el "Diagnóstico de
  Factum" de la instalación local) y la necesita la degradación de D1 b. Cambio aditivo.

**D9. ffprobe y "foto" en el título del issue.**
Del código surge que `ffprobe` hoy no se usa en ningún lado y que la web no usa la foto de webcam de Tatana.
- a) Interpretar "foto" como la **captura de pantalla del iPhone** (incluida), dejar fuera la foto de webcam de
  Tatana, no llevar `ffprobe.exe` y sacar su declaración sin uso.
- b) Llevar `ffprobe.exe` igual (ya viene en el zip de BtbN) por si se usa más adelante.
- **Recomendada: a)** — no agregar binarios que nada usa; si una HU futura lo necesita, se suma ahí. Confirmá que
  "foto" era la captura de pantalla del iPhone.

**D10. Versión y distribución del arreglo.**
- a) Publicar Tatana **1.3.0**: el zip "para la nube" (`armar-tatana-nube.sh`) en `/descargas` y avisar a los
  peritos que lo bajen (el portátil de la nube no se auto-actualiza, `UPDATE_URL` vacío); el paquete de
  instalación local se rearma solo si hay un estudio con instalación local que use iPhone.
- b) 1.2.1 solo para la nube.
- **Recomendada: a)** — cambia el contenido del portátil (versión fija de pymobiledevice3, scripts nuevos), no es un
  parche menor; y no hay que obligar a rearmar el paquete local si nadie lo necesita. Confirmá si hoy hay alguna
  instalación local en uso con iPhone.

## Validación del usuario (2026-10-07)

- **D1–D10:** se aceptan todas las opciones recomendadas (respuesta textual: "tomo todas las recomendaciones"). En particular,
  D1 = b: Windows queda sin AirPlay, pero con la tarjeta deshabilitada y explicando el motivo. Compilar uxplay va en otra HU.
