#!/usr/bin/env bash
# Factum en la nube — despliega una versión en el VPS (SDD despliegue-nube §6.5.1). Corre como deploy.
#
#   desplegar.sh --tag sha-<40 hex>   versión publicada en GHCR por el workflow "Desplegar producción"
#                                     (es lo que corre el deploy automático por SSH)
#   desplegar.sh --construir-local    plan B: arma las imágenes EN el VPS desde el checkout actual
#                                     (tag local-<sha>; necesita RAM, ver guía sección 7)
#   desplegar.sh --estado             qué versión corre y cómo están los contenedores
#
# Pasos: lock -> factum.env -> checkout del commit (tiene que estar en origin/main) -> imágenes ->
# guarda de dominio -> backup local pre-deploy -> up -d -> salud -> si falla, rollback automático
# a la versión anterior (solo código, no datos; el backup pre-deploy queda para eso).
#
# Códigos de salida: 0 OK; 1 falló y se volvió a la anterior (o no había anterior); 2 falló el
# rollback o argumentos inválidos; 3 hay otro deploy en curso; 4 guarda de dominio o de commit.
set -euo pipefail
# shellcheck source=deploy/cloud/scripts/_comun.sh
source "$(dirname "${BASH_SOURCE[0]}")/_comun.sh"

uso() {
  echo "Uso: $0 --tag sha-<40 hex> | --construir-local | --estado" >&2
  exit 2
}

