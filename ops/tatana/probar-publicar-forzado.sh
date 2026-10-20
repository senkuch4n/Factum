#!/usr/bin/env bash
# shellcheck disable=SC2015 # ok/mal nunca fallan: "cond && ok || mal" es un if/else.
# Prueba local de deploy/cloud/scripts/publicar-tatana-forzado.sh (SDD tatana-instalador-autoupdate B20;
# tatana-linux B24: 8 archivos, Windows + Linux, index.html y retención de AppImage).
# Corre en Linux (GNU tar, sha256sum, find -printf): en CI (ubuntu) o en la Mac con Docker:
#   docker run --rm -v "$PWD":/repo:ro -w /repo ubuntu:24.04 bash ops/tatana/probar-publicar-forzado.sh
# Usa una raíz temporal propia (--raiz) y la borra al terminar. No toca nada fuera de ella.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCRIPT="$REPO/deploy/cloud/scripts/publicar-tatana-forzado.sh"
T="$(mktemp -d "${TMPDIR:-/tmp}/probar-publicar.XXXXXX")"
trap 'rm -rf -- "$T"' EXIT
RAIZ="$T/srv"
mkdir -p "$RAIZ/tatana-updates" "$RAIZ/descargas/tatana" "$RAIZ/tatana-staging"

fallos=0
ok() { echo "ok   $*"; }
mal() { echo "FALLA $*"; fallos=$((fallos + 1)); }

# Los 7 archivos que cubre SHA256SUMS (orden de tar_de).
archivos_de() { # v
  printf '%s\n' "Tatana-Setup-$1.exe" "Tatana-Setup-$1.exe.blockmap" latest.yml tatana-update.json \
    "Tatana-$1-x86_64.AppImage" latest-linux.yml tatana-update-linux.json
}

# Arma en $T/b/<v> los 8 archivos de una versión.
# $2 = "roto-hash" | "roto-yml" | "roto-hash-linux" | "roto-yml-linux" | "" .
armar() {
  local v="$1" modo="${2:-}" d="$T/b/$1"
  rm -rf "$d"
  mkdir -p "$d"
  head -c 4096 /dev/urandom >"$d/Tatana-Setup-$v.exe"
  head -c 512 /dev/urandom >"$d/Tatana-Setup-$v.exe.blockmap"
  head -c 6144 /dev/urandom >"$d/Tatana-$v-x86_64.AppImage"
  printf 'version: %s\nfiles:\n  - url: Tatana-Setup-%s.exe\n    sha512: x\n    size: 4096\npath: Tatana-Setup-%s.exe\nsha512: x\n' \
    "$([[ "$modo" == roto-yml ]] && echo 0.0.1 || echo "$v")" "$v" "$v" >"$d/latest.yml"
  printf 'version: %s\nfiles:\n  - url: Tatana-%s-x86_64.AppImage\n    sha512: x\n    size: 6144\n    blockMapSize: 10\npath: Tatana-%s-x86_64.AppImage\nsha512: x\n' \
    "$([[ "$modo" == roto-yml-linux ]] && echo 0.0.1 || echo "$v")" "$v" "$v" >"$d/latest-linux.yml"
  printf '{"schema":1,"key_id":"prueba","payload":"e30=","signature":"AA=="}\n' >"$d/tatana-update.json"
  printf '{"schema":1,"key_id":"prueba","payload":"e31=","signature":"AA=="}\n' >"$d/tatana-update-linux.json"
  (cd "$d" && archivos_de "$v" | xargs sha256sum >SHA256SUMS)
  [[ "$modo" == roto-hash ]] && printf 'x' >>"$d/latest.yml"
  [[ "$modo" == roto-hash-linux ]] && printf 'x' >>"$d/Tatana-$v-x86_64.AppImage"
  return 0
}

# publicar <comando> <tar|-> → imprime el código de salida.
correr() {
  local cmd="$1" tarfile="$2" rc=0
  if [[ "$tarfile" == - ]]; then
    SSH_ORIGINAL_COMMAND="$cmd" bash "$SCRIPT" --raiz "$RAIZ" </dev/null >/dev/null 2>&1 || rc=$?
  else
    SSH_ORIGINAL_COMMAND="$cmd" bash "$SCRIPT" --raiz "$RAIZ" <"$tarfile" >/dev/null 2>&1 || rc=$?
  fi
  echo "$rc"
}

