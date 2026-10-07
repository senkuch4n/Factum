#!/usr/bin/env bash
# Factum en la nube — comando forzado de la clave SSH de CI (SDD despliegue-nube DT8).
#
# Va en ~deploy/.ssh/authorized_keys así (una sola línea):
#   command="/srv/factum/bin/deploy-forzado.sh",no-port-forwarding,no-X11-forwarding,no-agent-forwarding,no-pty ssh-ed25519 AAAA... factum-ci
#
# Es una COPIA ESTABLE en /srv/factum/bin/ (la instala preparar-servidor.sh): no la reescribe ningún
# git checkout. Si cambia en el repo, se vuelve a copiar a mano (guía, sección 11).
#
# Solo acepta dos comandos (lo que el cliente SSH pide llega en SSH_ORIGINAL_COMMAND):
#   desplegar sha-<40 hex>  -> desplegar.sh --tag sha-<40 hex>
#   estado                  -> desplegar.sh --estado
# Cualquier otra cosa: "comando no permitido", exit 2.
set -euo pipefail

DESPLEGAR=/srv/factum/repo/deploy/cloud/scripts/desplegar.sh
pedido="${SSH_ORIGINAL_COMMAND:-}"

registrar() { logger -t factum-deploy -- "$*" 2>/dev/null || true; }

if [[ "$pedido" =~ ^desplegar\ (sha-[0-9a-f]{40})$ ]]; then
  registrar "desplegar ${BASH_REMATCH[1]}"
  exec "$DESPLEGAR" --tag "${BASH_REMATCH[1]}"
elif [[ "$pedido" == estado ]]; then
  registrar "estado"
  exec "$DESPLEGAR" --estado
fi

# Se registra recortado y sin caracteres de control (es texto que manda el cliente).
registrar "rechazado: $(printf '%s' "$pedido" | tr -cd '[:print:]' | cut -c1-120)"
echo "ERROR: comando no permitido" >&2
exit 2
