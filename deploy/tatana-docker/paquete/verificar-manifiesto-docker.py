#!/usr/bin/env python3
# Verifica tatana-update-docker.json (SDD fiscalia-operacion §5.1, §10.1, §10.3) con las mismas reglas que
# verifyEnvelopeText/payloadError de ops/tatana/_lib.mjs (y que Tatana con Electron): sobre {schema, key_id, payload,
# signature}, base64 estricto, firma Ed25519 de 64 bytes sobre los BYTES EXACTOS del payload, key_id en la lista de
# confianza y payload del producto "tatana-docker".
#
# Corre con el Python de la imagen de Tatana YA INSTALADA (python-embed 3.11 + cryptography), con -I y sin red:
#
#   python3 -I verificar-manifiesto-docker.py --manifiesto <ruta> --claves <claves-confiables.txt>
#           [--version-instalada <v>]
#   python3 -I verificar-manifiesto-docker.py --autoprueba [--tabla versiones.txt]
#   python3 -I verificar-manifiesto-docker.py --comparar A B          (imprime -1, 0 o 1; para las pruebas)
#
# Salida por stdout, una CLAVE=valor por línea (bash la lee con lista blanca, nunca con source):
#   0  válido:   VERSION IMAGEN ARCHIVO TAMANO SHA256 KIT_ARCHIVO KIT_TAMANO KIT_SHA256 KEY_ID [COMPARACION]
#   1  inválido: MOTIVO (manifiesto_invalido | clave_desconocida | firma_invalida) y DETALLE (una línea)
#   2  uso incorrecto
import base64
import binascii
import datetime
import json
import os
import re
import sys
import tempfile

PRODUCTO = "tatana-docker"
RELEASE_VERSION = re.compile(r"^\d+\.\d+\.\d+$")
SEMVER = re.compile(
    r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
    r"(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?"
    r"(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$"
)
BASE64 = re.compile(r"^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$")
KEY_ID = re.compile(r"^[a-z0-9][a-z0-9._-]{0,63}$")
HEX64 = re.compile(r"^[0-9a-f]{64}$")
HEX40 = re.compile(r"^[0-9a-f]{40}$")
MAX_SAFE = 2**53 - 1


class Invalido(Exception):
    def __init__(self, motivo, detalle):
        super().__init__(detalle)
        self.motivo = motivo
        self.detalle = detalle


# ── Comparación de versiones (semver 2.0; misma tabla que version_cmp de comun.sh, §10.8) ─────────────────────────
def _ident_cmp(a, b):
    an, bn = a.isdigit(), b.isdigit()
    if an and bn:
        return (int(a) > int(b)) - (int(a) < int(b))
    if an:
        return -1  # numérico < alfanumérico
    if bn:
        return 1
    return (a > b) - (a < b)  # ASCII


def version_cmp(a, b):
    ma, mb = SEMVER.match(a or ""), SEMVER.match(b or "")
    if not ma or not mb:
        raise ValueError(f"versión inválida: {a if not ma else b!r}")
    for x, y in zip(ma.groups()[:3], mb.groups()[:3]):
        if int(x) != int(y):
            return 1 if int(x) > int(y) else -1
    pa, pb = ma.group(4), mb.group(4)
    if pa is None and pb is None:
        return 0
    if pa is None:
        return 1  # sin prerelease > con prerelease
    if pb is None:
        return -1
    ia, ib = pa.split("."), pb.split(".")
    for x, y in zip(ia, ib):
        c = _ident_cmp(x, y)
        if c:
            return c
    return (len(ia) > len(ib)) - (len(ia) < len(ib))


# ── Reglas del sobre y del payload (las de _lib.mjs) ──────────────────────────────────────────────────────────────
def b64_estricto(s):
    """Base64 canónico (como decodeBase64Strict de _lib.mjs): None si no lo es."""
    if not isinstance(s, str) or not s or not BASE64.match(s):
        return None
    try:
        buf = base64.b64decode(s, validate=True)
    except (binascii.Error, ValueError):
        return None
    return buf if base64.b64encode(buf).decode("ascii") == s else None


