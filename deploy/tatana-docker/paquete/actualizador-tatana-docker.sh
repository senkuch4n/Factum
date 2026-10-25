#!/usr/bin/env bash
# Actualizador automático de Tatana en Docker (SDD fiscalia-operacion §7.4–§7.7). Corre como ROOT; lo llama el timer de
# systemd (factum-tatana-actualizador.timer, cada 30 min) desde /opt/factum-tatana-docker/actual/.
#
#   actualizador-tatana-docker.sh --una-vez             una consulta al servidor de la fiscalía (lo que hace el timer)
#   actualizador-tatana-docker.sh --volver-a-anterior   vuelve a la versión anterior (si Tatana está libre)
#   actualizador-tatana-docker.sh --buscar-ahora        lanza ya la consulta del timer y muestra el resultado
#
# Reglas: nunca baja de versión; verifica la firma Ed25519 con las claves y el verificador del kit INSTALADO (nunca con
# lo que mande el servidor); descarga siempre, pero carga la imagen y cambia el contenedor SOLO con Tatana libre; si la
# versión nueva no responde en 2 minutos vuelve a la anterior y no la reintenta. Datos, evidencia, pareos y
# configuración del perito no se tocan. Cada corrida deja /var/lib/factum-tatana-docker/estado.env y una línea en
# /var/log/factum-tatana-docker/actualizador.log (y en el journal).
# shellcheck disable=SC2034 # las claves de estado.env se escriben por nombre (${!c}) e IMAGEN_PY/KIT_PY las lee comun.sh
set -uo pipefail
# shellcheck source=deploy/tatana-docker/paquete/comun.sh
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/comun.sh"

KIT_ACTUAL="$(cd "$DIR_PAQUETE" && pwd -P)"
DESCARGAS="$TD_VAR/descargas"
IMAGENES_TXT="$TD_VAR/imagenes.txt"
LOG_F="$TD_LOG/actualizador.log"
MODO=""
case "${1:-}" in
  --una-vez|--volver-a-anterior|--buscar-ahora) MODO="${1#--}" ;;
  -h|--help) sed -n '2,8p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
  *) echo "Uso: $0 --una-vez | --volver-a-anterior | --buscar-ahora" >&2; exit 2 ;;
esac
[[ "$(id -u)" == 0 ]] || { echo "Este script corre como root (sudo)." >&2; exit 1; }
umask 022

# ── Registro ────────────────────────────────────────────────────────────────────────────────────────────────────────
ahora() { date '+%Y-%m-%dT%H:%M:%S%z'; }
log_evento() {
  mkdir -p "$TD_LOG" 2>/dev/null
  if [[ -f "$LOG_F" ]] && (( $(wc -c <"$LOG_F") > 1048576 )); then mv -f "$LOG_F" "$LOG_F.1"; fi
  printf '%s %s\n' "$(ahora)" "$*" >>"$LOG_F" 2>/dev/null
  printf '%s\n' "$*"
}
registrar() { log_evento "$*"; }

# ── estado.env (§10.7) ──────────────────────────────────────────────────────────────────────────────────────────────
CLAVES_ESTADO="ULTIMA_CONSULTA RESULTADO DETALLE VERSION_INSTALADA IMAGEN_INSTALADA VERSION_DISPONIBLE VERSION_ANTERIOR IMAGEN_ANTERIOR VERSIONES_FALLIDAS ULTIMA_ACTUALIZACION"
for _c in $CLAVES_ESTADO; do printf -v "$_c" '%s' ""; done
leer_estado() {
  local clave valor c
  [[ -f "$ESTADO_ENV" ]] || return 0
  while IFS='=' read -r clave valor || [[ -n "$clave" ]]; do
    for c in $CLAVES_ESTADO; do [[ "$clave" == "$c" ]] && printf -v "$c" '%s' "$valor"; done
  done <"$ESTADO_ENV"
}
guardar_estado() {
  local tmp c v
  mkdir -p "$TD_VAR"
  tmp="$(mktemp "$TD_VAR/.estado.XXXXXX")" || return 0
  for c in $CLAVES_ESTADO; do
    v="${!c}"
    v="${v//$'\n'/ }"
    printf '%s=%s\n' "$c" "${v:0:400}"
  done >"$tmp"
  chmod 644 "$tmp"
  mv -f "$tmp" "$ESTADO_ENV"
}
fallida() { [[ ",$VERSIONES_FALLIDAS," == *",$1,"* ]]; }
marcar_fallida() { fallida "$1" || VERSIONES_FALLIDAS="${VERSIONES_FALLIDAS:+$VERSIONES_FALLIDAS,}$1"; }

