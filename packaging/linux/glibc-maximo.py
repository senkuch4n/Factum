#!/usr/bin/env python3
"""Versión máxima de glibc que piden los ELF de un árbol (SDD tatana-linux §6.1 paso 8, §6.2).

    python3 -I packaging/linux/glibc-maximo.py [--max 2.35] [--detalle] <carpeta>...

Lee la sección SHT_GNU_verneed (las versiones de símbolos que el ELF necesita, p. ej. GLIBC_2.34) de cada
archivo ELF bajo las carpetas, sin dependencias externas (ni readelf ni pyelftools). Imprime el máximo y qué
archivo lo pide. Con --max, sale con 1 si algún ELF pide más que eso: es la guarda que evita que un cambio de
versión de una herramienta suba el requisito publicado (glibc ≥ 2.35) sin que nadie se entere.

Códigos de salida: 0 ok · 1 algún ELF supera --max · 2 argumentos o carpeta inexistente.
"""

import argparse
import os
import re
import struct
import sys

SHT_GNU_VERNEED = 0x6FFFFFFE
GLIBC = re.compile(r"^GLIBC_(\d+)\.(\d+)(?:\.(\d+))?$")


def version_tuple(texto):
    m = GLIBC.match("GLIBC_" + texto) if not texto.startswith("GLIBC_") else GLIBC.match(texto)
    if not m:
        raise ValueError("versión de glibc inválida: %s" % texto)
    return tuple(int(g) for g in m.groups() if g is not None)


def formatear(v):
    return ".".join(str(x) for x in v)


def leer_cadena(datos, inicio):
    fin = datos.find(b"\0", inicio)
    if fin < 0:
        fin = len(datos)
    return datos[inicio:fin].decode("ascii", "replace")


def versiones_glibc(ruta):
    """Conjunto de versiones GLIBC_x.y que pide el ELF (vacío si no es ELF o no tiene verneed)."""
    try:
        with open(ruta, "rb") as f:
            datos = f.read()
    except OSError:
        return set()
    if len(datos) < 64 or datos[:4] != b"\x7fELF":
        return set()
    clase, endian = datos[4], datos[5]
    if clase not in (1, 2) or endian not in (1, 2):
        return set()
    e = "<" if endian == 1 else ">"
    es64 = clase == 2
    try:
        if es64:
            shoff = struct.unpack_from(e + "Q", datos, 0x28)[0]
            shentsize, shnum = struct.unpack_from(e + "HH", datos, 0x3A)
        else:
            shoff = struct.unpack_from(e + "I", datos, 0x20)[0]
            shentsize, shnum = struct.unpack_from(e + "HH", datos, 0x2E)
        secciones = []
        for i in range(shnum):
            base = shoff + i * shentsize
            if es64:
                _, tipo, _, _, off, tam, link, _, _, _ = struct.unpack_from(e + "IIQQQQIIQQ", datos, base)
            else:
                _, tipo, _, _, off, tam, link, _, _, _ = struct.unpack_from(e + "IIIIIIIIII", datos, base)
            secciones.append((tipo, off, tam, link))
    except struct.error:
        return set()

    out = set()
    for tipo, off, tam, link in secciones:
        if tipo != SHT_GNU_VERNEED or link >= len(secciones):
            continue
        _, str_off, str_tam, _ = secciones[link]
        strtab = datos[str_off:str_off + str_tam]
        pos = off
        try:
            while True:
                vn_version, vn_cnt, vn_file, vn_aux, vn_next = struct.unpack_from(e + "HHIII", datos, pos)
                aux = pos + vn_aux
                for _ in range(vn_cnt):
                    _, _, _, vna_name, vna_next = struct.unpack_from(e + "IHHII", datos, aux)
                    nombre = leer_cadena(strtab, vna_name)
                    m = GLIBC.match(nombre)
                    if m:
                        out.add(tuple(int(g) for g in m.groups() if g is not None))
                    if vna_next == 0:
                        break
                    aux += vna_next
                if vn_next == 0:
                    break
                pos += vn_next
        except struct.error:
            pass
    return out


def recorrer(carpetas):
    for carpeta in carpetas:
        if os.path.isfile(carpeta):
            yield carpeta
            continue
        for raiz, _, archivos in os.walk(carpeta):
            for nombre in archivos:
                ruta = os.path.join(raiz, nombre)
                if os.path.islink(ruta) or not os.path.isfile(ruta):
                    continue  # los symlinks apuntan a archivos que ya se recorren
                yield ruta


def main():
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--max", help="falla (exit 1) si algún ELF pide una glibc mayor (p. ej. 2.35)")
    p.add_argument("--detalle", action="store_true", help="listar cada ELF con su máximo")
    p.add_argument("carpetas", nargs="+")
    a = p.parse_args()

    for c in a.carpetas:
        if not os.path.exists(c):
            print("ERROR: no existe %s" % c, file=sys.stderr)
            sys.exit(2)
    try:
        tope = version_tuple(a.max) if a.max else None
    except ValueError as ex:
        print("ERROR: %s" % ex, file=sys.stderr)
        sys.exit(2)

    maximo, quien, elfs = (), None, 0
    excedidos = []
    for ruta in recorrer(a.carpetas):
        vs = versiones_glibc(ruta)
        if not vs:
            continue
        elfs += 1
        alto = max(vs)
        if a.detalle:
            print("%-8s %s" % (formatear(alto), ruta))
        if alto > maximo:
            maximo, quien = alto, ruta
        if tope is not None and alto > tope:
            excedidos.append((alto, ruta))

    if not maximo:
        print("glibc máxima: (ningún ELF pide versiones de glibc; %d archivos ELF)" % elfs)
    else:
        print("glibc máxima: %s (%s; %d archivos ELF con versiones de glibc)" % (formatear(maximo), quien, elfs))
    if excedidos:
        for alto, ruta in sorted(excedidos, reverse=True):
            print("FALLA  %s pide glibc %s (máximo permitido %s)" % (ruta, formatear(alto), a.max), file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
