# HU: Tatana para Linux (AppImage, Android + iPhone)

**Slug:** `tatana-linux` · **Issue:** #91
**Origen:** pedido del usuario del 2026-10-18: hacer para Linux lo que ya se hizo para Windows. En Windows eso fue
primero el portátil (HU #7 `ios-herramientas-windows`) y después el instalador con actualización firmada
(#21 `tatana-instalador-autoupdate`).
**Toca (preliminar, lo confirma la SDD):**
- Agente Tatana (`server/src/Factum.Agent`): mensajes por SO, micrófono, cámara, rutas por defecto, sonda de usbmuxd y
  resolución de herramientas.
- `agent-ui/`: AppImage, actualización, autoarranque y bandeja en Linux, y el asistente de configuración inicial
  según D2 y D7.
- Empaquetado (script nuevo de armado, del estilo de `deploy/windows/armar-tatana-portable.sh`) y CI/release
  (`.github/workflows/`, `ops/tatana/`, `deploy/cloud/scripts/publicar-tatana-forzado.sh`, `Caddyfile`).
- `client/`: solo el enlace de descarga por SO y algún texto de guía, según D9.

`Factum.Backend` no cambia.

**Decisiones ya tomadas por el usuario (no son dudas):**
- Formato **AppImage**, por ser el más general entre distros.
- Un paso de **configuración inicial** que pide la contraseña de administrador **una sola vez** e instala o configura
  lo de sistema: `usbmuxd` (iPhone por USB), reglas `udev` (adb e iPhone sin sudo), `avahi` (AirPlay con uxplay) y
  los permisos de cámara y micrófono (grupos `video`/`audio` o equivalente).
- Android **y** iPhone desde el arranque.
- Distro de los peritos **desconocida**, y puede ser una PC vieja. La HU fija **requisitos mínimos**, no una distro:
  x86-64 obligatorio (.NET no corre en Linux de 32 bits), la glibc mínima de .NET 10 se confirma en la SDD y la matriz
  de prueba sale de D1.

**Como** perito que usa Factum en la nube desde una PC con Linux
**quiero** bajar un único archivo de Tatana, prepararlo una vez con la contraseña de administrador y después capturar
y grabar Android y iPhone como en Windows o en la Mac
**para que** la distro o la antigüedad de la PC no me impidan sumar evidencia a un caso.

---

## Contexto

### Qué existe hoy (arqueología sobre `feat/grabacion-sesion-completa-f2`, que parte de `develop` `04bb57f`)

**Cómo se distribuye Tatana en Windows (el modelo a replicar)**
| # | Pieza | Qué hace | ¿Sirve para Linux? |
|---|---|---|---|
| W1 | `deploy/windows/armar-tatana-portable.sh` | Publica `Factum.Agent` para `win-x64`, self-contained, con `-p:Version`. Arma `tools/` con adb (platform-tools "latest"), scrcpy 4.1 con SHA-256 fijo y el adb propio quitado, Python embebido 3.11.9 + pymobiledevice3 **10.7.4** desde un lock con hashes (`packaging/portable/ios-win/`), ffmpeg de BtbN y uxplay solo si hay `UXPLAY_WIN_ARTIFACT_URL`. Hornea `AllowedOrigins`, verifica `Mock = false` y chequea el contenido del zip. Ya corre en Linux (lo usa el CI). | Sirve como **modelo**. Todo lo que baja es de Windows; para Linux hacen falta otros binarios y otro lock (D4). |
| W2 | `.github/workflows/tatana-windows.yml` + `ci/tatana-windows/prueba-humo.ps1` | Humo en `windows-latest`: descomprime el zip, arranca Tatana, mira `/health` y corre `--autoprueba-ios` (grabador DVT real sin iPhone). Corre en los PRs que tocan el agente o el empaquetado. | Hay que replicarlo en Ubuntu (D13). |
| W3 | `.github/workflows/tatana-release.yml` | `preparar → payload → instalador (NSIS en windows) → firmar (Ed25519, environment tatana-release) → prueba (windows) → publicar (VPS por SSH + GitHub Release)`. Modos *release* (tag `tatana-vX.Y.Z`, solo desde `main`) y *ensayo* (clave efímera). | Hay que sumarle Linux (D9). |
| W4 | `agent-ui/` (Electron 33, electron-builder 24) | `build.linux.target: AppImage` **ya está declarado** en `package.json`, pero nunca se armó. El agente va en `resources/agent` (`extraResources`). `agent-process.ts` lanza `Factum.Agent` con `--port --data --local-config --mode installed`. | Base para el AppImage. Faltan las piezas de Linux (abajo). |
| W5 | Actualizador firmado (`agent-ui/src/main/updater/*`, `ops/tatana/*`) | Manifiesto `tatana-update.json` firmado con Ed25519 y claves en `trusted-keys.ts`. **`validatePayload` exige `product: 'tatana-windows'` y `file: Tatana-Setup-<v>.exe`**. Antes de instalar consulta `/agent/maintenance`: nunca instala con una captura en curso. Hace `quitAndInstall`. | El mecanismo sirve. El manifiesto y los nombres son solo de Windows (D8). |
| W6 | `deploy/cloud/scripts/publicar-tatana-forzado.sh` + `Caddyfile` | El VPS acepta **exactamente** 5 archivos de Windows (`Tatana-Setup-X.Y.Z.exe`, el `.blockmap`, `latest.yml`, `tatana-update.json` y `SHA256SUMS`). Los sirve en `/tatana/updates/` y `/descargas/tatana/`. | Hay que extenderlo para los archivos de Linux (D9). |
| W7 | `deploy/cloud/armar-tatana-nube.sh` | Portátil de Windows "para la nube". **Retirado** desde la 1.4.0: queda como respaldo manual. | No se replica (ver D7). |
| W8 | Web: `NEXT_PUBLIC_TATANA_DOWNLOAD_URL` (`client/src/lib/agent-messages.ts`) | Hay **un solo** enlace de descarga, el instalador de Windows (`factum.env.example`). | Hace falta un enlace por SO (D9). |

**Qué ya contempla Linux en el agente (confirmado en el código)**
| # | Pieza | Estado en Linux |
|---|---|---|
| L1 | `Common/ToolResolver.cs` | Orden: `tools/<dir>/<nombre Unix>` junto al ejecutable, después PATH, después `/opt/homebrew/bin` y `/usr/local/bin`. Nombres Unix: `adb`, `scrcpy`, `ffmpeg`, `python3`, `uxplay` y `rife-ncnn-vulkan`. **OK.** Dentro del AppImage, `AppContext.BaseDirectory` es el montaje de solo lectura: `tools/` se encuentra igual. |
| L2 | `Common/ProcessStop.cs` | Fuera de Windows manda SIGINT o SIGTERM con el ejecutable `kill` (la sesión usa SIGTERM directo), escala a SIGTERM y al final hace `Kill(entireProcessTree)`. `WindowsConsoleSignal` es solo de Windows y en Linux devuelve sin hacer nada. **Debería andar** en cualquier distro, porque `kill` viene en procps/coreutils. Falta probarlo. `IosService.GracefulStopAsync` también usa `kill`. |
| L3 | `Services/MicCapture.cs` | Linux: `ffmpeg -f alsa -i default`. No hay forma de elegir el dispositivo: `Agent:MicDevice` solo aplica a dshow. **Riesgo:** en las distros actuales el audio es PipeWire o PulseAudio, y un ffmpeg estático puede no cargar el plugin ALSA de la distro (D10). |
| L4 | `Services/Session/CameraArgs.cs`, `CameraDevices.ListLinux()` | Hay backend `v4l2`. El listado recorre `/dev/video*` con el nombre de `/sys/class/video4linux/*/name`. **Riesgo:** cada cámara UVC expone dos nodos (captura y metadatos), así que una misma cámara aparecería dos veces (D11). Con la cámara de la sesión con audio (`WithAudio`), el audio va por `alsa default`. |
| L5 | `SessionOptions.EffectiveCameraEnabled` | La cámara del procedimiento viene **apagada en Windows y Linux** (`CameraEnabled ?? IsMacOS()`). |
| L6 | `Services/Ios/AppleServiceProbe.cs` | En Unix responde "ok" si existe `/var/run/usbmuxd`. **Riesgo:** en Ubuntu y Debian, udev/systemd levanta `usbmuxd` recién cuando se enchufa un dispositivo Apple, así que sin iPhone conectado la sonda dice `missing` aunque esté instalado. |
| L7 | `IosService` / `DvtRecorder` / `ios_helper.py` | El túnel de iOS 17+ es **userspace** (`establish_userspace_rsd`, `--userspace`), así que no necesita root ni un dispositivo TUN. AirPlay (`AirplayAvailable`) se habilita fuera de Windows si se encuentra `uxplay`, que se lanza con `-mp4 <stem>`. `IosSelfTest` (`--autoprueba-ios`) ya corre en macOS y Linux. |
| L8 | `Services/Ios/IosErrors.cs`, `AdbService` | Los textos se eligen con `isWindows`, así que **Linux recibe los de la Mac**: "servicio de dispositivos de **macOS** (usbmuxd)", "Python… en esta **Mac**", "Instalá: **brew install uxplay**". `IosService` L780 también menciona brew. |
| L9 | `IosHelper.PythonCandidates()` | `tools/python-embed/python3` → PATH → `/usr/bin/python3` y otros. Exige que importe pymobiledevice3 **10.7.4** (`ExpectedPymobiledevice3Version`). |
| L10 | `AgentOptions.DefaultEvidenceDirectory` | Linux: `~/Factum/Evidencia`. |
| L11 | `FolderReveal` | Linux: `xdg-open <carpeta>`. |
| L12 | `HealthController.ResolveMode` | Con `--mode installed` (Electron) responde `installed`. |

**Qué falta en `agent-ui/` para Linux (confirmado en el código)**
- `agentDataDir()`: en Linux empaquetado usa `app.getPath('userData')/agent-data`, o sea `~/.config/Tatana/agent-data`.
  Es la carpeta de **configuración**, no de datos (D14).
- `applyLoginItem` usa `app.setLoginItemSettings`, que **en Linux no hace nada**. No hay autoarranque (D15).
- La app se esconde en la bandeja. En GNOME sin la extensión AppIndicator (Debian, Fedora) **no hay bandeja**, y el
  perito no tiene cómo volver a abrir la ventana (D15).
- `stopAndWait` en Linux manda `SIGTERM` solo al agente. El cierre de adb/scrcpy/ffmpeg antes de actualizar
  (`update-manager.ts` L474) está escrito solo para Windows.
- La pantalla "Librerías" (`ipc.ts`) ofrece instalar con `sudo apt-get install -y adb` y `pip3 install pymobiledevice3`
  por shell. En un AppImage que trae sus herramientas eso confunde y puede romper la versión fija (ver Notas).
- `scripts/publish-agent.sh` ya sabe publicar `linux-x64`, pero en *single-file* y sin `tools/`.

### Por qué hace falta
Hay peritos con PCs Linux (muchas veces viejas, de distro variada) que hoy no pueden usar Factum en la nube, porque
no hay Tatana para Linux. Windows ya tiene instalador, actualización firmada y CI. Linux tiene que llegar a lo mismo
sin pedirle al perito que compile, use la terminal ni sepa qué es usbmuxd.

### Qué es nuevo
1. Un **AppImage de Tatana** (`linux-x64`) que trae el agente y sus herramientas (adb, scrcpy, ffmpeg, Python +
   pymobiledevice3 fijo) y, según D7, la UI de Electron.
2. Una **configuración inicial** con una sola contraseña de administrador, que deja la PC lista para Android, iPhone,
   AirPlay, cámara y micrófono, y que se puede **verificar** y repetir (D2, D3).
3. **Requisitos mínimos** publicados y una matriz de distros de prueba (D1).
4. **Mensajes y comportamientos propios de Linux** en el agente: ni brew ni "Mac" (L8), sonda de usbmuxd correcta
   (L6), micrófono (D10) y cámara (D11).
5. **CI y release** para Linux: humo en Ubuntu sin dispositivo, publicación firmada junto con Windows y actualización
   (D8, D9, D13).

---

## Criterios de aceptación

```gherkin
Feature: Tatana para Linux en AppImage, con Android e iPhone

  Background:
    Given una PC x86-64 con una distro de la matriz de prueba (D1) y escritorio gráfico
    And Factum abierto en Chrome, Chromium o Firefox (D1) en https://<dominio>
    And el AppImage de Tatana descargado de https://<dominio>/descargas/

  # ── Requisitos y arranque ────────────────────────────────────────────────
  Scenario: PC que no cumple los requisitos mínimos
    Given una PC de 32 bits, o con una glibc menor que la mínima (D1)
    When el perito intenta abrir el AppImage
    Then ve un mensaje en castellano que dice qué requisito no se cumple
    And la página de descarga lista los requisitos mínimos antes de bajar el archivo

  Scenario: Primer arranque sin libfuse2
    Given una distro sin libfuse2 instalado (por ejemplo Ubuntu 22.04 o posterior recién instalado)
    When el perito marca el AppImage como ejecutable y lo abre con doble clic
    Then Tatana arranca sin que el perito tenga que instalar nada antes (D6)

  Scenario: Primer arranque en Ubuntu 24.04 (sandbox de Chromium)
    Given Ubuntu 24.04, que restringe los user namespaces sin privilegios
    When el perito abre el AppImage
    Then la ventana de Tatana abre (no se cierra sola con un error de chrome-sandbox) (D6)

  # ── Configuración inicial ────────────────────────────────────────────────
  Scenario: Configuración inicial con una sola contraseña
    Given la PC sin usbmuxd, sin reglas udev para Android ni Apple y sin avahi
    When el perito abre Tatana por primera vez y acepta "Preparar esta PC"
    Then el sistema le pide la contraseña de administrador una sola vez (D2)
    And se instalan o configuran usbmuxd, las reglas udev de Android y Apple, avahi y los permisos de cámara y micrófono (D3)
    And al terminar Tatana muestra la lista de chequeos con el resultado de cada uno
    And si hace falta cerrar sesión para que los permisos se apliquen, Tatana lo dice con claridad

  Scenario: Configuración inicial ya hecha
    Given una PC donde la configuración inicial ya corrió bien
    When el perito vuelve a abrir Tatana, o lo actualiza
    Then no se le vuelve a pedir la contraseña
    And la lista de chequeos sigue disponible en la ventana para verla de nuevo

  Scenario: Distro sin soporte en la configuración inicial
    Given una distro cuyo gestor de paquetes no está entre los soportados (D3)
    When el perito pide "Preparar esta PC"
    Then Tatana le muestra la lista exacta de paquetes y reglas que hacen falta, para que se lo pase a su soporte técnico
    And Tatana no ejecuta nada a medias

  Scenario: El perito cancela la contraseña
    When el perito cierra el pedido de contraseña sin ingresarla
    Then Tatana sigue abierto y le explica qué no va a funcionar (iPhone, Android sin permisos)
    And le ofrece reintentar

  # ── Android ──────────────────────────────────────────────────────────────
  Scenario: Android por USB sin sudo
    Given la configuración inicial hecha y un Android con depuración USB autorizada
    When el perito abre el paso de captura en Factum
    Then el Android aparece en la lista sin correr nada como root
    And la captura de pantalla y la grabación con scrcpy funcionan como en Windows
    And al detener, el MP4 queda completo y se reproduce entero

  # ── iPhone ───────────────────────────────────────────────────────────────
  Scenario: El iPhone aparece en Factum
    Given la configuración inicial hecha y un iPhone con iOS 17 o posterior, desbloqueado y con "Confiar" aceptado
    When el perito abre el paso de captura
    Then el iPhone aparece con nombre, modelo y versión de iOS, con los acentos bien escritos

  Scenario: Captura y grabación de iPhone
    Given el iPhone con Modo Desarrollador activo
    When el perito saca una captura y graba 60 s en "Solo pantalla", "Pantalla + micrófono de PC" y "Grabación nativa"
    Then se generan el PNG y los MP4, como en Windows y en la Mac
    And ninguno de esos pasos pide la contraseña de administrador

  Scenario: Falta usbmuxd
    Given una PC donde la configuración inicial no corrió, o falló en usbmuxd
    When el perito conecta un iPhone
    Then Factum le indica que corra la configuración inicial de Tatana
    And el mensaje no menciona macOS, brew, Terminal ni iTunes

  Scenario: usbmuxd instalado sin iPhone conectado
    Given usbmuxd instalado pero sin un dispositivo Apple enchufado (el servicio todavía no arrancó)
    When se consulta /health
    Then el estado del servicio de Apple no se informa como "missing" (L6)

  Scenario: AirPlay en Linux (según D5)
    When el perito abre el selector "Modo de grabación iOS"
    Then "Espejo AirPlay" funciona con el uxplay que se decida en D5, o aparece deshabilitado con el motivo
    And ningún mensaje menciona brew

  # ── Grabación de la sesión completa ──────────────────────────────────────
  Scenario: Sesión completa en la PC mínima (según D12)
    Given la PC de referencia mínima (D1) con la configuración inicial hecha
    When el perito graba una sesión de 30 minutos con pantalla, micrófono y (si D11 la habilita) cámara
    Then los tramos se cierran sin huecos que no sean desconexiones reales
    And Tatana no deja la PC sin respuesta (la web sigue usable)

  Scenario: Micrófono en una distro con PipeWire
    Given una distro con PipeWire o PulseAudio (Ubuntu 22.04+, Fedora, Mint 21+)
    When el perito graba con micrófono de la PC
    Then el MP4 tiene el audio del micrófono que el sistema tiene como entrada por defecto, o el que se elija según D10

  # ── Ciclo de vida ────────────────────────────────────────────────────────
  Scenario: Arranque al iniciar sesión
    When el perito inicia sesión en su escritorio
    Then Tatana arranca solo, sin abrir la ventana (D15)
    And si el escritorio no tiene bandeja, el perito tiene una forma documentada de volver a abrir la ventana

  Scenario: Actualización automática (según D8)
    Given Tatana vN en Linux y vN+1 publicada para Linux
    When Tatana chequea actualizaciones
    Then descarga vN+1, verifica la firma Ed25519 del manifiesto de Linux y el hash del AppImage
    And la aplica sin una captura en curso, sin downgrade y sin pedir la contraseña
    And la evidencia, la configuración local y la configuración inicial siguen intactas

  Scenario: Cerrar o actualizar no deja procesos huérfanos
    When Tatana se cierra o se actualiza
    Then no quedan adb, scrcpy, ffmpeg ni python de Tatana corriendo

  Scenario: La evidencia no vive dentro del AppImage
    When Tatana guarda archivos de un caso
    Then quedan en la carpeta de datos de Linux decidida en D14 y el ZIP en la carpeta de evidencia (~/Factum/Evidencia o la de D14)
    And borrar o reemplazar el AppImage no los toca

  # ── Publicación y verificación ───────────────────────────────────────────
  Scenario: Humo en CI sin dispositivo (D13)
    When un PR toca el agente, agent-ui o el empaquetado de Linux
    Then un job en ubuntu arma el AppImage, arranca Tatana desde él y comprueba /health
    And comprueba que adb, scrcpy, ffmpeg y python con pymobiledevice3 10.7.4 se resuelven desde tools/
    And corre --autoprueba-ios y una grabación de prueba con ffmpeg (lavfi) que se corta limpio con SIGTERM
    And repite el arranque del agente en contenedores de las distros más viejas de la matriz (D1)

  Scenario: Publicar una versión
    When el usuario dispara una release de Tatana (D9)
    Then se arman, prueban, firman y publican el instalador de Windows y el AppImage de Linux según D9
    And si falla la prueba de Linux no se publica el AppImage, y la versión de Linux anterior sigue vigente

  # ── Lo que ya andaba ─────────────────────────────────────────────────────
  Scenario: Windows y macOS siguen igual
    Given Tatana en Windows (instalador) y en macOS
    Then su comportamiento, sus mensajes y su canal de actualización son los mismos que antes de esta HU
```

---

## Datos que se registran

**Sin cambios de modelo en Mongo.** Los archivos de evidencia son los mismos de siempre.

**En la PC del perito**
| Dato | Obligatorio | Uso |
|---|---|---|
| Resultado de la configuración inicial (fecha, versión de Tatana, chequeos ok/falla y motivo) | Sí | Saber si hay que volver a pedir la contraseña, mostrar la lista de chequeos y diagnosticar a distancia. Va en la carpeta de config de Tatana (D14). |
| Log de la configuración inicial (comandos ejecutados, salida) | Sí | Soporte: saber qué se instaló en la PC del perito. |
| Distro, versión, glibc y gestor de paquetes detectados | Sí | Log y, según D13, `/health` o `/info`, para soporte. |
| Micrófono elegido (si D10 lo habilita) | No | Grabaciones con micrófono de la PC. |

**Manifiesto de Linux** (formato exacto en la SDD, según D8): versión, archivo (`Tatana-<v>-x86_64.AppImage` o el que
fije la SDD), tamaño, SHA-512/SHA-256, `product: "tatana-linux"` y firma Ed25519 con la **misma clave** que Windows.

**Contrato `/health`** (cambio aditivo, snake_case, según D13): por ejemplo `os`, `distro` y el estado de la
configuración inicial. Los nombres exactos van en la SDD.

---

## Diseño UX/UI

### `agent-ui/` (Electron, empaquetado como AppImage)
- **Descarga y primer arranque.** La guía y la página de descarga dicen: bajá el archivo, clic derecho →
  Propiedades → "Permitir ejecutar como programa" (con el texto de GNOME, KDE y Cinnamon), doble clic. Si D8 lo
  pide, Tatana se ofrece a moverse a `~/Applications` (o la carpeta que fije la SDD) y a crear su acceso en el menú.
- **Asistente "Preparar esta PC"**, primera vez:
  - una pantalla que explica en una línea por qué se pide la contraseña ("para que la PC reconozca iPhone y Android
    por USB sin pedirte la clave cada vez");
  - el botón **"Preparar esta PC"** abre el pedido de contraseña del sistema (D2);
  - un progreso por paso: paquetes, reglas USB, AirPlay, cámara/micrófono;
  - al final, una **lista de chequeos** con un ícono por fila ("iPhone por USB: listo", "Android por USB: listo",
    "AirPlay: no disponible (motivo)", "Micrófono: listo", "Cámara: listo");
  - si hace falta cerrar sesión para que los permisos tomen efecto, un aviso destacado.
- **Estados de error del asistente:**
  - contraseña cancelada → "Sin la contraseña, Tatana no puede reconocer iPhone ni Android por USB." + "Reintentar";
  - sin internet → "Hace falta internet para instalar los paquetes del sistema.";
  - distro sin soporte → la lista de paquetes y reglas para copiar ("Copiar para soporte técnico");
  - un paso falló → la fila en rojo con el motivo y "Ver detalle" (el log).
- **Ventana principal:** la versión, el estado del agente y un acceso a "Chequeos de esta PC" (se vuelve a correr
  sin contraseña; solo pide la clave si hay algo para arreglar).
- **Bandeja y autoarranque:** igual que en Windows donde haya bandeja. Si no hay, según D15.
- **Actualización:** los mismos estados y avisos que en Windows (la línea de estado en la ventana y la notificación
  una vez por versión).
- La sección **"Librerías"** (instalar adb y pymobiledevice3 por shell) no se muestra en el AppImage (ver Notas).

### `client/` (web)
- El enlace **"Descargar Tatana"** ofrece la versión del SO del perito (Windows o Linux), con un enlace a la otra
  (D9).
- Los mensajes de iPhone y Android que manda Tatana en Linux se muestran tal cual: la web no los reescribe.
- Sin pantallas nuevas.

### Guía
`docs/despliegue-nube.md`: una sección "Tatana en Linux" con los requisitos mínimos, la descarga, cómo marcarlo
ejecutable, la configuración inicial, qué hacer si la distro no está soportada y cómo publicar una versión de Linux.

---

## Fuera de alcance
- Linux de 32 bits y ARM (aarch64/Raspberry Pi), incluidos los PC ARM con Linux.
- Paquetes `.deb`, `.rpm`, Flatpak o Snap. Solo AppImage.
- La **instalación local** de Factum (Docker en la PC) sobre Linux: `deploy/windows/` sigue siendo solo de Windows.
- Distros sin systemd o sin escritorio gráfico, y sesiones remotas (SSH, X forwarding).
- Firma GPG del AppImage o integración con AppImageHub. La integridad la cubre el manifiesto Ed25519.
- qvh y rife-ncnn-vulkan en Linux, igual que en Windows.
- La foto de webcam por Tatana (`WebcamService`), que la web no usa.
- Mejorar los ~2 FPS de `video_only` en iPhone (es un límite de iOS).
- Cambiar los modos, la marca o la UI de Tatana más allá de lo que Linux necesite.

---

## Notas de implementación (mínimas; el detalle es de la SDD)
- **Una sola fuente para el armado:** conviene un script de Linux hermano de `armar-tatana-portable.sh` (por ejemplo
  `deploy/linux/armar-tatana-linux.sh`) que deje `resources/agent/` con `Factum.Agent` (`linux-x64`, self-contained,
  **no single-file** si `tools/` y el `appsettings.json` tienen que ir al lado) y `tools/`, con las mismas guardas:
  `Mock = false`, orígenes horneados, sin `appsettings.Local.json` y chequeo del contenido.
- **Versiones fijas y avisos de terceros** (scrcpy, pymobiledevice3 y su lock, ffmpeg) con SHA-256, igual que en
  Windows. Para Linux, scrcpy publica `scrcpy-linux-x86_64-v<versión>.tar.gz`. El lock de Python es distinto del de
  Windows (`manylinux` / `x86_64`).
- **Permisos de ejecución:** el armado conserva el bit `+x` de `Factum.Agent`, adb, scrcpy, ffmpeg y python dentro del
  AppImage. Un `zip` creado en la Mac lo puede perder.
- **Señales:** `ProcessStop` e `IosService.GracefulStopAsync` dependen del ejecutable `kill`. Evaluar mandar la señal
  directo (P/Invoke a `kill(2)`) para no depender de procps. `stopAndWait` de Electron tiene que cortar el árbol entero
  (grupo de procesos) en Linux, como `taskkill /T` en Windows.
- **Mensajes por SO:** `IosErrors`, `AdbService` e `IosService` hoy reciben `isWindows`. Linux necesita textos propios
  (por ejemplo "Abrí Tatana y tocá 'Preparar esta PC'").
- **Sonda de usbmuxd (L6):** distinguir "no instalado" (no hay binario ni unidad de systemd) de "instalado sin iPhone
  conectado" (todavía no hay socket).
- **Reglas udev:** preferir `TAG+="uaccess"` (logind da acceso al usuario de la sesión activa sin grupos ni cerrar
  sesión) antes que grupos como `plugdev`/`video`. Lo decide la SDD por distro.
- **Pantalla "Librerías" de `agent-ui`** (`ipc.ts`): instala con `sudo apt-get` y `pip3` por shell, lo que en el
  AppImage rompería la versión fija de pymobiledevice3. Ocultarla empaquetado en Linux, como mínimo.
- **El AppImage es de solo lectura:** nada escribe junto al ejecutable. La config local va en `--local-config`
  (ya existe) y los scripts temporales de iOS, en `$TMPDIR`.
- **Tests:** `server/tests/Factum.Agent.Tests` corre en Ubuntu (`verificar.yml`). Ahí van los textos por SO, la sonda
  de usbmuxd, el parseo de los dispositivos de micrófono y cámara de Linux y el cierre con señales. El updater de
  `agent-ui` ya tiene tests (`src/main/updater/*.test.ts`) y suma los de `product: "tatana-linux"`.
- Prueba manual de referencia en Linux con el iPhone conectado (los subcomandos los ajusta el architect):
  ```
  ./Tatana-*.AppImage --appimage-extract    # o el modo de D6
  squashfs-root/resources/agent/tools/python/bin/python3 -m pymobiledevice3 usbmux list
  squashfs-root/resources/agent/tools/python/bin/python3 -m pymobiledevice3 lockdown info
  squashfs-root/resources/agent/Factum.Agent --autoprueba-ios /tmp/autoprueba
  ```

---

## Dudas para validar con el usuario

**D1. Requisitos mínimos y matriz de distros de prueba.**
- a) **Requisitos publicados:** x86-64 (64 bits), glibc ≥ la que exija .NET 10 o Electron, la mayor de las dos. La SDD
  la confirma; se estima 2.31–2.35, es decir Ubuntu 20.04/22.04 o Debian 11/12. Además, CPU con el conjunto de
  instrucciones que pida .NET 10: la SDD confirma si es x86-64-v2 (SSE4.2), lo que dejaría afuera CPUs anteriores a
  ~2009. También 4 GB de RAM (8 GB recomendados para la sesión completa), 10 GB libres, systemd y escritorio
  X11 o Wayland. **Matriz de prueba:** Ubuntu 22.04 y 24.04 LTS, Debian 12, Linux Mint 21/22 (base Ubuntu) y Fedora
  (la última). Las tres primeras van en CI (D13) y todas en la prueba con hardware.
- b) Solo Ubuntu 22.04/24.04 y derivadas (Mint, Pop!_OS, Zorin). Las demás distros, "puede andar, sin soporte".
- c) Apuntar más atrás (Ubuntu 18.04 o Debian 10) por las PCs viejas.
- **Recomendada: a).** Cubre la mayoría de las PCs reales de oficina (familia Ubuntu/Mint y Debian) y deja una
  RPM de muestra. c) choca con .NET 10 y con Electron 33, que ya no soportan glibc tan viejas: una PC vieja sirve si
  le instalan una distro de la matriz. **Preguntas:** ¿sabés de alguna distro concreta que usen los peritos (por
  ejemplo, una imagen institucional)? ¿Hay que soportar Fedora/RPM desde el día uno o puede ser "best effort"?

