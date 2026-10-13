using System.Net;
using Factum.Agent.Common;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Tests;

/// <summary>Filtro de <c>Host</c> contra DNS rebinding (tatana-ws-y-hosts §4.3, T-03).</summary>
public sealed class HostPolicyTests
{
    private static readonly IPAddress[] Machine =
    [
        IPAddress.Parse("192.168.1.20"),
        IPAddress.Parse("fe80::1%4"),
        IPAddress.Parse("2001:db8::5"),
        IPAddress.Loopback,
    ];

    private static IReadOnlyList<string> Hosts(string bind, params string?[] extra)
    {
        var (hosts, errors) = HostPolicy.Build(bind, extra, () => Machine);
        Assert.Empty(errors);
        return hosts;
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("10.0.0.5")]
    [InlineData("0.0.0.0")]
    public void LosLocalesSiempreEstan(string bind)
    {
        var hosts = Hosts(bind);
        Assert.Contains("localhost", hosts);
        Assert.Contains("127.0.0.1", hosts);
        Assert.Contains("[::1]", hosts);
    }

    [Fact]
    public void ConLocalhost_SoloLosLocales() =>
        Assert.Equal(["localhost", "127.0.0.1", "[::1]"], Hosts("localhost"));

    [Fact]
    public void IpDeBind_V4YV6ConCorchetes()
    {
        Assert.Contains("10.0.0.5", Hosts("10.0.0.5"));
        var v6 = Hosts("fe80::abcd%7");
        Assert.Contains("[fe80::abcd]", v6); // sin scope id
    }

    [Fact]
    public void Any_SumaIpv4_YIPv6Any_SumaAmbas()
    {
        var v4 = Hosts("0.0.0.0");
        Assert.Contains("192.168.1.20", v4);
        Assert.DoesNotContain("[2001:db8::5]", v4);
        Assert.Equal(v4.Count, v4.Distinct(StringComparer.OrdinalIgnoreCase).Count()); // 127.0.0.1 no se duplica

        var both = Hosts("::");
        Assert.Contains("192.168.1.20", both);
        Assert.Contains("[2001:db8::5]", both);
        Assert.Contains("[fe80::1]", both);
    }

    [Theory]
    [InlineData("tatana.local", "tatana.local")]
    [InlineData("  Tatana.LOCAL ", "tatana.local")]
    [InlineData("192.168.1.10", "192.168.1.10")]
    [InlineData("2001:db8::1", "[2001:db8::1]")]
    [InlineData("[2001:DB8::1]", "[2001:db8::1]")]
    [InlineData("pc-perito", "pc-perito")]
    public void ExtrasValidos_SeNormalizan(string raw, string expected)
    {
        Assert.True(HostPolicy.TryNormalize(raw, out var host));
        Assert.Equal(expected, host);
        Assert.Contains(expected, Hosts("localhost", raw));
    }

    [Theory]
    [InlineData("*", true)]
    [InlineData("*.x.com", true)]
    [InlineData("http://x", false)]
    [InlineData("x:8765", false)]
    [InlineData("[::1]:8765", false)]
    [InlineData("x/y", false)]
    [InlineData("", false)]
    [InlineData("a b", false)]
    [InlineData("user@x", false)]
    [InlineData("10.1", false)]
    public void ExtrasInvalidos_ConMensaje(string raw, bool star)
    {
        Assert.False(HostPolicy.TryNormalize(raw, out _));
        var (hosts, errors) = HostPolicy.Build("localhost", [raw], () => Machine);
        var error = Assert.Single(errors);
        Assert.Equal(star
            ? "Agent:AllowedHosts no admite \"*\": listá los hosts permitidos"
            : $"Agent:AllowedHosts: \"{raw}\" no es un host válido (nombre o IP, sin esquema, puerto ni path)", error);
        Assert.Equal(3, hosts.Count);
    }

    // El middleware real de ASP.NET con la lista de HostPolicy (riesgo §11: corchetes y puertos).
    [Theory]
    [InlineData("rebind.attacker.example:8765", 400)]
    [InlineData("rebind.attacker.example", 400)]
    [InlineData("localhost:8765", 200)]
    [InlineData("LOCALHOST:8765", 200)]
    [InlineData("127.0.0.1:8765", 200)]
    [InlineData("[::1]:8765", 200)]
    [InlineData("tatana.local:8765", 200)]
    [InlineData("192.168.1.20:8765", 400)]
    public async Task Middleware_FiltraPorHost(string host, int expected)
    {
        var (hosts, _) = HostPolicy.Build("127.0.0.1", ["tatana.local"], () => Machine);
        var reached = false;
        var mw = new HostFilteringMiddleware(_ =>
            {
                reached = true;
                return Task.CompletedTask;
            }, NullLogger<HostFilteringMiddleware>.Instance,
            new StaticMonitor(new HostFilteringOptions { AllowedHosts = hosts.ToList(), AllowEmptyHosts = true }));
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = HostString.FromUriComponent(host);
        ctx.Response.Body = new MemoryStream();
        ctx.Response.StatusCode = 200;

        await mw.Invoke(ctx);

        Assert.Equal(expected, ctx.Response.StatusCode);
        Assert.Equal(expected == 200, reached);
    }

    private sealed class StaticMonitor(HostFilteringOptions value) : IOptionsMonitor<HostFilteringOptions>
    {
        public HostFilteringOptions CurrentValue => value;
        public HostFilteringOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<HostFilteringOptions, string?> listener) => null;
    }
}
