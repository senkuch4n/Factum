#!/usr/bin/env bash
# Factum en la nube — verifica que cada DOCX guardado coincida con su report_hash (SDD despliegue-nube
# §6.5). SOLO LECTURA: no escribe nada en Mongo ni en disco. Corre como deploy:
#   verificar-informes.sh
#
# Para cada caso con ReportHash en la base factum (usuario factum_app), calcula el SHA-256 de
# $FACTUM_DATOS/backend/cases/<id>/<PdfFilename> y reporta OK / DISTINTO / FALTA.
# Termina con código 1 si hay alguno DISTINTO o FALTA. Lo usan restaurar.sh, el simulacro de
# restore (guía, sección 13) y la prueba del reinicio.
set -euo pipefail
# shellcheck source=deploy/cloud/scripts/_comun.sh
source "$(dirname "${BASH_SOURCE[0]}")/_comun.sh"

cargar_env
servicio_corriendo mongo || morir "Mongo no está corriendo (compose.sh up -d mongo)."

# La contraseña de factum_app no pasa por la línea de comandos: el contenedor de mongo ya la tiene
# en FACTUM_MONGO_APP_PASSWORD. En Mongo los campos van en PascalCase (PdfFilename, ReportHash).
# shellcheck disable=SC2016 # el JS se evalúa dentro del contenedor
LISTA="$(compose exec -T mongo mongosh --quiet --nodb --eval '
  const db = connect("mongodb://factum_app:" + encodeURIComponent(process.env.FACTUM_MONGO_APP_PASSWORD) +
    "@127.0.0.1:27017/factum?authSource=factum");
  db.cases.find({ ReportHash: { $type: "string", $ne: "" } }, { _id: 1, PdfFilename: 1, ReportHash: 1 })
    .forEach(c => print([String(c._id), c.PdfFilename || "", c.ReportHash].join("\t")));
')"

DIR_CASOS="$FACTUM_DATOS/backend/cases"
total=0 ok=0 distintos=0 n_faltan=0
while IFS=$'\t' read -r id archivo esperado; do
  [[ -n "$id" ]] || continue
  total=$((total + 1))
  # Defensa: ni el id ni el nombre pueden salir de cases/<id>/.
  if [[ -z "$archivo" || "$id" == *[/\\]* || "$archivo" == *[/\\]* || "$id" == .* || "$archivo" == .* ]]; then
    echo "FALTA     $id  (nombre de archivo inválido o vacío)"
    n_faltan=$((n_faltan + 1))
    continue
  fi
  ruta="$DIR_CASOS/$id/$archivo"
  if [[ ! -f "$ruta" ]]; then
    echo "FALTA     $id  $archivo"
    n_faltan=$((n_faltan + 1))
    continue
  fi
  real="$(sha256sum "$ruta" | cut -d' ' -f1)"
  if [[ "$real" == "${esperado,,}" ]]; then
    echo "OK        $id  $archivo"
    ok=$((ok + 1))
  else
    echo "DISTINTO  $id  $archivo"
    distintos=$((distintos + 1))
  fi
done <<<"$LISTA"

echo "Informes: $total en total, $ok OK, $distintos distintos, $n_faltan faltan."
((distintos == 0 && n_faltan == 0))
