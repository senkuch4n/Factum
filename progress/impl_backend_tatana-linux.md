# Implementación backend — tatana-linux (HU #91)

**Agente:** implementer-backend · **Rama:** `feat/tatana-linux` · **Fecha:** 2026-10-18
**SDD:** `Refactorizaciones/tatana-linux.md` (checklist §14.1, B1–B29; DU1 = a, DU2 = a).
**Estado:** done (sin commit ni push, como pidió el orquestador).

No se tocó `client/` ni `agent-ui/` (los lee el frontend), ni el Project, ni `progress/sesiones/`. No hubo acceso a
MongoDB ni a `Storage:DataDirectory` (esta HU no los usa). `preparar-pc.sh` nunca corrió en modo real en la Mac: solo
`--simular` con raíces falsas, y `--ejecutar` dentro de contenedores Docker descartables (`--rm`). No quedaron procesos
vivos: todo lo que arrancó Tatana/ffmpeg/scrcpy corrió dentro de contenedores efímeros.

---

## 1. Archivos tocados

### Agente Tatana (`server/src/Factum.Agent`) — B1–B13

| Archivo | Qué |
|---|---|
| `Common/HostOs.cs` (nuevo) | B1: `enum HostOs { Windows, MacOS, Linux }`, `HostOsInfo.Current`, `HostOsInfo.JsonName` (`windows`/`macos`/`linux`) |
| `Common/UnixSignals.cs` (nuevo) | B5: `TrySend(pid, signal)` con `kill(2)` por `DllImport("libc")` (`SetLastError`), fallback al ejecutable `kill` ante `DllNotFoundException`/`EntryPointNotFoundException`; `SIGINT=2`, `SIGTERM=15`, `SIGKILL=9` |
| `Common/ProcessStop.cs` | B5: en Linux las señales van por `UnixSignals` (`UsesNativeSignals(HostOs)`, `SignalNumber`); macOS sigue con `kill`, Windows con Ctrl+C |
| `Common/ToolResolver.cs` | B6: `ToolSpec.UnixPortablePath` (init) y `AgentTools.Python { UnixPortablePath = "bin/python3" }`; el paso portátil la usa fuera de Windows, el PATH sigue con `UnixName` |
| `Services/Ios/IosErrors.cs` | B2: `MessageFor/Create/FromHelper/RecordingEmptyFrom` con `HostOs` en vez de `bool isWindows`; textos de Linux de §5.4 (exactos); Windows y macOS idénticos; `LowerFirst` con "Linux" |
| `Services/Ios/IosHelper.cs`, `Services/Ios/DvtRecorder.cs`, `Services/Ios/IosSelfTest.cs` | B2: adaptados a `HostOs` (IosSelfTest imprime "Linux"/"macOS"/"Windows") |
| `Services/IosService.cs` | B2/B5/B9: `HostOs _os` en los errores; `ClassifyAfcError(stderr, HostOs)`; `GracefulStopAsync` con `UnixSignals` solo en Linux; `AirplayAvailable`/`AirplayUnavailableReason` por D-T25 (Linux: uxplay + `-mp4` + avahi; motivos `uxplay_without_mp4`, `avahi_not_running`); `-p` solo en Linux (al final de los argumentos, en los 3 lanzamientos de uxplay); texto de Linux "No se pudo iniciar el receptor AirPlay (uxplay)…" |
| `Services/AdbService.cs` | B3: `AdbMissingMessage(HostOs)` (texto de Linux de §5.4) en los dos lugares (L170 y L917 de antes) |
| `Services/Ios/AppleServiceProbe.cs` | B4: ctor interno con `HostOs` y `usbmuxdInstalled` inyectable; Linux = socket **o** usbmuxd instalado (`/usr/sbin/usbmuxd`, `/usr/bin/usbmuxd`, `usbmuxd.service` en `/usr/lib/systemd/system` o `/lib/systemd/system`); macOS sin cambios (solo socket) |
| `Services/MicCapture.cs` | B7: Linux `-f pulse -i <Agent:MicDevice o "default">`; si ffmpeg muere enseguida con `Unknown input format`, reintenta una vez con `-f alsa -i default`. Puros: `LinuxInputArgs`, `IsUnknownInputFormat`, `BuildArgs` |
| `Services/Session/CameraArgs.cs` | B8: `CameraArgsInput.LinuxAlsaAudio` (init); v4l2 con audio → `MicCapture.LinuxInputArgs(mic, alsa)` (`-f pulse -i <mic ?? default>`, `audioMap = "1:a"` igual) |
| `Services/Session/CameraTrack.cs` | B8: en Linux pasa `Agent:MicDevice` como `mic`; `ShouldRetryWithAlsa` (puro) relanza una vez con ALSA sin gastar el intento de modo |
| `Services/Session/CameraDevices.cs` | B8: `ListLinux(devDir, sysDir)` y `CaptureNodes` (puros respecto de las raíces): solo nodos con `index` = 0 o sin `index` |
| `Services/Linux/UxplayProbe.cs` (nuevo) | B9: `UxplayProbe` (una corrida en segundo plano por ruta + mtime, timeout 3 s, caché; espera ≤ 3,5 s solo la primera vez), `UxplayInfo`, `Parse` (puro); `AirplayRules.LinuxReason` y `ExtraArgs` |
| `Services/Linux/OsRelease.cs` (nuevo) | B10: parser puro de `/etc/os-release` (fallback `/usr/lib/os-release`, comillas, escapes, `ID` en minúsculas, `linux` si falta) |
| `Services/Linux/LinuxHostProbe.cs` (nuevo) | B10: `LinuxHostStatus` / `LinuxCheck` (§10.A), `LinuxHostEnvironment` inyectable (raíz falsa, env, glibc, access, uxplay), los 7 chequeos de §5.2 en orden, `setup_state`, caché 5 s, `VersionAtLeast`; `gnu_get_libc_version` y `access(R_OK\|W_OK)` por P/Invoke |
| `Services/Linux/LinuxSelfTest.cs` (nuevo) | B13: `--autoprueba-linux <carpeta>` (§5.7, los 6 pasos) |
| `Controllers/HealthController.cs` | B11: `os` y `linux` (null fuera de Linux → no viaja) |
| `Program.cs` | B10/B13: `LinuxHostProbe` en DI; flag `--autoprueba-linux` antes del host |
| `Models/AgentModels.cs` | B12: `VideoPostprocessOptions.MinProcessorCount` (int?), `EffectiveMinProcessorCount(HostOs)` (null → 4 en Linux, 0 en Windows/macOS), `Normalize` (negativo → null con warning) |
| `Services/VideoPostprocessService.cs` | B12: con menos núcleos que el mínimo efectivo, `Start` devuelve `[]` y lo loguea (`HasEnoughProcessors`, puro) |

