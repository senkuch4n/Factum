#!/usr/bin/env bash
# Prueba de actualizar.sh en un servidor plan A de verdad (SDD fiscalia-operacion §14.3). Corre como ROOT en una VM
# efímera (runner ubuntu-24.04 de CI); NUNCA en la Mac ni en una PC con datos: instala Factum en
# /srv/factum-fiscalia-prueba con un proyecto de compose propio (factum-fiscalia-prueba) en los puertos 18080/18443 y
# al final lo desinstala con --borrar-datos (solo esa carpeta).
#
#   sudo bash ci/fiscalia/prueba-actualizar.sh [--imagenes-de <tag>]
#
# --imagenes-de <tag>: no construye backend ni frontend-servidor; re-etiqueta factum-backend:<tag> y
#   factum-frontend-servidor:<tag> como 0.0.1 (para correrla en una máquina que ya las tiene).
# SALIDA_LOGS (opcional): carpeta donde quedan los registros de cada caso.
#
# Casos: actualizar a 0.0.2 (backup previo, datos y evidencia intactos, CA igual, canal de Tatana publicado y
# registrado) · 0.0.3 rota (vuelve sola a 0.0.2; canal sin cambios; mensaje con restaurar.sh) · paquete dañado (corta en
# [1/8] sin backup ni reinicios) · versión menor (corta; con --permitir-version-menor --si sigue) · --imagenes-de ·
# solo Tatana (publica sin backup ni reinicio) · candado (backup.sh mientras hay otra operación → se niega).
# shellcheck disable=SC2034 # variables que usan los chequeos (eval)
set -euo pipefail
RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
[[ "$(id -u)" == 0 ]] || { echo "Correr con sudo (en una VM efímera de CI)." >&2; exit 1; }
[[ "$(uname -s)" == Linux ]] || { echo "Solo Linux (CI)." >&2; exit 1; }
# actualizar.sh borra las imágenes factum-backend/frontend que no sean la versión nueva ni la anterior: en una máquina
# de trabajo se llevaría tags ajenos. Solo en la VM efímera de CI (o con FACTUM_PRUEBA_VM_DESCARTABLE=1 a sabiendas).
[[ "${GITHUB_ACTIONS:-}" == true || "${FACTUM_PRUEBA_VM_DESCARTABLE:-0}" == 1 ]] ||
  { echo "Esta prueba solo corre en CI (borra imágenes factum-* que no use). FACTUM_PRUEBA_VM_DESCARTABLE=1 para forzarla en una VM descartable." >&2; exit 1; }
# armar-paquete-fiscalia.sh usa git sobre el checkout (de otro usuario: root necesita safe.directory).
git config --global --add safe.directory "$RAIZ" 2>/dev/null || true
IMAGENES_DE=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --imagenes-de) IMAGENES_DE="$2"; shift 2 ;;
    *) echo "uso: $0 [--imagenes-de <tag>]" >&2; exit 2 ;;
  esac
done

CARPETA="${FACTUM_PRUEBA_CARPETA:-/srv/factum-fiscalia-prueba}"
export FACTUM_PROYECTO_PRUEBA=factum-fiscalia-prueba
export FACTUM_PUERTO_HTTP=18080 FACTUM_PUERTO_HTTPS=18443
export FACTUM_ESPERA_SANO=240
LOGS="${SALIDA_LOGS:-$(mktemp -d)}"
mkdir -p "$LOGS"
W="$(mktemp -d /tmp/prueba-actualizar.XXXXXX)"
F="$RAIZ/deploy/fiscalia"
fallos=0
ok() { echo "ok    $*"; }
mal() { echo "FALLA $*"; fallos=$((fallos + 1)); }
chequear() { if eval "$2"; then ok "$1"; else mal "$1 [$2]"; fi; }

