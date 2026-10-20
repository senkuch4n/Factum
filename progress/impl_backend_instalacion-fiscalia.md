# impl_backend — instalacion-fiscalia (#94)

**Estado:** done (con pendientes de verificación en hardware real, ver §6)
**Rama:** `feat/instalacion-fiscalia` (sin commit ni push, como se pidió)
**Fecha:** 2026-10-20
**SDD:** `Refactorizaciones/instalacion-fiscalia.md`, más el cambio de requisito del orquestador: **Docker offline e
independiente de la distro** dentro del paquete.

No se tocó `server/src`, `client/src` ni `agent-ui/src`. No se tocó `progress/sesiones/` ni el Project.

## 1. Archivos

Nuevos (`deploy/fiscalia/`; la carpeta respeta el nombre que proponía la SDD):

| Archivo | Qué es |
|---|---|
| `armar-paquete-fiscalia.sh` | Arma el paquete en la Mac (§3.1). `--version`, `--ref` (default HEAD), `--salida`, `--tatana-appimage`, `--deploy-de-trabajo` |
| `verificar-tar-imagen.py` | Verifica cada `.tar` de `docker save`: solo linux/amd64, tags esperados y `manifest.json` presente (que lo pueda cargar un Docker viejo) |
| `LEEME.txt`, `.gitattributes` | Resumen de una página para el paquete; LF en scripts/yml |
| `comun/docker-compose.yml` | Compose BASE de los dos planes: mongo (con usuario/clave), backend (`Auth:Mode=local`), frontend. Sin puertos |
| `comun/compose.superadmin.yml` | Primer superadmin (igual a `deploy/cloud/compose.bootstrap.yml`) |
| `comun/_comun.sh` | Funciones comunes (bash 3.2 y 5): SHA256SUMS, Docker, puertos, AVX, `.env`, `fc` (compose), salud, `/etc/hosts`, usuario real tras sudo |
| `comun/instalar.sh` | Instalador común de los dos planes (`--plan servidor\|una-pc`, `--reparar`, `--desinstalar [--borrar-datos]`, `--superadmin-dni/-nombre`, `--si`, `--sin-hosts`) |
| `comun/backup.sh`, `comun/restaurar.sh`, `comun/diagnostico.sh` | §3.5 |
| `comun/borrar-caso-prueba.sh` | Extra (ver desvío D-5): borra el caso de la prueba de humo **por su `_id`** |
| `servidor/instalar-servidor.sh` | Plan A (wrapper de `comun/instalar.sh --plan servidor`) |
| `servidor/compose.servidor.yml`, `servidor/Caddyfile` | Caddy 2.11.7 en 80/443, `tls internal`, CORS `https://factum.internal`, frontend `factum-frontend-servidor` |
| `perito/preparar-pc-perito.sh` | Plan A, PC de perito (§3.3), con `--simular` |
| `una-pc/instalar-una-pc.sh`, `una-pc/compose.una-pc.yml` | Plan B: web y API solo en `127.0.0.1:3000/8080` |
| `herramientas/instalar-docker-offline.sh` | **Cambio de requisito:** Docker Engine estático + plugin compose, sin internet |

Nuevo: `docs/instalacion-fiscalia.md` (guía con checklist imprimible, requisitos, Docker offline, plan A, perito,
primer superadmin, prueba de humo, plan B, cuándo pasar de A a B, backups, problemas frecuentes, desinstalar,
cómo armar el paquete).

Modificado: `.gitignore` (+ `deploy/fiscalia/dist/`).

Reusados **sin copiar al repo** (el armado los toma del `git archive`): `deploy/cloud/mongo-init/01-usuario-app.js`
y `deploy/windows/appsettings.Local.example.json`. No cambió nada de `deploy/cloud/` ni `deploy/windows/`.

Ojo, no es mío: en el working tree hay una carpeta `deploy/tatana-docker/` sin trackear que apareció mientras
trabajaba (otro agente/sesión). No la toqué ni la uso.

## 2. Paquete armado (verificación §5)

