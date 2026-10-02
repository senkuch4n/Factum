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

# ── scrcpy (Genymobile, Apache-2.0): versión FIJA, verificada por SHA-256 ──────────────────
# Para actualizarla: cambiar SCRCPY_VERSION y SCRCPY_WIN64_SHA256 juntas, sacando el hash de
# scrcpy-win64-v<versión>.zip del SHA256SUMS.txt del release
# (https://github.com/Genymobile/scrcpy/releases). Revisar también las versiones de FFmpeg,
# SDL, libusb y dav1d de THIRD-PARTY-NOTICES.txt (abajo) contra app/deps/*.sh del tag nuevo.
SCRCPY_VERSION="4.1"
SCRCPY_WIN64_SHA256="5b12172b3264b2889f4583ee64752ce832e29bc8b1089dca81093459697165db"

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

echo "[1/7] Verificando Mock en la fuente..."
verificar_mock "$SRC/server/src/Factum.Agent/appsettings.json"

echo "[2/7] Publicando Factum.Agent para win-x64 (self-contained)..."
dotnet publish "$SRC/server/src/Factum.Agent/Factum.Agent.csproj" \
  -c Release -r win-x64 --self-contained true -o "$OUT" --nologo -v quiet
[[ -f "$OUT/Factum.Agent.exe" ]] || { echo "ERROR: no se generó Factum.Agent.exe." >&2; exit 1; }
verificar_mock "$OUT/appsettings.json"
if [[ -e "$OUT/appsettings.Local.json" ]]; then
  echo "ERROR: el publish trae appsettings.Local.json (config local de desarrollo): no puede viajar." >&2
  exit 1
fi

echo "[3/7] Descargando adb (Android platform-tools para Windows)..."
mkdir -p "$OUT/tools"
curl -fsSL -o "$TMP/repo.xml" https://dl.google.com/android/repository/repository2-3.xml
PT_FILE="$(grep -o 'platform-tools_r[0-9.]*-win\.zip' "$TMP/repo.xml" | sort -V | tail -1)"
[[ -n "$PT_FILE" ]] || { echo "ERROR: no se encontró platform-tools para Windows en el manifest de Google." >&2; exit 1; }
curl -fsSL -o "$TMP/platform-tools.zip" "https://dl.google.com/android/repository/$PT_FILE"
unzip -q "$TMP/platform-tools.zip" -d "$TMP/pt"
mv "$TMP/pt/platform-tools" "$OUT/tools/platform-tools"
echo "  OK  $PT_FILE"

echo "[4/7] scrcpy $SCRCPY_VERSION (Genymobile, Apache-2.0)..."
curl -fsSL -o "$TMP/scrcpy-win64.zip" \
  "https://github.com/Genymobile/scrcpy/releases/download/v$SCRCPY_VERSION/scrcpy-win64-v$SCRCPY_VERSION.zip"
if ! echo "$SCRCPY_WIN64_SHA256  $TMP/scrcpy-win64.zip" | shasum -a 256 -c - >/dev/null 2>&1; then
  echo "ERROR: el SHA-256 de scrcpy no coincide (¿descarga corrupta o release reemplazado?)." >&2
  exit 1
fi
unzip -q "$TMP/scrcpy-win64.zip" -d "$TMP/scrcpy"
[[ -d "$TMP/scrcpy/scrcpy-win64-v$SCRCPY_VERSION" ]] || { echo "ERROR: el zip de scrcpy no trae scrcpy-win64-v$SCRCPY_VERSION/." >&2; exit 1; }
mkdir -p "$OUT/tools/scrcpy"
cp -R "$TMP/scrcpy/scrcpy-win64-v$SCRCPY_VERSION/." "$OUT/tools/scrcpy/"
# Sin el adb propio de scrcpy: Tatana usa el de tools/platform-tools (un solo servidor adb).
rm -f "$OUT/tools/scrcpy/adb.exe" "$OUT/tools/scrcpy/AdbWinApi.dll" "$OUT/tools/scrcpy/AdbWinUsbApi.dll"
cat > "$OUT/tools/scrcpy/THIRD-PARTY-NOTICES.txt" <<NOTICES
Avisos de terceros - tools/scrcpy de Tatana
===========================================

