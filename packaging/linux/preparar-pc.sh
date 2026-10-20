#!/usr/bin/env bash
# "Preparar esta PC" de Tatana para Linux (SDD tatana-linux §7, contrato con agent-ui en §10.B).
#
# Deja la PC lista para que Tatana reconozca iPhone y Android por USB sin sudo: instala usbmuxd y avahi (y uxplay
# solo si el repo trae la 1.73 o posterior), escribe una regla udev con uaccess para Android, abre los puertos de
# AirPlay si hay firewall activo y, si AppArmor restringe los user namespaces (Ubuntu 23.10+), instala un perfil
# para la ventana de Tatana. Funciona con apt (Debian/Ubuntu/Mint) y dnf (Fedora). Con otro gestor no ejecuta nada.
#
# agent-ui lo lleva embebido y lo corre por stdin (no queda un archivo temporal que se pueda cambiar):
#   bash -s -- --simular                                                (sin contraseña: plan)
#   pkexec /bin/bash -s -- --ejecutar --usuario <login> --version X.Y.Z (una sola contraseña)
# Fallback sin polkit: sudo bash ~/.config/Tatana/preparar-pc.sh --ejecutar --usuario <login>
#
# Salida (una línea JSON por evento, prefijo + un espacio):
#   TATANA_PLAN <json>       (--simular)
#   TATANA_PASO <json>       (--ejecutar: al empezar y al terminar cada paso)
#   TATANA_RESULTADO <json>  (--ejecutar: al final)
# El resto de la salida es log.
#
# Códigos de salida: 0 ok · 1 algún paso falló (parcial) · 2 argumentos · 3 distro sin soporte · 4 sin internet ·
# 5 sin root. (pkexec agrega 126 = cancelado y 127 = sin polkit.)
#
# Pruebas: TATANA_RAIZ_PRUEBA=<dir> antepone <dir> a las rutas que se leen (etc/os-release, binarios, /proc…) y solo
# vale con --simular (packaging/linux/tests/probar-preparar-pc.sh). Idempotente: correrlo dos veces deja lo mismo.
#
# Todo el cuerpo está en main(), que se llama en la última línea: así bash lee el script completo antes de ejecutar
# nada, y ningún comando puede comerse el resto del script desde stdin (además, todos reciben </dev/null).
set -uo pipefail

main() {
  export LC_ALL=C
  export DEBIAN_FRONTEND=noninteractive
  export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin${PATH:+:$PATH}"

  local SCHEMA=1
  local REGLA_UDEV="/etc/udev/rules.d/70-factum-tatana.rules"
  local PERFIL_APPARMOR="/etc/apparmor.d/factum-tatana"
  local SYSCTL_USERNS="/proc/sys/kernel/apparmor_restrict_unprivileged_userns"
  local UXPLAY_MINIMA="1.73"
  local APT_LOCK="-o DPkg::Lock::Timeout=120"

  local modo="" usuario="" version=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --simular) modo="simular"; shift ;;
      --ejecutar) modo="ejecutar"; shift ;;
      --usuario) usuario="${2:-}"; [[ -n "$usuario" ]] || { log "ERROR: --usuario vacío."; return 2; }; shift 2 ;;
      --version) version="${2:-}"; shift 2 ;;
      -h|--help) uso; return 2 ;;
      *) log "ERROR: argumento desconocido: $1"; uso; return 2 ;;
    esac
  done
  [[ -n "$modo" ]] || { uso; return 2; }

  RAIZ="${TATANA_RAIZ_PRUEBA:-}"
  RAIZ="${RAIZ%/}"
  if [[ "$modo" == "ejecutar" ]]; then
    [[ -n "$usuario" ]] || { log "ERROR: --ejecutar necesita --usuario <login>."; return 2; }
    [[ -z "$RAIZ" ]] || { log "ERROR: TATANA_RAIZ_PRUEBA solo vale con --simular."; return 2; }
    if [[ "$(id -u)" != "0" ]]; then
      log "ERROR: --ejecutar necesita permisos de administrador (pkexec o sudo)."
      return 5
    fi
    local entrada
    entrada="$(getent passwd "$usuario" </dev/null || true)"
    [[ -n "$entrada" ]] || { log "ERROR: el usuario '$usuario' no existe en esta PC."; return 2; }
    if [[ -n "${PKEXEC_UID:-}" ]]; then
      local uid_usuario
      uid_usuario="$(cut -d: -f3 <<<"$entrada")"
      if [[ "$uid_usuario" != "$PKEXEC_UID" ]]; then
        log "ERROR: --usuario '$usuario' (uid $uid_usuario) no es quien pidió la contraseña (uid $PKEXEC_UID)."
        return 2
      fi
    fi
  fi

  detectar
  log "Tatana: preparar esta PC (${version:-sin versión}) en ${DISTRO_NAME:-$DISTRO_ID} ${DISTRO_VERSION}, gestor $GESTOR, modo $modo"

  if [[ "$modo" == "simular" ]]; then
    echo "TATANA_PLAN $(plan_json)"
    [[ "$SOPORTADA" == "true" ]] && return 0 || return 3
  fi

  if [[ "$SOPORTADA" != "true" ]]; then
    log "Distro sin soporte (gestor $GESTOR): no se cambia nada. Comandos para soporte técnico:"
    echo "TATANA_PLAN $(plan_json)"
    resultado_json "sin_soporte" "[]"
    return 3
  fi

  ejecutar "$usuario"
}

