# impl_backend — Tatana en Docker (HU #94, SDD instalacion-fiscalia §7)

Estado: **done** (verificado en la Mac sin USB; lo que necesita celular real queda pendiente en la PC Ubuntu, ver abajo).
Rama `feat/instalacion-fiscalia`, sin commit ni push.

## Archivos (todos nuevos)

| Archivo | Qué es |
|---|---|
| `deploy/tatana-docker/armar-tatana-docker.sh` | Arma `Tatana-Docker-vX.Y.Z.tar.gz` (+ `.sha256`): agente → imagen `linux/amd64` → autopruebas dentro de la imagen → `docker save \| gzip` + scripts + `IMAGEN.txt` + `VERSION` + `SHA256SUMS`. Opciones `--ref`, `--agente-tar`, `--imagen`. |
| `deploy/tatana-docker/imagen/Dockerfile` | `ubuntu:24.04` fijada por digest; `usbmuxd libicu74 libssl3t64 ca-certificates tzdata tini curl`; agente en `/opt/tatana`; HEALTHCHECK a `/health`; ENTRYPOINT `tini -g`. |
| `deploy/tatana-docker/imagen/entrypoint.sh` | Como root levanta `usbmuxd -f` supervisado (se relanza si cae); después `setpriv` al uid/gid del host (+ sus grupos), `--no-new-privs`, `--inh-caps=-all`, y ejecuta `Factum.Agent` con los args del contenedor. Rechaza uid 0. Las autopruebas no levantan usbmuxd. |
| `deploy/tatana-docker/paquete/instalar-tatana-docker.sh` | `[--origen <url>]... [--puerto N] [--si] [--mock] [--red-con-puertos]`. Ver §"Cómo instala". |
| `deploy/tatana-docker/paquete/estado-tatana-docker.sh` | Contenedor, chequeo de Docker, reinicios, `/health`, orígenes, socket de usbmuxd, usbmuxd del host compitiendo, USB visibles, `adb devices`, registro. |
| `deploy/tatana-docker/paquete/desinstalar-tatana-docker.sh` | Borra el contenedor (solo si tiene la etiqueta `factum.tatana-docker=1`), `--borrar-imagen` opcional, ofrece `systemctl unmask usbmuxd` si la instalación lo había enmascarado. Nunca borra evidencia ni datos. |
| `deploy/tatana-docker/paquete/diagnostico-distro.sh` | Standalone: arquitectura, distro, kernel, glibc/musl, RAM, disco, systemd, FUSE 3, escritorio, usbmuxd, Docker (grupo, rootless), cgroups → recomienda **nativo (AppImage)** o **Docker** (o ninguna si no es x86_64). |
| `deploy/tatana-docker/paquete/comun.sh` | Rutas, `confirmar`, `detectar_docker` (`docker` o `sudo docker`), `leer_instalacion` (sin `source`), `usbmuxd_del_host_activo`, `health` (vía `docker exec curl`, el host no necesita curl). |
| `deploy/tatana-docker/paquete/LEEME.txt` | Guía corta del paquete. |
| `docs/tatana-docker.md` | Guía completa: nativo vs Docker, armado, instalación, cómo corre el contenedor, problemas, limitaciones, desinstalación, verificación y pendientes. |

No toqué `deploy/fiscalia/`, `docs/instalacion-fiscalia.md`, `.gitignore`, `server/src`, `client/src`, `agent-ui/src`
ni `deploy/linux/armar-tatana-linux.sh` (se **reusa tal cual**: no hizo falta extraer nada; `git status` lo confirma).

## Decisiones no obvias

- **Reuso del agente:** `armar-tatana-docker.sh` llama a `deploy/linux/armar-tatana-linux.sh` con un `git archive` de
  `--ref` (igual que el CI) o reusa un tar ya armado con `--agente-tar` (para que el paquete de la fiscalía no
  descargue dos veces). La imagen lleva exactamente el payload del AppImage (versiones fijas + SHA-256 de ahí).
- **Sin `--privileged`:** `-v /dev/bus/usb:/dev/bus/usb` + `--device-cgroup-rule 'c 189:* rmw'` (solo USB) +
  `-v /run/udev:/run/udev:ro` (hotplug de libusb para usbmuxd). `--network host` como pide la SDD (localhost:8765 del
  host + uevents). `--security-opt label=disable` por SELinux (Fedora/RHEL), inocuo en el resto.
