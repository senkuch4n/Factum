#!/usr/bin/env python3
"""Construye (o verifica) plantilla_informe_v4.docx a partir de la plantilla del usuario.

Uso:
    python3 ops/plantilla/build_plantilla_v4.py <origen.docx> <destino.docx>
    python3 ops/plantilla/build_plantilla_v4.py --check <origen.docx> <destino.docx>

Ver ops/plantilla/README.md y Refactorizaciones/informe-pericial-de-parte.md (Anexo A).

Reglas que este script respeta (no romperlas al editarlo):
  * Edita el XML como texto, ubicando cada parrafo por su w14:paraId. Antes de tocar nada
    comprueba una huella NO sensible de cada ancla; si no coincide, aborta.
  * NUNCA escribe ni imprime el texto original de los parrafos 7C035883 y 74B641B2 (traen
    datos reales escritos a mano): los reconstruye desde cero.
  * Cada placeholder va en su propio run y en un solo <w:t>.
  * Salida determinista: [Content_Types].xml primero, ZIP_DEFLATED y fecha fija.
  * --check imprime solo "OK" o lineas "FALLA:<tipo>", nunca el texto que fallo.

Solo usa la biblioteca estandar de Python 3.
"""

import argparse
import hashlib
import re
import sys
import xml.etree.ElementTree as ET
import zipfile

W_NS = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
REL_HEADER = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header"
CT_HEADER = "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"
FIXED_DATE = (1980, 1, 1, 0, 0, 0)

TNR = ('<w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman" '
       'w:eastAsia="Times New Roman" w:cs="Times New Roman"/>')
COURIER = ('<w:rFonts w:ascii="Courier New" w:hAnsi="Courier New" '
           'w:eastAsia="Courier New" w:cs="Courier New"/>')

SENSITIVE_ORGANISMO = "7C035883"
SENSITIVE_PRESENTACION = "74B641B2"

# ── Conjunto esperado de placeholders (A7.1) ────────────────────────────────────
BODY_PLACEHOLDERS = {
    "{nombreTribunal}", "{organismoTribunal}", "{nombrePerito}", "{matriculaPerito}",
    "{profesionPerito}", "{caracterPerito}", "{fraseDomicilio}", "{elSuscripto}",
    "{tipoCausa}", "{numeroCausa}", "{parteDenunciada}", "{tramiteAnte}",
    "{fraseIntegracion}", "{caratula}", "{parteDenunciante}", "{objetoCausa}",
    "{fechaIntervencion}", "{objetoInforme}", "{datosProponente}", "{ambitoCausa}",
    "{fechaInspeccion}", "{horaInspeccion}", "{tipoDispositivo}",
    "{marcaModeloDispositivo}", "{imeiDispositivo}", "{lineaDispositivo}",
    "{titularDispositivo}",
    "{capturasImeiModelo}", "{capturasNombreDispositivo}", "{anexoCapturas}",
    "{descripcionOperacionesRealizadas}", "{descripcionAseguramientoEvidencia}",
    "{descripcionResultados}", "{descripcionValoracionTecnica}",
    "{descripcionConclusiones}", "{descripcionNotasTecnicas}", "{descripcionReserva}",
    "{nombreArchivo}", "{hashArchivo}",
}
BODY_BLOCKS = {"organismoTribunal", "objetoInforme", "capturasNombreDispositivo",
               "descripcionNotasTecnicas", "descripcionReserva", "anexoCapturas"}
HEADER_PLACEHOLDERS = {"{LOGO_ORGANIZACION:5x1.5}", "{ORGANIZACION}", "{CONTACTO}"}
HEADER_BLOCKS = {"MEMBRETE"}

PARA_RE = re.compile(r'<w:p\b[^>]*?(?<!/)>.*?</w:p>', re.S)
T_RE = re.compile(r'<w:t(?:\s[^>]*)?>([^<]*)</w:t>')
PH_RE = re.compile(r'\{[^{}<>]+\}')


class Falla(Exception):
    pass


# ── Utilidades de XML como texto ────────────────────────────────────────────────

def esc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def unesc(s):
    return (s.replace("&lt;", "<").replace("&gt;", ">").replace("&quot;", '"')
             .replace("&apos;", "'").replace("&amp;", "&"))


def para_text(p):
    return unesc("".join(T_RE.findall(p)))


def para_id(p):
    m = re.match(r'<w:p\b[^>]*w14:paraId="([0-9A-Fa-f]{8})"', p)
    return m.group(1).upper() if m else None


