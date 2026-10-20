#!/usr/bin/env bash
# Factum en la fiscalía — PLAN A: prepara la PC Linux de un perito para usar el servidor de la fiscalía.
# Se corre DESDE EL PAQUETE (con el ca-factum-fiscalia.crt que exportó instalar-servidor.sh al lado):
#
#   sudo bash perito/preparar-pc-perito.sh <IP-del-servidor> [--ca <ruta .crt>] [--usuario <usuario>]
#   bash perito/preparar-pc-perito.sh <IP-del-servidor> --simular      (solo muestra lo que haría)
#
# Hace (todo idempotente; se puede volver a correr, p. ej. si cambia la IP del servidor):
#   1. /etc/hosts: "<IP> factum.internal" (reemplaza una línea anterior de factum.internal)
#   2. Instala la CA de Factum en el sistema, en Chrome/Chromium (~/.pki/nssdb) y en Firefox
#      (perfiles + /etc/firefox/policies/policies.json)
#   3. Tatana: ~/.config/Tatana/appsettings.Local.json con Agent:AllowedOrigins = localhost + https://factum.internal
#      (sin pisar otras claves) y, si el paquete trae tatana/*.AppImage, lo copia a ~/Applications/Tatana.AppImage
#   4. Prueba https://factum.internal/health
# El usuario por defecto es el que llamó a sudo (SUDO_USER).
set -euo pipefail
DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deploy/fiscalia/comun/_comun.sh
source "$DIR_SCRIPT/../comun/_comun.sh"
PAQ="$(cd "$DIR_SCRIPT/.." && pwd)"

IP=""
CA=""
SIMULAR=0
FACTUM_USUARIO=""
HOSTS_F="${FACTUM_HOSTS_PRUEBA:-/etc/hosts}" # solo para probar el script sin tocar el /etc/hosts real
uso() {
  sed -n '2,17p' "$0" | sed 's/^# \{0,1\}//' >&2
  exit 2
}
while [[ $# -gt 0 ]]; do
  case "$1" in
    --ca) CA="${2:-}"; shift 2 ;;
    --usuario) FACTUM_USUARIO="${2:-}"; shift 2 ;;
    --simular) SIMULAR=1; shift ;;
    -h | --help) uso ;;
    -*) echo "Argumento desconocido: $1" >&2; uso ;;
    *) [[ -z "$IP" ]] || uso; IP="$1"; shift ;;
  esac
done
[[ -n "$IP" ]] || uso
if [[ ! "$IP" =~ ^([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})$ ]]; then
  morir "'$IP' no es una IP (tiene que ser como 192.168.0.10)." "La muestra instalar-servidor.sh al terminar, o diagnostico.sh en el servidor."
fi
for o in 1 2 3 4; do ((BASH_REMATCH[o] <= 255)) || morir "'$IP' no es una IP válida."; done

hacer() { # hacer <descripción> <comando...>
  local d="$1"
  shift
  if ((SIMULAR)); then echo "  [simular] $d"; else "$@"; fi
}

echo ""
echo "=== Preparar esta PC de perito para Factum (servidor $IP) ==="
((SIMULAR)) && echo "  Modo --simular: no se cambia nada."
if [[ "$(id -u)" != 0 && $SIMULAR == 0 ]]; then
  morir "hace falta sudo (para /etc/hosts y el certificado)." "Corré: sudo bash $0 $IP"
fi
U="$(usuario_real)"
HU="$(home_de "$U")"
[[ -d "$HU" ]] || morir "no encuentro la carpeta personal de '$U' ($HU)." "Indicá el usuario del perito con --usuario <usuario>."
info "Usuario del perito: $U ($HU)"

# ── 1. Nombre ───────────────────────────────────────────────────────────────────────────────
paso 1 4 "Nombre factum.internal → $IP"
if ((SIMULAR)); then
  echo "  [simular] dejar en $HOSTS_F una sola línea: $IP $FACTUM_NOMBRE_RED"
  grep -E "[[:space:]]$FACTUM_NOMBRE_RED([[:space:]]|\$)" "$HOSTS_F" 2>/dev/null | sed 's/^/  [simular]   (se reemplaza) /' || true
else
  hosts_fijar "$IP" "$HOSTS_F"
  ok "$HOSTS_F: $IP $FACTUM_NOMBRE_RED"
fi

# ── 2. Certificado ──────────────────────────────────────────────────────────────────────────
paso 2 4 "Certificado de Factum (CA interna del servidor)"
[[ -n "$CA" ]] || CA="$PAQ/$FACTUM_CA_ARCHIVO"
[[ -f "$CA" ]] || morir "no encuentro $CA." "Copiá al pendrive el $FACTUM_CA_ARCHIVO que dejó instalar-servidor.sh (también está en /srv/factum-fiscalia/ del servidor) o indicalo con --ca <ruta>."
if command -v openssl >/dev/null 2>&1; then
  openssl x509 -in "$CA" -noout >/dev/null 2>&1 || morir "$CA no es un certificado válido."
  info "$(openssl x509 -in "$CA" -noout -subject 2>/dev/null)"
  info "Huella SHA-256: $(openssl x509 -in "$CA" -noout -fingerprint -sha256 2>/dev/null | cut -d= -f2)"
  info "(tiene que coincidir con la que mostró el instalador del servidor)"
