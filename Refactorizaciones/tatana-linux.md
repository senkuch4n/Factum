# SDD — Tatana para Linux (AppImage, Android + iPhone)

**Slug:** `tatana-linux` · **Issue:** #91 · **HU:** `docs/hu-tatana-linux.md` (validada el 2026-10-18: D1–D15 = a,
con Fedora "best effort", cámara apagada por defecto como en Windows, chequeo cada 6 h e instalación al reiniciar,
`/health` con distro y estado de la configuración inicial y sin portátil de respaldo para Linux).
**Base del análisis:** `feat/tatana-linux` = `develop` `984d85e` (ya incluye la #85, fase 2 de la sesión completa).
**Evidencia:** todo lo que dice "medido" se comprobó el 2026-10-18 con los binarios reales (ver §4, tabla E).

---

## 1. Resumen funcional

Tatana llega a Linux como **un único AppImage x86-64** (`Tatana-X.Y.Z-x86_64.AppImage`). Trae la UI de Electron
(`agent-ui`), el agente `Factum.Agent` (`linux-x64`, self-contained) y sus herramientas con versión fija y SHA-256:
adb (platform-tools r37.0.1), scrcpy 4.1, ffmpeg 8.1 (con PulseAudio y v4l2) y Python 3.11 portable con
pymobiledevice3 10.7.4 desde un lock de Linux con hashes. Arranca con doble clic sin `libfuse2`, porque usa el runtime
estático de AppImage. En Ubuntu 24.04 la ventana abre aunque AppArmor restrinja los user namespaces. La primera vez,
Tatana se ofrece a **instalarse en `~/Applications/Tatana.AppImage`**, con acceso en el menú y arranque al iniciar
sesión. Después propone **"Preparar esta PC"**: pide la contraseña de administrador **una sola vez** (polkit /
`pkexec`) y corre un script versionado que instala `usbmuxd` y `avahi`, deja una regla `udev` con `uaccess` para
Android, abre los puertos de AirPlay si hay firewall activo y, en Ubuntu 24.04, instala un perfil de AppArmor para la
ventana. Funciona con `apt` y `dnf`. Con otro gestor de paquetes no ejecuta nada y muestra la lista para soporte. El
agente expone en `/health` el SO, la distro y una **lista de chequeos** que Tatana muestra sin pedir contraseña.
Además corrige lo que en Linux hoy está mal: textos de macOS/brew, la sonda de usbmuxd, el micrófono (`pulse` en vez
de `alsa`), las cámaras duplicadas y el corte de procesos con `kill(2)` directo. La actualización usa el mismo canal
firmado con Ed25519 y la misma versión que Windows, con un manifiesto propio (`tatana-update-linux.json`,
`product: "tatana-linux"`). Se instala al reiniciar Tatana, nunca con una captura en curso. La evidencia vive en
`~/.local/share/Tatana/data` y el ZIP en `~/Factum/Evidencia`, así que borrar el AppImage no la toca. La web elige el
enlace de descarga según el SO del perito. CI arma, prueba (Ubuntu 24.04 más contenedores `ubuntu:22.04`,
`debian:12` y Fedora como best effort), firma y publica Windows y Linux en la misma release.

## 2. Toca

| Lado | ¿Toca? | Qué |
|---|---|---|
| backend (API) `server/src/Factum.Backend` | **no** | — |
| backend (Tatana) `server/src/Factum.Agent` | **sí** | `/health` (`os`, `linux`), sonda de usbmuxd, textos por SO, micrófono y cámara en Linux, señales, uxplay, `--autoprueba-linux`, tests |
| Empaquetado / CI / VPS (lo hace `implementer-backend`) | **sí** | `deploy/linux/`, `packaging/linux/`, `ci/tatana-linux/`, `ops/tatana/*`, `.github/workflows/*`, `deploy/cloud/*` |
| agent-ui `agent-ui/` | **sí** | electron-builder (AppImage), carpetas XDG, instalación en `~/Applications`, autoarranque, bandeja, "Preparar esta PC", chequeos, actualizador Linux, cierre del árbol de procesos |
| client `client/` | **sí (chico)** | enlace de descarga por SO, texto de "falta el servicio de Apple" y motivos de AirPlay en Linux |

**Mongo:** sin cambios. **Storage del backend:** sin cambios.

---

## 3. Vista general

```
                    GitHub Actions (tatana-release.yml)
 preparar ─► payload (ubuntu) ─┬─► instalador (windows, NSIS) ─┐
                               └─► appimage (ubuntu, AppImage) ┴─► firmar (release) ─┬─► prueba (windows)
                                                                                     └─► prueba-linux (ubuntu)
                                                                  ambas OK ─► publicar (VPS + GitHub Release)

 VPS  /tatana/updates/   tatana-update.json (windows) · latest.yml · Tatana-Setup-X.Y.Z.exe(.blockmap)
                         tatana-update-linux.json · latest-linux.yml · Tatana-X.Y.Z-x86_64.AppImage
      /descargas/tatana/ index.html (requisitos + los dos enlaces) · Tatana-Setup-Windows.exe(.sha256)
                         Tatana-Linux-x86_64.AppImage(.sha256)

 PC Linux
   ~/Applications/Tatana.AppImage ──(FUSE)──► /tmp/.mount_TatanXXXX/
        AppRun ─► tatana (Electron) ──spawn (grupo propio)──► resources/agent/Factum.Agent --mode installed
                         │                                         └─ tools/{platform-tools,scrcpy,ffmpeg,python-embed}
                         ├─ pkexec /bin/bash -s  ◄── stdin: preparar-pc.sh (embebido en el bundle de main)
                         └─ GET /health ─► linux.checks (sin contraseña)
   ~/.config/Tatana/            tatana.json · appsettings.Local.json · update-state.json · preparacion-linux.json
                                instalacion-linux.json · logs/{tatana-updater,preparacion-linux}.log
   ~/.local/share/Tatana/data/  carpeta de trabajo del agente (cases/, sesiones, .postprocess/)
   ~/Factum/Evidencia/          ZIP de cada caso (como hoy, L10)
   ~/.local/share/applications/factum-tatana.desktop · ~/.config/autostart/factum-tatana.desktop
   /etc/udev/rules.d/70-factum-tatana.rules · /etc/apparmor.d/factum-tatana (solo si AppArmor restringe userns)
```

---

## 4. Decisiones técnicas

### E. Evidencia medida (los puntos que la HU dejó para la SDD)

| # | Pregunta | Resultado | Cómo se midió |
|---|---|---|---|
| E1 | glibc mínima de .NET 10 self-contained `linux-x64` | **2.27** (`libcoreclr.so`, `libclrjit.so`, `libclrgcexp.so`; el resto ≤ 2.17) | `dotnet publish -r linux-x64 --self-contained` (runtime 10.0.9) + lectura de `SHT_GNU_verneed` de cada ELF |
| E2 | glibc mínima de Electron 33 (`agent-ui` usa 33.4.11) | **2.25** (`electron`, `chrome_crashpad_handler`) | ídem sobre `electron-v33.4.11-linux-x64.zip` |
| E3 | glibc de scrcpy 4.1 Linux | **2.35** (`scrcpy-linux-x86_64-v4.1.tar.gz`; su `adb` pide 2.16) | ídem. **Es la más alta de todo el paquete** |
| E4 | glibc de adb r37.0.1 / ffmpeg 8.1 BtbN / python-build-standalone | 2.16 / 2.28 / ≤ 2.17 (PBS apunta a manylinux2014); el wheel más exigente del lock es `pillow` (`manylinux_2_28`) | ídem + tags de los wheels |
| E5 | **glibc mínima publicada** | **2.35** → Ubuntu 22.04+, Mint 21+, Pop!_OS/Zorin 22.04+, Debian 12+, Fedora 36+. Queda afuera Ubuntu 20.04 / Debian 11, como preveía D1 | máx(E1…E4) |
| E6 | ¿.NET 10 exige x86-64-v2? | **No.** El JIT de 10.0.9 expone el knob `EnableSSE42` (el grupo v2 es opcional) y no tiene chequeo de "baseline". El piso real lo pone Chromium: **x86-64 con SSE3** (cualquier CPU de 64 bits posterior a ~2005). Si una versión futura de .NET sube la base a v2, se revisa | `strings libclrjit.so` (knobs `Enable*`) |
| E7 | Dependencias dinámicas del sistema | .NET: `libstdc++`, `libgcc_s`, **libicu** y **libssl** (OpenSSL 1.1 o 3, por SHA-256/AES). Electron: GTK3, NSS, `libgbm`, `libasound2`, `libudev`, X11/xcb. scrcpy: `libudev`. Vienen en cualquier escritorio de la matriz; un contenedor mínimo no los trae (CI los instala, §10.3) | `DT_NEEDED` de cada ELF; corrida en `ubuntu:22.04` (sin libicu, .NET aborta salvo modo invariante) |
| E8 | ¿electron-builder 24.13.3 permite el runtime estático (sin libfuse2)? | **No.** `app-builder-bin` 4.0.0 baja el toolset `appimage-12.0.1` (runtime de AppImageKit, depende de libfuse2). **electron-builder 26.x sí:** `toolsets.appimage: "1.0.3"` usa el *static runtime* 20251108 (sin libfuse2) y `mksquashfs`, y sigue agregando el blockmap embebido (actualización diferencial). El schema lo marca "beta" | `node_modules/app-builder-bin` (strings) y `app-builder-lib@26.15.3` (`scheme.json`, `out/toolsets/linux.js`, `out/targets/appimage/appImageUtil.js`) |
| E9 | Sandbox de Chromium en Ubuntu 24.04 en el primer arranque | Con el toolset nuevo, el `AppRun` que genera electron-builder 26 prueba `unshare -Ur true` y, **si los user namespaces no están disponibles, agrega `--no-sandbox` solo**. Con el toolset viejo, el `.desktop` interno lleva `--no-sandbox` fijo. Además, `agent-ui` ya crea la ventana con `webPreferences.sandbox: false` y solo carga `out/renderer/index.html` local (`loadFile`): el `--no-sandbox` del primer arranque cuesta poco. Para recuperar el sandbox, "Preparar esta PC" instala un perfil de AppArmor `flags=(unconfined)` con `userns,` **asociado a la ruta del AppImage** (`@{HOME}/Applications/Tatana.AppImage`). Un perfil *unconfined* lo heredan los hijos que no tienen perfil propio (AppRun → `unshare` → Electron), así que el chequeo de `unshare` pasa y Chromium arranca con sandbox. **A confirmar en la QA con hardware (§11, paso 3.4).** Si no pasa, el resultado igual es una ventana que abre | lectura de `generateAppRunScript()` y de `templates/linux/apparmor-profile.tpl` de app-builder-lib 26 |
| E10 | ¿Desde qué versión uxplay soporta `-mp4`? | **UxPlay 1.73 (2026-01-25)** ("New option -mp4 for recording to a mp4 file"). Ubuntu 22.04/24.04, Debian 12 y Mint traen versiones anteriores (1.4x–1.68): **en casi toda la matriz AirPlay va a salir deshabilitado con su motivo** (ver DU2). Puertos fijos con `-p`: TCP 7100, 7000, 7001 y UDP 6000, 6001, 7011 | `README.txt` de FDH2/UxPlay (changelog) |
| E11 | ¿El ffmpeg elegido trae `libpulse` y `v4l2`? | BtbN `linux64-gpl` rama 8.1: **sí** (`--enable-libpulse`; dispositivos `pulse` D/E y `video4linux2,v4l2`, más `libx264`). **No trae `alsa`**, así que el `-f alsa -i default` de hoy fallaría con este ffmpeg: Linux pasa a `pulse` (D10) | corrida de `ffmpeg -devices` y `-buildconf` en `ubuntu:22.04` (amd64) |
| E12 | pymobiledevice3 10.7.4 en Linux | Resuelve **solo con wheels** para `cp311` / `manylinux_2_17…2_28_x86_64`, salvo `hexdump` (sdist, como en Windows). Con las **mismas versiones que `requirements-win.lock`** como constraints, también resuelve (91 archivos) | `pip download --only-binary=:all: --platform manylinux… --python-version 311 -c <versiones del lock de Windows>` |
| E13 | `kill(2)` por P/Invoke desde .NET 10 | `[DllImport("libc", EntryPoint = "kill", SetLastError = true)]` funciona en `ubuntu:22.04` y `debian:12` **sin** `libc.so` de desarrollo (SIGTERM a un hijo → sale con 143; `kill(pid, 0)` a un pid inexistente → -1 / errno 3) | app de prueba `linux-x64` en contenedores amd64 |

### Versiones fijas (§6.1 las vuelca al script de armado)

| Componente | Versión | Archivo | SHA-256 |
|---|---|---|---|
| adb (Android platform-tools) | r37.0.1 | `https://dl.google.com/android/repository/platform-tools_r37.0.1-linux.zip` | `d230f13842f60f782a8645f9c813f8f845bf36089ea7289f28c48f17979313f1` |
| scrcpy | 4.1 | `https://github.com/Genymobile/scrcpy/releases/download/v4.1/scrcpy-linux-x86_64-v4.1.tar.gz` | `ad56ae8bfeedf41e824945c11dbf55fcb092b3e615b9b486f48a50e30d389635` |
| ffmpeg (BtbN, GPL, rama 8.1) | n8.1.3-9-g29e619e767 (build de fin de mes `autobuild-2026-09-30-13-08`; BtbN conserva los de fin de mes, los diarios se borran) | `https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-09-30-13-08/ffmpeg-n8.1.3-9-g29e619e767-linux64-gpl-8.1.tar.xz` | `97ce978979194b5cf7e06a5e68020dbdaa7a4f3294c5452b6a1bc347100dbb79` |
| Python portable (python-build-standalone) | CPython 3.11.17, release 20261013, `install_only_stripped` | `https://github.com/astral-sh/python-build-standalone/releases/download/20261013/cpython-3.11.17+20261013-x86_64-unknown-linux-gnu-install_only_stripped.tar.gz` | `3b8e5a248bcc351265ffe68f5e457668a2e09c99a47f81f658988c7d3d107522` |
| pymobiledevice3 | 10.7.4 | lock nuevo `packaging/linux/ios-linux/requirements-linux.lock` (con hashes; lo genera el implementador, §6.2) | — |
| hexdump | 3.3 (sdist) | `packaging/portable/ios-win/hexdump.txt` (se reutiliza tal cual) | `d781a43b…20db` |

Los SHA-256 de scrcpy, ffmpeg y Python salen del `digest` que publica GitHub para cada asset. El de scrcpy coincide
con el `SCRCPY_WIN64_SHA256` de Windows de la misma release. adb y scrcpy se bajaron y se recalcularon con
`shasum -a 256`. El script de armado vuelve a verificar todos.

### Decisiones

**D-T1. Requisitos mínimos publicados (D1).** PC x86-64 (Intel/AMD de 64 bits; ni 32 bits ni ARM) con SSE3, Linux con
**glibc ≥ 2.35** (E5), systemd, escritorio X11 o Wayland, polkit, **FUSE 3** (`fusermount3`, que viene instalado en
la matriz) y las bibliotecas de escritorio de E7. 4 GB de RAM (8 GB recomendados para la sesión completa), 10 GB
libres e internet para la configuración inicial. Matriz: Ubuntu 22.04 y 24.04, Debian 12, Mint 21/22 y Fedora (best
effort). Se publican en la página de descarga (§9.3) y en la guía.

**D-T2. Chequeo de requisitos al arrancar (escenario "PC que no cumple").** En el proceso main, antes de crear la
ventana: si `process.report.getReport().header.glibcVersionRuntime` < 2.35, se muestra un `dialog.showErrorBox` en
castellano ("Esta PC tiene una versión de Linux anterior a la que necesita Tatana (glibc X.Y; hace falta 2.35 o
posterior, por ejemplo Ubuntu 22.04 o Debian 12)") y la app sale. Electron pide glibc 2.25, así que llega a
mostrarlo. **Límite conocido:** en una PC de 32 bits el AppImage no ejecuta nada (el sistema muestra su propio
error) y, sin FUSE, el runtime solo escribe en stderr. Esos dos casos los cubre la página de descarga (los requisitos
se leen antes de bajar) y la guía (§12, "si con doble clic no pasa nada").

**D-T3. electron-builder 26 con `toolsets.appimage: "1.0.3"` (D6).** Es lo único que da el runtime estático (E8).
Cómo se convive con Windows es **DU1**. La recomendación es pasar todo `agent-ui` a `electron-builder@26.15.3`
**exacto** y borrar `scripts/parchar-electron-builder.mjs` y el `postinstall`, porque el arreglo de upstream ya viene
desde 26.12.0. Config nueva en `package.json`:
`"toolsets": { "appimage": "1.0.3" }`, `"linux": { "target": [{ "target": "AppImage", "arch": ["x64"] }], "icon":
"resources/icon.png", "category": "Utility", "executableName": "tatana", "synopsis": "Agente de Factum" }` y
`"appImage": { "artifactName": "Tatana-${version}-x86_64.${ext}" }`. **No** se fija `appImage.executableArgs`: así
queda el default del toolset nuevo (sin `--no-sandbox` fijo, E9).

**D-T4. Primer arranque y sandbox (D6).** No se agrega lanzador propio: el `AppRun` de electron-builder 26 ya hace
el fallback a `--no-sandbox` cuando no hay user namespaces (E9). "Preparar esta PC" instala el perfil de AppArmor
solo si `/proc/sys/kernel/apparmor_restrict_unprivileged_userns` vale `1` (§7.4). El perfil se asocia a la ruta fija
del AppImage, así que exige la instalación en `~/Applications` (D-T6). El chequeo `sandbox` de `/health` muestra el
estado.

**D-T5. Carpetas XDG (D14).** Empaquetado en Linux:
- `userData` = `app.getPath('appData')/Tatana` = `${XDG_CONFIG_HOME:-~/.config}/Tatana` (ya es así por
  `app.setPath('userData', …)`). Ahí van `tatana.json`, `appsettings.Local.json`, `update-state.json`,
  `preparacion-linux.json`, `instalacion-linux.json` y `logs/`.
- Datos del agente (`--data`) = `${XDG_DATA_HOME:-~/.local/share}/Tatana/data`. `tatana.json.agentDataDir` sigue
  pisándolo.
- ZIP: `~/Factum/Evidencia` (default de hoy, `AgentOptions.DefaultEvidenceDirectory`, sin cambios).
- Temporales del helper de iPhone: `Path.GetTempPath()/tatana` (`$TMPDIR` o `/tmp`, sin cambios).
Como no hay instalaciones de Linux previas, no se migra nada. En dev, Linux sigue usando `<userData>/agent-data`.

**D-T6. "Instalar en esta PC" (D8).** La ruta canónica es `~/Applications/Tatana.AppImage`, **sin versión en el
nombre**. Empaquetado en Linux, si `process.env.APPIMAGE` no es esa ruta, la ventana muestra una tarjeta "Instalá
Tatana en esta PC" (y un diálogo la primera vez) con **"Instalar (recomendado)"** y "Ahora no". Al instalar:
`mkdir -p ~/Applications`, se copia `$APPIMAGE` a `Tatana.AppImage.nuevo`, `chmod 755`, `rename` atómico, se
escriben el `.desktop` del menú y el ícono (§8.4), el autoarranque si `config.autostart` y `instalacion-linux.json`,
y después `app.relaunch({ execPath: <canónica> })` + `app.quit()`. El archivo original **no se borra** (puede estar en
un pendrive); el aviso dice "Ya podés borrar el archivo que descargaste". Si ya hay una instalada:
- versión que corre > la de `instalacion-linux.json`: se ofrece "Reemplazar la instalada";
- si no: "Abrir la instalada" (la relanza y sale).
**Sin instalar, el actualizador queda `disabled` con el motivo `no_instalado`** y la tarjeta lo explica. En cada
arranque desde la ruta canónica, Tatana reescribe el `.desktop` y el autoarranque (autorreparación).

**D-T7. Instalación de la actualización en Linux: propia, sin `quitAndInstall` (D8).** electron-updater se sigue
usando para chequear y descargar (`AppImageUpdater`, `latest-linux.yml`, `sha512`, diferencial con el blockmap
embebido). La instalación la hace `src/main/updater/appimage-install.ts`, por dos problemas medidos en el código de
`AppImageUpdater.doInstall` (electron-updater 6.8.9):
(1) con `isForceRunAfter` lanza el AppImage nuevo **antes** de que salga el viejo, y el
`requestSingleInstanceLock()` de `index.ts` hace que la instancia nueva se cierre sola;
(2) sin él hace `execFileSync` del AppImage nuevo con `APPIMAGE_EXIT_AFTER_INSTALL` (monta un FUSE solo para salir).
El flujo propio: re-verificar `size` + `sha512` del archivo descargado contra el manifiesto firmado, copiarlo a
`<canónica>.nuevo`, `chmod 755`, `fsync`, `rename` sobre la canónica (el proceso que corre conserva el inodo viejo vía
FUSE) y `app.relaunch({ execPath: <canónica> })` si corresponde relanzar. Después, `app.quit()`. El estado
`installing` persistido hace que el relanzado arranque oculto y avise "se actualizó" (lógica actual de `init`).

**D-T8. Manifiesto de Linux (D8).** Mismo sobre y misma clave que Windows. El payload lleva `product:
"tatana-linux"` y `file: "Tatana-X.Y.Z-x86_64.AppImage"`, y se publica como `tatana-update-linux.json` al lado de
`latest-linux.yml` (el nombre que usa electron-updater en Linux x64, `getChannelFilePrefix()` = `-linux`). Linux
**rechaza** un manifiesto `tatana-windows` y Windows rechaza uno `tatana-linux` (`manifiesto_invalido`), con tests de
los dos lados. El chequeo cada 6 h y "se instala al reiniciar" siguen igual (D8 validada).

**D-T9. Release única (D9).** `tatana-release.yml` suma los jobs `appimage` y `prueba-linux`. `publicar` necesita las
dos pruebas: si falla cualquiera, **no se publica nada** y queda vigente lo anterior en las dos plataformas. El
`publicar-tatana-forzado.sh` del VPS pasa a exigir **exactamente 8 archivos** (§9.2). Hasta que se vuelva a copiar
a mano en el VPS (paso manual, §13), una release falla con el código 4 sin publicar nada, que es el comportamiento
seguro. Sin portátil de respaldo para Linux (D9 validada).

**D-T10. "Preparar esta PC" con `pkexec` y el script por stdin (D2).** El script vive en
`packaging/linux/preparar-pc.sh` (lo escribe backend). `agent-ui` lo **embebe en el bundle de main**
(`import script from '<ruta>/preparar-pc.sh?raw'`) y lo ejecuta con
`pkexec /bin/bash -s -- --ejecutar --usuario <login> --version <v>`, mandando el contenido por **stdin**. No queda un
archivo temporal que otro proceso pueda cambiar entre la verificación y la ejecución. Además, root no puede leer el
montaje FUSE del usuario, así que el script no podría ejecutarse desde el AppImage. El SHA-256 del script se calcula
al vuelo y queda en `preparacion-linux.json` y en el log (trazabilidad: qué script corrió en esa PC). `pkexec` sale
con 126 (el perito cerró el diálogo) o con 127 (no hay agente de polkit o no está autorizado). Con 127, o si no
existe `pkexec`, se cae a D2 b): Tatana escribe el script en `~/.config/Tatana/preparar-pc.sh` y muestra
`sudo bash ~/.config/Tatana/preparar-pc.sh --ejecutar --usuario <login>` con un botón "Copiar" y el SHA-256.
**Sin contraseña** se corre `bash -s -- --simular`, que devuelve el plan (§7.2). Sirve para "distro sin soporte" y
para el CI.

