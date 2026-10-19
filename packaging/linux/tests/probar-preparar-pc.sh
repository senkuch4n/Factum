#!/usr/bin/env bash
# Pruebas de packaging/linux/preparar-pc.sh sin root y sin tocar el sistema (SDD tatana-linux §6.2, §7).
#
#   bash packaging/linux/tests/probar-preparar-pc.sh
#
# Arma una raíz falsa por distro (etc/os-release, binarios falsos de apt-cache/dnf/ufw/firewall-cmd/apparmor_parser,
# el sysctl de AppArmor y las reglas uaccess), corre `--simular` con TATANA_RAIZ_PRUEBA=<raíz> y compara el
# TATANA_PLAN con el plan esperado. También prueba los códigos de salida de argumentos y de "sin root".
# Lo corre .github/workflows/verificar.yml. Requisitos: bash y python3.
set -euo pipefail

AQUI="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT="$AQUI/../preparar-pc.sh"
TMP="$(mktemp -d "${TMPDIR:-/tmp}/probar-preparar-pc.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT
FALLAS=0

ok() { echo "OK  $*"; }
falla() { echo "FALLA  $*"; FALLAS=$((FALLAS + 1)); }

# Raíz falsa: raiz <nombre> → imprime la ruta.
raiz() {
  local d="$TMP/$1"
  mkdir -p "$d/etc" "$d/usr/bin" "$d/usr/sbin" "$d/usr/lib/udev/rules.d" "$d/lib/udev/rules.d" "$d/proc/sys/kernel"
  echo "$d"
}

# Binario falso que imprime $3 (y sale con 0): falso <raíz> <nombre> <salida>
falso() {
  printf '#!/bin/sh\ncat <<'"'"'FIN'"'"'\n%s\nFIN\n' "$3" > "$1/usr/bin/$2"
  chmod 755 "$1/usr/bin/$2"
}

vacio() { : > "$1/usr/bin/$2"; chmod 755 "$1/usr/bin/$2"; }

uaccess_con_video() { echo 'SUBSYSTEM=="video4linux", TAG+="uaccess"' > "$1/$2/70-uaccess.rules"; }

# Corre --simular sobre la raíz y compara el plan con el JSON esperado (solo las claves dadas).
# esperar <nombre> <raíz> <código esperado> <json esperado>
esperar() {
  local nombre="$1" r="$2" codigo_esperado="$3" esperado="$4" salida codigo
  set +e
  salida="$(TATANA_RAIZ_PRUEBA="$r" bash "$SCRIPT" --simular </dev/null 2>&1)"
  codigo=$?
  set -e
  if [[ "$codigo" != "$codigo_esperado" ]]; then
    falla "$nombre: salió con $codigo (se esperaba $codigo_esperado). Salida:"; echo "$salida" | sed 's/^/    /'
    return
  fi
  if comparar "$esperado" <<<"$salida"; then
    ok "$nombre"
  else
    falla "$nombre"
    echo "$salida" | sed 's/^/    /'
  fi
}

COMPARAR='
import json, sys
esperado = json.loads(sys.argv[1])
lineas = [l for l in sys.stdin.read().splitlines() if l.startswith("TATANA_")]
planes = [l for l in lineas if l.startswith("TATANA_PLAN ")]
errores = []
for l in lineas:
    prefijo, _, cuerpo = l.partition(" ")
    try:
        json.loads(cuerpo)
    except ValueError as e:
        errores.append("%s no es JSON válido: %s" % (prefijo, e))
if len(planes) != 1:
    errores.append("se esperaba exactamente una línea TATANA_PLAN y hay %d" % len(planes))
else:
    plan = json.loads(planes[0][len("TATANA_PLAN "):])
    for k in ("schema", "soportada", "distro_id", "distro_version", "distro_name", "gestor", "paquetes", "uxplay",
              "archivos", "servicios", "firewall", "grupos", "comandos"):
        if k not in plan:
            errores.append("falta la clave %s" % k)
    for k, v in esperado.items():
        if k == "comandos_contienen":
            texto = "\n".join(plan.get("comandos", []))
            for frag in v:
                if frag not in texto:
                    errores.append("comandos no contiene %r" % frag)
        elif k == "comandos_no_contienen":
            texto = "\n".join(plan.get("comandos", []))
            for frag in v:
                if frag in texto:
                    errores.append("comandos contiene %r y no debería" % frag)
        elif plan.get(k) != v:
            errores.append("%s = %r (se esperaba %r)" % (k, plan.get(k), v))
for e in errores:
    print("    " + e)
sys.exit(1 if errores else 0)
'
comparar() { python3 -c "$COMPARAR" "$1"; }

[[ -f "$SCRIPT" ]] || { echo "FALLA  no existe $SCRIPT"; exit 1; }
bash -n "$SCRIPT" && ok "bash -n preparar-pc.sh"

