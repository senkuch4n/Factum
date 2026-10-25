#!/usr/bin/env python3
# Habla con el agente de Tatana en Docker en 127.0.0.1:<puerto> para el actualizador (SDD fiscalia-operacion §7.5,
# §10.4). Corre con el Python de la imagen instalada (el host puede no tener curl, wget ni Python), con -I:
#
#   python3 -I estado-agente.py --puerto N --accion estado|mantenimiento|salir-mantenimiento|version
#           [--ttl 300] [--fixture <state.json>]
#   python3 -I estado-agente.py --autoprueba
#
# Salida por stdout, una CLAVE=valor por línea (bash la lee con lista blanca):
#   estado               LIBRE=1|0, MOTIVOS=<kinds separados por coma>, VERSION=
#                        exit 3 + INALCANZABLE=1 SOLO si nadie escucha en el puerto (conexión rechazada);
#                        exit 4 + ERROR_AGENTE=<motivo> si el agente acepta la conexión pero no contesta bien
#                        (timeout, reset, HTTP != 200, JSON roto)
#   mantenimiento        MANTENIMIENTO=ok|ocupado|error, HTTP=<status>  (POST /agent/maintenance {"ttl_seconds":N})
#                        HTTP=0 SOLO con conexión rechazada (no hay agente: nada que proteger); HTTP=-1 con timeout,
#                        reset o cualquier otro error (puede haber una captura en curso: quien llama no toca nada)
#   salir-mantenimiento  MANTENIMIENTO=fuera                            (DELETE /agent/maintenance)
#   version              VERSION=<version de /health>
# Las requests salen SIN Origin ni Sec-Fetch-Site (el agente solo acepta el mantenimiento así: no desde un navegador).
# --fixture (solo pruebas): el JSON de /agent/state sale de ese archivo en vez de la red.
import json
import re
import socket
import sys
import threading
import urllib.error
import urllib.request

TIMEOUT = 6


def conexion_rechazada(e):
    """True solo si nadie escucha en el puerto (ECONNREFUSED). Un timeout o un reset NO: el agente puede estar vivo y
    lento en medio de una captura (SDD fiscalia-operacion §7.5; review del reintento 1)."""
    if isinstance(e, ConnectionRefusedError):
        return True
    return isinstance(e, urllib.error.URLError) and isinstance(getattr(e, "reason", None), ConnectionRefusedError)


def motivo_error(e):
    if isinstance(e, urllib.error.URLError) and not isinstance(e, urllib.error.HTTPError) and getattr(e, "reason", None) is not None:
        e = e.reason
    if isinstance(e, (TimeoutError, socket.timeout)):
        return "timeout"
    if isinstance(e, ConnectionResetError):
        return "reset"
    if isinstance(e, ValueError):
        return "respuesta_invalida"
    return limpio(type(e).__name__.lower())


def limpio(v):
    return re.sub(r"[^A-Za-z0-9._,:+-]", "_", str(v))[:200]


def libre(estado):
    """(libre, motivos): libre solo sin nada en curso (D3=A, D-T7)."""
    motivos = []

    def kinds(lista, por_defecto):
        for x in lista or []:
            k = x.get("kind") if isinstance(x, dict) else None
            motivos.append(k if isinstance(k, str) and k else por_defecto)

    if not isinstance(estado, dict):
        return False, ["estado_invalido"]
    if estado.get("maintenance") is not False:
        motivos.append("maintenance")
    ops = estado.get("operations")
    if not isinstance(ops, list):
        motivos.append("operations_invalido")
    else:
        kinds(ops, "operation")
    if estado.get("busy") is not False and not (isinstance(ops, list) and ops):
        motivos.append("busy")
    bg = estado.get("background_operations", [])
    if not isinstance(bg, list):
        motivos.append("background_invalido")
    else:
        kinds(bg, "background_operation")
    rec = estado.get("recordings", [])
    if not isinstance(rec, list) or rec:
        motivos.append("recording")
    if estado.get("recording_session") is not None:
        motivos.append("recording_session")
    unc = estado.get("unclaimed_recordings", [])
    if not isinstance(unc, list) or unc:
        motivos.append("unclaimed_recording")
    vistos = []
    for m in motivos:
        if m not in vistos:
            vistos.append(m)
    return not vistos, vistos


