#!/usr/bin/env bash
# Factum en la nube — librería común de deploy/cloud/scripts (SDD despliegue-nube §6.5).
# No se ejecuta sola: la cargan los demás scripts con "source".
#
# Variables de ruta (todas con default):
#   FACTUM_ENV_FILE  /srv/factum/config/factum.env   (el .env real del VPS; nunca en el repo)
#   FACTUM_HOME      /srv/factum                     (lo pisa el factum.env)
#   FACTUM_DATOS     $FACTUM_HOME/datos              (ídem)
#   FACTUM_REPO      el repo que contiene este script (desplegar.sh y revertir.sh lo fijan antes de
#                    re-ejecutarse desde una copia, DT9)
#
# Variables SOLO para la prueba local del compose (guía, anexo A); en el VPS no se definen:
#   FACTUM_PROYECTO     nombre de proyecto de compose (default: el "name:" del compose, factum)
#   FACTUM_PUERTO_HTTPS puerto publicado de Caddy (default 443)
#   FACTUM_SALUD_IP     IP con la que se llega a Caddy desde este host (default 127.0.0.1)
#
# Ningún mensaje imprime secretos: el factum.env se valida por NOMBRE de clave, nunca por valor.

if [[ -n "${_FACTUM_COMUN_CARGADO:-}" ]]; then return 0; fi
_FACTUM_COMUN_CARGADO=1

FACTUM_ENV_FILE="${FACTUM_ENV_FILE:-/srv/factum/config/factum.env}"
FACTUM_HOME="${FACTUM_HOME:-/srv/factum}"
FACTUM_REPO="${FACTUM_REPO:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)}"
DIR_CLOUD="$FACTUM_REPO/deploy/cloud"
DIR_SCRIPTS="$DIR_CLOUD/scripts"

# Claves que tienen que tener valor sí o sí para levantar el compose.
CLAVES_OBLIGATORIAS=(
  FACTUM_HOME FACTUM_DATOS FACTUM_REGISTRO FACTUM_DOMINIO FACTUM_ACME_EMAIL FACTUM_JWT_SECRET
  FACTUM_MONGO_ROOT_PASSWORD FACTUM_MONGO_APP_PASSWORD FACTUM_BACKUP_AGE_RECIPIENT
)
# Claves que pueden quedar con el placeholder CAMBIAR hasta configurar el backup remoto (guía §12):
# solo las exige backup.sh sin --solo-local.
CLAVES_BACKUP_REMOTO=(FACTUM_BACKUP_DESTINO)

