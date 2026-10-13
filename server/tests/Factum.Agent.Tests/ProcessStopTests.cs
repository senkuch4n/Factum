using System.Diagnostics;
using Factum.Agent.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Factum.Agent.Tests;

/// <summary>Escalado SIGINT → SIGTERM (grabacion-android-robusta §5.2, DA2). Solo macOS/Linux.</summary>
public sealed class ProcessStopTests
{
    // Un shell que ignora SIGINT (como un scrcpy lanzado desde un Tatana que heredó SIGINT ignorado).
    private static Process StartIgnoringSigint()
    {
        var psi = new ProcessStartInfo("sh") { UseShellExecute = false };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("trap '' INT; exec sleep 60");
        var p = Process.Start(psi)!;
        Thread.Sleep(300); // que el trap quede instalado antes de la señal
        return p;
    }

    [Fact]
    public async Task ConEscalado_CierraConSigterm_SinKill()
    {
        if (OperatingSystem.IsWindows()) return;
        using var p = StartIgnoringSigint();
        var sw = Stopwatch.StartNew();
        var r = await ProcessStop.StopAsync(p, TimeSpan.FromSeconds(10), NullLogger.Instance, "test",
            escalateAfter: TimeSpan.FromSeconds(1));
        Assert.True(r.Clean);
        Assert.True(r.Escalated);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"tardó {sw.Elapsed}");
        Assert.True(p.HasExited);
    }

    [Fact]
    public async Task SinEscalado_TerminaEnKill()
    {
        if (OperatingSystem.IsWindows()) return;
        using var p = StartIgnoringSigint();
        var clean = await ProcessStop.StopGracefullyAsync(p, TimeSpan.FromSeconds(2), NullLogger.Instance, "test");
        Assert.False(clean);
        Assert.True(p.HasExited);
    }

    [Fact]
    public async Task ProcesoQueRespondeASigint_NoEscala()
    {
        if (OperatingSystem.IsWindows()) return;
        var psi = new ProcessStartInfo("sh") { UseShellExecute = false };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("trap 'exit 0' INT; while true; do sleep 0.1; done");
        using var p = Process.Start(psi)!;
        Thread.Sleep(300);
        var r = await ProcessStop.StopAsync(p, TimeSpan.FromSeconds(10), NullLogger.Instance, "test",
            escalateAfter: TimeSpan.FromSeconds(3));
        Assert.True(r.Clean);
        Assert.False(r.Escalated);
    }
}