uso() {
  echo "Uso: preparar-pc.sh --simular | --ejecutar --usuario <login> [--version X.Y.Z]" >&2
}

log() { echo "$*"; }

# ── Rutas y binarios (con TATANA_RAIZ_PRUEBA en las pruebas) ─────────────────────────────────────────────────

r() { echo "${RAIZ}$1"; }

existe() { [[ -e "$(r "$1")" ]]; }

# Ruta del binario $1 (en la raíz de prueba: solo los de <raíz>/{usr/,}{s,}bin; en la PC real: command -v).
bin() {
  local n="$1" d
  if [[ -n "$RAIZ" ]]; then
    for d in usr/local/sbin usr/local/bin usr/sbin usr/bin sbin bin; do
      [[ -x "$RAIZ/$d/$n" ]] && { echo "$RAIZ/$d/$n"; return 0; }
    done
    return 1
  fi
  command -v "$n" 2>/dev/null
}

# ── JSON sin dependencias (sin jq) ─────────────────────────────────────────────────────────────────────────

json_str() {
  local s="$1" out="" c i code
  for ((i = 0; i < ${#s}; i++)); do
    c="${s:i:1}"
    case "$c" in
      '"') out+='\"' ;;
      '\') out+='\\' ;;
      $'\n') out+='\n' ;;
      $'\r') out+='\r' ;;
      $'\t') out+='\t' ;;
      *)
        printf -v code '%d' "'$c" 2>/dev/null || code=32
        if (( code >= 0 && code < 32 )); then
          printf -v c '\\u%04x' "$code"
        fi
        out+="$c"
        ;;
    esac
  done
  printf '"%s"' "$out"
}

# Array JSON de strings: json_arr a b c → ["a","b","c"]
json_arr() {
  local out="[" primero=1 x
  for x in "$@"; do
    (( primero )) || out+=","
    out+="$(json_str "$x")"
    primero=0
  done
  printf '%s]' "$out"
}

json_bool() { [[ "$1" == "true" ]] && printf 'true' || printf 'false'; }

# ── Detección ─────────────────────────────────────────────────────────────────────────────────────────────