```
deploy/fiscalia/armar-paquete-fiscalia.sh --version 0.0.0-fiscalia-prueba --deploy-de-trabajo   → exit=0
  Carpeta:  deploy/fiscalia/dist/Factum-Fiscalia-v0.0.0-fiscalia-prueba (725M)
  tar.gz:   deploy/fiscalia/dist/Factum-Fiscalia-v0.0.0-fiscalia-prueba.tar.gz (672M), 30 archivos (+ .tar.gz.sha256)
            backend.tar 112M · caddy.tar 24M · frontend-servidor.tar 72M · frontend-una-pc.tar 72M · mongo.tar 290M
            herramientas/: docker-29.9.1.tgz 86M · docker-compose-linux-x86_64 77M
```

- Las cinco imágenes verificadas `linux/amd64` (inspect y dentro de cada tar), con `manifest.json`.
- `SHA256SUMS` generado y verificado (`shasum -c`), `.tar.gz` con `gzip -t`, misma cantidad de archivos que la
  carpeta y sin `._*` de macOS. Extraje el `.tar.gz` en una carpeta nueva e instalé desde ahí: `Paquete íntegro
  (29 archivos verificados con SHA256SUMS)`.
- Versiones fijas: Docker **29.9.1** (`c158ecc5…43ff57`; Docker no publica `.sha256` de los estáticos, el valor lo
  calculé al fijar la versión), compose **v2.40.3** (`dba9d98e…42fd372`, coincide con el `.sha256` oficial). Elegí
  compose v2 (no la v5 actual) por compatibilidad con Dockers de distro viejos.
- `--deploy-de-trabajo` fue necesario porque los scripts no están commiteados; `origen.txt` del paquete lo deja
  dicho (`deploy_de_trabajo=1`). **El paquete para la fiscalía hay que armarlo de nuevo después del commit**, sin
  ese flag y con `--tatana-appimage` (ver §6).
- Los tags `factum-backend|frontend|frontend-servidor:0.0.0-fiscalia-prueba` quedaron en la Mac (como el script de
  Windows); el paquete de prueba quedó en `deploy/fiscalia/dist/` (gitignoreado).

## 3. Pruebas en la Mac (proyectos propios `factum-fiscalia-prueba-a` / `-b`, carpetas en el scratchpad)

**Lint:** `bash -n` de todos los scripts OK; `shellcheck -x` (imagen `koalaman/shellcheck:stable`) **limpio**
(corregí SC1087, SC2015, SC2012; SC1003/SC1091 con disable comentado).

**Plan A** (80/443 estaban libres; `--sin-hosts`, `curl --resolve factum.internal:443:127.0.0.1 --cacert <CA exportada>`):
- Instalador: 10/10 pasos, superadmin creado y su contraseña temporal **vaciada del `.env`** y ausente del entorno
  del contenedor (`docker inspect` → 0 variables `BootstrapSuperadmins`). `config/` `datos/` `backups/` = 700, `.env` = 600.
- `/health` → `{"status":"ok",…,"auth_mode":"local"}`; `/health/ready` → `{"status":"ok","mongo":"ok"}`; web por
  Caddy → `200`; `http://` → `308 https://factum.internal/`; sin `--cacert` → `curl: (60)` (esperado).
- CA: `CN=Factum Fiscalia - 2026 ECC Root`, vence 2036; hoja emitida por `Factum Fiscalia - ECC Intermediate`.
- CORS: preflight desde `https://factum.internal` → `access-control-allow-origin: https://factum.internal`;
  desde `http://evil.example` → sin `allow-origin`.
- Login superadmin → `200`, `must_change_password: true`, `role: superadmin`; contraseña mala → `401`.
- Solo Caddy publica puertos (backend/frontend/mongo sin puertos al host).
- `diagnostico.sh`: todo OK; comprobé que la salida **no contiene** JWT, claves de Mongo ni la temporal.
- Instalación fallida a mitad → `--reparar`: muestra el DNI y la temporal pendientes, crea el superadmin y los
  vacía (probado).

