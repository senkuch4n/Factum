#!/usr/bin/env python3
"""Lock de Python de Tatana para Windows (SDD ios-herramientas-windows §7.2).

Corre en la Mac o en Linux (nunca hace falta Windows):

    python3 -I packaging/portable/ios-win/generar-lock.py          # escribe requirements-win.lock
    python3 -I packaging/portable/ios-win/generar-lock.py --check  # verifica el lock commiteado

Generar: resuelve requirements.in con pip para win_amd64 / CPython 3.11 (solo wheels; hexdump se
construye desde su sdist verificado por hash, hexdump.txt), recorre requires_dist de cada paquete
evaluando los marcadores con un entorno Windows/CPython 3.11.9 y FALLA si queda una dependencia
sin cubrir (pip --platform evalúa los marcadores con los de esta máquina, A3). Escribe
`nombre==versión --hash=sha256:<hash>` por línea, sin hexdump.

--check: no re-resuelve versiones (que PyPI publique versiones nuevas no rompe el check). Hace un
dry-run con el lock commiteado (--no-deps, mismas opciones) y verifica que cada wheel exista con
ese hash, que esté pymobiledevice3==10.7.4 y que la clausura con marcadores de Windows quede
cubierta por el lock + hexdump.txt. Sale distinto de 0 si algo no cierra.
"""

import argparse
import datetime
import json
import os
import re
import subprocess
import sys
import tempfile

AQUI = os.path.dirname(os.path.abspath(__file__))
REQUIREMENTS_IN = os.path.join(AQUI, "requirements.in")
LOCK = os.path.join(AQUI, "requirements-win.lock")
HEXDUMP = os.path.join(AQUI, "hexdump.txt")
PYMOBILEDEVICE3 = ("pymobiledevice3", "10.7.4")

PLATAFORMA = ["--platform", "win_amd64", "--implementation", "cp", "--python-version", "311",
              "--only-binary=:all:"]

# Entorno de marcadores (PEP 508) del Python embebido del portátil: Windows x64, CPython 3.11.9.
ENTORNO_WINDOWS = {
    "implementation_name": "cpython",
    "implementation_version": "3.11.9",
    "os_name": "nt",
    "platform_machine": "AMD64",
    "platform_python_implementation": "CPython",
    "platform_release": "10",
    "platform_system": "Windows",
    "platform_version": "10.0.19045",
    "python_full_version": "3.11.9",
    "python_version": "3.11",
    "sys_platform": "win32",
}

try:  # packaging de verdad o el que trae pip adentro
    from packaging.requirements import Requirement
    from packaging.utils import canonicalize_name
    from packaging.version import Version
except ImportError:  # pragma: no cover
    from pip._vendor.packaging.requirements import Requirement
    from pip._vendor.packaging.utils import canonicalize_name
    from pip._vendor.packaging.version import Version


def fallar(msg):
    sys.exit("ERROR: " + msg)


def pip(*args):
    cmd = [sys.executable, "-m", "pip", "--disable-pip-version-check", *args]
    r = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    if r.returncode != 0:
        sys.stderr.write(r.stdout)
        fallar("falló: " + " ".join(cmd))
    return r.stdout


def construir_hexdump(wheelhouse):
    pip("wheel", "--quiet", "--no-deps", "--require-hashes", "-r", HEXDUMP, "--wheel-dir", wheelhouse)
    if not any(n.startswith("hexdump-") and n.endswith(".whl") for n in os.listdir(wheelhouse)):
        fallar("no se construyó el wheel de hexdump")


def reporte(tmp, *args):
    """pip install --dry-run --report con la plataforma de Windows; devuelve report['install']."""
    ruta = os.path.join(tmp, "reporte-%d.json" % len(os.listdir(tmp)))
    destino = os.path.join(tmp, "target")  # --target: sin PEP 668 y sin mirar lo instalado
    pip("install", "--quiet", "--dry-run", "--ignore-installed", "--no-cache-dir", "--report", ruta,
        "--target", destino, *PLATAFORMA, *args)
    with open(ruta, encoding="utf-8") as f:
        return json.load(f)["install"]


