using Factum.Agent.Common;

namespace Factum.Agent.Tests;

/// <summary>Agent:AllowedOrigins y la guarda de origen (zip-local-informe-servidor §4.2, §6.1).</summary>
public sealed class OriginPolicyTests
{
    [Fact]
    public void Defaults_WhenEmpty()
    {
        var (origins, errors) = OriginPolicy.Parse(null);
        Assert.Empty(errors);
        Assert.Equal(["http://localhost:3000", "http://127.0.0.1:3000"], origins);
        Assert.Equal(OriginPolicy.Defaults, OriginPolicy.Parse([]).Origins);
    }

    [Fact]
    public void Parse_NormalizesAndDedupes()
    {
        var (origins, errors) = OriginPolicy.Parse(["https://Factum.Ejemplo.com/", "https://factum.ejemplo.com",
            "http://localhost:3001"]);
        Assert.Empty(errors);
        Assert.Equal(["https://factum.ejemplo.com", "http://localhost:3001"], origins);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://factum.ejemplo.com/app")]
    [InlineData("https://factum.ejemplo.com?x")]
    [InlineData("https://factum.ejemplo.com#x")]
    [InlineData("file:///tmp")]
    [InlineData("localhost:3000")]
    [InlineData("")]
    public void Parse_Invalid(string value) => Assert.Single(OriginPolicy.Parse([value]).Errors);

    [Theory]
    [InlineData("http://localhost:3000", true)]
    [InlineData("http://LOCALHOST:3000", true)]
    [InlineData("http://localhost:3000/", true)]
    [InlineData("http://127.0.0.1:3000", true)]
    [InlineData("http://localhost:3001", false)]
    [InlineData("https://evil.example", false)]
    [InlineData("null", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAllowed(string? origin, bool expected) =>
        Assert.Equal(expected, OriginPolicy.IsAllowed(origin, OriginPolicy.Defaults));
}
