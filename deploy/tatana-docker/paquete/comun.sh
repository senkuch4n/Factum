# shellcheck shell=bash
# shellcheck disable=SC2034  # variables para los scripts que cargan este archivo
# Funciones y rutas compartidas por los scripts de Tatana en Docker (SDD instalacion-fiscalia §7).
# Se carga con `. comun.sh`; no se ejecuta solo.
#
# Rutas (todas del usuario que corre el script, nunca de root):
#   ~/.local/share/tatana-docker/          configuración y datos del agente (no es la evidencia)
#     appsettings.Local.json               Agent:AllowedOrigins (lo escribe instalar-tatana-docker.sh)
#     instalacion.env                      puerto, imagen y orígenes de la instalación actual
#     agent-data/                          carpeta de trabajo del agente (--data): casos en curso, diarios de sesión
#     home/                                HOME del agente adentro del contenedor (pareos del iPhone)
#     lockdown/                            pareos de usbmuxd (/var/lib/lockdown del contenedor)
#   ~/Factum/Evidencia/                    ZIP de evidencia (la misma carpeta que usa Tatana nativo)
#
# Para probar sin pisar una instalación real se pueden cambiar con variables de entorno:
#   TATANA_DOCKER_CONTENEDOR, TATANA_DOCKER_DATOS, TATANA_DOCKER_EVIDENCIA.
#
# Actualizador automático (SDD fiscalia-operacion §7.1), todo de root:
#   /opt/factum-tatana-docker/kits/<versión>/    kit de cada versión instalada (la actual y la anterior)
#   /opt/factum-tatana-docker/actual             symlink al kit instalado (lo usan las units de systemd)
#   /etc/factum-tatana-docker/                   actualizador.env, instalacion.env (copia canónica), CA de la fiscalía
#   /var/lib/factum-tatana-docker/               estado.env, descargas/, imagenes.txt, candado
#   /var/log/factum-tatana-docker/actualizador.log

DIR_PAQUETE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONTENEDOR="${TATANA_DOCKER_CONTENEDOR:-tatana-docker}"
# Bajo systemd (el actualizador corre como servicio) no hay HOME: con set -u, "$HOME" cortaría el script. Root no usa
# estos dos valores (los toma de /etc/factum-tatana-docker/instalacion.env con leer_instalacion_root).
_CASA="${HOME:-$(getent passwd "$(id -un)" 2>/dev/null | cut -d: -f6)}"
DATOS="${TATANA_DOCKER_DATOS:-${_CASA:-/root}/.local/share/tatana-docker}"
EVIDENCIA="${TATANA_DOCKER_EVIDENCIA:-${_CASA:-/root}/Factum/Evidencia}"
INSTALACION_ENV="$DATOS/instalacion.env"
MARCA_USBMUXD="$DATOS/usbmuxd-del-host-enmascarado"
REGLA_UDEV="/etc/udev/rules.d/70-factum-tatana.rules"
PUERTO_DEFAULT=8765

# Actualizador (§7.1). Las variables FACTUM_TD_* existen solo para las pruebas; en una PC quedan las rutas fijas.
TD_OPT="${FACTUM_TD_OPT:-/opt/factum-tatana-docker}"
TD_ETC="${FACTUM_TD_ETC:-/etc/factum-tatana-docker}"
TD_VAR="${FACTUM_TD_VAR:-/var/lib/factum-tatana-docker}"
TD_LOG="${FACTUM_TD_LOG:-/var/log/factum-tatana-docker}"
TD_SYSTEMD="${FACTUM_TD_SYSTEMD:-/etc/systemd/system}"
INSTALACION_ROOT_ENV="$TD_ETC/instalacion.env"
ACTUALIZADOR_ENV="$TD_ETC/actualizador.env"
ESTADO_ENV="$TD_VAR/estado.env"
CANDADO="$TD_VAR/candado"
UNIDAD_SERVICIO="factum-tatana-actualizador.service"
UNIDAD_TIMER="factum-tatana-actualizador.timer"
CANAL_URL_DEFAULT="https://factum.internal/tatana/docker/"
CA_SISTEMA="${TATANA_DOCKER_CA_SISTEMA:-/usr/local/share/ca-certificates/factum-fiscalia.crt}"
PYTHON_IMAGEN=/opt/tatana/tools/python-embed/bin/python3
CONTENEDOR_ANTERIOR="$CONTENEDOR-anterior"

