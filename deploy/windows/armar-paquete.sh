#!/usr/bin/env bash
# Arma el paquete de instalación de Factum para la PC Windows de un estudio (sin internet).
#
#   deploy/windows/armar-paquete.sh --version X.Y.Z [--ref <git-ref>] [--salida <dir>]
#                                   [--tatana-zip <ruta>] [--sin-tatana]
#
# - Fuente LIMPIA: git archive de --ref (default HEAD). Los cambios sin commitear NO entran
#   (ni .env.local, ni appsettings.Local.json, ni branding/, ni un "Mock": true local).
# - Imágenes linux/amd64: factum-backend:X, factum-frontend:X y mongo:<tag del compose>.
# - Resultado: <salida>/Factum-Instalacion-vX.Y.Z/ y <salida>/Factum-Instalacion-vX.Y.Z.zip
# - No borra imágenes ni toca contenedores: los tags factum-*:X quedan en esta Mac.
#
# Requisitos en la Mac: git, Docker Desktop con buildx, python3, zip, shasum, internet (build
# del frontend descarga dependencias y fuentes) y, para Tatana, dotnet 10.
set -euo pipefail

VERSION=""
REF="HEAD"
SALIDA=""
TATANA_ZIP=""
SIN_TATANA=0

uso() {
  sed -n '2,6p' "$0" | sed 's/^# \{0,1\}//' >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="${2:-}"; shift 2 ;;
    --ref) REF="${2:-}"; shift 2 ;;
    --salida) SALIDA="${2:-}"; shift 2 ;;
    --tatana-zip) TATANA_ZIP="${2:-}"; shift 2 ;;
    --sin-tatana) SIN_TATANA=1; shift ;;
    -h|--help) uso ;;
    *) echo "Argumento desconocido: $1" >&2; uso ;;
  esac
done

[[ -n "$VERSION" ]] || { echo "ERROR: falta --version X.Y.Z" >&2; uso; }
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || { echo "ERROR: --version tiene que ser semver (X.Y.Z), no '$VERSION'." >&2; exit 2; }
if [[ -n "$TATANA_ZIP" && "$SIN_TATANA" == 1 ]]; then
  echo "ERROR: --tatana-zip y --sin-tatana son excluyentes." >&2; exit 2
fi
if [[ -n "$TATANA_ZIP" && ! -f "$TATANA_ZIP" ]]; then
  echo "ERROR: no existe $TATANA_ZIP" >&2; exit 2
fi

for herramienta in git docker python3 zip shasum tar; do
  command -v "$herramienta" >/dev/null 2>&1 || { echo "ERROR: falta '$herramienta' en esta Mac." >&2; exit 1; }
done
docker buildx version >/dev/null 2>&1 || { echo "ERROR: Docker no tiene buildx." >&2; exit 1; }

DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(git -C "$DIR_SCRIPT" rev-parse --show-toplevel)"
[[ -n "$SALIDA" ]] || SALIDA="$REPO/deploy/windows/dist"
mkdir -p "$SALIDA"
SALIDA="$(cd "$SALIDA" && pwd)"
[[ -n "$TATANA_ZIP" ]] && TATANA_ZIP="$(cd "$(dirname "$TATANA_ZIP")" && pwd)/$(basename "$TATANA_ZIP")"

COMMIT="$(git -C "$REPO" rev-parse --verify "$REF^{commit}")" || { echo "ERROR: '$REF' no es un commit válido." >&2; exit 2; }
NOMBRE="Factum-Instalacion-v$VERSION"
PAQ="$SALIDA/$NOMBRE"
TMP="$(mktemp -d "${TMPDIR:-/tmp}/factum-paquete.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT
SRC="$TMP/src"

echo "== Paquete de Factum $VERSION desde $REF ($COMMIT) =="

# 1 ── Avisar qué cambios locales NO van a entrar
echo "[1/9] Revisando la copia de trabajo..."
if [[ -n "$(git -C "$REPO" status --porcelain)" ]]; then
  echo "  AVISO: estos cambios locales NO entran al paquete (se usa git archive de $REF):"
  git -C "$REPO" status --porcelain | sed 's/^/    /'
else
  echo "  OK  copia de trabajo limpia"
fi

# 2 ── Fuente limpia
echo "[2/9] Extrayendo la fuente limpia (git archive)..."
mkdir -p "$SRC"
git -C "$REPO" archive "$COMMIT" | tar -x -C "$SRC"
for f in deploy/windows/docker-compose.yml deploy/windows/docker-compose.poca-ram.yml deploy/windows/.env.example deploy/windows/appsettings.Local.example.json \
         deploy/windows/LEEME.txt docs/instalacion-windows.md client/Dockerfile server/src/Factum.Backend/Dockerfile; do
  [[ -f "$SRC/$f" ]] || { echo "ERROR: $f no está en $REF (¿falta commitear?)." >&2; exit 1; }