def open_tag(p):
    return re.match(r'<w:p\b[^>]*>', p).group(0)


def get_ppr(p):
    m = re.search(r'<w:pPr>.*?</w:pPr>', p, re.S)
    return m.group(0) if m else ""


def runs_of(p):
    return re.findall(r'<w:r>.*?</w:r>', p, re.S)


def rpr_of(run):
    m = re.search(r'<w:rPr>.*?</w:rPr>', run, re.S)
    return m.group(0) if m else ""


def clean_rpr(rp):
    """rPr base: sin negrita, sin idioma forzado (en-US) y sin w:hint."""
    rp = rp.replace("<w:b/>", "").replace("<w:bCs/>", "")
    rp = re.sub(r'<w:lang\b[^>]*/>', "", rp)
    rp = rp.replace(' w:hint="default"', "")
    if not rp or "<w:rFonts" not in rp:
        inner = rp[len("<w:rPr>"):-len("</w:rPr>")] if rp else ""
        rp = "<w:rPr>" + TNR + inner + "</w:rPr>"
    return rp


def bold_rpr(rp):
    return re.sub(r'(<w:rFonts\b[^>]*/>)', r'\1<w:b/><w:bCs/>', rp, count=1)


def base_rpr(p):
    runs = runs_of(p)
    for r in runs:
        rp = rpr_of(r)
        if "<w:b/>" not in rp:
            return clean_rpr(rp)
    return clean_rpr(rpr_of(runs[0]) if runs else "")


def build_runs(markup, rp):
    """`**x**` = negrita. Cada placeholder `{...}` queda en su propio run."""
    out = []
    for i, seg in enumerate(markup.split("**")):
        r = bold_rpr(rp) if i % 2 == 1 else rp
        for piece in re.split(r'(\{[^{}]+\})', seg):
            if piece:
                out.append('<w:r>%s<w:t xml:space="preserve">%s</w:t></w:r>' % (r, esc(piece)))
    return "".join(out)


def set_jc(ppr, val):
    if "<w:jc " in ppr:
        return re.sub(r'<w:jc w:val="[^"]*"/>', '<w:jc w:val="%s"/>' % val, ppr)
    if "<w:rPr>" in ppr:
        return ppr.replace("<w:rPr>", '<w:jc w:val="%s"/><w:rPr>' % val, 1)
    return ppr.replace("</w:pPr>", '<w:jc w:val="%s"/></w:pPr>' % val)


class Ids:
    """paraIds nuevos, deterministas y unicos (< 0x80000000, como exige Word)."""

    def __init__(self, used):
        self.used = set(used)

    def new(self, seed):
        s = seed
        while True:
            v = int(hashlib.sha256(s.encode()).hexdigest()[:8], 16) & 0x7FFFFFFF
            pid = "%08X" % v
            if v != 0 and pid not in self.used:
                self.used.add(pid)
                return pid
            s += "#"


# ── Construccion ───────────────────────────────────────────────────────────────

