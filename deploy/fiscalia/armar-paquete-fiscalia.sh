#!/usr/bin/env bash
# Arma el paquete de Factum para la fiscalía (Linux, sin internet): plan A (servidor + peritos) y plan B (una PC).
# Corre en la Mac. SDD Refactorizaciones/instalacion-fiscalia.md, §3.1.
#
#   deploy/fiscalia/armar-paquete-fiscalia.sh --version X.Y.Z [--ref <git-ref>] [--salida <dir>]
#                                             [--tatana-appimage <ruta .AppImage>] [--deploy-de-trabajo]
#
# - Fuente LIMPIA: git archive de --ref (default HEAD); los cambios sin commitear NO entran.
#   --deploy-de-trabajo toma deploy/fiscalia/ y docs/instalacion-fiscalia.md de la copia de trabajo (solo para
#   probar los scripts antes de commitearlos; el código de la app sigue saliendo de --ref).
# - Imágenes linux/amd64: factum-backend:X, factum-frontend:X (plan B, http://localhost:8080),
#   factum-frontend-servidor:X (plan A, https://factum.internal), mongo y caddy con el tag de los compose, y
#   mongo 4.4 (imagenes/mongo-4.4.tar, tag de comun/compose.sin-avx.yml) para las PCs sin AVX.
#   Un tar por imagen en imagenes/ (cada plan carga solo las suyas).
# - herramientas/: Docker Engine estático (docker-<versión>.tgz) + plugin compose v2, con versión y SHA-256
#   fijos, e instalar-docker-offline.sh (Docker sin internet ni gestor de paquetes; guía, sección 2.3).
# - Resultado: <salida>/Factum-Fiscalia-vX.Y.Z/ y <salida>/Factum-Fiscalia-vX.Y.Z.tar.gz (default deploy/fiscalia/dist).
# - No borra imágenes ni toca contenedores: los tags quedan en esta Mac.
# Requisitos en la Mac: git, Docker Desktop con buildx, python3, curl, tar, gzip, shasum, internet.
set -euo pipefail

COMPOSE_VERSION="v2.40.3"
COMPOSE_SHA256="dba9d98e1ba5bfe11d88c99b9bd32fc4a0624a30fafe68eea34d61a3e42fd372"
COMPOSE_URL="https://github.com/docker/compose/releases/download/$COMPOSE_VERSION/docker-compose-linux-x86_64"
# Docker Engine estático oficial (las PCs de la fiscalía no tienen Docker y el internet es lento). Docker no
# publica .sha256 de estos .tgz: el valor se calculó al fijar la versión. Para actualizar: versión y SHA juntas.
DOCKER_VERSION="29.9.1"
DOCKER_SHA256="c158ecc55adb3afb01bbd3539ed3b997d20c26dbdc418080506cc1bf5243ff57"
DOCKER_URL="https://download.docker.com/linux/static/stable/x86_64/docker-$DOCKER_VERSION.tgz"
NOMBRE_RED="factum.internal"

VERSION=""
REF="HEAD"
SALIDA=""
APPIMAGE=""
DEPLOY_TRABAJO=0
uso() {
  sed -n '5,7p' "$0" | sed 's/^# \{0,1\}//' >&2
  exit 2
}
while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="${2:-}"; shift 2 ;;
    --ref) REF="${2:-}"; shift 2 ;;
    --salida) SALIDA="${2:-}"; shift 2 ;;
    --tatana-appimage) APPIMAGE="${2:-}"; shift 2 ;;
    --deploy-de-trabajo) DEPLOY_TRABAJO=1; shift ;;
    -h | --help) uso ;;
    *) echo "Argumento desconocido: $1" >&2; uso ;;
  esac
