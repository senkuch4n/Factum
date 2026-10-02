using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Factum.Backend.Services.Reports;

/// <summary>
/// Markdown del dialecto Factum → párrafos OpenXML (editor-texto-enriquecido, SDD §6.3). Recorre
/// el AST de Markdig a mano: nada de HTML intermedio ni XML como string. Parte del pPr del
/// párrafo del placeholder y del rPr de su primer run con texto (mismo criterio que
/// ReplaceParagraphPerLine) y les aplica formato directo sobre clones.
/// </summary>
internal static class ReportMarkdownRenderer
{
    internal const string CodeFont = "Courier New";
    internal const string CodeBlockFill = "EEF0F2";
    internal const string QuoteBorderColor = "D9DDE1";
    internal const string QuoteTextColor = "3D444C";
    internal const string LinkColor = "0E1013";
    internal const int QuoteIndent = 567;
    private const int Step = ReportListNumbering.IndentStep;

    /// <summary>
    /// Reemplaza cada párrafo cuyo texto (Trim) es exactamente <paramref name="placeholder"/> por
    /// los párrafos del Markdown. Cada párrafo creado va a <paramref name="resolved"/>: B-R6 no
    /// los escanea, así un "{caratula}" del perito queda literal.
    /// </summary>
    public static void ReplacePlaceholder(MainDocumentPart main, OpenXmlElement root, string placeholder,
        string markdown, HashSet<Paragraph> resolved, ReportListNumbering numbering)
    {
        foreach (var para in root.Descendants<Paragraph>().ToList())
        {
            if (OwnText(para).Trim() != placeholder) continue;

            var pPr = para.ParagraphProperties;
            var rPr = para.Descendants<Run>()
                .FirstOrDefault(r => r.Elements<Text>().Any(t => t.Text.Length > 0))?.RunProperties;

            var renderer = new Renderer(main, pPr, rPr, numbering);
            var paragraphs = renderer.Render(ReportMarkdown.Parse(markdown));

            while (paragraphs.Count > 0 && IsEmpty(paragraphs[0])) paragraphs.RemoveAt(0);
            while (paragraphs.Count > 0 && IsEmpty(paragraphs[^1])) paragraphs.RemoveAt(paragraphs.Count - 1);

            foreach (var p in paragraphs)
            {
                para.InsertBeforeSelf(p);
                resolved.Add(p);
            }
            para.Remove();

            if (paragraphs.Any(HasHardBreakInJustified)) EnsureDoNotExpandShiftReturn(main);
        }
    }

    private static bool HasHardBreakInJustified(Paragraph p) =>
        p.ParagraphProperties?.Justification?.Val?.Value == JustificationValues.Both &&
        p.Descendants<Break>().Any();

    /// <summary>
    /// Un salto de línea manual (Shift+Enter, "  \n" en el dialecto) en un párrafo justificado
    /// hace que Word y LibreOffice estiren la línea anterior a todo el ancho. La opción de
    /// compatibilidad <c>w:doNotExpandShiftReturn</c> evita eso; se agrega solo si hace falta (el
    /// texto plano nunca la necesita, así un caso viejo deja settings.xml como está).
    /// </summary>
    private static void EnsureDoNotExpandShiftReturn(MainDocumentPart main)
    {
        var settings = main.DocumentSettingsPart?.Settings;
        if (settings is null) return;
        var compat = settings.GetFirstChild<Compatibility>();
        if (compat is null)
        {
            compat = new Compatibility();
            settings.AddChild(compat);
        }
        compat.DoNotExpandShiftReturn ??= new DoNotExpandShiftReturn();
    }

    private static string OwnText(Paragraph para) => string.Concat(para.Descendants<Text>()
        .Where(t => t.Ancestors<Paragraph>().FirstOrDefault() == para).Select(t => t.Text));

    // Vacío = sin runs ni hipervínculos y sin numeración (un ítem vacío conserva su viñeta).
    private static bool IsEmpty(Paragraph p) =>
        !p.Elements<Run>().Any() && !p.Elements<Hyperlink>().Any() &&
        p.ParagraphProperties?.NumberingProperties is null;

    /// <summary>Contenedor de bloques: sangría izquierda, si está anidado, si es cita y la base de las listas.</summary>
    private readonly record struct Container(int Left, bool Nested, bool Quote, int ListBase, int Depth);

