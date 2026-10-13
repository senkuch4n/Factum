using System.Net;
using Factum.Backend.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Factum.Backend.Tests.Hardening;

/// <summary>api-hardening §4.6 / T7.</summary>
public sealed class SecuritySettingsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    private static readonly string[] DefaultNets =
        ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7"];

    [Fact]
    public void SinConfig_Defaults()
    {
        var (s, errors) = SecuritySettings.Resolve(Config());
        Assert.Empty(errors);
        Assert.Equal(10, s.LoginRateLimit.PerIpPermitLimit);
        Assert.Equal(60, s.LoginRateLimit.PerIpWindowSeconds);
        Assert.Equal(5, s.LoginRateLimit.PerDniPermitLimit);
        Assert.Equal(60, s.LoginRateLimit.PerDniWindowSeconds);
        Assert.Equal(DefaultNets, s.KnownNetworks.Select(n => n.ToString()));
        Assert.Equal(DefaultNets, SecuritySettings.Defaults.KnownNetworks.Select(n => n.ToString()));
    }

    [Fact]
    public void Override_DeLosCuatroNumeros()
    {
        var (s, errors) = SecuritySettings.Resolve(Config(
            ("Security:LoginRateLimit:PerIpPermitLimit", "20"),
            ("Security:LoginRateLimit:PerIpWindowSeconds", "30"),
            ("Security:LoginRateLimit:PerDniPermitLimit", "3"),
            ("Security:LoginRateLimit:PerDniWindowSeconds", "120")));
        Assert.Empty(errors);
        Assert.Equal((20, 30, 3, 120), (s.LoginRateLimit.PerIpPermitLimit, s.LoginRateLimit.PerIpWindowSeconds,
            s.LoginRateLimit.PerDniPermitLimit, s.LoginRateLimit.PerDniWindowSeconds));
    }

    [Theory]
    [InlineData("PerIpPermitLimit", "0")]
    [InlineData("PerIpWindowSeconds", "-1")]
    [InlineData("PerDniPermitLimit", "0")]
    [InlineData("PerDniWindowSeconds", "-30")]
    [InlineData("PerIpPermitLimit", "abc")]
    public void CeroNegativoOInvalido_Error(string name, string value)
    {
        var (_, errors) = SecuritySettings.Resolve(Config(($"Security:LoginRateLimit:{name}", value)));
        Assert.Equal([$"Security:LoginRateLimit:{name} tiene que ser mayor que 0"], errors);
    }

    [Fact]
    public void CidrInvalido_Error()
    {
        var (_, errors) = SecuritySettings.Resolve(Config(
            ("Security:ForwardedHeaders:KnownNetworks:0", "10.0.0.0/8"),
            ("Security:ForwardedHeaders:KnownNetworks:1", "no-es-red"),
            ("Security:ForwardedHeaders:KnownNetworks:2", "10.0.0.0/33")));
        Assert.Equal(
        [
            "Security:ForwardedHeaders:KnownNetworks: \"no-es-red\" no es una red válida (CIDR, p. ej. 10.0.0.0/8)",
            "Security:ForwardedHeaders:KnownNetworks: \"10.0.0.0/33\" no es una red válida (CIDR, p. ej. 10.0.0.0/8)",
        ], errors);
    }

    [Fact]
    public void ListaPropia_ReemplazaLosDefaults()
    {
        var (s, errors) = SecuritySettings.Resolve(Config(
            ("Security:ForwardedHeaders:KnownNetworks:0", "172.18.0.0/16")));
        Assert.Empty(errors);
        var net = Assert.Single(s.KnownNetworks);
        Assert.True(net.Contains(IPAddress.Parse("172.18.0.5")));
    }

    [Fact]
    public void ListaVacia_Defaults()
    {
        var (s, errors) = SecuritySettings.Resolve(Config(("Security:ForwardedHeaders:KnownNetworks:0", "  ")));
        Assert.Empty(errors);
        Assert.Equal(DefaultNets, s.KnownNetworks.Select(n => n.ToString()));
    }
}
