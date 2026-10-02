using Factum.Backend.Services.Cases;

namespace Factum.Backend.Tests;

/// <summary>Vectores de Refactorizaciones/formulario-caso-catalogos.md §4.2 (iguales en TS, DP1 A).</summary>
public sealed class IntegrantesFormatterTests
{
    public static TheoryData<string[], string> Vectores => new()
    {
        { [], "" },
        { ["Dr. Juan Pérez"], "Dr. Juan Pérez" },
        { ["Dr. Juan Pérez", "Dra. María Gómez"], "Dr. Juan Pérez y Dra. María Gómez" },
        { ["Dr. Juan Pérez", "Dra. María Gómez", "Dr. Luis Díaz"], "Dr. Juan Pérez, Dra. María Gómez y Dr. Luis Díaz" },
        { ["Juan Pérez", "Ignacio Díaz"], "Juan Pérez e Ignacio Díaz" },
        { ["Juan Pérez", "Hilda Gómez"], "Juan Pérez e Hilda Gómez" },
        { ["Juan Pérez", "Hielo Ruiz"], "Juan Pérez y Hielo Ruiz" },
        { ["Juan Pérez", "Íñigo Ruiz"], "Juan Pérez e Íñigo Ruiz" },
    };

    [Theory]
    [MemberData(nameof(Vectores))]
    public void Join(string[] items, string expected) =>
        Assert.Equal(expected, IntegrantesFormatter.Join(items));

    [Fact]
    public void Clean_DescartaVaciosYConservaOrden()
    {
        var r = IntegrantesFormatter.Clean(["  B  ", "", null, "   ", "A\t de  C", "Z"]);
        Assert.Equal(["B", "A de C", "Z"], r);
    }

    [Fact]
    public void Clean_Null_DevuelveListaVacia() => Assert.Empty(IntegrantesFormatter.Clean(null));
}