fi
CA_SISTEMA=""
if [[ -d /usr/local/share/ca-certificates ]] || command -v update-ca-certificates >/dev/null 2>&1; then
  CA_SISTEMA="/usr/local/share/ca-certificates/factum-fiscalia.crt"
  hacer "copiar $CA a $CA_SISTEMA y correr update-ca-certificates" \
    sh -c "install -m 644 '$CA' '$CA_SISTEMA' && update-ca-certificates >/dev/null"
  ok "Sistema (Debian/Ubuntu/Mint): $CA_SISTEMA"
elif command -v update-ca-trust >/dev/null 2>&1; then
  CA_SISTEMA="/etc/pki/ca-trust/source/anchors/factum-fiscalia.crt"
  hacer "copiar $CA a $CA_SISTEMA y correr update-ca-trust" \
    sh -c "install -m 644 '$CA' '$CA_SISTEMA' && update-ca-trust extract"
  ok "Sistema (Fedora/RHEL): $CA_SISTEMA"
elif command -v trust >/dev/null 2>&1; then
  CA_SISTEMA="$CA"
  hacer "trust anchor --store $CA" trust anchor --store "$CA"
  ok "Sistema (trust anchor)."
else
  aviso "No encontré cómo instalar certificados en este sistema: solo se instala en los navegadores."
fi

