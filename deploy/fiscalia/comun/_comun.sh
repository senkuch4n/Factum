#!/usr/bin/env bash
# Factum en la fiscalía — funciones comunes de los scripts de instalación, backup, restauración y
# diagnóstico (SDD Refactorizaciones/instalacion-fiscalia.md). Se incluye con "source"; no se ejecuta solo.
#
# Dos planes, cada uno en su carpeta y con su proyecto de Docker Compose (pueden convivir en la misma PC):
#   servidor (plan A): /srv/factum-fiscalia, proyecto factum-fiscalia, Caddy en 80/443 con https://factum.internal
#   una-pc   (plan B): /srv/factum-una-pc,   proyecto factum-una-pc,   web y API solo en 127.0.0.1 (3000 y 8080)
#
# Compatible con bash 3.2 (la Mac donde se prueba) y bash 5 (las PCs Linux): sin arrays asociativos,
# sin ${x,,} y sin mapfile.

# shellcheck disable=SC2034 # varias variables las usan los scripts que incluyen este archivo
FACTUM_NOMBRE_RED="factum.internal"
FACTUM_CARPETA_SERVIDOR="/srv/factum-fiscalia"
FACTUM_CARPETA_UNA_PC="/srv/factum-una-pc"
FACTUM_CA_ARCHIVO="ca-factum-fiscalia.crt"

if [[ -t 1 ]]; then
  _V=$'\033[32m'; _A=$'\033[33m'; _R=$'\033[31m'; _N=$'\033[1m'; _0=$'\033[0m'
else
  _V=""; _A=""; _R=""; _N=""; _0=""
fi

paso() { printf '\n%s[%s/%s] %s%s\n' "$_N" "$1" "$2" "$3" "$_0"; }
ok() { printf '  %sOK%s  %s\n' "$_V" "$_0" "$*"; }
aviso() { printf '  %sAVISO%s %s\n' "$_A" "$_0" "$*"; }
falla() { printf '  %sFALLA%s %s\n' "$_R" "$_0" "$*" >&2; }
info() { printf '      %s\n' "$*"; }
morir() {
  printf '\n%sERROR:%s %s\n' "$_R" "$_0" "$1" >&2
  [[ -n "${2:-}" ]] && printf '       %s\n' "$2" >&2
  exit 1
}

es_linux() { [[ "$(uname -s)" == "Linux" ]]; }

# Pregunta S/N. Con FACTUM_SI=1 (flag --si) responde que sí sin preguntar.
confirmar() {
  local r
  [[ "${FACTUM_SI:-0}" == 1 ]] && return 0
  [[ -t 0 ]] || morir "hace falta confirmar ('$1') y no hay una terminal interactiva." "Volvé a correrlo en una terminal, o con --si."
  read -r -p "  $1 [s/N]: " r
  [[ "$r" == "s" || "$r" == "S" || "$r" == "si" || "$r" == "SI" || "$r" == "Si" ]]
}

sha256_de() { # sha256_de <archivo> → hash en minúsculas
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi
}

# Verifica SHA256SUMS ("<hash>  <ruta/relativa>") de la carpeta del paquete.
verificar_paquete() {
  local paq="$1" hash ruta real n=0
  [[ -f "$paq/SHA256SUMS" ]] || morir "el paquete no tiene SHA256SUMS ($paq)." "Copiá de nuevo la carpeta completa del pendrive."
  while IFS= read -r linea; do
    [[ -n "$linea" ]] || continue
    hash="${linea%%  *}"
    ruta="${linea#*  }"
    [[ -f "$paq/$ruta" ]] || morir "falta $ruta en el paquete." "Copiá de nuevo la carpeta completa del pendrive."
    real="$(sha256_de "$paq/$ruta")"
    [[ "$real" == "$hash" ]] || morir "$ruta está dañado (no coincide con SHA256SUMS)." "Copiá de nuevo ese archivo (o el paquete entero) desde la Mac o el pendrive."
    n=$((n + 1))
  done <"$paq/SHA256SUMS"
  ok "Paquete íntegro ($n archivos verificados con SHA256SUMS)."
}

generar_hex() { # generar_hex <bytes>
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -hex "$1"
  else
    od -An -N"$1" -tx1 /dev/urandom | tr -d ' \n'
  fi
}