def sha256_de(item):
    info = item.get("download_info", {}).get("archive_info", {})
    hashes = info.get("hashes") or {}
    if "sha256" in hashes:
        return hashes["sha256"]
    h = info.get("hash", "")
    return h.split("=", 1)[1] if h.startswith("sha256=") else None


def paquetes_del_reporte(items):
    out = {}
    for it in items:
        meta = it["metadata"]
        out[canonicalize_name(meta["name"])] = {
            "name": meta["name"],
            "version": meta["version"],
            "requires_dist": meta.get("requires_dist") or [],
            "sha256": sha256_de(it),
            "url": it.get("download_info", {}).get("url", ""),
        }
    return out


def requisitos_windows(requires_dist, extras):
    """Requirement de requires_dist cuyo marcador da True en Windows (con esos extras)."""
    activos = []
    for linea in requires_dist:
        req = Requirement(linea)
        if req.marker is None:
            activos.append(req)
            continue
        for extra in [""] + sorted(extras):
            entorno = dict(ENTORNO_WINDOWS, extra=extra)
            if req.marker.evaluate(entorno):
                activos.append(req)
                break
    return activos


def verificar_clausura(paquetes, raices, cubiertos_aparte=()):
    """Recorre la clausura desde las raíces con marcadores de Windows. Devuelve los faltantes."""
    aparte = {canonicalize_name(n): v for n, v in cubiertos_aparte}
    faltan = []
    vistos = set()
    pendientes = [(canonicalize_name(r.name), frozenset(r.extras), r) for r in raices]
    while pendientes:
        nombre, extras, req = pendientes.pop()
        clave = (nombre, extras)
        if clave in vistos:
            continue
        vistos.add(clave)
        if nombre in aparte:
            if not req.specifier.contains(aparte[nombre], prereleases=True):
                faltan.append("%s (%s; está %s)" % (req.name, req.specifier, aparte[nombre]))
            continue
        pkg = paquetes.get(nombre)
        if pkg is None:
            faltan.append(str(req))
            continue
        if req.specifier and not req.specifier.contains(pkg["version"], prereleases=True):
            faltan.append("%s (pide %s; el lock tiene %s)" % (req.name, req.specifier, pkg["version"]))
            continue
        for dep in requisitos_windows(pkg["requires_dist"], extras):
            pendientes.append((canonicalize_name(dep.name), frozenset(dep.extras), dep))
    return sorted(set(faltan))


def leer_requirements(ruta):
    reqs = []
    with open(ruta, encoding="utf-8") as f:
        for linea in f:
            linea = linea.split("#", 1)[0].strip()
            if not linea:
                continue
            linea = re.split(r"\s+--hash=", linea)[0].strip()
            reqs.append(Requirement(linea))
    return reqs


def leer_lock(ruta):
    entradas = {}
    patron = re.compile(r"^([A-Za-z0-9_.\-]+)==(\S+)\s+--hash=sha256:([0-9a-f]{64})$")
    with open(ruta, encoding="utf-8") as f:
        for n, linea in enumerate(f, 1):
            linea = linea.strip()
            if not linea or linea.startswith("#"):
                continue
            m = patron.match(linea)
            if not m:
                fallar("%s:%d no tiene la forma nombre==versión --hash=sha256:<hash>: %s" % (ruta, n, linea))
            entradas[canonicalize_name(m.group(1))] = (m.group(1), m.group(2), m.group(3))
    return entradas


def hexdump_fijado():
    req = leer_requirements(HEXDUMP)[0]
    version = next(iter(req.specifier)).version
    return req.name, version


