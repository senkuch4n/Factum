using Factum.Backend.Infrastructure;
using Factum.Backend.Services.Branding;
using static Factum.Backend.Tests.Branding.BrandingTestData;

namespace Factum.Backend.Tests.Branding;

/// <summary>marca-por-cliente B19: reglas estrictas de §5 y textos exactos de §5.1.</summary>
public sealed class BrandingRulesTests
{
    private static BrandingFieldError Error(BrandingSaveInput input)
    {
        var (value, error) = BrandingRules.Validate(input);
        Assert.Null(value);
        return Assert.IsType<BrandingFieldError>(error);
    }

    private static NormalizedBranding Ok(BrandingSaveInput input)
    {
        var (value, error) = BrandingRules.Validate(input);
        Assert.Null(error);
        return Assert.IsType<NormalizedBranding>(value);
    }

    // ── nombre ───────────────────────────────────────────────────────────────

    [Fact]
    public void Nombre_150_Ok_151_Rechazado()
    {
        Assert.Equal(new string('a', 150), Ok(Input(Meta(name: "  " + new string('a', 150) + " "))).OrganizationName);
        var e = Error(Input(Meta(name: new string('a', 151))));
        Assert.Equal("organization_name", e.Field);
        Assert.Equal("El nombre de la organización puede tener hasta 150 caracteres.", e.Message);
    }

    [Fact]
    public void Nombre_ConSaltoDeLinea_Rechazado()
    {
        var e = Error(Input(Meta(name: "Estudio\nFicticio")));
        Assert.Equal("organization_name", e.Field);
        Assert.Equal("No puede tener saltos de línea ni caracteres de control.", e.Message);
    }

    // ── líneas de contacto ───────────────────────────────────────────────────

    [Fact]
    public void Lineas_6_Ok_7_Rechazado()
    {
        var six = Enumerable.Range(1, 6).Select(i => (string?)$"Línea {i}").ToList();
        Assert.Equal(6, Ok(Input(Meta(lines: six))).ContactLines.Count);

        var seven = Enumerable.Range(1, 7).Select(i => (string?)$"Línea {i}").ToList();
        var e = Error(Input(Meta(lines: seven)));
        Assert.Equal("contact_lines", e.Field);
        Assert.Equal("Podés cargar hasta 6 líneas de contacto.", e.Message);
        Assert.Null(e.Index);
    }

    [Fact]
    public void Lineas_VaciasYNull_SeDescartan_Trim()
    {
        var lines = new List<string?> { "  Calle Ficticia 123 ", "", null, "   ", "Tel. 000" };
        Assert.Equal(["Calle Ficticia 123", "Tel. 000"], Ok(Input(Meta(lines: lines))).ContactLines);

        // 6 no vacías + vacías intercaladas: no supera el máximo.
        var mixed = new List<string?> { "a", "", "b", "c", null, "d", "e", "f", " " };
        Assert.Equal(6, Ok(Input(Meta(lines: mixed))).ContactLines.Count);
    }

    [Fact]
    public void Linea_151_ConSuIndexEnElArrayRecibido()
    {
        var lines = new List<string?> { "ok", "", new string('x', 151) };
        var e = Error(Input(Meta(lines: lines)));
        Assert.Equal("contact_lines", e.Field);
        Assert.Equal(2, e.Index);
        Assert.Equal("Cada línea de contacto puede tener hasta 150 caracteres.", e.Message);
        Assert.Equal(2, e.Extra()!["index"]);
    }

    [Fact]
    public void Linea_ConControl_ConIndex()
    {
        var e = Error(Input(Meta(lines: ["ok", "tab\taqui"])));
        Assert.Equal("contact_lines", e.Field);
        Assert.Equal(1, e.Index);
        Assert.Equal("No puede tener saltos de línea ni caracteres de control.", e.Message);
    }

    // ── colores ──────────────────────────────────────────────────────────────

    [Fact]
    public void Primario_Minusculas_SeNormaliza()
    {
        Assert.Equal("1F3A93", Ok(Input(Meta(primary: "#1f3a93"))).PrimaryColor);
    }