if [[ -t 1 ]]; then
  _V=$'\e[32m'; _A=$'\e[33m'; _R=$'\e[31m'; _N=$'\e[1m'; _0=$'\e[0m'
else
  _V=""; _A=""; _R=""; _N=""; _0=""
fi
ok()    { echo "  ${_V}OK${_0}  $*"; }
aviso() { echo "  ${_A}AVISO${_0}  $*"; }
falla() { echo "  ${_R}ERROR${_0}  $*" >&2; }
paso()  { echo; echo "${_N}$*${_0}"; }
morir() { falla "$*"; exit 1; }

# Pregunta sí/no. Con SI_A_TODO=1 contesta que sí sin preguntar. Sin terminal (y sin SI_A_TODO) contesta que no.
confirmar() {
  local pregunta="$1" r
  if [[ "${SI_A_TODO:-0}" == 1 ]]; then echo "  $pregunta [s/N] s (--si)"; return 0; fi
  if [[ ! -t 0 ]]; then echo "  $pregunta [s/N] n (sin terminal)"; return 1; fi
  read -r -p "  $pregunta [s/N] " r
  [[ "$r" =~ ^[sSyY] ]]
}

no_root() {
  if [[ "$(id -u)" == 0 ]]; then
    morir "Corré este script con el usuario del perito, sin sudo: él mismo pide la contraseña cuando la necesita."
  fi
}

# DOCKER=(docker) si el usuario habla con el daemon, o (sudo docker) si solo root puede.
DOCKER=()
detectar_docker() {
  command -v docker >/dev/null 2>&1 || morir "Docker no está instalado. Instalalo con instalar-docker-offline.sh del paquete de la fiscalía (o el de tu distro) y volvé a correr este script."
  if docker info >/dev/null 2>&1; then
    DOCKER=(docker)
  elif command -v sudo >/dev/null 2>&1 && sudo docker info >/dev/null 2>&1; then
    DOCKER=(sudo docker)
    aviso "Tu usuario no está en el grupo docker: se usa 'sudo docker'."
  else
    morir "Docker está instalado pero no responde (¿el servicio está apagado? probá 'sudo systemctl start docker')."
  fi
}

sha256_de() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1
  else shasum -a 256 "$1" | cut -d' ' -f1
  fi
}

# Lee instalacion.env (PUERTO, IMAGEN, ORIGENES, MOCK) si existe. Las claves nuevas de fiscalia-operacion §10.5 las lee
# leer_instalacion_root (la copia de root, que es la que usa el actualizador).
leer_instalacion() {
  PUERTO="$PUERTO_DEFAULT"; IMAGEN=""; ORIGENES=""; MOCK=0
  if [[ -f "$INSTALACION_ENV" ]]; then
    # Solo líneas CLAVE=valor conocidas: no se hace `source` de un archivo editable.
    while IFS='=' read -r clave valor; do
      case "$clave" in
        PUERTO) PUERTO="$valor" ;;
        IMAGEN) IMAGEN="$valor" ;;
        ORIGENES) ORIGENES="$valor" ;;
        MOCK) MOCK="$valor" ;;
      esac
    done <"$INSTALACION_ENV"
  fi
}

contenedor_existe() { "${DOCKER[@]}" container inspect "$CONTENEDOR" >/dev/null 2>&1; }

# ¿Hay un usbmuxd del host corriendo? (choca con el del contenedor por el iPhone)
usbmuxd_del_host_activo() {
  if command -v systemctl >/dev/null 2>&1 && systemctl is-active --quiet usbmuxd 2>/dev/null; then return 0; fi
  if command -v pgrep >/dev/null 2>&1 && pgrep -x usbmuxd >/dev/null 2>&1; then
    # El del contenedor también aparece en pgrep del host: se descarta si su PID pertenece a un contenedor.
    local pid
    for pid in $(pgrep -x usbmuxd); do
      grep -qE 'docker|containerd' "/proc/$pid/cgroup" 2>/dev/null || return 0
    done
  fi
  return 1
}

# /health del agente (sin curl ni wget en el host: se pregunta desde adentro del contenedor).
health() {
  local puerto="$1"
  "${DOCKER[@]}" exec "$CONTENEDOR" curl -fsS --max-time 4 "http://127.0.0.1:$puerto/health" 2>/dev/null
}

# ══ Actualizador automático (SDD fiscalia-operacion §7.3) ═══════════════════════════════════════════════════════════