def fingerprint(paras, order):
    """Huellas no sensibles de las anclas. Nunca se imprime el texto."""
    def txt(pid):
        if pid not in paras:
            raise Falla("ancla " + pid)
        return para_text(paras[pid]).strip()

    checks = {
        "0DF4CF61": lambda t: t.startswith("INFORME PERICIAL"),
        "384E45D4": lambda t: "{tribunal/fiscalia}" in t,
        "27679AFE": lambda t: t.startswith("S____"),
        SENSITIVE_PRESENTACION: lambda t: t.startswith("{usuarioNombreCompleto}"),
        "03940239": lambda t: t.startswith("Que vengo a emitir"),
        "3A17DBEE": lambda t: t.startswith("Carátula:"),
        "2AFB1345": lambda t: t.startswith("Parte denunciante:"),
        "15BB4A06": lambda t: t.startswith("Parte denunciada:"),
        "69BAC851": lambda t: t.startswith("Objeto:"),
        "3DA3ACE8": lambda t: t.startswith("Fecha de intervención:"),
        "577D1792": lambda t: t.startswith("El suscripto manifiesta"),
        "2F541765": lambda t: t.startswith("El presente informe tiene por finalidad"),
        "5596EEF2": lambda t: t == "{objetoModificable}",
        "5F66E0AC": lambda t: t.startswith("Perito de parte:"),
        "08C61010": lambda t: t.startswith("Parte que lo propone:"),
        "3162E6AB": lambda t: t.startswith("Ámbito:"),
        "5FECA2F2": lambda t: t.startswith("En fecha"),
        "14341833": lambda t: t.startswith("Tipo:"),
        "15EF193E": lambda t: t.startswith("Marca y modelo:"),
        "490B4C01": lambda t: t.startswith("IMEI:"),
        "28E6822B": lambda t: t.startswith("Línea:"),
        "2E1C810A": lambda t: t.startswith("Titular:"),
        "26DDBA6A": lambda t: t.startswith("El mismo fue aportado"),
        "07342EEC": lambda t: t == "{capturasDispositivoMostrandoImeiYModelo}",
        "1FD00BD3": lambda t: t == "{capturasMostrandoNombreDispositivo}",
        "46A3AB79": lambda t: t == "{descripcionOperacionesRealizadas}",
        "2D02EAD7": lambda t: t == "{descripcionAseguramientoEvidencia}",
        "04E11EDC": lambda t: t == "NOMBRE",
        "4B698602": lambda t: t == "HASH SHA-256",
        "7B5B2493": lambda t: t == "{nombreArchivo}",
        "729723C5": lambda t: t == "{hashArchivo}",
        "404845D9": lambda t: t == "{descripcionResultados}",
        "10CF937D": lambda t: t == "{descripcionValoracionTecnica}",
        "08A80D56": lambda t: t == "{descripcionConclusiones}",
        "05D9340B": lambda t: t == "NOTAS TÉCNICAS",
        "7290E48F": lambda t: t == "{descripcionNotasTecnicas}",
        "10E394C7": lambda t: t == "RESERVA",
        "516E02D3": lambda t: t == "{descripcionReserva}",
        "4FC2AE2A": lambda t: t.startswith("SE DEJA CONSTANCIA"),
        "21DB9123": lambda t: t == "",
    }
    for pid, ok in checks.items():
        if not ok(txt(pid)):
            raise Falla("ancla " + pid)
    # 7C035883 se identifica por posicion: el parrafo siguiente a 384E45D4.
    i = order.index("384E45D4")
    if i + 1 >= len(order) or order[i + 1] != SENSITIVE_ORGANISMO:
        raise Falla("ancla " + SENSITIVE_ORGANISMO)
    # 21DB9123 es el ultimo parrafo del cuerpo.
    if order[-1] != "21DB9123":
        raise Falla("ancla 21DB9123")


