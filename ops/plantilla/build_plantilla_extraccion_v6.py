#!/usr/bin/env python3
"""Construye (o verifica) plantilla_informe_extraccion_v6.docx a partir de plantilla_informe_v6.docx.

Uso:
    python3 ops/plantilla/build_plantilla_extraccion_v6.py            # usa los paths por defecto en Templates/
    python3 ops/plantilla/build_plantilla_extraccion_v6.py <v6.docx> <extraccion.docx>
    python3 ops/plantilla/build_plantilla_extraccion_v6.py --check [<v6.docx> <extraccion.docx>]

Ver ops/plantilla/README.md y Refactorizaciones/informe-extraccion.md (§6).

Script HERMANO de build_plantilla_v6.py: lo importa como modulo (igual que build_plantilla_v6 importa
build_plantilla_v4) y HEREDA el diseno "Filete" sin tocarlo. El informe de extraccion reusa, byte a byte, las
partes de diseno de la v6 de inspeccion (styles, numbering, theme, settings, fontTable, header2/footer1/footer2 =
salida de build_styles/build_numbering/build_header2/build_footer1/build_footer2) y solo:
  * reescribe word/document.xml con las 10 secciones propias de una extraccion (§6.2);
  * reescribe la celda izquierda de word/header1.xml (encabezado interior) para que diga "Informe de extraccion
    logica · {equipoExtraccion}" en vez de la referencia de la causa (una extraccion no tiene causa).

El cuerpo se arma con los MISMOS patrones de parrafo/tabla de la v6 (portada con filete verde, titulo CG, fichas a
todo el ancho con lineas grises, titulos de seccion numerados con numId=2, filete del cierre + isotipo). El verde
va como CENTINELA (2F6F12 = BrandingColors.DefaultPrimary), que ReportService reemplaza al generar (B-R2b).

Las partes de corte de seccion de la portada (espaciador, filete y el parrafo con el <w:sectPr> que apunta a
header2/footer2) se copian EXACTAS de la v6 para conservar el cambio de seccion portada → interior.

Solo usa la biblioteca estandar de Python 3.
"""

import argparse
import os
import re
import sys
import zipfile

sys.dont_write_bytecode = True  # no dejar __pycache__ en ops/plantilla/
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_plantilla_v6 as v6  # noqa: E402  (reusa el modulo hermano y, por transitividad, v4/v5)

Falla = v6.Falla
esc = v6.esc
FIXED_DATE = v6.FIXED_DATE

TEMPLATES = os.path.normpath(os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "..",
    "server", "src", "Factum.Backend", "Templates"))
DEFAULT_SRC = os.path.join(TEMPLATES, "plantilla_informe_v6.docx")
DEFAULT_DST = os.path.join(TEMPLATES, "plantilla_informe_extraccion_v6.docx")

# ── Paleta (identica a la v6) ──────────────────────────────────────────────────
PRIMARY = "2F6F12"   # centinela del verde (= BrandingColors.DefaultPrimary)
INK = "0E1013"
TEXT2 = "3D444C"
TEXT3 = "5C656E"
LINE = "D9DDE1"

ARIAL = '<w:rFonts w:ascii="Arial" w:hAnsi="Arial" w:eastAsia="Arial" w:cs="Arial"/>'
CG = ('<w:rFonts w:ascii="Century Gothic" w:hAnsi="Century Gothic" '
      'w:eastAsia="Century Gothic" w:cs="Century Gothic"/>')

TITLE_RPR = ARIAL + '<w:b/><w:bCs/><w:caps/><w:color w:val="%s"/><w:spacing w:val="20"/><w:sz w:val="22"/><w:szCs w:val="22"/>' % INK
LABEL_RPR = ARIAL + '<w:b/><w:bCs/><w:caps/><w:color w:val="%s"/><w:spacing w:val="20"/><w:sz w:val="16"/><w:szCs w:val="16"/>' % TEXT3
VALUE_RPR = ARIAL + '<w:color w:val="%s"/><w:sz w:val="20"/><w:szCs w:val="20"/>' % INK
BODY_RPR = ARIAL
BODY_BOLD_RPR = ARIAL + '<w:b/><w:bCs/>'
SUBTITLE_RPR = ARIAL + '<w:color w:val="%s"/><w:sz w:val="26"/><w:szCs w:val="26"/>' % TEXT2
COVER_TITLE_RPR = CG + '<w:color w:val="%s"/><w:sz w:val="68"/><w:szCs w:val="68"/>' % INK
NUM_RPR = CG + '<w:color w:val="%s"/><w:sz w:val="40"/><w:szCs w:val="40"/>' % PRIMARY

