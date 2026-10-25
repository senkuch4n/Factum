#!/usr/bin/env bash
# Instala (o reinstala/actualiza) Tatana en Docker en esta PC: carga la imagen del paquete, escribe la configuración,
# levanta el contenedor, espera /health e instala el actualizador automático (plan A). Se corre con el usuario del
# perito (sin sudo); pide la contraseña solo para la regla USB de Android, para apagar el usbmuxd del host (con
# confirmación) y para instalar el actualizador.
#
#   ./instalar-tatana-docker.sh [--origen <url>]... [--puerto N] [--si] [--mock] [--forzar]
#                               [--actualizador <url> | --sin-actualizador]
#
#   --origen <url>   Página de Factum que puede hablar con Tatana, además de http://localhost:3000. Plan A (servidor
#                    de la fiscalía): --origen https://factum.internal. Plan B (todo en esta PC): sin --origen.
#                    Sin --origen y con una instalación anterior, se conservan los orígenes que ya tenía.
#   --puerto N       Puerto del agente (default 8765, el que espera la web). Cambiarlo solo para pruebas.
#   --si             Contestar que sí a todas las preguntas (instalación desatendida).
#   --mock           Agente simulado, sin USB ni usbmuxd (demostración o prueba sin celular).
#   --forzar         Reemplazar el contenedor aunque Tatana esté capturando (pide escribir FORZAR; se pierde la
#                    captura en curso). Sin esto, si Tatana está ocupado el script corta sin tocar nada.
#   --actualizador <url>  Canal de actualizaciones (default https://factum.internal/tatana/docker/ si los orígenes
#                    incluyen https://factum.internal; sin servidor de la fiscalía no se instala).
#   --sin-actualizador  No instalar el actualizador automático.
#   --red-con-puertos  Solo para probar en Docker Desktop (Mac/Windows), donde no hay --network host ni USB: publica
#                    el puerto en 127.0.0.1 en vez de usar la red del host. Implica --mock. No usar en la fiscalía.
#
# Reinstalar con otros orígenes o una imagen nueva no borra datos ni evidencia: solo recrea el contenedor, con vuelta
# atrás (si la versión nueva no arranca, queda la anterior).
set -euo pipefail
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/comun.sh"

# Instalación anterior (para conservar los orígenes si no se pasa --origen; SDD fiscalia-operacion §7.9).
leer_instalacion
ORIGENES_PREVIOS="$ORIGENES"

ORIGENES_PEDIDOS=()
PUERTO="$PUERTO_DEFAULT"
MOCK=0
RED_CON_PUERTOS=0
SI_A_TODO=0
FORZAR=0
ACTUALIZADOR_URL=""
SIN_ACTUALIZADOR=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --origen) [[ -n "${2:-}" ]] || morir "--origen necesita una URL (ej. https://factum.internal)."
              ORIGENES_PEDIDOS+=("$2"); shift 2 ;;
    --puerto) PUERTO="${2:-}"; shift 2 ;;
    --si) SI_A_TODO=1; shift ;;
    --mock) MOCK=1; shift ;;
    --forzar) FORZAR=1; shift ;;
    --actualizador) [[ -n "${2:-}" ]] || morir "--actualizador necesita una URL (ej. https://factum.internal/tatana/docker/)."
                    ACTUALIZADOR_URL="$2"; shift 2 ;;
    --sin-actualizador) SIN_ACTUALIZADOR=1; shift ;;
    --red-con-puertos) RED_CON_PUERTOS=1; MOCK=1; shift ;;
    -h|--help) sed -n '2,27p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) morir "Opción desconocida: $1 (ver --help)." ;;
  esac