**D-T11. Qué instala el script (D3).** Con `apt`: `usbmuxd` y `avahi-daemon`. Con `dnf`: `usbmuxd` y `avahi`.
**uxplay solo si la versión candidata del repo es ≥ 1.73** (E10): si es menor, no se instala porque no serviría.
Android: regla propia, independiente de la distro, `/etc/udev/rules.d/70-factum-tatana.rules`, que da `uaccess` a
cualquier dispositivo USB con la interfaz ADB (clase `ff`/`42`/`01`). Así no se depende de
`android-sdk-platform-tools-common` ni de grupos (`plugdev`), y no hace falta cerrar sesión. iPhone: lo resuelve el
paquete `usbmuxd` (sus reglas udev lo levantan al enchufar; el socket `/var/run/usbmuxd` es 0666). Cámara: el
`70-uaccess.rules` de systemd ya etiqueta `video4linux`. Solo si una distro no lo trae, `usermod -aG video` y
`requiere_reiniciar_sesion: true`. Micrófono: PipeWire/PulseAudio corren por usuario, no hay nada que hacer como root.
Firewall: si `ufw` está activo o `firewalld` corre, se abren mDNS y los puertos de `-p` (E10). `pacman`/`zypper` u
otro: `soportada: false`, no se ejecuta nada y se muestra la lista.

**D-T12. Sonda de usbmuxd en Linux (L6).** `ok` si existe el socket **o** si usbmuxd está instalado
(`/usr/sbin/usbmuxd`, `/usr/bin/usbmuxd` o la unidad `usbmuxd.service` en `/usr/lib/systemd/system` o
`/lib/systemd/system`): "instalado, arranca al conectar el iPhone". `missing` si no hay nada de eso. macOS no cambia
(solo mira el socket).

**D-T13. Textos por SO (L8).** `bool isWindows` pasa a `HostOs { Windows, MacOS, Linux }` en `IosErrors`,
`IosHelper.ErrorFromStderr`, `IosService.ClassifyAfcError`, `DvtRecorder` e `IosSelfTest`. Los textos de Windows y
macOS quedan **idénticos** (los tests de hoy lo fijan). Los de Linux están en §5.4. Ninguno menciona macOS, brew,
Terminal ni iTunes.

**D-T14. Micrófono en Linux (D10).** `ffmpeg -f pulse -i <Agent:MicDevice o "default">`. Si ffmpeg responde
`Unknown input format: 'pulse'` (un ffmpeg de la distro por PATH, sin libpulse), se reintenta con `-f alsa -i
default`. Mismo criterio para el audio de la cámara (`CameraArgs`, backend `v4l2`). `Agent:MicDevice` vale también
en Linux (nombre de una *source* de PulseAudio, `pactl list short sources`).

**D-T15. Cámaras en Linux (D11).** `ListLinux()` se queda solo con los nodos cuyo `/sys/class/video4linux/videoN/index`
sea `0` (el de captura; el `1` de UVC es el de metadatos). Si no hay archivo `index`, el nodo se incluye. La cámara
sigue **apagada por defecto** en Linux (`EffectiveCameraEnabled` sin cambios).

