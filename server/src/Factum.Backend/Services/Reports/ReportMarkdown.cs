using System.Text;
using System.Text.RegularExpressions;
using Factum.Backend.Models;
using Markdig;
using Markdig.Parsers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Factum.Backend.Services.Reports;

/// <summary>
/// Valores de <c>report_texts.formato</c> (editor-texto-enriquecido, SDD §3.1 y §4.1).
/// </summary>
public static class ReportTextFormats
{
    public const string Markdown = "markdown";
    public const string Texto = "texto";

    /// <summary>
    /// null / "" / "texto" → null (texto plano); "markdown" → "markdown"; cualquier otro valor →
    /// false (inválido: lo convierte en 400 <c>CaseValidation</c>).
    /// </summary>
    public static bool TryNormalize(string? value, out string? normalized)
    {
        normalized = null;
        switch (value)
        {
            case null or "" or Texto:
                return true;
            case Markdown:
                normalized = Markdown;
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// Dialecto Markdown de Factum para los textos del informe (SDD §4.3): CommonMark sin GFM, más
/// la etiqueta <c>&lt;u&gt;</c>. Parser Markdig 1.4.0 sin código indentado ni encabezados setext.
/// El HTML queda activo solo para reconocer <c>&lt;u&gt;</c> y rechazar el resto.
/// </summary>
public static class ReportMarkdown
{
    public const string ReasonHtml = "HTML";
    public const string ReasonImage = "imagen";
    public const string ReasonLink = "enlace";

    /// <summary>Construido una vez; <c>Markdown.Parse</c> es thread-safe con un pipeline compartido.</summary>
    internal static readonly MarkdownPipeline Pipeline = Build();

    private static MarkdownPipeline Build()
    {
        var builder = new MarkdownPipelineBuilder();
        builder.BlockParsers.TryRemove<IndentedCodeBlockParser>();
        builder.BlockParsers.Find<ParagraphBlockParser>()!.ParseSetexHeadings = false;
        return builder.Build();
    }

    /// <summary>
    /// Marca interna (uso privado de Unicode) con la que viajan por el parser los U+00A0 del
    /// principio de una línea (D10, sangría hecha a mano). Markdig recorta los U+00A0 iniciales
    /// de cada línea de un párrafo como si fueran espacios; con la marca no se pierden y,
    /// como no es espacio, tampoco abren un bloque. El renderer la vuelve a U+00A0
    /// (<see cref="RestoreNbsp"/>).
    /// </summary>
    internal const char LeadingNbspMarker = '\uE000';

    // Inicio de línea: espacios, marcadores de cita/lista y después la racha de U+00A0.
    private static readonly Regex LeadingNbspRegex = new(
        @"^((?:[ ]*(?:>[ ]?|[-+*][ ]+|\d{1,9}[.)][ ]+))*[ ]*)(\u00A0+)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// Parsea el Markdown ya sanitizado con <see cref="ReportValues.Clean"/>. Los U+00A0 del
    /// principio de línea pasan a <see cref="LeadingNbspMarker"/>.
    /// </summary>
    public static MarkdownDocument Parse(string? markdown)
    {
        var clean = ReportValues.Clean(markdown);
        if (clean.Contains('\u00A0'))
            clean = LeadingNbspRegex.Replace(clean,
                m => m.Groups[1].Value + new string(LeadingNbspMarker, m.Groups[2].Length));
        return Markdown.Parse(clean, Pipeline);
    }

    /// <summary>Vuelve <see cref="LeadingNbspMarker"/> a U+00A0 en un texto salido del AST.</summary>
    internal static string RestoreNbsp(string text) =>
        text.Contains(LeadingNbspMarker) ? text.Replace(LeadingNbspMarker, '\u00A0') : text;

    // ── Validación (§4.2, §6.2) ───────────────────────────────────────────────

    /// <summary>
    /// null = OK; si no, el motivo del primer contenido no permitido, en orden de documento:
    /// "HTML" (todo HTML salvo exactamente <c>&lt;u&gt;</c>/<c>&lt;/u&gt;</c>), "imagen" o
    /// "enlace" (esquema que no es http, https ni mailto). Lo que el dialecto no produce pero
    /// no es peligroso (otros niveles de encabezado, línea horizontal, "~~", "|") se acepta.
    /// </summary>
    public static string? Validate(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return null;
        foreach (var node in Parse(markdown).Descendants())
        {
            switch (node)
            {
                case HtmlBlock:
                    return ReasonHtml;
                case HtmlInline html when html.Tag is not ("<u>" or "</u>"):
                    return ReasonHtml;
                // editor-imagenes-informe: hoy las imágenes se rechazan; la HU de imágenes cambia
                // esta rama (y la del renderer).
                case LinkInline { IsImage: true }:
                    return ReasonImage;
                case LinkInline link when !IsAllowedUrl(link.Url):
                    return ReasonLink;
                case AutolinkInline auto when !auto.IsEmail && !IsAllowedUrl(auto.Url):
                    return ReasonLink;
            }
        }
        return null;
    }

    private static readonly Regex AllowedUrlRegex = new(
        @"^(https?://|mailto:)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>http://, https:// o mailto:, sin distinguir mayúsculas y sin espacios iniciales.</summary>
    internal static bool IsAllowedUrl(string? url) =>
        !string.IsNullOrEmpty(url) && AllowedUrlRegex.IsMatch(url);

    // ── Vacío (§4.5) ─────────────────────────────────────────────────────────

    /// <summary>
    /// Vacío si el texto visible no tiene ninguna letra ni dígito. Cuentan los literales, el
    /// código (en línea y en bloque), el texto de los enlaces y la URL de un autolink. No cuentan
    /// las marcas, los destinos de los enlaces, las entidades, <c>&lt;u&gt;</c> ni los números de
    /// las listas.
    /// </summary>
    public static bool IsBlank(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return true;
        foreach (var node in Parse(markdown).Descendants())
        {
            var visible = node switch
            {
                LiteralInline l => l.Content.ToString(),
                CodeInline c => c.Content,
                AutolinkInline a => a.Url,
                HtmlInline h when h.Tag is not ("<u>" or "</u>") => h.Tag,
                CodeBlock code => code.Lines.ToString(),
                HtmlBlock block => block.Lines.ToString(),
                _ => null,
            };
            if (visible is not null && visible.Any(char.IsLetterOrDigit)) return false;
        }
        return true;
    }

    // ── Texto plano → Markdown (§4.4) ────────────────────────────────────────

    // "Espacio" con el mismo conjunto que \s de JavaScript, para que el resultado sea byte a byte
    // el de plainToMarkdown/escapeLineStart del cliente (el \s de .NET difiere en U+0085/U+FEFF).
    private const string JsSpace = @"[\t\n\v\f\r \u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF]";
    private static readonly Regex OrderedMarkerStart = new(@"^\d{1,9}(?=[.)](" + JsSpace + "|$))", RegexOptions.CultureInvariant);
    private static readonly Regex BlockMarkerStart = new(
        @"^(#{1,6}(" + JsSpace + "|$)|[-+*](" + JsSpace + "|$)|=+" + JsSpace + "*$|-+" + JsSpace + "*$|`{3,}|~{3,})",
        RegexOptions.CultureInvariant);
    private static readonly Regex TrailingSpace = new(JsSpace + "+$", RegexOptions.CultureInvariant);
    private static readonly Regex LeadingSpace = new("^" + JsSpace + "+", RegexOptions.CultureInvariant);

    /// <summary>Pasos 4.1 y 4.2: entidades para &amp; &lt; &gt; y "\" antes de \ ` * _ [ ] ~.</summary>
    public static string EscapeInline(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '\\' or '`' or '*' or '_' or '[' or ']' or '~':
                    sb.Append('\\').Append(ch);
                    break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Paso 4.3 (sobre una línea ya escapada con <see cref="EscapeInline"/>): los espacios
    /// iniciales pasan a U+00A0 y, si la línea resultante empieza con algo que CommonMark
    /// tomaría como inicio de bloque (lista, encabezado, setext, fence), se escapa.
    /// </summary>
    internal static string EscapeLineStart(string line)
    {
        var spaces = 0;
        while (spaces < line.Length && line[spaces] == ' ') spaces++;
        if (spaces > 0) line = new string(' ', spaces) + line[spaces..];

        var ordered = OrderedMarkerStart.Match(line);
        if (ordered.Success) return line[..ordered.Length] + "\\" + line[ordered.Length..];
        if (BlockMarkerStart.IsMatch(line)) return "\\" + line;
        return line;
    }

    private static string EscapeContent(string text) => EscapeLineStart(EscapeInline(text));

    /// <summary>
    /// Conversión de un texto plano (caso anterior a esta HU) al dialecto, idéntica byte a byte a
    /// <c>plainToMarkdown</c> de <c>client/src/lib/report-markdown.ts</c> (fixtures F1-F17).
    /// </summary>
    public static string FromPlainText(string? text)
    {
        var lines = TrimmedLines(ReportValues.Clean(text), l => TrailingSpace.Replace(l, ""));
        var blocks = new List<string>();
        var list = new List<string>();

        void FlushList()
        {
            if (list.Count == 0) return;
            blocks.Add(string.Join("\n", list));
            list.Clear();
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                list.Add("- " + EscapeContent(LeadingSpace.Replace(line[2..], "")));
                continue;
            }
            FlushList();
            blocks.Add(line.Length == 0 ? "&nbsp;" : EscapeContent(line));
        }
        FlushList();
        return string.Join("\n\n", blocks);
    }

    private static readonly Regex BulletLine = new(@"^[-+*] ", RegexOptions.CultureInvariant);
    private static readonly Regex OrderedLine = new(@"^\d{1,9}[.)] ", RegexOptions.CultureInvariant);

    /// <summary>
    /// Textos por defecto (§6.5, D7): cada línea del template es un bloque. Las líneas
    /// "- "/"+ "/"* " son viñetas y "1. "/"1) " numeradas (los ítems consecutivos del mismo tipo
    /// forman una lista); una línea vacía del medio es "&amp;nbsp;"; el resto va tal cual (el
    /// Markdown que tenga se respeta: el texto lo controla el estudio).
    /// </summary>
    public static string FromLineTemplate(string? text)
    {
        var lines = TrimmedLines((text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n'), l => l.TrimEnd());
        var blocks = new List<string>();
        var list = new List<string>();
        var listKind = 0; // 0 = ninguna, 1 = viñetas, 2 = numerada

        void FlushList()
        {
            if (list.Count == 0) return;
            blocks.Add(string.Join("\n", list));
            list.Clear();
            listKind = 0;
        }

        foreach (var line in lines)
        {
            var kind = BulletLine.IsMatch(line) ? 1 : OrderedLine.IsMatch(line) ? 2 : 0;
            if (kind != 0)
            {
                if (kind != listKind) FlushList();
                listKind = kind;
                list.Add(line);
                continue;
            }
            FlushList();
            blocks.Add(line.Length == 0 ? "&nbsp;" : line);
        }
        FlushList();
        return string.Join("\n\n", blocks);
    }

    // Parte por \n, recorta el final de cada línea y descarta las vacías de los extremos.
    private static List<string> TrimmedLines(string text, Func<string, string> trimEnd)
    {
        var lines = text.Split('\n').Select(trimEnd).ToList();
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }
}

/// <summary>Reglas de los textos del informe que dependen del formato del caso.</summary>
public static class ReportTextRules
{
    /// <summary>Vacío según el formato del caso (§4.5). <paramref name="t"/> null → texto plano.</summary>
    public static bool IsBlank(ReportTexts? t, string? value) =>
        t?.Formato == ReportTextFormats.Markdown
            ? ReportMarkdown.IsBlank(value)
            : string.IsNullOrWhiteSpace(value);
}