### Tests (`server/tests/Factum.Agent.Tests`) — §5.8

`AppleServiceProbeTests` (Linux socket / instalado sin socket / nada / excepción; macOS solo socket), `IosErrorsTests`
(Windows/macOS iguales + textos Linux exactos + ningún texto Linux con `macOS`/`brew`/`Terminal`/`iTunes`/`Mac` +
`LowerFirst`), `IosHelperTests` (adaptado), `ToolResolverTests` (subruta portátil de Python en Unix, Windows igual),
`ProcessStopTests` (Linux usa UnixSignals, número de señal, SIGTERM directo), nuevos: `UnixSignalsTests` (SIGTERM a
`sleep` → 143; solo corre en Linux), `MicArgsTests` (+ `AdbMissingMessageTests`), `VideoPostprocessOptionsTests`,
`Linux/OsReleaseTests` (Ubuntu, Debian, Fedora, Mint, Arch, comillas, sin `VERSION_ID`), `Linux/LinuxHostProbeTests`
(cada fila de §5.2 con árbol falso, gestor, caché, contrato JSON, `os` y `linux` null fuera de Linux),
`Linux/CameraDevicesLinuxTests` (index 0 y 1 → una cámara), `Linux/UxplayProbeTests` (parseo, una corrida, regla y `-p`).

### Empaquetado — B15–B19

