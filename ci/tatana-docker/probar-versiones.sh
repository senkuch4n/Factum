#!/usr/bin/env bash
# Tabla de versiones (SDD fiscalia-operacion §10.8, ci/tatana-docker/versiones.txt) contra las TRES implementaciones:
# version_cmp de deploy/tatana-docker/paquete/comun.sh (PC), version_cmp de deploy/fiscalia/comun/_comun.sh (servidor)
# y verificar-manifiesto-docker.py --comparar (Python). Sin privilegios ni red.
#
#   bash ci/tatana-docker/probar-versiones.sh [--python "<comando>"]
#
# --python: cómo correr Python (default python3; p. ej. el de la imagen de Tatana:
#   --python "docker run --rm --network none -v $PWD:/r:ro --entrypoint /opt/tatana/tools/python-embed/bin/python3 <imagen>"
#   en ese caso el script se pasa como /r/deploy/... ). Si no hay Python, se prueban solo los dos bash.
set -euo pipefail
RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TABLA="$RAIZ/ci/tatana-docker/versiones.txt"
PY="python3"
PY_RAIZ="$RAIZ"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --python) PY="$2"; PY_RAIZ="/r"; shift 2 ;;
    *) echo "uso: $0 [--python \"<comando>\"]" >&2; exit 2 ;;
  esac
done

# Cada implementación en su propio bash (las dos se llaman version_cmp).
cmp_pc() { bash -c '. "$1/deploy/tatana-docker/paquete/comun.sh" >/dev/null; version_cmp "$2" "$3"' _ "$RAIZ" "$1" "$2"; }
cmp_servidor() { bash -c '. "$1/deploy/fiscalia/comun/_comun.sh"; version_cmp "$2" "$3"' _ "$RAIZ" "$1" "$2"; }
HAY_PY=1
# shellcheck disable=SC2086 # PY puede ser un comando con argumentos
$PY -I "$PY_RAIZ/deploy/tatana-docker/paquete/verificar-manifiesto-docker.py" --comparar 1.0.0 1.0.0 >/dev/null 2>&1 || {
  echo "AVISO: no hay Python para verificar-manifiesto-docker.py: se prueban solo los bash"; HAY_PY=0; }
cmp_py() {
  # shellcheck disable=SC2086
  $PY -I "$PY_RAIZ/deploy/tatana-docker/paquete/verificar-manifiesto-docker.py" --comparar "$1" "$2"
}

fallos=0
n=0
while read -r a b esperado; do
  [[ -z "$a" || "$a" == \#* ]] && continue
  for impl in pc servidor $( ((HAY_PY)) && echo py ); do
    for par in "$a $b $esperado" "$b $a $((-esperado))"; do
      read -r x y e <<<"$par"
      r="$(cmp_"$impl" "$x" "$y")"
      n=$((n + 1))
      if [[ "$r" == "$e" ]]; then echo "ok    $impl: $x vs $y = $r"
      else echo "FALLA $impl: $x vs $y = $r (esperado $e)"; fallos=$((fallos + 1)); fi
    done
  done
done <"$TABLA"
echo
if ((fallos)); then echo "$fallos de $n comparaciones fallaron"; exit 1; fi
echo "Las $n comparaciones dieron lo esperado"
