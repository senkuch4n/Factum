using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Tests;

/// <summary>wizard-concurrencia-pestanas DT4: atribución de la grabación en curso en <see cref="RecordingRegistry"/>.</summary>
public sealed class RecordingAttributionTests
{
    private static RecordingRegistry Create() =>
        new(Options.Create(new AgentOptions { DataDirectory = Path.GetTempPath() }), TimeProvider.System);

    [Fact]
    public void Set_y_Take_devuelve_y_vacia()
    {
        var reg = Create();
        reg.SetOwner("S1", new EventAttribution("c1", "o1"));
        var a = reg.TakeOwner("S1");
        Assert.NotNull(a);
        Assert.Equal(("S1", "c1", "o1"), (a!.Serial, a.CaseId, a.OriginId));
        Assert.Null(reg.TakeOwner("S1"));
    }

    [Fact]
    public void Take_sin_entrada_es_null() => Assert.Null(Create().TakeOwner("NADA"));

    [Fact]
    public void Set_dos_veces_gana_el_ultimo()
    {
        var reg = Create();
        reg.SetOwner("S1", new EventAttribution("c1", "o1"));
        reg.SetOwner("S1", new EventAttribution("c2", "o2"));
        Assert.Equal("o2", reg.PeekOwner("S1")!.OriginId);
        Assert.Equal("c2", reg.TakeOwner("S1")!.CaseId);
    }

    [Fact]
    public void Seriales_distintos_no_se_mezclan()
    {
        var reg = Create();
        reg.SetOwner("S1", new EventAttribution("c1", "o1"));
        reg.SetOwner("S2", new EventAttribution("c2", "o2"));
        Assert.Equal("o1", reg.TakeOwner("S1")!.OriginId);
        Assert.Equal("o2", reg.PeekOwner("S2")!.OriginId);
    }
}
