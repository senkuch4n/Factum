#!/usr/bin/env bash
# Arma el paquete de "Tatana en Docker" (SDD instalacion-fiscalia §7):
#
#   deploy/tatana-docker/armar-tatana-docker.sh --version X.Y.Z --salida <dir>
#       [--ref <git ref>] [--agente-tar <Tatana-Agent-vX.Y.Z-linux-x64.tar.gz>] [--imagen <nombre>]
#       [--canal <dir>] [--clave-extra key_id:spkiDerBase64]
#     → <dir>/Tatana-Docker-vX.Y.Z.tar.gz  (carpeta Tatana-Docker-vX.Y.Z/ con la imagen `docker save`, los scripts,
#        LEEME.txt, IMAGEN.txt y SHA256SUMS)
#     → <dir>/kit-tatana-docker-vX.Y.Z.tar.gz  (la misma carpeta SIN la imagen: decenas de KB; SDD fiscalia-operacion §6)
#
# --canal <dir>: además deja en <dir> lo que firma CI y publica el servidor de la fiscalía: imagen-tatana-docker-vX.Y.Z.tar.gz,
#   kit-tatana-docker-vX.Y.Z.tar.gz y VERSION.
# --clave-extra (SOLO ensayo de CI y pruebas): suma esa clave pública a claves-confiables.txt del kit, que si no tiene
#   exactamente las de agent-ui/src/main/updater/trusted-keys.ts (ops/tatana/claves-confiables.mjs).
#
# El agente NO se arma acá: se reusa deploy/linux/armar-tatana-linux.sh (el mismo payload del AppImage: Factum.Agent
# linux-x64 self-contained + tools/ con versiones fijas y SHA-256). Si ya se armó (por ejemplo en el paquete de la
# fiscalía o en el CI), se pasa con --agente-tar y no se descarga nada de nuevo; si no, se llama a ese script con una
# fuente limpia (git archive de --ref, default HEAD: los cambios sin commitear de server/ NO entran).
# Sin --origenes: los orígenes de la web se escriben al instalar (instalar-tatana-docker.sh --origen).
#
# --imagen cambia el nombre de la imagen (default factum-tatana-docker, el oficial: el manifiesto firmado exige ese
# nombre, así que otro solo sirve para pruebas); el tag es siempre la versión.
# Requisitos: docker con buildx (build linux/amd64; en una Mac Apple Silicon usa emulación), git, gzip, node (claves) y lo
# que pida armar-tatana-linux.sh si no se pasa --agente-tar (dotnet 10, python3, curl, unzip, xz e internet).
set -euo pipefail

VERSION=""
SALIDA=""
REF="HEAD"
AGENTE_TAR=""
NOMBRE_IMAGEN="factum-tatana-docker"
CANAL=""
CLAVE_EXTRA=""
DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RAIZ="$(cd "$DIR_SCRIPT/../.." && pwd)"
ARMAR_LINUX="$DIR_SCRIPT/../linux/armar-tatana-linux.sh"
HORNEAR="$DIR_SCRIPT/../windows/hornear-origenes-tatana.py"

uso() {
  sed -n '4,6p' "$0" | sed 's/^# \{0,1\}//' >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="${2:-}"; shift 2 ;;
    --salida) SALIDA="${2:-}"; shift 2 ;;
    --ref) REF="${2:-}"; shift 2 ;;
    --agente-tar) AGENTE_TAR="${2:-}"; shift 2 ;;
    --imagen) NOMBRE_IMAGEN="${2:-}"; shift 2 ;;
    --canal) CANAL="${2:-}"; shift 2 ;;
    --clave-extra) CLAVE_EXTRA="${2:-}"; shift 2 ;;
    -h|--help) uso ;;
    *) echo "Argumento desconocido: $1" >&2; uso ;;
  esac
done
[[ -n "$VERSION" && -n "$SALIDA" ]] || uso
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || { echo "ERROR: --version tiene que ser semver (X.Y.Z), no '$VERSION'." >&2; exit 2; }
[[ "$NOMBRE_IMAGEN" =~ ^[a-z0-9]+([._-][a-z0-9]+)*(/[a-z0-9]+([._-][a-z0-9]+)*)*$ ]] || { echo "ERROR: --imagen inválido: '$NOMBRE_IMAGEN'." >&2; exit 2; }
for herramienta in docker git gzip tar node; do
  command -v "$herramienta" >/dev/null 2>&1 || { echo "ERROR: falta '$herramienta'." >&2; exit 1; }