    [Fact]
    public void Primario_Amarillo_ContrasteTruncado()
    {
        var e = Error(Input(Meta(primary: "#FFFF00")));
        Assert.Equal("primary_color", e.Field);
        Assert.Equal(1.0, e.Contrast);
        Assert.Equal("Contraste 1.0:1 con blanco, mínimo 4.5:1.", e.Message);
        var extra = e.Extra()!;
        Assert.Equal(1.0, extra["contrast"]);
        Assert.Equal(4.5, extra["min_contrast"]);
        Assert.False(extra.ContainsKey("index"));
    }

    [Fact]
    public void Primario_FormatoInvalido()
    {
        var e = Error(Input(Meta(primary: "zzz")));
        Assert.Equal("primary_color", e.Field);
        Assert.Equal("Ingresá un color en formato #RRGGBB.", e.Message);
        Assert.Null(e.Extra());
    }

    [Fact]
    public void Colores_VaciosONull_SonDefault()
    {
        var v = Ok(Input(Meta(primary: "", accent: null)));
        Assert.Equal("", v.PrimaryColor);
        Assert.Equal("", v.AccentColor);
    }

    [Fact]
    public void Acento_Oscuro_RechazadoPorContrasteConLaTinta()
    {
        var e = Error(Input(Meta(accent: "#123456")));
        Assert.Equal("accent_color", e.Field);
        var ratio = BrandingColors.Contrast("123456", BrandingColors.InkColor);
        var shown = Math.Floor(ratio * 10) / 10;
        Assert.Equal(shown, e.Contrast);
        Assert.Equal($"Contraste {BrandingErrors.FormatContrast(shown)}:1 con el texto, mínimo 4.5:1.", e.Message);
    }

    [Fact]
    public void Acento_Valido()
    {
        Assert.Equal("E6ECFA", Ok(Input(Meta(accent: "e6ecfa"))).AccentColor);
    }

    [Fact]
    public void Contraste_SeTrunca_NoSeRedondea()
    {
        Assert.Equal(4.4, BrandingRules.TruncatedContrast(4.49));
        Assert.Equal(4.5, BrandingRules.TruncatedContrast(4.5));
        Assert.Equal("4.4", BrandingErrors.FormatContrast(BrandingRules.TruncatedContrast(4.49)));
    }

    // ── imágenes ─────────────────────────────────────────────────────────────

    [Fact]
    public void Imagen_PngValido()
    {
        var png = TestImages.Png(64, 32);
        var v = Ok(Input(Meta(logoAction: "replace"), logo: png));
        Assert.Equal(BrandingImageAction.Replace, v.Logo.Action);
        var img = v.Logo.Image!;
        Assert.Equal("image/png", img.ContentType);
        Assert.Equal((64, 32), (img.Width, img.Height));
        Assert.Equal(png.LongLength, img.Size);
        Assert.Matches("^[0-9a-f]{12}$", img.Version);
        Assert.Equal(BrandingImageAction.Keep, v.Isotype.Action);
    }

    [Fact]
    public void Imagen_JpegMinimoValido()
    {
        var v = Ok(Input(Meta(isotypeAction: "replace"), isotype: TestImages.Jpeg(100, 50)));
        Assert.Equal("image/jpeg", v.Isotype.Image!.ContentType);
        Assert.Equal((100, 50), (v.Isotype.Image.Width, v.Isotype.Image.Height));
    }

    [Fact]
    public void Imagen_BytesAlAzar_Formato()
    {
        var bytes = new byte[256];
        new Random(42).NextBytes(bytes);
        bytes[0] = 0x00;
        var e = Error(Input(Meta(logoAction: "replace"), logo: bytes));
        Assert.Equal("logo", e.Field);
        Assert.Equal("El archivo no es una imagen PNG ni JPEG.", e.Message);
    }

    [Fact]
    public void Imagen_Vacia_Formato()
    {
        var e = Error(Input(Meta(isotypeAction: "replace"), isotype: []));
        Assert.Equal("isotype", e.Field);
        Assert.Equal("El archivo no es una imagen PNG ni JPEG.", e.Message);
    }

