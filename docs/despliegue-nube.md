# Factum en la nube: guía paso a paso (VPS de DonWeb)

Esta guía te lleva desde "no tengo nada" hasta Factum publicado con HTTPS en un servidor de DonWeb, con deploy
automático al mergear a `main`, backups cifrados fuera del servidor y monitoreo. Está pensada para alguien que nunca usó
DonWeb ni administró un servidor Linux. Seguila en orden.

**Convenciones**

- **[A mano]**: lo hacés vos, en un panel web, en tu Mac o en el servidor.
- **[Repo]**: lo hace un script o un archivo que ya está en el repo; vos solo lo ejecutás.
- *(a confirmar en el panel)*: un paso del panel de DonWeb que no se pudo verificar al escribir esta guía. Puede que
  el botón se llame distinto o esté en otro lugar. Si algo no coincide, avisá para corregir la guía.
- "(a confirmar, consultado el 2026-10-06)": un límite o un precio de un tercero, tal como figuraba ese día en su web
  oficial. Pueden cambiar: revisalo antes de decidir.
- Los comandos van en bloques para copiar. Lo que está entre `<MAYÚSCULAS>` lo reemplazás vos (por ejemplo `<IP>` es la
  IP de tu servidor).
- **Nunca** pegues en el repo, en un issue ni en un chat una contraseña, un DNI real, una clave privada o el contenido de
  `factum.env`.

Referencias técnicas: la SDD `Refactorizaciones/despliegue-nube.md` y el mapa de `deploy/cloud/LEEME.md`.

---

## 0. Qué vas a tener al final

```
                    Internet (HTTPS, 443)
 Navegador ──────────────────────────────────┐
 (Chrome/Edge en la PC del perito)           │
     │                                       ▼
     │                         ┌───────────── VPS DonWeb (Ubuntu 24.04) ─────────────┐
     │                         │  Caddy :80/:443  (certificado Let's Encrypt, headers)│
     │                         │    ├─ /api, /health → backend :8080                  │
     │                         │    ├─ /tatana/updates → canal de Tatana (estático)   │
     │                         │    ├─ /descargas → archivos (instalador de Tatana)   │
     │                         │    └─ el resto → frontend :3000                      │
     │                         │  backend ──(red interna "datos")── Mongo 7 (con auth)│
     │                         │  /srv/factum/datos: Mongo, DOCX, certificados        │
     │                         │  cron 03:30: backup.sh ──────────────┐               │
     │                         └──────────────────────────────────────┼───────────────┘
     │ http://localhost:8765                    ▲ SSH (clave limitada) │ backup cifrado (age)
     ▼                                          │                      ▼
 Tatana (en la PC del perito, USB)   GitHub Actions ── GHCR      Cloudflare R2 (bucket)
                                     (merge a main: arma
                                      imágenes y despliega)
 UptimeRobot ── consulta /health/ready cada 5 min y te manda un mail si se cae
 healthchecks.io ── recibe un "ping" de cada backup; si no llega, te manda un mail
```

| Lo hacés vos [A mano] | Ya está en el repo [Repo] |
|---|---|
| Contratar el VPS en DonWeb, cargar tu clave SSH, abrir 22/80/443 | `deploy/cloud/scripts/preparar-servidor.sh` (Docker, firewall, usuarios, swap) |
| Elegir el dominio provisorio (hostname de DonWeb o DuckDNS) | `deploy/cloud/Caddyfile` (HTTPS automático, redirección, headers) |
| Generar los secretos y escribir `/srv/factum/config/factum.env` | `deploy/cloud/factum.env.example` (todas las claves, sin valores) |
| Elegir DNI, nombre y contraseña temporal de los superadmins | `deploy/cloud/docker-compose.yml` (solo Caddy expone puertos) |
| Cargar variables y secrets en GitHub | `.github/workflows/desplegar-produccion.yml` y `verificar.yml` |
| Crear el bucket de R2, la clave `age` y el check de healthchecks.io | `deploy/cloud/scripts/backup.sh`, `restaurar.sh`, `verificar-informes.sh` |
| Crear el monitor de UptimeRobot | `GET /health/ready` en el backend |
| Instalar Tatana en cada PC (una vez; después se actualiza solo) | `.github/workflows/tatana-release.yml` (arma, prueba, firma y publica el instalador) |
| Hacer un simulacro de restauración | `deploy/cloud/scripts/desplegar.sh`, `revertir.sh` (rollback) |

**Tiempo estimado:** entre medio día y un día la primera vez (lo que más tarda es esperar al panel, al DNS y leer con
calma). El simulacro de restauración (sección 13) es una hora más, otro día.

---

## 1. Antes de empezar

**Cuentas que vas a necesitar** [A mano]

| Cuenta | Para qué | ¿Hace falta ya? |
|---|---|---|
| DonWeb | El servidor (Cloud Server) | Sí |
| GitHub | Ya existe (el repo `senkuch4n/Factum`) | Sí |
| DuckDNS | Dominio provisorio, solo si DonWeb no te da un hostname usable (sección 4) | Quizás |
| Cloudflare | Bucket R2 para los backups | Antes de la sección 12 |
| UptimeRobot | Aviso por mail si Factum se cae | Antes de la sección 14 |
| healthchecks.io | Aviso por mail si el backup no corre | Antes de la sección 12 |

**Herramientas en tu Mac** [A mano]

```bash
# ssh y scp ya vienen con macOS.
brew install age gh        # age: cifrado de backups; gh: CLI de GitHub (probablemente ya lo tenés)
brew install nmap          # opcional: para verificar qué puertos quedan abiertos (sección 8)
```

**Datos que tenés que decidir (fuera del chat y fuera del repo)** [A mano]

- DNI (7 u 8 dígitos) y nombre completo de los dos superadmins (vos y Leo).
- Una contraseña temporal para cada uno (10 caracteres o más, sin el carácter `$`). Se cambia en el primer ingreso.
- El mail (o los mails) que reciben las alertas.
- El mail para Let's Encrypt (te avisa si un certificado no se puede renovar).

Anotalos en tu gestor de contraseñas. **Nada de esto va al repo ni a un chat.**

---

## 2. Contratar el Cloud Server en DonWeb

[A mano] Entrá a donweb.com y buscá los "Cloud Server" (servidores virtuales) *(a confirmar en el panel: el nombre
exacto del producto)*.

**Qué plan elegir** (decisión P1): como mínimo **2 vCPU, 4 GB de RAM y 40 GB de disco SSD**.

- Con 2 GB de RAM anda (el script agrega 2 GB de swap y podés bajar `FACTUM_MONGO_CACHE_GB=0.25`), pero entonces
  **no podés usar el plan B** de armar las imágenes en el servidor (`desplegar.sh --construir-local`): el build del
  frontend no entra en memoria.
- El precio y los planes vigentes los ves en la web de DonWeb; esta guía no los repite porque cambian.
- **Mongo 7 necesita un CPU con AVX.** Casi todos los servidores x86_64 actuales lo tienen, pero confirmalo con
  soporte de DonWeb antes de pagar si el panel no lo dice *(a confirmar en el panel)*. `preparar-servidor.sh` lo
  chequea y corta si falta.

**Al crear el servidor:**

1. Sistema operativo: **Ubuntu 24.04 LTS** de 64 bits (x86_64) *(a confirmar en el panel: cómo se elige la imagen)*.
2. Ubicación: Argentina (D6: los datos quedan en el país).
3. Clave SSH: si el panel permite cargar una clave pública al crear el servidor, cargá la tuya (la generás en la
   sección 3, paso 1; podés hacer ese paso primero) *(a confirmar en el panel)*. Si no lo permite, DonWeb te va a dar
   una contraseña de root: guardala, la usás una sola vez.