def build_document(doc, ids):
    paras = {}
    order = []
    for m in PARA_RE.finditer(doc):
        pid = para_id(m.group(0))
        if pid:
            paras[pid] = m.group(0)
            order.append(pid)
    fingerprint(paras, order)

    norm_ppr = get_ppr(paras["2D02EAD7"])  # (*) pPr normalizado

    def rebuild(pid, markup, ppr=None, rp=None):
        p = paras[pid]
        return (open_tag(p) + (get_ppr(p) if ppr is None else ppr)
                + build_runs(markup, base_rpr(p) if rp is None else rp) + "</w:p>")

    def marker(text, seed):
        pid = ids.new("marker:" + seed)
        return ('<w:p w14:paraId="%s"><w:pPr><w:spacing w:after="0" w:line="240" '
                'w:lineRule="auto"/><w:rPr>%s</w:rPr></w:pPr><w:r><w:rPr>%s</w:rPr>'
                '<w:t>%s</w:t></w:r></w:p>') % (pid, TNR, TNR, esc(text))

    def simple(seed, markup, jc="center", before=None, bold=False, extra_ppr=""):
        pid = ids.new("a4:" + seed)
        spacing = '<w:spacing%s w:after="0" w:line="360" w:lineRule="auto"/>' % (
            ' w:before="%s"' % before if before else "")
        ppr = '<w:pPr>%s%s<w:jc w:val="%s"/><w:rPr>%s</w:rPr></w:pPr>' % (
            spacing, extra_ppr, jc, TNR)
        rp = "<w:rPr>" + TNR + "</w:rPr>"
        if bold:
            markup = "**" + markup + "**"
        return '<w:p w14:paraId="%s">%s%s</w:p>' % (pid, ppr, build_runs(markup, rp))

    new = {}
    new["384E45D4"] = rebuild("384E45D4", "{nombreTribunal}")
    # Parrafo con datos reales: se reconstruye desde cero (pPr y rPr propios, sin texto original).
    new[SENSITIVE_ORGANISMO] = (
        marker("{#organismoTribunal}", "org-open")
        + rebuild(SENSITIVE_ORGANISMO, "{organismoTribunal}")
        + marker("{/organismoTribunal}", "org-close"))
    new[SENSITIVE_PRESENTACION] = rebuild(
        SENSITIVE_PRESENTACION,
        "{nombrePerito}, M.P. {matriculaPerito}, {profesionPerito}, en mi carácter de "
        "{caracterPerito}{fraseDomicilio}, me presento y respetuosamente digo:")
    new["03940239"] = rebuild(
        "03940239",
        "Que vengo a emitir el presente **Informe Pericial Técnico Informático** en el marco "
        "de la causa {tipoCausa} N° {numeroCausa}, seguida contra {parteDenunciada}, que "
        "tramita ante {tramiteAnte}{fraseIntegracion}, conforme los siguientes datos:")
    new["3A17DBEE"] = rebuild("3A17DBEE", "**Carátula:** {caratula}")
    new["2AFB1345"] = rebuild("2AFB1345", "**Parte denunciante:** {parteDenunciante}")
    new["15BB4A06"] = rebuild("15BB4A06", "**Parte denunciada:** {parteDenunciada}")
    new["69BAC851"] = rebuild("69BAC851", "**Objeto:** {objetoCausa}")
    new["3DA3ACE8"] = rebuild("3DA3ACE8", "**Fecha de intervención:** {fechaIntervencion}")
    new["577D1792"] = rebuild(
        "577D1792",
        "{elSuscripto} manifiesta que el presente informe ha sido elaborado conforme a los "
        "principios de objetividad, independencia de criterio y rigor técnico-científico, "
        "aplicando metodologías reconocidas en el ámbito de la informática forense.")
    new["2F541765"] = rebuild(
        "2F541765",
        "El presente informe tiene por finalidad realizar una **inspección técnica y "
        "documentación del contenido digital** existente en un dispositivo móvil aportado "
        "voluntariamente por su titular, a los fines de su análisis y eventual valoración en "
        "el marco de la actuación en trámite.")
    new["5596EEF2"] = (
        marker("{#objetoInforme}", "obj-open")
        + rebuild("5596EEF2", "{objetoInforme}", ppr=norm_ppr)
        + marker("{/objetoInforme}", "obj-close"))
    new["5F66E0AC"] = rebuild(
        "5F66E0AC",
        "**Perito de parte:** {nombrePerito}, M.P. {matriculaPerito}, {profesionPerito}.")
    new["08C61010"] = rebuild("08C61010", "**Parte que lo propone:** {datosProponente}")
    new["3162E6AB"] = rebuild("3162E6AB", "**Ámbito:** {ambitoCausa}")
    new["5FECA2F2"] = rebuild(
        "5FECA2F2",
        "En fecha {fechaInspeccion}, siendo horas {horaInspeccion}, se procedió a la "
        "inspección técnica del dispositivo:")
    new["14341833"] = rebuild("14341833", "**Tipo:** {tipoDispositivo}")
    new["15EF193E"] = rebuild("15EF193E", "**Marca y modelo:** {marcaModeloDispositivo}")
    new["490B4C01"] = rebuild("490B4C01", "**IMEI:** {imeiDispositivo}")
    new["28E6822B"] = rebuild("28E6822B", "**Línea:** {lineaDispositivo}")
    new["2E1C810A"] = rebuild("2E1C810A", "**Titular:** {titularDispositivo}")
    new["07342EEC"] = rebuild("07342EEC", "{capturasImeiModelo}",
                              ppr=set_jc(get_ppr(paras["07342EEC"]), "center"))
    new["1FD00BD3"] = (
        marker("{#capturasNombreDispositivo}", "nom-open")
        + rebuild("1FD00BD3", "{capturasNombreDispositivo}",
                  ppr=set_jc(get_ppr(paras["1FD00BD3"]), "center"))
        + marker("{/capturasNombreDispositivo}", "nom-close"))
    new["46A3AB79"] = rebuild("46A3AB79", "{descripcionOperacionesRealizadas}", ppr=norm_ppr)
    new["2D02EAD7"] = rebuild("2D02EAD7", "{descripcionAseguramientoEvidencia}", ppr=norm_ppr)
    new["404845D9"] = rebuild("404845D9", "{descripcionResultados}", ppr=norm_ppr)
    new["10CF937D"] = rebuild("10CF937D", "{descripcionValoracionTecnica}", ppr=norm_ppr)
    new["08A80D56"] = rebuild("08A80D56", "{descripcionConclusiones}", ppr=norm_ppr)
    new["05D9340B"] = marker("{#descripcionNotasTecnicas}", "notas-open") + paras["05D9340B"]
    new["7290E48F"] = (rebuild("7290E48F", "{descripcionNotasTecnicas}")
                       + marker("{/descripcionNotasTecnicas}", "notas-close"))
    new["10E394C7"] = marker("{#descripcionReserva}", "res-open") + paras["10E394C7"]
    new["516E02D3"] = (rebuild("516E02D3", "{descripcionReserva}")
                       + marker("{/descripcionReserva}", "res-close"))

    # Celdas de la tabla de hashes (A3, "Tabla").
    def cell_ppr(p, jc=None, sz=None, fonts=None):
        ppr = get_ppr(p).replace('w:line="360"', 'w:line="240"')
        if jc:
            ppr = set_jc(ppr, jc)
        if sz:
            ppr = re.sub(r'<w:sz w:val="\d+"/><w:szCs w:val="\d+"/>',
                         '<w:sz w:val="%s"/><w:szCs w:val="%s"/>' % (sz, sz), ppr)
        if fonts:
            ppr = re.sub(r'<w:rFonts\b[^>]*/>', fonts, ppr)
        return ppr

    def cell_rpr(p, sz, fonts=None):
        rp = base_rpr(p)
        rp = re.sub(r'<w:sz w:val="\d+"/><w:szCs w:val="\d+"/>',
                    '<w:sz w:val="%s"/><w:szCs w:val="%s"/>' % (sz, sz), rp)
        if fonts:
            rp = re.sub(r'<w:rFonts\b[^>]*/>', fonts, rp)
        return rp

    for pid in ("04E11EDC", "4B698602"):
        p = paras[pid]
        new[pid] = open_tag(p) + cell_ppr(p) + "".join(runs_of(p)) + "</w:p>"
    p = paras["7B5B2493"]
    new["7B5B2493"] = rebuild("7B5B2493", "{nombreArchivo}",
                              ppr=cell_ppr(p, jc="left", sz="18"), rp=cell_rpr(p, "18"))
    p = paras["729723C5"]
    new["729723C5"] = rebuild("729723C5", "{hashArchivo}",
                              ppr=cell_ppr(p, jc="left", sz="16", fonts=COURIER),
                              rp=cell_rpr(p, "16", fonts=COURIER))

    # A4: firma y anexo, despues del ultimo parrafo.
    a4 = [
        simple("firma-linea", "______________________________", before="720"),
        simple("firma-nombre", "{nombrePerito}", bold=True),
        simple("firma-prof", "{profesionPerito} – M.P. {matriculaPerito}"),
        simple("firma-caracter", "{caracterPerito}"),
        marker("{#anexoCapturas}", "anexo-open"),
        '<w:p w14:paraId="%s"><w:pPr><w:spacing w:after="0"/></w:pPr><w:r>'
        '<w:br w:type="page"/></w:r></w:p>' % ids.new("a4:salto"),
        simple("anexo-titulo", "ANEXO – CAPTURAS DE PANTALLA", bold=True),
        simple("anexo-texto",
               "Capturas de pantalla obtenidas durante la inspección del dispositivo. Cada "
               "una se identifica con el nombre de su archivo, que figura en la tabla de "
               "valores hash.", jc="both"),
        simple("anexo-ph", "{anexoCapturas}"),
        marker("{/anexoCapturas}", "anexo-close"),
        '<w:p w14:paraId="%s"><w:pPr><w:spacing w:after="0" w:line="240" w:lineRule="auto"/>'
        '<w:rPr><w:sz w:val="2"/><w:szCs w:val="2"/></w:rPr></w:pPr></w:p>' % ids.new("a4:fin"),
    ]
    new["21DB9123"] = paras["21DB9123"] + "".join(a4)

    def sub(m):
        pid = para_id(m.group(0))
        return new.get(pid, m.group(0)) if pid else m.group(0)

    out = PARA_RE.sub(sub, doc)

    # Tabla: grilla 3600/4691, layout fijo, encabezado repetido y filas que no se parten.
    t0 = out.index("<w:tbl>")
    t1 = out.index("</w:tbl>", t0) + len("</w:tbl>")
    tbl = out[t0:t1]
    if tbl.count("<w:tr ") != 2:
        raise Falla("ancla tabla")
    tbl = tbl.replace('<w:gridCol w:w="4153"/><w:gridCol w:w="4138"/>',
                      '<w:gridCol w:w="3600"/><w:gridCol w:w="4691"/>')
    tbl = tbl.replace('<w:tcW w:w="4153" w:type="dxa"/>', '<w:tcW w:w="3600" w:type="dxa"/>')
    tbl = tbl.replace('<w:tcW w:w="4138" w:type="dxa"/>', '<w:tcW w:w="4691" w:type="dxa"/>')
    tbl = tbl.replace('<w:tblLayout w:type="autofit"/>', '<w:tblLayout w:type="fixed"/>')
    rows = list(re.finditer(r'<w:tr\b[^>]*>', tbl))
    head, model = rows[0], rows[1]
    tbl = (tbl[:head.end()] + "<w:trPr><w:tblHeader/></w:trPr>"
           + tbl[head.end():model.end()] + "<w:trPr><w:cantSplit/></w:trPr>"
           + tbl[model.end():])
    if "3600" not in tbl or "4691" not in tbl:
        raise Falla("ancla tabla")
    out = out[:t0] + tbl + out[t1:]

    # sectPr: encabezado + pie a 1 cm del borde.
    if out.count("<w:sectPr>") != 1 or 'w:footer="0"' not in out:
        raise Falla("ancla sectPr")
    out = out.replace('<w:sectPr><w:footerReference',
                      '<w:sectPr><w:headerReference w:type="default" r:id="rIdHdr1"/>'
                      '<w:footerReference', 1)
    out = out.replace('w:footer="0"', 'w:footer="567"', 1)
    if 'r:id="rIdHdr1"' not in out:
        raise Falla("ancla sectPr")
    return out


