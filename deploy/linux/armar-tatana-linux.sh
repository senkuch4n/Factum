#!/usr/bin/env bash
# Arma el payload del agente de Tatana para Linux x86-64 (Tatana-Agent-vX.Y.Z-linux-x64.tar.gz): Factum.Agent
# self-contained + tools/ (adb, scrcpy, ffmpeg y Python portátil con pymobiledevice3), con versiones FIJAS y
# verificadas por SHA-256 (SDD tatana-linux §4 "Versiones fijas", §6.1). Es lo que agent-ui empaqueta dentro del
# AppImage (resources/agent/). Lo llaman .github/workflows/tatana-linux.yml (humo) y tatana-release.yml (release);
# hermano de deploy/windows/armar-tatana-portable.sh.
#
#   deploy/linux/armar-tatana-linux.sh --version X.Y.Z --src <árbol limpio> --salida <dir> [--origenes "<o1>[,<o2>...]"]
#     → <dir>/Tatana-Agent-vX.Y.Z-linux-x64.tar.gz
#
# --src tiene que ser un árbol LIMPIO (git archive), nunca la copia de trabajo: así no entra un
# appsettings.Local.json ni un "Mock": true local. Igual se verifica dos veces.
# Con --origenes se hornea Agent:AllowedOrigins = localhost + esos orígenes (../windows/hornear-origenes-tatana.py,
# que se busca relativo a ESTE script, no a --src).
# Requisitos: dotnet 10, python3 (con pip), curl, unzip, tar y xz, e internet (~250 MB). Corre en Linux (CI) y en
# la Mac: nada de opciones exclusivas de GNU ni de BSD. Si la máquina es Linux x86-64, además prueba el ffmpeg.
# El payload viaja como .tar.gz (no zip) para conservar los bits +x y los symlinks del Python portátil (D-T24).
set -euo pipefail

# ── Versiones fijas (§4). Para actualizar una: cambiar versión, URL y SHA-256 juntas, y correr la guarda de
#    glibc (paso 8): el requisito publicado es glibc ≥ 2.35 (E5) y no puede subir sin que se decida. ─────────
PT_VERSION="r37.0.1"
PT_URL="https://dl.google.com/android/repository/platform-tools_${PT_VERSION}-linux.zip"
PT_SHA256="d230f13842f60f782a8645f9c813f8f845bf36089ea7289f28c48f17979313f1"

SCRCPY_VERSION="4.1"
SCRCPY_URL="https://github.com/Genymobile/scrcpy/releases/download/v${SCRCPY_VERSION}/scrcpy-linux-x86_64-v${SCRCPY_VERSION}.tar.gz"
SCRCPY_SHA256="ad56ae8bfeedf41e824945c11dbf55fcb092b3e615b9b486f48a50e30d389635"

# BtbN conserva los builds de fin de mes; los diarios se borran.
FFMPEG_TAG="autobuild-2026-09-30-13-08"
FFMPEG_NAME="ffmpeg-n8.1.3-9-g29e619e767-linux64-gpl-8.1"
FFMPEG_URL="https://github.com/BtbN/FFmpeg-Builds/releases/download/${FFMPEG_TAG}/${FFMPEG_NAME}.tar.xz"
FFMPEG_SHA256="97ce978979194b5cf7e06a5e68020dbdaa7a4f3294c5452b6a1bc347100dbb79"

PYTHON_VERSION="3.11.17"
PBS_RELEASE="20261013"
PYTHON_URL="https://github.com/astral-sh/python-build-standalone/releases/download/${PBS_RELEASE}/cpython-${PYTHON_VERSION}+${PBS_RELEASE}-x86_64-unknown-linux-gnu-install_only_stripped.tar.gz"
PYTHON_SHA256="3b8e5a248bcc351265ffe68f5e457668a2e09c99a47f81f658988c7d3d107522"

PYMOBILEDEVICE3_VERSION="10.7.4"
GLIBC_MAXIMA="2.35"

VERSION=""
SRC=""
SALIDA=""
ORIGENES=""
DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HORNEAR="$DIR_SCRIPT/../windows/hornear-origenes-tatana.py"
GLIBC_MAXIMO="$DIR_SCRIPT/../../packaging/linux/glibc-maximo.py"