tar_de() { # v [archivos extra...] → ruta del tar
  local v="$1"
  shift
  local -a lista
  mapfile -t lista < <(archivos_de "$v")
  (cd "$T/b/$v" && tar -cf "$T/b/$v.tar" "${lista[@]}" SHA256SUMS "$@")
  echo "$T/b/$v.tar"
}

tar_sin() { # v archivo → tar con los 8 menos ese archivo
  local v="$1" sacar="$2"
  local -a lista
  mapfile -t lista < <(archivos_de "$v"; echo SHA256SUMS)
  local -a resto=()
  local f
  for f in "${lista[@]}"; do [[ "$f" == "$sacar" ]] || resto+=("$f"); done
  (cd "$T/b/$v" && tar -cf "$T/b/sin.tar" "${resto[@]}")
  echo "$T/b/sin.tar"
}

esperar() { # descripción código-esperado código-obtenido
  if [[ "$2" == "$3" ]]; then ok "$1 ($3)"; else mal "$1: esperaba $2 y dio $3"; fi
}

[[ "$(SSH_ORIGINAL_COMMAND=estado bash "$SCRIPT" --raiz "$RAIZ")" == ninguna ]] && ok "estado sin publicar = ninguna" || mal "estado inicial"
esperar "comando no permitido" 2 "$(correr 'rm -rf /' -)"
esperar "versión mal formada" 2 "$(correr 'publicar 1.4' -)"
esperar "versión con sufijo" 2 "$(correr 'publicar 1.4.0;id' -)"
esperar "stdin vacío" 4 "$(correr 'publicar 1.0.0' -)"

armar 1.0.0
esperar "publicar 1.0.0" 0 "$(correr 'publicar 1.0.0' "$(tar_de 1.0.0)")"
[[ "$(SSH_ORIGINAL_COMMAND=estado bash "$SCRIPT" --raiz "$RAIZ")" == 1.0.0 ]] && ok "estado = 1.0.0" || mal "estado tras publicar"
cmp -s "$T/b/1.0.0/Tatana-Setup-1.0.0.exe" "$RAIZ/tatana-updates/Tatana-Setup-1.0.0.exe" && ok "exe en el canal" || mal "exe en el canal"
cmp -s "$T/b/1.0.0/Tatana-Setup-1.0.0.exe" "$RAIZ/descargas/tatana/Tatana-Setup-Windows.exe" && ok "exe en descargas" || mal "exe en descargas"
(cd "$RAIZ/descargas/tatana" && sha256sum -c --quiet Tatana-Setup-Windows.exe.sha256) && ok ".sha256 de descargas" || mal ".sha256 de descargas"
cmp -s "$T/b/1.0.0/tatana-update.json" "$RAIZ/tatana-updates/tatana-update.json" && ok "manifiesto publicado" || mal "manifiesto"
[[ "$(stat -c %a "$RAIZ/tatana-updates/latest.yml")" == 644 ]] && ok "permisos 644" || mal "permisos"
# Linux (tatana-linux §10.D).
cmp -s "$T/b/1.0.0/Tatana-1.0.0-x86_64.AppImage" "$RAIZ/tatana-updates/Tatana-1.0.0-x86_64.AppImage" && ok "AppImage en el canal" || mal "AppImage en el canal"
cmp -s "$T/b/1.0.0/Tatana-1.0.0-x86_64.AppImage" "$RAIZ/descargas/tatana/Tatana-Linux-x86_64.AppImage" && ok "AppImage en descargas" || mal "AppImage en descargas"
(cd "$RAIZ/descargas/tatana" && sha256sum -c --quiet Tatana-Linux-x86_64.AppImage.sha256) && ok ".sha256 del AppImage" || mal ".sha256 del AppImage"
cmp -s "$T/b/1.0.0/latest-linux.yml" "$RAIZ/tatana-updates/latest-linux.yml" && ok "latest-linux.yml publicado" || mal "latest-linux.yml"
cmp -s "$T/b/1.0.0/tatana-update-linux.json" "$RAIZ/tatana-updates/tatana-update-linux.json" && ok "manifiesto de Linux publicado" || mal "manifiesto de Linux"
INDEX="$RAIZ/descargas/tatana/index.html"
if [[ -f "$INDEX" ]] && grep -q 'Versión 1.0.0' "$INDEX" && grep -q 'href="Tatana-Linux-x86_64.AppImage"' "$INDEX" \
   && grep -q 'href="Tatana-Setup-Windows.exe"' "$INDEX" && grep -q 'glibc 2.35' "$INDEX" \
   && grep -q "$(sha256sum "$T/b/1.0.0/Tatana-1.0.0-x86_64.AppImage" | cut -d' ' -f1)" "$INDEX" \
   && grep -q "$(sha256sum "$T/b/1.0.0/Tatana-Setup-1.0.0.exe" | cut -d' ' -f1)" "$INDEX" \
   && grep -q 'Nautilus' "$INDEX" && grep -q 'Dolphin' "$INDEX" && grep -q 'Nemo' "$INDEX" && ! grep -qi '<script' "$INDEX"; then
  ok "index.html con requisitos, los dos enlaces, sus SHA-256 y sin JS"
