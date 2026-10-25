#!/usr/bin/env bash
# Suma el actualizador automático a una PC de perito con Tatana en Docker (las instaladas antes de que existiera) o
# instala Tatana en Docker en una PC nueva, bajando todo del servidor de la fiscalía (SDD fiscalia-operacion §9.2).
# Se publica suelto en el canal; un comando por PC, con la sesión del perito (SIN sudo: lo pide una vez):
#
#   wget -qO /tmp/sumar-actualizador.sh https://factum.internal/tatana/docker/sumar-actualizador.sh \
#     && bash /tmp/sumar-actualizador.sh
#
#   bash sumar-actualizador.sh [--canal https://factum.internal/tatana/docker/] [--si]
#
# 1. Chequeos (Linux x86_64, Docker, no root). 2. Baja el manifiesto firmado, el kit y la imagen a
# ~/Tatana-Docker-vX.Y.Z/ (se reanuda). 3. Verifica la firma con el verificador del kit y el Python de la imagen YA
# INSTALADA, y tamaño y SHA-256 de kit e imagen. 4. Corre ./instalar-tatana-docker.sh --si (conserva los orígenes;
# si Tatana está capturando corta sin tocar nada). 5. Primera consulta del actualizador y su estado.
# No toca la evidencia, los datos del agente, los pareos del iPhone, /etc/hosts ni la CA.
set -euo pipefail

CANAL="https://factum.internal/tatana/docker/"
SI=1
CONTENEDOR="${TATANA_DOCKER_CONTENEDOR:-tatana-docker}"
CA_SISTEMA="${TATANA_DOCKER_CA_SISTEMA:-/usr/local/share/ca-certificates/factum-fiscalia.crt}"
PYTHON_IMAGEN=/opt/tatana/tools/python-embed/bin/python3
while [[ $# -gt 0 ]]; do
  case "$1" in
    --canal) CANAL="${2:-}"; shift 2 ;;
    --si) SI=1; shift ;;
    -h|--help) sed -n '2,16p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Opción desconocida: $1" >&2; exit 2 ;;
  esac
done
[[ "$CANAL" == */ ]] || CANAL="$CANAL/"

if [[ -t 1 ]]; then _V=$'\e[32m'; _A=$'\e[33m'; _R=$'\e[31m'; _N=$'\e[1m'; _0=$'\e[0m'; else _V=""; _A=""; _R=""; _N=""; _0=""; fi
ok()    { echo "  ${_V}OK${_0}  $*"; }
aviso() { echo "  ${_A}AVISO${_0}  $*"; }
morir() { echo "  ${_R}ERROR${_0}  $*" >&2; exit 1; }
paso()  { echo; echo "${_N}$*${_0}"; }
sha256_de() { sha256sum "$1" | cut -d' ' -f1; }
tamano_de() { wc -c <"$1" | tr -d ' '; }
clave_de() { local l; while IFS= read -r l; do [[ "$l" == "$2="* ]] && { printf '%s' "${l#*=}"; return 0; }; done <<<"$1"; }

echo "${_N}Tatana en Docker — sumar el actualizador automático (servidor: $CANAL)${_0}"

paso "[1/5] Esta PC"
[[ "$(uname -s)" == Linux && "$(uname -m)" == x86_64 ]] || morir "Esto es para PCs Linux x86-64 (esta es $(uname -s) $(uname -m))."
[[ "$(id -u)" != 0 ]] || morir "Corré este script con el usuario del perito, sin sudo: él mismo pide la contraseña cuando la necesita."
command -v docker >/dev/null 2>&1 || morir "Docker no está instalado (instalar-docker-offline.sh del paquete de la fiscalía)."
if docker info >/dev/null 2>&1; then DOCKER=(docker)
elif command -v sudo >/dev/null 2>&1 && sudo docker info >/dev/null 2>&1; then DOCKER=(sudo docker)
else morir "Docker está instalado pero no responde (probá 'sudo systemctl start docker')."; fi
IMAGEN_INSTALADA="$("${DOCKER[@]}" container inspect -f '{{.Config.Image}}' "$CONTENEDOR" 2>/dev/null || true)"
if [[ -n "$IMAGEN_INSTALADA" ]]; then ok "Tatana en Docker instalado: $IMAGEN_INSTALADA"
else aviso "Esta PC no tiene Tatana en Docker: se va a hacer una instalación NUEVA."; fi
HOST="$(sed -E 's|^https://([^/:]+).*|\1|' <<<"$CANAL")"
getent hosts "$HOST" >/dev/null 2>&1 || morir "Esta PC no resuelve $HOST: corré primero preparar-pc-perito.sh <IP del servidor> (paquete de la fiscalía)."
ok "usuario $(id -un), $HOST resuelve"

