#!/usr/bin/env bash
# Factum en la fiscalía — backup local de los dos planes (SDD instalacion-fiscalia, D7). Con sudo:
#
#   sudo /srv/factum-fiscalia/scripts/backup.sh [--destino <carpeta o pendrive>] [--sin-pausa] [--carpeta <instalación>]
#
# Deja una carpeta factum-<plan>-AAAAMMDD-HHMMSS/ con:
#   mongo.archive.gz   mongodump de la base factum
#   evidencia.tar.gz   datos/backend (ZIP, DOCX, fotos), sin temporales de subida
#   config.tar.gz      config/ (.env con los secretos, appsettings.Local.json, branding/)
#   caddy-data.tar.gz  (plan A) la CA interna: sin ella, hay que reinstalar el certificado en cada perito
#   version.txt, plan.txt, SHA256SUMS
# Destino por defecto: <instalación>/backups (ahí se conservan los FACTUM_BACKUP_CONSERVAR más nuevos;
# en un --destino externo no se borra nada). Sin cifrar: guardá el pendrive bajo llave (tiene config/.env).
# Por defecto pausa la web y la API unos minutos para que base y evidencia queden consistentes.
set -euo pipefail
DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deploy/fiscalia/comun/_comun.sh
source "$DIR_SCRIPT/_comun.sh"

CARPETA=""
DESTINO=""
PAUSA=1
while [[ $# -gt 0 ]]; do
  case "$1" in
    --carpeta) CARPETA="${2:-}"; shift 2 ;;
    --destino) DESTINO="${2:-}"; shift 2 ;;
    --sin-pausa) PAUSA=0; shift ;;
    -h | --help) sed -n '2,16p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Argumento desconocido: $1" >&2; exit 2 ;;
  esac
done

requiere_docker
buscar_instalacion "$CARPETA" "$DIR_SCRIPT"
# Una operación a la vez (SDD fiscalia-operacion §8.7): si lo llama actualizar.sh o restaurar.sh, hereda su candado.
tomar_candado "$FACTUM_HOME"
DATOS="$(env_leer "$FACTUM_ENV" FACTUM_DATOS)"
EXTERNO=1
[[ -n "$DESTINO" ]] || { DESTINO="$FACTUM_HOME/backups"; EXTERNO=0; }
mkdir -p "$DESTINO" || morir "no pude crear $DESTINO."
DESTINO="$(cd "$DESTINO" && pwd)"

echo "=== Backup de Factum ($FACTUM_PLAN, $FACTUM_HOME) → $DESTINO ==="
[[ "$(estado_servicio mongo)" == healthy ]] || morir "la base de datos no está funcionando." "Levantá Factum (instalador con --reparar) y volvé a intentar."

TAM_EVID="$(du -sk "$DATOS/backend" 2>/dev/null | cut -f1)"
LIBRE_K="$(df -Pk "$DESTINO" | awk 'NR==2 {print $4}')"
if [[ -n "$TAM_EVID" && -n "$LIBRE_K" ]] && ((LIBRE_K < TAM_EVID + 1048576)); then
  morir "no hay lugar en $DESTINO: hacen falta unos $(((TAM_EVID + 1048576) / 1048576)) GB y quedan $((LIBRE_K / 1048576)) GB."
fi

umask 077
SELLO="$(date '+%Y%m%d-%H%M%S')"
NOMBRE="factum-$FACTUM_PLAN-$SELLO"
PARCIAL="$DESTINO/.$NOMBRE.parcial"
REANUDAR=0
al_salir() {
  local c=$?
  ((REANUDAR)) && { fc start backend frontend >/dev/null 2>&1 || true; [[ "$FACTUM_PLAN" == servidor ]] && fc start caddy >/dev/null 2>&1; }
  [[ -d "$PARCIAL" ]] && rm -rf "$PARCIAL"
  ((c == 0)) || falla "El backup NO se completó (código $c)."
}
trap al_salir EXIT
mkdir -p "$PARCIAL"