done
export SI_A_TODO
if ! [[ "$PUERTO" =~ ^[0-9]+$ ]] || (( PUERTO < 1 || PUERTO > 65535 )); then morir "--puerto inválido: '$PUERTO'."; fi
if [[ -n "$ACTUALIZADOR_URL" ]]; then
  [[ "$ACTUALIZADOR_URL" == */ ]] || ACTUALIZADOR_URL="$ACTUALIZADOR_URL/"
  [[ "$ACTUALIZADOR_URL" =~ ^https://[A-Za-z0-9.-]+(:[0-9]{1,5})?/([A-Za-z0-9._~-]+/)*$ ]] ||
    morir "--actualizador tiene que ser https://<servidor>/<ruta>/ (ej. https://factum.internal/tatana/docker/)."
  ((SIN_ACTUALIZADOR == 0)) || morir "--actualizador y --sin-actualizador son excluyentes."
fi

echo "${_N}Tatana en Docker — instalación${_0}"
no_root

paso "[1/8] Esta PC"
if [[ "$RED_CON_PUERTOS" == 0 ]]; then
  [[ "$(uname -s)" == Linux ]] || morir "Tatana en Docker es para Linux (en Docker Desktop usá --red-con-puertos solo para probar)."
  [[ "$(uname -m)" == x86_64 ]] || morir "Hace falta una PC x86-64 (Intel/AMD de 64 bits); esta es $(uname -m)."
fi
ok "$(uname -s) $(uname -m), usuario $(id -un) (uid $(id -u))"
detectar_docker
ok "Docker $("${DOCKER[@]}" version --format '{{.Server.Version}}' 2>/dev/null || echo '?')"
# Una operación a la vez: si el actualizador automático está cambiando el contenedor, no se pisa.
tomar_candado || morir "El actualizador automático de Tatana está trabajando en este momento. Esperá unos minutos y volvé a correr el script."

paso "[2/8] Imagen del paquete"
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
# Lo que hay instalado ahora (queda registrado como "anterior" para el actualizador y para volver atrás).
IMAGEN_PREVIA=""
contenedor_existe && IMAGEN_PREVIA="$("${DOCKER[@]}" container inspect -f '{{.Config.Image}}' "$CONTENEDOR" 2>/dev/null || true)"
echo "  Cargando la imagen en Docker..."
"${DOCKER[@]}" load -i "$ARCHIVO_IMAGEN" >/dev/null
"${DOCKER[@]}" image inspect "$IMAGEN" >/dev/null 2>&1 || morir "La imagen cargada no se llama $IMAGEN (IMAGEN.txt no coincide con el archivo)."
ok "$IMAGEN"

paso "[3/8] Servicio de iPhone (usbmuxd)"
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

paso "[4/8] Permiso USB para Android (regla udev)"
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

paso "[5/8] Configuración"
mkdir -p "$DATOS/agent-data" "$DATOS/home" "$DATOS/lockdown" "$EVIDENCIA"
CONFIG="$DATOS/appsettings.Local.json"
[[ -s "$CONFIG" ]] || echo '{}' >"$CONFIG"
# Los orígenes se validan y escriben con el MISMO script que hornea el portátil (hornear-origenes-tatana.py), con el
# Python que trae la imagen: el host no necesita Python. Conserva el resto de las claves del archivo.
# (Sin expandir un arreglo vacío: con `set -u`, bash < 4.4 lo toma como variable sin definir.)
if [[ ${#ORIGENES_PEDIDOS[@]} -gt 0 ]]; then
  ARGS_HORNEAR=("${ORIGENES_PEDIDOS[@]}")
elif [[ -n "$ORIGENES_PREVIOS" ]]; then
  # Reinstalar sin --origen conserva los de antes (antes volvía a solo localhost y una PC del plan A perdía
  # https://factum.internal).
  IFS=',' read -r -a ARGS_HORNEAR <<<"$ORIGENES_PREVIOS"
  ok "se conservan los orígenes de la instalación anterior: $ORIGENES_PREVIOS"
else
  ARGS_HORNEAR=("http://localhost:3000")
fi
"${DOCKER[@]}" run --rm --platform linux/amd64 --security-opt label=disable --user "$(id -u):$(id -g)" --network none \
  -v "$DATOS:/cfg" --entrypoint /opt/tatana/tools/python-embed/bin/python3 "$IMAGEN" \
  -I /opt/tatana-docker/hornear-origenes-tatana.py /cfg/appsettings.Local.json "${ARGS_HORNEAR[@]}" \
  || morir "Algún --origen no es válido (tiene que ser como https://factum.internal, sin barra ni ruta al final)."
ORIGENES="$(IFS=,; echo "${ARGS_HORNEAR[*]}")"
ZONA=""
if command -v timedatectl >/dev/null 2>&1; then ZONA="$(timedatectl show -p Timezone --value 2>/dev/null || true)"; fi
[[ -n "$ZONA" || ! -f /etc/timezone ]] || ZONA="$(tr -d '[:space:]' </etc/timezone)"
[[ "$ZONA" =~ ^[A-Za-z0-9_+/-]*$ ]] || ZONA=""
T_UID="$(id -u)"; T_GID="$(id -g)"; T_GIDS="$(id -G | tr ' ' ',')"; USUARIO="$(id -un)"
# fiscalia-operacion §10.5: además de lo de siempre, lo que root necesita para recrear el contenedor IDÉNTICO sin el
# perito delante (actualizador). Las rutas tienen que ser absolutas y sin caracteres raros.
[[ "$DATOS" =~ ^/[A-Za-z0-9._/@+-]+$ && "$EVIDENCIA" =~ ^/[A-Za-z0-9._/@+-]+$ ]] ||
  morir "Las carpetas $DATOS o $EVIDENCIA tienen caracteres que el actualizador no acepta (espacios, acentos)."
{
  echo "PUERTO=$PUERTO"
  echo "IMAGEN=$IMAGEN"
  echo "ORIGENES=$ORIGENES"
  echo "MOCK=$MOCK"
  echo "USUARIO=$USUARIO"
  echo "UID=$T_UID"
  echo "GID=$T_GID"
  echo "GIDS=$T_GIDS"
  echo "DATOS=$DATOS"
  echo "EVIDENCIA=$EVIDENCIA"
  echo "ZONA=$ZONA"
  echo "RED_CON_PUERTOS=$RED_CON_PUERTOS"
} >"$INSTALACION_ENV"
ok "configuración en $CONFIG"
ok "evidencia en $EVIDENCIA"

paso "[6/8] Contenedor $CONTENEDOR"
run_completo "$IMAGEN"
if contenedor_existe; then
  ETIQUETA="$("${DOCKER[@]}" container inspect -f '{{index .Config.Labels "factum.tatana-docker"}}' "$CONTENEDOR" 2>/dev/null || true)"
  [[ "$ETIQUETA" == 1 ]] || morir "Ya existe un contenedor '$CONTENEDOR' que no es de Tatana en Docker: no se toca."
  echo "  Reemplazando el contenedor actual (los datos y la evidencia quedan; si la versión nueva no arranca, queda la anterior)..."
  FORZAR_CAMBIO=0
  if ((FORZAR)); then
    aviso "--forzar: se reemplaza aunque Tatana esté capturando (la captura en curso se pierde)."
    if [[ -t 0 ]]; then
      read -r -p "  Escribí FORZAR para confirmar: " conf
      [[ "$conf" == FORZAR ]] || morir "No se cambió nada."
    elif [[ "${TATANA_CONFIRMO_FORZAR:-0}" != 1 ]]; then
      morir "--forzar sin terminal necesita TATANA_CONFIRMO_FORZAR=1 en el entorno."
    fi
    FORZAR_CAMBIO=1
  fi
  set +e
  FORZAR_CAMBIO="$FORZAR_CAMBIO" KIT_PY="$DIR_PAQUETE" cambiar_contenedor "$IMAGEN" "" 120
  RC=$?
  set -e
  case $RC in
    0) ok "contenedor nuevo andando (se vuelve a levantar solo al reiniciar la PC)" ;;
    2) morir "Tatana está capturando (${MOTIVO_OCUPADO:-ocupado}): esperá a que termine y volvé a correr este script. No se cambió nada (para reemplazarlo igual: --forzar)." ;;
    3) morir "La versión nueva no arrancó: quedó andando la anterior, sin cambios. Mirá el registro de arriba (docs/tatana-docker.md)." ;;
    4) morir "Ya existe un contenedor '$CONTENEDOR' que no es de Tatana en Docker: no se toca." ;;
    5) morir "El Tatana actual no contestó a tiempo (o contestó algo inesperado) al pedirle el modo mantenimiento: puede estar capturando, así que no se cambió nada. Probá de nuevo en unos minutos (o con --forzar si estás seguro de que no hay una captura)." ;;
    *) morir "La versión nueva no arrancó y la anterior tampoco volvió a responder. Mirá: ${DOCKER[*]} ps -a; ${DOCKER[*]} logs $CONTENEDOR" ;;
  esac