def _entero_seguro(v):
    # Number.isSafeInteger: 5 y 5.0 sí (en JS son el mismo número); true/false y 5.5 no.
    if isinstance(v, bool):
        return False
    if isinstance(v, float):
        if not v.is_integer():
            return False
        v = int(v)
    return isinstance(v, int) and -MAX_SAFE <= v <= MAX_SAFE


def _fecha_iso(s):
    if not isinstance(s, str) or not s:
        return False
    try:
        datetime.datetime.fromisoformat(s.replace("Z", "+00:00") if s.endswith("Z") else s)
        return True
    except ValueError:
        return False


def error_payload(p):
    """payloadError(p, 'tatana-docker') de _lib.mjs: texto del error o None."""
    if not isinstance(p, dict):
        return "el payload no es un objeto JSON"
    if not (isinstance(p.get("schema"), int) and not isinstance(p.get("schema"), bool) and p.get("schema") == 1):
        return "payload.schema tiene que ser 1"
    if p.get("product") != PRODUCTO:
        return f'payload.product tiene que ser "{PRODUCTO}"'
    v = p.get("version")
    if not isinstance(v, str) or not RELEASE_VERSION.match(v):
        return "payload.version tiene que ser X.Y.Z"
    archivo = f"imagen-tatana-docker-v{v}.tar.gz"
    if p.get("file") != archivo:
        return f"payload.file tiene que ser {archivo}"
    if not _entero_seguro(p.get("size")) or p.get("size") <= 0:
        return "payload.size tiene que ser un entero > 0"
    sha512 = b64_estricto(p.get("sha512"))
    if sha512 is None or len(sha512) != 64:
        return "payload.sha512 tiene que ser base64 de 64 bytes"
    if not isinstance(p.get("sha256"), str) or not HEX64.match(p.get("sha256")):
        return "payload.sha256 tiene que ser hex en minúsculas"
    if not _fecha_iso(p.get("released_at")):
        return "payload.released_at tiene que ser ISO-8601"
    if not isinstance(p.get("commit"), str) or not HEX40.match(p.get("commit")):
        return "payload.commit tiene que ser un SHA de 40 hex"
    notas = p.get("notes")
    if notas is not None and not isinstance(notas, str):
        return "payload.notes tiene que ser texto"
    if isinstance(notas, str) and len(notas) > 2000:
        return "payload.notes tiene más de 2000 caracteres"
    imagen = f"factum-tatana-docker:{v}"
    if p.get("image") != imagen:
        return f"payload.image tiene que ser {imagen}"
    k = p.get("kit")
    if not isinstance(k, dict):
        return "payload.kit tiene que ser un objeto"
    kit = f"kit-tatana-docker-v{v}.tar.gz"
    if k.get("file") != kit:
        return f"payload.kit.file tiene que ser {kit}"
    if not _entero_seguro(k.get("size")) or k.get("size") <= 0:
        return "payload.kit.size tiene que ser un entero > 0"
    if not isinstance(k.get("sha256"), str) or not HEX64.match(k.get("sha256")):
        return "payload.kit.sha256 tiene que ser hex en minúsculas"
    return None


def leer_claves(ruta):
    """claves-confiables.txt: "key_id spkiDerBase64" por línea; # comentario. Líneas mal formadas se ignoran."""
    claves = {}
    with open(ruta, encoding="utf-8") as f:
        for linea in f:
            linea = linea.strip()
            if not linea or linea.startswith("#"):
                continue
            partes = linea.split()
            if len(partes) == 2 and KEY_ID.match(partes[0]) and b64_estricto(partes[1]) is not None:
                claves.setdefault(partes[0], partes[1])
    return claves


