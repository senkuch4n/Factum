using Factum.Backend.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Factum.Backend.Tests;

/// <summary>Cors:AllowedOrigins (zip-local-informe-servidor §4.1, §5.9).</summary>
public sealed class CorsOriginsTests
{
    private static (IReadOnlyList<string> Origins, List<string> Errors) FromConfig(params string[] values)
    {
        var data = new Dictionary<string, string?>();
        for (var i = 0; i < values.Length; i++) data[$"Cors:AllowedOrigins:{i}"] = values[i];
        return CorsOrigins.Parse(new ConfigurationBuilder().AddInMemoryCollection(data).Build());
    }

    [Fact]
    public void Default_WhenMissing()
    {
        var (origins, errors) = FromConfig();
        Assert.Empty(errors);
        Assert.Equal(["http://localhost:3000", "http://127.0.0.1:3000"], origins);
    }

    [Fact]
    public void ValidList()
    {
        var (origins, errors) = FromConfig("https://factum.ejemplo.com", "http://localhost:3001");
        Assert.Empty(errors);
        Assert.Equal(["https://factum.ejemplo.com", "http://localhost:3001"], origins);
    }

    [Fact]
    public void Star_IsError()
    {
        var (_, errors) = FromConfig("*");
        Assert.Single(errors);
        Assert.Contains("*", errors[0]);
    }

    [Theory]
    [InlineData("https://factum.ejemplo.com/app")]
    [InlineData("https://factum.ejemplo.com?x=1")]
    [InlineData("https://factum.ejemplo.com#f")]
    [InlineData("ftp://factum.ejemplo.com")]
    [InlineData("factum.ejemplo.com")]
    [InlineData("https://user@factum.ejemplo.com")]
    [InlineData("  ")]
    public void Invalid_IsError(string value) => Assert.Single(FromConfig(value).Errors);

    [Fact]
    public void TrailingSlash_IsRemoved()
    {
        var (origins, errors) = FromConfig("https://Factum.Ejemplo.com/");
        Assert.Empty(errors);
        Assert.Equal(["https://factum.ejemplo.com"], origins);
    }
}
