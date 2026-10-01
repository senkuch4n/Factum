// Genera server/src/Factum.Backend/Templates/plantilla-inspeccion-tecnica.docx a partir de:
//   - Templates/portada-fondo.png (diseño de Canva, exportado a .pptx, renderizado a PNG con
//     los placeholders vaciados — ver server/tools/ReportTemplateBuilder/README.md)
//   - Las coordenadas EMU exactas de cada campo, extraídas del .pptx original
//
// La imagen de fondo se recorta en 3 franjas: encabezado (banda diagonal + título), cuerpo
// (ícono del celular, datos del dispositivo, paneles de foto, ZIP/hash/clave) y pie de página
// (barra de contacto). Encabezado y pie van en un Header/Footer de Word de verdad —> se repiten
// en TODAS las páginas. El cuerpo con posición fija ocupa la página 1; después de un salto de
// página, Observaciones/Contenido/Capturas siguen fluyendo libremente (pueden ocupar varias
// páginas), con el mismo header/footer repitiéndose arriba/abajo automáticamente.
//
// Correr con: dotnet run (desde esta carpeta). Ver README.md para cómo regenerar todo desde
// un nuevo export de Canva.

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
var templatesDir = Path.Combine(repoRoot, "server", "src", "Factum.Backend", "Templates");
var backgroundPath = Path.Combine(templatesDir, "portada-fondo.png");
var outputPath = Path.Combine(templatesDir, "plantilla-inspeccion-tecnica.docx");

if (!File.Exists(backgroundPath))
    throw new FileNotFoundException("Falta el fondo. Corré primero el paso de export+blanking (ver README.md).", backgroundPath);

// Tamaño de página: el mismo EMU exacto del .pptx de origen (evita cualquier reescalado de
// las coordenadas extraídas). 1 twip = 635 EMU. 1 px (a 300dpi) = 3048 EMU.
const long PageCx = 7_556_500;
const long PageCy = 10_693_400;
const int PageTwipsW = (int)(PageCx / 635);
const int PageTwipsH = (int)(PageCy / 635);
const int EmuPerPx = 3048;

// Franjas de encabezado/pie — recortadas de portada-fondo.png (2480×3508px, 300dpi).
const int HeaderPx = 540;
const int FooterStartPx = 3150;
const long HeaderH = HeaderPx * EmuPerPx;
const long FooterY = FooterStartPx * EmuPerPx;
const long FooterH = PageCy - FooterY;
const int HeaderTwips = (int)(HeaderH / 635);
const int FooterTwips = (int)(FooterH / 635);

// ── Recortar el fondo en header / body / footer, y sacar el azul de la banda como acento ──
string headerImgPath, bodyImgPath, footerImgPath;
string accentHex;
using (var full = Image.Load<Rgba32>(backgroundPath))
{
    var px = full[2400, 30];
    accentHex = $"{px.R:X2}{px.G:X2}{px.B:X2}";

    headerImgPath = Path.Combine(templatesDir, "header-fondo.png");
    bodyImgPath   = Path.Combine(templatesDir, "body-fondo.png");
    footerImgPath = Path.Combine(templatesDir, "footer-fondo.png");

    using (var header = full.Clone(c => c.Crop(new Rectangle(0, 0, full.Width, HeaderPx))))
        header.SaveAsPng(headerImgPath);
    using (var bodyImg = full.Clone(c => c.Crop(new Rectangle(0, HeaderPx, full.Width, FooterStartPx - HeaderPx))))
        bodyImg.SaveAsPng(bodyImgPath);
    using (var footer = full.Clone(c => c.Crop(new Rectangle(0, FooterStartPx, full.Width, full.Height - FooterStartPx))))
        footer.SaveAsPng(footerImgPath);
}
Console.WriteLine($"Acento tomado de la banda: #{accentHex}");

// Campos de la franja de encabezado (van en el Header de Word — coordenadas de página, sin
// cambios respecto de dónde estaban en la imagen completa).
var headerFields = new (string Text, long X, long Y, long Cx, long Cy, string Color, int Pt)[]
{
    ("N de referencia: {NROREF}",              721040,    41137,  3900000, 300000, "000000", 11),
    ("Fecha y Hora de Pc: {FECHA_HORA}",       4802086,   41137,  2701082, 207645, "FFFFFF", 12),
    ("Dependencia: {DEPENDENCIA}",             5359940,  296832,  2200060, 417195, "FFFFFF", 12),
};

