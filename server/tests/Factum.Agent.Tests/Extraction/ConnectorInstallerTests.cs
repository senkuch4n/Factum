using Factum.Agent.Common;
using Factum.Agent.Services.Extraction;

namespace Factum.Agent.Tests.Extraction;

public class ConnectorInstallerTests
{
    private static ConnectorInstaller With(Func<IReadOnlyList<string>, ProcessResult> stub) =>
        new((args, _) => Task.FromResult(stub(args)));

    [Theory]
    [InlineData("INSTALL_FAILED_USER_RESTRICTED", ConnectorReasonCodes.InstallBlocked)]
    [InlineData("INSTALL_FAILED_VERIFICATION_FAILURE", ConnectorReasonCodes.InstallBlocked)]
    [InlineData("INSTALL_FAILED_UPDATE_INCOMPATIBLE", ConnectorReasonCodes.Conflict)]
    [InlineData("INSTALL_FAILED_INSUFFICIENT_STORAGE", ConnectorReasonCodes.InstallFailed)]
    public void Classify_MapeaInstallFailed(string output, string expected) =>
        Assert.Equal(expected, ConnectorInstaller.Classify(output));

    [Fact]
    public async Task Install_Exito()
    {
        var inst = With(_ => new ProcessResult(0, "Success", ""));
        var res = await inst.InstallAsync("S", "/tmp/x.apk", default);
        Assert.True(res.Installed);
    }

    [Fact]
    public async Task Install_FallaBloqueada()
    {
        var inst = With(_ => new ProcessResult(1, "", "Failure [INSTALL_FAILED_USER_RESTRICTED]"));
        var res = await inst.InstallAsync("S", "/tmp/x.apk", default);
        Assert.False(res.Installed);
        Assert.Equal(ConnectorReasonCodes.InstallBlocked, res.ReasonCode);
        Assert.Equal("INSTALL_FAILED_USER_RESTRICTED", res.Reason);
    }

    [Fact]
    public async Task Install_Timeout_Bloqueada()
    {
        var inst = With(_ => new ProcessResult(ProcessRunner.TimeoutExitCode, "", "No terminó en 120 s"));
        var res = await inst.InstallAsync("S", "/tmp/x.apk", default);
        Assert.False(res.Installed);
        Assert.Equal(ConnectorReasonCodes.InstallBlocked, res.ReasonCode);
    }

    [Fact]
    public async Task Uninstall_ExitoYNoInstalado()
    {
        Assert.True(await With(_ => new ProcessResult(0, "Success", "")).UninstallAsync("S", default));
        Assert.True(await With(_ => new ProcessResult(1, "", "Unknown package: app.factum.connector"))
            .UninstallAsync("S", default));
    }

    [Fact]
    public async Task Uninstall_SeIntentaAnteCancelacion()
    {
        var called = 0;
        var inst = new ConnectorInstaller((args, ct) =>
        {
            called++;
            return Task.FromResult(new ProcessResult(0, "Success", ""));
        });
        // Simula el finally: aunque la extracción se canceló, la desinstalación se intenta igual.
        Assert.True(await inst.UninstallAsync("S", CancellationToken.None));
        Assert.Equal(1, called);
    }
}
