using Factum.Backend.Services.Timestamping;

namespace Factum.Backend.Tests.Timestamping;

/// <summary><see cref="TimestampBackoff"/> (sellado-tiempo §6.4, §11.2).</summary>
public sealed class TimestampBackoffTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 5)]
    [InlineData(4, 10)]
    [InlineData(5, 30)]
    [InlineData(6, 60)]
    [InlineData(7, 120)]
    [InlineData(8, 240)]
    [InlineData(9, 480)]
    [InlineData(10, 720)]
    [InlineData(11, 1440)]
    [InlineData(12, 1440)]
    [InlineData(40, 1440)]
    public void Tabla_ConJitterAcotado(int attempts, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), TimestampBackoff.Base(attempts));
        var random = new Random(attempts);
        for (var i = 0; i < 200; i++)
        {
            var d = TimestampBackoff.Delay(attempts, random);
            Assert.InRange(d.TotalMinutes, minutes * 0.9, minutes * 1.1);
        }
    }

    [Fact]
    public void ConLosDefaults_ElUltimoIntentoCaeAUnas52Horas()
    {
        // Intentos 1..12: la espera después del intento n es Base(n); el 12 ya no espera (failed).
        var total = Enumerable.Range(1, 11).Sum(n => TimestampBackoff.Base(n).TotalHours);
        Assert.InRange(total, 51, 53);
    }

    [Fact]
    public void Intento12_ConMax12_EsFinal()
    {
        Assert.False(TimestampBackoff.IsFinal(11, 12));
        Assert.True(TimestampBackoff.IsFinal(12, 12));
        Assert.True(TimestampBackoff.IsFinal(1, 1));
    }
}