**D-T16. Señales en Linux (L2).** `Common/UnixSignals.cs`: `kill(2)` por P/Invoke (E13), con fallback al ejecutable
`kill` si tira `DllNotFoundException`/`EntryPointNotFoundException`. Lo usan `ProcessStop.SendSignalAsync` e
`IosService.GracefulStopAsync`, **solo con `OperatingSystem.IsLinux()`**: macOS sigue con el `kill` de hoy, sin
cambios de comportamiento.

**D-T17. Cierre sin huérfanos en Linux (Gherkin "Cerrar o actualizar").** `agent-process.ts` lanza el agente con
`detached: true` en Linux (sesión y grupo propios). `stopAndWait` en Linux:
(1) `SIGTERM` al pid del agente, que cierra su grabación de sesión y sus hijos como hoy, con un plazo de 10 s;
(2) `<binDir>/tools/platform-tools/adb kill-server`, porque el servidor de adb se desacopla con `setsid`;
(3) `SIGKILL` al grupo (`process.kill(-pid)`);
(4) **barrido de `/proc`**: los procesos del mismo usuario cuyo `/proc/<pid>/exe` (sin el sufijo ` (deleted)`) cae
bajo `binDir/`, con `SIGTERM` y, 2 s después, `SIGKILL` a los que sigan vivos.
Es el equivalente del `Get-Process | Where Path -like` de Windows. Se usa **al salir y antes de instalar** (en Windows,
salir sigue como hoy). El montaje `/tmp/.mount_*` es único por instancia, así que el barrido no puede tocar procesos
ajenos.

**D-T18. Autoarranque y bandeja (D15).** Electron no implementa `setLoginItemSettings` en Linux. `applyLoginItem`
escribe o borra `${XDG_CONFIG_HOME:-~/.config}/autostart/factum-tatana.desktop` (`Exec="<canónica>" --hidden`), solo
con la instalación canónica. Bandeja: se crea igual. Hay bandeja si existe el nombre DBus
`org.kde.StatusNotifierWatcher` (consulta con `gdbus call --session … NameHasOwner`, timeout 2 s; si `gdbus` no
está, se asume que hay). **Sin bandeja**, cerrar la ventana la **minimiza** en vez de esconderla. Tatana arrancado
con `--hidden` se vuelve a abrir desde el menú de aplicaciones: la segunda instancia muestra la ventana, como hoy. La
guía lo documenta.

**D-T19. Pantalla "Librerías" (Notas de la HU).** Empaquetado en Linux **no se muestra** (no aparece en la Sidebar) y
los handlers `tatana:install-library`/`update-library` no hacen nada. En dev y en Windows/macOS sigue igual.

**D-T20. Post-proceso de iPhone en PCs chicas (D12).** `Agent:VideoPostprocess:MinProcessorCount` (int?, nuevo):
`null` = 4 en Linux y 0 en Windows/macOS. Si `Environment.ProcessorCount` < el valor efectivo, no se generan
variantes ("Suavizado"/"MCI"): el stop devuelve `variants: []`, como con `Enabled = false`, y queda logueado. Android
no recodifica (scrcpy) y la cámara viene apagada, así que con esto la PC mínima no se satura.

**D-T21. ToolResolver con subruta portátil para Python.** PBS deja el ejecutable en `python/bin/python3`. `ToolSpec`
suma un `UnixPortablePath` opcional. Para Python es `"bin/python3"`: Linux busca
`tools/python-embed/bin/python3`. El paso de PATH sigue usando `UnixName` (`python3`). Windows no cambia. Los tests
de `ToolResolverTests` lo cubren.

**D-T22. Python 3.11.17 en Linux y 3.11.9 en Windows.** No hay 3.11.9 de PBS con los últimos parches y la
trazabilidad la da el lock. El lock de Linux usa **como constraints las versiones del lock de Windows** (E12), así
pymobiledevice3 y sus dependencias son las mismas en las dos plataformas. `generar-lock.py --check` lo verifica en
CI.

**D-T23. Sin `InvariantGlobalization`.** Activarlo cambiaría el comportamiento de `IdnMapping`/`Uri` y comparaciones
respecto de Windows. libicu viene en todo escritorio (E7). CI lo instala en los contenedores, que es justo lo que
falta en un contenedor mínimo y no en una PC.

**D-T24. Agente publicado igual que en Windows.** Single-file (lo fija el `.csproj`) con los `.so` nativos al lado,
`IncludeNativeLibrariesForSelfExtract=false` y `appsettings.json` al lado. Nada se extrae a disco al arrancar y anda
desde el montaje de solo lectura. El payload viaja como **`.tar.gz`** (no zip) para conservar los bits `+x` y los
symlinks de PBS.

**D-T25. AirPlay en Linux.** `AirplayAvailable` en Linux exige uxplay encontrado **y** con `-mp4` (`uxplay -h`
contiene `-mp4`, sondeado una vez por ruta con timeout de 3 s) **y** avahi corriendo (`/run/avahi-daemon/socket`).
Motivos nuevos en `/health.ios.airplay_unavailable_reason`: `uxplay_without_mp4` y `avahi_not_running`. En Linux,
uxplay se lanza además con `-p` (puertos fijos, para que el firewall que abrió el script sirva). macOS no cambia.

**D-T26. Autoprueba de Linux para CI y soporte.** `Factum.Agent --autoprueba-linux <carpeta>`, en el estilo de
`--autoprueba-ios` (§5.7). La usan el humo, los contenedores y el paso 1 de la QA.

**D-T27. Página de descarga estática.** `publicar-tatana-forzado.sh` genera `/descargas/tatana/index.html` (heredoc
dentro del script; sin JS) con los requisitos de D-T1, los dos enlaces con su SHA-256, los pasos para marcar el
AppImage como ejecutable (GNOME/Nautilus, KDE/Dolphin, Cinnamon/Nemo) y la versión. La web lo enlaza para Linux.
Caddy ya sirve `/descargas/*` con `file_server` (`index.html` incluido).

---

## 5. Agente Tatana (`server/src/Factum.Agent`)

### 5.1 `Common/HostOs.cs` (nuevo)

```csharp
public enum HostOs { Windows, MacOS, Linux }
public static class HostOsInfo
{
    public static HostOs Current { get; }            // por OperatingSystem.Is*()
    public static string JsonName(HostOs os);        // "windows" | "macos" | "linux"
}
```

### 5.2 `Services/Linux/LinuxHostProbe.cs` (nuevo, singleton en DI)

Solo hace algo en Linux. En otro SO, `GetStatus()` devuelve `null`. Todo lo que depende del sistema va inyectable
(raíz de archivos, `Func<string,bool> exists`, reloj) para los tests. Hay caché de 5 s, como `AppleServiceProbe`.

- `OsRelease.Parse(string)`: puro, lee `ID`, `VERSION_ID`, `PRETTY_NAME` e `ID_LIKE` de `/etc/os-release` (con
  fallback a `/usr/lib/os-release`) y saca las comillas.
- glibc: `gnu_get_libc_version()` por P/Invoke (`libc`). Si falla, `null`.
- Gestor de paquetes: el primero que exista entre `/usr/bin/apt-get` → `apt`, `/usr/bin/dnf` → `dnf`,
  `/usr/bin/pacman` → `pacman` y `/usr/bin/zypper` → `zypper`; si no, `unknown`. `setup_supported` = `apt` o `dnf`.
- Chequeos (`id` → regla). `required` vale `true` solo para `usbmuxd` y `android_usb`:

| `id` | `ok` | otro estado |
|---|---|---|
| `usbmuxd` | socket `/var/run/usbmuxd`, o binario/unidad instalados (D-T12) | `missing` |
| `android_usb` | existe `/etc/udev/rules.d/70-factum-tatana.rules`, o `51-android.rules` en `/etc/udev/rules.d`, `/usr/lib/udev/rules.d` o `/lib/udev/rules.d` | `missing` |
| `avahi` | existe `/run/avahi-daemon/socket` | `warning` si hay `/usr/sbin/avahi-daemon` pero no corre; si no, `missing` |
| `uxplay` | encontrado y con `-mp4` | `missing` (no está) · `outdated` (sin `-mp4`; `detail` con la versión si `uxplay -h` la imprime: "UxPlay 1.68") |
| `camera` | todos los nodos de captura (D-T15) con `access(R_OK\|W_OK)` = 0 | `not_applicable` (sin cámaras) · `warning` (alguno sin permiso; `detail` = la ruta) |
| `microphone` | existe `$XDG_RUNTIME_DIR/pulse/native` | `warning` ("No hay servidor de sonido PulseAudio/PipeWire en esta sesión") |
| `sandbox` | `apparmor_restrict_unprivileged_userns` = 1 **y** existe `/etc/apparmor.d/factum-tatana` | `not_applicable` (el sysctl no existe o vale 0) · `warning` (vale 1 y no hay perfil) |

- `setup_state` = `"ok"` si todos los `required` están `ok`; si no, `"incomplete"`.
- El sondeo de `uxplay -h` (texto de ayuda y versión) vive en un `UxplayProbe` chico, con caché por ruta y una
  sola corrida en segundo plano. Lo usan el chequeo y `IosService.AirplayAvailable`.

### 5.3 `/health` (cambio aditivo; contrato en §10.A)

`HealthController.Health` suma `os = HostOsInfo.JsonName(HostOsInfo.Current)` y `linux = linuxProbe.GetStatus()`
(null fuera de Linux, así que no viaja por `WhenWritingNull`). `ios.airplay_unavailable_reason` puede valer además
`uxplay_without_mp4` o `avahi_not_running` (solo Linux). No hay capability nueva: agent-ui viaja con su agente y la
web solo lee `os` si está.

### 5.4 Textos de Linux (exactos)

| Dónde | Texto de Linux |
|---|---|
| `IosErrors` `AppleServiceMissing` | `No se encontró el servicio de iPhone (usbmuxd) en esta PC. Abrí Tatana, tocá "Preparar esta PC" y volvé a conectar el iPhone.` |
| `IosErrors` `AdminRequired` | `Linux no dejó a Tatana abrir la conexión con el iPhone. Abrí Tatana, tocá "Preparar esta PC" y reintentá.` |
| `IosErrors` `ToolsMissing` | `Tatana no encuentra sus herramientas de iPhone (Python o ffmpeg). Descargá Tatana de nuevo desde la página de Factum.` |
| `IosErrors` `AirplayUnavailable` | `AirPlay no está disponible en esta PC: hace falta uxplay 1.73 o posterior con el servicio avahi activo. Usá "Solo pantalla", "Pantalla + micrófono de PC" o "Grabación nativa del iPhone".` |
| `IosService` (uxplay no arrancó, hoy L780) | `No se pudo iniciar el receptor AirPlay (uxplay). Revisá "Chequeos de esta PC" en Tatana y reintentá.` |
| `AdbService` (adb no encontrado, L170 y L917) | `No se encontró adb, el componente de Android de Tatana. Descargá Tatana de nuevo desde la página de Factum.` |
| `IosErrors.LowerFirst` | sumar `"Linux"` a los nombres propios |

Los demás códigos ya son neutrales. Windows y macOS: textos idénticos a los de hoy.

### 5.5 Micrófono, cámara y uxplay

- `MicCapture.StartAsync` (Linux): `-f pulse -i <MicDevice ?? "default">`. Si el arranque falla temprano y el stderr
  contiene `Unknown input format`, reintenta una vez con `-f alsa -i default` (D-T14). El texto de error sigue con
  el prefijo `ErrorOpenPrefix` de hoy.
- `CameraArgs.Input(BackendLinux, withAudio)`: el audio pasa a `-f pulse -i <mic ?? "default">` (`audioMap = "1:a"`
  igual). El `mic` ya llega como parámetro. El fallback a `alsa` va en `CameraTrack`: con el mismo criterio,
  relanza una vez con `alsa`.
- `CameraDevices.ListLinux()`: filtro por `index` (D-T15). Se extrae una función pura que recibe la raíz (`/dev` y
  `/sys/class/video4linux`) para testearla con un árbol falso.
- `IosService`: `AirplayAvailable`/`AirplayUnavailableReason` según D-T25. `-p` solo en Linux.
  `DYLD_LIBRARY_PATH` sigue solo en macOS.

### 5.6 Señales y herramientas

- `Common/UnixSignals.cs` (nuevo): `static bool TrySend(int pid, int signal)` (P/Invoke, `SetLastError`) más el
  fallback al ejecutable. Constantes `SIGINT = 2` y `SIGTERM = 15`. Lo usan `ProcessStop` (Linux) e
  `IosService.GracefulStopAsync` (Linux).
- `ToolSpec` + `UnixPortablePath` (D-T21): `AgentTools.Python = new("python", "python-embed", "python3", "python.exe")
  { UnixPortablePath = "bin/python3" }`. En `ToolResolver.Find`, el paso 2 usa `UnixPortablePath ?? name` cuando
  `!IsWindows`.
- `VideoPostprocessOptions.MinProcessorCount` (D-T20): se valida en `Normalize` (si es negativo, vuelve a null con
  un warning) y se aplica en `VideoPostprocessService`.

### 5.7 `--autoprueba-linux <carpeta>` (`Services/Linux/LinuxSelfTest.cs`, nuevo; flag en `Program.cs`, antes del host)

Una línea `OK  …` / `FALLA  …` por paso. Sale con 0 solo si pasan todos. Solo escribe en `<carpeta>`. Fuera de
Linux imprime `FALLA  solo Linux` y sale con 1.

1. adb, scrcpy, ffmpeg y python resueltos con `source = portable` (desde `tools/`).
2. `ffmpeg -hide_banner -devices` lista `pulse` y `v4l2`.
3. Grabación lavfi de 5 s con `CameraArgs.Build` (backend `lavfi`, un tramo), cortada con
   `ProcessStop.StopAsync(..., unixSignal: "-15")`. Tiene que cerrar limpio y `FfmpegProbe` debe dar duración > 0
   en el MKV.