// Campos de la franja de cuerpo (quedan en la página 1, posición fija).
var bodyFields = new (string Text, long X, long Y, long Cx, long Cy, string Color, int Pt)[]
{
    ("{SO}",                                   1238392, 3463574,  1877855, 207645, "000000", 12),
    ("IMEI {IMEI}",                            1092620, 4103430,  1877855, 207645, "000000", 12),
    ("N SERIE {NRO_SERIE}",                    1092620, 4405516,  1877855, 207645, "000000", 12),
    ("MODELO {MODELO}",                        1092620, 4711639,  1877855, 207645, "000000", 12),
    ("MARCA {MARCA}",                          1092620, 5109087,  1877855, 207645, "000000", 12),
    ("{ARCHIVO_GENERADO}",                     2919039, 5629014,  4180961, 320000, "000000", 10),
    ("HASH {HASH_ZIP_COMPLETO}",               2919039, 6012388,  4480961, 420000, "000000",  9),
    ("CLAVE {CLAVE}",                          2919039, 6606133,  3785962, 207645, "000000", 12),
};

// Cajas de 2 líneas (nombre + DNI) sobre los paneles de foto.
var twoLineFields = new (string Line1, string Line2, long X, long Y, long Cx, long Cy, string Color, int Pt)[]
{
    ("Nombre y Apellido: {NOMBRE_FUNCIONARIO}", "DNI: {DNI_LEGAJO}",       5108576, 2099009, 1480149, 853281, "FFFFFF", 10),
    ("Nombre y Apellido: {NOMBRE_DENUNCIANTE}", "DNI: {DNI_DENUNCIANTE}", 5108576, 3943801, 1480149, 853281, "000000", 10),
};

// Paneles de foto (fiscal / denunciante) — mismo tamaño, distinta Y.
const long PhotoCx = 1_238_700;
const long PhotoCy = 1_332_537;
var photoFields = new (string Token, long X, long Y)[]
{
    ("{FOTO_FUNCIONARIO}", 3831776, 2027595),
    ("{FOTO_DENUNCIANTE}", 3831776, 3872387),
};

var buildingPath = outputPath + ".building";
if (File.Exists(buildingPath)) File.Delete(buildingPath);

