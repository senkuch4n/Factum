#!/usr/bin/env bash
# Muestra en castellano el estado de Tatana en Docker: contenedor, imagen, /health, USB, actualizaciones automáticas y
# últimas líneas del registro. No cambia nada (salvo con --volver-a-anterior o --buscar-ahora, que piden sudo).
#
#   ./estado-tatana-docker.sh [--registro N]     (N = líneas del registro a mostrar, default 20)
#   ./estado-tatana-docker.sh --actualizaciones  solo el bloque "Actualizaciones"
#   ./estado-tatana-docker.sh --buscar-ahora     consulta ya al servidor de la fiscalía (sudo)
#   ./estado-tatana-docker.sh --volver-a-anterior  vuelve a la versión anterior de Tatana (sudo; solo con Tatana libre)
set -euo pipefail
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/comun.sh"

LINEAS=20
SOLO_ACTUALIZACIONES=0
ACTUALIZADOR="$TD_OPT/actual/actualizador-tatana-docker.sh"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --registro) LINEAS="${2:-20}"; shift 2 ;;
    --actualizaciones) SOLO_ACTUALIZACIONES=1; shift ;;
    --volver-a-anterior|--buscar-ahora)
      [[ -x "$ACTUALIZADOR" ]] || morir "Esta PC no tiene el actualizador: ver docs/tatana-docker.md §4 (sumar-actualizador)."
      if [[ "$(id -u)" == 0 ]]; then exec "$ACTUALIZADOR" "$1"; else exec sudo "$ACTUALIZADOR" "$1"; fi ;;
    -h|--help) sed -n '2,8p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) morir "Opción desconocida: $1" ;;
  esac
done
[[ "$LINEAS" =~ ^[0-9]+$ ]] || morir "--registro necesita un número."

# Bloque "Actualizaciones" (SDD fiscalia-operacion §7.8): sin sudo (estado.env, actualizador.env y el log son 644).
mostrar_actualizaciones() {
  local env_a est canal hab timer prox r
  paso "Actualizaciones"
  if [[ ! -f "$ACTUALIZADOR_ENV" ]]; then
    aviso "Esta PC no tiene el actualizador: ver docs/tatana-docker.md §4 (sumar-actualizador)."
    return 0
  fi
  env_a="$(cat "$ACTUALIZADOR_ENV" 2>/dev/null || true)"
  est="$(cat "$ESTADO_ENV" 2>/dev/null || true)"
  canal="$(clave_de "$env_a" CANAL_URL)"
  hab="$(clave_de "$env_a" HABILITADO)"
  echo "  Servidor:          ${canal:-?}"
  if [[ "$hab" == 1 ]]; then ok "habilitado"; else aviso "deshabilitado (HABILITADO=0 en $ACTUALIZADOR_ENV)"; fi
  if command -v systemctl >/dev/null 2>&1; then
    timer="$(systemctl is-active "$UNIDAD_TIMER" 2>/dev/null || true)"
    prox="$(systemctl show "$UNIDAD_TIMER" -p NextElapseUSecRealtime --value 2>/dev/null || true)"
    if [[ "$timer" == active ]]; then ok "timer activo; próxima consulta: ${prox:-?}"
    else aviso "el timer no está activo ($timer): sudo systemctl enable --now $UNIDAD_TIMER"; fi
  fi
  echo "  Versión instalada: $(clave_de "$est" VERSION_INSTALADA)  ($(clave_de "$est" IMAGEN_INSTALADA))"
  echo "  Disponible:        $(clave_de "$est" VERSION_DISPONIBLE)"
  echo "  Anterior:          $(clave_de "$est" VERSION_ANTERIOR)  (para volver: ./estado-tatana-docker.sh --volver-a-anterior)"
  r="$(clave_de "$est" RESULTADO)"
  echo "  Última consulta:   $(clave_de "$est" ULTIMA_CONSULTA) — $(texto_resultado "$r" "$(clave_de "$est" VERSION_INSTALADA)" "$(clave_de "$est" DETALLE)")"
  [[ -z "$(clave_de "$est" DETALLE)" ]] || echo "                     $(clave_de "$est" DETALLE)"
  echo "  Última actualización: $(clave_de "$est" ULTIMA_ACTUALIZACION)"
  [[ -z "$(clave_de "$est" VERSIONES_FALLIDAS)" ]] || aviso "versiones que fallaron en esta PC (no se reintentan): $(clave_de "$est" VERSIONES_FALLIDAS)"
  if [[ -r "$TD_LOG/actualizador.log" ]]; then
    echo "  Últimas líneas de $TD_LOG/actualizador.log:"
    tail -n 10 "$TD_LOG/actualizador.log" | sed 's/^/    /'
  fi
}
if ((SOLO_ACTUALIZACIONES)); then
  mostrar_actualizaciones
  exit 0
