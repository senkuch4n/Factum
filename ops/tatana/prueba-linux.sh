#!/usr/bin/env bash
# shellcheck disable=SC2015 # ok/falla nunca fallan: "cond && ok || falla" es un if/else.
# Prueba de actualización de Tatana para Linux en CI (SDD tatana-linux §9.4), inspirada en prueba-windows.ps1. La
# corre el job "prueba-linux" de .github/workflows/tatana-release.yml (ubuntu-24.04).
#
#   bash ops/tatana/prueba-linux.sh --canal <dir> --base <Tatana-0.0.1-x86_64.AppImage> --version X.Y.Z \
#        [--payload <Tatana-Agent-vX.Y.Z-linux-x64.tar.gz>] [--forzar-fuera-de-ci]
#
# --canal: carpeta con Tatana-X.Y.Z-x86_64.AppImage, latest-linux.yml y tatana-update-linux.json firmados (y
#          tatana-update.json, el manifiesto de Windows firmado con la misma clave, para el caso cruzado).
# --base:  el AppImage 0.0.1 armado con la misma clave de confianza que el canal.
#
# NO destructiva: HOME, XDG_CONFIG_HOME, XDG_DATA_HOME y XDG_RUNTIME_DIR apuntan a $RUNNER_TEMP/home (todo lo que
# escribe Tatana queda ahí). Se niega a correr fuera de CI salvo --forzar-fuera-de-ci. Lanza la app con un Xvfb
# propio (la app se relanza al actualizar y xvfb-run cerraría el display al salir el primer proceso) y FUSE; sin
# /dev/fuse, con APPIMAGE_EXTRACT_AND_RUN=1. Sirve el canal con python3 -m http.server y TATANA_UPDATE_URLS.
#
#   1. Base 0.0.1 en ~/Applications/Tatana.AppImage + centinelas (evidencia, ZIP, appsettings.Local.json y un
#      preparacion-linux.json falso) con sus hashes.
#   2. Arranque: /health.version == 0.0.1, .desktop del menú y autoarranque con la ruta canónica.
#   3. Manifiesto con la firma alterada → rejected/firma_invalida, sin descarga.
#   4. Manifiesto de Windows publicado como tatana-update-linux.json → rejected/manifiesto_invalido.
#   5. Canal válido con una subida en curso → ready, pero --install-update no instala (busyOperations).
#   6. Sin la subida → --install-update → relanzado en X.Y.Z, la canónica es el AppImage del canal, up_to_date y
#      centinelas intactos.
#   7. Mismo canal: no repite (up_to_date). Sin red: offline.
#   8. "Salir": no queda ningún proceso de Tatana.
set -uo pipefail

CANAL=""
BASE=""
VERSION=""
PAYLOAD=""
FORZAR=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --canal) CANAL="${2:-}"; shift 2 ;;
    --base) BASE="${2:-}"; shift 2 ;;
    --version) VERSION="${2:-}"; shift 2 ;;
    --payload) PAYLOAD="${2:-}"; shift 2 ;;
    --forzar-fuera-de-ci) FORZAR=1; shift ;;
    *) echo "Argumento desconocido: $1" >&2; exit 2 ;;
  esac