def verificar_texto(texto, claves):
    """verifyEnvelopeText(texto, claves, 'tatana-docker'): (payload, key_id) o Invalido."""
    if texto.startswith("﻿"):
        texto = texto[1:]
    try:
        env = json.loads(texto)
    except ValueError:
        raise Invalido("manifiesto_invalido", "JSON roto")
    if (
        not isinstance(env, dict)
        or not (isinstance(env.get("schema"), int) and not isinstance(env.get("schema"), bool) and env.get("schema") == 1)
        or not isinstance(env.get("key_id"), str)
        or not isinstance(env.get("payload"), str)
        or not isinstance(env.get("signature"), str)
    ):
        raise Invalido("manifiesto_invalido", "el sobre no tiene schema/key_id/payload/signature")
    spki = claves.get(env["key_id"])
    if spki is None:
        raise Invalido("clave_desconocida", f'key_id "{env["key_id"]}" no es de confianza')
    payload_bytes = b64_estricto(env["payload"])
    firma = b64_estricto(env["signature"])
    if payload_bytes is None:
        raise Invalido("manifiesto_invalido", "payload no es base64")
    if firma is None or len(firma) != 64:
        raise Invalido("firma_invalida", "firma mal formada")
    # cryptography recién acá: --comparar y el resto de las reglas no la necesitan.
    from cryptography.exceptions import InvalidSignature
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
    from cryptography.hazmat.primitives.serialization import load_der_public_key

    try:
        pub = load_der_public_key(base64.b64decode(spki))
    except Exception:
        raise Invalido("clave_desconocida", "la clave confiable está corrupta")
    if not isinstance(pub, Ed25519PublicKey):
        raise Invalido("clave_desconocida", "la clave confiable está corrupta")
    try:
        pub.verify(firma, payload_bytes)
    except InvalidSignature:
        raise Invalido("firma_invalida", "la firma no coincide")
    try:
        payload = json.loads(payload_bytes.decode("utf-8"))
    except (UnicodeDecodeError, ValueError):
        raise Invalido("manifiesto_invalido", "el payload no es JSON")
    err = error_payload(payload)
    if err:
        raise Invalido("manifiesto_invalido", err)
    return payload, env["key_id"]


def una_linea(s):
    return re.sub(r"[\r\n\t]+", " ", str(s))[:300]


def salida_valida(payload, key_id, version_instalada):
    lineas = [
        ("VERSION", payload["version"]),
        ("IMAGEN", payload["image"]),
        ("ARCHIVO", payload["file"]),
        ("TAMANO", int(payload["size"])),
        ("SHA256", payload["sha256"]),
        ("KIT_ARCHIVO", payload["kit"]["file"]),
        ("KIT_TAMANO", int(payload["kit"]["size"])),
        ("KIT_SHA256", payload["kit"]["sha256"]),
        ("KEY_ID", key_id),
    ]
    if version_instalada is not None:
        c = version_cmp(payload["version"], version_instalada)
        lineas.append(("COMPARACION", {1: "mayor", 0: "igual", -1: "menor"}[c]))
    return "".join(f"{k}={v}\n" for k, v in lineas)


# ── Autoprueba (clave efímera, sin red) ────────────────────────────────────────────────────────────────────────────
TABLA_INTERNA = """
0.0.0-docker-prueba 0.1.0 -1
0.0.0-fiscalia-prueba 0.0.1 -1
1.10.0 1.9.0 1
1.0.0-rc.1 1.0.0 -1
1.0.0-alpha 1.0.0-alpha.1 -1
1.0.0-alpha.2 1.0.0-alpha.10 -1
1.0.0-beta 1.0.0-alpha 1
2.0.0 2.0.0 0
"""


def leer_tabla(texto):
    casos = []
    for linea in texto.splitlines():
        linea = linea.split("#", 1)[0].strip()
        if linea:
            a, b, e = linea.split()
            casos.append((a, b, int(e)))
    return casos