| Archivo | Qué |
|---|---|
| `packaging/linux/ios-linux/requirements.in` (nuevo) | `pymobiledevice3==10.7.4` + `av`, `lzfse`, `sslpsk-pmd3` (marcadores de Linux que pip no ve desde la Mac, igual que A3 de Windows) |
| `packaging/linux/ios-linux/generar-lock.py` (nuevo) | Copia adaptada: manylinux x86-64 / cp311, entorno de marcadores Linux 3.11.17, **versiones de `requirements-win.lock` como constraints**; `--check` verifica hashes, clausura y que ninguna versión difiera de Windows |
| `packaging/linux/ios-linux/requirements-linux.lock` (nuevo, generado) | 93 wheels con hash (mismas versiones que Windows en los paquetes comunes) |
| `packaging/linux/glibc-maximo.py` (nuevo) | Lector de `SHT_GNU_verneed` sin dependencias; `--max`, `--detalle`; exit 1 si se pasa |
| `packaging/linux/preparar-pc.sh` (nuevo) | §7 completo: `--simular` (`TATANA_PLAN`) / `--ejecutar --usuario` (`TATANA_PASO`, `TATANA_RESULTADO`), códigos 0/1/2/3/4/5, archivos exactos, apt/dnf, firewall ufw/firewalld, grupo video, AppArmor, `TATANA_RAIZ_PRUEBA` |
| `packaging/linux/tests/probar-preparar-pc.sh` (nuevo) | Ubuntu 22.04/24.04, Debian 12, Fedora, Mint 22, Arch y distro desconocida + códigos de argumentos/sin root + por stdin |
| `deploy/linux/armar-tatana-linux.sh` (nuevo) | §6.1: versiones y SHA-256 de §4, publish linux-x64, adb (con `lib64/`), scrcpy sin adb + avisos, PBS + lock + hexdump + avisos, ffmpeg + avisos, `+x`, tar, chequeo del tar (requeridos, sin adb de scrcpy, `+x`, METADATA 10.7.4, Mock, orígenes) y guarda de glibc ≤ 2.35 |

### CI / release / VPS — B20–B28

| Archivo | Qué |
|---|---|
| `ci/tatana-linux/prueba-humo.sh` (nuevo) | §9.1 (a)–(e) con `--distro`, `--gestor`, `--sin-ui`, `--no-sandbox` |
| `.github/workflows/tatana-linux.yml` (nuevo) | Jobs `armar`, `humo`, `distros` (ubuntu:22.04, debian:12 con UI; fedora:latest best effort) |
| `.github/workflows/tatana-release.yml` | `paths` nuevos; chequeos de `preparar` (AppImage x64, `artifactName`, `toolsets.appimage`); `payload` arma Linux + base 0.0.1 de Linux; job `appimage` (nuevo); `firmar` firma los dos manifiestos y SHA256SUMS de 7; job `prueba-linux` (nuevo); `publicar` con `needs: [preparar, prueba, prueba-linux]`, 8 archivos, chequeo externo de los dos manifiestos y del AppImage, GitHub Release con los archivos de Linux. **Se sacó el paso `node scripts/parchar-electron-builder.mjs`** del job `instalador` (aviso del orquestador: el frontend borró el script por DU1 = a; si no, la release de Windows fallaba) |
| `.github/workflows/verificar.yml` | `bash -n` + `probar-preparar-pc.sh` + `shellcheck` de los 4 scripts de Linux (si está en el runner) |
| `.github/workflows/desplegar-produccion.yml` | build-arg `NEXT_PUBLIC_TATANA_DOWNLOAD_URL_LINUX=${{ vars.FACTUM_TATANA_DESCARGA_URL_LINUX }}` |
| `ops/tatana/_lib.mjs` | `PRODUCTS`, `expectedFile`, `productArg`; `payloadError(p, product = 'tatana-windows')`; `verifyEnvelopeText(text, keys, product)` |
| `ops/tatana/armar-payload.mjs` | `--producto` (default Windows) y `--archivo` (`--exe` alias) |
| `ops/tatana/verificar-manifiesto.mjs` | `--producto` y `--archivo` (`--exe` alias) |
| `ops/tatana/firmar-manifiesto.mjs` | Valida y verifica con el producto del payload (Windows o Linux) |
| `ops/tatana/probar-firma.mjs` | Casos de Linux (armar/firmar/verificar), producto desconocido, AppImage con producto Windows, exe con producto Linux, cruzados en los dos sentidos, `file` con otro nombre, AppImage alterado |
| `ops/tatana/prueba-linux.sh` (nuevo) | §9.4, pasos 1–8 |
| `deploy/cloud/scripts/publicar-tatana-forzado.sh` | 8 archivos, valida `latest-linux.yml`, orden de publicación de §9.3, copias de descarga de Linux + `.sha256`, `index.html` (D-T27, sin JS), retención de 5 AppImage |
| `ops/tatana/probar-publicar-forzado.sh` | 8 archivos, falta un archivo de Linux → 4, solo los 5 de Windows → 4, hash/yml de Linux → 5, `index.html`, retención de AppImage |
| `deploy/cloud/Caddyfile` | `@manifiestos` con `latest-linux.yml` y `tatana-update-linux.json` |
| `deploy/cloud/scripts/desplegar.sh`, `deploy/cloud/factum.env.example` | `FACTUM_TATANA_DESCARGA_URL_LINUX` → `NEXT_PUBLIC_TATANA_DOWNLOAD_URL_LINUX` |
| `docs/despliegue-nube.md` | §10.2 (variable de Linux), tabla de §11, §19 (release única, paso manual del VPS, ensayo, verificar Linux), §19.8 (logs de Linux) y **§20 nueva "Tatana en Linux"** (requisitos, descarga, ejecutable en GNOME/KDE/Cinnamon, instalar, preparar, distro sin soporte, problemas, qué deja y cómo sacarlo, carpetas, prueba con artifacts) |

