using System.Net.Sockets;
using Factum.Agent.Services.Ios;

namespace Factum.Agent.Tests;

// T2 de la SDD ios-herramientas-windows: sonda del servicio de Apple con conexión y reloj inyectados.
public sealed class AppleServiceProbeTests
{
    private static AppleServiceProbe Windows(Func<CancellationToken, Task<bool>> connect, Func<DateTimeOffset>? clock = null) =>
        new(connect, () => throw new InvalidOperationException("no se usa en Windows"), isWindows: true, clock);

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
    public async Task Unix_usa_el_socket_de_usbmuxd(bool exists, AppleServiceState expected)
    {
        var probe = new AppleServiceProbe(_ => throw new InvalidOperationException("no se usa en Unix"),
            () => exists, isWindows: false);
        Assert.Equal(expected, await probe.GetStateAsync());
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
