# Factum en la fiscalía — guía de instalación (Linux)

Guía para instalar Factum en persona en la red interna de una fiscalía, con PCs Linux y sin depender de
internet. Todo lo pesado viaja en el pendrive. La spec técnica es `Refactorizaciones/instalacion-fiscalia.md`.

Hay **dos planes**. Llevá preparados los dos: fallan por motivos distintos.

| | Plan A — red local | Plan B — una sola PC |
|---|---|---|
| Qué es | Un servidor corre la web, la API y la base; los peritos entran desde su PC | Todo en una PC (web, API, base y Tatana) |
| Dirección | `https://factum.internal` | `http://localhost:3000` |
| Varios peritos a la vez | Sí, cada uno desde su PC | Sí, pero de a uno en esa PC |
| Depende de | La red, la IP del servidor y que cada PC confíe en el certificado | Solo de esa PC |
| Script | `servidor/instalar-servidor.sh` + `perito/preparar-pc-perito.sh` | `una-pc/instalar-una-pc.sh` |

Los dos pueden estar instalados **en la misma PC** a la vez (carpetas, proyectos de Docker y puertos distintos).

---

## 1. Checklist imprimible

**Antes de salir (en la Mac):**

- [ ] Paquete armado: `deploy/fiscalia/armar-paquete-fiscalia.sh --version X.Y.Z --tatana-appimage <Tatana...AppImage>`
- [ ] El AppImage de Tatana para Linux es el del CI sobre `develop` (#91), descomprimido del artifact.
- [ ] Pendrive en **exFAT o ext4** (FAT32 no admite archivos de más de 4 GB), con la carpeta
      `Factum-Fiscalia-vX.Y.Z/` copiada entera (o el `.tar.gz` + su `.sha256`). ~2 GB libres extra.
- [ ] Esta guía impresa.

**En la fiscalía — plan A:**

- [ ] Servidor: Linux 64 bits con systemd, 4 GB RAM o más, 40 GB libres, IP fija o reservada. Docker no hace falta: lo trae el paquete (2.3).
- [ ] `sudo bash servidor/instalar-servidor.sh` → anotar **IP**, **DNI** y **contraseña temporal** del superadmin.
- [ ] Copiar `ca-factum-fiscalia.crt` (quedó en la carpeta del paquete) al pendrive.
- [ ] Anotar la **huella SHA-256** del certificado que mostró el instalador: ____________________
- [ ] Primer ingreso del superadmin en `https://factum.internal` (desde el servidor o la primera PC de perito)
      → cambiar la contraseña → crear las cuentas de los peritos.
- [ ] En cada PC de perito: `sudo bash perito/preparar-pc-perito.sh <IP>` → reabrir el navegador → abrir Tatana
      → "Preparar esta PC".
- [ ] Prueba de humo (sección 5) y borrar el caso de prueba.
- [ ] Primer backup (sección 8).

**En la fiscalía — plan B (si el A no anda, o además):**

- [ ] `sudo bash una-pc/instalar-una-pc.sh` → anotar DNI y contraseña temporal.
- [ ] Acceso "Factum" del Escritorio → primer ingreso → abrir Tatana → "Preparar esta PC".
- [ ] Prueba de humo y backup.

---

## 2. Requisitos

### 2.1 Servidor (plan A) y PC del plan B

- Linux de **64 bits (x86_64)**. Cualquier distro con Docker (Ubuntu 22.04+, Debian 12+, Mint 21+, Fedora 38+).
- Procesador con **AVX** (casi cualquier Intel/AMD desde 2012; algunos Celeron/Pentium/Atom no lo tienen):
  MongoDB 7 no arranca sin AVX. El instalador lo revisa. Para verlo antes: `grep -c avx /proc/cpuinfo` (tiene que
  dar más de 0). **Si da 0**, la PC igual sirve con el **modo sin AVX** (MongoDB 4.4): ver 2.4.
- **4 GB de RAM** como mínimo (8 GB recomendados si el plan B corre en la misma PC que Tatana capturando).
- **40 GB libres** en `/srv` (la evidencia ocupa mucho; los backups locales también).
- Plan A: **IP fija** o **reservada en el router** (DHCP) para el servidor. Si cambia, hay que volver a correr
  `preparar-pc-perito.sh` en cada perito. Los puertos **80 y 443** libres (sin Apache/nginx).
- Plan B: los puertos **3000 y 8080** libres.

### 2.2 PCs de los peritos (plan A) y PC del plan B

- Lo que pide Tatana para Linux (#91): **glibc 2.35 o posterior** — Ubuntu 22.04+, Debian 12+, Mint 21+,
  Pop!_OS/Zorin 22.04+, Fedora 36+ —, escritorio X11 o Wayland, x86_64.
- Un navegador: Chrome/Chromium o Firefox.
- Para la configuración inicial de Tatana ("Preparar esta PC") hace falta **internet** una vez: instala
  `usbmuxd` (iPhone) desde los repositorios de la distro. Si no hay internet, Android igual funciona.
- `curl` y `python3` (vienen en cualquier escritorio). Recomendado: `libnss3-tools` (Ubuntu/Debian) o `nss-tools`
  (Fedora), para que Chrome confíe en el certificado sin pasos manuales.

### 2.3 Docker (servidor y PC del plan B)

Las PCs de la fiscalía **no tienen Docker** y el internet es lento: **el paquete lo trae**, independiente de la
distro (los binarios estáticos oficiales de Docker Engine + el complemento `docker compose`, con versión y
SHA-256 fijos; ver `herramientas/VERSIONES`).

**No hace falta hacer nada aparte:** si `instalar-servidor.sh` o `instalar-una-pc.sh` no encuentran Docker,
preguntan *"¿Instalar Docker desde el paquete?"* y lo instalan. También se puede correr suelto antes:

```bash
sudo bash herramientas/instalar-docker-offline.sh
```

Qué hace: verifica el SHA-256 → copia `docker`, `dockerd`, `containerd`, `runc` y compañía a `/usr/local/bin` →
el complemento a `/usr/local/lib/docker/cli-plugins/docker-compose` → crea el grupo `docker` y agrega a tu
usuario (vale desde el próximo inicio de sesión; mientras, los scripts van con `sudo`) → instala
`docker.service` y `docker.socket` en `/etc/systemd/system`, los habilita y arranca (Docker queda arrancando
solo al prender la PC) → comprueba `docker info` y `docker compose version`.

- **Si ya hay un Docker instalado, no lo toca.** Si a ese Docker le falta `docker compose`, agrega solo el
  complemento (`--solo-compose`).
- **Si la distro trae Docker en su gestor de paquetes, también vale** (con internet):

  | Distro | Comando |
  |---|---|
  | Ubuntu 22.04+ / Mint 21+ | `sudo apt install docker.io docker-compose-v2` |
  | Debian 12 | `sudo apt install docker.io` y después `sudo bash herramientas/instalar-docker-offline.sh --solo-compose` (el `docker-compose` de Debian 12 es el viejo 1.x) |
  | Fedora | `sudo dnf install moby-engine docker-compose` |

  Después: `sudo systemctl enable --now docker`.
- Requisitos del Docker del paquete: x86_64 con **systemd** (cualquier distro de escritorio actual). Si la PC no
  tiene `iptables`, Docker se configura sin sus reglas de firewall (`/etc/docker/daemon.json`) y lo avisa; los
  puertos publicados igual funcionan.
- Para desinstalar ese Docker: `sudo systemctl disable --now docker.service docker.socket`, borrar
  `/etc/systemd/system/docker.{service,socket}`, `/usr/local/lib/docker/cli-plugins/docker-compose` y los
  binarios de `/usr/local/bin` (`docker dockerd containerd containerd-shim-runc-v2 ctr runc docker-init
  docker-proxy`). Las imágenes y contenedores están en `/var/lib/docker`.

---

### 2.4 PC sin AVX (modo sin AVX, MongoDB 4.4)

Si `grep -c avx /proc/cpuinfo` da **0**, MongoDB 7 se cae al arrancar (`Illegal instruction`). Factum puede usar
**MongoDB 4.4**, que no pide AVX: el backend (MongoDB.Driver 3.4) soporta servidores 4.x y no usa transacciones
ni funciones de Mongo 5+. El paquete trae las dos imágenes (`imagenes/mongo.tar` = 7, `imagenes/mongo-4.4.tar`).

- **Instalación nueva:** el instalador detecta que no hay AVX y pregunta *"Esta PC no tiene AVX. ¿Usar MongoDB
  4.4? [s/N]"*. Respondé `s`. También se puede pedir de entrada, en los dos planes:
  ```bash
  sudo bash una-pc/instalar-una-pc.sh --sin-avx          # plan B
  sudo bash servidor/instalar-servidor.sh --sin-avx      # plan A
  ```
- **Queda guardado** en `config/.env` (`FACTUM_MONGO_SIN_AVX=1`): `--reparar`, `backup.sh`, `restaurar.sh`,
  `diagnostico.sh` y `borrar-caso-prueba.sh` lo leen solos (usan `compose.sin-avx.yml` y el shell `mongo` en vez
  de `mongosh`). `diagnostico.sh` muestra `Base: MongoDB 4.4 (modo sin AVX)`.
- **Una instalación que ya falló por AVX** (Mongo 7 en bucle): `sudo bash <plan>/instalar-<plan>.sh --reparar
  --sin-avx`. Si el superadmin todavía no se había creado, lo crea y muestra su contraseña temporal. Si el
  instalador dice que *"la base la creó MongoDB 7"*, esa instalación nunca funcionó: `--desinstalar
  --borrar-datos` y volver a instalar con `--sin-avx`.
- Pasar una base **con casos** de Mongo 7 a 4.4 (o al revés) no se hace a mano con los archivos de `datos/mongo`:
  se hace con `backup.sh` en una y `restaurar.sh` en la otra (el backup es un `mongodump`, sirve entre versiones).
- **Paquete viejo sin el modo** (le falta `imagenes/mongo-4.4.tar`): aplicar el parche `parche-sin-avx/` (lo
  arma la Mac; ver su `LEEME.txt`): `bash parche-sin-avx/aplicar-parche.sh ~/Factum-Fiscalia-vX.Y.Z` copia los
  archivos nuevos al paquete y actualiza su `SHA256SUMS` solo en esas líneas.

## 3. Qué hay en el paquete

```
Factum-Fiscalia-vX.Y.Z/
  LEEME.txt                    resumen de una página
  instalacion-fiscalia.md      esta guía
  version.txt, origen.txt      versión y commit del que salió
  SHA256SUMS                   los instaladores verifican TODO el paquete antes de tocar nada
  imagenes/                    backend, frontend-servidor (plan A), frontend-una-pc (plan B), mongo (7),
                               mongo-4.4 (modo sin AVX, 2.4), caddy (.tar)
  herramientas/                Docker estático (docker-<versión>.tgz), docker-compose-linux-x86_64,
                               instalar-docker-offline.sh y VERSIONES (2.3)
  tatana/                      Tatana-...AppImage (si se armó con --tatana-appimage)
  comun/                       compose base, compose.sin-avx.yml (2.4), scripts de backup/restaurar/diagnóstico,
                               instalador común
  servidor/instalar-servidor.sh
  perito/preparar-pc-perito.sh
  una-pc/instalar-una-pc.sh
```

Se corre **siempre desde la carpeta del paquete** y **con `sudo bash`** (así anda aunque el pendrive no
conserve los permisos de ejecución). No edites archivos del paquete: el `SHA256SUMS` lo detecta.

Si llevás el `.tar.gz`: `sha256sum -c Factum-Fiscalia-vX.Y.Z.tar.gz.sha256 && tar -xzf Factum-Fiscalia-vX.Y.Z.tar.gz`.
Conviene copiar el paquete al disco de la PC (`~/Factum-Fiscalia-vX.Y.Z`) antes de instalar: es más rápido.

---

## 4. Plan A — red local

### 4.1 Servidor

```bash
cd ~/Factum-Fiscalia-vX.Y.Z
sudo bash servidor/instalar-servidor.sh
```

Pide el **DNI** (7 u 8 dígitos) y el **nombre** del primer superadmin y hace, en orden: verifica el paquete y
los requisitos (Docker, AVX, RAM, disco, puertos 80/443) → crea `/srv/factum-fiscalia/` (`config/` y `datos/`
con permisos 700, `config/.env` con 600 y **secretos generados en esta PC**) → carga las imágenes → levanta
todo y espera que esté sano → crea el superadmin y saca su contraseña temporal del `.env` → agrega
`127.0.0.1 factum.internal` a `/etc/hosts` → exporta el certificado raíz a `ca-factum-fiscalia.crt` (en la
carpeta del paquete y en `/srv/factum-fiscalia/`) → prueba `https://factum.internal/health` → muestra la IP.

**Anotá** la contraseña temporal (se muestra una sola vez), la IP y la huella SHA-256 del certificado.

Qué queda expuesto: solo Caddy en los puertos **80 y 443** (80 redirige a 443). La API, la web y MongoDB
no se publican fuera de Docker. El certificado sirve **solo para el nombre** `factum.internal`: entrar por IP
(`https://192.168.x.x`) da error de certificado, es normal.

Carpetas: `/srv/factum-fiscalia/{config,datos,backups,scripts}`. Otra carpeta: `--carpeta /otra/ruta`.

### 4.2 Cada PC de perito

Con el pendrive que ya tiene el `ca-factum-fiscalia.crt` en la carpeta del paquete:

```bash
cd ~/Factum-Fiscalia-vX.Y.Z
bash perito/preparar-pc-perito.sh 192.168.0.10 --simular   # opcional: muestra lo que haría
sudo bash perito/preparar-pc-perito.sh 192.168.0.10
```

Hace: `/etc/hosts` con `192.168.0.10 factum.internal` (reemplaza una línea anterior de ese nombre) → instala
el certificado en el sistema, en Chrome/Chromium (`~/.pki/nssdb`, si hay `certutil`) y en Firefox (perfiles
y `/etc/firefox/policies/policies.json`) → escribe `~/.config/Tatana/appsettings.Local.json` con
`Agent:AllowedOrigins` = `http://localhost:3000`, `http://127.0.0.1:3000`, `https://factum.internal` (conserva
las otras claves; deja una copia `.antes-de-factum-fiscalia`) → copia Tatana a `~/Applications/Tatana.AppImage`
→ prueba `https://factum.internal/health`.

Después: **cerrá y abrí el navegador**, abrí Tatana (doble clic en `~/Applications/Tatana.AppImage`, o desde el
menú) y tocá **"Preparar esta PC"**. Si Tatana ya estaba abierto, cerralo desde la bandeja y abrilo de nuevo.

Comprobá que la huella SHA-256 que muestra el script es **la misma** que anotaste en el servidor.

### 4.3 Primer superadmin

1. Entrá a `https://factum.internal` con el DNI y la contraseña temporal. La web pide cambiarla.
2. En **Administración → Cuentas**, creá una cuenta por perito (DNI + nombre; cada uno elige su contraseña al
   primer ingreso). Cada perito entra **siempre con su propia cuenta**: la autoría y la auditoría dependen de eso.
3. Opcional: identidad de la fiscalía (nombre, logo, textos del informe) en `/srv/factum-fiscalia/config/
   appsettings.Local.json` y el logo en `config/branding/`; se aplica con
   `sudo bash servidor/instalar-servidor.sh --reparar`.

Si se pierde la contraseña del único superadmin: ver `Auth:Local:ResetSuperadmin` en el README (se pone en
`config/appsettings.Local.json`, se corre `--reparar`, y después se borra).

---

## 5. Prueba de humo (plan A y B)

1. Entrá con una cuenta de perito.
2. Creá un caso con número de causa **`PRUEBA-INSTALACION`** (así el script de borrado lo reconoce).
3. Conectá un Android (y un iPhone si hay): tiene que aparecer en el asistente. Hacé una captura.
4. Probá la **cámara** del navegador (foto del perito/titular): si el navegador no la pide, el certificado no
   es de confianza (plan A) — ver 9.1.
5. Generá el informe y abrí el DOCX.
6. Borrá el caso de prueba **por su `_id`** (nunca con un filtro amplio):

```bash
sudo /srv/factum-fiscalia/scripts/borrar-caso-prueba.sh --listar      # muestra _id y número de causa
sudo /srv/factum-fiscalia/scripts/borrar-caso-prueba.sh <_id>
```

(Plan B: `/srv/factum-una-pc/scripts/...`.) El script se niega a borrar un caso cuyo número de causa no empiece
con `PRUEBA`. Borra el caso, sus eventos, sus sellos y su carpeta de evidencia.

---

## 6. Plan B — una sola PC

```bash
cd ~/Factum-Fiscalia-vX.Y.Z
sudo bash una-pc/instalar-una-pc.sh
```

Como el instalador de Windows pero en Linux: verifica el paquete y los requisitos (puertos 3000/8080) → crea
`/srv/factum-una-pc/` → secretos propios → carga las imágenes → levanta todo **solo en 127.0.0.1** → crea el
superadmin → acceso directo **"Factum"** en el Escritorio y en el menú (abre `http://localhost:3000`) → copia
Tatana a `~/Applications/Tatana.AppImage`.

Tatana en esta PC **no necesita configuración**: por defecto ya acepta `http://localhost:3000`. Abrilo y tocá
"Preparar esta PC". Después: primer ingreso (4.3, con `http://localhost:3000`), prueba de humo (5) y backup (8).

No hay certificados ni red: la cámara del navegador funciona porque `localhost` cuenta como seguro.

## 7. Cuándo pasar del plan A al B

Pasá al B si, después de 30 minutos, el A sigue sin andar por algo de **red**: los peritos no llegan al servidor
(`ping` falla, otra subred, firewall de la fiscalía que no podés tocar), la IP del servidor cambia sola, o un
navegador no hay forma de que confíe en el certificado (PC administrada por otra área).

Los datos se pueden mover entre planes: un backup del A se restaura en el B y al revés (sección 8). Si el A
se arregla después, backup del B → restaurar en el A.

---

## 8. Backups y restauración

**Backup** (pausa la web unos minutos para que base y evidencia queden coherentes):

```bash
sudo /srv/factum-fiscalia/scripts/backup.sh                                  # en /srv/factum-fiscalia/backups
sudo /srv/factum-fiscalia/scripts/backup.sh --destino /media/$USER/PENDRIVE  # a un pendrive / disco
```

Deja `factum-<plan>-AAAAMMDD-HHMMSS/` con la base (`mongo.archive.gz`), la evidencia, la configuración
(**incluido `config/.env` con los secretos**), la CA de Caddy (plan A) y `SHA256SUMS`. En la carpeta local se
conservan los 10 más nuevos (`FACTUM_BACKUP_CONSERVAR`); en un `--destino` externo no se borra nada.
**No está cifrado: guardá el pendrive bajo llave.**

`actualizar.sh` (sección 12) hace **siempre** un backup como este antes de tocar nada (backup previo) y dice dónde
quedó. Backup, restaurar, actualizar e `instalar-servidor.sh --reparar` **nunca corren a la vez**: el que llega
segundo dice "hay otra operación de Factum en curso" y no hace nada (candado `<carpeta>/.operacion.lock`).

Backup automático diario (opcional), con `sudo crontab -e`:

```
0 20 * * * /srv/factum-fiscalia/scripts/backup.sh >> /srv/factum-fiscalia/backups/backup.log 2>&1
```

**Restaurar** sobre una instalación ya hecha (misma PC u otra, mismo plan u otro):

```bash
sudo /srv/factum-una-pc/scripts/restaurar.sh --desde /media/$USER/PENDRIVE/factum-servidor-20261020-200000
```

Verifica el `SHA256SUMS`, pide escribir `RESTAURAR`, **hace un backup del estado actual**, reemplaza la base
(`mongorestore --drop`, solo `factum.*`), extrae la evidencia encima (no borra nada que ya esté), restaura
`appsettings.Local.json` y `branding/`, y levanta todo. **No** restaura `config/.env`: la instalación conserva
sus propios secretos (todos tienen que volver a iniciar sesión; la copia del `.env` del backup queda en
`config/.env.del-backup`). Las cuentas de usuario vienen con la base: después de restaurar valen las del backup.

**Restaurar en un servidor nuevo del plan A conservando el certificado** (para no reinstalarlo en cada perito):
instalá el servidor nuevo, restaurá, y después:

```bash
cd /srv/factum-fiscalia
sudo docker compose -p factum-fiscalia --env-file config/.env -f docker-compose.yml -f compose.servidor.yml stop caddy
sudo tar -C datos -xzf <backup>/caddy-data.tar.gz
sudo docker compose -p factum-fiscalia --env-file config/.env -f docker-compose.yml -f compose.servidor.yml up -d caddy
sudo cp datos/caddy-data/caddy/pki/authorities/local/root.crt ca-factum-fiscalia.crt
```

Si no, alcanza con volver a correr `preparar-pc-perito.sh` en cada perito con el `.crt` nuevo.

---

## 9. Problemas frecuentes

Primero: `sudo /srv/factum-fiscalia/scripts/diagnostico.sh` (o `factum-una-pc`). Muestra contenedores, salud,
certificado, IP, disco, backups y las últimas líneas de cada registro, **sin secretos**. Para mandarlo:
`sudo .../diagnostico.sh > diagnostico.txt 2>&1`.

### 9.1 "La conexión no es privada" / certificado no confiable (plan A)

- Volvé a correr `sudo bash perito/preparar-pc-perito.sh <IP>` y **cerrá del todo** el navegador (todas las
  ventanas) antes de abrirlo.
- Chrome/Chromium necesita `certutil` (paquete `libnss3-tools` / `nss-tools`). Sin internet, a mano:
  `chrome://certificate-manager` → **Certificados locales → Personalizados → Importar** →
  `ca-factum-fiscalia.crt` → marcar **"Confiar en este certificado para identificar sitios web"**.
- Firefox, a mano: **Ajustes → Privacidad y seguridad → Certificados → Ver certificados → Autoridades →
  Importar** → marcar "Confiar en esta CA para identificar sitios web".
- La **hora** de la PC tiene que estar bien (un reloj atrasado hace que el certificado parezca vencido).
- ¿Entraste por IP? Tiene que ser `https://factum.internal`.
- Si el servidor se reinstaló de cero, su certificado es **otro**: llevá el `.crt` nuevo a cada perito.

### 9.2 `factum.internal` no resuelve / no carga

- En la PC del perito: `getent hosts factum.internal` tiene que mostrar la IP del servidor. Si no, volvé a
  correr `preparar-pc-perito.sh <IP>`.
- `ping <IP del servidor>`. Si no responde: misma red/VLAN, cable, Wi-Fi de invitados (suele aislar equipos).
- `curl -vk --resolve factum.internal:443:<IP> https://factum.internal/health`: si esto anda y el navegador no,
  es el certificado (9.1) o un proxy configurado en el navegador (agregá `factum.internal` a las excepciones).
- Si la IP del servidor cambió: reservala en el router y corré `preparar-pc-perito.sh <IP nueva>` en cada PC.

### 9.3 Firewall en el servidor

Docker abre los puertos que publica por su cuenta (salta `ufw`), así que normalmente no hace falta nada. Si
igual no llegan: `sudo ufw status`; si está activo, `sudo ufw allow 80/tcp && sudo ufw allow 443/tcp`. En
Fedora (firewalld): `sudo firewall-cmd --permanent --add-service=http --add-service=https && sudo firewall-cmd --reload`.

### 9.4 Docker sin permisos / no arranca

- "este usuario no tiene permiso para usar Docker": corré los scripts con `sudo`.
- "Docker está instalado pero no está funcionando": `sudo systemctl enable --now docker`.
- Falta Docker o `docker compose`: `sudo bash herramientas/instalar-docker-offline.sh` (sección 2.3).
- Docker del paquete que no arranca: `sudo journalctl -u docker -n 50`. Si dice algo de `iptables`, instalá
  `iptables` o creá `/etc/docker/daemon.json` con `{"iptables": false, "ip6tables": false}` y
  `sudo systemctl restart docker`.
- MongoDB se reinicia en bucle y el registro dice `Illegal instruction` o menciona AVX: la CPU no tiene AVX
  (2.1). Reparalo con el modo sin AVX: `sudo bash <plan>/instalar-<plan>.sh --reparar --sin-avx` (2.4).

### 9.5 Tatana no conecta ("Tatana no está disponible" o `origin_not_allowed`)

- Plan A: `cat ~/.config/Tatana/appsettings.Local.json` tiene que incluir `"https://factum.internal"` en
  `Agent.AllowedOrigins` (junto a los `localhost`). Si no, volvé a correr `preparar-pc-perito.sh`. Ejemplo
  completo:

  ```json
  {
    "Agent": {
      "AllowedOrigins": ["http://localhost:3000", "http://127.0.0.1:3000", "https://factum.internal"]
    }
  }
  ```

- Tatana lee ese archivo **al arrancar**: cerralo desde la bandeja y abrilo de nuevo.
- Un JSON mal escrito no impide que Tatana arranque, pero se ignora: en la ventana de Tatana se ve el error.
- `curl http://localhost:8765/health` en la PC del perito tiene que responder.
- Plan B: no hace falta configurar nada; si existe ese archivo, tiene que incluir `http://localhost:3000`.

### 9.6 Actualización automática de Tatana

En esta fiscalía las PCs de los peritos usan **Tatana en Docker** (no el AppImage): se actualiza **desde el servidor
de la fiscalía**, no desde la nube. Cuando se aplica un paquete de actualización con Tatana nuevo (sección 12), el
servidor lo publica en `https://factum.internal/tatana/docker/` y cada PC lo toma sola en los próximos ~40 minutos,
**solo cuando no está capturando**; si la versión nueva no arranca, la PC vuelve sola a la anterior. Quién tiene qué
versión: `sudo /srv/factum-fiscalia/scripts/estado-peritos.sh`. En la PC: `./estado-tatana-docker.sh
--actualizaciones` (guía `docs/tatana-docker.md` §4).

- Una PC que no aparece en `estado-peritos.sh` o que dice "sin consultas recientes (!)": está apagada, sin red o sin
  el actualizador (las instaladas antes del actualizador: se suma con un comando, sección 12.5).
- El AppImage no se usa en esta fiscalía. Si alguna vez se instalara, busca actualizaciones en la nube de Factum: sin
  internet no encuentra nada y sigue funcionando igual.

### 9.7 El instalador falló a mitad de camino

Corregí lo que dice el error y corré el mismo instalador con `--reparar`: no toca la configuración ni los datos,
y si el superadmin no se había llegado a crear, lo crea y muestra la contraseña temporal.

---

## 10. Desinstalar

```bash
sudo bash servidor/instalar-servidor.sh --desinstalar        # para y borra contenedores; quita factum.internal de /etc/hosts
sudo bash una-pc/instalar-una-pc.sh --desinstalar
```

Los **datos, la configuración y los backups quedan** en `/srv/factum-fiscalia` (o `/srv/factum-una-pc`). Para
volver a levantar: el instalador con `--reparar`. Para borrar **todo** (pide escribir `BORRAR`; hacé un backup
antes): `--desinstalar --borrar-datos`. Las imágenes de Docker quedan cargadas; se borran con
`sudo docker image rm factum-backend:X.Y.Z factum-frontend:X.Y.Z factum-frontend-servidor:X.Y.Z`.

En una PC de perito: borrar la línea de `factum.internal` de `/etc/hosts`,
`/usr/local/share/ca-certificates/factum-fiscalia.crt` (+ `sudo update-ca-certificates --fresh`),
`/etc/firefox/policies/factum-fiscalia.crt` (y su entrada en `policies.json`), y `~/Applications/Tatana.AppImage`.

---

## 11. Armar el paquete (en la Mac)

```bash
deploy/fiscalia/armar-paquete-fiscalia.sh --version X.Y.Z [--ref develop] \
  --tatana-appimage ~/Downloads/Tatana-X.Y.Z-x86_64.AppImage
```

Sale de un `git archive` limpio de `--ref` (lo no commiteado no entra), construye las imágenes `linux/amd64`
(dos builds del frontend: uno con `https://factum.internal` y otro con `http://localhost:8080`; el emulado tarda),
las exporta (un `.tar` por imagen, verificado), baja Docker estático y el complemento compose con SHA-256 fijo, arma
`SHA256SUMS` y el `.tar.gz` en `deploy/fiscalia/dist/`. Los tags quedan en la Mac.

Con `--tatana-docker-release X.Y.Z` (o `--tatana-docker-canal <dir>`) el paquete de instalación también trae Tatana en
Docker y `instalar-servidor.sh` lo publica en el canal del servidor. El paquete de **actualización** se arma con
`--actualizacion` (sección 12.1).

---

## 12. Actualizar Factum y Tatana

El servidor (plan A) es **lo único que se actualiza a mano**; las PCs de los peritos toman Tatana del servidor solas.
Se hace en persona o por **AnyDesk**. Solo plan A (el plan B no se actualiza con este script en esta versión).

### 12.1 Armar el paquete de actualización (en la Mac)

Solo lleva lo que cambia (por AnyDesk varios GB no son viables):

```bash
# Factum nuevo (web + API) + Tatana en Docker de la release firmada tatana-vX.Y.Z:
deploy/fiscalia/armar-paquete-fiscalia.sh --actualizacion --version A.B.C --ref <ref> --tatana-docker-release X.Y.Z
# Solo scripts + canal (la web y la API quedan como están; re-etiqueta las imágenes que ya tiene el servidor):
deploy/fiscalia/armar-paquete-fiscalia.sh --actualizacion --version A.B.C --imagenes-de <versión instalada> \
  --tatana-docker-release X.Y.Z
```

- Tamaño: web ≈ 25 MB + API ≈ 45 MB + Tatana en Docker ≈ 290 MB (solo si se incluye). Con `--imagenes-de` y sin
  Tatana: menos de 1 MB. Mongo y Caddy no van, salvo `--incluir-base` (solo si cambió su versión en los compose).
- Tatana en Docker sale del **GitHub Release `tatana-vX.Y.Z`** (lo publica `tatana-docker.yml` desde `main`, firmado).
  El script lo verifica (firma, imagen y kit) antes de meterlo: un Tatana sin firma válida no entra al paquete.
- Resultado: `deploy/fiscalia/dist/Factum-Fiscalia-Actualizacion-vA.B.C.tar.gz` + `.sha256`.

### 12.2 Aplicarlo en el servidor

1. Copiar al servidor el `.tar.gz` y su `.sha256` (pendrive, o "Transferencia de archivos" de AnyDesk), **al disco**.
2. `sha256sum -c Factum-Fiscalia-Actualizacion-vA.B.C.tar.gz.sha256` y `tar -xzf Factum-Fiscalia-Actualizacion-vA.B.C.tar.gz`.
3. `cd Factum-Fiscalia-Actualizacion-vA.B.C && sudo bash actualizar.sh` (por AnyDesk con la terminal minimizada:
   `sudo bash actualizar.sh --si`).

Pasos que muestra: `[1/8]` verifica el paquete (SHA256SUMS) y la versión — `[2/8]` requisitos (imágenes, espacio) —
`[3/8]` **backup previo** (la web se pausa unos minutos) — `[4/8]` imágenes — `[5/8]` archivos (los de antes quedan en
`/srv/factum-fiscalia/actualizacion-anterior/`) — `[6/8]` levanta y espera que esté sano — `[7/8]` publica Tatana en
Docker — `[8/8]` limpieza y resumen. Todo queda registrado en `/srv/factum-fiscalia/registro/actualizar-*.log`.

- **Cortar AnyDesk no corta la actualización** (la terminal vive en el escritorio del servidor): reconectar y mirar.
- Paquete dañado, de otro plan, una versión **menor** o requisitos que faltan: corta en `[1/8]`/`[2/8]` **sin haber
  tocado nada** y dice por qué (p. ej. "este paquete necesita caddy:X: usá `--incluir-base` al armarlo"). Volver a
  una versión menor solo con `--permitir-version-menor` (pide escribir MENOR).
- Si Factum ya está en esa versión pero el paquete trae un Tatana más nuevo: solo publica Tatana (sin backup ni
  reinicio). `--solo-tatana` fuerza ese modo.
- `--sin-backup-previo` pide escribir `SIN BACKUP` (con `--si`, además `FACTUM_CONFIRMO_SIN_BACKUP=1`). No se usa.

### 12.3 Si la versión nueva no arranca

`actualizar.sh` **vuelve solo** a la versión anterior (imágenes, compose, Caddyfile y `config/.env` de antes) y
termina con error diciendo qué falló, "se volvió a vX", la ruta del **backup previo** y el comando:

```bash
sudo /srv/factum-fiscalia/scripts/restaurar.sh --desde /srv/factum-fiscalia/backups/factum-servidor-AAAAMMDD-HHMMSS
```

La base **no** se restaura sola: ese comando es solo si la versión nueva llegó a modificar datos antes de fallar. Tatana
no se publica. Si ni la anterior queda andando: `diagnostico.sh` y la sección 9.7.

### 12.4 Ver qué versión de Tatana tiene cada PC

```bash
sudo /srv/factum-fiscalia/scripts/estado-peritos.sh            # últimas 72 h (--horas N)
```

Lista cada PC (nombre e IP), la versión instalada, la disponible, cuándo consultó por última vez y el resultado
("al día", "esperando a que termine la captura", "la versión nueva no arrancó: volvió a la anterior"…). `(!)` = hace
más de 2 horas que no consulta. Sale del registro de Caddy del canal (`datos/caddy-logs/tatana.log`): solo se
registran las consultas de las PCs, no el uso de la web. `diagnostico.sh` muestra el resumen.

Si después de que todas las PCs se actualizaron la web exige un Tatana mínimo nuevo, se ajusta a mano
`FACTUM_TATANA_VERSION_MINIMA` en `config/.env` (`actualizar.sh` no la cambia).

### 12.5 Checklist de la migración de lo instalado el 2026-10-20

Estado de partida: servidor plan A con Factum `0.0.0-fiscalia-prueba` (Caddy sin canal) y ≥ 4 PCs con Tatana en
Docker `tatana-docker-prueba:0.0.0-docker-prueba` **sin actualizador**. No se pierden datos ni evidencia.

**Antes (en la Mac):**

- [ ] Release firmada `tatana-vX.Y.Z` con los archivos de Docker (promover `develop` → `main` y empujar el tag: lo
      decide el usuario; `tatana-docker.yml` arma, prueba, firma y publica).
- [ ] `deploy/fiscalia/armar-paquete-fiscalia.sh --actualizacion --version 0.1.0 --imagenes-de 0.0.0-fiscalia-prueba
      --tatana-docker-release X.Y.Z` (≈ 290 MB, casi todo Tatana; la web y la API quedan exactamente como hoy).

**Servidor (una vez, en persona o por AnyDesk):**

- [ ] Copiar el paquete y su `.sha256` al disco del servidor; `sha256sum -c`; descomprimir.
- [ ] `sudo bash Factum-Fiscalia-Actualizacion-v0.1.0/actualizar.sh`. Hace el backup previo completo en
      `/srv/factum-fiscalia/backups/`; reemplaza compose/Caddyfile/scripts (los viejos en `actualizacion-anterior/`);
      `config/.env`, `datos/`, `appsettings.Local.json`, `branding/`, la **CA de Caddy** (los certificados de las PCs
      siguen valiendo) y el modo sin AVX quedan como estaban; crea `canal-tatana/` y `datos/caddy-logs/`; publica Tatana.
- [ ] Desde una PC de perito: `https://factum.internal` abre igual; `curl -I https://factum.internal/tatana/docker/tatana-update-docker.json` → 200.

**Cada PC de perito (una vez; un comando, con la sesión del perito, sin sudo — lo pide una vez):**

```bash
wget -qO /tmp/sumar-actualizador.sh https://factum.internal/tatana/docker/sumar-actualizador.sh \
  && bash /tmp/sumar-actualizador.sh
```

- [ ] Termina mostrando "Actualizaciones" con la versión nueva instalada y el timer activo. Si Tatana estaba
      capturando, corta sin tocar nada: repetir al terminar.
- [ ] No cambian: `~/.local/share/tatana-docker/` (datos, pareos del iPhone, configuración), `~/Factum/Evidencia`, la
      regla udev, el usbmuxd enmascarado, `/etc/hosts` ni la CA.

**Al final:**

- [ ] `sudo /srv/factum-fiscalia/scripts/estado-peritos.sh` muestra todas las PCs en la versión publicada.
- [ ] Una captura de punta a punta desde una PC actualizada.

**Vuelta atrás:** servidor — `actualizacion-anterior/` + el backup previo (o `instalar-servidor.sh --reparar` desde el
paquete viejo, que sigue en el disco); PC — `./estado-tatana-docker.sh --volver-a-anterior` (vuelve a
`0.0.0-docker-prueba`) o `desinstalar-tatana-docker.sh` (saca el actualizador sin tocar datos).