---

## 2. Verificación (§15.1)

```
$ dotnet build server/src/Factum.Agent/Factum.Agent.csproj
    0 Warning(s)
    0 Error(s)
Build succeeded.

$ dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj          # macOS (arm64)
Passed!  - Failed: 0, Passed: 979, Skipped: 0, Total: 979

$ (mismo test en Linux: contenedor mcr.microsoft.com/dotnet/sdk:10.0, arm64, con ffmpeg)
Passed!  - Failed: 0, Passed: 977, Skipped: 2, Total: 979
  (los 2 saltados son DvtRecorderTests que piden un Python con pymobiledevice3; en Linux corren los de UnixSignals/ProcessStop nativos)

$ python3 -m py_compile server/src/Factum.Agent/Services/Ios/ios_helper.py      → OK
$ python3 -I packaging/linux/ios-linux/generar-lock.py --check
OK  packaging/linux/ios-linux/requirements-linux.lock: 93 wheels con hash verificado, pymobiledevice3==10.7.4, clausura de Linux cubierta, mismas versiones que Windows
$ python3 -I packaging/portable/ios-win/generar-lock.py --check
OK  packaging/portable/ios-win/requirements-win.lock: 97 wheels con hash verificado, pymobiledevice3==10.7.4, clausura de Windows cubierta

$ bash -n packaging/linux/preparar-pc.sh && bash packaging/linux/tests/probar-preparar-pc.sh
OK  bash -n preparar-pc.sh / Ubuntu 22.04 / Ubuntu 24.04 / Debian 12 / Fedora / Linux Mint 22 / Arch (sin soporte) /
    Distro desconocida / sin argumentos → 2 / argumento desconocido → 2 / --ejecutar sin --usuario → 2 /
    --ejecutar con raíz de prueba → 2 / --ejecutar sin root → 5 / bash -s -- --simular por stdin
RESULTADO: OK      (también en ubuntu:24.04 con bash 5)

$ docker run … ubuntu:24.04 bash ops/tatana/probar-publicar-forzado.sh          (necesita GNU tar/find -printf)
… 53 casos ok … Todas las pruebas pasaron

$ node ops/tatana/probar-firma.mjs
… 30 casos ok (incluidos los 11 de Linux y cruzados) … Todas las pruebas pasaron
```

`dotnet build server/src/Factum.Backend/Factum.Backend.csproj`: OK, 0 errores; los 4 warnings son NU1902/NU1903 de
paquetes del backend (SharpCompress, Snappier), preexistentes y ajenos a esta HU.

Lint: `actionlint` (Docker `rhysd/actionlint`) sobre los 4 workflows tocados → sin hallazgos. `shellcheck -S warning`
(Docker `koalaman/shellcheck:stable`) sobre `preparar-pc.sh`, `armar-tatana-linux.sh`, `prueba-humo.sh`,
`prueba-linux.sh`, `probar-preparar-pc.sh`, `probar-publicar-forzado.sh` y `publicar-tatana-forzado.sh` → sin
hallazgos.

### Armado real del payload (B17)

`bash deploy/linux/armar-tatana-linux.sh --version 0.0.0-ci --src <árbol limpio> --salida <scratch> [--origenes https://factum.ejemplo.com]`
corrido en la Mac (dos veces, con y sin `--origenes`): las 4 descargas pasan el SHA-256 de §4, el chequeo del tar
pasa y la guarda dice:

