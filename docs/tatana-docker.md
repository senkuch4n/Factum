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
deploy/tatana-docker/armar-tatana-docker.sh --version X.Y.Z --salida dist/ [--canal dist/canal]
# → dist/Tatana-Docker-vX.Y.Z.tar.gz  (+ .sha256)         paquete completo (pendrive, sin servidor)
# → dist/kit-tatana-docker-vX.Y.Z.tar.gz                  kit: lo mismo SIN la imagen (decenas de KB)
# → dist/canal/{imagen-tatana-docker-vX.Y.Z.tar.gz, kit-…, VERSION}   lo que firma CI y publica el servidor
```

La versión **oficial** (firmada, para el servidor de la fiscalía) no se arma a mano: la arma, prueba, firma y sube al
GitHub Release el workflow `tatana-docker.yml` con el tag `tatana-vX.Y.Z` (ver `docs/despliegue-nube.md` §19).

- El agente sale de `deploy/linux/armar-tatana-linux.sh` (el payload del AppImage, versiones fijas y SHA-256
  verificados ahí) con una fuente limpia (`git archive` de `--ref`, default `HEAD`). Si ya está armado (CI, paquete
  de la fiscalía), `--agente-tar Tatana-Agent-vX.Y.Z-linux-x64.tar.gz` lo reusa sin descargar nada.
- Build `linux/amd64` con `docker buildx` (en una Mac Apple Silicon, por emulación). Base `ubuntu:24.04` fijada por
  digest en `deploy/tatana-docker/imagen/Dockerfile`.
- Antes de empaquetar corre **dentro de la imagen** `--autoprueba-linux` y `--autoprueba-ios`; si alguna falla, no
  hay paquete.
- Los orígenes de la web **no** se hornean: se ponen al instalar (`--origen`).
- `claves-confiables.txt` (las claves públicas con las que el actualizador verifica cada versión) sale de
  `agent-ui/src/main/updater/trusted-keys.ts` con `ops/tatana/claves-confiables.mjs`: la misma fuente que firma y
  verifica. `--clave-extra key_id:spki` suma una clave efímera **solo** para ensayos y pruebas.
- Después del paquete corre dentro de la imagen las autopruebas de `verificar-manifiesto-docker.py` y
  `estado-agente.py` (con el Python de la imagen, que es el que los corre en la PC).

Contenido de `Tatana-Docker-vX.Y.Z/`:

| Archivo | Para qué |
|---|---|
| `imagen-tatana-docker-vX.Y.Z.tar.gz` | `docker save` de la imagen (`IMAGEN.txt` dice el nombre:tag) |
| `instalar-tatana-docker.sh` | carga la imagen, escribe la config, levanta el contenedor y espera `/health` |
| `estado-tatana-docker.sh` | contenedor, `/health`, usbmuxd, USB visibles, `adb devices` y registro |
| `desinstalar-tatana-docker.sh` | borra el contenedor y el actualizador (y con `--borrar-imagen` las imágenes); **nunca** la evidencia |
| `actualizador-tatana-docker.sh` | el actualizador automático (§4); lo corre el timer de systemd como root |
| `sumar-actualizador.sh` | suma el actualizador a una PC instalada antes (§3); el servidor lo publica suelto |
| `verificar-manifiesto-docker.py`, `claves-confiables.txt` | verificación Ed25519 de cada versión, con las claves del kit instalado |
| `estado-agente.py` | pregunta a Tatana si está libre (`/agent/state`) y pide el modo mantenimiento |
| `systemd/factum-tatana-actualizador.{service,timer}` | consulta cada 30 min (5 min después de prender, desfase de hasta 10 min) |
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
   **Sin `--origen` y con una instalación anterior, conserva los orígenes que ya tenía** (antes volvía a solo
   localhost y una PC del plan A perdía `https://factum.internal`).
6. Crea el contenedor `tatana-docker` (solo toca uno con la etiqueta `factum.tatana-docker=1`; uno ajeno con ese
   nombre no se toca). Si ya había uno, lo **reemplaza con vuelta atrás**: primero le pide el modo mantenimiento (si
   Tatana está capturando, corta sin tocar nada: "esperá a que termine"; `--forzar` lo saltea pidiendo escribir
   FORZAR), lo detiene y lo renombra `tatana-docker-anterior`, levanta el nuevo y espera `/health` hasta 2 minutos;
   si el nuevo no responde, vuelve el anterior tal cual estaba.
7. Espera `/health`.
8. **Actualizador automático** (plan A): por defecto se instala si los orígenes incluyen `https://factum.internal`
   (canal `https://factum.internal/tatana/docker/`); `--actualizador <url>` para otro canal, `--sin-actualizador`
   para no instalarlo. Con sudo: copia el kit a `/opt/factum-tatana-docker/kits/<versión>`, escribe
   `/etc/factum-tatana-docker/` (copia de la instalación, canal y la CA de la fiscalía de
   `/usr/local/share/ca-certificates/factum-fiscalia.crt`; sin la CA queda instalado pero apagado: correr
   `preparar-pc-perito.sh` y volver a instalar) y activa `factum-tatana-actualizador.timer`.

