using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Branding;
using DWP = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using DRAW = DocumentFormat.OpenXml.Drawing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Factum.Backend.Services.Reports;

/// <summary>
/// Resultado de la generación. <c>PdfPath</c>/<c>PdfFilename</c> conservan el nombre histórico
/// pero apuntan al DOCX. <c>ZipHash</c> es el SHA-256 del ZIP (cifrado, si corresponde) que se
/// descarga (el DOCX ya no va adentro) y <c>ReportHash</c> el del DOCX. <c>Password</c> es null
/// si el ZIP no se cifró (<c>Report:EncryptZip=false</c>).
/// </summary>
public sealed record ReportResult(
    string ZipPath, string ZipFilename, string ZipHash,
    string? Password, bool ZipEncrypted, string? ZipEncryption,
    string PdfPath, string PdfFilename, string ReportHash);

/// <summary>
/// Resultado de <see cref="IReportService.GenerateReportAsync"/> (zip-local-informe-servidor
/// §5.7): solo el DOCX. <c>PdfPath</c>/<c>PdfFilename</c> conservan el nombre histórico.
/// </summary>
public sealed record ReportOnlyResult(string PdfPath, string PdfFilename, string ReportHash);

public interface IReportService
{
    Task<ReportResult> GenerateAsync(Case cas, List<FileInfoDto> files, string caseDir,
        CancellationToken ct = default);

    /// <summary>
    /// Flujo "evidencia en la PC del perito" (zip-local-informe-servidor §5.7): arma SOLO el DOCX
    /// en <paramref name="outputDir"/>, con la tabla de hashes de <paramref name="hashes"/> (el
    /// manifiesto) y las capturas leídas de <paramref name="imagesDir"/>. No escribe ni toca ningún
    /// ZIP. Ante una excepción borra el DOCX de este intento y relanza.
    /// </summary>
    Task<ReportOnlyResult> GenerateReportAsync(Case cas, List<FileInfoDto> evidence,
        IReadOnlyDictionary<string, string> hashes, string imagesDir, string outputDir,
        string zipFilename, string zipHash, CancellationToken ct = default);
}

/// <summary>
/// Genera el Informe Pericial Técnico Informático a partir de <c>Templates/plantilla_informe_v6.docx</c>
/// (que nace de <c>ops/plantilla/build_plantilla_v6.py</c>, a partir de la v4) y el ZIP de
/// evidencia. Ver Refactorizaciones/informe-pericial-de-parte.md §7 y Anexo B,
/// Refactorizaciones/informe-diseno-modelo.md (colores de marca e isotipo) y
/// Refactorizaciones/informe-diseno-v6.md (diseño "Filete": portada, encabezado de texto, pie
/// con slot de atribución y tinte de la fila del ZIP).
/// </summary>
public sealed class ReportService : IReportService
{
    private const string DefaultTemplateFileName = "plantilla_informe_v6.docx";

    private readonly IBrandingService branding;
    private readonly IReportSettings settings;
    private readonly ILogger<ReportService> logger;
    private readonly string templateFileName;

    public ReportService(IBrandingService branding, IReportSettings settings, ILogger<ReportService> logger)
        : this(branding, settings, logger, DefaultTemplateFileName)
    {
    }

    // Solo para los tests (p. ej. generar el mismo caso con la v4 como línea de base). DI usa
    // el constructor público.
    internal ReportService(IBrandingService branding, IReportSettings settings, ILogger<ReportService> logger,
        string templateFileName)
    {
        this.branding = branding;
        this.settings = settings;
        this.logger = logger;
        this.templateFileName = templateFileName;
    }

    private static readonly string TemplatesDir =
        Path.Combine(AppContext.BaseDirectory, "Templates");

    // Nombres que este mismo servicio genera (o generaba: informe_forense_* es de la v3) —
    // nunca son evidencia real, aunque un intento de generación anterior los haya dejado en la
    // carpeta del caso. Se usa tanto para filtrar la lista de evidencia (CaseService) como para
    // la limpieza de restos acá abajo.
    public static bool IsGeneratedArtifact(string fileName) =>
        (fileName.StartsWith("evidencia_", StringComparison.Ordinal) && fileName.EndsWith(".zip", StringComparison.Ordinal))
        || ((fileName.StartsWith("informe_forense_", StringComparison.Ordinal) ||
             fileName.StartsWith("informe_pericial_", StringComparison.Ordinal))
            && (fileName.EndsWith(".docx", StringComparison.Ordinal) || fileName.EndsWith(".pdf", StringComparison.Ordinal)));

    // ── Flujo D10 (§7.1) ──────────────────────────────────────────────────────

    /// <summary>Nombre del ZIP de evidencia del caso (los dos flujos).</summary>
    internal static string ZipFilenameFor(Case cas) =>
        $"evidencia_{Sanitize($"{cas.NroReferencia}_{cas.NombreDenunciante}")}.zip";

