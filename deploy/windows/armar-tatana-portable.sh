#!/usr/bin/env bash
# Arma el portátil de Tatana para Windows (Tatana-Portable-vX.Y.Z-Windows.zip) en la Mac.
# Réplica local del job build-portable-win de .gitlab-ci.yml (el CI de GitLab no corre: el
# remoto real es GitHub). Lo llama armar-paquete.sh; también se puede usar suelto.
#
#   deploy/windows/armar-tatana-portable.sh --version X.Y.Z --src <árbol limpio> --salida <dir>
#
# --src tiene que ser un árbol LIMPIO (git archive), nunca la copia de trabajo: así no entra un
# appsettings.Local.json ni un "Mock": true local. Igual se verifica dos veces.
# Requisitos: dotnet 10, python3 (con pip), curl, unzip, zip e internet (descarga ~300 MB).
set -euo pipefail

VERSION=""
SRC=""
SALIDA=""

uso() {
  echo "Uso: $0 --version X.Y.Z --src <árbol limpio del repo> --salida <carpeta>" >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="${2:-}"; shift 2 ;;
    --src) SRC="${2:-}"; shift 2 ;;
    --salida) SALIDA="${2:-}"; shift 2 ;;
    -h|--help) uso ;;
    *) echo "Argumento desconocido: $1" >&2; uso ;;
  esac
done

[[ -n "$VERSION" && -n "$SRC" && -n "$SALIDA" ]] || uso
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || { echo "ERROR: --version tiene que ser semver (X.Y.Z), no '$VERSION'." >&2; exit 2; }
[[ -f "$SRC/server/src/Factum.Agent/Factum.Agent.csproj" ]] || { echo "ERROR: '$SRC' no parece el repo de Factum (falta server/src/Factum.Agent)." >&2; exit 2; }

for herramienta in dotnet python3 curl unzip zip; do
  command -v "$herramienta" >/dev/null 2>&1 || { echo "ERROR: falta '$herramienta' en esta Mac." >&2; exit 1; }
done

mkdir -p "$SALIDA"
SALIDA="$(cd "$SALIDA" && pwd)"
TMP="$(mktemp -d "${TMPDIR:-/tmp}/tatana-portable.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT
OUT="$TMP/agent-win-output"

# Guarda de Mock: "Agent": { "Mock": false } en el appsettings.json que viaja al lado del .exe.
verificar_mock() {
  python3 - "$1" <<'PY'
import json, sys
ruta = sys.argv[1]
with open(ruta, encoding="utf-8-sig") as f:
    cfg = json.load(f)
mock = cfg.get("Agent", {}).get("Mock", False)
if mock is not False:
    sys.exit(f"ERROR: {ruta} tiene Agent.Mock = {mock!r}; el portátil tiene que salir con Mock = false.")
print(f"  OK  Agent.Mock = false en {ruta}")
PY
}

echo "[1/6] Verificando Mock en la fuente..."
verificar_mock "$SRC/server/src/Factum.Agent/appsettings.json"

echo "[2/6] Publicando Factum.Agent para win-x64 (self-contained)..."
dotnet publish "$SRC/server/src/Factum.Agent/Factum.Agent.csproj" \
  -c Release -r win-x64 --self-contained true -o "$OUT" --nologo -v quiet
[[ -f "$OUT/Factum.Agent.exe" ]] || { echo "ERROR: no se generó Factum.Agent.exe." >&2; exit 1; }
verificar_mock "$OUT/appsettings.json"
if [[ -e "$OUT/appsettings.Local.json" ]]; then
  echo "ERROR: el publish trae appsettings.Local.json (config local de desarrollo): no puede viajar." >&2
  exit 1
fi

echo "[3/6] Descargando adb (Android platform-tools para Windows)..."
mkdir -p "$OUT/tools"
curl -fsSL -o "$TMP/repo.xml" https://dl.google.com/android/repository/repository2-3.xml
PT_FILE="$(grep -o 'platform-tools_r[0-9.]*-win\.zip' "$TMP/repo.xml" | sort -V | tail -1)"
[[ -n "$PT_FILE" ]] || { echo "ERROR: no se encontró platform-tools para Windows en el manifest de Google." >&2; exit 1; }
curl -fsSL -o "$TMP/platform-tools.zip" "https://dl.google.com/android/repository/$PT_FILE"
unzip -q "$TMP/platform-tools.zip" -d "$TMP/pt"
mv "$TMP/pt/platform-tools" "$OUT/tools/platform-tools"
echo "  OK  $PT_FILE"

