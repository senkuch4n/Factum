#!/usr/bin/env bash
# Instala (o reinstala/actualiza) Tatana en Docker en esta PC: carga la imagen del paquete, escribe la configuración,
# levanta el contenedor y espera /health. Se corre con el usuario del perito (sin sudo); pide la contraseña solo para
# la regla USB de Android y, con confirmación, para apagar el usbmuxd del host.
#
#   ./instalar-tatana-docker.sh [--origen <url>]... [--puerto N] [--si] [--mock]
#
#   --origen <url>   Página de Factum que puede hablar con Tatana, además de http://localhost:3000. Plan A (servidor
#                    de la fiscalía): --origen https://factum.internal. Plan B (todo en esta PC): sin --origen.
#   --puerto N       Puerto del agente (default 8765, el que espera la web). Cambiarlo solo para pruebas.
#   --si             Contestar que sí a todas las preguntas (instalación desatendida).
#   --mock           Agente simulado, sin USB ni usbmuxd (demostración o prueba sin celular).
#   --red-con-puertos  Solo para probar en Docker Desktop (Mac/Windows), donde no hay --network host ni USB: publica
#                    el puerto en 127.0.0.1 en vez de usar la red del host. Implica --mock. No usar en la fiscalía.
#
# Reinstalar con otros orígenes o una imagen nueva no borra datos ni evidencia: solo recrea el contenedor.
set -euo pipefail
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/comun.sh"

ORIGENES_PEDIDOS=()
PUERTO="$PUERTO_DEFAULT"
MOCK=0
RED_CON_PUERTOS=0
SI_A_TODO=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --origen) [[ -n "${2:-}" ]] || morir "--origen necesita una URL (ej. https://factum.internal)."
              ORIGENES_PEDIDOS+=("$2"); shift 2 ;;
    --puerto) PUERTO="${2:-}"; shift 2 ;;
    --si) SI_A_TODO=1; shift ;;
    --mock) MOCK=1; shift ;;
    --red-con-puertos) RED_CON_PUERTOS=1; MOCK=1; shift ;;
    -h|--help) sed -n '2,17p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) morir "Opción desconocida: $1 (ver --help)." ;;
  esac
done
export SI_A_TODO
if ! [[ "$PUERTO" =~ ^[0-9]+$ ]] || (( PUERTO < 1 || PUERTO > 65535 )); then morir "--puerto inválido: '$PUERTO'."; fi

echo "${_N}Tatana en Docker — instalación${_0}"
no_root

paso "[1/7] Esta PC"
if [[ "$RED_CON_PUERTOS" == 0 ]]; then
  [[ "$(uname -s)" == Linux ]] || morir "Tatana en Docker es para Linux (en Docker Desktop usá --red-con-puertos solo para probar)."
  [[ "$(uname -m)" == x86_64 ]] || morir "Hace falta una PC x86-64 (Intel/AMD de 64 bits); esta es $(uname -m)."
fi
ok "$(uname -s) $(uname -m), usuario $(id -un) (uid $(id -u))"
detectar_docker
ok "Docker $("${DOCKER[@]}" version --format '{{.Server.Version}}' 2>/dev/null || echo '?')"

paso "[2/7] Imagen del paquete"
[[ -f "$DIR_PAQUETE/IMAGEN.txt" ]] || morir "Falta IMAGEN.txt al lado de este script: ¿se copió el paquete completo?"
IMAGEN="$(tr -d '[:space:]' <"$DIR_PAQUETE/IMAGEN.txt")"
ARCHIVO_IMAGEN=""
for f in "$DIR_PAQUETE"/imagen-tatana-docker-*.tar.gz; do [[ -f "$f" ]] && { ARCHIVO_IMAGEN="$f"; break; }; done
[[ -n "$ARCHIVO_IMAGEN" ]] || morir "No está imagen-tatana-docker-*.tar.gz al lado de este script."
if [[ -f "$DIR_PAQUETE/SHA256SUMS" ]]; then
  ESPERADO="$(awk -v f="$(basename "$ARCHIVO_IMAGEN")" '$2 == f || $2 == "*"f {print $1}' "$DIR_PAQUETE/SHA256SUMS")"
  [[ -n "$ESPERADO" ]] || morir "SHA256SUMS no lista $(basename "$ARCHIVO_IMAGEN")."
  echo "  Verificando SHA-256 de $(basename "$ARCHIVO_IMAGEN") (puede tardar un poco)..."
  [[ "$(sha256_de "$ARCHIVO_IMAGEN")" == "$ESPERADO" ]] || morir "El SHA-256 de la imagen no coincide: el pendrive o la copia están dañados."
  ok "SHA-256 correcto"