- **Permiso de adb al Android:** el agente corre con el uid del perito, así que el permiso es el mismo que en nativo:
  la regla udev `70-factum-tatana.rules` con `TAG+="uaccess"` (mismo contenido exacto que `preparar-pc.sh`), que el
  instalador escribe en el host **con confirmación** si falta. El ACL de uaccess vale adentro (mismo nodo, mismo uid).
- **Chequeo `android_usb` de /health:** el agente busca la regla en su propio FS; el instalador monta de solo lectura
  las reglas del host que existan (`70-factum-tatana.rules`, `51-android.rules`) para que la web no diga "falta el
  permiso" cuando el host sí lo tiene.
- **usbmuxd del host:** si está activo, aviso + `systemctl stop` y `mask` **solo con confirmación** (o `--si`); marca
  en `~/.local/share/tatana-docker/usbmuxd-del-host-enmascarado` para que `desinstalar` ofrezca `unmask`.
- **Rutas iguales adentro y afuera:** datos (`~/.local/share/tatana-docker/{agent-data,home,lockdown}`) y evidencia
  (`~/Factum/Evidencia`, vía `Agent__EvidenceDirectory`) se montan en la misma ruta absoluta, así las rutas que
  reporta el agente son las del host. `lockdown/` persiste los pareos de usbmuxd.
- **Config:** `appsettings.Local.json` montado `:ro` en `/config` y pasado con `--local-config` (como el AppImage).
  Los orígenes se escriben con el mismo `deploy/windows/hornear-origenes-tatana.py` (copiado en la imagen y corrido con
  el Python portátil de la imagen, como el uid del perito): el host no necesita Python, la validación es la misma, y
  se conservan las demás claves del archivo. Sin `--origen` = plan B (solo localhost:3000/127.0.0.1:3000).
- **Hora:** `/etc/localtime:ro` + `TZ` del host (timedatectl o /etc/timezone).
- **`docker run`, no compose:** depende solo del Engine (el plugin compose no hace falta). `--restart unless-stopped`.
- **`--red-con-puertos`** (implica `--mock`): solo para probar en Docker Desktop (Mac/Windows), publica en
  `127.0.0.1:P` con `--bind 0.0.0.0` adentro. Documentado como "no usar en la fiscalía".
- `TATANA_DOCKER_CONTENEDOR/DATOS/EVIDENCIA`: variables para probar sin pisar una instalación real (las usé en la Mac).
- El modo que reporta `/health` es `portable` (heurística de `tools/` al lado del exe); no paso `--mode installed`
  porque eso es del Electron. Solo afecta la etiqueta del timeline.

## Verificación (Mac, Docker Desktop 29.4.1 arm64, build amd64 por Rosetta)

- `bash -n` de todos los scripts y `sh -n entrypoint.sh`: OK. `shellcheck -x` (contenedor `koalaman/shellcheck:stable`):
  sin warnings; queda 1 nota de estilo SC2001 en `estado-tatana-docker.sh:66`.
- `armar-tatana-docker.sh --version 0.0.0-docker-prueba --imagen tatana-docker-prueba` de punta a punta (16 min, casi
  todo descargas; un primer intento cortó por `curl: (18) Transferred a partial file` de la red, el reintento pasó):
  ```
  OK  linux/amd64, 290457654 bytes sin comprimir
  --autoprueba-linux: adb/scrcpy/ffmpeg/python portátiles, ffmpeg con pulse y v4l2, grabación lavfi cortada con
    SIGTERM, MKV legible 5.5 s, kill(2) → 143, glibc 2.39 (mínimo 2.35) → RESULTADO: OK
  --autoprueba-ios: Python + pymobiledevice3 10.7.4, ffmpeg, imports, helper, grabación sintética, MP4 legible,
    captura sin iPhone → ios_apple_service_missing (esperado), servicio de Apple ok → RESULTADO: OK
  OK  Tatana-Docker-v0.0.0-docker-prueba.tar.gz (289M)
  OK  imagen comprimida: 289M
  ```
  **Tamaño:** imagen ≈ 290 MB (lo que reporta Docker), `docker save | gzip` 289 MB; el paquete completo 289 MB.
- `instalar-tatana-docker.sh --red-con-puertos --puerto 18765 --si --origen https://factum.internal` (contenedor
  `tatana-docker-prueba`, datos/evidencia en el scratchpad): SHA-256 OK, `docker load`, orígenes horneados, `/health`
  en ~1 min (emulación). Desde el host: `curl localhost:18765/health` → `version 0.0.0-docker-prueba, mock true,
  os linux`; `Origin: https://factum.internal` → 200, origen ajeno → 403, preflight con `Access-Control-Allow-Origin`.
  Agente corriendo como uid 501 (el del host); Docker lo marcó `healthy`.