4. `UnixSignals.TrySend` a un `sleep 30` hijo: sale con 143.
5. `LinuxHostProbe.GetStatus()`: imprime distro, glibc, gestor, `setup_state` y los chequeos en JSON de una línea
   (no falla por chequeos `missing`, que en CI son lo esperado).
6. `glibc` ≥ 2.35.

### 5.8 Tests (`server/tests/Factum.Agent.Tests`, corren en Ubuntu en `verificar.yml`)

`AppleServiceProbeTests` (Linux: socket / instalado sin socket / nada; macOS sin cambios) · `IosErrorsTests`
(textos Linux de §5.4; Windows y macOS iguales; ningún texto Linux contiene `macOS`, `brew`, `Terminal` ni
`iTunes`) · `ToolResolverTests` (subruta portátil de Python en Unix, Windows igual) · `OsReleaseTests` (Ubuntu,
Debian, Fedora, Mint y Arch, con comillas y sin `VERSION_ID`) · `LinuxHostProbeTests` (cada fila de §5.2 con un
árbol falso) · `CameraDevicesLinuxTests` (dos nodos con `index` 0 y 1 → una sola cámara) · `MicArgsTests` (Linux usa
`pulse`; fallback ante `Unknown input format`) · `UnixSignalsTests` (`[SkippableFact]` / return temprano fuera de
Linux: SIGTERM a `sleep`) · `ProcessStopTests` (Linux usa `UnixSignals`) · `VideoPostprocessOptionsTests`
(`MinProcessorCount`) · `HealthControllerTests` o equivalente (con `os` y con `linux` null fuera de Linux).

---

## 6. Empaquetado (lo hace `implementer-backend`)

### 6.1 `deploy/linux/armar-tatana-linux.sh` (nuevo, hermano de `deploy/windows/armar-tatana-portable.sh`)

```
deploy/linux/armar-tatana-linux.sh --version X.Y.Z --src <árbol limpio> --salida <dir> [--origenes "<o1>[,<o2>]"]
  → <dir>/Tatana-Agent-vX.Y.Z-linux-x64.tar.gz
```

Mismo estilo y las mismas guardas que el de Windows (`set -euo pipefail`; `--src` tiene que ser `git archive`; nada
de BSD ni macOS; requisitos dotnet 10, python3 con pip, curl, unzip, tar y xz):
1. `verificar_mock` en la fuente.
2. `dotnet publish … -r linux-x64 --self-contained true -p:Version=$VERSION` (D-T24). Se verifica que existan
   `Factum.Agent` y `appsettings.json`, que no esté `appsettings.Local.json` y `Mock = false`. Con `--origenes`,
   hornea con `../windows/hornear-origenes-tatana.py` (se busca relativo a **este** script; mismo validador antes de
   descargar).
3. adb r37.0.1 (§4, versiones fijas) → `tools/platform-tools/` (todo el directorio, con `lib64/`).
4. scrcpy 4.1 Linux → `tools/scrcpy/` **sin su `adb`** (el de platform-tools es el único servidor) y
   `THIRD-PARTY-NOTICES.txt` (Apache-2.0; scrcpy enlaza SDL3, FFmpeg y libusb estáticos; las versiones se toman de
   `app/deps/*.sh` del tag, como en Windows).
5. PBS 3.11.17 → contenido de `python/` a `tools/python-embed/`. pymobiledevice3: `python3 -m pip install --no-compile
   --no-deps --require-hashes --only-binary=:all: --platform manylinux2014_x86_64 --platform manylinux_2_17_x86_64
   --platform manylinux_2_28_x86_64 --platform linux_x86_64 --implementation cp --python-version 311 --target
   tools/python-embed/lib/python3.11/site-packages -r packaging/linux/ios-linux/requirements-linux.lock`, más el
   wheel de hexdump construido desde `packaging/portable/ios-win/hexdump.txt`. Se copia el lock como
   `TATANA-PYTHON-LOCK.txt` y se escriben los avisos (PSF, GPL-3.0 de pymobiledevice3).
6. ffmpeg BtbN 8.1 (§4) → solo `bin/ffmpeg` a `tools/ffmpeg/ffmpeg`, con `THIRD-PARTY-NOTICES.txt` (GPL; fuente:
   el tag de BtbN y el commit de FFmpeg del nombre del archivo). Si la máquina que arma es Linux x86-64, se corre
   `ffmpeg -devices` y se exigen `pulse` y `v4l2`; si no, solo un aviso.
7. `version.txt`, `chmod 755` de `Factum.Agent`, `tools/platform-tools/adb`, `tools/scrcpy/scrcpy`,
   `tools/ffmpeg/ffmpeg` y `tools/python-embed/bin/python3*`, y `tar -czf`.
8. **Chequeo del tar final:** archivos requeridos (`Factum.Agent`, `appsettings.json`, `tools/platform-tools/adb`,
   `tools/scrcpy/{scrcpy,scrcpy-server,LICENSE,THIRD-PARTY-NOTICES.txt}`, `tools/ffmpeg/{ffmpeg,THIRD-PARTY-NOTICES.txt}`,
   `tools/python-embed/bin/python3`,
   `tools/python-embed/lib/python3.11/site-packages/pymobiledevice3/__init__.py`, `…/pymobiledevice3-10.7.4.dist-info/METADATA`
   con `Version: 10.7.4`, `TATANA-PYTHON-LOCK.txt`). Además: **no** está `tools/scrcpy/adb`, los ejecutables listados
   tienen `x` en `tar -tvzf`, y con `--origenes` los `AllowedOrigins` del `appsettings.json` del tar son exactamente
   los horneados. Por último, **guarda de glibc**: `python3 -I packaging/linux/glibc-maximo.py --max 2.35 <árbol>`
   falla si algún ELF pide más (evita que un cambio de versión suba el requisito sin que nadie se entere).

### 6.2 `packaging/linux/`

- `ios-linux/requirements.in`: `pymobiledevice3==10.7.4` + comentario. hexdump sale de `ios-win/hexdump.txt`.
- `ios-linux/generar-lock.py`: copia adaptada de `ios-win/generar-lock.py`, con plataforma manylinux x86-64 /
  cp311, marcadores de Linux (`sys_platform=linux`, `platform_system=Linux`, `platform_machine=x86_64`,
  `os_name=posix`, `python_full_version=3.11.17`) y **las versiones de `requirements-win.lock` como constraints**
  (D-T22). `--check` funciona igual que en Windows.
- `ios-linux/requirements-linux.lock`: generado y commiteado.
- `glibc-maximo.py`: lector de `SHT_GNU_verneed` de los ELF de un árbol, sin dependencias. Imprime el máximo y
  sale distinto de 0 si supera `--max`.
- `preparar-pc.sh`: §7.
- `tests/probar-preparar-pc.sh`: corre `--simular` con `TATANA_RAIZ_PRUEBA=<dir>` (prefijo de rutas para leer un
  `etc/os-release` y binarios falsos) para Ubuntu 22.04/24.04, Debian 12, Fedora, Mint y Arch, y compara el plan
  esperado (§7.2). Lo corre `verificar.yml`.

---

## 7. `packaging/linux/preparar-pc.sh` (contrato con agent-ui en §10.B)

### 7.1 Modos

| Modo | Root | Qué hace |
|---|---|---|
| `--simular` | no | Detecta la distro y el gestor e imprime **una** línea `TATANA_PLAN <json>`. No cambia nada. Sale con 0, o con 3 si la distro no está soportada |
| `--ejecutar --usuario <login> [--version X.Y.Z]` | sí (`pkexec` o `sudo`) | Ejecuta los pasos en orden. Por cada paso imprime `TATANA_PASO <json>` al empezar (`en_curso`) y al terminar, y al final `TATANA_RESULTADO <json>`. El resto de la salida es log |

Validaciones de `--ejecutar`: `id -u` = 0 (si no, sale con 5). `--usuario` tiene que existir (`getent passwd`) y,
si hay `PKEXEC_UID`, coincidir con él. Si la distro no está soportada, sale con 3 **antes de cambiar nada**. Es
idempotente: correrlo dos veces deja lo mismo. `LC_ALL=C` para los comandos y
`DEBIAN_FRONTEND=noninteractive`.

Códigos de salida: 0 todo ok · 1 algún paso falló (los demás se intentan igual; resultado `parcial`) · 3 distro sin
soporte · 4 sin internet · 5 sin root · 2 argumentos.

### 7.2 Plan y pasos

| Paso (`id`) | apt (Debian/Ubuntu/Mint) | dnf (Fedora) |
|---|---|---|
| `paquetes` | `apt-get -o DPkg::Lock::Timeout=120 update` (si falla, se sigue igual: un `cdrom:` en `sources.list` no implica falta de internet) → `apt-get -o DPkg::Lock::Timeout=120 install -y --no-install-recommends usbmuxd avahi-daemon` (+ `uxplay` si `apt-cache policy uxplay` da candidato ≥ 1.73). Si la instalación falla con `Temporary failure resolving` / `Could not resolve` → código 4 | `dnf install -y usbmuxd avahi` (+ `uxplay` si `dnf repoquery --qf '%{version}' uxplay` ≥ 1.73) |
| `reglas_usb` | escribe `/etc/udev/rules.d/70-factum-tatana.rules` (contenido abajo) y corre `udevadm control --reload-rules` y `udevadm trigger --action=change --subsystem-match=usb`. Si hay un Apple enchufado (`05ac` en `/sys/bus/usb/devices/*/idVendor`), `systemctl restart usbmuxd` (best effort) | igual |
| `airplay` | `systemctl enable --now avahi-daemon`. Si `ufw status` dice `active`: `ufw allow 5353/udp`, `ufw allow 7000,7001,7100/tcp`, `ufw allow 6000,6001,7011/udp` (con `comment 'Factum Tatana AirPlay'`) | `systemctl enable --now avahi-daemon`. Si `firewall-cmd --state` = `running`: `--permanent --add-service=mdns`, los mismos puertos y `--reload` |
| `camara_microfono` | si ningún `70-uaccess.rules` (`/usr/lib/udev/rules.d`, `/lib/udev/rules.d`) menciona `video4linux`: `usermod -aG video <usuario>` y `requiere_reiniciar_sesion: true`. Si no, `omitido` ("la sesión ya tiene acceso") | igual |
| `sandbox` | solo si `/proc/sys/kernel/apparmor_restrict_unprivileged_userns` = 1 y existe `apparmor_parser`: escribe `/etc/apparmor.d/factum-tatana` (abajo) y corre `apparmor_parser -r`. Si no, `omitido` | igual (Fedora: `omitido`) |

`/etc/udev/rules.d/70-factum-tatana.rules` (exacto):
```
# Factum Tatana: acceso a celulares Android por USB (adb) para el usuario de la sesión activa (logind, uaccess).
# Lo escribe "Preparar esta PC" de Tatana. Se puede borrar: Tatana lo vuelve a crear si hace falta.
SUBSYSTEM=="usb", ENV{DEVTYPE}=="usb_device", ENV{ID_USB_INTERFACES}=="*:ff4201:*", TAG+="uaccess"
```

`/etc/apparmor.d/factum-tatana` (exacto):
```
# Factum Tatana: permite que Chromium (la ventana de Tatana) use su sandbox con user namespaces (Ubuntu 23.10+).
abi <abi/4.0>,
include <tunables/global>

profile factum-tatana @{HOME}/Applications/Tatana.AppImage flags=(unconfined) {
  userns,
  include if exists <local/factum-tatana>
}
```

`TATANA_PLAN` (`--simular`), una línea JSON:
```json
{"schema":1,"soportada":true,"distro_id":"ubuntu","distro_version":"24.04","distro_name":"Ubuntu 24.04.1 LTS",
 "gestor":"apt","paquetes":["usbmuxd","avahi-daemon"],"uxplay":{"candidata":"1.68","instalar":false},
 "archivos":["/etc/udev/rules.d/70-factum-tatana.rules","/etc/apparmor.d/factum-tatana"],
 "servicios":["avahi-daemon"],"firewall":"inactivo","grupos":[],
 "comandos":["apt-get update","apt-get install -y --no-install-recommends usbmuxd avahi-daemon","…"]}
```
`comandos` es la lista legible que se copia para soporte. Con `soportada: false` (pacman, zypper, desconocido),
`comandos` trae el equivalente **genérico** (paquetes `usbmuxd` y `avahi` y el contenido de los dos archivos), para
que soporte lo adapte.

### 7.3 `TATANA_PASO` / `TATANA_RESULTADO`

```json
TATANA_PASO {"id":"paquetes","estado":"en_curso|ok|error|omitido","detalle":"usbmuxd avahi-daemon"}
TATANA_RESULTADO {"schema":1,"resultado":"ok|parcial|sin_soporte|sin_internet","requiere_reiniciar_sesion":false,
                  "pasos":[{"id":"paquetes","estado":"ok","detalle":"…"}, …]}
```
`detalle` es castellano corto: en `error`, el motivo (última línea útil del comando).

### 7.4 Desinstalación

No hay paso automático. La guía (§12) lista lo que quedó en la PC (los dos archivos, los paquetes y las reglas de
firewall) y cómo sacarlo a mano.

---

## 8. agent-ui (lo hace `implementer-frontend`; app: **`agent-ui/`**)

