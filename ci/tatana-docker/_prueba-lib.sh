# shellcheck shell=bash
# Funciones comunes de las pruebas de CI del actualizador de Tatana en Docker (SDD fiscalia-operacion §14.2):
# prueba-actualizador.sh (nivel 2) y prueba-2004.sh (nivel 3). Se cargan con `.`; corren como ROOT en una VM efímera
# de CI (runner ubuntu-24.04): instalan Caddy en el 443, tocan /etc/hosts, la CA del sistema y /opt|/etc|/var/lib.
# NUNCA en la Mac ni en una PC de trabajo (prueba_guardas lo impide).
#
# Variables que dejan: RAIZ, W (carpeta de trabajo, fuera de /tmp: el servicio usa PrivateTmp), LOGS, CLAVE_PRUEBA
# (key_id:spki de la clave efímera de la prueba), CANAL_DIR (lo que sirve Caddy en /tatana/docker/).

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OPS="$RAIZ/ops/tatana"
CADDY_IMG="$(sed -nE 's/^[[:space:]]*image:[[:space:]]*(caddy:[^[:space:]#]+).*/\1/p' "$RAIZ/deploy/fiscalia/servidor/compose.servidor.yml" | head -1)"
CONTENEDOR_CADDY=td-prueba-caddy
fallos=0
ok() { echo "ok    $*"; }
mal() { echo "FALLA $*"; fallos=$((fallos + 1)); }
chequear() { if eval "$2"; then ok "$1"; else mal "$1 [$2]"; fi; }
paso() { echo; echo "== $* =="; }

prueba_guardas() {
  [[ "$(id -u)" == 0 ]] || { echo "Correr como root (sudo), en la VM efímera de CI." >&2; exit 1; }
  [[ "$(uname -s)" == Linux && "$(uname -m)" == x86_64 ]] || { echo "Solo Linux x86_64 (CI)." >&2; exit 1; }
  [[ "${GITHUB_ACTIONS:-}" == true || "${FACTUM_PRUEBA_VM_DESCARTABLE:-0}" == 1 ]] ||
    { echo "Solo en CI: toca /etc/hosts, la CA del sistema, el puerto 443 y /opt|/etc|/var/lib. FACTUM_PRUEBA_VM_DESCARTABLE=1 en una VM descartable." >&2; exit 1; }
  W="$(mktemp -d /srv/td-prueba.XXXXXX)"
  chmod 755 "$W"
  LOGS="${SALIDA_LOGS:-$W/logs}"
  mkdir -p "$LOGS"
  CANAL_DIR="$W/srv-canal/docker"
  mkdir -p "$CANAL_DIR" "$W/caddy-logs" "$W/caddy-data"
}

# Servidor de la fiscalía de mentira: Caddy con el Caddyfile REAL (tls internal) en el 443, factum.internal → 127.0.0.1,
# CA instalada en el sistema con preparar-pc-perito.sh (como en una PC de perito).
servidor_arrancar() { # servidor_arrancar <usuario del perito>
  local u="$1" ca="$W/caddy-data/caddy/pki/authorities/local/root.crt"
  docker pull -q "$CADDY_IMG" >/dev/null
  docker rm -f "$CONTENEDOR_CADDY" >/dev/null 2>&1 || true
  docker run -d --name "$CONTENEDOR_CADDY" -p 443:443 \
    -v "$RAIZ/deploy/fiscalia/servidor/Caddyfile:/etc/caddy/Caddyfile:ro" -v "$W/srv-canal:/srv/canal-tatana:ro" \
    -v "$W/caddy-logs:/var/log/caddy" -v "$W/caddy-data:/data" "$CADDY_IMG" >/dev/null
  for _ in $(seq 1 30); do [[ -s "$ca" ]] && break; sleep 1; done
  [[ -s "$ca" ]] || { docker logs "$CONTENEDOR_CADDY" | tail -20; echo "ERROR: Caddy no generó su CA" >&2; exit 1; }
  # Caddy la deja en 600: la copia tiene que ser legible para el perito (certutil corre con su usuario).
  install -m 644 "$ca" "$W/ca-factum-fiscalia.crt"
  if ! bash "$RAIZ/deploy/fiscalia/perito/preparar-pc-perito.sh" 127.0.0.1 --ca "$W/ca-factum-fiscalia.crt" --usuario "$u" \
      >"$LOGS/preparar-pc-perito.log" 2>&1; then
    # Lo esperable acá es que falle solo su prueba final de /health (el Caddy de prueba no tiene backend: 502).
    echo "AVISO: preparar-pc-perito.sh terminó con error (ver $LOGS/preparar-pc-perito.log; si es solo la prueba de /health, es el Caddy de prueba sin backend); se asegura la CA a mano"
    install -m 644 "$W/ca-factum-fiscalia.crt" /usr/local/share/ca-certificates/factum-fiscalia.crt
    update-ca-certificates >/dev/null 2>&1 || true
    grep -q 'factum.internal' /etc/hosts || echo "127.0.0.1 factum.internal" >>/etc/hosts
  fi
  # TLS con la CA de Caddy y el nombre: un archivo que no existe tiene que dar 404 (no 000 = error de TLS/conexión).
  [[ "$(curl -sS -o /dev/null -w '%{http_code}' --cacert "$W/ca-factum-fiscalia.crt" https://factum.internal/tatana/docker/no-existe)" == 404 ]] ||
    { echo "ERROR: https://factum.internal no responde con la CA de Caddy" >&2; exit 1; }
}