limpiar() {
  set +e
  if [[ -f "$CARPETA/config/.env" ]]; then
    FACTUM_SI=1 bash "$F/comun/instalar.sh" --plan servidor --carpeta "$CARPETA" --desinstalar --borrar-datos --sin-hosts --si >/dev/null 2>&1
  fi
  [[ -n "${PID_CANDADO:-}" ]] && kill "$PID_CANDADO" 2>/dev/null
  rm -rf "$W"
}
trap limpiar EXIT
[[ ! -e "$CARPETA" ]] || { echo "ERROR: $CARPETA ya existe: esta prueba no pisa nada." >&2; exit 1; }

echo "== Imágenes 0.0.1 =="
if [[ -n "$IMAGENES_DE" ]]; then
  docker tag "factum-backend:$IMAGENES_DE" factum-backend:0.0.1
  docker tag "factum-frontend-servidor:$IMAGENES_DE" factum-frontend-servidor:0.0.1
else
  docker build -q -t factum-backend:0.0.1 "$RAIZ/server/src/Factum.Backend" >/dev/null
  docker build -q -t factum-frontend-servidor:0.0.1 --build-arg NEXT_PUBLIC_BACKEND_URL=https://factum.internal \
    --build-arg NEXT_PUBLIC_AGENT_URL=http://localhost:8765 "$RAIZ/client" >/dev/null
fi
MONGO_IMG="$(sed -nE 's/^[[:space:]]*image:[[:space:]]*(mongo:[^[:space:]#]+).*/\1/p' "$F/comun/docker-compose.yml" | head -1)"
CADDY_IMG="$(sed -nE 's/^[[:space:]]*image:[[:space:]]*(caddy:[^[:space:]#]+).*/\1/p' "$F/servidor/compose.servidor.yml" | head -1)"
docker pull -q "$MONGO_IMG" >/dev/null
docker pull -q "$CADDY_IMG" >/dev/null
# 0.0.3 "rota": el backend nunca responde /health (el healthcheck falla) → la actualización tiene que volver sola.
printf 'FROM factum-backend:0.0.1\nENTRYPOINT ["sh", "-c", "exec sleep infinity"]\n' | docker build -q -t factum-backend:0.0.3-rota - >/dev/null
ok "imágenes listas"