4. Cuando el servidor esté creado, anotá:
   - la **IP pública** (`<IP>`);
   - el **hostname** que asigna DonWeb, si asigna uno (por ejemplo algo tipo `vps-12345.donweb...`) *(a confirmar en
     el panel)*.
5. **Firewall del panel**: si DonWeb tiene un firewall propio para el servidor, dejá entrar solo **22/tcp, 80/tcp,
   443/tcp y 443/udp** *(a confirmar en el panel si existe y dónde está)*. El servidor tiene además su propio firewall
   (`ufw`), que configura el script.
6. **Huella de la "host key"** (para la sección 11): buscá en el panel una **consola web** (o "consola VNC") del
   servidor *(a confirmar en el panel)*. Entrá ahí como root y corré:

   ```bash
   ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub
   ```

   Anotá la línea que sale (empieza con `256 SHA256:...`). Es la "huella digital" del servidor: sirve para comprobar,
   más adelante, que GitHub se conecta a **tu** servidor y no a otro que se haga pasar por él.

---

## 3. Primer acceso y preparación del servidor

**3.1 Tu clave SSH** [A mano, en la Mac]. Si ya tenés una (`ls ~/.ssh/id_ed25519.pub`), usala. Si no:

```bash
ssh-keygen -t ed25519 -C "<TU-MAIL>"
# Enter para la ruta por defecto; poné una frase de contraseña (passphrase).
```

**3.2 Subir los scripts y entrar como root** [A mano, desde la raíz del repo en la Mac]

```bash
scp deploy/cloud/scripts/preparar-servidor.sh deploy/cloud/scripts/deploy-forzado.sh ~/.ssh/id_ed25519.pub root@<IP>:/root/
ssh root@<IP>
```

La primera vez, `ssh` pregunta si confiás en el servidor y muestra una huella `SHA256:...`: tiene que coincidir con la
que anotaste en la sección 2. Escribí `yes`.

**3.3 Correr la preparación** [Repo, en el servidor como root]

```bash
bash /root/preparar-servidor.sh --clave-admin /root/id_ed25519.pub
```

Pregunta antes de cada bloque (contestá `s`). Qué hace cada uno:

| Bloque | Qué hace |
|---|---|
| AVX | Chequea que el CPU tenga AVX (Mongo 7 lo exige). Si no, corta acá. |
| 1 | Actualiza el sistema e instala las actualizaciones automáticas de seguridad, `git`, `curl`, `age`, `rclone` y `ufw`. |
| 2 | Instala Docker Engine y `docker compose` desde el repositorio oficial de Docker. |
| 3 | Firewall `ufw`: deniega todo lo entrante salvo 22 (SSH), 80 y 443. |
| 4 | Crea 2 GB de swap si no hay. |
| 5 | Crea el usuario **`admin`** (con `sudo`) y le carga tu clave. Desde ahora entrás como `admin`, no como root. |
| 6 | Crea el usuario **`deploy`** (sin `sudo`, en el grupo `docker`): es el que corre Factum. |
| 7 | Crea `/srv/factum/` (repo, config, datos, backups, descargas, estado) con dueño `deploy`. |
| 8 | Instala `deploy-forzado.sh` en `/srv/factum/bin/` (lo usa el deploy automático, sección 11). |
| 9 | Apaga el login con contraseña y el de root por SSH. |

**Antes de contestar el bloque 9**, abrí **otra** terminal en la Mac y probá:

```bash
ssh admin@<IP>
sudo -n whoami # tiene que decir root sin pedir contraseña (no uses sudo -v: la imagen trae %admin ALL=(ALL) ALL y -v igual la pide)
uname -m       # tiene que decir x86_64
```

Si eso anda, volvé a la terminal de root y contestá `s` al bloque 9. **Si no anda, contestá que no** y revisá la clave:
si apagás el login con contraseña sin tener una clave que funcione, te quedás afuera (habría que entrar por la consola
web del panel).

**3.4 Desde acá** se trabaja así:

```bash
ssh admin@<IP>           # entrar
sudo -iu deploy          # pasar al usuario deploy (Factum corre con este usuario)
```

Podés borrar los archivos de `/root/`: `sudo rm /root/preparar-servidor.sh /root/deploy-forzado.sh /root/id_ed25519.pub`.

---

## 4. Dominio provisorio

El nombre del producto todavía no está definido, así que se arranca con un dominio **provisorio**. Cambiarlo después
está previsto (sección 15), pero obliga a redistribuir Tatana una vez: no lo cambies seguido.

**Opción A: el hostname de DonWeb** [A mano]. Si DonWeb asignó un hostname público al servidor *(a confirmar en el
panel si asigna uno y si es estable)*, verificá desde la Mac que apunta a tu IP:

```bash
dig +short <HOSTNAME-DE-DONWEB>     # tiene que devolver <IP>
```

**Opción B: DuckDNS** [A mano] (gratis, si la A no sirve):

1. Entrá a duckdns.org con tu cuenta de GitHub o Google.
2. Creá un subdominio (por ejemplo `factum-piloto`): queda `factum-piloto.duckdns.org`.
3. En el campo "current ip" poné `<IP>` y guardá ("update ip").
4. Verificá desde la Mac: `dig +short factum-piloto.duckdns.org` tiene que devolver `<IP>` (puede tardar unos minutos).

Elegí uno. De acá en adelante ese nombre es `<DOMINIO>` (sin `https://`).

---

## 5. Repo e imágenes en el servidor

Todo esto como `deploy` (`sudo -iu deploy`).

**5.1 Deploy key (para que el servidor pueda leer el repo privado)**

```bash
ssh-keygen -t ed25519 -f ~/.ssh/id_ed25519 -N "" -C "factum-vps-deploy-key"
cat ~/.ssh/id_ed25519.pub
```

[A mano] En GitHub: repo `senkuch4n/Factum` › **Settings › Deploy keys › Add deploy key**. Título: `VPS DonWeb`; pegá
la clave pública; **NO** marques "Allow write access" (solo lectura).

```bash
ssh -T git@github.com          # la primera vez pregunta por la huella de GitHub: yes
git clone git@github.com:senkuch4n/Factum.git /srv/factum/repo
```

(Si `git clone` se queja de que `/srv/factum/repo` no está vacía, es porque ya existe: está vacía, así que corré
`rmdir /srv/factum/repo` y repetí.)

**5.2 Login en GHCR** (el registro de imágenes de GitHub, privado)

[A mano] En GitHub: tu foto › **Settings › Developer settings › Personal access tokens › Tokens (classic) › Generate
new token (classic)**. Nombre: `factum-vps-ghcr`. Vencimiento: el que prefieras (anotá cuándo vence). Permiso: **solo
`read:packages`**. Copiá el token.

```bash
docker login ghcr.io -u <TU-USUARIO-DE-GITHUB>
# Password: pegá el token (no se ve al escribir)
```

Aviso: el token queda guardado en `~/.docker/config.json` del usuario `deploy`. Para rotarlo: generá uno nuevo, corré
de nuevo `docker login ghcr.io` y borrá el viejo en GitHub.

---

## 6. Configuración (`factum.env`)

```bash
cp /srv/factum/repo/deploy/cloud/factum.env.example /srv/factum/config/factum.env
chmod 600 /srv/factum/config/factum.env
nano /srv/factum/config/factum.env
```

Formato: `CLAVE=valor`, sin espacios alrededor del `=`. Un valor con espacios va entre comillas dobles
(`FACTUM_SUPERADMIN_0_NOMBRE="Nombre Apellido"`). **No uses el carácter `$`** en ningún valor.

