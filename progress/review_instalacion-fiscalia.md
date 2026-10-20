# Review — instalacion-fiscalia (#94, planes A y B; la §7 Tatana en Docker no se revisó)

**Veredicto:** APROBADA (con workarounds para la fiscalía; nada bloqueante encontrado en el código)

Revisado: `deploy/fiscalia/` (sin `dist/`), `docs/instalacion-fiscalia.md`, `.gitignore`, informe de impl.
Verificado por mí: `bash -n` de los 14 scripts OK; `shellcheck -x` (imagen `koalaman/shellcheck:stable`, sin
privilegios) sin warnings (solo SC1091 info por la ruta del `source`); SHA256SUMS del `dist/` verifica 29/29;
no hay `._*` ni `.DS_Store` dentro del paquete; los 15 scripts/compose/Caddyfile/guía de
`dist/Factum-Fiscalia-v0.0.0-fiscalia-prueba/` son **byte a byte iguales** a los de `deploy/fiscalia/` (`cmp`).
No toqué tarjetas ni `progress/sesiones/`. No se levantó ningún contenedor.

## Para el usuario en la fiscalía

0. **Lo que llevás puede ser una versión anterior.** Copiaste a ~12:45Z y el `dist/` actual se armó a las
   12:59:07Z (`origen.txt`); `comun/instalar.sh` se editó 12:49Z y `comun/backup.sh` 12:58Z, o sea DESPUÉS de tu
   copia. El pendrive pasa su propio SHA256SUMS (es autoconsistente), así que no avisa. Chequeá en la PC:
   `cat origen.txt` (tiene que decir `armado=2026-10-20T12:59:07Z`) y
   `sha256sum comun/instalar.sh comun/backup.sh` (esperado `34a6a7a9e9b7975a0de13ed3c1dd36e75f4da403787e7ad912eefd6e4e528775` y
   `2d30ae9d570824d916a2443390d4ff096d3c00515a920098b6b6f550c1ccaf1c`). Si no coinciden y podés, volvé a copiar la
   carpeta completa desde la Mac (o al menos `comun/`, `servidor/`, `una-pc/`, `perito/`, `herramientas/*.sh` y
   `SHA256SUMS`; no edites a mano: el SHA256SUMS lo rechaza).
1. **Siempre `sudo bash ...`, nunca `sh ...` ni `./...`** (en Ubuntu `sh` es dash y falla con `Bad substitution`;
   FAT/exFAT no tiene bit de ejecución). Copiá el paquete al disco (`cp -r /media/$USER/PEN/Factum-Fiscalia-v0.0.0-fiscalia-prueba ~/`) y
   corré desde ahí: `cd ~/Factum-Fiscalia-v0.0.0-fiscalia-prueba && sudo bash servidor/instalar-servidor.sh`.
2. **Archivos `._*` de macOS** (Finder los crea en exFAT/FAT): NO rompen SHA256SUMS (solo verifica los archivos
   listados) ni mongo-init (el glob de Mongo ignora los ocultos). Solo molestan si agregás un AppImage: borralos
   antes con `find ~/Factum-Fiscalia-v0.0.0-fiscalia-prueba -name '._*' -delete` (ver 5).
3. **Docker ya instalado pero de snap** (`which docker` → `/snap/bin/docker`, típico de Ubuntu Server): el
   snap no puede leer `/srv`. `sudo snap remove docker` y después `sudo bash herramientas/instalar-docker-offline.sh`
   (el script se niega a tocar un Docker existente, por eso hay que sacar el snap antes).
4. **Chrome/Chromium no confía en el certificado** (en Ubuntu/Debian de escritorio `certutil` NO viene instalado
   y sin internet no se puede instalar; el script solo avisa y sigue): importalo a mano en el navegador →
   Configuración → Privacidad y seguridad → Seguridad → Administrar certificados → **Autoridades** → Importar →
   `ca-factum-fiscalia.crt` → tildar "Confiar en este certificado para identificar sitios web"; reiniciar el
   navegador. Firefox: Ajustes → Privacidad y seguridad → Ver certificados → Autoridades → Importar → tildar
   confiar para sitios web (o `about:config` → `security.enterprise_roots.enabled` = `true`, que usa el almacén del
   sistema que el script sí dejó instalado). Verificación rápida desde terminal: `curl -v https://factum.internal/health`.