# Contraseña temporal legible (sin 0/O/1/l/I): 14 caracteres, el backend pide 10 o más.
generar_temporal() {
  local alfabeto="ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789" sal="" i b
  for i in 1 2 3 4 5 6 7 8 9 10 11 12 13 14; do
    b=$((0x$(generar_hex 1) % ${#alfabeto}))
    sal="$sal${alfabeto:$b:1}"
    [[ "$i" == 7 ]] && sal="$sal-"
  done
  printf '%s' "$sal"
}

# ── .env: CLAVE=valor por línea. Nunca se hace "source" (los valores no son bash). ─────────────
env_leer() { # env_leer <archivo> <CLAVE>
  local v
  v="$(grep -E "^$2=" "$1" 2>/dev/null | tail -1 | cut -d= -f2-)" || true
  v="${v#\"}"
  v="${v%\"}"
  printf '%s' "$v"
}

env_poner() { # env_poner <archivo> <CLAVE> <valor>  (reemplaza o agrega; conserva permisos)
  local f="$1" k="$2" v="$3" tmp
  case "$v" in *" "*) v="\"$v\"" ;; esac
  tmp="$(mktemp "$f.XXXXXX")"
  if grep -qE "^$k=" "$f" 2>/dev/null; then
    awk -v k="$k" -v v="$v" 'BEGIN{FS=OFS="="} $1==k {print k "=" v; next} {print}' "$f" >"$tmp"
  else
    { cat "$f" 2>/dev/null; printf '%s=%s\n' "$k" "$v"; } >"$tmp"
  fi
  chmod 600 "$tmp"
  mv "$tmp" "$f"
}

env_vaciar() { # env_vaciar <archivo> <CLAVE>  (deja CLAVE= vacía)
  grep -qE "^$2=" "$1" 2>/dev/null || return 0
  local tmp
  tmp="$(mktemp "$1.XXXXXX")"
  awk -v k="$2" 'BEGIN{FS="="} $1==k {print k "="; next} {print}' "$1" >"$tmp"
  chmod 600 "$tmp"
  mv "$tmp" "$1"
}

# ── Docker ────────────────────────────────────────────────────────────────────────────────────
requiere_docker() {
  command -v docker >/dev/null 2>&1 || morir "Docker no está instalado en esta PC." \
    "Instalalo desde el paquete (sin internet): sudo bash herramientas/instalar-docker-offline.sh — o con el gestor de la distro (guía, sección 2.3). Después volvé a correr este script."
  if ! docker info >/dev/null 2>&1; then
    if [[ "$(id -u)" != 0 ]]; then
      morir "este usuario no tiene permiso para usar Docker." "Corré el script con sudo (sudo bash $0 ...)."
    fi
    morir "Docker está instalado pero no está funcionando." "Arrancalo con: sudo systemctl enable --now docker   y volvé a correr este script."
  fi
  docker compose version >/dev/null 2>&1 || morir "Docker no tiene el complemento 'compose' (docker compose)." \
    "Instalalo desde el paquete: sudo bash herramientas/instalar-docker-offline.sh --solo-compose (guía, sección 2.3)."
}

# Puerto TCP en escucha en esta PC (cualquier dirección). 0 = ocupado, 1 = libre, 2 = no se pudo saber.
puerto_ocupado() {
  local p="$1"
  if command -v ss >/dev/null 2>&1; then
    ss -Htln 2>/dev/null | awk '{print $4}' | grep -qE "[:.]$p\$" && return 0
    return 1
  elif command -v lsof >/dev/null 2>&1; then
    lsof -nP -iTCP:"$p" -sTCP:LISTEN >/dev/null 2>&1 && return 0
    return 1
  fi
  return 2
}

# Mongo 5+ necesita una CPU con AVX (x86-64). Sin AVX el contenedor de Mongo muere al arrancar: para esas
# PCs está el modo sin AVX (MongoDB 4.4, instalador con --sin-avx).
cpu_tiene_avx() {
  es_linux || return 0
  grep -qw avx /proc/cpuinfo 2>/dev/null
}

ram_gb() { # entero, 0 si no se sabe
  if [[ -r /proc/meminfo ]]; then
    awk '/^MemTotal:/ {printf "%d", ($2 / 1048576) + 0.5}' /proc/meminfo
  else
    echo 0
  fi
}

disco_libre_gb() { # disco_libre_gb <ruta existente>
  df -Pk "$1" 2>/dev/null | awk 'NR==2 {printf "%d", $4 / 1048576}'
}

uso_disco_pct() { # uso_disco_pct <ruta existente>
  df -Pk "$1" 2>/dev/null | awk 'NR==2 {gsub("%","",$5); print $5}'
}

# ── Instalación (carpeta, plan, compose) ─────────────────────────────────────────────────────
# Fija FACTUM_HOME, FACTUM_PLAN, FACTUM_PROYECTO a partir de una carpeta instalada.
cargar_instalacion() {
  FACTUM_HOME="$1"
  FACTUM_ENV="$FACTUM_HOME/config/.env"
  [[ -f "$FACTUM_ENV" ]] || morir "no hay una instalación de Factum en $FACTUM_HOME (falta config/.env)."
  FACTUM_PLAN="$(env_leer "$FACTUM_ENV" FACTUM_PLAN)"
  FACTUM_PROYECTO="$(env_leer "$FACTUM_ENV" COMPOSE_PROJECT_NAME)"
  FACTUM_VERSION_INSTALADA="$(env_leer "$FACTUM_ENV" FACTUM_VERSION)"
  [[ "$FACTUM_PLAN" == "servidor" || "$FACTUM_PLAN" == "una-pc" ]] || morir "config/.env de $FACTUM_HOME no dice el plan (FACTUM_PLAN)."
  [[ -n "$FACTUM_PROYECTO" ]] || morir "config/.env de $FACTUM_HOME no tiene COMPOSE_PROJECT_NAME."
}

# Busca la instalación para backup/restaurar/diagnóstico: --carpeta, o la carpeta de arriba de
# scripts/ si el script corre instalado, o la única de las dos carpetas por defecto que exista.
buscar_instalacion() { # buscar_instalacion <carpeta pedida o vacío> <dir del script>
  local pedida="$1" dir_script="$2" encontradas=""
  if [[ -n "$pedida" ]]; then
    cargar_instalacion "$pedida"
    return
  fi
  if [[ -f "$dir_script/../config/.env" ]]; then
    cargar_instalacion "$(cd "$dir_script/.." && pwd)"
    return
  fi
  [[ -f "$FACTUM_CARPETA_SERVIDOR/config/.env" ]] && encontradas="$FACTUM_CARPETA_SERVIDOR"
  if [[ -f "$FACTUM_CARPETA_UNA_PC/config/.env" ]]; then
    [[ -n "$encontradas" ]] && morir "en esta PC están instalados los dos planes." "Indicá cuál con --carpeta $FACTUM_CARPETA_SERVIDOR o --carpeta $FACTUM_CARPETA_UNA_PC."
    encontradas="$FACTUM_CARPETA_UNA_PC"
  fi
  [[ -n "$encontradas" ]] || morir "no encontré una instalación de Factum en esta PC." "Indicá la carpeta con --carpeta <ruta>."
  cargar_instalacion "$encontradas"
}

# Modo sin AVX: config/.env con FACTUM_MONGO_SIN_AVX=1 → MongoDB 4.4 (compose.sin-avx.yml).
modo_sin_avx() { [[ "$(env_leer "$FACTUM_ENV" FACTUM_MONGO_SIN_AVX)" == 1 ]]; }

# Shell de Mongo dentro del contenedor: "mongo" en el modo sin AVX (4.4), "mongosh" con Mongo 7.
mongo_shell() { if modo_sin_avx; then echo mongo; else echo mongosh; fi; }

# docker compose con los archivos de la instalación. El superadmin inicial solo se suma si su DNI
# tiene valor en config/.env (un DNI vacío hace que el backend no arranque). En el modo sin AVX se suma
# compose.sin-avx.yml (MongoDB 4.4).
fc() {
  local extra=()
  modo_sin_avx && extra=(-f "$FACTUM_HOME/compose.sin-avx.yml")
  [[ -n "$(env_leer "$FACTUM_ENV" FACTUM_SUPERADMIN_0_DNI)" ]] && extra=(${extra[@]+"${extra[@]}"} -f "$FACTUM_HOME/compose.superadmin.yml")
  docker compose --project-directory "$FACTUM_HOME" -p "$FACTUM_PROYECTO" --env-file "$FACTUM_ENV" \
    -f "$FACTUM_HOME/docker-compose.yml" -f "$FACTUM_HOME/compose.$FACTUM_PLAN.yml" \
    ${extra[@]+"${extra[@]}"} "$@"
}

servicios_del_plan() {
  if [[ "$FACTUM_PLAN" == "servidor" ]]; then echo "mongo backend frontend caddy"; else echo "mongo backend frontend"; fi
}

estado_servicio() { # healthy | starting | unhealthy | running (sin healthcheck) | ausente | <estado>
  local id st
  id="$(fc ps -q "$1" 2>/dev/null | head -1)"
  [[ -n "$id" ]] || { echo ausente; return; }
  st="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$id" 2>/dev/null)"
  echo "${st:-ausente}"
}

# Espera a que todos los servicios estén sanos. Devuelve 1 si se cumple el tiempo.
esperar_sano() { # esperar_sano <segundos>
  local limite=$((SECONDS + $1)) s st todos
  while :; do
    todos=1
    for s in $(servicios_del_plan); do
      st="$(estado_servicio "$s")"
      case "$st" in healthy) ;; running) [[ "$s" == caddy ]] || todos=0 ;; *) todos=0 ;; esac
    done
    ((todos)) && return 0
    ((SECONDS < limite)) || return 1
    sleep 5
  done
}