| Clave | Qué poner | Cómo generarlo |
|---|---|---|
| `FACTUM_HOME`, `FACTUM_DATOS` | Dejá los valores del ejemplo | — |
| `FACTUM_REGISTRO` | `ghcr.io/senkuch4n` (en minúsculas) | — |
| `FACTUM_DOMINIO` | `<DOMINIO>`, sin `https://` ni barra | Sección 4 |
| `FACTUM_ACME_EMAIL` | Tu mail para Let's Encrypt | — |
| `FACTUM_JWT_SECRET` | Firma de las sesiones | `openssl rand -hex 64` |
| `FACTUM_JWT_EXPIRY_HOURS` | `8` | — |
| `FACTUM_MONGO_ROOT_PASSWORD` | Contraseña de `root` de Mongo (backups) | `openssl rand -hex 24` |
| `FACTUM_MONGO_APP_PASSWORD` | Contraseña de `factum_app` (la usa el backend). **Distinta** de la de root | `openssl rand -hex 24` |
| `FACTUM_MONGO_CACHE_GB` | `0.5` (o `0.25` con 2 GB de RAM) | — |
| `FACTUM_SUPERADMIN_0_DNI` / `_NOMBRE` / `_TEMPORAL` | Vos: DNI (7 u 8 dígitos), nombre entre comillas, contraseña temporal (10+ caracteres) | Sección 1 |
| `FACTUM_SUPERADMIN_1_*` | Leo, igual | Sección 1 |
| `FACTUM_BRANDING_NOMBRE` / `_LOGO` | Opcional: nombre y logo del login. Logo: subilo a `/srv/factum/config/branding/logo.png` y poné `branding/logo.png` | — |
| `FACTUM_BACKUP_DESTINO` | Dejalo como está hasta la sección 12 | Sección 12 |
| `FACTUM_BACKUP_AGE_RECIPIENT` | La clave **pública** de `age` | Ver abajo |
| `FACTUM_BACKUP_LOCALES` | `3` | — |
| `FACTUM_BACKUP_PING_URL` | Vacío hasta la sección 12 | Sección 12 |
| `FACTUM_AGENT_URL`, `FACTUM_TATANA_DESCARGA_URL` | Vacíos (solo los usa el plan B) | — |

Los `openssl rand` los podés correr en el mismo servidor y copiar el resultado.

**La clave de los backups se genera ahora** (cada deploy hace antes un backup cifrado, así que hace falta desde el
principio). En tu **Mac**, no en el servidor:

```bash
age-keygen -o ~/factum-backup.key
# Imprime "Public key: age1...": eso va en FACTUM_BACKUP_AGE_RECIPIENT.
```

`~/factum-backup.key` es la clave **privada**: guardala ya en dos lugares fuera de la Mac (sección 12, paso 2) y nunca
la subas al servidor salvo para restaurar.

**Si falta algo o quedó un `CAMBIAR`**, los scripts lo dicen antes de arrancar nada (con el nombre de la clave, nunca
con el valor).

---

## 7. Primer arranque

**7.1 Variable en GitHub** [A mano]. Repo › **Settings › Secrets and variables › Actions › pestaña Variables › New
repository variable**:

- `FACTUM_URL_PUBLICA` = `https://<DOMINIO>` (sin barra al final).

**Todavía no** cargues `FACTUM_DEPLOY_HABILITADO`.

**7.2 Publicar las imágenes** [A mano].

**La primera vez, `main` todavía no tiene `deploy/cloud/` ni el workflow:** promové `develop` a `main` con un PR
`develop → main` y mergealo **después** de cargar la variable del 7.1, porque el frontend la usa al armarse. El push a
`main` dispara el workflow solo. Si el clon del servidor (sección 5) se hizo antes de esta promoción, actualizalo con
`git -C /srv/factum/repo pull`.

Las veces siguientes, o para reintentar: Repo › **Actions › Desplegar producción › Run workflow** (rama `main`). Esperá
a que el job "Imágenes en GHCR" termine en verde (el job "Desplegar en el VPS" aparece salteado: es lo esperado).
Anotá el **SHA completo** del commit (40 caracteres; se ve en el run, o con `git rev-parse origin/main`).

Las imágenes se publican privadas en GHCR (paquetes `factum-backend` y `factum-frontend` de tu cuenta).

**7.3 Desplegar** [Repo, en el servidor como `deploy`]

```bash
/srv/factum/repo/deploy/cloud/scripts/desplegar.sh --tag sha-<SHA>
```

Qué vas a ver:

1. Checkout del commit y descarga de las imágenes.
2. Mongo arranca por primera vez y crea el usuario `factum_app`.
3. Caddy pide el certificado a Let's Encrypt (unos segundos).
4. El backend crea los superadmins iniciales.
5. Al final: `OK desplegado <sha8>`.

**Plan B** (si GHCR o Actions no andan): `desplegar.sh --construir-local` arma las imágenes en el servidor desde el
commit que tenga el checkout. Tarda bastante y necesita 4 GB de RAM.

**Si el certificado no sale** (el chequeo de salud falla y en los logs de Caddy hay errores de "challenge"):

- `dig +short <DOMINIO>` tiene que devolver `<IP>`.
- Los puertos 80 y 443 tienen que estar abiertos en el firewall del panel de DonWeb (sección 2) y en `ufw`
  (`sudo ufw status`).
- Mirá el log: `/srv/factum/repo/deploy/cloud/scripts/compose.sh logs --tail 100 caddy`.

Con el problema resuelto, corré de nuevo el mismo `desplegar.sh --tag ...`.

Tip: agregá los scripts al `PATH` del usuario `deploy` para no escribir la ruta entera:

```bash
echo 'export PATH="/srv/factum/repo/deploy/cloud/scripts:$PATH"' >> ~/.bashrc && source ~/.bashrc
```

---

## 8. Verificar HTTPS y lo que queda expuesto

[A mano] Desde la Mac:

```bash
# Candado: abrí https://<DOMINIO> en el navegador. Tiene que verse el login, sin avisos.
curl -I http://<DOMINIO>                 # 308 Permanent Redirect a https://
curl -I https://<DOMINIO>                # headers: strict-transport-security, x-content-type-options,
                                         # referrer-policy, x-frame-options, content-security-policy,
                                         # content-security-policy-report-only, permissions-policy
curl https://<DOMINIO>/health/ready      # {"status":"ok","mongo":"ok"}
curl -s -o /dev/null -w '%{http_code}\n' https://<DOMINIO>/swagger/index.html   # no tiene que mostrar la API
nmap -Pn -p- <IP>                        # solo 22, 80 y 443 abiertos (tarda unos minutos)
```

Mongo (27017) y el backend (8080) no son alcanzables desde afuera: no publican puertos, solo Caddy los ve por la red
interna de Docker.

---

## 9. Primer ingreso de los superadmins y alta de clientes

[A mano]

1. Entrá a `https://<DOMINIO>` con tu DNI y la contraseña temporal. Factum te obliga a cambiarla.
2. Leo hace lo mismo con la suya.
3. Andá a **`/admin/cuentas`** y dá de alta un cliente. El panel arma el mensaje con la dirección y la contraseña
   temporal para mandarle.

**Después del primer ingreso de los dos**, sacá los superadmins de la configuración (ya están en la base):

```bash
nano /srv/factum/config/factum.env      # dejá vacías las seis FACTUM_SUPERADMIN_*
compose.sh up -d backend
```

---

## 10. Tatana para la nube

Tatana sigue corriendo en la PC de cada perito (necesita el USB). Para Factum en la nube se distribuye como un
**instalador de Windows** (`Tatana-Setup-Windows.exe`) que no pide permisos de administrador, arranca solo al iniciar
sesión y **se actualiza solo** desde este servidor. Cada versión viaja con una firma digital que Tatana comprueba antes
de instalar nada: aunque alguien tomara el control del servidor, no podría colarle a las PCs un programa falso (la clave
de firma vive solo en GitHub). Cómo se publica una versión: sección 19.

