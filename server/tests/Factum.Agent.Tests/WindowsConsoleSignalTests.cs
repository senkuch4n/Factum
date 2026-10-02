using Factum.Agent.Common;

namespace Factum.Agent.Tests;

// T5: cómo se lanza el auxiliar `--ctrl-c <pid>`.
public sealed class WindowsConsoleSignalTests
{
    [Fact]
    public void Exe_publicado_se_lanza_solo_con_el_flag_y_el_pid()
    {
        const string exe = @"C:\Users\perito\AppData\Local\Programs\Tatana\Factum.Agent.exe";

        var (fileName, args) = WindowsConsoleSignal.BuildHelperCommand(exe, "", 4321);

        Assert.Equal(exe, fileName);
        Assert.Equal(["--ctrl-c", "4321"], args);
    }

    [Theory]
    [InlineData("/usr/local/share/dotnet/dotnet")]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    [InlineData(@"C:\Program Files\dotnet\DOTNET.EXE")]
    public void Con_dotnet_se_pasa_el_dll_primero(string host)
    {
        const string dll = "/repo/server/src/Factum.Agent/bin/Debug/net10.0/Factum.Agent.dll";

        var (fileName, args) = WindowsConsoleSignal.BuildHelperCommand(host, dll, 77);

        Assert.Equal(host, fileName);
        Assert.Equal([dll, "--ctrl-c", "77"], args);
    }

    [Fact]
    public void RunHelper_fuera_de_Windows_devuelve_1_sin_hacer_nada()
    {
        if (OperatingSystem.IsWindows()) return; // en Windows tocaría la consola real
        Assert.Equal(1, WindowsConsoleSignal.RunHelper("1234"));
    }
}