echo "[4/6] Python embebido 3.11.9 + pymobiledevice3 (ruedas win_amd64)..."
curl -fsSL -o "$TMP/python-embed.zip" https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip
mkdir -p "$OUT/tools/python-embed"
unzip -q "$TMP/python-embed.zip" -d "$OUT/tools/python-embed"
# Habilitar site-packages en el embebido (viene comentado). sed portable (BSD/GNU).
PTH="$OUT/tools/python-embed/python311._pth"
sed 's/^#import site/import site/' "$PTH" > "$PTH.tmp" && mv "$PTH.tmp" "$PTH"
python3 -m pip install --quiet --disable-pip-version-check --no-cache-dir \
  --target "$OUT/tools/python-embed" --platform win_amd64 --implementation cp \
  --python-version 311 --only-binary=:all: pymobiledevice3
echo "  OK  pymobiledevice3"

echo "[5/6] ffmpeg (BtbN win64 gpl) y uxplay..."
mkdir -p "$OUT/tools/ffmpeg" "$OUT/tools/uxplay"
curl -fsSL -o "$TMP/ffmpeg-win64.zip" https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip
unzip -q "$TMP/ffmpeg-win64.zip" -d "$TMP/ffmpeg-win64"
FFMPEG_EXE="$(find "$TMP/ffmpeg-win64" -iname ffmpeg.exe | head -1)"
[[ -n "$FFMPEG_EXE" ]] || { echo "ERROR: el zip de ffmpeg no trae ffmpeg.exe." >&2; exit 1; }
cp "$FFMPEG_EXE" "$OUT/tools/ffmpeg/ffmpeg.exe"
if [[ -n "${UXPLAY_WIN_ARTIFACT_URL:-}" ]]; then
  curl -fsSL -o "$TMP/uxplay-win.zip" "$UXPLAY_WIN_ARTIFACT_URL"
  unzip -q "$TMP/uxplay-win.zip" -d "$OUT/tools/uxplay"
else
  echo "  AVISO: \$UXPLAY_WIN_ARTIFACT_URL no está configurada — el portátil de Windows quedará sin captura AirPlay. Ver packaging/windows-uxplay-build.md"
fi

echo "[6/6] Scripts del portátil y zip..."
cp "$SRC"/packaging/portable/*.bat "$SRC"/packaging/portable/*.ps1 "$SRC"/packaging/portable/*.ini "$OUT/"
# En la copia (el .ini del repo no cambia, lo usa el CI): sin navegador al iniciar sesión
# (DT4: Factum se abre con el acceso del Escritorio) y sin auto-actualización (no hay
# endpoint de releases en la instalación local).
cat > "$OUT/tatana-portable.ini" <<'INI'
; Config de Tatana Portable para la instalacion local de Factum (Docker en esta PC).
; CLIENT_URL vacio = no abrir el navegador al iniciar sesion (Factum se abre con el
; acceso "Factum" del Escritorio). UPDATE_URL vacio = sin auto-actualizacion.
CLIENT_URL=
UPDATE_URL=
AGENT_PORT=8765
INI
# El .ini lo lee cmd.exe: fin de línea CRLF.
sed 's/$/\r/' "$OUT/tatana-portable.ini" > "$OUT/tatana-portable.ini.tmp" && mv "$OUT/tatana-portable.ini.tmp" "$OUT/tatana-portable.ini"
printf '%s\n' "$VERSION" > "$OUT/version.txt"

ZIP="$SALIDA/Tatana-Portable-v$VERSION-Windows.zip"
rm -f "$ZIP"
(cd "$OUT" && zip -rq "$ZIP" .)
echo "  OK  $ZIP ($(du -h "$ZIP" | cut -f1))"
