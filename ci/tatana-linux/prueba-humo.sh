#!/usr/bin/env bash
# shellcheck disable=SC2015 # ok/falla nunca fallan: "cond && ok || falla" es un if/else.
# Prueba de humo del AppImage de Tatana para Linux (SDD tatana-linux §9.1, D13 niveles 1 y 2). La corren los jobs
# "humo" (ubuntu-24.04) y "distros" (contenedores ubuntu:22.04, debian:12, fedora) de .github/workflows/tatana-linux.yml.
#
#   bash ci/tatana-linux/prueba-humo.sh <Tatana-X.Y.Z-x86_64.AppImage> [--distro <id>] [--gestor apt|dnf]
#        [--sin-ui] [--no-sandbox]
#
#   (a) --appimage-extract y bits x de resources/agent/
#   (b) Factum.Agent --autoprueba-linux y --autoprueba-ios
#   (c) agente suelto: /health (os, linux.distro_id, herramientas portátiles, pymobiledevice3, ios.apple_service) y
#       cierre con SIGTERM en < 20 s
#   (d) preparar-pc.sh --simular: soportada y gestor esperado
#   (e) (sin --sin-ui) la app con Xvfb: montada con FUSE si hay /dev/fuse (si no, APPIMAGE_EXTRACT_AND_RUN=1); a los
#       20 s sigue viva, /health responde y existe tatana.json; "Salir" (--quit) y a los 15 s no queda ningún proceso
#       de Tatana.
#
# No destructiva: HOME y XDG_* apuntan a una carpeta propia (en $RUNNER_TEMP o un temporal) y no toca nada afuera.
# Los logs quedan en $SALIDA_LOGS (default <trabajo>/logs) para subirlos si falla.
set -uo pipefail

APPIMAGE=""
DISTRO="ubuntu"
GESTOR="apt"
SIN_UI=0
NO_SANDBOX=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --distro) DISTRO="${2:-}"; shift 2 ;;
    --gestor) GESTOR="${2:-}"; shift 2 ;;
    --sin-ui) SIN_UI=1; shift ;;
    --no-sandbox) NO_SANDBOX=1; shift ;;
    -*) echo "Argumento desconocido: $1" >&2; exit 2 ;;
    *) APPIMAGE="$1"; shift ;;
  esac
done
[[ -f "$APPIMAGE" ]] || { echo "Uso: $0 <AppImage> [--distro id] [--gestor apt|dnf] [--sin-ui] [--no-sandbox]" >&2; exit 2; }
APPIMAGE="$(cd "$(dirname "$APPIMAGE")" && pwd)/$(basename "$APPIMAGE")"
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

TRABAJO="${RUNNER_TEMP:-$(mktemp -d "${TMPDIR:-/tmp}/tatana-humo.XXXXXX")}/tatana-humo-linux"
rm -rf "$TRABAJO"
mkdir -p "$TRABAJO"
SALIDA_LOGS="${SALIDA_LOGS:-$TRABAJO/logs}"
mkdir -p "$SALIDA_LOGS"
PUERTO=8765
FALLAS=0
PIDS_A_LIMPIAR=()

ok() { echo "OK  $*"; }
falla() { echo "FALLA  $*"; FALLAS=$((FALLAS + 1)); }
limpiar() {
  local p
  for p in "${PIDS_A_LIMPIAR[@]+"${PIDS_A_LIMPIAR[@]}"}"; do kill -KILL "$p" 2>/dev/null || true; done
}
trap limpiar EXIT

health() { curl -fsS --max-time 5 "http://127.0.0.1:$PUERTO/health" 2>/dev/null; }

esperar_health() { # segundos → imprime el JSON
  local hasta=$((SECONDS + $1)) h
  while ((SECONDS < hasta)); do
    h="$(health)" && { printf '%s' "$h"; return 0; }
    sleep 1
  done
  return 1
}

