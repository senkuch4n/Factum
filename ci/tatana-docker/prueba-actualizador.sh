#!/usr/bin/env bash
# Nivel 2 de la prueba del actualizador de Tatana en Docker (SDD fiscalia-operacion §14.2): Docker y systemd REALES del
# runner ubuntu-24.04 de CI (VM efímera, root). Canal real (Caddy con el Caddyfile de la fiscalía y tls internal en el
# 443), base 0.0.1 instalada con el instalador nuevo como usuario no root (--mock), y los casos del actualizador vía
# `systemctl start factum-tatana-actualizador.service`.
#
#   sudo bash ci/tatana-docker/prueba-actualizador.sh --paquete Tatana-Docker-vX.Y.Z.tar.gz --canal <dir firmado> \
#        --version X.Y.Z --usuario <usuario no root con sudo sin contraseña>
#
# <dir firmado> = tatana-update-docker.json + imagen + kit de X (en release, firmado con la clave real; en ensayo, con
# la efímera del job). La prueba suma su PROPIA clave efímera a la base para firmar las variantes (producto de otro SO,
# X+1 que no arranca, etc.). SALIDA_LOGS: carpeta de registros.
# shellcheck disable=SC2034,SC2024 # variables que usan los chequeos (eval); los registros son del root de CI
set -euo pipefail
# shellcheck source=ci/tatana-docker/_prueba-lib.sh
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/_prueba-lib.sh"

PAQUETE="" CANAL_X="" X="" USUARIO=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --paquete) PAQUETE="$2"; shift 2 ;;
    --canal) CANAL_X="$2"; shift 2 ;;
    --version) X="$2"; shift 2 ;;
    --usuario) USUARIO="$2"; shift 2 ;;
    *) echo "argumento desconocido: $1" >&2; exit 2 ;;
  esac
done
[[ -f "$PAQUETE" && -f "$CANAL_X/tatana-update-docker.json" && "$X" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ && -n "$USUARIO" ]] ||
  { echo "uso: $0 --paquete <tar.gz> --canal <dir> --version X.Y.Z --usuario <usuario>" >&2; exit 2; }
[[ "$USUARIO" != root ]] || { echo "--usuario tiene que ser un usuario NO root" >&2; exit 2; }
prueba_guardas
HU="$(getent passwd "$USUARIO" | cut -d: -f6)"
IFS=. read -r MA MI PA <<<"$X"
X1="$MA.$MI.$((PA + 1))"   # X re-etiquetada: no arranca (su /health dice X)
X2="$MA.$MI.$((PA + 2))"   # para "sin espacio" (no se llega a bajar)
X3="$MA.$MI.$((PA + 3))"   # para el corte a mitad del cambio
SERVICIO=factum-tatana-actualizador.service
ACT=/opt/factum-tatana-docker/actual/actualizador-tatana-docker.sh
DATOS="$HU/.local/share/tatana-docker"
EVID="$HU/Factum/Evidencia"

limpiar() {
  set +e
  systemctl unset-environment FACTUM_TATANA_ESTADO_FIXTURE FACTUM_TATANA_MARGEN_DISCO_MB 2>/dev/null
  docker logs tatana-docker >"$LOGS/tatana-docker.log" 2>&1
  cp -f /var/log/factum-tatana-docker/actualizador.log* "$LOGS/" 2>/dev/null
  cp -f /var/lib/factum-tatana-docker/estado.env "$LOGS/" 2>/dev/null
  cp -f "$W"/caddy-logs/tatana.log "$LOGS/caddy-tatana.log" 2>/dev/null
  journalctl -u "$SERVICIO" --no-pager >"$LOGS/journal-actualizador.log" 2>/dev/null
  chmod -R a+rX "$LOGS" 2>/dev/null # el artifact lo sube el usuario del runner, no root
  servidor_limpiar
}
trap limpiar EXIT
correr() { # una consulta del timer, como la haría systemd
  systemctl start "$SERVICIO" || true
  echo "  → RESULTADO=$(estado RESULTADO) DETALLE=$(estado DETALLE)"
}

paso "Servidor de la fiscalía (Caddy + CA + factum.internal)"
servidor_arrancar "$USUARIO"
clave_prueba
ok "canal en https://factum.internal/tatana/docker/"