**D2. Cómo se corre la configuración inicial.**
- a) **Desde la ventana de Tatana, con `pkexec`.** El sistema muestra su propio pedido de contraseña (polkit). Corre
  un script de Tatana, versionado y con hash verificado antes de ejecutarlo, que instala paquetes y reglas y escribe
  el resultado. Si la distro no tiene polkit o un agente gráfico, se cae a la opción b).
- b) Un comando para pegar en la terminal (`sudo ./Tatana.AppImage --configurar`), que la ventana muestra con un botón
  "Copiar".
- c) Sin configuración automática: una guía con los comandos por distro.
- **Recomendada: a) con b) de respaldo.** Es lo que pide el usuario (la contraseña una sola vez, sin terminal), y
  polkit viene con todos los escritorios de la matriz. b) cubre los casos raros sin un tercer camino. La verificación
  ("Chequeos de esta PC") corre **sin** contraseña: mira paquetes, reglas, servicio y grupos, y solo pide la clave si
  hay algo para arreglar. **Pregunta:** ¿te sirve que, si la configuración agrega al usuario a un grupo, Tatana pida
  cerrar sesión una vez? Con `uaccess` (ver Notas) eso no debería hacer falta para USB, pero quizás sí para la cámara
  en alguna distro.

**D3. Qué viene del gestor de paquetes y qué distros cubre la configuración inicial.**
- a) **Del sistema:** `usbmuxd` (es un servicio de root con udev; no se puede empaquetar dentro del AppImage),
  `avahi-daemon` (mDNS para AirPlay), las reglas udev de Android (si la distro las tiene, `android-sdk-platform-tools-common`
  o equivalente; si no, una regla propia de Tatana) y la apertura de puertos de AirPlay en `ufw`/`firewalld` si
  están activos. **Dentro del AppImage:** adb, scrcpy, ffmpeg y Python + pymobiledevice3 (D4). Para uxplay, ver D5.
  **Gestores soportados:** `apt` (Debian, Ubuntu, Mint) y `dnf` (Fedora). Con `pacman` y `zypper` se muestra la lista
  para soporte técnico.