    public async Task<ReportResult> GenerateAsync(Case cas, List<FileInfoDto> files,
        string caseDir, CancellationToken ct = default)
    {
        var safeName = Sanitize($"{cas.NroReferencia}_{cas.NombreDenunciante}");
        var docxPath = Path.Combine(caseDir, $"informe_pericial_{safeName}.docx");
        var zipFilename = ZipFilenameFor(cas);
        var zipPath = Path.Combine(caseDir, zipFilename);

        // 1. Restos de un intento anterior fallido (con los nombres de ESTE caso): si no se
        // borran, el ZIP que se está escribiendo podría leerse a sí mismo como evidencia.
        foreach (var stale in new[]
                 {
                     docxPath, Path.ChangeExtension(docxPath, ".pdf"), zipPath,
                     Path.Combine(caseDir, $"informe_forense_{safeName}.docx"),
                     Path.Combine(caseDir, $"informe_forense_{safeName}.pdf"),
                 })
            if (File.Exists(stale)) File.Delete(stale);

        // 2. Evidencia sin artefactos, en orden ordinal por nombre (tabla y anexo).
        var evidence = files
            .Where(f => !IsGeneratedArtifact(f.Name))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList();

        // 3. Contraseña (solo si se cifra) y SHA-256 de cada archivo, calculados ANTES de zipear:
        // son los de la tabla del informe y contra los que se verifica el ZIP en el paso 6.
        var password = settings.EncryptZip ? GeneratePassword() : null;
        var hashes = await BuildHashesAsync(evidence, caseDir);
        var evidencePaths = evidence.Select(f => Path.Combine(caseDir, f.Name)).Where(File.Exists).ToList();
        var expected = evidencePaths
            .Select(Path.GetFileName)
            .ToDictionary(n => n!, n => hashes[n!], StringComparer.Ordinal);

        string zipHash, reportHash;
        try
        {
            // 4. ZIP solo con la evidencia, cifrado AES-256 si hay contraseña (§5.3).
            await EvidenceZip.WriteAsync(zipPath, evidencePaths, password, ct);

            // 5. Hash del ZIP final (cifrado), ya cerrado: es el que figura en el informe y se
            // puede verificar sin la contraseña. AES-ZIP usa sal e IV aleatorios por entrada, así
            // que el mismo contenido da otro hash en cada generación; no importa porque un caso
            // Completed no se regenera (409).
            zipHash = await Sha256Async(zipPath);

            // 6. Verificación (D6): se reabre en solo lectura con la contraseña y se compara el
            // SHA-256 de cada entrada con el del original. Al ser solo lectura, zipHash no cambia.
            await EvidenceZip.VerifyAsync(zipPath, expected, password, ct);

            // 7. DOCX con el hash del ZIP en la tabla. 8. Hash del DOCX ya cerrado (el informe no
            // puede contener su propio hash: lo muestran ResultStep y CaseCard).
            // Flujo viejo: las imágenes y la salida son la misma carpeta del caso.
            await GenerateDocxAsync(cas, evidence, hashes, docxPath, imagesDir: caseDir, zipFilename, zipHash,
                branding.Current, ct);
            reportHash = await Sha256Async(docxPath);
        }
        catch
        {
            // Solo los dos artefactos de ESTE intento. Los archivos sueltos de la evidencia no se
            // tocan: con el caso en Error, el perito puede reintentar.
            foreach (var artifact in new[] { zipPath, docxPath })
            {
                try { if (File.Exists(artifact)) File.Delete(artifact); }
                catch (Exception cleanupEx)
                {
                    logger.LogWarning(cleanupEx, "No se pudo borrar {File} tras un error de generación",
                        Path.GetFileName(artifact));
                }
            }
            throw;
        }

        // 9. Recién con el ZIP verificado se borran los archivos sueltos: quedan dentro del ZIP.
        foreach (var f in evidence)
        {
            var fp = Path.Combine(caseDir, f.Name);
            if (File.Exists(fp)) File.Delete(fp);
        }

        return new ReportResult(zipPath, zipFilename, zipHash, password,
            ZipEncrypted: password is not null,
            ZipEncryption: password is null ? null : EvidenceZip.EncryptionAes256Ae2,
            docxPath, Path.GetFileName(docxPath), reportHash);
    }

    // ── Flujo agent (zip-local-informe-servidor §5.7) ───────────────────────

    public async Task<ReportOnlyResult> GenerateReportAsync(Case cas, List<FileInfoDto> evidence,
        IReadOnlyDictionary<string, string> hashes, string imagesDir, string outputDir,
        string zipFilename, string zipHash, CancellationToken ct = default)
    {
        var safeName = Sanitize($"{cas.NroReferencia}_{cas.NombreDenunciante}");
        var docxPath = Path.Combine(outputDir, $"informe_pericial_{safeName}.docx");

        // 1. Restos de un intento anterior con los nombres de ESTE caso. Ningún evidencia_*.zip.
        foreach (var stale in new[]
                 {
                     docxPath, Path.ChangeExtension(docxPath, ".pdf"),
                     Path.Combine(outputDir, $"informe_forense_{safeName}.docx"),
                     Path.Combine(outputDir, $"informe_forense_{safeName}.pdf"),
                 })
            if (File.Exists(stale)) File.Delete(stale);

        // 2. Evidencia en orden ordinal (tabla y anexo).
        var ordered = evidence
            .Where(f => !IsGeneratedArtifact(f.Name))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList();

        try
        {
            // 3. DOCX con las capturas de imagesDir y los hashes del manifiesto. 4. Hash del DOCX.
            await GenerateDocxAsync(cas, ordered, hashes, docxPath, imagesDir, zipFilename, zipHash,
                branding.Current, ct);
            var reportHash = await Sha256Async(docxPath);
            return new ReportOnlyResult(docxPath, Path.GetFileName(docxPath), reportHash);
        }
        catch
        {
            // 5. Solo el DOCX de este intento.
            try { if (File.Exists(docxPath)) File.Delete(docxPath); }
            catch (Exception cleanupEx)
            {
                logger.LogWarning(cleanupEx, "No se pudo borrar {File} tras un error de generación",
                    Path.GetFileName(docxPath));
            }
            throw;
        }
    }

    // ── DOCX (Anexo B, pasadas B-R0 a B-R9, más B-R2b de informe-diseno-modelo) ─

    // Placeholders de texto que la plantilla puede traer (A7.1, más el slot de atribución de la
    // v6, que llena B-R8). Cualquier otro se borra en B-R1.
    private static readonly HashSet<string> KnownPlaceholders = new(StringComparer.Ordinal)
    {
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
        "{ORGANIZACION}", "{CONTACTO}", "{CONTACTO_EN_LINEA}",
        AttributionSlot,
    };

    private static readonly HashSet<string> BlockKeys = new(StringComparer.Ordinal)
    {
        "MEMBRETE", "organismoTribunal", "objetoInforme", "capturasNombreDispositivo",
        "descripcionNotasTecnicas", "descripcionReserva", "anexoCapturas",
        "ISOTIPO", "MEMBRETE_CON_LOGO", "MEMBRETE_SIN_LOGO",
    };

    private static readonly Regex AnyPlaceholderRegex = new(
        @"\{[#/]?[A-Za-z_][A-Za-z0-9_]*(?::[0-9.,x]+)?\}", RegexOptions.CultureInvariant);
    private static readonly Regex TextPlaceholderRegex = new(
        @"\{[A-Za-z_][A-Za-z0-9_]*\}", RegexOptions.CultureInvariant);
    private static readonly Regex BlockMarkerRegex = new(
        @"^\{([#/])([A-Za-z_][A-Za-z0-9_]*)\}$", RegexOptions.CultureInvariant);

    // Capturas (B-4): identificación 7 × 10.5 cm; anexo 14 × 10.5 cm (dos por página en A4).
    private const long IdentShotMaxW = 2_520_000;
    private const long IdentShotMaxH = 3_780_000;
    private const long AnnexShotMaxW = 5_040_000;
    private const long AnnexShotMaxH = 3_780_000;

    private const string CaptionColor = "595959";
    private const string ContainerCaptionColor = "3D444C";
    private const string BodyFont = "Arial";

