#!/usr/bin/env python3
"""Construye (o verifica) plantilla_informe_v6.docx a partir de plantilla_informe_v4.docx.

Uso:
    python3 ops/plantilla/build_plantilla_v6.py <v4.docx> <v6.docx>
    python3 ops/plantilla/build_plantilla_v6.py --check <v4.docx> <v6.docx>

Ver ops/plantilla/README.md y Refactorizaciones/informe-diseno-v6.md (secciones 5 y 8.1).

La v6 es la v4 con el diseno "Filete" y la paleta de Factum: portada sobria (filete verde,
titulo grande, ficha de la causa e identidad del estudio en footer2), encabezado interior de
texto sobre una linea fina (header1), pie con la atribucion y "Pagina N de M" en la misma linea
(footer1, slot {ATRIBUCION_FACTUM}), numero romano en un parrafo propio arriba de cada titulo,
filete verde corto debajo, fichas a todo el ancho y tabla de hashes con linea verde. NO hay
formas flotantes en ninguna parte. El TEXTO pericial no cambia.

Reglas que este script respeta (no romperlas al editarlo):
  * Parte SOLO de la v4 versionada (ya sin datos reales): no lee la plantilla de docs/.
  * Reutiliza los helpers de build_plantilla_v4.py y build_plantilla_v5.py (por import).
  * Edita el XML como texto, ubicando cada parrafo por su w14:paraId. Antes de tocar nada
    comprueba una huella no sensible de cada ancla; si no coincide, aborta.
  * El verde va como CENTINELA (2F6F12 = BrandingColors.DefaultPrimary), que ReportService
    reemplaza al generar (B-R2b). El tinte (E8F3DF) NO va en la plantilla: lo aplica B-R5 en
    codigo. Si la v4 ya trae alguno de los dos valores, aborta.
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
import build_plantilla_v4 as v4  # noqa: E402
import build_plantilla_v5 as v5  # noqa: E402

Falla = v4.Falla
esc = v4.esc
para_id = v4.para_id
para_text = v4.para_text
PARA_RE = v4.PARA_RE
Ids = v4.Ids

children = v5.children
set_children = v5.set_children
get_child = v5.get_child
get_ppr = v5.get_ppr
with_ppr = v5.with_ppr
runs_of = v5.runs_of
restyle_runs = v5.restyle_runs
rpr = v5.rpr
color = v5.color
sz = v5.sz
run = v5.run
runs = v5.runs
root_of = v5.root_of
PPR_ORDER = v5.PPR_ORDER
RPR_ORDER = v5.RPR_ORDER
ARIAL = v5.ARIAL
CG = v5.CG
BOLD = v5.BOLD

REL_HEADER = v5.REL_HEADER
REL_FOOTER = v5.REL_FOOTER
CT_HEADER = v5.CT_HEADER
CT_FOOTER = v5.CT_FOOTER
FIXED_DATE = v4.FIXED_DATE

# ── Paleta (5.0) ───────────────────────────────────────────────────────────────
PRIMARY = "2F6F12"   # centinela del verde (= BrandingColors.DefaultPrimary)
ACCENT = "E8F3DF"    # tinte (= BrandingColors.DefaultAccent); NO va en la plantilla
INK = "0E1013"       # tinta: titulos, valores, nombres de archivo y hashes
TEXT2 = "3D444C"     # texto secundario: cuerpo y subtitulos
TEXT3 = "5C656E"     # texto terciario: etiquetas, encabezado y pie
LINE = "D9DDE1"      # linea gris

TEXT_W = 8647        # ancho de texto (dxa), portada e interior

COURIER = ('<w:rFonts w:ascii="Courier New" w:hAnsi="Courier New" '
           'w:eastAsia="Courier New" w:cs="Courier New"/>')
CAPS = "<w:caps/>"
TRACK = '<w:spacing w:val="20"/>'   # 1 pt de interletrado

TINY_RPR = rpr(sz(2))
# Etiquetas de fichas y encabezado de la tabla: Arial negrita caps 8 pt gris, interletrado.
LABEL_RPR = rpr(ARIAL, BOLD, CAPS, color(TEXT3), TRACK, sz(16))
# Titulos de seccion: Arial negrita caps 11 pt tinta, interletrado.
TITLE_RPR = rpr(ARIAL, BOLD, CAPS, color(INK), TRACK, sz(22))
# Numero romano: Century Gothic 20 pt verde, sin negrita.
NUM_RPR = rpr(CG, color(PRIMARY), sz(40))
ARIAL8GRIS = rpr('<w:rFonts w:ascii="Arial" w:hAnsi="Arial" w:cs="Arial"/>', color(TEXT3), sz(16))

ATTRIBUTION_SLOT = "{ATRIBUCION_FACTUM}"

# ── Placeholders esperados por parte (B8) ──────────────────────────────────────
BODY_PLACEHOLDERS = set(v4.BODY_PLACEHOLDERS) | {"{ISOTIPO_ORGANIZACION:2x2}"}
BODY_BLOCKS = set(v4.BODY_BLOCKS) | {"ISOTIPO"}
HEADER1_PLACEHOLDERS = {"{tipoCausa}", "{numeroCausa}", "{ORGANIZACION}"}
FOOTER1_PLACEHOLDERS = {ATTRIBUTION_SLOT}
FOOTER2_PLACEHOLDERS = {"{LOGO_ORGANIZACION:4.5x2.5}", "{ORGANIZACION}", "{CONTACTO}"}
FOOTER2_BLOCKS = {"MEMBRETE", "MEMBRETE_CON_LOGO", "MEMBRETE_SIN_LOGO"}

# ── Anclas (paraIds de la v4) ──────────────────────────────────────────────────
NUMBERED_TITLES = ["0BF0C8CD", "0386A4A4", "0F4EDFCE", "5B3056CF", "0FF62E21", "3A62DE0D",
                   "37BC776D", "5A06ED76", "390F7DAB", "05D9340B", "10E394C7"]
PLAIN_TITLES = ["3B831F98", "18A07FBA"]
SEPARATORS = ["262A3013", "236C9BC4", "48125D00", "3A37F215", "4A82988C", "2DD0B2AA",
              "4F0246D1"]
BLOCKS = (v5.BLOCK_REFERENCIA, v5.BLOCK_IDENTIFICACION, v5.BLOCK_ELEMENTOS)
TITLE_DOC = "0DF4CF61"
ANEXO_TITLE = "6A0EA424"
FIRMA_CARACTER = v5.FIRMA_CARACTER
HASH_HEAD = ("04E11EDC", "4B698602")


# ── Parrafos nuevos (semilla "v6:") ────────────────────────────────────────────

def para(ids, seed, ppr, content=""):
    return '<w:p w14:paraId="%s">%s%s</w:p>' % (ids.new("v6:" + seed), ppr, content)


def marker(ids, seed, text):
    return para(ids, seed, '<w:pPr><w:spacing w:before="0" w:after="0" w:line="240" '
                           'w:lineRule="auto"/></w:pPr>', run(text, ""))


TINY_PPR = ('<w:pPr><w:spacing w:before="0" w:after="%d" w:line="240" w:lineRule="auto"/>'
            '%s</w:pPr>')


def tiny(ids, seed, after=0, extra=""):
    return para(ids, seed, (TINY_PPR % (after, TINY_RPR)).replace("</w:pPr>", extra + "</w:pPr>"))


def rule_ppr(sz_border, ind, spacing, keep=False, jc=""):
    """Filete: borde superior de un parrafo vacio con sangria (D7)."""
    return ('<w:pPr>%s<w:pBdr><w:top w:val="single" w:sz="%d" w:space="0" w:color="%s"/></w:pBdr>'
            '<w:spacing %s/><w:ind %s/>%s%s</w:pPr>') % (
        "<w:keepNext/>" if keep else "", sz_border, PRIMARY, spacing, ind,
        '<w:jc w:val="%s"/>' % jc if jc else "", TINY_RPR)


def nil_borders(*names):
    return "".join('<w:%s w:val="nil"/>' % n for n in names)


def line(name, size=6, c=LINE):
    return '<w:%s w:val="single" w:sz="%d" w:space="0" w:color="%s"/>' % (name, size, c)


def cell_mar(top, left, bottom, right):
    return ('<w:tblCellMar><w:top w:w="%d" w:type="dxa"/><w:left w:w="%d" w:type="dxa"/>'
            '<w:bottom w:w="%d" w:type="dxa"/><w:right w:w="%d" w:type="dxa"/></w:tblCellMar>'
            % (top, left, bottom, right))


def tbl(grid, borders, mar, rows_xml):
    return ('<w:tbl><w:tblPr><w:tblW w:w="%d" w:type="dxa"/><w:tblInd w:w="0" w:type="dxa"/>'
            '<w:tblBorders>%s</w:tblBorders><w:tblLayout w:type="fixed"/>%s</w:tblPr>'
            '<w:tblGrid>%s</w:tblGrid>%s</w:tbl>') % (
        sum(grid), borders, mar, "".join('<w:gridCol w:w="%d"/>' % g for g in grid), rows_xml)


def tc(width, content, valign="top", extra=""):
    return ('<w:tc><w:tcPr><w:tcW w:w="%d" w:type="dxa"/>%s<w:vAlign w:val="%s"/></w:tcPr>%s</w:tc>'
            % (width, extra, valign, content))


# Bordes de las fichas (portada e interior): lineas grises arriba, abajo y entre filas.
CARD_BORDERS = (line("top") + '<w:left w:val="nil"/>' + line("bottom") + '<w:right w:val="nil"/>'
                + line("insideH") + '<w:insideV w:val="nil"/>')


# ── header1: encabezado de texto de las paginas interiores (5.4) ──────────────

def build_header1(footer_xml, ids):
    decl, open_, close = root_of(footer_xml, "w:hdr")
    cell_ppr = ('<w:pPr><w:spacing w:before="0" w:after="0" w:line="240" w:lineRule="auto"/>'
                '<w:jc w:val="%s"/>%s</w:pPr>')
    left = para(ids, "h1:causa", cell_ppr % ("left", ARIAL8GRIS),
                runs("Informe pericial técnico informático · {tipoCausa} N° {numeroCausa}",
                     ARIAL8GRIS))
    right = para(ids, "h1:org", cell_ppr % ("right", ARIAL8GRIS), run("{ORGANIZACION}", ARIAL8GRIS))
    borders = (nil_borders("top", "left") + line("bottom") + nil_borders("right", "insideH",
                                                                          "insideV"))
    grid = (5400, 3247)
    table = tbl(grid, borders, cell_mar(0, 0, 120, 0),
                '<w:tr>%s%s</w:tr>' % (tc(grid[0], left, "bottom"), tc(grid[1], right, "bottom")))
    return decl + "\n" + open_ + table + tiny(ids, "h1:fin") + close


# ── header2: vacio (la portada no lleva encabezado) ───────────────────────────

def build_header2(footer_xml, ids):
    decl, open_, close = root_of(footer_xml, "w:hdr")
    return decl + "\n" + open_ + tiny(ids, "h2:vacio") + close


# ── footer1: linea fina + slot de atribucion + "Pagina N de M" (5.4) ──────────

def build_footer1(footer_xml, ids):
    decl, open_, close = root_of(footer_xml, "w:ftr")
    rp = ARIAL8GRIS

    def field(code):
        return ('<w:r>%s<w:fldChar w:fldCharType="begin"/></w:r>'
                '<w:r>%s<w:instrText xml:space="preserve"> %s </w:instrText></w:r>'
                '<w:r>%s<w:fldChar w:fldCharType="separate"/></w:r>'
                '<w:r>%s<w:t>2</w:t></w:r>'
                '<w:r>%s<w:fldChar w:fldCharType="end"/></w:r>') % (rp, rp, code, rp, rp, rp)

    ppr = ('<w:pPr><w:pBdr><w:top w:val="single" w:sz="6" w:space="6" w:color="%s"/></w:pBdr>'
           '<w:tabs><w:tab w:val="right" w:pos="%d"/></w:tabs>'
           '<w:spacing w:before="0" w:after="0" w:line="240" w:lineRule="auto"/>'
           '<w:jc w:val="left"/>%s</w:pPr>') % (LINE, TEXT_W, rp)
    p = para(ids, "f1:pie", ppr,
             '<w:r>%s<w:t>%s</w:t></w:r>' % (rp, ATTRIBUTION_SLOT)
             + '<w:r>%s<w:tab/></w:r>' % rp
             + run("Página ", rp) + field("PAGE") + run(" de ", rp) + field("NUMPAGES"))
    return decl + "\n" + open_ + p + close


# ── footer2: identidad del estudio en la portada (5.3) ─────────────────────────

def build_footer2(footer_xml, ids):
    decl, open_, close = root_of(footer_xml, "w:ftr")
    org_rp = rpr(ARIAL, BOLD, color(INK), sz(21))
    contact_rp = rpr(ARIAL, color(TEXT3), sz(18))

    def ident_ppr(after, rp, jc="left"):
        return ('<w:pPr><w:spacing w:before="0" w:after="%d" w:line="240" w:lineRule="auto"/>'
                '<w:jc w:val="%s"/>%s</w:pPr>') % (after, jc, rp)

    def identity(prefix):
        return (para(ids, prefix + ":org", ident_ppr(60, org_rp), run("{ORGANIZACION}", org_rp))
                + para(ids, prefix + ":contacto", ident_ppr(0, contact_rp),
                       run("{CONTACTO}", contact_rp)))

    grid = (2851, 5796)
    logo = para(ids, "f2:logo", ident_ppr(0, ""), run("{LOGO_ORGANIZACION:4.5x2.5}", ""))
    table = tbl(grid, nil_borders("top", "left", "bottom", "right", "insideH", "insideV"),
                cell_mar(0, 0, 0, 0),
                '<w:tr>%s%s</w:tr>' % (
                    tc(grid[0], logo, "center",
                       '<w:tcMar><w:right w:w="300" w:type="dxa"/></w:tcMar>'),
                    tc(grid[1], identity("f2:con"), "center")))
    body = "".join([
        marker(ids, "f2:open", "{#MEMBRETE}"),
        marker(ids, "f2:con-open", "{#MEMBRETE_CON_LOGO}"),
        table,
        marker(ids, "f2:con-close", "{/MEMBRETE_CON_LOGO}"),
        marker(ids, "f2:sin-open", "{#MEMBRETE_SIN_LOGO}"),
        identity("f2:sin"),
        marker(ids, "f2:sin-close", "{/MEMBRETE_SIN_LOGO}"),
        marker(ids, "f2:close", "{/MEMBRETE}"),
        tiny(ids, "f2:fin", after=600),
    ])
    return decl + "\n" + open_ + body + close


# ── document.xml ───────────────────────────────────────────────────────────────

def fingerprint(paras, order):
    v5.fingerprint(paras, order)
    for pid in SEPARATORS:
        if pid not in paras or para_text(paras[pid]).strip() != "":
            raise Falla("ancla " + pid)
    if ANEXO_TITLE not in paras or not para_text(paras[ANEXO_TITLE]).strip().startswith("ANEXO"):
        raise Falla("ancla " + ANEXO_TITLE)
    for pid in NUMBERED_TITLES:
        if 'w:numId w:val="2"' not in paras[pid]:
            raise Falla("ancla " + pid)
    for pid in PLAIN_TITLES:
        if "<w:numPr>" in paras[pid]:
            raise Falla("ancla " + pid)


def cover(ids):
    title_rp = rpr(CG, color(INK), sz(68))
    sub_rp = rpr(ARIAL, color(TEXT2), sz(26))
    sect = ('<w:sectPr><w:headerReference w:type="default" r:id="rIdHdr2"/>'
            '<w:footerReference w:type="default" r:id="rIdFtr2"/><w:type w:val="nextPage"/>'
            '<w:pgSz w:w="11906" w:h="16838"/>'
            '<w:pgMar w:top="1440" w:right="1558" w:bottom="1417" w:left="1701" w:header="284" '
            'w:footer="567" w:gutter="0"/><w:cols w:space="708"/>'
            '<w:docGrid w:linePitch="360"/></w:sectPr>')

    def ppr(spacing, keep=False, rp="", ind='w:left="0"'):
        return '<w:pPr>%s<w:spacing %s/><w:ind %s/><w:jc w:val="left"/>%s</w:pPr>' % (
            "<w:keepNext/>" if keep else "", spacing, ind, rp)

    title_sp = 'w:before="0" w:after="%d" w:line="760" w:lineRule="exact"'

    # Ficha de la causa (T1).
    grid = (1920, 6727)
    cell_ppr = ('<w:pPr><w:spacing w:before="0" w:after="0" w:line="276" w:lineRule="auto"/>'
                '<w:ind w:left="0" w:firstLine="0"/><w:jc w:val="left"/>%s</w:pPr>')
    label_rp = LABEL_RPR
    value_rp = rpr(ARIAL, color(INK), sz(22))
    rows = []
    for key, label, value in (("causa", "Causa", "{tipoCausa} N° {numeroCausa}"),
                              ("caratula", "Carátula", "“{caratula}”"),
                              ("perito", "Perito", "{nombrePerito} · M.P. {matriculaPerito}"),
                              ("fecha", "Fecha", "{fechaInspeccion}")):
        lab = para(ids, "cover:lbl:" + key, cell_ppr % label_rp, run(label, label_rp))
        val = para(ids, "cover:val:" + key, cell_ppr % value_rp, runs(value, value_rp))
        rows.append('<w:tr><w:trPr><w:cantSplit/></w:trPr>%s%s</w:tr>'
                    % (tc(grid[0], lab), tc(grid[1], val)))
    card = tbl(grid, CARD_BORDERS, cell_mar(165, 0, 165, 108), "".join(rows))

    return "".join([
        para(ids, "p1", ppr('w:before="0" w:after="0" w:line="1800" w:lineRule="exact"',
                            rp=TINY_RPR)),
        para(ids, "p2", rule_ppr(36, 'w:left="0" w:right="%d"' % (TEXT_W - 1680),
                                 'w:before="0" w:after="540" w:line="40" w:lineRule="exact"',
                                 keep=True, jc="left")),
        para(ids, "p3", ppr(title_sp % 0, keep=True, rp=title_rp), run("Informe pericial", title_rp)),
        para(ids, "p4", ppr(title_sp % 270, keep=True, rp=title_rp),
             run("técnico informático", title_rp)),
        para(ids, "p5", ppr('w:before="0" w:after="1650" w:line="240" w:lineRule="auto"',
                            keep=True, rp=sub_rp),
             run("Inspección técnica de dispositivo móvil", sub_rp)),
        card,
        para(ids, "p6", (TINY_PPR % (0, TINY_RPR)).replace("</w:pPr>", sect + "</w:pPr>")),
    ])


def number_para(ids, pid):
    return para(ids, "num:" + pid,
                '<w:pPr><w:keepNext/><w:numPr><w:ilvl w:val="0"/><w:numId w:val="2"/></w:numPr>'
                '<w:spacing w:before="360" w:after="0" w:line="240" w:lineRule="auto"/>'
                '<w:ind w:left="0" w:firstLine="0"/><w:contextualSpacing w:val="0"/>'
                '<w:jc w:val="left"/>%s</w:pPr>' % NUM_RPR)


def title_rule(ids, pid):
    return para(ids, "fil:" + pid,
                rule_ppr(18, 'w:left="0" w:right="%d"' % (TEXT_W - 540),
                         'w:before="0" w:after="180" w:line="40" w:lineRule="exact"', keep=True))


def strip_leading_space(p):
    """Recorta el espacio inicial del primer <w:t> (" CADENA DE ..."): mismo margen para todos."""
    m = re.search(r'(<w:t(?:\s[^>]*)?>)([^<]*)(</w:t>)', p)
    if not m or not m.group(2).startswith(" "):
        return p
    return p[:m.start()] + m.group(1) + m.group(2).lstrip(" ") + m.group(3) + p[m.end():]


def title_group(ids, paras, pid, numbered):
    p = paras[pid]
    ppr = set_children(get_ppr(p) or "<w:pPr></w:pPr>", "w:pPr", PPR_ORDER, {
        "w:keepNext": "<w:keepNext/>",
        "w:spacing": '<w:spacing w:before="%d" w:after="120" w:line="240" w:lineRule="auto"/>'
                     % (120 if numbered else 360),
        "w:ind": '<w:ind w:left="0" w:firstLine="0"/>',
        # contextualSpacing anularia el "before" entre parrafos Normal.
        "w:contextualSpacing": '<w:contextualSpacing w:val="0"/>',
        "w:jc": '<w:jc w:val="left"/>',
        "w:rPr": TITLE_RPR,
    }, remove=("w:pStyle", "w:numPr"))
    title = strip_leading_space(restyle_runs(with_ppr(p, ppr), TITLE_RPR))
    return (number_para(ids, pid) if numbered else "") + title + title_rule(ids, pid)


def separator(p):
    open_tag = re.match(r'<w:p\b[^>]*>', p).group(0)
    return open_tag + TINY_PPR % (0, TINY_RPR) + "</w:p>"


CELL_PPR = ('<w:pPr><w:spacing w:before="0" w:after="0" w:line="260" w:lineRule="auto"/>'
            '<w:ind w:left="0" w:firstLine="0"/><w:jc w:val="left"/>%s</w:pPr>')
VALUE_RPR = rpr(ARIAL, color(INK), sz(20))


def split_row(ids, paras, pid):
    """`**Etiqueta:** valor` → (parrafo etiqueta con paraId nuevo, parrafo valor original)."""
    p = paras[pid]
    rs = runs_of(p)
    if not rs or "<w:b/>" not in rs[0]:
        raise Falla("ancla " + pid)
    label = "".join(re.findall(r'<w:t(?:\s[^>]*)?>([^<]*)</w:t>', rs[0]))
    if not label.endswith(":"):
        raise Falla("ancla " + pid)
    rest = rs[1:]
    # El run que es solo " " entre etiqueta y valor no forma parte del texto comparado.
    if rest and v4.unesc("".join(re.findall(r'<w:t(?:\s[^>]*)?>([^<]*)</w:t>', rest[0]))) == " ":
        rest = rest[1:]
    open_tag = re.match(r'<w:p\b[^>]*>', p).group(0)
    lab = para(ids, "lbl:" + pid, CELL_PPR % LABEL_RPR, run(v4.unesc(label), LABEL_RPR))
    val = open_tag + CELL_PPR % VALUE_RPR + "".join(rest) + "</w:p>"
    return lab, restyle_runs(val, VALUE_RPR)


def data_card(ids, paras, rows):
    grid = (2490, 6157)
    trs = []
    for pid in rows:
        lab, val = split_row(ids, paras, pid)
        trs.append('<w:tr><w:trPr><w:cantSplit/></w:trPr>%s%s</w:tr>'
                   % (tc(grid[0], lab), tc(grid[1], val)))
    return tbl(grid, CARD_BORDERS, cell_mar(105, 0, 105, 108), "".join(trs))


def hash_table(tbl_xml):
    if tbl_xml.count("<w:tr ") != 2:
        raise Falla("ancla tabla")
    grid = (3090, 5557)
    t = tbl_xml
    t = re.sub(r'<w:tblW [^>]*/>', '<w:tblW w:w="%d" w:type="dxa"/>' % TEXT_W, t, count=1)
    t = re.sub(r'<w:tblInd [^>]*/>', '<w:tblInd w:w="0" w:type="dxa"/>', t, count=1)
    t = re.sub(r'<w:tblBorders>.*?</w:tblBorders>',
               '<w:tblBorders><w:top w:val="nil"/><w:left w:val="nil"/>' + line("bottom")
               + '<w:right w:val="nil"/>' + line("insideH") + '<w:insideV w:val="nil"/>'
               '</w:tblBorders>', t, count=1, flags=re.S)
    t = re.sub(r'<w:tblCellMar>.*?</w:tblCellMar>', cell_mar(100, 85, 100, 85), t, count=1,
               flags=re.S)
    t = re.sub(r'<w:tblGrid>.*?</w:tblGrid>',
               '<w:tblGrid>%s</w:tblGrid>' % "".join('<w:gridCol w:w="%d"/>' % g for g in grid),
               t, count=1, flags=re.S)
    for old, new in (('<w:tcW w:w="3600" w:type="dxa"/>', '<w:tcW w:w="%d" w:type="dxa"/>' % grid[0]),
                     ('<w:tcW w:w="4691" w:type="dxa"/>', '<w:tcW w:w="%d" w:type="dxa"/>' % grid[1])):
        if t.count(old) != 2:
            raise Falla("ancla tabla")
        t = t.replace(old, new)

    rows = [m.group(0) for m in re.finditer(r'<w:tr\b.*?</w:tr>', t, re.S)]
    head, model = rows

    head_borders = ('<w:tcBorders><w:top w:val="nil"/><w:left w:val="nil"/>'
                    + line("bottom", 8, PRIMARY) + '<w:right w:val="nil"/></w:tcBorders>')
    new_head = re.sub(r'<w:tcBorders>.*?</w:tcBorders>', head_borders, head, flags=re.S)

    def head_para(m):
        p = m.group(0)
        ppr = set_children(get_ppr(p), "w:pPr", PPR_ORDER,
                           {"w:jc": '<w:jc w:val="left"/>', "w:rPr": LABEL_RPR})
        return restyle_runs(with_ppr(p, ppr), LABEL_RPR)

    new_head = re.sub(r'<w:p\b.*?</w:p>', head_para, new_head, flags=re.S)
    if "w:shd" in new_head:
        raise Falla("ancla tabla")

    model_borders = ('<w:tcBorders><w:top w:val="nil"/><w:left w:val="nil"/>' + line("bottom")
                     + '<w:right w:val="nil"/></w:tcBorders>')
    new_model = re.sub(r'<w:tcBorders>.*?</w:tcBorders>', model_borders, model, flags=re.S)
    name_rp = rpr(ARIAL, color(INK), sz(18))
    hash_rp = rpr(COURIER, color(INK), sz(16))

    def model_para(m):
        p = m.group(0)
        rp = hash_rp if "{hashArchivo}" in para_text(p) else name_rp
        ppr = set_children(get_ppr(p), "w:pPr", PPR_ORDER, {"w:rPr": rp})
        return restyle_runs(with_ppr(p, ppr), rp)

    new_model = re.sub(r'<w:p\b.*?</w:p>', model_para, new_model, flags=re.S)
    return t.replace(head, new_head, 1).replace(model, new_model, 1)


def build_title_doc(p):
    """Titulo del escrito: sin subrayado ni negrita; linea 1 CG 12 tinta, linea 2 Arial 9 gris."""
    ppr = get_ppr(p)
    mark = get_child(ppr, "w:pPr", "w:rPr")
    new_mark = set_children(mark, "w:rPr", RPR_ORDER, {}, remove=("w:u", "w:b", "w:bCs"))
    new_ppr = set_children(ppr, "w:pPr", PPR_ORDER, {
        "w:spacing": '<w:spacing w:before="0" w:after="330" w:line="276" w:lineRule="auto"/>',
        "w:rPr": new_mark,
    })
    p = with_ppr(p, new_ppr)
    seen_br = [False]

    def fix(m):
        r = m.group(0)
        old = re.search(r'<w:rPr>.*?</w:rPr>', r, re.S)
        rp = old.group(0) if old else "<w:rPr></w:rPr>"
        is_br = "<w:br" in r
        if seen_br[0]:
            upd = {"w:rFonts": ARIAL, "w:color": color(TEXT2), "w:spacing": TRACK,
                   "w:sz": '<w:sz w:val="18"/>', "w:szCs": '<w:szCs w:val="18"/>'}
        else:
            upd = {"w:rFonts": CG, "w:color": color(INK), "w:spacing": TRACK,
                   "w:sz": '<w:sz w:val="24"/>', "w:szCs": '<w:szCs w:val="24"/>'}
        new_rp = set_children(rp, "w:rPr", RPR_ORDER, upd, remove=("w:u", "w:b", "w:bCs"))
        if is_br:
            seen_br[0] = True
        if old:
            return r.replace(old.group(0), new_rp, 1)
        o = re.match(r'<w:r\b[^>]*>', r).group(0)
        return o + new_rp + r[len(o):]

    p = re.sub(r'<w:r>.*?</w:r>|<w:r\s[^>]*>.*?</w:r>', fix, p, flags=re.S)
    if "<w:u " in p or not seen_br[0]:
        raise Falla("ancla " + TITLE_DOC)
    return p


def build_document(doc, ids):
    doc = v5.tnr_to_arial(doc)
    paras, order = {}, []
    for m in PARA_RE.finditer(doc):
        pid = para_id(m.group(0))
        if pid:
            paras[pid] = m.group(0)
            order.append(pid)
    fingerprint(paras, order)

    new, drop = {}, set()
    new[TITLE_DOC] = cover(ids) + build_title_doc(paras[TITLE_DOC])

    for pid in NUMBERED_TITLES:
        new[pid] = title_group(ids, paras, pid, numbered=True)
    for pid in PLAIN_TITLES:
        new[pid] = title_group(ids, paras, pid, numbered=False)
    for pid in SEPARATORS:
        new[pid] = separator(paras[pid])

    # Fichas a todo el ancho (5.4.2). La intro de Elementos queda como parrafo de cuerpo.
    for title, intro, rows, _ in BLOCKS:
        new[title] = new[title] + (paras[intro] if intro else "") + data_card(ids, paras, rows)
        drop.update(([intro] if intro else []) + rows)

    # Anexo: mismo aspecto que un titulo, centrado, sin numero ni filete.
    new[ANEXO_TITLE] = restyle_runs(paras[ANEXO_TITLE], TITLE_RPR)

    # Cierre: filete centrado + isotipo.
    new[FIRMA_CARACTER] = paras[FIRMA_CARACTER] + "".join([
        para(ids, "cierre:fil",
             rule_ppr(18, 'w:left="%d" w:right="%d"' % ((TEXT_W - 540) // 2 + 1,
                                                        (TEXT_W - 540) // 2),
                      'w:before="480" w:after="0" w:line="40" w:lineRule="exact"')),
        marker(ids, "cierre:open", "{#ISOTIPO}"),
        para(ids, "cierre:iso", '<w:pPr><w:spacing w:before="240" w:after="0" w:line="240" '
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

    out = PARA_RE.sub(sub, doc)

    # Tabla de hashes (5.4.3): la unica tabla de la v4 con tblStyle.
    m = re.search(r'<w:tbl><w:tblPr><w:tblStyle w:val="17"/>.*?</w:tbl>', out, re.S)
    if not m:
        raise Falla("ancla tabla")
    out = out[:m.start()] + hash_table(m.group(0)) + out[m.end():]

    # sectPr final (seccion 2): top 1418 y header 680.
    finals = re.findall(r'<w:sectPr>(?:(?!<w:sectPr>).)*?</w:sectPr>\s*</w:body>', out, re.S)
    if len(finals) != 1 or 'r:id="rIdHdr1"' not in finals[0]:
        raise Falla("ancla sectPr")
    fs = finals[0]
    fs2 = re.sub(r'w:top="\d+"', 'w:top="1418"', fs, count=1)
    fs2 = re.sub(r'w:header="\d+"', 'w:header="680"', fs2, count=1)
    return out.replace(fs, fs2, 1)


def build_numbering(num):
    m = re.search(r'<w:abstractNum\b[^>]*w:abstractNumId="1".*?</w:abstractNum>', num, re.S)
    if not m:
        raise Falla("ancla numbering")
    an = m.group(0)
    lvl = re.search(r'<w:lvl w:ilvl="0"[ >].*?</w:lvl>', an, re.S)
    if not lvl or "upperRoman" not in lvl.group(0):
        raise Falla("ancla numbering")
    open_ = re.match(r'<w:lvl\b[^>]*>', lvl.group(0)).group(0)
    lvl_order = ["w:start", "w:numFmt", "w:lvlRestart", "w:pStyle", "w:isLgl", "w:suff",
                 "w:lvlText", "w:lvlPicBulletId", "w:legacy", "w:lvlJc", "w:pPr", "w:rPr"]
    new_lvl = set_children(lvl.group(0), "w:lvl", lvl_order, {
        "w:suff": '<w:suff w:val="nothing"/>',
        "w:lvlText": '<w:lvlText w:val="%1"/>',
        "w:lvlJc": '<w:lvlJc w:val="left"/>',
        "w:pPr": '<w:pPr><w:ind w:left="0" w:hanging="0"/></w:pPr>',
        "w:rPr": NUM_RPR,
    })
    if not new_lvl.startswith(open_):
        raise Falla("ancla numbering")
    return num.replace(an, an.replace(lvl.group(0), new_lvl, 1), 1)


def check_numbering_anchor(num):
    """numId 2 → abstractNum 1, y ningun otro num usa abstractNum 1 (huella, B2)."""
    nums = re.findall(r'<w:num w:numId="(\d+)"[^>]*>.*?<w:abstractNumId w:val="(\d+)"/>', num, re.S)
    users = [n for n, a in nums if a == "1"]
    if users != ["2"]:
        raise Falla("ancla numbering")


def build_styles(st):
    st = v5.build_styles(st)
    d = re.search(r'<w:docDefaults>.*?</w:docDefaults>', st, re.S)
    rd = re.search(r'<w:rPrDefault><w:rPr>.*?</w:rPr></w:rPrDefault>', d.group(0), re.S) if d else None
    if not rd:
        raise Falla("ancla styles")
    inner = rd.group(0)[len("<w:rPrDefault>"):-len("</w:rPrDefault>")]
    new_rd = "<w:rPrDefault>" + set_children(inner, "w:rPr", RPR_ORDER,
                                             {"w:color": color(TEXT2)}) + "</w:rPrDefault>"
    st = st.replace(d.group(0), d.group(0).replace(rd.group(0), new_rd, 1), 1)
    n = re.search(r'<w:style w:type="paragraph" w:default="1" w:styleId="1">.*?</w:style>', st,
                  re.S)
    rn = re.search(r'<w:rPr>.*?</w:rPr>', n.group(0), re.S) if n else None
    if not rn:
        raise Falla("ancla styles")
    nn = n.group(0).replace(rn.group(0), set_children(rn.group(0), "w:rPr", RPR_ORDER,
                                                      {"w:color": color(TEXT2)}), 1)
    return st.replace(n.group(0), nn, 1)


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

    check_numbering_anchor(data["word/numbering.xml"].decode("utf-8"))

    used = set()
    for n in names:
        if n.endswith(".xml"):
            used.update(x.upper() for x in re.findall(r'w14:paraId="([0-9A-Fa-f]{8})"',
                                                      data[n].decode("utf-8")))
    ids = Ids(used)

    footer_src = data["word/footer1.xml"].decode("utf-8")
    doc = build_document(data["word/document.xml"].decode("utf-8"), ids)
    header1 = build_header1(footer_src, ids)
    header2 = build_header2(footer_src, ids)
    footer2 = build_footer2(footer_src, ids)
    footer1 = build_footer1(footer_src, ids)
    doc = v5.tnr_to_arial(doc)

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
    data["word/fontTable.xml"] = v5.build_fonts(
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
    hf = {n: x for n, x in xml_parts.items() if re.match(r"word/(header|footer)\d*\.xml$", n)}

    # 1. Placeholders por parte.
    v4.check_placeholders(doc, BODY_PLACEHOLDERS, BODY_BLOCKS, fails, "cuerpo")
    v4.check_placeholders(h1, HEADER1_PLACEHOLDERS, set(), fails, "header1")
    v4.check_placeholders(f1, FOOTER1_PLACEHOLDERS, set(), fails, "footer1")
    v4.check_placeholders(f2, FOOTER2_PLACEHOLDERS, FOOTER2_BLOCKS, fails, "footer2")
    for n, x in hf.items():
        if n not in ("word/header1.xml", "word/footer1.xml", "word/footer2.xml"):
            single, joined = v4.placeholders_of(x)
            if single or joined:
                fails.add("FALLA:placeholder-desconocido")
    if "<w:t" in h2:
        fails.add("FALLA:header2")

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
        if 'w:top="1418"' not in sects[1] or 'w:header="680"' not in sects[1]:
            fails.add("FALLA:sectPr")
    ct = xml_parts.get("[Content_Types].xml", "")
    for part, kind in (("header1", CT_HEADER), ("header2", CT_HEADER), ("footer1", CT_FOOTER),
                       ("footer2", CT_FOOTER)):
        if '<Override PartName="/word/%s.xml" ContentType="%s"/>' % (part, kind) not in ct:
            fails.add("FALLA:content-type")

    # 3. Sin formas flotantes en ninguna parte.
    for x in xml_parts.values():
        if "wp:anchor" in x or "wps:wsp" in x or "v:shape" in x:
            fails.add("FALLA:formas")

    # 4. Centinelas donde corresponde.
    rules = re.findall(r'<w:pBdr><w:top w:val="single" w:sz="(?:18|36)" w:space="0" '
                       r'w:color="%s"/></w:pBdr>' % PRIMARY, doc)
    if len(rules) != 15:
        fails.add("FALLA:centinela-filetes")
    if doc.count('<w:bottom w:val="single" w:sz="8" w:space="0" w:color="%s"/>' % PRIMARY) != 2:
        fails.add("FALLA:centinela-tabla")
    if '<w:color w:val="%s"/>' % PRIMARY not in num:
        fails.add("FALLA:centinela-numbering")
    for x in hf.values():
        if PRIMARY.lower() in x.lower():
            fails.add("FALLA:centinela-header-footer")
    for x in xml_parts.values():
        if ACCENT.lower() in x.lower():
            fails.add("FALLA:centinela-acento")

    # 5. Numeracion: 11 parrafos con numId 2, todos sin texto; los 13 titulos sin numPr.
    numbered = [p for p in PARA_RE.findall(doc) if '<w:numId w:val="2"/>' in p]
    if len(numbered) != 11 or any("<w:t" in p for p in numbered):
        fails.add("FALLA:numeracion-titulos")
    for p in PARA_RE.findall(doc):
        pid = para_id(p)
        if pid in NUMBERED_TITLES or pid in PLAIN_TITLES:
            if "<w:numPr>" in p:
                fails.add("FALLA:numeracion-titulos")
        if pid == TITLE_DOC and "<w:u " in p:
            fails.add("FALLA:subrayado")
    m = re.search(r'<w:tbl><w:tblPr><w:tblStyle w:val="17"/>.*?</w:tbl>', doc, re.S)
    head = re.search(r'<w:tr\b.*?</w:tr>', m.group(0), re.S).group(0) if m else ""
    if not head or "w:shd" in head or "HASH SHA-256" not in head:
        fails.add("FALLA:tabla")

    # 6. Estilos y fuentes.
    for n in ["word/document.xml", "word/styles.xml"] + list(hf):
        if "Times New Roman" in xml_parts.get(n, ""):
            fails.add("FALLA:times-new-roman")
    if "Century Gothic" not in xml_parts.get("word/fontTable.xml", ""):
        fails.add("FALLA:fontTable")
    if 'w:ascii="Arial"' not in sty or TEXT2 not in sty:
        fails.add("FALLA:styles")

    # 7. paraIds unicos, sin autor.
    all_ids = []
    for x in xml_parts.values():
        x = re.sub(r"<mc:Fallback\b.*?</mc:Fallback>", "", x, flags=re.S)
        all_ids += [i.upper() for i in re.findall(r'w14:paraId="([0-9A-Fa-f]{8})"', x)]
    if len(all_ids) != len(set(all_ids)):
        fails.add("FALLA:paraid-repetido")
    if any("w:author" in x for x in xml_parts.values()):
        fails.add("FALLA:autor")

    # 8. Pagina N de M.
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
