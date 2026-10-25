#!/usr/bin/env bash
# Nivel 3 de la prueba del actualizador de Tatana en Docker (SDD fiscalia-operacion §14.2): Ubuntu 20.04 REAL (bash 5.0,
# coreutils y systemd 245) en un contenedor con systemd como PID 1. Privilegiado: SOLO en el runner efímero de CI
# (nunca en la Mac: un systemd privilegiado en Docker Desktop rompe binfmt; por eso systemd-binfmt va enmascarado igual).
# Usa el Docker del runner (socket montado) con las rutas del actualizador y el home de prueba montados 1:1.
#
#   sudo bash ci/tatana-docker/prueba-2004.sh --paquete Tatana-Docker-vX.Y.Z.tar.gz --canal <dir firmado> --version X.Y.Z
#
# Casos: systemd-analyze verify de las units; instalación con el instalador nuevo (como un usuario no root de 20.04);
# el timer aparece en list-timers; una corrida feliz (base 0.0.1 → X) y una de vuelta atrás (X+1 que no arranca) con
# `systemctl start` dentro de 20.04.
# shellcheck disable=SC2034,SC2024 # variables que usan los chequeos (eval); los registros son del root de CI
set -euo pipefail
# shellcheck source=ci/tatana-docker/_prueba-lib.sh
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/_prueba-lib.sh"

PAQUETE="" CANAL_X="" X=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --paquete) PAQUETE="$2"; shift 2 ;;
    --canal) CANAL_X="$2"; shift 2 ;;
    --version) X="$2"; shift 2 ;;
    *) echo "argumento desconocido: $1" >&2; exit 2 ;;
  esac
