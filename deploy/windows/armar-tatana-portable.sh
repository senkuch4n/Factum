#!/usr/bin/env bash
# Arma el portátil de Tatana para Windows (Tatana-Portable-vX.Y.Z-Windows.zip) en la Mac.
# Réplica local del job build-portable-win de .gitlab-ci.yml (el CI de GitLab no corre: el
# remoto real es GitHub). Lo llama armar-paquete.sh; también se puede usar suelto.
#
#   deploy/windows/armar-tatana-portable.sh --version X.Y.Z --src <árbol limpio> --salida <dir>
#       [--origenes "<o1>[,<o2>...]" [--client-url <url>]]
#
# Sin --origenes ni --client-url sale el portátil de la instalación local, igual que siempre (D15).
# Con --origenes (Factum en la nube, ver docs/despliegue-nube.md) se hornea Agent:AllowedOrigins =
# localhost + esos orígenes en el appsettings.json del zip (hornear-origenes-tatana.py, que se busca
# al lado de ESTE script y no en --src). --client-url hace que el portátil abra esa web al iniciar
# sesión; exige --origenes. El que arma el Tatana para la nube es deploy/cloud/armar-tatana-nube.sh.
#
# --src tiene que ser un árbol LIMPIO (git archive), nunca la copia de trabajo: así no entra un
# appsettings.Local.json ni un "Mock": true local. Igual se verifica dos veces.
# Requisitos: dotnet 10, python3 (con pip), curl, unzip, zip e internet (descarga ~300 MB).
# Funciona también en Linux (lo usa el CI .github/workflows/tatana-windows.yml): nada de BSD/macOS.
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
ORIGENES=""
CLIENT_URL=""
DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HORNEAR="$DIR_SCRIPT/hornear-origenes-tatana.py"

uso() {
  echo "Uso: $0 --version X.Y.Z --src <árbol limpio del repo> --salida <carpeta> [--origenes \"<o1>[,<o2>...]\" [--client-url <url>]]" >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="${2:-}"; shift 2 ;;
    --src) SRC="${2:-}"; shift 2 ;;
    --salida) SALIDA="${2:-}"; shift 2 ;;
    --origenes) ORIGENES="${2:-}"; [[ -n "$ORIGENES" ]] || { echo "ERROR: --origenes vacío." >&2; exit 2; }; shift 2 ;;
    --client-url) CLIENT_URL="${2:-}"; [[ -n "$CLIENT_URL" ]] || { echo "ERROR: --client-url vacío." >&2; exit 2; }; shift 2 ;;
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

# despliegue-nube §6.8: los orígenes se validan ANTES de la descarga de ~300 MB (un origen inválido
# haría que Tatana no arranque en la PC del perito).
ORIGENES_LISTA=()
if [[ -n "$ORIGENES" ]]; then
  [[ -f "$HORNEAR" ]] || { echo "ERROR: falta $HORNEAR." >&2; exit 1; }
  IFS=',' read -r -a ORIGENES_LISTA <<<"$ORIGENES"
  PRUEBA_ORIGENES="$(mktemp "${TMPDIR:-/tmp}/tatana-origenes.XXXXXX")"
  echo '{}' > "$PRUEBA_ORIGENES"
  if ! python3 "$HORNEAR" "$PRUEBA_ORIGENES" "${ORIGENES_LISTA[@]}" >/dev/null; then
    rm -f "$PRUEBA_ORIGENES"
    exit 2
  fi
  rm -f "$PRUEBA_ORIGENES"