Por seguridad, Tatana solo acepta pedidos de las páginas que tiene en su lista (`Agent:AllowedOrigins`). El instalador
ya trae `https://<DOMINIO>` (y los demás orígenes de `TATANA_ORIGENES`). El Tatana de la instalación local solo acepta
`localhost`.

**10.1 Dónde se descarga**: `https://<DOMINIO>/descargas/tatana/Tatana-Setup-Windows.exe` (con su `.sha256` al lado).
Lo publica el workflow de la sección 19; la primera vez hay que publicar una versión antes de poder instalar.

**10.2 Link en la web** [A mano]: en GitHub, variable `FACTUM_TATANA_DESCARGA_URL` =
`https://<DOMINIO>/descargas/tatana/Tatana-Setup-Windows.exe`. Se ve a partir del próximo deploy: si un perito no
tiene Tatana, o tiene uno que no acepta el dominio o que está por debajo de la versión mínima (sección 19.6), la web le
muestra ese link.

**10.3 En la PC del perito** [A mano]

1. Descargar `Tatana-Setup-Windows.exe` desde el link de la web.
2. Doble clic. Como el instalador no tiene firma de código (decisión D2), **Windows SmartScreen** muestra "Windows
   protegió su PC":
   1. tocá **Más información**;
   2. comprobá que diga "Aplicación: Tatana-Setup-…exe";
   3. tocá **Ejecutar de todas formas**.

   Si el navegador marca la descarga como "poco común", elegí **Conservar** (en Edge: los tres puntos de la descarga ›
   **Conservar** › **Mostrar más** › **Conservar de todas formas**).
3. El instalador no pregunta nada: instala en `%LOCALAPPDATA%\Programs\Tatana`, crea los accesos "Tatana" en el
   Escritorio y el menú Inicio, y abre Tatana. Desde ese momento arranca solo (oculto, en la bandeja) al iniciar sesión.
4. Abrí Factum desde la bandeja de Tatana (**Abrir Factum**) o desde el navegador.
5. **Permiso de red local**: Chrome y Edge pueden preguntar si la página puede "acceder a otros dispositivos o apps de
   tu red local" (o un texto parecido). Hay que **permitirlo**: es lo que deja a la web hablar con Tatana en
   `localhost`. Si alguien tocó "Bloquear": candado a la izquierda de la dirección › **Configuración del sitio** ›
   buscar el permiso de red local y ponerlo en **Permitir**, y recargar la página.

**10.4 Si la PC tenía Tatana portátil para la nube** (el zip de versiones anteriores): el instalador lo detecta,
avisa que lo va a reemplazar, lo detiene, saca su arranque automático, pasa su `appsettings.Local.json` a
`%APPDATA%\Tatana\` y guarda la carpeta vieja en `%LOCALAPPDATA%\Tatana\portable-anterior-<fecha>`. **La evidencia
no se toca**: Tatana instalado usa la misma carpeta de datos (`%LOCALAPPDATA%\Tatana\data`) y el ZIP del caso sigue en
`C:\Factum\Evidencia`. La ventana de Tatana muestra un aviso con la carpeta archivada: borrala cuando confirmes que
todo anda. Si había una grabación o captura en curso, terminala **antes** de instalar.

**10.5 Si la PC tiene la instalación local de Factum** (Docker en esa PC, con su Tatana portátil): el instalador
**se niega** y no toca nada. Las dos no pueden convivir (pelearían por el puerto 8765). Esa PC sigue con la instalación
local y se actualiza con `Actualizar Factum.bat`.

**10.6 Actualizaciones** (automáticas): Tatana busca una versión nueva al arrancar y cada 6 horas, la descarga en
silencio y la verifica. Cuando está lista avisa una vez ("Hay una versión nueva de Tatana") y la instala **al reiniciar
Tatana, al iniciar sesión o con "Reiniciar y actualizar"** (bandeja o ventana), **nunca** con una grabación, una subida
o un ZIP en curso. Si Tatana no puede verificar una descarga, la descarta y sigue con la versión que tiene.

**Navegadores soportados** (D10): **Chrome y Edge actualizados en Windows**. Firefox debería andar. Safari no está
soportado.

**Escape para una sola PC** (otro origen, otra carpeta de evidencia, etc.): crear o editar
`%APPDATA%\Tatana\appsettings.Local.json`, por ejemplo:

```json
{
  "Agent": {
    "AllowedOrigins": ["http://localhost:3000", "http://127.0.0.1:3000", "https://<DOMINIO>"]
  }
}
```

y reiniciar Tatana. Ese archivo **no lo toca ninguna actualización ni la desinstalación**. Ojo: si fija
`AllowedOrigins`, las actualizaciones ya no pueden cambiar esa lista (por ejemplo, en un cambio de dominio): la ventana
de Tatana lo avisa. Si el JSON tiene un error, Tatana lo ignora, arranca igual y muestra el error en su ventana.
**No uses variables de entorno** para esto (`Agent__AllowedOrigins__0=...`): .NET combina las listas por posición y
terminás con una mezcla difícil de entender. Tatana rechaza `*` y cualquier valor con path (`https://x.com/algo`).

**Desinstalar**: Configuración de Windows › Aplicaciones › Tatana › Desinstalar. Borra el programa; **no** borra
`%LOCALAPPDATA%\Tatana\data`, `C:\Factum\Evidencia` ni `%APPDATA%\Tatana`.

**Respaldo manual** (solo si el workflow no estuviera disponible): `deploy/cloud/armar-tatana-nube.sh` sigue armando un
portátil en la Mac, pero está retirado para la nube: no se actualiza solo.

---

## 11. Deploy automático al mergear a `main` (D9)

Cada merge `develop` → `main` arma las imágenes y, si está habilitado, entra al servidor por SSH y corre
`desplegar.sh --tag sha-<commit>`. La clave SSH de GitHub **solo** puede pedir eso (o `estado`): está atada a
`deploy-forzado.sh` en el servidor.

**11.1 Clave dedicada** [A mano, en la Mac]

```bash
ssh-keygen -t ed25519 -f ~/factum-ci -C factum-ci -N ""
cat ~/factum-ci.pub
```

**11.2 Autorizarla en el servidor, limitada** [A mano, como `deploy`]: agregá **una sola línea** a
`~/.ssh/authorized_keys` (reemplazá `<CLAVE-PUBLICA>` por el contenido completo de `factum-ci.pub`):

```bash
echo 'command="/srv/factum/bin/deploy-forzado.sh",no-port-forwarding,no-X11-forwarding,no-agent-forwarding,no-pty <CLAVE-PUBLICA>' >> ~/.ssh/authorized_keys
```

**11.3 Huella del servidor** [A mano, en la Mac]

```bash
ssh-keyscan -t ed25519 <IP> > ~/factum-known-hosts
ssh-keygen -lf ~/factum-known-hosts      # la huella SHA256 tiene que ser IGUAL a la de la sección 2
```

Si no coincide, **no sigas**: algo se interpone entre tu Mac y el servidor.

**Si SSH no escucha en el puerto 22** (o sea, si vas a cargar `DEPLOY_PORT` con otro valor): la línea de
`DEPLOY_KNOWN_HOSTS` tiene que empezar con `[<IP>]:<puerto>` en vez de `<IP>` a secas; si no, `ssh` no la
reconoce y el job falla con "Host key verification failed". `ssh-keyscan` ya la escribe así si le pasás el puerto:

```bash
ssh-keyscan -t ed25519 -p <PUERTO> <IP> > ~/factum-known-hosts
cat ~/factum-known-hosts                 # tiene que verse: [<IP>]:<PUERTO> ssh-ed25519 AAAA...
ssh-keygen -lf ~/factum-known-hosts      # misma comprobación de huella que arriba
```