else
  if [[ "$RED_CON_PUERTOS" == 0 ]] && command -v ss >/dev/null 2>&1 && ss -ltnH "sport = :$PUERTO" 2>/dev/null | grep -q .; then
    morir "El puerto $PUERTO ya está en uso en esta PC (¿Tatana nativo abierto?). Cerralo y volvé a correr el script."
  fi
  "${DOCKER[@]}" "${RUN_COMPLETO[@]}" >/dev/null
  ok "contenedor creado (se vuelve a levantar solo al reiniciar la PC)"
fi

paso "[7/8] Esperando a Tatana (/health)"
if ! esperar_version "" 120; then
  falla "Tatana no respondió en 2 minutos. Últimas líneas del registro:"
  "${DOCKER[@]}" logs --tail 40 "$CONTENEDOR" >&2 || true
  exit 1
fi
ok "Tatana ${VERSION_RESPONDIO:-} responde en http://localhost:$PUERTO"

paso "[8/8] Actualizador automático"
CANAL=""
if ((SIN_ACTUALIZADOR)); then
  ok "no se instala (--sin-actualizador)"
elif [[ -n "$ACTUALIZADOR_URL" ]]; then
  CANAL="$ACTUALIZADOR_URL"
elif [[ ",$ORIGENES," == *",https://factum.internal,"* ]]; then
  CANAL="$CANAL_URL_DEFAULT"