paso "Base 0.0.1 (imagen X re-etiquetada) instalada con instalar-tatana-docker.sh --mock como $USUARIO"
mkdir -p "$W/x"
tar -xzf "$PAQUETE" -C "$W/x"
KIT_X="$W/x/Tatana-Docker-v$X"
docker load -i "$KIT_X/imagen-tatana-docker-v$X.tar.gz" >/dev/null
imagen_reetiquetada "factum-tatana-docker:$X" 0.0.1
imagen_reetiquetada "factum-tatana-docker:$X" "$X1"
imagen_reetiquetada "factum-tatana-docker:$X" "$X3"
BASE="$(carpeta_version "$KIT_X" 0.0.1 "$W/base")"
D1="$(carpeta_version "$KIT_X" "$X1" "$W/v1")"
canal_de_carpeta "$D1" "$X1" "$W/canal-x1"
D3="$(carpeta_version "$KIT_X" "$X3" "$W/v3")"
canal_de_carpeta "$D3" "$X3" "$W/canal-x3"
# Las imágenes de X, X+1 y X+3 tienen que llegar por el canal: se sacan de Docker (queda solo la base).
docker image rm "factum-tatana-docker:$X" "factum-tatana-docker:$X1" "factum-tatana-docker:$X3" >/dev/null
docker image rm factum-tatana-docker:0.0.1 >/dev/null # el instalador la carga del paquete de la base
chown -R "$USUARIO" "$W/base"
sudo -u "$USUARIO" -H bash -c "cd '$BASE' && ./instalar-tatana-docker.sh --mock --origen https://factum.internal --si" \
  >"$LOGS/instalar-base.log" 2>&1 || { tail -40 "$LOGS/instalar-base.log"; echo "ERROR: no se pudo instalar la base"; exit 1; }
chequear "base 0.0.1 instalada y respondiendo" '[[ -n "$(version_health)" ]]'
chequear "timer del actualizador activo" 'systemctl is-active --quiet factum-tatana-actualizador.timer'
chequear "instalacion.env de root con UID/DATOS" 'grep -q "^UID=" /etc/factum-tatana-docker/instalacion.env && grep -q "^DATOS=$DATOS$" /etc/factum-tatana-docker/instalacion.env'
chequear "actualizador habilitado con la CA" 'grep -q "^HABILITADO=1" /etc/factum-tatana-docker/actualizador.env'

# Archivos sembrados por la prueba en lo que el actualizador NUNCA puede tocar.
sudo -u "$USUARIO" bash -c "echo agente >'$DATOS/agent-data/sembrado.txt'; echo home >'$DATOS/home/sembrado.txt'; head -c 50000 /dev/urandom >'$EVID/sembrado.zip'"
echo pareo >"$DATOS/lockdown/sembrado.plist"
hash_sembrados() { sha256sum "$DATOS/agent-data/sembrado.txt" "$DATOS/home/sembrado.txt" "$DATOS/lockdown/sembrado.plist" "$EVID/sembrado.zip" "$DATOS/appsettings.Local.json" | cut -d' ' -f1 | tr '\n' ' '; }
HASH0="$(hash_sembrados)"
FORMA0="$(forma_contenedor)"