5. **Tatana no viene en este paquete** (no hay carpeta `tatana/`; el instalador solo avisa). Hay que llevar el
   AppImage por separado: `mkdir -p ~/Applications && cp Tatana-*.AppImage ~/Applications/Tatana.AppImage && chmod +x ~/Applications/Tatana.AppImage`
   (si hay `._Tatana...` al lado, `find` del script podría elegir ese: borrá los `._*` antes). En Ubuntu 22.04/24.04
   el AppImage puede necesitar FUSE 2 (`libfuse2`/`libfuse2t64`, hace falta internet) o correrlo con
   `~/Applications/Tatana.AppImage --appimage-extract-and-run`; en Ubuntu 24.04 Electron puede quejarse del sandbox
   (`--no-sandbox` como último recurso). Esto es de #91, no de este paquete. El plan A en el perito además
   necesita que se haya corrido `preparar-pc-perito.sh` para que `~/.config/Tatana/appsettings.Local.json` acepte
   `https://factum.internal`.
6. **Los navegadores de la fiscalía suelen tener proxy**: si `https://factum.internal` va al proxy y da 502/403,
   agregá `factum.internal` (y `localhost`) a las excepciones del proxy (Configuración → Red → Proxy, o
   `no_proxy`). Es la causa más probable de "el servidor responde con curl pero no en el navegador".
7. **Puerto 80/443 ocupado** (Apache/nginx/otro): el instalador se corta ANTES de crear nada. Ver quién:
   `sudo ss -ltnp | grep -E ':(80|443)\b'` y pararlo (`sudo systemctl disable --now apache2`). Nada se pierde.
8. **El instalador se cortó a mitad** (ya existe `/srv/factum-*/config/.env`): no repetir sin flag. Usar
   `sudo bash servidor/instalar-servidor.sh --reparar` (o `una-pc/instalar-una-pc.sh --reparar`): conserva `.env` y
   datos y vuelve a mostrar DNI y contraseña temporal si aún no se habían mostrado. Diagnóstico:
   `sudo /srv/factum-fiscalia/scripts/diagnostico.sh > ~/diag.txt 2>&1`.
9. **Fedora/RHEL/Rocky con SELinux** (`getenforce` → Enforcing): si Mongo o el backend mueren con "Permission
   denied" sobre `/data/db` o `/app/appsettings.Local.json`, los bind mounts no llevan `:z`. Probar con
   `sudo setenforce 0` y `--reparar`; o `sudo chcon -Rt container_file_t /srv/factum-fiscalia` (y `/srv/factum-una-pc`).
10. **CPU sin AVX** (`grep -c avx /proc/cpuinfo` da 0, p. ej. VM sin passthrough o Celeron/Atom viejo): el
    instalador se niega a seguir (correcto: MongoDB 7 no arranca). Usar otra PC o, en una VM, CPU "host".
11. **Hora/fecha mal en el servidor o el perito** (pila de BIOS): el certificado aparece como no válido todavía/vencido.
    `timedatectl` y `sudo timedatectl set-ntp true` / `sudo date -s "2026-10-20 14:00"`.
12. **ufw/firewalld**: Docker publica 80/443 salteando ufw; si igual no llegan:
    `sudo ufw allow 80/tcp && sudo ufw allow 443/tcp`; en firewalld
    `sudo firewall-cmd --permanent --add-service=http --add-service=https && sudo firewall-cmd --reload`.
    Si Docker quedó sin iptables (el script de Docker offline lo avisa), ufw SÍ bloquea: abrir esos puertos.
13. **IP con ceros a la izquierda** (`192.168.001.008`): `preparar-pc-perito.sh` revienta con "value too great for
    base". Usar la IP sin ceros (`192.168.1.8`).
14. **Backup a pendrive FAT32**: `evidencia.tar.gz` falla si pasa de 4 GB; usar exFAT/ext4 o un disco. El backup no
    va cifrado y lleva `config/.env` (guardarlo bajo llave).
