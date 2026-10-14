using Factum.Backend.Services.Timestamping;
using Microsoft.Extensions.Configuration;

namespace Factum.Backend.Tests.Timestamping;

/// <summary><see cref="TimestampingOptions.Resolve"/> y <see cref="TimestampingOptions.SanitizeUrl"/> (§5.6, §11.2).</summary>
public sealed class TimestampingOptionsTests
{
    private static (TimestampingOptions.Settings S, List<string> E) Resolve(params (string Key, string? Value)[] values) =>
        TimestampingOptions.Resolve(new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>("Timestamping:" + v.Key, v.Value)))
            .Build());

    [Fact]
    public void Defaults_Desactivado_SinError()
    {
        var (s, e) = Resolve();
        Assert.Empty(e);
        Assert.False(s.IsActive);
        Assert.Empty(s.Urls);
        Assert.True(s.Enabled);
        Assert.Equal((10, 12, 10, 15, 120, 30),
            (s.TimeoutSeconds, s.MaxAttempts, s.InlineBudgetSeconds, s.GenerationBudgetSeconds, s.InlineCooldownSeconds,
                s.PollSeconds));
    }

    [Fact]
    public void Urls_ComaEspaciosYSaltos_VaciasIgnoradas_EnOrden()
    {
        var (s, e) = Resolve(("Urls", " https://freetsa.org/tsr ,, http://timestamp.digicert.com\n\nhttps://otra.example:8443/tsa "));
        Assert.Empty(e);
        Assert.True(s.IsActive);
        Assert.Equal(["https://freetsa.org/tsr", "http://timestamp.digicert.com/", "https://otra.example:8443/tsa"],
            s.Urls.Select(u => u.ToString()).ToArray());
        Assert.Equal(["freetsa.org", "timestamp.digicert.com", "otra.example:8443"], s.Hosts.ToArray());
    }

    [Theory]
    [InlineData("/tsr")]
    [InlineData("ftp://tsa.example/tsr")]
    [InlineData("https://tsa.example/tsr#x")]
    [InlineData("tsa.example")]
    public void UrlInvalida_ErrorDeArranque_ConLaClave(string url)
    {
        var (_, e) = Resolve(("Urls", url));
        var error = Assert.Single(e);
        Assert.Contains("Timestamping:Urls (FACTUM_SELLADO_URLS)", error);
    }

    [Fact]
    public void UrlInvalida_ConCredenciales_ElErrorNoLasRepite()
    {
        var (_, e) = Resolve(("Urls", "ftp://usuario:clave@tsa.example/tsr"));
        Assert.DoesNotContain("clave", Assert.Single(e));
    }

    [Fact]
    public void EnabledFalse_ConUrls_Desactivado()
    {
        var (s, e) = Resolve(("Urls", "https://freetsa.org/tsr"), ("Enabled", "false"));
        Assert.Empty(e);
        Assert.False(s.IsActive);
        Assert.Single(s.Urls);
    }

    [Fact]
    public void EnabledInvalido_Error() =>
        Assert.Contains("FACTUM_SELLADO_HABILITADO", Assert.Single(Resolve(("Enabled", "si")).E));

    [Theory]
    [InlineData("TimeoutSeconds", 2, 60, "FACTUM_SELLADO_TIMEOUT_SEGUNDOS")]
    [InlineData("MaxAttempts", 1, 50, "FACTUM_SELLADO_MAX_INTENTOS")]
    [InlineData("InlineBudgetSeconds", 1, 60, null)]
    [InlineData("GenerationBudgetSeconds", 1, 120, null)]
    [InlineData("InlineCooldownSeconds", 0, 3600, null)]
    [InlineData("PollSeconds", 5, 3600, null)]
    public void Rangos(string key, int min, int max, string? env)
    {
        Assert.Empty(Resolve((key, min.ToString())).E);
        Assert.Empty(Resolve((key, max.ToString())).E);
        var low = Assert.Single(Resolve((key, (min - 1).ToString())).E);
        Assert.Contains("Timestamping:" + key, low);
        if (env is not null) Assert.Contains(env, low);
        Assert.Single(Resolve((key, (max + 1).ToString())).E);
        Assert.Single(Resolve((key, "diez")).E);
    }

    [Theory]
    [InlineData("https://usuario:clave@tsa.example/tsr?x=1", "https://tsa.example/tsr")]
    [InlineData("http://timestamp.digicert.com", "http://timestamp.digicert.com/")]
    [InlineData("https://tsa.example:8443/a/b", "https://tsa.example:8443/a/b")]
    public void SanitizeUrl_SinUserinfoNiQuery(string raw, string expected) =>
        Assert.Equal(expected, TimestampingOptions.SanitizeUrl(new Uri(raw)));
}
