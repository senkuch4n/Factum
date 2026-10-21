# Tatana en Docker

Modo de respaldo de Tatana para PCs Linux donde el AppImage nativo no anda (glibc anterior a 2.35, sin FUSE 3, sin
systemd o una distro desconocida). Es el **mismo agente** (`Factum.Agent`, sin la ventana de Electron), con las mismas
herramientas y versiones que trae el AppImage (adb, scrcpy, ffmpeg, Python + pymobiledevice3), más `usbmuxd` para el
iPhone, dentro de un contenedor `ubuntu:24.04` con acceso a los USB de la PC.

**La web de Factum no cambia:** sigue hablando con `http://localhost:8765`. Sirve para el plan A (PC de perito que usa
el servidor de la fiscalía) y para el plan B (todo en una PC), en cualquier Linux x86-64 con Docker (el Docker sin
internet del paquete de la fiscalía alcanza).

Origen: SDD `Refactorizaciones/instalacion-fiscalia.md` §7 (HU #94).

## 1. ¿Nativo o Docker?

Correr primero, en la PC del perito y desde su escritorio (no por SSH):

```bash
./diagnostico-distro.sh
```

Dice distro, glibc, arquitectura, RAM, disco libre, systemd, FUSE 3, escritorio y Docker, y recomienda:

| La PC tiene… | Recomendación |
|---|---|
| x86-64, glibc ≥ 2.35, systemd, FUSE 3 y escritorio | **Tatana nativo (AppImage)** — Docker como respaldo |
| lo mismo pero sin FUSE 3 | nativo después de instalar `fuse3`; si no se puede, Docker |
| glibc vieja / musl, sin systemd o sin escritorio | **Tatana en Docker** (si falta Docker: `instalar-docker-offline.sh` del paquete de la fiscalía) |
| no es x86-64 (ARM, 32 bits) | ninguna de las dos |

Docker "rootless" no sirve (no le puede dar los USB al contenedor): hace falta el Docker de sistema.

## 2. Armar el paquete (en la Mac o en Linux, con internet)

```bash
deploy/tatana-docker/armar-tatana-docker.sh --version X.Y.Z --salida dist/
# → dist/Tatana-Docker-vX.Y.Z.tar.gz  (+ .sha256)
```

- El agente sale de `deploy/linux/armar-tatana-linux.sh` (el payload del AppImage, versiones fijas y SHA-256
  verificados ahí) con una fuente limpia (`git archive` de `--ref`, default `HEAD`). Si ya está armado (CI, paquete
  de la fiscalía), `--agente-tar Tatana-Agent-vX.Y.Z-linux-x64.tar.gz` lo reusa sin descargar nada.
- Build `linux/amd64` con `docker buildx` (en una Mac Apple Silicon, por emulación). Base `ubuntu:24.04` fijada por
  digest en `deploy/tatana-docker/imagen/Dockerfile`.
- Antes de empaquetar corre **dentro de la imagen** `--autoprueba-linux` y `--autoprueba-ios`; si alguna falla, no
  hay paquete.
- Los orígenes de la web **no** se hornean: se ponen al instalar (`--origen`).

Contenido de `Tatana-Docker-vX.Y.Z/`:

| Archivo | Para qué |
|---|---|
| `imagen-tatana-docker-vX.Y.Z.tar.gz` | `docker save` de la imagen (`IMAGEN.txt` dice el nombre:tag) |
| `instalar-tatana-docker.sh` | carga la imagen, escribe la config, levanta el contenedor y espera `/health` |
| `estado-tatana-docker.sh` | contenedor, `/health`, usbmuxd, USB visibles, `adb devices` y registro |
| `desinstalar-tatana-docker.sh` | borra el contenedor (y con `--borrar-imagen` la imagen); **nunca** la evidencia |
| `diagnostico-distro.sh` | el diagnóstico de §1 (anda suelto, sin el resto del paquete) |
| `comun.sh` | funciones que usan los otros scripts |
| `LEEME.txt`, `VERSION`, `SHA256SUMS` | guía corta, versión y sumas (`sha256sum -c SHA256SUMS`) |

## 3. Instalar en la PC del perito

Con el usuario del perito, **sin sudo** (el script pide la contraseña solo cuando hace falta):

```bash
tar -xzf Tatana-Docker-vX.Y.Z.tar.gz && cd Tatana-Docker-vX.Y.Z
./instalar-tatana-docker.sh --origen https://factum.internal   # plan A (servidor de la fiscalía)
./instalar-tatana-docker.sh                                     # plan B (todo en esta PC, http://localhost:3000)
```

Qué hace, en orden:

1. Chequea Linux x86-64 y Docker (si el usuario no está en el grupo `docker`, usa `sudo docker`).
2. Verifica el SHA-256 de la imagen contra `SHA256SUMS` y la carga (`docker load`).
3. **usbmuxd del host:** si la PC tiene su propio `usbmuxd` andando, avisa (choca con el del contenedor por el
   iPhone) y **solo con confirmación** lo detiene y lo enmascara (`systemctl mask usbmuxd.service`). Deja una marca
   para que la desinstalación ofrezca devolverlo.
4. **Regla USB de Android:** si no existe `/etc/udev/rules.d/70-factum-tatana.rules`, con confirmación escribe la
   misma regla que escribe Tatana nativo ("Preparar esta PC": `TAG+="uaccess"` para la interfaz adb) y recarga udev.
5. Escribe `~/.local/share/tatana-docker/appsettings.Local.json` con `Agent:AllowedOrigins` = `http://localhost:3000`,
   `http://127.0.0.1:3000` + cada `--origen`, validado con el mismo `hornear-origenes-tatana.py` del portátil (corre
   con el Python de la imagen: el host no necesita Python). Conserva las otras claves que el archivo ya tenga.
6. Recrea el contenedor `tatana-docker` (solo si tiene la etiqueta `factum.tatana-docker=1`; uno ajeno con ese nombre
   no se toca) y espera `/health` hasta 2 minutos.

Opciones: `--puerto N` (solo pruebas), `--si` (contesta que sí a todo), `--mock` (agente simulado, sin USB ni
usbmuxd: demostración sin celular), `--red-con-puertos` (solo para probar en Docker Desktop de Mac/Windows; implica
`--mock`; no usar en la fiscalía).

Reinstalar (otra versión u otros orígenes) es correr el script de nuevo: no se pierden datos ni evidencia.

### Cómo corre el contenedor

| Qué | Cómo | Por qué |
|---|---|---|
| Red | `--network host` | El agente escucha en `localhost:8765` **del host** (lo que llama la web) y recibe los avisos de enchufado USB (netlink del espacio de red del host). |
| USB | `-v /dev/bus/usb:/dev/bus/usb` + `--device-cgroup-rule 'c 189:* rmw'` + `-v /run/udev:/run/udev:ro` | Sin `--privileged`: solo los dispositivos USB (major 189). Los celulares que se enchufan después aparecen solos (es el árbol del host). `/run/udev` para que libusb (usbmuxd) vea el enchufado en caliente. |
| Usuarios | `usbmuxd` corre como root adentro; el agente baja con `setpriv` al uid/gid **del perito** (y sus grupos) con `no_new_privs` | La evidencia y los datos quedan a nombre del perito; el permiso de adb al Android es el ACL `uaccess` que udev le da al usuario de la sesión, que vale igual adentro (mismo uid, mismo nodo de dispositivo). |
| Reinicio | `--restart unless-stopped` | Arranca solo al prender la PC (con el servicio docker habilitado). |
| Config | `appsettings.Local.json` montado de solo lectura en `/config` (`--local-config`) | Igual que el AppImage. |
| Datos | `~/.local/share/tatana-docker/{agent-data,home,lockdown}` montados en la **misma ruta** | Las rutas que muestra Tatana coinciden con las del host. `lockdown/` guarda los pareos del iPhone entre reinicios. |
| Evidencia | `~/Factum/Evidencia` montada en la misma ruta (`Agent__EvidenceDirectory`) | La misma carpeta que usa Tatana nativo. |
| Hora | `/etc/localtime` de solo lectura + `TZ` del host | La hora de los registros forenses es la de la PC. |
| Audio | `Agent__Recording__AudioPlayback=false` (en la imagen) | El contenedor no tiene dispositivo de audio: scrcpy graba con `--no-audio-playback`. El audio interno del Android 11+ queda igual en el video, pero no suena en la PC mientras se graba. |
| SELinux | `--security-opt label=disable` | En Fedora/RHEL, sin esto el contenedor no puede abrir `/dev/bus/usb`. En el resto no cambia nada. |

## 4. Uso diario y problemas

- `./estado-tatana-docker.sh` — si algo no anda, es lo primero. Muestra si el contenedor anda, `/health`, si
  usbmuxd del contenedor tiene su socket, si hay un usbmuxd del host compitiendo, cuántos USB ve el contenedor, qué ve
  `adb devices` y las últimas líneas del registro (`--registro N` para más).
- **El Android no aparece:** falta la regla USB (paso 4) o el celular se enchufó antes de escribirla: desenchufar y
  volver a enchufar. La regla `uaccess` vale para el usuario con sesión gráfica activa: si el perito entra por SSH o
  la PC no usa logind, crear a mano una regla con `GROUP="plugdev", MODE="0660"` y agregar al perito a `plugdev`
  (el contenedor hereda sus grupos al reinstalar).
- **El iPhone no aparece:** un `usbmuxd` del host está andando (el estado lo dice): reinstalar y aceptar detenerlo.
  La primera vez hay que tocar "Confiar" en el iPhone.
- **Puerto 8765 ocupado:** Tatana nativo está abierto. No usar los dos a la vez.
- **No se puede usar Tatana nativo y Docker a la vez** por el puerto y por usbmuxd. Para volver a nativo:
  `./desinstalar-tatana-docker.sh` (ofrece devolver el usbmuxd del host).

### Limitaciones conocidas frente al nativo

- No hay ventana de Tatana: el estado se ve en la web y con `estado-tatana-docker.sh`.
- "Abrir la carpeta" de la evidencia no abre el explorador de archivos (no hay escritorio adentro); la carpeta es
  `~/Factum/Evidencia`.
- La cámara/micrófono de la PC para la grabación de la sesión no están en el contenedor (en Linux la cámara viene
  apagada por defecto igual). AirPlay (uxplay/avahi) tampoco.
- Sin actualizaciones automáticas: se actualiza instalando el paquete nuevo.

## 5. Desinstalar

```bash
./desinstalar-tatana-docker.sh                 # borra el contenedor
./desinstalar-tatana-docker.sh --borrar-imagen # y la imagen
```

Nunca borra `~/Factum/Evidencia` ni `~/.local/share/tatana-docker`, y deja la regla USB (la usa también Tatana
nativo). Si la instalación había enmascarado el `usbmuxd` del host, pregunta si devolverlo.

## 6. Verificación

En la Mac (sin USB: Docker Desktop no pasa dispositivos USB al contenedor):

1. `armar-tatana-docker.sh` de punta a punta: build `linux/amd64` y `--autoprueba-linux` / `--autoprueba-ios` OK
   dentro de la imagen.
2. `instalar-tatana-docker.sh --red-con-puertos --puerto <libre>` con `TATANA_DOCKER_CONTENEDOR`,
   `TATANA_DOCKER_DATOS` y `TATANA_DOCKER_EVIDENCIA` apuntando a carpetas de prueba: `/health` desde el host con
   `mock: true`; `estado` y `desinstalar --borrar-imagen`.

**Pendiente en una PC Ubuntu con celular real** (no se puede en la Mac):

- `./diagnostico-distro.sh` recomienda lo esperado.
- `./instalar-tatana-docker.sh` sin `--mock`: `/health` con `mock: false`; `estado` muestra usbmuxd OK y los USB.
- Android: enchufar después de instalar → aparece en la web; adquisición y ZIP en `~/Factum/Evidencia` a nombre del
  perito (`ls -l`).
- iPhone: "Confiar", aparece en la web; desenchufar/enchufar sin reiniciar el contenedor; pareo que sobrevive a
  `docker restart tatana-docker`.
- Con un `usbmuxd` del host activo: el instalador avisa, lo enmascara solo si se confirma, y `desinstalar` lo
  devuelve.
- Reiniciar la PC: Tatana vuelve solo.
- Plan A: la web en `https://factum.internal` habla con Tatana (orígenes); plan B: `http://localhost:3000`.