- b) Igual que a), soportando además `pacman` y `zypper` desde el inicio.
- c) Todo del sistema (adb, scrcpy, ffmpeg y python también).
- **Recomendada: a).** Lo que corre como root o necesita udev solo puede venir del sistema; lo demás va dentro para
  tener versiones fijas (trazabilidad forense, igual que en Windows). c) dejaría scrcpy y pymobiledevice3 en versiones
  distintas en cada distro (en Ubuntu 22.04, scrcpy es la 1.21). b) suma dos familias que no están en la matriz y que
  nadie va a probar con hardware.

**D4. Python y pymobiledevice3 en Linux.**
- a) **Python portable dentro del AppImage** (python-build-standalone 3.11, `x86_64-unknown-linux-gnu`) con
  pymobiledevice3 10.7.4 desde un **lock de Linux con hashes**, igual que `ios-win/`.
- b) Usar el `python3` del sistema y crear un `venv` en la carpeta de datos durante la configuración inicial.
- **Recomendada: a).** Cada distro trae un Python distinto: 3.10 en Ubuntu 22.04, 3.12 en 24.04, 3.11 en Debian 12.
  Varias bloquean `pip` (PEP 668) y algunas no traen `venv`. Con a), Linux usa la misma versión probada que Windows y
  `IosHelper` la sigue validando como hoy.

