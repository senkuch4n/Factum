#!/usr/bin/env python3
"""Construye (o verifica) plantilla_informe_v5.docx a partir de plantilla_informe_v4.docx.

Uso:
    python3 ops/plantilla/build_plantilla_v5.py <v4.docx> <v5.docx>
    python3 ops/plantilla/build_plantilla_v5.py --check <v4.docx> <v5.docx>

Ver ops/plantilla/README.md y Refactorizaciones/informe-diseno-modelo.md (secciones 5 y 8.1).

La v5 es la v4 con el diseño del modelo: portada como seccion propia (franja y remate en
header2, identidad del estudio en footer2), banda en las paginas interiores (header1),
"Pagina N de M" (footer1), titulos en Century Gothic, bloques de datos en dos columnas, tabla
de hashes con encabezado sombreado y cuerpo en Arial. El TEXTO pericial no cambia.

Reglas que este script respeta (no romperlas al editarlo):
  * Parte SOLO de la v4 versionada (ya sin datos reales): no lee la plantilla de docs/.
  * Edita el XML como texto, ubicando cada parrafo por su w14:paraId. Antes de tocar nada
    comprueba una huella no sensible de cada ancla; si no coincide, aborta.
  * Los colores configurables van como CENTINELAS (2F3B4C primario, 9AA5B1 acento), que
    ReportService reemplaza al generar (B-R2b). Si la v4 ya trae alguno, aborta.
  * Cada placeholder va en su propio run y en un solo <w:t>.
  * Salida determinista: [Content_Types].xml primero, ZIP_DEFLATED y fecha fija.
  * --check imprime solo "OK" o lineas "FALLA:<tipo>".

Solo usa la biblioteca estandar de Python 3.
"""

import argparse
import os
import re
import sys
import xml.etree.ElementTree as ET
import zipfile

sys.dont_write_bytecode = True  # no dejar __pycache__ en ops/plantilla/
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_plantilla_v4 as v4  # noqa: E402  (helpers compartidos: Ids, read_parts, ...)

Falla = v4.Falla
esc = v4.esc
para_id = v4.para_id
para_text = v4.para_text
PARA_RE = v4.PARA_RE
Ids = v4.Ids

REL_HEADER = v4.REL_HEADER
REL_FOOTER = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer"
CT_HEADER = v4.CT_HEADER
CT_FOOTER = "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"
FIXED_DATE = v4.FIXED_DATE

PRIMARY = "2F3B4C"   # centinela del color primario (= BrandingColors.DefaultPrimary)
ACCENT = "9AA5B1"    # centinela del color de acento (= BrandingColors.DefaultAccent)

ARIAL = '<w:rFonts w:ascii="Arial" w:hAnsi="Arial" w:eastAsia="Arial" w:cs="Arial"/>'
CG = ('<w:rFonts w:ascii="Century Gothic" w:hAnsi="Century Gothic" '
      'w:eastAsia="Century Gothic" w:cs="Century Gothic"/>')

# A4 en EMU
PAGE_W = 7560310
PAGE_H = 10692130

# ── Placeholders esperados por parte (B8) ──────────────────────────────────────
BODY_PLACEHOLDERS = set(v4.BODY_PLACEHOLDERS) | {"{ISOTIPO_ORGANIZACION:2x2}"}
BODY_BLOCKS = set(v4.BODY_BLOCKS) | {"ISOTIPO"}
HEADER1_PLACEHOLDERS = {"{tipoCausa}", "{numeroCausa}", "{ISOTIPO_ORGANIZACION:2.4x1.6}",
                        "{ORGANIZACION}"}
HEADER1_BLOCKS = {"ISOTIPO", "NOMBRE_EN_BANDA"}
FOOTER2_PLACEHOLDERS = {"{LOGO_ORGANIZACION:7x3.8}", "{ORGANIZACION}", "{CONTACTO}"}
FOOTER2_BLOCKS = {"MEMBRETE"}

SECTION_TITLES = ["18A07FBA", "0BF0C8CD", "5B3056CF", "0FF62E21", "3A62DE0D", "37BC776D",
                  "5A06ED76", "390F7DAB", "05D9340B", "10E394C7"]
BLOCK_REFERENCIA = ("3B831F98", None, ["3A17DBEE", "2AFB1345", "15BB4A06", "69BAC851",
                                       "3DA3ACE8"], "236C9BC4")
BLOCK_IDENTIFICACION = ("0386A4A4", None, ["5F66E0AC", "08C61010", "3162E6AB"], "3A37F215")
BLOCK_ELEMENTOS = ("0F4EDFCE", "5FECA2F2", ["14341833", "15EF193E", "490B4C01", "28E6822B",
                                           "2E1C810A"], None)
FIRMA_CARACTER = "4F0EBB3D"
ANEXO_OPEN = "16E02A15"

# ── Orden de los hijos (esquema WordprocessingML) ──────────────────────────────
PPR_ORDER = ["w:pStyle", "w:keepNext", "w:keepLines", "w:pageBreakBefore", "w:framePr",
             "w:widowControl", "w:numPr", "w:suppressLineNumbers", "w:pBdr", "w:shd", "w:tabs",
             "w:suppressAutoHyphens", "w:kinsoku", "w:wordWrap", "w:overflowPunct",
             "w:topLinePunct", "w:autoSpaceDE", "w:autoSpaceDN", "w:bidi", "w:adjustRightInd",
             "w:snapToGrid", "w:spacing", "w:ind", "w:contextualSpacing", "w:mirrorIndents",
             "w:suppressOverlap", "w:jc", "w:textDirection", "w:textAlignment",
             "w:textboxTightWrap", "w:outlineLvl", "w:divId", "w:cnfStyle", "w:rPr",
             "w:sectPr", "w:pPrChange"]
RPR_ORDER = ["w:rStyle", "w:rFonts", "w:b", "w:bCs", "w:i", "w:iCs", "w:caps", "w:smallCaps",
             "w:strike", "w:dstrike", "w:outline", "w:shadow", "w:emboss", "w:imprint",
             "w:noProof", "w:snapToGrid", "w:vanish", "w:webHidden", "w:color", "w:spacing",
             "w:w", "w:kern", "w:position", "w:sz", "w:szCs", "w:highlight", "w:u", "w:effect",
             "w:bdr", "w:shd", "w:fitText", "w:vertAlign", "w:rtl", "w:cs", "w:em", "w:lang",
             "w:eastAsianLayout", "w:specVanish", "w:oMath"]

TAG_RE = re.compile(r'<(/?)([\w:]+)(?:\s[^>]*?)?(/?)>')