else
  ok "sin servidor de la fiscalía en los orígenes: no se instala (se actualiza con un paquete nuevo)"
fi
if [[ -n "$CANAL" ]] && { [[ "$(uname -s)" != Linux ]] || ! command -v systemctl >/dev/null 2>&1 || [[ ! -d /run/systemd/system ]]; }; then
  aviso "esta PC no usa systemd: no se instala el actualizador (Tatana se actualiza con un paquete nuevo)."
  CANAL=""
fi
if [[ -n "$CANAL" ]]; then
  VERSION_KIT="$(tr -d '[:space:]' <"$DIR_PAQUETE/VERSION" 2>/dev/null || true)"
  [[ "$VERSION_KIT" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || morir "Falta VERSION en el paquete: no se puede instalar el actualizador."
  for f in actualizador-tatana-docker.sh estado-agente.py verificar-manifiesto-docker.py claves-confiables.txt \
           "systemd/$UNIDAD_SERVICIO" "systemd/$UNIDAD_TIMER"; do
    [[ -f "$DIR_PAQUETE/$f" ]] || morir "Al paquete le falta $f: no se puede instalar el actualizador."
  done
  echo "  Canal: $CANAL (cada 30 minutos; se actualiza solo con Tatana libre). Pide la contraseña de administrador."
  sudo -v || morir "Hace falta la contraseña de administrador para instalar el actualizador (o usá --sin-actualizador)."
  sudo mkdir -p "$TD_OPT/kits" "$TD_ETC" "$TD_VAR" "$TD_LOG"
  sudo mkdir -p -m 700 "$TD_VAR/descargas"
  # Kit (todo menos la imagen) en /opt, de root: lo que decide si una actualización vale es esto, no el servidor.
  KIT_DEST="$TD_OPT/kits/$VERSION_KIT"
  if [[ "$(cd "$DIR_PAQUETE" && pwd -P)" != "$(cd "$KIT_DEST" 2>/dev/null && pwd -P || true)" ]]; then
    TMP_KIT="$(sudo mktemp -d "$TD_OPT/kits/.nuevo.XXXXXX")"
    for f in "$DIR_PAQUETE"/*; do
      case "$(basename "$f")" in imagen-tatana-docker-*.tar.gz) continue ;; esac
      sudo cp -R "$f" "$TMP_KIT/"
    done
    sudo chown -R 0:0 "$TMP_KIT"
    sudo chmod -R u=rwX,go=rX "$TMP_KIT"
    sudo chmod 755 "$TMP_KIT" "$TMP_KIT"/*.sh
    sudo rm -rf -- "$KIT_DEST"
    sudo mv "$TMP_KIT" "$KIT_DEST"
  fi
  sudo ln -sfn "kits/$VERSION_KIT" "$TD_OPT/actual.nuevo"
  sudo mv -Tf "$TD_OPT/actual.nuevo" "$TD_OPT/actual"
  ok "kit $VERSION_KIT en $KIT_DEST"
  sudo install -m 644 "$INSTALACION_ENV" "$INSTALACION_ROOT_ENV"
  HABILITADO=1
  if [[ -f "$CA_SISTEMA" ]]; then
    sudo install -m 644 "$CA_SISTEMA" "$TD_ETC/ca-factum-fiscalia.crt"
  else
    HABILITADO=0
    aviso "Esta PC no tiene el certificado de la fiscalía ($CA_SISTEMA): corré preparar-pc-perito.sh primero."
    aviso "El actualizador queda instalado pero APAGADO (HABILITADO=0) hasta que vuelvas a correr este script."
  fi
  printf 'CANAL_URL=%s\nCA=%s\nHABILITADO=%s\n' "$CANAL" "$TD_ETC/ca-factum-fiscalia.crt" "$HABILITADO" |
    sudo tee "$ACTUALIZADOR_ENV" >/dev/null
  sudo chmod 644 "$ACTUALIZADOR_ENV"
  sudo touch "$CANDADO" "$TD_VAR/imagenes.txt"
  sudo chmod 644 "$CANDADO" "$TD_VAR/imagenes.txt"
  # Imágenes que Factum puede borrar más adelante (nunca una ajena): la instalada y la que había antes.
  for img in "$IMAGEN" "$IMAGEN_PREVIA"; do
    [[ -n "$img" ]] || continue
    sudo grep -qxF "$img" "$TD_VAR/imagenes.txt" || echo "$img" | sudo tee -a "$TD_VAR/imagenes.txt" >/dev/null
  done
  # estado.env: instalada y anterior (para --volver-a-anterior); se conservan las versiones fallidas, salvo esta.
  etiqueta_version() { "${DOCKER[@]}" image inspect -f '{{index .Config.Labels "org.opencontainers.image.version"}}' "$1" 2>/dev/null || true; }
  V_INST="$(etiqueta_version "$IMAGEN")"
  E_PREVIO="$(cat "$ESTADO_ENV" 2>/dev/null || true)"
  V_ANT="$(clave_de "$E_PREVIO" VERSION_ANTERIOR)"; I_ANT="$(clave_de "$E_PREVIO" IMAGEN_ANTERIOR)"
  if [[ -n "$IMAGEN_PREVIA" && "$IMAGEN_PREVIA" != "$IMAGEN" ]]; then
    I_ANT="$IMAGEN_PREVIA"; V_ANT="$(etiqueta_version "$IMAGEN_PREVIA")"
  fi
  FALLIDAS="$(clave_de "$E_PREVIO" VERSIONES_FALLIDAS | tr ',' '\n' | grep -vxF "$V_INST" | grep -v '^$' | paste -sd, - || true)"
  {
    echo "ULTIMA_CONSULTA=$(clave_de "$E_PREVIO" ULTIMA_CONSULTA)"
    echo "RESULTADO=$(clave_de "$E_PREVIO" RESULTADO)"
    echo "DETALLE=instalado con instalar-tatana-docker.sh el $(date '+%Y-%m-%d %H:%M')"
    echo "VERSION_INSTALADA=$V_INST"
    echo "IMAGEN_INSTALADA=$IMAGEN"
    echo "VERSION_DISPONIBLE=$(clave_de "$E_PREVIO" VERSION_DISPONIBLE)"
    echo "VERSION_ANTERIOR=$V_ANT"
    echo "IMAGEN_ANTERIOR=$I_ANT"
    echo "VERSIONES_FALLIDAS=$FALLIDAS"
    echo "ULTIMA_ACTUALIZACION=$(date '+%Y-%m-%dT%H:%M:%S%z')"
  } | sudo tee "$ESTADO_ENV" >/dev/null
  sudo chmod 644 "$ESTADO_ENV"
  sudo install -m 644 "$DIR_PAQUETE/systemd/$UNIDAD_SERVICIO" "$DIR_PAQUETE/systemd/$UNIDAD_TIMER" "$TD_SYSTEMD/"
  sudo systemctl daemon-reload
  sudo systemctl enable --now "$UNIDAD_TIMER" >/dev/null 2>&1 || aviso "no se pudo activar $UNIDAD_TIMER (systemctl status $UNIDAD_TIMER)"
  ok "actualizador instalado: $UNIDAD_TIMER$( ((HABILITADO)) || echo ' (apagado: falta el certificado)')"
fi

echo
echo "${_V}${_N}Listo.${_0} Abrí Factum en el navegador (${ORIGENES//,/ o }) y conectá el celular."
echo "Estado: ./estado-tatana-docker.sh   ·   Desinstalar: ./desinstalar-tatana-docker.sh"
