#!/usr/bin/env bash
# Diagnóstico de la PC para decidir cómo instalar Tatana (SDD instalacion-fiscalia §7): dice en castellano la distro,
# glibc, arquitectura, RAM, disco, si hay Docker y escritorio, y RECOMIENDA Tatana nativo (AppImage) o Tatana en Docker.
# No cambia nada ni pide contraseña. Anda solo (no necesita el resto del paquete): se puede copiar suelto a un pendrive.
#
#   ./diagnostico-distro.sh
set -uo pipefail

if [[ -t 1 ]]; then V=$'\e[32m'; A=$'\e[33m'; R=$'\e[31m'; N=$'\e[1m'; C=$'\e[0m'; else V=""; A=""; R=""; N=""; C=""; fi
si()   { printf '  %-22s %s\n' "$1" "${V}$2${C}"; }
mas()  { printf '  %-22s %s\n' "$1" "${A}$2${C}"; }
no()   { printf '  %-22s %s\n' "$1" "${R}$2${C}"; }
dato() { printf '  %-22s %s\n' "$1" "$2"; }

# version_ge A B → 0 si A >= B (números separados por puntos).
version_ge() {
  awk -v a="$1" -v b="$2" 'BEGIN {
    na = split(a, x, "."); nb = split(b, y, "."); n = (na > nb ? na : nb)
    for (i = 1; i <= n; i++) { xi = x[i] + 0; yi = y[i] + 0; if (xi > yi) exit 0; if (xi < yi) exit 1 }
    exit 0 }'
}

GLIBC_MINIMA="2.35"
RAM_MINIMA_MB=4096
DISCO_MINIMO_GB=10

echo "${N}Diagnóstico de esta PC para Tatana${C}  ($(date '+%Y-%m-%d %H:%M'))"
echo

# ── Sistema ──────────────────────────────────────────────────────────────────────────────────────────────────────
ARQ="$(uname -m)"
if [[ "$ARQ" == x86_64 ]]; then si "Arquitectura" "$ARQ (Intel/AMD de 64 bits)"; else no "Arquitectura" "$ARQ (Tatana necesita x86_64)"; fi

DISTRO="desconocida"
if [[ -r /etc/os-release ]]; then
  # shellcheck disable=SC1091
  DISTRO="$(. /etc/os-release && echo "${PRETTY_NAME:-${NAME:-?} ${VERSION_ID:-}}")"
fi
dato "Distro" "$DISTRO"
dato "Kernel" "$(uname -r)"

GLIBC=""
if command -v getconf >/dev/null 2>&1; then GLIBC="$(getconf GNU_LIBC_VERSION 2>/dev/null | awk '{print $2}')"; fi
if [[ -z "$GLIBC" ]] && command -v ldd >/dev/null 2>&1; then
  GLIBC="$(ldd --version 2>&1 | head -1 | grep -oE '[0-9]+\.[0-9]+' | tail -1)"
fi
ES_MUSL=0
if command -v ldd >/dev/null 2>&1 && ldd --version 2>&1 | grep -qi musl; then ES_MUSL=1; fi
GLIBC_OK=0
if [[ "$ES_MUSL" == 1 ]]; then
  no "Biblioteca C" "musl (no glibc): Tatana nativo no anda"
elif [[ -z "$GLIBC" ]]; then
  no "glibc" "no se pudo leer"
elif version_ge "$GLIBC" "$GLIBC_MINIMA"; then
  si "glibc" "$GLIBC (Tatana nativo pide $GLIBC_MINIMA o más)"; GLIBC_OK=1
else
  no "glibc" "$GLIBC (Tatana nativo pide $GLIBC_MINIMA o más)"
fi

RAM_MB="$(awk '/^MemTotal:/ {printf "%d", $2/1024}' /proc/meminfo 2>/dev/null)"
if [[ -z "$RAM_MB" ]]; then dato "RAM" "no se pudo leer"
elif (( RAM_MB >= 8192 )); then si "RAM" "$((RAM_MB / 1024)) GB"
elif (( RAM_MB >= RAM_MINIMA_MB - 256 )); then mas "RAM" "$((RAM_MB / 1024)) GB (alcanza; 8 GB recomendados para grabar la sesión completa)"
else no "RAM" "$((RAM_MB / 1024)) GB (pocas: Tatana pide 4 GB)"; fi

LIBRE_KB="$(df -Pk "$HOME" 2>/dev/null | awk 'NR == 2 {print $4}')"
if [[ -n "$LIBRE_KB" ]]; then
  LIBRE_GB=$((LIBRE_KB / 1024 / 1024))
  if (( LIBRE_GB >= DISCO_MINIMO_GB )); then si "Disco libre (home)" "$LIBRE_GB GB"
  else no "Disco libre (home)" "$LIBRE_GB GB (hacen falta $DISCO_MINIMO_GB GB: la evidencia de video ocupa)"; fi
else
  dato "Disco libre (home)" "no se pudo leer"
fi

