using Factum.Backend.DTOs;
using Factum.Backend.Services.Cases;

namespace Factum.Backend.Tests;

/// <summary>Refactorizaciones/formulario-caso-catalogos.md §10.3: CaseFields + ValidateCaseData.</summary>
public sealed class CaseFieldsIntegrantesTests
{
    private const string Imei = "356938035643809";

    [Fact]
    public void ConLista_LimpiaYDerivaElTexto_IgnorandoElDelRequest()
    {
        var f = CaseFields.From(new UpdateCaseRequest(
            IntegrantesTribunal: "otro texto", Integrantes: ["Dr. Juan Pérez", " ", "Dra. María Gómez"]));
        Assert.NotNull(f.Integrantes);
        Assert.Equal(["Dr. Juan Pérez", "Dra. María Gómez"], f.Integrantes!);
        Assert.Equal("Dr. Juan Pérez y Dra. María Gómez", f.IntegrantesTribunal);
    }

    [Fact]
    public void ConLista_Crear_TambienDeriva()
    {
        var f = CaseFields.From(new CreateCaseRequest(Integrantes: ["A", "B", "C"]));
        Assert.Equal("A, B y C", f.IntegrantesTribunal);
    }

    [Fact]
    public void ListaVacia_DerivaTextoVacio()
    {
        var f = CaseFields.From(new UpdateCaseRequest(IntegrantesTribunal: "viejo", Integrantes: []));
        Assert.NotNull(f.Integrantes);
        Assert.Empty(f.Integrantes!);
        Assert.Equal("", f.IntegrantesTribunal);
    }

    [Fact]
    public void SinLista_ClienteViejo_TextoTalCual()
    {
        var f = CaseFields.From(new UpdateCaseRequest(IntegrantesTribunal: "Dres. Juan Pérez y María Gómez"));
        Assert.Null(f.Integrantes);
        Assert.Equal("Dres. Juan Pérez y María Gómez", f.IntegrantesTribunal);
    }

    [Fact]
    public void Mas20Items_Error()
    {
        var f = CaseFields.From(new UpdateCaseRequest(
            Integrantes: Enumerable.Range(1, 21).Select(i => $"Integrante {i}").ToList()));
        Assert.Equal("Se pueden cargar hasta 20 integrantes", CaseValidation.ValidateCaseData(f, Imei).Error);
    }

    [Fact]
    public void Exactamente20Items_SinErrorDeLargo()
    {
        var f = CaseFields.From(new UpdateCaseRequest(
            Integrantes: Enumerable.Range(1, 20).Select(i => $"Integrante {i}").ToList()));
        Assert.Null(CaseValidation.ValidateCaseData(f, Imei).Error);
    }

    [Fact]
    public void ItemDe301_Error()
    {
        var f = CaseFields.From(new UpdateCaseRequest(Integrantes: [new string('a', 301)]));
        Assert.Equal("El campo integrantes supera los 300 caracteres",
            CaseValidation.ValidateCaseData(f, Imei).Error);
    }

    [Fact]
    public void TextoDerivadoMayorA500_SinErrorDeLargo()
    {
        var f = CaseFields.From(new UpdateCaseRequest(
            Integrantes: Enumerable.Range(1, 5).Select(i => new string((char)('a' + i), 200)).ToList()));
        Assert.True(f.IntegrantesTribunal.Length > 500);
        Assert.Null(CaseValidation.ValidateCaseData(f, Imei).Error);
    }

    [Fact]
    public void SinLista_TextoMayorA500_SigueDandoError()
    {
        var f = CaseFields.From(new UpdateCaseRequest(IntegrantesTribunal: new string('x', 501)));
        Assert.Equal("El campo integrantes_tribunal supera los 500 caracteres",
            CaseValidation.ValidateCaseData(f, Imei).Error);
    }
}