- Reinstalación como plan B (sin `--origen`): recrea el contenedor, la config queda solo con localhost →
  `factum.internal` 403, `localhost:3000` 200. `--origen https://x.com/ruta` → rechazado antes de tocar nada.
- usbmuxd + bajada de privilegios (contenedor aparte `tatana-docker-prueba-usbmuxd`, `TATANA_USBMUXD=1`):
  `srw-rw-rw- root /run/usbmuxd`; `usbmuxd -f` como root; agente `Uid 501`, `Groups 12 20 61`, `CapEff 0`,
  `NoNewPrivs 1`. `docker stop` → apagado limpio en 0,5 s, exit 0.
- `estado-tatana-docker.sh` y `desinstalar-tatana-docker.sh --borrar-imagen` (con la rama de "volver a habilitar
  usbmuxd" ejercitada sin terminal → no hace nada): OK; datos y evidencia quedan.
- `diagnostico-distro.sh` en Ubuntu 24.04 (glibc 2.39) y Ubuntu 20.04 (glibc 2.31) en contenedores: recomienda
  Docker con los motivos correctos (en 20.04 nombra la glibc vieja).
- Bug encontrado y corregido en la prueba: expansión de arreglo vacío con `set -u` en bash < 4.4.

**Limpieza:** borrados los contenedores `tatana-docker-prueba*`, la imagen `tatana-docker-prueba:0.0.0-docker-prueba`
y `ubuntu:20.04` (la bajé yo). Quedan `ubuntu:24.04` (base de la imagen, puede haber estado antes) y
`koalaman/shellcheck:stable`, más la caché de build de Docker. No toqué ningún otro contenedor ni el puerto 8765.
Los artefactos de prueba están en el scratchpad de la sesión, fuera del repo.

## Pendiente con celular real (PC Ubuntu del usuario) — no se puede en la Mac

1. `./diagnostico-distro.sh` en la PC: recomienda lo esperado.
2. `./instalar-tatana-docker.sh` (sin `--mock`): `/health` con `mock: false`, `linux.setup_state` y `android_usb ok`
   (regla montada), `estado` con usbmuxd OK y USB visibles.
3. **Android** enchufado después de instalar: aparece en la web; adquisición + ZIP en `~/Factum/Evidencia` con dueño
   el perito (`ls -l`). Confirmar que el ACL `uaccess` alcanza dentro del contenedor (si no, plan B de la guía:
   regla con `GROUP="plugdev"` y reinstalar).
4. **iPhone**: "Confiar", aparece en la web; desenchufar/enchufar sin reiniciar el contenedor (hotplug vía
   `/run/udev`); pareo que sobrevive a `docker restart tatana-docker`.
5. Con un `usbmuxd` del host activo: el instalador avisa, lo enmascara solo si se confirma; `desinstalar` lo devuelve.
6. Reiniciar la PC: Tatana vuelve solo (`--restart unless-stopped` + docker habilitado).
7. Plan A: la web en `https://factum.internal` habla con Tatana; plan B: `http://localhost:3000`.
8. Grabación de pantalla Android (scrcpy `--no-window`) y de iPhone dentro del contenedor, con un caso real.

## Limitaciones conocidas (documentadas en `docs/tatana-docker.md`)

- Sin ventana de Tatana ni "Preparar esta PC" (`/health` reporta `setup_supported: true` porque el agente ve un Ubuntu;
  avahi/uxplay/micrófono aparecen como faltantes opcionales). Si la web ofreciera el botón de preparar, no aplica acá.
- "Abrir carpeta" no abre el explorador; cámara/micrófono de la PC y AirPlay no están en el contenedor.
- Sin auto-update: se actualiza instalando el paquete nuevo.
- Integración en el paquete de la fiscalía (`deploy/fiscalia/`): pendiente, como dice la SDD.

## Contrato

No hay campos nuevos ni cambios de API: la web sigue usando `http://localhost:8765` y `/health` sin cambios. Los
orígenes usan la clave existente `Agent:AllowedOrigins`; la evidencia, `Agent:EvidenceDirectory` (por variable de
entorno `Agent__EvidenceDirectory`).