**PC de perito** (contenedor `ubuntu:24.04` con `libnss3-tools`, como root con `SUDO_USER=perito`, contra el Caddy
de la Mac por `host.docker.internal` = `192.168.65.254`): `/etc/hosts` con una sola línea (reemplazó una anterior),
CA en el sistema (`update-ca-certificates`), en `~/.pki/nssdb` y en un perfil de Firefox (`certutil -L` muestra
`Factum Fiscalia C,,`), `policies.json` creado, `appsettings.Local.json` de Tatana **fusionado** (conservó
`Port`, `Logging` y un origen previo; quedaron localhost×2 + `https://factum.internal`), AppImage (falso) copiado a
`~/Applications/Tatana.AppImage`, y `curl https://factum.internal/health` **sin `--cacert`** → OK (confía el sistema).
Segunda corrida: idempotente (1 línea en hosts, sin duplicados en orígenes ni en políticas). `--simular` probado en
la Mac (no escribe nada). IP inválida → error claro.

**Plan B** (3000/8080 libres): instalador OK, puertos solo `127.0.0.1:8080` y `127.0.0.1:3000`, `/health/ready` OK,
web `200`, CORS `http://localhost:3000`, login `200`. `--reparar` OK; reinstalar sin `--reparar` → se niega con el
mensaje correcto; `--desinstalar` deja datos; `--desinstalar --borrar-datos --si` borra la carpeta.

**Backup y restaurar** (sobre la instalación de prueba B, con un documento marcador en una colección propia y un
archivo en `datos/backend`): backup a carpeta "pendrive" → `SHA256SUMS` verificado; inserté datos nuevos; restaurar →
hizo backup previo del estado actual, `mongorestore --drop` dejó solo el dato anterior al backup, la evidencia se
extrajo encima, `appsettings.Local.json` restaurado, `.env` del backup guardado como `.env.del-backup`, Factum sano.
Un backup con `SHA256SUMS` roto es rechazado sin tocar nada (lo comprobé con un bug que tenía el primer borrador del
backup, ya corregido). Retención local probada.

**borrar-caso-prueba.sh:** `--listar` muestra solo `PRUEBA*`; con el `_id` de un caso `123/2026` se niega; con el
de `PRUEBA-INSTALACION` borra el caso, su evento y su carpeta, y deja intacto el otro caso y su evento.

**Docker offline** (`herramientas/instalar-docker-offline.sh`):
- `x86_64` en un contenedor amd64 con systemd (emulado): SHA, binarios, plugin, grupo `docker` y units OK, pero
  **systemd emulado no puede arrancar ningún servicio** (probé un unit trivial con `/bin/touch`: mismo
  `status=255/EXCEPTION`), y `dockerd` lanzado a mano sí arrancó. No es verificable así.
- Misma lógica en **arm64 nativo** (contenedor `ubuntu:24.04` con systemd; copia del script con solo el chequeo de
  arquitectura cambiado y los `.tgz` arm64 con su SHA): instaló, `docker.service`/`docker.socket` `enabled`,
  `/run/docker.sock` `root:docker 660`, `perito` en el grupo `docker`, `Docker 29.9.1 funcionando · compose 2.40.3`,
  un contenedor con puerto publicado respondió `hola (200)`, `docker compose up` OK, `systemctl restart docker` OK.
  Segunda corrida: "Docker ya está instalado… no se toca".

Todo apagado y limpio al final: sin contenedores, redes ni volúmenes `*fiscalia*`; borradas mis imágenes de prueba
(systemd, perito, binfmt, shellcheck) y mis archivos del scratchpad. Los contenedores ajenos siguen como estaban
(`gfd`, `gfd_redis`, `evidentia-v2-mongo-1`, `nutri-bot-db-1`, `buildx_buildkit_mybuilder0`, `gfd_e2e_redis_*`).
No toqué `/etc/hosts` ni el llavero de la Mac, ni la Mongo de desarrollo, ni `Storage:DataDirectory`.

## 4. Incidente en el Docker Desktop de la Mac (ya resuelto, pero el usuario tiene que saberlo)