    // imagesDir: carpeta de donde se leen las capturas (la del caso en el flujo viejo; la
    // temporal .generate-tmp/<id>/ en el flujo agent). outputPath puede estar en otra carpeta.
    private Task GenerateDocxAsync(Case cas, List<FileInfoDto> files,
        IReadOnlyDictionary<string, string> hashes, string outputPath, string imagesDir,
        string zipFilename, string zipHash, BrandingSnapshot brand, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            // B-R0
            var templatePath = Path.Combine(TemplatesDir, templateFileName);
            if (!File.Exists(templatePath))
                throw new FileNotFoundException("Plantilla DOCX no encontrada", templatePath);

            File.Copy(templatePath, outputPath, overwrite: true);

            using var doc = WordprocessingDocument.Open(outputPath, isEditable: true);
            var mainPart = doc.MainDocumentPart!;
            var body = mainPart.Document.Body!;

            // Contador único de docPr id para todos los dibujos que agrega este servicio (logo,
            // capturas, sello del pie). Arranca en un offset alto para no pisar los ids de la
            // plantilla: tienen que ser únicos en header+footer+body juntos.
            uint drawId = 100_000;

            // Partes donde pueden vivir placeholders: el cuerpo y TODOS los headers/footers.
            var parts = new List<(OpenXmlPart Part, OpenXmlElement Root)> { (mainPart, body) };
            parts.AddRange(mainPart.HeaderParts.Select(h => ((OpenXmlPart)h, (OpenXmlElement)h.Header)));
            parts.AddRange(mainPart.FooterParts.Select(f => ((OpenXmlPart)f, (OpenXmlElement)f.Footer)));

            // Párrafos cuyo texto ya es definitivo (textos del perito, nombres de archivo, …):
            // B-R6 no los vuelve a escanear, así un "{caratula}" escrito por el usuario queda
            // como texto literal.
            var resolved = new HashSet<Paragraph>();

            var values = ReportValues.Placeholders(cas, settings, brand);
            var texts = cas.ReportTexts ?? new ReportTexts();

            // Capturas por rol (§7.7). Las fotos de identidad no van al informe (D9 B).
            var sources = cas.FileSources.Select(s => s.Filename).ToHashSet(StringComparer.Ordinal);
            var roles = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in cas.CaptureRoles) roles[r.Filename] = r.Role;
            var shots = files
                .Where(f => EvidenceClassifier.Classify(f.Name, sources.Contains(f.Name)) == EvidenceClass.Screenshot)
                .Select(f => f.Name)
                .Where(n => IsUsableImage(Path.Combine(imagesDir, n)))
                .ToList();
            var imeiShots = shots.Where(n => roles.GetValueOrDefault(n) == CaptureRole.ImeiModelo).ToList();
            var nameShots = shots.Where(n => roles.GetValueOrDefault(n) == CaptureRole.NombreDispositivo).ToList();
            var annexShots = shots.Where(n => !roles.ContainsKey(n)).ToList();