done
echo "  OK"

# 3 ── Guarda de Mock
echo "[3/9] Verificando que Tatana no esté en modo simulado..."
python3 - "$SRC/server/src/Factum.Agent/appsettings.json" <<'PY'
import json, sys
ruta = sys.argv[1]
with open(ruta, encoding="utf-8-sig") as f:
    mock = json.load(f).get("Agent", {}).get("Mock", False)
if mock is not False:
    sys.exit(f"ERROR: Agent.Mock = {mock!r} en {ruta} (de {sys.argv[1]}); tiene que ser false.")
print("  OK  Agent.Mock = false")
PY

# Tag exacto de mongo: fuente única = el compose (nunca duplicado acá)
MONGO_IMG="$(sed -nE 's/^[[:space:]]*image:[[:space:]]*(mongo:[^[:space:]#]+).*/\1/p' "$SRC/deploy/windows/docker-compose.yml" | head -1)"
[[ "$MONGO_IMG" =~ ^mongo:[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "ERROR: el compose no fija un tag exacto de mongo (encontrado: '$MONGO_IMG')." >&2; exit 1; }
BACK_IMG="factum-backend:$VERSION"
FRONT_IMG="factum-frontend:$VERSION"

# 4 ── Imágenes linux/amd64 (sin atestaciones de buildx: no sirven sin registro y suman ruido al tar)
echo "[4/9] Construyendo $BACK_IMG (linux/amd64)..."
docker buildx build --platform linux/amd64 --provenance=false --sbom=false --load -t "$BACK_IMG" "$SRC/server/src/Factum.Backend"
echo "[4/9] Construyendo $FRONT_IMG (linux/amd64 emulado; puede tardar más de 10 minutos)..."
docker buildx build --platform linux/amd64 --provenance=false --sbom=false --load -t "$FRONT_IMG" \
  --build-arg NEXT_PUBLIC_BACKEND_URL=http://localhost:8080 \
  --build-arg NEXT_PUBLIC_AGENT_URL=http://localhost:8765 \
  "$SRC/client"
echo "[4/9] Descargando $MONGO_IMG (linux/amd64)..."
docker pull --platform linux/amd64 "$MONGO_IMG"

# 5 ── Arquitectura
echo "[5/9] Verificando arquitectura..."
for img in "$BACK_IMG" "$FRONT_IMG" "$MONGO_IMG"; do
  # containerd image store: el tag apunta a un índice multi-plataforma y, sin --platform, Docker
  # responde con la variante del host (arm64 en la Mac) si también está descargada. Se pide
  # siempre la variante amd64; si este Docker no admite --platform, se usa la forma simple.
  arq="$(docker image inspect --platform linux/amd64 -f '{{.Os}}/{{.Architecture}}' "$img" 2>/dev/null)" \
    || arq="$(docker image inspect -f '{{.Os}}/{{.Architecture}}' "$img")"
  [[ "$arq" == "linux/amd64" ]] || { echo "ERROR: $img es $arq, no linux/amd64." >&2; exit 1; }
  echo "  OK  $img = $arq"
done

# 6 ── docker save (solo amd64) + verificación del contenido del tar
echo "[6/9] Exportando las imágenes..."
rm -rf "$PAQ" "$SALIDA/$NOMBRE.zip"
mkdir -p "$PAQ/imagenes" "$PAQ/scripts"
TAR="$PAQ/imagenes/factum-v$VERSION.tar"
if docker save --help 2>/dev/null | grep -q -- '--platform'; then
  docker save --platform linux/amd64 -o "$TAR" "$BACK_IMG" "$FRONT_IMG" "$MONGO_IMG"
else
  docker save -o "$TAR" "$BACK_IMG" "$FRONT_IMG" "$MONGO_IMG"
fi
python3 - "$TAR" "$BACK_IMG" "$FRONT_IMG" "$MONGO_IMG" <<'PY'
# Recorre index.json del tar (OCI) y cada manifiesto hasta su config: toda imagen tiene que ser
# linux/amd64 (las atestaciones de buildx, sin plataforma real, se ignoran).
import json, sys, tarfile
tar_path, *esperadas = sys.argv[1:]
tf = tarfile.open(tar_path)
def blob(digest):
    alg, h = digest.split(":", 1)
    return json.load(tf.extractfile(f"blobs/{alg}/{h}"))
plataformas, tags, atestaciones = set(), set(), []
def recorrer(desc):
    mt = desc.get("mediaType", "")
    ref = desc.get("annotations", {}).get("io.containerd.image.name") or desc.get("annotations", {}).get("org.opencontainers.image.ref.name")
    if ref: tags.add(ref)
    if mt.endswith("image.index.v1+json") or mt.endswith("manifest.list.v2+json"):
        for d in blob(desc["digest"]).get("manifests", []):
            if d.get("annotations", {}).get("vnd.docker.reference.type") == "attestation-manifest":
                continue
            try:
                recorrer(d)
            except KeyError:
                pass  # variante no incluida en el tar (otra plataforma no exportada)
    elif mt.endswith("image.manifest.v1+json") or mt.endswith("manifest.v2+json"):
        man = blob(desc["digest"])
        # Atestación de buildx (procedencia/SBOM): capas in-toto, sin plataforma; se ignora.
        if "io.containerd.manifest.subject" in desc.get("annotations", {}) or \
           any(l.get("mediaType") == "application/vnd.in-toto+json" for l in man.get("layers", [])):
            atestaciones.append(desc["digest"])
            return
        cfg = blob(man["config"]["digest"])
        plataformas.add(f'{cfg.get("os")}/{cfg.get("architecture")}')
for d in json.load(tf.extractfile("index.json"))["manifests"]:
    recorrer(d)
print(f"  plataformas en el tar: {sorted(plataformas)} (+{len(atestaciones)} atestación/es de buildx, sin plataforma)")
if plataformas != {"linux/amd64"}:
    sys.exit("ERROR: el tar no trae solo linux/amd64.")
try:  # formato docker clásico, presente también en los tar del containerd store
    for m in json.load(tf.extractfile("manifest.json")):
        tags.update(m.get("RepoTags") or [])
except KeyError:
    pass
for e in esperadas:
    if not any(t == e or t.endswith("/" + e) for t in tags):
        sys.exit(f"ERROR: el tar no trae {e} (tags: {sorted(tags)}).")
print("  OK  solo linux/amd64; imágenes: " + ", ".join(esperadas))
PY

# 7 ── Tatana
echo "[7/9] Tatana..."
if [[ "$SIN_TATANA" == 1 ]]; then
  echo "  (omitido: --sin-tatana; solo para verificación)"
elif [[ -n "$TATANA_ZIP" ]]; then
  mkdir -p "$PAQ/tatana"
  cp "$TATANA_ZIP" "$PAQ/tatana/"
  echo "  OK  $(basename "$TATANA_ZIP") (ya armado)"
else
  mkdir -p "$PAQ/tatana"
  "$DIR_SCRIPT/armar-tatana-portable.sh" --version "$VERSION" --src "$SRC" --salida "$PAQ/tatana"
fi

# 8 ── Estructura del paquete (todo desde la fuente limpia)
echo "[8/9] Armando $NOMBRE..."
W="$SRC/deploy/windows"
cp "$W/docker-compose.yml" "$W/docker-compose.poca-ram.yml" "$W/.env.example" "$W/appsettings.Local.example.json" "$W/LEEME.txt" "$PAQ/"
cp "$W/1-Instalar Factum.bat" "$W/Actualizar Factum.bat" "$PAQ/"
cp "$W"/scripts/*.ps1 "$W"/scripts/*.psd1 "$W"/scripts/*.bat "$PAQ/scripts/"
cp "$SRC/docs/instalacion-windows.md" "$PAQ/"
cp "$SRC/client/public/logo-app.ico" "$PAQ/factum.ico"
printf '%s\r\n' "$VERSION" > "$PAQ/version.txt"

# 9 ── SHA256SUMS.txt (formato §7.3: "<sha256>  <ruta/relativa>", LF, orden por bytes) + zip
echo "[9/9] Calculando SHA256SUMS.txt y comprimiendo..."
(
  cd "$PAQ"
  find . -type f ! -name SHA256SUMS.txt | sed 's|^\./||' | LC_ALL=C sort | while IFS= read -r f; do
    shasum -a 256 "$f"
  done > "$TMP/SHA256SUMS.txt"
  mv "$TMP/SHA256SUMS.txt" SHA256SUMS.txt
  shasum -a 256 -c --quiet SHA256SUMS.txt
)
(cd "$SALIDA" && zip -rq "$NOMBRE.zip" "$NOMBRE")

echo ""
echo "== Listo =="
echo "  Paquete:  $PAQ"
echo "  Zip:      $SALIDA/$NOMBRE.zip ($(du -h "$SALIDA/$NOMBRE.zip" | cut -f1))"
echo "  Imágenes: $TAR ($(du -h "$TAR" | cut -f1))"
echo "            $BACK_IMG, $FRONT_IMG, $MONGO_IMG"
echo "  Los tags quedan en esta Mac (no se borran)."
