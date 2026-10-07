#!/usr/bin/env bash
# Factum en la nube — backup cifrado de Mongo + DOCX (SDD despliegue-nube §6.5.2, DT13). Corre como deploy.
#
#   backup.sh                               diario (cron): local + copia en el bucket (rclone)
#   backup.sh --solo-local --motivo <texto> solo local (lo usa desplegar.sh antes de cada deploy)
#
# Arma un tar con: mongo.archive.gz (mongodump de la base factum), docx.tar.gz (datos/backend sin
# temporales), version.txt y SHA256SUMS; lo cifra con age a FACTUM_BACKUP_AGE_RECIPIENT (la clave
# PRIVADA no está en este servidor) y lo deja en $FACTUM_HOME/backups/.
# Retención: locales, los FACTUM_BACKUP_LOCALES más nuevos (y aparte los 3 últimos pre-deploy);
# remotos, 8 días en diarios/ y 29 días en semanales/ (copia de los domingos).
#
# Cron (lo instala la guía, sección 12, con "crontab -e" del usuario deploy):
#   30 3 * * * /srv/factum/repo/deploy/cloud/scripts/backup.sh >> /srv/factum/backups/backup.log 2>&1
set -euo pipefail
# shellcheck source=deploy/cloud/scripts/_comun.sh
source "$(dirname "${BASH_SOURCE[0]}")/_comun.sh"

SOLO_LOCAL=0
MOTIVO=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --solo-local) SOLO_LOCAL=1; shift ;;
    --motivo) MOTIVO="${2:-}"; shift 2 ;;
    *) echo "Uso: $0 [--solo-local] [--motivo <texto>]" >&2; exit 2 ;;
  esac
done
if [[ -n "$MOTIVO" && ! "$MOTIVO" =~ ^[a-z0-9][a-z0-9-]{0,40}$ ]]; then
  echo "ERROR: --motivo admite minúsculas, dígitos y guiones (hasta 41)." >&2
  exit 2
fi

cargar_env
DIR_BACKUPS="$FACTUM_HOME/backups"
RCLONE_CONF="$FACTUM_HOME/config/rclone.conf"
PING_URL="${FACTUM_BACKUP_PING_URL:-}"
mkdir -p "$DIR_BACKUPS"

# El log del cron no crece sin límite: pasado 1 MB se vacía (el cron escribe en modo append).
LOG_CRON="$DIR_BACKUPS/backup.log"
if [[ -f "$LOG_CRON" ]] && (($(wc -c <"$LOG_CRON") > 1048576)); then : >"$LOG_CRON"; fi

ping_hc() { # ping_hc [/fail] [mensaje]
  [[ -n "$PING_URL" ]] || return 0
  curl -fsS -m 10 --retry 3 -o /dev/null --data-raw "${2:-}" "${PING_URL%/}${1:-}" || true
}

for herramienta in age tar sha256sum; do
  command -v "$herramienta" >/dev/null 2>&1 || morir "falta '$herramienta' en este servidor (preparar-servidor.sh lo instala)."
done
if ((!SOLO_LOCAL)); then
  command -v rclone >/dev/null 2>&1 || morir "falta rclone (preparar-servidor.sh lo instala)."
  [[ -n "${FACTUM_BACKUP_DESTINO:-}" && "$FACTUM_BACKUP_DESTINO" != *CAMBIAR* ]] ||
    morir "FACTUM_BACKUP_DESTINO no está configurado en factum.env (guía, sección 12)."
  [[ -f "$RCLONE_CONF" ]] || morir "falta $RCLONE_CONF (guía, sección 12)."
fi

tomar_lock "$DIR_BACKUPS/.lock" "ya hay un backup en curso."