uso() {
  echo "Uso: $0 --version X.Y.Z --src <árbol limpio del repo> --salida <carpeta> [--origenes \"<o1>[,<o2>...]\"]" >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="${2:-}"; shift 2 ;;
    --src) SRC="${2:-}"; shift 2 ;;
    --salida) SALIDA="${2:-}"; shift 2 ;;
    --origenes) ORIGENES="${2:-}"; [[ -n "$ORIGENES" ]] || { echo "ERROR: --origenes vacío." >&2; exit 2; }; shift 2 ;;
    -h|--help) uso ;;
    *) echo "Argumento desconocido: $1" >&2; uso ;;
  esac
done

[[ -n "$VERSION" && -n "$SRC" && -n "$SALIDA" ]] || uso
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || { echo "ERROR: --version tiene que ser semver (X.Y.Z), no '$VERSION'." >&2; exit 2; }
[[ -f "$SRC/server/src/Factum.Agent/Factum.Agent.csproj" ]] || { echo "ERROR: '$SRC' no parece el repo de Factum (falta server/src/Factum.Agent)." >&2; exit 2; }
SRC="$(cd "$SRC" && pwd)"

for herramienta in dotnet python3 curl unzip tar xz; do
  command -v "$herramienta" >/dev/null 2>&1 || { echo "ERROR: falta '$herramienta' en esta máquina." >&2; exit 1; }
done
[[ -f "$GLIBC_MAXIMO" ]] || { echo "ERROR: falta $GLIBC_MAXIMO." >&2; exit 1; }

# SHA-256 con lo que haya (sha256sum en Linux, shasum en la Mac).
sha256_de() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1
  else shasum -a 256 "$1" | cut -d' ' -f1
  fi
}

# Descarga $1 a $2 y exige el SHA-256 $3.
bajar() {
  local url="$1" destino="$2" esperado="$3" real
  curl -fsSL --retry 3 -o "$destino" "$url"
  real="$(sha256_de "$destino")"
  if [[ "$real" != "$esperado" ]]; then
    echo "ERROR: el SHA-256 de $(basename "$destino") no coincide (¿descarga corrupta o release reemplazado?)." >&2
    echo "       esperado $esperado" >&2
    echo "       real     $real" >&2
    exit 1
  fi
}

# Los orígenes se validan ANTES de la descarga (un origen inválido haría que Tatana no arranque).
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

mkdir -p "$SALIDA"
SALIDA="$(cd "$SALIDA" && pwd)"
TMP="$(mktemp -d "${TMPDIR:-/tmp}/tatana-linux.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT
OUT="$TMP/agent-linux-output"
IOS_LINUX="$SRC/packaging/linux/ios-linux"
IOS_WIN="$SRC/packaging/portable/ios-win"

# Guarda de Mock: "Agent": { "Mock": false } en el appsettings.json que viaja al lado del ejecutable.
verificar_mock() {
  python3 - "$1" <<'PY'
import json, sys
ruta = sys.argv[1]
with open(ruta, encoding="utf-8-sig") as f:
    cfg = json.load(f)
mock = cfg.get("Agent", {}).get("Mock", False)
if mock is not False:
    sys.exit(f"ERROR: {ruta} tiene Agent.Mock = {mock!r}; Tatana tiene que salir con Mock = false.")
print(f"  OK  Agent.Mock = false en {ruta}")
PY
}

echo "[1/8] Verificando Mock en la fuente..."
verificar_mock "$SRC/server/src/Factum.Agent/appsettings.json"
[[ -f "$IOS_LINUX/requirements-linux.lock" ]] || { echo "ERROR: falta $IOS_LINUX/requirements-linux.lock." >&2; exit 1; }
[[ -f "$IOS_WIN/hexdump.txt" ]] || { echo "ERROR: falta $IOS_WIN/hexdump.txt." >&2; exit 1; }

echo "[2/8] Publicando Factum.Agent para linux-x64 (self-contained, single-file)..."
# D-T24: igual que en Windows. Single-file (lo fija el .csproj) con los .so nativos al lado
# (IncludeNativeLibrariesForSelfExtract=false): nada se extrae a disco y anda desde el montaje de solo lectura.
dotnet publish "$SRC/server/src/Factum.Agent/Factum.Agent.csproj" \
  -c Release -r linux-x64 --self-contained true -p:Version="$VERSION" \
  -p:IncludeNativeLibrariesForSelfExtract=false -o "$OUT" --nologo -v quiet