**D5. AirPlay (uxplay) en Linux.**
- a) Usar el `uxplay` del gestor de paquetes. **Riesgo:** Tatana lo lanza con `-mp4 <stem>`, y hay que confirmar desde
  qué versión existe esa opción. Ubuntu 22.04 trae una versión vieja y es probable que no la tenga.
- b) **Degradación prolija, como en Windows:** AirPlay sale disponible solo si el `uxplay` encontrado soporta `-mp4`
  (Tatana lo comprueba). Si no, la tarjeta aparece deshabilitada con el motivo ("Tu distro trae una versión de
  uxplay sin grabación"). Los modos por USB son el objetivo de la HU.
- c) Compilar uxplay en CI y meterlo en el AppImage (GStreamer y licencias GPL/LGPL incluidos).
- **Recomendada: b),** con a) como fuente cuando la versión alcance. avahi y el firewall igual se configuran en la
  configuración inicial, así AirPlay anda donde la distro tenga un uxplay nuevo (Ubuntu 24.04, Fedora). c) mete GStreamer
  completo en el AppImage (cientos de MB y plugins por hardware) y las obligaciones de la GPL. Si hace falta, va en
  una HU propia, igual que se decidió para Windows.

**D6. Arranque del AppImage: libfuse2 y sandbox de Chromium.**
- a) **Armar con un runtime de AppImage que no dependa de libfuse2** (runtime estático type 2, si la versión de
  electron-builder lo permite; si no, actualizar electron-builder). Para el sandbox de Chromium en Ubuntu 24.04
  (AppArmor restringe los user namespaces sin privilegios), la configuración inicial instala un **perfil de AppArmor**
  para el AppImage y, mientras tanto, el primer arranque usa un lanzador que lo maneja.
