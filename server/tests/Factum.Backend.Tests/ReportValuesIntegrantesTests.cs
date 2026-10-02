using Factum.Backend.Models;
using Factum.Backend.Services.Branding;
using Factum.Backend.Services.Cases;
using Factum.Backend.Services.Reports;

namespace Factum.Backend.Tests;

/// <summary>{fraseIntegracion} sale del texto IntegrantesTribunal (SDD §6), con o sin lista.</summary>
public sealed class ReportValuesIntegrantesTests
{
    private sealed class FakeSettings : IReportSettings
    {
        public TimeZoneInfo Zone => TimeZoneInfo.Utc;
        public string ZoneLabel => "UTC";
        public string DomicilioConstituido => "";
        public bool EncryptZip => false;
        public string DefaultOperacionesRealizadas => "";
        public string DefaultAseguramientoEvidencia => "";
        public string DefaultNotasTecnicas => "";
        public string DefaultReserva => "";
    }

    private static string Frase(Case cas) =>
        ReportValues.Placeholders(cas, new FakeSettings(), new BrandingSnapshot(null, [], null))["{fraseIntegracion}"];

    [Fact]
    public void CasoViejo_SinLista_MismoTextoDeSiempre()
    {
        var cas = new Case { IntegrantesTribunal = "Dres. Juan Pérez y María Gómez", Integrantes = null };
        Assert.Equal(", con la integración de Dres. Juan Pérez y María Gómez", Frase(cas));
    }

    [Fact]
    public void CasoNuevo_ConListaDeTres()
    {
        List<string> lista = ["Dr. Juan Pérez", "Dra. María Gómez", "Dr. Luis Díaz"];
        var cas = new Case { Integrantes = lista, IntegrantesTribunal = IntegrantesFormatter.Join(lista) };
        Assert.Equal(", con la integración de Dr. Juan Pérez, Dra. María Gómez y Dr. Luis Díaz", Frase(cas));
    }

    [Fact]
    public void SinIntegrantes_FraseVacia()
    {
        var cas = new Case { IntegrantesTribunal = "", Integrantes = [] };
        Assert.Equal("", Frase(cas));
    }
}
