using System.Text;
using Factum.Backend.Services.Reports;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Factum.Backend.Tests;

/// <summary>Dialecto Markdown de Factum (Refactorizaciones/editor-texto-enriquecido.md §4, §6.2; T1-T7).</summary>
public sealed class ReportMarkdownTests
{
    // ── T1. Fixtures F1-F17 (§4.4), byte a byte ─────────────────────────────

    public static readonly TheoryData<string, string, string> Fixtures = new()
    {
        { "F1", "archivo_de_prueba_1.txt", "archivo\\_de\\_prueba\\_1.txt" },
        { "F2", "2 * 3 = 6", "2 \\* 3 = 6" },
        { "F3", "Expte. #123", "Expte. #123" },
        { "F4", "# Título", "\\# Título" },
        { "F5", "1. Primero", "1\\. Primero" },
        { "F6", "Línea A\nLínea B", "Línea A\n\nLínea B" },
        { "F7", "A\n\nB", "A\n\n&nbsp;\n\nB" },
        { "F8", "Intro:\n- uno\n- dos\nCierre", "Intro:\n\n- uno\n- dos\n\nCierre" },
        { "F9", "{caratula} <b>x</b> & [y]", "{caratula} &lt;b&gt;x&lt;/b&gt; &amp; \\[y\\]" },
        { "F10", "   sangría", "\u00A0\u00A0\u00A0sangría" },
        { "F11", "> cita", "&gt; cita" },
        { "F12", "- 2. item", "- 2\\. item" },
        { "F13", "\n\nA\n\n", "A" },
        { "F14", "`cmd`", "\\`cmd\\`" },
        { "F15", "+ más\n---", "\\+ más\n\n\\---" },
        { "F16", "C:\\ruta", "C:\\\\ruta" },
        { "F17", "", "" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void T1_FromPlainText_Fixtures(string id, string input, string expected)
    {
        Assert.True(expected == ReportMarkdown.FromPlainText(input), id);
        Assert.Equal(expected, ReportMarkdown.FromPlainText(input));
    }

    // ── T2. La conversión no pierde ni cambia ninguna palabra ───────────────

    private static string Visible(ContainerInline? container)
    {
        var sb = new StringBuilder();
        if (container is null) return "";
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline l: sb.Append(l.Content.ToString()); break;
                case HtmlEntityInline e: sb.Append(e.Transcoded.ToString()); break;
                case CodeInline c: sb.Append(c.Content); break;
                case LineBreakInline: sb.Append('\n'); break;
                case ContainerInline ci: sb.Append(Visible(ci)); break;
            }
        }
        return sb.ToString();
    }

    private static string Norm(string s) => s.Replace('\u00A0', ' ').Replace(ReportMarkdown.LeadingNbspMarker, ' ').TrimEnd();

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void T2_FromPlainText_ConservaElTexto(string id, string input, string _)
    {
        var md = ReportMarkdown.FromPlainText(input);
        Assert.Null(ReportMarkdown.Validate(md));

        var expected = input.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).ToList();
        while (expected.Count > 0 && expected[0].Length == 0) expected.RemoveAt(0);
        while (expected.Count > 0 && expected[^1].Length == 0) expected.RemoveAt(expected.Count - 1);
        expected = expected.Select(l => l.StartsWith("- ", StringComparison.Ordinal) ? l[2..].TrimStart() : l).ToList();

