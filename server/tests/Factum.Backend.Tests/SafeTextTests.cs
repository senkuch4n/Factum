using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Services.Admin;
using Factum.Backend.Services.Branding;
using Factum.Backend.Services.Cases;
using Factum.Backend.Services.Catalogs;
using static Factum.Backend.Tests.Branding.BrandingTestData;

namespace Factum.Backend.Tests;

/// <summary>
/// Helper <see cref="SafeText"/> (informe-formato-fixes §5.1) y su aplicación en los puntos de
/// entrada de §5.2 (funciones puras, sin Mongo). Todos los caracteres especiales van como escapes
/// para que el archivo fuente no tenga invisibles. Sin InlineData con surrogates sueltos: xUnit
/// serializa los datos y los cambia por U+FFFD.
/// </summary>
public sealed class SafeTextTests
{
    private static readonly TextShape[] Shapes = [TextShape.SingleLine, TextShape.MultiLine];

    // ── Tabla §5.1, fila por fila ────────────────────────────────────────────

    [Fact]
    public void Saltos_Y_Tabs_SingleLine_PasanAEspacio_MultiLine_SeConservan()
    {
        Assert.Equal("a b", SafeText.Clean("a\r\nb", TextShape.SingleLine));
        Assert.Equal("a b", SafeText.Clean("a\rb", TextShape.SingleLine));
        Assert.Equal("a b", SafeText.Clean("a\nb", TextShape.SingleLine));
        Assert.Equal("a b", SafeText.Clean("a\tb", TextShape.SingleLine));
        Assert.Equal("a  b", SafeText.Clean("a\n\nb", TextShape.SingleLine)); // no colapsa espacios
        Assert.Equal("Juan Pérez", SafeText.Clean("Juan\nPérez", TextShape.SingleLine));

        Assert.Equal("a\r\nb", SafeText.Clean("a\r\nb", TextShape.MultiLine));
        Assert.Equal("a\rb", SafeText.Clean("a\rb", TextShape.MultiLine));
        Assert.Equal("a\nb", SafeText.Clean("a\nb", TextShape.MultiLine));
        Assert.Equal("a\tb", SafeText.Clean("a\tb", TextShape.MultiLine));
    }

    [Fact]
    public void RestoDeC0_SeQuita_EnLasDosFormas()
    {
        var c0 = string.Concat(Enumerable.Range(0, 0x20)
            .Where(c => c is not (0x09 or 0x0A or 0x0D)).Select(c => (char)c));
        Assert.Equal(29, c0.Length);
        foreach (var shape in Shapes)
            Assert.Equal("ab", SafeText.Clean("a" + c0 + "b", shape));
    }

    [Fact]
    public void Del_Y_C1_SeConservan()
    {
        var c1 = string.Concat(Enumerable.Range(0x7F, 0x9F - 0x7F + 1).Select(c => (char)c));
        foreach (var shape in Shapes)
        {
            Assert.Equal("a" + c1 + "b", SafeText.Clean("a" + c1 + "b", shape));
            Assert.Equal("\u007F\u0085", SafeText.Clean("\u007F\u0085", shape));
        }
        Assert.False(SafeText.IsStrippedControl(0x7F, TextShape.SingleLine));
        Assert.False(SafeText.IsStrippedControl(0x85, TextShape.MultiLine));
        Assert.True(SafeText.IsStrippedControl(0x01, TextShape.MultiLine));
        Assert.False(SafeText.IsStrippedControl('\n', TextShape.MultiLine));
    }

    [Theory]
    [InlineData(0x202A)]
    [InlineData(0x202B)]
    [InlineData(0x202C)]
    [InlineData(0x202D)]
    [InlineData(0x202E)]
    [InlineData(0x2066)]
    [InlineData(0x2067)]
    [InlineData(0x2068)]
    [InlineData(0x2069)]
    public void Bidi_SeQuita(int cp)
    {
        Assert.True(SafeText.IsBidiControl(cp));
        foreach (var shape in Shapes)
            Assert.Equal("xevily", SafeText.Clean("x" + (char)cp + "evil" + (char)cp + "y", shape));
        Assert.Equal("ab", SafeText.StripInvisible("a" + (char)cp + "b"));
    }