def autoprueba(tabla):
    from cryptography.hazmat.primitives import serialization
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey

    fallos = 0

    def ok(m):
        print(f"OK    {m}")

    def mal(m):
        nonlocal fallos
        fallos += 1
        print(f"FALLA {m}")

    casos = leer_tabla(TABLA_INTERNA) + (leer_tabla(tabla) if tabla else [])
    for a, b, e in casos:
        r = version_cmp(a, b)
        (ok if r == e else mal)(f"version_cmp {a} {b} = {r} (esperado {e})")
        r2 = version_cmp(b, a)
        (ok if r2 == -e else mal)(f"version_cmp {b} {a} = {r2} (esperado {-e})")

    priv = Ed25519PrivateKey.generate()
    spki = base64.b64encode(
        priv.public_key().public_bytes(serialization.Encoding.DER, serialization.PublicFormat.SubjectPublicKeyInfo)
    ).decode()
    otra = Ed25519PrivateKey.generate()
    claves = {"autoprueba-1": spki}
    base = {
        "schema": 1,
        "product": PRODUCTO,
        "version": "1.5.0",
        "released_at": "2026-10-25T12:00:00Z",
        "commit": "a" * 40,
        "file": "imagen-tatana-docker-v1.5.0.tar.gz",
        "size": 1234,
        "sha256": "b" * 64,
        "sha512": base64.b64encode(b"\x01" * 64).decode(),
        "image": "factum-tatana-docker:1.5.0",
        "kit": {"file": "kit-tatana-docker-v1.5.0.tar.gz", "size": 99, "sha256": "c" * 64},
    }

    def sobre(payload, key_id="autoprueba-1", clave=priv, crudo=None):
        pb = crudo if crudo is not None else json.dumps(payload, indent=2).encode()
        return json.dumps(
            {
                "schema": 1,
                "key_id": key_id,
                "payload": base64.b64encode(pb).decode(),
                "signature": base64.b64encode(clave.sign(pb)).decode(),
            }
        )

    def espera(desc, texto, motivo):
        try:
            verificar_texto(texto, claves)
            got = "válido"
        except Invalido as e:
            got = e.motivo
        (ok if got == motivo else mal)(f"{desc}: {got}" + ("" if got == motivo else f" (esperado {motivo})"))

    espera("manifiesto válido", sobre(base), "válido")
    espera("con BOM", "﻿" + sobre(base), "válido")
    espera("JSON roto", "{ roto", "manifiesto_invalido")
    espera("sin signature", json.dumps({"schema": 1, "key_id": "autoprueba-1", "payload": "e30="}), "manifiesto_invalido")
    espera("key_id desconocido", sobre(base, key_id="otra-clave"), "clave_desconocida")
    espera("firmado con otra clave", sobre(base, clave=otra), "firma_invalida")
    env = json.loads(sobre(base))
    pb = bytearray(base64.b64decode(env["payload"]))
    pb[pb.index(b"1.5.0")] = ord("9")
    env_alt = dict(env, payload=base64.b64encode(bytes(pb)).decode())
    espera("payload alterado", json.dumps(env_alt), "firma_invalida")
    sig = bytearray(base64.b64decode(env["signature"]))
    sig[0] ^= 1
    espera("firma alterada", json.dumps(dict(env, signature=base64.b64encode(bytes(sig)).decode())), "firma_invalida")
    espera("firma corta", json.dumps(dict(env, signature=base64.b64encode(b"x" * 63).decode())), "firma_invalida")
    espera("payload no canónico", json.dumps(dict(env, payload=env["payload"] + "\n")), "manifiesto_invalido")
    espera("producto tatana-linux", sobre(dict(base, product="tatana-linux")), "manifiesto_invalido")
    espera("producto tatana-windows", sobre(dict(base, product="tatana-windows")), "manifiesto_invalido")
    espera("image con otro tag", sobre(dict(base, image="factum-tatana-docker:1.4.0")), "manifiesto_invalido")
    espera("image con otro nombre", sobre(dict(base, image="otra:1.5.0")), "manifiesto_invalido")
    espera("file con otro nombre", sobre(dict(base, file="imagen-1.5.0.tar.gz")), "manifiesto_invalido")
    espera("kit.file raro", sobre(dict(base, kit=dict(base["kit"], file="kit.tar.gz"))), "manifiesto_invalido")
    espera("kit.sha256 en mayúsculas", sobre(dict(base, kit=dict(base["kit"], sha256="C" * 64))), "manifiesto_invalido")
    espera("kit.size 0", sobre(dict(base, kit=dict(base["kit"], size=0))), "manifiesto_invalido")
    espera("sin kit", sobre({k: v for k, v in base.items() if k != "kit"}), "manifiesto_invalido")
    espera("versión prerelease", sobre(dict(base, version="1.5.0-rc.1")), "manifiesto_invalido")
    espera("size true", sobre(dict(base, size=True)), "manifiesto_invalido")
    espera("payload no JSON", sobre(None, crudo=b"no es json"), "manifiesto_invalido")

    # Por archivo, como lo llama el actualizador (claves-confiables.txt con comentarios y una línea basura).
    with tempfile.TemporaryDirectory() as d:
        man = os.path.join(d, "tatana-update-docker.json")
        cl = os.path.join(d, "claves-confiables.txt")
        with open(man, "w", encoding="utf-8") as f:
            f.write(sobre(base) + "\n")
        with open(cl, "w", encoding="utf-8") as f:
            f.write(f"# comentario\n\nbasura sin formato válido !!\nautoprueba-1 {spki}\n")
        payload, kid = verificar_texto(open(man, encoding="utf-8").read(), leer_claves(cl))
        out = salida_valida(payload, kid, "0.0.0-docker-prueba")
        esperado = (
            "VERSION=1.5.0\nIMAGEN=factum-tatana-docker:1.5.0\nARCHIVO=imagen-tatana-docker-v1.5.0.tar.gz\nTAMANO=1234\n"
            f"SHA256={'b' * 64}\nKIT_ARCHIVO=kit-tatana-docker-v1.5.0.tar.gz\nKIT_TAMANO=99\nKIT_SHA256={'c' * 64}\n"
            "KEY_ID=autoprueba-1\nCOMPARACION=mayor\n"
        )
        (ok if out == esperado else mal)("salida CLAVE=valor" + ("" if out == esperado else f": {out!r}"))
        for inst, comp in (("1.5.0", "igual"), ("1.6.0", "menor"), ("1.5.0-rc.2", "mayor")):
            c = salida_valida(payload, kid, inst).strip().splitlines()[-1]
            (ok if c == f"COMPARACION={comp}" else mal)(f"contra instalada {inst}: {c}")

    print(f"RESULTADO={'ok' if fallos == 0 else 'falla'}")
    return 0 if fallos == 0 else 1