CARD_BORDERS = ('<w:top w:val="single" w:sz="6" w:space="0" w:color="%s"/><w:left w:val="nil"/>'
                '<w:bottom w:val="single" w:sz="6" w:space="0" w:color="%s"/><w:right w:val="nil"/>'
                '<w:insideH w:val="single" w:sz="6" w:space="0" w:color="%s"/><w:insideV w:val="nil"/>'
                % (LINE, LINE, LINE))
CARD_MAR = ('<w:top w:w="105" w:type="dxa"/><w:left w:w="0" w:type="dxa"/>'
            '<w:bottom w:w="105" w:type="dxa"/><w:right w:w="108" w:type="dxa"/>')
CELL_PPR = ('<w:spacing w:before="0" w:after="0" w:line="260" w:lineRule="auto"/>'
            '<w:ind w:left="0" w:firstLine="0"/><w:jc w:val="left"/>')

COVER_SUBTITLE = "Informe de extracción lógica de dispositivo móvil"

# ── Generador de w14:paraId unicos ──────────────────────────────────────────────

class Ids:
    def __init__(self, used):
        self.used = {u.upper() for u in used}
        self.n = 0

    def new(self):
        # w14:paraId es un valor < 0x80000000 (restriccion del esquema); se usa el rango 0x7Exxxxxx.
        while True:
            self.n += 1
            cand = ("%08X" % (0x7E000000 + self.n)).upper()
            if cand not in self.used:
                self.used.add(cand)
                return cand


IDS = None  # inicializado en build()


def nid():
    return IDS.new()


# ── Helpers de XML (mismos patrones que la v6) ──────────────────────────────────

def run(rpr_inner, text):
    rp = ("<w:rPr>%s</w:rPr>" % rpr_inner) if rpr_inner else ""
    return '<w:r>%s<w:t xml:space="preserve">%s</w:t></w:r>' % (rp, esc(text))


def para(ppr_inner, runs=""):
    ppr = ("<w:pPr>%s</w:pPr>" % ppr_inner) if ppr_inner else ""
    return '<w:p w14:paraId="%s">%s%s</w:p>' % (nid(), ppr, runs)


def marker(text):
    """Parrafo cuyo texto es EXACTAMENTE un marcador de bloque ({#clave}/{/clave})."""
    return para('<w:spacing w:before="0" w:after="0" w:line="240" w:lineRule="auto"/>', run("", text))


def body_ph(ph):
    """Parrafo cuyo texto es EXACTAMENTE un placeholder multilinea (lo expande ReplaceParagraphPerLine)."""
    return para('<w:spacing w:before="0" w:after="120" w:line="276" w:lineRule="auto"/><w:jc w:val="both"/>',
                run(BODY_RPR, ph))


def body_text(*parts):
    """Parrafo de texto justificado; parts = (rpr, text)."""
    return para('<w:spacing w:before="0" w:after="120" w:line="276" w:lineRule="auto"/><w:jc w:val="both"/>',
                "".join(run(rp, t) for rp, t in parts))


def tc(width, content):
    return ('<w:tc><w:tcPr><w:tcW w:w="%d" w:type="dxa"/><w:vAlign w:val="top"/></w:tcPr>%s</w:tc>'
            % (width, content))


def tbl(grid, rows):
    return ('<w:tbl><w:tblPr><w:tblW w:w="8647" w:type="dxa"/><w:tblInd w:w="0" w:type="dxa"/>'
            '<w:tblBorders>%s</w:tblBorders><w:tblLayout w:type="fixed"/>'
            '<w:tblCellMar>%s</w:tblCellMar></w:tblPr><w:tblGrid>%s</w:tblGrid>%s</w:tbl>'
            % (CARD_BORDERS, CARD_MAR, grid, rows))