TMP=""
al_salir() {
  local codigo=$?
  [[ -n "$TMP" ]] && rm -rf "$TMP"
  rm -f "$DIR_BACKUPS"/*.parcial
  if ((codigo != 0)); then
    error "el backup falló (código $codigo)."
    ping_hc /fail "backup.sh falló en $(hostname) (código $codigo)"
  fi
}
trap al_salir EXIT

USO="$(uso_disco)"
if ((USO >= 85)); then
  error "el disco de $FACTUM_DATOS está al $USO %."
  ping_hc /fail "Disco al $USO % en $(hostname): liberá espacio (guía, sección 16)."
fi

umask 077
SELLO="$(date '+%Y%m%d-%H%M')"
NOMBRE="factum-$SELLO${MOTIVO:+-$MOTIVO}.tar.age"
mkdir -p "$DIR_BACKUPS/.tmp"
TMP="$(mktemp -d "$DIR_BACKUPS/.tmp/backup.XXXXXX")"

log "mongodump de la base factum..."
# La contraseña de root no pasa por la línea de comandos (se vería en "ps"): el contenedor ya la
# tiene en MONGO_INITDB_ROOT_PASSWORD y se la da a mongodump con un --config temporal.
# shellcheck disable=SC2016 # las $ se expanden dentro del contenedor, no acá
compose exec -T mongo sh -c '
  set -e
  umask 077
  cfg="$(mktemp)"
  trap "rm -f \"$cfg\"" EXIT
  printf "password: \"%s\"\n" "$MONGO_INITDB_ROOT_PASSWORD" > "$cfg"
  mongodump --quiet --config "$cfg" --username root --authenticationDatabase admin \
    --db factum --archive --gzip
' >"$TMP/mongo.archive.gz"
[[ -s "$TMP/mongo.archive.gz" ]] || morir "mongodump no produjo datos."

log "DOCX de $FACTUM_DATOS/backend..."
tar -C "$FACTUM_DATOS/backend" --exclude=./.upload-tmp --exclude=./.generate-tmp -czf "$TMP/docx.tar.gz" .

tag_actual >"$TMP/version.txt"
(cd "$TMP" && sha256sum mongo.archive.gz docx.tar.gz version.txt >SHA256SUMS)

log "Cifrando con age..."
tar -C "$TMP" -cf - mongo.archive.gz docx.tar.gz version.txt SHA256SUMS |
  age -r "$FACTUM_BACKUP_AGE_RECIPIENT" >"$DIR_BACKUPS/$NOMBRE.parcial"
mv "$DIR_BACKUPS/$NOMBRE.parcial" "$DIR_BACKUPS/$NOMBRE"
rm -rf "$TMP"
TMP=""
log "Backup local: $DIR_BACKUPS/$NOMBRE ($(du -h "$DIR_BACKUPS/$NOMBRE" | cut -f1))"

# Retención local: los N más nuevos de cada tipo (los nombres empiezan con la fecha, así que
# ordenar por nombre es ordenar por fecha). Los pre-deploy cuentan aparte: quedan los 3 últimos.
LOCALES="${FACTUM_BACKUP_LOCALES:-3}"
[[ "$LOCALES" =~ ^[0-9]+$ ]] || LOCALES=3
find "$DIR_BACKUPS" -maxdepth 1 -type f -name 'factum-*.tar.age' -printf '%f\n' | { grep -v -- '-pre-deploy-' || true; } |
  sort -r | tail -n +"$((LOCALES + 1))" | while IFS= read -r viejo; do rm -f -- "${DIR_BACKUPS:?}/$viejo"; done
find "$DIR_BACKUPS" -maxdepth 1 -type f -name 'factum-*-pre-deploy-*.tar.age' -printf '%f\n' |
  sort -r | tail -n +4 | while IFS= read -r viejo; do rm -f -- "${DIR_BACKUPS:?}/$viejo"; done

if ((SOLO_LOCAL)); then
  log "OK backup local (sin copia remota)."
  exit 0
fi

DESTINO="${FACTUM_BACKUP_DESTINO%/}"
log "Subiendo a $DESTINO/diarios/..."
rclone --config "$RCLONE_CONF" copy "$DIR_BACKUPS/$NOMBRE" "$DESTINO/diarios/"
if [[ "$(date +%u)" == 7 ]]; then
  log "Domingo: copia semanal en $DESTINO/semanales/..."
  rclone --config "$RCLONE_CONF" copy "$DIR_BACKUPS/$NOMBRE" "$DESTINO/semanales/"
fi
log "Retención remota (diarios > 8 días, semanales > 29 días)..."
rclone --config "$RCLONE_CONF" delete --min-age 8d "$DESTINO/diarios/"
# semanales/ puede no existir todavía (antes del primer domingo): eso no es una falla del backup.
rclone --config "$RCLONE_CONF" delete --min-age 29d "$DESTINO/semanales/" 2>/dev/null ||
  log "AVISO: no se pudo aplicar la retención de semanales/ (¿todavía no hay ninguna copia semanal?)."

ping_hc "" "OK $NOMBRE"
log "OK backup $NOMBRE"