using (var doc = WordprocessingDocument.Create(buildingPath, WordprocessingDocumentType.Document))
{
    var mainPart = doc.AddMainDocumentPart();
    mainPart.Document = new Document();
    var body = new Body();
    mainPart.Document.Append(body);

    uint drawId = 1;

    // ── Header (se repite en todas las páginas) ──────────────────────────
    var headerPart = mainPart.AddNewPart<HeaderPart>();
    var headerImgPart = headerPart.AddImagePart(ImagePartType.Png);
    using (var fs = File.OpenRead(headerImgPath)) headerImgPart.FeedData(fs);
    var headerImgRelId = headerPart.GetIdOfPart(headerImgPart);

    var header = new Header();
    header.AppendChild(new Paragraph(new Run { InnerXml = BuildBackgroundDrawingXml(headerImgRelId, 0, 0, PageCx, HeaderH, drawId++, "FondoHeader") })
    {
        ParagraphProperties = new ParagraphProperties(new SpacingBetweenLines { After = "0" }),
    });
    foreach (var f in headerFields)
        header.AppendChild(BuildTextBoxParagraph(new[] { f.Text }, f.X, f.Y, f.Cx, f.Cy, f.Color, f.Pt, drawId++));
    headerPart.Header = header;
    headerPart.Header.Save();
    var headerRelId = mainPart.GetIdOfPart(headerPart);

    // ── Footer (se repite en todas las páginas) ──────────────────────────
    var footerPart = mainPart.AddNewPart<FooterPart>();
    var footerImgPart = footerPart.AddImagePart(ImagePartType.Png);
    using (var fs = File.OpenRead(footerImgPath)) footerImgPart.FeedData(fs);
    var footerImgRelId = footerPart.GetIdOfPart(footerImgPart);

    var footer = new Footer();
    footer.AppendChild(new Paragraph(new Run { InnerXml = BuildBackgroundDrawingXml(footerImgRelId, 0, FooterY, PageCx, FooterH, drawId++, "FondoFooter") })
    {
        ParagraphProperties = new ParagraphProperties(new SpacingBetweenLines { After = "0" }),
    });
    footerPart.Footer = footer;
    footerPart.Footer.Save();
    var footerRelId = mainPart.GetIdOfPart(footerPart);

    // ── Cuerpo de la página 1 (posición fija) ────────────────────────────
    var bodyImagePart = mainPart.AddImagePart(ImagePartType.Png);
    using (var fs = File.OpenRead(bodyImgPath)) bodyImagePart.FeedData(fs);
    var bodyImgRelId = mainPart.GetIdOfPart(bodyImagePart);

    body.AppendChild(new Paragraph(new Run { InnerXml = BuildBackgroundDrawingXml(bodyImgRelId, 0, HeaderH, PageCx, FooterY - HeaderH, drawId++, "FondoCuerpo") })
    {
        ParagraphProperties = new ParagraphProperties(new SpacingBetweenLines { After = "0" }),
    });

    foreach (var f in bodyFields)
        body.AppendChild(BuildTextBoxParagraph(new[] { f.Text }, f.X, f.Y, f.Cx, f.Cy, f.Color, f.Pt, drawId++));

    foreach (var f in twoLineFields)
        body.AppendChild(BuildTextBoxParagraph(new[] { f.Line1, f.Line2 }, f.X, f.Y, f.Cx, f.Cy, f.Color, f.Pt, drawId++));

    foreach (var f in photoFields)
        body.AppendChild(BuildTextBoxParagraph(new[] { f.Token }, f.X, f.Y, PhotoCx, PhotoCy, "000000", 12, drawId++));

    // Salto de página: el resto (Observaciones/Contenido/Capturas) fluye libre, con el mismo
    // header/footer repitiéndose — puede ocupar cuantas páginas hagan falta.
    body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));

    // Estilo "de marca" (mismo acento tomado de la banda) para que la parte que fluye no se
    // sienta desconectada del diseño de la portada.
    body.AppendChild(BuildStyledHeading("OBSERVACIONES", accentHex));
    body.AppendChild(BuildPlainParagraph("{OBSERVACIONES}"));
    body.AppendChild(BuildStyledHeading("CONTENIDO", accentHex));
    body.AppendChild(BuildPlainParagraph("{ARCHIVOS}"));
    body.AppendChild(BuildStyledHeading("CAPTURAS", accentHex));
    body.AppendChild(BuildPlainParagraph("{CAPTURAS}"));

    body.AppendChild(new SectionProperties(
        new HeaderReference { Type = HeaderFooterValues.Default, Id = headerRelId },
        new FooterReference { Type = HeaderFooterValues.Default, Id = footerRelId },
        new PageSize { Width = (uint)PageTwipsW, Height = (uint)PageTwipsH },
        new PageMargin
        {
            Top = HeaderTwips, Bottom = FooterTwips, Left = 0, Right = 0,
            Header = 0, Footer = 0, Gutter = 0,
        }));

    mainPart.Document.Save();
}

File.Copy(buildingPath, outputPath, overwrite: true);
File.Delete(buildingPath);
Console.WriteLine($"OK -> {outputPath}");

// ── Helpers ──────────────────────────────────────────────────────────────

static string XmlEscape(string s) =>
    s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

static Paragraph BuildStyledHeading(string text, string colorHex) => new(
    new ParagraphProperties(
        new SpacingBetweenLines { Before = "280", After = "120" },
        new ParagraphBorders(new BottomBorder { Val = BorderValues.Single, Size = 8, Color = colorHex, Space = 4 })),
    new Run(
        new RunProperties(
            new Bold(),
            new DocumentFormat.OpenXml.Wordprocessing.Color { Val = colorHex },
            new FontSize { Val = "26" },
            new RunFonts { Ascii = "Humanst521 Lt BT", HighAnsi = "Humanst521 Lt BT" }),
        new Text(text)));