def card(rows):
    """Ficha a dos columnas (etiqueta | valor). rows = (label, runs_valor)."""
    grid = '<w:gridCol w:w="2490"/><w:gridCol w:w="6157"/>'
    trs = ""
    for label, value in rows:
        trs += ('<w:tr><w:trPr><w:cantSplit/></w:trPr>'
                + tc(2490, para(CELL_PPR, run(LABEL_RPR, label)))
                + tc(6157, para(CELL_PPR, value))
                + '</w:tr>')
    return tbl(grid, trs)


def heading(title):
    """Titulo de seccion numerado (numId=2 → I., II., …) + titulo en Arial negrita caps."""
    num = para('<w:keepNext/><w:numPr><w:ilvl w:val="0"/><w:numId w:val="2"/></w:numPr>'
               '<w:spacing w:before="360" w:after="0" w:line="240" w:lineRule="auto"/>'
               '<w:ind w:left="0" w:firstLine="0"/><w:contextualSpacing w:val="0"/><w:jc w:val="left"/>'
               '<w:rPr>%s</w:rPr>' % NUM_RPR)
    ttl = para('<w:keepNext/><w:spacing w:before="120" w:after="120" w:line="240" w:lineRule="auto"/>'
               '<w:ind w:left="0" w:firstLine="0"/><w:contextualSpacing w:val="0"/><w:jc w:val="left"/>',
               run(TITLE_RPR, title))
    return num + ttl


def category_table():
    """Tabla de inventario (§5): fila de encabezado + fila MODELO que clona FillCategoryTable."""
    grid = '<w:gridCol w:w="2400"/><w:gridCol w:w="3400"/><w:gridCol w:w="2847"/>'
    head = ('<w:tr><w:trPr><w:cantSplit/><w:tblHeader/></w:trPr>'
            + tc(2400, para(CELL_PPR, run(LABEL_RPR, "Categoría")))
            + tc(3400, para(CELL_PPR, run(LABEL_RPR, "Método")))
            + tc(2847, para(CELL_PPR, run(LABEL_RPR, "Resultado")))
            + '</w:tr>')
    model = ('<w:tr><w:trPr><w:cantSplit/></w:trPr>'
             + tc(2400, para(CELL_PPR, run(VALUE_RPR, "{categoriaExtraccion}")))
             + tc(3400, para(CELL_PPR, run(VALUE_RPR, "{metodoExtraccion}")))
             + tc(2847, para(CELL_PPR, run(VALUE_RPR, "{resultadoExtraccion}")))
             + '</w:tr>')
    return tbl(grid, head + model)


# ── Cuerpo del documento ────────────────────────────────────────────────────────

def extract_para(body, pid):
    i = body.index('<w:p w14:paraId="%s"' % pid)
    j = body.index('</w:p>', i) + len('</w:p>')
    return body[i:j]