Usá exactamente el mismo `<IP>` (o nombre) que cargues en `DEPLOY_HOST`.

**11.4 Secrets y variables en GitHub** [A mano]: repo › **Settings › Secrets and variables › Actions**.

| Nombre | Pestaña | Valor |
|---|---|---|
| `DEPLOY_SSH_KEY` | Secrets | Contenido completo de `~/factum-ci` (la **privada**, con las líneas BEGIN/END) |
| `DEPLOY_KNOWN_HOSTS` | Secrets | Contenido de `~/factum-known-hosts` |
| `DEPLOY_HOST` | Secrets | `<IP>` |
| `DEPLOY_PORT` | Variables | `22` (opcional) |
| `DEPLOY_USER` | Variables | `deploy` (opcional) |
| `FACTUM_URL_PUBLICA` | Variables | Ya cargada (sección 7) |
| `FACTUM_AGENT_URL` | Variables | Opcional; por defecto `http://localhost:8765` |
| `FACTUM_TATANA_DESCARGA_URL` | Variables | Sección 10.2 |
| `FACTUM_DEPLOY_HABILITADO` | Variables | `true`, recién en el paso 11.7 |

Cuando termines la prueba del paso 11.6, borrá la privada de la Mac (o guardala en tu gestor de contraseñas):
`rm ~/factum-ci`.

**11.5 Aprobación manual (opcional, P3)**: el job usa el environment `production`. En repos privados, exigir una
aprobación antes de desplegar depende del plan de GitHub de la cuenta (a confirmar, consultado el 2026-10-06). Se
decidió arrancar **sin** aprobación. Si más adelante el plan lo permite: Settings › Environments › `production` ›
Required reviewers.

**11.6 Probar la clave** [A mano, en la Mac, antes de borrarla]

```bash
ssh -i ~/factum-ci deploy@<IP> estado     # muestra la versión desplegada y los contenedores
ssh -i ~/factum-ci deploy@<IP> ls         # ERROR: comando no permitido
```

**11.7 Habilitar** [A mano]: variable `FACTUM_DEPLOY_HABILITADO` = `true` › Actions › Desplegar producción › Run
workflow, y seguí el log hasta ver `OK desplegado`.

**11.8 Desde ahora**: cada merge de `develop` a `main` despliega solo. Antes de tocar el servidor, el script:

- exige que el commit esté en `main`;
- verifica que el front se haya compilado para el dominio del servidor;
- hace un backup local.

**11.9 Si el job queda en rojo**: abrí el paso "Desplegar".

- `ROLLBACK a <sha8> OK`: la versión nueva no arrancó y **producción sigue con la anterior**. Mirá las líneas del
  backend que imprime arriba.
- `ROLLBACK FALLÓ`: entrá al servidor ya (sección 16).
- "el front se compiló para ...": `FACTUM_URL_PUBLICA` no coincide con `FACTUM_DOMINIO`. No se tocó nada.
- "hay otro deploy en curso": esperá y reintentá.

**11.10 Rollback manual**: `revertir.sh` vuelve a la versión anterior (y si lo corrés de nuevo, a la otra).

**11.11 Apagar el automático**: `FACTUM_DEPLOY_HABILITADO` = `false`. Los merges siguen publicando imágenes.

**Avisos**

- El usuario `deploy` está en el grupo `docker`, y eso **equivale a root** en el servidor. Lo que limita a la clave de
  GitHub es el `command=` de `authorized_keys`, no la falta de `sudo`.
- **Revocar la clave de CI**: borrá su línea de `~deploy/.ssh/authorized_keys` y el secret `DEPLOY_SSH_KEY`.
- Si cambia `deploy-forzado.sh` en el repo, la copia del servidor no se actualiza sola:
  `sudo install -m 755 -o root -g root /srv/factum/repo/deploy/cloud/scripts/deploy-forzado.sh /srv/factum/bin/`.
- **Minutos de Actions**: el repo es público, así que los runners estándar (también los de Windows que usa Tatana,
  sección 19) no consumen cupo. Se ve en Settings › Billing.
- **PRs de forks**: ningún workflow les pasa secrets. El deploy solo corre en `main` y la publicación de Tatana solo con
  el environment `tatana-release` (sección 19), que no se puede usar desde un PR.
- Los secrets y variables de Tatana (`TATANA_*`) están en la sección 19.

---

## 12. Backups

Todos los días a las 03:30 se hace un backup de Mongo y de los DOCX. Se cifra con tu clave `age` y se sube a un bucket
de Cloudflare R2. La clave privada **solo** la tenés vos.

**12.1 Cloudflare R2** [A mano] (decisión P2)

1. Creá una cuenta en cloudflare.com y entrá a **R2**. Puede pedir una tarjeta para habilitarlo, aunque uses solo la
   capa gratis *(a confirmar)*.
2. Capa gratis: 10 GB-mes de almacenamiento y sin costo de egreso (a confirmar, consultado el 2026-10-06).
3. Creá un bucket, por ejemplo `factum-backups`.
4. Creá un **token de API de R2** con permiso **Object Read & Write limitado a ese bucket**. Anotá el *Access Key ID*,
   el *Secret Access Key* y el *endpoint* (`https://<ACCOUNT-ID>.r2.cloudflarestorage.com`).

Ojo: así, una copia **cifrada** de los datos queda fuera de Argentina. Es la decisión P2.

**12.2 La clave `age`** (la generaste en la sección 6). Guardá `~/factum-backup.key` en **dos lugares fuera de la
Mac**, por ejemplo en el gestor de contraseñas y en un pendrive guardado. **Sin la clave privada no hay forma de
restaurar.**

**12.3 rclone en el servidor** [A mano, como `deploy`]

```bash
rclone config --config /srv/factum/config/rclone.conf
```

Respuestas: `n` (new remote) › nombre **`r2`** › tipo **`s3`** › provider **`Cloudflare`** › `env_auth` false ›
pegá el Access Key ID y el Secret › endpoint: el de R2 › el resto, por defecto › `q`.

```bash
chmod 600 /srv/factum/config/rclone.conf
rclone --config /srv/factum/config/rclone.conf lsd r2:factum-backups    # no tiene que dar error
```

En `factum.env`: `FACTUM_BACKUP_DESTINO=r2:factum-backups/factum`.

**12.4 healthchecks.io** [A mano]: creá una cuenta. El plan gratis ("Hobbyist") alcanza; la cantidad de checks está a
confirmar (consultado el 2026-10-06). Creá un check:

- **Period**: 1 día;
- **Grace**: 2 horas;
- avisos a tu mail.

Copiá la URL de ping (`https://hc-ping.com/...`) a `FACTUM_BACKUP_PING_URL`.

**12.5 Primera corrida a mano** [Repo]

```bash
backup.sh
ls -lh /srv/factum/backups/
rclone --config /srv/factum/config/rclone.conf ls r2:factum-backups/factum/diarios/
```

En healthchecks.io el check se pone en verde.

**12.6 Programarlo** [A mano, como `deploy`]: `crontab -e` y agregá:

```
30 3 * * * /srv/factum/repo/deploy/cloud/scripts/backup.sh >> /srv/factum/backups/backup.log 2>&1
```

**12.7 Qué se guarda**

- En el bucket:
  - `diarios/`: los backups de los últimos 8 días;
  - `semanales/`: la copia de cada domingo, de las últimas 4 semanas (29 días).
- En el servidor:
  - los 3 backups diarios más nuevos (`FACTUM_BACKUP_LOCALES`);
  - aparte, los 3 últimos `pre-deploy-*`, que son los que hace cada deploy antes de cambiar de versión.
- Si el disco pasa el 85 %, healthchecks.io te avisa.

---

## 13. Restauración y simulacro

**Cuándo restaurar**