        var actual = ReportMarkdown.Parse(md).Descendants<ParagraphBlock>()
            .Select(p => Norm(Visible(p.Inline)))
            .ToList();
        Assert.True(expected.SequenceEqual(actual),
            $"{id}: esperado [{string.Join("|", expected)}], obtenido [{string.Join("|", actual)}]");
    }

    // ── T3. Validate acepta el dialecto ──────────────────────────────────────

    [Theory]
    [InlineData("Un párrafo.\n\nOtro párrafo.")]
    [InlineData("&nbsp;")]
    [InlineData("Línea uno  \nLínea dos")]
    [InlineData("**negrita** y *cursiva*")]
    [InlineData("<u>subrayado</u>")]
    [InlineData("<u>a **b**</u>")]
    [InlineData("`código` en línea")]
    [InlineData("```\n<div>x</div>\n```")]
    [InlineData("> una cita\n> de dos líneas")]
    [InlineData("### Subtítulo")]
    [InlineData("- uno\n  - dos\n    - tres")]
    [InlineData("3. tercero\n   - y")]
    [InlineData("[sitio](https://ejemplo.com/a%20b)")]
    [InlineData("[correo](mailto:perito@ejemplo.com)")]
    [InlineData("<https://ejemplo.com>")]
    [InlineData("<perito@ejemplo.com>")]
    [InlineData("archivo\\_de\\_prueba \\* &lt;b&gt;")]
    [InlineData("# Otro nivel\n\n---\n\n~~tachado~~ | a | b |")]
    public void T3_Validate_AceptaElDialecto(string md) => Assert.Null(ReportMarkdown.Validate(md));

    // ── T4. Validate rechaza ─────────────────────────────────────────────────

    [Theory]
    [InlineData("<script>alert(1)</script>", ReportMarkdown.ReasonHtml)]
    [InlineData("<img src=x onerror=alert(1)>", ReportMarkdown.ReasonHtml)]
    [InlineData("texto <img src=x onerror=alert(1)> en línea", ReportMarkdown.ReasonHtml)]
    [InlineData("<div>x</div>", ReportMarkdown.ReasonHtml)]
    [InlineData("<u class=\"x\">a</u>", ReportMarkdown.ReasonHtml)]
    [InlineData("[a](javascript:alert(1))", ReportMarkdown.ReasonLink)]
    [InlineData("[a](data:text/html,x)", ReportMarkdown.ReasonLink)]
    [InlineData("<javascript:alert(1)>", ReportMarkdown.ReasonLink)]
    [InlineData("[a]\n\n[a]: javascript:alert(1)", ReportMarkdown.ReasonLink)]
    [InlineData("![a](https://x/a.png)", ReportMarkdown.ReasonImage)]
    public void T4_Validate_Rechaza(string md, string reason) => Assert.Equal(reason, ReportMarkdown.Validate(md));

    // ── T5. IsBlank ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("&nbsp;")]
    [InlineData("&nbsp;\n\n&nbsp;")]
    [InlineData("- ")]
    [InlineData("**  **")]
    [InlineData("<u></u>")]
    [InlineData("1. ")]
    [InlineData("> ")]
    [InlineData("\\*")]
    [InlineData("…")]
    [InlineData("[](https://ejemplo.com)")]
    public void T5_IsBlank_Vacios(string? md) => Assert.True(ReportMarkdown.IsBlank(md));

    [Theory]
    [InlineData("a")]
    [InlineData("`x`")]
    [InlineData("1\\. x")]
    [InlineData("<https://x.com>")]
    [InlineData("```\n7\n```")]
    [InlineData("[texto](https://ejemplo.com)")]
    public void T5_IsBlank_NoVacios(string md) => Assert.False(ReportMarkdown.IsBlank(md));

    // ── T6. EscapeInline ─────────────────────────────────────────────────────

    [Fact]
    public void T6_EscapeInline() =>
        Assert.Equal("Buenos\\_Aires \\*x\\* &lt;b&gt; &amp; \\[y\\]",
            ReportMarkdown.EscapeInline("Buenos_Aires *x* <b> & [y]"));

    // ── T7. FromLineTemplate ─────────────────────────────────────────────────

    [Fact]
    public void T7_FromLineTemplate_ListaYParrafos() =>
        Assert.Equal("Intro:\n\n- a\n- b\n\nCierre", ReportMarkdown.FromLineTemplate("Intro:\n- a\n- b\nCierre"));

    [Fact]
    public void T7_FromLineTemplate_LineaVaciaDelMedio() =>
        Assert.Equal("A\n\n&nbsp;\n\nB", ReportMarkdown.FromLineTemplate("\nA\n\nB\n"));

    [Fact]
    public void T7_FromLineTemplate_RespetaElMarkdown() =>
        Assert.Equal("Texto con **x**\n\n1. uno\n2. dos\n\n- viñeta",
            ReportMarkdown.FromLineTemplate("Texto con **x**\r\n1. uno\n2. dos\n- viñeta"));

    // ── Formato ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, true, null)]
    [InlineData("", true, null)]
    [InlineData("texto", true, null)]
    [InlineData("markdown", true, "markdown")]
    [InlineData("html", false, null)]
    [InlineData("Markdown", false, null)]
    public void Formato_TryNormalize(string? value, bool ok, string? normalized)
    {
        Assert.Equal(ok, ReportTextFormats.TryNormalize(value, out var n));
        Assert.Equal(normalized, n);
    }
}