    [Theory]
    [InlineData(0xFDD0)]
    [InlineData(0xFDEF)]
    [InlineData(0xFFFE)]
    [InlineData(0xFFFF)]
    [InlineData(0x1FFFE)]
    [InlineData(0x1FFFF)]
    [InlineData(0x10FFFE)]
    [InlineData(0x10FFFF)]
    public void NoCaracteres_SeQuitan_EnTodosLosPlanos(int cp)
    {
        Assert.True(SafeText.IsNonCharacter(cp));
        var s = "a" + char.ConvertFromUtf32(cp) + "b";
        foreach (var shape in Shapes)
            Assert.Equal("ab", SafeText.Clean(s, shape));
        Assert.Equal("ab", SafeText.StripInvisible(s));
    }

    [Fact]
    public void NoCaracteres_Vecinos_NoSonNoCaracteres()
    {
        Assert.False(SafeText.IsNonCharacter(0xFDCF));
        Assert.False(SafeText.IsNonCharacter(0xFDF0));
        Assert.False(SafeText.IsNonCharacter(0xFFFD));
        Assert.False(SafeText.IsNonCharacter(0x1FFFD));
        Assert.Equal("\uFFFD\U0001FFFD", SafeText.Clean("\uFFFD\U0001FFFD", TextShape.MultiLine));
    }

    [Fact]
    public void SurrogatesSueltos_SeQuitan_LosParesValidos_SeConservan()
    {
        foreach (var shape in Shapes)
        {
            Assert.Equal("ab", SafeText.Clean("a\uD800b", shape));
            Assert.Equal("ab", SafeText.Clean("a\uDC00b", shape));
            Assert.Equal("x", SafeText.Clean("\uD800x", shape));        // alto suelto al principio
            Assert.Equal("x", SafeText.Clean("x\uD800", shape));        // alto suelto al final
            Assert.Equal("x", SafeText.Clean("\uDC00x", shape));        // bajo suelto al principio
            Assert.Equal("x", SafeText.Clean("x\uDC00", shape));        // bajo suelto al final
            Assert.Equal("\U0001F600", SafeText.Clean("\uDC00\U0001F600\uD800", shape));
            Assert.Equal("lone hi ok \U0001F600", SafeText.Clean("lone\uD800 hi\uDC00 ok \U0001F600", shape));
            // Alto suelto seguido de un par válido: se quita solo el suelto.
            Assert.Equal("\uD800\uDE00", SafeText.Clean("\uD83D\uD800\uDE00", shape));
        }
    }

    [Fact]
    public void SeConservaTodoLoDemas()
    {
        // Acentos, ñ, comillas tipográficas, «», rayas, emoji con ZWJ, ZWNJ, LRM/RLM, ALM, NBSP,
        // U+2028/U+2029, BOM, hebreo, árabe y bandera.
        const string keep = "áéíóú ñÑ \u201Ccomillas\u201D «» \u2014 \u2013 " +
                            "\U0001F469\u200D\U0001F4BB a\u200Cb \u200E\u200F \u061C \u00A0 \u2028\u2029 \uFEFF " +
                            "\u05E9\u05DC\u05D5\u05DD \u0645\u0631\u062D\u0628\u0627 \U0001F1E6\U0001F1F7";
        foreach (var shape in Shapes)
            Assert.Equal(keep, SafeText.Clean(keep, shape));
        Assert.Equal(keep, SafeText.StripInvisible(keep));
    }

    [Fact]
    public void Nulos_Y_Vacios()
    {
        Assert.Equal("", SafeText.Clean(null, TextShape.SingleLine));
        Assert.Equal("", SafeText.Clean("", TextShape.MultiLine));
        Assert.Null(SafeText.CleanOrNull(null, TextShape.MultiLine));
        Assert.Equal("ab", SafeText.CleanOrNull("a\uFFFEb", TextShape.MultiLine));
        Assert.Equal("", SafeText.StripInvisible(null));
        Assert.Equal("  a  ", SafeText.Clean("  a  ", TextShape.SingleLine)); // no hace Trim
    }

    [Fact]
    public void StripInvisible_ConservaC0()
    {
        Assert.Equal("a\u0001\n\tb", SafeText.StripInvisible("a\u0001\u202E\n\t\uFFFEb"));
    }

