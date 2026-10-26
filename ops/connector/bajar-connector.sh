#!/usr/bin/env bash
# Baja el APK del connector del GitHub Release fijado en android/connector/connector-lock.json y lo deja
# verificado en <dir-destino>/tools/connector/ (SDD extraccion-logica-android §7.6). Lo llaman los dos
# armadores de Tatana (deploy/linux/armar-tatana-linux.sh y deploy/windows/armar-tatana-portable.sh).
#
#   ops/connector/bajar-connector.sh <dir-destino>
#
# - Si el lock tiene "sha256": "" (todavía no hay release firmada), sale con 0 SIN hacer nada: el paquete
#   de Tatana sale sin el connector y el registro de llamadas queda deshabilitado.
# - Si no, baja el asset (gh si está logueado; si no, curl con Authorization: Bearer $GH_TOKEN contra la API
#   de releases, porque el repo es privado), verifica el SHA-256 contra el lock y lo copia a
#   <dir-destino>/tools/connector/factum-connector.apk + connector-lock.json.
set -euo pipefail

DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOCK="$DIR_SCRIPT/../../android/connector/connector-lock.json"
REPO="${CONNECTOR_REPO:-senkuch4n/Factum}"

DEST="${1:-}"
[[ -n "$DEST" ]] || { echo "Uso: $0 <dir-destino>" >&2; exit 2; }
[[ -f "$LOCK" ]] || { echo "ERROR: no está $LOCK." >&2; exit 1; }

# Lector mínimo de un campo string del JSON del lock (sin jq).
leer_campo() {
  sed -n "s/.*\"$1\"[[:space:]]*:[[:space:]]*\"\([^\"]*\)\".*/\1/p" "$LOCK" | head -1
}

SHA="$(leer_campo sha256)"
RELEASE_TAG="$(leer_campo release_tag)"
ASSET="$(leer_campo asset)"

if [[ -z "$SHA" ]]; then
  echo "  connector: lock sin hash (\"sha256\": \"\"): el paquete sale SIN la app auxiliar (registro de llamadas deshabilitado)."
  exit 0
fi
[[ "$SHA" =~ ^[0-9a-f]{64}$ ]] || { echo "ERROR: sha256 del lock no es 64 hex: '$SHA'." >&2; exit 1; }
[[ -n "$RELEASE_TAG" && -n "$ASSET" ]] || { echo "ERROR: el lock no tiene release_tag o asset." >&2; exit 1; }

sha256_de() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1
  else shasum -a 256 "$1" | cut -d' ' -f1; fi
}

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
APK_TMP="$TMP/$ASSET"

echo "  connector: bajando $ASSET del release $RELEASE_TAG de $REPO..."
if command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1; then
  gh release download "$RELEASE_TAG" --repo "$REPO" --pattern "$ASSET" --dir "$TMP" --clobber
else
  [[ -n "${GH_TOKEN:-}" ]] || { echo "ERROR: no hay gh logueado ni GH_TOKEN para bajar el connector de un repo privado." >&2; exit 1; }
  # API de releases: resolver el id del asset por nombre y bajar el binario.
  API="https://api.github.com/repos/$REPO/releases/tags/$RELEASE_TAG"
  META="$(curl -fsSL -H "Authorization: Bearer $GH_TOKEN" -H "Accept: application/vnd.github+json" "$API")"
  ASSET_URL="$(printf '%s' "$META" \
    | tr ',' '\n' \
    | grep -A3 "\"name\"[[:space:]]*:[[:space:]]*\"$ASSET\"" \
    | sed -n 's/.*"url"[[:space:]]*:[[:space:]]*"\(https:\/\/api.github.com\/repos\/[^"]*\/assets\/[0-9]*\)".*/\1/p' \
    | head -1)"
  # Fallback robusto: recorrer los assets buscando el que tiene el nombre pedido.
  if [[ -z "$ASSET_URL" ]]; then
    ASSET_URL="$(curl -fsSL -H "Authorization: Bearer $GH_TOKEN" -H "Accept: application/vnd.github+json" "$API/assets" \
      | tr '{' '\n' | grep "\"name\"[[:space:]]*:[[:space:]]*\"$ASSET\"" \
      | sed -n 's/.*"url"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -1)"
  fi
  [[ -n "$ASSET_URL" ]] || { echo "ERROR: no se encontró el asset '$ASSET' en $RELEASE_TAG." >&2; exit 1; }
  curl -fsSL -H "Authorization: Bearer $GH_TOKEN" -H "Accept: application/octet-stream" -o "$APK_TMP" "$ASSET_URL"
fi

[[ -f "$APK_TMP" ]] || { echo "ERROR: no se bajó $ASSET." >&2; exit 1; }
REAL="$(sha256_de "$APK_TMP")"
if [[ "$REAL" != "$SHA" ]]; then
  echo "ERROR: el SHA-256 del APK bajado ($REAL) no coincide con el lock ($SHA)." >&2
  exit 1
fi

mkdir -p "$DEST/tools/connector"
cp "$APK_TMP" "$DEST/tools/connector/factum-connector.apk"
cp "$LOCK" "$DEST/tools/connector/connector-lock.json"
echo "  OK  connector $RELEASE_TAG verificado → tools/connector/factum-connector.apk"
