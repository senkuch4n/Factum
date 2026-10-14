using Factum.Backend.Services.Auth;

namespace Factum.Backend.Tests.Auth;

/// <summary>sesion-larga §9.2: ventana pura del token (segundos Unix).</summary>
public sealed class SessionWindowTests
{
    private const long Ttl = 2 * 3600;
    private const long Max = 12 * 3600;
    private const long Login = 1_800_000_000;

    [Fact]
    public void Refresh_ConservaAuthTime_ExpEsAhoraMasTtl()
    {
        var now = Login + 5 * 3600;
        var w = SessionWindow.Compute(now, Login, Ttl, Max);
        Assert.NotNull(w);
        Assert.Equal(now + Ttl, w.Value.ExpS);
        Assert.Equal(Login + Max, w.Value.MaxS); // el tope sale de auth_time, no de "ahora"
    }

    [Fact]
    public void Exp_SeRecortaAlTope()
    {
        var now = Login + 11 * 3600; // faltan 60 min para el tope, ttl 2 h
        var w = SessionWindow.Compute(now, Login, Ttl, Max);
        Assert.NotNull(w);
        Assert.Equal(Login + Max, w.Value.ExpS);
        Assert.Equal(w.Value.MaxS, w.Value.ExpS);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3600)]
    public void PasadoElTope_Null(long extra)
    {
        Assert.Null(SessionWindow.Compute(Login + Max + extra, Login, Ttl, Max));
    }

    [Fact]
    public void TopeMenorQueTtl_RecortaYaEnElLogin()
    {
        var w = SessionWindow.Compute(Login, Login, Ttl, 1800);
        Assert.NotNull(w);
        Assert.Equal(Login + 1800, w.Value.ExpS);
    }
}