echo "== Paquete de instalación 0.0.1 e instalación (plan A) =="
P0="$W/Factum-Fiscalia-v0.0.1"
mkdir -p "$P0"/{comun/mongo-init,comun/mongo-init-sin-avx,servidor,imagenes}
cp "$F/comun/docker-compose.yml" "$F/comun/compose.superadmin.yml" "$F/comun/compose.sin-avx.yml" "$F"/comun/*.sh "$P0/comun/"
cp "$F/comun/mongo-init-sin-avx/01-usuario-app.js" "$P0/comun/mongo-init-sin-avx/"
cp "$RAIZ/deploy/cloud/mongo-init/01-usuario-app.js" "$P0/comun/mongo-init/"
cp "$RAIZ/deploy/windows/appsettings.Local.example.json" "$P0/comun/"
cp "$F/servidor/instalar-servidor.sh" "$F/servidor/compose.servidor.yml" "$F/servidor/Caddyfile" "$P0/servidor/"
echo 0.0.1 >"$P0/version.txt"
docker save -o "$P0/imagenes/backend.tar" factum-backend:0.0.1
docker save -o "$P0/imagenes/frontend-servidor.tar" factum-frontend-servidor:0.0.1
docker save -o "$P0/imagenes/mongo.tar" "$MONGO_IMG"
docker save -o "$P0/imagenes/caddy.tar" "$CADDY_IMG"
(cd "$P0" && find . -type f ! -name SHA256SUMS | sed 's|^\./||' | sort | while IFS= read -r f; do printf '%s  %s\n' "$(sha256sum "$f" | cut -d' ' -f1)" "$f"; done >SHA256SUMS)
bash "$P0/servidor/instalar-servidor.sh" --carpeta "$CARPETA" --superadmin-dni 12345678 --superadmin-nombre "Prueba CI" \
  --si --sin-hosts >"$LOGS/instalar.log" 2>&1 || { tail -50 "$LOGS/instalar.log"; echo "ERROR: no se pudo instalar la base"; exit 1; }
ENVF="$CARPETA/config/.env"
DATOS="$CARPETA/datos"
ok "Factum 0.0.1 instalado en $CARPETA"
fc_() { docker compose --project-directory "$CARPETA" -p "$FACTUM_PROYECTO_PRUEBA" --env-file "$ENVF" -f "$CARPETA/docker-compose.yml" -f "$CARPETA/compose.servidor.yml" "$@"; }

# Datos sembrados por la prueba (en SU base y SU carpeta): un documento y un archivo de evidencia.
PW="$(grep '^FACTUM_MONGO_ROOT_PASSWORD=' "$ENVF" | cut -d= -f2)"
fc_ exec -T mongo mongosh --quiet -u root -p "$PW" --authenticationDatabase admin factum \
  --eval 'db.prueba_ci.insertOne({ _id: "caso-ci", nombre: "Caso de la prueba de actualizar.sh" })' >/dev/null
mkdir -p "$DATOS/backend/prueba-ci"
head -c 300000 /dev/urandom >"$DATOS/backend/prueba-ci/evidencia.bin"
HASH_EVID="$(sha256sum "$DATOS/backend/prueba-ci/evidencia.bin" | cut -d' ' -f1)"
CA_ROOT="$DATOS/caddy-data/caddy/pki/authorities/local/root.crt"
HUELLA_CA="$(sha256sum "$CA_ROOT" | cut -d' ' -f1)"
caso_existe() {
  fc_ exec -T mongo mongosh --quiet -u root -p "$PW" --authenticationDatabase admin factum \
    --eval 'print(db.prueba_ci.countDocuments({ _id: "caso-ci" }))' 2>/dev/null | tr -d '[:space:]'
}
n_backups() { find "$CARPETA/backups" -mindepth 1 -maxdepth 1 -type d -name 'factum-servidor-*' | wc -l | tr -d ' '; }
inicio_backend() { docker inspect -f '{{.State.StartedAt}}' "$(fc_ ps -q backend)"; }
version_inst() { grep '^FACTUM_VERSION=' "$ENVF" | cut -d= -f2; }
CURL=(curl -fsS -m 10 --resolve "factum.internal:18443:127.0.0.1" --cacert "$CARPETA/ca-factum-fiscalia.crt")

# ── Canal falso de Tatana en Docker: firmado con una clave efímera (el servidor no verifica la firma; armar sí) ──
node "$RAIZ/ops/tatana/generar-clave-firma.mjs" --key-id prueba-fiscalia --salida "$W/clave.pem" --extra-salida "$W/extra.txt" >/dev/null
EXTRA="$(tr -d '[:space:]' <"$W/extra.txt")"
canal_tatana() { # canal_tatana <versión> → carpeta con manifiesto firmado, imagen y kit falsos
  local v="$1" d="$W/canal-$1"
  mkdir -p "$d/kit/Tatana-Docker-v$v"
  head -c 50000 /dev/urandom >"$d/imagen-tatana-docker-v$v.tar.gz"
  printf '#!/usr/bin/env bash\necho sumar %s\n' "$v" >"$d/kit/Tatana-Docker-v$v/sumar-actualizador.sh"
  tar -C "$d/kit" -czf "$d/kit-tatana-docker-v$v.tar.gz" "Tatana-Docker-v$v"
  rm -rf "$d/kit"
  node "$RAIZ/ops/tatana/armar-payload.mjs" --producto tatana-docker --archivo "$d/imagen-tatana-docker-v$v.tar.gz" \
    --kit "$d/kit-tatana-docker-v$v.tar.gz" --imagen "factum-tatana-docker:$v" --version "$v" \
    --commit "$(printf 'b%.0s' {1..40})" --salida "$W/payload-$v.json" >/dev/null
  TATANA_FIRMA_CLAVE_PRIVADA="$(cat "$W/clave.pem")" node "$RAIZ/ops/tatana/firmar-manifiesto.mjs" --payload "$W/payload-$v.json" \
    --key-id prueba-fiscalia --clave-extra "$EXTRA" --salida "$d/tatana-update-docker.json" >/dev/null
  echo "$v" >"$d/VERSION"
  echo "$d"
}

# Paquete de actualización con el script REAL (armar-paquete-fiscalia.sh --actualizacion, sin construir: --imagenes-de)
# y, si hace falta, imágenes nuevas adentro (re-etiquetadas: armar las construiría, acá se ahorra el build).
armar_act() { # armar_act <versión> <imagenes-de|-> <canal tatana|-> [backend:tag frontend:tag]
  local v="$1" de="$2" canal="$3" args=() p base="$2"
  [[ "$de" != - ]] || base=0.0.1 # armar no construye nada; las imágenes nuevas se meten abajo
  args=(--actualizacion --version "$v" --deploy-de-trabajo --salida "$W/paq" --imagenes-de "$base")
  [[ "$canal" == - ]] || args+=(--tatana-docker-canal "$canal" --clave-extra "$EXTRA")
  bash "$F/armar-paquete-fiscalia.sh" "${args[@]}" >"$LOGS/armar-$v.log" 2>&1 || { tail -30 "$LOGS/armar-$v.log"; return 1; }
  p="$W/paq/Factum-Fiscalia-Actualizacion-v$v"
  if [[ "$de" == - ]]; then
    rm -f "$p/imagenes-de.txt"
    mkdir -p "$p/imagenes"
    docker tag "$4" "factum-backend:$v"
    docker tag "$5" "factum-frontend-servidor:$v"
    docker save "factum-backend:$v" | gzip -1 >"$p/imagenes/backend.tar.gz"
    docker save "factum-frontend-servidor:$v" | gzip -1 >"$p/imagenes/frontend-servidor.tar.gz"
    docker image rm "factum-backend:$v" "factum-frontend-servidor:$v" >/dev/null # tienen que venir del paquete
    (cd "$p" && find . -type f ! -name SHA256SUMS | sed 's|^\./||' | sort | while IFS= read -r f; do printf '%s  %s\n' "$(sha256sum "$f" | cut -d' ' -f1)" "$f"; done >SHA256SUMS)
  fi
  echo "$p"
}
actualizar() { # actualizar <paquete> <log> [args...] → código de salida
  local p="$1" log="$2"
  shift 2
  set +e
  bash "$p/actualizar.sh" --carpeta "$CARPETA" --si "$@" >"$LOGS/$log" 2>&1
  local c=$?
  set -e
  return $c
}

# Los paquetes con imágenes propias se arman ANTES de actualizar (la limpieza de actualizar.sh borra los tags viejos).
C100="$(canal_tatana 1.0.0)"
P2="$(armar_act 0.0.2 - "$C100" factum-backend:0.0.1 factum-frontend-servidor:0.0.1)"
C110="$(canal_tatana 1.1.0)"
P3="$(armar_act 0.0.3 - "$C110" factum-backend:0.0.3-rota factum-frontend-servidor:0.0.1)"

echo "== Caso 1: actualizar a 0.0.2 (imágenes en el paquete + Tatana en Docker 1.0.0) =="
B0="$(n_backups)"
if actualizar "$P2" caso1.log; then ok "actualizar.sh a 0.0.2 terminó bien"; else mal "actualizar.sh a 0.0.2 falló"; tail -40 "$LOGS/caso1.log"; fi
chequear "FACTUM_VERSION=0.0.2" '[[ "$(version_inst)" == 0.0.2 ]]'
chequear "hay un backup previo nuevo" '(( $(n_backups) == B0 + 1 ))'
chequear "/health/ready responde" '"${CURL[@]}" https://factum.internal:18443/health/ready >/dev/null'
chequear "el caso sembrado sigue en la base" '[[ "$(caso_existe)" == 1 ]]'
chequear "la evidencia sembrada sigue igual" '[[ "$(sha256sum "$DATOS/backend/prueba-ci/evidencia.bin" | cut -d" " -f1)" == "$HASH_EVID" ]]'
chequear "la CA de Caddy no cambió" '[[ "$(sha256sum "$CA_ROOT" | cut -d" " -f1)" == "$HUELLA_CA" ]]'
chequear "los archivos de 0.0.1 quedaron en actualizacion-anterior/" '[[ -f "$CARPETA/actualizacion-anterior/config.env" && -f "$CARPETA/actualizacion-anterior/Caddyfile" ]]'
chequear "canal: VERSION 1.0.0" '[[ "$(cat "$CARPETA/canal-tatana/docker/VERSION")" == 1.0.0 ]]'
H="$(curl -sS -m 10 -D - -o "$W/man.json" --resolve "factum.internal:18443:127.0.0.1" --cacert "$CARPETA/ca-factum-fiscalia.crt" \
  "https://factum.internal:18443/tatana/docker/tatana-update-docker.json?v=1&equipo=pc-ci-1&instalada=0.0.0-docker-prueba&disponible=&resultado=al_dia" | tr -d '\r')"
chequear "manifiesto por https con 200 y Cache-Control: no-cache" 'grep -q "^HTTP/[0-9.]* 200" <<<"$H" && grep -qi "^cache-control: no-cache" <<<"$H"'
chequear "el manifiesto servido es el del paquete" 'cmp -s "$W/man.json" "$C100/tatana-update-docker.json"'
chequear "sumar-actualizador.sh publicado" '"${CURL[@]}" https://factum.internal:18443/tatana/docker/sumar-actualizador.sh | grep -q "sumar 1.0.0"'
chequear "sin listado de carpetas en el canal" '! "${CURL[@]}" https://factum.internal:18443/tatana/docker/ >/dev/null 2>&1'
sleep 2
bash "$CARPETA/scripts/estado-peritos.sh" --carpeta "$CARPETA" >"$LOGS/estado-peritos.log" 2>&1 || true
chequear "estado-peritos.sh lista la PC con su versión" 'grep -E "pc-ci-1 .*0\.0\.0-docker-prueba" "$LOGS/estado-peritos.log" >/dev/null'
"${CURL[@]}" https://factum.internal:18443/health >/dev/null
chequear "el uso de la web/API no se registra" '! grep -q "\"uri\":\"/health\"" "$DATOS"/caddy-logs/tatana.log'

echo "== Caso 2: 0.0.3 rota → vuelve sola a 0.0.2 =="
if actualizar "$P3" caso2.log; then mal "actualizar.sh a 0.0.3 rota terminó con 0"; else ok "actualizar.sh a 0.0.3 rota terminó con error"; fi
chequear "FACTUM_VERSION volvió a 0.0.2" '[[ "$(version_inst)" == 0.0.2 ]]'
chequear "/health/ready responde (0.0.2)" '"${CURL[@]}" https://factum.internal:18443/health/ready >/dev/null'
chequear "el canal NO cambió (sigue 1.0.0)" '[[ "$(cat "$CARPETA/canal-tatana/docker/VERSION")" == 1.0.0 ]]'
chequear "el mensaje trae el comando de restaurar.sh" 'grep -q "restaurar.sh --desde" "$LOGS/caso2.log"'
chequear "el caso sembrado sigue en la base" '[[ "$(caso_existe)" == 1 ]]'

echo "== Caso 3: paquete dañado → corta en [1/8] =="
P4="$(armar_act 0.0.4 0.0.1 -)"
printf 'x' >>"$P4/comun/docker-compose.yml"
B1="$(n_backups)"
I1="$(inicio_backend)"
if actualizar "$P4" caso3.log; then mal "paquete dañado terminó con 0"; else ok "paquete dañado terminó con error"; fi
chequear "cortó en la verificación del paquete" 'grep -q "dañado" "$LOGS/caso3.log" && ! grep -q "\[2/8\]" "$LOGS/caso3.log"'
chequear "no hubo backup nuevo" '[[ "$(n_backups)" == "$B1" ]]'
chequear "no se reinició el backend" '[[ "$(inicio_backend)" == "$I1" ]]'

echo "== Caso 4: versión menor =="
P1="$(armar_act 0.0.1 0.0.1 -)"
if actualizar "$P1" caso4a.log; then mal "versión menor sin flag terminó con 0"; else ok "versión menor sin flag se niega"; fi
chequear "sigue en 0.0.2" '[[ "$(version_inst)" == 0.0.2 ]]'
if actualizar "$P1" caso4b.log --permitir-version-menor; then ok "con --permitir-version-menor --si sigue"; else mal "con --permitir-version-menor falló"; tail -30 "$LOGS/caso4b.log"; fi
chequear "quedó en 0.0.1" '[[ "$(version_inst)" == 0.0.1 ]]'

echo "== Caso 5: --imagenes-de (solo scripts + canal) =="
P5="$(armar_act 0.0.5 0.0.1 -)"
if actualizar "$P5" caso5.log; then ok "actualizar.sh con imagenes-de terminó bien"; else mal "imagenes-de falló"; tail -30 "$LOGS/caso5.log"; fi
chequear "FACTUM_VERSION=0.0.5" '[[ "$(version_inst)" == 0.0.5 ]]'
chequear "factum-backend:0.0.5 es la misma imagen que 0.0.1" '[[ "$(docker image inspect -f "{{.Id}}" factum-backend:0.0.5)" == "$(docker image inspect -f "{{.Id}}" factum-backend:0.0.1)" ]]'
chequear "/health/ready responde" '"${CURL[@]}" https://factum.internal:18443/health/ready >/dev/null'

echo "== Caso 6: solo Tatana (misma versión de Factum, Tatana mayor) =="
C120="$(canal_tatana 1.2.0)"
P6="$(armar_act 0.0.5 0.0.1 "$C120")"
B2="$(n_backups)"
I2="$(inicio_backend)"
if actualizar "$P6" caso6.log; then ok "solo Tatana terminó bien"; else mal "solo Tatana falló"; tail -30 "$LOGS/caso6.log"; fi
chequear "canal: VERSION 1.2.0" '[[ "$(cat "$CARPETA/canal-tatana/docker/VERSION")" == 1.2.0 ]]'
chequear "sin backup" '[[ "$(n_backups)" == "$B2" ]]'
chequear "sin reinicio" '[[ "$(inicio_backend)" == "$I2" ]]'
chequear "quedan la publicada y la anterior (1.2.0 y 1.0.0)" '[[ -f "$CARPETA/canal-tatana/docker/imagen-tatana-docker-v1.2.0.tar.gz" && -f "$CARPETA/canal-tatana/docker/imagen-tatana-docker-v1.0.0.tar.gz" ]]'
if actualizar "$P6" caso6b.log; then ok "repetir el mismo paquete: nada que hacer"; else mal "repetir el mismo paquete falló"; fi
chequear "dijo que no hay nada que hacer" 'grep -q "nada que hacer" "$LOGS/caso6b.log"'

echo "== Caso 7: candado =="
flock "$CARPETA/.operacion.lock" sleep 60 &
PID_CANDADO=$!
sleep 1
set +e
bash "$CARPETA/scripts/backup.sh" --carpeta "$CARPETA" >"$LOGS/caso7.log" 2>&1
C7=$?
set -e
chequear "backup.sh se niega mientras hay otra operación" '(( C7 != 0 )) && grep -q "otra operación" "$LOGS/caso7.log"'
kill "$PID_CANDADO" 2>/dev/null || true
PID_CANDADO=""

echo
if ((fallos)); then echo "$fallos chequeo(s) fallaron (registros en $LOGS)"; exit 1; fi
echo "actualizar.sh: todos los casos pasaron"