def children(inner):
    """Hijos de primer nivel de un fragmento XML: lista de (nombre, xml)."""
    out, depth, start, name = [], 0, 0, None
    for m in TAG_RE.finditer(inner):
        closing, tag, selfc = m.group(1), m.group(2), m.group(3)
        if depth == 0:
            if closing:
                raise Falla("xml-hijos")
            start, name = m.start(), tag
            if selfc:
                out.append((tag, inner[start:m.end()]))
            else:
                depth = 1
            continue
        if closing:
            depth -= 1
        elif not selfc:
            depth += 1
        if depth == 0:
            out.append((name, inner[start:m.end()]))
    return out


def split_container(xml, tag):
    """'<tag ...>inner</tag>' (o '<tag/>') → (open, inner, close)."""
    if not xml:
        return "<%s>" % tag, "", "</%s>" % tag
    m = re.match(r'<%s\b[^>]*?(/?)>' % re.escape(tag), xml)
    if not m:
        raise Falla("xml-contenedor")
    if m.group(1):
        return xml[:m.end() - 2] + ">", "", "</%s>" % tag
    close = "</%s>" % tag
    if not xml.endswith(close):
        raise Falla("xml-contenedor")
    return m.group(0), xml[m.end():-len(close)], close


def set_children(xml, tag, order, updates, remove=()):
    """Reemplaza/inserta hijos de `tag` respetando `order`. updates: {nombre: xml}."""
    open_, inner, close = split_container(xml, tag)
    kids = [k for k in children(inner) if k[0] not in remove]
    for name, new in updates.items():
        idx = [i for i, k in enumerate(kids) if k[0] == name]
        if idx:
            kids[idx[0]] = (name, new)
            continue
        rank = order.index(name)
        pos = len(kids)
        for i, (n, _) in enumerate(kids):
            r = order.index(n) if n in order else len(order)
            if r > rank:
                pos = i
                break
        kids.insert(pos, (name, new))
    return open_ + "".join(x for _, x in kids) + close


def get_child(xml, tag, name):
    _, inner, _ = split_container(xml, tag)
    for n, x in children(inner):
        if n == name:
            return x
    return ""


def get_ppr(p):
    m = re.search(r'<w:pPr>.*?</w:pPr>|<w:pPr/>', p, re.S)
    return m.group(0) if m else ""


def with_ppr(p, ppr):
    old = get_ppr(p)
    if old:
        return p.replace(old, ppr, 1)
    o = re.match(r'<w:p\b[^>]*>', p).group(0)
    return o + ppr + p[len(o):]


def runs_of(p):
    return re.findall(r'<w:r>.*?</w:r>|<w:r\b[^>]*>.*?</w:r>', p, re.S)


def restyle_runs(p, rpr):
    """Cambia el rPr de todos los runs del parrafo (el texto queda igual)."""
    def fix(m):
        r = m.group(0)
        r = re.sub(r'<w:rPr>.*?</w:rPr>', '', r, count=1, flags=re.S)
        o = re.match(r'<w:r\b[^>]*>', r).group(0)
        return o + rpr + r[len(o):]
    return re.sub(r'<w:r>.*?</w:r>|<w:r\s[^>]*>.*?</w:r>', fix, p, flags=re.S)


def rpr(*parts):
    return "<w:rPr>" + "".join(parts) + "</w:rPr>"


def color(c):
    return '<w:color w:val="%s"/>' % c


def sz(half_points):
    return '<w:sz w:val="%d"/><w:szCs w:val="%d"/>' % (half_points, half_points)


BOLD = "<w:b/><w:bCs/>"
TITLE_RPR = rpr(CG, BOLD, "<w:caps/>", color(PRIMARY), sz(22))
TINY_RPR = rpr(sz(2))


def run(text, rp):
    return '<w:r>%s<w:t xml:space="preserve">%s</w:t></w:r>' % (rp, esc(text))


def runs(markup, rp):
    """Cada placeholder `{...}` en su propio run."""
    return "".join(run(piece, rp) for piece in re.split(r'(\{[^{}]+\})', markup) if piece)


def para(ids, seed, ppr, content=""):
    return '<w:p w14:paraId="%s">%s%s</w:p>' % (ids.new("v5:" + seed), ppr, content)


def marker(ids, seed, text):
    return para(ids, seed, '<w:pPr><w:spacing w:before="0" w:after="0" w:line="240" '
                           'w:lineRule="auto"/></w:pPr>', run(text, ""))


def tiny(ids, seed, after=0):
    return para(ids, seed, '<w:pPr><w:spacing w:before="0" w:after="%d" w:line="240" '
                           'w:lineRule="auto"/>%s</w:pPr>' % (after, TINY_RPR))


# ── Formas decorativas (5.5, Anexo A.1) ────────────────────────────────────────

def shape(doc_id, name, x, y, cx, cy, fill, z):
    return (
        '<w:r><mc:AlternateContent><mc:Choice Requires="wps"><w:drawing>'
        '<wp:anchor distT="0" distB="0" distL="0" distR="0" simplePos="0" relativeHeight="%d" '
        'behindDoc="1" locked="1" layoutInCell="1" allowOverlap="1">'
        '<wp:simplePos x="0" y="0"/>'
        '<wp:positionH relativeFrom="page"><wp:posOffset>%d</wp:posOffset></wp:positionH>'
        '<wp:positionV relativeFrom="page"><wp:posOffset>%d</wp:posOffset></wp:positionV>'
        '<wp:extent cx="%d" cy="%d"/><wp:effectExtent l="0" t="0" r="0" b="0"/><wp:wrapNone/>'
        '<wp:docPr id="%d" name="%s"/><wp:cNvGraphicFramePr/>'
        '<a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">'
        '<a:graphicData uri="http://schemas.microsoft.com/office/word/2010/wordprocessingShape">'
        '<wps:wsp><wps:cNvSpPr/><wps:spPr>'
        '<a:xfrm><a:off x="0" y="0"/><a:ext cx="%d" cy="%d"/></a:xfrm>'
        '<a:prstGeom prst="rect"><a:avLst/></a:prstGeom>'
        '<a:solidFill><a:srgbClr val="%s"/></a:solidFill><a:ln><a:noFill/></a:ln>'
        '</wps:spPr><wps:bodyPr/></wps:wsp></a:graphicData></a:graphic></wp:anchor>'
        '</w:drawing></mc:Choice></mc:AlternateContent></w:r>'
    ) % (z, x, y, cx, cy, doc_id, name, cx, cy, fill)