def build_header(footer_xml, ids):
    root = re.match(r'(<\?xml[^>]*\?>)?\s*(<w:ftr\b[^>]*>)', footer_xml)
    if not root:
        raise Falla("ancla footer1")
    decl = root.group(1) or '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
    hdr_open = root.group(2).replace("<w:ftr", "<w:hdr", 1)
    rp_org = ('<w:rPr>%s<w:b/><w:bCs/><w:sz w:val="20"/><w:szCs w:val="20"/></w:rPr>'
              % TNR)
    rp_contact = ('<w:rPr>%s<w:color w:val="595959"/><w:sz w:val="16"/>'
                  '<w:szCs w:val="16"/></w:rPr>' % TNR)

    def p(seed, inner):
        return '<w:p w14:paraId="%s">%s</w:p>' % (ids.new("hdr:" + seed), inner)

    body = "".join([
        p("open", '<w:r><w:t>{#MEMBRETE}</w:t></w:r>'),
        '<w:tbl><w:tblPr><w:tblW w:w="5000" w:type="pct"/>'
        '<w:tblBorders><w:bottom w:val="single" w:sz="4" w:space="0" w:color="7F7F7F"/>'
        '</w:tblBorders><w:tblLayout w:type="fixed"/>'
        '<w:tblCellMar><w:left w:w="0" w:type="dxa"/><w:right w:w="0" w:type="dxa"/>'
        '</w:tblCellMar></w:tblPr>'
        '<w:tblGrid><w:gridCol w:w="3400"/><w:gridCol w:w="5247"/></w:tblGrid>'
        '<w:tr>'
        '<w:tc><w:tcPr><w:tcW w:w="3400" w:type="dxa"/><w:vAlign w:val="center"/></w:tcPr>',
        p("logo", '<w:pPr><w:spacing w:after="80"/><w:jc w:val="left"/></w:pPr>'
                  '<w:r><w:t>{LOGO_ORGANIZACION:5x1.5}</w:t></w:r>'),
        '</w:tc>'
        '<w:tc><w:tcPr><w:tcW w:w="5247" w:type="dxa"/><w:vAlign w:val="center"/></w:tcPr>',
        p("org", '<w:pPr><w:spacing w:after="0"/><w:jc w:val="right"/></w:pPr>'
                 '<w:r>%s<w:t>{ORGANIZACION}</w:t></w:r>' % rp_org),
        p("contacto", '<w:pPr><w:spacing w:after="80"/><w:jc w:val="right"/></w:pPr>'
                      '<w:r>%s<w:t>{CONTACTO}</w:t></w:r>' % rp_contact),
        '</w:tc></w:tr></w:tbl>',
        p("close", '<w:r><w:t>{/MEMBRETE}</w:t></w:r>'),
        p("fin", '<w:pPr><w:spacing w:after="0" w:line="240" w:lineRule="auto"/>'
                 '<w:rPr><w:sz w:val="2"/><w:szCs w:val="2"/></w:rPr></w:pPr>'),
    ])
    return decl + "\n" + hdr_open + body + "</w:hdr>"


