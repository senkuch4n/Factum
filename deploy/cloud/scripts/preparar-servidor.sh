#!/usr/bin/env bash
# Factum en la nube — preparación del VPS, UNA sola vez, como root (SDD despliegue-nube §6.5).
# Ubuntu 24.04 LTS x86_64. Idempotente: se puede volver a correr. Pide confirmación en cada bloque.
#
#   scp deploy/cloud/scripts/preparar-servidor.sh deploy/cloud/scripts/deploy-forzado.sh root@<IP>:/root/
#   ssh root@<IP>
#   bash /root/preparar-servidor.sh --clave-admin /root/admin.pub
#
# Bloques: 1) paquetes y actualizaciones automáticas; 2) Docker Engine + compose (repo oficial);
# 3) ufw (22, 80, 443/tcp y 443/udp); 4) swap de 2 GB; 5) usuario admin (sudo, con tu clave);
# 6) usuario deploy (sin sudo, grupo docker); 7) árbol /srv/factum; 8) deploy-forzado.sh en
# /srv/factum/bin; 9) sshd sin contraseña ni root (solo si admin tiene clave); 10) chequeo de AVX.
# Opción --si: no pregunta (contesta que sí a todo).
set -euo pipefail

CLAVE_ADMIN=""
SIEMPRE_SI=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --clave-admin) CLAVE_ADMIN="${2:-}"; shift 2 ;;
    --si) SIEMPRE_SI=1; shift ;;
    *) echo "Uso: $0 --clave-admin <archivo.pub> [--si]" >&2; exit 2 ;;
  esac
done

DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RAIZ=/srv/factum

log() { printf '\n== %s\n' "$*"; }
morir() { echo "ERROR: $*" >&2; exit 1; }
confirmar() {
  ((SIEMPRE_SI)) && return 0
  local r
  read -r -p "$1 [s/N] " r
  [[ "$r" =~ ^[sSyY]$ ]]
}

[[ "$(id -u)" == 0 ]] || morir "este script se corre como root."
[[ "$(uname -m)" == x86_64 ]] || morir "se esperaba x86_64 y este servidor es $(uname -m)."
# shellcheck disable=SC1091 # existe en Ubuntu
. /etc/os-release
[[ "${ID:-}" == ubuntu ]] || morir "se esperaba Ubuntu (24.04 LTS) y esto es ${PRETTY_NAME:-desconocido}."
[[ "${VERSION_ID:-}" == 24.04 ]] || echo "AVISO: la guía está probada para Ubuntu 24.04; esto es ${PRETTY_NAME}."

# ── 10 (primero): AVX. Mongo 5+ lo exige en x86_64; sin AVX no tiene sentido seguir ─────────────
log "Chequeo de AVX (Mongo 7 lo necesita)"
if grep -m1 -o -w avx /proc/cpuinfo >/dev/null; then
  echo "OK: el CPU tiene AVX."
else
  morir "el CPU de este VPS NO tiene AVX y Mongo 7 no va a arrancar. Pedí a DonWeb un plan/CPU con AVX (guía, sección 2)."
fi

# ── 1. Paquetes ──────────────────────────────────────────────────────────────────────────────
if confirmar "1) ¿Actualizar paquetes e instalar unattended-upgrades, git, curl, age, rclone, ufw?"; then
  export DEBIAN_FRONTEND=noninteractive
  apt-get update
  apt-get -y upgrade
  apt-get install -y unattended-upgrades git curl ca-certificates gnupg age rclone ufw util-linux
  dpkg-reconfigure -f noninteractive unattended-upgrades
fi

# ── 2. Docker Engine + compose desde el repo apt oficial de Docker ──────────────────────────
if confirmar "2) ¿Instalar Docker Engine y el plugin compose (repo oficial de Docker)?"; then
  if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
    echo "Docker ya está instalado: $(docker --version)"
  else
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME} stable" \
      >/etc/apt/sources.list.d/docker.list
    apt-get update
    apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
  fi
  systemctl enable --now docker
fi

# ── 3. Firewall del sistema ──────────────────────────────────────────────────────────────────
# Ojo: Docker publica puertos saltándose ufw. Por eso el compose solo publica los de Caddy.
if confirmar "3) ¿Configurar ufw (entrada denegada salvo 22/tcp, 80/tcp, 443/tcp y 443/udp)?"; then
  ufw default deny incoming
  ufw default allow outgoing
  ufw allow 22/tcp
  ufw allow 80/tcp
  ufw allow 443/tcp
  ufw allow 443/udp
  ufw --force enable
  ufw status verbose
fi

# ── 4. Swap ──────────────────────────────────────────────────────────────────────────────────
if [[ -z "$(swapon --show --noheadings)" ]]; then
  if confirmar "4) No hay swap. ¿Crear /swapfile de 2 GB?"; then
    fallocate -l 2G /swapfile
    chmod 600 /swapfile
    mkswap /swapfile
    swapon /swapfile
    grep -q '^/swapfile ' /etc/fstab || echo '/swapfile none swap sw 0 0' >>/etc/fstab
  fi
else
  log "4) Ya hay swap: $(swapon --show --noheadings | tr -s ' ')"