### 8.1 `package.json`
Según DU1, por default electron-builder `26.15.3` exacto con D-T3, sin `postinstall` ni
`scripts/parchar-electron-builder.mjs`. `"test": "tsx --test src/main/updater/*.test.ts src/main/linux/*.test.ts"`.

### 8.2 `src/main/linux/` (nuevo; todo lo puro con su `*.test.ts`)
- `paths.ts`: `canonicalAppImagePath()`, `dataDir()` (D-T5), `desktopEntryPath()`, `autostartPath()` e
  `iconPath()` (`~/.local/share/icons/hicolor/512x512/apps/factum-tatana.png`). Respetan `XDG_*`, `HOME` y
  `XDG_RUNTIME_DIR`.
- `requirements.ts`: `glibcTooOld(version, min = '2.35')` (puro) + el diálogo de D-T2.
- `desktop-integration.ts`: `desktopEntryText({ exec, version, hidden })` (puro, §8.4), `installOnPc()` (D-T6),
  `refreshDesktopEntries()` y `applyAutostart(enabled)` (D-T18).
- `prepare-pc.ts`: `simulate()`, `execute(onStep)` y `fallbackCommand()` (D-T10). Parser **puro**
  `parsePrepareLine(line)` → `{ kind: 'plan'|'paso'|'resultado'|'log', data }`. Mapeo de salida: 126 → `cancelado`,
  127 o sin `pkexec` → `sin_polkit` (muestra el fallback), 3 → `sin_soporte`, 4 → `sin_internet`, 1 → `parcial`,
  0 → `ok`. Guarda `~/.config/Tatana/preparacion-linux.json` (§10.F) y agrega el log a
  `~/.config/Tatana/logs/preparacion-linux.log` (comando, fecha, SHA-256 del script y la salida completa).
- `process-sweep.ts`: `selectOrphans(entries, binDir, selfPid)` (puro) + `sweep(binDir)` (D-T17).
- `tray-support.ts`: `hasStatusNotifierWatcher()` (D-T18).
- `env.d.ts`: `declare module '*.sh?raw' { const s: string; export default s }`.

### 8.3 Cambios en archivos existentes
- `agent-process.ts`: `agentDataDir()` en Linux empaquetado → `paths.dataDir()`. `spawn` con `detached: true` en
  Linux. `stopAndWait` en Linux según D-T17. `stop()` en Linux delega en `stopAndWait` (salir sin huérfanos).
- `config.ts`: `applyLoginItem` en Linux → `applyAutostart` (D-T18).
- `index.ts`: antes de `ready`, el chequeo de D-T2 (solo Linux empaquetado). En `ready` (Linux empaquetado):
  `refreshDesktopEntries()` si corre desde la canónica, y la oferta de instalación (D-T6) si no. Cerrar la ventana:
  `hide()` con bandeja y `minimize()` sin ella. La primera vez sin `preparacion-linux.json`, o con
  `/health.linux.setup_state = "incomplete"`, abre la vista "Esta PC". Si arrancó `--hidden`, manda una sola
  `Notification` ("Tatana necesita preparar esta PC para reconocer iPhone y Android").
- `updater/update-manager.ts`: en Linux, el manifiesto se pide a `url + 'tatana-update-linux.json'`. Motivos nuevos
  de `disabled`: `sin_appimage` (sin `process.env.APPIMAGE`) y `no_instalado` (D-T6). El archivo descargado se elige
  por `.AppImage` (hoy busca `.exe`). Hay que conservar `downloadedFile` hasta instalar. `installNow` en Linux:
  `stopAndWait` (D-T17) y después `appimage-install.ts` (D-T7) en lugar de `quitAndInstall`. El bloque `win32` de
  hoy queda igual.
- `updater/manifest.ts`: `UpdatePayload.product: 'tatana-windows' | 'tatana-linux'`. `validatePayload(raw,
  expected)`: el producto esperado sale de `process.platform` (`linux` → `tatana-linux`, cualquier otro →
  `tatana-windows`, como hoy) y el `file` se valida según el producto (§10.C). Tests nuevos en `manifest.test.ts`:
  Linux válido, Linux rechaza Windows, Windows rechaza Linux y `file` con otro nombre.
- `updater/state.ts` y el espejo del renderer: `UpdateReason` suma `no_instalado` y `sin_appimage`.
- `app-info.ts`: `TatanaAppInfo` suma `platform` (`process.platform`), `packaged` y
  `linux?: { installed: boolean; appImagePath?: string; canonicalPath: string; trayAvailable: boolean }`.
- `agent-api.ts`: `getHealth(port)` → tipo `AgentHealth` con `os` y `linux` (§10.A).
- `ipc.ts` + `preload/index.ts` + `renderer/src/env.d.ts`: canales de §10.G. Los de Librerías no hacen nada en
  Linux empaquetado.
- **Renderer:** vista nueva **"Esta PC"** (Sidebar, solo Linux) con:
  - tarjeta "Instalá Tatana en esta PC" (si `!linux.installed`);
  - asistente "Preparar esta PC": explicación de una línea, botón, progreso por paso (paquetes, reglas USB, AirPlay,
    cámara/micrófono, sandbox) y resultado;
  - lista de chequeos con ícono por fila y botón "Volver a revisar" (sin contraseña). Etiquetas por `id`:
    `usbmuxd` → "iPhone por USB", `android_usb` → "Android por USB", `avahi` → "AirPlay (descubrimiento en la red)",
    `uxplay` → "AirPlay (receptor)", `camera` → "Cámara", `microphone` → "Micrófono", `sandbox` → "Protección de
    la ventana". Muestra `detail` cuando no está `ok`;
  - aviso destacado si `requiere_reiniciar_sesion`;
  - estados de error de la HU (UX, "Estados de error del asistente"): cancelado → `Sin la contraseña, Tatana no
    puede reconocer iPhone ni Android por USB.` + "Reintentar"; sin internet → `Hace falta internet para instalar los
    paquetes del sistema.`; sin soporte → los `comandos` del plan con "Copiar para soporte técnico"; paso con error →
    fila en rojo, motivo y "Ver detalle" (abre el log con `shell.openPath`); sin polkit → el comando de D-T10 con
    "Copiar".

  `UpdateStatus` suma los textos de `no_instalado` ("Instalá Tatana en esta PC para recibir actualizaciones") y
  `sin_appimage`. `Libraries` y su ítem de Sidebar se ocultan en Linux empaquetado. Settings: con Linux sin
  instalar, el interruptor de autoarranque queda deshabilitado con la explicación.

### 8.4 `.desktop` (menú y autoarranque)

```
[Desktop Entry]
Type=Application
Name=Tatana
Comment=Agente de Factum para capturar evidencia de celulares
Exec="/home/<u>/Applications/Tatana.AppImage" %U          ← autoarranque: "…/Tatana.AppImage" --hidden
Icon=factum-tatana
Terminal=false
Categories=Utility;
StartupWMClass=<el WM_CLASS real de la ventana; el implementador lo confirma con xprop en la QA>
X-AppImage-Version=<versión>
X-GNOME-Autostart-enabled=true                          ← solo en el de autoarranque
```
Las rutas con espacios van entre comillas, como manda la especificación de Desktop Entry. El ícono sale de
`resources/icon.png` (1024×1024), copiado y escalado a 512 con `nativeImage.resize`.

---

## 9. CI, release y VPS (lo hace `implementer-backend`)

### 9.1 `.github/workflows/tatana-linux.yml` (nuevo, humo en PR; D13 nivel 1 y 2)

Disparo: `workflow_dispatch` y PRs a `develop`/`main` que tocan `server/src/Factum.Agent/**`, `agent-ui/**`,
`packaging/linux/**`, `deploy/linux/**`, `ci/tatana-linux/**` o el propio workflow. Las acciones van fijadas por SHA,
como en los workflows actuales.

- **`armar`** (ubuntu-24.04): checkout, setup-dotnet 10, setup-python 3.12, setup-node 22.
  `python3 -I packaging/linux/ios-linux/generar-lock.py --check`, `armar-tatana-linux.sh --version 0.0.0-ci`,
  extraer en `agent-ui/resources/agent`, `npm ci`, `npx electron-vite build` y
  `npx electron-builder --linux AppImage --x64 --publish never`. Sube el AppImage (`retention-days: 3`).
- **`humo`** (ubuntu-24.04, needs armar): `ci/tatana-linux/prueba-humo.sh <AppImage>`:
  (a) `--appimage-extract` y verificar los bits `x` dentro de `squashfs-root/resources/agent/`;
  (b) `squashfs-root/resources/agent/Factum.Agent --autoprueba-linux $RUNNER_TEMP/al` y `--autoprueba-ios
  $RUNNER_TEMP/ai`;
  (c) arrancar el agente (`--port 8765 --data $RUNNER_TEMP/d --mode installed`) y comprobar en `/health`: `os ==
  "linux"`, `linux.distro_id == "ubuntu"`, `tools.{adb,scrcpy,ffmpeg,python}.source == "portable"`,
  `tools.python.pymobiledevice3_version == "10.7.4"` y `ios.apple_service` presente. Después, SIGTERM y comprobar
  que salió en < 20 s;
  (d) `bash packaging/linux/preparar-pc.sh --simular` → `soportada: true`, `gestor: apt`;
  (e) UI: `xvfb-run -a` del AppImage **montado con FUSE** (instalar `fuse3` si falta). Si no hay `/dev/fuse`, con
  `APPIMAGE_EXTRACT_AND_RUN=1`. Con `HOME`/`XDG_*` en `$RUNNER_TEMP`, a los 20 s el proceso sigue vivo, `/health`
  responde (lo lanzó Electron) y `~/.config/Tatana/tatana.json` existe. Después "Salir" (segunda instancia con
  `--quit`) y, a los 15 s, ningún proceso con `exe` bajo `/tmp/.mount_Tatan*` ni `squashfs-root`.
  Si falla, sube los logs.
- **`distros`** (needs armar; matriz `ubuntu:22.04`, `debian:12` y `fedora:latest` con `continue-on-error: true` solo
  Fedora; `container:` del job): instala lo que un escritorio garantiza y un contenedor no (apt: `ca-certificates
  libicu* libssl3 libudev1 procps curl jq`; dnf: `libicu openssl-libs systemd-libs procps-ng curl jq`), extrae el
  AppImage y corre (b), (c) con `linux.distro_id` de la imagen y (d) (`gestor` `apt` o `dnf`). En `ubuntu:22.04` y
  `debian:12` corre además la UI (e) con `xvfb` y las libs de Electron (`libgtk-3-0 libnss3 libgbm1
  libasound2|libasound2t64 libxss1 xauth xvfb`) y `--no-sandbox`. **Es la prueba de "PC vieja con distro vieja"**
  (glibc 2.35, bibliotecas faltantes).

### 9.2 `tatana-release.yml` (cambios)

- `on.pull_request.paths`: suma `deploy/linux/**`, `packaging/linux/**`, `agent-ui/src/main/linux/**` y
  `agent-ui/src/main/agent-process.ts`.
- `preparar` → "Chequeos del instalador" suma: `build.linux.target` = AppImage x64, `build.appImage.artifactName`
  = `Tatana-${version}-x86_64.${ext}` y `build.toolsets.appimage` = `1.0.3`.
- `payload` además corre `armar-tatana-linux.sh … --origenes "$ORIGENES"` y sube el artifact `payload-linux`
  (`Tatana-Agent-v$VERSION-linux-x64.tar.gz`). También publica la base 0.0.1 de Linux (`dotnet publish -r linux-x64
  -p:Version=0.0.1`, solo `Factum.Agent`) como `agente-base-linux`.
- **`appimage`** (nuevo, ubuntu-24.04, needs preparar + payload): extrae `payload-linux` en
  `agent-ui/resources/agent` (con las mismas guardas que el paso de Windows, en bash: archivos requeridos,
  `Mock`, `AllowedOrigins`, sin `appsettings.Local.json`). Escribe `tatana-dist.json` (mismo esquema). En ensayo
  genera su **propia** clave efímera (`generar-clave-firma.mjs`) y exporta `TATANA_EXTRA_UPDATE_KEY`; en release
  exige que esté vacía. Después `npm ci`, `npm version $VERSION`, `electron-vite build` y `electron-builder --linux
  AppImage --x64 --publish never "-c.publish.url=$PRIMERA"`, y comprueba que existan
  `dist/Tatana-$VERSION-x86_64.AppImage` y `dist/latest-linux.yml`. Base 0.0.1: misma app con
  `agente-base-linux`, salida en `dist-base/`. **Solo ensayo:** arma el payload, firma con la clave efímera
  (`--producto tatana-linux`), verifica y escribe el `SHA256SUMS` de los 3 archivos de Linux en `$RUNNER_TEMP/canal-linux`.
  Borra la clave en `always()`. Artifacts: `appimage` (AppImage + `latest-linux.yml`), `appimage-base` y, en ensayo,
  `canal-linux`.
- `firmar` (release): baja `instalador` **y** `appimage` en `canal/`. Firma Windows como hoy y Linux con
  `armar-payload.mjs --producto tatana-linux --archivo … --latest canal/latest-linux.yml` →
  `firmar-manifiesto.mjs` → `canal/tatana-update-linux.json` → `verificar-manifiesto.mjs --producto tatana-linux`.
  `SHA256SUMS` cubre los **7** archivos.
- **`prueba-linux`** (nuevo, ubuntu-24.04): mismas condiciones `if` que `prueba`. Usa `canal` en release y
  `canal-linux` en ensayo, más `appimage-base` y `payload-linux`. Corre `ops/tatana/prueba-linux.sh` (§9.4).