15. **Docker de distro viejo con compose 1.x** (`docker-compose` con guion, Debian 12): el instalador ofrece
    instalar el plugin v2 del paquete (`sudo bash herramientas/instalar-docker-offline.sh --solo-compose`).
16. **Si el servidor no tiene systemd** o algo raro con Docker offline: instalar Docker con el gestor de la
    distro (`sudo apt install docker.io` / `sudo dnf install moby-engine`) y después `--solo-compose`.

## Checkpoints
(CHECKPOINTS.md aplica a HU de código de producción; esta HU solo toca `deploy/`, `docs/`, `.gitignore`.)
- Archivos tocados coinciden con la SDD §3 y no hay cambios en `server/src`, `client/src`, `agent-ui/src`: [x]
- Contrato compartido: [x] (no cambia; `factum.internal`, `Cors__AllowedOrigins__0`, `Agent:AllowedOrigins` coherentes
  entre Caddyfile, compose.servidor.yml, frontend horneado y `preparar-pc-perito.sh`).
- MongoDB: [x] no se escribe sobre documentos ajenos; `--desinstalar` sin `--borrar-datos` no borra datos,
  `--borrar-datos` exige escribir BORRAR; `borrar-caso-prueba.sh` borra por `_id` y solo `PRUEBA*`.
- Agente Tatana / agent-ui: [x] no se tocan.
- Seguridad: [x] secretos generados en la PC, `.env` 600, carpetas 700, superadmin temporal vaciado del `.env`, solo
  Caddy publica puertos, plan B solo en 127.0.0.1, sin secretos en `diagnostico.sh`.
- build/tsc: [x] no aplica (sin código de producción); scripts con `bash -n` y shellcheck limpios.

## Cambios requeridos
Ninguno bloqueante.

## Observaciones no bloqueantes (para después)
- `comun/instalar.sh:350`: `command -v openssl && HUELLA="$(openssl ... | cut ...)"` con `set -e pipefail`: si openssl
  falla al leer el certificado, el script sale justo después de instalar, antes del resumen. Poner `|| true`.
- `perito/preparar-pc-perito.sh:45`: `((BASH_REMATCH[o] <= 255))` con octetos con cero a la izquierda (`08`) aborta
  por aritmética octal (probado). Usar `10#${BASH_REMATCH[o]}`.
- `comun/instalar.sh:391` y `perito/preparar-pc-perito.sh:192`: `find ... -name '*.AppImage' | head -1` puede elegir un
  `._Tatana*.AppImage` de macOS; agregar `! -name '._*'`.
- `perito/preparar-pc-perito.sh:107-125`: sin `certutil` solo avisa (esperable en Ubuntu desktop sin internet). Sería más
  robusto poner `"ImportEnterpriseRoots": true` en la política de Firefox, y documentar la importación manual en Chrome
  con pasos (hoy solo remite a la guía). Los navegadores de snap pueden no leer `/etc/firefox/policies/factum-fiscalia.crt`.
- Los bind mounts de `comun/docker-compose.yml` y `servidor/compose.servidor.yml` no llevan `:z` (SELinux, ver 9).
- `comun/_comun.sh:puerto_ocupado`: `grep -q` dentro de pipeline con `pipefail` puede dar falso "libre" por SIGPIPE;
  con la salida chica de `ss` no pasa en la práctica.
- El paquete del `dist/` es `deploy_de_trabajo=1` y sin Tatana: rearmar después del commit y con `--tatana-appimage`.
- Probado solo en Mac (contenedores Ubuntu 24.04 y arm64 para Docker offline); sin prueba en una distro real ni con
  Docker de distro viejo (20.10/24) cargando los tar OCI de Docker Desktop (los tar traen `manifest.json`, debería cargar).

## Re-chequeo modo sin AVX

**Veredicto:** APROBADA (sin cambios bloqueantes)

Verificado por mí: `bash -n` OK en todos los `.sh` de `deploy/fiscalia` y en `aplicar-parche.sh` (no hay `shellcheck` instalado
en esta Mac; se confía en lo declarado para eso). Sin cambios en `server/src`, `client/src`, `agent-ui/src`.