- b) Pedir `libfuse2` como requisito y documentar `--no-sandbox` para Ubuntu 24.04.
- c) Arrancar siempre con `--no-sandbox` / `--appimage-extract-and-run`.
- **Recomendada: a).** Sin esto, en Ubuntu 22.04/24.04 recién instalados el doble clic **no hace nada** o la ventana
  se cierra sola, y el perito no llega a la configuración inicial. b) obliga a usar la terminal antes de la primera
  pantalla. c) apaga el sandbox de Chromium para siempre, en una app que muestra contenido web. La SDD confirma qué
  versión de electron-builder trae el runtime estático y cómo se resuelve el primer arranque antes de que exista el
  perfil.

**D7. Qué se distribuye: UI de Electron + agente, o solo el agente.**
- a) **Un solo AppImage con la UI de Electron (`agent-ui`) y el agente adentro,** igual que el instalador de Windows.
  Trae la ventana, el asistente de configuración inicial, la bandeja y la actualización firmada que ya existen.
- b) Un AppImage solo del agente (sin ventana), con la configuración inicial por terminal.
- c) Los dos.
- **Recomendada: a).** Reusa el actualizador firmado, la protección "nunca durante una captura" y la UI de estado,
  ya probados en Windows. El asistente de configuración inicial necesita una ventana. b) deja al perito sin interfaz
  para avisos y actualizaciones, y c) duplica el trabajo de CI y de soporte.

