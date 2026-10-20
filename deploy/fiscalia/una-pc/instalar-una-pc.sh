#!/usr/bin/env bash
# Factum en la fiscalía — PLAN B: todo en esta PC (web + API + MongoDB, solo en http://localhost:3000; Tatana aparte).
# Sin red, sin nombres ni certificados. Se corre DESDE EL PAQUETE descomprimido, con sudo:
#
#   sudo bash una-pc/instalar-una-pc.sh                 instalación nueva (pide DNI y nombre del superadmin)
#   sudo bash una-pc/instalar-una-pc.sh --reparar       vuelve a levantar todo sin tocar configuración ni datos
#   sudo bash una-pc/instalar-una-pc.sh --desinstalar [--borrar-datos]
#
# Otras opciones: --carpeta <ruta> (default /srv/factum-una-pc), --superadmin-dni, --superadmin-nombre, --si,
# --sin-avx (MongoDB 4.4 para PCs cuyo procesador no tiene AVX; si falta y la PC no tiene AVX, se pregunta).
# Puede convivir con el plan A en la misma PC (otra carpeta, otro proyecto de Docker, otros puertos).
set -euo pipefail
exec bash "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/../comun/instalar.sh" --plan una-pc "$@"
