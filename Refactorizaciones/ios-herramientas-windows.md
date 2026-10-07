# SDD: Capturar un iPhone desde Tatana en Windows igual que en la Mac

**Slug:** `ios-herramientas-windows` · **Issue:** #7
**HU:** `docs/hu-ios-herramientas-windows.md`, validada el 2026-10-07: D1–D10 con la opción
recomendada. D1 = **b)**: Windows queda **sin AirPlay**, con la tarjeta deshabilitada y el motivo.
El paquete que lleva el arreglo es **Tatana 1.3.0** "para la nube" (D10).
**Rama:** `feat/ios-herramientas-windows`, desde `develop`. Va contra `develop`.
**Implementadores:** `implementer-backend` (agente Tatana, tests .NET, script Python embebido,
`deploy/`, `packaging/`, `.github/workflows/`, guía) e `implementer-frontend` (**solo `client/`**).
**No se toca `agent-ui/` ni `Factum.Backend`.**

> **Árbol de trabajo:** `server/src/Factum.Agent/appsettings.json`, `agent-ui/*.tsbuildinfo` y
> `progress/sesiones/` tienen cambios locales que **no son de esta HU**: no se stagean ni se
> editan. Para armar el portátil se usa siempre un árbol limpio (`git archive`), como hoy.

---

## 0. Hallazgos de la arqueología (cambian el diagnóstico de la HU)

Verificado en la Mac del usuario el 2026-10-07 (solo lectura):