**D8. Actualización del AppImage.**
- a) **electron-updater con el AppImage** (reemplaza el archivo en su lugar) + **manifiesto propio de Linux**
  (`tatana-update-linux.json`, `product: "tatana-linux"`, firmado con la misma clave Ed25519 y verificado igual que
  en Windows). Tatana pide, la primera vez, moverse a una carpeta del usuario donde se pueda escribir
  (`~/Applications`) y crea su `.desktop` en el menú.
- b) Sin actualización automática: la web avisa cuando hay una versión nueva y el perito la baja a mano.
- **Recomendada: a).** El motivo es el mismo que en Windows: el cambio de dominio y las correcciones tienen que
  llegar solos. electron-updater soporta AppImage. Mover el archivo evita que la actualización falle porque quedó en
  `Descargas` o en un pendrive. La configuración inicial no se repite al actualizar.
  **Pregunta:** ¿el chequeo cada 6 h y "se instala al reiniciar Tatana" de Windows aplican igual?

**D9. Release, publicación y descarga.**
- a) **Mismo workflow `tatana-release.yml` y misma versión para Windows y Linux.** Se suman jobs `appimage` (ubuntu)
  y `prueba-linux`. Si falla cualquiera de las dos pruebas, no se publica nada (versión única). En el VPS,
  `publicar-tatana-forzado.sh` acepta además el AppImage, `latest-linux.yml` y `tatana-update-linux.json`. La web
  muestra el enlace del SO del perito (se suma `NEXT_PUBLIC_TATANA_DOWNLOAD_URL_LINUX` o un enlace a una página
  `/descargas/` con los dos).