CORE_XML = (
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
    '<cp:coreProperties '
    'xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" '
    'xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" '
    'xmlns:dcmitype="http://purl.org/dc/dcmitype/" '
    'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">'
    '<dcterms:created xsi:type="dcterms:W3CDTF">2026-10-01T00:00:00Z</dcterms:created>'
    '<dc:creator>Factum</dc:creator><cp:lastModifiedBy>Factum</cp:lastModifiedBy>'
    '<dcterms:modified xsi:type="dcterms:W3CDTF">2026-10-01T00:00:00Z</dcterms:modified>'
    '<cp:revision>1</cp:revision></cp:coreProperties>')


def read_parts(path):
    with zipfile.ZipFile(path) as z:
        return [(i.filename, z.read(i.filename)) for i in z.infolist()
                if not i.filename.endswith("/")]


def build(src, dst):
    parts = read_parts(src)
    names = [n for n, _ in parts]
    data = dict(parts)
    for required in ("word/document.xml", "word/footer1.xml", "word/_rels/document.xml.rels",
                     "[Content_Types].xml", "docProps/core.xml"):
        if required not in data:
            raise Falla("parte " + required)

    used = set()
    for n in names:
        if n.endswith(".xml"):
            used.update(x.upper() for x in re.findall(r'w14:paraId="([0-9A-Fa-f]{8})"',
                                                      data[n].decode("utf-8")))
    ids = Ids(used)

    doc = build_document(data["word/document.xml"].decode("utf-8"), ids)
    header = build_header(data["word/footer1.xml"].decode("utf-8"), ids)

    rels = data["word/_rels/document.xml.rels"].decode("utf-8")
    if "rIdHdr1" in rels:
        raise Falla("ancla rels")
    rels = rels.replace("</Relationships>",
                        '<Relationship Id="rIdHdr1" Type="%s" Target="header1.xml"/>'
                        "</Relationships>" % REL_HEADER)

    ct = data["[Content_Types].xml"].decode("utf-8")
    ct = ct.replace("</Types>", '<Override PartName="/word/header1.xml" ContentType="%s"/>'
                    "</Types>" % CT_HEADER)

    data["word/document.xml"] = doc.encode("utf-8")
    data["word/header1.xml"] = header.encode("utf-8")
    data["word/_rels/document.xml.rels"] = rels.encode("utf-8")
    data["[Content_Types].xml"] = ct.encode("utf-8")
    data["docProps/core.xml"] = CORE_XML.encode("utf-8")

    order = ["[Content_Types].xml"] + [n for n in names if n != "[Content_Types].xml"]
    order.insert(order.index("word/footer1.xml"), "word/header1.xml")

    with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as z:
        for n in order:
            info = zipfile.ZipInfo(n, date_time=FIXED_DATE)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 0
            info.external_attr = 0
            z.writestr(info, data[n])