paso "Rechazos (nada se carga en Docker)"
IMGS0="$(imagenes_tatana)"
rechazo() { # rechazo <descripción> <carpeta canal> <resultado esperado>
  local esperado_r="$3"
  # Cada caso desde cero: una descarga verificada de un caso anterior no tiene que tapar la alteración de este.
  rm -f /var/lib/factum-tatana-docker/descargas/*
  publicar "$2"
  correr
  chequear "$1 → $3" '[[ "$(estado RESULTADO)" == "$esperado_r" ]]'
  chequear "$1: nada cargado en Docker" '[[ "$(imagenes_tatana)" == "$IMGS0" ]]'
}
mkdir -p "$W/r"
cp -r "$CANAL_X" "$W/r/firma"
node -e 'const f=process.argv[1],fs=require("fs");const e=JSON.parse(fs.readFileSync(f,"utf8"));const s=Buffer.from(e.signature,"base64");s[3]^=1;fs.writeFileSync(f,JSON.stringify({...e,signature:s.toString("base64")}))' "$W/r/firma/tatana-update-docker.json"
rechazo "firma inválida" "$W/r/firma" firma_invalida
cp -r "$CANAL_X" "$W/r/clave"
node -e 'const f=process.argv[1],fs=require("fs");const e=JSON.parse(fs.readFileSync(f,"utf8"));fs.writeFileSync(f,JSON.stringify({...e,key_id:"no-es-de-confianza"}))' "$W/r/clave/tatana-update-docker.json"
rechazo "key_id desconocido" "$W/r/clave" clave_desconocida
cp -r "$CANAL_X" "$W/r/linux"
node -e 'const fs=require("fs");const e=JSON.parse(fs.readFileSync(process.argv[1],"utf8"));fs.writeFileSync(process.argv[2],Buffer.from(e.payload,"base64"))' "$CANAL_X/tatana-update-docker.json" "$W/r/payload-x.json"
firmar_crudo "$W/r/payload-x.json" "$W/r/linux/tatana-update-docker.json" product=tatana-linux
rechazo "producto tatana-linux (firma válida)" "$W/r/linux" manifiesto_invalido
cp -r "$CANAL_X" "$W/r/imagen"
printf '\x00' | dd of="$W/r/imagen/imagen-tatana-docker-v$X.tar.gz" bs=1 seek=1000 conv=notrunc status=none
rechazo "imagen con un byte cambiado" "$W/r/imagen" hash_invalido
cp -r "$CANAL_X" "$W/r/kit"
printf '\x00' | dd of="$W/r/kit/kit-tatana-docker-v$X.tar.gz" bs=1 seek=100 conv=notrunc status=none
rechazo "kit alterado" "$W/r/kit" hash_invalido
chequear "la base sigue respondiendo" '[[ -n "$(version_health)" ]]'

paso "Ocupado (FIXTURE solo de prueba) → esperando_libre; libre → actualizada a $X"
rm -f /var/lib/factum-tatana-docker/descargas/*
printf '{"version":"0.0.1","busy":true,"maintenance":false,"operations":[{"kind":"android_extraction","since":"x"}],"background_operations":[],"recordings":[],"unclaimed_recordings":[]}\n' >"$W/ocupado.json"
ID0="$(id_contenedor)"
publicar "$CANAL_X"
systemctl set-environment FACTUM_TATANA_ESTADO_FIXTURE="$W/ocupado.json"
correr
systemctl unset-environment FACTUM_TATANA_ESTADO_FIXTURE
chequear "ocupado → esperando_libre" '[[ "$(estado RESULTADO)" == esperando_libre ]]'
chequear "ocupado: el contenedor no cambió" '[[ "$(id_contenedor)" == "$ID0" ]]'
chequear "ocupado: la descarga quedó guardada" '[[ -f "/var/lib/factum-tatana-docker/descargas/imagen-tatana-docker-v$X.tar.gz" ]]'
correr
chequear "libre → actualizada" '[[ "$(estado RESULTADO)" == actualizada ]]'
chequear "/health = $X" '[[ "$(version_health)" == "$X" ]]'
chequear "estado: instalada $X, anterior 0.0.1" '[[ "$(estado VERSION_INSTALADA)" == "$X" && "$(estado VERSION_ANTERIOR)" == 0.0.1 ]]'
chequear "actual → kits/$X" '[[ "$(readlink /opt/factum-tatana-docker/actual)" == "kits/$X" ]]'
chequear "datos, home, pareos, evidencia y appsettings.Local.json intactos" '[[ "$(hash_sembrados)" == "$HASH0" ]]'
chequear "mismos montajes, uid/gid, puerto, red y argumentos" '[[ "$(forma_contenedor)" == "$FORMA0" ]]'
chequear "no quedó tatana-docker-anterior" '! docker container inspect tatana-docker-anterior >/dev/null 2>&1'
chequear "descargas limpias" '[[ -z "$(ls -A /var/lib/factum-tatana-docker/descargas)" ]]'
# Para las variantes que siguen: el kit instalado también confía en la clave de la prueba (solo en esta VM).
linea_clave_prueba >>"/opt/factum-tatana-docker/kits/$X/claves-confiables.txt"

paso "Versión igual o menor → al_dia"
correr
chequear "igual → al_dia" '[[ "$(estado RESULTADO)" == al_dia ]]'
mkdir -p "$W/menor"
cp "$CANAL_X"/* "$W/menor/"
node -e 'const fs=require("fs");const e=JSON.parse(fs.readFileSync(process.argv[1],"utf8"));fs.writeFileSync(process.argv[2],Buffer.from(e.payload,"base64"))' "$CANAL_X/tatana-update-docker.json" "$W/menor/p.json"
node -e '
  const fs=require("fs");const [f,v]=process.argv.slice(1);const p=JSON.parse(fs.readFileSync(f,"utf8"))
  p.version=v;p.file=`imagen-tatana-docker-v${v}.tar.gz`;p.image=`factum-tatana-docker:${v}`;p.kit={...p.kit,file:`kit-tatana-docker-v${v}.tar.gz`}
  fs.writeFileSync(f,JSON.stringify(p,null,2)+"\n")' "$W/menor/p.json" 0.0.9
firmar "$W/menor/p.json" "$W/menor/tatana-update-docker.json"
rm -f "$W/menor/p.json"
publicar "$W/menor"
correr
chequear "menor (0.0.9) → al_dia (nunca baja)" '[[ "$(estado RESULTADO)" == al_dia ]]'

paso "$X1 (X re-etiquetada: no arranca) → vuelve a $X en < 3 min y no la reintenta"
publicar "$W/canal-x1"
T0=$SECONDS
correr
DUR=$((SECONDS - T0))
chequear "no_arranca" '[[ "$(estado RESULTADO)" == no_arranca ]]'
chequear "volvió a $X (/health)" '[[ "$(version_health)" == "$X" ]]'
chequear "el cambio y la vuelta tardaron < 3 min sin contar la descarga ($DUR s en total)" '(( DUR < 600 ))'
chequear "VERSIONES_FALLIDAS tiene $X1" '[[ ",$(estado VERSIONES_FALLIDAS)," == *",$X1,"* ]]'
chequear "mismos montajes después de la vuelta" '[[ "$(forma_contenedor)" == "$FORMA0" ]]'
correr
chequear "la consulta siguiente → version_fallida" '[[ "$(estado RESULTADO)" == version_fallida ]]'
chequear "datos intactos" '[[ "$(hash_sembrados)" == "$HASH0" ]]'

paso "--volver-a-anterior (a mano) → 0.0.1"
publicar "$CANAL_X"
"$ACT" --volver-a-anterior >"$LOGS/volver.log" 2>&1 || { cat "$LOGS/volver.log"; mal "--volver-a-anterior falló"; }
chequear "vuelta_manual" '[[ "$(estado RESULTADO)" == vuelta_manual ]]'
chequear "instalada 0.0.1" '[[ "$(estado VERSION_INSTALADA)" == 0.0.1 && "$(docker inspect -f "{{.Config.Image}}" tatana-docker)" == factum-tatana-docker:0.0.1 ]]'
chequear "$X quedó marcada como fallida" '[[ ",$(estado VERSIONES_FALLIDAS)," == *",$X,"* ]]'
correr
chequear "con $X publicada → version_fallida (no se reinstala sola)" '[[ "$(estado RESULTADO)" == version_fallida ]]'

paso "Sin espacio (margen enorme, solo prueba) → nada descargado"
mkdir -p "$W/x2"
cp "$W/menor"/* "$W/x2/"
node -e 'const fs=require("fs");const e=JSON.parse(fs.readFileSync(process.argv[1],"utf8"));fs.writeFileSync(process.argv[2],Buffer.from(e.payload,"base64"))' "$CANAL_X/tatana-update-docker.json" "$W/x2/p.json"
node -e '
  const fs=require("fs");const [f,v]=process.argv.slice(1);const p=JSON.parse(fs.readFileSync(f,"utf8"))
  p.version=v;p.file=`imagen-tatana-docker-v${v}.tar.gz`;p.image=`factum-tatana-docker:${v}`;p.kit={...p.kit,file:`kit-tatana-docker-v${v}.tar.gz`}
  fs.writeFileSync(f,JSON.stringify(p,null,2)+"\n")' "$W/x2/p.json" "$X2"
firmar "$W/x2/p.json" "$W/x2/tatana-update-docker.json"
rm -f "$W/x2/p.json"
publicar "$W/x2"
systemctl set-environment FACTUM_TATANA_MARGEN_DISCO_MB=100000000
correr
systemctl unset-environment FACTUM_TATANA_MARGEN_DISCO_MB
chequear "sin_espacio" '[[ "$(estado RESULTADO)" == sin_espacio ]]'
chequear "nada descargado" '[[ -z "$(ls -A /var/lib/factum-tatana-docker/descargas | grep -v tatana-update-docker.json)" ]]'

paso "Servidor caído → sin_servidor; Tatana sigue respondiendo"
docker stop "$CONTENEDOR_CADDY" >/dev/null
correr
chequear "sin_servidor" '[[ "$(estado RESULTADO)" == sin_servidor ]]'
chequear "Tatana responde" '[[ -n "$(version_health)" ]]'
docker start "$CONTENEDOR_CADDY" >/dev/null
sleep 3

paso "Corte a mitad del cambio (entre rename y run) → la siguiente corrida da recuperado"
publicar "$W/canal-x3"
set +e
FACTUM_TATANA_PRUEBA_CORTE=despues_rename "$ACT" --una-vez >"$LOGS/corte.log" 2>&1
set -e
chequear "quedó tatana-docker-anterior y no tatana-docker (corte simulado)" 'docker container inspect tatana-docker-anterior >/dev/null 2>&1 && ! docker container inspect tatana-docker >/dev/null 2>&1'
correr
chequear "recuperado" '[[ "$(estado RESULTADO)" == recuperado ]]'
chequear "tatana-docker responde de nuevo" 'sleep 5; [[ -n "$(version_health)" ]]'
chequear "datos intactos" '[[ "$(hash_sembrados)" == "$HASH0" ]]'

paso "estado-peritos.sh con el registro de la prueba"
FACTUM_TATANA_PUBLICADA="$X3" bash "$RAIZ/deploy/fiscalia/comun/estado-peritos.sh" --registro "$W/caddy-logs" >"$LOGS/estado-peritos.log" 2>&1 || true
cat "$LOGS/estado-peritos.log"
chequear "lista este equipo" 'grep -q "^$(hostname | tr -c "A-Za-z0-9._\n-" _ | cut -c1-16)" "$LOGS/estado-peritos.log"'
sudo -u "$USUARIO" bash "$BASE/estado-tatana-docker.sh" --actualizaciones >"$LOGS/estado-tatana-docker.log" 2>&1 || true
chequear "estado-tatana-docker.sh --actualizaciones muestra el resultado" 'grep -q "Última consulta" "$LOGS/estado-tatana-docker.log"'

paso "Desinstalar"
sudo -u "$USUARIO" -H bash -c "cd '$BASE' && ./desinstalar-tatana-docker.sh --si" >"$LOGS/desinstalar.log" 2>&1 || { tail -20 "$LOGS/desinstalar.log"; mal "desinstalar falló"; }
chequear "sin units" '[[ ! -e /etc/systemd/system/factum-tatana-actualizador.timer && ! -e /etc/systemd/system/factum-tatana-actualizador.service ]]'
chequear "sin /opt|/etc|/var/lib|/var/log propios" '[[ ! -e /opt/factum-tatana-docker && ! -e /etc/factum-tatana-docker && ! -e /var/lib/factum-tatana-docker && ! -e /var/log/factum-tatana-docker ]]'
chequear "sin contenedores" '! docker container inspect tatana-docker >/dev/null 2>&1 && ! docker container inspect tatana-docker-anterior >/dev/null 2>&1'
chequear "evidencia y datos intactos" '[[ "$(hash_sembrados)" == "$HASH0" ]]'

echo
if ((fallos)); then echo "$fallos chequeo(s) fallaron (registros en $LOGS)"; exit 1; fi
echo "Actualizador de Tatana en Docker (nivel 2): todos los casos pasaron"
