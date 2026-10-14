using Factum.Agent.Services.Session;

namespace Factum.Agent.Tests.Session;

/// <summary>Cuentas de espacio (grabacion-sesion-completa §5.8, D13).</summary>
public sealed class SessionDiskBudgetTests
{
    private const long MiB = 1024 * 1024;
    private const long GiB = 1024 * MiB;
    private const long Rate = 30 * MiB;

    [Fact]
    public void Rate_UsaLaMayor()
    {
        Assert.Equal(Rate, SessionDiskBudget.Rate(Rate, null));
        Assert.Equal(Rate, SessionDiskBudget.Rate(Rate, 10 * MiB));
        Assert.Equal(40 * MiB, SessionDiskBudget.Rate(Rate, 40 * MiB));
        Assert.Equal(Rate, SessionDiskBudget.Rate(Rate, double.NaN));
    }

    [Fact]
    public void ObservedPerMinute()
    {
        Assert.Equal(60.0 * MiB, SessionDiskBudget.ObservedPerMinute(30 * MiB, 30));
        Assert.Null(SessionDiskBudget.ObservedPerMinute(0, 30));
        Assert.Null(SessionDiskBudget.ObservedPerMinute(10, null));
    }

    [Fact]
    public void RequisitoDeInicio()
    {
        // minFree + 2 × 30 min × 30 MiB = 1 GiB + 1800 MiB.
        Assert.Equal(GiB + 1800 * MiB, SessionDiskBudget.RequiredToStart(GiB, 30, Rate));
        Assert.True(SessionDiskBudget.CanStart(GiB + 1800 * MiB, GiB, 30, Rate));
        Assert.False(SessionDiskBudget.CanStart(GiB + 1799 * MiB, GiB, 30, Rate));
        // Fail-open: sin medición se graba igual.
        Assert.True(SessionDiskBudget.CanStart(null, GiB, 30, Rate));
    }

    [Fact]
    public void Estimacion_FactorDosConElMismoVolumen()
    {
        var available = GiB + 600 * MiB;
        Assert.Equal(10, SessionDiskBudget.EstimatedMinutes(available, GiB, Rate, zipOnSameVolume: true));
        Assert.Equal(20, SessionDiskBudget.EstimatedMinutes(available, GiB, Rate, zipOnSameVolume: false));
        Assert.Equal(0, SessionDiskBudget.EstimatedMinutes(GiB / 2, GiB, Rate, true));
        Assert.Null(SessionDiskBudget.EstimatedMinutes(null, GiB, Rate, true));
    }

    [Fact]
    public void MinutosRestantesAvisoYCorte()
    {
        var available = GiB + 59 * Rate;
        var left = SessionDiskBudget.MinutesLeft(available, GiB, Rate);
        Assert.Equal(59, left);
        Assert.True(SessionDiskBudget.ShouldWarn(left, 60));
        Assert.False(SessionDiskBudget.ShouldWarn(60, 60));
        Assert.False(SessionDiskBudget.ShouldWarn(null, 60));

        Assert.False(SessionDiskBudget.ShouldCut(GiB + 2 * Rate, GiB, Rate, 2));
        Assert.True(SessionDiskBudget.ShouldCut(GiB + 2 * Rate - 1, GiB, Rate, 2));
        Assert.False(SessionDiskBudget.ShouldCut(null, GiB, Rate, 2));
    }

    [Fact]
    public void Reemision_AlCruzarMultiploDeDiez()
    {
        Assert.True(SessionDiskBudget.ShouldReemit(null, 55));
        Assert.False(SessionDiskBudget.ShouldReemit(55, 51));
        Assert.True(SessionDiskBudget.ShouldReemit(51, 49));
        Assert.False(SessionDiskBudget.ShouldReemit(49, 49));
        Assert.True(SessionDiskBudget.ShouldReemit(10, 9));
    }

    [Fact]
    public void MountOf_PrefijoMasLargo()
    {
        string[] mounts = ["/", "/Volumes/Externo", "/System/Volumes/Data"];
        Assert.Equal("/", SessionDiskBudget.MountOf("/Users/perito/agent-data", mounts));
        Assert.Equal("/Volumes/Externo/", SessionDiskBudget.MountOf("/Volumes/Externo/Factum/Evidencia", mounts));
        Assert.Equal("/", SessionDiskBudget.MountOf("/Volumes/ExternoB/x", mounts));
    }

    [Fact]
    public void SameVolume_MismaCarpetaBase()
    {
        var root = Path.GetTempPath();
        Assert.True(SessionDiskBudget.SameVolume(Path.Combine(root, "a"), Path.Combine(root, "b")));
    }
}