done
[[ -n "$VERSION" ]] || { echo "ERROR: falta --version X.Y.Z" >&2; uso; }
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || { echo "ERROR: --version tiene que ser semver (X.Y.Z), no '$VERSION'." >&2; exit 2; }
if [[ -n "$APPIMAGE" ]]; then
  [[ -f "$APPIMAGE" ]] || { echo "ERROR: no existe $APPIMAGE" >&2; exit 2; }
  [[ "$APPIMAGE" == *.AppImage ]] || { echo "ERROR: --tatana-appimage tiene que ser un .AppImage" >&2; exit 2; }
  head -c 4 "$APPIMAGE" | od -An -tx1 | grep -q '7f 45 4c 46' || { echo "ERROR: $APPIMAGE no es un ejecutable ELF (¿es el zip del artifact? descomprimilo)." >&2; exit 2; }
fi
for h in git docker python3 curl tar gzip shasum; do
  command -v "$h" >/dev/null 2>&1 || { echo "ERROR: falta '$h' en esta Mac." >&2; exit 1; }
done
docker buildx version >/dev/null 2>&1 || { echo "ERROR: Docker no tiene buildx." >&2; exit 1; }

DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(git -C "$DIR_SCRIPT" rev-parse --show-toplevel)"
[[ -n "$SALIDA" ]] || SALIDA="$REPO/deploy/fiscalia/dist"
mkdir -p "$SALIDA"
SALIDA="$(cd "$SALIDA" && pwd)"
[[ -n "$APPIMAGE" ]] && APPIMAGE="$(cd "$(dirname "$APPIMAGE")" && pwd)/$(basename "$APPIMAGE")"
COMMIT="$(git -C "$REPO" rev-parse --verify "$REF^{commit}")" || { echo "ERROR: '$REF' no es un commit válido." >&2; exit 2; }
NOMBRE="Factum-Fiscalia-v$VERSION"
PAQ="$SALIDA/$NOMBRE"
TMP="$(mktemp -d "${TMPDIR:-/tmp}/factum-fiscalia.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT
SRC="$TMP/src"
T=10

echo "== Paquete de Factum para la fiscalía $VERSION desde $REF ($COMMIT) =="

echo "[1/$T] Revisando la copia de trabajo..."
if [[ -n "$(git -C "$REPO" status --porcelain)" ]]; then
  echo "  AVISO: estos cambios locales NO entran al paquete (git archive de $REF):"
  git -C "$REPO" status --porcelain | sed 's/^/    /'
else
  echo "  OK  copia de trabajo limpia"
fi

echo "[2/$T] Extrayendo la fuente limpia (git archive)..."
mkdir -p "$SRC"
git -C "$REPO" archive "$COMMIT" | tar -x -C "$SRC"
if ((DEPLOY_TRABAJO)); then
  echo "  AVISO: --deploy-de-trabajo: deploy/fiscalia y docs/instalacion-fiscalia.md salen de la copia de trabajo."
  rm -rf "$SRC/deploy/fiscalia"
  mkdir -p "$SRC/deploy/fiscalia"
  (cd "$REPO/deploy/fiscalia" && tar -cf - --exclude ./dist .) | tar -x -C "$SRC/deploy/fiscalia"
  cp "$REPO/docs/instalacion-fiscalia.md" "$SRC/docs/instalacion-fiscalia.md"
fi
F="$SRC/deploy/fiscalia"
for f in deploy/fiscalia/comun/docker-compose.yml deploy/fiscalia/comun/compose.sin-avx.yml \
  deploy/fiscalia/comun/mongo-init-sin-avx/01-usuario-app.js deploy/fiscalia/servidor/compose.servidor.yml deploy/fiscalia/servidor/Caddyfile \
  deploy/fiscalia/una-pc/compose.una-pc.yml deploy/fiscalia/perito/preparar-pc-perito.sh deploy/fiscalia/LEEME.txt \
  deploy/fiscalia/verificar-tar-imagen.py deploy/fiscalia/herramientas/instalar-docker-offline.sh deploy/cloud/mongo-init/01-usuario-app.js deploy/windows/appsettings.Local.example.json \
  docs/instalacion-fiscalia.md client/Dockerfile server/src/Factum.Backend/Dockerfile; do
  [[ -f "$SRC/$f" ]] || { echo "ERROR: $f no está en $REF (¿falta commitear? o usá --deploy-de-trabajo para probar)." >&2; exit 1; }