def root_of(footer_xml, tag):
    m = re.match(r'(<\?xml[^>]*\?>)?\s*(<w:ftr\b[^>]*>)', footer_xml)
    if not m:
        raise Falla("ancla footer1")
    decl = m.group(1) or '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
    root = m.group(2)
    for ns in ("xmlns:mc=", "xmlns:wp=", "xmlns:wps=", "xmlns:w14=", "xmlns:r="):
        if ns not in root:
            raise Falla("ancla footer1")
    return decl, root.replace("<w:ftr", "<" + tag, 1), "</%s>" % tag


# ── header1: banda de las paginas interiores (5.4) ─────────────────────────────

def build_header1(footer_xml, ids):
    decl, open_, close = root_of(footer_xml, "w:hdr")
    band_h = 900000
    accent_w = 989330
    anchor = para(ids, "h1:ancla",
                  '<w:pPr><w:spacing w:before="0" w:after="0" w:line="20" w:lineRule="exact"/>'
                  '%s</w:pPr>' % TINY_RPR,
                  shape(9001, "Factum banda", 0, 0, PAGE_W, band_h, PRIMARY, 251658240)
                  + shape(9002, "Factum banda acento", PAGE_W - accent_w, 0, accent_w, band_h,
                          ACCENT, 251659264))
    white_title = rpr(CG, BOLD, color("FFFFFF"), '<w:spacing w:val="20"/>', sz(22))
    white_causa = rpr(ARIAL, color("FFFFFF"), sz(18))
    white_org = rpr(CG, color("FFFFFF"), sz(18))
    cell_ppr = ('<w:pPr><w:spacing w:before="0" w:after="0" w:line="240" w:lineRule="auto"/>'
                '<w:jc w:val="%s"/></w:pPr>')
    left = (
        para(ids, "h1:titulo", cell_ppr % "left",
             run("INFORME PERICIAL TÉCNICO INFORMÁTICO", white_title))
        + para(ids, "h1:causa", cell_ppr % "left",
               runs("{tipoCausa} N° {numeroCausa}", white_causa)))
    right = (
        marker(ids, "h1:iso-open", "{#ISOTIPO}")
        + para(ids, "h1:iso", cell_ppr % "right", run("{ISOTIPO_ORGANIZACION:2.4x1.6}", ""))
        + marker(ids, "h1:iso-close", "{/ISOTIPO}")
        + marker(ids, "h1:nom-open", "{#NOMBRE_EN_BANDA}")
        + para(ids, "h1:nom", cell_ppr % "right", run("{ORGANIZACION}", white_org))
        + marker(ids, "h1:nom-close", "{/NOMBRE_EN_BANDA}")
        + tiny(ids, "h1:celda-fin"))
    nil = ''.join('<w:%s w:val="nil"/>' % b
                  for b in ("top", "left", "bottom", "right", "insideH", "insideV"))
    table = (
        '<w:tbl><w:tblPr><w:tblW w:w="8647" w:type="dxa"/><w:tblInd w:w="0" w:type="dxa"/>'
        '<w:tblBorders>%s</w:tblBorders><w:tblLayout w:type="fixed"/>'
        '<w:tblCellMar><w:left w:w="0" w:type="dxa"/><w:right w:w="0" w:type="dxa"/>'
        '</w:tblCellMar></w:tblPr>'
        '<w:tblGrid><w:gridCol w:w="6247"/><w:gridCol w:w="2400"/></w:tblGrid>'
        '<w:tr><w:trPr><w:trHeight w:val="1134" w:hRule="exact"/></w:trPr>'
        '<w:tc><w:tcPr><w:tcW w:w="6247" w:type="dxa"/><w:vAlign w:val="center"/></w:tcPr>%s</w:tc>'
        '<w:tc><w:tcPr><w:tcW w:w="2400" w:type="dxa"/><w:vAlign w:val="center"/></w:tcPr>%s</w:tc>'
        '</w:tr></w:tbl>') % (nil, left, right)
    return decl + "\n" + open_ + anchor + table + tiny(ids, "h1:fin") + close


# ── header2: franja y remate de la portada (5.3) ───────────────────────────────

def build_header2(footer_xml, ids):
    decl, open_, close = root_of(footer_xml, "w:hdr")
    x, w = 6048248, 1512062
    shapes = [shape(9003, "Factum franja", x, 0, w, 9072130, PRIMARY, 251658240)]
    for i, y in enumerate((9252130, 9792130, 10332130)):
        shapes.append(shape(9004 + i, "Factum remate %d" % (i + 1), x, y, w, 360000, ACCENT,
                            251659264 + i * 1024))
    p = para(ids, "h2:ancla",
             '<w:pPr><w:spacing w:before="0" w:after="0" w:line="20" w:lineRule="exact"/>'
             '%s</w:pPr>' % TINY_RPR, "".join(shapes))
    return decl + "\n" + open_ + p + close


# ── footer1: "Pagina N de M" (5.4, Anexo A.3) ──────────────────────────────────

def build_footer1(footer_xml, ids):
    decl, open_, close = root_of(footer_xml, "w:ftr")
    rp = rpr('<w:rFonts w:ascii="Arial" w:hAnsi="Arial" w:cs="Arial"/>', color("5C656E"), sz(16))

    def field(code):
        return ('<w:r>%s<w:fldChar w:fldCharType="begin"/></w:r>'
                '<w:r>%s<w:instrText xml:space="preserve"> %s </w:instrText></w:r>'
                '<w:r>%s<w:fldChar w:fldCharType="separate"/></w:r>'
                '<w:r>%s<w:t>2</w:t></w:r>'
                '<w:r>%s<w:fldChar w:fldCharType="end"/></w:r>') % (rp, rp, code, rp, rp, rp)

    p = para(ids, "f1:pagina",
             '<w:pPr><w:spacing w:before="0" w:after="40" w:line="240" w:lineRule="auto"/>'
             '<w:jc w:val="right"/></w:pPr>',
             run("Página ", rp) + field("PAGE") + run(" de ", rp) + field("NUMPAGES"))
    return decl + "\n" + open_ + p + close


# ── footer2: identidad del estudio en la portada (5.3) ─────────────────────────