# Procesos de Tatana: exe bajo un montaje del AppImage, la extracción o el squashfs-root de esta prueba.
procesos_tatana() {
  # exe (sin " (deleted)") o, si no se puede leer o es un emulador, el argv[0] de cmdline.
  local d exe arg0 x
  for d in /proc/[0-9]*; do
    exe="$(readlink "$d/exe" 2>/dev/null)" || exe=""
    exe="${exe% (deleted)}"
    arg0="$(tr '\0' '\n' <"$d/cmdline" 2>/dev/null | head -1)"
    for x in "$exe" "$arg0"; do
      case "$x" in
        */.mount_Tatan*|*/appimage_extracted_*|"$TRABAJO"/*) echo "${d#/proc/} $x"; break ;;
      esac
    done
  done
}

echo "AppImage: $APPIMAGE ($(du -h "$APPIMAGE" | cut -f1))"
echo "Distro esperada: $DISTRO, gestor $GESTOR, UI $([[ $SIN_UI == 1 ]] && echo no || echo sí)"

# ── (a) Extraer y bits de ejecución ───────────────────────────────────────────────────────────────────────
chmod +x "$APPIMAGE"
(cd "$TRABAJO" && "$APPIMAGE" --appimage-extract >/dev/null 2>"$SALIDA_LOGS/extract.log")
RAIZ="$TRABAJO/squashfs-root"
AGENTE="$RAIZ/resources/agent"
if [[ -x "$AGENTE/Factum.Agent" ]]; then ok "(a) --appimage-extract: resources/agent/Factum.Agent"; else falla "(a) no se extrajo resources/agent/Factum.Agent"; fi
for exe in Factum.Agent tools/platform-tools/adb tools/scrcpy/scrcpy tools/ffmpeg/ffmpeg tools/python-embed/bin/python3; do
  [[ -x "$AGENTE/$exe" ]] && ok "(a) +x $exe" || falla "(a) $exe no es ejecutable dentro del AppImage"
done
[[ ! -e "$AGENTE/appsettings.Local.json" ]] && ok "(a) sin appsettings.Local.json" || falla "(a) el AppImage trae appsettings.Local.json"
[[ ! -e "$AGENTE/tools/scrcpy/adb" ]] && ok "(a) sin el adb de scrcpy" || falla "(a) el AppImage trae el adb de scrcpy"

# ── (b) Autopruebas ───────────────────────────────────────────────────────────────────────────────────────
if "$AGENTE/Factum.Agent" --autoprueba-linux "$TRABAJO/al" >"$SALIDA_LOGS/autoprueba-linux.log" 2>&1; then
  ok "(b) --autoprueba-linux"
else
  falla "(b) --autoprueba-linux"; sed 's/^/    /' "$SALIDA_LOGS/autoprueba-linux.log"
fi
if "$AGENTE/Factum.Agent" --autoprueba-ios "$TRABAJO/ai" >"$SALIDA_LOGS/autoprueba-ios.log" 2>&1; then
  ok "(b) --autoprueba-ios"
else
  falla "(b) --autoprueba-ios"; sed 's/^/    /' "$SALIDA_LOGS/autoprueba-ios.log"
fi
grep -h '^\(OK\|FALLA\)' "$SALIDA_LOGS/autoprueba-linux.log" "$SALIDA_LOGS/autoprueba-ios.log" | sed 's/^/    /'

# ── (c) Agente suelto: /health y SIGTERM ──────────────────────────────────────────────────────────────────
if health >/dev/null; then
  falla "(c) ya hay algo escuchando en $PUERTO antes de arrancar"
fi
"$AGENTE/Factum.Agent" --port "$PUERTO" --data "$TRABAJO/d" --mode installed >"$SALIDA_LOGS/agente.log" 2>&1 &
AG=$!
PIDS_A_LIMPIAR+=("$AG")
if H="$(esperar_health 60)"; then
  # tools.python.pymobiledevice3_version se llena en segundo plano: se espera hasta 60 s.
  hasta=$((SECONDS + 60))
  while ((SECONDS < hasta)) && [[ "$(jq -r '.tools.python.pymobiledevice3_version // empty' <<<"$H")" == "" ]]; do
    sleep 2; H="$(health || echo '{}')"
  done
  printf '%s\n' "$H" >"$SALIDA_LOGS/health.json"
  chequeo() { # descripción expresión-jq valor-esperado
    local real
    real="$(jq -r "$2" <<<"$H" 2>/dev/null)"
    [[ "$real" == "$3" ]] && ok "(c) $1 = $3" || falla "(c) $1 = '$real' (se esperaba '$3')"
  }
  chequeo os '.os' linux
  chequeo linux.distro_id '.linux.distro_id' "$DISTRO"
  for t in adb scrcpy ffmpeg python; do chequeo "tools.$t.source" ".tools.$t.source" portable; done
  chequeo tools.python.pymobiledevice3_version '.tools.python.pymobiledevice3_version' 10.7.4
  chequeo "ios.apple_service presente" '.ios.apple_service | type' string
  chequeo "linux.checks (7)" '.linux.checks | length' 7
else
  falla "(c) /health no respondió en 60 s"; tail -50 "$SALIDA_LOGS/agente.log" | sed 's/^/    /'
fi
T0=$SECONDS
if kill -0 "$AG" 2>/dev/null; then
  kill -TERM "$AG" 2>/dev/null
  for _ in $(seq 1 20); do kill -0 "$AG" 2>/dev/null || break; sleep 1; done
  if kill -0 "$AG" 2>/dev/null; then
    falla "(c) el agente no salió en 20 s con SIGTERM"; kill -KILL "$AG" 2>/dev/null
  else
    wait "$AG" 2>/dev/null
    ok "(c) SIGTERM: el agente salió en $((SECONDS - T0)) s"
  fi
else
  falla "(c) el agente ya no estaba corriendo antes del SIGTERM"
fi

# ── (d) Preparar esta PC (simulación, sin contraseña) ─────────────────────────────────────────────────────
PLAN="$(bash "$REPO/packaging/linux/preparar-pc.sh" --simular 2>&1 | sed -n 's/^TATANA_PLAN //p')"
printf '%s\n' "$PLAN" >"$SALIDA_LOGS/plan.json"
[[ "$(jq -r .soportada <<<"$PLAN" 2>/dev/null)" == true ]] && ok "(d) --simular: soportada" || falla "(d) --simular: no soportada ($PLAN)"
[[ "$(jq -r .gestor <<<"$PLAN" 2>/dev/null)" == "$GESTOR" ]] && ok "(d) --simular: gestor $GESTOR" || falla "(d) --simular: gestor $(jq -r .gestor <<<"$PLAN" 2>/dev/null)"

# ── (e) La app con Xvfb ───────────────────────────────────────────────────────────────────────────────────
if [[ $SIN_UI == 0 ]]; then
  CASA="$TRABAJO/home"
  mkdir -p "$CASA/.config" "$CASA/.local/share" "$CASA/runtime"
  chmod 700 "$CASA/runtime"
  export HOME="$CASA" XDG_CONFIG_HOME="$CASA/.config" XDG_DATA_HOME="$CASA/.local/share" XDG_RUNTIME_DIR="$CASA/runtime"
  # Xvfb propio (no xvfb-run): la app se relanza a sí misma y xvfb-run cerraría el display al salir el primer proceso.
  NUM=$((90 + RANDOM % 9))
  Xvfb ":$NUM" -screen 0 1280x800x24 -nolisten tcp >"$SALIDA_LOGS/xvfb.log" 2>&1 &
  XV=$!
  PIDS_A_LIMPIAR+=("$XV")
  export DISPLAY=":$NUM"
  sleep 2
  EXTRA=()
  [[ $NO_SANDBOX == 1 ]] && EXTRA+=(--no-sandbox)
  if [[ -c /dev/fuse ]] && command -v fusermount3 >/dev/null 2>&1; then
    MODO_FUSE="FUSE"
    unset APPIMAGE_EXTRACT_AND_RUN
  else
    MODO_FUSE="extract-and-run"
    export APPIMAGE_EXTRACT_AND_RUN=1
  fi
  "$APPIMAGE" "${EXTRA[@]+"${EXTRA[@]}"}" >"$SALIDA_LOGS/app.log" 2>&1 &
  APP=$!
  PIDS_A_LIMPIAR+=("$APP")
  sleep 20
  kill -0 "$APP" 2>/dev/null && ok "(e) la app sigue viva a los 20 s ($MODO_FUSE)" || { falla "(e) la app murió antes de 20 s"; tail -60 "$SALIDA_LOGS/app.log" | sed 's/^/    /'; }
  if esperar_health 40 >"$SALIDA_LOGS/health-app.json"; then
    ok "(e) /health responde (agente lanzado por Electron, versión $(jq -r .version "$SALIDA_LOGS/health-app.json"))"
  else
    falla "(e) /health no responde con la app abierta"
  fi
  [[ -f "$XDG_CONFIG_HOME/Tatana/tatana.json" ]] && ok "(e) existe ~/.config/Tatana/tatana.json" || falla "(e) no se creó ~/.config/Tatana/tatana.json"
  procesos_tatana >"$SALIDA_LOGS/procesos-antes.txt"
  [[ -s "$SALIDA_LOGS/procesos-antes.txt" ]] || falla "(e) no se encontró ningún proceso de Tatana antes de Salir"
  "$APPIMAGE" "${EXTRA[@]+"${EXTRA[@]}"}" --quit >>"$SALIDA_LOGS/app-quit.log" 2>&1 &
  PIDS_A_LIMPIAR+=("$!")
  sleep 15
  QUEDAN="$(procesos_tatana)"
  if [[ -z "$QUEDAN" ]]; then
    ok "(e) \"Salir\": a los 15 s no queda ningún proceso de Tatana"
  else
    falla "(e) \"Salir\": quedaron procesos de Tatana:"; sed 's/^/    /' <<<"$QUEDAN"
  fi
  if health >/dev/null; then falla "(e) el puerto $PUERTO sigue respondiendo después de Salir"; else ok "(e) el puerto $PUERTO quedó libre"; fi
  cp -r "$XDG_CONFIG_HOME/Tatana/logs" "$SALIDA_LOGS/tatana-logs" 2>/dev/null || true
fi

echo
if ((FALLAS)); then
  echo "RESULTADO: $FALLAS paso(s) con FALLA (logs en $SALIDA_LOGS)"
  exit 1
fi
echo "RESULTADO: OK"
