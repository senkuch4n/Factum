#!/bin/sh
# Punto de entrada de la imagen de Tatana en Docker (SDD instalacion-fiscalia §7). Corre como root solo para:
#   1. levantar usbmuxd (servicio de iPhone) como root, supervisado (si se cae, se relanza);
#   2. bajar al uid/gid del usuario del host (TATANA_UID/TATANA_GID, más sus grupos TATANA_GIDS) y ejecutar
#      Factum.Agent con los argumentos que recibió el contenedor. Así la evidencia y los datos quedan a nombre del
#      perito y el agente no corre como root.
#
# Variables (las pone instalar-tatana-docker.sh):
#   TATANA_UID, TATANA_GID   uid/gid del usuario del host (default 1000:1000)
#   TATANA_GIDS              grupos suplementarios del usuario del host, separados por coma (opcional)
#   TATANA_USBMUXD           1 = levantar usbmuxd (default), 0 = no (modo --mock o sin iPhone)
#   HOME                     carpeta de la casa del agente adentro del contenedor (pareos de pymobiledevice3)
set -eu

UID_AGENTE="${TATANA_UID:-1000}"
GID_AGENTE="${TATANA_GID:-1000}"
GRUPOS="${TATANA_GIDS:-}"
USBMUXD="${TATANA_USBMUXD:-1}"

case "$UID_AGENTE" in ''|*[!0-9]*) echo "entrypoint: TATANA_UID inválido: '$UID_AGENTE'" >&2; exit 2 ;; esac
case "$GID_AGENTE" in ''|*[!0-9]*) echo "entrypoint: TATANA_GID inválido: '$GID_AGENTE'" >&2; exit 2 ;; esac
if [ "$UID_AGENTE" = 0 ]; then
  echo "entrypoint: el agente no corre como root (TATANA_UID=0). Instalá Tatana con el usuario del perito." >&2
  exit 2
fi
case "$GRUPOS" in *[!0-9,]*) echo "entrypoint: TATANA_GIDS inválido: '$GRUPOS'" >&2; exit 2 ;; esac
# Sin el grupo 0 (root) entre los suplementarios.
GRUPOS="$(printf '%s' "$GRUPOS" | tr ',' '\n' | grep -vx '0' | grep -v '^$' | paste -sd, - || true)"

# Las autopruebas no necesitan usbmuxd.
case "${1:-}" in --autoprueba-linux|--autoprueba-ios) USBMUXD=0 ;; esac

if [ "$USBMUXD" = 1 ]; then
  mkdir -p /var/lib/lockdown
  chmod 755 /var/lib/lockdown
  rm -f /run/usbmuxd /run/usbmuxd.pid
  (
    while :; do
      # Sin -U: queda como root (en el contenedor no está la regla udev que le da los USB al usuario usbmux).
      usbmuxd -f >/dev/null 2>&1 || true
      echo "entrypoint: usbmuxd terminó; se relanza en 2 s" >&2
      sleep 2
      rm -f /run/usbmuxd /run/usbmuxd.pid
    done
  ) &
  i=0
  while [ ! -S /run/usbmuxd ] && [ "$i" -lt 50 ]; do sleep 0.1; i=$((i + 1)); done
  if [ -S /run/usbmuxd ]; then
    echo "entrypoint: usbmuxd listo (/var/run/usbmuxd)"
  else
    echo "entrypoint: AVISO: usbmuxd no abrió su socket todavía; el iPhone puede no aparecer" >&2
  fi
fi

if [ -z "${HOME:-}" ] || [ "$HOME" = / ] || [ "$HOME" = /root ]; then
  HOME=/tmp/tatana-home
  export HOME
fi
if [ ! -d "$HOME" ]; then
  mkdir -p "$HOME"
  chown "$UID_AGENTE:$GID_AGENTE" "$HOME"
fi

if [ -n "$GRUPOS" ]; then
  set -- --groups "$GRUPOS" -- /opt/tatana/Factum.Agent "$@"
else
  set -- --clear-groups -- /opt/tatana/Factum.Agent "$@"
fi
exec setpriv --reuid="$UID_AGENTE" --regid="$GID_AGENTE" --no-new-privs --inh-caps=-all "$@"