def generar():
    with tempfile.TemporaryDirectory(prefix="tatana-lock-") as tmp:
        wheelhouse = os.path.join(tmp, "wheelhouse")
        os.makedirs(wheelhouse)
        construir_hexdump(wheelhouse)
        trabajo = os.path.join(tmp, "pip")
        os.makedirs(trabajo)
        items = reporte(trabajo, "--find-links", wheelhouse, "-r", REQUIREMENTS_IN)
    paquetes = paquetes_del_reporte(items)
    hx_nombre, hx_version = hexdump_fijado()

    faltan = verificar_clausura(paquetes, leer_requirements(REQUIREMENTS_IN))
    if faltan:
        fallar("dependencias de Windows sin cubrir (agregalas a requirements.in): " + ", ".join(faltan))

    nombre_pmd, version_pmd = PYMOBILEDEVICE3
    if paquetes.get(nombre_pmd, {}).get("version") != version_pmd:
        fallar("la resolución no trae %s==%s" % PYMOBILEDEVICE3)

    lineas = []
    for clave in sorted(paquetes):
        if clave == canonicalize_name(hx_nombre):
            continue
        pkg = paquetes[clave]
        if not pkg["sha256"]:
            fallar("%s %s no trae sha256 en el reporte de pip" % (pkg["name"], pkg["version"]))
        if not pkg["url"].endswith(".whl"):
            fallar("%s %s no es un wheel (%s)" % (pkg["name"], pkg["version"], pkg["url"]))
        lineas.append("%s==%s --hash=sha256:%s" % (pkg["name"], pkg["version"], pkg["sha256"]))

    hoy = datetime.date.today().isoformat()
    encabezado = [
        "# Lock de Python de Tatana para Windows (win_amd64, CPython 3.11). NO editar a mano.",
        "# Generado el %s con: python3 -I packaging/portable/ios-win/generar-lock.py" % hoy,
        "# Entrada: requirements.in. hexdump va aparte (hexdump.txt: solo existe como sdist).",
        "# Verificar: python3 -I packaging/portable/ios-win/generar-lock.py --check",
    ]
    with open(LOCK, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(encabezado + lineas) + "\n")
    print("OK  %s: %d paquetes" % (os.path.relpath(LOCK), len(lineas)))
    for clave in ("pymobiledevice3", "pywin32", "av", "lzfse"):
        if clave in paquetes:
            print("    %s %s" % (paquetes[clave]["name"], paquetes[clave]["version"]))


def check():
    if not os.path.isfile(LOCK):
        fallar("falta %s (generalo con generar-lock.py)" % LOCK)
    lock = leer_lock(LOCK)
    nombre_pmd, version_pmd = PYMOBILEDEVICE3
    if lock.get(nombre_pmd, (None, None))[1] != version_pmd:
        fallar("el lock no fija %s==%s" % PYMOBILEDEVICE3)

    with tempfile.TemporaryDirectory(prefix="tatana-lock-") as tmp:
        items = reporte(tmp, "--no-deps", "--require-hashes", "-r", LOCK)
    paquetes = paquetes_del_reporte(items)

    errores = []
    for clave, (nombre, version, sha) in sorted(lock.items()):
        pkg = paquetes.get(clave)
        if pkg is None:
            errores.append("%s==%s no se resolvió" % (nombre, version))
        elif pkg["version"] != version:
            errores.append("%s: el lock dice %s y pip resolvió %s" % (nombre, version, pkg["version"]))
        elif pkg["sha256"] != sha:
            errores.append("%s==%s: el wheel no tiene el hash del lock (%s ≠ %s)" % (nombre, version, pkg["sha256"], sha))
        elif not pkg["url"].endswith(".whl"):
            errores.append("%s==%s no es un wheel" % (nombre, version))
    if len(paquetes) != len(lock):
        errores.append("pip resolvió %d paquetes y el lock tiene %d" % (len(paquetes), len(lock)))

    raices = [Requirement("%s==%s" % (n, v)) for n, v, _ in lock.values()]
    faltan = verificar_clausura(paquetes, raices, cubiertos_aparte=[hexdump_fijado()])
    if faltan:
        errores.append("dependencias de Windows sin cubrir: " + ", ".join(faltan))

    if errores:
        fallar("el lock no cierra:\n  - " + "\n  - ".join(errores))
    print("OK  %s: %d wheels con hash verificado, %s==%s, clausura de Windows cubierta"
          % (os.path.relpath(LOCK), len(lock), nombre_pmd, version_pmd))


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true", help="verificar el lock commiteado sin reescribirlo")
    args = parser.parse_args()
    check() if args.check else generar()


if __name__ == "__main__":
    main()