mostrar_estado() {
  local s
  for s in $(servicios_del_plan); do info "$(printf '%-9s %s' "$s" "$(estado_servicio "$s")")"; done
}

mostrar_log_backend() {
  echo "  Últimas líneas del registro del backend:"
  fc logs --no-color --tail 40 backend 2>&1 | sed 's/^/    /' || true
}

# Ruta de la raíz de la CA de Caddy (plan A) dentro de los datos de la instalación.
ruta_ca_caddy() { echo "$(env_leer "$FACTUM_ENV" FACTUM_DATOS)/caddy-data/caddy/pki/authorities/local/root.crt"; }

# Primera IPv4 que no sea de loopback ni de Docker.
ips_de_esta_pc() {
  if command -v ip >/dev/null 2>&1; then
    ip -4 -o addr show scope global 2>/dev/null | awk '{print $2, $4}' | grep -vE '^(docker|br-|veth|virbr)' | awk '{sub("/.*","",$2); print $2 " (" $1 ")"}'
  elif command -v hostname >/dev/null 2>&1 && hostname -I >/dev/null 2>&1; then
    hostname -I | tr ' ' '\n' | grep -E '^[0-9.]+$'
  fi
}

# /etc/hosts: deja UNA línea "<ip> factum.internal" (reemplaza las anteriores de ese nombre).
hosts_fijar() { # hosts_fijar <ip> [archivo]
  local ip="$1" f="${2:-/etc/hosts}" tmp
  if grep -qE "^[[:space:]]*${ip}[[:space:]]+$FACTUM_NOMBRE_RED([[:space:]]|\$)" "$f" &&
    [[ "$(grep -cE "[[:space:]]$FACTUM_NOMBRE_RED([[:space:]]|\$)" "$f")" == 1 ]]; then
    return 0
  fi
  tmp="$(mktemp)"
  grep -vE "^[^#]*[[:space:]]$FACTUM_NOMBRE_RED([[:space:]]|\$)" "$f" >"$tmp" || true
  printf '%s %s # Factum fiscalía\n' "$ip" "$FACTUM_NOMBRE_RED" >>"$tmp"
  cat "$tmp" >"$f" # cat > conserva dueño, permisos y el inodo (/etc/hosts puede ser un bind mount)
  rm -f "$tmp"
}