- b) Igual que a), pero si falla la prueba de Linux se publica igual Windows (versiones por plataforma, que pueden
  divergir).
- c) Workflow y versión aparte para Linux (`tatana-linux-vX.Y.Z`).
- **Recomendada: a).** Una versión de Tatana significa lo mismo en las dos plataformas, que ya registran la versión
  real en el informe (`agent_version`) y en la auditoría. Además, la versión mínima del servidor (D6 de
  tatana-instalador-autoupdate) es una sola. Si alguna vez Linux bloquea un arreglo urgente de Windows, se
  reconsidera b). **Pregunta:** ¿el portátil de respaldo manual (`armar-tatana-nube.sh`) necesita un equivalente para
  Linux? Recomendado: no; el AppImage ya es un solo archivo portátil.

**D10. Micrófono de la PC en Linux.**
- a) **Usar PulseAudio/PipeWire (`-f pulse`) con el dispositivo de entrada por defecto del sistema** cuando el ffmpeg
  empaquetado lo soporte, y `alsa default` de respaldo. El perito elige el micrófono en la configuración de sonido de su
  escritorio. `Agent:MicDevice` se acepta también en Linux para casos de soporte.
- b) Dejar `alsa default` como hoy.
- c) Un selector de micrófono en la UI de Tatana o en la web.
- **Recomendada: a).** Todas las distros de la matriz usan PipeWire o PulseAudio. Con un ffmpeg estático, `alsa default`
  puede abrir un dispositivo equivocado o fallar, y la grabación quedaría sin audio. Usar el micrófono por defecto
  del escritorio es lo que el perito ya sabe cambiar. c) sirve en las tres plataformas y le corresponde una HU propia.
  La SDD confirma que el ffmpeg elegido trae `libpulse`.

**D11. Cámara del procedimiento (sesión completa) en Linux.**
- a) **Mantenerla apagada por defecto en Linux** (`CameraEnabled ?? IsMacOS()`, igual que Windows) hasta una QA con
  hardware, pero dejarla lista: el listado muestra solo los nodos de captura de video (no los de metadatos) y la
  configuración inicial deja el permiso de `/dev/video*`.
