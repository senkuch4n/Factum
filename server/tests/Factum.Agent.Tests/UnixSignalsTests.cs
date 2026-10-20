using System.Diagnostics;
using Factum.Agent.Common;

namespace Factum.Agent.Tests;

/// <summary>tatana-linux D-T16 / E13: kill(2) por P/Invoke. Solo corre en Linux (en otro SO, return temprano).</summary>
public sealed class UnixSignalsTests
{
    [Fact]
    public async Task Sigterm_a_sleep_sale_con_143()
    {
        if (!OperatingSystem.IsLinux()) return;
        var psi = new ProcessStartInfo("sh") { UseShellExecute = false };
        psi.ArgumentList.Add("-c");
        // sh no hace exec: el código de salida del hijo que murió por señal es 128 + 15.
        psi.ArgumentList.Add("sleep 30; exit 0");
        using var p = Process.Start(psi)!;
        Thread.Sleep(200);
        Assert.True(UnixSignals.TrySend(p.Id, UnixSignals.SIGTERM));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await p.WaitForExitAsync(cts.Token);
        Assert.Equal(143, p.ExitCode);
    }

    [Fact]
    public void Pid_inexistente_o_invalido_es_false()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.False(UnixSignals.TrySend(0, 0));
        Assert.False(UnixSignals.TrySend(-1, UnixSignals.SIGTERM));
        // Un pid enorme no existe (pid_max <= 4194304 en Linux).
        Assert.False(UnixSignals.TrySend(int.MaxValue - 1, 0));
    }

    [Fact]
    public void Senal_0_a_si_mismo_es_true()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.True(UnixSignals.TrySend(Environment.ProcessId, 0));
    }
}
