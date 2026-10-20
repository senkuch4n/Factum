#!/usr/bin/env bash
# Factum en la fiscalía — instala Docker Engine SIN INTERNET y sin depender de la distro, desde los binarios
# estáticos oficiales que trae el paquete (herramientas/docker-<versión>.tgz) + el complemento compose
# (herramientas/docker-compose-linux-x86_64). Versiones y SHA-256 fijos: los pone armar-paquete-fiscalia.sh
# en herramientas/VERSIONES y se verifican acá antes de instalar.
#
#   sudo bash herramientas/instalar-docker-offline.sh                 Docker + compose (si no hay Docker)
#   sudo bash herramientas/instalar-docker-offline.sh --solo-compose  solo el complemento compose (Docker ya está)
#
# Lo llaman instalar-servidor.sh e instalar-una-pc.sh cuando falta "docker"; también se puede correr suelto.
# Si ya hay un Docker instalado (de la distro o de otro lado) NO lo toca: solo agrega compose si falta.
#
# Qué instala:
#   /usr/local/bin/{docker,dockerd,containerd,containerd-shim-runc-v2,ctr,runc,docker-init,docker-proxy}
#   /usr/local/lib/docker/cli-plugins/docker-compose
#   /etc/systemd/system/docker.service y docker.socket (dockerd arranca su propio containerd: sin unit aparte)
#   grupo "docker" (con el usuario que llamó a sudo; vale desde el próximo inicio de sesión)
# Requisitos: Linux x86_64 con systemd. iptables recomendado (si falta, Docker se configura sin iptables).
set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOLO_COMPOSE=0
for a in "$@"; do
  case "$a" in
    --solo-compose) SOLO_COMPOSE=1 ;;
    -h | --help) sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Argumento desconocido: $a" >&2; exit 2 ;;
  esac
done
# Rutas de destino (variables solo para probar el script en un contenedor; en la PC quedan las de siempre).
BIN="${FACTUM_DOCKER_BIN:-/usr/local/bin}"
PLUGINS="${FACTUM_DOCKER_PLUGINS:-/usr/local/lib/docker/cli-plugins}"
UNITS="${FACTUM_DOCKER_UNITS:-/etc/systemd/system}"

ok() { printf '  OK  %s\n' "$*"; }
aviso() { printf '  AVISO %s\n' "$*"; }
morir() {
  printf '\nERROR: %s\n' "$1" >&2
  [[ -n "${2:-}" ]] && printf '       %s\n' "$2" >&2
  exit 1
}
sha() { if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi; }

echo ""
echo "=== Docker sin internet (binarios estáticos oficiales) ==="
[[ "$(uname -s)" == Linux ]] || morir "esto es solo para Linux."
[[ "$(uname -m)" == x86_64 ]] || morir "esta PC es $(uname -m); los binarios del paquete son para x86_64."
[[ "$(id -u)" == 0 ]] || morir "hace falta sudo." "Corré: sudo bash $0 $*"
[[ -f "$DIR/VERSIONES" ]] || morir "falta herramientas/VERSIONES en el paquete."
DOCKER_VERSION="$(grep -E '^DOCKER_VERSION=' "$DIR/VERSIONES" | cut -d= -f2)"
DOCKER_SHA256="$(grep -E '^DOCKER_SHA256=' "$DIR/VERSIONES" | cut -d= -f2)"
COMPOSE_VERSION="$(grep -E '^COMPOSE_VERSION=' "$DIR/VERSIONES" | cut -d= -f2)"
COMPOSE_SHA256="$(grep -E '^COMPOSE_SHA256=' "$DIR/VERSIONES" | cut -d= -f2)"
TGZ="$DIR/docker-$DOCKER_VERSION.tgz"
COMPOSE="$DIR/docker-compose-linux-x86_64"

instalar_compose() {
  [[ -f "$COMPOSE" ]] || morir "falta herramientas/docker-compose-linux-x86_64 en el paquete."
  [[ "$(sha "$COMPOSE")" == "$COMPOSE_SHA256" ]] || morir "docker-compose-linux-x86_64 está dañado (SHA-256 distinto)." "Copiá de nuevo el paquete."
  mkdir -p "$PLUGINS"
  install -m 755 "$COMPOSE" "$PLUGINS/docker-compose"
  ok "Complemento compose $COMPOSE_VERSION en $PLUGINS/docker-compose"
}

# ── ¿Ya hay Docker? Entonces no se toca; a lo sumo se agrega compose. ───────────────────────────
if command -v docker >/dev/null 2>&1 || command -v dockerd >/dev/null 2>&1 ||
  [[ -f /lib/systemd/system/docker.service || -f /usr/lib/systemd/system/docker.service || -f "$UNITS/docker.service" ]]; then
  ok "Docker ya está instalado en esta PC ($(command -v docker || command -v dockerd || echo 'docker.service')): no se toca."
  if docker compose version >/dev/null 2>&1; then
    ok "docker compose ya está: $(docker compose version --short 2>/dev/null)."
  else
    instalar_compose
  fi
  if ! docker info >/dev/null 2>&1; then
    aviso "Docker está instalado pero no responde. Probá: sudo systemctl enable --now docker"
  fi
  exit 0
fi
((SOLO_COMPOSE)) && morir "--solo-compose: no hay Docker instalado en esta PC." "Corré el script sin --solo-compose."