def build_document(src_doc):
    body = src_doc[src_doc.index("<w:body>") + len("<w:body>"):src_doc.index("</w:body>")]
    # Partes EXACTAS de la portada que se conservan (filete y corte de seccion a header2/footer2).
    spacer = extract_para(body, "20C98572")
    filete = extract_para(body, "1CF90477")
    section_break = extract_para(body, "2E11480D")  # <w:sectPr> → header2/footer2
    final_sectpr = src_doc[src_doc.rindex("<w:sectPr"):src_doc.index("</w:body>")]

    # Portada: titulo (CG), subtitulo, ficha de la extraccion.
    cover_title = para(
        '<w:keepNext/><w:spacing w:before="0" w:after="0" w:line="760" w:lineRule="exact"/>'
        '<w:ind w:left="0"/><w:jc w:val="left"/><w:rPr>%s</w:rPr>' % COVER_TITLE_RPR,
        run(COVER_TITLE_RPR, "Informe pericial"))
    cover_title2 = para(
        '<w:keepNext/><w:spacing w:before="0" w:after="270" w:line="760" w:lineRule="exact"/>'
        '<w:ind w:left="0"/><w:jc w:val="left"/><w:rPr>%s</w:rPr>' % COVER_TITLE_RPR,
        run(COVER_TITLE_RPR, "de extracción lógica"))
    subtitle = para(
        '<w:keepNext/><w:spacing w:before="0" w:after="1650" w:line="240" w:lineRule="auto"/>'
        '<w:ind w:left="0"/><w:jc w:val="left"/><w:rPr>%s</w:rPr>' % SUBTITLE_RPR,
        run(SUBTITLE_RPR, COVER_SUBTITLE))
    cover_card = card([
        ("Equipo", run(VALUE_RPR, "{equipoExtraccion}")),
        ("Fecha", run(VALUE_RPR, "{fechaExtraccion}")),
        ("Estado", run(VALUE_RPR, "{estadoExtraccion}")),
    ])

    # Titulo del documento (seccion interior).
    title_doc = para(
        '<w:spacing w:before="0" w:after="330" w:line="276" w:lineRule="auto"/>'
        '<w:ind w:right="-143"/><w:jc w:val="center"/>'
        '<w:rPr>%s<w:kern w:val="0"/></w:rPr>' % ARIAL,
        run(CG + '<w:color w:val="%s"/><w:spacing w:val="20"/><w:kern w:val="0"/><w:sz w:val="24"/><w:szCs w:val="24"/>' % INK,
            "INFORME PERICIAL DE EXTRACCIÓN LÓGICA")
        + '<w:r><w:rPr>%s</w:rPr><w:br w:type="textWrapping"/></w:r>' % ARIAL
        + run(ARIAL + '<w:color w:val="%s"/><w:spacing w:val="20"/><w:sz w:val="18"/><w:szCs w:val="18"/>' % TEXT2,
              "EXTRACCIÓN LÓGICA DE DISPOSITIVO MÓVIL"))

    sections = []

    # 1 · Identificación
    ident_rows = [
        ("Perito", run(VALUE_RPR, "{peritoExtraccion}")),
        ("DNI", run(VALUE_RPR, "{dniPerito}")),
        ("Estación de trabajo", run(VALUE_RPR, "{pcExtraccion}")),
        ("Usuario del sistema", run(VALUE_RPR, "{usuarioSoExtraccion}")),
        ("Versión de Tatana", run(VALUE_RPR, "{versionTatana}")),
        ("Versión de Factum", run(VALUE_RPR, "{versionFactum}")),
    ]
    sections.append(heading("Identificación") + card(ident_rows)
                    + marker("{#actuacionCaso}")
                    + body_text((BODY_BOLD_RPR, "Actuación asociada: "), (BODY_RPR, "{actuacionCaso}"))
                    + marker("{/actuacionCaso}"))

    # 2 · Tiempos
    sections.append(heading("Tiempos de la extracción") + card([
        ("Inicio", run(VALUE_RPR, "{inicioExtraccion}")),
        ("Fin", run(VALUE_RPR, "{finExtraccion}")),
    ]))

    # 3 · Dispositivo (bloque fichaTecnica: desaparece sin ficha)
    ficha_rows = [
        ("Ficha obtenida", run(VALUE_RPR, "{fechaFichaTecnica}") + run(VALUE_RPR, ", ") + run(VALUE_RPR, "{horaFichaTecnica}")),
        ("Sistema operativo", run(VALUE_RPR, "{fichaSistemaOperativo}")),
        ("Compilación", run(VALUE_RPR, "{fichaCompilacion}")),
        ("Parche de seguridad", run(VALUE_RPR, "{fichaParcheSeguridad}")),
        ("Número de serie", run(VALUE_RPR, "{fichaNumeroSerie}")),
        ("Identificador de conexión", run(VALUE_RPR, "{fichaIdentificadorConexion}")),
        ("Kernel", run(VALUE_RPR, "{fichaKernel}")),
        ("Operador / SIM", run(VALUE_RPR, "{fichaOperadorSim}")),
        ("Estado de arranque", run(VALUE_RPR, "{fichaEstadoArranque}")),
        ("Zona horaria", run(VALUE_RPR, "{fichaZonaHoraria}")),
        ("Wi-Fi", run(VALUE_RPR, "{fichaWifi}")),
    ]
    sections.append(marker("{#fichaTecnica}") + heading("Dispositivo")
                    + card(ficha_rows) + body_ph("{descripcionRelojExtraccion}")
                    + marker("{/fichaTecnica}"))

    # 4 · Metodología y herramientas
    sections.append(heading("Metodología y herramientas") + body_ph("{descripcionMetodologia}"))

    # 5 · Inventario de lo extraído
    sections.append(heading("Inventario de lo extraído") + category_table()
                    + body_text((BODY_RPR, "{totalesExtraccion}"))
                    + body_text((BODY_RPR, "El detalle archivo por archivo figura en el MANIFIESTO.tsv de la "
                                           "carpeta de la extracción, al que este informe remite para su verificación.")))

    # 6 · Integridad y cadena de custodia
    integ_rows = [
        ("Hash del manifiesto", run(VALUE_RPR, "{hashManifiesto}")),
        ("Entradas del manifiesto", run(VALUE_RPR, "{entradasManifiesto}")),
        ("Ubicación de la carpeta", run(VALUE_RPR, "{ubicacionCarpeta}")),
    ]
    sections.append(heading("Integridad y cadena de custodia") + card(integ_rows)
                    + body_ph("{hashDispositivoExtraccion}")
                    + body_ph("{descripcionSelloExtraccion}")
                    + marker("{#selloTiempo}")
                    + body_text((BODY_RPR, "Este informe remite al sello de tiempo del manifiesto como fecha "
                                           "cierta de la integridad del conjunto."))
                    + marker("{/selloTiempo}")
                    + body_ph("{descripcionCadenaCustodia}"))

    # 7 · Modificaciones al dispositivo
    sections.append(heading("Modificaciones al dispositivo") + body_ph("{descripcionModificaciones}"))

    # 8 · Interrupción / cancelación (bloque: solo si aplica)
    sections.append(marker("{#interrupcion}") + heading("Interrupción o cancelación")
                    + body_ph("{descripcionInterrupcion}") + marker("{/interrupcion}"))

    # 9 · Alcances y limitaciones
    sections.append(heading("Alcances y limitaciones") + body_ph("{descripcionAlcances}"))

    # 10 · Firma del perito + isotipo del cierre
    firma = (para('<w:spacing w:after="0" w:line="360" w:lineRule="auto"/><w:jc w:val="both"/>')
             + para('<w:keepNext/><w:spacing w:before="720" w:after="0" w:line="360" w:lineRule="auto"/><w:jc w:val="center"/>',
                    run(ARIAL, "______________________________"))
             + para('<w:keepNext/><w:keepLines/><w:spacing w:after="0" w:line="360" w:lineRule="auto"/><w:jc w:val="center"/>',
                    run(BODY_BOLD_RPR, "{peritoExtraccion}"))
             + para('<w:keepNext/><w:keepLines/><w:spacing w:after="0" w:line="360" w:lineRule="auto"/><w:jc w:val="center"/>',
                    run(ARIAL, "DNI ") + run(ARIAL, "{dniPerito}"))
             + para('<w:keepNext/><w:pBdr><w:top w:val="single" w:sz="18" w:space="0" w:color="%s"/></w:pBdr>'
                    '<w:spacing w:before="480" w:after="0" w:line="40" w:lineRule="exact"/>'
                    '<w:ind w:left="4054" w:right="4053"/><w:rPr><w:sz w:val="2"/><w:szCs w:val="2"/></w:rPr>' % PRIMARY)
             + marker("{#ISOTIPO}")
             + para('<w:spacing w:before="240" w:after="0" w:line="240" w:lineRule="auto"/><w:jc w:val="center"/>',
                    run("", "{ISOTIPO_ORGANIZACION:2x2}"))
             + marker("{/ISOTIPO}"))

    body_xml = (spacer + filete + cover_title + cover_title2 + subtitle + cover_card + section_break
                + title_doc + "".join(sections) + firma + final_sectpr)
    return src_doc[:src_doc.index("<w:body>") + len("<w:body>")] + body_xml + "</w:body></w:document>"