# ── Ubuntu 22.04: apt, uxplay 1.46 (no sirve), sin AppArmor de userns, ufw instalado pero apagado ──────────────
R="$(raiz ubuntu-2204)"
cat > "$R/etc/os-release" <<'EOF'
PRETTY_NAME="Ubuntu 22.04.5 LTS"
NAME="Ubuntu"
VERSION_ID="22.04"
ID=ubuntu
ID_LIKE=debian
EOF
vacio "$R" apt-get
falso "$R" apt-cache "uxplay:
  Installed: (none)
  Candidate: 1.46-1
  Version table:"
vacio "$R" ufw
mkdir -p "$R/etc/ufw" && echo "ENABLED=no" > "$R/etc/ufw/ufw.conf"
uaccess_con_video "$R" lib/udev/rules.d
esperar "Ubuntu 22.04" "$R" 0 '{"schema":1,"soportada":true,"distro_id":"ubuntu","distro_version":"22.04",
  "distro_name":"Ubuntu 22.04.5 LTS","gestor":"apt","paquetes":["usbmuxd","avahi-daemon"],
  "uxplay":{"candidata":"1.46-1","instalar":false},"archivos":["/etc/udev/rules.d/70-factum-tatana.rules"],
  "servicios":["avahi-daemon"],"firewall":"inactivo","grupos":[],
  "comandos_contienen":["apt-get -o DPkg::Lock::Timeout=120 update",
    "apt-get -o DPkg::Lock::Timeout=120 install -y --no-install-recommends usbmuxd avahi-daemon",
    "ENV{ID_USB_INTERFACES}==\"*:ff4201:*\", TAG+=\"uaccess\"","systemctl enable --now avahi-daemon"],
  "comandos_no_contienen":["apparmor_parser","usermod","ufw allow"]}'

# ── Ubuntu 24.04: AppArmor restringe userns (perfil), ufw activo, uxplay 1.68 ───────────────────────────────
R="$(raiz ubuntu-2404)"
cat > "$R/etc/os-release" <<'EOF'
PRETTY_NAME="Ubuntu 24.04.1 LTS"
VERSION_ID="24.04"
ID=ubuntu
ID_LIKE=debian
EOF
vacio "$R" apt-get
falso "$R" apt-cache "uxplay:
  Installed: (none)
  Candidate: 1.68.2-1build2
  Version table:"
vacio "$R" ufw
mkdir -p "$R/etc/ufw" && printf '# comentario\nENABLED=yes\n' > "$R/etc/ufw/ufw.conf"
echo 1 > "$R/proc/sys/kernel/apparmor_restrict_unprivileged_userns"
: > "$R/usr/sbin/apparmor_parser"; chmod 755 "$R/usr/sbin/apparmor_parser"
uaccess_con_video "$R" usr/lib/udev/rules.d
esperar "Ubuntu 24.04" "$R" 0 '{"soportada":true,"distro_id":"ubuntu","distro_version":"24.04",
  "distro_name":"Ubuntu 24.04.1 LTS","gestor":"apt","paquetes":["usbmuxd","avahi-daemon"],
  "uxplay":{"candidata":"1.68.2-1build2","instalar":false},
  "archivos":["/etc/udev/rules.d/70-factum-tatana.rules","/etc/apparmor.d/factum-tatana"],
  "firewall":"ufw","grupos":[],
  "comandos_contienen":["ufw allow 5353/udp","ufw allow 7000,7001,7100/tcp","ufw allow 6000,6001,7011/udp",
    "profile factum-tatana @{HOME}/Applications/Tatana.AppImage flags=(unconfined) {","apparmor_parser -r /etc/apparmor.d/factum-tatana"]}'

# ── Debian 12: apt, sin candidato de uxplay, sin firewall ──────────────────────────────────────────────────
R="$(raiz debian-12)"
cat > "$R/etc/os-release" <<'EOF'
PRETTY_NAME="Debian GNU/Linux 12 (bookworm)"
NAME="Debian GNU/Linux"
VERSION_ID="12"
ID=debian
EOF
vacio "$R" apt-get
falso "$R" apt-cache "uxplay:
  Installed: (none)
  Candidate: (none)"
echo 0 > "$R/proc/sys/kernel/apparmor_restrict_unprivileged_userns"
uaccess_con_video "$R" lib/udev/rules.d
esperar "Debian 12" "$R" 0 '{"soportada":true,"distro_id":"debian","distro_version":"12","gestor":"apt",
  "paquetes":["usbmuxd","avahi-daemon"],"uxplay":{"candidata":null,"instalar":false},
  "archivos":["/etc/udev/rules.d/70-factum-tatana.rules"],"firewall":"inactivo","grupos":[]}'

# ── Fedora: dnf, uxplay 1.73 en el repo (se instala), firewalld corriendo ─────────────────────────────────
R="$(raiz fedora)"
cat > "$R/etc/os-release" <<'EOF'
NAME="Fedora Linux"
VERSION_ID=41
ID=fedora
PRETTY_NAME="Fedora Linux 41 (Workstation Edition)"
EOF
falso "$R" dnf "1.72