done
[[ -d "$CANAL" && -f "$BASE" && "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || {
  echo "Uso: $0 --canal <dir> --base <AppImage 0.0.1> --version X.Y.Z [--payload <tar.gz>] [--forzar-fuera-de-ci]" >&2
  exit 2
}
if [[ "${CI:-}" != true && "${GITHUB_ACTIONS:-}" != true && $FORZAR -eq 0 ]]; then
  echo "ERROR: esta prueba es para CI (usa un HOME propio y Xvfb). Fuera de CI, --forzar-fuera-de-ci." >&2
  exit 2
fi
for h in curl jq python3 Xvfb sha256sum; do
  command -v "$h" >/dev/null 2>&1 || { echo "ERROR: falta '$h'." >&2; exit 1; }
done

CANAL="$(cd "$CANAL" && pwd)"
AI_CANAL="$CANAL/Tatana-$VERSION-x86_64.AppImage"
for f in "$AI_CANAL" "$CANAL/latest-linux.yml" "$CANAL/tatana-update-linux.json" "$CANAL/tatana-update.json"; do
  [[ -f "$f" ]] || { echo "ERROR: falta $f en el canal." >&2; exit 2; }
done

BASE_TMP="${RUNNER_TEMP:-$(mktemp -d "${TMPDIR:-/tmp}/tatana-prueba-linux.XXXXXX")}"
TRABAJO="$BASE_TMP/tatana-prueba-linux"
CASA="$BASE_TMP/home"
rm -rf "$TRABAJO" "$CASA"
mkdir -p "$TRABAJO/servido" "$TRABAJO/logs" "$CASA/.config" "$CASA/.local/share" "$CASA/runtime" "$CASA/Applications"
chmod 700 "$CASA/runtime"
export HOME="$CASA" XDG_CONFIG_HOME="$CASA/.config" XDG_DATA_HOME="$CASA/.local/share" XDG_RUNTIME_DIR="$CASA/runtime"
LOGS="${SALIDA_LOGS:-$TRABAJO/logs}"
mkdir -p "$LOGS"

USERDATA="$XDG_CONFIG_HOME/Tatana"
ESTADO="$USERDATA/update-state.json"
DATOS="$XDG_DATA_HOME/Tatana/data"
CANONICA="$HOME/Applications/Tatana.AppImage"
AGENTE="http://127.0.0.1:8765"
PUERTO_CANAL=8099
CASE_ID="0c1a5e00-ca5e-4000-8000-0000000000c1"
RESUMEN="$TRABAJO/resumen.md"
: >"$RESUMEN"
PIDS=()

ok() { echo "OK   $*"; echo "- OK $*" >>"$RESUMEN"; }
falla() {
  echo "FALLA  $*"
  echo "- **FALLA** $*" >>"$RESUMEN"
  terminar 1
}
paso() { echo; echo "== $*"; printf '\n**%s**\n\n' "$*" >>"$RESUMEN"; }

terminar() {
  local codigo="$1" p
  cerrar_app 30 >/dev/null 2>&1 || true
  for p in "${PIDS[@]+"${PIDS[@]}"}"; do kill -KILL "$p" 2>/dev/null || true; done
  cp -r "$USERDATA/logs" "$LOGS/tatana-logs" 2>/dev/null || true
  cp "$ESTADO" "$LOGS/" 2>/dev/null || true
  if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
    { echo "### Prueba de Linux (Tatana $VERSION)"; cat "$RESUMEN"; } >>"$GITHUB_STEP_SUMMARY"
  fi
  exit "$codigo"
}
trap 'terminar 1' INT TERM

health() { curl -fsS --max-time 5 "$AGENTE/health" 2>/dev/null; }

esperar_health() { # versión segundos
  local hasta=$((SECONDS + $2)) h v=""
  while ((SECONDS < hasta)); do
    if h="$(health)"; then
      v="$(jq -r .version <<<"$h")"
      [[ "$v" == "$1" ]] && return 0
    fi
    sleep 2
  done
  falla "/health no llegó a la versión $1 en $2 s (última: ${v:-sin respuesta})"
}

# Espera la fase en update-state.json con checkedAt posterior a $2 (epoch; 0 = cualquiera). Deja el JSON en E.
E=""
esperar_fase() { # fase desde segundos
  local hasta=$((SECONDS + ${3:-180})) e fase="" chk
  while ((SECONDS < hasta)); do
    if [[ -f "$ESTADO" ]] && e="$(cat "$ESTADO" 2>/dev/null)" && jq -e . >/dev/null 2>&1 <<<"$e"; then
      fase="$(jq -r '.phase // empty' <<<"$e")"
      chk="$(jq -r '.checkedAt // empty' <<<"$e")"
      if [[ "$fase" == "$1" ]] && { [[ "$2" == 0 || -z "$chk" ]] || (( $(date -d "$chk" +%s 2>/dev/null || echo 0) >= $2 - 2 )); }; then
        E="$e"
        return 0
      fi
    fi
    sleep 2
  done
  falla "update-state.json no llegó a '$1' en ${3:-180} s (última fase: ${fase:-ninguna})"
}

procesos_tatana() {
  # exe (sin " (deleted)") o, si no se puede leer o es un emulador, el argv[0] de cmdline.
  local d exe arg0 x
  for d in /proc/[0-9]*; do
    exe="$(readlink "$d/exe" 2>/dev/null)" || exe=""
    exe="${exe% (deleted)}"
    arg0="$(tr '\0' '\n' <"$d/cmdline" 2>/dev/null | head -1)"
    for x in "$exe" "$arg0"; do
      case "$x" in
        */.mount_Tatan*|*/appimage_extracted_*) echo "${d#/proc/} $x"; break ;;
      esac
    done
  done
}