def build_footer2(footer_xml, ids):
    decl, open_, close = root_of(footer_xml, "w:ftr")
    body = "".join([
        marker(ids, "f2:open", "{#MEMBRETE}"),
        para(ids, "f2:logo", '<w:pPr><w:spacing w:before="0" w:after="120" w:line="240" '
                             'w:lineRule="auto"/><w:jc w:val="left"/></w:pPr>',
             run("{LOGO_ORGANIZACION:7x3.8}", "")),
        para(ids, "f2:org", '<w:pPr><w:spacing w:before="0" w:after="0" w:line="240" '
                            'w:lineRule="auto"/><w:jc w:val="left"/></w:pPr>',
             run("{ORGANIZACION}", rpr(CG, BOLD, color(PRIMARY), sz(22)))),
        para(ids, "f2:contacto", '<w:pPr><w:spacing w:before="0" w:after="0" w:line="240" '
                                 'w:lineRule="auto"/><w:jc w:val="left"/></w:pPr>',
             run("{CONTACTO}", rpr(ARIAL, color("595959"), sz(16)))),
        marker(ids, "f2:close", "{/MEMBRETE}"),
        tiny(ids, "f2:fin", after=120),
    ])
    return decl + "\n" + open_ + body + close


# ── document.xml ───────────────────────────────────────────────────────────────

def fingerprint(paras, order):
    def txt(pid):
        if pid not in paras:
            raise Falla("ancla " + pid)
        return para_text(paras[pid]).strip()

    checks = {
        "0DF4CF61": lambda t: t.startswith("INFORME PERICIAL"),
        "18A07FBA": lambda t: t.startswith("DECLARACIÓN DE IMPARCIALIDAD"),
        "0BF0C8CD": lambda t: t == "OBJETO DEL INFORME",
        "5B3056CF": lambda t: t == "OPERACIONES REALIZADAS",
        "0FF62E21": lambda t: t.startswith("GENERACIÓN Y ASEGURAMIENTO"),
        "3A62DE0D": lambda t: t == "CADENA DE CUSTODIA DIGITAL",
        "37BC776D": lambda t: t == "RESULTADOS",
        "5A06ED76": lambda t: t == "VALORACIÓN TÉCNICA",
        "390F7DAB": lambda t: t == "CONCLUSIONES",
        "05D9340B": lambda t: t == "NOTAS TÉCNICAS",
        "10E394C7": lambda t: t == "RESERVA",
        "3B831F98": lambda t: t == "Referencia de la actuación:",
        "3A17DBEE": lambda t: t.startswith("Carátula:"),
        "2AFB1345": lambda t: t.startswith("Parte denunciante:"),
        "15BB4A06": lambda t: t.startswith("Parte denunciada:"),
        "69BAC851": lambda t: t.startswith("Objeto:"),
        "3DA3ACE8": lambda t: t.startswith("Fecha de intervención:"),
        "236C9BC4": lambda t: t == "",
        "0386A4A4": lambda t: t == "IDENTIFICACIÓN",
        "5F66E0AC": lambda t: t.startswith("Perito de parte:"),
        "08C61010": lambda t: t.startswith("Parte que lo propone:"),
        "3162E6AB": lambda t: t.startswith("Ámbito:"),
        "3A37F215": lambda t: t == "",
        "0F4EDFCE": lambda t: t == "ELEMENTOS OFRECIDOS",
        "5FECA2F2": lambda t: t.startswith("En fecha"),
        "14341833": lambda t: t.startswith("Tipo:"),
        "15EF193E": lambda t: t.startswith("Marca y modelo:"),
        "490B4C01": lambda t: t.startswith("IMEI:"),
        "28E6822B": lambda t: t.startswith("Línea:"),
        "2E1C810A": lambda t: t.startswith("Titular:"),
        "26DDBA6A": lambda t: t.startswith("El mismo fue aportado"),
        "04E11EDC": lambda t: t == "NOMBRE",
        "4B698602": lambda t: t == "HASH SHA-256",
        FIRMA_CARACTER: lambda t: t == "{caracterPerito}",
        ANEXO_OPEN: lambda t: t == "{#anexoCapturas}",
    }
    for pid, ok in checks.items():
        if not ok(txt(pid)):
            raise Falla("ancla " + pid)
    # Los bloques de datos son parrafos consecutivos.
    for title, intro, rows, after in (BLOCK_REFERENCIA, BLOCK_IDENTIFICACION, BLOCK_ELEMENTOS):
        seq = [title] + ([intro] if intro else []) + rows + ([after] if after else [])
        i = order.index(title)
        if order[i:i + len(seq)] != seq:
            raise Falla("ancla bloque " + title)
    if order[order.index(FIRMA_CARACTER) + 1] != ANEXO_OPEN:
        raise Falla("ancla " + ANEXO_OPEN)


def tnr_to_arial(xml):
    return re.sub(r'<w:rFonts\b[^>]*w:ascii="Times New Roman"[^>]*/>', ARIAL, xml)


def cover(ids):
    title_rp = rpr(CG, color(PRIMARY), sz(68))
    sub_rp = rpr(CG, color(PRIMARY), sz(32))
    car_rp = rpr(ARIAL, color("404040"), sz(26))
    per_rp = rpr(ARIAL, color("404040"), sz(22))
    sect = ('<w:sectPr><w:headerReference w:type="default" r:id="rIdHdr2"/>'
            '<w:footerReference w:type="default" r:id="rIdFtr2"/><w:type w:val="nextPage"/>'
            '<w:pgSz w:w="11906" w:h="16838"/>'
            '<w:pgMar w:top="1984" w:right="3685" w:bottom="1417" w:left="1701" w:header="284" '
            'w:footer="567" w:gutter="0"/><w:cols w:space="708"/>'
            '<w:docGrid w:linePitch="360"/></w:sectPr>')

    def ppr(spacing, keep=False, rp="", sect_xml=""):
        return '<w:pPr>%s<w:spacing %s/><w:jc w:val="left"/>%s%s</w:pPr>' % (
            "<w:keepNext/>" if keep else "", spacing, rp, sect_xml)

    title_sp = 'w:before="0" w:after="%d" w:line="820" w:lineRule="exact"'
    return "".join([
        para(ids, "p1", ppr('w:before="0" w:after="0" w:line="3402" w:lineRule="exact"',
                            rp=TINY_RPR)),
        para(ids, "p2", ppr(title_sp % 0, keep=True, rp=title_rp), run("Informe", title_rp)),
        para(ids, "p3", ppr(title_sp % 0, keep=True, rp=title_rp),
             run("Pericial Técnico", title_rp)),
        para(ids, "p4", ppr(title_sp % 480, keep=True, rp=title_rp), run("Informático", title_rp)),
        para(ids, "p5", ppr('w:before="0" w:after="120" w:line="240" w:lineRule="auto"', rp=sub_rp),
             runs("{tipoCausa} N° {numeroCausa}", sub_rp)),
        para(ids, "p6", ppr('w:before="0" w:after="600" w:line="276" w:lineRule="auto"', rp=car_rp),
             runs("“{caratula}”", car_rp)),
        para(ids, "p7", ppr('w:before="0" w:after="60" w:line="276" w:lineRule="auto"', rp=per_rp),
             runs("{nombrePerito} · M.P. {matriculaPerito}", per_rp)),
        para(ids, "p8", ppr('w:before="0" w:after="0" w:line="276" w:lineRule="auto"', rp=per_rp,
                            sect_xml=sect),
             runs("{fechaInspeccion}", per_rp)),
    ])


