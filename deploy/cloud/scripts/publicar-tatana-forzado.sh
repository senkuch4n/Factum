#!/usr/bin/env bash
# Factum en la nube — comando forzado de la clave SSH de publicación de Tatana
# (SDD tatana-instalador-autoupdate D-T21, §9, contrato §13.D).
#
# Va en ~tatana-pub/.ssh/authorized_keys así (una sola línea):
#   command="/srv/factum/bin/publicar-tatana-forzado.sh",restrict ssh-ed25519 AAAA... factum-tatana-release
#
# tatana-pub NO está en el grupo docker: lo único que puede hacer esta clave es esto. Es una COPIA
# ESTABLE en /srv/factum/bin/ (root:root 755, la instala preparar-servidor.sh, bloque 11): si cambia
# en el repo, se vuelve a copiar a mano (guía, sección 19).
#
# Comandos (llegan en SSH_ORIGINAL_COMMAND):
#   estado            -> imprime la versión publicada (tatana-updates/VERSION) o "ninguna"
#   publicar X.Y.Z    -> lee un tar por stdin con EXACTAMENTE estos 5 archivos:
#                          Tatana-Setup-X.Y.Z.exe, Tatana-Setup-X.Y.Z.exe.blockmap, latest.yml,
#                          tatana-update.json, SHA256SUMS
#                        valida nombres, hashes y versión, y publica con renombres atómicos en
#                          /srv/factum/tatana-updates/     (https://<dominio>/tatana/updates/)
#                          /srv/factum/descargas/tatana/   (Tatana-Setup-Windows.exe + .sha256)
# Códigos de salida: 0 ok; 2 comando no permitido; 3 demasiado grande; 4 contenido del tar;
# 5 hashes o versión de latest.yml; 6 versión <= la vigente.
#
# Este servidor NO es la raíz de confianza: Tatana verifica la firma Ed25519 de tatana-update.json
# (la clave privada vive solo en GitHub). Este script solo evita publicar basura o volver atrás.
#
# --raiz <dir> es SOLO para la prueba local (ops/tatana/probar-publicar-forzado.sh): el command= de
# authorized_keys fija la línea de comandos y el cliente SSH no puede agregar argumentos.
set -euo pipefail
umask 022

RAIZ=/srv/factum
if [[ "${1:-}" == --raiz && -n "${2:-}" ]]; then
  RAIZ="$2"
  shift 2
fi
CANAL="$RAIZ/tatana-updates"
DESCARGAS="$RAIZ/descargas/tatana"
STAGING_BASE="$RAIZ/tatana-staging"
MAX_BYTES=1073741824 # 1 GiB
RETENER=5            # D-T22: instaladores que quedan en el canal

pedido="${SSH_ORIGINAL_COMMAND:-}"
VERSION=""

registrar() { logger -t factum-tatana -- "$*" 2>/dev/null || true; }
fallar() {
  local codigo="$1"
  shift
  registrar "publicar ${VERSION:-?}: ERROR ($codigo) $*"
  echo "ERROR: $*" >&2
  exit "$codigo"
}

