#!/usr/bin/env bash
# Factum en la fiscalía — diagnóstico (plan A o B). No muestra secretos. Con sudo:
#
#   sudo /srv/factum-fiscalia/scripts/diagnostico.sh [--carpeta <instalación>] [--lineas N]
#
# Muestra: versión y plan, Docker, estado de cada contenedor, salud por HTTP, nombre factum.internal y
# certificado (plan A), espacio en disco, últimos backups y las últimas líneas del registro de cada servicio.
# Para mandarlo por mensaje: sudo .../diagnostico.sh > diagnostico.txt 2>&1
set -uo pipefail
DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deploy/fiscalia/comun/_comun.sh
source "$DIR_SCRIPT/_comun.sh"

CARPETA=""
LINEAS=30
while [[ $# -gt 0 ]]; do
  case "$1" in
    --carpeta) CARPETA="${2:-}"; shift 2 ;;
    --lineas) LINEAS="${2:-30}"; shift 2 ;;
    -h | --help) sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Argumento desconocido: $1" >&2; exit 2 ;;
  esac
done

echo "=== Diagnóstico de Factum — $(date '+%Y-%m-%d %H:%M:%S') — $(hostname 2>/dev/null) ==="
requiere_docker
buscar_instalacion "$CARPETA" "$DIR_SCRIPT"
DATOS="$(env_leer "$FACTUM_ENV" FACTUM_DATOS)"
PROBLEMAS=0

echo ""
echo "── Instalación"
info "Plan:        $FACTUM_PLAN"
info "Carpeta:     $FACTUM_HOME"
info "Versión:     $FACTUM_VERSION_INSTALADA"
info "Proyecto:    $FACTUM_PROYECTO"
if modo_sin_avx; then info "Base:        MongoDB 4.4 (modo sin AVX)"; else info "Base:        MongoDB 7"; fi
if es_linux && ! cpu_tiene_avx && ! modo_sin_avx; then
  falla "El procesador no tiene AVX y la instalación usa MongoDB 7: corré el instalador con --sin-avx (guía, \"PC sin AVX\")."
  PROBLEMAS=$((PROBLEMAS + 1))
fi
# shellcheck disable=SC1091 # /etc/os-release existe en la PC Linux, no donde corre shellcheck
[[ -f /etc/os-release ]] && info "Sistema:     $(. /etc/os-release && echo "${PRETTY_NAME:-?}") ($(uname -m))"
info "Docker:      $(docker version --format '{{.Server.Version}}' 2>/dev/null) · compose $(docker compose version --short 2>/dev/null)"
perm_env="$(stat -c '%a' "$FACTUM_ENV" 2>/dev/null || stat -f '%Lp' "$FACTUM_ENV" 2>/dev/null)"
[[ "$perm_env" == 600 ]] || { aviso "config/.env tiene permisos $perm_env (deberían ser 600): sudo chmod 600 $FACTUM_ENV"; PROBLEMAS=$((PROBLEMAS + 1)); }
[[ -n "$(env_leer "$FACTUM_ENV" FACTUM_SUPERADMIN_0_TEMPORAL)" ]] && aviso "config/.env todavía tiene la contraseña temporal del superadmin (la instalación no terminó: correla con --reparar)."

echo ""
echo "── Contenedores"
for s in $(servicios_del_plan); do
  st="$(estado_servicio "$s")"
  case "$st" in healthy | running) ok "$s: $st" ;; *) falla "$s: $st"; PROBLEMAS=$((PROBLEMAS + 1)) ;; esac
done