# Valor de CLAVE=valor en un texto (salida de los .py o de un .env), sin source: primera línea que coincide.
clave_de() { # clave_de <texto> <CLAVE>
  local linea
  while IFS= read -r linea; do
    [[ "$linea" == "$2="* ]] && { printf '%s' "${linea#*=}"; return 0; }
  done <<<"$1"
  return 0
}

# Lee la copia canónica de root (/etc/factum-tatana-docker/instalacion.env, §10.5) con lista blanca de claves y valida
# cada valor. Deja PUERTO IMAGEN ORIGENES MOCK USUARIO T_UID T_GID T_GIDS DATOS EVIDENCIA ZONA RED_CON_PUERTOS (UID es
# de solo lectura en bash: por eso T_UID). Devuelve 1 si falta el archivo, una clave obligatoria o algún valor no pasa
# su regex (el actualizador queda "sin_configuracion": nunca inventa valores).
leer_instalacion_root() { # leer_instalacion_root [archivo]
  local f="${1:-$INSTALACION_ROOT_ENV}" clave valor
  PUERTO="$PUERTO_DEFAULT"; IMAGEN=""; ORIGENES=""; MOCK=0; USUARIO=""; T_UID=""; T_GID=""; T_GIDS=""
  DATOS=""; EVIDENCIA=""; ZONA=""; RED_CON_PUERTOS=0; ERROR_INSTALACION=""
  [[ -f "$f" ]] || { ERROR_INSTALACION="no existe $f"; return 1; }
  while IFS='=' read -r clave valor || [[ -n "$clave" ]]; do
    case "$clave" in
      PUERTO) PUERTO="$valor" ;;
      IMAGEN) IMAGEN="$valor" ;;
      ORIGENES) ORIGENES="$valor" ;;
      MOCK) MOCK="$valor" ;;
      USUARIO) USUARIO="$valor" ;;
      UID) T_UID="$valor" ;;
      GID) T_GID="$valor" ;;
      GIDS) T_GIDS="$valor" ;;
      DATOS) DATOS="$valor" ;;
      EVIDENCIA) EVIDENCIA="$valor" ;;
      ZONA) ZONA="$valor" ;;
      RED_CON_PUERTOS) RED_CON_PUERTOS="$valor" ;;
    esac
  done <"$f"
  local ruta='^/[A-Za-z0-9._/@+-]+$'
  if ! [[ "$PUERTO" =~ ^[0-9]{1,5}$ ]] || (( 10#$PUERTO < 1 || 10#$PUERTO > 65535 )); then ERROR_INSTALACION="PUERTO inválido"
  elif [[ -n "$IMAGEN" && ! "$IMAGEN" =~ ^[a-z0-9._/-]+:[A-Za-z0-9._-]+$ ]]; then ERROR_INSTALACION="IMAGEN inválida"
  elif [[ -n "$ORIGENES" && ! "$ORIGENES" =~ ^[A-Za-z0-9:/.,_-]+$ ]]; then ERROR_INSTALACION="ORIGENES inválidos"
  elif [[ "$MOCK" != 0 && "$MOCK" != 1 ]]; then ERROR_INSTALACION="MOCK inválido"
  elif [[ "$RED_CON_PUERTOS" != 0 && "$RED_CON_PUERTOS" != 1 ]]; then ERROR_INSTALACION="RED_CON_PUERTOS inválido"
  elif ! [[ "$USUARIO" =~ ^[a-z_][a-z0-9_-]*$ ]]; then ERROR_INSTALACION="falta USUARIO (instalación anterior al actualizador: correr sumar-actualizador.sh)"
  elif ! [[ "$T_UID" =~ ^[1-9][0-9]*$ && "$T_GID" =~ ^[1-9][0-9]*$ ]]; then ERROR_INSTALACION="faltan UID/GID (instalación anterior al actualizador)"
  elif ! [[ "$T_GIDS" =~ ^[0-9,]*$ ]]; then ERROR_INSTALACION="GIDS inválido"
  elif ! [[ "$DATOS" =~ $ruta && "$DATOS" != *..* ]]; then ERROR_INSTALACION="falta DATOS o no es una ruta absoluta válida"
  elif ! [[ "$EVIDENCIA" =~ $ruta && "$EVIDENCIA" != *..* ]]; then ERROR_INSTALACION="falta EVIDENCIA o no es una ruta absoluta válida"
  elif ! [[ "$ZONA" =~ ^[A-Za-z0-9_+/-]*$ ]]; then ERROR_INSTALACION="ZONA inválida"
  fi
  [[ -z "$ERROR_INSTALACION" ]]
}

# Arma el `docker run` del contenedor de Tatana (antes estaba en línea en instalar-tatana-docker.sh, pasos [6/7]):
# deja RUN (argumentos de docker hasta antes de la imagen) y AGENTE (argumentos del agente). Parametrizado con T_UID,
# T_GID, T_GIDS, DATOS, EVIDENCIA, PUERTO, MOCK, ZONA y RED_CON_PUERTOS (no con id -u ni $HOME): así root lo
# reproduce IDÉNTICO desde instalacion.env. Lo usan el instalador y, con el kit NUEVO, el actualizador (D-T6).
armar_run() { # armar_run <imagen>  (la imagen no entra en RUN: va entre RUN y AGENTE; ver run_completo)
  local config="$DATOS/appsettings.Local.json" regla
  RUN=(run -d --platform linux/amd64 --name "$CONTENEDOR" --restart unless-stopped --label factum.tatana-docker=1
    --security-opt label=disable
    -e "TATANA_UID=$T_UID" -e "TATANA_GID=$T_GID" -e "TATANA_GIDS=$T_GIDS"
    -e "TATANA_PUERTO=$PUERTO" -e "HOME=$DATOS/home" -e "Agent__EvidenceDirectory=$EVIDENCIA"
    -v "$DATOS/agent-data:$DATOS/agent-data" -v "$DATOS/home:$DATOS/home" -v "$EVIDENCIA:$EVIDENCIA"
    -v "$config:/config/appsettings.Local.json:ro")
  [[ -z "$ZONA" ]] || RUN+=(-e "TZ=$ZONA")
  [[ ! -f /etc/localtime ]] || RUN+=(-v /etc/localtime:/etc/localtime:ro)
  AGENTE=(--port "$PUERTO" --data "$DATOS/agent-data" --local-config /config/appsettings.Local.json)
  if [[ "$RED_CON_PUERTOS" == 1 ]]; then
    # Docker Desktop: el agente escucha en todas las interfaces DEL CONTENEDOR y Docker publica solo en 127.0.0.1.
    RUN+=(-p "127.0.0.1:$PUERTO:$PUERTO")
    AGENTE+=(--bind 0.0.0.0)
  else
    # La web llama a http://localhost:8765 del host: el agente escucha en el localhost del host. Además recibe los
    # avisos de enchufado/desenchufado de USB (netlink), que viven en el espacio de red del host.
    RUN+=(--network host)
  fi
  if [[ "$MOCK" == 1 ]]; then
    RUN+=(-e TATANA_USBMUXD=0)
    AGENTE+=(--mock)
  else
    # USB sin --privileged: el árbol /dev/bus/usb del host (los celulares que se enchufen después aparecen solos) y
    # permiso de cgroup solo para los dispositivos USB (major 189). /run/udev para que libusb (usbmuxd) vea el
    # enchufado en caliente. Los pareos del iPhone persisten en $DATOS/lockdown.
    RUN+=(-e TATANA_USBMUXD=1 -v /dev/bus/usb:/dev/bus/usb --device-cgroup-rule 'c 189:* rmw'
      -v "$DATOS/lockdown:/var/lib/lockdown")
    [[ ! -d /run/udev ]] || RUN+=(-v /run/udev:/run/udev:ro)
    # Las reglas USB de Android del host, de solo lectura: el chequeo "android_usb" de /health (Esta PC) las busca en
    # el sistema de archivos del agente, y sin ellas la web diría que falta el permiso aunque el host lo tenga.
    for regla in "$REGLA_UDEV" /etc/udev/rules.d/51-android.rules /usr/lib/udev/rules.d/51-android.rules \
                 /lib/udev/rules.d/51-android.rules; do
      [[ ! -f "$regla" ]] || RUN+=(-v "$regla:$regla:ro")
    done
  fi
}

# RUN_COMPLETO = RUN + imagen + AGENTE (lo que se le pasa a docker). Con las variables del shell actual.
run_completo() { # run_completo <imagen>
  armar_run "$1"
  RUN_COMPLETO=("${RUN[@]}" "$1" "${AGENTE[@]}")
}

# RUN_COMPLETO armado con el comun.sh de OTRO kit (el nuevo, ya verificado; D-T6) y la instalacion.env de root. En un
# bash aparte para no mezclar funciones de dos versiones; los argumentos vuelven separados por NUL.
run_completo_de_kit() { # run_completo_de_kit <dir del kit> <imagen>
  local arg salida
  RUN_COMPLETO=()
  salida="$(mktemp)"
  if ! FACTUM_TD_ETC="$TD_ETC" TATANA_DOCKER_CONTENEDOR="$CONTENEDOR" bash -c '
      set -euo pipefail
      . "$1/comun.sh"
      leer_instalacion_root || { echo "$ERROR_INSTALACION" >&2; exit 3; }
      run_completo "$2"
      printf "%s\0" "${RUN_COMPLETO[@]}"' _ "$1" "$2" >"$salida"; then
    rm -f "$salida"
    return 1
  fi
  while IFS= read -r -d '' arg; do RUN_COMPLETO+=("$arg"); done <"$salida"
  rm -f "$salida"
  [[ ${#RUN_COMPLETO[@]} -gt 3 ]]
}

# Precedencia semver 2.0 (prerelease < release; identificadores numéricos vs alfanuméricos). Imprime -1, 0 o 1.
# Misma tabla de casos que el Python de verificar-manifiesto-docker.py (ci/tatana-docker/versiones.txt, §10.8).
version_cmp() { # version_cmp A B
  local LC_ALL=C a="${1%%+*}" b="${2%%+*}" na pa nb pb i x y
  na="${a%%-*}"; nb="${b%%-*}"
  pa=""; pb=""
  [[ "$a" == *-* ]] && pa="${a#*-}"
  [[ "$b" == *-* ]] && pb="${b#*-}"
  local -a A B
  IFS=. read -r -a A <<<"$na"
  IFS=. read -r -a B <<<"$nb"
  for i in 0 1 2; do
    x="${A[$i]:-0}"; y="${B[$i]:-0}"
    [[ "$x" =~ ^[0-9]+$ ]] || x=0
    [[ "$y" =~ ^[0-9]+$ ]] || y=0
    if (( 10#$x > 10#$y )); then echo 1; return; fi
    if (( 10#$x < 10#$y )); then echo -1; return; fi
  done
  if [[ -z "$pa" && -z "$pb" ]]; then echo 0; return; fi
  if [[ -z "$pa" ]]; then echo 1; return; fi
  if [[ -z "$pb" ]]; then echo -1; return; fi
  IFS=. read -r -a A <<<"$pa"
  IFS=. read -r -a B <<<"$pb"
  for ((i = 0; i < ${#A[@]} && i < ${#B[@]}; i++)); do
    x="${A[$i]}"; y="${B[$i]}"
    if [[ "$x" =~ ^[0-9]+$ && "$y" =~ ^[0-9]+$ ]]; then
      if (( 10#$x > 10#$y )); then echo 1; return; fi
      if (( 10#$x < 10#$y )); then echo -1; return; fi
    elif [[ "$x" =~ ^[0-9]+$ ]]; then echo -1; return
    elif [[ "$y" =~ ^[0-9]+$ ]]; then echo 1; return
    elif [[ "$x" > "$y" ]]; then echo 1; return
    elif [[ "$x" < "$y" ]]; then echo -1; return
    fi
  done
  if (( ${#A[@]} > ${#B[@]} )); then echo 1; elif (( ${#A[@]} < ${#B[@]} )); then echo -1; else echo 0; fi
}

# flock -n sobre /var/lib/factum-tatana-docker/candado, con un fd de SOLO LECTURA (el perito también puede tomarlo).
# 0 = tomado (o no hay con qué competir: el archivo todavía no existe o no hay flock); 1 = lo tiene otro proceso.
tomar_candado() {
  [[ -f "$CANDADO" ]] || return 0
  command -v flock >/dev/null 2>&1 || return 0
  exec 9<"$CANDADO" || return 0
  flock -n 9
}

# estado-agente.py con el Python de una imagen de Tatana (la instalada: el host no necesita curl, wget ni Python).
#   IMAGEN_PY  imagen con la que se corre (default: la del contenedor actual)
#   KIT_PY     carpeta con estado-agente.py (default: la de este comun.sh)
# Con RED_CON_PUERTOS=1 (solo pruebas en Docker Desktop) se usa la red del contenedor en vez de la del host.
# FACTUM_TATANA_ESTADO_FIXTURE (SOLO PRUEBAS): /agent/state sale de ese archivo.
estado_agente() { # estado_agente <accion> [--ttl N]
  local img="${IMAGEN_PY:-}" kit="${KIT_PY:-$DIR_PAQUETE}" red=(--network host) fx=()
  [[ -n "$img" ]] || img="$("${DOCKER[@]}" container inspect -f '{{.Config.Image}}' "$CONTENEDOR" 2>/dev/null || true)"
  [[ -n "$img" ]] || { echo "INALCANZABLE=1"; return 3; }
  [[ "${RED_CON_PUERTOS:-0}" == 1 ]] && red=(--network "container:$CONTENEDOR")
  if [[ -n "${FACTUM_TATANA_ESTADO_FIXTURE:-}" && -f "${FACTUM_TATANA_ESTADO_FIXTURE}" ]]; then
    fx=(-v "$FACTUM_TATANA_ESTADO_FIXTURE:/fixture.json:ro")
  fi
  "${DOCKER[@]}" run --rm --platform linux/amd64 "${red[@]}" --security-opt label=disable ${fx[@]+"${fx[@]}"} \
    -v "$kit:/k:ro" --entrypoint "$PYTHON_IMAGEN" "$img" -I /k/estado-agente.py --puerto "$PUERTO" --accion "$@" \
    ${fx[@]+--fixture /fixture.json} 2>/dev/null
}

# Registro del cambio de contenedor: el actualizador lo redefine (log propio); acá va a la terminal.
registrar() { echo "  $*"; }

# Espera hasta <segundos> a que /health responda con <versión> (vacía = cualquiera). 0 = respondió.
esperar_version() { # esperar_version <versión esperada> <segundos>
  local esperada="$1" limite=$((SECONDS + $2)) salida v
  while (( SECONDS < limite )); do
    if [[ "$("${DOCKER[@]}" container inspect -f '{{.State.Running}}' "$CONTENEDOR" 2>/dev/null)" != true ]]; then
      # Un contenedor que se cae al arrancar (reinicio automático) se espera un poco más; uno que no existe, no.
      "${DOCKER[@]}" container inspect "$CONTENEDOR" >/dev/null 2>&1 || return 1
    else
      salida="$(IMAGEN_PY="${IMAGEN_ESPERA:-}" estado_agente version || true)"
      v="$(clave_de "$salida" VERSION)"
      if [[ -n "$v" && ( -z "$esperada" || "$v" == "$esperada" ) ]]; then VERSION_RESPONDIO="$v"; return 0; fi
      [[ -n "$v" ]] && VERSION_RESPONDIO="$v"
    fi
    sleep 3
  done
  return 1
}

# Cambio de contenedor con vuelta atrás instantánea (§7.6). Usa RUN_COMPLETO (ya armado por quien llama: run_completo o
# run_completo_de_kit). Devuelve:
#   0 cambiado y respondiendo con <versión esperada>
#   2 Tatana no está libre (no se tocó nada)        → esperando_libre
#   3 la versión nueva no arrancó: se volvió a la anterior (sigue andando la anterior) → no_arranca
#   4 el contenedor existente no es de Tatana en Docker (nunca se toca)
#   5 error del agente al pedir mantenimiento (no se tocó nada) → error_agente
#   6 la nueva no arrancó y la anterior TAMPOCO volvió a responder
# FORZAR_CAMBIO=1 saltea el pedido de mantenimiento (instalar-tatana-docker.sh --forzar).
cambiar_contenedor() { # cambiar_contenedor <imagen nueva> <versión esperada> <segundos>
  local imagen="$1" esperada="$2" segundos="$3" etiqueta salida m hay_anterior=0 version_vieja=""
  VERSION_RESPONDIO=""
  MOTIVO_OCUPADO=""
  if "${DOCKER[@]}" container inspect "$CONTENEDOR" >/dev/null 2>&1; then
    etiqueta="$("${DOCKER[@]}" container inspect -f '{{index .Config.Labels "factum.tatana-docker"}}' "$CONTENEDOR" 2>/dev/null || true)"
    [[ "$etiqueta" == 1 ]] || return 4
    if [[ "${FORZAR_CAMBIO:-0}" != 1 && "$("${DOCKER[@]}" container inspect -f '{{.State.Running}}' "$CONTENEDOR" 2>/dev/null)" == true ]]; then
      salida="$(estado_agente mantenimiento --ttl 300 || true)"
      m="$(clave_de "$salida" MANTENIMIENTO)"
      case "$m" in
        ok) ;;
        ocupado) MOTIVO_OCUPADO="$(clave_de "$(estado_agente estado || true)" MOTIVOS)"; return 2 ;;
        *)
          # Solo si NADIE escucha en el puerto (conexión rechazada → HTTP=0) no hay captura que proteger y se sigue.
          # Timeout, reset, HTTP raro (HTTP=-1 u otro) o que no se pudo ni preguntar: el agente puede estar vivo y
          # capturando → no se toca nada (error_agente).
          [[ "$(clave_de "$salida" HTTP)" == 0 ]] || return 5
          registrar "nadie escucha en el puerto $PUERTO (conexión rechazada): se cambia igual (no hay captura que proteger)" ;;
      esac
    fi
    version_vieja="$(clave_de "$(estado_agente version 2>/dev/null || true)" VERSION)"
    "${DOCKER[@]}" rm -f "$CONTENEDOR_ANTERIOR" >/dev/null 2>&1 || true
    "${DOCKER[@]}" stop -t 30 "$CONTENEDOR" >/dev/null 2>&1 || true
    if ! "${DOCKER[@]}" rename "$CONTENEDOR" "$CONTENEDOR_ANTERIOR" >/dev/null; then
      "${DOCKER[@]}" start "$CONTENEDOR" >/dev/null 2>&1 || true
      return 5
    fi
    hay_anterior=1
    # SOLO PRUEBAS (corte de luz a mitad del cambio, fiscalia-operacion §14.2): muere acá, entre rename y run.
    [[ "${FACTUM_TATANA_PRUEBA_CORTE:-}" != despues_rename ]] || kill -9 $$
  fi
  if "${DOCKER[@]}" "${RUN_COMPLETO[@]}" >/dev/null && IMAGEN_ESPERA="$imagen" esperar_version "$esperada" "$segundos"; then
    ((hay_anterior)) && "${DOCKER[@]}" rm -f "$CONTENEDOR_ANTERIOR" >/dev/null 2>&1
    return 0
  fi
  registrar "la versión nueva ($imagen) no respondió${esperada:+ con la versión $esperada}${VERSION_RESPONDIO:+ (respondió $VERSION_RESPONDIO)}. Últimas líneas de su registro:"
  "${DOCKER[@]}" logs --tail 60 "$CONTENEDOR" 2>&1 | while IFS= read -r l; do registrar "  | $l"; done || true
  "${DOCKER[@]}" rm -f "$CONTENEDOR" >/dev/null 2>&1 || true
  ((hay_anterior)) || return 3
  "${DOCKER[@]}" rename "$CONTENEDOR_ANTERIOR" "$CONTENEDOR" >/dev/null 2>&1 || return 6
  "${DOCKER[@]}" start "$CONTENEDOR" >/dev/null 2>&1 || return 6
  IMAGEN_ESPERA="" esperar_version "$version_vieja" 120 || return 6
  return 3
}

# Texto en castellano de un código de resultado (§10.7).
texto_resultado() { # texto_resultado <RESULTADO> [versión] [detalle]
  case "$1" in
    al_dia) echo "al día" ;;
    actualizada) echo "actualizada a v${2:-?}" ;;
    esperando_libre) echo "esperando a que termine la captura" ;;
    sin_servidor) echo "no se pudo llegar al servidor" ;;
    manifiesto_invalido|clave_desconocida|firma_invalida) echo "manifiesto rechazado ($1)" ;;
    hash_invalido|imagen_invalida) echo "descarga dañada (se descartó)" ;;
    sin_espacio) echo "sin espacio en disco${3:+ ($3)}" ;;
    autoprueba_fallida) echo "la versión nueva no pasó la autoprueba (no se instaló)" ;;
    no_arranca) echo "la versión nueva no arrancó: se volvió a la anterior" ;;
    version_fallida) echo "la versión publicada ya falló en esta PC (se espera una mayor)" ;;
    vuelta_manual) echo "se volvió a la versión anterior a mano" ;;
    recuperado) echo "se recuperó un cambio interrumpido" ;;
    "") echo "(todavía no consultó)" ;;
    *) echo "$1${3:+ ($3)}" ;;
  esac
}
