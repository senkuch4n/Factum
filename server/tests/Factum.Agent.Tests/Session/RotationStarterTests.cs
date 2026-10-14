using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services;
using Factum.Agent.Services.Session;
using Microsoft.Extensions.Logging.Abstractions;

namespace Factum.Agent.Tests.Session;

/// <summary>
/// Arranque confirmado del tramo nuevo en un corte solapado (post-review #80, observación 1): el anterior se cierra solo
/// si el nuevo sigue vivo durante la ventana; si se cae, se reintenta sin tocar el anterior.
/// </summary>
public sealed class RotationStarterTests
{
    /// <summary>Un intento de scrcpy falso: muere a los <c>DiesAfter</c> del marcador (null = sigue vivo).</summary>
    private sealed class Attempt(int n, TimeSpan? diesAfter, bool failsToStart = false)
    {
        public int N { get; } = n;
        public bool FailsToStart { get; } = failsToStart;
        public DateTime? StartedAt;
        public bool Aborted;
        public bool HasExited => StartedAt is { } s && diesAfter is { } d && DateTime.UtcNow - s >= d;
    }

    private sealed class Script(params Attempt[] attempts)
    {
        private int _next;
        public List<Attempt> Launched { get; } = [];

        public Attempt Launch()
        {
            var a = attempts[_next++];
            Launched.Add(a);
            return a;
        }

        public static Task<TrackStart> Wait(Attempt a)
        {
            if (a.FailsToStart)
                throw new AgentHttpException(504, AgentErrorCodes.RecordingStartTimeout, "no arrancó");
            a.StartedAt = DateTime.UtcNow;
            return Task.FromResult(new TrackStart(a.StartedAt.Value, 1000 + a.N, a.StartedAt));
        }

        public static Task Abort(Attempt a)
        {
            a.Aborted = true;
            return Task.CompletedTask;
        }
    }

    private static Task<(Attempt Capture, TrackStart Started)> Run(Script s, int confirmMs = 400, int attempts = 3) =>
        RotationStarter.StartConfirmedAsync(s.Launch, Script.Wait, a => a.HasExited, Script.Abort,
            TimeSpan.FromMilliseconds(confirmMs), attempts, TimeSpan.Zero, NullLogger.Instance);

    [Fact]
    public async Task SigueVivo_SeAceptaSinReintentos()
    {
        var s = new Script(new Attempt(1, null));
        var (c, started) = await Run(s);
        Assert.Equal(1, c.N);
        Assert.Equal(1001, started.Pid);
        Assert.False(c.Aborted);
        Assert.Single(s.Launched);
    }

    [Fact]
    public async Task SeCaeDentroDeLaVentana_SeAbortaYSeReintenta()
    {
        // Como en el SM-M127F: el nuevo se cae ~0,5–0,7 s después del marcador (ventana de 1 s).
        var s = new Script(new Attempt(1, TimeSpan.FromMilliseconds(150)), new Attempt(2, null));
        var (c, _) = await Run(s, confirmMs: 1000);
        Assert.Equal(2, c.N);
        Assert.True(s.Launched[0].Aborted);
        Assert.False(c.Aborted);
    }

    [Fact]
    public async Task SeCaeDespuesDeLaVentana_NoCuenta()
    {
        var s = new Script(new Attempt(1, TimeSpan.FromSeconds(5)));
        var (c, _) = await Run(s, confirmMs: 300);
        Assert.Equal(1, c.N);
        Assert.False(c.Aborted);
    }

    [Fact]
    public async Task NoArranca_SeReintenta()
    {
        var s = new Script(new Attempt(1, null, failsToStart: true), new Attempt(2, null));
        var (c, _) = await Run(s);
        Assert.Equal(2, c.N);
        Assert.True(s.Launched[0].Aborted);
    }

    [Fact]
    public async Task TodosLosIntentosFallan_LanzaConTope()
    {
        var s = new Script(new Attempt(1, TimeSpan.Zero), new Attempt(2, TimeSpan.Zero), new Attempt(3, TimeSpan.Zero),
            new Attempt(4, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(s, attempts: 3));
        Assert.Equal(3, s.Launched.Count); // el cuarto no se lanza
        Assert.All(s.Launched, a => Assert.True(a.Aborted));
    }

    [Fact]
    public async Task UltimoErrorEsElDelArranque()
    {
        var s = new Script(new Attempt(1, null, failsToStart: true));
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() => Run(s, attempts: 1));
        Assert.Equal(504, ex.Status);
    }

    [Fact]
    public void Opciones_DefaultsYRangos()
    {
        var o = new SessionOptions().Normalize(null);
        Assert.Equal(1500, o.RotationConfirmMilliseconds);
        Assert.Equal(3, o.RotationStartAttempts);
        var bad = new SessionOptions { RotationConfirmMilliseconds = -1, RotationStartAttempts = 9 }.Normalize(null);
        Assert.Equal(1500, bad.RotationConfirmMilliseconds);
        Assert.Equal(3, bad.RotationStartAttempts);
        Assert.Equal(0, new SessionOptions { RotationConfirmMilliseconds = 0 }.Normalize(null).RotationConfirmMilliseconds);
    }
}

/// <summary>Con el corte fallido (todos los intentos), la sesión sigue en el tramo actual y "pantalla siempre encendida" no se toca.</summary>
public sealed class RotationFailureStayAwakeTests : IDisposable
{
    private readonly SessionHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task CorteFallido_SinReafirmar_YElStopRestaura()
    {
        var calls = new List<string>();
        var value = "0";
        var keeper = new StayAwakeKeeper(true, null, (args, _) =>
        {
            var cmd = args[^1];
            calls.Add(cmd);
            if (cmd.StartsWith("settings put", StringComparison.Ordinal)) value = cmd.Split(' ')[^1];
            return Task.FromResult(new ProcessResult(0, value, ""));
        });
        using var recorder = new SessionRecorder(_h.Options, _h.Journal, _h.Closer, keeper, _h.Tracks, _h.Screen, _h.Disk,
            _h.Sink, _h.PointRecordings, _h.Recovery, TimeProvider.System, null,
            s => Task.FromResult(_h.Connected.Contains(s)));

        await recorder.StartAsync(SessionHarness.CaseId, _h.Start());
        value = "7"; // lo que pone scrcpy
        _h.Tracks.FailNextRotate = true;
        Assert.Null(await recorder.RotateNowAsync());
        Assert.DoesNotContain(calls, c => c.StartsWith("settings put", StringComparison.Ordinal));
        Assert.Equal("recording", recorder.ActiveState!.State);
        Assert.Equal(1, recorder.ActiveState.ScreenSegmentIndex);

        // Un corte bueno: el saliente restauró el 0 con el nuevo grabando → se reafirma el 7.
        value = "0";
        Assert.NotNull(await recorder.RotateNowAsync());
        Assert.Equal("7", value);
        // Al terminar se restaura el original.
        await recorder.StopAsync(SessionHarness.CaseId, "cambio_de_paso", null);
        Assert.Equal("0", value);
        Assert.False(_h.Doc().StayAwake!.Owned);
    }
}