def title_ppr(ppr, border=True, in_cell=False):
    upd = {
        "w:keepNext": "<w:keepNext/>",
        "w:spacing": '<w:spacing w:before="%d" w:after="120" w:line="%s" w:lineRule="auto"/>'
                     % ((0, "240") if in_cell else (360, "360")),
        "w:rPr": TITLE_RPR,
    }
    if border:
        # Nota: Word y LibreOffice agrupan parrafos contiguos con el mismo borde y dibujan solo
        # el bottom del ultimo. Solo pasa con dos titulos seguidos (una seccion sin texto); un
        # "between" lo "arregla" pero dibuja la linea pegada ARRIBA del titulo siguiente.
        upd["w:pBdr"] = ('<w:pBdr><w:bottom w:val="single" w:sz="6" w:space="1" w:color="%s"/>'
                         '</w:pBdr>' % PRIMARY)
    # contextualSpacing anularia el "before" entre parrafos del mismo estilo (Normal).
    upd["w:contextualSpacing"] = '<w:contextualSpacing w:val="0"/>'
    # Justificado estiraba la primera linea de un titulo largo ("GENERACION Y ...").
    upd["w:jc"] = '<w:jc w:val="left"/>'
    if in_cell:
        if get_child(ppr, "w:pPr", "w:numPr"):
            # Numeracion romana (lvlJc right): el numero termina en left-hanging.
            upd["w:ind"] = '<w:ind w:left="567" w:hanging="300"/>'
        else:
            upd["w:ind"] = '<w:ind w:left="0" w:firstLine="0"/>'
    return set_children(ppr or "<w:pPr></w:pPr>", "w:pPr", PPR_ORDER, upd)


def restyle_title(p, border=True, in_cell=False):
    return restyle_runs(with_ppr(p, title_ppr(get_ppr(p), border, in_cell)), TITLE_RPR)


# Grilla de los bloques de datos (titulo / etiqueta / valor), 8647 dxa en total. La SDD
# proponia 2400/2200/4047; la columna del titulo se ensancho a 2700 porque "IDENTIFICACION"
# (Century Gothic 11 pt negrita, con la sangria del numero romano) no entraba en una linea.
GRID = (2700, 2000, 3947)

CELL_PPR = ('<w:pPr><w:spacing w:before="0" w:after="0" w:line="240" w:lineRule="auto"/>'
            '<w:jc w:val="left"/>%s</w:pPr>')
LABEL_RPR = rpr(ARIAL, BOLD, color("333333"), sz(20))
VALUE_RPR = rpr(ARIAL, sz(20))


def data_table(ids, paras, block, title_xml):
    title, intro, rows, _ = block
    border = '<w:%s w:val="single" w:sz="6" w:space="0" w:color="%s"/>'
    data_borders = ('<w:tcBorders>' + border % ("top", PRIMARY) + '<w:left w:val="nil"/>'
                    + border % ("bottom", PRIMARY) + '<w:right w:val="nil"/></w:tcBorders>')
    nil_borders = ('<w:tcBorders><w:top w:val="nil"/><w:left w:val="nil"/><w:bottom w:val="nil"/>'
                   '<w:right w:val="nil"/></w:tcBorders>')

    def tc(width, content, vmerge=None, span=None, borders=data_borders, valign="center",
           mar=""):
        pr = '<w:tcW w:w="%d" w:type="dxa"/>' % width
        if span:
            pr += '<w:gridSpan w:val="%d"/>' % span
        if vmerge is not None:
            pr += '<w:vMerge w:val="restart"/>' if vmerge else '<w:vMerge/>'
        pr += borders + mar + '<w:vAlign w:val="%s"/>' % valign
        return '<w:tc><w:tcPr>%s</w:tcPr>%s</w:tc>' % (pr, content)

    title_mar = '<w:tcMar><w:left w:w="0" w:type="dxa"/></w:tcMar>'

    def title_cell(i):
        content = title_xml if i == 0 else tiny(ids, "tbl:%s:%d" % (title, i))
        return tc(GRID[0], content, vmerge=(i == 0), borders=nil_borders, valign="top",
                  mar=title_mar)

    def split_row(pid):
        """`**Etiqueta:** valor` → (parrafo etiqueta con paraId nuevo, parrafo valor original)."""
        p = paras[pid]
        rs = runs_of(p)
        if not rs or "<w:b/>" not in rs[0]:
            raise Falla("ancla " + pid)
        label = "".join(re.findall(r'<w:t(?:\s[^>]*)?>([^<]*)</w:t>', rs[0]))
        if not label.endswith(":"):
            raise Falla("ancla " + pid)
        rest = rs[1:]
        # El espacio que separaba etiqueta y valor no forma parte del texto concatenado
        # que se compara: se descarta el run que es solo " ".
        if rest and v4.unesc("".join(re.findall(r'<w:t(?:\s[^>]*)?>([^<]*)</w:t>', rest[0]))) == " ":
            rest = rest[1:]
        open_tag = re.match(r'<w:p\b[^>]*>', p).group(0)
        lab = para(ids, "lbl:" + pid, CELL_PPR % LABEL_RPR, run(v4.unesc(label), LABEL_RPR))
        val = open_tag + CELL_PPR % VALUE_RPR + "".join(rest) + "</w:p>"
        return lab, restyle_runs(val, VALUE_RPR)

    trs = []
    if intro:
        p = paras[intro]
        ip = with_ppr(p, set_children(get_ppr(p), "w:pPr", PPR_ORDER, {
            "w:spacing": '<w:spacing w:before="0" w:after="0" w:line="240" w:lineRule="auto"/>',
            "w:ind": '<w:ind w:left="0" w:firstLine="0"/>',
            "w:jc": '<w:jc w:val="left"/>',
        }))
        trs.append((True, tc(GRID[1] + GRID[2], ip, span=2)))
    for pid in rows:
        lab, val = split_row(pid)
        trs.append((False, tc(GRID[1], lab) + tc(GRID[2], val)))

    out = []
    for i, (_, cells) in enumerate(trs):
        out.append('<w:tr><w:trPr><w:cantSplit/></w:trPr>%s%s</w:tr>'
                   % (title_cell(i), cells))
    return ('<w:tbl><w:tblPr><w:tblW w:w="8647" w:type="dxa"/><w:tblInd w:w="0" w:type="dxa"/>'
            '<w:tblBorders><w:top w:val="nil"/><w:left w:val="nil"/><w:bottom w:val="nil"/>'
            '<w:right w:val="nil"/><w:insideH w:val="nil"/><w:insideV w:val="nil"/>'
            '</w:tblBorders><w:tblLayout w:type="fixed"/>'
            '<w:tblCellMar><w:top w:w="60" w:type="dxa"/><w:left w:w="108" w:type="dxa"/>'
            '<w:bottom w:w="60" w:type="dxa"/><w:right w:w="108" w:type="dxa"/></w:tblCellMar>'
            '</w:tblPr>'
            '<w:tblGrid>' + "".join('<w:gridCol w:w="%d"/>' % g for g in GRID)
            + '</w:tblGrid>' + "".join(out) + '</w:tbl>')