done
docker buildx version >/dev/null 2>&1 || { echo "ERROR: falta docker buildx." >&2; exit 1; }
[[ -f "$HORNEAR" ]] || { echo "ERROR: falta $HORNEAR." >&2; exit 1; }

IMAGEN="$NOMBRE_IMAGEN:$VERSION"
mkdir -p "$SALIDA"
SALIDA="$(cd "$SALIDA" && pwd)"
if [[ -n "$CANAL" ]]; then mkdir -p "$CANAL"; CANAL="$(cd "$CANAL" && pwd)"; fi
CLAVES_ARGS=()
[[ -z "$CLAVE_EXTRA" ]] || CLAVES_ARGS=(--clave-extra "$CLAVE_EXTRA")
TMP="$(mktemp -d "${TMPDIR:-/tmp}/tatana-docker.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT

sha256_de() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1
  else shasum -a 256 "$1" | cut -d' ' -f1
  fi
}

echo "[1/6] Agente de Linux (payload del AppImage)..."
if [[ -n "$AGENTE_TAR" ]]; then
  [[ -f "$AGENTE_TAR" ]] || { echo "ERROR: no existe $AGENTE_TAR." >&2; exit 1; }
  echo "  Se reusa $AGENTE_TAR"
else
  if [[ -n "$(git -C "$RAIZ" status --porcelain -- server/src/Factum.Agent packaging/linux 2>/dev/null)" ]]; then
    echo "  AVISO: hay cambios sin commitear en server/src/Factum.Agent o packaging/linux: NO entran (se usa git archive de $REF)."
  fi
  mkdir -p "$TMP/src" "$TMP/agente-out"
  git -C "$RAIZ" archive "$REF" | tar -x -C "$TMP/src"
  bash "$ARMAR_LINUX" --version "$VERSION" --src "$TMP/src" --salida "$TMP/agente-out"
  AGENTE_TAR="$TMP/agente-out/Tatana-Agent-v$VERSION-linux-x64.tar.gz"
fi

echo "[2/6] Contexto de build..."
CTX="$TMP/contexto"
mkdir -p "$CTX/agente"
tar -xzf "$AGENTE_TAR" -C "$CTX/agente"
[[ -x "$CTX/agente/Factum.Agent" ]] || { echo "ERROR: el tar del agente no trae Factum.Agent ejecutable." >&2; exit 1; }
VERSION_AGENTE="$(tr -d '[:space:]' <"$CTX/agente/version.txt")"
[[ "$VERSION_AGENTE" == "$VERSION" ]] || { echo "ERROR: el agente es la versión '$VERSION_AGENTE' y se pidió '$VERSION'." >&2; exit 1; }
[[ ! -e "$CTX/agente/appsettings.Local.json" ]] || { echo "ERROR: el tar del agente trae appsettings.Local.json." >&2; exit 1; }
cp "$DIR_SCRIPT/imagen/Dockerfile" "$DIR_SCRIPT/imagen/entrypoint.sh" "$HORNEAR" "$CTX/"
echo "  OK  agente $VERSION_AGENTE"

echo "[3/6] Imagen $IMAGEN (linux/amd64)..."
docker buildx build --platform linux/amd64 --load \
  --build-arg "TATANA_VERSION=$VERSION" -t "$IMAGEN" "$CTX"
docker image inspect "$IMAGEN" --format '  OK  {{.Os}}/{{.Architecture}}, {{.Size}} bytes sin comprimir'

echo "[4/6] Prueba de humo dentro del contenedor..."
# --autoprueba-linux: herramientas portátiles, ffmpeg (pulse/v4l2, grabación lavfi), SIGTERM, glibc.
# --autoprueba-ios: Python + pymobiledevice3 + helper + ffmpeg de iPhone, sin iPhone.
for prueba in --autoprueba-linux --autoprueba-ios; do
  if ! docker run --rm --platform linux/amd64 --network none "$IMAGEN" "$prueba" /tmp/autoprueba >"$TMP/salida$prueba.txt" 2>&1; then
    cat "$TMP/salida$prueba.txt" >&2
    echo "ERROR: $prueba falló dentro de la imagen." >&2
    exit 1
  fi
  grep -E '^(OK|FALLA|RESULTADO)' "$TMP/salida$prueba.txt" | sed 's/^/  /' | cut -c1-160
done

