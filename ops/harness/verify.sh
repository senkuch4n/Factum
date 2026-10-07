#!/usr/bin/env bash
# ops/harness/verify.sh — Verificación del arnés RDD/SDD de Factum.
#
# La corre el hook Stop de .claude/settings.json antes de cerrar sesión, y el
# orquestador antes de declarar una HU "aprobada" o "arquitectura_lista".
# Solo compila/type-checkea los lados con cambios sin commitear: un build
# completo de todo en cada cierre es demasiado lento.

set -u
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[0;33m'; NC='\033[0m'
EXIT_CODE=0
ok()   { printf "${GREEN}[OK]${NC}    %s\n" "$1"; }
warn() { printf "${YELLOW}[WARN]${NC}  %s\n" "$1"; }
fail() { printf "${RED}[FAIL]${NC}  %s\n" "$1"; EXIT_CODE=1; }

REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null)"
if [ -z "$REPO_ROOT" ]; then
  fail "No estoy dentro de un repo git"
  exit 1
fi
cd "$REPO_ROOT" || exit 1

echo "── 1. Archivos base del arnés ──────────────────────────"
for f in AGENTS.md CLAUDE.md CHECKPOINTS.md skills/CATALOGO.md .mcp.json ops/harness/hu.mjs .github/ISSUE_TEMPLATE/hu.yml; do
  if [ -f "$f" ]; then ok "Existe $f"; else fail "Falta archivo base: $f"; fi
done

echo ""
echo "── 2. Estado de HU (GitHub Project) ────────────────────"
# El estado vive en el Project "Factum – HU" y lo mantiene ops/harness/hu.mjs
# (una activa por persona, tope de reintentos). Acá solo se chequea lo local,
# sin red, para que el hook Stop sea rápido.
if git diff --quiet HEAD -- backlog.json 2>/dev/null; then
  ok "backlog.json archivado sin cambios"
else
  fail "backlog.json está archivado (solo lectura) y tiene cambios: el estado se cambia con ops/harness/hu.mjs"
fi
if node --check ops/harness/hu.mjs 2>/dev/null; then ok "hu.mjs parsea"; else fail "ops/harness/hu.mjs tiene errores de sintaxis"; fi
RAMA="$(git branch --show-current)"
case "$RAMA" in
  feat/*|fix/*|develop|main) ok "Rama actual: $RAMA" ;;
  *) warn "Rama '$RAMA' no sigue feat/<slug> o fix/<slug>" ;;
esac

echo ""
echo "── 3. Build / type-check (solo lados con cambios sin commitear) ─"
CHANGED="$(git status --porcelain | awk '{print $2}')"

if echo "$CHANGED" | grep -q '^client/'; then
  if [ -d client/node_modules ]; then
    echo "  client/ tiene cambios -> npx tsc --noEmit"
    if (cd client && npx tsc --noEmit); then ok "client: tsc limpio"; else fail "client: tsc con errores"; fi
  else
    warn "client/ tiene cambios pero no hay node_modules; se saltea tsc (npm install en client/)"
  fi
else
  warn "client/ sin cambios sin commitear, se saltea tsc"
fi

if echo "$CHANGED" | grep -q '^agent-ui/'; then
  if [ -d agent-ui/node_modules ]; then
    echo "  agent-ui/ tiene cambios -> tsc web + node"
    if (cd agent-ui && npx tsc --noEmit -p tsconfig.web.json && npx tsc --noEmit -p tsconfig.node.json); then
      ok "agent-ui: tsc limpio"
    else
      fail "agent-ui: tsc con errores"
    fi
  else
    warn "agent-ui/ tiene cambios pero no hay node_modules; se saltea tsc (npm install en agent-ui/)"
  fi
else
  warn "agent-ui/ sin cambios sin commitear, se saltea tsc"
fi

dotnet_build() {
  local nombre="$1" carpeta="$2"
  if echo "$CHANGED" | grep -q "^server/src/$carpeta/"; then
    if command -v dotnet >/dev/null 2>&1; then
      echo "  $carpeta tiene cambios -> dotnet build"
      if dotnet build "server/src/$carpeta/$carpeta.csproj" -nologo -v q >/tmp/factum_build_"$carpeta".log 2>&1; then
        ok "$nombre: dotnet build limpio"
      else
        fail "$nombre: dotnet build con errores (ver /tmp/factum_build_$carpeta.log)"
        tail -20 /tmp/factum_build_"$carpeta".log
      fi
    else
      warn "$carpeta tiene cambios pero no hay dotnet en el PATH; se saltea el build"
    fi
  else
    warn "$carpeta sin cambios sin commitear, se saltea el build"
  fi
}
dotnet_build "API" "Factum.Backend"
dotnet_build "Agente Tatana" "Factum.Agent"

echo ""
echo "── 4. Recordatorios manuales (no bloquean, avisan) ─────"
if echo "$CHANGED" | grep -qE '^server/src/Factum\.Agent/'; then
  warn "Se tocó el agente Tatana — probar en modo Mock y, si toca USB, con un dispositivo real"
fi
if echo "$CHANGED" | grep -qE '^server/src/Factum\.Backend/(Templates|Services/.*(Report|Informe|Zip|Pdf))'; then
  warn "Se tocó la generación de informes — verificar el PDF/ZIP real, no solo el build"
fi
if echo "$CHANGED" | grep -qE '^server/src/Factum\.Backend/Models/'; then
  warn "Se tocaron modelos de Mongo — confirmar que toleran documentos existentes sin los campos nuevos"
fi

echo ""
echo "── 5. Resumen ───────────────────────────────────────────"
if [ "$EXIT_CODE" -eq 0 ]; then
  ok "Arnés OK."
else
  fail "Arnés con problemas — revisar arriba antes de cerrar la sesión."
fi

exit $EXIT_CODE