# Descarga con wget o curl del host (la CA de la fiscalía ya está en el sistema) o, si no hay, con el curl de la imagen
# instalada (Ubuntu 20.04 de escritorio trae wget pero no curl).
bajar() { # bajar <url> <destino> → con reanudación
  if command -v wget >/dev/null 2>&1; then
    wget -q -c -O "$2" "$1"
  elif command -v curl >/dev/null 2>&1; then
    curl -fsS -C - -o "$2" "$1"
  elif [[ -n "$IMAGEN_INSTALADA" && -f "$CA_SISTEMA" ]]; then
    local d ip
    d="$(cd "$(dirname "$2")" && pwd)"
    ip="$(getent ahostsv4 "$HOST" | awk 'NR == 1 {print $1}')"
    "${DOCKER[@]}" run --rm --platform linux/amd64 --network host --security-opt label=disable --user "$(id -u):$(id -g)" \
      -v "$CA_SISTEMA:/ca.crt:ro" -v "$d:/d" --entrypoint curl "$IMAGEN_INSTALADA" \
      -fsS -C - --cacert /ca.crt --resolve "$HOST:443:$ip" -o "/d/$(basename "$2")" "$1"
  else
    morir "No hay wget ni curl en esta PC (sudo apt install wget)."
  fi
}

paso "[2/5] Descargando del servidor"
TMP="$(mktemp -d "${TMPDIR:-/tmp}/sumar-actualizador.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT
bajar "${CANAL}tatana-update-docker.json" "$TMP/tatana-update-docker.json" ||
  morir "No se pudo bajar ${CANAL}tatana-update-docker.json (¿el servidor ya publicó Tatana en Docker? ¿la CA está instalada?)."
# La versión sale del payload (los nombres de los archivos dependen de ella). Todavía SIN verificar: se verifica abajo.
PAYLOAD="$(tr -d '\n' <"$TMP/tatana-update-docker.json" | sed -nE 's/.*"payload"[[:space:]]*:[[:space:]]*"([A-Za-z0-9+\/=]+)".*/\1/p')"
V="$(printf '%s' "$PAYLOAD" | base64 -d 2>/dev/null | tr -d '\n' | sed -nE 's/.*"version"[[:space:]]*:[[:space:]]*"([0-9]+\.[0-9]+\.[0-9]+)".*/\1/p')"
[[ -n "$V" ]] || morir "El manifiesto del servidor no se puede leer."
DEST="$HOME/Tatana-Docker-v$V"
mkdir -p "$DEST.descarga"
KIT="kit-tatana-docker-v$V.tar.gz"
IMG_F="imagen-tatana-docker-v$V.tar.gz"
echo "  Tatana en Docker $V: kit e imagen (unos 300 MB; si se corta, volvé a correr el script y sigue)..."
bajar "${CANAL}$KIT" "$DEST.descarga/$KIT" || morir "Se cortó la descarga de $KIT: volvé a correr el script."
bajar "${CANAL}$IMG_F" "$DEST.descarga/$IMG_F" || morir "Se cortó la descarga de $IMG_F: volvé a correr el script."
ok "descargado en $DEST.descarga"