- `publicar`: `needs: [preparar, prueba, prueba-linux]`. Verifica los dos manifiestos; el tar lleva los **8**
  archivos de §10.D. El chequeo externo compara `tatana-update-linux.json` y el `Content-Length` del AppImage en cada
  URL, y la GitHub Release adjunta además el AppImage, `latest-linux.yml` y `tatana-update-linux.json`.

### 9.3 `ops/tatana/` y `deploy/cloud/`

- `_lib.mjs`: `payloadError(p, product = 'tatana-windows')` acepta los dos productos, con su regla de `file`
  (§10.C). `armar-payload.mjs`: `--producto` (default `tatana-windows`) y `--archivo`, con `--exe` como alias que
  se sigue aceptando. `verificar-manifiesto.mjs`: `--producto` y `--archivo`. `probar-firma.mjs`: los casos de Linux
  y los cruzados.
- `publicar-tatana-forzado.sh`:
  - `ESPERADOS` = los 8 de §10.D y `HASHEADOS` = los 7. Valida `latest-linux.yml` (`version:` y `path`/`- url:` =
    AppImage).
  - Publica en este orden: binarios (exe, blockmap y AppImage), copias de descarga (`Tatana-Setup-Windows.exe`,
    `Tatana-Linux-x86_64.AppImage` y sus `.sha256`), `index.html` (D-T27), `latest.yml`, `latest-linux.yml`,
    `tatana-update-linux.json`, `tatana-update.json` y `VERSION`.
  - Retención: los 5 AppImage más nuevos, con el mismo criterio que los exe.
  - `probar-publicar-forzado.sh` se actualiza (8 archivos, falta un archivo de Linux → 4, hash de Linux mal → 5,
    `index.html` generado).
- `Caddyfile`: `@manifiestos path /latest.yml /tatana-update.json /latest-linux.yml /tatana-update-linux.json`.

### 9.4 `ops/tatana/prueba-linux.sh` (prueba de actualización en CI)

**No destructiva:** `HOME`, `XDG_CONFIG_HOME`, `XDG_DATA_HOME` y `XDG_RUNTIME_DIR` apuntan a `$RUNNER_TEMP/home`. Se
niega a correr fuera de CI salvo `--forzar-fuera-de-ci`. Lanza con `xvfb-run -a` y FUSE, con el mismo fallback que
§9.1 (e). Sirve el canal con `python3 -m http.server 8099` y `TATANA_UPDATE_URLS=http://127.0.0.1:8099/`.
Recorre, inspirada en `prueba-windows.ps1`:
1. Base 0.0.1 copiada a `$HOME/Applications/Tatana.AppImage`. Se siembran evidencia en
   `$XDG_DATA_HOME/Tatana/data/cases/<guid>/`, un ZIP en `$HOME/Factum/Evidencia/`, `appsettings.Local.json` y un
   `preparacion-linux.json` falso, y se guardan sus hashes.
2. Arranque: `/health.version == 0.0.1` y el `.desktop` y el autoarranque escritos con la ruta canónica.
3. Manifiesto con la firma alterada → `update-state.json` en `rejected/firma_invalida`, sin descarga.
4. Manifiesto de **Windows** en `tatana-update-linux.json` → `rejected/manifiesto_invalido`.
5. Canal válido con una operación en curso (mismo recurso que usa la prueba de Windows) → no instala (`busy`).
6. Sin la operación → `ready` → `--install-update` → relanzado: `/health.version == X.Y.Z`, el SHA-256 del archivo
   canónico es el del AppImage del canal, `update-state` pasa a `up_to_date` y evidencia, ZIP, config y
   `preparacion-linux.json` siguen intactos (hashes).
7. Sin repetición (vuelve a chequear → `up_to_date`) y sin red (canal apagado → `offline`).
8. "Salir": ningún proceso de Tatana vivo (D-T17).
Escribe un resumen en `$GITHUB_STEP_SUMMARY`. Los logs se suben `if: always()`.

### 9.5 `verificar.yml`
Suma `bash -n packaging/linux/preparar-pc.sh`, `bash packaging/linux/tests/probar-preparar-pc.sh` y, en el job de
TypeScript, los tests nuevos de agent-ui (`npm test` ya corre). Si está disponible, `shellcheck` sobre
`preparar-pc.sh`, `armar-tatana-linux.sh`, `prueba-humo.sh` y `prueba-linux.sh` (el runner de Ubuntu lo trae).

---

## 10. Contrato compartido

El agente serializa en **snake_case_lower** (`JsonNamingPolicy.SnakeCaseLower`, `Factum.Agent/Program.cs`). Los
`null` no viajan (`WhenWritingNull`).

### 10.A `/health` (Tatana → client y agent-ui)

| Campo JSON | Tipo | Valores | C# (`server/src/Factum.Agent`) | TS client (`client/src/lib/agent.ts`) | TS agent-ui (`agent-ui/src/main/agent-api.ts` + `src/renderer/src/env.d.ts`) |
|---|---|---|---|---|---|
| `os` | string | `"windows"` \| `"macos"` \| `"linux"` | `HealthController.Health` (`HostOsInfo.JsonName`, `Common/HostOs.cs`) | `AgentHealth.os?: AgentOs` (`type AgentOs`) | `AgentHealth.os` |
| `linux` | objeto \| ausente | solo en Linux | `Services/Linux/LinuxHostProbe.cs` → `record LinuxHostStatus` | `AgentHealth.linux?: AgentLinuxStatus` (lo declara; la web no lo usa) | `AgentLinuxStatus` (lo usa "Esta PC") |
| `linux.distro_id` | string | `ID` de os-release, en minúsculas (`ubuntu`, `debian`, `linuxmint`, `fedora`…) | `LinuxHostStatus.DistroId` | `distro_id: string` | ídem |
| `linux.distro_version` | string? | `VERSION_ID` (`"24.04"`) | `DistroVersion` | `distro_version?: string` | ídem |
| `linux.distro_name` | string? | `PRETTY_NAME` | `DistroName` | `distro_name?: string` | ídem |
| `linux.distro_like` | string? | `ID_LIKE` (`"debian"`, `"ubuntu debian"`) | `DistroLike` | `distro_like?: string` | ídem |
| `linux.glibc_version` | string? | `"2.39"` | `[JsonPropertyName("glibc_version")] GlibcVersion` | `glibc_version?: string` | ídem |
| `linux.package_manager` | string | `"apt"` \| `"dnf"` \| `"pacman"` \| `"zypper"` \| `"unknown"` | `PackageManager` | `package_manager: …` | ídem |
| `linux.setup_supported` | bool | `apt`/`dnf` → true | `SetupSupported` | `setup_supported: boolean` | ídem |
| `linux.setup_state` | string | `"ok"` \| `"incomplete"` | `SetupState` | `setup_state: "ok" \| "incomplete"` | ídem |
| `linux.checks` | array | uno por `id` (siempre los 7, en este orden) | `IReadOnlyList<LinuxCheck> Checks` | `checks: AgentLinuxCheck[]` | ídem |
| `linux.checks[].id` | string | `usbmuxd`, `android_usb`, `avahi`, `uxplay`, `camera`, `microphone`, `sandbox` | `LinuxCheck.Id` | `id: AgentLinuxCheckId` | ídem |
| `linux.checks[].status` | string | `ok` \| `missing` \| `outdated` \| `warning` \| `not_applicable` | `LinuxCheck.Status` (string, no enum) | `status: …` | ídem |
| `linux.checks[].required` | bool | true solo `usbmuxd`, `android_usb` | `LinuxCheck.Required` | `required: boolean` | ídem |
| `linux.checks[].detail` | string? | castellano corto | `LinuxCheck.Detail` | `detail?: string` | ídem |
| `ios.airplay_unavailable_reason` | string? | **+** `"uxplay_without_mp4"`, `"avahi_not_running"` (además de `not_supported_on_windows`, `uxplay_not_found`) | `IosService.AirplayUnavailableReason` | `type AirplayUnavailableReason` (sumar los dos) | — |
| `ios.apple_service` | string | sin cambio de valores; en Linux, `ok` también con usbmuxd instalado sin socket (D-T12) | `AppleServiceProbe` | — | — |

Los nombres de las propiedades C# (`DistroId` → `distro_id`, etc.) salen bien con la política. `GlibcVersion` lleva
`JsonPropertyName` explícito, igual que `pymobiledevice3_version`, para no depender de cómo se parte un nombre.

### 10.B agent-ui ↔ `packaging/linux/preparar-pc.sh`

| Qué | Valor exacto |
|---|---|
| Import en agent-ui | `import preparePcScript from '../../../../packaging/linux/preparar-pc.sh?raw'` (desde `agent-ui/src/main/linux/prepare-pc.ts`) |
| Simulación | `bash -s -- --simular` (stdin = el script) |
| Ejecución | `pkexec /bin/bash -s -- --ejecutar --usuario <os.userInfo().username> --version <app.getVersion()>` (stdin = el script) |
| Fallback | archivo `~/.config/Tatana/preparar-pc.sh` (0644) + `sudo bash ~/.config/Tatana/preparar-pc.sh --ejecutar --usuario <login>` |
| Líneas | `TATANA_PLAN <json>`, `TATANA_PASO <json>`, `TATANA_RESULTADO <json>` (prefijo + un espacio + JSON de una línea) |
| `id` de pasos | `paquetes`, `reglas_usb`, `airplay`, `camara_microfono`, `sandbox` |
| `estado` | `en_curso`, `ok`, `error`, `omitido` |
| `resultado` | `ok`, `parcial`, `sin_soporte`, `sin_internet` (los de agent-ui: + `cancelado`, `sin_polkit`, `error`) |
| Códigos de salida | 0 ok · 1 parcial · 2 argumentos · 3 sin soporte · 4 sin internet · 5 sin root · (pkexec) 126 cancelado · 127 sin polkit |
| Archivos que escribe (root) | `/etc/udev/rules.d/70-factum-tatana.rules`, `/etc/apparmor.d/factum-tatana` |

### 10.C Canal de actualización de Linux (CI ↔ agent-ui)

| Qué | Windows (sin cambios) | Linux (nuevo) |
|---|---|---|
| Manifiesto | `tatana-update.json` | `tatana-update-linux.json` |
| Sobre | `{ schema: 1, key_id, payload, signature }` | idéntico, misma clave (`trusted-keys.ts`) |
| `payload.product` | `"tatana-windows"` | `"tatana-linux"` |
| `payload.file` | `Tatana-Setup-${version}.exe` | `Tatana-${version}-x86_64.AppImage` |
| Resto del payload | `schema, version, released_at, commit, size, sha512 (base64), sha256 (hex), notes?` | igual |
| Archivo de electron-updater | `latest.yml` | `latest-linux.yml` |
| Validadores | `agent-ui/src/main/updater/manifest.ts` (`validatePayload`) ↔ `ops/tatana/_lib.mjs` (`payloadError`) | los dos, con el producto como parámetro |
| Config horneada | `<resources>/dist-config/tatana-dist.json` (`schema`, `client_url`, `update_urls`) | igual |

### 10.D Publicación (CI ↔ `publicar-tatana-forzado.sh`)

Tar por stdin con **exactamente** estos 8 archivos regulares: `Tatana-Setup-X.Y.Z.exe`,
`Tatana-Setup-X.Y.Z.exe.blockmap`, `latest.yml`, `tatana-update.json`, `Tatana-X.Y.Z-x86_64.AppImage`,
`latest-linux.yml`, `tatana-update-linux.json` y `SHA256SUMS` (este último cubre los otros 7). Salidas en
`/srv/factum/descargas/tatana/`: `Tatana-Setup-Windows.exe(.sha256)`, `Tatana-Linux-x86_64.AppImage(.sha256)` e
`index.html`. Códigos de salida sin cambios.

### 10.E Enlace de descarga en la web

| Lado | Nombre exacto |
|---|---|
| Variable de GitHub (Actions → Variables) | `FACTUM_TATANA_DESCARGA_URL_LINUX` (recomendado: `https://<dominio>/descargas/tatana/`) |
| `deploy/cloud/factum.env.example` / `.env` del VPS | `FACTUM_TATANA_DESCARGA_URL_LINUX=` |
| Build arg / env de Next | `NEXT_PUBLIC_TATANA_DOWNLOAD_URL_LINUX` |
| Archivos | `.github/workflows/desplegar-produccion.yml` (build-args), `deploy/cloud/scripts/desplegar.sh` (L98), `client/Dockerfile` (ARG + ENV), `client/next.config.ts` (`env`), `client/src/lib/agent-messages.ts` |
| Windows (sin cambios) | `FACTUM_TATANA_DESCARGA_URL` → `NEXT_PUBLIC_TATANA_DOWNLOAD_URL` |

### 10.F Archivos de agent-ui en la PC (Linux)

`~/.config/Tatana/preparacion-linux.json`:
```json
{ "schema": 1, "fecha": "2026-10-20T14:03:11Z", "tatana_version": "1.5.0", "distro": "ubuntu 24.04", "gestor": "apt",
  "resultado": "ok", "requiere_reiniciar_sesion": false, "script_sha256": "<64 hex>",
  "pasos": [ { "id": "paquetes", "estado": "ok", "detalle": "usbmuxd avahi-daemon" } ] }
```
`~/.config/Tatana/instalacion-linux.json`: `{ "schema": 1, "fecha", "version", "ruta": "<canónica>" }`.

### 10.G IPC main ↔ renderer (agent-ui, nuevos)