def pedir(puerto, metodo, ruta, cuerpo=None):
    """(status, texto). Lanza OSError si no hay conexión."""
    datos = None
    encabezados = {"Accept": "application/json"}
    if cuerpo is not None:
        datos = json.dumps(cuerpo).encode()
        encabezados["Content-Type"] = "application/json"
    req = urllib.request.Request(f"http://127.0.0.1:{puerto}{ruta}", data=datos, method=metodo, headers=encabezados)
    abridor = urllib.request.build_opener(urllib.request.ProxyHandler({}))  # nunca por un proxy del entorno
    try:
        with abridor.open(req, timeout=TIMEOUT) as r:
            return r.status, r.read(1024 * 1024).decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        return e.code, e.read(1024 * 1024).decode("utf-8", "replace")


def leer_estado(puerto, fixture):
    if fixture:
        with open(fixture, encoding="utf-8") as f:
            return json.load(f)
    status, texto = pedir(puerto, "GET", "/agent/state")
    if status != 200:
        raise ValueError(f"/agent/state respondió {status}")
    return json.loads(texto)


def accion(puerto, que, ttl, fixture, out):
    if que == "estado":
        try:
            estado = leer_estado(puerto, fixture)
        except (OSError, ValueError) as e:
            if conexion_rechazada(e):
                out.append("INALCANZABLE=1")
                return 3
            out.append(f"ERROR_AGENTE={motivo_error(e)}")
            return 4
        es_libre, motivos = libre(estado)
        out.append(f"LIBRE={1 if es_libre else 0}")
        out.append(f"MOTIVOS={limpio(','.join(motivos))}")
        out.append(f"VERSION={limpio(estado.get('version', '')) if isinstance(estado, dict) else ''}")
        return 0
    if que == "mantenimiento":
        if fixture:  # pruebas: el resultado sale del estado simulado
            try:
                es_libre, _ = libre(leer_estado(puerto, fixture))
            except (OSError, ValueError) as e:
                out += ["MANTENIMIENTO=error", "HTTP=0" if conexion_rechazada(e) else "HTTP=-1"]
                return 3
            out += ["MANTENIMIENTO=ok", "HTTP=200"] if es_libre else ["MANTENIMIENTO=ocupado", "HTTP=409"]
            return 0
        try:
            status, _ = pedir(puerto, "POST", "/agent/maintenance", {"ttl_seconds": ttl})
        except OSError as e:
            # Solo "nadie escucha" habilita a cambiar sin mantenimiento; timeout/reset/otro → HTTP=-1 (no tocar nada).
            out += ["MANTENIMIENTO=error", "HTTP=0" if conexion_rechazada(e) else "HTTP=-1"]
            return 3
        out.append("MANTENIMIENTO=" + ("ok" if status == 200 else "ocupado" if status == 409 else "error"))
        out.append(f"HTTP={status}")
        return 0
    if que == "salir-mantenimiento":
        if not fixture:
            try:
                pedir(puerto, "DELETE", "/agent/maintenance")
            except OSError:
                pass
        out.append("MANTENIMIENTO=fuera")
        return 0
    if que == "version":
        try:
            status, texto = pedir(puerto, "GET", "/health")
            v = json.loads(texto).get("version", "") if status == 200 else ""
        except (OSError, ValueError, AttributeError):
            out.append("INALCANZABLE=1")
            return 3
        if not v:
            out.append("INALCANZABLE=1")
            return 3
        out.append(f"VERSION={limpio(v)}")
        return 0
    raise ValueError(que)


