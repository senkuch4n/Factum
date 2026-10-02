using Factum.Backend.Services.Catalogs;

namespace Factum.Backend.Tests;

/// <summary>Vectores de Refactorizaciones/formulario-caso-catalogos.md §3.5 (iguales en TS).</summary>
public sealed class CatalogTextTests
{
    [Theory]
    [InlineData("  Teléfono   celular ", "Teléfono celular", "telefono celular")]
    [InlineData("TELÉFONO CELULAR", "TELÉFONO CELULAR", "telefono celular")]
    [InlineData("el Sr. Juan Pérez", "el Sr. Juan Pérez", "el sr. juan perez")]
    [InlineData("el sr. juan perez", "el sr. juan perez", "el sr. juan perez")]
    [InlineData("Muñoz\tGarcía\n", "Muñoz García", "munoz garcia")]
    [InlineData("   ", "", "")]
    [InlineData(null, "", "")]
    public void Vectores(string? input, string clean, string key)
    {
        Assert.Equal(clean, CatalogText.CleanValue(input));
        Assert.Equal(key, CatalogText.NormalizeKey(input));
    }

    [Fact]
    public void NoSeGeneranDuplicados_TelefonoCelular()
    {
        Assert.Equal(CatalogText.NormalizeKey("Teléfono celular"),
            CatalogText.NormalizeKey("  teléfono celular "));
    }

    [Fact]
    public void TresVariantesDelMismoNombre_MismaClave()
    {
        var a = CatalogText.NormalizeKey("el Sr. Juan Pérez");
        Assert.Equal(a, CatalogText.NormalizeKey("el sr. juan perez"));
        Assert.Equal(a, CatalogText.NormalizeKey("  EL SR.  JUAN PÉREZ "));
    }
}