- Se perdió el servidor (o hay que mudarse): restaurá el último backup del bucket en un servidor nuevo.
- Un deploy rompió datos: restaurá el `pre-deploy-*` local de antes de ese deploy. Un rollback (sección 11) **solo
  vuelve el código, no los datos**.

**Simulacro** [A mano]. Hacelo **una vez antes de tener clientes** y anotá la fecha:

1. Contratá un segundo servidor chico en DonWeb y seguí las secciones 2 a 6 en él. En el `factum.env`, dejá vacías las
   `FACTUM_SUPERADMIN_*` y poné el mismo `FACTUM_DOMINIO` solo si vas a mover el DNS. Para el simulacro conviene un
   subdominio aparte de DuckDNS.
2. Configurá `rclone` igual que en la sección 12.3.
3. Subí la clave privada solo por el rato que dura la restauración:
   `scp ~/factum-backup.key admin@<IP-NUEVA>:/tmp/ && ssh admin@<IP-NUEVA> 'sudo install -o deploy -m 600 /tmp/factum-backup.key /srv/factum/config/ && rm /tmp/factum-backup.key'`
4. Como `deploy`:

   ```bash
   rclone --config /srv/factum/config/rclone.conf ls r2:factum-backups/factum/diarios/   # elegí el más nuevo
   restaurar.sh --remoto diarios/<ARCHIVO>.tar.age --identidad /srv/factum/config/factum-backup.key
   ```

   El script:
   - descifra el backup y verifica que esté íntegro;
   - usa la versión que estaba desplegada cuando se hizo;
   - restaura Mongo y los DOCX;
   - levanta todo, corre `verificar-informes.sh` (todos los informes en **OK**);
   - **borra la clave privada** del servidor.
5. Entrá con tu DNI y tu contraseña **actual** (no la temporal).
6. Borrá el servidor del simulacro en el panel de DonWeb.

**Restaurar un `pre-deploy` en el mismo servidor**

```bash
ls /srv/factum/backups/                 # factum-AAAAMMDD-HHMM-pre-deploy-<sha8>.tar.age
revertir.sh                             # primero, volver a la versión de código anterior
restaurar.sh --archivo /srv/factum/backups/<ARCHIVO> --identidad /srv/factum/config/factum-backup.key --reemplazar
```

`--reemplazar` te pide escribir el dominio para confirmar, porque pisa la base actual. Los DOCX se extraen encima de los
que hay, sin borrar nada.

---

## 14. Monitoreo y logs

**UptimeRobot** [A mano]

1. Creá una cuenta.
2. "Add New Monitor" › tipo **HTTP(s)**:
   - URL: `https://<DOMINIO>/health/ready`;
   - intervalo: 5 minutos;
   - alertas: tus mails.
3. El plan gratis incluye monitores con intervalo de 5 minutos; la cantidad de monitores y demás límites están a
   confirmar (consultado el 2026-10-06).

`/health/ready` da error (503) si el backend no responde **o** si Mongo no responde. Te llega un mail al caer y otro
al volver.

**healthchecks.io**: te avisa si el backup no corrió (sección 12.4).

**Logs**

```bash
compose.sh logs --tail 200 backend
compose.sh logs --tail 100 caddy
compose.sh ps
df -h /srv                              # espacio en disco
```

- Cada contenedor guarda como mucho 5 archivos de 10 MB de log; no llenan el disco.
- **Qué no queda en los logs**: Caddy no tiene log de accesos (a propósito: las descargas llevan el token de sesión en
  la URL), el backend no loguea pedidos ni contraseñas, y los scripts nunca imprimen secretos. **No actives el access
  log de Caddy.**

---

## 15. Cambio de dominio

Cuando se defina el nombre definitivo. Se hace en tres fases, sin cortar el servicio.

**Fase A: Tatana con los dos dominios** (sin apuro, no hay que redistribuir nada)

1. En GitHub, variable `TATANA_ORIGENES` = `https://<PROVISORIO>,https://<DEFINITIVO>`. El primero es el que abre
   "Abrir Factum" y el primer canal de actualización: mientras el servidor siga en el provisorio, poné el provisorio
   primero.
2. Publicá una versión nueva de Tatana (sección 19). Las PCs la reciben solas desde el provisorio, y desde ahí aceptan
   los dos dominios y buscan actualizaciones en los dos.
3. Esperá a que las PCs actualicen. Para ver cuáles ya lo hicieron, mirá `agent_events.agent_version` (la versión de
   Tatana de cada PC que usó Factum): `"2.0.0"` significa **un Tatana anterior al instalador** (los viejos decían
   siempre 2.0.0), que no se actualiza solo y hay que reinstalar a mano (sección 10.3).

**Fase B: mudar el servidor** (cuando las PCs ya tienen el Tatana nuevo)

1. DNS: el `<DEFINITIVO>` apuntando a `<IP>` (registro A). Verificá con `dig +short <DEFINITIVO>`.
2. En el servidor: `FACTUM_DOMINIO=<DEFINITIVO>` en `factum.env`.
3. En GitHub: `FACTUM_URL_PUBLICA=https://<DEFINITIVO>` (y `FACTUM_TATANA_DESCARGA_URL` con el dominio nuevo).
4. *Run workflow* de "Desplegar producción". La guarda del deploy exige que los dos valores coincidan: si te olvidaste
   de uno, falla sin tocar nada.
5. Redirección del dominio anterior: crear `/srv/factum/repo/deploy/cloud/caddy-sitios/anterior.caddy` con el bloque de
   `deploy/cloud/caddy-sitios/LEEME.md` y correr `compose.sh up -d --force-recreate caddy`. La redirección cubre
   también `/tatana/updates/`: un Tatana que quedó con el provisorio primero sigue encontrando las actualizaciones.
6. UptimeRobot: cambiar la URL del monitor.

Los usuarios tienen que volver a iniciar sesión en el dominio nuevo: la sesión del navegador es por dominio.

**Fase C: limpiar** (semanas después, cuando `agent_events.agent_version` muestra que todas las PCs tienen una versión
con el definitivo): `TATANA_ORIGENES=https://<DEFINITIVO>`, publicá una versión de Tatana, y recién después borrá
`anterior.caddy` y `compose.sh up -d --force-recreate caddy`.

**Una PC que no actualizó Tatana**: en el dominio nuevo, la web le muestra "origen no permitido" con el link de
descarga (sección 10.2).

El renombre del producto en la web y en el informe es otra HU.

---

## 16. Problemas frecuentes y rotación de secretos

**El backend no arranca.** El backend valida su configuración al arrancar y, si falta algo, no arranca y dice qué
falta (nunca el valor). Para verlo: `compose.sh logs --tail 80 backend`. Mensajes típicos:

- `Jwt:Secret tiene que ser propio...`: `FACTUM_JWT_SECRET` corto.
- `necesita al menos un superadmin activo`: base vacía sin `FACTUM_SUPERADMIN_*`.
- `...:Dni="..." no es válido`: un DNI que no tiene 7 u 8 dígitos.
- `...TemporaryPassword tiene N caracteres; el mínimo es 10`.

**El certificado no sale**: sección 7 ("Si el certificado no sale").

**Disco lleno**

```bash
df -h /srv
du -sh /srv/factum/*
docker system df
docker image prune          # imágenes sin uso; NO uses "docker volume prune" ni borres /srv/factum/datos
```

**Mongo no responde**: `compose.sh ps` y `compose.sh logs --tail 100 mongo`. Si dice algo de AVX, el CPU no sirve
(sección 2). Si se reinició por memoria, bajá `FACTUM_MONGO_CACHE_GB` y `compose.sh up -d`.

**Deploy en rojo**: sección 11.9.

**Rotar secretos** (como `deploy`; después de cada cambio en `factum.env`, `compose.sh up -d`):