Al apagar los contenedores de prueba con systemd (privilegiados), su `systemd-binfmt` **desregistró todos los
manejadores binfmt del kernel de la VM de Docker Desktop** (Rosetta y qemu): durante ~10 minutos (≈12:42–12:52 UTC)
cualquier contenedor **nuevo** `linux/amd64` fallaba con `exec format error`. Los que ya corrían no se vieron
afectados (siguen arriba). Lo restauré sin reiniciar Docker: `tonistiigi/binfmt --install all` (qemu para todas las
arquitecturas) y volví a registrar a mano el manejador **Rosetta** para x86_64 (`/run/rosetta/rosetta`, flags `CF`,
sacando `qemu-x86_64`), porque el backend .NET aborta con qemu. Verificado: `docker run --platform linux/amd64 alpine
uname -m` → `x86_64` y el backend amd64 levantó sano después. **Recomendación:** reiniciar Docker Desktop cuando
no moleste, para volver exactamente a la configuración de binfmt original.

## 5. Decisiones y desvíos (respetan la intención de la SDD)

- **D-1 Primer superadmin.** Windows usa `Auth:Mode=dev` (cualquier DNI), inaceptable con varios peritos en red. Uso
  `Auth:Mode=local` en los dos planes y el bootstrap por variables de entorno de la nube
  (`compose.superadmin.yml`, solo si el DNI tiene valor). El instalador pide DNI y nombre, **genera** una temporal de
  14 caracteres (sin 0/O/1/l/I), arranca, y al estar sano (el backend crea el usuario antes de responder `/health`)
  vacía las tres claves del `.env` y recrea el backend. La temporal se muestra una vez al final (y `--reparar` la
  vuelve a mostrar si la instalación anterior no terminó).
- **D-2 Mongo con usuario y clave también en el plan B** (Windows no tiene): así backup/restaurar son un solo script
  para los dos planes y un backup del A se restaura en el B y al revés.
- **D-3 Reuso por parametrización:** compose base + un archivo por plan + superadmin, en vez de copiar el compose
  de la nube o de Windows. Los dos planes conviven en la misma PC (carpetas `/srv/factum-fiscalia` y
  `/srv/factum-una-pc`, proyectos `factum-fiscalia` y `factum-una-pc`).
- **D-4 Un solo instalador** (`comun/instalar.sh`) y dos wrappers con los nombres de la SDD.
- **D-5 Prueba de humo:** el backend no tiene `DELETE` de casos, así que agregué `borrar-caso-prueba.sh` (borra por
  `_id` exacto: `cases`, `case_events` y `case_timestamps` con `CaseId`, y `datos/backend/cases/<_id>`; se niega si
  el número de causa no empieza con `PRUEBA`). La guía pide crear el caso como `PRUEBA-INSTALACION`.
- **D-6 Firefox:** `ImportEnterpriseRoots` no funciona en Linux; uso `Certificates.Install` en
  `/etc/firefox/policies/policies.json` (fusionado, sin pisar otras políticas) + `certutil` en cada perfil
  (normal, snap y flatpak) si hay `certutil`. Sin `certutil`, el script lo dice y la guía explica la importación a
  mano en Chrome y Firefox.
- **D-7 Caddyfile sin HSTS** (si hay que regenerar la CA, el navegador tiene que poder mostrar la advertencia), sin
  ACME, sin `/tatana/updates` ni `/descargas`; `skip_install_trust`; CA con nombre "Factum Fiscalia".
- **D-8 Restaurar no pisa `config/.env`** (cada instalación conserva sus secretos; la copia queda en
  `.env.del-backup`), hace un backup del estado actual antes (como Windows) y la CA de Caddy del backup se restaura a
  mano (documentado en la guía) para no cambiar el certificado de todos los peritos por accidente.
