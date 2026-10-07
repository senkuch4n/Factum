#!/usr/bin/env bash
# Factum en la nube — restaura un backup cifrado de backup.sh (SDD despliegue-nube §6.5.3). Corre como deploy.
#
#   restaurar.sh --archivo <local.tar.age> --identidad <clave-privada-age> [opciones]
#   restaurar.sh --remoto <diarios/factum-...tar.age> --identidad <clave-privada-age> [opciones]
#
# Opciones:
#   --conservar-identidad  no borrar el archivo de la clave privada al terminar (por defecto se
#                          borra con shred: la clave privada no tiene que quedar en el servidor)
#   --reemplazar           permitir restaurar sobre una base factum que YA tiene datos (pide
#                          escribir el dominio para confirmar). Sin esto, se niega.
#
# Pensado para un host NUEVO y VACÍO (guía, sección 13): ahí se corre después de las secciones 2-6,
# sin hacer el primer arranque. Si todavía no hay nada desplegado, usa la versión que anotó el backup
# (version.txt). Nunca apuntarlo a la Mongo de desarrollo.
#
# Pasos: descifrar -> verificar SHA256SUMS -> guarda de base vacía -> parar backend y frontend ->
# mongorestore --drop (solo factum.*) -> extraer los DOCX -> up -d -> salud -> verificar-informes.sh.
set -euo pipefail
# shellcheck source=deploy/cloud/scripts/_comun.sh
source "$(dirname "${BASH_SOURCE[0]}")/_comun.sh"

# En un host nuevo hace git checkout de la versión del backup: corre desde una copia (DT9).
reejecutar_desde_copia "${BASH_SOURCE[0]}" "$@"

uso() {
  echo "Uso: $0 (--archivo <f.tar.age> | --remoto <ruta en el bucket>) --identidad <clave age> [--conservar-identidad] [--reemplazar]" >&2
  exit 2
}

ARCHIVO=""
REMOTO=""
IDENTIDAD=""
CONSERVAR=0
REEMPLAZAR=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --archivo) ARCHIVO="${2:-}"; shift 2 ;;
    --remoto) REMOTO="${2:-}"; shift 2 ;;
    --identidad) IDENTIDAD="${2:-}"; shift 2 ;;
    --conservar-identidad) CONSERVAR=1; shift ;;
    --reemplazar) REEMPLAZAR=1; shift ;;
    *) uso ;;
  esac
done
[[ -n "$IDENTIDAD" ]] || uso
[[ -n "$ARCHIVO" && -z "$REMOTO" || -z "$ARCHIVO" && -n "$REMOTO" ]] || uso
[[ -f "$IDENTIDAD" ]] || morir "no existe la clave $IDENTIDAD."

TMP=""
al_salir() {
  [[ -n "$TMP" ]] && rm -rf "$TMP"
  [[ -n "${FACTUM_COPIA_DIR:-}" ]] && rm -rf "$FACTUM_COPIA_DIR"
  if ((!CONSERVAR)) && [[ -f "$IDENTIDAD" ]]; then
    shred -u "$IDENTIDAD" 2>/dev/null || rm -f "$IDENTIDAD"
    echo "Se borró la clave privada $IDENTIDAD de este servidor."
  fi
}
trap al_salir EXIT

cargar_env
for herramienta in age tar sha256sum; do
  command -v "$herramienta" >/dev/null 2>&1 || morir "falta '$herramienta' en este servidor."
done
tomar_lock "$(estado_dir)/.lock" "hay un deploy en curso. Esperá a que termine."

umask 077
mkdir -p "$FACTUM_HOME/backups/.tmp"
TMP="$(mktemp -d "$FACTUM_HOME/backups/.tmp/restaurar.XXXXXX")"

if [[ -n "$REMOTO" ]]; then
  command -v rclone >/dev/null 2>&1 || morir "falta rclone."
  [[ "$REMOTO" != *..* ]] || morir "--remoto no puede tener '..'."
  log "Bajando $REMOTO del bucket..."
  rclone --config "$FACTUM_HOME/config/rclone.conf" copyto "${FACTUM_BACKUP_DESTINO%/}/$REMOTO" "$TMP/backup.tar.age"
  ARCHIVO="$TMP/backup.tar.age"
fi
[[ -f "$ARCHIVO" ]] || morir "no existe $ARCHIVO."

log "Descifrando y verificando SHA256SUMS..."
mkdir "$TMP/contenido"
age -d -i "$IDENTIDAD" "$ARCHIVO" | tar -xf - -C "$TMP/contenido"
(cd "$TMP/contenido" && sha256sum -c --quiet SHA256SUMS) || morir "el backup está dañado: SHA256SUMS no coincide."
log "OK el backup está íntegro."