            var conditions = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                // MEMBRETE vive en el pie de la portada (footer2 de la v6), con dos variantes:
                // con logo (tabla logo | nombre y contacto) y sin logo (solo texto).
                ["MEMBRETE"] = brand.OrganizationName is not null || brand.Logo is not null || brand.ContactLines.Count > 0,
                ["MEMBRETE_CON_LOGO"] = brand.Logo is not null,
                ["MEMBRETE_SIN_LOGO"] = brand.Logo is null,
                // ISOTIPO: solo al cierre (el nombre va siempre en el encabezado interior).
                ["ISOTIPO"] = brand.Isotype is not null,
                ["organismoTribunal"] = !string.IsNullOrWhiteSpace(cas.OrganismoTribunal),
                // editor-texto-enriquecido §6.4: "vacío" según el formato del caso. Con
                // editor-imagenes-informe (DP1 B) una sección opcional sale también si solo tiene
                // imágenes (HasBlockContent); los obligatorios siguen con IsBlank.
                ["objetoInforme"] = ReportTextRules.HasBlockContent(texts, texts.ObjetoInforme),
                ["capturasNombreDispositivo"] = nameShots.Count > 0,
                ["descripcionNotasTecnicas"] = ReportTextRules.HasBlockContent(texts, texts.NotasTecnicas),
                ["descripcionReserva"] = ReportTextRules.HasBlockContent(texts, texts.Reserva),
                ["anexoCapturas"] = annexShots.Count > 0,
            };

            // B-R1: placeholders desconocidos fuera, ANTES de insertar valores.
            var warned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (_, root) in parts)
                RemoveUnknownPlaceholders(root, warned);

            // B-R2: bloques condicionales.
            foreach (var (_, root) in parts)
                ResolveBlocks(root, conditions);

            // B-R2b: colores de marca. La plantilla trae el centinela del primario (= verde de
            // Factum, el default).
            BrandColors.Apply(mainPart, brand.PrimaryColor, brand.AccentColor);

            // B-R3: {LOGO_ORGANIZACION[:WxH]} → logo y {ISOTIPO_ORGANIZACION[:WxH]} → isotipo
            // (o párrafo vacío).
            foreach (var (part, root) in parts)
            {
                ReplaceImagePlaceholder(part, root, LogoPlaceholderRegex, brand.Logo, "logo-organizacion",
                    DefaultLogoBoxWidthCm, DefaultLogoBoxHeightCm, ref drawId);
                ReplaceImagePlaceholder(part, root, IsotypePlaceholderRegex, brand.Isotype, "isotipo-organizacion",
                    DefaultIsotypeBoxCm, DefaultIsotypeBoxCm, ref drawId);
            }

            // B-R4: multilínea. {CONTACTO} (saltos dentro del run) y los textos del perito
            // (un párrafo por línea).
            foreach (var (_, root) in parts)
                ReplaceMultilinePlaceholder(root, "{CONTACTO}", brand.ContactLines, values, resolved);
            var multiline = new (string Placeholder, string Value)[]
            {
                ("{objetoInforme}", texts.ObjetoInforme),
                ("{descripcionOperacionesRealizadas}", texts.OperacionesRealizadas),
                ("{descripcionAseguramientoEvidencia}", texts.AseguramientoEvidencia),
                ("{descripcionResultados}", texts.Resultados),
                ("{descripcionValoracionTecnica}", texts.ValoracionTecnica),
                ("{descripcionConclusiones}", texts.Conclusiones),
                ("{descripcionNotasTecnicas}", texts.NotasTecnicas),
                ("{descripcionReserva}", texts.Reserva),
            };
            // Dos caminos (editor-texto-enriquecido §6.4): con Formato = "markdown", el dialecto
            // Factum → OpenXML; sin marca (casos anteriores), texto plano, un párrafo por línea,
            // exactamente como antes.
            var markdown = texts.Formato == ReportTextFormats.Markdown;
            var numbering = new ReportListNumbering(mainPart);
            // editor-imagenes-informe §6.5: capturas del cuerpo; B-R4 deja anclas y B-R7b las llena.
            var bodyImages = new List<PendingReportImage>();
            foreach (var (placeholder, value) in multiline)
            {
                if (markdown)
                    ReportMarkdownRenderer.ReplacePlaceholder(mainPart, body, placeholder, value, resolved, numbering,
                        bodyImages);
                else
                    ReplaceParagraphPerLine(body, placeholder, value, resolved);
            }

            // B-R5: tabla de hashes. Corre después de B-R2b: el tinte de la fila del ZIP es el
            // acento ya resuelto, no un centinela.
            FillHashTable(body, files, hashes, zipFilename, zipHash, brand.AccentColor, resolved);

            // B-R6: texto, por run y en una sola pasada.
            foreach (var (_, root) in parts)
                foreach (var para in root.Descendants<Paragraph>().ToList())
                    if (!resolved.Contains(para))
                        RewriteParagraph(para, TextPlaceholderRegex,
                            m => values.TryGetValue(m.Value, out var v) ? v : null);

            // B-R7: imágenes con pie de foto.
            ReplaceWithImages(mainPart, body, "{capturasImeiModelo}", imagesDir, imeiShots,
                IdentShotMaxW, IdentShotMaxH,
                (_, name) => $"Captura de identificación (IMEI y modelo) – {name}", ref drawId);
            ReplaceWithImages(mainPart, body, "{capturasNombreDispositivo}", imagesDir, nameShots,
                IdentShotMaxW, IdentShotMaxH,
                (_, name) => $"Captura de identificación (nombre del dispositivo) – {name}", ref drawId);
            ReplaceWithImages(mainPart, body, "{anexoCapturas}", imagesDir, annexShots,
                AnnexShotMaxW, AnnexShotMaxH,
                (n, name) => $"Figura {n} – {name}", ref drawId);

            // B-R7b (editor-imagenes-informe §6.5): capturas dentro de las secciones, numeradas
            // con la misma lista del anexo y reutilizando su ImagePart.
            InsertBodyImages(mainPart, body, bodyImages, imagesDir, annexShots, roles, hashes, resolved, ref drawId);

            // B-R8: atribución obligatoria "Realizado con Factum" en el pie de todas las
            // páginas — se inyecta por código para que ninguna plantilla la pueda sacar.
            AddFactumAttributionFooter(mainPart, body, ref drawId);

            // B-R9
            WarnLeftovers(parts, resolved);
            doc.Save();
        }, ct);
    }

    // ── B-R1: placeholders desconocidos ──────────────────────────────────────

    private void RemoveUnknownPlaceholders(OpenXmlElement root, HashSet<string> warned)
    {
        foreach (var para in root.Descendants<Paragraph>().ToList())
        {
            RewriteParagraph(para, AnyPlaceholderRegex, m =>
            {
                if (IsKnownPlaceholder(m.Value)) return null;
                if (warned.Add(m.Value))
                    logger.LogWarning("Plantilla: placeholder desconocido {Placeholder}", m.Value);
                return string.Empty;
            });
        }
    }

    private static bool IsKnownPlaceholder(string ph)
    {
        if (KnownPlaceholders.Contains(ph)) return true;
        if (LogoPlaceholderRegex.IsMatch(ph) && LogoPlaceholderRegex.Match(ph).Length == ph.Length) return true;
        if (IsotypePlaceholderRegex.IsMatch(ph) && IsotypePlaceholderRegex.Match(ph).Length == ph.Length) return true;
        var marker = BlockMarkerRegex.Match(ph);
        return marker.Success && BlockKeys.Contains(marker.Groups[2].Value);
    }

    // ── B-R2: bloques condicionales {#clave} … {/clave} (§7.3) ──────────────

    private void ResolveBlocks(OpenXmlElement root, IReadOnlyDictionary<string, bool> conditions)
    {
        var markers = root.Descendants<Paragraph>()
            .Select(p => (Para: p, Match: BlockMarkerRegex.Match(ParagraphText(p).Trim())))
            .Where(x => x.Match.Success)
            .ToList();

        bool Attached(OpenXmlElement e) => e.Ancestors().Contains(root);

        foreach (var (open, match) in markers)
        {
            if (match.Groups[1].Value != "#" || !Attached(open)) continue;
            var key = match.Groups[2].Value;
            var closeText = "{/" + key + "}";
            var close = open.ElementsAfter().OfType<Paragraph>()
                .FirstOrDefault(p => ParagraphText(p).Trim() == closeText);

            if (close is null)
            {
                logger.LogWarning("Plantilla: el bloque {Key} abre y no cierra; se borran solo los marcadores", key);
                open.Remove();
                continue;
            }

            if (conditions.GetValueOrDefault(key, true))
            {
                open.Remove();
                close.Remove();
                continue;
            }

            var toRemove = new List<OpenXmlElement>();
            for (OpenXmlElement? e = open; e is not null; e = e.NextSibling())
            {
                toRemove.Add(e);
                if (e == close) break;
            }
            foreach (var e in toRemove) e.Remove();
        }

        // Cierres huérfanos (o marcadores que quedaron sueltos): se borran solo los marcadores.
        foreach (var (para, match) in markers)
        {
            if (!Attached(para)) continue;
            logger.LogWarning("Plantilla: el marcador {Marker} no tiene pareja; se borra",
                match.Value);
            para.Remove();
        }
    }

    // ── B-R4: texto multilínea (§7.4) ────────────────────────────────────────

    // Un párrafo que contiene SOLO el placeholder se reemplaza por un párrafo por línea, con el
    // pPr del párrafo y el rPr de su run. Las líneas vacías del medio quedan como párrafos
    // vacíos; las de los extremos se descartan.
    private static void ReplaceParagraphPerLine(OpenXmlElement root, string placeholder, string value,
        HashSet<Paragraph> resolved)
    {
        foreach (var para in root.Descendants<Paragraph>().ToList())
        {
            if (ParagraphText(para).Trim() != placeholder) continue;

            var pPr = para.ParagraphProperties;
            var rPr = para.Descendants<Run>()
                .FirstOrDefault(r => r.Elements<Text>().Any(t => t.Text.Length > 0))?.RunProperties;

            var lines = ReportValues.Clean(value).Split('\n').Select(l => l.TrimEnd()).ToList();
            while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
            while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

            foreach (var line in lines)
            {
                var p = new Paragraph();
                if (pPr is not null) p.AppendChild((ParagraphProperties)pPr.CloneNode(true));
                if (line.Length > 0)
                {
                    var run = new Run();
                    if (rPr is not null) run.AppendChild((RunProperties)rPr.CloneNode(true));
                    run.AppendChild(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
                    p.AppendChild(run);
                }
                para.InsertBeforeSelf(p);
                resolved.Add(p);
            }
            para.Remove();
        }
    }

    // ── B-R5: tabla de hashes (§7.5) ─────────────────────────────────────────

    private void FillHashTable(Body body, List<FileInfoDto> files, IReadOnlyDictionary<string, string> hashes,
        string zipFilename, string zipHash, string tint, HashSet<Paragraph> resolved)
    {
        var model = body.Descendants<TableRow>()
            .FirstOrDefault(r => r.Descendants<Paragraph>().Any(p => ParagraphText(p).Contains("{nombreArchivo}")));
        if (model is null)
        {
            logger.LogWarning("Plantilla: no se encontró la fila {{nombreArchivo}} de la tabla de hashes");
            return;
        }

        foreach (var f in files)
        {
            var origin = string.IsNullOrWhiteSpace(f.SourcePath) ? null : $"Origen: {f.SourcePath}";
            model.InsertBeforeSelf(BuildHashRow(model, f.Name, origin,
                hashes.GetValueOrDefault(f.Name, "-"), resolved));
        }
        model.InsertBeforeSelf(BuildHashRow(model, zipFilename, "Contenedor de la evidencia", zipHash, resolved,
            isContainer: true, tint: tint));
        model.Remove();
    }

    // isContainer: la fila del ZIP lleva el tinte (acento) de fondo, el nombre en negrita y la
    // leyenda recta en gris; así se distingue también impresa en blanco y negro.
    private static TableRow BuildHashRow(TableRow model, string name, string? secondLine, string hash,
        HashSet<Paragraph> resolved, bool isContainer = false, string? tint = null)
    {
        var row = (TableRow)model.CloneNode(true);
        StripParagraphIds(row);

        var namePara = row.Descendants<Paragraph>().First(p => ParagraphText(p).Contains("{nombreArchivo}"));
        var hashPara = row.Descendants<Paragraph>().FirstOrDefault(p => ParagraphText(p).Contains("{hashArchivo}"));

        RewriteParagraph(namePara, TextPlaceholderRegex,
            m => m.Value == "{nombreArchivo}" ? ReportValues.Clean(name) : null);
        if (hashPara is not null)
            RewriteParagraph(hashPara, TextPlaceholderRegex, m => m.Value == "{hashArchivo}" ? hash : null);

        if (secondLine is not null)
        {
            // Segundo párrafo en la misma celda. Archivos: cursiva, 7 pt, gris (el estilo del
            // "Origen:" de la v3). Contenedor ZIP: recto, 8 pt, gris secundario.
            var rPr = namePara.Descendants<Run>().FirstOrDefault()?.RunProperties?.CloneNode(true) as RunProperties
                      ?? new RunProperties();
            rPr.Bold = null;
            rPr.BoldComplexScript = null;
            if (isContainer)
            {
                rPr.Italic = null;
                rPr.Color = new Color { Val = ContainerCaptionColor };
                rPr.FontSize = new FontSize { Val = "16" };
                rPr.FontSizeComplexScript = new FontSizeComplexScript { Val = "16" };
            }
            else
            {
                rPr.Italic = new Italic();
                rPr.Color = new Color { Val = CaptionColor };
                rPr.FontSize = new FontSize { Val = "14" };
                rPr.FontSizeComplexScript = new FontSizeComplexScript { Val = "14" };
            }
            var p = new Paragraph();
            if (namePara.ParagraphProperties is { } pPr) p.AppendChild((ParagraphProperties)pPr.CloneNode(true));
            p.AppendChild(new Run(rPr, new Text(ReportValues.Clean(secondLine)) { Space = SpaceProcessingModeValues.Preserve }));
            namePara.InsertAfterSelf(p);
        }

        if (isContainer)
        {
            // Nombre del ZIP en negrita (los runs del párrafo del nombre, no la leyenda).
            foreach (var run in namePara.Elements<Run>())
            {
                var rp = run.RunProperties ??= new RunProperties();
                rp.Bold = new Bold();
                rp.BoldComplexScript = new BoldComplexScript();
            }

            if (!string.IsNullOrEmpty(tint))
                foreach (var cell in row.Elements<TableCell>())
                {
                    var tcPr = cell.TableCellProperties ??= new TableCellProperties();
                    tcPr.Shading = new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = tint };
                }
        }

        foreach (var p in row.Descendants<Paragraph>()) resolved.Add(p);
        return row;
    }

    private const string W14Namespace = "http://schemas.microsoft.com/office/word/2010/wordml";

    // Un clon no puede repetir los w14:paraId/textId de la plantilla (tienen que ser únicos).
    private static void StripParagraphIds(OpenXmlElement root)
    {
        foreach (var e in root.Descendants().Prepend(root))
        {
            switch (e)
            {
                case Paragraph p:
                    p.ParagraphId = null;
                    p.TextId = null;
                    break;
                case TableRow tr:
                    tr.ParagraphId = null;
                    tr.TextId = null;
                    break;
                default:
                    if (e.HasAttributes &&
                        e.GetAttributes().Any(a => a.NamespaceUri == W14Namespace && a.LocalName is "paraId" or "textId"))
                    {
                        e.RemoveAttribute("paraId", W14Namespace);
                        e.RemoveAttribute("textId", W14Namespace);
                    }
                    break;
            }
        }
    }

    // ── B-R6: reemplazo por run, en una sola pasada ──────────────────────────

    // Texto del párrafo (solo sus propios w:t, no los de párrafos anidados en cuadros de texto).
    private static List<Text> OwnTexts(Paragraph para) =>
        para.Descendants<Text>().Where(t => t.Ancestors<Paragraph>().FirstOrDefault() == para).ToList();

    private static string ParagraphText(Paragraph para) =>
        string.Concat(OwnTexts(para).Select(t => t.Text));

    // Concatena el texto de los w:t del párrafo guardando el offset de cada uno, busca los
    // matches y, de atrás hacia adelante, escribe el reemplazo en el w:t donde empieza el match
    // y recorta de los w:t siguientes los fragmentos del placeholder que caigan ahí. Los runs y
    // sus rPr se conservan (los rótulos en negrita siguen en negrita y el valor no), y los
    // valores insertados no se vuelven a escanear. replacement == null → se deja el match.
    private static void RewriteParagraph(Paragraph para, Regex regex, Func<Match, string?> replacement)
    {
        var texts = OwnTexts(para);
        if (texts.Count == 0) return;

        var starts = new int[texts.Count];
        var sb = new StringBuilder();
        for (var i = 0; i < texts.Count; i++)
        {
            starts[i] = sb.Length;
            sb.Append(texts[i].Text);
        }
        var full = sb.ToString();
        if (full.IndexOf('{') < 0) return;

        var matches = regex.Matches(full).Cast<Match>().Reverse().ToList();
        foreach (var m in matches)
        {
            var value = replacement(m);
            if (value is null) continue;

            var i = Array.FindLastIndex(starts, s => s <= m.Index);
            while (i < texts.Count - 1 && starts[i] + texts[i].Text.Length <= m.Index) i++;

            var remaining = m.Length;
            var local = m.Index - starts[i];
            var first = texts[i];
            var take = Math.Min(remaining, first.Text.Length - local);
            first.Text = first.Text[..local] + value + first.Text[(local + take)..];
            first.Space = SpaceProcessingModeValues.Preserve;
            remaining -= take;

            for (var j = i + 1; remaining > 0 && j < texts.Count; j++)
            {
                var t = texts[j];
                var cut = Math.Min(remaining, t.Text.Length);
                t.Text = t.Text[cut..];
                t.Space = SpaceProcessingModeValues.Preserve;
                remaining -= cut;
            }
        }
    }

    // ── B-R7: imágenes (§7.7) ────────────────────────────────────────────────

    private void ReplaceWithImages(MainDocumentPart mainPart, OpenXmlElement root, string placeholder,
        string caseDir, IReadOnlyList<string> fileNames, long maxW, long maxH,
        Func<int, string, string> caption, ref uint drawId)
    {
        foreach (var target in FindAllParagraphsWithPlaceholder(root, placeholder))
        {
            var n = 0;
            foreach (var name in fileNames)
            {
                var path = Path.Combine(caseDir, name);
                if (!IsUsableImage(path)) continue;
                n++;
                // keepNext: la imagen no se separa de su pie de foto.
                var pPr = new ParagraphProperties(
                    new KeepNext(),
                    new SpacingBetweenLines { Before = "120", After = "60" },
                    new Justification { Val = JustificationValues.Center });
                target.InsertBeforeSelf(BuildImageParagraph(mainPart, ImageSource.FromFile(path),
                    maxW, maxH, ref drawId, pPr));
                target.InsertBeforeSelf(BuildCaptionParagraph(caption(n, name)));
            }
            if (n == 0)
                logger.LogWarning("Informe: {Placeholder} sin imágenes; se borra el párrafo", placeholder);
            target.Remove();
        }
    }

    // ── B-R7b: capturas en el cuerpo (editor-imagenes-informe §6.5) ─────────

    /// <summary>
    /// Pone cada imagen pendiente de B-R4 en su ancla: dibujo centrado en la caja del anexo
    /// (14 × 10.5 cm, keepNext) y epígrafe D10. N = posición de la captura en
    /// <paramref name="annexShots"/>, la misma lista que numera <c>{anexoCapturas}</c>. Reutiliza
    /// el ImagePart que B-R7 ya embebió para esa captura si el tipo coincide (D9); si no, crea
    /// uno del tipo detectado por streaming. Los bytes nunca se recodifican.
    /// </summary>
    private static void InsertBodyImages(MainDocumentPart mainPart, Body body,
        IReadOnlyList<PendingReportImage> pending, string caseDir, IReadOnlyList<string> annexShots,
        IReadOnlyDictionary<string, string> roles, IReadOnlyDictionary<string, string> hashes,
        HashSet<Paragraph> resolved, ref uint drawId)
    {
        if (pending.Count == 0) return;

        var figure = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < annexShots.Count; i++) figure[annexShots[i]] = i + 1;

        // Dibujos que B-R7 ya puso en el cuerpo (anexo e identificación): BuildDrawing les pone
        // de nombre el del archivo. Las anclas todavía no tienen dibujos.
        var existing = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var inline in body.Descendants<DWP.Inline>())
        {
            var name = inline.GetFirstChild<DWP.DocProperties>()?.Name?.Value;
            var relId = inline.Descendants<DRAW.Blip>().FirstOrDefault()?.Embed?.Value;
            if (name is not null && relId is not null) existing.TryAdd(name, relId);
        }

        foreach (var image in pending)
        {
            var filename = image.Filename;
            if (!ReportImageFiles.TryOpen(caseDir, filename, out var stream, out var contentType,
                    out var width, out var height))
                throw new InvalidOperationException($"La captura {filename} no está disponible");

            using (stream)
            {
                if (!hashes.ContainsKey(filename))
                    throw new InvalidOperationException(
                        $"La captura {filename} no está en la tabla de hashes de la evidencia");

                var suffix = image.Alt.Length > 0 ? $"{image.Alt} ({filename})" : filename;
                var caption = roles.GetValueOrDefault(filename) switch
                {
                    CaptureRole.ImeiModelo => $"Captura de identificación (IMEI y modelo) – {suffix}",
                    CaptureRole.NombreDispositivo => $"Captura de identificación (nombre del dispositivo) – {suffix}",
                    _ => figure.TryGetValue(filename, out var n)
                        ? $"Figura {n} – {suffix}"
                        : throw new InvalidOperationException($"La captura {filename} no está en el anexo"),
                };

                string relId;
                if (existing.TryGetValue(filename, out var reused) &&
                    mainPart.TryGetPartById(reused, out var part) && part is ImagePart imagePart &&
                    imagePart.ContentType == contentType)
                {
                    relId = reused;
                }
                else
                {
                    var newPart = mainPart.AddImagePart(contentType == "image/jpeg" ? ImagePartType.Jpeg : ImagePartType.Png);
                    newPart.FeedData(stream); // por bloques, sin cargar el archivo en memoria
                    relId = mainPart.GetIdOfPart(newPart);
                    existing[filename] = relId;
                }

                // Mismo pPr que las capturas del anexo.
                var pPr = new ParagraphProperties(
                    new KeepNext(),
                    new SpacingBetweenLines { Before = "120", After = "60" },
                    new Justification { Val = JustificationValues.Center });
                image.Anchor.InsertBeforeSelf(BuildImageParagraphForRel(relId, filename, width, height,
                    AnnexShotMaxW, AnnexShotMaxH, ref drawId, pPr));
                var captionParagraph = BuildCaptionParagraph(caption);
                image.Anchor.InsertBeforeSelf(captionParagraph);
                resolved.Add(captionParagraph);
                resolved.Remove(image.Anchor);
                image.Anchor.Remove();
            }
        }
    }

    private static Paragraph BuildCaptionParagraph(string text) =>
        new(
            new ParagraphProperties(
                new SpacingBetweenLines { Before = "0", After = "200" },
                new Justification { Val = JustificationValues.Center }),
            new Run(
                new RunProperties(
                    new RunFonts { Ascii = BodyFont, HighAnsi = BodyFont, EastAsia = BodyFont, ComplexScript = BodyFont },
                    new Italic(),
                    new Color { Val = CaptionColor },
                    new FontSize { Val = "16" },
                    new FontSizeComplexScript { Val = "16" }),
                new Text(ReportValues.Clean(text)) { Space = SpaceProcessingModeValues.Preserve }));

    // ── B-R9: placeholders conocidos que quedaron sin reemplazar ─────────────

    private void WarnLeftovers(IEnumerable<(OpenXmlPart Part, OpenXmlElement Root)> parts,
        HashSet<Paragraph> resolved)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, root) in parts)
            foreach (var para in root.Descendants<Paragraph>())
            {
                if (resolved.Contains(para)) continue;
                foreach (Match m in AnyPlaceholderRegex.Matches(ParagraphText(para)))
                    if (IsKnownPlaceholder(m.Value) && seen.Add(m.Value))
                        logger.LogWarning("Informe: el placeholder {Placeholder} quedó sin reemplazar", m.Value);
            }
    }

    // ── Imágenes ─────────────────────────────────────────────────────────────

    // Un archivo de 0 bytes (captura que falló en el dispositivo) pasa File.Exists pero hace
    // explotar GetImageSize al intentar leer el header — se filtra acá, en el único lugar que
    // decide si una imagen es embebible.
    private static bool IsUsableImage(string path) =>
        File.Exists(path) && new FileInfo(path).Length > 0;

    // Devuelve TODOS los párrafos que contienen el placeholder (texto concatenado de sus runs).
    private static List<Paragraph> FindAllParagraphsWithPlaceholder(OpenXmlElement root, string placeholder) =>
        root.Descendants<Paragraph>().Where(p => ParagraphText(p).Contains(placeholder)).ToList();

    // Imagen a embeber: desde un archivo (capturas) o desde bytes en memoria (logo de la
    // organización, sello de Factum). Width/Height en píxeles, solo para la proporción.
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

    // owner: la parte dueña del contenido (MainDocumentPart, HeaderPart o FooterPart) — la
    // imagen se agrega como ImagePart de ESA parte, porque la relación r:embed se resuelve
    // contra las relaciones de la parte que contiene el dibujo.
    private static string EmbedImagePart(OpenXmlPart owner, ImageSource source)
    {
        var imagePart = AddImagePart(owner, source.PartType);
        using (var fs = source.Open())
            imagePart.FeedData(fs);
        return owner.GetIdOfPart(imagePart);
    }

    // Imagen ajustada SIN recortar (fit) dentro de maxW × maxH, manteniendo la proporción.
    // paragraphProperties: si viene, reemplaza al pPr por defecto (centrado, 6 pt arriba y abajo).
    private static Paragraph BuildImageParagraph(OpenXmlPart owner,
        ImageSource source, long maxW, long maxH, ref uint drawId,
        ParagraphProperties? paragraphProperties = null)
    {
        var relId = EmbedImagePart(owner, source);
        return BuildImageParagraphForRel(relId, source.Name, source.Width, source.Height, maxW, maxH,
            ref drawId, paragraphProperties);
    }

    // Párrafo con el dibujo de una parte ya embebida (relId), ajustado sin recortar a maxW × maxH.
    // Lo comparten BuildImageParagraph y las capturas del cuerpo (B-R7b), que reutilizan la parte.
    private static Paragraph BuildImageParagraphForRel(string relId, string name, int imgW, int imgH,
        long maxW, long maxH, ref uint drawId, ParagraphProperties? paragraphProperties)
    {
        (imgW, imgH) = (Math.Max(1, imgW), Math.Max(1, imgH));

        long dispW, dispH;
        double ratio = (double)imgH / imgW;
        if (ratio * maxW <= maxH) { dispW = maxW; dispH = (long)(maxW * ratio); }
        else { dispH = maxH; dispW = (long)(maxH / ratio); }

        var drawing = BuildDrawing(relId, name, dispW, dispH, drawId++);

        return new Paragraph(
            paragraphProperties ?? new ParagraphProperties(
                new SpacingBetweenLines { Before = "120", After = "120" },
                new Justification { Val = JustificationValues.Center }),
            new Run(drawing));
    }

    private static Drawing BuildDrawing(string relId, string name, long widthEmu, long heightEmu, uint id)
    {
        // Construído con AppendChild para evitar ambigüedad en object initializers anidados.
        var geom = new DRAW.PresetGeometry(new DRAW.AdjustValueList());
        geom.Preset = DRAW.ShapeTypeValues.Rectangle;

        var spPr = new PIC.ShapeProperties(
            new DRAW.Transform2D(
                new DRAW.Offset { X = 0L, Y = 0L },
                new DRAW.Extents { Cx = widthEmu, Cy = heightEmu }),
            geom);

        var blipFill = new PIC.BlipFill();
        blipFill.Append(new DRAW.Blip { Embed = relId });
        blipFill.Append(new DRAW.Stretch(new DRAW.FillRectangle()));

        var pic = new PIC.Picture(
            new PIC.NonVisualPictureProperties(
                new PIC.NonVisualDrawingProperties { Id = 0U, Name = name },
                new PIC.NonVisualPictureDrawingProperties(
                    new DRAW.PictureLocks { NoChangeAspect = true })),
            blipFill,
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
                if (w > 0 && h > 0) return (w, h);
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

    // {ISOTIPO_ORGANIZACION} o {ISOTIPO_ORGANIZACION:<ancho>x<alto>} (cm); en la v6, al cierre.
    private static readonly Regex IsotypePlaceholderRegex = new(
        @"\{ISOTIPO_ORGANIZACION(?::(\d+(?:[.,]\d+)?)x(\d+(?:[.,]\d+)?))?\}",
        RegexOptions.CultureInvariant);

    private const double DefaultLogoBoxWidthCm = 5.0;
    private const double DefaultLogoBoxHeightCm = 1.5;
    private const double DefaultIsotypeBoxCm = 2.0;
    private const long EmuPerCm = 360_000;

    // Reemplaza cada párrafo con el placeholder de imagen ({LOGO_ORGANIZACION[:WxH]} o
    // {ISOTIPO_ORGANIZACION[:WxH]}) por la imagen ajustada SIN recortar dentro de la caja,
    // conservando las ParagraphProperties del párrafo original. Sin imagen, se borra solo el
    // texto del placeholder y el párrafo queda vacío.
    private static void ReplaceImagePlaceholder(OpenXmlPart owner, OpenXmlElement root, Regex placeholder,
        BrandingLogo? image, string baseName, double defaultWidthCm, double defaultHeightCm, ref uint drawId)
    {
        foreach (var para in root.Descendants<Paragraph>().ToList())
        {
            var runs = para.Elements<Run>().ToList();
            var fullText = string.Concat(runs.SelectMany(r => r.Elements<Text>()).Select(t => t.Text));
            var match = placeholder.Match(fullText);
            if (!match.Success) continue;

            if (image is null)
            {
                CollapseParagraphText(runs, placeholder.Replace(fullText, string.Empty));
                continue;
            }

            var boxW = ParseCm(match.Groups[1], defaultWidthCm);
            var boxH = ParseCm(match.Groups[2], defaultHeightCm);
            var pPr = para.ParagraphProperties?.CloneNode(true) as ParagraphProperties
                      ?? new ParagraphProperties();
            var source = ImageSource.FromBytes($"{baseName}{image.Extension}", image.Data,
                image.ContentType, image.Width, image.Height);
            var imgPara = BuildImageParagraph(owner, source,
                (long)Math.Round(boxW * EmuPerCm), (long)Math.Round(boxH * EmuPerCm),
                ref drawId, paragraphProperties: pPr);
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

    // Placeholder de texto multilínea ({CONTACTO}): las líneas quedan en el MISMO run (con el
    // formato del run original), separadas por <w:br/>. El resto de los placeholders del
    // párrafo se resuelven acá mismo, pieza por pieza y en una sola pasada, y el párrafo queda
    // marcado como resuelto (B-R6 no lo vuelve a escanear).
    private static void ReplaceMultilinePlaceholder(OpenXmlElement root, string placeholder,
        IReadOnlyList<string> lines, IReadOnlyDictionary<string, string> values, HashSet<Paragraph> resolved)
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
                var piece = TextPlaceholderRegex.Replace(pieces[i],
                    m => values.TryGetValue(m.Value, out var v) ? v : m.Value);
                if (piece.Length > 0)
                    firstRun.AppendChild(new Text(piece) { Space = SpaceProcessingModeValues.Preserve });

                if (i == pieces.Length - 1) break;
                for (var l = 0; l < lines.Count; l++)
                {
                    if (l > 0) firstRun.AppendChild(new Break());
                    firstRun.AppendChild(new Text(ReportValues.Clean(lines[l])) { Space = SpaceProcessingModeValues.Preserve });
                }
            }

            foreach (var r in runs.Skip(1).ToList()) r.Remove();
            resolved.Add(para);
        }
    }

    // Deja newText en el primer run del párrafo y borra el resto de los runs.
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

    // Slot de la plantilla (pie interior de la v6): B-R8 pone ahí el sello y el texto, en la
    // misma línea que "Página N de M". Sin slot, la atribución va en un párrafo centrado al final.
    private const string AttributionSlot = "{ATRIBUCION_FACTUM}";
    private const long SelloSizeEmu = 144_000; // 0.4 cm

    // Agrega al pie de TODAS las páginas el Sello de Factum (0.4 cm) + "Realizado con Factum"
    // (Arial 8 pt, gris). Se inyecta por código para que ninguna plantilla (incluidas las de
    // clientes) la pueda omitir. Si el footer trae el slot {ATRIBUCION_FACTUM} entero en un run,
    // los runs de la atribución van en su lugar; si no, se agrega un párrafo centrado al final
    // (y si el slot estaba partido, se borra su texto). Cada footer termina con exactamente una
    // atribución. Por cada sección:
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
            var slot = footerPart.Footer.Descendants<Paragraph>()
                .FirstOrDefault(p => ParagraphText(p).Contains(AttributionSlot));
            if (slot is null || !TryFillAttributionSlot(footerPart, slot, sello, ref drawId))
                footerPart.Footer.AppendChild(BuildAttributionParagraph(footerPart, sello, ref drawId));
            footerPart.Footer.Save();
        }
    }

    // Reemplaza el run cuyo texto es exactamente el slot por los runs de la atribución. Si el
    // slot no está entero en un run, borra su texto (en todos los párrafos del footer donde
    // aparezca) y devuelve false: el llamador agrega el párrafo centrado de siempre.
    private static bool TryFillAttributionSlot(FooterPart owner, Paragraph slotPara, BrandingLogo? sello,
        ref uint drawId)
    {
        var slotRun = slotPara.Elements<Run>()
            .FirstOrDefault(r => string.Concat(r.Elements<Text>().Select(t => t.Text)) == AttributionSlot);
        if (slotRun is null)
        {
            foreach (var p in owner.Footer!.Descendants<Paragraph>().ToList())
                RewriteParagraph(p, new Regex(Regex.Escape(AttributionSlot)), _ => string.Empty);
            return false;
        }

        foreach (var run in BuildAttributionRuns(owner, sello, ref drawId))
            slotRun.InsertBeforeSelf(run);
        slotRun.Remove();
        return true;
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
        foreach (var run in BuildAttributionRuns(owner, sello, ref drawId))
            para.AppendChild(run);
        return para;
    }

    // El sello (si hay) y el texto "Realizado con Factum" en Arial 8 pt gris.
    private static List<Run> BuildAttributionRuns(FooterPart owner, BrandingLogo? sello, ref uint drawId)
    {
        var runs = new List<Run>();
        if (sello is not null)
        {
            var relId = EmbedImagePart(owner, ImageSource.FromBytes("factum-sello.png", sello.Data,
                sello.ContentType, sello.Width, sello.Height));
            // position -3 (medios puntos) baja el Sello 1.5 pt para centrarlo con el texto de 8 pt.
            runs.Add(new Run(
                new RunProperties(new Position { Val = "-3" }),
                BuildDrawing(relId, "factum-sello.png", SelloSizeEmu, SelloSizeEmu, drawId++)));
        }

        runs.Add(new Run(
            new RunProperties(
                new RunFonts { Ascii = "Arial", HighAnsi = "Arial", ComplexScript = "Arial" },
                new Color { Val = "5C656E" },
                new FontSize { Val = "16" },
                new FontSizeComplexScript { Val = "16" }),
            new Text(sello is not null ? " " + AttributionText : AttributionText)
            { Space = SpaceProcessingModeValues.Preserve }));
        return runs;
    }

    // ── PDF: conversión del DOCX con LibreOffice ─────────────────────────────
    // Sin uso: el informe se entrega en DOCX. libreoffice-writer ya no está en la imagen Docker
    // (HU instalacion-local-docker), así que en el contenedor esto devolvería "LibreOffice no
    // encontrado"; nadie lo llama. Borrar este código muerto queda para otra HU.

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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<Dictionary<string, string>> BuildHashesAsync(
        List<FileInfoDto> files, string caseDir)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in files)
        {
            var path = Path.Combine(caseDir, f.Name);
            if (!File.Exists(path)) continue;
            result[f.Name] = await Sha256Async(path);
        }
        return result;
    }

    private static async Task<string> Sha256Async(string path)
    {
        using var sha = SHA256.Create();
        await using var fs = File.OpenRead(path);
        return Convert.ToHexStringLower(await sha.ComputeHashAsync(fs));
    }

    internal static string GeneratePassword()
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

    // Recorta sobre el string ya filtrado y sin '_' en los extremos (antes cortaba con el largo
    // previo al Trim y explotaba con ArgumentOutOfRangeException). Vacío → "caso".
    internal static string Sanitize(string s)
    {
        var filtered = new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        filtered = filtered[..Math.Min(filtered.Length, 60)];
        return filtered.Length == 0 ? "caso" : filtered;
    }
}
