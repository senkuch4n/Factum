using Factum.Backend.Infrastructure;
using Factum.Backend.Services.Auth;

namespace Factum.Backend.Tests.Hardening;

/// <summary>api-hardening §4.4.2 / T8.</summary>
public sealed class LoginThrottleTests
{
    private static LoginThrottle Throttle(int limit, int windowSeconds = 60) => new(SecuritySettings.Defaults with
    {
        LoginRateLimit = new LoginRateLimitOptions { PerDniPermitLimit = limit, PerDniWindowSeconds = windowSeconds },
    });

    [Fact]
    public void ConLimite2_ElTerceroEspera()
    {
        using var t = Throttle(2);
        Assert.Null(t.TryAcquire("99000010"));
        Assert.Null(t.TryAcquire("99000010"));
        var wait = t.TryAcquire("99000010");
        Assert.NotNull(wait);
        Assert.True(wait > TimeSpan.Zero);
        Assert.True(wait <= TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void OtroDni_NoSeAfecta()
    {
        using var t = Throttle(1);
        Assert.Null(t.TryAcquire("99000010"));
        Assert.NotNull(t.TryAcquire("99000010"));
        Assert.Null(t.TryAcquire("99000011"));
    }

    [Fact]
    public void EspaciosEnLosExtremos_MismaParticion()
    {
        using var t = Throttle(1);
        Assert.Null(t.TryAcquire("  99000010 "));
        Assert.NotNull(t.TryAcquire("99000010"));
    }

    [Fact]
    public void DniGigante_NoExplota_YSeCortaA32()
    {
        using var t = Throttle(1);
        var big = new string('9', 1000);
        Assert.Null(t.TryAcquire(big));
        Assert.NotNull(t.TryAcquire(new string('9', 32) + "otra-cola"));
        Assert.Equal(32, LoginThrottle.KeyOf(big).Length);
    }

    [Fact]
    public void Vacio_SePermiteSiempre()
    {
        using var t = Throttle(1);
        for (var i = 0; i < 5; i++)
        {
            Assert.Null(t.TryAcquire(""));
            Assert.Null(t.TryAcquire(null));
            Assert.Null(t.TryAcquire("   "));
        }
    }
}
