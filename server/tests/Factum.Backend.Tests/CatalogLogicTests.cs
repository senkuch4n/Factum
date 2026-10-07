using Factum.Backend.Models;
using Factum.Backend.Services.Catalogs;

namespace Factum.Backend.Tests;

/// <summary>Definiciones y lógica pura de catálogos (sin Mongo).</summary>
public sealed class CatalogLogicTests
{
    [Fact]
    public void CuatroCatalogos_YPartesEnOrden()
    {
        Assert.Equal(["destinatarios", "partes", "profesiones", "tipos_dispositivo"],
            CatalogDefinitions.Ids.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        var cas = new Case { ParteDenunciante = "A", ParteDenunciada = "B", NombreProponente = "C" };
        Assert.Equal(["A", "B", "C"], CatalogDefinitions.Sources[CatalogDefinitions.Partes](cas));
        Assert.False(CatalogDefinitions.Exists("otro"));
        Assert.False(CatalogDefinitions.Exists(null));
    }

    [Fact]
    public void UsagesFor_DedupePorCatalogoYClave_PrimeraGrafia()
    {
        var u = CatalogLogic.UsagesFor(
        [
            ("partes", "Juan Pérez"), ("partes", " juan  perez "), ("destinatarios", "Juan Pérez"),
            ("partes", "  "), ("desconocido", "X"),
        ], DateTime.UnixEpoch);
        Assert.Equal(2, u.Count);
        Assert.Equal(("partes", "Juan Pérez", "juan perez"), (u[0].Catalog, u[0].Value, u[0].Key));
        Assert.Equal("destinatarios", u[1].Catalog);
    }

    [Fact]
    public void ValuesForUpdate_SoloLosQueCambiaron()
    {
        var prev = new Case { NombreTribunal = "Tribunal 1", ProfesionProponente = "Abogado", ParteDenunciante = "A" };
        var saved = new Case { NombreTribunal = "tribunal  1", ProfesionProponente = "Contador", ParteDenunciante = "A" };
        var v = CatalogLogic.ValuesForUpdate(saved, prev);
        Assert.Equal([("profesiones", "Contador")], v);
    }

    [Fact]
    public void SeedUsages_PrimeraGrafiaDelMasViejo_CountYUltimoUso_MasDefault()
    {
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddDays(5);
        var cases = new[]
        {
            new Case { CreatedAt = t2, NombreTribunal = "TRIBUNAL ORAL", TipoDispositivo = "Tablet" },
            new Case { CreatedAt = t1, NombreTribunal = "Tribunal Oral" },
        };
        var u = CatalogLogic.SeedUsages(cases);
        var trib = Assert.Single(u, x => x.Catalog == "destinatarios");
        Assert.Equal("Tribunal Oral", trib.Value);
        Assert.Equal(2, trib.Count);
        Assert.Equal(t2, trib.UsedAt);
        var cel = Assert.Single(u, x => x.Key == "telefono celular");
        Assert.Equal(0, cel.Count);
        Assert.Equal(DateTime.UnixEpoch, cel.UsedAt);
        Assert.Contains(u, x => x.Value == "Tablet");
    }

    [Fact]
    public void SeedUsages_TelefonoCelularYaUsado_NoSeDuplica()
    {
        var u = CatalogLogic.SeedUsages([new Case { TipoDispositivo = "teléfono  celular" }]);
        var cel = Assert.Single(u, x => x.Catalog == "tipos_dispositivo");
        Assert.Equal("teléfono celular", cel.Value);
        Assert.Equal(1, cel.Count);
    }

    [Fact]
    public void BuildResponse_CuatroClavesOrdenYDesconocidosIgnorados()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var entries = new[]
        {
            new CatalogEntry { Catalog = "partes", Value = "b", UseCount = 1, LastUsedAt = t },
            new CatalogEntry { Catalog = "partes", Value = "A", UseCount = 1, LastUsedAt = t },
            new CatalogEntry { Catalog = "partes", Value = "z", UseCount = 5, LastUsedAt = t },
            new CatalogEntry { Catalog = "partes", Value = "nuevo", UseCount = 1, LastUsedAt = t.AddDays(1) },
            new CatalogEntry { Catalog = "viejo", Value = "x" },
        };
        var r = CatalogLogic.BuildResponse(entries);
        Assert.Equal(4, r.Catalogs.Count);
        Assert.Empty(r.Catalogs["destinatarios"]);
        Assert.Equal(["nuevo", "z", "A", "b"], r.Catalogs["partes"].Select(e => e.Value));
    }

    [Fact]
    public void BuildResponse_TopeDe500()
    {
        var entries = Enumerable.Range(0, 600)
            .Select(i => new CatalogEntry { Catalog = "profesiones", Value = $"v{i}" });
        Assert.Equal(500, CatalogLogic.BuildResponse(entries).Catalogs["profesiones"].Count);
    }
}