def build_header1(src_header1):
    """Reescribe la celda izquierda: 'Informe de extraccion logica · {equipoExtraccion}'."""
    rpr = ARIAL + '<w:color w:val="%s"/><w:sz w:val="16"/><w:szCs w:val="16"/>' % TEXT3
    new_runs = run(rpr, "Informe de extracción lógica · ") + run(rpr, "{equipoExtraccion}")
    # La celda izquierda tiene 4 runs: fijo + {tipoCausa} + " N° " + {numeroCausa}. Se reemplazan por los nuevos.
    pat = re.compile(r'(<w:p w14:paraId="4739E209">.*?</w:pPr>)(.*?)(</w:p>)', re.S)
    m = pat.search(src_header1)
    if not m:
        raise Falla("ancla header1 (parrafo de la causa) no encontrada")
    return src_header1[:m.start()] + m.group(1) + new_runs + m.group(3) + src_header1[m.end():]


# ── Ensamblado del docx ──────────────────────────────────────────────────────────

def read_parts(path):
    with zipfile.ZipFile(path) as z:
        return [(i.filename, z.read(i.filename)) for i in z.infolist() if not i.filename.endswith("/")]


def build(src, dst):
    global IDS
    parts = read_parts(src)
    names = [n for n, _ in parts]
    data = dict(parts)
    for required in ("word/document.xml", "word/header1.xml", "word/footer2.xml", "[Content_Types].xml"):
        if required not in data:
            raise Falla("parte " + required)

    used = set()
    for n in names:
        if n.endswith(".xml"):
            used.update(re.findall(r'w14:paraId="([0-9A-Fa-f]{8})"', data[n].decode("utf-8")))
    IDS = Ids(used)

    data["word/document.xml"] = build_document(data["word/document.xml"].decode("utf-8")).encode("utf-8")
    data["word/header1.xml"] = build_header1(data["word/header1.xml"].decode("utf-8")).encode("utf-8")

    with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as z:
        order = ["[Content_Types].xml"] + [n for n in names if n != "[Content_Types].xml"]
        for n in order:
            info = zipfile.ZipInfo(n, date_time=FIXED_DATE)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 0
            info.external_attr = 0
            z.writestr(info, data[n])