| Secreto | Cómo |
|---|---|
| `FACTUM_JWT_SECRET` | Nuevo valor + `compose.sh up -d backend`. **Cierra todas las sesiones**. |
| Contraseña de `factum_app` | Ver abajo |
| Contraseña de `root` de Mongo | Ver abajo |
| Token de GHCR | Sección 5.2 |
| Deploy key del repo | Borrarla en GitHub › Deploy keys, generar otra (sección 5.1) |
| Clave de CI | Sección 11 (línea nueva en `authorized_keys`, secret nuevo, borrar la vieja) |
| Clave SSH de publicación de Tatana | Sección 19.2 (línea nueva en `~tatana-pub/.ssh/authorized_keys`, secret `TATANA_PUB_SSH_KEY` nuevo, borrar la vieja) |
| Clave de firma de Tatana | Sección 19.7 (**rotación en dos pasos**: no se cambia de golpe) |
| Token de R2 | Crear otro en Cloudflare, `rclone config` de nuevo, borrar el viejo |
| Clave `age` | Generar otra y cambiar `FACTUM_BACKUP_AGE_RECIPIENT`. **No borres la vieja**: los backups anteriores solo se abren con ella |

Contraseñas de Mongo (generá la nueva con `openssl rand -hex 24`):

```bash
compose.sh exec mongo mongosh -u root -p --authenticationDatabase admin
# (te pide la contraseña de root actual)
use factum
db.changeUserPassword("factum_app", "<NUEVA>")
use admin
db.changeUserPassword("root", "<NUEVA-ROOT>")      # solo si rotás también la de root
exit
```

Después actualizá `FACTUM_MONGO_APP_PASSWORD` (y `FACTUM_MONGO_ROOT_PASSWORD`) en `factum.env` y corré
`compose.sh up -d`.

**Actualizaciones del sistema y reinicio**: las de seguridad se instalan solas. Para todo lo demás:
`sudo apt update && sudo apt upgrade`. Para reiniciar: `sudo reboot`. Los contenedores vuelven solos
(`restart: unless-stopped`). Después, `curl https://<DOMINIO>/health/ready` y `verificar-informes.sh`.

---

## 17. Plan de salida (D14)

**Señales de que el servidor quedó chico**

- RAM o disco por encima del 80 % (`free -h`, `df -h /srv`);
- CPU alta todo el tiempo (`top`);
- caídas que avisa UptimeRobot.

**Opciones**

- **Agrandar el plan en DonWeb**: desde el panel *(a confirmar en el panel si se puede sin reinstalar)*.
- **Mudarse a otro servidor** (de DonWeb o de otro proveedor): secciones 2 a 6 en el nuevo, restaurar el último backup
  (sección 13) y apuntar el DNS. El objetivo es hacerlo en **horas**, no en días. El mismo compose corre en cualquier
  VPS Linux x86_64 con Docker.

---

## 18. Datos personales

- Los datos de producción (casos, cuentas, informes) quedan en Argentina, en el servidor de DonWeb (D6).
- Los backups cifrados quedan en Cloudflare R2, fuera del país, cifrados con una clave que solo tenés vos (decisión P2).
- La evidencia y el ZIP de cada caso nunca suben al servidor: quedan en la PC del perito.
- Esta guía no es asesoramiento legal sobre protección de datos personales (Ley 25.326).

---

## 19. Publicar una versión de Tatana

El instalador de Tatana para la nube lo arma, lo prueba, lo firma y lo publica el workflow
`.github/workflows/tatana-release.yml`. **Nunca se publica solo**: lo disparás vos con un tag o con "Run workflow". Las
releases salen **solo de commits que están en `main`** (P3); desde otras ramas solo hay ensayo.

```
 tag tatana-vX.Y.Z (o Run workflow)
   → preparar (versión, orígenes, chequeos) → payload (agente + herramientas, ubuntu)
   → instalador (NSIS, windows) → firmar (clave Ed25519, environment tatana-release)
   → prueba (windows: migra un portátil, instala, actualiza, desinstala; comprueba que no se pierde evidencia)
   → publicar (SSH como tatana-pub al VPS + GitHub Release)
```

### 19.1 Clave de firma (una vez) [A mano, en la Mac]

```bash
mkdir -p ~/Seguro && chmod 700 ~/Seguro
node ops/tatana/generar-clave-firma.mjs --key-id tatana-2026-10 --salida ~/Seguro/tatana-firma.pem
```

El script se niega a escribir la clave dentro de un repo y la deja con permisos `0600`. Imprime tres cosas:

1. Una línea `{ keyId: 'tatana-2026-10', spkiDerBase64: '…' },` (la clave **pública**). Pegala dentro de
   `TRUSTED_UPDATE_KEYS` en `agent-ui/src/main/updater/trusted-keys.ts` y commiteala (en un PR a `develop`, como
   cualquier cambio). Sin esa línea, Tatana no acepta ninguna actualización.
2. El comando para cargar la **privada** como secret: `gh secret set TATANA_FIRMA_CLAVE_PRIVADA --env tatana-release < ~/Seguro/tatana-firma.pem`
   (primero creá el environment, paso 19.3).
3. `gh variable set TATANA_FIRMA_KEY_ID --body tatana-2026-10`.

**Guardá dos copias de `tatana-firma.pem` fuera de GitHub**: una en tu gestor de contraseñas y otra en un medio offline
(un pendrive guardado). Después borrala de la Mac. Si se pierde la clave, las PCs **no** pueden recibir más
actualizaciones: hay que reinstalar Tatana a mano en cada una (sección 19.7).

### 19.2 Usuario y clave de publicación en el VPS (una vez)

GitHub sube los archivos con un usuario aparte, **`tatana-pub`**, que **no** está en el grupo `docker` (no puede tocar
los contenedores) y cuya única clave solo puede correr `publicar-tatana-forzado.sh`. Ese script valida el paquete
(nombres, SHA-256, versión de `latest.yml`), rechaza una versión igual o menor que la publicada y publica con renombres
atómicos en `/srv/factum/tatana-updates` (`https://<DOMINIO>/tatana/updates/`) y
`/srv/factum/descargas/tatana/Tatana-Setup-Windows.exe`. Guarda los 5 últimos instaladores.

[A mano, en la Mac] copiá los scripts al servidor:

```bash
scp deploy/cloud/scripts/preparar-servidor.sh deploy/cloud/scripts/publicar-tatana-forzado.sh admin@<IP>:/tmp/
```

[A mano, en el servidor, como `admin`] corré **solo** el bloque del canal de Tatana:

```bash
sudo bash /tmp/preparar-servidor.sh --solo-tatana
```

[A mano, en la Mac] la clave de publicación:

```bash
ssh-keygen -t ed25519 -f ~/factum-tatana-pub -C factum-tatana-release -N ""
cat ~/factum-tatana-pub.pub
```

[A mano, en el servidor, como `admin`] autorizala con **una sola línea** (reemplazá `<CLAVE-PUBLICA>` por el contenido
completo de `factum-tatana-pub.pub`):

```bash
echo 'command="/srv/factum/bin/publicar-tatana-forzado.sh",restrict <CLAVE-PUBLICA>' | sudo tee -a /home/tatana-pub/.ssh/authorized_keys
```

Probala desde la Mac:

```bash
ssh -i ~/factum-tatana-pub tatana-pub@<IP> estado    # "ninguna" (o la versión publicada)
ssh -i ~/factum-tatana-pub tatana-pub@<IP> ls        # ERROR: comando no permitido
```

Después redesplegá (o `compose.sh up -d --force-recreate caddy`) para que Caddy tome el Caddyfile y el volumen nuevos.
Si `publicar-tatana-forzado.sh` cambia en el repo, la copia del servidor no se actualiza sola: repetí el `scp` y
`sudo bash /tmp/preparar-servidor.sh --solo-tatana`.