Opciones: `--puerto N` (solo pruebas), `--si` (contesta que sí a todo), `--mock` (agente simulado, sin USB ni
usbmuxd: demostración sin celular), `--forzar`, `--actualizador <url>`, `--sin-actualizador`, `--red-con-puertos`
(solo para probar en Docker Desktop de Mac/Windows; implica `--mock`; no usar en la fiscalía).

Reinstalar (otra versión u otros orígenes) es correr el script de nuevo: no se pierden datos ni evidencia.

### Sumar el actualizador a una PC ya instalada (las del 2026-10-20) o instalar sin pendrive

Con el servidor de la fiscalía ya actualizado (publica Tatana en Docker en su canal), en cada PC, en una terminal con
la sesión del perito (**sin sudo**: el script la pide una vez):

```bash
wget -qO /tmp/sumar-actualizador.sh https://factum.internal/tatana/docker/sumar-actualizador.sh \
  && bash /tmp/sumar-actualizador.sh
```

Baja del servidor el manifiesto firmado, el kit y la imagen a `~/Tatana-Docker-vX.Y.Z/` (si se corta, se vuelve a
correr y sigue), verifica la firma con el Python de la imagen **ya instalada** y el tamaño y SHA-256 de kit e imagen,
y corre `./instalar-tatana-docker.sh --si` (conserva los orígenes; si Tatana está capturando corta y no se pierde
nada). Al final lanza una primera consulta para que la PC aparezca en `estado-peritos.sh` del servidor. No toca la
evidencia, `~/.local/share/tatana-docker` (agente, pareos del iPhone, configuración), la regla udev, el usbmuxd
enmascarado, `/etc/hosts` ni la CA. La imagen vieja (`tatana-docker-prueba:0.0.0-docker-prueba`) queda como
"anterior" para volver atrás.

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
| Discos USB (extracción) | `-v /media:/media:rslave` y `-v /run/media:/run/media:rslave` (si existen) + `Agent__Extraction__AllowedRoots` = evidencia, `/media`, `/run/media` | Destino de una extracción lógica a un disco externo. `rslave`: un disco enchufado **después** de arrancar el contenedor aparece solo. `AllowedRoots` limita a dónde puede escribir Tatana. Mismas rutas dentro y fuera. |
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

### Extraer a un disco USB

La extracción lógica de Android (sección "Extracciones" de la web) puede guardar en un disco externo. udisks2 del
escritorio monta el disco en `/media/<usuario>/<etiqueta>` (Ubuntu/Debian) o `/run/media/<usuario>/<etiqueta>`
(Fedora/RHEL), y el contenedor lo ve con el montaje `rslave`. Si enchufás el disco **después** de que arrancó el
contenedor, aparece solo; en el paso "Destino" del asistente, tocá **Actualizar** y la unidad nueva se lista. Solo se
ofrecen la carpeta de evidencia y esos discos (`AllowedRoots`). El disco tiene que estar montado con permiso de
escritura para el usuario del perito (udisks2 lo hace así). Requiere que `/` del host sea un montaje compartido
(`rshared`), lo normal con systemd (Ubuntu 18.04/20.04 incluidas). El montaje entra solo al **actualizar** a esta
versión (o al reinstalar); no hace falta tocar `instalacion.env`.

### Limitaciones conocidas frente al nativo

- No hay ventana de Tatana: el estado se ve en la web y con `estado-tatana-docker.sh`.
- "Abrir la carpeta" de la evidencia no abre el explorador de archivos (no hay escritorio adentro); la carpeta es
  `~/Factum/Evidencia`.
- La cámara/micrófono de la PC para la grabación de la sesión no están en el contenedor (en Linux la cámara viene
  apagada por defecto igual). AirPlay (uxplay/avahi) tampoco.

### Actualizaciones automáticas (plan A)

En las PCs del plan A, el **timer** `factum-tatana-actualizador.timer` consulta el servidor de la fiscalía 5 minutos
después de prender la PC y después cada 30 minutos (con un desfase aleatorio de hasta 10 minutos). En cada consulta:

1. Baja `https://factum.internal/tatana/docker/tatana-update-docker.json` (con el `curl` de la imagen instalada: el
   host no necesita curl) y lo verifica con `verificar-manifiesto-docker.py` y `claves-confiables.txt` **del kit
   instalado en `/opt`** (nunca con lo que mande el servidor): firma Ed25519, producto `tatana-docker`, nombres.
2. Si la versión publicada no es **mayor** que la instalada → "al día" (nunca baja de versión). Si ya falló en esta
   PC → no la reintenta hasta que haya una mayor.
3. Chequea el espacio y **baja** la imagen y el kit (se reanuda si se corta); solo los usa si tamaño y SHA-256
   coinciden con el manifiesto firmado.
4. **Solo con Tatana libre** (sin operaciones, grabaciones, sesión grabándose, posproceso ni grabaciones sin
   importar): carga la imagen, corre su autoprueba, pide el modo mantenimiento y cambia el contenedor con vuelta
   atrás (§3, paso 6), con los **mismos** montajes, uid/gid, puerto y configuración. Si la nueva no responde en 2
   minutos, vuelve a la anterior y la marca como fallida. Si Tatana está ocupado, espera a la próxima consulta (la
   descarga queda hecha).
