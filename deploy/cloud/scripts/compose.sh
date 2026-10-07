#!/usr/bin/env bash
# Factum en la nube — docker compose con el factum.env y el tag desplegado (SDD despliegue-nube §6.5).
# Para operar a mano en el VPS, como el usuario deploy:
#   compose.sh ps
#   compose.sh logs --tail 200 backend
#   compose.sh up -d backend        (por ejemplo, después de vaciar los superadmins del factum.env)
# No cambia de versión: para eso está desplegar.sh.
set -euo pipefail
# shellcheck source=deploy/cloud/scripts/_comun.sh
source "$(dirname "${BASH_SOURCE[0]}")/_comun.sh"

cargar_env
compose "$@"
