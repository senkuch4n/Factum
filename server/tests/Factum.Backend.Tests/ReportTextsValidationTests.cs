using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;
using Factum.Backend.Services.Reports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Tests;

/// <summary>Validación, faltantes y textos por defecto en Markdown (editor-texto-enriquecido T8-T10).</summary>
public sealed class ReportTextsValidationTests
{
    // ── T8. ValidateReportTexts ──────────────────────────────────────────────

    [Fact]
    public void T8_FormatoInvalido_Error() =>
        Assert.Equal("El campo formato no es válido",
            CaseValidation.ValidateReportTexts(new ReportTextsDto(Resultados: "x", Formato: "html")));

    [Theory]
    [InlineData("texto")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("markdown")]
    public void T8_FormatosValidos_Ok(string? formato) =>
        Assert.Null(CaseValidation.ValidateReportTexts(new ReportTextsDto(Resultados: "**x**", Formato: formato)));

    [Fact]
    public void T8_MarkdownConScript_Error() =>
        Assert.Equal("El campo resultados tiene contenido no permitido: HTML",
            CaseValidation.ValidateReportTexts(new ReportTextsDto(
                ObjetoInforme: "Objeto.", Resultados: "<script>alert(1)</script>", Formato: "markdown")));

    [Fact]
    public void T8_MarkdownPrimerErrorCorta_EnElOrdenDeLosCampos() =>
        Assert.Equal("El campo objeto_informe tiene contenido no permitido: enlace",
            CaseValidation.ValidateReportTexts(new ReportTextsDto(
                ObjetoInforme: "[a](javascript:alert(1))", Reserva: "![a](https://x/a.png)", Formato: "markdown")));

    [Fact]
    public void T8_MarkdownImagen_Error() =>
        Assert.Equal("El campo reserva tiene contenido no permitido: imagen",
            CaseValidation.ValidateReportTexts(new ReportTextsDto(Reserva: "![a](https://x/a.png)", Formato: "markdown")));

    [Fact]
    public void T8_TextoPlanoConScript_Ok() =>
        Assert.Null(CaseValidation.ValidateReportTexts(new ReportTextsDto(Resultados: "<script>alert(1)</script>")));

    [Fact]
    public void T8_Largo_SinCambios()
    {
        var largo = new string('a', CaseValidation.MaxText + 1);
        Assert.Equal("El campo conclusiones supera los 20000 caracteres",
            CaseValidation.ValidateReportTexts(new ReportTextsDto(Conclusiones: largo, Formato: "markdown")));
        Assert.Null(CaseValidation.ValidateReportTexts(new ReportTextsDto(
            Conclusiones: new string('a', CaseValidation.MaxText), Formato: "markdown")));
    }

    // ── T9. ValidateForGenerate ──────────────────────────────────────────────

    private static Case CaseWith(string? formato, string conclusiones)
    {
        var cas = ReportTestSupport.MakeCase();
        cas.ReportTexts = new ReportTexts
        {
            OperacionesRealizadas = "Operaciones.", AseguramientoEvidencia = "Aseguramiento.",
            Resultados = "Resultados.", ValoracionTecnica = "Valoración.", Conclusiones = conclusiones,
            Formato = formato,
        };
        return cas;
    }

    [Fact]
    public void T9_MarkdownViñetaVacia_Falta()
    {
        var missing = CaseValidation.ValidateForGenerate(CaseWith("markdown", "- "), hasImeiCapture: true);
        Assert.Contains("report_texts.conclusiones", missing);
        Assert.DoesNotContain("report_texts.resultados", missing);
    }

    [Theory]
    [InlineData("…")]
    [InlineData("&nbsp;")]
    [InlineData("**  **")]
    public void T9_MarkdownSoloSignos_Falta(string conclusiones) =>
        Assert.Contains("report_texts.conclusiones",
            CaseValidation.ValidateForGenerate(CaseWith("markdown", conclusiones), hasImeiCapture: true));