done
[[ -f "$PAQUETE" && -f "$CANAL_X/tatana-update-docker.json" && "$X" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] ||
  { echo "uso: $0 --paquete <tar.gz> --canal <dir> --version X.Y.Z" >&2; exit 2; }
prueba_guardas
IFS=. read -r MA MI PA <<<"$X"
X1="$MA.$MI.$((PA + 1))"
PERITO=perito2004
PUID=1500
HP="/home/$PERITO"
C2004=td-ubuntu-2004
# Docker estático (el mismo que lleva el paquete de la fiscalía): versión y SHA-256 de armar-paquete-fiscalia.sh.
DOCKER_VERSION="$(sed -nE 's/^DOCKER_VERSION="([^"]+)".*/\1/p' "$RAIZ/deploy/fiscalia/armar-paquete-fiscalia.sh")"
DOCKER_SHA256="$(sed -nE 's/^DOCKER_SHA256="([^"]+)".*/\1/p' "$RAIZ/deploy/fiscalia/armar-paquete-fiscalia.sh")"

limpiar() {
  set +e
  docker exec "$C2004" journalctl -u factum-tatana-actualizador.service --no-pager >"$LOGS/journal-2004.log" 2>&1
  cp -f /var/log/factum-tatana-docker/actualizador.log "$LOGS/" 2>/dev/null
  cp -f /var/lib/factum-tatana-docker/estado.env "$LOGS/" 2>/dev/null
  chmod -R a+rX "$LOGS" 2>/dev/null
  docker rm -f "$C2004" >/dev/null 2>&1
  docker rm -f tatana-docker tatana-docker-anterior >/dev/null 2>&1
  servidor_limpiar
}
trap limpiar EXIT
en2004() { docker exec "$C2004" "$@"; }
como_perito() { docker exec -u "$PERITO" -e HOME="$HP" -w "$HP" "$C2004" bash -c "$1"; }

paso "Servidor de la fiscalía (Caddy + CA en el runner)"
id -u "$PERITO" >/dev/null 2>&1 || useradd -m -u "$PUID" -s /bin/bash "$PERITO"
servidor_arrancar "$PERITO"
clave_prueba

paso "Imagen ubuntu:20.04 con systemd"
mkdir -p "$W/img2004"
curl -fsSL --retry 3 -o "$W/img2004/docker.tgz" "https://download.docker.com/linux/static/stable/x86_64/docker-$DOCKER_VERSION.tgz"
[[ "$(sha256sum "$W/img2004/docker.tgz" | cut -d' ' -f1)" == "$DOCKER_SHA256" ]] || { echo "ERROR: SHA-256 del Docker estático" >&2; exit 1; }
tar -xzf "$W/img2004/docker.tgz" -C "$W/img2004" docker/docker
GID_DOCKER="$(stat -c %g /var/run/docker.sock)"
cat >"$W/img2004/Dockerfile" <<EOF
FROM ubuntu:20.04
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends systemd systemd-sysv sudo ca-certificates util-linux \
      coreutils iproute2 procps tzdata && rm -rf /var/lib/apt/lists/*
# systemd-binfmt enmascarado (igual que en la regla para la Mac): no toca los manejadores binfmt del kernel compartido.
RUN ln -sf /dev/null /etc/systemd/system/systemd-binfmt.service \
 && ln -sf /dev/null /etc/systemd/system/proc-sys-fs-binfmt_misc.automount \
 && ln -sf /dev/null /etc/systemd/system/getty@tty1.service
COPY docker/docker /usr/local/bin/docker
RUN groupadd -g $GID_DOCKER dockerhost || true \
 && useradd -m -u $PUID -s /bin/bash -G \$(getent group $GID_DOCKER | cut -d: -f1) $PERITO \
 && echo "$PERITO ALL=(ALL) NOPASSWD:ALL" >/etc/sudoers.d/perito && chmod 440 /etc/sudoers.d/perito
STOPSIGNAL SIGRTMIN+3
CMD ["/sbin/init"]
EOF
docker build -q -t td-ubuntu2004 "$W/img2004" >/dev/null
mkdir -p /opt/factum-tatana-docker /etc/factum-tatana-docker /var/lib/factum-tatana-docker /var/log/factum-tatana-docker
docker run -d --name "$C2004" --privileged --cgroupns=host -v /sys/fs/cgroup:/sys/fs/cgroup:rw --network host \
  --tmpfs /run --tmpfs /run/lock -v /var/run/docker.sock:/var/run/docker.sock \
  -v /opt/factum-tatana-docker:/opt/factum-tatana-docker -v /etc/factum-tatana-docker:/etc/factum-tatana-docker \
  -v /var/lib/factum-tatana-docker:/var/lib/factum-tatana-docker -v /var/log/factum-tatana-docker:/var/log/factum-tatana-docker \
  -v "$HP:$HP" -v "$W:$W" td-ubuntu2004 >/dev/null
for _ in $(seq 1 30); do
  e="$(en2004 systemctl is-system-running 2>/dev/null || true)"
  [[ "$e" == running || "$e" == degraded ]] && break
  sleep 2
done
# "starting" = quedan trabajos de arranque en cola (en un contenedor suele ser alguno que espera hardware o red): el
# gestor ya atiende systemctl, que es lo que importa acá. Los trabajos pendientes quedan en el registro.
en2004 systemctl list-jobs --no-pager >"$LOGS/systemd-jobs-2004.log" 2>&1 || true
chequear "systemd de 20.04 arrancó ($e)" '[[ "$e" == running || "$e" == degraded || "$e" == starting ]]'
chequear "bash 5.0 y systemd 245" 'en2004 bash -c "[[ \${BASH_VERSINFO[0]}.\${BASH_VERSINFO[1]} == 5.0 ]]" && en2004 systemctl --version | head -1 | grep -q "systemd 245"'
docker cp "$W/ca-factum-fiscalia.crt" "$C2004:/usr/local/share/ca-certificates/factum-fiscalia.crt"
en2004 bash -c 'grep -q "factum.internal" /etc/hosts || echo "127.0.0.1 factum.internal" >>/etc/hosts'
en2004 update-ca-certificates >/dev/null 2>&1 || true
chequear "el perito habla con Docker" 'como_perito "docker info >/dev/null"'

paso "Base 0.0.1 instalada con el instalador nuevo dentro de 20.04"
mkdir -p "$W/x"
tar -xzf "$PAQUETE" -C "$W/x"
KIT_X="$W/x/Tatana-Docker-v$X"
docker load -i "$KIT_X/imagen-tatana-docker-v$X.tar.gz" >/dev/null
imagen_reetiquetada "factum-tatana-docker:$X" 0.0.1
imagen_reetiquetada "factum-tatana-docker:$X" "$X1"
BASE="$(carpeta_version "$KIT_X" 0.0.1 "$W/base")"
D1="$(carpeta_version "$KIT_X" "$X1" "$W/v1")"
canal_de_carpeta "$D1" "$X1" "$W/canal-x1"
docker image rm "factum-tatana-docker:$X" "factum-tatana-docker:$X1" factum-tatana-docker:0.0.1 >/dev/null
chown -R "$PUID" "$W/base"
if ! como_perito "cd '$BASE' && ./instalar-tatana-docker.sh --mock --origen https://factum.internal --si" >"$LOGS/instalar-2004.log" 2>&1; then
  tail -40 "$LOGS/instalar-2004.log"
  echo "ERROR: el instalador falló en 20.04" >&2
  exit 1
fi
chequear "instalado y respondiendo" '[[ -n "$(version_health)" ]]'
chequear "el timer aparece en list-timers" 'en2004 systemctl list-timers --all | grep -q factum-tatana-actualizador.timer'
chequear "systemd-analyze verify de las units (systemd 245)" \
  'en2004 systemd-analyze verify /etc/systemd/system/factum-tatana-actualizador.service /etc/systemd/system/factum-tatana-actualizador.timer >"$LOGS/systemd-analyze.log" 2>&1'

paso "Corrida feliz con systemctl start (0.0.1 → $X)"
publicar "$CANAL_X"
en2004 systemctl start factum-tatana-actualizador.service || true
echo "  → RESULTADO=$(estado RESULTADO) $(estado DETALLE)"
chequear "actualizada" '[[ "$(estado RESULTADO)" == actualizada ]]'
chequear "/health = $X" '[[ "$(version_health)" == "$X" ]]'
linea_clave_prueba >>"/opt/factum-tatana-docker/kits/$X/claves-confiables.txt"

paso "Vuelta atrás con systemctl start ($X1 no arranca)"
publicar "$W/canal-x1"
en2004 systemctl start factum-tatana-actualizador.service || true
echo "  → RESULTADO=$(estado RESULTADO) $(estado DETALLE)"
chequear "no_arranca" '[[ "$(estado RESULTADO)" == no_arranca ]]'
chequear "volvió a $X" '[[ "$(version_health)" == "$X" ]]'
chequear "$X1 en VERSIONES_FALLIDAS" '[[ ",$(estado VERSIONES_FALLIDAS)," == *",$X1,"* ]]'
como_perito "bash '$BASE/estado-tatana-docker.sh' --actualizaciones" >"$LOGS/estado-2004.log" 2>&1 || true
chequear "estado-tatana-docker.sh --actualizaciones en 20.04" 'grep -q "timer activo" "$LOGS/estado-2004.log"'

echo
if ((fallos)); then echo "$fallos chequeo(s) fallaron (registros en $LOGS)"; exit 1; fi
echo "Actualizador de Tatana en Docker en Ubuntu 20.04 (nivel 3): todos los casos pasaron"