# ── Autoprueba: fixtures + un agente falso en 127.0.0.1 (funciona con --network none) ──────────────────────────────
def autoprueba():
    import http.server
    import os
    import tempfile

    fallos = 0

    def chequear(desc, cond, extra=""):
        nonlocal fallos
        if cond:
            print(f"OK    {desc}")
        else:
            fallos += 1
            print(f"FALLA {desc} {extra}")

    base = {
        "version": "1.5.0", "mode": "real", "busy": False, "maintenance": False, "operations": [],
        "background_operations": [], "recordings": [], "unclaimed_recordings": [], "session_recovery": "done",
    }
    casos = [
        ("libre", base, True, ""),
        ("operación en curso", dict(base, busy=True, operations=[{"kind": "android_extraction", "since": "x"}]), False, "android_extraction"),
        ("posproceso de video", dict(base, background_operations=[{"kind": "video_postprocess", "since": "x"}]), False, "video_postprocess"),
        ("grabación en curso", dict(base, recordings=[{"platform": "android", "serial": "s", "state": "recording"}]), False, "recording"),
        ("sesión grabándose", dict(base, recording_session={"case_id": "c"}), False, "recording_session"),
        ("grabación sin reclamar", dict(base, unclaimed_recordings=[{"platform": "ios", "serial": "s", "filename": "f"}]), False, "unclaimed_recording"),
        ("en mantenimiento", dict(base, maintenance=True), False, "maintenance"),
        ("busy sin operaciones", dict(base, busy=True), False, "busy"),
        ("sin campos opcionales", {"version": "1.0.0", "busy": False, "maintenance": False, "operations": []}, True, ""),
        ("operations ausente", {"version": "1.0.0", "busy": False, "maintenance": False}, False, "operations_invalido"),
    ]
    with tempfile.TemporaryDirectory() as d:
        for desc, estado, esperado, motivo in casos:
            fx = os.path.join(d, "estado.json")
            with open(fx, "w", encoding="utf-8") as f:
                json.dump(estado, f)
            out = []
            rc = accion(1, "estado", 300, fx, out)
            linea = dict(x.split("=", 1) for x in out)
            chequear(f"estado: {desc} → LIBRE={linea.get('LIBRE')} MOTIVOS={linea.get('MOTIVOS')}",
                     rc == 0 and linea.get("LIBRE") == ("1" if esperado else "0") and motivo in linea.get("MOTIVOS", ""), str(out))
            out = []
            accion(1, "mantenimiento", 300, fx, out)
            chequear(f"mantenimiento simulado: {desc} → {out[0]}", out[0] == ("MANTENIMIENTO=ok" if esperado else "MANTENIMIENTO=ocupado"))

    # Agente falso: registra los encabezados y cuerpos recibidos.
    recibido = []
    respuestas = {"maintenance": 200}

    class H(http.server.BaseHTTPRequestHandler):
        def log_message(self, *a):
            pass

        def _resp(self, code, obj):
            b = json.dumps(obj).encode()
            self.send_response(code)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(b)))
            self.end_headers()
            self.wfile.write(b)

        def do_GET(self):
            recibido.append(("GET", self.path, dict(self.headers), b""))
            if self.path == "/agent/state":
                self._resp(200, dict(base, operations=[{"kind": "ios_backup", "since": "x"}], busy=True))
            elif self.path == "/health":
                self._resp(200, {"status": "ok", "version": "1.5.0"})
            else:
                self._resp(404, {})

        def do_POST(self):
            n = int(self.headers.get("Content-Length") or 0)
            recibido.append(("POST", self.path, dict(self.headers), self.rfile.read(n)))
            self._resp(respuestas["maintenance"], {"maintenance": True} if respuestas["maintenance"] == 200 else {"code": "agent_busy"})

        def do_DELETE(self):
            recibido.append(("DELETE", self.path, dict(self.headers), b""))
            self.send_response(204)
            self.end_headers()

    srv = http.server.HTTPServer(("127.0.0.1", 0), H)
    puerto = srv.server_address[1]
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    try:
        out = []
        accion(puerto, "estado", 300, None, out)
        chequear(f"estado por HTTP: {out}", out[:2] == ["LIBRE=0", "MOTIVOS=ios_backup"] and out[2] == "VERSION=1.5.0")
        out = []
        accion(puerto, "mantenimiento", 300, None, out)
        post = [r for r in recibido if r[0] == "POST"][-1]
        chequear(f"mantenimiento por HTTP: {out}", out == ["MANTENIMIENTO=ok", "HTTP=200"])
        chequear("POST /agent/maintenance con {\"ttl_seconds\":300}", post[1] == "/agent/maintenance" and json.loads(post[3]) == {"ttl_seconds": 300})
        respuestas["maintenance"] = 409
        out = []
        accion(puerto, "mantenimiento", 120, None, out)
        chequear(f"mantenimiento ocupado (409): {out}", out == ["MANTENIMIENTO=ocupado", "HTTP=409"])
        respuestas["maintenance"] = 500
        out = []
        accion(puerto, "mantenimiento", 120, None, out)
        chequear(f"mantenimiento con error (500): {out}", out == ["MANTENIMIENTO=error", "HTTP=500"])
        out = []
        accion(puerto, "salir-mantenimiento", 0, None, out)
        chequear(f"salir de mantenimiento: {out}", out == ["MANTENIMIENTO=fuera"] and recibido[-1][:2] == ("DELETE", "/agent/maintenance"))
        out = []
        accion(puerto, "version", 0, None, out)
        chequear(f"versión por /health: {out}", out == ["VERSION=1.5.0"])
        sin_origen = all(not any(k.lower() in ("origin", "sec-fetch-site") for k in r[2]) for r in recibido)
        chequear("ninguna request lleva Origin ni Sec-Fetch-Site", sin_origen)
    finally:
        srv.shutdown()
        srv.server_close()
    out = []
    rc = accion(puerto, "estado", 300, None, out)
    chequear(f"agente apagado → exit 3 {out}", rc == 3 and out == ["INALCANZABLE=1"])
    out = []
    accion(puerto, "mantenimiento", 300, None, out)
    chequear(f"mantenimiento con el agente apagado (conexión rechazada) → HTTP=0 {out}", out == ["MANTENIMIENTO=error", "HTTP=0"])

    # Agente vivo pero colgado (acepta la conexión y no contesta, p. ej. disco saturado durante una captura): NUNCA
    # puede parecer "apagado" (HTTP=0 habilitaría el cambio de contenedor sin mantenimiento).
    global TIMEOUT
    viejo_timeout, TIMEOUT = TIMEOUT, 1
    mudo = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    mudo.bind(("127.0.0.1", 0))
    mudo.listen(8)
    puerto_mudo = mudo.getsockname()[1]
    try:
        out = []
        rc = accion(puerto_mudo, "mantenimiento", 300, None, out)
        chequear(f"agente colgado: mantenimiento → HTTP=-1, nunca 0 {out}", out == ["MANTENIMIENTO=error", "HTTP=-1"])
        out = []
        rc = accion(puerto_mudo, "estado", 300, None, out)
        chequear(f"agente colgado: estado → exit 4 ERROR_AGENTE=timeout (no INALCANZABLE) {out}", rc == 4 and out == ["ERROR_AGENTE=timeout"])
        out = []
        rc = accion(puerto_mudo, "version", 0, None, out)
        chequear(f"agente colgado: version → no responde {out}", rc == 3 and out == ["INALCANZABLE=1"])
    finally:
        mudo.close()
        TIMEOUT = viejo_timeout

    # Agente que corta la conexión apenas la acepta (reset): tampoco es "apagado".
    corta = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    corta.bind(("127.0.0.1", 0))
    corta.listen(8)
    puerto_corta = corta.getsockname()[1]

    def cortar():
        import struct
        for _ in range(2):
            try:
                c, _a = corta.accept()
                c.setsockopt(socket.SOL_SOCKET, socket.SO_LINGER, struct.pack("ii", 1, 0))
                c.close()
            except OSError:
                return

    hilo = threading.Thread(target=cortar, daemon=True)
    hilo.start()
    try:
        out = []
        accion(puerto_corta, "mantenimiento", 300, None, out)
        chequear(f"agente que corta la conexión: mantenimiento → HTTP=-1 {out}", out == ["MANTENIMIENTO=error", "HTTP=-1"])
        out = []
        rc = accion(puerto_corta, "estado", 300, None, out)
        chequear(f"agente que corta la conexión: estado → exit 4 {out}", rc == 4 and out[0].startswith("ERROR_AGENTE="))
    finally:
        corta.close()
    print(f"RESULTADO={'ok' if fallos == 0 else 'falla'}")
    return 0 if fallos == 0 else 1