servidor_limpiar() {
  docker rm -f "$CONTENEDOR_CADDY" >/dev/null 2>&1 || true
}

# Publica en el canal como lo hace publicar_canal_tatana del servidor: archivos, VERSION y el manifiesto al final.
publicar() { # publicar <carpeta con tatana-update-docker.json, imagen y kit>
  local f
  rm -f "$CANAL_DIR"/*
  for f in "$1"/*; do
    [[ "$(basename "$f")" == tatana-update-docker.json ]] && continue
    cp "$f" "$CANAL_DIR/"
  done
  cp "$1/tatana-update-docker.json" "$CANAL_DIR/tatana-update-docker.json"
  chmod 644 "$CANAL_DIR"/*
}

# Clave efímera de la prueba (el kit de la base y, a mano, el kit instalado confían en ella).
clave_prueba() {
  node "$OPS/generar-clave-firma.mjs" --key-id prueba-actualizador --salida "$W/clave-prueba.pem" --extra-salida "$W/clave-prueba.txt" >/dev/null
  CLAVE_PRUEBA="$(tr -d '[:space:]' <"$W/clave-prueba.txt")"
  rm -f "$W/clave-prueba.txt"
}
linea_clave_prueba() { printf '%s %s\n' "${CLAVE_PRUEBA%%:*}" "${CLAVE_PRUEBA#*:}"; }

# Firma un payload con la clave de la prueba con las herramientas de ops/tatana (valida el payload).
firmar() { # firmar <payload.json> <salida>
  TATANA_FIRMA_CLAVE_PRIVADA="$(cat "$W/clave-prueba.pem")" node "$OPS/firmar-manifiesto.mjs" --payload "$1" \
    --key-id "${CLAVE_PRUEBA%%:*}" --clave-extra "$CLAVE_PRUEBA" --salida "$2" >/dev/null
}

# Firma SIN validar (para manifiestos con contenido inválido pero firma válida: producto de otro SO, etc.).
firmar_crudo() { # firmar_crudo <payload.json> <salida> [campo=valor ...]
  local p="$1" out="$2"
  shift 2
  node -e '
    const fs = require("fs"); const crypto = require("crypto")
    const [pl, pem, keyId, out, ...cambios] = process.argv.slice(1)
    const p = JSON.parse(fs.readFileSync(pl, "utf8"))
    for (const c of cambios) { const i = c.indexOf("="); p[c.slice(0, i)] = c.slice(i + 1) }
    const b = Buffer.from(JSON.stringify(p))
    const sig = crypto.sign(null, b, crypto.createPrivateKey(fs.readFileSync(pem, "utf8")))
    fs.writeFileSync(out, JSON.stringify({ schema: 1, key_id: keyId, payload: b.toString("base64"), signature: sig.toString("base64") }) + "\n")
  ' "$p" "$W/clave-prueba.pem" "${CLAVE_PRUEBA%%:*}" "$out" "$@"
}

# Imagen factum-tatana-docker:<v> = otra imagen con la etiqueta de versión cambiada (su /health sigue diciendo la versión
# del agente que lleva adentro: así se arma "una versión que no arranca").
imagen_reetiquetada() { # imagen_reetiquetada <imagen base> <versión>
  printf 'FROM %s\nLABEL org.opencontainers.image.version="%s"\n' "$1" "$2" | docker build -q -t "factum-tatana-docker:$2" - >/dev/null
}

# Carpeta del paquete (kit + imagen) para la versión <v> a partir de la carpeta del kit real: VERSION, IMAGEN.txt,
# imagen y claves (con la de la prueba) de esa versión; SHA256SUMS de nuevo.
carpeta_version() { # carpeta_version <carpeta kit real> <versión> <destino padre> [con_imagen=1]
  local src="$1" v="$2" d="$3/Tatana-Docker-v$2" f
  rm -rf "$d"
  mkdir -p "$d"
  (cd "$src" && tar -cf - --exclude 'imagen-tatana-docker-*.tar.gz' .) | tar -xf - -C "$d"
  echo "$v" >"$d/VERSION"
  echo "factum-tatana-docker:$v" >"$d/IMAGEN.txt"
  grep -qF "${CLAVE_PRUEBA#*:}" "$d/claves-confiables.txt" || linea_clave_prueba >>"$d/claves-confiables.txt"
  if [[ "${4:-1}" == 1 ]]; then
    docker save "factum-tatana-docker:$v" | gzip -1 >"$d/imagen-tatana-docker-v$v.tar.gz"
  fi
  (cd "$d" && find . -type f ! -name SHA256SUMS | sed 's|^\./||' | LC_ALL=C sort | while IFS= read -r f; do
    printf '%s  %s\n' "$(sha256sum "$f" | cut -d' ' -f1)" "$f"; done >SHA256SUMS)
  echo "$d"
}

# Canal firmado con la clave de la prueba para una carpeta_version: imagen, kit y manifiesto.
canal_de_carpeta() { # canal_de_carpeta <carpeta Tatana-Docker-vV> <versión> <destino>
  local d="$1" v="$2" c="$3"
  rm -rf "$c"
  mkdir -p "$c"
  cp "$d/imagen-tatana-docker-v$v.tar.gz" "$c/"
  tar -C "$(dirname "$d")" --exclude "Tatana-Docker-v$v/imagen-tatana-docker-v$v.tar.gz" -czf "$c/kit-tatana-docker-v$v.tar.gz" "Tatana-Docker-v$v"
  node "$OPS/armar-payload.mjs" --producto tatana-docker --archivo "$c/imagen-tatana-docker-v$v.tar.gz" \
    --kit "$c/kit-tatana-docker-v$v.tar.gz" --imagen "factum-tatana-docker:$v" --version "$v" \
    --commit "$(printf 'c%.0s' {1..40})" --salida "$c/payload.json" >/dev/null
  firmar "$c/payload.json" "$c/tatana-update-docker.json"
  rm -f "$c/payload.json"
  echo "$v" >"$c/VERSION"
}

# Lectura del estado del actualizador (§10.7).
estado() { grep "^$1=" /var/lib/factum-tatana-docker/estado.env 2>/dev/null | head -1 | cut -d= -f2-; }
imagenes_tatana() { docker images --format '{{.Repository}}:{{.Tag}}' | grep '^factum-tatana-docker:' | LC_ALL=C sort | tr '\n' ' '; }
id_contenedor() { docker inspect -f '{{.Id}}' tatana-docker 2>/dev/null; }
# La PRIMERA clave "version" de /health es la del agente (las herramientas también traen "version" más adelante).
version_health() { docker exec tatana-docker curl -fsS --max-time 5 http://127.0.0.1:8765/health 2>/dev/null | grep -o '"version":"[^"]*"' | head -1 | cut -d'"' -f4; }
# Lo que tiene que quedar igual del contenedor después de un cambio: montajes, uid/gid, puerto, red, reinicio, argumentos.
forma_contenedor() {
  {
    docker inspect -f '{{range .Mounts}}montaje {{.Source}} -> {{.Destination}} rw={{.RW}}{{"\n"}}{{end}}' tatana-docker
    docker inspect -f '{{range .Config.Env}}env {{.}}{{"\n"}}{{end}}' tatana-docker | grep -E '^env (TATANA_UID|TATANA_GID|TATANA_GIDS|TATANA_PUERTO|TATANA_USBMUXD|HOME|TZ|Agent__EvidenceDirectory)='
    docker inspect -f 'red {{.HostConfig.NetworkMode}}{{"\n"}}reinicio {{.HostConfig.RestartPolicy.Name}}{{"\n"}}args {{json .Args}}{{"\n"}}etiqueta {{index .Config.Labels "factum.tatana-docker"}}' tatana-docker
  } 2>/dev/null | grep -v '^$' | LC_ALL=C sort
}