# ── Verificacion (A7) ──────────────────────────────────────────────────────────

def norm(s):
    return re.sub(r"\s+", " ", s).strip().lower()


def placeholders_of(xml):
    """(placeholders enteros en un <w:t>, placeholders del texto concatenado por parrafo)."""
    single, joined = [], []
    for p in re.findall(r'<w:p\b[^>]*?(?<!/)>.*?</w:p>', xml, re.S):
        texts = [unesc(t) for t in T_RE.findall(p)]
        for t in texts:
            single += PH_RE.findall(t)
        joined += PH_RE.findall("".join(texts))
    return single, joined


def check_placeholders(xml, expected, blocks, fails, label):
    single, joined = placeholders_of(xml)
    if sorted(single) != sorted(joined):
        fails.add("FALLA:placeholder-partido")
    plain = {p for p in single if not p.startswith(("{#", "{/"))}
    if plain - expected:
        fails.add("FALLA:placeholder-desconocido")
    if expected - plain:
        fails.add("FALLA:placeholder-faltante-" + label)
    opens = [p[2:-1] for p in single if p.startswith("{#")]
    closes = [p[2:-1] for p in single if p.startswith("{/")]
    for b in blocks:
        if opens.count(b) != 1 or closes.count(b) != 1:
            fails.add("FALLA:bloque-" + label)
    if (set(opens) | set(closes)) - blocks:
        fails.add("FALLA:placeholder-desconocido")


