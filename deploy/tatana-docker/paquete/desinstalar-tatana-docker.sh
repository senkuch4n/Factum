#!/usr/bin/env bash
# Desinstala Tatana en Docker: detiene y borra el contenedor y, si se pide, la imagen. NUNCA borra la evidencia
# (~/Factum/Evidencia) ni la carpeta de trabajo del agente (~/.local/share/tatana-docker): eso lo decide el perito a
# mano. Si la instalación había enmascarado el usbmuxd de esta PC, ofrece devolverlo.
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

paso "Imagen"
if [[ "$BORRAR_IMAGEN" == 1 && -n "$IMAGEN" ]]; then
  if "${DOCKER[@]}" image rm "$IMAGEN" >/dev/null 2>&1; then ok "imagen $IMAGEN borrada"
  else aviso "no se pudo borrar la imagen $IMAGEN (¿la usa otro contenedor?)"; fi
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
