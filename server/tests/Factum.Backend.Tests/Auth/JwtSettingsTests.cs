using System.Globalization;
using Factum.Backend.Services.Auth;
using Microsoft.Extensions.Configuration;

namespace Factum.Backend.Tests.Auth;

/// <summary>sesion-larga §5.1 / D11 / D12.</summary>
public sealed class JwtSettingsTests
{
    private static IConfiguration Config(string? ttl = null, string? max = null)
    {
        var values = new Dictionary<string, string?>();
        if (ttl is not null) values["Jwt:ExpiryHours"] = ttl;
        if (max is not null) values["Jwt:SessionMaxHours"] = max;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Defaults_SinErroresNiWarnings()
    {
        var (errors, warnings) = JwtSettings.Validate(Config());
        Assert.Empty(errors);
        Assert.Empty(warnings);
        Assert.Equal(2, new JwtOptions().ExpiryHours);
        Assert.Equal(12, new JwtOptions().SessionMaxHours);
    }

    [Fact]
    public void ValoresDeAppsettings_SinErrores()
    {
        var (errors, warnings) = JwtSettings.Validate(Config("2", "12"));
        Assert.Empty(errors);
        Assert.Empty(warnings);
        Assert.Empty(JwtSettings.Validate(Config("8", "12")).Errors); // instalaciones existentes (S8)
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("200")]
    [InlineData("0,2")]
    public void Ttl_Invalido_ErrorQueNombraLaVariable(string value)
    {
        var (errors, _) = JwtSettings.Validate(Config(ttl: value));
        var e = Assert.Single(errors);
        Assert.Contains("Jwt:ExpiryHours", e);
        Assert.Contains("FACTUM_JWT_EXPIRY_HOURS", e);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("200")]
    public void Tope_Invalido_ErrorQueNombraLaVariable(string value)
    {
        var (errors, _) = JwtSettings.Validate(Config(max: value));
        var e = Assert.Single(errors);
        Assert.Contains("Jwt:SessionMaxHours", e);
        Assert.Contains("FACTUM_SESION_TOPE_HORAS", e);
    }

    [Fact]
    public void TopeMenorQueTtl_WarningConLasDosVariables()
    {
        var (errors, warnings) = JwtSettings.Validate(Config("0.2", "0.1"));
        Assert.Empty(errors);
        var w = Assert.Single(warnings);
        Assert.Contains("FACTUM_JWT_EXPIRY_HOURS", w);
        Assert.Contains("FACTUM_SESION_TOPE_HORAS", w);
    }

    [Fact]
    public void Decimal_ConCulturaEsAr_SeLeeConPunto()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-AR");
            var (errors, warnings) = JwtSettings.Validate(Config("0.2", "0.5"));
            Assert.Empty(errors);
            Assert.Empty(warnings);
            // El binder de opciones también lee con cultura invariante.
            var bound = Config("0.2", "0.5").GetSection("Jwt").Get<JwtOptions>()!;
            Assert.Equal(0.2, bound.ExpiryHours);
            Assert.Equal(720, bound.TokenTtlSeconds);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
