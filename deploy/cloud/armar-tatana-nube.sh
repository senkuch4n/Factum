#!/usr/bin/env bash
# Factum en la nube — arma el Tatana portátil "para la nube" en la Mac (SDD despliegue-nube §6.8, DT16).
#
# RETIRADO PARA LA NUBE (SDD tatana-instalador-autoupdate P1, B23): desde la 1.4.0, Tatana para la nube
# sale como INSTALADOR con actualización automática firmada, por .github/workflows/tatana-release.yml
# (tag tatana-vX.Y.Z o "Run workflow"; guía docs/despliegue-nube.md, sección 19). Este script queda solo
# como respaldo manual: un portátil armado acá NO se actualiza solo y el instalador lo reemplaza.
#
#   deploy/cloud/armar-tatana-nube.sh --version X.Y.Z --origenes "https://<dominio>[,https://<otro>]" \
#       [--ref origin/main] [--salida <carpeta>]
#
# Hornea Agent:AllowedOrigins = localhost + esos orígenes, y CLIENT_URL = el PRIMER origen (la web
# que se abre al iniciar sesión). Parte de un árbol limpio del --ref (git archive): no viaja ningún
# cambio local (ni un "Mock": true, ni un appsettings.Local.json).
# Requisitos: los de deploy/windows/armar-tatana-portable.sh (dotnet 10, python3, curl, unzip, zip,
# internet; descarga unos 300 MB).
set -euo pipefail

VERSION=""
ORIGENES=""
REF=""
SALIDA=""

uso() {
  echo "Uso: $0 --version X.Y.Z --origenes \"https://<dominio>[,https://<otro>]\" [--ref origin/main] [--salida <carpeta>]" >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="${2:-}"; shift 2 ;;
    --origenes) ORIGENES="${2:-}"; shift 2 ;;
    --ref) REF="${2:-}"; shift 2 ;;
    --salida) SALIDA="${2:-}"; shift 2 ;;
    -h | --help) uso ;;
    *) echo "Argumento desconocido: $1" >&2; uso ;;
  esac
done
[[ -n "$VERSION" && -n "$ORIGENES" ]] || uso

echo "AVISO: armar-tatana-nube.sh está retirado para la nube. Tatana se publica como instalador con" >&2
echo "       .github/workflows/tatana-release.yml (guía, sección 19). Esto arma un portátil de respaldo" >&2
echo "       que no se actualiza solo." >&2

DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$DIR_SCRIPT/../.." && pwd)"
ARMAR="$REPO/deploy/windows/armar-tatana-portable.sh"
SALIDA="${SALIDA:-$REPO/deploy/cloud/dist}"

PRIMERO="${ORIGENES%%,*}"
PRIMERO="$(printf '%s' "$PRIMERO" | tr -d '[:space:]' | tr '[:upper:]' '[:lower:]')"
PRIMERO="${PRIMERO%/}"
[[ "$PRIMERO" == https://* ]] || { echo "ERROR: el primer origen tiene que ser https:// (es la web en la nube): '$PRIMERO'." >&2; exit 2; }

if [[ -z "$REF" ]]; then
  echo "Actualizando origin/main..."
  git -C "$REPO" fetch --quiet origin main
  REF=origin/main
fi
COMMIT="$(git -C "$REPO" rev-parse --verify "$REF^{commit}")" || { echo "ERROR: '$REF' no es un commit de este repo." >&2; exit 2; }

TMP="$(mktemp -d "${TMPDIR:-/tmp}/tatana-nube.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT
mkdir -p "$TMP/src" "$TMP/salida"
echo "Árbol limpio de $REF (${COMMIT:0:8})..."
git -C "$REPO" archive "$COMMIT" | tar -x -C "$TMP/src"

# El armado (y el hornear-origenes-tatana.py) se toman de ESTA copia del repo, no del --ref.
"$ARMAR" --version "$VERSION" --src "$TMP/src" --salida "$TMP/salida" \
  --origenes "$ORIGENES" --client-url "$PRIMERO"

mkdir -p "$SALIDA"
SALIDA="$(cd "$SALIDA" && pwd)"
ZIP="$SALIDA/Tatana-Portable-v$VERSION-Windows-nube.zip"
mv -f "$TMP/salida/Tatana-Portable-v$VERSION-Windows.zip" "$ZIP"
(cd "$SALIDA" && shasum -a 256 "$(basename "$ZIP")" >"$(basename "$ZIP").sha256")

cat <<FIN

OK  $ZIP
    SHA-256: $(cut -d' ' -f1 <"$ZIP.sha256")

Para publicarlo en https://<dominio>/descargas/Tatana-Portable-Windows-nube.zip (guía, sección 10):

  scp "$ZIP" admin@<IP>:/tmp/Tatana-Portable-Windows-nube.zip
  ssh admin@<IP> 'sudo install -o deploy -g deploy -m 644 /tmp/Tatana-Portable-Windows-nube.zip /srv/factum/descargas/ && rm /tmp/Tatana-Portable-Windows-nube.zip'

FIN