scrcpy $SCRCPY_VERSION
  Copyright (C) Genymobile / Romain Vimont.
  Licencia: Apache License 2.0 (texto completo en LICENSE.txt, en esta carpeta).
  Fuente: https://github.com/Genymobile/scrcpy
  Archivo distribuido: scrcpy-win64-v$SCRCPY_VERSION.zip
  SHA-256 del zip: $SCRCPY_WIN64_SHA256
  Modificacion: se quitaron adb.exe, AdbWinApi.dll y AdbWinUsbApi.dll del zip original.
  Tatana usa el adb de tools/platform-tools.

Bibliotecas incluidas en el zip de scrcpy:

FFmpeg 8.1.2 (avcodec-62.dll, avformat-62.dll, avutil-60.dll, swresample-6.dll)
  Licencia: GNU LGPL v2.1 o posterior.
  Fuente: https://ffmpeg.org (codigo fuente de la release 8.1.2).
  Configuracion de build: https://github.com/Genymobile/scrcpy/blob/v$SCRCPY_VERSION/app/deps/ffmpeg.sh

SDL 3.4.12 (SDL3.dll)
  Licencia: zlib.
  Fuente: https://libsdl.org

libusb 1.0.30 (libusb-1.0.dll)
  Licencia: GNU LGPL v2.1 o posterior.
  Fuente: https://libusb.info

dav1d 1.5.3 (enlazado en FFmpeg)
  Licencia: BSD-2-Clause.
  Fuente: https://code.videolan.org/videolan/dav1d
NOTICES
# Lo abre un perito en Windows: fin de línea CRLF (mismo criterio que el .ini).
sed 's/$/\r/' "$OUT/tools/scrcpy/THIRD-PARTY-NOTICES.txt" > "$OUT/tools/scrcpy/THIRD-PARTY-NOTICES.txt.tmp" \
  && mv "$OUT/tools/scrcpy/THIRD-PARTY-NOTICES.txt.tmp" "$OUT/tools/scrcpy/THIRD-PARTY-NOTICES.txt"
echo "  OK  scrcpy $SCRCPY_VERSION (sin adb propio)"

echo "[5/7] Python embebido 3.11.9 + pymobiledevice3 (ruedas win_amd64)..."
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

echo "[6/7] ffmpeg (BtbN win64 gpl) y uxplay..."
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

echo "[7/7] Scripts del portátil y zip..."
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

# Chequeo del contenido del zip final (D9): lo que Tatana necesita para grabar Android está, y
# no viaja un segundo adb (el de scrcpy) que pelearía con el de platform-tools.
CONTENIDO="$(unzip -Z1 "$ZIP")"
FALTAN=()
for requerido in \
  tools/scrcpy/scrcpy.exe tools/scrcpy/scrcpy-server tools/scrcpy/SDL3.dll \
  tools/scrcpy/LICENSE.txt tools/scrcpy/THIRD-PARTY-NOTICES.txt \
  tools/platform-tools/adb.exe tools/ffmpeg/ffmpeg.exe Factum.Agent.exe; do
  grep -qxF "$requerido" <<<"$CONTENIDO" || FALTAN+=("$requerido")
done
if [[ ${#FALTAN[@]} -gt 0 ]]; then
  echo "ERROR: al zip de Tatana le falta: ${FALTAN[*]}" >&2
  exit 1
fi
SOBRAN="$(grep -E '^tools/scrcpy/(adb\.exe|AdbWin[^/]*\.dll)$' <<<"$CONTENIDO" || true)"
if [[ -n "$SOBRAN" ]]; then
  echo "ERROR: el zip de Tatana trae el adb propio de scrcpy (tiene que usar el de platform-tools): $(tr '\n' ' ' <<<"$SOBRAN")" >&2
  exit 1
fi
echo "  OK  contenido del zip verificado"
echo "  OK  $ZIP ($(du -h "$ZIP" | cut -f1))"