fi
if [[ -n "$CLIENT_URL" ]]; then
  [[ -n "$ORIGENES" ]] || { echo "ERROR: --client-url exige --origenes (Tatana tiene que aceptar la web que abre)." >&2; exit 2; }
  [[ "$CLIENT_URL" =~ ^https?://[^[:space:]]+$ ]] || { echo "ERROR: --client-url tiene que ser una URL http(s), no '$CLIENT_URL'." >&2; exit 2; }
fi

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
if [[ ${#ORIGENES_LISTA[@]} -gt 0 ]]; then
  echo "  Horneando los orígenes de la nube en el appsettings.json del portátil..."
  python3 "$HORNEAR" "$OUT/appsettings.json" "${ORIGENES_LISTA[@]}"
  verificar_mock "$OUT/appsettings.json"
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

echo "[5/7] Python embebido 3.11.9 + pymobiledevice3 10.7.4 (lock de Windows)..."
# SDD ios-herramientas-windows §7.3: lock con hashes (packaging/portable/ios-win/), con las
# dependencias que solo aplican en Windows (pywin32, av, lzfse…), instalado en Lib\site-packages
# para que el `import site` del ._pth procese pywin32.pth. Sin lock, pip retrocedía en silencio a
# pymobiledevice3 1.0.0 (hexdump solo existe como sdist) y sin pywin32 (A2, A3).
IOS_WIN="$SRC/packaging/portable/ios-win"
for f in requirements-win.lock hexdump.txt; do
  [[ -f "$IOS_WIN/$f" ]] || { echo "ERROR: falta $IOS_WIN/$f." >&2; exit 1; }
done
curl -fsSL -o "$TMP/python-embed.zip" https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip
mkdir -p "$OUT/tools/python-embed"
unzip -q "$TMP/python-embed.zip" -d "$OUT/tools/python-embed"
# Habilitar site-packages en el embebido (viene comentado). sed portable (BSD/GNU).
PTH="$OUT/tools/python-embed/python311._pth"
sed 's/^#import site/import site/' "$PTH" > "$PTH.tmp" && mv "$PTH.tmp" "$PTH"
grep -q '^import site' "$PTH" || { echo "ERROR: $PTH no quedó con 'import site'." >&2; exit 1; }
# hexdump: wheel puro construido desde el sdist verificado por hash.
python3 -m pip wheel --quiet --disable-pip-version-check --no-cache-dir --no-deps --require-hashes \
  -r "$IOS_WIN/hexdump.txt" --wheel-dir "$TMP/wheelhouse"
SITE="$OUT/tools/python-embed/Lib/site-packages"
mkdir -p "$SITE"
# --no-compile: sin .pyc del Python de esta máquina (no sirven para el 3.11 de Windows).
python3 -m pip install --quiet --disable-pip-version-check --no-cache-dir --no-compile --no-deps --require-hashes \
  --only-binary=:all: --platform win_amd64 --implementation cp --python-version 311 \
  --target "$SITE" -r "$IOS_WIN/requirements-win.lock"
HEXDUMP_WHL=("$TMP"/wheelhouse/hexdump-3.3-*.whl)
[[ -f "${HEXDUMP_WHL[0]}" ]] || { echo "ERROR: no se construyó el wheel de hexdump." >&2; exit 1; }
python3 -m pip install --quiet --disable-pip-version-check --no-cache-dir --no-compile --no-deps --no-index \
  --target "$SITE" "${HEXDUMP_WHL[@]}"
# Trazabilidad forense: con qué versiones exactas se capturó.
cp "$IOS_WIN/requirements-win.lock" "$OUT/tools/python-embed/TATANA-PYTHON-LOCK.txt"
cat > "$OUT/tools/python-embed/THIRD-PARTY-NOTICES.txt" <<'NOTICES'
Avisos de terceros - tools/python-embed de Tatana
=================================================

Python 3.11.9 (distribucion "embeddable" para Windows x64)
  Copyright (c) Python Software Foundation.
  Licencia: PSF License Agreement (texto completo en LICENSE.txt, en esta carpeta).
  Fuente: https://www.python.org/downloads/release/python-3119/

pymobiledevice3 10.7.4
  Copyright (c) doronz88 y colaboradores.
  Licencia: GNU General Public License v3.0 o posterior (GPL-3.0-or-later).
  Fuente exacta de esta version:
    https://github.com/doronz88/pymobiledevice3/tree/v10.7.4
    https://pypi.org/project/pymobiledevice3/10.7.4/ (sdist)
  Se distribuye sin modificaciones (wheel pymobiledevice3-10.7.4-py3-none-any.whl).

Las demas bibliotecas de Python y sus versiones exactas estan en TATANA-PYTHON-LOCK.txt (en
esta carpeta); hexdump 3.3 se construye desde su sdist de PyPI. Sus licencias estan en
Lib\site-packages\*.dist-info.
NOTICES
# Lo abre un perito en Windows: fin de línea CRLF.
for f in THIRD-PARTY-NOTICES.txt TATANA-PYTHON-LOCK.txt; do
  sed 's/$/\r/' "$OUT/tools/python-embed/$f" > "$OUT/tools/python-embed/$f.tmp" \
    && mv "$OUT/tools/python-embed/$f.tmp" "$OUT/tools/python-embed/$f"
done
echo "  OK  pymobiledevice3 10.7.4 (lock de Windows)"

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
  echo "  AVISO: el portátil de Windows sale sin AirPlay (decisión D1 de ios-herramientas-windows); ver packaging/windows-uxplay-build.md"
fi

echo "[7/7] Scripts del portátil y zip..."
cp "$SRC"/packaging/portable/*.bat "$SRC"/packaging/portable/*.ps1 "$SRC"/packaging/portable/*.ini "$OUT/"
# En la copia (el .ini del repo no cambia, lo usa el CI): sin navegador al iniciar sesión
# (DT4: Factum se abre con el acceso del Escritorio) y sin auto-actualización (no hay
# endpoint de releases en la instalación local).
if [[ -n "$CLIENT_URL" ]]; then
  # despliegue-nube §6.8: Tatana para Factum en la nube. UPDATE_URL sigue vacío (D8b).
  cat > "$OUT/tatana-portable.ini" <<INI
; Config de Tatana Portable para Factum en la nube.
; CLIENT_URL = la web de Factum que se abre al iniciar sesion.
; UPDATE_URL vacio = sin auto-actualizacion (se actualiza descargando el zip nuevo).
CLIENT_URL=$CLIENT_URL
UPDATE_URL=
AGENT_PORT=8765
INI
else
  cat > "$OUT/tatana-portable.ini" <<'INI'
; Config de Tatana Portable para la instalacion local de Factum (Docker en esta PC).
; CLIENT_URL vacio = no abrir el navegador al iniciar sesion (Factum se abre con el
; acceso "Factum" del Escritorio). UPDATE_URL vacio = sin auto-actualizacion.
CLIENT_URL=
UPDATE_URL=
AGENT_PORT=8765
INI
fi
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
  tools/platform-tools/adb.exe tools/ffmpeg/ffmpeg.exe Factum.Agent.exe \
  tools/python-embed/python.exe tools/python-embed/python311._pth \
  tools/python-embed/Lib/site-packages/pymobiledevice3/__init__.py \
  tools/python-embed/Lib/site-packages/win32/win32security.pyd \
  tools/python-embed/Lib/site-packages/pywin32.pth \
  tools/python-embed/TATANA-PYTHON-LOCK.txt tools/python-embed/THIRD-PARTY-NOTICES.txt; do
  grep -qxF "$requerido" <<<"$CONTENIDO" || FALTAN+=("$requerido")
done
if [[ ${#FALTAN[@]} -gt 0 ]]; then
  echo "ERROR: al zip de Tatana le falta: ${FALTAN[*]}" >&2
  exit 1
fi
# ios-herramientas-windows §7.3: el pymobiledevice3 del zip es el probado (no una versión vieja).
PMD_VERSION="$(unzip -p "$ZIP" 'tools/python-embed/Lib/site-packages/pymobiledevice3-10.7.4.dist-info/METADATA' 2>/dev/null \
  | sed -n 's/^Version: //p' | tr -d '\r' | head -1 || true)"
if [[ "$PMD_VERSION" != "10.7.4" ]]; then
  echo "ERROR: el zip de Tatana no trae pymobiledevice3 10.7.4 (METADATA dice '${PMD_VERSION:-nada}')." >&2
  exit 1
fi
SOBRAN="$(grep -E '^tools/scrcpy/(adb\.exe|AdbWin[^/]*\.dll)$' <<<"$CONTENIDO" || true)"
if [[ -n "$SOBRAN" ]]; then
  echo "ERROR: el zip de Tatana trae el adb propio de scrcpy (tiene que usar el de platform-tools): $(tr '\n' ' ' <<<"$SOBRAN")" >&2
  exit 1
fi
# despliegue-nube §6.8: con --origenes, el appsettings.json DEL ZIP trae exactamente la lista horneada.
if [[ ${#ORIGENES_LISTA[@]} -gt 0 ]]; then
  ESPERADO="$TMP/origenes-esperados.json"
  echo '{}' > "$ESPERADO"
  python3 "$HORNEAR" "$ESPERADO" "${ORIGENES_LISTA[@]}" >/dev/null
  unzip -p "$ZIP" appsettings.json > "$TMP/appsettings-del-zip.json"
  python3 - "$ESPERADO" "$TMP/appsettings-del-zip.json" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8-sig") as f:
    esperado = json.load(f)["Agent"]["AllowedOrigins"]
with open(sys.argv[2], encoding="utf-8-sig") as f:
    cfg = json.load(f)
real = cfg.get("Agent", {}).get("AllowedOrigins")
if real != esperado:
    sys.exit(f"ERROR: el appsettings.json del zip trae Agent.AllowedOrigins = {real!r}; se esperaba {esperado!r}.")
if cfg.get("Agent", {}).get("Mock", False) is not False:
    sys.exit("ERROR: el appsettings.json del zip tiene Agent.Mock distinto de false.")
print("  OK  Agent.AllowedOrigins del zip: " + ", ".join(real))
PY
fi
echo "  OK  contenido del zip verificado"
echo "  OK  $ZIP ($(du -h "$ZIP" | cut -f1))"
