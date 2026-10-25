#!/usr/bin/env bash
# Prueba cruzada obligatoria del manifiesto de Tatana en Docker (SDD fiscalia-operacion §10.1): un
# tatana-update-docker.json firmado con las herramientas de Node (ops/tatana) verifica en el Python de la PC
# (verificar-manifiesto-docker.py), y uno alterado falla en los DOS con el mismo motivo. Clave efímera, sin red ni
# privilegios; todo en una carpeta temporal propia que se borra al final.
#
#   bash ci/tatana-docker/prueba-cruzada.sh [--imagen <imagen de Tatana>]
#
# Sin --imagen usa el python3 del host (necesita el paquete cryptography). Con --imagen, el Python de la imagen
# (/opt/tatana/tools/python-embed/bin/python3), con --network none: es el que corre en la PC.
set -euo pipefail
RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
IMAGEN=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --imagen) IMAGEN="$2"; shift 2 ;;
    *) echo "uso: $0 [--imagen <imagen>]" >&2; exit 2 ;;
  esac
done
T="$(mktemp -d "${TMPDIR:-/tmp}/prueba-cruzada.XXXXXX")"
trap 'rm -rf "$T"' EXIT
OPS="$RAIZ/ops/tatana"
fallos=0

py() { # py <manifiesto en $T> → salida CLAVE=valor del verificador de la PC
  if [[ -n "$IMAGEN" ]]; then
    docker run --rm --platform linux/amd64 --network none -v "$T:/t:ro" -v "$RAIZ/deploy/tatana-docker/paquete:/k:ro" \
      --entrypoint /opt/tatana/tools/python-embed/bin/python3 "$IMAGEN" -I /k/verificar-manifiesto-docker.py \
      --manifiesto "/t/$1" --claves /t/claves.txt --version-instalada 0.0.0-docker-prueba || true
  else
    python3 -I "$RAIZ/deploy/tatana-docker/paquete/verificar-manifiesto-docker.py" --manifiesto "$T/$1" --claves "$T/claves.txt" \
      --version-instalada 0.0.0-docker-prueba || true
  fi
}
nodo() { # nodo <manifiesto en $T> → "OK" o el motivo de verificar-manifiesto.mjs
  local out
  if out="$(node "$OPS/verificar-manifiesto.mjs" --manifiesto "$T/$1" --producto tatana-docker --clave-extra "$EXTRA" 2>&1)"; then echo OK
  else sed -nE 's/^ERROR: ([a-z_]+):.*/\1/p' <<<"$out"; fi
}
caso() { # caso <descripción> <manifiesto> <motivo esperado | OK>
  local n p
  n="$(nodo "$2")"
  p="$(py "$2")"
  if [[ "$3" == OK ]]; then
    if [[ "$n" == OK ]] && grep -q '^VERSION=1.5.0$' <<<"$p" && grep -q '^COMPARACION=mayor$' <<<"$p"; then echo "ok    $1: válido en Node y en Python"
    else echo "FALLA $1: Node=$n Python=$(tr '\n' ' ' <<<"$p")"; fallos=$((fallos + 1)); fi
  else
    local mp
    mp="$(sed -n 's/^MOTIVO=//p' <<<"$p")"
    if [[ "$n" == "$3" && "$mp" == "$3" ]]; then echo "ok    $1: $3 en Node y en Python"
    else echo "FALLA $1: esperado $3, Node=$n Python=${mp:-$(tr '\n' ' ' <<<"$p")}"; fallos=$((fallos + 1)); fi
  fi
}

node "$OPS/generar-clave-firma.mjs" --key-id cruzada-1 --salida "$T/clave.pem" --extra-salida "$T/extra.txt" >/dev/null
node "$OPS/generar-clave-firma.mjs" --key-id cruzada-2 --salida "$T/otra.pem" --extra-salida "$T/otra.txt" >/dev/null
EXTRA="$(tr -d '[:space:]' <"$T/extra.txt")"
OTRA="$(tr -d '[:space:]' <"$T/otra.txt")"
printf '# claves de la prueba\n%s %s\n' "${EXTRA%%:*}" "${EXTRA#*:}" >"$T/claves.txt"
head -c 200000 /dev/urandom >"$T/imagen-tatana-docker-v1.5.0.tar.gz"
head -c 3000 /dev/urandom >"$T/kit-tatana-docker-v1.5.0.tar.gz"
node "$OPS/armar-payload.mjs" --producto tatana-docker --archivo "$T/imagen-tatana-docker-v1.5.0.tar.gz" \
  --kit "$T/kit-tatana-docker-v1.5.0.tar.gz" --imagen factum-tatana-docker:1.5.0 --version 1.5.0 --commit "$(printf 'a%.0s' {1..40})" \
  --notas "Prueba cruzada — acentos y ñ" --salida "$T/payload.json" >/dev/null