# Lo que se reportó en la consulta (para mandar un HEAD con el resultado final si cambió, paso 11).
REPORTADO=""
SERVIDOR_OK=0
terminar() { # terminar <RESULTADO> [detalle]
  RESULTADO="$1"
  DETALLE="${2:-}"
  ULTIMA_CONSULTA="$(ahora)"
  guardar_estado
  log_evento "RESULTADO=$RESULTADO${DETALLE:+ — $DETALLE} (instalada ${VERSION_INSTALADA:-?}, disponible ${VERSION_DISPONIBLE:-?})"
  if ((SERVIDOR_OK)) && [[ "$RESULTADO" != "$REPORTADO" ]]; then
    curl_canal -I --max-time 15 -o /dev/null "${CANAL_URL}tatana-update-docker.json?$(consulta)" >/dev/null 2>&1 || true
  fi
  exit 0
}

# ── Consulta al canal (§10.2) ───────────────────────────────────────────────────────────────────────────────────────
sanear() { local s="${1//[^A-Za-z0-9._-]/_}"; printf '%s' "${s:0:64}"; }
equipo() { hostname 2>/dev/null || cat /etc/hostname 2>/dev/null || uname -n; }
consulta() {
  printf 'v=1&equipo=%s&instalada=%s&disponible=%s&resultado=%s' "$(sanear "$(equipo)")" "$(sanear "$VERSION_INSTALADA")" \
    "$(sanear "$VERSION_DISPONIBLE")" "$(sanear "$RESULTADO")"
}
RESOLVE=()
# curl de la imagen INSTALADA (Ubuntu 20.04 de escritorio no trae curl; D-T4). factum.internal se resuelve en el host.
curl_canal() {
  "${DOCKER[@]}" run --rm --platform linux/amd64 --network host --security-opt label=disable \
    -v "$CA:/ca.crt:ro" -v "$DESCARGAS:/d" --entrypoint curl "$IMAGEN_INSTALADA" \
    -fsS --cacert /ca.crt ${RESOLVE[@]+"${RESOLVE[@]}"} "$@"
}

tamano_de() { wc -c <"$1" 2>/dev/null | tr -d ' '; }

