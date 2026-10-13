using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using Factum.Agent.Models;
using Factum.Agent.WebSockets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Factum.Agent.Tests;

/// <summary>Hub WS con cola acotada por cliente (tatana-ws-y-hosts §4.1, T-01/T-02).</summary>
public sealed class AgentWebSocketHubTests
{
    /// <summary>
    /// WebSocket falso: registra lo enviado, puede tener <c>SendAsync</c> trabado y devuelve <c>Close</c> en
    /// <c>ReceiveAsync</c> cuando se le pide. Falla si hay más de un envío en vuelo a la vez.
    /// </summary>
    private sealed class FakeWebSocket : WebSocket
    {
        private readonly TaskCompletionSource<WebSocketReceiveResult> _receive =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private WebSocketState _state = WebSocketState.Open;
        private int _inFlight;

        public bool Stalled { get; init; }
        public TimeSpan SendDelay { get; init; }
        public ConcurrentQueue<string> Sent { get; } = new();
        public WebSocketCloseStatus? CloseSentStatus { get; private set; }
        public int MaxInFlight { get; private set; }
        public bool Aborted { get; private set; }
        public int SendsAfterClose { get; private set; }

        public void ClientCloses() =>
            _receive.TrySetResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true,
                WebSocketCloseStatus.NormalClosure, ""));

        public override WebSocketCloseStatus? CloseStatus => CloseSentStatus;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort()
        {
            Aborted = true;
            _state = WebSocketState.Aborted;
            _receive.TrySetException(new WebSocketException("aborted"));
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken) => CloseOutputAsync(closeStatus, statusDescription, cancellationToken);

        public override async Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken);
            try
            {
                CloseSentStatus = closeStatus;
                Sent.Enqueue("close:" + (int)closeStatus);
                _state = _state == WebSocketState.CloseReceived ? WebSocketState.Closed : WebSocketState.CloseSent;
            }
            finally { Interlocked.Decrement(ref _inFlight); }
        }

        public override void Dispose() { }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            var r = await _receive.Task.WaitAsync(cancellationToken);
            if (r.MessageType == WebSocketMessageType.Close && _state == WebSocketState.Open)
                _state = WebSocketState.CloseReceived;
            return r;
        }

        public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType,
            bool endOfMessage, CancellationToken cancellationToken)
        {
            if (CloseSentStatus is not null) SendsAfterClose++;
            await EnterAsync(cancellationToken);
            try
            {
                Sent.Enqueue(Encoding.UTF8.GetString(buffer));
            }
            finally { Interlocked.Decrement(ref _inFlight); }
        }

        private async Task EnterAsync(CancellationToken ct)
        {
            var n = Interlocked.Increment(ref _inFlight);
            if (n > MaxInFlight) MaxInFlight = n;
            try
            {
                if (Stalled) await Task.Delay(Timeout.Infinite, ct);
                else if (SendDelay > TimeSpan.Zero) await Task.Delay(SendDelay, ct);
                else await Task.Yield();
            }
            catch
            {
                Interlocked.Decrement(ref _inFlight);
                throw;
            }
        }

        public List<string> Events() => Sent.Where(s => s.Contains("\"type\":\"evt\"")).ToList();
    }

    private static AgentWebSocketHub Hub(int queue = 256, TimeSpan? sendTimeout = null, TimeSpan? ping = null,
        TimeSpan? close = null) =>
        new(NullLogger.Instance, queue, sendTimeout ?? TimeSpan.FromSeconds(10), ping ?? TimeSpan.FromHours(1),
            close ?? TimeSpan.FromSeconds(1));

    private static AgentEvent Evt(int i) => new() { Type = "evt", Data = new { i } };

    private static async Task WaitUntil(Func<bool> cond, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!cond())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("condición no cumplida");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Broadcast_NoEspera_AUnClienteTrabado()
    {
        using var hub = Hub();
        var stalled = new FakeWebSocket { Stalled = true };
        var handle = hub.HandleAsync(stalled, CancellationToken.None);
        await WaitUntil(() => hub.ClientCount == 1);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 10; i++) await hub.BroadcastAsync(Evt(i));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 50, $"tardó {sw.ElapsedMilliseconds} ms");
        await hub.ShutdownAsync(TimeSpan.FromMilliseconds(200));
        await handle.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ColaLlena_DescartaAlTrabado_YElSanoRecibeTodoEnOrden()
    {
        using var hub = Hub(queue: 4);
        var stalled = new FakeWebSocket { Stalled = true };
        var healthy = new FakeWebSocket();
        var h1 = hub.HandleAsync(stalled, CancellationToken.None);
        var h2 = hub.HandleAsync(healthy, CancellationToken.None);
        await WaitUntil(() => hub.ClientCount == 2);

        const int n = 200;
        for (var i = 0; i < n; i++)
        {
            await hub.BroadcastAsync(Evt(i));
            // El sano lee: se le da tiempo a drenar para que su cola de 4 nunca se llene.
            await WaitUntil(() => healthy.Events().Count == i + 1);
        }

        Assert.True(stalled.Aborted);
        Assert.Equal(1, hub.ClientCount);
        await h1.WaitAsync(TimeSpan.FromSeconds(5)); // el trabado terminó su HandleAsync
        var received = healthy.Events();
        Assert.Equal(Enumerable.Range(0, n).Select(i => $"\"i\":{i}}}"), received.Select(s => s[s.IndexOf("\"i\":", StringComparison.Ordinal)..^1]));

        healthy.ClientCloses();
        await h2.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(WebSocketCloseStatus.NormalClosure, healthy.CloseSentStatus);
        Assert.Equal(1, healthy.MaxInFlight);
    }

    [Fact]
    public async Task TimeoutDeEnvio_DescartaAlCliente()
    {
        using var hub = Hub(sendTimeout: TimeSpan.FromMilliseconds(100));
        var stalled = new FakeWebSocket { Stalled = true };
        var handle = hub.HandleAsync(stalled, CancellationToken.None);
        await WaitUntil(() => hub.ClientCount == 1);

        await hub.BroadcastAsync(Evt(1));
        await WaitUntil(() => stalled.Aborted, 2000);
        Assert.Equal(0, hub.ClientCount);
        await handle.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Shutdown_CloseDespuesDeLosEventos_YNoEsperaAlTrabado()
    {
        using var hub = Hub(close: TimeSpan.FromMilliseconds(500));
        var healthy = new FakeWebSocket { SendDelay = TimeSpan.FromMilliseconds(5) };
        var stalled = new FakeWebSocket { Stalled = true };
        var h1 = hub.HandleAsync(healthy, CancellationToken.None);
        var h2 = hub.HandleAsync(stalled, CancellationToken.None);
        await WaitUntil(() => hub.ClientCount == 2);

        for (var i = 0; i < 5; i++) await hub.BroadcastAsync(Evt(i));

        var sw = Stopwatch.StartNew();
        await hub.ShutdownAsync(TimeSpan.FromMilliseconds(500));
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 1000, $"tardó {sw.ElapsedMilliseconds} ms");

        var sent = healthy.Sent.ToList();
        Assert.Equal(6, sent.Count);
        Assert.Equal("close:1001", sent[^1]);
        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, healthy.CloseSentStatus);
        Assert.True(stalled.Aborted);

        await Task.WhenAll(h1, h2).WaitAsync(TimeSpan.FromSeconds(5));

        // Después del apagado, los broadcast se ignoran y una conexión nueva recibe el close sin registrarse.
        await hub.BroadcastAsync(Evt(99));
        Assert.Equal(6, healthy.Sent.Count);
        var late = new FakeWebSocket();
        await hub.HandleAsync(late, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, late.CloseSentStatus);
        Assert.Equal(0, hub.ClientCount);
    }

    [Fact]
    public async Task NuncaHayDosEnviosConcurrentesSobreElMismoSocket()
    {
        using var hub = Hub(ping: TimeSpan.FromMilliseconds(5));
        var ws = new FakeWebSocket { SendDelay = TimeSpan.FromMilliseconds(1) };
        var handle = hub.HandleAsync(ws, CancellationToken.None);
        await WaitUntil(() => hub.ClientCount == 1);

        // Varios emisores a la vez + el ping cada 5 ms.
        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(async () =>
        {
            for (var i = 0; i < 20; i++)
            {
                await hub.BroadcastAsync(Evt(t * 100 + i));
                await Task.Delay(1);
            }
        })));
        await WaitUntil(() => ws.Events().Count == 160);
        ws.ClientCloses();
        await handle.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, ws.MaxInFlight);
        Assert.Contains(ws.Sent, s => s == "{\"type\":\"ping\"}");
        Assert.Equal(0, ws.SendsAfterClose);
    }

    [Fact]
    public async Task RequestAbortado_TerminaHandle()
    {
        using var hub = Hub();
        var ws = new FakeWebSocket();
        using var cts = new CancellationTokenSource();
        var handle = hub.HandleAsync(ws, cts.Token);
        await WaitUntil(() => hub.ClientCount == 1);
        cts.Cancel();
        await handle.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, hub.ClientCount);
    }
}
