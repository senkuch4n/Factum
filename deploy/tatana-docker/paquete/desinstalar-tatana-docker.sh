#!/usr/bin/env bash
# Desinstala Tatana en Docker: detiene y borra el contenedor (y el "-anterior" de un cambio, si quedó), quita el
# actualizador automático (timer, units, /opt|/etc|/var/lib|/var/log/factum-tatana-docker) y, si se pide, las imágenes.
# NUNCA borra la evidencia (~/Factum/Evidencia) ni la carpeta de trabajo del agente (~/.local/share/tatana-docker): eso
# lo decide el perito a mano. Si la instalación había enmascarado el usbmuxd de esta PC, ofrece devolverlo.
#
#   ./desinstalar-tatana-docker.sh [--borrar-imagen] [--si]
set -euo pipefail
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/comun.sh"

BORRAR_IMAGEN=0
SI_A_TODO=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --borrar-imagen) BORRAR_IMAGEN=1; shift ;;
    --si) SI_A_TODO=1; shift ;;
    -h|--help) sed -n '2,6p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) morir "Opción desconocida: $1" ;;
  esac
done
export SI_A_TODO

echo "${_N}Tatana en Docker — desinstalación${_0}"
no_root
detectar_docker
leer_instalacion
tomar_candado || morir "El actualizador automático está trabajando en este momento: esperá unos minutos y volvé a correr el script."
# Imágenes que cargó Factum (para --borrar-imagen): las de imagenes.txt del actualizador, si existe.
IMAGENES_FACTUM="$(cat "$TD_VAR/imagenes.txt" 2>/dev/null || true)"

paso "Actualizador automático"
if [[ -e "$TD_SYSTEMD/$UNIDAD_TIMER" || -e "$TD_SYSTEMD/$UNIDAD_SERVICIO" || -d "$TD_OPT" || -d "$TD_ETC" || -d "$TD_VAR" || -d "$TD_LOG" ]]; then
  echo "  Se quita el actualizador (pide la contraseña de administrador)."
  if command -v systemctl >/dev/null 2>&1; then
    sudo systemctl disable --now "$UNIDAD_TIMER" >/dev/null 2>&1 || true
    sudo systemctl stop "$UNIDAD_SERVICIO" >/dev/null 2>&1 || true
  fi
  sudo rm -f -- "$TD_SYSTEMD/$UNIDAD_TIMER" "$TD_SYSTEMD/$UNIDAD_SERVICIO"
  command -v systemctl >/dev/null 2>&1 && { sudo systemctl daemon-reload 2>/dev/null || true; }
  sudo rm -rf -- "${TD_OPT:?}" "${TD_ETC:?}" "${TD_VAR:?}" "${TD_LOG:?}"
  ok "actualizador quitado ($UNIDAD_TIMER, $TD_OPT, $TD_ETC, $TD_VAR, $TD_LOG)"
else
  ok "no estaba instalado"
fi

paso "Contenedor"
if contenedor_existe; then
  ETIQUETA="$("${DOCKER[@]}" container inspect -f '{{index .Config.Labels "factum.tatana-docker"}}' "$CONTENEDOR" 2>/dev/null || true)"
  [[ "$ETIQUETA" == 1 ]] || morir "El contenedor '$CONTENEDOR' no es de Tatana en Docker: no se toca."
  [[ -n "$IMAGEN" ]] || IMAGEN="$("${DOCKER[@]}" container inspect -f '{{.Config.Image}}' "$CONTENEDOR")"
  "${DOCKER[@]}" rm -f "$CONTENEDOR" >/dev/null
  ok "contenedor $CONTENEDOR borrado"
else
  ok "no había contenedor $CONTENEDOR"
fi
if "${DOCKER[@]}" container inspect "$CONTENEDOR_ANTERIOR" >/dev/null 2>&1; then
  if [[ "$("${DOCKER[@]}" container inspect -f '{{index .Config.Labels "factum.tatana-docker"}}' "$CONTENEDOR_ANTERIOR" 2>/dev/null)" == 1 ]]; then
    "${DOCKER[@]}" rm -f "$CONTENEDOR_ANTERIOR" >/dev/null
    ok "contenedor $CONTENEDOR_ANTERIOR (de un cambio interrumpido) borrado"
  fi
fi

paso "Imagen"
if [[ "$BORRAR_IMAGEN" == 1 && ( -n "$IMAGEN" || -n "$IMAGENES_FACTUM" ) ]]; then
  # La de la instalación y las que registró el actualizador (nunca otras).
  printf '%s\n%s\n' "$IMAGEN" "$IMAGENES_FACTUM" | grep -v '^$' | sort -u | while IFS= read -r img; do
    "${DOCKER[@]}" image inspect "$img" >/dev/null 2>&1 || continue
    if "${DOCKER[@]}" image rm "$img" >/dev/null 2>&1; then ok "imagen $img borrada"
    else aviso "no se pudo borrar la imagen $img (¿la usa otro contenedor?)"; fi
  done
else
  echo "  Se deja la imagen ${IMAGEN:-(desconocida)} (para borrarla: --borrar-imagen)."
fi

if [[ -f "$MARCA_USBMUXD" ]]; then
  paso "usbmuxd de esta PC"
  echo "  La instalación había detenido y enmascarado el usbmuxd de esta PC (Tatana nativo lo necesita)."
  if confirmar "¿Volver a habilitarlo?"; then
    sudo systemctl unmask usbmuxd.service
    rm -f "$MARCA_USBMUXD"
    ok "usbmuxd desenmascarado (arranca solo al enchufar un iPhone)"
  else
    echo "  Queda enmascarado. Para devolverlo después: sudo systemctl unmask usbmuxd.service"
  fi
fi

echo
echo "${_N}Quedan en el disco (no se borran solos):${_0}"
echo "  Evidencia:                    $EVIDENCIA"
echo "  Datos y configuración:        $DATOS"
[[ ! -f "$REGLA_UDEV" ]] || echo "  Regla USB de Android:         $REGLA_UDEV (la usa también Tatana nativo)"
