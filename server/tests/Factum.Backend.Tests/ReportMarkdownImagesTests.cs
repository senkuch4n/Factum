using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;
using Factum.Backend.Services.Reports;

namespace Factum.Backend.Tests;

/// <summary>
/// Imágenes en el dialecto Markdown (editor-imagenes-informe §4.1, §4.3, §4.6, §4.7, T4-T9).
/// Sin E/S: la disponibilidad entra por un delegado.
/// </summary>
public sealed class ReportMarkdownImagesTests
{
    private const string I1 = "![Chat con Juan](captura:screenshot_20261001_101530.png)";
    private const string I2 = "![](captura:screenshot_20261001_101530.png)";
    private const string I3 = "![Chat](captura:Captura%20de%20pantalla%20%281%29.png)";
    private const string I4 = "![a\\_b \\*c\\* \\[d\\] &lt;e&gt; &amp; f](captura:screenshot_1.png)";
    private const string I5 = "![x](captura:captura_%C3%B1.jpg)";
    private const string I6 = "Se observa la conversación:\n\n" + I1 + "\n\nFin.";

    private static string Images(int n) =>
        string.Join("\n\n", Enumerable.Range(1, n).Select(i => $"![Figura {i}](captura:screenshot_{i}.png)"));

    // ── T4. Validate acepta ──────────────────────────────────────────────────