MODO=""
TAG=""
case "${1:-}" in
  --tag)
    [[ $# -eq 2 && "$2" =~ ^sha-[0-9a-f]{40}$ ]] || uso
    MODO=tag
    TAG="$2"
    ;;
  --construir-local) [[ $# -eq 1 ]] || uso; MODO=local ;;
  --estado) [[ $# -eq 1 ]] || uso; MODO=estado ;;
  *) uso ;;
esac

if [[ "$MODO" == estado ]]; then
  cargar_env
  echo "Desplegado: $(tag_actual || true)"
  echo "Anterior:   $(tag_anterior || true)"
  if [[ -n "$(tag_actual)" ]]; then
    compose ps --format 'table {{.Service}}\t{{.Status}}'
  fi
  exit 0
fi

# DT9: desde acá puede haber un git checkout que reescriba este archivo.
reejecutar_desde_copia "${BASH_SOURCE[0]}" "$@"

cargar_env
tomar_lock "$(estado_dir)/.lock" "hay otro deploy en curso (o un restore). Probá de nuevo en unos minutos."
exigir_espacio_libre 2

PREVIO="$(tag_actual)"
HEAD_PREVIO="$(git -C "$FACTUM_REPO" rev-parse HEAD)"

volver_checkout() {
  git -C "$FACTUM_REPO" checkout --quiet --detach "$1" || error "no se pudo volver el checkout a ${1:0:8}."
}

# ── 1. Código: el compose, el Caddyfile y los scripts tienen que corresponder a las imágenes ──
if [[ "$MODO" == tag ]]; then
  SHA="$(sha_de_tag "$TAG")"
  log "Desplegando ${SHA:0:8} (anterior: ${PREVIO:-ninguna})."
  git -C "$FACTUM_REPO" fetch --quiet origin main ||
    morir "no se pudo actualizar el repo desde GitHub (¿la deploy key de la sección 5.1 sigue vigente?)." 1
  if ! git -C "$FACTUM_REPO" cat-file -e "$SHA^{commit}" 2>/dev/null ||
    ! git -C "$FACTUM_REPO" merge-base --is-ancestor "$SHA" origin/main; then
    morir "el commit ${SHA:0:8} no está en origin/main: no se despliega (DT7)." 4
  fi
  git -C "$FACTUM_REPO" checkout --quiet --detach "$SHA" ||
    morir "no se pudo hacer checkout de ${SHA:0:8}: ¿alguien editó archivos dentro de $FACTUM_REPO? (git -C $FACTUM_REPO status)" 1
else
  SHA="$(git -C "$FACTUM_REPO" rev-parse HEAD)"
  TAG="local-$SHA"
  log "Plan B: armando las imágenes en este servidor desde ${SHA:0:8} (anterior: ${PREVIO:-ninguna})."
fi
export FACTUM_TAG="$TAG"
URL_PUBLICA="https://$FACTUM_DOMINIO"
IMG_BACK="$FACTUM_REGISTRO/factum-backend:$TAG"
IMG_FRONT="$FACTUM_REGISTRO/factum-frontend:$TAG"

# ── 2. Imágenes ──────────────────────────────────────────────────────────────────────────────
if [[ "$MODO" == tag ]]; then
  log "Bajando las imágenes de $FACTUM_REGISTRO..."
  if ! compose pull --quiet backend frontend; then
    volver_checkout "$HEAD_PREVIO"
    morir "no se pudieron bajar las imágenes $TAG (¿el workflow terminó? ¿docker login ghcr.io vigente?)." 1
  fi
else
  log "docker build del backend..."
  docker build --pull -t "$IMG_BACK" "$FACTUM_REPO/server/src/Factum.Backend"
  log "docker build del frontend (NEXT_PUBLIC_BACKEND_URL=$URL_PUBLICA)..."
  docker build --pull -t "$IMG_FRONT" \
    --build-arg "NEXT_PUBLIC_BACKEND_URL=$URL_PUBLICA" \
    --build-arg "NEXT_PUBLIC_AGENT_URL=${FACTUM_AGENT_URL:-http://localhost:8765}" \
    --build-arg "NEXT_PUBLIC_TATANA_DOWNLOAD_URL=${FACTUM_TATANA_DESCARGA_URL:-}" \
    --label "factum.url_publica=$URL_PUBLICA" \
    "$FACTUM_REPO/client"
fi

# ── 3. Guarda de dominio (DT2): el front horneó la URL pública en el build ─────────────────────
URL_IMAGEN="$(docker image inspect --format '{{ index .Config.Labels "factum.url_publica" }}' "$IMG_FRONT" 2>/dev/null || true)"
if [[ "$URL_IMAGEN" != "$URL_PUBLICA" ]]; then
  volver_checkout "$HEAD_PREVIO"
  morir "el front se compiló para '${URL_IMAGEN:-<sin dato>}' y este servidor es $URL_PUBLICA. Revisá la variable FACTUM_URL_PUBLICA en GitHub (o FACTUM_DOMINIO en factum.env) y volvé a correr el workflow. No se tocó lo que está corriendo." 4
fi

# ── 4. Backup local previo (solo si ya hay una base corriendo) ───────────────────────────────
# (backup.sh anota en version.txt la versión que corre ahora, la de estado/desplegado.)
if [[ -n "$PREVIO" ]] && servicio_corriendo mongo; then
  log "Backup local pre-deploy..."
  if ! "$DIR_SCRIPTS/backup.sh" --solo-local --motivo "pre-deploy-${SHA:0:8}"; then
    volver_checkout "$HEAD_PREVIO"
    morir "falló el backup pre-deploy: no se despliega sin backup. Mirá el error de arriba (guía, sección 16)." 1
  fi
fi

# ── 5. Levantar la versión nueva ─────────────────────────────────────────────────────────────
log "Levantando $TAG..."
UP_OK=1
compose up -d --remove-orphans || UP_OK=0

if ((UP_OK)) && esperar_salud; then
  mkdir -p "$(estado_dir)"
  if [[ -n "$PREVIO" && "$PREVIO" != "$TAG" ]]; then
    printf '%s\n' "$PREVIO" >"$(estado_dir)/anterior"
  fi
  printf '%s\n' "$TAG" >"$(estado_dir)/desplegado"
  # Limpieza: solo quedan las imágenes de factum de la versión desplegada y la anterior.
  ANTERIOR="$(tag_anterior)"
  while IFS= read -r img; do
    case "${img##*:}" in
      "$TAG" | "${ANTERIOR:-<ninguno>}") ;;
      *) docker image rm "$img" >/dev/null 2>&1 || true ;;
    esac
  done < <(docker image ls --format '{{.Repository}}:{{.Tag}}' |
    grep -E "^$FACTUM_REGISTRO/factum-(backend|frontend):(sha|local)-[0-9a-f]{40}$" || true)
  log "OK desplegado ${SHA:0:8}"
  exit 0
fi

# ── 6. Falló: rollback automático (DT10) ─────────────────────────────────────────────────────
error "la versión ${SHA:0:8} no quedó sana. Últimas líneas del backend:"
compose logs --no-color --tail 80 backend >&2 || true

if [[ -z "$PREVIO" ]]; then
  error "no hay versión anterior a la que volver (primer arranque): los contenedores quedan como están para diagnosticar (guía, sección 16)."
  exit 1
fi

log "Volviendo a ${PREVIO}..."
volver_checkout "$(sha_de_tag "$PREVIO")"
export FACTUM_TAG="$PREVIO"
if compose up -d --remove-orphans && esperar_salud; then
  log "ROLLBACK a $(sha_de_tag "$PREVIO" | cut -c1-8) OK: producción sigue con la versión anterior."
  exit 1
fi
error "ROLLBACK FALLÓ: revisá 'compose.sh ps' y 'compose.sh logs backend' ya (guía, sección 16)."
exit 2