static Paragraph BuildPlainParagraph(string text) => new(
    new ParagraphProperties(new SpacingBetweenLines { After = "200" }),
    new Run(
        new RunProperties(
            new FontSize { Val = "22" },
            new RunFonts { Ascii = "Humanst521 Lt BT", HighAnsi = "Humanst521 Lt BT" }),
        new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

static string BuildBackgroundDrawingXml(string relId, long x, long y, long cx, long cy, uint id, string name) => $"""
    <w:drawing xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
               xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
               xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
               xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"
               xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
      <wp:anchor distT="0" distB="0" distL="0" distR="0" simplePos="0" relativeHeight="{id}"
                 behindDoc="1" locked="1" layoutInCell="1" allowOverlap="1">
        <wp:simplePos x="0" y="0"/>
        <wp:positionH relativeFrom="page"><wp:posOffset>{x}</wp:posOffset></wp:positionH>
        <wp:positionV relativeFrom="page"><wp:posOffset>{y}</wp:posOffset></wp:positionV>
        <wp:extent cx="{cx}" cy="{cy}"/>
        <wp:effectExtent l="0" t="0" r="0" b="0"/>
        <wp:wrapNone/>
        <wp:docPr id="{id}" name="{name}"/>
        <wp:cNvGraphicFramePr/>
        <a:graphic>
          <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
            <pic:pic>
              <pic:nvPicPr>
                <pic:cNvPr id="{id}" name="{name}"/>
                <pic:cNvPicPr/>
              </pic:nvPicPr>
              <pic:blipFill>
                <a:blip r:embed="{relId}"/>
                <a:stretch><a:fillRect/></a:stretch>
              </pic:blipFill>
              <pic:spPr>
                <a:xfrm><a:off x="0" y="0"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm>
                <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
              </pic:spPr>
            </pic:pic>
          </a:graphicData>
        </a:graphic>
      </wp:anchor>
    </w:drawing>
    """;

static Paragraph BuildTextBoxParagraph(string[] lines, long x, long y, long cx, long cy,
    string colorHex, int pt, uint id)
{
    var halfPoints = pt * 2;
    var linesXml = string.Join("", lines.Select(l => $"""
        <w:p><w:pPr><w:spacing w:after="0"/></w:pPr>
          <w:r><w:rPr><w:rFonts w:ascii="Humanst521 Lt BT" w:hAnsi="Humanst521 Lt BT"/>
            <w:color w:val="{colorHex}"/><w:sz w:val="{halfPoints}"/></w:rPr>
            <w:t xml:space="preserve">{XmlEscape(l)}</w:t></w:r>
        </w:p>
        """));

    var xml = $"""
        <w:drawing xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                   xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                   xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                   xmlns:wps="http://schemas.microsoft.com/office/word/2010/wordprocessingShape">
          <wp:anchor distT="0" distB="0" distL="0" distR="0" simplePos="0" relativeHeight="{id}"
                     behindDoc="0" locked="0" layoutInCell="1" allowOverlap="1">
            <wp:simplePos x="0" y="0"/>
            <wp:positionH relativeFrom="page"><wp:posOffset>{x}</wp:posOffset></wp:positionH>
            <wp:positionV relativeFrom="page"><wp:posOffset>{y}</wp:posOffset></wp:positionV>
            <wp:extent cx="{cx}" cy="{cy}"/>
            <wp:effectExtent l="0" t="0" r="0" b="0"/>
            <wp:wrapNone/>
            <wp:docPr id="{id}" name="Campo {id}"/>
            <wp:cNvGraphicFramePr/>
            <a:graphic>
              <a:graphicData uri="http://schemas.microsoft.com/office/word/2010/wordprocessingShape">
                <wps:wsp>
                  <wps:cNvSpPr txBox="1"/>
                  <wps:spPr>
                    <a:xfrm><a:off x="0" y="0"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm>
                    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                    <a:noFill/>
                    <a:ln><a:noFill/></a:ln>
                  </wps:spPr>
                  <wps:txbx>
                    <w:txbxContent>{linesXml}</w:txbxContent>
                  </wps:txbx>
                  <wps:bodyPr wrap="square" lIns="0" tIns="0" rIns="0" bIns="0" anchor="t"/>
                </wps:wsp>
              </a:graphicData>
            </a:graphic>
          </wp:anchor>
        </w:drawing>
        """;

    return new Paragraph(new Run { InnerXml = xml });
}
