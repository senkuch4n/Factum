# SDD — instalacion-fiscalia (#94)

> Tarea de despliegue urgente, pedida por el usuario el 2026-10-20 (sin RDD completa: la escribió el orquestador
> con lo que el usuario contestó). Toca solo `deploy/`, `docs/` y, si hace falta, scripts de armado. **No toca
> código de producción** de `server/src`, `client/src` ni `agent-ui/src`.

## 1. Contexto

Una fiscalía va a probar Factum en su red interna. Varios peritos comparten casos. Las PCs tienen Linux (distro
desconocida). Hay internet "en teoría", pero lento: todo lo pesado (imágenes Docker, AppImage) va en un pendrive.
El usuario instala en persona. Se piden **dos planes con modos de falla distintos**:

- **Plan A — red local:** un servidor Linux de la fiscalía corre web + API + MongoDB + Caddy en Docker. Los peritos
  entran desde su PC con `https://factum.internal` y tienen Tatana (AppImage de Linux, #91) en su PC.
- **Plan B — una sola PC:** todo en una PC (web, API, MongoDB y Tatana) con `http://localhost:3000`, como el
  paquete local de Windows pero en Linux. Sin certificados ni red: si el plan A falla por red/DNS/certificado, el
  plan B sigue andando. Se puede instalar en la misma PC que el servidor del plan A o en otra.

## 2. Decisiones (del orquestador; el usuario puede objetarlas)

- **D1. Nombre fijo `factum.internal`** (`.internal` está reservado por la IANA para uso privado). Se hornea en el
  frontend del plan A (`NEXT_PUBLIC_BACKEND_URL=https://factum.internal`), así se arma en la Mac sin saber la IP.
  Cada PC de perito resuelve el nombre con una línea en `/etc/hosts` (`<IP del servidor> factum.internal`) que
  escribe el script de la PC de perito; el servidor también la tiene (`127.0.0.1`). No depende de DNS ni de mDNS.
- **D2. HTTPS con `tls internal` de Caddy.** Sin HTTPS, la cámara del navegador (fotos del perito y del titular) no
  anda fuera de localhost. El script del servidor exporta la raíz de la CA de Caddy
  (`/data/caddy/pki/authorities/local/root.crt` del volumen) a un archivo que se lleva a cada PC de perito, y el
  script de perito la instala en el sistema (`/usr/local/share/ca-certificates` + `update-ca-certificates`, o
  `trust anchor` en Fedora) **y** en la base NSS del usuario (`~/.pki/nssdb`, con `certutil`, para Chrome/Chromium)
  y en los perfiles de Firefox si los hay (`policies.json` con `ImportEnterpriseRoots` o `certutil` en el perfil).
- **D3. Reuso.** Plan A parte de `deploy/cloud/docker-compose.yml` (Caddy + backend + frontend + Mongo con
  usuario/clave) y sus scripts; plan B parte de `deploy/windows/docker-compose.yml` (localhost, sin Caddy) y de
  la lógica de `deploy/windows/scripts/instalar.ps1` (generación de secretos, carpetas, `docker load`, health).
  No duplicar a mano lo que se puede parametrizar; si un archivo de `deploy/cloud/` necesita una variante (p. ej. un
  `Caddyfile` con `tls internal`, sin ACME, sin `/tatana/updates` ni `/descargas`), va como archivo nuevo de esta
  carpeta y **no cambia el comportamiento de la nube**.
- **D4. Offline.** Las imágenes `linux/amd64` (backend, frontend del plan A, frontend del plan B, mongo, caddy) se
  arman en la Mac y viajan como `.tar` (`docker save`), con `SHA256SUMS`. En la fiscalía solo hace falta Docker
  Engine + compose plugin. Si no está, la guía explica instalarlo desde el gestor de la distro
  (`apt install docker.io docker-compose-v2` / `dnf install moby-engine docker-compose`) y el script lo detecta y
  lo dice en castellano, sin intentar instalar nada por su cuenta.
- **D5. Tatana en las PCs de perito.** Se usa el AppImage de Linux de #91 (artifact del CI sobre `develop`). Plan A:
  el script de perito escribe `~/.config/Tatana/appsettings.Local.json` con `Agent:AllowedOrigins` =
  `["https://factum.internal"]` (sumado a localhost, ver cómo lo hornea `deploy/windows/hornear-origenes-tatana.py`
  y cómo lo lee `server/src/Factum.Agent/Program.cs`), sin pisar otras claves si el archivo ya existe. Plan B: no
  hace falta (los orígenes por defecto ya incluyen localhost — confirmalo). La actualización automática de Tatana
  apunta al canal de la nube: no molesta, pero se documenta.
- **D6. Sin datos de prueba.** No se cargan casos ni usuarios de ejemplo. El primer superadmin se crea como en la
  instalación local de Windows / la nube (mirá cómo lo hacen y reusá).
- **D7. Backups.** Plan A y B: script de backup local (Mongo dump + carpeta de evidencia + `.env`) a una carpeta /
  pendrive, inspirado en `deploy/windows/scripts/backup.ps1` y `deploy/cloud/scripts/backup.sh` (sin R2 ni age
  obligatorios). Restaurar: al menos documentado; script si sale barato.
- **D8. Seguridad mínima.** Plan A: el servidor publica solo 80/443 (Caddy) a la red; backend, frontend y Mongo no
  se exponen. Plan B: todo en `127.0.0.1`. Secretos generados en la instalación (nunca en el paquete), `.env` con
  permisos 600, carpeta de datos 700. CORS del backend = el origen exacto de cada plan.

## 3. Entregables (`implementer-backend`)

Carpeta nueva `deploy/fiscalia/` (o el nombre que mejor encaje, justificalo):

1. `armar-paquete-fiscalia.sh` (corre en la Mac): git archive limpio de `--ref` (default HEAD), build de las
   imágenes con los build-args de cada plan, `docker save` a `imagenes/*.tar`, copia de compose/Caddyfile/scripts,
   `--tatana-appimage <ruta>` opcional para meter el AppImage, `SHA256SUMS`, salida `Factum-Fiscalia-vX.Y.Z/` +
   `.tar.gz`. Mismos cuidados que `deploy/windows/armar-paquete.sh` (fuente limpia, verificación del tar).
2. `servidor/instalar-servidor.sh` (plan A, con `sudo`): verifica SHA256SUMS, Docker y puertos 80/443 libres;
   `docker load`; crea `/srv/factum-fiscalia` (o `/opt/...`) con config/datos/backups; genera secretos y `.env`;
   `compose up -d`; espera health; agrega `127.0.0.1 factum.internal` a `/etc/hosts` (idempotente); exporta la CA de
   Caddy a `ca-factum-fiscalia.crt` junto al paquete; muestra la IP de la PC y los pasos siguientes. Flags:
   `--reparar`, `--desinstalar` (sin borrar datos salvo `--borrar-datos` explícito).
3. `perito/preparar-pc-perito.sh <IP-del-servidor>` (plan A, con `sudo` para hosts y CA): línea en `/etc/hosts`
   (idempotente, reemplaza una anterior de `factum.internal`), instala la CA (D2), escribe el
   `appsettings.Local.json` de Tatana (D5), copia el AppImage a `~/Applications/` si viene en el paquete, y prueba
   `curl --cacert ... https://factum.internal/health`. Modo `--simular` que solo imprime.
4. `una-pc/instalar-una-pc.sh` (plan B): como el de Windows pero en bash: `docker load`, carpeta, secretos, `.env`,
   compose solo en `127.0.0.1`, health, acceso directo `.desktop` que abre `http://localhost:3000`. Flags `--reparar`
   y `--desinstalar` como arriba.
5. `backup.sh` / `restaurar.sh` (sirven para los dos planes, eligen por la carpeta instalada) y
   `diagnostico.sh` (estado de contenedores, health, espacio en disco, versión, últimas líneas de log — sin
   secretos).
6. `docs/instalacion-fiscalia.md`: guía para el usuario, en castellano, paso a paso y con checklist imprimible:
   qué llevar en el pendrive; requisitos (servidor: 64 bits, 4 GB RAM mín., 40 GB libres, IP fija o reservada en
   el router; peritos: requisitos de Tatana de #91 — Ubuntu 22.04 / Debian 12 en adelante); plan A (servidor,
   cada perito, primer superadmin, prueba de humo con un caso de prueba que después se borra por su `_id`); plan B;
   cuándo pasar de A a B; backups; problemas frecuentes (certificado no confiable, `factum.internal` no resuelve,
   firewall `ufw` en el servidor, Docker sin permisos, Tatana no conecta → `appsettings.Local.json`); cómo
   desinstalar.
7. Tests: `bash -n` y `shellcheck` (si está) de todos los scripts; prueba real del compose de los dos planes en la
   Mac con Docker Desktop (plan A con `factum.internal` en `/etc/hosts` de la Mac **solo si el usuario lo autoriza**;
   si no, usá `curl --resolve factum.internal:443:127.0.0.1 --cacert …`). Dejá todo apagado y limpio al terminar
   (contenedores, volúmenes y redes de la prueba, por nombre de proyecto propio; nunca los de otros proyectos de la
   Mac: hay contenedores `gfd`, `evidentia-*`, `nutri-bot-*` que no se tocan).

## 4. Fuera de alcance

Cambios en `server/src`, `client/src` o `agent-ui/src`; una release pública de Tatana; DNS real o certificados de
una CA pública; alta disponibilidad.

## 5. Verificación (antes de `done`)

- `bash -n`/`shellcheck` limpios; `armar-paquete-fiscalia.sh` corrido de punta a punta en la Mac (versión
  `0.0.0-fiscalia-prueba`), con el tamaño del paquete anotado.
- Plan A levantado en la Mac: `https://factum.internal/health` y la web por Caddy con la CA exportada; login del
  superadmin; CORS ok desde el origen. Plan B levantado: `http://localhost:3000` y `:8080/health`. Backup y
  restaurar probados al menos una vez sobre una instalación de prueba.
- Todo apagado al final; nada de otros proyectos tocado.

## 6. Cambio de requisito (2026-10-20, usuario)

Las PCs de la fiscalía **no tienen Docker** y el internet es lento: el paquete trae Docker Engine estático +
plugin compose (versiones fijas con SHA-256) e `instalar-docker-offline.sh` (systemd units, grupo docker; no toca
un Docker ya instalado). Reemplaza la instalación por el gestor de paquetes de D4 como camino principal.

## 7. Tatana en Docker (2026-10-20, usuario: "por aparte", en paralelo a A/B)

La distro de las PCs de la fiscalía es desconocida y Tatana nativo exige glibc ≥ 2.35. Modo de respaldo:
**el agente Tatana (`Factum.Agent`, sin la UI de Electron) corriendo en un contenedor `ubuntu:24.04`** con adb,
scrcpy, ffmpeg, Python + pymobiledevice3 y `usbmuxd`, con acceso a los USB del host. La web no cambia: habla con
`http://localhost:8765`. Sirve para el plan A (PC de perito) y el plan B (PC única) en cualquier Linux x86_64 con
Docker (el Docker offline del paquete).

- **Carpeta propia `deploy/tatana-docker/`** y guía propia `docs/tatana-docker.md`; artefacto propio
  (`Tatana-Docker-vX.Y.Z.tar.gz` con la imagen `docker save` + scripts + SHA256SUMS). No toca `deploy/fiscalia/`
  ni `docs/instalacion-fiscalia.md` (los está escribiendo otro implementador); la integración en el paquete de la
  fiscalía se hace después.
- **Contenido del agente:** reusar lo que arma `deploy/linux/armar-tatana-linux.sh` (agente linux-x64 self-contained
  + `tools/` con versiones fijas y SHA-256) en vez de duplicar descargas; si hace falta, extraer el bloque de armado
  a una función o flag del script existente sin cambiar su resultado.
- **Ejecución:** `--network host` (el agente escucha en `localhost:8765` del host y recibe los uevents de USB),
  `/dev/bus/usb` montado con la regla de cgroup para dispositivos USB (o `--privileged` si es lo único que anda;
  justificarlo), `restart: unless-stopped`. `usbmuxd` corre adentro como root; el agente corre con el uid/gid del
  usuario del host (así la evidencia en `~/Factum/Evidencia` y los datos quedan a su nombre). Si el host tiene un
  `usbmuxd` activo, el script avisa y lo detiene/enmascara solo con confirmación (choca por el iPhone).
- **Config:** `appsettings.Local.json` montado (plan A: `Agent:AllowedOrigins` con `https://factum.internal`;
  plan B: localhost). Carpeta de datos y de evidencia montadas desde el home del usuario.
- **Scripts:** `instalar-tatana-docker.sh [--origen <url>]` (carga la imagen, escribe config, levanta, espera
  `/health`), `estado-tatana-docker.sh`, `desinstalar-tatana-docker.sh` (sin borrar evidencia), y
  `diagnostico-distro.sh` que dice en castellano: distro, glibc, arquitectura, RAM, disco, si Docker está, y
  **recomienda Tatana nativo (AppImage) o Tatana en Docker**.
- **Verificación en la Mac:** build `linux/amd64`; dentro del contenedor `--autoprueba-linux` y `--autoprueba-ios`
  OK; `/health` desde el host con `--mock` (en Docker Desktop de Mac no hay USB: lo con celular real queda para
  la PC Ubuntu del usuario, decirlo). Limpiar solo lo propio.
