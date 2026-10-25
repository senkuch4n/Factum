#!/usr/bin/env bash
# Factum en la fiscalía (plan A) — qué versión de Tatana en Docker tiene cada PC de perito (SDD fiscalia-operacion §8.6).
# Lee el registro de Caddy del canal (datos/caddy-logs/tatana.log*, JSON por línea): cada consulta del actualizador de
# una PC trae en la URL su nombre, la versión instalada, la disponible y el resultado de la última vez. No muestra
# secretos ni toca nada. Con sudo:
#
#   sudo /srv/factum-fiscalia/scripts/estado-peritos.sh [--horas 72] [--carpeta <instalación>] [--registro <carpeta>]
#   sudo /srv/factum-fiscalia/scripts/estado-peritos.sh --resumen [--horas 24]     (una línea, para diagnostico.sh)
#
# (!) = la última consulta de esa PC tiene más de 2 horas (¿apagada? ¿sin red? ¿sin el actualizador?).
# Sin jq ni Python en el host: awk y sed.
set -uo pipefail
DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deploy/fiscalia/comun/_comun.sh
source "$DIR_SCRIPT/_comun.sh"

CARPETA=""
HORAS=72
RESUMEN=0
REGISTRO=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --carpeta) CARPETA="${2:-}"; shift 2 ;;
    --horas) HORAS="${2:-}"; shift 2 ;;
    --resumen) RESUMEN=1; shift ;;
    --registro) REGISTRO="${2:-}"; shift 2 ;;
    -h | --help) sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Argumento desconocido: $1" >&2; exit 2 ;;
  esac
done
[[ "$HORAS" =~ ^[0-9]+$ && "$HORAS" -gt 0 ]] || morir "--horas necesita un número de horas."

if [[ -z "$REGISTRO" ]]; then
  buscar_instalacion "$CARPETA" "$DIR_SCRIPT"
  REGISTRO="$(env_leer "$FACTUM_ENV" FACTUM_DATOS)/caddy-logs"
  PUBLICADA="$(tr -d '[:space:]' <"$FACTUM_HOME/canal-tatana/docker/VERSION" 2>/dev/null || true)"
else
  PUBLICADA="${FACTUM_TATANA_PUBLICADA:-}"
fi

texto_resultado() {
  case "$1" in
    al_dia) echo "al día" ;;
    actualizada) echo "actualizada a v$2" ;;
    esperando_libre) echo "esperando a que termine la captura" ;;
    sin_servidor) echo "no se pudo llegar al servidor" ;;
    manifiesto_invalido | clave_desconocida | firma_invalida) echo "manifiesto rechazado ($1)" ;;
    hash_invalido | imagen_invalida) echo "descarga dañada (se descartó)" ;;
    sin_espacio) echo "sin espacio en disco" ;;
    autoprueba_fallida) echo "la versión nueva no pasó la autoprueba" ;;
    no_arranca) echo "la versión nueva no arrancó: volvió a la anterior" ;;
    version_fallida) echo "la versión publicada ya falló en esa PC" ;;
    vuelta_manual) echo "se volvió a la versión anterior a mano" ;;
    recuperado) echo "se recuperó un cambio interrumpido" ;;
    "") echo "primera consulta" ;;
    *) echo "$1" ;;
  esac
}

AHORA="$(date +%s)"
DESDE=$((AHORA - HORAS * 3600))