    [Fact]
    public void Imagen_UnMiBMasUno_TooBig_ElPesoPrimero()
    {
        // PNG válido de cabecera, rellenado hasta 1 MiB + 1: se rechaza por peso antes de mirar el contenido.
        var png = TestImages.Png(64, 64);
        var big = new byte[BrandingService.MaxLogoBytes + 1];
        png.CopyTo(big, 0);
        var e = Error(Input(Meta(logoAction: "replace"), logo: big));
        Assert.Equal("logo", e.Field);
        Assert.Equal("La imagen supera el máximo de 1 MB.", e.Message);

        // Bytes al azar del mismo tamaño: también "too big", no "formato".
        var e2 = Error(Input(Meta(logoAction: "replace"), logo: new byte[BrandingService.MaxLogoBytes + 1]));
        Assert.Equal("La imagen supera el máximo de 1 MB.", e2.Message);
    }

    [Fact]
    public void Imagen_TooBigSinLeer_DesdeElController()
    {
        var input = new BrandingSaveInput(Meta(logoAction: "replace"), null, null, LogoTooBig: true);
        var e = Error(input);
        Assert.Equal("logo", e.Field);
        Assert.Equal("La imagen supera el máximo de 1 MB.", e.Message);
    }

    [Theory]
    [InlineData(15, 20)]
    [InlineData(4097, 16)]
    public void Imagen_LadoFueraDeRango_TextoExacto(int w, int h)
    {
        var e = Error(Input(Meta(logoAction: "replace"), logo: TestImages.Png(w, h)));
        Assert.Equal("logo", e.Field);
        Assert.Equal($"La imagen mide {w}×{h} px; cada lado tiene que estar entre 16 y 4096 px.", e.Message);
    }

    [Fact]
    public void Imagen_ReplaceSinArchivo_Missing()
    {
        var e = Error(Input(Meta(isotypeAction: "replace")));
        Assert.Equal("isotype", e.Field);
        Assert.Equal("Falta el archivo de la imagen.", e.Message);
    }

    // ── metadata ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("borrar", "keep")]
    [InlineData("keep", null)]
    [InlineData("KEEP", "keep")]
    public void Accion_Invalida_Metadata(string? logo, string? isotype)
    {
        var e = Error(Input(Meta(logoAction: logo, isotypeAction: isotype)));
        Assert.Equal("metadata", e.Field);
        Assert.Equal("La solicitud no es válida. Recargá e intentá de nuevo.", e.Message);
    }

    [Theory]
    [InlineData("keep")]
    [InlineData("remove")]
    public void ArchivoConKeepORemove_Metadata(string action)
    {
        var e = Error(Input(Meta(logoAction: action), logo: TestImages.Png(32, 32)));
        Assert.Equal("metadata", e.Field);
    }

    [Fact]
    public void Orden_MetadataAntesQueNombre_NombreAntesQueColor()
    {
        Assert.Equal("metadata", Error(Input(Meta(name: new string('a', 151), logoAction: "x"))).Field);
        Assert.Equal("organization_name",
            Error(Input(Meta(name: new string('a', 151), primary: "zzz"))).Field);
        Assert.Equal("contact_lines", Error(Input(Meta(lines: [new string('a', 151)], primary: "zzz"))).Field);
        Assert.Equal("primary_color", Error(Input(Meta(primary: "zzz", accent: "zzz"))).Field);
        Assert.Equal("accent_color",
            Error(Input(Meta(accent: "zzz", logoAction: "replace"), logo: [1, 2, 3])).Field);
        Assert.Equal("logo",
            Error(Input(Meta(logoAction: "replace", isotypeAction: "replace"), logo: [1], isotype: [1])).Field);
    }

    [Fact]
    public void FormularioVacio_IsEmpty()
    {
        Assert.True(Ok(Input(Meta())).IsEmpty);
        Assert.True(Ok(Input(Meta(logoAction: "remove"))).IsEmpty);
        Assert.False(Ok(Input(Meta(name: "x"))).IsEmpty);
    }
}