    /// <summary>Formato de los runs (el subrayado por &lt;u&gt; vive aparte, por profundidad).</summary>
    private readonly record struct Format(bool Bold, bool Italic, bool Code, bool Link, bool Quote);

    private sealed class Renderer(MainDocumentPart main, ParagraphProperties? basePPr, RunProperties? baseRPr,
        ReportListNumbering numbering)
    {
        private readonly List<Paragraph> output = [];
        private int underlineDepth;
        // Numeración pendiente del ítem de lista: la toma el primer párrafo que se crea adentro.
        private (int NumId, int Ilvl, int Left, bool Quote)? pendingNumbering;

        public List<Paragraph> Render(MarkdownDocument doc)
        {
            var baseLeft = ParseTwips(basePPr?.Indentation?.Left?.Value ?? basePPr?.Indentation?.Start?.Value);
            RenderBlocks(doc, new Container(baseLeft, Nested: false, Quote: false, ListBase: baseLeft, Depth: 0));
            FlushPendingNumbering();
            return output;
        }

        private static int ParseTwips(string? value) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

        private static string Twips(int value) => value.ToString(CultureInfo.InvariantCulture);

        // ── Bloques (§6.3.1) ─────────────────────────────────────────────────

        private void RenderBlocks(ContainerBlock container, Container c)
        {
            foreach (var block in container)
                RenderBlock(block, c);
        }

        private void RenderBlock(Block block, Container c)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    RenderHeading(heading, c);
                    break;
                case ParagraphBlock paragraph:
                    RenderParagraph(paragraph, c);
                    break;
                case ListBlock list:
                    RenderList(list, c);
                    break;
                case QuoteBlock quote:
                {
                    var left = c.Left + QuoteIndent;
                    RenderBlocks(quote, new Container(left, Nested: true, Quote: true, ListBase: left, c.Depth));
                    break;
                }
                case CodeBlock code: // FencedCodeBlock y cualquier otro CodeBlock
                    RenderCodeBlock(code, c);
                    break;
                case HtmlBlock html: // Defensa: la validación lo rechaza; sale literal.
                    RenderLiteralLines(html.Lines.Lines.Take(html.Lines.Count).Select(l => l.Slice.ToString()), c);
                    break;
                case ThematicBreakBlock or LinkReferenceDefinitionGroup or LinkReferenceDefinition:
                    break;
                case ContainerBlock other:
                    RenderBlocks(other, c);
                    break;
            }
        }

        private Paragraph NewParagraph(Container c)
        {
            var p = new Paragraph();
            var pPr = basePPr is null ? new ParagraphProperties() : (ParagraphProperties)basePPr.CloneNode(true);
            pPr.NumberingProperties = null;

            if (pendingNumbering is { } pending)
            {
                // Primer párrafo de un ítem: numeración + sangría francesa (pisa la de primera línea).
                pPr.NumberingProperties = new NumberingProperties(
                    new NumberingLevelReference { Val = pending.Ilvl },
                    new NumberingId { Val = pending.NumId });
                pPr.Indentation = new Indentation { Left = Twips(pending.Left), Hanging = Twips(Step) };
                pendingNumbering = null;
            }
            else if (c.Nested)
            {
                pPr.Indentation = new Indentation { Left = Twips(c.Left), FirstLine = "0" };
            }

            if (c.Quote)
                pPr.ParagraphBorders = new ParagraphBorders(new LeftBorder
                {
                    Val = BorderValues.Single, Size = 12, Space = 8, Color = QuoteBorderColor,
                });

            p.AppendChild(pPr);
            output.Add(p);
            return p;
        }

        // Párrafo sin numeración a la izquierda del contenedor (subtítulo, código).
        private static void FlushLeft(ParagraphProperties pPr, Container c)
        {
            if (pPr.NumberingProperties is null)
                pPr.Indentation = new Indentation { Left = Twips(c.Left), FirstLine = "0" };
            pPr.Justification = new Justification { Val = JustificationValues.Left };
        }

        private void RenderParagraph(ParagraphBlock block, Container c)
        {
            var p = NewParagraph(c);
            RenderInlines(block.Inline, p, new Format(false, false, false, false, c.Quote));
            // Solo espacios o U+00A0 (&nbsp;): párrafo vacío, como las líneas vacías de hoy.
            if (p.Elements<Hyperlink>().Any()) return;
            var text = string.Concat(p.Descendants<Text>().Select(t => t.Text));
            if (text.All(char.IsWhiteSpace))
                foreach (var run in p.Elements<Run>().ToList()) run.Remove();
        }

        private void RenderHeading(HeadingBlock block, Container c)
        {
            var p = NewParagraph(c);
            var pPr = p.ParagraphProperties!;
            FlushLeft(pPr, c);
            pPr.KeepNext = new KeepNext();
            var spacing = (SpacingBetweenLines?)pPr.SpacingBetweenLines?.CloneNode(true) ?? new SpacingBetweenLines();
            spacing.Before = "120";
            pPr.SpacingBetweenLines = spacing;
            RenderInlines(block.Inline, p, new Format(true, false, false, false, c.Quote));
        }

        private void RenderCodeBlock(CodeBlock block, Container c)
        {
            var lines = Enumerable.Range(0, block.Lines.Count)
                .Select(i => ReportMarkdown.RestoreNbsp(ReportValues.Clean(block.Lines.Lines[i].Slice.ToString())))
                .ToList();
            if (lines.Count == 0) return;

            var p = NewParagraph(c);
            var pPr = p.ParagraphProperties!;
            FlushLeft(pPr, c);
            pPr.SpacingBetweenLines = new SpacingBetweenLines
            {
                Before = "60", After = "60", Line = "240", LineRule = LineSpacingRuleValues.Auto,
            };
            pPr.Shading = new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = CodeBlockFill };

            var run = new Run();
            var rPr = BaseRunProperties();
            ApplyCodeFont(rPr, "18");
            if (c.Quote) rPr.Color = new Color { Val = QuoteTextColor };
            run.AppendChild(rPr);
            for (var i = 0; i < lines.Count; i++)
            {
                if (i > 0) run.AppendChild(new Break());
                if (lines[i].Length > 0)
                    run.AppendChild(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
            }
            p.AppendChild(run);
        }

        private void RenderLiteralLines(IEnumerable<string> rawLines, Container c)
        {
            var lines = rawLines.Select(l => ReportMarkdown.RestoreNbsp(ReportValues.Clean(l))).ToList();
            if (lines.Count == 0) return;
            var p = NewParagraph(c);
            var run = NewRun(new Format(false, false, false, false, c.Quote));
            for (var i = 0; i < lines.Count; i++)
            {
                if (i > 0) run.AppendChild(new Break());
                if (lines[i].Length > 0)
                    run.AppendChild(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
            }
            p.AppendChild(run);
        }

        // ── Listas (§6.3.3) ──────────────────────────────────────────────────

        private void RenderList(ListBlock list, Container c)
        {
            // Un ítem cuyo primer bloque es otra lista: el ítem de afuera conserva su viñeta en
            // un párrafo vacío propio.
            FlushPendingNumbering();

            var ilvl = Math.Min(c.Depth, 2);
            var start = 1;
            if (list.IsOrdered && int.TryParse(list.OrderedStart, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var s))
                start = s;
            var numId = numbering.Create(list.IsOrdered, start, ilvl);
            var itemLeft = c.ListBase + Step * (ilvl + 1);
            var inner = new Container(itemLeft, Nested: true, c.Quote, c.ListBase, c.Depth + 1);

            foreach (var item in list.OfType<ListItemBlock>())
            {
                FlushPendingNumbering();
                pendingNumbering = (numId, ilvl, itemLeft, c.Quote);
                RenderBlocks(item, inner);
                // Ítem vacío ("- "): igual sale su viñeta.
                FlushPendingNumbering();
            }
        }

        private void FlushPendingNumbering()
        {
            if (pendingNumbering is not { } pending) return;
            NewParagraph(new Container(pending.Left, Nested: true, pending.Quote, pending.Left, 0));
        }

        // ── Inlines (§6.3.2) ─────────────────────────────────────────────────

        private void RenderInlines(ContainerInline? container, OpenXmlElement target, Format f)
        {
            if (container is null) return;
            foreach (var inline in container)
                RenderInline(inline, target, f);
        }

        private void RenderInline(Inline inline, OpenXmlElement target, Format f)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    AppendText(target, literal.Content.ToString(), f);
                    break;
                case EmphasisInline emphasis:
                    RenderInlines(emphasis, target, emphasis.DelimiterCount >= 2
                        ? f with { Bold = true }
                        : f with { Italic = true });
                    break;
                case HtmlInline { Tag: "<u>" }:
                    underlineDepth++;
                    break;
                case HtmlInline { Tag: "</u>" }:
                    underlineDepth = Math.Max(0, underlineDepth - 1);
                    break;
                case HtmlInline html: // Defensa: la validación lo rechaza; sale literal.
                    AppendText(target, html.Tag, f);
                    break;
                case HtmlEntityInline entity:
                    AppendText(target, entity.Transcoded.ToString(), f);
                    break;
                case CodeInline code:
                    AppendText(target, code.Content, f with { Code = true });
                    break;
                case LineBreakInline lineBreak:
                    if (lineBreak.IsHard)
                    {
                        var run = NewRun(f);
                        run.AppendChild(new Break());
                        target.AppendChild(run);
                    }
                    else
                    {
                        AppendText(target, " ", f);
                    }
                    break;
                // editor-imagenes-informe: hoy una imagen (que la validación rechaza) sale como su
                // texto alternativo literal. La HU de imágenes reemplaza esta rama.
                case LinkInline { IsImage: true } image:
                    RenderInlines(image, target, f);
                    break;
                case LinkInline link:
                    RenderLink(link, target, f);
                    break;
                case AutolinkInline auto:
                    RenderAutolink(auto, target, f);
                    break;
                case ContainerInline other:
                    RenderInlines(other, target, f);
                    break;
            }
        }

        private void RenderLink(LinkInline link, OpenXmlElement target, Format f)
        {
            var url = link.Url;
            if (target is Hyperlink || !ReportMarkdown.IsAllowedUrl(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                RenderInlines(link, target, f);
                return;
            }

            var hyperlink = new Hyperlink { Id = main.AddHyperlinkRelationship(uri, true).Id, History = true };
            RenderInlines(link, hyperlink, f with { Link = true });
            target.AppendChild(hyperlink);

            var visible = VisibleUrl(url!);
            var text = string.Concat(hyperlink.Descendants<Text>().Select(t => t.Text));
            if (text != url && text != visible)
                AppendText(target, " (" + visible + ")", new Format(false, false, false, false, f.Quote), underline: false);
        }

        private void RenderAutolink(AutolinkInline auto, OpenXmlElement target, Format f)
        {
            var url = auto.IsEmail ? "mailto:" + auto.Url : auto.Url;
            if (target is Hyperlink || !ReportMarkdown.IsAllowedUrl(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                AppendText(target, auto.Url, f);
                return;
            }
            var hyperlink = new Hyperlink { Id = main.AddHyperlinkRelationship(uri, true).Id, History = true };
            AppendText(hyperlink, auto.Url, f with { Link = true });
            target.AppendChild(hyperlink);
        }

        private static string VisibleUrl(string url) =>
            url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? url["mailto:".Length..] : url;

        // ── Runs ─────────────────────────────────────────────────────────────

        private void AppendText(OpenXmlElement target, string text, Format f, bool? underline = null)
        {
            text = ReportMarkdown.RestoreNbsp(ReportValues.Clean(text));
            if (text.Length == 0) return;
            var run = NewRun(f, underline);
            run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            target.AppendChild(run);
        }

        private RunProperties BaseRunProperties() =>
            baseRPr is null ? new RunProperties() : (RunProperties)baseRPr.CloneNode(true);

        private Run NewRun(Format f, bool? underline = null)
        {
            var rPr = BaseRunProperties();
            if (f.Bold) rPr.Bold = new Bold();
            if (f.Italic) rPr.Italic = new Italic();
            if (f.Code) ApplyCodeFont(rPr, "20");
            if (f.Quote) rPr.Color = new Color { Val = QuoteTextColor };
            if (f.Link) rPr.Color = new Color { Val = LinkColor };
            if (underline ?? (f.Link || underlineDepth > 0))
                rPr.Underline = new Underline { Val = UnderlineValues.Single };
            var run = new Run();
            run.AppendChild(rPr);
            return run;
        }

        private static void ApplyCodeFont(RunProperties rPr, string halfPoints)
        {
            rPr.RunFonts = new RunFonts
            {
                Ascii = CodeFont, HighAnsi = CodeFont, EastAsia = CodeFont, ComplexScript = CodeFont,
            };
            rPr.FontSize = new FontSize { Val = halfPoints };
            rPr.FontSizeComplexScript = new FontSizeComplexScript { Val = halfPoints };
        }
    }
}
