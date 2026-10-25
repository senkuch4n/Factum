#!/usr/bin/env bash
# Factum en la fiscalía — actualiza el SERVIDOR (plan A) con un paquete de actualización y publica Tatana en Docker
# para las PCs de los peritos (SDD fiscalia-operacion §8.3–§8.5). Va en la raíz del paquete de actualización
# (Factum-Fiscalia-Actualizacion-vX.Y.Z/); se corre desde ahí, con sudo, en persona o por AnyDesk:
#
#   sudo bash actualizar.sh [--carpeta /srv/factum-fiscalia] [--si]
#                           [--sin-backup-previo] [--permitir-version-menor] [--solo-tatana]
#
#   --si                     no pregunta (AnyDesk con la terminal minimizada).
#   --sin-backup-previo      sin el backup previo obligatorio: pide escribir SIN BACKUP (con --si, además
#                            FACTUM_CONFIRMO_SIN_BACKUP=1 en el entorno).
#   --permitir-version-menor instalar una versión MENOR a la instalada (pide escribir MENOR; con --si, sin preguntar).
#   --solo-tatana            solo publicar Tatana en Docker (sin backup ni reinicio de la web).
#
# Pasos: [1/8] verifica el paquete (SHA256SUMS) y la versión · [2/8] requisitos (imágenes, espacio) · [3/8] backup
# previo (backup.sh) · [4/8] carga las imágenes nuevas · [5/8] copia los archivos (guarda los de antes en
# actualizacion-anterior/) · [6/8] levanta y espera que esté sano: si NO, vuelve sola a la versión anterior
# (la base NO se restaura sola) · [7/8] publica Tatana en Docker en el canal · [8/8] limpieza y resumen.
# Cortar AnyDesk no corta el script (la terminal vive en el escritorio del servidor). Registro de todo lo que muestra:
# <carpeta>/registro/actualizar-AAAAMMDD-HHMMSS.log.
set -euo pipefail
DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ -f "$DIR_SCRIPT/comun/_comun.sh" ]]; then
  PAQ="$DIR_SCRIPT" # raíz del paquete de actualización
else
  PAQ="$(cd "$DIR_SCRIPT/.." && pwd)" # copia en comun/ (repo o paquete completo)
fi
# shellcheck source=deploy/fiscalia/comun/_comun.sh
source "$PAQ/comun/_comun.sh"

CARPETA="$FACTUM_CARPETA_SERVIDOR"
FACTUM_SI=0
SIN_BACKUP_PREVIO=0
PERMITIR_MENOR=0
SOLO_TATANA=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --carpeta) CARPETA="${2:-}"; shift 2 ;;
    --si) FACTUM_SI=1; shift ;;
    --sin-backup-previo) SIN_BACKUP_PREVIO=1; shift ;;
    --permitir-version-menor) PERMITIR_MENOR=1; shift ;;
    --solo-tatana) SOLO_TATANA=1; shift ;;
    -h | --help) sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Argumento desconocido: $1" >&2; exit 2 ;;
  esac