### 19.3 Environment, secrets y variables en GitHub (una vez) [A mano]

1. Repo › **Settings › Environments › New environment** › `tatana-release`. En **Deployment branches and tags**:
   **Selected branches and tags** › agregar la rama `main` y la regla de tags `tatana-v*`. Así, ni un PR ni otra rama
   pueden leer la clave de firma.
2. En ese environment, **Environment secrets**:

| Nombre | Valor |
|---|---|
| `TATANA_FIRMA_CLAVE_PRIVADA` | El contenido completo de `tatana-firma.pem` (paso 19.1, con `gh secret set ... --env tatana-release`) |
| `TATANA_PUB_SSH_KEY` | El contenido completo de `~/factum-tatana-pub` (la **privada**, con las líneas BEGIN/END) |

3. **Settings › Secrets and variables › Actions › Variables** (del repo):

| Nombre | Valor |
|---|---|
| `TATANA_FIRMA_KEY_ID` | `tatana-2026-10` (el `key_id` vigente) |
| `TATANA_ORIGENES` | `https://<DOMINIO>` (o varios separados por coma, en orden; si falta, se usa `FACTUM_URL_PUBLICA`) |
| `TATANA_PUB_USER` | `tatana-pub` (opcional) |
| `TATANA_PUBLICAR_HABILITADO` | `true` cuando el VPS esté listo (paso 19.2). Mientras no sea `true`, el workflow arma y prueba pero no publica |

`DEPLOY_HOST`, `DEPLOY_KNOWN_HOSTS` y `DEPLOY_PORT` son los de la sección 11. Borrá `~/factum-tatana-pub` de la Mac
después de cargar el secret (o guardalo en el gestor de contraseñas).

### 19.4 Ensayo (sin publicar)

- Cada PR a `develop` o `main` que toque el armado o el actualizador corre el workflow en **modo ensayo** (versión
  `0.9.0`).
- A mano: **Actions › Tatana release › Run workflow**, con una versión y **sin** tildar "publicar", desde cualquier rama.

El ensayo firma con una clave descartable que se genera y se borra dentro del run (nunca usa los secrets) y corre la
misma prueba de Windows. No publica nada. Revisá el resumen del job "Prueba en Windows".

### 19.5 Publicar

1. Que el cambio esté en `main` (merge de `develop` → `main`).
2. Elegí la versión: **mayor** que la publicada (`ssh ... tatana-pub@<IP> estado` o el último release `tatana-v*`), en
   formato `X.Y.Z` (sin `v`, sin `-beta`). La primera es la **1.4.0**.
3. Una de dos:
   - tag anotado desde `main` (el mensaje del tag son las notas de la versión):

     ```bash
     git fetch origin && git tag -a tatana-v1.4.0 origin/main -m "Tatana 1.4.0: <qué cambia>" && git push origin tatana-v1.4.0
     ```

   - **Actions › Tatana release › Run workflow**, rama `main`, versión `1.4.0`, tildar **publicar** y, si querés,
     notas.
4. Seguí el run. Si la prueba de Windows falla, **no se publica nada**. Si pasa, el job "Publicar" sube los archivos,
   comprueba desde afuera que `https://<DOMINIO>/tatana/updates/tatana-update.json` es el firmado y crea el GitHub
   Release `tatana-v1.4.0` (archivo histórico).
5. Comprobalo vos:

   ```bash
   curl -fsS https://<DOMINIO>/tatana/updates/tatana-update.json -o /tmp/tatana-update.json
   node ops/tatana/verificar-manifiesto.mjs --manifiesto /tmp/tatana-update.json
   ```

Las PCs la reciben solas en las próximas horas (al arrancar Tatana o cada 6 h) y la instalan al reiniciar Tatana o al
iniciar sesión.

**Revertir**: no se puede publicar una versión menor (ni repetir una): las PCs la rechazarían igual. Para volver atrás,
publicá una **X.Y.Z+1** desde el commit anterior (un revert en `main`).

### 19.6 Versión mínima (`FACTUM_TATANA_VERSION_MINIMA`)

En `factum.env` del servidor, `FACTUM_TATANA_VERSION_MINIMA=X.Y.Z` hace que la web **no inicie capturas** desde una PC
con un Tatana menor y le muestre al perito cómo actualizar. Vacío = no bloquea (así arranca).

- Un Tatana anterior al instalador (portátil) **siempre** queda por debajo de cualquier mínimo y **no se actualiza
  solo**: fijá el mínimo recién cuando las PCs ya tienen el instalador (mirá `agent_events.agent_version`; `"2.0.0"` =
  anterior al instalador). Si no, los peritos no pueden capturar hasta que reinstalen a mano.
- Se aplica con `compose.sh up -d backend`. Un valor que no sea `X.Y.Z` hace que el backend no arranque (y lo dice en
  el log).

### 19.7 Rotar o perder la clave de firma

**Rotar** (por ejemplo, una vez por año o si la clave pudo filtrarse), en dos versiones:

1. Generá la nueva (`generar-clave-firma.mjs --key-id tatana-AAAA-MM`) y agregá su línea a `trusted-keys.ts` **sin
   sacar la vieja**. Publicá una versión firmada todavía con la vieja.
2. Esperá a que las PCs actualicen (`agent_events.agent_version`).
3. Cambiá el secret `TATANA_FIRMA_CLAVE_PRIVADA` y la variable `TATANA_FIRMA_KEY_ID` a la nueva y publicá otra versión.
4. Sacá la vieja de `trusted-keys.ts` en la versión siguiente.

**Si la clave se filtró**: hacé la rotación cuanto antes. Mientras tanto, quien la tenga **además** necesita el control
del servidor para hacer algo con ella.

**Si se perdió** (y no hay copia): las PCs no aceptan nada firmado con otra clave. Generá una nueva, publicá una versión
y **reinstalá Tatana a mano** en cada PC (sección 10.3; la evidencia no se toca).

### 19.8 Soporte: verificar una PC

- Versión instalada: ventana de Tatana (barra lateral) o `http://localhost:8765/health` (`version`).
- Log del actualizador: `%APPDATA%\Tatana\logs\tatana-updater.log`; estado: `%APPDATA%\Tatana\update-state.json`.
- Log del instalador y la migración del portátil: `%APPDATA%\Tatana\logs\instalador.log`.

---

## Anexo A: probar el compose de producción en tu Mac (opcional)

Sirve para probar un cambio de infraestructura antes de mergearlo (D16). **No** usa tu Mongo de desarrollo ni
`dev-data/`. Usa un directorio temporal propio y otros puertos.

Los scripts están hechos para Linux (`flock`, `find -printf`). En la Mac se corren dentro de un contenedor Ubuntu con
el cliente de Docker que usa el Docker de tu Mac. Las rutas tienen que ser **las mismas** adentro y afuera, por los
bind mounts. Variables solo para esta prueba:

- `FACTUM_PROYECTO=factum-prod-test`: nombre de proyecto propio;
- `FACTUM_PUERTO_HTTP` y `FACTUM_PUERTO_HTTPS`: por ejemplo `18080`/`18443`, para no chocar con 80/443;
- `FACTUM_SALUD_IP`: la IP de `host.docker.internal` dentro del contenedor;
- `FACTUM_ENV_FILE`: un `factum.env` de prueba con `FACTUM_DOMINIO=localhost`. Caddy usa su CA interna y los scripts
  pasan `-k` a `curl` solo con `localhost`.

Con eso: `desplegar.sh --construir-local`, `backup.sh --solo-local`, `restaurar.sh` y `verificar-informes.sh`. Para
bajar todo: `docker compose -p factum-prod-test ... down -v` (solo ese proyecto) y borrar el directorio temporal.