DESDE=0
lanzar_app() { # deja en DESDE el epoch de lanzamiento
  DESDE="$(date +%s)"
  "$CANONICA" >>"$LOGS/app.log" 2>&1 &
  PIDS+=("$!")
}

cerrar_app() { # segundos
  local hasta=$((SECONDS + ${1:-60}))
  [[ -x "$CANONICA" ]] && { "$CANONICA" --quit >>"$LOGS/app.log" 2>&1 & }
  while ((SECONDS < hasta)); do
    [[ -z "$(procesos_tatana)" ]] && ! health >/dev/null && return 0
    sleep 1
  done
  return 1
}

# El canal servido: copia los archivos con una mtime nueva y estrictamente creciente (Last-Modified tiene
# resolución de 1 s y http.server contesta 304 a un pedido condicional, como en prueba-windows.ps1).
ULTIMA_MTIME=0
servir() { # archivo:nombre ...
  local par origen nombre m
  rm -rf "${TRABAJO:?}/servido/"*
  m="$(date +%s)"
  ((m < ULTIMA_MTIME + 2)) && m=$((ULTIMA_MTIME + 2))
  ULTIMA_MTIME=$m
  for par in "$@"; do
    origen="${par%%:*}"; nombre="${par#*:}"
    cp "$origen" "$TRABAJO/servido/$nombre"
    touch -d "@$m" "$TRABAJO/servido/$nombre"
  done
}

declare -A CENTINELAS=()
verificar_centinelas() {
  local r
  for r in "${!CENTINELAS[@]}"; do
    [[ -f "$r" ]] || falla "$1: desapareció $r"
    [[ "$(sha256sum "$r" | cut -d' ' -f1)" == "${CENTINELAS[$r]}" ]] || falla "$1: cambió $r"
  done
  ok "$1: evidencia, ZIP, config y preparacion-linux.json intactos (${#CENTINELAS[@]} centinelas)"
}

aleatorio() { mkdir -p "$(dirname "$1")"; head -c "$2" /dev/urandom >"$1"; }

# ── Display, FUSE y canal ─────────────────────────────────────────────────────────────────────────────────
NUM=$((90 + RANDOM % 9))
Xvfb ":$NUM" -screen 0 1280x800x24 -nolisten tcp >"$LOGS/xvfb.log" 2>&1 &
PIDS+=("$!")
export DISPLAY=":$NUM"
if [[ -c /dev/fuse ]] && command -v fusermount3 >/dev/null 2>&1; then
  echo "AppImage montado con FUSE"
else
  echo "Sin /dev/fuse: APPIMAGE_EXTRACT_AND_RUN=1"
  export APPIMAGE_EXTRACT_AND_RUN=1
fi
python3 -m http.server "$PUERTO_CANAL" --bind 127.0.0.1 --directory "$TRABAJO/servido" >"$LOGS/canal-http.log" 2>&1 &
PIDS+=("$!")
export TATANA_UPDATE_URLS="http://127.0.0.1:$PUERTO_CANAL/"
sleep 2

# ── 1. Base 0.0.1 instalada + centinelas ──────────────────────────────────────────────────────────────────
paso "1. Base 0.0.1 en ~/Applications con evidencia"
cp "$BASE" "$CANONICA" && chmod 755 "$CANONICA"
mkdir -p "$USERDATA" "$DATOS/cases/$CASE_ID" "$HOME/Factum/Evidencia/ci-centinela"
aleatorio "$DATOS/cases/$CASE_ID/evidencia.bin" 1048576
aleatorio "$HOME/Factum/Evidencia/ci-centinela/caso.zip" 1048576
printf '{\n  "Agent": {\n    "AllowedOrigins": ["http://localhost:3000", "https://ci.invalid"]\n  }\n}\n' >"$USERDATA/appsettings.Local.json"
printf '{ "schema": 1, "fecha": "2026-10-18T00:00:00Z", "tatana_version": "0.0.1", "distro": "ubuntu 24.04", "gestor": "apt", "resultado": "ok", "requiere_reiniciar_sesion": false, "script_sha256": "%064d", "pasos": [] }\n' 0 >"$USERDATA/preparacion-linux.json"
for r in "$DATOS/cases/$CASE_ID/evidencia.bin" "$HOME/Factum/Evidencia/ci-centinela/caso.zip" \
         "$USERDATA/appsettings.Local.json" "$USERDATA/preparacion-linux.json"; do
  CENTINELAS["$r"]="$(sha256sum "$r" | cut -d' ' -f1)"
done
ok "centinelas creados (${#CENTINELAS[@]})"