| Canal | Dirección | Payload |
|---|---|---|
| `tatana:get-linux-status` | invoke | → `{ health: AgentLinuxStatus \| null, preparation: PreparacionLinux \| null, installed: boolean, canonicalPath: string }` |
| `tatana:prepare-pc` | invoke | `()` → `{ resultado, requiereReiniciarSesion, pasos, plan?, fallbackCommand?, scriptSha256 }` |
| `tatana:prepare-pc-step` | main → renderer | `{ id, estado, detalle? }` |
| `tatana:simulate-prepare-pc` | invoke | → `TATANA_PLAN` parseado |
| `tatana:install-on-pc` | invoke | `()` → `{ ok: boolean; error?: string }` (si `ok`, la app se relanza) |
| `tatana:open-prepare-log` | invoke | abre `logs/preparacion-linux.log` |

### 10.H Backend
Sin cambios: el backend ya recibe `agent_version` y la identidad de la PC por la web. La distro no viaja al backend.

---

## 11. Plan de QA manual (PC del usuario con Ubuntu, Android + iPhone)

> La corre el usuario en su PC Ubuntu (22.04 o 24.04) con un AppImage de **ensayo** del PR (artifact `appimage` del
> job `armar` de `tatana-linux.yml`, o `appimage` + `appimage-base` + `canal-linux` de `tatana-release.yml` en
> ensayo). Anotá en cada paso OK / falla y lo que veas. Antes de empezar, si ya existen, respaldá
> `~/.config/Tatana`, `~/.local/share/Tatana` y `~/Factum`.

**0. Estado inicial (anotar)**
- `lsb_release -ds; ldd --version | head -1; uname -m` (tiene que decir `x86_64` y glibc ≥ 2.35).
- `dpkg -l usbmuxd avahi-daemon libfuse2 fuse3 uxplay 2>/dev/null | grep ^ii`
- `cat /proc/sys/kernel/apparmor_restrict_unprivileged_userns 2>/dev/null` (24.04: `1`).
- `ls /etc/udev/rules.d/ /etc/apparmor.d/factum-tatana 2>&1`
- `free -h; nproc; lscpu | grep 'Model name'` (para registrar si es la "PC de referencia mínima", D12).

**1. Descarga y primer arranque**
1.1 (Si ya hay publicación) Abrí `https://<dominio>/descargas/tatana/`: se ven los requisitos antes del enlace.
1.2 Bajá el AppImage y verificá: `sha256sum Tatana-*.AppImage` contra el `.sha256`.
1.3 En "Archivos": clic derecho → Propiedades → "Ejecutable como programa" → doble clic. **Sin instalar nada
    antes**, la ventana abre (sin libfuse2). En 24.04 no se cierra sola.
1.4 `ps aux | grep -c '[t]atana.*--no-sandbox'` (en 24.04 se espera 1 antes de preparar la PC).
1.5 Terminal (solo para verificar): `~/Descargas/Tatana-*.AppImage --appimage-extract >/dev/null &&
    squashfs-root/resources/agent/Factum.Agent --autoprueba-linux /tmp/al` → todo `OK`. Borrá `squashfs-root`.

**2. Instalar en esta PC**
2.1 Tocá "Instalar (recomendado)". Tatana se relanza; `ls -l ~/Applications/Tatana.AppImage`.
2.2 Buscá "Tatana" en el menú de aplicaciones: aparece con ícono y abre la misma ventana (instancia única).
2.3 `cat ~/.config/autostart/factum-tatana.desktop` → `Exec=".../Applications/Tatana.AppImage" --hidden`.
2.4 La tarjeta "Instalá Tatana" ya no aparece y la línea de actualización no dice `no_instalado`.

**3. Preparar esta PC**
3.1 Se abre "Esta PC" con el asistente. Tocá "Preparar esta PC": aparece **un único** pedido de contraseña.
3.2 Se ve el progreso por paso y al final la lista de chequeos. Comprobá en la terminal:
    `dpkg -l usbmuxd avahi-daemon | grep ^ii`, `cat /etc/udev/rules.d/70-factum-tatana.rules`,
    `systemctl is-active avahi-daemon` y `cat ~/.config/Tatana/preparacion-linux.json`.
3.3 Si dice que hay que cerrar sesión, cerrá y volvé a entrar. Anotá si pasó (con `uaccess` no debería).
3.4 (24.04) Salí de Tatana y abrilo desde el menú: `ps aux | grep -c '[t]atana.*--no-sandbox'` → se espera **0**
    (el perfil de AppArmor tomó, E9). Si da 1, anotalo: no bloquea, pero hay que ajustar el perfil.
3.5 Cerrá Tatana, abrilo de nuevo: **no** pide contraseña y "Chequeos de esta PC" sigue disponible.
3.6 Cancelar: `sudo rm /etc/udev/rules.d/70-factum-tatana.rules`, "Volver a revisar" → "Android por USB" en rojo
    → "Preparar esta PC" → cerrá el pedido de contraseña sin escribirla → mensaje "Sin la contraseña…" +
    "Reintentar". Reintentá con la contraseña → vuelve a quedar en verde.
3.7 Sin internet: desconectá la red, `sudo apt remove -y avahi-daemon`, "Preparar esta PC" → "Hace falta internet…".
    Reconectá y prepará de nuevo.

**4. Android (USB, sin sudo)**
4.1 Conectá el Android con depuración USB y aceptá la huella. En Factum (Chrome/Chromium/Firefox) abrí un caso → paso
    de captura: el Android aparece. `ps -o user=,args= -C adb` → tu usuario, no root.
4.2 Captura de pantalla → PNG en la evidencia.
4.3 Grabar 60 s → detener → el MP4 se reproduce entero (`ffprobe` o el reproductor).
4.4 Grabar 60 s "con micrófono de la PC" hablando → el MP4 tiene tu voz.

**5. iPhone**
5.1 iPhone (iOS 17+) desbloqueado, "Confiar" → aparece con nombre, modelo y versión, con acentos bien.
5.2 Captura → PNG. Modo Desarrollador: si está apagado, usá el botón de la guía y repetí.
5.3 Grabá 60 s en "Solo pantalla", "Pantalla + micrófono de PC" y "Grabación nativa" → los tres MP4 se generan.
    En ningún paso se pide la contraseña.
5.4 Desenchufá el iPhone: `curl -s localhost:8765/health | jq '.ios.apple_service, .linux.setup_state'` →
    `"ok"`, `"ok"`.
5.5 Falta usbmuxd: `sudo apt remove -y usbmuxd`, enchufá el iPhone → la web dice que corras "Preparar esta PC" (sin
    macOS, brew, Terminal ni iTunes). Después prepará la PC de nuevo y comprobá que el iPhone vuelve.
5.6 AirPlay: en "Modo de grabación iOS", "Espejo AirPlay" aparece deshabilitado con el motivo (en 22.04/24.04,
    uxplay < 1.73; ver DU2) y sin mencionar brew.

**6. Micrófono**
6.1 Configuración → Sonido → cambiá la entrada por defecto (por ejemplo, a un headset), grabá Android con mic y
    comprobá que el audio sale del dispositivo elegido.

**7. Sesión completa (D12, PC de referencia)**
7.1 Un caso con grabación de la sesión: 30 min de pantalla de Android + micrófono. Mientras tanto, navegá la web: tiene
    que seguir usable. Anotá `uptime` (carga) a los 15 min.
7.2 Al cerrar: los tramos se cierran sin huecos (salvo desconexiones reales) y el ZIP se arma.

**8. Ciclo de vida**
8.1 Reiniciá la PC y entrá: Tatana arranca solo, sin ventana, con ícono en la bandeja (Ubuntu trae AppIndicator).
    Abrilo desde la bandeja y desde el menú.
8.2 (Opcional, si tenés una sesión GNOME sin la extensión de AppIndicator) Cerrar la ventana la minimiza; desde el
    menú vuelve.
8.3 "Salir" desde la bandeja → `pgrep -af 'mount_Tatan|platform-tools/adb|scrcpy|tools/ffmpeg|python-embed'` → nada.
8.4 Evidencia: `ls ~/.local/share/Tatana/data/cases ~/Factum/Evidencia`. Borrá el AppImage de `~/Descargas` y
    también (temporalmente) mové `~/Applications/Tatana.AppImage` a otro lado: la evidencia sigue intacta. Volvé a
    dejarlo en su lugar.

**9. Actualización (canal local, con artifacts de ensayo del mismo run)**
9.1 Bajá `appimage-base` (0.0.1) y `canal-linux` del mismo run de `tatana-release.yml` (ensayo; los dos confían en
    la misma clave efímera). Copiá la base sobre `~/Applications/Tatana.AppImage`.
9.2 `cd canal-linux && python3 -m http.server 8099 &` y después
    `TATANA_UPDATE_URLS=http://127.0.0.1:8099/ ~/Applications/Tatana.AppImage`.
9.3 Esperá el aviso "Hay una versión nueva…". Con una grabación en curso, "Reiniciar y actualizar" no instala y lo
    explica. Detené la grabación → "Reiniciar y actualizar" → Tatana se relanza con la versión nueva **sin pedir
    contraseña**, avisa "se actualizó" y la evidencia, la config y `preparacion-linux.json` siguen igual (no vuelve
    a pedir "Preparar esta PC").
9.4 Restaurá el AppImage de ensayo o el publicado.

**10. Windows y macOS siguen igual** (según DU1)
10.1 En una PC Windows con el Tatana publicado hoy: que se actualice al instalador de ensayo con canal local (como la
     prueba de Windows) o, después del merge, con la primera release. Comprobá versión, evidencia y arranque.
10.2 macOS: no cambia el empaquetado. Alcanza con un humo (abrir, grabar un Android 10 s, mic) si el implementador
     tocó `MicCapture`/`ProcessStop`.

**Fedora (best effort):** no hay prueba con hardware. Solo CI (contenedor + `--simular` con `dnf`).

---

## 12. Guía (`docs/despliegue-nube.md`)

- §10.2: suma la variable `FACTUM_TATANA_DESCARGA_URL_LINUX` (valor recomendado `https://<dominio>/descargas/tatana/`).
- §19: la release arma y publica Windows **y** Linux. Si falla cualquier prueba, no se publica nada. El VPS necesita
  el `publicar-tatana-forzado.sh` nuevo, copiado a mano a `/srv/factum/bin/` (comando exacto, como en la guía de
  hoy).
- **§20 nueva, "Tatana en Linux":** requisitos (D-T1); descarga; cómo marcarlo ejecutable en GNOME, KDE y Cinnamon;
  "Instalar en esta PC"; "Preparar esta PC" (qué instala y por qué pide la contraseña); distro sin soporte (la lista
  para soporte técnico); si con doble clic no pasa nada (FUSE: `sudo apt install fuse3`, o probar
  `./Tatana-*.AppImage --appimage-extract-and-run`); sin bandeja (abrirlo desde el menú); AirPlay (uxplay ≥ 1.73);
  qué deja en la PC y cómo sacarlo (§7.4); carpetas (D-T5).

---

## 13. Pasos manuales del usuario (no los puede hacer un agente)

1. Responder DU1 y DU2 antes de implementar.
2. En GitHub: crear la variable `FACTUM_TATANA_DESCARGA_URL_LINUX`.
3. En el VPS, **antes de la primera release con Linux**: copiar el `publicar-tatana-forzado.sh` nuevo a
   `/srv/factum/bin/` (root:root 755) y desplegar el `Caddyfile` nuevo (deploy normal).
4. QA con hardware de §11.

---

## 14. Checklist atómico

### 14.1 Backend — `implementer-backend` (Tatana + empaquetado + CI + VPS)

**Agente**
- [ ] B1 `Common/HostOs.cs` (`HostOs`, `HostOsInfo.Current`, `JsonName`) + test.
- [ ] B2 `IosErrors.MessageFor/Create/FromHelper/RecordingEmptyFrom` con `HostOs` en lugar de `bool isWindows`;
      adaptar `IosHelper`, `IosService`, `DvtRecorder`, `IosSelfTest` y los tests existentes. Textos Linux de §5.4.
      `LowerFirst` con "Linux".
- [ ] B3 `AdbService`: texto Linux de adb no encontrado (L170, L917).
- [ ] B4 `AppleServiceProbe`: `usbmuxdInstalled` inyectable y regla de D-T12 + tests.
- [ ] B5 `Common/UnixSignals.cs` + uso en `ProcessStop` (solo Linux) e `IosService.GracefulStopAsync` (solo Linux) +
      tests.
- [ ] B6 `ToolSpec.UnixPortablePath` + `AgentTools.Python` con `"bin/python3"` + `ToolResolverTests`.
- [ ] B7 `MicCapture` Linux: `pulse` + `MicDevice` + fallback `alsa` (D-T14) + tests de argumentos.
- [ ] B8 `CameraArgs`/`CameraTrack`: audio de `v4l2` por `pulse` con fallback; `CameraDevices.ListLinux` con filtro
      de `index` (función pura) + tests.
- [ ] B9 `UxplayProbe` (caché de `uxplay -h`) + `IosService.AirplayAvailable/AirplayUnavailableReason` en Linux
      (D-T25) + `-p` solo en Linux + tests de la regla.
- [ ] B10 `Services/Linux/OsRelease.cs` + `LinuxHostProbe.cs` (§5.2) registrados en DI + tests con árbol falso.
- [ ] B11 `HealthController`: `os` y `linux` (§10.A) + test.
- [ ] B12 `VideoPostprocessOptions.MinProcessorCount` (D-T20) + `Normalize` + aplicación + tests.
- [ ] B13 `--autoprueba-linux` (`LinuxSelfTest`, §5.7) en `Program.cs` antes del host.
- [ ] B14 `dotnet build server/src/Factum.Agent/Factum.Agent.csproj` y `dotnet test server/tests/Factum.Agent.Tests`
      en verde.