firmar() { # firmar <payload> <salida> [pem] [extra]
  TATANA_FIRMA_CLAVE_PRIVADA="$(cat "${3:-$T/clave.pem}")" node "$OPS/firmar-manifiesto.mjs" --payload "$1" \
    --key-id "$(cut -d: -f1 <<<"${4:-$EXTRA}")" --clave-extra "${4:-$EXTRA}" --salida "$2" >/dev/null
}
firmar "$T/payload.json" "$T/ok.json"
caso "firmado con Node" ok.json OK

# Payload alterado (un byte), firma alterada, key_id que no es de confianza, JSON roto.
node -e '
  const fs = require("fs"); const [src, dir] = process.argv.slice(1)
  const e = JSON.parse(fs.readFileSync(src, "utf8"))
  const p = Buffer.from(e.payload, "base64"); p[p.indexOf("1.5.0")] = 0x39
  fs.writeFileSync(dir + "/payload-alterado.json", JSON.stringify({ ...e, payload: p.toString("base64") }))
  const s = Buffer.from(e.signature, "base64"); s[5] ^= 1
  fs.writeFileSync(dir + "/firma-alterada.json", JSON.stringify({ ...e, signature: s.toString("base64") }))
  fs.writeFileSync(dir + "/clave-desconocida.json", JSON.stringify({ ...e, key_id: "no-esta" }))
  fs.writeFileSync(dir + "/roto.json", "{ roto")
' "$T/ok.json" "$T"
caso "payload alterado" payload-alterado.json firma_invalida
caso "firma alterada" firma-alterada.json firma_invalida
caso "key_id desconocido" clave-desconocida.json clave_desconocida
caso "JSON roto" roto.json manifiesto_invalido
# Firmado con otra clave que se presenta con el key_id de la buena.
TATANA_FIRMA_CLAVE_PRIVADA="$(cat "$T/otra.pem")" node "$OPS/firmar-manifiesto.mjs" --payload "$T/payload.json" \
  --key-id cruzada-2 --clave-extra "$OTRA" --salida "$T/otra-clave.json" >/dev/null
node -e 'const fs=require("fs");const e=JSON.parse(fs.readFileSync(process.argv[1],"utf8"));fs.writeFileSync(process.argv[2],JSON.stringify({...e,key_id:"cruzada-1"}))' \
  "$T/otra-clave.json" "$T/otra-clave-mismo-id.json"
caso "otra clave con el key_id de confianza" otra-clave-mismo-id.json firma_invalida
# Payloads con contenido inválido pero firma válida (se firman a mano: firmar-manifiesto.mjs se negaría).
for variante in linux imagen kit; do
  node -e '
    const fs = require("fs"); const crypto = require("crypto")
    const [pl, pem, keyId, variante, out] = process.argv.slice(1)
    const p = JSON.parse(fs.readFileSync(pl, "utf8"))
    if (variante === "linux") p.product = "tatana-linux"
    if (variante === "imagen") p.image = "factum-tatana-docker:1.4.0"
    if (variante === "kit") p.kit = { ...p.kit, file: "kit.tar.gz" }
    const b = Buffer.from(JSON.stringify(p))
    const sig = crypto.sign(null, b, crypto.createPrivateKey(fs.readFileSync(pem, "utf8")))
    fs.writeFileSync(out, JSON.stringify({ schema: 1, key_id: keyId, payload: b.toString("base64"), signature: sig.toString("base64") }) + "\n")
  ' "$T/payload.json" "$T/clave.pem" cruzada-1 "$variante" "$T/variante-$variante.json"
done
caso "producto tatana-linux firmado" variante-linux.json manifiesto_invalido
caso "image con otro tag firmado" variante-imagen.json manifiesto_invalido
caso "kit.file raro firmado" variante-kit.json manifiesto_invalido

echo
if ((fallos)); then echo "$fallos caso(s) fallaron"; exit 1; fi
echo "Prueba cruzada Node ↔ Python: todos los casos coinciden"