# Navegadores: Chrome/Chromium leen ~/.pki/nssdb; Firefox, su perfil (o la política del sistema).
NSS_OK=0
if command -v certutil >/dev/null 2>&1; then
  bases=("$HU/.pki/nssdb")
  for perfil in "$HU"/.mozilla/firefox/*/ "$HU"/snap/firefox/common/.mozilla/firefox/*/ \
    "$HU"/.var/app/org.mozilla.firefox/.mozilla/firefox/*/ "$HU"/snap/chromium/current/.pki/nssdb/; do
    [[ -f "$perfil/cert9.db" ]] && bases+=("${perfil%/}")
  done
  for db in "${bases[@]}"; do
    if [[ ! -f "$db/cert9.db" ]]; then
      hacer "crear la base NSS $db" como_usuario "$U" sh -c "mkdir -p '$db' && certutil -N -d 'sql:$db' --empty-password"
    fi
    hacer "importar la CA en $db" como_usuario "$U" sh -c \
      "certutil -D -d 'sql:$db' -n 'Factum Fiscalia' >/dev/null 2>&1; certutil -A -d 'sql:$db' -n 'Factum Fiscalia' -t 'C,,' -i '$CA'"
    ok "NSS: $db"
  done
  NSS_OK=1
else
  aviso "Falta 'certutil' (paquete libnss3-tools en Ubuntu/Debian, nss-tools en Fedora): Chrome no va a confiar solo."
  info "Con internet: sudo apt install libnss3-tools   y volvé a correr este script."
  info "Sin internet: importalo a mano en el navegador (guía, \"Certificado no confiable\")."
fi
if [[ -n "$CA_SISTEMA" && -f "$CA_SISTEMA" || $SIMULAR == 1 ]]; then
  POL_DIR="/etc/firefox/policies"
  POL="$POL_DIR/policies.json"
  CA_FF="$POL_DIR/factum-fiscalia.crt"
  if command -v python3 >/dev/null 2>&1; then
    hacer "agregar $CA_FF a Certificates.Install de $POL (sin pisar otras políticas)" sh -c \
      "mkdir -p '$POL_DIR' && install -m 644 '$CA' '$CA_FF' && python3 -I - '$POL' '$CA_FF'" <<'PY'
import json, os, sys
ruta, crt = sys.argv[1], sys.argv[2]
datos = {}
if os.path.exists(ruta):
    with open(ruta, encoding="utf-8") as f:
        datos = json.load(f)
pol = datos.setdefault("policies", {})
cert = pol.setdefault("Certificates", {})
inst = cert.setdefault("Install", [])
if crt not in inst:
    inst.append(crt)
tmp = ruta + ".nuevo"
with open(tmp, "w", encoding="utf-8") as f:
    json.dump(datos, f, indent=2, ensure_ascii=False)
os.chmod(tmp, 0o644)
os.replace(tmp, ruta)
PY
    ok "Firefox: política del sistema ($POL)."
  else
    aviso "Sin python3 no se escribe la política de Firefox; si Firefox no confía, importalo a mano."
  fi
fi
((NSS_OK)) && info "Cerrá y volvé a abrir el navegador para que tome el certificado."

# ── 3. Tatana ───────────────────────────────────────────────────────────────────────────────
paso 3 4 "Tatana (agente de captura por USB)"
CONF_DIR="$HU/.config/Tatana"
CONF="$CONF_DIR/appsettings.Local.json"
command -v python3 >/dev/null 2>&1 || morir "falta python3 para escribir $CONF." "Instalalo (sudo apt install python3) o escribí el archivo a mano (guía, \"Tatana no conecta\")."
if ((SIMULAR)); then
  echo "  [simular] escribir/actualizar $CONF con Agent.AllowedOrigins = http://localhost:3000, http://127.0.0.1:3000, https://$FACTUM_NOMBRE_RED (+ los que ya tenga)"
else
  como_usuario "$U" mkdir -p "$CONF_DIR"
  como_usuario "$U" python3 -I - "$CONF" "https://$FACTUM_NOMBRE_RED" <<'PY'
# En Tatana, Agent:AllowedOrigins REEMPLAZA a los defaults (OriginPolicy.cs): se dejan siempre los
# localhost (plan B en la misma PC) + el servidor + los que ya hubiera. Las otras claves no se tocan.
import json, os, shutil, sys
ruta, origen = sys.argv[1], sys.argv[2]
datos = {}
if os.path.exists(ruta) and os.path.getsize(ruta) > 0:
    with open(ruta, encoding="utf-8-sig") as f:
        datos = json.load(f)
    if not os.path.exists(ruta + ".antes-de-factum-fiscalia"):  # la copia es la del ORIGINAL, no la de otra corrida
        shutil.copy2(ruta, ruta + ".antes-de-factum-fiscalia")
agente = datos.setdefault("Agent", {})
lista = []
for o in ["http://localhost:3000", "http://127.0.0.1:3000"] + list(agente.get("AllowedOrigins") or []) + [origen]:
    o = (o or "").strip().lower().rstrip("/")
    if o and o not in lista:
        lista.append(o)
agente["AllowedOrigins"] = lista
tmp = ruta + ".nuevo"
with open(tmp, "w", encoding="utf-8") as f:
    json.dump(datos, f, indent=2, ensure_ascii=False)
    f.write("\n")
os.replace(tmp, ruta)
print("      Agent.AllowedOrigins = " + ", ".join(lista))
PY
  ok "$CONF"
fi
APPIMAGE="$(find "$PAQ/tatana" -maxdepth 1 -name '*.AppImage' -type f 2>/dev/null | head -1 || true)"
if [[ -n "$APPIMAGE" ]]; then
  hacer "copiar $(basename "$APPIMAGE") a $HU/Applications/Tatana.AppImage" como_usuario "$U" sh -c \
    "mkdir -p '$HU/Applications' && cp '$APPIMAGE' '$HU/Applications/Tatana.AppImage.nuevo' && chmod 755 '$HU/Applications/Tatana.AppImage.nuevo' && mv -f '$HU/Applications/Tatana.AppImage.nuevo' '$HU/Applications/Tatana.AppImage'"
  ok "Tatana en $HU/Applications/Tatana.AppImage"
else
  aviso "El paquete no trae Tatana (tatana/*.AppImage). Copialo a ~/Applications/Tatana.AppImage a mano."
fi
info "Si Tatana ya estaba abierto, cerralo desde la bandeja y abrilo de nuevo (lee la config al arrancar)."
info "La primera vez: abrir Tatana y tocar \"Preparar esta PC\" (instala usbmuxd; necesita internet)."

# ── 4. Prueba ───────────────────────────────────────────────────────────────────────────────
paso 4 4 "Probando https://$FACTUM_NOMBRE_RED"
if ((SIMULAR)); then
  echo "  [simular] curl --cacert $CA https://$FACTUM_NOMBRE_RED/health"
elif ! command -v curl >/dev/null 2>&1; then
  aviso "No hay curl: probá abriendo https://$FACTUM_NOMBRE_RED en el navegador."
elif curl -fsS -m 10 --cacert "$CA" "https://$FACTUM_NOMBRE_RED/health" >/dev/null 2>&1; then
  ok "El servidor responde y el certificado es el de Factum."
  if curl -fsS -m 10 "https://$FACTUM_NOMBRE_RED/health" >/dev/null 2>&1; then
    ok "El sistema ya confía en el certificado."
  else
    aviso "El sistema todavía no confía en el certificado (ver arriba)."
  fi
else
  falla "https://$FACTUM_NOMBRE_RED no responde desde esta PC."
  info "¿La IP $IP es la del servidor? ¿Está en la misma red? Probá: ping $IP   y   curl -vk --resolve $FACTUM_NOMBRE_RED:443:$IP https://$FACTUM_NOMBRE_RED/health"
  info "Guía: \"factum.internal no resuelve\" y \"firewall\"."
  exit 1
fi
echo ""
echo "  Listo. Abrí el navegador en  https://$FACTUM_NOMBRE_RED  y entrá con tu DNI."
