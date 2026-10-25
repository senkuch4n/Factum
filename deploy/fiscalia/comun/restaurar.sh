#!/usr/bin/env bash
# Factum en la fiscalía — restaura un backup de backup.sh sobre una instalación que YA está hecha
# (plan A o B; sirve también para pasar los datos del plan A al plan B o al revés). Con sudo:
#
#   sudo /srv/factum-una-pc/scripts/restaurar.sh --desde <carpeta factum-...> [--carpeta <instalación>]
#                                                [--sin-config] [--sin-backup-previo] [--si]
#
# Pasos: verificar SHA256SUMS -> confirmar (REEMPLAZA la base) -> backup del estado actual -> parar web/API -> mongorestore --drop
# (solo factum.*) -> evidencia encima de datos/backend (no borra nada que ya esté) -> appsettings.Local.json
# y branding/ (salvo --sin-config) -> levantar y esperar.
# NO se restaura config/.env: cada instalación conserva sus propios secretos (las sesiones abiertas se
# cierran). La copia del .env del backup queda en config/.env.del-backup por si hace falta un valor.
# Plan A: la CA de Caddy del backup NO se restaura sola (ver la guía, "Restaurar en un servidor nuevo").
set -euo pipefail
DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deploy/fiscalia/comun/_comun.sh
source "$DIR_SCRIPT/_comun.sh"

CARPETA=""
DESDE=""
SIN_CONFIG=0
SIN_BACKUP_PREVIO=0
FACTUM_SI=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --carpeta) CARPETA="${2:-}"; shift 2 ;;
    --desde) DESDE="${2:-}"; shift 2 ;;
    --sin-config) SIN_CONFIG=1; shift ;;
    --sin-backup-previo) SIN_BACKUP_PREVIO=1; shift ;;
    --si) FACTUM_SI=1; shift ;;
    -h | --help) sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Argumento desconocido: $1" >&2; exit 2 ;;
  esac
done
export FACTUM_SI
[[ -n "$DESDE" && -d "$DESDE" ]] || morir "indicá la carpeta del backup con --desde <carpeta factum-...>."
DESDE="$(cd "$DESDE" && pwd)"

requiere_docker
buscar_instalacion "$CARPETA" "$DIR_SCRIPT"
# Una operación a la vez (SDD fiscalia-operacion §8.7); el backup previo de abajo hereda este candado.
tomar_candado "$FACTUM_HOME"
DATOS="$(env_leer "$FACTUM_ENV" FACTUM_DATOS)"
echo "=== Restaurar Factum: $DESDE → $FACTUM_HOME ($FACTUM_PLAN) ==="

echo "  Verificando el backup..."
for f in mongo.archive.gz evidencia.tar.gz config.tar.gz version.txt SHA256SUMS; do
  [[ -f "$DESDE/$f" ]] || morir "al backup le falta $f: no es una carpeta de backup.sh o está incompleta."
done
while IFS= read -r linea; do
  [[ -n "$linea" ]] || continue
  h="${linea%%  *}"
  f="${linea#*  }"
  [[ "$(sha256_de "$DESDE/$f")" == "$h" ]] || morir "el backup está dañado: $f no coincide con SHA256SUMS."
done <"$DESDE/SHA256SUMS"
V_BACKUP="$(tr -d '[:space:]' <"$DESDE/version.txt")"
P_BACKUP="$(tr -d '[:space:]' <"$DESDE/plan.txt" 2>/dev/null || echo '?')"
ok "Backup íntegro (versión $V_BACKUP, plan $P_BACKUP)."
[[ "$V_BACKUP" == "$FACTUM_VERSION_INSTALADA" ]] || aviso "El backup es de la versión $V_BACKUP y la instalada es $FACTUM_VERSION_INSTALADA."

[[ "$(estado_servicio mongo)" == healthy ]] || { fc up -d mongo >/dev/null; esperar_mongo=1; }
if [[ "${esperar_mongo:-0}" == 1 ]]; then
  limite=$((SECONDS + 120))
  until [[ "$(estado_servicio mongo)" == healthy ]] || ((SECONDS > limite)); do sleep 3; done
  [[ "$(estado_servicio mongo)" == healthy ]] || morir "la base de datos no arranca (diagnostico.sh)."
fi

echo ""
echo "  ${_R}ATENCIÓN:${_0} se va a REEMPLAZAR la base de datos de $FACTUM_HOME por la del backup."
echo "  Todo lo que se cargó en esta instalación después de ese backup se pierde."
if [[ "${FACTUM_SI:-0}" != 1 ]]; then
  read -r -p "  Escribí RESTAURAR para confirmar: " conf
  [[ "$conf" == RESTAURAR ]] || morir "no se restauró nada."
fi

if ((SIN_BACKUP_PREVIO)); then
  aviso "Sin backup del estado actual (--sin-backup-previo)."
else
  echo "  Backup del estado actual antes de pisarlo (en $FACTUM_HOME/backups)..."
  bash "$DIR_SCRIPT/backup.sh" --carpeta "$FACTUM_HOME" || morir "no se pudo hacer el backup previo: no se restauró nada." "Si no hay lugar en disco, corré con --sin-backup-previo (se pierde el estado actual)."
fi

echo "  Parando la web y la API..."
[[ "$FACTUM_PLAN" == servidor ]] && fc stop caddy >/dev/null 2>&1
fc stop frontend backend >/dev/null 2>&1 || true

echo "  mongorestore (solo factum.*, con --drop)..."
# shellcheck disable=SC2016 # las $ se expanden dentro del contenedor
fc exec -T mongo sh -c '
  set -e
  umask 077
  cfg="$(mktemp)"
  trap "rm -f \"$cfg\"" EXIT
  printf "password: \"%s\"\n" "$MONGO_INITDB_ROOT_PASSWORD" > "$cfg"
  mongorestore --quiet --config "$cfg" --username root --authenticationDatabase admin \
    --drop --archive --gzip --nsInclude "factum.*"
' <"$DESDE/mongo.archive.gz"
ok "Base restaurada."

echo "  Evidencia..."
mkdir -p "$DATOS/backend"
tar -C "$DATOS/backend" -xzf "$DESDE/evidencia.tar.gz"
ok "Evidencia extraída en $DATOS/backend (encima de lo que había; no se borró nada)."

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
tar -C "$TMP" -xzf "$DESDE/config.tar.gz"
umask 077
[[ -f "$TMP/config/.env" ]] && cp "$TMP/config/.env" "$FACTUM_HOME/config/.env.del-backup" && chmod 600 "$FACTUM_HOME/config/.env.del-backup"
if ((SIN_CONFIG)); then
  ok "Configuración sin cambios (--sin-config)."
else
  [[ -f "$TMP/config/appsettings.Local.json" ]] && cp "$TMP/config/appsettings.Local.json" "$FACTUM_HOME/config/appsettings.Local.json"
  chmod 644 "$FACTUM_HOME/config/appsettings.Local.json"
  if [[ -d "$TMP/config/branding" ]]; then
    cp -R "$TMP/config/branding/." "$FACTUM_HOME/config/branding/"
    chmod -R a+rX "$FACTUM_HOME/config/branding"
  fi
  ok "appsettings.Local.json y branding/ restaurados (config/.env queda el de esta instalación)."
fi

echo "  Levantando Factum..."
fc up -d --remove-orphans >/dev/null
esperar_sano 300 || { mostrar_estado; mostrar_log_backend; morir "Factum no quedó sano después de restaurar."; }
ok "Restauración completa. Los peritos tienen que volver a iniciar sesión."