# ── Verificacion (§6.1: placeholders presentes y sin huerfanos, ficha, fila modelo) ──

PH_RE = re.compile(r'\{[#/]?[A-Za-z_][A-Za-z0-9_]*(?::[0-9.,x]+)?\}')
# Solo <w:t> y <w:t …> (no <w:tbl>, <w:tc>, <w:tr>, que tambien empiezan con "<w:t").
T_RE = re.compile(r'<w:t(?: [^>]*)?>(.*?)</w:t>', re.S)

# Placeholders de una linea que deben aparecer, cada uno, en su propio <w:t>.
EXPECTED_SINGLE = [
    "{ORGANIZACION}", "{equipoExtraccion}", "{fechaExtraccion}", "{estadoExtraccion}",
    "{peritoExtraccion}", "{dniPerito}", "{pcExtraccion}", "{usuarioSoExtraccion}", "{versionTatana}",
    "{versionFactum}", "{actuacionCaso}", "{inicioExtraccion}", "{finExtraccion}",
    "{fechaFichaTecnica}", "{horaFichaTecnica}", "{fichaSistemaOperativo}", "{fichaCompilacion}",
    "{fichaParcheSeguridad}", "{fichaNumeroSerie}", "{fichaIdentificadorConexion}", "{fichaKernel}",
    "{fichaOperadorSim}", "{fichaEstadoArranque}", "{fichaZonaHoraria}", "{fichaWifi}",
    "{descripcionRelojExtraccion}", "{descripcionMetodologia}", "{totalesExtraccion}",
    "{categoriaExtraccion}", "{metodoExtraccion}", "{resultadoExtraccion}",
    "{hashManifiesto}", "{entradasManifiesto}", "{hashDispositivoExtraccion}", "{ubicacionCarpeta}",
    "{descripcionSelloExtraccion}", "{descripcionCadenaCustodia}", "{descripcionModificaciones}",
    "{descripcionInterrupcion}", "{descripcionAlcances}",
]
EXPECTED_BLOCKS = ["actuacionCaso", "fichaTecnica", "selloTiempo", "interrupcion", "ISOTIPO"]
# Placeholders conocidos del motor que viven en las partes heredadas (footer/header) — no son huerfanos.
INHERITED = {"{CONTACTO}", "{LOGO_ORGANIZACION:4.5x2.5}", "{ATRIBUCION_FACTUM}", "{ISOTIPO_ORGANIZACION:2x2}"}


