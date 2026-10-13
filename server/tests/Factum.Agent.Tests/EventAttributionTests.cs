using Factum.Agent.Common;

namespace Factum.Agent.Tests;

/// <summary>wizard-concurrencia-pestanas §5.1: case_id/origin_id son solo atribución (inválido → null, nunca 400).</summary>
public sealed class EventAttributionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("../x")]
    [InlineData("a b")]
    [InlineData("<script>")]
    [InlineData("caso\n2")]
    public void Normalize_invalido_es_null(string? raw) => Assert.Null(EventAttribution.Normalize(raw));

    [Fact]
    public void Normalize_recorta_espacios() => Assert.Equal("o1", EventAttribution.Normalize("  o1 "));

    [Fact]
    public void Normalize_64_ok_65_null()
    {
        Assert.Equal(new string('a', 64), EventAttribution.Normalize(new string('a', 64)));
        Assert.Null(EventAttribution.Normalize(new string('a', 65)));
    }

    [Fact]
    public void Normalize_guid_valido_queda_canonico()
    {
        Assert.Equal("ba916e4f-1c2d-4e5f-8a9b-0c1d2e3f4a5b",
            EventAttribution.Normalize("ba916e4f-1c2d-4e5f-8a9b-0c1d2e3f4a5b"));
        // Mismo formato que record/start (TryParseCaseId): minúsculas.
        Assert.Equal("ba916e4f-1c2d-4e5f-8a9b-0c1d2e3f4a5b",
            EventAttribution.Normalize("BA916E4F-1C2D-4E5F-8A9B-0C1D2E3F4A5B"));
    }

    [Fact]
    public void Normalize_hex_de_origin_id_sin_guiones_se_conserva() =>
        Assert.Equal("0123456789abcdef0123456789abcdef", EventAttribution.Normalize("0123456789abcdef0123456789abcdef"));

    [Fact]
    public void From_normaliza_los_dos()
    {
        var a = EventAttribution.From("../x", " o1 ");
        Assert.Null(a.CaseId);
        Assert.Equal("o1", a.OriginId);
        Assert.Equal(EventAttribution.None, EventAttribution.From(null, null));
    }
}

public sealed class AgentCapabilitiesTests
{
    [Fact]
    public void Health_anuncia_atribucion_y_vista_previa()
    {
        Assert.Contains("event_attribution_v1", Factum.Agent.Controllers.HealthController.Capabilities);
        Assert.Contains("device_preview_v1", Factum.Agent.Controllers.HealthController.Capabilities);
    }
}