else
  mal "index.html"
fi

esperar "repetir 1.0.0" 6 "$(correr 'publicar 1.0.0' "$(tar_de 1.0.0)")"
armar 0.9.9
esperar "downgrade 0.9.9" 6 "$(correr 'publicar 0.9.9' "$(tar_de 0.9.9)")"

armar 1.0.1
esperar "archivo de más" 4 "$(correr 'publicar 1.0.1' "$(cd "$T/b/1.0.1" && echo x >extra.txt && tar_de 1.0.1 extra.txt)")"
esperar "falta el blockmap" 4 "$(correr 'publicar 1.0.1' "$(tar_sin 1.0.1 Tatana-Setup-1.0.1.exe.blockmap)")"
esperar "falta el AppImage" 4 "$(correr 'publicar 1.0.1' "$(tar_sin 1.0.1 Tatana-1.0.1-x86_64.AppImage)")"
esperar "falta latest-linux.yml" 4 "$(correr 'publicar 1.0.1' "$(tar_sin 1.0.1 latest-linux.yml)")"
esperar "falta tatana-update-linux.json" 4 "$(correr 'publicar 1.0.1' "$(tar_sin 1.0.1 tatana-update-linux.json)")"
esperar "solo los 5 de Windows (VPS viejo)" 4 "$(correr 'publicar 1.0.1' "$(cd "$T/b/1.0.1" && tar -cf "$T/b/win.tar" Tatana-Setup-1.0.1.exe Tatana-Setup-1.0.1.exe.blockmap latest.yml tatana-update.json SHA256SUMS && echo "$T/b/win.tar")")"
mkdir -p "$T/b/dir/latest.yml" && (cd "$T/b/1.0.1" && cp Tatana-Setup-1.0.1.exe Tatana-Setup-1.0.1.exe.blockmap tatana-update.json \
  Tatana-1.0.1-x86_64.AppImage latest-linux.yml tatana-update-linux.json SHA256SUMS "$T/b/dir/")
(cd "$T/b/dir" && tar -cf "$T/b/dir.tar" Tatana-Setup-1.0.1.exe Tatana-Setup-1.0.1.exe.blockmap latest.yml tatana-update.json \
  Tatana-1.0.1-x86_64.AppImage latest-linux.yml tatana-update-linux.json SHA256SUMS)
esperar "directorio en lugar de archivo" 4 "$(correr 'publicar 1.0.1' "$T/b/dir.tar")"
(cd "$T/b/1.0.1" && ln -sf /etc/passwd latest.yml.lnk && tar -cf "$T/b/link.tar" Tatana-Setup-1.0.1.exe Tatana-Setup-1.0.1.exe.blockmap \
  tatana-update.json Tatana-1.0.1-x86_64.AppImage latest-linux.yml tatana-update-linux.json SHA256SUMS \
  --transform 's/latest.yml.lnk/latest.yml/' latest.yml.lnk)
esperar "symlink en el tar" 4 "$(correr 'publicar 1.0.1' "$T/b/link.tar")"
(cd "$T/b/1.0.1" && tar -cf "$T/b/trav.tar" Tatana-Setup-1.0.1.exe Tatana-Setup-1.0.1.exe.blockmap latest.yml tatana-update.json \
  Tatana-1.0.1-x86_64.AppImage latest-linux.yml tatana-update-linux.json --transform 's|^SHA256SUMS$|../SHA256SUMS|' SHA256SUMS)
