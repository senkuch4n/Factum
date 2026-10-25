#!/usr/bin/env bash
# cambiar_contenedor (deploy/tatana-docker/paquete/comun.sh, SDD fiscalia-operacion §7.6) NUNCA baja el contenedor si el
# agente puede estar vivo: solo con "conexión rechazada" (HTTP=0) se cambia sin modo mantenimiento. Timeout, reset,
# HTTP raro o una respuesta vacía → código 5 (error_agente) sin stop/rename/run. Review de #95, reintento 1.
#
#   bash ci/tatana-docker/probar-cambiar-contenedor.sh
#
# Sin Docker ni red: `docker` es un doble que registra las llamadas. El último caso usa el estado-agente.py REAL contra
# un socket local que acepta la conexión y no contesta (agente colgado en medio de una captura).
set -uo pipefail
RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
KIT="$RAIZ/deploy/tatana-docker/paquete"
T="$(mktemp -d "${TMPDIR:-/tmp}/probar-cambiar.XXXXXX")"
trap 'rm -rf "$T"; [[ -n "${PID_MUDO:-}" ]] && kill "$PID_MUDO" 2>/dev/null' EXIT
fallos=0

caso() { # caso <descripción> <salida de "estado_agente mantenimiento" | @real:<puerto>> <rc esperado> <¿cambia? 0|1>
  local desc="$1" sim="$2" esperado="$3" cambia="$4" rc llamadas
  : >"$T/llamadas"
  # shellcheck disable=SC2016
  LLAMADAS="$T/llamadas" SIM="$sim" KIT="$KIT" bash -c '
    . "$KIT/comun.sh" >/dev/null
    registrar() { :; }
    sleep() { :; }
    docker_doble() {
      echo "$*" >>"$LLAMADAS"
      case "$1 $2" in
        "container inspect")
          case "$*" in
            *factum.tatana-docker*) echo 1 ;;
            *State.Running*) echo true ;;
            *Config.Image*) echo factum-tatana-docker:1.0.0 ;;
          esac ;;
      esac
      return 0
    }
    DOCKER=(docker_doble)
    PUERTO=8765
    if [[ "$SIM" == @real:* ]]; then
      estado_agente() { python3 -I "$KIT/estado-agente.py" --puerto "${SIM#@real:}" --accion "$@"; }
    else
      estado_agente() { case "$1" in mantenimiento) printf "%b\n" "$SIM" ;; version) echo VERSION=1.0.1 ;; estado) echo MOTIVOS=x ;; esac; }
    fi
    RUN_COMPLETO=(run -d --name tatana-docker factum-tatana-docker:1.0.1)
    cambiar_contenedor factum-tatana-docker:1.0.1 "" 5
  '
  rc=$?
  llamadas="$(grep -cE '^(stop|rename|run|rm) ' "$T/llamadas")"
  if [[ "$rc" == "$esperado" ]] && { [[ "$cambia" == 1 && "$llamadas" -gt 0 ]] || [[ "$cambia" == 0 && "$llamadas" == 0 ]]; }; then
    echo "ok    $desc (rc=$rc, llamadas que cambian: $llamadas)"
  else
    echo "FALLA $desc: rc=$rc (esperado $esperado), llamadas que cambian: $llamadas (esperado $([[ $cambia == 1 ]] && echo '>0' || echo 0))"
    sed 's/^/        /' "$T/llamadas"
    fallos=$((fallos + 1))
  fi
}

caso "timeout del agente (HTTP=-1)" 'MANTENIMIENTO=error\nHTTP=-1' 5 0
caso "error HTTP raro (500)" 'MANTENIMIENTO=error\nHTTP=500' 5 0
caso "sin respuesta del docker run de estado-agente (salida vacía)" '' 5 0
caso "agente ocupado (409)" 'MANTENIMIENTO=ocupado\nHTTP=409' 2 0
caso "mantenimiento concedido (200)" 'MANTENIMIENTO=ok\nHTTP=200' 0 1
caso "nadie escucha (conexión rechazada, HTTP=0)" 'MANTENIMIENTO=error\nHTTP=0' 0 1

# De punta a punta con el estado-agente.py real: un socket que acepta y nunca contesta.
if command -v python3 >/dev/null 2>&1; then
  python3 -c '
import socket, sys, time
s = socket.socket(); s.bind(("127.0.0.1", 0)); s.listen(8)
print(s.getsockname()[1], flush=True)
conns = []
while True:
    c, _ = s.accept(); conns.append(c)
' >"$T/puerto" &
  PID_MUDO=$!
  disown "$PID_MUDO" 2>/dev/null || true
  for _ in 1 2 3 4 5 6 7 8 9 10; do [[ -s "$T/puerto" ]] && break; /bin/sleep 0.2; done
  caso "agente colgado de verdad (estado-agente.py real, timeout de 6 s)" "@real:$(cat "$T/puerto")" 5 0
  kill "$PID_MUDO" 2>/dev/null; PID_MUDO=""
else
  echo "AVISO: sin python3: se saltea el caso con el estado-agente.py real"
fi

echo
if ((fallos)); then echo "$fallos caso(s) fallaron"; exit 1; fi
echo "cambiar_contenedor: nunca baja el contenedor si el agente puede estar vivo"