fi

# ── 5. Usuario admin (sudo, con tu clave) ────────────────────────────────────────────────────
if confirmar "5) ¿Crear/actualizar el usuario admin (sudo) con la clave de --clave-admin?"; then
  id admin >/dev/null 2>&1 || adduser --disabled-password --gecos "" admin
  usermod -aG sudo admin
  # Sin contraseña: sudo sin pedirla (el acceso ya está protegido por la clave SSH).
  echo 'admin ALL=(ALL) NOPASSWD:ALL' >/etc/sudoers.d/90-factum-admin
  chmod 440 /etc/sudoers.d/90-factum-admin
  visudo -cf /etc/sudoers.d/90-factum-admin
  if [[ -n "$CLAVE_ADMIN" ]]; then
    [[ -f "$CLAVE_ADMIN" ]] || morir "no existe $CLAVE_ADMIN."
    install -d -m 700 -o admin -g admin /home/admin/.ssh
    touch /home/admin/.ssh/authorized_keys
    while IFS= read -r clave; do
      [[ -z "$clave" ]] && continue
      grep -qxF "$clave" /home/admin/.ssh/authorized_keys || echo "$clave" >>/home/admin/.ssh/authorized_keys
    done <"$CLAVE_ADMIN"
    chown admin:admin /home/admin/.ssh/authorized_keys
    chmod 600 /home/admin/.ssh/authorized_keys
  else
    echo "AVISO: sin --clave-admin; admin queda sin clave SSH (el bloque 9 no se va a aplicar)."
  fi
fi

# ── 6. Usuario deploy (sin sudo, grupo docker) ───────────────────────────────────────────────
if confirmar "6) ¿Crear el usuario deploy (sin sudo, en el grupo docker, sin contraseña)?"; then
  id deploy >/dev/null 2>&1 || adduser --disabled-password --gecos "" deploy
  getent group docker >/dev/null || groupadd docker
  usermod -aG docker deploy
  install -d -m 700 -o deploy -g deploy /home/deploy/.ssh
  touch /home/deploy/.ssh/authorized_keys
  chown deploy:deploy /home/deploy/.ssh/authorized_keys
  chmod 600 /home/deploy/.ssh/authorized_keys
  echo "Aviso: el grupo docker equivale a root en este host. Lo que acota la clave de CI es el"
  echo "command= forzado de authorized_keys (guía, sección 11), no la falta de sudo."
fi

# ── 7. Árbol /srv/factum ─────────────────────────────────────────────────────────────────────
if confirmar "7) ¿Crear el árbol $RAIZ con dueño deploy?"; then
  id deploy >/dev/null 2>&1 || morir "falta el usuario deploy (bloque 6)."
  for d in repo config config/branding datos datos/mongo datos/backend datos/caddy-data datos/caddy-config \
    descargas backups estado bin; do
    install -d -o deploy -g deploy "$RAIZ/$d"
  done
  chmod 700 "$RAIZ/config" "$RAIZ/backups"
  # datos/mongo: el entrypoint de mongo le cambia el dueño a mongodb (uid 999) en el primer arranque.
  ls -la "$RAIZ"
fi

# ── 8. Comando forzado de la clave de CI ─────────────────────────────────────────────────────
if confirmar "8) ¿Instalar deploy-forzado.sh en $RAIZ/bin/?"; then
  [[ -f "$DIR_SCRIPT/deploy-forzado.sh" ]] || morir "falta $DIR_SCRIPT/deploy-forzado.sh (copialo junto a este script)."
  install -d -o deploy -g deploy "$RAIZ/bin"
  # Dueño root y sin escritura para deploy: la clave de CI no puede cambiar su propio comando.
  install -m 755 -o root -g root "$DIR_SCRIPT/deploy-forzado.sh" "$RAIZ/bin/deploy-forzado.sh"
  chown root:root "$RAIZ/bin"
  chmod 755 "$RAIZ/bin"
  echo "OK $RAIZ/bin/deploy-forzado.sh"
fi

# ── 9. sshd: sin contraseña ni root (al final, y solo si admin ya tiene clave) ─────────────────
if [[ -s /home/admin/.ssh/authorized_keys ]]; then
  echo
  echo "IMPORTANTE: antes de seguir, abrí OTRA terminal y probá:  ssh admin@<IP>  y después  sudo -v"
  echo "Si eso no anda, contestá que NO acá y revisá la clave: si no, te podés quedar afuera."
  if confirmar "9) ¿Desactivar el login por contraseña y el de root en sshd?"; then
    cat >/etc/ssh/sshd_config.d/90-factum.conf <<'SSHD'
# Factum (preparar-servidor.sh): solo claves, sin root.
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin no
SSHD
    sshd -t
    systemctl reload ssh 2>/dev/null || systemctl reload sshd
    echo "OK sshd recargado. Desde ahora se entra como admin (o deploy) con clave."
  fi
else
  echo "9) Se saltea: admin no tiene clave SSH cargada."
fi

log "Listo. Seguí con la guía (sección 4: dominio provisorio)."
echo "AVX: $(grep -m1 -o -w avx /proc/cpuinfo)   Arquitectura: $(uname -m)"