hosts_quitar() { # hosts_quitar [archivo]
  local f="${1:-/etc/hosts}" tmp
  grep -qE "^[^#]*[[:space:]]$FACTUM_NOMBRE_RED([[:space:]]|\$)" "$f" || return 0
  tmp="$(mktemp)"
  grep -vE "^[^#]*[[:space:]]$FACTUM_NOMBRE_RED([[:space:]]|\$)" "$f" >"$tmp" || true
  cat "$tmp" >"$f"
  rm -f "$tmp"
}

# Usuario "real" detrás de sudo (para el Escritorio, ~/Applications y ~/.config).
usuario_real() {
  if [[ -n "${FACTUM_USUARIO:-}" ]]; then echo "$FACTUM_USUARIO"
  elif [[ -n "${SUDO_USER:-}" && "$SUDO_USER" != root ]]; then echo "$SUDO_USER"
  else id -un
  fi
}

home_de() { # home_de <usuario>
  local h=""
  command -v getent >/dev/null 2>&1 && h="$(getent passwd "$1" | cut -d: -f6)"
  [[ -n "$h" ]] || h="$(eval echo "~$1")"
  echo "$h"
}

como_usuario() { # como_usuario <usuario> <comando...>: corre como el usuario real si somos root
  local u="$1"
  shift
  if [[ "$(id -u)" == 0 && "$u" != root ]] && command -v sudo >/dev/null 2>&1; then
    sudo -u "$u" -H "$@"
  else
    "$@"
  fi
}