# ── 2. Arranque ───────────────────────────────────────────────────────────────────────────────────────────
paso "2. Arranque de la base"
servir   # canal vacío: el chequeo da offline/404, no importa acá
lanzar_app
esperar_health 0.0.1 120
ok "/health.version = 0.0.1"
MENU="$XDG_DATA_HOME/applications/factum-tatana.desktop"
AUTO="$XDG_CONFIG_HOME/autostart/factum-tatana.desktop"
hasta=$((SECONDS + 30))
while ((SECONDS < hasta)) && [[ ! -f "$MENU" || ! -f "$AUTO" ]]; do sleep 1; done
grep -qF "Exec=\"$CANONICA\"" "$MENU" 2>/dev/null && ok ".desktop del menú con la ruta canónica" || falla ".desktop del menú ($MENU) sin Exec=\"$CANONICA\""
grep -qF "Exec=\"$CANONICA\" --hidden" "$AUTO" 2>/dev/null && ok "autoarranque con --hidden" || falla "autoarranque ($AUTO) sin Exec=\"$CANONICA\" --hidden"
cerrar_app 60 || falla "la base no se cerró con --quit"

# ── 3. Firma inválida ─────────────────────────────────────────────────────────────────────────────────────
paso "3. Manifiesto con firma inválida"
node -e '
  const fs = require("fs"), c = require("crypto");
  const [src, dst] = process.argv.slice(1);
  const env = JSON.parse(fs.readFileSync(src, "utf8"));
  const { privateKey } = c.generateKeyPairSync("ed25519");
  const sig = c.sign(null, Buffer.from(env.payload, "base64"), privateKey);
  fs.writeFileSync(dst, JSON.stringify({ ...env, signature: sig.toString("base64") }));
' "$CANAL/tatana-update-linux.json" "$TRABAJO/refirmado.json" || falla "no se pudo re-firmar el manifiesto"
CANAL_ARCH=("$AI_CANAL:Tatana-$VERSION-x86_64.AppImage" "$CANAL/latest-linux.yml:latest-linux.yml")
servir "${CANAL_ARCH[@]}" "$TRABAJO/refirmado.json:tatana-update-linux.json"
lanzar_app
esperar_health 0.0.1 120
esperar_fase rejected "$DESDE"
[[ "$(jq -r .reason <<<"$E")" == firma_invalida ]] || falla "reason = $(jq -r .reason <<<"$E") (se esperaba firma_invalida)"
[[ "$(health | jq -r .version)" == 0.0.1 ]] || falla "cambió la versión con una firma inválida"
ok "firma inválida: rejected/firma_invalida y sigue 0.0.1"
cerrar_app 60 || falla "no se cerró con --quit"

# ── 4. Manifiesto de Windows en el lugar del de Linux ─────────────────────────────────────────────────────
paso "4. Manifiesto de Windows publicado como tatana-update-linux.json"
servir "${CANAL_ARCH[@]}" "$CANAL/tatana-update.json:tatana-update-linux.json"
lanzar_app
esperar_health 0.0.1 120
esperar_fase rejected "$DESDE"
[[ "$(jq -r .reason <<<"$E")" == manifiesto_invalido ]] || falla "reason = $(jq -r .reason <<<"$E") (se esperaba manifiesto_invalido)"
ok "manifiesto de Windows: rejected/manifiesto_invalido"
cerrar_app 60 || falla "no se cerró con --quit"

# ── 5. Canal válido con una operación en curso ────────────────────────────────────────────────────────────
paso "5. Actualización a $VERSION con una subida en curso"
servir "${CANAL_ARCH[@]}" "$CANAL/tatana-update-linux.json:tatana-update-linux.json"
lanzar_app
esperar_health 0.0.1 120
esperar_fase ready "$DESDE" 300
[[ "$(jq -r .availableVersion <<<"$E")" == "$VERSION" ]] || falla "availableVersion = $(jq -r .availableVersion <<<"$E")"
ok "descargada y verificada: ready ($VERSION)"
aleatorio "$TRABAJO/ci-subida.bin" 4194304
HASH_SUBIDA="$(sha256sum "$TRABAJO/ci-subida.bin" | cut -d' ' -f1)"
curl -sS -o "$TRABAJO/subida.body" -w '%{http_code}' --limit-rate 100k -X POST -H 'Expect:' \
  -H 'Content-Type: application/octet-stream' --data-binary "@$TRABAJO/ci-subida.bin" \
  "$AGENTE/cases/$CASE_ID/evidence/upload?filename=ci-subida.bin" >"$TRABAJO/subida.code" 2>"$TRABAJO/subida.err" &