done
export FACTUM_SI
[[ "$CARPETA" == /* ]] || CARPETA="$(pwd)/$CARPETA"
CARPETA="${CARPETA%/}"
T=8
ESPERA="${FACTUM_ESPERA_SANO:-300}" # segundos para quedar sano (SOLO las pruebas lo bajan)
[[ "$ESPERA" =~ ^[0-9]+$ ]] || ESPERA=300

echo ""
echo "=== Factum — actualizar el servidor de la fiscalía ($CARPETA) ==="
[[ "$(id -u)" == 0 ]] || morir "este script necesita permisos de administrador." "Corré: sudo bash $0 ${*:-}"

# Registro de todo lo que se muestra (root, 600).
if [[ -d "$CARPETA" ]]; then
  mkdir -p "$CARPETA/registro"
  chmod 700 "$CARPETA/registro"
  REGISTRO="$CARPETA/registro/actualizar-$(date '+%Y%m%d-%H%M%S').log"
  (umask 077 && : >"$REGISTRO")
  exec > >(tee -a "$REGISTRO") 2>&1
  echo "  Registro: $REGISTRO"
fi

# ══ 1. Paquete ═════════════════════════════════════════════════════════════════════════════════════════════════
paso 1 $T "Verificando el paquete"
if [[ ! -f "$PAQ/tipo.txt" ]]; then
  morir "este es un paquete de INSTALACIÓN, no de actualización." \
    "Para actualizar usá el paquete Factum-Fiscalia-Actualizacion-vX.Y.Z (o instalar-servidor.sh --reparar, sin backup previo)."
fi
[[ "$(tr -d '[:space:]' <"$PAQ/tipo.txt")" == actualizacion ]] || morir "tipo.txt del paquete no dice 'actualizacion'."
verificar_paquete "$PAQ"
V_NUEVA="$(tr -d '[:space:]' <"$PAQ/version.txt")"
[[ "$V_NUEVA" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || morir "version.txt del paquete no es una versión ('$V_NUEVA')."
[[ -f "$CARPETA/config/.env" ]] || morir "no hay una instalación de Factum en $CARPETA." "Indicá la carpeta con --carpeta <ruta>."
cargar_instalacion "$CARPETA"
[[ "$FACTUM_PLAN" == servidor ]] || morir "en $CARPETA está el plan '$FACTUM_PLAN': el plan B no se actualiza con este script en esta versión."
tomar_candado "$CARPETA"
V_INST="$FACTUM_VERSION_INSTALADA"
TATANA_SRC=""
V_TATANA=""
[[ -d "$PAQ/tatana-docker" ]] && { TATANA_SRC="$PAQ/tatana-docker"; V_TATANA="$(tr -d '[:space:]' <"$TATANA_SRC/VERSION")"; }
V_PUBLICADA="$(tr -d '[:space:]' <"$CARPETA/canal-tatana/docker/VERSION" 2>/dev/null || true)"
TATANA_NUEVO=0
if [[ -n "$V_TATANA" ]] && { [[ -z "$V_PUBLICADA" ]] || [[ "$(version_cmp "$V_TATANA" "$V_PUBLICADA")" == 1 ]]; }; then TATANA_NUEVO=1; fi
CMP="$(version_cmp "$V_NUEVA" "$V_INST")"
ok "Paquete de actualización a Factum $V_NUEVA (instalada: $V_INST)${V_TATANA:+; Tatana en Docker $V_TATANA}."
if ((SOLO_TATANA)); then
  [[ -n "$TATANA_SRC" ]] || morir "--solo-tatana, pero este paquete no trae Tatana en Docker (tatana-docker/)."
  ok "Modo solo Tatana (--solo-tatana): sin backup ni reinicio de la web."
elif [[ "$CMP" == -1 ]]; then
  ((PERMITIR_MENOR)) || morir "el paquete ($V_NUEVA) es MENOR que la versión instalada ($V_INST): no se baja de versión." \
    "Si de verdad querés volver a $V_NUEVA: --permitir-version-menor (conviene más restaurar el backup previo de la actualización)."
  aviso "Se va a instalar una versión MENOR ($V_NUEVA) que la instalada ($V_INST)."
  if [[ "${FACTUM_SI:-0}" != 1 ]]; then
    read -r -p "  Escribí MENOR para confirmar: " conf
    [[ "$conf" == MENOR ]] || morir "no se cambió nada."
  fi
elif [[ "$CMP" == 0 ]]; then
  if ((TATANA_NUEVO)); then
    SOLO_TATANA=1
    ok "Factum ya está en $V_INST: solo se publica Tatana en Docker $V_TATANA (sin backup ni reinicio)."
  else
    ok "Factum ya está en $V_INST${V_PUBLICADA:+ y Tatana en Docker $V_PUBLICADA ya está publicado}: nada que hacer."
    exit 0
  fi
fi

# ══ 2. Requisitos ══════════════════════════════════════════════════════════════════════════════════════════════
paso 2 $T "Revisando los requisitos (nada se toca hasta acá)"
requiere_docker
FALTAN=""
falta() { FALTAN="$FALTAN"$'\n'"      - $1"; }
SIN_AVX=0
modo_sin_avx && SIN_AVX=1
if ((!SOLO_TATANA)); then
  # Imágenes que trae el paquete (RepoTags del manifest.json de cada docker save).
  INCLUIDAS=""
  for f in "$PAQ"/imagenes/*.tar.gz; do
    [[ -f "$f" ]] || continue
    INCLUIDAS="$INCLUIDAS $(tar -xzOf "$f" manifest.json 2>/dev/null | tr ',' '\n' | sed -nE 's/.*"([a-z0-9._/-]+:[A-Za-z0-9._-]+)".*/\1/p' | tr '\n' ' ' || true)"
  done
  incluida() { [[ " $INCLUIDAS " == *" $1 "* ]]; }
  cargada() { docker image inspect "$1" >/dev/null 2>&1; }
  # requiere.txt: "imagen [con-avx|sin-avx]" por línea (las de Mongo dependen del modo de la instalación).
  while read -r img modo _; do
    [[ -n "$img" && "$img" != \#* ]] || continue
    [[ "$modo" == sin-avx && $SIN_AVX == 0 ]] && continue
    [[ "$modo" == con-avx && $SIN_AVX == 1 ]] && continue
    cargada "$img" || incluida "$img" || falta "$img (no está cargada en Docker ni viene en el paquete: armalo con --incluir-base)"
  done <"$PAQ/requiere.txt"
  if [[ -f "$PAQ/imagenes-de.txt" ]]; then
    V_DE="$(tr -d '[:space:]' <"$PAQ/imagenes-de.txt")"
    for img in "factum-backend:$V_DE" "factum-frontend-servidor:$V_DE"; do
      cargada "$img" || falta "$img (el paquete reusa las imágenes de $V_DE y este servidor no las tiene: armalo sin --imagenes-de)"
    done
  else
    for img in "factum-backend:$V_NUEVA" "factum-frontend-servidor:$V_NUEVA"; do
      incluida "$img" || cargada "$img" || falta "$img (no viene en imagenes/ del paquete)"
    done
  fi
  # Espacio: imágenes (3 × lo comprimido) en la carpeta de Docker; backup (evidencia + 1 GB) en backups/.
  # Sin imagenes/ (paquete con --imagenes-de) no hay nada que cargar. Ojo con pipefail: un du que falla no puede
  # cortar el script en silencio (pasó en CI, PR #103).
  KB_IMG=0
  if [[ -d "$PAQ/imagenes" ]]; then KB_IMG="$(du -sk "$PAQ/imagenes" 2>/dev/null | cut -f1 || true)"; fi
  [[ "$KB_IMG" =~ ^[0-9]+$ ]] || KB_IMG=0
  RAIZ_DOCKER="$(docker info -f '{{.DockerRootDir}}' 2>/dev/null || true)"
  LIBRE_D="$(df -Pk "${RAIZ_DOCKER:-/}" 2>/dev/null | awk 'NR == 2 {print $4}' || true)"
  if [[ "$LIBRE_D" =~ ^[0-9]+$ ]] && ((LIBRE_D < 3 * KB_IMG)); then
    falta "espacio para las imágenes: hacen falta $((3 * KB_IMG / 1024)) MB en ${RAIZ_DOCKER:-/} y quedan $((LIBRE_D / 1024)) MB"
  fi
  if ((!SIN_BACKUP_PREVIO)); then
    KB_EVID="$(du -sk "$(env_leer "$FACTUM_ENV" FACTUM_DATOS)/backend" 2>/dev/null | cut -f1 || true)"
    LIBRE_B="$(df -Pk "$CARPETA/backups" 2>/dev/null | awk 'NR == 2 {print $4}' || true)"
    if [[ "${KB_EVID:-}" =~ ^[0-9]+$ && "$LIBRE_B" =~ ^[0-9]+$ ]] && ((LIBRE_B < KB_EVID + 1048576)); then
      falta "espacio para el backup previo: hacen falta $(((KB_EVID + 1048576) / 1024)) MB en $CARPETA/backups y quedan $((LIBRE_B / 1024)) MB"
    fi
  fi
fi
if [[ -n "$TATANA_SRC" ]]; then
  [[ "$(version_de_manifiesto "$TATANA_SRC/tatana-update-docker.json")" == "$V_TATANA" ]] ||
    falta "tatana-docker/: el manifiesto no es de la versión $V_TATANA (VERSION)"
fi
[[ -z "$FALTAN" ]] || morir "no se puede actualizar (no se tocó nada):$FALTAN"
ok "Docker, imágenes y espacio en orden."

# ══ 3. Backup previo ═══════════════════════════════════════════════════════════════════════════════════════════
BACKUP_PREVIO=""
if ((SOLO_TATANA)); then
  paso 3 $T "Backup previo"
  ok "No hace falta (solo se publica Tatana en Docker; la web no se toca)."
else
  paso 3 $T "Backup previo (obligatorio: base, evidencia, configuración y certificados)"
  if ((SIN_BACKUP_PREVIO)); then
    aviso "--sin-backup-previo: si la actualización sale mal, NO hay un backup de este momento para volver."
    if [[ "${FACTUM_SI:-0}" == 1 ]]; then
      [[ "${FACTUM_CONFIRMO_SIN_BACKUP:-0}" == 1 ]] || morir "--sin-backup-previo con --si necesita FACTUM_CONFIRMO_SIN_BACKUP=1 en el entorno."
    else
      read -r -p "  Escribí SIN BACKUP para confirmar: " conf
      [[ "$conf" == "SIN BACKUP" ]] || morir "no se cambió nada."
    fi
  else
    echo "  La web y la API se pausan unos minutos durante el backup y la actualización (los peritos no pueden trabajar)."
    confirmar "¿Seguir con la actualización de $V_INST a $V_NUEVA?" || morir "cancelado: no se cambió nada."
    ANTES="$(find "$CARPETA/backups" -mindepth 1 -maxdepth 1 -type d -name 'factum-servidor-*' 2>/dev/null | sort | tail -1)"
    bash "$PAQ/comun/backup.sh" --carpeta "$CARPETA" ||
      morir "el backup previo falló: no se actualizó nada (la web vuelve sola)." "Revisá el espacio en disco (diagnostico.sh) y volvé a intentar."
    BACKUP_PREVIO="$(find "$CARPETA/backups" -mindepth 1 -maxdepth 1 -type d -name 'factum-servidor-*' 2>/dev/null | sort | tail -1)"
    [[ -n "$BACKUP_PREVIO" && "$BACKUP_PREVIO" != "$ANTES" ]] || morir "no encuentro el backup previo recién hecho en $CARPETA/backups."
    ok "Backup previo: $BACKUP_PREVIO"
  fi
fi

# ══ 4. Imágenes ════════════════════════════════════════════════════════════════════════════════════════════════
paso 4 $T "Imágenes nuevas"
if ((SOLO_TATANA)); then
  ok "Sin cambios."
else
  for f in "$PAQ"/imagenes/*.tar.gz; do
    [[ -f "$f" ]] || continue
    docker load -i "$f" >/dev/null || morir "no se pudo cargar $(basename "$f") en Docker (no se cambió nada más)."
    ok "$(basename "$f" .tar.gz)"
  done
  if [[ -f "$PAQ/imagenes-de.txt" ]]; then
    for r in factum-backend factum-frontend-servidor; do
      docker tag "$r:$V_DE" "$r:$V_NUEVA"
      ok "$r:$V_NUEVA = $r:$V_DE (misma imagen, sin cambios en la web ni la API)"
    done
  fi
  for img in "factum-backend:$V_NUEVA" "factum-frontend-servidor:$V_NUEVA"; do
    docker image inspect "$img" >/dev/null 2>&1 || morir "falta $img después de cargar las imágenes."
  done
fi

# ══ 5 y 6. Archivos y arranque, con vuelta atrás automática ══════════════════════════════════════════════════════
ANT="$CARPETA/actualizacion-anterior"
EN_CAMBIO=0
volver_atras() { # vuelve archivos e imágenes a los de antes (la base NO: eso es restaurar.sh con el backup previo)
  EN_CAMBIO=0
  set +e
  aviso "Volviendo a la versión anterior ($V_INST)..."
  rm -f "$CARPETA"/compose.*.yml
  cp "$ANT"/docker-compose.yml "$ANT"/compose.*.yml "$ANT"/version.txt "$CARPETA/" 2>/dev/null
  [[ -f "$ANT/Caddyfile" ]] && cp "$ANT/Caddyfile" "$CARPETA/"
  for d in mongo-init mongo-init-sin-avx scripts; do
    [[ -d "$ANT/$d" ]] && { rm -rf "${CARPETA:?}/$d"; cp -R "$ANT/$d" "$CARPETA/$d"; }
  done
  cp "$ANT/config.env" "$FACTUM_ENV" && chmod 600 "$FACTUM_ENV"
  fc up -d --remove-orphans --force-recreate >/dev/null 2>&1
  if esperar_sano "$ESPERA"; then
    ok "Factum $V_INST volvió a funcionar."
    set -e
    return 0
  fi
  mostrar_estado
  mostrar_log_backend
  falla "Ni la versión anterior quedó funcionando: guía instalacion-fiscalia.md, \"El instalador falló a mitad de camino\" (§9.7)."
  set -e
  return 1
}
al_salir() {
  local c=$?
  if ((EN_CAMBIO)) && ((c != 0)); then
    falla "La actualización se cortó a mitad de camino (código $c)."
    volver_atras || true
    [[ -n "$BACKUP_PREVIO" ]] && info "Backup previo: $BACKUP_PREVIO"
  fi
}
trap al_salir EXIT

paso 5 $T "Archivos de Factum $V_NUEVA"
if ((SOLO_TATANA)); then
  ok "Sin cambios."
else
  umask 022
  rm -rf "$ANT"
  mkdir -p "$ANT"
  cp "$CARPETA"/docker-compose.yml "$CARPETA"/compose.*.yml "$CARPETA"/version.txt "$ANT/"
  [[ -f "$CARPETA/Caddyfile" ]] && cp "$CARPETA/Caddyfile" "$ANT/"
  for d in mongo-init mongo-init-sin-avx scripts; do [[ -d "$CARPETA/$d" ]] && cp -R "$CARPETA/$d" "$ANT/$d"; done
  (umask 077 && cp "$FACTUM_ENV" "$ANT/config.env")
  chmod 700 "$ANT"
  ok "Los archivos de $V_INST quedan en $ANT/ (para volver atrás)."
  EN_CAMBIO=1
  copiar_archivos_instalacion "$PAQ" "$CARPETA" servidor "$SIN_AVX"
  mkdir -p "$CARPETA/canal-tatana/docker" "$(env_leer "$FACTUM_ENV" FACTUM_DATOS)/caddy-logs"
  chmod 755 "$CARPETA/canal-tatana" "$CARPETA/canal-tatana/docker"
  chmod 700 "$(env_leer "$FACTUM_ENV" FACTUM_DATOS)/caddy-logs"
  env_poner "$FACTUM_ENV" FACTUM_VERSION "$V_NUEVA"
  ok "Compose, Caddyfile y scripts de $V_NUEVA copiados; config/.env (salvo la versión), datos, appsettings.Local.json y branding/ quedan."
fi

paso 6 $T "Iniciando Factum $V_NUEVA"
if ((SOLO_TATANA)); then
  ok "Sin cambios (la web sigue andando)."
else
  P_HTTPS="$(env_leer "$FACTUM_ENV" FACTUM_PUERTO_HTTPS)"
  P_HTTPS="${P_HTTPS:-443}"
  CA_F="$CARPETA/$FACTUM_CA_ARCHIVO"
  [[ -f "$CA_F" ]] || CA_F="$(ruta_ca_caddy)"
  sano() {
    fc up -d --remove-orphans || return 1
    # El Caddyfile es un bind de UN archivo: recrear Caddy asegura que lea el nuevo.
    fc up -d --force-recreate --no-deps caddy || return 1
    esperar_sano "$ESPERA" || return 1
    curl -fsS -m 10 --resolve "$FACTUM_NOMBRE_RED:$P_HTTPS:127.0.0.1" --cacert "$CA_F" "https://$FACTUM_NOMBRE_RED:$P_HTTPS/health" >/dev/null || return 1
    curl -fsS -m 10 --resolve "$FACTUM_NOMBRE_RED:$P_HTTPS:127.0.0.1" --cacert "$CA_F" "https://$FACTUM_NOMBRE_RED:$P_HTTPS/health/ready" >/dev/null || return 1
  }
  if ! sano; then
    falla "Factum $V_NUEVA no quedó funcionando."
    mostrar_estado
    mostrar_log_backend
    if volver_atras; then
      echo ""
      echo "  ${_R}La actualización NO se aplicó:${_0} se volvió a $V_INST (archivos e imágenes de antes)."
      echo "  Tatana en Docker NO se publicó."
      [[ -n "$BACKUP_PREVIO" ]] && echo "  Backup previo: $BACKUP_PREVIO"
      echo "  Solo si la versión nueva llegó a modificar datos (la base NO se restaura sola):"
      echo "      sudo $CARPETA/scripts/restaurar.sh --desde ${BACKUP_PREVIO:-<backup previo>}"
      echo "  Registro: ${REGISTRO:-}"
    else
      [[ -n "$BACKUP_PREVIO" ]] && echo "  Backup previo: $BACKUP_PREVIO (sudo $CARPETA/scripts/restaurar.sh --desde $BACKUP_PREVIO)"
    fi
    exit 1
  fi
  EN_CAMBIO=0
  ok "Factum $V_NUEVA funcionando (https://$FACTUM_NOMBRE_RED/health y /health/ready)."
fi

# ══ 7. Tatana en Docker ════════════════════════════════════════════════════════════════════════════════════════
paso 7 $T "Tatana en Docker para las PCs de los peritos"
PUBLICADO=0
if [[ -n "$TATANA_SRC" ]]; then
  publicar_canal_tatana "$TATANA_SRC" "$CARPETA"
else
  ok "Este paquete no trae Tatana en Docker${V_PUBLICADA:+ (sigue publicado $V_PUBLICADA)}."
fi

# ══ 8. Limpieza y resumen ══════════════════════════════════════════════════════════════════════════════════════
paso 8 $T "Limpieza y resumen"
if ((!SOLO_TATANA)); then
  # Solo imágenes de Factum (nunca mongo/caddy ni ajenas): quedan la versión nueva y la anterior.
  docker image ls --format '{{.Repository}}:{{.Tag}}' 2>/dev/null |
    grep -E '^factum-(backend|frontend-servidor|frontend):' | while IFS= read -r img; do
      t="${img##*:}"
      [[ "$t" == "$V_NUEVA" || "$t" == "$V_INST" ]] && continue
      docker ps -aq --filter "ancestor=$img" | grep -q . && continue
      docker image rm "$img" >/dev/null 2>&1 && info "Imagen vieja borrada: $img"
    done
fi
V_CANAL="$(tr -d '[:space:]' <"$CARPETA/canal-tatana/docker/VERSION" 2>/dev/null || true)"
echo ""
echo "  ${_V}Factum $(env_leer "$FACTUM_ENV" FACTUM_VERSION) funcionando.${_0}"
if ((PUBLICADO)); then
  echo "  Tatana en Docker $V_CANAL publicado: las PCs se actualizan solas en los próximos ~40 minutos mientras no estén"
  echo "  capturando. Ver: sudo $CARPETA/scripts/estado-peritos.sh"
elif [[ -n "$V_CANAL" ]]; then
  echo "  Tatana en Docker publicado: $V_CANAL (ver: sudo $CARPETA/scripts/estado-peritos.sh)"
fi
[[ -n "$BACKUP_PREVIO" ]] && echo "  Backup previo: $BACKUP_PREVIO"
echo "  Registro: ${REGISTRO:-}"
echo ""
