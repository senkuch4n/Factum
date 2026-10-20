#!/usr/bin/env bash
# Factum en la nube — comando forzado de la clave SSH de publicación de Tatana
# (SDD tatana-instalador-autoupdate D-T21, §9, contrato §13.D; tatana-linux D-T9, D-T27, §9.3, contrato §10.D).
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
#   publicar X.Y.Z    -> lee un tar por stdin con EXACTAMENTE estos 8 archivos (Windows y Linux, misma versión):
#                          Tatana-Setup-X.Y.Z.exe, Tatana-Setup-X.Y.Z.exe.blockmap, latest.yml,
#                          tatana-update.json, Tatana-X.Y.Z-x86_64.AppImage, latest-linux.yml,
#                          tatana-update-linux.json, SHA256SUMS (cubre los otros 7)
#                        valida nombres, hashes y versión, y publica con renombres atómicos en
#                          /srv/factum/tatana-updates/     (https://<dominio>/tatana/updates/)
#                          /srv/factum/descargas/tatana/   (Tatana-Setup-Windows.exe, Tatana-Linux-x86_64.AppImage,
#                                                           sus .sha256 e index.html con los requisitos)
#                        Si falta un archivo de cualquiera de las dos plataformas, no se publica nada (código 4).
# Códigos de salida: 0 ok; 2 comando no permitido; 3 demasiado grande; 4 contenido del tar;
# 5 hashes o versión de latest.yml / latest-linux.yml; 6 versión <= la vigente.
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
MAX_BYTES=2147483648 # 2 GiB (instalador de Windows + AppImage de Linux)
RETENER=5            # D-T22: instaladores (y AppImage) que quedan en el canal

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