- b) Encenderla por defecto en Linux.
- **Recomendada: a).** Es el mismo criterio que Windows. Hoy cada cámara aparecería duplicada (L4) y nadie la probó en
  Linux. Se enciende con `Agent__Session__CameraEnabled=true` para la prueba piloto. **Pregunta:** si para la fecha
  de esta HU la fase 2 (#85) ya habilitó la cámara en Windows, ¿se habilita también en Linux con la QA de esta HU?

**D12. Rendimiento en una PC vieja.**
- a) **Fijar una "PC de referencia mínima"** (por ejemplo, Core i3 de 4.ª generación o equivalente, 4 GB de RAM y
  disco HDD) y un criterio: una sesión de 30 min con pantalla + micrófono sin huecos ni tramos cortados, y la
  web usable. Si D11 la habilita, con la cámara a resolución reducida. Además, en Linux las variantes "Suavizado"/"MCI"
  del video de iPhone (interpolación con ffmpeg, muy costosa) se generan **solo a pedido**, o se apagan si la PC
  tiene menos de N núcleos.
- b) Sin criterio de rendimiento: "anda si anda".
- c) Exigir una PC moderna (8 GB, SSD) y declararlo en los requisitos.
- **Recomendada: a).** La grabación de pantalla de Android no recodifica (scrcpy entrega H.264 del celular), así que
  lo pesado es la cámara y el post-proceso de iPhone. Con un piso medible se puede decir que sí o que no a una PC
  concreta. **Preguntas:** ¿tenés a mano una PC vieja con Linux para usar como referencia? Si no, ¿qué modelo
  representa a las PCs de los peritos?

**D13. Verificación sin dispositivos y prueba con hardware.**
- a) **CI en dos niveles,** como Windows. (1) Un job en `ubuntu-24.04` arma el AppImage, lo extrae, arranca el agente
  y comprueba `/health` (herramientas desde `tools/`, pymobiledevice3 10.7.4, estado de la configuración inicial), corre
  `--autoprueba-ios` y una grabación lavfi cortada con SIGTERM; además abre la UI con `xvfb` y verifica que no se cae.
  (2) Arranque del agente dentro de contenedores `ubuntu:22.04` y `debian:12` (glibc mínima) y una corrida de la
  configuración inicial en modo "simulación" (lista lo que haría sin instalar). Después, una **prueba con hardware
  real** con un checklist (Android + iPhone) en al menos Ubuntu 24.04 y una PC vieja de la matriz.
- b) Solo el job de Ubuntu.
- c) Solo la prueba manual.
- **Recomendada: a).** Los contenedores son baratos y detectan justo el riesgo de "PC vieja con distro vieja"
  (glibc, bibliotecas faltantes). USB, udev y polkit solo se pueden probar en hardware. **Preguntas:** ¿quién hace la
  prueba con hardware y con qué PC Linux? ¿Vale una VM (UTM/VirtualBox con el USB del iPhone pasado a la VM) para la
  primera vuelta? ¿Sumamos a `/health` la distro y el estado de la configuración inicial (aditivo) para diagnosticar a
  distancia?

**D14. Carpetas de datos, configuración y evidencia en Linux.**
- a) **Siguiendo XDG:** datos del agente (evidencia en curso) en `~/.local/share/Tatana/data` (`$XDG_DATA_HOME`),
  config y `appsettings.Local.json` en `~/.config/Tatana/`, logs en `~/.local/state/Tatana/` o dentro de config, y el
  ZIP de evidencia en `~/Factum/Evidencia` (como hoy, L10).
- b) Dejarlo como queda hoy por defecto: `~/.config/Tatana/agent-data`.
- **Recomendada: a).** Es la equivalencia directa de `%LOCALAPPDATA%\Tatana\data` en Windows. `~/.config` es para
  configuración: herramientas de backup o limpieza la tratan distinto, y meter ahí GB de evidencia confunde. Como
  todavía no hay instalaciones en Linux, decidirlo ahora no exige migrar nada.

**D15. Arranque al iniciar sesión y bandeja del sistema.**
- a) **Autoarranque con `~/.config/autostart/tatana.desktop`** (el estándar de XDG; Electron no lo hace en Linux) con
  `--hidden`, apuntando a la ruta fija del AppImage (D8). Bandeja con AppIndicator donde exista (Ubuntu, Mint, KDE).
  Donde no haya bandeja (GNOME puro en Debian o Fedora), cerrar la ventana **la minimiza** en lugar de esconderla, y
  el acceso del menú de aplicaciones la vuelve a mostrar (instancia única).
- b) Sin autoarranque en Linux: el perito abre Tatana a mano cada vez.
- **Recomendada: a).** La web necesita a Tatana corriendo. Sin autoarranque, el error más común sería "Tatana no
  responde". Sin bandeja, esconder la ventana dejaría a Tatana inalcanzable, y el comportamiento de respaldo evita
  depender de una extensión de GNOME.

## Validación del usuario (2026-10-18)

El usuario validó todas las dudas con la opción recomendada (D1–D15 = a), con estas respuestas:
- **D1:** los peritos no usan una distro concreta. Se mantiene la matriz de a). Fedora queda como "best effort":
  se prueba en CI (contenedor) y con `dnf` en la configuración inicial, pero no hay prueba con hardware.
- **D11:** la #85 dejó la cámara apagada por defecto en Windows; Linux sigue el mismo criterio.
- **D12 / D13:** el usuario tiene una PC con Ubuntu para la prueba con hardware real (Android + iPhone). Esa PC es
  la referencia de la QA; si es vieja, sirve también como "PC de referencia mínima".
- **D13:** se suma a `/health` (aditivo) la distro y el estado de la configuración inicial.
- **D8:** chequeo cada 6 h e instalación al reiniciar Tatana, igual que en Windows.
- **D9:** sin portátil de respaldo aparte para Linux.