def hash_table(tbl):
    if tbl.count("<w:tr ") != 2:
        raise Falla("ancla tabla")
    line = '<w:%s w:val="single" w:sz="4" w:space="0" w:color="%s"/>'
    tbl = re.sub(r'<w:tblBorders>.*?</w:tblBorders>',
                 '<w:tblBorders>' + line % ("top", PRIMARY) + '<w:left w:val="nil"/>'
                 + line % ("bottom", PRIMARY) + '<w:right w:val="nil"/>'
                 + line % ("insideH", PRIMARY) + '<w:insideV w:val="nil"/></w:tblBorders>',
                 tbl, count=1, flags=re.S)
    tbl = re.sub(r'<w:tcBorders>.*?</w:tcBorders>',
                 '<w:tcBorders>' + line % ("top", PRIMARY) + '<w:left w:val="nil"/>'
                 + line % ("bottom", PRIMARY) + '<w:right w:val="nil"/></w:tcBorders>',
                 tbl, flags=re.S)
    rows = list(re.finditer(r'<w:tr\b.*?</w:tr>', tbl, re.S))
    head = rows[0].group(0)
    head_rpr = rpr(CG, BOLD, color("FFFFFF"), sz(18))
    new_head = head.replace(
        '</w:tcBorders>',
        '</w:tcBorders><w:shd w:val="clear" w:color="auto" w:fill="%s"/>' % PRIMARY)
    new_head = re.sub(r'<w:p\b.*?</w:p>', lambda m: restyle_runs(
        with_ppr(m.group(0), set_children(get_ppr(m.group(0)), "w:pPr", PPR_ORDER,
                                          {"w:rPr": head_rpr})), head_rpr), new_head, flags=re.S)
    if new_head.count("w:fill=") != 2:
        raise Falla("ancla tabla")
    tbl = tbl.replace(head, new_head, 1)
    return tbl


def build_document(doc, ids):
    doc = tnr_to_arial(doc)
    paras, order = {}, []
    for m in PARA_RE.finditer(doc):
        pid = para_id(m.group(0))
        if pid:
            paras[pid] = m.group(0)
            order.append(pid)
    fingerprint(paras, order)

    new = {}
    # Titulo del escrito: Century Gothic 14 pt negrita, primario (texto y alineacion igual).
    tp = paras["0DF4CF61"]

    def title_run(m):
        r = m.group(0)
        old = re.search(r'<w:rPr>.*?</w:rPr>', r, re.S)
        if not old:
            return r
        rp = set_children(old.group(0), "w:rPr", RPR_ORDER,
                          {"w:rFonts": CG, "w:color": color(PRIMARY),
                           "w:sz": '<w:sz w:val="28"/>', "w:szCs": '<w:szCs w:val="28"/>'})
        return r.replace(old.group(0), rp, 1)

    new["0DF4CF61"] = cover(ids) + re.sub(r'<w:r>.*?</w:r>', title_run, tp, flags=re.S)

    for pid in SECTION_TITLES:
        new[pid] = restyle_title(paras[pid])

    # Bloques de datos en dos columnas (5.4.1).
    drop = set()
    for block in (BLOCK_REFERENCIA, BLOCK_IDENTIFICACION, BLOCK_ELEMENTOS):
        title, intro, rows, after = block
        title_xml = restyle_title(paras[title], border=False, in_cell=True)
        tbl = data_table(ids, paras, block, title_xml)
        # Parrafo vacio obligatorio despues de la tabla: el de la v4 si existe, si no uno nuevo.
        tail = "" if after else tiny(ids, "tras:" + title)
        new[title] = tbl + tail
        drop.update(([intro] if intro else []) + rows)

    # Cierre con isotipo (entre la firma y el anexo).
    new[FIRMA_CARACTER] = paras[FIRMA_CARACTER] + "".join([
        marker(ids, "cierre:open", "{#ISOTIPO}"),
        para(ids, "cierre:iso", '<w:pPr><w:spacing w:before="480" w:after="0" w:line="240" '
                                'w:lineRule="auto"/><w:jc w:val="center"/></w:pPr>',
             run("{ISOTIPO_ORGANIZACION:2x2}", "")),
        marker(ids, "cierre:close", "{/ISOTIPO}"),
    ])

    def sub(m):
        pid = para_id(m.group(0))
        if not pid:
            return m.group(0)
        if pid in drop:
            return ""
        return new.get(pid, m.group(0))

    # Ojo: PARA_RE tambien encuentra los parrafos de las celdas de la tabla de hashes; esos
    # no estan en `new` ni en `drop` y quedan iguales.
    out = PARA_RE.sub(sub, doc)

    # Tabla de hashes (5.4.2): la unica tabla de la v4 (las nuevas no tienen tblStyle).
    m = re.search(r'<w:tbl><w:tblPr><w:tblStyle w:val="17"/>.*?</w:tbl>', out, re.S)
    if not m:
        raise Falla("ancla tabla")
    out = out[:m.start()] + hash_table(m.group(0)) + out[m.end():]

    # sectPr final (seccion 2): banda (header1), top 1984 y header 284.
    finals = re.findall(r'<w:sectPr>(?:(?!<w:sectPr>).)*?</w:sectPr>\s*</w:body>', out, re.S)
    if len(finals) != 1 or 'r:id="rIdHdr1"' not in finals[0]:
        raise Falla("ancla sectPr")
    fs = finals[0]
    fs2 = re.sub(r'w:top="\d+"', 'w:top="1984"', fs, count=1)
    fs2 = re.sub(r'w:header="\d+"', 'w:header="284"', fs2, count=1)
    out = out.replace(fs, fs2, 1)
    return out