else
  aviso "No hay SHA256SUMS: no se verifica la imagen."
fi
echo "  Cargando la imagen en Docker..."
"${DOCKER[@]}" load -i "$ARCHIVO_IMAGEN" >/dev/null
"${DOCKER[@]}" image inspect "$IMAGEN" >/dev/null 2>&1 || morir "La imagen cargada no se llama $IMAGEN (IMAGEN.txt no coincide con el archivo)."
ok "$IMAGEN"

paso "[3/7] Servicio de iPhone (usbmuxd)"
if [[ "$MOCK" == 1 ]]; then
  ok "modo simulado: no se usa usbmuxd"
elif usbmuxd_del_host_activo; then
  aviso "Esta PC tiene su propio usbmuxd andando. Choca con el de Tatana en Docker: los dos quieren el iPhone."
  if command -v systemctl >/dev/null 2>&1 && confirmar "¿Detener y deshabilitar (mask) el usbmuxd de esta PC? Se revierte con desinstalar-tatana-docker.sh."; then
    sudo systemctl stop usbmuxd.service usbmuxd.socket 2>/dev/null || sudo systemctl stop usbmuxd.service || true
    sudo systemctl mask usbmuxd.service
    mkdir -p "$DATOS"
    date '+%Y-%m-%d %H:%M:%S' >"$MARCA_USBMUXD"
    ok "usbmuxd del host detenido y enmascarado"
  else
    aviso "Se deja el usbmuxd de esta PC: Android anda igual, pero el iPhone puede no aparecer en Tatana."
  fi
else
  ok "no hay usbmuxd en el host (el del contenedor queda solo)"
fi

paso "[4/7] Permiso USB para Android (regla udev)"
if [[ "$MOCK" == 1 ]]; then
  ok "modo simulado: no hace falta"
elif [[ -f "$REGLA_UDEV" ]]; then
  ok "ya existe $REGLA_UDEV"
elif [[ ! -d /etc/udev/rules.d ]]; then
  aviso "Esta PC no tiene /etc/udev/rules.d: adb puede necesitar permisos a mano (ver docs/tatana-docker.md)."
else
  REGLA='# Factum Tatana: acceso a celulares Android por USB (adb) para el usuario de la sesión activa (logind, uaccess).
# Lo escribe "Preparar esta PC" de Tatana. Se puede borrar: Tatana lo vuelve a crear si hace falta.
SUBSYSTEM=="usb", ENV{DEVTYPE}=="usb_device", ENV{ID_USB_INTERFACES}=="*:ff4201:*", TAG+="uaccess"'
  echo "  Para que adb vea el celular hace falta una regla USB (la misma que escribe Tatana nativo) en $REGLA_UDEV."
  if confirmar "¿Escribirla ahora? (pide la contraseña de administrador)"; then
    printf '%s\n' "$REGLA" | sudo tee "$REGLA_UDEV" >/dev/null
    sudo udevadm control --reload-rules 2>/dev/null || true
    sudo udevadm trigger --action=change --subsystem-match=usb 2>/dev/null || true
    ok "regla escrita; si el celular ya estaba enchufado, desenchufalo y volvé a enchufarlo"
  else
    aviso "Sin la regla, adb no puede abrir el celular Android (sí el iPhone)."
  fi
fi