    public static TheoryData<string> Accepted => new()
    {
        I1, I2, I3, I4, I5, I6,
        Images(20),
        // Mezcla de I6 con lo de la base: listas, cita, código, negrita, subrayado y enlace.
        "## Detalle\n\n" + I6 + "\n\n- uno\n- **dos**\n\n1. a\n2. b\n\n> cita <u>subrayada</u>\n\n```\ncódigo ![x](https://x/a.png)\n```\n\n"
            + I3 + "\n\n[sitio](https://ejemplo.com) y `![x](file:///etc/passwd)`",
        I1 + "\n",
        "\n\n" + I1 + "\n\n",
        I1 + "   ",
        "Texto\n   \n" + I1 + "\n  \nFin.", // líneas con solo espacios = vacías (como trim() del cliente)
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void T4_Validate_Acepta(string md) => Assert.Null(ReportMarkdown.Validate(md));

    // ── T5. Validate rechaza ─────────────────────────────────────────────────

    public static TheoryData<string> Rejected => new()
    {
        "![x](https://sitio/imagen.png)",
        "![x](data:image/png;base64,AAAA)",
        "![x](file:///etc/passwd)",
        "![x](CAPTURA:screenshot_1.png)",
        "![x](captura:..%2Fotro%2Fa.png)",
        "![x](captura:%2E%2E)",
        "![x](captura:)",
        "![x](captura:a/b.png)",
        "![x](captura:%FF.png)",
        "![x](captura:foto_funcionario_20261001_100000.jpg)",
        "![x](captura:video_screenshot.mp4)",
        "![x](captura:informe_pericial_x.docx)",
        "Texto ![x](captura:screenshot_1.png)",
        "- ![x](captura:screenshot_1.png)",
        "> ![x](captura:screenshot_1.png)",
        "![x](captura:screenshot_1.png \"t\")",
        "![**x**](captura:screenshot_1.png)",
        "![" + new string('a', 201) + "](captura:screenshot_1.png)",
        // Más casos de posición y forma (el escáner del cliente tampoco los toma como imagen).
        "Texto\n![x](captura:screenshot_1.png)",
        "![x](captura:screenshot_1.png)\nTexto",
        "![x](captura:screenshot_1.png)\n- uno",
        "# ![x](captura:screenshot_1.png)",
        "  ![x](captura:screenshot_1.png)",
        "![x](<captura:screenshot_1.png>)",
        "![x]( captura:screenshot_1.png )",
        "![x][r]\n\n[r]: captura:screenshot_1.png",
        "![x](captura:screenshot_1.png)![y](captura:screenshot_2.png)",
        "![a <u>b</u>](captura:screenshot_1.png)",
        "![`x`](captura:screenshot_1.png)",
        "![[a](https://x)](captura:screenshot_1.png)",
    };

    [Theory]
    [MemberData(nameof(Rejected))]
    public void T5_Validate_RechazaConImagen(string md) => Assert.Equal(ReportMarkdown.ReasonImage, ReportMarkdown.Validate(md));

    [Fact]
    public void T5_Validate_21Imagenes_TooManyImages() =>
        Assert.Equal(ReportMarkdown.TooManyImages, ReportMarkdown.Validate(Images(21)));

    [Fact]
    public void T5_Validate_AltDe200_Acepta_ConEspaciosColapsados()
    {
        Assert.Null(ReportMarkdown.Validate("![" + new string('a', 200) + "](captura:screenshot_1.png)"));
        // 200 letras + espacios de los bordes y repetidos: se mide después de trim y colapso.
        Assert.Null(ReportMarkdown.Validate("![  " + new string('a', 100) + "    " + new string('a', 99) + "  ](captura:screenshot_1.png)"));
    }

    [Fact]
    public void T5_Validate_ElPrimerErrorCorta() =>
        Assert.Equal(ReportMarkdown.ReasonHtml, ReportMarkdown.Validate("<div>x</div>\n\n" + Images(21)));

    // ── T6. Vacío ────────────────────────────────────────────────────────────

    [Fact]
    public void T6_IsBlank()
    {
        Assert.True(ReportMarkdown.IsBlank(I1));
        Assert.False(ReportMarkdown.IsBlank(I6));
        Assert.True(ReportMarkdown.IsBlank("&nbsp;\n\n" + I1));
        Assert.True(ReportMarkdown.IsBlank(I4));
        // Una imagen inválida tampoco aporta texto (la validación igual la rechaza).
        Assert.True(ReportMarkdown.IsBlank("![texto](https://x/a.png)"));
        // Texto plano: hay letras.
        Assert.False(ReportTextRules.IsBlank(new ReportTexts { Formato = null }, I1));
        Assert.True(ReportTextRules.IsBlank(new ReportTexts { Formato = ReportTextFormats.Markdown }, I1));
    }

    [Fact]
    public void T6_HasBlockContent()
    {
        var md = new ReportTexts { Formato = ReportTextFormats.Markdown };
        Assert.True(ReportTextRules.HasBlockContent(md, I1));
        Assert.True(ReportTextRules.HasBlockContent(md, "Texto"));
        Assert.False(ReportTextRules.HasBlockContent(md, "&nbsp;"));
        Assert.False(ReportTextRules.HasBlockContent(md, "![x](https://x/a.png)"));
        Assert.False(ReportTextRules.HasBlockContent(md, ""));
        Assert.True(ReportTextRules.HasBlockContent(new ReportTexts(), I1)); // texto plano: hay letras
        Assert.False(ReportTextRules.HasBlockContent(new ReportTexts(), "  "));
        Assert.False(ReportTextRules.HasBlockContent(null, null));
    }

    // ── T7. ExtractImages ────────────────────────────────────────────────────

    [Fact]
    public void T7_ExtractImages()
    {
        Assert.Equal([("screenshot_20261001_101530.png", "Chat con Juan")], ReportMarkdown.ExtractImages(I6));
        Assert.Equal([("screenshot_1.png", "a_b *c* [d] <e> & f")], ReportMarkdown.ExtractImages(I4));
        Assert.Equal([("screenshot_20261001_101530.png", "")], ReportMarkdown.ExtractImages(I2));
        Assert.Equal([("captura_ñ.jpg", "x")], ReportMarkdown.ExtractImages(I5));
        Assert.Empty(ReportMarkdown.ExtractImages("```\n" + I1 + "\n```"));
        Assert.Empty(ReportMarkdown.ExtractImages("~~~~\n\n" + I1 + "\n\n~~~~"));
        Assert.Empty(ReportMarkdown.ExtractImages("- " + I1));
        Assert.Empty(ReportMarkdown.ExtractImages(null));
        Assert.Equal(20, ReportMarkdown.ExtractImages(Images(20)).Count);
        Assert.Equal(
            ["screenshot_1.png", "screenshot_2.png", "screenshot_3.png"],
            ReportMarkdown.ExtractImages(Images(3)).Select(i => i.Filename).ToArray());
    }

    // ── T8. ValidateReportTexts ──────────────────────────────────────────────

    [Fact]
    public void T8_ValidateReportTexts()
    {
        Assert.Equal("El campo resultados tiene contenido no permitido: imagen",
            CaseValidation.ValidateReportTexts(new ReportTextsDto(Resultados: "![x](file:///etc/passwd)", Formato: "markdown")));
        Assert.Equal("El campo resultados supera las 20 imágenes",
            CaseValidation.ValidateReportTexts(new ReportTextsDto(Resultados: Images(21), Formato: "markdown")));
        Assert.Null(CaseValidation.ValidateReportTexts(new ReportTextsDto(Resultados: "![x](file:///etc/passwd)")));
        Assert.Null(CaseValidation.ValidateReportTexts(new ReportTextsDto(Resultados: Images(21))));
        Assert.Null(CaseValidation.ValidateReportTexts(new ReportTextsDto(
            ObjetoInforme: I1, OperacionesRealizadas: I2, AseguramientoEvidencia: I3, Resultados: I6,
            ValoracionTecnica: I4, Conclusiones: I5, NotasTecnicas: Images(20), Reserva: I1, Formato: "markdown")));
    }

    // ── T9. BrokenImageKeys ──────────────────────────────────────────────────

    [Fact]
    public void T9_BrokenImageKeys()
    {
        var t = new ReportTexts
        {
            Formato = ReportTextFormats.Markdown,
            ObjetoInforme = "![ok](captura:screenshot_ok.png)",
            Resultados = "Texto\n\n![a](captura:screenshot_ok.png)\n\n![b](captura:screenshot_roto.png)\n\n![c](captura:screenshot_roto2.png)",
            Reserva = "![r](captura:screenshot_roto.png)",
            Conclusiones = "![x](https://x/a.png)", // inválida: no cuenta (la validación ya la rechazó)
        };
        var asked = new List<string>();
        bool Available(string n)
        {
            asked.Add(n);
            return n == "screenshot_ok.png";
        }

        Assert.Equal(["report_texts.resultados.imagen", "report_texts.reserva.imagen"],
            CaseValidation.BrokenImageKeys(t, Available));
        Assert.Equal(asked.Distinct().Count(), asked.Count); // cada archivo se mira una sola vez

        t.Formato = null;
        Assert.Empty(CaseValidation.BrokenImageKeys(t, _ => false));
        Assert.Empty(CaseValidation.BrokenImageKeys(null, _ => false));
        Assert.Equal("report_texts.notas_tecnicas.imagen", CaseValidation.ReportImageKey("notas_tecnicas"));
    }

    [Fact]
    public void T9_BrokenImageKeys_LasOchoSecciones_EnOrden()
    {
        const string broken = "![x](captura:screenshot_no_existe.png)";
        var t = new ReportTexts
        {
            Formato = ReportTextFormats.Markdown,
            ObjetoInforme = broken, OperacionesRealizadas = broken, AseguramientoEvidencia = broken,
            Resultados = broken, ValoracionTecnica = broken, Conclusiones = broken, NotasTecnicas = broken,
            Reserva = broken,
        };
        Assert.Equal(
        [
            "report_texts.objeto_informe.imagen", "report_texts.operaciones_realizadas.imagen",
            "report_texts.aseguramiento_evidencia.imagen", "report_texts.resultados.imagen",
            "report_texts.valoracion_tecnica.imagen", "report_texts.conclusiones.imagen",
            "report_texts.notas_tecnicas.imagen", "report_texts.reserva.imagen",
        ], CaseValidation.BrokenImageKeys(t, _ => false));
    }
}