def build_numbering(num):
    m = re.search(r'<w:abstractNum\b[^>]*w:abstractNumId="1".*?</w:abstractNum>', num, re.S)
    if not m:
        raise Falla("ancla numbering")
    an = m.group(0)
    lvl = re.search(r'<w:lvl w:ilvl="0"[ >].*?</w:lvl>', an, re.S)
    if not lvl or "upperRoman" not in lvl.group(0) or "<w:rPr><w:b/></w:rPr>" not in lvl.group(0):
        raise Falla("ancla numbering")
    new_lvl = lvl.group(0).replace("<w:rPr><w:b/></w:rPr>",
                                   rpr(CG, BOLD, color(PRIMARY)), 1)
    return num.replace(an, an.replace(lvl.group(0), new_lvl, 1), 1)


def build_styles(st):
    st = st.replace('w:cs="Times New Roman"', 'w:cs="Arial"')
    theme = re.compile(r'<w:rFonts w:asciiTheme="minorHAnsi" w:hAnsiTheme="minorHAnsi" '
                       r'w:eastAsiaTheme="minorHAnsi" w:cstheme="minorBidi"/>')
    d = re.search(r'<w:docDefaults>.*?</w:docDefaults>', st, re.S)
    if not d or not theme.search(d.group(0)):
        raise Falla("ancla styles")
    nd = theme.sub(ARIAL + sz(22), d.group(0), count=1)
    st = st.replace(d.group(0), nd, 1)
    n = re.search(r'<w:style w:type="paragraph" w:default="1" w:styleId="1">.*?</w:style>', st,
                  re.S)
    if not n or not theme.search(n.group(0)) or '<w:sz w:val="24"/>' not in n.group(0):
        raise Falla("ancla styles")
    nn = theme.sub(ARIAL, n.group(0), count=1)
    nn = nn.replace('<w:sz w:val="24"/><w:szCs w:val="24"/>', sz(22), 1)
    return st.replace(n.group(0), nn, 1)


CENTURY_GOTHIC_FONT = (
    '<w:font w:name="Century Gothic"><w:panose1 w:val="020B0502020202020204"/>'
    '<w:charset w:val="00"/><w:family w:val="swiss"/><w:pitch w:val="variable"/></w:font>')
ARIAL_FONT = (
    '<w:font w:name="Arial"><w:panose1 w:val="020B0604020202020204"/><w:charset w:val="00"/>'
    '<w:family w:val="swiss"/><w:pitch w:val="variable"/></w:font>')


def build_fonts(ft):
    add = ""
    if 'w:name="Century Gothic"' not in ft:
        add += CENTURY_GOTHIC_FONT
    if 'w:name="Arial"' not in ft:
        add += ARIAL_FONT
    if "</w:fonts>" not in ft:
        raise Falla("ancla fontTable")
    return ft.replace("</w:fonts>", add + "</w:fonts>", 1)


def build(src, dst):
    parts = v4.read_parts(src)
    names = [n for n, _ in parts]
    data = dict(parts)
    for required in ("word/document.xml", "word/footer1.xml", "word/header1.xml",
                     "word/_rels/document.xml.rels", "[Content_Types].xml", "word/styles.xml",
                     "word/numbering.xml", "word/fontTable.xml"):
        if required not in data:
            raise Falla("parte " + required)
    for n in ("word/header2.xml", "word/footer2.xml"):
        if n in data:
            raise Falla("ancla " + n)

    # Los centinelas no pueden estar en el origen (si no, B-R2b pisaria un color ajeno).
    for n, v in data.items():
        if n.endswith(".xml") or n.endswith(".rels"):
            low = v.decode("utf-8", "replace").lower()
            if PRIMARY.lower() in low or ACCENT.lower() in low:
                raise Falla("centinela-en-origen")

    used = set()
    for n in names:
        if n.endswith(".xml"):
            used.update(x.upper() for x in re.findall(r'w14:paraId="([0-9A-Fa-f]{8})"',
                                                      data[n].decode("utf-8")))
    # Los paraIds de header1 y footer1 de la v4 desaparecen (se reescriben las partes).
    ids = Ids(used)

    footer_src = data["word/footer1.xml"].decode("utf-8")
    doc = build_document(data["word/document.xml"].decode("utf-8"), ids)
    header1 = build_header1(footer_src, ids)
    header2 = build_header2(footer_src, ids)
    footer2 = build_footer2(footer_src, ids)
    footer1 = build_footer1(footer_src, ids)

    rels = data["word/_rels/document.xml.rels"].decode("utf-8")
    if "rIdHdr2" in rels or "rIdFtr2" in rels or 'Target="footer1.xml"' not in rels:
        raise Falla("ancla rels")
    m = re.search(r'<Relationship Id="([^"]+)"[^>]*Target="footer1\.xml"/>', rels)
    if not m or 'r:id="%s"' % m.group(1) not in doc:
        raise Falla("ancla rels")
    rels = rels.replace("</Relationships>",
                        '<Relationship Id="rIdHdr2" Type="%s" Target="header2.xml"/>'
                        '<Relationship Id="rIdFtr2" Type="%s" Target="footer2.xml"/>'
                        "</Relationships>" % (REL_HEADER, REL_FOOTER))

    ct = data["[Content_Types].xml"].decode("utf-8")
    ct = ct.replace("</Types>",
                    '<Override PartName="/word/header2.xml" ContentType="%s"/>'
                    '<Override PartName="/word/footer2.xml" ContentType="%s"/></Types>'
                    % (CT_HEADER, CT_FOOTER))

    data["word/document.xml"] = doc.encode("utf-8")
    data["word/header1.xml"] = header1.encode("utf-8")
    data["word/header2.xml"] = header2.encode("utf-8")
    data["word/footer1.xml"] = footer1.encode("utf-8")
    data["word/footer2.xml"] = footer2.encode("utf-8")
    data["word/numbering.xml"] = build_numbering(
        data["word/numbering.xml"].decode("utf-8")).encode("utf-8")
    data["word/styles.xml"] = build_styles(data["word/styles.xml"].decode("utf-8")).encode("utf-8")
    data["word/fontTable.xml"] = build_fonts(
        data["word/fontTable.xml"].decode("utf-8")).encode("utf-8")
    data["word/_rels/document.xml.rels"] = rels.encode("utf-8")
    data["[Content_Types].xml"] = ct.encode("utf-8")
    data["docProps/core.xml"] = v4.CORE_XML.encode("utf-8")

    order = ["[Content_Types].xml"] + [n for n in names if n != "[Content_Types].xml"]
    order.insert(order.index("word/header1.xml") + 1, "word/header2.xml")
    order.insert(order.index("word/footer1.xml") + 1, "word/footer2.xml")

    with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as z:
        for n in order:
            info = zipfile.ZipInfo(n, date_time=FIXED_DATE)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 0
            info.external_attr = 0
            z.writestr(info, data[n])


