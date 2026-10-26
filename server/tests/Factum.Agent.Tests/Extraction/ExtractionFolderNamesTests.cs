using Factum.Agent.Services.Extraction;

namespace Factum.Agent.Tests.Extraction;

public class ExtractionFolderNamesTests
{
    [Fact]
    public void Build_SaneaMarcaYModelo()
    {
        var name = ExtractionFolderNames.Build("samsung", "SM-M127F", new DateTime(2026, 10, 25, 14, 30, 0));
        Assert.Equal("Factum_Extraccion_samsung-SM_M127F_20261025-143000", name);
    }

    [Fact]
    public void Build_DefaultsSiVacio()
    {
        var name = ExtractionFolderNames.Build("", null, new DateTime(2026, 1, 2, 3, 4, 5));
        Assert.Equal("Factum_Extraccion_dispositivo-android_20260102-030405", name);
    }

    [Fact]
    public void Unique_SumaSufijoSiExiste()
    {
        var existentes = new HashSet<string> { "base", "base_2" };
        var n = ExtractionFolderNames.Unique("base", existentes.Contains);
        Assert.Equal("base_3", n);
    }

    [Fact]
    public void Unique_DevuelveBaseSiLibre()
    {
        var n = ExtractionFolderNames.Unique("libre", _ => false);
        Assert.Equal("libre", n);
    }
}
