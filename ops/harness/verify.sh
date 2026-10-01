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
for f in AGENTS.md CLAUDE.md backlog.json progress/current.md progress/history.md CHECKPOINTS.md skills/CATALOGO.md; do
  if [ -f "$f" ]; then ok "Existe $f"; else fail "Falta archivo base: $f"; fi
done

echo ""
echo "── 2. Validando backlog.json ───────────────────────────"
node -e '
const fs = require("fs");
try {
  const data = JSON.parse(fs.readFileSync("backlog.json", "utf8"));
  const activos = data.reglas.estados_activos;
  const enCurso = data.features.filter(f => activos.includes(f.estado));
  if (enCurso.length > 1) {
    console.log("[FAIL]  Hay " + enCurso.length + " HU activas a la vez (máximo 1): " + enCurso.map(f => f.id).join(", "));
    process.exit(1);
  }
  for (const f of data.features) {
    if (!f.id || !f.estado) {
      console.log("[FAIL]  HU sin id/estado: " + JSON.stringify(f));
      process.exit(1);
    }
    if (!data.reglas.valid_status.includes(f.estado)) {
      console.log("[FAIL]  Estado inválido en HU " + f.id + ": " + f.estado);
      process.exit(1);
    }
    if ((f.intentos_revision || 0) > data.reglas.max_intentos_revision && f.estado !== "bloqueada" && f.estado !== "aprobada") {
      console.log("[FAIL]  HU " + f.id + " superó el tope de reintentos y no está bloqueada");
      process.exit(1);
    }
  }
  const ids = data.features.map(f => f.id);
  const dup = ids.filter((id, i) => ids.indexOf(id) !== i);
  if (dup.length) {
    console.log("[FAIL]  IDs de HU duplicados: " + [...new Set(dup)].join(", "));
    process.exit(1);
  }
  console.log("[OK]    backlog.json válido (" + data.features.length + " HU, " + enCurso.length + " activa)");
} catch (e) {
  console.log("[FAIL]  backlog.json inválido: " + e.message);
  process.exit(1);
}
'
if [ $? -ne 0 ]; then EXIT_CODE=1; fi

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