| # | Hallazgo | Cómo se verificó | Consecuencia |
|---|---|---|---|
| A1 | **Tatana en la Mac usa pymobiledevice3 10.7.4** (Homebrew, Python 3.14). `/usr/bin/python3` tiene otra, la 9.34.0, que es la que nombra la guía de la web. | `ps eww` del `Factum.Agent` en ejecución: el PATH tiene `/opt/homebrew/bin` antes de `/usr/bin` → `python3` = `/opt/homebrew/bin/python3` → `pip show` = **10.7.4**. | D6: se fija **`pymobiledevice3==10.7.4`**. |
| A2 | **El portátil de Windows actual trae pymobiledevice3 1.0.0**, no la última. El `pip install --only-binary=:all: pymobiledevice3` del paso 5 de `armar-tatana-portable.sh` no puede instalar las versiones modernas: dependen de `hexdump`, que en PyPI **solo es sdist** (`hexdump-3.3.zip`). pip retrocede silenciosamente hasta una versión sin esa dependencia. | `pip install --dry-run --report … --platform win_amd64 --python-version 311 --only-binary=:all: pymobiledevice3` → `pymobiledevice3 1.0.0`. Con `==10.7.4` → `No matching distribution found for hexdump`. | **Esto explica casi todo lo que falla en Windows.** Los scripts embebidos usan una API (`establish_userspace_rsd`, `DvtProvider`) que la 1.0.0 no tiene. |
| A3 | `pip --platform` **no evalúa los marcadores de entorno para Windows**: los evalúa con los de la Mac. Las dependencias que solo aplican en Windows **no se instalan**: `pywin32` (pymobiledevice3 la importa en `osu/win_util.py` → `import win32security` apenas toca usbmux), `av`, `lzfse`, `sslpsk-pmd3`, `colorama`, `win32-setctime` y `pyreadline3`. | Clausura de `requires_dist` del reporte de pip con marcadores `win32`/3.11. | El armado tiene que usar un **lock explícito** con esas dependencias (§7.2). |
| A4 | `pywin32` necesita que se procese su `pywin32.pth` (suma `win32\`, `win32\lib` y el bootstrap de DLLs). Con `--target` en la raíz de `python-embed` eso queda librado a cómo el embebido trata los `.pth`. | Lectura de `pywin32` + el `._pth` del embebido. | Se instala en **`tools\python-embed\Lib\site-packages`**, que el `import site` del `._pth` procesa con sus `.pth`. Lo comprueba el CI (`import win32security`). |
| A5 | En 10.7.4 el túnel de iOS 17+ (`UserspaceRsdTunnel`) es **en modo usuario, sin administrador**: una pila TCP/IP en Python (`pmd-pytcp`) que dice soportar Windows en forma explícita (`io_backend.py`, `_packet_socketpair()` con rama Windows). | Lectura de `remote/userspace_tunnel.py` y `pmd_pytcp/lib/io_backend.py` (10.7.4). | D3: el camino esperado es **sin admin**. Igual se clasifica el error "acceso denegado" por si una PC lo pide (§6.3). |
| A6 | `establish_userspace_rsd()` (lo que usan los scripts de hoy) registra un `atexit` que hace `os._exit(0)`. **Por eso** el proceso de Python sale con código 0 aunque haya fallado (el comentario de `TakeDvtScreenshotAsync` lo describe sin saber la causa). `UserspaceRsdTunnel` como `async with` cierra limpio y no lo hace. | Lectura de `userspace_tunnel.py` L744-790. | El helper nuevo usa `UserspaceRsdTunnel(serial=udid)` y sale con códigos explícitos. |
| A7 | **D5, la pregunta al usuario:** en la Mac el DDI lo montó **un comando manual**: `~/.pymobiledevice3/Xcode_iOS_DDI_Personalized/` es del 2026-07-08 y solo el CLI `mounter auto-mount` lo descarga. Hoy, con el iPhone conectado (iPhone15,3, iOS 26.5.2, Modo Desarrollador **activo**), `mounter list` devuelve **`[]`**: el DDI **no está montado** (se desmonta en cada reinicio del iPhone). | `amfi developer-mode-status` → `true`; `mounter list` → `[]` (consultas de solo lectura al iPhone). | El montaje automático (D5) hace falta **también en la Mac**. Tatana lo hace una vez por iPhone conectado (§6.4). |
| A8 | El montaje del DDI personalizado (iOS 17+) **necesita internet**: la imagen (~16 MB) se baja una vez de GitHub (repositorio de pymobiledevice3) y queda en `%USERPROFILE%\.pymobiledevice3\`; cada montaje pide la firma al servidor TSS de Apple. | Lectura de `mobile_image_mounter.fetch_personalized_ddi` y `auto_mount_personalized`. | Los peritos de la nube tienen internet. El error se clasifica (`ios_ddi_mount_failed`) con un mensaje que lo dice. |
| A9 | `amfi enable-developer-mode` **falla si el iPhone tiene código** (`DeviceHasPasscodeSetError`). Lo que sí funciona siempre es `reveal_developer_mode_option_in_ui()`: hace visible el interruptor en Ajustes. | Lectura de `services/amfi.py`. | La acción "Activar Modo Desarrollador" (D5) intenta activar y, si hay código, revela el interruptor y le dice al perito cómo prenderlo (§4.3). |
| A10 | El servicio de Apple en Windows escucha en `127.0.0.1:27015` (`usbmux.ITUNES_HOST`), que es exactamente lo que usa pymobiledevice3. | `usbmux.py` L39. | D4 se detecta con una conexión TCP local, sin admin ni paquetes nuevos (§6.5). |

---

## 1. Resumen funcional

Un perito que usa Factum en la nube desde una PC con Windows y el Tatana portátil puede sacar
capturas de pantalla de un iPhone (iOS 17+) y grabarlo en los modos `video_only`, `with_mic` y
`on_device`, igual que en la Mac. Tatana 1.3.0 trae **pymobiledevice3 10.7.4 de verdad** (hoy trae
la 1.0.0) con sus dependencias de Windows. Los scripts de Python que usa Tatana pasan a ser **un
solo helper** multiplataforma que recibe la ruta de ffmpeg, se detiene por stdin en vez de con
señales Unix y reporta los errores con un código. Antes del primer uso de un iPhone, Tatana
comprueba el Modo Desarrollador y monta la imagen de desarrollador sola, en Windows y en la Mac.
Tatana detecta si falta el servicio de Apple, informa en `/health` la versión de pymobiledevice3,
el estado de uxplay y el del servicio, y en Windows declara AirPlay como no disponible. La web
muestra el motivo real de cada falla, avisa si falta "Apple Devices", deshabilita "Espejo AirPlay"
y "Espejar para capturas" con el motivo, y la guía de conexión del iPhone reemplaza el comando de
Terminal de la Mac por un botón **"Activar Modo Desarrollador"**. Un workflow de GitHub Actions arma
el portátil y le hace una prueba de humo en `windows-latest`, sin iPhone. Una grabación sin frames
ya no devuelve un archivo inexistente.

## 2. Toca

| Parte | ¿Toca? | Qué |
|---|---|---|
| backend (API, `Factum.Backend`) | **no** | — |
| backend (Tatana, `Factum.Agent`) | **sí** | helper Python embebido, `IosService` (refactor), detección del servicio de Apple, montaje del DDI, Modo Desarrollador, errores con código, `/health`, modo `--autoprueba-ios`, tests. |
| empaquetado (`deploy/windows/`, `packaging/portable/`) | **sí** (backend) | lock de pymobiledevice3 10.7.4 + dependencias de Windows, `Lib\site-packages`, `hexdump`, avisos de licencia, chequeos del zip. |
| CI (`.github/workflows/`) | **sí** (backend) | workflow nuevo `tatana-windows.yml` + `py_compile` en `verificar.yml`. |
| client (`client/`) | **sí** | `agent.ts`, `agent-messages.ts`, hook nuevo, `DeviceConnect`, `IOSModePicker`, `CaptureStep`, `dashboard/page.tsx`, guía `usb-guide`. |
| agent-ui (`agent-ui/`) | **no** | — |

## 3. Modelo de datos

No aplica: no hay colecciones ni documentos de Mongo nuevos ni modificados. Los archivos de
evidencia son los de hoy (`screenshot_*.png`, `grabacion_ios_*.mp4` y `_blend`/`_mci`, `.mov` de
`on_device`) en `Agent:DataDirectory` (`%LOCALAPPDATA%\Tatana\data`). **Ninguna prueba de esta HU
escribe en la base.** La autoprueba y el CI escriben solo en carpetas temporales propias.

Archivos nuevos en la PC del perito (no son evidencia):
- `%TEMP%\tatana\ios_helper_<sha8>.py`: el helper, escrito al arrancar (§6.1).
- `%USERPROFILE%\.pymobiledevice3\Xcode_iOS_DDI_Personalized\`: caché del DDI que maneja
  pymobiledevice3 (~16 MB, una vez por PC).

---

## 4. Endpoints de Tatana (API local `localhost:8765`)

Serialización: `JsonNamingPolicy.SnakeCaseLower` + `WhenWritingNull` (`Program.cs`). Los nombres
de esta sección son **los que viajan en el JSON**.

### 4.1 Errores de iOS: `{ error, code }` (aditivo)

Toda respuesta de error de las rutas de iOS sigue con **HTTP 500** y `error` (texto en castellano,
como hoy), y **suma `code`**:

```json
{ "error": "El Modo Desarrollador del iPhone está apagado. …", "code": "ios_developer_mode_disabled" }
```

Rutas alcanzadas: `POST /devices/{serial}/screenshot?platform=ios`,
`POST /devices/{serial}/record/start` (iOS), `POST /devices/{serial}/record/stop?platform=ios`,
`POST /devices/{serial}/screenshot/airplay/{start|mark|stop}` y la nueva de §4.3. Un error sin
clasificar sigue saliendo sin `code` (como hoy).

**Códigos y textos exactos** (`Services/Ios/IosErrors.cs`; los textos los muestra la web tal cual):

| `code` | Cuándo | `error` (Windows) | `error` (Mac, si difiere) |
|---|---|---|---|
| `ios_apple_service_missing` | No se puede conectar con usbmux (`ConnectionFailedToUsbmuxdError`, `ConnectionRefusedError` a 27015) o la sonda de §6.5 dice `missing` | `No se encontró el servicio de dispositivos de Apple en esta PC. Instalá la app "Apple Devices" desde Microsoft Store (o iTunes) y volvé a conectar el iPhone.` | `No se pudo hablar con el servicio de dispositivos de macOS (usbmuxd). Desconectá y volvé a conectar el iPhone.` |
| `ios_device_not_found` | `NoDeviceConnectedError`, `DeviceNotFoundError`, `NotConnectedError`, `ConnectionTerminatedError` | `El iPhone no está conectado o no responde. Revisá el cable USB y que el iPhone esté desbloqueado.` | igual |
| `ios_not_trusted` | `NotTrustedError`, `NotPairedError`, `PairingDialogResponsePendingError`, `UserDeniedPairingError`, `InvalidHostIDError`, `PairingError` | `El iPhone no confía en esta PC. Desbloquealo, tocá "Confiar" e ingresá el código.` | igual |
| `ios_locked` | `PasscodeRequiredError`, `PasswordRequiredError` | `El iPhone está bloqueado. Desbloquealo y reintentá.` | igual |
| `ios_developer_mode_disabled` | `DeveloperModeIsNotEnabledError`, `get_developer_mode_status() == False`, `InvalidServiceError` sobre `com.apple.instruments.*`/`dtservicehub` | `El Modo Desarrollador del iPhone está apagado. Activalo desde la guía de conexión del iPhone (botón "Activar Modo Desarrollador") y reintentá.` | igual |
| `ios_ddi_mount_failed` | `DeveloperDiskImageNotFoundError`, `urllib.error.URLError`, cualquier excepción del montaje | `No se pudo preparar el iPhone para las capturas (imagen de desarrollador). Revisá que esta PC tenga internet y que el iPhone esté desbloqueado, y reintentá.` | igual |
| `ios_tunnel_failed` | `UserspaceTunnelUnavailableError`, `TunneldConnectionError`, falla de `UserspaceRsdTunnel.aopen()` no clasificada arriba | `No se pudo abrir la conexión de servicios con el iPhone. Desconectá y volvé a conectar el cable con el iPhone desbloqueado.` | igual |
| `ios_admin_required` | `AccessDeniedError`, `PermissionError`, `OSError` con `winerror == 5` | `Windows no dejó a Tatana abrir la conexión con el iPhone. Cerrá Tatana y abrilo con clic derecho → "Ejecutar como administrador".` | `macOS no dejó a Tatana abrir la conexión con el iPhone. Revisá los permisos y reintentá.` |
| `ios_tools_missing` | No hay Python con pymobiledevice3, o el helper no encuentra ffmpeg | `Tatana no encuentra sus herramientas de iPhone (Python o ffmpeg). Descargá e instalá de nuevo Tatana.` | `Tatana no encuentra Python con pymobiledevice3 o ffmpeg en esta Mac.` |
| `ios_capture_failed` | Cualquier otra excepción del helper | `No se pudo capturar la pantalla del iPhone: <detalle>` | igual |
| `ios_recording_empty` | El stop no obtuvo un MP4 completo (0 frames, helper sin `DONE`, ffmpeg falló o hubo que matarlo) | `No se pudo guardar la grabación del iPhone: <motivo>` | igual |
| `airplay_unavailable` | Modo `airplay`, "espejar para capturas" o `method=airplay` sin uxplay | `AirPlay no está disponible en Tatana para Windows. Usá "Solo pantalla", "Pantalla + micrófono de PC" o "Grabación nativa del iPhone".` | `uxplay no encontrado. Instalá: brew install uxplay` (como hoy) |

- `<detalle>`: el `detail` que manda el helper (§6.2), recortado a 200 caracteres, sin traceback.
  El stderr completo va **solo al log** de Tatana.
- `<motivo>` de `ios_recording_empty`: el `error` del código que causó la falla, en minúscula
  inicial (p. ej. `no se pudo guardar la grabación del iPhone: el iPhone está bloqueado…`). Si no
  hay código: `no se capturó ningún cuadro de la pantalla.` o
  `la grabación no terminó de escribirse a tiempo.`
- Ningún texto de Windows menciona `brew`, Terminal, `/usr/bin` ni comandos.

### 4.2 `GET /health` (aditivo)

```jsonc
{
  "status": "ok", "version": "2.0.0", "mock": false,
  "ios_available": true,                       // sin cambios
  "tools": {
    "adb":    { "found": true, "source": "portable", "path": "…", "version": "…" },
    "scrcpy": { … }, "ffmpeg": { … },          // sin cambios
    "python": {
      "found": true, "source": "portable", "path": "C:\\…\\tools\\python-embed\\python.exe",
      "version": "3.11.9",
      "pymobiledevice3_version": "10.7.4"      // NUEVO; falta si no se pudo leer (todavía o nunca)
    },
    "uxplay": { "found": false }               // NUEVO (sin versión: no se ejecuta uxplay)
  },
  "ios": {                                     // NUEVO
    "apple_service": "ok",                     // "ok" | "missing" | "unknown"
    "airplay_available": false,
    "airplay_unavailable_reason": "not_supported_on_windows"  // falta si airplay_available = true
  },
  "capabilities": ["case_evidence_v1", "ios_developer_mode_v1"]   // suma la capacidad nueva
}
```

- `tools.uxplay`: `ToolResolver.Find(AgentTools.Uxplay)`; no se lee versión (lanzar uxplay
  levanta GStreamer). `found/source/path` igual que el resto.
- `tools.python.pymobiledevice3_version`: leída en segundo plano y cacheada por ruta, igual que
  `version` (§6.6). Mock no cambia nada: la resolución es real.
- `ios.apple_service`: resultado de la sonda de §6.5 (cache 5 s). En mock: `"ok"`.
- `ios.airplay_available` = `tools.uxplay.found`. En Windows **siempre** `false` con
  `airplay_unavailable_reason = "not_supported_on_windows"` (aunque alguien deje un `uxplay.exe`:
  el cierre limpio, Bonjour y el firewall quedan para la HU de AirPlay en Windows). Fuera de
  Windows, sin uxplay: `"uxplay_not_found"`. En mock: `true`.
- `capabilities` suma `"ios_developer_mode_v1"`: la web muestra el botón de §4.3 solo si está.
- `diagnostico.ps1` (`Get-LineasHerramientasTatana`) recorre una lista fija de claves: los campos
  nuevos no lo rompen y **no se toca** (el paquete local no se rearma, D10).

### 4.3 `POST /devices/{serial}/ios/developer-mode` (nuevo, D5)

- Sin cuerpo. `serial` = UDID del iPhone (el `serial` del `Device` de `/devices`).
- Controlador nuevo `Controllers/IosDeveloperModeController.cs` → `IIosService.EnableDeveloperModeAsync(udid)`.
- **200**: `{ "status": "enabled" | "restarting" | "manual_required" }`
  - `enabled`: ya estaba activo (no se hace nada).
  - `restarting`: el iPhone no tiene código; se pidió activar y **se va a reiniciar**. Al encender,
    el perito toca "Encender" en el aviso del iPhone. El helper llama
    `enable_developer_mode(enable_post_restart=False)`: no se queda esperando el reinicio.
  - `manual_required`: el iPhone tiene código (`DeviceHasPasscodeSetError`); se llamó
    `reveal_developer_mode_option_in_ui()` y el interruptor quedó visible en Ajustes.
- **500** `{ error, code }` con los códigos de §4.1 (`ios_apple_service_missing`,
  `ios_device_not_found`, `ios_not_trusted`, `ios_locked`, `ios_tools_missing`, `ios_capture_failed`).
- Mock: `{ "status": "enabled" }`.
- Al terminar con `restarting`, se borra el UDID del caché de "preparado" (§6.4).

### 4.4 Sin cambios de forma

`GET /devices`, `POST …/screenshot`, `POST …/record/start|stop` y las de AirPlay mantienen la forma
de éxito de hoy. Lo único que cambia es: (a) el `code` en los errores; (b) `record/stop` de iOS
**ya no responde 200 con un archivo que no existe**: responde 500 `ios_recording_empty`.

---

## 5. Contrato compartido

### 5.1 Tatana → `client/` (`client/src/lib/agent.ts` ↔ `server/src/Factum.Agent/…`)

| Campo JSON (exacto) | Tipo TS | Lado C# | Lado TS |
|---|---|---|---|
| `tools` | `Record<string, AgentToolStatus>` (opcional: Tatana < 1.1.0 no lo manda) | `ToolInventory.Snapshot()` (`Services/ToolInventory.cs`) | `AgentHealth.tools` |
| `tools.<k>.found` / `source` / `path` / `version` | `boolean` / `"portable" \| "path" \| "homebrew"` / `string` / `string` (los tres últimos opcionales) | record `ToolStatus` | `AgentToolStatus` |
| `tools.python.pymobiledevice3_version` | `string?` | `ToolStatus.Pymobiledevice3Version` con **`[property: JsonPropertyName("pymobiledevice3_version")]`** explícito (no confiar en cómo parte el snake_case un nombre con dígito) | `AgentToolStatus.pymobiledevice3_version` |
| `tools.uxplay` | `AgentToolStatus` | clave `"uxplay"` en `ToolInventory.Tools` | idem |
| `ios_available` | `boolean?` | `HealthController` (sin cambios) | `AgentHealth.ios_available` |
| `ios.apple_service` | `"ok" \| "missing" \| "unknown"` | `HealthController`: objeto anónimo `ios = new { apple_service, airplay_available, airplay_unavailable_reason }` (nombres ya en snake) | `AgentIosStatus.apple_service` |
| `ios.airplay_available` | `boolean` | idem | `AgentIosStatus.airplay_available` |
| `ios.airplay_unavailable_reason` | `"not_supported_on_windows" \| "uxplay_not_found"` (opcional) | idem; `null` → no viaja | `AgentIosStatus.airplay_unavailable_reason` |
| `capabilities[]` incluye `"ios_developer_mode_v1"` | `string[]` | `HealthController` | `AgentHealth.capabilities` |
| Error iOS: `error`, `code` | `string`, `AgentErrorCode` | `Controllers/*` capturan `IosException` → `StatusCode(500, new { error = ex.Message, code = ex.Code })` | `AgentErrorBody` + unión `AgentErrorCode` (suma los 12 códigos de §4.1) |
| `POST /devices/{serial}/ios/developer-mode` → `status` | `"enabled" \| "restarting" \| "manual_required"` | `IosDeveloperModeController` → `Ok(new { status })` | `agent.enableIosDeveloperMode(serial): Promise<{ status: IosDeveloperModeStatus }>` |

Compatibilidad: con un Tatana viejo (sin `ios`, sin `tools.uxplay`) la web se comporta como hoy
(AirPlay habilitado, sin aviso de servicio, sin botón de Modo Desarrollador: se muestra el paso de
la guía como texto, sin comando).

### 5.2 Tatana ↔ helper Python (interno del agente, mismo implementador)

Lo fija §6.2. No cruza al client, pero los nombres de subcomandos, líneas de stderr y códigos de
salida son contrato entre `IosHelper.cs` y `ios_helper.py` y los fijan tests.

---

## 6. Diseño del agente (`implementer-backend`)

Estructura nueva en `server/src/Factum.Agent/Services/Ios/`:

| Archivo | Qué |
|---|---|
| `ios_helper.py` | El helper Python único (reemplaza `DvtScreenshotScript` y `DvtRecorderScript`). **`EmbeddedResource`** en el `.csproj` (funciona con single-file). |
| `IosHelper.cs` | Escribe el helper a disco, resuelve Python, arma `ProcessStartInfo` (ArgumentList, UTF-8, entorno), corre subcomandos cortos y parsea `TATANA_ERROR`. |
| `DvtRecorder.cs` | Proceso de grabación de larga vida: arranque hasta `READY`, drenaje de stderr, stop por stdin, espera de `DONE`. Sin dependencias de DI (lo usa también la autoprueba). |
| `IosErrors.cs` | `IosException(code, message)`, tabla código → texto por SO (§4.1), `FromHelper(code, detail, isWindows)`. Puro y testeado. |
| `AppleServiceProbe.cs` | Sonda del servicio de Apple (§6.5). |
| `IosSelfTest.cs` | Modo `--autoprueba-ios` (§6.9). |

`IosService.cs` queda como orquestador (sesiones, cadena de capturas, on_device, AirPlay de Mac,
interpolaciones) y usa las piezas de arriba.

### 6.1 Rutas multiplataforma: Python, helper y ffmpeg

**Python** (`IosHelper.ResolvePython()`, reemplaza `PythonCandidates`):
1. `ToolResolver.Find(AgentTools.Python)?.Path` (portátil `tools\python-embed\python.exe` → PATH
   `python3`/`python3.exe` → Homebrew). En la Mac da `/opt/homebrew/bin/python3`, **igual que hoy** (A1).
2. Si no hay o no importa pymobiledevice3: los candidatos de hoy (`python.exe`, `python`, `py`,
   `python3`, `/usr/bin/python3`, `/usr/local/bin/python3`, `/opt/homebrew/bin/python3`).
3. El primero que pase `-c "import pymobiledevice3"` gana; se cachea. Si ninguno: `null` y toda
   operación de iOS lanza `ios_tools_missing` (hoy cae a `"python3"` pelado).
`IsAvailable` pasa a ser `ResolvePython() is not null` (mock: `true`).

**Versión esperada:** constante `IosHelper.ExpectedPymobiledevice3Version = "10.7.4"`. Al arrancar,
si la versión leída (§6.6) difiere, **Warning** en el log:
`pymobiledevice3 {Version} en {Path}; la versión probada es 10.7.4`. No bloquea (en la Mac puede
haber otra).

**Helper:** se lee del recurso embebido y se escribe en
`Path.Combine(Path.GetTempPath(), "tatana", $"ios_helper_{sha256[..8]}.py")` (crea la carpeta; si el
archivo existe con el mismo nombre, no lo reescribe). Un nombre por versión evita que un Tatana
viejo y uno nuevo pisen el mismo archivo. Rutas siempre por `ArgumentList` (perfiles con espacios o
acentos: `C:\Users\José Pérez\AppData\Local\Temp`).

**ffmpeg:** el helper **no lo busca**: Tatana le pasa `--ffmpeg <ruta absoluta>` resuelta con
`ToolResolver.Find(AgentTools.Ffmpeg)`. Si no hay ffmpeg: `ios_tools_missing` antes de lanzar Python.

**Entorno de todo proceso Python que lanza Tatana** (`IosHelper.CreateStartInfo`), también los
`-m pymobiledevice3 afc …` de `on_device`:
`PYTHONUTF8=1`, `PYTHONIOENCODING=utf-8`, `NO_COLOR=1`, `TERM=dumb`, `PYTHONDONTWRITEBYTECODE=1`;
`StandardOutputEncoding = StandardErrorEncoding = UTF8`; `CreateNoWindow = true`. Para los comandos
cortos se usa `ProcessRunner.RunArgumentListAsync` con un parámetro nuevo opcional
`IReadOnlyDictionary<string,string>? environment = null` (aditivo; no cambia a los llamadores
actuales). Esto cierra H16.

**ffprobe (D9 a):** se borra `AgentTools.Ffprobe` de `ToolResolver.cs` y la mención en el
comentario de `IosService`. No viaja.

### 6.2 El helper `ios_helper.py` (contrato con Tatana)

Python ≥ 3.9 (corre en el 3.11 embebido y en el 3.14 de la Mac). Solo stdlib + pymobiledevice3 10.7.4
(+ Pillow, que es dependencia de pymobiledevice3, para `--synthetic`). Sin `loop.add_signal_handler`,
sin `signal`, sin separadores de PATH.

```
python ios_helper.py devices
python ios_helper.py prepare     --udid U
python ios_helper.py screenshot  --udid U --output P
python ios_helper.py record      --udid U --output P --ffmpeg F [--fps 2] [--synthetic]
python ios_helper.py devmode     --udid U
```

| Subcomando | stdout (éxito, una línea JSON, `ensure_ascii=True`) | Notas |
|---|---|---|
| `devices` | `[{"udid","name","product_type","product_version","imei","phone_number"}]` (campos ausentes = `""`) | Una sola ejecución lista todos los iPhone USB (`usbmux.list_devices()` + `create_using_usbmux(serial)` por cada uno). Reemplaza `usbmux list` + N × `lockdown info` (una vez cada 3 s del polling: menos procesos, que en Windows tardan). Un iPhone que falla en lockdown sale solo con `udid`. `ensure_ascii` hace que "iPhone de José" viaje como `\u00e9` y no dependa de la página de códigos. |
| `prepare` | `{"developer_mode": true, "ddi": "mounted" \| "already_mounted"}` | Lockdown → `get_developer_mode_status()`; si `False` → error `ios_developer_mode_disabled` (sin intentar montar). Si `product_version ≥ 17`: `MobileImageMounterService(lockdown).is_image_mounted("Personalized")`; si no, `auto_mount(lockdown)` (`AlreadyMountedError` = `already_mounted`). Si < 17: lo mismo con `"Developer"`. |
| `screenshot` | `{"ok": true}` y el PNG en `--output` | iOS ≥ 17: `async with UserspaceRsdTunnel(serial=udid) as rsd` → `DvtProvider(rsd)` → `Screenshot` → `get_screenshot()` (timeout 10 s). iOS < 17: `DvtProvider(lockdown)`. Verifica que el archivo exista y pese > 0 antes de imprimir `ok`. |
| `record` | — (protocolo por stderr, abajo) | Igual que `screenshot` para abrir DVT, más ffmpeg. |
| `devmode` | `{"status": "enabled" \| "restarting" \| "manual_required"}` | Lockdown → si `get_developer_mode_status()` → `enabled`. Si no: `AmfiService(lockdown).enable_developer_mode(enable_post_restart=False)` → `restarting`; ante `DeviceHasPasscodeSetError` → `reveal_developer_mode_option_in_ui()` → `manual_required`. |

**Errores (todos los subcomandos):** un `try/except BaseException` de nivel superior clasifica la
excepción (tabla de §4.1, por clase con `isinstance`, importando las clases de
`pymobiledevice3.exceptions` dentro de un `try` para no romper si alguna no existe), escribe
**una** línea en stderr

```
TATANA_ERROR {"code": "ios_locked", "detail": "<str(exc) recortado a 300>"}
```

y sale con **código 2**. El traceback completo va a stderr **antes** de esa línea (Tatana lo loguea
en Debug). El JSON también con `ensure_ascii=True`. Salida siempre con `sys.stdout.flush();
sys.stderr.flush(); os._exit(code)` para no colgarse en hilos de la pila.

**Códigos de salida:** `0` ok · `2` error clasificado (`TATANA_ERROR`) · `3` grabación vacía
(`record` terminó con 0 cuadros o ffmpeg salió ≠ 0).

**Protocolo de `record`:**
1. Abre el origen: el túnel + DVT del iPhone, o con `--synthetic` un generador de PNG con Pillow
   (fondo gris, número de cuadro dibujado, 390×844) **sin tocar usbmux** — es para la autoprueba y
   el CI.
2. Lanza ffmpeg: `[F, "-y", "-f", "image2pipe", "-vcodec", "png", "-r", fps, "-i", "pipe:0",
   "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-c:v", "libx264", "-pix_fmt", "yuv420p",
   "-crf", "23", P]`, stdout/stderr a `DEVNULL` (como hoy).
3. Imprime `READY` en stderr.
4. Un **hilo daemon** lee `sys.stdin` línea por línea; ante `stop` **o EOF** hace
   `loop.call_soon_threadsafe(stop_event.set)`. (EOF también para: si Tatana muere, el MP4 se cierra igual.)
5. Bucle de captura igual que hoy (timeout de 5 s por captura, ritmo `1/fps`), cortando apenas
   `stop_event` está puesto.
6. Al salir: cierra stdin de ffmpeg, `ffmpeg.wait(timeout=60)`, imprime
   `DONE {"frames": N, "ffmpeg_exit": X}` en stderr y sale 0 si `N > 0 and X == 0`; si no, 3.
7. Si falla al abrir el origen (antes de `READY`): `TATANA_ERROR …` y código 2, **matando el ffmpeg**
   que ya lanzó y borrando `P` si quedó vacío.

### 6.3 Cadena de captura (`TakeScreenshotAsync`) y túnel (D3)

`TakeDvtScreenshotAsync` = `EnsurePreparedAsync(udid)` (§6.4) + `helper screenshot`. Éxito =
exit 0 **y** PNG > 0 bytes. Si no, `IosException` armada desde `TATANA_ERROR` (o
`ios_capture_failed` con el último renglón de stderr si no hubo línea).

Con `method = "auto"`:
1. DVT. Si falla con un código **definitivo** — `ios_apple_service_missing`, `ios_device_not_found`,
   `ios_not_trusted`, `ios_locked`, `ios_admin_required`, `ios_tools_missing` — se lanza ese error
   **sin fallback** (qvh y AirPlay tampoco andarían, y hoy el perito termina viendo "brew").
2. qvh: **solo si `FindBinary(AgentTools.Qvh)` existe**; si no, se saltea sin error.
3. AirPlay: **solo si** `AirplayAvailable` (no Windows y uxplay encontrado); si no, se saltea.
4. Si todos los intentos se saltearon o fallaron: se lanza **el error de DVT** (el más informativo).

`method = "airplay"` explícito, `StartAirplayShotSessionAsync` y `StartRecordingAsync(mode="airplay")`
sin AirPlay → `airplay_unavailable` (§4.1). Las tres llamadas de hoy con "Instalá uxplay: brew
install uxplay" pasan por `IosErrors` (el texto de la Mac no cambia).

**Túnel iOS 17+ (D3):** se usa `UserspaceRsdTunnel` (sin admin, A5). Si en una PC Windows el túnel
pide privilegios, el helper lo clasifica como `ios_admin_required` y la web muestra el texto de §4.1
(D3 a). No se eleva Tatana nunca. La primera prueba en hardware (§12.3) confirma si alguna PC lo pide.

### 6.4 Preparar el iPhone: Modo Desarrollador + DDI (D5)

`IosService.EnsurePreparedAsync(udid, ct)`:
- `ConcurrentDictionary<string, bool> _prepared`. Si el UDID está, no hace nada.
- Si no: `helper prepare --udid U` con **timeout de 120 s** (la primera vez baja el DDI). Éxito →
  se guarda el UDID y log Information `iPhone {Udid}: Modo Desarrollador activo, imagen de
  desarrollador {Ddi}`. Error → `IosException` (no se guarda).
- Se borra el UDID de `_prepared` cuando deja de aparecer en `ListDevicesAsync` (un reinicio del
  iPhone desmonta el DDI, A7) y tras `devmode` → `restarting`.
- Lo llaman: `TakeDvtScreenshotAsync` y `TryStartDvtRecorderAsync` (antes de lanzar `record`). **No**
  lo llaman `on_device` ni el listado (no necesitan DDI).
- `EnableDeveloperModeAsync(udid)` = `helper devmode` (timeout 60 s) → `status`.

### 6.5 Servicio Apple Mobile Device (D4): `AppleServiceProbe`

```csharp
internal sealed class AppleServiceProbe(Func<CancellationToken, Task<bool>> windowsConnect,
                                        Func<bool> unixSocketExists, bool isWindows) { … }
public enum AppleServiceState { Ok, Missing, Unknown }   // → "ok" | "missing" | "unknown"
```
- Windows: `TcpClient.ConnectAsync("127.0.0.1", 27015)` con timeout de **500 ms** → `Ok`; rechazo o
  timeout → `Missing`; otra excepción → `Unknown`.
- macOS/Linux: `File.Exists("/var/run/usbmuxd")` (es un socket; `File.Exists` da true) → `Ok`/`Missing`.
- Resultado cacheado **5 s** (lo usan `/health` y el polling cada 3 s). Singleton en DI.
- `ListDevicesAsync`: si la sonda dice `Missing`, devuelve `[]` **sin lanzar Python** (en una PC sin
  el servicio, el polling no gasta un proceso cada 3 s) y loguea **una vez** (no cada 3 s) el Warning
  `Servicio de dispositivos de Apple no encontrado (127.0.0.1:27015): los iPhone no se van a ver hasta instalar "Apple Devices"`.
- Las operaciones con UDID (`screenshot`, `record`, `devmode`) consultan la sonda antes de lanzar
  el helper: `Missing` → `ios_apple_service_missing` directo.

### 6.6 `ToolInventory` + `/health` (D8)

- `Tools` suma `("uxplay", AgentTools.Uxplay, null)`; con `arg == null` no se lee versión.
- `ToolStatus` suma `[property: JsonPropertyName("pymobiledevice3_version")] string? Pymobiledevice3Version = null`.
- Para la clave `python`, además de `--version`, se lanza en segundo plano (mismo caché por ruta,
  mismo timeout de 5 s, mismo `ProcessStartInfo` con UTF-8) la lectura
  `-c "import importlib.metadata as m; print(m.version('pymobiledevice3'))"`. Parser nuevo
  `VersionParser.Pymobiledevice3(stdout)`: primera línea no vacía que matchee
  `^\d+(\.\d+)*([a-z0-9.+-]*)$`; si no, `null`.
- `HealthController.Health()` suma `ios = new { apple_service, airplay_available,
  airplay_unavailable_reason }` (de `AppleServiceProbe` e `IIosService.AirplayAvailable` /
  `AirplayUnavailableReason`) y `"ios_developer_mode_v1"` a `capabilities`.
- `IIosService` suma: `bool AirplayAvailable { get; }`, `string? AirplayUnavailableReason { get; }`,
  `Task<string> EnableDeveloperModeAsync(string udid, CancellationToken ct = default)`.

### 6.7 Grabación DVT: arranque, stop rápido y archivo completo (H5, H6, H7)

`DvtRecorder` (sin DI; recibe `ILogger`):
- `StartAsync(python, helperPath, udid, outputPath, ffmpeg, fps, synthetic, ct)`:
  `RedirectStandardInput/Error = true`. Lee stderr hasta `READY` (timeout **30 s**) o
  `TATANA_ERROR`/EOF. Después de `READY`, **sigue drenando stderr en una tarea de fondo** (hoy deja
  de leer y el pipe se puede llenar): guarda las últimas 50 líneas, la línea `DONE {…}` y la
  `TATANA_ERROR {…}` si llega. Si no llega a `READY`: mata el árbol (`Kill(entireProcessTree: true)`)
  y lanza `IosException` con el código del helper.
- `StopAsync(ct)` → `DvtStopResult(bool Ok, int Frames, IosException? Error)`:
  1. `StandardInput.WriteLine("stop")`, `Flush()`, `Close()`.
  2. Espera la salida hasta **30 s**. Lo esperable: ≤ 5 s del último `get_screenshot` + el cierre de
     ffmpeg ≈ **< 10 s** (Gherkin "pocos segundos"; hoy son 40 s fijos en Windows).
  3. Ok = exit 0, `DONE.frames > 0`, `DONE.ffmpeg_exit == 0`, archivo existe y > 0 bytes.
  4. Si vence el timeout: `Kill(entireProcessTree: true)` (se lleva el ffmpeg hijo) y `Ok = false`
     con motivo `la grabación no terminó de escribirse a tiempo.`
  5. Tatana responde **recién después** de esto: el archivo que entrega está cerrado por ffmpeg (H7).
- **Sin `kill -15`** para el recorder: el stop por stdin es igual en Windows y en la Mac.

`IosService`:
- `TryStartDvtRecorderAsync`: `EnsurePreparedAsync` + `DvtRecorder.StartAsync`. Si falla con un
  código **definitivo** (lista de §6.3 + `ios_developer_mode_disabled` y `ios_ddi_mount_failed`):
  `StartRecordingAsync` lanza ese error (500 al iniciar, la web lo muestra) y **no** cae al burst.
  Con `ios_tunnel_failed`/`ios_capture_failed`: Warning y burst, como hoy, guardando el error en la
  sesión (`session.StartError`).
- `StopRecordingAsync` (modos DVT): mic primero (como hoy), `DvtRecorder.StopAsync`, mux del mic
  solo si `Ok`. Si `!Ok`: borra el MP4 parcial y el audio temporal, y lanza `ios_recording_empty`
  con el motivo. **No** lanza interpolaciones.
- Burst: si `frames.Count == 0` → `ios_recording_empty` con el motivo de `session.StartError` (o
  `no se capturó ningún cuadro de la pantalla.`). Si ffmpeg del burst falla → idem. Nunca se
  devuelve `session.OutputPath` si `!File.Exists`.
- `GracefulStopAsync` (uxplay, qvh — solo existen en la Mac): en Windows delega en
  `ProcessStop.StopGracefullyAsync` (Ctrl+C) en vez de lanzar el `kill` que no existe; fuera de
  Windows queda **igual que hoy** (`kill -15`/`-2`). `DYLD_LIBRARY_PATH` solo en macOS también en
  `TryStartAirplayRecorderAsync` (hoy se setea siempre).

### 6.8 `on_device` y listado

- `ListDCIMFilesAsync`/`PullNewRecordingAsync` siguen con el CLI (`-m pymobiledevice3 afc ls|pull`,
  probados en 10.7.4 en la Mac), pero por `IosHelper.CreateStartInfo` (ArgumentList + UTF-8 +
  entorno). Un `afc pull` que falla se clasifica: stderr con `ConnectionFailedToUsbmuxd` →
  `ios_apple_service_missing`; con `PasscodeRequired` → `ios_locked`; resto `ios_capture_failed`.
- `ListDevicesAsync` = sonda (§6.5) + `helper devices` (timeout 20 s) → `Device` (mismo mapeo de
  hoy: `FriendlyModel(product_type)`, `IosVersion`, `AndroidVersion = major`, `Imei`,
  `Operator = phone_number`). Si el helper falla: `[]` y Debug (como hoy).

### 6.9 Modo `--autoprueba-ios <carpeta>` (prueba de humo sin iPhone, D2)

En `Program.cs`, junto al `--ctrl-c`, **antes** de crear el host:
`if (args.Length == 2 && args[0] == "--autoprueba-ios") Environment.Exit(await IosSelfTest.RunAsync(args[1]));`

`IosSelfTest.RunAsync` escribe una línea por paso (`OK  …` / `FALLA  …`) y devuelve 0 solo si pasan todos:
1. Python resuelto (ruta) e importa pymobiledevice3.
2. `importlib.metadata.version('pymobiledevice3') == ExpectedPymobiledevice3Version`.
3. ffmpeg resuelto (ruta).
4. Imports de Windows: `python -c "import win32security, lzfse, pymobiledevice3.osu.win_util, pymobiledevice3.remote.userspace_tunnel, pymobiledevice3.services.dvt.instruments.screenshot, pymobiledevice3.services.mobile_image_mounter, pymobiledevice3.services.amfi"` (en macOS se omite `win32security` y `win_util`).
5. Grabación sintética con **`DvtRecorder` real**: `record --synthetic --fps 2` a `<carpeta>/autoprueba.mp4`, espera 4 s, `StopAsync` → `Ok`, `Frames ≥ 4`, y el stop tarda **< 10 s**.
6. `ffmpeg -v error -i <carpeta>/autoprueba.mp4 -f null -` sale 0 (el MP4 se puede leer entero).
7. `helper screenshot --udid 0000-AUTOPRUEBA --output <carpeta>/x.png` **sin iPhone**: tiene que
   salir con código 2 y `TATANA_ERROR` con código `ios_apple_service_missing` o
   `ios_device_not_found` en < 30 s (prueba la clasificación y que no sale 0 en falla, A6).
8. `AppleServiceProbe` responde (`ok`/`missing`, no excepción).

Solo escribe dentro de `<carpeta>`. No arranca Kestrel ni toca `DataDirectory`.

### 6.10 Logs fijos (Information/Warning) que buscan la autoprueba y el piloto

- `iOS DVT recorder iniciado (~{Fps} FPS)` · `iOS DVT recorder detenido en {Ms} ms ({Frames} cuadros)`
- `iPhone {Udid}: Modo Desarrollador activo, imagen de desarrollador {Ddi}`
- `Error de iPhone {Code}: {Detail}` (Warning, con el stderr del helper en Debug)
- el Warning de §6.5 y el de versión de §6.1.

---

## 7. Empaquetado (`implementer-backend`)

### 7.1 Versión fija de pymobiledevice3 (D6)

**`pymobiledevice3==10.7.4`** (A1). Wheel en PyPI: `pymobiledevice3-10.7.4-py3-none-any.whl`,
sha256 `60eab6402806b6c1441c7af4bd9aa80403240aee294ab0acb7298abfa31cd339`. Licencia
**GPL-3.0-or-later** (ya se redistribuye hoy; ver DP1).

### 7.2 Lock de dependencias para Windows (A2, A3)

Archivos nuevos en `packaging/portable/ios-win/`:

| Archivo | Qué |
|---|---|
| `requirements.in` | Entrada humana: `pymobiledevice3==10.7.4` + las dependencias que solo aplican en Windows y pip no ve desde la Mac (A3): `pywin32`, `av>=14.0.0`, `lzfse>=0.4.2`, `sslpsk-pmd3>=1.0.3`, `colorama>=0.4.4`, `win32-setctime>=1.0.0`, `pyreadline3`. |
| `generar-lock.py` | Corre en la Mac (o Linux): `pip install --dry-run --report` con `--platform win_amd64 --implementation cp --python-version 311 --only-binary=:all: --find-links <wheelhouse con hexdump>` sobre `requirements.in`; recorre `requires_dist` de cada paquete del reporte evaluando los marcadores con un entorno Windows/CPython 3.11.9 (`packaging.markers`) y **falla** si queda alguno sin cubrir, diciendo cuál agregar a `requirements.in`. Escribe `requirements-win.lock`. Con `--check` no escribe ni re-resuelve versiones: hace un dry-run con el lock commiteado (`--no-deps`, mismas opciones) y verifica que todo wheel exista con ese hash, que esté `pymobiledevice3==10.7.4` y que la clausura con marcadores de Windows quede cubierta; sale ≠ 0 si no. (No compara contra una resolución nueva: que PyPI publique versiones nuevas no debe romper el check.) Se corre con `python3 -I`. |
| `requirements-win.lock` | **Versionado**: `nombre==versión --hash=sha256:<hash del wheel win_amd64/cp311/abi3/none-any>` por línea, sacado de `download_info.archive_info.hashes` del reporte. Sin `hexdump`. Encabezado con la fecha y el comando que lo generó. |
| `hexdump.txt` | `hexdump==3.3 --hash=sha256:d781a43b0c16ace3f9366aade73e8ad3a7bd5137d58f0b45ab2d3f54876f20db` (el sdist `hexdump-3.3.zip`). |

### 7.3 `deploy/windows/armar-tatana-portable.sh`, paso 5

Reemplaza el `pip install … pymobiledevice3` por:
1. `python3 -m pip wheel --quiet --no-deps --require-hashes -r "$SRC/packaging/portable/ios-win/hexdump.txt" --wheel-dir "$TMP/wheelhouse"`
   (construye el wheel puro de hexdump desde el sdist verificado por hash).
2. `SITE="$OUT/tools/python-embed/Lib/site-packages"; mkdir -p "$SITE"`
3. `python3 -m pip install --quiet --disable-pip-version-check --no-cache-dir --no-deps --require-hashes --only-binary=:all: --platform win_amd64 --implementation cp --python-version 311 --target "$SITE" -r "$SRC/packaging/portable/ios-win/requirements-win.lock"`
4. `python3 -m pip install --quiet --no-deps --no-index --target "$SITE" "$TMP"/wheelhouse/hexdump-3.3-*.whl`
5. Copia `requirements-win.lock` a `tools/python-embed/TATANA-PYTHON-LOCK.txt` (trazabilidad
   forense: con qué versiones exactas se capturó).
6. Escribe `tools/python-embed/THIRD-PARTY-NOTICES.txt` (CRLF, como el de scrcpy): Python 3.11.9
   (PSF), pymobiledevice3 10.7.4 (GPL-3.0-or-later, fuente
   `https://github.com/doronz88/pymobiledevice3/tree/v10.7.4` y el sdist de PyPI), y "las demás
   bibliotecas y sus versiones están en TATANA-PYTHON-LOCK.txt; sus licencias, en
   `Lib\site-packages\*.dist-info`".
7. `echo "  OK  pymobiledevice3 10.7.4 (lock de Windows)"`.

El `._pth` sigue con `import site` habilitado (hoy). `site` procesa `Lib\site-packages` y sus `.pth`
(pywin32, A4).

**Paso 6:** sin cambios de ffmpeg. El `AVISO` de uxplay cambia a
`AVISO: el portátil de Windows sale sin AirPlay (decisión D1 de ios-herramientas-windows); ver packaging/windows-uxplay-build.md`.
`UXPLAY_WIN_ARTIFACT_URL` se deja como está (lo retoma la HU de AirPlay en Windows).

**Chequeo del zip** (lista `requerido`): suma `tools/python-embed/python.exe`,
`tools/python-embed/python311._pth`,
`tools/python-embed/Lib/site-packages/pymobiledevice3/__init__.py`,
`tools/python-embed/Lib/site-packages/win32/win32security.pyd`,
`tools/python-embed/Lib/site-packages/pywin32.pth`,
`tools/python-embed/TATANA-PYTHON-LOCK.txt` y `tools/python-embed/THIRD-PARTY-NOTICES.txt`.
Y un chequeo nuevo: el `METADATA` de pymobiledevice3 del zip dice `Version: 10.7.4`
(`unzip -p … 'tools/python-embed/Lib/site-packages/pymobiledevice3-10.7.4.dist-info/METADATA'`),
si no, `ERROR`.

**Requisitos del script:** el encabezado suma "funciona también en Linux (lo usa el CI)". Hay que
verificar que no use nada propio de BSD/macOS: `shasum` (existe en ubuntu-24.04), `sed` sin `-i`
(ya es portable), `mktemp -d "${TMPDIR:-/tmp}/…"` (OK).

### 7.4 `armar-tatana-nube.sh` y versión (D10)

Sin cambios de código. El zip de la 1.3.0 se arma con
`deploy/cloud/armar-tatana-nube.sh --version 1.3.0 --origenes "<los de producción>"` **después del
merge a `main`** (como pide el script con `--ref origin/main`) y se publica en `/descargas` según
`docs/despliegue-nube.md` §10. Eso lo hace el usuario/orquestador al cerrar, no los implementadores.

### 7.5 Guía (`docs/instalacion-windows.md` §8, "iPhone")

- Punto 1: "app **Apple Devices** (Microsoft Store) o iTunes"; si falta, Factum lo avisa al
  conectar el iPhone.
- Punto 3 se reemplaza por: "El Modo Desarrollador se activa desde la guía de conexión de iPhone de
  Factum (botón **Activar Modo Desarrollador**). La primera captura de cada iPhone tarda unos
  segundos más: Tatana prepara el iPhone y necesita internet. Tatana **no** necesita correr como
  administrador; si Windows lo pide, Factum lo dice."
- En "Herramientas de Tatana": "AirPlay no está disponible en Tatana para Windows."

---

## 8. CI de verificación (D2) — `implementer-backend`

### 8.1 Workflow nuevo `.github/workflows/tatana-windows.yml`

```yaml
name: Tatana Windows (humo)
on:
  workflow_dispatch: {}
  pull_request:
    branches: [develop, main]
    paths:
      - server/src/Factum.Agent/**
      - packaging/portable/**
      - deploy/windows/armar-tatana-portable.sh
      - .github/workflows/tatana-windows.yml
      - ci/tatana-windows/**
concurrency: { group: tatana-windows-${{ github.ref }}, cancel-in-progress: true }
permissions: { contents: read }
```
(El disparo por PR con `paths` queda sujeto a DP3; `workflow_dispatch` va siempre.)

**Job `armar`** (`ubuntu-24.04`, `timeout-minutes: 25`): mismas acciones fijadas por SHA que
`verificar.yml` (`checkout`, `setup-dotnet` 10.0.x) + `actions/setup-python` fijado por SHA con
`python-version: "3.12"` (pip propio, sin PEP 668). Corre
`deploy/windows/armar-tatana-portable.sh --version 0.0.0-ci --src "$GITHUB_WORKSPACE" --salida "$RUNNER_TEMP/dist"`
(el checkout de CI es un árbol limpio: no hay `appsettings.Local.json`; el chequeo de Mock del
script corre igual). Sube el zip con `actions/upload-artifact` (fijado por SHA,
`retention-days: 3`).

**Job `humo`** (`windows-latest`, `needs: armar`, `timeout-minutes: 15`): baja el artefacto y corre
`pwsh -File ci/tatana-windows/prueba-humo.ps1 -Zip <ruta>`. Si falla, sube
`$env:RUNNER_TEMP\tatana-humo\**` (logs) como artefacto.

### 8.2 `ci/tatana-windows/prueba-humo.ps1`

`Set-StrictMode -Version Latest; $ErrorActionPreference = 'Stop'`. Cada chequeo escribe `OK …` o
lanza con un mensaje claro. Pasos:
1. `Expand-Archive` a `$env:RUNNER_TEMP\tatana-humo\app`.
2. `tools\python-embed\python.exe -m pymobiledevice3 version` → `10.7.4`.
3. `Factum.Agent.exe --autoprueba-ios $env:RUNNER_TEMP\tatana-humo\autoprueba` → exit 0 (§6.9;
   sus líneas quedan en el log).
4. Arranca `Factum.Agent.exe --port 8765 --data $env:RUNNER_TEMP\tatana-humo\data` con
   `Start-Process -PassThru -RedirectStandardOutput … -RedirectStandardError …`.
5. Sondea `GET http://localhost:8765/health` hasta 60 s. Comprueba: `mock -eq $false`;
   `tools.python.found` y `source -eq 'portable'`; `tools.ffmpeg.found` y `source -eq 'portable'`;
   reintenta hasta 30 s más hasta que `tools.python.pymobiledevice3_version -eq '10.7.4'`;
   `tools.uxplay.found -eq $false`; `ios.airplay_available -eq $false`;
   `ios.airplay_unavailable_reason -eq 'not_supported_on_windows'`;
   `ios.apple_service -eq 'missing'` (el runner no tiene el servicio de Apple: es la prueba de D4);
   `ios_available -eq $true`; `capabilities -contains 'ios_developer_mode_v1'`.
6. `GET /devices` responde 200 en < 5 s con `devices` vacío (sin servicio no se lanza Python).
7. `POST /devices/0000-HUMO/screenshot?platform=ios` → 500 con `code -eq 'ios_apple_service_missing'`
   y un `error` que **no** contiene `brew` (con `Invoke-WebRequest -SkipHttpErrorCheck`).
8. `POST /devices/0000-HUMO/record/start` con `{"platform":"ios","ios_mode":"airplay","android_version":0}`
   → 500 con `code -eq 'airplay_unavailable'`.
9. Detiene el proceso (`Stop-Process`), en un `finally`.

Ningún paso toca Mongo ni datos del usuario.

### 8.3 `verificar.yml` (barato, en cada PR)

En el job `dotnet` (ubuntu), un paso nuevo después de los tests:
`python3 -m py_compile server/src/Factum.Agent/Services/Ios/ios_helper.py`.

---

## 9. Diseño del client (`implementer-frontend`, solo `client/`)

Skills obligatorios: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience`
(como criterio, **sin** 3D), `web-design-guidelines` (autochequeo final); `ui-styling` y
`mblode-agent-skills-ui-animation` si corresponden. Leer `client/AGENTS.md` (Next.js 16).

### 9.1 `client/src/lib/agent.ts`

- Tipos nuevos/extendidos (§5.1):
  ```ts
  export interface AgentToolStatus { found: boolean; source?: "portable" | "path" | "homebrew"; path?: string; version?: string; pymobiledevice3_version?: string; }
  export type AppleServiceState = "ok" | "missing" | "unknown";
  export type AirplayUnavailableReason = "not_supported_on_windows" | "uxplay_not_found";
  export interface AgentIosStatus { apple_service: AppleServiceState; airplay_available: boolean; airplay_unavailable_reason?: AirplayUnavailableReason; }
  export type IosDeveloperModeStatus = "enabled" | "restarting" | "manual_required";
  // AgentHealth suma: ios_available?: boolean; tools?: Record<string, AgentToolStatus>; ios?: AgentIosStatus;
  ```
- `AgentErrorCode` suma: `"ios_apple_service_missing" | "ios_device_not_found" | "ios_not_trusted" | "ios_locked" | "ios_developer_mode_disabled" | "ios_ddi_mount_failed" | "ios_tunnel_failed" | "ios_admin_required" | "ios_tools_missing" | "ios_capture_failed" | "ios_recording_empty" | "airplay_unavailable"`.
- **D7:** `takeScreenshot`, `startAirplayShot`, `markAirplayShot` y `stopAirplayShot` pasan al
  patrón de `startRecording`: `try { fetch } catch { throw new Error(AGENT_UNREACHABLE) }` y
  `if (!res.ok) throw await readAgentError(res, "<fallback actual>")`. Los fallbacks son los textos
  de hoy (`"Error tomando screenshot"`, etc.), que solo se ven si Tatana no manda `error`.
- Nueva: `enableIosDeveloperMode(serial): Promise<{ status: IosDeveloperModeStatus }>` →
  `POST /devices/{encodeURIComponent(serial)}/ios/developer-mode`, mismo patrón de errores.

### 9.2 `client/src/lib/agent-messages.ts`

Textos de la web (los de Tatana se muestran tal cual):
- `IOS_APPLE_SERVICE_MISSING_TITLE = "Falta el servicio de Apple en esta PC"`
- `IOS_APPLE_SERVICE_MISSING_MESSAGE = 'Para que Factum vea el iPhone, instalá la app "Apple Devices" desde Microsoft Store (o iTunes) y volvé a conectar el iPhone.'`
- `airplayUnavailableText(reason)`: `not_supported_on_windows` → `"AirPlay no está disponible en Tatana para Windows."`; `uxplay_not_found` (o sin motivo) → `"Falta el receptor AirPlay en esta PC."`
- `AIRPLAY_UNAVAILABLE_BADGE = "No disponible en esta PC"`
- `iosDeveloperModeMessage(status, deviceName)`:
  - `enabled` → `` `El Modo Desarrollador ya está activo en ${deviceName}.` ``
  - `restarting` → `` `Listo: ${deviceName} se va a reiniciar. Cuando encienda, tocá "Encender" en el aviso de Modo Desarrollador y volvé a conectarlo.` ``
  - `manual_required` → `` `${deviceName} tiene código de bloqueo, así que el Modo Desarrollador se prende a mano: en el iPhone andá a Ajustes → Privacidad y seguridad → Modo Desarrollador (ya quedó visible) y activalo. El iPhone se reinicia.` ``

### 9.3 Hook nuevo `client/src/hooks/useAgentIosStatus.ts`

`useAgentIosStatus(enabled = true)` → `{ appleService: AppleServiceState | null, airplayAvailable: boolean, airplayReason: AirplayUnavailableReason | null, supportsDeveloperMode: boolean }`.
Llama `agent.health()` al montar y cada **10 s** mientras `enabled` y `document.visibilityState === "visible"`.
Si falla o Tatana es viejo (sin `ios`): `appleService = null`, `airplayAvailable = true`,
`airplayReason = null` (comportamiento de hoy). `supportsDeveloperMode =
capabilities?.includes("ios_developer_mode_v1") ?? false`. Limpia el intervalo al desmontar.
No reemplaza a `useAgentIdentity` (ese cachea a nivel módulo y no revalida; el estado del servicio
cambia cuando el perito instala "Apple Devices").

### 9.4 Componentes

- **`DeviceConnect.tsx`**: prop nueva `appleServiceMissing?: boolean`. Si `agentOnline &&
  appleServiceMissing`: `FxBanner tone="warn" role="status"` con `IOS_APPLE_SERVICE_MISSING_TITLE`
  + `IOS_APPLE_SERVICE_MISSING_MESSAGE` y, si hay `onOpenGuide`, el botón link "Ver la guía"
  (mismo patrón que el banner de Tatana caído). Va debajo del de Tatana caído. Se muestra aunque
  haya Androids en la lista.
- **`IOSModePicker.tsx`**: prop nueva `airplayUnavailableReason?: AirplayUnavailableReason | null`
  (`undefined`/`null` = disponible). Si hay motivo, la tarjeta `airplay` **se muestra
  deshabilitada** (D1 b): `disabled` + `aria-disabled`, sin `fx-card-interactive`, opacidad
  reducida, `Tag severity="secondary"` con `AIRPLAY_UNAVAILABLE_BADGE` en lugar de "30 FPS", y la
  `desc` reemplazada por `airplayUnavailableText(reason)`. Se enlaza con `aria-describedby`. Las
  otras tres tarjetas no cambian.
- **`CaptureStep.tsx`**: props nuevas `airplayUnavailableReason?: AirplayUnavailableReason | null`
  → se pasa a `IOSModePicker`; el botón "Espejar para capturas" queda `disabled` con un texto
  chico debajo (`text-xs text-fx-text-3`) con `airplayUnavailableText(reason)` (se mantiene
  visible para que se entienda por qué no anda, coherente con la tarjeta).
- **`app/dashboard/page.tsx`**: usa `useAgentIosStatus(agentOnline)`; pasa
  `appleServiceMissing={appleService === "missing"}` a `DeviceConnect` y
  `airplayUnavailableReason={airplayAvailable ? null : airplayReason ?? "uxplay_not_found"}` a
  `CaptureStep`. `handleScreenshot` ya muestra `e.message` (con D7 es el motivo real). Si
  `onSelectIosMode` recibe `airplay` sin AirPlay (no debería: está deshabilitada), no se llama a Tatana.

### 9.5 Guía de conexión del iPhone (`usb-guide`)

- `types.ts`: `IOSStep` suma `{ type: "devmode"; icon; title; detail; tip? }` y **se quita** la
  variante `terminal` de `IOSStep` (Android no la usa; `TerminalBlock` queda en `Mockups.tsx`).
- `data.ts`: el paso "Ejecutá el comando de activación en la Mac" pasa a
  `{ type: "devmode", icon: ShieldCheck, title: "Activá el Modo Desarrollador desde Factum", detail: "Con el iPhone conectado, desbloqueado y con \"Confiar\" aceptado, tocá el botón. Tatana habilita el Modo Desarrollador sin Mac ni comandos.", tip: "Si el iPhone tiene código de bloqueo, Factum te indica cómo prenderlo en Ajustes." }`.
  El paso siguiente ("Confirmá el Modo Desarrollador en el iPhone") cambia su `detail` a:
  `"Si el iPhone tiene código, andá a Ajustes → Privacidad y seguridad → Modo Desarrollador y activá el interruptor. El iPhone pedirá reiniciarse."`
  Ningún texto menciona Mac, Terminal ni rutas.
- Componente nuevo `usb-guide/DeveloperModeAction.tsx` (lo renderiza `IOSGuide` para
  `type === "devmode"`): botón `"Activar Modo Desarrollador"` (Prime `Button`, `loading` mientras
  corre; puede tardar hasta ~1 min). Al tocar: `agent.listDevices()` → primer `platform === "ios"`;
  si no hay → mensaje inline `"Conectá el iPhone por USB, desbloqueado, y tocá \"Confiar\". Después volvé a tocar el botón."`;
  si hay → `agent.enableIosDeveloperMode(serial)` → `iosDeveloperModeMessage(status, device.name || "el iPhone")`
  en un mensaje inline (`role="status"`, tono success/info); error → `e.message` (tono danger).
  Si `useAgentIosStatus().supportsDeveloperMode` es `false` (Tatana viejo o caído): en lugar del
  botón, texto `"Actualizá Tatana para activar el Modo Desarrollador desde Factum."` (sin comando).

---

## 10. Decisiones técnicas

| # | Decisión | Por qué |
|---|---|---|
| T1 | Fijar **10.7.4** (la de la Mac, A1) y armar con un **lock con hashes** generado por script, más las dependencias de Windows explícitas. | Sin esto el portátil trae la 1.0.0 (A2) y sin `pywin32` (A3). El lock da reproducibilidad y trazabilidad forense (D6). |
| T2 | Instalar en `Lib\site-packages` del embebido. | Procesa `pywin32.pth` (A4). Lo prueba el CI. |
| T3 | Un **solo helper Python** como recurso embebido, con subcomandos y protocolo fijo. | Se puede compilar/probar en CI, no se duplica la apertura del túnel, y los errores salen clasificados. |
| T4 | Stop del grabador **por stdin** (y EOF) en las dos plataformas. | Sin `add_signal_handler` (no existe en Windows) ni `kill`; mismo comportamiento en la Mac; si Tatana muere, el MP4 igual se cierra. |
| T5 | `UserspaceRsdTunnel(serial=udid)` en lugar de `establish_userspace_rsd()`. | Sin `os._exit(0)` en el `atexit` (A6), apunta al iPhone correcto y cierra limpio. Sin admin (A5). |
| T6 | Montaje del DDI **una vez por iPhone conectado**, no en cada captura. | La primera vez baja ~16 MB y pide TSS (A8); hacerlo en cada foto la volvería lenta. Se invalida al desconectar (A7). |
| T7 | Códigos "definitivos" cortan la cadena de fallbacks (foto `auto` y grabación). | Hoy el perito termina en "brew install uxplay" o en un burst vacío; con el error de DVT sabe qué hacer. |
| T8 | Sonda TCP a `127.0.0.1:27015` para el servicio de Apple. | Es lo mismo que usa pymobiledevice3 (A10), no pide admin ni paquetes, y sirve para Apple Devices y para iTunes. |
| T9 | En Windows AirPlay = no disponible **aunque haya un `uxplay.exe`**. | El cierre, Bonjour y firewall no están resueltos (D1 b). |
| T10 | Errores siguen en **500** con `code` aditivo. | La web ya lee `error` de cualquier no-2xx; no hay que tocar más llamadores. |
| T11 | Autoprueba como modo del exe (`--autoprueba-ios`), que usa el `DvtRecorder` real. | La prueba de humo del CI recorre el código de producción (stop por stdin, ffmpeg, clasificación), no una copia en PowerShell. |
| T12 | CI en dos jobs: armar en Ubuntu (el script ya es portable) y probar en `windows-latest`. | El armado no necesita Windows; las ejecuciones en Windows valen el doble en minutos (DP3). |

### Decisiones pendientes (necesitan al usuario; ninguna bloquea empezar a implementar)

- **DP1. Licencia de pymobiledevice3 (GPL-3.0-or-later).** El portátil ya la redistribuye hoy sin
  aviso. La SDD suma `THIRD-PARTY-NOTICES.txt` con versión, licencia y enlace al fuente exacto, y el
  lock con todas las versiones (§7.3). **Recomendación:** alcanza con eso para esta HU (mismo
  criterio que scrcpy); si el producto se vende a terceros, pedir una revisión legal aparte que
  cubra también uxplay (HU de AirPlay en Windows).
- **DP2. Perito piloto (pendiente de la HU, D2).** La validación no dijo **quién** prueba en
  Windows ni con qué iPhone/iOS. Sin eso la HU no se puede cerrar (§12.3). **Recomendación:** un
  perito de la nube con Windows 11 y un iPhone con iOS 17.4 o posterior (ideal: el mismo modelo/iOS
  26 de la Mac, para comparar), con 30 minutos y la checklist de §12.3.
- **DP3. Cuándo corre el workflow de Windows.** Arma ~300 MB y corre en `windows-latest` (~10 min de
  Ubuntu + ~5 min de Windows, que en un repo privado cuentan doble). **Recomendación:** en PRs a
  `develop`/`main` **solo si cambian** `Factum.Agent`, `packaging/portable` o el script de armado
  (filtro `paths` de §8.1) + manual (`workflow_dispatch`). Alternativa más barata: solo manual,
  y el orquestador lo dispara antes de aprobar cualquier HU que toque Tatana.

---

## 11. Checklist atómico

### 11.1 Backend — Tatana (`implementer-backend`)

- [ ] B1. `Common/ToolResolver.cs`: borrar `AgentTools.Ffprobe` (D9 a).
- [ ] B2. `Common/ProcessRunner.cs`: `RunArgumentListAsync(..., IReadOnlyDictionary<string,string>? environment = null)` (aditivo).
- [ ] B3. `Services/Ios/IosErrors.cs`: `IosException(string Code, string Message)`, constantes de los 12 códigos, `MessageFor(code, isWindows, detail?)` con los textos **exactos** de §4.1, `IsDefinitive(code)` (§6.3/§6.7), `TryParseHelperError(string line, out code, out detail)`.
- [ ] B4. `Common/AgentErrorCodes.cs`: sumar los 12 códigos (mismos strings).
- [ ] B5. `Services/Ios/ios_helper.py` con los 5 subcomandos, clasificación, `TATANA_ERROR`, códigos de salida, `--synthetic`, stop por stdin/EOF y `os._exit` (§6.2). `EmbeddedResource` en `Factum.Agent.csproj`.
- [ ] B6. `Services/Ios/IosHelper.cs`: escribir el helper (§6.1), `ResolvePython()`, `CreateStartInfo()` con entorno y UTF-8, `RunAsync(subcomando, args, timeout)` → stdout JSON o `IosException`.
- [ ] B7. `Services/Ios/AppleServiceProbe.cs` (§6.5) + registro singleton en `Program.cs`.
- [ ] B8. `Services/Ios/DvtRecorder.cs` (§6.7): READY 30 s, drenaje de stderr, stop por stdin, 30 s + kill del árbol, `DvtStopResult`.
- [ ] B9. `IosService`: usar `IosHelper` para Python (borrar `PythonCandidates`, `DvtScreenshotScript`, `DvtRecorderScript`, `EnsureDvt*Script`); `IsAvailable` (§6.1); `ListDevicesAsync` = sonda + `helper devices` + invalidar `_prepared` (§6.8).
- [ ] B10. `IosService.EnsurePreparedAsync` y `EnableDeveloperModeAsync` (§6.4).
- [ ] B11. `IosService.TakeScreenshotAsync`: cadena de §6.3 (definitivos sin fallback, qvh/AirPlay solo si existen, error final = DVT).
- [ ] B12. `IosService` grabación: `TryStartDvtRecorderAsync` con `DvtRecorder`, definitivos sin burst, `StopRecordingAsync` sin archivo inexistente, burst vacío → `ios_recording_empty`, sin interpolaciones si falló (§6.7).
- [ ] B13. `IosService`: AirPlay no disponible en Windows (`AirplayAvailable`, `AirplayUnavailableReason`), errores `airplay_unavailable` en los tres puntos, `GracefulStopAsync` con `ProcessStop` en Windows, `DYLD_LIBRARY_PATH` solo en macOS.
- [ ] B14. `on_device` por `IosHelper.CreateStartInfo` + clasificación de `afc pull` (§6.8).
- [ ] B15. `Services/ToolInventory.cs`: clave `uxplay` sin versión; `ToolStatus.Pymobiledevice3Version` con `JsonPropertyName`; lectura en segundo plano; `VersionParser.Pymobiledevice3`; Warning de versión distinta (§6.1).
- [ ] B16. `Controllers/HealthController.cs`: bloque `ios` y capacidad `ios_developer_mode_v1` (§4.2).
- [ ] B17. `Controllers/IosDeveloperModeController.cs` (§4.3).
- [ ] B18. `ScreenshotController`, `RecordingController`, `AirplayShotController`: `catch (IosException ex)` → `StatusCode(500, new { error = ex.Message, code = ex.Code })` antes del `catch (Exception)` genérico.
- [ ] B19. `Services/Ios/IosSelfTest.cs` + rama `--autoprueba-ios` en `Program.cs` (§6.9).
- [ ] B20. Logs fijos de §6.10.

### 11.2 Backend — tests .NET (`server/tests/Factum.Agent.Tests`)

- [ ] T1. `IosErrorsTests`: cada código → texto exacto en Windows y Mac; ningún texto de Windows contiene `brew`, `Terminal` ni `/usr/`; `IsDefinitive`; parseo de `TATANA_ERROR {…}` (válido, JSON roto, código desconocido → `ios_capture_failed`), incluido un `detail` con `\u00e9`.
- [ ] T2. `AppleServiceProbeTests`: conexión OK → `Ok`; rechazo/timeout → `Missing`; excepción rara → `Unknown`; rama Unix con `unixSocketExists`; caché de 5 s (reloj inyectable).
- [ ] T3. `VersionParserTests`: `Pymobiledevice3("10.7.4\n")`, con espacios, vacío → `null`, traceback → `null`.
- [ ] T4. `IosHelperTests`: el recurso embebido existe, el nombre de archivo lleva el sha8, el contenido **no** contiene `add_signal_handler` ni `split(':')`; `CreateStartInfo` pone el entorno de §6.1 y pasa rutas con espacios/acentos como un solo argumento.
- [ ] T5. `ToolStatus` serializa `pymobiledevice3_version` con ese nombre exacto (con las mismas `JsonSerializerOptions` de `Program.cs`).
- [ ] T6. (Si `python3` con Pillow y ffmpeg están en el PATH de la máquina; si no, `Skip` con motivo) `DvtRecorder` con `--synthetic`: arranca, para por stdin en < 10 s, `Frames > 0`, MP4 > 0 bytes. En la Mac del implementador debería correr (Homebrew tiene ambos).

### 11.3 Backend — empaquetado, CI y guía

- [ ] P1. `packaging/portable/ios-win/requirements.in`, `hexdump.txt`, `generar-lock.py` (§7.2).
- [ ] P2. Correr `generar-lock.py` en la Mac y commitear `requirements-win.lock` (con hashes). Anotar en `progress/impl_backend_ios-herramientas-windows.md` la cantidad de paquetes y las versiones de `pywin32`, `av`, `lzfse`.
- [ ] P3. `deploy/windows/armar-tatana-portable.sh` paso 5 (§7.3), aviso de uxplay, chequeos del zip y de `Version: 10.7.4`, nota "funciona en Linux".
- [ ] P4. `.github/workflows/tatana-windows.yml` (§8.1), acciones fijadas por SHA (las de `verificar.yml` + `setup-python`, `upload-artifact`, `download-artifact` con SHA de un release verificado).
- [ ] P5. `ci/tatana-windows/prueba-humo.ps1` (§8.2).
- [ ] P6. `verificar.yml`: paso `py_compile` (§8.3).
- [ ] P7. `docs/instalacion-windows.md` §8 (§7.5).
- [ ] P8. Armar el portátil una vez en la Mac contra un árbol limpio del branch (`git archive HEAD | tar -x -C <tmp>`; `--salida` en el scratchpad, **nunca** en `deploy/*/dist`) y anotar el tamaño del zip y la salida de los chequeos.

### 11.4 Frontend (`implementer-frontend`, **solo `client/`**)

- [ ] F1. `src/lib/agent.ts`: tipos de §9.1, códigos nuevos en `AgentErrorCode`, D7 en las 4 funciones, `enableIosDeveloperMode`.
- [ ] F2. `src/lib/agent-messages.ts`: textos de §9.2.
- [ ] F3. `src/hooks/useAgentIosStatus.ts` (§9.3).
- [ ] F4. `src/components/DeviceConnect.tsx`: banner del servicio de Apple.
- [ ] F5. `src/components/IOSModePicker.tsx`: tarjeta AirPlay deshabilitada con motivo (accesible: `disabled`, `aria-disabled`, `aria-describedby`).
- [ ] F6. `src/components/CaptureStep.tsx`: prop nueva → `IOSModePicker` y "Espejar para capturas" deshabilitado con el texto.
- [ ] F7. `src/app/dashboard/page.tsx`: cablear el hook (§9.4).
- [ ] F8. `src/components/usb-guide/{types.ts,data.ts,IOSGuide.tsx}` + `DeveloperModeAction.tsx` (§9.5).
- [ ] F9. Constancia de los skills en `progress/impl_frontend_ios-herramientas-windows.md`.

---

## 12. Verificación

### 12.1 Antes de `done`, en la Mac

**`implementer-backend`:**
```bash
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
dotnet test  server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj
python3 -m py_compile server/src/Factum.Agent/Services/Ios/ios_helper.py
# autoprueba en la Mac (omite los imports de Windows; usa el Python/ffmpeg de Homebrew):
dotnet run --project server/src/Factum.Agent -- --autoprueba-ios "<scratchpad>/autoprueba"
bash -n deploy/windows/armar-tatana-portable.sh
python3 packaging/portable/ios-win/generar-lock.py --check   # el lock commiteado coincide
# P8: armar el portátil desde un árbol limpio, salida en el scratchpad
```
Con Mock: los endpoints responden como hoy. **No** hace falta iPhone para declarar `done`.
**No** escribir en Mongo ni en `Storage`; la autoprueba solo escribe en la carpeta que se le pasa.

**`implementer-frontend`:**
```bash
cd client && npx tsc --noEmit
```
y revisión visual con Tatana en mock (`/health` mock: AirPlay disponible) y con un `/health`
simulado sin AirPlay (por ejemplo, interceptando la respuesta en DevTools) para ver la tarjeta
deshabilitada y el banner del servicio.

**`./ops/harness/verify.sh`** lo corre el orquestador antes de `arquitectura_lista` y de `aprobada`.

### 12.2 CI (después del push del branch)

El orquestador dispara `tatana-windows.yml` con `workflow_dispatch` sobre `feat/ios-herramientas-windows`
(`gh workflow run tatana-windows.yml --ref feat/ios-herramientas-windows`) y no aprueba la HU
hasta que los dos jobs pasen. `verificar.yml` corre en el PR como siempre.

### 12.3 Prueba manual (usuario y perito piloto, DP2)

**En la Mac (usuario), regresión "La Mac sigue igual":** con el iPhone de siempre:
1. `/health` → `tools.python.pymobiledevice3_version = "10.7.4"`, `ios.apple_service = "ok"`,
   `ios.airplay_available = true` (si uxplay está).
2. Captura de pantalla → PNG. La primera vez el log muestra `iPhone …: Modo Desarrollador activo,
   imagen de desarrollador mounted` (hoy el DDI no está montado, A7).
3. `video_only` 60 s → MP4 que se reproduce entero; el stop tarda < 10 s; aparecen "Suavizado" y "MCI".
4. `with_mic`, `on_device` y `airplay` → como antes.
5. Guía de iPhone → "Activar Modo Desarrollador" → `El Modo Desarrollador ya está activo en …`.

**En Windows (perito piloto), con Tatana 1.3.0 de prueba armado por el orquestador:**
0. Sin "Apple Devices": conectar el iPhone → banner "Falta el servicio de Apple en esta PC"; el
   iPhone no aparece. Instalar "Apple Devices" → en ≤ 10 s desaparece el banner y aparece el
   iPhone con su nombre con acentos bien.
1. Comandos de referencia en `cmd`, **sin** admin (separan "pymobiledevice3 anda" de "Tatana lo invoca bien"):
   ```
   cd %LOCALAPPDATA%\Programs\Tatana\tools\python-embed
   python.exe -m pymobiledevice3 version
   python.exe -m pymobiledevice3 usbmux list
   python.exe -m pymobiledevice3 lockdown info
   python.exe -m pymobiledevice3 amfi developer-mode-status
   python.exe -m pymobiledevice3 mounter auto-mount
   python.exe -m pymobiledevice3 developer dvt screenshot --userspace "%USERPROFILE%\Desktop\prueba.png"
   ```
   Si el último falla, repetirlo con "Ejecutar como administrador" y anotar la diferencia (D3).
2. Guía → "Activar Modo Desarrollador" con un iPhone con código → mensaje `manual_required`;
   prenderlo en Ajustes, reiniciar, "Encender".
3. "Captura" → PNG en la bandeja. Sin abrir Tatana como admin (si hizo falta, que lo haya dicho el mensaje).
4. "Solo pantalla" 60 s → MP4 entero, stop en pocos segundos, después "Suavizado" y "MCI".
5. "Pantalla + micrófono de PC" hablando cerca → MP4 con audio.
6. "Grabación nativa" → el `.mov` aparece en la bandeja.
7. Selector de modos → "Espejo AirPlay" deshabilitado con "No disponible en esta PC" y el motivo;
   "Espejar para capturas" deshabilitado con el texto.
8. Con el iPhone bloqueado → "Captura" muestra "El iPhone está bloqueado…", no "Error tomando screenshot".
9. Iniciar "Solo pantalla" y desenchufar el iPhone → "Detener" muestra "No se pudo guardar la
   grabación del iPhone: …" y no aparece un archivo fantasma.
10. Mandar el log de Tatana (`%LOCALAPPDATA%\Tatana`) y el resultado de cada paso.

### 12.4 Riesgos

- Compatibilidad real de la pila `pmd-pytcp` con Windows en hardware: solo la confirma el piloto (paso 1).
- Hashes/versiones del lock: si PyPI retira un wheel, el armado falla con un error claro (mejor que
  instalar otra versión en silencio, que es lo que pasa hoy).
- Primera captura en una PC sin internet: falla con `ios_ddi_mount_failed` (mensaje lo dice).
- Tamaño del zip: `av` (~30 MB) y `pywin32` (~10 MB) suman; se anota en P8.

> **Decisión del usuario (2026-10-07):** se aceptan DP1, DP2 y DP3 como se recomendó. DP2: se implementa ahora y se verifica con CI en Windows más la revisión; se mergea a `develop` y la HU queda abierta hasta la prueba real con un perito con Windows 11 y un iPhone con iOS 17.4 o posterior, cuando el usuario consiga la PC.