1.73"
falso "$R" firewall-cmd "running"
uaccess_con_video "$R" usr/lib/udev/rules.d
esperar "Fedora" "$R" 0 '{"soportada":true,"distro_id":"fedora","distro_version":"41","gestor":"dnf",
  "paquetes":["usbmuxd","avahi","uxplay"],"uxplay":{"candidata":"1.73","instalar":true},
  "archivos":["/etc/udev/rules.d/70-factum-tatana.rules"],"firewall":"firewalld","grupos":[],
  "comandos_contienen":["dnf install -y usbmuxd avahi uxplay","firewall-cmd --permanent --add-service=mdns",
    "--add-port=7000/tcp --add-port=7001/tcp --add-port=7100/tcp","firewall-cmd --reload"],
  "comandos_no_contienen":["apt-get"]}'

# ── Linux Mint 22: apt, uxplay 1.73.2 (se instala), sin regla uaccess de video (grupo video) ─────────────────
R="$(raiz mint-22)"
cat > "$R/etc/os-release" <<'EOF'
NAME="Linux Mint"
VERSION="22 (Wilma)"
ID=linuxmint
ID_LIKE="ubuntu debian"
PRETTY_NAME="Linux Mint 22"
VERSION_ID="22"
EOF
vacio "$R" apt-get
falso "$R" apt-cache "uxplay:
  Candidate: 2:1.73.2-1"
esperar "Linux Mint 22" "$R" 0 '{"soportada":true,"distro_id":"linuxmint","distro_version":"22","gestor":"apt",
  "paquetes":["usbmuxd","avahi-daemon","uxplay"],"uxplay":{"candidata":"2:1.73.2-1","instalar":true},
  "grupos":["video"],"comandos_contienen":["usermod -aG video <usuario>",
    "install -y --no-install-recommends usbmuxd avahi-daemon uxplay"]}'

# ── Arch: pacman → sin soporte (exit 3), lista genérica para soporte técnico ────────────────────────────────
R="$(raiz arch)"
cat > "$R/etc/os-release" <<'EOF'
NAME="Arch Linux"
PRETTY_NAME="Arch Linux"
ID=arch
BUILD_ID=rolling
EOF
vacio "$R" pacman
uaccess_con_video "$R" usr/lib/udev/rules.d
esperar "Arch (sin soporte)" "$R" 3 '{"soportada":false,"distro_id":"arch","distro_version":"","gestor":"pacman",
  "paquetes":["usbmuxd","avahi"],"uxplay":{"candidata":null,"instalar":false},
  "archivos":["/etc/udev/rules.d/70-factum-tatana.rules","/etc/apparmor.d/factum-tatana"],
  "comandos_contienen":["usbmuxd avahi","70-factum-tatana.rules","TAG+=\"uaccess\"","profile factum-tatana",
    "udevadm control --reload-rules"],
  "comandos_no_contienen":["apt-get","dnf install"]}'

# ── Sin os-release ni gestor conocido ─────────────────────────────────────────────────────────────────────
R="$(raiz desconocida)"
esperar "Distro desconocida" "$R" 3 '{"soportada":false,"distro_id":"linux","gestor":"unknown"}'

# ── Códigos de salida de argumentos y permisos ────────────────────────────────────────────────────────────
codigo_de() { set +e; "$@" </dev/null >/dev/null 2>&1; local c=$?; set -e; echo "$c"; }
[[ "$(codigo_de bash "$SCRIPT")" == 2 ]] && ok "sin argumentos → 2" || falla "sin argumentos no sale con 2"
[[ "$(codigo_de bash "$SCRIPT" --cualquiera)" == 2 ]] && ok "argumento desconocido → 2" || falla "argumento desconocido no sale con 2"
[[ "$(codigo_de bash "$SCRIPT" --ejecutar)" == 2 ]] && ok "--ejecutar sin --usuario → 2" || falla "--ejecutar sin --usuario no sale con 2"
[[ "$(codigo_de env TATANA_RAIZ_PRUEBA="$TMP/ubuntu-2204" bash "$SCRIPT" --ejecutar --usuario nadie)" == 2 ]] \
  && ok "--ejecutar con raíz de prueba → 2 (nunca toca el sistema)" || falla "--ejecutar con raíz de prueba no sale con 2"
if [[ "$(id -u)" != "0" ]]; then
  [[ "$(codigo_de bash "$SCRIPT" --ejecutar --usuario "$(id -un)")" == 5 ]] && ok "--ejecutar sin root → 5" \
    || falla "--ejecutar sin root no sale con 5"
else
  echo "AVISO  se corre como root: se saltea la prueba de 'sin root' (no se ejecuta nada real)"
fi
# Por stdin, como lo corre agent-ui (bash -s): el plan sale igual.
set +e
SALIDA_STDIN="$(TATANA_RAIZ_PRUEBA="$TMP/debian-12" bash -s -- --simular <"$SCRIPT" 2>&1)"
set -e
grep -q '^TATANA_PLAN {"schema":1,"soportada":true,"distro_id":"debian"' <<<"$SALIDA_STDIN" \
  && ok "bash -s -- --simular por stdin" || { falla "bash -s -- --simular por stdin"; echo "$SALIDA_STDIN" | sed 's/^/    /'; }

if [[ $FALLAS -gt 0 ]]; then
  echo "RESULTADO: $FALLAS prueba(s) con FALLA"
  exit 1
fi
echo "RESULTADO: OK"