- **D-9 Tatana (D5):** confirmado en `OriginPolicy.Defaults` que los orígenes por defecto son
  `http://localhost:3000` y `http://127.0.0.1:3000` → el plan B no necesita config. En el plan A, como
  `Agent:AllowedOrigins` **reemplaza** los defaults, el script escribe localhost×2 + `https://factum.internal` + los
  que ya hubiera, en `~/.config/Tatana/appsettings.Local.json` (en Linux `userData` = `~/.config/Tatana`, que es lo
  que agent-ui pasa con `--local-config`). Copia el original una sola vez a `.antes-de-factum-fiscalia`. El AppImage
  va a `~/Applications/Tatana.AppImage` (la ruta canónica de #91).
- **D-10 Docker offline (cambio de requisito):** `dockerd` arranca y supervisa su propio `containerd` (el del mismo
  `.tgz`), así que no instalo `containerd.service`; `docker.service` sin `LimitNOFILE=infinity`. Si la PC no tiene
  `iptables`, escribe `/etc/docker/daemon.json` con `iptables:false` (solo si no existe) y avisa. Si ya hay Docker
  (comando, `dockerd` o un `docker.service`), no lo toca; si falta solo compose, instala solo el plugin
  (`--solo-compose`). Los instaladores lo ofrecen solos cuando falta Docker (con confirmación).
- **D-11 Robustez:** si `compose up` falla porque la base tarda en su primer arranque, el instalador espera a Mongo y
  reintenta una vez; el backup espera a que la web vuelva a estar sana antes de terminar; el chequeo de puertos se
  saltea si ya hay una instalación (para que el mensaje sea "ya instalado, usá --reparar").
- **D-12** `--sin-hosts` (y las variables `FACTUM_PROYECTO_PRUEBA`, `FACTUM_PUERTO_*`, `FACTUM_HOSTS_PRUEBA`) existen
  solo para probar en otra máquina; la guía no las ofrece.

## 6. Lo que no se pudo verificar (y por qué) — queda para el usuario / QA

1. **Hardware Linux x86_64 real**: chequeo de AVX, `/etc/hosts` del servidor, `sudo` real, acceso directo `.desktop`
   del plan B (rama solo Linux), `ufw`/firewalld. En la Mac esas ramas se saltean.
2. **Docker offline x86_64 bajo systemd**: no verificable con emulación (§3). Verificado equivalente en arm64.
   Tampoco probado sin `iptables` ni en una distro sin systemd (el script se niega y lo dice).
3. **Tatana real**: el paquete de prueba **no trae AppImage** (el run de CI de `tatana-linux` sobre `develop` estaba en
   curso). Hay que bajar el artifact, descomprimirlo y armar con `--tatana-appimage`. No probé Tatana conectando con
   `https://factum.internal` ni la actualización automática.
4. **Navegadores reales**: verifiqué que la CA queda en NSS/sistema/política, no abrí Chrome/Firefox ni la cámara.
5. **`docker load` en Dockers de distro viejos** (p. ej. 20.10): el tar trae `manifest.json`, pero no lo cargué en uno.
6. **El paquete final** tiene que armarse después del commit: `armar-paquete-fiscalia.sh --version X.Y.Z --ref <commit>
   --tatana-appimage <ruta>` (sin `--deploy-de-trabajo`).

## 7. Contrato

No hay contrato client ↔ server nuevo: sin cambios de código ni de DTOs. Se usan tal cual las claves de
configuración existentes (`Auth__Mode`, `Auth__Local__BootstrapSuperadmins__0__*`, `Cors__AllowedOrigins__N`,
`MongoDb__*`, `Jwt__*`, `Storage__DataDirectory`, `Agent:AllowedOrigins` de Tatana) y los build-args del frontend
(`NEXT_PUBLIC_BACKEND_URL=https://factum.internal` en el plan A, `http://localhost:8080` en el B;
`NEXT_PUBLIC_AGENT_URL=http://localhost:8765` en los dos), como manda la SDD (D1, D5, D8).

## 8. Modo sin AVX (MongoDB 4.4) — urgente, PC de la fiscalía sin AVX (2026-10-20)

**Estado:** hecho y probado en la Mac. Sin cambios en `server/src`, `client/src` ni `agent-ui/src`.

### Archivos
- Nuevos: `deploy/fiscalia/comun/compose.sin-avx.yml` (`image: mongo:4.4.29`, healthcheck con `mongo --quiet --eval
  "db.adminCommand('ping').ok"`, y reemplaza el montaje de `/docker-entrypoint-initdb.d` por `./mongo-init-sin-avx`),
  `deploy/fiscalia/comun/mongo-init-sin-avx/01-usuario-app.js` (igual al de la nube pero con `_getEnv`: el shell viejo
  `mongo` no tiene `process.env`; si se usaba el `.js` de la nube, el init de 4.4 fallaba).
- `comun/_comun.sh`: `modo_sin_avx` (lee `FACTUM_MONGO_SIN_AVX=1` de `config/.env`), `mongo_shell` (`mongo`/`mongosh`)
  y `fc` suma `-f compose.sin-avx.yml` en ese modo → `--reparar`, backup, restaurar, diagnóstico y borrar-caso-prueba
  lo heredan sin flags.
- `comun/instalar.sh`: flag `--sin-avx`; sin el flag y sin AVX **pregunta** "Esta PC no tiene AVX. ¿Usar MongoDB 4.4?
  [s/N]" (sin terminal y sin `--si` muere indicando `--sin-avx`); carga `imagenes/mongo-4.4.tar` en vez de `mongo.tar`;
  escribe `FACTUM_MONGO_SIN_AVX=1` (instalación nueva) o lo pone con `env_poner` (`--reparar --sin-avx`); copia
  `compose.sin-avx.yml` y `mongo-init-sin-avx/` a la instalación; el modo es "pegajoso" en `--reparar`. Guarda:
  `--reparar --sin-avx` sobre un `datos/mongo` creado por Mongo 7 (`WiredTiger` presente) se niega y explica
  `--desinstalar --borrar-datos` (4.4 no abre archivos de 7).
- `servidor/instalar-servidor.sh`, `una-pc/instalar-una-pc.sh`: ya pasaban `"$@"`; solo se documentó `--sin-avx`.
- `comun/borrar-caso-prueba.sh`: JS que sirve en los dos shells (`process.env` o `_getEnv`), usa `$(mongo_shell)` y
  filtra el ruido de conexión del shell viejo.
- `comun/diagnostico.sh`: muestra `Base: MongoDB 4.4 (modo sin AVX)` / `MongoDB 7`; marca problema si la CPU no tiene
  AVX y la instalación usa 7.
- `backup.sh` / `restaurar.sh`: sin cambios (`mongodump`/`mongorestore` vienen en `mongo:4.4`, con `--config`,
  `--archive --gzip` y `--nsInclude`).
- `armar-paquete-fiscalia.sh`: lee el tag de 4.4 de `compose.sin-avx.yml` (fuente única, exige `mongo:4.4.x`), lo baja,
  verifica amd64, guarda `imagenes/mongo-4.4.tar` y copia el compose + `mongo-init-sin-avx/`.
- Docs: `docs/instalacion-fiscalia.md` (nueva §2.4 "PC sin AVX", §2.1, §3 y §9.4 actualizadas) y
  `deploy/fiscalia/dist/Guia-rapida-fiscalia.md` (sección "PC sin AVX" con los comandos del parche).

### Parche para la PC ya preparada (`deploy/fiscalia/dist/parche-sin-avx/`, 177 MB; también `.tar.gz` + `.sha256`)
`aplicar-parche.sh <carpeta del paquete>` verifica el `SHA256SUMS` del parche, copia `archivos/` encima del paquete
(scripts, `compose.sin-avx.yml`, `mongo-init-sin-avx/`, `imagenes/mongo-4.4.tar`, guía) y reescribe el `SHA256SUMS`
del paquete **solo en las líneas de esos archivos** (el resto sigue verificándose contra el original; copia
`SHA256SUMS.antes-del-parche`). `LEEME.txt` con los comandos exactos para `~/Factum-Fiscalia-v0.0.0-fiscalia-prueba`
(instalación nueva, `--reparar --sin-avx`, y el camino `--desinstalar --borrar-datos`), más un procedimiento a mano.

### Driver
MongoDB.Driver 3.4.0 soporta servidores 4.4 (wire version 9, dentro del rango de la familia 3.x). Confirmado
empíricamente: el backend conectó, autenticó `factum_app` (SCRAM-SHA-256) y respondió todos los endpoints probados.

### Pruebas en la Mac (proyecto `factum-sinavx-prueba` / `-repro`, carpetas en el scratchpad, puertos 13010/18090)
- Paquete de `dist/` copiado + parche aplicado → `shasum -c SHA256SUMS` OK; diff de rutas: solo 3 agregadas.
- Plan B `--sin-avx --si`: 10/10, `mongod --version` = **v4.4.29**, factum_app creado por `mongo-init-sin-avx`.
  `/health` ok, `/health/ready` `{"mongo":"ok"}`; login superadmin 200 → change-password 200 → login 200; perfil PUT 200;
  caso `PRUEBA-SINAVX` creado (201); `GET /api/cases`, `/stats` (con y sin rango), `/breakdown` (las 4 dimensiones),
  `/{id}`, `/{id}/events`, `/{id}/files`, `/api/admin/users`, `/api/catalogs` → 200, 0 excepciones en el log.
- `backup.sh --destino` OK (mongodump de 4.4) → `borrar-caso-prueba.sh --listar` / `<_id> --si` (cases 1, events 1,
  carpeta borrada) → `restaurar.sh --desde ... --si` → el caso volvió → borrado de nuevo. `diagnostico.sh`: todo OK,
  "Base: MongoDB 4.4 (modo sin AVX)". `--reparar` sin flag: siguió en 4.4 (pegajoso).
- Pregunta interactiva (copia con `es_linux`/`cpu_tiene_avx` forzados, en una pty con `script`): `s` → "Modo sin AVX";
  `n` → muere sin tocar nada; sin terminal → muere pidiendo `--sin-avx`.
- Guarda de `WiredTiger` de Mongo 7 con `--reparar --sin-avx` → se niega con el mensaje esperado.
- Camino de la fiscalía: instalación con el paquete **viejo** (Mongo 7) → desinstalar sin borrar, vaciar `datos/mongo`,
  parche, `--reparar --sin-avx` con el superadmin pendiente en `.env` → 4.4 sano, superadmin creado, login 200.
- `shellcheck -x` limpio en los scripts tocados y en `aplicar-parche.sh`.
- Limpieza: `--desinstalar --borrar-datos` de las dos instalaciones de prueba; ningún contenedor ajeno tocado; sin
  contenedores privilegiados.

### Pendiente / no obvio
- **Rosetta de esta Mac sí emula AVX**: Mongo 7 amd64 arrancó bajo Rosetta, así que la Mac no reproduce la falla; la
  prueba confirma que 4.4 funciona con Factum, no la ausencia de AVX. Que 4.4 no pide AVX es la documentación de
  MongoDB (el requisito empezó en 5.0). La prueba real es en la PC de la fiscalía.
- Si el Mongo 7 de la PC llegó a escribir `datos/mongo` antes de caerse, `--reparar --sin-avx` lo dice y hay que ir por
  `--desinstalar --borrar-datos` + instalar con `--sin-avx` (pide DNI/nombre del superadmin de nuevo).
- Plan A con `--sin-avx` no se probó de punta a punta (comparte `fc`, compose e instalador con el B; Caddy no cambia).
- **Paquete completo rearmado** (`armar-paquete-fiscalia.sh --version 0.0.0-fiscalia-prueba --deploy-de-trabajo`,
  exit 0): `dist/Factum-Fiscalia-v0.0.0-fiscalia-prueba/` (919 MB, 33 archivos; `.tar.gz` 832 MB + `.sha256`), con
  `imagenes/mongo-4.4.tar` (178 MB). Los 10 archivos del parche son **byte a byte iguales** a los del paquete rearmado.