# Valor de CLAVE en os-release (sin comillas).
os_release_valor() {
  local clave="$1" archivo linea v
  for archivo in /etc/os-release /usr/lib/os-release; do
    [[ -f "$(r "$archivo")" ]] || continue
    while IFS= read -r linea || [[ -n "$linea" ]]; do
      linea="${linea%$'\r'}"
      [[ "$linea" == "$clave="* ]] || continue
      v="${linea#*=}"
      if [[ "$v" == \"*\" && ${#v} -ge 2 ]]; then v="${v:1:${#v}-2}"; v="${v//\\\"/\"}"
      elif [[ "$v" == \'*\' && ${#v} -ge 2 ]]; then v="${v:1:${#v}-2}"
      fi
      echo "$v"
      return 0
    done <"$(r "$archivo")"
    return 0
  done
}

# Versión "1.68-1ubuntu1" / "2:1.73.2-1" / "1.73" → "1.68" / "1.73.2" / "1.73" (sin época ni revisión).
version_base() {
  local v="$1"
  v="${v#*:}"
  v="${v%%-*}"
  v="${v%%+*}"
  v="${v%%~*}"
  echo "$v"
}

# version_ge A B: A ≥ B comparando los números separados por punto.
version_ge() {
  local a b i x y
  IFS=. read -r -a a <<<"$(version_base "$1")"
  IFS=. read -r -a b <<<"$2"
  for ((i = 0; i < ${#a[@]} || i < ${#b[@]}; i++)); do
    x="${a[i]:-0}"; y="${b[i]:-0}"
    x="${x//[^0-9]/}"; y="${y//[^0-9]/}"
    x=$((10#${x:-0})); y=$((10#${y:-0}))
    (( x > y )) && return 0
    (( x < y )) && return 1
  done
  return 0
}

uxplay_candidata() {
  local herramienta salida
  case "$GESTOR" in
    apt)
      herramienta="$(bin apt-cache)" || return 0
      salida="$("$herramienta" policy uxplay </dev/null 2>/dev/null || true)"
      salida="$(sed -n 's/^[[:space:]]*Candidate:[[:space:]]*//p' <<<"$salida" | head -1)"
      [[ "$salida" == "(none)" ]] && salida=""
      echo "$salida"
      ;;
    dnf)
      herramienta="$(bin dnf)" || return 0
      # dnf5 no agrega el salto de línea al --qf (dnf4 sí): se pide explícito y se sacan las líneas vacías.
      "$herramienta" -q repoquery --qf '%{version}\n' uxplay </dev/null 2>/dev/null | sed '/^[[:space:]]*$/d' \
        | sort -t. -k1,1n -k2,2n -k3,3n | tail -1
      ;;
  esac
}

ufw_activo() {
  local ufw
  ufw="$(bin ufw)" || return 1
  if [[ -z "$RAIZ" && "$(id -u)" == "0" ]]; then
    "$ufw" status </dev/null 2>/dev/null | grep -q '^Status: active'
    return
  fi
  # Sin root, `ufw status` no responde: se mira la config.
  grep -Eqs '^[[:space:]]*ENABLED[[:space:]]*=[[:space:]]*yes' "$(r /etc/ufw/ufw.conf)"
}

firewalld_activo() {
  local fc
  fc="$(bin firewall-cmd)" || return 1
  [[ "$("$fc" --state </dev/null 2>/dev/null)" == "running" ]]
}

uaccess_tiene_video() {
  local d
  for d in /usr/lib/udev/rules.d /lib/udev/rules.d; do
    grep -qs 'video4linux' "$(r "$d/70-uaccess.rules")" && return 0
  done
  return 1
}

apparmor_aplica() {
  local f
  f="$(r "$SYSCTL_USERNS")"
  [[ -r "$f" && "$(tr -d '[:space:]' <"$f" 2>/dev/null)" == "1" ]] && bin apparmor_parser >/dev/null
}

detectar() {
  DISTRO_ID="$(os_release_valor ID | tr '[:upper:]' '[:lower:]')"
  DISTRO_ID="${DISTRO_ID:-linux}"
  DISTRO_VERSION="$(os_release_valor VERSION_ID)"
  DISTRO_NAME="$(os_release_valor PRETTY_NAME)"

  if existe /usr/bin/apt-get; then GESTOR="apt"
  elif existe /usr/bin/dnf; then GESTOR="dnf"
  elif existe /usr/bin/pacman; then GESTOR="pacman"
  elif existe /usr/bin/zypper; then GESTOR="zypper"
  else GESTOR="unknown"
  fi
  [[ "$GESTOR" == "apt" || "$GESTOR" == "dnf" ]] && SOPORTADA="true" || SOPORTADA="false"

  PAQUETES=()
  case "$GESTOR" in
    apt) PAQUETES=(usbmuxd avahi-daemon) ;;
    *) PAQUETES=(usbmuxd avahi) ;;
  esac

  UXPLAY_CANDIDATA=""
  UXPLAY_INSTALAR="false"
  if [[ "$SOPORTADA" == "true" ]]; then
    UXPLAY_CANDIDATA="$(uxplay_candidata)"
    if [[ -n "$UXPLAY_CANDIDATA" ]] && version_ge "$UXPLAY_CANDIDATA" "$UXPLAY_MINIMA"; then
      UXPLAY_INSTALAR="true"
      PAQUETES+=(uxplay)
    fi
  fi

  if ufw_activo; then FIREWALL="ufw"
  elif firewalld_activo; then FIREWALL="firewalld"
  else FIREWALL="inactivo"
  fi

  GRUPOS=()
  uaccess_tiene_video || GRUPOS=(video)

  ARCHIVOS=("$REGLA_UDEV")
  if apparmor_aplica || [[ "$SOPORTADA" != "true" ]]; then
    ARCHIVOS+=("$PERFIL_APPARMOR")
  fi
}

# ── Contenido exacto de los archivos (§7.2) ───────────────────────────────────────────────────────────────

contenido_regla_udev() {
  cat <<'REGLA'
# Factum Tatana: acceso a celulares Android por USB (adb) para el usuario de la sesión activa (logind, uaccess).
# Lo escribe "Preparar esta PC" de Tatana. Se puede borrar: Tatana lo vuelve a crear si hace falta.
SUBSYSTEM=="usb", ENV{DEVTYPE}=="usb_device", ENV{ID_USB_INTERFACES}=="*:ff4201:*", TAG+="uaccess"
REGLA
}

contenido_perfil_apparmor() {
  cat <<'PERFIL'
# Factum Tatana: permite que Chromium (la ventana de Tatana) use su sandbox con user namespaces (Ubuntu 23.10+).
abi <abi/4.0>,
include <tunables/global>

profile factum-tatana @{HOME}/Applications/Tatana.AppImage flags=(unconfined) {
  userns,
  include if exists <local/factum-tatana>
}
PERFIL
}

# ── Plan (TATANA_PLAN) ────────────────────────────────────────────────────────────────────────────────────

comandos_plan() {
  COMANDOS=()
  local usuario_txt="<usuario>"
  case "$GESTOR" in
    apt)
      COMANDOS+=("apt-get $APT_LOCK update")
      COMANDOS+=("apt-get $APT_LOCK install -y --no-install-recommends ${PAQUETES[*]}")
      ;;
    dnf)
      COMANDOS+=("dnf install -y ${PAQUETES[*]}")
      ;;
    *)
      COMANDOS+=("Instalar con el gestor de paquetes de la distro: usbmuxd avahi (y uxplay 1.73 o posterior si se quiere AirPlay)")
      ;;
  esac
  COMANDOS+=("cat > $REGLA_UDEV <<'EOF'
$(contenido_regla_udev)
EOF")
  COMANDOS+=("udevadm control --reload-rules")
  COMANDOS+=("udevadm trigger --action=change --subsystem-match=usb")
  COMANDOS+=("systemctl enable --now avahi-daemon")
  case "$FIREWALL" in
    ufw)
      COMANDOS+=("ufw allow 5353/udp comment 'Factum Tatana AirPlay'")
      COMANDOS+=("ufw allow 7000,7001,7100/tcp comment 'Factum Tatana AirPlay'")
      COMANDOS+=("ufw allow 6000,6001,7011/udp comment 'Factum Tatana AirPlay'")
      ;;
    firewalld)
      COMANDOS+=("firewall-cmd --permanent --add-service=mdns")
      COMANDOS+=("firewall-cmd --permanent --add-port=7000/tcp --add-port=7001/tcp --add-port=7100/tcp")
      COMANDOS+=("firewall-cmd --permanent --add-port=6000/udp --add-port=6001/udp --add-port=7011/udp")
      COMANDOS+=("firewall-cmd --reload")
      ;;
  esac
  if [[ ${#GRUPOS[@]} -gt 0 ]]; then
    COMANDOS+=("usermod -aG video $usuario_txt")
  fi
  if [[ " ${ARCHIVOS[*]} " == *" $PERFIL_APPARMOR "* ]]; then
    COMANDOS+=("cat > $PERFIL_APPARMOR <<'EOF'
$(contenido_perfil_apparmor)
EOF")
    COMANDOS+=("apparmor_parser -r $PERFIL_APPARMOR")
  fi
}

plan_json() {
  comandos_plan
  local candidata="null"
  [[ -n "$UXPLAY_CANDIDATA" ]] && candidata="$(json_str "$UXPLAY_CANDIDATA")"
  local servicios=(avahi-daemon)
  printf '{"schema":%d,"soportada":%s,"distro_id":%s,"distro_version":%s,"distro_name":%s,"gestor":%s,"paquetes":%s,"uxplay":{"candidata":%s,"instalar":%s},"archivos":%s,"servicios":%s,"firewall":%s,"grupos":%s,"comandos":%s}' \
    "$SCHEMA" "$(json_bool "$SOPORTADA")" "$(json_str "$DISTRO_ID")" "$(json_str "$DISTRO_VERSION")" \
    "$(json_str "$DISTRO_NAME")" "$(json_str "$GESTOR")" "$(json_arr "${PAQUETES[@]}")" \
    "$candidata" "$(json_bool "$UXPLAY_INSTALAR")" "$(json_arr "${ARCHIVOS[@]}")" \
    "$(json_arr "${servicios[@]}")" "$(json_str "$FIREWALL")" "$(json_arr "${GRUPOS[@]+"${GRUPOS[@]}"}")" \
    "$(json_arr "${COMANDOS[@]}")"
}

# ── Ejecución (TATANA_PASO / TATANA_RESULTADO) ────────────────────────────────────────────────────────────

PASOS_JSON=()

paso() { # paso <id> <estado> <detalle>
  local j
  j="$(printf '{"id":%s,"estado":%s,"detalle":%s}' "$(json_str "$1")" "$(json_str "$2")" "$(json_str "$3")")"
  echo "TATANA_PASO $j"
  [[ "$2" == "en_curso" ]] || PASOS_JSON+=("$j")
}

resultado_json() { # resultado_json <resultado> <pasos-json-array>
  printf 'TATANA_RESULTADO {"schema":%d,"resultado":%s,"requiere_reiniciar_sesion":%s,"pasos":%s}\n' \
    "$SCHEMA" "$(json_str "$1")" "$(json_bool "${REQUIERE_REINICIAR:-false}")" "$2"
}

# Corre un comando, lo deja en el log y guarda su salida en ULTIMA_SALIDA. Devuelve su código.
correr() {
  local codigo
  log "\$ $*"
  if ! command -v "$1" >/dev/null 2>&1; then
    ULTIMA_SALIDA="Falta el comando $1 en esta PC."
    log "$ULTIMA_SALIDA"
    return 127
  fi
  ULTIMA_SALIDA="$("$@" </dev/null 2>&1)"
  codigo=$?
  [[ -n "$ULTIMA_SALIDA" ]] && printf '%s\n' "$ULTIMA_SALIDA" | sed 's/^TATANA_/tatana_/'
  return $codigo
}

# Última línea útil de la salida del último comando (detalle de un error).
motivo() {
  local m
  m="$(printf '%s\n' "${ULTIMA_SALIDA:-}" | sed '/^[[:space:]]*$/d' | tail -1 | cut -c1-200)"
  echo "${m:-código de salida $1}"
}

# Escribe $2 (contenido) en $1 de forma atómica, 0644.
escribir_archivo() {
  local destino="$1" contenido="$2" tmp
  mkdir -p "$(dirname "$destino")" || return 1
  tmp="$(mktemp "$destino.tatana.XXXXXX")" || return 1
  printf '%s\n' "$contenido" >"$tmp" && chmod 0644 "$tmp" && mv -f "$tmp" "$destino" || { rm -f "$tmp"; return 1; }
  log "escrito $destino"
}

sin_internet_en() { # <salida de un comando>
  grep -Eqi 'Temporary failure resolving|Could not resolve|Curl error \(6\)|Cannot download repomd|Failed to download metadata|Network is unreachable|Could not connect to' <<<"${1:-}"
}

ejecutar() {
  local usuario="$1" errores=0 sin_red=0 codigo
  REQUIERE_REINICIAR="false"

  # 1. Paquetes
  paso paquetes en_curso "${PAQUETES[*]}"
  if [[ "$GESTOR" == "apt" ]]; then
    # Si update falla se sigue igual: un "cdrom:" en sources.list no implica falta de internet.
    # shellcheck disable=SC2086
    correr apt-get $APT_LOCK update || log "apt-get update falló; se sigue con la instalación"
    SALIDA_UPDATE="${ULTIMA_SALIDA:-}"
    # shellcheck disable=SC2086
    correr apt-get $APT_LOCK install -y --no-install-recommends "${PAQUETES[@]}"
    codigo=$?
  else
    correr dnf install -y "${PAQUETES[@]}"
    codigo=$?
  fi
  if [[ $codigo -eq 0 ]]; then
    paso paquetes ok "${PAQUETES[*]}"
  elif sin_internet_en "$ULTIMA_SALIDA" || sin_internet_en "${SALIDA_UPDATE:-}"; then
    # Sin red, apt-get update no baja las listas y el install termina en "Unable to locate package": cuenta como
    # falta de internet si el update falló por la red.
    sin_red=1
    paso paquetes error "Sin internet: no se pudieron descargar los paquetes del sistema."
  else
    errores=$((errores + 1))
    paso paquetes error "$(motivo $codigo)"
  fi

  # 2. Reglas USB (Android con uaccess; iPhone lo resuelve el paquete usbmuxd)
  paso reglas_usb en_curso "$REGLA_UDEV"
  if escribir_archivo "$REGLA_UDEV" "$(contenido_regla_udev)" \
     && correr udevadm control --reload-rules \
     && correr udevadm trigger --action=change --subsystem-match=usb; then
    if grep -qsx '05ac' /sys/bus/usb/devices/*/idVendor; then
      correr systemctl restart usbmuxd || log "no se pudo reiniciar usbmuxd (se levanta al reconectar el iPhone)"
    fi
    paso reglas_usb ok "Android e iPhone por USB sin contraseña"
  else
    codigo=$?
    errores=$((errores + 1))
    paso reglas_usb error "$(motivo $codigo)"
  fi

  # 3. AirPlay: avahi y firewall
  paso airplay en_curso "avahi-daemon, firewall $FIREWALL"
  local ok_airplay=1
  if ! correr systemctl enable --now avahi-daemon; then
    ok_airplay=0
  fi
  if [[ $ok_airplay -eq 1 && "$FIREWALL" == "ufw" ]]; then
    correr ufw allow 5353/udp comment 'Factum Tatana AirPlay' \
      && correr ufw allow 7000,7001,7100/tcp comment 'Factum Tatana AirPlay' \
      && correr ufw allow 6000,6001,7011/udp comment 'Factum Tatana AirPlay' || ok_airplay=0
  elif [[ $ok_airplay -eq 1 && "$FIREWALL" == "firewalld" ]]; then
    correr firewall-cmd --permanent --add-service=mdns \
      && correr firewall-cmd --permanent --add-port=7000/tcp --add-port=7001/tcp --add-port=7100/tcp \
      && correr firewall-cmd --permanent --add-port=6000/udp --add-port=6001/udp --add-port=7011/udp \
      && correr firewall-cmd --reload || ok_airplay=0
  fi
  if [[ $ok_airplay -eq 1 ]]; then
    local det="avahi activo"
    [[ "$FIREWALL" != "inactivo" ]] && det+=", puertos de AirPlay abiertos en $FIREWALL"
    paso airplay ok "$det"
  else
    errores=$((errores + 1))
    paso airplay error "$(motivo 1)"
  fi

  # 4. Cámara y micrófono (PipeWire/PulseAudio corren por usuario: nada que hacer como root)
  paso camara_microfono en_curso "acceso a la cámara"
  if uaccess_tiene_video; then
    paso camara_microfono omitido "la sesión ya tiene acceso"
  elif id -nG "$usuario" 2>/dev/null </dev/null | tr ' ' '\n' | grep -qx video; then
    paso camara_microfono omitido "$usuario ya está en el grupo video"
  elif correr usermod -aG video "$usuario"; then
    REQUIERE_REINICIAR="true"
    paso camara_microfono ok "$usuario agregado al grupo video (hay que cerrar sesión y volver a entrar)"
  else
    codigo=$?
    errores=$((errores + 1))
    paso camara_microfono error "$(motivo $codigo)"
  fi

  # 5. Sandbox de la ventana (AppArmor, Ubuntu 23.10+)
  paso sandbox en_curso "perfil de AppArmor"
  if apparmor_aplica; then
    if escribir_archivo "$PERFIL_APPARMOR" "$(contenido_perfil_apparmor)" \
       && correr apparmor_parser -r "$PERFIL_APPARMOR"; then
      paso sandbox ok "perfil $PERFIL_APPARMOR cargado"
    else
      codigo=$?
      errores=$((errores + 1))
      paso sandbox error "$(motivo $codigo)"
    fi
  else
    paso sandbox omitido "esta PC no restringe los user namespaces"
  fi

  local pasos="[" i
  for ((i = 0; i < ${#PASOS_JSON[@]}; i++)); do
    (( i > 0 )) && pasos+=","
    pasos+="${PASOS_JSON[i]}"
  done
  pasos+="]"

  if [[ $sin_red -eq 1 ]]; then
    resultado_json "sin_internet" "$pasos"
    return 4
  elif [[ $errores -gt 0 ]]; then
    resultado_json "parcial" "$pasos"
    return 1
  fi
  resultado_json "ok" "$pasos"
  return 0
}

main "$@"; exit $?