# ── Configuración ───────────────────────────────────────────────────────────────────────────────────────────────────
CANAL_URL=""; CA=""; HABILITADO=0
leer_actualizador_env() {
  local clave valor
  [[ -f "$ACTUALIZADOR_ENV" ]] || return 1
  while IFS='=' read -r clave valor || [[ -n "$clave" ]]; do
    case "$clave" in
      CANAL_URL) CANAL_URL="$valor" ;;
      CA) CA="$valor" ;;
      HABILITADO) HABILITADO="$valor" ;;
    esac
  done <"$ACTUALIZADOR_ENV"
  [[ "$CANAL_URL" =~ ^https://[A-Za-z0-9.-]+(:[0-9]{1,5})?/([A-Za-z0-9._~-]+/)*$ ]] || return 1
  [[ "$CA" =~ ^/[A-Za-z0-9._/-]+$ && "$HABILITADO" =~ ^[01]$ ]]
}

preparar_docker() {
  DOCKER=(docker)
  command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1
}

# Imagen y versión del contenedor actual.
leer_instalada() {
  IMAGEN_INSTALADA="$("${DOCKER[@]}" container inspect -f '{{.Config.Image}}' "$CONTENEDOR" 2>/dev/null || true)"
  [[ -n "$IMAGEN_INSTALADA" ]] || return 1
  VERSION_INSTALADA="$("${DOCKER[@]}" image inspect -f '{{index .Config.Labels "org.opencontainers.image.version"}}' "$IMAGEN_INSTALADA" 2>/dev/null || true)"
  [[ -n "$VERSION_INSTALADA" && "$VERSION_INSTALADA" != "<no value>" ]] ||
    VERSION_INSTALADA="$(clave_de "$(IMAGEN_PY="$IMAGEN_INSTALADA" estado_agente version || true)" VERSION)"
  return 0
}

existe_contenedor() { "${DOCKER[@]}" container inspect "$1" >/dev/null 2>&1; }
es_de_tatana() { [[ "$("${DOCKER[@]}" container inspect -f '{{index .Config.Labels "factum.tatana-docker"}}' "$1" 2>/dev/null)" == 1 ]]; }

# IMAGEN=<imagen> en un instalacion.env (de root o del perito), conservando dueño y permisos.
poner_imagen_env() { # poner_imagen_env <archivo> <imagen>
  local f="$1" tmp
  [[ -f "$f" ]] || return 0
  tmp="$(mktemp)"
  if grep -q '^IMAGEN=' "$f"; then
    sed "s|^IMAGEN=.*|IMAGEN=$2|" "$f" >"$tmp"
  else
    { cat "$f"; echo "IMAGEN=$2"; } >"$tmp"
  fi
  cat "$tmp" >"$f"
  rm -f "$tmp"
}

registrar_imagen() { # registrar_imagen <imagen>  (imagenes.txt: las únicas que Factum puede borrar)
  [[ -n "$1" ]] || return 0
  touch "$IMAGENES_TXT"
  grep -qxF "$1" "$IMAGENES_TXT" || echo "$1" >>"$IMAGENES_TXT"
  chmod 644 "$IMAGENES_TXT"
}

imagen_en_uso() { "${DOCKER[@]}" ps -aq --filter "ancestor=$1" 2>/dev/null | grep -q .; }

borrar_imagen_si_se_puede() { # nunca la instalada, nunca una en uso
  local img="$1"
  [[ -n "$img" && "$img" != "$IMAGEN_INSTALADA" ]] || return 0
  "${DOCKER[@]}" image inspect "$img" >/dev/null 2>&1 || return 0
  imagen_en_uso "$img" && return 0
  "${DOCKER[@]}" image rm "$img" >/dev/null 2>&1 && log_evento "imagen $img borrada"
}

# Limpieza (D-T13): quedan solo la actual y la anterior (imágenes de imagenes.txt y kits); nunca una imagen ajena.
limpiar() {
  local img quedan="" d v
  if [[ -f "$IMAGENES_TXT" ]]; then
    while IFS= read -r img; do
      [[ -n "$img" ]] || continue
      if [[ "$img" == "$IMAGEN_INSTALADA" || "$img" == "$IMAGEN_ANTERIOR" ]] || imagen_en_uso "$img"; then
        quedan="$quedan$img"$'\n'
      elif "${DOCKER[@]}" image inspect "$img" >/dev/null 2>&1 && ! "${DOCKER[@]}" image rm "$img" >/dev/null 2>&1; then
        quedan="$quedan$img"$'\n'
      else
        log_evento "limpieza: imagen $img borrada"
      fi
    done <"$IMAGENES_TXT"
    printf '%s' "$quedan" >"$IMAGENES_TXT"
  fi
  for d in "$TD_OPT"/kits/*; do
    [[ -d "$d" ]] || continue
    v="$(basename "$d")"
    [[ "$v" == "$VERSION_INSTALADA" || "$v" == "$VERSION_ANTERIOR" ]] && continue
    rm -rf -- "$d" && log_evento "limpieza: kit $v borrado"
  done
  rm -f -- "$DESCARGAS"/* 2>/dev/null
}

# Recuperación de un cambio interrumpido (corte de luz durante §7.6).
recuperar() {
  if ! existe_contenedor "$CONTENEDOR" && existe_contenedor "$CONTENEDOR_ANTERIOR" && es_de_tatana "$CONTENEDOR_ANTERIOR"; then
    "${DOCKER[@]}" rename "$CONTENEDOR_ANTERIOR" "$CONTENEDOR" >/dev/null 2>&1 &&
      "${DOCKER[@]}" start "$CONTENEDOR" >/dev/null 2>&1
    leer_instalada
    terminar recuperado "se volvió a arrancar $CONTENEDOR_ANTERIOR (un cambio había quedado a medias)"
  fi
  if existe_contenedor "$CONTENEDOR" && existe_contenedor "$CONTENEDOR_ANTERIOR" && es_de_tatana "$CONTENEDOR_ANTERIOR"; then
    if es_de_tatana "$CONTENEDOR" && IMAGEN_ESPERA="" esperar_version "" 90; then
      "${DOCKER[@]}" rm -f "$CONTENEDOR_ANTERIOR" >/dev/null 2>&1
      log_evento "se borró $CONTENEDOR_ANTERIOR (quedaba de un cambio terminado)"
    elif es_de_tatana "$CONTENEDOR"; then
      "${DOCKER[@]}" rm -f "$CONTENEDOR" >/dev/null 2>&1
      "${DOCKER[@]}" rename "$CONTENEDOR_ANTERIOR" "$CONTENEDOR" >/dev/null 2>&1 &&
        "${DOCKER[@]}" start "$CONTENEDOR" >/dev/null 2>&1
      leer_instalada
      terminar recuperado "el contenedor nuevo no respondía: se volvió al anterior"
    fi
  fi
}

# ══ --buscar-ahora ══════════════════════════════════════════════════════════════════════════════════════════════════
if [[ "$MODO" == buscar-ahora ]]; then
  echo "Buscando una versión nueva de Tatana en el servidor (puede tardar si hay algo para bajar)..."
  if command -v systemctl >/dev/null 2>&1 && [[ -f "$TD_SYSTEMD/$UNIDAD_SERVICIO" ]]; then
    systemctl start "$UNIDAD_SERVICIO" || echo "  (systemctl start terminó con error: ver journalctl -u $UNIDAD_SERVICIO)"
  else
    "$0" --una-vez >/dev/null
  fi
  leer_estado
  echo "  Resultado: $(texto_resultado "$RESULTADO" "$VERSION_INSTALADA" "$DETALLE")${DETALLE:+ — $DETALLE}"
  echo "  Instalada: ${VERSION_INSTALADA:-?}   Disponible en el servidor: ${VERSION_DISPONIBLE:-?}"
  exit 0
fi

mkdir -p "$TD_VAR" "$TD_LOG"
mkdir -p "$DESCARGAS" && chmod 700 "$DESCARGAS"
leer_estado
RESULTADO_PREVIO="$RESULTADO"

# ══ --volver-a-anterior (§7.7) ═════════════════════════════════════════════════════════════════════════════════════
if [[ "$MODO" == volver-a-anterior ]]; then
  tomar_candado || { echo "Hay otra operación de Tatana en curso (actualización o instalación): probá en unos minutos." >&2; exit 1; }
  preparar_docker || { echo "Docker no responde." >&2; exit 1; }
  leer_instalacion_root || { echo "Falta la configuración del actualizador ($ERROR_INSTALACION)." >&2; exit 1; }
  leer_instalada || { echo "No hay contenedor $CONTENEDOR." >&2; exit 1; }
  if [[ -z "$IMAGEN_ANTERIOR" ]] || ! "${DOCKER[@]}" image inspect "$IMAGEN_ANTERIOR" >/dev/null 2>&1; then
    echo "No hay una versión anterior cargada en esta PC${IMAGEN_ANTERIOR:+ ($IMAGEN_ANTERIOR ya no está)}." >&2
    exit 1
  fi
  KIT_ANT="$TD_OPT/kits/$VERSION_ANTERIOR"
  if [[ -z "$VERSION_ANTERIOR" || ! -f "$KIT_ANT/comun.sh" ]]; then
    # Instalaciones del 2026-10-20 (tatana-docker-prueba): su versión no tenía kit. El docker run es el mismo.
    log_evento "no está el kit de ${VERSION_ANTERIOR:-la anterior}: se usa el del kit actual para recrear el contenedor"
    KIT_ANT="$KIT_ACTUAL"
  fi
  run_completo_de_kit "$KIT_ANT" "$IMAGEN_ANTERIOR" 2>>"$LOG_F" || { echo "No se pudo armar el contenedor anterior (ver $LOG_F)." >&2; exit 1; }
  echo "Volviendo de ${VERSION_INSTALADA:-?} a ${VERSION_ANTERIOR:-?} ($IMAGEN_ANTERIOR)..."
  IMAGEN_PY="$IMAGEN_INSTALADA" cambiar_contenedor "$IMAGEN_ANTERIOR" "" 120
  rc=$?
  case $rc in
    0)
      marcar_fallida "$VERSION_INSTALADA"
      V_VIEJA="$VERSION_INSTALADA"; I_VIEJA="$IMAGEN_INSTALADA"
      VERSION_INSTALADA="$VERSION_ANTERIOR"; IMAGEN_INSTALADA="$IMAGEN_ANTERIOR"
      VERSION_ANTERIOR="$V_VIEJA"; IMAGEN_ANTERIOR="$I_VIEJA"
      if [[ "$KIT_ANT" != "$KIT_ACTUAL" ]]; then
        ln -sfn "kits/$VERSION_INSTALADA" "$TD_OPT/actual.nuevo" && mv -Tf "$TD_OPT/actual.nuevo" "$TD_OPT/actual"
      fi
      poner_imagen_env "$INSTALACION_ROOT_ENV" "$IMAGEN_INSTALADA"
      poner_imagen_env "$DATOS/instalacion.env" "$IMAGEN_INSTALADA"
      ULTIMA_ACTUALIZACION="$(ahora)"
      RESULTADO=vuelta_manual
      DETALLE="de $V_VIEJA a $VERSION_INSTALADA; $V_VIEJA no se reinstala sola hasta que haya una mayor"
      ULTIMA_CONSULTA="$(ahora)"
      guardar_estado
      log_evento "RESULTADO=vuelta_manual — $DETALLE"
      echo "Listo: Tatana volvió a $VERSION_INSTALADA. La $V_VIEJA queda marcada como fallida."
      exit 0 ;;
    2) echo "Tatana está capturando (${MOTIVO_OCUPADO:-ocupado}): probá cuando termine. No se cambió nada." >&2; exit 1 ;;
    3) echo "La versión anterior no arrancó: sigue la actual (${VERSION_INSTALADA:-?}). Ver $LOG_F." >&2; exit 1 ;;
    *) echo "No se pudo volver a la anterior (código $rc). Ver $LOG_F." >&2; exit 1 ;;
  esac
fi

# ══ --una-vez (§7.4) ═══════════════════════════════════════════════════════════════════════════════════════════════
# 0. Candado, configuración y recuperación.
if ! tomar_candado; then
  log_evento "hay otra operación de Tatana en curso (instalador o actualizador): se sale sin hacer nada"
  RESULTADO=ocupado_otro_proceso; DETALLE="instalación o actualización en curso"; ULTIMA_CONSULTA="$(ahora)"
  guardar_estado
  exit 0
fi
leer_actualizador_env || terminar sin_configuracion "falta o está mal $ACTUALIZADOR_ENV"
[[ "$HABILITADO" == 1 ]] || terminar deshabilitado "HABILITADO=0 en $ACTUALIZADOR_ENV"
[[ -f "$CA" ]] || terminar sin_configuracion "no está la CA $CA"
leer_instalacion_root || terminar sin_configuracion "$ERROR_INSTALACION"
preparar_docker || terminar error_docker "Docker no responde"
recuperar

# 1. Versión instalada.
leer_instalada || terminar error_docker "no existe el contenedor $CONTENEDOR (¿se desinstaló Tatana en Docker?)"
[[ "$VERSION_INSTALADA" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || terminar error_docker "no se sabe qué versión está instalada ($IMAGEN_INSTALADA)"
IMAGEN_PY="$IMAGEN_INSTALADA"
KIT_PY="$KIT_ACTUAL"

# 2. Consulta (el manifiesto lleva en la query el resultado de la corrida anterior).
HOST=""; PUERTO_CANAL=443
if [[ "$CANAL_URL" =~ ^https://([A-Za-z0-9.-]+)(:([0-9]+))?/ ]]; then
  HOST="${BASH_REMATCH[1]}"
  PUERTO_CANAL="${BASH_REMATCH[3]:-443}"
fi
if ! [[ "$HOST" =~ ^[0-9.]+$ ]]; then
  IP="$(getent ahostsv4 "$HOST" 2>/dev/null | awk 'NR == 1 {print $1}')"
  [[ -n "$IP" ]] || terminar sin_servidor "no se resuelve $HOST en esta PC (/etc/hosts: preparar-pc-perito.sh)"
  RESOLVE=(--resolve "$HOST:$PUERTO_CANAL:$IP")
fi
RESULTADO="$RESULTADO_PREVIO"
REPORTADO="$RESULTADO_PREVIO"
rm -f "$DESCARGAS/tatana-update-docker.json"
if ! curl_canal --max-time 30 -o /d/tatana-update-docker.json "${CANAL_URL}tatana-update-docker.json?$(consulta)" 2>>"$LOG_F"; then
  terminar sin_servidor "no responde ${CANAL_URL}tatana-update-docker.json"
fi
SERVIDOR_OK=1

# 3. Verificación con el verificador, las claves y el Python del kit/imagen INSTALADOS (D-T3), sin red.
SALIDA="$("${DOCKER[@]}" run --rm --platform linux/amd64 --network none --security-opt label=disable \
  -v "$KIT_ACTUAL:/k:ro" -v "$DESCARGAS/tatana-update-docker.json:/m.json:ro" --entrypoint "$PYTHON_IMAGEN" \
  "$IMAGEN_INSTALADA" -I /k/verificar-manifiesto-docker.py --manifiesto /m.json --claves /k/claves-confiables.txt \
  --version-instalada "$VERSION_INSTALADA" 2>>"$LOG_F")"
RC=$?
if [[ $RC == 1 ]]; then
  MOTIVO="$(clave_de "$SALIDA" MOTIVO)"
  case "$MOTIVO" in
    manifiesto_invalido|clave_desconocida|firma_invalida) terminar "$MOTIVO" "$(clave_de "$SALIDA" DETALLE)" ;;
  esac
fi
[[ $RC == 0 ]] || terminar error_docker "no se pudo correr el verificador (código $RC)"
V="$(clave_de "$SALIDA" VERSION)"
IMG="$(clave_de "$SALIDA" IMAGEN)"
ARCHIVO="$(clave_de "$SALIDA" ARCHIVO)"
TAMANO="$(clave_de "$SALIDA" TAMANO)"
SHA="$(clave_de "$SALIDA" SHA256)"
KIT_ARCHIVO="$(clave_de "$SALIDA" KIT_ARCHIVO)"
KIT_TAMANO="$(clave_de "$SALIDA" KIT_TAMANO)"
KIT_SHA="$(clave_de "$SALIDA" KIT_SHA256)"
COMPARACION="$(clave_de "$SALIDA" COMPARACION)"
if ! [[ "$V" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ && "$IMG" == "factum-tatana-docker:$V" && "$ARCHIVO" == "imagen-tatana-docker-v$V.tar.gz" &&
        "$KIT_ARCHIVO" == "kit-tatana-docker-v$V.tar.gz" && "$TAMANO" =~ ^[0-9]+$ && "$KIT_TAMANO" =~ ^[0-9]+$ &&
        "$SHA" =~ ^[0-9a-f]{64}$ && "$KIT_SHA" =~ ^[0-9a-f]{64}$ && "$COMPARACION" =~ ^(mayor|igual|menor)$ ]]; then
  terminar manifiesto_invalido "salida inesperada del verificador"
fi
VERSION_DISPONIBLE="$V"

# 4. Decisión de versión: nunca baja; una versión que ya falló no se reintenta.
[[ "$COMPARACION" == mayor ]] || terminar al_dia
fallida "$V" && terminar version_fallida "$V falló antes en esta PC"

# 5. Espacio.
MARGEN_KB=$(( ${FACTUM_TATANA_MARGEN_DISCO_MB:-0} * 1024 ))  # SOLO PRUEBAS: simula un disco lleno
libre_kb() { df -Pk "$1" 2>/dev/null | awk 'NR == 2 {print $4}'; }
RAIZ_DOCKER="$("${DOCKER[@]}" info -f '{{.DockerRootDir}}' 2>/dev/null || true)"
LIBRE_D="$(libre_kb "$RAIZ_DOCKER")"
NECESITA_D=$(( 4 * TAMANO / 1024 + 1048576 + MARGEN_KB ))
if [[ "$LIBRE_D" =~ ^[0-9]+$ ]]; then
  (( LIBRE_D >= NECESITA_D )) || terminar sin_espacio "falta $(( (NECESITA_D - LIBRE_D) / 1024 )) MB en $RAIZ_DOCKER, hay $(( LIBRE_D / 1024 )) MB"
else
  log_evento "no se pudo medir el espacio libre de ${RAIZ_DOCKER:-la carpeta de Docker}: se sigue"
fi
YA=0
[[ -f "$DESCARGAS/$ARCHIVO" ]] && YA=$(( YA + $(tamano_de "$DESCARGAS/$ARCHIVO") ))
[[ -f "$DESCARGAS/$KIT_ARCHIVO" ]] && YA=$(( YA + $(tamano_de "$DESCARGAS/$KIT_ARCHIVO") ))
LIBRE_V="$(libre_kb "$TD_VAR")"
NECESITA_V=$(( (TAMANO + KIT_TAMANO - YA) / 1024 + 102400 + MARGEN_KB ))
if [[ "$LIBRE_V" =~ ^[0-9]+$ ]] && (( LIBRE_V < NECESITA_V )); then
  terminar sin_espacio "falta $(( (NECESITA_V - LIBRE_V) / 1024 )) MB en $TD_VAR, hay $(( LIBRE_V / 1024 )) MB"
fi

# 6. Descarga (se reanuda; el nombre final solo con tamaño y SHA-256 del payload firmado).
bajar() { # bajar <archivo> <tamaño> <sha256> → 0 ok, 1 descarga interrumpida, 2 no coincide
  local f="$1" tam="$2" sha="$3" final="$DESCARGAS/$1" parcial="$DESCARGAS/$1.parcial" t
  if [[ -f "$final" ]]; then
    [[ "$(tamano_de "$final")" == "$tam" && "$(sha256_de "$final")" == "$sha" ]] && return 0
    rm -f "$final"
  fi
  t="$(tamano_de "$parcial")"
  [[ -n "$t" ]] && (( t > tam )) && { rm -f "$parcial"; t=""; }
  if [[ "$t" != "$tam" ]]; then
    curl_canal -C - --max-time 3600 --speed-limit 1024 --speed-time 120 -o "/d/$f.parcial" "${CANAL_URL}$f" 2>>"$LOG_F" || return 1
  fi
  if [[ "$(tamano_de "$parcial")" == "$tam" && "$(sha256_de "$parcial")" == "$sha" ]]; then
    mv -f "$parcial" "$final"
    return 0
  fi
  rm -f "$parcial"
  return 2
}
for par in "$KIT_ARCHIVO $KIT_TAMANO $KIT_SHA" "$ARCHIVO $TAMANO $SHA"; do
  # shellcheck disable=SC2086 # tres palabras sin espacios
  bajar $par
  case $? in
    1) terminar sin_servidor "descarga de ${par%% *} interrumpida (se reanuda en la próxima consulta)" ;;
    2) terminar hash_invalido "${par%% *} no coincide con el manifiesto firmado (se descartó)" ;;
  esac
done
log_evento "descargada y verificada la versión $V"

# 7. Carga y autoprueba: SOLO con Tatana libre (D-T5).
SALIDA="$(estado_agente estado)"
[[ "$(clave_de "$SALIDA" INALCANZABLE)" == 1 ]] && terminar error_agente "el agente no responde en el puerto $PUERTO"
# Acepta la conexión pero no contesta bien (timeout, reset): puede estar en medio de una captura → no se toca nada.
[[ -n "$(clave_de "$SALIDA" ERROR_AGENTE)" ]] && terminar error_agente "el agente no contestó bien ($(clave_de "$SALIDA" ERROR_AGENTE))"
[[ "$(clave_de "$SALIDA" LIBRE)" == 1 ]] || terminar esperando_libre "en curso: $(clave_de "$SALIDA" MOTIVOS)"
if ! "${DOCKER[@]}" load -i "$DESCARGAS/$ARCHIVO" >>"$LOG_F" 2>&1; then
  terminar imagen_invalida "docker load falló con $ARCHIVO"
fi
ETIQ="$("${DOCKER[@]}" image inspect -f '{{index .Config.Labels "org.opencontainers.image.version"}}' "$IMG" 2>/dev/null || true)"
if [[ "$ETIQ" != "$V" ]]; then
  borrar_imagen_si_se_puede "$IMG"
  rm -f "$DESCARGAS/$ARCHIVO"
  terminar imagen_invalida "la imagen cargada no es $IMG con la versión $V (etiqueta: ${ETIQ:-ninguna})"
fi
registrar_imagen "$IMG"
log_evento "autoprueba de $IMG..."
if ! "${DOCKER[@]}" run --rm --platform linux/amd64 --network none --cpus 1 "$IMG" --autoprueba-linux /tmp/autoprueba >"$DESCARGAS/autoprueba.txt" 2>&1; then
  tail -n 40 "$DESCARGAS/autoprueba.txt" | while IFS= read -r l; do log_evento "  | $l"; done
  marcar_fallida "$V"
  borrar_imagen_si_se_puede "$IMG"
  rm -f "$DESCARGAS/$ARCHIVO" "$DESCARGAS/$KIT_ARCHIVO"
  terminar autoprueba_fallida "$V no pasó --autoprueba-linux"
fi

# 8. Kit nuevo (ya verificado contra el payload firmado en el paso 6).
KIT_NUEVO="$TD_OPT/kits/$V"
if [[ "$KIT_NUEVO" != "$KIT_ACTUAL" ]]; then
  mkdir -p "$TD_OPT/kits"
  rm -rf -- "$KIT_NUEVO.parcial"
  mkdir -p "$KIT_NUEVO.parcial"
  if ! tar -xzf "$DESCARGAS/$KIT_ARCHIVO" -C "$KIT_NUEVO.parcial" --strip-components=1 --no-same-owner 2>>"$LOG_F"; then
    rm -rf -- "$KIT_NUEVO.parcial"
    terminar hash_invalido "no se pudo extraer $KIT_ARCHIVO"
  fi
  for f in comun.sh actualizador-tatana-docker.sh estado-agente.py verificar-manifiesto-docker.py claves-confiables.txt \
           "systemd/$UNIDAD_SERVICIO" "systemd/$UNIDAD_TIMER"; do
    [[ -f "$KIT_NUEVO.parcial/$f" ]] || { rm -rf -- "$KIT_NUEVO.parcial"; terminar hash_invalido "al kit $V le falta $f"; }
  done
  chown -R 0:0 "$KIT_NUEVO.parcial" 2>/dev/null
  chmod -R go-w "$KIT_NUEVO.parcial"
  rm -rf -- "$KIT_NUEVO"
  mv "$KIT_NUEVO.parcial" "$KIT_NUEVO"
fi

# 9. Cambio con vuelta atrás, con el docker run del kit NUEVO (D-T6).
run_completo_de_kit "$KIT_NUEVO" "$IMG" 2>>"$LOG_F" || terminar sin_configuracion "el kit $V no pudo armar el contenedor (ver $LOG_F)"
log_evento "cambiando el contenedor a $IMG (Tatana libre)..."
cambiar_contenedor "$IMG" "$V" 120
RC=$?
case $RC in
  0) ;;
  2) terminar esperando_libre "en curso: ${MOTIVO_OCUPADO:-ocupado} (empezó algo justo antes del cambio)" ;;
  3|6)
    marcar_fallida "$V"
    borrar_imagen_si_se_puede "$IMG"
    rm -rf -- "$KIT_NUEVO"
    rm -f -- "$DESCARGAS"/*
    if [[ $RC == 6 ]]; then terminar no_arranca "$V no arrancó y la anterior tampoco volvió a responder: revisar la PC"; fi
    terminar no_arranca "$V no respondió en 2 minutos${VERSION_RESPONDIO:+ (respondió $VERSION_RESPONDIO)}: sigue ${VERSION_INSTALADA}" ;;
  4) terminar error_docker "el contenedor $CONTENEDOR no es de Tatana en Docker: no se toca" ;;
  *) terminar error_agente "no se pudo pedir el modo mantenimiento (código $RC)" ;;
esac

# 10. Éxito: kit actual, units, estado, imágenes y limpieza.
ln -sfn "kits/$V" "$TD_OPT/actual.nuevo" && mv -Tf "$TD_OPT/actual.nuevo" "$TD_OPT/actual"
CAMBIARON=0
for u in "$UNIDAD_SERVICIO" "$UNIDAD_TIMER"; do
  if ! cmp -s "$KIT_NUEVO/systemd/$u" "$TD_SYSTEMD/$u"; then
    install -m 644 "$KIT_NUEVO/systemd/$u" "$TD_SYSTEMD/$u" && CAMBIARON=1
  fi
done
((CAMBIARON)) && command -v systemctl >/dev/null 2>&1 && systemctl daemon-reload
VERSION_ANTERIOR="$VERSION_INSTALADA"; IMAGEN_ANTERIOR="$IMAGEN_INSTALADA"
VERSION_INSTALADA="$V"; IMAGEN_INSTALADA="$IMG"
ULTIMA_ACTUALIZACION="$(ahora)"
registrar_imagen "$IMAGEN_ANTERIOR"
poner_imagen_env "$INSTALACION_ROOT_ENV" "$IMG"
poner_imagen_env "$DATOS/instalacion.env" "$IMG"
limpiar
terminar actualizada "de $VERSION_ANTERIOR a $V"
