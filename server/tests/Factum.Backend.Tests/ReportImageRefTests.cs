using Factum.Backend.Services.Reports;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Factum.Backend.Tests;

/// <summary>
/// Referencia <c>captura:</c> (editor-imagenes-informe §4.1, §6.2, T1-T3). Fixtures I1-I5 del
/// contrato compartido: el cliente produce exactamente los mismos strings.
/// </summary>
public sealed class ReportImageRefTests
{
    // (archivo, alt, Markdown) — I1..I5 del Contrato compartido §4.1.
    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "screenshot_20261001_101530.png", "Chat con Juan", "![Chat con Juan](captura:screenshot_20261001_101530.png)" },
        { "screenshot_20261001_101530.png", "", "![](captura:screenshot_20261001_101530.png)" },
        { "Captura de pantalla (1).png", "Chat", "![Chat](captura:Captura%20de%20pantalla%20%281%29.png)" },
        { "screenshot_1.png", "a_b *c* [d] <e> & f", "![a\\_b \\*c\\* \\[d\\] &lt;e&gt; &amp; f](captura:screenshot_1.png)" },
        { "captura_ñ.jpg", "x", "![x](captura:captura_%C3%B1.jpg)" },
    };

    // ── T1 ───────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void T1_Format_ProduceLasFixtures_YTryParseDevuelveElArchivo(string file, string alt, string md)
    {
        Assert.Equal(md, ReportImageRef.Format(file, alt));
        var url = md[(md.IndexOf("](", StringComparison.Ordinal) + 2)..^1];
        Assert.Equal("captura:" + ReportImageRef.EncodeName(file), url);
        Assert.True(ReportImageRef.TryParse(url, out var parsed));
        Assert.Equal(file, parsed);
    }

    [Fact]
    public void T1_TryParse_AceptaHexEnMinusculas() =>
        Assert.True(ReportImageRef.TryParse("captura:captura_%c3%b1.jpg", out var f) && f == "captura_ñ.jpg");

    // ── T2 ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://sitio/imagen.png")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("file:///etc/passwd")]
    [InlineData("CAPTURA:screenshot_1.png")]
    [InlineData("captura:..%2Fotro%2Fa.png")]
    [InlineData("captura:%2E%2E")]
    [InlineData("captura:")]
    [InlineData("captura:a/b.png")]
    [InlineData("captura:%FF.png")]
    [InlineData("captura:%C3.png")]
    [InlineData("captura:%2.png")]
    [InlineData("captura:foto_funcionario_20261001_100000.jpg")]
    [InlineData("captura:video_screenshot.mp4")]
    [InlineData("captura:informe_pericial_x.docx")]
    [InlineData("captura:screenshot_1.png%00")]
    [InlineData("captura:a%5Cb_captura.png")]
    [InlineData(" captura:screenshot_1.png")]
    public void T2_TryParse_Rechaza(string? url)
    {
        Assert.False(ReportImageRef.TryParse(url, out var f));
        Assert.Equal("", f);
    }

    [Theory]
    [InlineData("foto_funcionario_20261001_100000.jpg")]
    [InlineData("foto_denunciante_20261001_100000.jpg")]
    [InlineData("adjunto_1.png")]
    [InlineData("screenshot.mp4")]
    [InlineData("informe_pericial_x.docx")]
    [InlineData("evidencia_x.zip")]
    [InlineData("a\\b_captura.png")]
    [InlineData("a/b_captura.png")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("screenshot_\u0001.png")]
    [InlineData("screenshot_\u007F.png")]
    [InlineData("screenshot.gif")]
    public void T2_IsInsertableName_Rechaza(string? name) => Assert.False(ReportImageRef.IsInsertableName(name));

    [Fact]
    public void T2_IsInsertableName_Rechaza256Caracteres()
    {
        var name = "screenshot_" + new string('a', 256 - "screenshot_".Length - 4) + ".png";
        Assert.Equal(256, name.Length);
        Assert.False(ReportImageRef.IsInsertableName(name));
        Assert.True(ReportImageRef.IsInsertableName(name[..^5] + ".png")); // 255: vale
    }

    [Theory]
    [InlineData("screenshot_20261001_101530.png")]
    [InlineData("Captura de pantalla (1).png")]
    [InlineData("captura_ñ.jpg")]
    [InlineData("SCREENSHOT_1.JPEG")]
    public void T2_IsInsertableName_Acepta(string name) => Assert.True(ReportImageRef.IsInsertableName(name));

    // ── T3. Markdig deja el destino sin decodificar ──────────────────────────

    [Fact]
    public void T3_Markdig_ConservaElPercentEncodingEnUrl()
    {
        var doc = ReportMarkdown.Parse("![Chat](captura:Captura%20de%20pantalla%20%281%29.png)");
        var link = doc.Descendants<LinkInline>().Single();
        Assert.True(link.IsImage);
        Assert.Equal("captura:Captura%20de%20pantalla%20%281%29.png", link.Url);
        Assert.True(ReportImageRef.TryParse(link.Url, out var f));
        Assert.Equal("Captura de pantalla (1).png", f);
    }
}
