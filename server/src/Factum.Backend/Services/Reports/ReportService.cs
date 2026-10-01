using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Branding;
using DWP = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using DRAW = DocumentFormat.OpenXml.Drawing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Factum.Backend.Services.Reports;

public sealed record ReportResult(
    string ZipPath, string ZipFilename, string ZipHash,
    string Password, string PdfPath, string PdfFilename);

public interface IReportService
{
    Task<ReportResult> GenerateAsync(Case cas, List<FileInfoDto> files, string caseDir,
        CancellationToken ct = default);
}

public sealed class ReportService(IBrandingService branding, ILogger<ReportService> logger)
    : IReportService
{
    private static readonly string TemplatesDir =
        Path.Combine(AppContext.BaseDirectory, "Templates");

    // Nombres que este mismo servicio genera (ver docxPath/pdfPath/zipFilename abajo) — nunca
    // son evidencia real, aunque un intento de generación anterior los haya dejado tirados en
    // la carpeta del caso. Se usa tanto para filtrar la lista de evidencia (CaseService) como
    // para la limpieza de restos acá abajo.
    public static bool IsGeneratedArtifact(string fileName) =>
        (fileName.StartsWith("evidencia_") && fileName.EndsWith(".zip"))
        || (fileName.StartsWith("informe_forense_") && (fileName.EndsWith(".docx") || fileName.EndsWith(".pdf")));

    public async Task<ReportResult> GenerateAsync(Case cas, List<FileInfoDto> files,
        string caseDir, CancellationToken ct = default)
    {
        var timestamp = DateTime.UtcNow;
        var safeName = Sanitize($"{cas.NroReferencia}_{cas.NombreDenunciante}");
        var docxPath = Path.Combine(caseDir, $"informe_forense_{safeName}.docx");
        var pdfPath = Path.Combine(caseDir, $"informe_forense_{safeName}.pdf");
        var zipFilename = $"evidencia_{safeName}.zip";
        var zipPath = Path.Combine(caseDir, zipFilename);

        // Limpieza de restos de un intento anterior fallido — si no se borran acá, un ZIP/DOCX
        // que un intento previo llegó a crear antes de explotar queda ocupando este mismo path,
        // y el paso de ZIP de más abajo puede terminar abriéndolo para escritura mientras lo lee
        // como si fuera evidencia (el error "being used by another process" reportado por el
        // usuario salía de acá).
        foreach (var stale in new[] { docxPath, pdfPath, zipPath })
            if (File.Exists(stale)) File.Delete(stale);

        var password = GeneratePassword();
        var hashes = await BuildHashesAsync(files, caseDir);

        // Fase 1: ZIP solo con evidencias → permite hashear antes de generar el informe.
        // El hash se embebe en el DOCX/PDF para que coincida con lo que muestra el frontend.
        var evidencePaths = files.Select(f => Path.Combine(caseDir, f.Name)).Where(File.Exists).ToList();
        await CreateZipAsync(zipPath, evidencePaths, ct);
        var zipHash = await Sha256Async(zipPath);
        hashes["__zip__"] = zipHash;

        // Fase 2: generar informe con el hash ya conocido. Se entrega el DOCX directamente
        // (no se convierte a PDF) — decisión explícita: la fidelidad de LibreOffice
        // convirtiendo esta plantilla nunca terminó de ser confiable, así que se prefiere el
        // documento Word tal cual queda armado.
        await GenerateDocxAsync(cas, files, hashes, docxPath, zipFilename, timestamp, password,
            branding.Current, ct);

        // Fase 3: agregar DOCX al ZIP de evidencias
        await AppendToZipAsync(zipPath, [docxPath], ct);

        foreach (var f in files)
        {
            var fp = Path.Combine(caseDir, f.Name);
            if (File.Exists(fp)) File.Delete(fp);
        }

        return new ReportResult(zipPath, zipFilename, zipHash, password,
            docxPath, Path.GetFileName(docxPath));
    }

    // ── DOCX ─────────────────────────────────────────────────────────────────

    private Task GenerateDocxAsync(Case cas, List<FileInfoDto> files,
        Dictionary<string, string> hashes, string outputPath, string zipFilename,
        DateTime timestamp, string password, BrandingSnapshot brand, CancellationToken ct)
    {
        var caseDir = Path.GetDirectoryName(outputPath)!;
        return Task.Run(() =>
        {
            var templatePath = Path.Combine(TemplatesDir, "plantilla_informe_v3.docx");
            if (!File.Exists(templatePath))
                throw new FileNotFoundException("Plantilla DOCX no encontrada", templatePath);

            File.Copy(templatePath, outputPath, overwrite: true);

            using var doc = WordprocessingDocument.Open(outputPath, isEditable: true);
            var mainPart = doc.MainDocumentPart!;
            var body = mainPart.Document.Body!;

            // Contador único de docPr id para todos los dibujos que agrega este servicio (logo
            // de la organización, fotos, capturas, sello del pie). Arranca en un offset alto
            // para no pisar los ids que ya usan los dibujos de la plantilla — deben ser únicos
            // en todo el documento (header+footer+body juntos). Con IDs repetidos, algunas
            // conversiones (LibreOffice incluido) descartan uno de los dibujos en conflicto.
            uint drawId = 100_000;

            // Partes donde pueden vivir placeholders: el cuerpo y TODOS los headers/footers.
            var parts = new List<(OpenXmlPart Part, OpenXmlElement Root)> { (mainPart, body) };
            parts.AddRange(mainPart.HeaderParts.Select(h => ((OpenXmlPart)h, (OpenXmlElement)h.Header)));
            parts.AddRange(mainPart.FooterParts.Select(f => ((OpenXmlPart)f, (OpenXmlElement)f.Footer)));

            // Pass 0: la plantilla (exportada de Google Docs) NO tiene un salto de página real
            // entre el panel de fotos (fijo, posicionado con floats "relativeFrom=page") y la
            // tabla OBSERVACIONES/CONTENIDO — separa ambos únicamente con párrafos vacíos de
            // relleno que empujan el resto del contenido a la "página 2" visual. Si la tabla
            // crece (más archivos → más filas de hash), come ese espacio de relleno y empuja el
            // párrafo que aloja el panel de fotos a la página 2 real — ahí es donde "se
            // desordena todo el pdf" al agregar fotos del denunciante/fiscal. Se corrige
            // moviendo el panel ANTES de la tabla y agregando un salto de página real entre
            // ambos, así el panel siempre queda en la página 1 sin importar cuánto crezca la
            // tabla de contenido.
            // NormalizePageFlow(body, files);

            // Pass 1a: {LOGO_ORGANIZACION[:WxH]} → logo de la organización (o párrafo vacío).
            foreach (var (part, root) in parts)
                ReplaceOrganizationLogo(part, root, brand.Logo, ref drawId);

            // Pass 1b: reemplaza texto (excluye placeholders de imagen y {ARCHIVOS}) en el
            // cuerpo y en los headers/footers que la plantilla tenga. {CONTACTO} va primero,
            // en un paso propio, porque necesita saltos de línea (<w:br/>) dentro del run y
            // ReplaceTextInBody junta todo en un solo <w:t>.
            var replacements = BuildTextReplacements(cas, files, hashes, timestamp, password, zipFilename, brand);
            foreach (var (_, root) in parts)
            {
                ReplaceMultilinePlaceholder(root, "{CONTACTO}", brand.ContactLines, replacements);
                ReplaceTextInBody(root, replacements);
            }

            // Pass 2: {ARCHIVOS} → lista de párrafos Archivo + Hash
            ReplaceArchivosWithList(body, files, hashes);

            // Pass 3: placeholders de imagen → imágenes embebidas
            EmbedImages(mainPart, body, files, caseDir, ref drawId);

            // Pass 4: atribución obligatoria "Realizado con Factum" en el pie de todas las
            // páginas — se inyecta por código para que ninguna plantilla la pueda sacar.
            AddFactumAttributionFooter(mainPart, body, ref drawId);

            doc.Save();
        }, ct);
    }

    // Debajo del panel fijo de fotos (página 1) queda un hueco libre en blanco antes de la
    // franja decorativa del pie ("informatica_cif@..." + flecha) — ese hueco NO es margen de
    // página real, es otro elemento flotante anclado a una altura fija, así que si la tabla
    // crece más de la cuenta lo atraviesa y se desordena todo de nuevo. Medido de forma empírica
    // (renderizando la plantilla): el flujo normal llega naturalmente a ~2.0in desde el borde
    // superior; el hueco seguro para contenido va de ~7.2in a ~10.4in.
    private const int TableFitsSpacerTwips = 7500; // empuja el inicio de la tabla a ~7.2in
    private const int TableFitsAvailableTwips = 4600; // ~3.2in de contenido antes de la franja
    private const int TableHeaderRowTwips = 300;
    private const int TableRowNoOriginTwips = 820;
    private const int TableRowWithOriginTwips = 970;

    // Mueve el párrafo que aloja el panel fijo de fotos (fiscal/denunciante) para que quede
    // ANTES de la tabla OBSERVACIONES/CONTENIDO, con un salto de página real entre ambos, y
    // elimina los párrafos vacíos de relleno que la plantilla usaba en su lugar. Si la tabla es
    // chica (pocos archivos), en vez del salto de página se la deja en el hueco libre de la
    // página 1 — ver comentario arriba. Ver comentario en GenerateDocxAsync sobre por qué el
    // resto de esto es necesario.
    private static void NormalizePageFlow(Body body, List<FileInfoDto> files)
    {
        var table = body.Elements<Table>().FirstOrDefault(t =>
            t.Descendants<Text>().Any(t2 => t2.Text.Contains("{ARCHIVOS}") || t2.Text.Contains("{OBSERVACIONES}")));
        if (table is null) return;

        // La tabla viene marcada como "tabla flotante" (<w:tblpPr>, posicionada a un offset fijo
        // respecto de un párrafo ancla, en vez de fluir en el orden normal del documento). Por
        // eso el contenido que va DESPUÉS de la tabla en el XML (las capturas) podía terminar
        // renderizándose ANTES que la tabla en el PDF — no respetan el mismo flujo. Se la
        // convierte en una tabla normal (in-flow) para que todo se ordene según el XML.
        table.GetFirstChild<TableProperties>()?.GetFirstChild<TablePositionProperties>()?.Remove();

        // Tabla más compacta: la plantilla trae 100 dxa (5pt) de margen arriba/abajo en cada
        // celda — con varios archivos listados eso suma bastante alto muerto. Se lo reduce.
        var cellMar = table.GetFirstChild<TableProperties>()?.GetFirstChild<TableCellMarginDefault>();
        if (cellMar != null)
        {
            cellMar.TopMargin = new TopMargin { Width = "40", Type = TableWidthUnitValues.Dxa };
            cellMar.BottomMargin = new BottomMargin { Width = "40", Type = TableWidthUnitValues.Dxa };
        }

        var photoPanel = body.Elements<Paragraph>().FirstOrDefault(p =>
            p.Descendants<Text>().Any(t => t.Text.Contains("{FOTO_FUNCIONARIO}")));
        if (photoPanel is null) return;

        // Ya está en el orden correcto (plantilla futura corregida a mano) → no hacer nada.
        var children = body.ChildElements.ToList();
        if (children.IndexOf(photoPanel) < children.IndexOf(table)) return;

        var capturasPara = body.Elements<Paragraph>().FirstOrDefault(p =>
            p.Descendants<Text>().Any(t => t.Text.Contains("{CAPTURAS}")));

        // Párrafos vacíos entre la tabla y {CAPTURAS}: eran relleno para simular el salto de
        // página; con un salto real de por medio ya no cumplen ningún propósito y solo
        // agregarían espacio en blanco de sobra.
        var fillers = new List<Paragraph>();
        for (var el = table.NextSibling(); el != null && el != capturasPara; el = el.NextSibling())
        {
            if (el is Paragraph p && string.IsNullOrWhiteSpace(p.InnerText) && !p.Descendants<Drawing>().Any())
                fillers.Add(p);
        }
        foreach (var filler in fillers) filler.Remove();

        photoPanel.Remove();
        body.InsertBefore(photoPanel, table);

        var estimatedTableTwips = TableHeaderRowTwips + files.Sum(f =>
            string.IsNullOrWhiteSpace(f.SourcePath) ? TableRowNoOriginTwips : TableRowWithOriginTwips);
        var tableFitsOnPage1 = estimatedTableTwips <= TableFitsAvailableTwips;

        var finalSectPr = body.Elements<SectionProperties>().FirstOrDefault();
        Paragraph BuildPageBreak() => finalSectPr != null
            ? new Paragraph(new ParagraphProperties((SectionProperties)finalSectPr.CloneNode(true)))
            : new Paragraph(new Run(new Break { Type = BreakValues.Page }));

        if (tableFitsOnPage1)
        {
            // Entra pocos archivos → en vez de saltar de página, se empuja la tabla al hueco
            // libre que queda debajo del panel de fotos en la página 1 (ver constantes arriba).
            var spacer = new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { Before = TableFitsSpacerTwips.ToString() }));
            body.InsertBefore(spacer, table);
        }
        else
        {
            // Salto de página como CORTE DE SECCIÓN (no un simple <w:br type="page"/>): con el
            // grupo flotante gigante que trae la plantilla (íconos + paneles de foto + textos
            // "de compatibilidad" duplicados, todo anclado con relativeFrom="page"), un salto de
            // línea simple deja a LibreOffice ambigüedad sobre a qué página pertenece cada float
            // y termina intercalando contenido (una captura entre medio de la tabla). Un corte
            // de sección real fuerza un límite de página inequívoco.
            body.InsertBefore(BuildPageBreak(), table);
        }

        // El título "CAPTURAS" venía dentro del mismo panel fijo que las fotos (por eso se movió
        // junto con él a la página 1), pero conceptualmente pertenece a la sección de capturas
        // — debe quedar pegado a las imágenes, después de la tabla, no aislado antes. Se lo
        // saca del panel (queda vacío, sin texto suelto en la página 1) y se agrega de nuevo,
        // con el mismo estilo, justo antes de donde se insertan las capturas.
        foreach (var heading in photoPanel.Descendants<Paragraph>()
            .Where(p => p.InnerText.Trim() == "CAPTURAS").ToList())
        {
            foreach (var run in heading.Elements<Run>().ToList()) run.Remove();
        }

        if (capturasPara != null)
        {
            // Si la tabla se quedó en el hueco de la página 1, las capturas necesitan su propio
            // salto de página — si no, arrancarían pegadas al panel fijo, encima de todo.
            // Si la tabla se fue a la página 2 (rama de arriba), no hace falta: ya vimos que
            // tabla + título "CAPTURAS" comparten página sin problema cuando entran.
            if (tableFitsOnPage1)
                capturasPara.Parent!.InsertBefore(BuildPageBreak(), capturasPara);

            var newHeading = new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "200" }),
                new Run(
                    new RunProperties(
                        new RunFonts { Ascii = "Verdana", HighAnsi = "Verdana" },
                        new Bold(),
                        new FontSize { Val = "24" }),
                    new Text("CAPTURAS")));
            capturasPara.Parent!.InsertBefore(newHeading, capturasPara);
        }
    }

    private static Dictionary<string, string> BuildTextReplacements(Case cas,
        List<FileInfoDto> files, Dictionary<string, string> hashes,
        DateTime timestamp, string password, string zipFilename, BrandingSnapshot brand)
    {
        var fileList = string.Join("\n", files.Select(f => $"• {f.Name}  ({f.Size / 1024} KB)"));
        var fecha = timestamp.ToString("dd/MM/yyyy");
        var hora = timestamp.ToString("HH:mm:ss") + " UTC";

        // NO incluir {FOTO_FUNCIONARIO}, {FOTO_DENUNCIANTE} ni {CAPTURAS}:
        // esos placeholders los busca EmbedImages en el documento DESPUÉS de esta pasada.
        // Si los incluimos aquí con valor vacío, EmbedImages no los encontrará.
        return new Dictionary<string, string>
        {
            ["{NROREF}"] = cas.NroReferencia,
            ["{NOMBRE_DENUNCIANTE}"] = cas.NombreDenunciante,
            ["{DNI_DENUNCIANTE}"] = cas.DniDenunciante,
            ["{FECHA_HORA}"] = $"{fecha} {hora}",
            ["{fHora}"] = hora,
            ["{NOMBRE_FUNCIONARIO}"] = cas.Officer.Name,
            ["{DEPENDENCIA}"] = cas.Officer.Sigla,
            ["{DNI_LEGAJO}"] = cas.Officer.Dni,
            ["{MARCA}"] = cas.Device.Manufacturer,
            ["{MODELO}"] = cas.Device.Model,
            ["{IMEI}"] = cas.Device.Imei,
            ["{SO}"] = cas.Device.OsLabel,
            ["{NRO_SERIE}"] = cas.Device.Serial,
            ["{OBSERVACIONES}"] = cas.Observaciones,
            ["{HASH_ZIP_COMPLETO}"] = hashes.GetValueOrDefault("__zip__", string.Empty),
            ["{HASH}"] = hashes.GetValueOrDefault("__zip__", string.Empty),
            // {ARCHIVOS} se maneja en ReplaceArchivosWithTable (tabla con hash), no aquí.
            ["{ARCHIVO_GENERADO}"] = zipFilename,
            ["{CLAVE}"] = password,
            // Identidad de la organización emisora (Branding). Vacío si no está configurada.
            ["{ORGANIZACION}"] = brand.OrganizationName ?? string.Empty,
            ["{CONTACTO_EN_LINEA}"] = string.Join(" · ", brand.ContactLines),
            // {CONTACTO} (multilínea) lo resuelve ReplaceMultilinePlaceholder antes, no acá.
        };
    }

    // ── Lista de archivos ─────────────────────────────────────────────────────

    // Reemplaza {ARCHIVOS} con dos párrafos por archivo:
    //   • nombre.ext  (xx KB)       ← negrita, sangría
    //   SHA-256: aabbcc…             ← monospace, más sangría, texto libre que wrappea
    // Este enfoque no usa tablas → no hay problemas de ancho en Word ni LibreOffice.
    private static void ReplaceArchivosWithList(Body body,
        List<FileInfoDto> files, Dictionary<string, string> hashes)
    {
        var target = FindAllParagraphsWithPlaceholder(body, "{ARCHIVOS}").FirstOrDefault();
        if (target is null) return;

        var parent = target.Parent!;

        foreach (var file in files)
        {
            var hash = hashes.GetValueOrDefault(file.Name, "-");
            var size = file.Size >= 1024 ? $"{file.Size / 1024} KB" : $"{file.Size} B";

            var hasOrigin = !string.IsNullOrWhiteSpace(file.SourcePath);

            var namePara = new Paragraph();
            namePara.AppendChild(new ParagraphProperties(
                new SpacingBetweenLines { Before = "40", After = "0", Line = "220", LineRule = LineSpacingRuleValues.Auto },
                new Indentation { Left = "180" }));
            namePara.AppendChild(new Run(
                new RunProperties(
                    new Bold(),
                    new Color { Val = "1F3864" },
                    new FontSize { Val = "18" }),
                new Text($"• {file.Name}  ({size})")
                { Space = SpaceProcessingModeValues.Preserve }));
            parent.InsertBefore(namePara, target);

            var hashPara = new Paragraph();
            hashPara.AppendChild(new ParagraphProperties(
                new SpacingBetweenLines { Before = "0", After = hasOrigin ? "0" : "40", Line = "200", LineRule = LineSpacingRuleValues.Auto },
                new Indentation { Left = "360" }));
            hashPara.AppendChild(new Run(
                new RunProperties(
                    new RunFonts { Ascii = "Courier New", HighAnsi = "Courier New" },
                    new Color { Val = "595959" },
                    new FontSize { Val = "14" }),
                new Text($"SHA-256: {hash}")
                { Space = SpaceProcessingModeValues.Preserve }));
            parent.InsertBefore(hashPara, target);

            if (hasOrigin)
            {
                var originPara = new Paragraph();
                originPara.AppendChild(new ParagraphProperties(
                    new SpacingBetweenLines { Before = "0", After = "40", Line = "200", LineRule = LineSpacingRuleValues.Auto },
                    new Indentation { Left = "360" }));
                originPara.AppendChild(new Run(
                    new RunProperties(
                        new Italic(),
                        new Color { Val = "595959" },
                        new FontSize { Val = "14" }),
                    new Text($"Origen: {file.SourcePath}")
                    { Space = SpaceProcessingModeValues.Preserve }));
                parent.InsertBefore(originPara, target);
            }
        }

        target.Remove();
    }

    // ── Reemplazo de texto ────────────────────────────────────────────────────

    // Reemplazo a nivel de párrafo para manejar placeholders partidos en múltiples runs.
    // Recibe un OpenXmlElement genérico (no solo Body) para poder procesar también el
    // contenido de Header/Footer, si la plantilla los tiene (la v3 no trae ninguno; una
    // plantilla puede poner ahí {NROREF}, {FECHA_HORA}, {DEPENDENCIA}, {ORGANIZACION}…).
    private static void ReplaceTextInBody(OpenXmlElement body, Dictionary<string, string> replacements)
    {
        foreach (var para in body.Descendants<Paragraph>())
        {
            var runs = para.Elements<Run>().ToList();
            if (runs.Count == 0) continue;

            var fullText = string.Concat(
                runs.SelectMany(r => r.Elements<Text>()).Select(t => t.Text));

            if (!replacements.Keys.Any(fullText.Contains)) continue;

            var newText = fullText;
            foreach (var (ph, val) in replacements)
                newText = newText.Replace(ph, val);

            var firstRun = runs[0];
            var firstText = firstRun.GetFirstChild<Text>() ?? firstRun.AppendChild(new Text());
            firstText.Text = newText;
            firstText.Space = SpaceProcessingModeValues.Preserve;

            foreach (var t in firstRun.Elements<Text>().Skip(1).ToList()) t.Remove();
            foreach (var r in runs.Skip(1).ToList()) r.Remove();
        }
    }

    // ── Embedding de imágenes ─────────────────────────────────────────────────

    // Busca párrafos con los placeholders de imagen (que no se tocaron en ReplaceTextInBody)
    // y los reemplaza con las imágenes reales.
    // drawId: contador compartido de docPr id (ver GenerateDocxAsync) — tiene que ser único
    // en todo el documento, no solo dentro de este método.
    private static void EmbedImages(MainDocumentPart mainPart, Body body,
        List<FileInfoDto> files, string caseDir, ref uint drawId)
    {
        var fotoFunc = files.FirstOrDefault(f => f.Name.Contains("foto_funcionario"));
        var fotoDen = files.FirstOrDefault(f => f.Name.Contains("foto_denunciante"));
        var shots = files
            .Where(f => f.Name.Contains("screenshot") || f.Name.Contains("captura"))
            .Select(f => Path.Combine(caseDir, f.Name))
            .Where(IsUsableImage)
            .ToList();

        // Tamaño de los paneles de foto de la plantilla v3.
        const long PhotoMaxW = 1_238_700;
        const long PhotoMaxH = 1_332_537;

        // Si el fiscal/denunciante no tiene foto cargada, no dejar el placeholder crudo
        // "{FOTO_FUNCIONARIO}"/"{FOTO_DENUNCIANTE}" visible en el informe final — se usa un
        // ícono genérico de "sin foto" en su lugar.
        var placeholderPhoto = Path.Combine(TemplatesDir, "sin-foto-placeholder.png");

        // cover=true: la foto llena todo el panel recortando el sobrante (estilo foto carnet).
        // Para las capturas de pantalla NO se recorta — perdería contenido de la evidencia.
        // Si el archivo capturado quedó vacío/corrupto (ej. una captura que falló en el
        // dispositivo), se cae al ícono genérico en vez de intentar embeber un archivo inválido
        // y tumbar la generación entera — ver IsUsableImage.
        var fotoFuncCandidate = fotoFunc != null ? Path.Combine(caseDir, fotoFunc.Name) : null;
        var fotoFuncPath = fotoFuncCandidate != null && IsUsableImage(fotoFuncCandidate) ? fotoFuncCandidate : placeholderPhoto;
        ReplacePlaceholderWithImage(mainPart, body, "{FOTO_FUNCIONARIO}",
            fotoFuncPath, PhotoMaxW, PhotoMaxH, ref drawId, cover: true);

        var fotoDenCandidate = fotoDen != null ? Path.Combine(caseDir, fotoDen.Name) : null;
        var fotoDenPath = fotoDenCandidate != null && IsUsableImage(fotoDenCandidate) ? fotoDenCandidate : placeholderPhoto;
        ReplacePlaceholderWithImage(mainPart, body, "{FOTO_DENUNCIANTE}",
            fotoDenPath, PhotoMaxW, PhotoMaxH, ref drawId, cover: true);

        if (shots.Count > 0)
            ReplacePlaceholderWithScreenshots(mainPart, body, "{CAPTURAS}", shots,
                5_040_000, 4_320_000, ref drawId);
    }

    // Encuentra TODAS las apariciones del párrafo con el placeholder y las reemplaza con una
    // imagen. Los paneles de foto de esta plantilla (exportada de Google Docs/Canva) guardan
    // cada shape DOS veces dentro de un mismo <mc:AlternateContent>: una copia moderna en
    // <mc:Choice Requires="wps|wpg"> y una copia VML de compatibilidad en <mc:Fallback> — ambas
    // contienen el mismo texto "{FOTO_FUNCIONARIO}"/"{FOTO_DENUNCIANTE}". Si solo se reemplaza
    // la primera aparición, el documento queda con el Choice y el Fallback desincronizados
    // (uno con la imagen, el otro con el placeholder crudo), y LibreOffice descarta en silencio
    // todo el grupo (y a veces la página 1 entera) al convertir a PDF — así se ve el "todo se
    // desordena" al poner fotos. Por eso hay que reemplazar TODAS las copias, no solo la primera.
    // Un archivo de 0 bytes (captura que falló en el dispositivo) pasa File.Exists pero hace
    // explotar GetImageSize al intentar leer el header — se filtra acá, en el único lugar que
    // decide si una imagen es embebible, para no duplicar el chequeo en cada llamador.
    private static bool IsUsableImage(string path) =>
        File.Exists(path) && new FileInfo(path).Length > 0;

    // owner: la parte dueña del contenido (MainDocumentPart, HeaderPart o FooterPart) — la
    // imagen se agrega como ImagePart de ESA parte, porque la relación r:embed se resuelve
    // contra las relaciones de la parte que contiene el dibujo.
    private static void ReplacePlaceholderWithImage(OpenXmlPart owner, OpenXmlElement root,
        string placeholder, string imagePath, long maxW, long maxH, ref uint drawId, bool cover = false)
    {
        if (!IsUsableImage(imagePath)) return;
        var targets = FindAllParagraphsWithPlaceholder(root, placeholder);
        var source = ImageSource.FromFile(imagePath);

        foreach (var target in targets)
        {
            var imgPara = BuildImageParagraph(owner, source, maxW, maxH, ref drawId, cover);
            target.Parent!.InsertBefore(imgPara, target);
            target.Remove();
        }
    }

    // Encuentra TODAS las apariciones de {CAPTURAS} y las reemplaza con todos los screenshots
    // (ver comentario de ReplacePlaceholderWithImage sobre por qué hay que cubrir cada aparición).
    private static void ReplacePlaceholderWithScreenshots(MainDocumentPart mainPart, Body body,
        string placeholder, List<string> imagePaths, long maxW, long maxH, ref uint drawId)
    {
        var targets = FindAllParagraphsWithPlaceholder(body, placeholder);

        if (targets.Count == 0)
        {
            foreach (var path in imagePaths)
            {
                if (!File.Exists(path)) continue;
                body.AppendChild(BuildImageParagraph(mainPart, ImageSource.FromFile(path), maxW, maxH, ref drawId, cover: false));
            }
            return;
        }

        foreach (var target in targets)
        {
            foreach (var path in imagePaths)
            {
                if (!File.Exists(path)) continue;
                var imgPara = BuildImageParagraph(mainPart, ImageSource.FromFile(path), maxW, maxH, ref drawId, cover: false);
                target.Parent!.InsertBefore(imgPara, target);
            }
            target.Remove();
        }
    }

    // Reconstruye el texto completo de cada párrafo concatenando todos los runs (Word parte los
    // runs) y devuelve TODAS las apariciones del placeholder en el documento — no solo la
    // primera. Ver comentario de ReplacePlaceholderWithImage.
    private static List<Paragraph> FindAllParagraphsWithPlaceholder(OpenXmlElement root, string placeholder)
    {
        var result = new List<Paragraph>();
        foreach (var para in root.Descendants<Paragraph>())
        {
            var fullText = string.Concat(
                para.Elements<Run>().SelectMany(r => r.Elements<Text>()).Select(t => t.Text));
            if (fullText.Contains(placeholder)) result.Add(para);
        }
        return result;
    }

    // Imagen a embeber: desde un archivo (fotos, capturas) o desde bytes en memoria (logo de
    // la organización, sello de Factum). Width/Height en píxeles, solo para la proporción.
    private sealed record ImageSource(string Name, PartTypeInfo PartType, Func<Stream> Open,
        int Width, int Height)
    {
        public static ImageSource FromFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var (w, h) = GetImageSize(path);
            return new ImageSource(Path.GetFileName(path),
                ext is ".jpg" or ".jpeg" ? ImagePartType.Jpeg : ImagePartType.Png,
                () => File.OpenRead(path), w, h);
        }

        public static ImageSource FromBytes(string name, byte[] data, string contentType, int w, int h) =>
            new(name, contentType == "image/jpeg" ? ImagePartType.Jpeg : ImagePartType.Png,
                () => new MemoryStream(data, writable: false), w, h);
    }

    private static ImagePart AddImagePart(OpenXmlPart owner, PartTypeInfo type) => owner switch
    {
        MainDocumentPart m => m.AddImagePart(type),
        HeaderPart h => h.AddImagePart(type),
        FooterPart f => f.AddImagePart(type),
        _ => throw new ArgumentException($"Parte no soportada para imágenes: {owner.GetType().Name}", nameof(owner)),
    };

    // Agrega la imagen como ImagePart de la parte dueña y devuelve el r:id para el blip.
    private static string EmbedImagePart(OpenXmlPart owner, ImageSource source)
    {
        var imagePart = AddImagePart(owner, source.PartType);
        using (var fs = source.Open())
            imagePart.FeedData(fs);
        return owner.GetIdOfPart(imagePart);
    }

    // paragraphProperties: si viene, reemplaza al pPr por defecto (centrado, 6 pt arriba y
    // abajo) — lo usa {LOGO_ORGANIZACION} para conservar la alineación del párrafo original.
    private static Paragraph BuildImageParagraph(OpenXmlPart owner,
        ImageSource source, long maxW, long maxH, ref uint drawId, bool cover = false,
        ParagraphProperties? paragraphProperties = null)
    {
        var relId = EmbedImagePart(owner, source);
        var (imgW, imgH) = (source.Width, source.Height);

        long dispW, dispH;
        DRAW.SourceRectangle? srcRect = null;
        if (cover)
        {
            // Llena maxW × maxH por completo, recortando el sobrante (como una foto carnet) —
            // en vez de reescalar el bitmap, se recorta el source rect del blip (a:srcRect,
            // en milésimas de porcentaje) y se muestra a tamaño fijo maxW × maxH.
            dispW = maxW; dispH = maxH;
            double imgRatio = (double)imgW / imgH, boxRatio = (double)maxW / maxH;
            if (imgRatio > boxRatio)
            {
                var visibleFrac = boxRatio / imgRatio;
                var cropSide = (int)Math.Round((1 - visibleFrac) / 2 * 100000);
                srcRect = new DRAW.SourceRectangle { Left = cropSide, Right = cropSide };
            }
            else
            {
                var visibleFrac = imgRatio / boxRatio;
                var cropSide = (int)Math.Round((1 - visibleFrac) / 2 * 100000);
                srcRect = new DRAW.SourceRectangle { Top = cropSide, Bottom = cropSide };
            }
        }
        else
        {
            // Fit dentro de maxW × maxH manteniendo aspect ratio — evita el desborde
            double ratio = (double)imgH / imgW;
            if (ratio * maxW <= maxH) { dispW = maxW; dispH = (long)(maxW * ratio); }
            else { dispH = maxH; dispW = (long)(maxH / ratio); }
        }

        var drawing = BuildDrawing(relId, source.Name, dispW, dispH, drawId++, srcRect);

        return new Paragraph(
            paragraphProperties ?? new ParagraphProperties(
                new SpacingBetweenLines { Before = "120", After = "120" },
                new Justification { Val = JustificationValues.Center }),
            new Run(drawing));
    }

    private static PIC.BlipFill BuildBlipFill(string relId, DRAW.SourceRectangle? srcRect)
    {
        var blipFill = new PIC.BlipFill();
        blipFill.Append(new DRAW.Blip { Embed = relId });
        if (srcRect != null) blipFill.Append(srcRect);
        blipFill.Append(new DRAW.Stretch(new DRAW.FillRectangle()));
        return blipFill;
    }

    private static Drawing BuildDrawing(string relId, string name,
        long widthEmu, long heightEmu, uint id, DRAW.SourceRectangle? srcRect = null)
    {
        // Construído con AppendChild para evitar ambigüedad en object initializers anidados.
        var geom = new DRAW.PresetGeometry(new DRAW.AdjustValueList());
        geom.Preset = DRAW.ShapeTypeValues.Rectangle;

        var spPr = new PIC.ShapeProperties(
            new DRAW.Transform2D(
                new DRAW.Offset { X = 0L, Y = 0L },
                new DRAW.Extents { Cx = widthEmu, Cy = heightEmu }),
            geom);

        var pic = new PIC.Picture(
            new PIC.NonVisualPictureProperties(
                new PIC.NonVisualDrawingProperties { Id = 0U, Name = name },
                new PIC.NonVisualPictureDrawingProperties(
                    new DRAW.PictureLocks { NoChangeAspect = true })),
            BuildBlipFill(relId, srcRect),
            spPr);

        var graphicData = new DRAW.GraphicData(pic);
        graphicData.Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture";

        var inline = new DWP.Inline(
            new DWP.Extent { Cx = widthEmu, Cy = heightEmu },
            new DWP.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
            new DWP.DocProperties { Id = id, Name = name },
            new DWP.NonVisualGraphicFrameDrawingProperties(
                new DRAW.GraphicFrameLocks { NoChangeAspect = true }),
            new DRAW.Graphic(graphicData));

        inline.DistanceFromTop = 0U;
        inline.DistanceFromBottom = 0U;
        inline.DistanceFromLeft = 0U;
        inline.DistanceFromRight = 0U;

        return new Drawing(inline);
    }

    // Un archivo truncado/corrupto (no vacío, IsUsableImage ya filtró ese caso, pero sí con
    // menos bytes de los que el header declara) puede hacer que ReadExactly tire
    // EndOfStreamException a mitad de parseo — eso no debería tumbar la generación completa del
    // informe por una sola captura dañada, así que se cae al tamaño por defecto igual que con
    // una extensión no reconocida.
    private static (int w, int h) GetImageSize(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".png")
            {
                var buf = new byte[24];
                fs.ReadExactly(buf, 0, 24);
                var w = (buf[16] << 24) | (buf[17] << 16) | (buf[18] << 8) | buf[19];
                var h = (buf[20] << 24) | (buf[21] << 16) | (buf[22] << 8) | buf[23];
                return (w, h);
            }

            if (ext is ".jpg" or ".jpeg")
            {
                var buf2 = new byte[2];
                fs.ReadExactly(buf2, 0, 2); // SOI FF D8
                while (fs.Position < fs.Length - 8)
                {
                    fs.ReadExactly(buf2, 0, 2);
                    if (buf2[0] != 0xFF) break;
                    var marker = buf2[1];
                    var lenBuf = new byte[2];
                    fs.ReadExactly(lenBuf, 0, 2);
                    var segLen = (lenBuf[0] << 8) | lenBuf[1];
                    if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                    {
                        var sof = new byte[5];
                        fs.ReadExactly(sof, 0, 5);
                        var h = (sof[1] << 8) | sof[2];
                        var w = (sof[3] << 8) | sof[4];
                        return (w, h);
                    }
                    fs.Seek(segLen - 2, SeekOrigin.Current);
                }
            }
        }
        catch (Exception)
        {
            // archivo truncado/corrupto — se sigue con el tamaño por defecto de abajo.
        }

        return (800, 600);
    }

    // ── Identidad de la organización (Branding) ──────────────────────────────

    // {LOGO_ORGANIZACION} o {LOGO_ORGANIZACION:<ancho>x<alto>} (cm, punto o coma decimal).
    private static readonly Regex LogoPlaceholderRegex = new(
        @"\{LOGO_ORGANIZACION(?::(\d+(?:[.,]\d+)?)x(\d+(?:[.,]\d+)?))?\}",
        RegexOptions.CultureInvariant);

    private const double DefaultLogoBoxWidthCm = 5.0;
    private const double DefaultLogoBoxHeightCm = 1.5;
    private const long EmuPerCm = 360_000;

    // Reemplaza cada párrafo con {LOGO_ORGANIZACION[:WxH]} — en TODAS sus copias (mc:Choice y
    // mc:Fallback, ver ReplacePlaceholderWithImage) — por el logo ajustado SIN recortar dentro
    // de la caja, conservando las ParagraphProperties del párrafo original (alineación, y un
    // eventual sectPr). Sin logo, se borra solo el texto del placeholder y el párrafo queda
    // vacío para no romper el layout de la caja que lo contiene.
    private static void ReplaceOrganizationLogo(OpenXmlPart owner, OpenXmlElement root,
        BrandingLogo? logo, ref uint drawId)
    {
        foreach (var para in root.Descendants<Paragraph>().ToList())
        {
            var runs = para.Elements<Run>().ToList();
            var fullText = string.Concat(runs.SelectMany(r => r.Elements<Text>()).Select(t => t.Text));
            var match = LogoPlaceholderRegex.Match(fullText);
            if (!match.Success) continue;

            if (logo is null)
            {
                CollapseParagraphText(runs, LogoPlaceholderRegex.Replace(fullText, string.Empty));
                continue;
            }

            var boxW = ParseCm(match.Groups[1], DefaultLogoBoxWidthCm);
            var boxH = ParseCm(match.Groups[2], DefaultLogoBoxHeightCm);
            var pPr = para.ParagraphProperties?.CloneNode(true) as ParagraphProperties
                      ?? new ParagraphProperties();
            var source = ImageSource.FromBytes($"logo-organizacion{logo.Extension}", logo.Data,
                logo.ContentType, logo.Width, logo.Height);
            var imgPara = BuildImageParagraph(owner, source,
                (long)Math.Round(boxW * EmuPerCm), (long)Math.Round(boxH * EmuPerCm),
                ref drawId, cover: false, paragraphProperties: pPr);
            para.Parent!.InsertBefore(imgPara, para);
            para.Remove();
        }
    }

    private static double ParseCm(Group g, double fallback)
    {
        if (!g.Success) return fallback;
        return double.TryParse(g.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture,
                   out var v) && v > 0 && v <= 100
            ? v
            : fallback;
    }

    // Placeholder de texto multilínea ({CONTACTO}): las líneas quedan en el MISMO run (con
    // el formato del run original), separadas por <w:br/>. El resto de los placeholders de
    // texto del párrafo se resuelven acá mismo, pieza por pieza, para que un "\n" dentro de
    // otro valor (p. ej. las observaciones) no se convierta en salto de línea.
    private static void ReplaceMultilinePlaceholder(OpenXmlElement root, string placeholder,
        IReadOnlyList<string> lines, Dictionary<string, string> replacements)
    {
        foreach (var para in root.Descendants<Paragraph>().ToList())
        {
            var runs = para.Elements<Run>().ToList();
            if (runs.Count == 0) continue;

            var fullText = string.Concat(runs.SelectMany(r => r.Elements<Text>()).Select(t => t.Text));
            if (!fullText.Contains(placeholder)) continue;

            var pieces = fullText.Split(placeholder);
            var firstRun = runs[0];
            foreach (var t in firstRun.Elements<Text>().ToList()) t.Remove();

            for (var i = 0; i < pieces.Length; i++)
            {
                var piece = pieces[i];
                foreach (var (ph, val) in replacements)
                    piece = piece.Replace(ph, val);
                if (piece.Length > 0)
                    firstRun.AppendChild(new Text(piece) { Space = SpaceProcessingModeValues.Preserve });

                if (i == pieces.Length - 1) break;
                for (var l = 0; l < lines.Count; l++)
                {
                    if (l > 0) firstRun.AppendChild(new Break());
                    firstRun.AppendChild(new Text(lines[l]) { Space = SpaceProcessingModeValues.Preserve });
                }
            }

            foreach (var r in runs.Skip(1).ToList()) r.Remove();
        }
    }

    // Deja newText en el primer run del párrafo y borra el resto de los runs (misma
    // estrategia que ReplaceTextInBody, para placeholders partidos en varios runs).
    private static void CollapseParagraphText(List<Run> runs, string newText)
    {
        if (runs.Count == 0) return;
        var firstRun = runs[0];
        var firstText = firstRun.GetFirstChild<Text>() ?? firstRun.AppendChild(new Text());
        firstText.Text = newText;
        firstText.Space = SpaceProcessingModeValues.Preserve;
        foreach (var t in firstRun.Elements<Text>().Skip(1).ToList()) t.Remove();
        foreach (var r in runs.Skip(1).ToList()) r.Remove();
    }

    // ── Atribución "Realizado con Factum" (pie de todas las páginas) ─────────

    private const string AttributionText = "Realizado con Factum";
    private const long SelloSizeEmu = 144_000; // 0.4 cm

    // Agrega al pie de TODAS las páginas un párrafo centrado con el Sello de Factum (0.4 cm)
    // + "Realizado con Factum" (Arial 8 pt, gris). Se inyecta por código para que ninguna
    // plantilla (incluidas las de clientes) la pueda omitir. Por cada sección:
    //  - footer Default (y First si la sección tiene titlePg, y Even si settings tiene
    //    evenAndOddHeaders): si existe, se le agrega el párrafo al final (una sola vez por
    //    parte, aunque varias secciones la compartan);
    //  - si falta, se hereda el de la sección anterior (como hace Word) y, si tampoco hay,
    //    se apunta a UN FooterPart nuevo creado acá, insertando la FooterReference en el
    //    orden del esquema (junto a las demás header/footerReference, antes del resto).
    // Si el Sello no está o no es un PNG válido, va solo el texto. Nunca hace fallar el informe.
    private void AddFactumAttributionFooter(MainDocumentPart mainPart, Body body, ref uint drawId)
    {
        var sello = LoadSello();

        var evenAndOdd = mainPart.DocumentSettingsPart?.Settings?.GetFirstChild<EvenAndOddHeaders>()
            is { } eo && (eo.Val is null || eo.Val.Value);

        var sections = body.Descendants<SectionProperties>().ToList();
        if (sections.Count == 0)
        {
            var sectPr = new SectionProperties();
            body.AppendChild(sectPr);
            sections.Add(sectPr);
        }

        var targets = new List<FooterPart>();
        FooterPart? created = null;
        string? createdId = null;
        var inherited = new Dictionary<string, string>(); // tipo → r:id vigente

        foreach (var sectPr in sections)
        {
            var types = new List<HeaderFooterValues> { HeaderFooterValues.Default };
            if (sectPr.GetFirstChild<TitlePage>() is { } tp && (tp.Val is null || tp.Val.Value))
                types.Add(HeaderFooterValues.First);
            if (evenAndOdd)
                types.Add(HeaderFooterValues.Even);

            foreach (var type in types)
            {
                var key = type.ToString();
                var existing = sectPr.Elements<FooterReference>().FirstOrDefault(fr =>
                    (fr.Type?.Value ?? HeaderFooterValues.Default) == type && !string.IsNullOrEmpty(fr.Id?.Value));

                string id;
                if (existing is not null)
                {
                    id = existing.Id!.Value!;
                }
                else
                {
                    if (!inherited.TryGetValue(key, out id!))
                    {
                        if (created is null)
                        {
                            created = mainPart.AddNewPart<FooterPart>();
                            created.Footer = NewFooter();
                            createdId = mainPart.GetIdOfPart(created);
                        }
                        id = createdId!;
                    }
                    InsertFooterReference(sectPr, new FooterReference { Type = type, Id = id });
                }
                inherited[key] = id;

                if (TryGetFooterPart(mainPart, id) is { } fp && !targets.Contains(fp))
                    targets.Add(fp);
            }
        }

        foreach (var footerPart in targets)
        {
            footerPart.Footer ??= NewFooter();
            footerPart.Footer.AppendChild(BuildAttributionParagraph(footerPart, sello, ref drawId));
            footerPart.Footer.Save();
        }
    }

    private BrandingLogo? LoadSello()
    {
        var path = Path.Combine(TemplatesDir, "factum-sello.png");
        try
        {
            if (!File.Exists(path))
            {
                logger.LogWarning("Informe: falta {Path}; la atribución va solo con texto", path);
                return null;
            }
            var data = File.ReadAllBytes(path);
            if (!ImageProbe.TryDetect(data, out var contentType, out var w, out var h))
            {
                logger.LogWarning("Informe: {Path} no es una imagen válida; la atribución va solo con texto", path);
                return null;
            }
            return new BrandingLogo(data, contentType, contentType == "image/jpeg" ? ".jpg" : ".png", w, h, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Informe: no se pudo leer {Path}; la atribución va solo con texto", path);
            return null;
        }
    }

    private static FooterPart? TryGetFooterPart(MainDocumentPart mainPart, string id)
    {
        try { return mainPart.GetPartById(id) as FooterPart; }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static Footer NewFooter()
    {
        var footer = new Footer();
        footer.AddNamespaceDeclaration("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        footer.AddNamespaceDeclaration("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        footer.AddNamespaceDeclaration("wp", "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing");
        footer.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");
        footer.AddNamespaceDeclaration("pic", "http://schemas.openxmlformats.org/drawingml/2006/picture");
        return footer;
    }

    // Las header/footerReference van al principio del sectPr (EG_HdrFtrReferences), antes
    // de footnotePr, type, pgSz, pgMar, etc.
    private static void InsertFooterReference(SectionProperties sectPr, FooterReference reference)
    {
        var lastRef = sectPr.Elements().LastOrDefault(e => e is HeaderReference or FooterReference);
        if (lastRef is not null) lastRef.InsertAfterSelf(reference);
        else sectPr.PrependChild(reference);
    }

    private static Paragraph BuildAttributionParagraph(FooterPart owner, BrandingLogo? sello, ref uint drawId)
    {
        var para = new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { Before = "0", After = "0" },
            new Justification { Val = JustificationValues.Center }));

        if (sello is not null)
        {
            var relId = EmbedImagePart(owner, ImageSource.FromBytes("factum-sello.png", sello.Data,
                sello.ContentType, sello.Width, sello.Height));
            // position -3 (medios puntos) baja el Sello 1.5 pt para centrarlo con el texto de 8 pt.
            para.AppendChild(new Run(
                new RunProperties(new Position { Val = "-3" }),
                BuildDrawing(relId, "factum-sello.png", SelloSizeEmu, SelloSizeEmu, drawId++)));
        }

        para.AppendChild(new Run(
            new RunProperties(
                new RunFonts { Ascii = "Arial", HighAnsi = "Arial", ComplexScript = "Arial" },
                new Color { Val = "5C656E" },
                new FontSize { Val = "16" },
                new FontSizeComplexScript { Val = "16" }),
            new Text(sello is not null ? " " + AttributionText : AttributionText)
            { Space = SpaceProcessingModeValues.Preserve }));

        return para;
    }

    // ── PDF: conversión del DOCX con LibreOffice ─────────────────────────────

    private static readonly string[] LibreOfficePaths =
    [
        "libreoffice",
        "soffice",
        "/Applications/LibreOffice.app/Contents/MacOS/soffice",
        "/usr/bin/libreoffice",
        "/usr/bin/soffice",
    ];

    private static async Task ConvertDocxToPdfAsync(string docxPath, string outDir,
        CancellationToken ct)
    {
        var soffice = LibreOfficePaths.FirstOrDefault(p =>
            p.StartsWith('/') ? File.Exists(p) : IsOnPath(p));

        if (soffice is null)
            throw new InvalidOperationException(
                "LibreOffice no está instalado: brew install --cask libreoffice");

        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = soffice,
                Arguments = $"--headless --convert-to pdf --outdir \"{outDir}\" \"{docxPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"LibreOffice falló (exit {process.ExitCode}): {err}");
        }
    }

    private static bool IsOnPath(string exe)
    {
        var sep = OperatingSystem.IsWindows() ? ';' : ':';
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(sep) ?? [];
        return paths.Any(dir => File.Exists(Path.Combine(dir, exe)));
    }

    // ── ZIP ───────────────────────────────────────────────────────────────────

    private static async Task CreateZipAsync(string zipPath, IEnumerable<string> filePaths,
        CancellationToken ct)
    {
        await using var fs = File.Create(zipPath);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var filePath in filePaths.Where(File.Exists))
        {
            ct.ThrowIfCancellationRequested();
            var entry = archive.CreateEntry(Path.GetFileName(filePath), CompressionLevel.Optimal);
            entry.LastWriteTime = DateTimeOffset.UtcNow;
            await using var entryStream = entry.Open();
            await using var fileStream = File.OpenRead(filePath);
            await fileStream.CopyToAsync(entryStream, ct);
        }
    }

    private static async Task AppendToZipAsync(string zipPath, IEnumerable<string> filePaths,
        CancellationToken ct)
    {
        await using var fs = File.Open(zipPath, FileMode.Open, FileAccess.ReadWrite);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Update, leaveOpen: false);

        foreach (var filePath in filePaths.Where(File.Exists))
        {
            ct.ThrowIfCancellationRequested();
            var entry = archive.CreateEntry(Path.GetFileName(filePath), CompressionLevel.Optimal);
            entry.LastWriteTime = DateTimeOffset.UtcNow;
            await using var entryStream = entry.Open();
            await using var fileStream = File.OpenRead(filePath);
            await fileStream.CopyToAsync(entryStream, ct);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<Dictionary<string, string>> BuildHashesAsync(
        List<FileInfoDto> files, string caseDir)
    {
        var result = new Dictionary<string, string>();
        foreach (var f in files)
        {
            var path = Path.Combine(caseDir, f.Name);
            if (!File.Exists(path)) continue;
            result[f.Name] = await Sha256Async(path);
            result[f.Name + ":path"] = path;
        }
        return result;
    }

    private static async Task<string> Sha256Async(string path)
    {
        using var sha = SHA256.Create();
        await using var fs = File.OpenRead(path);
        return Convert.ToHexStringLower(await sha.ComputeHashAsync(fs));
    }

    private static string GeneratePassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(16, chars, (span, c) =>
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[16];
            rng.GetBytes(bytes);
            for (var i = 0; i < span.Length; i++)
                span[i] = c[bytes[i] % c.Length];
        });
    }

    private static string Sanitize(string s) =>
        new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray())
            .Trim('_')[..Math.Min(s.Length, 60)];
}
