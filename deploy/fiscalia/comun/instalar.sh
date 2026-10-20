#!/usr/bin/env bash
# Factum en la fiscalía — instalador de los dos planes (lo llaman servidor/instalar-servidor.sh y
# una-pc/instalar-una-pc.sh; no hace falta correrlo directo). SDD instalacion-fiscalia §3.2 y §3.4.
#
#   instalar.sh --plan servidor|una-pc [--carpeta <ruta>] [--reparar] [--desinstalar [--borrar-datos]]
#               [--superadmin-dni <DNI> --superadmin-nombre "<Nombre Apellido>"] [--si] [--sin-hosts] [--sin-avx]
#
#   --reparar       vuelve a copiar compose/scripts y cargar imágenes; no toca config/.env ni datos.
#   --desinstalar   para y borra los contenedores (y la línea de /etc/hosts del plan A). Los datos,
#                   la configuración y los backups QUEDAN en la carpeta, salvo --borrar-datos.
#   --sin-hosts     no toca /etc/hosts (solo para probar el compose en otra máquina; no necesita sudo).
#   --si            responde "sí" a las preguntas (para correrlo sin terminal).
#   --sin-avx       MongoDB 4.4 en vez de 7 (PCs cuyo procesador no tiene AVX). Si no se pasa y la PC no
#                   tiene AVX, el instalador lo pregunta. Queda guardado en config/.env (FACTUM_MONGO_SIN_AVX=1).
set -euo pipefail
DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deploy/fiscalia/comun/_comun.sh
source "$DIR_SCRIPT/_comun.sh"
PAQ="$(cd "$DIR_SCRIPT/.." && pwd)"

PLAN=""
CARPETA=""
REPARAR=0
DESINSTALAR=0
BORRAR_DATOS=0
SIN_HOSTS=0
SA_DNI=""
SA_NOMBRE=""
FACTUM_SI=0
SIN_AVX=0
uso() {
  sed -n '5,15p' "$0" | sed 's/^# \{0,1\}//' >&2
  exit 2
}
while [[ $# -gt 0 ]]; do
  case "$1" in
    --plan) PLAN="${2:-}"; shift 2 ;;
    --carpeta) CARPETA="${2:-}"; shift 2 ;;
    --reparar) REPARAR=1; shift ;;
    --desinstalar) DESINSTALAR=1; shift ;;
    --borrar-datos) BORRAR_DATOS=1; shift ;;
    --sin-hosts) SIN_HOSTS=1; shift ;;
    --superadmin-dni) SA_DNI="${2:-}"; shift 2 ;;
    --superadmin-nombre) SA_NOMBRE="${2:-}"; shift 2 ;;
    --si) FACTUM_SI=1; shift ;;
    --sin-avx) SIN_AVX=1; shift ;;
    -h | --help) uso ;;
    *) echo "Argumento desconocido: $1" >&2; uso ;;
  esac
done
export FACTUM_SI
[[ "$PLAN" == servidor || "$PLAN" == una-pc ]] || uso
((REPARAR + DESINSTALAR <= 1)) || morir "--reparar y --desinstalar son excluyentes."
((BORRAR_DATOS == 0 || DESINSTALAR == 1)) || morir "--borrar-datos va solo con --desinstalar."
# El plan B no toca /etc/hosts nunca.
[[ "$PLAN" == una-pc ]] && SIN_HOSTS=1

if [[ "$PLAN" == servidor ]]; then
  [[ -n "$CARPETA" ]] || CARPETA="$FACTUM_CARPETA_SERVIDOR"
  PROYECTO="${FACTUM_PROYECTO_PRUEBA:-factum-fiscalia}"
  TITULO="Factum — servidor de la fiscalía (plan A)"
  IMAGENES="backend frontend-servidor caddy"
else
  [[ -n "$CARPETA" ]] || CARPETA="$FACTUM_CARPETA_UNA_PC"
  PROYECTO="${FACTUM_PROYECTO_PRUEBA:-factum-una-pc}"
  TITULO="Factum — todo en una PC (plan B)"
  IMAGENES="backend frontend-una-pc"