# ── Requisitos ─────────────────────────────────────────────────────────────────────────────────
[[ -d /run/systemd/system ]] || morir "esta PC no usa systemd: el paquete no sabe dejar Docker arrancando solo." \
  "Instalá Docker con el gestor de la distro (guía, sección 2.3)."
for h in tar install groupadd systemctl; do command -v "$h" >/dev/null 2>&1 || morir "falta '$h' en esta PC."; done
[[ -f "$TGZ" ]] || morir "falta herramientas/docker-$DOCKER_VERSION.tgz en el paquete."
echo "  Verificando docker-$DOCKER_VERSION.tgz..."
[[ "$(sha "$TGZ")" == "$DOCKER_SHA256" ]] || morir "docker-$DOCKER_VERSION.tgz está dañado (SHA-256 distinto)." "Copiá de nuevo el paquete."
ok "SHA-256 verificado."
for b in docker dockerd containerd containerd-shim-runc-v2 ctr runc docker-init docker-proxy; do
  [[ ! -e "$BIN/$b" ]] || morir "ya existe $BIN/$b (¿un Docker a medio instalar?)." "Revisalo a mano antes de seguir; este script no pisa binarios."
done

# ── Binarios ───────────────────────────────────────────────────────────────────────────────────
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
tar -xzf "$TGZ" -C "$TMP"
mkdir -p "$BIN"
INSTALADOS=""
for b in "$TMP"/docker/*; do
  install -m 755 "$b" "$BIN/$(basename "$b")"
  INSTALADOS="$INSTALADOS $(basename "$b")"
done
ok "Binarios en $BIN:$INSTALADOS"
instalar_compose

# ── Grupo docker ───────────────────────────────────────────────────────────────────────────────
getent group docker >/dev/null 2>&1 || groupadd --system docker
U="${SUDO_USER:-}"
if [[ -n "$U" && "$U" != root ]]; then
  usermod -aG docker "$U" && ok "Usuario $U agregado al grupo docker (vale desde el próximo inicio de sesión; mientras, usá sudo)."
fi

# ── iptables ───────────────────────────────────────────────────────────────────────────────────
if ! command -v iptables >/dev/null 2>&1; then
  mkdir -p /etc/docker
  if [[ ! -f /etc/docker/daemon.json ]]; then
    printf '{\n  "iptables": false,\n  "ip6tables": false\n}\n' >/etc/docker/daemon.json
    aviso "Esta PC no tiene iptables: Docker queda sin reglas de firewall propias (/etc/docker/daemon.json)."
    aviso "Los puertos publicados igual funcionan. Si podés, instalá iptables y borrá ese archivo."
  fi
fi
command -v modprobe >/dev/null 2>&1 && { modprobe overlay 2>/dev/null || true; modprobe br_netfilter 2>/dev/null || true; }

# ── systemd ────────────────────────────────────────────────────────────────────────────────────
mkdir -p "$UNITS"
cat >"$UNITS/docker.socket" <<'EOF'
# Instalado por instalar-docker-offline.sh (Factum). Igual al oficial de moby (contrib/init/systemd).
[Unit]
Description=Docker Socket for the API

[Socket]
ListenStream=/run/docker.sock
SocketMode=0660
SocketUser=root
SocketGroup=docker

[Install]
WantedBy=sockets.target
EOF
cat >"$UNITS/docker.service" <<EOF
# Instalado por instalar-docker-offline.sh (Factum). Basado en el oficial de moby (contrib/init/systemd), con
# dockerd en $BIN y SIN --containerd: dockerd arranca y supervisa su propio containerd (el del mismo .tgz).
[Unit]
Description=Docker Application Container Engine
Documentation=https://docs.docker.com
After=network-online.target nss-lookup.target docker.socket firewalld.service time-set.target
Wants=network-online.target
Requires=docker.socket

[Service]
Type=notify
Environment=PATH=$BIN:/usr/local/sbin:/usr/sbin:/usr/bin:/sbin:/bin
ExecStart=$BIN/dockerd -H fd://
ExecReload=/bin/kill -s HUP \$MAINPID
TimeoutStartSec=0
RestartSec=2
Restart=always
StartLimitBurst=3
StartLimitIntervalSec=60
LimitNPROC=infinity
LimitCORE=infinity
TasksMax=infinity
Delegate=yes
KillMode=process
OOMScoreAdjust=-500

[Install]
WantedBy=multi-user.target
EOF
chmod 644 "$UNITS/docker.service" "$UNITS/docker.socket"
systemctl daemon-reload
systemctl enable --now docker.socket docker.service
ok "docker.service y docker.socket habilitados (arrancan solos al prender la PC)."

# ── Verificación ───────────────────────────────────────────────────────────────────────────────
echo "  Esperando a Docker..."
for _ in $(seq 1 30); do docker info >/dev/null 2>&1 && break; sleep 2; done
if ! docker info >/dev/null 2>&1; then
  journalctl -u docker --no-pager -n 30 2>/dev/null | sed 's/^/    /' || true
  morir "Docker se instaló pero no arrancó (registro arriba)." "Ver la guía, \"Docker sin permisos / no arranca\"."
fi
docker compose version >/dev/null 2>&1 || morir "docker compose no responde."
ok "Docker $(docker version --format '{{.Server.Version}}') funcionando · compose $(docker compose version --short)."