**Empaquetado**
- [ ] B15 `packaging/linux/ios-linux/{requirements.in,generar-lock.py}` + `requirements-linux.lock` generado (con
      constraints del lock de Windows) + `--check` en verde.
- [ ] B16 `packaging/linux/glibc-maximo.py`.
- [ ] B17 `deploy/linux/armar-tatana-linux.sh` (§6.1) con las versiones y hashes de §4 + avisos de terceros.
      Correrlo en Linux (contenedor o CI) y comprobar el tar.
- [ ] B18 `packaging/linux/preparar-pc.sh` (§7) con `--simular`/`--ejecutar`, protocolo y códigos de §10.B.
- [ ] B19 `packaging/linux/tests/probar-preparar-pc.sh` (fixtures de os-release: Ubuntu 22.04/24.04, Debian 12,
      Fedora, Mint, Arch).

**CI / release / VPS**
- [ ] B20 `ci/tatana-linux/prueba-humo.sh` + `.github/workflows/tatana-linux.yml` (§9.1: `armar`, `humo`, `distros`).
- [ ] B21 `ops/tatana/_lib.mjs`, `armar-payload.mjs` y `verificar-manifiesto.mjs` con `--producto`/`--archivo` +
      `probar-firma.mjs` (casos de Linux y cruzados).
- [ ] B22 `tatana-release.yml` (§9.2): paths, chequeos de `preparar`, `payload` Linux, jobs `appimage` y
      `prueba-linux`, `firmar` con los dos manifiestos y `publicar` con los 8 archivos.
- [ ] B23 `ops/tatana/prueba-linux.sh` (§9.4).
- [ ] B24 `deploy/cloud/scripts/publicar-tatana-forzado.sh` (8 archivos, orden, retención de AppImage,
      `index.html`) + `ops/tatana/probar-publicar-forzado.sh`.
- [ ] B25 `deploy/cloud/Caddyfile` (manifiestos de Linux sin caché).
- [ ] B26 Variable de descarga de Linux: `desplegar-produccion.yml`, `deploy/cloud/scripts/desplegar.sh`,
      `deploy/cloud/factum.env.example` (§10.E).
- [ ] B27 `verificar.yml` (§9.5).
- [ ] B28 `docs/despliegue-nube.md` (§12).
- [ ] B29 Escribir `progress/impl_backend_tatana-linux.md`.

### 14.2 Frontend — `implementer-frontend` (app: **`agent-ui/`**, y **`client/`** en F15–F17)

- [ ] F1 `package.json` según DU1 (por default electron-builder 26.15.3 exacto, `toolsets`, `linux` y `appImage` de
      D-T3; sin `postinstall` ni `scripts/parchar-electron-builder.mjs`); `npm install` y `package-lock.json`.
- [ ] F2 `src/main/linux/paths.ts` + test (XDG y `HOME` con espacios).
- [ ] F3 `src/main/linux/requirements.ts` + chequeo antes de `ready` (D-T2) + test.
- [ ] F4 `src/main/linux/desktop-integration.ts` (`desktopEntryText` puro + test, `installOnPc`,
      `refreshDesktopEntries`, `applyAutostart`) (D-T6, D-T18, §8.4).
- [ ] F5 `src/main/linux/process-sweep.ts` (`selectOrphans` puro + test) y `agent-process.ts` (`detached`,
      `stopAndWait` y `stop` en Linux; `agentDataDir` XDG) (D-T5, D-T17).
- [ ] F6 `src/main/linux/prepare-pc.ts` (`?raw`, `simulate`, `execute` con `pkexec` por stdin, fallback,
      `parsePrepareLine` puro + test, `preparacion-linux.json`, log) (D-T10, §10.B, §10.F).
- [ ] F7 `src/main/linux/tray-support.ts` + `index.ts` (cerrar → minimizar sin bandeja; vista "Esta PC" la primera
      vez; notificación con `--hidden`; oferta de instalación).
- [ ] F8 `config.ts`: `applyLoginItem` en Linux → autoarranque XDG.
- [ ] F9 `updater/manifest.ts` (producto por plataforma) + `manifest.test.ts` (Linux válido, cruzados, `file`).
- [ ] F10 `updater/appimage-install.ts` (D-T7) + `update-manager.ts` (manifiesto de Linux, `.AppImage`, motivos
      `no_instalado`/`sin_appimage`, `installNow` en Linux) + `state.ts` + espejo del renderer.
- [ ] F11 `agent-api.ts` (`getHealth`, tipos de §10.A), `app-info.ts` (`platform`, `packaged`, `linux`).
- [ ] F12 `ipc.ts` + `preload/index.ts` + `renderer/src/env.d.ts` (canales de §10.G; Librerías apagada en Linux
      empaquetado).
- [ ] F13 Renderer: vista "Esta PC" (tarjeta de instalación, asistente, chequeos, estados de error, aviso de cerrar
      sesión), Sidebar (Librerías oculta en Linux empaquetado), `UpdateStatus` (textos nuevos), Settings
      (autoarranque deshabilitado sin instalar).
- [ ] F14 `npm test`, `npx tsc --noEmit -p tsconfig.web.json`, `npx tsc --noEmit -p tsconfig.node.json` y
      `npx electron-vite build` en verde. Si hay Docker con amd64 o una PC Linux,
      `npx electron-builder --linux AppImage --x64 --publish never` con un `resources/agent` de prueba.
- [ ] F15 `client/src/lib/agent.ts`: `AgentOs`, `AgentHealth.os`, `AgentLinuxStatus`/`AgentLinuxCheck` y
      `AirplayUnavailableReason` con `uxplay_without_mp4` y `avahi_not_running`.
- [ ] F16 `client/src/lib/agent-messages.ts`: `tatanaDownloadUrl()` según el SO del navegador (Linux sin Android →
      `NEXT_PUBLIC_TATANA_DOWNLOAD_URL_LINUX`; si no, la de Windows). La usan `agentMinVersionMessage` y
      `origin_not_allowed`. Además `iosAppleServiceMissingMessage(os)` (Linux: `Para que Factum vea el iPhone, abrí
      Tatana, tocá "Preparar esta PC" y volvé a conectar el iPhone.`; Windows/macOS: el texto de hoy) y
      `airplayUnavailableText` (`uxplay_without_mp4`: `La versión de uxplay de esta PC no puede grabar (hace falta
      la 1.73 o posterior).`; `avahi_not_running`: `El servicio avahi no está activo en esta PC: abrí Tatana y tocá
      "Preparar esta PC".`).
- [ ] F17 `client`: `useAgentIosStatus` devuelve `os`. `DeviceConnect` usa el mensaje por SO y, en el bloque "Tatana
      no está activo", muestra "Descargar Tatana para <SO>" con un enlace secundario al otro SO, solo si hay URL
      configurada. `next.config.ts` y `Dockerfile` con `NEXT_PUBLIC_TATANA_DOWNLOAD_URL_LINUX`. `npx tsc --noEmit`
      en verde.
- [ ] F18 Escribir `progress/impl_frontend_tatana-linux.md` (incluida la constancia de los skills obligatorios).

**Orden:** B1–B14 y F2–F13 pueden ir en paralelo: el contrato de §10 está fijo. F6 necesita que exista
`packaging/linux/preparar-pc.sh` (B18) para compilar el `?raw`. Si no está, F6 usa un stub con el mismo protocolo
y el implementador lo deja dicho. B20/B22 necesitan F1 (electron-builder 26).

---

## 15. Verificación

### 15.1 `implementer-backend` (antes de `done`)
```bash
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
dotnet test server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj
python3 -m py_compile server/src/Factum.Agent/Services/Ios/ios_helper.py
python3 -I packaging/linux/ios-linux/generar-lock.py --check
python3 -I packaging/portable/ios-win/generar-lock.py --check      # el de Windows no se rompió
bash -n packaging/linux/preparar-pc.sh && bash packaging/linux/tests/probar-preparar-pc.sh
bash ops/tatana/probar-publicar-forzado.sh
node ops/tatana/probar-firma.mjs
# Armado real en Linux x86-64 (Docker en la Mac: --platform linux/amd64, o el CI del PR):
bash deploy/linux/armar-tatana-linux.sh --version 0.0.0-ci --src <git archive> --salida /tmp/out
#   y dentro de ubuntu:22.04 / debian:12: extraer el tar y correr Factum.Agent --autoprueba-linux y --autoprueba-ios
```
El workflow `tatana-linux.yml` del PR tiene que quedar en verde (Fedora puede fallar: best effort), lo mismo que
`tatana-release.yml` en ensayo.

### 15.2 `implementer-frontend` (antes de `done`)
```bash
cd agent-ui && npm test && npx tsc --noEmit -p tsconfig.web.json && npx tsc --noEmit -p tsconfig.node.json && npx electron-vite build
cd client && npx tsc --noEmit
```

### 15.3 Lo que queda para el usuario
El plan de §11 en su PC Ubuntu (Android + iPhone) y, según DU1, la actualización de un Windows real al instalador
armado con electron-builder 26.

---

## 16. Riesgos y notas

- **Runtime estático "beta"** (E8): si una corrida del CI muestra un problema del toolset 1.0.3, la alternativa es
  1.0.2 (mismo runtime). El legacy (`0.0.0`) **no** sirve, porque vuelve a pedir libfuse2.
- **FUSE ausente**: el AppImage no arranca con doble clic y no se puede mostrar un mensaje propio. Queda
  documentado (§12) y el CI prueba el modo extraído.
- **AppArmor**: si el perfil por ruta no toma (paso 3.4 de la QA), Tatana sigue andando con `--no-sandbox` (ventana
  local, `sandbox: false` de hoy). Se ajusta en un seguimiento.
- **uxplay**: con 1.73 recién salida, la matriz casi no lo tiene (DU2).
- **Electron 33** ya no tiene soporte de upstream. No es parte de esta HU; si se actualiza, se vuelve a medir la
  glibc (E2) y se corre `glibc-maximo.py`.
- **Windows sin hashes fijos**: `armar-tatana-portable.sh` baja adb y ffmpeg "latest" sin SHA-256. No se toca en esta
  HU; Linux sí los fija. Queda anotado para una tarea aparte.

---

## 17. Decisiones pendientes (necesitan al usuario)

**DU1. electron-builder 26 para Windows también, o solo para Linux.** El AppImage sin libfuse2 exige electron-builder
26 (E8). Hoy Windows se arma con la 24.13.3 y un parche propio.
- **a) (recomendada)** Pasar todo `agent-ui` a `electron-builder@26.15.3` exacto y borrar el parche (el arreglo de
  upstream ya viene desde 26.12.0). Una sola herramienta y una sola versión de Tatana (D9). El instalador NSIS nuevo
  se prueba con la prueba de Windows de siempre en ensayo, y se suma un paso manual: actualizar un Windows con el
  Tatana publicado hoy al instalador nuevo (§11, 10.1). Riesgo: un cambio de plantillas NSIS entre la 24 y la 26 (por
  ejemplo, `FOLDERID_UserProgramFiles`, que el parche ya anticipa y que por defecto es la misma carpeta).
- b) Windows sigue en la 24 y Linux usa la 26 con un alias de npm (`"electron-builder-26": "npm:electron-builder@26.15.3"`)
  y un archivo de config solo para Linux. No toca Windows, pero deja dos versiones de la misma herramienta y config
  duplicada.

**DU2. AirPlay en Linux casi no va a estar disponible.** La opción `-mp4` con la que Tatana graba AirPlay existe
recién desde **uxplay 1.73 (enero 2026)** (E10). Ubuntu 22.04/24.04, Debian 12 y Mint traen versiones anteriores,
así que en casi toda la matriz "Espejo AirPlay" va a aparecer deshabilitado con el motivo. Los modos por USB (Solo
pantalla, con micrófono, grabación nativa) andan igual. La HU suponía que Ubuntu 24.04 lo tendría.
- **a) (recomendada)** Aceptarlo como prevé D5 b), con el motivo `uxplay_without_mp4` y la guía, y abrir una HU
  aparte en el backlog para empaquetar uxplay ≥ 1.73 si los peritos lo piden.
- b) Sumar a esta HU la compilación de uxplay 1.73 en CI y meterlo en el AppImage (D5 c): GStreamer completo (cientos
  de MB), plugins por hardware y obligaciones GPL/LGPL. Agranda bastante la HU.

### Respuesta del usuario (2026-10-18)

- **DU1 = a):** todo `agent-ui` pasa a `electron-builder@26.15.3` (Windows incluido) y se borra el parche NSIS.
- **DU2 = a):** AirPlay sale deshabilitado con `uxplay_without_mp4` donde el uxplay de la distro sea < 1.73;
  empaquetar uxplay queda para una HU aparte si los peritos lo piden.

### Corrección post-CI (2026-10-19, reintento 2)

- **D-T6 / D-T7:** `app.relaunch({ execPath })` no relanza desde un AppImage (el helper `--type=relauncher` corre
  desde el montaje FUSE, que se desmonta al salir el proceso viejo). Se relanza con `src/main/linux/relaunch.ts`: un
  `/bin/sh` despegado (`setsid`) que espera la salida del pid viejo (tope 30 s) y hace `exec` de la canónica;
  `app.relaunch` queda como respaldo. Aplica a la actualización, "Instalar en esta PC" y "Abrir la instalada".
- **D-T13:** `--quit` sin otra instancia corriendo sale sin arrancar la app.