fi
[[ "$CARPETA" == /* ]] || CARPETA="$(pwd)/$CARPETA"
CARPETA="${CARPETA%/}"
ENV_F="$CARPETA/config/.env"
# --reparar de una instalación que ya está en modo sin AVX: lo sigue estando (aunque no se pase el flag).
((REPARAR)) && [[ "$(env_leer "$ENV_F" FACTUM_MONGO_SIN_AVX)" == 1 ]] && SIN_AVX=1

# Puertos de la prueba (solo variables de entorno; nunca en la fiscalía).
P_HTTP="${FACTUM_PUERTO_HTTP:-80}"
P_HTTPS="${FACTUM_PUERTO_HTTPS:-443}"
P_WEB="${FACTUM_PUERTO_WEB:-3000}"
P_API="${FACTUM_PUERTO_API:-8080}"

echo ""
echo "=== $TITULO ==="

if [[ "$(id -u)" != 0 && $SIN_HOSTS == 0 ]]; then
  morir "este paso necesita permisos de administrador." "Corré: sudo bash $0 ${*:-}"
fi
es_linux || aviso "Esta PC no es Linux: modo de prueba (sin chequeos de CPU ni /etc/hosts del sistema)."

# ══ Desinstalar ═══════════════════════════════════════════════════════════════════════════════
if ((DESINSTALAR)); then
  requiere_docker
  cargar_instalacion "$CARPETA"
  [[ "$FACTUM_PLAN" == "$PLAN" ]] || morir "en $CARPETA está instalado el plan '$FACTUM_PLAN', no '$PLAN'."
  paso 1 3 "Parando y borrando los contenedores de $FACTUM_PROYECTO"
  fc down --remove-orphans
  ok "Contenedores y redes borrados (las imágenes quedan cargadas en Docker)."
  paso 2 3 "Nombre factum.internal"
  if [[ "$PLAN" == servidor && $SIN_HOSTS == 0 ]]; then
    hosts_quitar
    ok "Línea de factum.internal quitada de /etc/hosts."
  else
    ok "Nada que hacer."
  fi
  paso 3 3 "Datos"
  if ((BORRAR_DATOS)); then
    echo "  ${_R}ATENCIÓN:${_0} se van a BORRAR para siempre la base, la evidencia, la configuración y los"
    echo "  backups de $CARPETA. Si no hay un backup en otro lado, se pierde todo."
    if [[ "${FACTUM_SI:-0}" != 1 ]]; then
      read -r -p "  Escribí BORRAR para confirmar: " conf
      [[ "$conf" == BORRAR ]] || morir "no se borró nada (los contenedores ya están parados)."
    fi
    rm -rf -- "${CARPETA:?}"
    ok "Carpeta $CARPETA borrada."
  else
    ok "Los datos, la configuración y los backups quedan en $CARPETA."
    info "Para volver a levantar Factum: el instalador con --reparar."
  fi
  exit 0
fi

TOTAL=10

# ══ 1. Paquete ══════════════════════════════════════════════════════════════════════════════
paso 1 $TOTAL "Verificando el paquete"
[[ -f "$PAQ/version.txt" ]] || morir "no encuentro version.txt en $PAQ." "Corré el script desde la carpeta del paquete descomprimido."
VERSION="$(tr -d '[:space:]' <"$PAQ/version.txt")"
verificar_paquete "$PAQ"
for img in $IMAGENES; do
  [[ -f "$PAQ/imagenes/$img.tar" ]] || morir "al paquete le falta imagenes/$img.tar."
done
ok "Factum versión $VERSION."

# ══ 2. Requisitos ═══════════════════════════════════════════════════════════════════════════
paso 2 $TOTAL "Revisando los requisitos de esta PC"
if es_linux && [[ -f "$PAQ/herramientas/instalar-docker-offline.sh" ]]; then
  if ! command -v docker >/dev/null 2>&1; then
    aviso "Docker no está instalado. El paquete lo trae (binarios oficiales, sin internet)."
    confirmar "¿Instalar Docker desde el paquete?" || morir "instalación cancelada. No se tocó nada." "Para instalarlo de otra forma: guía, sección 2.3."
    bash "$PAQ/herramientas/instalar-docker-offline.sh" || morir "no se pudo instalar Docker (ver arriba)."
  elif ! docker compose version >/dev/null 2>&1; then
    aviso "Docker no tiene el complemento compose. El paquete lo trae."
    confirmar "¿Instalar docker compose desde el paquete?" || morir "instalación cancelada. No se tocó nada."
    bash "$PAQ/herramientas/instalar-docker-offline.sh" --solo-compose || morir "no se pudo instalar docker compose."
  fi
fi
requiere_docker
ok "Docker $(docker version --format '{{.Server.Version}}' 2>/dev/null) con $(docker compose version --short 2>/dev/null | sed 's/^/compose /')."
if es_linux; then
  [[ "$(uname -m)" == x86_64 ]] || morir "esta PC es $(uname -m); Factum necesita una PC de 64 bits (x86_64)."
  if ((SIN_AVX)); then
    ok "Procesador x86_64. Modo sin AVX (--sin-avx): base de datos MongoDB 4.4."
  elif cpu_tiene_avx; then
    ok "Procesador x86_64 con AVX."
  else
    aviso "El procesador de esta PC no tiene AVX: la base de datos MongoDB 7 no arranca sin eso."
    info "Factum puede usar MongoDB 4.4, que no lo necesita (modo sin AVX)."
    if [[ "${FACTUM_SI:-0}" != 1 && ! -t 0 ]]; then
      morir "esta PC no tiene AVX." "Volvé a correr el instalador con --sin-avx (usa MongoDB 4.4)."
    fi
    confirmar "Esta PC no tiene AVX. ¿Usar MongoDB 4.4?" || morir "esta PC no tiene AVX y no se eligió MongoDB 4.4. No se tocó nada." \
      "Volvé a correr el instalador con --sin-avx, o usá otra PC (Intel/AMD de 2012 en adelante, salvo algunos Celeron/Pentium/Atom)."
    SIN_AVX=1
    ok "Modo sin AVX: base de datos MongoDB 4.4."
  fi
elif ((SIN_AVX)); then
  ok "Modo sin AVX (--sin-avx): base de datos MongoDB 4.4."
fi
if ((SIN_AVX)); then MONGO_TAR=mongo-4.4; else MONGO_TAR=mongo; fi
[[ -f "$PAQ/imagenes/$MONGO_TAR.tar" ]] || morir "al paquete le falta imagenes/$MONGO_TAR.tar." \
  "$( ((SIN_AVX)) && echo "Aplicá el parche sin AVX (parche-sin-avx/LEEME.txt) o copiá el paquete completo nuevo.")"
IMAGENES="$MONGO_TAR $IMAGENES"
RAM="$(ram_gb)"
if ((RAM > 0 && RAM < 4)); then
  aviso "La PC tiene ${RAM} GB de RAM; se recomiendan 4 GB o más."
  confirmar "¿Seguir igual?" || morir "instalación cancelada. No se tocó nada."
elif ((RAM > 0)); then
  ok "${RAM} GB de RAM."
fi
BASE="$CARPETA"
while [[ ! -d "$BASE" ]]; do BASE="$(dirname "$BASE")"; done
LIBRE="$(disco_libre_gb "$BASE")"
if [[ -n "$LIBRE" ]] && ((LIBRE < 40)); then
  aviso "Quedan ${LIBRE} GB libres en $BASE; se recomiendan 40 GB (la evidencia ocupa mucho)."
  ((REPARAR)) || confirmar "¿Seguir igual?" || morir "instalación cancelada. No se tocó nada."
else
  ok "${LIBRE:-?} GB libres en $BASE."
fi
# Con una instalación ya hecha, el paso 3 da el mensaje correcto (sus propios contenedores ocupan los puertos).
if ((!REPARAR)) && [[ ! -f "$ENV_F" ]]; then
  if [[ "$PLAN" == servidor ]]; then PUERTOS="$P_HTTP $P_HTTPS"; else PUERTOS="$P_WEB $P_API"; fi
  for p in $PUERTOS; do
    set +e
    puerto_ocupado "$p"
    r=$?
    set -e
    case $r in
      0) morir "el puerto $p ya está en uso en esta PC." "Si es un Factum anterior, usá --reparar; si es otro programa (Apache, nginx...), paralo: sudo ss -ltnp | grep :$p" ;;
      2) aviso "No pude comprobar si el puerto $p está libre (faltan ss y lsof)." ;;
    esac
  done
  ok "Puertos libres: $PUERTOS."
fi

# ══ 3. ¿Ya instalado? ═══════════════════════════════════════════════════════════════════════
paso 3 $TOTAL "Revisando si Factum ya está instalado en $CARPETA"
if [[ -f "$ENV_F" && $REPARAR == 0 ]]; then
  morir "Factum ya está instalado en $CARPETA." "Para volver a levantarlo sin tocar datos: el instalador con --reparar."
fi
if ((REPARAR)); then
  [[ -f "$ENV_F" ]] || morir "no hay una instalación en $CARPETA para reparar." "Corré el instalador sin --reparar."
  [[ "$(env_leer "$ENV_F" FACTUM_PLAN)" == "$PLAN" ]] || morir "en $CARPETA hay otro plan instalado."
  # Pasar a MongoDB 4.4 una base creada por MongoDB 7 no se puede (4.4 no abre esos archivos).
  if ((SIN_AVX)) && [[ "$(env_leer "$ENV_F" FACTUM_MONGO_SIN_AVX)" != 1 && -f "$CARPETA/datos/mongo/WiredTiger" ]]; then
    morir "la base de $CARPETA la creó MongoDB 7: MongoDB 4.4 (modo sin AVX) no la puede abrir." \
      "Si la instalación anterior nunca funcionó (no tiene casos): desinstalala con --desinstalar --borrar-datos y volvé a instalar con --sin-avx."
  fi
  ok "Modo reparar: se conserva config/.env y los datos."
else
  ok "Instalación nueva."
fi

# ══ 4. Primer superadmin ════════════════════════════════════════════════════════════════════
paso 4 $TOTAL "Primer superadmin (el que después crea las cuentas de los peritos)"
SA_TEMPORAL=""
if ((REPARAR)); then
  # Una instalación que falló antes de terminar todavía tiene el superadmin pendiente en config/.env:
  # se muestra al final, porque el intento anterior no llegó a mostrarlo.
  SA_DNI="$(env_leer "$ENV_F" FACTUM_SUPERADMIN_0_DNI)"
  SA_TEMPORAL="$(env_leer "$ENV_F" FACTUM_SUPERADMIN_0_TEMPORAL)"
  ok "Sin cambios (modo reparar)."
else
  while [[ ! "$SA_DNI" =~ ^[0-9]{7,8}$ ]]; do
    [[ -z "$SA_DNI" ]] || aviso "El DNI tiene que tener 7 u 8 dígitos, sin puntos."
    [[ -t 0 ]] || morir "falta --superadmin-dni (7 u 8 dígitos)."
    read -r -p "  DNI del superadmin (sin puntos): " SA_DNI
  done
  # shellcheck disable=SC1003 # '"$\\' = comillas, $ y barra invertida (no van en el .env)
  SA_NOMBRE="$(printf '%s' "$SA_NOMBRE" | tr -d '"$\\' | sed 's/^ *//; s/ *$//')"
  while [[ -z "$SA_NOMBRE" || ${#SA_NOMBRE} -gt 100 ]]; do
    [[ -t 0 ]] || morir "falta --superadmin-nombre."
    read -r -p "  Nombre y apellido del superadmin: " SA_NOMBRE
    # shellcheck disable=SC1003
    SA_NOMBRE="$(printf '%s' "$SA_NOMBRE" | tr -d '"$\\' | sed 's/^ *//; s/ *$//')"
  done
  SA_TEMPORAL="$(generar_temporal)"
  ok "Superadmin: $SA_NOMBRE (DNI $SA_DNI). La contraseña temporal se muestra al final."
fi

# ══ 5. Carpetas y configuración ═════════════════════════════════════════════════════════════
paso 5 $TOTAL "Carpetas y configuración en $CARPETA"
umask 077
mkdir -p "$CARPETA"/{config/branding,datos/mongo,datos/backend,backups,scripts}
[[ "$PLAN" == servidor ]] && mkdir -p "$CARPETA"/datos/{caddy-data,caddy-config}
chmod 700 "$CARPETA/config" "$CARPETA/datos" "$CARPETA/backups"
chmod 755 "$CARPETA" "$CARPETA/scripts"
if ((!REPARAR)); then
  cat >"$ENV_F" <<EOF
# Factum en la fiscalía — configuración de ESTA instalación. La generó el instalador el $(date '+%Y-%m-%d %H:%M').
# Tiene secretos propios de esta PC: no compartir, no copiar a otra PC salvo para restaurar un backup.
COMPOSE_PROJECT_NAME=$PROYECTO
FACTUM_PLAN=$PLAN
FACTUM_VERSION=$VERSION
FACTUM_HOME=$CARPETA
FACTUM_DATOS=$CARPETA/datos
FACTUM_JWT_SECRET=$(generar_hex 64)
FACTUM_MONGO_ROOT_PASSWORD=$(generar_hex 24)
FACTUM_MONGO_APP_PASSWORD=$(generar_hex 24)
FACTUM_MONGO_CACHE_GB=0.5
# Sesión: el token dura N h y se renueva con actividad, hasta el tope de horas desde el login.
FACTUM_JWT_EXPIRY_HOURS=2
FACTUM_SESION_TOPE_HORAS=12
# Primer superadmin: solo durante la instalación (el instalador vacía estas tres claves al terminar).
FACTUM_SUPERADMIN_0_DNI=$SA_DNI
FACTUM_SUPERADMIN_0_NOMBRE="$SA_NOMBRE"
FACTUM_SUPERADMIN_0_TEMPORAL=$SA_TEMPORAL
# Sellado de tiempo RFC 3161 (necesita internet). Vacío = desactivado.
FACTUM_SELLADO_URLS=
FACTUM_SELLADO_HABILITADO=true
FACTUM_RELOJ_UMBRAL_SEGUNDOS=60
FACTUM_GRABACION_SESION_MODO=optional_off
FACTUM_GRABACION_SESION_TRAMO_MINUTOS=30
FACTUM_TATANA_VERSION_MINIMA=
# Backups: cuántos conservar en $CARPETA/backups.
FACTUM_BACKUP_CONSERVAR=10
EOF
  # Puertos de prueba: solo si vinieron por variable de entorno (en la fiscalía no se escriben).
  [[ -n "${FACTUM_PUERTO_HTTP:-}" ]] && echo "FACTUM_PUERTO_HTTP=$P_HTTP" >>"$ENV_F"
  [[ -n "${FACTUM_PUERTO_HTTPS:-}" ]] && echo "FACTUM_PUERTO_HTTPS=$P_HTTPS" >>"$ENV_F"
  [[ -n "${FACTUM_PUERTO_WEB:-}" ]] && echo "FACTUM_PUERTO_WEB=$P_WEB" >>"$ENV_F"
  [[ -n "${FACTUM_PUERTO_API:-}" ]] && echo "FACTUM_PUERTO_API=$P_API" >>"$ENV_F"
  ((SIN_AVX)) && printf '# Modo sin AVX: MongoDB 4.4 (comun/compose.sin-avx.yml).\nFACTUM_MONGO_SIN_AVX=1\n' >>"$ENV_F"
  chmod 600 "$ENV_F"
  ok "config/.env creado (permisos 600) con secretos propios de esta PC."
else
  ((SIN_AVX)) && env_poner "$ENV_F" FACTUM_MONGO_SIN_AVX 1
  ok "Se conserva config/.env$( ((SIN_AVX)) && echo " (modo sin AVX: MongoDB 4.4)")."
fi
[[ -f "$CARPETA/config/appsettings.Local.json" ]] || cp "$PAQ/comun/appsettings.Local.example.json" "$CARPETA/config/appsettings.Local.json"
chmod 644 "$CARPETA/config/appsettings.Local.json"
chmod 755 "$CARPETA/config/branding"

# ══ 6. Archivos ═════════════════════════════════════════════════════════════════════════════
paso 6 $TOTAL "Copiando los archivos de Factum"
umask 022
cp "$PAQ/comun/docker-compose.yml" "$PAQ/comun/compose.superadmin.yml" "$PAQ/version.txt" "$CARPETA/"
rm -rf "$CARPETA/mongo-init"
cp -R "$PAQ/comun/mongo-init" "$CARPETA/mongo-init"
if ((SIN_AVX)); then
  [[ -f "$PAQ/comun/compose.sin-avx.yml" && -d "$PAQ/comun/mongo-init-sin-avx" ]] ||
    morir "al paquete le faltan comun/compose.sin-avx.yml o comun/mongo-init-sin-avx/." "Aplicá el parche sin AVX (parche-sin-avx/LEEME.txt)."
  cp "$PAQ/comun/compose.sin-avx.yml" "$CARPETA/"
  rm -rf "$CARPETA/mongo-init-sin-avx"
  cp -R "$PAQ/comun/mongo-init-sin-avx" "$CARPETA/mongo-init-sin-avx"
fi
if [[ "$PLAN" == servidor ]]; then
  cp "$PAQ/servidor/compose.servidor.yml" "$PAQ/servidor/Caddyfile" "$CARPETA/"
else
  cp "$PAQ/una-pc/compose.una-pc.yml" "$CARPETA/"
fi
cp "$PAQ/comun/_comun.sh" "$PAQ/comun/backup.sh" "$PAQ/comun/restaurar.sh" "$PAQ/comun/diagnostico.sh" \
  "$PAQ/comun/borrar-caso-prueba.sh" "$CARPETA/scripts/"
chmod 755 "$CARPETA"/scripts/*.sh
[[ -f "$PAQ/instalacion-fiscalia.md" ]] && cp "$PAQ/instalacion-fiscalia.md" "$CARPETA/"
cargar_instalacion "$CARPETA"
# En --reparar con un paquete de otra versión: se avisa (la actualización no es parte de esta entrega).
if [[ "$FACTUM_VERSION_INSTALADA" != "$VERSION" ]]; then
  aviso "La versión instalada es $FACTUM_VERSION_INSTALADA y el paquete es $VERSION: se pasa a $VERSION."
  confirmar "¿Seguir? (antes conviene un backup: scripts/backup.sh)" || morir "cancelado."
  env_poner "$ENV_F" FACTUM_VERSION "$VERSION"
fi
ok "Archivos copiados."

# ══ 7. Imágenes ═════════════════════════════════════════════════════════════════════════════
paso 7 $TOTAL "Cargando las imágenes en Docker (puede tardar unos minutos)"
for img in $IMAGENES; do
  docker load -i "$PAQ/imagenes/$img.tar" >/dev/null || morir "no se pudo cargar imagenes/$img.tar en Docker."
  ok "$img"
done

# ══ 8. Arranque ═════════════════════════════════════════════════════════════════════════════
paso 8 $TOTAL "Iniciando Factum (la primera vez tarda un par de minutos)"
if ! fc up -d --remove-orphans; then
  # Un primer arranque lento de la base (PC lenta, disco lento) hace que compose dé por "unhealthy" la
  # dependencia antes de tiempo: se espera a la base y se reintenta una vez.
  aviso "Docker compose no pudo levantar todo de una vez; espero a la base de datos y reintento..."
  limite=$((SECONDS + 240))
  until [[ "$(estado_servicio mongo)" == healthy ]] || ((SECONDS > limite)); do sleep 5; done
  fc up -d --remove-orphans || { mostrar_estado; mostrar_log_backend; morir "docker compose up falló (ver arriba)."; }
fi
if ! esperar_sano 300; then
  mostrar_estado
  mostrar_log_backend
  morir "Factum no quedó funcionando a tiempo." "Revisá el registro de arriba y, corregido el problema, corré el instalador con --reparar. Diagnóstico: $CARPETA/scripts/diagnostico.sh"
fi
ok "Todos los servicios están sanos."
if [[ -n "$(env_leer "$ENV_F" FACTUM_SUPERADMIN_0_DNI)" ]]; then
  # El backend crea el superadmin ANTES de empezar a responder /health: ya está en la base.
  env_vaciar "$ENV_F" FACTUM_SUPERADMIN_0_DNI
  env_vaciar "$ENV_F" FACTUM_SUPERADMIN_0_NOMBRE
  env_vaciar "$ENV_F" FACTUM_SUPERADMIN_0_TEMPORAL
  fc up -d --remove-orphans >/dev/null 2>&1 || true
  esperar_sano 240 || { mostrar_log_backend; morir "el backend no volvió a arrancar después de crear el superadmin."; }
  ok "Superadmin creado; la contraseña temporal ya no queda en config/.env."
fi

# ══ 9. Nombre y certificado (plan A) / acceso directo (plan B) ══════════════════════════════
if [[ "$PLAN" == servidor ]]; then
  paso 9 $TOTAL "Nombre factum.internal y certificado"
  if ((SIN_HOSTS)); then
    aviso "--sin-hosts: no se tocó /etc/hosts (modo prueba)."
  else
    hosts_fijar 127.0.0.1
    ok "/etc/hosts: 127.0.0.1 factum.internal"
  fi
  CA_ORIGEN="$(ruta_ca_caddy)"
  limite=$((SECONDS + 60))
  until [[ -s "$CA_ORIGEN" ]] || ((SECONDS > limite)); do sleep 2; done
  [[ -s "$CA_ORIGEN" ]] || { fc logs --no-color --tail 30 caddy || true; morir "Caddy no generó su certificado raíz ($CA_ORIGEN)."; }
  cp "$CA_ORIGEN" "$CARPETA/$FACTUM_CA_ARCHIVO"
  chmod 644 "$CARPETA/$FACTUM_CA_ARCHIVO"
  if cp "$CA_ORIGEN" "$PAQ/$FACTUM_CA_ARCHIVO" 2>/dev/null; then
    chmod 644 "$PAQ/$FACTUM_CA_ARCHIVO" 2>/dev/null || true
    ok "Certificado exportado: $PAQ/$FACTUM_CA_ARCHIVO (y copia en $CARPETA/)."
  else
    aviso "No pude copiarlo al paquete (¿pendrive de solo lectura?). Está en $CARPETA/$FACTUM_CA_ARCHIVO."
  fi
  HUELLA=""
  command -v openssl >/dev/null 2>&1 && HUELLA="$(openssl x509 -in "$CA_ORIGEN" -noout -fingerprint -sha256 2>/dev/null | cut -d= -f2)"
  [[ -n "$HUELLA" ]] && info "Huella SHA-256 del certificado: $HUELLA"
  if command -v curl >/dev/null 2>&1; then
    if curl -fsS -m 10 --resolve "$FACTUM_NOMBRE_RED:$P_HTTPS:127.0.0.1" --cacert "$CA_ORIGEN" \
      "https://$FACTUM_NOMBRE_RED:$P_HTTPS/health" >/dev/null; then
      ok "https://$FACTUM_NOMBRE_RED/health responde con el certificado de Factum."
    else
      aviso "https://$FACTUM_NOMBRE_RED/health no respondió todavía (diagnostico.sh lo vuelve a probar)."
    fi
  fi
  if command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null | grep -q "Status: active"; then
    info "ufw está activo. Docker publica 80/443 por su cuenta, pero si los peritos no llegan:"
    info "  sudo ufw allow 80/tcp && sudo ufw allow 443/tcp   (guía, problemas frecuentes)"
  fi
else
  paso 9 $TOTAL "Acceso directo y Tatana"
  U="$(usuario_real)"
  HU="$(home_de "$U")"
  if es_linux && [[ -d "$HU" ]]; then
    ESCRITORIO="$(como_usuario "$U" xdg-user-dir DESKTOP 2>/dev/null || true)"
    [[ -n "$ESCRITORIO" && -d "$ESCRITORIO" ]] || ESCRITORIO="$HU/Escritorio"
    [[ -d "$ESCRITORIO" ]] || ESCRITORIO="$HU/Desktop"
    DESKTOP_TXT="[Desktop Entry]
Type=Application
Name=Factum
Comment=Abrir Factum (http://localhost:3000)
Exec=xdg-open http://localhost:3000
Icon=web-browser
Terminal=false
Categories=Office;
"
    como_usuario "$U" mkdir -p "$HU/.local/share/applications"
    printf '%s' "$DESKTOP_TXT" | como_usuario "$U" tee "$HU/.local/share/applications/factum.desktop" >/dev/null
    if [[ -d "$ESCRITORIO" ]]; then
      printf '%s' "$DESKTOP_TXT" | como_usuario "$U" tee "$ESCRITORIO/factum.desktop" >/dev/null
      como_usuario "$U" chmod 755 "$ESCRITORIO/factum.desktop"
      como_usuario "$U" gio set "$ESCRITORIO/factum.desktop" metadata::trusted true >/dev/null 2>&1 || true
      ok "Acceso \"Factum\" en el Escritorio de $U y en el menú de aplicaciones."
    else
      ok "Acceso \"Factum\" en el menú de aplicaciones de $U."
    fi
    APPIMAGE="$(find "$PAQ/tatana" -maxdepth 1 -name '*.AppImage' -type f 2>/dev/null | head -1 || true)"
    if [[ -n "$APPIMAGE" ]]; then
      como_usuario "$U" mkdir -p "$HU/Applications"
      como_usuario "$U" cp "$APPIMAGE" "$HU/Applications/Tatana.AppImage.nuevo"
      como_usuario "$U" chmod 755 "$HU/Applications/Tatana.AppImage.nuevo"
      como_usuario "$U" mv -f "$HU/Applications/Tatana.AppImage.nuevo" "$HU/Applications/Tatana.AppImage"
      ok "Tatana copiado a $HU/Applications/Tatana.AppImage (abrilo y tocá \"Preparar esta PC\")."
    else
      aviso "El paquete no trae Tatana (tatana/*.AppImage): instalalo a mano (guía, plan B)."
    fi
    info "Tatana en esta PC no necesita configuración: ya acepta http://localhost:3000."
  else
    aviso "No es Linux o no encontré el home de $U: sin acceso directo ni Tatana (modo prueba)."
  fi
fi

# ══ 10. Resumen ═════════════════════════════════════════════════════════════════════════════
paso 10 $TOTAL "Listo"
echo ""
echo "  ${_V}Factum $VERSION quedó instalado en $CARPETA.${_0}"
if [[ "$PLAN" == servidor ]]; then
  echo "  - En esta PC: https://$FACTUM_NOMBRE_RED (después de preparar-pc-perito.sh, o aceptando el certificado)."
  echo "  - IP de esta PC en la red (anotala para cada perito):"
  IPS="$(ips_de_esta_pc || true)"
  if [[ -n "$IPS" ]]; then printf '%s\n' "$IPS" | sed 's/^/        /'; else echo "        (no la pude averiguar: ip -4 addr)"; fi
  echo "  - Certificado para los peritos: $PAQ/$FACTUM_CA_ARCHIVO"
  echo ""
  echo "  Próximos pasos (guía instalacion-fiscalia.md):"
  echo "   1. Pedir a quien maneja la red que la IP de esta PC sea fija (o reservada en el router)."
  echo "   2. En cada PC de perito, con el paquete (y el .crt) en el pendrive:"
  echo "        sudo bash perito/preparar-pc-perito.sh <IP de esta PC>"
  echo "   3. Entrar con el superadmin, cambiar la contraseña y crear las cuentas de los peritos."
else
  echo "  - Abrilo con el acceso \"Factum\" o en http://localhost:3000"
fi
echo "  - Backup:      sudo $CARPETA/scripts/backup.sh --destino /media/<usuario>/<pendrive>"
echo "  - Diagnóstico: sudo $CARPETA/scripts/diagnostico.sh"
if [[ -n "$SA_TEMPORAL" ]]; then
  echo ""
  echo "  ${_N}PRIMER INGRESO — anotalo ahora, no se vuelve a mostrar:${_0}"
  echo "      DNI:                  $SA_DNI"
  echo "      Contraseña temporal:  $SA_TEMPORAL"
  echo "  La web pide cambiarla en el primer ingreso."
fi
echo ""