paso "[3/5] Verificación (firma Ed25519, tamaño y SHA-256)"
mkdir -p "$TMP/kit"
tar -xzf "$DEST.descarga/$KIT" -C "$TMP/kit" --strip-components=1
[[ -f "$TMP/kit/verificar-manifiesto-docker.py" && -f "$TMP/kit/claves-confiables.txt" ]] || morir "El kit descargado está incompleto."
IMG_PY="$IMAGEN_INSTALADA"
if [[ -z "$IMG_PY" ]]; then
  # PC nueva: no hay una imagen instalada en la que confiar; se usa la recién bajada (avisando).
  aviso "Instalación nueva: se verifica con el Python de la imagen recién descargada (la confianza es la del TLS del servidor)."
  "${DOCKER[@]}" load -i "$DEST.descarga/$IMG_F" >/dev/null
  IMG_PY="factum-tatana-docker:$V"
fi
cp "$TMP/tatana-update-docker.json" "$TMP/kit/"
SALIDA="$("${DOCKER[@]}" run --rm --platform linux/amd64 --network none --security-opt label=disable \
  -v "$TMP/kit:/k:ro" --entrypoint "$PYTHON_IMAGEN" "$IMG_PY" -I /k/verificar-manifiesto-docker.py \
  --manifiesto /k/tatana-update-docker.json --claves /k/claves-confiables.txt)" ||
  morir "El manifiesto del servidor NO es válido: $(clave_de "$SALIDA" MOTIVO) — $(clave_de "$SALIDA" DETALLE). No se instaló nada."
[[ "$(clave_de "$SALIDA" VERSION)" == "$V" ]] || morir "El manifiesto firmado es de otra versión."
[[ "$(tamano_de "$DEST.descarga/$KIT")" == "$(clave_de "$SALIDA" KIT_TAMANO)" && "$(sha256_de "$DEST.descarga/$KIT")" == "$(clave_de "$SALIDA" KIT_SHA256)" ]] ||
  { rm -f "$DEST.descarga/$KIT"; morir "El kit no coincide con el manifiesto firmado (se borró: volvé a correr el script)."; }
[[ "$(tamano_de "$DEST.descarga/$IMG_F")" == "$(clave_de "$SALIDA" TAMANO)" && "$(sha256_de "$DEST.descarga/$IMG_F")" == "$(clave_de "$SALIDA" SHA256)" ]] ||
  { rm -f "$DEST.descarga/$IMG_F"; morir "La imagen no coincide con el manifiesto firmado (se borró: volvé a correr el script)."; }
ok "firma válida ($(clave_de "$SALIDA" KEY_ID)); kit e imagen coinciden con lo firmado"

paso "[4/5] Instalando (instalar-tatana-docker.sh)"
rm -rf "$DEST"
mkdir -p "$DEST"
tar -xzf "$DEST.descarga/$KIT" -C "$DEST" --strip-components=1
mv "$DEST.descarga/$IMG_F" "$DEST/"
rm -rf "$DEST.descarga"
ARGS=()
((SI)) && ARGS+=(--si)
# Sin --origen: el instalador conserva los orígenes de la instalación anterior (https://factum.internal). En una PC
# nueva se le pasa el servidor.
[[ -n "$IMAGEN_INSTALADA" ]] || ARGS+=(--origen "https://$HOST")
[[ "$CANAL" == "https://factum.internal/tatana/docker/" ]] || ARGS+=(--actualizador "$CANAL")
(cd "$DEST" && bash ./instalar-tatana-docker.sh "${ARGS[@]}") ||
  morir "La instalación no terminó (ver arriba). No se perdió nada: los archivos quedan en $DEST; volvé a correr este script."

paso "[5/5] Primera consulta al servidor"
if [[ -f /etc/systemd/system/factum-tatana-actualizador.service ]]; then
  sudo systemctl start factum-tatana-actualizador.service || aviso "la primera consulta terminó con error (journalctl -u factum-tatana-actualizador)"
fi
bash "$DEST/estado-tatana-docker.sh" --actualizaciones || true
echo
echo "${_V}${_N}Listo.${_0} Esta PC se actualiza sola desde $CANAL (cada 30 minutos, solo con Tatana libre)."