esperar "ruta con .." 4 "$(correr 'publicar 1.0.1' "$T/b/trav.tar")"
printf 'esto no es un tar' >"$T/b/basura.tar"
esperar "no es un tar" 4 "$(correr 'publicar 1.0.1' "$T/b/basura.tar")"
armar 1.0.1 roto-hash
esperar "hash que no coincide" 5 "$(correr 'publicar 1.0.1' "$(tar_de 1.0.1)")"
armar 1.0.1 roto-hash-linux
esperar "hash del AppImage que no coincide" 5 "$(correr 'publicar 1.0.1' "$(tar_de 1.0.1)")"
armar 1.0.1 roto-yml
esperar "latest.yml de otra versión" 5 "$(correr 'publicar 1.0.1' "$(tar_de 1.0.1)")"
armar 1.0.1 roto-yml-linux
esperar "latest-linux.yml de otra versión" 5 "$(correr 'publicar 1.0.1' "$(tar_de 1.0.1)")"
armar 1.0.1
esperar "tar de otra versión" 4 "$(correr 'publicar 1.0.2' "$(tar_de 1.0.1)")"
[[ "$(SSH_ORIGINAL_COMMAND=estado bash "$SCRIPT" --raiz "$RAIZ")" == 1.0.0 ]] && ok "los rechazos no cambiaron nada" || mal "un rechazo publicó algo"
cmp -s "$T/b/1.0.0/Tatana-Setup-1.0.0.exe" "$RAIZ/descargas/tatana/Tatana-Setup-Windows.exe" && ok "descargas intacto tras rechazos" || mal "descargas cambió"
cmp -s "$T/b/1.0.0/Tatana-1.0.0-x86_64.AppImage" "$RAIZ/descargas/tatana/Tatana-Linux-x86_64.AppImage" && ok "AppImage de descargas intacto tras rechazos" || mal "AppImage de descargas cambió"
grep -q 'Versión 1.0.0' "$INDEX" && ok "index.html intacto tras rechazos" || mal "index.html cambió"

for v in 1.0.1 1.0.2 1.1.0 1.2.0 1.10.0 2.0.0; do
  armar "$v"
  esperar "publicar $v" 0 "$(correr "publicar $v" "$(tar_de "$v")")"
done
quedan="$(find "$RAIZ/tatana-updates" -maxdepth 1 -name 'Tatana-Setup-*.exe' -printf '%f\n' | sed -E 's/Tatana-Setup-(.*)\.exe/\1/' | sort -V | tr '\n' ' ')"
[[ "$quedan" == "1.0.2 1.1.0 1.2.0 1.10.0 2.0.0 " ]] && ok "retención: 5 instaladores ($quedan)" || mal "retención: $quedan"
[[ ! -e "$RAIZ/tatana-updates/Tatana-Setup-1.0.0.exe.blockmap" ]] && ok "retención borra el .blockmap" || mal "blockmap viejo"
quedan_ai="$(find "$RAIZ/tatana-updates" -maxdepth 1 -name 'Tatana-*-x86_64.AppImage' -printf '%f\n' | sed -E 's/Tatana-(.*)-x86_64\.AppImage/\1/' | sort -V | tr '\n' ' ')"
[[ "$quedan_ai" == "1.0.2 1.1.0 1.2.0 1.10.0 2.0.0 " ]] && ok "retención: 5 AppImage ($quedan_ai)" || mal "retención de AppImage: $quedan_ai"
[[ "$(SSH_ORIGINAL_COMMAND=estado bash "$SCRIPT" --raiz "$RAIZ")" == 2.0.0 ]] && ok "1.10.0 < 2.0.0 (orden numérico)" || mal "orden de versiones"
grep -q 'Versión 2.0.0' "$INDEX" && ok "index.html con la última versión" || mal "index.html no se actualizó"
armar 1.9.0
esperar "1.9.0 < 2.0.0" 6 "$(correr 'publicar 1.9.0' "$(tar_de 1.9.0)")"
[[ -z "$(find "$RAIZ/tatana-staging" -mindepth 1 -not -name .lock)" ]] && ok "staging limpio" || mal "quedó basura en staging"
[[ -z "$(find "$RAIZ" -name '*.tmp')" ]] && ok "sin .tmp" || mal "quedaron .tmp"

echo
if ((fallos)); then
  echo "$fallos prueba(s) fallaron"
  exit 1
fi
echo "Todas las pruebas pasaron"