5. Deja el resultado en `/var/lib/factum-tatana-docker/estado.env` y en `/var/log/factum-tatana-docker/actualizador.log`,
   y el servidor lo ve en su registro (`estado-peritos.sh`).

Comandos (con el usuario del perito, desde la carpeta del paquete o `/opt/factum-tatana-docker/actual/`):

| Qué | Comando |
|---|---|
| Ver el estado del actualizador (sin sudo) | `./estado-tatana-docker.sh --actualizaciones` (también sale en `./estado-tatana-docker.sh`) |
| Buscar una versión ya | `./estado-tatana-docker.sh --buscar-ahora` (pide sudo) |
| Volver a la versión anterior | `./estado-tatana-docker.sh --volver-a-anterior` (pide sudo; solo con Tatana libre; la versión de la que se vuelve no se reinstala sola hasta que haya una mayor) |
| Apagar el actualizador | `sudo systemctl disable --now factum-tatana-actualizador.timer` (prenderlo: `enable --now`) |
| Registro | `/var/log/factum-tatana-docker/actualizador.log` o `journalctl -u factum-tatana-actualizador` |

Resultados posibles (`estado.env`, `RESULTADO`): `al_dia`, `actualizada`, `esperando_libre` (Tatana estaba
capturando), `sin_servidor`, `manifiesto_invalido`/`clave_desconocida`/`firma_invalida` (se rechazó lo publicado),
`hash_invalido`/`imagen_invalida` (descarga dañada, se descartó), `sin_espacio`, `autoprueba_fallida`, `no_arranca`
(volvió a la anterior), `version_fallida`, `vuelta_manual`, `recuperado` (un cambio quedó a medias por un corte de luz
y se recuperó), `sin_configuracion`, `deshabilitado`, `ocupado_otro_proceso`, `error_docker`, `error_agente`.

Sin el actualizador (plan B o `--sin-actualizador`) se actualiza instalando el paquete nuevo. Una PC instalada antes
de que existiera el actualizador (`estado-tatana-docker.sh` dice "Esta PC no tiene el actualizador") se suma con
`sumar-actualizador.sh` (§3, "Sumar el actualizador a una PC ya instalada").

## 5. Desinstalar

```bash
./desinstalar-tatana-docker.sh                 # borra el contenedor y el actualizador
./desinstalar-tatana-docker.sh --borrar-imagen # y las imágenes que cargó Factum
```

Quita también el timer y las units del actualizador, `/opt/factum-tatana-docker`, `/etc/factum-tatana-docker`,
`/var/lib/factum-tatana-docker`, `/var/log/factum-tatana-docker` y un `tatana-docker-anterior` que haya quedado.

Nunca borra `~/Factum/Evidencia` ni `~/.local/share/tatana-docker`, y deja la regla USB (la usa también Tatana
nativo). Si la instalación había enmascarado el `usbmuxd` del host, pregunta si devolverlo.

## 6. Verificación

En la Mac (sin USB: Docker Desktop no pasa dispositivos USB al contenedor):

1. `armar-tatana-docker.sh` de punta a punta: build `linux/amd64` y `--autoprueba-linux` / `--autoprueba-ios` OK
   dentro de la imagen.
2. `instalar-tatana-docker.sh --red-con-puertos --puerto <libre>` con `TATANA_DOCKER_CONTENEDOR`,
   `TATANA_DOCKER_DATOS` y `TATANA_DOCKER_EVIDENCIA` apuntando a carpetas de prueba: `/health` desde el host con
   `mock: true`; `estado` y `desinstalar --borrar-imagen`.

3. Actualizador (SDD fiscalia-operacion §14.1): `node ops/tatana/probar-firma.mjs`, las autopruebas de los dos `.py`
   con el Python de la imagen (`--network none`), `ci/tatana-docker/probar-versiones.sh` (la misma tabla de versiones
   en bash y Python) y `ci/tatana-docker/prueba-cruzada.sh --imagen <imagen>` (manifiesto firmado con Node verifica en
   el Python de la PC; alterado, falla con el mismo motivo en los dos).

**En CI** (`tatana-docker.yml`; Docker y systemd reales, nunca en la Mac): `ci/tatana-docker/prueba-actualizador.sh`
(canal real con Caddy y el Caddyfile de la fiscalía; actualizada, rechazos por firma/clave/producto/hash, ocupado,
no arranca y vuelta atrás, volver a mano, sin espacio, servidor caído, corte a mitad del cambio, desinstalar) y
`ci/tatana-docker/prueba-2004.sh` (Ubuntu 20.04 con systemd 245: units, instalación, timer, una corrida feliz y una de
vuelta atrás).

**Pendiente en una PC Ubuntu con celular real** (no se puede en la Mac):

- Actualizador: con un servidor de prueba, capturar y publicar una versión nueva durante la captura → no se toca; al
  terminar, se actualiza sola; la captura y el ZIP quedan; el iPhone sigue pareado y adb sigue viendo el Android.

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