done
echo "  Sintaxis de los scripts (bash -n)..."
for s in "$F"/*.sh "$F"/*/*.sh; do bash -n "$s" || { echo "ERROR: $s tiene errores de sintaxis." >&2; exit 1; }; done
echo "  OK"

# Tags exactos: fuente única = los compose de deploy/fiscalia (nunca duplicados acá).
MONGO_IMG="$(sed -nE 's/^[[:space:]]*image:[[:space:]]*(mongo:[^[:space:]#]+).*/\1/p' "$F/comun/docker-compose.yml" | head -1)"
CADDY_IMG="$(sed -nE 's/^[[:space:]]*image:[[:space:]]*(caddy:[^[:space:]#]+).*/\1/p' "$F/servidor/compose.servidor.yml" | head -1)"
MONGO44_IMG="$(sed -nE 's/^[[:space:]]*image:[[:space:]]*(mongo:[^[:space:]#]+).*/\1/p' "$F/comun/compose.sin-avx.yml" | head -1)"
[[ "$MONGO_IMG" =~ ^mongo:[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "ERROR: tag de mongo no exacto: '$MONGO_IMG'." >&2; exit 1; }
[[ "$MONGO44_IMG" =~ ^mongo:4\.4\.[0-9]+$ ]] || { echo "ERROR: tag de mongo sin AVX no exacto (4.4.x): '$MONGO44_IMG'." >&2; exit 1; }
[[ "$CADDY_IMG" =~ ^caddy:[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "ERROR: tag de caddy no exacto: '$CADDY_IMG'." >&2; exit 1; }
BACK_IMG="factum-backend:$VERSION"
FRONT_B_IMG="factum-frontend:$VERSION"
FRONT_A_IMG="factum-frontend-servidor:$VERSION"

echo "[3/$T] Verificando que Tatana no esté en modo simulado..."
python3 - "$SRC/server/src/Factum.Agent/appsettings.json" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8-sig") as f:
    mock = json.load(f).get("Agent", {}).get("Mock", False)
if mock is not False:
    sys.exit(f"ERROR: Agent.Mock = {mock!r}; tiene que ser false.")
print("  OK  Agent.Mock = false")
PY

echo "[4/$T] Construyendo $BACK_IMG (linux/amd64)..."
docker buildx build --platform linux/amd64 --provenance=false --sbom=false --load -t "$BACK_IMG" "$SRC/server/src/Factum.Backend"
echo "[4/$T] Construyendo $FRONT_B_IMG (plan B: http://localhost:8080; emulado, puede tardar más de 10 minutos)..."
docker buildx build --platform linux/amd64 --provenance=false --sbom=false --load -t "$FRONT_B_IMG" \
  --build-arg NEXT_PUBLIC_BACKEND_URL=http://localhost:8080 \
  --build-arg NEXT_PUBLIC_AGENT_URL=http://localhost:8765 \
  "$SRC/client"
echo "[4/$T] Construyendo $FRONT_A_IMG (plan A: https://$NOMBRE_RED)..."
docker buildx build --platform linux/amd64 --provenance=false --sbom=false --load -t "$FRONT_A_IMG" \
  --build-arg NEXT_PUBLIC_BACKEND_URL="https://$NOMBRE_RED" \
  --build-arg NEXT_PUBLIC_AGENT_URL=http://localhost:8765 \
  "$SRC/client"
echo "[4/$T] Descargando $MONGO_IMG y $CADDY_IMG (linux/amd64)..."
docker pull --platform linux/amd64 "$MONGO_IMG"
docker pull --platform linux/amd64 "$MONGO44_IMG"
docker pull --platform linux/amd64 "$CADDY_IMG"

echo "[5/$T] Verificando arquitectura..."
for img in "$BACK_IMG" "$FRONT_B_IMG" "$FRONT_A_IMG" "$MONGO_IMG" "$MONGO44_IMG" "$CADDY_IMG"; do
  arq="$(docker image inspect --platform linux/amd64 -f '{{.Os}}/{{.Architecture}}' "$img" 2>/dev/null)" ||
    arq="$(docker image inspect -f '{{.Os}}/{{.Architecture}}' "$img")"
  [[ "$arq" == "linux/amd64" ]] || { echo "ERROR: $img es $arq, no linux/amd64." >&2; exit 1; }
  echo "  OK  $img = $arq"
done

echo "[6/$T] Exportando las imágenes (un tar por imagen)..."
rm -rf "$PAQ" "$SALIDA/$NOMBRE.tar.gz"
mkdir -p "$PAQ/imagenes"
PLAT=()
docker save --help 2>/dev/null | grep -q -- '--platform' && PLAT=(--platform linux/amd64)
guardar() { # guardar <archivo> <imagen>
  docker save ${PLAT[@]+"${PLAT[@]}"} -o "$PAQ/imagenes/$1.tar" "$2"
  python3 "$F/verificar-tar-imagen.py" "$PAQ/imagenes/$1.tar" "$2"
}
guardar backend "$BACK_IMG"
guardar frontend-una-pc "$FRONT_B_IMG"
guardar frontend-servidor "$FRONT_A_IMG"
guardar mongo "$MONGO_IMG"
guardar mongo-4.4 "$MONGO44_IMG"
guardar caddy "$CADDY_IMG"

echo "[7/$T] Docker $DOCKER_VERSION estático + compose $COMPOSE_VERSION (linux x86_64)..."
mkdir -p "$PAQ/herramientas"
CACHE="$SALIDA/.cache"
mkdir -p "$CACHE"
bajar_verificado() { # bajar_verificado <url> <archivo en cache> <sha256>
  if [[ ! -f "$CACHE/$2" ]] || [[ "$(shasum -a 256 "$CACHE/$2" | cut -d' ' -f1)" != "$3" ]]; then
    curl -fsSL --retry 3 -o "$CACHE/$2.parcial" "$1"
    mv "$CACHE/$2.parcial" "$CACHE/$2"
  fi
  [[ "$(shasum -a 256 "$CACHE/$2" | cut -d' ' -f1)" == "$3" ]] || { echo "ERROR: el SHA-256 de $2 no coincide con el fijado." >&2; exit 1; }
  echo "  OK  $2 (SHA-256 verificado)"
}
bajar_verificado "$DOCKER_URL" "docker-$DOCKER_VERSION.tgz" "$DOCKER_SHA256"
bajar_verificado "$COMPOSE_URL" "docker-compose-$COMPOSE_VERSION" "$COMPOSE_SHA256"
cp "$CACHE/docker-$DOCKER_VERSION.tgz" "$PAQ/herramientas/"
cp "$CACHE/docker-compose-$COMPOSE_VERSION" "$PAQ/herramientas/docker-compose-linux-x86_64"
chmod 755 "$PAQ/herramientas/docker-compose-linux-x86_64"
cp "$F/herramientas/instalar-docker-offline.sh" "$PAQ/herramientas/"
chmod 755 "$PAQ/herramientas/instalar-docker-offline.sh"
printf 'DOCKER_VERSION=%s\nDOCKER_SHA256=%s\nCOMPOSE_VERSION=%s\nCOMPOSE_SHA256=%s\n' \
  "$DOCKER_VERSION" "$DOCKER_SHA256" "$COMPOSE_VERSION" "$COMPOSE_SHA256" >"$PAQ/herramientas/VERSIONES"

echo "[8/$T] Tatana..."
if [[ -n "$APPIMAGE" ]]; then
  mkdir -p "$PAQ/tatana"
  cp "$APPIMAGE" "$PAQ/tatana/"
  chmod 755 "$PAQ/tatana/$(basename "$APPIMAGE")"
  echo "  OK  $(basename "$APPIMAGE")"
else
  echo "  AVISO: sin --tatana-appimage: el paquete no trae Tatana (los peritos lo instalan aparte)."
fi

echo "[9/$T] Armando $NOMBRE..."
mkdir -p "$PAQ"/{comun/mongo-init,comun/mongo-init-sin-avx,servidor,perito,una-pc}
cp "$F/comun/docker-compose.yml" "$F/comun/compose.superadmin.yml" "$F/comun/compose.sin-avx.yml" "$F"/comun/*.sh "$PAQ/comun/"
cp "$F/comun/mongo-init-sin-avx/01-usuario-app.js" "$PAQ/comun/mongo-init-sin-avx/"
cp "$SRC/deploy/cloud/mongo-init/01-usuario-app.js" "$PAQ/comun/mongo-init/"
cp "$SRC/deploy/windows/appsettings.Local.example.json" "$PAQ/comun/"
cp "$F/servidor/instalar-servidor.sh" "$F/servidor/compose.servidor.yml" "$F/servidor/Caddyfile" "$PAQ/servidor/"
cp "$F/perito/preparar-pc-perito.sh" "$PAQ/perito/"
cp "$F/una-pc/instalar-una-pc.sh" "$F/una-pc/compose.una-pc.yml" "$PAQ/una-pc/"
cp "$F/LEEME.txt" "$PAQ/"
cp "$SRC/docs/instalacion-fiscalia.md" "$PAQ/"
chmod 755 "$PAQ"/*/*.sh
printf '%s\n' "$VERSION" >"$PAQ/version.txt"
printf 'version=%s\nref=%s\ncommit=%s\narmado=%s\ndeploy_de_trabajo=%s\n' "$VERSION" "$REF" "$COMMIT" "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" "$DEPLOY_TRABAJO" >"$PAQ/origen.txt"

echo "[10/$T] SHA256SUMS y .tar.gz..."
(
  cd "$PAQ"
  find . -type f ! -name SHA256SUMS | sed 's|^\./||' | LC_ALL=C sort | while IFS= read -r f; do
    printf '%s  %s\n' "$(shasum -a 256 "$f" | cut -d' ' -f1)" "$f"
  done >"$TMP/SHA256SUMS"
  mv "$TMP/SHA256SUMS" SHA256SUMS
  shasum -a 256 -c --quiet SHA256SUMS
)
# Sin atributos extendidos de macOS (._*) en el tar: en Linux aparecerían como archivos basura.
if tar --help 2>&1 | grep -q -- --no-xattrs; then SIN_XATTRS=(--no-xattrs); else SIN_XATTRS=(); fi
(cd "$SALIDA" && COPYFILE_DISABLE=1 tar ${SIN_XATTRS[@]+"${SIN_XATTRS[@]}"} -czf "$NOMBRE.tar.gz.parcial" "$NOMBRE")
mv "$SALIDA/$NOMBRE.tar.gz.parcial" "$SALIDA/$NOMBRE.tar.gz"
echo "  Verificando el .tar.gz (gzip -t y lista de archivos)..."
gzip -t "$SALIDA/$NOMBRE.tar.gz"
EN_TAR="$(tar -tzf "$SALIDA/$NOMBRE.tar.gz" | grep -cv '/$')"
EN_DIR="$(find "$PAQ" -type f | wc -l | tr -d ' ')"
[[ "$EN_TAR" == "$EN_DIR" ]] || { echo "ERROR: el .tar.gz tiene $EN_TAR archivos y la carpeta $EN_DIR." >&2; exit 1; }
if tar -tzf "$SALIDA/$NOMBRE.tar.gz" | grep -q '/\._'; then echo "ERROR: el .tar.gz trae archivos ._ de macOS." >&2; exit 1; fi
(cd "$SALIDA" && shasum -a 256 "$NOMBRE.tar.gz" >"$NOMBRE.tar.gz.sha256")

echo ""
echo "== Listo =="
echo "  Carpeta:  $PAQ ($(du -sh "$PAQ" | cut -f1))"
echo "  tar.gz:   $SALIDA/$NOMBRE.tar.gz ($(du -h "$SALIDA/$NOMBRE.tar.gz" | cut -f1)), $EN_TAR archivos"
for t in "$PAQ"/imagenes/*.tar; do echo "            $(basename "$t"): $(du -h "$t" | cut -f1)"; done
echo "  Para el pendrive: copiá la CARPETA (o el .tar.gz + .sha256) — formateado en exFAT o ext4 (FAT32 no admite archivos de más de 4 GB)."