version_vigente() {
  [[ -f "$CANAL/VERSION" ]] || return 0
  local v
  v="$(head -c 64 "$CANAL/VERSION" | tr -cd '0-9.')"
  [[ "$v" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] && printf '%s' "$v"
  return 0
}

# true si $1 es estrictamente mayor que $2 (semver X.Y.Z, comparación numérica por campo).
mayor_que() {
  local a1 a2 a3 b1 b2 b3
  IFS=. read -r a1 a2 a3 <<<"$1"
  IFS=. read -r b1 b2 b3 <<<"$2"
  ((10#$a1 != 10#$b1)) && { ((10#$a1 > 10#$b1)); return; }
  ((10#$a2 != 10#$b2)) && { ((10#$a2 > 10#$b2)); return; }
  ((10#$a3 > 10#$b3))
}

if [[ "$pedido" == estado ]]; then
  v="$(version_vigente)"
  echo "${v:-ninguna}"
  exit 0
fi

if [[ ! "$pedido" =~ ^publicar\ ([0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5})$ ]]; then
  # Se registra recortado y sin caracteres de control (es texto que manda el cliente).
  registrar "rechazado: $(printf '%s' "$pedido" | tr -cd '[:print:]' | cut -c1-120)"
  echo "ERROR: comando no permitido" >&2
  exit 2
fi
VERSION="${BASH_REMATCH[1]}"
EXE="Tatana-Setup-$VERSION.exe"
BLOCKMAP="$EXE.blockmap"
ESPERADOS="$(printf '%s\n' "$EXE" "$BLOCKMAP" SHA256SUMS latest.yml tatana-update.json | LC_ALL=C sort)"
HASHEADOS="$(printf '%s\n' "$EXE" "$BLOCKMAP" latest.yml tatana-update.json | LC_ALL=C sort)"

[[ -d "$CANAL" && -d "$DESCARGAS" && -d "$STAGING_BASE" ]] ||
  fallar 1 "faltan las carpetas $CANAL, $DESCARGAS o $STAGING_BASE (preparar-servidor.sh, bloque 11)"

# Una publicación a la vez (el workflow ya serializa con concurrency; esto cubre un "a mano").
if command -v flock >/dev/null 2>&1; then
  exec 9>"$STAGING_BASE/.lock"
  flock -w 60 9 || fallar 1 "hay otra publicación en curso"
fi

registrar "publicar $VERSION: inicio"

# 1. Staging propio (se borra siempre al salir).
STAGING="$STAGING_BASE/$VERSION-$(date +%s)-$$"
mkdir -m 700 "$STAGING"
trap 'rm -rf -- "$STAGING"' EXIT
mkdir "$STAGING/x"

# 2. stdin con tope: se leen MAX+1 bytes; si llegan todos, es demasiado grande.
head -c $((MAX_BYTES + 1)) >"$STAGING/bundle.tar"
tam="$(wc -c <"$STAGING/bundle.tar" | tr -d ' ')"
((tam <= MAX_BYTES)) || fallar 3 "el paquete supera 1 GiB"
((tam > 0)) || fallar 4 "no llegó ningún paquete por stdin"
registrar "publicar $VERSION: recibido ($tam bytes)"

# 3. Contenido: exactamente los 5 nombres, todos archivos regulares (sin directorios, links ni rutas).
nombres="$(tar -tf "$STAGING/bundle.tar" 2>/dev/null)" || fallar 4 "el paquete no es un tar válido"
[[ "$(printf '%s\n' "$nombres" | LC_ALL=C sort)" == "$ESPERADOS" ]] ||
  fallar 4 "el tar tiene que traer exactamente: $(tr '\n' ' ' <<<"$ESPERADOS")"
tipos="$(tar -tvf "$STAGING/bundle.tar" 2>/dev/null | cut -c1 | LC_ALL=C sort -u)" || fallar 4 "el paquete no es un tar válido"
[[ "$tipos" == "-" ]] || fallar 4 "el tar solo puede traer archivos regulares"

# 4. Extraer (los nombres ya están validados: no hay "/" ni "..").
tar -xf "$STAGING/bundle.tar" -C "$STAGING/x" --no-same-owner --no-same-permissions ||
  fallar 4 "no se pudo extraer el tar"
rm -f -- "$STAGING/bundle.tar"
for f in $ESPERADOS; do
  [[ -f "$STAGING/x/$f" && ! -L "$STAGING/x/$f" ]] || fallar 4 "falta $f en el tar"
done

# 5. Hashes: SHA256SUMS cubre exactamente los otros 4 y todos coinciden; latest.yml es de esta versión.
listados="$(awk '{ n = $2; sub(/^\*/, "", n); print n }' "$STAGING/x/SHA256SUMS" | LC_ALL=C sort)"
[[ "$listados" == "$HASHEADOS" ]] || fallar 5 "SHA256SUMS tiene que listar exactamente: $(tr '\n' ' ' <<<"$HASHEADOS")"
(cd "$STAGING/x" && sha256sum --check --strict --quiet SHA256SUMS) >/dev/null 2>&1 ||
  fallar 5 "los SHA-256 no coinciden con SHA256SUMS"
grep -qx "version: $VERSION" "$STAGING/x/latest.yml" || fallar 5 "latest.yml no dice 'version: $VERSION'"
grep -qE "^(path|  - url): $EXE\$" "$STAGING/x/latest.yml" || fallar 5 "latest.yml no apunta a $EXE"
registrar "publicar $VERSION: hashes ok"

# 6. Monotonía (D-T7): nunca una versión igual o menor que la vigente.
vigente="$(version_vigente)"
if [[ -n "$vigente" ]] && ! mayor_que "$VERSION" "$vigente"; then
  fallar 6 "la versión $VERSION no es mayor que la publicada ($vigente). Para revertir, publicá una X.Y.Z+1."
fi

# 7. Publicar con renombres atómicos (mismo filesystem). Orden: instaladores primero, después los
#    manifiestos (latest.yml y por último tatana-update.json, que es lo que Tatana lee primero) y VERSION.
publicar_archivo() { # origen destino
  cp -- "$1" "$2.tmp"
  chmod 644 "$2.tmp"
  mv -f -- "$2.tmp" "$2"
}
publicar_archivo "$STAGING/x/$EXE" "$CANAL/$EXE"
publicar_archivo "$STAGING/x/$BLOCKMAP" "$CANAL/$BLOCKMAP"
publicar_archivo "$STAGING/x/$EXE" "$DESCARGAS/Tatana-Setup-Windows.exe"
hash_exe="$(sha256sum "$STAGING/x/$EXE" | cut -d' ' -f1)"
printf '%s  %s\n' "$hash_exe" Tatana-Setup-Windows.exe >"$STAGING/descarga.sha256"
publicar_archivo "$STAGING/descarga.sha256" "$DESCARGAS/Tatana-Setup-Windows.exe.sha256"
publicar_archivo "$STAGING/x/latest.yml" "$CANAL/latest.yml"
publicar_archivo "$STAGING/x/tatana-update.json" "$CANAL/tatana-update.json"
printf '%s\n' "$VERSION" >"$STAGING/VERSION"
publicar_archivo "$STAGING/VERSION" "$CANAL/VERSION"
registrar "publicar $VERSION: publicado (sha256 $hash_exe)"

# 8. Retención (D-T22): los $RETENER instaladores más nuevos con su .blockmap. La vigente nunca se borra.
mapfile -t instaladores < <(
  find "$CANAL" -maxdepth 1 -type f -name 'Tatana-Setup-*.exe' -printf '%f\n' 2>/dev/null |
    sed -nE 's/^Tatana-Setup-([0-9]+\.[0-9]+\.[0-9]+)\.exe$/\1/p' | sort -t. -k1,1n -k2,2n -k3,3n
)
total=${#instaladores[@]}
if ((total > RETENER)); then
  for ((i = 0; i < total - RETENER; i++)); do
    viejo="${instaladores[$i]}"
    [[ "$viejo" == "$VERSION" ]] && continue
    rm -f -- "$CANAL/Tatana-Setup-$viejo.exe" "$CANAL/Tatana-Setup-$viejo.exe.blockmap"
    registrar "publicar $VERSION: retención, se borró $viejo"
  done
fi

echo "OK Tatana $VERSION publicado (sha256 $hash_exe)"