```
glibc máxima: 2.35 (…/tools/scrcpy/scrcpy; 193 archivos ELF con versiones de glibc)
OK  contenido del tar verificado
OK  …/Tatana-Agent-v0.0.0-ci-linux-x64.tar.gz (241M)
```

(coincide con E3/E5). En la Mac el paso de `ffmpeg -devices` es solo un aviso (no es Linux x86-64); lo cubre la
autoprueba en los contenedores (abajo) y el CI.

### El tar en `ubuntu:22.04` y `debian:12` (linux/amd64, Docker)

Instalando solo `ca-certificates libicu libssl3 libudev1 procps curl` (lo de E7), extraído y corrido:

- `--autoprueba-linux`: **RESULTADO: OK** en las dos (adb/scrcpy/ffmpeg/python portátiles; ffmpeg con `pulse` y
  `v4l2`; lavfi 5 s cortado con SIGTERM limpio, MKV de 5,5 s; `kill(2)` → 143; estado de la PC; glibc 2.35 / 2.36).
- `--autoprueba-ios`: **RESULTADO: OK** en las dos (pymobiledevice3 10.7.4, imports, grabación DVT sintética, MP4
  legible, captura sin iPhone → `ios_apple_service_missing`).
- Agente suelto + `/health`: `"os":"linux"`, `linux` con los 7 chequeos, `distro_id` `ubuntu`/`debian`,
  `glibc_version`, `package_manager: apt`, `setup_state: incomplete`, herramientas `source: portable`,
  `pymobiledevice3_version: 10.7.4`, `ios.apple_service`, `airplay_unavailable_reason: uxplay_not_found`. SIGTERM → sale
  con 0 en < 1 s.

### `preparar-pc.sh --ejecutar` en contenedores descartables (como root, nunca en la Mac)

- `ubuntu:24.04` (amd64): instala `usbmuxd` y `avahi-daemon`, escribe la regla udev exacta, agrega al usuario a
  `video` (el contenedor no trae `70-uaccess.rules`) → `parcial` (exit 1) porque el contenedor no tiene `udevadm` ni
  `systemctl` (detalle: "Falta el comando udevadm en esta PC."). Segunda corrida: mismo resultado, `camara_microfono`
  `omitido` (idempotente). `PKEXEC_UID` distinto → 2; usuario inexistente → 2.
- `ubuntu:24.04 --network none` → `sin_internet`, exit 4.
- `--simular` real en `ubuntu:24.04` (uxplay candidato `1.68.2-1build2` → `instalar: false`, confirma DU2) y en
  `fedora:latest` (Fedora 44, dnf5: uxplay `1.73.7` → `instalar: true`).

### AppImage armado de punta a punta (copia en el scratchpad, sin tocar `agent-ui/`)

Para probar los scripts de CI con un AppImage real, se copió `agent-ui/` (lo que había del frontend en ese momento) y
`packaging/linux/` al scratchpad, se puso el payload de arriba en `resources/agent` y se armó en `node:22-bookworm`
linux/amd64: `npm ci && npx electron-vite build && npx electron-builder --linux AppImage --x64 --publish never` →
`dist/Tatana-1.0.0-x86_64.AppImage` (310 MB) + `latest-linux.yml`, con el runtime estático
(`appimage-tools-runtime-20251108`, toolset 1.0.3, E8) y blockmap embebido. **No se escribió nada en `agent-ui/`.**

`ci/tatana-linux/prueba-humo.sh` contra ese AppImage, en contenedores linux/amd64 con las mismas bibliotecas que
instala el job `distros` y `APPIMAGE_EXTRACT_AND_RUN=1`:

| Imagen | (a) | (b) | (c) | (d) | (e) UI con Xvfb, `--no-sandbox` | Resultado |
|---|---|---|---|---|---|---|
| `ubuntu:24.04` | OK | OK | OK (`distro_id` ubuntu, glibc 2.39) | OK (apt) | OK: viva a los 20 s, `/health` del agente lanzado por Electron, `tatana.json`, `--quit` sin procesos ni puerto | **OK** |
| `ubuntu:22.04` | OK | OK | OK (glibc 2.35) | OK | OK (corrida previa; la única falla era del detector de procesos, ya corregido) | OK |
| `debian:12` | OK | OK | OK (glibc 2.36) | OK | ídem | OK |
| `fedora:latest` (44) | OK | OK | OK (glibc 2.43) | OK (dnf) | — (`--sin-ui`, como en el workflow) | **OK** |