CURL=$!
PIDS+=("$CURL")
hasta=$((SECONDS + 15)); OCUPADO=0
while ((SECONDS < hasta)); do
  [[ "$(curl -fsS --max-time 3 "$AGENTE/agent/state" 2>/dev/null | jq -r .busy)" == true ]] && { OCUPADO=1; break; }
  sleep 0.3
done
((OCUPADO)) || falla "/agent/state.busy no se puso en true durante la subida"
ok "/agent/state.busy = true durante la subida"
"$CANONICA" --install-update >>"$LOGS/app.log" 2>&1 &
sleep 10
kill -0 "$CURL" 2>/dev/null || falla "la subida terminó antes de comprobar el bloqueo (subí el tamaño o bajá --limit-rate)"
[[ "$(health | jq -r .version)" == 0.0.1 ]] || falla "se instaló con una operación en curso"
E="$(cat "$ESTADO")"
[[ "$(jq -r .phase <<<"$E")" == ready && "$(jq -r '.busyOperations // [] | length' <<<"$E")" -gt 0 ]] \
  || falla "con la subida en curso: phase=$(jq -r .phase <<<"$E"), busyOperations=$(jq -c '.busyOperations' <<<"$E")"
ok "no instala con operaciones en curso (busyOperations: $(jq -r '.busyOperations | join(", ")' <<<"$E"))"
wait "$CURL" || true
[[ "$(cat "$TRABAJO/subida.code")" == 200 ]] || falla "la subida no dio 200: $(cat "$TRABAJO/subida.code") $(head -c 300 "$TRABAJO/subida.body")"
DESTINO="$DATOS/cases/$CASE_ID/ci-subida.bin"
[[ "$(sha256sum "$DESTINO" | cut -d' ' -f1)" == "$HASH_SUBIDA" ]] || falla "la subida no quedó idéntica"
CENTINELAS["$DESTINO"]="$HASH_SUBIDA"
ok "subida terminada (200) e idéntica"

# ── 6. Instalar ───────────────────────────────────────────────────────────────────────────────────────────
paso "6. Reiniciar y actualizar"
"$CANONICA" --install-update >>"$LOGS/app.log" 2>&1 &
esperar_health "$VERSION" 180
ok "/health.version = $VERSION después de relanzarse"
[[ -n "$(procesos_tatana)" ]] || falla "la app no se relanzó después de actualizar"
[[ "$(sha256sum "$CANONICA" | cut -d' ' -f1)" == "$(sha256sum "$AI_CANAL" | cut -d' ' -f1)" ]] \
  || falla "$CANONICA no es el AppImage del canal"
[[ -x "$CANONICA" ]] || falla "la canónica quedó sin permiso de ejecución"
ok "la canónica es el AppImage del canal (sha256) y es ejecutable"
esperar_fase up_to_date 0 120
ok "update-state: up_to_date"
verificar_centinelas "después de actualizar"
[[ ! -e "$CANONICA.nuevo" ]] && ok "no quedó Tatana.AppImage.nuevo" || falla "quedó $CANONICA.nuevo"

# ── 7. Sin repetición y sin red ───────────────────────────────────────────────────────────────────────────
paso "7. Mismo canal: no repite; sin red: offline"
cerrar_app 60 || falla "no se cerró con --quit"
lanzar_app
esperar_health "$VERSION" 120
esperar_fase up_to_date "$DESDE"
ok "sigue en $VERSION (up_to_date)"
cerrar_app 60 || falla "no se cerró con --quit"
export TATANA_UPDATE_URLS="http://127.0.0.1:9/"
lanzar_app
esperar_health "$VERSION" 120
esperar_fase offline "$DESDE"
ok "sin red: offline"
export TATANA_UPDATE_URLS="http://127.0.0.1:$PUERTO_CANAL/"

# ── 8. Salir sin huérfanos ────────────────────────────────────────────────────────────────────────────────
paso "8. Salir"
if cerrar_app 30; then
  ok "\"Salir\": no queda ningún proceso de Tatana y el puerto 8765 está libre"
else
  falla "\"Salir\": quedaron procesos: $(procesos_tatana | tr '\n' ';')"
fi
verificar_centinelas "al final"
[[ -n "$PAYLOAD" ]] && ok "payload de referencia: $(basename "$PAYLOAD")"

echo
echo "RESULTADO: OK"
terminar 0