    [Fact]
    public void EsIdempotente()
    {
        foreach (var input in new[]
                 {
                     "A\uFFFE\u202E B\n\r\n\t\u0001\u007F\uD800 \U0001F600\uDBFF", "\r\uFFFE\nfin", "\uD83D\uD800\uDE00",
                     "\uDBFF\uDFFE x", "\uFDD0x\U0001FFFE",
                 })
        {
            foreach (var shape in Shapes)
            {
                var once = SafeText.Clean(input, shape);
                Assert.Equal(once, SafeText.Clean(once, shape));
            }
            var s = SafeText.StripInvisible(input);
            Assert.Equal(s, SafeText.StripInvisible(s));
        }
    }

    // ── Puntos de entrada (§5.2) ─────────────────────────────────────────────

    [Fact]
    public void CaseFields_LimpiaYRecorta()
    {
        var create = CaseFields.From(new CreateCaseRequest(Caratula: "A\uFFFE\u202E B\n",
            Observaciones: " linea 1\n\u202Elinea 2\u0001 "));
        Assert.Equal("A B", create.Caratula);
        Assert.Equal("linea 1\nlinea 2", create.Observaciones);

        var update = CaseFields.From(new UpdateCaseRequest(TipoCausa: "\u2066Expte\tN\u2069 "));
        Assert.Equal("Expte N", update.TipoCausa);
        Assert.Null(update.Observaciones);
    }

    [Fact]
    public void CaseFields_LosLargosSeMidenDespuesDeLimpiar()
    {
        var withInvisibles = new string('a', CaseValidation.MaxLine) + "\u202E\u202C\uFFFE";
        var fields = CaseFields.From(new CreateCaseRequest(NroReferencia: withInvisibles));
        Assert.Equal(CaseValidation.MaxLine, fields.NroReferencia.Length);
    }

    [Fact]
    public void Integrantes_PasanPorCatalogText()
    {
        var fields = CaseFields.From(new CreateCaseRequest(Integrantes: ["Dr.\u202E Uno", "\uFFFE"]));
        Assert.Equal(["Dr. Uno"], fields.Integrantes!);
    }

    [Fact]
    public void CatalogText_CleanValue_QuitaInvisibles()
    {
        Assert.Equal("ab", CatalogText.CleanValue("a\u2066b"));
        Assert.Equal("a b", CatalogText.CleanValue(" a\n\u0001\tb "));
        Assert.Equal(CatalogText.NormalizeKey("Juzgado"), CatalogText.NormalizeKey("Juz\u202Egado"));
    }

    [Fact]
    public void ReportTexts_SeLimpianLosOcho_YNoSeTocaElResto()
    {
        var r = new ReportTextsDto(
            ObjetoInforme: "o\uFFFE", OperacionesRealizadas: "op\u202E", AseguramientoEvidencia: "as\u0001",
            Resultados: "re\nsul\ttados\uD800", ValoracionTecnica: null, Conclusiones: "co",
            NotasTecnicas: "\u2066no", Reserva: "res\uFFFF", Formato: "markdown", Trigger: "restore",
            RestoredFrom: "v1");
        var c = CaseService.CleanReportTexts(r);
        Assert.Equal("o", c.ObjetoInforme);
        Assert.Equal("op", c.OperacionesRealizadas);
        Assert.Equal("as", c.AseguramientoEvidencia);
        Assert.Equal("re\nsul\ttados", c.Resultados);
        Assert.Null(c.ValoracionTecnica);
        Assert.Equal("co", c.Conclusiones);
        Assert.Equal("no", c.NotasTecnicas);
        Assert.Equal("res", c.Reserva);
        Assert.Equal("markdown", c.Formato);
        Assert.Equal("restore", c.Trigger);
        Assert.Equal("v1", c.RestoredFrom);
    }

    [Fact]
    public void Branding_QuitaInvisiblesEnSilencio_YSigueRechazandoC0()
    {
        var (value, error) = BrandingRules.Validate(Input(Meta(name: "\u202EOrg", lines: ["Tel\uFFFE 1"])));
        Assert.Null(error);
        Assert.Equal("Org", value!.OrganizationName);
        Assert.Equal(["Tel 1"], value.ContactLines);

        var (v2, e2) = BrandingRules.Validate(Input(Meta(name: "Org\u0001")));
        Assert.Null(v2);
        Assert.Equal(BrandingErrors.MsgControlChars, e2!.Message);
    }

    [Fact]
    public void AccountFieldRules_Normalize_MultiLineYTrim()
    {
        Assert.Equal("a\nb", AccountFieldRules.Normalize(" a\u202E\n\u0001b "));
        Assert.Equal("", AccountFieldRules.Normalize(null));
    }
}
