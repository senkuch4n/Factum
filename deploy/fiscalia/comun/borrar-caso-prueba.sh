#!/usr/bin/env bash
# Factum en la fiscalía — borra el caso de la PRUEBA DE HUMO por su _id (nunca por un filtro amplio).
#
#   sudo <carpeta>/scripts/borrar-caso-prueba.sh --listar            casos cuyo número de causa empieza con PRUEBA
#   sudo <carpeta>/scripts/borrar-caso-prueba.sh <_id del caso>      lo borra (pide confirmación)
#
# Borra: el documento de "cases" con ese _id, sus "case_events" y "case_timestamps" (CaseId = ese _id) y la
# carpeta datos/backend/cases/<_id>. Se niega si el número de causa del caso no empieza con "PRUEBA"
# (para no borrar un caso real por error). Opciones: --carpeta <instalación>, --si.
set -euo pipefail
DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deploy/fiscalia/comun/_comun.sh
source "$DIR_SCRIPT/_comun.sh"

CARPETA=""
ID=""
LISTAR=0
FACTUM_SI=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --carpeta) CARPETA="${2:-}"; shift 2 ;;
    --listar) LISTAR=1; shift ;;
    --si) FACTUM_SI=1; shift ;;
    -h | --help) sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    -*) echo "Argumento desconocido: $1" >&2; exit 2 ;;
    *) ID="$1"; shift ;;
  esac
done
export FACTUM_SI
((LISTAR)) || [[ "$ID" =~ ^[0-9a-fA-F-]{8,64}$ ]] || morir "indicá el _id del caso (o --listar para verlos)."

requiere_docker
buscar_instalacion "$CARPETA" "$DIR_SCRIPT"
DATOS="$(env_leer "$FACTUM_ENV" FACTUM_DATOS)"

mongo_js() { # mongo_js <js fijo de este script>: JS como root sobre la base factum. La clave sale del entorno del
  # contenedor y el _id va por variable de entorno (nunca pegado en el JS).
  local pre
  # Sirve para mongosh (Mongo 7, process.env) y para el shell viejo "mongo" del modo sin AVX (4.4, _getEnv).
  # shellcheck disable=SC2016 # el JS se evalúa dentro del contenedor
  pre='const E = (k) => (typeof process !== "undefined" ? process.env[k] : _getEnv(k));
      const db = connect("mongodb://root:" + encodeURIComponent(E("MONGO_INITDB_ROOT_PASSWORD")) +
      "@127.0.0.1:27017/admin").getSiblingDB("factum"); const ID = E("FACTUM_ID");'
  # El shell viejo imprime al conectar ("connecting to", "Implicit session", versión): se descarta.
  fc exec -T -e FACTUM_ID="$ID" mongo "$(mongo_shell)" --quiet --nodb --eval "$pre $1" |
    sed -e '/^connecting to: /d' -e '/^Implicit session: /d' -e '/^MongoDB server version: /d'
}

if ((LISTAR)); then
  mongo_js 'db.cases.find({ NroReferencia: /^PRUEBA/i }, { NroReferencia: 1 }).forEach(c => print(c._id + "   " + c.NroReferencia));'
  exit 0
fi

NRO="$(mongo_js 'const c = db.cases.findOne({ _id: ID }); print(c ? c.NroReferencia : "<<no-existe>>");' | tail -1)"
[[ "$NRO" != "<<no-existe>>" ]] || morir "no hay un caso con _id $ID."
case "$NRO" in PRUEBA* | prueba* | Prueba*) ;; *) morir "el caso $ID tiene número de causa '$NRO', que no empieza con PRUEBA: no lo borro." ;; esac
echo "  Caso $ID — número de causa: $NRO"
confirmar "¿Borrar este caso de prueba, sus eventos, sus sellos y su carpeta de evidencia?" || morir "no se borró nada."
mongo_js 'print("cases: " + db.cases.deleteOne({ _id: ID }).deletedCount +
  " · case_events: " + db.case_events.deleteMany({ CaseId: ID }).deletedCount +
  " · case_timestamps: " + db.case_timestamps.deleteMany({ CaseId: ID }).deletedCount);'
if [[ -d "$DATOS/backend/cases/$ID" ]]; then
  rm -rf -- "${DATOS:?}/backend/cases/${ID:?}"
  ok "Carpeta $DATOS/backend/cases/$ID borrada."
fi
ok "Caso de prueba borrado."