# ── Verificacion (B8) ──────────────────────────────────────────────────────────

def check(src, dst):
    fails = set()
    d = dict(v4.read_parts(dst))
    xml_parts = {n: v.decode("utf-8", "replace") for n, v in d.items()
                 if n.endswith(".xml") or n.endswith(".rels")}

    for x in xml_parts.values():
        try:
            ET.fromstring(x.encode("utf-8"))
        except ET.ParseError:
            fails.add("FALLA:xml-mal-formado")

    core = xml_parts.get("docProps/core.xml", "")
    for tag in ("dc:creator", "cp:lastModifiedBy"):
        m = re.search(r"<%s>([^<]*)</%s>" % (tag, tag), core)
        if not m or m.group(1) != "Factum":
            fails.add("FALLA:core")

    doc = xml_parts.get("word/document.xml", "")
    h1 = xml_parts.get("word/header1.xml", "")
    h2 = xml_parts.get("word/header2.xml", "")
    f1 = xml_parts.get("word/footer1.xml", "")
    f2 = xml_parts.get("word/footer2.xml", "")
    num = xml_parts.get("word/numbering.xml", "")
    sty = xml_parts.get("word/styles.xml", "")

    # 1. Placeholders por parte.
    v4.check_placeholders(doc, BODY_PLACEHOLDERS, BODY_BLOCKS, fails, "cuerpo")
    v4.check_placeholders(h1, HEADER1_PLACEHOLDERS, HEADER1_BLOCKS, fails, "header1")
    v4.check_placeholders(f2, FOOTER2_PLACEHOLDERS, FOOTER2_BLOCKS, fails, "footer2")
    for n, x in xml_parts.items():
        if re.match(r"word/(header|footer)\d*\.xml$", n) and n not in (
                "word/header1.xml", "word/footer2.xml"):
            single, joined = v4.placeholders_of(x)
            if single or joined:
                fails.add("FALLA:placeholder-desconocido")

    # 2. Dos sectPr, cada uno con header y footer default hacia las partes de 5.2.
    rels = xml_parts.get("word/_rels/document.xml.rels", "")
    targets = dict(re.findall(r'<Relationship Id="([^"]+)"[^>]*Target="([^"]+)"', rels))
    sects = re.findall(r"<w:sectPr\b.*?</w:sectPr>", doc, re.S)
    expected = [("header2.xml", "footer2.xml"), ("header1.xml", "footer1.xml")]
    if len(sects) != 2:
        fails.add("FALLA:sectPr")
    else:
        for s, (eh, ef) in zip(sects, expected):
            hr = re.search(r'<w:headerReference w:type="default" r:id="([^"]+)"/>', s)
            fr = re.search(r'<w:footerReference (?:r:id="([^"]+)" w:type="default"|'
                           r'w:type="default" r:id="([^"]+)")/>', s)
            if not hr or not fr or targets.get(hr.group(1)) != eh \
                    or targets.get(fr.group(1) or fr.group(2)) != ef or "titlePg" in s:
                fails.add("FALLA:sectPr")
        if not re.search(r"<w:pPr>(?:(?!</w:pPr>).)*<w:sectPr>", doc, re.S):
            fails.add("FALLA:sectPr")
        if 'w:top="1984"' not in sects[1] or 'w:header="284"' not in sects[1]:
            fails.add("FALLA:sectPr")
    ct = xml_parts.get("[Content_Types].xml", "")
    for part, kind in (("header1", CT_HEADER), ("header2", CT_HEADER), ("footer1", CT_FOOTER),
                       ("footer2", CT_FOOTER)):
        if '<Override PartName="/word/%s.xml" ContentType="%s"/>' % (part, kind) not in ct:
            fails.add("FALLA:content-type")

    # 3. Centinelas donde corresponde.
    def clr(x, c):
        return len(re.findall(r'<a:srgbClr val="%s"/>' % c, x))
    if clr(h2, PRIMARY) != 1 or clr(h2, ACCENT) != 3:
        fails.add("FALLA:centinela-header2")
    if clr(h1, PRIMARY) != 1 or clr(h1, ACCENT) != 1:
        fails.add("FALLA:centinela-header1")
    if doc.count('<w:color w:val="%s"/>' % PRIMARY) < len(SECTION_TITLES):
        fails.add("FALLA:centinela-titulos")
    if '<w:color w:val="%s"/>' % PRIMARY not in num:
        fails.add("FALLA:centinela-numbering")
    if doc.count('w:fill="%s"' % PRIMARY) != 2:
        fails.add("FALLA:centinela-tabla")

    # 4. Sin Times New Roman.
    for n in ["word/document.xml", "word/styles.xml"] + [
            n for n in xml_parts if re.match(r"word/(header|footer)\d*\.xml$", n)]:
        if "Times New Roman" in xml_parts.get(n, ""):
            fails.add("FALLA:times-new-roman")
    if "Century Gothic" not in xml_parts.get("word/fontTable.xml", ""):
        fails.add("FALLA:fontTable")
    if 'w:ascii="Arial"' not in sty:
        fails.add("FALLA:styles")

    # 5. paraIds unicos, sin autor.
    all_ids = []
    for x in xml_parts.values():
        x = re.sub(r"<mc:Fallback\b.*?</mc:Fallback>", "", x, flags=re.S)
        all_ids += [i.upper() for i in re.findall(r'w14:paraId="([0-9A-Fa-f]{8})"', x)]
    if len(all_ids) != len(set(all_ids)):
        fails.add("FALLA:paraid-repetido")
    if any("w:author" in x for x in xml_parts.values()):
        fails.add("FALLA:autor")

    # 6. Pagina N de M.
    if " PAGE " not in f1 or " NUMPAGES " not in f1:
        fails.add("FALLA:numeracion")
    return sorted(fails)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--check", action="store_true", help="verifica en lugar de construir")
    ap.add_argument("origen")
    ap.add_argument("destino")
    a = ap.parse_args()
    try:
        if not a.check:
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