    [Fact]
    public void T9_TextoPlanoGuion_NoFalta() =>
        Assert.DoesNotContain("report_texts.conclusiones",
            CaseValidation.ValidateForGenerate(CaseWith(null, "-"), hasImeiCapture: true));

    // ── T10. RenderDefaults ──────────────────────────────────────────────────

    private static ReportSettings Settings(ReportDefaultTextsOptions? texts = null, ILogger<ReportSettings>? logger = null) =>
        new(Options.Create(new ReportOptions
        {
            TimeZone = "America/Argentina/Buenos_Aires",
            EncryptZip = true,
            DefaultTexts = texts ?? new ReportDefaultTextsOptions(),
        }), logger ?? NullLogger<ReportSettings>.Instance);

    private static List<FileInfoDto> Files(params string[] names) =>
        names.Select(n => new FileInfoDto(n, 1, "", DateTime.UtcNow)).ToList();

    [Fact]
    public void T10_RenderDefaults_MarkdownConListaReal()
    {
        var cas = ReportTestSupport.MakeCase();
        cas.Device.Imei = "INGRESAR_MANUALMENTE";
        var d = ReportValues.RenderDefaults(cas, Settings(),
            Files("captura_1.png", "captura_2.png", "grabacion.mp4", "notas.txt"));

        Assert.Equal(ReportTextFormats.Markdown, d.Formato);
        Assert.Contains(":\n\n- Capturas de pantalla: 2.\n- Grabaciones de pantalla: 1.\n", d.OperacionesRealizadas);
        Assert.Contains("- Otros archivos incorporados (fotografías y adjuntos): 1.\n\nLas operaciones se limitaron",
            d.OperacionesRealizadas);
        Assert.Contains("IMEI INGRESAR\\_MANUALMENTE", d.OperacionesRealizadas);
        Assert.Contains("America/Argentina/Buenos\\_Aires", d.NotasTecnicas);
        Assert.Contains("\n\n", d.NotasTecnicas); // cada línea del template es un párrafo
        Assert.Equal("", d.ObjetoInforme);
        Assert.Equal("", d.Resultados);

        foreach (var text in new[] { d.OperacionesRealizadas, d.AseguramientoEvidencia, d.NotasTecnicas, d.Reserva })
            Assert.Null(ReportMarkdown.Validate(text));

        // El texto visible no cambia: los defaults siguen siendo los de siempre.
        var md = ReportMarkdown.Parse(d.OperacionesRealizadas);
        Assert.Single(md.OfType<Markdig.Syntax.ListBlock>());
    }

    [Fact]
    public void T10_RenderDefaults_SinGrabaciones_OmiteLaLinea()
    {
        var d = ReportValues.RenderDefaults(ReportTestSupport.MakeCase(), Settings(), Files("captura_1.png"));
        Assert.DoesNotContain("Grabaciones de pantalla", d.OperacionesRealizadas);
        Assert.Contains("- Capturas de pantalla: 1.", d.OperacionesRealizadas);
    }

    // ── B13. Default configurado inválido → warning y default versionado ────

    private sealed class ListLogger : ILogger<ReportSettings>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    [Fact]
    public void DefaultConfiguradoInvalido_CaeAlVersionado()
    {
        var logger = new ListLogger();
        var s = Settings(new ReportDefaultTextsOptions
        {
            Reserva = "Texto <script>alert(1)</script>",
            NotasTecnicas = "Notas propias con **negrita**.\n- una viñeta",
        }, logger);

        Assert.Equal(ReportDefaultTexts.Reserva, s.DefaultReserva);
        Assert.Equal("Notas propias con **negrita**.\n- una viñeta", s.DefaultNotasTecnicas);
        Assert.Contains(logger.Messages, m =>
            m == "Report: DefaultTexts:Reserva tiene contenido no permitido (HTML); se usa el texto por defecto");
    }
}