def check(src, dst):
    fails = set()
    s = dict(read_parts(src))
    d = dict(read_parts(dst))

    # Datos sensibles del origen, solo en memoria.
    sdoc = s["word/document.xml"].decode("utf-8")
    sparas = {para_id(m.group(0)): m.group(0) for m in PARA_RE.finditer(sdoc)}
    secrets = []
    if SENSITIVE_ORGANISMO in sparas:
        secrets.append(para_text(sparas[SENSITIVE_ORGANISMO]).strip())
    if SENSITIVE_PRESENTACION in sparas:
        t = para_text(sparas[SENSITIVE_PRESENTACION])
        m = re.search(r"domicilio constituido en (.*?), me presento", t, re.S)
        if m:
            secrets.append(m.group(1).strip())
    score = s.get("docProps/core.xml", b"").decode("utf-8")
    for tag in ("dc:creator", "cp:lastModifiedBy"):
        m = re.search(r"<%s>([^<]*)</%s>" % (tag, tag), score)
        if m and m.group(1).strip():
            secrets.append(unesc(m.group(1)).strip())
    secrets = [norm(x) for x in secrets if norm(x)]

    xml_parts = {n: v.decode("utf-8", "replace") for n, v in d.items()
                 if n.endswith(".xml") or n.endswith(".rels")}

    # 5. XML bien formado.
    for n, x in xml_parts.items():
        try:
            ET.fromstring(x.encode("utf-8"))
        except ET.ParseError:
            fails.add("FALLA:xml-mal-formado")

    # 2. Sin datos reales (en el XML crudo y en el texto sin etiquetas).
    for x in xml_parts.values():
        hay = norm(unesc(x)) + "\n" + norm(unesc(re.sub(r"<[^>]+>", "", x)))
        if any(sec in hay for sec in secrets):
            fails.add("FALLA:dato-real")

    # 3. core.xml.
    core = xml_parts.get("docProps/core.xml", "")
    for tag in ("dc:creator", "cp:lastModifiedBy"):
        m = re.search(r"<%s>([^<]*)</%s>" % (tag, tag), core)
        if not m or m.group(1) != "Factum":
            fails.add("FALLA:core")

    # 1. Placeholders.
    doc = xml_parts.get("word/document.xml", "")
    hdr = xml_parts.get("word/header1.xml", "")
    check_placeholders(doc, BODY_PLACEHOLDERS, BODY_BLOCKS, fails, "cuerpo")
    check_placeholders(hdr, HEADER_PLACEHOLDERS, HEADER_BLOCKS, fails, "header")
    for n, x in xml_parts.items():
        if re.match(r"word/(header|footer)\d*\.xml$", n) and n != "word/header1.xml":
            single, joined = placeholders_of(x)
            if single or joined:
                fails.add("FALLA:placeholder-desconocido")

    # 4. Estructura.
    sect = re.search(r"<w:sectPr\b.*?</w:sectPr>", doc, re.S)
    if (not sect or '<w:headerReference w:type="default" r:id="rIdHdr1"/>' not in sect.group(0)
            or 'w:footer="567"' not in sect.group(0)):
        fails.add("FALLA:sectPr")
    rels = xml_parts.get("word/_rels/document.xml.rels", "")
    if not re.search(r'Id="rIdHdr1"[^>]*Target="header1\.xml"', rels) or REL_HEADER not in rels:
        fails.add("FALLA:relacion-header")
    ct = xml_parts.get("[Content_Types].xml", "")
    if '<Override PartName="/word/header1.xml" ContentType="%s"/>' % CT_HEADER not in ct:
        fails.add("FALLA:content-type-header")
    # La copia de compatibilidad (mc:Fallback) repite a proposito los paraId de su mc:Choice
    # (el numero de pagina de footer1.xml viene asi de origen): no cuenta como repeticion.
    all_ids = []
    for x in xml_parts.values():
        x = re.sub(r"<mc:Fallback\b.*?</mc:Fallback>", "", x, flags=re.S)
        all_ids += [i.upper() for i in re.findall(r'w14:paraId="([0-9A-Fa-f]{8})"', x)]
    if len(all_ids) != len(set(all_ids)):
        fails.add("FALLA:paraid-repetido")
    if any("w:author" in x for x in xml_parts.values()):
        fails.add("FALLA:autor")

    return sorted(fails)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--check", action="store_true", help="verifica en lugar de construir")
    ap.add_argument("origen")
    ap.add_argument("destino")
    a = ap.parse_args()
    try:
        if a.check:
            fails = check(a.origen, a.destino)
            if fails:
                print("\n".join(fails))
                return 1
            print("OK")
            return 0
        build(a.origen, a.destino)
        fails = check(a.origen, a.destino)
        if fails:
            print("\n".join(fails))
            return 1
        print("OK")
        return 0
    except Falla as e:
        print("FALLA: " + str(e))
        return 1
    except (OSError, zipfile.BadZipFile, KeyError):
        print("FALLA: lectura")
        return 1


if __name__ == "__main__":
    sys.exit(main())
