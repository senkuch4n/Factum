#!/usr/bin/env bash
# Factum en la fiscalía — PLAN A: instala el servidor (web + API + MongoDB + Caddy con HTTPS) en esta PC Linux.
# Se corre DESDE EL PAQUETE descomprimido, con sudo:
#
#   sudo bash servidor/instalar-servidor.sh                 instalación nueva (pide DNI y nombre del superadmin)
#   sudo bash servidor/instalar-servidor.sh --reparar       vuelve a levantar todo sin tocar configuración ni datos
#   sudo bash servidor/instalar-servidor.sh --desinstalar [--borrar-datos]
#
# Otras opciones: --carpeta <ruta> (default /srv/factum-fiscalia), --superadmin-dni, --superadmin-nombre, --si,
# --sin-avx (MongoDB 4.4 para PCs cuyo procesador no tiene AVX; si falta y la PC no tiene AVX, se pregunta).
# Qué hace, paso a paso: docs/instalacion-fiscalia.md (también va en el paquete).
set -euo pipefail
exec bash "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/../comun/instalar.sh" --plan servidor "$@"
