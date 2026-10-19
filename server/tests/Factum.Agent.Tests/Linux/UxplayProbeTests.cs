using Factum.Agent.Common;
using Factum.Agent.Services.Linux;

namespace Factum.Agent.Tests.Linux;

/// <summary>tatana-linux D-T25: sondeo de uxplay -h y regla de AirPlay en Linux.</summary>
public sealed class UxplayProbeTests
{
    private const string Help173 = """
        UxPlay 1.73: An open-source AirPlay mirroring server.
        =========== Website: https://github.com/FDH2/UxPlay ==========
        Usage: uxplay [-n name] [-s wxh] [-p [n]] [(other options)]
        Options:
        -n name   Specify the network name of the AirPlay server
        -mp4 [fn] Record (and display) the audio and video streams as mp4 file
        -p        Use legacy ports UDP 6000,6001,7011 TCP 7000,7001,7100
        """;

    private const string Help168 = """
        UxPlay 1.68: An open-source AirPlay mirroring server.
        Options:
        -n name   Specify the network name of the AirPlay server
        -p        Use legacy ports UDP 6000,6001,7011 TCP 7000,7001,7100
        """;

    [Fact]
    public void Parsea_version_y_mp4()
    {
        Assert.Equal(new UxplayInfo(true, "1.73"), UxplayProbe.Parse(Help173));
        Assert.Equal(new UxplayInfo(false, "1.68"), UxplayProbe.Parse(Help168));
        Assert.Equal(new UxplayInfo(false, null), UxplayProbe.Parse(""));
    }

    [Fact]
    public void Una_sola_corrida_por_ruta()
    {
        var calls = 0;
        var probe = new UxplayProbe(_ => { Interlocked.Increment(ref calls); return Task.FromResult(Help173); });
        Assert.True(probe.Get("/usr/bin/uxplay-falso")!.SupportsMp4);
        Assert.True(probe.Get("/usr/bin/uxplay-falso")!.SupportsMp4);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Falla_del_proceso_es_sin_mp4()
    {
        var probe = new UxplayProbe(_ => throw new InvalidOperationException("no arranca"));
        Assert.Equal(new UxplayInfo(false, null), probe.Get("/x/uxplay"));
    }

    [Theory]
    [InlineData(false, false, false, "uxplay_not_found")]
    [InlineData(true, false, true, "uxplay_without_mp4")]
    [InlineData(true, true, false, "avahi_not_running")]
    [InlineData(true, true, true, null)]
    public void Regla_de_AirPlay_en_Linux(bool found, bool mp4, bool avahi, string? expected) =>
        Assert.Equal(expected, AirplayRules.LinuxReason(found, mp4, avahi));

    [Fact]
    public void Puertos_fijos_solo_en_Linux()
    {
        Assert.Equal(["-p"], AirplayRules.ExtraArgs(HostOs.Linux));
        Assert.Empty(AirplayRules.ExtraArgs(HostOs.MacOS));
        Assert.Empty(AirplayRules.ExtraArgs(HostOs.Windows));
    }
}