# Todas las consultas del manifiesto con equipo=, de todos los archivos (los rotados pueden estar comprimidos), como
# "ts|ip|equipo|instalada|disponible|resultado"; después, la última por equipo + IP (PCs clonadas pueden repetir nombre).
leer_registros() {
  local f
  for f in "$REGISTRO"/tatana*.log*; do
    [[ -f "$f" ]] || continue
    case "$f" in *.gz) gzip -dc "$f" 2>/dev/null ;; *) cat "$f" ;; esac
  done
}
FILAS="$(leer_registros | sed 's/\\u0026/\&/g' | awk -v desde="$DESDE" '
  function campo(linea, nombre,    re, s) {
    re = "\"" nombre "\":\"[^\"]*\""
    if (!match(linea, re)) return ""
    s = substr(linea, RSTART, RLENGTH)
    sub("^\"" nombre "\":\"", "", s); sub("\"$", "", s)
    return s
  }
  function param(q, nombre,    n, i, kv, partes) {
    n = split(q, partes, "&")
    for (i = 1; i <= n; i++) { split(partes[i], kv, "="); if (kv[1] == nombre) return substr(partes[i], length(nombre) + 2) }
    return ""
  }
  /tatana-update-docker\.json\?/ && /equipo=/ {
    if (!match($0, /"ts":[0-9.eE+]+/)) next
    ts = substr($0, RSTART + 5, RLENGTH - 5) + 0
    if (ts < desde) next
    uri = campo($0, "uri"); ip = campo($0, "remote_ip")
    q = uri; sub(/^[^?]*\?/, "", q)
    eq = param(q, "equipo"); if (eq == "") next
    gsub(/[^A-Za-z0-9._-]/, "_", eq)
    printf "%d|%s|%s|%s|%s|%s\n", ts, ip, eq, param(q, "instalada"), param(q, "disponible"), param(q, "resultado")
  }' | sort -t'|' -k1,1n | awk -F'|' '{ ultima[$3 "|" $2] = $0 } END { for (k in ultima) print ultima[k] }' | sort -t'|' -k3,3 -k2,2)"

hace() {
  local s=$((AHORA - $1))
  if ((s < 120)); then echo "hace $s s"
  elif ((s < 7200)); then echo "hace $((s / 60)) min"
  elif ((s < 172800)); then echo "hace $((s / 3600)) h"
  else echo "hace $((s / 86400)) días"
  fi
}

VISTAS=0
DESACTUALIZADAS=0
if [[ -n "$FILAS" ]]; then
  while IFS='|' read -r ts ip eq inst disp res; do
    VISTAS=$((VISTAS + 1))
    [[ -n "$PUBLICADA" && "$inst" != "$PUBLICADA" ]] && DESACTUALIZADAS=$((DESACTUALIZADAS + 1))
  done <<<"$FILAS"
fi

if ((RESUMEN)); then
  echo "PUBLICADA=${PUBLICADA:-ninguna} VISTAS=$VISTAS DESACTUALIZADAS=$DESACTUALIZADAS HORAS=$HORAS"
  exit 0
fi

echo "Tatana en Docker publicado: ${PUBLICADA:-(ninguno todavía)}"
if [[ -z "$FILAS" ]]; then
  echo "Ninguna PC consultó el canal en las últimas $HORAS horas (registro: $REGISTRO)."
  echo "En cada PC: ./estado-tatana-docker.sh --actualizaciones (¿tiene el actualizador? docs/tatana-docker.md §4)."
  exit 0
fi
# (ancho en bytes: "ÚLTIMA" lleva una tilde de 2 bytes)
printf '%-16s %-15s %-20s %-11s %-20s %s\n' "EQUIPO" "IP" "INSTALADA" "DISPONIBLE" "ÚLTIMA CONSULTA" "RESULTADO"
while IFS='|' read -r ts ip eq inst disp res; do
  cuando="$(hace "$ts")"
  ((AHORA - ts > 7200)) && cuando="$cuando (!)"
  texto="$(texto_resultado "$res" "$inst")"
  ((AHORA - ts > 7200)) && [[ "$res" != sin_servidor ]] && texto="$texto · sin consultas recientes"
  printf '%-16s %-15s %-20s %-11s %-19s %s\n' "$eq" "$ip" "${inst:--}" "${disp:--}" "$cuando" "$texto"
done <<<"$FILAS"
echo ""
echo "$VISTAS PC(s) en las últimas $HORAS h$([[ -n "$PUBLICADA" ]] && echo "; $DESACTUALIZADAS sin la versión publicada ($PUBLICADA)")."