Nota de emulación (no aplica a una PC real): Docker en la Mac corre amd64 con Rosetta/binfmt, que no reconoce un ELF
con la marca `AI\x02` del AppImage en `e_ident[8..10]` ("Exec format error"). Solo para esta prueba local, el arnés
borró esa marca en una copia del AppImage; en un kernel x86-64 (el runner de GitHub o la PC del perito) no hace
falta. Lo que se encontró y se corrigió en `prueba-humo.sh`/`prueba-linux.sh` con esta corrida: el detector de
procesos de Tatana mira `/proc/<pid>/exe` **o** `argv[0]` (bajo emulación `exe` apunta al emulador), y el paso (c) ya
no da "OK" de SIGTERM si el agente nunca arrancó.

electron-builder avisa que falta `desktopName` en `package.json` (WM_CLASS de la ventana para asociarla al `.desktop`):
es del lado del frontend (`StartupWMClass` de §8.4, que la SDD deja para confirmar con `xprop` en la QA).

---

## 3. Lo que no se pudo verificar y por qué

- **Workflows en GitHub Actions** (`tatana-linux.yml` y `tatana-release.yml` en ensayo): necesitan el PR (no hay commit
  ni push en este paso). Quedan verificados con `actionlint` y con los scripts que llaman corridos localmente. En
  particular falta ver en CI real: FUSE/Xvfb en `ubuntu-24.04`, el job `appimage` con electron-builder 26 y
  `prueba-linux.sh` completo (instalar/actualizar/relanzar la app en Xvfb). Hay que mirar esos runs en el PR.
- **`ops/tatana/prueba-linux.sh`** no se corrió (necesita base 0.0.1 + canal firmados de un mismo run y la UI de
  Electron de esta rama): solo `bash -n` y `shellcheck`.
- **`prueba-humo.sh` con FUSE de verdad** (job `humo` en `ubuntu-24.04`): localmente solo se probó el modo
  extract-and-run (los contenedores no tienen `/dev/fuse`).
- **Hardware** (§11): Android/iPhone reales, micrófono PulseAudio, cámara V4L2, AppArmor en Ubuntu 24.04 (E9),
  `udevadm`/`systemctl` reales de "Preparar esta PC": quedan para la QA del usuario.

## 4. Decisiones no obvias / desvíos menores

- **Xvfb propio en vez de `xvfb-run -a`** en `prueba-humo.sh` (e) y `prueba-linux.sh`: la app se relanza a sí misma
  (instalar, actualizar) y `xvfb-run` cierra el display cuando termina el primer proceso.
- **`canal-linux` (ensayo) trae además `tatana-update.json`**: un manifiesto con producto `tatana-windows` firmado con
  la misma clave efímera, para el paso 4 de `prueba-linux.sh` (Linux tiene que rechazarlo con `manifiesto_invalido`
  aunque la firma sea válida). En release ese archivo es el manifiesto real de Windows del canal. `SHA256SUMS` de
  `canal-linux` cubre solo los 3 archivos de Linux, como dice §9.2.
- **El job `appimage` usa su propia clave efímera** (no la del job `instalador`): las dos claves nunca salen de su job.
- **`publicar-tatana-forzado.sh`: tope del tar 1 GiB → 2 GiB** (instalador + AppImage); el código de salida sigue
  siendo 3.
- **`requirements.in` de Linux suma `av`, `lzfse`, `sslpsk-pmd3`**: pip `--platform` evalúa los marcadores con los de
  la máquina que arma (Mac, Python 3.14) y no los ve; `generar-lock.py` lo detecta (mismo caso A3 que Windows).
- **`VideoPostprocessService`**: el ctor interno de tests no aplica el mínimo de núcleos salvo que se pase
  `processorCount` (los runners privados de GitHub tienen 2 núcleos y los tests existentes dejarían de generar
  variantes en Linux). El ctor de producción pasa `Environment.ProcessorCount` y `HostOsInfo.Current`.