def norm(s):
    return re.sub(r"\s+", " ", s).strip()


def check(src, dst):
    if not os.path.exists(dst):
        return ["FALLA:no_existe_destino"]
    data = dict(read_parts(dst))
    fails = []
    all_xml = "".join(v.decode("utf-8") for k, v in data.items() if k.endswith(".xml"))

    # Cada placeholder esperado, en su propio <w:t>.
    single_texts = []
    for xml in (data["word/document.xml"].decode("utf-8"), data["word/header1.xml"].decode("utf-8"),
                data["word/footer1.xml"].decode("utf-8"), data["word/footer2.xml"].decode("utf-8")):
        single_texts += [t.strip() for t in T_RE.findall(xml)]
    present = set(single_texts)
    for ph in EXPECTED_SINGLE:
        if ph not in present:
            fails.append("FALLA:falta_placeholder:" + ph)

    # Marcadores de bloque, abren y cierran.
    for key in EXPECTED_BLOCKS:
        if ("{#%s}" % key) not in present or ("{/%s}" % key) not in present:
            fails.append("FALLA:bloque_incompleto:" + key)

    # Sin placeholders huerfanos (fuera de los esperados, los de bloque y los heredados del motor).
    known = set(EXPECTED_SINGLE) | INHERITED
    for key in EXPECTED_BLOCKS + ["MEMBRETE", "MEMBRETE_CON_LOGO", "MEMBRETE_SIN_LOGO"]:
        known.add("{#%s}" % key)
        known.add("{/%s}" % key)
    for ph in set(PH_RE.findall(all_xml)):
        if ph not in known:
            fails.append("FALLA:placeholder_huerfano:" + ph)

    # Ficha tecnica: las 11 filas entre {#fichaTecnica} y {/fichaTecnica}.
    doc = data["word/document.xml"].decode("utf-8")
    try:
        ficha = doc[doc.index("{#fichaTecnica}"):doc.index("{/fichaTecnica}")]
        for ph in ("{fichaSistemaOperativo}", "{fichaNumeroSerie}", "{fichaKernel}", "{fichaWifi}"):
            if ph not in ficha:
                fails.append("FALLA:ficha_sin:" + ph)
    except ValueError:
        fails.append("FALLA:ficha_sin_marcadores")

    # Fila modelo de la tabla de categorias (los 3 placeholders en una misma <w:tr>).
    rows = re.findall(r"<w:tr>.*?</w:tr>", doc, re.S)
    if not any(all(p in r for p in ("{categoriaExtraccion}", "{metodoExtraccion}", "{resultadoExtraccion}"))
               for r in rows):
        fails.append("FALLA:sin_fila_modelo_categorias")

    # El subtitulo de extraccion y el corte de seccion a header2/footer2 siguen presentes.
    if COVER_SUBTITLE not in norm("".join(T_RE.findall(doc))):
        fails.append("FALLA:sin_subtitulo_extraccion")
    if "rIdHdr2" not in doc:
        fails.append("FALLA:sin_corte_seccion_portada")

    return fails


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    ap.add_argument("src", nargs="?", default=DEFAULT_SRC)
    ap.add_argument("dst", nargs="?", default=DEFAULT_DST)
    args = ap.parse_args()

    if args.check:
        fails = check(args.src, args.dst)
        if fails:
            print("\n".join(fails))
            sys.exit(1)
        print("OK")
        return

    build(args.src, args.dst)
    fails = check(args.src, args.dst)
    if fails:
        print("\n".join(fails))
        sys.exit(1)
    print("OK " + os.path.relpath(args.dst))


if __name__ == "__main__":
    main()
