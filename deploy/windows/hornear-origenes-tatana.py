#!/usr/bin/env python3
"""Hornea Agent:AllowedOrigins en el appsettings.json del portátil de Tatana (despliegue-nube §6.8).

Uso:
  hornear-origenes-tatana.py <appsettings.json> <origen> [<origen> ...]
  hornear-origenes-tatana.py --probar

La lista final es SIEMPRE los localhost de siempre + los orígenes pedidos (deduplicada, en orden):
en Tatana, Agent:AllowedOrigins REEMPLAZA a los defaults, no se suma (OriginPolicy.cs).

La validación es MÁS estricta que la de Tatana (OriginPolicy.TryNormalize): todo lo que acepta este
script lo acepta Tatana. Un origen inválido haría que Tatana no arranque en la PC del perito, así que
acá se corta antes de hornear (exit 2).

Lo llama deploy/windows/armar-tatana-portable.sh --origenes. Nunca correrlo sobre el
appsettings.json del repo: siempre sobre la copia del publish.
"""

import json
import re
import sys

LOCALES = ["http://localhost:3000", "http://127.0.0.1:3000"]

# Contrato compartido §5.3 de Refactorizaciones/despliegue-nube.md.
PATRON = re.compile(
    r"^https?://[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*(:[0-9]{1,5})?$"
)


class OrigenInvalido(ValueError):
    pass


def normalizar(crudo):
    """strip + minúsculas + sin '/' final. Tira OrigenInvalido si no pasa la regla."""
    valor = (crudo or "").strip().lower()
    if valor.endswith("/"):
        valor = valor[:-1]
    if not valor or "*" in valor:
        raise OrigenInvalido(crudo)
    if not PATRON.match(valor):
        raise OrigenInvalido(crudo)
    puerto = re.search(r":([0-9]{1,5})$", valor)
    if puerto and not 1 <= int(puerto.group(1)) <= 65535:
        raise OrigenInvalido(crudo)
    return valor


def lista_final(origenes):
    resultado = []
    for o in LOCALES + [normalizar(x) for x in origenes]:
        if o not in resultado:
            resultado.append(o)
    return resultado


def hornear(ruta, origenes):
    lista = lista_final(origenes)  # valida ANTES de tocar el archivo
    with open(ruta, encoding="utf-8-sig") as f:
        cfg = json.load(f)
    agente = cfg.get("Agent")
    if not isinstance(agente, dict):
        agente = {}
        cfg["Agent"] = agente
    agente["AllowedOrigins"] = lista
    with open(ruta, "w", encoding="utf-8", newline="\n") as f:  # UTF-8 sin BOM
        json.dump(cfg, f, ensure_ascii=False, indent=2)
        f.write("\n")
    return lista


def probar():
    validos = {
        "https://factum-piloto.duckdns.org": "https://factum-piloto.duckdns.org",
        "  HTTPS://Factum.Ejemplo.com.ar/ ": "https://factum.ejemplo.com.ar",
        "http://localhost:3001": "http://localhost:3001",
        "https://192.168.0.10:8443": "https://192.168.0.10:8443",
        "https://a.b-c.d": "https://a.b-c.d",
    }
    invalidos = [
        "", "*", "https://*.ejemplo.com", "https://x.com/path", "https://x.com?q=1", "https://x.com#f",
        "ftp://x.com", "x.com", "https://user@x.com", "https://-x.com", "https://x-.com", "https://x..com",
        "https://x.com:99999", "https://x.com:0", "https://x_y.com", "file:///tmp", "https://",
    ]
    fallas = []
    for entrada, esperado in validos.items():
        try:
            obtenido = normalizar(entrada)
            if obtenido != esperado:
                fallas.append(f"{entrada!r}: esperado {esperado!r}, obtenido {obtenido!r}")
        except OrigenInvalido:
            fallas.append(f"{entrada!r}: se rechazó y tenía que pasar")
    for entrada in invalidos:
        try:
            normalizar(entrada)
            fallas.append(f"{entrada!r}: pasó y tenía que rechazarse")
        except OrigenInvalido:
            pass
    dedup = lista_final(["https://a.com", "https://A.com/", "http://localhost:3000"])
    if dedup != LOCALES + ["https://a.com"]:
        fallas.append(f"deduplicado: {dedup!r}")
    if fallas:
        for f in fallas:
            print("FALLA:", f, file=sys.stderr)
        return 1
    print(f"OK  {len(validos)} válidos, {len(invalidos)} inválidos y deduplicado")
    return 0


def main(argv):
    if argv[1:] == ["--probar"]:
        return probar()
    if len(argv) < 3:
        print(__doc__.strip(), file=sys.stderr)
        return 2
    ruta, origenes = argv[1], argv[2:]
    try:
        lista = hornear(ruta, origenes)
    except OrigenInvalido as e:
        print(
            f"ERROR: origen inválido para Tatana: {e.args[0]!r}. Formato: http(s)://host[:puerto], "
            "sin path, query, fragmento, usuario ni '*'.",
            file=sys.stderr,
        )
        return 2
    print(f"  OK  Agent.AllowedOrigins en {ruta}:")
    for o in lista:
        print(f"      {o}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