- Comportamiento normal (con AVX) de A/B sin cambios: [x]. `modo_sin_avx` lee `FACTUM_MONGO_SIN_AVX` y, si no está en
  `config/.env` (instalaciones de producción), devuelve falso: `fc` no suma nada, `mongo_shell` da `mongosh`,
  `MONGO_TAR=mongo`, `IMAGENES` igual, no se copian `compose.sin-avx.yml`/`mongo-init-sin-avx`. `cpu_tiene_avx` solo
  corre en Linux x86_64 y con AVX imprime lo de siempre. Los `((SIN_AVX)) && ...` están en listas `&&` o en `$( )`,
  sin riesgo con `set -e`. `docker-compose.yml` (Mongo 7) y `backup.sh`/`restaurar.sh` no tienen diferencias.
  Instalaciones ya existentes siguen sin tocarse hasta que alguien reinstale/repare con los scripts nuevos.
- Guarda `--reparar` sobre base de Mongo 7 con `WiredTiger`: [x] (instalar.sh:213), se niega antes de tocar nada.
- Compatibilidad MongoDB.Driver 3.4.0 con servidor 4.4: [x]. Grep del backend: sin transacciones, change streams,
  collation, ni operadores de agregación 5.0+ (`$dateTrunc`, `$getField`, etc.); solo `Aggregate` con
  `$match/$project/$group/$sum` (MongoRepository.cs:219) y filtros/updates simples. Más la prueba empírica del implementador.
- `mongodump`/`mongorestore`/`mongo` 4.4: [x] por lectura. `--config`, `--archive --gzip`, `--nsInclude`, `--drop`,
  `--quiet`, `--db` existen en database-tools de la imagen 4.4; `borrar-caso-prueba.sh` usa JS válido en ambos shells
  (`typeof process` / `_getEnv`) y filtra el ruido de conexión; `mongo-init-sin-avx` usa `_getEnv`. El healthcheck
  de `compose.sin-avx.yml` reemplaza el de `mongosh`; el montaje de init se reemplaza por mismo destino.
  No levanté contenedores (pedido); me apoyo en las pruebas end to end declaradas (backup/restaurar/borrar/diagnóstico).
- `aplicar-parche.sh` y SHA256SUMS: [x]. Simulé el parche en una copia del paquete en el scratchpad: verifica sus 12
  archivos, copia atómica (`cp` + `mv`), reescribe en el `SHA256SUMS` del paquete solo las líneas de los 10 archivos
  parchados (original guardado en `.antes-del-parche`); `shasum -c` sigue OK para el resto y un archivo no parchado
  alterado (`herramientas/VERSIONES`) sigue dando FAILED. Los 10 archivos de `archivos/` son idénticos a las fuentes
  de `deploy/fiscalia/comun`, `servidor`, `una-pc` y `docs/` (cmp), y el `SHA256SUMS` del parche los cubre a todos.
- Docs (`docs/instalacion-fiscalia.md` §2.4) coherentes con el comportamiento.

### Observaciones no bloqueantes
- `aplicar-parche.sh` paso 2 copia todo lo que haya en `archivos/` aunque no figure en el `SHA256SUMS` del parche, y
  luego lo "firma" en el del paquete. Hoy las listas coinciden, pero convendría exigir que cada archivo copiado esté
  listado (o fallar si `archivos/` tiene algo no listado). Además el `SHA256SUMS` del parche solo detecta corrupción
  de copia, no autenticidad: la garantía real es el `.sha256` del `.tar.gz` comparado por otro canal (decirlo en el LEEME).
- Restaurar un backup hecho con Mongo 7 en una instalación 4.4 (o al revés con archivos de tools más nuevos) no está
  probado ni avisado; los backups de una instalación 4.4 se restauran en 4.4 (probado). Documentar "no mezclar".
- Plan A con `--sin-avx` no probado de punta a punta (declarado); el cambio es el mismo `fc`/compose, riesgo bajo.
- Sin `shellcheck` local para repetirlo.