[[ -f "$OUT/Factum.Agent" ]] || { echo "ERROR: no se generó Factum.Agent." >&2; exit 1; }
[[ -f "$OUT/appsettings.json" ]] || { echo "ERROR: el publish no trae appsettings.json." >&2; exit 1; }
verificar_mock "$OUT/appsettings.json"
if [[ -e "$OUT/appsettings.Local.json" ]]; then
  echo "ERROR: el publish trae appsettings.Local.json (config local de desarrollo): no puede viajar." >&2
  exit 1
fi
# Los .pdb no hacen falta en la PC del perito.
find "$OUT" -maxdepth 1 -name '*.pdb' -delete
if [[ ${#ORIGENES_LISTA[@]} -gt 0 ]]; then
  echo "  Horneando los orígenes de la nube en el appsettings.json..."
  python3 "$HORNEAR" "$OUT/appsettings.json" "${ORIGENES_LISTA[@]}"
  verificar_mock "$OUT/appsettings.json"
fi

echo "[3/8] adb (Android platform-tools $PT_VERSION para Linux)..."
mkdir -p "$OUT/tools"
bajar "$PT_URL" "$TMP/platform-tools.zip" "$PT_SHA256"
unzip -q "$TMP/platform-tools.zip" -d "$TMP/pt"
[[ -f "$TMP/pt/platform-tools/adb" ]] || { echo "ERROR: el zip de platform-tools no trae adb." >&2; exit 1; }
mv "$TMP/pt/platform-tools" "$OUT/tools/platform-tools"   # todo el directorio, con lib64/
echo "  OK  platform-tools $PT_VERSION"

echo "[4/8] scrcpy $SCRCPY_VERSION (Genymobile, Apache-2.0)..."
bajar "$SCRCPY_URL" "$TMP/scrcpy-linux.tar.gz" "$SCRCPY_SHA256"
mkdir -p "$TMP/scrcpy"
tar -xzf "$TMP/scrcpy-linux.tar.gz" -C "$TMP/scrcpy"
SCRCPY_DIR="$TMP/scrcpy/scrcpy-linux-x86_64-v$SCRCPY_VERSION"
[[ -f "$SCRCPY_DIR/scrcpy" ]] || { echo "ERROR: el tar de scrcpy no trae scrcpy-linux-x86_64-v$SCRCPY_VERSION/scrcpy." >&2; exit 1; }
mkdir -p "$OUT/tools/scrcpy"
cp -R "$SCRCPY_DIR/." "$OUT/tools/scrcpy/"
# Sin el adb propio de scrcpy: Tatana usa el de tools/platform-tools (un solo servidor adb).
rm -f "$OUT/tools/scrcpy/adb"
# El tar de Linux trae la licencia como LICENSE (se exige en el chequeo final).
cat > "$OUT/tools/scrcpy/THIRD-PARTY-NOTICES.txt" <<NOTICES
Avisos de terceros - tools/scrcpy de Tatana para Linux
======================================================

scrcpy $SCRCPY_VERSION
  Copyright (C) Genymobile / Romain Vimont.
  Licencia: Apache License 2.0 (texto completo en LICENSE, en esta carpeta).
  Fuente: https://github.com/Genymobile/scrcpy
  Archivo distribuido: scrcpy-linux-x86_64-v$SCRCPY_VERSION.tar.gz
  SHA-256 del tar: $SCRCPY_SHA256
  Modificacion: se quito el adb del tar original. Tatana usa el de tools/platform-tools.

Bibliotecas enlazadas estaticamente en el ejecutable scrcpy (versiones de app/deps/*.sh del tag v$SCRCPY_VERSION):

FFmpeg 8.1.2
  Licencia: GNU LGPL v2.1 o posterior.
  Fuente: https://ffmpeg.org (codigo fuente de la release 8.1.2).
  Configuracion de build: https://github.com/Genymobile/scrcpy/blob/v$SCRCPY_VERSION/app/deps/ffmpeg.sh

SDL 3.4.12
  Licencia: zlib.
  Fuente: https://libsdl.org

libusb 1.0.30
  Licencia: GNU LGPL v2.1 o posterior.
  Fuente: https://libusb.info

dav1d 1.5.3 (enlazado en FFmpeg)
  Licencia: BSD-2-Clause.
  Fuente: https://code.videolan.org/videolan/dav1d
NOTICES
echo "  OK  scrcpy $SCRCPY_VERSION (sin adb propio)"

echo "[5/8] Python portátil $PYTHON_VERSION (python-build-standalone $PBS_RELEASE) + pymobiledevice3 $PYMOBILEDEVICE3_VERSION..."
bajar "$PYTHON_URL" "$TMP/python.tar.gz" "$PYTHON_SHA256"
mkdir -p "$TMP/pbs"
tar -xzf "$TMP/python.tar.gz" -C "$TMP/pbs"
[[ -d "$TMP/pbs/python/bin" ]] || { echo "ERROR: el tar de Python no trae python/bin." >&2; exit 1; }
mkdir -p "$OUT/tools/python-embed"
cp -R "$TMP/pbs/python/." "$OUT/tools/python-embed/"
SITE="$OUT/tools/python-embed/lib/python3.11/site-packages"
[[ -d "$SITE" ]] || { echo "ERROR: el Python portátil no trae lib/python3.11/site-packages." >&2; exit 1; }
# hexdump: wheel puro construido desde el sdist verificado por hash (el mismo de Windows).
python3 -m pip wheel --quiet --disable-pip-version-check --no-cache-dir --no-deps --require-hashes \
  -r "$IOS_WIN/hexdump.txt" --wheel-dir "$TMP/wheelhouse"
# --no-compile: sin .pyc del Python de esta máquina. Solo wheels manylinux ≤ 2.28 (E4).
python3 -m pip install --quiet --disable-pip-version-check --no-cache-dir --no-compile --no-deps --require-hashes \
  --only-binary=:all: --platform manylinux2014_x86_64 --platform manylinux_2_17_x86_64 \
  --platform manylinux_2_28_x86_64 --platform linux_x86_64 --implementation cp --python-version 311 \
  --target "$SITE" -r "$IOS_LINUX/requirements-linux.lock"
HEXDUMP_WHL=("$TMP"/wheelhouse/hexdump-3.3-*.whl)
[[ -f "${HEXDUMP_WHL[0]}" ]] || { echo "ERROR: no se construyó el wheel de hexdump." >&2; exit 1; }
python3 -m pip install --quiet --disable-pip-version-check --no-cache-dir --no-compile --no-deps --no-index \
  --target "$SITE" "${HEXDUMP_WHL[@]}"
# pip --target deja bin/ con scripts de consola que apuntan al Python de ESTA máquina: no sirven y se sacan.
rm -rf "${SITE:?}/bin"
# Trazabilidad forense: con qué versiones exactas se capturó.
cp "$IOS_LINUX/requirements-linux.lock" "$OUT/tools/python-embed/TATANA-PYTHON-LOCK.txt"
cat > "$OUT/tools/python-embed/THIRD-PARTY-NOTICES.txt" <<NOTICES
Avisos de terceros - tools/python-embed de Tatana para Linux
============================================================

Python $PYTHON_VERSION (python-build-standalone $PBS_RELEASE, x86_64-unknown-linux-gnu, install_only_stripped)
  Copyright (c) Python Software Foundation.
  Licencia: PSF License Agreement (lib/python3.11/LICENSE.txt). Las bibliotecas que trae python-build-standalone
  (OpenSSL, SQLite, libffi, zlib, xz, bzip2, ncurses, readline/libedit, Tcl/Tk, ...) tienen sus licencias en
  https://github.com/astral-sh/python-build-standalone (docs "Licensing").
  Fuente: https://github.com/astral-sh/python-build-standalone/releases/tag/$PBS_RELEASE
  SHA-256 del tar: $PYTHON_SHA256

pymobiledevice3 $PYMOBILEDEVICE3_VERSION
  Copyright (c) doronz88 y colaboradores.
  Licencia: GNU General Public License v3.0 o posterior (GPL-3.0-or-later).
  Fuente exacta de esta version:
    https://github.com/doronz88/pymobiledevice3/tree/v$PYMOBILEDEVICE3_VERSION
    https://pypi.org/project/pymobiledevice3/$PYMOBILEDEVICE3_VERSION/ (sdist)
  Se distribuye sin modificaciones (wheel pymobiledevice3-$PYMOBILEDEVICE3_VERSION-py3-none-any.whl).

Las demas bibliotecas de Python y sus versiones exactas estan en TATANA-PYTHON-LOCK.txt (en esta carpeta);
hexdump 3.3 se construye desde su sdist de PyPI. Sus licencias estan en lib/python3.11/site-packages/*.dist-info.
NOTICES
echo "  OK  pymobiledevice3 $PYMOBILEDEVICE3_VERSION (lock de Linux)"

echo "[6/8] ffmpeg (BtbN linux64 gpl, rama 8.1)..."
bajar "$FFMPEG_URL" "$TMP/ffmpeg.tar.xz" "$FFMPEG_SHA256"
mkdir -p "$TMP/ffmpeg" "$OUT/tools/ffmpeg"
xz -dc "$TMP/ffmpeg.tar.xz" | tar -xf - -C "$TMP/ffmpeg"
[[ -f "$TMP/ffmpeg/$FFMPEG_NAME/bin/ffmpeg" ]] || { echo "ERROR: el tar de ffmpeg no trae $FFMPEG_NAME/bin/ffmpeg." >&2; exit 1; }
cp "$TMP/ffmpeg/$FFMPEG_NAME/bin/ffmpeg" "$OUT/tools/ffmpeg/ffmpeg"
FFMPEG_COMMIT="${FFMPEG_NAME#ffmpeg-n8.1.3-9-g}"
FFMPEG_COMMIT="${FFMPEG_COMMIT%%-*}"
cat > "$OUT/tools/ffmpeg/THIRD-PARTY-NOTICES.txt" <<NOTICES
Avisos de terceros - tools/ffmpeg de Tatana para Linux
======================================================

FFmpeg n8.1.3-9-g$FFMPEG_COMMIT (build "linux64-gpl-8.1" de BtbN/FFmpeg-Builds)
  Licencia: GNU General Public License v3.0 o posterior (el build GPL incluye libx264 y otras bibliotecas GPL).
  Codigo fuente de FFmpeg: https://git.ffmpeg.org/ffmpeg.git, commit $FFMPEG_COMMIT (rama release/8.1)
    https://github.com/FFmpeg/FFmpeg/commit/$FFMPEG_COMMIT
  Scripts de build y fuentes de las bibliotecas: https://github.com/BtbN/FFmpeg-Builds/releases/tag/$FFMPEG_TAG
  Archivo distribuido: $FFMPEG_NAME.tar.xz (solo bin/ffmpeg)
  SHA-256 del tar: $FFMPEG_SHA256
NOTICES
if [[ "$(uname -s)" == "Linux" && "$(uname -m)" == "x86_64" ]]; then
  DISPOSITIVOS="$("$OUT/tools/ffmpeg/ffmpeg" -hide_banner -devices 2>&1 || true)"
  for d in pulse v4l2; do
    grep -Eq "^ *[DE.]{1,2} +([a-z0-9]+,)*$d( |,|$)" <<<"$DISPOSITIVOS" \
      || { echo "ERROR: el ffmpeg empaquetado no trae el dispositivo '$d' (E11)." >&2; exit 1; }
  done
  echo "  OK  ffmpeg con pulse y v4l2"
else
  echo "  AVISO: esta máquina no es Linux x86-64: no se prueban los dispositivos pulse/v4l2 de ffmpeg (lo hace el CI)."
fi

# App auxiliar del registro de llamadas (extraccion-logica-android §7.6): entra en tools/connector/ solo si
# connector-lock.json ya tiene hash. Con el lock vacío no hace nada y el paquete sale sin el connector.
# GH_TOKEN (en CI: github.token) permite bajar el release del repo privado. Reusa el payload de Docker.
echo "[6.5/8] App auxiliar (connector)..."
"$DIR_SCRIPT/../../ops/connector/bajar-connector.sh" "$OUT"

echo "[7/8] Permisos, version.txt y tar..."
printf '%s\n' "$VERSION" > "$OUT/version.txt"
chmod 755 "$OUT/Factum.Agent" "$OUT/tools/platform-tools/adb" "$OUT/tools/scrcpy/scrcpy" "$OUT/tools/ffmpeg/ffmpeg"
for p in "$OUT"/tools/python-embed/bin/python3*; do
  [[ -L "$p" ]] || chmod 755 "$p"
done
TAR="$SALIDA/Tatana-Agent-v$VERSION-linux-x64.tar.gz"
rm -f "$TAR"
tar --no-xattrs -C "$OUT" -czf "$TAR" .

echo "[8/8] Chequeo del tar final..."
CONTENIDO="$(tar -tzf "$TAR" | sed 's#^\./##' | sed '/^$/d')"
LISTADO="$(tar -tvzf "$TAR")"
FALTAN=()
for requerido in \
  Factum.Agent appsettings.json version.txt tools/platform-tools/adb \
  tools/scrcpy/scrcpy tools/scrcpy/scrcpy-server tools/scrcpy/LICENSE tools/scrcpy/THIRD-PARTY-NOTICES.txt \
  tools/ffmpeg/ffmpeg tools/ffmpeg/THIRD-PARTY-NOTICES.txt \
  tools/python-embed/bin/python3 \
  tools/python-embed/lib/python3.11/site-packages/pymobiledevice3/__init__.py \
  "tools/python-embed/lib/python3.11/site-packages/pymobiledevice3-$PYMOBILEDEVICE3_VERSION.dist-info/METADATA" \
  tools/python-embed/TATANA-PYTHON-LOCK.txt tools/python-embed/THIRD-PARTY-NOTICES.txt; do
  grep -qxF "$requerido" <<<"$CONTENIDO" || FALTAN+=("$requerido")
done
if [[ ${#FALTAN[@]} -gt 0 ]]; then
  echo "ERROR: al tar de Tatana le falta: ${FALTAN[*]}" >&2
  exit 1
fi
if grep -qxF "tools/scrcpy/adb" <<<"$CONTENIDO"; then
  echo "ERROR: el tar trae el adb propio de scrcpy (tiene que usar el de platform-tools)." >&2
  exit 1
fi
if grep -qxF "appsettings.Local.json" <<<"$CONTENIDO"; then
  echo "ERROR: el tar trae appsettings.Local.json." >&2
  exit 1
fi
# Bits de ejecución: cada ejecutable tiene que tener 'x' para el dueño en el listado del tar.
for exe in Factum.Agent tools/platform-tools/adb tools/scrcpy/scrcpy tools/ffmpeg/ffmpeg; do
  linea="$(awk -v n="./$exe" '$NF == n' <<<"$LISTADO" | head -1)"
  [[ "$linea" =~ ^-..x ]] || { echo "ERROR: $exe no tiene permiso de ejecución en el tar ('${linea:-no está}')." >&2; exit 1; }
done
# python3 es un symlink a python3.11 en python-build-standalone: se exige el destino ejecutable.
PY_REAL="$(awk '$NF ~ /^\.\/tools\/python-embed\/bin\/python3\.11$/' <<<"$LISTADO" | head -1)"
[[ "$PY_REAL" =~ ^-..x ]] || { echo "ERROR: tools/python-embed/bin/python3.11 no está o no es ejecutable en el tar." >&2; exit 1; }
PMD_VERSION="$(tar -xzOf "$TAR" "./tools/python-embed/lib/python3.11/site-packages/pymobiledevice3-$PYMOBILEDEVICE3_VERSION.dist-info/METADATA" 2>/dev/null \
  | sed -n 's/^Version: //p' | head -1 || true)"
if [[ "$PMD_VERSION" != "$PYMOBILEDEVICE3_VERSION" ]]; then
  echo "ERROR: el tar no trae pymobiledevice3 $PYMOBILEDEVICE3_VERSION (METADATA dice '${PMD_VERSION:-nada}')." >&2
  exit 1
fi
tar -xzOf "$TAR" ./appsettings.json > "$TMP/appsettings-del-tar.json"
verificar_mock "$TMP/appsettings-del-tar.json"
if [[ ${#ORIGENES_LISTA[@]} -gt 0 ]]; then
  ESPERADO="$TMP/origenes-esperados.json"
  echo '{}' > "$ESPERADO"
  python3 "$HORNEAR" "$ESPERADO" "${ORIGENES_LISTA[@]}" >/dev/null
  python3 - "$ESPERADO" "$TMP/appsettings-del-tar.json" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8-sig") as f:
    esperado = json.load(f)["Agent"]["AllowedOrigins"]
with open(sys.argv[2], encoding="utf-8-sig") as f:
    cfg = json.load(f)
real = cfg.get("Agent", {}).get("AllowedOrigins")
if real != esperado:
    sys.exit(f"ERROR: el appsettings.json del tar trae Agent.AllowedOrigins = {real!r}; se esperaba {esperado!r}.")
print("  OK  Agent.AllowedOrigins del tar: " + ", ".join(real))
PY
fi
# Guarda de glibc (E5): ningún ELF del paquete puede pedir más que la glibc mínima publicada.
python3 -I "$GLIBC_MAXIMO" --max "$GLIBC_MAXIMA" "$OUT"
echo "  OK  contenido del tar verificado"
echo "  OK  $TAR ($(du -h "$TAR" | cut -f1))"