# Página de descarga estática (tatana-linux D-T27): requisitos, los dos enlaces con su SHA-256 y cómo marcar el
# AppImage como ejecutable. Sin JS. Caddy la sirve como /descargas/tatana/ (file_server con index.html).
pagina_descarga() { # <versión> <sha256 exe> <sha256 AppImage>
  cat <<HTML
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Descargar Tatana $1</title>
<style>
  body { font-family: system-ui, -apple-system, "Segoe UI", Roboto, Ubuntu, sans-serif; max-width: 52rem; margin: 2rem auto;
         padding: 0 1rem; line-height: 1.5; color: #1a1a1a; background: #fff; }
  h1 { font-size: 1.6rem; margin-bottom: .2rem; }
  h2 { font-size: 1.2rem; margin-top: 2rem; }
  .version { color: #555; margin-top: 0; }
  .caja { border: 1px solid #ccc; border-radius: .5rem; padding: 1rem 1.2rem; margin: 1rem 0; }
  a.boton { display: inline-block; padding: .6rem 1rem; border-radius: .4rem; background: #1f4e79; color: #fff;
            text-decoration: none; font-weight: 600; }
  a.boton:focus-visible { outline: 3px solid #f0b400; outline-offset: 2px; }
  code { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: .85em; word-break: break-all; }
  li { margin: .3rem 0; }
</style>
</head>
<body>
<main>
<h1>Tatana, el agente de Factum</h1>
<p class="version">Versión $1</p>

<section class="caja" aria-labelledby="win">
<h2 id="win">Windows</h2>
<p>Windows 10 u 11 de 64 bits.</p>
<p><a class="boton" href="Tatana-Setup-Windows.exe">Descargar Tatana para Windows</a></p>
<p>SHA-256: <code>$2</code> (<a href="Tatana-Setup-Windows.exe.sha256">archivo .sha256</a>)</p>
</section>

<section class="caja" aria-labelledby="linux">
<h2 id="linux">Linux</h2>
<p><strong>Antes de descargar, revisá que tu PC cumpla los requisitos:</strong></p>
<ul>
  <li>PC de 64 bits Intel o AMD (x86-64). No sirve en PCs de 32 bits ni ARM.</li>
  <li>Linux con glibc 2.35 o posterior: Ubuntu 22.04 o 24.04, Linux Mint 21 o 22, Debian 12, Pop!_OS o Zorin 22.04
      o posterior. Fedora 36 o posterior funciona sin garantía.</li>
  <li>Escritorio gráfico (X11 o Wayland) con systemd y polkit, y FUSE 3 (<code>fusermount3</code>, viene instalado
      en esas distros).</li>
  <li>4 GB de RAM (8 GB recomendados para grabar la sesión completa), 10 GB libres e internet para la configuración
      inicial.</li>
</ul>
<p><a class="boton" href="Tatana-Linux-x86_64.AppImage">Descargar Tatana para Linux</a></p>
<p>SHA-256: <code>$3</code> (<a href="Tatana-Linux-x86_64.AppImage.sha256">archivo .sha256</a>). Para verificarlo:
<code>sha256sum Tatana-Linux-x86_64.AppImage</code></p>

<h2>Abrir Tatana en Linux</h2>
<p>El archivo descargado hay que marcarlo como ejecutable una sola vez:</p>
<ul>
  <li><strong>GNOME (Ubuntu, Archivos/Nautilus):</strong> clic derecho en el archivo → Propiedades → activá
      "Ejecutable como programa". Después, doble clic.</li>
  <li><strong>KDE (Dolphin):</strong> clic derecho → Propiedades → Permisos → marcá "Es ejecutable". Después,
      doble clic.</li>
  <li><strong>Cinnamon (Linux Mint, Nemo):</strong> clic derecho → Propiedades → Permisos → marcá "Permitir
      ejecutar el archivo como un programa". Después, doble clic.</li>
  <li>Desde una terminal: <code>chmod +x Tatana-Linux-x86_64.AppImage &amp;&amp; ./Tatana-Linux-x86_64.AppImage</code></li>
</ul>
<p>La primera vez, Tatana ofrece instalarse en esta PC y después "Preparar esta PC", que pide la contraseña de
administrador una sola vez para reconocer iPhone y Android por USB.</p>
<p>Si con doble clic no pasa nada, falta FUSE: <code>sudo apt install fuse3</code>, o probá
<code>./Tatana-Linux-x86_64.AppImage --appimage-extract-and-run</code>.</p>
</section>
</main>
</body>
</html>
HTML
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
APPIMAGE="Tatana-$VERSION-x86_64.AppImage"
ESPERADOS="$(printf '%s\n' "$EXE" "$BLOCKMAP" latest.yml tatana-update.json \
  "$APPIMAGE" latest-linux.yml tatana-update-linux.json SHA256SUMS | LC_ALL=C sort)"
HASHEADOS="$(printf '%s\n' "$EXE" "$BLOCKMAP" latest.yml tatana-update.json \
  "$APPIMAGE" latest-linux.yml tatana-update-linux.json | LC_ALL=C sort)"

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

# 3. Contenido: exactamente los 8 nombres, todos archivos regulares (sin directorios, links ni rutas).
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

# 5. Hashes: SHA256SUMS cubre exactamente los otros 7 y todos coinciden; latest.yml y latest-linux.yml son de
#    esta versión y apuntan a su archivo.
listados="$(awk '{ n = $2; sub(/^\*/, "", n); print n }' "$STAGING/x/SHA256SUMS" | LC_ALL=C sort)"
[[ "$listados" == "$HASHEADOS" ]] || fallar 5 "SHA256SUMS tiene que listar exactamente: $(tr '\n' ' ' <<<"$HASHEADOS")"
(cd "$STAGING/x" && sha256sum --check --strict --quiet SHA256SUMS) >/dev/null 2>&1 ||
  fallar 5 "los SHA-256 no coinciden con SHA256SUMS"
grep -qx "version: $VERSION" "$STAGING/x/latest.yml" || fallar 5 "latest.yml no dice 'version: $VERSION'"
grep -qE "^(path|  - url): $EXE\$" "$STAGING/x/latest.yml" || fallar 5 "latest.yml no apunta a $EXE"
grep -qx "version: $VERSION" "$STAGING/x/latest-linux.yml" || fallar 5 "latest-linux.yml no dice 'version: $VERSION'"
grep -qE "^(path|  - url): $APPIMAGE\$" "$STAGING/x/latest-linux.yml" || fallar 5 "latest-linux.yml no apunta a $APPIMAGE"
registrar "publicar $VERSION: hashes ok"

# 6. Monotonía (D-T7): nunca una versión igual o menor que la vigente.
vigente="$(version_vigente)"
if [[ -n "$vigente" ]] && ! mayor_que "$VERSION" "$vigente"; then
  fallar 6 "la versión $VERSION no es mayor que la publicada ($vigente). Para revertir, publicá una X.Y.Z+1."
fi

# 7. Publicar con renombres atómicos (mismo filesystem). Orden (tatana-linux §9.3): binarios (exe, blockmap y
#    AppImage), copias de descarga con su .sha256, index.html, latest.yml, latest-linux.yml, tatana-update-linux.json,
#    tatana-update.json (lo que Tatana de Windows lee primero) y VERSION.
publicar_archivo() { # origen destino
  cp -- "$1" "$2.tmp"
  chmod 644 "$2.tmp"
  mv -f -- "$2.tmp" "$2"
}
publicar_archivo "$STAGING/x/$EXE" "$CANAL/$EXE"
publicar_archivo "$STAGING/x/$BLOCKMAP" "$CANAL/$BLOCKMAP"
publicar_archivo "$STAGING/x/$APPIMAGE" "$CANAL/$APPIMAGE"
publicar_archivo "$STAGING/x/$EXE" "$DESCARGAS/Tatana-Setup-Windows.exe"
hash_exe="$(sha256sum "$STAGING/x/$EXE" | cut -d' ' -f1)"
printf '%s  %s\n' "$hash_exe" Tatana-Setup-Windows.exe >"$STAGING/descarga.sha256"
publicar_archivo "$STAGING/descarga.sha256" "$DESCARGAS/Tatana-Setup-Windows.exe.sha256"
publicar_archivo "$STAGING/x/$APPIMAGE" "$DESCARGAS/Tatana-Linux-x86_64.AppImage"
hash_appimage="$(sha256sum "$STAGING/x/$APPIMAGE" | cut -d' ' -f1)"
printf '%s  %s\n' "$hash_appimage" Tatana-Linux-x86_64.AppImage >"$STAGING/descarga-linux.sha256"
publicar_archivo "$STAGING/descarga-linux.sha256" "$DESCARGAS/Tatana-Linux-x86_64.AppImage.sha256"
pagina_descarga "$VERSION" "$hash_exe" "$hash_appimage" >"$STAGING/index.html"
publicar_archivo "$STAGING/index.html" "$DESCARGAS/index.html"
publicar_archivo "$STAGING/x/latest.yml" "$CANAL/latest.yml"
publicar_archivo "$STAGING/x/latest-linux.yml" "$CANAL/latest-linux.yml"
publicar_archivo "$STAGING/x/tatana-update-linux.json" "$CANAL/tatana-update-linux.json"
publicar_archivo "$STAGING/x/tatana-update.json" "$CANAL/tatana-update.json"
printf '%s\n' "$VERSION" >"$STAGING/VERSION"
publicar_archivo "$STAGING/VERSION" "$CANAL/VERSION"
registrar "publicar $VERSION: publicado (sha256 exe $hash_exe, AppImage $hash_appimage)"

# 8. Retención (D-T22; tatana-linux §9.3): los $RETENER instaladores más nuevos con su .blockmap y, con el mismo
#    criterio, los $RETENER AppImage más nuevos. La vigente nunca se borra.
retener() { # <regex con la versión en el grupo 1> <función que borra una versión>
  local patron="$1" borrar="$2" viejo i total
  local -a versiones
  mapfile -t versiones < <(
    find "$CANAL" -maxdepth 1 -type f -printf '%f\n' 2>/dev/null |
      sed -nE "s/$patron/\\1/p" | sort -t. -k1,1n -k2,2n -k3,3n
  )
  total=${#versiones[@]}
  if ((total > RETENER)); then
    for ((i = 0; i < total - RETENER; i++)); do
      viejo="${versiones[$i]}"
      [[ "$viejo" == "$VERSION" ]] && continue
      "$borrar" "$viejo"
      registrar "publicar $VERSION: retención, se borró $viejo ($borrar)"
    done
  fi
}
borrar_exe() { rm -f -- "$CANAL/Tatana-Setup-$1.exe" "$CANAL/Tatana-Setup-$1.exe.blockmap"; }
borrar_appimage() { rm -f -- "$CANAL/Tatana-$1-x86_64.AppImage"; }
retener '^Tatana-Setup-([0-9]+\.[0-9]+\.[0-9]+)\.exe$' borrar_exe
retener '^Tatana-([0-9]+\.[0-9]+\.[0-9]+)-x86_64\.AppImage$' borrar_appimage

echo "OK Tatana $VERSION publicado (Windows sha256 $hash_exe; Linux sha256 $hash_appimage)"