paso "[5/7] Configuración"
mkdir -p "$DATOS/agent-data" "$DATOS/home" "$DATOS/lockdown" "$EVIDENCIA"
CONFIG="$DATOS/appsettings.Local.json"
[[ -s "$CONFIG" ]] || echo '{}' >"$CONFIG"
# Los orígenes se validan y escriben con el MISMO script que hornea el portátil (hornear-origenes-tatana.py), con el
# Python que trae la imagen: el host no necesita Python. Conserva el resto de las claves del archivo.
# (Sin expandir un arreglo vacío: con `set -u`, bash < 4.4 lo toma como variable sin definir.)
if [[ ${#ORIGENES_PEDIDOS[@]} -gt 0 ]]; then ARGS_HORNEAR=("${ORIGENES_PEDIDOS[@]}"); else ARGS_HORNEAR=("http://localhost:3000"); fi
"${DOCKER[@]}" run --rm --platform linux/amd64 --security-opt label=disable --user "$(id -u):$(id -g)" --network none \
  -v "$DATOS:/cfg" --entrypoint /opt/tatana/tools/python-embed/bin/python3 "$IMAGEN" \
  -I /opt/tatana-docker/hornear-origenes-tatana.py /cfg/appsettings.Local.json "${ARGS_HORNEAR[@]}" \
  || morir "Algún --origen no es válido (tiene que ser como https://factum.internal, sin barra ni ruta al final)."
ORIGENES="$(IFS=,; echo "${ARGS_HORNEAR[*]}")"
ZONA=""
if command -v timedatectl >/dev/null 2>&1; then ZONA="$(timedatectl show -p Timezone --value 2>/dev/null || true)"; fi
[[ -n "$ZONA" || ! -f /etc/timezone ]] || ZONA="$(tr -d '[:space:]' </etc/timezone)"
{
  echo "PUERTO=$PUERTO"
  echo "IMAGEN=$IMAGEN"
  echo "ORIGENES=$ORIGENES"
  echo "MOCK=$MOCK"
} >"$INSTALACION_ENV"
ok "configuración en $CONFIG"
ok "evidencia en $EVIDENCIA"

paso "[6/7] Contenedor $CONTENEDOR"
if contenedor_existe; then
  ETIQUETA="$("${DOCKER[@]}" container inspect -f '{{index .Config.Labels "factum.tatana-docker"}}' "$CONTENEDOR" 2>/dev/null || true)"
  [[ "$ETIQUETA" == 1 ]] || morir "Ya existe un contenedor '$CONTENEDOR' que no es de Tatana en Docker: no se toca."
  echo "  Reemplazando el contenedor anterior (los datos y la evidencia quedan)..."
  "${DOCKER[@]}" rm -f "$CONTENEDOR" >/dev/null
fi
if [[ "$RED_CON_PUERTOS" == 0 ]] && command -v ss >/dev/null 2>&1 && ss -ltnH "sport = :$PUERTO" 2>/dev/null | grep -q .; then
  morir "El puerto $PUERTO ya está en uso en esta PC (¿Tatana nativo abierto?). Cerralo y volvé a correr el script."
fi

RUN=(run -d --platform linux/amd64 --name "$CONTENEDOR" --restart unless-stopped --label factum.tatana-docker=1
  --security-opt label=disable
  -e "TATANA_UID=$(id -u)" -e "TATANA_GID=$(id -g)" -e "TATANA_GIDS=$(id -G | tr ' ' ',')"
  -e "TATANA_PUERTO=$PUERTO" -e "HOME=$DATOS/home" -e "Agent__EvidenceDirectory=$EVIDENCIA"
  -v "$DATOS/agent-data:$DATOS/agent-data" -v "$DATOS/home:$DATOS/home" -v "$EVIDENCIA:$EVIDENCIA"
  -v "$CONFIG:/config/appsettings.Local.json:ro")
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
"${DOCKER[@]}" "${RUN[@]}" "$IMAGEN" "${AGENTE[@]}" >/dev/null
ok "contenedor creado (se vuelve a levantar solo al reiniciar la PC)"

paso "[7/7] Esperando a Tatana (/health)"
for _ in $(seq 1 60); do
  if SALUD="$(health "$PUERTO")" && [[ -n "$SALUD" ]]; then
    ok "Tatana responde en http://localhost:$PUERTO"
    echo "  $SALUD" | cut -c1-300
    echo
    echo "${_V}${_N}Listo.${_0} Abrí Factum en el navegador (${ORIGENES//,/ o }) y conectá el celular."
    echo "Estado: ./estado-tatana-docker.sh   ·   Desinstalar: ./desinstalar-tatana-docker.sh"
    exit 0
  fi
  if [[ "$("${DOCKER[@]}" container inspect -f '{{.State.Running}}' "$CONTENEDOR" 2>/dev/null)" != true ]]; then
    break
  fi
  sleep 2
done
falla "Tatana no respondió en 2 minutos. Últimas líneas del registro:"
"${DOCKER[@]}" logs --tail 40 "$CONTENEDOR" >&2 || true
exit 1