# Versión con la que se levanta: la desplegada o, en un host nuevo, la que anotó el backup.
TAG="$(tag_actual)"
if [[ -z "$TAG" ]]; then
  TAG="$(tr -d '[:space:]' <"$TMP/contenido/version.txt")"
  [[ "$TAG" =~ ^(sha|local)-[0-9a-f]{40}$ ]] || morir "no hay nada desplegado y el backup no dice qué versión usar. Hacé el primer arranque (guía, sección 7) y volvé a correr con --reemplazar."
  [[ "$TAG" == sha-* ]] || morir "el backup se hizo con una versión armada en el servidor ($TAG): hacé el primer arranque (sección 7) y volvé a correr con --reemplazar."
  log "Host sin versión desplegada: se usa la del backup, ${TAG}."
  git -C "$FACTUM_REPO" fetch --quiet origin main || morir "no se pudo actualizar el repo desde GitHub (deploy key, sección 5.1)."
  git -C "$FACTUM_REPO" checkout --quiet --detach "$(sha_de_tag "$TAG")"
  export FACTUM_TAG="$TAG"
  compose pull --quiet backend frontend
fi
export FACTUM_TAG="$TAG"

log "Levantando Mongo..."
compose up -d --wait mongo

# Guarda: no se pisa una base con datos sin pedirlo explícitamente.
# En un datadir nuevo, el entrypoint de mongo corre primero un mongod temporal (que ya responde al
# healthcheck) para crear los usuarios y después lo reinicia: se reintenta hasta 60 s.
contar_documentos() {
  # shellcheck disable=SC2016 # el JS se evalúa dentro del contenedor
  compose exec -T mongo mongosh --quiet --nodb --eval '
    const db = connect("mongodb://root:" + encodeURIComponent(process.env.MONGO_INITDB_ROOT_PASSWORD) +
      "@127.0.0.1:27017/admin").getSiblingDB("factum");
    print(db.getCollectionNames().reduce((n, c) => n + db.getCollection(c).countDocuments({}), 0));
  ' 2>/dev/null | tr -d '[:space:]'
}
DOCUMENTOS=""
LIMITE=$((SECONDS + 60))
until DOCUMENTOS="$(contar_documentos)" && [[ "$DOCUMENTOS" =~ ^[0-9]+$ ]]; do
  ((SECONDS < LIMITE)) || break
  sleep 3
done
[[ "$DOCUMENTOS" =~ ^[0-9]+$ ]] || morir "no se pudo contar los documentos de la base factum."
if ((DOCUMENTOS > 0)); then
  ((REEMPLAZAR)) || morir "la base factum ya tiene $DOCUMENTOS documentos. Si de verdad querés pisarla, volvé a correr con --reemplazar."
  echo "ATENCIÓN: se van a REEMPLAZAR los $DOCUMENTOS documentos de la base factum de $FACTUM_DOMINIO."
  read -r -p "Escribí el dominio ($FACTUM_DOMINIO) para confirmar: " confirmacion
  [[ "$confirmacion" == "$FACTUM_DOMINIO" ]] || morir "no coincide: no se restauró nada."
fi

log "Parando backend y frontend..."
compose stop backend frontend 2>/dev/null || true

log "mongorestore (solo factum.*, con --drop)..."
# shellcheck disable=SC2016 # las $ se expanden dentro del contenedor
compose exec -T mongo sh -c '
  set -e
  umask 077
  cfg="$(mktemp)"
  trap "rm -f \"$cfg\"" EXIT
  printf "password: \"%s\"\n" "$MONGO_INITDB_ROOT_PASSWORD" > "$cfg"
  mongorestore --quiet --config "$cfg" --username root --authenticationDatabase admin \
    --drop --archive --gzip --nsInclude "factum.*"
' <"$TMP/contenido/mongo.archive.gz"

log "Extrayendo los DOCX en $FACTUM_DATOS/backend..."
mkdir -p "$FACTUM_DATOS/backend"
# Con la imagen del backend (corre como root): los archivos que ya hubiera pueden ser de root.
# Se extrae ENCIMA: no se borra nada que ya estuviera.
docker run --rm -i --network none -v "$FACTUM_DATOS/backend:/data" --entrypoint tar \
  "$FACTUM_REGISTRO/factum-backend:$TAG" -xzf - -C /data <"$TMP/contenido/docx.tar.gz"

log "Levantando todo..."
compose up -d --remove-orphans
esperar_salud || morir "Factum no quedó sano después de restaurar (compose.sh logs backend)."
if [[ -z "$(tag_actual)" ]]; then
  mkdir -p "$(estado_dir)"
  printf '%s\n' "$TAG" >"$(estado_dir)/desplegado"
fi

log "Verificando los informes contra report_hash..."
if "$DIR_SCRIPTS/verificar-informes.sh"; then
  log "OK restauración completa."
else
  morir "la restauración terminó pero hay informes que no coinciden (ver arriba)."
fi