fi

echo "${_N}Tatana en Docker — estado${_0}"
detectar_docker
leer_instalacion
echo "  Contenedor: $CONTENEDOR    Datos: $DATOS    Evidencia: $EVIDENCIA"

paso "Contenedor"
if ! contenedor_existe; then
  falla "No hay contenedor '$CONTENEDOR': Tatana en Docker no está instalado (correr instalar-tatana-docker.sh)."
  exit 1
fi
FORMATO='{{.State.Status}}|{{.State.Health.Status}}|{{.HostConfig.RestartPolicy.Name}}|{{.RestartCount}}|{{.Config.Image}}|{{.State.StartedAt}}'
IFS='|' read -r ESTADO SALUD_DOCKER REINICIO REINICIOS IMG DESDE \
  <<<"$("${DOCKER[@]}" container inspect -f "$FORMATO" "$CONTENEDOR" 2>/dev/null || echo '|||||')"
case "$ESTADO" in
  running) ok "andando desde $DESDE (chequeo de Docker: ${SALUD_DOCKER:-sin dato})" ;;
  *) falla "el contenedor está '$ESTADO' (no andando). Probá: ${DOCKER[*]} start $CONTENEDOR" ;;
esac
echo "  Imagen: $IMG    Reinicio automático: $REINICIO    Reinicios: $REINICIOS"
[[ "$REINICIOS" =~ ^[0-9]+$ ]] && (( REINICIOS > 3 )) && aviso "se reinició $REINICIOS veces: mirar el registro de abajo."
[[ "$MOCK" == 1 ]] && aviso "instalado en modo SIMULADO (--mock): no ve celulares reales."

paso "Agente (/health en el puerto $PUERTO)"
if [[ "$ESTADO" == running ]] && SALUD="$(health "$PUERTO")" && [[ -n "$SALUD" ]]; then
  ok "responde"
  echo "  $SALUD" | cut -c1-400
else
  falla "no responde en http://localhost:$PUERTO/health"
fi
LISTA_ORIGENES="http://localhost:3000, http://127.0.0.1:3000"
[[ -z "$ORIGENES" || "$ORIGENES" == "http://localhost:3000" ]] || LISTA_ORIGENES+=", ${ORIGENES//,/, }"
echo "  Orígenes permitidos: $LISTA_ORIGENES"

if [[ "$ESTADO" == running && "$MOCK" != 1 ]]; then
  paso "USB"
  if "${DOCKER[@]}" exec "$CONTENEDOR" test -S /run/usbmuxd; then
    ok "usbmuxd del contenedor andando (iPhone)"
  else
    falla "usbmuxd del contenedor no tiene su socket: el iPhone no va a aparecer"
  fi
  if usbmuxd_del_host_activo; then
    aviso "también hay un usbmuxd del HOST andando: choca con el del contenedor (reinstalá y aceptá detenerlo)."
  fi
  N_USB="$("${DOCKER[@]}" exec "$CONTENEDOR" sh -c 'ls /dev/bus/usb/*/* 2>/dev/null | wc -l' || echo 0)"
  echo "  Dispositivos USB visibles en el contenedor: $N_USB"
  if [[ -f "$REGLA_UDEV" ]]; then ok "regla USB de Android: $REGLA_UDEV"; else aviso "falta la regla USB de Android ($REGLA_UDEV)"; fi
  ADB="$("${DOCKER[@]}" exec -u "$(id -u):$(id -g)" -e "HOME=$DATOS/home" "$CONTENEDOR" \
    /opt/tatana/tools/platform-tools/adb devices 2>/dev/null | sed '1d;/^$/d' || true)"
  if [[ -n "$ADB" ]]; then echo "  adb ve:"; echo "$ADB" | sed 's/^/    /'; else echo "  adb no ve ningún Android (o no hay ninguno enchufado)."; fi
fi

mostrar_actualizaciones

paso "Registro (últimas $LINEAS líneas)"
"${DOCKER[@]}" logs --tail "$LINEAS" "$CONTENEDOR" 2>&1 | sed 's/^/  /'