if ((PAUSA)); then
  echo "  Pausando la web y la API (los peritos no pueden trabajar durante el backup)..."
  REANUDAR=1
  [[ "$FACTUM_PLAN" == servidor ]] && fc stop caddy >/dev/null 2>&1
  fc stop frontend backend >/dev/null 2>&1
fi

echo "  mongodump de la base factum..."
# La contraseña de root no pasa por la línea de comandos: el contenedor ya la tiene en su entorno.
# shellcheck disable=SC2016 # las $ se expanden dentro del contenedor
fc exec -T mongo sh -c '
  set -e
  umask 077
  cfg="$(mktemp)"
  trap "rm -f \"$cfg\"" EXIT
  printf "password: \"%s\"\n" "$MONGO_INITDB_ROOT_PASSWORD" > "$cfg"
  mongodump --quiet --config "$cfg" --username root --authenticationDatabase admin --db factum --archive --gzip
' >"$PARCIAL/mongo.archive.gz"
[[ -s "$PARCIAL/mongo.archive.gz" ]] || morir "mongodump no produjo datos."
ok "Base de datos ($(du -h "$PARCIAL/mongo.archive.gz" | cut -f1))."

echo "  Evidencia (datos/backend)..."
tar -C "$DATOS/backend" --exclude=./.upload-tmp --exclude=./.generate-tmp -czf "$PARCIAL/evidencia.tar.gz" .
ok "Evidencia ($(du -h "$PARCIAL/evidencia.tar.gz" | cut -f1))."

if ((REANUDAR)); then
  fc start backend frontend >/dev/null 2>&1 || true
  [[ "$FACTUM_PLAN" == servidor ]] && fc start caddy >/dev/null 2>&1
  REANUDAR=0
  if esperar_sano 180; then echo "  Web y API reanudadas."; else aviso "La web o la API tardan en volver (diagnostico.sh)."; fi
fi

tar -C "$FACTUM_HOME" -czf "$PARCIAL/config.tar.gz" config
ok "Configuración (config/)."
if [[ "$FACTUM_PLAN" == servidor && -d "$DATOS/caddy-data" ]]; then
  tar -C "$DATOS" -czf "$PARCIAL/caddy-data.tar.gz" caddy-data
  ok "Certificados de Caddy (caddy-data/)."
fi
printf '%s\n' "$FACTUM_VERSION_INSTALADA" >"$PARCIAL/version.txt"
printf '%s\n' "$FACTUM_PLAN" >"$PARCIAL/plan.txt"
(
  cd "$PARCIAL"
  sumas=""
  for f in *; do sumas="$sumas$(sha256_de "$f")  $f"$'\n'; done
  printf '%s' "$sumas" >SHA256SUMS
)
mv "$PARCIAL" "$DESTINO/$NOMBRE"
sync 2>/dev/null || true
ok "Backup: $DESTINO/$NOMBRE ($(du -sh "$DESTINO/$NOMBRE" | cut -f1))"

if ((!EXTERNO)); then
  CONSERVAR="$(env_leer "$FACTUM_ENV" FACTUM_BACKUP_CONSERVAR)"
  [[ "$CONSERVAR" =~ ^[0-9]+$ && "$CONSERVAR" -gt 0 ]] || CONSERVAR=10
  # Los nombres llevan la fecha: ordenar por nombre es ordenar por fecha.
  find "$DESTINO" -mindepth 1 -maxdepth 1 -type d -name "factum-$FACTUM_PLAN-*" | sort -r | tail -n +"$((CONSERVAR + 1))" |
    while IFS= read -r viejo; do rm -rf -- "$viejo"; echo "  Borrado el backup viejo $(basename "$viejo")"; done
fi
echo "  Para restaurarlo: sudo $FACTUM_HOME/scripts/restaurar.sh --desde $DESTINO/$NOMBRE"
