#!/usr/bin/env bash
# Aserción determinística del montaje de discos USB para la extracción (extraccion-logica-android §11.1):
# armar_run (deploy/tatana-docker/paquete/comun.sh), en modo NO mock, agrega Agent__Extraction__AllowedRoots
# (evidencia + /media + /run/media) y, cuando /media o /run/media existen en el host, los monta con rslave.
# No necesita Docker: solo evalúa el array RUN que arma la función. No se puede enchufar un USB en CI, así que
# la prueba con disco real queda manual (§12.4).
set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
# Rutas de prueba (no pisa una instalación real).
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
export TATANA_DOCKER_CONTENEDOR="tatana-docker-test"
export TATANA_DOCKER_DATOS="$TMP/datos"
export TATANA_DOCKER_EVIDENCIA="$TMP/evidencia"

# shellcheck source=deploy/tatana-docker/paquete/comun.sh
. "$RAIZ/deploy/tatana-docker/paquete/comun.sh"

# Variables que arma el instalador antes de llamar a armar_run.
T_UID=1000; T_GID=1000; T_GIDS=""; PUERTO=8765; MOCK=0; ZONA=""; RED_CON_PUERTOS=0

fallos=0
afirmar() { # afirmar <descripción> <patrón esperado en RUN_STR>
  if [[ "$RUN_STR" == *"$2"* ]]; then echo "  OK  $1"; else echo "  FALLA  $1 (falta: $2)"; fallos=$((fallos + 1)); fi
}
negar() { # negar <descripción> <patrón que NO debe estar>
  if [[ "$RUN_STR" != *"$2"* ]]; then echo "  OK  $1"; else echo "  FALLA  $1 (sobra: $2)"; fallos=$((fallos + 1)); fi
}

echo "[1/2] armar_run en modo NO mock..."
armar_run "factum-tatana-docker:test"
RUN_STR="${RUN[*]}"
afirmar "AllowedRoots incluye la evidencia" "Agent__Extraction__AllowedRoots__0=$EVIDENCIA"
afirmar "AllowedRoots incluye /media" "Agent__Extraction__AllowedRoots__1=/media"
afirmar "AllowedRoots incluye /run/media" "Agent__Extraction__AllowedRoots__2=/run/media"
for raiz in /media /run/media; do
  if [[ -d "$raiz" ]]; then
    afirmar "monta $raiz con rslave (existe en el host)" "-v $raiz:$raiz:rslave"
  else
    echo "  NOTA  $raiz no existe en este host: no se monta (correcto)"
  fi
done

echo "[2/2] armar_run en modo mock (no monta ni limita raíces)..."
MOCK=1
armar_run "factum-tatana-docker:test"
RUN_STR="${RUN[*]}"
negar "mock no agrega AllowedRoots" "Agent__Extraction__AllowedRoots__1=/media"
negar "mock no monta /media" "-v /media:/media:rslave"

if [[ "$fallos" -gt 0 ]]; then
  echo "FALLÓ: $fallos aserción(es)." >&2
  exit 1
fi
echo "OK: montaje de /media y AllowedRoots correctos."