def uso(msg):
    sys.stderr.write(f"verificar-manifiesto-docker.py: {msg}\n")
    sys.exit(2)


def main(argv):
    args = {}
    i = 0
    flags = {"--autoprueba"}
    con_valor = {"--manifiesto", "--claves", "--version-instalada", "--tabla"}
    while i < len(argv):
        a = argv[i]
        if a in flags:
            args[a] = True
            i += 1
        elif a == "--comparar":
            if len(argv) - i < 3:
                uso("--comparar necesita dos versiones")
            args[a] = (argv[i + 1], argv[i + 2])
            i += 3
        elif a in con_valor:
            if i + 1 >= len(argv):
                uso(f"falta el valor de {a}")
            args[a] = argv[i + 1]
            i += 2
        else:
            uso(f"opción desconocida: {a}")

    if "--comparar" in args:
        a, b = args["--comparar"]
        try:
            print(version_cmp(a, b))
        except ValueError as e:
            uso(str(e))
        return 0
    if args.get("--autoprueba"):
        tabla = None
        if "--tabla" in args:
            with open(args["--tabla"], encoding="utf-8") as f:
                tabla = f.read()
        return autoprueba(tabla)
    if "--manifiesto" not in args or "--claves" not in args:
        uso("uso: --manifiesto <ruta> --claves <claves-confiables.txt> [--version-instalada <v>]")
    instalada = args.get("--version-instalada")
    if instalada is not None and not SEMVER.match(instalada):
        uso(f"--version-instalada inválida: {instalada!r}")
    try:
        with open(args["--manifiesto"], "rb") as f:
            crudo = f.read(1024 * 1024)
        texto = crudo.decode("utf-8")
    except (OSError, UnicodeDecodeError) as e:
        print(f"MOTIVO=manifiesto_invalido\nDETALLE={una_linea(e)}")
        return 1
    try:
        claves = leer_claves(args["--claves"])
    except OSError as e:
        print(f"MOTIVO=clave_desconocida\nDETALLE=no se pudo leer la lista de claves: {una_linea(e)}")
        return 1
    if not claves:
        print("MOTIVO=clave_desconocida\nDETALLE=la lista de claves confiables está vacía")
        return 1
    try:
        payload, key_id = verificar_texto(texto, claves)
    except Invalido as e:
        print(f"MOTIVO={e.motivo}\nDETALLE={una_linea(e.detalle)}")
        return 1
    sys.stdout.write(salida_valida(payload, key_id, instalada))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
