using Factum.Agent.Common;
using Factum.Agent.Services;

namespace Factum.Agent.Tests;

// T4: parsers de versión de /health.tools.
public sealed class VersionParserTests
{
    [Fact]
    public void Adb_toma_la_linea_Version()
    {
        const string salida = "Android Debug Bridge version 1.0.41\r\nVersion 37.0.1-13426479\r\nInstalled as C:\\Tatana\\tools\\platform-tools\\adb.exe\r\nRunning on Windows 10.0.19045\r\n";
        Assert.Equal("37.0.1-13426479", VersionParser.Parse("adb", salida, ""));
    }

    [Fact]
    public void Adb_sin_linea_Version_devuelve_la_primera()
    {
        Assert.Equal("Android Debug Bridge version 1.0.32", VersionParser.Parse("adb", "Android Debug Bridge version 1.0.32\n", ""));
    }

    [Fact]
    public void Scrcpy_segundo_token_de_la_primera_linea()
    {
        const string salida = "scrcpy 4.1 <https://github.com/Genymobile/scrcpy>\n\nDependencies (compiled / linked):\n - SDL: 3.4.12 / 3.4.12\n";
        Assert.Equal("4.1", VersionParser.Parse("scrcpy", salida, ""));
    }

    [Theory]
    [InlineData("ffmpeg version 8.1.2 Copyright (c) 2000-2026 the FFmpeg developers\nbuilt with clang\n", "8.1.2")]
    [InlineData("ffmpeg version N-121345-g0a1b2c3d4e-20261001 Copyright (c) 2000-2026 the FFmpeg developers\r\n", "N-121345-g0a1b2c3d4e-20261001")]
    public void Ffmpeg_token_despues_de_version(string salida, string esperado)
    {
        Assert.Equal(esperado, VersionParser.Parse("ffmpeg", salida, ""));
    }

    [Fact]
    public void Python_por_stdout_o_stderr()
    {
        Assert.Equal("3.11.9", VersionParser.Parse("python", "Python 3.11.9\r\n", ""));
        Assert.Equal("2.7.18", VersionParser.Parse("python", "", "Python 2.7.18\n"));
    }

    [Fact]
    public void Salida_vacia_da_null()
    {
        Assert.Null(VersionParser.Parse("scrcpy", "", ""));
        Assert.Null(VersionParser.Parse("ffmpeg", "", ""));
        Assert.Null(VersionParser.Parse("python", "", ""));
        Assert.Null(VersionParser.Parse("adb", "", ""));
    }

    [Theory]
    [InlineData(ToolSource.Portable, "portable")]
    [InlineData(ToolSource.Path, "path")]
    [InlineData(ToolSource.Homebrew, "homebrew")]
    public void Source_se_serializa_como_string_literal(ToolSource s, string esperado)
    {
        Assert.Equal(esperado, ToolInventory.SourceText(s));
    }
    // T3 de ios-herramientas-windows: versión de pymobiledevice3 (importlib.metadata).
    [Theory]
    [InlineData("10.7.4\n", "10.7.4")]
    [InlineData("  10.7.4  \r\n", "10.7.4")]
    [InlineData("\n\n10.7.4.dev3+g1a2b\n", "10.7.4.dev3+g1a2b")]
    public void Pymobiledevice3_version(string salida, string esperado)
    {
        Assert.Equal(esperado, VersionParser.Pymobiledevice3(salida));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("Traceback (most recent call last):\n  File \"<string>\", line 1, in <module>\nimportlib.metadata.PackageNotFoundError: No package metadata was found for pymobiledevice3\n")]
    public void Pymobiledevice3_vacio_o_traceback_es_null(string salida)
    {
        Assert.Null(VersionParser.Pymobiledevice3(salida));
    }
}
