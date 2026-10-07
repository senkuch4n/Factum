#!/usr/bin/env bash
# Factum en la nube — rollback MANUAL a la versión anterior (SDD despliegue-nube §6.5, DT10).
# Corre como deploy:   revertir.sh
#
# Vuelve el checkout y las imágenes al tag de estado/anterior (el mismo mecanismo que el rollback
# automático de desplegar.sh) y, si queda sano, intercambia desplegado <-> anterior.
# Solo revierte CÓDIGO, no datos: si una versión rompió datos, ver la guía, sección 13
# (restaurar un backup pre-deploy).
set -euo pipefail
# shellcheck source=deploy/cloud/scripts/_comun.sh
source "$(dirname "${BASH_SOURCE[0]}")/_comun.sh"

[[ $# -eq 0 ]] || { echo "Uso: $0   (sin argumentos)" >&2; exit 2; }

# Hace git checkout: corre desde una copia (DT9).
reejecutar_desde_copia "${BASH_SOURCE[0]}" "$@"

cargar_env
tomar_lock "$(estado_dir)/.lock" "hay un deploy o un restore en curso. Probá de nuevo en unos minutos."

ACTUAL="$(tag_actual)"
DESTINO="$(tag_anterior)"
[[ -n "$DESTINO" ]] || morir "no hay versión anterior registrada en $(estado_dir)/anterior."
[[ "$DESTINO" =~ ^(sha|local)-[0-9a-f]{40}$ ]] || morir "$(estado_dir)/anterior no tiene un tag válido."
SHA="$(sha_de_tag "$DESTINO")"

log "Volviendo de ${ACTUAL:-<nada>} a $DESTINO..."
git -C "$FACTUM_REPO" checkout --quiet --detach "$SHA"
export FACTUM_TAG="$DESTINO"
if [[ "$DESTINO" == sha-* ]]; then
  compose pull --quiet backend frontend
fi
compose up -d --remove-orphans
if esperar_salud; then
  printf '%s\n' "$DESTINO" >"$(estado_dir)/desplegado"
  [[ -n "$ACTUAL" ]] && printf '%s\n' "$ACTUAL" >"$(estado_dir)/anterior"
  log "OK revertido a ${SHA:0:8}. Para volver a la otra versión, corré este script de nuevo."
  log "Mientras tanto, conviene poner FACTUM_DEPLOY_HABILITADO=false en GitHub si el próximo merge a main traería el mismo problema."
  exit 0
fi
error "la versión ${SHA:0:8} tampoco quedó sana. Revisá 'compose.sh logs backend' (guía, sección 16)."
exit 1
