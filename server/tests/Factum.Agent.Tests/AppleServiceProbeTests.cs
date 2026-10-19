using System.Net.Sockets;
using Factum.Agent.Common;
using Factum.Agent.Services.Ios;

namespace Factum.Agent.Tests;

// T2 de la SDD ios-herramientas-windows: sonda del servicio de Apple con conexión y reloj inyectados.
public sealed class AppleServiceProbeTests
{
    private static AppleServiceProbe Windows(Func<CancellationToken, Task<bool>> connect, Func<DateTimeOffset>? clock = null) =>
        new(connect, () => throw new InvalidOperationException("no se usa en Windows"), HostOs.Windows, clock);

    [Fact]
    public async Task Windows_conecta_es_Ok()
    {
        Assert.Equal(AppleServiceState.Ok, await Windows(_ => Task.FromResult(true)).GetStateAsync());
    }

    [Fact]
    public async Task Windows_rechazo_o_timeout_es_Missing()
    {
        Assert.Equal(AppleServiceState.Missing, await Windows(_ => Task.FromResult(false)).GetStateAsync());
    }

    [Fact]
    public async Task Windows_excepcion_rara_es_Unknown()
    {
        Assert.Equal(AppleServiceState.Unknown,
            await Windows(_ => throw new SocketException((int)SocketError.AccessDenied)).GetStateAsync());
        Assert.Equal(AppleServiceState.Unknown,
            await Windows(_ => throw new InvalidOperationException("raro")).GetStateAsync());
    }

    [Theory]
    [InlineData(true, AppleServiceState.Ok)]
    [InlineData(false, AppleServiceState.Missing)]
    public async Task MacOS_usa_solo_el_socket_de_usbmuxd(bool exists, AppleServiceState expected)
    {
        var probe = new AppleServiceProbe(_ => throw new InvalidOperationException("no se usa en Unix"),
            () => exists, HostOs.MacOS, usbmuxdInstalled: () => true);
        Assert.Equal(expected, await probe.GetStateAsync());
    }

    // tatana-linux D-T12: socket, o usbmuxd instalado sin socket (sin iPhone enchufado), o nada.
    [Theory]
    [InlineData(true, false, AppleServiceState.Ok)]
    [InlineData(false, true, AppleServiceState.Ok)]
    [InlineData(true, true, AppleServiceState.Ok)]
    [InlineData(false, false, AppleServiceState.Missing)]
    public async Task Linux_socket_o_usbmuxd_instalado(bool socket, bool installed, AppleServiceState expected)
    {
        var probe = new AppleServiceProbe(_ => throw new InvalidOperationException("no se usa en Linux"),
            () => socket, HostOs.Linux, usbmuxdInstalled: () => installed);
        Assert.Equal(expected, await probe.GetStateAsync());
    }

    [Fact]
    public async Task Linux_excepcion_es_Unknown()
    {
        var probe = new AppleServiceProbe(_ => Task.FromResult(true), () => false, HostOs.Linux,
            usbmuxdInstalled: () => throw new UnauthorizedAccessException());
        Assert.Equal(AppleServiceState.Unknown, await probe.GetStateAsync());
    }

    [Theory]
    [InlineData("/usr/sbin/usbmuxd", true)]
    [InlineData("/usr/bin/usbmuxd", true)]
    [InlineData("/usr/lib/systemd/system/usbmuxd.service", true)]
    [InlineData("/lib/systemd/system/usbmuxd.service", true)]
    [InlineData("/usr/local/bin/usbmuxd", false)]
    public void Usbmuxd_instalado_por_binario_o_unidad(string path, bool expected)
    {
        Assert.Equal(expected, AppleServiceProbe.IsUsbmuxdInstalled(p => p == path));
    }

    [Fact]
    public async Task Cachea_5_segundos()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var calls = 0;
        var answer = false;
        var probe = Windows(_ => { calls++; return Task.FromResult(answer); }, () => now);

        Assert.Equal(AppleServiceState.Missing, await probe.GetStateAsync());
        answer = true;
        now = now.AddSeconds(4.9);
        Assert.Equal(AppleServiceState.Missing, await probe.GetStateAsync()); // sigue el caché
        Assert.Equal(1, calls);

        now = now.AddSeconds(0.2);
        Assert.Equal(AppleServiceState.Ok, await probe.GetStateAsync()); // venció: se vuelve a sondear
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(AppleServiceState.Ok, "ok")]
    [InlineData(AppleServiceState.Missing, "missing")]
    [InlineData(AppleServiceState.Unknown, "unknown")]
    public void Json(AppleServiceState state, string expected) => Assert.Equal(expected, AppleServiceProbe.ToJson(state));
}