echo "[5/6] Paquete..."
NOMBRE="Tatana-Docker-v$VERSION"
PAQ="$TMP/$NOMBRE"
ARCHIVO_IMAGEN="imagen-tatana-docker-v$VERSION.tar.gz"
ARCHIVO_KIT="kit-tatana-docker-v$VERSION.tar.gz"
mkdir -p "$PAQ/systemd"
docker save "$IMAGEN" | gzip -6 >"$PAQ/$ARCHIVO_IMAGEN"
cp "$DIR_SCRIPT"/paquete/*.sh "$DIR_SCRIPT"/paquete/*.py "$DIR_SCRIPT/paquete/LEEME.txt" "$PAQ/"
cp "$DIR_SCRIPT"/paquete/systemd/*.service "$DIR_SCRIPT"/paquete/systemd/*.timer "$PAQ/systemd/"
chmod 755 "$PAQ"/*.sh "$PAQ"/*.py
chmod 644 "$PAQ/comun.sh" "$PAQ"/systemd/*
# Claves públicas del actualizador: la misma fuente que firmar/verificar (trusted-keys.ts).
node "$RAIZ/ops/tatana/claves-confiables.mjs" ${CLAVES_ARGS[@]+"${CLAVES_ARGS[@]}"} --salida "$PAQ/claves-confiables.txt"
grep -c '^[a-z0-9]' "$PAQ/claves-confiables.txt" | sed 's/^/  OK  claves confiables: /'
printf '%s\n' "$IMAGEN" >"$PAQ/IMAGEN.txt"
printf '%s\n' "$VERSION" >"$PAQ/VERSION"
(
  cd "$PAQ"
  find . -type f ! -name SHA256SUMS | sed 's|^\./||' | LC_ALL=C sort | while IFS= read -r f; do
    printf '%s  %s\n' "$(sha256_de "$f")" "$f"
  done >"$TMP/SHA256SUMS"
  mv "$TMP/SHA256SUMS" SHA256SUMS
)
SALIDA_TAR="$SALIDA/$NOMBRE.tar.gz"
rm -f "$SALIDA_TAR"
if tar --help 2>&1 | grep -q -- --no-xattrs; then SIN_XATTRS=(--no-xattrs); else SIN_XATTRS=(); fi
COPYFILE_DISABLE=1 tar ${SIN_XATTRS[@]+"${SIN_XATTRS[@]}"} -C "$TMP" -czf "$SALIDA_TAR" "$NOMBRE"
printf '%s  %s\n' "$(sha256_de "$SALIDA_TAR")" "$NOMBRE.tar.gz" >"$SALIDA/$NOMBRE.tar.gz.sha256"
# Kit = la misma carpeta sin la imagen (el SHA256SUMS de adentro igual la lista: así el kit + la imagen bajada se
# verifican juntos con sha256sum -c).
COPYFILE_DISABLE=1 tar ${SIN_XATTRS[@]+"${SIN_XATTRS[@]}"} -C "$TMP" --exclude "$NOMBRE/$ARCHIVO_IMAGEN" -czf "$SALIDA/$ARCHIVO_KIT" "$NOMBRE"
echo "  OK  $SALIDA_TAR ($(du -h "$SALIDA_TAR" | cut -f1))"
echo "  OK  imagen comprimida: $(du -h "$PAQ/$ARCHIVO_IMAGEN" | cut -f1)"
echo "  OK  kit: $SALIDA/$ARCHIVO_KIT ($(du -h "$SALIDA/$ARCHIVO_KIT" | cut -f1))"

echo "[6/6] Autopruebas del actualizador con el Python de la imagen..."
for py in verificar-manifiesto-docker.py estado-agente.py; do
  if ! docker run --rm --platform linux/amd64 --network none -v "$PAQ:/k:ro" \
      --entrypoint /opt/tatana/tools/python-embed/bin/python3 "$IMAGEN" -I "/k/$py" --autoprueba >"$TMP/salida-$py.txt" 2>&1; then
    cat "$TMP/salida-$py.txt" >&2
    echo "ERROR: $py --autoprueba falló dentro de la imagen." >&2
    exit 1
  fi
  echo "  OK  $py: $(grep -c '^OK' "$TMP/salida-$py.txt") chequeos ($(grep '^RESULTADO' "$TMP/salida-$py.txt"))"
done
if [[ -n "$CANAL" ]]; then
  cp "$PAQ/$ARCHIVO_IMAGEN" "$SALIDA/$ARCHIVO_KIT" "$CANAL/"
  printf '%s\n' "$VERSION" >"$CANAL/VERSION"
  echo "  OK  canal en $CANAL: $ARCHIVO_IMAGEN, $ARCHIVO_KIT y VERSION (falta firmar tatana-update-docker.json)"
fi