echo ""
echo "── Salud por HTTP"
if [[ "$FACTUM_PLAN" == servidor ]]; then
  P_HTTPS="$(env_leer "$FACTUM_ENV" FACTUM_PUERTO_HTTPS)"
  P_HTTPS="${P_HTTPS:-443}"
  CA="$FACTUM_HOME/$FACTUM_CA_ARCHIVO"
  [[ -f "$CA" ]] || CA="$(ruta_ca_caddy)"
  if [[ -f "$CA" ]]; then
    command -v openssl >/dev/null 2>&1 && info "CA: $(openssl x509 -in "$CA" -noout -subject -enddate 2>/dev/null | tr '\n' ' ')"
    if curl -fsS -m 10 --resolve "$FACTUM_NOMBRE_RED:$P_HTTPS:127.0.0.1" --cacert "$CA" "https://$FACTUM_NOMBRE_RED:$P_HTTPS/health" >/dev/null 2>&1; then
      ok "https://$FACTUM_NOMBRE_RED/health (con la CA de Factum)"
    else
      falla "https://$FACTUM_NOMBRE_RED/health no responde"; PROBLEMAS=$((PROBLEMAS + 1))
    fi
    if curl -fsS -m 10 --resolve "$FACTUM_NOMBRE_RED:$P_HTTPS:127.0.0.1" --cacert "$CA" "https://$FACTUM_NOMBRE_RED:$P_HTTPS/health/ready" >/dev/null 2>&1; then
      ok "/health/ready (la API llega a la base)"
    else
      falla "/health/ready falla: la API no llega a la base"; PROBLEMAS=$((PROBLEMAS + 1))
    fi
  else
    falla "No encuentro el certificado raíz de Caddy."; PROBLEMAS=$((PROBLEMAS + 1))
  fi
  if grep -qE "[[:space:]]$FACTUM_NOMBRE_RED([[:space:]]|\$)" /etc/hosts 2>/dev/null; then
    ok "/etc/hosts: $(grep -E "[[:space:]]$FACTUM_NOMBRE_RED([[:space:]]|\$)" /etc/hosts | head -1)"
  else
    aviso "/etc/hosts de esta PC no tiene factum.internal (solo afecta al navegador de esta PC)."
  fi
  echo "  IP de esta PC (la que va en cada perito):"
  ips_de_esta_pc | sed 's/^/      /'
  if command -v ufw >/dev/null 2>&1; then info "ufw: $(ufw status 2>/dev/null | head -1)"; fi
  if command -v firewall-cmd >/dev/null 2>&1; then info "firewalld: $(firewall-cmd --state 2>/dev/null)"; fi
else
  P_API="$(env_leer "$FACTUM_ENV" FACTUM_PUERTO_API)"
  P_WEB="$(env_leer "$FACTUM_ENV" FACTUM_PUERTO_WEB)"
  if curl -fsS -m 10 "http://127.0.0.1:${P_API:-8080}/health/ready" >/dev/null 2>&1; then ok "API http://localhost:${P_API:-8080}/health/ready"; else falla "la API no responde en localhost:${P_API:-8080}"; PROBLEMAS=$((PROBLEMAS + 1)); fi
  if curl -fsS -m 10 -o /dev/null "http://127.0.0.1:${P_WEB:-3000}/"; then ok "Web http://localhost:${P_WEB:-3000}"; else falla "la web no responde en localhost:${P_WEB:-3000}"; PROBLEMAS=$((PROBLEMAS + 1)); fi
fi
if curl -fsS -m 3 http://127.0.0.1:8765/health >/dev/null 2>&1; then ok "Tatana responde en esta PC (localhost:8765)."; else info "Tatana no está corriendo en esta PC (normal en un servidor sin celulares)."; fi

echo ""
echo "── Disco"
df -h "$DATOS" 2>/dev/null | sed 's/^/      /'
USO="$(uso_disco_pct "$DATOS")"
[[ -n "$USO" ]] && ((USO >= 85)) && { aviso "El disco está al $USO %: liberá espacio o mové backups viejos."; PROBLEMAS=$((PROBLEMAS + 1)); }
info "Base:       $(du -sh "$DATOS/mongo" 2>/dev/null | cut -f1)"
info "Evidencia:  $(du -sh "$DATOS/backend" 2>/dev/null | cut -f1)"
info "Backups:    $(du -sh "$FACTUM_HOME/backups" 2>/dev/null | cut -f1)"
ULTIMO="$(find "$FACTUM_HOME/backups" -mindepth 1 -maxdepth 1 -type d -name 'factum-*' 2>/dev/null | sort | tail -1)"
info "Último backup en la carpeta: ${ULTIMO:+$(basename "$ULTIMO")}${ULTIMO:-ninguno}"

echo ""
echo "── Registro (últimas $LINEAS líneas de cada servicio)"
for s in $(servicios_del_plan); do
  echo "  [$s]"
  fc logs --no-color --tail "$LINEAS" "$s" 2>&1 | sed 's/^/    /'
done

echo ""
if ((PROBLEMAS == 0)); then ok "Sin problemas detectados."; else falla "$PROBLEMAS problema(s): ver arriba y la guía, \"Problemas frecuentes\"."; fi
exit $((PROBLEMAS > 0))
