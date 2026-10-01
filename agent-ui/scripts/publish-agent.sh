#!/usr/bin/env bash
# Compila Factum.Agent como self-contained y lo deja en resources/agent/
# Las librerías externas (adb, pymobiledevice3) las instala el fiscal desde la UI de Tatana.
#
# Uso:
#   ./scripts/publish-agent.sh              → detecta plataforma actual
#   ./scripts/publish-agent.sh win-x64      → cross-compile para Windows
#   ./scripts/publish-agent.sh osx-arm64    → para Mac Apple Silicon

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
AGENT_SRC="$SCRIPT_DIR/../../server/src/Factum.Agent"
OUT_DIR="$SCRIPT_DIR/../resources/agent"

# ── Detectar RID ──────────────────────────────────────────────────────────────
if [ -n "$1" ]; then
  RID="$1"
else
  case "$(uname -s)" in
    Darwin) [ "$(uname -m)" = "arm64" ] && RID="osx-arm64" || RID="osx-x64" ;;
    Linux)  RID="linux-x64" ;;
    MINGW*|MSYS*|CYGWIN*) RID="win-x64" ;;
    *) echo "Plataforma no reconocida. Pasá el RID como argumento." && exit 1 ;;
  esac
fi

echo "→ Publicando Factum.Agent para $RID..."
echo "  Fuente : $AGENT_SRC"
echo "  Destino: $OUT_DIR"

rm -rf "$OUT_DIR"
mkdir -p "$OUT_DIR"

dotnet publish "$AGENT_SRC" \
  --runtime "$RID" \
  --self-contained true \
  --configuration Release \
  --output "$OUT_DIR" \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true

# Permisos en macOS/Linux
if [ "$RID" != "win-x64" ]; then
  chmod +x "$OUT_DIR/Factum.Agent" 2>/dev/null || true
fi

echo ""
echo "✓ Listo. Contenido de resources/agent/:"
ls -lh "$OUT_DIR"
echo ""
echo "Próximo paso: npm run package"