def main(argv):
    if argv == ["--autoprueba"]:
        return autoprueba()
    puerto, que, ttl, fixture = None, None, 300, None
    i = 0
    while i < len(argv):
        a = argv[i]
        if a in ("--puerto", "--accion", "--ttl", "--fixture") and i + 1 < len(argv):
            v = argv[i + 1]
            if a == "--puerto":
                puerto = v
            elif a == "--accion":
                que = v
            elif a == "--ttl":
                ttl = v
            else:
                fixture = v
            i += 2
        else:
            sys.stderr.write(f"estado-agente.py: argumento inválido: {a}\n")
            return 2
    if not (puerto and puerto.isdigit() and 1 <= int(puerto) <= 65535):
        sys.stderr.write("estado-agente.py: --puerto N (1-65535)\n")
        return 2
    if que not in ("estado", "mantenimiento", "salir-mantenimiento", "version"):
        sys.stderr.write("estado-agente.py: --accion estado|mantenimiento|salir-mantenimiento|version\n")
        return 2
    if not str(ttl).isdigit():
        sys.stderr.write("estado-agente.py: --ttl N\n")
        return 2
    out = []
    rc = accion(int(puerto), que, int(ttl), fixture, out)
    sys.stdout.write("".join(f"{x}\n" for x in out))
    return rc


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
