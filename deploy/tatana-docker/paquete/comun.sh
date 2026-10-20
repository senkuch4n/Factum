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

DIR_PAQUETE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONTENEDOR="${TATANA_DOCKER_CONTENEDOR:-tatana-docker}"
DATOS="${TATANA_DOCKER_DATOS:-$HOME/.local/share/tatana-docker}"
EVIDENCIA="${TATANA_DOCKER_EVIDENCIA:-$HOME/Factum/Evidencia}"
INSTALACION_ENV="$DATOS/instalacion.env"
MARCA_USBMUXD="$DATOS/usbmuxd-del-host-enmascarado"
REGLA_UDEV="/etc/udev/rules.d/70-factum-tatana.rules"
PUERTO_DEFAULT=8765

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

# Lee instalacion.env (PUERTO, IMAGEN, ORIGENES, MOCK) si existe.
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