- **`UnixSignals` con `DllImport`** (no `LibraryImport`), para no habilitar `AllowUnsafeBlocks` en el `.csproj`.
- **`preparar-pc.sh`**: todo el cuerpo en `main()` llamado en la última línea (bash lee el script completo antes de
  ejecutar, porque viene por stdin) y todos los comandos con `</dev/null`; `TATANA_RAIZ_PRUEBA` se rechaza con
  `--ejecutar` (exit 2); sin root, `ufw` se detecta por `/etc/ufw/ufw.conf` (`ufw status` exige root); dnf5 no agrega
  salto de línea al `--qf` (se pide `%{version}\n`); "sin internet" también cuando `apt-get update` falló por la red
  y el install termina en "Unable to locate package"; un comando que falta da el detalle "Falta el comando X en esta
  PC.". La simulación de una distro sin soporte lista los dos archivos (también el de AppArmor) para soporte técnico.
- **`/health` y uxplay**: en Linux, la primera vez que se consulta `AirplayAvailable` con uxplay presente puede
  esperar hasta ~3,5 s (sondeo de `uxplay -h`); después queda en caché por ruta + fecha del archivo.
- **`--autoprueba-linux`** imprime el JSON del estado con escapes relajados (acentos legibles); `/health` no cambia.
- **`armar-tatana-linux.sh`**: `tar --no-xattrs` (si se arma en la Mac, bsdtar agregaba headers
  `LIBARCHIVE.xattr.com.apple.provenance` que GNU tar avisa al extraer) y se borran los `.pdb` del publish.

## 5. Contrato compartido (§10) — confirmado

- **§10.A `/health`**: `os` (`windows`|`macos`|`linux`) y `linux` con `distro_id`, `distro_version`, `distro_name`,
  `distro_like`, `glibc_version` (con `[JsonPropertyName]`), `package_manager`, `setup_supported`, `setup_state`,
  `checks[]` (`id`, `status`, `required`, `detail`), siempre los 7 en el orden `usbmuxd`, `android_usb`, `avahi`,
  `uxplay`, `camera`, `microphone`, `sandbox`; `required` solo en los dos primeros. Comprobado en el JSON real de
  `/health` en ubuntu:22.04 y debian:12 y en `LinuxHostProbeTests.Contrato_JSON_de_linux`. `linux` no viaja fuera de
  Linux. `ios.airplay_unavailable_reason` suma `uxplay_without_mp4` y `avahi_not_running`.
- **§10.B `preparar-pc.sh`**: `--simular` / `--ejecutar --usuario --version`, líneas `TATANA_PLAN`/`TATANA_PASO`/
  `TATANA_RESULTADO` (prefijo + espacio + JSON de una línea), ids `paquetes`, `reglas_usb`, `airplay`,
  `camara_microfono`, `sandbox`, estados `en_curso`/`ok`/`error`/`omitido`, resultados `ok`/`parcial`/`sin_soporte`/
  `sin_internet`, códigos 0/1/2/3/4/5, archivos exactos. El frontend lo importa desde
  `agent-ui/src/main/linux/prepare-pc.ts` (`../../../../packaging/linux/preparar-pc.sh?raw`), que existe.
- **§10.C**: `tatana-update-linux.json`, `product: "tatana-linux"`, `file: Tatana-${version}-x86_64.AppImage`,
  `latest-linux.yml`; `payloadError(p, product)` igual que `validatePayload` del frontend.
- **§10.D**: 8 archivos, `SHA256SUMS` de 7, salidas `Tatana-Setup-Windows.exe(.sha256)`,
  `Tatana-Linux-x86_64.AppImage(.sha256)` e `index.html`.
- **§10.E**: `FACTUM_TATANA_DESCARGA_URL_LINUX` → `NEXT_PUBLIC_TATANA_DOWNLOAD_URL_LINUX` en `desplegar-produccion.yml`,
  `desplegar.sh` y `factum.env.example` (el `Dockerfile` y `next.config.ts` los hizo el frontend).

## 6. Pasos manuales que quedan para el usuario (§13)

1. GitHub: crear la variable `FACTUM_TATANA_DESCARGA_URL_LINUX` (recomendado `https://<dominio>/descargas/tatana/`).
2. VPS, antes de la primera release con Linux: copiar el `publicar-tatana-forzado.sh` nuevo a `/srv/factum/bin/`
   (root:root 755; comando en `docs/despliegue-nube.md` §19.2) y desplegar el `Caddyfile` nuevo.
3. QA con hardware de §11.

## 7. Bloqueos

Ninguno.