# ── Escritorio y servicios (lo que pide el AppImage) ────────────────────────────────────────────────────────────
echo
SYSTEMD=0; [[ -d /run/systemd/system ]] && SYSTEMD=1
if [[ "$SYSTEMD" == 1 ]]; then si "systemd" "sí"; else mas "systemd" "no (el AppImage lo necesita)"; fi
FUSE=0; command -v fusermount3 >/dev/null 2>&1 && FUSE=1
if [[ "$FUSE" == 1 ]]; then si "FUSE 3" "sí (fusermount3)"; else mas "FUSE 3" "no (el AppImage lo necesita: paquete fuse3)"; fi
SESION="${XDG_SESSION_TYPE:-}"
[[ -n "$SESION" ]] || { [[ -n "${WAYLAND_DISPLAY:-}" ]] && SESION=wayland; }
[[ -n "$SESION" ]] || { [[ -n "${DISPLAY:-}" ]] && SESION=x11; }
ESCRITORIO=0
case "$SESION" in x11|wayland) ESCRITORIO=1; si "Escritorio" "$SESION" ;; *) mas "Escritorio" "no detectado (¿se corrió por SSH?)" ;; esac
if command -v systemctl >/dev/null 2>&1 && systemctl is-active --quiet usbmuxd 2>/dev/null; then
  dato "usbmuxd del sistema" "andando (Tatana en Docker pide detenerlo al instalar)"
elif [[ -x /usr/sbin/usbmuxd || -x /usr/bin/usbmuxd ]]; then
  dato "usbmuxd del sistema" "instalado, detenido"
else
  dato "usbmuxd del sistema" "no instalado"
fi

# ── Docker ───────────────────────────────────────────────────────────────────────────────────────────────────────
echo
DOCKER=0; DOCKER_ANDA=0; DOCKER_SIN_ROOT=0
if command -v docker >/dev/null 2>&1; then
  DOCKER=1
  if docker info >/dev/null 2>&1; then
    DOCKER_ANDA=1
    si "Docker" "$(docker version --format '{{.Server.Version}}' 2>/dev/null) (tu usuario lo puede usar)"
    docker info --format '{{.SecurityOptions}}' 2>/dev/null | grep -q rootless && DOCKER_SIN_ROOT=1
  elif command -v systemctl >/dev/null 2>&1 && systemctl is-active --quiet docker 2>/dev/null; then
    DOCKER_ANDA=1
    mas "Docker" "instalado y andando, pero tu usuario no está en el grupo docker (los scripts usan sudo)"
  else
    mas "Docker" "instalado pero el servicio no responde (sudo systemctl start docker)"
  fi
  if [[ "$DOCKER_SIN_ROOT" == 1 ]]; then
    no "Docker sin root" "es Docker 'rootless': no puede darle los USB al contenedor"
  fi
else
  mas "Docker" "no está (el paquete de la fiscalía trae instalar-docker-offline.sh)"
fi
if [[ -f /sys/fs/cgroup/cgroup.controllers ]]; then dato "cgroups" "v2"; else dato "cgroups" "v1"; fi
KERNEL_OK=0; version_ge "$(uname -r | grep -oE '^[0-9]+\.[0-9]+')" "3.10" && KERNEL_OK=1

# ── Recomendación ───────────────────────────────────────────────────────────────────────────────────────────────
echo
echo "${N}Recomendación${C}"
if [[ "$ARQ" != x86_64 ]]; then
  echo "  ${R}Ninguna de las dos:${C} Tatana solo corre en PCs Intel/AMD de 64 bits (x86_64). Usá otra PC."
  exit 0
fi
NATIVO_OK=0
[[ "$GLIBC_OK" == 1 && "$SYSTEMD" == 1 && "$FUSE" == 1 && "$ESCRITORIO" == 1 ]] && NATIVO_OK=1
if [[ "$NATIVO_OK" == 1 ]]; then
  echo "  ${V}Tatana nativo (AppImage).${C} Esta PC cumple todo lo que pide: es lo más simple (ventana propia,"
  echo "  actualizaciones, \"Preparar esta PC\"). Tatana en Docker queda como respaldo si el AppImage fallara."
elif [[ "$GLIBC_OK" == 1 && "$SYSTEMD" == 1 && "$FUSE" == 0 && "$ESCRITORIO" == 1 ]]; then
  echo "  ${V}Tatana nativo (AppImage)${C}, después de instalar FUSE 3 (sudo apt install fuse3 / sudo dnf install fuse3)."
  echo "  Si no se puede instalar nada, Tatana en Docker."
else
  echo "  ${V}Tatana en Docker.${C} El AppImage no anda acá:"
  [[ "$GLIBC_OK" == 1 ]] || echo "    - la glibc de esta distro es más vieja que $GLIBC_MINIMA (o no es glibc)"
  [[ "$SYSTEMD" == 1 ]] || echo "    - no hay systemd"
  [[ "$ESCRITORIO" == 1 ]] || echo "    - no se detectó escritorio gráfico (si corriste esto por SSH, repetilo en la PC)"
  [[ "$FUSE" == 1 ]] || echo "    - falta FUSE 3"
  if [[ "$DOCKER" == 0 ]]; then
    echo "  Primero instalá Docker sin internet: instalar-docker-offline.sh del paquete de la fiscalía."
  elif [[ "$DOCKER_ANDA" == 0 ]]; then
    echo "  Primero hacé andar Docker: sudo systemctl enable --now docker"
  fi
  [[ "$DOCKER_SIN_ROOT" == 0 ]] || echo "  ${R}Docker rootless no sirve para el USB:${C} hace falta el Docker normal (de sistema)."
  [[ "$KERNEL_OK" == 1 ]] || echo "  ${R}El kernel es demasiado viejo para Docker${C} (hace falta 3.10 o más)."
  echo "  Después: ./instalar-tatana-docker.sh  (plan A agregá --origen https://factum.internal)."
fi
if [[ "$ESCRITORIO" == 0 && "$NATIVO_OK" == 0 ]]; then
  echo "  Nota: sin escritorio igual se necesita un navegador en la PC del perito para usar Factum."
fi
