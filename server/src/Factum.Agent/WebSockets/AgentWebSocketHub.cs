using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Factum.Agent.Models;

namespace Factum.Agent.WebSockets;

/// <summary>
/// Emisor de eventos al WebSocket (ios-grabacion-larga §5.3). Lo implementa <see cref="AgentWebSocketHub"/>;
/// existe para que el post-proceso se pueda probar con un sink falso.
/// </summary>
public interface IAgentEventSink
{
    /// <summary>
    /// Encola <paramref name="evt"/> para todos los clientes conectados (tatana-ws-y-hosts §4.1). Que la
    /// <see cref="Task"/> complete significa <b>encolado</b>, no <b>entregado</b>: nunca espera a un cliente
    /// lento ni propaga errores de envío. <paramref name="ct"/> se ignora.
    /// </summary>
    Task BroadcastAsync(AgentEvent evt, CancellationToken ct = default);
}

/// <summary>
/// Hub de WebSockets de Tatana (tatana-ws-y-hosts §4.1). Cada cliente tiene una cola acotada y un único
/// enviador (<see cref="SendLoopAsync"/>), que es el ÚNICO que llama a <c>SendAsync</c>/<c>CloseOutputAsync</c>
/// sobre su socket. Un cliente que no lee se descarta (<c>Abort()</c>) cuando su cola se llena o un envío
/// supera el timeout, sin trabar a nadie más.
/// </summary>
public sealed class AgentWebSocketHub : IAgentEventSink, IDisposable
{
    /// <summary>Mensajes en cola por cliente.</summary>
    internal const int DefaultQueueCapacity = 256;
    /// <summary>Tope de cada envío individual.</summary>
    internal static readonly TimeSpan DefaultSendTimeout = TimeSpan.FromSeconds(10);
    /// <summary>Intervalo del <c>{"type":"ping"}</c>.</summary>
    internal static readonly TimeSpan DefaultPingInterval = TimeSpan.FromSeconds(25);
    /// <summary>Espera al enviador al desconectarse o al apagar.</summary>
    internal static readonly TimeSpan DefaultCloseTimeout = TimeSpan.FromSeconds(1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static readonly ReadOnlyMemory<byte> PingPayload = Encoding.UTF8.GetBytes("{\"type\":\"ping\"}");

    private readonly ConcurrentDictionary<Guid, Client> _clients = new();
    private readonly ILogger _log;
    private readonly int _queueCapacity;
    private readonly TimeSpan _sendTimeout;
    private readonly TimeSpan _pingInterval;
    private readonly TimeSpan _closeTimeout;
    private readonly CancellationTokenSource _stopping = new();
    private volatile bool _closing;

    public AgentWebSocketHub(ILogger<AgentWebSocketHub> log)
        : this(log, DefaultQueueCapacity, DefaultSendTimeout, DefaultPingInterval, DefaultCloseTimeout)
    {
    }

    internal AgentWebSocketHub(ILogger log, int queueCapacity, TimeSpan sendTimeout, TimeSpan pingInterval,
        TimeSpan closeTimeout)
    {
        _log = log;
        _queueCapacity = queueCapacity;
        _sendTimeout = sendTimeout;
        _pingInterval = pingInterval;
        _closeTimeout = closeTimeout;
    }

    /// <summary>Clientes conectados (para tests).</summary>
    internal int ClientCount => _clients.Count;

    // ── Tipos internos ──────────────────────────────────────────────────────

    private abstract record Outgoing;
    private sealed record PayloadMessage(ReadOnlyMemory<byte> Payload) : Outgoing;
    private sealed record CloseMessage(WebSocketCloseStatus Status, string Reason) : Outgoing;

    private sealed class Client(Guid id, WebSocket ws, Channel<Outgoing> queue, CancellationTokenSource cts)
    {
        public Guid Id { get; } = id;
        public WebSocket Ws { get; } = ws;
        public Channel<Outgoing> Queue { get; } = queue;
        public CancellationTokenSource Cts { get; } = cts;
        public Task Sender { get; set; } = Task.CompletedTask;
        public int Dropped;
    }

    // ── Conexión ────────────────────────────────────────────────────────────

    public async Task HandleAsync(WebSocket ws, CancellationToken requestAborted)
    {
        if (_closing)
        {
            try
            {
                using var cts = new CancellationTokenSource(_closeTimeout);
                await ws.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "going away", cts.Token);
            }
            catch { /* el cliente ya se fue o no leyó el close: no importa */ }
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, _stopping.Token);
        var client = new Client(Guid.NewGuid(), ws,
            Channel.CreateBounded<Outgoing>(new BoundedChannelOptions(_queueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            }),
            new CancellationTokenSource());
        client.Sender = Task.Run(() => SendLoopAsync(client));
        _clients.TryAdd(client.Id, client);
        _log.LogInformation("WS client connected: {Id}", client.Id);

        // Ping por la cola: el enviador es el único que escribe en el socket.
        var pingTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(_pingInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(linked.Token))
                {
                    if (Volatile.Read(ref client.Dropped) != 0) break;
                    if (!client.Queue.Writer.TryWrite(new PayloadMessage(PingPayload)))
                    {
                        if (Volatile.Read(ref client.Dropped) == 0 && !_closing) Drop(client, "cola llena");
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { }
        });

        var buffer = new byte[1024];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, linked.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // Completa el handshake de cierre (por la cola, detrás de lo pendiente).
                    client.Queue.Writer.TryWrite(new CloseMessage(WebSocketCloseStatus.NormalClosure, ""));
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex) { _log.LogDebug("WS cerrado: {Msg}", ex.Message); }
        catch (ObjectDisposedException) { }
        finally
        {
            _clients.TryRemove(client.Id, out _);
            client.Queue.Writer.TryComplete();
            try
            {
                await client.Sender.WaitAsync(_closeTimeout);
            }
            catch (TimeoutException)
            {
                Drop(client, "cerrado");
            }
            catch { /* el enviador ya registró su motivo */ }
            try { linked.Cancel(); } catch (ObjectDisposedException) { }
            try { await pingTask; } catch { /* nada que hacer */ }
            _log.LogInformation("WS client disconnected: {Id}", client.Id);
        }
    }

    // ── Emisión ─────────────────────────────────────────────────────────────

    public Task BroadcastAsync(AgentEvent evt, CancellationToken ct = default)
    {
        if (_closing) return Task.CompletedTask;
        byte[] payload;
        try
        {
            payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(evt, JsonOpts));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "No se pudo serializar el evento WS {Type}", evt.Type);
            return Task.CompletedTask;
        }

        var message = new PayloadMessage(payload);
        foreach (var (_, client) in _clients)
        {
            if (client.Ws.State != WebSocketState.Open)
            {
                Drop(client, "cerrado");
                continue;
            }
            if (!client.Queue.Writer.TryWrite(message))
            {
                // TryWrite también da false si la cola ya se completó (cliente que se iba o apagado):
                // eso no es "cola llena".
                if (Volatile.Read(ref client.Dropped) == 0 && !_closing && _clients.ContainsKey(client.Id))
                    Drop(client, "cola llena");
            }
        }
        return Task.CompletedTask;
    }

    // ── Enviador (uno por cliente) ──────────────────────────────────────────

    private async Task SendLoopAsync(Client client)
    {
        try
        {
            await foreach (var item in client.Queue.Reader.ReadAllAsync(client.Cts.Token))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(client.Cts.Token);
                timeout.CancelAfter(_sendTimeout);
                try
                {
                    switch (item)
                    {
                        case CloseMessage close:
                            if (client.Ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                                await client.Ws.CloseOutputAsync(close.Status, close.Reason, timeout.Token);
                            return;
                        case PayloadMessage msg:
                            await client.Ws.SendAsync(msg.Payload, WebSocketMessageType.Text, true, timeout.Token);
                            break;
                    }
                }
                catch (OperationCanceledException) when (!client.Cts.IsCancellationRequested)
                {
                    Drop(client, "timeout de envío");
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (client.Cts.IsCancellationRequested)
        {
            // Descartado o apagado: salir en silencio.
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or IOException
                                       or InvalidOperationException)
        {
            if (Volatile.Read(ref client.Dropped) == 0) Drop(client, "error de envío");
        }
    }

    /// <summary>Descarta al cliente (idempotente): fuera de la lista, cola completa, cancelado y <c>Abort()</c>.</summary>
    private void Drop(Client client, string motivo)
    {
        if (Interlocked.Exchange(ref client.Dropped, 1) != 0) return;
        _clients.TryRemove(client.Id, out _);
        client.Queue.Writer.TryComplete();
        try { client.Cts.Cancel(); } catch (ObjectDisposedException) { }
        try { client.Ws.Abort(); } catch { /* ya cerrado o dispuesto */ }
        if (motivo == "cerrado")
            _log.LogDebug("WS {Id} desconectado: {Motivo}", client.Id, motivo);
        else
            _log.LogWarning("WS {Id} desconectado: {Motivo}", client.Id, motivo);
    }

    // ── Apagado ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Apagado de Tatana (§4.1/§4.2): close 1001 "going away" a cada cliente, DETRÁS de los eventos ya
    /// encolados; espera a los enviadores hasta <paramref name="timeout"/>, descarta a los que no
    /// terminaron y corta los loops de lectura.
    /// </summary>
    public async Task ShutdownAsync(TimeSpan timeout)
    {
        _closing = true;
        var clients = _clients.Values.ToList();
        foreach (var client in clients)
        {
            if (!client.Queue.Writer.TryWrite(new CloseMessage(WebSocketCloseStatus.EndpointUnavailable, "going away")))
                Drop(client, "apagado");
            client.Queue.Writer.TryComplete();
        }
        try
        {
            await Task.WhenAll(clients.Select(c => c.Sender)).WaitAsync(timeout);
        }
        catch (TimeoutException) { }
        catch { /* los enviadores no tiran, pero por las dudas */ }
        foreach (var client in clients)
            if (!client.Sender.IsCompleted)
                Drop(client, "apagado");
        try { _stopping.Cancel(); } catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        _closing = true;
        try { _stopping.Cancel(); } catch (ObjectDisposedException) { }
        foreach (var (_, client) in _clients)
        {
            try { client.Ws.Abort(); } catch { }
            try { client.Ws.Dispose(); } catch { }
        }
        _clients.Clear();
        _stopping.Dispose();
    }
}