log() { printf '%s %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*"; }
error() { printf '%s ERROR: %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*" >&2; }
morir() { local codigo="${2:-1}"; error "$1"; exit "$codigo"; }

# Lee el factum.env SIN ejecutarlo (no es "source": un valor con espacios o con $( ) no corre nada).
# Formato: CLAVE=valor por línea; '#' al principio = comentario; comillas dobles o simples
# alrededor del valor se sacan. Sin interpolación. Exporta cada clave.
_leer_env() {
  local archivo="$1" linea clave valor n=0
  while IFS= read -r linea || [[ -n "$linea" ]]; do
    n=$((n + 1))
    linea="${linea%$'\r'}"
    [[ "$linea" =~ ^[[:space:]]*(#|$) ]] && continue
    if [[ ! "$linea" =~ ^([A-Za-z_][A-Za-z0-9_]*)=(.*)$ ]]; then
      morir "$archivo, línea $n: no tiene la forma CLAVE=valor."
    fi
    clave="${BASH_REMATCH[1]}"
    valor="${BASH_REMATCH[2]}"
    if [[ "$valor" =~ ^\"(.*)\"$ || "$valor" =~ ^\'(.*)\'$ ]]; then
      valor="${BASH_REMATCH[1]}"
    fi
    # docker compose interpreta '$' dentro de los valores del --env-file: se prohíbe para que el
    # valor que ven los scripts sea el mismo que ve el contenedor.
    if [[ "$valor" == *'$'* ]]; then
      morir "$archivo: $clave tiene un '\$'. No uses '\$' en factum.env (compose lo interpreta)."
    fi
    export "$clave=$valor"
  done <"$archivo"
}

# Carga y valida el factum.env. Corta con un mensaje claro (y sin valores) si algo falta.
cargar_env() {
  [[ -f "$FACTUM_ENV_FILE" ]] || morir "no existe $FACTUM_ENV_FILE. Copiá deploy/cloud/factum.env.example ahí (guía, sección 6)."
  [[ -r "$FACTUM_ENV_FILE" ]] || morir "no se puede leer $FACTUM_ENV_FILE (¿lo estás corriendo como el usuario deploy?)."
  _leer_env "$FACTUM_ENV_FILE"

  local clave faltan=() cambiar=()
  for clave in "${CLAVES_OBLIGATORIAS[@]}"; do
    [[ -n "${!clave:-}" ]] || faltan+=("$clave")
  done
  ((${#faltan[@]} == 0)) || morir "faltan en $FACTUM_ENV_FILE: ${faltan[*]}"

  while IFS= read -r clave; do
    [[ " ${CLAVES_BACKUP_REMOTO[*]} " == *" $clave "* ]] && continue
    [[ "${!clave:-}" == *CAMBIAR* ]] && cambiar+=("$clave")
  done < <(grep -oE '^[A-Za-z_][A-Za-z0-9_]*=' "$FACTUM_ENV_FILE" | tr -d '=')
  ((${#cambiar[@]} == 0)) || morir "todavía tienen el placeholder CAMBIAR en $FACTUM_ENV_FILE: ${cambiar[*]}"

  ((${#FACTUM_JWT_SECRET} >= 32)) || morir "FACTUM_JWT_SECRET tiene que tener 32 caracteres o más (openssl rand -hex 64)."
  if [[ "$FACTUM_DOMINIO" == *"://"* || "$FACTUM_DOMINIO" == */* ]]; then
    morir "FACTUM_DOMINIO va sin https:// y sin barras (por ejemplo factum.ejemplo.com)."
  fi
  [[ "$FACTUM_DOMINIO" =~ ^[a-z0-9]([a-z0-9.-]*[a-z0-9])?$ ]] || morir "FACTUM_DOMINIO tiene que ser un nombre de host en minúsculas."
  [[ "$FACTUM_REGISTRO" =~ ^[a-z0-9./_-]+$ ]] || morir "FACTUM_REGISTRO tiene que ir en minúsculas (ghcr.io/<dueño>)."
  for clave in FACTUM_MONGO_ROOT_PASSWORD FACTUM_MONGO_APP_PASSWORD; do
    [[ "${!clave}" =~ ^[A-Za-z0-9]{16,}$ ]] || morir "$clave tiene que tener solo letras y números, 16 o más (openssl rand -hex 24)."
  done
  [[ "$FACTUM_MONGO_ROOT_PASSWORD" != "$FACTUM_MONGO_APP_PASSWORD" ]] || morir "FACTUM_MONGO_ROOT_PASSWORD y FACTUM_MONGO_APP_PASSWORD tienen que ser distintas."
  [[ "$FACTUM_BACKUP_AGE_RECIPIENT" =~ ^age1[0-9a-z]+$ ]] || morir "FACTUM_BACKUP_AGE_RECIPIENT tiene que ser una clave pública de age (age1...)."
  for clave in 0 1; do
    local dni="FACTUM_SUPERADMIN_${clave}_DNI" nombre="FACTUM_SUPERADMIN_${clave}_NOMBRE" temporal="FACTUM_SUPERADMIN_${clave}_TEMPORAL"
    if [[ -n "${!dni:-}" ]]; then
      [[ "${!dni}" =~ ^[0-9]{7,8}$ ]] || morir "$dni tiene que tener 7 u 8 dígitos."
      [[ -n "${!nombre:-}" ]] || morir "$nombre está vacío y $dni no."
      local largo
      largo="$(printf '%s' "${!temporal:-}" | wc -m | tr -d ' ')"
      ((largo >= 10)) || morir "$temporal tiene que tener 10 caracteres o más."
    fi
  done

  FACTUM_HOME="${FACTUM_HOME%/}"
  FACTUM_DATOS="${FACTUM_DATOS%/}"
  export FACTUM_HOME FACTUM_DATOS
}

estado_dir() { printf '%s/estado' "$FACTUM_HOME"; }

# Tag desplegado (sha-<40hex> o local-<40hex>), o vacío si nunca se desplegó.
tag_actual() {
  local f
  f="$(estado_dir)/desplegado"
  [[ -s "$f" ]] && tr -d '[:space:]' <"$f" || true
}
tag_anterior() {
  local f
  f="$(estado_dir)/anterior"
  [[ -s "$f" ]] && tr -d '[:space:]' <"$f" || true
}
# sha-<40hex> | local-<40hex> -> <40hex>
sha_de_tag() { printf '%s' "${1#*-}"; }

# docker compose con el env del VPS. Suma compose.bootstrap*.yml solo si hay superadmins
# iniciales cargados (un DNI vacío hace que el backend no arranque; A1 de la SDD).
compose() {
  export FACTUM_TAG="${FACTUM_TAG:-$(tag_actual)}"
  [[ -n "$FACTUM_TAG" ]] || morir "todavía no hay ninguna versión desplegada (falta $(estado_dir)/desplegado). Hacé el primer arranque con desplegar.sh (guía, sección 7)."
  local archivos=(-f "$DIR_CLOUD/docker-compose.yml")
  [[ -n "${FACTUM_SUPERADMIN_0_DNI:-}" ]] && archivos+=(-f "$DIR_CLOUD/compose.bootstrap.yml")
  [[ -n "${FACTUM_SUPERADMIN_1_DNI:-}" ]] && archivos+=(-f "$DIR_CLOUD/compose.bootstrap-2.yml")
  local proyecto=()
  [[ -n "${FACTUM_PROYECTO:-}" ]] && proyecto=(-p "$FACTUM_PROYECTO")
  docker compose "${proyecto[@]+"${proyecto[@]}"}" --project-directory "$DIR_CLOUD" "${archivos[@]}" \
    --env-file "$FACTUM_ENV_FILE" "$@"
}

# ¿El contenedor del servicio está corriendo?
servicio_corriendo() {
  [[ -n "$(compose ps --status running -q "$1" 2>/dev/null)" ]]
}

# curl a Caddy por el dominio público pero resolviendo a este host (no depende del DNS externo).
_curl_local() {
  local ruta="$1" puerto="${FACTUM_PUERTO_HTTPS:-443}" ip="${FACTUM_SALUD_IP:-127.0.0.1}" extra=()
  # Con localhost (prueba local) Caddy usa su CA interna: no hay certificado público que validar.
  [[ "$FACTUM_DOMINIO" == localhost ]] && extra=(-k)
  curl -fsS -o /dev/null -m 10 "${extra[@]+"${extra[@]}"}" \
    --resolve "$FACTUM_DOMINIO:$puerto:$ip" "https://$FACTUM_DOMINIO:$puerto$ruta"
}

# Espera a que el backend esté healthy (180 s) y a que /health/ready y / respondan por Caddy (60 s).
esperar_salud() {
  local limite id estado
  log "Esperando que el backend esté sano (hasta 180 s)..."
  limite=$((SECONDS + 180))
  while :; do
    id="$(compose ps -q backend 2>/dev/null || true)"
    estado=""
    [[ -n "$id" ]] && estado="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{end}}' "$id" 2>/dev/null || true)"
    [[ "$estado" == healthy ]] && break
    if ((SECONDS >= limite)); then
      error "el backend no quedó sano (estado: ${estado:-sin contenedor})."
      return 1
    fi
    sleep 5
  done
  log "Backend sano. Chequeando https://$FACTUM_DOMINIO/health/ready y la web a través de Caddy (hasta 60 s)..."
  limite=$((SECONDS + 60))
  # Mientras Caddy arranca (o saca el certificado) los errores de curl son esperables: no se muestran.
  until _curl_local /health/ready 2>/dev/null && _curl_local / 2>/dev/null; do
    if ((SECONDS >= limite)); then
      _curl_local /health/ready || true
      _curl_local / || true
      error "/health/ready o la web no respondieron bien a través de Caddy."
      return 1
    fi
    sleep 5
  done
  log "OK /health/ready y la web responden."
}

# Toma un lock exclusivo sin esperar (fd 9). Sale con 3 si ya está tomado.
tomar_lock() {
  local archivo="$1" mensaje="$2"
  command -v flock >/dev/null 2>&1 || morir "falta flock (paquete util-linux)."
  mkdir -p "$(dirname "$archivo")"
  exec 9>"$archivo"
  flock -n 9 || morir "$mensaje" 3
}

# DT9: bash lee el script a medida que lo ejecuta y un "git checkout" lo reescribiría a mitad de
# camino. Los scripts que hacen checkout se re-ejecutan desde una copia en un temporal.
#   reejecutar_desde_copia "${BASH_SOURCE[0]}" "$@"
reejecutar_desde_copia() {
  local script="$1"
  shift
  if [[ -n "${FACTUM_COPIA_DIR:-}" ]]; then
    # Ya corremos desde la copia: se borra al salir.
    trap 'rm -rf "$FACTUM_COPIA_DIR"' EXIT
    return 0
  fi
  local dir
  dir="$(mktemp -d "${TMPDIR:-/tmp}/factum-deploy.XXXXXX")"
  cp "$script" "$dir/"
  cp "$DIR_SCRIPTS/_comun.sh" "$dir/"
  export FACTUM_COPIA_DIR="$dir" FACTUM_REPO
  exec bash "$dir/$(basename "$script")" "$@"
}

# ¿Quedan al menos N GiB libres en el disco de los datos?
exigir_espacio_libre() {
  local gib="$1" libres_kib
  libres_kib="$(df -Pk "$FACTUM_DATOS" | awk 'NR==2 {print $4}')"
  ((libres_kib >= gib * 1024 * 1024)) || morir "quedan menos de $gib GB libres en $FACTUM_DATOS: liberá espacio antes (guía, sección 16)."
}

# Porcentaje de uso del disco de los datos.
uso_disco() { df -P "$FACTUM_DATOS" | awk 'NR==2 {gsub("%", "", $5); print $5}'; }
